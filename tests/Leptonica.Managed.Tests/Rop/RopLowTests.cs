using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Rop;

/// <summary>
/// Compares the word-oriented low-level rasterops against the naive pixel reference
/// on random images (fixed seeds).
/// </summary>
public class RopLowTests
{
    private const int Iterations = 210;   // 3 sweeps over widths 1..70

    private static int Rnd(Random rng, int lo, int hi) => rng.Next(lo, hi + 1);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void RasteropLowMatchesNaive(int d)
    {
        int pw = 32 / d;   // pixels per word
        for (int op = 0; op < 16; op++) {
            int seed = 1000 * d + op;
            var rng = new Random(seed);
            for (int iter = 0; iter < Iterations; iter++) {
                int w = 1 + (iter % 70);
                int h = Rnd(rng, 1, 5);
                int sw = (iter % 5 == 0) ? w : Rnd(rng, 1, 70);
                int sh = Rnd(rng, 1, 5);
                int dx, dy, dw, dh, sx, sy;
                int mode = (iter / 70 + iter) % 4;
                switch (mode) {
                case 0:   // arbitrary
                    dx = Rnd(rng, -10, w + 10);
                    sx = Rnd(rng, -10, sw + 10);
                    break;
                case 1:   // both rectangles word aligned
                    dx = pw * Rnd(rng, 0, 3);
                    sx = pw * Rnd(rng, 0, 3);
                    break;
                case 2:   // same alignment relative to word boundaries
                    dx = Rnd(rng, 0, w + 3);
                    sx = dx + pw * Rnd(rng, -1, 2);
                    break;
                default:  // full images
                    dx = 0; sx = 0;
                    break;
                }
                dy = Rnd(rng, -2, h + 2);
                sy = Rnd(rng, -2, sh + 2);
                dw = (mode == 3) ? w : Rnd(rng, 0, w + 10);
                dh = (mode == 3) ? h : Rnd(rng, 0, h + 3);

                Pix expected = NaiveRop.RandomPix(rng, w, h, d);
                Pix actual = Pix.Copy(null, expected);
                Pix src = NaiveRop.RandomPix(rng, sw, sh, d);

                NaiveRop.Rasterop(expected, dx, dy, dw, dh, op, src, sx, sy);
                NaiveRop.DispatchLow(actual, dx, dy, dw, dh, op, src, sx, sy);

                string diff = NaiveRop.FirstDifference(expected.Data, actual.Data);
                Assert.True(diff == "",
                    $"seed={seed} iter={iter} d={d} op={op:x} dest {w}x{h} rect ({dx},{dy},{dw},{dh}) " +
                    $"src {sw}x{sh} at ({sx},{sy}): {diff}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void RasteropLowLargeUnalignedSrcMatchesNaive(int d)
    {
        for (int seed = 1; seed <= 12; seed++) {
            var rng = new Random(91000 + 100 * d + seed);
            Pix expected = NaiveRop.RandomPix(rng, 521, 13, d);
            Pix actual = Pix.Copy(null, expected);
            Pix src = NaiveRop.RandomPix(rng, 527, 15, d);

            NaiveRop.Rasterop(expected, 13, 1, 430, 10, L.PIX_SRC, src, 2, 2);
            actual.Rasterop(13, 1, 430, 10, L.PIX_SRC, src, 2, 2);

            string diff = NaiveRop.FirstDifference(expected.Data, actual.Data);
            Assert.True(diff == "",
                $"seed={seed} d={d} large unaligned PIX_SRC: {diff}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void RasteropLowLargeUnalignedInPlaceDisjointMatchesNaive(int d)
    {
        for (int seed = 1; seed <= 40; seed++) {
            var rng = new Random(92000 + 100 * d + seed);
            Pix expected = NaiveRop.RandomPix(rng, 601, 25, d);
            Pix actual = Pix.Copy(null, expected);
            bool copyRight = (seed & 1) == 0;
            int dx = copyRight ? 283 : 17;
            int sx = copyRight ? 17 : 283;

            NaiveRop.Rasterop(expected, dx, 1, 200, 20, L.PIX_SRC, expected, sx, 1);
            actual.Rasterop(dx, 1, 200, 20, L.PIX_SRC, actual, sx, 1);

            string diff = NaiveRop.FirstDifference(expected.Data, actual.Data);
            Assert.True(diff == "",
                $"seed={seed} d={d} large unaligned in-place PIX_SRC: {diff}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void RasteropLowInPlaceDisjointRectangles(int d)
    {
        int pw = 32 / d;
        for (int op = 0; op < 16; op++) {
            int seed = 5000 + 1000 * d + op;
            var rng = new Random(seed);
            for (int iter = 0; iter < Iterations; iter++) {
                int w = 8 + (iter % 70);
                int h = Rnd(rng, 2, 6);
                int dx, dy, sx, sy;
                int dw = Rnd(rng, 1, w / 2);
                int dh = Rnd(rng, 1, h);
                if (iter % 2 == 0) {   // side by side
                    dx = Rnd(rng, 0, w / 2 - dw);
                    sx = (iter % 4 == 0) ? dx + dw + pw * Rnd(rng, 0, 1) : Rnd(rng, dx + dw, w);
                    dy = Rnd(rng, 0, h - dh);
                    sy = Rnd(rng, 0, h - dh);
                } else {               // one above the other
                    dx = Rnd(rng, 0, w - dw);
                    sx = Rnd(rng, 0, w - dw);
                    dh = Rnd(rng, 1, h / 2);
                    dy = Rnd(rng, 0, h / 2 - dh);
                    sy = Rnd(rng, dy + dh, h - dh);
                }

                Pix expected = NaiveRop.RandomPix(rng, w, h, d);
                Pix actual = Pix.Copy(null, expected);

                NaiveRop.Rasterop(expected, dx, dy, dw, dh, op, expected, sx, sy);
                NaiveRop.DispatchLow(actual, dx, dy, dw, dh, op, actual, sx, sy);

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
    public void RasteropVipLowMatchesNaive(int d)
    {
        int seed = 20000 + d;
        var rng = new Random(seed);
        for (int iter = 0; iter < 700; iter++) {
            int w = 1 + (iter % 70);
            int h = Rnd(rng, 1, 7);
            int x = Rnd(rng, -10, w + 10);
            int bw = Rnd(rng, -2, w + 10);
            int shift = Rnd(rng, -(h + 3), h + 3);
            if (iter % 7 == 0) {   // aligned band
                x = (32 / d) * Rnd(rng, 0, 2);
            }

            Pix expected = NaiveRop.RandomPix(rng, w, h, d);
            Pix actual = Pix.Copy(null, expected);

            NaiveRop.Vip(expected, x, bw, shift);
            Pix.rasteropVipLow(actual.Data, w, h, d, actual.Wpl, x, bw, shift);

            string diff = NaiveRop.FirstDifference(expected.Data, actual.Data);
            Assert.True(diff == "",
                $"seed={seed} iter={iter} d={d} image {w}x{h} band x={x} w={bw} shift={shift}: {diff}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void RasteropHipLowMatchesNaive(int d)
    {
        int seed = 30000 + d;
        var rng = new Random(seed);
        int checkedCases = 0;
        for (int iter = 0; iter < 1400; iter++) {
            int w = 1 + (iter % 70);
            int h = Rnd(rng, 1, 7);
            int y = Rnd(rng, -3, h + 3);
            int bh = Rnd(rng, -2, h + 4);
            int shift = Rnd(rng, -(w + 3), w + 3);

            Pix src = NaiveRop.RandomPix(rng, w, h, d);
            int wpl = src.Wpl;
            long shiftBits = (long)Math.Abs(shift) * d;
            // Upstream writes outside the line when the shift is larger than the line
            // (firstdw > wpl, or firstdw == wpl with a partial word); see shiftDataHorizontalLow.
            if (shiftBits > 32L * wpl)
                continue;
            checkedCases++;

            Pix expected = Pix.Copy(null, src);
            Pix actual = Pix.Copy(null, src);
            var unchecked_ = new bool[h, w];

            NaiveRop.Hip(expected, y, bh, shift, unchecked_);
            Pix.rasteropHipLow(actual.Data, h, d, actual.Wpl, y, bh, shift);

            // Rows outside the band must be untouched, pad bits included.
            int y0 = Math.Max(y, 0);
            int y1 = (bh <= 0 || y > h) ? y0 : Math.Min(y + bh, h);
            for (int row = 0; row < h; row++) {
                bool inBand = row >= y0 && row < y1;
                for (int x = 0; x < w; x++) {
                    if (inBand && unchecked_[row, x])
                        continue;
                    Assert.True(NaiveRop.GetPixel(expected, x, row) == NaiveRop.GetPixel(actual, x, row),
                        $"seed={seed} iter={iter} d={d} image {w}x{h} band y={y} h={bh} shift={shift}: " +
                        $"pixel ({x},{row})");
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
}
