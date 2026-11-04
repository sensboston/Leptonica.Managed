using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// compfilter_reg.c: regression test for filters that select connected components
/// based on size, using logical combinations of indicator arrays.
/// </summary>
public class CompFilterRegTests
{
    private static readonly float[] edges = { 0.0f, 0.2f, 0.3f, 0.35f, 0.4f, 0.45f, 0.5f,
                                              0.55f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f };

        /* for feyn.tif */
    private static readonly int[] band = { 1, 11, 48, 264, 574, 704, 908, 786, 466,
                                           157, 156, 230 };
    private static readonly int[] total = { 1, 12, 60, 324, 898, 1602, 2510, 3296,
                                            3762, 3919, 4075, 4305 };

    [RegFact]
    public void CompFilter()
    {
        int w, h, n, i, sum, sumi;
        bool empty;
        Box box1, box2, box3, box4;
        Boxa boxa1, boxa2;
        Numa na1, na2, na3, na4, na5;
        Numa na2i, na3i, na4i, nat, naw, nah;
        Pix pixs, pix1, pix2, pix3, pix4;
        Pixa pixa1, pixa2, pixa3;
        var rp = new RegTest("compfilter");

            /* Draw 4 filled boxes of different sizes */
        pixs = Pix.Create(200, 200, 1);
        box1 = Box.Create(10, 10, 20, 30);
        box2 = Box.Create(50, 10, 40, 20);
        box3 = Box.Create(110, 10, 35, 5);
        box4 = Box.Create(160, 10, 5, 15);
        boxa1 = Boxa.Create(4);
        boxa1.AddBox(box1, L.L_INSERT);
        boxa1.AddBox(box2, L.L_INSERT);
        boxa1.AddBox(box3, L.L_INSERT);
        boxa1.AddBox(box4, L.L_INSERT);
        pixs.RenderBox(box1, 1, L.L_SET_PIXELS);
        pixs.RenderBox(box2, 1, L.L_SET_PIXELS);
        pixs.RenderBox(box3, 1, L.L_SET_PIXELS);
        pixs.RenderBox(box4, 1, L.L_SET_PIXELS);
        pix1 = pixs.FillClosedBorders(4);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */
        pix2 = Pix.CreateTemplate(pixs);
        pix2.RenderHashBox(box1, 6, 4, L.L_POS_SLOPE_LINE, 1, L.L_SET_PIXELS);
        pix2.RenderHashBox(box2, 7, 2, L.L_POS_SLOPE_LINE, 1, L.L_SET_PIXELS);
        pix2.RenderHashBox(box3, 4, 2, L.L_VERTICAL_LINE, 1, L.L_SET_PIXELS);
        pix2.RenderHashBox(box4, 3, 1, L.L_HORIZONTAL_LINE, 1, L.L_SET_PIXELS);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 1 */

            /* Exercise the parameters:  Reg indices 2-27  */
        pix3 = pix1.SelectBySize(0, 22, 8, L.L_SELECT_HEIGHT, L.L_SELECT_IF_GT, out _);
        CountPieces(rp, pix3, 1);
        pix3 = pix1.SelectBySize(0, 30, 8, L.L_SELECT_HEIGHT, L.L_SELECT_IF_LT, out _);
        CountPieces(rp, pix3, 3);
        pix3 = pix1.SelectBySize(0, 5, 8, L.L_SELECT_HEIGHT, L.L_SELECT_IF_GT, out _);
        CountPieces(rp, pix3, 3);
        pix3 = pix1.SelectBySize(0, 6, 8, L.L_SELECT_HEIGHT, L.L_SELECT_IF_LT, out _);
        CountPieces(rp, pix3, 1);
        pix3 = pix1.SelectBySize(20, 0, 8, L.L_SELECT_WIDTH, L.L_SELECT_IF_GT, out _);
        CountPieces(rp, pix3, 2);
        pix3 = pix1.SelectBySize(31, 0, 8, L.L_SELECT_WIDTH, L.L_SELECT_IF_LT, out _);
        CountPieces(rp, pix3, 2);
        pix3 = pix1.SelectBySize(21, 10, 8, L.L_SELECT_IF_EITHER, L.L_SELECT_IF_LT, out _);
        CountPieces(rp, pix3, 3);
        pix3 = pix1.SelectBySize(20, 30, 8, L.L_SELECT_IF_EITHER, L.L_SELECT_IF_GT, out _);
        CountPieces(rp, pix3, 2);
        pix3 = pix1.SelectBySize(22, 32, 8, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_LT, out _);
        CountPieces(rp, pix3, 2);
        pix3 = pix1.SelectBySize(6, 32, 8, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_LT, out _);
        CountPieces(rp, pix3, 1);
        pix3 = pix1.SelectBySize(5, 25, 8, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_GT, out _);
        CountPieces(rp, pix3, 1);
        pix3 = pix1.SelectBySize(25, 5, 8, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_GT, out _);
        CountPieces(rp, pix3, 1);

        pix3 = pix1.SelectByPerimToAreaRatio(0.3f, 8, L.L_SELECT_IF_GT, out _);
        CountPieces(rp, pix3, 2);
        pix3 = pix1.SelectByPerimToAreaRatio(0.15f, 8, L.L_SELECT_IF_GT, out _);
        CountPieces(rp, pix3, 3);
        pix3 = pix1.SelectByPerimToAreaRatio(0.4f, 8, L.L_SELECT_IF_LTE, out _);
        CountPieces(rp, pix3, 2);
        pix3 = pix1.SelectByPerimToAreaRatio(0.45f, 8, L.L_SELECT_IF_LT, out _);
        CountPieces(rp, pix3, 3);

        pix3 = pix2.SelectByPerimSizeRatio(2.3f, 8, L.L_SELECT_IF_GT, out _);
        CountPieces(rp, pix3, 2);
        pix3 = pix2.SelectByPerimSizeRatio(1.2f, 8, L.L_SELECT_IF_GT, out _);
        CountPieces(rp, pix3, 3);
        pix3 = pix2.SelectByPerimSizeRatio(1.7f, 8, L.L_SELECT_IF_LTE, out _);
        CountPieces(rp, pix3, 1);
        pix3 = pix2.SelectByPerimSizeRatio(2.9f, 8, L.L_SELECT_IF_LT, out _);
        CountPieces(rp, pix3, 3);

        pix3 = pix2.SelectByAreaFraction(0.3f, 8, L.L_SELECT_IF_LT, out _);
        CountPieces(rp, pix3, 0);
        pix3 = pix2.SelectByAreaFraction(0.9f, 8, L.L_SELECT_IF_LT, out _);
        CountPieces(rp, pix3, 4);
        pix3 = pix2.SelectByAreaFraction(0.5f, 8, L.L_SELECT_IF_GTE, out _);
        CountPieces(rp, pix3, 3);
        pix3 = pix2.SelectByAreaFraction(0.7f, 8, L.L_SELECT_IF_GT, out _);
        CountPieces(rp, pix3, 2);

        boxa2 = boxa1.SelectBySize(21, 10, L.L_SELECT_IF_EITHER, L.L_SELECT_IF_LT, out _);
        CountPieces2(rp, boxa2, 3);
        boxa2 = boxa1.SelectBySize(22, 32, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_LT, out _);
        CountPieces2(rp, boxa2, 2);

            /* Here's the most general method for selecting components.   *
             * We do it for area fraction, but any combination of         *
             * size, area/perimeter ratio and area fraction can be used.  *
             * Reg indices 28-85                                          */
        pixs = TestPaths.ReadInput("feyn.tif");
        pix1 = Pix.Copy(null, pixs);  /* subtract bands from this */
        pix2 = Pix.CreateTemplate(pixs);  /* add bands to this */
        pixs.GetDimensions(out w, out h, out _);
        boxa1 = pixs.ConnComp(out pixa1, 8);
        n = boxa1.Count;
        na1 = pixa1.FindAreaFraction();
        nat = Numa.Create(0);
        nat.Count = n;  /* initialize to all 0 */
        sum = sumi = 0;
        pixa3 = Pixa.Create(0);
        for (i = 0; i < 12; i++) {
                /* Compute within the intervals using an intersection. */
            na2 = na1.MakeThresholdIndicator(edges[i], L.L_SELECT_IF_GTE);
            if (i != 11)
                na3 = na1.MakeThresholdIndicator(edges[i + 1], L.L_SELECT_IF_LT);
            else
                na3 = na1.MakeThresholdIndicator(edges[i + 1], L.L_SELECT_IF_LTE);
            na4 = Numa.LogicalOp(null, na2, na3, L.L_INTERSECTION);
            sum += CountOnes(rp, na4, 0, 0, null);

                /* Compute outside the intervals using a union, and invert */
            na2i = na1.MakeThresholdIndicator(edges[i], L.L_SELECT_IF_LT);
            if (i != 11)
                na3i = na1.MakeThresholdIndicator(edges[i + 1], L.L_SELECT_IF_GTE);
            else
                na3i = na1.MakeThresholdIndicator(edges[i + 1], L.L_SELECT_IF_GT);
            na4i = Numa.LogicalOp(null, na3i, na2i, L.L_UNION);
            Numa.Invert(na4i, na4i);
            sumi += CountOnes(rp, na4i, 0, 0, null);

                /* Reconstruct the image, band by band. */
            Numa.LogicalOp(nat, nat, na4, L.L_UNION);
            pixa2 = pixa1.SelectWithIndicator(na4, out _);
            pix3 = pixa2.Display(w, h);
            Pix.Or(pix2, pix2, pix3);  /* add them in */
            pix4 = Pix.Copy(null, pix2);  /* destroyed by Count_pieces */
            CountOnes(rp, na4, band[i], i, "band");
            CountPieces(rp, pix3, band[i]);
            CountOnes(rp, nat, total[i], i, "total");
            CountPieces(rp, pix4, total[i]);

                /* Remove band successively from full image */
            pix1.RemoveWithIndicator(pixa1, na4);
            pixa3.AddPix(pix1, L.L_COPY);
        }

            /* Did we remove all components from pix1? */
        empty = pix1.Zero();
        rp.CompareValues(1, empty ? 1 : 0, 0.0f);

            /* One last extraction.  Get all components that have either
             * a height of at least 50 or a width of between 30 and 35,
             * and also have a relatively large perimeter/area ratio. */
        pixs = TestPaths.ReadInput("feyn.tif");
        boxa1 = pixs.ConnComp(out pixa1, 8);
        n = boxa1.Count;
        pixa1.FindDimensions(out naw, out nah);
        na1 = pixa1.FindPerimToAreaRatio();
        na2 = nah.MakeThresholdIndicator(50, L.L_SELECT_IF_GTE);
        na3 = naw.MakeThresholdIndicator(30, L.L_SELECT_IF_GTE);
        na4 = naw.MakeThresholdIndicator(35, L.L_SELECT_IF_LTE);
        na5 = na1.MakeThresholdIndicator(0.4f, L.L_SELECT_IF_GTE);
        Numa.LogicalOp(na3, na3, na4, L.L_INTERSECTION);
        Numa.LogicalOp(na2, na2, na3, L.L_UNION);
        Numa.LogicalOp(na2, na2, na5, L.L_INTERSECTION);
        Numa.Invert(na2, na2);  /* get components to be removed */
        pixs.RemoveWithIndicator(pixa1, na2);
        rp.WritePixAndCheck(pixs, L.IFF_PNG);  /* 86 */
        pixa3.AddPix(pixs, L.L_INSERT);

        Assert.Equal(86, rp.Index);
        rp.Cleanup();
    }

    /* ---------------------  Helpers -------------------------- */
    private static void CountPieces(RegTest rp, Pix pix, int nexp)
    {
        int n;
        Boxa boxa;

        if (rp.Index > 28 && rp.Index < 55)
            rp.WritePixAndCheck(pix, L.IFF_PNG);  /* ... */
        boxa = pix.ConnComp(8);
        n = boxa.Count;
        rp.CompareValues(nexp, n, 0.0f);
    }

    private static void CountPieces2(RegTest rp, Boxa boxa, int nexp)
    {
        int n = boxa.Count;
        rp.CompareValues(nexp, n, 0.0f);
    }

    private static int CountOnes(RegTest rp, Numa na, int nexp, int index, string? name)
    {
        int i, n, val, sum;

        n = na.Count;
        sum = 0;
        for (i = 0; i < n; i++) {
            val = na.GetIValue(i);
            if (val == 1) sum++;
        }
        if (name == null) return sum;
        rp.CompareValues(nexp, sum, 0.0f);
        return 0;
    }
}
