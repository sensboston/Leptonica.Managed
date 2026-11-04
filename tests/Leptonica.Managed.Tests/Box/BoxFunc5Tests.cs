using System;
using System.Linq;
using Leptonica;

namespace Leptonica.Managed.Tests;

/// <summary>Tests for boxfunc5.c with hand-computed expected values.</summary>
public class BoxFunc5Tests
{
    private static Boxa MakeBoxa(params (int x, int y, int w, int h)[] boxes)
    {
        Boxa boxa = Boxa.Create(0);
        foreach (var b in boxes)
            boxa.AddBox(Box.Create(b.x, b.y, b.w, b.h), L.L_INSERT);
        return boxa;
    }

    private static (int, int, int, int) G(Boxa boxa, int i)
    {
        boxa.GetBoxGeometry(i, out int x, out int y, out int w, out int h);
        return (x, y, w, h);
    }

    private static (int, int, int, int)[] GA(Boxa boxa) =>
        Enumerable.Range(0, boxa.Count).Select(i => G(boxa, i)).ToArray();

    private static readonly (int, int, int, int) Inv = (0, 0, 0, 0);

    [Fact]
    public void FillSequence_AllBoxes_UsesNearestValid()
    {
        Boxa boxa = MakeBoxa((10, 10, 20, 20), Inv, Inv, (30, 30, 5, 5), Inv);
        Boxa filled = boxa.FillSequence(L.L_USE_ALL_BOXES, 0);
        Assert.Equal(new[] { (10, 10, 20, 20), (10, 10, 20, 20), (30, 30, 5, 5),
                             (30, 30, 5, 5), (30, 30, 5, 5) }, GA(filled));
        // the source is unchanged
        Assert.Equal(Inv, G(boxa, 1));
    }

    [Fact]
    public void FillSequence_SameParity()
    {
        Boxa boxa = MakeBoxa((10, 10, 20, 20), Inv, Inv, (30, 30, 5, 5), Inv);
        Boxa filled = boxa.FillSequence(L.L_USE_SAME_PARITY_BOXES, 0);
        Assert.Equal(new[] { (10, 10, 20, 20), (30, 30, 5, 5), (10, 10, 20, 20),
                             (30, 30, 5, 5), (10, 10, 20, 20) }, GA(filled));
    }

    [Fact]
    public void FillSequence_AllValid_ReturnsCopy_AndValidatesFlag()
    {
        Boxa boxa = MakeBoxa((1, 2, 3, 4), (5, 6, 7, 8));
        Boxa copy = boxa.FillSequence(L.L_USE_ALL_BOXES, 0);
        Assert.NotSame(boxa, copy);
        Assert.Equal(GA(boxa), GA(copy));
        Assert.Throws<ArgumentException>(() => boxa.FillSequence(9, 0));
    }

    [Theory]
    [InlineData(L.L_USE_MINSIZE, 1, 0, 12, 10, 18, 20)]
    [InlineData(L.L_USE_MAXSIZE, 1, 0, 10, 8, 22, 26)]
    [InlineData(L.L_SUB_ON_LOC_DIFF, 1, 2, 10, 6, 24, 30)]
    [InlineData(L.L_SUB_ON_LOC_DIFF, 2, 2, 10, 10, 20, 26)]
    [InlineData(L.L_SUB_ON_SIZE_DIFF, 1, 0, 10, 8, 20, 26)]
    [InlineData(L.L_USE_CAPPED_MIN, 1, 0, 12, 9, 19, 24)]
    [InlineData(L.L_USE_CAPPED_MAX, 1, 0, 11, 8, 21, 26)]
    public void ModifyWithBoxa_Rules(int subflag, int maxdiff, int extra,
                                     int x, int y, int w, int h)
    {
        Boxa boxas = MakeBoxa((10, 10, 20, 20));
        Boxa boxam = MakeBoxa((12, 8, 20, 26));
        Boxa boxad = boxas.ModifyWithBoxa(boxam, subflag, maxdiff, extra);
        Assert.Equal((x, y, w, h), G(boxad, 0));
    }

