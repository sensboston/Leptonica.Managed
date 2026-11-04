using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Compare;

/// <summary>
/// Tests of compare.c on small synthetic images.  The expected values are worked out by hand
/// from the definitions in the C code.  The photo/text decision (pageseg.c), the plotting
/// (gplot.c), the translation search (correlscore.c, binreduce.c) and the color dilation
/// (colormorph.c) are not ported yet and are not covered here.
/// </summary>
public class CompareTests
{
    private static Pix MakeConst(int w, int h, int d, uint val)
    {
        Pix pix = Pix.Create(w, h, d);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                pix.SetPixel(j, i, val);
        return pix;
    }

    private static uint Rgb(int r, int g, int b)
    {
        Pix.ComposeRGBPixel(r, g, b, out uint p);
        return p;
    }

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    /* Two 4x4 gray images: 100 vs 90, except 150 at (1,1) in the second. */
    private static (Pix, Pix) GrayPair()
    {
        Pix a = MakeConst(4, 4, 8, 100);
        Pix b = MakeConst(4, 4, 8, 90);
        b.SetPixel(1, 1, 150);
        return (a, b);
    }

    [Fact]
    public void Equal_SameAndDifferent()
    {
        Pix a = MakeConst(5, 3, 8, 7);
        Pix b = MakeConst(5, 3, 8, 7);
        Assert.True(Pix.Equal(a, b));
        b.SetPixel(4, 2, 8);
        Assert.False(Pix.Equal(a, b));
        Assert.False(Pix.Equal(a, MakeConst(6, 3, 8, 7)));
    }

    [Fact]
    public void UsesCmapColor_GrayEntriesOnly()
    {
        Pix pix = MakeConst(4, 4, 8, 0);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(10, 10, 10);
        cmap.AddColor(200, 200, 200);
        pix.SetColormap(cmap);
        Assert.False(pix.UsesCmapColor());
    }

    [Fact]
    public void UsesCmapColor_ColorEntry()
    {
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(10, 10, 10);
        cmap.AddColor(200, 20, 20);
        Pix pix = MakeConst(4, 4, 8, 0);
        pix.SetColormap(cmap);
        /* upstream returns true for any colormap with a color entry (see UPSTREAM_NOTES.md) */
        Assert.True(pix.UsesCmapColor());
        pix.SetPixel(2, 2, 1);
        Assert.True(pix.UsesCmapColor());
    }

    [Fact]
    public void UsesCmapColor_NoColormap()
    {
        Assert.False(MakeConst(4, 4, 8, 3).UsesCmapColor());
    }

    [Fact]
    public void Equal_CmappedGrayAgainstPlainGray()
    {
        Pix pix1 = MakeConst(4, 4, 8, 1);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(10, 10, 10);
        cmap.AddColor(77, 77, 77);
        pix1.SetColormap(cmap);
        Pix pix2 = MakeConst(4, 4, 8, 77);
        Assert.True(Pix.Equal(pix1, pix2));
        Assert.True(Pix.Equal(pix2, pix1));
        pix2.SetPixel(0, 0, 78);
        Assert.False(Pix.Equal(pix1, pix2));
    }

    [Fact]
    public void CorrelationBinary_Values()
    {
        Pix p1 = Pix.Create(8, 8, 1);
        Pix p2 = Pix.Create(8, 8, 1);
        Assert.Equal(0.0f, Pix.CorrelationBinary(p1, p2));  /* both empty */
        foreach (var (x, y) in new[] { (1, 1), (2, 1), (3, 5), (6, 6) })
            p1.SetPixel(x, y, 1);
        Assert.Equal(0.0f, Pix.CorrelationBinary(p1, p2));  /* one empty */
        Assert.Equal(1.0f, Pix.CorrelationBinary(p1, Pix.Copy(null, p1)));
        p2.SetPixel(1, 1, 1);
        p2.SetPixel(3, 5, 1);
        /* |1 AND 2| = 2, |1| = 4, |2| = 2: 2 * 2 / (4 * 2) */
        Assert.Equal(0.5f, Pix.CorrelationBinary(p1, p2));
    }

