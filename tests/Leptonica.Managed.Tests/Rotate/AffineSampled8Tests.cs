using System;
using Leptonica;

namespace Leptonica.Managed.Tests.RotateTests;

/// <summary>
/// Compares the 8 bpp pixAffineSampled with the plain per-pixel expression
/// on random images and random coefficients (fixed seeds).
/// </summary>
public class AffineSampled8Tests
{
    [Fact]
    public void AffineSampled8MatchesReference()
    {
        var rng = new Random(2468);
        var widths = new[] { 1, 2, 3, 4, 5, 7, 8, 9, 31, 32, 33, 64, 65, 100 };
        var heights = new[] { 1, 2, 5, 17 };
        foreach (int w in widths) {
            foreach (int h in heights) {
                for (int trial = 0; trial < 6; trial++) {
                    Pix pix = Pix.Create(w, h, 8);
                    for (int i = 0; i < pix.Data.Length; i++)
                        pix.Data[i] = (uint)rng.NextInt64(0, 1L << 32);

                    float[] vc = new float[6];
                    vc[0] = (float)(0.3 + 1.4 * rng.NextDouble());
                    vc[1] = (float)(rng.NextDouble() - 0.5);
                    vc[2] = (float)(rng.NextDouble() * 80 - 40);
                    vc[3] = (float)(rng.NextDouble() - 0.5);
                    vc[4] = (float)(0.3 + 1.4 * rng.NextDouble());
                    vc[5] = (float)(rng.NextDouble() * 80 - 40);
                    if (trial == 0) {   // identity with sub-pixel shift: exercises the 0.5 rounding
                        vc[0] = 1; vc[1] = 0; vc[2] = 0.5f; vc[3] = 0; vc[4] = 1; vc[5] = -0.5f;
                    } else if (trial == 1) {
                        vc[2] = -1.2f; vc[5] = -1.5f;
                    }
                    byte fill = trial % 2 == 0 ? (byte)255 : (byte)0;
                    Pix actual = pix.AffineSampled(vc, fill == 255 ? L.L_BRING_IN_WHITE : L.L_BRING_IN_BLACK);

                    for (int i = 0; i < h; i++) {
                        float ry1 = vc[1] * i, ry4 = vc[4] * i;
                        for (int j = 0; j < w; j++) {
                            int x = (int)(vc[0] * j + ry1 + vc[2] + 0.5);
                            int y = (int)(vc[3] * j + ry4 + vc[5] + 0.5);
                            uint e = fill;
                            if ((uint)x < (uint)w && (uint)y < (uint)h)
                                pix.GetPixel(x, y, out e);
                            actual.GetPixel(j, i, out uint g);
                            Assert.True(e == g, $"{w}x{h} trial {trial} ({j},{i}): expected {e}, got {g}");
                        }
                    }
                }
            }
        }
    }
}
