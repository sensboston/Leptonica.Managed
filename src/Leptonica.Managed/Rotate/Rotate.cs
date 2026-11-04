using System;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  rotate.c: Rotation about the image center.
 *
 *  General rotation about image center
 *      pixRotate, pixEmbedForRotation
 *  General rotation by sampling
 *      pixRotateBySampling
 *  Nice (slow) rotation of 1 bpp image
 *      pixRotateBinaryNice
 *  Rotation including alpha (blend) component
 *      pixRotateWithAlpha
 *
 *  Rotations are measured in radians; clockwise is positive.
 *
 *  The general rotation pixRotate() does the best job for
 *  rotating about the image center.  For 1 bpp, it uses shear;
 *  for others, it uses either shear or area mapping.
 *  If requested, it expands the output image so that no pixels are lost
 *  in the rotation, and this can be done on multiple successive shears
 *  without expanding beyond the maximum necessary size.
 *
 *  Differences from C:
 *    - The C pointers into the image data are word offsets into Pix.Data.
 *    - The extern AlphaMaskBorderVals is Pix.AlphaMaskBorderVals (pix2.c).
 *
 *  Not ported: nothing.
 *
 *  Stubs for callees in other files: pixRotateShearCenter (rotateshear.c),
 *  pixRotateAM, pixRotateAMColor, pixRotateAMGray (rotateam.c);
 *  pixBlockconv (convolve.c) and pixUnsharpMasking (enhance.c) are stubbed
 *  in their own files.
 */

/// <summary>Constants from pix.h used by rotate.c.</summary>
public static partial class L
{
    /*-------------------------------------------------------------------------*
     *                       Rotation types (pix.h)                            *
     *-------------------------------------------------------------------------*/
    /// <summary>Use area-map rotation, if possible.</summary>
    public const int L_ROTATE_AREA_MAP = 1;
    /// <summary>Use rotation by shears.</summary>
    public const int L_ROTATE_SHEAR = 2;
    /// <summary>Use sampling for the rotation.</summary>
    public const int L_ROTATE_SAMPLING = 3;
}

public sealed partial class Pix
{
    private const float MinAngleToRotate = 0.001f;   /* radians; ~0.06 deg */
    private const float Max1BppShearAngle = 0.06f;   /* radians; ~3 deg    */
    private const float LimitShearAngle = 0.35f;     /* radians; ~20 deg   */

