using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using static Leptonica.ArrayAccess;
using static Leptonica.L;

namespace Leptonica;

/*
 *  pixconv.c: Pixel depth conversion.
 *
 *  Differences from C:
 *    - Errors that return NULL or 1 in C throw.
 *    - pixAddGrayColormap8() and the other functions that modify their input
 *      in place keep that behavior.
 *    - pixQuantizeIfFewColors() returns the quantized pix, or null if there
 *      are too many colors (C: error return with *ppixd == NULL).
 *
 *  Stubs for callees in other files: pixNumColors, pixColorsForQuantization
 *  (colorcontent.c), pixThresholdTo2bpp, pixThresholdTo4bpp, pixThresholdOn8bpp
 *  (grayquant.c), pixConvertRGBToHue, pixConvertRGBToSaturation (colorspace.c),
 *  pixOctreeColorQuant, pixFewColorsOctcubeQuant1, pixFewColorsOctcubeQuant2,
 *  pixOctcubeHistogram (colorquant1.c), pixBackgroundNormSimple (adaptmap.c),
 *  pixScaleBySampling (scale1.c), pixScaleAndTransferAlpha (scale2.c).
 */

/// <summary>Constants from pix.h used by pixconv.c.</summary>
public static partial class L
{
    /* Perceptual weights for rgb to gray (pix.h) */
    /// <summary>Perceptual weight of the red component in the conversion from rgb to gray.</summary>
    public const float L_RED_WEIGHT = 0.3f;
    /// <summary>Perceptual weight of the green component in the conversion from rgb to gray.</summary>
    public const float L_GREEN_WEIGHT = 0.5f;
    /// <summary>Perceptual weight of the blue component in the conversion from rgb to gray.</summary>
    public const float L_BLUE_WEIGHT = 0.2f;

    /* Conversion between 16 bpp and 8 bpp, or 32 bpp and 16 bpp (pix.h) */
    /// <summary>Use the least significant byte.</summary>
    public const int L_LS_BYTE = 1;
    /// <summary>Use the most significant byte.</summary>
    public const int L_MS_BYTE = 2;
    /// <summary>Use the least significant byte if the maximum value is below 256; otherwise the most significant byte.</summary>
    public const int L_AUTO_BYTE = 3;
    /// <summary>Clip values to a maximum of 255.</summary>
    public const int L_CLIP_TO_FF = 4;
    /// <summary>Use the two least significant bytes.</summary>
    public const int L_LS_TWO_BYTES = 5;
    /// <summary>Use the two most significant bytes.</summary>
    public const int L_MS_TWO_BYTES = 6;
    /// <summary>Clip values to a maximum of 65535.</summary>
    public const int L_CLIP_TO_FFFF = 7;

    /* Subpixel color component ordering in LCD display (pix.h) */
    /// <summary>Sensor order is RGB from left to right.</summary>
    public const int L_SUBPIXEL_ORDER_RGB = 1;
    /// <summary>Sensor order is BGR from left to right.</summary>
    public const int L_SUBPIXEL_ORDER_BGR = 2;
    /// <summary>Sensor order is RGB from top to bottom.</summary>
    public const int L_SUBPIXEL_ORDER_VRGB = 3;
    /// <summary>Sensor order is BGR from top to bottom.</summary>
    public const int L_SUBPIXEL_ORDER_VBGR = 4;
}

public sealed partial class Pix
{
    private static int var_NEUTRAL_BOOST_VAL = 180;   /* l_setNeutralBoostVal() changes it */

    /*-------------------------------------------------------------*
     *     Conversion from 8 bpp grayscale to 1, 2 4 and 8 bpp     *
     *-------------------------------------------------------------*/
    // leptonica: pixThreshold8 (pixconv.c)
    /// <summary>Thresholds an 8 bpp grayscale image to 1, 2, 4 or 8 bpp.</summary>
    /// <param name="d">destination depth: 1, 2, 4 or 8</param>
    /// <param name="nlevels">number of levels to be used for colormap</param>
    /// <param name="cmapflag">1 if makes colormap; 0 otherwise</param>
    /// <returns>pixd thresholded with standard dest thresholds</returns>
    /// <remarks>
    /// (1) This uses, by default, equally spaced "target" values
    ///     that depend on the number of levels, with thresholds
    ///     halfway between.  For N levels, with separation (N-1)/255,
    ///     there are N-1 fixed thresholds.
    /// (2) For 1 bpp destination, the number of levels can only be 2
    ///     and if a cmap is made, black is (0,0,0) and white
    ///     is (255,255,255), which is opposite to the convention
    ///     without a colormap.
    /// (3) For 1, 2 and 4 bpp, the nlevels arg is used if a colormap
    ///     is made; otherwise, we take the most significant bits
    ///     from the src that will fit in the dest.
    /// (4) For 8 bpp, the input pixs is quantized to nlevels.  The
    ///     dest quantized with that mapping, either through a colormap
    ///     table or directly with 8 bit values.
    /// (5) Typically you should not use make a colormap for 1 bpp dest.
    /// (6) This is not dithering.  Each pixel is treated independently.
    /// </remarks>
    public Pix Threshold8(int d, int nlevels, int cmapflag)
    {
        Pix pixs = this;
        Pix pixd;

        if (pixs.Depth != 8)
            throw Lept.Arg("pixThreshold8", "pixs not 8 bpp");
        if (cmapflag != 0 && nlevels < 2)
            throw Lept.Arg("pixThreshold8", "nlevels must be at least 2");

        switch (d) {
        case 1:
            pixd = pixs.ThresholdToBinary(128);
            if (cmapflag != 0) {
                PixColormap cmap = PixColormap.CreateLinear(1, 2);
                pixd.SetColormap(cmap);
            }
            break;
        case 2:
            pixd = pixs.ThresholdTo2bpp(nlevels, cmapflag != 0);
            break;
        case 4:
            pixd = pixs.ThresholdTo4bpp(nlevels, cmapflag != 0);
            break;
        case 8:
            pixd = pixs.ThresholdOn8bpp(nlevels, cmapflag != 0);
            break;
        default:
            throw Lept.Arg("pixThreshold8", "d must be in {1,2,4,8}");
        }

        pixd.CopyInputFormat(pixs);
        return pixd;
    }

    /*-------------------------------------------------------------*
     *               Conversion from colormapped pix               *
     *-------------------------------------------------------------*/
    // leptonica: pixRemoveColormapGeneral (pixconv.c)
    /// <summary>
    /// Removes the colormap if there is one; otherwise returns a clone or
    /// a copy of pixs.
    /// </summary>
    /// <param name="type">REMOVE_CMAP_TO_BINARY, REMOVE_CMAP_TO_GRAYSCALE,
    /// REMOVE_CMAP_TO_FULL_COLOR, REMOVE_CMAP_WITH_ALPHA, REMOVE_CMAP_BASED_ON_SRC</param>
    /// <param name="ifnocmap">L_CLONE, L_COPY</param>
    /// <returns>pixd always a new pix; without colormap</returns>
    /// <remarks>
    /// (1) Convenience function that allows choice between returning
    ///     a clone or a copy if pixs does not have a colormap.
    /// (2) See pixRemoveColormap().
    /// </remarks>
    public Pix RemoveColormapGeneral(int type, int ifnocmap)
    {
        Pix pixs = this;
        if (ifnocmap != L_CLONE && ifnocmap != L_COPY)
            throw Lept.Arg("pixRemoveColormapGeneral", "invalid value for ifnocmap");

        if (pixs.Colormap != null)
            return pixs.RemoveColormap(type);

        if (ifnocmap == L_CLONE)
            return Clone(pixs);
        else
            return Copy(null, pixs);
    }

    // leptonica: pixRemoveColormap (pixconv.c)
    /// <summary>Removes the colormap, converting to 1 bpp, 8 bpp gray or 32 bpp rgb(a).</summary>
    /// <param name="type">REMOVE_CMAP_TO_BINARY, REMOVE_CMAP_TO_GRAYSCALE,
    /// REMOVE_CMAP_TO_FULL_COLOR, REMOVE_CMAP_WITH_ALPHA, REMOVE_CMAP_BASED_ON_SRC</param>
    /// <returns>pixd without colormap</returns>
    /// <remarks>
    /// (1) If pixs does not have a colormap, a clone is returned.
    /// (2) Otherwise, the input pixs is restricted to 1, 2, 4 or 8 bpp.
    /// (3) Use REMOVE_CMAP_TO_BINARY only on 1 bpp pix.
    /// (4) For grayscale conversion from RGB, use a weighted average
    ///     of RGB values, and always return an 8 bpp pix, regardless
    ///     of whether the input pixs depth is 2, 4 or 8 bpp.
    /// (5) REMOVE_CMAP_TO_FULL_COLOR ignores the alpha component and
    ///     returns a 32 bpp pix with spp == 3 and the alpha bytes are 0.
    /// (6) For REMOVE_CMAP_BASED_ON_SRC, if there is no color, this
    ///     returns either a 1 bpp or 8 bpp grayscale pix.
    ///     If there is color, this returns a 32 bpp pix, with either:
    ///      * 3 spp, if the alpha values are all 255 (opaque), or
    ///      * 4 spp (preserving the alpha), if any alpha values are not 255.
    /// </remarks>
    public Pix RemoveColormap(int type)
    {
        Pix pixs = this;
        int sval, rval, gval, bval, val0, val1;
        int i, j, k, count;
        uint sword, dword;
        Pix pixd;

        PixColormap? cmap = pixs.Colormap;
        if (cmap == null)
            return Clone(pixs);
        if (type != REMOVE_CMAP_TO_BINARY &&
            type != REMOVE_CMAP_TO_GRAYSCALE &&
            type != REMOVE_CMAP_TO_FULL_COLOR &&
            type != REMOVE_CMAP_WITH_ALPHA &&
            type != REMOVE_CMAP_BASED_ON_SRC) {
            Log.Warning("pixRemoveColormap", "Invalid type; converting based on src");
            type = REMOVE_CMAP_BASED_ON_SRC;
        }
        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 2 && d != 4 && d != 8)
            throw Lept.Arg("pixRemoveColormap", "pixs must be {1,2,4,8} bpp");

        int ncolors = cmap.Count;
        int nalloc = 1 << d;  /* allocate for max size in case of pixel corruption */
        if (ncolors > nalloc)
            throw Lept.Arg("pixRemoveColormap", "too many colors for pixel depth");

        if (d != 1 && type == REMOVE_CMAP_TO_BINARY) {
            Log.Warning("pixRemoveColormap", "not 1 bpp; can't remove cmap to binary");
            type = REMOVE_CMAP_BASED_ON_SRC;
        }

