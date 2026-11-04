using System;
using Leptonica;

namespace Leptonica.Managed.Tests;

/// <summary>
/// Tests for boxfunc4.c with hand-computed expected values.  Callees in files that are
/// not ported yet (numaPseudorandomSequence, boxaSortByIndex, ptaJoin, ...) are tested only
/// up to the point where they are reached.
/// </summary>
public class BoxFunc4Tests
{
    private static Boxa MakeBoxa(params (int x, int y, int w, int h)[] boxes)
    {
        Boxa boxa = Boxa.Create(0);
        foreach (var b in boxes)
            boxa.AddBox(Box.Create(b.x, b.y, b.w, b.h), L.L_INSERT);
        return boxa;
    }

    private static (int, int, int, int) G(Box box)
    {
        box.GetGeometry(out int x, out int y, out int w, out int h);
        return (x, y, w, h);
    }

    private static (int, int, int, int) G(Boxa boxa, int i) => G(boxa.GetBox(i, L.L_CLONE));

    private static int[] Ints(Numa na)
    {
        int[] a = new int[na.Count];
        for (int i = 0; i < a.Length; i++)
            a[i] = na.GetIValue(i);
        return a;
    }

    /// <summary>
    /// Four boxes: sizes 10x20, 30x10, 10x10, 50x50;
    /// areas 200, 300, 100, 2500; w/h ratios 0.5, 3, 1, 1.
    /// </summary>
    private static Boxa Four() => MakeBoxa((0, 0, 10, 20), (5, 5, 30, 10), (2, 3, 10, 10), (7, 7, 50, 50));

    /*------------------------------ boxaSelectRange -----------------------------*/

    [Fact]
    public void SelectRange_MiddleAndToEnd()
    {
        Boxa boxa = Four();
        Boxa r = boxa.SelectRange(1, 2, L.L_COPY);
        Assert.Equal(2, r.Count);
        Assert.Equal((5, 5, 30, 10), G(r, 0));
        Assert.Equal((2, 3, 10, 10), G(r, 1));

        r = boxa.SelectRange(2, -1, L.L_COPY);
        Assert.Equal(2, r.Count);
        Assert.Equal((2, 3, 10, 10), G(r, 0));
        Assert.Equal((7, 7, 50, 50), G(r, 1));

        r = boxa.SelectRange(0, -1, L.L_COPY);
        Assert.Equal(4, r.Count);
        r = boxa.SelectRange(3, 3, L.L_COPY);
        Assert.Equal(1, r.Count);
        Assert.Equal((7, 7, 50, 50), G(r, 0));
    }

    [Fact]
    public void SelectRange_ClampsFirstAndLast()
    {
        Boxa boxa = Four();
        Assert.Equal(4, boxa.SelectRange(-5, 100, L.L_COPY).Count);   // first -> 0, last -> 3
        Assert.Equal(2, boxa.SelectRange(2, 100, L.L_COPY).Count);
    }

    [Fact]
    public void SelectRange_CopyAndCloneSemantics()
    {
        Boxa boxa = Four();
        Boxa cl = boxa.SelectRange(1, 1, L.L_CLONE);
        Assert.Same(boxa.GetBox(1, L.L_CLONE), cl.GetBox(0, L.L_CLONE));
        Boxa cp = boxa.SelectRange(1, 1, L.L_COPY);
        Assert.NotSame(boxa.GetBox(1, L.L_CLONE), cp.GetBox(0, L.L_CLONE));
    }

    [Fact]
    public void SelectRange_Errors()
    {
        Boxa boxa = Four();
        Assert.Throws<ArgumentException>(() => boxa.SelectRange(0, -1, L.L_INSERT));
        Assert.Throws<ArgumentException>(() => boxa.SelectRange(0, -1, 7));
        Assert.Throws<ArgumentException>(() => boxa.SelectRange(4, -1, L.L_COPY));   // first >= n
        Assert.Throws<ArgumentException>(() => boxa.SelectRange(3, 1, L.L_COPY));    // first > last
    }