    /*------------------------------------------------------------------*
     *                  General rotation about the center               *
     *------------------------------------------------------------------*/
    // leptonica: pixRotate (rotate.c)
    /// <summary>General rotation about the image center.</summary>
    /// <param name="angle">radians; clockwise is positive</param>
    /// <param name="type">L_ROTATE_AREA_MAP, L_ROTATE_SHEAR, L_ROTATE_SAMPLING</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <param name="width">original width; use 0 to avoid embedding</param>
    /// <param name="height">original height; use 0 to avoid embedding</param>
    /// <remarks>
    /// Input is 1, 2, 4, 8, 32 bpp rgb.
    /// (1) This is a high-level, simple interface for rotating images
    ///     about their center.
    /// (2) For very small rotations, just return a clone.
    /// (3) Rotation brings either white or black pixels in
    ///     from outside the image.
    /// (4) The rotation type is adjusted if necessary for the image
    ///     depth and size of rotation angle.  For 1 bpp images, we
    ///     rotate either by shear or sampling.
    /// (5) Colormaps are removed for rotation by area mapping.
    /// (6) The dest can be expanded so that no image pixels
    ///     are lost.  To invoke expansion, input the original
    ///     width and height.  For repeated rotation, use of the
    ///     original width and height allows the expansion to
    ///     stop at the maximum required size, which is a square
    ///     with side = sqrt(w*w + h*h).
    /// </remarks>
    public Pix Rotate(float angle, int type, int incolor, int width, int height)
    {
        int w, h, d;
        uint fillval;
        Pix pix1, pix2, pix3, pixd;
        PixColormap? cmap;

        Pix pixs = this;
        if (type != L.L_ROTATE_SHEAR && type != L.L_ROTATE_AREA_MAP &&
            type != L.L_ROTATE_SAMPLING)
            throw Lept.Arg("pixRotate", "invalid type");
        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg("pixRotate", "invalid incolor");

        if (Math.Abs(angle) < MinAngleToRotate)
            return Clone(pixs);

            /* Adjust rotation type if necessary:
             *  - If d == 1 bpp and the angle is more than about 6 degrees,
             *    rotate by sampling; otherwise rotate by shear.
             *  - If d > 1, only allow shear rotation up to about 20 degrees;
             *    beyond that, default a shear request to sampling. */
        if (pixs.Depth == 1) {
            if (Math.Abs(angle) > Max1BppShearAngle) {
                if (type != L.L_ROTATE_SAMPLING)
                    Log.Info("pixRotate", "1 bpp, large angle; rotate by sampling");
                type = L.L_ROTATE_SAMPLING;
            } else if (type != L.L_ROTATE_SHEAR) {
                Log.Info("pixRotate", "1 bpp; rotate by shear");
                type = L.L_ROTATE_SHEAR;
            }
        } else if (Math.Abs(angle) > LimitShearAngle && type == L.L_ROTATE_SHEAR) {
            Log.Info("pixRotate", "large angle; rotate by sampling");
            type = L.L_ROTATE_SAMPLING;
        }

            /* Remove colormap if we rotate by area mapping. */
        cmap = pixs.Colormap;
        if (cmap != null && type == L.L_ROTATE_AREA_MAP)
            pix1 = pixs.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        else
            pix1 = Clone(pixs);
        cmap = pix1.Colormap;

            /* Otherwise, if there is a colormap and we're not embedding,
             * add white color if it doesn't exist. */
        if (cmap != null && width == 0) {  /* no embedding; generate %incolor */
            if (incolor == L.L_BRING_IN_BLACK)
                cmap.AddBlackOrWhite(0, out _);
            else  /* L_BRING_IN_WHITE */
                cmap.AddBlackOrWhite(1, out _);
        }

            /* Request to embed in a larger image; do if necessary */
        pix2 = pix1.EmbedForRotation(angle, incolor, width, height);

            /* Area mapping requires 8 or 32 bpp.  If less than 8 bpp and
             * area map rotation is requested, convert to 8 bpp. */
        d = pix2.Depth;
        if (type == L.L_ROTATE_AREA_MAP && d < 8)
            pix3 = pix2.ConvertTo8(false);
        else
            pix3 = Clone(pix2);

            /* Do the rotation: shear, sampling or area mapping */
        pix3.GetDimensions(out w, out h, out d);
        if (type == L.L_ROTATE_SHEAR) {
            pixd = pix3.RotateShearCenter(angle, incolor);
        } else if (type == L.L_ROTATE_SAMPLING) {
            pixd = pix3.RotateBySampling(w / 2, h / 2, angle, incolor);
        } else {  /* rotate by area mapping */
            if (pix3.Spp != 4 &&
                LeptGpu.TryGetActive((long)w * h, d == 32 ? 20 : 12, out IGpuAccelerator? gpu) &&
                gpu.TryRotateAM(pix3, angle, incolor, out Pix? gpuPix)) {
                return gpuPix;
            }
            fillval = 0;
            if (incolor == L.L_BRING_IN_WHITE) {
                if (d == 8)
                    fillval = 255;
                else  /* d == 32 */
                    fillval = 0xffffff00;
            }
            if (d == 8)
                pixd = pix3.RotateAMGray(angle, (byte)fillval);
            else  /* d == 32 */
                pixd = pix3.RotateAMColor(angle, fillval);
        }

        return pixd;
    }


