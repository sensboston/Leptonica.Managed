using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// boxa4_reg.c: smoothing, reconciliation, and display operations on boxa and boxaa.
/// </summary>
public class Boxa4RegTests
{
    [RegFact(Skip = "needs gnuplot output (boxaPlotSides) and JPEG read/write (showboxes.pac, IFF_JFIF_JPEG); non-gnuplot/non-JPEG checks run in Boxa4NoGnuplotAndNoJpeg")]
    public void Boxa4()
    {
        var rp = new RegTest("boxa4");
        string progDir = TestPaths.ProgDir;

        Utils.LeptMkdir("lept/boxa");

            /* Input is a fairly clean boxa */
        Boxa boxa1 = Boxa.Read(Path.Combine(progDir, "boxa1.ba"));
        Boxa boxa2 = boxa1.SmoothSequenceMedian(10, L.L_USE_CAPPED_MAX, 50, 0, 0);
        int width = 100;
        boxa2.GetExtent(out int w, out int h, out _);
        float scalefact = (float)width / (float)w;
        Boxa boxa3 = boxa2.Transform(0, 0, scalefact, scalefact);
        Pix pix1 = boxa3.DisplayTiled(null, 0, -1, 1500, 2, 1.0f, 0, 3, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */

            /* Input is an unsmoothed and noisy boxa */
        boxa1 = Boxa.Read(Path.Combine(progDir, "boxa2.ba"));
        boxa2 = boxa1.SmoothSequenceMedian(10, L.L_USE_CAPPED_MAX, 50, 0, 0);
        boxa2.GetExtent(out w, out h, out _);
        scalefact = (float)width / (float)w;
        boxa3 = boxa2.Transform(0, 0, scalefact, scalefact);
        pix1 = boxa3.DisplayTiled(null, 0, -1, 1500, 2, 1.0f, 0, 3, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 1 */

            /* Input is an unsmoothed and noisy boxa (gnuplot plots) */
        boxa1 = Boxa.Read(Path.Combine(progDir, "boxa2.ba"));
        boxa2 = boxa1.SmoothSequenceMedian(10, L.L_SUB_ON_LOC_DIFF, 80, 20, 1);
        boxa3 = boxa1.SmoothSequenceMedian(10, L.L_SUB_ON_SIZE_DIFF, 80, 20, 1);
        boxa1.PlotSides("initial", out _, out _, out _, out _, out pix1);
        boxa2.PlotSides("side-smoothing", out _, out _, out _, out _, out Pix pix2);
        boxa3.PlotSides("size-smoothing", out _, out _, out _, out _, out Pix pix3);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);

            /* Reconcile all sides by median */
        boxa1 = Boxa.Read(Path.Combine(progDir, "boxa5.ba"));
        Pixa pixa1 = Pixa.Create(0);
        boxa2 = boxa1.ReconcileAllByMedian(L.L_ADJUST_LEFT_AND_RIGHT,
                                           L.L_ADJUST_TOP_AND_BOT, 50, 0, pixa1);
        rp.WriteDataAndCheck(boxa2.WriteMem(), "ba");
        pix1 = Pix.Read("/tmp/lept/boxa/recon_sides.png");
        rp.WritePixAndCheck(pix1, L.IFF_PNG);

            /* Reconcile top/bot sides by median */
        pixa1 = Pixa.Create(0);
        boxa2 = boxa1.ReconcileAllByMedian(L.L_ADJUST_SKIP,
                                           L.L_ADJUST_TOP_AND_BOT, 50, 0, pixa1);
        rp.WriteDataAndCheck(boxa2.WriteMem(), "ba");
        pix1 = Pix.Read("/tmp/lept/boxa/recon_sides.png");
        rp.WritePixAndCheck(pix1, L.IFF_PNG);

            /* Split even/odd and reconcile all sides by median */
        boxa1 = Boxa.Read(Path.Combine(progDir, "boxa5.ba"));
        pixa1 = Pixa.Create(0);
        boxa1.SplitEvenOdd(0, out Boxa boxa1e, out Boxa boxa1o);
        Boxa boxa2e = boxa1e.ReconcileSidesByMedian(L.L_ADJUST_TOP_AND_BOT, 50, 0, pixa1);
        Boxa boxa3e = boxa2e.ReconcileSidesByMedian(L.L_ADJUST_LEFT_AND_RIGHT, 50, 0, pixa1);
        Boxa boxa2o = boxa1o.ReconcileSidesByMedian(L.L_ADJUST_TOP_AND_BOT, 50, 0, pixa1);
        Boxa boxa3o = boxa2o.ReconcileSidesByMedian(L.L_ADJUST_LEFT_AND_RIGHT, 50, 0, pixa1);
        boxa3 = boxa3e.MergeEvenOdd(boxa3o, 0);
        rp.WriteDataAndCheck(boxa3.WriteMem(), "ba");

            /* Input is a boxa smoothed with a median window filter */
        boxa1 = Boxa.Read(Path.Combine(progDir, "boxa3.ba"));
        boxa2 = boxa1.SmoothSequenceMedian(10, L.L_USE_CAPPED_MIN, 20, 0, 1);
        boxa2.GetExtent(out w, out h, out _);
        scalefact = (float)width / (float)w;
        boxa3 = boxa2.Transform(0, 0, scalefact, scalefact);
        pix1 = boxa3.DisplayTiled(null, 0, -1, 1500, 2, 1.0f, 0, 3, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);

            /* ----------- Test pixaDisplayBoxaa() ------------ */
        pixa1 = Pixa.ReadBoth(Path.Combine(progDir, "showboxes.pac"));
        Boxaa baa1 = Boxaa.Read(Path.Combine(progDir, "showboxes1.baa"));
        Boxaa baa2 = baa1.Transpose();
        Boxaa baa3 = baa2.Transpose();
        int nb = baa1.Count;
        int success = 1;
        for (int i = 0; i < nb; i++) {
            boxa1 = baa1.GetBoxa(i, L.L_CLONE);
            boxa2 = baa3.GetBoxa(i, L.L_CLONE);
            if (!boxa1.Equal(boxa2, 0, out _))
                success = 0;
        }

        rp.CompareValues(1, success, 0.0f);
        Pixa pixa2 = pixa1.DisplayBoxaa(baa2, L.L_DRAW_RGB, 2);
        pix1 = pixa2.DisplayTiledInRows(32, 1400, 1.0f, 0, 10, 0);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);
        PdfIo.SetDateAndVersion(0);
        pixa2.ConvertToPdf(75, 0.6f, 0, 0, null, "/tmp/lept/boxa/show.pdf");
        rp.CheckFile("/tmp/lept/boxa/show.pdf");

        rp.Cleanup();
    }

