using System;
using static Leptonica.ArrayAccess;
using static Leptonica.L;

namespace Leptonica;

/*
 *  pix4.c: Histograms and pixel statistics.
 *
 *  Differences from C:
 *    - Optional output pointers become plain out parameters that are always
 *      computed (pixRowStats, pixColumnStats, pixGetColorHistogram, ...).
 *      pixGetBinnedComponentRange, pixGetRankColorArray and pixGetBinnedColor
 *      return the color array.  pixSplitDistributionFgBg has two overloads,
 *      without and with the debug plot (ppixdb != NULL in C).
 *    - Errors return 1 in C; here they throw, except where the C code logs an
 *      L_ERROR for a condition of the data and returns 1 with the color array
 *      set to NULL (pixGetRankColorArray, pixGetBinnedColor): these return null.
 *    - pixaGetAlignedStats, pixaExtractColumnFromEachPix are methods of Pixa and
 *      amapGetCountForColor is a method of L_Amap.
 *
 *  Stubs for callees in other files: pixcmapGetRangeValues (colormap.c),
 *  l_dnaRemoveDupsByHmap, pixConvertDataToDna (dnafunc1.c), gplotCreate,
 *  gplotAddPlot, gplotMakeOutputPix, gplotSimplePix1 (gplot.c), l_amapCreate,
 *  l_amapFind, l_amapInsert, l_amapSize (map.c), pixClipRectangles (pix5.c),
 *  pixConvertTo8BySampling, pixConvertRGBToGrayGeneral, pixRemoveColormap (pixconv.c),
 *  pixaSplitPix, pixaGetPixDimensions, pixaGetCount (pixabasic.c),
 *  pixaDisplayTiledInColumns (pixafunc2.c), pixScale, pixScaleByIntSampling (scale1.c),
 *  pixScaleRGBToGrayFast (scale2.c), pixThresholdToBinary (grayquant.c),
 *  pixWriteDebug, pixDisplay (writefile.c), lept_roundftoi (utils1.c), lept_mkdir (utils2.c).
 */

/// <summary>Constants from pix.h used by pix4.c.</summary>
public static partial class L
{
    /* Choose the component or the function of the components used to select a gray value */
    /// <summary>Use the minimum color component.</summary>
    public const int L_SELECT_MIN = 4;
    /// <summary>Use the maximum color component.</summary>
    public const int L_SELECT_MAX = 5;
    /// <summary>Use the hue value (in HSV color space).</summary>
    public const int L_SELECT_HUE = 7;
    /// <summary>Use the saturation value (in HSV color space).</summary>
    public const int L_SELECT_SATURATION = 8;
    /// <summary>Use a weighted average of the color components.</summary>
    public const int L_SELECT_WEIGHTED = 9;

    /* Statistical measures */
}

public sealed partial class Pix
{
    /*------------------------------------------------------------------*
     *                  Pixel histogram and averaging                   *
     *------------------------------------------------------------------*/
    // leptonica: pixGetGrayHistogram (pix4.c)
    /// <summary>Gets the gray histogram of a 1, 2, 4, 8 or 16 bpp image.</summary>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <returns>histogram</returns>
    /// <remarks>
    /// (1) If pixs has a colormap, it is converted to 8 bpp gray.
    ///     If you want a histogram of the colormap indices, use
    ///     pixGetCmapHistogram().
    /// (2) If pixs does not have a colormap, the output histogram is
    ///     of size 2^d, where d is the depth of pixs.
    /// (3) Set the subsampling factor > 1 to reduce the amount of computation.
    /// </remarks>
    public Numa GetGrayHistogram(int factor)
    {
        const string func = "pixGetGrayHistogram";
        int d = Depth;
        if (d > 16)
            throw Lept.Arg(func, "depth not in {1,2,4,8,16}");
        if (factor < 1)
            throw Lept.Arg(func, "sampling must be >= 1");

        Pix pixg;
        if (Colormap != null)
            pixg = RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
        else
            pixg = this;

        pixg.GetDimensions(out int w, out int h, out d);
        int size = 1 << d;
        Numa na = Numa.Create(size);
        na.Count = size;  /* all initialized to 0.0 */
        float[] array = na.GetFArray(L_NOCOPY);

        if (d == 1) {  /* special case */
            int count = pixg.CountPixels(null);
            array[0] = w * h - count;
            array[1] = count;
            return na;
        }

        int wpl = pixg.Wpl;
        uint[] data = pixg.Data;
        for (int i = 0; i < h; i += factor) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            if (d == 2) {
                for (int j = 0; j < w; j += factor) {
                    int val = GetDataDibit(line, j);
                    array[val] += 1.0f;
                }
            } else if (d == 4) {
                for (int j = 0; j < w; j += factor) {
                    int val = GetDataQbit(line, j);
                    array[val] += 1.0f;
                }
            } else if (d == 8) {
                if (factor == 1) {
                    int j = 0;
                    for (; j + 3 < w; j += 4) {
                        uint word = line[j >> 2];
                        array[word >> 24] += 1.0f;
                        array[(word >> 16) & 255] += 1.0f;
                        array[(word >> 8) & 255] += 1.0f;
                        array[word & 255] += 1.0f;
                    }
                    for (; j < w; j++)
                        array[GetDataByte(line, j)] += 1.0f;
                } else {
                    for (int j = 0; j < w; j += factor) {
                        int val = GetDataByte(line, j);
                        array[val] += 1.0f;
                    }
                }
            } else {  /* d == 16 */
                for (int j = 0; j < w; j += factor) {
                    int val = GetDataTwoBytes(line, j);
                    array[val] += 1.0f;
                }
            }
        }

