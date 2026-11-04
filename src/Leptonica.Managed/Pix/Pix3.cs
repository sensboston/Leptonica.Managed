using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  pix3.c: Masking, logical ops, pixel counting, average and variance of
 *  pixel values, mirrored tiling.
 *
 *  Differences from C:
 *    - Functions that return 2 for "all pixels are filtered out"
 *      (pixAverageInRect, pixAverageInRectRGB) return bool: false means
 *      that no pixel was used and the average (0) is to be disregarded.
 *    - pixPaintSelfThroughMask returns int: 1 when the mask has no fg pixels
 *      (a warning, not an argument error), 0 otherwise.
 *    - pixFindRepCloseTile and pixGetColorNearMaskBoundary return bool and log
 *      an error instead of throwing: pixPaintSelfThroughMask relies on their
 *      failure (the C code ignores or stores the return value and goes on).
 *    - pixMakeAlphaFromMask has two overloads: without %pbox the alpha mask has
 *      the full size of pixs; with %pbox it covers the fg region only.
 *    - pixaCountPixels is a method of Pixa, in this file.
 *    - A NULL pixm in pixSetMaskedGeneral, pixCombineMasked, pixCombineMaskedGeneral,
 *      pixPaintThroughMask and pixPaintSelfThroughMask is a null argument, as in C.
 *
 *  Stubs for callees in other files: pixSetMaskedCmap (paintcmap.c);
 *  pixUnpackBinary, pixConvertRGBToGrayArb, pixConvertRGBToGray (pixconv.c);
 *  pixClipRectangle, pixClipRectangles, pixClipToForeground (pix5.c);
 *  pixGetAverageMaskedRGB, pixGetAverageMasked (pix4.c);
 *  pixThresholdToBinary (grayquant.c);
 *  pixDistanceFunction (seedfill.c); pixMultConstantGray (pixarith.c);
 *  pixDilate, pixDilateBrick (morph.c); pixFlipLR, pixFlipTB (rotateorth.c);
 *  pixConnComp (conncomp.c); pixaGetBox, pixaGetBoxGeometry (pixabasic.c);
 *  pixWriteDebug, pixDisplay (writefile.c); lept_rmdir (utils2.c).
 */

/// <summary>Constants from pix.h and morph.h used by pix3.c.</summary>
public static partial class L
{
    /* Direction flags (morph.h) */
    /// <summary>Horizontal direction.</summary>
    public const int L_HORIZ = 1;
    /// <summary>Vertical direction.</summary>
    public const int L_VERT = 2;
    /// <summary>Both horizontal and vertical directions.</summary>
    public const int L_BOTH_DIRECTIONS = 3;

    /* Grayscale intensity orientation (pix.h) */
    /// <summary>White pixels have the maximum value (0xff or 0xffff) and black pixels are 0.</summary>
    public const int L_WHITE_IS_MAX = 1;
    /// <summary>Black pixels have the maximum value (0xff or 0xffff) and white pixels are 0.</summary>
    public const int L_BLACK_IS_MAX = 2;

    /* Statistical measures (pix.h) */
    /// <summary>Average of the absolute values.</summary>
    public const int L_MEAN_ABSVAL = 1;
    /// <summary>Median value of the set.</summary>
    public const int L_MEDIAN_VAL = 2;
    /// <summary>Mode value of the set.</summary>
    public const int L_MODE_VAL = 3;
    /// <summary>Mode count of the set.</summary>
    public const int L_MODE_COUNT = 4;
    /// <summary>Root mean square of the values.</summary>
    public const int L_ROOT_MEAN_SQUARE = 5;
    /// <summary>Standard deviation from the mean.</summary>
    public const int L_STANDARD_DEVIATION = 6;
    /// <summary>Variance of the values.</summary>
    public const int L_VARIANCE = 7;

    /* Line orientation (pix.h) */
    /// <summary>A horizontal line.</summary>
    public const int L_HORIZONTAL_LINE = 0;
    /* L_POS_SLOPE_LINE = 1 and L_NEG_SLOPE_LINE = 3 are defined in BoxFunc3.cs */
    /// <summary>A vertical line.</summary>
    public const int L_VERTICAL_LINE = 2;
    /// <summary>A line that is neither horizontal nor vertical.</summary>
    public const int L_OBLIQUE_LINE = 4;
}

public sealed partial class Pix
{
    /*-------------------------------------------------------------*
     *                        Masked operations                    *
     *-------------------------------------------------------------*/
    private static readonly uint[] Mask8Lut = BuildMask8Lut();

