using System;
using System.Linq;
using System.Numerics;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.PixCore;

/// <summary>
/// Tests of pix3.c.  Every expectation is computed pixel by pixel with the plain bit
/// arithmetic of NaiveRop, which shares no code with the word-oriented implementation.
/// Functions that need not-yet-ported callees (pixUnpackBinary, pixSetMaskedCmap,
/// pixFlipLR, pixClipRectangle, ...) are covered only on the paths that do not reach them.
/// </summary>
public class Pix3Tests
{
    private static uint Mask(int d) => d == 32 ? 0xffffffffu : (1u << d) - 1;

    /// <summary>Random pixels; the pad bits at the end of each line are random too.</summary>
    private static Pix RandomWithPad(Random rng, int w, int h, int d) => Naive.RandomPix(rng, w, h, d);

    /// <summary>Random pixels with zero pad bits.</summary>
    private static Pix Random(Random rng, int w, int h, int d)
    {
        Pix pix = Pix.Create(w, h, d);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Naive.SetPixel(pix, j, i, (uint)rng.NextInt64(0, 1L << 32) & Mask(d));
        return pix;
    }

    private static Pix RandomMask(Random rng, int w, int h) => RandomWithPad(rng, w, h, 1);

    private static void AssertClose(double expected, double actual)
    {
        Assert.Equal(expected, actual, 1e-4 * Math.Max(1.0, Math.Abs(expected)));
    }

    /// <summary>Compares every pixel of pix with expected(x, y).</summary>
    private static void AssertPixels(Pix pix, Func<int, int, uint> expected, string what = "")
    {
        for (int y = 0; y < pix.Height; y++) {
            for (int x = 0; x < pix.Width; x++) {
                uint exp = expected(x, y);
                uint act = Naive.GetPixel(pix, x, y);
                if (exp != act)
                    Assert.Fail($"{what} d={pix.Depth} {pix.Width}x{pix.Height}: pixel ({x},{y}) expected {exp:x} got {act:x}");
            }
        }
    }

    /// <summary>Snapshot of the pixels (not the pad bits) of a pix.</summary>
    private static uint[,] Snapshot(Pix pix)
    {
        uint[,] s = new uint[pix.Height, pix.Width];
        for (int y = 0; y < pix.Height; y++)
            for (int x = 0; x < pix.Width; x++)
                s[y, x] = Naive.GetPixel(pix, x, y);
        return s;
    }

    private static readonly (int w1, int h1, int w2, int h2)[] SizePairs =
    {
        (1, 1, 1, 1), (7, 5, 7, 5), (31, 4, 31, 4), (32, 3, 32, 3), (33, 6, 33, 6),
        (70, 5, 70, 5), (33, 6, 20, 3), (20, 3, 33, 6), (65, 4, 64, 7),
    };

