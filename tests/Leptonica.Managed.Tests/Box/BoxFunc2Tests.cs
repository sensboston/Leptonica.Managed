using System;
using System.Linq;
using Leptonica;

namespace Leptonica.Managed.Tests;

/// <summary>
/// Tests for boxfunc2.c with hand-computed expected values.  The sorting and rank-value tests
/// depend on callees in numafunc1.c, and some others on boxaGetExtent() (boxfunc4.c); those
/// are skipped until the callees are ported.
/// </summary>
public class BoxFunc2Tests
{

    private static Box B(int x, int y, int w, int h) => Box.Create(x, y, w, h);

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

    private static (int, int, int, int)[] GA(Boxa boxa) =>
        Enumerable.Range(0, boxa.Count).Select(i => G(boxa, i)).ToArray();

    private static int[] I(Numa na) =>
        Enumerable.Range(0, na.Count).Select(i => na.GetIValue(i)).ToArray();

    private static (int, int)[] P(Pta pta)
    {
        var r = new (int, int)[pta.Count];
        for (int i = 0; i < r.Length; i++) {
            pta.GetIPt(i, out int x, out int y);
            r[i] = (x, y);
        }
        return r;
    }

    /*------------------------- boxTransform / boxaTransform -------------------------*/

    [Fact]
    public void Transform_ShiftsThenScales()
    {
        // x = 2.0 * (10 + 5) + 0.5 = 30.5 -> 30; y = 0.5 * (20 - 10) + 0.5 = 5.5 -> 5
        // w = 2.0 * 30 + 0.5 = 60.5 -> 60;       h = 0.5 * 40 + 0.5 = 20.5 -> 20
        Box box = B(10, 20, 30, 40).Transform(5, -10, 2.0f, 0.5f);
        Assert.Equal((30, 5, 60, 20), G(box));
    }

    [Fact]
    public void Transform_ClipsNegativeCornerAndKeepsSizeAtLeastOne()
    {
        // x = 1.0 * (2 - 5) + 0.5 = -2.5 -> clipped to 0
        Assert.Equal((0, 0, 10, 10), G(B(2, 2, 10, 10).Transform(-5, -5, 1.0f, 1.0f)));
        // w = 0.1 * 1 + 0.5 = 0.6 -> raised to 1
        Assert.Equal((0, 0, 1, 1), G(B(0, 0, 1, 1).Transform(0, 0, 0.1f, 0.1f)));
    }

    [Fact]
    public void Transform_InvalidBoxGivesInvalidBox()
    {
        Assert.Equal((0, 0, 0, 0), G(B(5, 5, 0, 10).Transform(3, 3, 2.0f, 2.0f)));
        Assert.Equal((0, 0, 0, 0), G(B(5, 5, 10, 0).Transform(3, 3, 2.0f, 2.0f)));
    }

    [Fact]
    public void BoxaTransform_AppliesToEachBox()
    {
        Boxa boxa = MakeBoxa((1, 2, 3, 4), (0, 0, 0, 0), (10, 10, 5, 5));
        Boxa res = boxa.Transform(1, 1, 2.0f, 3.0f);
        // x = 2 * (1 + 1) + 0.5 = 4.5 -> 4, y = 3 * (2 + 1) + 0.5 = 9.5 -> 9, w = 6.5 -> 6, h = 12.5 -> 12
        Assert.Equal(new[] { (4, 9, 6, 12), (0, 0, 0, 0), (22, 33, 10, 15) }, GA(res));
        // The source is unchanged
        Assert.Equal((1, 2, 3, 4), G(boxa, 0));
        Assert.Equal(0, Boxa.Create(0).Transform(1, 1, 2.0f, 2.0f).Count);
    }

    /*------------------- boxTransformOrdered / boxaTransformOrdered -------------------*/

    [Theory]
    [InlineData(L.L_TR_SC_RO, 30, 81, 60, 120)]
    [InlineData(L.L_SC_TR_RO, 25, 67, 60, 120)]
    [InlineData(L.L_RO_TR_SC, 30, 81, 60, 120)]
    [InlineData(L.L_RO_SC_TR, 25, 67, 60, 120)]
    [InlineData(L.L_TR_RO_SC, 30, 81, 60, 120)]
    [InlineData(L.L_SC_RO_TR, 25, 67, 60, 120)]
    public void TransformOrdered_NoRotation(int order, int x, int y, int w, int h)
    {
        // box (10, 20, 30, 40), shift (5, 7), scale (2, 3)
        // shift first:  x = 2 * (10 + 5) + 0.5 = 30,  y = 3 * (20 + 7) + 0.5 = 81
        // scale first:  x = 2 * 10 + 5 + 0.5 = 25,    y = 3 * 20 + 7 + 0.5 = 67
        // w = 2 * 30 + 0.5 = 60, h = 3 * 40 + 0.5 = 120
        Box box = B(10, 20, 30, 40).TransformOrdered(5, 7, 2.0f, 3.0f, 0, 0, 0.0f, order);
        Assert.Equal((x, y, w, h), G(box));
    }

