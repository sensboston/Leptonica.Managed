using System;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.BilateralTests;

/// <summary>Tests of bilateral.c: properties of both filters and a naive reference of the exact one.</summary>
public class BilateralTests
{
    private static Pix Make(int w, int h, Func<int, int, uint> f)
    {
        Pix pix = Pix.Create(w, h, 8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Naive.SetPixel(pix, x, y, f(x, y));
        return pix;
    }

    private static Pix Noisy(int w, int h, int seed)
    {
        var rnd = new Random(seed);
        return Make(w, h, (x, y) => (uint)((x * 3 + y * 2 + rnd.Next(40)) & 0xff));
    }

    private static Pix Step(int w, int h, uint left, uint right) =>
        Make(w, h, (x, y) => x < w / 2 ? left : right);

    private static void AssertSame(Pix expected, Pix actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Depth, actual.Depth);
        Assert.Equal(expected.Data, actual.Data);
    }

    /// <summary>Straight-line version of the exact filter on mirrored coordinates.</summary>
    private static Pix NaiveExact(Pix pixs, L_Kernel spatial, L_Kernel range)
    {
        int w = pixs.Width, h = pixs.Height;
        spatial.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Pix pixd = Pix.Create(w, h, 8);
        int Mirror(int v, int n) => v < 0 ? -v - 1 : (v >= n ? 2 * n - 1 - v : v);
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                int center = (int)Naive.GetPixel(pixs, x, y);
                float sum = 0.0f, wsum = 0.0f;
                for (int k = 0; k < sy; k++) {
                    for (int m = 0; m < sx; m++) {
                        int val = (int)Naive.GetPixel(pixs, Mirror(x + m - cx, w), Mirror(y + k - cy, h));
                        float weight = spatial.Data[sy - 1 - k][sx - 1 - m] * range.Data[0][Math.Abs(center - val)];
                        wsum += weight;
                        sum += val * weight;
                    }
                }
                Naive.SetPixel(pixd, x, y, (uint)(int)(sum / wsum + 0.5));
            }
        }
        return pixd;
    }

    [Fact]
    public void MakeRangeKernel_IsOneSidedGaussian()
    {
        L_Kernel kel = Pix.MakeRangeKernel(20.0f);
        kel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal(1, sy);
        Assert.Equal(256, sx);
        Assert.Equal(0, cy);
        Assert.Equal(0, cx);
        Assert.Equal(1.0f, kel.Data[0][0]);
        for (int x = 1; x < 256; x++) {
            Assert.Equal(MathF.Exp(-(float)(x * x) / (float)(2.0 * 20.0f * 20.0f)), kel.Data[0][x]);
            Assert.True(kel.Data[0][x] < kel.Data[0][x - 1]);
        }
        Assert.Throws<ArgumentException>(() => Pix.MakeRangeKernel(0.0f));
        Assert.Throws<ArgumentException>(() => Pix.MakeRangeKernel(-3.0f));
    }

    [Theory]
    [InlineData(1, 1.0f, 20.0f)]
    [InlineData(2, 1.5f, 40.0f)]
    [InlineData(3, 2.0f, 255.0f)]
    public void BilateralGrayExact_MatchesNaiveReference(int seed, float sstdev, float rstdev)
    {
        Pix pixs = Noisy(41, 29, seed);
        int halfwidth = (int)(2 * sstdev);
        L_Kernel spatial = L_Kernel.MakeGaussianKernel(halfwidth, halfwidth, sstdev, 1.0f);
        L_Kernel range = Pix.MakeRangeKernel(rstdev);
        AssertSame(NaiveExact(pixs, spatial, range), pixs.BilateralGrayExact(spatial, range));
        AssertSame(NaiveExact(pixs, spatial, range), pixs.BlockBilateralExact(sstdev, rstdev));
    }

    [Fact]
    public void BilateralGrayExact_WithoutRangeKernelIsConvolution()
    {
        Pix pixs = Noisy(30, 20, 7);
        L_Kernel spatial = L_Kernel.MakeGaussianKernel(2, 2, 1.0f, 1.0f);
        AssertSame(pixs.Convolve(spatial, 8, 1), pixs.BilateralGrayExact(spatial, null));
    }

    [Fact]
    public void BilateralGrayExact_SmallImageReturnsCopy()
    {
        Pix pixs = Noisy(4, 4, 3);
        L_Kernel spatial = L_Kernel.MakeGaussianKernel(2, 2, 1.0f, 1.0f);
        Pix pixd = pixs.BilateralGrayExact(spatial, Pix.MakeRangeKernel(20.0f));
        Assert.NotSame(pixs, pixd);
        AssertSame(pixs, pixd);
    }

    [Fact]
    public void BilateralGrayExact_InvalidArguments()
    {
        Pix pixs = Noisy(30, 30, 3);
        L_Kernel spatial = L_Kernel.MakeGaussianKernel(2, 2, 1.0f, 1.0f);
        Assert.Throws<ArgumentException>(() => Pix.Create(30, 30, 32).BilateralGrayExact(spatial, null));
        Assert.Throws<ArgumentException>(() => pixs.BilateralGrayExact(spatial, spatial));
    }

    [Fact]
    public void BilateralExact_RgbIsFilteredPerChannel()
    {
        Pix r = Noisy(20, 20, 1), g = Noisy(20, 20, 2), b = Noisy(20, 20, 3);
        Pix rgb = Pix.CreateRGBImage(r, g, b);
        L_Kernel spatial = L_Kernel.MakeGaussianKernel(2, 2, 1.0f, 1.0f);
        L_Kernel range = Pix.MakeRangeKernel(30.0f);
        Pix expected = Pix.CreateRGBImage(r.BilateralGrayExact(spatial, range),
                                          g.BilateralGrayExact(spatial, range),
                                          b.BilateralGrayExact(spatial, range));
        AssertSame(expected, rgb.BilateralExact(spatial, range));
        Assert.Throws<ArgumentException>(() => Pix.Create(20, 20, 16).BilateralExact(spatial, range));
    }

    [Fact]
    public void BlockBilateralExact_InvalidArguments()
    {
        Pix pixs = Noisy(30, 30, 3);
        Assert.Throws<ArgumentException>(() => pixs.BlockBilateralExact(0.0f, 20.0f));
        Assert.Throws<ArgumentException>(() => pixs.BlockBilateralExact(2.0f, 0.0f));
        Assert.Throws<ArgumentException>(() => Pix.Create(30, 30, 1).BlockBilateralExact(2.0f, 20.0f));
    }

    [Fact]
    public void BlockBilateralExact_PreservesStepEdge()
    {
        Pix pixs = Step(40, 24, 50, 200);
        Pix pixd = pixs.BlockBilateralExact(2.0f, 10.0f);
        for (int y = 0; y < 24; y++) {
            for (int x = 0; x < 40; x++) {
                uint v = Naive.GetPixel(pixd, x, y);
                // The range kernel at a difference of 150 with stdev 10 is exp(-112): no mixing at all.
                Assert.Equal(Naive.GetPixel(pixs, x, y), v);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void BilateralGray_ConstantImageIsUnchanged(int reduction)
    {
        Pix pixs = Make(64, 48, (x, y) => 100);
        Pix pixd = pixs.BilateralGray(4.0f, 30.0f, 6, reduction);
        AssertSame(pixs, pixd);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void BilateralGray_StepEdgeStaysSharp(int reduction)
    {
        Pix pixs = Step(67, 51, 40, 210);
        Pix pixd = pixs.BilateralGray(4.0f, 20.0f, 8, reduction);
        Assert.Equal(pixs.Width, pixd.Width);
        Assert.Equal(pixs.Height, pixd.Height);
        for (int y = 0; y < 51; y++) {
            for (int x = 0; x < 67; x++) {
                uint v = Naive.GetPixel(pixd, x, y);
                if (x < 67 / 2)
                    Assert.True(v < 125, $"({x},{y}) = {v}");
                else
                    Assert.True(v > 125, $"({x},{y}) = {v}");
            }
        }
    }

    [Fact]
    public void BilateralGray_SmoothsNoise()
    {
        var rnd = new Random(5);
        Pix pixs = Make(64, 64, (x, y) => (uint)(100 + rnd.Next(20)));
        Pix pixd = pixs.BilateralGray(3.0f, 50.0f, 6, 1);
        static double Variance(Pix pix)
        {
            double sum = 0, sum2 = 0;
            int n = pix.Width * pix.Height;
            for (int y = 0; y < pix.Height; y++)
                for (int x = 0; x < pix.Width; x++) {
                    double v = Naive.GetPixel(pix, x, y);
                    sum += v;
                    sum2 += v * v;
                }
            return sum2 / n - (sum / n) * (sum / n);
        }
        Assert.True(Variance(pixd) < Variance(pixs) / 4);
    }

    [Fact]
    public void Bilateral_RgbIsFilteredPerChannel()
    {
        Pix r = Noisy(60, 50, 1), g = Noisy(60, 50, 2), b = Noisy(60, 50, 3);
        Pix rgb = Pix.CreateRGBImage(r, g, b);
        Pix expected = Pix.CreateRGBImage(r.BilateralGray(3.0f, 40.0f, 5, 2),
                                          g.BilateralGray(3.0f, 40.0f, 5, 2),
                                          b.BilateralGray(3.0f, 40.0f, 5, 2));
        AssertSame(expected, rgb.Bilateral(3.0f, 40.0f, 5, 2));
        AssertSame(r.BilateralGray(3.0f, 40.0f, 5, 2), r.Bilateral(3.0f, 40.0f, 5, 2));
    }

    [Fact]
    public void Bilateral_SmallImageReturnsCopy()
    {
        Pix pixs = Noisy(10, 10, 4);
        Pix pixd = pixs.Bilateral(8.0f, 40.0f, 5, 1);  // filtersize 17 > w / 2
        Assert.NotSame(pixs, pixd);
        AssertSame(pixs, pixd);
    }

    [Fact]
    public void Bilateral_InvalidArguments()
    {
        Pix pixs = Noisy(80, 80, 4);
        Assert.Throws<ArgumentException>(() => pixs.Bilateral(3.0f, 40.0f, 5, 3));       // reduction
        Assert.Throws<ArgumentException>(() => pixs.Bilateral(3.0f, 40.0f, 3, 1));       // ncomps < 4
        Assert.Throws<ArgumentException>(() => pixs.Bilateral(3.0f, 40.0f, 31, 1));      // ncomps > 30
        Assert.Throws<ArgumentException>(() => pixs.Bilateral(3.0f, 5.0f, 20, 1));       // range_stdev <= 5
        Assert.Throws<ArgumentException>(() => pixs.Bilateral(3.0f, 20.0f, 4, 1));       // ncomps * range < 100
        Assert.Throws<ArgumentException>(() => pixs.Bilateral(1.5f, 40.0f, 5, 4));       // sstdev < 0.5
        Assert.Throws<ArgumentException>(() => Pix.Create(80, 80, 1).Bilateral(3.0f, 40.0f, 5, 1));
        Assert.Throws<ArgumentException>(() => Pix.Create(80, 80, 32).BilateralGray(3.0f, 40.0f, 5, 1));
    }
}
