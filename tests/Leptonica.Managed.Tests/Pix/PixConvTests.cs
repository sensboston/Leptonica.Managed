using System;
using Leptonica;

namespace Leptonica.Managed.Tests.PixConv;

/// <summary>Tests of pixconv.c.</summary>
public class PixConvTests
{
    private static uint Rgb(int r, int g, int b)
    {
        Pix.ComposeRGBPixel(r, g, b, out uint p);
        return p;
    }

    private static uint Rgba(int r, int g, int b, int a)
    {
        Pix.ComposeRGBAPixel(r, g, b, a, out uint p);
        return p;
    }

    private static uint Px(Pix pix, int x, int y)
    {
        Assert.True(pix.GetPixel(x, y, out uint v));
        return v;
    }

    /* A colormapped pix of depth d with n colors and pseudo-random indices. */
    private static Pix MakeCmapPix(int w, int h, int d, int n, Random rng, bool color, int alpha = 255)
    {
        Pix pix = Pix.Create(w, h, d);
        PixColormap cmap = PixColormap.Create(d);
        for (int i = 0; i < n; i++) {
            int r = (i * 37 + 11) & 0xff;
            int g = color ? (i * 91 + 5) & 0xff : r;
            int b = color ? (i * 53 + 200) & 0xff : r;
            cmap.AddRGBA(r, g, b, alpha);
        }
        pix.SetColormap(cmap);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pix.SetPixel(x, y, (uint)rng.Next(n));
        return pix;
    }

    /*------------------------------------------------------------*
     *                     Colormap removal                       *
     *------------------------------------------------------------*/
    [Fact]
    public void RemoveColormapMatchesScalarForRandomPalettesAndWidths()
    {
        var rng = new Random(290147);
        int[] widths = { 1, 31, 32, 33, 63, 64, 65, 71 };
        foreach (int depth in new[] { 1, 2, 4, 8 }) {
            foreach (int count in new[] { 1, 1 << depth }) {
                foreach (int width in widths) {
                    foreach (bool color in new[] { false, true }) {
                        foreach (int alpha in new[] { 255, 77 }) {
                            Pix src = MakeCmapPix(width, 3, depth, count, rng, color, alpha);
                            foreach (int type in new[] { L.REMOVE_CMAP_BASED_ON_SRC, L.REMOVE_CMAP_TO_GRAYSCALE,
                                                        L.REMOVE_CMAP_TO_FULL_COLOR, L.REMOVE_CMAP_WITH_ALPHA }) {
                                bool useAlpha = type == L.REMOVE_CMAP_WITH_ALPHA ||
                                                (type == L.REMOVE_CMAP_BASED_ON_SRC && alpha != 255);
                                bool useGray = type == L.REMOVE_CMAP_TO_GRAYSCALE ||
                                               (type == L.REMOVE_CMAP_BASED_ON_SRC && alpha == 255 && !color);
                                Pix expected = Pix.Create(width, 3, useGray ? 8 : 32);
                                if (useAlpha) expected.Spp = 4;
                                for (int y = 0; y < 3; y++) {
                                    for (int x = 0; x < width; x++) {
                                        int index = (int)Px(src, x, y);
                                        src.Colormap!.GetRGBA(index, out int r, out int g, out int b, out int a);
                                        uint value = useGray
                                            ? (uint)(L.L_RED_WEIGHT * r + L.L_GREEN_WEIGHT * g + L.L_BLUE_WEIGHT * b + 0.5)
                                            : (useAlpha ? Rgba(r, g, b, a) : Rgb(r, g, b));
                                        expected.SetPixel(x, y, value);
                                    }
                                }
                                Pix actual = src.RemoveColormap(type);
                                Assert.Equal(expected.Depth, actual.Depth);
                                Assert.Equal(expected.Spp, actual.Spp);
                                Assert.Equal(expected.Data, actual.Data);
                            }
                        }
                    }
                }
            }
        }
        foreach (int width in widths) {
            foreach (bool invert in new[] { false, true }) {
                Pix src = Pix.Create(width, 3, 1);
                PixColormap cmap = PixColormap.Create(1);
                cmap.AddColor(invert ? 0 : 255, invert ? 0 : 255, invert ? 0 : 255);
                cmap.AddColor(invert ? 255 : 0, invert ? 255 : 0, invert ? 255 : 0);
                src.SetColormap(cmap);
                for (int i = 0; i < src.Data.Length; i++)
                    src.Data[i] = (uint)rng.NextInt64(0, 1L << 32);
                uint[] expected = (uint[])src.Data.Clone();
                if (invert) {
                    for (int y = 0; y < src.Height; y++) {
                        for (int x = 0; x < width; x++)
                            expected[y * src.Wpl + (x >> 5)] ^= 0x80000000u >> (x & 31);
                    }
                }
                Pix actual = src.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
                Assert.Equal(1, actual.Depth);
                Assert.Equal(expected, actual.Data);
            }
        }
    }

    [Fact]
    public void RemoveColormapOutOfRangeGrayIndicesRemainZero()
    {
        foreach (int depth in new[] { 1, 2, 4, 8 }) {
            foreach (int width in new[] { 1, 31, 32, 33, 63, 64, 65 }) {
                Pix src = Pix.Create(width, 3, depth);
                PixColormap cmap = PixColormap.Create(depth);
                cmap.AddColor(200, 200, 200);
                src.SetColormap(cmap);
                Pix expected = Pix.Create(width, 3, 8);
                for (int y = 0; y < 3; y++) {
                    for (int x = 0; x < width; x++) {
                        uint index = (uint)((x + y) % (1 << depth));
                        src.SetPixel(x, y, index);
                        if (index == 0)
                            expected.SetPixel(x, y, (uint)(L.L_RED_WEIGHT * 200 + L.L_GREEN_WEIGHT * 200 + L.L_BLUE_WEIGHT * 200 + 0.5));
                    }
                }
                Pix actual = src.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
                Assert.Equal(expected.Data, actual.Data);
            }
        }
    }

