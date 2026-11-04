using System;
using Leptonica;

namespace Leptonica.Managed.Tests;

/// <summary>
/// Tests for boxfunc1.c with hand-computed expected values.  Functions that need
/// callees in files that are not ported yet (Numa, Pta, boxaGetExtent, ...) are tested
/// only up to the point where those callees are reached.
/// </summary>
public class BoxFunc1Tests
{
    private static Box B(int x, int y, int w, int h) => Box.Create(x, y, w, h);

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

    /*------------------------------ Contains ------------------------------*/

    [Fact]
    public void Contains_InsideEqualAndPartial()
    {
        Box outer = B(0, 0, 10, 10);
        Assert.True(outer.Contains(B(2, 2, 5, 5)));
        Assert.True(outer.Contains(B(0, 0, 10, 10)));   // equal boxes
        Assert.True(outer.Contains(B(9, 9, 1, 1)));     // last pixel
        Assert.False(outer.Contains(B(5, 5, 6, 6)));    // sticks out right and bottom by 1
        Assert.False(outer.Contains(B(0, 0, 11, 10)));
        Assert.False(B(2, 2, 5, 5).Contains(outer));    // not symmetric
    }

    [Fact]
    public void Contains_NegativeCoordinates()
    {
        Box outer = Neg(-5, -5, 20, 20);     // spans [-5, 14]
        Assert.True(outer.Contains(B(0, 0, 10, 10)));
        Assert.True(outer.Contains(Neg(-5, -5, 20, 20)));
        Assert.False(outer.Contains(Neg(-6, 0, 3, 3)));
        Assert.False(outer.Contains(B(0, 0, 20, 5)));   // right edge 20 > 15
    }

    [Fact]
    public void Contains_InvalidBoxThrows()
    {
        Box zero = B(5, 5, 0, 0);
        Assert.Throws<ArgumentException>(() => zero.Contains(B(1, 1, 1, 1)));
        Assert.Throws<ArgumentException>(() => B(0, 0, 10, 10).Contains(zero));
        Assert.Throws<ArgumentException>(() => B(0, 0, 10, 10).Contains(B(1, 1, 0, 3)));
    }

    /*----------------------------- Intersects -----------------------------*/

    [Fact]
    public void Intersects_EdgesAndCorners()
    {
        Box box = B(0, 0, 10, 10);                      // pixels 0..9
        Assert.True(box.Intersects(B(9, 9, 5, 5)));     // share the pixel (9, 9)
        Assert.False(box.Intersects(B(10, 0, 5, 5)));   // adjacent, no common pixel
        Assert.False(box.Intersects(B(0, 10, 5, 5)));
        Assert.True(box.Intersects(B(2, 2, 3, 3)));     // contained
        Assert.True(B(2, 2, 3, 3).Intersects(box));     // symmetric
        Assert.False(B(10, 0, 5, 5).Intersects(box));
    }

    [Fact]
    public void Intersects_NegativeCoordinates()
    {
        Box left = Neg(-3, 0, 3, 5);             // pixels -3..-1
        Assert.False(left.Intersects(B(0, 0, 5, 5)));
        Assert.True(Neg(-3, 0, 4, 5).Intersects(B(0, 0, 5, 5)));    // pixels -3..0
    }

    [Fact]
    public void Intersects_InvalidBoxThrows()
    {
        Assert.Throws<ArgumentException>(() => B(0, 0, 0, 5).Intersects(B(0, 0, 5, 5)));
        Assert.Throws<ArgumentException>(() => B(0, 0, 5, 5).Intersects(B(1, 1, 5, 0)));
    }

    /*---------------------- Boxa containment / intersection ---------------------*/

    private static Boxa SelectionBoxa() => MakeBoxa(
        (1, 1, 3, 3),      // inside
        (0, 0, 20, 20),    // contains the selection box
        (5, 5, 2, 2),      // inside
        (9, 9, 0, 0),      // invalid placeholder
        (8, 8, 5, 5));     // sticks out

    [Fact]
    public void ContainedInBox_SelectsContainedValidBoxes()
    {
        Boxa result = SelectionBoxa().ContainedInBox(B(0, 0, 10, 10));
        Assert.Equal(2, result.Count);
        Assert.Equal((1, 1, 3, 3), G(result, 0));
        Assert.Equal((5, 5, 2, 2), G(result, 1));
    }

    [Fact]
    public void ContainedInBox_ReturnsCopies()
    {
        Boxa boxas = SelectionBoxa();
        Boxa result = boxas.ContainedInBox(B(0, 0, 10, 10));
        result.GetBox(0, L.L_CLONE).X = 7;
        Assert.Equal((1, 1, 3, 3), G(boxas, 0));
    }

    [Fact]
    public void ContainedInBox_InvalidBoxOrEmptyBoxaGivesEmpty()
    {
        Assert.Equal(0, SelectionBoxa().ContainedInBox(B(0, 0, 0, 10)).Count);
        Assert.Equal(0, Boxa.Create(0).ContainedInBox(B(0, 0, 10, 10)).Count);
    }

    [Fact]
    public void ContainedInBoxCount_CountsValidContainedBoxes()
    {
        Boxa boxas = SelectionBoxa();
        Assert.Equal(2, boxas.ContainedInBoxCount(B(0, 0, 10, 10)));
        Assert.Equal(4, boxas.ContainedInBoxCount(B(0, 0, 20, 20)));  // all valid ones
        Assert.Equal(0, boxas.ContainedInBoxCount(B(30, 30, 5, 5)));
        Assert.Equal(0, boxas.ContainedInBoxCount(B(3, 3, 0, 0)));    // invalid box
        Assert.Equal(0, Boxa.Create(0).ContainedInBoxCount(B(0, 0, 10, 10)));
    }

    [Fact]
    public void ContainedInBoxa_EveryBoxMustHaveAContainer()
    {
        Boxa boxa1 = MakeBoxa((0, 0, 10, 10), (20, 20, 10, 10));
        Assert.True(boxa1.ContainedInBoxa(MakeBoxa((1, 1, 2, 2), (25, 25, 3, 3))));
        Assert.False(boxa1.ContainedInBoxa(MakeBoxa((1, 1, 2, 2), (15, 15, 2, 2))));
        Assert.False(boxa1.ContainedInBoxa(MakeBoxa((5, 5, 10, 10))));   // straddles the first box
    }

    [Fact]
    public void ContainedInBoxa_EdgeCases()
    {
        Boxa boxa1 = MakeBoxa((0, 0, 10, 10));
        Assert.True(boxa1.ContainedInBoxa(Boxa.Create(0)));                  // nothing to contain
        Assert.True(boxa1.ContainedInBoxa(MakeBoxa((50, 50, 0, 0))));        // invalid box is skipped
        Assert.False(Boxa.Create(0).ContainedInBoxa(MakeBoxa((1, 1, 2, 2))));
        // an invalid box in boxa1 cannot contain anything
        Assert.False(MakeBoxa((1, 1, 0, 0)).ContainedInBoxa(MakeBoxa((1, 1, 1, 1))));
    }

    [Fact]
    public void IntersectsBox_SelectsIntersectingBoxes()
    {
        Boxa boxas = MakeBoxa((9, 9, 5, 5), (10, 10, 5, 5), (20, 0, 3, 3), (5, 5, 1, 1), (4, 4, 0, 0));
        Boxa result = boxas.IntersectsBox(B(0, 0, 10, 10));
        Assert.Equal(2, result.Count);
        Assert.Equal((9, 9, 5, 5), G(result, 0));
        Assert.Equal((5, 5, 1, 1), G(result, 1));
        Assert.Equal(2, boxas.IntersectsBoxCount(B(0, 0, 10, 10)));
    }

    [Fact]
    public void IntersectsBox_InvalidBoxOrEmptyBoxaGivesEmpty()
    {
        Boxa boxas = MakeBoxa((0, 0, 5, 5));
        Assert.Equal(0, boxas.IntersectsBox(B(0, 0, 5, 0)).Count);
        Assert.Equal(0, boxas.IntersectsBoxCount(B(0, 0, 0, 5)));
        Assert.Equal(0, Boxa.Create(0).IntersectsBox(B(0, 0, 5, 5)).Count);
        Assert.Equal(0, Boxa.Create(0).IntersectsBoxCount(B(0, 0, 5, 5)));
    }

