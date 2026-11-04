using System;
using System.Collections.Generic;
using Leptonica;

namespace Leptonica.Managed.Tests.PtaTests;

/// <summary>
/// Tests for ptafunc1.c with hand-computed expected values.  Functions that need
/// callees in files that are not ported yet (pixGetPixel(), pixMorphSequence(), ...)
/// are tested only where they do not reach those callees.
/// </summary>
public class PtaFunc1Tests
{
    private static Pta P(params (float x, float y)[] pts)
    {
        Pta pta = Pta.Create(0);
        foreach (var p in pts)
            pta.AddPt(p.x, p.y);
        return pta;
    }

    private static List<(float, float)> Pts(Pta pta)
    {
        List<(float, float)> list = new List<(float, float)>();
        for (int i = 0; i < pta.Count; i++) {
            pta.GetPt(i, out float x, out float y);
            list.Add((x, y));
        }
        return list;
    }

    private static void AssertPts(Pta pta, params (float, float)[] expected)
    {
        Assert.Equal(expected, Pts(pta));
    }

    /*------------------------ Simple rearrangements ---------------------*/

    [Fact]
    public void Subsample_TakesEveryKth()
    {
        Pta pta = Pta.Create(0);
        for (int i = 0; i < 7; i++)
            pta.AddPt(i, 2 * i);
        AssertPts(pta.Subsample(3), (0, 0), (3, 6), (6, 12));
        AssertPts(pta.Subsample(1), (0, 0), (1, 2), (2, 4), (3, 6), (4, 8), (5, 10), (6, 12));
        AssertPts(pta.Subsample(100), (0, 0));
        Assert.Throws<ArgumentException>(() => pta.Subsample(0));
        Assert.Equal(0, Pta.Create(0).Subsample(2).Count);
    }

    [Fact]
    public void Join_AddsRoundedPoints()
    {
        Pta ptas = P((0.4f, 0.6f), (2.5f, 3.5f), (7f, 8f));
        Pta ptad = P((1f, 1f));
        ptad.Join(ptas, 0, -1);
        /* the points come through ptaGetIPt(), so they are rounded */
        AssertPts(ptad, (1, 1), (0, 1), (3, 4), (7, 8));

        Pta p2 = P((9f, 9f));
        p2.Join(ptas, -5, 1);
        AssertPts(p2, (9, 9), (0, 1), (3, 4));

        Pta p3 = P();
        p3.Join(ptas, 1, 100);
        AssertPts(p3, (3, 4), (7, 8));
    }

    [Fact]
    public void Join_NullSourceIsNoOp_BadRangeThrows()
    {
        Pta ptad = P((1f, 1f));
        ptad.Join(null, 0, -1);
        AssertPts(ptad, (1, 1));
        Pta ptas = P((0f, 0f), (1f, 1f), (2f, 2f));
        Assert.Throws<ArgumentException>(() => ptad.Join(ptas, 2, 1));
        Assert.Throws<ArgumentException>(() => ptad.Join(P(), 0, -1));
        Assert.Equal(1, ptad.Count);
    }

    [Fact]
    public void PtaaJoin_SharesThePtas()
    {
        Pta a = P((1f, 1f));
        Pta b = P((2f, 2f));
        Pta c = P((3f, 3f));
        Ptaa ptaad = Ptaa.Create(0);
        ptaad.AddPta(a, L.L_INSERT);
        Ptaa ptaas = Ptaa.Create(0);
        ptaas.AddPta(b, L.L_INSERT);
        ptaas.AddPta(c, L.L_INSERT);

        ptaad.Join(ptaas, 0, -1);
        Assert.Equal(3, ptaad.Count);
        Assert.Same(b, ptaad.GetPta(1, L.L_CLONE));
        Assert.Same(c, ptaad.GetPta(2, L.L_CLONE));

        ptaad.Join(ptaas, 1, 1);
        Assert.Equal(4, ptaad.Count);
        Assert.Same(c, ptaad.GetPta(3, L.L_CLONE));

        ptaad.Join(null, 0, -1);
        Assert.Equal(4, ptaad.Count);
        Assert.Throws<ArgumentException>(() => ptaad.Join(ptaas, 2, 1));
        Assert.Throws<ArgumentException>(() => ptaad.Join(Ptaa.Create(0), 0, -1));
    }

