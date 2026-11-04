using System;
using Leptonica;

namespace Leptonica.Managed.Tests.EnhanceTests;

/// <summary>
/// Compares pixUnsharpMaskingGray2D (halfwidth 1) and the 32 bpp wrapper with
/// the original scalar float algorithm on random images (fixed seeds).
/// </summary>
public class UnsharpGray2DTests
{
    private static readonly int[] Widths = { 1, 2, 3, 4, 5, 8, 9, 10, 16, 31, 32, 33, 63, 64, 65, 100, 129 };
    private static readonly int[] Heights = { 1, 2, 3, 4, 7 };

    private static Pix RandomPix(Random rng, int w, int h, int d, int mode)
    {
        Pix pix = Pix.Create(w, h, d);
        uint[] data = pix.Data;
        for (int i = 0; i < data.Length; i++) {
            data[i] = mode switch {
                0 => (uint)rng.NextInt64(0, 1L << 32),
                1 => 0xffffffffu,
                2 => 0u,
                // blocks of 0/255 make large differences from the local mean
                _ => (rng.Next(2) == 0 ? 0xff000000u : 0u) | (rng.Next(2) == 0 ? 0x00ff0000u : 0u)
                     | (rng.Next(2) == 0 ? 0x0000ff00u : 0u) | (rng.Next(2) == 0 ? 0x000000ffu : 0u),
            };
        }
        return pix;
    }

    // The scalar algorithm as it was before vectorization.
    private static byte[,] Reference(Pix pix, float fract)
    {
        int w = pix.Width, h = pix.Height;
        var src = new int[h, w];
        var res = new byte[h, w];
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                pix.GetPixel(x, y, out uint v);
                src[y, x] = (int)v;
                res[y, x] = (byte)v;
            }
        }
        var hs = new float[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 1; x < w - 1; x++)
                hs[y, x] = src[y, x - 1] + src[y, x] + src[y, x + 1];
        const float norm1 = 1.0f / 9.0f;
        for (int y = 1; y < h - 1; y++) {
            for (int x = 1; x < w - 1; x++) {
                float low = norm1 * (hs[y - 1, x] + hs[y, x] + hs[y + 1, x]);
                int sval = src[y, x];
                int ival = Math.Clamp((int)(sval + fract * (sval - low) + 0.5), 0, 255);
                res[y, x] = (byte)ival;
            }
        }
        return res;
    }

    [Theory]
    [InlineData(0.2f)]
    [InlineData(0.4f)]
    [InlineData(0.77f)]
    [InlineData(1.0f)]
    [InlineData(3.5f)]
    [InlineData(40.0f)]
    public void Gray2DHalfwidth1MatchesReference(float fract)
    {
        var rng = new Random(1234);
        foreach (int w in Widths) {
            foreach (int h in Heights) {
                for (int mode = 0; mode < 4; mode++) {
                    Pix pix = RandomPix(rng, w, h, 8, mode);
                    Pix actual = pix.UnsharpMaskingGray2D(1, fract);
                    byte[,] expected = Reference(pix, fract);
                    for (int y = 0; y < h; y++) {
                        for (int x = 0; x < w; x++) {
                            actual.GetPixel(x, y, out uint a);
                            Assert.True(expected[y, x] == a,
                                $"fract={fract} w={w} h={h} mode={mode} ({x},{y}): expected {expected[y, x]}, got {a}");
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(0.2f)]
    [InlineData(0.4f)]
    public void Rgb2DMatchesReference(float fract)
    {
        var rng = new Random(4321);
        foreach (int w in new[] { 3, 5, 16, 17, 33, 64, 90 }) {
            foreach (int h in new[] { 3, 4, 9 }) {
                Pix pix = RandomPix(rng, w, h, 32, 0);
                Pix actual = pix.UnsharpMaskingFast(1, fract, L.L_BOTH_DIRECTIONS);
                for (int comp = 0; comp < 3; comp++) {
                    Pix chan = pix.GetRGBComponent(comp);
                    byte[,] expected = Reference(chan, fract);
                    Pix achan = actual.GetRGBComponent(comp);
                    for (int y = 0; y < h; y++) {
                        for (int x = 0; x < w; x++) {
                            achan.GetPixel(x, y, out uint a);
                            Assert.True(expected[y, x] == a,
                                $"comp={comp} fract={fract} w={w} h={h} ({x},{y}): expected {expected[y, x]}, got {a}");
                        }
                    }
                }
            }
        }
    }
}
