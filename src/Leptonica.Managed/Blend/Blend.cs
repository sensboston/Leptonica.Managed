using System;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  blend.c: Blending of images.
 *
 *  In blending operations a new pix is produced where typically
 *  a subset of pixels in src1 are changed by the set of pixels
 *  in src2, when src2 is located in a given position relative
 *  to src1.  This is similar to rasterop, except that the
 *  blending operations we allow are more complex, and typically
 *  result in dest pixels that are a linear combination of two
 *  pixels, such as src1 and its inverse.  I find it convenient
 *  to think of src2 as the "blender" (the one that takes the action)
 *  and src1 as the "blendee" (the one that changes).
 *
 *  Blending works best when src1 is 8 or 32 bpp.  We also allow
 *  src1 to be colormapped, but the colormap is removed before blending,
 *  so if src1 is colormapped, we can't allow in-place blending.
 *
 *  Because src2 is typically smaller than src1, we can implement by
 *  clipping src2 to src1 and then transforming some of the dest
 *  pixels that are under the support of src2.  In practice, we
 *  do the clipping in the inner pixel loop.  For grayscale and
 *  color src2, we also allow a simple form of transparency, where
 *  pixels of a particular value in src2 are transparent; for those pixels,
 *  no blending is done.
 *
 *  The blending functions are categorized by the depth of src2,
 *  the blender, and not that of src1, the blendee.
 *
 *   ~ If src2 is 1 bpp, we can do one of three things:
 *     (1) L_BLEND_WITH_INVERSE: Blend a given fraction of src1 with its
 *         inverse color for those pixels in src2 that are fg (ON),
 *         and leave the dest pixels unchanged for pixels in src2 that
 *         are bg (OFF).
 *     (2) L_BLEND_TO_WHITE: Fade the src1 pixels toward white by a
 *         given fraction for those pixels in src2 that are fg (ON),
 *         and leave the dest pixels unchanged for pixels in src2 that
 *         are bg (OFF).
 *     (3) L_BLEND_TO_BLACK: Fade the src1 pixels toward black by a
 *         given fraction for those pixels in src2 that are fg (ON),
 *         and leave the dest pixels unchanged for pixels in src2 that
 *         are bg (OFF).
 *     The blending function is pixBlendMask().
 *
 *   ~ If src2 is 8 bpp grayscale, we can do one of two things
 *     (but see pixFadeWithGray() below):
 *     (1) L_BLEND_GRAY: If src1 is 8 bpp, mix the two values, using
 *         a fraction of src2 and (1 - fraction) of src1.
 *         If src1 is 32 bpp (rgb), mix the fraction of src2 with
 *         each of the color components in src1.
 *     (2) L_BLEND_GRAY_WITH_INVERSE: Use the grayscale value in src2
 *         to determine how much of the inverse of a src1 pixel is
 *         to be combined with the pixel value.  The input fraction
 *         further acts to scale the change in the src1 pixel.
 *     The blending function is pixBlendGray().
 *
 *   ~ If src2 is color, we blend a given fraction of src2 with
 *     src1.  If src1 is 8 bpp, the resulting image is 32 bpp.
 *     The blending function is pixBlendColor().
 *
 *   ~ For all three blending functions -- pixBlendMask(), pixBlendGray()
 *     and pixBlendColor() -- you can apply the blender to the blendee
 *     either in-place or generating a new pix.  For the in-place
 *     operation, this requires that the depth of the resulting pix
 *     must equal that of the input pixs1.
 *
 *   ~ We remove colormaps from src1 and src2 before blending.
 *     Any quantization would have to be done after blending.
 *
 *  We include another function, pixFadeWithGray(), that blends
 *  a gray or color src1 with a gray src2.  It does one of these things:
 *     (1) L_BLEND_TO_WHITE: Fade the src1 pixels toward white by
 *         a number times the value in src2.
 *     (2) L_BLEND_TO_BLACK: Fade the src1 pixels toward black by
 *         a number times the value in src2.
 *
 *  Also included is a generalization of the so-called "hard light"
 *  blending: pixBlendHardLight().  We generalize by allowing a fraction < 1.0
 *  of the blender to be admixed with the blendee.  The standard function
 *  does full mixing.
 *
 *  Differences from C:
 *    - The functions with an optional destination %pixd (NULL or equal to
 *      %pixs1 for in-place operation) are static methods with the C argument
 *      order; pixBlend, pixBlendWithGrayMask, pixAlphaBlendUniform,
 *      pixFadeWithGray, pixBlendCmap, pixAddAlphaToBlend,
 *      pixSetAlphaOverWhite and pixLinearEdgeFade are instance methods.
 *    - Where the C function returns %pixd or %pixs1 on error (after printing
 *      the error), the managed function throws.
 *    - pixBlend returns null if pixs2 does not overlap pixs1 (C: NULL after a
 *      warning).
 *    - pixBlendCmap and pixLinearEdgeFade return void.
 *    - In pixBlendMask, the C conversion of a float to l_int32 (an
 *      undefined behavior outside the range of l_int32) is reproduced as the
 *      x86 instruction does it: see CastToInt.
 *
 *  Not ported: nothing.
 *
 *  Stubs for callees in other files: none.
 */

/// <summary>Constants from pix.h used by blend.c.</summary>
public static partial class L
{
    /* Blend types */

    /// <summary>Adds some of the inverse of the source to itself.</summary>
    public const int L_BLEND_WITH_INVERSE = 1;

    /// <summary>Shifts the source colors towards white.</summary>
    public const int L_BLEND_TO_WHITE = 2;

    /// <summary>Shifts the source colors towards black.</summary>
    public const int L_BLEND_TO_BLACK = 3;

    /// <summary>Blends the source directly with the blender.</summary>
    public const int L_BLEND_GRAY = 4;

    /// <summary>Adds an amount of the inverse of the source to itself, based on the blender pixel value.</summary>
    public const int L_BLEND_GRAY_WITH_INVERSE = 5;
}

public sealed partial class Pix
{
    // C converts a float to l_int32 with cvttss2si, which gives INT_MIN for values out of range and
    // for NaN; .NET 9+ saturates, so that is reproduced here
    private static int CastToInt(float val)
    {
        if (float.IsNaN(val) || val >= 2147483648.0f || val < -2147483648.0f)
            return int.MinValue;
        return (int)val;
    }

    /*-------------------------------------------------------------*
     *         Blending two images that are not colormapped        *
     *-------------------------------------------------------------*/
    // leptonica: pixBlend (blend.c)
    /// <summary>Blends pixs2 into this pix (pixs1), with the UL corner of pixs2 at (x, y).</summary>
    /// <param name="pixs2">blender; typ. smaller</param>
    /// <param name="x">origin [UL corner] of pixs2 relative to the origin of pixs1; can be &lt; 0</param>
    /// <param name="y">origin [UL corner] of pixs2 relative to the origin of pixs1; can be &lt; 0</param>
    /// <param name="fract">blending fraction</param>
    /// <returns>pixd blended image; null if pixs2 does not overlap pixs1</returns>
    /// <remarks>
    /// (1) This is a simple top-level interface.  For more flexibility,
    ///     call directly into pixBlendMask(), etc.
    /// </remarks>
    public Pix? Blend(Pix pixs2, int x, int y, float fract)
    {
        const string func = "pixBlend";
        int w1, h1, d1, d2;
        Box box;
        Pix? pixc;
        Pix pixt, pixd;
        Pix pixs1 = this;

        /* check relative depths */
        d1 = pixs1.Depth;
        d2 = pixs2.Depth;
        if (d1 == 1 && d2 > 1)
            throw Lept.Arg(func, "mixing gray or color with 1 bpp");

        /* remove colormap from pixs2 if necessary */
        pixt = pixs2.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        d2 = pixt.Depth;

        /* Check if pixs2 is clipped by its position with respect
         * to pixs1; if so, clip it and redefine x and y if necessary.
         * This actually isn't necessary, as the specific blending
         * functions do the clipping directly in the pixel loop
         * over pixs2, but it's included here to show how it can
         * easily be done on pixs2 first. */
        pixs1.GetDimensions(out w1, out h1, out _);
        try {
            box = Box.Create(-x, -y, w1, h1);  /* box of pixs1 relative to pixs2 */
        } catch (ArgumentException) {  /* C: boxCreate fails and pixClipRectangle gets no box */
            Log.Warning(func, "box doesn't overlap pix");
            return null;
        }
        pixc = pixt.ClipRectangle(box, out _);
        if (pixc == null) {
            Log.Warning(func, "box doesn't overlap pix");
            return null;
        }
        x = Math.Max(0, x);
        y = Math.Max(0, y);

        if (d2 == 1) {
            pixd = BlendMask(null, pixs1, pixc, x, y, fract,
                             L.L_BLEND_WITH_INVERSE);
        } else if (d2 == 8) {
            pixd = BlendGray(null, pixs1, pixc, x, y, fract,
                             L.L_BLEND_GRAY, 0, 0);
        } else {  /* d2 == 32 */
            pixd = BlendColor(null, pixs1, pixc, x, y, fract, 0, 0);
        }

        return pixd;
    }