        return na;
    }

    // leptonica: pixGetGrayHistogramMasked (pix4.c)
    /// <summary>Gets the 256-value gray histogram of an 8 bpp or colormapped image, under a mask.</summary>
    /// <param name="pixm">[optional] 1 bpp mask over which histogram is
    ///                    to be computed; use all pixels if null</param>
    /// <param name="x">UL corner of pixm relative to the UL corner of pixs;
    ///                 can be &lt; 0; ignored if pixm is null</param>
    /// <param name="y">see x</param>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <returns>histogram</returns>
    /// <remarks>
    /// (1) If pixs is cmapped, it is converted to 8 bpp gray.
    ///     If you want a histogram of the colormap indices, use
    ///     pixGetCmapHistogramMasked().
    /// (2) This always returns a 256-value histogram of pixel values.
    /// (3) Set the subsampling factor > 1 to reduce the amount of computation.
    /// (4) Clipping of pixm (if it exists) to pixs is done in the inner loop.
    /// (5) Input x,y are ignored unless pixm exists.
    /// </remarks>
    public Numa GetGrayHistogramMasked(Pix? pixm, int x, int y, int factor)
    {
        const string func = "pixGetGrayHistogramMasked";
        if (pixm == null)
            return GetGrayHistogram(factor);
        if (Depth != 8 && Colormap == null)
            throw Lept.Arg(func, "pixs neither 8 bpp nor colormapped");
        pixm.GetDimensions(out int wm, out int hm, out int dm);
        if (dm != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");
        if (factor < 1)
            throw Lept.Arg(func, "sampling must be >= 1");

        Numa na = Numa.Create(256);
        na.Count = 256;  /* all initialized to 0.0 */
        float[] array = na.GetFArray(L_NOCOPY);

        Pix pixg;
        if (Colormap != null)
            pixg = RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
        else
            pixg = this;
        pixg.GetDimensions(out int w, out int h, out _);
        uint[] datag = pixg.Data;
        int wplg = pixg.Wpl;
        uint[] datam = pixm.Data;
        int wplm = pixm.Wpl;

            /* Generate the histogram */
        for (int i = 0; i < hm; i += factor) {
            if (y + i < 0 || y + i >= h) continue;
            ReadOnlySpan<uint> lineg = datag.AsSpan((y + i) * wplg, wplg);
            ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
            for (int j = 0; j < wm; j += factor) {
                if (x + j < 0 || x + j >= w) continue;
                if (GetDataBit(linem, j) != 0) {
                    int val = GetDataByte(lineg, x + j);
                    array[val] += 1.0f;
                }
            }
        }

        return na;
    }

    // leptonica: pixGetGrayHistogramInRect (pix4.c)
    /// <summary>Gets the 256-value gray histogram of an 8 bpp or colormapped image, in a rectangle.</summary>
    /// <param name="box">[optional] over which histogram is to be computed;
    ///                   use full image if null</param>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <returns>histogram</returns>
    /// <remarks>
    /// (1) If pixs is cmapped, it is converted to 8 bpp gray.
    ///     If you want a histogram of the colormap indices, use
    ///     pixGetCmapHistogramInRect().
    /// (2) This always returns a 256-value histogram of pixel values.
    /// (3) Set the subsampling %factor > 1 to reduce the amount of computation.
    /// </remarks>
    public Numa GetGrayHistogramInRect(Box? box, int factor)
    {
        const string func = "pixGetGrayHistogramInRect";
        if (box == null)
            return GetGrayHistogram(factor);
        if (Depth != 8 && Colormap == null)
            throw Lept.Arg(func, "pixs neither 8 bpp nor colormapped");
        if (factor < 1)
            throw Lept.Arg(func, "sampling must be >= 1");

        Numa na = Numa.Create(256);
        na.Count = 256;  /* all initialized to 0.0 */
        float[] array = na.GetFArray(L_NOCOPY);

        Pix pixg;
        if (Colormap != null)
            pixg = RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
        else
            pixg = this;
        pixg.GetDimensions(out int w, out int h, out _);
        uint[] datag = pixg.Data;
        int wplg = pixg.Wpl;
        box.GetGeometry(out int bx, out int by, out int bw, out int bh);

            /* Generate the histogram */
        for (int i = 0; i < bh; i += factor) {
            if (by + i < 0 || by + i >= h) continue;
            ReadOnlySpan<uint> lineg = datag.AsSpan((by + i) * wplg, wplg);
            for (int j = 0; j < bw; j += factor) {
                if (bx + j < 0 || bx + j >= w) continue;
                int val = GetDataByte(lineg, bx + j);
                array[val] += 1.0f;
            }
        }

        return na;
    }

    // leptonica: pixGetGrayHistogramTiled (pix4.c)
    /// <summary>Gets a set of 256-value gray histograms, one for each tile.</summary>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <param name="nx">tiling; >= 1; typically small</param>
    /// <param name="ny">tiling; >= 1; typically small</param>
    /// <returns>set of histograms</returns>
    /// <remarks>
    /// (1) If pixs is cmapped, it is converted to 8 bpp gray.
    /// (2) This returns a set of 256-value histograms of pixel values.
    /// (3) Set the subsampling factor > 1 to reduce the amount of computation.
    /// </remarks>
    public Numaa GetGrayHistogramTiled(int factor, int nx, int ny)
    {
        const string func = "pixGetGrayHistogramTiled";
        if (factor < 1)
            throw Lept.Arg(func, "sampling must be >= 1");
        if (nx < 1 || ny < 1)
            throw Lept.Arg(func, "nx and ny must both be > 0");

        int n = nx * ny;
        Numaa naa = Numaa.Create(n);
        Pix pix1 = ConvertTo8(false);
        Pixa pixa = Pixa.SplitPix(pix1, nx, ny, 0, 0);
        for (int i = 0; i < n; i++) {
            Pix pix2 = pixa.GetPix(i, L_CLONE);
            Numa na = pix2.GetGrayHistogram(factor);
            naa.AddNuma(na, L_INSERT);
        }

        return naa;
    }

    // leptonica: pixGetColorHistogram (pix4.c)
    /// <summary>Gets three 256-value histograms, one for each color component.</summary>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <param name="nar">red histogram</param>
    /// <param name="nag">green histogram</param>
    /// <param name="nab">blue histogram</param>
    /// <remarks>
    /// (1) This generates a set of three 256 entry histograms,
    ///     one for each color component (r,g,b).
    /// (2) Set the subsampling %factor > 1 to reduce the amount of computation.
    /// </remarks>
    public void GetColorHistogram(int factor, out Numa nar, out Numa nag, out Numa nab)
    {
        const string func = "pixGetColorHistogram";
        nar = nag = nab = null!;
        GetDimensions(out int w, out int h, out int d);
        PixColormap? cmap = Colormap;
        if (cmap != null && (d != 2 && d != 4 && d != 8))
            throw Lept.Arg(func, "colormap and not 2, 4, or 8 bpp");
        if (cmap == null && d != 32)
            throw Lept.Arg(func, "no colormap and not rgb");
        if (factor < 1)
            throw Lept.Arg(func, "sampling factor must be >= 1");

            /* Set up the histogram arrays */
        nar = Numa.Create(256);
        nag = Numa.Create(256);
        nab = Numa.Create(256);
        nar.Count = 256;
        nag.Count = 256;
        nab.Count = 256;
        float[] rarray = nar.GetFArray(L_NOCOPY);
        float[] garray = nag.GetFArray(L_NOCOPY);
        float[] barray = nab.GetFArray(L_NOCOPY);

            /* Generate the color histograms */
        uint[] data = Data;
        int wpl = Wpl;
        if (cmap != null) {
            for (int i = 0; i < h; i += factor) {
                ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
                for (int j = 0; j < w; j += factor) {
                    int index;
                    if (d == 8)
                        index = GetDataByte(line, j);
                    else if (d == 4)
                        index = GetDataQbit(line, j);
                    else   /* 2 bpp */
                        index = GetDataDibit(line, j);
                    cmap.GetColor(index, out int rval, out int gval, out int bval);
                    rarray[rval] += 1.0f;
                    garray[gval] += 1.0f;
                    barray[bval] += 1.0f;
                }
            }
        } else {  /* 32 bpp rgb */
            for (int i = 0; i < h; i += factor) {
                ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
                for (int j = 0; j < w; j += factor) {
                    ExtractRGBValues(line[j], out int rval, out int gval, out int bval);
                    rarray[rval] += 1.0f;
                    garray[gval] += 1.0f;
                    barray[bval] += 1.0f;
                }
            }
        }
    }

    // leptonica: pixGetColorHistogramMasked (pix4.c)
    /// <summary>Gets three 256-value histograms, one for each color component, under a mask.</summary>
    /// <param name="pixm">[optional] 1 bpp mask over which histogram is
    ///                    to be computed; use all pixels if null</param>
    /// <param name="x">UL corner of pixm relative to the UL corner of pixs;
    ///                 can be &lt; 0; ignored if pixm is null</param>
    /// <param name="y">see x</param>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <param name="nar">red histogram</param>
    /// <param name="nag">green histogram</param>
    /// <param name="nab">blue histogram</param>
    /// <remarks>
    /// (1) This generates a set of three 256 entry histograms,
    /// (2) Set the subsampling %factor > 1 to reduce the amount of computation.
    /// (3) Clipping of pixm (if it exists) to pixs is done in the inner loop.
    /// (4) Input x,y are ignored unless pixm exists.
    /// </remarks>
    public void GetColorHistogramMasked(Pix? pixm, int x, int y, int factor,
                                        out Numa nar, out Numa nag, out Numa nab)
    {
        const string func = "pixGetColorHistogramMasked";
        if (pixm == null) {
            GetColorHistogram(factor, out nar, out nag, out nab);
            return;
        }

        nar = nag = nab = null!;
        GetDimensions(out int w, out int h, out int d);
        PixColormap? cmap = Colormap;
        if (cmap != null && (d != 2 && d != 4 && d != 8))
            throw Lept.Arg(func, "colormap and not 2, 4, or 8 bpp");
        if (cmap == null && d != 32)
            throw Lept.Arg(func, "no colormap and not rgb");
        pixm.GetDimensions(out int wm, out int hm, out int dm);
        if (dm != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");
        if (factor < 1)
            throw Lept.Arg(func, "sampling factor must be >= 1");

            /* Set up the histogram arrays */
        nar = Numa.Create(256);
        nag = Numa.Create(256);
        nab = Numa.Create(256);
        nar.Count = 256;
        nag.Count = 256;
        nab.Count = 256;
        float[] rarray = nar.GetFArray(L_NOCOPY);
        float[] garray = nag.GetFArray(L_NOCOPY);
        float[] barray = nab.GetFArray(L_NOCOPY);

            /* Generate the color histograms */
        uint[] datas = Data;
        int wpls = Wpl;
        uint[] datam = pixm.Data;
        int wplm = pixm.Wpl;
        if (cmap != null) {
            for (int i = 0; i < hm; i += factor) {
                if (y + i < 0 || y + i >= h) continue;
                ReadOnlySpan<uint> lines = datas.AsSpan((y + i) * wpls, wpls);
                ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
                for (int j = 0; j < wm; j += factor) {
                    if (x + j < 0 || x + j >= w) continue;
                    if (GetDataBit(linem, j) != 0) {
                        int index;
                        if (d == 8)
                            index = GetDataByte(lines, x + j);
                        else if (d == 4)
                            index = GetDataQbit(lines, x + j);
                        else   /* 2 bpp */
                            index = GetDataDibit(lines, x + j);
                        cmap.GetColor(index, out int rval, out int gval, out int bval);
                        rarray[rval] += 1.0f;
                        garray[gval] += 1.0f;
                        barray[bval] += 1.0f;
                    }
                }
            }
        } else {  /* 32 bpp rgb */
            for (int i = 0; i < hm; i += factor) {
                if (y + i < 0 || y + i >= h) continue;
                ReadOnlySpan<uint> lines = datas.AsSpan((y + i) * wpls, wpls);
                ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
                for (int j = 0; j < wm; j += factor) {
                    if (x + j < 0 || x + j >= w) continue;
                    if (GetDataBit(linem, j) != 0) {
                        ExtractRGBValues(lines[x + j], out int rval, out int gval, out int bval);
                        rarray[rval] += 1.0f;
                        garray[gval] += 1.0f;
                        barray[bval] += 1.0f;
                    }
                }
            }
        }
    }

    // leptonica: pixGetCmapHistogram (pix4.c)
    /// <summary>Gets the histogram of colormap indices of a 2, 4 or 8 bpp colormapped image.</summary>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <returns>histogram of cmap indices</returns>
    /// <remarks>
    /// (1) This generates a histogram of colormap pixel indices,
    ///     and is of size 2^d.
    /// (2) Set the subsampling %factor > 1 to reduce the amount of computation.
    /// </remarks>
    public Numa GetCmapHistogram(int factor)
    {
        const string func = "pixGetCmapHistogram";
        if (Colormap == null)
            throw Lept.Arg(func, "pixs not cmapped");
        if (factor < 1)
            throw Lept.Arg(func, "sampling must be >= 1");
        GetDimensions(out int w, out int h, out int d);
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "d not 2, 4 or 8");

        int size = 1 << d;
        Numa na = Numa.Create(size);
        na.Count = size;  /* all initialized to 0.0 */
        float[] array = na.GetFArray(L_NOCOPY);

        int wpl = Wpl;
        uint[] data = Data;
        for (int i = 0; i < h; i += factor) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w; j += factor) {
                int val;
                if (d == 8)
                    val = GetDataByte(line, j);
                else if (d == 4)
                    val = GetDataQbit(line, j);
                else  /* d == 2 */
                    val = GetDataDibit(line, j);
                array[val] += 1.0f;
            }
        }

        return na;
    }

    // leptonica: pixGetCmapHistogramMasked (pix4.c)
    /// <summary>Gets the histogram of colormap indices of a colormapped image, under a mask.</summary>
    /// <param name="pixm">[optional] 1 bpp mask over which histogram is
    ///                    to be computed; use all pixels if null</param>
    /// <param name="x">UL corner of pixm relative to the UL corner of pixs;
    ///                 can be &lt; 0; ignored if pixm is null</param>
    /// <param name="y">see x</param>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <returns>histogram of cmap indices</returns>
    /// <remarks>
    /// (1) This generates a histogram of colormap pixel indices,
    ///     and is of size 2^d.
    /// (2) Set the subsampling %factor > 1 to reduce the amount of computation.
    /// (3) Clipping of pixm to pixs is done in the inner loop.
    /// </remarks>
    public Numa GetCmapHistogramMasked(Pix? pixm, int x, int y, int factor)
    {
        const string func = "pixGetCmapHistogramMasked";
        if (pixm == null)
            return GetCmapHistogram(factor);

        if (Colormap == null)
            throw Lept.Arg(func, "pixs not cmapped");
        pixm.GetDimensions(out int wm, out int hm, out int dm);
        if (dm != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");
        if (factor < 1)
            throw Lept.Arg(func, "sampling must be >= 1");
        GetDimensions(out int w, out int h, out int d);
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "d not 2, 4 or 8");

        int size = 1 << d;
        Numa na = Numa.Create(size);
        na.Count = size;  /* all initialized to 0.0 */
        float[] array = na.GetFArray(L_NOCOPY);

        uint[] datas = Data;
        int wpls = Wpl;
        uint[] datam = pixm.Data;
        int wplm = pixm.Wpl;

        for (int i = 0; i < hm; i += factor) {
            if (y + i < 0 || y + i >= h) continue;
            ReadOnlySpan<uint> lines = datas.AsSpan((y + i) * wpls, wpls);
            ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
            for (int j = 0; j < wm; j += factor) {
                if (x + j < 0 || x + j >= w) continue;
                if (GetDataBit(linem, j) != 0) {
                    int val;
                    if (d == 8)
                        val = GetDataByte(lines, x + j);
                    else if (d == 4)
                        val = GetDataQbit(lines, x + j);
                    else  /* d == 2 */
                        val = GetDataDibit(lines, x + j);
                    array[val] += 1.0f;
                }
            }
        }

        return na;
    }

    // leptonica: pixGetCmapHistogramInRect (pix4.c)
    /// <summary>Gets the histogram of colormap indices of a colormapped image, in a rectangle.</summary>
    /// <param name="box">[optional] over which histogram is to be computed;
    ///                   use full image if null</param>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <returns>histogram of cmap indices</returns>
    /// <remarks>
    /// (1) This generates a histogram of colormap pixel indices,
    ///     and is of size 2^d.
    /// (2) Set the subsampling %factor > 1 to reduce the amount of computation.
    /// (3) Clipping to the box is done in the inner loop.
    /// </remarks>
    public Numa GetCmapHistogramInRect(Box? box, int factor)
    {
        const string func = "pixGetCmapHistogramInRect";
        if (box == null)
            return GetCmapHistogram(factor);
        if (Colormap == null)
            throw Lept.Arg(func, "pixs not cmapped");
        if (factor < 1)
            throw Lept.Arg(func, "sampling must be >= 1");
        GetDimensions(out int w, out int h, out int d);
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "d not 2, 4 or 8");

        int size = 1 << d;
        Numa na = Numa.Create(size);
        na.Count = size;  /* all initialized to 0.0 */
        float[] array = na.GetFArray(L_NOCOPY);

        uint[] datas = Data;
        int wpls = Wpl;
        box.GetGeometry(out int bx, out int by, out int bw, out int bh);

        for (int i = 0; i < bh; i += factor) {
            if (by + i < 0 || by + i >= h) continue;
            ReadOnlySpan<uint> lines = datas.AsSpan((by + i) * wpls, wpls);
            for (int j = 0; j < bw; j += factor) {
                if (bx + j < 0 || bx + j >= w) continue;
                int val;
                if (d == 8)
                    val = GetDataByte(lines, bx + j);
                else if (d == 4)
                    val = GetDataQbit(lines, bx + j);
                else  /* d == 2 */
                    val = GetDataDibit(lines, bx + j);
                array[val] += 1.0f;
            }
        }

        return na;
    }

    // leptonica: pixCountRGBColorsByHash (pix4.c)
    /// <summary>Counts the number of different colors in an rgb or rgba image, using a hash map.</summary>
    /// <returns>number of colors found</returns>
    /// <remarks>
    /// (1) This is about 4x faster than pixCountRGBColors(),
    ///     which uses an ordered map.
    /// </remarks>
    public int CountRGBColorsByHash()
    {
        if (Depth != 32)
            throw Lept.Arg("pixCountRGBColorsByHash", "pixs not defined or not 32 bpp");
        L_Dna da1 = ConvertDataToDna();
        L_Dna da2 = da1.RemoveDupsByHmap(out _);
        return da2.Count;
    }

    // leptonica: pixCountRGBColors (pix4.c)
    /// <summary>Counts the number of different colors in an rgb or rgba image, using an ordered map.</summary>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <returns>number of colors found</returns>
    /// <remarks>
    /// (1) If %factor == 1, this gives the exact number of colors.
    /// (2) This is about 4x slower than pixCountRGBColorsByHash().
    /// </remarks>
    public int CountRGBColors(int factor)
    {
        const string func = "pixCountRGBColors";
        if (Depth != 32)
            throw Lept.Arg(func, "pixs not defined or not 32 bpp");
        if (factor <= 0)
            throw Lept.Arg(func, "factor must be > 0");
        L_Amap amap = GetColorAmapHistogram(factor);
        return amap.Size;
    }

    // leptonica: pixGetColorAmapHistogram (pix4.c)
    /// <summary>Generates an ordered map from pixel value to histogram count.</summary>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <returns>amap</returns>
    /// <remarks>
    /// (1) This generates an ordered map from pixel value to histogram count.
    /// (2) Use amapGetCountForColor() to use the map to look up a count.
    /// </remarks>
    public L_Amap GetColorAmapHistogram(int factor)
    {
        const string func = "pixGetColorAmapHistogram";
        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (factor <= 0)
            throw Lept.Arg(func, "factor must be > 0");
        GetDimensions(out int w, out int h, out _);
        uint[] data = Data;
        int wpl = Wpl;
        L_Amap amap = L_Amap.Create(L_UINT_TYPE);
        for (int i = 0; i < h; i += factor) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w; j += factor) {
                RbType key = default;
                RbType value = default;
                key.Utype = line[j];
                RbType? pval = amap.Find(key);
                if (pval == null)
                    value.Itype = 1;
                else
                    value.Itype = 1 + pval.Value.Itype;
                amap.Insert(key, value);
            }
        }

        return amap;
    }

    // leptonica: pixGetRankValue (pix4.c)
    /// <summary>Gets the pixel value (gray or color) at a given rank.</summary>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <param name="rank">between 0.0 and 1.0; 1.0 is brightest, 0.0 is darkest</param>
    /// <returns>pixel value corresponding to input rank</returns>
    /// <remarks>
    /// (1) Simple function to get a rank value (color) of an image.
    ///     For a color image, the median value (rank = 0.5) can be
    ///     used to linearly remap the colors based on the median
    ///     of a target image, using pixLinearMapToTargetColor().
    /// (2) For RGB, this treats each color component independently.
    ///     It calls pixGetGrayHistogramMasked() on each component, and
    ///     uses the returned gray histogram to get the rank value.
    ///     It then combines the 3 rank values into a color pixel.
    /// </remarks>
    public uint GetRankValue(int factor, float rank)
    {
        int d = Depth;
        PixColormap? cmap = Colormap;
        if (d != 8 && d != 32 && cmap == null)
            throw Lept.Arg("pixGetRankValue", "pixs not 8 or 32 bpp, or cmapped");
        Pix pixt;
        if (cmap != null)
            pixt = RemoveColormap(REMOVE_CMAP_BASED_ON_SRC);
        else
            pixt = this;
        d = pixt.Depth;

        uint value;
        if (d == 8) {
            pixt.GetRankValueMasked(null, 0, 0, factor, rank, out float val, out _);
            value = (uint)Utils.RoundFToI(val);
        } else {
            pixt.GetRankValueMaskedRGB(null, 0, 0, factor, rank,
                                       out float rval, out float gval, out float bval);
            ComposeRGBPixel(Utils.RoundFToI(rval), Utils.RoundFToI(gval),
                            Utils.RoundFToI(bval), out value);
        }

        return value;
    }

    // leptonica: pixGetRankValueMaskedRGB (pix4.c)
    /// <summary>Gets the rank value of each color component, under a mask.</summary>
    /// <param name="pixm">[optional] 1 bpp mask over which rank val is to be taken;
    ///                    use all pixels if null</param>
    /// <param name="x">UL corner of pixm relative to the UL corner of pixs;
    ///                 can be &lt; 0; ignored if pixm is null</param>
    /// <param name="y">see x</param>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <param name="rank">between 0.0 and 1.0; 1.0 is brightest, 0.0 is darkest</param>
    /// <param name="rval">red component val for input rank</param>
    /// <param name="gval">green component val for input rank</param>
    /// <param name="bval">blue component val for input rank</param>
    /// <remarks>
    /// (1) Computes the rank component values of pixels in pixs that
    ///     are under the fg of the optional mask.  If the mask is null, it
    ///     computes the average of the pixels in pixs.
    /// (2) Set the subsampling %factor > 1 to reduce the amount of
    ///     computation.
    /// (4) Input x,y are ignored unless pixm exists.
    /// (5) The rank must be in [0.0 ... 1.0], where the brightest pixel
    ///     has rank 1.0.  For the median pixel value, use 0.5.
    /// </remarks>
    public void GetRankValueMaskedRGB(Pix? pixm, int x, int y, int factor, float rank,
                                      out float rval, out float gval, out float bval)
    {
        const string func = "pixGetRankValueMaskedRGB";
        rval = gval = bval = 0.0f;
        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (pixm != null && pixm.Depth != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");
        if (factor < 1)
            throw Lept.Arg(func, "sampling factor must be >= 1");
        if (rank < 0.0 || rank > 1.0)
            throw Lept.Arg(func, "rank not in [0.0 ... 1.0]");

        Pix? pixmt = null;
        if (pixm != null) {
            float scale = 1.0f / (float)factor;
            pixmt = pixm.Scale(scale, scale);
        }
        Pix pixt = ScaleRGBToGrayFast(factor, COLOR_RED);
        pixt.GetRankValueMasked(pixmt, x / factor, y / factor, factor, rank, out rval, out _);
        pixt = ScaleRGBToGrayFast(factor, COLOR_GREEN);
        pixt.GetRankValueMasked(pixmt, x / factor, y / factor, factor, rank, out gval, out _);
        pixt = ScaleRGBToGrayFast(factor, COLOR_BLUE);
        pixt.GetRankValueMasked(pixmt, x / factor, y / factor, factor, rank, out bval, out _);
    }

    // leptonica: pixGetRankValueMasked (pix4.c)
    /// <summary>Gets the rank value of an 8 bpp or colormapped image, under a mask.</summary>
    /// <param name="pixm">[optional] 1 bpp mask, over which the rank val
    ///                    is to be taken; use all pixels if null</param>
    /// <param name="x">UL corner of pixm relative to the UL corner of pixs;
    ///                 can be &lt; 0; ignored if pixm is null</param>
    /// <param name="y">see x</param>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <param name="rank">between 0.0 and 1.0; 1.0 is brightest, 0.0 is darkest</param>
    /// <param name="val">pixel value corresponding to input rank</param>
    /// <param name="na">histogram</param>
    /// <remarks>
    /// (1) Computes the rank value of pixels in pixs that are under
    ///     the fg of the optional mask.  If the mask is null, it
    ///     computes the average of the pixels in pixs.
    /// (2) Set the subsampling %factor > 1 to reduce the amount of
    ///     computation.
    /// (3) Clipping of pixm (if it exists) to pixs is done in the inner loop.
    /// (4) Input x,y are ignored unless pixm exists.
    /// (5) The rank must be in [0.0 ... 1.0], where the brightest pixel
    ///     has rank 1.0.  For the median pixel value, use 0.5.
    /// (6) The histogram can optionally be returned, so that other rank
    ///     values can be extracted without recomputing the histogram.
    ///     In that case, just use
    ///         numaHistogramGetValFromRank(na, rank, &amp;val);
    ///     on the returned Numa for additional rank values.
    /// </remarks>
    public void GetRankValueMasked(Pix? pixm, int x, int y, int factor, float rank,
                                   out float val, out Numa na)
    {
        const string func = "pixGetRankValueMasked";
        val = 0.0f;
        na = null!;
        if (Depth != 8 && Colormap == null)
            throw Lept.Arg(func, "pixs neither 8 bpp nor colormapped");
        if (pixm != null && pixm.Depth != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");
        if (factor < 1)
            throw Lept.Arg(func, "sampling factor must be >= 1");
        if (rank < 0.0 || rank > 1.0)
            throw Lept.Arg(func, "rank not in [0.0 ... 1.0]");

        na = GetGrayHistogramMasked(pixm, x, y, factor);
        val = na.HistogramGetValFromRank(rank);
    }

    // leptonica: pixGetPixelAverage (pix4.c)
    /// <summary>Gets the average pixel value of an 8 bpp or rgb image, or of a colormapped image.</summary>
    /// <param name="pixm">[optional] 1 bpp mask over which average is
    ///                    to be taken; use all pixels if null</param>
    /// <param name="x">UL corner of pixm relative to the UL corner of pixs;
    ///                 can be &lt; 0</param>
    /// <param name="y">see x</param>
    /// <param name="factor">subsampling factor; >= 1</param>
    /// <returns>average pixel value</returns>
    /// <remarks>
    /// (1) For rgb pix, this is a more direct computation of the
    ///     average value of the pixels in %pixs that are under the
    ///     mask %pixm. It is faster than pixGetPixelStats(), which
    ///     calls pixGetAverageMaskedRGB() and has the overhead of
    ///     generating a temporary pix of each of the three components;
    ///     this can take most of the time if %factor > 1.
    /// (2) If %pixm is null, this gives the average value of all
    ///     pixels in %pixs.  The returned value is an integer.
    /// (3) For color %pixs, the returned pixel value is in the standard
    ///     uint32 RGBA packing.
    /// (4) Clipping of pixm (if it exists) to pixs is done in the inner loop.
    /// (5) Input x,y are ignored if %pixm does not exist.
    /// (6) For general averaging of 1, 2, 4 or 8 bpp grayscale, use
    ///     pixAverageInRect().
    /// </remarks>
    public uint GetPixelAverage(Pix? pixm, int x, int y, int factor)
    {
        const string func = "pixGetPixelAverage";
        int d = Depth;
        if (d != 32 && Colormap == null)
            throw Lept.Arg(func, "pixs not rgb or colormapped");
        if (pixm != null && pixm.Depth != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");
        if (factor < 1)
            throw Lept.Arg(func, "sampling factor must be >= 1");

        Pix pix1;
        if (Colormap != null)
            pix1 = RemoveColormap(REMOVE_CMAP_BASED_ON_SRC);
        else
            pix1 = this;
        pix1.GetDimensions(out int w, out int h, out d);
        if (d == 1)
            throw Lept.Arg(func, "pix1 is just 1 bpp");
        uint[] data1 = pix1.Data;
        int wpl1 = pix1.Wpl;

        int rval, gval, bval;
        double sum, rsum, gsum, bsum;
        sum = rsum = gsum = bsum = 0.0;
        int count = 0;
        if (pixm == null) {
            for (int i = 0; i < h; i += factor) {
                ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl1, wpl1);
                for (int j = 0; j < w; j += factor) {
                    if (d == 8) {
                        int val = GetDataByte(line1, j);
                        sum += val;
                    } else {  /* rgb */
                        ExtractRGBValues(line1[j], out rval, out gval, out bval);
                        rsum += rval;
                        gsum += gval;
                        bsum += bval;
                    }
                    count++;
                }
            }
        } else {  /* masked */
            pixm.GetDimensions(out int wm, out int hm, out _);
            uint[] datam = pixm.Data;
            int wplm = pixm.Wpl;
            for (int i = 0; i < hm; i += factor) {
                if (y + i < 0 || y + i >= h) continue;
                ReadOnlySpan<uint> line1 = data1.AsSpan((y + i) * wpl1, wpl1);
                ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
                for (int j = 0; j < wm; j += factor) {
                    if (x + j < 0 || x + j >= w) continue;
                    if (GetDataBit(linem, j) != 0) {
                        if (d == 8) {
                            int val = GetDataByte(line1, x + j);
                            sum += val;
                        } else {  /* rgb */
                            ExtractRGBValues(line1[x + j], out rval, out gval, out bval);
                            rsum += rval;
                            gsum += gval;
                            bsum += bval;
                        }
                        count++;
                    }
                }
            }
        }

        if (count == 0)
            throw Lept.Fail(func, "no pixels sampled");
        uint pval;
        if (d == 8) {
            pval = (uint)(sum / (double)count);
        } else {  /* d == 32 */
            rval = (int)(uint)(rsum / (double)count);
            gval = (int)(uint)(gsum / (double)count);
            bval = (int)(uint)(bsum / (double)count);
            ComposeRGBPixel(rval, gval, bval, out pval);
        }

        return pval;
    }

    // leptonica: pixGetPixelStats (pix4.c)
    /// <summary>Gets one of four statistical values of an 8 bpp, 32 bpp or colormapped image.</summary>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <param name="type">L_MEAN_ABSVAL, L_ROOT_MEAN_SQUARE,
    ///                    L_STANDARD_DEVIATION, L_VARIANCE</param>
    /// <returns>pixel value corresponding to input type</returns>
    /// <remarks>
    /// (1) Simple function to get one of four statistical values of an image.
    /// (2) It does not take a mask: it uses the entire image.
    /// (3) To get the average pixel value of an RGB image, suggest using
    ///     pixGetPixelAverage(), which is considerably faster.
    /// </remarks>
    public uint GetPixelStats(int factor, int type)
    {
        int d = Depth;
        PixColormap? cmap = Colormap;
        if (d != 8 && d != 32 && cmap == null)
            throw Lept.Arg("pixGetPixelStats", "pixs not 8 or 32 bpp, or cmapped");
        Pix pixt;
        if (cmap != null)
            pixt = RemoveColormap(REMOVE_CMAP_BASED_ON_SRC);
        else
            pixt = this;
        d = pixt.Depth;

        uint value;
        if (d == 8) {
            float val = pixt.GetAverageMasked(null, 0, 0, factor, type);
            value = (uint)Utils.RoundFToI(val);
        } else {
            pixt.GetAverageMaskedRGB(null, 0, 0, factor, type,
                                     out float rval, out float gval, out float bval);
            ComposeRGBPixel(Utils.RoundFToI(rval), Utils.RoundFToI(gval),
                            Utils.RoundFToI(bval), out value);
        }

        return value;
    }

    // leptonica: pixGetAverageMaskedRGB (pix4.c)
    /// <summary>Gets a statistic of each color component of a 32 bpp or colormapped image, under a mask.</summary>
    /// <param name="pixm">[optional] 1 bpp mask over which average is
    ///                    to be taken; use all pixels if null</param>
    /// <param name="x">UL corner of pixm relative to the UL corner of pixs;
    ///                 can be &lt; 0</param>
    /// <param name="y">see x</param>
    /// <param name="factor">subsampling factor; >= 1</param>
    /// <param name="type">L_MEAN_ABSVAL, L_ROOT_MEAN_SQUARE,
    ///                    L_STANDARD_DEVIATION, L_VARIANCE</param>
    /// <param name="rval">measured red value of given 'type'</param>
    /// <param name="gval">measured green value of given 'type'</param>
    /// <param name="bval">measured blue value of given 'type'</param>
    /// <remarks>
    /// (1) For usage, see pixGetAverageMasked().
    /// (2) If there is a colormap, it is removed before the 8 bpp
    ///     component images are extracted.
    /// (3) A better name for this would be: pixGetPixelStatsRGB()
    /// </remarks>
    public void GetAverageMaskedRGB(Pix? pixm, int x, int y, int factor, int type,
                                    out float rval, out float gval, out float bval)
    {
        const string func = "pixGetAverageMaskedRGB";
        rval = gval = bval = 0.0f;
        PixColormap? cmap = Colormap;
        if (Depth != 32 && cmap == null)
            throw Lept.Arg(func, "pixs neither 32 bpp nor colormapped");
        if (pixm != null && pixm.Depth != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");
        if (factor < 1)
            throw Lept.Arg(func, "sampling factor must be >= 1");
        if (type != L_MEAN_ABSVAL && type != L_ROOT_MEAN_SQUARE &&
            type != L_STANDARD_DEVIATION && type != L_VARIANCE)
            throw Lept.Arg(func, "invalid measure type");
        if (pixm != null) {
            bool empty = pixm.Zero();
            if (empty)
                throw Lept.Arg(func, "empty mask");
        }

        Pix pixt;
        if (cmap != null)
            pixt = GetRGBComponentCmap(COLOR_RED);
        else
            pixt = GetRGBComponent(COLOR_RED);
        rval = pixt.GetAverageMasked(pixm, x, y, factor, type);
        if (cmap != null)
            pixt = GetRGBComponentCmap(COLOR_GREEN);
        else
            pixt = GetRGBComponent(COLOR_GREEN);
        gval = pixt.GetAverageMasked(pixm, x, y, factor, type);
        if (cmap != null)
            pixt = GetRGBComponentCmap(COLOR_BLUE);
        else
            pixt = GetRGBComponent(COLOR_BLUE);
        bval = pixt.GetAverageMasked(pixm, x, y, factor, type);
    }

    // leptonica: pixGetAverageMasked (pix4.c)
    /// <summary>Gets a statistic of an 8 or 16 bpp (or colormapped) image, under a mask.</summary>
    /// <param name="pixm">[optional] 1 bpp mask over which average is
    ///                    to be taken; use all pixels if null</param>
    /// <param name="x">UL corner of pixm relative to the UL corner of pixs;
    ///                 can be &lt; 0</param>
    /// <param name="y">see x</param>
    /// <param name="factor">subsampling factor; >= 1</param>
    /// <param name="type">L_MEAN_ABSVAL, L_ROOT_MEAN_SQUARE,
    ///                    L_STANDARD_DEVIATION, L_VARIANCE</param>
    /// <returns>measured value of given 'type'</returns>
    /// <remarks>
    /// (1) Use L_MEAN_ABSVAL to get the average value of pixels in pixs
    ///     that are under the fg of the optional mask.  If the mask
    ///     is null, it finds the average of the pixels in pixs.
    /// (2) Likewise, use L_ROOT_MEAN_SQUARE to get the rms value of
    ///     pixels in pixs, either masked or not; L_STANDARD_DEVIATION
    ///     to get the standard deviation from the mean of the pixels;
    ///     L_VARIANCE to get the average squared difference from the
    ///     expected value.  The variance is the square of the stdev.
    ///     For the standard deviation, we use
    ///         sqrt([([x] - x)]^2) = sqrt([x^2] - [x]^2)
    /// (3) Set the subsampling %factor > 1 to reduce the amount of
    ///     computation.
    /// (4) Clipping of pixm (if it exists) to pixs is done in the inner loop.
    /// (5) Input x,y are ignored unless pixm exists.
    /// (6) A better name for this would be: pixGetPixelStatsGray()
    /// </remarks>
    public float GetAverageMasked(Pix? pixm, int x, int y, int factor, int type)
    {
        const string func = "pixGetAverageMasked";
        int d = Depth;
        if (d != 8 && d != 16 && Colormap == null)
            throw Lept.Arg(func, "pixs not 8 or 16 bpp or colormapped");
        if (pixm != null && pixm.Depth != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");
        if (factor < 1)
            throw Lept.Arg(func, "sampling factor must be >= 1");
        if (type != L_MEAN_ABSVAL && type != L_ROOT_MEAN_SQUARE &&
            type != L_STANDARD_DEVIATION && type != L_VARIANCE)
            throw Lept.Arg(func, "invalid measure type");
        if (pixm != null) {
            bool empty = pixm.Zero();
            if (empty)
                throw Lept.Arg(func, "empty mask");
        }

        Pix pixg;
        if (Colormap != null)
            pixg = RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE);
        else
            pixg = this;
        pixg.GetDimensions(out int w, out int h, out d);
        uint[] datag = pixg.Data;
        int wplg = pixg.Wpl;

        double sumave = 0.0, summs = 0.0;
        int count = 0;
        if (pixm == null) {
            for (int i = 0; i < h; i += factor) {
                ReadOnlySpan<uint> lineg = datag.AsSpan(i * wplg, wplg);
                for (int j = 0; j < w; j += factor) {
                    int val;
                    if (d == 8)
                        val = GetDataByte(lineg, j);
                    else  /* d == 16 */
                        val = GetDataTwoBytes(lineg, j);
                    if (type != L_ROOT_MEAN_SQUARE)
                        sumave += val;
                    if (type != L_MEAN_ABSVAL)
                        summs += (double)(val) * val;
                    count++;
                }
            }
        } else {
            pixm.GetDimensions(out int wm, out int hm, out _);
            uint[] datam = pixm.Data;
            int wplm = pixm.Wpl;
            for (int i = 0; i < hm; i += factor) {
                if (y + i < 0 || y + i >= h) continue;
                ReadOnlySpan<uint> lineg = datag.AsSpan((y + i) * wplg, wplg);
                ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
                for (int j = 0; j < wm; j += factor) {
                    if (x + j < 0 || x + j >= w) continue;
                    if (GetDataBit(linem, j) != 0) {
                        int val;
                        if (d == 8)
                            val = GetDataByte(lineg, x + j);
                        else  /* d == 16 */
                            val = GetDataTwoBytes(lineg, x + j);
                        if (type != L_ROOT_MEAN_SQUARE)
                            sumave += val;
                        if (type != L_MEAN_ABSVAL)
                            summs += (double)(val) * val;
                        count++;
                    }
                }
            }
        }

        if (count == 0)
            throw Lept.Fail(func, "no pixels sampled");
        double ave = sumave / (double)count;
        double meansq = summs / (double)count;
        double var = meansq - ave * ave;
        if (type == L_MEAN_ABSVAL)
            return (float)ave;
        else if (type == L_ROOT_MEAN_SQUARE)
            return (float)Math.Sqrt(meansq);
        else if (type == L_STANDARD_DEVIATION)
            return (float)Math.Sqrt(var);
        else  /* type == L_VARIANCE */
            return (float)var;
    }

    // leptonica: pixGetAverageTiledRGB (pix4.c)
    /// <summary>Gets the tiled statistic of each color component of a 32 bpp or colormapped image.</summary>
    /// <param name="sx">tile size; must be at least 2 x 2</param>
    /// <param name="sy">tile size; must be at least 2 x 2</param>
    /// <param name="type">L_MEAN_ABSVAL, L_ROOT_MEAN_SQUARE, L_STANDARD_DEVIATION</param>
    /// <param name="pixr">tiled 'average' of red component</param>
    /// <param name="pixg">tiled 'average' of green component</param>
    /// <param name="pixb">tiled 'average' of blue component</param>
    /// <remarks>
    /// (1) For usage, see pixGetAverageTiled().
    /// (2) If there is a colormap, it is removed before the 8 bpp
    ///     component images are extracted.
    /// </remarks>
    public void GetAverageTiledRGB(int sx, int sy, int type, out Pix pixr, out Pix pixg, out Pix pixb)
    {
        const string func = "pixGetAverageTiledRGB";
        pixr = pixg = pixb = null!;
        PixColormap? cmap = Colormap;
        if (Depth != 32 && cmap == null)
            throw Lept.Arg(func, "pixs neither 32 bpp nor colormapped");
        if (sx < 2 || sy < 2)
            throw Lept.Arg(func, "sx and sy not both > 1");
        if (type != L_MEAN_ABSVAL && type != L_ROOT_MEAN_SQUARE &&
            type != L_STANDARD_DEVIATION)
            throw Lept.Arg(func, "invalid measure type");

        Pix pixt;
        if (cmap != null)
            pixt = GetRGBComponentCmap(COLOR_RED);
        else
            pixt = GetRGBComponent(COLOR_RED);
        pixr = pixt.GetAverageTiled(sx, sy, type);
        if (cmap != null)
            pixt = GetRGBComponentCmap(COLOR_GREEN);
        else
            pixt = GetRGBComponent(COLOR_GREEN);
        pixg = pixt.GetAverageTiled(sx, sy, type);
        if (cmap != null)
            pixt = GetRGBComponentCmap(COLOR_BLUE);
        else
            pixt = GetRGBComponent(COLOR_BLUE);
        pixb = pixt.GetAverageTiled(sx, sy, type);
    }

    // leptonica: pixGetAverageTiled (pix4.c)
    /// <summary>Gets the tiled statistic of an 8 bpp or colormapped image.</summary>
    /// <param name="sx">tile size; must be at least 2 x 2</param>
    /// <param name="sy">tile size; must be at least 2 x 2</param>
    /// <param name="type">L_MEAN_ABSVAL, L_ROOT_MEAN_SQUARE, L_STANDARD_DEVIATION</param>
    /// <returns>pixd average values in each tile</returns>
    /// <remarks>
    /// (1) Only computes for tiles that are entirely contained in pixs.
    /// (2) Use L_MEAN_ABSVAL to get the average abs value within the tile;
    ///     L_ROOT_MEAN_SQUARE to get the rms value within each tile;
    ///     L_STANDARD_DEVIATION to get the standard dev. from the average
    ///     within each tile.
    /// (3) If colormapped, converts to 8 bpp gray.
    /// </remarks>
    public Pix GetAverageTiled(int sx, int sy, int type)
    {
        const string func = "pixGetAverageTiled";
        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && Colormap == null)
            throw Lept.Arg(func, "pixs not 8 bpp or cmapped");
        if (sx < 2 || sy < 2)
            throw Lept.Arg(func, "sx and sy not both > 1");
        int wd = w / sx;
        int hd = h / sy;
        if (wd < 1 || hd < 1)
            throw Lept.Arg(func, "wd or hd == 0");
        if (type != L_MEAN_ABSVAL && type != L_ROOT_MEAN_SQUARE &&
            type != L_STANDARD_DEVIATION)
            throw Lept.Arg(func, "invalid measure type");

        // pixRemoveColormap() returns a clone of pixs if there is no colormap
        Pix pixt = (Colormap != null) ? RemoveColormap(REMOVE_CMAP_TO_GRAYSCALE) : this;
        Pix pixd = Create(wd, hd, 8);
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        double normfact = 1.0 / (double)(sx * sy);
        double sumave, summs, ave = 0.0, meansq = 0.0;
        int valt;
        for (int i = 0; i < hd; i++) {
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            int linet = i * sy * wplt;
            for (int j = 0; j < wd; j++) {
                if (type == L_MEAN_ABSVAL || type == L_STANDARD_DEVIATION) {
                    sumave = 0.0;
                    for (int k = 0; k < sy; k++) {
                        ReadOnlySpan<uint> startt = datat.AsSpan(linet + k * wplt, wplt);
                        for (int m = 0; m < sx; m++) {
                            int pos = j * sx + m;
                            valt = GetDataByte(startt, pos);
                            sumave += valt;
                        }
                    }
                    ave = normfact * sumave;
                }
                if (type == L_ROOT_MEAN_SQUARE || type == L_STANDARD_DEVIATION) {
                    summs = 0.0;
                    for (int k = 0; k < sy; k++) {
                        ReadOnlySpan<uint> startt = datat.AsSpan(linet + k * wplt, wplt);
                        for (int m = 0; m < sx; m++) {
                            int pos = j * sx + m;
                            valt = GetDataByte(startt, pos);
                            summs += (double)(valt) * valt;
                        }
                    }
                    meansq = normfact * summs;
                }
                if (type == L_MEAN_ABSVAL)
                    valt = (int)(ave + 0.5);
                else if (type == L_ROOT_MEAN_SQUARE)
                    valt = (int)(Math.Sqrt(meansq) + 0.5);
                else  /* type == L_STANDARD_DEVIATION */
                    valt = (int)(Math.Sqrt(meansq - ave * ave) + 0.5);
                SetDataByte(lined, j, valt);
            }
        }

        return pixd;
    }

    // leptonica: pixRowStats (pix4.c)
    /// <summary>Computes, for each row of an 8 bpp image, the mean, median, mode, mode count,
    /// variance and root variance.</summary>
    /// <param name="box">[optional] clipping box; can be null</param>
    /// <param name="namean">numa of mean values</param>
    /// <param name="namedian">numa of median values</param>
    /// <param name="namode">numa of mode intensity values</param>
    /// <param name="namodecount">numa of mode counts</param>
    /// <param name="navar">numa of variance</param>
    /// <param name="narootvar">numa of square root of variance</param>
    /// <remarks>
    /// (1) This computes numas that represent column vectors of statistics,
    ///     with each of its values derived from the corresponding row of a Pix.
    /// (2) The C function can skip the computation of any of the 5 numas by passing
    ///     NULL; here they are all returned.
    /// (3) Other functions that compute pixel row statistics are:
    ///        pixCountPixelsByRow()
    ///        pixAverageByRow()
    ///        pixVarianceByRow()
    ///        pixGetRowStats()
    ///
    /// NOTE(upstream): the C code stores the value of row i at index i of arrays that
    /// have only bh elements, where bh is the height of the clipped box.  With a box
    /// that starts below the first row (ystart > 0), it writes beyond the arrays.  Here the
    /// arrays hold yend elements, so nothing is written out of bounds, and the numas
    /// returned hold the first bh of them (rows ystart..yend-1 are stored at
    /// those indices; the leading elements are zero).
    /// </remarks>
    public void RowStats(Box? box, out Numa namean, out Numa namedian, out Numa namode,
                         out Numa namodecount, out Numa navar, out Numa narootvar)
    {
        const string func = "pixRowStats";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");

        GetDimensions(out int w, out int h, out _);
        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart, out int xend,
                                  out int yend, out int bw, out int bh);

            /* We need the mean for variance and root variance */
        uint[] datas = Data;
        int wpls = Wpl;
        float norm = 1.0f / (float)bw;
        float[] famean = new float[yend];
        float[] fameansq = new float[yend];
        float[] favar = new float[yend];
        float[] farootvar = new float[yend];
        for (int i = ystart; i < yend; i++) {
            int sum = 0, sumsq = 0;
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            for (int j = xstart; j < xend; j++) {
                int val = GetDataByte(lines, j);
                sum += val;
                sumsq += val * val;
            }
            famean[i] = norm * sum;
            fameansq[i] = norm * sumsq;
            favar[i] = fameansq[i] - famean[i] * famean[i];
            farootvar[i] = MathF.Sqrt(favar[i]);
        }
        namean = Numa.CreateFromFArray(famean, bh, L_INSERT);
        navar = Numa.CreateFromFArray(favar, bh, L_INSERT);
        narootvar = Numa.CreateFromFArray(farootvar, bh, L_INSERT);

            /* We need a histogram to find the median and/or mode values */
        int[] histo = new int[256];
        float[] famedian = new float[yend];
        float[] famode = new float[yend];
        float[] famodecount = new float[yend];
        for (int i = ystart; i < yend; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Array.Clear(histo);
            for (int j = xstart; j < xend; j++) {
                int val = GetDataByte(lines, j);
                histo[val]++;
            }

            int sum = 0;
            int target = (bw + 1) / 2;
            for (int k = 0; k < 256; k++) {
                sum += histo[k];
                if (sum >= target) {
                    famedian[i] = k;
                    break;
                }
            }

            int max = 0;
            int modeval = 0;
            for (int k = 0; k < 256; k++) {
                if (histo[k] > max) {
                    max = histo[k];
                    modeval = k;
                }
            }
            famode[i] = modeval;
            famodecount[i] = max;
        }
        namedian = Numa.CreateFromFArray(famedian, bh, L_INSERT);
        namode = Numa.CreateFromFArray(famode, bh, L_INSERT);
        namodecount = Numa.CreateFromFArray(famodecount, bh, L_INSERT);
    }

    // leptonica: pixColumnStats (pix4.c)
    /// <summary>Computes, for each column of an 8 bpp image, the mean, median, mode, mode count,
    /// variance and root variance.</summary>
    /// <param name="box">[optional] clipping box; can be null</param>
    /// <param name="namean">numa of mean values</param>
    /// <param name="namedian">numa of median values</param>
    /// <param name="namode">numa of mode intensity values</param>
    /// <param name="namodecount">numa of mode counts</param>
    /// <param name="navar">numa of variance</param>
    /// <param name="narootvar">numa of square root of variance</param>
    /// <remarks>
    /// (1) This computes numas that represent row vectors of statistics,
    ///     with each of its values derived from the corresponding col of a Pix.
    /// (2) The C function can skip the computation of any of the 5 numas by passing
    ///     NULL; here they are all returned.
    /// (3) Other functions that compute pixel column statistics are:
    ///        pixCountPixelsByColumn()
    ///        pixAverageByColumn()
    ///        pixVarianceByColumn()
    ///        pixGetColumnStats()
    ///
    /// NOTE(upstream): as in pixRowStats(), the value of column j is stored at index j
    /// of arrays with bw elements, which overflows for a box with xstart > 0 (handled
    /// the same way).  Moreover, the pixel rows are read starting at row 0 instead of
    /// row ystart, so with a box with ystart > 0 the statistics are those of the
    /// rows 0..bh-1.
    /// </remarks>
    public void ColumnStats(Box? box, out Numa namean, out Numa namedian, out Numa namode,
                            out Numa namodecount, out Numa navar, out Numa narootvar)
    {
        const string func = "pixColumnStats";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");

        GetDimensions(out int w, out int h, out _);
        Box.ClipToRectangleParams(box, w, h, out int xstart, out int ystart, out int xend,
                                  out int yend, out int bw, out int bh);

            /* We need the mean for variance and root variance */
        uint[] datas = Data;
        int wpls = Wpl;
        float norm = 1.0f / (float)bh;
        float[] famean = new float[xend];
        float[] fameansq = new float[xend];
        float[] favar = new float[xend];
        float[] farootvar = new float[xend];
        for (int j = xstart; j < xend; j++) {
            int sum = 0, sumsq = 0;
            for (int i = ystart, lines = 0; i < yend; lines += wpls, i++) {
                int val = GetDataByte(datas.AsSpan(lines, wpls), j);
                sum += val;
                sumsq += val * val;
            }
            famean[j] = norm * sum;
            fameansq[j] = norm * sumsq;
            favar[j] = fameansq[j] - famean[j] * famean[j];
            farootvar[j] = MathF.Sqrt(favar[j]);
        }
        namean = Numa.CreateFromFArray(famean, bw, L_INSERT);
        navar = Numa.CreateFromFArray(favar, bw, L_INSERT);
        narootvar = Numa.CreateFromFArray(farootvar, bw, L_INSERT);

            /* We need a histogram to find the median and/or mode values */
        int[] histo = new int[256];
        float[] famedian = new float[xend];
        float[] famode = new float[xend];
        float[] famodecount = new float[xend];
        for (int j = xstart; j < xend; j++) {
            Array.Clear(histo);
            for (int i = ystart, lines = 0; i < yend; lines += wpls, i++) {
                int val = GetDataByte(datas.AsSpan(lines, wpls), j);
                histo[val]++;
            }

            int sum = 0;
            int target = (bh + 1) / 2;
            for (int k = 0; k < 256; k++) {
                sum += histo[k];
                if (sum >= target) {
                    famedian[j] = k;
                    break;
                }
            }

            int max = 0;
            int modeval = 0;
            for (int k = 0; k < 256; k++) {
                if (histo[k] > max) {
                    max = histo[k];
                    modeval = k;
                }
            }
            famode[j] = modeval;
            famodecount[j] = max;
        }
        namedian = Numa.CreateFromFArray(famedian, bw, L_INSERT);
        namode = Numa.CreateFromFArray(famode, bw, L_INSERT);
        namodecount = Numa.CreateFromFArray(famodecount, bw, L_INSERT);
    }

    // leptonica: pixGetRangeValues (pix4.c)
    /// <summary>Gets the min and max values of a component of an 8 bpp, 32 bpp or colormapped image.</summary>
    /// <param name="factor">subsampling factor; >= 1; ignored if colormapped</param>
    /// <param name="color">L_SELECT_RED, L_SELECT_GREEN or L_SELECT_BLUE</param>
    /// <param name="minval">minimum value of component</param>
    /// <param name="maxval">maximum value of component</param>
    /// <remarks>
    /// (1) If pixs is 8 bpp grayscale, the color selection type is ignored.
    /// </remarks>
    public void GetRangeValues(int factor, int color, out int minval, out int maxval)
    {
        const string func = "pixGetRangeValues";
        minval = maxval = 0;

        PixColormap? cmap = Colormap;
        if (cmap != null) {
            cmap.GetRangeValues(color, out minval, out maxval, out _, out _);
            return;
        }

        if (factor < 1)
            throw Lept.Arg(func, "sampling factor must be >= 1");
        int d = Depth;
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 8 or 32 bpp");

        if (d == 8) {
            GetExtremeValueCore(factor, L_SELECT_MIN, false, false, false, out _, out _, out _, out minval);
            GetExtremeValueCore(factor, L_SELECT_MAX, false, false, false, out _, out _, out _, out maxval);
        } else if (color == L_SELECT_RED) {
            GetExtremeValueCore(factor, L_SELECT_MIN, true, false, false, out minval, out _, out _, out _);
            GetExtremeValueCore(factor, L_SELECT_MAX, true, false, false, out maxval, out _, out _, out _);
        } else if (color == L_SELECT_GREEN) {
            GetExtremeValueCore(factor, L_SELECT_MIN, false, true, false, out _, out minval, out _, out _);
            GetExtremeValueCore(factor, L_SELECT_MAX, false, true, false, out _, out maxval, out _, out _);
        } else if (color == L_SELECT_BLUE) {
            GetExtremeValueCore(factor, L_SELECT_MIN, false, false, true, out _, out _, out minval, out _);
            GetExtremeValueCore(factor, L_SELECT_MAX, false, false, true, out _, out _, out maxval, out _);
        } else {
            throw Lept.Arg(func, "invalid color");
        }
    }

    // leptonica: pixGetExtremeValue (pix4.c)
    /// <summary>Gets the min or max value of each component of an 8 bpp, 32 bpp or colormapped image.</summary>
    /// <param name="factor">subsampling factor; >= 1; ignored if colormapped</param>
    /// <param name="type">L_SELECT_MIN or L_SELECT_MAX</param>
    /// <param name="rval">red component; -1 if not rgb or colormapped</param>
    /// <param name="gval">green component; -1 if not rgb or colormapped</param>
    /// <param name="bval">blue component; -1 if not rgb or colormapped</param>
    /// <param name="grayval">min or max gray value; -1 if not 8 bpp grayscale</param>
    /// <remarks>
    /// (1) If pixs is grayscale, the result is returned in %grayval.
    ///     Otherwise, if there is a colormap or d == 32,
    ///     each color component is returned.
    /// </remarks>
    public void GetExtremeValue(int factor, int type, out int rval, out int gval, out int bval,
                                out int grayval)
    {
        GetExtremeValueCore(factor, type, true, true, true, out rval, out gval, out bval, out grayval);
    }

    // The flags take the place of the optional output pointers of pixGetExtremeValue():
    // only the requested components are computed.
    private void GetExtremeValueCore(int factor, int type, bool wantr, bool wantg, bool wantb,
                                     out int rval, out int gval, out int bval, out int grayval)
    {
        const string func = "pixGetExtremeValue";
        rval = gval = bval = grayval = -1;
        if (type != L_SELECT_MIN && type != L_SELECT_MAX)
            throw Lept.Arg(func, "invalid type");

        PixColormap? cmap = Colormap;
        if (cmap != null) {
            if (type == L_SELECT_MIN) {
                if (wantr) cmap.GetRangeValues(L_SELECT_RED, out rval, out _, out _, out _);
                if (wantg) cmap.GetRangeValues(L_SELECT_GREEN, out gval, out _, out _, out _);
                if (wantb) cmap.GetRangeValues(L_SELECT_BLUE, out bval, out _, out _, out _);
            } else {  /* type == L_SELECT_MAX */
                if (wantr) cmap.GetRangeValues(L_SELECT_RED, out _, out rval, out _, out _);
                if (wantg) cmap.GetRangeValues(L_SELECT_GREEN, out _, out gval, out _, out _);
                if (wantb) cmap.GetRangeValues(L_SELECT_BLUE, out _, out bval, out _, out _);
            }
            return;
        }

        GetDimensions(out int w, out int h, out int d);
        if (factor < 1)
            throw Lept.Arg(func, "sampling factor must be >= 1");
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 8 or 32 bpp");
        if (d == 32 && !wantr && !wantg && !wantb)
            throw Lept.Arg(func, "can't return result in r/g/b-val");

        uint[] data = Data;
        int wpl = Wpl;
        if (d == 8) {
            int extval;
            if (type == L_SELECT_MIN)
                extval = 100000;
            else  /* get max */
                extval = -1;

            for (int i = 0; i < h; i += factor) {
                ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
                for (int j = 0; j < w; j += factor) {
                    int val = GetDataByte(line, j);
                    if ((type == L_SELECT_MIN && val < extval) ||
                        (type == L_SELECT_MAX && val > extval))
                        extval = val;
                }
            }
            grayval = extval;
            return;
        }

            /* 32 bpp rgb */
        int extrval, extgval, extbval;
        if (type == L_SELECT_MIN) {
            extrval = 100000;
            extgval = 100000;
            extbval = 100000;
        } else {
            extrval = -1;
            extgval = -1;
            extbval = -1;
        }
        for (int i = 0; i < h; i += factor) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w; j += factor) {
                uint pixel = line[j];
                if (wantr) {
                    int r = (int)((pixel >> L_RED_SHIFT) & 0xff);
                    if ((type == L_SELECT_MIN && r < extrval) ||
                        (type == L_SELECT_MAX && r > extrval))
                        extrval = r;
                }
                if (wantg) {
                    int g = (int)((pixel >> L_GREEN_SHIFT) & 0xff);
                    if ((type == L_SELECT_MIN && g < extgval) ||
                        (type == L_SELECT_MAX && g > extgval))
                        extgval = g;
                }
                if (wantb) {
                    int b = (int)((pixel >> L_BLUE_SHIFT) & 0xff);
                    if ((type == L_SELECT_MIN && b < extbval) ||
                        (type == L_SELECT_MAX && b > extbval))
                        extbval = b;
                }
            }
        }
        if (wantr) rval = extrval;
        if (wantg) gval = extgval;
        if (wantb) bval = extbval;
    }

    // leptonica: pixGetMaxValueInRect (pix4.c)
    /// <summary>Gets the max value, and its location, in a rectangle of an 8, 16 or 32 bpp image.</summary>
    /// <param name="box">[optional] region; set box = null to use entire pixs</param>
    /// <param name="maxval">max value in region</param>
    /// <param name="xmax">x location of max value</param>
    /// <param name="ymax">y location of max value</param>
    /// <remarks>
    /// (1) This can be used to find the maximum and its location
    ///     in a 2-dimensional histogram, where the x and y directions
    ///     represent two color components (e.g., saturation and hue).
    /// (2) Note that here a 32 bpp pixs has pixel values that are simply
    ///     numbers.  They are not 8 bpp components in a colorspace.
    ///
    /// NOTE(upstream): the box is not clipped to the image; a box that is not
    /// contained in pixs makes the C code read outside the pix data.  The port throws
    /// where the access leaves the array.
    /// </remarks>
    public void GetMaxValueInRect(Box? box, out uint maxval, out int xmax, out int ymax)
    {
        const string func = "pixGetMaxValueInRect";
        maxval = 0;
        xmax = 0;
        ymax = 0;
        if (Colormap != null)
            throw Lept.Arg(func, "pixs has colormap");
        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 16 && d != 32)
            throw Lept.Arg(func, "pixs not 8, 16 or 32 bpp");

        int xstart = 0, ystart = 0;
        int xend = w - 1;
        int yend = h - 1;
        if (box != null) {
            box.GetGeometry(out xstart, out ystart, out int bw, out int bh);
            xend = xstart + bw - 1;
            yend = ystart + bh - 1;
        }

        uint[] data = Data;
        int wpl = Wpl;
        uint max = 0;
        int xm = 0, ym = 0;
        for (int i = ystart; i <= yend; i++) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = xstart; j <= xend; j++) {
                uint val;
                if (d == 8)
                    val = (uint)GetDataByte(line, j);
                else if (d == 16)
                    val = (uint)GetDataTwoBytes(line, j);
                else  /* d == 32 */
                    val = line[j];
                if (val > max) {
                    max = val;
                    xm = j;
                    ym = i;
                }
            }
        }
        if (max == 0) {  /* no counts; pick the center of the rectangle */
            xm = (xstart + xend) / 2;
            ym = (ystart + yend) / 2;
        }

        maxval = max;
        xmax = xm;
        ymax = ym;
    }

    // leptonica: pixGetMaxColorIndex (pix4.c)
    /// <summary>Max pixel value in a 1, 2, 4 or 8 bpp image.</summary>
    /// <returns>max colormap index value</returns>
    public int GetMaxColorIndex()
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 2 && d != 4 && d != 8)
            throw Lept.Arg("pixGetMaxColorIndex", "invalid pixs depth; not in (1,2,4,8}");

        int wpl = Wpl;
        uint[] data = Data;
        int max = 0;
        int maxval = (1 << d) - 1;
        if (d == 1) {
            bool empty = Zero();
            if (!empty) max = 1;
            return max;
        }
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            if (d == 2) {
                for (int j = 0; j < w; j++) {
                    int val = GetDataDibit(line, j);
                    if (val > max) max = val;
                }
            } else if (d == 4) {
                for (int j = 0; j < w; j++) {
                    int val = GetDataQbit(line, j);
                    if (val > max) max = val;
                }
            } else if (d == 8) {
                for (int j = 0; j < w; j++) {
                    int val = GetDataByte(line, j);
                    if (val > max) max = val;
                }
            }
            if (max == maxval) break;
        }
        return max;
    }

    // leptonica: pixGetBinnedComponentRange (pix4.c)
    /// <summary>Gets the min and max average values of a color component in a set of rank bins.</summary>
    /// <param name="nbins">number of equal population bins; must be > 1</param>
    /// <param name="factor">subsampling factor; >= 1</param>
    /// <param name="color">L_SELECT_RED, L_SELECT_GREEN or L_SELECT_BLUE</param>
    /// <param name="minval">minimum value of component</param>
    /// <param name="maxval">maximum value of component</param>
    /// <param name="carray">color array of bins</param>
    /// <param name="fontsize">0 for no debug; for debug, valid set
    ///                        is {4,6,8,10,12,14,16,18,20}.</param>
    /// <remarks>
    /// (1) This returns the min and max average values of the
    ///     selected color component in the set of rank bins,
    ///     where the ranking is done using the specified component.
    /// </remarks>
    public void GetBinnedComponentRange(int nbins, int factor, int color, out int minval,
                                        out int maxval, out uint[] carray, int fontsize)
    {
        const string func = "pixGetBinnedComponentRange";
        minval = maxval = 0;
        carray = null!;
        if (Depth != 32)
            throw Lept.Arg(func, "pixs not defined or not 32 bpp");
        if (factor < 1)
            throw Lept.Arg(func, "sampling factor must be >= 1");
        if (color != L_SELECT_RED && color != L_SELECT_GREEN &&
            color != L_SELECT_BLUE)
            throw Lept.Arg(func, "invalid color");
        if (fontsize < 0 || fontsize > 20 || (fontsize & 1) != 0 || fontsize == 2)
            throw Lept.Arg(func, "invalid fontsize");

        uint[]? array = GetRankColorArray(nbins, color, factor, null, 0);
        if (array == null)
            throw Lept.Fail(func, "carray not made");

        if (fontsize > 0) {
            for (int i = 0; i < nbins; i++)
                Log.Info(func, CFormat.Sprintf("c[%d] = %x\n", i, array[i]));
            Pix pixt = DisplayColorArray(array, nbins, 200, 5, fontsize);
            pixt.Display(100, 100);
        }

        ExtractRGBValues(array[0], out int rval, out int gval, out int bval);
        int min = rval;
        if (color == L_SELECT_GREEN)
            min = gval;
        else if (color == L_SELECT_BLUE)
            min = bval;
        ExtractRGBValues(array[nbins - 1], out rval, out gval, out bval);
        int max = rval;
        if (color == L_SELECT_GREEN)
            max = gval;
        else if (color == L_SELECT_BLUE)
            max = bval;

        minval = min;
        maxval = max;
        carray = array;
    }

    // leptonica: pixGetRankColorArray (pix4.c)
    /// <summary>Gets the average color in each of a set of equal population rank bins.</summary>
    /// <param name="nbins">number of equal population bins; must be > 1</param>
    /// <param name="type">color selection flag</param>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <param name="pixadb">[optional] debug: caller passes this in.
    ///                      Use to display color squares and to
    ///                      capture plots of color components</param>
    /// <param name="fontsize">[optional] debug: only used if pixadb exists.
    ///                        Valid set is {4,6,8,10,12,14,16,18,20}.
    ///                        fontsize == 6 is typical.</param>
    /// <returns>array of colors, ranked by intensity; null if there are fewer than 10
    /// samples per bin (the C function logs an error and returns 1)</returns>
    /// <remarks>
    /// (1) The color selection flag is one of: L_SELECT_RED, L_SELECT_GREEN,
    ///     L_SELECT_BLUE, L_SELECT_MIN, L_SELECT_MAX, L_SELECT_AVERAGE,
    ///     L_SELECT_HUE, L_SELECT_SATURATION.
    /// (2) The pixels are ordered by the value of the selected color
    ///     value, and an equal number are placed in %nbins.  The average
    ///     color in each bin is returned in a color array with %nbins colors.
    /// (3) Set the subsampling factor > 1 to reduce the amount of
    ///     computation.  Typically you want at least 10,000 pixels
    ///     for reasonable statistics.  Must be at least 10 samples/bin.
    /// (4) A crude "rank color" as a function of rank can be found from
    ///        rankint = (l_int32)(rank * (nbins - 1) + 0.5);
    ///        extractRGBValues(array[rankint], &amp;rval, &amp;gval, &amp;bval);
    ///     where the rank is in [0.0 ... 1.0].
    /// </remarks>
    public uint[]? GetRankColorArray(int nbins, int type, int factor, Pixa? pixadb, int fontsize)
    {
        const string func = "pixGetRankColorArray";
        if (factor < 1)
            throw Lept.Arg(func, "sampling factor must be >= 1");
        if (nbins < 2)
            throw Lept.Arg(func, "nbins must be at least 2");
        PixColormap? cmap = Colormap;
        if (Depth != 32 && cmap == null)
            throw Lept.Arg(func, "pixs neither 32 bpp nor cmapped");
        if (type != L_SELECT_RED && type != L_SELECT_GREEN &&
            type != L_SELECT_BLUE && type != L_SELECT_MIN &&
            type != L_SELECT_MAX && type != L_SELECT_AVERAGE &&
            type != L_SELECT_HUE && type != L_SELECT_SATURATION)
            throw Lept.Arg(func, "invalid type");
        if (pixadb != null) {
            if (fontsize < 0 || fontsize > 20 || (fontsize & 1) != 0 || fontsize == 2) {
                Log.Warning(func, CFormat.Sprintf("invalid fontsize %d; setting to 6\n", fontsize));
                fontsize = 6;
            }
        }
        GetDimensions(out int w, out int h, out _);
        int samplesperbin = (w * h) / (factor * factor * nbins);
        if (samplesperbin < 10) {
            Log.Error(func, CFormat.Sprintf("samplesperbin = %d < 10\n", samplesperbin));
            return null;
        }

            /* Downscale by factor and remove colormap if it exists */
        Pix pix1 = ScaleByIntSampling(factor);
        Pix pixc;
        if (cmap != null)
            pixc = pix1.RemoveColormap(REMOVE_CMAP_TO_FULL_COLOR);
        else
            pixc = pix1;

            /* Convert to an 8 bit version for ordering the pixels */
        Pix pixg = pixc.ConvertRGBToGrayGeneral(type, 0.0f, 0.0f, 0.0f);

            /* Get the average color in each bin for pixels whose grayscale
             * values are in the range for that bin. */
        uint[]? array = pixc.GetBinnedColor(pixg, 1, nbins, pixadb);
        if (array == null)
            Log.Error(func, "color array not returned\n");
        if (array != null && pixadb != null) {
            Pix pixd = DisplayColorArray(array, nbins, 200, 5, fontsize);
            pixd.WriteDebug("/tmp/lept/regout/rankhisto.png", IFF_PNG);
        }

        return array;
    }

    // leptonica: pixGetBinnedColor (pix4.c)
    /// <summary>Gets the average color in each of a set of equal population bins,
    /// ordered by the gray value of a grayscale version.</summary>
    /// <param name="pixg">8 bpp grayscale version of pixs</param>
    /// <param name="factor">sampling factor along pixel counting direction</param>
    /// <param name="nbins">number of bins based on grayscale value {1,...,100}</param>
    /// <param name="pixadb">[optional] debug: caller passes this in.
    ///                      Use to display output color squares and plots of
    ///                      color components.</param>
    /// <returns>array of average color values in each bin; null if the average number of
    /// points in a bin is less than 5 (the C function logs an error and returns 1)</returns>
    /// <remarks>
    /// (1) This takes a color image, a grayscale version, and the number
    ///     of requested bins.  The pixels are ordered by the corresponding
    ///     gray value and an equal number of pixels are put in each bin.
    ///     The average color for each bin is returned as an array
    ///     of l_uint32 colors in our standard RGBA ordering.  We require
    ///     at least 5 pixels in each bin.
    /// (2) This is used by pixGetRankColorArray(), which generates the
    ///     grayscale image %pixg from the color image %pixs.
    /// (3) Arrays of float64 are used for intermediate storage, without
    ///     loss of precision, of the sampled uint32 pixel values.
    /// </remarks>
    public uint[]? GetBinnedColor(Pix pixg, int factor, int nbins, Pixa? pixadb)
    {
        const string func = "pixGetBinnedColor";
        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        if (pixg.Depth != 8)
            throw Lept.Arg(func, "pixg undefined or not 8 bpp");
        if (factor < 1) {
            Log.Warning(func, "sampling factor less than 1; setting to 1\n");
            factor = 1;
        }
        if (nbins < 1 || nbins > 100)
            throw Lept.Arg(func, "nbins not in [1,100]");

            /* Require that each bin has at least 5 pixels. */
        GetDimensions(out int w, out int h, out _);
        int npts = (w + factor - 1) * (h + factor - 1) / (factor * factor);
        int avepts = (npts + nbins - 1) / nbins;  /* average number of pts in a bin */
        if (avepts < 5) {
            Log.Error(func, CFormat.Sprintf("avepts = %d; must be >= 5\n", avepts));
            return null;
        }

        /* ------------------------------------------------------------ *
         * Find the average color for each bin.  The colors are ordered *
         * by the gray value in the corresponding pixel in %pixg.       *
         * The bins have equal numbers of pixels (within 1).            *
         * ------------------------------------------------------------ */

            /* Generate a dnaa, where each dna has the colors corresponding
             * to the grayscale value given by the index of the dna in the dnaa */
        uint[] datas = Data;
        int wpls = Wpl;
        uint[] datag = pixg.Data;
        int wplg = pixg.Wpl;
        L_Dnaa daa = L_Dnaa.CreateFull(256, 0);
        for (int i = 0; i < h; i += factor) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            ReadOnlySpan<uint> lineg = datag.AsSpan(i * wplg, wplg);
            for (int j = 0; j < w; j += factor) {
                int grayval = GetDataByte(lineg, j);
                daa.AddNumber(grayval, lines[j]);
            }
        }

        if (pixadb != null) {
            Numa na = Numa.Create(256);  /* grayscale histogram */
            for (int i = 0; i < 256; i++)
                na.AddNumber(daa.GetDnaCount(i));

                /* Plot the gray bin value and the rank(gray) values */
            na.DiscretizeHistoInBins(nbins, out Numa nabinval, out Numa narank);
            Pix pix1 = Gplot.SimplePix1(nabinval, "Gray value in each bin");
            pixadb.AddPix(pix1, L_INSERT);
            pix1 = Gplot.SimplePix1(narank, "rank as function of gray value");
            pixadb.AddPix(pix1, L_INSERT);
        }

            /* Get the number of items in each bin */
        int ntot = daa.GetNumberCount();
        Numa naeach = Numa.GetUniformBinSizes(ntot, nbins);

            /* Get the average color in each bin.  This algorithm is
             * esssentially the same as in numaDiscretizeHistoInBins() */
        uint[] carray = new uint[nbins];
        double rsum, gsum, bsum;
        rsum = gsum = bsum = 0.0;
        int bincount = 0;
        int binindex = 0;
        int binsize = naeach.GetIValue(0);
        for (int i = 0; i < 256; i++) {
            int count = daa.GetDnaCount(i);
            for (int j = 0; j < count; j++) {
                bincount++;
                double val64 = daa.GetValue(i, j);
                ExtractRGBValues((uint)val64, out int rval, out int gval, out int bval);
                rsum += rval;
                gsum += gval;
                bsum += bval;
                if (bincount == binsize) {  /* add bin entry */
                    int rave = (int)(rsum / binsize + 0.5);
                    int gave = (int)(gsum / binsize + 0.5);
                    int bave = (int)(bsum / binsize + 0.5);
                    ComposeRGBPixel(rave, gave, bave, out carray[binindex]);
                    rsum = gsum = bsum = 0.0;
                    bincount = 0;
                    binindex++;
                    if (binindex == nbins) break;
                    binsize = naeach.GetIValue(binindex);
                }
            }
            if (binindex == nbins) break;
        }
        if (binindex != nbins)
            Log.Error(func, CFormat.Sprintf("binindex = %d != nbins = %d\n", binindex, nbins));

        if (pixadb != null) {
            Numa nared = Numa.Create(nbins);
            Numa nagreen = Numa.Create(nbins);
            Numa nablue = Numa.Create(nbins);
            for (int i = 0; i < nbins; i++) {
                ExtractRGBValues(carray[i], out int rval, out int gval, out int bval);
                nared.AddNumber(rval);
                nagreen.AddNumber(gval);
                nablue.AddNumber(bval);
            }
            Utils.LeptMkdir("lept/regout");
            Pix pix1 = Gplot.SimplePix1(nared, "Average red val vs. rank bin");
            pixadb.AddPix(pix1, L_INSERT);
            pix1 = Gplot.SimplePix1(nagreen, "Average green val vs. rank bin");
            pixadb.AddPix(pix1, L_INSERT);
            pix1 = Gplot.SimplePix1(nablue, "Average blue val vs. rank bin");
            pixadb.AddPix(pix1, L_INSERT);
        }

        return carray;
    }

    // leptonica: pixDisplayColorArray (pix4.c)
    /// <summary>Generates an array of labeled color squares from an array of color values.</summary>
    /// <param name="carray">array of colors: 0xrrggbb00</param>
    /// <param name="ncolors">size of array</param>
    /// <param name="side">size of each color square; suggest 200</param>
    /// <param name="ncols">number of columns in output color matrix</param>
    /// <param name="fontsize">to label each square with text.
    ///                        Valid set is {4,6,8,10,12,14,16,18,20}.
    ///                        Suggest 6 for 200x200 square. Use 0 to disable.</param>
    /// <returns>pixd color array</returns>
    /// <remarks>
    /// (1) This generates an array of labeled color squares from an
    ///     array of color values.
    /// (2) To make a single color square, use pixMakeColorSquare().
    /// </remarks>
    public static Pix DisplayColorArray(uint[] carray, int ncolors, int side, int ncols, int fontsize)
    {
        if (fontsize < 0 || fontsize > 20 || (fontsize & 1) != 0 || fontsize == 2)
            throw Lept.Arg("pixDisplayColorArray", "invalid fontsize");

        L_Bmf? bmf = (fontsize == 0) ? null : L_Bmf.Create(null, fontsize);
        Pixa pixa = Pixa.Create(ncolors);
        for (int i = 0; i < ncolors; i++) {
            Pix pix1 = Create(side, side, 32);
            pix1.SetAllArbitrary(carray[i]);
            Pix pix2 = pix1.AddBorder(2, 1);
            Pix pix3;
            if (bmf != null) {
                ExtractRGBValues(carray[i], out int rval, out int gval, out int bval);
                string textstr = CFormat.Sprintf("%d: (%d %d %d)", i, rval, gval, bval);
                pix3 = pix2.AddSingleTextblock(bmf, textstr, 0xff000000, L_ADD_BELOW, out _);
            } else {
                pix3 = pix2;
            }
            pixa.AddPix(pix3, L_INSERT);
        }
        Pix pix4 = pixa.DisplayTiledInColumns(ncols, 1.0f, 20, 2);
        return pix4;
    }

    // leptonica: pixRankBinByStrip (pix4.c)
    /// <summary>Generates a pix whose columns (or rows) hold the rank binned colors of
    /// the strips of the input image.</summary>
    /// <param name="direction">L_SCAN_HORIZONTAL or L_SCAN_VERTICAL</param>
    /// <param name="size">of strips in scan direction</param>
    /// <param name="nbins">number of equal population bins; must be > 1</param>
    /// <param name="type">color selection flag</param>
    /// <returns>pixd result</returns>
    /// <remarks>
    /// (1) This generates a pix of height %nbins, where each column
    ///     represents a horizontal or vertical strip of the input image.
    ///     If %direction == L_SCAN_HORIZONTAL, the input image is
    ///     tiled into vertical strips of width %size, where %size is
    ///     chosen as a compromise between getting better spatial
    ///     columnwise resolution (small %size) and getting better
    ///     columnwise statistical information (larger %size).  Likewise
    ///     with rows of the image if %direction == L_SCAN_VERTICAL.
    /// (2) For L_HORIZONTAL_SCAN, the output pix contains rank binned
    ///     median colors in each column that correspond to a vertical
    ///     strip of width %size in the input image.
    /// (3) The color selection flag is one of: L_SELECT_RED, L_SELECT_GREEN,
    ///     L_SELECT_BLUE, L_SELECT_MIN, L_SELECT_MAX, L_SELECT_AVERAGE,
    ///     L_SELECT_HUE, L_SELECT_SATURATION.
    ///     It determines how the rank ordering is done.
    /// (4) Typical input values might be %size = 5, %nbins = 10.
    /// </remarks>
    public Pix RankBinByStrip(int direction, int size, int nbins, int type)
    {
        const string func = "pixRankBinByStrip";
        PixColormap? cmap = Colormap;
        if (Depth != 32 && cmap == null)
            throw Lept.Arg(func, "pixs neither 32 bpp nor cmapped");
        if (direction != L_SCAN_HORIZONTAL && direction != L_SCAN_VERTICAL)
            throw Lept.Arg(func, "invalid direction");
        if (size < 1)
            throw Lept.Arg(func, "size < 1");
        if (nbins < 2)
            throw Lept.Arg(func, "nbins must be at least 2");
        if (type != L_SELECT_RED && type != L_SELECT_GREEN &&
            type != L_SELECT_BLUE && type != L_SELECT_MIN &&
            type != L_SELECT_MAX && type != L_SELECT_AVERAGE &&
            type != L_SELECT_HUE && type != L_SELECT_SATURATION)
            throw Lept.Arg(func, "invalid type");
        GetDimensions(out int w, out int h, out _);
        int mindim = Math.Min(w, h);
        if (mindim < 20 || nbins > mindim)
            throw Lept.Arg(func, "pix too small and/or too many bins");

            /* Remove colormap if it exists */
        Pix pix1;
        if (cmap != null)
            pix1 = RemoveColormap(REMOVE_CMAP_TO_FULL_COLOR);
        else
            pix1 = this;
        GetDimensions(out w, out h, out _);

        Pix pixd;
        Boxa boxa = Boxa.MakeMosaicStrips(w, h, direction, size);
        Pixa pixa = pix1.ClipRectangles(boxa);
        int nstrips = pixa.Count;
        if (direction == L_SCAN_HORIZONTAL) {
            pixd = Create(nstrips, nbins, 32);
            for (int i = 0; i < nstrips; i++) {
                Pix pix2 = pixa.GetPix(i, L_CLONE);
                uint[]? array = pix2.GetRankColorArray(nbins, type, 1, null, 0);
                if (array != null) {
                    for (int j = 0; j < nbins; j++)
                        pixd.SetPixel(i, j, array[j]);
                }
            }
        } else {  /* L_SCAN_VERTICAL */
            pixd = Create(nbins, nstrips, 32);
            for (int i = 0; i < nstrips; i++) {
                Pix pix2 = pixa.GetPix(i, L_CLONE);
                uint[]? array = pix2.GetRankColorArray(nbins, type, 1, null, 0);
                if (array != null) {
                    for (int j = 0; j < nbins; j++)
                        pixd.SetPixel(j, i, array[j]);
                }
            }
        }
        return pixd;
    }

    /*-------------------------------------------------------------*
     *                 Pixelwise aligned statistics                *
     *-------------------------------------------------------------*/
    // leptonica: pixGetRowStats (pix4.c)
    /// <summary>Computes a column vector of statistics using each row of an 8 bpp image.</summary>
    /// <param name="type">L_MEAN_ABSVAL, L_MEDIAN_VAL, L_MODE_VAL, L_MODE_COUNT</param>
    /// <param name="nbins">of histogram for median and mode; ignored for mean</param>
    /// <param name="thresh">on histogram for mode; ignored for mean and median</param>
    /// <param name="colvect">vector of results gathered across the rows of pixs</param>
    /// <remarks>
    /// (1) This computes a column vector of statistics using each
    ///     row of a Pix.  The result is put in %colvect.
    /// (2) The %thresh parameter works with L_MODE_VAL only, and
    ///     sets a minimum occupancy of the mode bin.
    ///     If the occupancy of the mode bin is less than %thresh, the
    ///     mode value is returned as 0.  To always return the actual
    ///     mode value, set %thresh = 0.
    /// (3) What is the meaning of this %thresh parameter?
    ///     For each row, the total count in the histogram is w, the
    ///     image width.  So %thresh, relative to w, gives a measure
    ///     of the ratio of the bin width to the width of the distribution.
    ///     The larger %thresh, the narrower the distribution must be
    ///     for the mode value to be returned (instead of returning 0).
    /// (4) If the Pix consists of a set of corresponding columns,
    ///     one for each Pix in a Pixa, the width of the Pix is the
    ///     number of Pix in the Pixa and the column vector can
    ///     be stored as a column in a Pix of the same size as
    ///     each Pix in the Pixa.
    /// </remarks>
    public void GetRowStats(int type, int nbins, int thresh, float[] colvect)
    {
        const string func = "pixGetRowStats";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not defined or not 8 bpp");
        if (type != L_MEAN_ABSVAL && type != L_MEDIAN_VAL &&
            type != L_MODE_VAL && type != L_MODE_COUNT)
            throw Lept.Arg(func, "invalid type");
        if (type != L_MEAN_ABSVAL && (nbins < 1 || nbins > 256))
            throw Lept.Arg(func, "invalid nbins");
        GetDimensions(out int w, out int h, out _);

        uint[] datas = Data;
        int wpls = Wpl;
        if (type == L_MEAN_ABSVAL) {
            for (int i = 0; i < h; i++) {
                int sum = 0;
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                for (int j = 0; j < w; j++)
                    sum += GetDataByte(lines, j);
                colvect[i] = (float)sum / (float)w;
            }
            return;
        }

            /* We need a histogram; binwidth ~ 256 / nbins */
        int[] histo = new int[nbins];
        int[] gray2bin = new int[256];
        int[] bin2gray = new int[nbins];
        for (int i = 0; i < 256; i++)  /* gray value --> histo bin */
            gray2bin[i] = (i * nbins) / 256;
        for (int i = 0; i < nbins; i++)  /* histo bin --> gray value */
            bin2gray[i] = (i * 256 + 128) / nbins;

        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            for (int k = 0; k < nbins; k++)
                histo[k] = 0;
            for (int j = 0; j < w; j++) {
                int val = GetDataByte(lines, j);
                histo[gray2bin[val]]++;
            }

            if (type == L_MEDIAN_VAL) {
                int sum = 0;
                int target = (w + 1) / 2;
                for (int k = 0; k < nbins; k++) {
                    sum += histo[k];
                    if (sum >= target) {
                        colvect[i] = bin2gray[k];
                        break;
                    }
                }
            } else if (type == L_MODE_VAL) {
                int max = 0;
                int modeval = 0;
                for (int k = 0; k < nbins; k++) {
                    if (histo[k] > max) {
                        max = histo[k];
                        modeval = k;
                    }
                }
                if (max < thresh)
                    colvect[i] = 0;
                else
                    colvect[i] = bin2gray[modeval];
            } else {  /* type == L_MODE_COUNT */
                int max = 0;
                for (int k = 0; k < nbins; k++) {
                    if (histo[k] > max)
                        max = histo[k];
                }
                colvect[i] = max;
            }
        }
    }

    // leptonica: pixGetColumnStats (pix4.c)
    /// <summary>Computes a row vector of statistics using each column of an 8 bpp image.</summary>
    /// <param name="type">L_MEAN_ABSVAL, L_MEDIAN_VAL, L_MODE_VAL, L_MODE_COUNT</param>
    /// <param name="nbins">of histogram for median and mode; ignored for mean</param>
    /// <param name="thresh">on histogram for mode val; ignored for all other types</param>
    /// <param name="rowvect">vector of results gathered down the columns of pixs</param>
    /// <remarks>
    /// (1) This computes a row vector of statistics using each
    ///     column of a Pix.  The result is put in %rowvect.
    /// (2) The %thresh parameter works with L_MODE_VAL only, and
    ///     sets a minimum occupancy of the mode bin.
    ///     If the occupancy of the mode bin is less than %thresh, the
    ///     mode value is returned as 0.  To always return the actual
    ///     mode value, set %thresh = 0.
    /// (3) What is the meaning of this %thresh parameter?
    ///     For each column, the total count in the histogram is h, the
    ///     image height.  So %thresh, relative to h, gives a measure
    ///     of the ratio of the bin width to the width of the distribution.
    ///     The larger %thresh, the narrower the distribution must be
    ///     for the mode value to be returned (instead of returning 0).
    /// </remarks>
    public void GetColumnStats(int type, int nbins, int thresh, float[] rowvect)
    {
        const string func = "pixGetColumnStats";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not defined or not 8 bpp");
        if (type != L_MEAN_ABSVAL && type != L_MEDIAN_VAL &&
            type != L_MODE_VAL && type != L_MODE_COUNT)
            throw Lept.Arg(func, "invalid type");
        if (type != L_MEAN_ABSVAL && (nbins < 1 || nbins > 256))
            throw Lept.Arg(func, "invalid nbins");
        GetDimensions(out int w, out int h, out _);

        uint[] datas = Data;
        int wpls = Wpl;
        if (type == L_MEAN_ABSVAL) {
            for (int j = 0; j < w; j++) {
                int sum = 0;
                for (int i = 0; i < h; i++)
                    sum += GetDataByte(datas.AsSpan(i * wpls, wpls), j);
                rowvect[j] = (float)sum / (float)h;
            }
            return;
        }

            /* We need a histogram; binwidth ~ 256 / nbins */
        int[] histo = new int[nbins];
        int[] gray2bin = new int[256];
        int[] bin2gray = new int[nbins];
        for (int i = 0; i < 256; i++)  /* gray value --> histo bin */
            gray2bin[i] = (i * nbins) / 256;
        for (int i = 0; i < nbins; i++)  /* histo bin --> gray value */
            bin2gray[i] = (i * 256 + 128) / nbins;

        for (int j = 0; j < w; j++) {
            for (int i = 0; i < h; i++) {
                int val = GetDataByte(datas.AsSpan(i * wpls, wpls), j);
                histo[gray2bin[val]]++;
            }

            if (type == L_MEDIAN_VAL) {
                int sum = 0;
                int target = (h + 1) / 2;
                for (int k = 0; k < nbins; k++) {
                    sum += histo[k];
                    if (sum >= target) {
                        rowvect[j] = bin2gray[k];
                        break;
                    }
                }
            } else if (type == L_MODE_VAL) {
                int max = 0;
                int modeval = 0;
                for (int k = 0; k < nbins; k++) {
                    if (histo[k] > max) {
                        max = histo[k];
                        modeval = k;
                    }
                }
                if (max < thresh)
                    rowvect[j] = 0;
                else
                    rowvect[j] = bin2gray[modeval];
            } else {  /* type == L_MODE_COUNT */
                int max = 0;
                for (int k = 0; k < nbins; k++) {
                    if (histo[k] > max)
                        max = histo[k];
                }
                rowvect[j] = max;
            }
            for (int k = 0; k < nbins; k++)
                histo[k] = 0;
        }
    }

    // leptonica: pixSetPixelColumn (pix4.c)
    /// <summary>Sets a column of an 8 bpp image from a vector of floats.</summary>
    /// <param name="col">column index</param>
    /// <param name="colvect">vector of floats</param>
    /// <remarks>
    /// NOTE(upstream): the check accepts col == w, one past the last column.
    /// </remarks>
    public void SetPixelColumn(int col, float[] colvect)
    {
        const string func = "pixSetPixelColumn";
        if (Depth != 8)
            throw Lept.Arg(func, "pix not defined or not 8 bpp");
        GetDimensions(out int w, out int h, out _);
        if (col < 0 || col > w)
            throw Lept.Arg(func, "invalid col");

        uint[] data = Data;
        int wpl = Wpl;
        for (int i = 0; i < h; i++)
            SetDataByte(data.AsSpan(i * wpl), col, (int)colvect[i]);
    }

    /*-------------------------------------------------------------*
     *              Foreground/background estimation               *
     *-------------------------------------------------------------*/
    // leptonica: pixThresholdForFgBg (pix4.c)
    /// <summary>Estimates the average foreground and background values.</summary>
    /// <param name="factor">subsampling factor; integer >= 1</param>
    /// <param name="thresh">threshold for generating foreground mask</param>
    /// <param name="fgval">average foreground value</param>
    /// <param name="bgval">average background value</param>
    /// <remarks>
    /// Both values are always computed (each is optional in C), so an image with no
    /// foreground or no background pixels throws.
    /// </remarks>
    public void ThresholdForFgBg(int factor, int thresh, out int fgval, out int bgval)
    {
        fgval = bgval = 0;

            /* Generate a subsampled 8 bpp version and a mask over the fg */
        Pix pixg = ConvertTo8BySampling(factor, 0);
        Pix pixm = pixg.ThresholdToBinary(thresh);

        float fval = pixg.GetAverageMasked(pixm, 0, 0, 1, L_MEAN_ABSVAL);
        fgval = (int)(fval + 0.5);

        Invert(pixm, pixm);
        fval = pixg.GetAverageMasked(pixm, 0, 0, 1, L_MEAN_ABSVAL);
        bgval = (int)(fval + 0.5);
    }

    // leptonica: pixSplitDistributionFgBg (pix4.c)
    /// <summary>Splits the gray distribution of an image into foreground and background.</summary>
    /// <param name="scorefract">Fraction of the maximum score, used to determine the range over which the histogram minimum is searched.</param>
    /// <param name="factor">Subsampling factor; an integer greater than or equal to 1.</param>
    /// <param name="thresh">Receives the best threshold for separating foreground from background.</param>
    /// <param name="fgval">Receives the average foreground value.</param>
    /// <param name="bgval">Receives the average background value.</param>
    /// <remarks>
    /// See <see cref="Numa.SplitDistribution(float, out int, out float, out float, out float, out float)"/> for details on the underlying method of choosing a threshold.
    /// </remarks>
    public void SplitDistributionFgBg(float scorefract, int factor, out int thresh,
                                      out int fgval, out int bgval)
    {
        SplitDistributionFgBgCore(scorefract, factor, false, out thresh, out fgval, out bgval, out _);
    }

    /// <summary>As the other overload, but also returns a plot of the distribution and the split point for debugging.</summary>
    /// <param name="scorefract">Fraction of the maximum score, used to determine the range over which the histogram minimum is searched.</param>
    /// <param name="factor">Subsampling factor; an integer greater than or equal to 1.</param>
    /// <param name="thresh">Receives the best threshold for separating foreground from background.</param>
    /// <param name="fgval">Receives the average foreground value.</param>
    /// <param name="bgval">Receives the average background value.</param>
    /// <param name="pixdb">Receives a plot of the distribution and the split point.</param>
    public void SplitDistributionFgBg(float scorefract, int factor, out int thresh,
                                      out int fgval, out int bgval, out Pix pixdb)
    {
        SplitDistributionFgBgCore(scorefract, factor, true, out thresh, out fgval, out bgval, out pixdb);
    }

    private void SplitDistributionFgBgCore(float scorefract, int factor, bool debug, out int pthresh,
                                           out int pfgval, out int pbgval, out Pix pixdb)
    {
        pthresh = pfgval = pbgval = 0;
        pixdb = null!;

            /* Generate a subsampled 8 bpp version */
        Pix pixg = ConvertTo8BySampling(factor, 0);

            /* Make the fg/bg estimates */
        Numa na = pixg.GetGrayHistogram(1);
        int thresh;
        float avefg, avebg;
        if (debug) {
            na.SplitDistribution(scorefract, out thresh, out avefg, out avebg,
                                 out _, out _, out _);
        } else {
            na.SplitDistribution(scorefract, out thresh, out avefg, out avebg,
                                 out _, out _);
        }

        pthresh = thresh;
        pfgval = (int)(avefg + 0.5);
        pbgval = (int)(avebg + 0.5);

        if (debug) {
            Utils.LeptMkdir("lept/redout");
            Gplot gplot = Gplot.Create("/tmp/lept/redout/histplot", GPLOT_PNG, "Histogram",
                                       "Grayscale value", "Number of pixels");
            gplot.AddPlot(null, na, GPLOT_LINES, null);
            Numa nax = Numa.MakeConstant(thresh, 2);
            na.GetMax(out float maxnum, out _);
            Numa nay = Numa.MakeConstant(0, 2);
            nay.ReplaceNumber(1, (int)(0.5 * maxnum));
            string buf = CFormat.Sprintf("score fract = %3.1f", (double)scorefract);
            gplot.AddPlot(nax, nay, GPLOT_LINES, buf);
            pixdb = gplot.MakeOutputPix();
        }
    }
}

