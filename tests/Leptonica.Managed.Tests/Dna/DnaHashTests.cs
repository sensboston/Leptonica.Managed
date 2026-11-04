using System;
using Leptonica;

namespace Leptonica.Managed.Tests.DnaTests;

public class DnaHashTests
{
    [Theory]
    [InlineData(7, 7)]
    [InlineData(12, 13)]
    [InlineData(100, 101)]
    [InlineData(101, 101)]
    public void Create_RoundsUpToAPrime(int requested, int expected)
    {
        var h = L_DnaHash.Create(requested, 0);
        Assert.Equal(expected, h.NBuckets);
    }

    [Fact]
    public void Create_RejectsNonPositiveSizes()
    {
        Assert.Throws<ArgumentException>(() => L_DnaHash.Create(0, 0));
        Assert.Throws<ArgumentException>(() => L_DnaHash.Create(-3, 0));
    }

    [Fact]
    public void GetDna_EmptyBucketIsNull()
    {
        var h = L_DnaHash.Create(11, 10);
        Assert.Null(h.GetDna(5, L.L_NOCOPY));
        Assert.Null(h.GetDna(5, L.L_COPY));
        Assert.Null(h.GetDna(5, L.L_CLONE));
    }

    [Fact]
    public void Add_StoresByKeyModuloBuckets()
    {
        var h = L_DnaHash.Create(11, 0);
        h.Add(3, 1.5);
        h.Add(3 + 11, 2.5);          /* same bucket */
        h.Add(3 + 11UL * 1000000, 3.5);
        h.Add(4, 9.0);               /* another bucket */

        L_Dna da = h.GetDna(3, L.L_NOCOPY)!;
        Assert.Equal(3, da.Count);
        Assert.Equal(1.5, da.GetDValue(0));
        Assert.Equal(2.5, da.GetDValue(1));
        Assert.Equal(3.5, da.GetDValue(2));
        Assert.Equal(1, h.GetDna(4, L.L_NOCOPY)!.Count);
        Assert.Same(da, h.GetDna(3 + 22, L.L_NOCOPY));
    }

    [Fact]
    public void GetDna_CopyAndClone()
    {
        var h = L_DnaHash.Create(7, 5);
        h.Add(2, 10.0);
        L_Dna orig = h.GetDna(2, L.L_NOCOPY)!;

        L_Dna copy = h.GetDna(2, L.L_COPY)!;
        Assert.NotSame(orig, copy);
        copy.AddNumber(11.0);
        Assert.Equal(1, orig.Count);   /* the copy is independent */

        L_Dna clone = h.GetDna(2, L.L_CLONE)!;
        Assert.Equal(1, clone.Count);
        clone.AddNumber(12.0);         /* a clone shares the data */
        Assert.Equal(2, orig.Count);
    }

    [Fact]
    public void Add_LargeKey()
    {
        var h = L_DnaHash.Create(13, 0);
        ulong key = 0xFFFFFFFFFFFFFFFFUL;
        h.Add(key, 1.0);
        Assert.Equal(1, h.GetDna(key % 13, L.L_NOCOPY)!.Count);
    }
}
