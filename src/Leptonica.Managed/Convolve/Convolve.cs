using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  convolve.c: Convolution and block-based image statistics.
 *
 *      Top level grayscale or color block convolution
 *          PIX          *pixBlockconv()
 *
 *      Grayscale block convolution
 *          PIX          *pixBlockconvGray()
 *          static void   blockconvLow()
 *
 *      Accumulator for 1, 8 and 32 bpp convolution
 *          PIX          *pixBlockconvAccum()
 *          static void   blockconvAccumLow()
 *
 *      Un-normalized grayscale block convolution
 *          PIX          *pixBlockconvGrayUnnormalized()
 *
 *      Tiled grayscale or color block convolution
 *          PIX          *pixBlockconvTiled()
 *          PIX          *pixBlockconvGrayTile()
 *
 *      Convolution for mean, mean square, variance and rms deviation
 *      in specified window
 *          l_int32       pixWindowedStats()
 *          PIX          *pixWindowedMean()
 *          PIX          *pixWindowedMeanSquare()
 *          l_int32       pixWindowedVariance()
 *          DPIX         *pixMeanSquareAccum()
 *
 *      Binary block sum and rank filter
 *          PIX          *pixBlockrank()
 *          PIX          *pixBlocksum()
 *          static void   blocksumLow()
 *
 *      Census transform
 *          PIX          *pixCensusTransform()
 *
 *      Generic convolution (with Pix)
 *          PIX          *pixConvolve()
 *          PIX          *pixConvolveSep()
 *          PIX          *pixConvolveRGB()
 *          PIX          *pixConvolveRGBSep()
 *
 *      Generic convolution (with float arrays)
 *          FPIX         *fpixConvolve()
 *          FPIX         *fpixConvolveSep()
 *
 *      Convolution with bias (for non-negative output)
 *          PIX          *pixConvolveWithBias()
 *
 *      Set parameter for convolution subsampling
 *          void          l_setConvolveSampling()
 *
 *      Additive gaussian noise
 *          PIX          *pixAddGaussNoise()
 *          l_float32     gaussDistribSampling()
 *
 *  Differences from C:
 *    - The globals ConvolveSamplingFactX/Y are public static fields of Pix;
 *      l_setConvolveSampling() is Pix.SetConvolveSampling().
 *    - pixConvolveSep() and fpixConvolveSep() restore the sampling factors
 *      also when an exception passes through.
 *    - The static state of gaussDistribSampling() is thread-static, like the
 *      generator of CRand.
 *    - Optional output pointers become out parameters.  pixWindowedStats()
 *      and pixWindowedVariance() have a private "Core" variant with flags
 *      telling which outputs are wanted; the public variant requests all.
 *    - The conversion (l_uint8)(norm * val) in pixWindowedMean() is written
 *      (byte)(int)(...): for the un-normalized sum it wraps like the C code
 *      on x86 instead of depending on the runtime version.
 *
 *  Stubs for callees in other files: pixTilingCreate, pixTilingGetTile,
 *  pixTilingPaintTile (pixtiling.c).
 */

/// <summary>Constants from pix.h used by fpixConvertToPix().</summary>
public static partial class L
{
    /// <summary>Clips negative values to zero.</summary>
    public const int L_CLIP_TO_ZERO = 1;
}

public sealed partial class Pix
{
        /* These globals determine the subsampling factors for
         * generic convolution of pix and fpix.
         * To change the values, use SetConvolveSampling(). */
    /// <summary>The horizontal subsampling factor used by generic convolution; the default is 1.</summary>
    public static int ConvolveSamplingFactX = 1;

    /// <summary>The vertical subsampling factor used by generic convolution; the default is 1.</summary>
    public static int ConvolveSamplingFactY = 1;

    /*----------------------------------------------------------------------*
     *             Top-level grayscale or color block convolution           *
     *----------------------------------------------------------------------*/
    // leptonica: pixBlockconv (convolve.c)
    /// <summary>Top-level grayscale or color block convolution.</summary>
    /// <param name="wc">half width of the convolution kernel</param>
    /// <param name="hc">half height of the convolution kernel</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// This pix is 8 or 32 bpp; or 2, 4 or 8 bpp with colormap.
    /// (1) The full width and height of the convolution kernel
    ///     are (2 * wc + 1) and (2 * hc + 1)
    /// (2) Returns a copy if either wc or hc are 0
    /// (3) Require that w >= 2 * wc + 1 and h >= 2 * hc + 1,
    ///     where (w,h) are the dimensions of pixs.  Attempt to
    ///     reduce the kernel size if necessary.
    /// </remarks>
    public Pix Blockconv(int wc, int hc)
    {
        const string func = "pixBlockconv";
        if (wc <= 0 || hc <= 0)
            return Copy(null, this);
        GetDimensions(out int w, out int h, out int d);
        if (w < 2 * wc + 1 || h < 2 * hc + 1) {
            Log.Warning(func, $"kernel too large: wc = {wc}, hc = {hc}, w = {w}, h = {h}; reducing!");
            wc = Math.Min(wc, (w - 1) / 2);
            hc = Math.Min(hc, (h - 1) / 2);
        }
        if (wc == 0 || hc == 0)   /* no-op */
            return Copy(null, this);

            /* Remove colormap if necessary */
        Pix pixs;
        if ((d == 2 || d == 4 || d == 8) && Colormap != null) {
            Log.Warning(func, "pix has colormap; removing");
            pixs = RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            d = pixs.Depth;
        } else {
            pixs = this;  /* all block convolution paths below are read-only on pixs */
        }

        if (d != 8 && d != 32)
            throw Lept.Arg(func, "depth not 8 or 32 bpp");

        Pix pixd;
        if (d == 8) {
            pixd = pixs.BlockconvGray(null, wc, hc);
        } else { /* d == 32 */
            Pix pixr = pixs.GetRGBComponent(L.COLOR_RED);
            Pix pixrc = pixr.BlockconvGray(null, wc, hc);
            Pix pixg = pixs.GetRGBComponent(L.COLOR_GREEN);
            Pix pixgc = pixg.BlockconvGray(null, wc, hc);
            Pix pixb = pixs.GetRGBComponent(L.COLOR_BLUE);
            Pix pixbc = pixb.BlockconvGray(null, wc, hc);
            pixd = CreateRGBImage(pixrc, pixgc, pixbc);
        }

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *                     Grayscale block convolution                      *
     *----------------------------------------------------------------------*/
    // leptonica: pixBlockconvGray (convolve.c)
    /// <summary>Grayscale block convolution.</summary>
    /// <param name="pixacc">pix 32 bpp; can be null</param>
    /// <param name="wc">half width of the convolution kernel</param>
    /// <param name="hc">half height of the convolution kernel</param>
    /// <returns>pix 8 bpp</returns>
    /// <remarks>
    /// This pix is 8 bpp.
    /// (1) If accum pix is null, make one and destroy it before
    ///     returning; otherwise, just use the input accum pix.
    /// (2) The full width and height of the convolution kernel
    ///     are (2 * wc + 1) and (2 * hc + 1).
    /// (3) Returns a copy if either wc or hc are 0
    /// (4) Require that w >= 2 * wc + 1 and h >= 2 * hc + 1,
    ///     where (w,h) are the dimensions of pixs.  Attempt to
    ///     reduce the kernel size if necessary.
    /// </remarks>
    public Pix BlockconvGray(Pix? pixacc, int wc, int hc)
    {
        const string func = "pixBlockconvGray";
        GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (wc <= 0 || hc <= 0)   /* no-op */
            return Copy(null, this);
        if (w < 2 * wc + 1 || h < 2 * hc + 1) {
            Log.Warning(func, $"kernel too large: wc = {wc}, hc = {hc}, w = {w}, h = {h}; reducing!");
            wc = Math.Min(wc, (w - 1) / 2);
            hc = Math.Min(hc, (h - 1) / 2);
        }
        if (wc == 0 || hc == 0)
            return Copy(null, this);

        Pix pixt;
        if (pixacc != null) {
            if (pixacc.Depth == 32) {
                pixt = Clone(pixacc);
            } else {
                Log.Warning(func, "pixacc not 32 bpp; making new one");
                pixt = BlockconvAccum();
            }
        } else {
            pixt = BlockconvAccum();
        }

        Pix pixd = CreateTemplateNoInit(this);

        pixt.SetPadBits(0);
        int wpl = pixd.Wpl;
        int wpla = pixt.Wpl;
        BlockconvLow(pixd.Data, w, h, wpl, pixt.Data, wpla, wc, hc);

        return pixd;
    }

    /// <summary>Computes four adjacent normalized window sums from the accumulator rows.</summary>
    /// <param name="rowMaxA">The accumulator row at the larger vertical offset.</param>
    /// <param name="rowMinA">The accumulator row at the smaller vertical offset.</param>
    /// <param name="jmax">The column index of the right edge of the window.</param>
    /// <param name="jmin">The column index of the left edge of the window.</param>
    /// <param name="off">The offset of the first of the four adjacent pixels.</param>
    /// <param name="nv">The normalization factor, broadcast to all lanes.</param>
    /// <param name="half">The rounding constant, broadcast to all lanes.</param>
    /// <returns>The four normalized, rounded sums.</returns>
    // Four window sums -> four bytes (as uint lanes): (int)(norm * v + 0.5f)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> Q(ref uint rowMaxA, ref uint rowMinA, int jmax, int jmin,
                                     int off, Vector128<float> nv, Vector128<float> half)
    {
        var v = Vector128.LoadUnsafe(ref rowMaxA, (nuint)(jmax + off))
              - Vector128.LoadUnsafe(ref rowMaxA, (nuint)(jmin + off))
              + Vector128.LoadUnsafe(ref rowMinA, (nuint)(jmin + off))
              - Vector128.LoadUnsafe(ref rowMinA, (nuint)(jmax + off));
        var f = Vector128.ConvertToSingle(v.AsInt32()) * nv + half;
        return Vector128.ConvertToInt32(f).AsUInt32();
    }

    internal static bool BlockconvSimdEnabled = true;  // test hook
    internal static bool ConvolveSimdEnabled = true;  // test hook