public sealed partial class Pixa
{
    // leptonica: pixaGetAlignedStats (pix4.c)
    /// <summary>Gets a pix with pixelwise aligned statistics over a pixa of identically sized 8 bpp pix.</summary>
    /// <param name="type">L_MEAN_ABSVAL, L_MEDIAN_VAL, L_MODE_VAL, L_MODE_COUNT</param>
    /// <param name="nbins">of histogram for median and mode; ignored for mean</param>
    /// <param name="thresh">on histogram for mode val; ignored for all other types</param>
    /// <returns>pix with pixelwise aligned stats</returns>
    /// <remarks>
    /// (1) Each pixel in the returned pix represents an average
    ///     (or median, or mode) over the corresponding pixels in each
    ///     pix in the pixa.
    /// (2) The %thresh parameter works with L_MODE_VAL only, and
    ///     sets a minimum occupancy of the mode bin.
    ///     If the occupancy of the mode bin is less than %thresh, the
    ///     mode value is returned as 0.  To always return the actual
    ///     mode value, set %thresh = 0.  See pixGetRowStats().
    /// </remarks>
    public Pix GetAlignedStats(int type, int nbins, int thresh)
    {
        const string func = "pixaGetAlignedStats";
        if (type != L_MEAN_ABSVAL && type != L_MEDIAN_VAL &&
            type != L_MODE_VAL && type != L_MODE_COUNT)
            throw Lept.Arg(func, "invalid type");
        int n = Count;
        if (n == 0)
            throw Lept.Arg(func, "no pix in pixa");
        GetPixDimensions(0, out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "pix not 8 bpp");

        Pix pixd = Pix.Create(w, h, 8);
        Pix pixt = Pix.Create(n, h, 8);
        float[] colvect = new float[h];
        for (int j = 0; j < w; j++) {
            ExtractColumnFromEachPix(j, pixt);
            pixt.GetRowStats(type, nbins, thresh, colvect);
            pixd.SetPixelColumn(j, colvect);
        }

        return pixd;
    }

