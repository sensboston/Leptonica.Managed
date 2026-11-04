using System;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.RotateTests;

/// <summary>Tests of skew.c: hand-computed scores and angle recovery on synthetic text-like images.</summary>
public class SkewTests
{
    /// <summary>Horizontal "text lines": bars 6 pixels high every 20 rows, broken into words.</summary>
    private static Pix TextLines(int w, int h, int seed)
    {
        var rnd = new Random(seed);
        Pix pix = Pix.Create(w, h, 1);
        for (int y0 = 20; y0 + 6 < h - 20; y0 += 20) {
            int x = 25;
            while (x < w - 25) {
                int len = 15 + rnd.Next(40);
                for (int y = y0; y < y0 + 6; y++)
                    for (int xx = x; xx < Math.Min(x + len, w - 25); xx++)
                        Naive.SetPixel(pix, xx, y, 1);
                x += len + 8 + rnd.Next(8);
            }
        }
        return pix;
    }

    /// <summary>Full-width horizontal bars: exactly straight.</summary>
    private static Pix Bars(int w, int h)
    {
        Pix pix = Pix.Create(w, h, 1);
        for (int y0 = 20; y0 + 6 < h - 20; y0 += 20)
            for (int y = y0; y < y0 + 6; y++)
                for (int x = 25; x < w - 25; x++)
                    Naive.SetPixel(pix, x, y, 1);
        return pix;
    }

    private static Pix Skew(Pix pix, float degrees) =>
        Pix.VShearCorner(null, pix, degrees * (float)(Math.PI / 180.0), L.L_BRING_IN_WHITE);

    /// <summary>Rows with counts[i] fg pixels, starting at x = 0.</summary>
    private static Pix FromRowCounts(int w, int[] counts)
    {
        Pix pix = Pix.Create(w, counts.Length, 1);
        for (int y = 0; y < counts.Length; y++)
            for (int x = 0; x < counts[y]; x++)
                Naive.SetPixel(pix, x, y, 1);
        return pix;
    }

    [Fact]
    public void FindDifferentialSquareSum_SkipsOneLineAtTopAndBottom()
    {
        // w = 8: skiph = (int)(0.4) = 0, skip = 0, nskip = max(0, 1) = 1; rows 1..8 are summed.
        Pix pix = FromRowCounts(8, new[] { 0, 0, 3, 3, 0, 5, 5, 5, 0, 0 });
        Assert.Equal(9f + 0f + 9f + 25f + 0f + 0f + 25f, pix.FindDifferentialSquareSum());
    }

    [Fact]
    public void FindDifferentialSquareSum_SkipsFractionOfImage()
    {
        // w = 100, h = 40: skiph = 5, skip = min(4, 5) = 4, nskip = 2; rows 2..37 are summed.
        var counts = new int[40];
        for (int i = 0; i < 40; i++)
            counts[i] = (i * 7) % 23;
        Pix pix = FromRowCounts(100, counts);
        float expected = 0.0f;
        for (int i = 2; i <= 37; i++) {
            float d = counts[i] - counts[i - 1];
            expected += d * d;
        }
        Assert.Equal(expected, pix.FindDifferentialSquareSum());
    }

    [Fact]
    public void FindNormalizedSquareSum_UniformAndEmpty()
    {
        // Columns 0..3 ON in all 4 rows of an 8 x 4 image.
        Pix pix = FromRowCounts(8, new[] { 4, 4, 4, 4 });
        Assert.True(pix.FindNormalizedSquareSum(out float h, out float v, out float fract));
        Assert.Equal(1.0f, h);    // every row has the same count
        Assert.Equal(2.0f, v);    // columns: 4 4 4 4 0 0 0 0 -> 64 / (256 / 8)
        Assert.Equal(0.5f, fract);

        Pix empty = Pix.Create(8, 4, 1);
        Assert.False(empty.FindNormalizedSquareSum(out h, out v, out fract));
        Assert.Equal(0.0f, h);
        Assert.Equal(0.0f, v);
        Assert.Equal(0.0f, fract);
        Assert.Throws<ArgumentException>(() => Pix.Create(8, 4, 8).FindNormalizedSquareSum(out _, out _, out _));
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(1.0f)]
    [InlineData(-1.5f)]
    public void FindSkew_RecoversShearAngle(float skewDegrees)
    {
        Pix straight = TextLines(500, 360, 1);
        Pix skewed = Skew(straight, skewDegrees);
        Assert.True(skewed.FindSkew(out float angle, out float conf));
        // The angle returned is the one required for deskew: the negative of the skew.
        Assert.InRange(angle, -skewDegrees - 0.15f, -skewDegrees + 0.15f);
        Assert.True(conf > 0.0f);
    }