    private static uint[] BuildMask8Lut()
    {
        var t = new uint[512];
        for (int b = 0; b < 256; b++) {
            uint m0 = 0, m1 = 0;
            for (int p = 0; p < 4; p++) {
                if ((b & (0x80 >> p)) != 0) m0 |= 0xffu << (24 - 8 * p);
                if ((b & (0x08 >> p)) != 0) m1 |= 0xffu << (24 - 8 * p);
            }
            t[2 * b] = m0;
            t[2 * b + 1] = m1;
        }
        return t;
    }
    // leptonica: pixSetMasked (pix3.c)
    /// <summary>Sets each pixel of this pix that co-locates with an ON pixel in pixm to val.</summary>
    /// <param name="pixm">[optional] 1 bpp mask; no operation if null</param>
    /// <param name="val">value to set at each masked pixel</param>
    /// <remarks>
    /// This pix (pixd) is 1, 2, 4, 8, 16 or 32 bpp; or colormapped.
    /// (1) In-place operation.
    /// (2) NOTE: For cmapped images, this calls pixSetMaskedCmap().
    ///     %val must be the 32-bit color representation of the RGB pixel.
    ///     It is not the index into the colormap!
    /// (2) If pixm == NULL, a warning is given.
    /// (3) This is an implicitly aligned operation, where the UL
    ///     corners of pixd and pixm coincide.  A warning is
    ///     issued if the two image sizes differ significantly,
    ///     but the operation proceeds.
    /// (4) Each pixel in pixd that co-locates with an ON pixel
    ///     in pixm is set to the specified input value.
    ///     Other pixels in pixd are not changed.
    /// (5) You can visualize this as painting the color through
    ///     the mask, as a stencil.
    /// (6) If you do not want to have the UL corners aligned,
    ///     use the function pixSetMaskedGeneral(), which requires
    ///     you to input the UL corner of pixm relative to pixd.
    /// (7) Implementation details: see comments in pixPaintThroughMask()
    ///     for when we use rasterop to do the painting.
    /// </remarks>
    public void SetMasked(Pix? pixm, uint val)
    {
        const string func = "pixSetMasked";

        if (pixm == null) {
            Log.Warning(func, "no mask; nothing to do");
            return;
        }
        if (Colormap != null) {
            ExtractRGBValues(val, out int rval, out int gval, out int bval);
            SetMaskedCmap(pixm, 0, 0, rval, gval, bval);
            return;
        }

        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");
        int d = Depth;
        if (d == 1)
            val &= 1;
        else if (d == 2)
            val &= 3;
        else if (d == 4)
            val &= 0x0f;
        else if (d == 8)
            val &= 0xff;
        else if (d == 16)
            val &= 0xffff;
        else if (d != 32)
            throw Lept.Arg(func, "pixd not 1, 2, 4, 8, 16 or 32 bpp");
        pixm.GetDimensions(out int wm, out int hm, out _);

            /* If d == 1, use rasterop; it's about 25x faster */
        if (d == 1) {
            if (val == 0) {
                Pix pixmi = Invert(null, pixm);
                Rasterop(0, 0, wm, hm, L.PIX_MASK, pixmi, 0, 0);
            } else {  /* val == 1 */
                Rasterop(0, 0, wm, hm, L.PIX_PAINT, pixm, 0, 0);
            }
            return;
        }

            /* For d < 32, use rasterop for val == 0 (black); ~3x faster. */
        if (d < 32 && val == 0) {
            Pix pixmd = pixm.UnpackBinary(d, 1);
            Rasterop(0, 0, wm, hm, L.PIX_MASK, pixmd, 0, 0);
            return;
        }

            /* For d < 32, use rasterop for val == maxval (white); ~3x faster. */
        if (d < 32 && val == (uint)((1 << d) - 1)) {
            Pix pixmd = pixm.UnpackBinary(d, 0);
            Rasterop(0, 0, wm, hm, L.PIX_PAINT, pixmd, 0, 0);
            return;
        }

        GetDimensions(out int wd, out int hd, out d);
        int w = Math.Min(wd, wm);
        int h = Math.Min(hd, hm);
        if (Math.Abs(wd - wm) > 7 || Math.Abs(hd - hm) > 7)  /* allow a small tolerance */
            Log.Warning(func, "pixd and pixm sizes differ");

        uint[] datad = Data;
        uint[] datam = pixm.Data;
        int wpld = Wpl;
        int wplm = pixm.Wpl;
        if (d == 8) {
                /* Expand 8 mask bits at a time through a lookup table of
                 * byte masks (pixel 0 in the most significant byte). */
            uint[] lut = Mask8Lut;
            uint rep = val * 0x01010101u;
            int nbytes = (w + 7) >> 3;
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int k = 0; k < nbytes; k++) {
                    uint b = (linem[k >> 2] >> (24 - 8 * (k & 3))) & 0xff;
                    int rem = w - (k << 3);
                    if (rem < 8)
                        b &= 0xffu << (8 - rem);   /* ignore bits beyond the width */
                    if (b == 0)
                        continue;
                    uint m0 = lut[2 * b], m1 = lut[2 * b + 1];
                    int wi = 2 * k;
                    lined[wi] = (lined[wi] & ~m0) | (rep & m0);
                    if (wi + 1 < wpld && rem > 4)
                        lined[wi + 1] = (lined[wi + 1] & ~m1) | (rep & m1);
                }
            }
            return;
        }
        for (int i = 0; i < h; i++) {
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
            for (int j = 0; j < w; j++) {
                if (GetDataBit(linem, j) != 0) {
                    switch (d)
                    {
                    case 2:
                        SetDataDibit(lined, j, (int)val);
                        break;
                    case 4:
                        SetDataQbit(lined, j, (int)val);
                        break;
                    case 8:
                        SetDataByte(lined, j, (int)val);
                        break;
                    case 16:
                        SetDataTwoBytes(lined, j, (int)val);
                        break;
                    case 32:
                        lined[j] = val;
                        break;
                    default:
                        throw Lept.Arg(func, "shouldn't get here");
                    }
                }
            }
        }
    }

    // leptonica: pixSetMaskedGeneral (pix3.c)
    /// <summary>Sets each pixel of this pix under the fg of pixm, placed at (x, y), to val.</summary>
    /// <param name="pixm">[optional] 1 bpp mask; no operation if null</param>
    /// <param name="val">value to set at each masked pixel</param>
    /// <param name="x">location of UL corner of pixm relative to pixd; can be negative</param>
    /// <param name="y">location of UL corner of pixm relative to pixd; can be negative</param>
    /// <remarks>
    /// This pix (pixd) is 8, 16 or 32 bpp.
    /// (1) This is an in-place operation.
    /// (2) Alignment is explicit.  If you want the UL corners of
    ///     the two images to be aligned, use pixSetMasked().
    /// (3) A typical use would be painting through the foreground
    ///     of a small binary mask pixm, located somewhere on a
    ///     larger pixd.  Other pixels in pixd are not changed.
    /// (4) You can visualize this as painting the color through
    ///     the mask, as a stencil.
    /// (5) This uses rasterop to handle clipping and different depths of pixd.
    /// (6) If pixd has a colormap, you should call pixPaintThroughMask().
    /// (7) Why is this function here, if pixPaintThroughMask() does the
    ///     same thing, and does it more generally?  I've retained it here
    ///     to show how one can paint through a mask using only full
    ///     image rasterops, rather than pixel peeking in pixm and poking
    ///     in pixd.  It's somewhat baroque, but I found it amusing.
    /// </remarks>
    public void SetMaskedGeneral(Pix? pixm, uint val, int x, int y)
    {
        const string func = "pixSetMaskedGeneral";

        if (pixm == null)  /* nothing to do */
            return;

        int d = Depth;
        if (d != 8 && d != 16 && d != 32)
            throw Lept.Arg(func, "pixd not 8, 16 or 32 bpp");
        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");

            /* Unpack binary to depth d, with inversion:  1 --> 0, 0 --> 0xff... */
        Pix pixmu = pixm.UnpackBinary(d, 1);

            /* Clear stenciled pixels in pixd */
        pixm.GetDimensions(out int wm, out int hm, out _);
        Rasterop(x, y, wm, hm, L.PIX_SRC & L.PIX_DST, pixmu, 0, 0);

            /* Generate image with requisite color */
        Pix pixc = CreateTemplate(pixmu);
        pixc.SetAllArbitrary(val);

            /* Invert stencil mask, and paint color color into stencil */
        Invert(pixmu, pixmu);
        And(pixmu, pixmu, pixc);

            /* Finally, repaint stenciled pixels, with val, in pixd */
        Rasterop(x, y, wm, hm, L.PIX_SRC | L.PIX_DST, pixmu, 0, 0);
    }

    // leptonica: pixCombineMasked (pix3.c)
    /// <summary>Sets each pixel of this pix under the fg of pixm to the value of pixs.</summary>
    /// <param name="pixs">1 bpp, 8 bpp gray or 32 bpp rgb; no cmap</param>
    /// <param name="pixm">[optional] 1 bpp mask; no operation if null</param>
    /// <remarks>
    /// This pix (pixd) is 1 bpp, 8 bpp gray or 32 bpp rgb; no cmap.
    /// (1) In-place operation; pixd is changed.
    /// (2) This sets each pixel in pixd that co-locates with an ON
    ///     pixel in pixm to the corresponding value of pixs.
    /// (3) pixs and pixd must be the same depth and not colormapped.
    /// (4) All three input pix are aligned at the UL corner, and the
    ///     operation is clipped to the intersection of all three images.
    /// (5) If pixm == NULL, it's a no-op.
    /// (6) Implementation: see notes in pixCombineMaskedGeneral().
    ///     For 8 bpp selective masking, you might guess that it
    ///     would be faster to generate an 8 bpp version of pixm,
    ///     using pixConvert1To8(pixm, 0, 255), and then use a
    ///     general combine operation
    ///          d = (d &amp; ~m) | (s &amp; m)
    ///     on a word-by-word basis.  Not always.  The word-by-word
    ///     combine takes a time that is independent of the mask data.
    ///     If the mask is relatively sparse, the byte-check method
    ///     is actually faster!
    /// </remarks>
    public void CombineMasked(Pix pixs, Pix? pixm)
    {
        const string func = "pixCombineMasked";

        if (pixm == null)  /* nothing to do */
            return;
        GetDimensions(out int w, out int h, out int d);
        pixs.GetDimensions(out int ws, out int hs, out int ds);
        pixm.GetDimensions(out int wm, out int hm, out int dm);
        if (d != ds)
            throw Lept.Arg(func, "pixs and pixd depths differ");
        if (dm != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");
        if (d != 1 && d != 8 && d != 32)
            throw Lept.Arg(func, "pixd not 1, 8 or 32 bpp");
        if (Colormap != null || pixs.Colormap != null)
            throw Lept.Arg(func, "pixs and/or pixd is cmapped");

            /* For d = 1, use rasterop.  pixt is the part from pixs, under
             * the fg of pixm, that is to be combined with pixd.  We also
             * use pixt to remove all fg of pixd that is under the fg of pixm.
             * Then pixt and pixd are combined by ORing. */
        int wmin = Math.Min(w, Math.Min(ws, wm));
        int hmin = Math.Min(h, Math.Min(hs, hm));
        if (d == 1) {
            Pix pixt = And(null, pixs, pixm);
            Rasterop(0, 0, wmin, hmin, L.PIX_DST & L.PIX_NOT(L.PIX_SRC),
                     pixm, 0, 0);
            Rasterop(0, 0, wmin, hmin, L.PIX_SRC | L.PIX_DST, pixt, 0, 0);
            return;
        }

        uint[] data = Data;
        uint[] datas = pixs.Data;
        uint[] datam = pixm.Data;
        int wpl = Wpl;
        int wpls = pixs.Wpl;
        int wplm = pixm.Wpl;
        if (d == 8) {
            for (int i = 0; i < hmin; i++) {
                Span<uint> line = data.AsSpan(i * wpl, wpl);
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
                for (int j = 0; j < wmin; j++) {
                    if (GetDataBit(linem, j) != 0) {
                        int val = GetDataByte(lines, j);
                        SetDataByte(line, j, val);
                    }
                }
            }
        } else {  /* d == 32 */
            for (int i = 0; i < hmin; i++) {
                Span<uint> line = data.AsSpan(i * wpl, wpl);
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
                for (int j = 0; j < wmin; j++) {
                    if (GetDataBit(linem, j) != 0)
                        line[j] = lines[j];
                }
            }
        }
    }

    // leptonica: pixCombineMaskedGeneral (pix3.c)
    /// <summary>Sets each pixel of this pix under the fg of pixm to the value of pixs,
    /// with the origin of pixs and pixm at (x, y) of this pix.</summary>
    /// <param name="pixs">1 bpp, 8 bpp gray or 32 bpp rgb</param>
    /// <param name="pixm">[optional] 1 bpp mask</param>
    /// <param name="x">origin of pixs and pixm relative to pixd; can be negative</param>
    /// <param name="y">origin of pixs and pixm relative to pixd; can be negative</param>
    /// <remarks>
    /// This pix (pixd) is 1 bpp, 8 bpp gray or 32 bpp rgb.
    /// (1) In-place operation; pixd is changed.
    /// (2) This is a generalized version of pixCombinedMasked(), where
    ///     the source and mask can be placed at the same (arbitrary)
    ///     location relative to pixd.
    /// (3) pixs and pixd must be the same depth and not colormapped.
    /// (4) The UL corners of both pixs and pixm are aligned with
    ///     the point (x, y) of pixd, and the operation is clipped to
    ///     the intersection of all three images.
    /// (5) If pixm == NULL, it's a no-op.
    /// (6) Implementation.  There are two ways to do these.  In the first,
    ///     we use rasterop, ORing the part of pixs under the mask
    ///     with pixd (which has been appropriately cleared there first).
    ///     In the second, the mask is used one pixel at a time to
    ///     selectively replace pixels of pixd with those of pixs.
    ///     Here, we use rasterop for 1 bpp and pixel-wise replacement
    ///     for 8 and 32 bpp.  To use rasterop for 8 bpp, for example,
    ///     we must first generate an 8 bpp version of the mask.
    ///     The code is simple:
    ///
    ///        Pix *pixm8 = pixConvert1To8(NULL, pixm, 0, 255);
    ///        Pix *pixt = pixAnd(NULL, pixs, pixm8);
    ///        pixRasterop(pixd, x, y, wmin, hmin, PIX_DST &amp; PIX_NOT(PIX_SRC),
    ///                    pixm8, 0, 0);
    ///        pixRasterop(pixd, x, y, wmin, hmin, PIX_SRC | PIX_DST,
    ///                    pixt, 0, 0);
    ///        pixDestroy(&amp;pixt);
    ///        pixDestroy(&amp;pixm8);
    /// </remarks>
    public void CombineMaskedGeneral(Pix pixs, Pix? pixm, int x, int y)
    {
        const string func = "pixCombineMaskedGeneral";

        if (pixm == null)  /* nothing to do */
            return;
        GetDimensions(out int w, out int h, out int d);
        pixs.GetDimensions(out int ws, out int hs, out int ds);
        pixm.GetDimensions(out int wm, out int hm, out int dm);
        if (d != ds)
            throw Lept.Arg(func, "pixs and pixd depths differ");
        if (dm != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");
        if (d != 1 && d != 8 && d != 32)
            throw Lept.Arg(func, "pixd not 1, 8 or 32 bpp");
        if (Colormap != null || pixs.Colormap != null)
            throw Lept.Arg(func, "pixs and/or pixd is cmapped");

            /* For d = 1, use rasterop.  pixt is the part from pixs, under
             * the fg of pixm, that is to be combined with pixd.  We also
             * use pixt to remove all fg of pixd that is under the fg of pixm.
             * Then pixt and pixd are combined by ORing. */
        int wmin = Math.Min(ws, wm);
        int hmin = Math.Min(hs, hm);
        if (d == 1) {
            Pix pixt = And(null, pixs, pixm);
            Rasterop(x, y, wmin, hmin, L.PIX_DST & L.PIX_NOT(L.PIX_SRC),
                     pixm, 0, 0);
            Rasterop(x, y, wmin, hmin, L.PIX_SRC | L.PIX_DST, pixt, 0, 0);
            return;
        }

        int wpl = Wpl;
        uint[] data = Data;
        int wpls = pixs.Wpl;
        uint[] datas = pixs.Data;
        int wplm = pixm.Wpl;
        uint[] datam = pixm.Data;

        for (int i = 0; i < hmin; i++) {
            if (y + i < 0 || y + i >= h) continue;
            Span<uint> line = data.AsSpan((y + i) * wpl, wpl);
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
            for (int j = 0; j < wmin; j++) {
                if (x + j < 0 || x + j >= w) continue;
                if (GetDataBit(linem, j) != 0) {
                    switch (d)
                    {
                    case 8:
                        int val = GetDataByte(lines, j);
                        SetDataByte(line, x + j, val);
                        break;
                    case 32:
                        line[x + j] = lines[j];
                        break;
                    default:
                        throw Lept.Arg(func, "shouldn't get here");
                    }
                }
            }
        }
    }

    // leptonica: pixPaintThroughMask (pix3.c)
    /// <summary>Sets each pixel of this pix under the fg of pixm, placed at (x, y), to val.</summary>
    /// <param name="pixm">[optional] 1 bpp mask</param>
    /// <param name="x">origin of pixm relative to pixd; can be negative</param>
    /// <param name="y">origin of pixm relative to pixd; can be negative</param>
    /// <param name="val">pixel value to set at each masked pixel</param>
    /// <remarks>
    /// This pix (pixd) is 1, 2, 4, 8, 16 or 32 bpp; or colormapped.
    /// (1) In-place operation.  Calls pixSetMaskedCmap() for colormapped
    ///     images.
    /// (2) For 1, 2, 4, 8 and 16 bpp gray, we take the appropriate
    ///     number of least significant bits of val.
    /// (3) If pixm == NULL, it's a no-op.
    /// (4) The mask origin is placed at (x,y) on pixd, and the
    ///     operation is clipped to the intersection of rectangles.
    /// (5) For rgb, the components in val are in the canonical locations,
    ///     with red in location COLOR_RED, etc.
    /// (6) Implementation detail 1:
    ///     For painting with val == 0 or val == maxval, you can use rasterop.
    ///     If val == 0, invert the mask so that it's 0 over the region
    ///     into which you want to write, and use PIX_SRC &amp; PIX_DST to
    ///     clear those pixels.  To write with val = maxval (all 1's),
    ///     use PIX_SRC | PIX_DST to set all bits under the mask.
    /// (7) Implementation detail 2:
    ///     The rasterop trick can be used for depth > 1 as well.
    ///     For val == 0, generate the mask for depth d from the binary
    ///     mask using
    ///         pixmd = pixUnpackBinary(pixm, d, 1);
    ///     and use pixRasterop() with PIX_MASK.  For val == maxval,
    ///         pixmd = pixUnpackBinary(pixm, d, 0);
    ///     and use pixRasterop() with PIX_PAINT.
    ///     But note that if d == 32 bpp, it is about 3x faster to use
    ///     the general implementation (not pixRasterop()).
    /// (8) Implementation detail 3:
    ///     It might be expected that the switch in the inner loop will
    ///     cause large branching delays and should be avoided.
    ///     This is not the case, because the entrance is always the
    ///     same and the compiler can correctly predict the jump.
    /// </remarks>
    public void PaintThroughMask(Pix? pixm, int x, int y, uint val)
    {
        const string func = "pixPaintThroughMask";

        if (pixm == null)  /* nothing to do */
            return;
        if (Colormap != null) {
            ExtractRGBValues(val, out int rval, out int gval, out int bval);
            SetMaskedCmap(pixm, x, y, rval, gval, bval);
            return;
        }

        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");
        int d = Depth;
        if (d == 1)
            val &= 1;
        else if (d == 2)
            val &= 3;
        else if (d == 4)
            val &= 0x0f;
        else if (d == 8)
            val &= 0xff;
        else if (d == 16)
            val &= 0xffff;
        else if (d != 32)
            throw Lept.Arg(func, "pixd not 1, 2, 4, 8, 16 or 32 bpp");
        pixm.GetDimensions(out int wm, out int hm, out _);

            /* If d == 1, use rasterop; it's about 25x faster. */
        if (d == 1) {
            if (val == 0) {
                Pix pixmi = Invert(null, pixm);
                Rasterop(x, y, wm, hm, L.PIX_MASK, pixmi, 0, 0);
            } else {  /* val == 1 */
                Rasterop(x, y, wm, hm, L.PIX_PAINT, pixm, 0, 0);
            }
            return;
        }

            /* For d < 32, use rasterop if val == 0 (black); ~3x faster. */
        if (d < 32 && val == 0) {
            Pix pixmd = pixm.UnpackBinary(d, 1);
            Rasterop(x, y, wm, hm, L.PIX_MASK, pixmd, 0, 0);
            return;
        }

            /* For d < 32, use rasterop if val == maxval (white); ~3x faster. */
        if (d < 32 && val == (uint)((1 << d) - 1)) {
            Pix pixmd = pixm.UnpackBinary(d, 0);
            Rasterop(x, y, wm, hm, L.PIX_PAINT, pixmd, 0, 0);
            return;
        }

            /* All other cases */
        GetDimensions(out int w, out int h, out _);
        int wpl = Wpl;
        uint[] data = Data;
        int wplm = pixm.Wpl;
        uint[] datam = pixm.Data;
        for (int i = 0; i < hm; i++) {
            if (y + i < 0 || y + i >= h) continue;
            Span<uint> line = data.AsSpan((y + i) * wpl, wpl);
            ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
            for (int j = 0; j < wm; j++) {
                if (x + j < 0 || x + j >= w) continue;
                if (GetDataBit(linem, j) != 0) {
                    switch (d)
                    {
                    case 2:
                        SetDataDibit(line, x + j, (int)val);
                        break;
                    case 4:
                        SetDataQbit(line, x + j, (int)val);
                        break;
                    case 8:
                        SetDataByte(line, x + j, (int)val);
                        break;
                    case 16:
                        SetDataTwoBytes(line, x + j, (int)val);
                        break;
                    case 32:
                        line[x + j] = val;
                        break;
                    default:
                        throw Lept.Arg(func, "shouldn't get here");
                    }
                }
            }
        }
    }

    // leptonica: pixCopyWithBoxa (pix3.c)
    /// <summary>Copies the parts of this pix inside the boxes of boxa into a new pix.</summary>
    /// <param name="boxa">e.g., from components of a photomask</param>
    /// <param name="background">L_SET_WHITE or L_SET_BLACK</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// This pix (pixs) is of all depths; cmap ok.
    /// (1) Pixels from pixs are copied ("blitted") through each box into pixd.
    /// (2) Pixels not copied are preset to either white or black.
    /// (3) This fast and simple implementation can use rasterop because
    ///     each region to be copied is rectangular.
    /// (4) A much slower implementation that doesn't use rasterop would make
    ///     a 1 bpp mask from the boxa and then copy, pixel by pixel,
    ///     through the mask:
    ///        pixGetDimensions(pixs, &amp;w, &amp;h, NULL);
    ///        pixm = pixCreate(w, h, 1);
    ///        pixm = pixMaskBoxa(pixm, pixm, boxa);
    ///        pixd = pixCreateTemplate(pixs);
    ///        pixSetBlackOrWhite(pixd, background);
    ///        pixCombineMasked(pixd, pixs, pixm);
    ///        pixDestroy(&amp;pixm);
    /// </remarks>
    public Pix CopyWithBoxa(Boxa boxa, int background)
    {
        if (background != L.L_SET_WHITE && background != L.L_SET_BLACK)
            throw Lept.Arg("pixCopyWithBoxa", "invalid background");

        Pix pixd = CreateTemplate(this);
        pixd.SetBlackOrWhite(background);
        int n = boxa.Count;
        for (int i = 0; i < n; i++) {
            boxa.GetBoxGeometry(i, out int x, out int y, out int w, out int h);
            pixd.Rasterop(x, y, w, h, L.PIX_SRC, this, x, y);
        }
        return pixd;
    }

    // leptonica: pixPaintSelfThroughMask (pix3.c)
    /// <summary>Paints this pix through the fg of pixm with tiles taken from near the mask components.</summary>
    /// <param name="pixm">[optional] 1 bpp mask</param>
    /// <param name="x">origin of pixm relative to pixd; must not be negative</param>
    /// <param name="y">origin of pixm relative to pixd; must not be negative</param>
    /// <param name="searchdir">L_HORIZ, L_VERT or L_BOTH_DIRECTIONS</param>
    /// <param name="mindist">min distance of nearest tile edge to box; >= 0</param>
    /// <param name="tilesize">requested size for tiling; may be reduced</param>
    /// <param name="ntiles">number of tiles tested in each row/column</param>
    /// <param name="distblend">distance outside the fg used for blending with pixs</param>
    /// <returns>0 if OK; 1 if the mask has no fg pixels (a warning is logged)</returns>
    /// <remarks>
    /// This pix (pixd) is 8 bpp gray or 32 bpp rgb; not colormapped.
    /// (1) In-place operation; pixd is changed.
    /// (2) If pixm == NULL, it's a no-op.
    /// (3) The mask origin is placed at (x,y) on pixd, and the
    ///     operation is clipped to the intersection of pixd and the
    ///     fg of the mask.
    /// (4) %tsize is the the requested size for tiling.  The actual
    ///     actual size for each c.c. will be bounded by the minimum
    ///     dimension of the c.c.
    /// (5) For %mindist, %searchdir and %ntiles, see pixFindRepCloseTile().
    ///     They determine the set of possible tiles that can be used
    ///     to build a larger mirrored tile to paint onto pixd through
    ///     the c.c. of pixm.
    /// (6) %distblend is used for alpha blending.  It is only applied
    ///     if there is exactly one c.c. in the mask.  Use distblend == 0
    ///     to skip blending and just paint through the 1 bpp mask.
    /// (7) To apply blending to more than 1 component, call this function
    ///     repeatedly with %pixm, %x and %y representing one component of
    ///     the mask each time.  This would be done as follows, for an
    ///     underlying image pixs and mask pixm of components to fill:
    ///         Boxa *boxa = pixConnComp(pixm, &amp;pixa, 8);
    ///         n = boxaGetCount(boxa);
    ///         for (i = 0; i &lt; n; i++) {
    ///             Pix *pix = pixaGetPix(pixa, i, L_CLONE);
    ///             Box *box = pixaGetBox(pixa, i, L_CLONE);
    ///             boxGetGeometry(box, &amp;bx, &amp;by, &amp;bw, &amp;bh);
    ///             pixPaintSelfThroughMask(pixs, pix, bx, by, searchdir,
    ///                                mindist, tilesize, ntiles, distblend);
    ///             pixDestroy(&amp;pix);
    ///             boxDestroy(&amp;box);
    ///         }
    ///         pixaDestroy(&amp;pixa);
    ///         boxaDestroy(&amp;boxa);
    /// (8) If no tiles can be found, this falls back to estimating the
    ///     color near the boundary of the region to be textured.
    /// (9) This can be used to replace the pixels in some regions of
    ///     an image by selected neighboring pixels.  The mask represents
    ///     the pixels to be replaced.  For each connected component in
    ///     the mask, this function selects up to two tiles of neighboring
    ///     pixels to be used for replacement of pixels represented by
    ///     the component (i.e., under the FG of that component in the mask).
    ///     After selection, mirror replication is used to generate an
    ///     image that is large enough to cover the component.  Alpha
    ///     blending can also be used outside of the component, but near the
    ///     edge, to blur the transition between painted and original pixels.
    /// </remarks>
    public int PaintSelfThroughMask(Pix? pixm, int x, int y, int searchdir, int mindist,
                                    int tilesize, int ntiles, int distblend)
    {
        const string func = "pixPaintSelfThroughMask";

        if (pixm == null)  /* nothing to do */
            return 0;
        if (Colormap != null)
            throw Lept.Arg(func, "pixd has colormap");
        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixd not 8 or 32 bpp");
        pixm.GetDimensions(out int wm, out int hm, out int dm);
        if (dm != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");
        if (x < 0 || y < 0)
            throw Lept.Arg(func, "x and y must be non-negative");
        if (searchdir != L.L_HORIZ && searchdir != L.L_VERT &&
            searchdir != L.L_BOTH_DIRECTIONS)
            throw Lept.Arg(func, "invalid searchdir");
        if (tilesize < 2)
            throw Lept.Arg(func, "tilesize must be >= 2");
        if (distblend < 0)
            throw Lept.Arg(func, "distblend must be >= 0");

            /* Embed mask in full sized mask */
        Pix pixf;
        if (wm < w || hm < h) {
            pixf = Create(w, h, 1);
            pixf.Rasterop(x, y, wm, hm, L.PIX_SRC, pixm, 0, 0);
        } else {
            pixf = Copy(null, pixm);
        }

            /* Get connected components of mask */
        Boxa boxa = pixf.ConnComp(out Pixa pixa, 8);
        int n = pixa.Count;
        if (n == 0) {
            Log.Warning(func, "no fg in mask");
            return 1;
        }

            /* For each c.c., generate one or two representative tiles for
             * texturizing and apply through the mask.  The input 'tilesize'
             * is the requested value.  Note that if there is exactly one
             * component, and blending at the edge is requested, an alpha mask
             * is generated, which is larger than the bounding box of the c.c. */
        int edgeblend = (n == 1 && distblend > 0) ? 1 : 0;
        if (distblend > 0 && n > 1)
            Log.Warning(func, $"{n} components; can not blend at edges");
        int retval = 0;
        for (int i = 0; i < n; i++) {
            Pix pix1;
            Box box;
            if (edgeblend != 0) {
                pix1 = pixf.MakeAlphaFromMask(distblend, out Box? alphabox);
                box = alphabox ?? throw Lept.Fail(func, "alpha box not made");
            } else {
                pix1 = pixa.GetPix(i, L.L_CLONE);
                box = pixa.GetBox(i, L.L_CLONE) ?? throw Lept.Arg(func, "box not defined");
            }
            box.GetGeometry(out int bx, out int by, out int bw, out int bh);
            int minside = Math.Min(bw, bh);

            Box? boxh = null;
            Box? boxv = null;
            if (searchdir == L.L_HORIZ || searchdir == L.L_BOTH_DIRECTIONS) {
                FindRepCloseTile(box, L.L_HORIZ, mindist,
                                 Math.Min(minside, tilesize), ntiles, out boxh, 0);
            }
            if (searchdir == L.L_VERT || searchdir == L.L_BOTH_DIRECTIONS) {
                FindRepCloseTile(box, L.L_VERT, mindist,
                                 Math.Min(minside, tilesize), ntiles, out boxv, 0);
            }
            if (boxh == null && boxv == null) {
                Log.Warning(func, "tile region not selected; paint color near boundary");
                pix1 = pixa.GetPix(i, L.L_CLONE);
                pixa.GetBoxGeometry(i, out bx, out by, out _, out _);
                retval = GetColorNearMaskBoundary(pixm, box, distblend,
                                                  out uint pixval, 0) ? 0 : 1;
                SetMaskedGeneral(pix1, pixval, bx, by);
                continue;
            }

                /* Extract the selected squares from pixd */
            Pix? pixh = (boxh != null) ? ClipRectangle(boxh, out _) : null;
            Pix? pixv = (boxv != null) ? ClipRectangle(boxv, out _) : null;
            Pix pix2;
            if (pixh != null && pixv != null)
                pix2 = pixh.Blend(pixv, 0, 0, 0.5f) ?? throw Lept.Fail(func, "pix2 not made");
            else if (pixh != null)
                pix2 = Clone(pixh);
            else  /* pixv */
                pix2 = Clone(pixv!);

                /* Generate an image the size of the b.b. of the c.c.,
                 * possibly extended by the blending distance, which
                 * is then either painted through the c.c. mask or
                 * blended using the alpha mask for that c.c.  */
            Pix pix3 = pix2.MirroredTiling(bw, bh);
            if (edgeblend != 0) {
                Pix pix4 = ClipRectangle(box, out _) ?? throw Lept.Fail(func, "pix4 not made");
                Pix pix5 = pix4.BlendWithGrayMask(pix3, pix1, 0, 0);
                Rasterop(bx, by, bw, bh, L.PIX_SRC, pix5, 0, 0);
            } else {
                CombineMaskedGeneral(pix3, pix1, bx, by);
            }
        }

        return retval;
    }

    // leptonica: pixMakeMaskFromVal (pix3.c)
    /// <summary>Generates a 1 bpp mask with a 1 for each pixel of this pix that has the value val.</summary>
    /// <param name="val">pixel value</param>
    /// <returns>pixd 1 bpp mask</returns>
    /// <remarks>
    /// This pix (pixs) is 2, 4 or 8 bpp; can be colormapped.
    /// (1) This generates a 1 bpp mask image, where a 1 is written in
    ///     the mask for each pixel in pixs that has a value %val.
    /// (2) If no pixels have the value, an empty mask is generated.
    /// </remarks>
    public Pix MakeMaskFromVal(int val)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg("pixMakeMaskFromVal", "pix not 2, 4 or 8 bpp");

        Pix pixd = Create(w, h, 1);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpls = Wpl;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                int sval;
                if (d == 2)
                    sval = GetDataDibit(lines, j);
                else if (d == 4)
                    sval = GetDataQbit(lines, j);
                else  /* d == 8 */
                    sval = GetDataByte(lines, j);
                if (sval == val)
                    SetDataBit(lined, j);
            }
        }

        return pixd;
    }

    // leptonica: pixMakeMaskFromLUT (pix3.c)
    /// <summary>Generates a 1 bpp mask with a 1 for each pixel of this pix that has a 1 in the LUT.</summary>
    /// <param name="tab">256-entry LUT; 1 means to write to mask</param>
    /// <returns>pixd 1 bpp mask</returns>
    /// <remarks>
    /// This pix (pixs) is 2, 4 or 8 bpp; can be colormapped.
    /// (1) This generates a 1 bpp mask image, where a 1 is written in
    ///     the mask for each pixel in pixs that has a value corresponding
    ///     to a 1 in the LUT.
    /// (2) The LUT should be of size 256.
    /// </remarks>
    public Pix MakeMaskFromLUT(int[] tab)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg("pixMakeMaskFromLUT", "pix not 2, 4 or 8 bpp");

        Pix pixd = Create(w, h, 1);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpls = Wpl;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                int val;
                if (d == 2)
                    val = GetDataDibit(lines, j);
                else if (d == 4)
                    val = GetDataQbit(lines, j);
                else  /* d == 8 */
                    val = GetDataByte(lines, j);
                if (tab[val] == 1)
                    SetDataBit(lined, j);
            }
        }

        return pixd;
    }

    // leptonica: pixMakeArbMaskFromRGB (pix3.c)
    /// <summary>Generates a 1 bpp mask where rc * rval + gc * gval + bc * bval > thresh.</summary>
    /// <param name="rc">arithmetic factors; can be negative</param>
    /// <param name="gc">arithmetic factors; can be negative</param>
    /// <param name="bc">arithmetic factors; can be negative</param>
    /// <param name="thresh">lower threshold on weighted sum of components</param>
    /// <returns>pixd 1 bpp mask</returns>
    /// <remarks>
    /// This pix (pixs) is 32 bpp RGB.
    /// (1) This generates a 1 bpp mask image, where a 1 is written in
    ///     the mask for each pixel in pixs that satisfies
    ///          rc * rval + gc * gval + bc * bval > thresh
    ///     where rval is the red component, etc.
    /// (2) Unlike with pixConvertToGray(), there are no constraints
    ///     on the color coefficients, which can be negative.  For
    ///     example, a mask that discriminates against red and in favor
    ///     of blue will have rc &lt; 0.0 and bc > 0.0.
    /// (3) To make the result independent of intensity (the 'V' in HSV),
    ///     select coefficients so that %thresh = 0.  Then the result
    ///     is not changed when all components are multiplied by the
    ///     same constant (as long as nothing saturates).  This can be
    ///     useful if, for example, the illumination is not uniform.
    /// </remarks>
    public Pix MakeArbMaskFromRGB(float rc, float gc, float bc, float thresh)
    {
        if (Depth != 32)
            throw Lept.Arg("pixMakeArbMaskFromRGB", "pixs undefined or not 32 bpp");
        if (thresh >= 255.0) thresh = 254.0f;  /* avoid 8 bit overflow */

        Pix pix1 = ConvertRGBToGrayArb(rc, gc, bc);
        Pix pix2 = pix1.ThresholdToBinary((int)(thresh + 1));
        Invert(pix2, pix2);
        return pix2;
    }

    // leptonica: pixSetUnderTransparency (pix3.c)
    /// <summary>Sets the r, g and b components under every fully transparent alpha component to val.</summary>
    /// <param name="val">32 bit unsigned color to use where alpha == 0</param>
    /// <param name="debug">displays layers of pixs</param>
    /// <returns>pixd 32 bpp rgba</returns>
    /// <remarks>
    /// This pix (pixs) is 32 bpp rgba.
    /// (1) This sets the r, g and b components under every fully
    ///     transparent alpha component to %val.  The alpha components
    ///     are unchanged.
    /// (2) Full transparency is denoted by alpha == 0.  Setting
    ///     all pixels to a constant %val where alpha is transparent
    ///     can improve compressibility by reducing the entropy.
    /// (3) The visual result depends on how the image is displayed.
    ///     (a) For display devices that respect the use of the alpha
    ///         layer, this will not affect the appearance.
    ///     (b) For typical leptonica operations, alpha is ignored,
    ///         so there will be a change in appearance because this
    ///         resets the rgb values in the fully transparent region.
    /// (4) pixRead() and pixWrite() will, by default, read and write
    ///     4-component (rgba) pix in png format.  To ignore the alpha
    ///     component after reading, or omit it on writing, pixSetSpp(..., 3).
    /// (5) Here are some examples:
    ///     * To convert all fully transparent pixels in a 4 component
    ///       (rgba) png file to white:
    ///         pixs = pixRead(&lt;infile>);
    ///         pixd = pixSetUnderTransparency(pixs, 0xffffff00, 0);
    ///     * To write pixd with the alpha component:
    ///         pixWrite(&lt;outfile>, pixd, IFF_PNG);
    ///     * To write and rgba image without the alpha component, first do:
    ///         pixSetSpp(pixd, 3);
    ///       If you later want to use the alpha, spp must be reset to 4.
    ///     * (fancier) To remove the alpha by blending the image over
    ///       a white background:
    ///         pixRemoveAlpha()
    ///       This changes all pixel values where the alpha component is
    ///       not opaque (255).
    /// (6) Caution.  rgb images in leptonica typically have value 0 in
    ///     the alpha channel, which is fully transparent.  If spp for
    ///     such an image were changed from 3 to 4, the image becomes
    ///     fully transparent, and this function will set each pixel to %val.
    ///     If you really want to set every pixel to the same value,
    ///     use pixSetAllArbitrary().
    /// (7) This is useful for compressing an RGBA image where the part
    ///     of the image that is fully transparent is random junk; compression
    ///     is typically improved by setting that region to a constant.
    ///     For rendering as a 3 component RGB image over a uniform
    ///     background of arbitrary color, use pixAlphaBlendUniform().
    /// </remarks>
    public Pix SetUnderTransparency(uint val, int debug)
    {
        if (Depth != 32)
            throw Lept.Arg("pixSetUnderTransparency", "pixs not defined or not 32 bpp");

        if (Spp != 4) {
            Log.Warning("pixSetUnderTransparency", "no alpha channel; returning a copy");
            return Copy(null, this);
        }

            /* Make a mask from the alpha component with ON pixels
             * wherever the alpha component is fully transparent (0).
             * The hard way:
             *     l_int32 *lut = (l_int32 *)LEPT_CALLOC(256, sizeof(l_int32));
             *     lut[0] = 1;
             *     pixg = pixGetRGBComponent(pixs, L_ALPHA_CHANNEL);
             *     pixm = pixMakeMaskFromLUT(pixg, lut);
             *     LEPT_FREE(lut);
             * But there's an easier way to set pixels in a mask where
             * the alpha component is 0 ...  */
        Pix pixg = GetRGBComponent(L.L_ALPHA_CHANNEL);
        Pix pixm = pixg.ThresholdToBinary(1);

        if (debug != 0) {
            Pix pixt = DisplayLayersRGBA(0xffffff00, 600);
            pixt.Display(0, 0);
        }

        Pix pixd = Copy(null, this);
        pixd.SetMasked(pixm, (val & 0xffffff00));
        return pixd;
    }

    // leptonica: pixMakeAlphaFromMask (pix3.c)
    /// <summary>Generates an 8 bpp alpha layer of the full size of this 1 bpp mask,
    /// opaque over the fg and decaying linearly to transparent over the distance dist.</summary>
    /// <param name="dist">blending distance; typically 10 - 30</param>
    /// <returns>pixd 8 bpp gray</returns>
    /// <remarks>See <see cref="MakeAlphaFromMask(int, out Box?)"/>; this is the case where %pbox is NULL.</remarks>
    public Pix MakeAlphaFromMask(int dist)
    {
        return makeAlphaFromMask(dist, false, out _);
    }

    /// <summary>Generates an 8 bpp alpha layer from this 1 bpp mask, opaque over the fg
    /// and decaying linearly to transparent over the distance dist.</summary>
    /// <param name="dist">blending distance; typically 10 - 30</param>
    /// <param name="pbox">location of the returned mask relative to pixs;
    /// null if there are no ON pixels in pixs</param>
    /// <returns>pixd 8 bpp gray</returns>
    /// <remarks>
    /// This pix (pixs) is 1 bpp.
    /// (1) This generates a 8 bpp alpha layer that is opaque (256)
    ///     over the FG of pixs, and goes transparent linearly away
    ///     from the FG pixels, decaying to 0 (transparent) is an
    ///     8-connected distance given by %dist.  If %dist == 0,
    ///     this does a simple conversion from 1 to 8 bpp.
    /// (2) If &amp;box == NULL, this returns an alpha mask that is the
    ///     full size of pixs.  Otherwise, the returned mask pixd covers
    ///     just the FG pixels of pixs, expanded by %dist in each
    ///     direction (if possible), and the returned box gives the
    ///     location of the returned mask relative to pixs.
    /// (3) This is useful for painting through a mask and allowing
    ///     blending of the painted image with an underlying image
    ///     in the mask background for pixels near foreground mask pixels.
    ///     For example, with an underlying rgb image pix1, an overlaying
    ///     image rgb pix2, binary mask pixm, and dist > 0, this
    ///     blending is achieved with:
    ///         pix3 = pixMakeAlphaFromMask(pixm, dist, &amp;box);
    ///         boxGetGeometry(box, &amp;x, &amp;y, NULL, NULL);
    ///         pix4 = pixBlendWithGrayMask(pix1, pix2, pix3, x, y);
    /// </remarks>
    public Pix MakeAlphaFromMask(int dist, out Box? pbox)
    {
        return makeAlphaFromMask(dist, true, out pbox);
    }

    private Pix makeAlphaFromMask(int dist, bool wantbox, out Box? pbox)
    {
        const string func = "pixMakeAlphaFromMask";

        pbox = null;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (dist < 0)
            throw Lept.Arg(func, "dist must be >= 0");

            /* If requested, extract just the region to be affected by the mask */
        Pix pix1;
        if (wantbox) {
            ClipToForeground(out _, out Box? box1);
            if (box1 == null) {
                Log.Warning(func, "no ON pixels in mask");
                return CreateTemplate(this);  /* all background (0) */
            }

            Box.AdjustSides(box1, box1, -dist, dist, -dist, dist);
            GetDimensions(out int w, out int h, out _);
            Box box2 = box1.ClipToRectangle(w, h);
            pbox = box2;
            pix1 = ClipRectangle(box2, out _) ?? throw Lept.Fail(func, "pix1 not made");
        } else {
            pix1 = Copy(null, this);
        }

        if (dist == 0) {
            Pix pixd0 = Convert1To8(null, pix1, 0, 255);
            return pixd0;
        }

            /* Blur the boundary of the input mask */
        Invert(pix1, pix1);
        Pix pixd = pix1.DistanceFunction(8, 8, L.L_BOUNDARY_FG);
        pixd.MultConstantGray(256.0f / dist);
        Invert(pixd, pixd);
        return pixd;
    }

    // leptonica: pixGetColorNearMaskBoundary (pix3.c)
    /// <summary>Finds the average color in a set of pixels that are roughly a distance dist
    /// from the c.c. boundary and in the background of the mask image.</summary>
    /// <param name="pixm">1 bpp mask, full image</param>
    /// <param name="box">region of mask; typically b.b. of a component</param>
    /// <param name="dist">distance into BG from mask boundary to use</param>
    /// <param name="pval">average pixel value; white (0xffffff00) if no pixels are found or on error</param>
    /// <param name="debug">1 to output mask images</param>
    /// <returns>true if OK; false on error (the C function returns 1; an error is logged)</returns>
    /// <remarks>
    /// This pix (pixs) is 32 bpp rgb.
    /// (1) This finds the average color in a set of pixels that are
    ///     roughly a distance %dist from the c.c. boundary and in the
    ///     background of the mask image.
    /// </remarks>
    public bool GetColorNearMaskBoundary(Pix pixm, Box box, int dist, out uint pval, int debug)
    {
        const string func = "pixGetColorNearMaskBoundary";

        pval = 0xffffff00;  /* white */
        if (Depth != 32) {
            Log.Error(func, "pixs undefined or not 32 bpp");
            return false;
        }
        if (pixm.Depth != 1) {
            Log.Error(func, "pixm undefined or not 1 bpp");
            return false;
        }
        if (dist < 0) {
            Log.Error(func, "dist must be >= 0");
            return false;
        }

            /* Clip mask piece, expanded beyond %box by (%dist + 5) on each side.
             * box1 is the region requested; box2 is the actual region retrieved,
             * which is clipped to %pixm */
        Box box1 = Box.AdjustSides(null, box, -dist - 5, dist + 5, -dist - 5, dist + 5);
        Pix? pix1 = pixm.ClipRectangle(box1, out Box? box2);
        if (pix1 == null || box2 == null) {
            Log.Error(func, "mask region not clipped");
            return false;
        }

            /* Expand FG by %dist into the BG */
        Pix pix2;
        if (dist == 0) {
            pix2 = Copy(null, pix1);
        } else {
            string op = CFormat.Sprintf("d%d.%d", 2 * dist, 2 * dist);
            pix2 = pix1.MorphSequence(op, 0);
        }

            /* Expand again by 5 pixels on all sides (dilate 11x11) and XOR,
             * getting the annulus of FG pixels between %dist and %dist + 5 */
        Pix pix3 = Copy(null, pix2);
        DilateBrick(pix3, pix3, 11, 11);
        Xor(pix3, pix3, pix2);
        bool empty = pix3.Zero();
        if (!empty) {
                /* Scan the same region in %pixs, to get average under FG in pix3 */
            box2.GetGeometry(out int bx, out int by, out _, out _);
            GetAverageMaskedRGB(pix3, bx, by, 1, L.L_MEAN_ABSVAL,
                                out float rval, out float gval, out float bval);
            ComposeRGBPixel((int)(rval + 0.5), (int)(gval + 0.5),
                            (int)(bval + 0.5), out pval);
        } else {
            Log.Warning(func, "no pixels found");
        }

        if (debug != 0) {
            Utils.LeptRmdir("masknear");  /* erase previous images */
            Utils.LeptMkdir("masknear");
            pix1.WriteDebug("/tmp/masknear/input.png", L.IFF_PNG);
            pix2.WriteDebug("/tmp/masknear/adjusted.png", L.IFF_PNG);
            pix3.WriteDebug("/tmp/masknear/outerfive.png", L.IFF_PNG);
            CStdio.Stderr($"Input box; with adjusted sides; clipped\n");
            box.PrintStreamInfo(null);
            box1.PrintStreamInfo(null);
            box2.PrintStreamInfo(null);
        }

        return true;
    }

    // leptonica: pixDisplaySelectedPixels (pix3.c)
    /// <summary>Paints the pattern sel in color val at every fg pixel of pixm, on a copy of pixs.</summary>
    /// <param name="pixs">[optional] any depth</param>
    /// <param name="pixm">1 bpp mask, aligned UL corner with %pixs</param>
    /// <param name="sel">[optional] pattern to paint at each pixel in pixm</param>
    /// <param name="val">rgb rendering of pattern</param>
    /// <remarks>
    /// (1) For every fg pixel in %pixm, this paints the pattern in %sel
    ///     in color %val on a copy of %pixs.
    /// (2) The implementation is to dilate %pixm by %sel, and then
    ///     paint through the dilated mask onto %pixs.
    /// (3) If %pixs == NULL, it paints on a white image.
    /// (4) If %sel == NULL, it paints only the pixels in the input %pixm.
    /// (5) This visualization would typically be used in debugging.
    /// </remarks>
    public static Pix DisplaySelectedPixels(Pix? pixs, Pix pixm, Sel? sel, uint val)
    {
        if (pixm.Depth != 1)
            throw Lept.Arg("pixDisplaySelectedPixels", "pixm undefined or not 1 bpp");

        Pix pix1;
        if (pixs != null) {
            pix1 = pixs.ConvertTo32();
        } else {
            pixm.GetDimensions(out int w, out int h, out _);
            pix1 = Create(w, h, 32);
            pix1.SetAll();
        }

        Pix pix2;
        if (sel != null)
            pix2 = Dilate(null, pixm, sel);
        else
            pix2 = Clone(pixm);
        pix1.SetMasked(pix2, val);
        return pix1;
    }

    /*-------------------------------------------------------------*
     *    One and two-image boolean ops on arbitrary depth images  *
     *-------------------------------------------------------------*/
    // leptonica: pixInvert (pix3.c)
    /// <summary>Inverts pixs, into pixd if given.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">any depth</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This inverts pixs, for all pixel depths.
    /// (2) There are 3 cases:
    ///      (a) pixd == null,   ~src --> new pixd
    ///      (b) pixd == pixs,   ~src --> src  (in-place)
    ///      (c) pixd != pixs,   ~src --> input pixd
    /// (3) For clarity, if the case is known, use these patterns:
    ///      (a) pixd = pixInvert(NULL, pixs);
    ///      (b) pixInvert(pixs, pixs);
    ///      (c) pixInvert(pixd, pixs);
    /// </remarks>
    public static Pix Invert(Pix? pixd, Pix pixs)
    {
        pixs.GetDimensions(out int w, out int h, out int d);
        int wpl = pixs.Wpl;
        int linebits = w * d;
        int fullwords = linebits >> 5;
        int endbits = linebits & 31;
        uint endmask = endbits == 0 ? 0u : (0xffffffffu << (32 - endbits));

        if (pixd == null)
            pixd = CreateTemplateRawNoInit(pixs);
        else if (pixd != pixs) {
            pixd.ResizeImageDataNoInit(pixs);
            pixd.CopyColormap(pixs);
            pixd.CopySpp(pixs);
            pixd.CopyResolution(pixs);
            pixd.CopyInputFormat(pixs);
            pixd.CopyText(pixs);
        }

        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;

        if (endbits == 0) {
            InvertWordsSimd(datas, datad, wpl * h);
        } else {
            for (int i = 0; i < h; i++) {
                int off = i * wpl;
                if (fullwords > 0)
                    InvertWordsSimdOffset(datas, datad, off, fullwords);
                datad[off + fullwords] = datas[off + fullwords] ^ endmask;
            }
        }

        return pixd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InvertWordsSimd(uint[] src, uint[] dst, int count)
    {
        InvertWordsSimdOffset(src, dst, 0, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InvertWordsSimdOffset(uint[] src, uint[] dst, int offset, int count)
    {
        ref uint sRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), (nuint)(uint)offset);
        ref uint dRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), (nuint)(uint)offset);
        nuint i = 0;
        nuint n = (nuint)(uint)count;
        if (Vector512.IsHardwareAccelerated && n >= 16) {
            nuint limit = n - 15;
            for (; i < limit; i += 16) {
                Vector512<uint> v = Vector512.LoadUnsafe(ref sRef, i);
                Vector512.OnesComplement(v).StoreUnsafe(ref dRef, i);
            }
        }
        if (Vector256.IsHardwareAccelerated && i + 8 <= n) {
            for (; i + 8 <= n; i += 8) {
                Vector256<uint> v = Vector256.LoadUnsafe(ref sRef, i);
                Vector256.OnesComplement(v).StoreUnsafe(ref dRef, i);
            }
        }
        for (; i < n; i++)
            Unsafe.Add(ref dRef, i) = ~Unsafe.Add(ref sRef, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BinaryWordsSimd(Pix pixd, Pix pixs1, Pix pixs2, int mode)
    {
        int w = pixs1.Width;
        int h = pixs1.Height;
        int d = pixs1.Depth;
        int wpl = pixs1.Wpl;
        int linebits = w * d;
        int fullwords = linebits >> 5;
        int endbits = linebits & 31;
        uint endmask = endbits == 0 ? 0u : (0xffffffffu << (32 - endbits));
        uint[] s1 = pixs1.Data;
        uint[] s2 = pixs2.Data;
        uint[] dst = pixd.Data;

        if (endbits == 0) {
            ApplyBinaryWords(s1, s2, dst, 0, wpl * h, mode);
        } else {
            for (int y = 0; y < h; y++) {
                int off = y * wpl;
                if (fullwords > 0)
                    ApplyBinaryWords(s1, s2, dst, off, fullwords, mode);
                int last = off + fullwords;
                uint w1 = s1[last];
                uint w2 = s2[last] & endmask;
                dst[last] = mode switch {
                    0 => w1 | w2,
                    1 => w1 & (s2[last] | ~endmask),
                    2 => w1 ^ w2,
                    _ => w1 & ~w2,
                };
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyBinaryWords(uint[] s1, uint[] s2, uint[] dst, int offset, int count, int mode)
    {
        ref uint r1 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(s1), (nuint)(uint)offset);
        ref uint r2 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(s2), (nuint)(uint)offset);
        ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), (nuint)(uint)offset);
        nuint i = 0;
        nuint n = (nuint)(uint)count;
        if (Vector256.IsHardwareAccelerated && n >= 8) {
            if (mode == 0) {
                for (; i + 8 <= n; i += 8)
                    (Vector256.LoadUnsafe(ref r1, i) | Vector256.LoadUnsafe(ref r2, i)).StoreUnsafe(ref rd, i);
            } else if (mode == 1) {
                for (; i + 8 <= n; i += 8)
                    (Vector256.LoadUnsafe(ref r1, i) & Vector256.LoadUnsafe(ref r2, i)).StoreUnsafe(ref rd, i);
            } else if (mode == 2) {
                for (; i + 8 <= n; i += 8)
                    (Vector256.LoadUnsafe(ref r1, i) ^ Vector256.LoadUnsafe(ref r2, i)).StoreUnsafe(ref rd, i);
            } else {
                for (; i + 8 <= n; i += 8)
                    Vector256.AndNot(Vector256.LoadUnsafe(ref r1, i), Vector256.LoadUnsafe(ref r2, i)).StoreUnsafe(ref rd, i);
            }
        }
        if (mode == 0) {
            for (; i < n; i++) Unsafe.Add(ref rd, i) = Unsafe.Add(ref r1, i) | Unsafe.Add(ref r2, i);
        } else if (mode == 1) {
            for (; i < n; i++) Unsafe.Add(ref rd, i) = Unsafe.Add(ref r1, i) & Unsafe.Add(ref r2, i);
        } else if (mode == 2) {
            for (; i < n; i++) Unsafe.Add(ref rd, i) = Unsafe.Add(ref r1, i) ^ Unsafe.Add(ref r2, i);
        } else {
            for (; i < n; i++) Unsafe.Add(ref rd, i) = Unsafe.Add(ref r1, i) & ~Unsafe.Add(ref r2, i);
        }
    }

    private static Pix PrepareSameSizeDest(Pix? pixd, Pix pixs1)
    {
        if (pixd == null)
            return CreateTemplateRawNoInit(pixs1);
        if (pixd != pixs1) {
            pixd.ResizeImageDataNoInit(pixs1);
            pixd.CopyColormap(pixs1);
            pixd.CopySpp(pixs1);
            pixd.CopyResolution(pixs1);
            pixd.CopyInputFormat(pixs1);
            pixd.CopyText(pixs1);
        }
        return pixd;
    }

    // leptonica: pixOr (pix3.c)
    /// <summary>Union of two images with equal depth, aligned at the UL corner.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs1, different from pixs1</param>
    /// <param name="pixs1">can be == pixd</param>
    /// <param name="pixs2">must be != pixd</param>
    /// <returns>pixd always</returns>
    /// <remarks>
    /// (1) This gives the union of two images with equal depth,
    ///     aligning them to the UL corner.  pixs1 and pixs2
    ///     need not have the same width and height.
    /// (2) There are 3 cases:
    ///       (a) pixd == null,   (src1 | src2) --> new pixd
    ///       (b) pixd == pixs1,  (src1 | src2) --> src1  (in-place)
    ///       (c) pixd != pixs1,  (src1 | src2) --> input pixd
    /// (3) For clarity, if the case is known, use these patterns:
    ///       (a) pixd = pixOr(NULL, pixs1, pixs2);
    ///       (b) pixOr(pixs1, pixs1, pixs2);
    ///       (c) pixOr(pixd, pixs1, pixs2);
    /// (4) The size of the result is determined by pixs1.
    /// (5) The depths of pixs1 and pixs2 must be equal.
    /// (6) Note carefully that the order of pixs1 and pixs2 only matters
    ///     for the in-place case.  For in-place, you must have
    ///     pixd == pixs1.  Setting pixd == pixs2 gives an incorrect
    ///     result: the copy puts pixs1 image data in pixs2, and
    ///     the rasterop is then between pixs2 and pixs2 (a no-op).
    /// </remarks>
    public static Pix Or(Pix? pixd, Pix pixs1, Pix pixs2)
    {
        if (pixd == pixs2)
            throw Lept.Arg("pixOr", "cannot have pixs2 == pixd");
        if (pixs1.Depth != pixs2.Depth)
            throw Lept.Arg("pixOr", "depths of pixs* unequal");

        if (SizesEqual(pixs1, pixs2)) {
            pixd = PrepareSameSizeDest(pixd, pixs1);
            BinaryWordsSimd(pixd, pixs1, pixs2, 0);
            return pixd;
        }

            /* Prepare pixd to be a copy of pixs1 */
        pixd = Copy(pixd, pixs1);

            /* src1 | src2 --> dest */
        pixd.Rasterop(0, 0, pixd.Width, pixd.Height,
                      L.PIX_SRC | L.PIX_DST, pixs2, 0, 0);

        return pixd;
    }

    // leptonica: pixAnd (pix3.c)
    /// <summary>Intersection of two images with equal depth, aligned at the UL corner.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs1, different from pixs1</param>
    /// <param name="pixs1">can be == pixd</param>
    /// <param name="pixs2">must be != pixd</param>
    /// <returns>pixd always</returns>
    /// <remarks>
    /// (1) This gives the intersection of two images with equal depth,
    ///     aligning them to the the UL corner.  pixs1 and pixs2
    ///     need not have the same width and height.
    /// (2) There are 3 cases:
    ///       (a) pixd == null,   (src1 &amp; src2) --> new pixd
    ///       (b) pixd == pixs1,  (src1 &amp; src2) --> src1  (in-place)
    ///       (c) pixd != pixs1,  (src1 &amp; src2) --> input pixd
    /// (3) For clarity, if the case is known, use these patterns:
    ///       (a) pixd = pixAnd(NULL, pixs1, pixs2);
    ///       (b) pixAnd(pixs1, pixs1, pixs2);
    ///       (c) pixAnd(pixd, pixs1, pixs2);
    /// (4) The size of the result is determined by pixs1.
    /// (5) The depths of pixs1 and pixs2 must be equal.
    /// (6) Note carefully that the order of pixs1 and pixs2 only matters
    ///     for the in-place case.  For in-place, you must have
    ///     pixd == pixs1.  Setting pixd == pixs2 gives an incorrect
    ///     result: the copy puts pixs1 image data in pixs2, and
    ///     the rasterop is then between pixs2 and pixs2 (a no-op).
    /// </remarks>
    public static Pix And(Pix? pixd, Pix pixs1, Pix pixs2)
    {
        if (pixd == pixs2)
            throw Lept.Arg("pixAnd", "cannot have pixs2 == pixd");
        if (pixs1.Depth != pixs2.Depth)
            throw Lept.Arg("pixAnd", "depths of pixs* unequal");

        if (SizesEqual(pixs1, pixs2)) {
            pixd = PrepareSameSizeDest(pixd, pixs1);
            BinaryWordsSimd(pixd, pixs1, pixs2, 1);
            return pixd;
        }

            /* Prepare pixd to be a copy of pixs1 */
        pixd = Copy(pixd, pixs1);

            /* src1 & src2 --> dest */
        pixd.Rasterop(0, 0, pixd.Width, pixd.Height,
                      L.PIX_SRC & L.PIX_DST, pixs2, 0, 0);

        return pixd;
    }

    // leptonica: pixXor (pix3.c)
    /// <summary>XOR of two images with equal depth, aligned at the UL corner.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs1, different from pixs1</param>
    /// <param name="pixs1">can be == pixd</param>
    /// <param name="pixs2">must be != pixd</param>
    /// <returns>pixd always</returns>
    /// <remarks>
    /// (1) This gives the XOR of two images with equal depth,
    ///     aligning them to the the UL corner.  pixs1 and pixs2
    ///     need not have the same width and height.
    /// (2) There are 3 cases:
    ///       (a) pixd == null,   (src1 ^ src2) --> new pixd
    ///       (b) pixd == pixs1,  (src1 ^ src2) --> src1  (in-place)
    ///       (c) pixd != pixs1,  (src1 ^ src2) --> input pixd
    /// (3) For clarity, if the case is known, use these patterns:
    ///       (a) pixd = pixXor(NULL, pixs1, pixs2);
    ///       (b) pixXor(pixs1, pixs1, pixs2);
    ///       (c) pixXor(pixd, pixs1, pixs2);
    /// (4) The size of the result is determined by pixs1.
    /// (5) The depths of pixs1 and pixs2 must be equal.
    /// (6) Note carefully that the order of pixs1 and pixs2 only matters
    ///     for the in-place case.  For in-place, you must have
    ///     pixd == pixs1.  Setting pixd == pixs2 gives an incorrect
    ///     result: the copy puts pixs1 image data in pixs2, and
    ///     the rasterop is then between pixs2 and pixs2 (a no-op).
    /// </remarks>
    public static Pix Xor(Pix? pixd, Pix pixs1, Pix pixs2)
    {
        if (pixd == pixs2)
            throw Lept.Arg("pixXor", "cannot have pixs2 == pixd");
        if (pixs1.Depth != pixs2.Depth)
            throw Lept.Arg("pixXor", "depths of pixs* unequal");

        if (SizesEqual(pixs1, pixs2)) {
            pixd = PrepareSameSizeDest(pixd, pixs1);
            BinaryWordsSimd(pixd, pixs1, pixs2, 2);
            return pixd;
        }

            /* Prepare pixd to be a copy of pixs1 */
        pixd = Copy(pixd, pixs1);

            /* src1 ^ src2 --> dest */
        pixd.Rasterop(0, 0, pixd.Width, pixd.Height,
                      L.PIX_SRC ^ L.PIX_DST, pixs2, 0, 0);

        return pixd;
    }

    // leptonica: pixSubtract (pix3.c)
    /// <summary>Set subtraction pixs1 \ pixs2 of two images with equal depth, aligned at the UL corner.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs1, equal to pixs2,
    /// or different from both pixs1 and pixs2</param>
    /// <param name="pixs1">can be == pixd</param>
    /// <param name="pixs2">can be == pixd</param>
    /// <returns>pixd always</returns>
    /// <remarks>
    /// (1) This gives the set subtraction of two images with equal depth,
    ///     aligning them to the the UL corner.  pixs1 and pixs2
    ///     need not have the same width and height.
    /// (2) Source pixs2 is always subtracted from source pixs1.
    ///     The result is
    ///             pixs1 \ pixs2 = pixs1 &amp; (~pixs2)
    /// (3) There are 4 cases:
    ///       (a) pixd == null,   (src1 - src2) --> new pixd
    ///       (b) pixd == pixs1,  (src1 - src2) --> src1  (in-place)
    ///       (c) pixd == pixs2,  (src1 - src2) --> src2  (in-place)
    ///       (d) pixd != pixs1 &amp;&amp; pixd != pixs2),
    ///                            (src1 - src2) --> input pixd
    /// (4) For clarity, if the case is known, use these patterns:
    ///       (a) pixd = pixSubtract(NULL, pixs1, pixs2);
    ///       (b) pixSubtract(pixs1, pixs1, pixs2);
    ///       (c) pixSubtract(pixs2, pixs1, pixs2);
    ///       (d) pixSubtract(pixd, pixs1, pixs2);
    /// (5) The size of the result is determined by pixs1.
    /// (6) The depths of pixs1 and pixs2 must be equal.
    /// </remarks>
    public static Pix Subtract(Pix? pixd, Pix pixs1, Pix pixs2)
    {
        if (pixs1.Depth != pixs2.Depth)
            throw Lept.Arg("pixSubtract", "depths of pixs* unequal");

        if (SizesEqual(pixs1, pixs2)) {
            if (pixd == pixs2) {
                BinaryWordsSimd(pixd, pixs1, pixs2, 3);
                return pixd;
            }
            pixd = PrepareSameSizeDest(pixd, pixs1);
            BinaryWordsSimd(pixd, pixs1, pixs2, 3);
            return pixd;
        }

        pixs1.GetDimensions(out int w, out int h, out _);
        if (pixd == null) {
            pixd = Copy(null, pixs1);
            pixd.Rasterop(0, 0, w, h, L.PIX_DST & L.PIX_NOT(L.PIX_SRC),
                          pixs2, 0, 0);   /* src1 & (~src2)  */
        } else if (pixd == pixs1) {
            pixd.Rasterop(0, 0, w, h, L.PIX_DST & L.PIX_NOT(L.PIX_SRC),
                          pixs2, 0, 0);   /* src1 & (~src2)  */
        } else if (pixd == pixs2) {
            pixd.Rasterop(0, 0, w, h, L.PIX_NOT(L.PIX_DST) & L.PIX_SRC,
                          pixs1, 0, 0);   /* src1 & (~src2)  */
        } else  { /* pixd != pixs1 && pixd != pixs2 */
            Copy(pixd, pixs1);  /* sizes pixd to pixs1 if unequal */
            pixd.Rasterop(0, 0, w, h, L.PIX_DST & L.PIX_NOT(L.PIX_SRC),
                          pixs2, 0, 0);   /* src1 & (~src2)  */
        }

        return pixd;
    }

    /*-------------------------------------------------------------*
     *                         Pixel counting                      *
     *-------------------------------------------------------------*/
    // leptonica: pixZero (pix3.c)
    /// <summary>True if all pixels are 0.</summary>
    /// <remarks>
    /// This pix (pix) is of all depths; colormap OK.
    /// (1) For a binary image, if there are no fg (black) pixels, empty = true.
    /// (2) For a grayscale image, if all pixels are black (0), empty = true.
    /// (3) For an RGB image, if all 4 components in every pixel is 0
    ///     (i.e. opaque black), empty = true.
    /// (4) For a colormapped image, pixel values are 0.  The colormap
    ///     is ignored.
    /// </remarks>
    public bool Zero()
    {
        int w = Width * Depth;  /* in bits */
        int h = Height;
        int wpl = Wpl;
        uint[] data = Data;
        int fullwords = w >> 5;
        int endbits = w & 31;

        if (endbits == 0)
            return data.AsSpan(0, wpl * h).IndexOfAnyExcept(0u) < 0;

        uint endmask = 0xffffffffU << (32 - endbits);
        for (int i = 0; i < h; i++) {
            int line = wpl * i;
            if (fullwords > 0 && data.AsSpan(line, fullwords).IndexOfAnyExcept(0u) >= 0)
                return false;
            if ((data[line + fullwords] & endmask) != 0)
                return false;
        }
        return true;
    }

    // leptonica: pixForegroundFraction (pix3.c)
    /// <summary>Fraction of ON pixels of this 1 bpp pix.</summary>
    /// <returns>fraction of ON pixels</returns>
    public float ForegroundFraction()
    {
        if (Depth != 1)
            throw Lept.Arg("pixForegroundFraction", "pix not defined or not 1 bpp");

        int count = CountPixels(null);
        GetDimensions(out int w, out int h, out _);
        return (float)count / (float)(w * h);
    }

}

