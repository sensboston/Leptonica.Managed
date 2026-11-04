using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// ptra2_reg.c: basic ptra and ptraa operations, bin sort using ptra,
/// and boxaEqual() and pixaEqual().
/// </summary>
public class Ptra2RegTests
{
    [RegFact]
    public void Ptra2()
    {
        var rp = new RegTest("ptra2");

        Utils.LeptMkdir("lept/ptra");

            /* 0 - 8 */
        BoxaSortTest(rp, "feyn-fract.tif", 1, "Boxa sort test on small image");
            /* 9 - 17 */
        BoxaSortTest(rp, "feyn.tif", 2, "Boxa sort test on large image");
            /* 18 - 27 */
        PixaSortTest(rp, "feyn-fract.tif", 3, "Pixa sort test on small image");
            /* 28 - 37 */
        PixaSortTest(rp, "feyn.tif", 4, "Pixa sort test on large image");
        Assert.Equal(37, rp.Index);
        rp.Cleanup();
    }

    private static void BoxaSortTest(RegTest rp, string fname, int index, string text)
    {
        int i, n, m, imax, w, h, x, count;
        bool same;
        Box box;
        Boxa boxa, boxa1, boxa2, boxa3;
        Numa na, nad1, nad2, nad3, naindex;
        Pix pixs;
        L_Ptra<Box> pa, pad;
        L_Ptra<Numa> paindex;
        L_Ptraa<Box> paa;
        string buf;

        pixs = TestPaths.ReadInput(fname);
        boxa = pixs.ConnComp(8);

            /* Sort by x */
        boxa1 = boxa.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out nad1!);
        buf = $"/tmp/lept/ptra/boxa1.{index}.ba";
        boxa1.Write(buf);
        rp.CheckFile(buf);  /* 0 */
        buf = $"/tmp/lept/ptra/nad1.{index}.na";
        nad1.Write(buf);
        rp.CheckFile(buf);  /* 1 */

        boxa2 = boxa.BinSort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out nad2!);
        buf = $"/tmp/lept/ptra/boxa2.{index}.ba";
        boxa2.Write(buf);
        rp.CheckFile(buf);  /* 2 */
        buf = $"/tmp/lept/ptra/nad2.{index}.na";
        nad2.Write(buf);
        rp.CheckFile(buf);  /* 3 */

        same = boxa1.Equal(boxa2, 0, out _);
        rp.CompareValues(1, same ? 1 : 0, 0.0f);  /* 4 */
        same = boxa1.Equal(boxa2, 2, out naindex!);
        rp.CompareValues(1, same ? 1 : 0, 0.0f);  /* 5 */
        buf = $"/tmp/lept/ptra/naindex.{index}.na";
        naindex.Write(buf);
        rp.CheckFile(buf);  /* 6 */

            /* Now do this stuff with ptra and ptraa */
            /* First, store the boxes in a ptraa, where each ptra contains
             * the boxes, and store the sort index in a ptra of numa */
        pixs.GetDimensions(out w, out h, out _);
        paa = L_Ptraa<Box>.Create(w);
        paindex = L_Ptra<Numa>.Create(w);
        n = boxa.Count;
        for (i = 0; i < n; i++) {
            box = boxa.GetBox(i, L.L_CLONE);
            box.GetGeometry(out x, out _, out _, out _);
            pa = paa.GetPtra(x, L.L_HANDLE_ONLY)!;
            na = paindex.GetPtrToItem(x)!;
            if (pa == null) {  /* na also needs to be made */
                pa = L_Ptra<Box>.Create(1);
                paa.InsertPtra(x, pa);
                na = Numa.Create(1);
                paindex.Insert(x, na, L.L_MIN_DOWNSHIFT);
            }
            pa.Add(box);
            na.AddNumber(i);
        }
        count = paindex.ActualCount;

            /* Flatten the ptraa to a ptra containing all the boxes
             * in sorted order, and put them in a boxa */
        pad = paa.FlattenToPtra();
        m = pad.ActualCount;
        boxa3 = Boxa.Create(m);
        for (i = 0; i < m; i++) {
            box = pad.Remove(i, L.L_NO_COMPACTION)!;
            boxa3.AddBox(box, L.L_INSERT);
        }

            /* Extract the data from the ptra of Numa, putting it into
             * a single Numa */
        imax = paindex.MaxIndex;
        nad3 = Numa.Create(0);
        for (i = 0; i <= imax; i++) {
            na = paindex.Remove(i, L.L_NO_COMPACTION)!;
            nad3.Join(na, 0, -1);
        }

        buf = $"/tmp/lept/ptra/boxa3.{index}.ba";
        boxa3.Write(buf);
        rp.CheckFile(buf);  /* 7 */
        buf = $"/tmp/lept/ptra/nad3.{index}.na";
        nad3.Write(buf);
        rp.CheckFile(buf);  /* 8 */
    }

    private static void PixaSortTest(RegTest rp, string fname, int index, string text)
    {
        bool same;
        Boxa boxa, boxa1, boxa2;
        Numa nap1, nap2, naindex;
        Pix pixs;
        Pixa pixa, pixa1, pixa2;
        string buf;

        pixs = TestPaths.ReadInput(fname);
        boxa = pixs.ConnComp(out pixa, 8);

        pixa1 = pixa.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out nap1!, L.L_CLONE);
        boxa1 = pixa1.GetBoxa(L.L_CLONE);
        buf = $"/tmp/lept/ptra/bap1.{index}.ba";
        boxa1.Write(buf);
        rp.CheckFile(buf);  /* 0 */
        buf = $"/tmp/lept/ptra/nap1.{index}.na";
        nap1.Write(buf);
        rp.CheckFile(buf);  /* 1 */
        buf = $"/tmp/lept/ptra/pixa1.{index}.pa";
        pixa1.Write(buf);
        rp.CheckFile(buf);  /* 2 */

        pixa2 = pixa.BinSort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out nap2!, L.L_CLONE);
        boxa2 = pixa2.GetBoxa(L.L_CLONE);
        buf = $"/tmp/lept/ptra/bap2.{index}.ba";
        boxa2.Write(buf);
        rp.CheckFile(buf);  /* 3 */
        buf = $"/tmp/lept/ptra/nap2.{index}.na";
        nap2.Write(buf);
        rp.CheckFile(buf);  /* 4 */
        buf = $"/tmp/lept/ptra/pixa2.{index}.pa";
        pixa2.Write(buf);
        rp.CheckFile(buf);  /* 5 */

        same = boxa1.Equal(boxa2, 0, out naindex!);
        rp.CompareValues(1, same ? 1 : 0, 0.0f);  /* 6 */
        same = boxa1.Equal(boxa2, 3, out naindex!);
        rp.CompareValues(1, same ? 1 : 0, 0.0f);  /* 7 */

        same = pixa1.Equal(pixa2, 0, out naindex!);
        rp.CompareValues(1, same ? 1 : 0, 0.0f);  /* 8 */
        same = pixa1.Equal(pixa2, 3, out naindex!);
        rp.CompareValues(1, same ? 1 : 0, 0.0f);  /* 9 */
    }
}