    [Fact]
    public void TransformOrdered_InvalidOrderThrows()
    {
        Assert.Throws<ArgumentException>(() => B(1, 1, 2, 2).TransformOrdered(0, 0, 1.0f, 1.0f, 0, 0, 0.0f, 0));
        Assert.Throws<ArgumentException>(() => B(1, 1, 2, 2).TransformOrdered(0, 0, 1.0f, 1.0f, 0, 0, 0.0f, 7));
    }

    [Fact]
    public void TransformOrdered_InvalidBoxGivesInvalidBox()
    {
        Box res = B(1, 1, 0, 2).TransformOrdered(3, 3, 2.0f, 2.0f, 0, 0, 0.5f, L.L_TR_SC_RO);
        Assert.Equal((0, 0, 0, 0), G(res));
    }

    /* Rotation by pi/2 (as a float) about (50, 50) of box (10, 20, 30, 40).  The float values are
     * sina = 1.0f, cosa = -4.371139e-8f, so that
     *   rw = |30 cosa| + |40 sina| = 40 (the 1.3e-6 is lost in float),
     *   rh = |40 cosa| + |30 sina| = 30.000002 (1.7e-6 is more than half an ulp at 30).
     * The center moves to (60, 25), the UL corner is (40, 25 - 15.000001) = (40, 9.999999).
     * Orders that truncate the corner give y = 9; orders that add 0.5 first give y = 10. */
    private const float HalfPi = 1.5707964f;

    [Theory]
    [InlineData(L.L_TR_SC_RO, 9)]
    [InlineData(L.L_SC_TR_RO, 9)]
    [InlineData(L.L_RO_TR_SC, 10)]
    [InlineData(L.L_RO_SC_TR, 10)]
    [InlineData(L.L_TR_RO_SC, 10)]
    [InlineData(L.L_SC_RO_TR, 10)]
    public void TransformOrdered_QuarterTurn(int order, int expectedY)
    {
        Box box = B(10, 20, 30, 40).TransformOrdered(0, 0, 1.0f, 1.0f, 50, 50, HalfPi, order);
        Assert.Equal((40, expectedY, 40, 30), G(box));
    }

    [Fact]
    public void BoxaTransformOrdered_AppliesToEachBox()
    {
        Boxa boxa = MakeBoxa((10, 20, 30, 40), (0, 0, 0, 0));
        Boxa res = boxa.TransformOrdered(5, 7, 2.0f, 3.0f, 0, 0, 0.0f, L.L_TR_SC_RO);
        Assert.Equal(new[] { (30, 81, 60, 120), (0, 0, 0, 0) }, GA(res));
        Assert.Throws<ArgumentException>(
            () => boxa.TransformOrdered(5, 7, 2.0f, 3.0f, 0, 0, 0.0f, 0));
    }

    /*------------------------- boxRotateOrth / boxaRotateOrth -------------------------*/

    [Fact]
    public void RotateOrth_AllRotations()
    {
        // image 100 x 80, box (10, 20, 30, 40): xdist = 100 - 10 - 30 = 60, ydist = 80 - 20 - 40 = 20
        Box box = B(10, 20, 30, 40);
        Box r0 = box.RotateOrth(100, 80, 0);
        Assert.Equal((10, 20, 30, 40), G(r0));
        Assert.NotSame(box, r0);
        Assert.Equal((20, 10, 40, 30), G(box.RotateOrth(100, 80, 1)));  // (ydist, x, h, w)
        Assert.Equal((60, 20, 30, 40), G(box.RotateOrth(100, 80, 2)));  // (xdist, ydist, w, h)
        Assert.Equal((20, 60, 40, 30), G(box.RotateOrth(100, 80, 3)));  // (y, xdist, h, w)
    }

    [Fact]
    public void RotateOrth_FourQuarterTurnsAreIdentity()
    {
        Box box = B(10, 20, 30, 40);
        int w = 100, h = 80;
        Box r = box;
        for (int k = 0; k < 4; k++) {
            r = r.RotateOrth(w, h, 1);
            (w, h) = (h, w);
        }
        Assert.Equal((10, 20, 30, 40), G(r));
    }

    [Fact]
    public void RotateOrth_InvalidInputs()
    {
        Assert.Throws<ArgumentException>(() => B(1, 1, 2, 2).RotateOrth(10, 10, 4));
        Assert.Throws<ArgumentException>(() => B(1, 1, 2, 2).RotateOrth(10, 10, -1));
        Assert.Equal((0, 0, 0, 0), G(B(1, 1, 0, 2).RotateOrth(10, 10, 1)));
        Assert.Throws<ArgumentException>(() => MakeBoxa((1, 1, 2, 2)).RotateOrth(10, 10, 4));
    }

    [Fact]
    public void BoxaRotateOrth_RotatesEachBoxAndCopiesForZero()
    {
        Boxa boxa = MakeBoxa((10, 20, 30, 40), (0, 0, 5, 5));
        Boxa r1 = boxa.RotateOrth(100, 80, 1);
        Assert.Equal(new[] { (20, 10, 40, 30), (75, 0, 5, 5) }, GA(r1));
        Boxa r0 = boxa.RotateOrth(100, 80, 0);
        Assert.NotSame(boxa, r0);
        Assert.Equal(GA(boxa), GA(r0));
    }

