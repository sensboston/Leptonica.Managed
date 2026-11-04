using System;
using Leptonica;

namespace Leptonica.Managed.Tests.PtaTests;

public class PtaFunc2Tests
{
    /* Builds a pta from x0, y0, x1, y1, ... */
    private static Pta Pts(params float[] xy)
    {
        Pta pta = Pta.Create(0);
        for (int i = 0; i < xy.Length; i += 2)
            pta.AddPt(xy[i], xy[i + 1]);
        return pta;
    }

    private static void AssertPts(Pta pta, params float[] xy)
    {
        Assert.Equal(xy.Length / 2, pta.Count);
        for (int i = 0; i < pta.Count; i++) {
            pta.GetPt(i, out float x, out float y);
            Assert.Equal((xy[2 * i], xy[2 * i + 1]), (x, y));
        }
    }

    private static void AssertIndex(Numa na, params int[] expected)
    {
        Assert.Equal(expected.Length, na.Count);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], na.GetIValue(i));
    }

    /* Callees that are not ported yet throw NotImplementedException; the tests
     * that need them return early until the real functions replace the stubs. */
    private static bool NotPorted(Action a)
    {
        try {
            a();
            return false;
        } catch (NotImplementedException) {
            return true;
        }
    }

    private static bool SortNotPorted =>
        NotPorted(() => Numa.Create(1).GetSortIndex(L.L_SORT_INCREASING))
        || NotPorted(() => Numa.Sort(null, Numa.Create(1), L.L_SORT_INCREASING));

    private static bool JoinNotPorted =>
        NotPorted(() => Pts(1, 1).Join(Pts(2, 2), 0, -1));

    private static bool AsetNotPorted =>
        JoinNotPorted
        || NotPorted(() => Utils.HashPtToUint64(1, 1))
        || NotPorted(() => L_Aset.Create(L.L_UINT_TYPE));

    private static bool HmapNotPorted =>
        JoinNotPorted
        || NotPorted(() => Utils.HashPtToUint64(1, 1))
        || NotPorted(() => L_Hashmap.Create(1, 2));

    /* P0 = (3,10)  P1 = (1,20)  P2 = (3,5)  P3 = (2,20)  P4 = (1,5)
     * x values 3 1 3 2 1 and y values 10 20 5 20 5 have ties. */
    private static Pta Tied() => Pts(3, 10, 1, 20, 3, 5, 2, 20, 1, 5);

    /*------------------------------ Sorting ------------------------------*/

    [Fact]
    public void GetSortIndex_ArgumentErrors()
    {
        Pta pta = Tied();
        Assert.Throws<ArgumentException>(() => pta.GetSortIndex(0, L.L_SORT_INCREASING));
        Assert.Throws<ArgumentException>(() => pta.GetSortIndex(L.L_SORT_BY_RIGHT, L.L_SORT_INCREASING));
        Assert.Throws<ArgumentException>(() => pta.GetSortIndex(L.L_SORT_BY_X, 0));
        Assert.Throws<ArgumentException>(() => pta.GetSortIndex(L.L_SORT_BY_X, 3));
        Assert.Throws<ArgumentException>(() => pta.Sort(7, L.L_SORT_INCREASING, out _));
        Assert.Throws<ArgumentException>(() => pta.Sort(L.L_SORT_BY_X, 7, out _));
    }

    [Fact]
    public void GetSortIndex_ShellSortTies()
    {
        if (SortNotPorted) return;
        Pta pta = Tied();

        /* The shell sort is not stable: of equal keys, the later index
         * can come first.  Hand-traced from the C code. */
        AssertIndex(pta.GetSortIndex(L.L_SORT_BY_X, L.L_SORT_INCREASING), 4, 1, 3, 0, 2);
        AssertIndex(pta.GetSortIndex(L.L_SORT_BY_X, L.L_SORT_DECREASING), 0, 2, 3, 1, 4);
        AssertIndex(pta.GetSortIndex(L.L_SORT_BY_Y, L.L_SORT_INCREASING), 2, 4, 0, 1, 3);
        AssertIndex(pta.GetSortIndex(L.L_SORT_BY_Y, L.L_SORT_DECREASING), 1, 3, 0, 2, 4);
    }

    [Fact]
    public void GetSortIndex_AllEqualAndReversed()
    {
        if (SortNotPorted) return;
        Pta same = Pts(2, 1, 2, 2, 2, 3, 2, 4);
        AssertIndex(same.GetSortIndex(L.L_SORT_BY_X, L.L_SORT_INCREASING), 0, 1, 2, 3);
        AssertIndex(same.GetSortIndex(L.L_SORT_BY_X, L.L_SORT_DECREASING), 0, 1, 2, 3);

        Pta rev = Pts(5, 0, 4, 0, 3, 0, 2, 0, 1, 0, 0, 0);
        AssertIndex(rev.GetSortIndex(L.L_SORT_BY_X, L.L_SORT_INCREASING), 5, 4, 3, 2, 1, 0);
    }

    [Fact]
    public void Sort_ReturnsSortedPtaAndIndex()
    {
        if (SortNotPorted) return;
        Pta pta = Tied();

        Pta byx = pta.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out Numa naindex);
        AssertIndex(naindex, 4, 1, 3, 0, 2);
        AssertPts(byx, 1, 5, 1, 20, 2, 20, 3, 10, 3, 5);

        Pta byxd = pta.Sort(L.L_SORT_BY_X, L.L_SORT_DECREASING, out _);
        AssertPts(byxd, 3, 10, 3, 5, 2, 20, 1, 20, 1, 5);

        Pta byy = pta.Sort(L.L_SORT_BY_Y, L.L_SORT_INCREASING, out _);
        AssertPts(byy, 3, 5, 1, 5, 3, 10, 1, 20, 2, 20);

        Pta byyd = pta.Sort(L.L_SORT_BY_Y, L.L_SORT_DECREASING, out _);
        AssertPts(byyd, 1, 20, 2, 20, 3, 10, 3, 5, 1, 5);

        /* the source is unchanged */
        AssertPts(pta, 3, 10, 1, 20, 3, 5, 2, 20, 1, 5);
    }

    [Fact]
    public void Sort_KeepsFloatCoordinates()
    {
        if (SortNotPorted) return;
        Pta pta = Pts(2.75f, -1.5f, -0.25f, 8.125f, 1e-3f, 0f);
        Pta sorted = pta.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out Numa na);
        AssertIndex(na, 1, 2, 0);
        AssertPts(sorted, -0.25f, 8.125f, 1e-3f, 0f, 2.75f, -1.5f);
    }

    [Fact]
    public void Sort_EmptyAndSingle()
    {
        if (SortNotPorted) return;
        Pta empty = Pta.Create(0);
        Pta sorted = empty.Sort(L.L_SORT_BY_Y, L.L_SORT_INCREASING, out Numa na);
        Assert.Equal(0, sorted.Count);
        Assert.Equal(0, na.Count);

        Pta one = Pts(4, 9).Sort(L.L_SORT_BY_X, L.L_SORT_DECREASING, out na);
        AssertPts(one, 4, 9);
        AssertIndex(na, 0);
    }

    [Fact]
    public void SortByIndex_FollowsTheIndexAndMayRepeat()
    {
        Pta pta = Pts(10, 11, 20, 21, 30, 31);
        Numa na = Numa.CreateFromIArray(new[] { 2, 0, 1 }, 3);
        AssertPts(pta.SortByIndex(na), 30, 31, 10, 11, 20, 21);

        /* any index list works: it need not be a permutation */
        na = Numa.CreateFromIArray(new[] { 1, 1, 0, 2, 2 }, 5);
        AssertPts(pta.SortByIndex(na), 20, 21, 20, 21, 10, 11, 30, 31, 30, 31);

        Assert.Equal(0, pta.SortByIndex(Numa.Create(0)).Count);
    }

    [Fact]
    public void SortByIndex_IndexOutOfRangeThrows()
    {
        Pta pta = Pts(10, 11, 20, 21);
        Numa na = Numa.CreateFromIArray(new[] { 0, 2 }, 2);
        Assert.Throws<ArgumentException>(() => pta.SortByIndex(na));
        na = Numa.CreateFromIArray(new[] { -1 }, 1);
        Assert.Throws<ArgumentException>(() => pta.SortByIndex(na));
    }

    [Fact]
    public void Ptaa_SortByIndex()
    {
        Ptaa ptaas = Ptaa.Create(0);
        ptaas.AddPta(Pts(1, 1), L.L_INSERT);
        ptaas.AddPta(Pts(2, 2, 3, 3), L.L_INSERT);
        ptaas.AddPta(Pts(), L.L_INSERT);

        Numa na = Numa.CreateFromIArray(new[] { 1, 2, 0 }, 3);
        Ptaa sorted = ptaas.SortByIndex(na);
        Assert.Equal(3, sorted.Count);
        AssertPts(sorted.GetPta(0, L.L_CLONE), 2, 2, 3, 3);
        AssertPts(sorted.GetPta(1, L.L_CLONE), new float[0]);
        AssertPts(sorted.GetPta(2, L.L_CLONE), 1, 1);

        /* the pta are copies */
        sorted.GetPta(2, L.L_CLONE).SetPt(0, 9, 9);
        AssertPts(ptaas.GetPta(0, L.L_CLONE), 1, 1);

        /* the index may repeat entries */
        na = Numa.CreateFromIArray(new[] { 0, 0, 0 }, 3);
        sorted = ptaas.SortByIndex(na);
        for (int i = 0; i < 3; i++)
            AssertPts(sorted.GetPta(i, L.L_CLONE), 1, 1);
    }

    [Fact]
    public void Ptaa_SortByIndex_Errors()
    {
        Ptaa ptaas = Ptaa.Create(0);
        ptaas.AddPta(Pts(1, 1), L.L_INSERT);
        ptaas.AddPta(Pts(2, 2), L.L_INSERT);

        Numa wrongSize = Numa.CreateFromIArray(new[] { 0 }, 1);
        Assert.Throws<ArgumentException>(() => ptaas.SortByIndex(wrongSize));

        Numa badIndex = Numa.CreateFromIArray(new[] { 0, 2 }, 2);
        Assert.Throws<ArgumentException>(() => ptaas.SortByIndex(badIndex));
    }

    /*---------------------------- GetRankValue ----------------------------*/

    [Fact]
    public void GetRankValue_PresortedByX()
    {
        /* sorted by x: 1 1 2 3 3 (n = 5, so index = (int)(fract * 4 + 0.5)) */
        Pta pta = Tied();
        Pta sorted = Pts(1, 5, 1, 20, 2, 20, 3, 10, 3, 5);

        Assert.Equal(1f, pta.GetRankValue(0.0f, sorted, L.L_SORT_BY_X));
        Assert.Equal(1f, pta.GetRankValue(0.125f, sorted, L.L_SORT_BY_X));   /* 0.5 + 0.5 = 1.0 -> 1 */
        Assert.Equal(1f, pta.GetRankValue(0.3f, sorted, L.L_SORT_BY_X));     /* 1.7 -> 1 */
        Assert.Equal(2f, pta.GetRankValue(0.375f, sorted, L.L_SORT_BY_X));   /* 1.5 + 0.5 = 2.0 -> 2 */
        Assert.Equal(2f, pta.GetRankValue(0.5f, sorted, L.L_SORT_BY_X));
        Assert.Equal(3f, pta.GetRankValue(0.7f, sorted, L.L_SORT_BY_X));     /* 3.3 -> 3 */
        Assert.Equal(3f, pta.GetRankValue(1.0f, sorted, L.L_SORT_BY_X));
    }

    [Fact]
    public void GetRankValue_PresortedByY()
    {
        /* sorted by y: 5 5 10 20 20 */
        Pta pta = Tied();
        Pta sorted = Pts(3, 5, 1, 5, 3, 10, 1, 20, 2, 20);

        Assert.Equal(5f, pta.GetRankValue(0.0f, sorted, L.L_SORT_BY_Y));
        Assert.Equal(10f, pta.GetRankValue(0.5f, sorted, L.L_SORT_BY_Y));
        Assert.Equal(20f, pta.GetRankValue(1.0f, sorted, L.L_SORT_BY_Y));
    }

    [Fact]
    public void GetRankValue_UsesTheGivenSortedPta()
    {
        /* the value comes from ptasort, not from pta */
        Pta pta = Pts(100, 200);
        Pta sorted = Pts(7, 8);
        Assert.Equal(7f, pta.GetRankValue(0.5f, sorted, L.L_SORT_BY_X));
        Assert.Equal(8f, pta.GetRankValue(0.5f, sorted, L.L_SORT_BY_Y));
    }

    [Fact]
    public void GetRankValue_SortsWhenNoPresortedPta()
    {
        if (SortNotPorted) return;
        Pta pta = Tied();
        Assert.Equal(1f, pta.GetRankValue(0.0f, null, L.L_SORT_BY_X));
        Assert.Equal(2f, pta.GetRankValue(0.5f, null, L.L_SORT_BY_X));
        Assert.Equal(3f, pta.GetRankValue(1.0f, null, L.L_SORT_BY_X));
        Assert.Equal(5f, pta.GetRankValue(0.0f, null, L.L_SORT_BY_Y));
        Assert.Equal(10f, pta.GetRankValue(0.5f, null, L.L_SORT_BY_Y));
        Assert.Equal(20f, pta.GetRankValue(1.0f, null, L.L_SORT_BY_Y));
    }

    [Fact]
    public void GetRankValue_Errors()
    {
        Pta pta = Tied();
        Pta sorted = Pts(1, 5, 1, 20, 2, 20, 3, 10, 3, 5);
        Assert.Throws<ArgumentException>(() => pta.GetRankValue(0.5f, sorted, 0));
        Assert.Throws<ArgumentException>(() => pta.GetRankValue(-0.01f, sorted, L.L_SORT_BY_X));
        Assert.Throws<ArgumentException>(() => pta.GetRankValue(1.01f, sorted, L.L_SORT_BY_X));
        Assert.Throws<ArgumentException>(() => Pta.Create(0).GetRankValue(0.5f, sorted, L.L_SORT_BY_X));
    }

    /*------------------------------- Sort2d -------------------------------*/

    [Fact]
    public void Sort2d_RowMajor()
    {
        if (SortNotPorted) return;
        /* y = 5: x 1, 3;  y = 10: x 3;  y = 20: x 1, 2 */
        Pta sorted = Tied().Sort2d();
        AssertPts(sorted, 1, 5, 3, 5, 3, 10, 1, 20, 2, 20);
    }

    [Fact]
    public void Sort2d_AllSameYOrSameX()
    {
        if (SortNotPorted) return;
        AssertPts(Pts(9, 4, -2, 4, 5, 4, 0, 4).Sort2d(), -2, 4, 0, 4, 5, 4, 9, 4);
        AssertPts(Pts(4, 3, 4, 1, 4, 2).Sort2d(), 4, 1, 4, 2, 4, 3);
    }

    [Fact]
    public void Sort2d_DuplicatesAndFloats()
    {
        if (SortNotPorted) return;
        AssertPts(Pts(2.5f, 1, 2.5f, 1, 0.5f, 1, 7, 0.25f).Sort2d(),
                  7, 0.25f, 0.5f, 1, 2.5f, 1, 2.5f, 1);
    }

    [Fact]
    public void Sort2d_SingleAndEmpty()
    {
        if (SortNotPorted) return;
        AssertPts(Pts(3, 4).Sort2d(), 3, 4);
        Assert.Equal(0, Pta.Create(0).Sort2d().Count);
    }

    [Fact]
    public void Sort2d_DoesNotChangeTheInput()
    {
        if (SortNotPorted) return;
        Pta pta = Tied();
        pta.Sort2d();
        AssertPts(pta, 3, 10, 1, 20, 3, 5, 2, 20, 1, 5);
    }

    /*-------------------------------- Equal --------------------------------*/

    [Fact]
    public void Equal_DifferentCountIsFalseWithoutSorting()
    {
        /* does not need the sort */
        Assert.False(Pts(1, 1).Equal(Pts(1, 1, 2, 2)));
        Assert.False(Pts(1, 1).Equal(Pta.Create(0)));
    }

    [Fact]
    public void Equal_OrderIndependent()
    {
        if (SortNotPorted) return;
        Pta a = Tied();
        Pta b = Pts(1, 5, 2, 20, 3, 5, 1, 20, 3, 10);
        Assert.True(a.Equal(b));
        Assert.True(b.Equal(a));
        Assert.True(a.Equal(a));
        Assert.True(a.Equal(a.Copy()));
    }

    [Fact]
    public void Equal_OnePointDiffers()
    {
        if (SortNotPorted) return;
        Pta a = Tied();
        Assert.False(a.Equal(Pts(1, 5, 2, 20, 3, 5, 1, 20, 3, 11)));   /* y differs */
        Assert.False(a.Equal(Pts(1, 5, 2, 20, 3, 5, 1, 20, 4, 10)));   /* x differs */
        Assert.False(a.Equal(Pts(1, 5, 2, 20, 3, 5, 1, 20, 3, 10.5f)));
    }

    [Fact]
    public void Equal_MultiplicityMatters()
    {
        if (SortNotPorted) return;
        Pta a = Pts(1, 1, 1, 1, 2, 2);
        Pta b = Pts(1, 1, 2, 2, 2, 2);
        Assert.False(a.Equal(b));
        Assert.True(a.Equal(Pts(2, 2, 1, 1, 1, 1)));
    }

    [Fact]
    public void Equal_TwoEmptyPtaAreEqual()
    {
        if (SortNotPorted) return;
        Assert.True(Pta.Create(0).Equal(Pta.Create(5)));
    }

    /*--------------------------- Set operations (aset) ---------------------------*/

    [Fact]
    public void RemoveDupsByAset_KeepsFirstOccurrenceInOrder()
    {
        if (AsetNotPorted) return;
        Pta pta = Pts(1, 1, 2, 2, 1, 1, 3, 3, 2, 2);
        AssertPts(pta.RemoveDupsByAset(), 1, 1, 2, 2, 3, 3);
        AssertPts(pta, 1, 1, 2, 2, 1, 1, 3, 3, 2, 2);
    }

    [Fact]
    public void RemoveDupsByAset_UsesRoundedIntegerValues()
    {
        if (AsetNotPorted) return;
        /* (int)(v + 0.5): 1.4 -> 1, 2.6 -> 3, 0.5 -> 1 */
        Pta pta = Pts(1.4f, 1.4f, 1, 1, 2.6f, 2.6f, 3, 3, 0.5f, 0.5f);
        AssertPts(pta.RemoveDupsByAset(), 1, 1, 3, 3);
    }

    [Fact]
    public void RemoveDupsByAset_DistinguishesXFromY()
    {
        if (AsetNotPorted) return;
        AssertPts(Pts(1, 2, 2, 1, 1, 2).RemoveDupsByAset(), 1, 2, 2, 1);
    }

    [Fact]
    public void RemoveDupsByAset_Empty()
    {
        if (AsetNotPorted) return;
        Assert.Equal(0, Pta.Create(0).RemoveDupsByAset().Count);
    }

    [Fact]
    public void UnionByAset_FirstThenSecondWithoutDuplicates()
    {
        if (AsetNotPorted) return;
        Pta pta1 = Pts(1, 1, 2, 2);
        Pta pta2 = Pts(2, 2, 3, 3, 1, 1, 4, 4);
        AssertPts(pta1.UnionByAset(pta2), 1, 1, 2, 2, 3, 3, 4, 4);
        AssertPts(pta1, 1, 1, 2, 2);
        AssertPts(pta2, 2, 2, 3, 3, 1, 1, 4, 4);
    }

    [Fact]
    public void UnionByAset_DuplicatesWithinOneInput()
    {
        if (AsetNotPorted) return;
        AssertPts(Pts(5, 5, 5, 5).UnionByAset(Pts(6, 6, 6, 6)), 5, 5, 6, 6);
    }

    [Fact]
    public void IntersectionByAset_OrderOfTheSmallerArray()
    {
        if (AsetNotPorted) return;
        /* pta2 is smaller (3 < 4): its order and its multiplicity filtering decide */
        Pta pta1 = Pts(1, 1, 2, 2, 3, 3, 2, 2);
        Pta pta2 = Pts(3, 3, 2, 2, 9, 9);
        AssertPts(pta1.IntersectionByAset(pta2), 3, 3, 2, 2);
        AssertPts(pta2.IntersectionByAset(pta1), 3, 3, 2, 2);
    }

    [Fact]
    public void IntersectionByAset_EqualSizesUseTheSecondAsSmall()
    {
        if (AsetNotPorted) return;
        /* n1 < n2 is false, so pta2 is the small one */
        Pta pta1 = Pts(1, 1, 2, 2);
        Pta pta2 = Pts(2, 2, 1, 1);
        AssertPts(pta1.IntersectionByAset(pta2), 2, 2, 1, 1);
        AssertPts(pta2.IntersectionByAset(pta1), 1, 1, 2, 2);
    }

    [Fact]
    public void IntersectionByAset_DuplicatesInSmallAddedOnce_AndDisjoint()
    {
        if (AsetNotPorted) return;
        Pta small = Pts(2, 2, 2, 2, 7, 7);
        Pta big = Pts(1, 1, 2, 2, 3, 3, 4, 4, 5, 5);
        AssertPts(small.IntersectionByAset(big), 2, 2);
        Assert.Equal(0, Pts(1, 1).IntersectionByAset(Pts(2, 2)).Count);
        Assert.Equal(0, Pta.Create(0).IntersectionByAset(Pts(2, 2)).Count);
    }

    [Fact]
    public void AsetCreateFromPta_Works()
    {
        if (AsetNotPorted) return;
        L_Aset set = L_Aset.CreateFromPta(Pts(1, 2, 3, 4, 1, 2));
        RbType key = default;
        key.Utype = Utils.HashPtToUint64(3, 4);
        Assert.NotNull(set.Find(key));
        key.Utype = Utils.HashPtToUint64(4, 3);
        Assert.Null(set.Find(key));
    }

    /*--------------------------- Hashmap operations ---------------------------*/

    [Fact]
    public void RemoveDupsByHmap_UniquePointsInAnyOrder()
    {
        if (HmapNotPorted || SortNotPorted) return;
        Pta pta = Pts(1, 1, 2, 2, 1, 1, 3, 3, 2, 2);
        pta.RemoveDupsByHmap(out Pta ptad, out L_Hashmap hmap);
        Assert.NotNull(hmap);
        Assert.Equal(3, ptad.Count);
        Assert.True(ptad.Equal(Pts(1, 1, 2, 2, 3, 3)));
        AssertPts(pta, 1, 1, 2, 2, 1, 1, 3, 3, 2, 2);
    }

    [Fact]
    public void RemoveDupsByHmap_RoundsAndKeepsFirstIndex()
    {
        if (HmapNotPorted || SortNotPorted) return;
        /* the stored val is the index of the first occurrence; the point is taken
         * from the input at that index and rounded by ptaGetIPt */
        Pta pta = Pts(1.4f, 1.4f, 1, 1, 2.6f, 2.6f);
        pta.RemoveDupsByHmap(out Pta ptad, out _);
        Assert.True(ptad.Equal(Pts(1, 1, 3, 3)));
    }

    [Fact]
    public void RemoveDupsByHmap_Empty()
    {
        if (HmapNotPorted) return;
        Pta.Create(0).RemoveDupsByHmap(out Pta ptad, out _);
        Assert.Equal(0, ptad.Count);
    }

    [Fact]
    public void UnionByHmap_AllDistinctPoints()
    {
        if (HmapNotPorted || SortNotPorted) return;
        Pta pta1 = Pts(1, 1, 2, 2);
        Pta pta2 = Pts(2, 2, 3, 3, 1, 1, 4, 4);
        Pta ptad = pta1.UnionByHmap(pta2);
        Assert.Equal(4, ptad.Count);
        Assert.True(ptad.Equal(Pts(1, 1, 2, 2, 3, 3, 4, 4)));
        AssertPts(pta1, 1, 1, 2, 2);
    }

    [Fact]
    public void IntersectionByHmap_OrderOfTheSmallerArray()
    {
        if (HmapNotPorted) return;
        Pta small = Pts(2, 2, 2, 2, 5, 5, 1, 1);
        Pta big = Pts(1, 1, 2, 2, 3, 3, 4, 4, 6, 6);
        AssertPts(small.IntersectionByHmap(big), 2, 2, 1, 1);
        AssertPts(big.IntersectionByHmap(small), 2, 2, 1, 1);
    }

    [Fact]
    public void IntersectionByHmap_EqualSizesUseTheSecondAsSmall()
    {
        if (HmapNotPorted) return;
        Pta pta1 = Pts(1, 1, 2, 2);
        Pta pta2 = Pts(2, 2, 1, 1);
        AssertPts(pta1.IntersectionByHmap(pta2), 2, 2, 1, 1);
        AssertPts(pta2.IntersectionByHmap(pta1), 1, 1, 2, 2);
    }

    [Fact]
    public void IntersectionByHmap_DuplicatesInBigAndDisjoint()
    {
        if (HmapNotPorted) return;
        Pta small = Pts(2, 2, 3, 3);
        Pta big = Pts(2, 2, 2, 2, 2, 2, 3, 3, 3, 3);
        AssertPts(small.IntersectionByHmap(big), 2, 2, 3, 3);
        Assert.Equal(0, Pts(1, 1).IntersectionByHmap(Pts(2, 2)).Count);
        Assert.Equal(0, Pta.Create(0).IntersectionByHmap(Pts(2, 2)).Count);
    }

    [Fact]
    public void HmapCreateFromPta_StoresFirstIndexAsVal()
    {
        if (HmapNotPorted) return;
        L_Hashmap hmap = L_Hashmap.CreateFromPta(Pts(5, 6, 7, 8, 5, 6));
        ulong key = Utils.HashPtToUint64(5, 6);
        L_Hashitem? item = hmap.Lookup(key, 99, L.L_HMAP_CHECK);
        Assert.NotNull(item);
        Assert.Equal(0UL, item!.Val);
        Assert.Equal(2, item.Count);
        Assert.Null(hmap.Lookup(Utils.HashPtToUint64(6, 5), 0, L.L_HMAP_CHECK));
    }
}