    // leptonica: pixBlendMask (blend.c)
    /// <summary>Blends a 1 bpp blender into a pix, for the fg pixels of the blender.</summary>
    /// <param name="pixd">[optional]; either null or equal to pixs1 for in-place</param>
    /// <param name="pixs1">blendee, depth &gt; 1</param>
    /// <param name="pixs2">blender, 1 bpp; typ. smaller in size than pixs1</param>
    /// <param name="x">origin [UL corner] of pixs2 relative to the origin of pixs1; can be &lt; 0</param>
    /// <param name="y">origin [UL corner] of pixs2 relative to the origin of pixs1; can be &lt; 0</param>
    /// <param name="fract">blending fraction</param>
    /// <param name="type">L_BLEND_WITH_INVERSE, L_BLEND_TO_WHITE, L_BLEND_TO_BLACK</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Clipping of pixs2 to pixs1 is done in the inner pixel loop.
    /// (2) If pixs1 has a colormap, it is removed.
    /// (3) For inplace operation (pixs1 not cmapped), call it this way:
    ///       pixBlendMask(pixs1, pixs1, pixs2, ...)
    /// (4) For generating a new pixd:
    ///       pixd = pixBlendMask(NULL, pixs1, pixs2, ...)
    /// (5) Only call in-place if pixs1 does not have a colormap.
    /// (6) Invalid %fract defaults to 0.5 with a warning.
    ///     Invalid %type defaults to L_BLEND_WITH_INVERSE with a warning.
    /// </remarks>
    public static Pix BlendMask(Pix? pixd, Pix pixs1, Pix pixs2, int x, int y, float fract, int type)
    {
        const string func = "pixBlendMask";
        int i, j, d, wc, hc, w, h, wplc;
        int val, rval, gval, bval;
        uint pixval;
        Pix pixc, pix1, pix2;

        if (pixs1.Depth == 1)
            throw Lept.Arg(func, "pixs1 is 1 bpp");
        if (pixs2.Depth != 1)
            throw Lept.Arg(func, "pixs2 not 1 bpp");
        if (pixd == pixs1 && pixs1.Colormap != null)
            throw Lept.Arg(func, "inplace; pixs1 has colormap");
        if (pixd != null && (pixd != pixs1))
            throw Lept.Arg(func, "pixd must be NULL or pixs1");
        if (fract < 0.0 || fract > 1.0) {
            Log.Warning(func, "fract must be in [0.0, 1.0]; setting to 0.5");
            fract = 0.5f;
        }
        if (type != L.L_BLEND_WITH_INVERSE && type != L.L_BLEND_TO_WHITE &&
            type != L.L_BLEND_TO_BLACK) {
            Log.Warning(func, "invalid blend type; setting to L_BLEND_WITH_INVERSE");
            type = L.L_BLEND_WITH_INVERSE;
        }

        /* If pixd != NULL, we know that it is equal to pixs1 and
         * that pixs1 does not have a colormap, so that an in-place operation
         * can be done.  Otherwise, remove colormap from pixs1 if
         * it exists and unpack to at least 8 bpp if necessary,
         * to do the blending on a new pix. */
        if (pixd == null) {
            pix1 = pixs1.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            if (pix1.Depth < 8)
                pix2 = pix1.ConvertTo8(false);
            else
                pix2 = Clone(pix1);
            pixd = Copy(null, pix2);
        }

        pixd.GetDimensions(out w, out h, out d);  /* d must be either 8 or 32 bpp */
        pixc = Clone(pixs2);
        wc = pixc.Width;
        hc = pixc.Height;
        uint[] datac = pixc.Data;
        wplc = pixc.Wpl;

        /* Check limits for src1, in case clipping was not done. */
        switch (type)
        {
        case L.L_BLEND_WITH_INVERSE:
            /*
             * The basic logic for this blending is:
             *      p -->  (1 - f) * p + f * (1 - p)
             * where p is a normalized value: p = pixval / 255.
             * Thus,
             *      p -->  p + f * (1 - 2 * p)
             */
            for (i = 0; i < hc; i++) {
                if (i + y < 0  || i + y >= h) continue;
                ReadOnlySpan<uint> linec = datac.AsSpan(i * wplc);
                for (j = 0; j < wc; j++) {
                    if (j + x < 0  || j + x >= w) continue;
                    bval = GetDataBit(linec, j);
                    if (bval != 0) {
                        switch (d)
                        {
                        case 8:
                            pixd.GetPixel(x + j, y + i, out pixval);
                            // NOTE(upstream): pixval is unsigned, so 255 - 2 * pixval wraps for
                            // pixval > 127; the result is the low byte of a garbage value
                            val = CastToInt(pixval + fract * (255u - 2u * pixval));
                            pixd.SetPixel(x + j, y + i, (uint)val);
                            break;
                        case 32:
                            pixd.GetPixel(x + j, y + i, out pixval);
                            ExtractRGBValues(pixval, out rval, out gval, out bval);
                            rval = (int)(rval + fract * (255 - 2 * rval));
                            gval = (int)(gval + fract * (255 - 2 * gval));
                            bval = (int)(bval + fract * (255 - 2 * bval));
                            ComposeRGBPixel(rval, gval, bval, out pixval);
                            pixd.SetPixel(x + j, y + i, pixval);
                            break;
                        default:
                            Log.Warning(func, "d neither 8 nor 32 bpp; no blend");
                            break;
                        }
                    }
                }
            }
            break;
        case L.L_BLEND_TO_WHITE:
            /*
             * The basic logic for this blending is:
             *      p -->  p + f * (1 - p)    (p normalized to [0...1])
             */
            for (i = 0; i < hc; i++) {
                if (i + y < 0  || i + y >= h) continue;
                ReadOnlySpan<uint> linec = datac.AsSpan(i * wplc);
                for (j = 0; j < wc; j++) {
                    if (j + x < 0  || j + x >= w) continue;
                    bval = GetDataBit(linec, j);
                    if (bval != 0) {
                        switch (d)
                        {
                        case 8:
                            pixd.GetPixel(x + j, y + i, out pixval);
                            val = (int)(pixval + fract * (255 - pixval));
                            pixd.SetPixel(x + j, y + i, (uint)val);
                            break;
                        case 32:
                            pixd.GetPixel(x + j, y + i, out pixval);
                            ExtractRGBValues(pixval, out rval, out gval, out bval);
                            rval = (int)(rval + fract * (255 - rval));
                            gval = (int)(gval + fract * (255 - gval));
                            bval = (int)(bval + fract * (255 - bval));
                            ComposeRGBPixel(rval, gval, bval, out pixval);
                            pixd.SetPixel(x + j, y + i, pixval);
                            break;
                        default:
                            Log.Warning(func, "d neither 8 nor 32 bpp; no blend");
                            break;
                        }
                    }
                }
            }
            break;
        case L.L_BLEND_TO_BLACK:
            /*
             * The basic logic for this blending is:
             *      p -->  (1 - f) * p     (p normalized to [0...1])
             */
            for (i = 0; i < hc; i++) {
                if (i + y < 0  || i + y >= h) continue;
                ReadOnlySpan<uint> linec = datac.AsSpan(i * wplc);
                for (j = 0; j < wc; j++) {
                    if (j + x < 0  || j + x >= w) continue;
                    bval = GetDataBit(linec, j);
                    if (bval != 0) {
                        switch (d)
                        {
                        case 8:
                            pixd.GetPixel(x + j, y + i, out pixval);
                            val = (int)((1.0 - fract) * pixval);
                            pixd.SetPixel(x + j, y + i, (uint)val);
                            break;
                        case 32:
                            pixd.GetPixel(x + j, y + i, out pixval);
                            ExtractRGBValues(pixval, out rval, out gval, out bval);
                            rval = (int)((1.0 - fract) * rval);
                            gval = (int)((1.0 - fract) * gval);
                            bval = (int)((1.0 - fract) * bval);
                            ComposeRGBPixel(rval, gval, bval, out pixval);
                            pixd.SetPixel(x + j, y + i, pixval);
                            break;
                        default:
                            Log.Warning(func, "d neither 8 nor 32 bpp; no blend");
                            break;
                        }
                    }
                }
            }
            break;
        default:
            Log.Warning(func, "invalid binary mask blend type");
            break;
        }

        return pixd;
    }

    // leptonica: pixBlendGray (blend.c)
    /// <summary>Blends a blender of any depth, converted to gray, into a pix.</summary>
    /// <param name="pixd">[optional] either equal to pixs1 for in-place, or null</param>
    /// <param name="pixs1">blendee, depth &gt; 1</param>
    /// <param name="pixs2">blender, any depth; typically, the area of pixs2 is smaller than pixs1</param>
    /// <param name="x">origin [UL corner] of pixs2 relative to the origin of pixs1; can be &lt; 0</param>
    /// <param name="y">origin [UL corner] of pixs2 relative to the origin of pixs1; can be &lt; 0</param>
    /// <param name="fract">blending fraction</param>
    /// <param name="type">L_BLEND_GRAY, L_BLEND_GRAY_WITH_INVERSE</param>
    /// <param name="transparent">1 to use transparency; 0 otherwise</param>
    /// <param name="transpix">pixel grayval in pixs2 that is to be transparent</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) For inplace operation (pixs1 not cmapped), call it this way:
    ///       pixBlendGray(pixs1, pixs1, pixs2, ...)
    /// (2) For generating a new pixd:
    ///       pixd = pixBlendGray(NULL, pixs1, pixs2, ...)
    /// (3) Clipping of pixs2 to pixs1 is done in the inner pixel loop.
    /// (4) If pixs1 has a colormap, it is removed; otherwise, if pixs1
    ///     has depth &lt; 8, it is unpacked to generate a 8 bpp pix.
    /// (5) If transparent = 0, the blending fraction (fract) is
    ///     applied equally to all pixels.
    /// (6) If transparent = 1, all pixels of value transpix (typically
    ///     either 0 or 0xff) in pixs2 are transparent in the blend.
    /// (7) After processing pixs1, it is either 8 bpp or 32 bpp:
    ///     ~ if 8 bpp, the fraction of pixs2 is mixed with pixs1.
    ///     ~ if 32 bpp, each component of pixs1 is mixed with
    ///       the same fraction of pixs2.
    /// (8) For L_BLEND_GRAY_WITH_INVERSE, the white values of the blendee
    ///     (cval == 255 in the code below) result in a delta of 0.
    ///     Thus, these pixels are intrinsically transparent!
    ///     The "pivot" value of the src, at which no blending occurs, is
    ///     128.  Compare with the adaptive pivot in pixBlendGrayAdapt().
    /// (9) Invalid %fract defaults to 0.5 with a warning.
    ///     Invalid %type defaults to L_BLEND_GRAY with a warning.
    /// </remarks>
    public static Pix BlendGray(Pix? pixd, Pix pixs1, Pix pixs2, int x, int y, float fract,
                                int type, int transparent, uint transpix)
    {
        const string func = "pixBlendGray";
        int i, j, d, wc, hc, w, h, wplc, wpld, delta;
        int ival, irval, igval, ibval, cval, dval;
        uint val32;
        Pix pixc, pix1, pix2;

        if (pixs1.Depth == 1)
            throw Lept.Arg(func, "pixs1 is 1 bpp");
        if (pixd == pixs1 && pixs1.Colormap != null)
            throw Lept.Arg(func, "can't do in-place with cmap");
        if (pixd != null && (pixd != pixs1))
            throw Lept.Arg(func, "pixd must be NULL or pixs1");
        if (fract < 0.0 || fract > 1.0) {
            Log.Warning(func, "fract must be in [0.0, 1.0]; setting to 0.5");
            fract = 0.5f;
        }
        if (type != L.L_BLEND_GRAY && type != L.L_BLEND_GRAY_WITH_INVERSE) {
            Log.Warning(func, "invalid blend type; setting to L_BLEND_GRAY");
            type = L.L_BLEND_GRAY;
        }

        /* If pixd != NULL, we know that it is equal to pixs1 and
         * that pixs1 does not have a colormap, so that an in-place operation
         * can be done.  Otherwise, remove colormap from pixs1 if
         * it exists and unpack to at least 8 bpp if necessary,
         * to do the blending on a new pix. */
        if (pixd == null) {
            pix1 = pixs1.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            if (pix1.Depth < 8)
                pix2 = pix1.ConvertTo8(false);
            else
                pix2 = Clone(pix1);
            pixd = Copy(null, pix2);
        }

        pixd.GetDimensions(out w, out h, out d);  /* 8 or 32 bpp */
        wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        pixc = pixs2.ConvertTo8(false);
        pixc.GetDimensions(out wc, out hc, out _);
        uint[] datac = pixc.Data;
        wplc = pixc.Wpl;

        /* Check limits for src1, in case clipping was not done */
        if (type == L.L_BLEND_GRAY) {
            /*
             * The basic logic for this blending is:
             *      p -->  (1 - f) * p + f * c
             * where c is the 8 bpp blender.  All values are normalized to [0...1].
             */
            for (i = 0; i < hc; i++) {
                if (i + y < 0  || i + y >= h) continue;
                ReadOnlySpan<uint> linec = datac.AsSpan(i * wplc);
                Span<uint> lined = datad.AsSpan((i + y) * wpld);
                switch (d)
                {
                case 8:
                    for (j = 0; j < wc; j++) {
                        if (j + x < 0  || j + x >= w) continue;
                        cval = GetDataByte(linec, j);
                        if (transparent == 0 || cval != transpix) {
                            dval = GetDataByte(lined, j + x);
                            ival = (int)((1.0 - fract) * dval + fract * cval);
                            SetDataByte(lined, j + x, ival);
                        }
                    }
                    break;
                case 32:
                    for (j = 0; j < wc; j++) {
                        if (j + x < 0  || j + x >= w) continue;
                        cval = GetDataByte(linec, j);
                        if (transparent == 0 || cval != transpix) {
                            val32 = lined[j + x];
                            ExtractRGBValues(val32, out irval, out igval, out ibval);
                            irval = (int)((1.0 - fract) * irval + fract * cval);
                            igval = (int)((1.0 - fract) * igval + fract * cval);
                            ibval = (int)((1.0 - fract) * ibval + fract * cval);
                            ComposeRGBPixel(irval, igval, ibval, out val32);
                            lined[j + x] = val32;
                        }
                    }
                    break;
                default:
                    break;   /* shouldn't happen */
                }
            }
        } else {  /* L_BLEND_GRAY_WITH_INVERSE */
            for (i = 0; i < hc; i++) {
                if (i + y < 0  || i + y >= h) continue;
                ReadOnlySpan<uint> linec = datac.AsSpan(i * wplc);
                Span<uint> lined = datad.AsSpan((i + y) * wpld);
                switch (d)
                {
                case 8:
                    /*
                     * For 8 bpp, the dest pix is shifted by a signed amount
                     * proportional to the distance from 128 (the pivot value),
                     * and to the darkness of src2.  If the dest is darker
                     * than 128, it becomes lighter, and v.v.
                     * The basic logic is:
                     *     d  -->  d + f * (0.5 - d) * (1 - c)
                     * where d and c are normalized pixel values for src1 and
                     * src2, respectively, with 8 bit normalization to [0...1].
                     */
                    for (j = 0; j < wc; j++) {
                        if (j + x < 0  || j + x >= w) continue;
                        cval = GetDataByte(linec, j);
                        if (transparent == 0 || cval != transpix) {
                            ival = GetDataByte(lined, j + x);
                            delta = (128 - ival) * (255 - cval) / 256;
                            ival += (int)(fract * delta + 0.5);
                            SetDataByte(lined, j + x, ival);
                        }
                    }
                    break;
                case 32:
                    /* Each component is shifted by the same formula for 8 bpp */
                    for (j = 0; j < wc; j++) {
                        if (j + x < 0  || j + x >= w) continue;
                        cval = GetDataByte(linec, j);
                        if (transparent == 0 || cval != transpix) {
                            val32 = lined[j + x];
                            ExtractRGBValues(val32, out irval, out igval, out ibval);
                            delta = (128 - irval) * (255 - cval) / 256;
                            irval += (int)(fract * delta + 0.5);
                            delta = (128 - igval) * (255 - cval) / 256;
                            igval += (int)(fract * delta + 0.5);
                            delta = (128 - ibval) * (255 - cval) / 256;
                            ibval += (int)(fract * delta + 0.5);
                            ComposeRGBPixel(irval, igval, ibval, out val32);
                            lined[j + x] = val32;
                        }
                    }
                    break;
                default:
                    break;   /* shouldn't happen */
                }
            }
        }

        return pixd;
    }

