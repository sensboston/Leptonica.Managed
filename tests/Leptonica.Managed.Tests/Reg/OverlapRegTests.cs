using System;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// overlap_reg.c: tests functions that combine boxes that overlap into their bounding
/// regions.  Also tests the overlap and separation distance between boxes.
/// </summary>
public class OverlapRegTests
{
        /* Determines maximum size of randomly-generated boxes.  Note the
         * rapid change in results as the maximum box dimension approaches
         * the critical size of 28. */
    private static readonly float[] maxsize = { 5.0f, 10.0f, 15.0f, 20.0f, 25.0f, 26.0f, 27.0f };

    private static double Rand() => (double)CRand.Next() / (double)CRand.RandMax;

    [RegFact]
    public void Overlap()
    {
        int i, j, n, k, x, y, w, h, hovl, hsep, vovl, vsep;
        bool result;
        Box box1, box2;
        Boxa boxa1, boxa2, boxa3, boxa4;
        Pix pix1, pix2, pix3;
        Pixa pixa1;
        var rp = new RegTest("overlap");

        /* -------------------------------------------------------- */
        /*     Show the result as a kind of percolation problem     */
        /* -------------------------------------------------------- */
        for (k = 0; k < 7; k++) {
            CRand.Seed(45617);
            pixa1 = Pixa.Create(2);
            boxa1 = Boxa.Create(0);
            for (i = 0; i < 500; i++) {
                x = (int)(600.0 * Rand());
                y = (int)(600.0 * Rand());
                w = (int)(1.0 + maxsize[k] * Rand());
                h = (int)(1.0 + maxsize[k] * Rand());
                box1 = Box.Create(x, y, w, h);
                boxa1.AddBox(box1, L.L_INSERT);
            }

            pix1 = Pix.Create(660, 660, 1);
            pix1.RenderBoxa(boxa1, 2, L.L_SET_PIXELS);
            pixa1.AddPix(pix1, L.L_INSERT);
            boxa2 = boxa1.CombineOverlaps(null);
            pix2 = Pix.Create(660, 660, 1);
            pix2.RenderBoxa(boxa2, 2, L.L_SET_PIXELS);
            pixa1.AddPix(pix2, L.L_INSERT);

            pix3 = pixa1.DisplayTiledInRows(1, 1500, 1.0f, 0, 50, 2);
            rp.WritePixAndCheck(pix3, L.IFF_PNG);   /* 0 - 6 */
        }

        /* -------------------------------------------------------- */
        /*  Show for one case, with debugging, and compare with an  */
        /*                   an alternative version.                */
        /* -------------------------------------------------------- */
        boxa1 = Boxa.Create(0);
        pixa1 = Pixa.Create(10);
        n = 80;
        for (i = 0; i < n; i++) {
            x = (int)(600.0 * Rand());
            y = (int)(600.0 * Rand());
            w = (int)(10 + 48 * Rand());
            h = (int)(10 + 53 * Rand());
            box1 = Box.Create(x, y, w, h);
            boxa1.AddBox(box1, L.L_INSERT);
        }

        boxa2 = boxa1.CombineOverlaps(pixa1);
        result = boxa2.ContainedInBoxa(boxa1);  /* 7 */
        rp.CompareValues(1, result ? 1 : 0, 0.0f);

        pix1 = pixa1.DisplayTiledInRows(32, 1500, 1.0f, 0, 50, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);   /* 8 */

            /* Show the boxa from both functions are identical */
        boxa3 = BoxaCombineOverlapsAlt(boxa1);
        result = boxa3.ContainedInBoxa(boxa2);
        rp.CompareValues(1, result ? 1 : 0, 0.0f);  /* 9 */
        result = boxa2.ContainedInBoxa(boxa3);
        rp.CompareValues(1, result ? 1 : 0, 0.0f);  /* 10 */

        /* --------------------------------------------------------- */
        /*  Show for two boxa that are greedily munching each other  */
        /* --------------------------------------------------------- */
        boxa1 = Boxa.Create(0);
        boxa2 = Boxa.Create(0);
        n = 80;
        for (i = 0; i < n; i++) {
            x = (int)(600.0 * Rand());
            y = (int)(600.0 * Rand());
            w = (int)(10 + 55 * Rand());
            h = (int)(10 + 55 * Rand());
            box1 = Box.Create(x, y, w, h);
            if (i < n / 2)
                boxa1.AddBox(box1, L.L_INSERT);
            else
                boxa2.AddBox(box1, L.L_INSERT);
        }

        pixa1 = Pixa.Create(0);
        boxa1.CombineOverlapsInPair(boxa2, out boxa3, out boxa4, pixa1);
        pix1 = pixa1.DisplayTiledInRows(32, 1500, 1.0f, 0, 50, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);   /* 11 */

        /* --------------------------------------------------------- */
        /*    Test the overlap and separation distance functions     */
        /* --------------------------------------------------------- */
        box1 = Box.Create(0, 0, 1, 1);
        Utils.LeptMkdir("lept/overlap");
        var sb = new StringBuilder();
        for (i = 0; i < 3; i++) {  /* 9 1x1 boxes on a 3x3 square */
            for (j = 0; j < 3; j++) {
                box2 = Box.Create(i, j, 1, 1);
                box1.OverlapDistance(box2, out hovl, out vovl);
                box1.SeparationDistance(box2, out hsep, out vsep);
                sb.Append($"({i},{j}): ovl = ({hovl},{vovl}); sep = ({hsep},{vsep})\n");
            }
        }
        Utils.BinaryWrite("/tmp/lept/overlap/result.dat", "w", Encoding.ASCII.GetBytes(sb.ToString()));
        byte[] data = Utils.BinaryRead("/tmp/lept/overlap/result.dat");
        rp.WriteDataAndCheck(data, "dat");  /* 12 */

        Assert.Equal(12, rp.Index);
        rp.Cleanup();
    }

    /* -------------------------------------------------------------------- *
     *   Alternative (less elegant) implementation of boxaCombineOverlaps() *
     * -------------------------------------------------------------------- */
    private static Boxa BoxaCombineOverlapsAlt(Boxa boxas)
    {
        int i, j, n1, n2, niters;
        bool interfound;
        Box box1, box2, box3;
        Boxa boxa1, boxa2;

        boxa1 = boxas.Copy(L.L_COPY);
        n1 = boxa1.Count;
        niters = 0;
        while (true) {  /* loop until no change from previous iteration */
            niters++;
            boxa2 = Boxa.Create(n1);
            for (i = 0; i < n1; i++) {
                box1 = boxa1.GetBox(i, L.L_COPY);
                if (i == 0) {
                    boxa2.AddBox(box1, L.L_INSERT);
                    continue;
                }
                n2 = boxa2.Count;
                    /* Now test box1 against all boxes already put in boxa2.
                     * If it is found to intersect with an existing box,
                     * replace that box by the union of the two boxes,
                     * and break to the outer loop.  If no overlap is
                     * found, add box1 to boxa2. */
                interfound = false;
                for (j = 0; j < n2; j++) {
                    box2 = boxa2.GetBox(j, L.L_CLONE);
                    if (box1.Intersects(box2)) {
                        box3 = box1.BoundingRegion(box2);
                        boxa2.ReplaceBox(j, box3);
                        interfound = true;
                        break;
                    }
                }
                if (!interfound)
                    boxa2.AddBox(box1, L.L_INSERT);
            }

            n2 = boxa2.Count;
            if (n2 == n1)  /* we're done */
                break;

            n1 = n2;
            boxa1 = boxa2;
        }
        return boxa2;
    }
}
