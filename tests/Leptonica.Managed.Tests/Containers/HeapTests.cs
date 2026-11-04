using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests.ContainersTests;

public class HeapTests
{
    private sealed class Item : IHeapItem
    {
        public float Key;
        public int Id;
        public float HeapKey => Key;
    }

    private static List<int> Drain(L_Heap lh)
    {
        var ids = new List<int>();
        IHeapItem? item;
        while ((item = lh.Remove()) != null)
            ids.Add(((Item)item).Id);
        return ids;
    }

    [Fact]
    public void Decreasing_RemovesLargestFirst()
    {
        L_Heap lh = L_Heap.Create(0, L.L_SORT_DECREASING);
        float[] keys = { 3f, 9f, 1f, 7f, 5f, 8f, 2f };
        for (int i = 0; i < keys.Length; i++)
            lh.Add(new Item { Key = keys[i], Id = i });
        Assert.Equal(7, lh.Count);
        Assert.Equal(new[] { 1, 5, 3, 4, 0, 6, 2 }, Drain(lh));
        Assert.Equal(0, lh.Count);
        Assert.Null(lh.Remove());
    }

    [Fact]
    public void Increasing_RemovesSmallestFirst()
    {
        L_Heap lh = L_Heap.Create(0, L.L_SORT_INCREASING);
        float[] keys = { 3f, 9f, 1f, 7f, 5f, 8f, 2f };
        for (int i = 0; i < keys.Length; i++)
            lh.Add(new Item { Key = keys[i], Id = i });
        Assert.Equal(new[] { 2, 6, 0, 4, 3, 5, 1 }, Drain(lh));
    }

    [Fact]
    public void Add_ExtendsArrayByDoubling()
    {
        L_Heap lh = L_Heap.Create(0, L.L_SORT_INCREASING);
        Assert.Equal(20, lh.NAlloc);
        for (int i = 0; i < 21; i++)
            lh.Add(new Item { Key = 100 - i, Id = i });
        Assert.Equal(40, lh.NAlloc);
        Assert.Equal(20, ((Item)lh.Remove()!).Id);
        Assert.Equal(20, L_Heap.Create(5, L.L_SORT_INCREASING).NAlloc);
        Assert.Equal(20, L_Heap.Create(100001, L.L_SORT_INCREASING).NAlloc);
        Assert.Equal(500, L_Heap.Create(500, L.L_SORT_INCREASING).NAlloc);
    }

    [Fact]
    public void TiesAreRemovedInTheOrderOfTheCHeap()
    {
        // equal keys: the order is determined by the swap rules (valp >= valc stops)
        L_Heap lh = L_Heap.Create(0, L.L_SORT_DECREASING);
        for (int i = 0; i < 5; i++)
            lh.Add(new Item { Key = 1f, Id = i });
        // no swap up on equal keys; removal moves the last item to the root, and
        // no swap down happens on equal keys
        Assert.Equal(new[] { 0, 4, 3, 2, 1 }, Drain(lh));
    }

    [Fact]
    public void GetElement_ReadsWithoutDisturbing()
    {
        L_Heap lh = L_Heap.Create(0, L.L_SORT_DECREASING);
        lh.Add(new Item { Key = 1f, Id = 0 });
        lh.Add(new Item { Key = 5f, Id = 1 });
        Assert.Equal(1, ((Item)lh.GetElement(0)).Id);
        Assert.Equal(0, ((Item)lh.GetElement(1)).Id);
        Assert.Equal(2, lh.Count);
        Assert.Throws<ArgumentException>(() => lh.GetElement(2));
        Assert.Throws<ArgumentException>(() => lh.GetElement(-1));
    }

    [Fact]
    public void Add_NullThrows()
    {
        L_Heap lh = L_Heap.Create(0, L.L_SORT_DECREASING);
        Assert.Throws<ArgumentException>(() => lh.Add(null!));
    }

    [Fact]
    public void Sort_And_SortStrictOrder()
    {
        L_Heap lh = L_Heap.Create(0, L.L_SORT_INCREASING);
        float[] keys = { 4f, 2f, 9f, 1f, 6f, 3f };
        for (int i = 0; i < keys.Length; i++)
            lh.Add(new Item { Key = keys[i], Id = i });
        lh.Sort();   // already in heap order: no effect on the removal order
        lh.SortStrictOrder();
        var sorted = new List<float>();
        for (int i = 0; i < lh.Count; i++)
            sorted.Add(((Item)lh.GetElement(i)).Key);
        var expected = new List<float>(keys);
        expected.Sort();
        Assert.Equal(expected, sorted);
    }

    [Fact]
    public void KeyIsReadAtComparisonTime()
    {
        L_Heap lh = L_Heap.Create(0, L.L_SORT_DECREASING);
        var a = new Item { Key = 1f, Id = 0 };
        var b = new Item { Key = 2f, Id = 1 };
        lh.Add(a);
        lh.Add(b);
        a.Key = 10f;   // changed after insertion: used by the next sort
        lh.Sort();
        Assert.Equal(0, ((Item)lh.Remove()!).Id);
    }

    [Fact]
    public void Print_WritesKeys()
    {
        L_Heap lh = L_Heap.Create(0, L.L_SORT_DECREASING);
        lh.Add(new Item { Key = 1.5f, Id = 0 });
        using MemoryStream ms = new MemoryStream();
        lh.Print(ms);
        string text = Encoding.ASCII.GetString(ms.ToArray());
        Assert.Contains("n = 1", text);
        Assert.Contains("keyval[0] = 1.500000", text);
    }
}