    [Fact]
    public void ModifyWithBoxa_InvalidBoxesAndMismatches()
    {
        Boxa boxas = MakeBoxa((10, 10, 20, 20), (1, 1, 5, 5));
        Boxa boxam = MakeBoxa(Inv, (2, 2, 5, 5));
        Boxa boxad = boxas.ModifyWithBoxa(boxam, L.L_USE_MINSIZE, 0, 0);
        Assert.Equal(Inv, G(boxad, 0));
        Assert.Equal((2, 2, 4, 4), G(boxad, 1));

        Assert.Equal(GA(boxas), GA(boxas.ModifyWithBoxa(null, L.L_USE_MINSIZE, 0, 0)));
        Assert.Equal(GA(boxas), GA(boxas.ModifyWithBoxa(MakeBoxa((1, 1, 1, 1)), L.L_USE_MINSIZE, 0, 0)));
        Assert.Equal(GA(boxas), GA(boxas.ModifyWithBoxa(boxam, 99, 0, 0)));
    }

    [Fact]
    public void WindowedMedian_RemovesOutlier()
    {
        var list = Enumerable.Repeat((10, 20, 30, 40), 7).ToArray();
        list[3] = (50, 60, 70, 80);
        Boxa boxad = MakeBoxa(list).WindowedMedian(1, 0);
        Assert.All(GA(boxad), b => Assert.Equal((10, 20, 30, 40), b));
    }

    [Fact]
    public void WindowedMedian_TooFewBoxesOrBadWindow_ReturnsCopy()
    {
        Boxa two = MakeBoxa((1, 1, 2, 2), (3, 3, 4, 4));
        Assert.Equal(GA(two), GA(two.WindowedMedian(1, 0)));
        Boxa five = MakeBoxa((1, 1, 2, 2), (3, 3, 4, 4), (5, 5, 6, 6), (7, 7, 8, 8), (9, 9, 1, 1));
        Assert.Equal(GA(five), GA(five.WindowedMedian(0, 0)));
    }

    [Fact]
    public void SmoothSequenceMedian_FixesOddOutlier()
    {
        var list = Enumerable.Repeat((10, 20, 30, 40), 8).ToArray();
        list[3] = (50, 20, 30, 40);
        Boxa boxad = MakeBoxa(list).SmoothSequenceMedian(1, L.L_SUB_ON_LOC_DIFF, 5, 0, 0);
        Assert.All(GA(boxad), b => Assert.Equal((10, 20, 30, 40), b));
    }

    [Fact]
    public void SmoothSequenceMedian_InvalidParams_ReturnCopy()
    {
        Boxa five = MakeBoxa((1, 1, 2, 2), (3, 3, 4, 4), (5, 5, 6, 6), (7, 7, 8, 8), (9, 9, 1, 1));
        Assert.Equal(GA(five), GA(five.SmoothSequenceMedian(1, L.L_USE_MINSIZE, 0, 0, 0)));   // < 6 boxes
        Boxa six = MakeBoxa((1, 1, 2, 2), (3, 3, 4, 4), (5, 5, 6, 6), (7, 7, 8, 8), (9, 9, 1, 1), (2, 2, 2, 2));
        Assert.Equal(GA(six), GA(six.SmoothSequenceMedian(0, L.L_USE_MINSIZE, 0, 0, 0)));
        Assert.Equal(GA(six), GA(six.SmoothSequenceMedian(1, L.L_USE_MINSIZE, -1, 0, 0)));
        Assert.Equal(GA(six), GA(six.SmoothSequenceMedian(1, 77, 0, 0, 0)));
    }