    // leptonica: pixEmbedForRotation (rotate.c)
    /// <summary>Embeds the image in a larger one, if needed to hold any rotation of the original size.</summary>
    /// <param name="angle">radians; clockwise is positive</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <param name="width">original width; use 0 to avoid embedding</param>
    /// <param name="height">original height; use 0 to avoid embedding</param>
    /// <remarks>
    /// Input is 1, 2, 4, 8, 32 bpp rgb.
    /// (1) For very small rotations, just return a clone.
    /// (2) Generate larger image to embed pixs if necessary, and
    ///     place the center of the input image in the center.
    /// (3) Rotation brings either white or black pixels in
    ///     from outside the image.  For colormapped images where
    ///     there is no white or black, a new color is added if
    ///     possible for these pixels; otherwise, either the
    ///     lightest or darkest color is used.  In most cases,
    ///     the colormap will be removed prior to rotation.
    /// (4) The dest is to be expanded so that no image pixels
    ///     are lost after rotation.  Input of the original width
    ///     and height allows the expansion to stop at the maximum
    ///     required size, which is a square with side equal to
    ///     sqrt(w*w + h*h).
    /// (5) For an arbitrary angle, the expansion can be found by
    ///     considering the UL and UR corners.  As the image is
    ///     rotated, these move in an arc centered at the center of
    ///     the image.  Normalize to a unit circle by dividing by half
    ///     the image diagonal.  After a rotation of T radians, the UL
    ///     and UR corners are at points T radians along the unit
    ///     circle.  Compute the x and y coordinates of both these
    ///     points and take the max of absolute values; these represent
    ///     the half width and half height of the containing rectangle.
    ///     The arithmetic is done using formulas for sin(a+b) and cos(a+b),
    ///     where b = T.  For the UR corner, sin(a) = h/d and cos(a) = w/d.
    ///     For the UL corner, replace a by (pi - a), and you have
    ///     sin(pi - a) = h/d, cos(pi - a) = -w/d.  The equations
    ///     given below follow directly.
    /// </remarks>
    public Pix EmbedForRotation(float angle, int incolor, int width, int height)
    {
        int w, h, d, w1, h1, w2, h2, maxside, wnew, hnew, xoff, yoff, setcolor;
        double sina, cosa, fw, fh;
        Pix pixd;

        Pix pixs = this;
        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg("pixEmbedForRotation", "invalid incolor");
        if (Math.Abs(angle) < MinAngleToRotate)
            return Clone(pixs);

            /* Test if big enough to hold any rotation of the original image */
        pixs.GetDimensions(out w, out h, out d);
        maxside = (int)(Math.Sqrt((double)(width * width) +
                                  (double)(height * height)) + 0.5);
        if (w >= maxside && h >= maxside)  /* big enough */
            return Clone(pixs);

            /* Find the new sizes required to hold the image after rotation.
             * Note that the new dimensions must be at least as large as those
             * of pixs, because we're rasterop-ing into it before rotation. */
        cosa = Math.Cos(angle);
        sina = Math.Sin(angle);
        fw = (double)w;
        fh = (double)h;
        w1 = (int)(Math.Abs(fw * cosa - fh * sina) + 0.5);
        w2 = (int)(Math.Abs(-fw * cosa - fh * sina) + 0.5);
        h1 = (int)(Math.Abs(fw * sina + fh * cosa) + 0.5);
        h2 = (int)(Math.Abs(-fw * sina + fh * cosa) + 0.5);
        wnew = Math.Max(w, Math.Max(w1, w2));
        hnew = Math.Max(h, Math.Max(h1, h2));

        pixd = Create(wnew, hnew, d);
        pixd.CopyResolution(pixs);
        pixd.CopyColormap(pixs);
        pixd.CopySpp(pixs);
        pixd.CopyText(pixs);
        xoff = (wnew - w) / 2;
        yoff = (hnew - h) / 2;

            /* Set background to color to be rotated in */
        setcolor = (incolor == L.L_BRING_IN_BLACK) ? L.L_SET_BLACK : L.L_SET_WHITE;
        pixd.SetBlackOrWhite(setcolor);

            /* Rasterop automatically handles all 4 channels for rgba */
        pixd.Rasterop(xoff, yoff, w, h, L.PIX_SRC, pixs, 0, 0);
        return pixd;
    }


