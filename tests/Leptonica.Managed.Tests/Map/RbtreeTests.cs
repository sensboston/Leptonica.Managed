using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests.MapTests;

public class RbtreeTests
{
    private const int Red = 1;
    private const int Black = 2;

    private static RbType Key(long k)
    {
        RbType key = default;
        key.Itype = k;
        return key;
    }

    private static RbType UKey(ulong k)
    {
        RbType key = default;
        key.Utype = k;
        return key;
    }

    private static RbType FKey(double k)
    {
        RbType key = default;
        key.Ftype = k;
        return key;
    }

    /* Checks the red-black invariants; returns the black height. */
    private static int CheckNode(L_RbtreeNode? n)
    {
        if (n == null) return 1;
        if (n.Color == Red) {
            Assert.True(n.Left == null || n.Left.Color == Black);
            Assert.True(n.Right == null || n.Right.Color == Black);
        }
        if (n.Left != null) Assert.Same(n, n.Left.Parent);
        if (n.Right != null) Assert.Same(n, n.Right.Parent);
        int lh = CheckNode(n.Left);
        int rh = CheckNode(n.Right);
        Assert.Equal(lh, rh);
        return lh + (n.Color == Black ? 1 : 0);
    }

    private static void CheckTree(L_Rbtree t)
    {
        if (t.Root == null) return;
        Assert.Equal(Black, t.Root.Color);
        Assert.Null(t.Root.Parent);
        CheckNode(t.Root);
    }

    private static List<long> InOrder(L_Rbtree t)
    {
        var list = new List<long>();
        for (L_RbtreeNode? n = t.GetFirst(); n != null; n = L_Rbtree.GetNext(n))
            list.Add(n.Key.Itype);
        return list;
    }

    [Fact]
    public void Create_ValidatesKeyType()
    {
        Assert.Throws<ArgumentException>(() => L_Rbtree.Create(4));
        Assert.Throws<ArgumentException>(() => L_Rbtree.Create(-1));
        Assert.NotNull(L_Rbtree.Create(0));    // as in C, keytype 0 is accepted
        Assert.NotNull(L_Rbtree.Create(L.L_FLOAT_TYPE));
    }

    [Fact]
    public void Insert_Lookup_UpdatesExistingKey()
    {
        L_Rbtree t = L_Rbtree.Create(L.L_INT_TYPE);
        Assert.Null(t.Lookup(Key(5)));
        t.Insert(Key(5), Key(50));
        t.Insert(Key(3), Key(30));
        t.Insert(Key(5), Key(55));
        Assert.Equal(55, t.Lookup(Key(5))!.Value.Itype);
        Assert.Equal(30, t.Lookup(Key(3))!.Value.Itype);
        Assert.Equal(2, t.GetCount());
    }

    [Fact]
    public void Insert_Sequential_KeepsInvariants()
    {
        L_Rbtree t = L_Rbtree.Create(L.L_INT_TYPE);
        for (int i = 0; i < 200; i++) {
            t.Insert(Key(i), Key(i * 2));
            CheckTree(t);
        }
        Assert.Equal(200, t.GetCount());
        Assert.Equal(Enumerable.Range(0, 200).Select(i => (long)i), InOrder(t));
    }

    [Fact]
    public void InsertAndDelete_Random_KeepsInvariantsAndOrder()
    {
        var rng = new Random(12345);
        L_Rbtree t = L_Rbtree.Create(L.L_INT_TYPE);
        var model = new SortedSet<long>();
        for (int step = 0; step < 3000; step++) {
            long k = rng.Next(-300, 300);
            if (rng.Next(3) != 0) {
                t.Insert(Key(k), Key(k));
                model.Add(k);
            } else {
                t.Delete(Key(k));
                model.Remove(k);
            }
            if (step % 50 == 0) {
                CheckTree(t);
                Assert.Equal(model.ToList(), InOrder(t));
                Assert.Equal(model.Count, t.GetCount());
            }
        }
        foreach (long k in model.ToList()) {
            t.Delete(Key(k));
            CheckTree(t);
        }
        Assert.Equal(0, t.GetCount());
        Assert.Null(t.Root);
    }

    [Fact]
    public void Delete_MissingKey_DoesNothing()
    {
        L_Rbtree t = L_Rbtree.Create(L.L_INT_TYPE);
        t.Delete(Key(1));
        t.Insert(Key(1), Key(1));
        t.Delete(Key(2));
        Assert.Equal(1, t.GetCount());
    }

