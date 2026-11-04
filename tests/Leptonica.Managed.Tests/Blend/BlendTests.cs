using System;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.BlendTests;

/// <summary>Tests of blend.c: small images with hand-computed expectations.</summary>
public class BlendTests
{
    private static Pix Gray(int w, int h, uint val)
    {
        Pix pix = Pix.Create(w, h, 8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Naive.SetPixel(pix, x, y, val);
        return pix;
    }

    private static uint Rgb(int r, int g, int b)
    {
        Pix.ComposeRGBPixel(r, g, b, out uint val);
        return val;
    }

    private static Pix Color(int w, int h, int r, int g, int b)
    {
        Pix pix = Pix.Create(w, h, 32);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Naive.SetPixel(pix, x, y, Rgb(r, g, b));
        return pix;
    }

    private static Pix Mask(int w, int h, params (int x, int y)[] on)
    {
        Pix pix = Pix.Create(w, h, 1);
        foreach (var (x, y) in on)
            Naive.SetPixel(pix, x, y, 1);
        return pix;
    }

    private static void AssertPixel(Pix pix, int x, int y, uint expected)
    {
        Assert.True(expected == Naive.GetPixel(pix, x, y),
            $"pixel ({x},{y}): expected {expected:x} got {Naive.GetPixel(pix, x, y):x}");
    }

    private static void AssertRgb(Pix pix, int x, int y, int r, int g, int b)
    {
        Pix.ExtractRGBValues(Naive.GetPixel(pix, x, y), out int pr, out int pg, out int pb);
        Assert.True(r == pr && g == pg && b == pb,
            $"pixel ({x},{y}): expected ({r},{g},{b}) got ({pr},{pg},{pb})");
    }

    /*------------------------------ pixBlendMask ------------------------------*/

    [Fact]
    public void BlendMask_Gray_Values()
    {
        // Mask ON at (0,0) and (1,1); placed at (1,1) in a 4x4 image: dest pixels (1,1) and (2,2).
        Pix mask = Mask(2, 2, (0, 0), (1, 1));
        foreach (var (type, expected) in new[] { (L.L_BLEND_TO_BLACK, 50u), (L.L_BLEND_TO_WHITE, 177u),
                                                  (L.L_BLEND_WITH_INVERSE, 127u) }) {
            Pix pixd = Pix.BlendMask(null, Gray(4, 4, 100), mask, 1, 1, 0.5f, type);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++) {
                    bool on = (x == 1 && y == 1) || (x == 2 && y == 2);
                    AssertPixel(pixd, x, y, on ? expected : 100);
                }
        }
    }

    [Fact]
    public void BlendMask_Gray_InverseOfBrightPixelsWraps()
    {
        // NOTE(upstream): pixval is l_uint32, so 255 - 2 * pixval wraps for pixval > 127.
        // pixval = 200, fract = 0.5: float(4294967151) = 4294967040; * 0.5 = 2147483520;
        // + 200 rounds to 2^31, which converts to INT_MIN (x86); its low byte is 0.
        Pix mask = Mask(1, 1, (0, 0));
        Pix pixd = Pix.BlendMask(null, Gray(2, 2, 200), mask, 0, 0, 0.5f, L.L_BLEND_WITH_INVERSE);
        AssertPixel(pixd, 0, 0, 0);
        AssertPixel(pixd, 1, 1, 200);
        // fract = 0.25: 1073741760 + 200 rounds to 1073741952 = 0x40000080; low byte 0x80.
        pixd = Pix.BlendMask(null, Gray(2, 2, 200), mask, 0, 0, 0.25f, L.L_BLEND_WITH_INVERSE);
        AssertPixel(pixd, 0, 0, 0x80);
    }

    [Fact]
    public void BlendMask_Rgb()
    {
        Pix mask = Mask(1, 1, (0, 0));
        Pix src = Color(2, 2, 100, 200, 0);
        Pix inv = Pix.BlendMask(null, src, mask, 0, 0, 0.5f, L.L_BLEND_WITH_INVERSE);
        AssertRgb(inv, 0, 0, 127, 127, 127);   // c + 0.5 * (255 - 2c): 127.5, 127.5 (200 - 72.5 ... see below), 127.5
        AssertRgb(inv, 1, 1, 100, 200, 0);
        Pix white = Pix.BlendMask(null, src, mask, 0, 0, 0.5f, L.L_BLEND_TO_WHITE);
        AssertRgb(white, 0, 0, 177, 227, 127);
        Pix black = Pix.BlendMask(null, src, mask, 0, 0, 0.5f, L.L_BLEND_TO_BLACK);
        AssertRgb(black, 0, 0, 50, 100, 0);
    }