            /* Select output type depending on colormap content */
        if (type == REMOVE_CMAP_BASED_ON_SRC) {
            bool opaque = true, colorfound = false;
            int firstGray = 0, secondGray = 0;
            for (i = 0; i < ncolors; i++) {
                cmap.GetRGBA(i, out int r, out int g, out int b, out int a);
                opaque &= a == 255;
                colorfound |= r != g || r != b;
                if (i == 0) firstGray = r;
                if (i == 1) secondGray = r;
            }
            if (!opaque) {  /* save the alpha */
                type = REMOVE_CMAP_WITH_ALPHA;
            } else if (colorfound) {
                type = REMOVE_CMAP_TO_FULL_COLOR;
            } else {  /* opaque and no color */
                bool blackwhite = ncolors == 2 &&
                    ((firstGray == 0 && secondGray == 255) || (firstGray == 255 && secondGray == 0));
                if (d == 1 && blackwhite)  /* can binarize without loss */
                    type = REMOVE_CMAP_TO_BINARY;
                else
                    type = REMOVE_CMAP_TO_GRAYSCALE;
            }
        }

        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        if (type == REMOVE_CMAP_TO_BINARY) {
            pixd = Copy(null, pixs);
            cmap.GetColor(0, out rval, out gval, out bval);
            val0 = rval + gval + bval;
            cmap.GetColor(1, out rval, out gval, out bval);
            val1 = rval + gval + bval;
            if (val0 < val1)  /* photometrically inverted from standard */
                Invert(pixd, pixd);
            pixd.DestroyColormap();
        } else if (type == REMOVE_CMAP_TO_GRAYSCALE) {
            pixd = Create(w, h, 8);
            pixd.CopyResolution(pixs);
            pixd.CopyInputFormat(pixs);
            uint[] datad = pixd.Data;
            int wpld = pixd.Wpl;
            Span<uint> graymap = stackalloc uint[nalloc];
            // Invalid palette indices must still map to zero, even with SkipLocalsInit.
            graymap.Clear();
            for (i = 0; i < ncolors; i++) {
                cmap.GetRGBA(i, out int r, out int g, out int b, out _);
                graymap[i] = (uint)(L_RED_WEIGHT * r +
                                    L_GREEN_WEIGHT * g +
                                    L_BLUE_WEIGHT * b + 0.5);
            }
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                switch (d)   /* depth test above; no default permitted */
                {
                    case 8:
                            /* Unrolled 4x */
                        for (j = 0, count = 0; j + 3 < w; j += 4, count++) {
                            sword = lines[count];
                            dword = (graymap[(int)((sword >> 24) & 0xff)] << 24) |
                                (graymap[(int)((sword >> 16) & 0xff)] << 16) |
                                (graymap[(int)((sword >> 8) & 0xff)] << 8) |
                                graymap[(int)(sword & 0xff)];
                            lined[count] = dword;
                        }
                            /* Cleanup partial word */
                        for (; j < w; j++) {
                            sval = GetDataByte(lines, j);
                            gval = (int)graymap[sval];
                            SetDataByte(lined, j, gval);
                        }
                        break;
                    case 4:
                            /* Unrolled 8x */
                        for (j = 0, count = 0; j + 7 < w; j += 8, count++) {
                            sword = lines[count];
                            dword = (graymap[(int)((sword >> 28) & 0xf)] << 24) |
                                (graymap[(int)((sword >> 24) & 0xf)] << 16) |
                                (graymap[(int)((sword >> 20) & 0xf)] << 8) |
                                graymap[(int)((sword >> 16) & 0xf)];
                            lined[2 * count] = dword;
                            dword = (graymap[(int)((sword >> 12) & 0xf)] << 24) |
                                (graymap[(int)((sword >> 8) & 0xf)] << 16) |
                                (graymap[(int)((sword >> 4) & 0xf)] << 8) |
                                graymap[(int)(sword & 0xf)];
                            lined[2 * count + 1] = dword;
                        }
                            /* Cleanup partial word */
                        for (; j < w; j++) {
                            sval = GetDataQbit(lines, j);
                            gval = (int)graymap[sval];
                            SetDataByte(lined, j, gval);
                        }
                        break;
                    case 2:
                            /* Unrolled 16x */
                        for (j = 0, count = 0; j + 15 < w; j += 16, count++) {
                            sword = lines[count];
                            dword = (graymap[(int)((sword >> 30) & 0x3)] << 24) |
                                (graymap[(int)((sword >> 28) & 0x3)] << 16) |
                                (graymap[(int)((sword >> 26) & 0x3)] << 8) |
                                graymap[(int)((sword >> 24) & 0x3)];
                            lined[4 * count] = dword;
                            dword = (graymap[(int)((sword >> 22) & 0x3)] << 24) |
                                (graymap[(int)((sword >> 20) & 0x3)] << 16) |
                                (graymap[(int)((sword >> 18) & 0x3)] << 8) |
                                graymap[(int)((sword >> 16) & 0x3)];
                            lined[4 * count + 1] = dword;
                            dword = (graymap[(int)((sword >> 14) & 0x3)] << 24) |
                                (graymap[(int)((sword >> 12) & 0x3)] << 16) |
                                (graymap[(int)((sword >> 10) & 0x3)] << 8) |
                                graymap[(int)((sword >> 8) & 0x3)];
                            lined[4 * count + 2] = dword;
                            dword = (graymap[(int)((sword >> 6) & 0x3)] << 24) |
                                (graymap[(int)((sword >> 4) & 0x3)] << 16) |
                                (graymap[(int)((sword >> 2) & 0x3)] << 8) |
                                graymap[(int)(sword & 0x3)];
                            lined[4 * count + 3] = dword;
                        }
                            /* Cleanup partial word */
                        for (; j < w; j++) {
                            sval = GetDataDibit(lines, j);
                            gval = (int)graymap[sval];
                            SetDataByte(lined, j, gval);
                        }
                        break;
                    case 1:
                            /* Unrolled 8x */
                        for (j = 0, count = 0; j + 31 < w; j += 32, count++) {
                            sword = lines[count];
                            for (k = 0; k < 4; k++) {
                                    /* The top byte is always the relevant one */
                                dword = (graymap[(int)((sword >> 31) & 0x1)] << 24) |
                                    (graymap[(int)((sword >> 30) & 0x1)] << 16) |
                                    (graymap[(int)((sword >> 29) & 0x1)] << 8) |
                                    graymap[(int)((sword >> 28) & 0x1)];
                                lined[8 * count + 2 * k] = dword;
                                dword = (graymap[(int)((sword >> 27) & 0x1)] << 24) |
                                    (graymap[(int)((sword >> 26) & 0x1)] << 16) |
                                    (graymap[(int)((sword >> 25) & 0x1)] << 8) |
                                    graymap[(int)((sword >> 24) & 0x1)];
                                lined[8 * count + 2 * k + 1] = dword;
                                sword <<= 8;  /* Move up the next byte */
                            }
                        }
                            /* Cleanup partial word */
                        for (; j < w; j++) {
                            sval = GetDataBit(lines, j);
                            gval = (int)graymap[sval];
                            SetDataByte(lined, j, gval);
                        }
                        break;
                    default:
                        throw Lept.Fail("pixRemoveColormap", "invalid depth");
                }
            }
        } else {  /* type == REMOVE_CMAP_TO_FULL_COLOR or REMOVE_CMAP_WITH_ALPHA */
            pixd = Create(w, h, 32);
            pixd.CopyInputFormat(pixs);
            pixd.CopyResolution(pixs);
            if (type == REMOVE_CMAP_WITH_ALPHA)
                pixd.Spp = 4;
            uint[] datad = pixd.Data;
            int wpld = pixd.Wpl;
            Span<uint> lut = stackalloc uint[nalloc];
            lut.Clear();
            for (i = 0; i < ncolors; i++) {
                cmap.GetRGBA(i, out int r, out int g, out int b, out int a);
                if (type == REMOVE_CMAP_TO_FULL_COLOR)
                    ComposeRGBPixel(r, g, b, out lut[i]);
                else  /* full color plus alpha */
                    ComposeRGBAPixel(r, g, b, a, out lut[i]);
            }

            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (j = 0; j < w; j++) {
                    if (d == 8)
                        sval = GetDataByte(lines, j);
                    else if (d == 4)
                        sval = GetDataQbit(lines, j);
                    else if (d == 2)
                        sval = GetDataDibit(lines, j);
                    else  /* (d == 1) */
                        sval = GetDataBit(lines, j);
                    if (sval >= ncolors)
                        Log.Warning("pixRemoveColormap", "pixel value out of bounds");
                    else
                        lined[j] = lut[sval];
                }
            }
        }

        return pixd;
    }

    /*-------------------------------------------------------------*
     *              Add colormap losslessly (8 to 8)               *
     *-------------------------------------------------------------*/
    // leptonica: pixAddGrayColormap8 (pixconv.c)
    /// <summary>Adds a linear gray colormap to an 8 bpp pix, in place.</summary>
    /// <remarks>(1) If pixs has a colormap, this is a no-op.</remarks>
    public void AddGrayColormap8()
    {
        Pix pixs = this;
        if (pixs.Depth != 8)
            throw Lept.Arg("pixAddGrayColormap8", "pixs not defined or not 8 bpp");
        if (pixs.Colormap != null)
            return;

        PixColormap cmap = PixColormap.CreateLinear(8, 256);
        pixs.SetColormap(cmap);
    }

    // leptonica: pixAddMinimalGrayColormap8 (pixconv.c)
    /// <summary>
    /// Generates a colormapped version of the input image with one colormap
    /// entry for each unique gray level.
    /// </summary>
    /// <remarks>
    /// (1) This generates a colormapped version of the input image
    ///     that has the same number of colormap entries as the
    ///     input image has unique gray levels.
    /// </remarks>
    public Pix AddMinimalGrayColormap8()
    {
        Pix pixs = this;
        int i, j, index, val;
        Pix pix1, pixd;

        if (pixs.Depth != 8)
            throw Lept.Arg("pixAddMinimalGrayColormap8", "pixs undefined or not 8 bpp");

            /* Eliminate the easy cases */
        int ncolors = pixs.NumColors(1);
        PixColormap? cmap = pixs.Colormap;
        if (cmap != null) {
            if (cmap.Count == ncolors)  /* irreducible */
                return Copy(null, pixs);
            else
                pix1 = pixs.RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
        } else {
            if (ncolors == 256) {
                pix1 = Copy(null, pixs);
                pix1.AddGrayColormap8();
                return pix1;
            }
            pix1 = Clone(pixs);
        }

            /* Find the gray levels and make a reverse map */
        pix1.GetDimensions(out int w, out int h, out _);
        uint[] data1 = pix1.Data;
        int wpl1 = pix1.Wpl;
        int[] inta = new int[256];
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl1, wpl1);
            for (j = 0; j < w; j++) {
                val = GetDataByte(line1, j);
                inta[val] = 1;
            }
        }
        cmap = PixColormap.Create(8);
        int[] revmap = new int[256];
        for (i = 0, index = 0; i < 256; i++) {
            if (inta[i] != 0) {
                cmap.AddColor(i, i, i);
                revmap[i] = index++;
            }
        }

            /* Set all pixels in pixd to the colormap index */
        pixd = CreateTemplate(pix1);
        pixd.SetColormap(cmap);
        pixd.CopyInputFormat(pixs);
        pixd.CopyResolution(pixs);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl1, wpl1);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                val = GetDataByte(line1, j);
                SetDataByte(lined, j, revmap[val]);
            }
        }

        return pixd;
    }

    /*-------------------------------------------------------------*
     *            Conversion from RGB color to grayscale           *
     *-------------------------------------------------------------*/
    // leptonica: pixConvertRGBToLuminance (pixconv.c)
    /// <summary>Converts a 32 bpp RGB pix to 8 bpp gray using a standard luminance conversion.</summary>
    /// <remarks>(1) Use a standard luminance conversion.</remarks>
    public Pix ConvertRGBToLuminance()
    {
        return ConvertRGBToGray(0.0f, 0.0f, 0.0f);
    }

    // leptonica: pixConvertRGBToGrayGeneral (pixconv.c)
    /// <summary>Converts a 32 bpp RGB pix to 8 bpp gray, selecting a component or combination.</summary>
    /// <param name="type">color selection flag</param>
    /// <param name="rwt">ignored if type != L_SELECT_WEIGHTED; if used, must sum to 1.0</param>
    /// <param name="gwt">ignored if type != L_SELECT_WEIGHTED; if used, must sum to 1.0</param>
    /// <param name="bwt">ignored if type != L_SELECT_WEIGHTED; if used, must sum to 1.0</param>
    /// <returns>8 bpp pix</returns>
    /// <remarks>
    /// (1) The color selection flag is one of: L_SELECT_RED, L_SELECT_GREEN,
    ///     L_SELECT_BLUE, L_SELECT_MIN, L_SELECT_MAX, L_SELECT_AVERAGE,
    ///     L_SELECT_HUE, L_SELECT_SATURATION, L_SELECT_WEIGHTED.
    /// (2) The weights, if used, must all be non-negative and must sum to 1.0.
    /// </remarks>
    public Pix ConvertRGBToGrayGeneral(int type, float rwt, float gwt, float bwt)
    {
        Pix pixs = this;
        Pix pix1;

        if (pixs.Depth != 32)
            throw Lept.Arg("pixConvertRGBToGrayGeneral", "pixs undefined or not 32 bpp");
        if (type != L_SELECT_RED && type != L_SELECT_GREEN &&
            type != L_SELECT_BLUE && type != L_SELECT_MIN &&
            type != L_SELECT_MAX && type != L_SELECT_AVERAGE &&
            type != L_SELECT_HUE && type != L_SELECT_SATURATION &&
            type != L_SELECT_WEIGHTED)
            throw Lept.Arg("pixConvertRGBToGrayGeneral", "invalid type");

        if (type == L_SELECT_RED) {
            pix1 = pixs.GetRGBComponent(COLOR_RED);
        } else if (type == L_SELECT_GREEN) {
            pix1 = pixs.GetRGBComponent(COLOR_GREEN);
        } else if (type == L_SELECT_BLUE) {
            pix1 = pixs.GetRGBComponent(COLOR_BLUE);
        } else if (type == L_SELECT_MIN) {
            pix1 = pixs.ConvertRGBToGrayMinMax(L_CHOOSE_MIN);
        } else if (type == L_SELECT_MAX) {
            pix1 = pixs.ConvertRGBToGrayMinMax(L_CHOOSE_MAX);
        } else if (type == L_SELECT_AVERAGE) {
            pix1 = pixs.ConvertRGBToGray(0.34f, 0.33f, 0.33f);
        } else if (type == L_SELECT_HUE) {
            pix1 = pixs.ConvertRGBToHue();
        } else if (type == L_SELECT_SATURATION) {
            pix1 = pixs.ConvertRGBToSaturation();
        } else { /* L_SELECT_WEIGHTED */
            if (rwt < 0.0 || gwt < 0.0 || bwt < 0.0)
                throw Lept.Arg("pixConvertRGBToGrayGeneral", "weights not all >= 0.0");
            if (rwt + gwt + bwt != 1.0)
                throw Lept.Arg("pixConvertRGBToGrayGeneral", "weights don't sum to 1.0");
            pix1 = pixs.ConvertRGBToGray(rwt, gwt, bwt);
        }

        return pix1;
    }

    // leptonica: pixConvertRGBToGray (pixconv.c)
    /// <summary>Converts a 32 bpp RGB pix to 8 bpp gray with a weighted average.</summary>
    /// <param name="rwt">non-negative; these should add to 1.0, or use 0.0 for default</param>
    /// <param name="gwt">non-negative; these should add to 1.0, or use 0.0 for default</param>
    /// <param name="bwt">non-negative; these should add to 1.0, or use 0.0 for default</param>
    /// <returns>8 bpp pix</returns>
    /// <remarks>(1) Use a weighted average of the RGB values.</remarks>
    public Pix ConvertRGBToGray(float rwt, float gwt, float bwt)
    {
        Pix pixs = this;

        if (pixs.Depth != 32)
            throw Lept.Arg("pixConvertRGBToGray", "pixs not 32 bpp");
        if (rwt < 0.0 || gwt < 0.0 || bwt < 0.0)
            throw Lept.Arg("pixConvertRGBToGray", "weights not all >= 0.0");

            /* Make sure the sum of weights is 1.0; otherwise, you can get
             * overflow in the gray value. */
        if (rwt == 0.0 && gwt == 0.0 && bwt == 0.0) {
            rwt = L_RED_WEIGHT;
            gwt = L_GREEN_WEIGHT;
            bwt = L_BLUE_WEIGHT;
        }
        float sum = rwt + gwt + bwt;
        if (Math.Abs(sum - 1.0) > 0.0001) {  /* maintain ratios with sum == 1.0 */
            Log.Warning("pixConvertRGBToGray", "weights don't sum to 1; maintaining ratios");
            rwt = rwt / sum;
            gwt = gwt / sum;
            bwt = bwt / sum;
        }

        pixs.GetDimensions(out int w, out int h, out _);
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        Pix pixd = CreateNoInit(w, h, 8);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        int fullwords = w >> 2;
        // Cache the same single-precision products used by the scalar formula.
        // Keep the additions in their original order and retain double 0.5 rounding.
        float[] products = new float[3 * 256];
        for (int value = 0; value < 256; value++) {
            products[value] = rwt * (uint)value;
            products[256 + value] = gwt * (uint)value;
            products[512 + value] = bwt * (uint)value;
        }
        LeptParallel.ForRows(h, (long)w * 6L, (yStart, yEnd) => {
            for (int row = yStart; row < yEnd; row++) {
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(row * wpls));
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(row * wpld));
                int px = 0;
                for (int jw = 0; jw < fullwords; jw++, px += 4) {
                    uint w0 = Unsafe.Add(ref rs, (nuint)px);
                    uint w1 = Unsafe.Add(ref rs, (nuint)(px + 1));
                    uint w2 = Unsafe.Add(ref rs, (nuint)(px + 2));
                    uint w3 = Unsafe.Add(ref rs, (nuint)(px + 3));
                    uint v0 = (uint)(int)(products[0 + (int)((w0 >> L_RED_SHIFT) & 0xff)] + products[256 + (int)((w0 >> L_GREEN_SHIFT) & 0xff)] + products[512 + (int)((w0 >> L_BLUE_SHIFT) & 0xff)] + 0.5);
                    uint v1 = (uint)(int)(products[0 + (int)((w1 >> L_RED_SHIFT) & 0xff)] + products[256 + (int)((w1 >> L_GREEN_SHIFT) & 0xff)] + products[512 + (int)((w1 >> L_BLUE_SHIFT) & 0xff)] + 0.5);
                    uint v2 = (uint)(int)(products[0 + (int)((w2 >> L_RED_SHIFT) & 0xff)] + products[256 + (int)((w2 >> L_GREEN_SHIFT) & 0xff)] + products[512 + (int)((w2 >> L_BLUE_SHIFT) & 0xff)] + 0.5);
                    uint v3 = (uint)(int)(products[0 + (int)((w3 >> L_RED_SHIFT) & 0xff)] + products[256 + (int)((w3 >> L_GREEN_SHIFT) & 0xff)] + products[512 + (int)((w3 >> L_BLUE_SHIFT) & 0xff)] + 0.5);
                    Unsafe.Add(ref rd, (nuint)jw) = (v0 << 24) | (v1 << 16) | (v2 << 8) | v3;
                }
                for (int col = px; col < w; col++) {
                    uint wd = Unsafe.Add(ref rs, (nuint)col);
                    int v = (int)(products[0 + (int)((wd >> L_RED_SHIFT) & 0xff)] +
                                  products[256 + (int)((wd >> L_GREEN_SHIFT) & 0xff)] +
                                  products[512 + (int)((wd >> L_BLUE_SHIFT) & 0xff)] + 0.5);
                    SetDataByteUnchecked(ref rd, col, v);
                }
            }
        });

        return pixd;
    }

    // leptonica: pixConvertRGBToGrayFast (pixconv.c)
    /// <summary>Converts a 32 bpp RGB pix to 8 bpp gray by taking the green channel.</summary>
    /// <remarks>
    /// (1) This function should be used if speed of conversion
    ///     is paramount, and the green channel can be used as
    ///     a fair representative of the RGB intensity.  It is
    ///     several times faster than pixConvertRGBToGray().
    /// (2) To combine RGB to gray conversion with subsampling,
    ///     use pixScaleRGBToGrayFast() instead.
    /// </remarks>
    public Pix ConvertRGBToGrayFast()
    {
        Pix pixs = this;
        int i, j, val;

        if (pixs.Depth != 32)
            throw Lept.Arg("pixConvertRGBToGrayFast", "pixs not 32 bpp");

        pixs.GetDimensions(out int w, out int h, out _);
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        Pix pixd = CreateNoInit(w, h, 8);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        Vector128<byte> greenPackMask = Vector128.Create(
            (byte)14, 10, 6, 2,
            0x80, 0x80, 0x80, 0x80,
            0x80, 0x80, 0x80, 0x80,
            0x80, 0x80, 0x80, 0x80);

        for (i = 0; i < h; i++) {
            ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
            ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
            j = 0;
            if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
                ref byte bs = ref Unsafe.As<uint, byte>(ref rs);
                for (int jw = 0; j + 4 <= w; j += 4, jw++) {
                    Vector128<byte> vRgb = Vector128.LoadUnsafe(ref bs, (nuint)(j * 4));
                    Unsafe.Add(ref rd, (nuint)jw) = Vector128.Shuffle(vRgb, greenPackMask).AsUInt32().ToScalar();
                }
            } else {
                for (int jw = 0; j + 4 <= w; j += 4, jw++) {
                    uint g0 = (Unsafe.Add(ref rs, (nuint)j) >> L_GREEN_SHIFT) & 0xff;
                    uint g1 = (Unsafe.Add(ref rs, (nuint)(j + 1)) >> L_GREEN_SHIFT) & 0xff;
                    uint g2 = (Unsafe.Add(ref rs, (nuint)(j + 2)) >> L_GREEN_SHIFT) & 0xff;
                    uint g3 = (Unsafe.Add(ref rs, (nuint)(j + 3)) >> L_GREEN_SHIFT) & 0xff;
                    Unsafe.Add(ref rd, (nuint)jw) = (g0 << 24) | (g1 << 16) | (g2 << 8) | g3;
                }
            }
            for (; j < w; j++) {
                val = (int)((Unsafe.Add(ref rs, (nuint)j) >> L_GREEN_SHIFT) & 0xff);
                SetDataByteUnchecked(ref rd, j, val);
            }
        }

        return pixd;
    }

    // leptonica: pixConvertRGBToGrayMinMax (pixconv.c)
    /// <summary>Converts a 32 bpp RGB pix to 8 bpp gray by choosing min, max, maxdiff or boosted min/max.</summary>
    /// <param name="type">L_CHOOSE_MIN, L_CHOOSE_MAX, L_CHOOSE_MAXDIFF,
    /// L_CHOOSE_MIN_BOOST, L_CHOOSE_MAX_BOOST</param>
    /// <returns>8 bpp pix</returns>
    /// <remarks>
    /// (1) This chooses various components or combinations of them,
    ///     from the three RGB sample values.  In addition to choosing
    ///     the min, max, and maxdiff (difference between max and min),
    ///     this also allows boosting the min and max about a reference
    ///     value.
    /// (2) The default reference value for boosting the min and max
    ///     is 200.  This can be changed with l_setNeutralBoostVal()
    /// (3) The result with L_CHOOSE_MAXDIFF is surprisingly sensitive
    ///     to a jpeg compression/decompression cycle with quality = 75.
    /// </remarks>
    public Pix ConvertRGBToGrayMinMax(int type)
    {
        Pix pixs = this;
        int i, j, val, minval, maxval;

        if (pixs.Depth != 32)
            throw Lept.Arg("pixConvertRGBToGrayMinMax", "pixs not 32 bpp");
        if (type != L_CHOOSE_MIN && type != L_CHOOSE_MAX &&
            type != L_CHOOSE_MAXDIFF && type != L_CHOOSE_MIN_BOOST &&
            type != L_CHOOSE_MAX_BOOST)
            throw Lept.Arg("pixConvertRGBToGrayMinMax", "invalid type");

        pixs.GetDimensions(out int w, out int h, out _);
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        Pix pixd = Create(w, h, 8);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                ExtractRGBValues(lines[j], out int rval, out int gval, out int bval);
                if (type == L_CHOOSE_MIN || type == L_CHOOSE_MIN_BOOST) {
                    val = Math.Min(rval, gval);
                    val = Math.Min(val, bval);
                    if (type == L_CHOOSE_MIN_BOOST)
                        val = Math.Min(255, (val * val) / var_NEUTRAL_BOOST_VAL);
                } else if (type == L_CHOOSE_MAX || type == L_CHOOSE_MAX_BOOST) {
                    val = Math.Max(rval, gval);
                    val = Math.Max(val, bval);
                    if (type == L_CHOOSE_MAX_BOOST)
                        val = Math.Min(255, (val * val) / var_NEUTRAL_BOOST_VAL);
                } else {  /* L_CHOOSE_MAXDIFF */
                    minval = Math.Min(rval, gval);
                    minval = Math.Min(minval, bval);
                    maxval = Math.Max(rval, gval);
                    maxval = Math.Max(maxval, bval);
                    val = maxval - minval;
                }
                SetDataByte(lined, j, val);
            }
        }

        return pixd;
    }

    // leptonica: pixConvertRGBToGraySatBoost (pixconv.c)
    /// <summary>Returns the max component value of each pixel, boosted by the saturation.</summary>
    /// <param name="refval">between 1 and 255; typ. less than 128</param>
    /// <returns>pixd 8 bpp</returns>
    /// <remarks>
    /// (1) This returns the max component value, boosted by
    ///     the saturation. The maximum boost occurs where
    ///     the maximum component value is equal to some reference value.
    ///     This particular weighting is due to Dany Qumsiyeh.
    /// (2) For gray pixels (zero saturation), this returns
    ///     the intensity of any component.
    /// (3) For fully saturated pixels ('fullsat'), this rises linearly
    ///     with the max value and has a slope equal to 255 divided
    ///     by the reference value; for a max value greater than
    ///     the reference value, it is clipped to 255.
    /// (4) For saturation values in between, the output is a linear
    ///     combination of (2) and (3), weighted by saturation.
    ///     It falls between these two curves, and does not exceed 255.
    /// (5) This can be useful for distinguishing an object that has nonzero
    ///     saturation from a gray background.  For this, the refval
    ///     should be chosen near the expected value of the background,
    ///     to achieve maximum saturation boost there.
    /// </remarks>
    public Pix ConvertRGBToGraySatBoost(int refval)
    {
        Pix pixs = this;
        int i, j;
        int sval, minrg, maxrg, min, max, delta;
        int fullsat, newval;

        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 32 && pixs.Colormap == null)
            throw Lept.Arg("pixConvertRGBToGraySatBoost", "pixs not cmapped or rgb");
        if (refval < 1 || refval > 255)
            throw Lept.Arg("pixConvertRGBToGraySatBoost", "refval not in [1 ... 255]");

        Pix pixt = pixs.RemoveColormap(REMOVE_CMAP_TO_FULL_COLOR);
        Pix pixd = Create(w, h, 8);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        int wplt = pixt.Wpl;
        uint[] datat = pixt.Data;
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        float[] invmax = new float[256];
        float[] ratio = new float[256];
        for (i = 1; i < 256; i++) {  /* i == 0  --> delta = sval = newval = 0 */
            invmax[i] = 1.0f / (float)i;
            ratio[i] = (float)i / (float)refval;
        }
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> linet = datat.AsSpan(i * wplt, wplt);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                ExtractRGBValues(linet[j], out int rval, out int gval, out int bval);
                minrg = Math.Min(rval, gval);
                min = Math.Min(minrg, bval);
                maxrg = Math.Max(rval, gval);
                max = Math.Max(maxrg, bval);
                delta = max - min;
                if (delta == 0)  /* gray; no chroma */
                    sval = 0;
                else
                    sval = (int)(255.0 * (float)delta * invmax[max] + 0.5);

                float fsat = 255 * ratio[max];
                fullsat = (int)(255 < fsat ? 255.0f : fsat);
                newval = (sval * fullsat + (255 - sval) * max) / 255;
                SetDataByte(lined, j, newval);
            }
        }

        return pixd;
    }

    // leptonica: pixConvertRGBToGrayArb (pixconv.c)
    /// <summary>Converts a 32 bpp rgb pix to 8 bpp gray with arbitrary (possibly negative) weights.</summary>
    /// <param name="rc">arithmetic factor; can be negative</param>
    /// <param name="gc">arithmetic factor; can be negative</param>
    /// <param name="bc">arithmetic factor; can be negative</param>
    /// <returns>8 bpp pix</returns>
    /// <remarks>
    /// (1) This converts to gray using an arbitrary linear combination
    ///     of the rgb color components.  It differs from pixConvertToGray(),
    ///     which uses only positive coefficients that sum to 1.
    /// (2) The gray output values are clipped to 0 and 255.
    /// </remarks>
    public Pix ConvertRGBToGrayArb(float rc, float gc, float bc)
    {
        Pix pixs = this;
        int i, j, val;

        if (pixs.Depth != 32)
            throw Lept.Arg("pixConvertRGBToGrayArb", "pixs not 32 bpp");
        if (rc <= 0 && gc <= 0 && bc <= 0)
            throw Lept.Arg("pixConvertRGBToGrayArb", "all coefficients <= 0");

        pixs.GetDimensions(out int w, out int h, out _);
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        Pix pixd = Create(w, h, 8);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                ExtractRGBValues(lines[j], out int rval, out int gval, out int bval);
                val = (int)(rc * rval + gc * gval + bc * bval);
                val = Math.Min(255, Math.Max(0, val));
                SetDataByte(lined, j, val);
            }
        }

        return pixd;
    }

    // leptonica: pixConvertRGBToBinaryArb (pixconv.c)
    /// <summary>Makes a 1 bpp mask from an RGB image, using a linear combination of the components.</summary>
    /// <param name="rc">arithmetic factor; can be negative</param>
    /// <param name="gc">arithmetic factor; can be negative</param>
    /// <param name="bc">arithmetic factor; can be negative</param>
    /// <param name="thresh">binarization threshold</param>
    /// <param name="relation">L_SELECT_IF_LT, L_SELECT_IF_GT, L_SELECT_IF_LTE, L_SELECT_IF_GTE</param>
    /// <returns>1 bpp pix</returns>
    /// <remarks>
    /// (1) This makes a 1 bpp mask from an RGB image, using an arbitrary
    ///     linear combination of the rgb color components, along with
    ///     a threshold and a selection choice of the gray value relative
    ///     to %thresh.
    /// </remarks>
    public Pix ConvertRGBToBinaryArb(float rc, float gc, float bc, int thresh, int relation)
    {
        Pix pixs = this;

        if (pixs.Depth != 32)
            throw Lept.Arg("pixConvertRGBToBinaryArb", "pixs undefined or not 32 bpp");
        if (rc <= 0 && gc <= 0 && bc <= 0)
            throw Lept.Arg("pixConvertRGBToBinaryArb", "all coefficients <= 0");
        if (relation != L_SELECT_IF_LT && relation != L_SELECT_IF_GT &&
            relation != L_SELECT_IF_LTE && relation != L_SELECT_IF_GTE)
            throw Lept.Arg("pixConvertRGBToBinaryArb", "invalid relation");

        Pix pix1 = pixs.ConvertRGBToGrayArb(rc, gc, bc);
        int threshold = (relation == L_SELECT_IF_LTE || relation == L_SELECT_IF_GT) ?
                                 thresh : thresh + 1;
        Pix pix2 = pix1.ThresholdToBinary(threshold);
        if (relation == L_SELECT_IF_GT || relation == L_SELECT_IF_GTE)
            Invert(pix2, pix2);
        return pix2;
    }

    /*---------------------------------------------------------------------------*
     *                  Conversion from grayscale to colormap                    *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertGrayToColormap (pixconv.c)
    /// <summary>Adds a colormap to a 2, 4 or 8 bpp grayscale image without quantization.</summary>
    /// <returns>pixd 2, 4 or 8 bpp with colormap</returns>
    /// <remarks>
    /// (1) This is a simple interface for adding a colormap to a
    ///     2, 4 or 8 bpp grayscale image without causing any
    ///     quantization.  There is some similarity to operations
    ///     in grayquant.c, such as pixThresholdOn8bpp(), where
    ///     the emphasis is on quantization with an arbitrary number
    ///     of levels, and a colormap is an option.
    /// (2) Returns a copy if pixs already has a colormap.
    /// (3) For 8 bpp src, this is a lossless transformation.
    /// (4) For 2 and 4 bpp src, this generates a colormap that
    ///     assumes full coverage of the gray space, with equally spaced
    ///     levels: 4 levels for d = 2 and 16 levels for d = 4.
    /// (5) In all cases, the depth of the dest is the same as the src.
    /// </remarks>
    public Pix ConvertGrayToColormap()
    {
        Pix pixs = this;
        int d = pixs.Depth;
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg("pixConvertGrayToColormap", "pixs not 2, 4 or 8 bpp");

        if (pixs.Colormap != null) {
            Log.Info("pixConvertGrayToColormap", "pixs already has a colormap");
            return Copy(null, pixs);
        }

        if (d == 8)  /* lossless conversion */
            return pixs.ConvertGrayToColormap8(2);

            /* Build a cmap with equally spaced target values over the
             * full 8 bpp range. */
        Pix pixd = Copy(null, pixs);
        PixColormap cmap = PixColormap.CreateLinear(d, 1 << d);
        pixd.SetColormap(cmap);
        pixd.CopyInputFormat(pixs);
        return pixd;
    }

    // leptonica: pixConvertGrayToColormap8 (pixconv.c)
    /// <summary>Adds a colormap with exactly the gray values of an 8 bpp image; lossless.</summary>
    /// <param name="mindepth">of pixd; valid values are 2, 4 and 8</param>
    /// <returns>pixd 2, 4 or 8 bpp with colormap</returns>
    /// <remarks>
    /// (1) Returns a copy if pixs already has a colormap.
    /// (2) This is a lossless transformation; there is no quantization.
    ///     We compute the number of different gray values in pixs,
    ///     and construct a colormap that has exactly these values.
    /// (3) 'mindepth' is the minimum depth of pixd.  If mindepth == 8,
    ///     pixd will always be 8 bpp.  Let the number of different
    ///     gray values in pixs be ngray.  If mindepth == 4, we attempt
    ///     to save pixd as a 4 bpp image, but if ngray > 16,
    ///     pixd must be 8 bpp.  Likewise, if mindepth == 2,
    ///     the depth of pixd will be 2 if ngray &lt;= 4 and 4 if ngray &gt; 4
    ///     but &lt;= 16.
    /// </remarks>
    public Pix ConvertGrayToColormap8(int mindepth)
    {
        Pix pixs = this;
        int depth, i, j, index, val, newval;
        int[] array = new int[256];

        if (pixs.Depth != 8)
            throw Lept.Arg("pixConvertGrayToColormap8", "pixs not 8 bpp");
        if (mindepth != 2 && mindepth != 4 && mindepth != 8) {
            Log.Warning("pixConvertGrayToColormap8", "invalid value of mindepth; setting to 8");
            mindepth = 8;
        }

        if (pixs.Colormap != null) {
            Log.Info("pixConvertGrayToColormap8", "pixs already has a colormap");
            return Copy(null, pixs);
        }

        Numa na = pixs.GetGrayHistogram(1);
        int ncolors = na.GetCountRelativeToZero(L_GREATER_THAN_ZERO);
        if (mindepth == 8 || ncolors > 16)
            depth = 8;
        else if (mindepth == 4 || ncolors > 4)
            depth = 4;
        else
            depth = 2;

        pixs.GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, depth);
        PixColormap cmap = PixColormap.Create(depth);
        pixd.SetColormap(cmap);
        pixd.CopyInputFormat(pixs);
        pixd.CopyResolution(pixs);

        index = 0;
        for (i = 0; i < 256; i++) {
            array[i] = 0;  /* only to quiet the static checker */
            int num = na.GetIValue(i);
            if (num > 0) {
                cmap.AddColor(i, i, i);
                array[i] = index;
                index++;
            }
        }

        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                val = GetDataByte(lines, j);
                newval = array[val];
                if (depth == 2)
                    SetDataDibit(lined, j, newval);
                else if (depth == 4)
                    SetDataQbit(lined, j, newval);
                else  /* depth == 8 */
                    SetDataByte(lined, j, newval);
            }
        }

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                Colorizing conversion from grayscale to color              *
     *---------------------------------------------------------------------------*/
    // leptonica: pixColorizeGray (pixconv.c)
    /// <summary>Applies a specific color to a grayscale image.</summary>
    /// <param name="color">32 bit rgba pixel</param>
    /// <param name="cmapflag">1 for result to have colormap; 0 for RGB</param>
    /// <returns>pixd 8 bpp colormapped or 32 bpp rgb</returns>
    /// <remarks>
    /// (1) This applies the specific color to the grayscale image.
    /// (2) If pixs already has a colormap, it is removed to gray
    ///     before colorizing.
    /// </remarks>
    public Pix ColorizeGray(uint color, int cmapflag)
    {
        Pix pixs = this;
        int i, j, val8;
        Pix pixt, pixd;

        if (pixs.Depth != 8 && pixs.Colormap == null)
            throw Lept.Arg("pixColorizeGray", "pixs not 8 bpp or cmapped");

        if (pixs.Colormap != null)
            pixt = pixs.RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
        else
            pixt = Clone(pixs);

        PixColormap cmap = PixColormap.GrayToColor(color);
        if (cmapflag != 0) {
            pixd = Copy(null, pixt);
            pixd.SetColormap(cmap);
            return pixd;
        }

            /* Make an RGB pix */
        uint[] tab = cmap.ToRGBTable(out _);
        pixt.GetDimensions(out int w, out int h, out _);
        pixd = Create(w, h, 32);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;
        for (i = 0; i < h; i++) {
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            ReadOnlySpan<uint> linet = datat.AsSpan(i * wplt, wplt);
            for (j = 0; j < w; j++) {
                val8 = GetDataByte(linet, j);
                lined[j] = tab[val8];
            }
        }

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                    Conversion from RGB color to colormap                  *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertRGBToColormap (pixconv.c)
    /// <summary>Quantizes a 32 bpp rgb image to a 2, 4 or 8 bpp image with a colormap.</summary>
    /// <param name="ditherflag">1 to dither, 0 otherwise</param>
    /// <returns>pixd 2, 4 or 8 bpp with colormap</returns>
    /// <remarks>
    /// (1) This function has two relatively simple modes of color
    ///     quantization:
    ///       (a) If the image is made orthographically and has not more
    ///           than 256 'colors' at the level 4 octcube leaves,
    ///           it is quantized nearly exactly.  The ditherflag
    ///           is ignored.
    ///       (b) Most natural images have more than 256 different colors;
    ///           in that case we use adaptive octree quantization,
    ///           with dithering if requested.
    /// (2) If there are not more than 256 occupied level 4 octcubes,
    ///     the color in the colormap that represents all pixels in
    ///     one of those octcubes is given by the first pixel that
    ///     falls into that octcube.
    /// (3) Dithering gives better visual results on images where
    ///     there is a color wash (a slow variation of color), but it
    ///     is about twice as slow and results in significantly larger
    ///     files when losslessly compressed (e.g., into png).
    /// </remarks>
    public Pix ConvertRGBToColormap(int ditherflag)
    {
        Pix pixs = this;

        if (pixs.Depth != 32)
            throw Lept.Arg("pixConvertRGBToColormap", "pixs not 32 bpp");
        if (pixs.Spp == 4)
            Log.Warning("pixConvertRGBToColormap", "pixs has alpha; removing");

            /* Get the histogram and count the number of occupied level 4
             * leaf octcubes.  We don't yet know if this is the number of
             * actual colors, but if it's not, all pixels falling into
             * the same leaf octcube will be assigned to the color of the
             * first pixel that lands there. */
        Numa na = pixs.OctcubeHistogram(4, out int ncolors);

            /* If 256 or fewer occupied leaf octcubes, quantize to those octcubes */
        if (ncolors <= 256) {
            Pix pixd = pixs.FewColorsOctcubeQuant2(4, na, ncolors, out _);
            pixd.CopyInputFormat(pixs);
            return pixd;
        }

            /* There are too many occupied leaf octcubes to be represented
             * directly in a colormap.  Fall back to octree quantization,
             * optionally with dithering. */
        if (ditherflag != 0)
            Log.Info("pixConvertRGBToColormap",
                     "More than 256 colors; using octree quant with dithering");
        else
            Log.Info("pixConvertRGBToColormap",
                     "More than 256 colors; using octree quant; no dithering");
        return pixs.OctreeColorQuant(240, ditherflag);
    }

    /*---------------------------------------------------------------------------*
     *                     Conversion from colormap to 1 bpp                     *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertCmapTo1 (pixconv.c)
    /// <summary>Converts a colormapped image to 1 bpp, deciding which colors are FG and BG.</summary>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// (1) This is an extreme color quantizer.  It decides which
    ///     colors map to FG (black) and which to BG (white).
    /// (2) This uses two heuristics to make the decision:
    ///     (a) colors similar to each other are likely to be in the same class
    ///     (b) there is usually much less FG than BG.
    /// </remarks>
    public Pix ConvertCmapTo1()
    {
        Pix pixs = this;
        int i, j;

        PixColormap? cmap = pixs.Colormap;
        if (cmap == null)
            throw Lept.Arg("pixConvertCmapTo1", "no colormap");

            /* Select target colors for the two classes.  Find the
             * colors with smallest and largest average component values.
             * The smallest is class 0 and the largest is class 1. */
        cmap.GetRangeValues(L_SELECT_AVERAGE, out _, out _, out int imin, out int imax);
        cmap.GetColor(imin, out int rmin, out int gmin, out int bmin);
        cmap.GetColor(imax, out int rmax, out int gmax, out int bmax);
        int nc = cmap.Count;

            /* Assign colors to the two classes.  The histogram is
             * initialized to 0, so any colors not found when computing
             * the sampled histogram will get zero weight in minfract. */
        int[] lut = new int[nc];
        pixs.GetDimensions(out int w, out int h, out _);
        int factor = Math.Max(1, (int)Math.Sqrt((double)(w * h) / 50000.0 + 0.5));
        Numa na1 = pixs.GetCmapHistogram(factor);
        Numa na2 = na1.NormalizeHistogram(1.0f);
        float minfract = 0.0f;
        for (i = 0; i < nc; i++) {
            float ifract = na2.GetFValue(i);
            int dmin = cmap.GetDistanceToColor(i, rmin, gmin, bmin);
            int dmax = cmap.GetDistanceToColor(i, rmax, gmax, bmax);
            if (dmin < dmax) {  /* closer to dark extreme value */
                lut[i] = 1;  /* black pixel in 1 bpp image */
                minfract += ifract;
            }
        }

            /* Generate the output binarized image */
        Pix pix1 = pixs.ConvertTo8(true);
        Pix pixd = Create(w, h, 1);
        uint[] data1 = pix1.Data;
        uint[] datad = pixd.Data;
        int wpl1 = pix1.Wpl;
        int wpld = pixd.Wpl;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl1, wpl1);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                int index = GetDataByte(line1, j);
                if (lut[index] == 1) SetDataBit(lined, j);
            }
        }

            /* We expect minfract (the dark colors) to be less than 0.5.
             * If that is not the case, invert pixd. */
        if (minfract > 0.5) {
            Log.Info("pixConvertCmapTo1", CFormat.Sprintf("minfract = %5.3f; inverting", minfract));
            Invert(pixd, pixd);
        }

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *        Quantization for relatively small number of colors in source       *
     *---------------------------------------------------------------------------*/
    // leptonica: pixQuantizeIfFewColors (pixconv.c)
    /// <summary>Quantizes an 8 bpp gray or 32 bpp rgb image if it has few colors.</summary>
    /// <param name="maxcolors">max number of colors allowed to be returned
    /// from pixColorsForQuantization(); use 0 for default</param>
    /// <param name="mingraycolors">min number of gray levels that a grayscale
    /// image is quantized to; use 0 for default</param>
    /// <param name="octlevel">for octcube quantization: 3 or 4</param>
    /// <returns>2, 4 or 8 bpp quantized; null if pixs can't be quantized into
    /// a small number of colors (too many colors)</returns>
    /// <remarks>
    /// (1) This is a wrapper that tests if the pix can be quantized
    ///     with good quality using a small number of colors.  If so,
    ///     it does the quantization, defining a colormap and using
    ///     pixels whose value is an index into the colormap.
    /// (2) If the image has color, it is quantized with 8 bpp pixels.
    ///     If the image is essentially grayscale, the pixels are
    ///     either 4 or 8 bpp, depending on the size of the required
    ///     colormap.
    /// (3) %octlevel = 4 generates a larger colormap and larger
    ///     compressed image than %octlevel = 3.  If image quality is
    ///     important, you should use %octlevel = 4.
    /// (4) If the image already has a colormap, it returns a clone.
    /// </remarks>
    public Pix? QuantizeIfFewColors(int maxcolors, int mingraycolors, int octlevel)
    {
        Pix pixs = this;
        Pix pixg;
        Pix? pixd;

        int d = pixs.Depth;
        if (d != 8 && d != 32)
            throw Lept.Arg("pixQuantizeIfFewColors", "pixs not defined");
        if (pixs.Colormap != null)
            return Clone(pixs);
        if (maxcolors <= 0)
            maxcolors = 15;  /* default */
        if (maxcolors > 50)
            Log.Warning("pixQuantizeIfFewColors", "maxcolors > 50; very large!");
        if (mingraycolors <= 0)
            mingraycolors = 10;  /* default */
        if (mingraycolors > 30)
            Log.Warning("pixQuantizeIfFewColors", "mingraycolors > 30; very large!");
        if (octlevel != 3 && octlevel != 4) {
            Log.Warning("pixQuantizeIfFewColors", "invalid octlevel; setting to 3");
            octlevel = 3;
        }

            /* Test the number of colors.  For color, the octcube leaves
             * are at level 4. */
        pixs.ColorsForQuantization(0, out int ncolors, out bool iscolor, 0);
        if (ncolors > maxcolors)
            return null;  /* too many colors */

            /* Quantize!
             *  (1) For color:
             *      If octlevel == 4, try to quantize to an octree where
             *      the octcube leaves are at level 4. If that fails,
             *      back off to level 3.
             *      If octlevel == 3, quantize to level 3 directly.
             *      For level 3, the quality is usually good enough and there
             *      is negligible chance of getting more than 256 colors.
             *  (2) For grayscale, multiply ncolors by 1.5 for extra quality,
             *      but use at least mingraycolors and not more than 256. */
        if (iscolor) {
            pixd = pixs.FewColorsOctcubeQuant1(octlevel);
            if (pixd == null) {  /* backoff */
                pixd = pixs.FewColorsOctcubeQuant1(octlevel - 1);
                if (octlevel == 3)  /* shouldn't happen */
                    Log.Warning("pixQuantizeIfFewColors", "quantized at level 2; low quality");
            }
        } else { /* image is really grayscale */
            if (d == 32)
                pixg = pixs.ConvertRGBToLuminance();
            else
                pixg = Clone(pixs);
            int graycolors = Math.Max(mingraycolors, (int)(1.5 * ncolors));
            graycolors = Math.Min(graycolors, 256);
            if (graycolors < 16)
                pixd = pixg.ThresholdTo4bpp(graycolors, true);
            else
                pixd = pixg.ThresholdOn8bpp(graycolors, true);
        }

        if (pixd == null)
            throw Lept.Fail("pixQuantizeIfFewColors", "pixd not made");
        pixd.CopyInputFormat(pixs);
        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                    Conversion from 16 bpp to 8 bpp                        *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvert16To8 (pixconv.c)
    /// <summary>Converts a 16 bpp image to 8 bpp.</summary>
    /// <param name="type">L_LS_BYTE, L_MS_BYTE, L_AUTO_BYTE, L_CLIP_TO_FF</param>
    /// <returns>pixd 8 bpp</returns>
    /// <remarks>
    /// (1) With L_AUTO_BYTE, if the max pixel value is greater than 255,
    ///     use the MSB; otherwise, use the LSB.
    /// (2) With L_CLIP_TO_FF, use min(pixel-value, 0xff) for each
    ///     16-bit src pixel.
    /// </remarks>
    public Pix Convert16To8(int type)
    {
        Pix pixs = this;
        ushort dword;
        int i, j, val;
        uint sword, first, second;

        if (pixs.Depth != 16)
            throw Lept.Arg("pixConvert16To8", "pixs not 16 bpp");
        if (type != L_LS_BYTE && type != L_MS_BYTE &&
            type != L_AUTO_BYTE && type != L_CLIP_TO_FF)
            throw Lept.Arg("pixConvert16To8", "invalid type");

        pixs.GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 8);
        pixd.CopyInputFormat(pixs);
        pixd.CopyResolution(pixs);
        int wpls = pixs.Wpl;
        uint[] datas = pixs.Data;
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;

        if (type == L_AUTO_BYTE) {
                /* NOTE(upstream): only the first wpls 16-bit pixels of each
                 * line are tested, which is about half of the line. */
            bool use_lsb = true;
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                for (j = 0; j < wpls; j++) {
                     val = GetDataTwoBytes(lines, j);
                     if (val > 255) {
                         use_lsb = false;
                         break;
                     }
                }
                if (!use_lsb) break;
            }
            type = (use_lsb) ? L_LS_BYTE : L_MS_BYTE;
        }

            /* Convert 2 pixels at a time */
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            if (type == L_LS_BYTE) {
                for (j = 0; j < wpls; j++) {
                    sword = lines[j];
                    dword = (ushort)(((sword >> 8) & 0xff00) | (sword & 0xff));
                    SetDataTwoBytes(lined, j, dword);
                }
            } else if (type == L_MS_BYTE) {
                for (j = 0; j < wpls; j++) {
                    sword = lines[j];
                    dword = (ushort)(((sword >> 16) & 0xff00) | ((sword >> 8) & 0xff));
                    SetDataTwoBytes(lined, j, dword);
                }
            } else {  /* type == L_CLIP_TO_FF */
                for (j = 0; j < wpls; j++) {
                    sword = lines[j];
                    first = (sword >> 24) != 0 ? 255u : ((sword >> 16) & 0xff);
                    second = ((sword >> 8) & 0xff) != 0 ? 255u : (sword & 0xff);
                    dword = (ushort)((first << 8) | second);
                    SetDataTwoBytes(lined, j, dword);
                }
            }
        }

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                Conversion from grayscale to false color
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertGrayToFalseColor (pixconv.c)
    /// <summary>Converts an 8 or 16 bpp grayscale image to 8 bpp with a false color colormap.</summary>
    /// <param name="gamma">(factor) 0.0 or 1.0 for default; > 1.0 for brighter; 2.0 is quite nice</param>
    /// <returns>pixd 8 bpp with colormap</returns>
    /// <remarks>
    /// (1) For 8 bpp input, this simply adds a colormap to the input image.
    /// (2) For 16 bpp input, it first converts to 8 bpp, using the MSB,
    ///     and then adds the colormap.
    /// (3) The colormap is modeled after the Matlab "jet" configuration.
    /// </remarks>
    public Pix ConvertGrayToFalseColor(float gamma)
    {
        Pix pixs = this;
        Pix pixd;

        int d = pixs.Depth;
        if (d != 8 && d != 16)
            throw Lept.Arg("pixConvertGrayToFalseColor", "pixs not 8 or 16 bpp");

        if (d == 16) {
            pixd = pixs.Convert16To8(L_MS_BYTE);
        } else {  /* d == 8 */
            if (pixs.Colormap != null)
                pixd = pixs.RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
            else
                pixd = Copy(null, pixs);
        }

        PixColormap cmap = PixColormap.GrayToFalseColor(gamma);
        pixd.SetColormap(cmap);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *         Unpacking conversion from 1 bpp to 2, 4, 8, 16 and 32 bpp         *
     *---------------------------------------------------------------------------*/
    // leptonica: pixUnpackBinary (pixconv.c)
    /// <summary>Unpacks a 1 bpp image to 2, 4, 8, 16 or 32 bpp.</summary>
    /// <param name="depth">of destination: 2, 4, 8, 16 or 32 bpp</param>
    /// <param name="invert">0: binary 0 --> grayscale 0, binary 1 --> grayscale 0xff...;
    /// 1: binary 0 --> grayscale 0xff..., binary 1 --> grayscale 0</param>
    /// <returns>pixd 2, 4, 8, 16 or 32 bpp</returns>
    /// <remarks>
    /// (1) This function calls special cases of pixConvert1To*(),
    ///     for 2, 4, 8, 16 and 32 bpp destinations.
    /// </remarks>
    public Pix UnpackBinary(int depth, int invert)
    {
        Pix pixs = this;
        Pix pixd;

        if (pixs.Depth != 1)
            throw Lept.Arg("pixUnpackBinary", "pixs not 1 bpp");
        if (depth != 2 && depth != 4 && depth != 8 && depth != 16 && depth != 32)
            throw Lept.Arg("pixUnpackBinary", "depth not 2, 4, 8, 16 or 32 bpp");

        if (depth == 2) {
            if (invert == 0)
                pixd = Convert1To2(null, pixs, 0, 3);
            else  /* invert bits */
                pixd = Convert1To2(null, pixs, 3, 0);
        } else if (depth == 4) {
            if (invert == 0)
                pixd = Convert1To4(null, pixs, 0, 15);
            else  /* invert bits */
                pixd = Convert1To4(null, pixs, 15, 0);
        } else if (depth == 8) {
            if (invert == 0)
                pixd = Convert1To8(null, pixs, 0, 255);
            else  /* invert bits */
                pixd = Convert1To8(null, pixs, 255, 0);
        } else if (depth == 16) {
            if (invert == 0)
                pixd = Convert1To16(null, pixs, 0, 0xffff);
            else  /* invert bits */
                pixd = Convert1To16(null, pixs, 0xffff, 0);
        } else {
            if (invert == 0)
                pixd = Convert1To32(null, pixs, 0, 0xffffffff);
            else  /* invert bits */
                pixd = Convert1To32(null, pixs, 0xffffffff, 0);
        }

        pixd.CopyInputFormat(pixs);
        return pixd;
    }

    // leptonica: pixConvert1To16 (pixconv.c)
    /// <summary>Unpacks a 1 bpp image to 16 bpp.</summary>
    /// <param name="pixd">[optional] 16 bpp, can be null</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="val0">16 bit value to be used for 0s in pixs</param>
    /// <param name="val1">16 bit value to be used for 1s in pixs</param>
    /// <returns>pixd 16 bpp</returns>
    /// <remarks>
    /// (1) If pixd is null, a new pix is made.
    /// (2) If pixd is not null, it must be of equal width and height
    ///     as pixs.  It is always returned.
    /// </remarks>
    public static Pix Convert1To16(Pix? pixd, Pix pixs, ushort val0, ushort val1)
    {
        int i, j, dibit;
        ushort[] val = new ushort[2];

        if (pixs.Depth != 1)
            throw Lept.Arg("pixConvert1To16", "pixs not 1 bpp");

        pixs.GetDimensions(out int w, out int h, out _);
        if (pixd != null) {
            if (w != pixd.Width || h != pixd.Height)
                throw Lept.Arg("pixConvert1To16", "pix sizes unequal");
            if (pixd.Depth != 16)
                throw Lept.Arg("pixConvert1To16", "pixd not 16 bpp");
        } else {
            pixd = Create(w, h, 16);
        }
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);

            /* Use a table to convert 2 src bits at a time */
        uint[] tab = new uint[4];
        val[0] = val0;
        val[1] = val1;
        for (uint index = 0; index < 4; index++) {
            tab[index] = (uint)((val[(index >> 1) & 1] << 16) | val[index & 1]);
        }

        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        int ndibits = (w + 1) / 2;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < ndibits; j++) {
                dibit = GetDataDibit(lines, j);
                lined[j] = tab[dibit];
            }
        }

        return pixd;
    }

    // leptonica: pixConvert1To32 (pixconv.c)
    /// <summary>Unpacks a 1 bpp image to 32 bpp.</summary>
    /// <param name="pixd">[optional] 32 bpp, can be null</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="val0">32 bit value to be used for 0s in pixs</param>
    /// <param name="val1">32 bit value to be used for 1s in pixs</param>
    /// <returns>pixd 32 bpp</returns>
    /// <remarks>
    /// (1) If pixd is null, a new pix is made.
    /// (2) If pixd is not null, it must be of equal width and height
    ///     as pixs.  It is always returned.
    /// </remarks>
    public static Pix Convert1To32(Pix? pixd, Pix pixs, uint val0, uint val1)
    {
        int i, j, bit;
        uint[] val = new uint[2];

        if (pixs.Depth != 1)
            throw Lept.Arg("pixConvert1To32", "pixs not 1 bpp");

        pixs.GetDimensions(out int w, out int h, out _);
        if (pixd != null) {
            if (w != pixd.Width || h != pixd.Height)
                throw Lept.Arg("pixConvert1To32", "pix sizes unequal");
            if (pixd.Depth != 32)
                throw Lept.Arg("pixConvert1To32", "pixd not 32 bpp");
        } else {
            pixd = Create(w, h, 32);
        }
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);

        val[0] = val0;
        val[1] = val1;
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                bit = GetDataBit(lines, j);
                lined[j] = val[bit];
            }
        }

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                    Conversion from 1 bpp to 2 bpp                         *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvert1To2Cmap (pixconv.c)
    /// <summary>Converts a 1 bpp image to 2 bpp with a colormap.</summary>
    /// <returns>pixd 2 bpp, cmapped</returns>
    /// <remarks>(1) Input 0 is mapped to (255, 255, 255); 1 is mapped to (0, 0, 0)</remarks>
    public Pix Convert1To2Cmap()
    {
        Pix pixs = this;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixConvert1To2Cmap", "pixs not 1 bpp");

        Pix pixd = Convert1To2(null, pixs, 0, 1);
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddColor(255, 255, 255);
        cmap.AddColor(0, 0, 0);
        pixd.SetColormap(cmap);
        pixd.CopyInputFormat(pixs);

        return pixd;
    }

    // leptonica: pixConvert1To2 (pixconv.c)
    /// <summary>Unpacks a 1 bpp image to 2 bpp.</summary>
    /// <param name="pixd">[optional] 2 bpp, can be null</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="val0">2 bit value to be used for 0s in pixs</param>
    /// <param name="val1">2 bit value to be used for 1s in pixs</param>
    /// <returns>pixd 2 bpp</returns>
    /// <remarks>
    /// (1) If pixd is null, a new pix is made.
    /// (2) If pixd is not null, it must be of equal width and height
    ///     as pixs.  It is always returned.
    /// (3) A simple unpacking might use val0 = 0 and val1 = 3.
    /// (4) If you want a colormapped pixd, use pixConvert1To2Cmap().
    /// </remarks>
    public static Pix Convert1To2(Pix? pixd, Pix pixs, int val0, int val1)
    {
        int i, j, byteval;
        byte[] val = new byte[2];

        if (pixs.Depth != 1)
            throw Lept.Arg("pixConvert1To2", "pixs not 1 bpp");

        pixs.GetDimensions(out int w, out int h, out _);
        if (pixd != null) {
            if (w != pixd.Width || h != pixd.Height)
                throw Lept.Arg("pixConvert1To2", "pix sizes unequal");
            if (pixd.Depth != 2)
                throw Lept.Arg("pixConvert1To2", "pixd not 2 bpp");
        } else {
            pixd = Create(w, h, 2);
        }
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);

            /* Use a table to convert 8 src bits to 16 dest bits */
        ushort[] tab = new ushort[256];
        val[0] = (byte)val0;
        val[1] = (byte)val1;
        for (uint index = 0; index < 256; index++) {
            tab[index] = (ushort)((val[(index >> 7) & 1] << 14) |
                                  (val[(index >> 6) & 1] << 12) |
                                  (val[(index >> 5) & 1] << 10) |
                                  (val[(index >> 4) & 1] << 8) |
                                  (val[(index >> 3) & 1] << 6) |
                                  (val[(index >> 2) & 1] << 4) |
                                  (val[(index >> 1) & 1] << 2) | val[index & 1]);
        }

        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        int nbytes = (w + 7) / 8;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < nbytes; j++) {
                byteval = GetDataByte(lines, j);
                SetDataTwoBytes(lined, j, tab[byteval]);
            }
        }

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                    Conversion from 1 bpp to 4 bpp                         *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvert1To4Cmap (pixconv.c)
    /// <summary>Converts a 1 bpp image to 4 bpp with a colormap.</summary>
    /// <returns>pixd 4 bpp, cmapped</returns>
    /// <remarks>(1) Input 0 is mapped to (255, 255, 255); 1 is mapped to (0, 0, 0)</remarks>
    public Pix Convert1To4Cmap()
    {
        Pix pixs = this;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixConvert1To4Cmap", "pixs not 1 bpp");

        Pix pixd = Convert1To4(null, pixs, 0, 1);
        PixColormap cmap = PixColormap.Create(4);
        cmap.AddColor(255, 255, 255);
        cmap.AddColor(0, 0, 0);
        pixd.SetColormap(cmap);
        pixd.CopyInputFormat(pixs);

        return pixd;
    }

    // leptonica: pixConvert1To4 (pixconv.c)
    /// <summary>Unpacks a 1 bpp image to 4 bpp.</summary>
    /// <param name="pixd">[optional] 4 bpp, can be null</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="val0">4 bit value to be used for 0s in pixs</param>
    /// <param name="val1">4 bit value to be used for 1s in pixs</param>
    /// <returns>pixd 4 bpp</returns>
    /// <remarks>
    /// (1) If pixd is null, a new pix is made.
    /// (2) If pixd is not null, it must be of equal width and height
    ///     as pixs.  It is always returned.
    /// (3) A simple unpacking might use val0 = 0 and val1 = 15, or v.v.
    /// (4) If you want a colormapped pixd, use pixConvert1To4Cmap().
    /// </remarks>
    public static Pix Convert1To4(Pix? pixd, Pix pixs, int val0, int val1)
    {
        int i, j, byteval;
        byte[] val = new byte[2];

        if (pixs.Depth != 1)
            throw Lept.Arg("pixConvert1To4", "pixs not 1 bpp");

        pixs.GetDimensions(out int w, out int h, out _);
        if (pixd != null) {
            if (w != pixd.Width || h != pixd.Height)
                throw Lept.Arg("pixConvert1To4", "pix sizes unequal");
            if (pixd.Depth != 4)
                throw Lept.Arg("pixConvert1To4", "pixd not 4 bpp");
        } else {
            pixd = Create(w, h, 4);
        }
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);

            /* Use a table to convert 8 src bits to 32 bit dest word */
        uint[] tab = new uint[256];
        val[0] = (byte)val0;
        val[1] = (byte)val1;
        for (uint index = 0; index < 256; index++) {
            tab[index] = (uint)((val[(index >> 7) & 1] << 28) |
                                (val[(index >> 6) & 1] << 24) |
                                (val[(index >> 5) & 1] << 20) |
                                (val[(index >> 4) & 1] << 16) |
                                (val[(index >> 3) & 1] << 12) |
                                (val[(index >> 2) & 1] << 8) |
                                (val[(index >> 1) & 1] << 4) | val[index & 1]);
        }

        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        int nbytes = (w + 7) / 8;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < nbytes; j++) {
                byteval = GetDataByte(lines, j);
                lined[j] = tab[byteval];
            }
        }

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *               Conversion from 1, 2 and 4 bpp to 8 bpp                     *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvert1To8Cmap (pixconv.c)
    /// <summary>Converts a 1 bpp image to 8 bpp with a colormap.</summary>
    /// <returns>pixd 8 bpp, cmapped</returns>
    /// <remarks>(1) Input 0 is mapped to (255, 255, 255); 1 is mapped to (0, 0, 0)</remarks>
    public Pix Convert1To8Cmap()
    {
        Pix pixs = this;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixConvert1To8Cmap", "pixs not 1 bpp");

        Pix pixd = Convert1To8(null, pixs, 0, 1);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(255, 255, 255);
        cmap.AddColor(0, 0, 0);
        pixd.SetColormap(cmap);
        pixd.CopyInputFormat(pixs);
        return pixd;
    }

    // leptonica: pixConvert1To8 (pixconv.c)
    /// <summary>Unpacks a 1 bpp image to 8 bpp.</summary>
    /// <param name="pixd">[optional] 8 bpp, can be null</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="val0">8 bit value to be used for 0s in pixs</param>
    /// <param name="val1">8 bit value to be used for 1s in pixs</param>
    /// <returns>pixd 8 bpp</returns>
    /// <remarks>
    /// (1) If pixd is null, a new pix is made.
    /// (2) If pixd is not null, it must be of equal width and height
    ///     as pixs.  It is always returned.
    /// (3) A simple unpacking might use val0 = 0 and val1 = 255, or v.v.
    /// (4) To have a colormap associated with the 8 bpp pixd,
    ///     use pixConvert1To8Cmap().
    /// </remarks>
    public static Pix Convert1To8(Pix? pixd, Pix pixs, byte val0, byte val1)
    {
        if (pixs.Depth != 1)
            throw Lept.Arg("pixConvert1To8", "pixs not 1 bpp");

        pixs.GetDimensions(out int w, out int h, out _);
        if (pixd != null) {
            if (w != pixd.Width || h != pixd.Height)
                throw Lept.Arg("pixConvert1To8", "pix sizes unequal");
            if (pixd.Depth != 8)
                throw Lept.Arg("pixConvert1To8", "pixd not 8 bpp");
        } else {
            pixd = CreateNoInit(w, h, 8);
        }
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        pixs.SetPadBits(0);

            /* Use a stack-allocated table to convert 4 src bits at a time */
        Span<uint> tab = stackalloc uint[16];
        Span<byte> val = stackalloc byte[2] { val0, val1 };
        for (uint index = 0; index < 16; index++) {
            tab[(int)index] = ((uint)val[(int)((index >> 3) & 1)] << 24) |
                              ((uint)val[(int)((index >> 2) & 1)] << 16) |
                              ((uint)val[(int)((index >> 1) & 1)] << 8) |
                              val[(int)(index & 1)];
        }

        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        int nqbits = (w + 3) >> 2;
        int fullSrcWords = nqbits >> 3;
        Vector256<uint> vZeroWord = Vector256.Create(tab[0]);
        ref uint tabRef = ref MemoryMarshal.GetReference(tab);

        for (int i = 0; i < h; i++) {
            ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
            ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
            int jd = 0;
            for (int jw = 0; jw < fullSrcWords; jw++, jd += 8) {
                uint sw = Unsafe.Add(ref rs, (nuint)jw);
                if (sw == 0 && Vector256.IsHardwareAccelerated) {
                    vZeroWord.StoreUnsafe(ref rd, (nuint)jd);
                } else {
                    Unsafe.Add(ref rd, (nuint)(jd + 0)) = Unsafe.Add(ref tabRef, (nuint)(sw >> 28));
                    Unsafe.Add(ref rd, (nuint)(jd + 1)) = Unsafe.Add(ref tabRef, (nuint)((sw >> 24) & 0xf));
                    Unsafe.Add(ref rd, (nuint)(jd + 2)) = Unsafe.Add(ref tabRef, (nuint)((sw >> 20) & 0xf));
                    Unsafe.Add(ref rd, (nuint)(jd + 3)) = Unsafe.Add(ref tabRef, (nuint)((sw >> 16) & 0xf));
                    Unsafe.Add(ref rd, (nuint)(jd + 4)) = Unsafe.Add(ref tabRef, (nuint)((sw >> 12) & 0xf));
                    Unsafe.Add(ref rd, (nuint)(jd + 5)) = Unsafe.Add(ref tabRef, (nuint)((sw >> 8) & 0xf));
                    Unsafe.Add(ref rd, (nuint)(jd + 6)) = Unsafe.Add(ref tabRef, (nuint)((sw >> 4) & 0xf));
                    Unsafe.Add(ref rd, (nuint)(jd + 7)) = Unsafe.Add(ref tabRef, (nuint)(sw & 0xf));
                }
            }
            for (; jd < nqbits; jd++)
                Unsafe.Add(ref rd, (nuint)jd) = Unsafe.Add(ref tabRef, (nuint)GetDataQbitUnchecked(ref rs, jd));
        }

        if ((w & 3) != 0)
            pixd.SetPadBits(0);

        return pixd;
    }

    // leptonica: pixConvert2To8 (pixconv.c)
    /// <summary>Unpacks a 2 bpp image to 8 bpp, with or without a colormap.</summary>
    /// <param name="val0">8 bit value to be used for 00 in pixs</param>
    /// <param name="val1">8 bit value to be used for 01 in pixs</param>
    /// <param name="val2">8 bit value to be used for 10 in pixs</param>
    /// <param name="val3">8 bit value to be used for 11 in pixs</param>
    /// <param name="cmapflag">true if pixd is to have a colormap; false otherwise</param>
    /// <returns>pixd 8 bpp</returns>
    /// <remarks>
    /// ~ A simple unpacking might use val0 = 0,
    ///   val1 = 85 (0x55), val2 = 170 (0xaa), val3 = 255.
    /// ~ If cmapflag is TRUE:
    ///     ~ The 8 bpp image is made with a colormap.
    ///     ~ If pixs has a colormap, the input values are ignored and
    ///       the 8 bpp image is made using the colormap
    ///     ~ If pixs does not have a colormap, the input values are
    ///       used to build the colormap.
    /// ~ If cmapflag is FALSE:
    ///     ~ The 8 bpp image is made without a colormap.
    ///     ~ If pixs has a colormap, the input values are ignored,
    ///       the colormap is removed, and the values stored in the 8 bpp
    ///       image are from the colormap.
    ///     ~ If pixs does not have a colormap, the input values are
    ///       used to populate the 8 bpp image.
    /// </remarks>
    public Pix Convert2To8(byte val0, byte val1, byte val2, byte val3, bool cmapflag)
    {
        Pix pixs = this;
        int i, j, dibit;
        uint[] val = new uint[4];
        PixColormap cmapd;

        if (pixs.Depth != 2)
            throw Lept.Arg("pixConvert2To8", "pixs not 2 bpp");

        PixColormap? cmaps = pixs.Colormap;
        if (cmaps != null && cmapflag == false)
            return pixs.RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);

        pixs.GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 8);
        pixs.SetPadBits(0);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        if (cmapflag == true) {  /* pixd will have a colormap */
            if (cmaps != null) {  /* use the existing colormap from pixs */
                cmapd = cmaps.ConvertTo8();
            } else {  /* make a colormap from the input values */
                cmapd = PixColormap.Create(8);
                cmapd.AddColor(val0, val0, val0);
                cmapd.AddColor(val1, val1, val1);
                cmapd.AddColor(val2, val2, val2);
                cmapd.AddColor(val3, val3, val3);
            }
            pixd.SetColormap(cmapd);
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (j = 0; j < w; j++) {
                    dibit = GetDataDibit(lines, j);
                    SetDataByte(lined, j, dibit);
                }
            }
            return pixd;
        }

            /* Last case: no colormap in either pixs or pixd.
             * Use input values and build a table to convert 1 src byte
             * (4 src pixels) at a time */
        uint[] tab = new uint[256];
        val[0] = val0;
        val[1] = val1;
        val[2] = val2;
        val[3] = val3;
        for (uint index = 0; index < 256; index++) {
            tab[index] = (val[(index >> 6) & 3] << 24) |
                         (val[(index >> 4) & 3] << 16) |
                         (val[(index >> 2) & 3] << 8) | val[index & 3];
        }

        int nbytes = (w + 3) / 4;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < nbytes; j++) {
                int b = GetDataByte(lines, j);
                lined[j] = tab[b];
            }
        }

        return pixd;
    }

    // leptonica: pixConvert4To8 (pixconv.c)
    /// <summary>Unpacks a 4 bpp image to 8 bpp, with or without a colormap.</summary>
    /// <param name="cmapflag">true if pixd is to have a colormap; false otherwise</param>
    /// <returns>pixd 8 bpp</returns>
    /// <remarks>
    /// ~ If cmapflag is TRUE:
    ///     ~ pixd is made with a colormap.
    ///     ~ If pixs has a colormap, it is copied and the colormap
    ///       index values are placed in pixd.
    ///     ~ If pixs does not have a colormap, a colormap with linear
    ///       trc is built and the pixel values in pixs are placed in
    ///       pixd as colormap index values.
    /// ~ If cmapflag is FALSE:
    ///     ~ pixd is made without a colormap.
    ///     ~ If pixs has a colormap, it is removed and the values stored
    ///       in pixd are from the colormap (converted to gray).
    ///     ~ If pixs does not have a colormap, the pixel values in pixs
    ///       are used, with shift replication, to populate pixd.
    /// </remarks>
    public Pix Convert4To8(bool cmapflag)
    {
        Pix pixs = this;
        int i, j, qbit;
        PixColormap cmapd;

        if (pixs.Depth != 4)
            throw Lept.Arg("pixConvert4To8", "pixs not 4 bpp");

        PixColormap? cmaps = pixs.Colormap;
        if (cmaps != null && cmapflag == false)
            return pixs.RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);

        pixs.GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 8);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        if (cmapflag == true) {  /* pixd will have a colormap */
            if (cmaps != null) {  /* use the existing colormap from pixs */
                cmapd = cmaps.ConvertTo8();
            } else {  /* make a colormap with a linear trc */
                cmapd = PixColormap.Create(8);
                for (i = 0; i < 16; i++)
                    cmapd.AddColor(17 * i, 17 * i, 17 * i);
            }
            pixd.SetColormap(cmapd);
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (j = 0; j < w; j++) {
                    qbit = GetDataQbit(lines, j);
                    SetDataByte(lined, j, qbit);
                }
            }
            return pixd;
        }

            /* Last case: no colormap in either pixs or pixd.
             * Replicate the qbit value into 8 bits. */
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                qbit = GetDataQbit(lines, j);
                int b = (qbit << 4) | qbit;
                SetDataByte(lined, j, b);
            }
        }
        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *               Unpacking conversion from 8 bpp to 16 bpp                   *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvert8To16 (pixconv.c)
    /// <summary>Unpacks an 8 bpp image to 16 bpp, with a left shift.</summary>
    /// <param name="leftshift">number of bits: 0 is no shift; 8 replicates in MSB and LSB of dest</param>
    /// <returns>pixd 16 bpp</returns>
    /// <remarks>
    /// (1) For left shift of 8, the 8 bit value is replicated in both
    ///     the MSB and the LSB of the pixels in pixd.  That way, we get
    ///     proportional mapping, with a correct map from 8 bpp white
    ///     (0xff) to 16 bpp white (0xffff).
    /// </remarks>
    public Pix Convert8To16(int leftshift)
    {
        Pix pixs = this;
        int i, j, val;
        Pix pixt;

        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg("pixConvert8To16", "pixs not 8 bpp");
        if (leftshift < 0 || leftshift > 8)
            throw Lept.Arg("pixConvert8To16", "leftshift not in [0 ... 8]");

        if (pixs.Colormap != null)
            pixt = pixs.RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
        else
            pixt = Clone(pixs);

        Pix pixd = Create(w, h, 16);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        uint[] datat = pixt.Data;
        uint[] datad = pixd.Data;
        int wplt = pixt.Wpl;
        int wpld = pixd.Wpl;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> linet = datat.AsSpan(i * wplt, wplt);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                val = GetDataByte(linet, j);
                if (leftshift == 8)
                    val = val | (val << leftshift);
                else
                    val <<= leftshift;
                SetDataTwoBytes(lined, j, val);
            }
        }

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                     Top-level conversion to 2 bpp                         *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertTo2 (pixconv.c)
    /// <summary>Converts a 1, 2, 4, 8, 24 or 32 bpp image to 2 bpp gray; a colormap is allowed but is removed.</summary>
    /// <returns>A new 2 bpp image.</returns>
    /// <remarks>
    /// <para>This is a top-level function, with simple default values used in <see cref="ConvertTo8"/> if unpacking is necessary.</para>
    /// <para>Any existing colormap is removed; the result is always gray.</para>
    /// <para>If the input image has 2 bpp and no colormap, the operation is lossless and a copy is returned.</para>
    /// </remarks>
    public Pix ConvertTo2()
    {
        Pix pixs = this;
        Pix pix1, pix2, pix3, pixd;

        int d = pixs.Depth;
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 24 && d != 32)
            throw Lept.Arg("pixConvertTo2", "depth not {1,2,4,8,24,32}");

        if (pixs.Colormap != null) {
            pix1 = pixs.RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
            d = pix1.Depth;
        } else {
            pix1 = Copy(null, pixs);
        }
        if (d == 24 || d == 32)
            pix2 = pix1.ConvertTo8(false);
        else
            pix2 = Clone(pix1);
        if (d == 1) {
            pixd = Convert1To2(null, pix2, 3, 0);
        } else if (d == 2) {
            pixd = Clone(pix2);
        } else if (d == 4) {
            pix3 = pix2.Convert4To8(false);  /* unpack to 8 */
            pixd = pix3.Convert8To2();
        } else {  /* d == 8 */
            pixd = pix2.Convert8To2();
        }
        return pixd;
    }

    // leptonica: pixConvert8To2 (pixconv.c)
    /// <summary>Converts an 8 bpp image to 2 bpp, taking the top 2 bits of each pixel.</summary>
    /// <remarks>(1) Any existing colormap is removed to gray.</remarks>
    public Pix Convert8To2()
    {
        Pix pix = this;
        int i, j;
        uint word;
        Pix pixs;

        if (pix.Depth != 8)
            throw Lept.Arg("pixConvert8To2", "pix undefined or not 8 bpp");

        if (pix.Colormap != null)
            pixs = pix.RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
        else
            pixs = Clone(pix);
        pixs.GetDimensions(out int w, out int h, out _);
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        Pix pixd = Create(w, h, 2);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < wpls; j++) {  /* march through 4 pixels at a time */
                word = lines[j] & 0xc0c0c0c0;  /* top 2 bits of each byte */
                word = (word >> 24) | ((word & 0xff0000) >> 18) |
                       ((word & 0xff00) >> 12) | ((word & 0xff) >> 6);
                SetDataByte(lined, j, (int)word);  /* only LS byte is filled */
            }
        }
        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                     Top-level conversion to 4 bpp                         *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertTo4 (pixconv.c)
    /// <summary>Converts a 1, 2, 4, 8, 24 or 32 bpp image to 4 bpp gray; a colormap is allowed but is removed.</summary>
    /// <returns>A new 4 bpp image.</returns>
    /// <remarks>
    /// <para>This is a top-level function, with simple default values used in <see cref="ConvertTo8"/> if unpacking is necessary.</para>
    /// <para>Any existing colormap is removed; the result is always gray.</para>
    /// <para>If the input image has 4 bpp and no colormap, the operation is lossless and a copy is returned.</para>
    /// </remarks>
    public Pix ConvertTo4()
    {
        Pix pixs = this;
        Pix pix1, pix2, pix3, pixd;

        int d = pixs.Depth;
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 24 && d != 32)
            throw Lept.Arg("pixConvertTo4", "depth not {1,2,4,8,24,32}");

        if (pixs.Colormap != null) {
            pix1 = pixs.RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
            d = pix1.Depth;
        } else {
            pix1 = Copy(null, pixs);
        }
        if (d == 24 || d == 32)
            pix2 = pix1.ConvertTo8(false);
        else
            pix2 = Clone(pix1);
        if (d == 1) {
            pixd = Convert1To4(null, pix2, 15, 0);
        } else if (d == 2) {
            pix3 = pix2.Convert2To8(0, 0x55, 0xaa, 0xff, false);
            pixd = pix3.Convert8To4();
        } else if (d == 4) {
            pixd = Clone(pix2);
        } else {  /* d == 8 */
            pixd = pix2.Convert8To4();
        }
        return pixd;
    }

    // leptonica: pixConvert8To4 (pixconv.c)
    /// <summary>Converts an 8 bpp image to 4 bpp, taking the top 4 bits of each pixel.</summary>
    /// <remarks>(1) Any existing colormap is removed to gray.</remarks>
    public Pix Convert8To4()
    {
        Pix pix = this;
        int i, j, val;
        Pix pixs;

        if (pix.Depth != 8)
            throw Lept.Arg("pixConvert8To4", "pix undefined or not 8 bpp");

        if (pix.Colormap != null)
            pixs = pix.RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
        else
            pixs = Clone(pix);
        pixs.GetDimensions(out int w, out int h, out _);
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        Pix pixd = Create(w, h, 4);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                val = GetDataByte(lines, j);
                val = val >> 4;  /* take top 4 bits */
                SetDataQbit(lined, j, val);
            }
        }
        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                     Top-level conversion to 1 bpp                         *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertTo1Adaptive (pixconv.c)
    /// <summary>Converts a 1, 2, 4, 8, 16, 24 or 32 bpp image to 1 bpp, using adaptive thresholding if necessary.</summary>
    /// <returns>A new 1 bpp image.</returns>
    /// <remarks>
    /// <para>This is a top-level function that uses default values for adaptive thresholding, if necessary.
    /// Otherwise, it is the same as <see cref="ConvertTo1"/>, which uses a global threshold for binarization.</para>
    /// <para>Other high-level adaptive thresholding functions are <c>AdaptThresholdToBinary</c> and <c>CleanImage</c>.</para>
    /// </remarks>
    public Pix ConvertTo1Adaptive()
    {
        Pix pixs = this;
        int color0, color1;
        Pix pix1, pix2, pixd;

        int d = pixs.Depth;
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 24 && d != 32)
            throw Lept.Arg("pixConvertTo1Adaptive", "depth not {1,2,4,8,16,24,32}");

        PixColormap? cmap = pixs.Colormap;
        if (d == 1) {
            if (cmap == null) {
                return Copy(null, pixs);
            } else {  /* strip the colormap off, and invert if reasonable
                       for standard binary photometry.  */
                cmap.GetColor(0, out int rval, out int gval, out int bval);
                color0 = rval + gval + bval;
                cmap.GetColor(1, out rval, out gval, out bval);
                color1 = rval + gval + bval;
                pixd = Copy(null, pixs);
                pixd.DestroyColormap();
                if (color1 > color0)
                    Invert(pixd, pixd);
                return pixd;
            }
        }

            /* For all other depths, use 8 bpp as an intermediary */
        pix1 = pixs.ConvertTo8(false);
        pix2 = pix1.BackgroundNormSimple(null, null);
        pixd = pix2.ThresholdToBinary(180);
        return pixd;
    }

    // leptonica: pixConvertTo1 (pixconv.c)
    /// <summary>Converts a 1, 2, 4, 8, 16, 24 or 32 bpp image to 1 bpp using a global threshold.</summary>
    /// <param name="threshold">Threshold for the final binarization, relative to 8 bpp.</param>
    /// <returns>A new 1 bpp image.</returns>
    /// <remarks>
    /// <para>This is a top-level function, with simple default values used in <see cref="ConvertTo8"/> if unpacking is necessary.</para>
    /// <para>Any existing colormap is removed.</para>
    /// <para>If the input image has 1 bpp and no colormap, the operation is lossless and a copy is returned.</para>
    /// </remarks>
    public Pix ConvertTo1(int threshold)
    {
        Pix pixs = this;
        int color0, color1;
        Pix pixg, pixd;

        int d = pixs.Depth;
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 24 && d != 32)
            throw Lept.Arg("pixConvertTo1", "depth not {1,2,4,8,16,24,32}");

        PixColormap? cmap = pixs.Colormap;
        if (d == 1) {
            if (cmap == null) {
                return Copy(null, pixs);
            } else {  /* strip the colormap off, and invert if reasonable
                       for standard binary photometry.  */
                cmap.GetColor(0, out int rval, out int gval, out int bval);
                color0 = rval + gval + bval;
                cmap.GetColor(1, out rval, out gval, out bval);
                color1 = rval + gval + bval;
                pixd = Copy(null, pixs);
                pixd.DestroyColormap();
                if (color1 > color0)
                    Invert(pixd, pixd);
                return pixd;
            }
        }

            /* For all other depths, use 8 bpp as an intermediary */
        pixg = pixs.ConvertTo8(false);
        pixd = pixg.ThresholdToBinary(threshold);
        return pixd;
    }

    // leptonica: pixConvertTo1BySampling (pixconv.c)
    /// <summary>Converts a 1, 2, 4, 8, 16, 24 or 32 bpp image to 1 bpp quickly, using subsampling.</summary>
    /// <param name="factor">Subsampling factor; an integer greater than or equal to 1.</param>
    /// <param name="threshold">Threshold for the final binarization, relative to 8 bpp.</param>
    /// <returns>A new 1 bpp image.</returns>
    /// <exception cref="ArgumentException"><paramref name="factor"/> is less than 1.</exception>
    /// <remarks>
    /// This is a quick and dirty, top-level converter. See <see cref="ConvertTo1"/> for default values.
    /// </remarks>
    public Pix ConvertTo1BySampling(int factor, int threshold)
    {
        Pix pixs = this;

        if (factor < 1)
            throw Lept.Arg("pixConvertTo1BySampling", "factor must be >= 1");

        float scalefactor = 1.0f / (float)factor;
        Pix pixt = pixs.ScaleBySampling(scalefactor, scalefactor);
        Pix pixd = pixt.ConvertTo1(threshold);
        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                     Top-level conversion to 8 bpp                         *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertTo8 (pixconv.c)
    /// <summary>Converts to 8 bpp, with or without a colormap.</summary>
    /// <param name="cmapflag">true if pixd is to have a colormap; false otherwise</param>
    /// <returns>pixd 8 bpp</returns>
    /// <remarks>
    /// (1) This is a top-level function, with simple default values
    ///     for unpacking.
    /// (2) The result, pixd, is made with a colormap if specified.
    ///     It is always a new image -- never a clone.  For example,
    ///     if d == 8, and cmapflag matches the existence of a cmap
    ///     in pixs, the operation is lossless and it returns a copy.
    /// (3) The default values used are:
    ///     ~ 1 bpp: val0 = 255, val1 = 0
    ///     ~ 2 bpp: 4 bpp:  even increments over dynamic range
    ///     ~ 8 bpp: lossless if cmap matches cmapflag
    ///     ~ 16 bpp: use most significant byte
    /// (4) If 24 bpp or 32 bpp RGB, this is converted to gray.
    ///     For color quantization, you must specify the type explicitly,
    ///     using the color quantization code.
    /// </remarks>
    public Pix ConvertTo8(bool cmapflag)
    {
        Pix pixs = this;
        Pix pix1, pixd;

        int d = pixs.Depth;
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 24 && d != 32)
            throw Lept.Arg("pixConvertTo8", "depth not {1,2,4,8,16,24,32}");

        if (d == 1) {
            if (cmapflag)
                return pixs.Convert1To8Cmap();
            else
                return Convert1To8(null, pixs, 255, 0);
        } else if (d == 2) {
            return pixs.Convert2To8(0, 85, 170, 255, cmapflag);
        } else if (d == 4) {
            return pixs.Convert4To8(cmapflag);
        } else if (d == 8) {
            PixColormap? cmap = pixs.Colormap;
            if ((cmap != null && cmapflag) || (cmap == null && !cmapflag)) {
                return Copy(null, pixs);
            } else if (cmap != null) {  /* !cmapflag */
                return pixs.RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
            } else {  /* !cmap && cmapflag; add colormap to pixd */
                pixd = Copy(null, pixs);
                pixd.AddGrayColormap8();
                return pixd;
            }
        } else if (d == 16) {
            pixd = pixs.Convert16To8(L_MS_BYTE);
            if (cmapflag)
                pixd.AddGrayColormap8();
            return pixd;
        } else if (d == 24) {
            pix1 = pixs.Convert24To32();
            pixd = pix1.ConvertRGBToLuminance();
            if (cmapflag)
                pixd.AddGrayColormap8();
            return pixd;
        } else { /* d == 32 */
            pixd = pixs.ConvertRGBToLuminance();
            if (cmapflag)
                pixd.AddGrayColormap8();
            return pixd;
        }
    }

    // leptonica: pixConvertTo8BySampling (pixconv.c)
    /// <summary>Quick and dirty conversion to 8 bpp, with subsampling.</summary>
    /// <param name="factor">submsampling factor; integer >= 1</param>
    /// <param name="cmapflag">1 (TRUE) if pixd is to have a colormap; 0 (FALSE) otherwise</param>
    /// <returns>pixd 8 bpp</returns>
    /// <remarks>
    /// (1) This is a fast, quick/dirty, top-level converter.
    /// (2) See pixConvertTo8() for default values.
    /// </remarks>
    public Pix ConvertTo8BySampling(int factor, int cmapflag)
    {
        Pix pixs = this;

        if (factor < 1)
            throw Lept.Arg("pixConvertTo8BySampling", "factor must be >= 1");

        float scalefactor = 1.0f / (float)factor;
        Pix pixt = pixs.ScaleBySampling(scalefactor, scalefactor);
        Pix pixd = pixt.ConvertTo8(cmapflag != 0);
        return pixd;
    }

    // leptonica: pixConvertTo8Colormap (pixconv.c)
    /// <summary>Converts to 8 bpp with a colormap.</summary>
    /// <param name="dither">1 to dither if necessary; 0 otherwise</param>
    /// <returns>pixd 8 bpp, cmapped</returns>
    /// <remarks>
    /// (1) This is a top-level function, with simple default values
    ///     for unpacking.
    /// (2) The result, pixd, is always made with a colormap.
    /// (3) If d == 8, the operation is lossless and it returns a copy.
    /// (4) The default values used for increasing depth are:
    ///     ~ 1 bpp: val0 = 255, val1 = 0
    ///     ~ 2 bpp: 4 bpp:  even increments over dynamic range
    /// (5) For 16 bpp, use the most significant byte.
    /// (6) For 32 bpp RGB, use octcube quantization with optional dithering.
    /// </remarks>
    public Pix ConvertTo8Colormap(int dither)
    {
        Pix pixs = this;

        int d = pixs.Depth;
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixConvertTo8Colormap", "depth not {1,2,4,8,16,32}");

        if (d != 32)
            return pixs.ConvertTo8(true);

        return pixs.ConvertRGBToColormap(dither);
    }

    /*---------------------------------------------------------------------------*
     *                    Top-level conversion to 16 bpp                         *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertTo16 (pixconv.c)
    /// <summary>Converts a 1 or 8 bpp image to 16 bpp.</summary>
    /// <returns>A new 16 bpp image.</returns>
    /// <exception cref="ArgumentException">The depth is not 1 or 8 bpp.</exception>
    /// <remarks>
    /// This is a top-level function, with simple default values for unpacking:
    /// <list type="bullet">
    /// <item><description>1 bpp: value 0 maps to 0xffff and value 1 maps to 0.</description></item>
    /// <item><description>8 bpp: the 8-bit value is replicated in both the most and least significant bytes of the 16-bit pixel.</description></item>
    /// </list>
    /// </remarks>
    public Pix ConvertTo16()
    {
        Pix pixs = this;

        int d = pixs.Depth;
        if (d == 1)
            return Convert1To16(null, pixs, 0xffff, 0);
        else if (d == 8)
            return pixs.Convert8To16(8);
        else
            throw Lept.Arg("pixConvertTo16", "src depth not 1 or 8 bpp");
    }

    /*---------------------------------------------------------------------------*
     *                    Top-level conversion to 32 bpp                         *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertTo32 (pixconv.c)
    /// <summary>Converts a 1, 2, 4, 8, 16, 24 or 32 bpp image to 32 bpp rgb.</summary>
    /// <returns>A new 32 bpp image; this never returns a clone of the source.</returns>
    /// <remarks>
    /// This is a top-level function, with simple default values for unpacking:
    /// <list type="bullet">
    /// <item><description>1 bpp: value 0 maps to 255 and value 1 maps to 0, replicated into the red, green and blue components.</description></item>
    /// <item><description>2 bpp: if colormapped, the colormap values are used; otherwise the values 0, 0x55, 0xaa and 255 are replicated into the red, green and blue components.</description></item>
    /// <item><description>4 bpp: if colormapped, the colormap values are used; otherwise two nybbles are replicated into a byte, and then into the red, green and blue components.</description></item>
    /// <item><description>8 bpp: if colormapped, the colormap values are used; otherwise the gray values are replicated into the red, green and blue components.</description></item>
    /// <item><description>16 bpp: the most significant byte is replicated into the red, green and blue components.</description></item>
    /// <item><description>24 bpp: the pixels are unpacked, maintaining word alignment on each scanline.</description></item>
    /// <item><description>32 bpp: a copy is made.</description></item>
    /// </list>
    /// </remarks>
    public Pix ConvertTo32()
    {
        Pix pixs = this;
        Pix pix1, pixd;

        int d = pixs.Depth;
        if (d == 1) {
            return Convert1To32(null, pixs, 0xffffffff, 0);
        } else if (d == 2) {
            pix1 = pixs.Convert2To8(0, 85, 170, 255, true);
            pixd = pix1.Convert8To32();
            return pixd;
        } else if (d == 4) {
            pix1 = pixs.Convert4To8(true);
            pixd = pix1.Convert8To32();
            return pixd;
        } else if (d == 8) {
            return pixs.Convert8To32();
        } else if (d == 16) {
            pix1 = pixs.Convert16To8(L_MS_BYTE);
            pixd = pix1.Convert8To32();
            return pixd;
        } else if (d == 24) {
            return pixs.Convert24To32();
        } else if (d == 32) {
            return Copy(null, pixs);
        } else {
            throw Lept.Arg("pixConvertTo32", "depth not 1, 2, 4, 8, 16, 32 bpp");
        }
    }

    // leptonica: pixConvertTo32BySampling (pixconv.c)
    /// <summary>Quick and dirty conversion to 32 bpp, with subsampling.</summary>
    /// <param name="factor">submsampling factor; integer >= 1</param>
    /// <returns>pixd 32 bpp</returns>
    /// <remarks>
    /// (1) This is a fast, quick/dirty, top-level converter.
    /// (2) See pixConvertTo32() for default values.
    /// </remarks>
    public Pix ConvertTo32BySampling(int factor)
    {
        Pix pixs = this;

        if (factor < 1)
            throw Lept.Arg("pixConvertTo32BySampling", "factor must be >= 1");

        float scalefactor = 1.0f / (float)factor;
        Pix pix1 = pixs.ScaleBySampling(scalefactor, scalefactor);
        Pix pixd = pix1.ConvertTo32();
        return pixd;
    }

    // leptonica: pixConvert8To32 (pixconv.c)
    /// <summary>Converts an 8 bpp image to 32 bpp rgb.</summary>
    /// <returns>32 bpp rgb pix</returns>
    /// <remarks>
    /// (1) If there is no colormap, replicates the gray value
    ///     into the 3 MSB of the dest pixel.
    /// </remarks>
    public Pix Convert8To32()
    {
        Pix pixs = this;
        if (pixs.Depth != 8)
            throw Lept.Arg("pixConvert8To32", "pixs not 8 bpp");

        if (pixs.Colormap != null)
            return pixs.RemoveColormap(REMOVE_CMAP_TO_FULL_COLOR);

        pixs.GetDimensions(out int w, out int h, out _);
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        Pix pixd = CreateRawNoInit(w, h, 32);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        Vector128<byte> maskLo = Vector128.Create(
            (byte)0x80, 3, 3, 3,
            0x80, 2, 2, 2,
            0x80, 1, 1, 1,
            0x80, 0, 0, 0);
        Vector128<byte> maskHi = Vector128.Create(
            (byte)0x80, 7, 7, 7,
            0x80, 6, 6, 6,
            0x80, 5, 5, 5,
            0x80, 4, 4, 4);

        for (int i = 0; i < h; i++) {
            ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
            ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
            int j = 0;
            if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
                ref byte bs = ref Unsafe.As<uint, byte>(ref rs);
                for (; j + 8 <= w; j += 8) {
                    ulong pair = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref bs, (nuint)j));
                    Vector128<byte> v8 = Vector128.CreateScalar(pair).AsByte();
                    Vector128.Shuffle(v8, maskLo).AsUInt32().StoreUnsafe(ref rd, (nuint)j);
                    Vector128.Shuffle(v8, maskHi).AsUInt32().StoreUnsafe(ref rd, (nuint)(j + 4));
                }
            }
            for (; j < w; j++) {
                uint v = (uint)GetDataByteUnchecked(ref rs, j);
                Unsafe.Add(ref rd, (nuint)j) = (v << 24) | (v << 16) | (v << 8);
            }
        }

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *           Top-level conversion to 8 or 32 bpp, without colormap           *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertTo8Or32 (pixconv.c)
    /// <summary>Converts to 8 bpp gray or 32 bpp rgb, without a colormap.</summary>
    /// <param name="copyflag">L_CLONE or L_COPY</param>
    /// <param name="warnflag">1 to issue warning if colormap is removed; else 0</param>
    /// <returns>pixd 8 bpp grayscale or 32 bpp rgb</returns>
    /// <remarks>
    /// (1) If there is a colormap, the colormap is removed to 8 or 32 bpp,
    ///     depending on whether the colors in the colormap are all gray.
    /// (2) If the input is either rgb or 8 bpp without a colormap,
    ///     this returns either a clone or a copy, depending on %copyflag.
    /// (3) Otherwise, the pix is converted to 8 bpp grayscale.
    ///     In all cases, pixd does not have a colormap.
    /// </remarks>
    public Pix ConvertTo8Or32(int copyflag, int warnflag)
    {
        Pix pixs = this;
        Pix pixd;

        if (copyflag != L_CLONE && copyflag != L_COPY)
            throw Lept.Arg("pixConvertTo8Or32", "invalid copyflag");

        int d = pixs.Depth;
        if (pixs.Colormap != null) {
            if (warnflag != 0) Log.Warning("pixConvertTo8Or32", "pix has colormap; removing");
            pixd = pixs.RemoveColormap(REMOVE_CMAP_BASED_ON_SRC);
        } else if (d == 8 || d == 32) {
            if (copyflag == L_CLONE)
                pixd = Clone(pixs);
            else  /* copyflag == L_COPY */
                pixd = Copy(null, pixs);
        } else {
            pixd = pixs.ConvertTo8(false);
        }

            /* Sanity check on result */
        d = pixd.Depth;
        if (d != 8 && d != 32)
            throw Lept.Arg("pixConvertTo8Or32", "depth not 8 or 32 bpp");

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                 Conversion between 24 bpp and 32 bpp rgb                  *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvert24To32 (pixconv.c)
    /// <summary>Converts a 24 bpp rgb image to 32 bpp rgb.</summary>
    /// <returns>pixd 32 bpp rgb</returns>
    /// <remarks>
    /// (1) 24 bpp rgb pix are not supported in leptonica, except for a small
    ///     number of formatted write operations.  The data is a byte array,
    ///     with pixels in order r,g,b, and padded to 32 bit boundaries
    ///     in each line.
    /// (2) Because 24 bpp rgb pix are conveniently generated by programs
    ///     such as xpdf (which has SplashBitmaps that store the raster
    ///     data in consecutive 24-bit rgb pixels), it is useful to provide
    ///     24 bpp pix that simply incorporate that data.  The only things
    ///     we can do with these are:
    ///       (a) write them to file in png, jpeg, tiff and pnm
    ///       (b) interconvert between 24 and 32 bpp in memory (for testing).
    /// </remarks>
    public Pix Convert24To32()
    {
        Pix pixs = this;
        int i, j, rval, gval, bval;

        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 24)
            throw Lept.Arg("pixConvert24To32", "pixs not 24 bpp");

        Pix pixd = Create(w, h, 32);
        ReadOnlySpan<byte> datas = MemoryMarshal.AsBytes(pixs.Data.AsSpan());
        uint[] datad = pixd.Data;
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<byte> lines = datas.Slice(i * wpls * 4);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            int k = 0;
            for (j = 0; j < w; j++) {
                rval = lines[k++];
                gval = lines[k++];
                bval = lines[k++];
                ComposeRGBPixel(rval, gval, bval, out uint pixel);
                lined[j] = pixel;
            }
        }
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        return pixd;
    }

    // leptonica: pixConvert32To24 (pixconv.c)
    /// <summary>Converts a 32 bpp rgb image to 24 bpp rgb.</summary>
    /// <returns>pixd 24 bpp rgb</returns>
    /// <remarks>(1) See pixconvert24To32().</remarks>
    public Pix Convert32To24()
    {
        Pix pixs = this;
        int i, j;

        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 32)
            throw Lept.Arg("pixConvert32To24", "pixs not 32 bpp");

        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        Pix pixd = Create(w, h, 24);
        Span<byte> rgbdata = MemoryMarshal.AsBytes(pixd.Data.AsSpan());
        int wpld = pixd.Wpl;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            int k = i * wpld * 4;
            for (j = 0; j < w; j++) {
                ExtractRGBValues(lines[j], out int rval, out int gval, out int bval);
                rgbdata[k++] = (byte)rval;
                rgbdata[k++] = (byte)gval;
                rgbdata[k++] = (byte)bval;
            }
        }
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *            Conversion between 32 bpp (1 spp) and 16 or 8 bpp              *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvert32To16 (pixconv.c)
    /// <summary>Converts a 32 bpp single component image to 16 bpp.</summary>
    /// <param name="type">L_LS_TWO_BYTES, L_MS_TWO_BYTES, L_CLIP_TO_FFFF</param>
    /// <returns>pixd 16 bpp</returns>
    /// <remarks>
    /// (1) The data in pixs is typically used for labelling.
    ///     It is an array of l_uint32 values, not rgb or rgba.
    /// </remarks>
    public Pix Convert32To16(int type)
    {
        Pix pixs = this;
        ushort dword;
        int i, j;
        uint sword;

        if (pixs.Depth != 32)
            throw Lept.Arg("pixConvert32To16", "pixs undefined or not 32 bpp");
        if (type != L_LS_TWO_BYTES && type != L_MS_TWO_BYTES &&
            type != L_CLIP_TO_FFFF)
            throw Lept.Arg("pixConvert32To16", "invalid type");

        pixs.GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 16);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        int wpls = pixs.Wpl;
        uint[] datas = pixs.Data;
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;

        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            if (type == L_LS_TWO_BYTES) {
                for (j = 0; j < wpls; j++) {
                    sword = lines[j];
                    dword = (ushort)(sword & 0xffff);
                    SetDataTwoBytes(lined, j, dword);
                }
            } else if (type == L_MS_TWO_BYTES) {
                for (j = 0; j < wpls; j++) {
                    sword = lines[j];
                    dword = (ushort)(sword >> 16);
                    SetDataTwoBytes(lined, j, dword);
                }
            } else {  /* type == L_CLIP_TO_FFFF */
                for (j = 0; j < wpls; j++) {
                    sword = lines[j];
                    dword = (ushort)((sword >> 16) != 0 ? 0xffff : (sword & 0xffff));
                    SetDataTwoBytes(lined, j, dword);
                }
            }
        }

        return pixd;
    }

    // leptonica: pixConvert32To8 (pixconv.c)
    /// <summary>Converts a 32 bpp single component image to 8 bpp, through 16 bpp.</summary>
    /// <param name="type16">L_LS_TWO_BYTES, L_MS_TWO_BYTES, L_CLIP_TO_FFFF</param>
    /// <param name="type8">L_LS_BYTE, L_MS_BYTE, L_CLIP_TO_FF</param>
    /// <returns>pixd 8 bpp</returns>
    public Pix Convert32To8(int type16, int type8)
    {
        Pix pixs = this;

        if (pixs.Depth != 32)
            throw Lept.Arg("pixConvert32To8", "pixs undefined or not 32 bpp");
        if (type16 != L_LS_TWO_BYTES && type16 != L_MS_TWO_BYTES &&
            type16 != L_CLIP_TO_FFFF)
            throw Lept.Arg("pixConvert32To8", "invalid type16");
        if (type8 != L_LS_BYTE && type8 != L_MS_BYTE && type8 != L_CLIP_TO_FF)
            throw Lept.Arg("pixConvert32To8", "invalid type8");

        Pix pix1 = pixs.Convert32To16(type16);
        Pix pixd = pix1.Convert16To8(type8);
        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *        Removal of alpha component by blending with white background       *
     *---------------------------------------------------------------------------*/
    // leptonica: pixRemoveAlpha (pixconv.c)
    /// <summary>Blends a 32 bpp rgba image over a white background.</summary>
    /// <returns>pixd if 32 bpp rgba, pixs blended over a white background; a clone of pixs otherwise</returns>
    /// <remarks>(1) This is a wrapper on pixAlphaBlendUniform()</remarks>
    public Pix RemoveAlpha()
    {
        Pix pixs = this;

        if (pixs.Depth == 32 && pixs.Spp == 4)
            return pixs.AlphaBlendUniform(0xffffff00);
        else
            return Clone(pixs);
    }

    /*---------------------------------------------------------------------------*
     *                  Addition of alpha component to 1 bpp                     *
     *---------------------------------------------------------------------------*/
    // leptonica: pixAddAlphaTo1bpp (pixconv.c)
    /// <summary>Makes a 1 bpp colormapped copy of a stencil, with a transparent background.</summary>
    /// <param name="pixd">[optional] 1 bpp, can be null or equal to pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <returns>pixd 1 bpp with colormap and non-opaque alpha</returns>
    /// <remarks>
    /// (1) We don't use 1 bpp colormapped images with alpha in leptonica,
    ///     but we support generating them (here), writing to png, and reading
    ///     the png.  On reading, they are converted to 32 bpp RGBA.
    /// (2) The background (0) pixels in pixs become fully transparent, and the
    ///     foreground (1) pixels are fully opaque.  Thus, pixd is a 1 bpp
    ///     representation of a stencil, that can be used to paint over pixels
    ///     of a backing image that are masked by the foreground in pixs.
    /// </remarks>
    public static Pix AddAlphaTo1bpp(Pix? pixd, Pix pixs)
    {
        if (pixs.Depth != 1)
            throw Lept.Arg("pixAddAlphaTo1bpp", "pixs undefined or not 1 bpp");
        if (pixd != null && (pixd != pixs))
            throw Lept.Arg("pixAddAlphaTo1bpp", "pixd defined but != pixs");

        pixd = Copy(pixd, pixs);
        PixColormap cmap = PixColormap.Create(1);
        pixd.SetColormap(cmap);
        cmap.AddRGBA(255, 255, 255, 0);  /* 0 ==> white + transparent */
        cmap.AddRGBA(0, 0, 0, 255);  /* 1 ==> black + opaque */
        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                  Lossless depth conversion (unpacking)                    *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertLossless (pixconv.c)
    /// <summary>Unpacks a 1, 2, 4 or 8 bpp image losslessly to a greater depth.</summary>
    /// <param name="d">destination depth: 2, 4 or 8</param>
    /// <returns>pixd 2, 4 or 8 bpp</returns>
    /// <remarks>
    /// (1) This is a lossless unpacking (depth-increasing)
    ///     conversion.  If ds is the depth of pixs, then
    ///      ~ if d &lt; ds, returns NULL
    ///      ~ if d == ds, returns a copy
    ///      ~ if d > ds, does the unpacking conversion
    /// (2) If pixs has a colormap, this is an error.
    /// (NOTE(upstream)) For ds == 4 the C code reads the source with
    ///     GET_DATA_DIBIT instead of GET_DATA_QBIT; see docs/UPSTREAM_NOTES.md.
    /// </remarks>
    public Pix ConvertLossless(int d)
    {
        Pix pixs = this;
        int i, j, val;

        if (pixs.Colormap != null)
            throw Lept.Arg("pixConvertLossless", "pixs has colormap");
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg("pixConvertLossless", "invalid dest depth");

        pixs.GetDimensions(out int w, out int h, out int ds);
        if (d < ds)
            throw Lept.Arg("pixConvertLossless", "depth > d");
        else if (d == ds)
            return Copy(null, pixs);

        Pix pixd = Create(w, h, d);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);

            /* Unpack the bits */
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            switch (ds)
            {
            case 1:
                for (j = 0; j < w; j++) {
                    val = GetDataBit(lines, j);
                    if (d == 8)
                        SetDataByte(lined, j, val);
                    else if (d == 4)
                        SetDataQbit(lined, j, val);
                    else  /* d == 2 */
                        SetDataDibit(lined, j, val);
                }
                break;
            case 2:
                for (j = 0; j < w; j++) {
                    val = GetDataDibit(lines, j);
                    if (d == 8)
                        SetDataByte(lined, j, val);
                    else  /* d == 4 */
                        SetDataQbit(lined, j, val);
                }
                break;
            case 4:
                    /* NOTE(upstream): this reads dibits from the 4 bpp source. */
                for (j = 0; j < w; j++) {
                    val = GetDataDibit(lines, j);
                    SetDataByte(lined, j, val);
                }
                break;
            }
        }

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                     Conversion for printing in PostScript                 *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertForPSWrap (pixconv.c)
    /// <summary>Converts to 1, 8 or 32 bpp, without a colormap, for PostScript wrapping.</summary>
    /// <returns>A new image with 1, 8 or 32 bpp and no colormap.</returns>
    /// <remarks>
    /// <para>For wrapping in PostScript, the image is converted to 1 bpp, 8 bpp (gray) or 32 bpp (rgb color).</para>
    /// <para>Colormaps are removed. Images with colormaps are converted to either 8 bpp gray or 32 bpp rgb,
    /// depending on whether the colormap has color content.</para>
    /// <para>Images without colormaps that are not 1 bpp or 32 bpp are converted to 8 bpp gray.</para>
    /// <para>The source depth can be 1, 2, 4, 8, 16 or 32 bpp.</para>
    /// </remarks>
    public Pix ConvertForPSWrap()
    {
        Pix pixs = this;
        Pix pixd;

        PixColormap? cmap = pixs.Colormap;
        int d = pixs.Depth;
        switch (d)
        {
        case 1:
        case 32:
            pixd = Clone(pixs);
            break;
        case 2:
            if (cmap != null)
                pixd = pixs.RemoveColormap(REMOVE_CMAP_BASED_ON_SRC);
            else
                pixd = pixs.Convert2To8(0, 0x55, 0xaa, 0xff, false);
            break;
        case 4:
            if (cmap != null)
                pixd = pixs.RemoveColormap(REMOVE_CMAP_BASED_ON_SRC);
            else
                pixd = pixs.Convert4To8(false);
            break;
        case 8:
            pixd = pixs.RemoveColormap(REMOVE_CMAP_BASED_ON_SRC);
            break;
        case 16:
            pixd = pixs.Convert16To8(L_MS_BYTE);
            break;
        default:
            // C: writes this message to stderr and returns NULL
            throw Lept.Arg("pixConvertForPSWrap", "depth not in {1, 2, 4, 8, 16, 32}");
       }

       return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                      Scaling conversion to subpixel RGB                   *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertToSubpixelRGB (pixconv.c)
    /// <summary>Scales a gray, color or colormapped image to 32 bpp, splitting pixels into subpixels.</summary>
    /// <param name="scalex">anisotropic scaling permitted between source and destination</param>
    /// <param name="scaley">anisotropic scaling permitted between source and destination</param>
    /// <param name="order">of subpixel rgb color components in composition of pixd:
    /// L_SUBPIXEL_ORDER_RGB, L_SUBPIXEL_ORDER_BGR, L_SUBPIXEL_ORDER_VRGB, L_SUBPIXEL_ORDER_VBGR</param>
    /// <returns>pixd 32 bpp</returns>
    /// <remarks>
    /// (1) If pixs has a colormap, it is removed based on its contents
    ///     to either 8 bpp gray or rgb.
    /// (2) For horizontal subpixel splitting, the input image
    ///     is rescaled by %scaley vertically and by 3.0 times
    ///     %scalex horizontally.  Then each horizontal triplet
    ///     of pixels is mapped back to a single rgb pixel, with the
    ///     r, g and b values being assigned based on the pixel triplet.
    ///     For gray triplets, the r, g, and b values are set equal to
    ///     the three gray values.  For color triplets, the r, g and b
    ///     values are set equal to the components from the appropriate
    ///     subpixel.  Vertical subpixel splitting is handled similarly.
    /// (3) See pixConvertGrayToSubpixelRGB() and
    ///     pixConvertColorToSubpixelRGB() for further details.
    /// </remarks>
    public Pix? ConvertToSubpixelRGB(float scalex, float scaley, int order)
    {
        Pix pixs = this;
        Pix? pixd;

        int d = pixs.Depth;
        PixColormap? cmap = pixs.Colormap;
        if (d != 8 && d != 32 && cmap == null)
            throw Lept.Arg("pixConvertToSubpixelRGB", "pix not 8 or 32 bpp and not cmapped");
        if (scalex <= 0.0 || scaley <= 0.0)
            throw Lept.Arg("pixConvertToSubpixelRGB", "scale factors must be > 0");
        if (order != L_SUBPIXEL_ORDER_RGB && order != L_SUBPIXEL_ORDER_BGR &&
            order != L_SUBPIXEL_ORDER_VRGB && order != L_SUBPIXEL_ORDER_VBGR)
            throw Lept.Arg("pixConvertToSubpixelRGB", "invalid subpixel order");
        Pix pix1 = pixs.RemoveColormap(REMOVE_CMAP_BASED_ON_SRC);

        d = pix1.Depth;
        pixd = null;
        if (d == 8)
            pixd = pix1.ConvertGrayToSubpixelRGB(scalex, scaley, order);
        else if (d == 32)
            pixd = pix1.ConvertColorToSubpixelRGB(scalex, scaley, order);
        else
            Log.Error("pixConvertToSubpixelRGB", $"invalid depth {d}");

        return pixd;
    }

    // leptonica: pixConvertGrayToSubpixelRGB (pixconv.c)
    /// <summary>Scales a gray (or colormapped) image to 32 bpp, assigning gray values to subpixels.</summary>
    /// <param name="scalex">scaling factor</param>
    /// <param name="scaley">scaling factor</param>
    /// <param name="order">of subpixel rgb color components in composition of pixd:
    /// L_SUBPIXEL_ORDER_RGB, L_SUBPIXEL_ORDER_BGR, L_SUBPIXEL_ORDER_VRGB, L_SUBPIXEL_ORDER_VBGR</param>
    /// <returns>pixd 32 bpp</returns>
    /// <remarks>
    /// (1) If pixs has a colormap, it is removed to 8 bpp.
    /// (2) For horizontal subpixel splitting, the input gray image
    ///     is rescaled by %scaley vertically and by 3.0 times
    ///     %scalex horizontally.  Then each horizontal triplet
    ///     of pixels is mapped back to a single rgb pixel, with the
    ///     r, g and b values being assigned from the triplet of gray values.
    ///     Similar operations are used for vertical subpixel splitting.
    /// (3) This is a form of subpixel rendering that tends to give the
    ///     resulting text a sharper and somewhat chromatic display.
    ///     For horizontal subpixel splitting, the observable difference
    ///     between %order=L_SUBPIXEL_ORDER_RGB and
    ///     %order=L_SUBPIXEL_ORDER_BGR is reduced by optical diffusers
    ///     in the display that make the pixel color appear to emerge
    ///     from the entire pixel.
    /// </remarks>
    public Pix ConvertGrayToSubpixelRGB(float scalex, float scaley, int order)
    {
        Pix pixs = this;
        int i, j, rval, gval, bval;
        Pix pix2;

        int d = pixs.Depth;
        PixColormap? cmap = pixs.Colormap;
        if (d != 8 && cmap == null)
            throw Lept.Arg("pixConvertGrayToSubpixelRGB", "pix not 8 bpp & not cmapped");
        if (scalex <= 0.0 || scaley <= 0.0)
            throw Lept.Arg("pixConvertGrayToSubpixelRGB", "scale factors must be > 0");
        if (order != L_SUBPIXEL_ORDER_RGB && order != L_SUBPIXEL_ORDER_BGR &&
            order != L_SUBPIXEL_ORDER_VRGB && order != L_SUBPIXEL_ORDER_VBGR)
            throw Lept.Arg("pixConvertGrayToSubpixelRGB", "invalid subpixel order");

        int direction =
            (order == L_SUBPIXEL_ORDER_RGB || order == L_SUBPIXEL_ORDER_BGR)
            ? L_HORIZ : L_VERT;
        Pix pix1 = pixs.RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
        if (direction == L_HORIZ)
            pix2 = pix1.Scale(3.0f * scalex, scaley);
        else  /* L_VERT */
            pix2 = pix1.Scale(scalex, 3.0f * scaley);

        pix2.GetDimensions(out int w, out int h, out _);
        int wd = (direction == L_HORIZ) ? w / 3 : w;
        int hd = (direction == L_VERT) ? h / 3 : h;
        Pix pixd = Create(wd, hd, 32);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        uint[] datat = pix2.Data;
        int wplt = pix2.Wpl;
        if (direction == L_HORIZ) {
            for (i = 0; i < hd; i++) {
                ReadOnlySpan<uint> linet = datat.AsSpan(i * wplt, wplt);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (j = 0; j < wd; j++) {
                    rval = GetDataByte(linet, 3 * j);
                    gval = GetDataByte(linet, 3 * j + 1);
                    bval = GetDataByte(linet, 3 * j + 2);
                    if (order == L_SUBPIXEL_ORDER_RGB)
                        ComposeRGBPixel(rval, gval, bval, out lined[j]);
                    else  /* order BGR */
                        ComposeRGBPixel(bval, gval, rval, out lined[j]);
                }
            }
        } else {  /* L_VERT */
            for (i = 0; i < hd; i++) {
                int tline = 3 * i * wplt;
                ReadOnlySpan<uint> linet0 = datat.AsSpan(tline, wplt);
                ReadOnlySpan<uint> linet1 = datat.AsSpan(tline + wplt, wplt);
                ReadOnlySpan<uint> linet2 = datat.AsSpan(tline + 2 * wplt, wplt);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (j = 0; j < wd; j++) {
                    rval = GetDataByte(linet0, j);
                    gval = GetDataByte(linet1, j);
                    bval = GetDataByte(linet2, j);
                    if (order == L_SUBPIXEL_ORDER_VRGB)
                        ComposeRGBPixel(rval, gval, bval, out lined[j]);
                    else  /* order VBGR */
                        ComposeRGBPixel(bval, gval, rval, out lined[j]);
                }
            }
        }

        return pixd;
    }

    // leptonica: pixConvertColorToSubpixelRGB (pixconv.c)
    /// <summary>Scales a color (or colormapped) image to 32 bpp, selecting components from subpixels.</summary>
    /// <param name="scalex">scaling factor</param>
    /// <param name="scaley">scaling factor</param>
    /// <param name="order">of subpixel rgb color components in composition of pixd:
    /// L_SUBPIXEL_ORDER_RGB, L_SUBPIXEL_ORDER_BGR, L_SUBPIXEL_ORDER_VRGB, L_SUBPIXEL_ORDER_VBGR</param>
    /// <returns>pixd 32 bpp</returns>
    /// <remarks>
    /// (1) If pixs has a colormap, it is removed to 32 bpp rgb.
    ///     If the colormap has no color, pixConvertGrayToSubpixelRGB()
    ///     should be called instead, because it will give the same result
    ///     more efficiently.  The function pixConvertToSubpixelRGB()
    ///     will do the best thing for all cases.
    /// (2) For horizontal subpixel splitting, the input rgb image
    ///     is rescaled by %scaley vertically and by 3.0 times
    ///     %scalex horizontally.  Then for each horizontal triplet
    ///     of pixels, the r component of the final pixel is selected
    ///     from the r component of the appropriate pixel in the triplet,
    ///     and likewise for g and b.  Vertical subpixel splitting is
    ///     handled similarly.
    /// </remarks>
    public Pix ConvertColorToSubpixelRGB(float scalex, float scaley, int order)
    {
        Pix pixs = this;
        int i, j, rval, gval, bval;
        Pix pix2;

        int d = pixs.Depth;
        PixColormap? cmap = pixs.Colormap;
        if (d != 32 && cmap == null)
            throw Lept.Arg("pixConvertColorToSubpixelRGB", "pix not 32 bpp & not cmapped");
        if (scalex <= 0.0 || scaley <= 0.0)
            throw Lept.Arg("pixConvertColorToSubpixelRGB", "scale factors must be > 0");
        if (order != L_SUBPIXEL_ORDER_RGB && order != L_SUBPIXEL_ORDER_BGR &&
            order != L_SUBPIXEL_ORDER_VRGB && order != L_SUBPIXEL_ORDER_VBGR)
            throw Lept.Arg("pixConvertColorToSubpixelRGB", "invalid subpixel order");

        int direction =
            (order == L_SUBPIXEL_ORDER_RGB || order == L_SUBPIXEL_ORDER_BGR)
            ? L_HORIZ : L_VERT;
        Pix pix1 = pixs.RemoveColormap(REMOVE_CMAP_TO_FULL_COLOR);
        if (direction == L_HORIZ)
            pix2 = pix1.Scale(3.0f * scalex, scaley);
        else  /* L_VERT */
            pix2 = pix1.Scale(scalex, 3.0f * scaley);

        pix2.GetDimensions(out int w, out int h, out _);
        int wd = (direction == L_HORIZ) ? w / 3 : w;
        int hd = (direction == L_VERT) ? h / 3 : h;
        Pix pixd = Create(wd, hd, 32);
        pixd.CopyInputFormat(pixs);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        uint[] datat = pix2.Data;
        int wplt = pix2.Wpl;
        if (direction == L_HORIZ) {
            for (i = 0; i < hd; i++) {
                ReadOnlySpan<uint> linet = datat.AsSpan(i * wplt, wplt);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (j = 0; j < wd; j++) {
                    if (order == L_SUBPIXEL_ORDER_RGB) {
                        ExtractRGBValues(linet[3 * j], out rval, out _, out _);
                        ExtractRGBValues(linet[3 * j + 1], out _, out gval, out _);
                        ExtractRGBValues(linet[3 * j + 2], out _, out _, out bval);
                    } else {  /* order BGR */
                        ExtractRGBValues(linet[3 * j], out _, out _, out bval);
                        ExtractRGBValues(linet[3 * j + 1], out _, out gval, out _);
                        ExtractRGBValues(linet[3 * j + 2], out rval, out _, out _);
                    }
                    ComposeRGBPixel(rval, gval, bval, out lined[j]);
                }
            }
        } else {  /* L_VERT */
            for (i = 0; i < hd; i++) {
                int tline = 3 * i * wplt;
                ReadOnlySpan<uint> linet0 = datat.AsSpan(tline, wplt);
                ReadOnlySpan<uint> linet1 = datat.AsSpan(tline + wplt, wplt);
                ReadOnlySpan<uint> linet2 = datat.AsSpan(tline + 2 * wplt, wplt);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (j = 0; j < wd; j++) {
                    if (order == L_SUBPIXEL_ORDER_VRGB) {
                        ExtractRGBValues(linet0[j], out rval, out _, out _);
                        ExtractRGBValues(linet1[j], out _, out gval, out _);
                        ExtractRGBValues(linet2[j], out _, out _, out bval);
                    } else {  /* order VBGR */
                        ExtractRGBValues(linet0[j], out _, out _, out bval);
                        ExtractRGBValues(linet1[j], out _, out gval, out _);
                        ExtractRGBValues(linet2[j], out rval, out _, out _);
                    }
                    ComposeRGBPixel(rval, gval, bval, out lined[j]);
                }
            }
        }

        if (pixs.Spp == 4)
            pixd.ScaleAndTransferAlpha(pixs, scalex, scaley);

        return pixd;
    }

    /*---------------------------------------------------------------------*
     *       Setting neutral point for min/max boost conversion to gray    *
     *---------------------------------------------------------------------*/
    // leptonica: l_setNeutralBoostVal (pixconv.c)
    /// <summary>Sets the reference value for the min and max boost conversions to gray.</summary>
    /// <param name="val">between 1 and 255; typical value is 180</param>
    /// <remarks>
    /// (1) This raises or lowers the selected min or max RGB component value,
    ///     depending on if that component is above or below this value.
    /// </remarks>
    public static void SetNeutralBoostVal(int val)
    {
        if (val <= 0) {
            Log.Error("l_setNeutralBoostVal", "invalid reference value for neutral boost");
            return;
        }
        var_NEUTRAL_BOOST_VAL = val;
    }

}
