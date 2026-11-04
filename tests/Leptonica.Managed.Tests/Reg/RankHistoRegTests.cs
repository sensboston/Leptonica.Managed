using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// rankhisto_reg.c: tests grayscale rank functions:
/// pixGetRankColorArray and numaDiscretizeHistoInBins.
/// </summary>
public class RankHistoRegTests
{
    [RegFact(Skip = "needs gnuplot output (gplotSimplePix1, gplot.c); non-gnuplot checks run in RankHistoNoGnuplot")]
    public void RankHisto()
    {
        RunRankHisto(useGnuplot: true);
    }

    [RegFact]
    public void RankHistoNoGnuplot()
    {
        RunRankHisto(useGnuplot: false);
    }

    private static void RunRankHisto(bool useGnuplot)
    {
        var rp = new RegTest("rankhisto");

            /* Find the rank bin colors */
        Pix pixs = TestPaths.ReadInput("map1.jpg");
        pixs.GetDimensions(out int w, out int h, out _);
        int factor = Math.Max(1, (int)Math.Sqrt(w * h / 20000.0));
        int nbins = 10;
        Pixa? pixa = useGnuplot ? Pixa.Create(0) : null;
        uint[]? array = pixs.GetRankColorArray(nbins, L.L_SELECT_MIN, factor, pixa, 6);
        Assert.NotNull(array);
        if (useGnuplot) {
            Pix pixTile = pixa!.DisplayTiledInColumns(3, 1.0f, 20, 0);
            rp.WritePixAndCheck(pixTile, L.IFF_PNG);  /* 0 */
        }

        Pix pix1 = Pix.DisplayColorArray(array!, nbins, 200, 5, 6);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 1 (0 without gnuplot) */

            /* Modify the rank bin colors by mapping them such
             * that the lightest color is mapped to white */
        uint[] marray = new uint[nbins];
        for (int i = 0; i < nbins; i++)
            marray[i] = Pix.PixelLinearMapToTargetColor(array![i], array[nbins - 1], 0xffffff00);
        pix1 = Pix.DisplayColorArray(marray, nbins, 200, 5, 6);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 2 (1 without gnuplot) */

            /* Map to the lightest bin; then do TRC adjustment */
        pix1 = Pix.LinearMapToTargetColor(null, pixs, array![nbins - 1], 0xffffff00);
        Pix pix2 = Pix.GammaTRC(null, pix1, 1.0f, 0, 240);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 3 (2 without gnuplot) */

            /* Now test the edge case where all the histo data is piled up
             * at one place.  We only require that the result be sensible. */
        pixa = Pixa.Create(0);
        for (int i = 0; i < 3; i++) {
            int spike = i == 0 ? 1 : (i == 1 ? 50 : 99);
            Numa na = Numa.MakeConstant(0, 100);
            na.ReplaceNumber(spike, 200.0f);
            na.DiscretizeHistoInBins(10, out Numa nabinval, out Numa narank);
            Assert.Equal(10, nabinval.Count);
            Assert.Equal(100, narank.Count);
            if (useGnuplot) {
                pixa.AddPix(Gplot.SimplePix1(na, "Histogram"), L.L_INSERT);
                pixa.AddPix(Gplot.SimplePix1(nabinval, "Gray value vs rank bin"), L.L_INSERT);
                pixa.AddPix(Gplot.SimplePix1(narank, "rank value vs. gray value"), L.L_INSERT);
            }
        }
        if (useGnuplot) {
            pix1 = pixa.DisplayTiledInColumns(3, 1.0f, 20, 0);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 4 */
        }

        Assert.Equal(useGnuplot ? 4 : 2, rp.Index);
        rp.Cleanup();
    }
}
