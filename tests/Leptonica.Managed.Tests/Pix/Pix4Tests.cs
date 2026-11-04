using System;
using Leptonica;

namespace Leptonica.Managed.Tests.PixCore;

/// <summary>
/// Tests of pix4.c: histograms, averages, ranges and extreme values.  The expected
/// values are computed by hand on small images.
/// </summary>
public class Pix4Tests
{
    /// <summary>Makes a pix from row-major pixel values.</summary>
    private static Pix Make(int w, int h, int d, params uint[] vals)
    {
        Assert.Equal(w * h, vals.Length);
        Pix pix = Pix.Create(w, h, d);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Assert.True(pix.SetPixel(j, i, vals[i * w + j]));
        return pix;
    }

    private static uint Rgb(int r, int g, int b)
    {
        Pix.ComposeRGBPixel(r, g, b, out uint pixel);
        return pixel;
    }

    private static float[] Vals(Numa na)
    {
        float[] vals = new float[na.Count];
        for (int i = 0; i < vals.Length; i++)
            vals[i] = na.GetFValue(i);
        return vals;
    }

    /// <summary>Expected histogram: all zero except for the given (index, count) pairs.</summary>
    private static float[] Histo(int size, params (int index, float count)[] counts)
    {
        float[] histo = new float[size];
        foreach ((int index, float count) in counts)
            histo[index] = count;
        return histo;
    }

    // 4 x 3, 8 bpp; the histogram is 0:4, 1:3, 2:3, 3:1, 255:1
    private static Pix Gray8() => Make(4, 3, 8,
        0, 1, 1, 2,
        2, 2, 3, 255,
        0, 0, 0, 1);

    /*------------------------------------------------------------*
     *                        Gray histograms                     *
     *------------------------------------------------------------*/
    [Fact]
    public void GrayHistogram8MatchesScalarAtWordBoundaries()
    {
        var rng = new Random(731049);
        foreach (int width in new[] { 1, 2, 3, 4, 31, 32, 33, 63, 64, 65, 71, 257 }) {
            foreach (int height in new[] { 1, 3, 17 }) {
                Pix pix = Pix.Create(width, height, 8);
                for (int i = 0; i < pix.Data.Length; i++)
                    pix.Data[i] = (uint)rng.NextInt64(0, 1L << 32);
                foreach (int factor in new[] { 1, 2, 3, 7 }) {
                    float[] expected = new float[256];
                    for (int y = 0; y < height; y += factor) {
                        for (int x = 0; x < width; x += factor) {
                            pix.GetPixel(x, y, out uint value);
                            expected[value] += 1.0f;
                        }
                    }
                    Assert.Equal(expected, Vals(pix.GetGrayHistogram(factor)));
                }
            }
        }
    }

    [Fact]
    public void GrayHistogram_8bpp()
    {
        Pix pix = Gray8();
        Assert.Equal(Histo(256, (0, 4), (1, 3), (2, 3), (3, 1), (255, 1)),
                     Vals(pix.GetGrayHistogram(1)));
    }

    [Fact]
    public void GrayHistogram_8bpp_Subsampled()
    {
        // factor 2 samples (0,0), (2,0), (0,2), (2,2): values 0, 1, 0, 0
        Assert.Equal(Histo(256, (0, 3), (1, 1)), Vals(Gray8().GetGrayHistogram(2)));
    }

    [Fact]
    public void GrayHistogram_1bpp()
    {
        Pix pix = Make(5, 2, 1,
            1, 0, 1, 1, 0,
            0, 0, 1, 0, 0);
        Assert.Equal(new float[] { 6, 4 }, Vals(pix.GetGrayHistogram(1)));
    }

    [Fact]
    public void GrayHistogram_2bpp()
    {
        Pix pix = Make(3, 2, 2,
            0, 1, 2,
            3, 3, 0);
        Assert.Equal(new float[] { 2, 1, 1, 2 }, Vals(pix.GetGrayHistogram(1)));
    }

    [Fact]
    public void GrayHistogram_4bpp()
    {
        Pix pix = Make(3, 2, 4,
            15, 0, 7,
            7, 7, 15);
        Assert.Equal(Histo(16, (0, 1), (7, 3), (15, 2)), Vals(pix.GetGrayHistogram(1)));
    }

    [Fact]
    public void GrayHistogram_16bpp()
    {
        Pix pix = Make(3, 2, 16,
            0, 65535, 300,
            300, 300, 1);
        Assert.Equal(Histo(65536, (0, 1), (1, 1), (300, 3), (65535, 1)),
                     Vals(pix.GetGrayHistogram(1)));
    }

