using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Morph;

/// <summary>
/// Tests for graymorph.c against a naive min/max filter over the brick window.  The pixels
/// outside the image are 255 for erosion and 0 for dilation; in opening and closing the
/// second step sees the opposite value outside, because the C code resets the border.
/// </summary>
public class GrayMorphTests
{
    private static Pix RandomGray(Random rng, int w, int h, int lo = 0, int hi = 256)
    {
        Pix pix = Pix.Create(w, h, 8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pix.SetPixel(x, y, (uint)rng.Next(lo, hi));
        return pix;
    }

    private static uint Get(Pix p, int x, int y)
    {
        p.GetPixel(x, y, out uint v);
        return v;
    }

    /// <summary>Naive min (erode) or max (dilate) over a hsize x vsize window.</summary>
    private static Pix Naive(Pix src, int hsize, int vsize, bool erode)
    {
        int w = src.Width, h = src.Height;
        int hr = hsize / 2, vr = vsize / 2;
        uint outside = erode ? 255u : 0u;
        Pix dst = Pix.Create(w, h, 8);
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                uint best = outside;
                for (int dy = -vr; dy <= vr; dy++) {
                    for (int dx = -hr; dx <= hr; dx++) {
                        int xx = x + dx, yy = y + dy;
                        uint v = (xx < 0 || yy < 0 || xx >= w || yy >= h) ? outside : Get(src, xx, yy);
                        best = erode ? Math.Min(best, v) : Math.Max(best, v);
                    }
                }
                dst.SetPixel(x, y, best);
            }
        }
        return dst;
    }

    private static void AssertSame(Pix expected, Pix actual, string what)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(8, actual.Depth);
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                if (Get(expected, x, y) != Get(actual, x, y))
                    Assert.Fail($"{what}: mismatch at ({x},{y}): expected {Get(expected, x, y)}, got {Get(actual, x, y)}");
    }

    public static TheoryData<int, int, int, int> Cases()
    {
        var data = new TheoryData<int, int, int, int>();
        int[] dims = { 1, 2, 5, 8, 13, 31, 32, 33, 47 };
        int[] sizes = { 1, 2, 3, 4, 5, 7, 8, 11 };
        var rng = new Random(1234);
        for (int n = 0; n < 40; n++)
            data.Add(dims[rng.Next(dims.Length)], dims[rng.Next(dims.Length)],
                     sizes[rng.Next(sizes.Length)], sizes[rng.Next(sizes.Length)]);
        // brick sels larger than the image, sizes 1 in one direction
        data.Add(5, 5, 11, 1);
        data.Add(5, 5, 1, 11);
        data.Add(3, 3, 9, 9);
        data.Add(40, 17, 1, 1);
        return data;
    }

    private static int Odd(int s) => (s & 1) == 0 ? s + 1 : s;

    [Theory]
    [MemberData(nameof(Cases))]
    public void ErodeAndDilateGrayMatchNaive(int w, int h, int hsize, int vsize)
    {
        Pix pix = RandomGray(new Random(w * 1000 + h * 10 + hsize + vsize), w, h);
        int hs = Odd(hsize), vs = Odd(vsize);
        AssertSame(Naive(pix, hs, vs, true), pix.ErodeGray(hsize, vsize), "erode");
        AssertSame(Naive(pix, hs, vs, false), pix.DilateGray(hsize, vsize), "dilate");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void OpenAndCloseGrayMatchNaive(int w, int h, int hsize, int vsize)
    {
        Pix pix = RandomGray(new Random(w * 77 + h * 5 + hsize * 3 + vsize), w, h);
        int hs = Odd(hsize), vs = Odd(vsize);
        Pix open = Naive(Naive(pix, hs, vs, true), hs, vs, false);
        Pix close = Naive(Naive(pix, hs, vs, false), hs, vs, true);
        AssertSame(open, pix.OpenGray(hsize, vsize), "open");
        AssertSame(close, pix.CloseGray(hsize, vsize), "close");
    }

    [Fact]
    public void ExtremeValuesAreKeptAtBorders()
    {
        // all-255 and all-0 images must be fixed points of every operation
        foreach (uint val in new uint[] { 0, 255 }) {
            Pix pix = Pix.Create(19, 11, 8);
            for (int y = 0; y < 11; y++)
                for (int x = 0; x < 19; x++)
                    pix.SetPixel(x, y, val);
            AssertSame(pix, pix.ErodeGray(5, 3), "erode const");
            AssertSame(pix, pix.DilateGray(5, 3), "dilate const");
            AssertSame(pix, pix.OpenGray(5, 3), "open const");
            AssertSame(pix, pix.CloseGray(5, 3), "close const");
        }
    }

    [Fact]
    public void SizeOneReturnsCopy()
    {
        Pix pix = RandomGray(new Random(5), 20, 9);
        Pix copy = pix.ErodeGray(1, 1);
        Assert.NotSame(pix, copy);
        AssertSame(pix, copy, "copy");
    }

    [Fact]
    public void InvalidArgumentsThrow()
    {
        Pix pix8 = RandomGray(new Random(1), 10, 10);
        Pix pix1 = Pix.Create(10, 10, 1);
        Assert.Throws<ArgumentException>(() => pix1.ErodeGray(3, 3));
        Assert.Throws<ArgumentException>(() => pix8.DilateGray(0, 3));
        Assert.Throws<ArgumentException>(() => pix8.OpenGray(3, -1));
        Assert.Throws<ArgumentException>(() => pix1.CloseGray(3, 3));
        Assert.Throws<ArgumentException>(() => pix8.ErodeGray3(2, 3));
        Assert.Throws<ArgumentException>(() => pix1.DilateGray3(3, 3));
    }

    public static TheoryData<int, int> Dims3()
    {
        var data = new TheoryData<int, int>();
        int[] dims = { 1, 2, 3, 8, 9, 10, 17, 18, 25, 33, 64, 67 };
        foreach (int w in dims)
            foreach (int h in new[] { 1, 4, 9, 10, 23, 40 })
                if ((w + h) % 3 != 0)
                    data.Add(w, h);
        return data;
    }

    [Theory]
    [MemberData(nameof(Dims3))]
    public void Gray3MatchNaive(int w, int h)
    {
        Pix pix = RandomGray(new Random(w * 131 + h), w, h);
        foreach (var (hs, vs) in new[] { (3, 1), (1, 3), (3, 3), (1, 1) }) {
            Pix open = Naive(Naive(pix, hs, vs, true), hs, vs, false);
            Pix close = Naive(Naive(pix, hs, vs, false), hs, vs, true);
            AssertSame(Naive(pix, hs, vs, true), pix.ErodeGray3(hs, vs), $"erode3 {hs}x{vs}");
            AssertSame(Naive(pix, hs, vs, false), pix.DilateGray3(hs, vs), $"dilate3 {hs}x{vs}");
            AssertSame(open, pix.OpenGray3(hs, vs), $"open3 {hs}x{vs}");
            AssertSame(close, pix.CloseGray3(hs, vs), $"close3 {hs}x{vs}");
        }
    }

    [Theory]
    [InlineData(37, 29)]
    [InlineData(64, 64)]
    public void Gray3EqualsGeneralVersion(int w, int h)
    {
        Pix pix = RandomGray(new Random(99), w, h);
        foreach (var (hs, vs) in new[] { (3, 1), (1, 3), (3, 3) }) {
            AssertSame(pix.ErodeGray(hs, vs), pix.ErodeGray3(hs, vs), "erode");
            AssertSame(pix.DilateGray(hs, vs), pix.DilateGray3(hs, vs), "dilate");
            AssertSame(pix.OpenGray(hs, vs), pix.OpenGray3(hs, vs), "open");
            AssertSame(pix.CloseGray(hs, vs), pix.CloseGray3(hs, vs), "close");
        }
    }

    [Fact]
    public void Gray3RejectsColormappedPix()
    {
        Pix pix = RandomGray(new Random(3), 12, 12);
        pix.SetColormap(PixColormap.CreateLinear(8, 256));
        Assert.Throws<ArgumentException>(() => pix.ErodeGray3(3, 3));
        Assert.Throws<ArgumentException>(() => pix.CloseGray3(3, 3));
    }
}
