using System;
using Leptonica;
using Leptonica.Managed.Tests.Rop;

namespace Leptonica.Managed.Tests.Rotate;

/// <summary>
/// Compares the orthogonal rotations and flips against a naive per-pixel reference
/// for all depths and odd sizes.
/// </summary>
public class RotateOrthTests
{
    private static readonly int[] Widths = { 1, 2, 3, 5, 7, 8, 9, 15, 16, 17, 31, 32, 33, 47, 64, 65, 70 };
    private static readonly int[] Heights = { 1, 2, 3, 5, 31, 32, 33, 64, 65, 70 };

    private static Pix RandomPix(Random rng, int w, int h, int d)
    {
        Pix pix = Pix.Create(w, h, d);
        uint mask = d == 32 ? 0xffffffffu : (1u << d) - 1;
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                uint v = (uint)rng.NextInt64(0, 1L << 32) & mask;
                if (rng.Next(4) == 0)
                    v = 0;   // exercise the "zero word" shortcuts
                NaiveRop.SetPixel(pix, x, y, v);
            }
        }
        return pix;
    }

    /// <summary>Maps destination (x, y) to the source pixel.</summary>
    private delegate (int, int) SrcMap(int x, int y);

    private static Pix Naive(Pix pixs, int wd, int hd, SrcMap map)
    {
        Pix pixd = Pix.Create(wd, hd, pixs.Depth);
        for (int y = 0; y < hd; y++) {
            for (int x = 0; x < wd; x++) {
                (int sx, int sy) = map(x, y);
                NaiveRop.SetPixel(pixd, x, y, NaiveRop.GetPixel(pixs, sx, sy));
            }
        }
        return pixd;
    }

    private static Pix NaiveRot90(Pix pixs, int direction)
    {
        int w = pixs.Width, h = pixs.Height;
        return direction == 1
            ? Naive(pixs, h, w, (x, y) => (y, h - 1 - x))
            : Naive(pixs, h, w, (x, y) => (w - 1 - y, x));
    }

    private static Pix NaiveLR(Pix pixs) => Naive(pixs, pixs.Width, pixs.Height, (x, y) => (pixs.Width - 1 - x, y));
    private static Pix NaiveTB(Pix pixs) => Naive(pixs, pixs.Width, pixs.Height, (x, y) => (x, pixs.Height - 1 - y));
    private static Pix NaiveRot180(Pix pixs) =>
        Naive(pixs, pixs.Width, pixs.Height, (x, y) => (pixs.Width - 1 - x, pixs.Height - 1 - y));

    private static void AssertSame(Pix expected, Pix actual, string what)
    {
        Assert.True(expected.Width == actual.Width && expected.Height == actual.Height &&
                    expected.Depth == actual.Depth, $"{what}: size mismatch");
        Assert.True(expected.Wpl == actual.Wpl, $"{what}: wpl mismatch");
        // Pixel comparison.  The padding bits at the end of each line are not compared:
        // for d < 8 pixFlipLR() leaves the shifted original bits in the bytes it does not
        // rewrite (same as the C code).
        for (int y = 0; y < expected.Height; y++) {
            for (int x = 0; x < expected.Width; x++) {
                if (NaiveRop.GetPixel(expected, x, y) != NaiveRop.GetPixel(actual, x, y))
                    Assert.Fail($"{what}: pixel mismatch at ({x}, {y})");
            }
        }
    }

    [Fact]
    public void FlipLR8MatchesScalarIncludingPaddingAndInPlace()
    {
        var rng = new Random(73004);
        foreach (int w in new[] { 1, 2, 3, 4, 5, 31, 32, 33, 63, 64, 65, 127, 257 }) {
            Pix source = RandomPix(rng, w, 5, 8);
            // Include arbitrary padding, which the operation must preserve.
            for (int y = 0; y < source.Height; y++)
                if ((w & 3) != 0)
                    source.Data[y * source.Wpl + source.Wpl - 1] |= (uint)rng.Next(1, 256);
            Pix expected = Pix.Copy(null, source);
            for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < w; x++)
                    NaiveRop.SetPixel(expected, x, y, NaiveRop.GetPixel(source, w - 1 - x, y));

            Pix actual = Pix.FlipLR(null, source);
            Assert.Equal(expected.Data, actual.Data);
            Pix inPlace = Pix.Copy(null, source);
            Pix.FlipLR(inPlace, inPlace);
            Assert.Equal(expected.Data, inPlace.Data);
            Pix existing = Pix.Create(2, 3, 8);
            Pix.FlipLR(existing, source);
            Assert.Equal(expected.Data, existing.Data);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void Rotate90MatchesNaive(int d)
    {
        var rng = new Random(70000 + d);
        foreach (int w in Widths) {
            foreach (int h in Heights) {
                Pix pixs = RandomPix(rng, w, h, d);
                AssertSame(NaiveRot90(pixs, 1), pixs.Rotate90(1), $"cw d={d} {w}x{h}");
                AssertSame(NaiveRot90(pixs, -1), pixs.Rotate90(-1), $"ccw d={d} {w}x{h}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void FlipsAndRotate180MatchNaive(int d)
    {
        var rng = new Random(71000 + d);
        foreach (int w in Widths) {
            foreach (int h in new[] { 1, 2, 3, 6, 33 }) {
                Pix pixs = RandomPix(rng, w, h, d);
                Pix orig = Pix.Copy(null, pixs);

                AssertSame(NaiveLR(pixs), Pix.FlipLR(null, pixs), $"LR d={d} {w}x{h}");
                AssertSame(NaiveTB(pixs), Pix.FlipTB(null, pixs), $"TB d={d} {w}x{h}");
                AssertSame(NaiveRot180(pixs), Pix.Rotate180(null, pixs), $"180 d={d} {w}x{h}");
                AssertSame(orig, pixs, $"source unchanged d={d} {w}x{h}");

                // in place
                Pix ip = Pix.Copy(null, pixs);
                Pix.FlipLR(ip, ip);
                AssertSame(NaiveLR(pixs), ip, $"LR in place d={d} {w}x{h}");
                ip = Pix.Copy(null, pixs);
                Pix.FlipTB(ip, ip);
                AssertSame(NaiveTB(pixs), ip, $"TB in place d={d} {w}x{h}");
                ip = Pix.Copy(null, pixs);
                Pix.Rotate180(ip, ip);
                AssertSame(NaiveRot180(pixs), ip, $"180 in place d={d} {w}x{h}");

                // existing pixd of a different size
                Pix other = RandomPix(rng, 11, 4, d);
                Pix res = Pix.FlipLR(other, pixs);
                Assert.Same(other, res);
                AssertSame(NaiveLR(pixs), res, $"LR existing d={d} {w}x{h}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void RotateOrthMatchesNaiveAndFourQuadsIsIdentity(int d)
    {
        var rng = new Random(72000 + d);
        foreach (int w in new[] { 1, 5, 17, 33, 65 }) {
            foreach (int h in new[] { 1, 4, 31, 33, 66 }) {
                Pix pixs = RandomPix(rng, w, h, d);
                AssertSame(pixs, pixs.RotateOrth(0), $"quads 0 d={d} {w}x{h}");
                AssertSame(NaiveRot90(pixs, 1), pixs.RotateOrth(1), $"quads 1 d={d} {w}x{h}");
                AssertSame(NaiveRot180(pixs), pixs.RotateOrth(2), $"quads 2 d={d} {w}x{h}");
                AssertSame(NaiveRot90(pixs, -1), pixs.RotateOrth(3), $"quads 3 d={d} {w}x{h}");

                Pix p = pixs;
                for (int i = 0; i < 4; i++)
                    p = p.Rotate90(1);
                AssertSame(pixs, p, $"4 x cw d={d} {w}x{h}");

                p = pixs;
                for (int i = 0; i < 4; i++)
                    p = p.Rotate90(-1);
                AssertSame(pixs, p, $"4 x ccw d={d} {w}x{h}");

                AssertSame(pixs, pixs.Rotate90(1).Rotate90(-1), $"cw then ccw d={d} {w}x{h}");
                AssertSame(pixs, Pix.FlipLR(null, Pix.FlipLR(null, pixs)), $"LR twice d={d} {w}x{h}");
                AssertSame(pixs, Pix.FlipTB(null, Pix.FlipTB(null, pixs)), $"TB twice d={d} {w}x{h}");
            }
        }
    }

    [Fact]
    public void Rotate90BinaryLargeOddImageMatchesNaive()
    {
        var rng = new Random(73001);
        Pix pixs = RandomPix(rng, 257, 139, 1);
        AssertSame(NaiveRot90(pixs, 1), pixs.Rotate90(1), "cw 1 bpp 257x139");
        AssertSame(NaiveRot90(pixs, -1), pixs.Rotate90(-1), "ccw 1 bpp 257x139");
    }

    [Fact]
    public void Rotate90CopiesAttributes()
    {
        Pix pixs = Pix.Create(5, 3, 8);
        pixs.XRes = 300;
        pixs.YRes = 200;
        pixs.InputFormat = L.IFF_PNG;
        PixColormap cmap = PixColormap.CreateLinear(8, 256);
        pixs.SetColormap(cmap);

        Pix pixd = pixs.Rotate90(1);
        Assert.Equal(3, pixd.Width);
        Assert.Equal(5, pixd.Height);
        Assert.Equal(300, pixd.XRes);
        Assert.Equal(200, pixd.YRes);
        Assert.Equal(L.IFF_PNG, pixd.InputFormat);
        Assert.NotNull(pixd.Colormap);
        Assert.Equal(256, pixd.Colormap!.Count);
    }

    [Fact]
    public void InvalidArguments()
    {
        Pix pixs = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => pixs.RotateOrth(4));
        Assert.Throws<ArgumentException>(() => pixs.RotateOrth(-1));
        Assert.Throws<ArgumentException>(() => pixs.Rotate90(0));
    }
}
