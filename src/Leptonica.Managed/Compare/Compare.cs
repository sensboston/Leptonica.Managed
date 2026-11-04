using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Threading;

namespace Leptonica;

/// <summary>Constants from morph.h used by compare.c.</summary>
public static partial class L
{
    /* Comparison of two binary images (pixCompareBinary) */
    /// <summary>Compares the two images using the exclusive-or of their pixels.</summary>
    public const int L_COMPARE_XOR = 1;

    /// <summary>Compares the two images by subtracting one from the other.</summary>
    public const int L_COMPARE_SUBTRACT = 2;

    /// <summary>Compares the two images using the absolute value of their pixel differences.</summary>
    public const int L_COMPARE_ABS_DIFF = 3;
}

/*
 *  compare.c: Pix equality and similarity.
 *
 *  All functions are ported.  The optional output pointers of the C functions become
 *  out parameters (always computed), except in pixaComparePhotoRegionsByHisto, where
 *  getScores / getPixd select the outputs, because the score array output writes a file.
 *  The static helper pixCompareTilesByHisto() is an overload of CompareTilesByHisto().
 *  Callees that are not ported yet are stubs in the files of their C files
 *  (gplot.c, pageseg.c, correlscore.c, binreduce.c, morphapp.c, pdfio1.c, ...).
 */
public sealed partial class Pix
{
    // leptonica: pixEqual (compare.c)
    /// <summary>True if the two pix are equal, ignoring alpha.</summary>
    /// <remarks>
    /// (1) Equality is defined as having the same pixel values for
    ///     each respective image pixel.
    /// (2) This works on two pix of any depth.  If one or both pix
    ///     have a colormap, the depths can be different and the
    ///     two pix can still be equal.
    /// (3) This ignores the alpha component for 32 bpp images.
    /// (4) If both pix have colormaps and the depths are equal,
    ///     use the pixEqualWithCmap() function, which does a fast
    ///     comparison if the colormaps are identical and a relatively
    ///     slow comparison otherwise.
    /// (5) In all other cases, any existing colormaps must first be
    ///     removed before doing pixel comparison.  After the colormaps
    ///     are removed, the resulting two images must have the same depth.
    ///     The "lowest common denominator" is RGB, but this is only
    ///     chosen when necessary, or when both have colormaps but
    ///     different depths.
    /// (6) For images without colormaps that are not 32 bpp, all bits
    ///     in the image part of the data array must be identical.
    /// </remarks>
    public static bool Equal(Pix pix1, Pix pix2)
    {
        return EqualWithAlpha(pix1, pix2, false);
    }

    // leptonica: pixEqualWithAlpha (compare.c)
    /// <summary>True if the two pix are equal.</summary>
    /// <param name="pix1">The first image.</param>
    /// <param name="pix2">The second image.</param>
    /// <param name="use_alpha"><c>true</c> to include the alpha component of 32 bpp RGBA images in the comparison.</param>
    /// <remarks>
    /// (1) See notes in pixEqual().
    /// (2) This is more general than pixEqual(), in that for 32 bpp
    ///     RGBA images, where spp = 4, you can optionally include
    ///     the alpha component in the comparison.
    /// </remarks>
    public static bool EqualWithAlpha(Pix pix1, Pix pix2, bool use_alpha)
    {
        const string func = "pixEqualWithAlpha";
        pix1.GetDimensions(out int w1, out int h1, out int d1);
        pix2.GetDimensions(out int w2, out int h2, out int d2);
        if (w1 != w2 || h1 != h2) {
            Log.Info(func, "pix sizes differ");
            return false;
        }

            /* Suppose the use_alpha flag is true.
             * If only one of two 32 bpp images has spp == 4, we call that
             * a "mismatch" of the alpha component.  In the case of a mismatch,
             * if the 4 bpp pix does not have all alpha components opaque (255),
             * the images are not-equal.  However if they are all opaque,
             * this image is equivalent to spp == 3, so we allow the
             * comparison to go forward, testing only for the RGB equality. */
        int spp1 = pix1.Spp;
        int spp2 = pix2.Spp;
        bool mismatch = false;
        if (use_alpha && d1 == 32 && d2 == 32) {
            mismatch = ((spp1 == 4 && spp2 != 4) || (spp1 != 4 && spp2 == 4));
            if (mismatch) {
                Pix pixalpha = (spp1 == 4) ? pix1 : pix2;
                bool opaque = pixalpha.AlphaIsOpaque();
                if (!opaque) {
                    Log.Info(func, "just one pix has a non-opaque alpha layer");
                    return false;
                }
            }
        }

        PixColormap? cmap1 = pix1.Colormap;
        PixColormap? cmap2 = pix2.Colormap;
        if (cmap1 == null && cmap2 == null && (d1 != d2) && (d1 == 32 || d2 == 32)) {
            Log.Info(func, "no colormaps, pix depths unequal, and one of them is RGB");
            return false;
        }

        if (cmap1 != null && cmap2 != null && (d1 == d2))   /* use special function */
            return EqualWithCmap(pix1, pix2);

            /* Must remove colormaps if they exist, and in the process
             * end up with the resulting images having the same depth. */
        Pix pixs1, pixs2;
        if (cmap1 != null && cmap2 == null) {
            bool color = pix1.UsesCmapColor();
            if (color && d2 <= 8)  /* can't be equal */
                return false;
            if (d2 < 8)
                pixs2 = pix2.ConvertTo8(false);
            else
                pixs2 = Clone(pix2);
            if (d2 <= 8)
                pixs1 = pix1.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
            else
                pixs1 = pix1.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        } else if (cmap1 == null && cmap2 != null) {
            bool color = pix2.UsesCmapColor();
            if (color && d1 <= 8)  /* can't be equal */
                return false;
            if (d1 < 8)
                pixs1 = pix1.ConvertTo8(false);
            else
                pixs1 = Clone(pix1);
            if (d1 <= 8)
                pixs2 = pix2.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
            else
                pixs2 = pix2.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        } else if (cmap1 != null && cmap2 != null) {  /* depths not equal; use rgb */
            pixs1 = pix1.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
            pixs2 = pix2.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        } else {  /* no colormaps */
            pixs1 = Clone(pix1);
            pixs2 = Clone(pix2);
        }

            /* OK, we have no colormaps, but the depths may still be different */
        d1 = pixs1.Depth;
        d2 = pixs2.Depth;
        Pix pixt1, pixt2;
        if (d1 != d2) {
            if (d1 == 16 || d2 == 16) {
                Log.Info(func, "one pix is 16 bpp");
                return false;
            }
            try {
                pixt1 = pixs1.ConvertLossless(8);
                pixt2 = pixs2.ConvertLossless(8);
            } catch (LeptonicaException) {
                Log.Info(func, "failure to convert to 8 bpp");
                return false;
            }
        } else {
            pixt1 = Clone(pixs1);
            pixt2 = Clone(pixs2);
        }

            /* No colormaps, equal depths; do pixel comparisons */
        d1 = pixt1.Depth;
        int wpl1 = pixt1.Wpl;
        int wpl2 = pixt2.Wpl;
        uint[] data1 = pixt1.Data;
        uint[] data2 = pixt2.Data;
        if (d1 == 32) {  /* test either RGB or RGBA pixels */
            uint wordmask;
            if (use_alpha && !mismatch)
                wordmask = (spp1 == 3) ? 0xffffff00 : 0xffffffff;
            else
                wordmask = 0xffffff00;
            if (wordmask == 0xffffffff)
                return data1.AsSpan(0, wpl1 * h1).SequenceEqual(data2.AsSpan(0, wpl2 * h1));

            int total = wpl1 * h1;
            ref uint r1 = ref MemoryMarshal.GetArrayDataReference(data1);
            ref uint r2 = ref MemoryMarshal.GetArrayDataReference(data2);
            int k = 0;
            if (Vector256.IsHardwareAccelerated && total >= 8) {
                Vector256<uint> vMask = Vector256.Create(wordmask);
                for (; k + 8 <= total; k += 8) {
                    Vector256<uint> vDiff = (Vector256.LoadUnsafe(ref r1, (nuint)k) ^ Vector256.LoadUnsafe(ref r2, (nuint)k)) & vMask;
                    if (vDiff != Vector256<uint>.Zero)
                        return false;
                }
            }
            for (; k < total; k++) {
                if (((Unsafe.Add(ref r1, (nuint)k) ^ Unsafe.Add(ref r2, (nuint)k)) & wordmask) != 0)
                    return false;
            }
        } else {  /* all bits count */
            int linebits = d1 * w1;
            int fullwords = linebits >> 5;
            int endbits = linebits & 31;
            if (endbits == 0)
                return data1.AsSpan(0, wpl1 * h1).SequenceEqual(data2.AsSpan(0, wpl2 * h1));

            uint endmask = 0xffffffffu << (32 - endbits);
            for (int i = 0; i < h1; i++) {
                int line1 = wpl1 * i;
                int line2 = wpl2 * i;
                if (fullwords > 0 && !data1.AsSpan(line1, fullwords).SequenceEqual(data2.AsSpan(line2, fullwords)))
                    return false;
                if (((data1[line1 + fullwords] ^ data2[line2 + fullwords]) & endmask) != 0)
                    return false;
            }
        }

        return true;
    }

    // leptonica: pixEqualWithCmap (compare.c)
    /// <summary>True if the two colormapped pix of equal depth are equal.</summary>
    /// <remarks>
    /// (1) This returns same = true if the images have the same
    ///     size, depth and pixel values.
    /// (2) Both images must have a colormap, and both pix must have the
    ///     same depth.
    /// (3) If the colormaps are identical, the comparison is by words.
    ///     Otherwise, the comparison is by the rgb values of each pixel.
    /// </remarks>
    public static bool EqualWithCmap(Pix pix1, Pix pix2)
    {
        const string func = "pixEqualWithCmap";
        if (!SizesEqual(pix1, pix2))
            return false;
        PixColormap? cmap1 = pix1.Colormap;
        PixColormap? cmap2 = pix2.Colormap;
        if (cmap1 == null || cmap2 == null) {
            Log.Info(func, "both images don't have colormap");
            return false;
        }
        pix1.GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 2 && d != 4 && d != 8) {
            Log.Info(func, "pix depth not in {1, 2, 4, 8}");
            return false;
        }

        bool samecmaps = PixColormap.CmapEqual(cmap1, cmap2, 3);
        if (samecmaps == true) {  /* colormaps are identical; compare by words */
            int linebits = d * w;
            int wpl1 = pix1.Wpl;
            int wpl2 = pix2.Wpl;
            uint[] data1 = pix1.Data;
            uint[] data2 = pix2.Data;
            int fullwords = linebits / 32;
            int endbits = linebits & 31;
            uint endmask = (endbits == 0) ? 0 : (0xffffffff << (32 - endbits));
            for (int i = 0; i < h; i++) {
                int line1 = wpl1 * i;
                int line2 = wpl2 * i;
                for (int j = 0; j < fullwords; j++) {
                    if ((data1[line1] ^ data2[line2]) != 0)
                        return false;
                    line1++;
                    line2++;
                }
                if (endbits != 0) {
                    if (((data1[line1] ^ data2[line2]) & endmask) != 0)
                        return false;
                }
            }
            return true;
        }