    [Fact]
    public void ReconcilePairWidth_ChooseMin()
    {
        Boxa boxas = MakeBoxa((0, 0, 100, 50), (200, 0, 80, 50));
        Boxa boxad = boxas.ReconcilePairWidth(10, L.L_ADJUST_CHOOSE_MIN, 1.0f, null);
        Assert.Equal(new[] { (20, 0, 80, 50), (200, 0, 80, 50) }, GA(boxad));

        boxas = MakeBoxa((0, 0, 80, 50), (200, 0, 100, 50));
        boxad = boxas.ReconcilePairWidth(10, L.L_ADJUST_CHOOSE_MIN, 1.0f, null);
        Assert.Equal(new[] { (0, 0, 80, 50), (200, 0, 80, 50) }, GA(boxad));
    }

    [Fact]
    public void ReconcilePairWidth_ChooseMax()
    {
        Boxa boxas = MakeBoxa((50, 0, 100, 50), (200, 0, 130, 50));
        Boxa boxad = boxas.ReconcilePairWidth(10, L.L_ADJUST_CHOOSE_MAX, 1.0f, null);
        // the even box grows to the left to width 130
        Assert.Equal(new[] { (20, 0, 130, 50), (200, 0, 130, 50) }, GA(boxad));

        boxas = MakeBoxa((0, 0, 130, 50), (200, 0, 100, 50));
        boxad = boxas.ReconcilePairWidth(10, L.L_ADJUST_CHOOSE_MAX, 1.0f, null);
        Assert.Equal(new[] { (0, 0, 130, 50), (200, 0, 130, 50) }, GA(boxad));
    }

    [Fact]
    public void ReconcilePairWidth_IndicatorArrayAndSmallDifferences()
    {
        Boxa boxas = MakeBoxa((0, 0, 100, 50), (200, 0, 80, 50));
        Numa na = Numa.Create(2);
        na.AddNumber(0);
        na.AddNumber(1);
        Boxa boxad = boxas.ReconcilePairWidth(10, L.L_ADJUST_CHOOSE_MIN, 1.0f, na);
        Assert.Equal(GA(boxas), GA(boxad));
        // difference below the threshold
        boxad = boxas.ReconcilePairWidth(50, L.L_ADJUST_CHOOSE_MIN, 1.0f, null);
        Assert.Equal(GA(boxas), GA(boxad));
    }

    [Fact]
    public void SizeConsistency_Decisions()
    {
        var same = Enumerable.Repeat((0, 0, 100, 200), 10).ToArray();
        MakeBoxa(same).SizeConsistency(L.L_CHECK_WIDTH, 0, 0, out float fvarp, out float fvarm, out int decision);
        Assert.Equal((0f, 0f, 1), (fvarp, fvarm, decision));

        // same pairs, but one pair of pages is wider: median deviation is large
        var diff = same.ToArray();
        diff[6] = (0, 0, 150, 200);
        diff[7] = (0, 0, 150, 200);
        MakeBoxa(diff).SizeConsistency(L.L_CHECK_WIDTH, 0, 0, out fvarp, out fvarm, out decision);
        Assert.Equal(0f, fvarp);
        Assert.Equal(0.1f, fvarm, 4);
        Assert.Equal(0, decision);

        // large pairwise variation: unknown
        var odd = same.ToArray();
        odd[1] = (0, 0, 150, 200);
        MakeBoxa(odd).SizeConsistency(L.L_CHECK_WIDTH, 0, 0, out fvarp, out _, out decision);
        Assert.True(fvarp > 0.02f);
        Assert.Equal(-1, decision);

        // heights
        MakeBoxa(same).SizeConsistency(L.L_CHECK_HEIGHT, 0, 0, out _, out _, out decision);
        Assert.Equal(1, decision);
    }