    /*------------------------------------------------------------*
     *       One and two-image boolean ops, all depths            *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void InvertMatchesPixelwise(int d)
    {
        Random rng = new(300 + d);
        foreach (var (w, h, _, _) in SizePairs) {
            Pix src = RandomWithPad(rng, w, h, d);
            uint[,] orig = Snapshot(src);

            Pix p1 = Pix.Invert(null, src);
            AssertPixels(p1, (x, y) => ~orig[y, x] & Mask(d), "new");
            AssertPixels(src, (x, y) => orig[y, x], "source unchanged");

            Pix p2 = Pix.Invert(Pix.Create(3, 3, d), src);   // pixd of other size
            Assert.Equal(w, p2.Width);
            Assert.Equal(h, p2.Height);
            AssertPixels(p2, (x, y) => ~orig[y, x] & Mask(d), "other pixd");

            Pix same = Pix.Copy(null, src);
            Pix p3 = Pix.Invert(same, same);
            Assert.Same(same, p3);
            AssertPixels(same, (x, y) => ~orig[y, x] & Mask(d), "in place");
        }
    }

    private static void CheckBinaryOp(int d, Func<Pix?, Pix, Pix, Pix> op, Func<uint, uint, uint> f,
                                      string name, int seed)
    {
        Random rng = new(seed + d);
        foreach (var (w1, h1, w2, h2) in SizePairs) {
            Pix a = RandomWithPad(rng, w1, h1, d);
            Pix b = RandomWithPad(rng, w2, h2, d);
            uint[,] sa = Snapshot(a);
            uint[,] sb = Snapshot(b);
            Func<int, int, uint> expected = (x, y) =>
                (x < w2 && y < h2) ? f(sa[y, x], sb[y, x]) & Mask(d) : sa[y, x];

            Pix r1 = op(null, a, b);
            AssertPixels(r1, expected, name + " new");
            AssertPixels(a, (x, y) => sa[y, x], name + " pixs1 unchanged");
            AssertPixels(b, (x, y) => sb[y, x], name + " pixs2 unchanged");

            Pix r2 = op(Pix.Create(4, 2, d), a, b);
            Assert.Equal(w1, r2.Width);
            Assert.Equal(h1, r2.Height);
            AssertPixels(r2, expected, name + " other pixd");

            Pix inplace = Pix.Copy(null, a);
            Pix r3 = op(inplace, inplace, b);
            Assert.Same(inplace, r3);
            AssertPixels(inplace, expected, name + " in place");
            AssertPixels(b, (x, y) => sb[y, x], name + " pixs2 unchanged after in place");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void OrMatchesPixelwise(int d) =>
        CheckBinaryOp(d, Pix.Or, (a, b) => a | b, "Or", 400);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void AndMatchesPixelwise(int d) =>
        CheckBinaryOp(d, Pix.And, (a, b) => a & b, "And", 500);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void XorMatchesPixelwise(int d) =>
        CheckBinaryOp(d, Pix.Xor, (a, b) => a ^ b, "Xor", 600);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void SubtractMatchesPixelwise(int d)
    {
        CheckBinaryOp(d, Pix.Subtract, (a, b) => a & ~b, "Subtract", 700);

        // pixd == pixs2: the result goes into pixs2, which is src1 & ~src2
        Random rng = new(710 + d);
        foreach (var (w, h, _, _) in SizePairs) {
            Pix a = RandomWithPad(rng, w, h, d);
            Pix b = RandomWithPad(rng, w, h, d);
            uint[,] sa = Snapshot(a);
            uint[,] sb = Snapshot(b);
            Pix r = Pix.Subtract(b, a, b);
            Assert.Same(b, r);
            AssertPixels(b, (x, y) => sa[y, x] & ~sb[y, x] & Mask(d), "pixd == pixs2");
            AssertPixels(a, (x, y) => sa[y, x], "pixs1 unchanged");
        }
    }

    [Fact]
    public void BooleanOpsRejectBadArguments()
    {
        Pix a = Pix.Create(10, 10, 8);
        Pix b = Pix.Create(10, 10, 8);
        Pix c = Pix.Create(10, 10, 1);
        Assert.Throws<ArgumentException>(() => Pix.Or(null, a, c));
        Assert.Throws<ArgumentException>(() => Pix.And(null, a, c));
        Assert.Throws<ArgumentException>(() => Pix.Xor(null, a, c));
        Assert.Throws<ArgumentException>(() => Pix.Subtract(null, a, c));
        Assert.Throws<ArgumentException>(() => Pix.Or(b, a, b));
        Assert.Throws<ArgumentException>(() => Pix.And(b, a, b));
        Assert.Throws<ArgumentException>(() => Pix.Xor(b, a, b));
    }

    /*------------------------------------------------------------*
     *                          Masks                             *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(1, 1u)]
    [InlineData(1, 0u)]
    [InlineData(2, 0x45u)]          // masked to 1
    [InlineData(4, 0x35u)]          // masked to 5
    [InlineData(8, 0x1abu)]         // masked to 0xab
    [InlineData(16, 0x12345u)]      // masked to 0x2345
    [InlineData(32, 0u)]
    [InlineData(32, 0xdeadbeefu)]
    public void SetMaskedMatchesPixelwise(int d, uint val)
    {
        Random rng = new(800 + d);
        uint v = val & Mask(d);
        (int dw, int dh, int mw, int mh)[] sizes =
            { (7, 5, 7, 5), (33, 6, 33, 6), (70, 5, 64, 5), (40, 8, 45, 4), (20, 3, 60, 9) };
        foreach (var (dw, dh, mw, mh) in sizes) {
            Pix pixd = RandomWithPad(rng, dw, dh, d);
            Pix pixm = RandomMask(rng, mw, mh);
            uint[,] sd = Snapshot(pixd);
            uint[,] sm = Snapshot(pixm);
            pixd.SetMasked(pixm, val);
            AssertPixels(pixd, (x, y) =>
                (x < mw && y < mh && sm[y, x] != 0) ? v : sd[y, x], "SetMasked");
        }
    }

    [Fact]
    public void SetMaskedWithoutMaskIsNoOp()
    {
        Random rng = new(810);
        Pix pixd = Random(rng, 20, 5, 8);
        uint[,] sd = Snapshot(pixd);
        pixd.SetMasked(null, 0x55);
        AssertPixels(pixd, (x, y) => sd[y, x]);
        Assert.Throws<ArgumentException>(() => pixd.SetMasked(Pix.Create(20, 5, 8), 0x55));
    }

    [Theory]
    [InlineData(1, 1u)]
    [InlineData(1, 0u)]
    [InlineData(2, 1u)]
    [InlineData(4, 0x5u)]
    [InlineData(8, 0xabu)]
    [InlineData(16, 0x2345u)]
    [InlineData(32, 0u)]
    [InlineData(32, 0xdeadbeefu)]
    public void PaintThroughMaskMatchesPixelwise(int d, uint val)
    {
        Random rng = new(900 + d);
        uint v = val & Mask(d);
        (int x, int y)[] origins = { (0, 0), (3, 2), (-2, -1), (5, -3), (30, 4), (-40, 0) };
        foreach (var (ox, oy) in origins) {
            foreach (var (dw, dh, mw, mh) in new[] { (33, 8, 20, 5), (70, 6, 70, 6), (15, 4, 40, 9) }) {
                Pix pixd = RandomWithPad(rng, dw, dh, d);
                Pix pixm = RandomMask(rng, mw, mh);
                uint[,] sd = Snapshot(pixd);
                uint[,] sm = Snapshot(pixm);
                pixd.PaintThroughMask(pixm, ox, oy, val);
                AssertPixels(pixd, (x, y) =>
                {
                    int mx = x - ox, my = y - oy;
                    bool under = mx >= 0 && my >= 0 && mx < mw && my < mh && sm[my, mx] != 0;
                    return under ? v : sd[y, x];
                }, $"PaintThroughMask origin ({ox},{oy})");
            }
        }
    }

    [Fact]
    public void PaintThroughMaskChecksArguments()
    {
        Pix pixd = Pix.Create(10, 10, 8);
        pixd.PaintThroughMask(null, 0, 0, 1);   // no-op
        Assert.Throws<ArgumentException>(() => pixd.PaintThroughMask(Pix.Create(5, 5, 8), 0, 0, 1));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 1).SetMasked(Pix.Create(5, 5, 4), 1));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 1).SetMasked(Pix.Create(5, 5, 2), 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(32)]
    public void CombineMaskedMatchesPixelwise(int d)
    {
        Random rng = new(1000 + d);
        (int dw, int dh, int sw, int sh, int mw, int mh)[] sizes =
        {
            (33, 6, 33, 6, 33, 6), (70, 5, 64, 7, 70, 3), (20, 9, 40, 9, 33, 4),
            (31, 4, 31, 4, 8, 8), (1, 1, 1, 1, 1, 1),
        };
        foreach (var (dw, dh, sw, sh, mw, mh) in sizes) {
            Pix pixd = RandomWithPad(rng, dw, dh, d);
            Pix pixs = RandomWithPad(rng, sw, sh, d);
            Pix pixm = RandomMask(rng, mw, mh);
            uint[,] sd = Snapshot(pixd);
            uint[,] ss = Snapshot(pixs);
            uint[,] sm = Snapshot(pixm);
            int wmin = Math.Min(dw, Math.Min(sw, mw));
            int hmin = Math.Min(dh, Math.Min(sh, mh));
            pixd.CombineMasked(pixs, pixm);
            AssertPixels(pixd, (x, y) =>
                (x < wmin && y < hmin && sm[y, x] != 0) ? ss[y, x] : sd[y, x], "CombineMasked");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(32)]
    public void CombineMaskedGeneralMatchesPixelwise(int d)
    {
        Random rng = new(1100 + d);
        (int x, int y)[] origins = { (0, 0), (3, 2), (-2, -1), (5, -3), (30, 4), (-40, 0) };
        foreach (var (ox, oy) in origins) {
            foreach (var (dw, dh, sw, sh, mw, mh) in new[]
                     { (33, 8, 20, 5, 20, 5), (70, 6, 64, 7, 70, 3), (15, 4, 40, 9, 33, 12) }) {
                Pix pixd = RandomWithPad(rng, dw, dh, d);
                Pix pixs = RandomWithPad(rng, sw, sh, d);
                Pix pixm = RandomMask(rng, mw, mh);
                uint[,] sd = Snapshot(pixd);
                uint[,] ss = Snapshot(pixs);
                uint[,] sm = Snapshot(pixm);
                int wmin = Math.Min(sw, mw);
                int hmin = Math.Min(sh, mh);
                pixd.CombineMaskedGeneral(pixs, pixm, ox, oy);
                AssertPixels(pixd, (x, y) =>
                {
                    int mx = x - ox, my = y - oy;
                    bool under = mx >= 0 && my >= 0 && mx < wmin && my < hmin && sm[my, mx] != 0;
                    return under ? ss[my, mx] : sd[y, x];
                }, $"CombineMaskedGeneral origin ({ox},{oy})");
            }
        }
    }

    [Fact]
    public void CombineMaskedChecksArguments()
    {
        Pix pixd = Pix.Create(10, 10, 8);
        Pix pixs = Pix.Create(10, 10, 8);
        Pix mask = Pix.Create(10, 10, 1);
        pixd.CombineMasked(pixs, null);             // no-op
        pixd.CombineMaskedGeneral(pixs, null, 1, 1);  // no-op
        Assert.Throws<ArgumentException>(() => pixd.CombineMasked(Pix.Create(10, 10, 32), mask));
        Assert.Throws<ArgumentException>(() => pixd.CombineMasked(pixs, Pix.Create(10, 10, 8)));
        Assert.Throws<ArgumentException>(() =>
            Pix.Create(10, 10, 16).CombineMasked(Pix.Create(10, 10, 16), mask));
        Assert.Throws<ArgumentException>(() => pixd.CombineMaskedGeneral(Pix.Create(10, 10, 32), mask, 0, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void CopyWithBoxaMatchesPixelwise(int d)
    {
        Random rng = new(1200 + d);
        Pix pixs = RandomWithPad(rng, 61, 23, d);
        uint[,] ss = Snapshot(pixs);
        Boxa boxa = Boxa.Create(0);
        boxa.AddBox(Box.Create(3, 2, 20, 7), L.L_COPY);
        boxa.AddBox(Box.Create(30, 10, 31, 13), L.L_COPY);
        boxa.AddBox(Box.Create(0, 0, 1, 1), L.L_COPY);
        uint white = d == 1 ? 0u : Mask(d);
        uint black = d == 1 ? 1u : 0u;
        foreach (var (background, bgval) in new[] { (L.L_SET_WHITE, white), (L.L_SET_BLACK, black) }) {
            Pix pixd = pixs.CopyWithBoxa(boxa, background);
            Assert.Equal(pixs.Width, pixd.Width);
            Assert.Equal(pixs.Height, pixd.Height);
            Assert.Equal(pixs.Depth, pixd.Depth);
            AssertPixels(pixd, (x, y) =>
            {
                bool inside = (x >= 3 && x < 23 && y >= 2 && y < 9) ||
                              (x >= 30 && x < 61 && y >= 10 && y < 23) ||
                              (x == 0 && y == 0);
                return inside ? ss[y, x] : bgval;
            }, "CopyWithBoxa");
        }
        Assert.Throws<ArgumentException>(() => pixs.CopyWithBoxa(boxa, 7));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void MakeMaskFromValMatchesPixelwise(int d)
    {
        Random rng = new(1300 + d);
        foreach (var (w, h, _, _) in SizePairs) {
            Pix pixs = Random(rng, w, h, d);
            pixs.SetResolution(200, 300);
            int[] vals = { 0, 1, (int)Mask(d), 2 };
            foreach (int val in vals) {
                Pix pixm = pixs.MakeMaskFromVal(val);
                Assert.Equal(1, pixm.Depth);
                Assert.Equal(w, pixm.Width);
                Assert.Equal(h, pixm.Height);
                Assert.Equal(200, pixm.XRes);
                Assert.Equal(300, pixm.YRes);
                AssertPixels(pixm, (x, y) => Naive.GetPixel(pixs, x, y) == (uint)val ? 1u : 0u, "MakeMaskFromVal");
            }
        }
        Assert.Throws<ArgumentException>(() => Random(rng, 5, 5, 1).MakeMaskFromVal(1));
        Assert.Throws<ArgumentException>(() => Random(rng, 5, 5, 16).MakeMaskFromVal(1));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void MakeMaskFromLutMatchesPixelwise(int d)
    {
        Random rng = new(1400 + d);
        int[] tab = new int[256];
        for (int i = 0; i < 256; i++)
            tab[i] = rng.Next(0, 2);
        foreach (var (w, h, _, _) in SizePairs) {
            Pix pixs = Random(rng, w, h, d);
            Pix pixm = pixs.MakeMaskFromLUT(tab);
            Assert.Equal(1, pixm.Depth);
            AssertPixels(pixm, (x, y) => (uint)tab[Naive.GetPixel(pixs, x, y)], "MakeMaskFromLUT");
        }
        Assert.Throws<ArgumentException>(() => Random(rng, 5, 5, 32).MakeMaskFromLUT(tab));
    }

    /*------------------------------------------------------------*
     *                     Counting, 1 bpp                        *
     *------------------------------------------------------------*/
    private static readonly int[] Widths = { 1, 7, 31, 32, 33, 64, 65, 100 };

