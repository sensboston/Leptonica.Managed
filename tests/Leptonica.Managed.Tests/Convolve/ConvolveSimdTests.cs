using System;
using Leptonica;

namespace Leptonica.Managed.Tests.ConvolveTests;

public class ConvolveSimdTests
{
    [Fact]
    public void Convolve8_SimdMatchesScalar()
    {
        var rng = new Random(9);
        foreach (int w in new[] { 1, 5, 15, 16, 17, 31, 32, 33, 63, 64, 65, 100 })
            foreach (int h in new[] { 1, 3, 20 })
                foreach (var (sx, sy, sig) in new[] { (3, 3, 1.0f), (5, 3, 2.0f), (1, 1, 1.0f), (7, 7, 1.5f) })
                    foreach (bool signedKernel in new[] { false, true }) {
                        if (w <= sx || h <= sy) continue;
                        Pix s = Pix.Create(w, h, 8);
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                                s.SetPixel(x, y, (uint)rng.Next(256));
                        L_Kernel kel = L_Kernel.MakeGaussianKernel(sy / 2 * 2 + 1 > 0 ? sy / 2 : 0, sx / 2, sig, sig);
                        if (signedKernel) kel.SetElement(0, 0, -0.5f);
                        Pix a = s.Convolve(kel, 8, 1);
                        Pix.ConvolveSimdEnabled = false;
                        Pix b;
                        try { b = s.Convolve(kel, 8, 1); } finally { Pix.ConvolveSimdEnabled = true; }
                        Assert.Equal(b.Data, a.Data);
                    }
    }
}