    [Fact]
    public void SizeConsistency_Validates()
    {
        Boxa few = MakeBoxa((0, 0, 1, 1), (0, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => few.SizeConsistency(L.L_CHECK_WIDTH, 0, 0, out _, out _, out _));
        Boxa ten = MakeBoxa(Enumerable.Repeat((0, 0, 100, 200), 10).ToArray());
        Assert.Throws<ArgumentException>(() => ten.SizeConsistency(9, 0, 0, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => ten.SizeConsistency(L.L_CHECK_WIDTH, 0.6f, 0, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => ten.SizeConsistency(L.L_CHECK_WIDTH, 0, -0.1f, out _, out _, out _));
    }

    [Fact]
    public void ReconcileSidesByMedian_Left()
    {
        Boxa boxas = MakeBoxa((10, 5, 50, 20), (10, 5, 50, 20), (10, 5, 50, 20), (10, 5, 50, 20),
                              (40, 5, 50, 20), (10, 5, 50, 20), (12, 5, 50, 20));
        Boxa boxad = boxas.ReconcileSidesByMedian(L.L_ADJUST_LEFT, 5, 0, null);
        // the outlier left side moves to the median 10, the right edge stays at 89
        Assert.Equal((10, 5, 80, 20), G(boxad, 4));
        Assert.Equal((12, 5, 50, 20), G(boxad, 6));
        Assert.Equal((10, 5, 50, 20), G(boxad, 0));

        boxad = boxas.ReconcileSidesByMedian(L.L_ADJUST_LEFT, 5, 3, null);
        Assert.Equal((7, 5, 83, 20), G(boxad, 4));
    }

    [Fact]
    public void ReconcileSidesByMedian_RightTopBottom()
    {
        Boxa boxas = MakeBoxa((10, 10, 50, 20), (10, 10, 50, 20), (10, 10, 50, 20), (10, 10, 50, 20),
                              (10, 10, 90, 60), (10, 10, 50, 20), (10, 10, 50, 20));
        Boxa r = boxas.ReconcileSidesByMedian(L.L_ADJUST_RIGHT, 5, 0, null);
        Assert.Equal((10, 10, 50, 60), G(r, 4));
        Boxa b = boxas.ReconcileSidesByMedian(L.L_ADJUST_BOT, 5, 0, null);
        Assert.Equal((10, 10, 90, 20), G(b, 4));
        // top: the outlier top side is the same, so nothing changes
        Boxa t = boxas.ReconcileSidesByMedian(L.L_ADJUST_TOP, 5, 0, null);
        Assert.Equal(GA(boxas), GA(t));
        Boxa lr = boxas.ReconcileSidesByMedian(L.L_ADJUST_LEFT_AND_RIGHT, 5, 0, null);
        Assert.Equal((10, 10, 50, 60), G(lr, 4));
        Boxa tb = boxas.ReconcileSidesByMedian(L.L_ADJUST_TOP_AND_BOT, 5, 0, null);
        Assert.Equal((10, 10, 90, 20), G(tb, 4));
    }

    [Fact]
    public void ReconcileSidesByMedian_InvalidArgs_ReturnCopy()
    {
        Boxa boxas = MakeBoxa((10, 5, 50, 20), (10, 5, 50, 20), (10, 5, 50, 20));
        Assert.Equal(GA(boxas), GA(boxas.ReconcileSidesByMedian(77, 5, 0, null)));
        Assert.Equal(GA(boxas), GA(boxas.ReconcileSidesByMedian(L.L_ADJUST_LEFT, -1, 0, null)));
        Boxa two = MakeBoxa((10, 5, 50, 20), (10, 5, 50, 20));
        Assert.Equal(GA(two), GA(two.ReconcileSidesByMedian(L.L_ADJUST_LEFT, 5, 0, null)));
    }

    [Fact]
    public void ReconcileAllByMedian_NoOutliers_IsIdentity_AndSidesApplied()
    {
        var even = (10, 20, 100, 200);
        var odd = (300, 20, 100, 200);
        var list = Enumerable.Range(0, 8).Select(i => i % 2 == 0 ? even : odd).ToArray();
        Boxa boxas = MakeBoxa(list);
        Boxa boxad = boxas.ReconcileAllByMedian(L.L_ADJUST_LEFT_AND_RIGHT, L.L_ADJUST_TOP_AND_BOT, 3, 0, null);
        Assert.Equal(GA(boxas), GA(boxad));

        list[2] = (40, 20, 100, 200);    // even box with a shifted left side (right edge 139 vs. 109)
        boxad = MakeBoxa(list).ReconcileAllByMedian(L.L_ADJUST_LEFT_AND_RIGHT, L.L_ADJUST_SKIP, 3, 0, null);
        Assert.Equal(even, G(boxad, 2));
    }

    [Fact]
    public void ReconcileAllByMedian_BadParams_ReturnCopy()
    {
        Boxa boxas = MakeBoxa((1, 1, 5, 5), (2, 2, 5, 5), (3, 3, 5, 5));
        Assert.Equal(GA(boxas), GA(boxas.ReconcileAllByMedian(L.L_ADJUST_LEFT, L.L_ADJUST_SKIP, 3, 0, null)));
        Assert.Equal(GA(boxas), GA(boxas.ReconcileAllByMedian(L.L_ADJUST_SKIP, L.L_ADJUST_LEFT, 3, 0, null)));
        Assert.Equal(GA(boxas), GA(boxas.ReconcileAllByMedian(L.L_ADJUST_SKIP, L.L_ADJUST_SKIP, -1, 0, null)));
    }

    [Fact]
    public void MedianDimensions_ReportsMediansAndDeviations()
    {
        var list = Enumerable.Repeat((0, 0, 100, 200), 8).ToArray();
        list[7] = (0, 0, 150, 200);
        Boxa boxa = MakeBoxa(list);
        boxa.MedianDimensions(out int medw, out int medh, out int medwe, out int medwo,
                              out int medhe, out int medho, out Numa? nadelw, out Numa? nadelh);
        Assert.Equal((100, 200, 100, 100, 200, 200), (medw, medh, medwe, medwo, medhe, medho));
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 0, 50 },
                     Enumerable.Range(0, 8).Select(i => nadelw!.GetIValue(i)).ToArray());
        Assert.All(Enumerable.Range(0, 8), i => Assert.Equal(0, nadelh!.GetIValue(i)));
        Assert.Throws<ArgumentException>(() =>
            MakeBoxa((0, 0, 1, 1), (0, 0, 1, 1)).MedianDimensions(out _, out _, out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void ReconcileSizeByMedian_Width_FixesOutlier()
    {
        var list = Enumerable.Repeat((10, 20, 100, 200), 8).ToArray();
        list[7] = (10, 20, 150, 200);
        Boxa boxa = MakeBoxa(list);
        Boxa boxad = boxa.ReconcileSizeByMedian(L.L_CHECK_WIDTH, 0.05f, 0.04f, 1.0f,
                                                out Numa? nadelw, out Numa? nadelh, out float ratio);
        Assert.Equal((10, 20, 100, 200), G(boxad, 7));
        Assert.Equal(0.5f, ratio);
        Assert.Equal(50, nadelw!.GetIValue(7));
        Assert.Null(nadelh);
        for (int i = 0; i < 7; i++)
            Assert.Equal((10, 20, 100, 200), G(boxad, i));

        boxad = boxa.ReconcileSizeByMedian(L.L_CHECK_WIDTH, 0.05f, 0.04f, 1.1f, out _, out _, out _);
        Assert.Equal((5, 20, 110, 200), G(boxad, 7));
    }

    [Fact]
    public void ReconcileSizeByMedian_Height_FixesOutlier()
    {
        var list = Enumerable.Repeat((10, 20, 100, 200), 8).ToArray();
        list[2] = (10, 20, 100, 260);
        Boxa boxad = MakeBoxa(list).ReconcileSizeByMedian(L.L_CHECK_HEIGHT, 0.05f, 0.04f, 1.0f,
                                                          out Numa? nadelw, out Numa? nadelh, out _);
        Assert.Equal((10, 20, 100, 200), G(boxad, 2));
        Assert.Null(nadelw);
        Assert.Equal(60, nadelh!.GetIValue(2));
    }

    [Fact]
    public void ReconcileSizeByMedian_Both_AndNoOutliers()
    {
        var list = Enumerable.Repeat((10, 20, 100, 200), 8).ToArray();
        list[1] = (10, 20, 100, 300);
        list[4] = (10, 20, 160, 200);
        Boxa boxad = MakeBoxa(list).ReconcileSizeByMedian(L.L_CHECK_BOTH, 0.05f, 0.04f, 1.0f,
                                                          out Numa? nadelw, out Numa? nadelh, out float ratio);
        Assert.Equal((10, 20, 100, 200), G(boxad, 1));
        Assert.Equal((10, 20, 100, 200), G(boxad, 4));
        Assert.NotNull(nadelw);
        Assert.NotNull(nadelh);
        Assert.Equal(0.5f, ratio);

        Boxa same = MakeBoxa(Enumerable.Repeat((10, 20, 100, 200), 8).ToArray());
        Boxa copy = same.ReconcileSizeByMedian(L.L_CHECK_BOTH, 0.05f, 0.04f, 1.0f, out _, out _, out _);
        Assert.Equal(GA(same), GA(copy));
    }

    [Fact]
    public void ReconcileSizeByMedian_BadParams_ReturnCopy()
    {
        Boxa boxa = MakeBoxa(Enumerable.Repeat((10, 20, 100, 200), 8).ToArray());
        Assert.Equal(GA(boxa), GA(boxa.ReconcileSizeByMedian(9, 0.05f, 0.04f, 1.0f, out _, out _, out _)));
        Assert.Equal(GA(boxa), GA(boxa.ReconcileSizeByMedian(L.L_CHECK_WIDTH, 0f, 0.04f, 1.0f, out _, out _, out _)));
        Assert.Equal(GA(boxa), GA(boxa.ReconcileSizeByMedian(L.L_CHECK_WIDTH, 0.05f, 0.5f, 1.0f, out _, out _, out _)));
        Boxa few = MakeBoxa((10, 20, 100, 200), (10, 20, 100, 200));
        Assert.Equal(GA(few), GA(few.ReconcileSizeByMedian(L.L_CHECK_WIDTH, 0.05f, 0.04f, 1.0f, out _, out _, out _)));
    }

    [Fact]
    public void SizeVariation_Statistics()
    {
        // even widths 10, 20, 30; odd widths 10, 10, 10
        Boxa boxa = MakeBoxa((0, 0, 10, 5), (0, 0, 10, 5), (0, 0, 20, 5), (0, 0, 10, 5),
                             (0, 0, 30, 5), (0, 0, 10, 5));
        boxa.SizeVariation(L.L_SELECT_WIDTH, out float del, out float rmse, out float rmso, out float rmsa);
        Assert.Equal((0f + 10f + 20f) / 3f, del, 4);
        Assert.Equal(Math.Sqrt(200.0 / 3.0), rmse, 3);   // sqrt of the variance of {10,20,30}: 66.67
        Assert.Equal(0f, rmso);
        Assert.True(rmsa > 0f);
        boxa.SizeVariation(L.L_SELECT_HEIGHT, out del, out rmse, out rmso, out rmsa);
        Assert.Equal((0f, 0f, 0f, 0f), (del, rmse, rmso, rmsa));
    }

    [Fact]
    public void SizeVariation_Validates()
    {
        Boxa boxa = MakeBoxa((0, 0, 1, 1), (0, 0, 1, 1), (0, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => boxa.SizeVariation(L.L_SELECT_WIDTH, out _, out _, out _, out _));
        Boxa four = MakeBoxa((0, 0, 1, 1), (0, 0, 1, 1), (0, 0, 1, 1), (0, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => four.SizeVariation(9, out _, out _, out _, out _));
    }

    [Fact]
    public void PlotSides_Validates()
    {
        Boxa one = MakeBoxa((0, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => one.PlotSides(null, out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => one.PlotSizes(null, out _, out _, out _));
    }
}