    [Fact]
    public void GrayHistogram_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 32).GetGrayHistogram(1));
        Assert.Throws<ArgumentException>(() => Gray8().GetGrayHistogram(0));
    }

    [Fact]
    public void GrayHistogramMasked_Mask()
    {
        // The mask covers (1,1) = 2, (1,2) = 0, (2,2) = 0; the pixel (2,1) is not set
        Pix mask = Make(2, 2, 1,
            1, 0,
            1, 1);
        Assert.Equal(Histo(256, (0, 2), (2, 1)), Vals(Gray8().GetGrayHistogramMasked(mask, 1, 1, 1)));
    }

    [Fact]
    public void GrayHistogramMasked_NegativeOffsetIsClipped()
    {
        // Only the mask pixel (1,1) lands inside the image, on the pixel (0,0) = 0
        Pix mask = Make(2, 2, 1,
            1, 0,
            1, 1);
        Assert.Equal(Histo(256, (0, 1)), Vals(Gray8().GetGrayHistogramMasked(mask, -1, -1, 1)));
    }

    [Fact]
    public void GrayHistogramMasked_NullMaskIsFullHistogram()
    {
        Pix pix = Gray8();
        Assert.Equal(Vals(pix.GetGrayHistogram(1)), Vals(pix.GetGrayHistogramMasked(null, 5, 5, 1)));
    }

    [Fact]
    public void GrayHistogramMasked_InvalidArguments()
    {
        Pix mask8 = Pix.Create(2, 2, 8);
        Assert.Throws<ArgumentException>(() => Gray8().GetGrayHistogramMasked(mask8, 0, 0, 1));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 32)
            .GetGrayHistogramMasked(Pix.Create(2, 2, 1), 0, 0, 1));
    }

    [Fact]
    public void GrayHistogramInRect()
    {
        // pixels (1,1) = 2, (2,1) = 3, (1,2) = 0, (2,2) = 0
        Assert.Equal(Histo(256, (0, 2), (2, 1), (3, 1)),
                     Vals(Gray8().GetGrayHistogramInRect(Box.Create(1, 1, 2, 2), 1)));
    }

    [Fact]
    public void GrayHistogramInRect_BoxPartlyOutsideIsClipped()
    {
        // inside: x = 2..3, y = 1..2 -> 3, 255, 0, 1
        Assert.Equal(Histo(256, (0, 1), (1, 1), (3, 1), (255, 1)),
                     Vals(Gray8().GetGrayHistogramInRect(Box.Create(2, 1, 5, 5), 1)));
    }

    [Fact]
    public void GrayHistogramInRect_NullBoxIsFullHistogram()
    {
        Pix pix = Gray8();
        Assert.Equal(Vals(pix.GetGrayHistogram(1)), Vals(pix.GetGrayHistogramInRect(null, 1)));
    }

    /*------------------------------------------------------------*
     *                       Color histograms                     *
     *------------------------------------------------------------*/
    private static Pix Rgb3x2() => Make(3, 2, 32,
        Rgb(10, 20, 30), Rgb(10, 0, 30), Rgb(255, 20, 0),
        Rgb(0, 0, 0), Rgb(10, 20, 30), Rgb(255, 255, 255));

    [Fact]
    public void ColorHistogram_Rgb()
    {
        Rgb3x2().GetColorHistogram(1, out Numa nar, out Numa nag, out Numa nab);
        Assert.Equal(Histo(256, (0, 1), (10, 3), (255, 2)), Vals(nar));
        Assert.Equal(Histo(256, (0, 2), (20, 3), (255, 1)), Vals(nag));
        Assert.Equal(Histo(256, (0, 2), (30, 3), (255, 1)), Vals(nab));
    }

    [Fact]
    public void ColorHistogram_Colormapped()
    {
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddColor(1, 2, 3);
        cmap.AddColor(4, 5, 6);
        cmap.AddColor(7, 8, 9);
        Pix pix = Make(3, 2, 2,
            0, 1, 2,
            1, 1, 0);
        pix.SetColormap(cmap);
        pix.GetColorHistogram(1, out Numa nar, out Numa nag, out Numa nab);
        Assert.Equal(Histo(256, (1, 2), (4, 3), (7, 1)), Vals(nar));
        Assert.Equal(Histo(256, (2, 2), (5, 3), (8, 1)), Vals(nag));
        Assert.Equal(Histo(256, (3, 2), (6, 3), (9, 1)), Vals(nab));
    }

    [Fact]
    public void ColorHistogram_Subsampled()
    {
        // factor 2 samples (0,0) and (2,0)
        Rgb3x2().GetColorHistogram(2, out Numa nar, out _, out _);
        Assert.Equal(Histo(256, (10, 1), (255, 1)), Vals(nar));
    }

    [Fact]
    public void ColorHistogram_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Gray8().GetColorHistogram(1, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => Rgb3x2().GetColorHistogram(0, out _, out _, out _));
    }

    [Fact]
    public void ColorHistogramMasked()
    {
        // mask over the second row, first and third pixels: (0,0,0) and (255,255,255)
        Pix mask = Make(3, 1, 1, 1, 0, 1);
        Rgb3x2().GetColorHistogramMasked(mask, 0, 1, 1, out Numa nar, out Numa nag, out Numa nab);
        Assert.Equal(Histo(256, (0, 1), (255, 1)), Vals(nar));
        Assert.Equal(Histo(256, (0, 1), (255, 1)), Vals(nag));
        Assert.Equal(Histo(256, (0, 1), (255, 1)), Vals(nab));
    }

    [Fact]
    public void ColorHistogramMasked_NullMaskIsFullHistogram()
    {
        Pix pix = Rgb3x2();
        pix.GetColorHistogram(1, out Numa nar1, out _, out _);
        pix.GetColorHistogramMasked(null, 0, 0, 1, out Numa nar2, out _, out _);
        Assert.Equal(Vals(nar1), Vals(nar2));
    }

    /*------------------------------------------------------------*
     *                     Colormap histograms                    *
     *------------------------------------------------------------*/
    private static Pix Cmapped2()
    {
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddColor(1, 2, 3);
        cmap.AddColor(4, 5, 6);
        cmap.AddColor(7, 8, 9);
        Pix pix = Make(3, 2, 2,
            0, 1, 2,
            1, 1, 0);
        pix.SetColormap(cmap);
        return pix;
    }

    [Fact]
    public void CmapHistogram()
    {
        Assert.Equal(new float[] { 2, 3, 1, 0 }, Vals(Cmapped2().GetCmapHistogram(1)));
    }

    [Fact]
    public void CmapHistogram_Subsampled()
    {
        // samples (0,0) = 0 and (2,0) = 2
        Assert.Equal(new float[] { 1, 0, 1, 0 }, Vals(Cmapped2().GetCmapHistogram(2)));
    }

    [Fact]
    public void CmapHistogram_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Gray8().GetCmapHistogram(1));
        Assert.Throws<ArgumentException>(() => Cmapped2().GetCmapHistogram(0));
    }

    [Fact]
    public void CmapHistogramMasked()
    {
        // mask [1 0 1] over the second row of indices [1 1 0]: indices 1 and 0
        Pix mask = Make(3, 1, 1, 1, 0, 1);
        Assert.Equal(new float[] { 1, 1, 0, 0 }, Vals(Cmapped2().GetCmapHistogramMasked(mask, 0, 1, 1)));
    }

    [Fact]
    public void CmapHistogramMasked_NullMaskIsFullHistogram()
    {
        Assert.Equal(new float[] { 2, 3, 1, 0 }, Vals(Cmapped2().GetCmapHistogramMasked(null, 0, 0, 1)));
    }

    [Fact]
    public void CmapHistogramInRect()
    {
        // x = 1..2, y = 0..1: indices 1, 2, 1, 0
        Assert.Equal(new float[] { 1, 2, 1, 0 },
                     Vals(Cmapped2().GetCmapHistogramInRect(Box.Create(1, 0, 2, 2), 1)));
    }

    [Fact]
    public void CmapHistogramInRect_NullBoxIsFullHistogram()
    {
        Assert.Equal(new float[] { 2, 3, 1, 0 }, Vals(Cmapped2().GetCmapHistogramInRect(null, 1)));
    }

    /*------------------------------------------------------------*
     *                         Rank values                        *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(0.0f, 0.0f)]
    [InlineData(0.5f, 21.0f)]
    [InlineData(1.0f, 41.0f)]
    public void RankValueMasked(float rank, float expected)
    {
        // histogram with one count at each of 10, 20, 30 and 40
        Pix pix = Make(4, 1, 8, 10, 20, 30, 40);
        pix.GetRankValueMasked(null, 0, 0, 1, rank, out float val, out Numa na);
        Assert.Equal(expected, val);
        Assert.Equal(Histo(256, (10, 1), (20, 1), (30, 1), (40, 1)), Vals(na));
    }

    [Fact]
    public void RankValueMasked_InvalidArguments()
    {
        Pix pix = Make(4, 1, 8, 10, 20, 30, 40);
        Assert.Throws<ArgumentException>(() => pix.GetRankValueMasked(null, 0, 0, 1, 1.5f, out _, out _));
        Assert.Throws<ArgumentException>(() => pix.GetRankValueMasked(null, 0, 0, 0, 0.5f, out _, out _));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 32)
            .GetRankValueMasked(null, 0, 0, 1, 0.5f, out _, out _));
    }

    /*------------------------------------------------------------*
     *                          Averages                          *
     *------------------------------------------------------------*/
    // mean 4.5, mean square 25.5
    private static Pix Gray4x2() => Make(4, 2, 8,
        1, 2, 3, 4,
        5, 6, 7, 8);

    [Fact]
    public void AverageMasked_AllTypes()
    {
        Pix pix = Gray4x2();
        Assert.Equal(4.5f, pix.GetAverageMasked(null, 0, 0, 1, L.L_MEAN_ABSVAL));
        Assert.Equal((float)Math.Sqrt(25.5), pix.GetAverageMasked(null, 0, 0, 1, L.L_ROOT_MEAN_SQUARE));
        Assert.Equal((float)Math.Sqrt(5.25), pix.GetAverageMasked(null, 0, 0, 1, L.L_STANDARD_DEVIATION));
        Assert.Equal(5.25f, pix.GetAverageMasked(null, 0, 0, 1, L.L_VARIANCE));
    }

    [Fact]
    public void AverageMasked_Mask()
    {
        // pixels 1 and 4
        Pix mask = Make(4, 1, 1, 1, 0, 0, 1);
        Assert.Equal(2.5f, Gray4x2().GetAverageMasked(mask, 0, 0, 1, L.L_MEAN_ABSVAL));
        // pixels 5 and 8 with an offset
        Assert.Equal(6.5f, Gray4x2().GetAverageMasked(mask, 0, 1, 1, L.L_MEAN_ABSVAL));
    }

    [Fact]
    public void AverageMasked_Subsampled()
    {
        // factor 2 samples (0,0) = 1 and (2,0) = 3
        Assert.Equal(2.0f, Gray4x2().GetAverageMasked(null, 0, 0, 2, L.L_MEAN_ABSVAL));
    }

    [Fact]
    public void AverageMasked_16bpp()
    {
        Pix pix = Make(2, 1, 16, 100, 300);
        Assert.Equal(200.0f, pix.GetAverageMasked(null, 0, 0, 1, L.L_MEAN_ABSVAL));
        Assert.Equal(10000.0f, pix.GetAverageMasked(null, 0, 0, 1, L.L_VARIANCE));
        Assert.Equal(100.0f, pix.GetAverageMasked(null, 0, 0, 1, L.L_STANDARD_DEVIATION));
        Assert.Equal((float)Math.Sqrt(50000.0), pix.GetAverageMasked(null, 0, 0, 1, L.L_ROOT_MEAN_SQUARE));
    }

    [Fact]
    public void AverageMasked_InvalidArguments()
    {
        Pix pix = Gray4x2();
        Assert.Throws<ArgumentException>(() => pix.GetAverageMasked(null, 0, 0, 0, L.L_MEAN_ABSVAL));
        Assert.Throws<ArgumentException>(() => pix.GetAverageMasked(null, 0, 0, 1, 99));
        Assert.Throws<ArgumentException>(() => pix.GetAverageMasked(Pix.Create(4, 1, 1), 0, 0, 1, L.L_MEAN_ABSVAL));  // empty mask
        Assert.Throws<ArgumentException>(() => pix.GetAverageMasked(Pix.Create(4, 1, 8), 0, 0, 1, L.L_MEAN_ABSVAL));  // mask not 1 bpp
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 32).GetAverageMasked(null, 0, 0, 1, L.L_MEAN_ABSVAL));
    }

    [Fact]
    public void AverageMasked_MaskOutsideImageSamplesNothing()
    {
        Pix mask = Make(1, 1, 1, 1);
        Assert.Throws<LeptonicaException>(() => Gray4x2().GetAverageMasked(mask, 10, 10, 1, L.L_MEAN_ABSVAL));
    }

    private static Pix Rgb3x1() => Make(3, 1, 32,
        Rgb(10, 20, 30), Rgb(20, 40, 60), Rgb(31, 61, 91));

    [Fact]
    public void PixelAverage_Rgb()
    {
        // sums 61, 121, 181 over 3 pixels: 20.33, 40.33, 60.33, truncated
        Assert.Equal(Rgb(20, 40, 60), Rgb3x1().GetPixelAverage(null, 0, 0, 1));
    }

    [Fact]
    public void PixelAverage_RgbMasked()
    {
        Pix mask = Make(3, 1, 1, 1, 1, 0);
        Assert.Equal(Rgb(15, 30, 45), Rgb3x1().GetPixelAverage(mask, 0, 0, 1));
    }

    [Fact]
    public void PixelAverage_RgbSubsampled()
    {
        // factor 2 samples (0,0) and (2,0)
        Assert.Equal(Rgb(20, 40, 60), Rgb3x1().GetPixelAverage(null, 0, 0, 2));
    }

    [Fact]
    public void PixelAverage_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Gray4x2().GetPixelAverage(null, 0, 0, 1));
        Assert.Throws<ArgumentException>(() => Rgb3x1().GetPixelAverage(null, 0, 0, 0));
        Assert.Throws<LeptonicaException>(() => Rgb3x1().GetPixelAverage(Make(1, 1, 1, 1), 10, 10, 1));
    }

    [Fact]
    public void AverageMaskedRGB()
    {
        Pix pix = Rgb3x1();
        pix.GetAverageMaskedRGB(null, 0, 0, 1, L.L_MEAN_ABSVAL, out float r, out float g, out float b);
        Assert.Equal((float)(61.0 / 3.0), r);
        Assert.Equal((float)(121.0 / 3.0), g);
        Assert.Equal((float)(181.0 / 3.0), b);
    }

    [Fact]
    public void AverageMaskedRGB_Variance()
    {
        // red components 10, 20, 31: mean 61/3, mean square 1461/3
        Pix pix = Rgb3x1();
        pix.GetAverageMaskedRGB(null, 0, 0, 1, L.L_VARIANCE, out float r, out _, out _);
        double mean = 61.0 / 3.0;
        Assert.Equal((float)(1461.0 / 3.0 - mean * mean), r);
    }

    [Fact]
    public void AverageMaskedRGB_Masked()
    {
        Pix mask = Make(3, 1, 1, 0, 1, 1);
        Rgb3x1().GetAverageMaskedRGB(mask, 0, 0, 1, L.L_MEAN_ABSVAL, out float r, out float g, out float b);
        Assert.Equal(25.5f, r);
        Assert.Equal(50.5f, g);
        Assert.Equal(75.5f, b);
    }

    [Fact]
    public void AverageMaskedRGB_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Gray4x2()
            .GetAverageMaskedRGB(null, 0, 0, 1, L.L_MEAN_ABSVAL, out _, out _, out _));
    }

    /*------------------------------------------------------------*
     *                       Tiled averages                       *
     *------------------------------------------------------------*/
    private static Pix Tiles4x4() => Make(4, 4, 8,
        1, 3, 10, 10,
        5, 7, 10, 10,
        0, 0, 8, 8,
        0, 0, 8, 8);

    private static uint[] Pixels(Pix pix)
    {
        pix.GetDimensions(out int w, out int h, out _);
        uint[] vals = new uint[w * h];
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++) {
                pix.GetPixel(j, i, out uint val);
                vals[i * w + j] = val;
            }
        return vals;
    }

    [Fact]
    public void AverageTiled_Mean()
    {
        Pix pixd = Tiles4x4().GetAverageTiled(2, 2, L.L_MEAN_ABSVAL);
        Assert.Equal(2, pixd.Width);
        Assert.Equal(2, pixd.Height);
        Assert.Equal(8, pixd.Depth);
        Assert.Equal(new uint[] { 4, 10, 0, 8 }, Pixels(pixd));
    }

    [Fact]
    public void AverageTiled_RootMeanSquare()
    {
        // first tile: mean square (1 + 9 + 25 + 49) / 4 = 21, sqrt + 0.5 = 5.08
        Pix pixd = Tiles4x4().GetAverageTiled(2, 2, L.L_ROOT_MEAN_SQUARE);
        Assert.Equal(new uint[] { 5, 10, 0, 8 }, Pixels(pixd));
    }

    [Fact]
    public void AverageTiled_StandardDeviation()
    {
        // first tile: mean 4, mean square 21, variance 5, sqrt + 0.5 = 2.74
        // the other tiles are constant
        Pix pixd = Tiles4x4().GetAverageTiled(2, 2, L.L_STANDARD_DEVIATION);
        Assert.Equal(new uint[] { 2, 0, 0, 0 }, Pixels(pixd));
    }

    [Fact]
    public void AverageTiled_OnlyCompleteTiles()
    {
        // 5 x 5 image: only 2 x 2 tiles of size 2 x 2 fit; the last row and column are ignored
        Pix pix = Pix.Create(5, 5, 8);
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
                pix.SetPixel(j, i, (uint)(i < 4 && j < 4 ? 10 : 200));
        Pix pixd = pix.GetAverageTiled(2, 2, L.L_MEAN_ABSVAL);
        Assert.Equal(2, pixd.Width);
        Assert.Equal(2, pixd.Height);
        Assert.Equal(new uint[] { 10, 10, 10, 10 }, Pixels(pixd));
    }

    [Fact]
    public void AverageTiled_InvalidArguments()
    {
        Pix pix = Tiles4x4();
        Assert.Throws<ArgumentException>(() => pix.GetAverageTiled(1, 2, L.L_MEAN_ABSVAL));
        Assert.Throws<ArgumentException>(() => pix.GetAverageTiled(2, 2, L.L_VARIANCE));
        Assert.Throws<ArgumentException>(() => pix.GetAverageTiled(5, 2, L.L_MEAN_ABSVAL));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).GetAverageTiled(2, 2, L.L_MEAN_ABSVAL));
    }

    [Fact]
    public void AverageTiledRGB()
    {
        // a single 2 x 2 tile; the components are 10/20/30 in two pixels and 30/40/50 in the others
        Pix pix = Make(2, 2, 32,
            Rgb(10, 20, 30), Rgb(30, 40, 50),
            Rgb(30, 40, 50), Rgb(10, 20, 30));
        pix.GetAverageTiledRGB(2, 2, L.L_MEAN_ABSVAL, out Pix pixr, out Pix pixg, out Pix pixb);
        Assert.Equal(new uint[] { 20 }, Pixels(pixr));
        Assert.Equal(new uint[] { 30 }, Pixels(pixg));
        Assert.Equal(new uint[] { 40 }, Pixels(pixb));
    }

    /*------------------------------------------------------------*
     *                   Row and column statistics                *
     *------------------------------------------------------------*/
    private static Pix Stats4x2() => Make(4, 2, 8,
        1, 2, 3, 4,
        5, 5, 5, 9);

    [Fact]
    public void RowStats_WholeImage()
    {
        Stats4x2().RowStats(null, out Numa mean, out Numa median, out Numa mode, out Numa modecount,
                            out Numa var, out Numa rootvar);
        Assert.Equal(new float[] { 2.5f, 6.0f }, Vals(mean));
        Assert.Equal(new float[] { 2, 5 }, Vals(median));
        Assert.Equal(new float[] { 1, 5 }, Vals(mode));
        Assert.Equal(new float[] { 1, 3 }, Vals(modecount));
        Assert.Equal(new float[] { 1.25f, 3.0f }, Vals(var));
        Assert.Equal(new float[] { MathF.Sqrt(1.25f), MathF.Sqrt(3.0f) }, Vals(rootvar));
    }

    [Fact]
    public void RowStats_Box()
    {
        // columns 1..2: row 0 has 2, 3 and row 1 has 5, 5
        Stats4x2().RowStats(Box.Create(1, 0, 2, 2), out Numa mean, out Numa median, out Numa mode,
                            out Numa modecount, out Numa var, out Numa rootvar);
        Assert.Equal(new float[] { 2.5f, 5.0f }, Vals(mean));
        Assert.Equal(new float[] { 2, 5 }, Vals(median));
        Assert.Equal(new float[] { 2, 5 }, Vals(mode));
        Assert.Equal(new float[] { 1, 2 }, Vals(modecount));
        Assert.Equal(new float[] { 0.25f, 0.0f }, Vals(var));
        Assert.Equal(new float[] { 0.5f, 0.0f }, Vals(rootvar));
    }

    [Fact]
    public void ColumnStats_WholeImage()
    {
        Stats4x2().ColumnStats(null, out Numa mean, out Numa median, out Numa mode, out Numa modecount,
                               out Numa var, out Numa rootvar);
        Assert.Equal(new float[] { 3.0f, 3.5f, 4.0f, 6.5f }, Vals(mean));
        Assert.Equal(new float[] { 1, 2, 3, 4 }, Vals(median));
        Assert.Equal(new float[] { 1, 2, 3, 4 }, Vals(mode));
        Assert.Equal(new float[] { 1, 1, 1, 1 }, Vals(modecount));
        Assert.Equal(new float[] { 4.0f, 2.25f, 1.0f, 6.25f }, Vals(var));
        Assert.Equal(new float[] { 2.0f, 1.5f, 1.0f, 2.5f }, Vals(rootvar));
    }

    [Fact]
    public void ColumnStats_Box()
    {
        // columns 0..1
        Stats4x2().ColumnStats(Box.Create(0, 0, 2, 2), out Numa mean, out _, out _, out _, out _, out _);
        Assert.Equal(new float[] { 3.0f, 3.5f }, Vals(mean));
    }

    [Fact]
    public void RowAndColumnStats_InvalidArguments()
    {
        Pix pix = Pix.Create(4, 4, 32);
        Assert.Throws<ArgumentException>(() => pix.RowStats(null, out _, out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => pix.ColumnStats(null, out _, out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => Stats4x2()
            .RowStats(Box.Create(10, 10, 2, 2), out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void GetRowStats_Mean()
    {
        float[] colvect = new float[2];
        Stats4x2().GetRowStats(L.L_MEAN_ABSVAL, 0, 0, colvect);
        Assert.Equal(new float[] { 2.5f, 6.0f }, colvect);
    }

    [Fact]
    public void GetRowStats_FineBins()
    {
        // with 256 bins, bin and gray value are the same
        float[] colvect = new float[2];
        Stats4x2().GetRowStats(L.L_MEDIAN_VAL, 256, 0, colvect);
        Assert.Equal(new float[] { 2, 5 }, colvect);
        Stats4x2().GetRowStats(L.L_MODE_VAL, 256, 0, colvect);
        Assert.Equal(new float[] { 1, 5 }, colvect);
        Stats4x2().GetRowStats(L.L_MODE_COUNT, 256, 0, colvect);
        Assert.Equal(new float[] { 1, 3 }, colvect);
    }

    [Fact]
    public void GetRowStats_CoarseBins()
    {
        // 16 bins of width 16: all values are in bin 0, whose gray value is (0 * 256 + 128) / 16 = 8
        float[] colvect = new float[2];
        Stats4x2().GetRowStats(L.L_MEDIAN_VAL, 16, 0, colvect);
        Assert.Equal(new float[] { 8, 8 }, colvect);
        Stats4x2().GetRowStats(L.L_MODE_VAL, 16, 0, colvect);
        Assert.Equal(new float[] { 8, 8 }, colvect);
        Stats4x2().GetRowStats(L.L_MODE_COUNT, 16, 0, colvect);
        Assert.Equal(new float[] { 4, 4 }, colvect);
    }

    [Fact]
    public void GetRowStats_ModeThreshold()
    {
        // the mode bin of row 0 holds a single pixel, which is below the threshold 2
        float[] colvect = new float[2];
        Stats4x2().GetRowStats(L.L_MODE_VAL, 256, 2, colvect);
        Assert.Equal(new float[] { 0, 5 }, colvect);
    }

    [Fact]
    public void GetColumnStats()
    {
        float[] rowvect = new float[4];
        Stats4x2().GetColumnStats(L.L_MEAN_ABSVAL, 0, 0, rowvect);
        Assert.Equal(new float[] { 3.0f, 3.5f, 4.0f, 6.5f }, rowvect);
        Stats4x2().GetColumnStats(L.L_MEDIAN_VAL, 256, 0, rowvect);
        Assert.Equal(new float[] { 1, 2, 3, 4 }, rowvect);
        Stats4x2().GetColumnStats(L.L_MODE_VAL, 256, 0, rowvect);
        Assert.Equal(new float[] { 1, 2, 3, 4 }, rowvect);
        Stats4x2().GetColumnStats(L.L_MODE_COUNT, 16, 0, rowvect);
        Assert.Equal(new float[] { 2, 2, 2, 2 }, rowvect);
        Stats4x2().GetColumnStats(L.L_MEDIAN_VAL, 16, 0, rowvect);
        Assert.Equal(new float[] { 8, 8, 8, 8 }, rowvect);
    }

    [Fact]
    public void GetRowAndColumnStats_InvalidArguments()
    {
        Pix pix = Stats4x2();
        float[] vect = new float[4];
        Assert.Throws<ArgumentException>(() => pix.GetRowStats(99, 16, 0, vect));
        Assert.Throws<ArgumentException>(() => pix.GetRowStats(L.L_MEDIAN_VAL, 0, 0, vect));
        Assert.Throws<ArgumentException>(() => pix.GetColumnStats(L.L_MODE_VAL, 257, 0, vect));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).GetRowStats(L.L_MEAN_ABSVAL, 0, 0, vect));
    }

    [Fact]
    public void SetPixelColumn()
    {
        Pix pix = Pix.Create(3, 2, 8);
        pix.SetPixelColumn(1, new[] { 7.9f, 3.2f });
        Assert.Equal(new uint[] { 0, 7, 0, 0, 3, 0 }, Pixels(pix));
        Assert.Throws<ArgumentException>(() => pix.SetPixelColumn(-1, new[] { 0f, 0f }));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 2, 32).SetPixelColumn(0, new[] { 0f, 0f }));
    }

    /*------------------------------------------------------------*
     *                  Ranges and extreme values                 *
     *------------------------------------------------------------*/
    private static Pix Colors3x1() => Make(3, 1, 32,
        Rgb(10, 200, 30), Rgb(20, 100, 40), Rgb(5, 150, 255));

    [Fact]
    public void RangeValues_Gray()
    {
        Pix pix = Make(4, 1, 8, 7, 3, 9, 250);
        pix.GetRangeValues(1, L.L_SELECT_RED, out int minval, out int maxval);
        Assert.Equal(3, minval);
        Assert.Equal(250, maxval);
        // factor 2 samples (0,0) = 7 and (2,0) = 9
        pix.GetRangeValues(2, L.L_SELECT_RED, out minval, out maxval);
        Assert.Equal(7, minval);
        Assert.Equal(9, maxval);
    }

    [Theory]
    [InlineData(L.L_SELECT_RED, 5, 20)]
    [InlineData(L.L_SELECT_GREEN, 100, 200)]
    [InlineData(L.L_SELECT_BLUE, 30, 255)]
    public void RangeValues_Rgb(int color, int expectedMin, int expectedMax)
    {
        Colors3x1().GetRangeValues(1, color, out int minval, out int maxval);
        Assert.Equal(expectedMin, minval);
        Assert.Equal(expectedMax, maxval);
    }

    [Fact]
    public void RangeValues_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Colors3x1().GetRangeValues(1, L.L_SELECT_MIN, out _, out _));
        Assert.Throws<ArgumentException>(() => Colors3x1().GetRangeValues(0, L.L_SELECT_RED, out _, out _));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 16).GetRangeValues(1, L.L_SELECT_RED, out _, out _));
    }

    [Fact]
    public void ExtremeValue_Gray()
    {
        Pix pix = Make(4, 1, 8, 7, 3, 9, 250);
        pix.GetExtremeValue(1, L.L_SELECT_MIN, out int r, out int g, out int b, out int gray);
        Assert.Equal(3, gray);
        Assert.Equal(-1, r);
        Assert.Equal(-1, g);
        Assert.Equal(-1, b);
        pix.GetExtremeValue(1, L.L_SELECT_MAX, out _, out _, out _, out gray);
        Assert.Equal(250, gray);
    }

    [Fact]
    public void ExtremeValue_Rgb()
    {
        Colors3x1().GetExtremeValue(1, L.L_SELECT_MIN, out int r, out int g, out int b, out int gray);
        Assert.Equal((5, 100, 30, -1), (r, g, b, gray));
        Colors3x1().GetExtremeValue(1, L.L_SELECT_MAX, out r, out g, out b, out gray);
        Assert.Equal((20, 200, 255, -1), (r, g, b, gray));
    }

    [Fact]
    public void ExtremeValue_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Colors3x1().GetExtremeValue(1, 99, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => Colors3x1().GetExtremeValue(0, L.L_SELECT_MAX, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 4).GetExtremeValue(1, L.L_SELECT_MAX, out _, out _, out _, out _));
    }

    private static Pix MaxPix() => Make(4, 3, 8,
        0, 1, 2, 3,
        4, 200, 6, 7,
        8, 9, 10, 200);

    [Fact]
    public void MaxValueInRect_WholeImage()
    {
        // the first of two equal maxima, in raster order, is kept
        MaxPix().GetMaxValueInRect(null, out uint maxval, out int xmax, out int ymax);
        Assert.Equal((200u, 1, 1), (maxval, xmax, ymax));
    }

    [Fact]
    public void MaxValueInRect_Box()
    {
        MaxPix().GetMaxValueInRect(Box.Create(2, 0, 2, 3), out uint maxval, out int xmax, out int ymax);
        Assert.Equal((200u, 3, 2), (maxval, xmax, ymax));
        MaxPix().GetMaxValueInRect(Box.Create(0, 0, 2, 1), out maxval, out xmax, out ymax);
        Assert.Equal((1u, 1, 0), (maxval, xmax, ymax));
    }

    [Fact]
    public void MaxValueInRect_NoCountsPicksTheCenter()
    {
        Pix pix = Pix.Create(3, 3, 8);
        pix.GetMaxValueInRect(null, out uint maxval, out int xmax, out int ymax);
        Assert.Equal((0u, 1, 1), (maxval, xmax, ymax));
        pix.GetMaxValueInRect(Box.Create(0, 0, 2, 2), out maxval, out xmax, out ymax);
        Assert.Equal((0u, 0, 0), (maxval, xmax, ymax));
    }

    [Fact]
    public void MaxValueInRect_16And32bpp()
    {
        Pix pix16 = Make(3, 1, 16, 5, 65535, 7);
        pix16.GetMaxValueInRect(null, out uint maxval, out int xmax, out int ymax);
        Assert.Equal((65535u, 1, 0), (maxval, xmax, ymax));

        Pix pix32 = Make(3, 2, 32,
            1, 2, 3,
            4, 0xdeadbeef, 6);
        pix32.GetMaxValueInRect(null, out maxval, out xmax, out ymax);
        Assert.Equal((0xdeadbeefu, 1, 1), (maxval, xmax, ymax));
    }

    [Fact]
    public void MaxValueInRect_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 4).GetMaxValueInRect(null, out _, out _, out _));
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(0, 0, 0);
        Pix pix = Pix.Create(3, 3, 8);
        pix.SetColormap(cmap);
        Assert.Throws<ArgumentException>(() => pix.GetMaxValueInRect(null, out _, out _, out _));
    }

    [Fact]
    public void MaxColorIndex()
    {
        Assert.Equal(0, Pix.Create(3, 3, 1).GetMaxColorIndex());
        Assert.Equal(1, Make(3, 1, 1, 0, 1, 0).GetMaxColorIndex());
        Assert.Equal(2, Make(3, 1, 2, 0, 2, 1).GetMaxColorIndex());
        Assert.Equal(3, Make(3, 1, 2, 0, 3, 1).GetMaxColorIndex());
        Assert.Equal(9, Make(3, 2, 4, 0, 9, 1, 3, 3, 2).GetMaxColorIndex());
        Assert.Equal(200, Make(3, 2, 8, 0, 9, 1, 3, 200, 2).GetMaxColorIndex());
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 16).GetMaxColorIndex());
    }

    /*------------------------------------------------------------*
     *                    Binned (rank) colors                    *
     *------------------------------------------------------------*/
    // The gray value of each pixel is its sorting key, and the color is (gray, gray + 1, gray + 2)
    private static void MakeBinnedPair(int w, int h, uint[] grays, out Pix pixs, out Pix pixg)
    {
        pixg = Make(w, h, 8, grays);
        uint[] colors = new uint[grays.Length];
        for (int i = 0; i < grays.Length; i++)
            colors[i] = Rgb((int)grays[i], (int)grays[i] + 1, (int)grays[i] + 2);
        pixs = Make(w, h, 32, colors);
    }

    [Fact]
    public void BinnedColor_TwoBins()
    {
        MakeBinnedPair(5, 2, new uint[] { 100, 10, 90, 20, 80, 30, 70, 40, 60, 50 },
                       out Pix pixs, out Pix pixg);
        uint[]? carray = pixs.GetBinnedColor(pixg, 1, 2, null);
        Assert.NotNull(carray);
        // the 5 darkest pixels have grays 10..50 (mean 30); the others 60..100 (mean 80)
        Assert.Equal(new[] { Rgb(30, 31, 32), Rgb(80, 81, 82) }, carray);
    }

    [Fact]
    public void BinnedColor_OneBinIsTheAverageColor()
    {
        MakeBinnedPair(5, 2, new uint[] { 100, 10, 90, 20, 80, 30, 70, 40, 60, 50 },
                       out Pix pixs, out Pix pixg);
        uint[]? carray = pixs.GetBinnedColor(pixg, 1, 1, null);
        Assert.Equal(new[] { Rgb(55, 56, 57) }, carray);
    }

    [Fact]
    public void BinnedColor_TooFewPixelsPerBinReturnsNull()
    {
        // 8 pixels in 2 bins: 4 per bin, less than the required 5
        MakeBinnedPair(4, 2, new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 }, out Pix pixs, out Pix pixg);
        Assert.Null(pixs.GetBinnedColor(pixg, 1, 2, null));
    }

    [Fact]
    public void BinnedColor_InvalidArguments()
    {
        MakeBinnedPair(5, 2, new uint[] { 100, 10, 90, 20, 80, 30, 70, 40, 60, 50 },
                       out Pix pixs, out Pix pixg);
        Assert.Throws<ArgumentException>(() => pixs.GetBinnedColor(pixg, 1, 101, null));
        Assert.Throws<ArgumentException>(() => pixs.GetBinnedColor(pixg, 1, 0, null));
        Assert.Throws<ArgumentException>(() => pixg.GetBinnedColor(pixg, 1, 2, null));
        Assert.Throws<ArgumentException>(() => pixs.GetBinnedColor(pixs, 1, 2, null));
    }

    [Fact]
    public void RankColorArray_InvalidArguments()
    {
        Pix pix = Pix.Create(20, 20, 32);
        Assert.Throws<ArgumentException>(() => pix.GetRankColorArray(1, L.L_SELECT_RED, 1, null, 0));
        Assert.Throws<ArgumentException>(() => pix.GetRankColorArray(2, L.L_SELECT_RED, 0, null, 0));
        Assert.Throws<ArgumentException>(() => pix.GetRankColorArray(2, 99, 1, null, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(20, 20, 8).GetRankColorArray(2, L.L_SELECT_RED, 1, null, 0));
    }

    [Fact]
    public void RankColorArray_TooFewSamplesReturnsNull()
    {
        // 20 x 20 / (1 * 1 * 100) = 4 samples per bin < 10
        Assert.Null(Pix.Create(20, 20, 32).GetRankColorArray(100, L.L_SELECT_RED, 1, null, 0));
    }

    [Fact]
    public void DisplayColorArray_InvalidFontsize()
    {
        Assert.Throws<ArgumentException>(() => Pix.DisplayColorArray(new uint[] { 0 }, 1, 20, 1, 3));
        Assert.Throws<ArgumentException>(() => Pix.DisplayColorArray(new uint[] { 0 }, 1, 20, 1, 22));
    }

    [Fact]
    public void RankBinByStrip_InvalidArguments()
    {
        Pix pix = Pix.Create(40, 40, 32);
        Assert.Throws<ArgumentException>(() => pix.RankBinByStrip(99, 5, 10, L.L_SELECT_RED));
        Assert.Throws<ArgumentException>(() => pix.RankBinByStrip(L.L_SCAN_HORIZONTAL, 0, 10, L.L_SELECT_RED));
        Assert.Throws<ArgumentException>(() => pix.RankBinByStrip(L.L_SCAN_HORIZONTAL, 5, 1, L.L_SELECT_RED));
        Assert.Throws<ArgumentException>(() => pix.RankBinByStrip(L.L_SCAN_HORIZONTAL, 5, 10, 99));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 40, 32).RankBinByStrip(L.L_SCAN_HORIZONTAL, 5, 5, L.L_SELECT_RED));
        Assert.Throws<ArgumentException>(() => Pix.Create(40, 40, 8).RankBinByStrip(L.L_SCAN_HORIZONTAL, 5, 10, L.L_SELECT_RED));
    }
}
