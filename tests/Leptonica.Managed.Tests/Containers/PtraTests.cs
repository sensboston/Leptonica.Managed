using System;
using System.Collections.Generic;
using System.Linq;
using Leptonica;

namespace Leptonica.Managed.Tests.ContainersTests;

public class PtraTests
{
    private sealed class Item
    {
        public int Id;
        public Item(int id) { Id = id; }
    }

    private static List<int?> Snapshot(L_Ptra<Item> pa, int n)
    {
        var list = new List<int?>();
        for (int i = 0; i < n; i++)
            list.Add(pa.GetPtrToItem(i)?.Id);
        return list;
    }

    private static L_Ptra<Item> Make(params int[] ids)
    {
        var pa = L_Ptra<Item>.Create(0);
        foreach (int id in ids)
            pa.Add(new Item(id));
        return pa;
    }

    [Fact]
    public void Create_DefaultAndLimits()
    {
        var pa = L_Ptra<Item>.Create(0);
        Assert.Equal(-1, pa.MaxIndex);
        Assert.Equal(0, pa.ActualCount);
        Assert.NotNull(L_Ptra<Item>.Create(5));
        Assert.Throws<LeptonicaException>(() => L_Ptra<Item>.Create(1000002));
    }

    [Fact]
    public void Add_GrowsArray()
    {
        var pa = L_Ptra<Item>.Create(2);
        for (int i = 0; i < 50; i++)
            pa.Add(new Item(i));
        Assert.Equal(49, pa.MaxIndex);
        Assert.Equal(50, pa.ActualCount);
        Assert.Equal(37, pa.GetPtrToItem(37)!.Id);
        Assert.Null(pa.GetPtrToItem(60));
        Assert.Null(pa.GetPtrToItem(1000));    // outside the array: reported, null returned
        Assert.Null(pa.GetPtrToItem(-1));
    }

    [Fact]
    public void Insert_IntoHoleAndBeyondMax()
    {
        var pa = L_Ptra<Item>.Create(4);
        pa.Insert(2, new Item(7), L.L_MIN_DOWNSHIFT);
        Assert.Equal(2, pa.MaxIndex);
        Assert.Equal(1, pa.ActualCount);
        pa.Insert(0, new Item(5), L.L_MIN_DOWNSHIFT);
        Assert.Equal(2, pa.MaxIndex);
        Assert.Equal(new int?[] { 5, null, 7, null }, Snapshot(pa, 4));
        // inserting at nalloc extends the array
        pa.Insert(4, new Item(9), L.L_MIN_DOWNSHIFT);
        Assert.Equal(4, pa.MaxIndex);
        Assert.Equal(9, pa.GetPtrToItem(4)!.Id);
        Assert.Throws<ArgumentException>(() => pa.Insert(-1, null, L.L_MIN_DOWNSHIFT));
        Assert.Throws<ArgumentException>(() => pa.Insert(0, null, 7));
    }

    [Fact]
    public void Insert_FullDownshift()
    {
        var pa = Make(1, 2, 3);
        pa.Insert(1, new Item(9), L.L_FULL_DOWNSHIFT);
        Assert.Equal(new int?[] { 1, 9, 2, 3 }, Snapshot(pa, 4));
        Assert.Equal(3, pa.MaxIndex);
        Assert.Equal(4, pa.ActualCount);
    }

    [Fact]
    public void Insert_MinDownshift_StopsAtFirstHole()
    {
        var pa = L_Ptra<Item>.Create(10);
        pa.Insert(0, new Item(1), L.L_MIN_DOWNSHIFT);
        pa.Insert(1, new Item(2), L.L_MIN_DOWNSHIFT);
        pa.Insert(3, new Item(3), L.L_MIN_DOWNSHIFT);
        pa.Insert(5, new Item(4), L.L_MIN_DOWNSHIFT);
        // insert at 0: 1,2 shift down into the hole at index 2; 3 and 4 stay
        pa.Insert(0, new Item(9), L.L_MIN_DOWNSHIFT);
        Assert.Equal(new int?[] { 9, 1, 2, 3, null, 4 }, Snapshot(pa, 6));
        Assert.Equal(5, pa.MaxIndex);
        Assert.Equal(5, pa.ActualCount);
    }