    // leptonica: pixaExtractColumnFromEachPix (pix4.c)
    /// <summary>Inserts a column of each pix of an 8 bpp pixa as a column of pixd.</summary>
    /// <param name="col">column index</param>
    /// <param name="pixd">pix into which each column is inserted</param>
    public void ExtractColumnFromEachPix(int col, Pix pixd)
    {
        const string func = "pixaExtractColumnFromEachPix";
        if (pixd.Depth != 8)
            throw Lept.Arg(func, "pixd not defined or not 8 bpp");
        int n = Count;
        pixd.GetDimensions(out int w, out int h, out _);
        if (n != w)
            throw Lept.Arg(func, "pix width != n");
        Pix pixt = GetPix(0, L_CLONE);
        int wplt = pixt.Wpl;
        pixt.GetDimensions(out _, out int ht, out _);
        if (h != ht)
            throw Lept.Arg(func, "pixd height != column height");

        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (int k = 0; k < n; k++) {
            pixt = GetPix(k, L_CLONE);
            uint[] datat = pixt.Data;
            int linet = 0;
            for (int i = 0; i < h; i++) {
                int val = GetDataByte(datat.AsSpan(linet, wplt), col);
                SetDataByte(datad.AsSpan(i * wpld, wpld), k, val);
                linet += wplt;
            }
        }
    }
}

public sealed partial class L_Amap
{
    // leptonica: amapGetCountForColor (pix4.c)
    /// <summary>Gets the count for a color from a map made by pixGetColorAmapHistogram().</summary>
    /// <param name="val">rgb or rgba pixel value</param>
    /// <returns>count</returns>
    /// <remarks>
    /// (1) The ordered map is made by pixGetColorAmapHistogram().
    /// </remarks>
    public int GetCountForColor(uint val)
    {
        RbType key = default;
        key.Utype = val;
        RbType? pval = Find(key);
        return (pval != null) ? (int)pval.Value.Itype : 0;
    }

}