    [Fact]
    public void ClipToBox_ClipsAndDropsDisjointBoxes()
    {
        // clip box covers pixels 5..14 in x and y
        Boxa boxas = MakeBoxa((0, 0, 10, 10), (10, 10, 10, 10), (20, 20, 2, 2), (6, 6, 2, 2), (7, 7, 0, 0));
        Boxa result = boxas.ClipToBox(B(5, 5, 10, 10));
        Assert.Equal(3, result.Count);
        Assert.Equal((5, 5, 5, 5), G(result, 0));
        Assert.Equal((10, 10, 5, 5), G(result, 1));
        Assert.Equal((6, 6, 2, 2), G(result, 2));
    }

    [Fact]
    public void ClipToBox_InvalidBoxOrEmptyBoxaGivesEmpty()
    {
        Assert.Equal(0, MakeBoxa((0, 0, 5, 5)).ClipToBox(B(0, 0, 0, 0)).Count);
        Assert.Equal(0, Boxa.Create(0).ClipToBox(B(0, 0, 5, 5)).Count);
    }

    /*----------------------------- CombineOverlaps ----------------------------*/

    [Fact]
    public void CombineOverlaps_MergesChainsOfOverlaps()
    {
        // 0 and 1 overlap -> (0,0,15,15); that one reaches pixel 14 and overlaps box 3 (14,14,4,4)
        // -> (0,0,18,18).  Box 2 stays alone.
        Boxa boxas = MakeBoxa((0, 0, 10, 10), (5, 5, 10, 10), (30, 30, 5, 5), (14, 14, 4, 4));
        Boxa result = boxas.CombineOverlaps();
        Assert.Equal(2, result.Count);
        Assert.Equal((0, 0, 18, 18), G(result, 0));
        Assert.Equal((30, 30, 5, 5), G(result, 1));
        Assert.Equal(4, boxas.Count);                       // the input is not changed
        Assert.Equal((0, 0, 10, 10), G(boxas, 0));
    }

    [Fact]
    public void CombineOverlaps_NeedsSeveralIterations()
    {
        // The first pass merges 0 and 2 (through the loop order) and leaves 1, which touches only
        // the merged box; the second pass joins it.
        Boxa boxas = MakeBoxa((0, 0, 4, 4), (10, 0, 4, 4), (3, 0, 8, 4));
        // i=0: j=1 no overlap, j=2 overlaps -> (0,0,11,4); i=1: (10,0,4,4) vs invalid j=2 -> skipped
        // after pass 1: boxa = [(0,0,11,4), (10,0,4,4)] still overlapping -> pass 2 -> (0,0,14,4)
        Boxa result = boxas.CombineOverlaps();
        Assert.Equal(1, result.Count);
        Assert.Equal((0, 0, 14, 4), G(result, 0));
    }

    [Fact]
    public void CombineOverlaps_TouchingBoxesAreNotCombined()
    {
        Boxa boxas = MakeBoxa((0, 0, 5, 5), (5, 0, 5, 5));
        Boxa result = boxas.CombineOverlaps();
        Assert.Equal(2, result.Count);
        Assert.Equal((0, 0, 5, 5), G(result, 0));
        Assert.Equal((5, 0, 5, 5), G(result, 1));
    }

    [Fact]
    public void CombineOverlaps_DropsInvalidBoxesAndHandlesEmpty()
    {
        Boxa result = MakeBoxa((0, 0, 5, 5), (2, 2, 0, 0), (20, 20, 5, 5)).CombineOverlaps();
        Assert.Equal(2, result.Count);
        Assert.Equal((0, 0, 5, 5), G(result, 0));
        Assert.Equal((20, 20, 5, 5), G(result, 1));
        Assert.Equal(0, Boxa.Create(0).CombineOverlaps().Count);
    }

    /*------------------------ OverlapRegion / BoundingRegion -----------------------*/

    [Fact]
    public void OverlapRegion_IsTheIntersection()
    {
        Assert.Equal((5, 5, 5, 5), G(B(0, 0, 10, 10).OverlapRegion(B(5, 5, 10, 10))!));
        Assert.Equal((2, 3, 4, 5), G(B(0, 0, 10, 10).OverlapRegion(B(2, 3, 4, 5))!));
        Assert.Equal((2, 3, 4, 5), G(B(2, 3, 4, 5).OverlapRegion(B(0, 0, 10, 10))!));
        Assert.Equal((9, 9, 1, 1), G(B(0, 0, 10, 10).OverlapRegion(B(9, 9, 5, 5))!));   // corner pixel
    }

    [Fact]
    public void OverlapRegion_DisjointOrInvalidGivesNull()
    {
        Assert.Null(B(0, 0, 10, 10).OverlapRegion(B(10, 0, 5, 5)));
        Assert.Null(B(0, 0, 10, 10).OverlapRegion(B(0, 20, 5, 5)));
        Assert.Null(B(0, 0, 10, 10).OverlapRegion(B(2, 2, 0, 0)));
        Assert.Null(B(2, 2, 0, 3).OverlapRegion(B(0, 0, 10, 10)));
    }

    [Fact]
    public void OverlapRegion_NegativeCoordinates()
    {
        // pixels -4..3 and 0..9 overlap in 0..3
        Assert.Equal((0, 0, 4, 4), G(Neg(-4, -4, 8, 8).OverlapRegion(B(0, 0, 10, 10))!));
        // the overlap starts at -2; boxCreate() clips it to the +quad: x = 0, w = 4 - 2 = 2
        Assert.Equal((0, 0, 2, 2), G(Neg(-4, -4, 8, 8).OverlapRegion(Neg(-2, -2, 4, 4))!));
    }

    [Fact]
    public void BoundingRegion_IsTheUnion()
    {
        Assert.Equal((0, 0, 25, 10), G(B(0, 0, 10, 10).BoundingRegion(B(20, 5, 5, 5))));
        Assert.Equal((0, 0, 10, 10), G(B(0, 0, 10, 10).BoundingRegion(B(2, 2, 3, 3))));
        Assert.Equal((3, 4, 12, 7), G(B(10, 4, 5, 5).BoundingRegion(B(3, 6, 4, 5))));
    }

    [Fact]
    public void BoundingRegion_IgnoresInvalidBoxes()
    {
        Box bounding = B(3, 4, 5, 6).BoundingRegion(B(100, 100, 0, 0));
        Assert.Equal((3, 4, 5, 6), G(bounding));
        Assert.Equal((3, 4, 5, 6), G(B(100, 100, 0, 7).BoundingRegion(B(3, 4, 5, 6))));
        Assert.Equal((0, 0, 0, 0), G(B(1, 1, 0, 0).BoundingRegion(B(2, 2, 0, 5))));
        // the result of a lone valid box is a copy
        Box lone = B(3, 4, 5, 6);
        Assert.NotSame(lone, lone.BoundingRegion(B(0, 0, 0, 0)));
    }

    /*--------------------------- Overlap fraction / area ---------------------------*/

    [Fact]
    public void OverlapFraction_IsFractionOfBox2()
    {
        Assert.Equal(0.25f, B(0, 0, 10, 10).OverlapFraction(B(5, 5, 10, 10)));
        Assert.Equal(0.25f, B(5, 5, 5, 5).OverlapFraction(B(0, 0, 10, 10)));
        Assert.Equal(1.0f, B(0, 0, 10, 10).OverlapFraction(B(5, 5, 5, 5)));
        // order matters: here the overlap (5,5,5,5) is all of box2
        Assert.Equal(1.0f, B(0, 0, 10, 10).OverlapFraction(B(5, 5, 5, 5)));
        Assert.Equal(0.25f, B(5, 5, 5, 5).OverlapFraction(B(0, 0, 10, 10)));
    }

    [Fact]
    public void OverlapFraction_UsesFloatDivision()
    {
        // overlap (2,0,1,1) of area 1, box2 area 3
        Assert.Equal((float)1 / (float)3, B(2, 0, 5, 1).OverlapFraction(B(0, 0, 3, 1)));
    }

