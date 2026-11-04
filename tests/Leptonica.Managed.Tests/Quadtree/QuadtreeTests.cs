using System;
using Leptonica;

namespace Leptonica.Managed.Tests;

/// <summary>Tests of quadtree.c against direct computation over the regions.</summary>
public class QuadtreeTests
{
    private static Pix RandomGray(int seed, int w, int h)
    {
        Random rng = new Random(seed);
        Pix pix = Pix.Create(w, h, 8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pix.SetPixel(x, y, (uint)rng.Next(256));
        return pix;
    }

    private static (double mean, double var) Stats(Pix pix, Box box)
    {
        double sum = 0, sum2 = 0;
        int n = 0;
        for (int y = box.Y; y < box.Y + box.Height; y++)
            for (int x = box.X; x < box.X + box.Width; x++) {
                pix.GetPixel(x, y, out uint v);
                sum += v;
                sum2 += (double)v * v;
                n++;
            }
        double mean = sum / n;
        return (mean, sum2 / n - mean * mean);
    }

    [Theory]
    [InlineData(1, 1, -1)]
    [InlineData(2, 2, 0)]
    [InlineData(3, 3, 1)]
    [InlineData(5, 5, 1)]
    [InlineData(6, 100, 2)]
    [InlineData(100, 100, 6)]
    [InlineData(1000, 1000, 9)]
    public void QuadtreeMaxLevels_KeepsRectanglesNonEmpty(int w, int h, int expected)
    {
        Assert.Equal(expected, Pix.QuadtreeMaxLevels(w, h));
    }

    [Fact]
    public void QuadtreeRegions_TileTheImageInRasterOrder()
    {
        int w = 37, h = 22, nlevels = 4;
        Boxaa baa = Boxaa.QuadtreeRegions(w, h, nlevels);
        Assert.Equal(nlevels, baa.Count);
        for (int k = 0; k < nlevels; k++) {
            Boxa boxa = baa.GetBoxa(k, L.L_CLONE);
            int side = 1 << k;
            Assert.Equal(side * side, boxa.Count);
            Box first = boxa.GetBox(0, L.L_CLONE);
            Box last = boxa.GetBox(boxa.Count - 1, L.L_CLONE);
            Assert.Equal((0, 0), (first.X, first.Y));
            Assert.Equal((w - 1, h - 1), (last.X + last.Width - 1, last.Y + last.Height - 1));
            /* the regions cover every pixel exactly once */
            int[,] cover = new int[w, h];
            for (int i = 0; i < boxa.Count; i++) {
                Box b = boxa.GetBox(i, L.L_CLONE);
                for (int y = b.Y; y < b.Y + b.Height; y++)
                    for (int x = b.X; x < b.X + b.Width; x++)
                        cover[x, y]++;
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    Assert.True(cover[x, y] >= 1);
        }
        Box level0 = baa.GetBoxa(0, L.L_CLONE).GetBox(0, L.L_CLONE);
        Assert.Equal((0, 0, w, h), (level0.X, level0.Y, level0.Width, level0.Height));
        Assert.Throws<ArgumentException>(() => Boxaa.QuadtreeRegions(8, 8, 0));
        Assert.Throws<ArgumentException>(() => Boxaa.QuadtreeRegions(3, 8, 4));
        Assert.Throws<ArgumentException>(() => Boxaa.QuadtreeRegions(8, 3, 4));
    }

    [Fact]
    public void MeanInRectangle_MatchesTheDirectMean()
    {
        Pix pix = RandomGray(1, 40, 30);
        Pix acc = pix.BlockconvAccum();
        foreach (Box box in new[] { Box.Create(0, 0, 40, 30), Box.Create(5, 0, 10, 8), Box.Create(0, 7, 12, 9),
                                    Box.Create(9, 11, 17, 13), Box.Create(30, 20, 30, 30) }) {
            Box clipped = box.ClipToRectangle(40, 30);
            double expected = Stats(pix, clipped).mean;
            Assert.Equal(expected, pix.MeanInRectangle(box, acc), 2);
        }
        Assert.Throws<ArgumentException>(() => pix.MeanInRectangle(Box.Create(100, 100, 5, 5), acc));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 1).MeanInRectangle(Box.Create(0, 0, 2, 2), acc));
    }

