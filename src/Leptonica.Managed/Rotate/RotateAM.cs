using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  rotateam.c: Grayscale and color rotation by area mapping (== interpolation).
 *
 *  Rotation about the image center
 *      pixRotateAM, pixRotateAMColor, pixRotateAMGray
 *      (static) rotateAMColorLow, rotateAMGrayLow
 *  Rotation about the UL corner of the image
 *      pixRotateAMCorner, pixRotateAMColorCorner, pixRotateAMGrayCorner
 *      (static) rotateAMColorCornerLow, rotateAMGrayCornerLow
 *  Faster color rotation about the image center
 *      pixRotateAMColorFast
 *      (static) rotateAMColorFastLow
 *
 *  Rotations are measured in radians; clockwise is positive.
 *
 *  Area mapping works as follows.  For each dest pixel you find the 4 source
 *  pixels that it partially covers.  You then compute the dest pixel value as
 *  the area-weighted average of those 4 source pixels.  Two simplifying
 *  approximations are made: the areas are computed as if the dest pixel were
 *  translated but not rotated, and the area overlaps are computed on a
 *  discrete 16x16 sub-pixel grid.
 *
 *  Differences from C:
 *    - The C pointers into the image data (lines, lined, datas, datad) are
 *      word offsets into Pix.Data.
 *    - In the 16-subpixel weights the C code multiplies int by an unsigned
 *      value, so the sums are unsigned; the weights are never negative, so
 *      the int arithmetic used here gives the same results.
 *    - sin() and cos() are the double-precision functions applied to a float
 *      angle, with the product rounded to float, as in C.
 *    - The unreachable default branch of the switch in rotateAMColorFastLow
 *      only logs.
 *
 *  Not ported: nothing.
 */
public sealed partial class Pix
{
    /* rotate.c has its own MinAngleToRotate in the same class; this is the rotateam.c copy */
    private const float MinAngleToRotateAM = 0.001f;  /* radians; ~0.06 deg */

    /*------------------------------------------------------------------*
     *                     Rotation about the center                    *
     *------------------------------------------------------------------*/
    // leptonica: pixRotateAM (rotateam.c)
    /// <summary>Rotation about the image center by area mapping (2, 4, 8 bpp gray or colormapped, or 32 bpp rgb).</summary>
    /// <param name="angle">radians; clockwise is positive</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <remarks>
    /// (1) Rotates about image center.
    /// (2) A positive angle gives a clockwise rotation.
    /// (3) Brings in either black or white pixels from the boundary.
    /// </remarks>
    public Pix RotateAM(float angle, int incolor)
    {
        if (Depth == 1)
            throw Lept.Arg("pixRotateAM", "pixs is 1 bpp");

        if (Math.Abs(angle) < MinAngleToRotateAM)
            return Clone(this);

            /* Remove cmap if it exists, and unpack to 8 bpp if necessary */
        Pix pixt1 = RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        int d = pixt1.Depth;
        Pix pixt2;
        if (d < 8)
            pixt2 = pixt1.ConvertTo8(false);
        else
            pixt2 = Clone(pixt1);
        d = pixt2.Depth;

        if (pixt2.Spp != 4 &&
            LeptGpu.TryGetActive((long)pixt2.Width * pixt2.Height, d == 32 ? 20 : 12, out IGpuAccelerator? gpu) &&
            gpu.TryRotateAM(pixt2, angle, incolor, out Pix? gpuPix)) {
            return gpuPix;
        }

            /* Compute actual incoming color */
        uint fillval = 0;
        if (incolor == L.L_BRING_IN_WHITE) {
            if (d == 8)
                fillval = 255;
            else  /* d == 32 */
                fillval = 0xffffff00;
        }

        Pix pixd;
        if (d == 8)
            pixd = pixt2.RotateAMGray(angle, (byte)fillval);
        else   /* d == 32 */
            pixd = pixt2.RotateAMColor(angle, fillval);

        return pixd;
    }

