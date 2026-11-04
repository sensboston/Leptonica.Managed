using System;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.Binarize;

/// <summary>
/// Tests of binarize.c.  The Sauvola threshold step is checked against a direct computation on
/// small images, with the mean and mean-square maps built here by plain loops (the windowed
/// convolutions of convolve.c are not ported yet).  The functions that call into not yet ported
/// files (pixtiling.c, convolve.c, adaptmap.c, conncomp.c) are covered only by their argument
/// checks, which run before the first such call.
/// </summary>
public class BinarizeTests
{
    private static Pix MakeGray(int w, int h, Func<int, int, int> f)
    {
        Pix pix = Pix.Create(w, h, 8);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Naive.SetPixel(pix, j, i, (uint)f(j, i));
        return pix;
    }

    private static Pix MakeConst(int w, int h, int d, uint val)
    {
        Pix pix = Pix.Create(w, h, d);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Naive.SetPixel(pix, j, i, val);
        return pix;
    }

    /// <summary>
    /// Direct window statistics over (2 * wh + 1)^2, clipped at the image edge.  The mean is
    /// floored, so mv * mv never exceeds the (rounded) mean square.
    /// </summary>
    private static void DirectMaps(Pix src, int wh, out Pix pixm, out Pix pixms)
    {
        src.GetDimensions(out int w, out int h, out _);
        pixm = Pix.Create(w, h, 8);
        pixms = Pix.Create(w, h, 32);
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                long sum = 0, sumsq = 0, n = 0;
                for (int y = Math.Max(0, i - wh); y <= Math.Min(h - 1, i + wh); y++) {
                    for (int x = Math.Max(0, j - wh); x <= Math.Min(w - 1, j + wh); x++) {
                        long v = Naive.GetPixel(src, x, y);
                        sum += v;
                        sumsq += v * v;
                        n++;
                    }
                }
                Naive.SetPixel(pixm, j, i, (uint)(sum / n));
                Naive.SetPixel(pixms, j, i, (uint)((sumsq + n / 2) / n));
            }
        }
    }

    private static void CheckSauvolaMaps(Pix pixm, Pix pixms, float factor)
    {
        Pix pixth = Pix.SauvolaGetThreshold(pixm, pixms, factor, true, out Pix? pixsd);
        Assert.NotNull(pixsd);
        pixm.GetDimensions(out int w, out int h, out _);
        Assert.Equal(w, pixth.Width);
        Assert.Equal(h, pixth.Height);
        Assert.Equal(8, pixth.Depth);
        Assert.Equal(w, pixsd!.Width);
        Assert.Equal(h, pixsd.Height);
        Assert.Equal(8, pixsd.Depth);
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                int mv = (int)Naive.GetPixel(pixm, j, i);
                int ms = (int)Naive.GetPixel(pixms, j, i);
                int var = ms - mv * mv;
                Assert.True(var >= 0);
                float sd = (float)Math.Sqrt(var);
                int expectedSd = (int)sd;
                int expectedTh = (int)(mv * (1.0 - factor * (1.0 - sd / 128.0)));
                Assert.True(expectedTh >= 0 && expectedTh <= 255);
                Assert.Equal((uint)expectedSd, Naive.GetPixel(pixsd, j, i));
                Assert.Equal((uint)expectedTh, Naive.GetPixel(pixth, j, i));
            }
        }
    }

    [Theory]
    [InlineData(2, 0.35f)]
    [InlineData(3, 0.2f)]
    [InlineData(2, 0.0f)]
    public void SauvolaGetThreshold_MatchesDirectComputation(int wh, float factor)
    {
        var rng = new Random(1234);
        Pix src = MakeGray(13, 11, (x, y) => rng.Next(256));
        DirectMaps(src, wh, out Pix pixm, out Pix pixms);
        CheckSauvolaMaps(pixm, pixms, factor);
    }

    [Fact]
    public void SauvolaGetThreshold_LargeImageUsesSqrtTable()
    {
        // 400 x 260 > 100000 pixels: the table of 2^16 square roots is used.
        var rng = new Random(99);
        Pix src = MakeGray(400, 260, (x, y) => (x / 8 + y / 8) % 2 == 0 ? rng.Next(100) : 155 + rng.Next(100));
        DirectMaps(src, 3, out Pix pixm, out Pix pixms);
        CheckSauvolaMaps(pixm, pixms, 0.35f);
    }

    [Theory]
    [InlineData(0.2f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void SauvolaGetThreshold_HalfBlackHalfWhiteWindow(float factor)
    {
        // Window with half the samples 0 and half 255: mean 127 (floored), mean square
        // 32513 (rounded), variance 16384, sd 128, so the threshold equals the mean
        // for every factor.
        Pix pixm = MakeConst(5, 4, 8, 127);
        Pix pixms = MakeConst(5, 4, 32, 32513);
        Pix pixth = Pix.SauvolaGetThreshold(pixm, pixms, factor, true, out Pix? pixsd);
        for (int i = 0; i < 4; i++) {
            for (int j = 0; j < 5; j++) {
                Assert.Equal(128u, Naive.GetPixel(pixsd!, j, i));
                Assert.Equal(127u, Naive.GetPixel(pixth, j, i));
            }
        }
    }

    [Theory]
    [InlineData(0.0f, 100u)]
    [InlineData(0.5f, 50u)]
    [InlineData(1.0f, 0u)]
    public void SauvolaGetThreshold_UniformWindow(float factor, uint expected)
    {
        // Zero variance: t = m * (1 - k).
        Pix pixm = MakeConst(6, 3, 8, 100);
        Pix pixms = MakeConst(6, 3, 32, 10000);
        Pix pixth = Pix.SauvolaGetThreshold(pixm, pixms, factor, true, out Pix? pixsd);
        for (int i = 0; i < 3; i++) {
            for (int j = 0; j < 6; j++) {
                Assert.Equal(0u, Naive.GetPixel(pixsd!, j, i));
                Assert.Equal(expected, Naive.GetPixel(pixth, j, i));
            }
        }
    }

    [Fact]
    public void SauvolaGetThreshold_NoStdDevOutputWhenNotWanted()
    {
        Pix pixm = MakeConst(6, 3, 8, 100);
        Pix pixms = MakeConst(6, 3, 32, 10000);
        Pix pixth = Pix.SauvolaGetThreshold(pixm, pixms, 0.5f, false, out Pix? pixsd);
        Assert.Null(pixsd);
        Assert.Equal(50u, Naive.GetPixel(pixth, 2, 1));
    }

    [Fact]
    public void SauvolaGetThreshold_RejectsBadInput()
    {
        Pix pixm = MakeConst(4, 4, 8, 10);
        Pix pixms = MakeConst(4, 4, 32, 100);
        Assert.Throws<ArgumentException>(() => Pix.SauvolaGetThreshold(MakeConst(4, 4, 1, 0), pixms, 0.3f, false, out _));
        Assert.Throws<ArgumentException>(() => Pix.SauvolaGetThreshold(pixm, MakeConst(4, 4, 8, 0), 0.3f, false, out _));
        Assert.Throws<ArgumentException>(() => Pix.SauvolaGetThreshold(pixm, pixms, -0.1f, false, out _));
    }

    [Fact]
    public void ApplyLocalThreshold_SetsBitWhereValueIsBelowThreshold()
    {
        Pix pixs = MakeGray(4, 2, (x, y) => new[] { 10, 50, 50, 200, 0, 255, 99, 100 }[y * 4 + x]);
        Pix pixth = MakeGray(4, 2, (x, y) => new[] { 11, 50, 51, 100, 0, 255, 100, 100 }[y * 4 + x]);
        Pix pixd = Pix.ApplyLocalThreshold(pixs, pixth);
        Assert.Equal(1, pixd.Depth);
        Assert.Equal(4, pixd.Width);
        Assert.Equal(2, pixd.Height);
        uint[] expected = { 1, 0, 1, 0, 0, 0, 1, 0 };
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 4; j++)
                Assert.Equal(expected[i * 4 + j], Naive.GetPixel(pixd, j, i));
    }

    [Fact]
    public void ApplyLocalThreshold_RejectsBadInput()
    {
        Pix pix8 = MakeConst(4, 4, 8, 0);
        Assert.Throws<ArgumentException>(() => Pix.ApplyLocalThreshold(MakeConst(4, 4, 1, 0), pix8));
        Assert.Throws<ArgumentException>(() => Pix.ApplyLocalThreshold(pix8, MakeConst(4, 4, 1, 0)));
    }

    [Fact]
    public void ThresholdByHisto_SplitsTwoGrayLevels()
    {
        // Dark text level 40 on a background of level 200.
        Pix pixs = MakeGray(64, 64, (x, y) => x < 24 ? 40 : 200);
        bool ok = pixs.ThresholdByHisto(1, 0, 0, out int thresh, out Pix? pixd,
                                        out Numa? nahisto, false, out Pix? pixhisto);
        Assert.True(ok);
        Assert.InRange(thresh, 41, 200);
        Assert.Null(pixhisto);
        Assert.NotNull(nahisto);
        Assert.Equal(256, nahisto!.Count);
        nahisto.GetMax(out float maxval, out _);
        Assert.Equal(1.0f, maxval, 5);

        Assert.NotNull(pixd);
        Assert.Equal(1, pixd!.Depth);
        for (int i = 0; i < 64; i++)
            for (int j = 0; j < 64; j++)
                Assert.Equal(j < 24 ? 1u : 0u, Naive.GetPixel(pixd, j, i));
    }

    [Fact]
    public void ThresholdByHisto_SubsampledGivesSameSplit()
    {
        Pix pixs = MakeGray(64, 64, (x, y) => x < 24 ? 40 : 200);
        Assert.True(pixs.ThresholdByHisto(2, 20, 20, out int thresh, out Pix? pixd, out _, false, out _));
        Assert.InRange(thresh, 41, 200);
        Assert.Equal(1u, Naive.GetPixel(pixd!, 0, 0));
        Assert.Equal(0u, Naive.GetPixel(pixd!, 63, 63));
    }

    [Fact]
    public void ThresholdByHisto_RejectsBadInput()
    {
        Assert.Throws<ArgumentException>(() => MakeConst(8, 8, 1, 0).ThresholdByHisto(1, 0, 0, out _, out _, out _, false, out _));
        Assert.Throws<ArgumentException>(() => MakeConst(8, 8, 8, 0).ThresholdByHisto(0, 0, 0, out _, out _, out _, false, out _));
        Pix cmapped = MakeConst(8, 8, 8, 0);
        cmapped.SetColormap(PixColormap.CreateLinear(8, 256));
        Assert.Throws<ArgumentException>(() => cmapped.ThresholdByHisto(1, 0, 0, out _, out _, out _, false, out _));
    }

    [Fact]
    public void OtsuAdaptiveThreshold_RejectsBadInput()
    {
        Assert.Throws<ArgumentException>(() => MakeConst(64, 64, 1, 0).OtsuAdaptiveThreshold(32, 32, 0, 0, 0.1f, out _, out _));
        Assert.Throws<ArgumentException>(() => MakeConst(64, 64, 8, 0).OtsuAdaptiveThreshold(15, 32, 0, 0, 0.1f, out _, out _));
        Assert.Throws<ArgumentException>(() => MakeConst(64, 64, 8, 0).OtsuAdaptiveThreshold(32, 15, 0, 0, 0.1f, out _, out _));
    }

    [Fact]
    public void OtsuThreshOnBackgroundNorm_RejectsBadInput()
    {
        Assert.Throws<ArgumentException>(() => MakeConst(64, 64, 32, 0).OtsuThreshOnBackgroundNorm(null, 10, 15, 100, 50, 255, 2, 2, 0.1f, out _));
        Assert.Throws<ArgumentException>(() => MakeConst(64, 64, 8, 0).OtsuThreshOnBackgroundNorm(null, 3, 15, 100, 50, 255, 2, 2, 0.1f, out _));
        Assert.Throws<ArgumentException>(() => MakeConst(64, 64, 8, 0).OtsuThreshOnBackgroundNorm(null, 10, 3, 100, 50, 255, 2, 2, 0.1f, out _));
    }

    [Fact]
    public void MaskedThreshOnBackgroundNorm_RejectsBadInput()
    {
        Assert.Throws<ArgumentException>(() => MakeConst(64, 64, 32, 0).MaskedThreshOnBackgroundNorm(null, 10, 15, 100, 50, 2, 2, 0.1f, out _));
        Assert.Throws<ArgumentException>(() => MakeConst(64, 64, 8, 0).MaskedThreshOnBackgroundNorm(null, 3, 15, 100, 50, 2, 2, 0.1f, out _));
    }

    [Fact]
    public void SauvolaBinarize_RejectsBadInput()
    {
        Pix pix = MakeConst(40, 40, 8, 128);
        Assert.Throws<ArgumentException>(() => MakeConst(40, 40, 1, 0).SauvolaBinarize(7, 0.35f, 1, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => pix.SauvolaBinarize(1, 0.35f, 1, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => pix.SauvolaBinarize(19, 0.35f, 1, out _, out _, out _, out _));  // 40 < 2 * 19 + 3
        Assert.Throws<ArgumentException>(() => pix.SauvolaBinarize(7, -0.5f, 1, out _, out _, out _, out _));
    }

    [Fact]
    public void SauvolaBinarizeTiled_RejectsBadInput()
    {
        Pix pix = MakeConst(40, 40, 8, 128);
        Assert.Throws<ArgumentException>(() => MakeConst(40, 40, 1, 0).SauvolaBinarizeTiled(7, 0.35f, 2, 2, out _, out _));
        Assert.Throws<ArgumentException>(() => pix.SauvolaBinarizeTiled(1, 0.35f, 2, 2, out _, out _));
        Assert.Throws<ArgumentException>(() => pix.SauvolaBinarizeTiled(19, 0.35f, 2, 2, out _, out _));
        Assert.Throws<ArgumentException>(() => pix.SauvolaBinarizeTiled(7, -0.5f, 2, 2, out _, out _));
    }

    [Fact]
    public void SauvolaOnContrastNormAndThreshOnDoubleNorm_RejectLowDepth()
    {
        Pix pix1 = MakeConst(40, 40, 1, 0);
        Assert.Throws<ArgumentException>(() => pix1.SauvolaOnContrastNorm(130, out _, out _));
        Assert.Throws<ArgumentException>(() => pix1.ThreshOnDoubleNorm(130));
    }

    [Fact]
    public void ThresholdByConnComp_RejectsBadInput()
    {
        Pix pix8 = MakeConst(40, 40, 8, 128);
        Assert.Throws<ArgumentException>(() => MakeConst(40, 40, 1, 0).ThresholdByConnComp(null, 0, 0, 0, 0f, 0f, out _, out _, 0));
        Assert.Throws<ArgumentException>(() => pix8.ThresholdByConnComp(MakeConst(40, 40, 8, 0), 0, 0, 0, 0f, 0f, out _, out _, 0));
        Assert.Throws<ArgumentException>(() => pix8.ThresholdByConnComp(null, 150, 100, 10, 0f, 0f, out _, out _, 0));
    }
}