    [Fact]
    public void Reverse_FloatAndInteger()
    {
        Pta pta = P((1.5f, 2.5f), (3.5f, 4.5f), (5.25f, 6.75f));
        AssertPts(pta.Reverse(0), (5.25f, 6.75f), (3.5f, 4.5f), (1.5f, 2.5f));
        AssertPts(pta.Reverse(1), (5, 7), (4, 5), (2, 3));
        Assert.Equal(0, P().Reverse(0).Count);
    }

    [Fact]
    public void Transpose_SwapsXY()
    {
        AssertPts(P((1f, 2f), (3.5f, -4f)).Transpose(), (2, 1), (-4f, 3.5f));
    }

    [Fact]
    public void CyclicPerm_StartsAndEndsAtGivenPoint()
    {
        Pta pta = P((0f, 0f), (1f, 0f), (1f, 1f), (0f, 1f), (0f, 0f));
        AssertPts(pta.CyclicPerm(1, 1), (1, 1), (0, 1), (0, 0), (1, 0), (1, 1));
        AssertPts(pta.CyclicPerm(0, 1), (0, 1), (0, 0), (1, 0), (1, 1), (0, 1));
        /* the first point is the first match: it gives the same path */
        AssertPts(pta.CyclicPerm(0, 0), (0, 0), (1, 0), (1, 1), (0, 1), (0, 0));
    }

    [Fact]
    public void CyclicPerm_Errors()
    {
        Pta open = P((0f, 0f), (1f, 0f), (1f, 1f));
        Assert.Throws<ArgumentException>(() => open.CyclicPerm(0, 0));
        Pta closed = P((0f, 0f), (1f, 0f), (1f, 1f), (0f, 0f));
        Assert.Throws<ArgumentException>(() => closed.CyclicPerm(5, 5));
        Assert.Throws<ArgumentException>(() => P().CyclicPerm(0, 0));
    }

    [Fact]
    public void SelectRange_Cases()
    {
        Pta pta = P((0f, 0f), (1f, 10f), (2f, 20f), (3f, 30f), (4f, 40f));
        AssertPts(pta.SelectRange(1, 3), (1, 10), (2, 20), (3, 30));
        AssertPts(pta.SelectRange(3, -1), (3, 30), (4, 40));
        AssertPts(pta.SelectRange(-2, 1), (0, 0), (1, 10));
        AssertPts(pta.SelectRange(4, 99), (4, 40));
        Assert.Throws<ArgumentException>(() => pta.SelectRange(5, -1));
        Assert.Throws<ArgumentException>(() => pta.SelectRange(3, 2));
        /* an empty pta gives an empty copy */
        Pta empty = P();
        Pta copy = empty.SelectRange(0, -1);
        Assert.Equal(0, copy.Count);
        Assert.NotSame(empty, copy);
    }

    /*------------------------------ Geometric ---------------------------*/

    [Fact]
    public void GetBoundingRegion_UsesRoundedPoints()
    {
        /* rounded: (3,5), (10,2), (7,9) */
        Box box = P((3.4f, 5f), (10f, 2f), (7f, 9f)).GetBoundingRegion();
        Assert.Equal((3, 2, 8, 8), (box.X, box.Y, box.Width, box.Height));

        Box single = P((4f, 6f)).GetBoundingRegion();
        Assert.Equal((4, 6, 1, 1), (single.X, single.Y, single.Width, single.Height));
    }

    [Fact]
    public void GetBoundingRegion_EmptyThrows()
    {
        Assert.Throws<ArgumentException>(() => P().GetBoundingRegion());
    }

