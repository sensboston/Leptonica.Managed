using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>ptra1_reg.c: basic ptra operations.</summary>
public class Ptra1RegTests
{
    [RegFact]
    public void Ptra1()
    {
        int i, n, w, h, nactual, imax;
        Box? box;
        Boxa boxa;
        Pix pixs, pixd;
        Pix? pix;
        Pixa pixas, pixa1, pixa2, pixac1, pixac2;
        L_Ptra<Pix> papix, papix2;
        L_Ptra<Box> pabox, pabox2;
        var rp = new RegTest("ptra1");

        pixac1 = Pixa.Create(0);
        pixac2 = Pixa.Create(0);

        pixs = TestPaths.ReadInput("lucasta.1.300.tif");
        pixs.GetDimensions(out w, out h, out _);
        boxa = pixs.ConnComp(out pixas, 8);
        n = pixas.Count;

            /* Fill ptras with clones and reconstruct */
        MakePtrasFromPixa(pixas, out papix, out pabox, L.L_CLONE);
        pixa1 = ReconstructPixa1(papix, pabox);
        MakePtrasFromPixa(pixas, out papix, out pabox, L.L_CLONE);
        pixa2 = ReconstructPixa2(papix, pabox);
        pixd = SaveResult(pixac1, pixa1, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 0 */
        pixd = SaveResult(pixac2, pixa2, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 1 */

            /* Remove every other one for the first half;
             * with compaction at each removal */
        MakePtrasFromPixa(pixas, out papix, out pabox, L.L_COPY);
        for (i = 0; i < n / 2; i++) {
            if (i % 2 == 0) {
                pix = papix.Remove(i, L.L_COMPACTION);
                box = pabox.Remove(i, L.L_COMPACTION);
            }
        }
        pixa1 = ReconstructPixa1(papix, pabox);
        pixd = SaveResult(pixac1, pixa1, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 2 */

        MakePtrasFromPixa(pixas, out papix, out pabox, L.L_COPY);
        for (i = 0; i < n / 2; i++) {
            if (i % 2 == 0) {
                pix = papix.Remove(i, L.L_COMPACTION);
                box = pabox.Remove(i, L.L_COMPACTION);
            }
        }
        pixa2 = ReconstructPixa2(papix, pabox);
        pixd = SaveResult(pixac2, pixa2, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 3 */

            /* Remove every other one for the entire set,
             * but without compaction at each removal */
        MakePtrasFromPixa(pixas, out papix, out pabox, L.L_COPY);
        for (i = 0; i < n; i++) {
            if (i % 2 == 0) {
                pix = papix.Remove(i, L.L_NO_COMPACTION);
                box = pabox.Remove(i, L.L_NO_COMPACTION);
            }
        }
        papix.CompactArray();  /* now do the compaction */
        pabox.CompactArray();
        pixa1 = ReconstructPixa1(papix, pabox);
        pixd = SaveResult(pixac1, pixa1, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 4 */

        MakePtrasFromPixa(pixas, out papix, out pabox, L.L_COPY);
        for (i = 0; i < n; i++) {
            if (i % 2 == 0) {
                pix = papix.Remove(i, L.L_NO_COMPACTION);
                box = pabox.Remove(i, L.L_NO_COMPACTION);
            }
        }
        papix.CompactArray();  /* now do the compaction */
        pabox.CompactArray();
        pixa2 = ReconstructPixa2(papix, pabox);
        pixd = SaveResult(pixac2, pixa2, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 5 */

            /* Fill ptras using insert at head, and reconstruct */
        papix = L_Ptra<Pix>.Create(n);
        pabox = L_Ptra<Box>.Create(n);
        for (i = 0; i < n; i++) {
            pix = pixas.GetPix(i, L.L_CLONE);
            box = pixas.GetBox(i, L.L_CLONE);
            papix.Insert(0, pix, L.L_MIN_DOWNSHIFT);
            pabox.Insert(0, box, L.L_FULL_DOWNSHIFT);
        }
        pixa1 = ReconstructPixa1(papix, pabox);
        pixd = SaveResult(pixac1, pixa1, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 6 */

        papix = L_Ptra<Pix>.Create(n);
        pabox = L_Ptra<Box>.Create(n);
        for (i = 0; i < n; i++) {
            pix = pixas.GetPix(i, L.L_CLONE);
            box = pixas.GetBox(i, L.L_CLONE);
            papix.Insert(0, pix, L.L_MIN_DOWNSHIFT);
            pabox.Insert(0, box, L.L_FULL_DOWNSHIFT);
        }
        pixa2 = ReconstructPixa2(papix, pabox);
        pixd = SaveResult(pixac2, pixa2, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 7 */

            /* Reverse the arrays by swapping */
        MakePtrasFromPixa(pixas, out papix, out pabox, L.L_CLONE);
        for (i = 0; i < n / 2; i++) {
            papix.Swap(i, n - i - 1);
            pabox.Swap(i, n - i - 1);
        }
        papix.CompactArray();  /* already compact; shouldn't do anything */
        pabox.CompactArray();
        pixa1 = ReconstructPixa1(papix, pabox);
        pixd = SaveResult(pixac1, pixa1, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 8 */

        MakePtrasFromPixa(pixas, out papix, out pabox, L.L_CLONE);
        for (i = 0; i < n / 2; i++) {
            papix.Swap(i, n - i - 1);
            pabox.Swap(i, n - i - 1);
        }
        papix.CompactArray();  /* already compact; shouldn't do anything */
        pabox.CompactArray();
        pixa2 = ReconstructPixa2(papix, pabox);
        pixd = SaveResult(pixac2, pixa2, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 9 */

            /* Remove at the top of the array and push the hole to the end
             * by neighbor swapping (!).  This is O(n^2), so it's not a
             * recommended way to copy a ptra. [joke]  */
        MakePtrasFromPixa(pixas, out papix, out pabox, L.L_CLONE);
        papix2 = L_Ptra<Pix>.Create(0);
        pabox2 = L_Ptra<Box>.Create(0);
        while (true) {
            nactual = papix.ActualCount;
            if (nactual == 0) break;
            imax = papix.MaxIndex;
            pix = papix.Remove(0, L.L_NO_COMPACTION);
            box = pabox.Remove(0, L.L_NO_COMPACTION);
            papix2.Add(pix!);
            pabox2.Add(box!);
            for (i = 1; i <= imax; i++) {
                papix.Swap(i - 1, i);
                pabox.Swap(i - 1, i);
            }
        }
        papix.CompactArray();  /* should be empty */
        pabox.CompactArray();  /* ditto */
        pixa1 = ReconstructPixa1(papix, pabox);
        pixd = SaveResult(pixac1, pixa1, w, h);  /* nothing there */
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 10 */

        pixa1 = ReconstructPixa1(papix2, pabox2);
        pixd = SaveResult(pixac1, pixa1, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 11 */

            /* Remove and insert one position above, allowing minimum downshift.
             * If you specify L_AUTO_DOWNSHIFT, because there is only 1 hole,
             * it will do a full downshift at each insert.  This is a
             * situation where the heuristic (expected number of holes)
             * fails to do the optimal thing. */
        MakePtrasFromPixa(pixas, out papix, out pabox, L.L_CLONE);
        for (i = 1; i < n; i++) {
            pix = papix.Remove(i, L.L_NO_COMPACTION);
            box = pabox.Remove(i, L.L_NO_COMPACTION);
            papix.Insert(i - 1, pix, L.L_MIN_DOWNSHIFT);
            pabox.Insert(i - 1, box, L.L_MIN_DOWNSHIFT);
        }
        pixa1 = ReconstructPixa1(papix, pabox);
        pixd = SaveResult(pixac1, pixa1, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 12 */

        MakePtrasFromPixa(pixas, out papix, out pabox, L.L_CLONE);
        for (i = 1; i < n; i++) {
            pix = papix.Remove(i, L.L_NO_COMPACTION);
            box = pabox.Remove(i, L.L_NO_COMPACTION);
            papix.Insert(i - 1, pix, L.L_MIN_DOWNSHIFT);
            pabox.Insert(i - 1, box, L.L_MIN_DOWNSHIFT);
        }
        pixa2 = ReconstructPixa2(papix, pabox);
        pixd = SaveResult(pixac2, pixa2, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 13 */

            /* Remove and insert one position above, but this time
             * forcing a full downshift at each step.  */
        MakePtrasFromPixa(pixas, out papix, out pabox, L.L_CLONE);
        for (i = 1; i < n; i++) {
            pix = papix.Remove(i, L.L_NO_COMPACTION);
            box = pabox.Remove(i, L.L_NO_COMPACTION);
            papix.Insert(i - 1, pix, L.L_AUTO_DOWNSHIFT);
            pabox.Insert(i - 1, box, L.L_AUTO_DOWNSHIFT);
        }
        pixa1 = ReconstructPixa1(papix, pabox);
        pixd = SaveResult(pixac1, pixa1, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 14 */

        MakePtrasFromPixa(pixas, out papix, out pabox, L.L_CLONE);
        for (i = 1; i < n; i++) {
            pix = papix.Remove(i, L.L_NO_COMPACTION);
            box = pabox.Remove(i, L.L_NO_COMPACTION);
            papix.Insert(i - 1, pix, L.L_AUTO_DOWNSHIFT);
            pabox.Insert(i - 1, box, L.L_AUTO_DOWNSHIFT);
        }
        pixa2 = ReconstructPixa2(papix, pabox);
        pixd = SaveResult(pixac2, pixa2, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);   /* 15 */

        pixd = pixac1.DisplayTiledInColumns(10, 0.5f, 15, 2);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 16 */
        pixd = pixac2.DisplayTiledInColumns(10, 0.5f, 15, 2);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 17 */

        Assert.Equal(17, rp.Index);
        rp.Cleanup();
    }

    private static void MakePtrasFromPixa(Pixa pixa, out L_Ptra<Pix> ppapix,
                                          out L_Ptra<Box> ppabox, int copyflag)
    {
        int i, n;
        Box? box;
        Pix pix;
        L_Ptra<Pix> papix;
        L_Ptra<Box> pabox;

        n = pixa.Count;
        papix = L_Ptra<Pix>.Create(n);
        pabox = L_Ptra<Box>.Create(n);
        for (i = 0; i < n; i++) {
            pix = pixa.GetPix(i, copyflag);
            box = pixa.GetBox(i, copyflag);
            papix.Add(pix);
            pabox.Add(box!);
        }

        ppapix = papix;
        ppabox = pabox;
    }

        /* Reconstruction without compaction */
    private static Pixa ReconstructPixa1(L_Ptra<Pix> papix, L_Ptra<Box> pabox)
    {
        int i, imax;
        Box? box;
        Pix? pix;
        Pixa pixat;

        imax = papix.MaxIndex;

        pixat = Pixa.Create(imax + 1);
        for (i = 0; i <= imax; i++) {
            pix = papix.Remove(i, L.L_NO_COMPACTION);
            box = pabox.Remove(i, L.L_NO_COMPACTION);
            if (pix != null) pixat.AddPix(pix, L.L_INSERT);
            if (box != null) pixat.AddBox(box, L.L_INSERT);
        }

        return pixat;
    }

        /* Reconstruction with compaction */
    private static Pixa ReconstructPixa2(L_Ptra<Pix> papix, L_Ptra<Box> pabox)
    {
        int i, imax, nactual;
        Box? box;
        Pix? pix;
        Pixa pixat;

        imax = papix.MaxIndex;

            /* Remove half */
        pixat = Pixa.Create(imax + 1);
        for (i = 0; i <= imax; i++) {
            if (i % 2 == 0) {
                pix = papix.Remove(i, L.L_NO_COMPACTION);
                box = pabox.Remove(i, L.L_NO_COMPACTION);
                if (pix != null) pixat.AddPix(pix, L.L_INSERT);
                if (box != null) pixat.AddBox(box, L.L_INSERT);
            }
        }

            /* Compact */
        papix.CompactArray();
        pabox.CompactArray();

            /* Remove the rest (and test compaction with removal) */
        while (true) {
            nactual = papix.ActualCount;
            if (nactual == 0) break;

            pix = papix.Remove(0, L.L_COMPACTION);
            box = pabox.Remove(0, L.L_COMPACTION);
            pixat.AddPix(pix!, L.L_INSERT);
            pixat.AddBox(box!, L.L_INSERT);
        }

        return pixat;
    }

    private static Pix SaveResult(Pixa pixac, Pixa pixa, int w, int h)
    {
        Pix pixd = pixa.Display(w, h);
        pixac.AddPix(pixd, L.L_COPY);
        return pixd;
    }
}
