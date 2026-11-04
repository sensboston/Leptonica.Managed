using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  affine.c: Affine transforms.
 *
 *      Affine (3 pt) image transformation using a sampled
 *      (to nearest integer) transform on each dest point
 *      Affine (3 pt) image transformation using interpolation
 *      (or area mapping) for anti-aliasing images that are
 *      2, 4, or 8 bpp gray, or colormapped, or 32 bpp RGB
 *      Affine transform including alpha (blend) component
 *      Affine coordinate transformation
 *      Interpolation helper functions
 *      Gauss-jordan linear equation solver
 *      Affine image transformation using a sequence of
 *      shear/scale/translation operations
 *
 *  An affine transform maps (x, y) to (x', y') with
 *
 *      x' = ax + by + c
 *      y' = dx + ey + f
 *
 *  The 6 coefficients are passed around as a float[6] vector (vc).
 *  All functions of this file are members of Pix, because the C functions
 *  that have no Pix argument (getAffineXformCoeffs(), gaussjordan(), ...)
 *  are static members of the primary class of the file.
 *
 *  Differences from C:
 *    - No reference counting and no destroy calls: the GC owns the objects.
 *    - Functions that return NULL or an error code in C throw instead.
 *    - The coefficient vectors are returned as float[6].
 *    - gaussjordan() returns false on failure (singular matrix).
 *    - The C-only debugging output (DEBUG == 0) is not ported.
 *    - Callees that are not ported yet (pixHShearIP(), pixVShearIP()) are
 *      stubs in their own file.
 */
public sealed partial class Pix
{
    /*-------------------------------------------------------------*
     *               Sampled affine image transformation           *
     *-------------------------------------------------------------*/
    // leptonica: pixAffineSampledPta (affine.c)
    /// <summary>Sampled affine transform given 3 pairs of points.</summary>
    /// <param name="ptad">3 pts of final coordinate space</param>
    /// <param name="ptas">3 pts of initial coordinate space</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>the transformed pix; all depths</returns>
    /// <remarks>
    /// (1) Brings in either black or white pixels from the boundary.
    /// (2) Retains colormap, which you can do for a sampled transform..
    /// (3) The 3 points must not be collinear.
    /// (4) The order of the 3 points is arbitrary; however, to compare
    ///     with the sequential transform they must be in these locations
    ///     and in this order: origin, x-axis, y-axis.
    /// (5) For 1 bpp images, this has much better quality results
    ///     than pixAffineSequential(), particularly for text.
    ///     It is about 3x slower, but does not require additional
    ///     border pixels.  The poor quality of pixAffineSequential()
    ///     is due to repeated quantized transforms.  It is strongly
    ///     recommended that pixAffineSampled() be used for 1 bpp images.
    /// (6) For 8 or 32 bpp, much better quality is obtained by the
    ///     somewhat slower pixAffinePta().  See that function
    ///     for relative timings between sampled and interpolated.
    /// (7) To repeat, use of the sequential transform,
    ///     pixAffineSequential(), for any images, is discouraged.
    /// </remarks>
    public Pix AffineSampledPta(Pta ptad, Pta ptas, int incolor)
    {
        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg("pixAffineSampledPta", "invalid incolor");
        if (ptas.Count != 3)
            throw Lept.Arg("pixAffineSampledPta", "ptas count not 3");
        if (ptad.Count != 3)
            throw Lept.Arg("pixAffineSampledPta", "ptad count not 3");

            /* Get backwards transform from dest to src, and apply it */
        float[] vc = GetAffineXformCoeffs(ptad, ptas);
        return AffineSampled(vc, incolor);
    }

    /// <summary>
    /// (int)(f + 0.5), with the sum formed in double, for 4 floats of magnitude below 2^30.
    /// Values below -1.5 give -1 instead of the exact negative integer; both are outside
    /// any image, which is all the callers test.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> RoundHalfUp(Vector128<float> f)
    {
        Vector128<float> half = Vector128.Create(0.5f);
        Vector128<int> t = Vector128.ConvertToInt32(f);
        Vector128<float> fr = f - Vector128.ConvertToSingle(t);
        Vector128<int> pos = t - Vector128.GreaterThanOrEqual(fr, half).AsInt32();   // f >= 0.5
        Vector128<int> small = Vector128.ConditionalSelect(
            Vector128.GreaterThan(f, Vector128.Create(-1.5f)).AsInt32(), Vector128<int>.Zero, Vector128.Create(-1));
        return Vector128.ConditionalSelect(Vector128.GreaterThanOrEqual(f, half).AsInt32(), pos, small);
    }