            /* Colormaps aren't identical; compare pixel by pixel */
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                pix1.GetPixel(j, i, out uint val1);
                pix2.GetPixel(j, i, out uint val2);
                cmap1.GetColor((int)val1, out int rval1, out int gval1, out int bval1);
                cmap2.GetColor((int)val2, out int rval2, out int gval2, out int bval2);
                if (rval1 != rval2 || gval1 != gval2 || bval1 != bval2)
                    return false;
            }
        }

        return true;
    }

    // leptonica: pixUsesCmapColor (compare.c)
    /// <summary>True if the colormap has color entries that are used by pixels.</summary>
    /// <remarks>
    /// (1) This returns color = TRUE if three things are obtained:
    ///     (a) the pix has a colormap
    ///     (b) the colormap has at least one color entry
    ///     (c) a color entry is actually used
    /// (2) It is used in pixEqual() for comparing two images, in a
    ///     situation where it is required to know if the colormap
    ///     has color entries that are actually used in the image.
    /// </remarks>
    public bool UsesCmapColor()
    {
        PixColormap? cmap = Colormap;
        if (cmap == null)
            return false;

        bool color = cmap.HasColor();
        if (!color)  /* no color */
            return false;

            /* The cmap has color entries.  Are they used? */
            // NOTE(upstream): color is not reset when no used entry is a color,
            // and the gray-value histogram is indexed by colormap index
        Numa na = GetGrayHistogram(1);
        int n = cmap.Count;
        for (int i = 0; i < n; i++) {
            cmap.GetColor(i, out int rval, out int gval, out int bval);
            int numpix = na.GetIValue(i);
            if ((rval != gval || rval != bval) && numpix != 0) {  /* color found! */
                color = true;
                break;
            }
        }

        return color;
    }

    /*------------------------------------------------------------------*
     *                          Binary correlation                      *
     *------------------------------------------------------------------*/
    // leptonica: pixCorrelationBinary (compare.c)
    /// <summary>Correlation of the foreground of two 1 bpp images.</summary>
    /// <param name="pix1">1 bpp</param>
    /// <param name="pix2">1 bpp</param>
    /// <returns>correlation, in [0.0 .. 1.0]</returns>
    /// <remarks>
    /// (1) The correlation is a number between 0.0 and 1.0,
    ///     based on foreground similarity:
    ///                      (|1 AND 2|)**2
    ///       correlation =  --------------
    ///                        |1| * |2|
    ///     where |x| is the count of foreground pixels in image x.
    ///     If the images are identical, this is 1.0.
    ///     If they have no fg pixels in common, this is 0.0.
    ///     If one or both images have no fg pixels, the correlation is 0.0.
    /// (2) Typically the two images are of equal size, but this
    ///     is not enforced.  Instead, the UL corners are aligned.
    /// </remarks>
    public static float CorrelationBinary(Pix pix1, Pix pix2)
    {
        int[] tab8 = MakePixelSumTab8();
        int count1 = pix1.CountPixels(tab8);
        int count2 = pix2.CountPixels(tab8);
        if (count1 == 0 || count2 == 0)
            return 0.0f;
        Pix pixn = And(null, pix1, pix2);
        int countn = pixn.CountPixels(tab8);
        return (float)countn * (float)countn /
               ((float)count1 * (float)count2);
    }

    /*------------------------------------------------------------------*
     *                   Difference of two images                       *
     *------------------------------------------------------------------*/
    // leptonica: pixDisplayDiff (compare.c)
    /// <summary>Shows which pixels of two images differ significantly.</summary>
    /// <param name="pix2">any depth</param>
    /// <param name="showall">true to display input images; false to only display result</param>
    /// <param name="mindiff">min difference to identify pixel</param>
    /// <param name="diffcolor">color of pixel indicating difference &gt;= mindiff</param>
    /// <returns>pixd 32 bpp rgb</returns>
    /// <remarks>
    /// (1) This aligns the UL corners of pix1 and pix2, crops to the
    ///     overlapping pixels, and shows which pixels have a significant
    ///     difference in value.
    /// (2) Requires %pix1 and %pix2 to have the same depth.
    /// (3) If rgb, a pixel is identified as different if any component
    ///     values of the corresponding pixels equals or exceeds %mindiff.
    /// (4) %diffcolor is in format 0xrrggbbaa.
    /// (5) If %pix1 and %pix2 are 1 bpp, ignores %mindiff and %diffcolor,
    ///     and uses the result of pixDisplayDiffBinary().
    /// </remarks>
    public Pix DisplayDiff(Pix pix2, bool showall, int mindiff, uint diffcolor)
    {
        const string func = "pixDisplayDiff";
        Pix pix1 = this;
        pix1.GetDimensions(out int w1, out int h1, out int d1);
        pix2.GetDimensions(out int w2, out int h2, out int d2);
        if (d1 != d2)
            throw Lept.Arg(func, "unequal depths");
        if (mindiff <= 0)
            throw Lept.Arg(func, "mindiff must be > 0");

        Pix? pix3 = null, pix4 = null;
        Pix pixd;
        if (d1 == 1) {
            pix3 = pix1.DisplayDiffBinary(pix2);
            pixd = pix3.ConvertTo32();
            pix3 = null;
        } else {
            int minw = Math.Min(w1, w2);
            int minh = Math.Min(h1, h2);
            pix3 = pix1.ConvertTo32();
            pix4 = pix2.ConvertTo32();
            pixd = Create(minw, minh, 32);
            pixd.Rasterop(0, 0, minw, minh, L.PIX_SRC, pix3, 0, 0);
            uint[] data1 = pix3.Data;
            int wpl1 = pix3.Wpl;
            uint[] data2 = pix4.Data;
            int wpl2 = pix4.Wpl;
            uint[] data3 = pixd.Data;
            int wpl3 = pixd.Wpl;
            for (int i = 0; i < minh; i++) {
                int line1 = i * wpl1;
                int line2 = i * wpl2;
                int line3 = i * wpl3;
                for (int j = 0; j < minw; j++) {
                    uint val1 = data1[line1 + j];
                    uint val2 = data2[line2 + j];
                    ExtractRGBValues(val1, out int rval1, out int gval1, out int bval1);
                    ExtractRGBValues(val2, out int rval2, out int gval2, out int bval2);
                    if (Math.Abs(rval1 - rval2) >= mindiff ||
                        Math.Abs(gval1 - gval2) >= mindiff ||
                        Math.Abs(bval1 - bval2) >= mindiff)
                        data3[line3 + j] = diffcolor;
                }
            }
        }

        if (showall) {
            Pixa pixa1 = Pixa.Create(3);
            if (d1 == 1) {
                pixa1.AddPix(pix1, L.L_COPY);
                pixa1.AddPix(pix2, L.L_COPY);
            } else {
                pixa1.AddPix(pix3!, L.L_INSERT);
                pixa1.AddPix(pix4!, L.L_INSERT);
            }
            pixa1.AddPix(pixd, L.L_INSERT);  /* save diff image */
            pixd = pixa1.DisplayTiledInColumns(2, 1.0f, 30, 2);  /* all 3 */
        }
        return pixd;
    }

    // leptonica: pixDisplayDiffBinary (compare.c)
    /// <summary>Color representation of the difference between two 1 bpp images.</summary>
    /// <param name="pix2">1 bpp</param>
    /// <returns>pixd 4 bpp cmapped</returns>
    /// <remarks>
    /// (1) This gives a color representation of the difference between
    ///     pix1 and pix2.  The color difference depends on the order.
    ///     The pixels in pixd have 4 colors:
    ///      * unchanged:  black (on), white (off)
    ///      * on in pix1, off in pix2: red
    ///      * on in pix2, off in pix1: green
    /// (2) This aligns the UL corners of pix1 and pix2, and crops
    ///     to the overlapping pixels.
    /// </remarks>
    public Pix DisplayDiffBinary(Pix pix2)
    {
        const string func = "pixDisplayDiffBinary";
        Pix pix1 = this;
        pix1.GetDimensions(out int w1, out int h1, out int d1);
        pix2.GetDimensions(out int w2, out int h2, out int d2);
        if (d1 != 1 || d2 != 1)
            throw Lept.Arg(func, "pix1 and pix2 not 1 bpp");
        int minw = Math.Min(w1, w2);
        int minh = Math.Min(h1, h2);

        Pix pixd = Create(minw, minh, 4);
        PixColormap cmap = PixColormap.Create(4);
        cmap.AddColor(255, 255, 255);  /* initialized to white */
        cmap.AddColor(0, 0, 0);
        cmap.AddColor(255, 0, 0);
        cmap.AddColor(0, 255, 0);
        pixd.SetColormap(cmap);

        Pix pixt = And(null, pix1, pix2);
        pixd.PaintThroughMask(pixt, 0, 0, 0x0);  /* black */
        Subtract(pixt, pix1, pix2);
        pixd.PaintThroughMask(pixt, 0, 0, 0xff000000);  /* red */
        Subtract(pixt, pix2, pix1);
        pixd.PaintThroughMask(pixt, 0, 0, 0x00ff0000);  /* green */
        return pixd;
    }

    // leptonica: pixCompareBinary (compare.c)
    /// <summary>Fraction of pixels that differ between two 1 bpp images.</summary>
    /// <param name="pix2">1 bpp</param>
    /// <param name="comptype">L_COMPARE_XOR, L_COMPARE_SUBTRACT</param>
    /// <param name="fract">fraction of pixels that are different</param>
    /// <param name="pixdiff">pix of difference</param>
    /// <remarks>
    /// (1) The two images are aligned at the UL corner, and do not
    ///     need to be the same size.
    /// (2) If using L_COMPARE_SUBTRACT, pix2 is subtracted from pix1.
    /// (3) The total number of pixels is determined by pix1.
    /// (4) On error, the returned fraction is 1.0.
    /// </remarks>
    public void CompareBinary(Pix pix2, int comptype, out float fract, out Pix pixdiff)
    {
        const string func = "pixCompareBinary";
        Pix pix1 = this;
        if (pix1.Depth != 1)
            throw Lept.Arg(func, "pix1 not defined or not 1 bpp");
        if (pix2.Depth != 1)
            throw Lept.Arg(func, "pix2 not defined or not 1 bpp");
        if (comptype != L.L_COMPARE_XOR && comptype != L.L_COMPARE_SUBTRACT)
            throw Lept.Arg(func, "invalid comptype");

        Pix pixt;
        if (comptype == L.L_COMPARE_XOR)
            pixt = Xor(null, pix1, pix2);
        else  /* comptype == L_COMPARE_SUBTRACT) */
            pixt = Subtract(null, pix1, pix2);
        int count = pixt.CountPixels(null);
        pix1.GetDimensions(out int w, out int h, out _);
        fract = (float)(count) / (float)(w * h);
        pixdiff = pixt;
    }

    // leptonica: pixCompareGrayOrRGB (compare.c)
    /// <summary>Compares two gray or rgb images, or colormapped versions of them.</summary>
    /// <param name="pix2">2,4,8,16 bpp gray, 32 bpp rgb, or colormapped</param>
    /// <param name="comptype">L_COMPARE_SUBTRACT, L_COMPARE_ABS_DIFF</param>
    /// <param name="plottype">gplot plot output type, or 0 for no plot</param>
    /// <param name="same">true if pixel values are identical</param>
    /// <param name="diff">average difference</param>
    /// <param name="rmsdiff">rms of difference</param>
    /// <param name="pixdiff">pix of difference</param>
    /// <remarks>
    /// (1) The two images are aligned at the UL corner, and do not
    ///     need to be the same size.  If they are not the same size,
    ///     the comparison will be made over overlapping pixels.
    /// (2) If there is a colormap, it is removed and the result
    ///     is either gray or RGB depending on the colormap.
    /// (3) If RGB, each component is compared separately.
    /// (4) If type is L_COMPARE_ABS_DIFF, pix2 is subtracted from pix1
    ///     and the absolute value is taken.
    /// (5) If type is L_COMPARE_SUBTRACT, pix2 is subtracted from pix1
    ///     and the result is clipped to 0.
    /// (6) The plot output types are specified in gplot.h.
    ///     Use 0 if no difference plot is to be made.
    /// (7) If the images are pixelwise identical, no difference
    ///     plot is made, even if requested.  The result (TRUE or FALSE)
    ///     is optionally returned in the parameter 'same'.
    /// (8) The average difference (either subtracting or absolute value)
    ///     is optionally returned in the parameter 'diff'.
    /// (9) The RMS difference is optionally returned in the
    ///     parameter 'rmsdiff'.  For RGB, we return the average of
    ///     the RMS differences for each of the components.
    /// (10) Because pixel values are compared, pix1 and pix2 can be equal when:
    ///      * they are both gray with different depth
    ///      * one is colormapped and the other is not
    ///      * they are both colormapped and have different size colormaps
    /// </remarks>
    public void CompareGrayOrRGB(Pix pix2, int comptype, int plottype,
                                 out bool same, out float diff, out float rmsdiff,
                                 out Pix? pixdiff)
    {
        const string func = "pixCompareGrayOrRGB";
        Pix pix1 = this;
        if (pix1.Depth == 1)
            throw Lept.Arg(func, "pix1 not defined or 1 bpp");
        if (pix2.Depth == 1)
            throw Lept.Arg(func, "pix2 not defined or 1 bpp");
        if (comptype != L.L_COMPARE_SUBTRACT && comptype != L.L_COMPARE_ABS_DIFF)
            throw Lept.Arg(func, "invalid comptype");
        if (plottype < 0 || plottype >= L.NUM_GPLOT_OUTPUTS)
            throw Lept.Arg(func, "invalid plottype");

        Pix pixt1 = pix1.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        Pix pixt2 = pix2.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        int d1 = pixt1.Depth;
        int d2 = pixt2.Depth;
        Pix pixs1, pixs2;
        if (d1 < 8)
            pixs1 = pixt1.ConvertTo8(false);
        else
            pixs1 = Clone(pixt1);
        if (d2 < 8)
            pixs2 = pixt2.ConvertTo8(false);
        else
            pixs2 = Clone(pixt2);
        d1 = pixs1.Depth;
        d2 = pixs2.Depth;
        if (d1 != d2)
            throw Lept.Arg(func, "intrinsic depths are not equal");

        if (d1 == 8 || d1 == 16)
            pixs1.CompareGray(pixs2, comptype, plottype, out same, out diff, out rmsdiff,
                              out pixdiff);
        else  /* d1 == 32 */
            pixs1.CompareRGB(pixs2, comptype, plottype, out same, out diff, out rmsdiff,
                             out pixdiff);
    }

    // leptonica: pixCompareGray (compare.c)
    /// <summary>Compares two 8 or 16 bpp gray images.</summary>
    /// <param name="pix2">8 or 16 bpp, not cmapped</param>
    /// <param name="comptype">L_COMPARE_SUBTRACT, L_COMPARE_ABS_DIFF</param>
    /// <param name="plottype">gplot plot output type, or 0 for no plot</param>
    /// <param name="same">true if pixel values are identical</param>
    /// <param name="diff">average difference</param>
    /// <param name="rmsdiff">rms of difference</param>
    /// <param name="pixdiff">pix of difference</param>
    /// <remarks>
    /// (1) See pixCompareGrayOrRGB() for details.
    /// (2) Use pixCompareGrayOrRGB() if the input pix are colormapped.
    /// (3) Note: setting %plottype > 0 can result in writing named
    ///           output files.
    /// </remarks>
    public void CompareGray(Pix pix2, int comptype, int plottype,
                            out bool same, out float diff, out float rmsdiff,
                            out Pix pixdiff)
    {
        CompareGrayCore(pix2, comptype, plottype, true, true, true,
                        out same, out diff, out rmsdiff, out Pix? pd);
        pixdiff = pd!;
    }

    private static int compareGrayIndex = 0;

    /* The wantXxx flags stand for the optional output pointers of the C
     * function, which skip the computation of the corresponding result. */
    private void CompareGrayCore(Pix pix2, int comptype, int plottype,
                                 bool wantDiff, bool wantRms, bool wantPix,
                                 out bool same, out float diff, out float rmsdiff,
                                 out Pix? pixdiff)
    {
        const string func = "pixCompareGray";
        Pix pix1 = this;
        same = false;
        diff = 255.0f;
        rmsdiff = 255.0f;
        pixdiff = null;
        int d1 = pix1.Depth;
        int d2 = pix2.Depth;
        if ((d1 != d2) || (d1 != 8 && d1 != 16))
            throw Lept.Arg(func, "depths unequal or not 8 or 16 bpp");
        if (pix1.Colormap != null || pix2.Colormap != null)
            throw Lept.Arg(func, "pix1 and/or pix2 are colormapped");
        if (comptype != L.L_COMPARE_SUBTRACT && comptype != L.L_COMPARE_ABS_DIFF)
            throw Lept.Arg(func, "invalid comptype");
        if (plottype < 0 || plottype >= L.NUM_GPLOT_OUTPUTS)
            throw Lept.Arg(func, "invalid plottype");

        Utils.LeptMkdir("lept/comp");

        Pix pixt;
        if (comptype == L.L_COMPARE_SUBTRACT)
            pixt = SubtractGray(null, pix1, pix2);
        else  /* comptype == L_COMPARE_ABS_DIFF) */
            pixt = pix1.AbsDifference(pix2);

        same = pixt.Zero();
        if (same)
            Log.Info(func, "Images are pixel-wise identical");

        if (wantDiff)
            diff = pixt.GetAverageMasked(null, 0, 0, 1, L.L_MEAN_ABSVAL);

            /* Don't bother to plot if the images are the same */
        if (plottype != 0 && !same) {
            Log.Info(func, "Images differ: output plots will be generated");
            Numa na = pixt.GetGrayHistogram(1);
            na.GetNonzeroRange(TINY, out _, out int last);
            Numa nac = na.ClipToInterval(0, last);
            int index = Interlocked.Increment(ref compareGrayIndex) - 1;
            string buf = CFormat.Sprintf("/tmp/lept/comp/compare_gray%d", index);
            Gplot gplot = Gplot.Create(buf, plottype, "Pixel Difference Histogram",
                                       "diff val", "number of pixels");
            gplot.AddPlot(null, nac, L.GPLOT_LINES, "gray");
            gplot.MakeOutput();
            buf = CFormat.Sprintf("/tmp/lept/comp/compare_gray%d.png", index);
            WriteFile.FileDisplay(buf, 100, 100, 1.0f);
        }

        if (wantPix)
            pixdiff = Copy(null, pixt);

        if (wantRms) {
            if (comptype == L.L_COMPARE_SUBTRACT)  /* wrong type for rms diff */
                pixt = pix1.AbsDifference(pix2);
            rmsdiff = pixt.GetAverageMasked(null, 0, 0, 1, L.L_ROOT_MEAN_SQUARE);
        }
    }

    // leptonica: pixCompareRGB (compare.c)
    /// <summary>Compares two 32 bpp rgb images, component by component.</summary>
    /// <param name="pix2">32 bpp rgb</param>
    /// <param name="comptype">L_COMPARE_SUBTRACT, L_COMPARE_ABS_DIFF</param>
    /// <param name="plottype">gplot plot output type, or 0 for no plot</param>
    /// <param name="same">true if pixel values are identical</param>
    /// <param name="diff">average difference</param>
    /// <param name="rmsdiff">rms of difference</param>
    /// <param name="pixdiff">pix of difference</param>
    /// <remarks>
    /// (1) See pixCompareGrayOrRGB() for details.
    /// (2) Note: setting %plottype > 0 can result in writing named
    ///           output files.
    /// </remarks>
    public void CompareRGB(Pix pix2, int comptype, int plottype,
                           out bool same, out float diff, out float rmsdiff,
                           out Pix pixdiff)
    {
        CompareRGBCore(pix2, comptype, plottype, true, true, true,
                       out same, out diff, out rmsdiff, out Pix? pd);
        pixdiff = pd!;
    }

    private static int compareRGBIndex = 0;

    private void CompareRGBCore(Pix pix2, int comptype, int plottype,
                                bool wantDiff, bool wantRms, bool wantPix,
                                out bool same, out float diff, out float rmsdiff,
                                out Pix? pixdiff)
    {
        const string func = "pixCompareRGB";
        Pix pix1 = this;
        same = false;
        diff = 0.0f;
        rmsdiff = 0.0f;
        pixdiff = null;
        if (pix1.Depth != 32)
            throw Lept.Arg(func, "pix1 not defined or not 32 bpp");
        if (pix2.Depth != 32)
            throw Lept.Arg(func, "pix2 not defined or not ew bpp");
        if (comptype != L.L_COMPARE_SUBTRACT && comptype != L.L_COMPARE_ABS_DIFF)
            throw Lept.Arg(func, "invalid comptype");
        if (plottype < 0 || plottype >= L.NUM_GPLOT_OUTPUTS)
            throw Lept.Arg(func, "invalid plottype");

        Utils.LeptMkdir("lept/comp");

        Pix pixr1 = pix1.GetRGBComponent(L.COLOR_RED);
        Pix pixr2 = pix2.GetRGBComponent(L.COLOR_RED);
        Pix pixg1 = pix1.GetRGBComponent(L.COLOR_GREEN);
        Pix pixg2 = pix2.GetRGBComponent(L.COLOR_GREEN);
        Pix pixb1 = pix1.GetRGBComponent(L.COLOR_BLUE);
        Pix pixb2 = pix2.GetRGBComponent(L.COLOR_BLUE);
        Pix pixr, pixg, pixb;
        if (comptype == L.L_COMPARE_SUBTRACT) {
            pixr = SubtractGray(null, pixr1, pixr2);
            pixg = SubtractGray(null, pixg1, pixg2);
            pixb = SubtractGray(null, pixb1, pixb2);
        } else { /* comptype == L_COMPARE_ABS_DIFF) */
            pixr = pixr1.AbsDifference(pixr2);
            pixg = pixg1.AbsDifference(pixg2);
            pixb = pixb1.AbsDifference(pixb2);
        }

        bool rsame = pixr.Zero();
        bool gsame = pixg.Zero();
        bool bsame = pixb.Zero();
        same = rsame && gsame && bsame;
        if (same)
            Log.Info(func, "Images are pixel-wise identical");

        float rdiff, gdiff, bdiff;
        if (wantDiff) {
            rdiff = pixr.GetAverageMasked(null, 0, 0, 1, L.L_MEAN_ABSVAL);
            gdiff = pixg.GetAverageMasked(null, 0, 0, 1, L.L_MEAN_ABSVAL);
            bdiff = pixb.GetAverageMasked(null, 0, 0, 1, L.L_MEAN_ABSVAL);
            diff = (float)((rdiff + gdiff + bdiff) / 3.0);
        }

            /* Don't bother to plot if the images are the same */
        if (plottype != 0 && !same) {
            Log.Info(func, "Images differ: output plots will be generated");
            Numa nar = pixr.GetGrayHistogram(1);
            Numa nag = pixg.GetGrayHistogram(1);
            Numa nab = pixb.GetGrayHistogram(1);
            nar.GetNonzeroRange(TINY, out _, out int rlast);
            nag.GetNonzeroRange(TINY, out _, out int glast);
            nab.GetNonzeroRange(TINY, out _, out int blast);
            int last = Math.Max(rlast, glast);
            last = Math.Max(last, blast);
            Numa narc = nar.ClipToInterval(0, last);
            Numa nagc = nag.ClipToInterval(0, last);
            Numa nabc = nab.ClipToInterval(0, last);
            int index = Interlocked.Increment(ref compareRGBIndex) - 1;
            string buf = CFormat.Sprintf("/tmp/lept/comp/compare_rgb%d", index);
            Gplot gplot = Gplot.Create(buf, plottype, "Pixel Difference Histogram",
                                       "diff val", "number of pixels");
            gplot.AddPlot(null, narc, L.GPLOT_LINES, "red");
            gplot.AddPlot(null, nagc, L.GPLOT_LINES, "green");
            gplot.AddPlot(null, nabc, L.GPLOT_LINES, "blue");
            gplot.MakeOutput();
            buf = CFormat.Sprintf("/tmp/lept/comp/compare_rgb%d.png", index);
            WriteFile.FileDisplay(buf, 100, 100, 1.0f);
        }

        if (wantPix)
            pixdiff = CreateRGBImage(pixr, pixg, pixb);

        if (wantRms) {
            if (comptype == L.L_COMPARE_SUBTRACT) {
                pixr = pixr1.AbsDifference(pixr2);
                pixg = pixg1.AbsDifference(pixg2);
                pixb = pixb1.AbsDifference(pixb2);
            }
            rdiff = pixr.GetAverageMasked(null, 0, 0, 1, L.L_ROOT_MEAN_SQUARE);
            gdiff = pixg.GetAverageMasked(null, 0, 0, 1, L.L_ROOT_MEAN_SQUARE);
            bdiff = pixb.GetAverageMasked(null, 0, 0, 1, L.L_ROOT_MEAN_SQUARE);
            rmsdiff = (float)((rdiff + gdiff + bdiff) / 3.0);
        }
    }

    // leptonica: pixCompareTiled (compare.c)
    /// <summary>Tile by tile difference of two images.</summary>
    /// <param name="pix2">8 bpp 32 bpp rgb</param>
    /// <param name="sx">tile width; must be &gt; 1</param>
    /// <param name="sy">tile height; must be &gt; 1</param>
    /// <param name="type">L_MEAN_ABSVAL or L_ROOT_MEAN_SQUARE</param>
    /// <returns>pix of difference</returns>
    /// <remarks>
    /// (1) With L_MEAN_ABSVAL, we compute for each tile the
    ///     average abs value of the pixel component difference between
    ///     the two (aligned) images.  With L_ROOT_MEAN_SQUARE, we
    ///     compute instead the rms difference over all components.
    /// (2) The two input pix must be the same depth.  Comparison is made
    ///     using UL corner alignment.
    /// (3) For 32 bpp, the distance between corresponding tiles
    ///     is found by averaging the measured difference over all three
    ///     components of each pixel in the tile.
    /// (4) The result, pixdiff, contains one pixel for each source tile.
    /// </remarks>
    public Pix CompareTiled(Pix pix2, int sx, int sy, int type)
    {
        const string func = "pixCompareTiled";
        Pix pix1 = this;
        int d1 = pix1.Depth;
        int d2 = pix2.Depth;
        if (d1 != d2)
            throw Lept.Arg(func, "depths not equal");
        if (d1 != 8 && d1 != 32)
            throw Lept.Arg(func, "pix1 not 8 or 32 bpp");
        if (d2 != 8 && d2 != 32)
            throw Lept.Arg(func, "pix2 not 8 or 32 bpp");
        if (sx < 2 || sy < 2)
            throw Lept.Arg(func, "sx and sy not both > 1");
        if (type != L.L_MEAN_ABSVAL && type != L.L_ROOT_MEAN_SQUARE)
            throw Lept.Arg(func, "invalid type");

        Pix pixt = pix1.AbsDifference(pix2);
        Pix pixdiff;
        if (d1 == 8) {
            pixdiff = pixt.GetAverageTiled(sx, sy, type);
        } else {  /* d1 == 32 */
            Pix pixr = pixt.GetRGBComponent(L.COLOR_RED);
            Pix pixg = pixt.GetRGBComponent(L.COLOR_GREEN);
            Pix pixb = pixt.GetRGBComponent(L.COLOR_BLUE);
            Pix pixrdiff = pixr.GetAverageTiled(sx, sy, type);
            Pix pixgdiff = pixg.GetAverageTiled(sx, sy, type);
            Pix pixbdiff = pixb.GetAverageTiled(sx, sy, type);
            pixrdiff.GetDimensions(out int w, out int h, out _);
            Pixacc pixacc = Pixacc.Create(w, h, 0);
            pixacc.Add(pixrdiff);
            pixacc.Add(pixgdiff);
            pixacc.Add(pixbdiff);
            pixacc.MultConst(1.0f / 3.0f);
            pixdiff = pixacc.Final(8);
        }
        return pixdiff;
    }

    /*------------------------------------------------------------------*
     *            Other measures of the difference of two images        *
     *------------------------------------------------------------------*/
    // leptonica: pixCompareRankDifference (compare.c)
    /// <summary>Rank function of the pixel differences of two images.</summary>
    /// <param name="pix2">8 bpp gray or 32 bpp rgb, or colormapped</param>
    /// <param name="factor">subsampling factor; use 0 or 1 for no subsampling</param>
    /// <returns>numa of rank difference</returns>
    /// <remarks>
    /// (1) This answers the question: if the pixel values in each
    ///     component are compared by absolute difference, for
    ///     any value of difference, what is the fraction of
    ///     pixel pairs that have a difference of this magnitude
    ///     or greater.  For a difference of 0, the fraction is 1.0.
    ///     In this sense, it is a mapping from pixel difference to
    ///     rank order of difference.
    /// (2) The two images are aligned at the UL corner, and do not
    ///     need to be the same size.  If they are not the same size,
    ///     the comparison will be made over overlapping pixels.
    /// (3) If there is a colormap, it is removed and the result
    ///     is either gray or RGB depending on the colormap.
    /// (4) If RGB, pixel differences for each component are aggregated
    ///     into a single histogram.
    /// </remarks>
    public Numa CompareRankDifference(Pix pix2, int factor)
    {
        Numa nah = GetDifferenceHistogram(pix2, factor);

        Numa nan = nah.NormalizeHistogram(1.0f);
        float[] array1 = nan.GetFArray(L.L_NOCOPY);

        Numa nad = Numa.Create(256);
        nad.Count = 256;  /* all initialized to 0.0 */
        float[] array2 = nad.GetFArray(L.L_NOCOPY);

            /* Do rank accumulation on normalized histo of diffs */
        array2[0] = 1.0f;
        for (int i = 1; i < 256; i++)
            array2[i] = array2[i - 1] - array1[i - 1];

        return nad;
    }

    // leptonica: pixTestForSimilarity (compare.c)
    /// <summary>Tests whether two images of the same size are similar.</summary>
    /// <param name="pix2">8 bpp gray or 32 bpp rgb, or colormapped</param>
    /// <param name="factor">subsampling factor; use 0 or 1 for no subsampling</param>
    /// <param name="mindiff">minimum pixel difference to be counted; &gt; 0</param>
    /// <param name="maxfract">maximum fraction of pixels allowed to have
    ///     diff greater than or equal to mindiff</param>
    /// <param name="maxave">maximum average difference of pixels allowed for
    ///     pixels with diff greater than or equal to
    ///     mindiff, after subtracting mindiff</param>
    /// <param name="details">use 1 to give normalized histogram and other data</param>
    /// <returns>true if similar</returns>
    /// <remarks>
    /// (1) This takes 2 pix that are the same size and determines using
    ///     3 input parameters if they are "similar".  The first parameter
    ///     %mindiff establishes a criterion of pixel-to-pixel similarity:
    ///     two pixels are not similar if their difference in value is
    ///     at least mindiff.  Then %maxfract and %maxave are thresholds
    ///     on the number and distribution of dissimilar pixels
    ///     allowed for the two pix to be similar.   If the pix are
    ///     to be similar, neither threshold can be exceeded.
    /// (2) In setting the %maxfract and %maxave thresholds, you have
    ///     these options:
    ///       (a) Base the comparison only on %maxfract.  Then set
    ///           %maxave = 0.0 or 256.0.  (If 0, we always ignore it.)
    ///       (b) Base the comparison only on %maxave.  Then set
    ///           %maxfract = 1.0.
    ///       (c) Base the comparison on both thresholds.
    /// (3) Example of values that can be expected at mindiff = 15 when
    ///     comparing lossless png encoding with jpeg encoding, q=75:
    ///        (smoothish bg)       fractdiff = 0.01, avediff = 2.5
    ///        (natural scene)      fractdiff = 0.13, avediff = 3.5
    ///     To identify these images as 'similar', select maxfract
    ///     and maxave to be upper bounds of what you expect.
    /// (4) See pixGetDifferenceStats() for a discussion of why we subtract
    ///     mindiff from the computed average diff of the nonsimilar pixels
    ///     to get the 'avediff' returned by that function.
    /// (5) If there is a colormap, it is removed and the result
    ///     is either gray or RGB depending on the colormap.
    /// (6) If RGB, the maximum difference between pixel components is
    ///     saved in the histogram.
    /// </remarks>
    public bool TestForSimilarity(Pix pix2, int factor, int mindiff, float maxfract,
                                  float maxave, int details)
    {
        const string func = "pixTestForSimilarity";
        if (!SizesEqual(this, pix2))
            throw Lept.Arg(func, "pix sizes not equal");
        if (mindiff <= 0)
            throw Lept.Arg(func, "mindiff must be > 0");

        GetDifferenceStats(pix2, factor, mindiff, out float fractdiff, out float avediff,
                           details);

        bool similar = false;
        if (maxave <= 0.0) maxave = 256.0f;
        if (fractdiff <= maxfract && avediff <= maxave)
            similar = true;
        return similar;
    }

    // leptonica: pixGetDifferenceStats (compare.c)
    /// <summary>Describes the difference between two images by a fraction and an average.</summary>
    /// <param name="pix2">8 bpp gray or 32 bpp rgb, or colormapped</param>
    /// <param name="factor">subsampling factor; use 0 or 1 for no subsampling</param>
    /// <param name="mindiff">minimum pixel difference to be counted; &gt; 0</param>
    /// <param name="fractdiff">fraction of pixels with diff greater than or equal to mindiff</param>
    /// <param name="avediff">average difference of pixels with diff greater
    ///     than or equal to mindiff, less mindiff</param>
    /// <param name="details">use 1 to give normalized histogram and other data</param>
    /// <remarks>
    /// (1) This takes a threshold %mindiff and describes the difference
    ///     between two images in terms of two numbers:
    ///       (a) the fraction of pixels, %fractdiff, whose difference
    ///           equals or exceeds the threshold %mindiff, and
    ///       (b) the average value %avediff of the difference in pixel value
    ///           for the pixels in the set given by (a), after you subtract
    ///           %mindiff.  The reason for subtracting %mindiff is that
    ///           you then get a useful measure for the rate of falloff
    ///           of the distribution for larger differences.  For example,
    ///           if %mindiff = 10 and you find that %avediff = 2.5, it
    ///           says that of the pixels with diff > 10, the average of
    ///           their diffs is just mindiff + 2.5 = 12.5.  This is a
    ///           fast falloff in the histogram with increasing difference.
    /// (2) The two images are aligned at the UL corner, and do not
    ///     need to be the same size.  If they are not the same size,
    ///     the comparison will be made over overlapping pixels.
    /// (3) If there is a colormap, it is removed and the result
    ///     is either gray or RGB depending on the colormap.
    /// (4) If RGB, the maximum difference between pixel components is
    ///     saved in the histogram.
    /// (5) Set %details == 1 to see the difference histogram and get
    ///     an output that shows for each value of %mindiff, what are the
    ///     minimum values required for fractdiff and avediff in order
    ///     that the two pix will be considered similar.
    /// </remarks>
    public void GetDifferenceStats(Pix pix2, int factor, int mindiff,
                                   out float fractdiff, out float avediff, int details)
    {
        const string func = "pixGetDifferenceStats";
        fractdiff = 0.0f;
        avediff = 0.0f;
        if (mindiff <= 0)
            throw Lept.Arg(func, "mindiff must be > 0");

        Numa nah = GetDifferenceHistogram(pix2, factor);
        Numa nan = nah.NormalizeHistogram(1.0f);
        float[] array = nan.GetFArray(L.L_NOCOPY);

        float fract, ave;
        if (details != 0) {
            Utils.LeptMkdir("lept/comp");
            nan.GetNonzeroRange(0.0f, out int first, out int last);
            Numa nac = nan.ClipToInterval(first, last);
            Gplot.Simple1(nac, L.GPLOT_PNG, "/tmp/lept/comp/histo",
                          "Difference histogram");
            WriteFile.FileDisplay("/tmp/lept/comp/histo.png", 500, 0, 1.0f);
            Utils.LeptStderr("\nNonzero values in normalized histogram:");
            nac.WriteStderr();
            Utils.LeptStderr(" Mindiff      fractdiff      avediff\n");
            Utils.LeptStderr(" -----------------------------------\n");
            for (int diff = 1; diff < Math.Min(2 * mindiff, last); diff++) {
                fract = 0.0f;
                ave = 0.0f;
                for (int i = diff; i <= last; i++) {
                    fract += array[i];
                    ave += (float)i * array[i];
                }
                ave = (fract == 0.0) ? 0.0f : ave / fract;
                ave -= diff;
                Utils.LeptStderr("%5d         %7.4f        %7.4f\n",
                                 diff, fract, ave);
            }
            Utils.LeptStderr(" -----------------------------------\n");
        }

        fract = 0.0f;
        ave = 0.0f;
        for (int i = mindiff; i < 256; i++) {
            fract += array[i];
            ave += (float)i * array[i];
        }
        ave = (fract == 0.0) ? 0.0f : ave / fract;
        ave -= mindiff;

        fractdiff = fract;
        avediff = ave;
    }

    // leptonica: pixGetDifferenceHistogram (compare.c)
    /// <summary>Histogram of the differences of the pixel values of two images.</summary>
    /// <param name="pix2">8 bpp gray or 32 bpp rgb, or colormapped</param>
    /// <param name="factor">subsampling factor; use 0 or 1 for no subsampling</param>
    /// <returns>Numa of histogram of differences</returns>
    /// <remarks>
    /// (1) The two images are aligned at the UL corner, and do not
    ///     need to be the same size.  If they are not the same size,
    ///     the comparison will be made over overlapping pixels.
    /// (2) If there is a colormap, it is removed and the result
    ///     is either gray or RGB depending on the colormap.
    /// (3) If RGB, the maximum difference between pixel components is
    ///     saved in the histogram.
    /// </remarks>
    public Numa GetDifferenceHistogram(Pix pix2, int factor)
    {
        const string func = "pixGetDifferenceHistogram";
        Pix pix1 = this;
        int d1 = pix1.Depth;
        int d2 = pix2.Depth;
        if (d1 == 16 || d2 == 16)
            throw Lept.Arg(func, "d == 16 not supported");
        if (d1 < 8 && pix1.Colormap == null)
            throw Lept.Arg(func, "pix1 depth < 8 bpp and not cmapped");
        if (d2 < 8 && pix2.Colormap == null)
            throw Lept.Arg(func, "pix2 depth < 8 bpp and not cmapped");
        Pix pixt1 = pix1.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        Pix pixt2 = pix2.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        pixt1.GetDimensions(out int w1, out int h1, out d1);
        pixt2.GetDimensions(out int w2, out int h2, out d2);
        if (d1 != d2)
            throw Lept.Arg(func, "pix depths not equal");
        if (factor < 1) factor = 1;

        Numa na = Numa.Create(256);
        na.Count = 256;  /* all initialized to 0.0 */
        float[] array = na.GetFArray(L.L_NOCOPY);
        int w = Math.Min(w1, w2);
        int h = Math.Min(h1, h2);
        uint[] data1 = pixt1.Data;
        uint[] data2 = pixt2.Data;
        int wpl1 = pixt1.Wpl;
        int wpl2 = pixt2.Wpl;
        if (d1 == 8) {
            for (int i = 0; i < h; i += factor) {
                ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl1);
                ReadOnlySpan<uint> line2 = data2.AsSpan(i * wpl2);
                for (int j = 0; j < w; j += factor) {
                    int val1 = ArrayAccess.GetDataByte(line1, j);
                    int val2 = ArrayAccess.GetDataByte(line2, j);
                    int val = Math.Abs(val1 - val2);
                    array[val]++;
                }
            }
        } else {  /* d1 == 32 */
            for (int i = 0; i < h; i += factor) {
                int line1 = i * wpl1;
                int line2 = i * wpl2;
                for (int j = 0; j < w; j += factor) {
                    ExtractRGBValues(data1[line1 + j], out int rval1, out int gval1, out int bval1);
                    ExtractRGBValues(data2[line2 + j], out int rval2, out int gval2, out int bval2);
                    int rdiff = Math.Abs(rval1 - rval2);
                    int gdiff = Math.Abs(gval1 - gval2);
                    int bdiff = Math.Abs(bval1 - bval2);
                    int maxdiff = Math.Max(rdiff, gdiff);
                    maxdiff = Math.Max(maxdiff, bdiff);
                    array[maxdiff]++;
                }
            }
        }

        return na;
    }

    // leptonica: pixGetPerceptualDiff (compare.c)
    /// <summary>Fraction of pixels that differ after a small dilation of each image.</summary>
    /// <param name="pixs2">8 bpp gray or 32 bpp rgb, or colormapped</param>
    /// <param name="sampling">subsampling factor; use 0 or 1 for no subsampling</param>
    /// <param name="dilation">size of grayscale or color Sel; odd</param>
    /// <param name="mindiff">minimum pixel difference to be counted; &gt; 0</param>
    /// <param name="fract">fraction of pixels with diff greater than mindiff</param>
    /// <param name="pixdiff1">showing difference (gray or color)</param>
    /// <param name="pixdiff2">showing pixels of sufficient diff</param>
    /// <returns>false if the depths are unequal or not in {8,32}</returns>
    /// <remarks>
    /// (1) This takes 2 pix and determines, using 2 input parameters:
    ///      * %dilation specifies the amount of grayscale or color
    ///        dilation to apply to the images, to compensate for
    ///        a small amount of misregistration.  A typical number might
    ///        be 5, which uses a 5x5 Sel.  Grayscale dilation expands
    ///        lighter pixels into darker pixel regions.
    ///      * %mindiff determines the threshold on the difference in
    ///        pixel values to be counted -- two pixels are not similar
    ///        if their difference in value is at least %mindiff.  For
    ///        color pixels, we use the maximum component difference.
    /// (2) The pixelwise comparison is always done with the UL corners
    ///     aligned.  The sizes of pix1 and pix2 need not be the same,
    ///     although in practice it can be useful to scale to the same size.
    /// (3) If there is a colormap, it is removed and the result
    ///     is either gray or RGB depending on the colormap.
    /// (4) Two optional diff images can be retrieved (typ. for debugging):
    ///      pixdiff1: the gray or color difference
    ///      pixdiff2: thresholded to 1 bpp for pixels exceeding %mindiff
    /// (5) The returned value of fract can be compared to some threshold,
    ///     which is application dependent.
    /// (6) This method is in analogy to the two-sided hausdorff transform,
    ///     except here it is for d > 1.  For d == 1 (see pixRankHaustest()),
    ///     we verify that when one pix1 is dilated, it covers at least a
    ///     given fraction of the pixels in pix2, and v.v.; in that
    ///     case, the two pix are sufficiently similar.  Here, we
    ///     do an analogous thing: subtract the dilated pix1 from pix2 to
    ///     get a 1-sided hausdorff-like transform.  Then do it the
    ///     other way.  Take the component-wise max of the two results,
    ///     and threshold to get the fraction of pixels with a difference
    ///     below the threshold.
    /// </remarks>
    public bool GetPerceptualDiff(Pix pixs2, int sampling, int dilation, int mindiff,
                                  out float fract, out Pix? pixdiff1, out Pix? pixdiff2)
    {
        const string func = "pixGetPerceptualDiff";
        Pix pixs1 = this;
        pixdiff1 = null;
        pixdiff2 = null;
        fract = 1.0f;  /* init to completely different */
        if ((dilation & 1) == 0)
            throw Lept.Arg(func, "dilation must be odd");
        int d1 = pixs1.Depth;
        int d2 = pixs2.Depth;
        if (pixs1.Colormap == null && d1 < 8)
            throw Lept.Arg(func, "pixs1 not cmapped and < 8 bpp");
        if (pixs2.Colormap == null && d2 < 8)
            throw Lept.Arg(func, "pixs2 not cmapped and < 8 bpp");

            /* Integer downsample if requested */
        Pix pix1, pix2;
        if (sampling > 1) {
            pix1 = pixs1.ScaleByIntSampling(sampling);
            pix2 = pixs2.ScaleByIntSampling(sampling);
        } else {
            pix1 = Clone(pixs1);
            pix2 = Clone(pixs2);
        }

            /* Remove colormaps */
        Pix pix3, pix4;
        if (pix1.Colormap != null) {
            pix3 = pix1.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            d1 = pix3.Depth;
        } else {
            pix3 = Clone(pix1);
        }
        if (pix2.Colormap != null) {
            pix4 = pix2.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            d2 = pix4.Depth;
        } else {
            pix4 = Clone(pix2);
        }
        if (d1 != d2 || (d1 != 8 && d1 != 32)) {
            Log.Info(func, CFormat.Sprintf("depths unequal or not in {8,32}: d1 = %d, d2 = %d",
                                           d1, d2));
            return false;
        }

            /* In each direction, do a small dilation and subtract the dilated
             * image from the other image to get a one-sided difference.
             * Then take the max of the differences for each direction
             * and clipping each component to 255 if necessary.  Note that
             * for RGB images, the dilations and max selection are done
             * component-wise, and the conversion to grayscale also uses the
             * maximum component.  The resulting grayscale images are
             * thresholded using %mindiff. */
        Pix pix5, pix6, pix9;
        Pix? pix7, pix8;
        if (d1 == 8) {
            pix5 = pix3.DilateGray(dilation, dilation);
            pix4.CompareGrayCore(pix5, L.L_COMPARE_SUBTRACT, 0, false, false, true,
                                 out _, out _, out _, out pix7);
            pix6 = pix4.DilateGray(dilation, dilation);
            pix3.CompareGrayCore(pix6, L.L_COMPARE_SUBTRACT, 0, false, false, true,
                                 out _, out _, out _, out pix8);
            pix9 = MinOrMax(null, pix7!, pix8!, L.L_CHOOSE_MAX);
            Pix pix10 = pix9.ThresholdToBinary(mindiff);
            Invert(pix10, pix10);
            int count = pix10.CountPixels(null);
            pix10.GetDimensions(out int w, out int h, out _);
            fract = (w <= 0 || h <= 0) ? 0.0f :
                                         (float)count / (float)(w * h);
            pixdiff1 = pix9;
            pixdiff2 = pix10;
        } else {  /* d1 == 32 */
            pix5 = pix3.ColorMorph(L.L_MORPH_DILATE, dilation, dilation);
            pix4.CompareRGBCore(pix5, L.L_COMPARE_SUBTRACT, 0, false, false, true,
                                out _, out _, out _, out pix7);
            pix6 = pix4.ColorMorph(L.L_MORPH_DILATE, dilation, dilation);
            pix3.CompareRGBCore(pix6, L.L_COMPARE_SUBTRACT, 0, false, false, true,
                                out _, out _, out _, out pix8);
            pix9 = MinOrMax(null, pix7!, pix8!, L.L_CHOOSE_MAX);
            Pix pix10 = pix9.ConvertRGBToGrayMinMax(L.L_CHOOSE_MAX);
            Pix pix11 = pix10.ThresholdToBinary(mindiff);
            Invert(pix11, pix11);
            int count = pix11.CountPixels(null);
            pix11.GetDimensions(out int w, out int h, out _);
            fract = (w <= 0 || h <= 0) ? 0.0f :
                                         (float)count / (float)(w * h);
            pixdiff1 = pix9;
            pixdiff2 = pix11;
        }
        return true;
    }

    // leptonica: pixGetPSNR (compare.c)
    /// <summary>Power signal/noise ratio, in dB, of the difference between two images.</summary>
    /// <param name="pix2">8 or 32 bpp; no colormap</param>
    /// <param name="factor">sampling factor; &gt;= 1</param>
    /// <returns>power signal/noise ratio difference</returns>
    /// <remarks>
    /// (1) This computes the power S/N ratio, in dB, for the difference
    ///     between two images.  By convention, the power S/N
    ///     for a grayscale image is ('log' == log base 10,
    ///     and 'ln == log base e):
    ///       PSNR = 10 * log((255/MSE)^2)
    ///            = 4.3429 * ln((255/MSE)^2)
    ///            = -4.3429 * ln((MSE/255)^2)
    ///     where MSE is the mean squared error.
    ///     Here are some examples:
    ///        MSE             PSNR
    ///        ---             ----
    ///        10              28.1
    ///        3               38.6
    ///        1               48.1
    ///        0.1             68.1
    /// (2) If pix1 and pix2 have the same pixel values, the MSE = 0.0
    ///     and the PSNR is infinity.  For that case, this returns
    ///     PSNR = 1000, which corresponds to the very small MSE of
    ///     about 10^(-48).
    /// </remarks>
    public float GetPSNR(Pix pix2, int factor)
    {
        const string func = "pixGetPSNR";
        Pix pix1 = this;
        if (!SizesEqual(pix1, pix2))
            throw Lept.Arg(func, "pix sizes unequal");
        if (pix1.Colormap != null)
            throw Lept.Arg(func, "pix1 has colormap");
        if (pix2.Colormap != null)
            throw Lept.Arg(func, "pix2 has colormap");
        pix1.GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pix not 8 or 32 bpp");
        if (factor < 1)
            throw Lept.Arg(func, "invalid sampling factor");

        bool same = Equal(pix1, pix2);
        if (same)
            return 1000.0f;  /* crazy big exponent */

        uint[] data1 = pix1.Data;
        uint[] data2 = pix2.Data;
        int wpl1 = pix1.Wpl;
        int wpl2 = pix2.Wpl;
        float mse = 0.0f;  /* mean squared error */
        if (d == 8) {
            for (int i = 0; i < h; i += factor) {
                ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl1);
                ReadOnlySpan<uint> line2 = data2.AsSpan(i * wpl2);
                for (int j = 0; j < w; j += factor) {
                    int v1 = ArrayAccess.GetDataByte(line1, j);
                    int v2 = ArrayAccess.GetDataByte(line2, j);
                    mse += (float)(v1 - v2) * (v1 - v2);
                }
            }
        } else {  /* d == 32 */
            for (int i = 0; i < h; i += factor) {
                int line1 = i * wpl1;
                int line2 = i * wpl2;
                for (int j = 0; j < w; j += factor) {
                    ExtractRGBValues(data1[line1 + j], out int r1, out int g1, out int b1);
                    ExtractRGBValues(data2[line2 + j], out int r2, out int g2, out int b2);
                    mse = (float)(mse + ((float)(r1 - r2) * (r1 - r2) +
                                         (g1 - g2) * (g1 - g2) +
                                         (b1 - b2) * (b1 - b2)) / 3.0);
                }
            }
        }
        mse = mse / ((float)(w) * h);

        return (float)(-4.3429448 * Math.Log(mse / (255 * 255)));
    }

    /*------------------------------------------------------------------*
     *             Comparison of photo regions by histogram             *
     *------------------------------------------------------------------*/
    // leptonica: pixComparePhotoRegionsByHisto (compare.c)
    /// <summary>Compares two grayscale photo regions by tiled histograms.</summary>
    /// <param name="pix2">any depth; colormap OK</param>
    /// <param name="box1">[optional] photo region from pix1; can be null</param>
    /// <param name="box2">[optional] photo region from pix2; can be null</param>
    /// <param name="minratio">requiring sizes be compatible; &lt; 1.0</param>
    /// <param name="factor">subsampling factor; &gt;= 1</param>
    /// <param name="n">in range {1, ... 7}. n^2 is the maximum number
    ///     of subregions for histograms; typ. n = 3.</param>
    /// <param name="debugflag">true for debug output</param>
    /// <returns>similarity score of histograms</returns>
    /// <remarks>
    /// (1) This function compares two grayscale photo regions.  If a
    ///     box is given, the region is clipped; otherwise assume
    ///     the entire images are photo regions.  This is done with a
    ///     set of not more than n^2 spatially aligned histograms, which are
    ///     aligned using the centroid of the inverse image.
    /// (2) The parameter %n specifies the "side" of an n x n grid
    ///     of subimages.  If the subimages have an aspect ratio larger
    ///     than 2, the grid will change, using n^2 as a maximum for
    ///     the number of subimages.  For example, if n == 3, but the
    ///     image is 600 x 200 pixels, a 3x3 grid would have subimages
    ///     of 200 x 67 pixels, which is more than 2:1, so we change
    ///     to a 4x2 grid where each subimage has 150 x 100 pixels.
    /// (3) An initial filter gives %score = 0 if the ratio of widths
    ///     and heights (smallest / largest) does not exceed a
    ///     threshold %minratio.  This must be between 0.5 and 1.0.
    ///     If set at 1.0, both images must be exactly the same size.
    ///     A typical value for %minratio is 0.9.
    /// (4) Because this function should not be used on text or
    ///     line graphics, which can give false positive results
    ///     (i.e., high scores for different images), filter the images
    ///     using pixGenPhotoHistos(), which returns tiled histograms
    ///     only if an image is not text and comparison is expected
    ///     to work with histograms.  If either image fails the test,
    ///     the comparison returns a score of 0.0.
    /// (5) The white value counts in the histograms are removed; they
    ///     are typically pixels that were padded to achieve alignment.
    /// (6) For an efficient representation of the histogram, normalize
    ///     using a multiplicative factor so that the number in the
    ///     maximum bucket is 255.  It then takes 256 bytes to store.
    /// (7) When comparing the histograms of two regions, use the
    ///     Earth Mover distance (EMD), with the histograms normalized
    ///     so that the sum over bins is the same.  Further normalize
    ///     by dividing by 255, so that the result is in [0.0 ... 1.0].
    /// (8) Get a similarity score S = 1.0 - k * D, where
    ///       k is a constant, say in the range 5-10
    ///       D = normalized EMD
    ///     and for multiple tiles, take the Min(S) to be the final score.
    ///     Using aligned tiles gives protection against accidental
    ///     similarity of the overall grayscale histograms.
    ///     A small number of aligned tiles works well.
    /// (9) With debug on, you get a pdf that shows, for each tile,
    ///     the images, histograms and score.
    /// </remarks>
    public float ComparePhotoRegionsByHisto(Pix pix2, Box? box1, Box? box2,
                                            float minratio, int factor, int n,
                                            bool debugflag)
    {
        const string func = "pixComparePhotoRegionsByHisto";
        Pix pix1 = this;
        if (minratio < 0.5 || minratio > 1.0)
            throw Lept.Arg(func, "minratio not in [0.5 ... 1.0]");
        if (factor < 1)
            throw Lept.Arg(func, "subsampling factor must be >= 1");
        if (n < 1 || n > 7) {
            Log.Warning(func, CFormat.Sprintf("n = %d is invalid; setting to 4", n));
            n = 4;
        }

        int debugindex = 0;
        if (debugflag) {
            Utils.LeptMkdir("lept/comp");
            debugindex = 666;  /* arbitrary number used for naming output */
        }

            /* Initial filter by size */
        int w1, h1, w2, h2;
        if (box1 != null)
            box1.GetGeometry(out _, out _, out w1, out h1);
        else
            pix1.GetDimensions(out w1, out h1, out _);
        if (box2 != null)
            box2.GetGeometry(out _, out _, out w2, out h2);
        else
            // NOTE(upstream): uses the size of pix1 instead of pix2 when box2 is null
            pix1.GetDimensions(out w2, out h2, out _);
        float wratio = (w1 < w2) ? (float)w1 / (float)w2 :
                       (float)w2 / (float)w1;
        float hratio = (h1 < h2) ? (float)h1 / (float)h2 :
                       (float)h2 / (float)h1;
        if (wratio < minratio || hratio < minratio)
            return 0.0f;

            /* Initial crop, if necessary, and make histos */
        Pix pix3;
        if (box1 != null)
            pix3 = pix1.ClipRectangle(box1, out _)!;
        else
            pix3 = Clone(pix1);
        Numaa? naa1 = pix3.GenPhotoHistos(null, factor, 0.0f, n, out int w1c, out int h1c,
                                          debugindex);
        if (naa1 == null) return 0.0f;
        Pix pix4;
        if (box2 != null)
            pix4 = pix2.ClipRectangle(box2, out _)!;
        else
            pix4 = Clone(pix2);
        Numaa? naa2 = pix4.GenPhotoHistos(null, factor, 0.0f, n, out int w2c, out int h2c,
                                          debugindex);
        if (naa2 == null) return 0.0f;

            /* Compare histograms */
        Pixa? pixa = debugflag ? Pixa.Create(0) : null;
        return CompareTilesByHisto(naa1, naa2, minratio, w1c, h1c, w2c, h2c, pixa);
    }

    // leptonica: pixGenPhotoHistos (compare.c)
    /// <summary>Makes the tiled gray histograms of a photo, or null if it is not a photo.</summary>
    /// <param name="box">[optional] region to be selected; can be null</param>
    /// <param name="factor">subsampling; &gt;= 1</param>
    /// <param name="thresh">threshold for photo/text; use 0 for default</param>
    /// <param name="n">in range {1, ... 7}. n^2 is the maximum number
    ///     of subregions for histograms; typ. n = 3.</param>
    /// <param name="w">width of image used to make histograms</param>
    /// <param name="h">height of image used to make histograms</param>
    /// <param name="debugindex">0 for no debugging; positive integer otherwise</param>
    /// <returns>nx * ny 256-entry gray histograms, or null if not a photo</returns>
    /// <remarks>
    /// (1) This crops and converts to 8 bpp if necessary.  It adds a
    ///     minimal white boundary such that the centroid of the
    ///     photo-inverted image is in the center. This allows
    ///     automatic alignment with histograms of other image regions.
    /// (2) The parameter %n specifies the "side" of the n x n grid
    ///     of subimages.  If the subimages have an aspect ratio larger
    ///     than 2, the grid will change, using n^2 as a maximum for
    ///     the number of subimages.  For example, if n == 3, but the
    ///     image is 600 x 200 pixels, a 3x3 grid would have subimages
    ///     of 200 x 67 pixels, which is more than 2:1, so we change
    ///     to a 4x2 grid where each subimage has 150 x 100 pixels.
    /// (3) The white value in the histogram is removed, because of
    ///     the padding.
    /// (4) Use 0 for conservative default (1.3) for thresh.
    /// (5) For an efficient representation of the histogram, normalize
    ///     using a multiplicative factor so that the number in the
    ///     maximum bucket is 255.  It then takes 256 bytes to store.
    /// (6) With %debugindex > 0, this makes a pdf that shows, for each tile,
    ///     the images and histograms.
    /// </remarks>
    public Numaa? GenPhotoHistos(Box? box, int factor, float thresh, int n,
                                 out int w, out int h, int debugindex)
    {
        const string func = "pixGenPhotoHistos";
        Pix pixs = this;
        w = 0;
        h = 0;
        if (pixs.Depth == 1)
            throw Lept.Arg(func, "pixs not defined or 1 bpp");
        if (factor < 1)
            throw Lept.Arg(func, "subsampling factor must be >= 1");
        if (thresh <= 0.0) thresh = 1.3f;  /* default */
        if (n < 1 || n > 7) {
            Log.Warning(func, CFormat.Sprintf("n = %d is invalid; setting to 4", n));
            n = 4;
        }

        Pixa? pixa = null;
        if (debugindex > 0) {
            pixa = Pixa.Create(0);
            Utils.LeptMkdir("lept/comp");
        }

            /* Initial crop, if necessary */
        Pix pix1;
        if (box != null)
            pix1 = pixs.ClipRectangle(box, out _)!;
        else
            pix1 = Clone(pixs);

            /* Convert to 8 bpp and pad to center the centroid */
        Pix pix2 = pix1.ConvertTo8(false);
        Pix pix3 = pix2.PadToCenterCentroid(factor);

            /* Set to 255 all pixels above 230.  Do this so that light gray
             * pixels do not enter into the comparison. */
        Pix pixm = pix3.ThresholdToBinary(230);
        Invert(pixm, pixm);
        pix3.SetMaskedGeneral(pixm, 255, 0, 0);

        if (debugindex > 0) {
            Pix pix4 = pix2.ConvertTo32();
            Pix pix5 = pix3.ConvertTo32();
            Pix pix6 = pix4.ScaleToSize(400, 0);
            Pix pix7 = pix5.ScaleToSize(400, 0);
            Pixa pixa2 = Pixa.Create(2);
            pixa2.AddPix(pix6, L.L_INSERT);
            pixa2.AddPix(pix7, L.L_INSERT);
            Pix pix8 = pixa2.DisplayTiledInRows(32, 1000, 1.0f, 0, 50, 3);
            pixa!.AddPix(pix8, L.L_INSERT);
        }

            /* Test if this is a photoimage */
        Numaa? naa = pix3.DecideIfPhotoImage(factor, thresh, n, pixa);
        if (naa != null) {
            w = pix3.Width;
            h = pix3.Height;
        }

        if (pixa != null) {
            string buf = CFormat.Sprintf("/tmp/lept/comp/tiledhistos.%d.pdf", debugindex);
            Utils.LeptStderr("Writing to %s\n", buf);
            pixa.ConvertToPdf(300, 1.0f, L.L_FLATE_ENCODE, 0, null, buf);
        }

        return naa;
    }

    // leptonica: pixPadToCenterCentroid (compare.c)
    /// <summary>Pads with white so that the centroid of the photometric inverse is in the center.</summary>
    /// <param name="factor">subsampling for centroid; &gt;= 1</param>
    /// <returns>pixd padded with white pixels</returns>
    /// <remarks>
    /// (1) This add minimum white padding to an 8 bpp pix, such that
    ///     the centroid of the photometric inverse is in the center of
    ///     the resulting image.  Thus in computing the centroid,
    ///     black pixels have weight 255, and white pixels have weight 0.
    /// </remarks>
    public Pix PadToCenterCentroid(int factor)
    {
        const string func = "pixPadToCenterCentroid";
        Pix pixs = this;
        if (factor < 1)
            throw Lept.Arg(func, "invalid sampling factor");

        Pix pix1 = pixs.ConvertTo8(false);
        pix1.Centroid8(factor, out float cx, out float cy);
        int icx = (int)(cx + 0.5);
        int icy = (int)(cy + 0.5);
        pix1.GetDimensions(out int ws, out int hs, out _);
        int delx = ws - 2 * icx;
        int dely = hs - 2 * icy;
        int xs = Math.Max(0, delx);
        int ys = Math.Max(0, dely);
        int wd = 2 * Math.Max(icx, ws - icx);
        int hd = 2 * Math.Max(icy, hs - icy);
        Pix pixd = Create(wd, hd, 8);
        pixd.SetAll();  /* to white */
        pixd.CopyResolution(pixs);
        pixd.Rasterop(xs, ys, ws, hs, L.PIX_SRC, pix1, 0, 0);
        return pixd;
    }

    // leptonica: pixCentroid8 (compare.c)
    /// <summary>Centroid of the photometric inverse of an 8 bpp image.</summary>
    /// <param name="factor">subsampling factor; &gt;= 1</param>
    /// <param name="cx">x value of centroid</param>
    /// <param name="cy">y value of centroid</param>
    /// <remarks>
    /// (1) This first does a photometric inversion (black = 255, white = 0).
    ///     It then finds the centroid of the result.  The inversion is
    ///     done because white is usually background, so the centroid
    ///     is computed based on the "foreground" gray pixels, and the
    ///     darker the pixel, the more weight it is given.
    /// </remarks>
    public void Centroid8(int factor, out float cx, out float cy)
    {
        const string func = "pixCentroid8";
        Pix pixs = this;
        cx = 0.0f;
        cy = 0.0f;
        if (pixs.Depth != 8)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");
        if (factor < 1)
            throw Lept.Arg(func, "subsampling factor must be >= 1");

        pixs.GetDimensions(out int w, out int h, out _);
        uint[] data = pixs.Data;
        int wpl = pixs.Wpl;
        float sumx = 0.0f, sumy = 0.0f, sumv = 0.0f;
        for (int i = 0; i < h; i++) {
            ref uint lineRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(data), (nuint)(i * wpl));
            for (int j = 0; j < w; j++) {
                int val = 255 - ArrayAccess.GetDataByteUnchecked(ref lineRef, j);
                sumx += val * j;
                sumy += val * i;
                sumv += val;
            }
        }

        if (sumv == 0) {
            Log.Info(func, "input image is white");
            cx = (float)(w) / 2;
            cy = (float)(h) / 2;
        } else {
            cx = sumx / sumv;
            cy = sumy / sumv;
        }
    }

    // leptonica: pixDecideIfPhotoImage (compare.c)
    /// <summary>Decides if an 8 bpp image is a photo, and makes its tiled histograms.</summary>
    /// <param name="factor">subsampling for histograms; &gt;= 1</param>
    /// <param name="thresh">threshold for photo/text; use 0 for default</param>
    /// <param name="n">in range {1, ... 7}. n^2 is the maximum number
    ///     of subregions for histograms; typ. n = 3.</param>
    /// <param name="pixadebug">[optional] use only for debug output</param>
    /// <returns>array of normalized histograms, or null if not a photo</returns>
    /// <remarks>
    /// (1) The input image must be 8 bpp (no colormap), and padded with
    ///     white pixels so the centroid of photo-inverted pixels is at
    ///     the center of the image.
    /// (2) The parameter %n specifies the "side" of the n x n grid
    ///     of subimages.  If the subimages have an aspect ratio larger
    ///     than 2, the grid will change, using n^2 as a maximum for
    ///     the number of subimages.  For example, if n == 3, but the
    ///     image is 600 x 200 pixels, a 3x3 grid would have subimages
    ///     of 200 x 67 pixels, which is more than 2:1, so we change
    ///     to a 4x2 grid where each subimage has 150 x 100 pixels.
    /// (3) If the pix is not almost certainly a photoimage, the returned
    ///     histograms (%naa) are null.
    /// (4) If histograms are generated, the white (255) count is set
    ///     to 0.  This removes all pixels values above 230, including
    ///     white padding from the centroid matching operation, from
    ///     consideration.  The resulting histograms are then normalized
    ///     so the maximum count is 255.
    /// (5) Default for %thresh is 1.3; this seems sufficiently conservative.
    /// (6) Use %pixadebug == NULL unless debug output is requested.
    /// </remarks>
    public Numaa? DecideIfPhotoImage(int factor, float thresh, int n, Pixa? pixadebug)
    {
        const string func = "pixDecideIfPhotoImage";
        Pix pix = this;
        if (pix.Depth != 8 || pix.Colormap != null)
            throw Lept.Arg(func, "pix undefined or invalid");
        if (n < 1 || n > 7) {
            Log.Warning(func, CFormat.Sprintf("n = %d is invalid; setting to 4", n));
            n = 4;
        }
        if (thresh <= 0.0) thresh = 1.3f;  /* default */

            /* Look for text lines */
        int istext = pix.DecideIfText(null, pixadebug);
        if (istext != 0) {
            Log.Info(func, "Image is text");
            return null;
        }

            /* Determine grid from n */
        pix.GetDimensions(out int w, out int h, out _);
        if (w == 0 || h == 0)
            throw Lept.Arg(func, "invalid pix dimension");
        FindHistoGridDimensions(n, w, h, out int nx, out int ny, 1);

            /* Evaluate histograms in each tile */
        Pixa pixa1 = Pixa.SplitPix(pix, nx, ny, 0, 0);
        int ngrids = nx * ny;
        L_Bmf? bmf = (pixadebug != null) ? L_Bmf.Create(null, 6) : null;
        Numaa naa = Numaa.Create(ngrids);
        if (pixadebug != null) {
            Utils.LeptRmdir("lept/compplot");
            Utils.LeptMkdir("lept/compplot");
        }
        Pix pix1;
        for (int i = 0; i < ngrids; i++) {
            pix1 = pixa1.GetPix(i, L.L_CLONE);

                /* Get histograms, set white count to 0, normalize max to 255 */
            Numa na1 = pix1.GetGrayHistogram(factor);
            na1.SetValue(255, 0);
            Numa na2 = na1.WindowedMean(5);  /* do some smoothing */
            na2.GetMax(out float maxval, out _);
            Numa na3 = na2.Transform(0, 255.0f / maxval);
            if (pixadebug != null) {
                string buf = CFormat.Sprintf("/tmp/lept/compplot/plot.%d", i);
                Gplot.Simple1(na3, L.GPLOT_PNG, buf, "Histos");
            }

            naa.AddNuma(na3, L.L_INSERT);
        }
        if (pixadebug != null) {
            pix1 = pixa1.DisplayTiledInColumns(nx, 1.0f, 30, 2);
            pixadebug.AddPix(pix1, L.L_INSERT);
            Pixa pixa2 = Pixa.ReadFiles("/tmp/lept/compplot", ".png");
            Pixa pixa3 = pixa2.Scale(0.4f, 0.4f);
            pix1 = pixa3.DisplayTiledInColumns(nx, 1.0f, 30, 2);
            pixadebug.AddPix(pix1, L.L_INSERT);
        }

            /* Compute the standard deviation between these histos to decide
             * if the image is photo or something more like line art,
             * which does not support good comparison by tiled histograms.  */
        Numa.GrayInterHistogramStats(naa, 5, out _, out _, out _, out Numa narv);

            /* For photos, the root variance has a larger weight of
             * values in the range [50 ... 150] compared to [200 ... 230],
             * than text or line art.  For the latter, most of the variance
             * between tiles is in the lightest parts of the image, well
             * above 150.  */
        float sum1 = narv.GetSumOnInterval(50, 150);
        float sum2 = narv.GetSumOnInterval(200, 230);
        float ratio;
        bool isphoto;
        if (sum2 == 0.0) {  /* shouldn't happen */
            ratio = 0.001f;  /* anything very small for debug output */
            isphoto = false;  /* be conservative */
        } else {
            ratio = sum1 / sum2;
            isphoto = (ratio > thresh);
        }
        if (pixadebug != null) {
            if (isphoto)
                Log.Info(func, CFormat.Sprintf("ratio %f > %f; isphoto is true", ratio, thresh));
            else
                Log.Info(func, CFormat.Sprintf("ratio %f < %f; isphoto is false", ratio, thresh));
        }
        return isphoto ? naa : null;
    }

    // leptonica: findHistoGridDimensions (compare.c)
    /// <remarks>
    /// (1) This determines the number of subdivisions to be used on
    ///     the image in each direction.  A histogram will be built
    ///     for each subimage.
    /// (2) The parameter %n specifies the "side" of the n x n grid
    ///     of subimages.  If the subimages have an aspect ratio larger
    ///     than 2, the grid will change, using n^2 as a maximum for
    ///     the number of subimages.  For example, if n == 3, but the
    ///     image is 600 x 200 pixels, a 3x3 grid would have subimages
    ///     of 200 x 67 pixels, which is more than 2:1, so we change
    ///     to a 4x2 grid where each subimage has 150 x 100 pixels.
    /// </remarks>
    private static void FindHistoGridDimensions(int n, int w, int h,
                                                out int pnx, out int pny, int debug)
    {
        float ratio = (float)w / (float)h;
        int max = n * n;
        int nx = n, ny = n;
        while (nx > 1 && ny > 1) {
            if (ratio > 2.0) {  /* reduce ny */
                ny--;
                nx = max / ny;
                if (debug != 0)
                    Utils.LeptStderr("nx = %d, ny = %d, ratio w/h = %4.2f\n",
                                     nx, ny, ratio);
            } else if (ratio < 0.5) {  /* reduce nx */
                nx--;
                ny = max / nx;
                if (debug != 0)
                    Utils.LeptStderr("nx = %d, ny = %d, ratio w/h = %4.2f\n",
                                     nx, ny, ratio);
            } else {  /* we're ok */
                if (debug != 0)
                    Utils.LeptStderr("nx = %d, ny = %d, ratio w/h = %4.2f\n",
                                     nx, ny, ratio);
                break;
            }
            ratio = (float)(ny * w) / (float)(nx * h);
        }
        pnx = nx;
        pny = ny;
    }

    // leptonica: compareTilesByHisto (compare.c)
    /// <summary>Compares two sets of tiled gray histograms.</summary>
    /// <param name="naa1">a set of 256 entry histograms</param>
    /// <param name="naa2">a set of 256 entry histograms</param>
    /// <param name="minratio">requiring image sizes be compatible; &lt; 1.0</param>
    /// <param name="w1">image sizes from which histograms were made</param>
    /// <param name="h1">image sizes from which histograms were made</param>
    /// <param name="w2">image sizes from which histograms were made</param>
    /// <param name="h2">image sizes from which histograms were made</param>
    /// <param name="pixadebug">[optional] use only for debug output</param>
    /// <returns>similarity score of histograms</returns>
    /// <remarks>
    /// (1) naa1 and naa2 must be generated using pixGenPhotoHistos(),
    ///     using the same tile sizes.
    /// (2) The image dimensions must be similar.  The score is 0.0
    ///     if the ratio of widths and heights (smallest / largest)
    ///     exceeds a threshold %minratio, which must be between
    ///     0.5 and 1.0.  If set at 1.0, both images must be exactly
    ///     the same size.  A typical value for %minratio is 0.9.
    /// (3) The input pixadebug is null unless debug output is requested.
    /// </remarks>
    public static float CompareTilesByHisto(Numaa naa1, Numaa naa2, float minratio,
                                            int w1, int h1, int w2, int h2,
                                            Pixa? pixadebug)
    {
        const string func = "compareTilesByHisto";

            /* Filter for different sizes */
        float wratio = (w1 < w2) ? (float)w1 / (float)w2 :
                       (float)w2 / (float)w1;
        float hratio = (h1 < h2) ? (float)h1 / (float)h2 :
                       (float)h2 / (float)h1;
        if (wratio < minratio || hratio < minratio) {
            if (pixadebug != null)
                Log.Info(func, CFormat.Sprintf("Sizes differ: wratio = %f, hratio = %f",
                                               wratio, hratio));
            return 0.0f;
        }
        int n = naa1.Count;
        if (n != naa2.Count) {  /* due to differing w/h ratio */
            Log.Info(func, "naa1 and naa2 sizes are different");
            return 0.0f;
        }

        if (pixadebug != null) {
            Utils.LeptRmdir("lept/comptile");
            Utils.LeptMkdir("lept/comptile");
        }

            /* Evaluate histograms in each tile.  Remove white before
             * computing EMD, because there are may be a lot of white
             * pixels due to padding, and we don't want to include them.
             * This also makes the debug histo plots more informative. */
        float minscore = 1.0f;
        Numa nadist = Numa.Create(n);
        Numa nascore = Numa.Create(n);
        L_Bmf? bmf = (pixadebug != null) ? L_Bmf.Create(null, 6) : null;
        for (int i = 0; i < n; i++) {
            Numa na1 = naa1.GetNuma(i, L.L_CLONE);
            Numa na2 = naa2.GetNuma(i, L.L_CLONE);
            na1.SetValue(255, 0.0f);
            na2.SetValue(255, 0.0f);

                /* To compare histograms, use the normalized earthmover distance.
                 * Further normalize to get the EM distance as a fraction of the
                 * maximum distance in the histogram (255).  Finally, scale this
                 * up by 10.0, and subtract from 1.0 to get a similarity score. */
            float dist = na1.EarthMoverDistance(na2);
            float score = (float)Math.Max(0.0, 1.0 - 10.0 * (dist / 255.0));
            nadist.AddNumber(dist);
            nascore.AddNumber(score);
            minscore = Math.Min(minscore, score);
            if (pixadebug != null) {
                string buf1 = CFormat.Sprintf("/tmp/lept/comptile/plot.%d", i);
                Gplot.Simple2(na1, na2, L.GPLOT_PNG, buf1, "Histos");
            }
        }
        float pscore = minscore;

        if (pixadebug != null) {
            for (int i = 0; i < n; i++) {
                string buf1 = CFormat.Sprintf("/tmp/lept/comptile/plot.%d.png", i);
                Pix pix1 = Read(buf1);
                float dist = nadist.GetFValue(i);
                float score = nascore.GetFValue(i);
                string buf2 = CFormat.Sprintf("Image %d\ndist = %5.3f, score = %5.3f",
                                              i, dist, score);
                Pix pix2 = pix1.AddTextlines(bmf!, buf2, 0x0000ff00, L.L_ADD_BELOW);
                pixadebug.AddPix(pix2, L.L_INSERT);
            }
            Utils.LeptStderr("Writing to /tmp/lept/comptile/comparegray.pdf\n");
            pixadebug.ConvertToPdf(300, 1.0f, L.L_FLATE_ENCODE, 0, null,
                                   "/tmp/lept/comptile/comparegray.pdf");
            nascore.WriteDebug("/tmp/lept/comptile/scores.na");
            nadist.WriteDebug("/tmp/lept/comptile/dists.na");
        }

        return pscore;
    }

    // leptonica: pixCompareGrayByHisto (compare.c)
    /// <summary>Compares two grayscale photo regions by (tiled) gray histograms.</summary>
    /// <param name="pix2">any depth; colormap OK</param>
    /// <param name="box1">[optional] region selected from pix1; can be null</param>
    /// <param name="box2">[optional] region selected from pix2; can be null</param>
    /// <param name="minratio">requiring sizes be compatible; &lt; 1.0</param>
    /// <param name="maxgray">max value to keep in histo; &gt;= 200, 255 to keep all</param>
    /// <param name="factor">subsampling factor; &gt;= 1</param>
    /// <param name="n">in range {1, ... 7}. n^2 is the maximum number
    ///     of subregions for histograms; typ. n = 3.</param>
    /// <param name="debugflag">true for debug output</param>
    /// <returns>similarity score of histograms</returns>
    /// <remarks>
    /// (1) This function compares two grayscale photo regions.  It can
    ///     do it with a single histogram from each region, or with a
    ///     set of spatially aligned histograms.  For both cases,
    ///     align the regions using the centroid of the inverse image,
    ///     and crop to the smallest of the two.
    /// (2) The parameter %n specifies the "side" of an n x n grid
    ///     of subimages.  If the subimages have an aspect ratio larger
    ///     than 2, the grid will change, using n^2 as a maximum for
    ///     the number of subimages.  For example, if n == 3, but the
    ///     image is 600 x 200 pixels, a 3x3 grid would have subimages
    ///     of 200 x 67 pixels, which is more than 2:1, so we change
    ///     to a 4x2 grid where each subimage has 150 x 100 pixels.
    /// (3) An initial filter gives %score = 0 if the ratio of widths
    ///     and heights (smallest / largest) does not exceed a
    ///     threshold %minratio.  This must be between 0.5 and 1.0.
    ///     If set at 1.0, both images must be exactly the same size.
    ///     A typical value for %minratio is 0.9.
    /// (4) The lightest values in the histogram can be disregarded.
    ///     Set %maxgray to the lightest value to be kept.  For example,
    ///     to eliminate white (255), set %maxgray = 254.  %maxgray must
    ///     be >= 200.
    /// (5) For an efficient representation of the histogram, normalize
    ///     using a multiplicative factor so that the number in the
    ///     maximum bucket is 255.  It then takes 256 bytes to store.
    /// (6) When comparing the histograms of two regions:
    ///     ~ Use %maxgray = 254 to ignore the white pixels, the number
    ///       of which may be sensitive to the crop region if the pixels
    ///       outside that region are white.
    ///     ~ Use the Earth Mover distance (EMD), with the histograms
    ///       normalized so that the sum over bins is the same.
    ///       Further normalize by dividing by 255, so that the result
    ///       is in [0.0 ... 1.0].
    /// (7) Get a similarity score S = 1.0 - k * D, where
    ///       k is a constant, say in the range 5-10
    ///       D = normalized EMD
    ///     and for multiple tiles, take the Min(S) to be the final score.
    ///     Using aligned tiles gives protection against accidental
    ///     similarity of the overall grayscale histograms.
    ///     A small number of aligned tiles works well.
    /// (8) With debug on, you get a pdf that shows, for each tile,
    ///     the images, histograms and score.
    /// (9) When to use:
    ///     (a) Because this function should not be used on text or
    ///         line graphics, which can give false positive results
    ///         (i.e., high scores for different images), the input
    ///         images should be filtered.
    ///     (b) To filter, first use pixDecideIfText().  If that function
    ///         says the image is text, do not use it.  If the function
    ///         says it is not text, it still may be line graphics, and
    ///         in that case, use:
    ///            pixGetGrayHistogramTiled()
    ///            grayInterHistogramStats()
    ///         to determine whether it is photo or line graphics.
    /// </remarks>
    public float CompareGrayByHisto(Pix pix2, Box? box1, Box? box2, float minratio,
                                    int maxgray, int factor, int n, bool debugflag)
    {
        const string func = "pixCompareGrayByHisto";
        Pix pix1 = this;
        if (minratio < 0.5 || minratio > 1.0)
            throw Lept.Arg(func, "minratio not in [0.5 ... 1.0]");
        if (maxgray < 200)
            throw Lept.Arg(func, "invalid maxgray; should be >= 200");
        maxgray = Math.Min(255, maxgray);
        if (factor < 1)
            throw Lept.Arg(func, "subsampling factor must be >= 1");
        if (n < 1 || n > 7) {
            Log.Warning(func, CFormat.Sprintf("n = %d is invalid; setting to 4", n));
            n = 4;
        }

        if (debugflag)
            Utils.LeptMkdir("lept/comp");

            /* Initial filter by size */
        int w1, h1, w2, h2;
        if (box1 != null)
            box1.GetGeometry(out _, out _, out w1, out h1);
        else
            pix1.GetDimensions(out w1, out h1, out _);
        if (box2 != null)
            box2.GetGeometry(out _, out _, out w2, out h2);
        else
            // NOTE(upstream): uses the size of pix1 instead of pix2 when box2 is null
            pix1.GetDimensions(out w2, out h2, out _);
        float wratio = (w1 < w2) ? (float)w1 / (float)w2 :
                       (float)w2 / (float)w1;
        float hratio = (h1 < h2) ? (float)h1 / (float)h2 :
                       (float)h2 / (float)h1;
        if (wratio < minratio || hratio < minratio)
            return 0.0f;

            /* Initial crop, if necessary */
        Pix pix3, pix4;
        if (box1 != null)
            pix3 = pix1.ClipRectangle(box1, out _)!;
        else
            pix3 = Clone(pix1);
        if (box2 != null)
            pix4 = pix2.ClipRectangle(box2, out _)!;
        else
            pix4 = Clone(pix2);

            /* Convert to 8 bpp, align centroids and do maximal crop */
        Pix pix5 = pix3.ConvertTo8(false);
        Pix pix6 = pix4.ConvertTo8(false);
        pix5.CropAlignedToCentroid(pix6, factor, out Box box3, out Box box4);
        Pix pix7 = pix5.ClipRectangle(box3, out _)!;
        Pix pix8 = pix6.ClipRectangle(box4, out _)!;
        Pixa? pixa = debugflag ? Pixa.Create(0) : null;
        if (debugflag) {
            Pix pix9 = pix5.ConvertTo32();
            Pix pix10 = pix6.ConvertTo32();
            pix9.RenderBoxArb(box3, 2, 255, 0, 0);
            pix10.RenderBoxArb(box4, 2, 255, 0, 0);
            Pix pix11 = pix9.ScaleToSize(400, 0);
            Pix pix12 = pix10.ScaleToSize(400, 0);
            Pixa pixa2 = Pixa.Create(2);
            pixa2.AddPix(pix11, L.L_INSERT);
            pixa2.AddPix(pix12, L.L_INSERT);
            Pix pix13 = pixa2.DisplayTiledInRows(32, 1000, 1.0f, 0, 50, 0);
            pixa!.AddPix(pix13, L.L_INSERT);
        }

            /* Tile and compare histograms */
        return CompareTilesByHisto(pix7, pix8, maxgray, factor, n, pixa);
    }

    // leptonica: pixCompareTilesByHisto (compare.c)
    /// <remarks>
    /// (1) This static function is only called from pixCompareGrayByHisto().
    ///     The input images have been converted to 8 bpp if necessary,
    ///     aligned and cropped.
    /// (2) The input pixadebug is null unless debug output is requested.
    /// (3) See pixCompareGrayByHisto() for details.
    /// </remarks>
    private static float CompareTilesByHisto(Pix pix1, Pix pix2, int maxgray, int factor,
                                             int n, Pixa? pixadebug)
    {
            /* Determine grid from n */
        pix1.GetDimensions(out int w, out int h, out _);
        FindHistoGridDimensions(n, w, h, out int nx, out int ny, 1);
        int ngr = nx * ny;

            /* Evaluate histograms in each tile */
        Pixa pixa1 = Pixa.SplitPix(pix1, nx, ny, 0, 0);
        Pixa pixa2 = Pixa.SplitPix(pix2, nx, ny, 0, 0);
        Numa? na7 = (pixadebug != null) ? Numa.Create(ngr) : null;
        L_Bmf? bmf = (pixadebug != null) ? L_Bmf.Create(null, 6) : null;
        float minscore = 1.0f;
        for (int i = 0; i < ngr; i++) {
            Pix pix3 = pixa1.GetPix(i, L.L_CLONE);
            Pix pix4 = pixa2.GetPix(i, L.L_CLONE);

                /* Get histograms, set white count to 0, normalize max to 255 */
            Numa na1 = pix3.GetGrayHistogram(factor);
            Numa na2 = pix4.GetGrayHistogram(factor);
            if (maxgray < 255) {
                for (int j = maxgray + 1; j <= 255; j++) {
                    na1.SetValue(j, 0);
                    na2.SetValue(j, 0);
                }
            }
            Numa na3 = na1.WindowedMean(5);
            Numa na4 = na2.WindowedMean(5);
            na3.GetMax(out float maxval1, out _);
            na4.GetMax(out float maxval2, out _);
            Numa na5 = na3.Transform(0, 255.0f / maxval1);
            Numa na6 = na4.Transform(0, 255.0f / maxval2);
            if (pixadebug != null) {
                Gplot.Simple2(na5, na6, L.GPLOT_PNG, "/tmp/lept/comp/plot1", "Histos");
            }

                /* To compare histograms, use the normalized earthmover distance.
                 * Further normalize to get the EM distance as a fraction of the
                 * maximum distance in the histogram (255).  Finally, scale this
                 * up by 10.0, and subtract from 1.0 to get a similarity score. */
            float dist = na5.EarthMoverDistance(na6);
            float score = (float)Math.Max(0.0, 1.0 - 8.0 * (dist / 255.0));
            if (pixadebug != null) na7!.AddNumber(score);
            minscore = Math.Min(minscore, score);
            if (pixadebug != null) {
                Pixa pixa3 = Pixa.Create(3);
                pix3.GetDimensions(out int w3, out int h3, out _);
                int wscale = (w3 > h3) ? 700 : 400;
                Pix pix5 = pix3.ScaleToSize(wscale, 0);
                Pix pix6 = pix4.ScaleToSize(wscale, 0);
                pixa3.AddPix(pix5, L.L_INSERT);
                pixa3.AddPix(pix6, L.L_INSERT);
                Pix pix7 = Read("/tmp/lept/comp/plot1.png");
                Pix pix8 = pix7.ScaleToSize(700, 0);
                string buf = CFormat.Sprintf("%5.3f", score);
                Pix pix9 = pix8.AddTextlines(bmf!, buf, 0x0000ff00, L.L_ADD_RIGHT);
                pixa3.AddPix(pix9, L.L_INSERT);
                Pix pix10 = pixa3.DisplayTiledInRows(32, 1000, 1.0f, 0, 50, 0);
                pixadebug.AddPix(pix10, L.L_INSERT);
            }
        }
        float pscore = minscore;

        if (pixadebug != null) {
            pixadebug.ConvertToPdf(300, 1.0f, L.L_FLATE_ENCODE, 0, null,
                                   "/tmp/lept/comp/comparegray.pdf");
            na7!.WriteDebug("/tmp/lept/comp/tilescores.na");
        }

        return pscore;
    }

    // leptonica: pixCropAlignedToCentroid (compare.c)
    /// <summary>Maximum crop boxes for two images with aligned centroids.</summary>
    /// <param name="pix2">any depth; colormap OK</param>
    /// <param name="factor">subsampling; &gt;= 1</param>
    /// <param name="box1">crop box for pix1</param>
    /// <param name="box2">crop box for pix2</param>
    /// <remarks>
    /// (1) This finds the maximum crop boxes for two 8 bpp images when
    ///     their centroids of their photometric inverses are aligned.
    ///     Black pixels have weight 255; white pixels have weight 0.
    /// </remarks>
    public void CropAlignedToCentroid(Pix pix2, int factor, out Box box1, out Box box2)
    {
        const string func = "pixCropAlignedToCentroid";
        Pix pix1 = this;
        if (factor < 1)
            throw Lept.Arg(func, "subsampling factor must be >= 1");

        Pix pix3 = pix1.ConvertTo8(false);
        Pix pix4 = pix2.ConvertTo8(false);
        pix3.Centroid8(factor, out float cx1, out float cy1);
        pix4.Centroid8(factor, out float cx2, out float cy2);
        pix3.GetDimensions(out int w1, out int h1, out _);
        pix4.GetDimensions(out int w2, out int h2, out _);

        int icx1 = (int)(cx1 + 0.5);
        int icy1 = (int)(cy1 + 0.5);
        int icx2 = (int)(cx2 + 0.5);
        int icy2 = (int)(cy2 + 0.5);
        int xm = Math.Min(icx1, icx2);
        int xm1 = icx1 - xm;
        int xm2 = icx2 - xm;
        int xp = Math.Min(w1 - icx1, w2 - icx2);  /* one pixel beyond to the right */
        int xp1 = icx1 + xp;
        int xp2 = icx2 + xp;
        int ym = Math.Min(icy1, icy2);
        int ym1 = icy1 - ym;
        int ym2 = icy2 - ym;
        int yp = Math.Min(h1 - icy1, h2 - icy2);  /* one pixel below the bottom */
        int yp1 = icy1 + yp;
        int yp2 = icy2 + yp;
        box1 = Box.Create(xm1, ym1, xp1 - xm1, yp1 - ym1);
        box2 = Box.Create(xm2, ym2, xp2 - xm2, yp2 - ym2);
    }

    // leptonica: l_compressGrayHistograms (compare.c)
    /// <summary>Compresses a set of 256-entry gray histograms to a byte array.</summary>
    /// <param name="naa">set of 256-entry histograms</param>
    /// <param name="w">size of image</param>
    /// <param name="h">size of image</param>
    /// <returns>the byte array</returns>
    /// <remarks>
    /// (1) This first writes w and h to the byte array as 4 byte ints.
    /// (2) Then it normalizes each histogram to a max value of 255,
    ///     and saves each value as a byte.  If there are
    ///     N histograms, the output bytearray has 8 + 256 * N bytes.
    /// (3) Further compression of the array with zlib yields only about
    ///     a 25% decrease in size, so we don't bother.  If size reduction
    ///     were important, a lossy transform using a 1-dimensional DCT
    ///     would be effective, because we don't care about the fine
    ///     details of these histograms.
    /// </remarks>
    public static byte[] CompressGrayHistograms(Numaa naa, int w, int h)
    {
        const string func = "l_compressGrayHistograms";
        int n = naa.Count;
        for (int i = 0; i < n; i++) {
            int nn = naa.GetNumaCount(i);
            if (nn != 256) {
                Log.Error(func, CFormat.Sprintf("%d numbers in numa[%d]", nn, i));
                throw Lept.Fail(func, "histogram does not have 256 numbers");
            }
        }

        byte[] bytea = new byte[8 + 256 * n];
        BinaryPrimitives.WriteUInt32LittleEndian(bytea.AsSpan(0, 4), (uint)w);
        BinaryPrimitives.WriteUInt32LittleEndian(bytea.AsSpan(4, 4), (uint)h);
        for (int i = 0; i < n; i++) {
            Numa na1 = naa.GetNuma(i, L.L_COPY);
            na1.GetMax(out float maxval, out _);
            Numa na2 = na1.Transform(0, 255.0f / maxval);
            for (int j = 0; j < 256; j++) {
                int ival = na2.GetIValue(j);
                bytea[8 + 256 * i + j] = (byte)ival;
            }
        }

        return bytea;
    }

    // leptonica: l_uncompressGrayHistograms (compare.c)
    /// <summary>Makes the set of 256-entry gray histograms from the byte array of l_compressGrayHistograms().</summary>
    /// <param name="bytea">byte array of size 8 + 256 * N, N an integer</param>
    /// <param name="w">width of the image that generated the histograms</param>
    /// <param name="h">height of the image</param>
    /// <returns>numaa representing N histograms, each with 256 bins</returns>
    /// <remarks>
    /// (1) The first 8 bytes are read as two 32-bit ints.
    /// (2) Then this constructs a numaa representing some number of
    ///     gray histograms that are normalized such that the max value
    ///     in each histogram is 255.  The data is stored as a byte
    ///     array, with 256 bytes holding the data for each histogram.
    ///     Each gray histogram was computed from a tile of a grayscale image.
    /// </remarks>
    public static Numaa UncompressGrayHistograms(byte[] bytea, out int w, out int h)
    {
        const string func = "l_uncompressGrayHistograms";
        w = 0;
        h = 0;
        ulong size = (ulong)bytea.Length;
        int n = (int)((size - 8) / 256);
        if ((size - 8) % 256 != 0)
            throw Lept.Arg(func, "bytea size is invalid");

        w = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytea.AsSpan(0, 4));
        h = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytea.AsSpan(4, 4));
        Numaa naa = Numaa.Create(n);
        for (int i = 0; i < n; i++) {
            Numa na = Numa.Create(256);
            for (int j = 0; j < 256; j++)
                na.AddNumber(bytea[8 + 256 * i + j]);
            naa.AddNuma(na, L.L_INSERT);
        }

        return naa;
    }

    /*------------------------------------------------------------------*
     *             Translated images at the same resolution             *
     *------------------------------------------------------------------*/
    // leptonica: pixCompareWithTranslation (compare.c)
    /// <summary>Finds the translation that best aligns two images, by correlation.</summary>
    /// <param name="pix2">any depth; colormap OK</param>
    /// <param name="thresh">threshold for converting to 1 bpp</param>
    /// <param name="delx">x translation on pix2 to align with pix1</param>
    /// <param name="dely">y translation on pix2 to align with pix1</param>
    /// <param name="score">correlation score at best alignment</param>
    /// <param name="debugflag">true for debug output</param>
    /// <remarks>
    /// (1) This does a coarse-to-fine search for best translational
    ///     alignment of two images, measured by a scoring function
    ///     that is the correlation between the fg pixels.
    /// (2) The threshold is used if the images aren't 1 bpp.
    /// (3) With debug on, you get a pdf that shows, as a grayscale
    ///     image, the score as a function of shift from the initial
    ///     estimate, for each of the four levels.  The shift is 0 at
    ///     the center of the image.
    /// (4) With debug on, you also get a pdf that shows the
    ///     difference at the best alignment between the two images,
    ///     at each of the four levels.  The red and green pixels
    ///     show locations where one image has a fg pixel and the
    ///     other doesn't.  The black pixels are where both images
    ///     have fg pixels, and white pixels are where neither image
    ///     has fg pixels.
    /// </remarks>
    public void CompareWithTranslation(Pix pix2, int thresh, out int delx, out int dely,
                                       out float score, bool debugflag)
    {
        Pix pix1 = this;
        delx = 0;
        dely = 0;
        score = 0.0f;

            /* Make tables */
        byte[] subtab = MakeSubsampleTab2x();
        int[] stab = MakePixelSumTab8();
        int[] ctab = MakePixelCentroidTab8();

            /* Binarize each image */
        Pix pixb1 = pix1.ConvertTo1(thresh);
        Pix pixb2 = pix2.ConvertTo1(thresh);

            /* Make a cascade of 2x reduced images for each, thresholding
             * with level 2 (neutral), down to 8x reduction */
        Pixa pixa1 = Pixa.Create(4);
        Pixa pixa2 = Pixa.Create(4);
        Pixa? pixadb = null;
        if (debugflag)
            pixadb = Pixa.Create(4);
        pixa1.AddPix(pixb1, L.L_INSERT);
        pixa2.AddPix(pixb2, L.L_INSERT);
        for (int i = 0; i < 3; i++) {
            Pix pixt1 = pixb1.ReduceRankBinary2(2, subtab);
            Pix pixt2 = pixb2.ReduceRankBinary2(2, subtab);
            pixa1.AddPix(pixt1, L.L_INSERT);
            pixa2.AddPix(pixt2, L.L_INSERT);
            pixb1 = pixt1;
            pixb2 = pixt2;
        }

            /* At the lowest level, use the centroids with a maxshift of 6
             * to search for the best alignment.  Then at higher levels,
             * use the result from the level below as the initial approximation
             * for the alignment, and search with a maxshift of 2. */
        for (int level = 3; level >= 0; level--) {
            Pix pixt1 = pixa1.GetPix(level, L.L_CLONE);
            Pix pixt2 = pixa2.GetPix(level, L.L_CLONE);
            int area1 = pixt1.CountPixels(stab);
            int area2 = pixt2.CountPixels(stab);
            int etransx, etransy, maxshift;
            if (level == 3) {
                pixt1.Centroid(ctab, stab, out float cx1, out float cy1);
                pixt2.Centroid(ctab, stab, out float cx2, out float cy2);
                etransx = Utils.RoundFToI(cx1 - cx2);
                etransy = Utils.RoundFToI(cy1 - cy2);
                maxshift = 6;
            } else {
                etransx = 2 * delx;
                etransy = 2 * dely;
                maxshift = 2;
            }
            int dbint = debugflag ? level + 1 : 0;
            pixt1.BestCorrelation(pixt2, area1, area2, etransx, etransy,
                                  maxshift, stab, out delx, out dely, out score, dbint);
            if (debugflag) {
                Utils.LeptStderr("Level %d: delx = %d, dely = %d, score = %7.4f\n",
                                 level, delx, dely, score);
                pixt2.RasteropIP(delx, dely, L.L_BRING_IN_WHITE);
                Pix pixt3 = pixt1.DisplayDiffBinary(pixt2);
                Pix pixt4 = pixt3.ExpandReplicate(8 / (1 << (3 - level)));
                pixadb!.AddPix(pixt4, L.L_INSERT);
            }
        }

        if (debugflag) {
            pixadb!.ConvertToPdf(300, 1.0f, L.L_FLATE_ENCODE, 0, null,
                                 "/tmp/lept/comp/compare.pdf");
            PdfIo.ConvertFilesToPdf("/tmp/lept/comp", "correl_", 30, 1.0f, L.L_FLATE_ENCODE,
                                    0, "Correlation scores at levels 1 through 5",
                                    "/tmp/lept/comp/correl.pdf");
        }
    }

    // leptonica: pixBestCorrelation (compare.c)
    /// <summary>Finds the shift of pix2 that maximizes the correlation with pix1.</summary>
    /// <param name="pix2">1 bpp</param>
    /// <param name="area1">number of on pixels in pix1</param>
    /// <param name="area2">number of on pixels in pix2</param>
    /// <param name="etransx">estimated x translation of pix2 to align with pix1</param>
    /// <param name="etransy">estimated y translation of pix2 to align with pix1</param>
    /// <param name="maxshift">max x and y shift of pix2, around the estimated
    ///     alignment location, relative to pix1</param>
    /// <param name="tab8">[optional] sum tab for ON pixels in byte; can be null</param>
    /// <param name="delx">best x shift of pix2 relative to pix1</param>
    /// <param name="dely">best y shift of pix2 relative to pix1</param>
    /// <param name="score">maximum score found</param>
    /// <param name="debugflag">&lt;= 0 to skip; positive to generate output.
    ///     The integer is used to label the debug image.</param>
    /// <remarks>
    /// (1) This maximizes the correlation score between two 1 bpp images,
    ///     by starting with an estimate of the alignment
    ///     (%etransx, %etransy) and computing the correlation around this.
    ///     It optionally returns the shift (%delx, %dely) that maximizes
    ///     the correlation score when pix2 is shifted by this amount
    ///     relative to pix1.
    /// (2) Get the centroids of pix1 and pix2, using pixCentroid(),
    ///     to compute (%etransx, %etransy).  Get the areas using
    ///     pixCountPixels().
    /// (3) The centroid of pix2 is shifted with respect to the centroid
    ///     of pix1 by all values between -maxshiftx and maxshiftx,
    ///     and likewise for the y shifts.  Therefore, the number of
    ///     correlations computed is:
    ///          (2 * maxshiftx + 1) * (2 * maxshifty + 1)
    ///     Consequently, if pix1 and pix2 are large, you should do this
    ///     in a coarse-to-fine sequence.  See the use of this function
    ///     in pixCompareWithTranslation().
    /// </remarks>
    public void BestCorrelation(Pix pix2, int area1, int area2, int etransx, int etransy,
                                int maxshift, int[]? tab8, out int delx, out int dely,
                                out float score, int debugflag)
    {
        const string func = "pixBestCorrelation";
        Pix pix1 = this;
        delx = 0;
        dely = 0;
        score = 0.0f;
        if (pix1.Depth != 1)
            throw Lept.Arg(func, "pix1 not defined or not 1 bpp");
        if (pix2.Depth != 1)
            throw Lept.Arg(func, "pix2 not defined or not 1 bpp");
        if (area1 == 0 || area2 == 0)
            throw Lept.Arg(func, "areas must be > 0");

        int side = 2 * maxshift + 1;
        if (debugflag == 0 && maxshift >= 1 &&
            LeptGpu.TryGetActive((long)pix1.Width * pix1.Height, side * side, out IGpuAccelerator? gpuCorrel) &&
            gpuCorrel.TryBestCorrelation(pix1, pix2, area1, area2, etransx, etransy, maxshift, tab8, out int gDx, out int gDy, out float gScore)) {
            delx = gDx;
            dely = gDy;
            score = gScore;
            return;
        }
        FPix? fpix = null;
        if (debugflag > 0)
            fpix = FPix.Create(side, side);

            /* Search over a set of {shiftx, shifty} for the max */
        float maxscore = 0;
        int bestx = etransx;
        int besty = etransy;
        long opsPerShiftRow = (long)side * pix1.Height * pix1.Wpl * 4L;
        if (LeptParallel.EffectiveDop > 1 && (long)side * opsPerShiftRow >= LeptParallel.MinWorkThreshold) {
            float[] grid = new float[side * side];
            LeptParallel.ForRows(side, opsPerShiftRow, (rowStart, rowEnd) => {
                for (int ry = rowStart; ry < rowEnd; ry++) {
                    int shifty = ry - maxshift;
                    int baseIdx = ry * side;
                    for (int rx = 0; rx < side; rx++) {
                        int shiftx = rx - maxshift;
                        grid[baseIdx + rx] = pix1.CorrelationScoreShifted(
                            pix2, area1, area2, etransx + shiftx, etransy + shifty, tab8);
                    }
                }
            });
            for (int ry = 0; ry < side; ry++) {
                int shifty = ry - maxshift;
                int baseIdx = ry * side;
                for (int rx = 0; rx < side; rx++) {
                    int shiftx = rx - maxshift;
                    float sc = grid[baseIdx + rx];
                    if (debugflag > 0)
                        fpix!.SetPixel(rx, ry, 1000.0f * sc);
                    if (sc > maxscore) {
                        maxscore = sc;
                        bestx = etransx + shiftx;
                        besty = etransy + shifty;
                    }
                }
            }
        } else {
            for (int shifty = -maxshift; shifty <= maxshift; shifty++) {
                for (int shiftx = -maxshift; shiftx <= maxshift; shiftx++) {
                    float sc = pix1.CorrelationScoreShifted(pix2, area1, area2,
                                                            etransx + shiftx,
                                                            etransy + shifty, tab8);
                    if (debugflag > 0) {
                        fpix!.SetPixel(maxshift + shiftx, maxshift + shifty,
                                       1000.0f * sc);
                    }
                    if (sc > maxscore) {
                        maxscore = sc;
                        bestx = etransx + shiftx;
                        besty = etransy + shifty;
                    }
                }
            }
        }

        if (debugflag > 0) {
            Utils.LeptMkdir("lept/comp");
            Pix pix3 = fpix!.DisplayMaxDynamicRange();
            Pix pix4 = pix3.ExpandReplicate(20);
            string buf = CFormat.Sprintf("/tmp/lept/comp/correl_%d.png", debugflag);
            Pix.Write(buf, pix4, L.IFF_PNG);
        }

        delx = bestx;
        dely = besty;
        score = maxscore;
    }

    private const float TINY = 0.00001f;  /* small enough to consider equal to 0.0, for plot output */
}