    // leptonica: pixBlendGrayInverse (blend.c)
    /// <summary>Blends a gray blender into a pix, with maximum change for dark blender pixels.</summary>
    /// <param name="pixd">[optional] either equal to pixs1 for in-place, or null</param>
    /// <param name="pixs1">blendee, depth &gt; 1</param>
    /// <param name="pixs2">blender, any depth; typ. smaller in size than pixs1</param>
    /// <param name="x">origin [UL corner] of pixs2 relative to the origin of pixs1; can be &lt; 0</param>
    /// <param name="y">origin [UL corner] of pixs2 relative to the origin of pixs1; can be &lt; 0</param>
    /// <param name="fract">blending fraction</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) For inplace operation (pixs1 not cmapped), call it this way:
    ///       pixBlendGrayInverse(pixs1, pixs1, pixs2, ...)
    /// (2) For generating a new pixd:
    ///       pixd = pixBlendGrayInverse(NULL, pixs1, pixs2, ...)
    /// (3) Clipping of pixs2 to pixs1 is done in the inner pixel loop.
    /// (4) If pixs1 has a colormap, it is removed; otherwise if pixs1
    ///     has depth &lt; 8, it is unpacked to generate a 8 bpp pix.
    /// (5) This is a no-nonsense blender.  It changes the src1 pixel except
    ///     when the src1 pixel is midlevel gray.  Use fract == 1 for the most
    ///     aggressive blending, where, if the gray pixel in pixs2 is 0,
    ///     we get a complete inversion of the color of the src pixel in pixs1.
    /// (6) The basic logic is that each component transforms by:
    ///          d  -->  c * d + (1 - c ) * (f * (1 - d) + d * (1 - f))
    ///     where c is the blender pixel from pixs2,
    ///           f is %fract,
    ///           c and d are normalized to [0...1]
    ///     This has the property that for f == 0 (no blend) or c == 1 (white):
    ///          d  -->  d
    ///     For c == 0 (black) we get maximum inversion:
    ///          d  -->  f * (1 - d) + d * (1 - f)   [inversion by fraction f]
    /// </remarks>
    public static Pix BlendGrayInverse(Pix? pixd, Pix pixs1, Pix pixs2, int x, int y, float fract)
    {
        const string func = "pixBlendGrayInverse";
        int i, j, d, wc, hc, w, h, wplc, wpld;
        int irval, igval, ibval, cval, dval;
        float a;
        uint val32;
        Pix pixc, pix1, pix2;

        if (pixs1.Depth == 1)
            throw Lept.Arg(func, "pixs1 is 1 bpp");
        if (pixd == pixs1 && pixs1.Colormap != null)
            throw Lept.Arg(func, "can't do in-place with cmap");
        if (pixd != null && (pixd != pixs1))
            throw Lept.Arg(func, "pixd must be NULL or pixs1");
        if (fract < 0.0 || fract > 1.0) {
            Log.Warning(func, "fract must be in [0.0, 1.0]; setting to 0.5");
            fract = 0.5f;
        }

        /* If pixd != NULL, we know that it is equal to pixs1 and
         * that pixs1 does not have a colormap, so that an in-place operation
         * can be done.  Otherwise, remove colormap from pixs1 if
         * it exists and unpack to at least 8 bpp if necessary,
         * to do the blending on a new pix. */
        if (pixd == null) {
            pix1 = pixs1.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            if (pix1.Depth < 8)
                pix2 = pix1.ConvertTo8(false);
            else
                pix2 = Clone(pix1);
            pixd = Copy(null, pix2);
        }

        pixd.GetDimensions(out w, out h, out d);  /* 8 or 32 bpp */
        wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        pixc = pixs2.ConvertTo8(false);
        pixc.GetDimensions(out wc, out hc, out _);
        uint[] datac = pixc.Data;
        wplc = pixc.Wpl;

        /* Check limits for src1, in case clipping was not done */
        for (i = 0; i < hc; i++) {
            if (i + y < 0  || i + y >= h) continue;
            ReadOnlySpan<uint> linec = datac.AsSpan(i * wplc);
            Span<uint> lined = datad.AsSpan((i + y) * wpld);
            switch (d)
            {
            case 8:
                for (j = 0; j < wc; j++) {
                    if (j + x < 0  || j + x >= w) continue;
                    cval = GetDataByte(linec, j);
                    dval = GetDataByte(lined, j + x);
                    a = (float)((1.0 - fract) * dval + fract * (255.0 - dval));
                    dval = (int)(cval * dval / 255.0 +
                                 a * (255.0 - cval) / 255.0);
                    SetDataByte(lined, j + x, dval);
                }
                break;
            case 32:
                for (j = 0; j < wc; j++) {
                    if (j + x < 0  || j + x >= w) continue;
                    cval = GetDataByte(linec, j);
                    val32 = lined[j + x];
                    ExtractRGBValues(val32, out irval, out igval, out ibval);
                    a = (float)((1.0 - fract) * irval + fract * (255.0 - irval));
                    irval = (int)(cval * irval / 255.0 +
                                  a * (255.0 - cval) / 255.0);
                    a = (float)((1.0 - fract) * igval + fract * (255.0 - igval));
                    igval = (int)(cval * igval / 255.0 +
                                  a * (255.0 - cval) / 255.0);
                    a = (float)((1.0 - fract) * ibval + fract * (255.0 - ibval));
                    ibval = (int)(cval * ibval / 255.0 +
                                  a * (255.0 - cval) / 255.0);
                    ComposeRGBPixel(irval, igval, ibval, out val32);
                    lined[j + x] = val32;
                }
                break;
            default:
                break;   /* shouldn't happen */
            }
        }

        return pixd;
    }