    [Fact]
    public void BlendMask_InPlaceAndClipping()
    {
        Pix pixs = Gray(4, 4, 100);
        Pix mask = Mask(3, 3, (0, 0), (2, 2), (1, 1));
        Pix pixd = Pix.BlendMask(pixs, pixs, mask, -1, -1, 0.5f, L.L_BLEND_TO_BLACK);
        Assert.Same(pixs, pixd);
        // Mask (0,0) falls outside; (1,1) -> (0,0); (2,2) -> (1,1).
        AssertPixel(pixs, 0, 0, 50);
        AssertPixel(pixs, 1, 1, 50);
        AssertPixel(pixs, 2, 2, 100);
    }

    [Fact]
    public void BlendMask_DefaultsAndErrors()
    {
        Pix mask = Mask(1, 1, (0, 0));
        // Invalid fract -> 0.5; invalid type -> WITH_INVERSE.
        Pix pixd = Pix.BlendMask(null, Gray(2, 2, 100), mask, 0, 0, 2.0f, 77);
        AssertPixel(pixd, 0, 0, 127);
        Assert.Throws<ArgumentException>(() => Pix.BlendMask(null, Pix.Create(2, 2, 1), mask, 0, 0, 0.5f, 1));
        Assert.Throws<ArgumentException>(() => Pix.BlendMask(null, Gray(2, 2, 1), Gray(1, 1, 1), 0, 0, 0.5f, 1));
        Pix other = Gray(2, 2, 1);
        Assert.Throws<ArgumentException>(() => Pix.BlendMask(other, Gray(2, 2, 1), mask, 0, 0, 0.5f, 1));
    }

    [Fact]
    public void BlendMask_RemovesColormapAndUnpacks()
    {
        Pix pix4 = Pix.Create(2, 2, 4);
        Naive.SetPixel(pix4, 0, 0, 8);
        Pix mask = Mask(1, 1, (0, 0));
        Pix pixd = Pix.BlendMask(null, pix4, mask, 0, 0, 0.5f, L.L_BLEND_TO_BLACK);
        Assert.Equal(8, pixd.Depth);
        Assert.Null(pixd.Colormap);
    }

    /*------------------------------ pixBlendGray ------------------------------*/

    [Fact]
    public void BlendGray_Gray8()
    {
        Pix pixd = Pix.BlendGray(null, Gray(4, 4, 100), Gray(2, 2, 200), 1, 1, 0.5f, L.L_BLEND_GRAY, 0, 0);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                AssertPixel(pixd, x, y, (x >= 1 && x <= 2 && y >= 1 && y <= 2) ? 150u : 100u);
    }

    [Fact]
    public void BlendGray_TransparentValueIsSkipped()
    {
        Pix blender = Gray(2, 1, 200);
        Naive.SetPixel(blender, 1, 0, 40);
        Pix pixd = Pix.BlendGray(null, Gray(2, 1, 100), blender, 0, 0, 0.5f, L.L_BLEND_GRAY, 1, 40);
        AssertPixel(pixd, 0, 0, 150);
        AssertPixel(pixd, 1, 0, 100);
    }

    [Fact]
    public void BlendGray_Rgb()
    {
        Pix src = Pix.Create(2, 1, 32);
        Naive.SetPixel(src, 0, 0, Rgb(100, 150, 200));
        Pix pixd = Pix.BlendGray(null, src, Gray(1, 1, 100), 0, 0, 0.5f, L.L_BLEND_GRAY, 0, 0);
        AssertRgb(pixd, 0, 0, 100, 125, 150);
        AssertRgb(pixd, 1, 0, 0, 0, 0);
    }

    [Fact]
    public void BlendGray_WithInverse()
    {
        // delta = (128 - 100) * (255 - c) / 256 with integer division.
        Pix pixd = Pix.BlendGray(null, Gray(2, 1, 100), Gray(2, 1, 0), 0, 0, 1.0f, L.L_BLEND_GRAY_WITH_INVERSE, 0, 0);
        AssertPixel(pixd, 0, 0, 127);   // delta = 28 * 255 / 256 = 27; + (int)(27.5) = 27
        pixd = Pix.BlendGray(null, Gray(2, 1, 100), Gray(2, 1, 255), 0, 0, 1.0f, L.L_BLEND_GRAY_WITH_INVERSE, 0, 0);
        AssertPixel(pixd, 0, 0, 100);   // white blender: delta 0
        Pix src = Pix.Create(1, 1, 32);
        Naive.SetPixel(src, 0, 0, Rgb(100, 128, 200));
        pixd = Pix.BlendGray(null, src, Gray(1, 1, 0), 0, 0, 1.0f, L.L_BLEND_GRAY_WITH_INVERSE, 0, 0);
        AssertRgb(pixd, 0, 0, 127, 128, 200 + (int)(1.0 * ((128 - 200) * 255 / 256) + 0.5));
    }