public sealed partial class Pixa
{
    // leptonica: pixaCountPixels (pix3.c)
    /// <summary>Number of ON pixels in each pix of this array of 1 bpp pix.</summary>
    /// <returns>na of ON pixels in each pix</returns>
    public Numa CountPixels()
    {
        int n = Count;
        if (n == 0)
            return Numa.Create(1);

        Pix pix = GetPix(0, L.L_CLONE);
        int d = pix.Depth;
        if (d != 1)
            throw Lept.Arg("pixaCountPixels", "pixa not 1 bpp");

        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            pix = GetPix(i, L.L_CLONE);
            int count = pix.CountPixels(null);
            na.AddNumber(count);
        }

        return na;
    }
}

public sealed partial class Pix
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PopCountSpan(ReadOnlySpan<uint> words)
    {
        int sum = 0;
        ReadOnlySpan<ulong> u64 = MemoryMarshal.Cast<uint, ulong>(words);
        for (int i = 0; i < u64.Length; i++)
            sum += BitOperations.PopCount(u64[i]);
        if ((words.Length & 1) != 0)
            sum += BitOperations.PopCount(words[words.Length - 1]);
        return sum;
    }

    // leptonica: pixCountPixels (pix3.c)
    /// <summary>Counts the ON pixels of this 1 bpp pix.</summary>
    /// <param name="tab8">[optional] 8-bit pixel lookup table</param>
    /// <returns>count of ON pixels</returns>
    public int CountPixels(int[]? tab8)
    {
        if (Depth != 1)
            throw Lept.Arg("pixCountPixels", "pixs not defined or not 1 bpp");

        GetDimensions(out int w, out int h, out _);
        int wpl = Wpl;
        uint[] data = Data;
        int fullwords = w >> 5;
        int endbits = w & 31;

        if (endbits == 0)
            return PopCountSpan(data.AsSpan(0, wpl * h));

        uint endmask = 0xffffffffU << (32 - endbits);
        int sum = 0;
        for (int i = 0, line = 0; i < h; i++, line += wpl) {
            if (fullwords > 0)
                sum += PopCountSpan(data.AsSpan(line, fullwords));
            sum += BitOperations.PopCount(data[line + fullwords] & endmask);
        }
        return sum;
    }

    // leptonica: pixCountPixelsInRect (pix3.c)
    /// <summary>Counts the ON pixels of this 1 bpp pix in a rectangle.</summary>
    /// <param name="box">(can be null)</param>
    /// <param name="tab8">[optional] 8-bit pixel lookup table</param>
    /// <returns>count of ON pixels</returns>
    public int CountPixelsInRect(Box? box, int[]? tab8)
    {
        if (Depth != 1)
            throw Lept.Arg("pixCountPixelsInRect", "pixs not defined or not 1 bpp");

        if (box != null) {
            GetDimensions(out int w, out int h, out _);
            Box box1 = box.ClipToRectangle(w, h);
            box1.GetGeometry(out int bx, out int by, out int bw, out int bh);
            Pix pix1 = Create(bw, bh, 1);
            pix1.Rasterop(0, 0, bw, bh, L.PIX_SRC, this, bx, by);
            return pix1.CountPixels(tab8);
        }
        return CountPixels(tab8);
    }

    // leptonica: pixCountByRow (pix3.c)
    /// <summary>Number of ON pixels of this 1 bpp pix by row.</summary>
    /// <param name="box">[optional] clipping box for count; can be null</param>
    /// <returns>na of number of ON pixels by row</returns>
    /// <remarks>
    /// (1) To resample for a bin size different from 1, use
    ///     numaUniformSampling() on the result of this function.
    /// </remarks>
    public Numa CountByRow(Box? box)
    {
        if (Depth != 1)
            throw Lept.Arg("pixCountByRow", "pix undefined or not 1 bpp");
        if (box == null)
            return CountPixelsByRow(null);

        GetDimensions(out int w, out int h, out _);
        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart,
                                  out int xend, out int yend, out _, out int bh);

        Numa na = Numa.Create(bh);
        na.SetParameters(ystart, 1);
        uint[] data = Data;
        int wpl = Wpl;
        for (int i = ystart; i < yend; i++) {
            int count = 0;
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = xstart; j < xend; j++) {
                if (GetDataBit(line, j) != 0)
                    count++;
            }
            na.AddNumber(count);
        }

        return na;
    }

    // leptonica: pixCountByColumn (pix3.c)
    /// <summary>Number of ON pixels of this 1 bpp pix by column.</summary>
    /// <param name="box">[optional] clipping box for count; can be null</param>
    /// <returns>na of number of ON pixels by column</returns>
    /// <remarks>
    /// (1) To resample for a bin size different from 1, use
    ///     numaUniformSampling() on the result of this function.
    /// </remarks>
    public Numa CountByColumn(Box? box)
    {
        if (Depth != 1)
            throw Lept.Arg("pixCountByColumn", "pix undefined or not 1 bpp");
        if (box == null)
            return CountPixelsByColumn();

        GetDimensions(out int w, out int h, out _);
        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart,
                                  out int xend, out int yend, out int bw, out _);

        Numa na = Numa.Create(bw);
        na.SetParameters(xstart, 1);
        uint[] data = Data;
        int wpl = Wpl;
        for (int j = xstart; j < xend; j++) {
            int count = 0;
            for (int i = ystart; i < yend; i++) {
                ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
                if (GetDataBit(line, j) != 0)
                    count++;
            }
            na.AddNumber(count);
        }

        return na;
    }

    // leptonica: pixCountPixelsByRow (pix3.c)
    /// <summary>Number of ON pixels of this 1 bpp pix in each row.</summary>
    /// <param name="tab8">[optional] 8-bit pixel lookup table</param>
    /// <returns>na of counts</returns>
    public Numa CountPixelsByRow(int[]? tab8)
    {
        if (Depth != 1)
            throw Lept.Arg("pixCountPixelsByRow", "pix undefined or not 1 bpp");

        int h = Height;
        Numa na = Numa.Create(h);
        for (int i = 0; i < h; i++)
            na.AddNumber(CountPixelsInRow(i, null));

        return na;
    }

    // leptonica: pixCountPixelsByColumn (pix3.c)
    /// <summary>Number of ON pixels of this 1 bpp pix in each column.</summary>
    /// <returns>na of counts in each column</returns>
    public Numa CountPixelsByColumn()
    {
        if (Depth != 1)
            throw Lept.Arg("pixCountPixelsByColumn", "pix undefined or not 1 bpp");

        GetDimensions(out int w, out int h, out _);
        Numa na = Numa.Create(w);
        na.Count = w;
        float[] array = na.GetFArray(L.L_NOCOPY);
        uint[] data = Data;
        int wpl = Wpl;
        int fullwords = w >> 5;
        int endbits = w & 31;
        uint endmask = endbits == 0 ? 0u : (0xffffffffU << (32 - endbits));

        for (int i = 0; i < h; i++) {
            int rowOff = wpl * i;
            for (int jw = 0; jw < fullwords; jw++) {
                uint word = data[rowOff + jw];
                int baseCol = jw << 5;
                while (word != 0) {
                    int bit = BitOperations.TrailingZeroCount(word);
                    array[baseCol + (31 - bit)] += 1.0f;
                    word &= word - 1;
                }
            }
            if (endbits != 0) {
                uint word = data[rowOff + fullwords] & endmask;
                int baseCol = fullwords << 5;
                while (word != 0) {
                    int bit = BitOperations.TrailingZeroCount(word);
                    array[baseCol + (31 - bit)] += 1.0f;
                    word &= word - 1;
                }
            }
        }

        return na;
    }

    // leptonica: pixCountPixelsInRow (pix3.c)
    /// <summary>Number of ON pixels of this 1 bpp pix in a raster line.</summary>
    /// <param name="row">number</param>
    /// <param name="tab8">[optional] 8-bit pixel lookup table</param>
    /// <returns>sum of ON pixels in raster line</returns>
    public int CountPixelsInRow(int row, int[]? tab8)
    {
        if (Depth != 1)
            throw Lept.Arg("pixCountPixelsInRow", "pix not defined or not 1 bpp");

        GetDimensions(out int w, out int h, out _);
        if (row < 0 || row >= h)
            throw Lept.Arg("pixCountPixelsInRow", "row out of bounds");
        int wpl = Wpl;
        int line = row * wpl;
        uint[] data = Data;
        int fullwords = w >> 5;
        int endbits = w & 31;

        int sum = fullwords > 0 ? PopCountSpan(data.AsSpan(line, fullwords)) : 0;
        if (endbits != 0)
            sum += BitOperations.PopCount(data[line + fullwords] & (0xffffffffU << (32 - endbits)));
        return sum;
    }

    // leptonica: pixGetMomentByColumn (pix3.c)
    /// <summary>First or second moment of the fg pixels of this 1 bpp pix, by column.</summary>
    /// <param name="order">of moment, either 1 or 2</param>
    /// <returns>na of first moment of fg pixels, by column</returns>
    public Numa GetMomentByColumn(int order)
    {
        if (Depth != 1)
            throw Lept.Arg("pixGetMomentByColumn", "pix undefined or not 1 bpp");
        if (order != 1 && order != 2)
            throw Lept.Arg("pixGetMomentByColumn", "order of moment not 1 or 2");

        GetDimensions(out int w, out int h, out _);
        Numa na = Numa.Create(w);
        na.Count = w;
        float[] array = na.GetFArray(L.L_NOCOPY);
        uint[] data = Data;
        int wpl = Wpl;
        int fullwords = w >> 5;
        int endbits = w & 31;
        uint endmask = endbits == 0 ? 0u : (0xffffffffU << (32 - endbits));

        for (int i = 0; i < h; i++) {
            float weight = order == 1 ? i : (float)(i * i);
            if (weight == 0f)
                continue;
            int rowOff = wpl * i;
            for (int jw = 0; jw < fullwords; jw++) {
                uint word = data[rowOff + jw];
                int baseCol = jw << 5;
                while (word != 0) {
                    int bit = BitOperations.TrailingZeroCount(word);
                    array[baseCol + (31 - bit)] += weight;
                    word &= word - 1;
                }
            }
            if (endbits != 0) {
                uint word = data[rowOff + fullwords] & endmask;
                int baseCol = fullwords << 5;
                while (word != 0) {
                    int bit = BitOperations.TrailingZeroCount(word);
                    array[baseCol + (31 - bit)] += weight;
                    word &= word - 1;
                }
            }
        }

        return na;
    }

    // leptonica: pixThresholdPixelSum (pix3.c)
    /// <summary>Tests whether the number of ON pixels of this 1 bpp pix is above a threshold.</summary>
    /// <param name="thresh">threshold</param>
    /// <param name="tab8">[optional] 8-bit pixel lookup table</param>
    /// <returns>true if above threshold; false if equal to or less than threshold</returns>
    /// <remarks>
    /// (1) This sums the ON pixels and returns immediately if the count
    ///     goes above threshold.  It is therefore more efficient
    ///     for matching images (by running this function on the xor of
    ///     the 2 images) than using pixCountPixels(), which counts all
    ///     pixels before returning.
    /// </remarks>
    public bool ThresholdPixelSum(int thresh, int[]? tab8)
    {
        if (Depth != 1)
            throw Lept.Arg("pixThresholdPixelSum", "pix not defined or not 1 bpp");

        GetDimensions(out int w, out int h, out _);
        int wpl = Wpl;
        uint[] data = Data;
        int fullwords = w >> 5;
        int endbits = w & 31;
        uint endmask = endbits == 0 ? 0u : (0xffffffffu << (32 - endbits));

        int sum = 0;
        for (int i = 0; i < h; i++) {
            int line = wpl * i;
            if (fullwords > 0)
                sum += PopCountSpan(data.AsSpan(line, fullwords));
            if (endbits != 0)
                sum += BitOperations.PopCount(data[line + fullwords] & endmask);
            if (sum > thresh)
                return true;
        }

        return false;
    }

    // leptonica: makePixelSumTab8 (pix3.c)
    /// <returns>table of 256 l_int32</returns>
    /// <remarks>
    /// (1) This table of integers gives the number of 1 bits
    ///     in the 8 bit index.
    /// </remarks>
    public static int[] MakePixelSumTab8()
    {
        int[] tab = new int[256];
        for (int i = 0; i < 256; i++) {
            byte b = (byte)i;
            tab[i] = (b & 0x1) +
                     ((b >> 1) & 0x1) +
                     ((b >> 2) & 0x1) +
                     ((b >> 3) & 0x1) +
                     ((b >> 4) & 0x1) +
                     ((b >> 5) & 0x1) +
                     ((b >> 6) & 0x1) +
                     ((b >> 7) & 0x1);
        }
        return tab;
    }

    // leptonica: makePixelCentroidTab8 (pix3.c)
    /// <returns>table of 256 l_int32</returns>
    /// <remarks>
    /// (1) This table of integers gives the centroid weight of the 1 bits
    ///     in the 8 bit index.  In other words, if sumtab is obtained by
    ///     makePixelSumTab8, and centroidtab is obtained by
    ///     makePixelCentroidTab8, then, for 1 &lt;= i &lt;= 255,
    ///     centroidtab[i] / (float)sumtab[i]
    ///     is the centroid of the 1 bits in the 8-bit index i, where the
    ///     MSB is considered to have position 0 and the LSB is considered
    ///     to have position 7.
    /// </remarks>
    public static int[] MakePixelCentroidTab8()
    {
        int[] tab = new int[256];
        tab[0] = 0;
        tab[1] = 7;
        for (int i = 2; i < 4; i++) {
            tab[i] = tab[i - 2] + 6;
        }
        for (int i = 4; i < 8; i++) {
            tab[i] = tab[i - 4] + 5;
        }
        for (int i = 8; i < 16; i++) {
            tab[i] = tab[i - 8] + 4;
        }
        for (int i = 16; i < 32; i++) {
            tab[i] = tab[i - 16] + 3;
        }
        for (int i = 32; i < 64; i++) {
            tab[i] = tab[i - 32] + 2;
        }
        for (int i = 64; i < 128; i++) {
            tab[i] = tab[i - 64] + 1;
        }
        for (int i = 128; i < 256; i++) {
            tab[i] = tab[i - 128];
        }
        return tab;
    }

    /*-------------------------------------------------------------*
     *             Average of pixel values in gray images          *
     *-------------------------------------------------------------*/
    // leptonica: pixAverageByRow (pix3.c)
    /// <summary>Average of the pixel values of this 8 or 16 bpp pix, by row.</summary>
    /// <param name="box">[optional] clipping box for sum; can be null</param>
    /// <param name="type">L_WHITE_IS_MAX, L_BLACK_IS_MAX</param>
    /// <returns>na of pixel averages by row</returns>
    /// <remarks>
    /// This pix (pix) is 8 or 16 bpp; no colormap.
    /// (1) To resample for a bin size different from 1, use
    ///     numaUniformSampling() on the result of this function.
    /// (2) If type == L_BLACK_IS_MAX, black pixels get the maximum
    ///     value (0xff for 8 bpp, 0xffff for 16 bpp) and white get 0.
    /// </remarks>
    public Numa AverageByRow(Box? box, int type)
    {
        const string func = "pixAverageByRow";

        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 16)
            throw Lept.Arg(func, "pix not 8 or 16 bpp");
        if (type != L.L_WHITE_IS_MAX && type != L.L_BLACK_IS_MAX)
            throw Lept.Arg(func, "invalid type");
        if (Colormap != null)
            throw Lept.Arg(func, "pix colormapped");

        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart,
                                  out int xend, out int yend, out int bw, out int bh);

        double norm = 1.0 / (float)bw;
        Numa na = Numa.Create(bh);
        na.SetParameters(ystart, 1);
        uint[] data = Data;
        int wpl = Wpl;
        for (int i = ystart; i < yend; i++) {
            double sum = 0.0;
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            if (d == 8) {
                for (int j = xstart; j < xend; j++)
                    sum += GetDataByte(line, j);
                if (type == L.L_BLACK_IS_MAX)
                    sum = bw * 255 - sum;
            } else {  /* d == 16 */
                for (int j = xstart; j < xend; j++)
                    sum += GetDataTwoBytes(line, j);
                if (type == L.L_BLACK_IS_MAX)
                    sum = bw * 0xffff - sum;
            }
            na.AddNumber((float)(norm * sum));
        }

        return na;
    }

    // leptonica: pixAverageByColumn (pix3.c)
    /// <summary>Average of the pixel values of this 8 or 16 bpp pix, by column.</summary>
    /// <param name="box">[optional] clipping box for sum; can be null</param>
    /// <param name="type">L_WHITE_IS_MAX, L_BLACK_IS_MAX</param>
    /// <returns>na of pixel averages by column</returns>
    /// <remarks>
    /// This pix (pix) is 8 or 16 bpp; no colormap.
    /// (1) To resample for a bin size different from 1, use
    ///     numaUniformSampling() on the result of this function.
    /// (2) If type == L_BLACK_IS_MAX, black pixels get the maximum
    ///     value (0xff for 8 bpp, 0xffff for 16 bpp) and white get 0.
    /// </remarks>
    public Numa AverageByColumn(Box? box, int type)
    {
        const string func = "pixAverageByColumn";

        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 16)
            throw Lept.Arg(func, "pix not 8 or 16 bpp");
        if (type != L.L_WHITE_IS_MAX && type != L.L_BLACK_IS_MAX)
            throw Lept.Arg(func, "invalid type");
        if (Colormap != null)
            throw Lept.Arg(func, "pix colormapped");

        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart,
                                  out int xend, out int yend, out int bw, out int bh);

        Numa na = Numa.Create(bw);
        na.SetParameters(xstart, 1);
        float norm = 1.0f / (float)bh;
        uint[] data = Data;
        int wpl = Wpl;
        for (int j = xstart; j < xend; j++) {
            float sum = 0.0f;
            if (d == 8) {
                for (int i = ystart; i < yend; i++) {
                    ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
                    sum += GetDataByte(line, j);
                }
                if (type == L.L_BLACK_IS_MAX)
                    sum = bh * 255 - sum;
            } else {  /* d == 16 */
                for (int i = ystart; i < yend; i++) {
                    ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
                    sum += GetDataTwoBytes(line, j);
                }
                if (type == L.L_BLACK_IS_MAX)
                    sum = bh * 0xffff - sum;
            }
            na.AddNumber((float)(norm * sum));
        }

        return na;
    }

    // leptonica: pixAverageInRect (pix3.c)
    /// <summary>Average of the pixel values of this pix under optional rectangle, mask,
    /// range and subsampling filters.</summary>
    /// <param name="pixm">[optional] 1 bpp mask; if null, use all pixels</param>
    /// <param name="box">[optional] if null, use entire image</param>
    /// <param name="minval">ignore values less than this</param>
    /// <param name="maxval">ignore values greater than this</param>
    /// <param name="subsamp">subsample factor: integer; use 1 for all pixels</param>
    /// <param name="pave">average of pixel values under consideration</param>
    /// <returns>true if OK; false if all pixels are filtered out (the C function returns 2)</returns>
    /// <remarks>
    /// This pix (pixs) is 1, 2, 4, 8 bpp; not cmapped.
    /// (1) The average is computed with 4 optional filters: a rectangle,
    ///     a mask, a contiguous set of range values, and subsampling.
    ///     In practice you might use only one or two of these.
    /// (2) The mask %pixm is a blocking mask: only count pixels in the bg.
    ///     If it exists, alignment is assumed at UL corner and computation
    ///     is over the minimum intersection of %pixs and %pixm.
    ///     If you want the average of pixels under the mask fg, invert it.
    /// (3) Set the range limits %minval = 0 and %maxval = 255 to use
    ///     all non-masked pixels (regardless of value) in the average.
    /// (4) If no pixels are used in the averaging, the returned average
    ///     value is 0 and the function returns false.  This is not an error,
    ///     but it says to disregard the returned average value.
    /// (5) For example, to average all pixels in a given clipping rect %box,
    ///         pixAverageInRect(pixs, NULL, box, 0, 255, 1, &amp;aveval);
    /// </remarks>
    public bool AverageInRect(Pix? pixm, Box? box, int minval, int maxval, int subsamp,
                              out float pave)
    {
        const string func = "pixAverageInRect";

        pave = 0;
        if (Colormap != null)
            throw Lept.Arg(func, "pixs is colormapped");
        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "pixs not 1, 2, 4 or 8 bpp");
        if (pixm != null) {
            pixm.GetDimensions(out int wm, out int hm, out int dm);
            if (dm != 1)
                throw Lept.Arg(func, "pixm not 1 bpp");
            w = Math.Min(w, wm);
            h = Math.Min(h, hm);
        }
        if (subsamp < 1)
            throw Lept.Arg(func, "subsamp must be >= 1");

        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart,
                                  out int xend, out int yend, out _, out _);

        uint[] datas = Data;
        int wpls = Wpl;
        uint[] datam = Array.Empty<uint>();
        int wplm = 0;
        if (pixm != null) {
            datam = pixm.Data;
            wplm = pixm.Wpl;
        }
        double sum = 0.0;
        int count = 0;
        for (int i = ystart; i < yend; i += subsamp) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            ReadOnlySpan<uint> linem = default;
            if (pixm != null)
                linem = datam.AsSpan(i * wplm, wplm);
            for (int j = xstart; j < xend; j += subsamp) {
                if (pixm != null && (GetDataBit(linem, j) == 1))
                    continue;
                int val;
                if (d == 1)
                    val = GetDataBit(lines, j);
                else if (d == 2)
                    val = GetDataDibit(lines, j);
                else if (d == 4)
                    val = GetDataQbit(lines, j);
                else  /* d == 8 */
                    val = GetDataByte(lines, j);
                if (val >= minval && val <= maxval) {
                    sum += val;
                    count++;
                }
            }
        }

        if (count == 0)
            return false;  /* not an error; don't use the average value (0.0) */
        pave = (float)(sum / (float)count);
        return true;
    }

    /*-------------------------------------------------------------*
     *             Average of pixel values in RGB images           *
     *-------------------------------------------------------------*/
    // leptonica: pixAverageInRectRGB (pix3.c)
    /// <summary>Average color of this rgb pix under optional rectangle, mask and subsampling filters.</summary>
    /// <param name="pixm">[optional] 1 bpp mask; if null, use all pixels</param>
    /// <param name="box">[optional] if null, use entire image</param>
    /// <param name="subsamp">subsample factor: integer; use 1 for all pixels</param>
    /// <param name="pave">average color of pixel values under consideration,
    /// in format 0xrrggbb00</param>
    /// <returns>true if OK; false if all pixels are filtered out (the C function returns 2)</returns>
    /// <remarks>
    /// This pix (pixs) is rgb; not cmapped.
    /// (1) The average is computed with 3 optional filters: a rectangle,
    ///     a mask, and subsampling.
    ///     In practice you might use only one or two of these.
    /// (2) The mask %pixm is a blocking mask: only count pixels in the bg.
    ///     If it exists, alignment is assumed at UL corner and computation
    ///     is over the minimum intersection of %pixs and %pixm.
    ///     If you want the average of pixels under the mask fg, invert it.
    /// (3) If no pixels are used in the averaging, the returned average
    ///     value is 0 and the function returns false.  This is not an error,
    ///     but it says to disregard the returned average value.
    /// (4) For example, to average all pixels in a given clipping rect %box,
    ///         pixAverageInRectRGB(pixs, NULL, box, 1, &amp;aveval);
    /// </remarks>
    public bool AverageInRectRGB(Pix? pixm, Box? box, int subsamp, out uint pave)
    {
        const string func = "pixAverageInRectRGB";

        pave = 0;
        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        GetDimensions(out int w, out int h, out _);
        if (pixm != null) {
            pixm.GetDimensions(out int wm, out int hm, out int dm);
            if (dm != 1)
                throw Lept.Arg(func, "pixm not 1 bpp");
            w = Math.Min(w, wm);
            h = Math.Min(h, hm);
        }
        if (subsamp < 1)
            throw Lept.Arg(func, "subsamp must be >= 1");

        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart,
                                  out int xend, out int yend, out _, out _);

        uint[] datas = Data;
        int wpls = Wpl;
        uint[] datam = Array.Empty<uint>();
        int wplm = 0;
        if (pixm != null) {
            datam = pixm.Data;
            wplm = pixm.Wpl;
        }
        double rsum = 0.0, gsum = 0.0, bsum = 0.0;
        int count = 0;
        for (int i = ystart; i < yend; i += subsamp) {
            int lines = i * wpls;
            ReadOnlySpan<uint> linem = default;
            if (pixm != null)
                linem = datam.AsSpan(i * wplm, wplm);
            for (int j = xstart; j < xend; j += subsamp) {
                if (pixm != null && (GetDataBit(linem, j) == 1))
                    continue;
                uint pixel = datas[lines + j];
                ExtractRGBValues(pixel, out int rval, out int gval, out int bval);
                rsum += rval;
                gsum += gval;
                bsum += bval;
                count++;
            }
        }

        if (count == 0)
            return false;  /* not an error */
        int rave = (int)(uint)(rsum / (double)count);
        int gave = (int)(uint)(gsum / (double)count);
        int bave = (int)(uint)(bsum / (double)count);
        ComposeRGBPixel(rave, gave, bave, out pave);
        return true;
    }

    /*------------------------------------------------------------------*
     *               Variance of pixel values in gray images            *
     *------------------------------------------------------------------*/
    // leptonica: pixVarianceByRow (pix3.c)
    /// <summary>RMS deviation of the pixel values of this 8 or 16 bpp pix, by row.</summary>
    /// <param name="box">[optional] clipping box for variance; can be null</param>
    /// <returns>na of rmsdev by row</returns>
    /// <remarks>
    /// This pix (pix) is 8 or 16 bpp; no colormap.
    /// (1) To resample for a bin size different from 1, use
    ///     numaUniformSampling() on the result of this function.
    /// (2) We are actually computing the RMS deviation in each row.
    ///     This is the square root of the variance.
    /// </remarks>
    public Numa VarianceByRow(Box? box)
    {
        const string func = "pixVarianceByRow";

        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 16)
            throw Lept.Arg(func, "pix not 8 or 16 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pix colormapped");

        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart,
                                  out int xend, out int yend, out int bw, out int bh);

        Numa na = Numa.Create(bh);
        na.SetParameters(ystart, 1);
        double norm = 1.0 / (float)bw;
        uint[] data = Data;
        int wpl = Wpl;
        for (int i = ystart; i < yend; i++) {
            double sum1 = 0.0, sum2 = 0.0;
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = xstart; j < xend; j++) {
                int val;
                if (d == 8)
                    val = GetDataByte(line, j);
                else  /* d == 16 */
                    val = GetDataTwoBytes(line, j);
                sum1 += val;
                sum2 += (double)(val) * val;
            }
            double ave = norm * sum1;
            double variance = norm * sum2 - ave * ave;
            double rootvar = Math.Sqrt(variance);
            na.AddNumber((float)rootvar);
        }

        return na;
    }

    // leptonica: pixVarianceByColumn (pix3.c)
    /// <summary>RMS deviation of the pixel values of this 8 or 16 bpp pix, by column.</summary>
    /// <param name="box">[optional] clipping box for variance; can be null</param>
    /// <returns>na of rmsdev by column</returns>
    /// <remarks>
    /// This pix (pix) is 8 or 16 bpp; no colormap.
    /// (1) To resample for a bin size different from 1, use
    ///     numaUniformSampling() on the result of this function.
    /// (2) We are actually computing the RMS deviation in each row.
    ///     This is the square root of the variance.
    /// </remarks>
    public Numa VarianceByColumn(Box? box)
    {
        const string func = "pixVarianceByColumn";

        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 16)
            throw Lept.Arg(func, "pix not 8 or 16 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pix colormapped");

        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart,
                                  out int xend, out int yend, out int bw, out int bh);

        Numa na = Numa.Create(bw);
        na.SetParameters(xstart, 1);
        double norm = 1.0 / (float)bh;
        uint[] data = Data;
        int wpl = Wpl;
        for (int j = xstart; j < xend; j++) {
            double sum1 = 0.0, sum2 = 0.0;
            for (int i = ystart; i < yend; i++) {
                ReadOnlySpan<uint> line = data.AsSpan(wpl * i, wpl);
                int val;
                if (d == 8)
                    val = GetDataByte(line, j);
                else  /* d == 16 */
                    val = GetDataTwoBytes(line, j);
                sum1 += val;
                sum2 += (double)(val) * val;
            }
            double ave = norm * sum1;
            double variance = norm * sum2 - ave * ave;
            double rootvar = Math.Sqrt(variance);
            na.AddNumber((float)rootvar);
        }

        return na;
    }

    // leptonica: pixVarianceInRect (pix3.c)
    /// <summary>RMS deviation of the pixel values of this pix in a rectangle.</summary>
    /// <param name="box">[optional] if null, use entire image</param>
    /// <returns>sqrt variance of pixel values in region</returns>
    /// <remarks>This pix (pix) is 1, 2, 4, 8 bpp; not cmapped.</remarks>
    public float VarianceInRect(Box? box)
    {
        const string func = "pixVarianceInRect";

        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "pix not 1, 2, 4 or 8 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pix is colormapped");

        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart,
                                  out int xend, out int yend, out int bw, out int bh);

        int wpl = Wpl;
        uint[] data = Data;
        double sum1 = 0.0, sum2 = 0.0;
        for (int i = ystart; i < yend; i++) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = xstart; j < xend; j++) {
                int val;
                if (d == 1) {
                    val = GetDataBit(line, j);
                    sum1 += val;
                    sum2 += (double)(val) * val;
                } else if (d == 2) {
                    val = GetDataDibit(line, j);
                    sum1 += val;
                    sum2 += (double)(val) * val;
                } else if (d == 4) {
                    val = GetDataQbit(line, j);
                    sum1 += val;
                    sum2 += (double)(val) * val;
                } else {  /* d == 8 */
                    val = GetDataByte(line, j);
                    sum1 += val;
                    sum2 += (double)(val) * val;
                }
            }
        }
        double norm = 1.0 / ((double)(bw) * bh);
        double ave = norm * sum1;
        double variance = norm * sum2 - ave * ave;
        return (float)Math.Sqrt(variance);
    }

    /*---------------------------------------------------------------------*
     *    Average of absolute value of pixel differences in gray images    *
     *---------------------------------------------------------------------*/
    // leptonica: pixAbsDiffByRow (pix3.c)
    /// <summary>Average of the abs value of the differences of adjacent pixels along each row.</summary>
    /// <param name="box">[optional] clipping box for region; can be null</param>
    /// <returns>na of abs val pixel difference averages by row</returns>
    /// <remarks>
    /// This pix (pix) is 8 bpp; no colormap.
    /// (1) This is an average over differences of adjacent pixels along
    ///     each row.
    /// (2) To resample for a bin size different from 1, use
    ///     numaUniformSampling() on the result of this function.
    /// </remarks>
    public Numa AbsDiffByRow(Box? box)
    {
        const string func = "pixAbsDiffByRow";

        if (Depth != 8)
            throw Lept.Arg(func, "pix undefined or not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pix colormapped");

        GetDimensions(out int w, out int h, out _);
        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart,
                                  out int xend, out int yend, out int bw, out int bh);
        if (bw < 2)
            throw Lept.Arg(func, "row width must be >= 2");

        double norm = 1.0 / (float)(bw - 1);
        Numa na = Numa.Create(bh);
        na.SetParameters(ystart, 1);
        uint[] data = Data;
        int wpl = Wpl;
        for (int i = ystart; i < yend; i++) {
            double sum = 0.0;
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            int val0 = GetDataByte(line, xstart);
            for (int j = xstart + 1; j < xend; j++) {
                int val1 = GetDataByte(line, j);
                sum += Math.Abs(val1 - val0);
                val0 = val1;
            }
            na.AddNumber((float)(norm * sum));
        }

        return na;
    }

    // leptonica: pixAbsDiffByColumn (pix3.c)
    /// <summary>Average of the abs value of the differences of adjacent pixels along each column.</summary>
    /// <param name="box">[optional] clipping box for region; can be null</param>
    /// <returns>na of abs val pixel difference averages by column</returns>
    /// <remarks>
    /// This pix (pix) is 8 bpp; no colormap.
    /// (1) This is an average over differences of adjacent pixels along
    ///     each column.
    /// (2) To resample for a bin size different from 1, use
    ///     numaUniformSampling() on the result of this function.
    /// </remarks>
    public Numa AbsDiffByColumn(Box? box)
    {
        const string func = "pixAbsDiffByColumn";

        if (Depth != 8)
            throw Lept.Arg(func, "pix undefined or not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pix colormapped");

        GetDimensions(out int w, out int h, out _);
        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart,
                                  out int xend, out int yend, out int bw, out int bh);
        if (bh < 2)
            throw Lept.Arg(func, "column height must be >= 2");

        double norm = 1.0 / (float)(bh - 1);
        Numa na = Numa.Create(bw);
        na.SetParameters(xstart, 1);
        uint[] data = Data;
        int wpl = Wpl;
        for (int j = xstart; j < xend; j++) {
            double sum = 0.0;
            ReadOnlySpan<uint> line = data.AsSpan(ystart * wpl, wpl);
            int val0 = GetDataByte(line, j);
            for (int i = ystart + 1; i < yend; i++) {
                line = data.AsSpan(i * wpl, wpl);
                int val1 = GetDataByte(line, j);
                sum += Math.Abs(val1 - val0);
                val0 = val1;
            }
            na.AddNumber((float)(norm * sum));
        }

        return na;
    }

    // leptonica: pixAbsDiffInRect (pix3.c)
    /// <summary>Average of the abs value of the differences of adjacent pixels in a rectangle.</summary>
    /// <param name="box">[optional] if null, use entire image</param>
    /// <param name="dir">differences along L_HORIZONTAL_LINE or L_VERTICAL_LINE</param>
    /// <returns>average of abs diff pixel values in region</returns>
    /// <remarks>
    /// This pix (pix) is 8 bpp; not cmapped.
    /// (1) This gives the average over the abs val of differences of
    ///     adjacent pixels values, along either each
    ///        row:     dir == L_HORIZONTAL_LINE
    ///        column:  dir == L_VERTICAL_LINE
    /// </remarks>
    public float AbsDiffInRect(Box? box, int dir)
    {
        const string func = "pixAbsDiffInRect";

        if (Depth != 8)
            throw Lept.Arg(func, "pix undefined or not 8 bpp");
        if (dir != L.L_HORIZONTAL_LINE && dir != L.L_VERTICAL_LINE)
            throw Lept.Arg(func, "invalid direction");
        if (Colormap != null)
            throw Lept.Arg(func, "pix is colormapped");

        GetDimensions(out int w, out int h, out _);
        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart,
                                  out int xend, out int yend, out int bw, out int bh);

        int wpl = Wpl;
        uint[] data = Data;
        double norm, sum;
        if (dir == L.L_HORIZONTAL_LINE) {
            norm = 1.0 / (float)(bh * (bw - 1));
            sum = 0.0;
            for (int i = ystart; i < yend; i++) {
                ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
                int val0 = GetDataByte(line, xstart);
                for (int j = xstart + 1; j < xend; j++) {
                    int val1 = GetDataByte(line, j);
                    sum += Math.Abs(val1 - val0);
                    val0 = val1;
                }
            }
        } else {  /* vertical line */
            norm = 1.0 / (float)(bw * (bh - 1));
            sum = 0.0;
            for (int j = xstart; j < xend; j++) {
                ReadOnlySpan<uint> line = data.AsSpan(ystart * wpl, wpl);
                int val0 = GetDataByte(line, j);
                for (int i = ystart + 1; i < yend; i++) {
                    line = data.AsSpan(i * wpl, wpl);
                    int val1 = GetDataByte(line, j);
                    sum += Math.Abs(val1 - val0);
                    val0 = val1;
                }
            }
        }
        return (float)(norm * sum);
    }

    // leptonica: pixAbsDiffOnLine (pix3.c)
    /// <summary>Average of the abs value of the differences of adjacent pixels along a horizontal
    /// or vertical line.</summary>
    /// <param name="x1">first point; x1 &lt;= x2, y1 &lt;= y2</param>
    /// <param name="y1">first point; x1 &lt;= x2, y1 &lt;= y2</param>
    /// <param name="x2">second point</param>
    /// <param name="y2">second point</param>
    /// <returns>average of abs diff pixel values on line</returns>
    /// <remarks>
    /// This pix (pix) is 8 bpp; not cmapped.
    /// (1) This gives the average over the abs val of differences of
    ///     adjacent pixels values, along a line that is either horizontal
    ///     or vertical.
    /// (2) If horizontal, require x1 &lt; x2; if vertical, require y1 &lt; y2.
    /// </remarks>
    public float AbsDiffOnLine(int x1, int y1, int x2, int y2)
    {
        const string func = "pixAbsDiffOnLine";

        if (Depth != 8)
            throw Lept.Arg(func, "pix undefined or not 8 bpp");
        int dir;
        if (y1 == y2) {
            dir = L.L_HORIZONTAL_LINE;
        } else if (x1 == x2) {
            dir = L.L_VERTICAL_LINE;
        } else {
            throw Lept.Arg(func, "line is neither horiz nor vert");
        }
        if (Colormap != null)
            throw Lept.Arg(func, "pix is colormapped");

        GetDimensions(out int w, out int h, out _);
        int sum = 0;
        int size;
        uint val0, val1;
        if (dir == L.L_HORIZONTAL_LINE) {
            x1 = Math.Max(x1, 0);
            x2 = Math.Min(x2, w - 1);
            if (x1 >= x2)
                throw Lept.Arg(func, "x1 >= x2");
            size = x2 - x1;
            GetPixel(x1, y1, out val0);
            for (int j = x1 + 1; j <= x2; j++) {
                GetPixel(j, y1, out val1);
                sum += Math.Abs((int)val1 - (int)val0);
                val0 = val1;
            }
        } else {  /* vertical */
            y1 = Math.Max(y1, 0);
            y2 = Math.Min(y2, h - 1);
            if (y1 >= y2)
                throw Lept.Arg(func, "y1 >= y2");
            size = y2 - y1;
            GetPixel(x1, y1, out val0);
            for (int i = y1 + 1; i <= y2; i++) {
                GetPixel(x1, i, out val1);
                sum += Math.Abs((int)val1 - (int)val0);
                val0 = val1;
            }
        }
        return (float)sum / (float)size;
    }

    /*-------------------------------------------------------------*
     *              Count of pixels with specific value            *
     *-------------------------------------------------------------*/
    // leptonica: pixCountArbInRect (pix3.c)
    /// <summary>Counts the pixels of this pix that have a specific value.</summary>
    /// <param name="box">[optional] over which count is made; use entire image if null</param>
    /// <param name="val">pixel value to count</param>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <returns>count; estimate it if factor > 1</returns>
    /// <remarks>
    /// This pix (pixs) is 1, 2, 4, 8 bpp; can be colormapped.
    /// (1) If pixs is cmapped, %val is compared to the colormap index;
    ///     otherwise, %val is compared to the grayscale value.
    /// (2) Set the subsampling %factor > 1 to reduce the amount of computation.
    ///     If %factor > 1, multiply the count by %factor * %factor.
    /// </remarks>
    public int CountArbInRect(Box? box, int val, int factor)
    {
        const string func = "pixCountArbInRect";

        int d = Depth;
        if (d != 1 && d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "pixs not 1, 2, 4 or 8 bpp");
        if (val < 0)
            throw Lept.Arg(func, "val < 0");
        if (val > (1 << d) - 1)
            throw Lept.Arg(func, $"invalid val = {val} for depth {d}");
        if (factor < 1)
            throw Lept.Arg(func, "sampling factor < 1");

        GetDimensions(out int w, out int h, out _);
        uint[] data = Data;
        int wpl = Wpl;
        int count = 0;
        int pixval;
        if (box == null) {
            for (int i = 0; i < h; i += factor) {
                ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
                for (int j = 0; j < w; j += factor) {
                    if (d == 8) {
                        pixval = GetDataByte(line, j);
                    } else if (d == 1) {
                        pixval = GetDataBit(line, j);
                    } else if (d == 2) {
                        pixval = GetDataDibit(line, j);
                    } else  /* d == 4 */  {
                        pixval = GetDataQbit(line, j);
                    }
                    if (pixval == val) count++;
                }
            }
        } else {
            box.GetGeometry(out int bx, out int by, out int bw, out int bh);
            for (int i = 0; i < bh; i += factor) {
                if (by + i < 0 || by + i >= h) continue;
                ReadOnlySpan<uint> line = data.AsSpan((by + i) * wpl, wpl);
                for (int j = 0; j < bw; j += factor) {
                    if (bx + j < 0 || bx + j >= w) continue;
                    if (d == 8) {
                        pixval = GetDataByte(line, bx + j);
                    } else if (d == 1) {
                        pixval = GetDataBit(line, bx + j);
                    } else if (d == 2) {
                        pixval = GetDataDibit(line, bx + j);
                    } else  /* d == 4 */  {
                        pixval = GetDataQbit(line, bx + j);
                    }
                    if (pixval == val) count++;
                }
            }
        }

        if (factor > 1)  /* assume pixel color is randomly distributed */
            count = count * factor * factor;
        return count;
    }

    /*-------------------------------------------------------------*
     *              Mirrored tiling of a smaller image             *
     *-------------------------------------------------------------*/
    // leptonica: pixMirroredTiling (pix3.c)
    /// <summary>Tiles an image of size w x h with this small tile, mirrored.</summary>
    /// <param name="w">dimensions of output pix</param>
    /// <param name="h">dimensions of output pix</param>
    /// <returns>pixd usually larger pix, mirror-tiled with pixs</returns>
    /// <remarks>
    /// This pix (pixs) is 8 or 32 bpp, small tile; to be replicated.
    /// (1) This uses mirrored tiling, where each row alternates
    ///     with LR flips and every column alternates with TB
    ///     flips, such that the result is a tiling with identical
    ///     2 x 2 tiles, each of which is composed of these transforms:
    ///             -----------------
    ///             | 1    |  LR    |
    ///             -----------------
    ///             | TB   |  LR/TB |
    ///             -----------------
    /// </remarks>
    public Pix MirroredTiling(int w, int h)
    {
        GetDimensions(out int wt, out int ht, out int d);
        if (wt <= 0 || ht <= 0)
            throw Lept.Arg("pixMirroredTiling", "pixs size illegal");
        if (d != 8 && d != 32)
            throw Lept.Arg("pixMirroredTiling", "depth not 32 bpp");

        Pix pixd = Create(w, h, d);
        pixd.CopySpp(this);

        int nx = (w + wt - 1) / wt;
        int ny = (h + ht - 1) / ht;
        Pix pixsfx = FlipLR(null, this);
        Pix pixsfy = FlipTB(null, this);
        Pix pixsfxy = FlipTB(null, pixsfx);
        for (int i = 0; i < ny; i++) {
            for (int j = 0; j < nx; j++) {
                Pix pix = this;
                if ((i & 1) != 0 && (j & 1) == 0)
                    pix = pixsfy;
                else if ((i & 1) == 0 && (j & 1) != 0)
                    pix = pixsfx;
                else if ((i & 1) != 0 && (j & 1) != 0)
                    pix = pixsfxy;
                pixd.Rasterop(j * wt, i * ht, wt, ht, L.PIX_SRC, pix, 0, 0);
            }
        }

        return pixd;
    }

    // leptonica: pixFindRepCloseTile (pix3.c)
    /// <summary>Finds a square tile with conforming median intensity and low variance,
    /// outside but near the input box.</summary>
    /// <param name="box">region of pixs to search around</param>
    /// <param name="searchdir">L_HORIZ or L_VERT; direction to search</param>
    /// <param name="mindist">min distance of selected tile edge from box; >= 0</param>
    /// <param name="tsize">tile size; > 1; even; typically ~50</param>
    /// <param name="ntiles">number of tiles tested in each row/column</param>
    /// <param name="pboxtile">region of best tile; null on failure</param>
    /// <param name="debug">1 for debug output</param>
    /// <returns>true if OK; false on error (the C function returns 1; an error is logged)</returns>
    /// <remarks>
    /// This pix (pixs) is 32 bpp rgb.
    /// (1) This looks for one or two square tiles with conforming median
    ///     intensity and low variance, that is outside but near the input box.
    /// (2) %mindist specifies the gap between the box and the
    ///     potential tiles.  The tiles are given an overlap of 50%.
    ///     %ntiles specifies the number of tiles that are tested
    ///     beyond %mindist for each row or column.
    /// (3) For example, if %mindist = 20, %tilesize = 50 and %ntiles = 3,
    ///     a horizontal search to the right will have 3 tiles in each row,
    ///     with left edges at 20, 45 and 70 from the right edge of the
    ///     input %box.  The number of rows of tiles is determined by
    ///     the height of %box and %tsize, with the 50% overlap..
    /// </remarks>
    public bool FindRepCloseTile(Box box, int searchdir, int mindist, int tsize,
                                 int ntiles, out Box? pboxtile, int debug)
    {
        const string func = "pixFindRepCloseTile";

        pboxtile = null;
        if (searchdir != L.L_HORIZ && searchdir != L.L_VERT) {
            Log.Error(func, "invalid searchdir");
            return false;
        }
        if (mindist < 0) {
            Log.Error(func, "mindist must be >= 0");
            return false;
        }
        if (tsize < 2) {
            Log.Error(func, "tsize must be > 1");
            return false;
        }
        if (ntiles > 7) {
            Log.Warning(func, $"ntiles = {ntiles}; larger than suggested max of 7");
        }

            /* Locate tile regions */
        GetDimensions(out int w, out int h, out _);
        Boxa? boxa = findTileRegionsForSearch(box, w, h, searchdir, mindist,
                                              tsize, ntiles);
        if (boxa == null) {
            Log.Error(func, "no tiles found");
            return false;
        }

            /* Generate the tiles and the mean and stdev of intensity */
        Pixa pixa = ClipRectangles(boxa);
        int n = pixa.Count;
        Numa namean = Numa.Create(n);
        Numa nastdev = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            Pix pixg = pix.ConvertRGBToGray(0.33f, 0.34f, 0.33f);
            float mean = pixg.GetAverageMasked(null, 0, 0, 1, L.L_MEAN_ABSVAL);
            float stdev = pixg.GetAverageMasked(null, 0, 0, 1, L.L_STANDARD_DEVIATION);
            namean.AddNumber(mean);
            nastdev.AddNumber(stdev);
        }

            /* Find the median and variance of the averages.  We require
             * the best tile to have a mean pixel intensity within a standard
             * deviation of the median of mean intensities, and choose the
             * tile in that set with the smallest stdev of pixel intensities
             * (as a proxy for the tile with least visible structure).
             * The median of the stdev is used, for debugging, as a normalizing
             * factor for the stdev of intensities within a tile. */
        namean.GetStatsUsingHistogram(256, out _, out _, out _, out float var_of_mean,
                                      out float median_of_mean, 0.0f, out _, out _);
        nastdev.GetStatsUsingHistogram(256, out _, out _, out _, out _,
                                       out float median_of_stdev, 0.0f, out _, out _);
        float mindels = 1000.0f;
        float bestdelm = 1000.0f;
        int bestindex = 0;
        for (int i = 0; i < n; i++) {
            float mean_val = namean.GetFValue(i);
            float stdev_val = nastdev.GetFValue(i);
            float delm, dels;
            if (var_of_mean == 0.0) {  /* uniform color; any box will do */
                delm = 0.0f;  /* any value < 1.01 */
                dels = 1.0f;  /* n'importe quoi */
            } else {
                delm = (float)(Math.Abs(mean_val - median_of_mean) / Math.Sqrt(var_of_mean));
                dels = stdev_val / median_of_stdev;
            }
            if (delm < 1.01) {
                if (dels < mindels) {
                    if (debug != 0) {
                        Console.Error.Write(CFormat.Sprintf(
                            "i = %d, mean = %7.3f, delm = %7.3f, stdev = %7.3f, dels = %7.3f\n",
                            i, mean_val, delm, stdev_val, dels));
                    }
                    mindels = dels;
                    bestdelm = delm;
                    bestindex = i;
                }
            }
        }
        pboxtile = boxa.GetBox(bestindex, L.L_COPY);

        if (debug != 0) {
            Log.Info(func, CFormat.Sprintf("median of mean = %7.3f", median_of_mean));
            Log.Info(func, CFormat.Sprintf("standard dev of mean = %7.3f", Math.Sqrt(var_of_mean)));
            Log.Info(func, CFormat.Sprintf("median of stdev = %7.3f", median_of_stdev));
            Log.Info(func, CFormat.Sprintf("best tile: index = %d", bestindex));
            Log.Info(func, CFormat.Sprintf("delta from median in units of stdev = %5.3f", bestdelm));
            Log.Info(func, CFormat.Sprintf("stdev as fraction of median stdev = %5.3f", mindels));
        }

        return true;
    }

    // leptonica: findTileRegionsForSearch (pix3.c)
    /// <param name="box">region of Pix to search around</param>
    /// <param name="w">dimensions of Pix</param>
    /// <param name="h">dimensions of Pix</param>
    /// <param name="searchdir">L_HORIZ or L_VERT; direction to search</param>
    /// <param name="mindist">min distance of selected tile edge from box; >= 0</param>
    /// <param name="tsize">tile size; > 1; even; typically ~50</param>
    /// <param name="ntiles">number of tiles tested in each row/column</param>
    /// <returns>boxa if OK, or null on error (the error is logged)</returns>
    /// <remarks>
    /// (1) See calling function pixfindRepCloseTile().
    /// </remarks>
    private static Boxa? findTileRegionsForSearch(Box box, int w, int h, int searchdir,
                                                  int mindist, int tsize, int ntiles)
    {
        const string func = "findTileRegionsForSearch";

        if (ntiles == 0) {
            Log.Error(func, "no tiles requested");
            return null;
        }

        box.GetGeometry(out int bx, out int by, out int bw, out int bh);
        Boxa boxa;
        if (searchdir == L.L_HORIZ) {
                /* Find the tile parameters for the search.  Note that the
                 * tiles are overlapping by 50% in each direction. */
            int left = bx;   /* distance to left of box */
            int right = w - bx - bw + 1;   /* distance to right of box */
            int w_avail = Math.Max(left, right) - mindist;
            if ((tsize & 1) != 0) tsize++;  /* be sure it's even */
            if (w_avail < tsize) {
                Log.Error(func, $"tsize = {tsize}, w_avail = {w_avail}");
                return null;
            }
            int w_needed = tsize + (ntiles - 1) * (tsize / 2);
            if (w_needed > w_avail) {
                int t_avail = 1 + 2 * (w_avail - tsize) / tsize;
                Log.Warning(func, $"ntiles = {ntiles}; room for only {t_avail}");
                ntiles = t_avail;
                w_needed = tsize + (ntiles - 1) * (tsize / 2);
            }
            int nrows = Math.Max(1, 1 + 2 * (bh - tsize) / tsize);

                /* Generate the tile regions to search */
            boxa = Boxa.Create(0);
            int x0;
            if (left > right)  /* search to left */
                x0 = bx - w_needed;
            else  /* search to right */
                x0 = bx + bw + mindist;
            for (int i = 0; i < nrows; i++) {
                int y = by + i * tsize / 2;
                for (int j = 0; j < ntiles; j++) {
                    int x = x0 + j * tsize / 2;
                    Box box1 = Box.Create(x, y, tsize, tsize);
                    boxa.AddBox(box1, L.L_INSERT);
                }
            }
        } else {  /* L_VERT */
                /* Find the tile parameters for the search */
            int top = by;   /* distance above box */
            int bot = h - by - bh + 1;   /* distance below box */
            int h_avail = Math.Max(top, bot) - mindist;
            if (h_avail < tsize) {
                Log.Error(func, $"tsize = {tsize}, h_avail = {h_avail}");
                return null;
            }
            int h_needed = tsize + (ntiles - 1) * (tsize / 2);
            if (h_needed > h_avail) {
                int t_avail = 1 + 2 * (h_avail - tsize) / tsize;
                Log.Warning(func, $"ntiles = {ntiles}; room for only {t_avail}");
                ntiles = t_avail;
                h_needed = tsize + (ntiles - 1) * (tsize / 2);
            }
            int ncols = Math.Max(1, 1 + 2 * (bw - tsize) / tsize);

                /* Generate the tile regions to search */
            boxa = Boxa.Create(0);
            int y0;
            if (top > bot)  /* search above */
                y0 = by - h_needed;
            else  /* search below */
                y0 = by + bh + mindist;
            for (int j = 0; j < ncols; j++) {
                int x = bx + j * tsize / 2;
                for (int i = 0; i < ntiles; i++) {
                    int y = y0 + i * tsize / 2;
                    Box box1 = Box.Create(x, y, tsize, tsize);
                    boxa.AddBox(box1, L.L_INSERT);
                }
            }
        }
        return boxa;
    }
}