    [Fact]
    public void GetRange_FindsMinMax()
    {
        Pta pta = P((3f, -2.5f), (-1f, 8f), (5.5f, 4f));
        pta.GetRange(out float minx, out float maxx, out float miny, out float maxy);
        Assert.Equal((-1f, 5.5f, -2.5f, 8f), (minx, maxx, miny, maxy));

        P((2f, 3f)).GetRange(out minx, out maxx, out miny, out maxy);
        Assert.Equal((2f, 2f, 3f, 3f), (minx, maxx, miny, maxy));
        Assert.Throws<ArgumentException>(() => P().GetRange(out _, out _, out _, out _));
    }

    [Fact]
    public void GetInsideBox_HalfOpenBox()
    {
        Box box = Box.Create(2, 2, 3, 3);  /* x and y in [2, 5) */
        Pta pta = P((2f, 2f), (4.9f, 4.9f), (5f, 3f), (1.9f, 3f), (3f, 5f), (3.5f, 3.5f));
        AssertPts(pta.GetInsideBox(box), (2, 2), (4.9f, 4.9f), (3.5f, 3.5f));
    }

    private static Pix MakeBitPix(int w, int h, params (int x, int y)[] fg)
    {
        Pix pix = Pix.Create(w, h, 1);
        int wpl = pix.Wpl;
        foreach (var p in fg)
            ArrayAccess.SetDataBit(pix.Data.AsSpan(p.y * wpl, wpl), p.x);
        return pix;
    }

    [Fact]
    public void FindCornerPixels_SearchesAlong45DegreeLines()
    {
        /* UL: i = 3, j = 2 is (x=2, y=1), found before (3, 0) at j = 3.
         * UR: (9, 0) at i = 0.  LL: (0, 7) at i = 0.
         * LR: i = 2, j = 1 is y = 7 - 2 + 1 = 6, x = 9 - 1 = 8. */
        Pix pix = MakeBitPix(10, 8, (2, 1), (3, 0), (9, 0), (0, 7), (8, 6));
        AssertPts(pix.FindCornerPixels(), (2, 1), (9, 0), (0, 7), (8, 6));
    }