    private static int CountOn(Pix pix, int x0, int y0, int x1, int y1)
    {
        int n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                n += (int)Naive.GetPixel(pix, x, y);
        return n;
    }

    [Fact]
    public void PixelSumTablesAreCorrect()
    {
        int[] sum = Pix.MakePixelSumTab8();
        int[] cen = Pix.MakePixelCentroidTab8();
        Assert.Equal(256, sum.Length);
        Assert.Equal(256, cen.Length);
        for (int i = 0; i < 256; i++) {
            Assert.Equal(BitOperations.PopCount((uint)i), sum[i]);
            int expectedCentroid = 0;
            for (int b = 0; b < 8; b++)
                if ((i & (1 << b)) != 0)
                    expectedCentroid += 7 - b;   // MSB is position 0, LSB is position 7
            Assert.Equal(expectedCentroid, cen[i]);
        }
    }

    [Fact]
    public void ZeroDetectsAnyOnPixel()
    {
        foreach (int d in new[] { 1, 2, 4, 8, 16, 32 }) {
            foreach (int w in Widths) {
                Pix pix = Pix.Create(w, 3, d);
                Assert.True(pix.Zero());
                // a pad bit that is set does not count
                int bits = w * d;
                if ((bits & 31) != 0) {
                    pix.Data[1 * pix.Wpl + bits / 32] |= 1u;
                    Assert.True(pix.Zero());
                }
                // the last pixel of the last line does
                Naive.SetPixel(pix, w - 1, 2, 1);
                Assert.False(pix.Zero());
                Naive.SetPixel(pix, w - 1, 2, 0);
                Assert.True(pix.Zero());
                Naive.SetPixel(pix, 0, 0, 1);
                Assert.False(pix.Zero());
            }
        }
    }