    [Fact]
    public void OverlapFraction_NoOverlapOrInvalidIsZero()
    {
        Assert.Equal(0.0f, B(0, 0, 10, 10).OverlapFraction(B(10, 10, 5, 5)));
        Assert.Equal(0.0f, B(0, 0, 10, 10).OverlapFraction(B(1, 1, 0, 5)));
        Assert.Equal(0.0f, B(1, 1, 5, 0).OverlapFraction(B(0, 0, 10, 10)));
    }

    [Fact]
    public void OverlapArea_CountsOverlapPixels()
    {
        Assert.Equal(25, B(0, 0, 10, 10).OverlapArea(B(5, 5, 10, 10)));
        Assert.Equal(1, B(0, 0, 10, 10).OverlapArea(B(9, 9, 10, 10)));
        Assert.Equal(12, B(0, 0, 10, 10).OverlapArea(B(2, 3, 4, 3)));
        Assert.Equal(0, B(0, 0, 10, 10).OverlapArea(B(10, 0, 4, 4)));
        Assert.Equal(16, Neg(-4, -4, 8, 8).OverlapArea(B(0, 0, 10, 10)));
    }

    [Fact]
    public void OverlapArea_InvalidBoxThrows()
    {
        Assert.Throws<ArgumentException>(() => B(0, 0, 10, 10).OverlapArea(B(0, 0, 0, 0)));
        Assert.Throws<ArgumentException>(() => B(0, 0, 0, 10).OverlapArea(B(0, 0, 4, 4)));
    }

    /*------------------------------ HandleOverlaps -----------------------------*/

    [Fact]
    public void HandleOverlaps_InvalidOpThrows()
    {
        Assert.Throws<ArgumentException>(
            () => MakeBoxa((0, 0, 5, 5)).HandleOverlaps(0, 1, 0.0f, 1.0f, out _));
        Assert.Throws<ArgumentException>(
            () => MakeBoxa((0, 0, 5, 5)).HandleOverlaps(3, 1, 0.0f, 1.0f, out _));
    }

    [Fact]
    public void HandleOverlaps_EmptyBoxaAndZeroRange()
    {
        Boxa empty = Boxa.Create(0).HandleOverlaps(L.L_COMBINE, 5, 0.0f, 1.0f, out Numa? namap);
        Assert.Equal(0, empty.Count);
        Assert.Null(namap);

        Boxa boxas = MakeBoxa((0, 0, 5, 5), (2, 2, 5, 5));
        Boxa copy = boxas.HandleOverlaps(L.L_REMOVE_SMALL, 0, 0.0f, 1.0f, out namap);
        Assert.Null(namap);
        Assert.NotSame(boxas, copy);
        Assert.Equal(2, copy.Count);
        Assert.Equal((2, 2, 5, 5), G(copy, 1));
    }

    [Fact]
    public void HandleOverlaps_CombineOverlappingPair()
    {
        /* Equal areas: the later box is the smaller one (na[1] = 0).  The first box
         * becomes the bounding region (0,0)-(6,6) of the two. */
        Boxa boxad = MakeBoxa((0, 0, 5, 5), (2, 2, 5, 5))
            .HandleOverlaps(L.L_COMBINE, 3, 0.0f, 1.0f, out Numa? namap);
        Assert.Equal(1, boxad.Count);
        Assert.Equal((0, 0, 7, 7), G(boxad, 0));
        Assert.NotNull(namap);
        Assert.Equal(new[] { -1, 0 }, new[] { namap!.GetIValue(0), namap.GetIValue(1) });
    }

    /*------------------------ Overlap and separation distance -----------------------*/

    [Fact]
    public void OverlapDistance_Horizontal()
    {
        Box box1 = B(0, 0, 10, 10);
        box1.OverlapDistance(B(5, 5, 10, 10), out int h, out int v);
        Assert.Equal((5, 5), (h, v));
        box1.OverlapDistance(B(10, 0, 5, 5), out h, out v);     // adjacent in x
        Assert.Equal((0, 10), (h, v));
        box1.OverlapDistance(B(20, 30, 5, 5), out h, out v);    // separated
        Assert.Equal((-10, -20), (h, v));
    }

    [Fact]
    public void OverlapDistance_BoxOrderDoesNotMatter()
    {
        Box box1 = B(5, 5, 10, 10);
        Box box2 = B(0, 0, 10, 10);
        box1.OverlapDistance(box2, out int h1, out int v1);
        box2.OverlapDistance(box1, out int h2, out int v2);
        Assert.Equal((5, 5), (h1, v1));
        Assert.Equal((h1, v1), (h2, v2));
    }

    [Fact]
    public void OverlapDistance_InvalidBoxThrows()
    {
        Assert.Throws<ArgumentException>(() => B(0, 0, 0, 5).OverlapDistance(B(0, 0, 5, 5), out _, out _));
        Assert.Throws<ArgumentException>(() => B(0, 0, 5, 5).OverlapDistance(B(0, 0, 5, 0), out _, out _));
    }

    [Fact]
    public void SeparationDistance_IsZeroOnlyForPositiveOverlap()
    {
        Box box1 = B(0, 0, 10, 10);
        box1.SeparationDistance(B(5, 5, 10, 10), out int h, out int v);
        Assert.Equal((0, 0), (h, v));
        box1.SeparationDistance(B(20, 30, 5, 5), out h, out v);          // -ho + 1, -vo + 1
        Assert.Equal((11, 21), (h, v));
        box1.SeparationDistance(B(10, 0, 5, 5), out h, out v);           // adjacent pixels are 1 apart
        Assert.Equal((1, 0), (h, v));
        B(20, 30, 5, 5).SeparationDistance(box1, out h, out v);
        Assert.Equal((11, 21), (h, v));
    }

    [Fact]
    public void SeparationDistance_InvalidBoxThrows()
    {
        Assert.Throws<ArgumentException>(() => B(0, 0, 0, 0).SeparationDistance(B(0, 0, 5, 5), out _, out _));
    }

    /*-------------------------------- CompareSize -------------------------------*/

    [Fact]
    public void CompareSize_ByEachType()
    {
        Box wide = B(0, 0, 10, 2);     // w 10, h 2, max 10, perimeter 12, area 20
        Box tall = B(0, 0, 5, 6);      // w 5,  h 6, max 6,  perimeter 11, area 30
        Assert.Equal(1, wide.CompareSize(tall, L.L_SORT_BY_WIDTH));
        Assert.Equal(-1, tall.CompareSize(wide, L.L_SORT_BY_WIDTH));
        Assert.Equal(-1, wide.CompareSize(tall, L.L_SORT_BY_HEIGHT));
        Assert.Equal(1, wide.CompareSize(tall, L.L_SORT_BY_MAX_DIMENSION));
        Assert.Equal(1, wide.CompareSize(tall, L.L_SORT_BY_PERIMETER));
        Assert.Equal(-1, wide.CompareSize(tall, L.L_SORT_BY_AREA));
    }

    [Fact]
    public void CompareSize_EqualValues()
    {
        Box a = B(0, 0, 4, 9);
        Box b = B(50, 60, 9, 4);          // same size rotated: equal perimeter, area, max dimension
        Assert.Equal(0, a.CompareSize(b, L.L_SORT_BY_MAX_DIMENSION));
        Assert.Equal(0, a.CompareSize(b, L.L_SORT_BY_PERIMETER));
        Assert.Equal(0, a.CompareSize(b, L.L_SORT_BY_AREA));
        Assert.Equal(-1, a.CompareSize(b, L.L_SORT_BY_WIDTH));
        Assert.Equal(1, a.CompareSize(b, L.L_SORT_BY_HEIGHT));
    }

    [Fact]
    public void CompareSize_Errors()
    {
        Assert.Throws<ArgumentException>(() => B(0, 0, 4, 4).CompareSize(B(0, 0, 4, 4), L.L_SORT_BY_X));
        Assert.Throws<ArgumentException>(() => B(0, 0, 4, 4).CompareSize(B(0, 0, 4, 4), L.L_SORT_BY_MIN_DIMENSION));
        Assert.Throws<ArgumentException>(() => B(0, 0, 0, 4).CompareSize(B(0, 0, 4, 4), L.L_SORT_BY_AREA));
        Assert.Throws<ArgumentException>(() => B(0, 0, 4, 4).CompareSize(B(0, 0, 4, 0), L.L_SORT_BY_AREA));
    }