    [Fact]
    public void CompareBinary_XorAndSubtract()
    {
        Pix p1 = Pix.Create(5, 4, 1);
        Pix p2 = Pix.Create(5, 4, 1);
        p1.SetPixel(0, 0, 1);
        p1.SetPixel(1, 0, 1);
        p1.SetPixel(2, 2, 1);
        p2.SetPixel(1, 0, 1);
        p2.SetPixel(4, 3, 1);
        p1.CompareBinary(p2, L.L_COMPARE_XOR, out float fract, out Pix diff);
        Assert.Equal(3.0f / 20.0f, fract);  /* (0,0), (2,2), (4,3) */
        Assert.Equal(3, diff.CountPixels(null));
        p1.CompareBinary(p2, L.L_COMPARE_SUBTRACT, out fract, out diff);
        Assert.Equal(2.0f / 20.0f, fract);  /* (0,0), (2,2) */
        Assert.Equal(1u, Get(diff, 0, 0));
        Assert.Equal(1u, Get(diff, 2, 2));
        Assert.Equal(0u, Get(diff, 4, 3));
    }

    [Fact]
    public void CompareBinary_InvalidType()
    {
        Pix p = Pix.Create(4, 4, 1);
        Assert.Throws<ArgumentException>(() => p.CompareBinary(p, 7, out _, out _));
        Pix g = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => g.CompareBinary(p, L.L_COMPARE_XOR, out _, out _));
    }

    [Fact]
    public void DisplayDiffBinary_Colors()
    {
        Pix p1 = Pix.Create(6, 3, 1);
        Pix p2 = Pix.Create(5, 4, 1);  /* the result is cropped to 5 x 3 */
        p1.SetPixel(0, 0, 1);  /* on in both */
        p2.SetPixel(0, 0, 1);
        p1.SetPixel(1, 1, 1);  /* only in pix1: red */
        p2.SetPixel(2, 2, 1);  /* only in pix2: green */
        Pix d = p1.DisplayDiffBinary(p2);
        d.GetDimensions(out int w, out int h, out int depth);
        Assert.Equal((5, 3, 4), (w, h, depth));
        PixColormap cmap = d.Colormap!;
        Assert.NotNull(cmap);
        Assert.Equal(4, cmap.Count);
        Assert.Equal(1u, Get(d, 0, 0));  /* black */
        Assert.Equal(2u, Get(d, 1, 1));  /* red */
        Assert.Equal(3u, Get(d, 2, 2));  /* green */
        Assert.Equal(0u, Get(d, 4, 0));  /* white */
    }

    [Fact]
    public void DisplayDiff_Rgb()
    {
        Pix a = MakeConst(4, 3, 32, Rgb(100, 100, 100));
        Pix b = Pix.Copy(null, a);
        b.SetPixel(2, 1, Rgb(100, 140, 100));  /* diff 40 in green */
        b.SetPixel(0, 0, Rgb(95, 100, 100));   /* diff 5 only */
        uint red = 0xff000000;
        Pix d = a.DisplayDiff(b, false, 10, red);
        d.GetDimensions(out int w, out int h, out int depth);
        Assert.Equal((4, 3, 32), (w, h, depth));
        Assert.Equal(red, Get(d, 2, 1));
        Assert.Equal(Rgb(100, 100, 100), Get(d, 0, 0));
        Assert.Throws<ArgumentException>(() => a.DisplayDiff(b, false, 0, red));
    }

    [Fact]
    public void CompareGray_AbsDiff()
    {
        var (a, b) = GrayPair();
        a.CompareGray(b, L.L_COMPARE_ABS_DIFF, 0, out bool same, out float diff,
                      out float rms, out Pix pixdiff);
        Assert.False(same);
        Assert.Equal(200.0f / 16.0f, diff);  /* 15 * 10 + 50 */
        Assert.Equal(MathF.Sqrt(250.0f), rms, 1e-3f);  /* (15 * 100 + 2500) / 16 */
        Assert.Equal(10u, Get(pixdiff, 0, 0));
        Assert.Equal(50u, Get(pixdiff, 1, 1));
    }

    [Fact]
    public void CompareGray_Subtract()
    {
        var (a, b) = GrayPair();
        a.CompareGray(b, L.L_COMPARE_SUBTRACT, 0, out bool same, out float diff,
                      out float rms, out Pix pixdiff);
        Assert.False(same);
        Assert.Equal(150.0f / 16.0f, diff);  /* 15 * 10, the pixel with b > a is clipped to 0 */
        Assert.Equal(0u, Get(pixdiff, 1, 1));
        /* the rms is computed from the absolute difference */
        Assert.Equal(MathF.Sqrt(250.0f), rms, 1e-3f);
    }

    [Fact]
    public void CompareGray_Identical()
    {
        Pix a = MakeConst(4, 4, 8, 33);
        a.CompareGray(Pix.Copy(null, a), L.L_COMPARE_ABS_DIFF, 0, out bool same,
                      out float diff, out float rms, out _);
        Assert.True(same);
        Assert.Equal(0.0f, diff);
        Assert.Equal(0.0f, rms);
    }

    [Fact]
    public void CompareGrayOrRGB_DifferentGrayDepths()
    {
        Pix p4 = MakeConst(4, 4, 4, 5);       /* 5 / 15 of full scale: 85 */
        Pix p8 = MakeConst(4, 4, 8, 85);
        p4.CompareGrayOrRGB(p8, L.L_COMPARE_ABS_DIFF, 0, out bool same, out float diff,
                            out _, out _);
        Assert.True(same);
        Assert.Equal(0.0f, diff);
        Assert.Throws<ArgumentException>(() =>
            MakeConst(4, 4, 1, 1).CompareGrayOrRGB(p8, L.L_COMPARE_ABS_DIFF, 0, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(() =>
            p4.CompareGrayOrRGB(p8, L.L_COMPARE_XOR, 0, out _, out _, out _, out _));
    }

    [Fact]
    public void CompareGrayOrRGB_GrayVersusRgbFails()
    {
        Pix p8 = MakeConst(4, 4, 8, 85);
        Pix p32 = MakeConst(4, 4, 32, Rgb(1, 2, 3));
        Assert.Throws<ArgumentException>(() =>
            p8.CompareGrayOrRGB(p32, L.L_COMPARE_ABS_DIFF, 0, out _, out _, out _, out _));
    }

    [Fact]
    public void CompareRGB_AbsDiffAndSubtract()
    {
        Pix a = MakeConst(2, 2, 32, Rgb(10, 20, 30));
        Pix b = MakeConst(2, 2, 32, Rgb(5, 25, 30));
        a.CompareRGB(b, L.L_COMPARE_ABS_DIFF, 0, out bool same, out float diff,
                     out float rms, out Pix pixdiff);
        Assert.False(same);
        Assert.Equal((float)((5.0f + 5.0f + 0.0f) / 3.0), diff);
        Assert.Equal((float)((5.0f + 5.0f + 0.0f) / 3.0), rms);  /* constant diffs: rms == mean */
        Assert.Equal(Rgb(5, 5, 0), Get(pixdiff, 1, 1));

        a.CompareRGB(b, L.L_COMPARE_SUBTRACT, 0, out same, out diff, out rms, out pixdiff);
        Assert.False(same);
        Assert.Equal((float)((5.0f + 0.0f + 0.0f) / 3.0), diff);
        Assert.Equal((float)((5.0f + 5.0f + 0.0f) / 3.0), rms);
        Assert.Equal(Rgb(5, 0, 0), Get(pixdiff, 0, 0));
    }

    [Fact]
    public void CompareRGB_Identical()
    {
        Pix a = MakeConst(3, 3, 32, Rgb(1, 2, 3));
        a.CompareRGB(Pix.Copy(null, a), L.L_COMPARE_ABS_DIFF, 0, out bool same, out float diff,
                     out float rms, out _);
        Assert.True(same);
        Assert.Equal(0.0f, diff);
        Assert.Equal(0.0f, rms);
    }

    [Fact]
    public void CompareTiled_Gray()
    {
        Pix a = MakeConst(8, 8, 8, 100);
        Pix b = MakeConst(8, 8, 8, 90);
        Pix d = a.CompareTiled(b, 4, 4, L.L_MEAN_ABSVAL);
        d.GetDimensions(out int w, out int h, out int depth);
        Assert.Equal((2, 2, 8), (w, h, depth));
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                Assert.Equal(10u, Get(d, j, i));
    }

    [Fact]
    public void CompareTiled_Rgb()
    {
        Pix a = MakeConst(8, 8, 32, Rgb(100, 50, 10));
        Pix b = MakeConst(8, 8, 32, Rgb(90, 40, 0));
        Pix d = a.CompareTiled(b, 4, 4, L.L_MEAN_ABSVAL);
        d.GetDimensions(out int w, out int h, out int depth);
        Assert.Equal((2, 2, 8), (w, h, depth));
        Assert.Equal(10u, Get(d, 0, 0));
        Assert.Equal(10u, Get(d, 1, 1));
    }

    [Fact]
    public void CompareTiled_InvalidArguments()
    {
        Pix a = MakeConst(8, 8, 8, 100);
        Assert.Throws<ArgumentException>(() => a.CompareTiled(a, 1, 4, L.L_MEAN_ABSVAL));
        Assert.Throws<ArgumentException>(() => a.CompareTiled(a, 4, 4, 99));
        Assert.Throws<ArgumentException>(() => a.CompareTiled(MakeConst(8, 8, 32, 0), 4, 4, L.L_MEAN_ABSVAL));
    }

    [Fact]
    public void GetDifferenceHistogram_Gray()
    {
        var (a, b) = GrayPair();
        Numa na = a.GetDifferenceHistogram(b, 1);
        Assert.Equal(256, na.Count);
        Assert.Equal(15.0f, na.GetFValue(10));
        Assert.Equal(1.0f, na.GetFValue(50));
        Assert.Equal(0.0f, na.GetFValue(0));
    }

    [Fact]
    public void GetDifferenceHistogram_RgbUsesMaxComponent()
    {
        Pix a = MakeConst(2, 2, 32, Rgb(10, 20, 30));
        Pix b = MakeConst(2, 2, 32, Rgb(5, 45, 30));
        Numa na = a.GetDifferenceHistogram(b, 1);
        Assert.Equal(4.0f, na.GetFValue(25));
    }

    [Fact]
    public void GetDifferenceHistogram_Subsampled()
    {
        var (a, b) = GrayPair();
        /* factor 2 samples (0,0), (2,0), (0,2), (2,2): none of them is the differing pixel */
        Numa na = a.GetDifferenceHistogram(b, 2);
        Assert.Equal(4.0f, na.GetFValue(10));
        Assert.Equal(0.0f, na.GetFValue(50));
    }

    [Fact]
    public void GetDifferenceHistogram_InvalidDepths()
    {
        Pix p16 = Pix.Create(4, 4, 16);
        Pix p8 = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => p16.GetDifferenceHistogram(p8, 1));
        Assert.Throws<ArgumentException>(() => MakeConst(4, 4, 4, 1).GetDifferenceHistogram(p8, 1));
    }

    [Fact]
    public void CompareRankDifference_Values()
    {
        var (a, b) = GrayPair();
        Numa rank = a.CompareRankDifference(b, 1);
        Assert.Equal(256, rank.Count);
        Assert.Equal(1.0f, rank.GetFValue(0));
        Assert.Equal(1.0f, rank.GetFValue(10));
        Assert.Equal(1.0f / 16.0f, rank.GetFValue(11), 1e-6f);
        Assert.Equal(0.0f, rank.GetFValue(51), 1e-6f);
    }

    [Fact]
    public void GetDifferenceStats_Values()
    {
        var (a, b) = GrayPair();
        a.GetDifferenceStats(b, 1, 20, out float fract, out float ave, 0);
        Assert.Equal(1.0f / 16.0f, fract);  /* only the pixel with diff 50 */
        Assert.Equal(30.0f, ave, 1e-4f);    /* 50 - mindiff */
        a.GetDifferenceStats(b, 1, 5, out fract, out ave, 0);
        Assert.Equal(1.0f, fract, 1e-6f);
        Assert.Equal((15 * 10 + 50) / 16.0f - 5, ave, 1e-4f);
        Assert.Throws<ArgumentException>(() => a.GetDifferenceStats(b, 1, 0, out _, out _, 0));
    }

    [Fact]
    public void TestForSimilarity_Thresholds()
    {
        var (a, b) = GrayPair();
        Assert.True(a.TestForSimilarity(b, 1, 20, 0.1f, 0.0f, 0));    /* maxave 0 is ignored */
        Assert.False(a.TestForSimilarity(b, 1, 20, 0.01f, 0.0f, 0));  /* 1/16 > 0.01 */
        Assert.False(a.TestForSimilarity(b, 1, 20, 0.1f, 10.0f, 0));  /* avediff 30 > 10 */
        Assert.True(a.TestForSimilarity(b, 1, 20, 0.1f, 30.0f, 0));
        Assert.Throws<ArgumentException>(() => a.TestForSimilarity(MakeConst(5, 4, 8, 0), 1, 20, 0.1f, 0.0f, 0));
        Assert.Throws<ArgumentException>(() => a.TestForSimilarity(b, 1, 0, 0.1f, 0.0f, 0));
    }

    [Fact]
    public void GetPSNR_Values()
    {
        var (a, b) = GrayPair();
        float psnr = a.GetPSNR(b, 1);
        double expected = -4.3429448 * Math.Log(250.0 / (255.0 * 255.0));
        Assert.Equal(expected, psnr, 1e-3);
        Assert.Equal(1000.0f, a.GetPSNR(Pix.Copy(null, a), 1));
        Assert.Throws<ArgumentException>(() => a.GetPSNR(b, 0));
        Assert.Throws<ArgumentException>(() => a.GetPSNR(MakeConst(4, 5, 8, 0), 1));
    }

    [Fact]
    public void GetPSNR_Rgb()
    {
        Pix a = MakeConst(2, 2, 32, Rgb(10, 20, 30));
        Pix b = MakeConst(2, 2, 32, Rgb(13, 20, 30));
        /* mse = 9 / 3 per pixel */
        double expected = -4.3429448 * Math.Log(3.0 / (255.0 * 255.0));
        Assert.Equal(expected, a.GetPSNR(b, 1), 1e-3);
    }

    [Fact]
    public void GetPerceptualDiff_Gray()
    {
        Pix black = MakeConst(8, 8, 8, 0);
        Pix white = MakeConst(8, 8, 8, 255);
        Assert.True(black.GetPerceptualDiff(Pix.Copy(null, black), 1, 3, 50, out float fract,
                                            out Pix? d1, out Pix? d2));
        Assert.Equal(0.0f, fract);
        Assert.NotNull(d1);
        Assert.NotNull(d2);
        Assert.True(black.GetPerceptualDiff(white, 1, 3, 50, out fract, out d1, out d2));
        Assert.Equal(1.0f, fract);
        Assert.Equal(255u, Get(d1!, 3, 3));
        Assert.Equal(1, d2!.Depth);
        Assert.Throws<ArgumentException>(() =>
            black.GetPerceptualDiff(white, 1, 4, 50, out _, out _, out _));  /* even dilation */
    }

    [Fact]
    public void GetPerceptualDiff_DepthMismatch()
    {
        Pix g = MakeConst(8, 8, 8, 0);
        Pix c = MakeConst(8, 8, 32, 0);
        Assert.False(g.GetPerceptualDiff(c, 1, 3, 50, out float fract, out Pix? d1, out Pix? d2));
        Assert.Equal(1.0f, fract);
        Assert.Null(d1);
        Assert.Null(d2);
    }

    [Fact]
    public void Centroid8_SingleDarkPixelAndWhite()
    {
        Pix pix = MakeConst(8, 6, 8, 255);
        pix.Centroid8(1, out float cx, out float cy);
        Assert.Equal((4.0f, 3.0f), (cx, cy));  /* white: the center */
        pix.SetPixel(3, 2, 0);
        pix.Centroid8(1, out cx, out cy);
        Assert.Equal((3.0f, 2.0f), (cx, cy));
        Assert.Throws<ArgumentException>(() => pix.Centroid8(0, out _, out _));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 1).Centroid8(1, out _, out _));
    }

    [Fact]
    public void PadToCenterCentroid_CentersTheDot()
    {
        Pix pix = MakeConst(8, 6, 8, 255);
        pix.SetPixel(1, 1, 0);
        Pix d = pix.PadToCenterCentroid(1);
        d.GetDimensions(out int w, out int h, out int depth);
        Assert.Equal((14, 10, 8), (w, h, depth));
        Assert.Equal(0u, Get(d, 7, 5));
        Assert.Equal(255u, Get(d, 0, 0));
        d.Centroid8(1, out float cx, out float cy);
        Assert.Equal((7.0f, 5.0f), (cx, cy));
    }

    [Fact]
    public void CropAlignedToCentroid_Boxes()
    {
        Pix p1 = MakeConst(10, 10, 8, 255);
        Pix p2 = MakeConst(10, 10, 8, 255);
        p1.SetPixel(2, 3, 0);
        p2.SetPixel(5, 4, 0);
        p1.CropAlignedToCentroid(p2, 1, out Box box1, out Box box2);
        box1.GetGeometry(out int x1, out int y1, out int w1, out int h1);
        box2.GetGeometry(out int x2, out int y2, out int w2, out int h2);
        Assert.Equal((0, 0, 7, 9), (x1, y1, w1, h1));
        Assert.Equal((3, 1, 7, 9), (x2, y2, w2, h2));
        Assert.Throws<ArgumentException>(() => p1.CropAlignedToCentroid(p2, 0, out _, out _));
    }

    private static Numa Ramp()
    {
        Numa na = Numa.Create(256);
        for (int j = 0; j < 256; j++)
            na.AddNumber(j);
        return na;
    }

    [Fact]
    public void CompressGrayHistograms_RoundTrip()
    {
        Numaa naa = Numaa.Create(2);
        naa.AddNuma(Ramp(), L.L_INSERT);
        Numa na2 = Numa.Create(256);
        for (int j = 0; j < 256; j++)
            na2.AddNumber(j == 100 ? 255 : 0);
        naa.AddNuma(na2, L.L_INSERT);

        byte[] bytea = Pix.CompressGrayHistograms(naa, 300, 200);
        Assert.Equal(8 + 2 * 256, bytea.Length);
        Assert.Equal(new byte[] { 44, 1, 0, 0, 200, 0, 0, 0 }, bytea[..8]);
        Assert.Equal(7, bytea[8 + 7]);
        Assert.Equal(255, bytea[8 + 255]);
        Assert.Equal(255, bytea[8 + 256 + 100]);

        Numaa back = Pix.UncompressGrayHistograms(bytea, out int w, out int h);
        Assert.Equal((300, 200), (w, h));
        Assert.Equal(2, back.Count);
        Numa b0 = back.GetNuma(0, L.L_CLONE);
        Numa b1 = back.GetNuma(1, L.L_CLONE);
        for (int j = 0; j < 256; j++) {
            Assert.Equal((float)j, b0.GetFValue(j));
            Assert.Equal(j == 100 ? 255.0f : 0.0f, b1.GetFValue(j));
        }
    }

    [Fact]
    public void CompressGrayHistograms_NormalizesToMax255()
    {
        Numaa naa = Numaa.Create(1);
        Numa na = Numa.Create(256);
        for (int j = 0; j < 256; j++)
            na.AddNumber(j == 3 ? 10 : 0);  /* max 10: scaled by 25.5 to 255 */
        naa.AddNuma(na, L.L_INSERT);
        byte[] bytea = Pix.CompressGrayHistograms(naa, 1, 1);
        Assert.Equal(255, bytea[8 + 3]);
        Assert.Equal(0, bytea[8 + 4]);
    }

    [Fact]
    public void CompressGrayHistograms_RejectsWrongSize()
    {
        Numaa naa = Numaa.Create(1);
        naa.AddNuma(Numa.Create(10), L.L_INSERT);
        Assert.Throws<LeptonicaException>(() => Pix.CompressGrayHistograms(naa, 1, 1));
        Assert.Throws<ArgumentException>(() => Pix.UncompressGrayHistograms(new byte[8 + 100], out _, out _));
    }

    private static Numa Spike(int pos)
    {
        Numa na = Numa.Create(256);
        for (int j = 0; j < 256; j++)
            na.AddNumber(j == pos ? 255 : 0);
        return na;
    }

    private static Numaa OneTile(Numa na)
    {
        Numaa naa = Numaa.Create(1);
        naa.AddNuma(na, L.L_INSERT);
        return naa;
    }

    [Fact]
    public void CompareTilesByHisto_IdenticalAndFarApart()
    {
        float same = Pix.CompareTilesByHisto(OneTile(Spike(100)), OneTile(Spike(100)),
                                             0.9f, 50, 50, 50, 50, null);
        Assert.Equal(1.0f, same);
        /* spikes 100 apart: EMD is large, so the score is clipped at 0 */
        float far = Pix.CompareTilesByHisto(OneTile(Spike(10)), OneTile(Spike(200)),
                                            0.9f, 50, 50, 50, 50, null);
        Assert.Equal(0.0f, far);
        float near = Pix.CompareTilesByHisto(OneTile(Spike(100)), OneTile(Spike(101)),
                                             0.9f, 50, 50, 50, 50, null);
        Assert.InRange(near, 0.9f, 1.0f);
    }

    [Fact]
    public void CompareTilesByHisto_SizeFilters()
    {
        Numaa a = OneTile(Spike(100));
        Numaa b = OneTile(Spike(100));
        Assert.Equal(0.0f, Pix.CompareTilesByHisto(a, b, 0.9f, 100, 50, 50, 50, null));
        Assert.Equal(0.0f, Pix.CompareTilesByHisto(a, b, 0.9f, 50, 100, 50, 50, null));
        Assert.Equal(1.0f, Pix.CompareTilesByHisto(a, b, 0.5f, 50, 50, 50, 50, null));
        Numaa two = Numaa.Create(2);
        two.AddNuma(Spike(100), L.L_INSERT);
        two.AddNuma(Spike(100), L.L_INSERT);
        Assert.Equal(0.0f, Pix.CompareTilesByHisto(a, two, 0.9f, 50, 50, 50, 50, null));
    }

    [Fact]
    public void CompareGrayByHisto_IdenticalImagesScoreOne()
    {
        Pix pix = Pix.Create(60, 60, 8);
        for (int i = 0; i < 60; i++)
            for (int j = 0; j < 60; j++)
                pix.SetPixel(j, i, (uint)((i * 3 + j * 2) % 220));
        float score = pix.CompareGrayByHisto(Pix.Copy(null, pix), null, null, 0.9f, 255, 1, 3, false);
        Assert.Equal(1.0f, score);
        Assert.Throws<ArgumentException>(() => pix.CompareGrayByHisto(pix, null, null, 0.4f, 255, 1, 3, false));
        Assert.Throws<ArgumentException>(() => pix.CompareGrayByHisto(pix, null, null, 0.9f, 100, 1, 3, false));
    }
}
