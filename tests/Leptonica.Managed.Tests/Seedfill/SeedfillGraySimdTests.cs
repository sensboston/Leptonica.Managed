using System;
using Leptonica;

namespace Leptonica.Managed.Tests.SeedfillTests;

/// <summary>pixSeedfillGray against naive grayscale reconstruction by dilation.</summary>
public class SeedfillGraySimdTests
{
    private static byte[,] Reference(byte[,] seed, byte[,] mask, int conn)
    {
        int h = seed.GetLength(0), w = seed.GetLength(1);
        var j = (byte[,])seed.Clone();
        bool changed = true;
        while (changed) {
            changed = false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) {
                    int mx = j[y, x];
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++) {
                            if (conn == 4 && dx != 0 && dy != 0) continue;
                            int yy = y + dy, xx = x + dx;
                            if (yy < 0 || yy >= h || xx < 0 || xx >= w) continue;
                            mx = Math.Max(mx, j[yy, xx]);
                        }
                    int v = Math.Min(mx, (int)mask[y, x]);
                    if (v > j[y, x]) { j[y, x] = (byte)v; changed = true; }
                }
        }
        return j;
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void SeedfillGray_MatchesReference(int conn)
    {
        var rng = new Random(100 + conn);
        foreach (int w in new[] { 1, 2, 15, 16, 17, 31, 32, 33, 47, 64, 65, 80 })
            foreach (int h in new[] { 1, 2, 9, 40 })
                foreach (int density in new[] { 0, 2, 30 })
                    foreach (int zeroMask in new[] { 0, 1 }) {
                        var mask = new byte[h, w];
                        var seed = new byte[h, w];
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++) {
                                mask[y, x] = (byte)(zeroMask == 1 && rng.Next(4) == 0 ? 0 : rng.Next(1, 256));
                                if (rng.Next(100) < density)
                                    seed[y, x] = (byte)rng.Next(0, mask[y, x] + 1);
                            }
                        Pix ps = Pix.Create(w, h, 8), pm = Pix.Create(w, h, 8);
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++) {
                                ps.SetPixel(x, y, seed[y, x]);
                                pm.SetPixel(x, y, mask[y, x]);
                            }
                        ps.SeedfillGray(pm, conn);
                        var exp = Reference(seed, mask, conn);
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++) {
                                ps.GetPixel(x, y, out uint v);
                                Assert.Equal((uint)exp[y, x], v);
                            }
                    }
    }
}