    [Fact]
    public void RemoveColormapNoCmapReturnsSameImage()
    {
        Pix pix = Pix.Create(5, 3, 8);
        Assert.Same(pix, pix.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE));
    }

    [Fact]
    public void RemoveColormapToGrayscaleHandChecked()
    {
        Pix pix = Pix.Create(4, 1, 8);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(0, 0, 0);
        cmap.AddColor(100, 200, 50);
        cmap.AddColor(255, 255, 255);
        pix.SetColormap(cmap);
        pix.SetPixel(0, 0, 0);
        pix.SetPixel(1, 0, 1);
        pix.SetPixel(2, 0, 2);
        pix.SetPixel(3, 0, 1);

        Pix pixd = pix.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        Assert.Null(pixd.Colormap);
        Assert.Equal(8, pixd.Depth);
        // 0.3 * 100 + 0.5 * 200 + 0.2 * 50 + 0.5 = 140.5 --> 140
        Assert.Equal(0u, Px(pixd, 0, 0));
        Assert.Equal(140u, Px(pixd, 1, 0));
        Assert.Equal(255u, Px(pixd, 2, 0));
        Assert.Equal(140u, Px(pixd, 3, 0));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(4, 16)]
    [InlineData(8, 256)]
    public void RemoveColormapToGrayscaleAllDepths(int d, int n)
    {
        Random rng = new(d);
        foreach (int w in new[] { 1, 3, 4, 15, 16, 17, 31, 32, 33, 70 }) {
            Pix pix = MakeCmapPix(w, 4, d, n, rng, color: true);
            Pix pixd = pix.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
            Assert.Equal(8, pixd.Depth);
            Assert.Equal(w, pixd.Width);
            Assert.Null(pixd.Colormap);
            for (int y = 0; y < 4; y++) {
                for (int x = 0; x < w; x++) {
                    pix.Colormap!.GetColor((int)Px(pix, x, y), out int r, out int g, out int b);
                    uint expected = (uint)(L.L_RED_WEIGHT * r + L.L_GREEN_WEIGHT * g + L.L_BLUE_WEIGHT * b + 0.5);
                    Assert.Equal(expected, Px(pixd, x, y));
                }
            }
        }
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(4, 16)]
    [InlineData(8, 256)]
    public void RemoveColormapToFullColorAllDepths(int d, int n)
    {
        Random rng = new(100 + d);
        foreach (int w in new[] { 1, 5, 33 }) {
            Pix pix = MakeCmapPix(w, 3, d, n, rng, color: true, alpha: 77);
            Pix pixd = pix.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
            Assert.Equal(32, pixd.Depth);
            Assert.Equal(3, pixd.Spp);
            for (int y = 0; y < 3; y++) {
                for (int x = 0; x < w; x++) {
                    pix.Colormap!.GetColor((int)Px(pix, x, y), out int r, out int g, out int b);
                    Assert.Equal(Rgb(r, g, b), Px(pixd, x, y));
                }
            }
        }
    }

    [Fact]
    public void RemoveColormapWithAlphaHandChecked()
    {
        Pix pix = Pix.Create(2, 1, 2);
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddRGBA(10, 20, 30, 40);
        cmap.AddRGBA(200, 100, 50, 255);
        pix.SetColormap(cmap);
        pix.SetPixel(0, 0, 0);
        pix.SetPixel(1, 0, 1);

        Pix pixd = pix.RemoveColormap(L.REMOVE_CMAP_WITH_ALPHA);
        Assert.Equal(32, pixd.Depth);
        Assert.Equal(4, pixd.Spp);
        Assert.Equal(0x0a141e28u, Px(pixd, 0, 0));
        Assert.Equal(0xc86432ffu, Px(pixd, 1, 0));
    }

    [Fact]
    public void RemoveColormapToBinaryKeepsOrientation()
    {
        // Standard: index 0 = white (sum larger), index 1 = black: the data is kept.
        Pix pix = Pix.Create(3, 1, 1);
        PixColormap cmap = PixColormap.Create(1);
        cmap.AddColor(255, 255, 255);
        cmap.AddColor(0, 0, 0);
        pix.SetColormap(cmap);
        pix.SetPixel(1, 0, 1);
        Pix pixd = pix.RemoveColormap(L.REMOVE_CMAP_TO_BINARY);
        Assert.Null(pixd.Colormap);
        Assert.Equal(1, pixd.Depth);
        Assert.Equal(0u, Px(pixd, 0, 0));
        Assert.Equal(1u, Px(pixd, 1, 0));
        Assert.Equal(0u, Px(pixd, 2, 0));

        // Inverted: index 0 = black, index 1 = white: the data is inverted.
        Pix pix2 = Pix.Create(3, 1, 1);
        PixColormap cmap2 = PixColormap.Create(1);
        cmap2.AddColor(0, 0, 0);
        cmap2.AddColor(255, 255, 255);
        pix2.SetColormap(cmap2);
        pix2.SetPixel(1, 0, 1);
        Pix pixd2 = pix2.RemoveColormap(L.REMOVE_CMAP_TO_BINARY);
        Assert.Equal(1u, Px(pixd2, 0, 0));
        Assert.Equal(0u, Px(pixd2, 1, 0));
        Assert.Equal(1u, Px(pixd2, 2, 0));
        // the source is unchanged
        Assert.Equal(0u, Px(pix2, 0, 0));
        Assert.Equal(1u, Px(pix2, 1, 0));
    }

    [Fact]
    public void RemoveColormapBasedOnSrc()
    {
        Random rng = new(5);
        // 1 bpp black and white --> 1 bpp
        Pix pix1 = Pix.Create(4, 2, 1);
        PixColormap c1 = PixColormap.Create(1);
        c1.AddColor(0, 0, 0);
        c1.AddColor(255, 255, 255);
        pix1.SetColormap(c1);
        Assert.Equal(1, pix1.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC).Depth);

        // gray, 4 bpp --> 8 bpp gray
        Pix pix2 = MakeCmapPix(9, 2, 4, 5, rng, color: false);
        Assert.Equal(8, pix2.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC).Depth);

        // opaque color --> 32 bpp, 3 spp
        Pix pix3 = MakeCmapPix(9, 2, 4, 5, rng, color: true);
        Pix pixd3 = pix3.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        Assert.Equal(32, pixd3.Depth);
        Assert.Equal(3, pixd3.Spp);

        // non-opaque --> 32 bpp, 4 spp
        Pix pix4 = MakeCmapPix(9, 2, 4, 5, rng, color: false, alpha: 100);
        Pix pixd4 = pix4.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        Assert.Equal(32, pixd4.Depth);
        Assert.Equal(4, pixd4.Spp);
        pix4.Colormap!.GetRGBA((int)Px(pix4, 0, 0), out int r, out int g, out int b, out _);
        Assert.Equal(Rgba(r, g, b, 100), Px(pixd4, 0, 0));
    }

    [Fact]
    public void RemoveColormapInvalidTypeAndBinaryOnNon1bpp()
    {
        Random rng = new(6);
        // an invalid type is treated as REMOVE_CMAP_BASED_ON_SRC
        Pix pix = MakeCmapPix(5, 2, 8, 10, rng, color: true);
        Assert.Equal(32, pix.RemoveColormap(99).Depth);
        // TO_BINARY on a non-1 bpp pix is treated as BASED_ON_SRC
        Pix pixg = MakeCmapPix(5, 2, 8, 10, rng, color: false);
        Assert.Equal(8, pixg.RemoveColormap(L.REMOVE_CMAP_TO_BINARY).Depth);
    }

    [Fact]
    public void RemoveColormapGeneral()
    {
        Pix pix = Pix.Create(4, 4, 8);
        Assert.Same(pix, pix.RemoveColormapGeneral(L.REMOVE_CMAP_TO_GRAYSCALE, L.L_CLONE));
        Pix copy = pix.RemoveColormapGeneral(L.REMOVE_CMAP_TO_GRAYSCALE, L.L_COPY);
        Assert.NotSame(pix, copy);
        Assert.Equal(pix.Data, copy.Data);
        Assert.Throws<ArgumentException>(() => pix.RemoveColormapGeneral(L.REMOVE_CMAP_TO_GRAYSCALE, 7));

        Pix cm = MakeCmapPix(4, 4, 8, 4, new Random(1), color: false);
        Assert.Null(cm.RemoveColormapGeneral(L.REMOVE_CMAP_TO_GRAYSCALE, L.L_CLONE).Colormap);
    }

    /*------------------------------------------------------------*
     *                  Add colormap losslessly                   *
     *------------------------------------------------------------*/
    [Fact]
    public void AddGrayColormap8()
    {
        Pix pix = Pix.Create(3, 1, 8);
        pix.SetPixel(1, 0, 77);
        pix.AddGrayColormap8();
        PixColormap cmap = pix.Colormap!;
        Assert.Equal(256, cmap.Count);
        Assert.True(cmap.GetColor(77, out int r, out int g, out int b));
        Assert.Equal((77, 77, 77), (r, g, b));
        Assert.Equal(77u, Px(pix, 1, 0));

        // no-op if there is already a colormap
        PixColormap same = pix.Colormap!;
        pix.AddGrayColormap8();
        Assert.Same(same, pix.Colormap);

        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 4).AddGrayColormap8());
    }

    /*------------------------------------------------------------*
     *               Conversion from RGB to grayscale             *
     *------------------------------------------------------------*/
    private static Pix MakeRgb(params (int r, int g, int b)[] pixels)
    {
        Pix pix = Pix.Create(pixels.Length, 1, 32);
        for (int x = 0; x < pixels.Length; x++)
            pix.SetPixel(x, 0, Rgb(pixels[x].r, pixels[x].g, pixels[x].b));
        return pix;
    }

    [Fact]
    public void WeightedGrayAndLuminanceMatchScalarReference()
    {
        var rng = new Random(192348);
        foreach (int width in new[] { 1, 31, 32, 33, 63, 64, 65, 71, 257 }) {
            Pix src = Pix.Create(width, 5, 32);
            for (int i = 0; i < src.Data.Length; i++)
                src.Data[i] = (uint)rng.NextInt64(0, 1L << 32);
            foreach (var weights in new[] { (0.3f, 0.5f, 0.2f), (0.0f, 0.0f, 0.0f),
                                             (1.0f, 1.0f, 2.0f), (0.001f, 0.099f, 0.9f) }) {
                float r = weights.Item1, g = weights.Item2, b = weights.Item3;
                if (r == 0.0f && g == 0.0f && b == 0.0f) {
                    r = L.L_RED_WEIGHT;
                    g = L.L_GREEN_WEIGHT;
                    b = L.L_BLUE_WEIGHT;
                }
                float sum = r + g + b;
                if (Math.Abs(sum - 1.0) > 0.0001) {
                    r /= sum;
                    g /= sum;
                    b /= sum;
                }
                Pix expected = Pix.Create(width, 5, 8);
                for (int y = 0; y < 5; y++) {
                    for (int x = 0; x < width; x++) {
                        uint pixel = src.Data[y * src.Wpl + x];
                        int value = (int)(r * ((pixel >> L.L_RED_SHIFT) & 0xff) +
                                          g * ((pixel >> L.L_GREEN_SHIFT) & 0xff) +
                                          b * ((pixel >> L.L_BLUE_SHIFT) & 0xff) + 0.5);
                        expected.SetPixel(x, y, (uint)value);
                    }
                }
                Pix actual = src.ConvertRGBToGray(weights.Item1, weights.Item2, weights.Item3);
                Assert.Equal(expected.Data, actual.Data);
                if (weights.Item1 == 0.0f && weights.Item2 == 0.0f && weights.Item3 == 0.0f)
                    Assert.Equal(expected.Data, src.ConvertRGBToLuminance().Data);
            }
        }
    }

    [Fact]
    public void ConvertRGBToGrayDefaultWeights()
    {
        Pix pix = MakeRgb((100, 200, 50), (255, 255, 255), (0, 0, 0), (255, 0, 0));
        Pix pixd = pix.ConvertRGBToGray(0.0f, 0.0f, 0.0f);
        Assert.Equal(8, pixd.Depth);
        // 0.3 * 100 + 0.5 * 200 + 0.2 * 50 + 0.5 = 140.5 --> 140
        Assert.Equal(140u, Px(pixd, 0, 0));
        Assert.Equal(255u, Px(pixd, 1, 0));
        Assert.Equal(0u, Px(pixd, 2, 0));
        Assert.Equal(77u, Px(pixd, 3, 0));   // 0.3f * 255 + 0.5 = 77.000003
        Assert.Equal(pixd.Data, pix.ConvertRGBToLuminance().Data);
    }

    [Fact]
    public void ConvertRGBToGrayNormalizesWeights()
    {
        Pix pix = MakeRgb((100, 200, 40));
        // weights (1, 1, 2) are scaled to (0.25, 0.25, 0.5): 25 + 50 + 20 + 0.5
        Assert.Equal(95u, Px(pix.ConvertRGBToGray(1.0f, 1.0f, 2.0f), 0, 0));
        Assert.Throws<ArgumentException>(() => pix.ConvertRGBToGray(-0.1f, 0.5f, 0.6f));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 8).ConvertRGBToGray(0.3f, 0.5f, 0.2f));
    }

    [Fact]
    public void ConvertRGBToGrayFastTakesGreen()
    {
        Pix pix = MakeRgb((100, 200, 50), (1, 2, 3));
        Pix pixd = pix.ConvertRGBToGrayFast();
        Assert.Equal(200u, Px(pixd, 0, 0));
        Assert.Equal(2u, Px(pixd, 1, 0));
    }

    [Fact]
    public void ConvertRGBToGrayMinMaxHandChecked()
    {
        Pix pix = MakeRgb((10, 200, 60), (250, 0, 0));
        Assert.Equal(10u, Px(pix.ConvertRGBToGrayMinMax(L.L_CHOOSE_MIN), 0, 0));
        Assert.Equal(200u, Px(pix.ConvertRGBToGrayMinMax(L.L_CHOOSE_MAX), 0, 0));
        Assert.Equal(190u, Px(pix.ConvertRGBToGrayMinMax(L.L_CHOOSE_MAXDIFF), 0, 0));
        Assert.Equal(0u, Px(pix.ConvertRGBToGrayMinMax(L.L_CHOOSE_MIN_BOOST), 0, 0));   // 10 * 10 / 180
        Assert.Equal(222u, Px(pix.ConvertRGBToGrayMinMax(L.L_CHOOSE_MAX_BOOST), 0, 0)); // 200 * 200 / 180
        Assert.Equal(255u, Px(pix.ConvertRGBToGrayMinMax(L.L_CHOOSE_MAX_BOOST), 1, 0)); // clipped
        Assert.Throws<ArgumentException>(() => pix.ConvertRGBToGrayMinMax(99));
    }

    [Fact]
    public void ConvertRGBToGrayGeneralSelects()
    {
        Pix pix = MakeRgb((10, 200, 60));
        Assert.Equal(10u, Px(pix.ConvertRGBToGrayGeneral(L.L_SELECT_RED, 0, 0, 0), 0, 0));
        Assert.Equal(200u, Px(pix.ConvertRGBToGrayGeneral(L.L_SELECT_GREEN, 0, 0, 0), 0, 0));
        Assert.Equal(60u, Px(pix.ConvertRGBToGrayGeneral(L.L_SELECT_BLUE, 0, 0, 0), 0, 0));
        Assert.Equal(10u, Px(pix.ConvertRGBToGrayGeneral(L.L_SELECT_MIN, 0, 0, 0), 0, 0));
        Assert.Equal(200u, Px(pix.ConvertRGBToGrayGeneral(L.L_SELECT_MAX, 0, 0, 0), 0, 0));
        // 0.34 * 10 + 0.33 * 200 + 0.33 * 60 + 0.5 = 89.7 --> 89
        Assert.Equal(89u, Px(pix.ConvertRGBToGrayGeneral(L.L_SELECT_AVERAGE, 0, 0, 0), 0, 0));
        // 0.5 * 10 + 0.25 * 200 + 0.25 * 60 + 0.5 = 70.5 --> 70
        Assert.Equal(70u, Px(pix.ConvertRGBToGrayGeneral(L.L_SELECT_WEIGHTED, 0.5f, 0.25f, 0.25f), 0, 0));
        Assert.Throws<ArgumentException>(() => pix.ConvertRGBToGrayGeneral(L.L_SELECT_WEIGHTED, 0.5f, 0.5f, 0.5f));
        Assert.Throws<ArgumentException>(() => pix.ConvertRGBToGrayGeneral(L.L_SELECT_WEIGHTED, -0.5f, 1.0f, 0.5f));
        Assert.Throws<ArgumentException>(() => pix.ConvertRGBToGrayGeneral(77, 0, 0, 0));
    }

    [Fact]
    public void ConvertRGBToGraySatBoostHandChecked()
    {
        Pix pix = MakeRgb((50, 50, 50), (200, 0, 0), (50, 25, 25), (0, 0, 0));
        Pix pixd = pix.ConvertRGBToGraySatBoost(100);
        Assert.Equal(50u, Px(pixd, 0, 0));    // gray: unchanged
        Assert.Equal(255u, Px(pixd, 1, 0));   // fully saturated above refval: 255
        // sval = 127, fullsat = 127: (127 * 127 + 128 * 50) / 255 = 88
        Assert.Equal(88u, Px(pixd, 2, 0));
        Assert.Equal(0u, Px(pixd, 3, 0));
        Assert.Throws<ArgumentException>(() => pix.ConvertRGBToGraySatBoost(0));
        Assert.Throws<ArgumentException>(() => pix.ConvertRGBToGraySatBoost(256));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 8).ConvertRGBToGraySatBoost(100));
    }

    [Fact]
    public void ConvertRGBToGrayArbClips()
    {
        Pix pix = MakeRgb((100, 50, 200), (10, 100, 0), (255, 255, 255));
        Pix pixd = pix.ConvertRGBToGrayArb(1.0f, -1.0f, 0.5f);
        Assert.Equal(150u, Px(pixd, 0, 0));   // 100 - 50 + 100
        Assert.Equal(0u, Px(pixd, 1, 0));     // 10 - 100 clipped to 0
        Assert.Equal(127u, Px(pixd, 2, 0));   // 255 - 255 + 127.5 truncated
        Assert.Equal(255u, Px(pix.ConvertRGBToGrayArb(2.0f, 2.0f, 2.0f), 2, 0));
        Assert.Throws<ArgumentException>(() => pix.ConvertRGBToGrayArb(0.0f, -1.0f, 0.0f));
    }

    [Fact]
    public void ConvertRGBToBinaryArbRejectsBadArguments()
    {
        Pix pix = MakeRgb((1, 2, 3));
        Assert.Throws<ArgumentException>(() => pix.ConvertRGBToBinaryArb(0.0f, 0.0f, 0.0f, 100, L.L_SELECT_IF_LT));
        Assert.Throws<ArgumentException>(() => pix.ConvertRGBToBinaryArb(0.3f, 0.5f, 0.2f, 100, 99));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 8).ConvertRGBToBinaryArb(0.3f, 0.5f, 0.2f, 100, L.L_SELECT_IF_LT));
    }

    /*------------------------------------------------------------*
     *                Conversion from gray to colormap            *
     *------------------------------------------------------------*/
    [Fact]
    public void ConvertGrayToColormap2bpp()
    {
        Pix pix = Pix.Create(4, 1, 2);
        for (int x = 0; x < 4; x++)
            pix.SetPixel(x, 0, (uint)x);
        Pix pixd = pix.ConvertGrayToColormap();
        Assert.Equal(2, pixd.Depth);
        Assert.Equal(pix.Data, pixd.Data);
        PixColormap cmap = pixd.Colormap!;
        Assert.Equal(4, cmap.Count);
        int[] expected = { 0, 85, 170, 255 };
        for (int i = 0; i < 4; i++) {
            cmap.GetColor(i, out int r, out int g, out int b);
            Assert.Equal((expected[i], expected[i], expected[i]), (r, g, b));
        }
        // the source is unchanged
        Assert.Null(pix.Colormap);
    }

    [Fact]
    public void ConvertGrayToColormap8UsesOnlyPresentGrayLevels()
    {
        Pix pix = Pix.Create(4, 1, 8);
        uint[] vals = { 10, 10, 200, 7 };
        for (int x = 0; x < 4; x++)
            pix.SetPixel(x, 0, vals[x]);

        // 3 gray levels, mindepth 2 --> 2 bpp, levels sorted: 7, 10, 200
        Pix pix2 = pix.ConvertGrayToColormap8(2);
        Assert.Equal(2, pix2.Depth);
        Assert.Equal(3, pix2.Colormap!.Count);
        uint[] idx = { 1, 1, 2, 0 };
        for (int x = 0; x < 4; x++)
            Assert.Equal(idx[x], Px(pix2, x, 0));
        pix2.Colormap!.GetColor(2, out int r, out _, out _);
        Assert.Equal(200, r);

        // mindepth 4 and 8
        Assert.Equal(4, pix.ConvertGrayToColormap8(4).Depth);
        Assert.Equal(8, pix.ConvertGrayToColormap8(8).Depth);
        // invalid mindepth is set to 8
        Assert.Equal(8, pix.ConvertGrayToColormap8(3).Depth);
        // ConvertGrayToColormap on 8 bpp is lossless with mindepth 2
        Assert.Equal(2, pix.ConvertGrayToColormap().Depth);

        // already has a colormap: copy
        Pix copy = pix2.ConvertGrayToColormap();
        Assert.NotSame(pix2, copy);
        Assert.Equal(pix2.Data, copy.Data);

        // more than 16 gray levels need 8 bpp
        Pix many = Pix.Create(20, 1, 8);
        for (int x = 0; x < 20; x++)
            many.SetPixel(x, 0, (uint)(x * 10));
        Pix manyd = many.ConvertGrayToColormap8(2);
        Assert.Equal(8, manyd.Depth);
        Assert.Equal(20, manyd.Colormap!.Count);
        Assert.Equal(19u, Px(manyd, 19, 0));

        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 1).ConvertGrayToColormap());
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 4).ConvertGrayToColormap8(2));
    }

    [Fact]
    public void ColorizeGray()
    {
        Pix pix = Pix.Create(3, 1, 8);
        pix.SetPixel(0, 0, 0);
        pix.SetPixel(1, 0, 51);
        pix.SetPixel(2, 0, 255);
        uint color = Rgb(200, 100, 0);

        Pix pixc = pix.ColorizeGray(color, 0);
        Assert.Equal(32, pixc.Depth);
        // 200 + 51 * 55 / 255 = 211, 100 + 51 * 155 / 255 = 131, 0 + 51
        Assert.Equal(0xc86400ffu, Px(pixc, 0, 0));
        Assert.Equal(0xd38333ffu, Px(pixc, 1, 0));
        Assert.Equal(0xffffffffu, Px(pixc, 2, 0));

        Pix pixm = pix.ColorizeGray(color, 1);
        Assert.Equal(8, pixm.Depth);
        Assert.Equal(256, pixm.Colormap!.Count);
        Assert.Equal(pix.Data, pixm.Data);

        // a colormapped source is first converted to gray
        Pix cm = pix.ConvertGrayToColormap();
        Assert.Equal(Px(pixc, 1, 0), Px(cm.ColorizeGray(color, 0), 1, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 4).ColorizeGray(color, 0));
    }

    /*------------------------------------------------------------*
     *                  Conversion from 16 bpp to 8 bpp           *
     *------------------------------------------------------------*/
    private static Pix Make16(params uint[] vals)
    {
        Pix pix = Pix.Create(vals.Length, 1, 16);
        for (int x = 0; x < vals.Length; x++)
            pix.SetPixel(x, 0, vals[x]);
        return pix;
    }

    [Fact]
    public void Convert16To8HandChecked()
    {
        Pix pix = Make16(0x1234, 0x00ff, 0x0100, 0x0010, 0xffff);

        Pix ls = pix.Convert16To8(L.L_LS_BYTE);
        uint[] expLs = { 0x34, 0xff, 0x00, 0x10, 0xff };
        Pix ms = pix.Convert16To8(L.L_MS_BYTE);
        uint[] expMs = { 0x12, 0x00, 0x01, 0x00, 0xff };
        Pix clip = pix.Convert16To8(L.L_CLIP_TO_FF);
        uint[] expClip = { 0xff, 0xff, 0xff, 0x10, 0xff };
        Pix auto = pix.Convert16To8(L.L_AUTO_BYTE);
        for (int x = 0; x < 5; x++) {
            Assert.Equal(expLs[x], Px(ls, x, 0));
            Assert.Equal(expMs[x], Px(ms, x, 0));
            Assert.Equal(expClip[x], Px(clip, x, 0));
            Assert.Equal(expMs[x], Px(auto, x, 0));   // max > 255: uses the MSB
        }
        Assert.Equal(8, ls.Depth);
        Assert.Throws<ArgumentException>(() => pix.Convert16To8(99));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 8).Convert16To8(L.L_LS_BYTE));
    }

    [Fact]
    public void Convert16To8AutoUsesLsbForSmallValues()
    {
        Pix pix = Make16(5, 255, 17, 0);
        Pix pixd = pix.Convert16To8(L.L_AUTO_BYTE);
        uint[] exp = { 5, 255, 17, 0 };
        for (int x = 0; x < 4; x++)
            Assert.Equal(exp[x], Px(pixd, x, 0));
    }

    [Fact]
    public void Convert16To8AutoTestsOnlyFirstHalfOfLine()
    {
        // NOTE(upstream): only the first wpls (= 2) pixels of the line are tested for values > 255,
        // so the large values in the second half are not seen and the LSB is used.
        Pix pix = Make16(10, 20, 300, 400);
        Pix pixd = pix.Convert16To8(L.L_AUTO_BYTE);
        uint[] exp = { 10, 20, 0x2c, 0x90 };
        for (int x = 0; x < 4; x++)
            Assert.Equal(exp[x], Px(pixd, x, 0));
    }

    [Fact]
    public void ConvertGrayToFalseColor()
    {
        Pix pix8 = Pix.Create(3, 1, 8);
        pix8.SetPixel(1, 0, 100);
        Pix pixd = pix8.ConvertGrayToFalseColor(1.0f);
        Assert.Equal(8, pixd.Depth);
        Assert.NotNull(pixd.Colormap);
        Assert.Equal(pix8.Data, pixd.Data);

        Pix pix16 = Make16(0x1234, 0xff00, 0);
        Pix pixd16 = pix16.ConvertGrayToFalseColor(0.0f);
        Assert.Equal(8, pixd16.Depth);
        Assert.Equal(0x12u, Px(pixd16, 0, 0));
        Assert.Equal(0xffu, Px(pixd16, 1, 0));
        Assert.NotNull(pixd16.Colormap);
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 4).ConvertGrayToFalseColor(1.0f));
    }

    [Fact]
    public void QuantizeIfFewColorsOfColormappedPixIsClone()
    {
        Pix cm = MakeCmapPix(4, 4, 8, 4, new Random(2), color: false);
        Assert.Same(cm, cm.QuantizeIfFewColors(0, 0, 4));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 4).QuantizeIfFewColors(0, 0, 4));
    }

    /*------------------------------------------------------------*
     *                 Unpacking from 1 bpp                       *
     *------------------------------------------------------------*/
    private static readonly int[] Widths = { 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 31, 32, 33, 70 };

    private static Pix RandomBinary(Random rng, int w, int h)
    {
        Pix pix = Pix.Create(w, h, 1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pix.SetPixel(x, y, (uint)rng.Next(2));
        return pix;
    }

    private static void AssertUnpacked(Pix pixs, Pix pixd, int depth, uint val0, uint val1)
    {
        Assert.Equal(depth, pixd.Depth);
        Assert.Equal(pixs.Width, pixd.Width);
        Assert.Equal(pixs.Height, pixd.Height);
        for (int y = 0; y < pixs.Height; y++)
            for (int x = 0; x < pixs.Width; x++)
                Assert.Equal(Px(pixs, x, y) == 0 ? val0 : val1, Px(pixd, x, y));
    }

    [Fact]
    public void Convert1To8HandChecked()
    {
        Pix pix = Pix.Create(10, 1, 1);
        foreach (int x in new[] { 0, 2, 3, 7, 8 })
            pix.SetPixel(x, 0, 1);
        Pix pixd = Pix.Convert1To8(null, pix, 0, 255);
        uint[] exp = { 255, 0, 255, 255, 0, 0, 0, 255, 255, 0 };
        for (int x = 0; x < 10; x++)
            Assert.Equal(exp[x], Px(pixd, x, 0));
        // the first word holds 4 pixels per byte group: 0xff00ffff 0x000000ff ...
        Assert.Equal(0xff00ffffu, pixd.Data[0]);
        Assert.Equal(0x000000ffu, pixd.Data[1]);
        Assert.Equal(0xff000000u, pixd.Data[2]);
    }

    [Fact]
    public void Convert1ToNAllWidths()
    {
        Random rng = new(11);
        foreach (int w in Widths) {
            Pix pix = RandomBinary(rng, w, 3);
            AssertUnpacked(pix, Pix.Convert1To2(null, pix, 0, 3), 2, 0, 3);
            AssertUnpacked(pix, Pix.Convert1To2(null, pix, 2, 1), 2, 2, 1);
            AssertUnpacked(pix, Pix.Convert1To4(null, pix, 0, 15), 4, 0, 15);
            AssertUnpacked(pix, Pix.Convert1To4(null, pix, 9, 6), 4, 9, 6);
            AssertUnpacked(pix, Pix.Convert1To8(null, pix, 0, 255), 8, 0, 255);
            AssertUnpacked(pix, Pix.Convert1To8(null, pix, 100, 7), 8, 100, 7);
            AssertUnpacked(pix, Pix.Convert1To16(null, pix, 0, 0xffff), 16, 0, 0xffff);
            AssertUnpacked(pix, Pix.Convert1To16(null, pix, 0x1234, 0xabcd), 16, 0x1234, 0xabcd);
            AssertUnpacked(pix, Pix.Convert1To32(null, pix, 0, 0xffffffff), 32, 0, 0xffffffff);
            AssertUnpacked(pix, Pix.Convert1To32(null, pix, 0x01020304, 0xa0b0c0d0), 32, 0x01020304, 0xa0b0c0d0);
        }
    }

    [Fact]
    public void Convert1ToNIntoExistingPix()
    {
        Random rng = new(12);
        Pix pix = RandomBinary(rng, 20, 4);
        Pix pixd = Pix.Create(20, 4, 8);
        Assert.Same(pixd, Pix.Convert1To8(pixd, pix, 1, 2));
        AssertUnpacked(pix, pixd, 8, 1, 2);

        Assert.Throws<ArgumentException>(() => Pix.Convert1To8(Pix.Create(19, 4, 8), pix, 0, 255));
        Assert.Throws<ArgumentException>(() => Pix.Convert1To8(Pix.Create(20, 4, 4), pix, 0, 255));
        Assert.Throws<ArgumentException>(() => Pix.Convert1To2(Pix.Create(20, 4, 4), pix, 0, 3));
        Assert.Throws<ArgumentException>(() => Pix.Convert1To4(Pix.Create(20, 4, 8), pix, 0, 15));
        Assert.Throws<ArgumentException>(() => Pix.Convert1To16(Pix.Create(20, 4, 8), pix, 0, 1));
        Assert.Throws<ArgumentException>(() => Pix.Convert1To32(Pix.Create(20, 4, 8), pix, 0, 1));
        Pix gray = Pix.Create(20, 4, 8);
        Assert.Throws<ArgumentException>(() => Pix.Convert1To8(null, gray, 0, 255));
        Assert.Throws<ArgumentException>(() => Pix.Convert1To2(null, gray, 0, 3));
        Assert.Throws<ArgumentException>(() => Pix.Convert1To4(null, gray, 0, 15));
        Assert.Throws<ArgumentException>(() => Pix.Convert1To16(null, gray, 0, 1));
        Assert.Throws<ArgumentException>(() => Pix.Convert1To32(null, gray, 0, 1));
    }

    [Theory]
    [InlineData(2, 3u)]
    [InlineData(4, 15u)]
    [InlineData(8, 255u)]
    [InlineData(16, 0xffffu)]
    [InlineData(32, 0xffffffffu)]
    public void UnpackBinary(int depth, uint max)
    {
        Random rng = new(depth);
        Pix pix = RandomBinary(rng, 37, 3);
        AssertUnpacked(pix, pix.UnpackBinary(depth, 0), depth, 0, max);
        AssertUnpacked(pix, pix.UnpackBinary(depth, 1), depth, max, 0);
    }

    [Fact]
    public void UnpackBinaryInvalidArguments()
    {
        Pix pix = Pix.Create(5, 5, 1);
        Assert.Throws<ArgumentException>(() => pix.UnpackBinary(1, 0));
        Assert.Throws<ArgumentException>(() => pix.UnpackBinary(24, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).UnpackBinary(8, 0));
    }

    [Fact]
    public void Convert1ToNCmapKeepsIndices()
    {
        Random rng = new(13);
        Pix pix = RandomBinary(rng, 21, 3);
        foreach ((Pix pixd, int d) in new[] { (pix.Convert1To2Cmap(), 2), (pix.Convert1To4Cmap(), 4), (pix.Convert1To8Cmap(), 8) }) {
            Assert.Equal(d, pixd.Depth);
            PixColormap cmap = pixd.Colormap!;
            Assert.Equal(2, cmap.Count);
            cmap.GetColor(0, out int r0, out int g0, out int b0);
            cmap.GetColor(1, out int r1, out int g1, out int b1);
            Assert.Equal((255, 255, 255), (r0, g0, b0));
            Assert.Equal((0, 0, 0), (r1, g1, b1));
            AssertUnpacked(pix, pixd, d, 0, 1);
        }
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).Convert1To8Cmap());
    }

    /*------------------------------------------------------------*
     *             Unpacking from 2 and 4 bpp, 8 to 16            *
     *------------------------------------------------------------*/
    private static Pix RandomPix(Random rng, int w, int h, int d)
    {
        Pix pix = Pix.Create(w, h, d);
        uint mask = (1u << d) - 1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pix.SetPixel(x, y, (uint)rng.Next() & mask);
        return pix;
    }

    [Fact]
    public void Convert2To8WithoutColormap()
    {
        Pix pix = Pix.Create(5, 1, 2);
        for (int x = 0; x < 5; x++)
            pix.SetPixel(x, 0, (uint)(x & 3));
        Pix pixd = pix.Convert2To8(0, 85, 170, 255, false);
        uint[] exp = { 0, 85, 170, 255, 0 };
        Assert.Equal(8, pixd.Depth);
        Assert.Null(pixd.Colormap);
        for (int x = 0; x < 5; x++)
            Assert.Equal(exp[x], Px(pixd, x, 0));

        Random rng = new(21);
        foreach (int w in Widths) {
            Pix p = RandomPix(rng, w, 3, 2);
            Pix q = p.Convert2To8(10, 20, 30, 40, false);
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < w; x++)
                    Assert.Equal(10u + 10u * Px(p, x, y), Px(q, x, y));
        }
    }

    [Fact]
    public void Convert2To8WithColormap()
    {
        Pix pix = Pix.Create(4, 1, 2);
        for (int x = 0; x < 4; x++)
            pix.SetPixel(x, 0, (uint)x);

        // no colormap in pixs: the values build the colormap, the pixels become indices
        Pix pixd = pix.Convert2To8(0, 85, 170, 255, true);
        Assert.Equal(4, pixd.Colormap!.Count);
        pixd.Colormap.GetColor(2, out int r, out _, out _);
        Assert.Equal(170, r);
        for (int x = 0; x < 4; x++)
            Assert.Equal((uint)x, Px(pixd, x, 0));

        // colormap in pixs: kept (as an 8 bpp colormap) with cmapflag true; the input values are ignored
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddColor(10, 20, 30);
        cmap.AddColor(40, 50, 60);
        cmap.AddColor(70, 80, 90);
        cmap.AddColor(100, 110, 120);
        pix.SetColormap(cmap);
        Pix pixc = pix.Convert2To8(1, 2, 3, 4, true);
        Assert.Equal(8, pixc.Colormap!.Depth);
        pixc.Colormap.GetColor(3, out int r3, out int g3, out int b3);
        Assert.Equal((100, 110, 120), (r3, g3, b3));
        for (int x = 0; x < 4; x++)
            Assert.Equal((uint)x, Px(pixc, x, 0));

        // colormap in pixs with cmapflag false: removed to gray
        Pix pixg = pix.Convert2To8(1, 2, 3, 4, false);
        Assert.Null(pixg.Colormap);
        Assert.Equal(8, pixg.Depth);
        // 0.3 * 40 + 0.5 * 50 + 0.2 * 60 + 0.5 = 49.5 --> 49
        Assert.Equal(49u, Px(pixg, 1, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 4).Convert2To8(0, 1, 2, 3, false));
    }

    [Fact]
    public void Convert4To8()
    {
        Pix pix = Pix.Create(4, 1, 4);
        pix.SetPixel(0, 0, 0);
        pix.SetPixel(1, 0, 5);
        pix.SetPixel(2, 0, 10);
        pix.SetPixel(3, 0, 15);

        // no colormap, no cmapflag: shift replication
        Pix pixd = pix.Convert4To8(false);
        uint[] exp = { 0x00, 0x55, 0xaa, 0xff };
        for (int x = 0; x < 4; x++)
            Assert.Equal(exp[x], Px(pixd, x, 0));
        Assert.Null(pixd.Colormap);

        // no colormap, cmapflag: linear trc colormap and indices
        Pix pixc = pix.Convert4To8(true);
        Assert.Equal(16, pixc.Colormap!.Count);
        pixc.Colormap.GetColor(10, out int r, out int g, out int b);
        Assert.Equal((170, 170, 170), (r, g, b));
        Assert.Equal(10u, Px(pixc, 2, 0));

        Random rng = new(31);
        foreach (int w in Widths) {
            Pix p = RandomPix(rng, w, 3, 4);
            Pix q = p.Convert4To8(false);
            Pix qc = p.Convert4To8(true);
            for (int y = 0; y < 3; y++) {
                for (int x = 0; x < w; x++) {
                    uint v = Px(p, x, y);
                    Assert.Equal((v << 4) | v, Px(q, x, y));
                    Assert.Equal(v, Px(qc, x, y));
                }
            }
        }

        // with a colormap
        PixColormap cmap = PixColormap.Create(4);
        for (int i = 0; i < 16; i++)
            cmap.AddColor(i, 2 * i, 3 * i);
        pix.SetColormap(cmap);
        Pix withc = pix.Convert4To8(true);
        Assert.Equal(8, withc.Colormap!.Depth);
        Assert.Equal(16, withc.Colormap.Count);
        Assert.Equal(10u, Px(withc, 2, 0));
        Pix gray = pix.Convert4To8(false);
        Assert.Null(gray.Colormap);
        // index 10: 0.3 * 10 + 0.5 * 20 + 0.2 * 30 + 0.5 = 19.5 --> 19
        Assert.Equal(19u, Px(gray, 2, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 2).Convert4To8(false));
    }

    [Fact]
    public void Convert8To16()
    {
        Pix pix = Pix.Create(3, 1, 8);
        pix.SetPixel(0, 0, 0);
        pix.SetPixel(1, 0, 0x12);
        pix.SetPixel(2, 0, 0xff);

        Pix p0 = pix.Convert8To16(0);
        Assert.Equal(16, p0.Depth);
        Assert.Equal(0x12u, Px(p0, 1, 0));
        Pix p4 = pix.Convert8To16(4);
        Assert.Equal(0x120u, Px(p4, 1, 0));
        Assert.Equal(0xff0u, Px(p4, 2, 0));
        Pix p8 = pix.Convert8To16(8);
        Assert.Equal(0x1212u, Px(p8, 1, 0));
        Assert.Equal(0xffffu, Px(p8, 2, 0));
        Assert.Equal(0u, Px(p8, 0, 0));

        // a colormap is first removed to gray
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(0, 0, 0);
        cmap.AddColor(100, 200, 50);
        pix.SetColormap(cmap);
        pix.SetPixel(1, 0, 1);
        pix.SetPixel(2, 0, 1);
        Assert.Equal(140u, Px(pix.Convert8To16(0), 1, 0));

        Assert.Throws<ArgumentException>(() => pix.Convert8To16(-1));
        Assert.Throws<ArgumentException>(() => pix.Convert8To16(9));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 4).Convert8To16(0));
    }

    /*------------------------------------------------------------*
     *               Top-level conversion to 2 and 4 bpp          *
     *------------------------------------------------------------*/
    [Fact]
    public void Convert8To2TakesTopTwoBits()
    {
        Pix pix = Pix.Create(6, 1, 8);
        uint[] vals = { 0x00, 0x3f, 0x40, 0x80, 0xc0, 0xff };
        for (int x = 0; x < 6; x++)
            pix.SetPixel(x, 0, vals[x]);
        Pix pixd = pix.Convert8To2();
        Assert.Equal(2, pixd.Depth);
        uint[] exp = { 0, 0, 1, 2, 3, 3 };
        for (int x = 0; x < 6; x++)
            Assert.Equal(exp[x], Px(pixd, x, 0));

        Random rng = new(41);
        foreach (int w in Widths) {
            Pix p = RandomPix(rng, w, 3, 8);
            Pix q = p.Convert8To2();
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < w; x++)
                    Assert.Equal(Px(p, x, y) >> 6, Px(q, x, y));
        }
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 4).Convert8To2());
    }

    [Fact]
    public void Convert8To4TakesTopFourBits()
    {
        Random rng = new(42);
        foreach (int w in Widths) {
            Pix p = RandomPix(rng, w, 3, 8);
            Pix q = p.Convert8To4();
            Assert.Equal(4, q.Depth);
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < w; x++)
                    Assert.Equal(Px(p, x, y) >> 4, Px(q, x, y));
        }
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 2).Convert8To4());
    }

    [Fact]
    public void ConvertTo2AllDepths()
    {
        // 1 bpp: 0 --> 3, 1 --> 0
        Pix p1 = Pix.Create(3, 1, 1);
        p1.SetPixel(1, 0, 1);
        Pix q1 = p1.ConvertTo2();
        Assert.Equal(2, q1.Depth);
        Assert.Equal(3u, Px(q1, 0, 0));
        Assert.Equal(0u, Px(q1, 1, 0));

        // 2 bpp without colormap: a copy
        Pix p2 = Pix.Create(4, 1, 2);
        for (int x = 0; x < 4; x++)
            p2.SetPixel(x, 0, (uint)x);
        Pix q2 = p2.ConvertTo2();
        Assert.NotSame(p2, q2);
        Assert.Equal(p2.Data, q2.Data);

        // 4 bpp: via 8 bpp, top two bits of the replicated value
        Pix p4 = Pix.Create(4, 1, 4);
        uint[] v4 = { 0, 5, 10, 15 };
        for (int x = 0; x < 4; x++)
            p4.SetPixel(x, 0, v4[x]);
        Pix q4 = p4.ConvertTo2();
        uint[] e4 = { 0, 1, 2, 3 };
        for (int x = 0; x < 4; x++)
            Assert.Equal(e4[x], Px(q4, x, 0));

        // 8 bpp and 32 bpp
        Pix p8 = Pix.Create(3, 1, 8);
        p8.SetPixel(0, 0, 10);
        p8.SetPixel(1, 0, 130);
        p8.SetPixel(2, 0, 250);
        Pix q8 = p8.ConvertTo2();
        Assert.Equal(0u, Px(q8, 0, 0));
        Assert.Equal(2u, Px(q8, 1, 0));
        Assert.Equal(3u, Px(q8, 2, 0));
        Pix q32 = MakeRgb((100, 200, 50), (255, 255, 255)).ConvertTo2();
        Assert.Equal(2u, Px(q32, 0, 0));   // gray 140
        Assert.Equal(3u, Px(q32, 1, 0));

        // colormap: removed to gray
        Pix pc = MakeCmapPix(5, 1, 2, 4, new Random(3), color: false);
        Assert.Null(pc.ConvertTo2().Colormap);
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 16).ConvertTo2());
    }

    [Fact]
    public void ConvertTo4AllDepths()
    {
        Pix p1 = Pix.Create(3, 1, 1);
        p1.SetPixel(1, 0, 1);
        Pix q1 = p1.ConvertTo4();
        Assert.Equal(4, q1.Depth);
        Assert.Equal(15u, Px(q1, 0, 0));
        Assert.Equal(0u, Px(q1, 1, 0));

        Pix p2 = Pix.Create(4, 1, 2);
        for (int x = 0; x < 4; x++)
            p2.SetPixel(x, 0, (uint)x);
        Pix q2 = p2.ConvertTo4();
        uint[] e2 = { 0, 5, 10, 15 };
        for (int x = 0; x < 4; x++)
            Assert.Equal(e2[x], Px(q2, x, 0));

        Pix p4 = RandomPix(new Random(4), 9, 2, 4);
        Pix q4 = p4.ConvertTo4();
        Assert.NotSame(p4, q4);
        Assert.Equal(p4.Data, q4.Data);

        Pix p8 = Pix.Create(2, 1, 8);
        p8.SetPixel(0, 0, 0x7f);
        p8.SetPixel(1, 0, 0xf0);
        Pix q8 = p8.ConvertTo4();
        Assert.Equal(7u, Px(q8, 0, 0));
        Assert.Equal(15u, Px(q8, 1, 0));
        Assert.Equal(8u, Px(MakeRgb((100, 200, 50)).ConvertTo4(), 0, 0));   // gray 140 = 0x8c
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 16).ConvertTo4());
    }

    /*------------------------------------------------------------*
     *               Top-level conversion to 1 bpp                *
     *------------------------------------------------------------*/
    [Fact]
    public void ConvertTo1OfBinaryPix()
    {
        Random rng = new(51);
        Pix pix = RandomBinary(rng, 13, 3);
        foreach (Pix pixd in new[] { pix.ConvertTo1(128), pix.ConvertTo1Adaptive() }) {
            Assert.NotSame(pix, pixd);
            Assert.Equal(pix.Data, pixd.Data);
        }

        // colormapped: the colormap is removed; inverted if the photometry is not standard
        Pix cm1 = Pix.Copy(null, pix);
        PixColormap cmap = PixColormap.Create(1);
        cmap.AddColor(0, 0, 0);
        cmap.AddColor(255, 255, 255);
        cm1.SetColormap(cmap);
        Pix out1 = cm1.ConvertTo1(128);
        Assert.Null(out1.Colormap);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 13; x++)
                Assert.Equal(1u - Px(pix, x, y), Px(out1, x, y));
        Pix out2 = cm1.ConvertTo1Adaptive();
        Assert.Equal(out1.Data, out2.Data);

        // standard photometry: kept
        Pix cm2 = Pix.Copy(null, pix);
        PixColormap cmap2 = PixColormap.Create(1);
        cmap2.AddColor(255, 255, 255);
        cmap2.AddColor(0, 0, 0);
        cm2.SetColormap(cmap2);
        Pix out3 = cm2.ConvertTo1(128);
        Assert.Null(out3.Colormap);
        Assert.Equal(pix.Data, out3.Data);

        Assert.Throws<ArgumentException>(() => pix.ConvertTo1BySampling(0, 128));
    }

    /*------------------------------------------------------------*
     *               Top-level conversion to 8 bpp                *
     *------------------------------------------------------------*/
    [Fact]
    public void ConvertTo8FromEachDepth()
    {
        // 1 bpp
        Pix p1 = Pix.Create(3, 1, 1);
        p1.SetPixel(1, 0, 1);
        Pix q1 = p1.ConvertTo8(false);
        Assert.Equal(255u, Px(q1, 0, 0));
        Assert.Equal(0u, Px(q1, 1, 0));
        Assert.Null(q1.Colormap);
        Pix q1c = p1.ConvertTo8(true);
        Assert.Equal(2, q1c.Colormap!.Count);
        Assert.Equal(1u, Px(q1c, 1, 0));

        // 2 bpp
        Pix p2 = Pix.Create(4, 1, 2);
        for (int x = 0; x < 4; x++)
            p2.SetPixel(x, 0, (uint)x);
        Pix q2 = p2.ConvertTo8(false);
        uint[] e2 = { 0, 85, 170, 255 };
        for (int x = 0; x < 4; x++)
            Assert.Equal(e2[x], Px(q2, x, 0));
        Assert.Equal(4, p2.ConvertTo8(true).Colormap!.Count);

        // 4 bpp
        Pix p4 = Pix.Create(2, 1, 4);
        p4.SetPixel(0, 0, 3);
        p4.SetPixel(1, 0, 12);
        Pix q4 = p4.ConvertTo8(false);
        Assert.Equal(0x33u, Px(q4, 0, 0));
        Assert.Equal(0xccu, Px(q4, 1, 0));
        Assert.Equal(16, p4.ConvertTo8(true).Colormap!.Count);

        // 16 bpp: MSB; with a colormap flag: gray colormap
        Pix p16 = Make16(0x1234, 0xff00);
        Pix q16 = p16.ConvertTo8(false);
        Assert.Equal(0x12u, Px(q16, 0, 0));
        Assert.Equal(0xffu, Px(q16, 1, 0));
        Pix q16c = p16.ConvertTo8(true);
        Assert.Equal(256, q16c.Colormap!.Count);
        Assert.Equal(0x12u, Px(q16c, 0, 0));

        // 32 bpp: luminance
        Pix p32 = MakeRgb((100, 200, 50), (0, 0, 0));
        Pix q32 = p32.ConvertTo8(false);
        Assert.Equal(140u, Px(q32, 0, 0));
        Assert.Equal(256, p32.ConvertTo8(true).Colormap!.Count);

        // 24 bpp: through 32 bpp
        Pix q24 = p32.Convert32To24().ConvertTo8(false);
        Assert.Equal(140u, Px(q24, 0, 0));

        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 8).ConvertTo8BySampling(0, 0));
    }

    [Fact]
    public void ConvertTo8FromEightBpp()
    {
        Pix p8 = Pix.Create(3, 1, 8);
        p8.SetPixel(1, 0, 99);
        // cmap flag equal to the existence of a colormap: a copy
        Pix c0 = p8.ConvertTo8(false);
        Assert.NotSame(p8, c0);
        Assert.Equal(p8.Data, c0.Data);
        // add a gray colormap
        Pix c1 = p8.ConvertTo8(true);
        Assert.Equal(256, c1.Colormap!.Count);
        Assert.Equal(99u, Px(c1, 1, 0));
        Assert.Null(p8.Colormap);
        // remove the colormap
        Pix c2 = c1.ConvertTo8(false);
        Assert.Null(c2.Colormap);
        Assert.Equal(99u, Px(c2, 1, 0));
        // keep it
        Pix c3 = c1.ConvertTo8(true);
        Assert.NotSame(c1, c3);
        Assert.Equal(256, c3.Colormap!.Count);
    }

    [Fact]
    public void ConvertTo8ColormapKeepsColormapForNon32bpp()
    {
        Pix p4 = RandomPix(new Random(7), 5, 2, 4);
        Assert.NotNull(p4.ConvertTo8Colormap(0).Colormap);
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 24).ConvertTo8Colormap(0));
    }

    [Fact]
    public void ConvertTo16()
    {
        Pix p1 = Pix.Create(3, 1, 1);
        p1.SetPixel(1, 0, 1);
        Pix q1 = p1.ConvertTo16();
        Assert.Equal(16, q1.Depth);
        Assert.Equal(0xffffu, Px(q1, 0, 0));
        Assert.Equal(0u, Px(q1, 1, 0));

        Pix p8 = Pix.Create(2, 1, 8);
        p8.SetPixel(0, 0, 0x12);
        p8.SetPixel(1, 0, 0xff);
        Pix q8 = p8.ConvertTo16();
        Assert.Equal(0x1212u, Px(q8, 0, 0));
        Assert.Equal(0xffffu, Px(q8, 1, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 4).ConvertTo16());
    }

    /*------------------------------------------------------------*
     *               Top-level conversion to 32 bpp               *
     *------------------------------------------------------------*/
    [Fact]
    public void Convert8To32HandChecked()
    {
        Pix pix = Pix.Create(3, 1, 8);
        pix.SetPixel(0, 0, 0x12);
        pix.SetPixel(1, 0, 0xff);
        Pix pixd = pix.Convert8To32();
        Assert.Equal(32, pixd.Depth);
        Assert.Equal(0x12121200u, Px(pixd, 0, 0));
        Assert.Equal(0xffffff00u, Px(pixd, 1, 0));
        Assert.Equal(0u, Px(pixd, 2, 0));

        // with a colormap: the colormap colors
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(1, 2, 3);
        cmap.AddColor(10, 20, 30);
        pix.SetColormap(cmap);
        pix.SetPixel(0, 0, 1);
        pix.SetPixel(1, 0, 0);
        Pix pixc = pix.Convert8To32();
        Assert.Equal(Rgb(10, 20, 30), Px(pixc, 0, 0));
        Assert.Equal(Rgb(1, 2, 3), Px(pixc, 1, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 4).Convert8To32());
    }

    [Fact]
    public void ConvertTo32FromEachDepth()
    {
        Pix p1 = Pix.Create(3, 1, 1);
        p1.SetPixel(1, 0, 1);
        Pix q1 = p1.ConvertTo32();
        Assert.Equal(0xffffffffu, Px(q1, 0, 0));
        Assert.Equal(0u, Px(q1, 1, 0));

        Pix p2 = Pix.Create(4, 1, 2);
        for (int x = 0; x < 4; x++)
            p2.SetPixel(x, 0, (uint)x);
        Pix q2 = p2.ConvertTo32();
        Assert.Equal(0x55555500u, Px(q2, 1, 0));
        Assert.Equal(0xffffff00u, Px(q2, 3, 0));

        Pix p4 = Pix.Create(2, 1, 4);
        p4.SetPixel(0, 0, 3);
        p4.SetPixel(1, 0, 12);
        Pix q4 = p4.ConvertTo32();
        Assert.Equal(0x33333300u, Px(q4, 0, 0));
        Assert.Equal(0xcccccc00u, Px(q4, 1, 0));

        Pix p8 = Pix.Create(1, 1, 8);
        p8.SetPixel(0, 0, 0x40);
        Assert.Equal(0x40404000u, Px(p8.ConvertTo32(), 0, 0));

        Pix p16 = Make16(0x1234);
        Assert.Equal(0x12121200u, Px(p16.ConvertTo32(), 0, 0));

        Pix p32 = MakeRgb((1, 2, 3));
        Pix q32 = p32.ConvertTo32();
        Assert.NotSame(p32, q32);
        Assert.Equal(p32.Data, q32.Data);

        Pix q24 = p32.Convert32To24().ConvertTo32();
        Assert.Equal(Rgb(1, 2, 3), Px(q24, 0, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 32).ConvertTo32BySampling(0));
    }

    [Fact]
    public void Convert24To32AndBack()
    {
        Random rng = new(61);
        foreach (int w in new[] { 1, 2, 3, 4, 5, 9, 33 }) {
            Pix p32 = Pix.Create(w, 3, 32);
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < w; x++)
                    p32.SetPixel(x, y, Rgb(rng.Next(256), rng.Next(256), rng.Next(256)));
            Pix p24 = p32.Convert32To24();
            Assert.Equal(24, p24.Depth);
            Pix back = p24.Convert24To32();
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < w; x++)
                    Assert.Equal(Px(p32, x, y) & 0xffffff00u, Px(back, x, y));
        }
    }

    [Fact]
    public void Convert32To24ByteLayout()
    {
        Pix p32 = MakeRgb((0x11, 0x22, 0x33), (0x44, 0x55, 0x66));
        Pix p24 = p32.Convert32To24();
        byte[] bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(p24.Data.AsSpan()).ToArray();
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 }, bytes[..6]);
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 8).Convert24To32());
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 8).Convert32To24());
    }

    [Fact]
    public void ConvertTo8Or32()
    {
        Pix p8 = Pix.Create(3, 1, 8);
        Assert.Same(p8, p8.ConvertTo8Or32(L.L_CLONE, 0));
        Pix copy = p8.ConvertTo8Or32(L.L_COPY, 0);
        Assert.NotSame(p8, copy);
        Assert.Equal(p8.Data, copy.Data);

        Pix p32 = MakeRgb((1, 2, 3));
        Assert.Same(p32, p32.ConvertTo8Or32(L.L_CLONE, 1));

        // other depths: to 8 bpp gray
        Pix p4 = RandomPix(new Random(8), 5, 1, 4);
        Assert.Equal(8, p4.ConvertTo8Or32(L.L_CLONE, 0).Depth);

        // colormapped: removed based on its content
        Pix gray = MakeCmapPix(5, 1, 4, 5, new Random(9), color: false);
        Pix g = gray.ConvertTo8Or32(L.L_CLONE, 1);
        Assert.Equal(8, g.Depth);
        Assert.Null(g.Colormap);
        Pix col = MakeCmapPix(5, 1, 4, 5, new Random(9), color: true);
        Assert.Equal(32, col.ConvertTo8Or32(L.L_CLONE, 0).Depth);

        Assert.Throws<ArgumentException>(() => p8.ConvertTo8Or32(7, 0));
    }

    /*------------------------------------------------------------*
     *            Conversion between 32 bpp and 16 or 8 bpp       *
     *------------------------------------------------------------*/
    [Fact]
    public void Convert32To16HandChecked()
    {
        Pix pix = Pix.Create(3, 1, 32);
        pix.SetPixel(0, 0, 0x12345678);
        pix.SetPixel(1, 0, 0x0000abcd);
        pix.SetPixel(2, 0, 0xffff0001);

        Pix ls = pix.Convert32To16(L.L_LS_TWO_BYTES);
        Assert.Equal(16, ls.Depth);
        Assert.Equal(0x5678u, Px(ls, 0, 0));
        Assert.Equal(0xabcdu, Px(ls, 1, 0));
        Assert.Equal(0x0001u, Px(ls, 2, 0));

        Pix ms = pix.Convert32To16(L.L_MS_TWO_BYTES);
        Assert.Equal(0x1234u, Px(ms, 0, 0));
        Assert.Equal(0u, Px(ms, 1, 0));
        Assert.Equal(0xffffu, Px(ms, 2, 0));

        Pix clip = pix.Convert32To16(L.L_CLIP_TO_FFFF);
        Assert.Equal(0xffffu, Px(clip, 0, 0));
        Assert.Equal(0xabcdu, Px(clip, 1, 0));
        Assert.Equal(0xffffu, Px(clip, 2, 0));

        Assert.Throws<ArgumentException>(() => pix.Convert32To16(L.L_LS_BYTE));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 8).Convert32To16(L.L_LS_TWO_BYTES));
    }

    [Fact]
    public void Convert32To8HandChecked()
    {
        Pix pix = Pix.Create(2, 1, 32);
        pix.SetPixel(0, 0, 0x12345678);
        pix.SetPixel(1, 0, 0x0000abcd);

        Pix a = pix.Convert32To8(L.L_LS_TWO_BYTES, L.L_LS_BYTE);
        Assert.Equal(8, a.Depth);
        Assert.Equal(0x78u, Px(a, 0, 0));
        Assert.Equal(0xcdu, Px(a, 1, 0));
        Pix b = pix.Convert32To8(L.L_MS_TWO_BYTES, L.L_MS_BYTE);
        Assert.Equal(0x12u, Px(b, 0, 0));
        Assert.Equal(0u, Px(b, 1, 0));
        Pix c = pix.Convert32To8(L.L_LS_TWO_BYTES, L.L_CLIP_TO_FF);
        Assert.Equal(0xffu, Px(c, 0, 0));
        Assert.Equal(0xffu, Px(c, 1, 0));

        Assert.Throws<ArgumentException>(() => pix.Convert32To8(99, L.L_LS_BYTE));
        Assert.Throws<ArgumentException>(() => pix.Convert32To8(L.L_LS_TWO_BYTES, L.L_AUTO_BYTE));
    }

    /*------------------------------------------------------------*
     *                  Alpha, lossless, PostScript               *
     *------------------------------------------------------------*/
    [Fact]
    public void RemoveAlphaWithoutAlphaIsClone()
    {
        Pix p32 = MakeRgb((1, 2, 3));
        Assert.Same(p32, p32.RemoveAlpha());
        Pix p8 = Pix.Create(3, 3, 8);
        Assert.Same(p8, p8.RemoveAlpha());
    }

    [Fact]
    public void AddAlphaTo1bpp()
    {
        Pix pix = Pix.Create(5, 2, 1);
        pix.SetPixel(2, 1, 1);
        Pix pixd = Pix.AddAlphaTo1bpp(null, pix);
        Assert.NotSame(pix, pixd);
        Assert.Equal(pix.Data, pixd.Data);
        PixColormap cmap = pixd.Colormap!;
        Assert.Equal(2, cmap.Count);
        cmap.GetRGBA(0, out int r0, out int g0, out int b0, out int a0);
        cmap.GetRGBA(1, out int r1, out int g1, out int b1, out int a1);
        Assert.Equal((255, 255, 255, 0), (r0, g0, b0, a0));
        Assert.Equal((0, 0, 0, 255), (r1, g1, b1, a1));
        Assert.Null(pix.Colormap);

        // pixd == pixs: in place
        Pix same = Pix.AddAlphaTo1bpp(pix, pix);
        Assert.Same(pix, same);
        Assert.NotNull(pix.Colormap);

        Assert.Throws<ArgumentException>(() => Pix.AddAlphaTo1bpp(Pix.Create(5, 2, 1), Pix.Create(5, 2, 1)));
        Assert.Throws<ArgumentException>(() => Pix.AddAlphaTo1bpp(null, Pix.Create(5, 2, 8)));
    }

    [Fact]
    public void ConvertLossless()
    {
        Random rng = new(71);
        foreach (int w in Widths) {
            Pix p1 = RandomBinary(rng, w, 3);
            foreach (int d in new[] { 2, 4, 8 }) {
                Pix q = p1.ConvertLossless(d);
                Assert.Equal(d, q.Depth);
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < w; x++)
                        Assert.Equal(Px(p1, x, y), Px(q, x, y));
            }
            Pix p2 = RandomPix(rng, w, 3, 2);
            foreach (int d in new[] { 4, 8 }) {
                Pix q = p2.ConvertLossless(d);
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < w; x++)
                        Assert.Equal(Px(p2, x, y), Px(q, x, y));
            }
        }

        // same depth: a copy
        Pix p8 = RandomPix(rng, 7, 2, 8);
        Pix c = p8.ConvertLossless(8);
        Assert.NotSame(p8, c);
        Assert.Equal(p8.Data, c.Data);

        Assert.Throws<ArgumentException>(() => p8.ConvertLossless(4));
        Assert.Throws<ArgumentException>(() => p8.ConvertLossless(16));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 16).ConvertLossless(8));
        Pix cm = MakeCmapPix(3, 1, 4, 4, rng, color: false);
        Assert.Throws<ArgumentException>(() => cm.ConvertLossless(8));
    }

    [Fact]
    public void ConvertLosslessFourBppReadsDibitsLikeC()
    {
        // NOTE(upstream): the C code reads a 4 bpp source with GET_DATA_DIBIT, so output pixel j
        // is the 2 bit value at position j of the source line, not the 4 bit value.
        Pix pix = Pix.Create(4, 1, 4);
        uint[] vals = { 1, 2, 3, 4 };
        for (int x = 0; x < 4; x++)
            pix.SetPixel(x, 0, vals[x]);
        // data word: 0x1234xxxx; dibits from the top: 0, 1, 0, 2
        Pix pixd = pix.ConvertLossless(8);
        uint[] exp = { 0, 1, 0, 2 };
        for (int x = 0; x < 4; x++)
            Assert.Equal(exp[x], Px(pixd, x, 0));
    }

    [Fact]
    public void ConvertForPSWrap()
    {
        Pix p1 = Pix.Create(3, 3, 1);
        Assert.Same(p1, p1.ConvertForPSWrap());
        Pix p32 = MakeRgb((1, 2, 3));
        Assert.Same(p32, p32.ConvertForPSWrap());

        Pix p2 = Pix.Create(4, 1, 2);
        p2.SetPixel(1, 0, 1);
        Pix q2 = p2.ConvertForPSWrap();
        Assert.Equal(8, q2.Depth);
        Assert.Equal(0x55u, Px(q2, 1, 0));

        Pix p4 = Pix.Create(2, 1, 4);
        p4.SetPixel(1, 0, 15);
        Assert.Equal(0xffu, Px(p4.ConvertForPSWrap(), 1, 0));

        Pix p8 = Pix.Create(2, 1, 8);
        p8.SetPixel(1, 0, 15);
        Assert.Same(p8, p8.ConvertForPSWrap());

        Pix c8 = MakeCmapPix(5, 1, 8, 5, new Random(10), color: true);
        Assert.Equal(32, c8.ConvertForPSWrap().Depth);
        Pix c4 = MakeCmapPix(5, 1, 4, 5, new Random(10), color: false);
        Assert.Equal(8, c4.ConvertForPSWrap().Depth);
        Pix c2 = MakeCmapPix(5, 1, 2, 4, new Random(10), color: false);
        Assert.Equal(8, c2.ConvertForPSWrap().Depth);

        Assert.Equal(0x12u, Px(Make16(0x1234).ConvertForPSWrap(), 0, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 24).ConvertForPSWrap());
    }

    [Fact]
    public void ConvertCmapTo1()
    {
        // mostly light, a few dark pixels: the dark colors become foreground (1)
        Pix pix = MakeBlackWhiteish(false);
        Pix pixd = pix.ConvertCmapTo1();
        Assert.Equal(1, pixd.Depth);
        for (int y = 0; y < 10; y++) {
            for (int x = 0; x < 10; x++) {
                bool dark = y == 0;
                Assert.Equal(dark ? 1u : 0u, Px(pixd, x, y));
            }
        }

        // mostly dark: the result is inverted, so the dark majority is background (0)
        Pix pixm = MakeBlackWhiteish(true);
        Pix pixe = pixm.ConvertCmapTo1();
        for (int y = 0; y < 10; y++) {
            for (int x = 0; x < 10; x++) {
                bool dark = y != 0;
                Assert.Equal(dark ? 0u : 1u, Px(pixe, x, y));
            }
        }
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 8).ConvertCmapTo1());
    }

    // 8 bpp, 4 colors: two dark (indices 0, 1) and two light (2, 3). Row 0 uses a dark index
    // and the other rows a light one, or v.v. if inverse.
    private static Pix MakeBlackWhiteish(bool inverse)
    {
        Pix pix = Pix.Create(10, 10, 8);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(0, 0, 0);
        cmap.AddColor(30, 30, 30);
        cmap.AddColor(220, 220, 220);
        cmap.AddColor(255, 255, 255);
        pix.SetColormap(cmap);
        for (int y = 0; y < 10; y++) {
            for (int x = 0; x < 10; x++) {
                uint idx;
                if (!inverse)
                    idx = y == 0 ? (uint)(x & 1) : (uint)(2 + (x & 1));
                else
                    idx = y == 0 ? (uint)(2 + (x & 1)) : (uint)(x & 1);
                pix.SetPixel(x, y, idx);
            }
        }
        return pix;
    }

    [Fact]
    public void NeutralBoostValueChangesBoostConversion()
    {
        Pix pix = MakeRgb((100, 200, 60));
        try {
            Pix.SetNeutralBoostVal(100);
            // min 60: 60 * 60 / 100 = 36; max 200: 200 * 200 / 100 = 400 --> 255
            Assert.Equal(36u, Px(pix.ConvertRGBToGrayMinMax(L.L_CHOOSE_MIN_BOOST), 0, 0));
            Assert.Equal(255u, Px(pix.ConvertRGBToGrayMinMax(L.L_CHOOSE_MAX_BOOST), 0, 0));
            // an invalid value is ignored
            Pix.SetNeutralBoostVal(0);
            Assert.Equal(36u, Px(pix.ConvertRGBToGrayMinMax(L.L_CHOOSE_MIN_BOOST), 0, 0));
        } finally {
            Pix.SetNeutralBoostVal(180);
        }
        Assert.Equal(20u, Px(pix.ConvertRGBToGrayMinMax(L.L_CHOOSE_MIN_BOOST), 0, 0));   // 60 * 60 / 180
    }

    [Fact]
    public void ConvertToSubpixelRGBRejectsBadArguments()
    {
        Pix gray = Pix.Create(6, 6, 8);
        Assert.Throws<ArgumentException>(() => gray.ConvertToSubpixelRGB(0.0f, 1.0f, L.L_SUBPIXEL_ORDER_RGB));
        Assert.Throws<ArgumentException>(() => gray.ConvertToSubpixelRGB(1.0f, 1.0f, 9));
        Assert.Throws<ArgumentException>(() => Pix.Create(6, 6, 4).ConvertToSubpixelRGB(1.0f, 1.0f, L.L_SUBPIXEL_ORDER_RGB));
        Assert.Throws<ArgumentException>(() => Pix.Create(6, 6, 4).ConvertGrayToSubpixelRGB(1.0f, 1.0f, L.L_SUBPIXEL_ORDER_RGB));
        Assert.Throws<ArgumentException>(() => gray.ConvertColorToSubpixelRGB(1.0f, 1.0f, L.L_SUBPIXEL_ORDER_RGB));
        Assert.Throws<ArgumentException>(() => gray.ConvertGrayToSubpixelRGB(1.0f, -1.0f, L.L_SUBPIXEL_ORDER_RGB));
    }
}