    [Fact]
    public void BlendGray_ErrorsAndUnpacking()
    {
        Assert.Throws<ArgumentException>(() =>
            Pix.BlendGray(null, Pix.Create(2, 2, 1), Gray(1, 1, 1), 0, 0, 0.5f, L.L_BLEND_GRAY, 0, 0));
        Pix cmapped = Pix.Create(2, 2, 8);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(0, 0, 0);
        cmapped.SetColormap(cmap);
        Assert.Throws<ArgumentException>(() =>
            Pix.BlendGray(cmapped, cmapped, Gray(1, 1, 1), 0, 0, 0.5f, L.L_BLEND_GRAY, 0, 0));
        // A colormapped blendee is converted when not blending in place.
        Pix pixd = Pix.BlendGray(null, cmapped, Gray(1, 1, 200), 0, 0, 0.5f, L.L_BLEND_GRAY, 0, 0);
        Assert.Null(pixd.Colormap);
        AssertPixel(pixd, 0, 0, 100);
        Pix pix4 = Pix.BlendGray(null, Pix.Create(2, 2, 4), Gray(1, 1, 200), 0, 0, 0.5f, L.L_BLEND_GRAY, 0, 0);
        Assert.Equal(8, pix4.Depth);
    }

    /*---------------------------- pixBlendGrayInverse -------------------------*/

    [Fact]
    public void BlendGrayInverse_Values()
    {
        Pix pixd = Pix.BlendGrayInverse(null, Gray(2, 1, 100), Gray(2, 1, 0), 0, 0, 1.0f);
        AssertPixel(pixd, 0, 0, 155);    // full inversion
        pixd = Pix.BlendGrayInverse(null, Gray(2, 1, 100), Gray(2, 1, 255), 0, 0, 1.0f);
        AssertPixel(pixd, 0, 0, 100);    // white blender: no change
        pixd = Pix.BlendGrayInverse(null, Gray(2, 1, 100), Gray(2, 1, 0), 0, 0, 0.5f);
        AssertPixel(pixd, 0, 0, 127);    // a = 127.5, truncated
        Pix src = Color(1, 1, 100, 150, 200);
        pixd = Pix.BlendGrayInverse(null, src, Gray(1, 1, 0), 0, 0, 1.0f);
        AssertRgb(pixd, 0, 0, 155, 105, 55);
    }

    /*------------------------------ pixBlendColor -----------------------------*/

    [Fact]
    public void BlendColor_Values()
    {
        Pix pixd = Pix.BlendColor(null, Color(3, 3, 100, 150, 200), Color(1, 1, 200, 50, 0), 1, 2, 0.25f, 0, 0);
        AssertRgb(pixd, 1, 2, 125, 125, 150);
        AssertRgb(pixd, 0, 2, 100, 150, 200);
        AssertRgb(pixd, 1, 1, 100, 150, 200);
    }

    [Fact]
    public void BlendColor_TransparentColorIsSkippedAndGrayBlendeeIsPromoted()
    {
        Pix blender = Pix.Create(2, 1, 32);
        Naive.SetPixel(blender, 0, 0, Rgb(200, 50, 0));
        Naive.SetPixel(blender, 1, 0, Rgb(10, 20, 30));
        Pix pixd = Pix.BlendColor(null, Gray(2, 1, 100), blender, 0, 0, 0.5f, 1, Rgb(10, 20, 30));
        Assert.Equal(32, pixd.Depth);
        AssertRgb(pixd, 0, 0, 150, 75, 50);
        AssertRgb(pixd, 1, 0, 100, 100, 100);
    }

    [Fact]
    public void BlendColor_InPlaceNeeds32Bpp()
    {
        Pix gray = Gray(2, 2, 100);
        Assert.Throws<ArgumentException>(() => Pix.BlendColor(gray, gray, Color(1, 1, 0, 0, 0), 0, 0, 0.5f, 0, 0));
        Pix rgb = Color(2, 2, 100, 100, 100);
        Pix pixd = Pix.BlendColor(rgb, rgb, Color(1, 1, 0, 0, 0), 0, 0, 0.5f, 0, 0);
        Assert.Same(rgb, pixd);
        AssertRgb(rgb, 0, 0, 50, 50, 50);
    }

    [Fact]
    public void BlendColorByChannel_MinMaxAndMix()
    {
        Pix pixd = Pix.BlendColorByChannel(null, Color(1, 1, 100, 100, 100), Color(1, 1, 50, 150, 200), 0, 0,
                                           -1.0f, 2.0f, 0.5f, 0, 0);
        AssertRgb(pixd, 0, 0, 50, 150, 150);    // min, max, mix
        pixd = Pix.BlendColorByChannel(null, Color(1, 1, 100, 100, 100), Color(1, 1, 50, 150, 200), 0, 0,
                                       0.0f, 1.0f, 0.0f, 0, 0);
        AssertRgb(pixd, 0, 0, 100, 150, 100);
    }

    /*---------------------------- pixBlendGrayAdapt ---------------------------*/

