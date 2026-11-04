using System;
using Leptonica;

namespace Leptonica.Managed.Tests;

/// <summary>Tests of selgen.c on small synthetic patterns.</summary>
public class SelGenTests
{
    private static Pix Block(int w, int h, int x0, int y0, int bw, int bh)
    {
        Pix pix = Pix.Create(w, h, 1);
        for (int y = y0; y < y0 + bh; y++)
            for (int x = x0; x < x0 + bw; x++)
                pix.SetPixel(x, y, 1);
        return pix;
    }

    private static int Count(Sel sel, int type)
    {
        int n = 0;
        for (int i = 0; i < sel.Sy; i++)
            for (int j = 0; j < sel.Sx; j++)
                if (sel.GetElement(i, j) == type)
                    n++;
        return n;
    }

    [Fact]
    public void GetRunsOnLine_ListsBgAndFgRuns()
    {
        Pix pix = Pix.Create(12, 3, 1);
        for (int x = 2; x < 5; x++) pix.SetPixel(x, 1, 1);
        for (int x = 9; x < 11; x++) pix.SetPixel(x, 1, 1);
        Numa na = pix.GetRunsOnLine(0, 1, 11, 1);
        Assert.Equal(new float[] { 2, 3, 4, 2, 1 }, new[] { na.GetFValue(0), na.GetFValue(1), na.GetFValue(2),
                                                            na.GetFValue(3), na.GetFValue(4) });
        Assert.Equal(5, na.Count);

        pix.SetPixel(0, 1, 1);     /* first pixel black: a white run of size 0 comes first */
        na = pix.GetRunsOnLine(0, 1, 11, 1);
        Assert.Equal(0, na.GetIValue(0));
        Assert.Equal(1, na.GetIValue(1));

        Assert.Throws<ArgumentException>(() => pix.GetRunsOnLine(0, 1, 12, 1));
        Assert.Throws<ArgumentException>(() => pix.GetRunsOnLine(0, -1, 5, 1));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).GetRunsOnLine(0, 0, 1, 1));
    }

    [Fact]
    public void GetRunCentersOnLine_ReturnsCentersOfLongFgRuns()
    {
        Pix pix = Pix.Create(20, 6, 1);
        for (int x = 2; x < 5; x++) pix.SetPixel(x, 3, 1);          /* length 3, center 3 */
        for (int x = 10; x < 18; x++) pix.SetPixel(x, 3, 1);        /* length 8, center 14 */
        pix.SetPixel(7, 3, 1);                                      /* length 1 */
        Numa na = pix.GetRunCentersOnLine(-1, 3, 3);
        Assert.Equal(2, na.Count);
        Assert.Equal(3, na.GetIValue(0));
        Assert.Equal(14, na.GetIValue(1));
        Assert.Equal(3, pix.GetRunCentersOnLine(-1, 3, 1).Count);
        Assert.Equal(0, pix.GetRunCentersOnLine(-1, 0, 1).Count);
        Assert.Equal(0, pix.GetRunCentersOnLine(-1, 10, 1).Count);   /* outside: empty */

        Numa nv = pix.GetRunCentersOnLine(3, -1, 1);                 /* column 3: one pixel at y = 3 */
        Assert.Equal(1, nv.Count);
        Assert.Equal(3, nv.GetIValue(0));
        Assert.Throws<ArgumentException>(() => pix.GetRunCentersOnLine(1, 1, 1));
        Assert.Throws<ArgumentException>(() => pix.GetRunCentersOnLine(-1, -1, 1));
    }

    [Fact]
    public void AdjacentOnPixelInRaster_SearchesFourConnectedFirst()
    {
        Pix pix = Pix.Create(5, 5, 1);
        pix.SetPixel(3, 3, 1);        /* diagonal neighbor of (2, 2) */
        pix.SetPixel(2, 3, 1);        /* below (2, 2): 4-connected */
        Assert.True(pix.AdjacentOnPixelInRaster(2, 2, out int xa, out int ya));
        Assert.Equal((2, 3), (xa, ya));
        pix.SetPixel(2, 3, 0);
        Assert.True(pix.AdjacentOnPixelInRaster(2, 2, out xa, out ya));
        Assert.Equal((3, 3), (xa, ya));
        Assert.False(pix.AdjacentOnPixelInRaster(0, 0, out _, out _));
        Assert.True(pix.AdjacentOnPixelInRaster(4, 4, out xa, out ya));     /* edge: skips outside neighbors */
        Assert.Equal((3, 3), (xa, ya));
    }

    [Fact]
    public void SubsampleBoundaryPixels_TakesEveryNthPixelAlongTheContour()
    {
        Pix ring = Block(12, 12, 2, 2, 8, 8).ExtractBoundary(1);
        int total = ring.CountPixels(null);
        Assert.Equal(28, total);
        Assert.Equal(total, ring.SubsampleBoundaryPixels(0).Count);
        Pta every2 = ring.SubsampleBoundaryPixels(1);
        Assert.Equal(1 + (total - 1) / 2, every2.Count);
        Pta every4 = ring.SubsampleBoundaryPixels(3);
        Assert.Equal(1 + (total - 1) / 4, every4.Count);
        every4.GetIPt(0, out int x, out int y);
        Assert.Equal((2, 2), (x, y));        /* first pixel in raster order */
        Assert.Throws<ArgumentException>(() => ring.SubsampleBoundaryPixels(-1));
    }

    [Fact]
    public void GenerateSelBoundary_HitsOnTheInnerRingAndMissesOutside()
    {
        Pix pix = Block(20, 20, 6, 6, 7, 7);
        Sel sel = pix.GenerateSelBoundary(1, 1, 0, 0, 1, 1, 1, 1, out Pix? pixe);
        Assert.Equal(11, sel.Sx);
        Assert.Equal(11, sel.Sy);
        Assert.Equal(16, Count(sel, L.SEL_HIT));       /* ring of the 5x5 block eroded once */
        Assert.Equal(40, Count(sel, L.SEL_MISS));      /* ring of width 1 around the block grown once */
        Assert.NotNull(pixe);
        Assert.Equal(11, pixe!.Width);
        Assert.Equal(49, pixe.CountPixels(null));
        Assert.Equal(L.SEL_HIT, sel.GetElement(3, 3));
        Assert.Equal(L.SEL_DONT_CARE, sel.GetElement(5, 5));
        Assert.Equal(L.SEL_MISS, sel.GetElement(0, 0));

        /* hits only, no extra pixels */
        Sel hits = pix.GenerateSelBoundary(1, 1, 0, -1, 0, 0, 0, 0, out pixe);
        Assert.Equal(7, hits.Sx);
        Assert.Equal(16, Count(hits, L.SEL_HIT));
        Assert.Equal(0, Count(hits, L.SEL_MISS));

        /* the sel with skipping has fewer elements */
        Sel sparse = pix.GenerateSelBoundary(1, 1, 3, 3, 1, 1, 1, 1, out _);
        Assert.InRange(Count(sparse, L.SEL_HIT), 1, 8);
        Assert.InRange(Count(sparse, L.SEL_MISS), 1, 20);

        Assert.Throws<ArgumentException>(() => pix.GenerateSelBoundary(1, 1, -1, -1, 0, 0, 0, 0, out _));
        Assert.Throws<ArgumentException>(() => pix.GenerateSelBoundary(5, 1, 0, 0, 0, 0, 0, 0, out _));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).GenerateSelBoundary(1, 1, 0, 0, 0, 0, 0, 0, out _));
    }

    [Fact]
    public void GenerateSelWithRuns_PutsElementsAtRunCenters()
    {
        Pix pix = Block(30, 30, 8, 8, 13, 13);
        Sel sel = pix.GenerateSelWithRuns(3, 3, 1, 3, 5, 5, 5, 5, out Pix? pixe);
        Assert.Equal(23, sel.Sx);
        Assert.Equal(23, sel.Sy);
        Assert.Equal(23, pixe!.Width);
        Assert.True(Count(sel, L.SEL_HIT) > 0);
        Assert.True(Count(sel, L.SEL_MISS) > 0);
        /* every hit is a fg pixel of the expanded image */
        for (int i = 0; i < sel.Sy; i++)
            for (int j = 0; j < sel.Sx; j++) {
                pixe.GetPixel(j, i, out uint v);
                if (sel.GetElement(i, j) == L.SEL_HIT) Assert.Equal(1u, v);
                if (sel.GetElement(i, j) == L.SEL_MISS) Assert.Equal(0u, v);
            }
        Assert.Throws<ArgumentException>(() => pix.GenerateSelWithRuns(0, 0, 1, 3, 0, 0, 0, 0, out _));
    }

    [Fact]
    public void GenerateSelRandom_IsDeterministicAndRespectsTheFractions()
    {
        Pix pix = Block(30, 30, 8, 8, 13, 13);
        CRand.Seed(1);
        Sel a = pix.GenerateSelRandom(0.5f, 0.2f, 1, 3, 3, 3, 3, out Pix? pixe);
        CRand.Seed(1);
        Sel b = pix.GenerateSelRandom(0.5f, 0.2f, 1, 3, 3, 3, 3, out _);
        Assert.Equal(Count(a, L.SEL_HIT), Count(b, L.SEL_HIT));
        Assert.Equal(Count(a, L.SEL_MISS), Count(b, L.SEL_MISS));
        Assert.InRange(Count(a, L.SEL_HIT), 20, 60);       /* 11 x 11 = 121 allowable pixels, half chosen */
        Assert.True(Count(a, L.SEL_MISS) > 0);
        Assert.Equal(19, pixe!.Width);

        CRand.Seed(1);
        Sel all = pix.GenerateSelRandom(1.0f, 0.0f, 1, 0, 0, 0, 0, out _);
        Assert.Equal(121, Count(all, L.SEL_HIT));
        Assert.Equal(0, Count(all, L.SEL_MISS));
        Assert.Throws<ArgumentException>(() => pix.GenerateSelRandom(0.0f, 0.0f, 1, 0, 0, 0, 0, out _));
        Assert.Throws<ArgumentException>(() => pix.GenerateSelRandom(1.5f, 0.0f, 1, 0, 0, 0, 0, out _));
    }

    [Fact]
    public void DisplayHitMissSel_ScalesAndColors()
    {
        Pix pix = Block(8, 8, 1, 1, 6, 6);
        Sel sel = Sel.CreateBrick(8, 8, 4, 4, L.SEL_DONT_CARE);
        sel.SetElement(2, 3, L.SEL_HIT);
        sel.SetElement(6, 6, L.SEL_MISS);
        Pix pixd = pix.DisplayHitMissSel(sel, 4, 0xff000000, 0x00ff0000);
        Assert.Equal(32, pixd.Width);
        Assert.Equal(8, pixd.Depth);
        Assert.Equal(4, pixd.Colormap!.Count);
        pixd.GetPixel(3 * 4, 2 * 4, out uint hit);
        pixd.GetPixel(6 * 4, 6 * 4, out uint miss);
        pixd.GetPixel(2 * 4, 2 * 4, out uint fg);
        pixd.GetPixel(0, 0, out uint bg);
        Assert.Equal((2u, 3u, 1u, 0u), (hit, miss, fg, bg));
        Assert.Equal(7 * 8, Pix.Create(8, 8, 1).DisplayHitMissSel(sel, 0, 0, 0).Width);
        Assert.Equal(31 * 8, Pix.Create(8, 8, 1).DisplayHitMissSel(sel, 99, 0, 0).Width);
    }
}