    // leptonica: pixBlendColor (blend.c)
    /// <summary>Blends a color blender into a pix, with a uniform fraction.</summary>
    /// <param name="pixd">[optional] either equal to pixs1 for in-place, or null</param>
    /// <param name="pixs1">blendee; depth &gt; 1</param>
    /// <param name="pixs2">blender, any depth; typically, the area of pixs2 is smaller than pixs1</param>
    /// <param name="x">origin [UL corner] of pixs2 relative to the origin of pixs1</param>
    /// <param name="y">origin [UL corner] of pixs2 relative to the origin of pixs1</param>
    /// <param name="fract">blending fraction</param>
    /// <param name="transparent">1 to use transparency; 0 otherwise</param>
    /// <param name="transpix">pixel color in pixs2 that is to be transparent</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) For inplace operation (pixs1 must be 32 bpp), call it this way:
    ///       pixBlendColor(pixs1, pixs1, pixs2, ...)
    /// (2) For generating a new pixd:
    ///       pixd = pixBlendColor(NULL, pixs1, pixs2, ...)
    /// (3) If pixs2 is not 32 bpp rgb, it is converted.
    /// (4) Clipping of pixs2 to pixs1 is done in the inner pixel loop.
    /// (5) If pixs1 has a colormap, it is removed to generate a 32 bpp pix.
    /// (6) If pixs1 has depth &lt; 32, it is unpacked to generate a 32 bpp pix.
    /// (7) If transparent = 0, the blending fraction (fract) is
    ///     applied equally to all pixels.
    /// (8) If transparent = 1, all pixels of value transpix (typically
    ///     either 0 or 0xffffff00) in pixs2 are transparent in the blend.
    /// </remarks>
    public static Pix BlendColor(Pix? pixd, Pix pixs1, Pix pixs2, int x, int y, float fract,
                                 int transparent, uint transpix)
    {
        const string func = "pixBlendColor";
        int i, j, wc, hc, w, h, wplc, wpld;
        int rval, gval, bval, rcval, gcval, bcval;
        uint cval32, val32;
        Pix pixc;

        if (pixs1.Depth == 1)
            throw Lept.Arg(func, "pixs1 is 1 bpp");
        if (pixd == pixs1 && pixs1.Depth != 32)
            throw Lept.Arg(func, "inplace; pixs1 not 32 bpp");
        if (pixd != null && (pixd != pixs1))
            throw Lept.Arg(func, "pixd must be NULL or pixs1");
        if (fract < 0.0 || fract > 1.0) {
            Log.Warning(func, "fract must be in [0.0, 1.0]; setting to 0.5");
            fract = 0.5f;
        }

        /* If pixd != null, we know that it is equal to pixs1 and
         * that pixs1 is 32 bpp rgb, so that an in-place operation
         * can be done.  Otherwise, pixConvertTo32() will remove a
         * colormap from pixs1 if it exists and unpack to 32 bpp
         * (if necessary) to do the blending on a new 32 bpp Pix. */
        if (pixd == null)
            pixd = pixs1.ConvertTo32();
        pixd.GetDimensions(out w, out h, out _);
        wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        pixc = pixs2.ConvertTo32();  /* blend with 32 bpp rgb */
        pixc.GetDimensions(out wc, out hc, out _);
        uint[] datac = pixc.Data;
        wplc = pixc.Wpl;

        /* Check limits for src1, in case clipping was not done */
        for (i = 0; i < hc; i++) {
            /*
             * The basic logic for this blending is:
             *      p -->  (1 - f) * p + f * c
             * for each color channel.  c is a color component of the blender.
             * All values are normalized to [0...1].
             */
            if (i + y < 0  || i + y >= h) continue;
            int linec = i * wplc;
            int lined = (i + y) * wpld;
            for (j = 0; j < wc; j++) {
                if (j + x < 0  || j + x >= w) continue;
                cval32 = datac[linec + j];
                if (transparent == 0 ||
                    ((cval32 & 0xffffff00) != (transpix & 0xffffff00))) {
                    val32 = datad[lined + j + x];
                    ExtractRGBValues(cval32, out rcval, out gcval, out bcval);
                    ExtractRGBValues(val32, out rval, out gval, out bval);
                    rval = (int)((1.0 - fract) * rval + fract * rcval);
                    gval = (int)((1.0 - fract) * gval + fract * gcval);
                    bval = (int)((1.0 - fract) * bval + fract * bcval);
                    ComposeRGBPixel(rval, gval, bval, out val32);
                    datad[lined + j + x] = val32;
                }
            }
        }

        return pixd;
    }

    // leptonica: pixBlendColorByChannel (blend.c)
    /// <summary>Blends a color blender into a pix, with a separate fraction for each channel.</summary>
    /// <param name="pixd">[optional] either equal to pixs1 for in-place, or null</param>
    /// <param name="pixs1">blendee; depth &gt; 1</param>
    /// <param name="pixs2">blender, any depth; typically, the area of pixs2 is smaller than pixs1</param>
    /// <param name="x">origin [UL corner] of pixs2 relative to the origin of pixs1</param>
    /// <param name="y">origin [UL corner] of pixs2 relative to the origin of pixs1</param>
    /// <param name="rfract">blending fraction in red channel</param>
    /// <param name="gfract">blending fraction in green channel</param>
    /// <param name="bfract">blending fraction in blue channel</param>
    /// <param name="transparent">1 to use transparency; 0 otherwise</param>
    /// <param name="transpix">pixel color in pixs2 that is to be transparent</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This generalizes pixBlendColor() in two ways:
    ///     (a) The mixing fraction is specified per channel.
    ///     (b) The mixing fraction may be &lt; 0 or &gt; 1, in which case,
    ///         the min or max of two images are taken, respectively.
    /// (2) Specifically,
    ///     for p = pixs1[i], c = pixs2[i], f = fract[i], i = 1, 2, 3:
    ///         f &lt; 0.0:          p --> min(p, c)
    ///         0.0 &lt;= f &lt;= 1.0:  p --> (1 - f) * p + f * c
    ///         f &gt; 1.0:          p --> max(a, c)
    ///     Special cases:
    ///         f = 0:   p --> p
    ///         f = 1:   p --> c
    /// (3) See usage notes in pixBlendColor()
    /// (4) pixBlendColor() would be equivalent to
    ///       pixBlendColorChannel(..., fract, fract, fract, ...);
    ///     at a small cost of efficiency.
    /// </remarks>
    public static Pix BlendColorByChannel(Pix? pixd, Pix pixs1, Pix pixs2, int x, int y,
                                          float rfract, float gfract, float bfract,
                                          int transparent, uint transpix)
    {
        const string func = "pixBlendColorByChannel";
        int i, j, wc, hc, w, h, wplc, wpld;
        int rval, gval, bval, rcval, gcval, bcval;
        uint cval32, val32;
        Pix pixc;

        if (pixs1.Depth == 1)
            throw Lept.Arg(func, "pixs1 is 1 bpp");
        if (pixd == pixs1 && pixs1.Depth != 32)
            throw Lept.Arg(func, "inplace; pixs1 not 32 bpp");
        if (pixd != null && (pixd != pixs1))
            throw Lept.Arg(func, "pixd must be NULL or pixs1");

        /* If pixd != NULL, we know that it is equal to pixs1 and
         * that pixs1 is 32 bpp rgb, so that an in-place operation
         * can be done.  Otherwise, pixConvertTo32() will remove a
         * colormap from pixs1 if it exists and unpack to 32 bpp
         * (if necessary) to do the blending on a new 32 bpp Pix. */
        if (pixd == null)
            pixd = pixs1.ConvertTo32();
        pixd.GetDimensions(out w, out h, out _);
        wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        pixc = pixs2.ConvertTo32();
        pixc.GetDimensions(out wc, out hc, out _);
        uint[] datac = pixc.Data;
        wplc = pixc.Wpl;

        /* Check limits for src1, in case clipping was not done */
        for (i = 0; i < hc; i++) {
            if (i + y < 0  || i + y >= h) continue;
            int linec = i * wplc;
            int lined = (i + y) * wpld;
            for (j = 0; j < wc; j++) {
                if (j + x < 0  || j + x >= w) continue;
                cval32 = datac[linec + j];
                if (transparent == 0 ||
                    ((cval32 & 0xffffff00) != (transpix & 0xffffff00))) {
                    val32 = datad[lined + j + x];
                    ExtractRGBValues(cval32, out rcval, out gcval, out bcval);
                    ExtractRGBValues(val32, out rval, out gval, out bval);
                    rval = BlendComponents(rval, rcval, rfract);
                    gval = BlendComponents(gval, gcval, gfract);
                    bval = BlendComponents(bval, bcval, bfract);
                    ComposeRGBPixel(rval, gval, bval, out val32);
                    datad[lined + j + x] = val32;
                }
            }
        }

        return pixd;
    }

    // leptonica: blendComponents (blend.c)
    private static int BlendComponents(int a, int b, float fract)
    {
        if (fract < 0.0)
            return ((a < b) ? a : b);
        if (fract > 1.0)
            return ((a > b) ? a : b);
        return (int)((1.0 - fract) * a + fract * b);
    }