    // leptonica: blockconvLow (convolve.c)
    /// <summary>Computes the block convolution of an image from its accumulator image, in place.</summary>
    /// <param name="data">The data of the image to be convolved; it is overwritten with the result.</param>
    /// <param name="w">The width of the image in pixels.</param>
    /// <param name="h">The height of the image in pixels.</param>
    /// <param name="wpl">The number of 32-bit words per line of <paramref name="data"/>.</param>
    /// <param name="dataa">The data of the 32 bpp accumulator image.</param>
    /// <param name="wpla">The number of 32-bit words per line of the accumulator.</param>
    /// <param name="wc">The convolution half-width.</param>
    /// <param name="hc">The convolution half-height.</param>
    /// <remarks>
    /// <para>The full width and height of the convolution kernel are (2 * wc + 1) and (2 * hc + 1).</para>
    /// <para>
    /// The lack of symmetry between the handling of the first (hc + 1) lines and the last (hc) lines,
    /// and similarly with the columns, is due to the fact that for the pixel at (x,y) the accumulator
    /// values are taken at (x + wc, y + hc), (x - wc - 1, y + hc), (x + wc, y - hc - 1) and
    /// (x - wc - 1, y - hc - 1).
    /// </para>
    /// <para>
    /// The sums are normalized as if there were no reduced area at the boundary. This under-estimates
    /// the value of the boundary pixels, so they are multiplied by another normalization factor that
    /// is greater than 1. This second normalization is done first for the first hc + 1 lines, then
    /// for the last hc lines, and finally for the first wc + 1 and last wc columns in the
    /// intermediate lines.
    /// </para>
    /// <para>
    /// The caller must verify that <paramref name="wc"/> is less than <paramref name="w"/> and
    /// <paramref name="hc"/> is less than <paramref name="h"/>; otherwise illegal reads and writes can occur.
    /// </para>
    /// <para>
    /// To get the same results in the interior as <c>Convolve</c>, 0.5 is added for roundoff in the main
    /// loop. Adding 0.25 instead avoids overflow near the boundary but gives off-by-one errors in
    /// interior pixel values. So 0.5 is added in the main loop, and for pixels within half a filter
    /// width of the boundary the computed value is limited to at most 255 to avoid overflow during
    /// normalization.
    /// </para>
    /// </remarks>
    private static void BlockconvLow(uint[] data, int w, int h, int wpl,
                                     uint[] dataa, int wpla, int wc, int hc)
    {
        int wmwc = w - wc;
        int hmhc = h - hc;
        if (wmwc <= 0 || hmhc <= 0) {
            Log.Error("blockconvLow", "wc >= w || hc >= h");
            return;
        }
        int fwc = 2 * wc + 1;
        int fhc = 2 * hc + 1;
        float norm = (float)(1.0 / ((float)(fwc) * fhc));
        uint val;
        int wn, hn;
        float normh, normw;

            /*------------------------------------------------------------*
             *  Compute, using b.c. only to set limits on the accum image *
             *------------------------------------------------------------*/
            /* NOTE(upstream): imin and jmin are clamped to 0, so row 0 and column 0 are
             * left out of the window near the top and left edge; see UPSTREAM_NOTES.md. */
        bool simdOk = BlockconvSimdEnabled && Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian
                      && 255L * fwc * fhc < (1L << 22);
        LeptParallel.ForRows(h, (long)w * 6L, (yStart, yEnd) => {
            ref uint baseA = ref MemoryMarshal.GetArrayDataReference(dataa);
            ref uint baseD = ref MemoryMarshal.GetArrayDataReference(data);
            int jInteriorStart = wc + 1;
            int jInteriorEnd = w - wc;
            for (int i = yStart; i < yEnd; i++) {
                int imin = Math.Max(i - 1 - hc, 0);
                int imax = Math.Min(i + hc, h - 1);
                ref uint lineRef = ref Unsafe.Add(ref baseD, (nuint)(wpl * i));
                ref uint rowMinA = ref Unsafe.Add(ref baseA, (nuint)(wpla * imin));
                ref uint rowMaxA = ref Unsafe.Add(ref baseA, (nuint)(wpla * imax));

                if (jInteriorStart < jInteriorEnd) {
                    int leftLimit = Math.Min(jInteriorStart, w);
                    for (int jl = 0; jl < leftLimit; jl++) {
                        int jmin = Math.Max(jl - 1 - wc, 0);
                        int jmax = Math.Min(jl + wc, w - 1);
                        uint v = Unsafe.Add(ref rowMaxA, (nuint)jmax) - Unsafe.Add(ref rowMaxA, (nuint)jmin)
                               + Unsafe.Add(ref rowMinA, (nuint)jmin) - Unsafe.Add(ref rowMinA, (nuint)jmax);
                        SetDataByteUnchecked(ref lineRef, jl, (byte)(norm * v + 0.5));
                    }
                    int j = jInteriorStart;
                    for (; j < jInteriorEnd && (j & 3) != 0; j++) {
                        int jmin = j - 1 - wc;
                        int jmax = j + wc;
                        uint v = Unsafe.Add(ref rowMaxA, (nuint)jmax) - Unsafe.Add(ref rowMaxA, (nuint)jmin)
                               + Unsafe.Add(ref rowMinA, (nuint)jmin) - Unsafe.Add(ref rowMinA, (nuint)jmax);
                        SetDataByteUnchecked(ref lineRef, j, (byte)(norm * v + 0.5));
                    }
                    if (simdOk) {
                            /* 16 pixels per step; exact same float arithmetic as the
                             * scalar path (product in float, +0.5, truncate). */
                        var nv = Vector128.Create(norm);
                        var half = Vector128.Create(0.5f);
                        var rev = Vector128.Create((byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12);
                        ref byte lineB = ref Unsafe.As<uint, byte>(ref lineRef);
                        for (; j + 16 <= jInteriorEnd; j += 16) {
                            int jmin = j - 1 - wc;
                            int jmax = j + wc;
                            var q0 = Q(ref rowMaxA, ref rowMinA, jmax, jmin, 0, nv, half);
                            var q1 = Q(ref rowMaxA, ref rowMinA, jmax, jmin, 4, nv, half);
                            var q2 = Q(ref rowMaxA, ref rowMinA, jmax, jmin, 8, nv, half);
                            var q3 = Q(ref rowMaxA, ref rowMinA, jmax, jmin, 12, nv, half);
                            var bytes = Vector128.Narrow(Vector128.Narrow(q0, q1), Vector128.Narrow(q2, q3));
                            Vector128.StoreUnsafe(Vector128.Shuffle(bytes, rev), ref lineB, (nuint)j);
                        }
                    }
                    for (; j + 4 <= jInteriorEnd; j += 4) {
                        int jmin = j - 1 - wc;
                        int jmax = j + wc;
                        uint v0 = Unsafe.Add(ref rowMaxA, (nuint)jmax) - Unsafe.Add(ref rowMaxA, (nuint)jmin)
                                + Unsafe.Add(ref rowMinA, (nuint)jmin) - Unsafe.Add(ref rowMinA, (nuint)jmax);
                        uint v1 = Unsafe.Add(ref rowMaxA, (nuint)(jmax + 1)) - Unsafe.Add(ref rowMaxA, (nuint)(jmin + 1))
                                + Unsafe.Add(ref rowMinA, (nuint)(jmin + 1)) - Unsafe.Add(ref rowMinA, (nuint)(jmax + 1));
                        uint v2 = Unsafe.Add(ref rowMaxA, (nuint)(jmax + 2)) - Unsafe.Add(ref rowMaxA, (nuint)(jmin + 2))
                                + Unsafe.Add(ref rowMinA, (nuint)(jmin + 2)) - Unsafe.Add(ref rowMinA, (nuint)(jmax + 2));
                        uint v3 = Unsafe.Add(ref rowMaxA, (nuint)(jmax + 3)) - Unsafe.Add(ref rowMaxA, (nuint)(jmin + 3))
                                + Unsafe.Add(ref rowMinA, (nuint)(jmin + 3)) - Unsafe.Add(ref rowMinA, (nuint)(jmax + 3));
                        uint b0 = (byte)(norm * v0 + 0.5);
                        uint b1 = (byte)(norm * v1 + 0.5);
                        uint b2 = (byte)(norm * v2 + 0.5);
                        uint b3 = (byte)(norm * v3 + 0.5);
                        Unsafe.Add(ref lineRef, (nuint)(j >> 2)) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                    }
                    for (; j < w; j++) {
                        int jmin = Math.Max(j - 1 - wc, 0);
                        int jmax = Math.Min(j + wc, w - 1);
                        uint v = Unsafe.Add(ref rowMaxA, (nuint)jmax) - Unsafe.Add(ref rowMaxA, (nuint)jmin)
                               + Unsafe.Add(ref rowMinA, (nuint)jmin) - Unsafe.Add(ref rowMinA, (nuint)jmax);
                        SetDataByteUnchecked(ref lineRef, j, (byte)(norm * v + 0.5));
                    }
                } else {
                    for (int j = 0; j < w; j++) {
                        int jmin = Math.Max(j - 1 - wc, 0);
                        int jmax = Math.Min(j + wc, w - 1);
                        uint v = Unsafe.Add(ref rowMaxA, (nuint)jmax) - Unsafe.Add(ref rowMaxA, (nuint)jmin)
                               + Unsafe.Add(ref rowMinA, (nuint)jmin) - Unsafe.Add(ref rowMinA, (nuint)jmax);
                        SetDataByteUnchecked(ref lineRef, j, (byte)(norm * v + 0.5));
                    }
                }
            }
        });

            /*------------------------------------------------------------*
             *             Fix normalization for boundary pixels          *
             *------------------------------------------------------------*/
        for (int i = 0; i <= hc; i++) {    /* first hc + 1 lines */
            hn = Math.Max(1, hc + i);
            normh = (float)fhc / (float)hn;   /* >= 1 */
            Span<uint> line = data.AsSpan(wpl * i);
            for (int j = 0; j <= wc; j++) {
                wn = Math.Max(1, wc + j);
                normw = (float)fwc / (float)wn;   /* >= 1 */
                val = (uint)GetDataByte(line, j);
                val = (byte)Math.Min(val * normh * normw, 255f);
                SetDataByte(line, j, (int)val);
            }
            for (int j = wc + 1; j < wmwc; j++) {
                val = (uint)GetDataByte(line, j);
                val = (byte)Math.Min(val * normh, 255f);
                SetDataByte(line, j, (int)val);
            }
            for (int j = wmwc; j < w; j++) {
                wn = wc + w - j;
                normw = (float)fwc / (float)wn;   /* > 1 */
                val = (uint)GetDataByte(line, j);
                val = (byte)Math.Min(val * normh * normw, 255f);
                SetDataByte(line, j, (int)val);
            }
        }

        for (int i = hmhc; i < h; i++) {  /* last hc lines */
            hn = hc + h - i;
            normh = (float)fhc / (float)hn;   /* > 1 */
            Span<uint> line = data.AsSpan(wpl * i);
            for (int j = 0; j <= wc; j++) {
                wn = wc + j;
                normw = (float)fwc / (float)wn;   /* > 1 */
                val = (uint)GetDataByte(line, j);
                val = (byte)Math.Min(val * normh * normw, 255f);
                SetDataByte(line, j, (int)val);
            }
            for (int j = wc + 1; j < wmwc; j++) {
                val = (uint)GetDataByte(line, j);
                val = (byte)Math.Min(val * normh, 255f);
                SetDataByte(line, j, (int)val);
            }
            for (int j = wmwc; j < w; j++) {
                wn = wc + w - j;
                normw = (float)fwc / (float)wn;   /* > 1 */
                val = (uint)GetDataByte(line, j);
                val = (byte)Math.Min(val * normh * normw, 255f);
                SetDataByte(line, j, (int)val);
            }
        }

        for (int i = hc + 1; i < hmhc; i++) {    /* intermediate lines */
            Span<uint> line = data.AsSpan(wpl * i);
            for (int j = 0; j <= wc; j++) {   /* first wc + 1 columns */
                wn = wc + j;
                normw = (float)fwc / (float)wn;   /* > 1 */
                val = (uint)GetDataByte(line, j);
                val = (byte)Math.Min(val * normw, 255f);
                SetDataByte(line, j, (int)val);
            }
            for (int j = wmwc; j < w; j++) {   /* last wc columns */
                wn = wc + w - j;
                normw = (float)fwc / (float)wn;   /* > 1 */
                val = (uint)GetDataByte(line, j);
                val = (byte)Math.Min(val * normw, 255f);
                SetDataByte(line, j, (int)val);
            }
        }
    }

    /*----------------------------------------------------------------------*
     *              Accumulator for 1, 8 and 32 bpp convolution             *
     *----------------------------------------------------------------------*/
    // leptonica: pixBlockconvAccum (convolve.c)
    /// <summary>Accumulator for 1, 8 and 32 bpp convolution.</summary>
    /// <returns>accum pix 32 bpp</returns>
    /// <remarks>
    /// This pix is 1, 8 or 32 bpp.
    /// (1) The general recursion relation is
    ///       a(i,j) = v(i,j) + a(i-1, j) + a(i, j-1) - a(i-1, j-1)
    ///     For the first line, this reduces to the special case
    ///       a(i,j) = v(i,j) + a(i, j-1)
    ///     For the first column, the special case is
    ///       a(i,j) = v(i,j) + a(i-1, j)
    /// </remarks>
    public Pix BlockconvAccum()
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 8 && d != 32)
            throw Lept.Arg("pixBlockconvAccum", "pixs not 1, 8 or 32 bpp");
        Pix pixd = CreateRawNoInit(w, h, 32);

        BlockconvAccumLow(pixd.Data, w, h, pixd.Wpl, Data, d, Wpl);

