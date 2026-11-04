using System;
using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// boxa3_reg.c: higher-level operations that can search for anomalous-sized boxes
/// in a boxa, where the widths and heights of the boxes are expected to be similar.
/// These can be corrected by moving the appropriate sides of the outlier boxes.
/// </summary>
public class Boxa3RegTests
{
    private static readonly string[] boxafiles = {"boxap1.ba", "boxap2.ba", "boxap3.ba"};
    private static readonly float[] varp = {0.0165f, 0.0432f, 0.0716f};
    private static readonly float[] varm = {0.0088f, 0.0213f, 0.0357f};
    private static readonly int[] same = {1, -1, -1};

    [RegFact(Skip = "needs gnuplot output (gplotMakeOutputPix reads the png made by gnuplot, gplot.c); the text part (TestBoxa) passes")]
    public void Boxa3()
    {
        int i;
        var rp = new RegTest("boxa3");

        for (i = 0; i < 3; i++)
            TestBoxa(rp, i);
        for (i = 0; i < 3; i++)
            PlotBoxa(rp, i);

        Assert.Equal(44, rp.Index);
        rp.Cleanup();
    }

    private static void TestBoxa(RegTest rp, int index)
    {
        int w, h, medw, medh, isame;
        float scalefact, ratiowh, fvarp, fvarm;
        Boxa boxa1, boxa2, boxa3;
        Pix pix1;

        boxa1 = Boxa.Read(Path.Combine(TestPaths.ProgDir, boxafiles[index]));

            /* Read and display initial boxa */
        boxa1.GetExtent(out w, out h, out _);
        scalefact = 100.0f / (float)w;
        boxa2 = boxa1.Transform(0, 0, scalefact, scalefact);
        rp.WriteDataAndCheck(boxa2.WriteMem(), "ba");  /* 0, 13, 26 */
        pix1 = boxa2.DisplayTiled(null, 0, -1, 2200, 2, 1.0f, 0, 3, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);   /* 1, 14, 27 */

            /* Find the median sizes */
        boxa2.MedianDimensions(out medw, out medh, out _, out _, out _, out _,
                               out _, out _);

            /* Check for deviations from median by pairs */
        boxa2.SizeConsistency(L.L_CHECK_HEIGHT, 0.0f, 0.0f,
                              out fvarp, out fvarm, out isame);
        rp.CompareValues(varp[index], fvarp, 0.003f);  /* 2, 15, 28 */
        rp.CompareValues(varm[index], fvarm, 0.003f);  /* 3, 16, 29 */
        rp.CompareValues(same[index], isame, 0);  /* 4, 17, 30 */

            /* Reconcile widths */
        boxa3 = boxa2.ReconcileSizeByMedian(L.L_CHECK_WIDTH, 0.05f, 0.04f, 1.03f,
                                            out _, out _, out ratiowh);
        rp.WriteDataAndCheck(boxa3.WriteMem(), "ba");  /* 5, 18, 31 */
        pix1 = boxa3.DisplayTiled(null, 0, -1, 2200, 2, 1.0f, 0, 3, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);   /* 6, 19, 32 */

            /* Reconcile heights */
        boxa3 = boxa2.ReconcileSizeByMedian(L.L_CHECK_HEIGHT, 0.05f, 0.04f, 1.03f,
                                            out _, out _, out _);
        rp.WriteDataAndCheck(boxa3.WriteMem(), "ba");  /* 7, 20, 33 */
        pix1 = boxa3.DisplayTiled(null, 0, -1, 2200, 2, 1.0f, 0, 3, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);   /* 8, 21, 34 */

            /* Reconcile both widths and heights */
        boxa3 = boxa2.ReconcileSizeByMedian(L.L_CHECK_BOTH, 0.05f, 0.04f, 1.03f,
                                            out _, out _, out _);
        rp.WriteDataAndCheck(boxa3.WriteMem(), "ba");  /* 9, 22, 35 */
        pix1 = boxa3.DisplayTiled(null, 0, -1, 2200, 2, 1.0f, 0, 3, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);   /* 10, 23, 36 */
    }

    private static void PlotBoxa(RegTest rp, int index)
    {
        Boxa boxa1, boxa2;
        Pix pix1, pix2, pix3;
        Pixa pixa;

        boxa1 = Boxa.Read(Path.Combine(TestPaths.ProgDir, boxafiles[index]));

            /* Read and display initial boxa */
        boxa1.PlotSizes(null, out _, out _, out pix1);
        boxa1.PlotSides(null, out _, out _, out _, out _, out pix2);
        pixa = Pixa.Create(2);
        pixa.AddPix(pix1, L.L_INSERT);
        pixa.AddPix(pix2, L.L_INSERT);
        pix3 = pixa.DisplayTiledInColumns(2, 1.0f, 20, 2);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 39, 41, 43 */

            /* Read and display reconciled boxa */
        boxa2 = boxa1.ReconcileSizeByMedian(L.L_CHECK_BOTH, 0.05f, 0.04f, 1.03f,
                                            out _, out _, out _);
        boxa2.PlotSizes(null, out _, out _, out pix1);
        boxa2.PlotSides(null, out _, out _, out _, out _, out pix2);
        pixa = Pixa.Create(2);
        pixa.AddPix(pix1, L.L_INSERT);
        pixa.AddPix(pix2, L.L_INSERT);
        pix3 = pixa.DisplayTiledInColumns(2, 1.0f, 20, 2);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 40, 42, 44 */
    }
}