    [Fact]
    public void CountPixelsMatchesPixelwise()
    {
        Random rng = new(1500);
        int[] tab = Pix.MakePixelSumTab8();
        foreach (int w in Widths) {
            foreach (int h in new[] { 1, 2, 9 }) {
                Pix pix = RandomWithPad(rng, w, h, 1);   // random pad bits must not be counted
                int expected = CountOn(pix, 0, 0, w, h);
                Assert.Equal(expected, pix.CountPixels(null));
                Assert.Equal(expected, pix.CountPixels(tab));
                Assert.Equal((float)expected / (float)(w * h), pix.ForegroundFraction());
            }
        }
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).CountPixels(null));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).ForegroundFraction());
    }

    [Fact]
    public void CountPixelsInRectMatchesPixelwise()
    {
        Random rng = new(1600);
        Pix pix = RandomWithPad(rng, 83, 29, 1);
        Assert.Equal(CountOn(pix, 0, 0, 83, 29), pix.CountPixelsInRect(null, null));
        (int x, int y, int w, int h)[] boxes =
            { (0, 0, 83, 29), (5, 3, 40, 10), (33, 7, 1, 1), (70, 20, 50, 50), (31, 0, 33, 29) };
        foreach (var (x, y, w, h) in boxes) {
            int x1 = Math.Min(x + w, 83), y1 = Math.Min(y + h, 29);
            Assert.Equal(CountOn(pix, x, y, x1, y1), pix.CountPixelsInRect(Box.Create(x, y, w, h), null));
        }
        Assert.Throws<ArgumentException>(() => pix.CountPixelsInRect(Box.Create(200, 200, 5, 5), null));
    }

    [Fact]
    public void CountByRowAndColumnMatchPixelwise()
    {
        Random rng = new(1700);
        foreach (int w in Widths) {
            Pix pix = RandomWithPad(rng, w, 11, 1);
            int h = 11;

            Numa byRow = pix.CountPixelsByRow(null);
            Assert.Equal(h, byRow.Count);
            for (int i = 0; i < h; i++) {
                Assert.Equal(CountOn(pix, 0, i, w, i + 1), byRow.GetFValue(i));
                Assert.Equal(CountOn(pix, 0, i, w, i + 1), pix.CountPixelsInRow(i, null));
            }
            Numa byRow2 = pix.CountByRow(null);
            Assert.Equal(h, byRow2.Count);
            for (int i = 0; i < h; i++)
                Assert.Equal(byRow.GetFValue(i), byRow2.GetFValue(i));

            Numa byCol = pix.CountPixelsByColumn();
            Assert.Equal(w, byCol.Count);
            for (int j = 0; j < w; j++)
                Assert.Equal(CountOn(pix, j, 0, j + 1, h), byCol.GetFValue(j));
            Numa byCol2 = pix.CountByColumn(null);
            Assert.Equal(w, byCol2.Count);
            for (int j = 0; j < w; j++)
                Assert.Equal(byCol.GetFValue(j), byCol2.GetFValue(j));

            // with a clipping box (partly outside the image)
            Box box = Box.Create(w / 3, 2, w + 5, 6);
            int xs = w / 3, xe = w, ys = 2, ye = 8;
            Numa rb = pix.CountByRow(box);
            Assert.Equal(ye - ys, rb.Count);
            rb.GetParameters(out float startx, out float delx);
            Assert.Equal((float)ys, startx);
            Assert.Equal(1f, delx);
            for (int i = ys; i < ye; i++)
                Assert.Equal(CountOn(pix, xs, i, xe, i + 1), rb.GetFValue(i - ys));
            Numa cb = pix.CountByColumn(box);
            Assert.Equal(xe - xs, cb.Count);
            cb.GetParameters(out startx, out delx);
            Assert.Equal((float)xs, startx);
            Assert.Equal(1f, delx);
            for (int j = xs; j < xe; j++)
                Assert.Equal(CountOn(pix, j, ys, j + 1, ye), cb.GetFValue(j - xs));
        }
        Pix p = Pix.Create(10, 10, 1);
        Assert.Throws<ArgumentException>(() => p.CountPixelsInRow(-1, null));
        Assert.Throws<ArgumentException>(() => p.CountPixelsInRow(10, null));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 8).CountByRow(null));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 8).CountByColumn(null));
    }

    [Fact]
    public void GetMomentByColumnMatchesPixelwise()
    {
        Random rng = new(1800);
        foreach (int w in Widths) {
            Pix pix = RandomWithPad(rng, w, 13, 1);
            Numa m1 = pix.GetMomentByColumn(1);
            Numa m2 = pix.GetMomentByColumn(2);
            Assert.Equal(w, m1.Count);
            Assert.Equal(w, m2.Count);
            for (int j = 0; j < w; j++) {
                double e1 = 0, e2 = 0;
                for (int i = 0; i < 13; i++) {
                    if (Naive.GetPixel(pix, j, i) != 0) {
                        e1 += i;
                        e2 += i * i;
                    }
                }
                Assert.Equal((float)e1, m1.GetFValue(j));
                Assert.Equal((float)e2, m2.GetFValue(j));
            }
        }
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 1).GetMomentByColumn(3));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).GetMomentByColumn(1));
    }

    [Fact]
    public void ThresholdPixelSumComparesCount()
    {
        Random rng = new(1900);
        int[] tab = Pix.MakePixelSumTab8();
        foreach (int w in Widths) {
            Pix pix = RandomWithPad(rng, w, 7, 1);
            int count = CountOn(pix, 0, 0, w, 7);
            Assert.False(pix.ThresholdPixelSum(count, null));
            Assert.False(pix.ThresholdPixelSum(count + 10, tab));
            if (count > 0) {
                Assert.True(pix.ThresholdPixelSum(count - 1, null));
                Assert.True(pix.ThresholdPixelSum(0, tab));
            }
        }
    }

    /*------------------------------------------------------------*
     *               Average, variance, abs diff                  *
     *------------------------------------------------------------*/
    private static (int xs, int ys, int xe, int ye) ClipBox(Box? box, int w, int h)
    {
        if (box == null)
            return (0, 0, w, h);
        return (Math.Max(box.X, 0), Math.Max(box.Y, 0),
                Math.Min(box.X + box.Width, w), Math.Min(box.Y + box.Height, h));
    }

    private static readonly Box?[] TestBoxes =
        { null, Box.Create(3, 2, 20, 9), Box.Create(10, 5, 100, 100), Box.Create(0, 0, 1, 1) };

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void AverageByRowAndColumnMatchPixelwise(int d)
    {
        Random rng = new(2000 + d);
        uint maxval = d == 8 ? 255u : 65535u;
        Pix pix = Random(rng, 47, 19, d);
        foreach (Box? box in TestBoxes) {
            var (xs, ys, xe, ye) = ClipBox(box, 47, 19);
            foreach (int type in new[] { L.L_WHITE_IS_MAX, L.L_BLACK_IS_MAX }) {
                Numa rows = pix.AverageByRow(box, type);
                Assert.Equal(ye - ys, rows.Count);
                rows.GetParameters(out float sx, out float dx);
                Assert.Equal((float)ys, sx);
                Assert.Equal(1f, dx);
                for (int i = ys; i < ye; i++) {
                    double sum = 0;
                    for (int j = xs; j < xe; j++) {
                        uint v = Naive.GetPixel(pix, j, i);
                        sum += type == L.L_BLACK_IS_MAX ? maxval - v : v;
                    }
                    AssertClose(sum / (xe - xs), rows.GetFValue(i - ys));
                }

                Numa cols = pix.AverageByColumn(box, type);
                Assert.Equal(xe - xs, cols.Count);
                cols.GetParameters(out sx, out dx);
                Assert.Equal((float)xs, sx);
                Assert.Equal(1f, dx);
                for (int j = xs; j < xe; j++) {
                    double sum = 0;
                    for (int i = ys; i < ye; i++) {
                        uint v = Naive.GetPixel(pix, j, i);
                        sum += type == L.L_BLACK_IS_MAX ? maxval - v : v;
                    }
                    AssertClose(sum / (ye - ys), cols.GetFValue(j - xs));
                }
            }
        }
        Assert.Throws<ArgumentException>(() => pix.AverageByRow(null, 0));
        Assert.Throws<ArgumentException>(() => pix.AverageByColumn(null, 3));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 4).AverageByRow(null, L.L_WHITE_IS_MAX));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 32).AverageByColumn(null, L.L_WHITE_IS_MAX));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void AverageInRectMatchesPixelwise(int d)
    {
        Random rng = new(2100 + d);
        Pix pix = Random(rng, 53, 21, d);
        Pix mask = RandomMask(rng, 50, 25);   // blocking mask, smaller in width, taller
        uint max = Mask(d);
        foreach (Box? box in TestBoxes) {
            foreach (Pix? pixm in new Pix?[] { null, mask }) {
                foreach (int subsamp in new[] { 1, 2, 3 }) {
                    foreach (var (minval, maxval) in new[] { (0, (int)max), (1, (int)max - 1), (0, 0) }) {
                        int w = 53, h = 21;
                        if (pixm != null) {
                            w = Math.Min(w, pixm.Width);
                            h = Math.Min(h, pixm.Height);
                        }
                        var (xs, ys, xe, ye) = ClipBox(box, w, h);
                        double sum = 0;
                        int count = 0;
                        for (int i = ys; i < ye; i += subsamp) {
                            for (int j = xs; j < xe; j += subsamp) {
                                if (pixm != null && Naive.GetPixel(pixm, j, i) == 1) continue;
                                int v = (int)Naive.GetPixel(pix, j, i);
                                if (v >= minval && v <= maxval) {
                                    sum += v;
                                    count++;
                                }
                            }
                        }
                        bool ok = pix.AverageInRect(pixm, box, minval, maxval, subsamp, out float ave);
                        if (count == 0) {
                            Assert.False(ok);
                            Assert.Equal(0f, ave);
                        } else {
                            Assert.True(ok);
                            AssertClose(sum / count, ave);
                        }
                    }
                }
            }
        }
        Assert.Throws<ArgumentException>(() => pix.AverageInRect(null, null, 0, 255, 0, out _));
        Assert.Throws<ArgumentException>(() => pix.AverageInRect(Pix.Create(5, 5, 8), null, 0, 255, 1, out _));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 16).AverageInRect(null, null, 0, 255, 1, out _));
    }

    [Fact]
    public void AverageInRectRGBMatchesPixelwise()
    {
        Random rng = new(2200);
        Pix pix = Random(rng, 41, 17, 32);
        Pix mask = RandomMask(rng, 41, 17);
        foreach (Box? box in TestBoxes) {
            foreach (Pix? pixm in new Pix?[] { null, mask }) {
                foreach (int subsamp in new[] { 1, 2, 5 }) {
                    var (xs, ys, xe, ye) = ClipBox(box, 41, 17);
                    double rsum = 0, gsum = 0, bsum = 0;
                    int count = 0;
                    for (int i = ys; i < ye; i += subsamp) {
                        for (int j = xs; j < xe; j += subsamp) {
                            if (pixm != null && Naive.GetPixel(pixm, j, i) == 1) continue;
                            uint p = Naive.GetPixel(pix, j, i);
                            rsum += p >> 24;
                            gsum += (p >> 16) & 0xff;
                            bsum += (p >> 8) & 0xff;
                            count++;
                        }
                    }
                    bool ok = pix.AverageInRectRGB(pixm, box, subsamp, out uint ave);
                    if (count == 0) {
                        Assert.False(ok);
                        Assert.Equal(0u, ave);
                    } else {
                        Assert.True(ok);
                        uint expected = ((uint)(rsum / count) << 24) | ((uint)(gsum / count) << 16) |
                                        ((uint)(bsum / count) << 8);
                        Assert.Equal(expected, ave);
                    }
                }
            }
        }
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).AverageInRectRGB(null, null, 1, out _));
        Assert.Throws<ArgumentException>(() => pix.AverageInRectRGB(null, null, 0, out _));
    }

    private static double RootVar(Func<int, uint> values, int n)
    {
        double mean = 0;
        for (int k = 0; k < n; k++) mean += values(k);
        mean /= n;
        double var = 0;
        for (int k = 0; k < n; k++) var += (values(k) - mean) * (values(k) - mean);
        return Math.Sqrt(var / n);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void VarianceByRowAndColumnMatchPixelwise(int d)
    {
        Random rng = new(2300 + d);
        Pix pix = Random(rng, 45, 23, d);
        foreach (Box? box in TestBoxes) {
            var (xs, ys, xe, ye) = ClipBox(box, 45, 23);
            Numa rows = pix.VarianceByRow(box);
            Assert.Equal(ye - ys, rows.Count);
            rows.GetParameters(out float sx, out float dx);
            Assert.Equal((float)ys, sx);
            Assert.Equal(1f, dx);
            for (int i = ys; i < ye; i++) {
                int row = i;
                AssertClose(RootVar(k => Naive.GetPixel(pix, xs + k, row), xe - xs), rows.GetFValue(i - ys));
            }
            Numa cols = pix.VarianceByColumn(box);
            Assert.Equal(xe - xs, cols.Count);
            cols.GetParameters(out sx, out dx);
            Assert.Equal((float)xs, sx);
            for (int j = xs; j < xe; j++) {
                int col = j;
                AssertClose(RootVar(k => Naive.GetPixel(pix, col, ys + k), ye - ys), cols.GetFValue(j - xs));
            }
        }
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 4).VarianceByRow(null));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 32).VarianceByColumn(null));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void VarianceInRectMatchesPixelwise(int d)
    {
        Random rng = new(2400 + d);
        Pix pix = Random(rng, 38, 15, d);
        foreach (Box? box in TestBoxes) {
            var (xs, ys, xe, ye) = ClipBox(box, 38, 15);
            int bw = xe - xs;
            AssertClose(RootVar(k => Naive.GetPixel(pix, xs + k % bw, ys + k / bw), bw * (ye - ys)),
                        pix.VarianceInRect(box));
        }
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 16).VarianceInRect(null));
    }

    [Fact]
    public void AbsDiffMatchesPixelwise()
    {
        Random rng = new(2500);
        Pix pix = Random(rng, 49, 27, 8);
        foreach (Box? box in TestBoxes) {
            var (xs, ys, xe, ye) = ClipBox(box, 49, 27);
            int bw = xe - xs, bh = ye - ys;

            if (bw >= 2) {
                Numa rows = pix.AbsDiffByRow(box);
                Assert.Equal(bh, rows.Count);
                rows.GetParameters(out float sx, out _);
                Assert.Equal((float)ys, sx);
                for (int i = ys; i < ye; i++) {
                    double sum = 0;
                    for (int j = xs + 1; j < xe; j++)
                        sum += Math.Abs((int)Naive.GetPixel(pix, j, i) - (int)Naive.GetPixel(pix, j - 1, i));
                    AssertClose(sum / (bw - 1), rows.GetFValue(i - ys));
                }
            } else {
                Assert.Throws<ArgumentException>(() => pix.AbsDiffByRow(box));
            }

            if (bh >= 2) {
                Numa cols = pix.AbsDiffByColumn(box);
                Assert.Equal(bw, cols.Count);
                cols.GetParameters(out float sx, out _);
                Assert.Equal((float)xs, sx);
                for (int j = xs; j < xe; j++) {
                    double sum = 0;
                    for (int i = ys + 1; i < ye; i++)
                        sum += Math.Abs((int)Naive.GetPixel(pix, j, i) - (int)Naive.GetPixel(pix, j, i - 1));
                    AssertClose(sum / (bh - 1), cols.GetFValue(j - xs));
                }
            } else {
                Assert.Throws<ArgumentException>(() => pix.AbsDiffByColumn(box));
            }

            if (bw >= 2 && bh >= 2) {
                double hsum = 0, vsum = 0;
                for (int i = ys; i < ye; i++)
                    for (int j = xs + 1; j < xe; j++)
                        hsum += Math.Abs((int)Naive.GetPixel(pix, j, i) - (int)Naive.GetPixel(pix, j - 1, i));
                for (int j = xs; j < xe; j++)
                    for (int i = ys + 1; i < ye; i++)
                        vsum += Math.Abs((int)Naive.GetPixel(pix, j, i) - (int)Naive.GetPixel(pix, j, i - 1));
                AssertClose(hsum / (bh * (bw - 1)), pix.AbsDiffInRect(box, L.L_HORIZONTAL_LINE));
                AssertClose(vsum / (bw * (bh - 1)), pix.AbsDiffInRect(box, L.L_VERTICAL_LINE));
            }
        }
        Assert.Throws<ArgumentException>(() => pix.AbsDiffInRect(null, L.L_OBLIQUE_LINE));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 1).AbsDiffByRow(null));
    }

    [Fact]
    public void AbsDiffOnLineMatchesPixelwise()
    {
        Random rng = new(2600);
        Pix pix = Random(rng, 30, 20, 8);

        double hsum = 0;
        for (int j = 4; j <= 25; j++)
            hsum += Math.Abs((int)Naive.GetPixel(pix, j, 7) - (int)Naive.GetPixel(pix, j - 1, 7));
        AssertClose(hsum / 22, pix.AbsDiffOnLine(3, 7, 25, 7));

        double vsum = 0;
        for (int i = 6; i <= 19; i++)
            vsum += Math.Abs((int)Naive.GetPixel(pix, 12, i) - (int)Naive.GetPixel(pix, 12, i - 1));
        AssertClose(vsum / 14, pix.AbsDiffOnLine(12, 5, 12, 19));

        // the line is clipped to the image
        double csum = 0;
        for (int j = 1; j <= 29; j++)
            csum += Math.Abs((int)Naive.GetPixel(pix, j, 0) - (int)Naive.GetPixel(pix, j - 1, 0));
        AssertClose(csum / 29, pix.AbsDiffOnLine(-10, 0, 100, 0));

        Assert.Throws<ArgumentException>(() => pix.AbsDiffOnLine(0, 0, 5, 5));
        Assert.Throws<ArgumentException>(() => pix.AbsDiffOnLine(5, 3, 5, 3));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 1).AbsDiffOnLine(0, 0, 3, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void CountArbInRectMatchesPixelwise(int d)
    {
        Random rng = new(2700 + d);
        Pix pix = Random(rng, 61, 23, d);
        foreach (Box? box in TestBoxes.Append(Box.Create(50, 15, 30, 30))) {
            foreach (int factor in new[] { 1, 2, 3 }) {
                foreach (int val in new[] { 0, 1, (int)Mask(d) }) {
                    int bx = box?.X ?? 0, by = box?.Y ?? 0;
                    int bw = box?.Width ?? 61, bh = box?.Height ?? 23;
                    int count = 0;
                    for (int i = 0; i < (box == null ? 23 : bh); i += factor) {
                        for (int j = 0; j < (box == null ? 61 : bw); j += factor) {
                            int x = bx + j, y = by + i;
                            if (x < 0 || y < 0 || x >= 61 || y >= 23) continue;
                            if (Naive.GetPixel(pix, x, y) == (uint)val) count++;
                        }
                    }
                    if (factor > 1) count *= factor * factor;
                    Assert.Equal(count, pix.CountArbInRect(box, val, factor));
                }
            }
        }
        Assert.Throws<ArgumentException>(() => pix.CountArbInRect(null, -1, 1));
        Assert.Throws<ArgumentException>(() => pix.CountArbInRect(null, (int)Mask(d) + 1, 1));
        Assert.Throws<ArgumentException>(() => pix.CountArbInRect(null, 0, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 16).CountArbInRect(null, 0, 1));
    }

    [Fact]
    public void MakeArbMaskAndUnderTransparencyCheckArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).MakeArbMaskFromRGB(0.3f, 0.5f, 0.2f, 100f));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).SetUnderTransparency(0xffffff00, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).MakeAlphaFromMask(5));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 1).MakeAlphaFromMask(-1));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 4).MirroredTiling(10, 10));
    }

    [Fact]
    public void SetUnderTransparencyWithoutAlphaReturnsCopy()
    {
        Random rng = new(2800);
        Pix pix = Random(rng, 12, 5, 32);   // spp is 3
        Pix copy = pix.SetUnderTransparency(0xffffff00, 0);
        Assert.NotSame(pix, copy);
        Assert.Equal(pix.Data, copy.Data);
    }
}