    /// <summary>
    /// Runs all non-gnuplot, non-JPEG checks of boxa4_reg against golden indices 0..6
    /// (since the C goldens were generated without gnuplot, indices 0..6 correspond to
    /// the 2 tiled PNGs, 3 reconciled .ba files, 1 smoothed tiled PNG, and boxaaTranspose).
    /// </summary>
    [RegFact]
    public void Boxa4NoGnuplotAndNoJpeg()
    {
        var rp = new RegTest("boxa4");
        string progDir = TestPaths.ProgDir;
        const int width = 100;

            /* Input is a fairly clean boxa (golden 00.png) */
        Boxa boxa1 = Boxa.Read(Path.Combine(progDir, "boxa1.ba"));
        Boxa boxa2 = boxa1.SmoothSequenceMedian(10, L.L_USE_CAPPED_MAX, 50, 0, 0);
        boxa2.GetExtent(out int w, out _, out _);
        float scalefact = (float)width / (float)w;
        Boxa boxa3 = boxa2.Transform(0, 0, scalefact, scalefact);
        Pix pix1 = boxa3.DisplayTiled(null, 0, -1, 1500, 2, 1.0f, 0, 3, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */

            /* Input is an unsmoothed and noisy boxa (golden 01.png) */
        boxa1 = Boxa.Read(Path.Combine(progDir, "boxa2.ba"));
        boxa2 = boxa1.SmoothSequenceMedian(10, L.L_USE_CAPPED_MAX, 50, 0, 0);
        boxa2.GetExtent(out w, out _, out _);
        scalefact = (float)width / (float)w;
        boxa3 = boxa2.Transform(0, 0, scalefact, scalefact);
        pix1 = boxa3.DisplayTiled(null, 0, -1, 1500, 2, 1.0f, 0, 3, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 1 */

            /* Exercise L_SUB_ON_LOC_DIFF and L_SUB_ON_SIZE_DIFF without gnuplot */
        boxa2 = boxa1.SmoothSequenceMedian(10, L.L_SUB_ON_LOC_DIFF, 80, 20, 0);
        boxa3 = boxa1.SmoothSequenceMedian(10, L.L_SUB_ON_SIZE_DIFF, 80, 20, 0);
        Assert.Equal(boxa1.Count, boxa2.Count);
        Assert.Equal(boxa1.Count, boxa3.Count);

            /* Reconcile all sides by median (golden 02.ba) */
        boxa1 = Boxa.Read(Path.Combine(progDir, "boxa5.ba"));
        boxa2 = boxa1.ReconcileAllByMedian(L.L_ADJUST_LEFT_AND_RIGHT,
                                           L.L_ADJUST_TOP_AND_BOT, 50, 0, null);
        rp.WriteDataAndCheck(boxa2.WriteMem(), "ba");  /* 2 */

            /* Reconcile top/bot sides by median (golden 03.ba) */
        boxa2 = boxa1.ReconcileAllByMedian(L.L_ADJUST_SKIP,
                                           L.L_ADJUST_TOP_AND_BOT, 50, 0, null);
        rp.WriteDataAndCheck(boxa2.WriteMem(), "ba");  /* 3 */

            /* Split even/odd and reconcile all sides by median (golden 04.ba) */
        boxa1 = Boxa.Read(Path.Combine(progDir, "boxa5.ba"));
        boxa1.SplitEvenOdd(0, out Boxa boxa1e, out Boxa boxa1o);
        Boxa boxa2e = boxa1e.ReconcileSidesByMedian(L.L_ADJUST_TOP_AND_BOT, 50, 0, null);
        Boxa boxa3e = boxa2e.ReconcileSidesByMedian(L.L_ADJUST_LEFT_AND_RIGHT, 50, 0, null);
        Boxa boxa2o = boxa1o.ReconcileSidesByMedian(L.L_ADJUST_TOP_AND_BOT, 50, 0, null);
        Boxa boxa3o = boxa2o.ReconcileSidesByMedian(L.L_ADJUST_LEFT_AND_RIGHT, 50, 0, null);
        boxa3 = boxa3e.MergeEvenOdd(boxa3o, 0);
        rp.WriteDataAndCheck(boxa3.WriteMem(), "ba");  /* 4 */

            /* Input is a boxa smoothed with a median window filter (golden 05.png) */
        boxa1 = Boxa.Read(Path.Combine(progDir, "boxa3.ba"));
        boxa2 = boxa1.SmoothSequenceMedian(10, L.L_USE_CAPPED_MIN, 20, 0, 0);
        boxa2.GetExtent(out w, out _, out _);
        scalefact = (float)width / (float)w;
        boxa3 = boxa2.Transform(0, 0, scalefact, scalefact);
        pix1 = boxa3.DisplayTiled(null, 0, -1, 1500, 2, 1.0f, 0, 3, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 5 */

            /* Check that boxaaTranspose is reversible (check 6) */
        Boxaa baa1 = Boxaa.Read(Path.Combine(progDir, "showboxes1.baa"));
        Boxaa baa2 = baa1.Transpose();
        Boxaa baa3 = baa2.Transpose();
        int nb = baa1.Count;
        int success = 1;
        for (int i = 0; i < nb; i++) {
            boxa1 = baa1.GetBoxa(i, L.L_CLONE);
            boxa2 = baa3.GetBoxa(i, L.L_CLONE);
            if (!boxa1.Equal(boxa2, 0, out _))
                success = 0;
        }
        rp.CompareValues(1, success, 0.0f);  /* 6 */

        Assert.Equal(6, rp.Index);
        rp.Cleanup();
    }
}
