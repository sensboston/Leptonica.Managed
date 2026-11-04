using System;
using Leptonica;

namespace Leptonica.Managed.Tests.HashmapTests;

public class HashmapTests
{
    private static int CountItems(L_Hashmap hmap)
    {
        int n = 0;
        for (int i = 0; i < hmap.TabSize; i++)
            for (L_Hashitem? h = hmap.HashTab[i]; h != null; h = h.Next)
                n++;
        return n;
    }

    [Fact]
    public void Create_UsesDefaults()
    {
        L_Hashmap hmap = L_Hashmap.Create(0, 0);
        Assert.Equal(0, hmap.NItems);
        Assert.Equal(2000, hmap.NToGo);
        Assert.Equal(2, hmap.MaxOcc);
        Assert.Equal(hmap.TabSize, hmap.HashTab.Length);
        Assert.True(hmap.TabSize > 1000);      // first prime above 2000 / 2
    }

    [Fact]
    public void Create_BadMaxoccFallsBackToDefault()
    {
        Assert.Equal(2, L_Hashmap.Create(100, 9).MaxOcc);
        Assert.Equal(5, L_Hashmap.Create(100, 5).MaxOcc);
    }

    [Fact]
    public void Lookup_CreateAndCheck()
    {
        L_Hashmap hmap = L_Hashmap.Create(0, 2);
        Assert.Null(hmap.Lookup(77, 0, L.L_HMAP_CHECK));
        L_Hashitem item = hmap.Lookup(77, 5, L.L_HMAP_CREATE)!;
        Assert.Equal((77UL, 5UL, 1), (item.Key, item.Val, item.Count));
        L_Hashitem again = hmap.Lookup(77, 9, L.L_HMAP_CREATE)!;
        Assert.Same(item, again);
        Assert.Equal(2, again.Count);
        Assert.Equal(5UL, again.Val);    // the first value stays
        Assert.Same(item, hmap.Lookup(77, 0, L.L_HMAP_CHECK));
        Assert.Equal(2, item.Count);     // CHECK does not count
        Assert.Equal(1, hmap.NItems);
        Assert.Equal(1999, hmap.NToGo);
        Assert.Throws<ArgumentException>(() => hmap.Lookup(1, 1, 0));
    }

    [Fact]
    public void Lookup_CollidingKeysShareAList()
    {
        L_Hashmap hmap = L_Hashmap.Create(0, 2);
        ulong k1 = 5, k2 = 5 + (ulong)hmap.TabSize;
        hmap.Lookup(k1, 1, L.L_HMAP_CREATE);
        hmap.Lookup(k2, 2, L.L_HMAP_CREATE);
        Assert.Equal(1UL, hmap.Lookup(k1, 0, L.L_HMAP_CHECK)!.Val);
        Assert.Equal(2UL, hmap.Lookup(k2, 0, L.L_HMAP_CHECK)!.Val);
        Assert.Equal(2, hmap.NItems);
        // newest item is at the head of the list
        Assert.Equal(k2, hmap.HashTab[5]!.Key);
        Assert.Equal(k1, hmap.HashTab[5]!.Next!.Key);
    }

    [Fact]
    public void Rehash_GrowsTableAndKeepsItems()
    {
        L_Hashmap hmap = L_Hashmap.Create(0, 2);
        int tab0 = hmap.TabSize;
        // hash-like keys spread over the table
        for (int i = 0; i < 2500; i++)
            hmap.Lookup((ulong)i * 2654435761UL, (ulong)i, L.L_HMAP_CREATE);
        Assert.True(hmap.TabSize > tab0);
        Assert.Equal(2500, hmap.NItems);
        Assert.Equal(2500, CountItems(hmap));
        for (int i = 0; i < 2500; i += 97) {
            L_Hashitem? h = hmap.Lookup((ulong)i * 2654435761UL, 0, L.L_HMAP_CHECK);
            Assert.NotNull(h);
            Assert.Equal((ulong)i, h!.Val);
        }
        Assert.Equal(hmap.MaxOcc * hmap.TabSize - hmap.NItems, hmap.NToGo);
    }
}