    /*------------------------------- boxaShiftWithPta -------------------------------*/

    [Fact]
    public void ShiftWithPta_ShiftsAndUnshifts()
    {
        Boxa boxa = MakeBoxa((10, 10, 5, 5), (20, 30, 5, 5));
        Pta pta = Pta.Create(2);
        pta.AddPt(1, 2);
        pta.AddPt(3, 4);
        Boxa shifted = boxa.ShiftWithPta(pta, 1);
        Assert.Equal(new[] { (11, 12, 5, 5), (23, 34, 5, 5) }, GA(shifted));
        Assert.Equal(new[] { (9, 8, 5, 5), (17, 26, 5, 5) }, GA(boxa.ShiftWithPta(pta, -1)));
        // (1, -1) are inverses
        Assert.Equal(GA(boxa), GA(shifted.ShiftWithPta(pta, -1)));
    }

    [Fact]
    public void ShiftWithPta_ClipsAtLeftAndTop()
    {
        Boxa boxa = MakeBoxa((1, 1, 5, 5));
        Pta pta = Pta.Create(1);
        pta.AddPt(3, 3);
        Assert.Equal((0, 0, 5, 5), G(boxa.ShiftWithPta(pta, -1), 0));
    }

    [Fact]
    public void ShiftWithPta_InvalidInputs()
    {
        Boxa boxa = MakeBoxa((10, 10, 5, 5), (20, 30, 5, 5));
        Pta pta = Pta.Create(2);
        pta.AddPt(1, 2);
        pta.AddPt(3, 4);
        Assert.Throws<ArgumentException>(() => boxa.ShiftWithPta(pta, 0));
        Assert.Throws<ArgumentException>(() => boxa.ShiftWithPta(pta, 2));
        Pta pta1 = Pta.Create(1);
        pta1.AddPt(1, 2);
        Assert.Throws<ArgumentException>(() => boxa.ShiftWithPta(pta1, 1));
    }

    /*----------------------------------- boxaSort -----------------------------------*/

    private static Boxa SortFixture() => MakeBoxa(
        (30, 5, 10, 20),    // 0
        (10, 15, 40, 5),    // 1
        (20, 0, 5, 50),     // 2
        (10, 40, 15, 15));  // 3

