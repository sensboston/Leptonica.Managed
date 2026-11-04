using System;
using Leptonica.Internal.Png;

namespace Leptonica.Managed.Tests.Png;

/// <summary>Compares PngFilter.Unfilter with the plain per-byte definition from the PNG specification.</summary>
public class PngUnfilterEquivalenceTests
{
    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    private static void ScalarUnfilter(int type, byte[] row, byte[] prev, int bpp)
    {
        for (int i = 0; i < row.Length; i++)
        {
            int a = i >= bpp ? row[i - bpp] : 0;
            int b = prev[i];
            int c = i >= bpp ? prev[i - bpp] : 0;
            int pred = type switch
            {
                PngFilter.None => 0,
                PngFilter.Sub => a,
                PngFilter.Up => b,
                PngFilter.Avg => (a + b) >> 1,
                _ => Paeth(a, b, c),
            };
            row[i] = (byte)(row[i] + pred);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void UnfilterMatchesScalarReference(int bpp)
    {
        var rng = new Random(1000 + bpp);
        int[] widths = [1, 2, 3, 5, 31, 32, 33, 63, 64, 65, 97, 255, 640];
        foreach (int w in widths)
        {
            int len = w * bpp;
            for (int iter = 0; iter < 20; iter++)
            {
                var filtered = new byte[len];
                var prev = new byte[len];
                rng.NextBytes(filtered);
                rng.NextBytes(prev);
                if ((iter & 1) == 1)
                {
                    // Smooth data so that every branch of the Paeth predictor is taken.
                    for (int i = 0; i < len; i++) prev[i] = (byte)(200 + rng.Next(-3, 4));
                    for (int i = 0; i < len; i++) filtered[i] = (byte)rng.Next(-3, 4);
                }
                else if (iter % 4 == 2)
                {
                    // Extreme values.
                    for (int i = 0; i < len; i++) prev[i] = rng.Next(2) == 0 ? (byte)0 : (byte)255;
                }

                for (int type = PngFilter.None; type <= PngFilter.Paeth; type++)
                {
                    var expected = (byte[])filtered.Clone();
                    ScalarUnfilter(type, expected, prev, bpp);
                    var actual = (byte[])filtered.Clone();
                    PngFilter.Unfilter(type, actual, prev, bpp);
                    Assert.Equal(expected, actual);
                }
            }
        }
    }
}