    [Fact]
    public void VarianceInRectangle_MatchesTheDirectVariance()
    {
        Pix pix = RandomGray(2, 40, 30);
        Pix acc = pix.BlockconvAccum();
        DPix msa = pix.MeanSquareAccum();
        foreach (Box box in new[] { Box.Create(0, 0, 40, 30), Box.Create(5, 0, 10, 8), Box.Create(0, 7, 12, 9),
                                    Box.Create(9, 11, 17, 13) }) {
            (double mean, double var) = Stats(pix, box);
            pix.VarianceInRectangle(box, acc, msa, out float v, out float rv);
            Assert.Equal(var, v, 1);
            Assert.Equal(Math.Sqrt(var), rv, 2);
        }
    }

    [Fact]
    public void QuadtreeMean_And_Variance_MatchTheRegions()
    {
        Pix pix = RandomGray(3, 41, 29);
        int nlevels = 4;
        FPixa means = pix.QuadtreeMean(nlevels, null);
        pix.QuadtreeVariance(nlevels, null, null, out FPixa vars, out FPixa rvars);
        Assert.Equal(nlevels, means.Count);
        Boxaa baa = Boxaa.QuadtreeRegions(41, 29, nlevels);
        for (int k = 0; k < nlevels; k++) {
            Boxa boxa = baa.GetBoxa(k, L.L_CLONE);
            int side = 1 << k;
            means.GetFPixDimensions(k, out int fw, out int fh);
            Assert.Equal((side, side), (fw, fh));
            for (int j = 0; j < boxa.Count; j++) {
                (double mean, double var) = Stats(pix, boxa.GetBox(j, L.L_CLONE));
                means.GetPixel(k, j % side, j / side, out float m);
                vars.GetPixel(k, j % side, j / side, out float v);
                rvars.GetPixel(k, j % side, j / side, out float rv);
                Assert.Equal(mean, m, 2);
                Assert.Equal(var, v, 0);
                Assert.Equal(Math.Sqrt(var), rv, 1);
            }
        }
        /* accumulators given by the caller */
        FPixa again = pix.QuadtreeMean(nlevels, pix.BlockconvAccum());
        Assert.Equal(means.GetFPix(2, L.L_CLONE).Data, again.GetFPix(2, L.L_CLONE).Data);

        Assert.Throws<ArgumentException>(() => pix.QuadtreeMean(20, null));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 1).QuadtreeMean(2, null));
    }

    [Fact]
    public void QuadtreeGetParent_And_Children_ReadAdjacentLevels()
    {
        Pix pix = RandomGray(4, 33, 33);
        FPixa fpixa = pix.QuadtreeMean(3, null);

        Assert.True(Pix.QuadtreeGetParent(fpixa, 2, 3, 2, out float parent));
        fpixa.GetPixel(1, 1, 1, out float expected);
        Assert.Equal(expected, parent);
        Assert.False(Pix.QuadtreeGetParent(fpixa, 0, 0, 0, out parent));
        Assert.Equal(0.0f, parent);
        Assert.False(Pix.QuadtreeGetParent(fpixa, 3, 0, 0, out _));

        Assert.True(Pix.QuadtreeGetChildren(fpixa, 1, 1, 0, out float v00, out float v10, out float v01, out float v11));
        fpixa.GetPixel(2, 2, 0, out float e00);
        fpixa.GetPixel(2, 3, 0, out float e10);
        fpixa.GetPixel(2, 2, 1, out float e01);
        fpixa.GetPixel(2, 3, 1, out float e11);
        Assert.Equal((e00, e10, e01, e11), (v00, v10, v01, v11));
        Assert.False(Pix.QuadtreeGetChildren(fpixa, 2, 0, 0, out v00, out _, out _, out _));
        Assert.Equal(0.0f, v00);
        Assert.False(Pix.QuadtreeGetChildren(fpixa, 1, 5, 5, out _, out _, out _, out _));
    }
}