public sealed partial class Pixa
{
    // leptonica: pixaComparePhotoRegionsByHisto (compare.c)
    /// <summary>Compares each of a set of cropped photo images to the others for similarity.</summary>
    /// <param name="minratio">requiring sizes be compatible; &lt; 1.0</param>
    /// <param name="textthresh">threshold for text/photo; use 0 for default</param>
    /// <param name="factor">subsampling; &gt;= 1</param>
    /// <param name="n">in range {1, ... 7}. n^2 is the maximum number
    ///     of subregions for histograms; typ. n = 3.</param>
    /// <param name="simthresh">threshold for similarity; use 0 for default</param>
    /// <param name="getScores">true to return the score matrix (and write its
    ///     visualization to a temp file); the C function does this only when
    ///     the output pointer is given</param>
    /// <param name="scores">score matrix as 1-D array of size N^2; null if not requested</param>
    /// <param name="getPixd">true to return the pix of similarity classes</param>
    /// <param name="pixd">pix of similarity classes; null if not requested</param>
    /// <param name="debug">true to output histograms</param>
    /// <returns>array giving similarity class indices</returns>
    /// <remarks>
    /// (1) This function takes a pixa of cropped photo images and
    ///     compares each one to the others for similarity.
    ///     Each image is first tested to see if it is a photo that can
    ///     be compared by tiled histograms.  If so, it is padded to put
    ///     the centroid in the center of the image, and the histograms
    ///     are generated.  The final step of comparing each histogram
    ///     with all the others is very fast.
    /// (2) To make the histograms, each image is subdivided in a maximum
    ///     of n^2 subimages.  The parameter %n specifies the "side" of
    ///     an n x n grid of such subimages.  If the subimages have an
    ///     aspect ratio larger than 2, the grid will change, again using n^2
    ///     as a maximum for the number of subimages.  For example,
    ///     if n == 3, but the image is 600 x 200 pixels, a 3x3 grid
    ///     would have subimages of 200 x 67 pixels, which is more
    ///     than 2:1, so we change to a 4x2 grid where each subimage
    ///     has 150 x 100 pixels.
    /// (3) An initial filter gives %score = 0 if the ratio of widths
    ///     and heights (smallest / largest) does not exceed a
    ///     threshold %minratio.  If set at 1.0, both images must be
    ///     exactly the same size.  A typical value for %minratio is 0.9.
    /// (4) The comparison score between two images is a value in [0.0 .. 1.0].
    ///     If the comparison score >= %simthresh, the images are placed in
    ///     the same similarity class.  Default value for %simthresh is 0.25.
    /// (5) An array %nai of similarity class indices for pix in the
    ///     input pixa is returned.
    /// (6) There are two debugging options:
    ///     * An optional 2D matrix of scores is returned as a 1D array.
    ///       A visualization of this is written to a temp file.
    ///     * An optional pix showing the similarity classes can be
    ///       returned.  Text in each input pix is reproduced.
    /// (7) See the notes in pixComparePhotoRegionsByHisto() for details
    ///     on the implementation.
    /// </remarks>
    public Numa ComparePhotoRegionsByHisto(float minratio, float textthresh, int factor,
                                           int n, float simthresh,
                                           bool getScores, out float[]? scores,
                                           bool getPixd, out Pix? pixd, bool debug)
    {
        const string func = "pixaComparePhotoRegionsByHisto";
        Pixa pixa = this;
        scores = null;
        pixd = null;
        if (minratio < 0.0 || minratio > 1.0)
            throw Lept.Arg(func, "minratio not in [0.0 ... 1.0]");
        if (textthresh <= 0.0) textthresh = 1.3f;
        if (factor < 1)
            throw Lept.Arg(func, "subsampling factor must be >= 1");
        if (n < 1 || n > 7) {
            Log.Warning(func, CFormat.Sprintf("n = %d is invalid; setting to 4", n));
            n = 4;
        }
        if (simthresh <= 0.0) simthresh = 0.25f;
        if (simthresh > 1.0)
            throw Lept.Arg(func, "simthresh invalid; should be near 0.25");

            /* Prepare the histograms */
        int nim = pixa.Count;
        Numaa?[] n3a = new Numaa?[nim];  /* array of naa */
        Numa naw = Numa.Create(0);
        Numa nah = Numa.Create(0);
        for (int i = 0; i < nim; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            string? text = pix.Text;
            pix.SetResolution(150, 150);
            int index = debug ? i : 0;
            Numaa? naa = pix.GenPhotoHistos(null, factor, textthresh, n,
                                            out int w, out int h, index);
            n3a[i] = naa;
            naw.AddNumber(w);
            nah.AddNumber(h);
            if (naa != null)
                Utils.LeptStderr("Image %s is photo\n", text ?? "(null)");
            else
                Utils.LeptStderr("Image %s is NOT photo\n", text ?? "(null)");
        }

            /* Do the comparisons.  We are making a set of classes, where
             * all similar images are placed in the same class.  There are
             * 'nim' input images.  The classes are labeled by 'classid' (all
             * similar images get the same 'classid' value), and 'nai' maps
             * the classid of the image in the input array to the classid
             * of the similarity class.  */
        float[] sc = new float[(long)nim * nim];
        Numa nai = Numa.MakeConstant(-1, nim);  /* classid array */
        int classid = 0;
        for (int i = 0; i < nim; i++) {
            sc[nim * i + i] = 1.0f;
            int ival = nai.GetIValue(i);
            if (ival != -1)  /* already set */
                continue;
            nai.SetValue(i, classid);
            if (n3a[i] == null) {  /* not a photo */
                classid++;
                continue;
            }
            int w1 = naw.GetIValue(i);
            int h1 = nah.GetIValue(i);
            for (int j = i + 1; j < nim; j++) {
                ival = nai.GetIValue(j);
                if (ival != -1)  /* already set */
                    continue;
                if (n3a[j] == null)  /* not a photo */
                    continue;
                int w2 = naw.GetIValue(j);
                int h2 = nah.GetIValue(j);
                float score = Pix.CompareTilesByHisto(n3a[i]!, n3a[j]!, minratio,
                                                      w1, h1, w2, h2, null);
                sc[nim * i + j] = score;
                sc[nim * j + i] = score;  /* the score array is symmetric */
                if (score > simthresh) {
                    nai.SetValue(j, classid);
                    Utils.LeptStderr(
                            "Setting %d similar to %d, in class %d; score %5.3f\n",
                            j, i, classid, score);
                }
            }
            classid++;
        }

            /* Debug: optionally save and display the score array.
             * All images that are photos are represented by a point on
             * the diagonal. Other images in the same similarity class
             * are on the same horizontal raster line to the right.
             * The array has been symmetrized, so images in the same
             * same similarity class also appear on the same column below. */
        if (getScores) {
            Pix pix2 = Pix.Create(nim, nim, 8);
            uint[] data = pix2.Data;
            int wpl = pix2.Wpl;
            for (int i = 0; i < nim; i++) {
                Span<uint> line = data.AsSpan(i * wpl);
                for (int j = 0; j < nim; j++) {
                    ArrayAccess.SetDataByte(line, j,
                            (int)Math.Min(255, 4.0 * 255 * sc[nim * i + j]));
                }
            }
            int fact = Math.Max(2, 1000 / nim);
            Pix pix3 = pix2.ExpandReplicate(fact);
            Utils.LeptStderr("Writing to /tmp/lept/comp/scorearray.png\n");
            Utils.LeptMkdir("lept/comp");
            Pix.Write("/tmp/lept/comp/scorearray.png", pix3, L.IFF_PNG);
            scores = sc;
        }

            /* Debug: optionally display and save the image comparisons.
             * Image similarity classes are displayed by column; similar
             * images are displayed in the same column. */
        if (getPixd)
            pixd = pixa.DisplayTiledByIndex(nai, 200, 20, 2, 6, 0x0000ff00);

        return nai;
    }
}

public sealed partial class PixColormap
{
    // leptonica: cmapEqual (compare.c)
    /// <summary>True if the colormaps are equal in size and entries.</summary>
    /// <param name="cmap1">The first colormap.</param>
    /// <param name="cmap2">The second colormap.</param>
    /// <param name="ncomps">The number of components to compare: 3 for RGB, 4 for RGBA.</param>
    public static bool CmapEqual(PixColormap cmap1, PixColormap cmap2, int ncomps)
    {
        if (ncomps != 3 && ncomps != 4)
            throw Lept.Arg("cmapEqual", "ncomps not 3 or 4");

        int n1 = cmap1.Count;
        int n2 = cmap2.Count;
        if (n1 != n2) {
            Log.Info("cmapEqual", "colormap sizes are different");
            return false;
        }

        for (int i = 0; i < n1; i++) {
            cmap1.GetRGBA(i, out int rval1, out int gval1, out int bval1, out int aval1);
            cmap2.GetRGBA(i, out int rval2, out int gval2, out int bval2, out int aval2);
            if (rval1 != rval2 || gval1 != gval2 || bval1 != bval2)
                return false;
            if (ncomps == 4 && aval1 != aval2)
                return false;
        }
        return true;
    }
}