    // leptonica: pixBlendGrayAdapt (blend.c)
    /// <summary>Blends a gray blender into a pix, with the zero blend point adapted to the blendee.</summary>
    /// <param name="pixd">[optional] either equal to pixs1 for in-place, or null</param>
    /// <param name="pixs1">blendee; depth &gt; 1</param>
    /// <param name="pixs2">blender, any depth; typically, the area of pixs2 is smaller than pixs1</param>
    /// <param name="x">origin [UL corner] of pixs2 relative to the origin of pixs1; can be &lt; 0</param>
    /// <param name="y">origin [UL corner] of pixs2 relative to the origin of pixs1; can be &lt; 0</param>
    /// <param name="fract">blending fraction</param>
    /// <param name="shift">&gt;= 0 but &lt;= 128: shift of zero blend value from median source; use -1 for default value</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) For inplace operation (pixs1 not cmapped), call it this way:
    ///       pixBlendGrayAdapt(pixs1, pixs1, pixs2, ...)
    ///     For generating a new pixd:
    ///       pixd = pixBlendGrayAdapt(NULL, pixs1, pixs2, ...)
    /// (2) Clipping of pixs2 to pixs1 is done in the inner pixel loop.
    /// (3) If pixs1 has a colormap, it is removed.
    /// (4) If pixs1 has depth &lt; 8, it is unpacked to generate a 8 bpp pix.
    /// (5) This does a blend with inverse.  Whereas in pixGlendGray(), the
    ///     zero blend point is where the blendee pixel is 128, here
    ///     the zero blend point is found adaptively, with respect to the
    ///     median of the blendee region.  If the median is &lt; 128,
    ///     the zero blend point is found from
    ///         median + shift.
    ///     Otherwise, if the median >= 128, the zero blend point is
    ///         median - shift.
    ///     The purpose of shifting the zero blend point away from the
    ///     median is to prevent a situation in pixBlendGray() where
    ///     the median is 128 and the blender is not visible.
    ///     The default value of shift is 64.
    /// (6) After processing pixs1, it is either 8 bpp or 32 bpp:
    ///     ~ if 8 bpp, the fraction of pixs2 is mixed with pixs1.
    ///     ~ if 32 bpp, each component of pixs1 is mixed with
    ///       the same fraction of pixs2.
    /// (7) The darker the blender, the more it mixes with the blendee.
    ///     A blender value of 0 has maximum mixing; a value of 255
    ///     has no mixing and hence is transparent.
    /// </remarks>
    public static Pix BlendGrayAdapt(Pix? pixd, Pix pixs1, Pix pixs2, int x, int y, float fract, int shift)
    {
        const string func = "pixBlendGrayAdapt";
        int i, j, d, wc, hc, w, h, wplc, wpld, delta;
        int rval, gval, bval, cval, dval, mval, median, pivot;
        uint val32;
        float fmedian, factor;
        Box box, boxt;
        bool overlap;
        Pix pixc, pix1, pix2;

        if (pixs1.Depth == 1)
            throw Lept.Arg(func, "pixs1 is 1 bpp");
        if (pixd == pixs1 && pixs1.Colormap != null)
            throw Lept.Arg(func, "can't do in-place with cmap");
        if (pixd != null && (pixd != pixs1))
            throw Lept.Arg(func, "pixd must be NULL or pixs1");
        if (fract < 0.0 || fract > 1.0) {
            Log.Warning(func, "fract must be in [0.0, 1.0]; setting to 0.5");
            fract = 0.5f;
        }
        if (shift == -1) shift = 64;   /* default value */
        if (shift < 0 || shift > 127) {
            Log.Warning(func, "invalid shift; setting to 64");
            shift = 64;
        }

        /* Test for overlap */
        pixs1.GetDimensions(out w, out h, out _);
        pixs2.GetDimensions(out wc, out hc, out _);
        box = Box.Create(x, y, wc, hc);
        boxt = Box.Create(0, 0, w, h);
        overlap = box.Intersects(boxt);
        if (!overlap)
            throw Lept.Arg(func, "no image overlap");

        /* If pixd != NULL, we know that it is equal to pixs1 and
         * that pixs1 does not have a colormap, so that an in-place operation
         * can be done.  Otherwise, remove colormap from pixs1 if
         * it exists and unpack to at least 8 bpp if necessary,
         * to do the blending on a new pix. */
        if (pixd == null) {
            pix1 = pixs1.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            if (pix1.Depth < 8)
                pix2 = pix1.ConvertTo8(false);
            else
                pix2 = Clone(pix1);
            pixd = Copy(null, pix2);
        }

        /* Get the median value in the region of blending */
        pix1 = pixd.ClipRectangle(box, out _) ?? throw Lept.Fail(func, "pix1 not made");
        pix2 = pix1.ConvertTo8(false);
        pix2.GetRankValueMasked(null, 0, 0, 1, 0.5f, out fmedian, out _);
        median = (int)(fmedian + 0.5);
        if (median < 128)
            pivot = median + shift;
        else
            pivot = median - shift;

        /* Process over src2; clip to src1. */
        d = pixd.Depth;
        wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        pixc = pixs2.ConvertTo8(false);
        uint[] datac = pixc.Data;
        wplc = pixc.Wpl;
        for (i = 0; i < hc; i++) {
            if (i + y < 0  || i + y >= h) continue;
            ReadOnlySpan<uint> linec = datac.AsSpan(i * wplc);
            Span<uint> lined = datad.AsSpan((i + y) * wpld);
            switch (d)
            {
            case 8:
                /*
                 * For 8 bpp, the dest pix is shifted by an amount
                 * proportional to the distance from the pivot value,
                 * and to the darkness of src2.  In no situation will it
                 * pass the pivot value in intensity.
                 * The basic logic is:
                 *     d  -->  d + f * (np - d) * (1 - c)
                 * where np, d and c are normalized pixel values for
                 * the pivot, src1 and src2, respectively, with normalization
                 * to 255.
                 */
                for (j = 0; j < wc; j++) {
                    if (j + x < 0  || j + x >= w) continue;
                    dval = GetDataByte(lined, j + x);
                    cval = GetDataByte(linec, j);
                    delta = (pivot - dval) * (255 - cval) / 256;
                    dval += (int)(fract * delta + 0.5);
                    SetDataByte(lined, j + x, dval);
                }
                break;
            case 32:
                /*
                 * For 32 bpp, the dest pix is shifted by an amount
                 * proportional to the max component distance from the
                 * pivot value, and to the darkness of src2.  Each component
                 * is shifted by the same fraction, either up or down,
                 * depending on the shift direction (which is toward the
                 * pivot).   The basic logic for the red component is:
                 *     r  -->  r + f * (np - m) * (1 - c) * (r / m)
                 * where np, r, m and c are normalized pixel values for
                 * the pivot, the r component of src1, the max component
                 * of src1, and src2, respectively, again with normalization
                 * to 255.  Likewise for the green and blue components.
                 */
                for (j = 0; j < wc; j++) {
                    if (j + x < 0  || j + x >= w) continue;
                    cval = GetDataByte(linec, j);
                    val32 = lined[j + x];
                    ExtractRGBValues(val32, out rval, out gval, out bval);
                    mval = Math.Max(rval, gval);
                    mval = Math.Max(mval, bval);
                    mval = Math.Max(mval, 1);
                    delta = (pivot - mval) * (255 - cval) / 256;
                    factor = fract * delta / mval;
                    rval += (int)(factor * rval + 0.5);
                    gval += (int)(factor * gval + 0.5);
                    bval += (int)(factor * bval + 0.5);
                    ComposeRGBPixel(rval, gval, bval, out val32);
                    lined[j + x] = val32;
                }
                break;
            default:
                break;   /* shouldn't happen */
            }
        }

        return pixd;
    }

    // leptonica: pixFadeWithGray (blend.c)
    /// <summary>Fades this pix toward white or black, by the values of a gray blender.</summary>
    /// <param name="pixb">8 bpp blender</param>
    /// <param name="factor">multiplicative factor to apply to blender value</param>
    /// <param name="type">L_BLEND_TO_WHITE, L_BLEND_TO_BLACK</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This function combines two pix aligned to the UL corner; they
    ///     need not be the same size.
    /// (2) Each pixel in pixb is multiplied by 'factor' divided by 255, and
    ///     clipped to the range [0 ... 1].  This gives the fade fraction
    ///     to be applied to pixs.  Fade either to white (L_BLEND_TO_WHITE)
    ///     or to black (L_BLEND_TO_BLACK).
    /// </remarks>
    public Pix FadeWithGray(Pix pixb, float factor, int type)
    {
        const string func = "pixFadeWithGray";
        int i, j, w, h, d, wb, hb, db, wd, hd, wplb, wpld;
        int valb, vald, nvald, rval, gval, bval, nrval, ngval, nbval;
        float nfactor, fract;
        uint val32, nval32;
        Pix pixd;
        Pix pixs = this;

        if (pixs.Depth == 1)
            throw Lept.Arg(func, "pixs is 1 bpp");
        pixb.GetDimensions(out wb, out hb, out db);
        if (db != 8)
            throw Lept.Arg(func, "pixb not 8 bpp");
        if (factor < 0.0 || factor > 255.0)
            throw Lept.Arg(func, "factor not in [0.0...255.0]");
        if (type != L.L_BLEND_TO_WHITE && type != L.L_BLEND_TO_BLACK)
            throw Lept.Arg(func, "invalid fade type");

        /* Remove colormap if it exists; otherwise copy */
        pixd = pixs.RemoveColormapGeneral(L.REMOVE_CMAP_BASED_ON_SRC, L.L_COPY);
        pixd.GetDimensions(out wd, out hd, out d);
        w = Math.Min(wb, wd);
        h = Math.Min(hb, hd);
        uint[] datad = pixd.Data;
        wpld = pixd.Wpl;
        uint[] datab = pixb.Data;
        wplb = pixb.Wpl;

        /* The basic logic for this blending is, for each component p of pixs:
         *   fade-to-white:   p -->  p + (f * c) * (1 - p)
         *   fade-to-black:   p -->  p - (f * c) * p
         * with c being the 8 bpp blender pixel of pixb, and with both
         * p and c normalized to [0...1]. */
        nfactor = (float)(factor / 255.0);
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lineb = datab.AsSpan(i * wplb);
            Span<uint> lined = datad.AsSpan(i * wpld);
            for (j = 0; j < w; j++) {
                valb = GetDataByte(lineb, j);
                fract = nfactor * (float)valb;
                fract = (fract < 1.0) ? fract : 1.0f;
                if (d == 8) {
                    vald = GetDataByte(lined, j);
                    if (type == L.L_BLEND_TO_WHITE)
                        nvald = vald + (int)(fract * (255.0 - (float)vald));
                    else  /* L_BLEND_TO_BLACK */
                        nvald = vald - (int)(fract * (float)vald);
                    SetDataByte(lined, j, nvald);
                } else {  /* d == 32 */
                    val32 = lined[j];
                    ExtractRGBValues(val32, out rval, out gval, out bval);
                    if (type == L.L_BLEND_TO_WHITE) {
                        nrval = rval + (int)(fract * (255.0 - (float)rval));
                        ngval = gval + (int)(fract * (255.0 - (float)gval));
                        nbval = bval + (int)(fract * (255.0 - (float)bval));
                    } else {
                        nrval = rval - (int)(fract * (float)rval);
                        ngval = gval - (int)(fract * (float)gval);
                        nbval = bval - (int)(fract * (float)bval);
                    }
                    ComposeRGBPixel(nrval, ngval, nbval, out nval32);
                    lined[j] = nval32;
                }
            }
        }

