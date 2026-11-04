using System;
using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// rankbin_reg.c: tests rank bin functions:
/// numaDiscretizeSortedInBins, numaDiscretizeHistoInBins, numaGetRankBinValues, pixRankBinByStrip.
/// </summary>
public class RankBinRegTests
{
    [RegFact(Skip = "needs gnuplot output (gplotSimplePix1, gplot.c); non-gnuplot checks run in RankBinNoGnuplot")]
    public void RankBin()
    {
        RunRankBin(useGnuplot: true);
    }

    [RegFact]
    public void RankBinNoGnuplot()
    {
        RunRankBin(useGnuplot: false);
    }

    private static void RunRankBin(bool useGnuplot)
    {
        var rp = new RegTest("rankbin");

            /* Generate arrays of word widths and heights */
        Pix pixs = TestPaths.ReadInput("feyn.tif");
        Pix pix1 = pixs.ReduceRankBinaryCascade(1, 0, 0, 0);
        pix1.GetWordBoxesInTextlines(6, 6, 500, 50, out Boxa boxa1, out _);
        int n = boxa1.Count;
        Numa na1 = Numa.Create(0);
        Numa na2 = Numa.Create(0);
        for (int i = 0; i < n; i++) {
            boxa1.GetBoxGeometry(i, out _, out _, out int w, out int h);
            na1.AddNumber(w);
            na2.AddNumber(h);
        }

            /* Make the rank bin arrays of average values, with 10 and 30 bins */
        Numa na3 = na1.GetRankBinValues(10);
        Numa na4 = na2.GetRankBinValues(10);
        Assert.Equal(10, na3.Count);
        Assert.Equal(10, na4.Count);
        if (useGnuplot) {
            pix1 = Gplot.SimplePix1(na3, "width vs rank bins (10)");
            Pix pix2Plot = Gplot.SimplePix1(na4, "height vs rank bins (10)");
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */
            rp.WritePixAndCheck(pix2Plot, L.IFF_PNG);  /* 1 */
        }

        na3 = na1.GetRankBinValues(30);
        na4 = na2.GetRankBinValues(30);
        Assert.Equal(30, na3.Count);
        Assert.Equal(30, na4.Count);
        if (useGnuplot) {
            pix1 = Gplot.SimplePix1(na3, "width vs rank bins (30)");
            Pix pix2Plot = Gplot.SimplePix1(na4, "height vs rank bins (30)");
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 2 */
            rp.WritePixAndCheck(pix2Plot, L.IFF_PNG);  /* 3 */
        }

            /* Test pixRankBinByStrip */
        pix1 = TestPaths.ReadInput("pancrazi.15.jpg");
        Pixa pixa = Pixa.Create(3);
        Pix pix2 = pix1.RankBinByStrip(L.L_SCAN_HORIZONTAL, 16, 10, L.L_SELECT_HUE);
        Pix pix3 = pix2.ExpandReplicate(20);
        pixa.AddPix(pix3, L.L_INSERT);
        pix2 = pix1.RankBinByStrip(L.L_SCAN_HORIZONTAL, 16, 10, L.L_SELECT_SATURATION);
        pix3 = pix2.ExpandReplicate(20);
        pixa.AddPix(pix3, L.L_INSERT);
        pix2 = pix1.RankBinByStrip(L.L_SCAN_HORIZONTAL, 16, 10, L.L_SELECT_RED);
        pix3 = pix2.ExpandReplicate(20);
        pixa.AddPix(pix3, L.L_INSERT);
        pix2 = pixa.DisplayTiledInRows(32, 800, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 4 (0 without gnuplot) */

            /* Test numaGetRankBinValues() and numaDiscretize functions */
        boxa1 = Boxa.Read(Path.Combine(TestPaths.ProgDir, "boxa4.ba"));
        boxa1.SplitEvenOdd(0, out Boxa boxa2, out _);
        boxa2.GetSizes(out na1, out _);
        rp.WriteDataAndCheck(na1.WriteMem(), "na");  /* 5 (1) */
        n = na1.Count;
        int nbins = Math.Max(5, n / 50);
        na2 = na1.GetRankBinValues(nbins);
        rp.WriteDataAndCheck(na2.WriteMem(), "na");  /* 6 (2) */

        na3 = Numa.Sort(null, na1, L.L_SORT_INCREASING);
        na4 = na3.DiscretizeSortedInBins(nbins);
        rp.WriteDataAndCheck(na4.WriteMem(), "na");  /* 7 (3) */

        na3 = na1.MakeHistogram(100000, out _);
        na3.DiscretizeHistoInBins(nbins, out na4, out _);
        rp.WriteDataAndCheck(na4.WriteMem(), "na");  /* 8 (4) */
        if (useGnuplot) {
            rp.CompareFiles(6, 7);  /* 9 */
            rp.CompareFiles(6, 8);  /* 10 */
        } else {
            rp.CompareFiles(2, 3);  /* 5 */
            rp.CompareFiles(2, 4);  /* 6 */
        }

        pix1 = TestPaths.ReadInput("karen8.jpg");
        na1 = pix1.GetGrayHistogram(1);
        na1.DiscretizeHistoInBins(1000, out na2, out na3);
        Assert.Equal(1000, na2.Count);
        if (useGnuplot) {
            pix2 = Gplot.SimplePix1(na3, "rank vs gray");
            pix3 = Gplot.SimplePix1(na2, "gray vs rank-binval");
            rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 11 */
            rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 12 */
        }

        pix1 = TestPaths.ReadInput("wyom.jpg");
        uint[] carray = pix1.GetRankColorArray(20, L.L_SELECT_RED, 5, null, 0)!;
        pix2 = Pix.DisplayColorArray(carray, 20, 200, 5, 6);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 13 (7) */

        Assert.Equal(useGnuplot ? 13 : 7, rp.Index);
        rp.Cleanup();
    }
}
