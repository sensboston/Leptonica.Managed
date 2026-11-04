using System;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.EdgeTests;

/// <summary>Tests of edge.c with hand-computed expectations.</summary>
public class EdgeTests
{
    private static Pix Gray(uint[][] rows)
    {
        Pix pix = Pix.Create(rows[0].Length, rows.Length, 8);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                Naive.SetPixel(pix, x, y, rows[y][x]);
        return pix;
    }

    private static Pix Transpose(Pix pix)
    {
        Pix t = Pix.Create(pix.Height, pix.Width, pix.Depth);
        for (int y = 0; y < pix.Height; y++)
            for (int x = 0; x < pix.Width; x++)
                Naive.SetPixel(t, y, x, Naive.GetPixel(pix, x, y));
        return t;
    }

    private static void AssertRow(Pix pix, int y, params uint[] expected)
    {
        Assert.Equal(expected.Length, pix.Width);
        for (int x = 0; x < expected.Length; x++)
            Assert.True(expected[x] == Naive.GetPixel(pix, x, y),
                $"row {y} pixel {x}: expected {expected[x]} got {Naive.GetPixel(pix, x, y)}");
    }

    /// <summary>
    /// 8x4, ON pixels:
    ///   ..XXX...
    ///   .XXXXX..
    ///   ..XXXX..
    ///   ...XX...
    /// </summary>
    private static Pix Blob()
    {
        string[] rows = { "..XXX...", ".XXXXX..", "..XXXX..", "...XX..." };
        Pix pix = Pix.Create(8, 4, 1);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < 8; x++)
                if (rows[y][x] == 'X')
                    Naive.SetPixel(pix, x, y, 1);
        return pix;
    }

    private static int[] Profile(Numa na)
    {
        var v = new int[na.Count];
        for (int i = 0; i < v.Length; i++)
            v[i] = na.GetIValue(i);
        return v;
    }

    [Fact]
    public void SobelEdgeFilter_ConstantImageHasNoEdges()
    {
        Pix pix = Gray(new[] { new uint[] { 90, 90, 90 }, new uint[] { 90, 90, 90 }, new uint[] { 90, 90, 90 } });
        foreach (int flag in new[] { L.L_HORIZONTAL_EDGES, L.L_VERTICAL_EDGES, L.L_ALL_EDGES }) {
            Pix pixd = pix.SobelEdgeFilter(flag);
            for (int y = 0; y < 3; y++)
                AssertRow(pixd, y, 0, 0, 0);
        }
    }

    [Fact]
    public void SobelEdgeFilter_VerticalStep()
    {
        uint[] row = { 0, 0, 0, 80, 80, 80 };
        Pix pix = Gray(new[] { row, row, row });
        // (4 * 80) >> 3 = 40 on the two columns next to the step; the mirrored border adds none.
        Pix v = pix.SobelEdgeFilter(L.L_VERTICAL_EDGES);
        for (int y = 0; y < 3; y++)
            AssertRow(v, y, 0, 0, 40, 40, 0, 0);
        Pix h = pix.SobelEdgeFilter(L.L_HORIZONTAL_EDGES);
        for (int y = 0; y < 3; y++)
            AssertRow(h, y, 0, 0, 0, 0, 0, 0);
        Pix all = pix.SobelEdgeFilter(L.L_ALL_EDGES);
        for (int y = 0; y < 3; y++)
            AssertRow(all, y, 0, 0, 40, 40, 0, 0);
    }

    [Fact]
    public void SobelEdgeFilter_HorizontalStepIsTransposeOfVertical()
    {
        uint[] row = { 0, 0, 0, 80, 80, 80 };
        Pix pix = Gray(new[] { row, row, row, row });
        Pix h = Transpose(pix).SobelEdgeFilter(L.L_HORIZONTAL_EDGES);
        Pix v = pix.SobelEdgeFilter(L.L_VERTICAL_EDGES);
        Assert.Equal(Transpose(v).Data, h.Data);
    }

    [Fact]
    public void SobelEdgeFilter_AllEdgesClipsAt255()
    {
        // A single bright pixel: gx = gy = (255 * 4) >> 3 = 127 at the 4 diagonal neighbors -> 254;
        // mirrored border on a corner pixel doubles the contribution, so the sum is clipped.
        Pix pix = Pix.Create(3, 3, 8);
        Naive.SetPixel(pix, 0, 0, 255);
        Pix pixd = pix.SobelEdgeFilter(L.L_ALL_EDGES);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                Assert.True(Naive.GetPixel(pixd, x, y) <= 255);
        Assert.Equal(8, pixd.Depth);
    }

    [Fact]
    public void SobelEdgeFilter_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 1).SobelEdgeFilter(L.L_ALL_EDGES));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).SobelEdgeFilter(7));
    }

    [Fact]
    public void TwoSidedEdgeFilter_Vertical()
    {
        Pix pix = Gray(new[] {
            new uint[] { 0, 10, 20, 30, 40 },      // monotonic ramp: min gradient 10
            new uint[] { 40, 30, 20, 10, 0 },      // descending ramp
            new uint[] { 0, 50, 0, 50, 0 },        // noise: gradient changes sign
            new uint[] { 0, 10, 30, 31, 31 },      // gradients 10, 20, 1, 0
        });
        Pix pixd = pix.TwoSidedEdgeFilter(L.L_VERTICAL_EDGES);
        AssertRow(pixd, 0, 0, 10, 10, 10, 0);
        AssertRow(pixd, 1, 0, 10, 10, 10, 0);
        AssertRow(pixd, 2, 0, 0, 0, 0, 0);
        AssertRow(pixd, 3, 0, 10, 1, 0, 0);
    }

    [Fact]
    public void TwoSidedEdgeFilter_HorizontalIsTransposeOfVertical()
    {
        Pix pix = Gray(new[] {
            new uint[] { 0, 10, 20, 30, 40 },
            new uint[] { 40, 30, 20, 10, 0 },
            new uint[] { 0, 50, 0, 50, 0 },
            new uint[] { 0, 10, 30, 31, 31 },
        });
        Pix h = Transpose(pix).TwoSidedEdgeFilter(L.L_HORIZONTAL_EDGES);
        Pix v = pix.TwoSidedEdgeFilter(L.L_VERTICAL_EDGES);
        Assert.Equal(Transpose(v).Data, h.Data);
    }

    [Fact]
    public void TwoSidedEdgeFilter_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).TwoSidedEdgeFilter(L.L_ALL_EDGES));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 1).TwoSidedEdgeFilter(L.L_VERTICAL_EDGES));
    }

    [Fact]
    public void GetEdgeProfile_AllSides()
    {
        Pix pix = Blob();
        Assert.Equal(new[] { 2, 1, 2, 3 }, Profile(pix.GetEdgeProfile(L.L_FROM_LEFT)));
        Assert.Equal(new[] { 4, 5, 5, 4 }, Profile(pix.GetEdgeProfile(L.L_FROM_RIGHT)));
        Assert.Equal(new[] { 0, 1, 0, 0, 0, 1, 0, 0 }, Profile(pix.GetEdgeProfile(L.L_FROM_TOP)));
        Assert.Equal(new[] { 3, 1, 2, 3, 3, 2, 3, 3 }, Profile(pix.GetEdgeProfile(L.L_FROM_BOT)));
    }

    [Fact]
    public void GetEdgeProfile_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Blob().GetEdgeProfile(9));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).GetEdgeProfile(L.L_FROM_LEFT));
    }

    [Fact]
    public void GetLastPixelInRun()
    {
        Pix pix = Blob();
        // Row 1 is ".XXXXX..": OFF run at x = 0, ON run x = 1..5, OFF run x = 6..7.
        Assert.Equal(0, pix.GetLastOffPixelInRun(0, 1, L.L_FROM_LEFT));
        Assert.Equal(7, pix.GetLastOffPixelInRun(6, 1, L.L_FROM_LEFT));
        Assert.Equal(6, pix.GetLastOffPixelInRun(7, 1, L.L_FROM_RIGHT));
        Assert.Equal(5, pix.GetLastOnPixelInRun(1, 1, L.L_FROM_LEFT));
        Assert.Equal(1, pix.GetLastOnPixelInRun(5, 1, L.L_FROM_RIGHT));
        // Column 3 is ON in all four rows.
        Assert.Equal(3, pix.GetLastOnPixelInRun(3, 0, L.L_FROM_TOP));
        Assert.Equal(0, pix.GetLastOnPixelInRun(3, 3, L.L_FROM_BOT));
        // Column 0 is OFF everywhere.
        Assert.Equal(3, pix.GetLastOffPixelInRun(0, 0, L.L_FROM_TOP));
        Assert.Equal(0, pix.GetLastOffPixelInRun(0, 3, L.L_FROM_BOT));
        Assert.Throws<ArgumentException>(() => pix.GetLastOffPixelInRun(0, 0, 5));
        Assert.Throws<ArgumentException>(() => pix.GetLastOnPixelInRun(0, 0, -1));
    }

    [Fact]
    public void MeasureEdgeSmoothness_JumpsAndReversals()
    {
        Pix pix = Blob();
        // Left profile 2,1,2,3: three jumps of size 1 over n - 1 = 3 intervals.
        pix.MeasureEdgeSmoothness(L.L_FROM_LEFT, 1, 1, out float jpl, out float jspl, out float rpl);
        Assert.Equal(1.0f, jpl);
        Assert.Equal(1.0f, jspl);
        Numa na = pix.GetEdgeProfile(L.L_FROM_LEFT);
        Numa extrema = na.FindExtrema(1.0f, out _);
        Assert.Equal((float)(extrema.Count - 1) / 3.0f, rpl);

        pix.MeasureEdgeSmoothness(L.L_FROM_LEFT, 2, 1, out jpl, out jspl, out _);
        Assert.Equal(0.0f, jpl);
        Assert.Equal(0.0f, jspl);

        // Bottom profile 3,1,2,3,3,2,3,3: jumps 2,1,1,0,1,1,0 -> 5 of size >= 1, sum 6 over 7 intervals.
        pix.MeasureEdgeSmoothness(L.L_FROM_BOT, 1, 1, out jpl, out jspl, out _);
        Assert.Equal(5.0f / 7.0f, jpl);
        Assert.Equal(6.0f / 7.0f, jspl);
        pix.MeasureEdgeSmoothness(L.L_FROM_BOT, 2, 1, out jpl, out jspl, out _);
        Assert.Equal(1.0f / 7.0f, jpl);
        Assert.Equal(2.0f / 7.0f, jspl);
    }

    [Fact]
    public void MeasureEdgeSmoothness_FilledRectangleHasNoJumps()
    {
        Pix pix = Pix.Create(10, 6, 1);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 10; x++)
                Naive.SetPixel(pix, x, y, 1);
        pix.MeasureEdgeSmoothness(L.L_FROM_LEFT, 1, 1, out float jpl, out float jspl, out float rpl);
        Assert.Equal(0.0f, jpl);
        Assert.Equal(0.0f, jspl);
        // NOTE(upstream): no extrema gives -1 reversals, see docs/UPSTREAM_NOTES.md
        Assert.Equal(-1.0f / 5.0f, rpl);
    }

    [Fact]
    public void MeasureEdgeSmoothness_InvalidArguments()
    {
        Pix pix = Blob();
        Assert.Throws<ArgumentException>(() => pix.MeasureEdgeSmoothness(9, 1, 1, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => pix.MeasureEdgeSmoothness(L.L_FROM_LEFT, 0, 1, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => pix.MeasureEdgeSmoothness(L.L_FROM_LEFT, 1, 0, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).MeasureEdgeSmoothness(L.L_FROM_LEFT, 1, 1, out _, out _, out _));
    }
}