    [Fact]
    public void FindSkewSweep_RecoversShearAngle()
    {
        Pix skewed = Skew(TextLines(500, 360, 2), 2.0f);
        Assert.True(skewed.FindSkewSweep(out float angle, 2, 5.0f, 0.5f));
        Assert.InRange(angle, -2.3f, -1.7f);
    }

    [Theory]
    [InlineData(L.L_SHEAR_ABOUT_CORNER)]
    [InlineData(L.L_SHEAR_ABOUT_CENTER)]
    public void FindSkewSweepAndSearchScorePivot_ReturnsScore(int pivot)
    {
        Pix skewed = Skew(TextLines(500, 360, 3), 1.0f);
        Assert.True(skewed.FindSkewSweepAndSearchScorePivot(out float angle, out float conf, out float score,
                                                            4, 2, 0.0f, 7.0f, 1.0f, 0.01f, pivot));
        Assert.InRange(angle, -1.15f, -0.85f);
        Assert.True(score > 0.0f);
        Assert.True(conf > 0.0f);
    }

    [Fact]
    public void FindSkew_EmptyImageIsNotValid()
    {
        Pix empty = Pix.Create(200, 100, 1);
        Assert.False(empty.FindSkew(out float angle, out float conf));
        Assert.Equal(0.0f, angle);
        Assert.Equal(0.0f, conf);
        Assert.False(empty.FindSkewSweep(out angle, 1, 5.0f, 1.0f));
    }

    [Fact]
    public void FindSkew_InvalidArguments()
    {
        Pix pix = TextLines(200, 100, 4);
        Assert.Throws<ArgumentException>(() => Pix.Create(100, 100, 8).FindSkew(out _, out _));
        Assert.Throws<ArgumentException>(() =>
            pix.FindSkewSweepAndSearch(out _, out _, 3, 1, 7.0f, 1.0f, 0.01f));
        Assert.Throws<ArgumentException>(() =>
            pix.FindSkewSweepAndSearch(out _, out _, 4, 8, 7.0f, 1.0f, 0.01f));   // redsearch > redsweep
        Assert.Throws<ArgumentException>(() =>
            pix.FindSkewSweepAndSearchScorePivot(out _, out _, out _, 4, 2, 0.0f, 7.0f, 1.0f, 0.01f, 0));
        Assert.Throws<ArgumentException>(() => pix.FindSkewSweep(out _, 3, 5.0f, 1.0f));
    }

    [Fact]
    public void FindSkewSweepAndSearch_MaxAtSweepEdgeGivesZero()
    {
        // The skew of 6 degrees lies at the edge of a sweep over [-2, 2].
        Pix skewed = Skew(TextLines(500, 360, 5), 6.0f);
        Assert.True(skewed.FindSkewSweepAndSearch(out float angle, out float conf, 4, 2, 2.0f, 1.0f, 0.01f));
        Assert.Equal(0.0f, angle);
        Assert.Equal(0.0f, conf);
    }