    [Fact]
    public void BlendGrayAdapt_Gray()
    {
        // The rank value of a one-bin histogram is bin + 0.5: fmedian = 200.5, median = (int)(201.0) = 201 >= 128,
        // pivot = 201 - 64 = 137; delta = (137 - 200) * 255 / 256 = -62 (truncated);
        // 200 + (int)(1.0 * -62 + 0.5) = 200 - 61
        Pix pixd = Pix.BlendGrayAdapt(null, Gray(4, 4, 200), Gray(2, 2, 0), 0, 0, 1.0f, -1);
        AssertPixel(pixd, 0, 0, 139);
        AssertPixel(pixd, 3, 3, 200);
        // median 51 < 128: pivot = 115; delta = 65 * 255 / 256 = 64; 50 + (int)(64.5)
        pixd = Pix.BlendGrayAdapt(null, Gray(4, 4, 50), Gray(2, 2, 0), 0, 0, 1.0f, 64);
        AssertPixel(pixd, 1, 1, 114);
    }

    [Fact]
    public void BlendGrayAdapt_Errors()
    {
        Assert.Throws<ArgumentException>(() =>
            Pix.BlendGrayAdapt(null, Gray(4, 4, 50), Gray(2, 2, 0), 10, 10, 1.0f, -1));   // no overlap
        Assert.Throws<ArgumentException>(() =>
            Pix.BlendGrayAdapt(null, Pix.Create(4, 4, 1), Gray(2, 2, 0), 0, 0, 1.0f, -1));
    }

    [Fact]
    public void BlendGrayAdapt_Rgb()
    {
        // The region median is of the gray conversion (8 bpp) of the blendee: all 100 -> 101 < 128,
        // pivot = 101 + 64 = 165; mval = 100; delta = (165 - 100) * 255 / 256 = 64;
        // factor = 1.0 * 64 / 100 = 0.64; each component: 100 + (int)(0.64 * 100 + 0.5)
        Pix pixd = Pix.BlendGrayAdapt(null, Color(2, 2, 100, 100, 100), Gray(1, 1, 0), 0, 0, 1.0f, 64);
        float factor = 1.0f * 64 / 100;
        int v = 100 + (int)(factor * 100 + 0.5);
        AssertRgb(pixd, 0, 0, v, v, v);
        AssertRgb(pixd, 1, 1, 100, 100, 100);
    }

    /*----------------------------- pixFadeWithGray ----------------------------*/

