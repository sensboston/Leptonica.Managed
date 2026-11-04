using System;
using System.Threading;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.Convolve;

/// <summary>
/// Tests of convolve.c against direct computations on small random images: window sums are
/// formed by plain loops over the window, borders included.  The functions that need
/// pixtiling.c (pixBlockconvTiled) or fpix2.c (fpixConvolve, the negative-kernel path of
/// pixConvolveWithBias) are not covered here, because those files are not ported yet.
/// </summary>
public class ConvolveTests
{
    private static Pix MakePix(Random rng, int w, int h, int d, uint max)
    {
        Pix pix = Pix.Create(w, h, d);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Naive.SetPixel(pix, j, i, (uint)rng.NextInt64(0, (long)max + 1));
        return pix;
    }

    private static int Mirror(int i, int n) => i < 0 ? -i - 1 : (i >= n ? 2 * n - 1 - i : i);

    private static L_Kernel MakeKernel(int sy, int sx, int cy, int cx, params float[] values)
    {
        L_Kernel kel = L_Kernel.Create(sy, sx);
        kel.SetOrigin(cy, cx);
        int k = 0;
        for (int i = 0; i < sy; i++)
            for (int j = 0; j < sx; j++)
                kel.SetElement(i, j, values[k++]);
        return kel;
    }