        return pixd;
    }

    // leptonica: blockconvAccumLow (convolve.c)
    /// <param name="datad">The data of the 32 bpp destination (accumulator) image.</param>
    /// <param name="w">The width of the image in pixels.</param>
    /// <param name="h">The height of the image in pixels.</param>
    /// <param name="wpld">The number of 32-bit words per line of the destination.</param>
    /// <param name="datas">The data of the 1, 8 or 32 bpp source image.</param>
    /// <param name="d">The depth of the source in bits per pixel.</param>
    /// <param name="wpls">The number of 32-bit words per line of the source.</param>
    /// <remarks>
    /// (1) The general recursion relation is
    ///        a(i,j) = v(i,j) + a(i-1, j) + a(i, j-1) - a(i-1, j-1)
    ///     For the first line, this reduces to the special case
    ///        a(0,j) = v(0,j) + a(0, j-1), j > 0
    ///     For the first column, the special case is
    ///        a(i,0) = v(i,0) + a(i-1, 0), i > 0
    /// </remarks>
    private static void BlockconvAccumLow(uint[] datad, int w, int h, int wpld,
                                          uint[] datas, int d, int wpls)
    {
        ReadOnlySpan<uint> lines = datas;
        int lined = 0;
        int linedp;
        uint val;

        if (d == 1) {
                /* Do the first line */
            for (int j = 0; j < w; j++) {
                val = (byte)GetDataBit(lines, j);
                if (j == 0)
                    datad[lined] = val;
                else
                    datad[lined + j] = datad[lined + j - 1] + val;
            }

                /* Do the other lines */
            for (int i = 1; i < h; i++) {
                lines = datas.AsSpan(i * wpls);
                lined = i * wpld;  /* curr dest line */
                linedp = lined - wpld;   /* prev dest line */
                for (int j = 0; j < w; j++) {
                    val = (byte)GetDataBit(lines, j);
                    if (j == 0)
                        datad[lined] = val + datad[linedp];
                    else
                        datad[lined + j] = val + datad[lined + j - 1] + datad[linedp + j]
                                           - datad[linedp + j - 1];
                }
            }
        } else if (d == 8) {
            ref uint rs0 = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint rd0 = ref MemoryMarshal.GetArrayDataReference(datad);
            uint rowSum = 0;
            int j = 0;
            for (int jw = 0; j + 4 <= w; j += 4, jw++) {
                uint sw = Unsafe.Add(ref rs0, (nuint)jw);
                rowSum += sw >> 24;
                Unsafe.Add(ref rd0, (nuint)j) = rowSum;
                rowSum += (sw >> 16) & 0xff;
                Unsafe.Add(ref rd0, (nuint)(j + 1)) = rowSum;
                rowSum += (sw >> 8) & 0xff;
                Unsafe.Add(ref rd0, (nuint)(j + 2)) = rowSum;
                rowSum += sw & 0xff;
                Unsafe.Add(ref rd0, (nuint)(j + 3)) = rowSum;
            }
            for (; j < w; j++) {
                rowSum += (uint)GetDataByteUnchecked(ref rs0, j);
                Unsafe.Add(ref rd0, (nuint)j) = rowSum;
            }

                /* Do the other lines: rowSum(i, j) + datad[i-1, j] */
            for (int i = 1; i < h; i++) {
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
                ref uint rdp = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)((i - 1) * wpld));
                rowSum = 0;
                j = 0;
                for (int jw = 0; j + 4 <= w; j += 4, jw++) {
                    uint sw = Unsafe.Add(ref rs, (nuint)jw);
                    rowSum += sw >> 24;
                    Unsafe.Add(ref rd, (nuint)j) = rowSum + Unsafe.Add(ref rdp, (nuint)j);
                    rowSum += (sw >> 16) & 0xff;
                    Unsafe.Add(ref rd, (nuint)(j + 1)) = rowSum + Unsafe.Add(ref rdp, (nuint)(j + 1));
                    rowSum += (sw >> 8) & 0xff;
                    Unsafe.Add(ref rd, (nuint)(j + 2)) = rowSum + Unsafe.Add(ref rdp, (nuint)(j + 2));
                    rowSum += sw & 0xff;
                    Unsafe.Add(ref rd, (nuint)(j + 3)) = rowSum + Unsafe.Add(ref rdp, (nuint)(j + 3));
                }
                for (; j < w; j++) {
                    rowSum += (uint)GetDataByteUnchecked(ref rs, j);
                    Unsafe.Add(ref rd, (nuint)j) = rowSum + Unsafe.Add(ref rdp, (nuint)j);
                }
            }
        } else if (d == 32) {
                /* Do the first line */
            for (int j = 0; j < w; j++) {
                val = lines[j];
                if (j == 0)
                    datad[lined] = val;
                else
                    datad[lined + j] = datad[lined + j - 1] + val;
            }

                /* Do the other lines */
            for (int i = 1; i < h; i++) {
                lines = datas.AsSpan(i * wpls);
                lined = i * wpld;  /* curr dest line */
                linedp = lined - wpld;   /* prev dest line */
                for (int j = 0; j < w; j++) {
                    val = lines[j];
                    if (j == 0)
                        datad[lined] = val + datad[linedp];
                    else
                        datad[lined + j] = val + datad[lined + j - 1] + datad[linedp + j]
                                           - datad[linedp + j - 1];
                }
            }
        } else {
            Log.Error("blockconvAccumLow", "depth not 1, 8 or 32 bpp");
        }
    }

    /*----------------------------------------------------------------------*
     *               Un-normalized grayscale block convolution              *
     *----------------------------------------------------------------------*/
    // leptonica: pixBlockconvGrayUnnormalized (convolve.c)
    /// <summary>Un-normalized grayscale block convolution.</summary>
    /// <param name="wc">half width of the convolution kernel</param>
    /// <param name="hc">half height of the convolution kernel</param>
    /// <returns>pix 32 bpp; containing the convolution without normalizing
    /// for the window size</returns>
    /// <remarks>
    /// This pix is 8 bpp.
    /// (1) The full width and height of the convolution kernel
    ///     are (2 * wc + 1) and (2 * hc + 1).
    /// (2) Require that w >= 2 * wc + 1 and h >= 2 * hc + 1,
    ///     where (w,h) are the dimensions of pixs.  Attempt to
    ///     reduce the kernel size if necessary.
    /// (3) Returns a copy if either wc or hc are 0.
    /// (3) Adds mirrored border to avoid treating the boundary pixels
    ///     specially.  Note that we add wc + 1 pixels to the left
    ///     and wc to the right.  The added width is 2 * wc + 1 pixels,
    ///     and the particular choice simplifies the indexing in the loop.
    ///     Likewise, add hc + 1 pixels to the top and hc to the bottom.
    /// (4) To get the normalized result, divide by the area of the
    ///     convolution kernel: (2 * wc + 1) * (2 * hc + 1)
    ///     Specifically, do this:
    ///          pixc = pixBlockconvGrayUnnormalized(pixs, wc, hc);
    ///          fract = 1. / ((2 * wc + 1) * (2 * hc + 1));
    ///          pixMultConstantGray(pixc, fract);
    ///          pixd = pixGetRGBComponent(pixc, L_ALPHA_CHANNEL);
    /// (5) Unlike pixBlockconvGray(), this always computes the accumulation
    ///     pix because its size is tied to wc and hc.
    /// (6) Compare this implementation with pixBlockconvGray(), where
    ///     most of the code in blockconvLow() is special casing for
    ///     efficiently handling the boundary.  Here, the use of
    ///     mirrored borders and destination indexing makes the
    ///     implementation very simple.
    /// </remarks>
    public Pix BlockconvGrayUnnormalized(int wc, int hc)
    {
        const string func = "pixBlockconvGrayUnnormalized";
        GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (wc <= 0 || hc <= 0)  /* no-op */
            return Copy(null, this);
        if (w < 2 * wc + 1 || h < 2 * hc + 1) {
            Log.Warning(func, $"kernel too large: wc = {wc}, hc = {hc}, w = {w}, h = {h}; reducing!");
            wc = Math.Min(wc, (w - 1) / 2);
            hc = Math.Min(hc, (h - 1) / 2);
        }
        if (wc == 0 || hc == 0)
            return Copy(null, this);

        Pix pixsb = AddMirroredBorder(wc + 1, wc, hc + 1, hc);
        Pix pixacc = pixsb.BlockconvAccum();
        Pix pixd = Create(w, h, 32);

        int wpla = pixacc.Wpl;
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        uint[] dataa = pixacc.Data;
        for (int i = 0; i < h; i++) {
            int lined = i * wpld;
            int linemina = i * wpla;
            int linemaxa = (i + 2 * hc + 1) * wpla;
            for (int j = 0; j < w; j++) {
                int jmax = j + 2 * wc + 1;
                datad[lined + j] = dataa[linemaxa + jmax] - dataa[linemaxa + j] -
                                   dataa[linemina + jmax] + dataa[linemina + j];
            }
        }

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *               Tiled grayscale or color block convolution             *
     *----------------------------------------------------------------------*/
    // leptonica: pixBlockconvTiled (convolve.c)
    /// <summary>Tiled grayscale or color block convolution.</summary>
    /// <param name="wc">half width of the convolution kernel</param>
    /// <param name="hc">half height of the convolution kernel</param>
    /// <param name="nx">subdivision into tiles</param>
    /// <param name="ny">subdivision into tiles</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// This pix is 8 or 32 bpp; or 2, 4 or 8 bpp with colormap.
    /// (1) The full width and height of the convolution kernel
    ///     are (2 * wc + 1) and (2 * hc + 1)
    /// (2) Returns a copy if either wc or hc are 0.
    /// (3) Require that w >= 2 * wc + 1 and h >= 2 * hc + 1,
    ///     where (w,h) are the dimensions of pixs.  Attempt to
    ///     reduce the kernel size if necessary.
    /// (4) For nx == ny == 1, this defaults to pixBlockconv(), which
    ///     is typically about twice as fast, and gives nearly
    ///     identical results as pixBlockconvGrayTile().
    /// (5) If the tiles are too small, nx and/or ny are reduced
    ///     a minimum amount so that the tiles are expanded to the
    ///     smallest workable size in the problematic direction(s).
    /// (6) Why a tiled version?  Three reasons:
    ///     (a) Because the accumulator is a uint32, overflow can occur
    ///         for an image with more than 16M pixels.
    ///     (b) The accumulator array for 16M pixels is 64 MB; using
    ///         tiles reduces the size of this array.
    ///     (c) Each tile can be processed independently, in parallel,
    ///         on a multicore processor.
    /// </remarks>
    public Pix BlockconvTiled(int wc, int hc, int nx, int ny)
    {
        const string func = "pixBlockconvTiled";
        if (wc <= 0 || hc <= 0)   /* no-op */
            return Copy(null, this);
        if (nx <= 1 && ny <= 1)
            return Blockconv(wc, hc);
        GetDimensions(out int w, out int h, out int d);
        if (w < 2 * wc + 3 || h < 2 * hc + 3) {
            Log.Warning(func, $"kernel too large: wc = {wc}, hc = {hc}, w = {w}, h = {h}; reducing!");
            wc = Math.Min(wc, (w - 1) / 2);
            hc = Math.Min(hc, (h - 1) / 2);
        }
        if (wc == 0 || hc == 0)
            return Copy(null, this);

            /* Test to see if the tiles are too small.  The required
             * condition is that the tile dimensions must be at least
             * (wc + 2) x (hc + 2). */
        int xrat = w / nx;
        int yrat = h / ny;
        if (xrat < wc + 2) {
            nx = w / (wc + 2);
            Log.Warning(func, $"tile width too small; nx reduced to {nx}");
        }
        if (yrat < hc + 2) {
            ny = h / (hc + 2);
            Log.Warning(func, $"tile height too small; ny reduced to {ny}");
        }

            /* Remove colormap if necessary */
        Pix pixs;
        if ((d == 2 || d == 4 || d == 8) && Colormap != null) {
            Log.Warning(func, "pix has colormap; removing");
            pixs = RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            d = pixs.Depth;
        } else {
            pixs = Clone(this);
        }

        if (d != 8 && d != 32)
            throw Lept.Arg(func, "depth not 8 or 32 bpp");

           /* Note that the overlaps in the width and height that
            * are added to the tile are (wc + 2) and (hc + 2).
            * These overlaps are removed by pixTilingPaintTile().
            * They are larger than the extent of the filter because
            * although the filter is symmetric with respect to its origin,
            * the implementation is asymmetric -- see the implementation in
            * pixBlockconvGrayTile(). */
        Pix pixd = CreateTemplate(pixs);
        PixTiling pt = PixTiling.Create(pixs, nx, ny, 0, 0, wc + 2, hc + 2);
        for (int i = 0; i < ny; i++) {
            for (int j = 0; j < nx; j++) {
                Pix pixt = pt.GetTile(i, j);

                    /* Convolve over the tile */
                Pix pixc;
                if (d == 8) {
                    pixc = pixt.BlockconvGrayTile(null, wc, hc);
                } else { /* d == 32 */
                    Pix pixr = pixt.GetRGBComponent(L.COLOR_RED);
                    Pix pixrc = pixr.BlockconvGrayTile(null, wc, hc);
                    Pix pixg = pixt.GetRGBComponent(L.COLOR_GREEN);
                    Pix pixgc = pixg.BlockconvGrayTile(null, wc, hc);
                    Pix pixb = pixt.GetRGBComponent(L.COLOR_BLUE);
                    Pix pixbc = pixb.BlockconvGrayTile(null, wc, hc);
                    pixc = CreateRGBImage(pixrc, pixgc, pixbc);
                }

                pt.PaintTile(pixd, i, j, pixc);
            }
        }

        return pixd;
    }

    // leptonica: pixBlockconvGrayTile (convolve.c)
    /// <summary>Grayscale block convolution on a tile that carries its own border.</summary>
    /// <param name="pixacc">32 bpp accum pix</param>
    /// <param name="wc">half width of the convolution kernel</param>
    /// <param name="hc">half height of the convolution kernel</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// This pix is 8 bpp gray.
    /// (1) The full width and height of the convolution kernel
    ///     are (2 * wc + 1) and (2 * hc + 1)
    /// (2) Assumes that the input pixs is padded with (wc + 1) pixels on
    ///     left and right, and with (hc + 1) pixels on top and bottom.
    ///     The returned pix has these stripped off; they are only used
    ///     for computation.
    /// (3) Returns a copy if either wc or hc are 0.
    /// (4) Require that w > 2 * wc + 3 and h > 2 * hc + 3,
    ///     where (w,h) are the dimensions of pixs.  Attempt to
    ///     reduce the kernel size if necessary.
    /// </remarks>
    public Pix BlockconvGrayTile(Pix? pixacc, int wc, int hc)
    {
        const string func = "pixBlockconvGrayTile";
        GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (wc <= 0 || hc <= 0)  /* no-op */
            return Copy(null, this);
        if (w < 2 * wc + 3 || h < 2 * hc + 3) {
            Log.Warning(func, $"kernel too large: wc = {wc}, hc = {hc}, w = {w}, h = {h}; reducing!");
            wc = Math.Min(wc, (w - 1) / 2);
            hc = Math.Min(hc, (h - 1) / 2);
        }
        if (wc == 0 || hc == 0)
            return Copy(null, this);
        int wd = w - 2 * wc;
        int hd = h - 2 * hc;

        Pix pixt;
        if (pixacc != null) {
            if (pixacc.Depth == 32) {
                pixt = Clone(pixacc);
            } else {
                Log.Warning(func, "pixacc not 32 bpp; making new one");
                pixt = BlockconvAccum();
            }
        } else {
            pixt = BlockconvAccum();
        }

        Pix pixd = CreateTemplate(this);
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        float norm = 1.0f / (float)((2 * wc + 1) * (2 * hc + 1));

            /* Do the convolution over the subregion of size (wd - 2, hd - 2),
             * which exactly corresponds to the size of the subregion that
             * will be extracted by pixTilingPaintTile().  Note that the
             * region in which points are computed is not symmetric about
             * the center of the images; instead the computation in
             * the accumulator image is shifted up and to the left by 1,
             * relative to the center, because the 4 accumulator sampling
             * points are taken at the LL corner of the filter and at 3 other
             * points that are shifted -wc and -hc to the left and above.  */
        for (int i = hc; i < hc + hd - 2; i++) {
            int imin = Math.Max(i - hc - 1, 0);
            int imax = Math.Min(i + hc, h - 1);
            Span<uint> lined = datad.AsSpan(i * wpld);
            int linemint = imin * wplt;
            int linemaxt = imax * wplt;
            for (int j = wc; j < wc + wd - 2; j++) {
                int jmin = Math.Max(j - wc - 1, 0);
                int jmax = Math.Min(j + wc, w - 1);
                uint val = datat[linemaxt + jmax] - datat[linemaxt + jmin]
                           + datat[linemint + jmin] - datat[linemint + jmax];
                val = (byte)(norm * val + 0.5);
                SetDataByte(lined, j, (int)val);
            }
        }

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *     Convolution for mean, mean square, variance and rms deviation    *
     *----------------------------------------------------------------------*/
    // leptonica: pixWindowedStats (convolve.c)
    /// <summary>Mean, mean square, variance and rms deviation in a window.</summary>
    /// <param name="wc">half width of the convolution kernel</param>
    /// <param name="hc">half height of the convolution kernel</param>
    /// <param name="hasborder">use 1 if it already has (wc + 1 border pixels
    /// on left and right, and hc + 1 on top and bottom;
    /// use 0 to add kernel-dependent border</param>
    /// <param name="pixm">8 bpp mean value in window</param>
    /// <param name="pixms">32 bpp mean square value in window</param>
    /// <param name="fpixv">float variance in window</param>
    /// <param name="fpixrv">float rms deviation from the mean</param>
    /// <remarks>
    /// This pix is 8 bpp grayscale.
    /// (1) This is a high-level convenience function for calculating
    ///     any or all of these derived images.
    /// (2) If hasborder = 0, a border is added and the result is
    ///     computed over all pixels in pixs.  Otherwise, no border is
    ///     added and the border pixels are removed from the output images.
    /// (3) These statistical measures over the pixels in the
    ///     rectangular window are:
    ///       ~ average value: &lt;p>  (pixm)
    ///       ~ average squared value: &lt;p*p> (pixms)
    ///       ~ variance: &lt;(p - &lt;p>)*(p - &lt;p>)> = &lt;p*p> - &lt;p>*&lt;p>  (pixv)
    ///       ~ square-root of variance: (pixrv)
    ///     where the brackets &lt; .. > indicate that the average value is
    ///     to be taken over the window.
    /// (4) Note that the variance is just the mean square difference from
    ///     the mean value; and the square root of the variance is the
    ///     root mean square difference from the mean, sometimes also
    ///     called the 'standard deviation'.
    /// (5) The added border, along with the use of an accumulator array,
    ///     allows computation without special treatment of pixels near
    ///     the image boundary, and runs in a time that is independent
    ///     of the size of the convolution kernel.
    /// </remarks>
    public void WindowedStats(int wc, int hc, int hasborder,
                              out Pix pixm, out Pix pixms, out FPix fpixv, out FPix fpixrv)
    {
        WindowedStatsCore(wc, hc, hasborder, true, true, true, true,
                          out Pix? pm, out Pix? pms, out FPix? fv, out FPix? frv);
        pixm = pm!;
        pixms = pms!;
        fpixv = fv!;
        fpixrv = frv!;
    }

    private void WindowedStatsCore(int wc, int hc, int hasborder,
                                   bool wantm, bool wantms, bool wantv, bool wantrv,
                                   out Pix? ppixm, out Pix? ppixms, out FPix? pfpixv, out FPix? pfpixrv)
    {
        const string func = "pixWindowedStats";
        ppixm = null;
        ppixms = null;
        pfpixv = null;
        pfpixrv = null;
        if (!wantm && !wantms && !wantv && !wantrv)
            throw Lept.Arg(func, "no output requested");
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not defined or not 8 bpp");
        if (wc < 2 || hc < 2)
            throw Lept.Arg(func, "wc and hc not >= 2");

            /* Add border if requested */
        Pix pixb;
        if (hasborder == 0)
            pixb = AddBorderGeneral(wc + 1, wc + 1, hc + 1, hc + 1, 0);
        else
            pixb = Clone(this);

        if (!wantv && !wantrv) {
            if (wantm) ppixm = pixb.WindowedMean(wc, hc, 1, 1);
            if (wantms) ppixms = pixb.WindowedMeanSquare(wc, hc, 1);
            return;
        }

        Pix pixm = pixb.WindowedMean(wc, hc, 1, 1);
        Pix pixms = pixb.WindowedMeanSquare(wc, hc, 1);
        pixm.WindowedVarianceCore(pixms, wantv, wantrv, out pfpixv, out pfpixrv);
        if (wantm)
            ppixm = pixm;
        if (wantms)
            ppixms = pixms;
    }

    // leptonica: pixWindowedMean (convolve.c)
    /// <summary>Average over the kernel window.</summary>
    /// <param name="wc">half width of the convolution kernel</param>
    /// <param name="hc">half height of the convolution kernel</param>
    /// <param name="hasborder">use 1 if it already has (wc + 1 border pixels
    /// on left and right, and hc + 1 on top and bottom;
    /// use 0 to add kernel-dependent border</param>
    /// <param name="normflag">1 for normalization to get average in window;
    /// 0 for the sum in the window (un-normalized)</param>
    /// <returns>pixd 8 or 32 bpp, average over kernel window</returns>
    /// <remarks>
    /// This pix is 8 or 32 bpp grayscale.
    /// (1) The input and output depths are the same.
    /// (2) A set of border pixels of width (wc + 1) on left and right,
    ///     and of height (hc + 1) on top and bottom, must be on the
    ///     pix before the accumulator is found.  The output pixd
    ///     (after convolution) has this border removed.
    ///     If hasborder = 0, the required border is added.
    /// (3) Typically, normflag == 1.  However, if you want the sum
    ///     within the window, rather than a normalized convolution,
    ///     use normflag == 0.
    /// (4) This builds a block accumulator pix, uses it here, and
    ///     destroys it.
    /// (5) The added border, along with the use of an accumulator array,
    ///     allows computation without special treatment of pixels near
    ///     the image boundary, and runs in a time that is independent
    ///     of the size of the convolution kernel.
    /// </remarks>
    public Pix WindowedMean(int wc, int hc, int hasborder, int normflag)
    {
        const string func = "pixWindowedMean";
        int d = Depth;
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 8 or 32 bpp");
        if (wc < 2 || hc < 2)
            throw Lept.Arg(func, "wc and hc not >= 2");

            /* Add border if requested */
        Pix pixb;
        if (hasborder == 0)
            pixb = AddBorderGeneral(wc + 1, wc + 1, hc + 1, hc + 1, 0);
        else
            pixb = Clone(this);

            /* Make the accumulator pix from pixb */
        Pix pixc = pixb.BlockconvAccum();
        int wplc = pixc.Wpl;
        uint[] datac = pixc.Data;

            /* The output has wc + 1 border pixels stripped from each side
             * of pixb, and hc + 1 border pixels stripped from top and bottom. */
        pixb.GetDimensions(out int w, out int h, out _);
        int wd = w - 2 * (wc + 1);
        int hd = h - 2 * (hc + 1);
        if (wd < 2 || hd < 2)
            throw Lept.Fail(func, "w or h is too small for the kernel");
        Pix pixd = Create(wd, hd, d);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;

        int wincr = 2 * wc + 1;
        int hincr = 2 * hc + 1;
        float norm = 1.0f;  /* use this for sum-in-window */
        if (normflag != 0)
            norm = (float)(1.0 / ((float)(wincr) * hincr));
        for (int i = 0; i < hd; i++) {
            int linec1 = i * wplc;
            int linec2 = (i + hincr) * wplc;
            Span<uint> lined = datad.AsSpan(i * wpld);
            for (int j = 0; j < wd; j++) {
                uint val = datac[linec2 + j + wincr] - datac[linec2 + j]
                           - datac[linec1 + j + wincr] + datac[linec1 + j];
                if (d == 8) {
                    val = (byte)(int)(norm * val);
                    SetDataByte(lined, j, (int)val);
                } else {  /* d == 32 */
                    val = (uint)(norm * val);
                    lined[j] = val;
                }
            }
        }

        return pixd;
    }

    // leptonica: pixWindowedMeanSquare (convolve.c)
    /// <summary>Average of the squares over the kernel window.</summary>
    /// <param name="wc">half width of the convolution kernel</param>
    /// <param name="hc">half height of the convolution kernel</param>
    /// <param name="hasborder">use 1 if it already has (wc + 1 border pixels
    /// on left and right, and hc + 1 on top and bottom;
    /// use 0 to add kernel-dependent border</param>
    /// <returns>pixd 32 bpp, average over rectangular window of
    /// width = 2 * wc + 1 and height = 2 * hc + 1</returns>
    /// <remarks>
    /// This pix is 8 bpp grayscale.
    /// (1) A set of border pixels of width (wc + 1) on left and right,
    ///     and of height (hc + 1) on top and bottom, must be on the
    ///     pix before the accumulator is found.  The output pixd
    ///     (after convolution) has this border removed.
    ///     If hasborder = 0, the required border is added.
    /// (2) The advantage is that we are unaffected by the boundary, and
    ///     it is not necessary to treat pixels within wc and hc of the
    ///     border differently.  This is because processing for pixd
    ///     only takes place for pixels in pixs for which the
    ///     kernel is entirely contained in pixs.
    /// (3) Why do we have an added border of width (wc + 1) and
    ///     height (hc + 1), when we only need wc and hc pixels
    ///     to satisfy this condition?  Answer: the accumulators
    ///     are asymmetric, requiring an extra row and column of
    ///     pixels at top and left to work accurately.
    /// (4) The added border, along with the use of an accumulator array,
    ///     allows computation without special treatment of pixels near
    ///     the image boundary, and runs in a time that is independent
    ///     of the size of the convolution kernel.
    /// </remarks>
    public Pix WindowedMeanSquare(int wc, int hc, int hasborder)
    {
        const string func = "pixWindowedMeanSquare";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");
        if (wc < 2 || hc < 2)
            throw Lept.Arg(func, "wc and hc not >= 2");

            /* Add border if requested */
        Pix pixb;
        if (hasborder == 0)
            pixb = AddBorderGeneral(wc + 1, wc + 1, hc + 1, hc + 1, 0);
        else
            pixb = Clone(this);

        DPix dpix = pixb.MeanSquareAccum();
        int wpl = dpix.Wpl;
        double[] data = dpix.Data;

            /* The output has wc + 1 border pixels stripped from each side
             * of pixb, and hc + 1 border pixels stripped from top and bottom. */
        pixb.GetDimensions(out int w, out int h, out _);
        int wd = w - 2 * (wc + 1);
        int hd = h - 2 * (hc + 1);
        if (wd < 2 || hd < 2)
            throw Lept.Fail(func, "w or h too small for kernel");
        Pix pixd = Create(wd, hd, 32);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;

        int wincr = 2 * wc + 1;
        int hincr = 2 * hc + 1;
        double norm = 1.0 / ((float)(wincr) * hincr);
        for (int i = 0; i < hd; i++) {
            int line1 = i * wpl;
            int line2 = (i + hincr) * wpl;
            int lined = i * wpld;
            for (int j = 0; j < wd; j++) {
                double val = data[line2 + j + wincr] - data[line2 + j]
                             - data[line1 + j + wincr] + data[line1 + j];
                uint ival = (uint)(norm * val + 0.5);  /* to round up */
                datad[lined + j] = ival;
            }
        }

        return pixd;
    }

    // leptonica: pixWindowedVariance (convolve.c)
    /// <summary>Variance and rms deviation from the precomputed mean and mean square.</summary>
    /// <param name="pixms">mean square over window; 32 bpp</param>
    /// <param name="fpixv">float variance -- the ms deviation from the mean</param>
    /// <param name="fpixrv">float rms deviation from the mean</param>
    /// <remarks>
    /// This pix (pixm) is the mean over window; 8 or 32 bpp grayscale.
    /// (1) The mean and mean square values are precomputed, using
    ///     pixWindowedMean() and pixWindowedMeanSquare().
    /// (2) Either or both of the variance and square-root of variance
    ///     are returned as an fpix, where the variance is the
    ///     average over the window of the mean square difference of
    ///     the pixel value from the mean:
    ///           &lt;(p - &lt;p>)*(p - &lt;p>)> = &lt;p*p> - &lt;p>*&lt;p>
    /// (3) To visualize the results:
    ///       ~ for both, use fpixDisplayMaxDynamicRange().
    ///       ~ for rms deviation, simply convert the output fpix to pix,
    /// </remarks>
    public void WindowedVariance(Pix pixms, out FPix fpixv, out FPix fpixrv)
    {
        WindowedVarianceCore(pixms, true, true, out FPix? fv, out FPix? frv);
        fpixv = fv!;
        fpixrv = frv!;
    }

    private void WindowedVarianceCore(Pix pixms, bool wantv, bool wantrv,
                                      out FPix? pfpixv, out FPix? pfpixrv)
    {
        const string func = "pixWindowedVariance";
        pfpixv = null;
        pfpixrv = null;
        if (!wantv && !wantrv)
            throw Lept.Arg(func, "no output requested");
        if (Depth != 8)
            throw Lept.Arg(func, "pixm undefined or not 8 bpp");
        if (pixms.Depth != 32)
            throw Lept.Arg(func, "pixms undefined or not 32 bpp");
        GetDimensions(out int w, out int h, out _);
        pixms.GetDimensions(out int ws, out int hs, out int ds);
        if (w != ws || h != hs)
            throw Lept.Arg(func, "pixm and pixms sizes differ");

        int wplv = 0, wplrv = 0;
        float[] datav = Array.Empty<float>(), datarv = Array.Empty<float>();
        if (wantv) {
            FPix fpixv = FPix.Create(w, h);
            pfpixv = fpixv;
            wplv = fpixv.Wpl;
            datav = fpixv.Data;
        }
        if (wantrv) {
            FPix fpixrv = FPix.Create(w, h);
            pfpixrv = fpixrv;
            wplrv = fpixrv.Wpl;
            datarv = fpixrv.Data;
        }

        int wplm = Wpl;
        int wplms = pixms.Wpl;
        uint[] datam = Data;
        uint[] datams = pixms.Data;
        int linev = 0, linerv = 0;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm);
            ReadOnlySpan<uint> linems = datams.AsSpan(i * wplms);
            if (wantv)
                linev = i * wplv;
            if (wantrv)
                linerv = i * wplrv;
            for (int j = 0; j < w; j++) {
                int valm = GetDataByte(linem, j);
                int valms;
                if (ds == 8)
                    valms = GetDataByte(linems, j);
                else  /* ds == 32 */
                    valms = (int)linems[j];
                float var = (float)valms - (float)valm * valm;
                if (wantv)
                    datav[linev + j] = var;
                if (wantrv)
                    datarv[linerv + j] = (float)Math.Sqrt(var);
            }
        }
    }

    // leptonica: pixMeanSquareAccum (convolve.c)
    /// <summary>Accumulator of the squares of the pixel values.</summary>
    /// <returns>dpix 64 bit array</returns>
    /// <remarks>
    /// This pix is 8 bpp grayscale.
    /// (1) Similar to pixBlockconvAccum(), this computes the
    ///     sum of the squares of the pixel values in such a way
    ///     that the value at (i,j) is the sum of all squares in
    ///     the rectangle from the origin to (i,j).
    /// (2) The general recursion relation (v are squared pixel values) is
    ///       a(i,j) = v(i,j) + a(i-1, j) + a(i, j-1) - a(i-1, j-1)
    ///     For the first line, this reduces to the special case
    ///       a(i,j) = v(i,j) + a(i, j-1)
    ///     For the first column, the special case is
    ///       a(i,j) = v(i,j) + a(i-1, j)
    /// </remarks>
    public DPix MeanSquareAccum()
    {
        const string func = "pixMeanSquareAccum";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");
        GetDimensions(out int w, out int h, out _);
        DPix dpix = DPix.Create(w, h);

        uint[] datas = Data;
        int wpls = Wpl;
        double[] data = dpix.Data;
        int wpl = dpix.Wpl;

        ReadOnlySpan<uint> lines = datas;
        int line = 0;
        int val;
        for (int j = 0; j < w; j++) {   /* first line */
            val = GetDataByte(lines, j);
            if (j == 0)
                data[line] = (double)(val) * val;
            else
                data[line + j] = data[line + j - 1] + (double)(val) * val;
        }

            /* Do the other lines */
        for (int i = 1; i < h; i++) {
            lines = datas.AsSpan(i * wpls);
            line = i * wpl;  /* current dest line */
            int linep = line - wpl;  /* prev dest line */
            for (int j = 0; j < w; j++) {
                val = GetDataByte(lines, j);
                if (j == 0)
                    data[line] = data[linep] + (double)(val) * val;
                else
                    data[line + j] = data[line + j - 1] + data[linep + j] - data[linep + j - 1]
                                     + (double)(val) * val;
            }
        }

        return dpix;
    }

    /*----------------------------------------------------------------------*
     *                        Binary block sum/rank                         *
     *----------------------------------------------------------------------*/
    // leptonica: pixBlockrank (convolve.c)
    /// <summary>Binary block rank filter.</summary>
    /// <param name="pixacc">pix [optional] 32 bpp</param>
    /// <param name="wc">half width of block sum/rank kernel</param>
    /// <param name="hc">half height of block sum/rank kernel</param>
    /// <param name="rank">between 0.0 and 1.0; 0.5 is median filter</param>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// This pix is 1 bpp.
    /// (1) The full width and height of the convolution kernel
    ///     are (2 * wc + 1) and (2 * hc + 1)
    /// (2) This returns a pixd where each pixel is a 1 if the
    ///     neighborhood (2 * wc + 1) x (2 * hc + 1)) pixels
    ///     contains the rank fraction of 1 pixels.  Otherwise,
    ///     the returned pixel is 0.  Note that the special case
    ///     of rank = 0.0 is always satisfied, so the returned
    ///     pixd has all pixels with value 1.
    /// (3) If accum pix is null, make one, use it, and destroy it
    ///     before returning; otherwise, just use the input accum pix
    /// (4) If both wc and hc are 0, returns a copy unless rank == 0.0,
    ///     in which case this returns an all-ones image.
    /// (5) Require that w >= 2 * wc + 1 and h >= 2 * hc + 1,
    ///     where (w,h) are the dimensions of pixs.  Attempt to
    ///     reduce the kernel size if necessary.
    /// </remarks>
    public Pix Blockrank(Pix? pixacc, int wc, int hc, float rank)
    {
        const string func = "pixBlockrank";
        GetDimensions(out int w, out int h, out int d);
        if (d != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (rank < 0.0 || rank > 1.0)
            throw Lept.Arg(func, "rank must be in [0.0, 1.0]");

        if (rank == 0.0) {
            Pix pixd0 = CreateTemplate(this);
            pixd0.SetAll();
            return pixd0;
        }

        if (wc <= 0 || hc <= 0)
            return Copy(null, this);
        if (w < 2 * wc + 1 || h < 2 * hc + 1) {
            Log.Warning(func, $"kernel too large: wc = {wc}, hc = {hc}, w = {w}, h = {h}; reducing!");
            wc = Math.Min(wc, (w - 1) / 2);
            hc = Math.Min(hc, (h - 1) / 2);
        }
        if (wc == 0 || hc == 0)
            return Copy(null, this);

        Pix pixt = Blocksum(pixacc, wc, hc);

            /* 1 bpp block rank filter output.
             * Must invert because threshold gives 1 for values < thresh,
             * but we need a 1 if the value is >= thresh. */
        int thresh = (int)(255.0 * rank);
        Pix pixd = pixt.ThresholdToBinary(thresh);
        Invert(pixd, pixd);
        return pixd;
    }

    // leptonica: pixBlocksum (convolve.c)
    /// <summary>Binary block sum.</summary>
    /// <param name="pixacc">pix [optional] 32 bpp</param>
    /// <param name="wc">half width of block sum/rank kernel</param>
    /// <param name="hc">half height of block sum/rank kernel</param>
    /// <returns>pixd 8 bpp</returns>
    /// <remarks>
    /// This pix is 1 bpp.
    /// (1) If accum pix is null, make one and destroy it before
    ///     returning; otherwise, just use the input accum pix
    /// (2) The full width and height of the convolution kernel
    ///     are (2 * wc + 1) and (2 * hc + 1)
    /// (3) Use of wc = hc = 1, followed by pixInvert() on the
    ///     8 bpp result, gives a nice anti-aliased, and somewhat
    ///     darkened, result on text.
    /// (4) Require that w >= 2 * wc + 1 and h >= 2 * hc + 1,
    ///     where (w,h) are the dimensions of pixs.  Attempt to
    ///     reduce the kernel size if necessary.
    /// (5) Returns in each dest pixel the sum of all src pixels
    ///     that are within a block of size of the kernel, centered
    ///     on the dest pixel.  This sum is the number of src ON
    ///     pixels in the block at each location, normalized to 255
    ///     for a block containing all ON pixels.  For pixels near
    ///     the boundary, where the block is not entirely contained
    ///     within the image, we then multiply by a second normalization
    ///     factor that is greater than one, so that all results
    ///     are normalized by the number of participating pixels
    ///     within the block.
    /// </remarks>
    public Pix Blocksum(Pix? pixacc, int wc, int hc)
    {
        const string func = "pixBlocksum";
        GetDimensions(out int w, out int h, out int d);
        if (d != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (wc <= 0 || hc <= 0)
            return Copy(null, this);
        if (w < 2 * wc + 1 || h < 2 * hc + 1) {
            Log.Warning(func, $"kernel too large: wc = {wc}, hc = {hc}, w = {w}, h = {h}; reducing!");
            wc = Math.Min(wc, (w - 1) / 2);
            hc = Math.Min(hc, (h - 1) / 2);
        }
        if (wc == 0 || hc == 0)
            return Copy(null, this);

        Pix pixt;
        if (pixacc != null) {
            if (pixacc.Depth != 32)
                throw Lept.Arg(func, "pixacc not 32 bpp");
            pixt = Clone(pixacc);
        } else {
            pixt = BlockconvAccum();
        }

            /* 8 bpp block sum output */
        Pix pixd = Create(w, h, 8);
        pixd.CopyResolution(this);

        int wpld = pixd.Wpl;
        int wplt = pixt.Wpl;
        BlocksumLow(pixd.Data, w, h, wpld, pixt.Data, wplt, wc, hc);

        return pixd;
    }

    // leptonica: blocksumLow (convolve.c)
    /// <summary>Computes the normalized block sums of an image from its accumulator image.</summary>
    /// <param name="datad">The data of the 8 bpp destination image.</param>
    /// <param name="w">The width of the image in pixels.</param>
    /// <param name="h">The height of the image in pixels.</param>
    /// <param name="wpl">The number of 32-bit words per line of the destination.</param>
    /// <param name="dataa">The data of the 32 bpp accumulator image.</param>
    /// <param name="wpla">The number of 32-bit words per line of the accumulator.</param>
    /// <param name="wc">The convolution half-width.</param>
    /// <param name="hc">The convolution half-height.</param>
    /// <remarks>
    /// (1) The full width and height of the convolution kernel
    ///     are (2 * wc + 1) and (2 * hc + 1).
    /// (2) The lack of symmetry between the handling of the
    ///     first (hc + 1) lines and the last (hc) lines,
    ///     and similarly with the columns, is due to fact that
    ///     for the pixel at (x,y), the accumulator values are
    ///     taken at (x + wc, y + hc), (x - wc - 1, y + hc),
    ///     (x + wc, y - hc - 1) and (x - wc - 1, y - hc - 1).
    /// (3) Compute sums of ON pixels within the block filter size,
    ///     normalized between 0 and 255, as if there were no reduced
    ///     area at the boundary.  This under-estimates the value
    ///     of the boundary pixels, so we multiply them by another
    ///     normalization factor that is greater than 1.
    /// (4) This second normalization is done first for the first
    ///     hc + 1 lines; then for the last hc lines; and finally
    ///     for the first wc + 1 and last wc columns in the intermediate
    ///     lines.
    /// (5) Required constraints are: wc &lt; w and hc &lt; h.
    /// </remarks>
    private static void BlocksumLow(uint[] datad, int w, int h, int wpl,
                                    uint[] dataa, int wpla, int wc, int hc)
    {
        int wmwc = w - wc;
        int hmhc = h - hc;
        if (wmwc <= 0 || hmhc <= 0) {
            Log.Error("blocksumLow", "wc >= w || hc >=h");
            return;
        }
        int fwc = 2 * wc + 1;
        int fhc = 2 * hc + 1;
        float norm = (float)(255.0 / ((float)(fwc) * fhc));
        uint val;
        int wn, hn;
        float normh, normw;

            /*------------------------------------------------------------*
             *  Compute, using b.c. only to set limits on the accum image *
             *------------------------------------------------------------*/
            /* NOTE(upstream): same clamping of imin and jmin as in blockconvLow */
        for (int i = 0; i < h; i++) {
            int imin = Math.Max(i - 1 - hc, 0);
            int imax = Math.Min(i + hc, h - 1);
            Span<uint> lined = datad.AsSpan(wpl * i);
            int linemina = wpla * imin;
            int linemaxa = wpla * imax;
            for (int j = 0; j < w; j++) {
                int jmin = Math.Max(j - 1 - wc, 0);
                int jmax = Math.Min(j + wc, w - 1);
                val = dataa[linemaxa + jmax] - dataa[linemaxa + jmin]
                      - dataa[linemina + jmax] + dataa[linemina + jmin];
                val = (byte)(norm * val);
                SetDataByte(lined, j, (int)val);
            }
        }

            /*------------------------------------------------------------*
             *             Fix normalization for boundary pixels          *
             *------------------------------------------------------------*/
        for (int i = 0; i <= hc; i++) {    /* first hc + 1 lines */
            hn = hc + i;
            normh = (float)fhc / (float)hn;   /* > 1 */
            Span<uint> lined = datad.AsSpan(wpl * i);
            for (int j = 0; j <= wc; j++) {
                wn = wc + j;
                normw = (float)fwc / (float)wn;   /* > 1 */
                val = (uint)GetDataByte(lined, j);
                val = (byte)(val * normh * normw);
                SetDataByte(lined, j, (int)val);
            }
            for (int j = wc + 1; j < wmwc; j++) {
                val = (uint)GetDataByte(lined, j);
                val = (byte)(val * normh);
                SetDataByte(lined, j, (int)val);
            }
            for (int j = wmwc; j < w; j++) {
                wn = wc + w - j;
                normw = (float)fwc / (float)wn;   /* > 1 */
                val = (uint)GetDataByte(lined, j);
                val = (byte)(val * normh * normw);
                SetDataByte(lined, j, (int)val);
            }
        }

        for (int i = hmhc; i < h; i++) {  /* last hc lines */
            hn = hc + h - i;
            normh = (float)fhc / (float)hn;   /* > 1 */
            Span<uint> lined = datad.AsSpan(wpl * i);
            for (int j = 0; j <= wc; j++) {
                wn = wc + j;
                normw = (float)fwc / (float)wn;   /* > 1 */
                val = (uint)GetDataByte(lined, j);
                val = (byte)(val * normh * normw);
                SetDataByte(lined, j, (int)val);
            }
            for (int j = wc + 1; j < wmwc; j++) {
                val = (uint)GetDataByte(lined, j);
                val = (byte)(val * normh);
                SetDataByte(lined, j, (int)val);
            }
            for (int j = wmwc; j < w; j++) {
                wn = wc + w - j;
                normw = (float)fwc / (float)wn;   /* > 1 */
                val = (uint)GetDataByte(lined, j);
                val = (byte)(val * normh * normw);
                SetDataByte(lined, j, (int)val);
            }
        }

        for (int i = hc + 1; i < hmhc; i++) {    /* intermediate lines */
            Span<uint> lined = datad.AsSpan(wpl * i);
            for (int j = 0; j <= wc; j++) {   /* first wc + 1 columns */
                wn = wc + j;
                normw = (float)fwc / (float)wn;   /* > 1 */
                val = (uint)GetDataByte(lined, j);
                val = (byte)(val * normw);
                SetDataByte(lined, j, (int)val);
            }
            for (int j = wmwc; j < w; j++) {   /* last wc columns */
                wn = wc + w - j;
                normw = (float)fwc / (float)wn;   /* > 1 */
                val = (uint)GetDataByte(lined, j);
                val = (byte)(val * normw);
                SetDataByte(lined, j, (int)val);
            }
        }
    }

    /*----------------------------------------------------------------------*
     *                          Census transform                            *
     *----------------------------------------------------------------------*/
    // leptonica: pixCensusTransform (convolve.c)
    /// <summary>Census transform.</summary>
    /// <param name="halfsize">of square over which neighbors are averaged</param>
    /// <param name="pixacc">[optional] 32 bpp pix</param>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// This pix is 8 bpp.
    /// (1) The Census transform was invented by Ramin Zabih and John Woodfill
    ///     ("Non-parametric local transforms for computing visual
    ///     correspondence", Third European Conference on Computer Vision,
    ///     Stockholm, Sweden, May 1994); see publications at
    ///        http://www.cs.cornell.edu/~rdz/index.htm
    ///     This compares each pixel against the average of its neighbors,
    ///     in a square of odd dimension centered on the pixel.
    ///     If the pixel is greater than the average of its neighbors,
    ///     the output pixel value is 1; otherwise it is 0.
    /// (2) This can be used as an encoding for an image that is
    ///     fairly robust against slow illumination changes, with
    ///     applications in image comparison and mosaicing.
    /// (3) The size of the convolution kernel is (2 * halfsize + 1)
    ///     on a side.  The halfsize parameter must be >= 1.
    /// (4) If accum pix is null, make one, use it, and destroy it
    ///     before returning; otherwise, just use the input accum pix
    /// </remarks>
    public Pix CensusTransform(int halfsize, Pix? pixacc)
    {
        const string func = "pixCensusTransform";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (halfsize < 1)
            throw Lept.Arg(func, "halfsize must be >= 1");

            /* Get the average of each pixel with its neighbors */
        Pix pixav = BlockconvGray(pixacc, halfsize, halfsize);

            /* Subtract the pixel from the average, and then compare
             * the pixel value with the remaining average */
        GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 1);
        uint[] datas = Data;
        uint[] datav = pixav.Data;
        uint[] datad = pixd.Data;
        int wpls = Wpl;
        int wplv = pixav.Wpl;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls);
            ReadOnlySpan<uint> linev = datav.AsSpan(i * wplv);
            Span<uint> lined = datad.AsSpan(i * wpld);
            for (int j = 0; j < w; j++) {
                int vals = GetDataByte(lines, j);
                int valv = GetDataByte(linev, j);
                if (vals > valv)
                    SetDataBit(lined, j);
            }
        }

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *                         Generic convolution                          *
     *----------------------------------------------------------------------*/
    // leptonica: pixConvolve (convolve.c)
    /// <summary>Generic convolution with an arbitrary kernel.</summary>
    /// <param name="kel">kernel</param>
    /// <param name="outdepth">of pixd: 8, 16 or 32</param>
    /// <param name="normflag">1 to normalize kernel to unit sum; 0 otherwise</param>
    /// <returns>pixd 8, 16 or 32 bpp</returns>
    /// <remarks>
    /// This pix is 8, 16, 32 bpp; no colormap.
    /// (1) This gives a convolution with an arbitrary kernel.
    /// (2) The input pixs must have only one sample/pixel.
    ///     To do a convolution on an RGB image, use pixConvolveRGB().
    /// (3) The parameter outdepth determines the depth of the result.
    ///     If the kernel is normalized to unit sum, the output values
    ///     can never exceed 255, so an output depth of 8 bpp is sufficient.
    ///     If the kernel is not normalized, it may be necessary to use
    ///     16 or 32 bpp output to avoid overflow.
    /// (4) If normflag == 1, the result is normalized by scaling all
    ///     kernel values for a unit sum.  If the sum of kernel values
    ///     is very close to zero, the kernel can not be normalized and
    ///     the convolution will not be performed.  A warning is issued.
    /// (5) The kernel values can be positive or negative, but the
    ///     result for the convolution can only be stored as a positive
    ///     number.  Consequently, if it goes negative, the choices are
    ///     to clip to 0 or take the absolute value.  We're choosing
    ///     to take the absolute value.  (Another possibility would be
    ///     to output a second unsigned image for the negative values.)
    ///     If you want to get a clipped result, or to keep the negative
    ///     values in the result, use fpixConvolve(), with the
    ///     converters in fpix2.c between pix and fpix.
    /// (6) This uses a mirrored border to avoid special casing on
    ///     the boundaries.
    /// (7) To get a subsampled output, call l_setConvolveSampling().
    ///     The time to make a subsampled output is reduced by the
    ///     product of the sampling factors.
    /// (8) The function is slow, running at about 12 machine cycles for
    ///     each pixel-op in the convolution.  For example, with a 3 GHz
    ///     cpu, a 1 Mpixel grayscale image, and a kernel with
    ///     (sx * sy) = 25 elements, the convolution takes about 100 msec.
    /// </remarks>
    public Pix Convolve(L_Kernel kel, int outdepth, int normflag)
    {
        const string func = "pixConvolve";
        if (Colormap != null)
            throw Lept.Arg(func, "pixs has colormap");
        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 16 && d != 32)
            throw Lept.Arg(func, "pixs not 8, 16, or 32 bpp");

        if (LeptGpu.TryGetActive((long)w * h, kel.Sx * kel.Sy * 2, out IGpuAccelerator? gpu) &&
            gpu.TryConvolve(this, kel, outdepth, normflag, ConvolveSamplingFactX, ConvolveSamplingFactY, out Pix? gpuPix)) {
            return gpuPix;
        }

        L_Kernel keli = kel.Invert();
        keli.GetParameters(out int sy, out int sx, out int cy, out int cx);
        L_Kernel keln;
        if (normflag != 0)
            keln = keli.Normalize(1.0f);
        else
            keln = keli.Copy();

        Pix pixt = AddMirroredBorder(cx, sx - cx, cy, sy - cy);

        int sfx = ConvolveSamplingFactX;
        int sfy = ConvolveSamplingFactY;
        int wd = (w + sfx - 1) / sfx;
        int hd = (h + sfy - 1) / sfy;
        Pix pixd = CreateNoInit(wd, hd, outdepth);
        uint[] datat = pixt.Data;
        uint[] datad = pixd.Data;
        int wplt = pixt.Wpl;
        int wpld = pixd.Wpl;
        float[] flatKel = keln.GetFlatData();

            /* Fast path (8 bpp -> 8 bpp, no subsampling): 16 outputs at a time with
             * the same per-pixel float accumulation order as the scalar loop.  The
             * source is first put in plain pixel order.  Only used when the result
             * is guaranteed to stay in 0..255 and float sums are exact to +0.5. */
        double sumAbs = 0.0;
        foreach (float kv in flatKel) sumAbs += Math.Abs((double)kv);
        bool fast = ConvolveSimdEnabled && Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian
                    && d == 8 && outdepth == 8 && sfx == 1 && wd >= 16
                    && sumAbs * 255.0 < 255.4;
        byte[]? lin = null;
        if (fast) {
            lin = new byte[datat.Length * 4];
            ReadOnlySpan<byte> sb = MemoryMarshal.AsBytes<uint>(datat);
            var rv = Vector128.Create((byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12);
            for (int n = 0; n < lin.Length; n += 16)
                Vector128.StoreUnsafe(Vector128.Shuffle(
                    Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(sb), (nuint)n), rv),
                    ref lin[0], (nuint)n);
        }

        LeptParallel.ForRows(hd, (long)wd * sy * sx * 3L, (idStart, idEnd) => {
            ref uint baseT = ref MemoryMarshal.GetArrayDataReference(datat);
            ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
            ref float baseK = ref MemoryMarshal.GetArrayDataReference(flatKel);
            for (int id = idStart; id < idEnd; id++) {
                int i = id * sfy;
                ref uint lined = ref Unsafe.Add(ref baseD, (nuint)(id * wpld));
                int jd0 = 0;
                if (fast) {
                    ref byte pl = ref MemoryMarshal.GetArrayDataReference(lin!);
                    ref byte outB = ref Unsafe.As<uint, byte>(ref lined);
                    var rv = Vector128.Create((byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12);
                    var half = Vector128.Create(0.5f);
                    for (; jd0 + 16 <= wd; jd0 += 16) {
                        var s0 = Vector128<float>.Zero; var s1 = s0; var s2 = s0; var s3 = s0;
                        for (int k = 0; k < sy; k++) {
                            ref byte rowp = ref Unsafe.Add(ref pl, (nuint)((i + k) * wplt * 4 + jd0));
                            for (int m = 0; m < sx; m++) {
                                var kk = Vector128.Create(Unsafe.Add(ref baseK, (nuint)(k * sx + m)));
                                var px = Vector128.LoadUnsafe(ref rowp, (nuint)m);
                                var (lo, hi) = Vector128.Widen(px);
                                var (a0, a1) = Vector128.Widen(lo);
                                var (a2, a3) = Vector128.Widen(hi);
                                s0 += Vector128.ConvertToSingle(a0.AsInt32()) * kk;
                                s1 += Vector128.ConvertToSingle(a1.AsInt32()) * kk;
                                s2 += Vector128.ConvertToSingle(a2.AsInt32()) * kk;
                                s3 += Vector128.ConvertToSingle(a3.AsInt32()) * kk;
                            }
                        }
                        var q0 = Vector128.ConvertToInt32(Vector128.Abs(s0) + half).AsUInt32();
                        var q1 = Vector128.ConvertToInt32(Vector128.Abs(s1) + half).AsUInt32();
                        var q2 = Vector128.ConvertToInt32(Vector128.Abs(s2) + half).AsUInt32();
                        var q3 = Vector128.ConvertToInt32(Vector128.Abs(s3) + half).AsUInt32();
                        var bytes = Vector128.Narrow(Vector128.Narrow(q0, q1), Vector128.Narrow(q2, q3));
                        Vector128.StoreUnsafe(Vector128.Shuffle(bytes, rv), ref outB, (nuint)jd0);
                    }
                }
                for (int j = jd0, jd = jd0; jd < wd; j += sfx, jd++) {
                    float sum = 0.0f;
                    if (d == 8) {
                        for (int k = 0; k < sy; k++) {
                            ref uint linet = ref Unsafe.Add(ref baseT, (nuint)((i + k) * wplt));
                            ref float krow = ref Unsafe.Add(ref baseK, (nuint)(k * sx));
                            for (int m = 0; m < sx; m++)
                                sum += GetDataByteUnchecked(ref linet, j + m) * Unsafe.Add(ref krow, (nuint)m);
                        }
                    } else if (d == 16) {
                        for (int k = 0; k < sy; k++) {
                            ref uint linet = ref Unsafe.Add(ref baseT, (nuint)((i + k) * wplt));
                            ref float krow = ref Unsafe.Add(ref baseK, (nuint)(k * sx));
                            for (int m = 0; m < sx; m++)
                                sum += GetDataTwoBytesUnchecked(ref linet, j + m) * Unsafe.Add(ref krow, (nuint)m);
                        }
                    } else {  /* d == 32 */
                        for (int k = 0; k < sy; k++) {
                            ref uint linet = ref Unsafe.Add(ref baseT, (nuint)((i + k) * wplt + j));
                            ref float krow = ref Unsafe.Add(ref baseK, (nuint)(k * sx));
                            for (int m = 0; m < sx; m++)
                                sum += (int)Unsafe.Add(ref linet, (nuint)m) * Unsafe.Add(ref krow, (nuint)m);
                        }
                    }
                    if (sum < 0.0f) sum = -sum;
                    if (outdepth == 8)
                        SetDataByteUnchecked(ref lined, jd, (int)(sum + 0.5));
                    else if (outdepth == 16)
                        SetDataTwoBytesUnchecked(ref lined, jd, (int)(sum + 0.5));
                    else
                        Unsafe.Add(ref lined, (nuint)jd) = (uint)(sum + 0.5);
                }
            }
        });

        return pixd;
    }

    // leptonica: pixConvolveSep (convolve.c)
    /// <summary>Convolution with a separable kernel.</summary>
    /// <param name="kelx">x-dependent kernel</param>
    /// <param name="kely">y-dependent kernel</param>
    /// <param name="outdepth">of pixd: 8, 16 or 32</param>
    /// <param name="normflag">1 to normalize kernel to unit sum; 0 otherwise</param>
    /// <returns>pixd 8, 16 or 32 bpp</returns>
    /// <remarks>
    /// This pix is 8, 16, 32 bpp; no colormap.
    /// (1) This does a convolution with a separable kernel that is
    ///     is a sequence of convolutions in x and y.  The two
    ///     one-dimensional kernel components must be input separately;
    ///     the full kernel is the product of these components.
    ///     The support for the full kernel is thus a rectangular region.
    /// (2) The input pixs must have only one sample/pixel.
    ///     To do a convolution on an RGB image, use pixConvolveSepRGB().
    /// (3) The parameter outdepth determines the depth of the result.
    ///     If the kernel is normalized to unit sum, the output values
    ///     can never exceed 255, so an output depth of 8 bpp is sufficient.
    ///     If the kernel is not normalized, it may be necessary to use
    ///     16 or 32 bpp output to avoid overflow.
    /// (2) The normflag parameter is used as in pixConvolve().
    /// (4) The kernel values can be positive or negative, but the
    ///     result for the convolution can only be stored as a positive
    ///     number.  Consequently, if it goes negative, the choices are
    ///     to clip to 0 or take the absolute value.  We're choosing
    ///     the former for now.  Another possibility would be to output
    ///     a second unsigned image for the negative values.
    /// (5) Warning: if you use l_setConvolveSampling() to get a
    ///     subsampled output, and the sampling factor is larger than
    ///     the kernel half-width, it is faster to use the non-separable
    ///     version pixConvolve().  This is because the first convolution
    ///     here must be done on every raster line, regardless of the
    ///     vertical sampling factor.  If the sampling factor is smaller
    ///     than kernel half-width, it's faster to use the separable
    ///     convolution.
    /// (6) This uses mirrored borders to avoid special casing on
    ///     the boundaries.
    /// </remarks>
    public Pix ConvolveSep(L_Kernel kelx, L_Kernel kely, int outdepth, int normflag)
    {
        const string func = "pixConvolveSep";
        int d = Depth;
        if (d != 8 && d != 16 && d != 32)
            throw Lept.Arg(func, "pixs not 8, 16, or 32 bpp");

        int xfact = ConvolveSamplingFactX;
        int yfact = ConvolveSamplingFactY;
        if (Colormap == null &&
            LeptGpu.TryGetActive((long)Width * Height, (kelx.Sx * kelx.Sy + kely.Sx * kely.Sy) * 2, out IGpuAccelerator? gpu) &&
            gpu.TryConvolveSep(this, kelx, kely, outdepth, normflag, xfact, yfact, out Pix? gpuPix)) {
            return gpuPix;
        }
        try {
            Pix pixt, pixd;
            if (normflag != 0) {
                L_Kernel kelxn = kelx.Normalize(1000.0f);
                L_Kernel kelyn = kely.Normalize(0.001f);
                SetConvolveSampling(xfact, 1);
                pixt = Convolve(kelxn, 32, 0);
                SetConvolveSampling(1, yfact);
                pixd = pixt.Convolve(kelyn, outdepth, 0);
            } else {  /* don't normalize */
                SetConvolveSampling(xfact, 1);
                pixt = Convolve(kelx, 32, 0);
                SetConvolveSampling(1, yfact);
                pixd = pixt.Convolve(kely, outdepth, 0);
            }
            return pixd;
        } finally {
            SetConvolveSampling(xfact, yfact);  /* restore */
        }
    }

    // leptonica: pixConvolveRGB (convolve.c)
    /// <summary>Convolution of an RGB image with an arbitrary kernel.</summary>
    /// <param name="kel">kernel</param>
    /// <returns>pixd 32 bpp rgb</returns>
    /// <remarks>
    /// This pix is 32 bpp rgb.
    /// (1) This gives a convolution on an RGB image using an
    ///     arbitrary kernel (which we normalize to keep each
    ///     component within the range [0 ... 255].
    /// (2) The input pixs must be RGB.
    /// (3) The kernel values can be positive or negative, but the
    ///     result for the convolution can only be stored as a positive
    ///     number.  Consequently, if it goes negative, we clip the
    ///     result to 0.
    /// (4) To get a subsampled output, call l_setConvolveSampling().
    ///     The time to make a subsampled output is reduced by the
    ///     product of the sampling factors.
    /// (5) This uses a mirrored border to avoid special casing on
    ///     the boundaries.
    /// </remarks>
    public Pix ConvolveRGB(L_Kernel kel)
    {
        const string func = "pixConvolveRGB";
        if (Depth != 32)
            throw Lept.Arg(func, "pixs is not 32 bpp");

        if (LeptGpu.TryGetActive((long)Width * Height, kel.Sx * kel.Sy * 6, out IGpuAccelerator? gpu) &&
            gpu.TryConvolveRGB(this, kel, ConvolveSamplingFactX, ConvolveSamplingFactY, out Pix? gpuPix)) {
            return gpuPix;
        }

        Pix pixt = GetRGBComponent(L.COLOR_RED);
        Pix pixr = pixt.Convolve(kel, 8, 1);
        pixt = GetRGBComponent(L.COLOR_GREEN);
        Pix pixg = pixt.Convolve(kel, 8, 1);
        pixt = GetRGBComponent(L.COLOR_BLUE);
        Pix pixb = pixt.Convolve(kel, 8, 1);
        Pix pixd = CreateRGBImage(pixr, pixg, pixb);

        return pixd;
    }

    // leptonica: pixConvolveRGBSep (convolve.c)
    /// <summary>Convolution of an RGB image with a separable kernel.</summary>
    /// <param name="kelx">x-dependent kernel</param>
    /// <param name="kely">y-dependent kernel</param>
    /// <returns>pixd 32 bpp rgb</returns>
    /// <remarks>
    /// This pix is 32 bpp rgb.
    /// (1) This does a convolution on an RGB image using a separable
    ///     kernel that is a sequence of convolutions in x and y.  The two
    ///     one-dimensional kernel components must be input separately;
    ///     the full kernel is the product of these components.
    ///     The support for the full kernel is thus a rectangular region.
    /// (2) The kernel values can be positive or negative, but the
    ///     result for the convolution can only be stored as a positive
    ///     number.  Consequently, if it goes negative, we clip the
    ///     result to 0.
    /// (3) To get a subsampled output, call l_setConvolveSampling().
    ///     The time to make a subsampled output is reduced by the
    ///     product of the sampling factors.
    /// (4) This uses a mirrored border to avoid special casing on
    ///     the boundaries.
    /// </remarks>
    public Pix ConvolveRGBSep(L_Kernel kelx, L_Kernel kely)
    {
        const string func = "pixConvolveRGBSep";
        if (Depth != 32)
            throw Lept.Arg(func, "pixs is not 32 bpp");

        if (LeptGpu.TryGetActive((long)Width * Height, (kelx.Sx * kelx.Sy + kely.Sx * kely.Sy) * 6, out IGpuAccelerator? gpu) &&
            gpu.TryConvolveRGBSep(this, kelx, kely, ConvolveSamplingFactX, ConvolveSamplingFactY, out Pix? gpuPix)) {
            return gpuPix;
        }

        Pix pixt = GetRGBComponent(L.COLOR_RED);
        Pix pixr = pixt.ConvolveSep(kelx, kely, 8, 1);
        pixt = GetRGBComponent(L.COLOR_GREEN);
        Pix pixg = pixt.ConvolveSep(kelx, kely, 8, 1);
        pixt = GetRGBComponent(L.COLOR_BLUE);
        Pix pixb = pixt.ConvolveSep(kelx, kely, 8, 1);
        Pix pixd = CreateRGBImage(pixr, pixg, pixb);

        return pixd;
    }

    /*------------------------------------------------------------------------*
     *              Convolution with bias (for non-negative output)           *
     *------------------------------------------------------------------------*/
    // leptonica: pixConvolveWithBias (convolve.c)
    /// <summary>Convolution that applies a bias so that the output is non-negative.</summary>
    /// <param name="kel1">kernel</param>
    /// <param name="kel2">can be null; use if separable</param>
    /// <param name="force8">if 1, force output to 8 bpp; otherwise, determine
    /// output depth by the dynamic range of pixel values</param>
    /// <param name="pbias">applied bias</param>
    /// <returns>pixd 8 or 16 bpp</returns>
    /// <remarks>
    /// This pix is 8 bpp; no colormap.
    /// (1) This does a convolution with either a single kernel or
    ///     a pair of separable kernels, and automatically applies whatever
    ///     bias (shift) is required so that the resulting pixel values
    ///     are non-negative.
    /// (2) The kernel is always normalized.  If there are no negative
    ///     values in the kernel, a standard normalized convolution is
    ///     performed, with 8 bpp output.  If the sum of kernel values is
    ///     very close to zero, the kernel can not be normalized and
    ///     the convolution will not be performed.  An error message results.
    /// (3) If there are negative values in the kernel, the pix is
    ///     converted to an fpix, the convolution is done on the fpix, and
    ///     a bias (shift) may need to be applied.
    /// (4) If force8 == TRUE and the range of values after the convolution
    ///     is > 255, the output values will be scaled to fit in [0 ... 255].
    ///     If force8 == FALSE, the output will be either 8 or 16 bpp,
    ///     to accommodate the dynamic range of output values without scaling.
    /// </remarks>
    public Pix ConvolveWithBias(L_Kernel kel1, L_Kernel? kel2, int force8, out int pbias)
    {
        const string func = "pixConvolveWithBias";
        pbias = 0;
        if (Depth != 8)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pixs has colormap");

            /* Determine if negative values can be produced in the convolution */
        kel1.GetMinMax(out float min1, out _);
        float min2 = 0.0f;
        if (kel2 != null)
            kel2.GetMinMax(out min2, out _);
        float min = Math.Min(min1, min2);

        if (min >= 0.0) {
            if (kel2 == null)
                return Convolve(kel1, 8, 1);
            else
                return ConvolveSep(kel1, kel2, 8, 1);
        }

            /* Bias may need to be applied; convert to fpix and convolve */
        FPix fpix1 = ConvertToFPix(1);
        FPix fpix2;
        if (kel2 == null)
            fpix2 = fpix1.Convolve(kel1, 1);
        else
            fpix2 = fpix1.ConvolveSep(kel1, kel2, 1);

            /* Determine the bias and the dynamic range.
             * If the dynamic range is <= 255, just shift the values by the
             * bias, if any.
             * If the dynamic range is > 255, there are two cases:
             *    (1) the output depth is not forced to 8 bpp
             *           ==> apply the bias without scaling; outdepth = 16
             *    (2) the output depth is forced to 8
             *           ==> linearly map the pixel values to [0 ... 255].  */
        fpix2.GetMin(out float minval, out _, out _);
        fpix2.GetMax(out float maxval, out _, out _);
        float range = maxval - minval;
        pbias = (int)((minval < 0.0) ? -minval : 0.0);
        fpix2.AddMultConstant(pbias, 1.0f);  /* shift: min val ==> 0 */
        int outdepth;
        if (range <= 255 || force8 == 0) {  /* no scaling of output values */
            outdepth = (range > 255) ? 16 : 8;
        } else {  /* scale output values to fit in 8 bpp */
            fpix2.AddMultConstant(0.0f, (float)(255.0 / range));
            outdepth = 8;
        }

            /* Convert back to pix; it won't do any clipping */
        Pix pixd = fpix2.ConvertToPix(outdepth, L.L_CLIP_TO_ZERO, 0);

        return pixd;
    }

    /*------------------------------------------------------------------------*
     *                Set parameter for convolution subsampling               *
     *------------------------------------------------------------------------*/
    // leptonica: l_setConvolveSampling (convolve.c)
    /// <summary>Sets the x and y output subsampling factors for generic convolution.</summary>
    /// <param name="xfact">integer >= 1</param>
    /// <param name="yfact">integer >= 1</param>
    /// <remarks>
    /// (1) This sets the x and y output subsampling factors for generic pix
    ///     and fpix convolution.  The default values are 1 (no subsampling).
    /// </remarks>
    public static void SetConvolveSampling(int xfact, int yfact)
    {
        if (xfact < 1) xfact = 1;
        if (yfact < 1) yfact = 1;
        ConvolveSamplingFactX = xfact;
        ConvolveSamplingFactY = yfact;
    }

    /*------------------------------------------------------------------------*
     *                          Additive gaussian noise                       *
     *------------------------------------------------------------------------*/
    // leptonica: pixAddGaussianNoise (convolve.c)
    /// <summary>Adds noise from a normal distribution with zero mean to each pixel.</summary>
    /// <param name="stdev">of noise</param>
    /// <returns>pixd 8 or 32 bpp</returns>
    /// <remarks>
    /// This pix is 8 bpp gray or 32 bpp rgb; no colormap.
    /// (1) This adds noise to each pixel, taken from a normal
    ///     distribution with zero mean and specified standard deviation.
    /// </remarks>
    public Pix AddGaussianNoise(float stdev)
    {
        const string func = "pixAddGaussianNoise";
        if (Colormap != null)
            throw Lept.Arg(func, "pixs has colormap");
        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 8 or 32 bpp");

        Pix pixd = CreateTemplate(this);
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpls = Wpl;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls);
            Span<uint> lined = datad.AsSpan(i * wpld);
            for (int j = 0; j < w; j++) {
                if (d == 8) {
                    int val = GetDataByte(lines, j);
                    val += (int)(stdev * GaussDistribSampling() + 0.5);
                    val = Math.Min(255, Math.Max(0, val));
                    SetDataByte(lined, j, val);
                } else {  /* d = 32 */
                    uint pixel = lines[j];
                    ExtractRGBValues(pixel, out int rval, out int gval, out int bval);
                    rval += (int)(stdev * GaussDistribSampling() + 0.5);
                    rval = Math.Min(255, Math.Max(0, rval));
                    gval += (int)(stdev * GaussDistribSampling() + 0.5);
                    gval = Math.Min(255, Math.Max(0, gval));
                    bval += (int)(stdev * GaussDistribSampling() + 0.5);
                    bval = Math.Min(255, Math.Max(0, bval));
                    ComposeRGBPixel(rval, gval, bval, out lined[j]);
                }
            }
        }
        return pixd;
    }

    /* State of gaussDistribSampling(): flips between 0 and 1 on successive calls */
    [ThreadStatic] private static int gaussSelect;
    [ThreadStatic] private static float gaussSaveval;

    // leptonica: gaussDistribSampling (convolve.c)
    /// <returns>gaussian distributed variable with zero mean and unit stdev</returns>
    /// <remarks>
    /// (1) For an explanation of the Box-Muller method for generating
    ///     a normally distributed random variable with zero mean and
    ///     unit standard deviation, see Numerical Recipes in C,
    ///     2nd edition, p. 288ff.
    /// (2) This can be called sequentially to get samples that can be
    ///     used for adding noise to each pixel of an image, for example.
    /// </remarks>
    public static float GaussDistribSampling()
    {
        float frand, xval, yval, rsq, factor;

        if (gaussSelect == 0) {
            while (true) {  /* choose a point in a 2x2 square, centered at origin */
                frand = (float)CRand.Next() / (float)CRand.RandMax;
                xval = (float)(2.0 * frand - 1.0);
                frand = (float)CRand.Next() / (float)CRand.RandMax;
                yval = (float)(2.0 * frand - 1.0);
                rsq = xval * xval + yval * yval;
                if (rsq > 0.0 && rsq < 1.0)  /* point is inside the unit circle */
                    break;
            }
            factor = (float)Math.Sqrt(-2.0 * Math.Log(rsq) / rsq);
            gaussSaveval = xval * factor;
            gaussSelect = 1;
            return yval * factor;
        } else {
            gaussSelect = 0;
            return gaussSaveval;
        }
    }
}