        return pixd;
    }

    // leptonica: pixBlendHardLight (blend.c)
    /// <summary>Hard light blending of a gray or color blender into a pix.</summary>
    /// <param name="pixd">either null or equal to pixs1 for in-place</param>
    /// <param name="pixs1">blendee; depth &gt; 1, may be cmapped</param>
    /// <param name="pixs2">blender, 8 or 32 bpp; may be colormapped; typ. smaller in size than pixs1</param>
    /// <param name="x">origin [UL corner] of pixs2 relative to the origin of pixs1</param>
    /// <param name="y">origin [UL corner] of pixs2 relative to the origin of pixs1</param>
    /// <param name="fract">blending fraction, or 'opacity factor'</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) pixs2 must be 8 or 32 bpp; either may have a colormap.
    /// (2) Clipping of pixs2 to pixs1 is done in the inner pixel loop.
    /// (3) Only call in-place if pixs1 is not colormapped.
    /// (4) If pixs1 has a colormap, it is removed to generate either an
    ///     8 or 32 bpp pix, depending on the colormap.
    /// (5) For inplace operation, call it this way:
    ///       pixBlendHardLight(pixs1, pixs1, pixs2, ...)
    /// (6) For generating a new pixd:
    ///       pixd = pixBlendHardLight(NULL, pixs1, pixs2, ...)
    /// (7) This is a generalization of the usual hard light blending,
    ///     where fract == 1.0.
    /// (8) "Overlay" blending is the same as hard light blending, with
    ///     fract == 1.0, except that the components are switched
    ///     in the test.  (Note that the result is symmetric in the
    ///     two components.)
    /// (9) See, e.g.:
    ///      http://www.pegtop.net/delphi/articles/blendmodes/hardlight.htm
    ///      http://www.digitalartform.com/imageArithmetic.htm
    /// (10) This function was built by Paco Galanes.
    /// </remarks>
    public static Pix BlendHardLight(Pix? pixd, Pix pixs1, Pix pixs2, int x, int y, float fract)
    {
        const string func = "pixBlendHardLight";
        int i, j, w, h, d, wc, hc, dc, wplc, wpld;
        int cval, dval, rcval, gcval, bcval, rdval, gdval, bdval;
        uint cval32, dval32;
        Pix pixc, pixt;

        pixs1.GetDimensions(out w, out h, out d);
        pixs2.GetDimensions(out wc, out hc, out dc);
        if (d == 1)
            throw Lept.Arg(func, "pixs1 is 1 bpp");
        if (dc != 8 && dc != 32)
            throw Lept.Arg(func, "pixs2 not 8 or 32 bpp");
        if (pixd != null && (pixd != pixs1))
            throw Lept.Arg(func, "inplace and pixd != pixs1");
        if (pixd == pixs1 && pixs1.Colormap != null)
            throw Lept.Arg(func, "inplace and pixs1 cmapped");
        if (pixd != null && d != 8 && d != 32)
            throw Lept.Arg(func, "inplace and not 8 or 32 bpp");

        if (fract < 0.0 || fract > 1.0) {
            Log.Warning(func, "fract must be in [0.0, 1.0]; setting to 0.5");
            fract = 0.5f;
        }

        /* If pixs2 has a colormap, remove it */
        pixc = pixs2.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);  /* clone ok */
        dc = pixc.Depth;

        /* There are 4 cases:
         *    * pixs1 has or doesn't have a colormap
         *    * pixc is either 8 or 32 bpp
         * In all situations, if pixs has a colormap it must be removed,
         * and pixd must have a depth that is equal to or greater than pixc. */
        if (dc == 32) {
            if (pixs1.Colormap != null) {  /* pixd == NULL */
                pixd = pixs1.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
            } else {
                if (pixd == null) {
                    pixd = pixs1.ConvertTo32();
                } else {
                    pixt = pixs1.ConvertTo32();
                    Copy(pixd, pixt);
                }
            }
            d = 32;
        } else {  /* dc == 8 */
            if (pixs1.Colormap != null)   /* pixd == NULL */
                pixd = pixs1.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            else
                pixd = Copy(pixd, pixs1);
            d = pixd.Depth;
        }

        if (!(d == 8 && dc == 8) &&   /* 3 cases only */
            !(d == 32 && dc == 8) &&
            !(d == 32 && dc == 32))
            throw Lept.Fail(func, "bad! -- invalid depth combo!");

        wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        uint[] datac = pixc.Data;
        wplc = pixc.Wpl;
        for (i = 0; i < hc; i++) {
            if (i + y < 0  || i + y >= h) continue;
            ReadOnlySpan<uint> linec = datac.AsSpan(i * wplc);
            Span<uint> lined = datad.AsSpan((i + y) * wpld);
            for (j = 0; j < wc; j++) {
                if (j + x < 0  || j + x >= w) continue;
                if (d == 8 && dc == 8) {
                    dval = GetDataByte(lined, x + j);
                    cval = GetDataByte(linec, j);
                    dval = BlendHardLightComponents(dval, cval, fract);
                    SetDataByte(lined, x + j, dval);
                } else if (d == 32 && dc == 8) {
                    dval32 = lined[x + j];
                    ExtractRGBValues(dval32, out rdval, out gdval, out bdval);
                    cval = GetDataByte(linec, j);
                    rdval = BlendHardLightComponents(rdval, cval, fract);
                    gdval = BlendHardLightComponents(gdval, cval, fract);
                    bdval = BlendHardLightComponents(bdval, cval, fract);
                    ComposeRGBPixel(rdval, gdval, bdval, out dval32);
                    lined[x + j] = dval32;
                } else if (d == 32 && dc == 32) {
                    dval32 = lined[x + j];
                    ExtractRGBValues(dval32, out rdval, out gdval, out bdval);
                    cval32 = linec[j];
                    ExtractRGBValues(cval32, out rcval, out gcval, out bcval);
                    rdval = BlendHardLightComponents(rdval, rcval, fract);
                    gdval = BlendHardLightComponents(gdval, gcval, fract);
                    bdval = BlendHardLightComponents(bdval, bcval, fract);
                    ComposeRGBPixel(rdval, gdval, bdval, out dval32);
                    lined[x + j] = dval32;
                }
            }
        }

        return pixd;
    }

    /* blendHardLightComponents (blend.c) */
    /// <remarks>
    /// The basic logic for this blending is:
    ///   b &lt; 0.5:
    ///       a --> 2 * a * (0.5 - f * (0.5 - b))
    ///   b >= 0.5:
    ///       a --> 1 - 2 * (1 - a) * (1 - (0.5 - f * (0.5 - b)))
    ///
    /// In the limit that f == 1 (standard hardlight blending):
    ///   b &lt; 0.5:   a --> 2 * a * b
    ///                  or
    ///              a --> a - a * (1 - 2 * b)
    ///   b >= 0.5:  a --> 1 - 2 * (1 - a) * (1 - b)
    ///                  or
    ///              a --> a + (1 - a) * (2 * b - 1)
    ///
    /// You can see that for standard hardlight blending:
    ///   b &lt; 0.5:   a is pushed linearly with b down to 0
    ///   b >= 0.5:  a is pushed linearly with b up to 1
    /// a is unchanged if b = 0.5
    ///
    /// Our opacity factor f reduces the deviation of b from 0.5:
    ///   f == 0:  b -->  0.5, so no blending occurs
    ///   f == 1:  b -->  b, so we get full conventional blending
    ///
    /// There is a variant of hardlight blending called "softlight" blending:
    /// (e.g., http://jswidget.com/blog/tag/hard-light/)
    ///   b &lt; 0.5:
    ///       a --> a - a * (0.5 - b) * (1 - Abs(2 * a - 1))
    ///   b >= 0.5:
    ///       a --> a + (1 - a) * (b - 0.5) * (1 - Abs(2 * a - 1))
    /// which limits the amount that 'a' can be moved to a maximum of
    /// halfway toward 0 or 1, and further reduces it as 'a' moves
    /// away from 0.5.
    /// As you can see, there are a nearly infinite number of different
    /// blending formulas that can be conjured up.
    /// </remarks>
    private static int BlendHardLightComponents(int a, int b, float fract)
    {
        if (b < 0x80) {
            b = 0x80 - (int)(fract * (0x80 - b));
            return (a * b) >> 7;
        } else {
            b = 0x80 + (int)(fract * (b - 0x80));
            return 0xff - (((0xff - b) * (0xff - a)) >> 7);
        }
    }

    /*-------------------------------------------------------------*
     *               Blending two colormapped images               *
     *-------------------------------------------------------------*/
    // leptonica: pixBlendCmap (blend.c)
    /// <summary>Replaces the pixels of a given colormap index by those of a colormapped blender.</summary>
    /// <param name="pixb">colormapped blender</param>
    /// <param name="x">UL corner of blender relative to pixs</param>
    /// <param name="y">UL corner of blender relative to pixs</param>
    /// <param name="sindex">colormap index of pixels in pixs to be changed</param>
    /// <remarks>
    /// (1) This function combines two colormaps, and replaces the pixels
    ///     in pixs that have a specified color value with those in pixb.
    /// (2) sindex must be in the existing colormap; otherwise an
    ///     error is returned.  In use, sindex will typically be the index
    ///     for white (255, 255, 255).
    /// (3) Blender colors that already exist in the colormap are used;
    ///     others are added.  If any blender colors cannot be
    ///     stored in the colormap, an error is returned.
    /// (4) In the implementation, a mapping is generated from each
    ///     original blender colormap index to the corresponding index
    ///     in the expanded colormap for pixs.  Then for each pixel in
    ///     pixs with value sindex, and which is covered by a blender pixel,
    ///     the new index corresponding to the blender pixel is substituted
    ///     for sindex.
    /// </remarks>
    public void BlendCmap(Pix pixb, int x, int y, int sindex)
    {
        const string func = "pixBlendCmap";
        int rval, gval, bval;
        int i, j, w, h, d, ncb, wb, hb, wpls;
        int index, val, nadded;
        int[] lut = new int[256];
        uint pval;
        PixColormap? cmaps, cmapb, cmapsc;
        Pix pixs = this;

        if ((cmaps = pixs.Colormap) == null)
            throw Lept.Arg(func, "no colormap in pixs");
        if ((cmapb = pixb.Colormap) == null)
            throw Lept.Arg(func, "no colormap in pixb");
        ncb = cmapb.Count;

        pixs.GetDimensions(out w, out h, out d);
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "depth not in {2,4,8}");

        /* Make a copy of cmaps; we'll add to this if necessary
         * and substitute at the end if we found there was enough room
         * to hold all the new colors. */
        cmapsc = cmaps.Copy();

        /* Add new colors if necessary; get mapping array between
         * cmaps and cmapb. */
        for (i = 0, nadded = 0; i < ncb; i++) {
            cmapb.GetColor(i, out rval, out gval, out bval);
            if (!cmapsc.GetIndex(rval, gval, bval, out index)) {  /* not found */
                try {
                    cmapsc.AddColor(rval, gval, bval);
                } catch (LeptonicaException) {
                    throw Lept.Fail(func, "not enough room in cmaps");
                }
                lut[i] = cmapsc.Count - 1;
                nadded++;
            } else {
                lut[i] = index;
            }
        }

        /* Replace cmaps if colors have been added. */
        if (nadded != 0)
            pixs.SetColormap(cmapsc);

        /* Replace each pixel value sindex by mapped colormap index when
         * a blender pixel in pixbc overlays it. */
        uint[] datas = pixs.Data;
        wpls = pixs.Wpl;
        pixb.GetDimensions(out wb, out hb, out _);
        for (i = 0; i < hb; i++) {
            if (i + y < 0  || i + y >= h) continue;
            Span<uint> lines = datas.AsSpan((y + i) * wpls);
            for (j = 0; j < wb; j++) {
                if (j + x < 0  || j + x >= w) continue;
                switch (d) {
                case 2:
                    val = GetDataDibit(lines, x + j);
                    if (val == sindex) {
                        pixb.GetPixel(j, i, out pval);
                        SetDataDibit(lines, x + j, lut[pval]);
                    }
                    break;
                case 4:
                    val = GetDataQbit(lines, x + j);
                    if (val == sindex) {
                        pixb.GetPixel(j, i, out pval);
                        SetDataQbit(lines, x + j, lut[pval]);
                    }
                    break;
                case 8:
                    val = GetDataByte(lines, x + j);
                    if (val == sindex) {
                        pixb.GetPixel(j, i, out pval);
                        SetDataByte(lines, x + j, lut[pval]);
                    }
                    break;
                default:
                    throw Lept.Arg(func, "depth not in {2,4,8}");
                }
            }
        }
    }

    /*---------------------------------------------------------------------*
     *                  Blending two images using a third                  *
     *---------------------------------------------------------------------*/
    // leptonica: pixBlendWithGrayMask (blend.c)
    /// <summary>Blends pixs2 into this pix (pixs1) through the 8 bpp gray mask pixg.</summary>
    /// <param name="pixs2">8 bpp gray, rgb, rgba or colormapped</param>
    /// <param name="pixg">[optional] 8 bpp gray, for transparency of pixs2; can be null</param>
    /// <param name="x">UL corner of pixs2 and pixg with respect to pixs1</param>
    /// <param name="y">UL corner of pixs2 and pixg with respect to pixs1</param>
    /// <returns>pixd blended image</returns>
    /// <remarks>
    /// (1) The result is 8 bpp grayscale if both pixs1 and pixs2 are
    ///     8 bpp gray.  Otherwise, the result is 32 bpp rgb.
    /// (2) pixg is an 8 bpp transparency image, where 0 is transparent
    ///     and 255 is opaque.  It determines the transparency of pixs2
    ///     when applied over pixs1.  It can be null if pixs2 is rgba,
    ///     in which case we use the alpha component of pixs2.
    /// (3) If pixg exists, it need not be the same size as pixs2.
    ///     However, we assume their UL corners are aligned with each other,
    ///     and placed at the location (x, y) in pixs1.
    /// (4) The pixels in pixd are a combination of those in pixs1
    ///     and pixs2, where the amount from pixs2 is proportional to
    ///     the value of the pixel (p) in pixg, and the amount from pixs1
    ///     is proportional to (255 - p).  Thus pixg is a transparency
    ///     image (usually called an alpha blender) where each pixel
    ///     can be associated with a pixel in pixs2, and determines
    ///     the amount of the pixs2 pixel in the final result.
    ///     For example, if pixg is all 0, pixs2 is transparent and
    ///     the result in pixd is simply pixs1.
    /// (5) A typical use is for the pixs2/pixg combination to be
    ///     a small watermark that is applied to pixs1.
    /// </remarks>
    public Pix BlendWithGrayMask(Pix pixs2, Pix? pixg, int x, int y)
    {
        const string func = "pixBlendWithGrayMask";
        int w1, h1, d1, w2, h2, d2, spp, wg, hg, wmin, hmin, wpld, wpls, wplg;
        int i, j, val, dval, sval;
        int drval, dgval, dbval, srval, sgval, sbval;
        uint dval32, sval32;
        float fract;
        Pix pixr1, pixr2, pix1, pix2, pixg2, pixd;
        Pix pixs1 = this;

        pixs1.GetDimensions(out w1, out h1, out d1);
        pixs2.GetDimensions(out w2, out h2, out d2);
        if (d1 == 1 || d2 == 1)
            throw Lept.Arg(func, "pixs1 or pixs2 is 1 bpp");
        if (pixg != null) {
            if (pixg.Depth != 8)
                throw Lept.Arg(func, "pixg not 8 bpp");
            pixg.GetDimensions(out wg, out hg, out _);
            wmin = Math.Min(w2, wg);
            hmin = Math.Min(h2, hg);
            pixg2 = Clone(pixg);
        } else {  /* use the alpha component of pixs2 */
            spp = pixs2.Spp;
            if (d2 != 32 || spp != 4)
                throw Lept.Arg(func, "no alpha; pixs2 not rgba");
            wmin = w2;
            hmin = h2;
            pixg2 = pixs2.GetRGBComponent(L.L_ALPHA_CHANNEL);
        }

        /* Remove colormaps if they exist; clones are OK */
        pixr1 = pixs1.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        pixr2 = pixs2.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);

        /* Regularize to the same depth if necessary */
        d1 = pixr1.Depth;
        d2 = pixr2.Depth;
        if (d1 == 32) {  /* convert d2 to rgb if necessary */
            pix1 = Clone(pixr1);
            if (d2 != 32)
                pix2 = pixr2.ConvertTo32();
            else
                pix2 = Clone(pixr2);
        } else if (d2 == 32) {   /* and d1 != 32; convert to 32 */
            pix2 = Clone(pixr2);
            pix1 = pixr1.ConvertTo32();
        } else {  /* both are 8 bpp or less */
            pix1 = pixr1.ConvertTo8(false);
            pix2 = pixr2.ConvertTo8(false);
        }

        /* Output a copy of pix1 to avoid side-effecting input pixs1 */
        pixd = Copy(null, pix1);

        /* Sanity check: both either 8 or 32 bpp */
        d1 = pixd.Depth;
        d2 = pix2.Depth;
        if (d1 != d2 || (d1 != 8 && d1 != 32))
            throw Lept.Fail(func, "depths not regularized! bad!");

        /* Blend pix2 onto pixd, using pixg2.
         * Let the normalized pixel value of pixg2 be f = pixval / 255,
         * and the pixel values of pixd and pix2 be p1 and p2, rsp.
         * Then the blended value is:
         *      p = (1.0 - f) * p1 + f * p2
         * Blending is done component-wise if rgb.
         * Scan over pix2 and pixg2, clipping to pixd where necessary.  */
        uint[] datad = pixd.Data;
        uint[] datas = pix2.Data;
        uint[] datag = pixg2.Data;
        wpld = pixd.Wpl;
        wpls = pix2.Wpl;
        wplg = pixg2.Wpl;
        for (i = 0; i < hmin; i++) {
            if (i + y < 0  || i + y >= h1) continue;
            Span<uint> lined = datad.AsSpan((i + y) * wpld);
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls);
            ReadOnlySpan<uint> lineg = datag.AsSpan(i * wplg);
            for (j = 0; j < wmin; j++) {
                if (j + x < 0  || j + x >= w1) continue;
                val = GetDataByte(lineg, j);
                if (val == 0) continue;  /* pix2 is transparent */
                fract = (float)((float)val / 255.0);
                if (d1 == 8) {
                    dval = GetDataByte(lined, j + x);
                    sval = GetDataByte(lines, j);
                    dval = (int)((1.0 - fract) * dval + fract * sval);
                    SetDataByte(lined, j + x, dval);
                } else {  /* 32 */
                    dval32 = lined[j + x];
                    sval32 = lines[j];
                    ExtractRGBValues(dval32, out drval, out dgval, out dbval);
                    ExtractRGBValues(sval32, out srval, out sgval, out sbval);
                    drval = (int)((1.0 - fract) * drval + fract * srval);
                    dgval = (int)((1.0 - fract) * dgval + fract * sgval);
                    dbval = (int)((1.0 - fract) * dbval + fract * sbval);
                    ComposeRGBPixel(drval, dgval, dbval, out dval32);
                    lined[j + x] = dval32;
                }
            }
        }

        return pixd;
    }

    /*---------------------------------------------------------------------*
     *                Blending background to a specific color              *
     *---------------------------------------------------------------------*/
    // leptonica: pixBlendBackgroundToColor (blend.c)
    /// <summary>Replaces light background pixels by a color, by alpha blending.</summary>
    /// <param name="pixd">can be null or pixs</param>
    /// <param name="pixs">32 bpp rgb</param>
    /// <param name="box">region for blending; can be null</param>
    /// <param name="color">32 bit color in 0xrrggbb00 format</param>
    /// <param name="gamma">args for grayscale TRC mapping</param>
    /// <param name="minval">args for grayscale TRC mapping</param>
    /// <param name="maxval">args for grayscale TRC mapping</param>
    /// <returns>pixd always</returns>
    /// <remarks>
    /// (1) This in effect replaces light background pixels in pixs
    ///     by the input color.  It does it by alpha blending so that
    ///     there are no visible artifacts from hard cutoffs.
    /// (2) If pixd == pixs, this is done in-place.
    /// (3) If box == NULL, this is performed on all of pixs.
    /// (4) The alpha component for blending is derived from pixs,
    ///     by converting to grayscale and enhancing with a TRC.
    /// (5) The last three arguments specify the TRC operation.
    ///     Suggested values are: %gamma = 0.3, %minval = 50, %maxval = 200.
    ///     To skip the TRC, use %gamma == 1, %minval = 0, %maxval = 255.
    ///     See pixGammaTRC() for details.
    /// </remarks>
    public static Pix BlendBackgroundToColor(Pix? pixd, Pix pixs, Box? box, uint color,
                                             float gamma, int minval, int maxval)
    {
        const string func = "pixBlendBackgroundToColor";
        int x = 0, y = 0, w, h;
        Box? boxt;
        Pix pixc, pixr, pixg;

        if (pixs.Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (pixd != null && (pixd != pixs))
            throw Lept.Arg(func, "pixd neither null nor pixs");

        /* Extract the (optionally cropped) region, pixr, and generate
         * an identically sized pixc with the uniform color. */
        if (pixd == null)
            pixd = Copy(null, pixs);
        if (box != null) {
            pixr = pixd.ClipRectangle(box, out boxt) ?? throw Lept.Arg(func, "box doesn't overlap pixs");
            boxt!.GetGeometry(out x, out y, out w, out h);
            pixc = Create(w, h, 32);
        } else {
            pixc = CreateTemplate(pixs);
            pixr = Clone(pixd);
        }
        pixc.SetAllArbitrary(color);

        /* Set up the alpha channel */
        pixg = pixr.ConvertTo8(false);
        GammaTRC(pixg, pixg, gamma, minval, maxval);
        pixc.SetRGBComponent(pixg, L.L_ALPHA_CHANNEL);

        /* Blend and replace in pixd */
        Pix? pixtr = pixr.BlendWithGrayMask(pixc, null, 0, 0);
        if (box != null) {
            pixd.Rasterop(x, y, pixr.Width, pixr.Height, L.PIX_SRC, pixtr, 0, 0);
        } else {
            pixd.TransferAllData(ref pixtr, false, false);
        }

        return pixd;
    }

    /*---------------------------------------------------------------------*
     *                     Multiplying by a specific color                 *
     *---------------------------------------------------------------------*/
    // leptonica: pixMultiplyByColor (blend.c)
    /// <summary>Multiplies each component of the pixels in a region by a color.</summary>
    /// <param name="pixd">can be null or pixs</param>
    /// <param name="pixs">32 bpp rgb</param>
    /// <param name="box">region for filtering; can be null</param>
    /// <param name="color">32 bit color in 0xrrggbb00 format</param>
    /// <returns>pixd always</returns>
    /// <remarks>
    /// (1) This filters all pixels in the specified region by
    ///     multiplying each component by the input color.
    ///     This leaves black invariant and transforms white to the
    ///     input color.
    /// (2) If pixd == pixs, this is done in-place.
    /// (3) If box == NULL, this is performed on all of pixs.
    /// </remarks>
    public static Pix MultiplyByColor(Pix? pixd, Pix pixs, Box? box, uint color)
    {
        const string func = "pixMultiplyByColor";
        int i, j, bx = 0, by = 0, w, h, wpl;
        int red, green, blue, rval, gval, bval, nrval, ngval, nbval;
        float frval, fgval, fbval;
        Pix? pixt;

        if (pixs.Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (pixd != null && (pixd != pixs))
            throw Lept.Arg(func, "pixd neither null nor pixs");

        if (pixd == null)
            pixd = Copy(null, pixs);
        if (box != null) {
            box.GetGeometry(out bx, out by, out _, out _);
            pixt = pixd.ClipRectangle(box, out _);
            if (pixt == null)  /* C: nothing is done and the rasterop fails */
                return pixd;
        } else {
            pixt = Clone(pixd);
        }

        /* Multiply each pixel in pixt by the color */
        ExtractRGBValues(color, out red, out green, out blue);
        frval = (float)((1.0 / 255.0) * red);
        fgval = (float)((1.0 / 255.0) * green);
        fbval = (float)((1.0 / 255.0) * blue);
        uint[] data = pixt.Data;
        wpl = pixt.Wpl;
        pixt.GetDimensions(out w, out h, out _);
        for (i = 0; i < h; i++) {
            int line = i * wpl;
            for (j = 0; j < w; j++) {
                ExtractRGBValues(data[line + j], out rval, out gval, out bval);
                nrval = (int)(frval * rval + 0.5);
                ngval = (int)(fgval * gval + 0.5);
                nbval = (int)(fbval * bval + 0.5);
                ComposeRGBPixel(nrval, ngval, nbval, out data[line + j]);
            }
        }

        /* Replace */
        if (box != null)
            pixd.Rasterop(bx, by, w, h, L.PIX_SRC, pixt, 0, 0);
        return pixd;
    }

    /*---------------------------------------------------------------------*
     *       Rendering with alpha blending over a uniform background       *
     *---------------------------------------------------------------------*/
    // leptonica: pixAlphaBlendUniform (blend.c)
    /// <summary>Renders an rgba image over a uniform background color.</summary>
    /// <param name="color">32 bit color in 0xrrggbb00 format</param>
    /// <returns>pixd 32 bpp rgb: pixs blended over uniform color %color,
    /// a clone of pixs if no alpha</returns>
    /// <remarks>
    /// (1) This is a convenience function that renders 32 bpp RGBA images
    ///     (with an alpha channel) over a uniform background of
    ///     value %color.  To render over a white background,
    ///     use %color = 0xffffff00.  The result is an RGB image.
    /// (2) If pixs does not have an alpha channel, it returns a clone
    ///     of pixs.
    /// </remarks>
    public Pix AlphaBlendUniform(uint color)
    {
        const string func = "pixAlphaBlendUniform";
        Pix pixt, pixd;
        Pix pixs = this;

        if (pixs.Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (pixs.Spp != 4) {
            Log.Warning(func, "no alpha channel; returning clone");
            return Clone(pixs);
        }

        pixt = CreateTemplate(pixs);
        pixt.SetAllArbitrary(color);
        pixt.Spp = 3;  /* not required */
        pixd = pixt.BlendWithGrayMask(pixs, null, 0, 0);

        return pixd;
    }

    /*---------------------------------------------------------------------*
     *                   Adding an alpha layer for blending                *
     *---------------------------------------------------------------------*/
    // leptonica: pixAddAlphaToBlend (blend.c)
    /// <summary>Adds an alpha layer for blending, with white transparent and black opaque.</summary>
    /// <param name="fract">fade fraction in the alpha component</param>
    /// <param name="invert">1 to photometrically invert pixs</param>
    /// <returns>pixd 32 bpp with alpha</returns>
    /// <remarks>
    /// (1) This is a simple alpha layer generator, where typically white has
    ///     maximum transparency and black has minimum.
    /// (2) If %invert == 1, generate the same alpha layer but invert
    ///     the input image photometrically.  This is useful for blending
    ///     over dark images, where you want dark regions in pixs, such
    ///     as text, to be lighter in the blended image.
    /// (3) The fade %fract gives the minimum transparency (i.e.,
    ///     maximum opacity).  A small fraction is useful for adding
    ///     a watermark to an image.
    /// (4) If pixs has a colormap, it is removed to rgb.
    /// (5) If pixs already has an alpha layer, it is overwritten.
    /// </remarks>
    public Pix AddAlphaToBlend(float fract, int invert)
    {
        Pix pixd, pix1, pix2;
        Pix pixs = this;

        if (fract < 0.0 || fract > 1.0)
            throw Lept.Arg("pixAddAlphaToBlend", "invalid fract");

        /* Convert to 32 bpp */
        if (pixs.Colormap != null)
            pix1 = pixs.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        else
            pix1 = Clone(pixs);
        pixd = pix1.ConvertTo32();  /* new */

        /* Use an inverted image if this will be blended with a dark image */
        if (invert != 0) Invert(pixd, pixd);

        /* Generate alpha layer */
        pix2 = pix1.ConvertTo8(false);  /* new */
        Invert(pix2, pix2);
        pix2.MultConstantGray(fract);
        pixd.SetRGBComponent(pix2, L.L_ALPHA_CHANNEL);

        return pixd;
    }

    /*---------------------------------------------------------------------*
     *    Setting a transparent alpha component over a white background    *
     *---------------------------------------------------------------------*/
    // leptonica: pixSetAlphaOverWhite (blend.c)
    /// <summary>Sets the alpha component transparent over white pixels, quickly grading to opaque.</summary>
    /// <returns>pixd new pix with meaningful alpha component</returns>
    /// <remarks>
    /// (1) The generated alpha component is transparent over white
    ///     (background) pixels in pixs, and quickly grades to opaque
    ///     away from the transparent parts.  This is a cheap and
    ///     dirty alpha generator.  The 2 pixel gradation is useful
    ///     to blur the boundary between the transparent region
    ///     (that will render entirely from a backing image) and
    ///     the remainder which renders from pixs.
    /// (2) All alpha component bits in pixs are overwritten.
    /// </remarks>
    public Pix SetAlphaOverWhite()
    {
        Pix pixd, pix1, pix2, pix3, pix4;
        Pix pixs = this;

        if (!(pixs.Depth == 32 || pixs.Colormap != null))
            throw Lept.Arg("pixSetAlphaOverWhite", "pixs not 32 bpp or cmapped");

        /* Remove colormap if it exists; otherwise copy */
        pixd = pixs.RemoveColormapGeneral(L.REMOVE_CMAP_TO_FULL_COLOR, L.L_COPY);

        /* Generate a 1 bpp image where a white pixel in pixd is 0.
         * In the comments below, a "white" pixel refers to pixd.
         * pix1 is rgb, pix2 is 8 bpp gray, pix3 is 1 bpp. */
        pix1 = Invert(null, pixd);  /* send white (255) to 0 for each sample */
        pix2 = pix1.ConvertRGBToGrayMinMax(L.L_CHOOSE_MAX);  /* 0 if white */
        pix3 = pix2.ThresholdToBinary(1);  /* sets white pixels to 1 */
        Invert(pix3, pix3);  /* sets white pixels to 0 */

        /* Generate the alpha component using the distance transform,
         * which measures the distance to the nearest bg (0) pixel in pix3.
         * After multiplying by 128, its value is 0 (transparent)
         * over white pixels, and goes to opaque (255) two pixels away
         * from the nearest white pixel. */
        pix4 = pix3.DistanceFunction(8, 8, L.L_BOUNDARY_FG);
        pix4.MultConstantGray(128.0f);
        pixd.SetRGBComponent(pix4, L.L_ALPHA_CHANNEL);

        return pixd;
    }

    /*---------------------------------------------------------------------*
     *                          Fading from the edge                       *
     *---------------------------------------------------------------------*/
    // leptonica: pixLinearEdgeFade (blend.c)
    /// <summary>Fades the pixels near one edge of this pix, in place.</summary>
    /// <param name="dir">L_FROM_LEFT, L_FROM_RIGHT, L_FROM_TOP, L_FROM_BOT</param>
    /// <param name="fadeto">L_BLEND_TO_WHITE, L_BLEND_TO_BLACK</param>
    /// <param name="distfract">fraction of width or height over which fading occurs</param>
    /// <param name="maxfade">fraction of fading at the edge, &lt;= 1.0</param>
    /// <remarks>
    /// (1) In-place operation.
    /// (2) Maximum fading fraction %maxfade occurs at the edge of the image,
    ///     and the fraction goes to 0 at the fractional distance %distfract
    ///     from the edge.  %maxfade must be in [0, 1].
    /// (3) %distrfact must be in [0, 1], and typically it would be &lt;= 0.5.
    /// </remarks>
    public void LinearEdgeFade(int dir, int fadeto, float distfract, float maxfade)
    {
        const string func = "pixLinearEdgeFade";
        int i, j, w, h, d, wpl, xmin = 0, ymin = 0, range, val, rval, gval, bval;
        float slope, limit, del;
        Pix pixs = this;

        if (pixs.Colormap != null)
            throw Lept.Arg(func, "pixs has a colormap");
        pixs.GetDimensions(out w, out h, out d);
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 8 or 32 bpp");
        if (dir != L.L_FROM_LEFT && dir != L.L_FROM_RIGHT &&
            dir != L.L_FROM_TOP && dir != L.L_FROM_BOT)
            throw Lept.Arg(func, "invalid fade direction from edge");
        if (fadeto != L.L_BLEND_TO_WHITE && fadeto != L.L_BLEND_TO_BLACK)
            throw Lept.Arg(func, "invalid fadeto photometry");
        if (maxfade <= 0) return;
        if (maxfade > 1.0)
            throw Lept.Arg(func, "invalid maxfade");
        if (distfract <= 0 || distfract * Math.Min(w, h) < 1.0) {
            Log.Info(func, "distfract is too small");
            return;
        }
        if (distfract > 1.0)
            throw Lept.Arg(func, "invalid distfract");

        /* Set up parameters */
        if (dir == L.L_FROM_LEFT) {
            range = (int)(distfract * w);
            xmin = 0;
            slope = maxfade / (float)range;
        } else if (dir == L.L_FROM_RIGHT) {
            range = (int)(distfract * w);
            xmin = w - range;
            slope = maxfade / (float)range;
        } else if (dir == L.L_FROM_TOP) {
            range = (int)(distfract * h);
            ymin = 0;
            slope = maxfade / (float)range;
        } else {  /* dir == L_FROM_BOT */
            range = (int)(distfract * h);
            ymin = h - range;
            slope = maxfade / (float)range;
        }

        limit = (fadeto == L.L_BLEND_TO_WHITE) ? 255.0f : 0.0f;
        uint[] data = pixs.Data;
        wpl = pixs.Wpl;
        if (dir == L.L_FROM_LEFT || dir == L.L_FROM_RIGHT) {
            for (j = 0; j < range; j++) {
                del = (dir == L.L_FROM_LEFT) ? maxfade - slope * j
                                             : maxfade - slope * (range - j);
                for (i = 0; i < h; i++) {
                    Span<uint> line = data.AsSpan(i * wpl);
                    if (d == 8) {
                        val = GetDataByte(line, xmin + j);
                        val = (int)(val + ((limit - val) * del + 0.5));
                        SetDataByte(line, xmin + j, val);
                    } else {  /* rgb */
                        ExtractRGBValues(line[xmin + j], out rval, out gval, out bval);
                        rval = (int)(rval + ((limit - rval) * del + 0.5));
                        gval = (int)(gval + ((limit - gval) * del + 0.5));
                        bval = (int)(bval + ((limit - bval) * del + 0.5));
                        ComposeRGBPixel(rval, gval, bval, out line[xmin + j]);
                    }
                }
            }
        } else {  /* dir == L_FROM_TOP || L_FROM_BOT */
            for (i = 0; i < range; i++) {
                del = (dir == L.L_FROM_TOP) ? maxfade - slope * i
                                            : maxfade - slope * (range - i);
                Span<uint> line = data.AsSpan((ymin + i) * wpl);
                for (j = 0; j < w; j++) {
                    if (d == 8) {
                        val = GetDataByte(line, j);
                        val = (int)(val + ((limit - val) * del + 0.5));
                        SetDataByte(line, j, val);
                    } else {  /* rgb */
                        ExtractRGBValues(line[j], out rval, out gval, out bval);
                        rval = (int)(rval + ((limit - rval) * del + 0.5));
                        gval = (int)(gval + ((limit - gval) * del + 0.5));
                        bval = (int)(bval + ((limit - bval) * del + 0.5));
                        ComposeRGBPixel(rval, gval, bval, out line[j]);
                    }
                }
            }
        }
    }
}