    [Fact]
    public void Deskew_ReturnsInputWhenAngleIsSmallOrNotConfident()
    {
        // Empty image: the angle measurement is not valid.
        Pix empty = Pix.Create(300, 200, 1);
        Assert.Same(empty, empty.Deskew(0));
        Pix pixe = empty.FindSkewAndDeskew(2, out float angle, out float conf);
        Assert.Same(empty, pixe);
        Assert.Equal(0.0f, angle);
        Assert.Equal(0.0f, conf);

        // The rule: rotate only if |angle| >= 0.1 degree and conf >= 3.0.
        var rnd = new Random(12);
        Pix noise = Pix.Create(300, 200, 1);
        for (int y = 0; y < 200; y++)
            for (int x = 0; x < 300; x++)
                if (rnd.Next(2) == 0)
                    Naive.SetPixel(noise, x, y, 1);
        foreach (Pix pix in new[] { Bars(500, 360), noise }) {
            Pix pixd = pix.FindSkewAndDeskew(2, out angle, out conf);
            if (Math.Abs(angle) < 0.1f || conf < 3.0f)
                Assert.Same(pix, pixd);
            else
                Assert.NotSame(pix, pixd);
        }
    }

    [Fact]
    public void Deskew_RemovesSkew()
    {
        Pix skewed = Skew(TextLines(500, 360, 7), 1.5f);
        Pix pixd = skewed.FindSkewAndDeskew(2, out float angle, out float conf);
        Assert.InRange(angle, -1.65f, -1.35f);
        Assert.True(conf >= 3.0f);
        Assert.NotSame(skewed, pixd);
        Assert.True(pixd.FindSkew(out float angle2, out _));
        Assert.True(Math.Abs(angle2) < 0.25f, $"remaining angle {angle2}");
    }

    [Fact]
    public void DeskewGeneral_GrayInputAndInvalidArguments()
    {
        Pix skewed = Skew(TextLines(500, 360, 8), 1.5f);
        Pix gray = skewed.ConvertTo8(false);
        Pix pixd = gray.DeskewGeneral(0, 0.0f, 0.0f, 0, 0, out float angle, out _);
        Assert.InRange(angle, -1.65f, -1.35f);
        Assert.Equal(8, pixd.Depth);
        Assert.Throws<ArgumentException>(() => gray.DeskewGeneral(3, 0.0f, 0.0f, 0, 0, out _, out _));
        Assert.Throws<ArgumentException>(() => gray.DeskewGeneral(0, 0.0f, 0.0f, 3, 0, out _, out _));
        Assert.Throws<ArgumentException>(() => gray.Deskew(8));
        Assert.Throws<ArgumentException>(() => gray.DeskewBoth(3));
        // redsearch above redsweep is a failed angle measurement in C: the input comes back.
        Assert.Same(gray, gray.DeskewGeneral(1, 0.0f, 0.0f, 2, 0, out angle, out float conf));
        Assert.Equal(0.0f, angle);
        Assert.Equal(0.0f, conf);
    }

    [Fact]
    public void DeskewBoth_KeepsImageSize()
    {
        Pix straight = Bars(500, 360);
        Pix pixd = straight.DeskewBoth(0);
        Assert.Equal(straight.Width, pixd.Width);
        Assert.Equal(straight.Height, pixd.Height);
    }

    [Fact]
    public void FindSkewOrthogonalRange_FindsPortraitAndLandscape()
    {
        Pix portrait = TextLines(500, 360, 10);
        portrait.FindSkewOrthogonalRange(out float angle, out float conf, 4, 2, 5.0f, 1.0f, 0.01f, 0.0f);
        Assert.InRange(angle, -0.15f, 0.15f);
        Assert.True(conf > 0.0f);

        Pix landscape = portrait.RotateOrth(1);
        landscape.FindSkewOrthogonalRange(out angle, out conf, 4, 2, 5.0f, 1.0f, 0.01f, 0.0f);
        Assert.InRange(angle, -90.15f, -89.85f);
        Assert.True(conf > 0.0f);

        Assert.Throws<ArgumentException>(() =>
            Pix.Create(10, 10, 8).FindSkewOrthogonalRange(out _, out _, 4, 2, 5.0f, 1.0f, 0.01f, 0.0f));
    }
}