    /*-------------------------------- ContainsPt --------------------------------*/

    [Fact]
    public void ContainsPt_HalfOpenInterval()
    {
        Box box = B(10, 20, 5, 5);         // x in [10, 15), y in [20, 25)
        Assert.True(box.ContainsPt(10.0f, 20.0f));
        Assert.True(box.ContainsPt(14.99f, 24.99f));
        Assert.False(box.ContainsPt(15.0f, 22.0f));
        Assert.False(box.ContainsPt(12.0f, 25.0f));
        Assert.False(box.ContainsPt(9.99f, 22.0f));
        Assert.False(box.ContainsPt(12.0f, 19.99f));
    }

    [Fact]
    public void ContainsPt_NegativeCoordinatesAndZeroSizeBox()
    {
        Box box = Neg(-3, -3, 6, 6);       // [-3, 3)
        Assert.True(box.ContainsPt(-3.0f, -3.0f));
        Assert.False(box.ContainsPt(-3.5f, 0.0f));
        Assert.True(box.ContainsPt(2.99f, 0.0f));
        Assert.False(box.ContainsPt(3.0f, 0.0f));
        Assert.False(B(5, 5, 0, 0).ContainsPt(5.0f, 5.0f));    // an empty box contains no point
    }

    /*------------------------ GetNearestToPt / ToLine -------------------------*/

    // centers: (5,5) (25,5) (5,25) (25,25)
    private static Boxa Grid() => MakeBoxa((0, 0, 10, 10), (20, 0, 10, 10), (0, 20, 10, 10), (20, 20, 10, 10));

    [Fact]
    public void GetNearestToPt_PicksClosestCentroid()
    {
        Boxa grid = Grid();
        Assert.Equal((20, 0, 10, 10), G(grid.GetNearestToPt(24, 6)));
        Assert.Equal((0, 20, 10, 10), G(grid.GetNearestToPt(4, 24)));
        Assert.Equal((20, 20, 10, 10), G(grid.GetNearestToPt(100, 100)));
        Assert.Equal((0, 0, 10, 10), G(grid.GetNearestToPt(-50, -50)));
    }

    [Fact]
    public void GetNearestToPt_TieGoesToTheFirstBoxAndResultIsACopy()
    {
        Boxa grid = Grid();
        // (15, 5) is 10 away from the centers of boxes 0 and 1
        Box nearest = grid.GetNearestToPt(15, 5);
        Assert.Equal((0, 0, 10, 10), G(nearest));
        nearest.X = 99;
        Assert.Equal((0, 0, 10, 10), G(grid, 0));
    }

    [Fact]
    public void GetNearestToPt_SkipsInvalidBoxes()
    {
        Boxa boxa = MakeBoxa((30, 30, 0, 0), (50, 50, 10, 10));
        Assert.Equal((50, 50, 10, 10), G(boxa.GetNearestToPt(30, 30)));
        // with only invalid boxes, the first box is returned
        Assert.Equal((30, 30, 0, 0), G(MakeBoxa((30, 30, 0, 0), (1, 1, 0, 0)).GetNearestToPt(1, 1)));
    }

