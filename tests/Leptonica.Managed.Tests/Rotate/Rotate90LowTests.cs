using System;
using Leptonica;

namespace Leptonica.Managed.Tests.RotateTests;

/// <summary>
/// Compares pixRotate90 (1 and 32 bpp low-level code) with a per-pixel
/// reference on random images (fixed seeds).
/// </summary>
public class Rotate90LowTests
{
    private static readonly int[] Sizes = { 1, 2, 3, 15, 16, 17, 31, 32, 33, 47, 63, 64, 65, 97, 130 };

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, -1)]
    [InlineData(32, 1)]
    [InlineData(32, -1)]
    [InlineData(8, 1)]
    public void Rotate90MatchesReference(int d, int direction)
    {
        var rng = new Random(500 + d * 3 + direction);
        foreach (int w in Sizes) {
            foreach (int h in Sizes) {
                if ((w * 31 + h) % 3 == 0 && w > 40 && h > 40)
                    continue;   // keep the run time down
                Pix pixs = Pix.Create(w, h, d);
                for (int i = 0; i < pixs.Data.Length; i++) {
                    uint v = (uint)rng.NextInt64(0, 1L << 32);
                    if (d == 1 && i % 3 == 0)
                        v = 0;   // some empty words
                    pixs.Data[i] = v;
                }
                Pix actual = pixs.Rotate90(direction);
                Assert.Equal(h, actual.Width);
                Assert.Equal(w, actual.Height);
                for (int y = 0; y < w; y++) {
                    for (int x = 0; x < h; x++) {
                        // clockwise: dest (x, y) = src (y, h - 1 - x); ccw: src (w - 1 - y, x)
                        int sx = direction == 1 ? y : w - 1 - y;
                        int sy = direction == 1 ? h - 1 - x : x;
                        pixs.GetPixel(sx, sy, out uint e);
                        actual.GetPixel(x, y, out uint a);
                        Assert.True(e == a, $"d={d} dir={direction} {w}x{h} ({x},{y}): expected {e}, got {a}");
                    }
                }
                // pad bits of the dest rows stay zero
                if (d == 1) {
                    int wpl = actual.Wpl;
                    for (int y = 0; y < actual.Height; y++)
                        for (int x = actual.Width; x < wpl * 32; x++) {
                            uint bit = (actual.Data[y * wpl + (x >> 5)] >> (31 - (x & 31))) & 1;
                            Assert.True(bit == 0, $"pad bit {x} of row {y} set");
                        }
                }
            }
        }
    }
}
