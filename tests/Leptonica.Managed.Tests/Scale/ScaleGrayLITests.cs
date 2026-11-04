using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Scale;

/// <summary>
/// Compares pixScaleGray2xLI and pixScaleGrayLI with per-pixel references
/// on random images (fixed seeds).
/// </summary>
public class ScaleGrayLITests
{
    private static readonly int[] Widths = { 1, 2, 3, 4, 5, 15, 16, 17, 18, 31, 32, 33, 34, 35, 47, 48, 49, 63, 64, 65, 66, 100, 129 };
    private static readonly int[] Heights = { 1, 2, 3, 7 };

    private static Pix RandomGray(Random rng, int w, int h)
    {
        Pix pix = Pix.Create(w, h, 8);
        for (int i = 0; i < pix.Data.Length; i++)
            pix.Data[i] = (uint)rng.NextInt64(0, 1L << 32);
        return pix;
    }

    private static uint Px(Pix p, int x, int y)
    {
        p.GetPixel(x, y, out uint v);
        return v;
    }

    [Fact]
    public void ScaleGray2xLIMatchesReference()
    {
        var rng = new Random(2024);
        foreach (int w in Widths) {
            foreach (int h in Heights) {
                Pix pix = RandomGray(rng, w, h);
                Pix actual = pix.ScaleGray2xLI();
                Assert.Equal(2 * w, actual.Width);
                Assert.Equal(2 * h, actual.Height);
                for (int i = 0; i < h; i++) {
                    int i1 = Math.Min(i + 1, h - 1);
                    for (int j = 0; j < w; j++) {
                        int j1 = Math.Min(j + 1, w - 1);
                        uint a = Px(pix, j, i), a1 = Px(pix, j1, i);
                        uint b = Px(pix, j, i1), b1 = Px(pix, j1, i1);
                        uint[] e = { a, (a + a1) >> 1, (a + b) >> 1, (a + a1 + b + b1) >> 2 };
                        uint[] g = {
                            Px(actual, 2 * j, 2 * i), Px(actual, 2 * j + 1, 2 * i),
                            Px(actual, 2 * j, 2 * i + 1), Px(actual, 2 * j + 1, 2 * i + 1) };
                        for (int k = 0; k < 4; k++)
                            Assert.True(e[k] == g[k], $"{w}x{h} src ({j},{i}) pix {k}: expected {e[k]}, got {g[k]}");
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(1.3f, 1.3f)]
    [InlineData(2.3f, 2.3f)]
    [InlineData(1.0f, 3.7f)]
    [InlineData(0.9f, 1.6f)]
    public void ScaleGrayLIMatchesReference(float scalex, float scaley)
    {
        var rng = new Random(77);
        foreach (int w in new[] { 1, 2, 5, 17, 31, 32, 33, 64, 65, 97 }) {
            foreach (int h in new[] { 1, 2, 3, 9 }) {
                Pix pix = RandomGray(rng, w, h);
                Pix actual = pix.ScaleGrayLI(scalex, scaley);
                int wd = (int)(scalex * (float)w + 0.5);
                int hd = (int)(scaley * (float)h + 0.5);
                Assert.Equal(wd, actual.Width);
                Assert.Equal(hd, actual.Height);
                float scx = 16.0f * (float)w / (float)wd;
                float scy = 16.0f * (float)h / (float)hd;
                for (int i = 0; i < hd; i++) {
                    int ypm = (int)(scy * (float)i);
                    int yp = ypm >> 4, yf = ypm & 0xf;
                    int yp2 = yp > h - 2 ? yp : yp + 1;
                    for (int j = 0; j < wd; j++) {
                        int xpm = (int)(scx * (float)j);
                        int xp = xpm >> 4, xf = xpm & 0xf;
                        int xp2 = xp > w - 2 ? xp : xp + 1;
                        int v = (16 - xf) * (16 - yf) * (int)Px(pix, xp, yp) + xf * (16 - yf) * (int)Px(pix, xp2, yp)
                              + (16 - xf) * yf * (int)Px(pix, xp, yp2) + xf * yf * (int)Px(pix, xp2, yp2);
                        uint e = (uint)((v + 128) >> 8);
                        uint g = Px(actual, j, i);
                        Assert.True(e == g, $"{w}x{h} scale ({scalex},{scaley}) at ({j},{i}): expected {e}, got {g}");
                    }
                }
            }
        }
    }
}