    [Fact]
    public void SelectRange_EmptyReturnsCopy()
    {
        Boxa r = Boxa.Create(0).SelectRange(0, -1, L.L_COPY);
        Assert.Equal(0, r.Count);
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).SelectRange(0, -1, 5));
    }

    [Fact]
    public void BoxaaSelectRange_RangeClampAndErrors()
    {
        Boxaa baa = Boxaa.Create(0);
        baa.AddBoxa(MakeBoxa((0, 0, 1, 1)), L.L_INSERT);
        baa.AddBoxa(MakeBoxa((0, 0, 2, 2), (0, 0, 3, 3)), L.L_INSERT);
        baa.AddBoxa(MakeBoxa((0, 0, 4, 4)), L.L_INSERT);

        Boxaa r = baa.SelectRange(1, 2, L.L_COPY);
        Assert.Equal(2, r.Count);
        Assert.Equal(2, r.GetBoxa(0, L.L_CLONE).Count);
        Assert.Equal((0, 0, 4, 4), G(r.GetBoxa(1, L.L_CLONE), 0));

        Assert.Equal(3, baa.SelectRange(0, -1, L.L_COPY).Count);
        Assert.Equal(2, baa.SelectRange(1, 99, L.L_COPY).Count);     // last adjusted
        Assert.Equal(3, baa.SelectRange(-3, 2, L.L_COPY).Count);     // first -> 0

        Boxaa cl = baa.SelectRange(0, 0, L.L_CLONE);
        Assert.Same(baa.GetBoxa(0, L.L_CLONE), cl.GetBoxa(0, L.L_CLONE));

        Assert.Throws<ArgumentException>(() => baa.SelectRange(0, -1, L.L_INSERT));
        Assert.Throws<ArgumentException>(() => baa.SelectRange(3, -1, L.L_COPY));
        Assert.Throws<ArgumentException>(() => baa.SelectRange(2, 0, L.L_COPY));
        Assert.Throws<ArgumentException>(() => Boxaa.Create(0).SelectRange(0, -1, L.L_COPY));   // empty
    }

    /*------------------------- size / area / ratio selection --------------------*/

    [Fact]
    public void MakeSizeIndicator_Width()
    {
        Boxa boxa = Four();   // widths 10, 30, 10, 50
        Assert.Equal(new[] { 0, 0, 0, 0 }, Ints(boxa.MakeSizeIndicator(10, 0, L.L_SELECT_WIDTH, L.L_SELECT_IF_LT)));
        Assert.Equal(new[] { 1, 0, 1, 0 }, Ints(boxa.MakeSizeIndicator(10, 0, L.L_SELECT_WIDTH, L.L_SELECT_IF_LTE)));
        Assert.Equal(new[] { 0, 1, 0, 1 }, Ints(boxa.MakeSizeIndicator(10, 0, L.L_SELECT_WIDTH, L.L_SELECT_IF_GT)));
        Assert.Equal(new[] { 1, 1, 1, 1 }, Ints(boxa.MakeSizeIndicator(10, 0, L.L_SELECT_WIDTH, L.L_SELECT_IF_GTE)));
    }

    [Fact]
    public void MakeSizeIndicator_Height()
    {
        Boxa boxa = Four();   // heights 20, 10, 10, 50
        Assert.Equal(new[] { 0, 1, 1, 0 }, Ints(boxa.MakeSizeIndicator(0, 15, L.L_SELECT_HEIGHT, L.L_SELECT_IF_LT)));
        Assert.Equal(new[] { 1, 0, 0, 1 }, Ints(boxa.MakeSizeIndicator(0, 15, L.L_SELECT_HEIGHT, L.L_SELECT_IF_GTE)));
        Assert.Equal(new[] { 1, 1, 1, 0 }, Ints(boxa.MakeSizeIndicator(0, 20, L.L_SELECT_HEIGHT, L.L_SELECT_IF_LTE)));
        Assert.Equal(new[] { 0, 0, 0, 1 }, Ints(boxa.MakeSizeIndicator(0, 20, L.L_SELECT_HEIGHT, L.L_SELECT_IF_GT)));
    }

    [Fact]
    public void MakeSizeIndicator_EitherAndBoth()
    {
        Boxa boxa = Four();   // (w,h): (10,20) (30,10) (10,10) (50,50)
        Assert.Equal(new[] { 1, 1, 1, 0 }, Ints(boxa.MakeSizeIndicator(20, 15, L.L_SELECT_IF_EITHER, L.L_SELECT_IF_LT)));
        Assert.Equal(new[] { 0, 0, 1, 0 }, Ints(boxa.MakeSizeIndicator(20, 15, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_LT)));
        Assert.Equal(new[] { 0, 0, 0, 1 }, Ints(boxa.MakeSizeIndicator(20, 15, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_GT)));
        Assert.Equal(new[] { 1, 1, 0, 1 }, Ints(boxa.MakeSizeIndicator(20, 15, L.L_SELECT_IF_EITHER, L.L_SELECT_IF_GT)));
        Assert.Equal(new[] { 0, 0, 1, 0 }, Ints(boxa.MakeSizeIndicator(10, 10, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_LTE)));
        Assert.Equal(new[] { 0, 1, 0, 1 }, Ints(boxa.MakeSizeIndicator(30, 50, L.L_SELECT_IF_EITHER, L.L_SELECT_IF_GTE)));
        Assert.Equal(new[] { 0, 0, 0, 1 }, Ints(boxa.MakeSizeIndicator(30, 50, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_GTE)));
    }

    [Fact]
    public void MakeSizeIndicator_Errors()
    {
        Boxa boxa = Four();
        Assert.Throws<ArgumentException>(() => boxa.MakeSizeIndicator(1, 1, 0, L.L_SELECT_IF_LT));
        Assert.Throws<ArgumentException>(() => boxa.MakeSizeIndicator(1, 1, L.L_SELECT_XVAL, L.L_SELECT_IF_LT));
        Assert.Throws<ArgumentException>(() => boxa.MakeSizeIndicator(1, 1, L.L_SELECT_WIDTH, 0));
        Assert.Throws<ArgumentException>(() => boxa.MakeSizeIndicator(1, 1, L.L_SELECT_WIDTH, 5));
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).MakeSizeIndicator(1, 1, L.L_SELECT_WIDTH, L.L_SELECT_IF_LT));
    }

    [Fact]
    public void SelectBySize_FiltersAndReportsChanged()
    {
        Boxa boxa = Four();
        Boxa r = boxa.SelectBySize(10, 0, L.L_SELECT_WIDTH, L.L_SELECT_IF_LTE, out bool changed);
        Assert.True(changed);
        Assert.Equal(2, r.Count);
        Assert.Equal((0, 0, 10, 20), G(r, 0));
        Assert.Equal((2, 3, 10, 10), G(r, 1));
        Assert.Equal(4, boxa.Count);   // input unchanged
    }

    [Fact]
    public void SelectBySize_NothingRemovedReturnsCopy()
    {
        Boxa boxa = Four();
        Boxa r = boxa.SelectBySize(10, 0, L.L_SELECT_WIDTH, L.L_SELECT_IF_GTE, out bool changed);
        Assert.False(changed);
        Assert.Equal(4, r.Count);
        Assert.NotSame(boxa.GetBox(0, L.L_CLONE), r.GetBox(0, L.L_CLONE));   // L_COPY
    }

    [Fact]
    public void SelectBySize_AllRemovedAndEmptyAndErrors()
    {
        Boxa boxa = Four();
        Boxa r = boxa.SelectBySize(10, 0, L.L_SELECT_WIDTH, L.L_SELECT_IF_LT, out bool changed);
        Assert.True(changed);
        Assert.Equal(0, r.Count);

        r = Boxa.Create(0).SelectBySize(10, 10, L.L_SELECT_WIDTH, L.L_SELECT_IF_LT, out changed);
        Assert.False(changed);
        Assert.Equal(0, r.Count);

        // an empty boxa is returned before the type and relation are checked
        r = Boxa.Create(0).SelectBySize(10, 10, 99, 99, out changed);
        Assert.Equal(0, r.Count);

        Assert.Throws<ArgumentException>(() => boxa.SelectBySize(1, 1, 0, L.L_SELECT_IF_LT, out _));
        Assert.Throws<ArgumentException>(() => boxa.SelectBySize(1, 1, L.L_SELECT_WIDTH, 0, out _));
    }

    [Fact]
    public void MakeAreaIndicator_AllRelations()
    {
        Boxa boxa = Four();   // areas 200, 300, 100, 2500
        Assert.Equal(new[] { 0, 0, 1, 0 }, Ints(boxa.MakeAreaIndicator(200, L.L_SELECT_IF_LT)));
        Assert.Equal(new[] { 1, 0, 1, 0 }, Ints(boxa.MakeAreaIndicator(200, L.L_SELECT_IF_LTE)));
        Assert.Equal(new[] { 0, 0, 0, 1 }, Ints(boxa.MakeAreaIndicator(300, L.L_SELECT_IF_GT)));
        Assert.Equal(new[] { 0, 1, 0, 1 }, Ints(boxa.MakeAreaIndicator(300, L.L_SELECT_IF_GTE)));
    }

    [Fact]
    public void MakeAreaIndicator_ProductWrapsLikeC()
    {
        // 100000 * 100000 = 10^10 = 2 * 2^32 + 1410065408
        Boxa boxa = MakeBoxa((0, 0, 100000, 100000));
        Assert.Equal(new[] { 1 }, Ints(boxa.MakeAreaIndicator(1410065408, L.L_SELECT_IF_GTE)));
        Assert.Equal(new[] { 0 }, Ints(boxa.MakeAreaIndicator(1410065408, L.L_SELECT_IF_GT)));
    }

    [Fact]
    public void MakeAreaIndicator_Errors()
    {
        Assert.Throws<ArgumentException>(() => Four().MakeAreaIndicator(1, 0));
        Assert.Throws<ArgumentException>(() => Four().MakeAreaIndicator(1, 5));
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).MakeAreaIndicator(1, L.L_SELECT_IF_LT));
    }

    [Fact]
    public void SelectByArea_FiltersAndErrors()
    {
        Boxa boxa = Four();
        Boxa r = boxa.SelectByArea(150, L.L_SELECT_IF_GT, out bool changed);
        Assert.True(changed);
        Assert.Equal(3, r.Count);
        Assert.Equal((0, 0, 10, 20), G(r, 0));
        Assert.Equal((5, 5, 30, 10), G(r, 1));
        Assert.Equal((7, 7, 50, 50), G(r, 2));

        r = boxa.SelectByArea(0, L.L_SELECT_IF_GTE, out changed);
        Assert.False(changed);
        Assert.Equal(4, r.Count);

        r = Boxa.Create(0).SelectByArea(1, 99, out changed);   // empty: returned before the relation check
        Assert.Equal(0, r.Count);
        Assert.Throws<ArgumentException>(() => boxa.SelectByArea(1, 0, out _));
    }

    [Fact]
    public void MakeWHRatioIndicator_AllRelations()
    {
        Boxa boxa = Four();   // ratios 0.5, 3, 1, 1
        Assert.Equal(new[] { 1, 0, 0, 0 }, Ints(boxa.MakeWHRatioIndicator(1.0f, L.L_SELECT_IF_LT)));
        Assert.Equal(new[] { 1, 0, 1, 1 }, Ints(boxa.MakeWHRatioIndicator(1.0f, L.L_SELECT_IF_LTE)));
        Assert.Equal(new[] { 0, 1, 0, 0 }, Ints(boxa.MakeWHRatioIndicator(1.0f, L.L_SELECT_IF_GT)));
        Assert.Equal(new[] { 0, 1, 1, 1 }, Ints(boxa.MakeWHRatioIndicator(1.0f, L.L_SELECT_IF_GTE)));
    }

    [Fact]
    public void MakeWHRatioIndicator_UsesSinglePrecision()
    {
        // (float)1 / (float)3 equals 1.0f / 3.0f exactly
        Boxa boxa = MakeBoxa((0, 0, 1, 3));
        float third = 1.0f / 3.0f;
        Assert.Equal(new[] { 0 }, Ints(boxa.MakeWHRatioIndicator(third, L.L_SELECT_IF_LT)));
        Assert.Equal(new[] { 1 }, Ints(boxa.MakeWHRatioIndicator(third, L.L_SELECT_IF_LTE)));
        Assert.Equal(new[] { 1 }, Ints(boxa.MakeWHRatioIndicator(third, L.L_SELECT_IF_GTE)));
        Assert.Equal(new[] { 0 }, Ints(boxa.MakeWHRatioIndicator(third, L.L_SELECT_IF_GT)));
    }

    [Fact]
    public void MakeWHRatioIndicator_ZeroHeightIsInfinityAndZeroByZeroIsNaN()
    {
        Boxa boxa = MakeBoxa((0, 0, 5, 0), (0, 0, 0, 0));
        // 5/0 = +inf; 0/0 = NaN, for which every comparison is false
        Assert.Equal(new[] { 1, 0 }, Ints(boxa.MakeWHRatioIndicator(1.0f, L.L_SELECT_IF_GT)));
        Assert.Equal(new[] { 1, 0 }, Ints(boxa.MakeWHRatioIndicator(1.0f, L.L_SELECT_IF_GTE)));
        Assert.Equal(new[] { 0, 0 }, Ints(boxa.MakeWHRatioIndicator(1.0f, L.L_SELECT_IF_LT)));
        Assert.Equal(new[] { 0, 0 }, Ints(boxa.MakeWHRatioIndicator(1.0f, L.L_SELECT_IF_LTE)));
    }

    [Fact]
    public void MakeWHRatioIndicator_Errors()
    {
        Assert.Throws<ArgumentException>(() => Four().MakeWHRatioIndicator(1f, 0));
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).MakeWHRatioIndicator(1f, L.L_SELECT_IF_LT));
    }

    [Fact]
    public void SelectByWHRatio_FiltersAndErrors()
    {
        Boxa boxa = Four();
        Boxa r = boxa.SelectByWHRatio(1.0f, L.L_SELECT_IF_GT, out bool changed);   // wide boxes
        Assert.True(changed);
        Assert.Equal(1, r.Count);
        Assert.Equal((5, 5, 30, 10), G(r, 0));

        r = boxa.SelectByWHRatio(1.0f, L.L_SELECT_IF_LTE, out changed);
        Assert.True(changed);
        Assert.Equal(3, r.Count);

        r = boxa.SelectByWHRatio(0.0f, L.L_SELECT_IF_GT, out changed);
        Assert.False(changed);
        Assert.Equal(4, r.Count);

        r = Boxa.Create(0).SelectByWHRatio(1f, 99, out changed);
        Assert.Equal(0, r.Count);
        Assert.Throws<ArgumentException>(() => boxa.SelectByWHRatio(1f, 0, out _));
    }

    [Fact]
    public void SelectWithIndicator_SelectsOnes()
    {
        Boxa boxa = Four();
        Numa na = Numa.Create(4);
        foreach (int v in new[] { 1, 0, 1, 1 })
            na.AddNumber(v);
        Boxa r = boxa.SelectWithIndicator(na, out bool changed);
        Assert.True(changed);
        Assert.Equal(3, r.Count);
        Assert.Equal((0, 0, 10, 20), G(r, 0));
        Assert.Equal((2, 3, 10, 10), G(r, 1));
        Assert.Equal((7, 7, 50, 50), G(r, 2));
        Assert.NotSame(boxa.GetBox(0, L.L_CLONE), r.GetBox(0, L.L_CLONE));   // box copies
    }

    [Fact]
    public void SelectWithIndicator_AllOnesGivesCopyAllZerosGivesEmpty()
    {
        Boxa boxa = Four();
        Numa ones = Numa.Create(4);
        Numa zeros = Numa.Create(4);
        for (int i = 0; i < 4; i++) {
            ones.AddNumber(1);
            zeros.AddNumber(0);
        }
        Boxa r = boxa.SelectWithIndicator(ones, out bool changed);
        Assert.False(changed);
        Assert.Equal(4, r.Count);
        r = boxa.SelectWithIndicator(zeros, out changed);
        Assert.True(changed);
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void SelectWithIndicator_ValuesOtherThanZeroAndOneAreKept()
    {
        // Only 1 is counted in nsave, but any non-zero value selects the box.
        Boxa boxa = MakeBoxa((0, 0, 1, 1), (0, 0, 2, 2));
        Numa na = Numa.Create(2);
        na.AddNumber(1);
        na.AddNumber(2);
        Boxa r = boxa.SelectWithIndicator(na, out bool changed);
        Assert.True(changed);   // nsave (1) != n (2)
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void SelectWithIndicator_ShortAndLongIndicator()
    {
        Boxa boxa = Four();
        Numa shortNa = Numa.Create(1);
        shortNa.AddNumber(1);
        // nsave == n == 1, so the whole boxa is copied
        Boxa r = boxa.SelectWithIndicator(shortNa, out bool changed);
        Assert.False(changed);
        Assert.Equal(4, r.Count);

        Numa longNa = Numa.Create(5);
        for (int i = 0; i < 5; i++)
            longNa.AddNumber(i == 4 ? 1 : 0);
        Assert.Throws<ArgumentException>(() => boxa.SelectWithIndicator(longNa, out _));   // no box 4
    }

    /*---------------------------------- permutation ----------------------------*/

    [Fact]
    public void CRand_MatchesMsvcSequenceForSeed1()
    {
        CRand.Seed(1);
        Assert.Equal(new[] { 41, 18467, 6334, 26500, 19169 },
                     new[] { CRand.Next(), CRand.Next(), CRand.Next(), CRand.Next(), CRand.Next() });
    }

    [Fact]
    public void PermuteRandom_FollowsTheCRandSequence()
    {
        // rand() with seed 1: 41, 18467, 6334, 26500.  With n = 4:
        //   index = 41 % 4 = 1                       swap(0, 1): B A C D
        //   i = 1: 18467 % 4 = 3                     swap(1, 3): B D C A
        //   i = 2: 6334 % 4 = 2 == i -> index = 1    swap(2, 1): B C D A
        //   i = 3: 26500 % 4 = 0                     swap(3, 0): A C D B
        Boxa boxa = MakeBoxa((0, 0, 1, 1), (0, 0, 2, 2), (0, 0, 3, 3), (0, 0, 4, 4));
        CRand.Seed(1);
        Boxa r = Boxa.PermuteRandom(null, boxa);
        Assert.Equal(new[] { 1, 3, 4, 2 }, new[] { G(r, 0).Item3, G(r, 1).Item3, G(r, 2).Item3, G(r, 3).Item3 });
        Assert.Equal(1, G(boxa, 0).Item3);   // input unchanged when boxad is null
        Assert.Equal(2, G(boxa, 1).Item3);
    }

    [Fact]
    public void PermuteRandom_InPlaceReturnsTheSameBoxa()
    {
        Boxa boxa = MakeBoxa((0, 0, 1, 1), (0, 0, 2, 2), (0, 0, 3, 3), (0, 0, 4, 4));
        CRand.Seed(1);
        Boxa r = Boxa.PermuteRandom(boxa, boxa);
        Assert.Same(boxa, r);
        Assert.Equal(new[] { 1, 3, 4, 2 }, new[] { G(r, 0).Item3, G(r, 1).Item3, G(r, 2).Item3, G(r, 3).Item3 });
    }

    [Fact]
    public void PermuteRandom_TwoBoxesAreSwapped()
    {
        // n = 2: index = 41 % 2 = 1 -> swap(0, 1); i = 1: 18467 % 2 = 1 == i -> index 0 -> swap(1, 0)
        // The two swaps cancel each other.
        Boxa boxa = MakeBoxa((0, 0, 1, 1), (0, 0, 2, 2));
        CRand.Seed(1);
        Boxa r = Boxa.PermuteRandom(null, boxa);
        Assert.Equal(1, G(r, 0).Item3);
        Assert.Equal(2, G(r, 1).Item3);
    }

    [Fact]
    public void PermuteRandom_SingleBoxAndEmptyAndErrors()
    {
        // n = 1: swap(0, 1) fails in C and the error is ignored
        Boxa one = MakeBoxa((3, 4, 5, 6));
        Boxa r = Boxa.PermuteRandom(null, one);
        Assert.Equal(1, r.Count);
        Assert.Equal((3, 4, 5, 6), G(r, 0));

        Assert.Equal(0, Boxa.PermuteRandom(null, Boxa.Create(0)).Count);

        Boxa other = MakeBoxa((0, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => Boxa.PermuteRandom(other, one));
    }

    [Fact]
    public void SwapBoxes_SwapsAndValidates()
    {
        Boxa boxa = Four();
        boxa.SwapBoxes(0, 3);
        Assert.Equal((7, 7, 50, 50), G(boxa, 0));
        Assert.Equal((0, 0, 10, 20), G(boxa, 3));
        Assert.Equal((5, 5, 30, 10), G(boxa, 1));   // untouched
        boxa.SwapBoxes(3, 0);                       // order of i, j does not matter
        Assert.Equal((0, 0, 10, 20), G(boxa, 0));

        Assert.Throws<ArgumentException>(() => boxa.SwapBoxes(-1, 1));
        Assert.Throws<ArgumentException>(() => boxa.SwapBoxes(4, 1));
        Assert.Throws<ArgumentException>(() => boxa.SwapBoxes(1, -1));
        Assert.Throws<ArgumentException>(() => boxa.SwapBoxes(1, 4));
        Assert.Throws<ArgumentException>(() => boxa.SwapBoxes(2, 2));
    }

    /*---------------------------------- conversions ----------------------------*/

    private static (int, int) P(Pta pta, int i)
    {
        pta.GetIPt(i, out int x, out int y);
        return (x, y);
    }

    private static Pta MakePta(params (int x, int y)[] pts)
    {
        Pta pta = Pta.Create(0);
        foreach (var p in pts)
            pta.AddPt(p.x, p.y);
        return pta;
    }

    [Fact]
    public void BoxConvertToPta_TwoAndFourCorners()
    {
        Box box = Box.Create(3, 4, 5, 6);   // x in [3, 7], y in [4, 9]
        Pta p2 = box.ConvertToPta(2);
        Assert.Equal(2, p2.Count);
        Assert.Equal((3, 4), P(p2, 0));
        Assert.Equal((7, 9), P(p2, 1));

        Pta p4 = box.ConvertToPta(4);   // UL, UR, LL, LR
        Assert.Equal(4, p4.Count);
        Assert.Equal((3, 4), P(p4, 0));
        Assert.Equal((7, 4), P(p4, 1));
        Assert.Equal((3, 9), P(p4, 2));
        Assert.Equal((7, 9), P(p4, 3));
    }

    [Fact]
    public void BoxConvertToPta_UnitBoxAndErrors()
    {
        Pta p = Box.Create(10, 20, 1, 1).ConvertToPta(2);
        Assert.Equal((10, 20), P(p, 0));
        Assert.Equal((10, 20), P(p, 1));
        Assert.Throws<ArgumentException>(() => Box.Create(0, 0, 1, 1).ConvertToPta(3));
        Assert.Throws<ArgumentException>(() => Box.Create(0, 0, 1, 1).ConvertToPta(0));
    }

    [Fact]
    public void PtaConvertToBox_TwoCorners()
    {
        Box box = MakePta((3, 4), (7, 9)).ConvertToBox();
        Assert.Equal((3, 4, 5, 6), G(box));
        // a single pixel
        Assert.Equal((2, 2, 1, 1), G(MakePta((2, 2), (2, 2)).ConvertToBox()));
    }

    [Fact]
    public void PtaConvertToBox_FourCornersUsesMinimumContainingBox()
    {
        Assert.Equal((3, 4, 5, 6), G(MakePta((3, 4), (7, 4), (3, 9), (7, 9)).ConvertToBox()));
        // skewed corners: x = min(1, 0), y = min(2, 3), xmax = max(10, 9), ymax = max(12, 11)
        Assert.Equal((0, 2, 11, 11), G(MakePta((1, 2), (10, 3), (0, 12), (9, 11)).ConvertToBox()));
    }

    [Fact]
    public void PtaConvertToBox_Errors()
    {
        Assert.Throws<ArgumentException>(() => MakePta((1, 1)).ConvertToBox());
        Assert.Throws<ArgumentException>(() => MakePta((1, 1), (2, 2), (3, 3)).ConvertToBox());
        Assert.Throws<ArgumentException>(() => Pta.Create(0).ConvertToBox());
        // LR left of UL: negative width, rejected by boxCreate
        Assert.Throws<ArgumentException>(() => MakePta((7, 9), (3, 4)).ConvertToBox());
    }

    [Fact]
    public void PtaConvertToBoxa_TwoAndFourCorners()
    {
        Boxa b2 = MakePta((3, 4), (7, 9), (0, 0), (0, 0)).ConvertToBoxa(2);
        Assert.Equal(2, b2.Count);
        Assert.Equal((3, 4, 5, 6), G(b2, 0));
        Assert.Equal((0, 0, 1, 1), G(b2, 1));

        Boxa b4 = MakePta((3, 4), (7, 4), (3, 9), (7, 9),
                          (1, 2), (10, 3), (0, 12), (9, 11)).ConvertToBoxa(4);
        Assert.Equal(2, b4.Count);
        Assert.Equal((3, 4, 5, 6), G(b4, 0));
        Assert.Equal((0, 2, 11, 11), G(b4, 1));
    }

    [Fact]
    public void PtaConvertToBoxa_RoundTripWithBoxConvertToPta()
    {
        Box box = Box.Create(11, 22, 33, 44);
        Assert.Equal((11, 22, 33, 44), G(box.ConvertToPta(2).ConvertToBoxa(2), 0));
        Assert.Equal((11, 22, 33, 44), G(box.ConvertToPta(4).ConvertToBoxa(4), 0));
    }

    [Fact]
    public void PtaConvertToBoxa_ClipsToPositiveQuadrantAndSkipsFailures()
    {
        // (-3,0)-(4,5): x = -3, w = 8 -> clipped to x = 0, w = 5
        Boxa clipped = MakePta((-3, 0), (4, 5)).ConvertToBoxa(2);
        Assert.Equal((0, 0, 5, 6), G(clipped, 0));

        // The first pair has LR left of UL (w = -2); C logs the failure of boxCreate()
        // and boxaAddBox() and goes on with the next pair.
        Boxa skipped = MakePta((5, 5), (2, 2), (1, 1), (3, 3)).ConvertToBoxa(2);
        Assert.Equal(1, skipped.Count);
        Assert.Equal((1, 1, 3, 3), G(skipped, 0));
    }

    [Fact]
    public void PtaConvertToBoxa_Errors()
    {
        Assert.Throws<ArgumentException>(() => MakePta((1, 1), (2, 2)).ConvertToBoxa(3));
        Assert.Throws<ArgumentException>(() => MakePta((1, 1), (2, 2), (3, 3)).ConvertToBoxa(2));   // 3 % 2 != 0
        Assert.Throws<ArgumentException>(() => MakePta((1, 1), (2, 2)).ConvertToBoxa(4));           // 2 % 4 != 0
        Assert.Equal(0, Pta.Create(0).ConvertToBoxa(2).Count);
    }

    /*------------------------------- extent / coverage -------------------------*/

    /// <summary>A box with a negative left/top edge; Box.Create() would clip it.</summary>
    private static Box Neg(int x, int y, int w, int h)
    {
        Box box = Box.Create(0, 0, 1, 1);
        box.X = x;
        box.Y = y;
        box.Width = w;
        box.Height = h;
        return box;
    }

    [Fact]
    public void GetExtent_BoundingRegionOfAllBoxes()
    {
        Boxa boxa = MakeBoxa((10, 20, 30, 40), (5, 50, 10, 10));
        boxa.GetExtent(out int w, out int h, out Box box);
        Assert.Equal((40, 60), (w, h));          // xmax = max(40, 15), ymax = max(60, 60)
        Assert.Equal((5, 20, 35, 40), G(box));   // xmin = 5, ymin = 20
    }

    [Fact]
    public void GetExtent_EmptyAndAllInvalidGiveZeros()
    {
        Boxa.Create(0).GetExtent(out int w, out int h, out Box box);
        Assert.Equal((0, 0), (w, h));
        Assert.Equal((0, 0, 0, 0), G(box));

        MakeBoxa((3, 4, 0, 5), (6, 7, 5, 0)).GetExtent(out w, out h, out box);
        Assert.Equal((0, 0), (w, h));
        Assert.Equal((0, 0, 0, 0), G(box));
    }

    [Fact]
    public void GetExtent_IgnoresInvalidBoxes()
    {
        Boxa boxa = MakeBoxa((100, 100, 0, 5), (10, 20, 30, 40), (200, 200, 5, 0));
        boxa.GetExtent(out int w, out int h, out Box box);
        Assert.Equal((40, 60), (w, h));
        Assert.Equal((10, 20, 30, 40), G(box));
    }

    [Fact]
    public void GetExtent_SingleBox()
    {
        MakeBoxa((7, 9, 1, 1)).GetExtent(out int w, out int h, out Box box);
        Assert.Equal((8, 10), (w, h));
        Assert.Equal((7, 9, 1, 1), G(box));
    }

    [Fact]
    public void GetExtent_NegativeCoordinatesAreClippedByBoxCreate()
    {
        Boxa boxa = Boxa.Create(0);
        boxa.AddBox(Neg(-5, 2, 20, 10), L.L_INSERT);   // spans x in [-5, 15), y in [2, 12)
        boxa.GetExtent(out int w, out int h, out Box box);
        Assert.Equal((15, 12), (w, h));
        Assert.Equal((0, 2, 15, 10), G(box));          // boxCreate(-5, 2, 20, 10)
    }

    [Fact]
    public void GetExtent_BoxEntirelyLeftOfPositiveQuadrantThrows()
    {
        // C: boxCreate(-10, 0, 10, 5) fails and *pbox stays NULL; the port throws.
        Boxa boxa = Boxa.Create(0);
        boxa.AddBox(Neg(-10, 0, 5, 5), L.L_INSERT);
        Assert.Throws<ArgumentException>(() => boxa.GetExtent(out _, out _, out _));
    }

    [Fact]
    public void GetCoverage_QuickSumsClippedAreasAndCountsOverlapTwice()
    {
        // 20 x 20 rectangle: 100 + 100 = 200 of 400 (the overlap (5,5)-(9,9) is counted twice)
        Boxa boxa = MakeBoxa((0, 0, 10, 10), (5, 5, 10, 10));
        Assert.Equal(200f / 400f, boxa.GetCoverage(20, 20, 0));
        Assert.Equal(0.5f, boxa.GetCoverage(20, 20, 0));
    }

    [Fact]
    public void GetCoverage_QuickClipsToRectangle()
    {
        // (15,15,10,10) clipped to 20 x 20 is 5 x 5
        Assert.Equal(25f / 400f, MakeBoxa((15, 15, 10, 10)).GetCoverage(20, 20, 0));
        // a box that is entirely outside adds nothing
        Assert.Equal(9f / 20f, MakeBoxa((0, 0, 3, 3), (30, 30, 5, 5)).GetCoverage(4, 5, 0));
        Boxa neg = Boxa.Create(0);
        neg.AddBox(Neg(-5, -5, 10, 10), L.L_INSERT);   // clipped to (0, 0, 5, 5)
        Assert.Equal(25f / 400f, neg.GetCoverage(20, 20, 0));
    }

    [Fact]
    public void GetCoverage_ErrorsAndExact()
    {
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).GetCoverage(10, 10, 0));
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).GetCoverage(10, 10, 1));

        // exact: the area of the union of the boxes, counted pixel by pixel
        (int x, int y, int w, int h)[] boxes = { (0, 0, 10, 20), (5, 5, 30, 10), (2, 3, 10, 10), (7, 7, 50, 50) };
        bool[,] covered = new bool[100, 100];
        foreach (var (x, y, w, h) in boxes)
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++)
                    covered[j, i] = true;
        int union = 0;
        foreach (bool b in covered)
            if (b) union++;
        Assert.Equal((float)union / 10000f, Four().GetCoverage(100, 100, 1));
    }

    /*--------------------------- size and location ranges ----------------------*/

    [Fact]
    public void SizeRange_MinAndMax()
    {
        Four().SizeRange(out int minw, out int minh, out int maxw, out int maxh);
        Assert.Equal((10, 10, 50, 50), (minw, minh, maxw, maxh));
        MakeBoxa((0, 0, 7, 3)).SizeRange(out minw, out minh, out maxw, out maxh);
        Assert.Equal((7, 3, 7, 3), (minw, minh, maxw, maxh));
    }

    [Fact]
    public void SizeRange_EmptyKeepsInitialValues()
    {
        Boxa.Create(0).SizeRange(out int minw, out int minh, out int maxw, out int maxh);
        Assert.Equal((100000000, 100000000, 0, 0), (minw, minh, maxw, maxh));
    }

    [Fact]
    public void BoxaaSizeRange_CombinesAllBoxa()
    {
        Boxaa baa = Boxaa.Create(0);
        baa.AddBoxa(MakeBoxa((0, 0, 5, 7)), L.L_INSERT);
        baa.AddBoxa(Boxa.Create(0), L.L_INSERT);
        baa.AddBoxa(MakeBoxa((0, 0, 3, 9), (0, 0, 8, 2)), L.L_INSERT);
        baa.SizeRange(out int minw, out int minh, out int maxw, out int maxh);
        Assert.Equal((3, 2, 8, 9), (minw, minh, maxw, maxh));
    }

    [Fact]
    public void BoxaaSizeRange_EmptyKeepsInitialValues()
    {
        Boxaa.Create(0).SizeRange(out int minw, out int minh, out int maxw, out int maxh);
        Assert.Equal((100000000, 100000000, 0, 0), (minw, minh, maxw, maxh));
    }

    [Fact]
    public void LocationRange_MinAndMaxOfUpperLeftCorners()
    {
        Four().LocationRange(out int minx, out int miny, out int maxx, out int maxy);
        Assert.Equal((0, 0, 7, 7), (minx, miny, maxx, maxy));
        MakeBoxa((10, 20, 1, 1)).LocationRange(out minx, out miny, out maxx, out maxy);
        Assert.Equal((10, 20, 10, 20), (minx, miny, maxx, maxy));
    }

    [Fact]
    public void LocationRange_MaxStartsAtZeroAndEmptyKeepsInitialValues()
    {
        Boxa boxa = Boxa.Create(0);
        boxa.AddBox(Neg(-3, -4, 1, 1), L.L_INSERT);
        boxa.LocationRange(out int minx, out int miny, out int maxx, out int maxy);
        Assert.Equal((-3, -4, 0, 0), (minx, miny, maxx, maxy));

        Boxa.Create(0).LocationRange(out minx, out miny, out maxx, out maxy);
        Assert.Equal((100000000, 100000000, 0, 0), (minx, miny, maxx, maxy));
    }

    /*------------------------------ sizes and area -----------------------------*/

    [Fact]
    public void GetSizes_WidthsAndHeights()
    {
        Four().GetSizes(out Numa naw, out Numa nah);
        Assert.Equal(new[] { 10, 30, 10, 50 }, Ints(naw));
        Assert.Equal(new[] { 20, 10, 10, 50 }, Ints(nah));

        Boxa.Create(0).GetSizes(out naw, out nah);
        Assert.Equal(0, naw.Count);
        Assert.Equal(0, nah.Count);
    }

    [Fact]
    public void GetSizes_LoopsOverTheFirstValidCountIndices()
    {
        // NOTE(upstream): n = 2 valid boxes, so only indices 0 and 1 are visited;
        // box 1 (5 x 6) is reported and box 2 (7 x 8) is missed.
        Boxa boxa = MakeBoxa((0, 0, 0, 0), (0, 0, 5, 6), (0, 0, 7, 8));
        boxa.GetSizes(out Numa naw, out Numa nah);
        Assert.Equal(new[] { 5 }, Ints(naw));
        Assert.Equal(new[] { 6 }, Ints(nah));
    }

    [Fact]
    public void GetArea_SumsWithoutRegardToOverlap()
    {
        Assert.Equal(3100, Four().GetArea());   // 200 + 300 + 100 + 2500
        Assert.Equal(0, Boxa.Create(0).GetArea());
        Assert.Equal(200, MakeBoxa((0, 0, 10, 10), (0, 0, 10, 10)).GetArea());   // overlap counted twice
        Assert.Equal(0, MakeBoxa((0, 0, 0, 9)).GetArea());
    }

    [Fact]
    public void GetArea_WrapsLikeC()
    {
        // each area is 10^10 mod 2^32 = 1410065408; the sum 2820130816 wraps to 2820130816 - 2^32
        Boxa boxa = MakeBoxa((0, 0, 100000, 100000), (0, 0, 100000, 100000));
        Assert.Equal(-1474836480, boxa.GetArea());
    }

    /*-------------------------------- display ---------------------------------*/

    [Fact]
    public void DisplayTiled_ValidatesTheIndexRangeBeforeUsingStubs()
    {
        Boxa boxa = Four();
        Assert.Throws<ArgumentException>(() => boxa.DisplayTiled(null, 4, -1, 500, 2, 1.0f, 0, 10, 2));   // first >= n
        Assert.Throws<ArgumentException>(() => boxa.DisplayTiled(null, 3, 1, 500, 2, 1.0f, 0, 10, 2));    // first > last
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).DisplayTiled(null, 0, -1, 500, 2, 1.0f, 0, 10, 2));
        // only invalid boxes: nothing is left after boxaSaveValid()
        Assert.Throws<ArgumentException>(
            () => MakeBoxa((0, 0, 0, 5)).DisplayTiled(null, 0, -1, 500, 2, 1.0f, 0, 10, 2));
    }

    [Fact]
    public void DisplayTiled_RendersWithTheBitmapFont()
    {
        Boxa boxa = Four();
        Pix pix = boxa.DisplayTiled(null, 0, -1, 500, 2, 1.0f, 0, 10, 2);
        Assert.Equal(32, pix.Depth);
        Assert.NotNull(boxa.DisplayTiled(null, 1, 99, 500, 2, 0.5f, 1, 10, 2));   // last adjusted
        Assert.NotNull(boxa.DisplayTiled(null, -2, -1, 500, 2, 0.1f, 1, 10, 2));  // first -> 0
    }
}
