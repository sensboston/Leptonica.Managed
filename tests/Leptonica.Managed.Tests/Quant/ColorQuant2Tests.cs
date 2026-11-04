using System;
using System.Reflection;
using Leptonica;

namespace Leptonica.Managed.Tests.QuantTests;

public class ColorQuant2Tests
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

    private static (int r, int g, int b) CmapColorAt(Pix pixd, int x, int y)
    {
        pixd.GetPixel(x, y, out uint index);
        pixd.Colormap!.GetColor((int)index, out int r, out int g, out int b);
        return (r, g, b);
    }

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    [Fact]
    public void MedianCutPaletteMappingMatchesScalarAtWordBoundaries()
    {
        var rng = new Random(913704);
        var method = typeof(Pix).GetMethod("QuantizeWithColormap", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (int sigbits in new[] { 5, 6 }) {
            int[] indexmap = new int[1 << (3 * sigbits)];
            for (int i = 0; i < indexmap.Length; i++) indexmap[i] = rng.Next(256);
            foreach (int width in new[] { 1, 31, 32, 33, 63, 64, 65, 71, 257 }) {
                Pix src = Pix.Create(width, 3, 32);
                for (int i = 0; i < src.Data.Length; i++)
                    src.Data[i] = (uint)rng.NextInt64(0, 1L << 32);
                PixColormap cmap = PixColormap.Create(8);
                for (int i = 0; i < 256; i++) cmap.AddColor(i, 255 - i, (i * 37) & 255);
                Pix expected = Pix.Create(width, 3, 8);
                int shift = 8 - sigbits;
                for (int y = 0; y < 3; y++) {
                    for (int x = 0; x < width; x++) {
                        uint pixel = src.Data[y * src.Wpl + x];
                        int r = (int)((pixel >> 24) & 255) >> shift;
                        int g = (int)((pixel >> 16) & 255) >> shift;
                        int b = (int)((pixel >> 8) & 255) >> shift;
                        int index = (r << (2 * sigbits)) | (g << sigbits) | b;
                        expected.SetPixel(x, y, (uint)indexmap[index]);
                    }
                }
                Pix actual = (Pix)method.Invoke(null, new object[] { src, 0, 8, cmap, indexmap,
                                                                     indexmap.Length, sigbits })!;
                Assert.Equal(expected.Data, actual.Data);
                Assert.Same(cmap, actual.Colormap);
            }
        }
    }

    [Fact]
    public void MedianCutHisto_IndexesByQuantizedRgb()
    {
        Pix pix = MakeRgb(3, 1, (x, y) => x switch { 0 => (255, 0, 0), 1 => (0, 255, 0), _ => (0, 0, 255) });
        int[] histo = pix.MedianCutHisto(5, 1);
        Assert.Equal(1 << 15, histo.Length);
        Assert.Equal(1, histo[31 << 10]);
        Assert.Equal(1, histo[31 << 5]);
        Assert.Equal(1, histo[31]);
        Assert.Equal(3, Array.FindAll(histo, v => v != 0).Length);

        int[] histo6 = pix.MedianCutHisto(6, 1);
        Assert.Equal(1 << 18, histo6.Length);
        Assert.Equal(1, histo6[63 << 12]);
    }

    [Fact]
    public void MedianCutHisto_Subsamples()
    {
        Pix pix = MakeRgb(4, 4, (x, y) => (x * 60, y * 60, 0));
        int[] histo = pix.MedianCutHisto(5, 2);
        int total = 0;
        foreach (int v in histo) total += v;
        Assert.Equal(4, total);   // pixels (0,0) (2,0) (0,2) (2,2)
    }

    [Fact]
    public void MedianCutHisto_InvalidArgs()
    {
        Pix pix = Pix.Create(2, 2, 32);
        Assert.Throws<ArgumentException>(() => pix.MedianCutHisto(4, 1));
        Assert.Throws<ArgumentException>(() => pix.MedianCutHisto(7, 1));
        Assert.Throws<ArgumentException>(() => pix.MedianCutHisto(5, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 8).MedianCutHisto(5, 1));
    }

    [Fact]
    public void MedianCutQuantGeneral_FewColorsUseTheHistogramColors()
    {
        Pix pix = MakeRgb(9, 3, (x, y) => (x / 3) switch { 0 => (200, 30, 30), 1 => (30, 200, 30), _ => (30, 30, 200) });
        Pix pixd = pix.MedianCutQuantGeneral(0, 0, 256, 0, 1, 0);
        Assert.Equal(2, pixd.Depth);
        Assert.Equal(3, pixd.Colormap!.Count);
        // the colors are the lower corners of the 5 bit cells
        Assert.Equal((200, 24, 24), CmapColorAt(pixd, 0, 0));
        Assert.Equal((24, 200, 24), CmapColorAt(pixd, 4, 1));
        Assert.Equal((24, 24, 200), CmapColorAt(pixd, 8, 2));
    }

    [Fact]
    public void MedianCutQuantGeneral_OutdepthOne()
    {
        Pix pix = MakeRgb(4, 1, (x, y) => x < 2 ? (0, 0, 0) : (255, 255, 255));
        Pix pixd = pix.MedianCutQuantGeneral(0, 1, 2, 0, 1, 0);
        Assert.Equal(1, pixd.Depth);
        Assert.Equal(2, pixd.Colormap!.Count);
        Assert.Equal(0u, Get(pixd, 0, 0));
        Assert.Equal(1u, Get(pixd, 3, 0));
    }

    [Fact]
    public void MedianCutQuantGeneral_ManyColorsNoDither()
    {
        Pix pix = MakeRgb(64, 64, (x, y) => (x * 4, y * 4, (x + y) * 2));
        Pix pixd = pix.MedianCutQuantGeneral(0, 0, 16, 0, 1, 0);
        Assert.Equal(4, pixd.Depth);
        int n = pixd.Colormap!.Count;
        Assert.InRange(n, 8, 16);
        long err = 0;
        int count = 0;
        for (int y = 0; y < 64; y += 3)
            for (int x = 0; x < 64; x += 3) {
                var (r, g, b) = CmapColorAt(pixd, x, y);
                err += Math.Abs(r - x * 4) + Math.Abs(g - y * 4) + Math.Abs(b - (x + y) * 2);
                count++;
            }
        Assert.True(err / (3.0 * count) < 40, $"mean error {err / (3.0 * count)}");
    }

    [Fact]
    public void MedianCutQuantGeneral_DitherGives8bpp()
    {
        Pix pix = MakeRgb(64, 32, (x, y) => (x * 4, y * 8, (x + y) * 3));
        Pix pixd = pix.MedianCutQuantGeneral(1, 0, 32, 6, 1, 0);
        Assert.Equal(8, pixd.Depth);
        Assert.InRange(pixd.Colormap!.Count, 8, 32);
        Assert.Equal(64, pixd.Width);
    }

    [Fact]
    public void MedianCutQuantGeneral_SubsampledLargeImage()
    {
        Pix pix = MakeRgb(400, 300, (x, y) => ((x * 5) % 256, (y * 7) % 256, ((x + y) * 3) % 256));
        Pix pixd = pix.MedianCutQuantGeneral(0, 8, 64, 0, 0, 0);
        Assert.Equal(8, pixd.Depth);
        Assert.InRange(pixd.Colormap!.Count, 16, 64);
    }

    [Fact]
    public void MedianCutQuantGeneral_InvalidArgs()
    {
        Pix pix = MakeRgb(2, 2, (x, y) => (x, y, 0));
        Assert.Throws<ArgumentException>(() => pix.MedianCutQuantGeneral(0, 0, 1, 0, 1, 0));
        Assert.Throws<ArgumentException>(() => pix.MedianCutQuantGeneral(0, 0, 257, 0, 1, 0));
        Assert.Throws<ArgumentException>(() => pix.MedianCutQuantGeneral(0, 3, 8, 0, 1, 0));
        Assert.Throws<ArgumentException>(() => pix.MedianCutQuantGeneral(0, 2, 8, 0, 1, 0));
        Assert.Throws<ArgumentException>(() => pix.MedianCutQuantGeneral(0, 0, 8, 7, 1, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 8).MedianCutQuantGeneral(0, 0, 8, 0, 1, 0));
    }

    [Fact]
    public void MedianCutQuant_GrayImageIsQuantizedInGray()
    {
        Pix pix = MakeRgb(16, 16, (x, y) => (x * 16, x * 16, x * 16));
        Pix pixd = pix.MedianCutQuant(0);
        Assert.Equal(8, pixd.Depth);
    }

    [Fact]
    public void MedianCutQuantMixed_SeparatesColorAndGray()
    {
        Pix pix = MakeRgb(16, 4, (x, y) => x < 8 ? (200, 20, 20) : (128, 128, 128));
        Pix pixd = pix.MedianCutQuantMixed(10, 8, 0, 0, 0);
        Assert.Equal(8, pixd.Depth);
    }

    [Fact]
    public void MedianCutQuantMixed_InvalidArgs()
    {
        Pix pix = Pix.Create(4, 4, 32);
        Assert.Throws<ArgumentException>(() => pix.MedianCutQuantMixed(10, 1, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => pix.MedianCutQuantMixed(200, 100, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).MedianCutQuantMixed(10, 8, 0, 0, 0));
    }

    [Fact]
    public void FewColorsMedianCutQuantMixed_RequiresRgb()
    {
        Assert.Throws<ArgumentException>(() =>
            Pix.Create(4, 4, 8).FewColorsMedianCutQuantMixed(10, 10, 0, 0, 0, 0));
    }
}