    [Fact]
    public void FadeWithGray_Gray()
    {
        Pix pixs = Gray(4, 4, 100);
        Pix white = pixs.FadeWithGray(Gray(2, 2, 255), 255.0f, L.L_BLEND_TO_WHITE);
        Pix black = pixs.FadeWithGray(Gray(2, 2, 255), 255.0f, L.L_BLEND_TO_BLACK);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++) {
                bool inside = x < 2 && y < 2;
                AssertPixel(white, x, y, inside ? 255u : 100u);
                AssertPixel(black, x, y, inside ? 0u : 100u);
            }
        // fract = (127.5 / 255) * blender value 1 = 0.5
        white = pixs.FadeWithGray(Gray(1, 1, 1), 127.5f, L.L_BLEND_TO_WHITE);
        black = pixs.FadeWithGray(Gray(1, 1, 1), 127.5f, L.L_BLEND_TO_BLACK);
        AssertPixel(white, 0, 0, 177);   // 100 + (int)(0.5 * 155)
        AssertPixel(black, 0, 0, 50);
    }

    [Fact]
    public void FadeWithGray_RgbAndErrors()
    {
        Pix pixd = Color(2, 2, 100, 150, 200).FadeWithGray(Gray(2, 2, 1), 127.5f, L.L_BLEND_TO_WHITE);
        AssertRgb(pixd, 0, 0, 177, 202, 227);
        Assert.Throws<ArgumentException>(() => Gray(2, 2, 1).FadeWithGray(Pix.Create(2, 2, 1), 1.0f, L.L_BLEND_TO_WHITE));
        Assert.Throws<ArgumentException>(() => Gray(2, 2, 1).FadeWithGray(Gray(2, 2, 1), 300.0f, L.L_BLEND_TO_WHITE));
        Assert.Throws<ArgumentException>(() => Gray(2, 2, 1).FadeWithGray(Gray(2, 2, 1), 1.0f, L.L_BLEND_GRAY));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 1).FadeWithGray(Gray(2, 2, 1), 1.0f, L.L_BLEND_TO_WHITE));
    }

    /*---------------------------- pixBlendHardLight ---------------------------*/

    [Fact]
    public void BlendHardLight_GrayOnGray()
    {
        // b < 0x80: b' = 0x80 - (int)(1.0 * (0x80 - 50)) = 50; (100 * 50) >> 7 = 39
        Pix pixd = Pix.BlendHardLight(null, Gray(2, 2, 100), Gray(1, 1, 50), 0, 0, 1.0f);
        AssertPixel(pixd, 0, 0, 39);
        AssertPixel(pixd, 1, 0, 100);
        // b >= 0x80: b' = 200; 255 - (((255 - 200) * (255 - 100)) >> 7) = 255 - 66
        pixd = Pix.BlendHardLight(null, Gray(2, 2, 100), Gray(1, 1, 200), 0, 0, 1.0f);
        AssertPixel(pixd, 0, 0, 189);
    }

    [Fact]
    public void BlendHardLight_ColorCases()
    {
        Pix rgb = Color(2, 2, 100, 100, 100);
        // rgb blendee, gray blender
        Pix pixd = Pix.BlendHardLight(null, rgb, Gray(1, 1, 50), 0, 0, 1.0f);
        AssertRgb(pixd, 0, 0, 39, 39, 39);
        Assert.Equal(32, pixd.Depth);
        // gray blendee, rgb blender -> 32 bpp
        pixd = Pix.BlendHardLight(null, Gray(2, 2, 100), Color(1, 1, 50, 200, 50), 0, 0, 1.0f);
        Assert.Equal(32, pixd.Depth);
        AssertRgb(pixd, 0, 0, 39, 189, 39);
        AssertRgb(pixd, 1, 1, 100, 100, 100);
        // in place
        Pix pixs = Color(2, 2, 100, 100, 100);
        pixd = Pix.BlendHardLight(pixs, pixs, Color(1, 1, 50, 200, 50), 0, 0, 1.0f);
        Assert.Same(pixs, pixd);
        AssertRgb(pixs, 0, 0, 39, 189, 39);
    }

    [Fact]
    public void BlendHardLight_Errors()
    {
        Assert.Throws<ArgumentException>(() => Pix.BlendHardLight(null, Pix.Create(2, 2, 1), Gray(1, 1, 1), 0, 0, 1.0f));
        Assert.Throws<ArgumentException>(() => Pix.BlendHardLight(null, Gray(2, 2, 1), Pix.Create(1, 1, 1), 0, 0, 1.0f));
        Pix a = Gray(2, 2, 1), b = Gray(2, 2, 1);
        Assert.Throws<ArgumentException>(() => Pix.BlendHardLight(a, b, Gray(1, 1, 1), 0, 0, 1.0f));
    }

    /*------------------------------ pixBlendCmap ------------------------------*/

    private static Pix Cmapped(int w, int h, int depth, params (int r, int g, int b)[] colors)
    {
        Pix pix = Pix.Create(w, h, depth);
        PixColormap cmap = PixColormap.Create(depth);
        foreach (var (r, g, b) in colors)
            cmap.AddColor(r, g, b);
        pix.SetColormap(cmap);
        return pix;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void BlendCmap_AddsColorsAndMapsIndices(int depth)
    {
        Pix pixs = Cmapped(4, 4, depth, (255, 255, 255), (0, 0, 0));
        Naive.SetPixel(pixs, 0, 0, 1);   // not sindex: stays
        Pix pixb = Cmapped(2, 2, depth, (255, 0, 0), (0, 0, 0));
        Naive.SetPixel(pixb, 1, 0, 1);
        Naive.SetPixel(pixb, 0, 1, 1);

        pixs.BlendCmap(pixb, 1, 1, 0);

        Assert.Equal(3, pixs.Colormap!.Count);
        AssertPixel(pixs, 0, 0, 1);
        AssertPixel(pixs, 1, 1, 2);   // red, added
        AssertPixel(pixs, 2, 1, 1);   // black, existing
        AssertPixel(pixs, 1, 2, 1);
        AssertPixel(pixs, 2, 2, 2);
        AssertPixel(pixs, 3, 3, 0);
    }

    [Fact]
    public void BlendCmap_OnlyReplacesSindexAndClips()
    {
        Pix pixs = Cmapped(3, 3, 8, (255, 255, 255), (0, 0, 0));
        Naive.SetPixel(pixs, 1, 1, 1);
        Pix pixb = Cmapped(2, 2, 8, (0, 0, 0));
        pixs.BlendCmap(pixb, 1, 1, 0);
        Assert.Equal(2, pixs.Colormap!.Count);   // black already there: nothing added
        AssertPixel(pixs, 1, 1, 1);
        AssertPixel(pixs, 2, 2, 1);
        pixs.BlendCmap(pixb, 2, 2, 0);   // clipped at the lower right
        AssertPixel(pixs, 0, 0, 0);
    }

    [Fact]
    public void BlendCmap_Errors()
    {
        Pix pixs = Cmapped(3, 3, 2, (0, 0, 0), (50, 50, 50), (100, 100, 100));
        Pix pixb = Cmapped(1, 1, 2, (1, 2, 3), (4, 5, 6));
        // Only one free slot in a 2 bpp colormap, but two new colors: no room.
        Assert.Throws<LeptonicaException>(() => pixs.BlendCmap(pixb, 0, 0, 0));
        Assert.Equal(3, pixs.Colormap!.Count);
        Assert.Throws<ArgumentException>(() => Gray(2, 2, 0).BlendCmap(pixb, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => pixs.BlendCmap(Gray(2, 2, 0), 0, 0, 0));
    }

    /*------------------------- pixBlendWithGrayMask ---------------------------*/

    [Fact]
    public void BlendWithGrayMask_Gray()
    {
        Pix pixs1 = Gray(4, 4, 100);
        Pix pixs2 = Gray(2, 2, 200);
        Pix pixg = Gray(2, 2, 0);
        Naive.SetPixel(pixg, 0, 0, 255);
        Naive.SetPixel(pixg, 1, 0, 128);
        Pix pixd = pixs1.BlendWithGrayMask(pixs2, pixg, 1, 1);
        Assert.NotSame(pixs1, pixd);
        AssertPixel(pixs1, 1, 1, 100);       // the source is not changed
        AssertPixel(pixd, 1, 1, 200);        // opaque
        AssertPixel(pixd, 2, 1, 150);        // fract = 128 / 255
        AssertPixel(pixd, 1, 2, 100);        // transparent
        AssertPixel(pixd, 0, 0, 100);
        Assert.Equal(8, pixd.Depth);
    }

    [Fact]
    public void BlendWithGrayMask_Color()
    {
        Pix pixs1 = Gray(2, 2, 100);
        Pix pixs2 = Color(1, 1, 200, 0, 50);
        Pix pixg = Gray(1, 1, 255);
        Pix pixd = pixs1.BlendWithGrayMask(pixs2, pixg, 0, 0);
        Assert.Equal(32, pixd.Depth);
        AssertRgb(pixd, 0, 0, 200, 0, 50);
        AssertRgb(pixd, 1, 1, 100, 100, 100);
    }

    [Fact]
    public void BlendWithGrayMask_UsesAlphaOfPixs2()
    {
        Pix pixs2 = Color(1, 1, 200, 0, 50);
        pixs2.Spp = 4;
        Naive.SetPixel(pixs2, 0, 0, Rgb(200, 0, 50) | 255u);
        Pix pixd = Gray(2, 2, 100).BlendWithGrayMask(pixs2, null, 1, 1);
        AssertRgb(pixd, 1, 1, 200, 0, 50);
        Assert.Throws<ArgumentException>(() => Gray(2, 2, 100).BlendWithGrayMask(Color(1, 1, 1, 2, 3), null, 0, 0));
        Assert.Throws<ArgumentException>(() => Gray(2, 2, 100).BlendWithGrayMask(pixs2, Pix.Create(1, 1, 1), 0, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 1).BlendWithGrayMask(pixs2, Gray(1, 1, 1), 0, 0));
    }

    /*-------------------- pixBlendBackgroundToColor and others ----------------*/

    [Fact]
    public void BlendBackgroundToColor_ReplacesWhiteKeepsDark()
    {
        Pix pixs = Color(6, 6, 255, 255, 255);
        Naive.SetPixel(pixs, 2, 2, Rgb(0, 0, 0));
        Pix pixd = Pix.BlendBackgroundToColor(null, pixs, null, 0xff000000, 1.0f, 0, 255);
        Assert.NotSame(pixs, pixd);
        AssertRgb(pixd, 0, 0, 255, 0, 0);
        AssertRgb(pixd, 2, 2, 0, 0, 0);
        AssertRgb(pixs, 0, 0, 255, 255, 255);

        // in place, restricted to a box
        pixd = Pix.BlendBackgroundToColor(pixs, pixs, Box.Create(0, 0, 3, 3), 0xff000000, 1.0f, 0, 255);
        Assert.Same(pixs, pixd);
        AssertRgb(pixs, 0, 0, 255, 0, 0);
        AssertRgb(pixs, 2, 2, 0, 0, 0);
        AssertRgb(pixs, 4, 4, 255, 255, 255);
        Assert.Throws<ArgumentException>(() => Pix.BlendBackgroundToColor(null, Gray(2, 2, 0), null, 0, 1.0f, 0, 255));
    }

    [Fact]
    public void MultiplyByColor_Values()
    {
        Pix pixs = Color(4, 4, 200, 100, 50);
        Pix pixd = Pix.MultiplyByColor(null, pixs, null, Rgb(128, 255, 0));
        // red: (1/255 * 128) * 200 + 0.5 = 100.89 -> 100; green: 100.5 -> 100; blue: 0
        AssertRgb(pixd, 3, 3, 100, 100, 0);
        AssertRgb(pixs, 3, 3, 200, 100, 50);

        pixd = Pix.MultiplyByColor(pixs, pixs, Box.Create(1, 1, 2, 2), Rgb(0, 0, 0));
        Assert.Same(pixs, pixd);
        AssertRgb(pixs, 1, 1, 0, 0, 0);
        AssertRgb(pixs, 2, 2, 0, 0, 0);
        AssertRgb(pixs, 0, 0, 200, 100, 50);
        AssertRgb(pixs, 3, 3, 200, 100, 50);
        Assert.Throws<ArgumentException>(() => Pix.MultiplyByColor(null, Gray(2, 2, 0), null, 0));
    }

    [Fact]
    public void AlphaBlendUniform_BlendsOverColor()
    {
        Pix rgba = Color(2, 1, 200, 0, 0);
        rgba.Spp = 4;
        Naive.SetPixel(rgba, 0, 0, Rgb(200, 0, 0) | 255u);   // opaque
        Naive.SetPixel(rgba, 1, 0, Rgb(200, 0, 0));          // transparent
        Pix pixd = rgba.AlphaBlendUniform(0x00ff0000);
        AssertRgb(pixd, 0, 0, 200, 0, 0);
        AssertRgb(pixd, 1, 0, 0, 255, 0);

        Pix rgb = Color(2, 1, 1, 2, 3);
        Assert.Same(rgb, rgb.AlphaBlendUniform(0xffffff00));
        Assert.Throws<ArgumentException>(() => Gray(2, 2, 0).AlphaBlendUniform(0));
    }

    [Fact]
    public void AddAlphaToBlend_AlphaFromInvertedGray()
    {
        Pix gray = Gray(2, 1, 255);
        Naive.SetPixel(gray, 1, 0, 0);
        Pix pixd = gray.AddAlphaToBlend(0.5f, 0);
        Assert.Equal(32, pixd.Depth);
        Assert.Equal(4, pixd.Spp);
        Assert.Equal(0u, Naive.GetPixel(pixd, 0, 0) & 0xff);       // white: transparent
        Assert.Equal(127u, Naive.GetPixel(pixd, 1, 0) & 0xff);     // black: (int)(0.5 * 255)
        AssertRgb(pixd, 0, 0, 255, 255, 255);
        AssertRgb(pixd, 1, 0, 0, 0, 0);

        Pix inverted = gray.AddAlphaToBlend(0.5f, 1);
        AssertRgb(inverted, 0, 0, 0, 0, 0);
        AssertRgb(inverted, 1, 0, 255, 255, 255);
        Assert.Equal(127u, Naive.GetPixel(inverted, 1, 0) & 0xff);

        Assert.Throws<ArgumentException>(() => gray.AddAlphaToBlend(1.5f, 0));
    }

    [Fact]
    public void SetAlphaOverWhite_GradesFromWhite()
    {
        Pix pixs = Color(40, 40, 255, 255, 255);
        for (int y = 10; y < 30; y++)
            for (int x = 10; x < 30; x++)
                Naive.SetPixel(pixs, x, y, Rgb(0, 0, 0));
        Pix pixd = pixs.SetAlphaOverWhite();
        Assert.Equal(4, pixd.Spp);
        Assert.Equal(0u, Naive.GetPixel(pixd, 0, 0) & 0xff);       // white: transparent
        Assert.Equal(128u, Naive.GetPixel(pixd, 10, 10) & 0xff);   // distance 1 from white
        Assert.Equal(255u, Naive.GetPixel(pixd, 12, 12) & 0xff);   // distance 3: opaque
        Assert.Throws<ArgumentException>(() => Gray(2, 2, 0).SetAlphaOverWhite());
    }

    /*---------------------------- pixLinearEdgeFade ---------------------------*/

    [Fact]
    public void LinearEdgeFade_FadesTowardWhiteFromLeft()
    {
        Pix pix = Gray(10, 4, 100);
        pix.LinearEdgeFade(L.L_FROM_LEFT, L.L_BLEND_TO_WHITE, 0.5f, 1.0f);
        // range = 5 columns; full fade at the edge, decreasing to the inside.
        uint prev = 256;
        for (int x = 0; x < 5; x++) {
            uint v = Naive.GetPixel(pix, x, 0);
            Assert.True(v < prev && v > 100 || x == 0 && v == 255, $"x = {x}: {v}");
            Assert.Equal(v, Naive.GetPixel(pix, x, 3));
            prev = v;
        }
        Assert.Equal(255u, Naive.GetPixel(pix, 0, 0));
        for (int x = 5; x < 10; x++)
            AssertPixel(pix, x, 0, 100);
    }

    [Theory]
    [InlineData(L.L_FROM_LEFT)]
    [InlineData(L.L_FROM_RIGHT)]
    [InlineData(L.L_FROM_TOP)]
    [InlineData(L.L_FROM_BOT)]
    public void LinearEdgeFade_ToBlackAtTheRequestedEdge(int dir)
    {
        // range = (int)(0.3 * 10) = 3, slope = 1 / 3.  From the left and top the edge pixel gets the
        // maximum fade; from the right and bottom the offset is (range - j) >= 1, so the edge pixel
        // gets 1 - 1/3: 100 + (int)(-100 * 0.667 + 0.5) with the sum truncated = 33.
        Pix pix = Color(10, 10, 100, 100, 100);
        pix.LinearEdgeFade(dir, L.L_BLEND_TO_BLACK, 0.3f, 1.0f);
        bool horizontal = dir == L.L_FROM_LEFT || dir == L.L_FROM_RIGHT;
        bool fromFarSide = dir == L.L_FROM_RIGHT || dir == L.L_FROM_BOT;
        int[] depth = fromFarSide ? new[] { 9, 8, 7, 6 } : new[] { 0, 1, 2, 3 };
        uint edge = fromFarSide ? 33u : 0u;
        uint prev = edge;
        for (int k = 0; k < 4; k++) {
            int x = horizontal ? depth[k] : 5, y = horizontal ? 5 : depth[k];
            Pix.ExtractRGBValues(Naive.GetPixel(pix, x, y), out int r, out int g, out int b);
            Assert.True(r == g && g == b);
            if (k == 0)
                Assert.Equal(edge, (uint)r);
            else if (k < (fromFarSide ? 2 : 3))
                Assert.True(r > prev && r < 100, $"depth {k}: {r}");
            else
                Assert.Equal(100, r);   // beyond the fade range (from the far side, del = 0 at its start)
            prev = (uint)r;
        }
        // the opposite edge is untouched
        int ox = horizontal ? (fromFarSide ? 0 : 9) : 5, oy = horizontal ? 5 : (fromFarSide ? 0 : 9);
        AssertRgb(pix, ox, oy, 100, 100, 100);
    }

    [Fact]
    public void LinearEdgeFade_NoOpAndErrors()
    {
        Pix pix = Gray(10, 10, 100);
        pix.LinearEdgeFade(L.L_FROM_LEFT, L.L_BLEND_TO_WHITE, 0.5f, 0.0f);    // maxfade <= 0
        pix.LinearEdgeFade(L.L_FROM_LEFT, L.L_BLEND_TO_WHITE, 0.05f, 1.0f);   // distfract too small
        pix.LinearEdgeFade(L.L_FROM_LEFT, L.L_BLEND_TO_WHITE, 0.0f, 1.0f);
        for (int x = 0; x < 10; x++)
            AssertPixel(pix, x, 0, 100);
        Assert.Throws<ArgumentException>(() => pix.LinearEdgeFade(7, L.L_BLEND_TO_WHITE, 0.5f, 1.0f));
        Assert.Throws<ArgumentException>(() => pix.LinearEdgeFade(L.L_FROM_LEFT, 7, 0.5f, 1.0f));
        Assert.Throws<ArgumentException>(() => pix.LinearEdgeFade(L.L_FROM_LEFT, L.L_BLEND_TO_WHITE, 0.5f, 1.5f));
        Assert.Throws<ArgumentException>(() => pix.LinearEdgeFade(L.L_FROM_LEFT, L.L_BLEND_TO_WHITE, 1.5f, 1.0f));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 1).LinearEdgeFade(L.L_FROM_LEFT, L.L_BLEND_TO_WHITE, 0.5f, 1.0f));
    }

    /*--------------------------------- pixBlend -------------------------------*/

    [Fact]
    public void Blend_DispatchesOnBlenderDepth()
    {
        Pix pixs1 = Gray(4, 4, 100);
        // 8 bpp blender -> pixBlendGray
        Pix pixd = pixs1.Blend(Gray(2, 2, 200), 1, 1, 0.5f)!;
        AssertPixel(pixd, 1, 1, 150);
        AssertPixel(pixd, 0, 0, 100);
        // 1 bpp blender -> pixBlendMask with inverse
        pixd = pixs1.Blend(Mask(2, 2, (0, 0)), 1, 1, 0.5f)!;
        AssertPixel(pixd, 1, 1, 127);
        AssertPixel(pixd, 2, 2, 100);
        // 32 bpp blender -> pixBlendColor
        pixd = pixs1.Blend(Color(1, 1, 200, 200, 200), 0, 0, 0.5f)!;
        Assert.Equal(32, pixd.Depth);
        AssertRgb(pixd, 0, 0, 150, 150, 150);
    }

    [Fact]
    public void Blend_NegativeOffsetClipsAndNoOverlapGivesNull()
    {
        Pix pixs1 = Gray(4, 4, 100);
        Pix pixd = pixs1.Blend(Gray(3, 3, 200), -2, -2, 0.5f)!;
        AssertPixel(pixd, 0, 0, 150);
        AssertPixel(pixd, 1, 0, 100);
        Assert.Null(pixs1.Blend(Gray(2, 2, 200), 10, 10, 0.5f));
        Assert.Null(pixs1.Blend(Gray(2, 2, 200), -5, 0, 0.5f));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 1).Blend(Gray(2, 2, 200), 0, 0, 0.5f));
    }

    [Fact]
    public void Blend_RemovesColormapFromBlender()
    {
        Pix blender = Cmapped(1, 1, 8, (200, 200, 200));
        Pix pixd = Gray(2, 2, 100).Blend(blender, 0, 0, 0.5f)!;
        AssertPixel(pixd, 0, 0, 150);
    }
}