    // leptonica: pixRotateAMColor (rotateam.c)
    /// <summary>Rotation of a 32 bpp rgb image about the center by area mapping.</summary>
    /// <param name="angle">radians; clockwise is positive</param>
    /// <param name="colorval">e.g., 0 to bring in BLACK, 0xffffff00 for WHITE</param>
    /// <remarks>
    /// (1) Rotates about image center.
    /// (2) A positive angle gives a clockwise rotation.
    /// (3) Specify the color to be brought in from outside the image.
    /// </remarks>
    public Pix RotateAMColor(float angle, uint colorval)
    {
        if (Depth != 32)
            throw Lept.Arg("pixRotateAMColor", "pixs must be 32 bpp");

        if (Math.Abs(angle) < MinAngleToRotateAM)
            return Clone(this);

        GetDimensions(out int w, out int h, out _);
        uint[] datas = Data;
        int wpls = Wpl;
        Pix pixd = CreateTemplateNoInit(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        RotateAMColorLow(datad, w, h, wpld, datas, wpls, angle, colorval);
        if (Spp == 4) {
            Pix pix1 = GetRGBComponent(L.L_ALPHA_CHANNEL);
            Pix pix2 = pix1.RotateAMGray(angle, 255);  /* bring in opaque */
            pixd.SetRGBComponent(pix2, L.L_ALPHA_CHANNEL);
        }

        return pixd;
    }

    // leptonica: pixRotateAMGray (rotateam.c)
    /// <summary>Rotation of an 8 bpp image about the center by area mapping.</summary>
    /// <param name="angle">radians; clockwise is positive</param>
    /// <param name="grayval">0 to bring in BLACK, 255 for WHITE</param>
    /// <remarks>
    /// (1) Rotates about image center.
    /// (2) A positive angle gives a clockwise rotation.
    /// (3) Specify the grayvalue to be brought in from outside the image.
    /// </remarks>
    public Pix RotateAMGray(float angle, byte grayval)
    {
        if (Depth != 8)
            throw Lept.Arg("pixRotateAMGray", "pixs must be 8 bpp");

        if (Math.Abs(angle) < MinAngleToRotateAM)
            return Clone(this);

        GetDimensions(out int w, out int h, out _);
        uint[] datas = Data;
        int wpls = Wpl;
        Pix pixd = CreateTemplateNoInit(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        RotateAMGrayLow(datad, w, h, wpld, datas, wpls, angle, grayval);

        return pixd;
    }

    // leptonica: rotateAMColorLow (rotateam.c)
    private static void RotateAMColorLow(uint[] datad, int w, int h, int wpld,
                                         uint[] datas, int wpls, float angle,
                                         uint colorval)
    {
        int xcen = w / 2;
        int wm2 = w - 2;
        int ycen = h / 2;
        int hm2 = h - 2;
        float sina = (float)(16.0 * Math.Sin(angle));
        float cosa = (float)(16.0 * Math.Cos(angle));

        /* Per-column parts of the float expressions (identical to the per-pixel ones) */
        var xpmBase = new float[w];
        var ypmBase = new float[w];
        for (int x = 0; x < w; x++) {
            int xdif = xcen - x;
            xpmBase[x] = -xdif * cosa;
            ypmBase[x] = xdif * sina;
        }
        int w4 = w & ~3;

        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                int ydif = ycen - i;
                float ydifSina = ydif * sina;
                float negYdifCosa = -ydif * cosa;
                int lined = i * wpld;
                int j = 0;
                if (Vector128.IsHardwareAccelerated) {
                    Vector128<float> vYs = Vector128.Create(ydifSina);
                    Vector128<float> vYc = Vector128.Create(negYdifCosa);
                    ref float rxb = ref MemoryMarshal.GetArrayDataReference(xpmBase);
                    ref float ryb = ref MemoryMarshal.GetArrayDataReference(ypmBase);
                    for (; j < w4; j += 4) {
                        Vector128<int> vx = Vector128.ConvertToInt32(Vector128.LoadUnsafe(ref rxb, (nuint)j) - vYs);
                        Vector128<int> vy = Vector128.ConvertToInt32(vYc + Vector128.LoadUnsafe(ref ryb, (nuint)j));
                        Unsafe.Add(ref datadRef, lined + j) = RotateAMColorPix(ref datasRef, wpls, xcen, ycen, wm2, hm2, vx.GetElement(0), vy.GetElement(0), colorval);
                        Unsafe.Add(ref datadRef, lined + j + 1) = RotateAMColorPix(ref datasRef, wpls, xcen, ycen, wm2, hm2, vx.GetElement(1), vy.GetElement(1), colorval);
                        Unsafe.Add(ref datadRef, lined + j + 2) = RotateAMColorPix(ref datasRef, wpls, xcen, ycen, wm2, hm2, vx.GetElement(2), vy.GetElement(2), colorval);
                        Unsafe.Add(ref datadRef, lined + j + 3) = RotateAMColorPix(ref datasRef, wpls, xcen, ycen, wm2, hm2, vx.GetElement(3), vy.GetElement(3), colorval);
                    }
                }
                for (; j < w; j++) {
                    int xpm = (int)(xpmBase[j] - ydifSina);
                    int ypm = (int)(negYdifCosa + ypmBase[j]);
                    Unsafe.Add(ref datadRef, lined + j) = RotateAMColorPix(ref datasRef, wpls, xcen, ycen, wm2, hm2, xpm, ypm, colorval);
                }
            }
        });
    }

    // One dest pixel from the interpolation coordinates (1/16 pixel units).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint RotateAMColorPix(ref uint datasRef, int wpls, int xcen, int ycen,
                                         int wm2, int hm2, int xpm, int ypm, uint colorval)
    {
        int xp = xcen + (xpm >> 4);
        int yp = ycen + (ypm >> 4);
        if (xp < 0 || yp < 0 || xp > wm2 || yp > hm2)
            return colorval;

        int xf = xpm & 0x0f;
        int yf = ypm & 0x0f;
        int wx0 = 16 - xf;
        int wy0 = 16 - yf;
        uint w00 = (uint)(wx0 * wy0);
        uint w10 = (uint)(xf * wy0);
        uint w01 = (uint)(wx0 * yf);
        uint w11 = (uint)(xf * yf);

        int p0 = yp * wpls + xp;
        uint word00 = Unsafe.Add(ref datasRef, p0);
        uint word10 = Unsafe.Add(ref datasRef, p0 + 1);
        uint word01 = Unsafe.Add(ref datasRef, p0 + wpls);
        uint word11 = Unsafe.Add(ref datasRef, p0 + wpls + 1);

        uint rb = (w00 * ((word00 >> 8) & 0x00ff00ffu) +
                   w10 * ((word10 >> 8) & 0x00ff00ffu) +
                   w01 * ((word01 >> 8) & 0x00ff00ffu) +
                   w11 * ((word11 >> 8) & 0x00ff00ffu) + 0x00800080u) & 0xff00ff00u;
        uint g = ((w00 * ((word00 >> 16) & 0xffu) +
                   w10 * ((word10 >> 16) & 0xffu) +
                   w01 * ((word01 >> 16) & 0xffu) +
                   w11 * ((word11 >> 16) & 0xffu) + 128u) << 8) & 0x00ff0000u;
        return rb | g;
    }

    // leptonica: rotateAMGrayLow (rotateam.c)
    private static void RotateAMGrayLow(uint[] datad, int w, int h, int wpld,
                                        uint[] datas, int wpls, float angle,
                                        byte grayval)
    {
        int xcen = w / 2;
        int wm2 = w - 2;
        int ycen = h / 2;
        int hm2 = h - 2;
        float sina = (float)(16.0 * Math.Sin(angle));
        float cosa = (float)(16.0 * Math.Cos(angle));
        int w4 = w & ~3;
        var xpmBase = new float[w];
        var ypmBase = new float[w];
        for (int x = 0; x < w; x++) {
            int xdif = xcen - x;
            xpmBase[x] = -xdif * cosa;
            ypmBase[x] = xdif * sina;
        }

        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                int ydif = ycen - i;
                float ydifSina = ydif * sina;
                float negYdifCosa = -ydif * cosa;
                int lined = i * wpld;
                int j = 0;
                if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
                    /* The float steps for 4 pixels at a time: the same subtraction/addition
                     * and truncation as the scalar expressions. */
                    Vector128<float> vYs = Vector128.Create(ydifSina);
                    Vector128<float> vYc = Vector128.Create(negYdifCosa);
                    ref float rxb = ref MemoryMarshal.GetArrayDataReference(xpmBase);
                    ref float ryb = ref MemoryMarshal.GetArrayDataReference(ypmBase);
                    for (; j < w4; j += 4) {
                        Vector128<int> vx = Vector128.ConvertToInt32(Vector128.LoadUnsafe(ref rxb, (nuint)j) - vYs);
                        Vector128<int> vy = Vector128.ConvertToInt32(vYc + Vector128.LoadUnsafe(ref ryb, (nuint)j));
                        uint b0 = RotateAMGrayPix(ref datasRef, wpls, xcen, ycen, wm2, hm2, vx.GetElement(0), vy.GetElement(0), grayval);
                        uint b1 = RotateAMGrayPix(ref datasRef, wpls, xcen, ycen, wm2, hm2, vx.GetElement(1), vy.GetElement(1), grayval);
                        uint b2 = RotateAMGrayPix(ref datasRef, wpls, xcen, ycen, wm2, hm2, vx.GetElement(2), vy.GetElement(2), grayval);
                        uint b3 = RotateAMGrayPix(ref datasRef, wpls, xcen, ycen, wm2, hm2, vx.GetElement(3), vy.GetElement(3), grayval);
                        Unsafe.Add(ref datadRef, lined + (j >> 2)) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                    }
                }
                for (; j < w4; j += 4) {
                    uint b0 = RotateAMGraySample(ref datasRef, wpls, xcen, ycen, wm2, hm2, xpmBase, ypmBase, ydifSina, negYdifCosa, j, grayval);
                    uint b1 = RotateAMGraySample(ref datasRef, wpls, xcen, ycen, wm2, hm2, xpmBase, ypmBase, ydifSina, negYdifCosa, j + 1, grayval);
                    uint b2 = RotateAMGraySample(ref datasRef, wpls, xcen, ycen, wm2, hm2, xpmBase, ypmBase, ydifSina, negYdifCosa, j + 2, grayval);
                    uint b3 = RotateAMGraySample(ref datasRef, wpls, xcen, ycen, wm2, hm2, xpmBase, ypmBase, ydifSina, negYdifCosa, j + 3, grayval);
                    Unsafe.Add(ref datadRef, lined + (j >> 2)) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                }
                if (j < w) {
                    ref uint dstLineRef = ref Unsafe.Add(ref datadRef, lined);
                    Unsafe.Add(ref dstLineRef, w >> 2) = 0;
                    for (; j < w; j++) {
                        uint b = RotateAMGraySample(ref datasRef, wpls, xcen, ycen, wm2, hm2, xpmBase, ypmBase, ydifSina, negYdifCosa, j, grayval);
                        SetDataByteUnchecked(ref dstLineRef, j, (int)b);
                    }
                }
            }
        });
    }

    // One dest pixel from the interpolation coordinates (1/16 pixel units).
    // Byte n of a line is at memory byte n ^ 3 (little endian only).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint RotateAMGrayPix(ref uint datasRef, int wpls, int xcen, int ycen,
                                        int wm2, int hm2, int xpm, int ypm, byte grayval)
    {
        int xp = xcen + (xpm >> 4);
        int yp = ycen + (ypm >> 4);
        if (xp < 0 || yp < 0 || xp > wm2 || yp > hm2)
            return grayval;

        int xf = xpm & 0x0f;
        int yf = ypm & 0x0f;
        int wx0 = 16 - xf;
        int wy0 = 16 - yf;
        ref byte line0 = ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref datasRef, yp * wpls));
        ref byte line1 = ref Unsafe.Add(ref line0, wpls * 4);
        int i0 = xp ^ 3;
        int i1 = (xp + 1) ^ 3;
        int v00 = wx0 * wy0 * Unsafe.Add(ref line0, i0);
        int v10 = xf * wy0 * Unsafe.Add(ref line0, i1);
        int v01 = wx0 * yf * Unsafe.Add(ref line1, i0);
        int v11 = xf * yf * Unsafe.Add(ref line1, i1);
        return (uint)((v00 + v01 + v10 + v11 + 128) >> 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint RotateAMGraySample(ref uint datasRef, int wpls, int xcen, int ycen,
                                           int wm2, int hm2, float[] xpmBase, float[] ypmBase,
                                           float ydifSina, float negYdifCosa, int j, byte grayval)
    {
        int xpm = (int)(xpmBase[j] - ydifSina);
        int ypm = (int)(negYdifCosa + ypmBase[j]);
        int xp = xcen + (xpm >> 4);
        int yp = ycen + (ypm >> 4);
        if (xp < 0 || yp < 0 || xp > wm2 || yp > hm2)
            return grayval;

        int xf = xpm & 0x0f;
        int yf = ypm & 0x0f;
        int wx0 = 16 - xf;
        int wy0 = 16 - yf;
        ref uint line0Ref = ref Unsafe.Add(ref datasRef, yp * wpls);
        ref uint line1Ref = ref Unsafe.Add(ref line0Ref, wpls);
        int v00 = wx0 * wy0 * GetDataByteUnchecked(ref line0Ref, xp);
        int v10 = xf * wy0 * GetDataByteUnchecked(ref line0Ref, xp + 1);
        int v01 = wx0 * yf * GetDataByteUnchecked(ref line1Ref, xp);
        int v11 = xf * yf * GetDataByteUnchecked(ref line1Ref, xp + 1);
        return (uint)((v00 + v01 + v10 + v11 + 128) >> 8);
    }


    /*------------------------------------------------------------------*
     *                    Rotation about the UL corner                  *
     *------------------------------------------------------------------*/
    // leptonica: pixRotateAMCorner (rotateam.c)
    /// <summary>Rotation about the UL corner of the image by area mapping (1, 2, 4, 8 bpp gray or colormapped, or 32 bpp rgb).</summary>
    /// <param name="angle">radians; clockwise is positive</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <remarks>
    /// (1) Rotates about the UL corner of the image.
    /// (2) A positive angle gives a clockwise rotation.
    /// (3) Brings in either black or white pixels from the boundary.
    /// </remarks>
    public Pix RotateAMCorner(float angle, int incolor)
    {
        if (Math.Abs(angle) < MinAngleToRotateAM)
            return Clone(this);

            /* Remove cmap if it exists, and unpack to 8 bpp if necessary */
        Pix pixt1 = RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        int d = pixt1.Depth;
        Pix pixt2;
        if (d < 8)
            pixt2 = pixt1.ConvertTo8(false);
        else
            pixt2 = Clone(pixt1);
        d = pixt2.Depth;

            /* Compute actual incoming color */
        uint fillval = 0;
        if (incolor == L.L_BRING_IN_WHITE) {
            if (d == 8)
                fillval = 255;
            else  /* d == 32 */
                fillval = 0xffffff00;
        }

        Pix pixd;
        if (d == 8)
            pixd = pixt2.RotateAMGrayCorner(angle, (byte)fillval);
        else   /* d == 32 */
            pixd = pixt2.RotateAMColorCorner(angle, fillval);

        return pixd;
    }

    // leptonica: pixRotateAMColorCorner (rotateam.c)
    /// <summary>Rotation of a 32 bpp rgb image about the UL corner by area mapping.</summary>
    /// <param name="angle">radians; clockwise is positive</param>
    /// <param name="fillval">e.g., 0 to bring in BLACK, 0xffffff00 for WHITE</param>
    /// <remarks>
    /// (1) Rotates the image about the UL corner.
    /// (2) A positive angle gives a clockwise rotation.
    /// (3) Specify the color to be brought in from outside the image.
    /// </remarks>
    public Pix RotateAMColorCorner(float angle, uint fillval)
    {
        if (Depth != 32)
            throw Lept.Arg("pixRotateAMColorCorner", "pixs must be 32 bpp");

        if (Math.Abs(angle) < MinAngleToRotateAM)
            return Clone(this);

        GetDimensions(out int w, out int h, out _);
        uint[] datas = Data;
        int wpls = Wpl;
        Pix pixd = CreateTemplateNoInit(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        RotateAMColorCornerLow(datad, w, h, wpld, datas, wpls, angle, fillval);
        if (Spp == 4) {
            Pix pix1 = GetRGBComponent(L.L_ALPHA_CHANNEL);
            Pix pix2 = pix1.RotateAMGrayCorner(angle, 255);  /* bring in opaque */
            pixd.SetRGBComponent(pix2, L.L_ALPHA_CHANNEL);
        }

        return pixd;
    }

    // leptonica: pixRotateAMGrayCorner (rotateam.c)
    /// <summary>Rotation of an 8 bpp image about the UL corner by area mapping.</summary>
    /// <param name="angle">radians; clockwise is positive</param>
    /// <param name="grayval">0 to bring in BLACK, 255 for WHITE</param>
    /// <remarks>
    /// (1) Rotates the image about the UL corner.
    /// (2) A positive angle gives a clockwise rotation.
    /// (3) Specify the grayvalue to be brought in from outside the image.
    /// </remarks>
    public Pix RotateAMGrayCorner(float angle, byte grayval)
    {
        if (Depth != 8)
            throw Lept.Arg("pixRotateAMGrayCorner", "pixs must be 8 bpp");

        if (Math.Abs(angle) < MinAngleToRotateAM)
            return Clone(this);

        GetDimensions(out int w, out int h, out _);
        uint[] datas = Data;
        int wpls = Wpl;
        Pix pixd = CreateTemplateNoInit(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        RotateAMGrayCornerLow(datad, w, h, wpld, datas, wpls, angle, grayval);

        return pixd;
    }

    // leptonica: rotateAMColorCornerLow (rotateam.c)
    private static void RotateAMColorCornerLow(uint[] datad, int w, int h, int wpld,
                                               uint[] datas, int wpls, float angle,
                                               uint colorval)
    {
        int wm2 = w - 2;
        int hm2 = h - 2;
        float sina = (float)(16.0 * Math.Sin(angle));
        float cosa = (float)(16.0 * Math.Cos(angle));

        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                float iSina = i * sina;
                float iCosa = i * cosa;
                int lined = i * wpld;
                for (int j = 0; j < w; j++) {
                    int xpm = (int)(j * cosa + iSina);
                    int ypm = (int)(iCosa - j * sina);
                    int xp = xpm >> 4;
                    int yp = ypm >> 4;

                    if (xp < 0 || yp < 0 || xp > wm2 || yp > hm2) {
                        Unsafe.Add(ref datadRef, lined + j) = colorval;
                        continue;
                    }

                    int xf = xpm & 0x0f;
                    int yf = ypm & 0x0f;
                    int wx0 = 16 - xf;
                    int wy0 = 16 - yf;
                    uint w00 = (uint)(wx0 * wy0);
                    uint w10 = (uint)(xf * wy0);
                    uint w01 = (uint)(wx0 * yf);
                    uint w11 = (uint)(xf * yf);

                    int p0 = yp * wpls + xp;
                    uint word00 = Unsafe.Add(ref datasRef, p0);
                    uint word10 = Unsafe.Add(ref datasRef, p0 + 1);
                    uint word01 = Unsafe.Add(ref datasRef, p0 + wpls);
                    uint word11 = Unsafe.Add(ref datasRef, p0 + wpls + 1);

                    uint rb = (w00 * ((word00 >> 8) & 0x00ff00ffu) +
                               w10 * ((word10 >> 8) & 0x00ff00ffu) +
                               w01 * ((word01 >> 8) & 0x00ff00ffu) +
                               w11 * ((word11 >> 8) & 0x00ff00ffu) + 0x00800080u) & 0xff00ff00u;
                    uint g = ((w00 * ((word00 >> 16) & 0xffu) +
                               w10 * ((word10 >> 16) & 0xffu) +
                               w01 * ((word01 >> 16) & 0xffu) +
                               w11 * ((word11 >> 16) & 0xffu) + 128u) << 8) & 0x00ff0000u;
                    Unsafe.Add(ref datadRef, lined + j) = rb | g;
                }
            }
        });
    }

    // leptonica: rotateAMGrayCornerLow (rotateam.c)
    private static void RotateAMGrayCornerLow(uint[] datad, int w, int h, int wpld,
                                              uint[] datas, int wpls, float angle,
                                              byte grayval)
    {
        int wm2 = w - 2;
        int hm2 = h - 2;
        float sina = (float)(16.0 * Math.Sin(angle));
        float cosa = (float)(16.0 * Math.Cos(angle));
        int w4 = w & ~3;

        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                float iSina = i * sina;
                float iCosa = i * cosa;
                int lined = i * wpld;
                int j = 0;
                for (; j < w4; j += 4) {
                    uint b0 = RotateAMGrayCornerSample(ref datasRef, wpls, wm2, hm2, cosa, sina, iSina, iCosa, j, grayval);
                    uint b1 = RotateAMGrayCornerSample(ref datasRef, wpls, wm2, hm2, cosa, sina, iSina, iCosa, j + 1, grayval);
                    uint b2 = RotateAMGrayCornerSample(ref datasRef, wpls, wm2, hm2, cosa, sina, iSina, iCosa, j + 2, grayval);
                    uint b3 = RotateAMGrayCornerSample(ref datasRef, wpls, wm2, hm2, cosa, sina, iSina, iCosa, j + 3, grayval);
                    Unsafe.Add(ref datadRef, lined + (j >> 2)) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                }
                if (j < w) {
                    ref uint dstLineRef = ref Unsafe.Add(ref datadRef, lined);
                    Unsafe.Add(ref dstLineRef, w >> 2) = 0;
                    for (; j < w; j++) {
                        uint b = RotateAMGrayCornerSample(ref datasRef, wpls, wm2, hm2, cosa, sina, iSina, iCosa, j, grayval);
                        SetDataByteUnchecked(ref dstLineRef, j, (int)b);
                    }
                }
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint RotateAMGrayCornerSample(ref uint datasRef, int wpls, int wm2, int hm2,
                                                 float cosa, float sina, float iSina, float iCosa,
                                                 int j, byte grayval)
    {
        int xpm = (int)(j * cosa + iSina);
        int ypm = (int)(iCosa - j * sina);
        int xp = xpm >> 4;
        int yp = ypm >> 4;
        if (xp < 0 || yp < 0 || xp > wm2 || yp > hm2)
            return grayval;

        int xf = xpm & 0x0f;
        int yf = ypm & 0x0f;
        int wx0 = 16 - xf;
        int wy0 = 16 - yf;
        ref uint line0Ref = ref Unsafe.Add(ref datasRef, yp * wpls);
        ref uint line1Ref = ref Unsafe.Add(ref line0Ref, wpls);
        int v00 = wx0 * wy0 * GetDataByteUnchecked(ref line0Ref, xp);
        int v10 = xf * wy0 * GetDataByteUnchecked(ref line0Ref, xp + 1);
        int v01 = wx0 * yf * GetDataByteUnchecked(ref line1Ref, xp);
        int v11 = xf * yf * GetDataByteUnchecked(ref line1Ref, xp + 1);
        return (uint)((v00 + v01 + v10 + v11 + 128) >> 8);
    }


    /*------------------------------------------------------------------*
     *               Fast RGB color rotation about center               *
     *------------------------------------------------------------------*/
    // leptonica: pixRotateAMColorFast (rotateam.c)
    /// <summary>Fast approximate rotation of a 32 bpp rgb image about the center by area mapping.</summary>
    /// <param name="angle">radians; clockwise is positive</param>
    /// <param name="colorval">e.g., 0 to bring in BLACK, 0xffffff00 for WHITE</param>
    /// <remarks>
    /// (1) This rotates a color image about the image center.
    /// (2) A positive angle gives a clockwise rotation.
    /// (3) It uses area mapping, dividing each pixel into
    ///     16 subpixels.
    /// (4) It is about 10% to 20% faster than the more accurate linear
    ///     interpolation function pixRotateAMColor(),
    ///     which uses 256 subpixels.
    /// (5) For some reason it shifts the image center.
    ///     No attempt is made to rotate the alpha component.
    /// </remarks>
    public Pix RotateAMColorFast(float angle, uint colorval)
    {
        if (Depth != 32)
            throw Lept.Arg("pixRotateAMColorFast", "pixs must be 32 bpp");

        if (Math.Abs(angle) < MinAngleToRotateAM)
            return Clone(this);

        GetDimensions(out int w, out int h, out _);
        uint[] datas = Data;
        int wpls = Wpl;
        Pix pixd = CreateTemplateNoInit(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        RotateAMColorFastLow(datad, w, h, wpld, datas, wpls, angle, colorval);
        return pixd;
    }

    // leptonica: rotateAMColorFastLow (rotateam.c)
    private static void RotateAMColorFastLow(uint[] datad, int w, int h, int wpld,
                                             uint[] datas, int wpls, float angle,
                                             uint colorval)
    {
        int xcen = w / 2;
        int wm2 = w - 2;
        int ycen = h / 2;
        int hm2 = h - 2;
        float sina = (float)(4.0 * Math.Sin(angle));
        float cosa = (float)(4.0 * Math.Cos(angle));

        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                int ydif = ycen - i;
                float ydifSina = ydif * sina;
                float negYdifCosa = -ydif * cosa;
                int lined = i * wpld;
                for (int j = 0; j < w; j++) {
                    int xdif = xcen - j;
                    int xpm = (int)(-xdif * cosa - ydifSina);
                    int ypm = (int)(negYdifCosa + xdif * sina);
                    int xp = xcen + (xpm >> 2);
                    int yp = ycen + (ypm >> 2);

                    if (xp < 0 || yp < 0 || xp > wm2 || yp > hm2) {
                        Unsafe.Add(ref datadRef, lined + j) = colorval;
                        continue;
                    }

                    int xf = xpm & 0x03;
                    int yf = ypm & 0x03;
                    int pword = yp * wpls + xp;
                    if ((xf | yf) == 0) {
                        Unsafe.Add(ref datadRef, lined + j) = Unsafe.Add(ref datasRef, pword);
                        continue;
                    }

                    int wx0 = 4 - xf;
                    int wy0 = 4 - yf;
                    uint w00 = (uint)(wx0 * wy0);
                    uint w10 = (uint)(xf * wy0);
                    uint w01 = (uint)(wx0 * yf);
                    uint w11 = (uint)(xf * yf);

                    uint word1 = Unsafe.Add(ref datasRef, pword);
                    uint word2 = Unsafe.Add(ref datasRef, pword + 1);
                    uint word3 = Unsafe.Add(ref datasRef, pword + wpls);
                    uint word4 = Unsafe.Add(ref datasRef, pword + wpls + 1);

                    uint rb = ((w00 * ((word1 >> 8) & 0x00ff00ffu) +
                                w10 * ((word2 >> 8) & 0x00ff00ffu) +
                                w01 * ((word3 >> 8) & 0x00ff00ffu) +
                                w11 * ((word4 >> 8) & 0x00ff00ffu)) << 4) & 0xff00ff00u;
                    uint g = ((w00 * ((word1 >> 16) & 0xffu) +
                               w10 * ((word2 >> 16) & 0xffu) +
                               w01 * ((word3 >> 16) & 0xffu) +
                               w11 * ((word4 >> 16) & 0xffu)) << 12) & 0x00ff0000u;
                    Unsafe.Add(ref datadRef, lined + j) = rb | g;
                }
            }
        });
    }
}
