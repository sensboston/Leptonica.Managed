using System;
using Leptonica;

namespace Leptonica.Managed.Tests;

public class NumaFunc1Tests
{
    private static Numa MakeNuma(params float[] values)
    {
        Numa na = Numa.Create(0);
        foreach (float v in values)
            na.AddNumber(v);
        return na;
    }

    private static float[] Values(Numa na)
    {
        float[] values = new float[na.Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = na.GetFValue(i);
        return values;
    }

    /*------------------------- arithmetic and logic -------------------------*/

    [Fact]
    public void ArithOp_AllOperations()
    {
        Numa na1 = MakeNuma(1, 2, 3);
        Numa na2 = MakeNuma(4, 5, 6);

        Assert.Equal(new float[] { 5, 7, 9 }, Values(Numa.ArithOp(null, na1, na2, L.L_ARITH_ADD)));
        Assert.Equal(new float[] { -3, -3, -3 }, Values(Numa.ArithOp(null, na1, na2, L.L_ARITH_SUBTRACT)));
        Assert.Equal(new float[] { 4, 10, 18 }, Values(Numa.ArithOp(null, na1, na2, L.L_ARITH_MULTIPLY)));
        Assert.Equal(new float[] { 0.25f, 0.4f, 0.5f }, Values(Numa.ArithOp(null, na1, na2, L.L_ARITH_DIVIDE)));
        Assert.Equal(new float[] { 1, 2, 3 }, Values(na1));   /* sources unchanged */
    }

    [Fact]
    public void ArithOp_InPlaceReturnsNa1()
    {
        Numa na1 = MakeNuma(1, 2, 3);
        Numa na2 = MakeNuma(10, 20, 30);
        Numa nad = Numa.ArithOp(na1, na1, na2, L.L_ARITH_ADD);
        Assert.Same(na1, nad);
        Assert.Equal(new float[] { 11, 22, 33 }, Values(na1));
    }

    [Fact]
    public void ArithOp_InvalidInput()
    {
        Numa na1 = MakeNuma(1, 2, 3);
        Assert.Throws<ArgumentException>(() => Numa.ArithOp(null, na1, MakeNuma(1, 2), L.L_ARITH_ADD));
        Assert.Throws<ArgumentException>(() => Numa.ArithOp(MakeNuma(1, 2, 3), na1, MakeNuma(1, 2, 3), L.L_ARITH_ADD));
        Assert.Throws<ArgumentException>(() => Numa.ArithOp(null, na1, MakeNuma(1, 2, 3), 0));
        Assert.Throws<ArgumentException>(() => Numa.ArithOp(null, na1, MakeNuma(1, 2, 3), L.L_UNION));
        Assert.Throws<ArgumentException>(() => Numa.ArithOp(null, na1, MakeNuma(1, 0, 3), L.L_ARITH_DIVIDE));
        /* a zero in na2 is fine for the other operations */
        Assert.Equal(new float[] { 1, 2, 3 }, Values(Numa.ArithOp(null, na1, MakeNuma(0, 0, 0), L.L_ARITH_ADD)));
    }

    [Fact]
    public void ArithOp_EmptyNumas()
    {
        Numa nad = Numa.ArithOp(null, Numa.Create(0), Numa.Create(0), L.L_ARITH_DIVIDE);
        Assert.Equal(0, nad.Count);
    }

    [Fact]
    public void LogicalOp_AllOperations()
    {
        Numa na1 = MakeNuma(0, 1, 0, 1);
        Numa na2 = MakeNuma(0, 0, 1, 1);

        Assert.Equal(new float[] { 0, 1, 1, 1 }, Values(Numa.LogicalOp(null, na1, na2, L.L_UNION)));
        Assert.Equal(new float[] { 0, 0, 0, 1 }, Values(Numa.LogicalOp(null, na1, na2, L.L_INTERSECTION)));
        Assert.Equal(new float[] { 0, 1, 0, 0 }, Values(Numa.LogicalOp(null, na1, na2, L.L_SUBTRACTION)));
        Assert.Equal(new float[] { 0, 1, 1, 0 }, Values(Numa.LogicalOp(null, na1, na2, L.L_EXCLUSIVE_OR)));
    }

    [Fact]
    public void LogicalOp_UsesRoundedIntegers()
    {
        /* 2.4 -> 2 (true), 0.4 -> 0 (false), -3 -> true */
        Numa na1 = MakeNuma(2.4f, 0.4f, -3, 0);
        Numa na2 = MakeNuma(0.4f, 0.4f, 7, 0);
        Assert.Equal(new float[] { 1, 0, 1, 0 }, Values(Numa.LogicalOp(null, na1, na2, L.L_UNION)));
        Assert.Equal(new float[] { 1, 0, 0, 0 }, Values(Numa.LogicalOp(null, na1, na2, L.L_EXCLUSIVE_OR)));
    }

    [Fact]
    public void LogicalOp_InPlaceAndInvalidInput()
    {
        Numa na1 = MakeNuma(1, 1, 0);
        Numa na2 = MakeNuma(0, 1, 0);
        Numa nad = Numa.LogicalOp(na1, na1, na2, L.L_SUBTRACTION);
        Assert.Same(na1, nad);
        Assert.Equal(new float[] { 1, 0, 0 }, Values(na1));

        Assert.Throws<ArgumentException>(() => Numa.LogicalOp(null, na1, MakeNuma(1), L.L_UNION));
        Assert.Throws<ArgumentException>(() => Numa.LogicalOp(MakeNuma(0, 0, 0), na1, na2, L.L_UNION));
        Assert.Throws<ArgumentException>(() => Numa.LogicalOp(null, na1, na2, L.L_ARITH_ADD));
    }

    [Fact]
    public void Invert_FlipsIndicators()
    {
        Numa nas = MakeNuma(0, 1, 2, 0.4f);
        Assert.Equal(new float[] { 1, 0, 0, 1 }, Values(Numa.Invert(null, nas)));
        Assert.Equal(new float[] { 0, 1, 2, 0.4f }, Values(nas));

        Numa nad = Numa.Invert(nas, nas);
        Assert.Same(nas, nad);
        Assert.Equal(new float[] { 1, 0, 0, 1 }, Values(nas));
        Assert.Throws<ArgumentException>(() => Numa.Invert(MakeNuma(0), nas));
    }

    [Fact]
    public void Similar_UsesMaxdiff()
    {
        Numa na1 = MakeNuma(1, 2, 3);
        Numa na2 = MakeNuma(1, 2.05f, 3);
        Assert.True(na1.Similar(na2, 0.1f));
        Assert.True(na1.Similar(na2, -0.1f));   /* the sign of maxdiff is ignored */
        Assert.False(na1.Similar(na2, 0.01f));
        Assert.False(na1.Similar(na2, 0.0f));
        Assert.True(na1.Similar(MakeNuma(1, 2, 3), 0.0f));
        Assert.False(na1.Similar(MakeNuma(1, 2), 100.0f));   /* sizes differ */
        Assert.True(Numa.Create(0).Similar(Numa.Create(0), 0.0f));
    }

    [Fact]
    public void AddToNumber_Accumulates()
    {
        Numa na = Numa.MakeConstant(0.0f, 3);
        na.AddToNumber(1, 2.5f);
        na.AddToNumber(1, 1.0f);
        na.AddToNumber(2, -4.0f);
        Assert.Equal(new float[] { 0, 3.5f, -4 }, Values(na));
        Assert.Throws<ArgumentException>(() => na.AddToNumber(3, 1.0f));
        Assert.Throws<ArgumentException>(() => na.AddToNumber(-1, 1.0f));
        Assert.Throws<ArgumentException>(() => Numa.Create(0).AddToNumber(0, 1.0f));
    }

    /*--------------------------- simple extractions ---------------------------*/

    [Fact]
    public void GetMinMax_FirstOccurrence()
    {
        Numa na = MakeNuma(3, -1, 7, -1, 7);
        na.GetMin(out float minval, out int iminloc);
        Assert.Equal(-1.0f, minval);
        Assert.Equal(1, iminloc);
        na.GetMax(out float maxval, out int imaxloc);
        Assert.Equal(7.0f, maxval);
        Assert.Equal(2, imaxloc);
    }

    [Fact]
    public void GetMinMax_StartFromOneBillion()
    {
        /* the search starts from +/-1e9, so larger magnitudes are not found */
        Numa na = MakeNuma(2e9f, 3e9f);
        na.GetMin(out float minval, out int iminloc);
        Assert.Equal(1000000000.0f, minval);
        Assert.Equal(0, iminloc);
        Numa nb = MakeNuma(-2e9f, -3e9f);
        nb.GetMax(out float maxval, out int imaxloc);
        Assert.Equal(-1000000000.0f, maxval);
        Assert.Equal(0, imaxloc);
    }

    [Fact]
    public void GetMinMax_EmptyThrows()
    {
        Numa na = Numa.Create(0);
        Assert.Throws<ArgumentException>(() => na.GetMin(out _, out _));
        Assert.Throws<ArgumentException>(() => na.GetMax(out _, out _));
    }

    [Fact]
    public void GetSum_AndMean()
    {
        Assert.Equal(3.0f, MakeNuma(1.5f, 2.5f, -1).GetSum());
        Assert.Equal(2.5f, MakeNuma(1, 2, 3, 4).GetMean());
        Assert.Equal(2.0f, MakeNuma(-1, -3, 2).GetMeanAbsval());
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetSum());
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetMean());
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetMeanAbsval());
    }

    [Fact]
    public void GetPartialSums_RunningSums()
    {
        Numa nasum = MakeNuma(1, 2, 3, 4).GetPartialSums();
        Assert.Equal(new float[] { 1, 3, 6, 10 }, Values(nasum));
        Assert.Equal(0, Numa.Create(0).GetPartialSums().Count);   /* warning only */
    }

    [Fact]
    public void GetSumOnInterval_Clamps()
    {
        Numa na = MakeNuma(1, 2, 3, 4, 5);
        Assert.Equal(9.0f, na.GetSumOnInterval(1, 3));
        Assert.Equal(15.0f, na.GetSumOnInterval(-5, -1));
        Assert.Equal(9.0f, na.GetSumOnInterval(3, 100));
        Assert.Equal(3.0f, na.GetSumOnInterval(2, 2));
        Assert.Equal(0.0f, na.GetSumOnInterval(5, 10));    /* first >= n is not an error */
        Assert.Equal(0.0f, na.GetSumOnInterval(0, -2));    /* last < -1 is not an error */
        Assert.Equal(0.0f, na.GetSumOnInterval(3, 1));     /* empty interval */
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetSumOnInterval(0, -1));
    }

    [Fact]
    public void HasOnlyIntegers_Tests()
    {
        Assert.True(MakeNuma(1, -2, 3.0f).HasOnlyIntegers());
        Assert.False(MakeNuma(1, 2.5f).HasOnlyIntegers());
        Assert.False(MakeNuma(-0.5f).HasOnlyIntegers());
        Assert.Throws<ArgumentException>(() => Numa.Create(0).HasOnlyIntegers());
    }

    [Fact]
    public void Subsample_TakesEveryNth()
    {
        Numa na = MakeNuma(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        Assert.Equal(new float[] { 0, 3, 6, 9 }, Values(na.Subsample(3)));
        Assert.Equal(new float[] { 0, 5 }, Values(na.Subsample(5)));
        Assert.Equal(Values(na), Values(na.Subsample(1)));
        Assert.Equal(new float[] { 0 }, Values(na.Subsample(100)));
        Assert.Equal(0, Numa.Create(0).Subsample(2).Count);
        Assert.Throws<ArgumentException>(() => na.Subsample(0));
    }

    [Fact]
    public void MakeDelta_Differences()
    {
        Assert.Equal(new float[] { 3, 5, 7 }, Values(MakeNuma(1, 4, 9, 16).MakeDelta()));
        Assert.Equal(0, MakeNuma(5).MakeDelta().Count);
        Assert.Equal(0, Numa.Create(0).MakeDelta().Count);
    }

    [Fact]
    public void MakeSequenceAndConstant()
    {
        Assert.Equal(new float[] { 1, 1.5f, 2, 2.5f }, Values(Numa.MakeSequence(1, 0.5f, 4)));
        Assert.Equal(new float[] { 10, 7, 4 }, Values(Numa.MakeSequence(10, -3, 3)));
        Assert.Equal(new float[] { 7, 7, 7 }, Values(Numa.MakeConstant(7, 3)));
        Assert.Equal(0, Numa.MakeConstant(7, 0).Count);
        Assert.Equal(0, Numa.MakeSequence(0, 1, -2).Count);
    }

    [Fact]
    public void MakeAbsval_NewAndInPlace()
    {
        Numa nas = MakeNuma(-1.5f, 2, -0.0f, -7);
        Numa nad = Numa.MakeAbsval(null, nas);
        Assert.Equal(new float[] { 1.5f, 2, -0.0f, 7 }, Values(nad));   /* L_ABS keeps -0.0 */
        Assert.Equal(new float[] { -1.5f, 2, -0.0f, -7 }, Values(nas));

        Assert.Same(nas, Numa.MakeAbsval(nas, nas));
        Assert.Equal(new float[] { 1.5f, 2, -0.0f, 7 }, Values(nas));
        Assert.Throws<ArgumentException>(() => Numa.MakeAbsval(MakeNuma(1), nas));
    }

    [Fact]
    public void AddBorder_ConstantBorder()
    {
        Numa nas = MakeNuma(1, 2, 3);
        Numa nad = nas.AddBorder(2, 1, 9);
        Assert.Equal(new float[] { 9, 9, 1, 2, 3, 9 }, Values(nad));
        nad.GetParameters(out float startx, out float delx);
        Assert.Equal(-2.0f, startx);
        Assert.Equal(1.0f, delx);

        Numa copy = nas.AddBorder(-1, 0, 9);
        Assert.NotSame(nas, copy);
        Assert.Equal(new float[] { 1, 2, 3 }, Values(copy));
    }

    [Fact]
    public void AddBorder_UsesStartxAndDelx()
    {
        Numa nas = MakeNuma(1, 2);
        nas.SetParameters(10, 2);
        Numa nad = nas.AddBorder(3, 0, 0);
        nad.GetParameters(out float startx, out float delx);
        Assert.Equal(4.0f, startx);   /* 10 - 2 * 3 */
        Assert.Equal(2.0f, delx);
    }

    [Fact]
    public void AddSpecifiedBorder_ContinuedAndMirrored()
    {
        Numa nas = MakeNuma(1, 2, 3);
        Assert.Equal(new float[] { 1, 1, 1, 2, 3, 3, 3 },
            Values(nas.AddSpecifiedBorder(2, 2, L.L_CONTINUED_BORDER)));
        Assert.Equal(new float[] { 2, 1, 1, 2, 3, 3, 2 },
            Values(nas.AddSpecifiedBorder(2, 2, L.L_MIRRORED_BORDER)));
        Assert.Equal(new float[] { 3, 2, 1, 1, 2, 3, 3, 2, 1 },
            Values(nas.AddSpecifiedBorder(3, 3, L.L_MIRRORED_BORDER)));
        Assert.Equal(new float[] { 1, 2, 3 }, Values(nas.AddSpecifiedBorder(0, 0, L.L_MIRRORED_BORDER)));
    }

    [Fact]
    public void AddSpecifiedBorder_InvalidInput()
    {
        Numa nas = MakeNuma(1, 2, 3);
        Assert.Throws<ArgumentException>(() => nas.AddSpecifiedBorder(1, 1, L.L_SLOPE_BORDER));
        Assert.Throws<ArgumentException>(() => nas.AddSpecifiedBorder(4, 1, L.L_MIRRORED_BORDER));
        Assert.Throws<ArgumentException>(() => nas.AddSpecifiedBorder(1, 4, L.L_MIRRORED_BORDER));
        /* a continued border can be of any size */
        Assert.Equal(8, nas.AddSpecifiedBorder(4, 1, L.L_CONTINUED_BORDER).Count);
    }

    [Fact]
    public void RemoveBorder_RemovesAndShiftsStartx()
    {
        Numa nas = MakeNuma(1, 2, 3, 4, 5);
        Numa nad = nas.RemoveBorder(1, 2);
        Assert.Equal(new float[] { 2, 3 }, Values(nad));
        nad.GetParameters(out float startx, out float delx);
        Assert.Equal(1.0f, startx);
        Assert.Equal(1.0f, delx);

        Assert.Equal(0, nas.RemoveBorder(3, 2).Count);
        Assert.Equal(new float[] { 1, 2, 3, 4, 5 }, Values(nas.RemoveBorder(-1, 0)));
        Assert.Throws<ArgumentException>(() => nas.RemoveBorder(3, 3));
    }

    [Fact]
    public void CountNonzeroRuns_CountsRuns()
    {
        Assert.Equal(3, MakeNuma(0, 1, 2, 0, 0, 3, 0, 4, 4).CountNonzeroRuns());
        Assert.Equal(0, MakeNuma(0, 0, 0).CountNonzeroRuns());
        Assert.Equal(1, MakeNuma(5, 6, 7).CountNonzeroRuns());
        Assert.Equal(1, MakeNuma(0, 0.4f, 0, 3).CountNonzeroRuns());   /* 0.4 rounds to 0 */
        Assert.Equal(0, MakeNuma(-2, -1).CountNonzeroRuns());          /* only val > 0 starts a run */
        Assert.Throws<ArgumentException>(() => Numa.Create(0).CountNonzeroRuns());
    }

    [Fact]
    public void GetNonzeroRange_FindsInterval()
    {
        Assert.True(MakeNuma(0, 0, 1, 2, 0, 3, 0).GetNonzeroRange(0.0f, out int first, out int last));
        Assert.Equal(2, first);
        Assert.Equal(5, last);

        Assert.True(MakeNuma(0, 1, 2, 1, 0).GetNonzeroRange(1.0f, out first, out last));
        Assert.Equal(2, first);
        Assert.Equal(2, last);

        Assert.True(MakeNuma(4).GetNonzeroRange(0.0f, out first, out last));
        Assert.Equal(0, first);
        Assert.Equal(0, last);
    }

    [Fact]
    public void GetNonzeroRange_NothingFound()
    {
        Assert.False(MakeNuma(0, 0, 0).GetNonzeroRange(0.0f, out int first, out int last));
        Assert.Equal(2, first);
        Assert.Equal(0, last);
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetNonzeroRange(0.0f, out _, out _));
    }

    [Fact]
    public void GetCountRelativeToZero_Counts()
    {
        Numa na = MakeNuma(-1, 0, 2, 0, -3, 5, 7);
        Assert.Equal(2, na.GetCountRelativeToZero(L.L_LESS_THAN_ZERO));
        Assert.Equal(2, na.GetCountRelativeToZero(L.L_EQUAL_TO_ZERO));
        Assert.Equal(3, na.GetCountRelativeToZero(L.L_GREATER_THAN_ZERO));
        Assert.Equal(0, na.GetCountRelativeToZero(99));
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetCountRelativeToZero(L.L_EQUAL_TO_ZERO));
    }

    [Fact]
    public void ClipToInterval_ClipsAndShiftsStartx()
    {
        Numa nas = MakeNuma(10, 20, 30, 40, 50);
        nas.SetParameters(0, 2);
        Numa nad = nas.ClipToInterval(1, 3);
        Assert.Equal(new float[] { 20, 30, 40 }, Values(nad));
        nad.GetParameters(out float startx, out float delx);
        Assert.Equal(2.0f, startx);
        Assert.Equal(2.0f, delx);

        Assert.Equal(new float[] { 40, 50 }, Values(nas.ClipToInterval(3, 99)));
        Assert.Equal(new float[] { 10 }, Values(nas.ClipToInterval(0, 0)));
        Assert.Throws<ArgumentException>(() => nas.ClipToInterval(3, 1));
        Assert.Throws<ArgumentException>(() => nas.ClipToInterval(-1, 2));
        Assert.Throws<ArgumentException>(() => nas.ClipToInterval(5, 6));
        Assert.Throws<ArgumentException>(() => Numa.Create(0).ClipToInterval(0, 1));
    }

    [Fact]
    public void MakeThresholdIndicator_AllTypes()
    {
        Numa nas = MakeNuma(1, 2, 3);
        Assert.Equal(new float[] { 1, 0, 0 }, Values(nas.MakeThresholdIndicator(2, L.L_SELECT_IF_LT)));
        Assert.Equal(new float[] { 0, 0, 1 }, Values(nas.MakeThresholdIndicator(2, L.L_SELECT_IF_GT)));
        Assert.Equal(new float[] { 1, 1, 0 }, Values(nas.MakeThresholdIndicator(2, L.L_SELECT_IF_LTE)));
        Assert.Equal(new float[] { 0, 1, 1 }, Values(nas.MakeThresholdIndicator(2, L.L_SELECT_IF_GTE)));
        Assert.Throws<ArgumentException>(() => nas.MakeThresholdIndicator(2, 0));
        Assert.Throws<ArgumentException>(() => Numa.Create(0).MakeThresholdIndicator(2, L.L_SELECT_IF_LT));
    }

    [Fact]
    public void UniformSampling_BinsOfOneAndAHalf()
    {
        /* bin size 1.5 is exact in float.  The first sample of a bin that starts on
         * a sample boundary is dropped (see UPSTREAM_NOTES.md). */
        Numa nas = MakeNuma(1, 2, 3, 4, 5, 6);
        Numa nad = nas.UniformSampling(4);
        Assert.Equal(new float[] { 1, 4, 2.5f, 2.5f }, Values(nad));
        nad.GetParameters(out float startx, out float delx);
        Assert.Equal(0.0f, startx);
        Assert.Equal(1.5f, delx);
    }

    [Fact]
    public void UniformSampling_ThreeBins()
    {
        Numa nad = MakeNuma(1, 2, 3, 4).UniformSampling(3);
        Assert.Equal(3, nad.Count);
        Assert.Equal(0.6666667f, nad.GetFValue(0), 5);
        Assert.Equal(3.3333335f, nad.GetFValue(1), 5);
        Assert.Equal(1.0f, nad.GetFValue(2), 5);
        nad.GetParameters(out _, out float delx);
        Assert.Equal(4.0f / 3.0f, delx);
    }

    [Fact]
    public void UniformSampling_InvalidInput()
    {
        Assert.Throws<ArgumentException>(() => MakeNuma(1, 2).UniformSampling(0));
        Assert.Throws<ArgumentException>(() => Numa.Create(0).UniformSampling(2));
    }

    [Fact]
    public void Reverse_NewAndInPlace()
    {
        Numa nas = MakeNuma(1, 2, 3);
        nas.SetParameters(10, 2);
        Numa nad = Numa.Reverse(null, nas);
        Assert.Equal(new float[] { 3, 2, 1 }, Values(nad));
        nad.GetParameters(out float startx, out float delx);
        Assert.Equal(14.0f, startx);   /* 10 + 2 * 2 */
        Assert.Equal(-2.0f, delx);
        Assert.Equal(new float[] { 1, 2, 3 }, Values(nas));

        Numa nac = MakeNuma(1, 2, 3, 4);
        Assert.Same(nac, Numa.Reverse(nac, nac));
        Assert.Equal(new float[] { 4, 3, 2, 1 }, Values(nac));
        nac.GetParameters(out startx, out delx);
        Assert.Equal(3.0f, startx);    /* 0 + 3 * 1 */
        Assert.Equal(-1.0f, delx);

        Assert.Throws<ArgumentException>(() => Numa.Reverse(MakeNuma(1, 2, 3), nas));
        Assert.Equal(0, Numa.Reverse(null, Numa.Create(0)).Count);
    }

    /*------------------------- signal feature extraction ------------------------*/

    [Fact]
    public void LowPassIntervals_FindsRuns()
    {
        /* max 5, threshold 2.5: runs below at indices 1..2 and 5 */
        Numa nas = MakeNuma(5, 1, 1, 5, 5, 1, 5);
        Assert.Equal(new float[] { 5, 1, 3, 5, 6 }, Values(nas.LowPassIntervals(0.5f, 0.0f)));

        /* the abscissa values follow startx and delx */
        nas.SetParameters(10, 2);
        Assert.Equal(new float[] { 5, 12, 16, 20, 22 }, Values(nas.LowPassIntervals(0.5f, 0.0f)));
    }

    [Fact]
    public void LowPassIntervals_LastRunEndsAtLastIndex()
    {
        Assert.Equal(new float[] { 5, 2, 3 }, Values(MakeNuma(5, 5, 1, 1).LowPassIntervals(0.5f, 0.0f)));
    }

    [Fact]
    public void LowPassIntervals_UsesGivenMax()
    {
        /* maxn = 10 gives threshold 5; the value 8 ends the run, the final run is one point */
        Assert.Equal(new float[] { 10, 0, 1, 3, 3 }, Values(MakeNuma(2, 8, 8, 2).LowPassIntervals(0.5f, 10.0f)));
        /* a value equal to the threshold neither starts nor ends a run */
        Assert.Equal(new float[] { 10, 0, 3 }, Values(MakeNuma(2, 5, 5, 2).LowPassIntervals(0.5f, 10.0f)));
    }

    [Fact]
    public void LowPassIntervals_InvalidInput()
    {
        Numa nas = MakeNuma(1, 2, 3);
        Assert.Throws<ArgumentException>(() => nas.LowPassIntervals(-0.1f, 0.0f));
        Assert.Throws<ArgumentException>(() => nas.LowPassIntervals(1.1f, 0.0f));
        Assert.Throws<ArgumentException>(() => Numa.Create(0).LowPassIntervals(0.5f, 0.0f));
        Assert.Equal(new float[] { 3 }, Values(nas.LowPassIntervals(0.0f, 0.0f)));   /* nothing below 0 */
    }

    [Fact]
    public void ThresholdEdges_FullJumps()
    {
        /* max 10, thresholds 2 and 8; jump up between 1 and 2, jump down between 3 and 4 */
        Numa nas = MakeNuma(0, 0, 10, 10, 0);
        Assert.Equal(new float[] { 10, 1, 2, 1, 3, 4, -1 }, Values(nas.ThresholdEdges(0.2f, 0.8f, 0.0f)));
    }

    [Fact]
    public void ThresholdEdges_EdgeThroughTheBand()
    {
        Numa nas = MakeNuma(0, 5, 10);
        Assert.Equal(new float[] { 10, 0, 2, 1 }, Values(nas.ThresholdEdges(0.2f, 0.8f, 0.0f)));

        nas.SetParameters(100, 0.5f);
        Assert.Equal(new float[] { 10, 100, 101, 1 }, Values(nas.ThresholdEdges(0.2f, 0.8f, 0.0f)));

        /* falling edge through the band */
        Assert.Equal(new float[] { 10, 0, 2, -1 }, Values(MakeNuma(10, 5, 0).ThresholdEdges(0.2f, 0.8f, 0.0f)));
    }

    [Fact]
    public void ThresholdEdges_FailedEntryIntoBandIsNotAnEdge()
    {
        /* enters the band from below and returns below (x0 moves to 2); the jump up
         * from 2 to 3 is the only edge */
        Assert.Equal(new float[] { 10, 2, 3, 1 }, Values(MakeNuma(0, 5, 0, 10).ThresholdEdges(0.2f, 0.8f, 0.0f)));
        /* the same from above */
        Assert.Equal(new float[] { 10, 2, 3, -1 }, Values(MakeNuma(10, 5, 10, 0).ThresholdEdges(0.2f, 0.8f, 0.0f)));
    }

    [Fact]
    public void ThresholdEdges_MaxnHasNoEffect()
    {
        /* upstream determines the max again, so maxn = 20 is ignored */
        Numa nad = MakeNuma(0, 10).ThresholdEdges(0.2f, 0.8f, 20.0f);
        Assert.Equal(new float[] { 10, 0, 1, 1 }, Values(nad));
    }

    [Fact]
    public void ThresholdEdges_AllValuesInBandGivesOnlyMax()
    {
        Assert.Equal(new float[] { 5 }, Values(MakeNuma(5, 5, 5).ThresholdEdges(0.5f, 1.0f, 0.0f)));
    }

    [Fact]
    public void ThresholdEdges_InvalidInput()
    {
        Numa nas = MakeNuma(1, 2, 3);
        Assert.Throws<ArgumentException>(() => nas.ThresholdEdges(0.8f, 0.2f, 0.0f));
        Assert.Throws<ArgumentException>(() => nas.ThresholdEdges(-0.1f, 0.2f, 0.0f));
        Assert.Throws<ArgumentException>(() => nas.ThresholdEdges(0.1f, 1.2f, 0.0f));
        Assert.Throws<ArgumentException>(() => Numa.Create(0).ThresholdEdges(0.1f, 0.2f, 0.0f));
    }

    [Fact]
    public void GetSpanValues_ReadsPairs()
    {
        Numa na = MakeNuma(5, 12, 16, 20, 22);
        na.GetSpanValues(0, out int start, out int end);
        Assert.Equal((12, 16), (start, end));
        na.GetSpanValues(1, out start, out end);
        Assert.Equal((20, 22), (start, end));
        Assert.Throws<ArgumentException>(() => na.GetSpanValues(2, out _, out _));
        Assert.Throws<ArgumentException>(() => MakeNuma(5, 12, 16, 20).GetSpanValues(0, out _, out _));
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetSpanValues(0, out _, out _));
    }

    [Fact]
    public void GetEdgeValues_ReadsTriplets()
    {
        Numa na = MakeNuma(10, 1, 2, 1, 3, 4, -1);
        na.GetEdgeValues(0, out int start, out int end, out int sign);
        Assert.Equal((1, 2, 1), (start, end, sign));
        na.GetEdgeValues(1, out start, out end, out sign);
        Assert.Equal((3, 4, -1), (start, end, sign));
        Assert.Throws<ArgumentException>(() => na.GetEdgeValues(2, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => na.GetEdgeValues(-1, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => MakeNuma(10, 1, 2, 1, 3).GetEdgeValues(0, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetEdgeValues(0, out _, out _, out _));
    }

    /*------------------------------- interpolation ------------------------------*/

    [Fact]
    public void InterpolateEqxVal_Linear()
    {
        Numa nay = MakeNuma(0, 10, 20, 40);
        Assert.Equal(15.0f, Numa.InterpolateEqxVal(0, 1, nay, L.L_LINEAR_INTERP, 1.5f));
        Assert.Equal(20.0f, Numa.InterpolateEqxVal(0, 1, nay, L.L_LINEAR_INTERP, 2.0f));
        Assert.Equal(0.0f, Numa.InterpolateEqxVal(0, 1, nay, L.L_LINEAR_INTERP, 0.0f));
        Assert.Equal(40.0f, Numa.InterpolateEqxVal(0, 1, nay, L.L_LINEAR_INTERP, 3.0f));
        Assert.Equal(30.0f, Numa.InterpolateEqxVal(0, 1, nay, L.L_LINEAR_INTERP, 2.5f));
        /* startx = 10, deltax = 2: x = 13 is at index 1.5 */
        Assert.Equal(15.0f, Numa.InterpolateEqxVal(10, 2, nay, L.L_LINEAR_INTERP, 13.0f));
    }

    [Fact]
    public void InterpolateEqxVal_Quadratic()
    {
        Numa nay = MakeNuma(0, 10, 20, 40);
        Assert.Equal(15.0f, Numa.InterpolateEqxVal(0, 1, nay, L.L_QUADRATIC_INTERP, 1.5f));
        Assert.Equal(5.0f, Numa.InterpolateEqxVal(0, 1, nay, L.L_QUADRATIC_INTERP, 0.5f));
        /* through (1,10), (2,20), (3,40): 10 + 10 (x-1) + 5 (x-1)(x-2) */
        Assert.Equal(28.75f, Numa.InterpolateEqxVal(0, 1, nay, L.L_QUADRATIC_INTERP, 2.5f));
        /* exact points need no interpolation */
        Assert.Equal(40.0f, Numa.InterpolateEqxVal(0, 1, nay, L.L_QUADRATIC_INTERP, 3.0f));
        /* two points: quadratic falls back to linear */
        Assert.Equal(5.0f, Numa.InterpolateEqxVal(0, 1, MakeNuma(0, 10), L.L_QUADRATIC_INTERP, 0.5f));
    }

    [Fact]
    public void InterpolateEqxVal_InvalidInput()
    {
        Numa nay = MakeNuma(0, 10, 20, 40);
        Assert.Throws<ArgumentException>(() => Numa.InterpolateEqxVal(0, 0, nay, L.L_LINEAR_INTERP, 1.0f));
        Assert.Throws<ArgumentException>(() => Numa.InterpolateEqxVal(0, -1, nay, L.L_LINEAR_INTERP, 1.0f));
        Assert.Throws<ArgumentException>(() => Numa.InterpolateEqxVal(0, 1, nay, 0, 1.0f));
        Assert.Throws<ArgumentException>(() => Numa.InterpolateEqxVal(0, 1, MakeNuma(1), L.L_LINEAR_INTERP, 0.0f));
        Assert.Throws<ArgumentException>(() => Numa.InterpolateEqxVal(0, 1, nay, L.L_LINEAR_INTERP, -0.5f));
        Assert.Throws<ArgumentException>(() => Numa.InterpolateEqxVal(0, 1, nay, L.L_LINEAR_INTERP, 3.5f));
    }

    [Fact]
    public void InterpolateArbxVal_ExactPointsAndQuadratic()
    {
        Numa nax = MakeNuma(0, 1, 3, 6);
        Numa nay = MakeNuma(0, 10, 30, 60);
        Assert.Equal(0.0f, nax.InterpolateArbxVal(nay, L.L_LINEAR_INTERP, 0.0f));
        Assert.Equal(30.0f, nax.InterpolateArbxVal(nay, L.L_LINEAR_INTERP, 3.0f));
        Assert.Equal(60.0f, nax.InterpolateArbxVal(nay, L.L_LINEAR_INTERP, 6.0f));
        /* y = 10 x is reproduced exactly by the quadratic through (0,0), (1,10), (3,30) */
        Assert.Equal(20.0f, nax.InterpolateArbxVal(nay, L.L_QUADRATIC_INTERP, 2.0f));
    }

    [Fact]
    public void InterpolateArbxVal_LinearFollowsUpstreamIndexing()
    {
        /* x = 2 lies in [1, 3] (im = 1, fract = 0.5), but the C code interpolates between
         * fay[2] and fay[3]: 30 + 0.5 * (60 - 30) = 45 instead of 20 (see UPSTREAM_NOTES.md) */
        Numa nax = MakeNuma(0, 1, 3, 6);
        Numa nay = MakeNuma(0, 10, 30, 60);
        Assert.Equal(45.0f, nax.InterpolateArbxVal(nay, L.L_LINEAR_INTERP, 2.0f));
    }

    [Fact]
    public void InterpolateArbxVal_InvalidInput()
    {
        Numa nax = MakeNuma(0, 1, 3, 6);
        Numa nay = MakeNuma(0, 10, 30, 60);
        Assert.Throws<ArgumentException>(() => nax.InterpolateArbxVal(nay, 0, 2.0f));
        Assert.Throws<ArgumentException>(() => nax.InterpolateArbxVal(MakeNuma(1, 2, 3), L.L_LINEAR_INTERP, 2.0f));
        Assert.Throws<ArgumentException>(() => MakeNuma(1).InterpolateArbxVal(MakeNuma(1), L.L_LINEAR_INTERP, 1.0f));
        Assert.Throws<ArgumentException>(() => nax.InterpolateArbxVal(nay, L.L_LINEAR_INTERP, -0.5f));
        Assert.Throws<ArgumentException>(() => nax.InterpolateArbxVal(nay, L.L_LINEAR_INTERP, 6.5f));
    }

    [Fact]
    public void InterpolateEqxInterval_LinearOverInterval()
    {
        Numa nasy = MakeNuma(0, 10, 20, 30);
        Numa.InterpolateEqxInterval(0, 1, nasy, L.L_LINEAR_INTERP, 0.5f, 2.5f, 5, out Numa nax, out Numa nay);
        Assert.Equal(new float[] { 0.5f, 1, 1.5f, 2, 2.5f }, Values(nax));
        Assert.Equal(new float[] { 5, 10, 15, 20, 25 }, Values(nay));
        nay.GetParameters(out float startx, out float delx);
        Assert.Equal(0.5f, startx);
        Assert.Equal(0.5f, delx);
    }

    [Fact]
    public void InterpolateEqxInterval_ErrorInOneValueGivesZero()
    {
        /* x0 + 3 * delx = 0.50000006 is beyond the last point 0.5 because of roundoff;
         * the C code ignores the error of numaInterpolateEqxVal() and stores 0.0 */
        Numa nasy = MakeNuma(0, 1);
        Numa.InterpolateEqxInterval(0, 0.5f, nasy, L.L_LINEAR_INTERP, 0.1f, 0.5f, 4, out _, out Numa nay);
        Assert.Equal(4, nay.Count);
        Assert.Equal(0.2f, nay.GetFValue(0), 5);
        Assert.Equal(0.0f, nay.GetFValue(3));
    }

    [Fact]
    public void InterpolateEqxInterval_InvalidInput()
    {
        Numa nasy = MakeNuma(0, 10, 20, 30);
        Assert.Throws<ArgumentException>(() => Numa.InterpolateEqxInterval(
            0, 1, MakeNuma(1), L.L_LINEAR_INTERP, 0.0f, 1.0f, 5, out _, out _));
        Assert.Throws<ArgumentException>(() => Numa.InterpolateEqxInterval(
            0, 0, nasy, L.L_LINEAR_INTERP, 0.0f, 1.0f, 5, out _, out _));
        Assert.Throws<ArgumentException>(() => Numa.InterpolateEqxInterval(
            0, 1, nasy, 0, 0.0f, 1.0f, 5, out _, out _));
        Assert.Throws<ArgumentException>(() => Numa.InterpolateEqxInterval(
            0, 1, nasy, L.L_LINEAR_INTERP, -1.0f, 1.0f, 5, out _, out _));   /* x0 < startx */
        Assert.Throws<ArgumentException>(() => Numa.InterpolateEqxInterval(
            0, 1, nasy, L.L_LINEAR_INTERP, 0.0f, 3.5f, 5, out _, out _));    /* x1 > maxx */
        Assert.Throws<ArgumentException>(() => Numa.InterpolateEqxInterval(
            0, 1, nasy, L.L_LINEAR_INTERP, 1.0f, 1.0f, 5, out _, out _));    /* x1 <= x0 */
        Assert.Throws<ArgumentException>(() => Numa.InterpolateEqxInterval(
            0, 1, nasy, L.L_LINEAR_INTERP, 0.0f, 1.0f, 2, out _, out _));    /* npts < 3 */
    }

    /*------------------------ functions requiring interpolation -----------------*/

    [Fact]
    public void FitMax_InteriorMaxByParabola()
    {
        /* parabola through (0,1), (1,4), (2,3): vertex at 1.25, value 4.125 */
        MakeNuma(1, 4, 3).FitMax(out float maxval, null, out float maxloc);
        Assert.Equal(4.125f, maxval);
        Assert.Equal(1.25f, maxloc);

        /* the same with abscissa values (0,2,4): vertex at 2.5 */
        MakeNuma(1, 4, 3).FitMax(out maxval, MakeNuma(0, 2, 4), out maxloc);
        Assert.Equal(4.125f, maxval);
        Assert.Equal(2.5f, maxloc);
    }

    [Fact]
    public void FitMax_MaxAtEndPoint()
    {
        MakeNuma(5, 3, 1).FitMax(out float maxval, null, out float maxloc);
        Assert.Equal((5.0f, 0.0f), (maxval, maxloc));
        MakeNuma(1, 3, 5).FitMax(out maxval, null, out maxloc);
        Assert.Equal((5.0f, 2.0f), (maxval, maxloc));
        MakeNuma(5, 3, 1).FitMax(out maxval, MakeNuma(7, 8, 9), out maxloc);
        Assert.Equal((5.0f, 7.0f), (maxval, maxloc));
        MakeNuma(1, 3, 5).FitMax(out maxval, MakeNuma(7, 8, 9), out maxloc);
        Assert.Equal((5.0f, 9.0f), (maxval, maxloc));
    }

    [Fact]
    public void FitMax_CannotInterpolateWithEqualAbscissas()
    {
        MakeNuma(1, 4, 3).FitMax(out float maxval, MakeNuma(2, 2, 5), out float maxloc);
        Assert.Equal((4.0f, 2.0f), (maxval, maxloc));
    }

    [Fact]
    public void FitMax_InvalidInput()
    {
        Assert.Throws<ArgumentException>(() => Numa.Create(0).FitMax(out _, null, out _));
        Assert.Throws<ArgumentException>(() => MakeNuma(1, 4, 3).FitMax(out _, MakeNuma(1, 2), out _));
    }

    /* y = x * x at x = 0 ... 4 */
    private static Numa SquareX() => MakeNuma(0, 1, 2, 3, 4);
    private static Numa SquareY() => MakeNuma(0, 1, 4, 9, 16);

    [Fact]
    public void InterpolateArbxInterval_LinearBetweenPoints()
    {
        SquareX().InterpolateArbxInterval(SquareY(), L.L_LINEAR_INTERP, 0.5f, 1.5f, 3,
                                          out Numa nadx, out Numa nady);
        Assert.Equal(new float[] { 0.5f, 1, 1.5f }, Values(nadx));
        Assert.Equal(new float[] { 0.5f, 1, 2.5f }, Values(nady));
    }

    [Fact]
    public void InterpolateArbxInterval_QuadraticReproducesParabola()
    {
        SquareX().InterpolateArbxInterval(SquareY(), L.L_QUADRATIC_INTERP, 2.5f, 3.5f, 3,
                                          out Numa nadx, out Numa nady);
        Assert.Equal(new float[] { 2.5f, 3, 3.5f }, Values(nadx));
        Assert.Equal(new float[] { 6.25f, 9, 12.25f }, Values(nady));
    }

    [Fact]
    public void InterpolateArbxInterval_WholeRangeHitsThePoints()
    {
        SquareX().InterpolateArbxInterval(SquareY(), L.L_LINEAR_INTERP, 0, 4, 5, out _, out Numa nady);
        Assert.Equal(Values(SquareY()), Values(nady));
    }

    [Fact]
    public void InterpolateArbxInterval_SortsUnsortedAbscissas()
    {
        Numa nax = MakeNuma(2, 0, 1);
        Numa nay = MakeNuma(20, 0, 10);
        nax.InterpolateArbxInterval(nay, L.L_LINEAR_INTERP, 0.5f, 1.5f, 3, out Numa nadx, out Numa nady);
        Assert.Equal(new float[] { 0.5f, 1, 1.5f }, Values(nadx));
        Assert.Equal(new float[] { 5, 10, 15 }, Values(nady));
        Assert.Equal(new float[] { 2, 0, 1 }, Values(nax));   /* the input is not sorted in place */
        Assert.Equal(new float[] { 20, 0, 10 }, Values(nay));
    }

    [Fact]
    public void InterpolateArbxInterval_ZeroPointsGivesEmptyNumas()
    {
        SquareX().InterpolateArbxInterval(SquareY(), L.L_LINEAR_INTERP, 0, 4, 0, out Numa nadx, out Numa nady);
        Assert.Equal(0, nadx.Count);
        Assert.Equal(0, nady.Count);
        Assert.Throws<LeptonicaException>(() => SquareX().InterpolateArbxInterval(
            SquareY(), L.L_LINEAR_INTERP, 0, 4, -1, out _, out _));
    }

    [Fact]
    public void InterpolateArbxInterval_InvalidInput()
    {
        Numa nax = SquareX();
        Numa nay = SquareY();
        Assert.Throws<ArgumentException>(() => nax.InterpolateArbxInterval(nay, 0, 0, 4, 5, out _, out _));
        Assert.Throws<ArgumentException>(() => nax.InterpolateArbxInterval(nay, L.L_LINEAR_INTERP, 3, 2, 5, out _, out _));
        Assert.Throws<ArgumentException>(() => nax.InterpolateArbxInterval(
            MakeNuma(1, 2), L.L_LINEAR_INTERP, 0, 4, 5, out _, out _));
        Assert.Throws<ArgumentException>(() => MakeNuma(1).InterpolateArbxInterval(
            MakeNuma(1), L.L_LINEAR_INTERP, 1, 1, 5, out _, out _));
        Assert.Throws<ArgumentException>(() => nax.InterpolateArbxInterval(nay, L.L_LINEAR_INTERP, -1, 4, 5, out _, out _));
        Assert.Throws<ArgumentException>(() => nax.InterpolateArbxInterval(nay, L.L_LINEAR_INTERP, 0, 5, 5, out _, out _));
    }

    [Fact]
    public void DifferentiateInterval_DerivativeOfSquare()
    {
        /* invdel = 0.5 * (npts - 1) / (x1 - x0) = 0.5; end points use half of it */
        SquareX().DifferentiateInterval(SquareY(), 0, 4, 5, out Numa nadx, out Numa nady);
        Assert.Equal(new float[] { 0, 1, 2, 3, 4 }, Values(nadx));
        Assert.Equal(new float[] { 0.25f, 2, 4, 6, 1.75f }, Values(nady));
    }

    [Fact]
    public void DifferentiateInterval_InvalidInput()
    {
        Numa nax = SquareX();
        Numa nay = SquareY();
        Assert.Throws<ArgumentException>(() => nax.DifferentiateInterval(nay, 3, 2, 5, out _, out _));
        Assert.Throws<ArgumentException>(() => nax.DifferentiateInterval(MakeNuma(1, 2), 0, 4, 5, out _, out _));
        Assert.Throws<ArgumentException>(() => MakeNuma(1).DifferentiateInterval(MakeNuma(1), 1, 1, 5, out _, out _));
        Assert.Throws<ArgumentException>(() => nax.DifferentiateInterval(nay, -1, 4, 5, out _, out _));
        Assert.Throws<ArgumentException>(() => nax.DifferentiateInterval(nay, 0, 4, 1, out _, out _));
    }

    [Fact]
    public void IntegrateInterval_Trapezoid()
    {
        /* exact points: 0.5 * (0 + 16) + (1 + 4 + 9) = 22 */
        Assert.Equal(22.0f, SquareX().IntegrateInterval(SquareY(), 0, 4, 5));
        /* interpolated values 0.5, 1, 2.5: 0.5 * (0.5 * (0.5 + 2.5) + 1) = 1.25 */
        Assert.Equal(1.25f, SquareX().IntegrateInterval(SquareY(), 0.5f, 1.5f, 3));
    }

    [Fact]
    public void IntegrateInterval_InvalidInput()
    {
        Numa nax = SquareX();
        Numa nay = SquareY();
        Assert.Throws<ArgumentException>(() => nax.IntegrateInterval(nay, 3, 2, 5));
        Assert.Throws<ArgumentException>(() => nax.IntegrateInterval(nay, 0, 4, 1));
        Assert.Throws<ArgumentException>(() => nax.IntegrateInterval(MakeNuma(1, 2), 0, 4, 5));
        Assert.Throws<ArgumentException>(() => MakeNuma(1).IntegrateInterval(MakeNuma(1), 1, 1, 5));
        Assert.Throws<ArgumentException>(() => nax.IntegrateInterval(nay, -1, 4, 5));
    }

    /*---------------------------------- sorting ---------------------------------*/

    [Fact]
    public void SortGeneral_ReproducesTheDocumentedExample()
    {
        /* the table in the notes of numaSortGeneral(), decreasing order */
        foreach (int sorttype in new[] { L.L_SHELL_SORT, L.L_BIN_SORT }) {
            MakeNuma(3, 4, 9, 6, 1).SortGeneral(out Numa nasort, out Numa naindex, out Numa nainvert,
                                                L.L_SORT_DECREASING, sorttype);
            Assert.Equal(new float[] { 9, 6, 4, 3, 1 }, Values(nasort));
            Assert.Equal(new float[] { 2, 3, 1, 0, 4 }, Values(naindex));
            Assert.Equal(new float[] { 3, 2, 0, 1, 4 }, Values(nainvert));
        }
    }

    [Fact]
    public void SortGeneral_Increasing()
    {
        foreach (int sorttype in new[] { L.L_SHELL_SORT, L.L_BIN_SORT }) {
            MakeNuma(3, 4, 9, 6, 1).SortGeneral(out Numa nasort, out Numa naindex, out Numa nainvert,
                                                L.L_SORT_INCREASING, sorttype);
            Assert.Equal(new float[] { 1, 3, 4, 6, 9 }, Values(nasort));
            Assert.Equal(new float[] { 4, 0, 1, 3, 2 }, Values(naindex));
            Assert.Equal(new float[] { 1, 2, 4, 3, 0 }, Values(nainvert));
        }
    }

    [Fact]
    public void SortGeneral_BinSortFallsBackToShellSort()
    {
        /* the max is over MaxInitPtraSize - 1 */
        MakeNuma(2e6f, 1).SortGeneral(out Numa nasort, out Numa naindex, out _,
                                      L.L_SORT_INCREASING, L.L_BIN_SORT);
        Assert.Equal(new float[] { 1, 2e6f }, Values(nasort));
        Assert.Equal(new float[] { 1, 0 }, Values(naindex));

        /* a negative max is compared as an unsigned number in C, so it also falls back */
        MakeNuma(-1, -5, -3).SortGeneral(out nasort, out naindex, out _,
                                         L.L_SORT_INCREASING, L.L_BIN_SORT);
        Assert.Equal(new float[] { -5, -3, -1 }, Values(nasort));
        Assert.Equal(new float[] { 1, 2, 0 }, Values(naindex));
    }

    [Fact]
    public void SortGeneral_EmptyAndInvalidInput()
    {
        foreach (int sorttype in new[] { L.L_SHELL_SORT, L.L_BIN_SORT }) {
            Numa.Create(0).SortGeneral(out Numa nasort, out Numa naindex, out Numa nainvert,
                                       L.L_SORT_INCREASING, sorttype);
            Assert.Equal(0, nasort.Count);
            Assert.Equal(0, naindex.Count);
            Assert.Equal(0, nainvert.Count);
        }
        Numa na = MakeNuma(1, 2);
        Assert.Throws<ArgumentException>(() => na.SortGeneral(out _, out _, out _, 0, L.L_SHELL_SORT));
        Assert.Throws<ArgumentException>(() => na.SortGeneral(out _, out _, out _, L.L_SORT_INCREASING, 0));
    }

    [Fact]
    public void ChooseSortType_Cases()
    {
        Assert.Equal(L.L_SHELL_SORT, Numa.Create(0).ChooseSortType());
        Assert.Equal(L.L_SHELL_SORT, MakeNuma(5, 3, 1).ChooseSortType());   /* n < 200 */

        float[] small = new float[200];   /* n = 200, max 9: n ln n = 1059.7 > 0.003 * 9 */
        for (int i = 0; i < small.Length; i++)
            small[i] = i % 10;
        Assert.Equal(L.L_BIN_SORT, MakeNuma(small).ChooseSortType());

        float[] neg = (float[])small.Clone();
        neg[7] = -1;
        Assert.Equal(L.L_SHELL_SORT, MakeNuma(neg).ChooseSortType());       /* negative value */

        float[] big = (float[])small.Clone();
        big[3] = 100000;                  /* 0.003 * 1e5 = 300 < 1059.7 */
        Assert.Equal(L.L_BIN_SORT, MakeNuma(big).ChooseSortType());
        big[3] = 500000;                  /* 1500 > 1059.7 */
        Assert.Equal(L.L_SHELL_SORT, MakeNuma(big).ChooseSortType());
        big[3] = 2000000;                 /* over MaxInitPtraSize - 1 */
        Assert.Equal(L.L_SHELL_SORT, MakeNuma(big).ChooseSortType());
    }

    [Fact]
    public void Sort_ShellSortBothOrders()
    {
        Numa na = MakeNuma(3, 1, 2);
        Assert.Equal(new float[] { 1, 2, 3 }, Values(Numa.Sort(null, na, L.L_SORT_INCREASING)));
        Assert.Equal(new float[] { 3, 2, 1 }, Values(Numa.Sort(null, na, L.L_SORT_DECREASING)));
        Assert.Equal(new float[] { 3, 1, 2 }, Values(na));   /* the input is unchanged */

        Assert.Same(na, Numa.Sort(na, na, L.L_SORT_INCREASING));
        Assert.Equal(new float[] { 1, 2, 3 }, Values(na));
    }

    [Fact]
    public void Sort_MatchesArraySort()
    {
        float[] values = new float[30];
        for (int i = 0; i < values.Length; i++)
            values[i] = (i * 7) % 11 - 3.5f;
        float[] expected = (float[])values.Clone();
        Array.Sort(expected);
        Assert.Equal(expected, Values(Numa.Sort(null, MakeNuma(values), L.L_SORT_INCREASING)));
        Array.Reverse(expected);
        Assert.Equal(expected, Values(Numa.Sort(null, MakeNuma(values), L.L_SORT_DECREASING)));
    }

    [Fact]
    public void Sort_InvalidInputAndEmpty()
    {
        Numa na = MakeNuma(2, 1);
        Assert.Throws<ArgumentException>(() => Numa.Sort(null, na, 0));
        Assert.Throws<ArgumentException>(() => Numa.Sort(MakeNuma(1, 2), na, L.L_SORT_INCREASING));
        Numa empty = Numa.Create(0);
        Assert.Same(empty, Numa.Sort(empty, empty, L.L_SORT_INCREASING));
        Assert.Equal(0, Numa.Sort(null, empty, L.L_SORT_DECREASING).Count);
    }

    [Fact]
    public void BinSort_SortsNonNegativeIntegers()
    {
        Numa na = MakeNuma(5, 3, 5, 0, 2);
        Assert.Equal(new float[] { 0, 2, 3, 5, 5 }, Values(na.BinSort(L.L_SORT_INCREASING)));
        Assert.Equal(new float[] { 5, 5, 3, 2, 0 }, Values(na.BinSort(L.L_SORT_DECREASING)));
        Assert.Equal(new float[] { 5, 3, 5, 0, 2 }, Values(na));
    }

    [Fact]
    public void BinSort_UsesRoundedValuesAsKeys()
    {
        /* 4.7 rounds to bin 5, one beyond the truncated max 4 */
        Assert.Equal(new float[] { 1.0f, 4.7f }, Values(MakeNuma(4.7f, 1.0f).BinSort(L.L_SORT_INCREASING)));
        Assert.Equal(new float[] { 4.7f, 1.0f }, Values(MakeNuma(1.0f, 4.7f).BinSort(L.L_SORT_DECREASING)));
        /* 1.4 -> 1, 1.6 -> 2 */
        Assert.Equal(new float[] { 1.4f, 1.6f }, Values(MakeNuma(1.6f, 1.4f).BinSort(L.L_SORT_INCREASING)));
    }

    [Fact]
    public void BinSort_InvalidInputAndEmpty()
    {
        Assert.Throws<ArgumentException>(() => MakeNuma(1, 2).BinSort(0));
        Assert.Throws<ArgumentException>(() => MakeNuma(1, -2).BinSort(L.L_SORT_INCREASING));
        Assert.Throws<ArgumentException>(() => MakeNuma(1, 2e6f).BinSort(L.L_SORT_INCREASING));
        Assert.Equal(0, Numa.Create(0).BinSort(L.L_SORT_INCREASING).Count);
    }

    [Fact]
    public void GetSortIndex_ShellSort()
    {
        Numa na = MakeNuma(3, 4, 9, 6, 1);
        Assert.Equal(new float[] { 2, 3, 1, 0, 4 }, Values(na.GetSortIndex(L.L_SORT_DECREASING)));
        Assert.Equal(new float[] { 4, 0, 1, 3, 2 }, Values(na.GetSortIndex(L.L_SORT_INCREASING)));
        /* equal values: hand-traced order of the shell sort */
        Assert.Equal(new float[] { 1, 3, 0, 2 }, Values(MakeNuma(2, 1, 2, 1).GetSortIndex(L.L_SORT_INCREASING)));
        Assert.Equal(new float[] { 0, 2, 1, 3 }, Values(MakeNuma(2, 1, 2, 1).GetSortIndex(L.L_SORT_DECREASING)));
        Assert.Equal(0, Numa.Create(0).GetSortIndex(L.L_SORT_INCREASING).Count);
        Assert.Throws<ArgumentException>(() => na.GetSortIndex(0));
    }

    [Fact]
    public void GetBinSortIndex_IsStable()
    {
        Numa na = MakeNuma(2, 1, 2, 1);
        Assert.Equal(new float[] { 1, 3, 0, 2 }, Values(na.GetBinSortIndex(L.L_SORT_INCREASING)));
        Assert.Equal(new float[] { 0, 2, 1, 3 }, Values(na.GetBinSortIndex(L.L_SORT_DECREASING)));
        Assert.Equal(new float[] { 2, 3, 1, 0, 4 }, Values(MakeNuma(3, 4, 9, 6, 1).GetBinSortIndex(L.L_SORT_DECREASING)));
        Assert.Equal(0, Numa.Create(0).GetBinSortIndex(L.L_SORT_INCREASING).Count);
        Assert.Throws<ArgumentException>(() => na.GetBinSortIndex(0));
        Assert.Throws<ArgumentException>(() => MakeNuma(-1, 2).GetBinSortIndex(L.L_SORT_INCREASING));
        Assert.Throws<ArgumentException>(() => MakeNuma(1000001, 2).GetBinSortIndex(L.L_SORT_INCREASING));
        /* the largest allowed value */
        Assert.Equal(new float[] { 1, 0 }, Values(MakeNuma(1000000, 2).GetBinSortIndex(L.L_SORT_INCREASING)));
    }

    [Fact]
    public void SortAutoSelect_UsesEitherSort()
    {
        Assert.Equal(new float[] { 1, 2, 3 }, Values(MakeNuma(3, 1, 2).SortAutoSelect(L.L_SORT_INCREASING)));

        float[] values = new float[200];   /* chooses the bin sort */
        for (int i = 0; i < values.Length; i++)
            values[i] = (i * 7) % 10;
        float[] expected = (float[])values.Clone();
        Array.Sort(expected);
        Numa na = MakeNuma(values);
        Assert.Equal(L.L_BIN_SORT, na.ChooseSortType());
        Assert.Equal(expected, Values(na.SortAutoSelect(L.L_SORT_INCREASING)));
        Array.Reverse(expected);
        Assert.Equal(expected, Values(na.SortAutoSelect(L.L_SORT_DECREASING)));

        Assert.Equal(0, Numa.Create(0).SortAutoSelect(L.L_SORT_INCREASING).Count);
        Assert.Throws<ArgumentException>(() => na.SortAutoSelect(0));
    }

    [Fact]
    public void SortIndexAutoSelect_UsesEitherSort()
    {
        Assert.Equal(new float[] { 2, 3, 1, 0, 4 },
            Values(MakeNuma(3, 4, 9, 6, 1).SortIndexAutoSelect(L.L_SORT_DECREASING)));

        float[] values = new float[200];
        for (int i = 0; i < values.Length; i++)
            values[i] = (i * 7) % 10;
        Numa na = MakeNuma(values);
        Numa naindex = na.SortIndexAutoSelect(L.L_SORT_INCREASING);
        Assert.Equal(200, naindex.Count);
        for (int i = 1; i < 200; i++) {
            float prev = values[naindex.GetIValue(i - 1)];
            float cur = values[naindex.GetIValue(i)];
            Assert.True(prev < cur || (prev == cur && naindex.GetIValue(i - 1) < naindex.GetIValue(i)));   /* stable */
        }

        Assert.Equal(0, Numa.Create(0).SortIndexAutoSelect(L.L_SORT_INCREASING).Count);
        Assert.Throws<ArgumentException>(() => na.SortIndexAutoSelect(0));
    }

    [Fact]
    public void SortByIndex_ReordersAndChecks()
    {
        Numa na = MakeNuma(10, 20, 30);
        Assert.Equal(new float[] { 30, 10, 20 }, Values(na.SortByIndex(MakeNuma(2, 0, 1))));
        Assert.Throws<ArgumentException>(() => na.SortByIndex(MakeNuma(0, 1)));
        Assert.Throws<ArgumentException>(() => na.SortByIndex(MakeNuma(0, 1, 3)));
        Assert.Equal(0, Numa.Create(0).SortByIndex(Numa.Create(0)).Count);
    }

    [Fact]
    public void IsSorted_BothOrders()
    {
        Assert.True(MakeNuma(1, 2, 2, 3).IsSorted(L.L_SORT_INCREASING));
        Assert.False(MakeNuma(1, 2, 2, 3).IsSorted(L.L_SORT_DECREASING));
        Assert.True(MakeNuma(3, 2, 2, 1).IsSorted(L.L_SORT_DECREASING));
        Assert.False(MakeNuma(3, 1, 4).IsSorted(L.L_SORT_INCREASING));
        Assert.False(MakeNuma(1, 3, 0).IsSorted(L.L_SORT_INCREASING));
        Assert.True(MakeNuma(5).IsSorted(L.L_SORT_INCREASING));
        Assert.True(MakeNuma(5).IsSorted(L.L_SORT_DECREASING));
        Assert.True(Numa.Create(0).IsSorted(L.L_SORT_INCREASING));
        Assert.True(Numa.Create(0).IsSorted(0));   /* the empty case is handled before the order is checked */
        Assert.Throws<ArgumentException>(() => MakeNuma(1, 2).IsSorted(0));
    }

    [Fact]
    public void IsSorted_ComparesWithTheFirstValueOnly()
    {
        /* upstream never updates prevval: every value is only compared with the first one
         * (see UPSTREAM_NOTES.md), so these unsorted arrays count as sorted */
        Assert.True(MakeNuma(1, 3, 2).IsSorted(L.L_SORT_INCREASING));
        Assert.True(MakeNuma(3, 1, 2).IsSorted(L.L_SORT_DECREASING));
    }

    [Fact]
    public void SortPair_SortsBothByTheFirst()
    {
        Numa nax = MakeNuma(3, 1, 2);
        Numa nay = MakeNuma(30, 10, 20);
        nax.SortPair(nay, L.L_SORT_INCREASING, out Numa nasx, out Numa nasy);
        Assert.Equal(new float[] { 1, 2, 3 }, Values(nasx));
        Assert.Equal(new float[] { 10, 20, 30 }, Values(nasy));

        nax = MakeNuma(1, 3, 2);
        nay = MakeNuma(10, 30, 20);
        nax.SortPair(nay, L.L_SORT_DECREASING, out nasx, out nasy);
        Assert.Equal(new float[] { 3, 2, 1 }, Values(nasx));
        Assert.Equal(new float[] { 30, 20, 10 }, Values(nasy));
    }

    [Fact]
    public void SortPair_SortedInputIsCopied()
    {
        Numa nax = MakeNuma(1, 2, 3);
        Numa nay = MakeNuma(7, 8, 9);
        nax.SortPair(nay, L.L_SORT_INCREASING, out Numa nasx, out Numa nasy);
        Assert.NotSame(nax, nasx);
        Assert.NotSame(nay, nasy);
        Assert.Equal(new float[] { 1, 2, 3 }, Values(nasx));
        Assert.Equal(new float[] { 7, 8, 9 }, Values(nasy));
        Assert.Throws<ArgumentException>(() => nax.SortPair(nay, 0, out _, out _));
    }

    [Fact]
    public void InvertMap_InvertsPermutation()
    {
        Assert.Equal(new float[] { 3, 2, 0, 1, 4 }, Values(MakeNuma(2, 3, 1, 0, 4).InvertMap()));
        Assert.Equal(new float[] { 0 }, Values(MakeNuma(0).InvertMap()));
        Assert.Equal(0, Numa.Create(0).InvertMap().Count);
    }

    [Fact]
    public void InvertMap_NotInvertibleThrows()
    {
        Assert.Throws<ArgumentException>(() => MakeNuma(0, 0, 1).InvertMap());   /* repeated value */
        Assert.Throws<ArgumentException>(() => MakeNuma(0, 5).InvertMap());      /* value >= n */
    }

    [Fact]
    public void AddSorted_KeepsTheOrder()
    {
        Numa inc = MakeNuma(1, 3, 5);
        inc.AddSorted(4);
        inc.AddSorted(0);
        inc.AddSorted(9);
        inc.AddSorted(3);
        Assert.Equal(new float[] { 0, 1, 3, 3, 4, 5, 9 }, Values(inc));

        Numa dec = MakeNuma(5, 3, 1);
        dec.AddSorted(4);
        dec.AddSorted(6);
        dec.AddSorted(0);
        Assert.Equal(new float[] { 6, 5, 4, 3, 1, 0 }, Values(dec));

        Numa one = MakeNuma(5);
        one.AddSorted(7);
        one.AddSorted(3);
        Assert.Equal(new float[] { 3, 5, 7 }, Values(one));

        Numa empty = Numa.Create(0);
        empty.AddSorted(2);
        Assert.Equal(new float[] { 2 }, Values(empty));
    }

    [Fact]
    public void FindSortedLoc_BinarySearch()
    {
        Numa na = MakeNuma(1, 2, 3, 4, 5, 6, 7, 8);
        Assert.Equal(4, na.FindSortedLoc(4.5f));
        Assert.Equal(0, na.FindSortedLoc(0.0f));
        Assert.Equal(8, na.FindSortedLoc(9.0f));
        Assert.Equal(1, MakeNuma(1, 2).FindSortedLoc(1.5f));
        Assert.Equal(0, Numa.Create(0).FindSortedLoc(3.0f));
        Assert.Equal(1, MakeNuma(5).FindSortedLoc(5.0f));    /* size 1: increasing order */
        Assert.Equal(0, MakeNuma(5).FindSortedLoc(4.0f));
        /* decreasing */
        Assert.Equal(3, MakeNuma(8, 7, 6, 5, 4).FindSortedLoc(5.5f));
    }

    /*---------------------------- random permutation ----------------------------*/

    [Fact]
    public void PseudorandomSequence_MatchesTheMsvcGenerator()
    {
        /* expected values from the MSVC rand() recurrence (state * 214013 + 2531011,
         * bits 16..30), e.g., 41, 18467, 6334, ... for seed 1 */
        Assert.Equal(new float[] { 3, 1, 4, 2, 0 }, Values(Numa.PseudorandomSequence(5, 1)));
        Assert.Equal(new float[] { 2, 3, 1, 4, 0 }, Values(Numa.PseudorandomSequence(5, 0)));
        Assert.Equal(new float[] { 2, 6, 5, 1, 4, 3, 7, 0 }, Values(Numa.PseudorandomSequence(8, 42)));
        Assert.Equal(new float[] { 1, 0 }, Values(Numa.PseudorandomSequence(2, 7)));
        Assert.Equal(new float[] { 0 }, Values(Numa.PseudorandomSequence(1, 3)));
    }

    [Fact]
    public void PseudorandomSequence_IsAPermutationAndRepeatable()
    {
        Numa na = Numa.PseudorandomSequence(1000, 12345);
        bool[] seen = new bool[1000];
        for (int i = 0; i < 1000; i++)
            seen[na.GetIValue(i)] = true;
        Assert.All(seen, Assert.True);
        Assert.Equal(Values(na), Values(Numa.PseudorandomSequence(1000, 12345)));
        Assert.NotEqual(Values(na), Values(Numa.PseudorandomSequence(1000, 12346)));
        Assert.Throws<ArgumentException>(() => Numa.PseudorandomSequence(0, 1));
        Assert.Throws<ArgumentException>(() => Numa.PseudorandomSequence(-5, 1));
    }

    [Fact]
    public void RandomPermutation_ShufflesByTheSequence()
    {
        /* the sequence for size 5, seed 1 is 3, 1, 4, 2, 0 */
        Numa nas = MakeNuma(10, 20, 30, 40, 50);
        Assert.Equal(new float[] { 40, 20, 50, 30, 10 }, Values(nas.RandomPermutation(1)));
        Assert.Equal(new float[] { 10, 20, 30, 40, 50 }, Values(nas));
        Assert.Equal(0, Numa.Create(0).RandomPermutation(1).Count);
    }

    /*-------------------------- functions requiring sorting ---------------------*/

    [Fact]
    public void GetRankValue_Fractions()
    {
        Numa na = MakeNuma(5, 1, 4, 2, 3);
        Assert.Equal(1.0f, na.GetRankValue(0.0f, null, 0));
        Assert.Equal(5.0f, na.GetRankValue(1.0f, null, 0));
        Assert.Equal(3.0f, na.GetRankValue(0.5f, null, 0));
        Assert.Equal(2.0f, na.GetRankValue(0.25f, null, 0));   /* index (int)(1.0 + 0.5) = 1 */
        Assert.Equal(4.0f, na.GetRankValue(0.75f, null, 0));   /* index (int)(3.0 + 0.5) = 3 */
        /* bin sort gives the same for non-negative integers */
        Assert.Equal(2.0f, na.GetRankValue(0.25f, null, 1));
        /* an increasing sorted numa can be given */
        Assert.Equal(40.0f, na.GetRankValue(0.5f, MakeNuma(10, 20, 40, 50, 60), 0));
    }

    [Fact]
    public void GetRankValue_InvalidInput()
    {
        Numa na = MakeNuma(5, 1, 4);
        Assert.Throws<ArgumentException>(() => na.GetRankValue(-0.1f, null, 0));
        Assert.Throws<ArgumentException>(() => na.GetRankValue(1.1f, null, 0));
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetRankValue(0.5f, null, 0));
    }

    [Fact]
    public void GetMedian_OddAndEvenCounts()
    {
        Assert.Equal(3.0f, MakeNuma(5, 1, 4, 2, 3).GetMedian());
        /* even count: index (int)(0.5 * 3 + 0.5) = 2, the upper middle value */
        Assert.Equal(3.0f, MakeNuma(4, 1, 3, 2).GetMedian());
        Assert.Equal(7.0f, MakeNuma(7).GetMedian());
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetMedian());
    }

    [Fact]
    public void GetBinnedMedian_EmptyThrows()
    {
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetBinnedMedian());
    }

    [Fact]
    public void GetMeanDevFromMedian_Average()
    {
        /* |1-3| + |2-3| + 0 + |4-3| + |100-3| = 101 */
        Assert.Equal(101.0f / 5.0f, MakeNuma(1, 2, 3, 4, 100).GetMeanDevFromMedian(3.0f));
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetMeanDevFromMedian(0.0f));
    }

    [Fact]
    public void GetMedianDevFromMedian_Values()
    {
        /* median 3; deviations 2 1 0 1 97 have median 1 */
        MakeNuma(1, 2, 3, 4, 100).GetMedianDevFromMedian(out float med, out float dev);
        Assert.Equal(3.0f, med);
        Assert.Equal(1.0f, dev);
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetMedianDevFromMedian(out _, out _));
    }

    [Fact]
    public void GetMode_LargestCountAndTies()
    {
        MakeNuma(1, 2, 2, 3, 3, 3, 4).GetMode(out float val, out int count);
        Assert.Equal((3.0f, 3), (val, count));
        /* tie: the larger value wins (the sort is decreasing, only a larger count replaces) */
        MakeNuma(1, 1, 2, 2).GetMode(out val, out count);
        Assert.Equal((2.0f, 2), (val, count));
        /* the mode is the last run of the decreasing sort */
        MakeNuma(1, 1, 1, 2).GetMode(out val, out count);
        Assert.Equal((1.0f, 3), (val, count));
        MakeNuma(7).GetMode(out val, out count);
        Assert.Equal((7.0f, 1), (val, count));
        Assert.Throws<ArgumentException>(() => Numa.Create(0).GetMode(out _, out _));
    }

    /*------------------------------- rearrangements -----------------------------*/

    [Fact]
    public void Join_AppendsRanges()
    {
        Numa nas = MakeNuma(3, 4, 5, 6);
        Numa nad = MakeNuma(1, 2);
        nad.Join(nas, 0, -1);
        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6 }, Values(nad));

        nad = MakeNuma(1, 2);
        nad.Join(nas, 1, 2);
        Assert.Equal(new float[] { 1, 2, 4, 5 }, Values(nad));

        nad = MakeNuma(1, 2);
        nad.Join(nas, -5, 99);   /* istart < 0 means 0; iend >= n means n - 1 */
        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6 }, Values(nad));

        nad = MakeNuma(1, 2);
        nad.Join(null, 0, -1);   /* no-op */
        Assert.Equal(new float[] { 1, 2 }, Values(nad));
    }

    [Fact]
    public void Join_NothingToAddThrows()
    {
        Numa nad = MakeNuma(1, 2);
        Assert.Throws<ArgumentException>(() => nad.Join(MakeNuma(3, 4), 3, 1));
        Assert.Throws<ArgumentException>(() => nad.Join(MakeNuma(3, 4), 2, -1));
        Assert.Throws<ArgumentException>(() => nad.Join(Numa.Create(0), 0, -1));
        Assert.Equal(new float[] { 1, 2 }, Values(nad));
    }

    private static Numaa MakeNumaa(params Numa[] numas)
    {
        Numaa naa = Numaa.Create(0);
        foreach (Numa na in numas)
            naa.AddNuma(na, L.L_INSERT);
        return naa;
    }

    [Fact]
    public void NumaaJoin_AppendsClonedNumas()
    {
        Numa a = MakeNuma(1), b = MakeNuma(2, 3), c = MakeNuma(4);
        Numaa naas = MakeNumaa(a, b, c);
        Numaa naad = MakeNumaa(MakeNuma(9));
        naad.Join(naas, 1, -1);
        Assert.Equal(3, naad.Count);
        Assert.Same(b, naad.GetNuma(1, L.L_CLONE));
        Assert.Same(c, naad.GetNuma(2, L.L_CLONE));

        naad = MakeNumaa();
        naad.Join(naas, -3, 100);
        Assert.Equal(3, naad.Count);

        naad.Join(null, 0, -1);   /* no-op */
        Assert.Equal(3, naad.Count);
    }

    [Fact]
    public void NumaaJoin_NothingToAddThrows()
    {
        Numaa naad = MakeNumaa(MakeNuma(1));
        Assert.Throws<ArgumentException>(() => naad.Join(MakeNumaa(MakeNuma(1), MakeNuma(2)), 2, 1));
        Assert.Throws<ArgumentException>(() => naad.Join(MakeNumaa(), 0, -1));
    }

    [Fact]
    public void NumaaFlattenToNuma_JoinsAllNumas()
    {
        Numaa naa = MakeNumaa(MakeNuma(1, 2), MakeNuma(3), MakeNuma(4, 5));
        Assert.Equal(new float[] { 1, 2, 3, 4, 5 }, Values(naa.FlattenToNuma()));
        Assert.Equal(new float[] { 3 }, Values(naa.GetNuma(1, L.L_CLONE)));   /* the numas are unchanged */
    }

    [Fact]
    public void NumaaFlattenToNuma_SetsTheCountToTheAllocatedSize()
    {
        /* numaaGetPtrArray() sets the count to nalloc (see UPSTREAM_NOTES.md) */
        Numaa naa = MakeNumaa(MakeNuma(1, 2));
        Assert.Equal(1, naa.Count);
        naa.FlattenToNuma();
        Assert.Equal(naa.Nalloc, naa.Count);
    }

    [Fact]
    public void NumaaFlattenToNuma_SkipsEmptyNumasAndEmptyNumaa()
    {
        /* numaJoin() fails for an empty numa; the error is ignored */
        Numaa naa = MakeNumaa(MakeNuma(1), Numa.Create(0), MakeNuma(2, 3));
        Assert.Equal(new float[] { 1, 2, 3 }, Values(naa.FlattenToNuma()));
        Assert.Equal(0, MakeNumaa().FlattenToNuma().Count);
    }
}
