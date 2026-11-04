using System;
using Leptonica.Internal.Png;

namespace Leptonica.Managed.Tests.Png;

public class PngFilterTests
{
    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    /// <summary>Straightforward per-byte filter, the definition from the PNG specification.</summary>
    private static byte Reference(int type, byte[] row, byte[] prev, int bpp, int i)
    {
        int x = row[i];
        int a = i >= bpp ? row[i - bpp] : 0;
        int b = prev[i];
        int c = i >= bpp ? prev[i - bpp] : 0;
        return (byte)(type switch
        {
            PngFilter.None => x,
            PngFilter.Sub => x - a,
            PngFilter.Up => x - b,
            PngFilter.Avg => x - ((a + b) >> 1),
            _ => x - Paeth(a, b, c),
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void ApplyMatchesSpecificationAndUnfilterInverts(int bpp)
    {
        var rng = new Random(bpp);
        foreach (int len in new[] { bpp, bpp + 1, 7, 31, 32, 33, 64, 100, 257, 1000 })
        {
            var row = new byte[len];
            var prev = new byte[len];
            // Mix of smooth and random data so that all branches of Paeth are taken.
            rng.NextBytes(row);
            rng.NextBytes(prev);
            for (int i = len / 2; i < len; i++) { prev[i] = (byte)(row[i] + rng.Next(-2, 3)); }

            for (int type = PngFilter.None; type <= PngFilter.Paeth; type++)
            {
                var filtered = new byte[len];
                PngFilter.Apply(type, row, prev, bpp, filtered);
                for (int i = 0; i < len; i++)
                    Assert.Equal(Reference(type, row, prev, bpp, i), filtered[i]);

                var restored = (byte[])filtered.Clone();
                PngFilter.Unfilter(type, restored, prev, bpp);
                Assert.Equal(row, restored);
            }
        }
    }

    [Fact]
    public void CostIsSumOfAbsoluteSignedValues()
    {
        var rng = new Random(11);
        foreach (int len in new[] { 0, 1, 5, 31, 32, 33, 64, 1000, 20000 })
        {
            var data = new byte[len];
            rng.NextBytes(data);
            if (len > 3) { data[0] = 0x80; data[1] = 0x7f; data[2] = 0xff; data[3] = 0x01; }
            int expected = 0;
            foreach (byte b in data) expected += b < 128 ? b : 256 - b;
            Assert.Equal(expected, PngFilter.Cost(data, int.MaxValue));
            // With a limit the result is only guaranteed to exceed it when the true sum does.
            if (expected > 10)
                Assert.True(PngFilter.Cost(data, expected / 2) > expected / 2);
            Assert.Equal(expected, PngFilter.Cost(data, expected));
        }
    }

    [Fact]
    public void UnknownFilterTypeIsAnError()
    {
        Assert.Throws<PngException>(() => PngFilter.Unfilter(5, new byte[4], new byte[4], 1));
    }
}