    [Fact]
    public void Iteration_ForwardAndBackward()
    {
        L_Rbtree t = L_Rbtree.Create(L.L_INT_TYPE);
        foreach (int k in new[] { 8, 3, 10, 1, 6, 14, 4, 7, 13 })
            t.Insert(Key(k), Key(0));
        Assert.Equal(new long[] { 1, 3, 4, 6, 7, 8, 10, 13, 14 }, InOrder(t));
        var back = new List<long>();
        for (L_RbtreeNode? n = t.GetLast(); n != null; n = L_Rbtree.GetPrev(n))
            back.Add(n.Key.Itype);
        Assert.Equal(new long[] { 14, 13, 10, 8, 7, 6, 4, 3, 1 }, back);
        Assert.Equal(1, t.GetFirst()!.Key.Itype);
        Assert.Equal(14, t.GetLast()!.Key.Itype);
    }

    [Fact]
    public void EmptyTree_HasNoFirstOrLast()
    {
        L_Rbtree t = L_Rbtree.Create(L.L_UINT_TYPE);
        Assert.Null(t.GetFirst());
        Assert.Null(t.GetLast());
        Assert.Equal(0, t.GetCount());
    }

    [Fact]
    public void UnsignedKeys_AreOrderedAsUnsigned()
    {
        L_Rbtree t = L_Rbtree.Create(L.L_UINT_TYPE);
        t.Insert(UKey(ulong.MaxValue), default);
        t.Insert(UKey(1), default);
        t.Insert(UKey(1UL << 63), default);
        Assert.Equal(1UL, t.GetFirst()!.Key.Utype);
        Assert.Equal(ulong.MaxValue, t.GetLast()!.Key.Utype);
    }

    [Fact]
    public void FloatKeys_AreOrdered()
    {
        L_Rbtree t = L_Rbtree.Create(L.L_FLOAT_TYPE);
        foreach (double d in new[] { 2.5, -1.0, 0.0, 100.25 })
            t.Insert(FKey(d), default);
        Assert.Equal(-1.0, t.GetFirst()!.Key.Ftype);
        Assert.Equal(100.25, t.GetLast()!.Key.Ftype);
        Assert.NotNull(t.Lookup(FKey(2.5)));
        Assert.Null(t.Lookup(FKey(2.6)));
    }

    [Fact]
    public void Print_WritesRotatedTree()
    {
        L_Rbtree t = L_Rbtree.Create(L.L_INT_TYPE);
        var empty = new MemoryStream();
        t.Print(empty);
        Assert.Equal("<empty tree>\n", Encoding.ASCII.GetString(empty.ToArray()));

        foreach (int k in new[] { 2, 1, 3 })
            t.Insert(Key(k), default);
        var ms = new MemoryStream();
        t.Print(ms);
        Assert.Equal("    <3>\n2\n    <1>\n\n", Encoding.ASCII.GetString(ms.ToArray()));
    }

    [Fact]
    public void Amap_StoresValuesAndIterates()
    {
        L_Amap m = L_Amap.Create(L.L_UINT_TYPE);
        for (uint k = 10; k > 0; k--)
            m.Insert(UKey(k), Key(k * 7));
        Assert.Equal(10, m.Size);
        Assert.Equal(21, m.Find(UKey(3))!.Value.Itype);
        Assert.Null(m.Find(UKey(11)));
        long sum = 0;
        for (L_RbtreeNode? n = m.GetFirst(); n != null; n = L_Amap.GetNext(n))
            sum += n.Value.Itype;
        Assert.Equal(7 * 55, sum);
        m.Delete(UKey(3));
        Assert.Null(m.Find(UKey(3)));
        Assert.Equal(9, m.Size);
        Assert.Equal(10UL, m.GetLast()!.Key.Utype);
        Assert.Equal(9UL, L_Amap.GetPrev(m.GetLast()!)!.Key.Utype);
        Assert.Throws<ArgumentException>(() => L_Amap.Create(0));
    }

    [Fact]
    public void Aset_StoresUniqueKeys()
    {
        L_Aset s = L_Aset.Create(L.L_INT_TYPE);
        foreach (int k in new[] { 5, 1, 5, 9, 1 })
            s.Insert(Key(k));
        Assert.Equal(3, s.Size);
        Assert.NotNull(s.Find(Key(9)));
        Assert.Null(s.Find(Key(2)));
        s.Delete(Key(5));
        Assert.Equal(2, s.Size);
        Assert.Equal(1, s.GetFirst()!.Key.Itype);
        Assert.Equal(9, s.GetLast()!.Key.Itype);
        Assert.Equal(1, L_Aset.GetPrev(s.GetLast()!)!.Key.Itype);
        Assert.Throws<ArgumentException>(() => L_Aset.Create(7));
    }
}
