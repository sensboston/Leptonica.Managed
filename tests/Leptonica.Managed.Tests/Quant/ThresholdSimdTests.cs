using System;
using Leptonica;

namespace Leptonica.Managed.Tests.QuantTests;

/// <summary>Equivalence of the vectorized 8 bpp threshold against a scalar reference.</summary>
public class ThresholdSimdTests
{
    internal static readonly int[] Widths = { 1, 2, 3, 15, 16, 17, 31, 32, 33, 47, 63, 64, 65, 95, 97, 129, 250 };

    internal static Pix RandomGray(Random rng, int w, int h)
    {
        Pix pix = Pix.Create(w, h, 8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pix.SetPixel(x, y, (uint)rng.Next(256));
        return pix;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(128)]
    [InlineData(200)]
    [InlineData(255)]
    [InlineData(256)]
    public void ThresholdToBinary8_MatchesScalarReference(int thresh)
    {
        var rng = new Random(1234 + thresh);
        foreach (int w in Widths)
            foreach (int h in new[] { 1, 2, 7 }) {
                Pix src = RandomGray(rng, w, h);
                Pix dst = src.ThresholdToBinary(thresh);
                Pix exp = Pix.Create(w, h, 1);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) {
                        src.GetPixel(x, y, out uint v);
                        if (v < (uint)thresh) exp.SetPixel(x, y, 1);
                    }
                Assert.Equal(exp.Data, dst.Data);
            }
    }
}