    [Fact]
    public void GetNearestToPt_EmptyBoxaThrows()
    {
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).GetNearestToPt(1, 1));
    }

    [Fact]
    public void GetNearestToLine_VerticalAndHorizontal()
    {
        Boxa grid = Grid();
        Assert.Equal((20, 0, 10, 10), G(grid.GetNearestToLine(24, -1)));    // vertical line x = 24
        Assert.Equal((0, 20, 10, 10), G(grid.GetNearestToLine(-1, 22)));    // horizontal line y = 22
        Assert.Equal((0, 0, 10, 10), G(grid.GetNearestToLine(10, -1)));     // tie 5 / 15: closer is 5
    }

    [Fact]
    public void GetNearestToLine_Errors()
    {
        Boxa grid = Grid();
        Assert.Throws<ArgumentException>(() => grid.GetNearestToLine(5, 5));
        Assert.Throws<ArgumentException>(() => grid.GetNearestToLine(-1, -1));
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).GetNearestToLine(5, -1));
    }

    /*------------------------------ GetNearestByDirection -----------------------------*/

    // A(0,0,10,10) B(15,0,10,10) C(40,0,10,10) D(0,30,10,10)
    private static Boxa Row() => MakeBoxa((0, 0, 10, 10), (15, 0, 10, 10), (40, 0, 10, 10), (0, 30, 10, 10));

    [Fact]
    public void GetNearestByDirection_LeftRight()
    {
        Boxa row = Row();
        row.GetNearestByDirection(1, L.L_FROM_LEFT, L.L_ALL, 0, out int index, out int dist);
        Assert.Equal((0, 6), (index, dist));        // A: 15 - (0 + 10 - 1)
        row.GetNearestByDirection(1, L.L_FROM_RIGHT, L.L_ALL, 0, out index, out dist);
        Assert.Equal((2, 16), (index, dist));       // C: 40 - (15 + 10 - 1); D has no vertical overlap
    }

    [Fact]
    public void GetNearestByDirection_TopBottom()
    {
        Boxa row = Row();
        row.GetNearestByDirection(3, L.L_FROM_TOP, L.L_ALL, 0, out int index, out int dist);
        Assert.Equal((0, 21), (index, dist));       // A: 30 - (0 + 10 - 1)
        row.GetNearestByDirection(0, L.L_FROM_BOT, L.L_ALL, 0, out index, out dist);
        Assert.Equal((3, 21), (index, dist));       // D: 30 - (0 + 10 - 1)
    }

    [Fact]
    public void GetNearestByDirection_NoBoxGivesDefaults()
    {
        Boxa row = Row();
        row.GetNearestByDirection(1, L.L_FROM_TOP, L.L_ALL, 0, out int index, out int dist);
        Assert.Equal((-1, 100000), (index, dist));
        row.GetNearestByDirection(1, L.L_FROM_BOT, L.L_ALL, 0, out index, out dist);   // D does not overlap in x
        Assert.Equal((-1, 100000), (index, dist));
    }

    [Fact]
    public void GetNearestByDirection_OverlappingBoxesHaveNegativeDistance()
    {
        Boxa boxa = MakeBoxa((0, 0, 10, 10), (8, 0, 10, 10));
        boxa.GetNearestByDirection(1, L.L_FROM_LEFT, L.L_ALL, 0, out int index, out int dist);
        Assert.Equal((0, -1), (index, dist));       // 8 - (0 + 10 - 1)
        boxa.GetNearestByDirection(1, L.L_FROM_LEFT, L.L_NON_NEGATIVE, 0, out index, out dist);
        Assert.Equal((-1, 100000), (index, dist));
    }

    [Fact]
    public void GetNearestByDirection_RangeLimitsTheSearch()
    {
        Boxa boxa = MakeBoxa((15, 0, 10, 10), (100, 100, 5, 5), (120, 100, 5, 5), (40, 0, 10, 10));
        boxa.GetNearestByDirection(3, L.L_FROM_LEFT, L.L_ALL, 1, out int index, out int dist);
        Assert.Equal((-1, 100000), (index, dist));  // only boxes 2 and 3 are looked at
        boxa.GetNearestByDirection(3, L.L_FROM_LEFT, L.L_ALL, 0, out index, out dist);
        Assert.Equal((0, 16), (index, dist));       // 40 - (15 + 10 - 1)
        boxa.GetNearestByDirection(3, L.L_FROM_LEFT, L.L_ALL, 3, out index, out dist);
        Assert.Equal((0, 16), (index, dist));
    }

    [Fact]
    public void GetNearestByDirection_Errors()
    {
        Boxa row = Row();
        Assert.Throws<ArgumentException>(() => row.GetNearestByDirection(0, 9, L.L_ALL, 0, out _, out _));
        Assert.Throws<ArgumentException>(() => row.GetNearestByDirection(0, L.L_FROM_LEFT, 1, 0, out _, out _));
        Assert.Throws<ArgumentException>(() => row.GetNearestByDirection(4, L.L_FROM_LEFT, L.L_ALL, 0, out _, out _));
        Assert.Throws<ArgumentException>(() => row.GetNearestByDirection(-1, L.L_FROM_LEFT, L.L_ALL, 0, out _, out _));
    }

    /*-------------------------------- GetCenter ---------------------------------*/

    [Fact]
    public void GetCenter_UsesDoubleArithmeticThenFloat()
    {
        Assert.True(B(10, 20, 4, 6).GetCenter(out float cx, out float cy));
        Assert.Equal((12.0f, 23.0f), (cx, cy));
        Assert.True(B(1, 1, 2, 3).GetCenter(out cx, out cy));
        Assert.Equal((2.0f, 2.5f), (cx, cy));
        Assert.True(B(0, 0, 1, 1).GetCenter(out cx, out cy));
        Assert.Equal((0.5f, 0.5f), (cx, cy));
        Assert.True(Neg(-4, -6, 8, 4).GetCenter(out cx, out cy));
        Assert.Equal((0.0f, -4.0f), (cx, cy));
    }

    [Fact]
    public void GetCenter_NoAreaReturnsFalseAndZeros()
    {
        Assert.False(B(7, 9, 0, 5).GetCenter(out float cx, out float cy));
        Assert.Equal((0.0f, 0.0f), (cx, cy));
        Assert.False(B(7, 9, 5, 0).GetCenter(out cx, out cy));
        Assert.Equal((0.0f, 0.0f), (cx, cy));
    }

    /*------------------------------ IntersectByLine ----------------------------*/

    [Fact]
    public void IntersectByLine_HorizontalLine()
    {
        Box box = B(10, 20, 30, 40);       // x in 10..39, y in 20..59
        Assert.True(box.IntersectByLine(0, 30, 0.0f, out int x1, out int y1, out int x2, out int y2, out int n));
        Assert.Equal((10, 30, 39, 30), (x1, y1, x2, y2));
        Assert.Equal(0, n);                // the C code does not set n for horizontal lines
        Assert.True(box.IntersectByLine(0, 59, 0.0f, out x1, out y1, out x2, out y2, out _));
        Assert.Equal((10, 59, 39, 59), (x1, y1, x2, y2));
        Assert.True(box.IntersectByLine(0, 60, 0.0f, out x1, out y1, out x2, out y2, out _));   // just below
        Assert.Equal((0, 0, 0, 0), (x1, y1, x2, y2));
        Assert.True(box.IntersectByLine(0, 19, 0.0f, out x1, out y1, out x2, out y2, out _));   // just above
        Assert.Equal((0, 0, 0, 0), (x1, y1, x2, y2));
    }

    [Fact]
    public void IntersectByLine_NoAreaReturnsFalse()
    {
        Assert.False(B(10, 20, 0, 40).IntersectByLine(15, 30, 0.0f, out int x1, out int y1, out int x2, out int y2, out int n));
        Assert.Equal((0, 0, 0, 0, 0), (x1, y1, x2, y2, n));
        Assert.False(B(10, 20, 30, 0).IntersectByLine(15, 30, 5.0f, out _, out _, out _, out _, out _));
    }

    /*------------------------------ ClipToRectangle ----------------------------*/

    [Fact]
    public void ClipToRectangle_ClipsToTheImage()
    {
        Assert.Equal((90, 40, 10, 10), G(B(90, 40, 20, 20).ClipToRectangle(100, 50)));
        Assert.Equal((99, 0, 1, 5), G(B(99, 0, 5, 5).ClipToRectangle(100, 50)));      // last column
        Assert.Equal((0, 0, 15, 15), G(Neg(-5, -5, 20, 20).ClipToRectangle(100, 50)));
        Assert.Equal((0, 0, 1, 1), G(Neg(-9, -9, 10, 10).ClipToRectangle(100, 50)));  // last pixel inside
    }

    [Fact]
    public void ClipToRectangle_InsideBoxIsCopiedUnchanged()
    {
        Box box = B(10, 10, 20, 20);
        Box clipped = box.ClipToRectangle(100, 50);
        Assert.NotSame(box, clipped);
        Assert.Equal((10, 10, 20, 20), G(clipped));
    }

    [Fact]
    public void ClipToRectangle_OutsideThrows()
    {
        Assert.Throws<ArgumentException>(() => B(100, 0, 5, 5).ClipToRectangle(100, 50));
        Assert.Throws<ArgumentException>(() => B(0, 50, 5, 5).ClipToRectangle(100, 50));
        Assert.Throws<ArgumentException>(() => Neg(-10, 0, 10, 5).ClipToRectangle(100, 50));
        Assert.Throws<ArgumentException>(() => Neg(0, -5, 5, 5).ClipToRectangle(100, 50));
    }

    [Fact]
    public void ClipToRectangleParams_NoBoxGivesTheWholeRectangle()
    {
        Box.ClipToRectangleParams(null, 100, 50, out int xs, out int ys, out int xe, out int ye, out int bw, out int bh);
        Assert.Equal((0, 0, 100, 50, 100, 50), (xs, ys, xe, ye, bw, bh));
    }

    [Fact]
    public void ClipToRectangleParams_BoxIsClipped()
    {
        Box.ClipToRectangleParams(B(10, 5, 20, 10), 100, 50, out int xs, out int ys, out int xe, out int ye, out int bw, out int bh);
        Assert.Equal((10, 5, 30, 15, 20, 10), (xs, ys, xe, ye, bw, bh));
        Box.ClipToRectangleParams(B(90, 40, 20, 20), 100, 50, out xs, out ys, out xe, out ye, out bw, out bh);
        Assert.Equal((90, 40, 100, 50, 10, 10), (xs, ys, xe, ye, bw, bh));
        Box.ClipToRectangleParams(Neg(-3, -2, 10, 10), 100, 50, out xs, out ys, out xe, out ye, out bw, out bh);
        Assert.Equal((0, 0, 7, 8, 7, 8), (xs, ys, xe, ye, bw, bh));
    }

    [Fact]
    public void ClipToRectangleParams_Errors()
    {
        Assert.Throws<ArgumentException>(
            () => Box.ClipToRectangleParams(B(100, 0, 5, 5), 100, 50, out _, out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(      // inside, but without area
            () => Box.ClipToRectangleParams(B(10, 5, 0, 0), 100, 50, out _, out _, out _, out _, out _, out _));
    }

    /*------------------------------ RelocateOneSide ----------------------------*/

    [Fact]
    public void RelocateOneSide_EachSide()
    {
        Box boxs = B(10, 20, 30, 40);      // right edge 39, bottom edge 59
        Assert.Equal((15, 20, 25, 40), G(Box.RelocateOneSide(null, boxs, 15, L.L_FROM_LEFT)));
        Assert.Equal((10, 20, 41, 40), G(Box.RelocateOneSide(null, boxs, 50, L.L_FROM_RIGHT)));
        Assert.Equal((10, 25, 30, 35), G(Box.RelocateOneSide(null, boxs, 25, L.L_FROM_TOP)));
        Assert.Equal((10, 20, 30, 51), G(Box.RelocateOneSide(null, boxs, 70, L.L_FROM_BOT)));
        Assert.Equal((10, 20, 30, 40), G(boxs));       // the source is not changed
    }

    [Fact]
    public void RelocateOneSide_InPlaceAndOtherDestination()
    {
        Box boxs = B(10, 20, 30, 40);
        Assert.Same(boxs, Box.RelocateOneSide(boxs, boxs, 15, L.L_FROM_LEFT));
        Assert.Equal((15, 20, 25, 40), G(boxs));

        // only the geometry that changes is written into another destination
        Box boxd = B(0, 0, 5, 5);
        Assert.Same(boxd, Box.RelocateOneSide(boxd, B(10, 20, 30, 40), 50, L.L_FROM_RIGHT));
        Assert.Equal((0, 0, 41, 5), G(boxd));
    }

    [Fact]
    public void RelocateOneSide_NoAreaOrUnknownSideChangesNothing()
    {
        Assert.Equal((10, 20, 0, 40), G(Box.RelocateOneSide(null, B(10, 20, 0, 40), 15, L.L_FROM_LEFT)));
        Assert.Equal((10, 20, 30, 40), G(Box.RelocateOneSide(null, B(10, 20, 30, 40), 15, 7)));
    }

    /*------------------------------- AdjustSides -------------------------------*/

    [Fact]
    public void BoxAdjustSides_NewInPlaceAndOther()
    {
        Box boxs = B(10, 20, 30, 40);
        // expanding by 20 on each side is cropped at x >= 0 and y >= 0 (here x = 0; y = 0)
        Assert.Equal((0, 0, 60, 80), G(Box.AdjustSides(null, boxs, -20, 20, -20, 20)));
        Assert.Equal((10, 20, 30, 40), G(boxs));
        Assert.Equal((12, 21, 26, 36), G(Box.AdjustSides(null, boxs, 2, -2, 1, -3)));

        Assert.Same(boxs, Box.AdjustSides(boxs, boxs, 2, -2, 1, -3));
        Assert.Equal((12, 21, 26, 36), G(boxs));

        Box boxd = B(0, 0, 1, 1);
        Assert.Same(boxd, Box.AdjustSides(boxd, B(10, 20, 30, 40), 5, 5, 5, 5));
        Assert.Equal((15, 25, 30, 40), G(boxd));
    }

    [Fact]
    public void BoxAdjustSides_NoAreaThrowsAndLeavesDestinationAlone()
    {
        Box boxs = B(10, 20, 30, 40);
        Assert.Throws<ArgumentException>(() => Box.AdjustSides(null, boxs, 15, -15, 0, 0));
        Assert.Throws<ArgumentException>(() => Box.AdjustSides(boxs, boxs, 0, 0, 20, -21));
        Assert.Equal((10, 20, 30, 40), G(boxs));
    }

    [Fact]
    public void BoxAdjustSides_NegativeCoordinates()
    {
        // the left side -4 is moved by +2 to -2 and cropped to 0; the right side one past the edge
        // is -4 + 8 - 2 = 2, so the width is 2 (same for the height)
        Assert.Equal((0, 0, 2, 2), G(Box.AdjustSides(null, Neg(-4, -4, 8, 8), 2, -2, 2, -2)));
    }

    [Fact]
    public void BoxaAdjustSides_ZeroAreaBecomesOnePixelPlaceholder()
    {
        Boxa boxas = MakeBoxa((10, 20, 30, 40), (5, 5, 4, 4));
        Boxa boxad = boxas.AdjustSides(2, -2, 0, 0);
        Assert.Equal(2, boxad.Count);
        Assert.Equal((12, 20, 26, 40), G(boxad, 0));
        Assert.Equal((5, 5, 1, 1), G(boxad, 1));        // xl = 7, xr = 5 + 4 - 2 = 7: width 0
        Assert.Equal((10, 20, 30, 40), G(boxas, 0));
        Assert.Equal((5, 5, 4, 4), G(boxas, 1));
    }

    [Fact]
    public void BoxaAdjustSides_EmptyBoxa()
    {
        Assert.Equal(0, Boxa.Create(0).AdjustSides(1, 1, 1, 1).Count);
    }

    [Fact]
    public void BoxaAdjustBoxSides_ChangesTheBoxInPlace()
    {
        Boxa boxa = MakeBoxa((10, 20, 30, 40), (5, 5, 4, 4));
        boxa.AdjustBoxSides(0, 2, -2, 1, -3);
        Assert.Equal((12, 21, 26, 36), G(boxa, 0));
        Assert.Equal((5, 5, 4, 4), G(boxa, 1));
        // no area: error logged, the box stays as it was
        boxa.AdjustBoxSides(1, 2, -2, 0, 0);
        Assert.Equal((5, 5, 4, 4), G(boxa, 1));
    }

    [Fact]
    public void BoxaAdjustBoxSides_InvalidIndexThrows()
    {
        Boxa boxa = MakeBoxa((10, 20, 30, 40));
        Assert.Throws<ArgumentException>(() => boxa.AdjustBoxSides(1, 0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => boxa.AdjustBoxSides(-1, 0, 0, 0, 0));
    }

    /*--------------------------------- SetSide ---------------------------------*/

    [Fact]
    public void BoxSetSide_EachSideKeepsTheOppositeSide()
    {
        Box box = B(10, 20, 30, 40);       // right 39, bottom 59
        box.SetSide(L.L_SET_LEFT, 15, 0);
        Assert.Equal((15, 20, 25, 40), G(box));
        box.SetSide(L.L_SET_RIGHT, 50, 0);
        Assert.Equal((15, 20, 36, 40), G(box));
        box.SetSide(L.L_SET_TOP, 25, 0);
        Assert.Equal((15, 25, 36, 35), G(box));
        box.SetSide(L.L_SET_BOT, 70, 0);
        Assert.Equal((15, 25, 36, 46), G(box));
    }

    [Fact]
    public void BoxSetSide_ThresholdIsAMinimumDifference()
    {
        Box box = B(10, 20, 30, 40);
        box.SetSide(L.L_SET_LEFT, 11, 2);      // |10 - 11| = 1 < 2
        Assert.Equal((10, 20, 30, 40), G(box));
        box.SetSide(L.L_SET_LEFT, 11, 1);      // 1 >= 1
        Assert.Equal((11, 20, 29, 40), G(box));
        box.SetSide(L.L_SET_BOT, 59, 1);       // already at 59: |0| < 1
        Assert.Equal((11, 20, 29, 40), G(box));
        box.SetSide(L.L_SET_BOT, 59, 0);       // thresh 0 always sets
        Assert.Equal((11, 20, 29, 40), G(box));
    }

    [Fact]
    public void BoxSetSide_Errors()
    {
        Box box = B(10, 20, 30, 40);
        Assert.Throws<ArgumentException>(() => box.SetSide(L.L_ADJUST_LEFT, 5, 0));
        Assert.Throws<ArgumentException>(() => box.SetSide(L.L_SET_LEFT, -1, 0));
        Assert.Equal((10, 20, 30, 40), G(box));
    }

    [Fact]
    public void BoxaSetSide_NewAndInPlace()
    {
        Boxa boxas = MakeBoxa((10, 20, 30, 40), (12, 22, 28, 38));
        Boxa boxad = Boxa.SetSide(null, boxas, L.L_SET_LEFT, 15, 0);
        Assert.NotSame(boxas, boxad);
        Assert.Equal((15, 20, 25, 40), G(boxad, 0));
        Assert.Equal((15, 22, 25, 38), G(boxad, 1));
        Assert.Equal((10, 20, 30, 40), G(boxas, 0));

        Assert.Same(boxas, Boxa.SetSide(boxas, boxas, L.L_SET_TOP, 30, 0));
        Assert.Equal((10, 30, 30, 30), G(boxas, 0));
        Assert.Equal((12, 30, 28, 30), G(boxas, 1));
    }

    [Fact]
    public void BoxaSetSide_Errors()
    {
        Boxa boxas = MakeBoxa((10, 20, 30, 40));
        Assert.Throws<ArgumentException>(() => Boxa.SetSide(MakeBoxa((1, 1, 1, 1)), boxas, L.L_SET_LEFT, 5, 0));
        Assert.Throws<ArgumentException>(() => Boxa.SetSide(null, boxas, L.L_ADJUST_TOP, 5, 0));
        Assert.Throws<ArgumentException>(() => Boxa.SetSide(null, boxas, L.L_SET_LEFT, -1, 0));
    }

    /*------------------------- AdjustWidth/HeightToTarget ------------------------*/

    private static Boxa One() => MakeBoxa((10, 20, 30, 40));

    [Fact]
    public void AdjustWidthToTarget_EachSides()
    {
        // width 30 -> 20, diff = 10
        Assert.Equal((20, 20, 20, 40), G(Boxa.AdjustWidthToTarget(null, One(), L.L_ADJUST_LEFT, 20, 5), 0));
        Assert.Equal((10, 20, 20, 40), G(Boxa.AdjustWidthToTarget(null, One(), L.L_ADJUST_RIGHT, 20, 5), 0));
        Assert.Equal((15, 20, 20, 40), G(Boxa.AdjustWidthToTarget(null, One(), L.L_ADJUST_LEFT_AND_RIGHT, 20, 5), 0));
    }

    [Fact]
    public void AdjustWidthToTarget_ThresholdAndDivisionTruncation()
    {
        Assert.Equal((10, 20, 30, 40), G(Boxa.AdjustWidthToTarget(null, One(), L.L_ADJUST_LEFT, 20, 11), 0));
        // diff = 5: 5 / 2 == 2
        Assert.Equal((12, 20, 25, 40), G(Boxa.AdjustWidthToTarget(null, One(), L.L_ADJUST_LEFT_AND_RIGHT, 25, 5), 0));
        // diff = -5: -5 / 2 == -2 in C (truncation toward zero), not -3
        Assert.Equal((8, 20, 35, 40), G(Boxa.AdjustWidthToTarget(null, One(), L.L_ADJUST_LEFT_AND_RIGHT, 35, 5), 0));
        // diff = -6 on the left side, x + diff = 4
        Assert.Equal((4, 20, 36, 40), G(Boxa.AdjustWidthToTarget(null, One(), L.L_ADJUST_LEFT, 36, 5), 0));
    }

    [Fact]
    public void AdjustWidthToTarget_LeftEdgeStopsAtZero()
    {
        Boxa boxas = MakeBoxa((2, 20, 30, 40));
        Assert.Equal((0, 20, 36, 40), G(Boxa.AdjustWidthToTarget(null, boxas, L.L_ADJUST_LEFT, 36, 5), 0));
    }

    [Fact]
    public void AdjustWidthToTarget_InPlaceSkipsInvalidBoxes()
    {
        Boxa boxas = MakeBoxa((10, 20, 30, 40), (5, 5, 0, 7));
        Boxa result = Boxa.AdjustWidthToTarget(boxas, boxas, L.L_ADJUST_RIGHT, 20, 0);
        Assert.Same(boxas, result);
        Assert.Equal((10, 20, 20, 40), G(boxas, 0));
        Assert.Equal((5, 5, 0, 7), G(boxas, 1));
    }

    [Fact]
    public void AdjustWidthToTarget_Errors()
    {
        Assert.Throws<ArgumentException>(() => Boxa.AdjustWidthToTarget(One(), One(), L.L_ADJUST_LEFT, 20, 0));
        Assert.Throws<ArgumentException>(() => Boxa.AdjustWidthToTarget(null, One(), L.L_ADJUST_TOP, 20, 0));
        Assert.Throws<ArgumentException>(() => Boxa.AdjustWidthToTarget(null, One(), L.L_ADJUST_LEFT, 0, 0));
    }

    [Fact]
    public void AdjustHeightToTarget_EachSides()
    {
        // height 40 -> 30, diff = 10
        Assert.Equal((10, 30, 30, 30), G(Boxa.AdjustHeightToTarget(null, One(), L.L_ADJUST_TOP, 30, 5), 0));
        Assert.Equal((10, 20, 30, 30), G(Boxa.AdjustHeightToTarget(null, One(), L.L_ADJUST_BOT, 30, 5), 0));
        Assert.Equal((10, 25, 30, 30), G(Boxa.AdjustHeightToTarget(null, One(), L.L_ADJUST_TOP_AND_BOT, 30, 5), 0));
        Assert.Equal((10, 20, 30, 40), G(Boxa.AdjustHeightToTarget(null, One(), L.L_ADJUST_TOP, 30, 11), 0));
        // diff = -5: -5 / 2 == -2
        Assert.Equal((10, 18, 30, 45), G(Boxa.AdjustHeightToTarget(null, One(), L.L_ADJUST_TOP_AND_BOT, 45, 5), 0));
    }

    [Fact]
    public void AdjustHeightToTarget_InPlaceTopStopsAtZeroAndErrors()
    {
        Boxa boxas = MakeBoxa((10, 2, 30, 40));
        Assert.Same(boxas, Boxa.AdjustHeightToTarget(boxas, boxas, L.L_ADJUST_TOP, 46, 0));
        Assert.Equal((10, 0, 30, 46), G(boxas, 0));
        Assert.Throws<ArgumentException>(() => Boxa.AdjustHeightToTarget(One(), One(), L.L_ADJUST_TOP, 20, 0));
        Assert.Throws<ArgumentException>(() => Boxa.AdjustHeightToTarget(null, One(), L.L_ADJUST_LEFT, 20, 0));
        Assert.Throws<ArgumentException>(() => Boxa.AdjustHeightToTarget(null, One(), L.L_ADJUST_TOP, -3, 0));
    }

    /*---------------------------------- Equal -----------------------------------*/

    [Fact]
    public void BoxEqual_ComparesAllFourValues()
    {
        Box box = B(1, 2, 3, 4);
        Assert.True(box.Equal(B(1, 2, 3, 4)));
        Assert.False(box.Equal(B(0, 2, 3, 4)));
        Assert.False(box.Equal(B(1, 3, 3, 4)));
        Assert.False(box.Equal(B(1, 2, 4, 4)));
        Assert.False(box.Equal(B(1, 2, 3, 5)));
        Assert.True(B(5, 5, 0, 0).Equal(B(5, 5, 0, 0)));      // no validity requirement
        Assert.True(Neg(-2, -3, 4, 5).Equal(Neg(-2, -3, 4, 5)));
    }

    [Fact]
    public void BoxaEqual_DifferentCountIsNotEqual()
    {
        Assert.False(One().Equal(Boxa.Create(0), 0, out Numa? naindex));
        Assert.Null(naindex);
    }

    [Fact]
    public void BoxaEqual_IdenticalBoxaGiveIdentityIndex()
    {
        Assert.True(One().Equal(One(), 0, out Numa? naindex));
        Assert.NotNull(naindex);
        Assert.Equal(1, naindex!.Count);
        Assert.Equal(0, naindex.GetIValue(0));
    }

    /*--------------------------------- Similar ----------------------------------*/

    [Fact]
    public void BoxSimilar_EachSideSeparately()
    {
        Box box1 = B(0, 0, 10, 10);                 // l 0, r 9, t 0, b 9
        Box box2 = B(1, 1, 10, 10);                 // l 1, r 10, t 1, b 10
        Assert.True(box1.Similar(box2, 1, 1, 1, 1));
        Assert.False(box1.Similar(box2, 0, 1, 1, 1));
        Assert.False(box1.Similar(box2, 1, 0, 1, 1));
        Assert.False(box1.Similar(box2, 1, 1, 0, 1));
        Assert.False(box1.Similar(box2, 1, 1, 1, 0));
        Assert.True(box1.Similar(box1, 0, 0, 0, 0));
        Assert.True(box2.Similar(box1, 1, 1, 1, 1));    // symmetric
    }

    [Fact]
    public void BoxSimilar_NegativeCoordinatesAndInvalidBoxes()
    {
        Assert.True(Neg(-4, -4, 8, 8).Similar(Neg(-2, -3, 8, 8), 2, 2, 1, 1));
        Assert.False(Neg(-4, -4, 8, 8).Similar(Neg(-2, -3, 8, 8), 1, 2, 1, 1));
        Assert.Throws<ArgumentException>(() => B(0, 0, 0, 5).Similar(B(0, 0, 5, 5), 9, 9, 9, 9));
        Assert.Throws<ArgumentException>(() => B(0, 0, 5, 5).Similar(B(0, 0, 5, 0), 9, 9, 9, 9));
    }

    [Fact]
    public void BoxaSimilar_ComparesBoxesInOrder()
    {
        Boxa boxa1 = MakeBoxa((0, 0, 10, 10), (20, 20, 10, 10));
        Boxa boxa2 = MakeBoxa((1, 1, 10, 10), (20, 21, 10, 10));
        Assert.True(boxa1.Similar(boxa2, 1, 1, 1, 1, 0));
        Assert.False(boxa1.Similar(boxa2, 1, 1, 0, 1, 0));
        Assert.False(boxa1.Similar(boxa2, 1, 1, 0, 1, 1));     // debug goes on testing, same result
        Assert.False(boxa1.Similar(MakeBoxa((20, 20, 10, 10), (0, 0, 10, 10)), 1, 1, 1, 1, 0));
    }

    [Fact]
    public void BoxaSimilar_CountMismatchThrowsAndInvalidPairsAreMismatches()
    {
        Assert.Throws<ArgumentException>(
            () => MakeBoxa((0, 0, 1, 1)).Similar(Boxa.Create(0), 9, 9, 9, 9, 0));
        // an invalid box makes boxSimilar() fail; the pair counts as not similar and testing goes on
        Assert.False(MakeBoxa((0, 0, 0, 0)).Similar(MakeBoxa((0, 0, 0, 0)), 9, 9, 9, 9, 0));
        Assert.True(Boxa.Create(0).Similar(Boxa.Create(0), 0, 0, 0, 0, 0));
    }

    /*------------------------------------ Join ----------------------------------*/

    private static Boxa Three() => MakeBoxa((2, 2, 2, 2), (3, 3, 3, 3), (4, 4, 4, 4));

    [Fact]
    public void BoxaJoin_AppendsClones()
    {
        Boxa boxad = MakeBoxa((1, 1, 1, 1));
        Boxa boxas = Three();
        boxad.Join(boxas, 0, -1);
        Assert.Equal(4, boxad.Count);
        Assert.Equal((4, 4, 4, 4), G(boxad, 3));
        Assert.Same(boxas.GetBox(1, L.L_CLONE), boxad.GetBox(2, L.L_CLONE));
        Assert.Equal(3, boxas.Count);
    }

    [Fact]
    public void BoxaJoin_RangeHandling()
    {
        Boxa boxad = Boxa.Create(0);
        boxad.Join(Three(), 1, 1);
        Assert.Equal(1, boxad.Count);
        Assert.Equal((3, 3, 3, 3), G(boxad, 0));

        boxad = Boxa.Create(0);
        boxad.Join(Three(), -5, 1);        // istart < 0 means 0
        Assert.Equal(2, boxad.Count);
        Assert.Equal((2, 2, 2, 2), G(boxad, 0));

        boxad = Boxa.Create(0);
        boxad.Join(Three(), 1, 99);        // iend >= n means n - 1
        Assert.Equal(2, boxad.Count);
        Assert.Equal((4, 4, 4, 4), G(boxad, 1));
    }

    [Fact]
    public void BoxaJoin_NullOrEmptySourceIsANoOp()
    {
        Boxa boxad = MakeBoxa((1, 1, 1, 1));
        boxad.Join(null, 0, -1);
        boxad.Join(Boxa.Create(0), 5, 2);      // not an error: nothing to add
        Assert.Equal(1, boxad.Count);
    }

    [Fact]
    public void BoxaJoin_StartAfterEndThrows()
    {
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).Join(Three(), 2, 1));
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).Join(Three(), 3, -1));   // iend = 2 < 3
    }

    [Fact]
    public void BoxaaJoin_AppendsClonesOfBoxa()
    {
        Boxaa baas = Boxaa.Create(0);
        baas.AddBoxa(MakeBoxa((1, 1, 1, 1)), L.L_INSERT);
        baas.AddBoxa(MakeBoxa((2, 2, 2, 2), (3, 3, 3, 3)), L.L_INSERT);
        baas.AddBoxa(Boxa.Create(0), L.L_INSERT);

        Boxaa baad = Boxaa.Create(0);
        baad.Join(baas, 0, -1);
        Assert.Equal(3, baad.Count);
        Assert.Same(baas.GetBoxa(1, L.L_CLONE), baad.GetBoxa(1, L.L_CLONE));
        Assert.Equal(3, baad.GetBoxCount());

        baad.Join(baas, 1, 1);
        Assert.Equal(4, baad.Count);
        baad.Join(baas, -3, 0);
        Assert.Equal(5, baad.Count);
        baad.Join(baas, 2, 99);
        Assert.Equal(6, baad.Count);
    }

    [Fact]
    public void BoxaaJoin_NullIsANoOpAndBadRangeThrows()
    {
        Boxaa baad = Boxaa.Create(0);
        baad.Join(null, 0, -1);
        Assert.Equal(0, baad.Count);

        Boxaa baas = Boxaa.Create(0);
        baas.AddBoxa(Boxa.Create(0), L.L_INSERT);
        Assert.Throws<ArgumentException>(() => baad.Join(baas, 1, -1));
        // unlike boxaJoin(), an empty source with a bad range is an error
        Assert.Throws<ArgumentException>(() => baad.Join(Boxaa.Create(0), 0, -1));
    }

    /*------------------------------ Split / Merge ------------------------------*/

    private static Boxa Five() => MakeBoxa((1, 1, 1, 1), (2, 2, 2, 2), (3, 3, 3, 3), (4, 4, 4, 4), (5, 5, 5, 5));

    [Fact]
    public void SplitEvenOdd_WithoutFill()
    {
        Boxa boxa = Five();
        boxa.SplitEvenOdd(0, out Boxa boxae, out Boxa boxao);
        Assert.Equal(3, boxae.Count);
        Assert.Equal(2, boxao.Count);
        Assert.Equal((1, 1, 1, 1), G(boxae, 0));
        Assert.Equal((3, 3, 3, 3), G(boxae, 1));
        Assert.Equal((5, 5, 5, 5), G(boxae, 2));
        Assert.Equal((2, 2, 2, 2), G(boxao, 0));
        Assert.Equal((4, 4, 4, 4), G(boxao, 1));
        Assert.NotSame(boxa.GetBox(0, L.L_CLONE), boxae.GetBox(0, L.L_CLONE));   // copies
    }

    [Fact]
    public void SplitEvenOdd_WithFillUsesInvalidPlaceholders()
    {
        Boxa boxa = Five();
        boxa.SplitEvenOdd(1, out Boxa boxae, out Boxa boxao);
        Assert.Equal(5, boxae.Count);
        Assert.Equal(5, boxao.Count);
        for (int i = 0; i < 5; i++) {
            Box? e = boxae.GetValidBox(i, L.L_CLONE);
            Box? o = boxao.GetValidBox(i, L.L_CLONE);
            if ((i & 1) == 0) {
                Assert.Equal((i + 1, i + 1, i + 1, i + 1), G(e!));
                Assert.Null(o);
            } else {
                Assert.Null(e);
                Assert.Equal((i + 1, i + 1, i + 1, i + 1), G(o!));
            }
        }
        Assert.Equal((0, 0, 0, 0), G(boxae, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MergeEvenOdd_InvertsSplitEvenOdd(int fillflag)
    {
        Boxa boxa = Five();
        boxa.SplitEvenOdd(fillflag, out Boxa boxae, out Boxa boxao);
        Boxa merged = boxae.MergeEvenOdd(boxao, fillflag);
        Assert.Equal(5, merged.Count);
        for (int i = 0; i < 5; i++)
            Assert.Equal(G(boxa, i), G(merged, i));

        boxa = MakeBoxa((1, 1, 1, 1), (2, 2, 2, 2), (3, 3, 3, 3), (4, 4, 4, 4));   // even length
        boxa.SplitEvenOdd(fillflag, out boxae, out boxao);
        merged = boxae.MergeEvenOdd(boxao, fillflag);
        Assert.Equal(4, merged.Count);
        for (int i = 0; i < 4; i++)
            Assert.Equal(G(boxa, i), G(merged, i));
    }

    [Fact]
    public void SplitAndMerge_EmptyBoxa()
    {
        Boxa.Create(0).SplitEvenOdd(0, out Boxa boxae, out Boxa boxao);
        Assert.Equal(0, boxae.Count);
        Assert.Equal(0, boxao.Count);
        Assert.Equal(0, boxae.MergeEvenOdd(boxao, 0).Count);
    }

    [Fact]
    public void MergeEvenOdd_SizeCheck()
    {
        Boxa one = MakeBoxa((1, 1, 1, 1));
        Boxa three = Three();
        Assert.Throws<ArgumentException>(() => one.MergeEvenOdd(three, 0));       // ne < no
        Assert.Throws<ArgumentException>(() => three.MergeEvenOdd(one, 0));       // ne > no + 1
        Assert.Equal(5, three.MergeEvenOdd(MakeBoxa((8, 8, 8, 8), (9, 9, 9, 9)), 0).Count);
    }
}
