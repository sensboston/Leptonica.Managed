using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.RunLengthTests;

public class RunLengthTests
{
    // 10 x 3 image; row 0: "..XXX..XX." ; row 1: all on; row 2: all off
    private static Pix MakePix()
    {
        Pix pix = Pix.Create(10, 3, 1);
        foreach (int x in new[] { 2, 3, 4, 7, 8 })
            pix.SetPixel(x, 0, 1);
        for (int x = 0; x < 10; x++)
            pix.SetPixel(x, 1, 1);
        return pix;
    }

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    [Fact]
    public void FindHorizontalRuns_FindsRuns()
    {
        Pix pix = MakePix();
        int[] s = new int[6], e = new int[6];
        pix.FindHorizontalRuns(0, s, e, out int n);
        Assert.Equal(2, n);
        Assert.Equal(new[] { 2, 7 }, new[] { s[0], s[1] });
        Assert.Equal(new[] { 4, 8 }, new[] { e[0], e[1] });
        pix.FindHorizontalRuns(1, s, e, out n);
        Assert.Equal(1, n);
        Assert.Equal((0, 9), (s[0], e[0]));
        pix.FindHorizontalRuns(2, s, e, out n);
        Assert.Equal(0, n);
    }

    [Fact]
    public void FindHorizontalRuns_RejectsBadInput()
    {
        Pix pix = MakePix();
        int[] s = new int[6], e = new int[6];
        Assert.Throws<ArgumentException>(() => pix.FindHorizontalRuns(3, s, e, out _));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).FindHorizontalRuns(0, s, e, out _));
    }

    [Fact]
    public void FindVerticalRuns_FindsRuns()
    {
        Pix pix = MakePix();
        int[] s = new int[3], e = new int[3];
        pix.FindVerticalRuns(3, s, e, out int n);
        Assert.Equal(1, n);
        Assert.Equal((0, 1), (s[0], e[0]));
        pix.FindVerticalRuns(0, s, e, out n);
        Assert.Equal(1, n);
        Assert.Equal((1, 1), (s[0], e[0]));
        Assert.Throws<ArgumentException>(() => pix.FindVerticalRuns(10, s, e, out _));
    }

    [Fact]
    public void FindMaxHorizontalRunOnLine_ReturnsLongestRun()
    {
        Pix pix = MakePix();
        pix.FindMaxHorizontalRunOnLine(0, out int start, out int size);
        Assert.Equal((2, 3), (start, size));
        pix.FindMaxHorizontalRunOnLine(1, out start, out size);
        Assert.Equal((0, 10), (start, size));
        pix.FindMaxHorizontalRunOnLine(2, out start, out size);
        Assert.Equal((0, 0), (start, size));
    }

    [Fact]
    public void FindMaxVerticalRunOnLine_ReturnsLongestRun()
    {
        Pix pix = MakePix();
        pix.FindMaxVerticalRunOnLine(3, out int start, out int size);
        Assert.Equal((0, 2), (start, size));
        pix.FindMaxVerticalRunOnLine(0, out start, out size);
        Assert.Equal((1, 1), (start, size));
        pix.FindMaxVerticalRunOnLine(5, out start, out size);
        Assert.Equal((1, 1), (start, size));
    }

    [Fact]
    public void FindMaxRuns_CollectsPerLine()
    {
        Pix pix = MakePix();
        Numa na = pix.FindMaxRuns(L.L_HORIZONTAL_RUNS, out Numa nastart);
        Assert.Equal(3, na.Count);
        Assert.Equal(new float[] { 3, 10, 0 }, new[] { na.GetFValue(0), na.GetFValue(1), na.GetFValue(2) });
        Assert.Equal(new float[] { 2, 0, 0 }, new[] { nastart.GetFValue(0), nastart.GetFValue(1), nastart.GetFValue(2) });
        na = pix.FindMaxRuns(L.L_VERTICAL_RUNS, out nastart);
        Assert.Equal(10, na.Count);
        Assert.Equal(2f, na.GetFValue(3));
        Assert.Equal(1f, na.GetFValue(0));
        Assert.Equal(1f, nastart.GetFValue(0));
    }

    [Fact]
    public void RunlengthMembershipOnLine_LabelsRuns()
    {
        int[] buf = new int[10];
        Array.Fill(buf, 99);
        L.RunlengthMembershipOnLine(buf, 10, 8, new[] { 2, 7 }, new[] { 4, 8 }, 2);
        Assert.Equal(new[] { 0, 0, 3, 3, 3, 0, 0, 2, 2, 0 }, buf);
    }

    [Fact]
    public void RunlengthMembershipOnLine_ClipsToMax()
    {
        int[] buf = new int[1000];
        L.RunlengthMembershipOnLine(buf, 1000, 8, new[] { 0 }, new[] { 999 }, 1);
        Assert.Equal(255, buf[500]);
        L.RunlengthMembershipOnLine(buf, 1000, 16, new[] { 0 }, new[] { 999 }, 1);
        Assert.Equal(1000, buf[500]);
    }

    [Fact]
    public void RunlengthTransform_Horizontal()
    {
        Pix pix = MakePix();
        Pix d = pix.RunlengthTransform(1, L.L_HORIZONTAL_RUNS, 8);
        Assert.Equal(8, d.Depth);
        Assert.Equal(new uint[] { 0, 0, 3, 3, 3, 0, 0, 2, 2, 0 }, Row(d, 0));
        Assert.Equal(10u, Get(d, 5, 1));
        Assert.Equal(0u, Get(d, 5, 2));
    }

    [Fact]
    public void RunlengthTransform_WhiteRunsVertical16()
    {
        Pix pix = MakePix();
        Pix d = pix.RunlengthTransform(0, L.L_VERTICAL_RUNS, 16);
        Assert.Equal(16, d.Depth);
        // column 0: off, on, off -> white runs of length 1 at rows 0 and 2
        Assert.Equal(1u, Get(d, 0, 0));
        Assert.Equal(0u, Get(d, 0, 1));
        Assert.Equal(1u, Get(d, 0, 2));
        // column 3: on, on, off
        Assert.Equal(0u, Get(d, 3, 0));
        Assert.Equal(1u, Get(d, 3, 2));
    }

    [Fact]
    public void StrokeWidthTransform_TwoAngles_IsMinOfRuns()
    {
        Pix pix = Pix.Create(12, 12, 1);
        for (int y = 3; y < 7; y++)
            for (int x = 2; x < 10; x++)
                pix.SetPixel(x, y, 1);   // 8 wide, 4 high block
        Pix d = pix.StrokeWidthTransform(1, 8, 2);
        Assert.Equal(4u, Get(d, 5, 4));
        Assert.Equal(0u, Get(d, 0, 0));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void StrokeWidthTransform_MoreAngles_NeverExceedTwoAngleResult(int nangles)
    {
        Pix pix = Pix.Create(30, 30, 1);
        for (int y = 8; y < 14; y++)
            for (int x = 5; x < 25; x++)
                pix.SetPixel(x, y, 1);
        Pix d2 = pix.StrokeWidthTransform(1, 8, 2);
        Pix dn = pix.StrokeWidthTransform(1, 8, nangles);
        Assert.Equal((30, 30, 8), (dn.Width, dn.Height, dn.Depth));
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 30; x++)
                Assert.True(Get(dn, x, y) <= Get(d2, x, y));
    }

    [Fact]
    public void StrokeWidthTransform_RejectsBadArgs()
    {
        Pix pix = MakePix();
        Assert.Throws<ArgumentException>(() => pix.StrokeWidthTransform(1, 4, 2));
        Assert.Throws<ArgumentException>(() => pix.StrokeWidthTransform(1, 8, 3));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).StrokeWidthTransform(1, 8, 2));
    }

    [Fact]
    public void MakeMSBitLocTab_Values()
    {
        int[] tab1 = L.MakeMSBitLocTab(1);
        Assert.Equal(8, tab1[0]);
        Assert.Equal(0, tab1[0x80]);
        Assert.Equal(7, tab1[0x01]);
        Assert.Equal(3, tab1[0x1f]);
        int[] tab0 = L.MakeMSBitLocTab(0);
        Assert.Equal(8, tab0[0xff]);
        Assert.Equal(0, tab0[0x00]);
        Assert.Equal(1, tab0[0x80]);
    }

    private static uint[] Row(Pix pix, int y)
    {
        uint[] r = new uint[pix.Width];
        for (int x = 0; x < r.Length; x++)
            r[x] = Get(pix, x, y);
        return r;
    }
}
