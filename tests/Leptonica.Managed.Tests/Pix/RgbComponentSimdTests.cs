using System;
using Leptonica;

namespace Leptonica.Managed.Tests.PixTests;

/// <summary>Vectorized Get/SetRGBComponent against per-pixel reference.</summary>
public class RgbComponentSimdTests
{
    private static readonly int[] Widths = { 1, 15, 16, 17, 31, 32, 33, 47, 63, 64, 65, 97 };
    private static readonly int[] Comps = { L.COLOR_RED, L.COLOR_GREEN, L.COLOR_BLUE, L.L_ALPHA_CHANNEL };

    private static Pix Rgb(Random rng, int w, int h)
    {
        Pix p = Pix.Create(w, h, 32);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                p.SetPixel(x, y, (uint)rng.Next() ^ ((uint)rng.Next() << 8));
        return p;
    }

    [Fact]
    public void GetRGBComponent_MatchesReference()
    {
        var rng = new Random(11);
        foreach (int w in Widths)
            foreach (int h in new[] { 1, 3 })
                foreach (int comp in Comps) {
                    Pix s = Rgb(rng, w, h);
                    Pix got = s.GetRGBComponent(comp);
                    Pix exp = Pix.Create(w, h, 8);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++) {
                            s.GetPixel(x, y, out uint v);
                            exp.SetPixel(x, y, (v >> (8 * (3 - comp))) & 0xff);
                        }
                    Assert.Equal(exp.Data, got.Data);
                }
    }

    [Fact]
    public void SetRGBComponent_MatchesReference()
    {
        var rng = new Random(12);
        foreach (int w in Widths)
            foreach (int h in new[] { 1, 3 })
                foreach (int comp in Comps) {
                    Pix d = Rgb(rng, w, h);
                    Pix exp = Pix.Copy(null, d);
                    Pix c = Pix.Create(w, h, 8);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            c.SetPixel(x, y, (uint)rng.Next(256));
                    d.SetRGBComponent(c, comp);
                    int sh = 8 * (3 - comp);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++) {
                            exp.GetPixel(x, y, out uint v);
                            c.GetPixel(x, y, out uint b);
                            exp.SetPixel(x, y, (v & ~(0xffu << sh)) | (b << sh));
                        }
                    Assert.Equal(exp.Data, d.Data);
                }
    }
}
