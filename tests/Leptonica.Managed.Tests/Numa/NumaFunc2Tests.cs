using System;
using Leptonica;

namespace Leptonica.Managed.Tests;

/*
 *  Tests of numafunc2.c.  The expected values are computed by hand from the C code.
 *
 *  Most functions call numafunc1.c (numaMakeConstant, numaGetSum, ...), which may be
 *  a stub in the tree.  Pending() ends such a test quietly when it meets a stub of
 *  numafunc1.c or gplot.c, so that the suite stays green until that file is merged.
 */
public class NumaFunc2Tests
{
    private static void Pending(Action test)
    {
        try {
            test();
        } catch (NotImplementedException e) when (e.Message.StartsWith("leptonica: numa")
                                                  || e.Message.StartsWith("leptonica: gplot")) {
            /* callee not ported yet */
        }
    }

    /* Assert.Throws<ArgumentException>, but a stub of a callee passes through to Pending() */
    private static void ThrowsArg(Action action)
    {
        try {
            action();
        } catch (ArgumentException) {
            return;
        }
        Assert.Fail("ArgumentException expected");
    }

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

    private static void AssertValues(float[] expected, Numa actual, float tol = 0.0f)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(expected[i] - actual.GetFValue(i)) <= tol,
                        $"index {i}: expected {expected[i]}, got {actual.GetFValue(i)}");
    }

    private static void AssertParameters(float startx, float delx, Numa na)
    {
        na.GetParameters(out float s, out float d);
        Assert.Equal((startx, delx), (s, d));
    }

    private static Numa WithParameters(Numa na, float startx, float delx)
    {
        na.SetParameters(startx, delx);
        return na;
    }

    /*------------------------ morphological operations ------------------------*/

    [Fact]
    public void Erode_MinOverWindow_BordersDoNotLimit()
    {
        Pending(() => {
            Numa na = WithParameters(MakeNuma(5, 3, 8, 1, 9, 2), 10.0f, 2.0f);
            Numa nad = na.Erode(3);
            AssertValues(new float[] { 3, 3, 1, 1, 1, 2 }, nad);
            AssertParameters(10.0f, 2.0f, nad);
            AssertValues(new float[] { 3, 3, 1, 1, 1, 2 }, na.Erode(2));  /* even size: increased by 1 */
            AssertValues(new float[] { 1, 1, 1, 1, 1, 1 }, na.Erode(11));
        });
    }

    [Fact]
    public void Dilate_MaxOverWindow()
    {
        Pending(() => {
            Numa na = WithParameters(MakeNuma(5, 3, 8, 1, 9, 2), 10.0f, 2.0f);
            Numa nad = na.Dilate(3);
            AssertValues(new float[] { 5, 8, 8, 9, 9, 9 }, nad);
            AssertParameters(10.0f, 2.0f, nad);
            AssertValues(new float[] { 5, 8, 8, 9, 9, 9 }, na.Dilate(2));  /* even size: increased by 1 */
            AssertValues(new float[] { 8, 8, 9, 9, 9, 9 }, na.Dilate(4));  /* window of 5 */
        });
    }

    [Fact]
    public void ErodeDilate_SizeOneIsACopy_BadSizeThrows()
    {
        Numa na = MakeNuma(5, 3, 8);
        foreach (Numa nad in new[] { na.Erode(1), na.Dilate(1), na.Open(1), na.Close(1) }) {
            Assert.NotSame(na, nad);
            AssertValues(new float[] { 5, 3, 8 }, nad);
        }
        ThrowsArg(() => na.Erode(0));
        ThrowsArg(() => na.Dilate(-1));
        ThrowsArg(() => na.Open(0));
        ThrowsArg(() => na.Close(-3));
    }

    [Fact]
    public void Open_ErodeThenDilate()
    {
        Pending(() => {
            AssertValues(new float[] { 3, 3, 3, 1, 2, 2 }, MakeNuma(5, 3, 8, 1, 9, 2).Open(3));
        });
    }

    [Fact]
    public void Close_UsesABorderSoThatTheResultIsExtensive()
    {
        Pending(() => {
            Numa na = MakeNuma(5, 3, 8, 1, 9, 2);
            Numa nad = na.Close(3);
            AssertValues(new float[] { 5, 5, 8, 8, 9, 2 }, nad);
            for (int i = 0; i < na.Count; i++)
                Assert.True(nad.GetFValue(i) >= na.GetFValue(i));
        });
    }

    /*----------------------------- other transforms -----------------------------*/

    [Fact]
    public void Transform_ShiftsBeforeScaling()
    {
        Numa nad = WithParameters(MakeNuma(1, 2, 3), 4.0f, 0.5f).Transform(1.0f, 2.0f);
        AssertValues(new float[] { 4, 6, 8 }, nad);
        AssertParameters(4.0f, 0.5f, nad);
    }

    [Fact]
    public void SimpleStats_MeanVarianceRmsDeviation()
    {
        Numa na = MakeNuma(1, 2, 3, 4);
        na.SimpleStats(0, -1, out float mean, out float var, out float rvar);
        Assert.Equal((2.5f, 1.25f, MathF.Sqrt(1.25f)), (mean, var, rvar));

        na.SimpleStats(1, 2, out mean, out var, out rvar);
        Assert.Equal((2.5f, 0.25f, 0.5f), (mean, var, rvar));

        na.SimpleStats(-5, 100, out mean, out var, out _);  /* first clipped to 0, last adjusted */
        Assert.Equal((2.5f, 1.25f), (mean, var));
    }

    [Fact]
    public void SimpleStats_InvalidRanges()
    {
        Numa na = MakeNuma(1, 2, 3, 4);
        ThrowsArg(() => na.SimpleStats(4, -1, out _, out _, out _));
        ThrowsArg(() => na.SimpleStats(3, 1, out _, out _, out _));
        ThrowsArg(() => Numa.Create(0).SimpleStats(0, -1, out _, out _, out _));
    }

    [Fact]
    public void WindowedMean_UsesMirroredBorders()
    {
        Pending(() => {
            Numa nad = MakeNuma(1, 2, 3, 4, 5).WindowedMean(1);
            AssertValues(new float[] { 4.0f / 3, 2, 3, 4, 14.0f / 3 }, nad, 1e-5f);
        });
    }

    [Fact]
    public void WindowedMeanSquare_UsesMirroredBorders()
    {
        Pending(() => {
            Numa nad = MakeNuma(1, 2, 3, 4, 5).WindowedMeanSquare(1);
            AssertValues(new float[] { 2, 14.0f / 3, 29.0f / 3, 50.0f / 3, 22 }, nad, 1e-4f);
        });
    }

    [Fact]
    public void WindowedVariance_FromMeanAndMeanSquare()
    {
        Pending(() => {
            Numa na = MakeNuma(1, 2, 3, 4, 5);
            Numa nam = na.WindowedMean(1);
            Numa nams = na.WindowedMeanSquare(1);
            nam.WindowedVariance(nams, out Numa nav, out Numa narv);
            float[] expected = { 2.0f / 9, 2.0f / 3, 2.0f / 3, 2.0f / 3, 2.0f / 9 };
            AssertValues(expected, nav, 1e-4f);
            float[] rexpected = new float[5];
            for (int i = 0; i < 5; i++)
                rexpected[i] = MathF.Sqrt(expected[i]);
            AssertValues(rexpected, narv, 1e-4f);
        });
    }

    [Fact]
    public void WindowedVariance_SizeMismatchThrows()
    {
        ThrowsArg(() => MakeNuma(1, 2).WindowedVariance(MakeNuma(1), out _, out _));
    }

    [Fact]
    public void WindowedStats_AllFourResults()
    {
        Pending(() => {
            Numa na = MakeNuma(4, 4, 4, 4);
            na.WindowedStats(1, out Numa nam, out Numa nams, out Numa nav, out Numa narv);
            AssertValues(new float[] { 4, 4, 4, 4 }, nam, 1e-5f);
            AssertValues(new float[] { 16, 16, 16, 16 }, nams, 1e-4f);
            AssertValues(new float[] { 0, 0, 0, 0 }, nav, 1e-3f);
            Assert.Equal(4, narv.Count);
        });
    }

    [Fact]
    public void WindowedMedian_MirroredBorders()
    {
        Pending(() => {
            Numa na = MakeNuma(1, 9, 2, 8, 3);
            AssertValues(new float[] { 1, 2, 8, 3, 3 }, na.WindowedMedian(1));
            AssertValues(new float[] { 2, 2, 3, 3, 3 }, na.WindowedMedian(2));
            AssertValues(new float[] { 2, 2, 3, 3, 3 }, na.WindowedMedian(10));  /* reduced to 2 */
        });
    }

    [Fact]
    public void WindowedMedian_SmallInputOrFilterGivesACopy()
    {
        Numa two = MakeNuma(7, 3);
        Numa nad = two.WindowedMedian(1);
        Assert.NotSame(two, nad);
        AssertValues(new float[] { 7, 3 }, nad);
        AssertValues(new float[] { 1, 9, 2 }, MakeNuma(1, 9, 2).WindowedMedian(0));
        AssertValues(new float[] { 1, 9, 2 }, MakeNuma(1, 9, 2).WindowedMedian(-4));
    }

    [Fact]
    public void ConvertToInt_RoundsHalfAwayFromZero()
    {
        Numa nad = WithParameters(MakeNuma(1.4f, 2.5f, -1.5f, -0.4f, 3.6f), 3.0f, 0.25f).ConvertToInt();
        AssertValues(new float[] { 1, 3, -2, 0, 4 }, nad);
        AssertParameters(3.0f, 0.25f, nad);
    }

    /*------------------------------- histograms -------------------------------*/

    [Fact]
    public void MakeHistogram_WithoutBinstart_StartsAtZero()
    {
        Pending(() => {
            Numa nah = MakeNuma(0, 1, 1, 3, 3, 3).MakeHistogram(10, out int binsize);
            Assert.Equal(1, binsize);
            AssertValues(new float[] { 1, 2, 0, 3, 0 }, nah);  /* one more bin than needed */
            AssertParameters(0.0f, 1.0f, nah);

            /* negative values are discarded */
            nah = MakeNuma(-3, 2, 5).MakeHistogram(100, out binsize);
            AssertValues(new float[] { 0, 0, 1, 0, 0, 1, 0 }, nah);
        });
    }

    [Fact]
    public void MakeHistogram_WithBinstart_AccommodatesNegativeValues()
    {
        Pending(() => {
            Numa nah = MakeNuma(4, 4, 7).MakeHistogram(100, out int binsize, out int binstart);
            Assert.Equal((1, 4), (binsize, binstart));
            AssertValues(new float[] { 2, 0, 0, 1, 0 }, nah);
            AssertParameters(4.0f, 1.0f, nah);
        });
    }

    [Fact]
    public void MakeHistogram_WithBinstart_NegativeMinimumIsTruncatedTowardZero()
    {
        Pending(() => {
            /* iminval = (int)(-3 + 0.5) = -2, so the bins start at -2 and the value -3 is dropped */
            Numa nah = MakeNuma(-3, -3, 2, 5).MakeHistogram(100, out int binsize, out int binstart);
            Assert.Equal((1, -2), (binsize, binstart));
            AssertValues(new float[] { 0, 0, 0, 0, 1, 0, 0, 1, 0 }, nah);
            AssertParameters(-2.0f, 1.0f, nah);
        });
    }

    [Fact]
    public void MakeHistogram_LargerBinSizes()
    {
        Pending(() => {
            /* range 100 > maxbins - 1: ratio 10.0 is not < 10, so the bin size is 20 */
            Numa nah = MakeNuma(0, 10, 25, 99).MakeHistogram(10, out int binsize, out int binstart);
            Assert.Equal((20, 0), (binsize, binstart));
            AssertValues(new float[] { 2, 1, 0, 0, 1, 0 }, nah);
            AssertParameters(0.0f, 20.0f, nah);

            /* iminval = (int)(-25 + 0.5) = -24, range 65, ratio 13 -> bin size 20, start -40;
             * the value 40 falls in bin 4, beyond the 4 bins, and is dropped */
            nah = MakeNuma(-25, 5, 40).MakeHistogram(5, out binsize, out binstart);
            Assert.Equal((20, -40), (binsize, binstart));
            AssertValues(new float[] { 1, 0, 1, 0 }, nah);
        });
    }

    [Fact]
    public void MakeHistogram_TableOfBinSizes_IsSearchedInOrder()
    {
        Pending(() => {
            /* ratio 15000000 is below 200000000, which comes before 20000000 in the C table */
            Numa nah = MakeNuma(0, 14999999).MakeHistogram(1, out int binsize);
            Assert.Equal(200000000, binsize);
            AssertValues(new float[] { 2 }, nah);
        });
    }

    [Fact]
    public void MakeHistogram_Errors()
    {
        Pending(() => {
            ThrowsArg(() => MakeNuma(1, 2).MakeHistogram(0, out _));
            ThrowsArg(() => MakeNuma(-5, -2).MakeHistogram(10, out _));
            ThrowsArg(() => MakeNuma(0, 2000000000).MakeHistogram(1, out _));  /* too large */
        });
    }

    [Fact]
    public void MakeHistogramAuto_IntegerBinning()
    {
        Pending(() => {
            Numa nah = MakeNuma(3, 5, 5, 4, 3, 3).MakeHistogramAuto(10);
            AssertValues(new float[] { 3, 1, 2 }, nah);
            AssertParameters(3.0f, 1.0f, nah);
        });
    }

    [Fact]
    public void MakeHistogramAuto_FloatBinning()
    {
        Pending(() => {
            Numa nah = MakeNuma(0.5f, 1.5f, 2.5f, 3.5f).MakeHistogramAuto(2);
            AssertValues(new float[] { 2, 2 }, nah);
            AssertParameters(0.5f, 1.5f, nah);

            /* integers with a range not smaller than maxbins are binned as floats */
            nah = MakeNuma(0, 10, 20).MakeHistogramAuto(5);
            AssertValues(new float[] { 1, 0, 1, 0, 1 }, nah);
            AssertParameters(0.0f, 4.0f, nah);
        });
    }

    [Fact]
    public void MakeHistogramAuto_ConstantFloats_OneBin()
    {
        Pending(() => {
            Numa nah = MakeNuma(2.5f, 2.5f, 2.5f).MakeHistogramAuto(4);
            AssertValues(new float[] { 3 }, nah);
            AssertParameters(2.5f, 0.0f, nah);
        });
    }

    [Fact]
    public void MakeHistogramClipped_DiscardsValuesOutsideTheRange()
    {
        Pending(() => {
            Numa nah = MakeNuma(0.2f, 1.7f, 1.2f, 7, -1, 3.9f).MakeHistogramClipped(1.0f, 5.0f);
            AssertValues(new float[] { 1, 2, 0, 1, 0, 0 }, nah);
            AssertParameters(0.0f, 1.0f, nah);

            /* binsize larger than maxsize: just one bin */
            nah = MakeNuma(1, 2, 3).MakeHistogramClipped(10.0f, 4.0f);
            AssertValues(new float[] { 3 }, nah);
            AssertParameters(0.0f, 4.0f, nah);

            ThrowsArg(() => MakeNuma(1).MakeHistogramClipped(0.0f, 4.0f));
        });
    }

    [Fact]
    public void RebinHistogram_SumsGroupsOfBins()
    {
        Numa nad = MakeNuma(1, 2, 3, 4, 5).RebinHistogram(2);
        AssertValues(new float[] { 3, 7, 5 }, nad);
        AssertValues(new float[] { 15 }, MakeNuma(1, 2, 3, 4, 5).RebinHistogram(7));
        ThrowsArg(() => MakeNuma(1, 2).RebinHistogram(1));
        ThrowsArg(() => Numa.Create(0).RebinHistogram(2));
    }

    [Fact]
    public void RebinHistogram_IgnoresTheParametersOfTheSource()
    {
        /* upstream reads startx and delx from the new numa */
        Numa nad = WithParameters(MakeNuma(1, 2, 3, 4), 10.0f, 5.0f).RebinHistogram(2);
        AssertParameters(0.0f, 2.0f, nad);
    }

    [Fact]
    public void NormalizeHistogram_ScalesToTheTargetSum()
    {
        Pending(() => {
            Numa nad = WithParameters(MakeNuma(1, 1, 2), 3.0f, 2.0f).NormalizeHistogram(1.0f);
            AssertValues(new float[] { 0.25f, 0.25f, 0.5f }, nad);
            AssertParameters(3.0f, 2.0f, nad);
            AssertValues(new float[] { 2.5f, 2.5f, 5 }, MakeNuma(1, 1, 2).NormalizeHistogram(10.0f));
            ThrowsArg(() => MakeNuma(1, 1).NormalizeHistogram(0.0f));
            ThrowsArg(() => Numa.Create(0).NormalizeHistogram(1.0f));
        });
    }

    [Fact]
    public void GetStatsUsingHistogram_WithoutHistogram()
    {
        Pending(() => {
            MakeNuma(1, 2, 3, 4).GetStatsUsingHistogram(out float min, out float max, out float mean, out float variance);
            Assert.Equal((1.0f, 4.0f, 2.5f, 1.25f), (min, max, mean, variance));
            ThrowsArg(
                () => Numa.Create(0).GetStatsUsingHistogram(out _, out _, out _, out _));
        });
    }

    [Fact]
    public void GetStatsUsingHistogram_MedianAndRankValue()
    {
        Pending(() => {
            Numa na = MakeNuma(1, 2, 2, 3, 3, 3, 4);
            na.GetStatsUsingHistogram(10, out float min, out float max, out float mean, out float variance,
                                      out float median, 0.9f, out float rval, out Numa histo);
            Assert.Equal((1.0f, 4.0f), (min, max));
            Assert.Equal(18.0f / 7, mean, 5);
            Assert.Equal(52.0f / 7 - mean * mean, variance, 4);
            AssertValues(new float[] { 1, 2, 3, 1 }, histo);
            AssertParameters(1.0f, 1.0f, histo);
            Assert.Equal(1.0f + 2.0f + 0.5f / 3, median, 4);
            Assert.Equal(4.3f, rval, 4);
        });
    }

    [Fact]
    public void GetHistogramStats_XMeanMedianModeVariance()
    {
        Pending(() => {
            Numa nah = MakeNuma(1, 2, 3, 4);
            nah.GetHistogramStats(10.0f, 2.0f, out float mean, out float median, out float mode, out float variance);
            Assert.Equal((14.0f, 14.0f, 16.0f, 4.0f), (mean, median, mode, variance));
        });
    }

    [Fact]
    public void GetHistogramStatsOnInterval_UsesTheBinsInTheInterval()
    {
        MakeNuma(1, 2, 3, 4).GetHistogramStatsOnInterval(10.0f, 2.0f, 1, 2,
            out float mean, out float median, out float mode, out float variance);
        Assert.Equal(66.0f / 5, mean, 4);
        Assert.Equal(14.0f, median);  /* x = 12, 14: halfsum 2.5 is reached at the second bin */
        Assert.Equal(14.0f, mode);
        Assert.Equal((144 * 2 + 196 * 3) / 5.0f - (66.0f * 66) / 25, variance, 3);

        /* ilast beyond the end is adjusted */
        MakeNuma(1, 2, 3, 4).GetHistogramStatsOnInterval(10.0f, 2.0f, 2, 10,
            out mean, out _, out mode, out _);
        Assert.Equal((14.0f * 3 + 16.0f * 4) / 7, mean, 4);
        Assert.Equal(16.0f, mode);
    }

    [Fact]
    public void GetHistogramStatsOnInterval_ZeroSumAndErrors()
    {
        MakeNuma(0, 0, 0).GetHistogramStats(1.0f, 1.0f, out float mean, out float median, out float mode, out float variance);
        Assert.Equal((0.0f, 0.0f, 0.0f, 0.0f), (mean, median, mode, variance));

        Numa nah = MakeNuma(1, 2, 3);
        ThrowsArg(
            () => nah.GetHistogramStatsOnInterval(0, 1, 3, -1, out _, out _, out _, out _));
        ThrowsArg(
            () => nah.GetHistogramStatsOnInterval(0, 1, 2, 1, out _, out _, out _, out _));
    }

    [Fact]
    public void MakeRankFromHistogram_CumulativeNormalized()
    {
        Pending(() => {
            Numa.MakeRankFromHistogram(0.0f, 1.0f, MakeNuma(1, 1, 2), 4, out Numa? nax, out Numa nay);
            AssertValues(new float[] { 0, 0.25f, 0.5f, 1 }, nay, 1e-6f);
            Assert.NotNull(nax);
            AssertValues(new float[] { 0, 1, 2, 3 }, nax!, 1e-6f);
            ThrowsArg(
                () => Numa.MakeRankFromHistogram(0.0f, 1.0f, Numa.Create(0), 4, out _, out _));
        });
    }

    [Fact]
    public void HistogramGetRankFromVal_FractionBelowTheValue()
    {
        Pending(() => {
            Numa nah = MakeNuma(1, 2, 3, 4);
            Assert.Equal(0.0f, nah.HistogramGetRankFromVal(-1.0f));
            Assert.Equal(0.0f, nah.HistogramGetRankFromVal(0.0f));
            Assert.Equal(0.45f, nah.HistogramGetRankFromVal(2.5f), 6);
            Assert.Equal(1.0f, nah.HistogramGetRankFromVal(4.0f));
            Assert.Equal(1.0f, nah.HistogramGetRankFromVal(100.0f));

            /* x = startx + i * binsize */
            Numa nah2 = WithParameters(MakeNuma(1, 2, 3, 4), 10.0f, 2.0f);
            Assert.Equal(0.3f, nah2.HistogramGetRankFromVal(14.0f), 6);
        });
    }

    [Fact]
    public void HistogramGetValFromRank_InverseOfTheRank()
    {
        Pending(() => {
            Numa nah = MakeNuma(1, 2, 3, 4);
            Assert.Equal(0.0f, nah.HistogramGetValFromRank(0.0f));
            Assert.Equal(4.0f, nah.HistogramGetValFromRank(1.0f));
            Assert.Equal(2.0f + 2.0f / 3, nah.HistogramGetValFromRank(0.5f), 5);
            Assert.Equal(0.0f, nah.HistogramGetValFromRank(-0.5f));  /* clamped */
            Assert.Equal(4.0f, nah.HistogramGetValFromRank(1.5f));   /* clamped */
        });
    }

    [Fact]
    public void DiscretizeSortedInBins_AveragesOfEqualPopulationBins()
    {
        AssertValues(new float[] { 1.5f, 3.5f, 5.5f }, MakeNuma(1, 2, 3, 4, 5, 6).DiscretizeSortedInBins(3));
        AssertValues(new float[] { 1.5f, 4 }, MakeNuma(1, 2, 3, 4, 5).DiscretizeSortedInBins(2));
        ThrowsArg(() => MakeNuma(1, 2, 3).DiscretizeSortedInBins(1));
    }

    [Fact]
    public void DiscretizeHistoInBins_AveragesAndRank()
    {
        Pending(() => {
            MakeNuma(0, 4, 0, 4).DiscretizeHistoInBins(2, out Numa nabinval, out Numa narank);
            AssertValues(new float[] { 1, 3 }, nabinval);
            AssertValues(new float[] { 0, 0.5f, 0.5f, 1 }, narank);
            ThrowsArg(
                () => MakeNuma(0, 4).DiscretizeHistoInBins(1, out _, out _));
        });
    }

    [Fact]
    public void GetRankBinValues_SortedArray()
    {
        Pending(() => {
            AssertValues(new float[] { 1.5f, 3.5f, 5.5f }, MakeNuma(5, 1, 4, 2, 3, 6).GetRankBinValues(3));
            ThrowsArg(() => MakeNuma(5, 1).GetRankBinValues(1));
            ThrowsArg(() => Numa.Create(0).GetRankBinValues(3));
        });
    }

    [Fact]
    public void GetRankBinValues_Histogram()
    {
        Pending(() => {
            /* 400 values in 0..9, 40 each: a histogram is used */
            Numa na = Numa.Create(400);
            for (int i = 0; i < 400; i++)
                na.AddNumber(i % 10);
            AssertValues(new float[] { 0.5f, 2.5f, 4.5f, 6.5f, 8.5f }, na.GetRankBinValues(5));
        });
    }

    [Fact]
    public void GetUniformBinSizes_SplitsEvenly()
    {
        AssertValues(new float[] { 3, 3, 4 }, Numa.GetUniformBinSizes(10, 3));
        AssertValues(new float[] { 2, 2, 2 }, Numa.GetUniformBinSizes(6, 3));
        AssertValues(new float[] { 5 }, Numa.GetUniformBinSizes(5, 1));
        AssertValues(new float[] { 1, 1 }, Numa.GetUniformBinSizes(2, 5));  /* fewer values than bins */
        ThrowsArg(() => Numa.GetUniformBinSizes(0, 3));
        ThrowsArg(() => Numa.GetUniformBinSizes(3, 0));
    }

    /*--------------------------- splitting a distribution ---------------------------*/

    [Fact]
    public void SplitDistribution_TwoEqualBumps()
    {
        Pending(() => {
            Numa na = MakeNuma(4, 0, 0, 0, 0, 0, 0, 0, 0, 4);
            na.SplitDistribution(0.1f, out int splitindex, out float ave1, out float ave2,
                                 out float num1, out float num2);
            /* all scores in 1..8 are 1.0; the histogram minimum is first reached at index 1,
             * and the threshold is one more */
            Assert.Equal(2, splitindex);
            Assert.Equal((0.0f, 9.0f, 4.0f, 4.0f), (ave1, ave2, num1, num2));
        });
    }

    [Fact]
    public void SplitDistribution_SplitBeyondTheArrayGivesZeros()
    {
        Pending(() => {
            /* bestsplit + 1 == n: C ignores the failure of numaGetFValue() and returns 0 */
            MakeNuma(5, 3).SplitDistribution(1.0f, out int splitindex, out float ave1, out float ave2,
                                             out float num1, out float num2);
            Assert.Equal(2, splitindex);
            Assert.Equal((0.0f, 0.0f, 0.0f, 0.0f), (ave1, ave2, num1, num2));

            /* the usual case */
            MakeNuma(5, 3).SplitDistribution(0.1f, out splitindex, out ave1, out ave2, out num1, out num2);
            Assert.Equal(1, splitindex);
            Assert.Equal((0.375f, 1.0f, 8.0f, 0.0f), (ave1, ave2, num1, num2));
        });
    }

    [Fact]
    public void SplitDistribution_Errors()
    {
        Pending(() => {
            ThrowsArg(() => MakeNuma(5).SplitDistribution(0.1f, out _, out _, out _, out _, out _));
            ThrowsArg(() => MakeNuma(0, 0, 0).SplitDistribution(0.1f, out _, out _, out _, out _, out _));
        });
    }

    /*----------------------------- comparing histograms -----------------------------*/

    private static Numa Histogram256(int index, float value)
    {
        Numa na = Numa.Create(256);
        for (int i = 0; i < 256; i++)
            na.AddNumber(i == index ? value : 0.0f);
        return na;
    }

    [Fact]
    public void EarthMoverDistance_MovesEarthSequentially()
    {
        Pending(() => {
            Assert.Equal(2.0f, MakeNuma(1, 0, 0).EarthMoverDistance(MakeNuma(0, 0, 2)));  /* normalized to the first */
            Assert.Equal(0.0f, MakeNuma(1, 2, 3).EarthMoverDistance(MakeNuma(1, 2, 3)));
            Assert.Equal(1.0f, MakeNuma(1, 0).EarthMoverDistance(MakeNuma(0, 1)));
            ThrowsArg(() => MakeNuma(1, 2).EarthMoverDistance(MakeNuma(1, 2, 3)));
        });
    }

    [Fact]
    public void GrayHistogramsToEMD_NormalizedToTheMaximumDistance()
    {
        Pending(() => {
            Numaa naa1 = Numaa.Create(2);
            naa1.AddNuma(Histogram256(0, 10), L.L_INSERT);
            naa1.AddNuma(Histogram256(100, 5), L.L_INSERT);
            Numaa naa2 = Numaa.Create(2);
            naa2.AddNuma(Histogram256(255, 10), L.L_INSERT);
            naa2.AddNuma(Histogram256(100, 5), L.L_INSERT);
            AssertValues(new float[] { 1.0f, 0.0f }, Numa.GrayHistogramsToEMD(naa1, naa2));
        });
    }

    [Fact]
    public void GrayHistogramsToEMD_Errors()
    {
        Pending(() => {
            Numaa one = Numaa.Create(1);
            one.AddNuma(Histogram256(0, 1), L.L_INSERT);
            Numaa two = Numaa.Create(2);
            two.AddNuma(Histogram256(0, 1), L.L_INSERT);
            two.AddNuma(Histogram256(0, 1), L.L_INSERT);
            ThrowsArg(() => Numa.GrayHistogramsToEMD(one, two));

            Numaa small = Numaa.Create(1);
            small.AddNuma(MakeNuma(1, 2, 3), L.L_INSERT);
            ThrowsArg(() => Numa.GrayHistogramsToEMD(small, small));
        });
    }

    [Fact]
    public void GrayInterHistogramStats_ColumnStatistics()
    {
        Pending(() => {
            /* After normalization to 10000: 78.125 in half of the bins, 0 in the others */
            Numa h1 = Numa.Create(256);
            Numa h2 = Numa.Create(256);
            for (int i = 0; i < 256; i++) {
                h1.AddNumber(i < 128 ? 2 : 0);
                h2.AddNumber(i < 128 ? 0 : 2);
            }
            Numaa naa = Numaa.Create(2);
            naa.AddNuma(h1, L.L_INSERT);
            naa.AddNuma(h2, L.L_INSERT);
            Numa.GrayInterHistogramStats(naa, 0, out Numa nam, out Numa nams, out Numa nav, out Numa narv);
            AssertValues(Constant(256, 39.0625f), nam, 1e-3f);
            AssertValues(Constant(256, 1525.87890625f), nams, 1e-2f);  /* mean * mean */
            AssertValues(Constant(256, 1525.87890625f), nav, 1e-2f);
            AssertValues(Constant(256, 39.0625f), narv, 1e-3f);

            Numaa bad = Numaa.Create(1);
            bad.AddNuma(MakeNuma(1, 2, 3), L.L_INSERT);
            ThrowsArg(() => Numa.GrayInterHistogramStats(bad, 0, out _, out _, out _, out _));
        });
    }

    private static float[] Constant(int n, float value)
    {
        float[] values = new float[n];
        Array.Fill(values, value);
        return values;
    }

    /*-------------------------------- extrema finding --------------------------------*/

    [Fact]
    public void FindPeaks_LargestPeaksFirst()
    {
        Pending(() => {
            Numa na = MakeNuma(0, 1, 5, 2, 0, 0, 3, 8, 3, 0);
            Numa napeak = na.FindPeaks(2, 0.3f, 0.5f);
            AssertValues(new float[] { 6, 7, 8, 14.0f / 22, 1, 2, 3, 8.0f / 22 }, napeak, 1e-6f);
            AssertValues(Values(napeak), na.FindPeaks(5, 0.3f, 0.5f));  /* no more peaks: stops when the array is zero */
            AssertValues(new float[] { 6, 7, 8, 14.0f / 22 }, na.FindPeaks(1, 0.3f, 0.5f), 1e-6f);
            Assert.Equal(0, na.FindPeaks(0, 0.3f, 0.5f).Count);
        });
    }

    [Fact]
    public void FindExtrema_AlternatingPeaksAndValleys()
    {
        Numa nad = MakeNuma(0, 5, 1, 6, 2, 8, 8, 3).FindExtrema(3.0f, out Numa nav);
        AssertValues(new float[] { 1, 2, 3, 4, 5 }, nad);
        AssertValues(new float[] { 5, 1, 6, 2, 8 }, nav);

        nad = MakeNuma(5, 0, 4).FindExtrema(3.0f, out nav);  /* starts with a valley */
        AssertValues(new float[] { 1 }, nad);
        AssertValues(new float[] { 0 }, nav);
    }

    [Fact]
    public void FindExtrema_NothingFoundOrEmpty()
    {
        Numa nad = MakeNuma(1, 1.5f, 1.2f).FindExtrema(1.0f, out Numa nav);
        Assert.Equal(0, nad.Count);
        Assert.Equal(0, nav.Count);
        Assert.Equal(0, Numa.Create(0).FindExtrema(1.0f, out _).Count);
        ThrowsArg(() => MakeNuma(1, 2).FindExtrema(-1.0f, out _));
    }

    [Fact]
    public void FindLocForThreshold_ValleyAfterTheFirstPeak()
    {
        Pending(() => {
            Numa na = MakeNuma(1, 3, 6, 9, 7, 4, 2, 1, 2, 5, 8, 10, 6, 3, 1, 0, 0, 1, 0, 0);
            Assert.True(na.FindLocForThreshold(2, out int thresh, out float fract));
            /* the rise is detected at index 8, jumping ahead to 10; the backward search
             * covers only the points after the rise */
            Assert.Equal(9, thresh);
            Assert.Equal(40.0f / 69, fract, 5);
        });
    }

    [Fact]
    public void FindLocForThreshold_NoThreshold()
    {
        Pending(() => {
            Assert.False(MakeNuma(2, 2, 2, 2).FindLocForThreshold(2, out int thresh, out float fract));
            Assert.Equal((0, 0.0f), (thresh, fract));

            float[] rising = new float[20];
            for (int i = 0; i < 20; i++)
                rising[i] = i;
            Assert.False(MakeNuma(rising).FindLocForThreshold(2, out thresh, out _));  /* no peak */

            Numa falling = MakeNuma(1, 3, 6, 9, 7, 5, 4, 3, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.False(falling.FindLocForThreshold(2, out thresh, out fract));  /* no minimum */
            Assert.Equal((0, 0.0f), (thresh, fract));
        });
    }

    [Fact]
    public void CountReversals_BinaryAndGeneral()
    {
        Pending(() => {
            MakeNuma(0, 0, 1, 1, 0, 1).CountReversals(1.0f, out int nr, out float rd);
            Assert.Equal((3, 0.5f), (nr, rd));

            MakeNuma(0, 0, 1, 1, 0, 1).CountReversals(2.0f, out nr, out rd);  /* binary, minreversal > 1 */
            Assert.Equal((0, 0.0f), (nr, rd));

            Numa na = WithParameters(MakeNuma(0, 5, 1, 6, 2, 8, 8, 3), 0.0f, 2.0f);
            na.CountReversals(3.0f, out nr, out rd);
            Assert.Equal((5, 0.3125f), (nr, rd));

            Numa.Create(0).CountReversals(1.0f, out nr, out rd);
            Assert.Equal((0, 0.0f), (nr, rd));
            ThrowsArg(() => na.CountReversals(-1.0f, out _, out _));
        });
    }

    /*------------------- threshold crossings and frequency analysis -------------------*/

    [Fact]
    public void CrossingsByThreshold_Interpolates()
    {
        Pending(() => {
            Numa nay = MakeNuma(0, 10, 0, 10);
            AssertValues(new float[] { 0.5f, 1.5f, 2.5f }, Numa.CrossingsByThreshold(null, nay, 5.0f));

            nay.SetParameters(100.0f, 2.0f);
            AssertValues(new float[] { 101, 103, 105 }, Numa.CrossingsByThreshold(null, nay, 5.0f));

            Numa nax = MakeNuma(0, 1, 3, 4);
            AssertValues(new float[] { 0.5f, 2, 3.5f }, Numa.CrossingsByThreshold(nax, nay, 5.0f));
        });
    }

    [Fact]
    public void CrossingsByThreshold_ValueEqualToTheThresholdCountsTwice()
    {
        Pending(() => {
            /* both neighbouring pairs report the point where the value equals the threshold */
            AssertValues(new float[] { 1, 1, 3 }, Numa.CrossingsByThreshold(null, MakeNuma(0, 10, 0, 10), 10.0f));
        });
    }

    [Fact]
    public void CrossingsByThreshold_EdgeCases()
    {
        Pending(() => {
            Assert.Equal(0, Numa.CrossingsByThreshold(null, MakeNuma(7), 5.0f).Count);
            ThrowsArg(() => Numa.CrossingsByThreshold(MakeNuma(1, 2), MakeNuma(1, 2, 3), 1.0f));
        });
    }

    [Fact]
    public void SelectCrossingThreshold_CenterOfThePlateau()
    {
        Pending(() => {
            /* all 41 thresholds (20 ... 180) cross all 5 transitions */
            Assert.True(Numa.SelectCrossingThreshold(null, MakeNuma(0, 200, 0, 200, 0, 200), 100.0f,
                                                     out float best));
            Assert.Equal(100.0f, best);

            /* the thresholds 20 ... 100 give 6 crossings, the higher ones none */
            Assert.True(Numa.SelectCrossingThreshold(null, MakeNuma(0, 100, 0, 100, 0, 100, 0), 100.0f,
                                                     out best));
            Assert.Equal(60.0f, best);
        });
    }

    [Fact]
    public void SelectCrossingThreshold_TooFewPointsIsAWarning()
    {
        Assert.False(Numa.SelectCrossingThreshold(null, MakeNuma(5), 100.0f, out float best));
        Assert.Equal(0.0f, best);
    }

    [Fact]
    public void CrossingsByPeaks_CrossesTheMidlevelBetweenExtrema()
    {
        Pending(() => {
            AssertValues(new float[] { 0.5f, 1.5f, 2.5f, 3.5f},
                         Numa.CrossingsByPeaks(null, MakeNuma(0, 10, 0, 10, 0), 5.0f));
            ThrowsArg(() => Numa.CrossingsByPeaks(MakeNuma(1), MakeNuma(1, 2), 1.0f));
        });
    }

    [Fact]
    public void EvalHaarSum_CombScore()
    {
        Pending(() => {
            Numa na = MakeNuma(0, 1, 0, 1, 0, 1, 0, 1);
            Assert.Equal(1.0f, na.EvalHaarSum(1.0f, 0.0f, 1.0f));
            Assert.Equal(0.75f, na.EvalHaarSum(1.0f, 0.5f, 1.0f));
            Assert.Equal(-0.5f, na.EvalHaarSum(2.0f, 1.0f, 1.0f));
            Assert.Equal(1.0f, na.EvalHaarSum(1.0f, 0.0f, 3.0f));  /* only the +1 weights see the nonzero values */
            ThrowsArg(() => MakeNuma(1, 2, 3).EvalHaarSum(2.0f, 0.0f, 1.0f));  /* n < 2 * width */
        });
    }

    [Fact]
    public void EvalBestHaarParameters_FindsTheBestWidthAndShift()
    {
        Pending(() => {
            Numa na = MakeNuma(0, 1, 0, 1, 0, 1, 0, 1);
            na.EvalBestHaarParameters(1.0f, 2, 2, 1.0f, 2.0f, out float width, out float shift, out float score);
            Assert.Equal((1.0f, 0.0f, 1.0f), (width, shift, score));

            /* widths that are too large for the array score 0 and are skipped */
            na.EvalBestHaarParameters(1.0f, 2, 2, 1.0f, 10.0f, out width, out shift, out score);
            Assert.Equal((1.0f, 0.0f, 1.0f), (width, shift, score));
        });
    }

    /*------------------------ generating numbers in a range ------------------------*/

    [Fact]
    public void GenConstrainedNumaInRange_Singles()
    {
        AssertValues(new float[] { 0, 3, 6, 9 }, Numa.GenConstrainedNumaInRange(0, 9, 4, 0));
        AssertValues(new float[] { 5, 10, 15, 20 }, Numa.GenConstrainedNumaInRange(5, 20, 4, 0));
        AssertValues(new float[] { 2 }, Numa.GenConstrainedNumaInRange(2, 9, 1, 0));
        AssertValues(new float[] { 0, 1, 2 }, Numa.GenConstrainedNumaInRange(-5, 2, 10, 0));  /* first >= 0, at most last - first + 1 */
    }

    [Fact]
    public void GenConstrainedNumaInRange_Pairs()
    {
        AssertValues(new float[] { 0, 1, 8, 9 }, Numa.GenConstrainedNumaInRange(0, 9, 4, 1));
        AssertValues(new float[] { 3, 4 }, Numa.GenConstrainedNumaInRange(3, 9, 2, 1));
    }

    [Fact]
    public void GenConstrainedNumaInRange_Errors()
    {
        ThrowsArg(() => Numa.GenConstrainedNumaInRange(5, 4, 3, 0));   /* last < first */
        ThrowsArg(() => Numa.GenConstrainedNumaInRange(0, 9, 0, 0));   /* nmax < 1 */
        ThrowsArg(() => Numa.GenConstrainedNumaInRange(0, 9, 1, 1));   /* no pair */
    }
}