    private static void AssertSame(Pix expected, Pix actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Depth, actual.Depth);
        for (int i = 0; i < expected.Height; i++)
            for (int j = 0; j < expected.Width; j++)
                Assert.True(Naive.GetPixel(expected, j, i) == Naive.GetPixel(actual, j, i),
                            $"differ at ({j},{i}): {Naive.GetPixel(expected, j, i)} vs {Naive.GetPixel(actual, j, i)}");
    }

    /*----------------------------- accumulators ----------------------------*/

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(32)]
    public void BlockconvAccum_IsRectangleSum(int d)
    {
        var rng = new Random(10 + d);
        int w = 17, h = 9;
        Pix pix = MakePix(rng, w, h, d, d == 1 ? 1u : (d == 8 ? 255u : 100000u));
        Pix acc = pix.BlockconvAccum();
        Assert.Equal(32, acc.Depth);
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                uint sum = 0;
                for (int y = 0; y <= i; y++)
                    for (int x = 0; x <= j; x++)
                        sum += Naive.GetPixel(pix, x, y);
                Assert.Equal(sum, Naive.GetPixel(acc, j, i));
            }
        }
    }

    [Fact]
    public void BlockconvAccum_RejectsOtherDepths()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 16).BlockconvAccum());
    }

    [Fact]
    public void MeanSquareAccum_IsRectangleSumOfSquares()
    {
        var rng = new Random(21);
        int w = 12, h = 7;
        Pix pix = MakePix(rng, w, h, 8, 255);
        DPix dpix = pix.MeanSquareAccum();
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                double sum = 0;
                for (int y = 0; y <= i; y++)
                    for (int x = 0; x <= j; x++) {
                        double v = Naive.GetPixel(pix, x, y);
                        sum += v * v;
                    }
                Assert.Equal(sum, dpix.Data[i * dpix.Wpl + j]);
            }
        }
    }

    /*------------------------- block convolution (gray) --------------------*/

    /* Window of rows (imin, imax] and columns (jmin, jmax], with imin and jmin clamped at 0,
     * then the two normalizations of the boundary, exactly as blockconvLow and blocksumLow
     * specify them (conv = true for the former). */
    private static byte[,] RefBlock(Func<int, int, uint> px, int w, int h, int wc, int hc, bool conv)
    {
        int fwc = 2 * wc + 1, fhc = 2 * hc + 1;
        float norm = conv ? (float)(1.0 / ((float)fwc * fhc)) : (float)(255.0 / ((float)fwc * fhc));
        var res = new byte[h, w];
        for (int i = 0; i < h; i++) {
            int imin = Math.Max(i - 1 - hc, 0), imax = Math.Min(i + hc, h - 1);
            for (int j = 0; j < w; j++) {
                int jmin = Math.Max(j - 1 - wc, 0), jmax = Math.Min(j + wc, w - 1);
                uint sum = 0;
                for (int y = imin + 1; y <= imax; y++)
                    for (int x = jmin + 1; x <= jmax; x++)
                        sum += px(x, y);
                uint val = conv ? (byte)(norm * sum + 0.5) : (byte)(norm * sum);

                bool hasH = false, hasW = false;
                float normh = 1f, normw = 1f;
                if (i <= hc) { hasH = true; normh = (float)fhc / (float)Math.Max(1, hc + i); }
                else if (i >= h - hc) { hasH = true; normh = (float)fhc / (float)(hc + h - i); }
                if (j <= wc) { hasW = true; normw = (float)fwc / (float)Math.Max(1, wc + j); }
                else if (j >= w - wc) { hasW = true; normw = (float)fwc / (float)(wc + w - j); }
                if (hasH || hasW) {
                    float f = val;
                    if (hasH) f = f * normh;
                    if (hasW) f = f * normw;
                    val = conv ? (byte)Math.Min(f, 255f) : (byte)f;
                }
                res[i, j] = (byte)val;
            }
        }
        return res;
    }

    private static void AssertBlock(byte[,] expected, Pix actual)
    {
        for (int i = 0; i < expected.GetLength(0); i++)
            for (int j = 0; j < expected.GetLength(1); j++)
                Assert.True(expected[i, j] == Naive.GetPixel(actual, j, i),
                            $"differ at ({j},{i}): {expected[i, j]} vs {Naive.GetPixel(actual, j, i)}");
    }

    [Theory]
    [InlineData(13, 11, 1, 1)]
    [InlineData(13, 11, 2, 1)]
    [InlineData(13, 11, 3, 4)]
    [InlineData(7, 9, 3, 4)]      /* smallest legal size for the kernel */
    [InlineData(20, 5, 1, 2)]
    public void BlockconvGray_MatchesDirectWindow(int w, int h, int wc, int hc)
    {
        var rng = new Random(w * 100 + h);
        Pix pix = MakePix(rng, w, h, 8, 255);
        Pix res = pix.BlockconvGray(null, wc, hc);
        AssertBlock(RefBlock((x, y) => Naive.GetPixel(pix, x, y), w, h, wc, hc, true), res);
    }

    [Fact]
    public void BlockconvGray_WhiteImageDoesNotWrapAround()
    {
        Pix pix = Pix.Create(15, 12, 8);
        for (int i = 0; i < 12; i++)
            for (int j = 0; j < 15; j++)
                Naive.SetPixel(pix, j, i, 255);
        Pix res = pix.BlockconvGray(null, 2, 3);
        /* the boundary normalization is clipped at 255; an overflow would wrap to small values */
        for (int i = 0; i < 12; i++)
            for (int j = 0; j < 15; j++)
                Assert.InRange(Naive.GetPixel(res, j, i), 240u, 255u);
    }

    [Fact]
    public void BlockconvGray_GivenAccumGivesSameResult()
    {
        var rng = new Random(5);
        Pix pix = MakePix(rng, 14, 10, 8, 255);
        Pix acc = pix.BlockconvAccum();
        AssertSame(pix.BlockconvGray(null, 2, 2), pix.BlockconvGray(acc, 2, 2));
        /* an accum pix that is not 32 bpp is replaced */
        AssertSame(pix.BlockconvGray(null, 2, 2), pix.BlockconvGray(pix, 2, 2));
    }

    [Fact]
    public void BlockconvGray_ZeroHalfSizeReturnsCopy()
    {
        var rng = new Random(6);
        Pix pix = MakePix(rng, 9, 9, 8, 255);
        Pix res = pix.BlockconvGray(null, 0, 3);
        Assert.NotSame(pix, res);
        AssertSame(pix, res);
    }

    [Fact]
    public void BlockconvGray_TooLargeKernelIsReduced()
    {
        var rng = new Random(7);
        Pix pix = MakePix(rng, 7, 5, 8, 255);
        /* wc = 10 -> 3, hc = 10 -> 2 */
        Pix res = pix.BlockconvGray(null, 10, 10);
        AssertSame(pix.BlockconvGray(null, 3, 2), res);
    }

    [Fact]
    public void BlockconvGray_RejectsNon8bpp()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(9, 9, 32).BlockconvGray(null, 1, 1));
    }

    [Fact]
    public void Blockconv_ColorIsPerComponent()
    {
        var rng = new Random(8);
        Pix pix = MakePix(rng, 13, 9, 32, 0xffffffffu);
        Pix res = pix.Blockconv(2, 1);
        Assert.Equal(32, res.Depth);
        for (int comp = 0; comp < 3; comp++) {
            Pix c = pix.GetRGBComponent(comp);
            Pix expected = c.BlockconvGray(null, 2, 1);
            Pix actual = res.GetRGBComponent(comp);
            AssertSame(expected, actual);
        }
    }

    [Fact]
    public void Blockconv_GrayAndEdgeCases()
    {
        var rng = new Random(9);
        Pix pix = MakePix(rng, 11, 11, 8, 255);
        AssertSame(pix.BlockconvGray(null, 2, 2), pix.Blockconv(2, 2));
        AssertSame(pix, pix.Blockconv(0, 2));
        Assert.Throws<ArgumentException>(() => Pix.Create(9, 9, 16).Blockconv(1, 1));
    }

    [Fact]
    public void Blockconv_RemovesColormap()
    {
        PixColormap cmap = PixColormap.Create(4);
        for (int i = 0; i < 16; i++)
            cmap.AddColor(i * 16, i * 16, i * 16);
        Pix pix = Pix.Create(12, 12, 4);
        pix.SetColormap(cmap);
        var rng = new Random(3);
        for (int i = 0; i < 12; i++)
            for (int j = 0; j < 12; j++)
                Naive.SetPixel(pix, j, i, (uint)rng.Next(16));
        Pix res = pix.Blockconv(1, 1);
        Assert.Equal(8, res.Depth);
        Assert.Null(res.Colormap);
    }

    [Theory]
    [InlineData(13, 11, 1, 1)]
    [InlineData(10, 9, 2, 3)]
    [InlineData(5, 7, 2, 3)]
    public void BlockconvGrayUnnormalized_IsMirroredWindowSum(int w, int h, int wc, int hc)
    {
        var rng = new Random(w + h);
        Pix pix = MakePix(rng, w, h, 8, 255);
        Pix res = pix.BlockconvGrayUnnormalized(wc, hc);
        Assert.Equal(32, res.Depth);
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                uint sum = 0;
                for (int dy = -hc; dy <= hc; dy++)
                    for (int dx = -wc; dx <= wc; dx++)
                        sum += Naive.GetPixel(pix, Mirror(j + dx, w), Mirror(i + dy, h));
                Assert.True(sum == Naive.GetPixel(res, j, i), $"differ at ({j},{i})");
            }
        }
    }

    [Fact]
    public void BlockconvGrayTile_MatchesDirectWindowInTheTileCore()
    {
        var rng = new Random(12);
        int w = 16, h = 14, wc = 2, hc = 3;
        Pix pix = MakePix(rng, w, h, 8, 255);
        Pix res = pix.BlockconvGrayTile(null, wc, hc);
        float norm = 1.0f / (float)((2 * wc + 1) * (2 * hc + 1));
        int wd = w - 2 * wc, hd = h - 2 * hc;
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                uint expected = 0;
                if (i >= hc && i < hc + hd - 2 && j >= wc && j < wc + wd - 2) {
                    int imin = Math.Max(i - hc - 1, 0), imax = Math.Min(i + hc, h - 1);
                    int jmin = Math.Max(j - wc - 1, 0), jmax = Math.Min(j + wc, w - 1);
                    uint sum = 0;
                    for (int y = imin + 1; y <= imax; y++)
                        for (int x = jmin + 1; x <= jmax; x++)
                            sum += Naive.GetPixel(pix, x, y);
                    expected = (byte)(norm * sum + 0.5);
                }
                Assert.True(expected == Naive.GetPixel(res, j, i), $"differ at ({j},{i})");
            }
        }
    }

    [Fact]
    public void BlockconvTiled_SingleTileDefaultsToBlockconv()
    {
        var rng = new Random(13);
        Pix pix = MakePix(rng, 12, 12, 8, 255);
        AssertSame(pix.Blockconv(2, 2), pix.BlockconvTiled(2, 2, 1, 1));
        AssertSame(pix, pix.BlockconvTiled(0, 2, 2, 2));
    }

    /*----------------------- windowed mean / variance ----------------------*/

    private static Pix ZeroBorder(Pix pix, int wc, int hc)
    {
        Pix b = Pix.Create(pix.Width + 2 * (wc + 1), pix.Height + 2 * (hc + 1), pix.Depth);
        for (int i = 0; i < pix.Height; i++)
            for (int j = 0; j < pix.Width; j++)
                Naive.SetPixel(b, j + wc + 1, i + hc + 1, Naive.GetPixel(pix, j, i));
        return b;
    }

    private static uint WindowSum(Pix b, int x, int y, int wc, int hc)
    {
        uint sum = 0;
        for (int dy = -hc; dy <= hc; dy++)
            for (int dx = -wc; dx <= wc; dx++)
                sum += Naive.GetPixel(b, x + wc + 1 + dx, y + hc + 1 + dy);
        return sum;
    }

    [Theory]
    [InlineData(8, 1, 1)]
    [InlineData(8, 0, 1)]
    [InlineData(8, 0, 0)]
    [InlineData(32, 1, 1)]
    [InlineData(32, 0, 0)]
    public void WindowedMean_MatchesDirectWindow(int d, int hasborder, int normflag)
    {
        var rng = new Random(30 + d + hasborder);
        int wc = 2, hc = 3;
        uint max = d == 8 ? (normflag == 1 ? 255u : 3u) : 1000u;
        Pix pix = MakePix(rng, 15, 14, d, max);
        Pix b = hasborder == 1 ? pix : ZeroBorder(pix, wc, hc);
        Pix res = pix.WindowedMean(wc, hc, hasborder, normflag);

        int wd = b.Width - 2 * (wc + 1), hd = b.Height - 2 * (hc + 1);
        Assert.Equal((wd, hd, d), (res.Width, res.Height, res.Depth));
        float norm = normflag == 1 ? (float)(1.0 / ((float)(2 * wc + 1) * (2 * hc + 1))) : 1.0f;
        for (int i = 0; i < hd; i++) {
            for (int j = 0; j < wd; j++) {
                uint sum = WindowSum(b, j, i, wc, hc);
                uint expected = d == 8 ? (byte)(int)(norm * sum) : (uint)(norm * sum);
                Assert.True(expected == Naive.GetPixel(res, j, i), $"differ at ({j},{i})");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void WindowedMeanSquare_MatchesDirectWindow(int hasborder)
    {
        var rng = new Random(40 + hasborder);
        int wc = 2, hc = 2;
        Pix pix = MakePix(rng, 14, 13, 8, 255);
        Pix b = hasborder == 1 ? pix : ZeroBorder(pix, wc, hc);
        Pix res = pix.WindowedMeanSquare(wc, hc, hasborder);

        int wd = b.Width - 2 * (wc + 1), hd = b.Height - 2 * (hc + 1);
        Assert.Equal((wd, hd, 32), (res.Width, res.Height, res.Depth));
        double norm = 1.0 / ((float)(2 * wc + 1) * (2 * hc + 1));
        for (int i = 0; i < hd; i++) {
            for (int j = 0; j < wd; j++) {
                double sum = 0;
                for (int dy = -hc; dy <= hc; dy++)
                    for (int dx = -wc; dx <= wc; dx++) {
                        double v = Naive.GetPixel(b, j + wc + 1 + dx, i + hc + 1 + dy);
                        sum += v * v;
                    }
                uint expected = (uint)(norm * sum + 0.5);
                Assert.True(expected == Naive.GetPixel(res, j, i), $"differ at ({j},{i})");
            }
        }
    }

    [Fact]
    public void WindowedMean_RejectsBadArguments()
    {
        Pix pix = Pix.Create(20, 20, 8);
        Assert.Throws<ArgumentException>(() => pix.WindowedMean(1, 2, 0, 1));
        Assert.Throws<ArgumentException>(() => pix.WindowedMeanSquare(2, 1, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(20, 20, 16).WindowedMean(2, 2, 0, 1));
        /* hasborder = 1 and an image too small for the border */
        Assert.Throws<LeptonicaException>(() => Pix.Create(7, 7, 8).WindowedMean(2, 2, 1, 1));
    }

    [Fact]
    public void WindowedStats_VarianceAndRmsFromMeanAndMeanSquare()
    {
        var rng = new Random(50);
        int wc = 2, hc = 2;
        Pix pix = MakePix(rng, 14, 12, 8, 255);
        pix.WindowedStats(wc, hc, 0, out Pix pixm, out Pix pixms, out FPix fpixv, out FPix fpixrv);

        AssertSame(pix.WindowedMean(wc, hc, 0, 1), pixm);
        AssertSame(pix.WindowedMeanSquare(wc, hc, 0), pixms);
        Assert.Equal((14, 12), (pixm.Width, pixm.Height));
        for (int i = 0; i < 12; i++) {
            for (int j = 0; j < 14; j++) {
                int valm = (int)Naive.GetPixel(pixm, j, i);
                int valms = (int)Naive.GetPixel(pixms, j, i);
                float var = (float)valms - (float)valm * valm;
                fpixv.GetPixel(j, i, out float v);
                fpixrv.GetPixel(j, i, out float rv);
                Assert.Equal(var, v);
                Assert.Equal((float)Math.Sqrt(var), rv);
            }
        }
    }

    [Fact]
    public void WindowedVariance_ChecksItsInputs()
    {
        Pix pixm = Pix.Create(10, 10, 8);
        Assert.Throws<ArgumentException>(() => pixm.WindowedVariance(Pix.Create(10, 10, 8), out _, out _));
        Assert.Throws<ArgumentException>(() => pixm.WindowedVariance(Pix.Create(9, 10, 32), out _, out _));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 32).WindowedVariance(Pix.Create(10, 10, 32), out _, out _));
    }

    /*---------------------- binary block sum / rank / census ---------------*/

    [Theory]
    [InlineData(13, 11, 1, 1)]
    [InlineData(13, 11, 3, 2)]
    [InlineData(7, 9, 3, 4)]
    public void Blocksum_MatchesDirectWindow(int w, int h, int wc, int hc)
    {
        var rng = new Random(60 + w);
        Pix pix = MakePix(rng, w, h, 1, 1);
        Pix res = pix.Blocksum(null, wc, hc);
        Assert.Equal(8, res.Depth);
        AssertBlock(RefBlock((x, y) => Naive.GetPixel(pix, x, y), w, h, wc, hc, false), res);
        /* same result with a precomputed accumulator */
        AssertSame(res, pix.Blocksum(pix.BlockconvAccum(), wc, hc));
    }

    [Fact]
    public void Blocksum_CopiesResolutionAndChecksInputs()
    {
        Pix pix = Pix.Create(10, 10, 1);
        pix.XRes = 300;
        pix.YRes = 200;
        Pix res = pix.Blocksum(null, 1, 1);
        Assert.Equal((300, 200), (res.XRes, res.YRes));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 8).Blocksum(null, 1, 1));
        Assert.Throws<ArgumentException>(() => pix.Blocksum(Pix.Create(10, 10, 8), 1, 1));
        AssertSame(pix, pix.Blocksum(null, 0, 1));
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void Blockrank_ThresholdsTheBlockSum(float rank)
    {
        var rng = new Random(70);
        int w = 14, h = 12, wc = 2, hc = 1;
        Pix pix = MakePix(rng, w, h, 1, 1);
        Pix res = pix.Blockrank(null, wc, hc, rank);
        Assert.Equal(1, res.Depth);
        byte[,] sums = RefBlock((x, y) => Naive.GetPixel(pix, x, y), w, h, wc, hc, false);
        int thresh = (int)(255.0 * rank);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Assert.True((sums[i, j] >= thresh ? 1u : 0u) == Naive.GetPixel(res, j, i), $"differ at ({j},{i})");
    }

    [Fact]
    public void Blockrank_SpecialCases()
    {
        var rng = new Random(71);
        Pix pix = MakePix(rng, 9, 9, 1, 1);
        Pix all = pix.Blockrank(null, 2, 2, 0.0f);
        for (int i = 0; i < 9; i++)
            for (int j = 0; j < 9; j++)
                Assert.Equal(1u, Naive.GetPixel(all, j, i));
        AssertSame(pix, pix.Blockrank(null, 0, 0, 0.5f));
        Assert.Throws<ArgumentException>(() => pix.Blockrank(null, 1, 1, 1.5f));
        Assert.Throws<ArgumentException>(() => pix.Blockrank(null, 1, 1, -0.1f));
        Assert.Throws<ArgumentException>(() => Pix.Create(9, 9, 8).Blockrank(null, 1, 1, 0.5f));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void CensusTransform_ComparesPixelWithBlockAverage(int halfsize)
    {
        var rng = new Random(80);
        int w = 15, h = 13;
        Pix pix = MakePix(rng, w, h, 8, 255);
        Pix res = pix.CensusTransform(halfsize, null);
        Assert.Equal(1, res.Depth);
        byte[,] avg = RefBlock((x, y) => Naive.GetPixel(pix, x, y), w, h, halfsize, halfsize, true);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Assert.True((Naive.GetPixel(pix, j, i) > avg[i, j] ? 1u : 0u) == Naive.GetPixel(res, j, i),
                            $"differ at ({j},{i})");
        AssertSame(res, pix.CensusTransform(halfsize, pix.BlockconvAccum()));
    }

    [Fact]
    public void CensusTransform_ChecksInputs()
    {
        Pix pix = Pix.Create(9, 9, 8);
        Assert.Throws<ArgumentException>(() => pix.CensusTransform(0, null));
        Assert.Throws<ArgumentException>(() => Pix.Create(9, 9, 1).CensusTransform(1, null));
    }

    /*---------------------------- generic convolution ----------------------*/

    private static long RefConvAt(Pix pix, L_Kernel kel, int i, int j)
    {
        kel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        long sum = 0;
        for (int k = 0; k < sy; k++)
            for (int m = 0; m < sx; m++)
                sum += (long)kel.Data[k][m] *
                       Naive.GetPixel(pix, Mirror(j + cx - m, pix.Width), Mirror(i + cy - k, pix.Height));
        return sum;
    }

    [Theory]
    [InlineData(8, 16)]
    [InlineData(16, 32)]
    [InlineData(32, 32)]
    [InlineData(8, 8)]
    public void Convolve_IntegerKernelIsExactConvolution(int d, int outdepth)
    {
        var rng = new Random(90 + d);
        uint max = outdepth == 8 ? 20u : (d == 32 ? 100000u : 255u);
        Pix pix = MakePix(rng, 11, 9, d, max);
        /* 3 x 2 kernel with an origin that is not in the center */
        L_Kernel kel = MakeKernel(3, 2, 2, 0, 1, -2, 0, 3, 2, 1);
        Pix res = pix.Convolve(kel, outdepth, 0);
        Assert.Equal((11, 9, outdepth), (res.Width, res.Height, res.Depth));
        for (int i = 0; i < 9; i++)
            for (int j = 0; j < 11; j++) {
                long expected = Math.Abs(RefConvAt(pix, kel, i, j));
                if (outdepth == 8) expected &= 0xff;
                Assert.True(expected == Naive.GetPixel(res, j, i), $"differ at ({j},{i})");
            }
    }

    [Fact]
    public void Convolve_NormalizedKernel()
    {
        var rng = new Random(95);
        Pix pix = MakePix(rng, 12, 10, 8, 255);
        /* sum = 8, so the normalization factor 1/8 is exact */
        L_Kernel kel = MakeKernel(3, 3, 1, 1, 1, 2, 1, 1, 2, 1, 0, 0, 0);
        Assert.Equal(8.0f, kel.GetSum());
        Pix res = pix.Convolve(kel, 8, 1);
        for (int i = 0; i < 10; i++)
            for (int j = 0; j < 12; j++) {
                double expected = Math.Abs(RefConvAt(pix, kel, i, j)) / 8.0;
                Assert.True((int)(expected + 0.5) == (int)Naive.GetPixel(res, j, i), $"differ at ({j},{i})");
            }
    }

    [Fact]
    public void Convolve_Subsampling()
    {
        var rng = new Random(96);
        Pix pix = MakePix(rng, 9, 8, 8, 255);
        L_Kernel kel = MakeKernel(3, 3, 1, 1, 0, 1, 0, 1, 2, 1, 0, 1, 0);
        Pix full = pix.Convolve(kel, 16, 0);
        try {
            Pix.SetConvolveSampling(2, 3);
            Pix sub = pix.Convolve(kel, 16, 0);
            Assert.Equal((5, 3), (sub.Width, sub.Height));
            for (int id = 0; id < 3; id++)
                for (int jd = 0; jd < 5; jd++)
                    Assert.Equal(Naive.GetPixel(full, jd * 2, id * 3), Naive.GetPixel(sub, jd, id));
        } finally {
            Pix.SetConvolveSampling(1, 1);
        }
    }

    [Fact]
    public void SetConvolveSampling_ClampsToOne()
    {
        try {
            Pix.SetConvolveSampling(0, -4);
            Assert.Equal((1, 1), (Pix.ConvolveSamplingFactX, Pix.ConvolveSamplingFactY));
            Pix.SetConvolveSampling(3, 2);
            Assert.Equal((3, 2), (Pix.ConvolveSamplingFactX, Pix.ConvolveSamplingFactY));
        } finally {
            Pix.SetConvolveSampling(1, 1);
        }
    }

    [Fact]
    public void Convolve_ChecksInputs()
    {
        L_Kernel kel = MakeKernel(1, 1, 0, 0, 1);
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 1).Convolve(kel, 8, 0));
        Pix cmapped = Pix.Create(5, 5, 8);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(0, 0, 0);
        cmapped.SetColormap(cmap);
        Assert.Throws<ArgumentException>(() => cmapped.Convolve(kel, 8, 0));
    }

    [Fact]
    public void ConvolveSep_UnnormalizedIsTwoPassConvolution()
    {
        var rng = new Random(97);
        Pix pix = MakePix(rng, 12, 11, 8, 255);
        L_Kernel kelx = MakeKernel(1, 3, 0, 1, 1, -2, 1);
        L_Kernel kely = MakeKernel(2, 1, 1, 0, 3, 2);
        Pix res = pix.ConvolveSep(kelx, kely, 32, 0);

            /* each pass stores the absolute value */
        Pix inter = Pix.Create(12, 11, 32);
        for (int i = 0; i < 11; i++)
            for (int j = 0; j < 12; j++)
                Naive.SetPixel(inter, j, i, (uint)Math.Abs(RefConvAt(pix, kelx, i, j)));
        for (int i = 0; i < 11; i++)
            for (int j = 0; j < 12; j++)
                Assert.True(Math.Abs(RefConvAt(inter, kely, i, j)) == Naive.GetPixel(res, j, i), $"differ at ({j},{i})");
        Assert.Equal((1, 1), (Pix.ConvolveSamplingFactX, Pix.ConvolveSamplingFactY));
    }

    [Fact]
    public void ConvolveSep_NormalizedIsCloseToDirectResult()
    {
        var rng = new Random(98);
        Pix pix = MakePix(rng, 12, 11, 8, 255);
        L_Kernel kelx = MakeKernel(1, 3, 0, 1, 1, 2, 1);
        L_Kernel kely = MakeKernel(3, 1, 1, 0, 1, 2, 1);
        Pix res = pix.ConvolveSep(kelx, kely, 8, 1);
        L_Kernel prod = L_Kernel.Create(3, 3);
        prod.SetOrigin(1, 1);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                prod.SetElement(i, j, kely.Data[i][0] * kelx.Data[0][j]);
        for (int i = 0; i < 11; i++)
            for (int j = 0; j < 12; j++) {
                double expected = RefConvAt(pix, prod, i, j) / 16.0;
                Assert.True(Math.Abs(expected - Naive.GetPixel(res, j, i)) <= 1.0, $"differ at ({j},{i})");
            }
    }

    [Fact]
    public void ConvolveRGB_IsPerComponent()
    {
        var rng = new Random(99);
        Pix pix = MakePix(rng, 10, 9, 32, 0xffffffffu);
        L_Kernel kel = MakeKernel(3, 3, 1, 1, 0, 1, 0, 1, 4, 1, 0, 1, 0);
        Pix res = pix.ConvolveRGB(kel);
        Assert.Equal(32, res.Depth);
        for (int comp = 0; comp < 3; comp++)
            AssertSame(pix.GetRGBComponent(comp).Convolve(kel, 8, 1), res.GetRGBComponent(comp));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).ConvolveRGB(kel));

        L_Kernel kelx = MakeKernel(1, 3, 0, 1, 1, 2, 1);
        L_Kernel kely = MakeKernel(3, 1, 1, 0, 1, 2, 1);
        Pix sep = pix.ConvolveRGBSep(kelx, kely);
        for (int comp = 0; comp < 3; comp++)
            AssertSame(pix.GetRGBComponent(comp).ConvolveSep(kelx, kely, 8, 1), sep.GetRGBComponent(comp));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).ConvolveRGBSep(kelx, kely));
    }

    [Fact]
    public void ConvolveWithBias_PositiveKernelIsPlainNormalizedConvolution()
    {
        var rng = new Random(100);
        Pix pix = MakePix(rng, 10, 9, 8, 255);
        L_Kernel kel = MakeKernel(3, 3, 1, 1, 0, 1, 0, 1, 4, 1, 0, 1, 0);
        Pix res = pix.ConvolveWithBias(kel, null, 0, out int bias);
        Assert.Equal(0, bias);
        AssertSame(pix.Convolve(kel, 8, 1), res);

        L_Kernel kelx = MakeKernel(1, 3, 0, 1, 1, 2, 1);
        L_Kernel kely = MakeKernel(3, 1, 1, 0, 1, 2, 1);
        AssertSame(pix.ConvolveSep(kelx, kely, 8, 1), pix.ConvolveWithBias(kelx, kely, 1, out bias));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 32).ConvolveWithBias(kel, null, 0, out _));
    }

    /*------------------------------ gaussian noise -------------------------*/

    /* gaussDistribSampling() keeps its state per thread; run on a fresh thread to start clean. */
    private static void OnFreshThread(Action action)
    {
        Exception? error = null;
        var t = new Thread(() => {
            try { action(); } catch (Exception e) { error = e; }
        });
        t.Start();
        t.Join();
        if (error != null)
            throw new Exception("failed on the helper thread", error);
    }

    [Fact]
    public void GaussDistribSampling_FollowsBoxMuller()
    {
        OnFreshThread(() => {
            CRand.Seed(7);
            float x, y, rsq;
            do {
                float f = (float)CRand.Next() / (float)CRand.RandMax;
                x = (float)(2.0 * f - 1.0);
                f = (float)CRand.Next() / (float)CRand.RandMax;
                y = (float)(2.0 * f - 1.0);
                rsq = x * x + y * y;
            } while (!(rsq > 0.0 && rsq < 1.0));
            float factor = (float)Math.Sqrt(-2.0 * Math.Log(rsq) / rsq);

            CRand.Seed(7);
            Assert.Equal(y * factor, Pix.GaussDistribSampling());
            Assert.Equal(x * factor, Pix.GaussDistribSampling());   /* the saved value */
        });
    }

    [Fact]
    public void GaussDistribSampling_HasUnitVariance()
    {
        OnFreshThread(() => {
            CRand.Seed(1234);
            int n = 20000;
            double sum = 0, sumsq = 0;
            for (int i = 0; i < n; i++) {
                double v = Pix.GaussDistribSampling();
                sum += v;
                sumsq += v * v;
            }
            Assert.InRange(sum / n, -0.05, 0.05);
            Assert.InRange(sumsq / n, 0.95, 1.05);
        });
    }

    [Theory]
    [InlineData(8)]
    [InlineData(32)]
    public void AddGaussianNoise_UsesTheSamplesInRasterOrder(int d)
    {
        var rng = new Random(110);
        Pix pix = MakePix(rng, 9, 7, d, d == 8 ? 255u : 0xffffff00u);
        Pix res = null!;
        float stdev = 40.0f;
        OnFreshThread(() => {
            CRand.Seed(3);
            res = pix.AddGaussianNoise(stdev);
        });
        OnFreshThread(() => {
            CRand.Seed(3);
            for (int i = 0; i < 7; i++) {
                for (int j = 0; j < 9; j++) {
                    uint pixel = Naive.GetPixel(pix, j, i);
                    if (d == 8) {
                        int val = (int)pixel + (int)(stdev * Pix.GaussDistribSampling() + 0.5);
                        val = Math.Min(255, Math.Max(0, val));
                        Assert.True((uint)val == Naive.GetPixel(res, j, i), $"differ at ({j},{i})");
                    } else {
                        Pix.ExtractRGBValues(pixel, out int r, out int g, out int b);
                        r = Math.Min(255, Math.Max(0, r + (int)(stdev * Pix.GaussDistribSampling() + 0.5)));
                        g = Math.Min(255, Math.Max(0, g + (int)(stdev * Pix.GaussDistribSampling() + 0.5)));
                        b = Math.Min(255, Math.Max(0, b + (int)(stdev * Pix.GaussDistribSampling() + 0.5)));
                        Pix.ComposeRGBPixel(r, g, b, out uint expected);
                        Assert.True(expected == Naive.GetPixel(res, j, i), $"differ at ({j},{i})");
                    }
                }
            }
        });
    }

    [Fact]
    public void AddGaussianNoise_ChecksInputs()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 16).AddGaussianNoise(1.0f));
    }
}
