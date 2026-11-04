using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests;

/// <summary>Tests for correlscore.c: the optimized correlators must give the same result as the simple one.</summary>
public class CorrelScoreTests
{
    private static Pix RandomPix(Random rng, int w, int h, double density)
    {
        Pix pix = Pix.Create(w, h, 1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (rng.NextDouble() < density)
                    pix.SetPixel(x, y, 1);
        return pix;
    }

    /// <summary>downcount[y] = number of ON pixels in the rows below row y.</summary>
    private static int[] DownCount(Pix pix, int[] tab)
    {
        Numa na = pix.CountPixelsByRow(tab);
        int h = pix.Height;
        int[] down = new int[h];
        int sum = 0;
        for (int y = h - 1; y >= 0; y--) {
            down[y] = sum;
            sum += na.GetIValue(y);
        }
        return down;
    }

    [Fact]
    public void OptimizedScoreEqualsSimpleScoreForAllShifts()
    {
        var rng = new Random(12345);
        int[] tab = Pix.MakePixelSumTab8();
        int ncases = 0;
        for (int iter = 0; iter < 400; iter++) {
            int w1 = rng.Next(8, 110), h1 = rng.Next(5, 20);
            int w2 = Math.Max(1, w1 + rng.Next(-5, 6)), h2 = Math.Max(1, h1 + rng.Next(-3, 4));
            Pix pix1 = RandomPix(rng, w1, h1, 0.5);
            Pix pix2 = RandomPix(rng, w2, h2, 0.5);
            int area1 = pix1.CountPixels(tab);
            int area2 = pix2.CountPixels(tab);
            if (area1 == 0 || area2 == 0)
                continue;
            float delx = (float)(rng.NextDouble() * 150 - 75);   /* includes |delx| >= 32 */
            float dely = (float)(rng.NextDouble() * 12 - 6);

            float simple = pix1.CorrelationScoreSimple(pix2, area1, area2, delx, dely, 1000, 1000, tab);
            float fast = pix1.CorrelationScore(pix2, area1, area2, delx, dely, 1000, 1000, tab);
            Assert.Equal(simple, fast);
            ncases++;
        }
        Assert.True(ncases > 300);
    }

    [Fact]
    public void ThresholdedScoreAgreesWithTheScore()
    {
        var rng = new Random(777);
        int[] tab = Pix.MakePixelSumTab8();
        for (int iter = 0; iter < 400; iter++) {
            int w1 = rng.Next(8, 110), h1 = rng.Next(5, 20);
            int w2 = Math.Max(1, w1 + rng.Next(-3, 4)), h2 = Math.Max(1, h1 + rng.Next(-2, 3));
            Pix pix1 = RandomPix(rng, w1, h1, 0.6);
            Pix pix2 = RandomPix(rng, w2, h2, 0.6);
            int area1 = pix1.CountPixels(tab);
            int area2 = pix2.CountPixels(tab);
            if (area1 == 0 || area2 == 0)
                continue;
            float delx = (float)(rng.NextDouble() * 150 - 75);
            float dely = (float)(rng.NextDouble() * 8 - 4);
            int[] down = DownCount(pix1, tab);

            float score = pix1.CorrelationScoreSimple(pix2, area1, area2, delx, dely, 1000, 1000, tab);
            foreach (float thresh in new[] { 0.05f, 0.2f, 0.4f, 0.7f }) {
                bool expected = score >= thresh;
                bool actual = pix1.CorrelationScoreThresholded(pix2, area1, area2, delx, dely,
                                                               1000, 1000, tab, down, thresh);
                Assert.True(expected == actual, $"iter {iter} thresh {thresh} score {score}");
            }
        }
    }

    [Fact]
    public void ShiftedScoreOfIdenticalImagesIsOneWithoutShift()
    {
        var rng = new Random(5);
        int[] tab = Pix.MakePixelSumTab8();
        Pix pix = RandomPix(rng, 40, 30, 0.4);
        int area = pix.CountPixels(tab);
        Assert.Equal(1.0f, pix.CorrelationScoreShifted(pix, area, area, 0, 0, tab));
        Assert.True(pix.CorrelationScoreShifted(pix, area, area, 3, 2, tab) < 1.0f);
        /* the shifted score is the same as the simple score for the same integer shift */
        Pix pix2 = RandomPix(rng, 38, 29, 0.4);
        int area2 = pix2.CountPixels(tab);
        Assert.Equal(pix.CorrelationScoreSimple(pix2, area, area2, 4.0f, -3.0f, 100, 100, tab),
                     pix.CorrelationScoreShifted(pix2, area, area2, 4, -3, tab));
    }

    [Fact]
    public void SizeDifferenceAndArgumentChecks()
    {
        int[] tab = Pix.MakePixelSumTab8();
        Pix pix1 = Pix.Create(40, 20, 1);
        Pix pix2 = Pix.Create(50, 20, 1);
        Assert.Equal(0.0f, pix1.CorrelationScore(pix2, 10, 10, 0.0f, 0.0f, 2, 2, tab));
        Assert.Equal(0.0f, pix1.CorrelationScoreSimple(pix2, 10, 10, 0.0f, 0.0f, 2, 2, tab));
        Assert.False(pix1.CorrelationScoreThresholded(pix2, 10, 10, 0.0f, 0.0f, 2, 2, tab, new int[20], 0.5f));
        Assert.Throws<ArgumentException>(() => pix1.CorrelationScore(pix2, 0, 10, 0.0f, 0.0f, 20, 20, tab));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 8).CorrelationScore(pix2, 1, 1, 0.0f, 0.0f, 20, 20, tab));
        Assert.Throws<ArgumentException>(() => pix1.CorrelationScoreShifted(pix2, 0, 10, 0, 0, tab));
    }
}