    [Fact]
    public void Insert_AutoDownshiftBehavesLikeFullWhenCompact()
    {
        var pa = Make(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
        pa.Insert(3, new Item(99), L.L_AUTO_DOWNSHIFT);
        Assert.Equal(new int?[] { 1, 2, 3, 99, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, Snapshot(pa, 13));
        Assert.Equal(12, pa.MaxIndex);
    }

    [Fact]
    public void Remove_NoCompactionLeavesHole()
    {
        var pa = Make(1, 2, 3, 4);
        Assert.Equal(2, pa.Remove(1, L.L_NO_COMPACTION)!.Id);
        Assert.Equal(new int?[] { 1, null, 3, 4 }, Snapshot(pa, 4));
        Assert.Equal(3, pa.MaxIndex);
        Assert.Equal(3, pa.ActualCount);
        // removing the last one lowers imax to the previous occupied slot
        Assert.Equal(4, pa.Remove(3, L.L_NO_COMPACTION)!.Id);
        Assert.Equal(2, pa.MaxIndex);
        Assert.Equal(3, pa.Remove(2, L.L_NO_COMPACTION)!.Id);
        Assert.Equal(0, pa.MaxIndex);
        Assert.Throws<ArgumentException>(() => pa.Remove(1, L.L_NO_COMPACTION));
        Assert.Throws<ArgumentException>(() => pa.Remove(-1, L.L_NO_COMPACTION));
    }

    [Fact]
    public void Remove_Compaction()
    {
        var pa = Make(1, 2, 3, 4);
        pa.Remove(1, L.L_COMPACTION);
        Assert.Equal(new int?[] { 1, 3, 4 }, Snapshot(pa, 3));
        Assert.Equal(2, pa.MaxIndex);
        Assert.Equal(3, pa.ActualCount);
    }

    [Fact]
    public void RemoveLast_PopsLikeAStack()
    {
        var pa = Make(1, 2);
        Assert.Equal(2, pa.RemoveLast()!.Id);
        Assert.Equal(1, pa.RemoveLast()!.Id);
        Assert.Null(pa.RemoveLast());
        Assert.Equal(-1, pa.MaxIndex);
    }

    [Fact]
    public void Replace_ReturnsOldItemAndUpdatesCount()
    {
        var pa = Make(1, 2, 3);
        Assert.Equal(2, pa.Replace(1, new Item(20), false)!.Id);
        Assert.Equal(20, pa.GetPtrToItem(1)!.Id);
        Assert.Equal(3, pa.ActualCount);
        Assert.Null(pa.Replace(1, null, true));
        Assert.Equal(2, pa.ActualCount);
        Assert.Null(pa.Replace(1, new Item(5), false));
        Assert.Equal(3, pa.ActualCount);
        Assert.Throws<ArgumentException>(() => pa.Replace(3, null, false));
    }

    [Fact]
    public void Swap_ExchangesItems()
    {
        var pa = Make(1, 2, 3, 4);
        pa.Swap(0, 3);
        Assert.Equal(new int?[] { 4, 2, 3, 1 }, Snapshot(pa, 4));
        pa.Swap(2, 2);
        Assert.Equal(new int?[] { 4, 2, 3, 1 }, Snapshot(pa, 4));
        Assert.Equal(4, pa.ActualCount);
        Assert.Throws<ArgumentException>(() => pa.Swap(0, 9));
    }

    [Fact]
    public void Reverse_ReversesOrder()
    {
        var pa = Make(1, 2, 3, 4, 5);
        pa.Reverse();
        Assert.Equal(new int?[] { 5, 4, 3, 2, 1 }, Snapshot(pa, 5));
    }

    [Fact]
    public void CompactArray_FillsHoles()
    {
        var pa = Make(1, 2, 3, 4, 5);
        pa.Remove(1, L.L_NO_COMPACTION);
        pa.Remove(3, L.L_NO_COMPACTION);
        pa.CompactArray();
        Assert.Equal(new int?[] { 1, 3, 5 }, Snapshot(pa, 3));
        Assert.Equal(2, pa.MaxIndex);
        Assert.Equal(3, pa.ActualCount);
    }

    [Fact]
    public void Join_MovesItemsAndEmptiesSource()
    {
        var a = Make(1, 2);
        var b = Make(3, 4, 5);
        a.Join(b);
        Assert.Equal(new int?[] { 1, 2, 3, 4, 5 }, Snapshot(a, 5));
        Assert.Equal(0, b.ActualCount);
        Assert.Equal(-1, b.MaxIndex);
        a.Join(null);
        Assert.Equal(5, a.ActualCount);
    }

    [Fact]
    public void Ptraa_InsertGetAndFlatten()
    {
        var paa = L_Ptraa<Item>.Create(4);
        Assert.Equal(4, paa.Size);
        Assert.Null(paa.GetPtra(0, L.L_HANDLE_ONLY));
        var p1 = Make(1, 2);
        var p3 = Make(3, 4, 5);
        paa.InsertPtra(3, p3);
        paa.InsertPtra(1, p1);
        Assert.Throws<ArgumentException>(() => paa.InsertPtra(1, Make(9)));
        Assert.Throws<ArgumentException>(() => paa.InsertPtra(4, Make(9)));
        Assert.Same(p1, paa.GetPtra(1, L.L_HANDLE_ONLY));
        Assert.Same(p1, paa.GetPtra(1, L.L_HANDLE_ONLY));
        Assert.Throws<ArgumentException>(() => paa.GetPtra(1, 5));

        L_Ptra<Item> flat = paa.FlattenToPtra();
        Assert.Equal(new int?[] { 1, 2, 3, 4, 5 }, Snapshot(flat, 5));
        Assert.Null(paa.GetPtra(1, L.L_HANDLE_ONLY));
        Assert.Null(paa.GetPtra(3, L.L_HANDLE_ONLY));
        Assert.Throws<ArgumentException>(() => L_Ptraa<Item>.Create(0));
    }

    [Fact]
    public void Ptraa_GetPtraRemoveClearsSlot()
    {
        var paa = L_Ptraa<Item>.Create(2);
        var p = Make(1);
        paa.InsertPtra(0, p);
        Assert.Same(p, paa.GetPtra(0, L.L_REMOVE));
        Assert.Null(paa.GetPtra(0, L.L_HANDLE_ONLY));
    }

    [Fact]
    public void RandomOperations_MatchModel()
    {
        var rng = new Random(7);
        var pa = L_Ptra<Item>.Create(3);
        var model = new List<int?>();
        for (int step = 0; step < 400; step++) {
            int op = rng.Next(4);
            if (op == 0 || model.Count(x => x != null) == 0) {
                int id = rng.Next(1000);
                pa.Add(new Item(id));
                model.Add(id);
            } else if (op == 1) {
                int idx = rng.Next(model.Count);
                var item = pa.Remove(idx, L.L_NO_COMPACTION);
                Assert.Equal(model[idx], item?.Id);
                model[idx] = null;
                while (model.Count > 0 && model[^1] == null) model.RemoveAt(model.Count - 1);
            } else if (op == 2) {
                int idx = rng.Next(model.Count);
                int id = rng.Next(1000);
                var old = pa.Replace(idx, new Item(id), false);
                Assert.Equal(model[idx], old?.Id);
                model[idx] = id;
            } else {
                int a = rng.Next(model.Count), b = rng.Next(model.Count);
                pa.Swap(a, b);
                (model[a], model[b]) = (model[b], model[a]);
                while (model.Count > 0 && model[^1] == null) model.RemoveAt(model.Count - 1);
            }
            Assert.Equal(model.Count - 1, pa.MaxIndex);
            Assert.Equal(model.Count(x => x != null), pa.ActualCount);
        }
        for (int i = 0; i < model.Count; i++)
            Assert.Equal(model[i], pa.GetPtrToItem(i)?.Id);
    }
}
