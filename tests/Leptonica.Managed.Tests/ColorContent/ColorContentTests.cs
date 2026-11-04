using System;
using Leptonica;

namespace Leptonica.Managed.Tests.ColorContentTests;

public class ColorContentTests
{
    private static Pix MakeRgb(int w, int h, Func<int, int, (int r, int g, int b)> color)
    {
        Pix pix = Pix.Create(w, h, 32);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) {
                var (r, g, b) = color(x, y);
                Pix.ComposeRGBPixel(r, g, b, out uint pixel);
                pix.SetPixel(x, y, pixel);
            }
        return pix;
    }

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    private static (int r, int g, int b) Rgb(Pix pix, int x, int y)
    {
        Pix.ExtractRGBValues(Get(pix, x, y), out int r, out int g, out int b);
        return (r, g, b);
    }

    [Fact]
    public void ColorContent_ComputesDiffFromOtherTwo()
    {
        Pix pix = MakeRgb(2, 1, (x, y) => x == 0 ? (0, 0, 255) : (0, 127, 255));
        pix.ColorContent(0, 0, 0, 0, out Pix pr, out Pix pg, out Pix pb);
        // (a) r=g=0, b=255: rgdiff=0, rbdiff=255, gbdiff=255
        Assert.Equal(127u, Get(pr, 0, 0));
        Assert.Equal(127u, Get(pg, 0, 0));
        Assert.Equal(255u, Get(pb, 0, 0));
        // (b) r=0, g=127, b=255: rgdiff=127, rbdiff=255, gbdiff=128
        Assert.Equal(191u, Get(pr, 1, 0));
        Assert.Equal(127u, Get(pg, 1, 0));
        Assert.Equal(191u, Get(pb, 1, 0));
    }

    [Fact]
    public void ColorContent_MingrayFiltersDarkPixels()
    {
        Pix pix = MakeRgb(2, 1, (x, y) => x == 0 ? (0, 0, 40) : (0, 0, 200));
        pix.ColorContent(0, 0, 0, 100, out Pix pr, out _, out Pix pb);
        Assert.Equal(0u, Get(pb, 0, 0));
        Assert.Equal(0u, Get(pr, 0, 0));
        Assert.Equal(200u, Get(pb, 1, 0));
        Assert.Throws<ArgumentException>(() => pix.ColorContent(0, 0, 0, 256, out _, out _, out _));
    }

    [Theory]
    [InlineData(L.L_INTERMED_DIFF, 128)]   // r=0,g=127,b=255: diffs 127, 255, 128 -> intermediate
    [InlineData(L.L_AVE_MAX_DIFF_2, 192)]  // b - (r + g) / 2 = 255 - 63
    [InlineData(L.L_MAX_DIFF, 255)]
    public void ColorMagnitude_Types(int type, int expected)
    {
        Pix pix = MakeRgb(1, 1, (x, y) => (0, 127, 255));
        Pix d = pix.ColorMagnitude(0, 0, 0, type);
        Assert.Equal((uint)expected, Get(d, 0, 0));
    }

    [Fact]
    public void ColorMagnitude_RejectsBadType()
    {
        Pix pix = MakeRgb(1, 1, (x, y) => (1, 2, 3));
        Assert.Throws<ArgumentException>(() => pix.ColorMagnitude(0, 0, 0, 9));
    }

    [Fact]
    public void ColorFraction_CountsColorfulPixels()
    {
        // 4x4: 4 white, 4 black, 4 gray (128), 4 red
        Pix pix = MakeRgb(4, 4, (x, y) => y switch {
            0 => (255, 255, 255), 1 => (0, 0, 0), 2 => (128, 128, 128), _ => (200, 30, 30) });
        pix.ColorFraction(20, 244, 60, 1, out float pixfract, out float colorfract);
        Assert.Equal(0.5f, pixfract);
        Assert.Equal(0.5f, colorfract);
    }

    [Fact]
    public void ColorFraction_NothingToConsider_ReturnsZeros()
    {
        Pix pix = MakeRgb(3, 3, (x, y) => (255, 255, 255));
        pix.ColorFraction(20, 244, 60, 1, out float pixfract, out float colorfract);
        Assert.Equal(0f, pixfract);
        Assert.Equal(0f, colorfract);
        Assert.Throws<ArgumentException>(() =>
            Pix.Create(3, 3, 8).ColorFraction(20, 244, 60, 1, out _, out _));
    }

    [Fact]
    public void ColorShiftWhitePoint_MapsRefToWhite()
    {
        Pix pix = MakeRgb(2, 1, (x, y) => x == 0 ? (200, 100, 50) : (0, 0, 0));
        Pix d = pix.ColorShiftWhitePoint(200, 100, 50);
        Assert.Equal((255, 255, 255), Rgb(d, 0, 0));
        Assert.Equal((0, 0, 0), Rgb(d, 1, 0));
    }

    [Fact]
    public void ColorShiftWhitePoint_NoRefOrInvalidRef_ReturnsSameImage()
    {
        Pix pix = MakeRgb(2, 1, (x, y) => (10, 20, 30));
        Assert.Same(pix, pix.ColorShiftWhitePoint(0, 0, 0));
        Assert.Same(pix, pix.ColorShiftWhitePoint(100, 0, 100));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 8).ColorShiftWhitePoint(1, 1, 1));
    }

    [Fact]
    public void MaskOverColorPixels_MarksColorPixels()
    {
        Pix pix = MakeRgb(4, 1, (x, y) => x < 2 ? (200, 50, 50) : (100, 100, 100));
        Pix m = pix.MaskOverColorPixels(60, 1);
        Assert.Equal(1, m.Depth);
        Assert.Equal(new uint[] { 1, 1, 0, 0 },
                     new[] { Get(m, 0, 0), Get(m, 1, 0), Get(m, 2, 0), Get(m, 3, 0) });
    }

    [Fact]
    public void MaskOverColorPixels_MindistErodesMask()
    {
        Pix pix = MakeRgb(5, 5, (x, y) => (200, 50, 50));
        Pix m = pix.MaskOverColorPixels(60, 2);   // 3x3 erosion
        Assert.Equal(1u, Get(m, 2, 2));
        Assert.Equal(1u, Get(m, 1, 1));
    }

    [Fact]
    public void MaskOverGrayPixels_SelectsDarkGray()
    {
        Pix pix = MakeRgb(3, 1, (x, y) => x switch { 0 => (100, 100, 100), 1 => (240, 240, 240), _ => (150, 100, 50) });
        Pix m = pix.MaskOverGrayPixels(220, 10);
        Assert.Equal(new uint[] { 1, 0, 0 }, new[] { Get(m, 0, 0), Get(m, 1, 0), Get(m, 2, 0) });
        Assert.Throws<ArgumentException>(() => pix.MaskOverGrayPixels(256, 10));
        Assert.Throws<ArgumentException>(() => pix.MaskOverGrayPixels(100, 0));
    }

    [Fact]
    public void MaskOverColorRange_SelectsCube()
    {
        Pix pix = MakeRgb(3, 1, (x, y) => x switch { 0 => (10, 20, 30), 1 => (10, 20, 90), _ => (200, 20, 30) });
        Pix m = pix.MaskOverColorRange(0, 50, 0, 50, 0, 50);
        Assert.Equal(new uint[] { 1, 0, 0 }, new[] { Get(m, 0, 0), Get(m, 1, 0), Get(m, 2, 0) });
    }

    [Fact]
    public void NumSignificantGrayColors_CountsLevels()
    {
        Pix pix = Pix.Create(10, 10, 8);
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
                pix.SetPixel(x, y, (uint)(x < 5 ? 100 : 150));
        // two significant levels plus black and white
        Assert.Equal(4, pix.NumSignificantGrayColors(20, 236, 0.05f, 1));
        Assert.Throws<ArgumentException>(() => pix.NumSignificantGrayColors(200, 100, 0.05f, 1));
        Assert.Throws<ArgumentException>(() => pix.NumSignificantGrayColors(20, 236, 1.5f, 1));
    }

    [Fact]
    public void NumColors_Gray()
    {
        Pix pix = Pix.Create(4, 1, 8);
        for (int x = 0; x < 4; x++)
            pix.SetPixel(x, 0, (uint)(x % 3));
        Assert.Equal(3, pix.NumColors(1));
        Pix pix2 = Pix.Create(4, 1, 2);
        pix2.SetPixel(1, 0, 3);
        Assert.Equal(2, pix2.NumColors(1));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 1).NumColors(1));
    }

    [Fact]
    public void NumColors_Rgb_FewColors()
    {
        Pix few = MakeRgb(10, 1, (x, y) => (x % 4, 0, 0));
        Assert.Equal(4, few.NumColors(1));
    }

    [Fact]
    public void NumColors_Rgb_ManyColors()
    {
        Pix many = MakeRgb(20, 20, (x, y) => (x * 10, y * 10, 7));
        Assert.Equal(400, many.NumColors(1));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(9, 4)]
    [InlineData(40, 8)]
    public void ConvertRGBToCmapLossless_ChoosesDepth(int ncolors, int depth)
    {
        Pix pix = MakeRgb(ncolors, 1, (x, y) => (x * 5, 255 - x, x));
        Pix d = pix.ConvertRGBToCmapLossless()!;
        Assert.Equal(depth, d.Depth);
        Assert.Equal(ncolors, d.Colormap!.Count);
        for (int x = 0; x < ncolors; x++) {
            uint idx = Get(d, x, 0);
            d.Colormap.GetColor((int)idx, out int r, out int g, out int b);
            Assert.Equal((x * 5, 255 - x, x), (r, g, b));
        }
    }

    [Fact]
    public void ConvertRGBToCmapLossless_TooManyColors_ReturnsNull()
    {
        Pix pix = MakeRgb(20, 20, (x, y) => (x * 10, y * 10, 7));
        Assert.Null(pix.ConvertRGBToCmapLossless());
    }

    [Fact]
    public void MakeRGBIndexTables_And_GetRGBFromIndex_AreConsistent()
    {
        for (int sigbits = 2; sigbits <= 6; sigbits++) {
            L.MakeRGBIndexTables(out uint[] rt, out uint[] gt, out uint[] bt, sigbits);
            foreach (var (r, g, b) in new[] { (255, 0, 128), (13, 200, 77), (0, 0, 0), (255, 255, 255) }) {
                uint index = rt[r] | gt[g] | bt[b];
                Assert.True(index < (1u << (3 * sigbits)));
                L.GetRGBFromIndex(index, sigbits, out int rv, out int gv, out int bv);
                int shift = 8 - sigbits;
                int half = 1 << (shift - 1);
                Assert.Equal(((r >> shift) << shift) | half, rv);
                Assert.Equal(((g >> shift) << shift) | half, gv);
                Assert.Equal(((b >> shift) << shift) | half, bv);
            }
        }
        Assert.Throws<ArgumentException>(() => L.MakeRGBIndexTables(out _, out _, out _, 1));
        Assert.Throws<ArgumentException>(() => L.GetRGBFromIndex(0, 7, out _, out _, out _));
    }

    [Fact]
    public void GetRGBHistogram_CountsByIndex()
    {
        Pix pix = MakeRgb(4, 1, (x, y) => x < 3 ? (255, 0, 0) : (0, 0, 255));
        Numa na = pix.GetRGBHistogram(2, 1);
        Assert.Equal(64, na.Count);
        L.MakeRGBIndexTables(out uint[] rt, out uint[] gt, out uint[] bt, 2);
        Assert.Equal(3, na.GetIValue((int)(rt[255] | gt[0] | bt[0])));
        Assert.Equal(1, na.GetIValue((int)(rt[0] | gt[0] | bt[255])));
    }

    [Fact]
    public void GetMostPopulatedColors_OrdersByPopulation()
    {
        Pix pix = MakeRgb(10, 1, (x, y) => x < 6 ? (255, 0, 0) : x < 9 ? (0, 255, 0) : (0, 0, 255));
        pix.GetMostPopulatedColors(2, 1, 2, out uint[] colors, out PixColormap cmap);
        Assert.Equal(2, colors.Length);
        Assert.Equal(2, cmap.Count);
        Pix.ExtractRGBValues(colors[0], out int r, out int g, out int b);
        Assert.Equal((224, 32, 32), (r, g, b));
        Pix.ExtractRGBValues(colors[1], out r, out g, out b);
        Assert.Equal((32, 224, 32), (r, g, b));
        Assert.Throws<ArgumentException>(() => pix.GetMostPopulatedColors(7, 1, 2, out _, out _));
        Assert.Throws<ArgumentException>(() => pix.GetMostPopulatedColors(2, 1, 0, out _, out _));
    }

    [Fact]
    public void SimpleColorQuantize_RejectsBadSigbits()
    {
        Pix pix = MakeRgb(4, 4, (x, y) => (x, y, 0));
        Assert.Throws<ArgumentException>(() => pix.SimpleColorQuantize(5, 1, 4));
    }

    [Fact]
    public void SimpleColorQuantize_MakesCmappedImage()
    {
        Pix pix = MakeRgb(10, 10, (x, y) => x < 5 ? (255, 0, 0) : (0, 0, 255));
        Pix d = pix.SimpleColorQuantize(3, 1, 2);
        Assert.Equal(8, d.Depth);
        Assert.Equal(2, d.Colormap!.Count);
    }

    [Fact]
    public void HasHighlightRed_DetectsRed()
    {
        Pix pix = MakeRgb(20, 20, (x, y) => x < 2 ? (230, 20, 20) : (250, 250, 250));
        pix.HasHighlightRed(1, 0.01f, 2.5f, out bool hasred, out float ratio, out Pix _);
        Assert.True(hasred);
        Assert.True(ratio >= 1.0f);
    }

    [Fact]
    public void ColorsForQuantization_GrayAndColormap()
    {
        Pix gray = Pix.Create(100, 100, 8);
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 100; x++)
                gray.SetPixel(x, y, 128);
        gray.ColorsForQuantization(0, out int n, out bool iscolor, 0);
        Assert.False(iscolor);
        Assert.Equal(3, n);   // one gray level plus black and white

        Pix cm = Pix.Create(4, 4, 8);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(10, 20, 30);
        cmap.AddColor(40, 50, 60);
        cm.SetColormap(cmap);
        cm.ColorsForQuantization(0, out n, out iscolor, 0);
        Assert.Equal(2, n);
        Assert.True(iscolor);

        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 16).ColorsForQuantization(0, out _, out _, 0));
    }

    [Fact]
    public void ColorsForQuantization_RgbWithoutColorIsGray()
    {
        Pix pix = MakeRgb(100, 100, (x, y) => (128, 128, 128));
        pix.ColorsForQuantization(0, out int n, out bool iscolor, 0);
        Assert.False(iscolor);
        Assert.Equal(3, n);
    }

    [Fact]
    public void FindColorRegions_FindsBlueRegionOnLightBackground()
    {
        // light, slightly reddish background with a blue block in the middle
        Pix pix = MakeRgb(100, 100, (x, y) =>
            x >= 30 && x < 70 && y >= 30 && y < 70 ? (30, 60, 200) : (250, 245, 245));
        pix.FindColorRegions(null, 1, -1, -1, -1, -1, 0.05f,
                             out float colorfract, out Pix? mask1, out Pix? mask2, null);
        Assert.True(colorfract > 0.1f && colorfract < 0.2f);
        Assert.NotNull(mask1);
        Assert.NotNull(mask2);
        Assert.Equal(1, mask1!.Depth);
    }

    [Fact]
    public void FindColorRegions_DarkBackground_ReturnsZero()
    {
        Pix pix = MakeRgb(50, 50, (x, y) => (100, 100, 100));
        pix.FindColorRegions(null, 1, -1, -1, -1, -1, 0.05f,
                             out float colorfract, out Pix? mask1, out Pix? mask2, null);
        Assert.Equal(0f, colorfract);
        Assert.Null(mask1);
        Assert.Null(mask2);
    }
}