    [Fact]
    public void FindCornerPixels_EmptyAndErrors()
    {
        Assert.Equal(0, MakeBitPix(10, 8).FindCornerPixels().Count);
        /* a single pixel in the middle of a 5 x 5 image is found from all 4 corners */
        Pix pix = MakeBitPix(5, 5, (2, 2));
        AssertPts(pix.FindCornerPixels(), (2, 2), (2, 2), (2, 2), (2, 2));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).FindCornerPixels());
    }

    [Fact]
    public void ContainsPt_UsesRoundedPoints()
    {
        Pta pta = P((1.4f, 2.4f), (7f, 8f));
        Assert.True(pta.ContainsPt(1, 2));
        Assert.True(pta.ContainsPt(7, 8));
        Assert.False(pta.ContainsPt(1, 3));
        Assert.False(P().ContainsPt(0, 0));
    }

    [Fact]
    public void TestIntersection_ComparesSameIndex()
    {
        /* NOTE(upstream): the inner loop reads pta2 at i instead of j */
        Pta a = P((1f, 1f), (5f, 5f));
        Pta b = P((1f, 1f), (9f, 9f));
        Assert.True(a.TestIntersection(b));
        Assert.False(P((2f, 2f)).TestIntersection(P((3f, 3f))));
        Assert.False(P((2f, 2f)).TestIntersection(P()));
        Assert.False(P().TestIntersection(P((2f, 2f))));
    }

    [Fact]
    public void TestIntersection_ReproducesUpstreamIndexBug()
    {
        /* Common point (1,1) / (5,5), but never at the same index: not found */
        Pta a = P((5f, 5f), (1f, 1f));
        Pta b = P((1f, 1f), (5f, 5f));
        Assert.False(a.TestIntersection(b));
        /* For i >= n2 the C code compares with (0, 0) */
        Assert.True(P((7f, 7f), (0f, 0f)).TestIntersection(P((3f, 3f))));
        Assert.False(P((7f, 7f), (1f, 0f)).TestIntersection(P((3f, 3f))));
    }

    [Fact]
    public void Transform_ShiftThenScale()
    {
        Pta pta = P((1f, 2f), (3f, 4f));
        AssertPts(pta.Transform(1, -1, 2.0f, 0.5f), (4, 1), (8, 2));
        AssertPts(pta.Transform(0, 0, 1.0f, 1.0f), (1, 2), (3, 4));
        /* (int)(-1 * 2 + 0.5) truncates toward zero */
        AssertPts(P((2f, 0f)).Transform(0, 0, -1.0f, 1.0f), (-1, 0));
        /* the points are rounded first: 2.5 -> 3, then * 1.5 + 0.5 = 5.0 */
        AssertPts(P((2.5f, 0f)).Transform(0, 0, 1.5f, 1.0f), (5, 0));
    }

    [Fact]
    public void PtInsidePolygon_Square()
    {
        Pta square = P((0f, 0f), (10f, 0f), (10f, 10f), (0f, 10f));
        Assert.True(square.PtInsidePolygon(5f, 5f));
        Assert.True(square.PtInsidePolygon(0.5f, 9f));
        Assert.False(square.PtInsidePolygon(20f, 20f));
        Assert.False(square.PtInsidePolygon(-1f, 5f));
        /* the orientation does not matter */
        Pta rev = square.Reverse(0);
        Assert.True(rev.PtInsidePolygon(5f, 5f));
        Assert.False(rev.PtInsidePolygon(20f, 5f));
    }

    [Fact]
    public void AngleBetweenVectors_FoldedToPlusMinusPi()
    {
        float halfpi = (float)(Math.PI / 2);
        Assert.Equal(halfpi, Pta.AngleBetweenVectors(1f, 0f, 0f, 1f), 6);
        Assert.Equal(-halfpi, Pta.AngleBetweenVectors(0f, 1f, 1f, 0f), 6);
        Assert.Equal(0f, Pta.AngleBetweenVectors(2f, 3f, 4f, 6f), 6);
        /* atan2(-1, 0) - atan2(0, -1) = -3pi/2 is folded to pi/2 */
        Assert.Equal(halfpi, Pta.AngleBetweenVectors(-1f, 0f, 0f, -1f), 6);
    }

    [Fact]
    public void PolygonIsConvex_OrientationAndConcave()
    {
        /* the cross product test is satisfied for this orientation */
        Pta square = P((0f, 0f), (0f, 10f), (10f, 10f), (10f, 0f));
        Assert.True(square.PolygonIsConvex());
        Assert.False(square.Reverse(0).PolygonIsConvex());
        Pta concave = P((0f, 0f), (0f, 10f), (5f, 5f), (10f, 10f), (10f, 0f));
        Assert.False(concave.PolygonIsConvex());
        /* collinear points pass: cprod is 0 */
        Pta line = P((0f, 0f), (1f, 1f), (2f, 2f));
        Assert.True(line.PolygonIsConvex());
        Assert.Throws<ArgumentException>(() => P((0f, 0f), (1f, 1f)).PolygonIsConvex());
    }

    /*-------------------------- Min/max and filtering -------------------*/

    [Fact]
    public void GetMinMax_Values()
    {
        Pta pta = P((3f, -2f), (-1f, 8f), (5f, 4f));
        pta.GetMinMax(out float xmin, out float ymin, out float xmax, out float ymax);
        Assert.Equal((-1f, -2f, 5f, 8f), (xmin, ymin, xmax, ymax));
    }

    [Fact]
    public void GetMinMax_EmptyGivesMinusOne()
    {
        P().GetMinMax(out float xmin, out float ymin, out float xmax, out float ymax);
        Assert.Equal((-1f, -1f, -1f, -1f), (xmin, ymin, xmax, ymax));
    }

    [Fact]
    public void SelectByValue_AllTypesAndRelations()
    {
        Pta pta = P((1f, 10f), (2f, 20f), (3f, 30f), (4f, 40f));
        AssertPts(pta.SelectByValue(2f, 0f, L.L_SELECT_XVAL, L.L_SELECT_IF_GT), (3, 30), (4, 40));
        AssertPts(pta.SelectByValue(2f, 0f, L.L_SELECT_XVAL, L.L_SELECT_IF_LTE), (1, 10), (2, 20));
        AssertPts(pta.SelectByValue(2f, 0f, L.L_SELECT_XVAL, L.L_SELECT_IF_LT), (1, 10));
        AssertPts(pta.SelectByValue(2f, 0f, L.L_SELECT_XVAL, L.L_SELECT_IF_GTE), (2, 20), (3, 30), (4, 40));
        AssertPts(pta.SelectByValue(0f, 25f, L.L_SELECT_YVAL, L.L_SELECT_IF_LT), (1, 10), (2, 20));
        AssertPts(pta.SelectByValue(0f, 30f, L.L_SELECT_YVAL, L.L_SELECT_IF_GTE), (3, 30), (4, 40));
        /* either: x < 2 or y < 20 -> only the first (the second is on the threshold) */
        AssertPts(pta.SelectByValue(2f, 20f, L.L_SELECT_IF_EITHER, L.L_SELECT_IF_LT), (1, 10));
        AssertPts(pta.SelectByValue(2f, 20f, L.L_SELECT_IF_EITHER, L.L_SELECT_IF_LTE), (1, 10), (2, 20));
        AssertPts(pta.SelectByValue(3f, 20f, L.L_SELECT_IF_EITHER, L.L_SELECT_IF_GT), (3, 30), (4, 40));
        /* both */
        AssertPts(pta.SelectByValue(2f, 30f, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_GTE), (3, 30), (4, 40));
        AssertPts(pta.SelectByValue(2f, 30f, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_GT), (4, 40));
        AssertPts(pta.SelectByValue(3f, 15f, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_LTE), (1, 10));
    }

    [Fact]
    public void SelectByValue_ErrorsAndEmpty()
    {
        Pta pta = P((1f, 10f));
        Assert.Throws<ArgumentException>(() => pta.SelectByValue(0f, 0f, 99, L.L_SELECT_IF_LT));
        Assert.Throws<ArgumentException>(() => pta.SelectByValue(0f, 0f, L.L_SELECT_XVAL, 99));
        /* an empty pta is copied before the arguments are checked */
        Pta empty = P();
        Pta copy = empty.SelectByValue(0f, 0f, 99, 99);
        Assert.Equal(0, copy.Count);
        Assert.NotSame(empty, copy);
    }

    [Fact]
    public void CropToMask_EmptyPtaAndBadMask()
    {
        Pix mask = MakeBitPix(4, 4, (1, 1));
        Pta empty = P();
        Pta copy = empty.CropToMask(mask);
        Assert.Equal(0, copy.Count);
        Assert.NotSame(empty, copy);
        Assert.Throws<ArgumentException>(() => P((1f, 1f)).CropToMask(Pix.Create(4, 4, 8)));
    }

    /*--------------------------- Least squares fit ----------------------*/

    private static void AssertNuma(Numa na, params float[] expected)
    {
        Assert.Equal(expected.Length, na.Count);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], na.GetFValue(i), 4);
    }

    [Fact]
    public void GetLinearLSF_ExactLine()
    {
        /* y = 2x + 1: n = 4, sx = 6, sy = 16, sxx = 14, sxy = 34, factor = 4*14 - 36 = 20;
         * a = (4*34 - 6*16) / 20 = 2, b = (14*16 - 6*34) / 20 = 1 */
        Pta pta = P((0f, 1f), (1f, 3f), (2f, 5f), (3f, 7f));
        pta.GetLinearLSF(out float a, out float b, out Numa nafit);
        Assert.Equal(2f, a, 5);
        Assert.Equal(1f, b, 5);
        AssertNuma(nafit, 1f, 3f, 5f, 7f);
    }

    [Fact]
    public void GetLinearLSF_NoisyData()
    {
        /* (0,0), (1,1), (2,1): sx = 3, sy = 2, sxx = 5, sxy = 3, n = 3;
         * factor = 15 - 9 = 6; a = (9 - 6) / 6 = 0.5; b = (10 - 9) / 6 = 1/6 */
        Pta pta = P((0f, 0f), (1f, 1f), (2f, 1f));
        pta.GetLinearLSF(out float a, out float b, out Numa nafit);
        Assert.Equal(0.5f, a, 5);
        Assert.Equal(1f / 6f, b, 5);
        AssertNuma(nafit, 1f / 6f, 2f / 3f, 7f / 6f);
    }

    [Fact]
    public void GetLinearLSFThroughOrigin_FitsSlopeOnly()
    {
        /* sxx = 1 + 4 + 9 = 14, sxy = 2 + 8 + 19.5 = 29.5 */
        Pta pta = P((1f, 2f), (2f, 4f), (3f, 6.5f));
        pta.GetLinearLSFThroughOrigin(out float a, out Numa nafit);
        float expected = 29.5f / 14f;
        Assert.Equal(expected, a, 5);
        AssertNuma(nafit, expected, 2 * expected, 3 * expected);

        Assert.Throws<LeptonicaException>(
            () => P((0f, 1f), (0f, 2f)).GetLinearLSFThroughOrigin(out _, out _));
    }

    [Fact]
    public void GetLinearLSFHorizontal_FitsMean()
    {
        Pta pta = P((0f, 1f), (5f, 2f), (9f, 6f));
        pta.GetLinearLSFHorizontal(out float b, out Numa nafit);
        Assert.Equal(3f, b, 5);
        AssertNuma(nafit, 3f, 3f, 3f);
    }

    [Fact]
    public void GetLinearLSF_Errors()
    {
        Assert.Throws<ArgumentException>(() => P((1f, 1f)).GetLinearLSF(out _, out _, out _));
        Assert.Throws<ArgumentException>(() => P((1f, 1f)).GetLinearLSFHorizontal(out _, out _));
        /* all x equal: factor = 2*2 - 2*2 = 0 */
        Assert.Throws<LeptonicaException>(
            () => P((1f, 1f), (1f, 2f)).GetLinearLSF(out _, out _, out _));
    }

    [Fact]
    public void HigherOrderLSF_NeedEnoughPoints()
    {
        Assert.Throws<ArgumentException>(
            () => P((0f, 0f), (1f, 1f)).GetQuadraticLSF(out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(
            () => P((0f, 0f), (1f, 1f), (2f, 4f)).GetCubicLSF(out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(
            () => P((0f, 0f), (1f, 1f), (2f, 4f), (3f, 9f))
                .GetQuarticLSF(out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void NoisyLSF_ArgumentChecks()
    {
        Pta pta = P((0f, 0f), (1f, 1f), (2f, 2f));
        Assert.Throws<ArgumentException>(
            () => pta.NoisyLinearLSF(0f, out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(
            () => pta.NoisyLinearLSFThroughOrigin(-1f, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(
            () => P((0f, 0f), (1f, 1f)).NoisyLinearLSF(3f, out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(
            () => pta.NoisyQuadraticLSF(0f, out _, out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(
            () => P((0f, 0f), (1f, 1f)).NoisyQuadraticLSF(3f, out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void ApplyFits_EvaluatePolynomials()
    {
        Assert.Equal(7f, Pta.ApplyLinearFit(2f, 1f, 3f));
        Assert.Equal(11f, Pta.ApplyQuadraticFit(1f, 2f, 3f, 2f));      /* 4 + 4 + 3 */
        Assert.Equal(26f, Pta.ApplyCubicFit(1f, 2f, 3f, 4f, 2f));      /* 8 + 8 + 6 + 4 */
        Assert.Equal(57f, Pta.ApplyQuarticFit(1f, 2f, 3f, 4f, 5f, 2f)); /* 16 + 16 + 12 + 8 + 5 */
        Assert.Equal(-0.5f, Pta.ApplyLinearFit(0.5f, -1f, 1f));
    }

    /*----------------------- Interconversions with Pix ------------------*/

    [Fact]
    public void GetPixelsFromPix_ScansRowByRow()
    {
        Pix pix = MakeBitPix(40, 4, (1, 0), (4, 0), (0, 2), (33, 1), (39, 3));
        AssertPts(Pta.GetPixelsFromPix(pix, null), (1, 0), (4, 0), (33, 1), (0, 2), (39, 3));
        /* box: x in [0, 3), y in [1, 4) */
        AssertPts(Pta.GetPixelsFromPix(pix, Box.Create(0, 1, 3, 3)), (0, 2));
        AssertPts(Pta.GetPixelsFromPix(pix, Box.Create(30, 0, 10, 4)), (33, 1), (39, 3));
        Assert.Equal(0, Pta.GetPixelsFromPix(MakeBitPix(5, 5), null).Count);
        Assert.Throws<ArgumentException>(() => Pta.GetPixelsFromPix(Pix.Create(4, 4, 8), null));
    }

    [Fact]
    public void GetNeighborPixLocs_Corner()
    {
        Pix pix = Pix.Create(5, 5, 8);
        AssertPts(Pta.GetNeighborPixLocs(pix, 0, 0, 4), (1, 0), (0, 1));
        AssertPts(Pta.GetNeighborPixLocs(pix, 0, 0, 8), (1, 0), (0, 1), (1, 1));
        AssertPts(Pta.GetNeighborPixLocs(pix, 4, 4, 8), (3, 4), (4, 3), (3, 3));
    }

    [Fact]
    public void GetNeighborPixLocs_Interior_Errors()
    {
        Pix pix = Pix.Create(5, 5, 1);
        AssertPts(Pta.GetNeighborPixLocs(pix, 2, 2, 4), (1, 2), (3, 2), (2, 1), (2, 3));
        AssertPts(Pta.GetNeighborPixLocs(pix, 2, 2, 8),
                  (1, 2), (3, 2), (2, 1), (2, 3), (1, 1), (1, 3), (3, 1), (3, 3));
        Assert.Throws<ArgumentException>(() => Pta.GetNeighborPixLocs(pix, 5, 0, 4));
        Assert.Throws<ArgumentException>(() => Pta.GetNeighborPixLocs(pix, 0, -1, 4));
        Assert.Throws<ArgumentException>(() => Pta.GetNeighborPixLocs(pix, 2, 2, 6));
    }

    [Fact]
    public void BoundaryAndIndexFunctions_ArgumentChecks()
    {
        Pix gray = Pix.Create(4, 4, 8);
        Pix bin = Pix.Create(4, 4, 1);
        Assert.Throws<ArgumentException>(() => Pta.GetBoundaryPixels(gray, L.L_BOUNDARY_FG));
        Assert.Throws<ArgumentException>(() => Pta.GetBoundaryPixels(bin, 7));
        Assert.Throws<ArgumentException>(
            () => Ptaa.GetBoundaryPixels(gray, L.L_BOUNDARY_FG, 8, out _, out _));
        Assert.Throws<ArgumentException>(
            () => Ptaa.GetBoundaryPixels(bin, 7, 8, out _, out _));
        Assert.Throws<ArgumentException>(
            () => Ptaa.GetBoundaryPixels(bin, L.L_BOUNDARY_FG, 6, out _, out _));
        Assert.Throws<ArgumentException>(() => Ptaa.IndexLabeledPixels(bin, out _));
    }

    /*------------------------ Interconversions with Numa ----------------*/

    [Fact]
    public void NumaConvertToPta1_UsesImplicitX()
    {
        Numa na = Numa.Create(0);
        na.AddNumber(10f);
        na.AddNumber(20f);
        na.AddNumber(30f);
        na.SetParameters(1f, 0.5f);
        AssertPts(na.ConvertToPta1(), (1f, 10f), (1.5f, 20f), (2f, 30f));
    }

    [Fact]
    public void NumaConvertToPta2_UsesShorterLength()
    {
        Numa nax = Numa.Create(0);
        Numa nay = Numa.Create(0);
        for (int i = 0; i < 3; i++)
            nax.AddNumber(i + 1);
        nay.AddNumber(4f);
        nay.AddNumber(5f);
        AssertPts(nax.ConvertToPta2(nay), (1, 4), (2, 5));
        AssertPts(nay.ConvertToPta2(nax), (4, 1), (5, 2));
    }

    [Fact]
    public void PtaConvertToNuma_RoundTrip()
    {
        Pta pta = P((1.5f, 2f), (3f, -4.25f));
        pta.ConvertToNuma(out Numa nax, out Numa nay);
        AssertNuma(nax, 1.5f, 3f);
        AssertNuma(nay, 2f, -4.25f);
        AssertPts(nax.ConvertToPta2(nay), (1.5f, 2f), (3f, -4.25f));
        P().ConvertToNuma(out Numa empty1, out Numa empty2);
        Assert.Equal((0, 0), (empty1.Count, empty2.Count));
    }

    /*------------------------------ Display -----------------------------*/

    [Fact]
    public void ReplicatePattern_WithPoints()
    {
        Pta ptas = P((5f, 5f), (0f, 0f));
        Pta ptap = P((0f, 0f), (1f, 0f), (0f, 1f));
        AssertPts(ptas.ReplicatePattern(null, ptap, 0, 0, 10, 10),
                  (5, 5), (6, 5), (5, 6), (0, 0), (1, 0), (0, 1));
        /* reference point (1, 1): the pattern around (0, 0) is outside the image */
        AssertPts(ptas.ReplicatePattern(null, ptap, 1, 1, 10, 10), (4, 4), (5, 4), (4, 5));
        /* clipping to w = 6, h = 6 */
        AssertPts(ptas.ReplicatePattern(null, ptap, 0, 0, 6, 6),
                  (5, 5), (0, 0), (1, 0), (0, 1));
    }

    [Fact]
    public void ReplicatePattern_WithPix_AndErrors()
    {
        Pta ptas = P((5f, 5f), (0f, 0f));
        /* pattern pixels (0, 0) and (2, 1), reference point (1, 1) */
        Pix pixp = MakeBitPix(3, 3, (0, 0), (2, 1));
        AssertPts(ptas.ReplicatePattern(pixp, null, 1, 1, 10, 10), (4, 4), (6, 5), (1, 0));
        /* ptap wins over pixp when both are given */
        Pta ptap = P((0f, 0f));
        AssertPts(ptas.ReplicatePattern(pixp, ptap, 0, 0, 10, 10), (5, 5), (0, 0));
        Assert.Throws<ArgumentException>(() => ptas.ReplicatePattern(null, null, 0, 0, 10, 10));
        Assert.Equal(0, P().ReplicatePattern(null, ptap, 0, 0, 10, 10).Count);
    }

    [Fact]
    public void Display_ArgumentChecks()
    {
        Pta pta = P((1f, 1f));
        Pix rgb = Pix.Create(4, 4, 32);
        Pix other = Pix.Create(4, 4, 32);
        Pix gray = Pix.Create(4, 4, 8);
        Pix pat = MakeBitPix(2, 2, (0, 0));
        Ptaa ptaa = Ptaa.Create(0);
        ptaa.AddPta(pta, L.L_INSERT);

        /* pixd must be pixs, and 32 bpp */
        Assert.Throws<ArgumentException>(() => Pix.DisplayPta(rgb, other, pta));
        Assert.Throws<ArgumentException>(() => Pix.DisplayPta(gray, gray, pta));
        Assert.Throws<ArgumentException>(() => Pix.DisplayPtaPattern(rgb, other, pta, pat, 0, 0, 0xff000000));
        Assert.Throws<ArgumentException>(() => Pix.DisplayPtaPattern(gray, gray, pta, pat, 0, 0, 0xff000000));
        Assert.Throws<ArgumentException>(() => Pix.DisplayPtaaPattern(rgb, other, ptaa, pat, 0, 0));
        Assert.Throws<ArgumentException>(() => Pix.DisplayPtaaPattern(gray, gray, ptaa, pat, 0, 0));
        /* no paths */
        Assert.Throws<ArgumentException>(() => rgb.DisplayPtaa(Ptaa.Create(0)));
    }
}
