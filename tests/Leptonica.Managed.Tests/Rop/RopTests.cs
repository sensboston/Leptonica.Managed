using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Rop;

/// <summary>
/// Compares the public rasterop functions against the naive pixel reference on
/// random images (fixed seeds).
/// </summary>
public class RopTests
{
    private const int Iterations = 210;   // 3 sweeps over widths 1..70

    private static int Rnd(Random rng, int lo, int hi) => rng.Next(lo, hi + 1);

    /// <summary>Value of a pixel that is brought in as white or black (no colormap).</summary>
    private static uint InColor(int d, int incolor)
    {
        if (incolor == L.L_BRING_IN_WHITE)
            return d == 1 ? 0u : (d == 32 ? 0xffffffffu : (1u << d) - 1);
        return d == 1 ? 1u : 0u;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void RasteropMatchesNaive(int d)
    {
        int pw = 32 / d;
        for (int op = 0; op < 16; op++) {
            int seed = 40000 + 1000 * d + op;
            var rng = new Random(seed);
            for (int iter = 0; iter < Iterations; iter++) {
                int w = 1 + (iter % 70);
                int h = Rnd(rng, 1, 5);
                int sw = (iter % 5 == 0) ? w : Rnd(rng, 1, 70);
                int sh = Rnd(rng, 1, 5);
                int mode = iter % 4;
                int dx, sx;
                switch (mode) {
                case 0:
                    dx = Rnd(rng, -10, w + 10);
                    sx = Rnd(rng, -10, sw + 10);
                    break;
                case 1:
                    dx = pw * Rnd(rng, 0, 3);
                    sx = pw * Rnd(rng, 0, 3);
                    break;
                case 2:
                    dx = Rnd(rng, 0, w + 3);
                    sx = dx + pw * Rnd(rng, -1, 2);
                    break;
                default:
                    dx = 0; sx = 0;
                    break;
                }
                int dy = Rnd(rng, -2, h + 2);
                int sy = Rnd(rng, -2, sh + 2);
                int dw = (mode == 3) ? w : Rnd(rng, 0, w + 10);
                int dh = (mode == 3) ? h : Rnd(rng, 0, h + 3);

                Pix expected = NaiveRop.RandomPix(rng, w, h, d);
                Pix actual = Pix.Copy(null, expected);
                Pix src = NaiveRop.RandomPix(rng, sw, sh, d);
                Pix srcCopy = Pix.Copy(null, src);
                // Ops that involve only the dest accept a null source.
                Pix? srcArg = NaiveRop.UsesSrc(op) || (iter % 2 == 0) ? src : null;

                NaiveRop.Rasterop(expected, dx, dy, dw, dh, op, src, sx, sy);
                actual.Rasterop(dx, dy, dw, dh, op, srcArg, sx, sy);

                string diff = NaiveRop.FirstDifference(expected.Data, actual.Data);
                Assert.True(diff == "",
                    $"seed={seed} iter={iter} d={d} op={op:x} dest {w}x{h} rect ({dx},{dy},{dw},{dh}) " +
                    $"src {sw}x{sh} at ({sx},{sy}): {diff}");
                Assert.Equal(srcCopy.Data, src.Data);
            }
        }
    }

    [Fact]
    public void ShiftedXorMatchesScalarReferenceAtWordAndVectorBoundaries()
    {
        var rng = new Random(481720);
        int[] widths = { 1, 31, 32, 33, 63, 64, 65, 127, 128, 129, 255, 256, 257, 513 };
        int[] offsets = { -3, -1, 0, 1, 3, 7, 31, 33 };
        foreach (int width in widths) {
            foreach (int dx in offsets) {
                foreach (int sx in offsets) {
                    Pix expected = NaiveRop.RandomPix(rng, width, 5, 1);
                    Pix actual = Pix.Copy(null, expected);
                    Pix src = NaiveRop.RandomPix(rng, width, 5, 1);
                    int op = L.PIX_SRC ^ L.PIX_DST;
                    NaiveRop.Rasterop(expected, dx, -1, width + 3, 7, op, src, sx, 0);
                    actual.Rasterop(dx, -1, width + 3, 7, op, src, sx, 0);
                    Assert.Equal(expected.Data, actual.Data);
                }
            }
        }
    }

    [Fact]
    public void ShiftedMorphologicalRasteropsMatchScalarReference()
    {
        var rng = new Random(485621);
        int[] widths = { 1, 31, 32, 33, 63, 64, 65, 127, 129, 255, 257, 513 };
        int[] offsets = { -7, -1, 0, 1, 3, 7, 31, 33 };
        int[] ops = { L.PIX_SRC | L.PIX_DST, L.PIX_SRC & L.PIX_DST };
        foreach (int width in widths) {
            foreach (int dx in offsets) {
                foreach (int sx in offsets) {
                    foreach (int op in ops) {
                        Pix expected = NaiveRop.RandomPix(rng, width, 5, 1);
                        Pix actual = Pix.Copy(null, expected);
                        Pix src = NaiveRop.RandomPix(rng, width, 5, 1);
                        NaiveRop.Rasterop(expected, dx, -1, width + 3, 7, op, src, sx, 0);
                        actual.Rasterop(dx, -1, width + 3, 7, op, src, sx, 0);
                        Assert.Equal(expected.Data, actual.Data);
                    }
                }
            }
        }
    }

    [Fact]
    public void ShiftedSourceCopyMatchesScalarAtVectorAndImageBoundaries()
    {
        var rng = new Random(284813);
        int[] widths = { 1, 31, 32, 33, 63, 64, 65, 127, 128, 129, 255, 256, 257, 513, 2051 };
        int[] offsets = { -3, -1, 0, 1, 3, 7, 31, 33, 100 };
        foreach (int width in widths) {
            foreach (int dx in offsets) {
                foreach (int sx in offsets) {
                    Pix expected = NaiveRop.RandomPix(rng, width, 5, 1);
                    Pix actual = Pix.Copy(null, expected);
                    Pix src = NaiveRop.RandomPix(rng, width, 5, 1);
                    NaiveRop.Rasterop(expected, dx, -1, width + 3, 7, L.PIX_SRC, src, sx, 0);
                    actual.Rasterop(dx, -1, width + 3, 7, L.PIX_SRC, src, sx, 0);
                    Assert.Equal(expected.Data, actual.Data);
                }
            }
            Pix expectedInPlace = NaiveRop.RandomPix(rng, width, 4, 1);
            Pix actualInPlace = Pix.Copy(null, expectedInPlace);
            NaiveRop.Rasterop(expectedInPlace, 1, 2, width, 2, L.PIX_SRC, expectedInPlace, 0, 0);
            actualInPlace.Rasterop(1, 2, width, 2, L.PIX_SRC, actualInPlace, 0, 0);
            Assert.Equal(expectedInPlace.Data, actualInPlace.Data);
        }
    }

    [Fact]
    public void RasteropRejectsBadArguments()
    {
        Pix pixd = Pix.Create(10, 10, 8);
        Pix pix1 = Pix.Create(10, 10, 1);
        Assert.Throws<ArgumentException>(() => pixd.Rasterop(0, 0, 10, 10, L.PIX_SRC, null, 0, 0));
        Assert.Throws<ArgumentException>(() => pixd.Rasterop(0, 0, 10, 10, L.PIX_SRC, pix1, 0, 0));
        // the no-op and dest-only ops do not look at the source
        pixd.Rasterop(0, 0, 10, 10, L.PIX_DST, null, 0, 0);
        pixd.Rasterop(0, 0, 10, 10, L.PIX_SET, null, 0, 0);
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
                Assert.Equal(255u, NaiveRop.GetPixel(pixd, x, y));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(32)]
    public void RasteropInPlaceDisjointRectangles(int d)
    {
        int pw = 32 / d;
        for (int op = 0; op < 16; op++) {
            int seed = 60000 + 1000 * d + op;
            var rng = new Random(seed);
            for (int iter = 0; iter < Iterations; iter++) {
                int w = 8 + (iter % 70);
                int h = Rnd(rng, 2, 6);
                int dw = Rnd(rng, 1, w / 2);
                int dh = Rnd(rng, 1, h);
                int dx, dy, sx, sy;
                if (iter % 2 == 0) {
                    dx = Rnd(rng, 0, w / 2 - dw);
                    sx = (iter % 4 == 0) ? dx + dw + pw * Rnd(rng, 0, 1) : Rnd(rng, dx + dw, w);
                    dy = Rnd(rng, 0, h - dh);
                    sy = Rnd(rng, 0, h - dh);
                } else {
                    dx = Rnd(rng, 0, w - dw);
                    sx = Rnd(rng, 0, w - dw);
                    dh = Rnd(rng, 1, h / 2);
                    dy = Rnd(rng, 0, h / 2 - dh);
                    sy = Rnd(rng, dy + dh, h - dh);
                }

                Pix expected = NaiveRop.RandomPix(rng, w, h, d);
                Pix actual = Pix.Copy(null, expected);

                NaiveRop.Rasterop(expected, dx, dy, dw, dh, op, expected, sx, sy);
                actual.Rasterop(dx, dy, dw, dh, op, actual, sx, sy);

                string diff = NaiveRop.FirstDifference(expected.Data, actual.Data);
                Assert.True(diff == "",
                    $"seed={seed} iter={iter} d={d} op={op:x} image {w}x{h} dest ({dx},{dy},{dw},{dh}) " +
                    $"src at ({sx},{sy}): {diff}");
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
    public void RasteropFullImageMatchesNaive(int d)
    {
        int seed = 70000 + d;
        var rng = new Random(seed);
        int[] ops = { L.PIX_SRC, L.PIX_PAINT, L.PIX_MASK, L.PIX_XOR, L.PIX_SUBTRACT, L.PIX_NOT(L.PIX_SRC) };
        for (int iter = 0; iter < 140; iter++) {
            int w = 1 + (iter % 70);
            int h = Rnd(rng, 1, 5);
            int sw = Rnd(rng, 1, 70);
            int sh = Rnd(rng, 1, 5);
            int op = ops[iter % ops.Length];
            Pix expected = NaiveRop.RandomPix(rng, w, h, d);
            Pix actual = Pix.Copy(null, expected);
            Pix src = NaiveRop.RandomPix(rng, sw, sh, d);

            NaiveRop.Rasterop(expected, 0, 0, w, h, op, src, 0, 0);
            actual.RasteropFullImage(src, op);

            string diff = NaiveRop.FirstDifference(expected.Data, actual.Data);
            Assert.True(diff == "", $"seed={seed} iter={iter} d={d} op={op:x} {w}x{h} from {sw}x{sh}: {diff}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void RasteropVipMatchesNaive(int d)
    {
        int seed = 80000 + d;
        var rng = new Random(seed);
        for (int iter = 0; iter < 700; iter++) {
            int w = 1 + (iter % 70);
            int h = Rnd(rng, 1, 7);
            int x = Rnd(rng, -10, w + 10);
            int bw = Rnd(rng, 1, w + 10);
            int shift = Rnd(rng, -(h + 3), h + 3);
            int incolor = (iter % 2 == 0) ? L.L_BRING_IN_WHITE : L.L_BRING_IN_BLACK;

            Pix expected = NaiveRop.RandomPix(rng, w, h, d);
            Pix actual = Pix.Copy(null, expected);

            NaiveRop.Vip(expected, x, bw, shift);
            if (shift != 0) {   // pixels brought in
                uint fill = InColor(d, incolor);
                int x0 = Math.Max(x, 0), x1 = Math.Min(x + bw, w);
                int y0 = shift > 0 ? 0 : Math.Max(h + shift, 0);
                int y1 = shift > 0 ? Math.Min(shift, h) : h;
                for (int yy = y0; yy < y1; yy++)
                    for (int xx = x0; xx < x1; xx++)
                        NaiveRop.SetPixel(expected, xx, yy, fill);
            }
            actual.RasteropVip(x, bw, shift, incolor);

            string diff = NaiveRop.FirstDifference(expected.Data, actual.Data);
            Assert.True(diff == "",
                $"seed={seed} iter={iter} d={d} image {w}x{h} band x={x} w={bw} shift={shift} incolor={incolor}: {diff}");
        }
    }

    [Fact]
    public void RasteropVipHipRejectBadArguments()
    {
        Pix pix = Pix.Create(10, 10, 8);
        Assert.Throws<ArgumentException>(() => pix.RasteropVip(0, 10, 1, 0));
        Assert.Throws<ArgumentException>(() => pix.RasteropVip(0, 0, 1, L.L_BRING_IN_WHITE));
        Assert.Throws<ArgumentException>(() => pix.RasteropHip(0, 10, 1, 3));
        Assert.Throws<ArgumentException>(() => pix.RasteropHip(0, -1, 1, L.L_BRING_IN_BLACK));
        // a zero shift is a no-op
        uint[] before = (uint[])pix.Data.Clone();
        pix.RasteropVip(0, 10, 0, L.L_BRING_IN_WHITE);
        pix.RasteropHip(0, 10, 0, L.L_BRING_IN_WHITE);
        Assert.Equal(before, pix.Data);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void RasteropHipMatchesNaive(int d)
    {
        int seed = 90000 + d;
        var rng = new Random(seed);
        int checkedCases = 0;
        for (int iter = 0; iter < 1400; iter++) {
            int w = 1 + (iter % 70);
            int h = Rnd(rng, 1, 7);
            int y = Rnd(rng, -3, h + 3);
            int bh = Rnd(rng, 1, h + 4);
            int shift = Rnd(rng, -(w + 3), w + 3);
            int incolor = (iter % 2 == 0) ? L.L_BRING_IN_WHITE : L.L_BRING_IN_BLACK;

            Pix src = NaiveRop.RandomPix(rng, w, h, d);
            int wpl = src.Wpl;
            // see NOTE(upstream) in shiftDataHorizontalLow
            if ((long)Math.Abs(shift) * d > 32L * wpl)
                continue;
            checkedCases++;

            Pix expected = Pix.Copy(null, src);
            Pix actual = Pix.Copy(null, src);

            // The pixels brought in are defined at this level: fill them in the reference.
            var unchecked_ = new bool[h, w];
            NaiveRop.Hip(expected, y, bh, shift, unchecked_);
            uint fill = InColor(d, incolor);
            int y0 = Math.Max(y, 0);
            int y1 = Math.Min(y + bh, h);
            if (shift != 0 && y1 > y0) {
                for (int yy = y0; yy < y1; yy++) {
                    int xa = shift > 0 ? 0 : Math.Max(w + shift, 0);
                    int xb = shift > 0 ? Math.Min(shift, w) : w;
                    for (int xx = xa; xx < xb; xx++) {
                        NaiveRop.SetPixel(expected, xx, yy, fill);
                        unchecked_[yy, xx] = false;
                    }
                }
            }
            actual.RasteropHip(y, bh, shift, incolor);

            for (int row = 0; row < h; row++) {
                bool inBand = row >= y0 && row < y1 && shift != 0;
                for (int x = 0; x < w; x++) {
                    Assert.True(!unchecked_[row, x] &&
                                NaiveRop.GetPixel(expected, x, row) == NaiveRop.GetPixel(actual, x, row),
                        $"seed={seed} iter={iter} d={d} image {w}x{h} band y={y} h={bh} shift={shift} " +
                        $"incolor={incolor}: pixel ({x},{row})");
                }
                if (!inBand) {
                    for (int k = 0; k < wpl; k++)
                        Assert.True(src.Data[row * wpl + k] == actual.Data[row * wpl + k],
                            $"seed={seed} iter={iter} d={d}: row {row} outside the band changed");
                }
            }
        }
        Assert.True(checkedCases > 1000);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void TranslateMatchesNaive(int d)
    {
        int seed = 100000 + d;
        var rng = new Random(seed);
        int checkedCases = 0;
        for (int iter = 0; iter < 1400; iter++) {
            int w = 1 + (iter % 70);
            int h = Rnd(rng, 1, 7);
            int hshift = Rnd(rng, -(w + 3), w + 3);
            int vshift = Rnd(rng, -(h + 3), h + 3);
            int incolor = (iter % 2 == 0) ? L.L_BRING_IN_WHITE : L.L_BRING_IN_BLACK;

            Pix pixs = NaiveRop.RandomPix(rng, w, h, d);
            if ((long)Math.Abs(hshift) * d > 32L * pixs.Wpl)
                continue;   // see NOTE(upstream) in shiftDataHorizontalLow
            checkedCases++;
            Pix orig = Pix.Copy(null, pixs);
            uint fill = InColor(d, incolor);

            Pix pixd;
            switch (iter % 3) {
            case 0:   // new pix
                pixd = Pix.Translate(null, pixs, hshift, vshift, incolor);
                Assert.NotSame(pixs, pixd);
                Assert.Equal(orig.Data, pixs.Data);
                break;
            case 1:   // to an existing pixd of another size
                Pix other = Pix.Create(3, 3, 1);
                pixd = Pix.Translate(other, pixs, hshift, vshift, incolor);
                Assert.Same(other, pixd);
                Assert.Equal(orig.Data, pixs.Data);
                break;
            default:  // in place
                pixd = Pix.Translate(pixs, pixs, hshift, vshift, incolor);
                Assert.Same(pixs, pixd);
                break;
            }

            Assert.Equal(w, pixd.Width);
            Assert.Equal(h, pixd.Height);
            Assert.Equal(d, pixd.Depth);
            for (int yy = 0; yy < h; yy++) {
                for (int xx = 0; xx < w; xx++) {
                    int xs = xx - hshift, ys = yy - vshift;
                    uint want = (xs >= 0 && xs < w && ys >= 0 && ys < h)
                        ? NaiveRop.GetPixel(orig, xs, ys) : fill;
                    Assert.True(want == NaiveRop.GetPixel(pixd, xx, yy),
                        $"seed={seed} iter={iter} d={d} image {w}x{h} shift ({hshift},{vshift}) " +
                        $"incolor={incolor}: pixel ({xx},{yy})");
                }
            }
        }
        Assert.True(checkedCases > 1000);
    }
}
