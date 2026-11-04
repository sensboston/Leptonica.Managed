using System;
using Leptonica;

namespace Leptonica.Managed.Tests.SeedfillTests;

/// <summary>pixDistanceFunction (8 bpp out) and pixSetMasked (8 bpp) against scalar references.</summary>
public class DistanceFunctionSimdTests
{
    private static Pix RandomBinary(Random rng, int w, int h, int density, bool blobs)
    {
        Pix p = Pix.Create(w, h, 1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) {
                bool on = blobs ? ((x / 7 + y / 5) % 3 == 0 && rng.Next(100) < density * 3) : rng.Next(100) < density;
                if (on) p.SetPixel(x, y, 1);
            }
        return p;
    }

    private static int[,] RefDistance(Pix src, int conn)
    {
        int w = src.Width, h = src.Height;
        var d = new int[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) { src.GetPixel(x, y, out uint v); d[y, x] = (int)v; }
        for (int i = 1; i < h - 1; i++)
            for (int j = 1; j < w - 1; j++) {
                if (d[i, j] == 0) continue;
                int min = Math.Min(d[i - 1, j], d[i, j - 1]);
                if (conn == 8) min = Math.Min(min, Math.Min(d[i - 1, j - 1], d[i - 1, j + 1]));
                if (min > 254) min = 254;
                d[i, j] = min + 1;
            }
        for (int i = h - 2; i > 0; i--)
            for (int j = w - 2; j > 0; j--) {
                if (d[i, j] == 0) continue;
                int min = Math.Min(d[i + 1, j], d[i, j + 1]);
                if (conn == 8) min = Math.Min(min, Math.Min(d[i + 1, j - 1], d[i + 1, j + 1]));
                d[i, j] = Math.Min(min + 1, d[i, j]);
            }
        return d;
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void DistanceFunction8_MatchesReference(int conn)
    {
        var rng = new Random(40 + conn);
        foreach (int w in new[] { 3, 4, 15, 16, 17, 31, 32, 33, 47, 63, 64, 65, 100 })
            foreach (int h in new[] { 3, 4, 9, 40 })
                foreach (int density in new[] { 1, 10, 50, 95 })
                    foreach (bool blobs in new[] { false, true }) {
                        Pix s = RandomBinary(rng, w, h, density, blobs);
                        Pix got = s.DistanceFunction(conn, 8, L.L_BOUNDARY_BG);
                        var exp = RefDistance(s, conn);
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++) {
                                got.GetPixel(x, y, out uint v);
                                Assert.Equal(exp[y, x], (int)v);
                            }
                    }
    }

    [Fact]
    public void SetMasked8_MatchesReference()
    {
        var rng = new Random(8);
        foreach (int w in new[] { 1, 2, 7, 8, 9, 15, 16, 17, 31, 32, 33, 63, 65 })
            foreach (int h in new[] { 1, 3 })
                foreach (uint val in new uint[] { 1, 77, 255, 0 }) {
                    Pix m = RandomBinary(rng, w, h, 40, false);
                    Pix a = Pix.Create(w, h, 8);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++) a.SetPixel(x, y, (uint)rng.Next(256));
                    Pix e = Pix.Copy(null, a);
                    a.SetMasked(m, val);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++) {
                            m.GetPixel(x, y, out uint b);
                            if (b != 0) e.SetPixel(x, y, val);
                        }
                    Assert.Equal(e.Data, a.Data);
                }
    }
}