    /*------------------------------------------------------------------*
     *                    General rotation by sampling                  *
     *------------------------------------------------------------------*/
    // leptonica: pixRotateBySampling (rotate.c)
    /// <summary>Rotation by sampling about (xcen, ycen).</summary>
    /// <param name="xcen">x value of center of rotation</param>
    /// <param name="ycen">y value of center of rotation</param>
    /// <param name="angle">radians; clockwise is positive</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <remarks>
    /// Input is 1, 2, 4, 8, 16, 32 bpp rgb; can be cmapped.
    /// (1) For very small rotations, just return a clone.
    /// (2) Rotation brings either white or black pixels in
    ///     from outside the image.
    /// (3) Colormaps are retained.
    /// </remarks>
    public Pix RotateBySampling(int xcen, int ycen, float angle, int incolor)
    {
        int w, h, d, i, j, x, y, xdif, ydif, wm1, hm1, wpld;
        uint val;
        float sina, cosa;
        int lined;
        Pix pixd;

        Pix pixs = this;
        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg("pixRotateBySampling", "invalid incolor");
        pixs.GetDimensions(out w, out h, out d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixRotateBySampling", "invalid depth");

        if (Math.Abs(angle) < MinAngleToRotate)
            return Clone(pixs);

        pixd = CreateTemplate(pixs);
        pixd.SetBlackOrWhite(incolor);

        sina = (float)Math.Sin(angle);
        cosa = (float)Math.Cos(angle);
        uint[] datad = pixd.Data;
        wpld = pixd.Wpl;
        wm1 = w - 1;
        hm1 = h - 1;
        uint[] datas = pixs.Data;
        int[] lines = pixs.GetLinePtrs();

            /* Treat 1 bpp case specially */
        if (d == 1) {
            for (i = 0; i < h; i++) {  /* scan over pixd */
                lined = i * wpld;
                ydif = ycen - i;
                for (j = 0; j < w; j++) {
                    xdif = xcen - j;
                    x = xcen + (int)(-xdif * cosa - ydif * sina);
                    if (x < 0 || x > wm1) continue;
                    y = ycen + (int)(-ydif * cosa + xdif * sina);
                    if (y < 0 || y > hm1) continue;
                    if (incolor == L.L_BRING_IN_WHITE) {
                        if (GetDataBit(datas.AsSpan(lines[y]), x) != 0)
                            SetDataBit(datad.AsSpan(lined), j);
                    } else {
                        if (GetDataBit(datas.AsSpan(lines[y]), x) == 0)
                            ClearDataBit(datad.AsSpan(lined), j);
                    }
                }
            }
            return pixd;
        }

        for (i = 0; i < h; i++) {  /* scan over pixd */
            lined = i * wpld;
            ydif = ycen - i;
            for (j = 0; j < w; j++) {
                xdif = xcen - j;
                x = xcen + (int)(-xdif * cosa - ydif * sina);
                if (x < 0 || x > wm1) continue;
                y = ycen + (int)(-ydif * cosa + xdif * sina);
                if (y < 0 || y > hm1) continue;
                switch (d)
                {
                case 8:
                    val = (uint)GetDataByte(datas.AsSpan(lines[y]), x);
                    SetDataByte(datad.AsSpan(lined), j, (int)val);
                    break;
                case 32:
                    val = GetDataFourBytes(datas.AsSpan(lines[y]), x);
                    SetDataFourBytes(datad.AsSpan(lined), j, val);
                    break;
                case 2:
                    val = (uint)GetDataDibit(datas.AsSpan(lines[y]), x);
                    SetDataDibit(datad.AsSpan(lined), j, (int)val);
                    break;
                case 4:
                    val = (uint)GetDataQbit(datas.AsSpan(lines[y]), x);
                    SetDataQbit(datad.AsSpan(lined), j, (int)val);
                    break;
                case 16:
                    val = (uint)GetDataTwoBytes(datas.AsSpan(lines[y]), x);
                    SetDataTwoBytes(datad.AsSpan(lined), j, (int)val);
                    break;
                default:
                    throw Lept.Arg("pixRotateBySampling", "invalid depth");
                }
            }
        }

        return pixd;
    }


    /*------------------------------------------------------------------*
     *                 Nice (slow) rotation of 1 bpp image              *
     *------------------------------------------------------------------*/
    // leptonica: pixRotateBinaryNice (rotate.c)
    /// <summary>Computationally expensive rotation of a 1 bpp image, about the center.</summary>
    /// <param name="angle">radians; clockwise is positive; about the center</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <remarks>
    /// Input is 1 bpp.
    /// (1) For very small rotations, just return a clone.
    /// (2) This does a computationally expensive rotation of 1 bpp images.
    ///     The fastest rotators (using shears or subsampling) leave
    ///     visible horizontal and vertical shear lines across which
    ///     the image shear changes by one pixel.  To ameliorate the
    ///     visual effect one can introduce random dithering.  One
    ///     way to do this in a not-too-random fashion is given here.
    ///     We convert to 8 bpp, do a very small blur, rotate using
    ///     linear interpolation (same as area mapping), do a
    ///     small amount of sharpening to compensate for the initial
    ///     blur, and threshold back to binary.  The shear lines
    ///     are magically removed.
    /// (3) This operation is about 5x slower than rotation by sampling.
    /// </remarks>
    public Pix RotateBinaryNice(float angle, int incolor)
    {
        Pix pix1, pix2, pix3, pix4, pixd;

        Pix pixs = this;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixRotateBinaryNice", "pixs undefined or not 1 bpp");
        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg("pixRotateBinaryNice", "invalid incolor");

        pix1 = pixs.ConvertTo8(false);
        pix2 = pix1.Blockconv(1, 1);  /* smallest blur allowed */
        pix3 = pix2.RotateAM(angle, incolor);
        pix4 = pix3.UnsharpMasking(1, 1.0f);  /* sharpen a bit */
        pixd = pix4.ThresholdToBinary(128);
        return pixd;
    }


    /*------------------------------------------------------------------*
     *             Rotation including alpha (blend) component           *
     *------------------------------------------------------------------*/
    // leptonica: pixRotateWithAlpha (rotate.c)
    /// <summary>Rotation by area mapping, including an alpha (blend) component.</summary>
    /// <param name="angle">radians; clockwise is positive</param>
    /// <param name="pixg">[optional] 8 bpp, can be null</param>
    /// <param name="fract">between 0.0 and 1.0, with 0.0 fully transparent
    ///                     and 1.0 fully opaque</param>
    /// <returns>pixd 32 bpp rgba</returns>
    /// <remarks>
    /// Input is 32 bpp rgb or cmapped.
    /// (1) The alpha channel is transformed separately from pixs,
    ///     and aligns with it, being fully transparent outside the
    ///     boundary of the transformed pixs.  For pixels that are fully
    ///     transparent, a blending function like pixBlendWithGrayMask()
    ///     will give zero weight to corresponding pixels in pixs.
    /// (2) Rotation is about the center of the image; for very small
    ///     rotations, just return a clone.  The dest is automatically
    ///     expanded so that no image pixels are lost.
    /// (3) Rotation is by area mapping.  It doesn't matter what
    ///     color is brought in because the alpha channel will
    ///     be transparent (black) there.
    /// (4) If pixg is NULL, it is generated as an alpha layer that is
    ///     partially opaque, using %fract.  Otherwise, it is cropped
    ///     to pixs if required and %fract is ignored.  The alpha
    ///     channel in pixs is never used.
    /// (4) Colormaps are removed to 32 bpp.
    /// (5) The default setting for the border values in the alpha channel
    ///     is 0 (transparent) for the outermost ring of pixels and
    ///     (0.5 * fract * 255) for the second ring.  When blended over
    ///     a second image, this
    ///     (a) shrinks the visible image to make a clean overlap edge
    ///         with an image below, and
    ///     (b) softens the edges by weakening the aliasing there.
    ///     Use l_setAlphaMaskBorder() to change these values.
    /// (6) A subtle use of gamma correction is to remove gamma correction
    ///     before rotation and restore it afterwards.  This is done
    ///     by sandwiching this function between a gamma/inverse-gamma
    ///     photometric transform:
    ///         pixt = pixGammaTRCWithAlpha(NULL, pixs, 1.0 / gamma, 0, 255);
    ///         pixd = pixRotateWithAlpha(pixt, angle, NULL, fract);
    ///         pixGammaTRCWithAlpha(pixd, pixd, gamma, 0, 255);
    ///         pixDestroy(&amp;pixt);
    ///     This has the side-effect of producing artifacts in the very
    ///     dark regions.
    /// </remarks>
    public Pix RotateWithAlpha(float angle, Pix? pixg, float fract)
    {
        int ws, hs, d, spp;
        Pix pixd, pix32, pixg2, pixgr;

        Pix pixs = this;
        pixs.GetDimensions(out ws, out hs, out d);
        if (d != 32 && pixs.Colormap == null)
            throw Lept.Arg("pixRotateWithAlpha", "pixs not cmapped or 32 bpp");
        if (pixg != null && pixg.Depth != 8) {
            Log.Warning("pixRotateWithAlpha", "pixg not 8 bpp; using 'fract' transparent alpha");
            pixg = null;
        }
        if (pixg == null && (fract < 0.0 || fract > 1.0)) {
            Log.Warning("pixRotateWithAlpha", "invalid fract; using fully opaque");
            fract = 1.0f;
        }
        if (pixg == null && fract == 0.0)
            Log.Warning("pixRotateWithAlpha", "transparent alpha; image will not be blended");

            /* Make sure input to rotation is 32 bpp rgb, and rotate it */
        if (d != 32)
            pix32 = pixs.ConvertTo32();
        else
            pix32 = Clone(pixs);
        spp = pix32.Spp;
        pix32.Spp = 3;  /* ignore the alpha channel for the rotation */
        pixd = pix32.Rotate(angle, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, ws, hs);
        pix32.Spp = spp;  /* restore initial value in case it's a clone */

            /* Set up alpha layer with a fading border and rotate it */
        if (pixg == null) {
            pixg2 = Create(ws, hs, 8);
            if (fract == 1.0)
                pixg2.SetAll();
            else if (fract > 0.0)
                pixg2.SetAllArbitrary((uint)(int)(255.0 * fract));
        } else {
            pixg2 = pixg.ResizeToMatch(null, ws, hs);
        }
        if (ws > 10 && hs > 10) {  /* see note 8 */
            pixg2.SetBorderRingVal(1,
                                   (uint)(int)(255.0 * fract * AlphaMaskBorderVals[0]));
            pixg2.SetBorderRingVal(2,
                                   (uint)(int)(255.0 * fract * AlphaMaskBorderVals[1]));
        }
        pixgr = pixg2.Rotate(angle, L.L_ROTATE_AREA_MAP,
                             L.L_BRING_IN_BLACK, ws, hs);

            /* Combine into a 4 spp result */
        pixd.SetRGBComponent(pixgr, L.L_ALPHA_CHANNEL);

        return pixd;
    }
}