public sealed partial class FPix
{
    /*----------------------------------------------------------------------*
     *                  Generic convolution with float array                *
     *----------------------------------------------------------------------*/
    // leptonica: fpixConvolve (convolve.c)
    /// <summary>Generic convolution of a float array with an arbitrary kernel.</summary>
    /// <param name="kel">kernel</param>
    /// <param name="normflag">1 to normalize kernel to unit sum; 0 otherwise</param>
    /// <returns>fpixd 32 bit float array</returns>
    /// <remarks>
    /// (1) This gives a float convolution with an arbitrary kernel.
    /// (2) If normflag == 1, the result is normalized by scaling all
    ///     kernel values for a unit sum.  If the sum of kernel values
    ///     is very close to zero, the kernel can not be normalized and
    ///     the convolution will not be performed.  A warning is issued.
    /// (3) With the FPix, there are no issues about negative
    ///     array or kernel values.  The convolution is performed
    ///     with single precision arithmetic.
    /// (4) To get a subsampled output, call l_setConvolveSampling().
    ///     The time to make a subsampled output is reduced by the
    ///     product of the sampling factors.
    /// (5) This uses a mirrored border to avoid special casing on
    ///     the boundaries.
    /// </remarks>
    public FPix Convolve(L_Kernel kel, int normflag)
    {
        L_Kernel keli = kel.Invert();
        keli.GetParameters(out int sy, out int sx, out int cy, out int cx);
        L_Kernel keln;
        if (normflag != 0)
            keln = keli.Normalize(1.0f);
        else
            keln = keli.Copy();

        GetDimensions(out int w, out int h);
        FPix fpixt = AddMirroredBorder(cx, sx - cx, cy, sy - cy);

        int sfx = Pix.ConvolveSamplingFactX;
        int sfy = Pix.ConvolveSamplingFactY;
        int wd = (w + sfx - 1) / sfx;
        int hd = (h + sfy - 1) / sfy;
        FPix fpixd = CreateNoInit(wd, hd);
        float[] datat = fpixt.Data;
        float[] datad = fpixd.Data;
        int wplt = fpixt.Wpl;
        int wpld = fpixd.Wpl;
        float[] flatKel = keln.GetFlatData();

        LeptParallel.ForRows(hd, (long)wd * sy * sx * 2L, (idStart, idEnd) => {
            ref float baseT = ref MemoryMarshal.GetArrayDataReference(datat);
            ref float baseD = ref MemoryMarshal.GetArrayDataReference(datad);
            ref float baseK = ref MemoryMarshal.GetArrayDataReference(flatKel);
            for (int id = idStart; id < idEnd; id++) {
                int i = id * sfy;
                ref float rowD = ref Unsafe.Add(ref baseD, (nuint)(id * wpld));
                int jd = 0;
                if (sfx == 1 && Vector256.IsHardwareAccelerated) {
                    for (; jd + 8 <= wd; jd += 8) {
                        Vector256<float> vSum = Vector256<float>.Zero;
                        for (int k = 0; k < sy; k++) {
                            ref float rowT = ref Unsafe.Add(ref baseT, (nuint)((i + k) * wplt + jd));
                            ref float krow = ref Unsafe.Add(ref baseK, (nuint)(k * sx));
                            for (int m = 0; m < sx; m++)
                                vSum += Vector256.LoadUnsafe(ref rowT, (nuint)m) * Vector256.Create(Unsafe.Add(ref krow, (nuint)m));
                        }
                        vSum.StoreUnsafe(ref rowD, (nuint)jd);
                    }
                }
                for (int j = jd * sfx; jd < wd; j += sfx, jd++) {
                    float sum = 0.0f;
                    for (int k = 0; k < sy; k++) {
                        ref float rowT = ref Unsafe.Add(ref baseT, (nuint)((i + k) * wplt + j));
                        ref float krow = ref Unsafe.Add(ref baseK, (nuint)(k * sx));
                        for (int m = 0; m < sx; m++)
                            sum += Unsafe.Add(ref rowT, (nuint)m) * Unsafe.Add(ref krow, (nuint)m);
                    }
                    Unsafe.Add(ref rowD, (nuint)jd) = sum;
                }
            }
        });

        return fpixd;
    }