    // Sampled 8 bpp pixel; byte n of a line is at memory byte n ^ 3 (little endian only).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AffinePix8(ref uint datasRef, int wpls, int w, int h, int x, int y, byte fill8)
    {
        if ((uint)x < (uint)w && (uint)y < (uint)h)
            return Unsafe.Add(ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref datasRef, y * wpls)), x ^ 3);
        return fill8;
    }

    // leptonica: pixAffineSampled (affine.c)
    /// <summary>Sampled affine transform given the 6 coefficients.</summary>
    /// <param name="vc">vector of 6 coefficients for affine transformation</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>the transformed pix; all depths</returns>
    /// <remarks>
    /// (1) Brings in either black or white pixels from the boundary.
    /// (2) Retains colormap, which you can do for a sampled transform..
    /// (3) For 8 or 32 bpp, much better quality is obtained by the
    ///     somewhat slower pixAffine().  See that function
    ///     for relative timings between sampled and interpolated.
    /// </remarks>
    public Pix AffineSampled(float[] vc, int incolor)
    {
        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg("pixAffineSampled", "invalid incolor");
        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 32)
            throw Lept.Arg("pixAffineSampled", "depth not 1, 2, 4, 8 or 16");

        float vc0 = vc[0], vc1 = vc[1], vc2 = vc[2];
        float vc3 = vc[3], vc4 = vc[4], vc5 = vc[5];

        uint[] datas = Data;
        int wpls = Wpl;
        PixColormap? cmap = Colormap;

        if (cmap == null && Spp != 4 && (d == 1 || d == 8 || d == 32) &&
            LeptGpu.TryGetActive((long)w * h, 8, out IGpuAccelerator? gpuSampled) &&
            gpuSampled.TryAffine(this, vc, incolor, sampled: true, out Pix? gpuSampledPix)) {
            return gpuSampledPix;
        }

        if (d == 32 && cmap == null) {
            Pix pixd32 = CreateTemplateNoInit(this);
            uint fill32 = incolor == L.L_BRING_IN_BLACK ? 0u : 0xffffffffu;
            uint[] datad32 = pixd32.Data;
            int wpld32 = pixd32.Wpl;
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad32);
                for (int i = yStart; i < yEnd; i++) {
                    int lined = i * wpld32;
                    float ry1 = vc1 * i, ry4 = vc4 * i;
                    for (int j = 0; j < w; j++) {
                        int x = (int)(vc0 * j + ry1 + vc2 + 0.5);
                        int y = (int)(vc3 * j + ry4 + vc5 + 0.5);
                        Unsafe.Add(ref datadRef, lined + j) =
                            (uint)x < (uint)w && (uint)y < (uint)h
                                ? Unsafe.Add(ref datasRef, y * wpls + x)
                                : fill32;
                    }
                }
            });
            return pixd32;
        }

        if (d == 8) {
            Pix pixd8 = CreateTemplateNoInit(this);
            byte fill8;
            if (cmap != null) {
                int color = incolor == L.L_BRING_IN_WHITE ? 1 : 0;
                pixd8.Colormap!.AddBlackOrWhite(color, out int cmapindex);
                fill8 = (byte)cmapindex;
            } else {
                fill8 = incolor == L.L_BRING_IN_BLACK ? (byte)0 : (byte)255;
            }
            uint fillWord8 = fill8 * 0x01010101u;
            uint[] datad8 = pixd8.Data;
            int wpld8 = pixd8.Wpl;
            int w4 = w & ~3;

                /* The vector path needs the coordinates to stay far from the int range */
            double bx = Math.Abs((double)vc0) * w + Math.Abs((double)vc1) * h + Math.Abs((double)vc2);
            double by = Math.Abs((double)vc3) * w + Math.Abs((double)vc4) * h + Math.Abs((double)vc5);
            bool simd = Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian &&
                        bx < 1.0e9 && by < 1.0e9;
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad8);
                for (int i = yStart; i < yEnd; i++) {
                    int lined = i * wpld8;
                    float ry1 = vc1 * i, ry4 = vc4 * i;
                    int j = 0;
                    if (simd) {
                        Vector128<float> v0 = Vector128.Create(vc0), v3 = Vector128.Create(vc3);
                        Vector128<float> r1 = Vector128.Create(ry1), r4 = Vector128.Create(ry4);
                        Vector128<float> c2 = Vector128.Create(vc2), c5 = Vector128.Create(vc5);
                        Vector128<float> jv = Vector128.Create(0.0f, 1.0f, 2.0f, 3.0f);
                        Vector128<float> four = Vector128.Create(4.0f);
                        for (; j < w4; j += 4) {
                            Vector128<int> vx = RoundHalfUp((v0 * jv + r1) + c2);
                            Vector128<int> vy = RoundHalfUp((v3 * jv + r4) + c5);
                            jv += four;
                            uint b0 = AffinePix8(ref datasRef, wpls, w, h, vx.GetElement(0), vy.GetElement(0), fill8);
                            uint b1 = AffinePix8(ref datasRef, wpls, w, h, vx.GetElement(1), vy.GetElement(1), fill8);
                            uint b2 = AffinePix8(ref datasRef, wpls, w, h, vx.GetElement(2), vy.GetElement(2), fill8);
                            uint b3 = AffinePix8(ref datasRef, wpls, w, h, vx.GetElement(3), vy.GetElement(3), fill8);
                            Unsafe.Add(ref datadRef, lined + (j >> 2)) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                        }
                    }
                    for (; j < w4; j += 4) {
                        int x0 = (int)(vc0 * j + ry1 + vc2 + 0.5);
                        int y0 = (int)(vc3 * j + ry4 + vc5 + 0.5);
                        uint b0 = (uint)x0 < (uint)w && (uint)y0 < (uint)h
                            ? (uint)GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y0 * wpls), x0) : fill8;

                        int j1 = j + 1;
                        int x1 = (int)(vc0 * j1 + ry1 + vc2 + 0.5);
                        int y1 = (int)(vc3 * j1 + ry4 + vc5 + 0.5);
                        uint b1 = (uint)x1 < (uint)w && (uint)y1 < (uint)h
                            ? (uint)GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y1 * wpls), x1) : fill8;

                        int j2 = j + 2;
                        int x2 = (int)(vc0 * j2 + ry1 + vc2 + 0.5);
                        int y2 = (int)(vc3 * j2 + ry4 + vc5 + 0.5);
                        uint b2 = (uint)x2 < (uint)w && (uint)y2 < (uint)h
                            ? (uint)GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y2 * wpls), x2) : fill8;

                        int j3 = j + 3;
                        int x3 = (int)(vc0 * j3 + ry1 + vc2 + 0.5);
                        int y3 = (int)(vc3 * j3 + ry4 + vc5 + 0.5);
                        uint b3 = (uint)x3 < (uint)w && (uint)y3 < (uint)h
                            ? (uint)GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y3 * wpls), x3) : fill8;

                        Unsafe.Add(ref datadRef, lined + (j >> 2)) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                    }
                    if (j < w) {
                        ref uint dstLineRef = ref Unsafe.Add(ref datadRef, lined);
                        Unsafe.Add(ref dstLineRef, w >> 2) = fillWord8;
                        for (; j < w; j++) {
                            int x = (int)(vc0 * j + ry1 + vc2 + 0.5);
                            int y = (int)(vc3 * j + ry4 + vc5 + 0.5);
                            if ((uint)x < (uint)w && (uint)y < (uint)h)
                                SetDataByteUnchecked(ref dstLineRef, j, GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y * wpls), x));
                        }
                    }
                }
            });
            return pixd8;
        }

            /* Init all dest pixels to color to be brought in from outside */
        Pix pixd = CreateTemplate(this);
        if (cmap != null) {
            int color = incolor == L.L_BRING_IN_WHITE ? 1 : 0;
            pixd.Colormap!.AddBlackOrWhite(color, out int cmapindex);
            pixd.SetAllArbitrary((uint)cmapindex);
        } else {
            if ((d == 1 && incolor == L.L_BRING_IN_WHITE) ||
                (d > 1 && incolor == L.L_BRING_IN_BLACK)) {
                pixd.ClearAll();
            } else {
                pixd.SetAll();
            }
        }

            /* Scan over the dest pixels */
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            for (int i = yStart; i < yEnd; i++) {
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                float ry1 = vc1 * i, ry4 = vc4 * i;
                if (d == 1) {
                    for (int j = 0; j < w; j++) {
                        int x = (int)(vc0 * j + ry1 + vc2 + 0.5);
                        int y = (int)(vc3 * j + ry4 + vc5 + 0.5);
                        if ((uint)x >= (uint)w || (uint)y >= (uint)h) continue;
                        int val = GetDataBit(datas.AsSpan(y * wpls, wpls), x);
                        SetDataBitVal(lined, j, val);
                    }
                } else if (d == 2) {
                    for (int j = 0; j < w; j++) {
                        int x = (int)(vc0 * j + ry1 + vc2 + 0.5);
                        int y = (int)(vc3 * j + ry4 + vc5 + 0.5);
                        if ((uint)x >= (uint)w || (uint)y >= (uint)h) continue;
                        int val = GetDataDibit(datas.AsSpan(y * wpls, wpls), x);
                        SetDataDibit(lined, j, val);
                    }
                } else {
                    for (int j = 0; j < w; j++) {
                        int x = (int)(vc0 * j + ry1 + vc2 + 0.5);
                        int y = (int)(vc3 * j + ry4 + vc5 + 0.5);
                        if ((uint)x >= (uint)w || (uint)y >= (uint)h) continue;
                        int val = GetDataQbit(datas.AsSpan(y * wpls, wpls), x);
                        SetDataQbit(lined, j, val);
                    }
                }
            }
        });

        return pixd;
    }

    /*---------------------------------------------------------------------*
     *               Interpolated affine image transformation              *
     *---------------------------------------------------------------------*/
    // leptonica: pixAffinePta (affine.c)
    /// <summary>Interpolated affine transform given 3 pairs of points.</summary>
    /// <param name="ptad">3 pts of final coordinate space</param>
    /// <param name="ptas">3 pts of initial coordinate space</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>the transformed pix; all depths; colormap ok</returns>
    /// <remarks>
    /// (1) Brings in either black or white pixels from the boundary
    /// (2) Removes any existing colormap, if necessary, before transforming
    /// </remarks>
    public Pix AffinePta(Pta ptad, Pta ptas, int incolor)
    {
        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg("pixAffinePta", "invalid incolor");
        if (ptas.Count != 3)
            throw Lept.Arg("pixAffinePta", "ptas count not 3");
        if (ptad.Count != 3)
            throw Lept.Arg("pixAffinePta", "ptad count not 3");

        if (Depth == 1)
            return AffineSampledPta(ptad, ptas, incolor);

            /* Remove cmap if it exists, and unpack to 8 bpp if necessary */
        Pix pixt1 = RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        int d = pixt1.Depth;
        Pix pixt2;
        if (d < 8)
            pixt2 = pixt1.ConvertTo8(false);
        else
            pixt2 = Clone(pixt1);
        d = pixt2.Depth;

            /* Compute actual color to bring in from edges */
        uint colorval = 0;
        if (incolor == L.L_BRING_IN_WHITE) {
            if (d == 8)
                colorval = 255;
            else  /* d == 32 */
                colorval = 0xffffff00;
        }

        Pix pixd;
        if (d == 8)
            pixd = pixt2.AffinePtaGray(ptad, ptas, (byte)colorval);
        else  /* d == 32 */
            pixd = pixt2.AffinePtaColor(ptad, ptas, colorval);
        return pixd;
    }

    // leptonica: pixAffine (affine.c)
    /// <summary>Interpolated affine transform given the 6 coefficients.</summary>
    /// <param name="vc">vector of 6 coefficients for affine transformation</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>the transformed pix; all depths; colormap ok</returns>
    /// <remarks>
    /// (1) Brings in either black or white pixels from the boundary
    /// (2) Removes any existing colormap, if necessary, before transforming
    /// </remarks>
    public Pix Affine(float[] vc, int incolor)
    {
        if (Depth == 1)
            return AffineSampled(vc, incolor);

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
            gpu.TryAffine(pixt2, vc, incolor, sampled: false, out Pix? gpuPix)) {
            return gpuPix;
        }

            /* Compute actual color to bring in from edges */
        uint colorval = 0;
        if (incolor == L.L_BRING_IN_WHITE) {
            if (d == 8)
                colorval = 255;
            else  /* d == 32 */
                colorval = 0xffffff00;
        }

        Pix pixd;
        if (d == 8)
            pixd = pixt2.AffineGray(vc, (byte)colorval);
        else  /* d == 32 */
            pixd = pixt2.AffineColor(vc, colorval);
        return pixd;
    }

    // leptonica: pixAffinePtaColor (affine.c)
    /// <summary>Interpolated affine transform of a 32 bpp pix, given 3 pairs of points.</summary>
    /// <param name="ptad">3 pts of final coordinate space</param>
    /// <param name="ptas">3 pts of initial coordinate space</param>
    /// <param name="colorval">e.g.: 0 to bring in BLACK, 0xffffff00 for WHITE</param>
    /// <returns>the transformed pix; this must be 32 bpp</returns>
    public Pix AffinePtaColor(Pta ptad, Pta ptas, uint colorval)
    {
        if (Depth != 32)
            throw Lept.Arg("pixAffinePtaColor", "pixs must be 32 bpp");
        if (ptas.Count != 3)
            throw Lept.Arg("pixAffinePtaColor", "ptas count not 3");
        if (ptad.Count != 3)
            throw Lept.Arg("pixAffinePtaColor", "ptad count not 3");

            /* Get backwards transform from dest to src, and apply it */
        float[] vc = GetAffineXformCoeffs(ptad, ptas);
        return AffineColor(vc, colorval);
    }

    // leptonica: pixAffineColor (affine.c)
    /// <summary>Interpolated affine transform of a 32 bpp pix, given the 6 coefficients.</summary>
    /// <param name="vc">vector of 6 coefficients for affine transformation</param>
    /// <param name="colorval">e.g.: 0 to bring in BLACK, 0xffffff00 for WHITE</param>
    /// <returns>the transformed pix; this must be 32 bpp</returns>
    public Pix AffineColor(float[] vc, uint colorval)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 32)
            throw Lept.Arg("pixAffineColor", "pixs must be 32 bpp");

        float vc0 = vc[0], vc1 = vc[1], vc2 = vc[2];
        float vc3 = vc[3], vc4 = vc[4], vc5 = vc[5];

        uint[] datas = Data;
        int wpls = Wpl;
        Pix pixd = CreateTemplateNoInit(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

            /* Iterate over destination pixels */
        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                int lined = i * wpld;
                float ry1 = vc1 * i, ry4 = vc4 * i;
                for (int j = 0; j < w; j++) {
                    float x = vc0 * j + ry1 + vc2;
                    float y = vc3 * j + ry4 + vc5;
                    Unsafe.Add(ref datadRef, lined + j) =
                        LinearInterpolatePixelColorCore(ref datasRef, wpls, w, h, x, y, colorval);
                }
            }
        });

            /* If rgba, transform the pixs alpha channel and insert in pixd */
        if (Spp == 4) {
            Pix pix1 = GetRGBComponent(L.L_ALPHA_CHANNEL);
            Pix pix2 = pix1.AffineGray(vc, 255);  /* bring in opaque */
            pixd.SetRGBComponent(pix2, L.L_ALPHA_CHANNEL);
        }

        return pixd;
    }

    // leptonica: pixAffinePtaGray (affine.c)
    /// <summary>Interpolated affine transform of an 8 bpp pix, given 3 pairs of points.</summary>
    /// <param name="ptad">3 pts of final coordinate space</param>
    /// <param name="ptas">3 pts of initial coordinate space</param>
    /// <param name="grayval">e.g.: 0 to bring in BLACK, 255 for WHITE</param>
    /// <returns>the transformed pix; this must be 8 bpp</returns>
    public Pix AffinePtaGray(Pta ptad, Pta ptas, byte grayval)
    {
        if (Depth != 8)
            throw Lept.Arg("pixAffinePtaGray", "pixs must be 8 bpp");
        if (ptas.Count != 3)
            throw Lept.Arg("pixAffinePtaGray", "ptas count not 3");
        if (ptad.Count != 3)
            throw Lept.Arg("pixAffinePtaGray", "ptad count not 3");

            /* Get backwards transform from dest to src, and apply it */
        float[] vc = GetAffineXformCoeffs(ptad, ptas);
        return AffineGray(vc, grayval);
    }

    // leptonica: pixAffineGray (affine.c)
    /// <summary>Interpolated affine transform of an 8 bpp pix, given the 6 coefficients.</summary>
    /// <param name="vc">vector of 6 coefficients for affine transformation</param>
    /// <param name="grayval">e.g.: 0 to bring in BLACK, 255 for WHITE</param>
    /// <returns>the transformed pix; this must be 8 bpp</returns>
    public Pix AffineGray(float[] vc, byte grayval)
    {
        GetDimensions(out int w, out int h, out _);
        if (Depth != 8)
            throw Lept.Arg("pixAffineGray", "pixs must be 8 bpp");

        float vc0 = vc[0], vc1 = vc[1], vc2 = vc[2];
        float vc3 = vc[3], vc4 = vc[4], vc5 = vc[5];

        uint[] datas = Data;
        int wpls = Wpl;
        Pix pixd = CreateTemplateNoInit(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        int w4 = w & ~3;
        uint fillWord = grayval * 0x01010101u;

            /* Iterate over destination pixels */
        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                int lined = i * wpld;
                float ry1 = vc1 * i, ry4 = vc4 * i;
                int j = 0;
                for (; j < w4; j += 4) {
                    uint b0 = (uint)LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, vc0 * j + ry1 + vc2, vc3 * j + ry4 + vc5, grayval);
                    int j1 = j + 1;
                    uint b1 = (uint)LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, vc0 * j1 + ry1 + vc2, vc3 * j1 + ry4 + vc5, grayval);
                    int j2 = j + 2;
                    uint b2 = (uint)LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, vc0 * j2 + ry1 + vc2, vc3 * j2 + ry4 + vc5, grayval);
                    int j3 = j + 3;
                    uint b3 = (uint)LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, vc0 * j3 + ry1 + vc2, vc3 * j3 + ry4 + vc5, grayval);
                    Unsafe.Add(ref datadRef, lined + (j >> 2)) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                }
                if (j < w) {
                    ref uint dstLineRef = ref Unsafe.Add(ref datadRef, lined);
                    Unsafe.Add(ref dstLineRef, w >> 2) = fillWord;
                    for (; j < w; j++) {
                        int val = LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, vc0 * j + ry1 + vc2, vc3 * j + ry4 + vc5, grayval);
                        SetDataByteUnchecked(ref dstLineRef, j, val);
                    }
                }
            }
        });

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *            Affine transform including alpha (blend) component             *
     *---------------------------------------------------------------------------*/
    // leptonica: pixAffinePtaWithAlpha (affine.c)
    /// <summary>Interpolated affine transform of a 32 bpp or colormapped pix, with an alpha channel.</summary>
    /// <param name="ptad">3 pts of final coordinate space</param>
    /// <param name="ptas">3 pts of initial coordinate space</param>
    /// <param name="pixg">[optional] 8 bpp, can be null</param>
    /// <param name="fract">between 0.0 and 1.0, with 0.0 fully transparent
    /// and 1.0 fully opaque</param>
    /// <param name="border">of pixels added to capture transformed source pixels</param>
    /// <returns>the transformed pix; this is 32 bpp rgb</returns>
    /// <remarks>
    /// (1) The alpha channel is transformed separately from pixs,
    ///     and aligns with it, being fully transparent outside the
    ///     boundary of the transformed pixs.  For pixels that are fully
    ///     transparent, a blending function like pixBlendWithGrayMask()
    ///     will give zero weight to corresponding pixels in pixs.
    /// (2) If pixg is NULL, it is generated as an alpha layer that is
    ///     partially opaque, using %fract.  Otherwise, it is cropped
    ///     to pixs if required and %fract is ignored.  The alpha channel
    ///     in pixs is never used.
    /// (3) Colormaps are removed.
    /// (4) When pixs is transformed, it doesn't matter what color is brought
    ///     in because the alpha channel will be transparent (0) there.
    /// (5) To avoid losing source pixels in the destination, it may be
    ///     necessary to add a border to the source pix before doing
    ///     the affine transformation.  This can be any non-negative number.
    /// (6) The input %ptad and %ptas are in a coordinate space before
    ///     the border is added.  Internally, we compensate for this
    ///     before doing the affine transform on the image after the border
    ///     is added.
    /// (7) The default setting for the border values in the alpha channel
    ///     is 0 (transparent) for the outermost ring of pixels and
    ///     (0.5 * fract * 255) for the second ring.  When blended over
    ///     a second image, this
    ///     (a) shrinks the visible image to make a clean overlap edge
    ///         with an image below, and
    ///     (b) softens the edges by weakening the aliasing there.
    ///     Use l_setAlphaMaskBorder() to change these values.
    /// </remarks>
    public Pix AffinePtaWithAlpha(Pta ptad, Pta ptas, Pix? pixg, float fract, int border)
    {
        GetDimensions(out int ws, out int hs, out int d);
        if (d != 32 && Colormap == null)
            throw Lept.Arg("pixAffinePtaWithAlpha", "pixs not cmapped or 32 bpp");
        if (pixg != null && pixg.Depth != 8) {
            Log.Warning("pixAffinePtaWithAlpha", "pixg not 8 bpp; using 'fract' transparent alpha");
            pixg = null;
        }
        if (pixg == null && (fract < 0.0 || fract > 1.0)) {
            Log.Warning("pixAffinePtaWithAlpha", "invalid fract; using 1.0 (fully transparent)");
            fract = 1.0f;
        }
        if (pixg == null && fract == 0.0)
            Log.Warning("pixAffinePtaWithAlpha", "fully opaque alpha; image will not be blended");

            /* Add border; the color doesn't matter */
        Pix pixb1 = AddBorder(border, 0);

            /* Transform the ptr arrays to work on the bordered image */
        Pta ptad2 = ptad.Transform(border, border, 1.0f, 1.0f);
        Pta ptas2 = ptas.Transform(border, border, 1.0f, 1.0f);

            /* Do separate affine transform of rgb channels of pixs and of pixg */
        Pix pixd = pixb1.AffinePtaColor(ptad2, ptas2, 0);
        Pix pixg2;
        if (pixg == null) {
            pixg2 = Create(ws, hs, 8);
            if (fract == 1.0)
                pixg2.SetAll();
            else
                pixg2.SetAllArbitrary((uint)(int)(255.0 * fract));
        } else {
            pixg2 = pixg.ResizeToMatch(null, ws, hs);
        }
        if (ws > 10 && hs > 10) {  /* see note 7 */
            pixg2.SetBorderRingVal(1,
                (uint)(int)(255.0 * fract * AlphaMaskBorderVals[0]));
            pixg2.SetBorderRingVal(2,
                (uint)(int)(255.0 * fract * AlphaMaskBorderVals[1]));
        }
        Pix pixb2 = pixg2.AddBorder(border, 0);  /* must be black border */
        Pix pixga = pixb2.AffinePtaGray(ptad2, ptas2, 0);
        pixd.SetRGBComponent(pixga, L.L_ALPHA_CHANNEL);
        pixd.Spp = 4;

        return pixd;
    }

    /*-------------------------------------------------------------*
     *                 Affine coordinate transformation            *
     *-------------------------------------------------------------*/
    // leptonica: getAffineXformCoeffs (affine.c)
    /// <summary>Computes the 6 coefficients of the affine transform that takes 3 points ptas into 3 points ptad.</summary>
    /// <param name="ptas">source 3 points; unprimed</param>
    /// <param name="ptad">transformed 3 points; primed</param>
    /// <returns>vector of coefficients of transform</returns>
    /// <remarks>
    /// We have a set of six equations, describing the affine
    /// transformation that takes 3 points ptas into 3 other
    /// points ptad.  These equations are:
    ///
    ///     x1' = c[0]*x1 + c[1]*y1 + c[2]
    ///     y1' = c[3]*x1 + c[4]*y1 + c[5]
    ///     x2' = c[0]*x2 + c[1]*y2 + c[2]
    ///     y2' = c[3]*x2 + c[4]*y2 + c[5]
    ///     x3' = c[0]*x3 + c[1]*y3 + c[2]
    ///     y3' = c[3]*x3 + c[4]*y3 + c[5]
    ///
    /// This can be represented as
    ///
    ///     AC = B
    ///
    /// where B and C are column vectors
    ///
    ///     B = [ x1' y1' x2' y2' x3' y3' ]
    ///     C = [ c[0] c[1] c[2] c[3] c[4] c[5] c[6] ]
    ///
    /// and A is the 6x6 matrix
    ///
    ///     x1   y1   1   0    0    0
    ///      0    0   0   x1   y1   1
    ///     x2   y2   1   0    0    0
    ///      0    0   0   x2   y2   1
    ///     x3   y3   1   0    0    0
    ///      0    0   0   x3   y3   1
    ///
    /// These six equations are solved here for the coefficients C.
    ///
    /// These six coefficients can then be used to find the dest
    /// point x',y') corresponding to any src point (x,y, according
    /// to the equations
    ///
    ///      x' = c[0]x + c[1]y + c[2]
    ///      y' = c[3]x + c[4]y + c[5]
    ///
    /// that are implemented in affineXformPt.
    ///
    /// !!!!!!!!!!!!!!!!!!   Very important   !!!!!!!!!!!!!!!!!!!!!!
    ///
    /// When the affine transform is composed from a set of simple
    /// operations such as translation, scaling and rotation,
    /// it is built in a form to convert from the un-transformed src
    /// point to the transformed dest point.  However, when an
    /// affine transform is used on images, it is used in an inverted
    /// way: it converts from the transformed dest point to the
    /// un-transformed src point.  So, for example, if you transform
    /// a boxa using transform A, to transform an image in the same
    /// way you must use the inverse of A.
    ///
    /// For example, if you transform a boxa with a 3x3 affine matrix
    /// 'mat', the analogous image transformation must use 'matinv':
    ///
    ///     boxad = boxaAffineTransform(boxas, mat);
    ///     affineInvertXform(mat, &amp;matinv);
    ///     pixd = pixAffine(pixs, matinv, L_BRING_IN_WHITE);
    ///
    /// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
    /// </remarks>
    public static float[] GetAffineXformCoeffs(Pta ptas, Pta ptad)
    {
        float[] b = new float[6];   /* rhs vector of primed coords X'; coeffs returned */
        float[][] a = new float[6][];  /* 6x6 matrix A  */

        ptas.GetPt(0, out float x1, out float y1);
        ptas.GetPt(1, out float x2, out float y2);
        ptas.GetPt(2, out float x3, out float y3);
        ptad.GetPt(0, out b[0], out b[1]);
        ptad.GetPt(1, out b[2], out b[3]);
        ptad.GetPt(2, out b[4], out b[5]);

        for (int i = 0; i < 6; i++)
            a[i] = new float[6];
        a[0][0] = x1;
        a[0][1] = y1;
        a[0][2] = 1.0f;
        a[1][3] = x1;
        a[1][4] = y1;
        a[1][5] = 1.0f;
        a[2][0] = x2;
        a[2][1] = y2;
        a[2][2] = 1.0f;
        a[3][3] = x2;
        a[3][4] = y2;
        a[3][5] = 1.0f;
        a[4][0] = x3;
        a[4][1] = y3;
        a[4][2] = 1.0f;
        a[5][3] = x3;
        a[5][4] = y3;
        a[5][5] = 1.0f;

        GaussJordan(a, b, 6);

        return b;
    }

    // leptonica: affineInvertXform (affine.c)
    /// <summary>Inverts an affine transform.</summary>
    /// <param name="vc">vector of 6 coefficients</param>
    /// <returns>the inverted transform</returns>
    /// <remarks>
    /// (1) The 6 affine transform coefficients are the first
    ///     two rows of a 3x3 matrix where the last row has
    ///     only a 1 in the third column.  We invert this
    ///     using gaussjordan(), and select the first 2 rows
    ///     as the coefficients of the inverse affine transform.
    /// (2) Alternatively, we can find the inverse transform
    ///     coefficients by inverting the 2x2 submatrix,
    ///     and treating the top 2 coefficients in the 3rd column as
    ///     a RHS vector for that 2x2 submatrix.  Then the
    ///     6 inverted transform coefficients are composed of
    ///     the inverted 2x2 submatrix and the negative of the
    ///     transformed RHS vector.  Why is this so?  We have
    ///        Y = AX + R  (2 equations in 6 unknowns)
    ///     Then
    ///        X = A'Y - A'R
    ///     Gauss-jordan solves
    ///        AF = R
    ///     and puts the solution for F, which is A'R,
    ///     into the input R vector.
    /// </remarks>
    public static float[] AffineInvertXform(float[] vc)
    {
        float[][] a = new float[3][];
        float[] b = { 1.0f, 1.0f, 1.0f };   /* anything; results ignored */

        for (int i = 0; i < 3; i++)
            a[i] = new float[3];
        a[0][0] = vc[0];
        a[0][1] = vc[1];
        a[0][2] = vc[2];
        a[1][0] = vc[3];
        a[1][1] = vc[4];
        a[1][2] = vc[5];
        a[2][2] = 1.0f;
        GaussJordan(a, b, 3);  /* this inverts matrix a */
        float[] vci = new float[6];
        vci[0] = a[0][0];
        vci[1] = a[0][1];
        vci[2] = a[0][2];
        vci[3] = a[1][0];
        vci[4] = a[1][1];
        vci[5] = a[1][2];

        return vci;
    }

    // leptonica: affineXformSampledPt (affine.c)
    /// <summary>Transforms a point to the nearest integer pixel location.</summary>
    /// <param name="vc">vector of 6 coefficients</param>
    /// <param name="x">initial point</param>
    /// <param name="y">initial point</param>
    /// <param name="pxp">transformed point</param>
    /// <param name="pyp">transformed point</param>
    /// <remarks>
    /// (1) This finds the nearest pixel coordinates of the transformed point.
    /// (2) It does not check ptrs for returned data!
    /// </remarks>
    public static void AffineXformSampledPt(float[] vc, int x, int y, out int pxp, out int pyp)
    {
        pxp = (int)(vc[0] * x + vc[1] * y + vc[2] + 0.5);
        pyp = (int)(vc[3] * x + vc[4] * y + vc[5] + 0.5);
    }

    // leptonica: affineXformPt (affine.c)
    /// <summary>Transforms a point to its floating point location.</summary>
    /// <param name="vc">vector of 6 coefficients</param>
    /// <param name="x">initial point</param>
    /// <param name="y">initial point</param>
    /// <param name="pxp">transformed point</param>
    /// <param name="pyp">transformed point</param>
    /// <remarks>
    /// (1) This computes the floating point location of the transformed point.
    /// (2) It does not check ptrs for returned data!
    /// </remarks>
    public static void AffineXformPt(float[] vc, int x, int y, out float pxp, out float pyp)
    {
        pxp = vc[0] * x + vc[1] * y + vc[2];
        pyp = vc[3] * x + vc[4] * y + vc[5];
    }

    /*-------------------------------------------------------------*
     *                 Interpolation helper functions              *
     *-------------------------------------------------------------*/
    // leptonica: linearInterpolatePixelColor (affine.c)
    /// <summary>Linear interpolation of a 32 bpp pixel value at a floating point location.</summary>
    /// <param name="datas">image data</param>
    /// <param name="wpls">32-bit word/line for this data array</param>
    /// <param name="w">of image</param>
    /// <param name="h">of image</param>
    /// <param name="x">floating pt location for evaluation</param>
    /// <param name="y">floating pt location for evaluation</param>
    /// <param name="colorval">color brought in from the outside when the
    /// input x,y location is outside the image; in 0xrrggbb00 format)</param>
    /// <returns>interpolated color value</returns>
    /// <remarks>
    /// (1) This is a standard linear interpolation function.  It is
    ///     equivalent to area weighting on each component, and
    ///     avoids "jaggies" when rendering sharp edges.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint LinearInterpolatePixelColor(ReadOnlySpan<uint> datas, int wpls, int w, int h,
                                                   float x, float y, uint colorval)
    {
        bool valid = (x >= 0.0 && y >= 0.0 && x < w && y < h);
        if (!valid) return colorval;
        return LinearInterpolatePixelColorCore(ref MemoryMarshal.GetReference(datas), wpls, w, h, x, y, colorval);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint LinearInterpolatePixelColorCore(ref uint datasRef, int wpls, int w, int h,
                                                         float x, float y, uint colorval)
    {
        if (!(x >= 0.0f && y >= 0.0f && x < w && y < h))
            return colorval;

        int xpm = (int)(16.0 * x);
        int ypm = (int)(16.0 * y);
        int xp = xpm >> 4;
        int xp2 = xp + 1 < w ? xp + 1 : xp;
        int yp = ypm >> 4;
        if (yp + 1 >= h) wpls = 0;
        // NOTE(upstream): with wpls == 0 the line offset below becomes 0, so on the last
        // source row the pixel values are read from row 0; see docs/UPSTREAM_NOTES.md.
        int xf = xpm & 0x0f;
        int yf = ypm & 0x0f;
        int wx0 = 16 - xf;
        int wy0 = 16 - yf;
        uint w00 = (uint)(wx0 * wy0);
        uint w10 = (uint)(xf * wy0);
        uint w01 = (uint)(wx0 * yf);
        uint w11 = (uint)(xf * yf);

            /* Do area weighting (eqiv. to linear interpolation) */
        int lines = yp * wpls;
        uint word00 = Unsafe.Add(ref datasRef, lines + xp);
        uint word10 = Unsafe.Add(ref datasRef, lines + xp2);
        uint word01 = Unsafe.Add(ref datasRef, lines + wpls + xp);
        uint word11 = Unsafe.Add(ref datasRef, lines + wpls + xp2);
        uint rval = (w00 * (word00 >> 24) +
                     w10 * (word10 >> 24) +
                     w01 * (word01 >> 24) +
                     w11 * (word11 >> 24)) >> 8;
        uint gval = (w00 * ((word00 >> 16) & 0xffu) +
                     w10 * ((word10 >> 16) & 0xffu) +
                     w01 * ((word01 >> 16) & 0xffu) +
                     w11 * ((word11 >> 16) & 0xffu)) >> 8;
        uint bval = (w00 * ((word00 >> 8) & 0xffu) +
                     w10 * ((word10 >> 8) & 0xffu) +
                     w01 * ((word01 >> 8) & 0xffu) +
                     w11 * ((word11 >> 8) & 0xffu)) >> 8;
        return (rval << 24) | (gval << 16) | (bval << 8);
    }

    // leptonica: linearInterpolatePixelGray (affine.c)
    /// <summary>Linear interpolation of an 8 bpp pixel value at a floating point location.</summary>
    /// <param name="datas">image data</param>
    /// <param name="wpls">32-bit word/line for this data array</param>
    /// <param name="w">of image</param>
    /// <param name="h">of image</param>
    /// <param name="x">floating pt location for evaluation</param>
    /// <param name="y">floating pt location for evaluation</param>
    /// <param name="grayval">color brought in from the outside when the
    /// input x,y location is outside the image</param>
    /// <returns>interpolated gray value</returns>
    /// <remarks>
    /// (1) This is a standard linear interpolation function.  It is
    ///     equivalent to area weighting on each component, and
    ///     avoids "jaggies" when rendering sharp edges.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LinearInterpolatePixelGray(ReadOnlySpan<uint> datas, int wpls, int w, int h,
                                                 float x, float y, int grayval)
    {
        bool valid = (x >= 0.0 && y >= 0.0 && x < w && y < h);
        if (!valid) return grayval;
        return LinearInterpolatePixelGrayCore(ref MemoryMarshal.GetReference(datas), wpls, w, h, x, y, grayval);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int LinearInterpolatePixelGrayCore(ref uint datasRef, int wpls, int w, int h,
                                                       float x, float y, int grayval)
    {
        if (!(x >= 0.0f && y >= 0.0f && x < w && y < h))
            return grayval;

        int xpm = (int)(16.0 * x);
        int ypm = (int)(16.0 * y);
        int xp = xpm >> 4;
        int xp2 = xp + 1 < w ? xp + 1 : xp;
        int yp = ypm >> 4;
        if (yp + 1 >= h) wpls = 0;
        // NOTE(upstream): with wpls == 0 the line offset below becomes 0, so on the last
        // source row the pixel values are read from row 0; see docs/UPSTREAM_NOTES.md.
        int xf = xpm & 0x0f;
        int yf = ypm & 0x0f;
        int wx0 = 16 - xf;
        int wy0 = 16 - yf;

            /* Interpolate by area weighting. */
        ref uint line0Ref = ref Unsafe.Add(ref datasRef, yp * wpls);
        ref uint line1Ref = ref Unsafe.Add(ref line0Ref, wpls);
        int v00 = wx0 * wy0 * GetDataByteUnchecked(ref line0Ref, xp);
        int v10 = xf * wy0 * GetDataByteUnchecked(ref line0Ref, xp2);
        int v01 = wx0 * yf * GetDataByteUnchecked(ref line1Ref, xp);
        int v11 = xf * yf * GetDataByteUnchecked(ref line1Ref, xp2);
        return (v00 + v01 + v10 + v11) >> 8;
    }

    /*-------------------------------------------------------------*
     *               Gauss-jordan linear equation solver           *
     *-------------------------------------------------------------*/
    // leptonica: gaussjordan (affine.c)
    /// <summary>Solves the linear system a x = b in place.</summary>
    /// <param name="a">n x n matrix</param>
    /// <param name="b">n x 1 right-hand side column vector</param>
    /// <param name="n">dimension</param>
    /// <returns>true if ok; false if the matrix is singular (the C function returns 1)</returns>
    /// <remarks>
    /// (1) There are two side-effects:
    ///     * The matrix a is transformed to its inverse A
    ///     * The rhs vector b is transformed to the solution x
    ///       of the linear equation ax = b
    /// (2) The inverse A can then be used to solve the same equation with
    ///     different rhs vectors c by multiplication: x = Ac
    /// (3) Adapted from "Numerical Recipes in C, Second Edition", 1992,
    ///     pp. 36-41 (gauss-jordan elimination)
    /// </remarks>
    public static bool GaussJordan(float[][] a, float[] b, int n)
    {
        float temp;
        int[] indexc = new int[n];
        int[] indexr = new int[n];
        int[] ipiv = new int[n];

        int icol = 0, irow = 0;  /* silence static checker */
        for (int i = 0; i < n; i++) {
            float maxval = 0.0f;
            for (int j = 0; j < n; j++) {
                if (ipiv[j] != 1) {
                    for (int k = 0; k < n; k++) {
                        if (ipiv[k] == 0) {
                            if (MathF.Abs(a[j][k]) >= maxval) {
                                maxval = MathF.Abs(a[j][k]);
                                irow = j;
                                icol = k;
                            }
                        } else if (ipiv[k] > 1) {
                            Log.Error("gaussjordan", "singular matrix");
                            return false;
                        }
                    }
                }
            }
            ++(ipiv[icol]);

            if (irow != icol) {
                for (int col = 0; col < n; col++) {
                    temp = a[irow][col]; a[irow][col] = a[icol][col]; a[icol][col] = temp;
                }
                temp = b[irow]; b[irow] = b[icol]; b[icol] = temp;
            }

            indexr[i] = irow;
            indexc[i] = icol;
            if (a[icol][icol] == 0.0) {
                Log.Error("gaussjordan", "singular matrix");
                return false;
            }
            float pivinv = (float)(1.0 / a[icol][icol]);
            a[icol][icol] = 1.0f;
            for (int col = 0; col < n; col++)
                a[icol][col] *= pivinv;
            b[icol] *= pivinv;

            for (int row = 0; row < n; row++) {
                if (row != icol) {
                    float val = a[row][icol];
                    a[row][icol] = 0.0f;
                    for (int col = 0; col < n; col++)
                        a[row][col] -= a[icol][col] * val;
                    b[row] -= b[icol] * val;
                }
            }
        }

        for (int col = n - 1; col >= 0; col--) {
            if (indexr[col] != indexc[col]) {
                for (int k = 0; k < n; k++) {
                    temp = a[k][indexr[col]]; a[k][indexr[col]] = a[k][indexc[col]]; a[k][indexc[col]] = temp;
                }
            }
        }

        return true;
    }

    /*-------------------------------------------------------------*
     *              Sequential affine image transformation         *
     *-------------------------------------------------------------*/
    // leptonica: pixAffineSequential (affine.c)
    /// <summary>Affine transform by a sequence of shear, scale and translation operations.</summary>
    /// <param name="ptad">3 pts of final coordinate space</param>
    /// <param name="ptas">3 pts of initial coordinate space</param>
    /// <param name="bw">pixels of additional border width during computation</param>
    /// <param name="bh">pixels of additional border height during computation</param>
    /// <returns>the transformed pix</returns>
    /// <remarks>
    /// (1) The 3 pts must not be collinear.
    /// (2) The 3 pts must be given in this order:
    ///      ~ origin
    ///      ~ a location along the x-axis
    ///      ~ a location along the y-axis.
    /// (3) You must guess how much border must be added so that no
    ///     pixels are lost in the transformations from src to
    ///     dest coordinate space.  (This can be calculated but it
    ///     is a lot of work!)  For coordinate spaces that are nearly
    ///     at right angles, on a 300 ppi scanned page, the addition
    ///     of 1000 pixels on each side is usually sufficient.
    /// (4) This is here for pedagogical reasons.  It is about 3x faster
    ///     on 1 bpp images than pixAffineSampled(), but the results
    ///     on text are much inferior.
    /// </remarks>
    public Pix AffineSequential(Pta ptad, Pta ptas, int bw, int bh)
    {
        if (ptas.Count != 3)
            throw Lept.Arg("pixAffineSequential", "ptas count not 3");
        if (ptad.Count != 3)
            throw Lept.Arg("pixAffineSequential", "ptad count not 3");
        ptas.GetIPt(0, out int x1, out int y1);
        ptas.GetIPt(1, out int x2, out int y2);
        ptas.GetIPt(2, out int x3, out int y3);
        ptad.GetIPt(0, out int x1p, out int y1p);
        ptad.GetIPt(1, out int x2p, out int y2p);
        ptad.GetIPt(2, out int x3p, out int y3p);

        if (y1 == y3)
            throw Lept.Arg("pixAffineSequential", "y1 == y3!");
        if (y1p == y3p)
            throw Lept.Arg("pixAffineSequential", "y1p == y3p!");

        Pix pix1;
        if (bw != 0 || bh != 0) {
                /* resize all points and add border to pixs */
            x1 = x1 + bw;
            y1 = y1 + bh;
            x2 = x2 + bw;
            y2 = y2 + bh;
            x3 = x3 + bw;
            y3 = y3 + bh;
            x1p = x1p + bw;
            y1p = y1p + bh;
            x2p = x2p + bw;
            y2p = y2p + bh;
            x3p = x3p + bw;
            y3p = y3p + bh;

            pix1 = AddBorderGeneral(bw, bw, bh, bh, 0);
        } else {
            pix1 = Copy(null, this);
        }

        /*-------------------------------------------------------------*
            The horizontal shear is done to move the 3rd point to the
            y axis.  This moves the 2nd point either towards or away
            from the y axis, depending on whether it is above or below
            the x axis.  That motion must be computed so that we know
            the angle of vertical shear to use to get the 2nd point
            on the x axis.  We must also know the x coordinate of the
            2nd point in order to compute how much scaling is required
            to match points on the axis.
         *-------------------------------------------------------------*/

            /* Shear angles required to put src points on x and y axes */
        float th3 = (float)Math.Atan2((double)(x1 - x3), (double)(y1 - y3));
        float x2s = (float)(x2 - ((float)(y1 - y2) * (x3 - x1)) / (y1 - y3));
        if (x2s == (float)x1)
            throw Lept.Fail("pixAffineSequential", "x2s == x1!");
        float ph2 = (float)Math.Atan2((double)(y1 - y2), (double)(x2s - x1));

            /* Shear angles required to put dest points on x and y axes.
             * Use the negative of these values to instead move the
             * src points from the axes to the actual dest position.
             * These values are also needed to scale the image. */
        float th3p = (float)Math.Atan2((double)(x1p - x3p), (double)(y1p - y3p));
        float x2sp = (float)(x2p -
                             ((float)(y1p - y2p) * (x3p - x1p)) / (y1p - y3p));
        if (x2sp == (float)x1p)
            throw Lept.Fail("pixAffineSequential", "x2sp == x1p!");
        float ph2p = (float)Math.Atan2((double)(y1p - y2p), (double)(x2sp - x1p));

            /* Shear image to first put src point 3 on the y axis,
             * and then to put src point 2 on the x axis */
        pix1.HShearIP(y1, th3, L.L_BRING_IN_WHITE);
        pix1.VShearIP(x1, ph2, L.L_BRING_IN_WHITE);

            /* Scale image to match dest scale.  The dest scale
             * is calculated above from the angles th3p and ph2p
             * that would be required to move the dest points to
             * the x and y axes. */
        float scalex = (float)(x2sp - x1p) / (x2s - x1);
        float scaley = (float)(y3p - y1p) / (y3 - y1);
        Pix pix2 = pix1.Scale(scalex, scaley);

        /*-------------------------------------------------------------*
            Scaling moves the 1st src point, which is the origin.
            It must now be moved again to coincide with the origin
            (1st point) of the dest.  After this is done, the 2nd
            and 3rd points must be sheared back to the original
            positions of the 2nd and 3rd dest points.  We use the
            negative of the angles that were previously computed
            for shearing those points in the dest image to x and y
            axes, and take the shears in reverse order as well.
         *-------------------------------------------------------------*/
            /* Shift image to match dest origin. */
        int x1sc = (int)(scalex * x1 + 0.5);   /* x comp of origin after scaling */
        int y1sc = (int)(scaley * y1 + 0.5);   /* y comp of origin after scaling */
        pix2.RasteropIP(x1p - x1sc, y1p - y1sc, L.L_BRING_IN_WHITE);

            /* Shear image to take points 2 and 3 off the axis and
             * put them in the original dest position */
        pix2.VShearIP(x1p, -ph2p, L.L_BRING_IN_WHITE);
        pix2.HShearIP(y1p, -th3p, L.L_BRING_IN_WHITE);

        Pix pixd;
        if (bw != 0 || bh != 0)
            pixd = pix2.RemoveBorderGeneral(bw, bw, bh, bh);
        else
            pixd = Clone(pix2);

        return pixd;
    }
}
