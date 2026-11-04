using System;
using Leptonica;

namespace Leptonica.Managed.Tests.RotateTests;

/// <summary>
/// Compares pixRotateAMGray and pixRotateAMColor with the plain per-pixel
/// algorithm on random images (fixed seeds).
/// </summary>
public class RotateAMLowTests
{
    private static readonly int[] Sizes = { 2, 3, 4, 5, 7, 8, 9, 16, 17, 31, 32, 33, 64, 65 };

    private static Pix RandomPix(Random rng, int w, int h, int d)
    {
        Pix pix = Pix.Create(w, h, d);
        for (int i = 0; i < pix.Data.Length; i++)
            pix.Data[i] = (uint)rng.NextInt64(0, 1L << 32);
        return pix;
    }

    private static uint Px(Pix p, int x, int y)
    {
        p.GetPixel(x, y, out uint v);
        return v;
    }

    [Theory]
    [InlineData(0.05f)]
    [InlineData(-0.3f)]
    [InlineData(1.2f)]
    public void RotateAMGrayMatchesReference(float angle)
    {
        var rng = new Random(11);
        float sina = (float)(16.0 * Math.Sin(angle));
        float cosa = (float)(16.0 * Math.Cos(angle));
        foreach (int w in Sizes) {
            foreach (int h in Sizes) {
                Pix pix = RandomPix(rng, w, h, 8);
                Pix actual = pix.RotateAMGray(angle, 200);
                int xcen = w / 2, ycen = h / 2;
                for (int i = 0; i < h; i++) {
                    int ydif = ycen - i;
                    for (int j = 0; j < w; j++) {
                        int xdif = xcen - j;
                        int xpm = (int)(-xdif * cosa - ydif * sina);
                        int ypm = (int)(-ydif * cosa + xdif * sina);
                        int xp = xcen + (xpm >> 4), yp = ycen + (ypm >> 4);
                        uint e;
                        if ((uint)xp > (uint)(w - 2) || (uint)yp > (uint)(h - 2)) {
                            e = 200;
                        } else {
                            int xf = xpm & 15, yf = ypm & 15;
                            int v = (16 - xf) * (16 - yf) * (int)Px(pix, xp, yp) + xf * (16 - yf) * (int)Px(pix, xp + 1, yp)
                                  + (16 - xf) * yf * (int)Px(pix, xp, yp + 1) + xf * yf * (int)Px(pix, xp + 1, yp + 1);
                            e = (uint)((v + 128) >> 8);
                        }
                        uint g = Px(actual, j, i);
                        Assert.True(e == g, $"angle={angle} {w}x{h} ({j},{i}): expected {e}, got {g}");
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(0.05f)]
    [InlineData(-0.3f)]
    [InlineData(1.2f)]
    public void RotateAMColorMatchesReference(float angle)
    {
        var rng = new Random(12);
        float sina = (float)(16.0 * Math.Sin(angle));
        float cosa = (float)(16.0 * Math.Cos(angle));
        const uint fill = 0xffffff00;
        foreach (int w in Sizes) {
            foreach (int h in Sizes) {
                Pix pix = RandomPix(rng, w, h, 32);
                Pix actual = pix.RotateAMColor(angle, fill);
                int xcen = w / 2, ycen = h / 2;
                for (int i = 0; i < h; i++) {
                    int ydif = ycen - i;
                    for (int j = 0; j < w; j++) {
                        int xdif = xcen - j;
                        int xpm = (int)(-xdif * cosa - ydif * sina);
                        int ypm = (int)(-ydif * cosa + xdif * sina);
                        int xp = xcen + (xpm >> 4), yp = ycen + (ypm >> 4);
                        uint e;
                        if ((uint)xp > (uint)(w - 2) || (uint)yp > (uint)(h - 2)) {
                            e = fill;
                        } else {
                            int xf = xpm & 15, yf = ypm & 15;
                            uint p00 = Px(pix, xp, yp), p10 = Px(pix, xp + 1, yp);
                            uint p01 = Px(pix, xp, yp + 1), p11 = Px(pix, xp + 1, yp + 1);
                            e = 0;
                            for (int sh = 24; sh >= 8; sh -= 8) {
                                int v = (16 - xf) * (16 - yf) * (int)((p00 >> sh) & 0xff) + xf * (16 - yf) * (int)((p10 >> sh) & 0xff)
                                      + (16 - xf) * yf * (int)((p01 >> sh) & 0xff) + xf * yf * (int)((p11 >> sh) & 0xff);
                                e |= (uint)((v + 128) >> 8) << sh;
                            }
                        }
                        uint g = Px(actual, j, i);
                        Assert.True(e == g, $"angle={angle} {w}x{h} ({j},{i}): expected {e:x8}, got {g:x8}");
                    }
                }
            }
        }
    }
}