    [Fact]
    public void Sort_EmptyReturnsEmptyCopyWithoutIndex()
    {
        Boxa boxa = Boxa.Create(0);
        Boxa res = boxa.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out Numa? naindex);
        Assert.Equal(0, res.Count);
        Assert.NotSame(boxa, res);
        Assert.Null(naindex);
    }

    [Fact]
    public void Sort_InvalidTypeOrOrderThrows()
    {
        Boxa boxa = SortFixture();
        Assert.Throws<ArgumentException>(() => boxa.Sort(0, L.L_SORT_INCREASING, out _));
        Assert.Throws<ArgumentException>(() => boxa.Sort(12, L.L_SORT_INCREASING, out _));
        Assert.Throws<ArgumentException>(() => boxa.Sort(L.L_SORT_BY_X, 0, out _));
        Assert.Throws<ArgumentException>(() => boxa.BinSort(L.L_SORT_BY_RIGHT, L.L_SORT_INCREASING, out _));
        Assert.Throws<ArgumentException>(() => boxa.BinSort(L.L_SORT_BY_X, 3, out _));
    }

    // Expected index arrays follow from the shellsort of numaGetSortIndex(): ties keep the
    // order that the gap sequence produces, e.g. for x = {30, 10, 20, 10} the two 10s stay in
    // their original order (1 before 3).
    [Theory]
    [InlineData(L.L_SORT_BY_X, L.L_SORT_INCREASING, new[] { 1, 3, 2, 0 })]
    [InlineData(L.L_SORT_BY_X, L.L_SORT_DECREASING, new[] { 0, 2, 1, 3 })]
    [InlineData(L.L_SORT_BY_Y, L.L_SORT_INCREASING, new[] { 2, 0, 1, 3 })]
    [InlineData(L.L_SORT_BY_RIGHT, L.L_SORT_INCREASING, new[] { 2, 3, 0, 1 })]
    [InlineData(L.L_SORT_BY_BOT, L.L_SORT_INCREASING, new[] { 1, 0, 2, 3 })]
    [InlineData(L.L_SORT_BY_WIDTH, L.L_SORT_INCREASING, new[] { 2, 0, 3, 1 })]
    [InlineData(L.L_SORT_BY_HEIGHT, L.L_SORT_INCREASING, new[] { 1, 3, 0, 2 })]
    [InlineData(L.L_SORT_BY_MIN_DIMENSION, L.L_SORT_INCREASING, new[] { 2, 1, 0, 3 })]
    [InlineData(L.L_SORT_BY_MAX_DIMENSION, L.L_SORT_INCREASING, new[] { 3, 0, 1, 2 })]
    [InlineData(L.L_SORT_BY_PERIMETER, L.L_SORT_INCREASING, new[] { 0, 3, 1, 2 })]
    [InlineData(L.L_SORT_BY_AREA, L.L_SORT_INCREASING, new[] { 0, 1, 3, 2 })]
    [InlineData(L.L_SORT_BY_ASPECT_RATIO, L.L_SORT_INCREASING, new[] { 2, 0, 3, 1 })]
    public void Sort_ByEachKey(int sorttype, int sortorder, int[] expected)
    {
        Boxa boxa = SortFixture();
        Boxa res = boxa.Sort(sorttype, sortorder, out Numa? naindex);
        Assert.Equal(expected, I(naindex!));
        Assert.Equal(expected.Select(i => G(boxa, i)).ToArray(), GA(res));
    }

    [Fact]
    public void Sort_LargeBoxaUsesBinSort()
    {
        // 201 > MinCompsForBinSort distinct x values: (7 i) % 211 for i = 0..200
        Boxa boxa = Boxa.Create(0);
        for (int i = 0; i < 201; i++)
            boxa.AddBox(B((7 * i) % 211, i, 5, 5), L.L_INSERT);
        Boxa res = boxa.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out Numa? naindex);
        Assert.Equal(201, naindex!.Count);
        int prev = -1;
        for (int k = 0; k < 201; k++) {
            int x = G(res, k).Item1;
            Assert.True(x > prev);
            prev = x;
            Assert.Equal(G(boxa, naindex.GetIValue(k)), G(res, k));
        }
    }

    [Fact]
    public void BinSort_SortsByPerimeterDecreasing()
    {
        Boxa boxa = MakeBoxa((0, 0, 3, 4), (0, 0, 10, 10), (0, 0, 1, 1), (0, 0, 5, 2));
        Boxa res = boxa.BinSort(L.L_SORT_BY_PERIMETER, L.L_SORT_DECREASING, out Numa? naindex);
        // perimeters 7, 20, 2, 7: the two 7s are in either order
        Assert.Equal(1, naindex!.GetIValue(0));
        Assert.Equal(2, naindex.GetIValue(3));
        Assert.Equal((0, 0, 10, 10), G(res, 0));
    }

    [Fact]
    public void SortByIndex_ReordersCopies()
    {
        Boxa boxa = MakeBoxa((1, 1, 1, 1), (2, 2, 2, 2), (3, 3, 3, 3));
        Numa naindex = Numa.CreateFromIArray(new[] { 2, 0, 1 }, 3);
        Boxa res = boxa.SortByIndex(naindex);
        Assert.Equal(new[] { (3, 3, 3, 3), (1, 1, 1, 1), (2, 2, 2, 2) }, GA(res));
        Assert.NotSame(boxa.GetBox(0, L.L_CLONE), res.GetBox(1, L.L_CLONE));
    }

    [Fact]
    public void SortByIndex_EmptyReturnsCopy()
    {
        Boxa res = Boxa.Create(0).SortByIndex(Numa.Create(0));
        Assert.Equal(0, res.Count);
    }

    /*---------------------------------- boxaSort2d ----------------------------------*/

    [Fact]
    public void Sort2d_EmptyThrows()
    {
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).Sort2d(out _, 0, 0, 5));
    }

    [Fact]
    public void Sort2d_TwoTextLinesWithSmallComponent()
    {
        Boxa boxas = MakeBoxa(
            (50, 10, 8, 12),   // 0  line 1
            (10, 11, 8, 12),   // 1  line 1
            (30, 9, 8, 12),    // 2  line 1
            (60, 42, 8, 12),   // 3  line 2
            (20, 40, 8, 12),   // 4  line 2
            (45, 12, 3, 3));   // 5  small component, h < minh1, joins line 1 in pass 2
        Boxaa baa = boxas.Sort2d(out Numaa naad, 0, 0, 5);

        Assert.Equal(2, baa.Count);
        Assert.Equal(new[] { (10, 11, 8, 12), (30, 9, 8, 12), (45, 12, 3, 3), (50, 10, 8, 12) },
                     GA(baa.GetBoxa(0, L.L_CLONE)));
        Assert.Equal(new[] { (20, 40, 8, 12), (60, 42, 8, 12) }, GA(baa.GetBoxa(1, L.L_CLONE)));

        // The values are the indices into boxas, except for the small component of pass 2:
        // NOTE(upstream): pass 2 stores its position in the x-sorted boxa (3) instead of the
        // original index (5).
        Assert.Equal(2, naad.Count);
        Assert.Equal(new[] { 1, 2, 3, 0 }, I(naad.GetNuma(0, L.L_CLONE)));
        Assert.Equal(new[] { 4, 3 }, I(naad.GetNuma(1, L.L_CLONE)));
    }

    [Fact]
    public void Sort2d_RowsAreSortedTopToBottom()
    {
        Boxa boxas = MakeBoxa((0, 40, 5, 10), (0, 10, 5, 10));
        Boxaa baa = boxas.Sort2d(out Numaa naad, 0, 0, 5);
        Assert.Equal(2, baa.Count);
        Assert.Equal((0, 10, 5, 10), G(baa.GetBoxa(0, L.L_CLONE), 0));
        Assert.Equal((0, 40, 5, 10), G(baa.GetBoxa(1, L.L_CLONE), 0));
        Assert.Equal(new[] { 1 }, I(naad.GetNuma(0, L.L_CLONE)));
        Assert.Equal(new[] { 0 }, I(naad.GetNuma(1, L.L_CLONE)));
    }

    /*-------------------------------- boxaSort2dByIndex -------------------------------*/

    [Fact]
    public void Sort2dByIndex_InvalidInputs()
    {
        Numaa naa = Numaa.Create(1);
        Numa na = Numa.CreateFromIArray(new[] { 0, 1 }, 2);
        naa.AddNuma(na, L.L_INSERT);
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).Sort2dByIndex(naa));
        // 2 numbers in naa, 3 boxes
        Assert.Throws<ArgumentException>(
            () => MakeBoxa((1, 1, 1, 1), (2, 2, 2, 2), (3, 3, 3, 3)).Sort2dByIndex(naa));
    }

    [Fact]
    public void Sort2dByIndex_ReadsEntryIForEveryJ()
    {
        // NOTE(upstream): the inner loop reads entry i (the row) instead of entry j, so the
        // single row (i = 0) {2, 1, 0} gives box 2 three times.
        Boxa boxas = MakeBoxa((1, 1, 1, 1), (2, 2, 2, 2), (3, 3, 3, 3));
        Numaa naa = Numaa.Create(1);
        naa.AddNuma(Numa.CreateFromIArray(new[] { 2, 1, 0 }, 3), L.L_INSERT);
        Boxaa baa = boxas.Sort2dByIndex(naa);
        Assert.Equal(1, baa.Count);
        Assert.Equal(new[] { (3, 3, 3, 3), (3, 3, 3, 3), (3, 3, 3, 3) },
                     GA(baa.GetBoxa(0, L.L_CLONE)));
    }

    /*------------------------- boxaExtractAsNuma / AsPta / Corners ------------------------*/

    private static Boxa ExtractFixture() => MakeBoxa(
        (3, 4, 5, 6),
        (10, 20, 0, 5),    // invalid
        (7, 8, 2, 3));

    [Fact]
    public void ExtractAsNuma_KeepInvalid()
    {
        ExtractFixture().ExtractAsNuma(out Numa nal, out Numa nat, out Numa nar, out Numa nab,
                                       out Numa naw, out Numa nah, 1);
        Assert.Equal(new[] { 3, 10, 7 }, I(nal));
        Assert.Equal(new[] { 4, 20, 8 }, I(nat));
        Assert.Equal(new[] { 7, 9, 8 }, I(nar));     // left + w - 1; 10 + 0 - 1 for the invalid box
        Assert.Equal(new[] { 9, 24, 10 }, I(nab));
        Assert.Equal(new[] { 5, 0, 2 }, I(naw));
        Assert.Equal(new[] { 6, 5, 3 }, I(nah));
    }

    [Fact]
    public void ExtractAsNuma_RemoveInvalid()
    {
        ExtractFixture().ExtractAsNuma(out Numa nal, out Numa nat, out Numa nar, out Numa nab,
                                       out Numa naw, out Numa nah, 0);
        Assert.Equal(new[] { 3, 7 }, I(nal));
        Assert.Equal(new[] { 4, 8 }, I(nat));
        Assert.Equal(new[] { 7, 8 }, I(nar));
        Assert.Equal(new[] { 9, 10 }, I(nab));
        Assert.Equal(new[] { 5, 2 }, I(naw));
        Assert.Equal(new[] { 6, 3 }, I(nah));
    }

    [Fact]
    public void ExtractAsNuma_NoValidBoxes()
    {
        Boxa boxa = MakeBoxa((1, 1, 0, 0));
        Assert.Throws<ArgumentException>(() => boxa.ExtractAsNuma(
            out _, out _, out _, out _, out _, out _, 0));
        boxa.ExtractAsNuma(out Numa nal, out _, out _, out _, out _, out _, 1);
        Assert.Equal(new[] { 1 }, I(nal));
        Boxa.Create(0).ExtractAsNuma(out Numa nae, out _, out _, out _, out _, out _, 1);
        Assert.Equal(0, nae.Count);
    }

    [Fact]
    public void ExtractAsPta_StoresBoxIndexInX()
    {
        ExtractFixture().ExtractAsPta(out Pta ptal, out Pta ptat, out Pta ptar, out Pta ptab,
                                      out Pta ptaw, out Pta ptah, 0);
        Assert.Equal(new[] { (0, 3), (2, 7) }, P(ptal));
        Assert.Equal(new[] { (0, 4), (2, 8) }, P(ptat));
        Assert.Equal(new[] { (0, 7), (2, 8) }, P(ptar));
        Assert.Equal(new[] { (0, 9), (2, 10) }, P(ptab));
        Assert.Equal(new[] { (0, 5), (2, 2) }, P(ptaw));
        Assert.Equal(new[] { (0, 6), (2, 3) }, P(ptah));

        ExtractFixture().ExtractAsPta(out Pta ptal1, out _, out _, out _, out Pta ptaw1, out _, 1);
        Assert.Equal(new[] { (0, 3), (1, 10), (2, 7) }, P(ptal1));
        Assert.Equal(new[] { (0, 5), (1, 0), (2, 2) }, P(ptaw1));
    }

    [Fact]
    public void ExtractAsPta_NoValidBoxesThrows()
    {
        Assert.Throws<ArgumentException>(() => MakeBoxa((1, 1, 0, 0)).ExtractAsPta(
            out _, out _, out _, out _, out _, out _, 0));
    }

    [Theory]
    [InlineData(L.L_UPPER_LEFT, new[] { 3, 4, 0, 0, 7, 8 })]
    [InlineData(L.L_UPPER_RIGHT, new[] { 7, 4, 0, 0, 8, 8 })]
    [InlineData(L.L_LOWER_LEFT, new[] { 3, 9, 0, 0, 7, 10 })]
    [InlineData(L.L_LOWER_RIGHT, new[] { 7, 9, 0, 0, 8, 10 })]
    [InlineData(L.L_BOX_CENTER, new[] { 5, 6, 0, 0, 7, 9 })]  // (3 + 7) / 2, (4 + 9) / 2, ...; 15 / 2 = 7
    public void ExtractCorners(int loc, int[] xy)
    {
        Pta pta = ExtractFixture().ExtractCorners(loc);
        Assert.Equal(new[] { (xy[0], xy[1]), (xy[2], xy[3]), (xy[4], xy[5]) }, P(pta));
    }

    [Fact]
    public void ExtractCorners_InvalidLocationThrows()
    {
        Assert.Throws<ArgumentException>(() => ExtractFixture().ExtractCorners(0));
        Assert.Throws<ArgumentException>(() => ExtractFixture().ExtractCorners(6));
    }

    /*------------------------------------ statistics ------------------------------------*/

    private static Boxa StatsFixture() => MakeBoxa(
        (10, 20, 30, 40),   // l 10, t 20, r 39, b 59, w 30, h 40
        (12, 25, 20, 10),   // l 12, t 25, r 31, b 34, w 20, h 10
        (8, 22, 50, 30),    // l  8, t 22, r 57, b 51, w 50, h 30
        (14, 18, 10, 60),   // l 14, t 18, r 23, b 77, w 10, h 60
        (0, 0, 0, 0));      // invalid: not used by the rank values

    [Fact]
    public void GetRankVals_InvalidInputs()
    {
        Assert.Throws<ArgumentException>(() => StatsFixture().GetRankVals(-0.1f, out _, out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => StatsFixture().GetRankVals(1.5f, out _, out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(
            () => MakeBoxa((0, 0, 0, 0)).GetRankVals(0.5f, out _, out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(
            () => Boxa.Create(0).GetMedianVals(out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void GetMedianVals_UsesValidBoxesOnly()
    {
        // sorted x {8, 10, 12, 14}, y {18, 20, 22, 25}, r {23, 31, 39, 57}, b {34, 51, 59, 77},
        // w {10, 20, 30, 50}, h {10, 30, 40, 60}; index (int)(0.5 * 3 + 0.5) = 2
        StatsFixture().GetMedianVals(out int x, out int y, out int r, out int b, out int w, out int h);
        Assert.Equal((12, 22, 39, 59, 30, 40), (x, y, r, b, w, h));
    }

    [Fact]
    public void GetRankVals_Extremes()
    {
        // fract 0: smallest r, b, w, h but largest x, y
        StatsFixture().GetRankVals(0.0f, out int x, out int y, out int r, out int b, out int w, out int h);
        Assert.Equal((14, 25, 23, 34, 10, 10), (x, y, r, b, w, h));
        // fract 1: largest r, b, w, h but smallest x, y
        StatsFixture().GetRankVals(1.0f, out x, out y, out r, out b, out w, out h);
        Assert.Equal((8, 18, 57, 77, 50, 60), (x, y, r, b, w, h));
    }

    [Fact]
    public void GetAverageSize()
    {
        // widths 30 + 20 + 50 + 10 = 110, heights 40 + 10 + 30 + 60 = 140; the invalid box counts
        StatsFixture().GetAverageSize(out float w, out float h);
        Assert.Equal(22.0f, w);
        Assert.Equal(28.0f, h);
        MakeBoxa((0, 0, 3, 4), (5, 5, 4, 1)).GetAverageSize(out w, out h);
        Assert.Equal(3.5f, w);
        Assert.Equal(2.5f, h);
    }

    [Fact]
    public void GetAverageSize_EmptyThrows()
    {
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).GetAverageSize(out _, out _));
    }

    /*------------------------------- Boxaa functions -------------------------------*/

    [Fact]
    public void BoxaaGetExtent_EmptyThrows()
    {
        Assert.Throws<ArgumentException>(() => Boxaa.Create(0).GetExtent(out _, out _, out _, out _));
    }

    [Fact]
    public void BoxaaGetExtent_BoxaWithoutValidBoxes()
    {
        Boxaa baa = Boxaa.Create(0);
        baa.AddBoxa(Boxa.Create(0), L.L_INSERT);
        baa.GetExtent(out int w, out int h, out Box box, out Boxa boxa);
        Assert.Equal((0, 0), (w, h));
        Assert.Equal((0, 0, 0, 0), G(box));
        Assert.Equal(new[] { (0, 0, 0, 0) }, GA(boxa));
    }

    [Fact]
    public void BoxaaGetExtent_ExtentOfEachBoxaAndOfAll()
    {
        Boxaa baa = Boxaa.Create(0);
        baa.AddBoxa(MakeBoxa((10, 10, 20, 20), (40, 5, 10, 10)), L.L_INSERT);
        baa.AddBoxa(MakeBoxa((100, 100, 5, 5)), L.L_INSERT);
        baa.GetExtent(out int w, out int h, out Box box, out Boxa boxa);
        Assert.Equal((105, 105), (w, h));
        Assert.Equal((10, 5, 95, 100), G(box));
        Assert.Equal(new[] { (10, 5, 40, 25), (100, 100, 5, 5) }, GA(boxa));
    }

    private static Boxaa FlattenFixture()
    {
        Boxaa baa = Boxaa.Create(0);
        baa.AddBoxa(MakeBoxa((1, 1, 1, 1), (2, 2, 2, 2)), L.L_INSERT);
        baa.AddBoxa(Boxa.Create(0), L.L_INSERT);
        baa.AddBoxa(MakeBoxa((3, 3, 3, 3)), L.L_INSERT);
        return baa;
    }

    [Fact]
    public void FlattenToBoxa_PlaceholderForEmptyBoxa()
    {
        Boxa boxa = FlattenFixture().FlattenToBoxa(out Numa naindex, L.L_COPY);
        Assert.Equal(new[] { (1, 1, 1, 1), (2, 2, 2, 2), (0, 0, 0, 0), (3, 3, 3, 3) }, GA(boxa));
        Assert.Equal(new[] { 0, 0, 1, 2 }, I(naindex));
    }

    [Fact]
    public void FlattenToBoxa_CloneSharesBoxesCopyDoesNot()
    {
        Boxaa baa = FlattenFixture();
        Box orig = baa.GetBox(0, 0, L.L_CLONE)!;
        Boxa cloned = baa.FlattenToBoxa(out _, L.L_CLONE);
        Boxa copied = baa.FlattenToBoxa(out _, L.L_COPY);
        Assert.Same(orig, cloned.GetBox(0, L.L_CLONE));
        Assert.NotSame(orig, copied.GetBox(0, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => baa.FlattenToBoxa(out _, L.L_INSERT));
    }

    [Fact]
    public void FlattenAligned_FillsShortBoxa()
    {
        Boxaa baa = FlattenFixture();
        // num = 2: the two boxes of the first boxa, 2 placeholders for the empty second boxa,
        // the one box of the third boxa and 1 placeholder
        Boxa b1 = baa.FlattenAligned(2, null, L.L_COPY);
        Assert.Equal(new[] { (1, 1, 1, 1), (2, 2, 2, 2), (0, 0, 0, 0), (0, 0, 0, 0),
                             (3, 3, 3, 3), (0, 0, 0, 0) }, GA(b1));
        // The same with a filler box
        Boxa b2 = baa.FlattenAligned(2, B(9, 9, 9, 9), L.L_COPY);
        Assert.Equal(new[] { (1, 1, 1, 1), (2, 2, 2, 2), (9, 9, 9, 9), (9, 9, 9, 9),
                             (3, 3, 3, 3), (9, 9, 9, 9) }, GA(b2));
        Assert.Throws<ArgumentException>(() => baa.FlattenAligned(2, null, L.L_INSERT));
    }

    [Fact]
    public void FlattenAligned_TakesOnlyFirstNum()
    {
        Boxaa baa = Boxaa.Create(0);
        baa.AddBoxa(MakeBoxa((1, 1, 1, 1), (2, 2, 2, 2), (3, 3, 3, 3)), L.L_INSERT);
        Assert.Equal(new[] { (1, 1, 1, 1) }, GA(baa.FlattenAligned(1, null, L.L_CLONE)));
        Assert.Equal(0, baa.FlattenAligned(0, null, L.L_CLONE).Count);
    }

    [Fact]
    public void EncapsulateAligned_GroupsConsecutiveBoxes()
    {
        Boxa boxa = MakeBoxa((1, 1, 1, 1), (2, 2, 2, 2), (3, 3, 3, 3), (4, 4, 4, 4), (5, 5, 5, 5), (6, 6, 6, 6));
        Boxaa baa = boxa.EncapsulateAligned(3, L.L_COPY);
        Assert.Equal(2, baa.Count);
        Assert.Equal(new[] { (1, 1, 1, 1), (2, 2, 2, 2), (3, 3, 3, 3) }, GA(baa.GetBoxa(0, L.L_CLONE)));
        Assert.Equal(new[] { (4, 4, 4, 4), (5, 5, 5, 5), (6, 6, 6, 6) }, GA(baa.GetBoxa(1, L.L_CLONE)));
        Assert.Equal(3, boxa.EncapsulateAligned(2, L.L_CLONE).Count);
        Assert.Throws<ArgumentException>(() => boxa.EncapsulateAligned(3, L.L_INSERT));
    }

    [Fact]
    public void EncapsulateAligned_RemainderIsDropped()
    {
        // 5 boxes, num = 2: the error is logged and 2 boxa of 2 boxes are made
        Boxa boxa = MakeBoxa((1, 1, 1, 1), (2, 2, 2, 2), (3, 3, 3, 3), (4, 4, 4, 4), (5, 5, 5, 5));
        Boxaa baa = boxa.EncapsulateAligned(2, L.L_COPY);
        Assert.Equal(2, baa.Count);
        Assert.Equal(new[] { (3, 3, 3, 3), (4, 4, 4, 4) }, GA(baa.GetBoxa(1, L.L_CLONE)));
    }

    [Fact]
    public void Transpose_SwapsRowsAndColumns()
    {
        Boxaa baas = Boxaa.Create(0);
        baas.AddBoxa(MakeBoxa((1, 1, 1, 1), (2, 2, 2, 2), (3, 3, 3, 3)), L.L_INSERT);
        baas.AddBoxa(MakeBoxa((4, 4, 4, 4), (5, 5, 5, 5), (6, 6, 6, 6)), L.L_INSERT);
        Boxaa baad = baas.Transpose();
        Assert.Equal(3, baad.Count);
        Assert.Equal(new[] { (1, 1, 1, 1), (4, 4, 4, 4) }, GA(baad.GetBoxa(0, L.L_CLONE)));
        Assert.Equal(new[] { (2, 2, 2, 2), (5, 5, 5, 5) }, GA(baad.GetBoxa(1, L.L_CLONE)));
        Assert.Equal(new[] { (3, 3, 3, 3), (6, 6, 6, 6) }, GA(baad.GetBoxa(2, L.L_CLONE)));
        // transposing twice gives the original arrangement
        Boxaa back = baad.Transpose();
        Assert.Equal(GA(baas.GetBoxa(1, L.L_CLONE)), GA(back.GetBoxa(1, L.L_CLONE)));
    }

    [Fact]
    public void Transpose_InvalidInputs()
    {
        Assert.Throws<ArgumentException>(() => Boxaa.Create(0).Transpose());
        Boxaa baas = Boxaa.Create(0);
        baas.AddBoxa(MakeBoxa((1, 1, 1, 1), (2, 2, 2, 2)), L.L_INSERT);
        baas.AddBoxa(MakeBoxa((4, 4, 4, 4)), L.L_INSERT);
        Assert.Throws<ArgumentException>(() => baas.Transpose());
    }

    [Fact]
    public void AlignBox_NoBoxGivesNextIndex()
    {
        Box box = B(0, 10, 5, 10);
        Assert.Equal(0, Boxaa.Create(0).AlignBox(box, 0));
        // a boxa without boxes is skipped (with a warning)
        Boxaa baa = Boxaa.Create(0);
        baa.AddBoxa(Boxa.Create(0), L.L_INSERT);
        Assert.Equal(1, baa.AlignBox(box, 0));
    }

    private static Boxaa AlignFixture()
    {
        Boxaa baa = Boxaa.Create(0);
        baa.AddBoxa(MakeBoxa((0, 10, 5, 10)), L.L_INSERT);   // y 10..19
        baa.AddBoxa(MakeBoxa((0, 40, 5, 10)), L.L_INSERT);   // y 40..49
        return baa;
    }

    [Fact]
    public void AlignBox_PicksBestOverlap()
    {
        Boxaa baa = AlignFixture();
        // y 12..21: overlap with boxa 0 is 10 + 10 - 1 - 12 = 7
        Assert.Equal(0, baa.AlignBox(B(0, 12, 5, 10), 0));
        // y 42..46: overlap with boxa 1 is 40 + 10 - 1 - 42 = 7
        Assert.Equal(1, baa.AlignBox(B(0, 42, 5, 5), 0));
    }

    [Fact]
    public void AlignBox_DeltaPermitsMisses()
    {
        Boxaa baa = AlignFixture();
        // y 25..29: overlap with boxa 0 is 10 + 10 - 1 - 25 = -6 (best), with boxa 1 it is -11
        Box box = B(0, 25, 5, 5);
        Assert.Equal(2, baa.AlignBox(box, 0));   // no alignment: index of the next boxa
        Assert.Equal(2, baa.AlignBox(box, 5));
        Assert.Equal(0, baa.AlignBox(box, 6));
    }
}