    // leptonica: fpixConvolveSep (convolve.c)
    /// <summary>Convolution of a float array with a separable kernel.</summary>
    /// <param name="kelx">x-dependent kernel</param>
    /// <param name="kely">y-dependent kernel</param>
    /// <param name="normflag">1 to normalize kernel to unit sum; 0 otherwise</param>
    /// <returns>fpixd 32 bit float array</returns>
    /// <remarks>
    /// (1) This does a convolution with a separable kernel that is
    ///     is a sequence of convolutions in x and y.  The two
    ///     one-dimensional kernel components must be input separately;
    ///     the full kernel is the product of these components.
    ///     The support for the full kernel is thus a rectangular region.
    /// (2) The normflag parameter is used as in fpixConvolve().
    /// (3) Warning: if you use l_setConvolveSampling() to get a
    ///     subsampled output, and the sampling factor is larger than
    ///     the kernel half-width, it is faster to use the non-separable
    ///     version pixConvolve().  This is because the first convolution
    ///     here must be done on every raster line, regardless of the
    ///     vertical sampling factor.  If the sampling factor is smaller
    ///     than kernel half-width, it's faster to use the separable
    ///     convolution.
    /// (4) This uses mirrored borders to avoid special casing on
    ///     the boundaries.
    /// </remarks>
    public FPix ConvolveSep(L_Kernel kelx, L_Kernel kely, int normflag)
    {
        int xfact = Pix.ConvolveSamplingFactX;
        int yfact = Pix.ConvolveSamplingFactY;
        try {
            FPix fpixt, fpixd;
            if (normflag != 0) {
                L_Kernel kelxn = kelx.Normalize(1.0f);
                L_Kernel kelyn = kely.Normalize(1.0f);
                Pix.SetConvolveSampling(xfact, 1);
                fpixt = Convolve(kelxn, 0);
                Pix.SetConvolveSampling(1, yfact);
                fpixd = fpixt.Convolve(kelyn, 0);
            } else {  /* don't normalize */
                Pix.SetConvolveSampling(xfact, 1);
                fpixt = Convolve(kelx, 0);
                Pix.SetConvolveSampling(1, yfact);
                fpixd = fpixt.Convolve(kely, 0);
            }
            return fpixd;
        } finally {
            Pix.SetConvolveSampling(xfact, yfact);  /* restore */
        }
    }
}
