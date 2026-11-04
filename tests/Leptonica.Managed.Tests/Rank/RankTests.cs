using System;
using System.Linq;
using Leptonica;

namespace Leptonica.Managed.Tests.RankTests;

/// <summary>
/// Tests of rank.c.  The two-histogram filter is checked against a direct selection from the
/// sorted window, taken from the mirrored-border image that the filter itself uses.
/// </summary>
public class RankTests
{
    private static Pix RandomGray(int seed, int w, int h)
    {
        var rng = new Random(seed);
        Pix pix = Pix.Create(w, h, 8);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                pix.SetPixel(j, i, (uint)rng.Next(256));
        return pix;
    }

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    /// <summary>The element of index (int)(rank * wf * hf) of the sorted window.</summary>
    private static Pix Reference(Pix pixs, int wf, int hf, float rank)
    {
        pixs.GetDimensions(out int w, out int h, out _);
        Pix pixt = pixs.AddMirroredBorder(wf / 2, wf / 2, hf / 2, hf / 2);
        int rankloc = (int)(rank * wf * hf);
        Pix pixd = Pix.CreateTemplate(pixs);
        var window = new uint[wf * hf];
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                int n = 0;
                for (int k = 0; k < hf; k++)
                    for (int m = 0; m < wf; m++)
                        window[n++] = Get(pixt, j + m, i + k);
                Array.Sort(window);
                pixd.SetPixel(j, i, window[rankloc]);
            }
        }
        return pixd;
    }

    private static void AssertSame(Pix expected, Pix actual)
    {
        Assert.True(Pix.Equal(expected, actual));
    }

    [Theory]
    [InlineData(3, 3, 0.5f)]
    [InlineData(4, 5, 0.3f)]
    [InlineData(5, 2, 0.8f)]
    [InlineData(1, 5, 0.5f)]
    [InlineData(6, 6, 0.25f)]
    [InlineData(2, 2, 0.5f)]
    [InlineData(7, 3, 0.65f)]
    [InlineData(3, 7, 0.4f)]
    public void RankFilterGray_MatchesDirectSelection(int wf, int hf, float rank)
    {
        Pix pixs = RandomGray(wf * 100 + hf, 23, 19);
        AssertSame(Reference(pixs, wf, hf, rank), pixs.RankFilterGray(wf, hf, rank));
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(2, 6)]
    [InlineData(6, 2)]
    public void RankFilterGray_EvenFilterWithExtremeRanks(int wf, int hf)
    {
        Pix pixs = RandomGray(7, 21, 18);
        /* even dimensions: not dispatched to the gray morphology, rank is moved off 0.0 / 1.0 */
        AssertSame(Reference(pixs, wf, hf, 0.0001f), pixs.RankFilterGray(wf, hf, 0.0f));
        AssertSame(Reference(pixs, wf, hf, 0.9999f), pixs.RankFilterGray(wf, hf, 1.0f));
    }

    [Fact]
    public void RankFilterGray_OddFilterWithExtremeRanksIsErodeOrDilate()
    {
        Pix pixs = RandomGray(11, 25, 20);
        AssertSame(pixs.ErodeGray(5, 3), pixs.RankFilterGray(5, 3, 0.0f));
        AssertSame(pixs.DilateGray(5, 3), pixs.RankFilterGray(5, 3, 1.0f));
    }

    [Fact]
    public void RankFilterGray_OneByOneIsACopy()
    {
        Pix pixs = RandomGray(3, 10, 10);
        Pix pixd = pixs.RankFilterGray(1, 1, 0.5f);
        Assert.NotSame(pixs, pixd);
        AssertSame(pixs, pixd);
    }

    [Fact]
    public void RankFilterGray_ConstantImageIsUnchanged()
    {
        Pix pixs = Pix.Create(15, 12, 8);
        for (int i = 0; i < 12; i++)
            for (int j = 0; j < 15; j++)
                pixs.SetPixel(j, i, 97);
        AssertSame(pixs, pixs.RankFilterGray(5, 4, 0.5f));
    }

    [Fact]
    public void MedianFilter_RemovesIsolatedNoise()
    {
        Pix pixs = Pix.Create(15, 15, 8);
        for (int i = 0; i < 15; i++)
            for (int j = 0; j < 15; j++)
                pixs.SetPixel(j, i, 50);
        pixs.SetPixel(7, 7, 255);
        pixs.SetPixel(3, 11, 0);
        Pix pixd = pixs.MedianFilter(3, 3);
        Assert.Equal(50u, Get(pixd, 7, 7));
        Assert.Equal(50u, Get(pixd, 3, 11));
        AssertSame(pixs.RankFilter(3, 3, 0.5f), pixd);
    }

    [Fact]
    public void RankFilter_Rgb_IsPerComponent()
    {
        var rng = new Random(5);
        Pix pixs = Pix.Create(17, 13, 32);
        for (int i = 0; i < 13; i++) {
            for (int j = 0; j < 17; j++) {
                Pix.ComposeRGBPixel(rng.Next(256), rng.Next(256), rng.Next(256), out uint p);
                pixs.SetPixel(j, i, p);
            }
        }
        Pix pixd = pixs.RankFilter(3, 5, 0.6f);
        Assert.Equal(32, pixd.Depth);
        foreach (int comp in new[] { L.COLOR_RED, L.COLOR_GREEN, L.COLOR_BLUE }) {
            Pix expected = pixs.GetRGBComponent(comp).RankFilterGray(3, 5, 0.6f);
            AssertSame(expected, pixd.GetRGBComponent(comp));
        }
        AssertSame(pixd, pixs.RankFilterRGB(3, 5, 0.6f));
    }

    [Fact]
    public void RankFilter_InvalidArguments()
    {
        Pix gray = RandomGray(1, 10, 10);
        Assert.Throws<ArgumentException>(() => gray.RankFilter(0, 3, 0.5f));
        Assert.Throws<ArgumentException>(() => gray.RankFilter(3, 3, 1.5f));
        Assert.Throws<ArgumentException>(() => gray.RankFilter(3, 3, -0.1f));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 1).RankFilter(3, 3, 0.5f));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 32).RankFilterGray(3, 3, 0.5f));
        Assert.Throws<ArgumentException>(() => gray.RankFilterRGB(3, 3, 0.5f));
        PixColormap cmap = PixColormap.CreateLinear(8, 256);
        Pix cm = Pix.Create(10, 10, 8);
        cm.SetColormap(cmap);
        Assert.Throws<ArgumentException>(() => cm.RankFilter(3, 3, 0.5f));
        Assert.Throws<ArgumentException>(() => cm.RankFilterGray(3, 3, 0.5f));
    }

    [Fact]
    public void RankFilterWithScaling_KeepsTheSize()
    {
        Pix pixs = Pix.Create(60, 40, 8);
        for (int i = 0; i < 40; i++)
            for (int j = 0; j < 60; j++)
                pixs.SetPixel(j, i, 120);
        Pix pixd = pixs.RankFilterWithScaling(9, 7, 0.5f, 0.5f);
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((60, 40, 8), (w, h, d));
        Assert.Equal(120u, Get(pixd, 30, 20));
        Assert.Equal(120u, Get(pixd, 0, 0));
    }

    [Fact]
    public void RankFilterWithScaling_BadScaleFactorFallsBackToUnscaled()
    {
        Pix pixs = RandomGray(9, 20, 20);
        AssertSame(pixs.RankFilter(5, 5, 0.5f), pixs.RankFilterWithScaling(5, 5, 0.5f, 0.9f));
        AssertSame(pixs.RankFilter(5, 5, 0.5f), pixs.RankFilterWithScaling(5, 5, 0.5f, 0.1f));
        AssertSame(pixs, pixs.RankFilterWithScaling(1, 1, 0.5f, 0.5f));
    }
}
