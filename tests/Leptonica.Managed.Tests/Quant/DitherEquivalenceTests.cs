using System;
using Leptonica;

namespace Leptonica.Managed.Tests.QuantTests;

/// <summary>The branch-free dither line kernel must equal the original scalar kernel.</summary>
public class DitherEquivalenceTests
{
    private static uint[] RandomWords(Random rng, int n, int mode)
    {
        var a = new uint[n];
        for (int i = 0; i < n; i++) {
            if (mode == 0) a[i] = (uint)rng.Next() ^ ((uint)rng.Next() << 16);
            else {
                // mostly near-black / near-white / mid values to hit the clip paths
                uint v = 0;
                for (int k = 0; k < 4; k++) {
                    int c = rng.Next(4);
                    int val = c switch { 0 => rng.Next(0, 14), 1 => rng.Next(242, 256), 2 => rng.Next(100, 156), _ => rng.Next(256) };
                    v = (v << 8) | (uint)val;
                }
                a[i] = v;
            }
        }
        return a;
    }

    [Fact]
    public void LineKernel_MatchesScalarReference()
    {
        var rng = new Random(77);
        foreach (int w in new[] { 1, 2, 3, 31, 32, 33, 63, 64, 65, 97, 129, 250 })
            foreach (int last in new[] { 0, 1 })
                foreach (var (lo, up) in new[] { (10, 10), (5, 5), (0, 0), (20, 3), (255, 255), (1, 200) })
                    for (int mode = 0; mode < 2; mode++)
                        for (int rep = 0; rep < 8; rep++) {
                            int wpl = (w + 3) / 4;
                            uint[] a1 = RandomWords(rng, wpl, mode);
                            uint[] a2 = RandomWords(rng, wpl, mode);
                            uint[] b1 = (uint[])a1.Clone();
                            uint[] b2 = (uint[])a2.Clone();
                            var d1 = new uint[(w + 31) / 32];
                            var d2 = new uint[(w + 31) / 32];
                            Pix.DitherToBinaryLineLowScalar(d1, w, a1, a2, lo, up, last);
                            Pix.DitherToBinaryLineLow(d2, w, b1, b2, lo, up, last);
                            Assert.Equal(d1, d2);
                            Assert.Equal(a1, b1);
                            Assert.Equal(a2, b2);
                        }
    }

    [Fact]
    public void DitherToBinary_MatchesPixelwiseReference()
    {
        var rng = new Random(5);
        foreach (int w in new[] { 1, 2, 31, 32, 33, 64, 65, 101 })
            foreach (int h in new[] { 1, 2, 9 }) {
                Pix src = ThresholdSimdTests.RandomGray(rng, w, h);
                Pix got = src.DitherToBinary();

                // scalar reference over a plain int image
                var img = new int[h, w];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) { src.GetPixel(x, y, out uint v); img[y, x] = (int)v; }
                Pix exp = Pix.Create(w, h, 1);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) {
                        int o = img[y, x];
                        bool right = x < w - 1, down = y < h - 1;
                        if (o > 127) {
                            int e = 255 - o;
                            if (e > 10) {
                                int f1 = 3 * e / 8, f2 = e / 4;
                                if (right) img[y, x + 1] = Math.Max(0, img[y, x + 1] - f1);
                                if (down) img[y + 1, x] = Math.Max(0, img[y + 1, x] - f1);
                                if (down && right) img[y + 1, x + 1] = Math.Max(0, img[y + 1, x + 1] - f2);
                            }
                        } else {
                            exp.SetPixel(x, y, 1);
                            if (o > 10) {
                                int f1 = 3 * o / 8, f2 = o / 4;
                                if (right) img[y, x + 1] = Math.Min(255, img[y, x + 1] + f1);
                                if (down) img[y + 1, x] = Math.Min(255, img[y + 1, x] + f1);
                                if (down && right) img[y + 1, x + 1] = Math.Min(255, img[y + 1, x + 1] + f2);
                            }
                        }
                    }
                Assert.Equal(exp.Data, got.Data);
            }
    }
}
