using System;
using Leptonica;

namespace Leptonica.Managed.Tests.ConvolveTests;

[Collection("BlockconvSimd")]
public class BlockconvSimdTests
{
    [Fact]
    public void Blockconv_SimdMatchesScalar()
    {
        var rng = new Random(3);
        foreach (int w in new[] { 11, 31, 32, 33, 40, 63, 64, 65, 97, 130 })
            foreach (int h in new[] { 11, 12, 37 })
                foreach (int wc in new[] { 1, 2, 5 })
                    foreach (int bpp in new[] { 8, 32 }) {
                        Pix s = Pix.Create(w, h, bpp);
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                                s.SetPixel(x, y, bpp == 8 ? (uint)rng.Next(256) : ((uint)rng.Next() << 8));
                        // include saturated regions
                        if (w > 20) for (int x = 0; x < 20; x++) s.SetPixel(x, 0, bpp == 8 ? 255u : 0xffffff00u);
                        Pix a = s.Blockconv(wc, wc);
                        Pix.BlockconvSimdEnabled = false;
                        Pix b;
                        try { b = s.Blockconv(wc, wc); } finally { Pix.BlockconvSimdEnabled = true; }
                        Assert.Equal(b.Data, a.Data);
                    }
    }
}
