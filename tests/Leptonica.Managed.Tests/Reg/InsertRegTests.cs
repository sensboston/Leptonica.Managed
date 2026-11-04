using System;
using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>insert_reg.c: tests removal and insertion operations in numa, boxa and pixa.</summary>
public class InsertRegTests
{
    private static void RmFile(string path) => File.Delete(Utils.GenPathname(path, null));

    [RegFact]
    public void Insert()
    {
        int i, n;
        float pi, angle, val;
        Box? box;
        Box box0;
        Boxa boxa, boxa1, boxa2;
        Numa na1, na2;
        Pix? pix;
        Pix pix1, pix2;
        Pixa pixa1, pixa2, pixa3, pixa4;
        var rp = new RegTest("insert");

        RmFile("/tmp/lept/regout/insert3.ba");
        RmFile("/tmp/lept/regout/insert4.ba");
        RmFile("/tmp/lept/regout/insert6.pa");
        RmFile("/tmp/lept/regout/insert7.pa");
        RmFile("/tmp/lept/regout/insert9.pa");
        RmFile("/tmp/lept/regout/insert10.pa");

        /* ----------------- Test numa operations -------------------- */
        pi = 3.1415926535f;
        na1 = Numa.Create(500);
        for (i = 0; i < 500; i++) {
            angle = (float)(0.02293 * i * (double)pi);
            val = (float)Math.Sin(angle);
            na1.AddNumber(val);
        }
        na1.Write("/tmp/lept/regout/insert0.na");
        na2 = na1.Copy();
        n = na2.Count;
        for (i = 0; i < n; i++) {
            val = na2.GetFValue(i);
            na2.RemoveNumber(i);
            na2.InsertNumber(i, val);
        }
        na2.Write("/tmp/lept/regout/insert1.na");
        rp.CheckFile("/tmp/lept/regout/insert0.na");  /* 0 */
        rp.CheckFile("/tmp/lept/regout/insert1.na");  /* 1 */
        rp.CompareFiles(0, 1);  /* 2 */

        /* ----------------- Test boxa operations -------------------- */
        pix1 = TestPaths.ReadInput("feyn.tif");
        box0 = Box.Create(1138, 1666, 1070, 380);
        pix2 = pix1.ClipRectangle(box0, out _)!;
        boxa1 = pix2.ConnComp(8);
        boxa1.Write("/tmp/lept/regout/insert3.ba");
        boxa2 = boxa1.Copy(L.L_COPY);
        n = boxa2.Count;
        for (i = 0; i < n; i++) {
            boxa2.RemoveBoxAndSave(i, out box);
            boxa2.InsertBox(i, box!);
        }
        boxa2.Write("/tmp/lept/regout/insert4.ba");
        rp.CheckFile("/tmp/lept/regout/insert3.ba");  /* 3 */
        rp.CheckFile("/tmp/lept/regout/insert4.ba");  /* 4 */
        rp.CompareFiles(3, 4);  /* 5 */

        /* ----------------- Test pixa operations -------------------- */
        pix1 = TestPaths.ReadInput("feyn.tif");
        box0 = Box.Create(1138, 1666, 1070, 380);
        pix2 = pix1.ClipRectangle(box0, out _)!;
        boxa = pix2.ConnComp(out pixa1, 8);
        pixa1.Write("/tmp/lept/regout/insert6.pa");
        rp.CheckFile("/tmp/lept/regout/insert6.pa");  /* 6 */

            /* Remove and insert each one */
        pixa2 = pixa1.Copy(L.L_COPY);
        n = pixa2.Count;
        for (i = 0; i < n; i++) {
            pixa2.RemovePixAndSave(i, out pix, out box);
            pixa2.InsertPix(i, pix!, box);
        }
        pixa2.Write("/tmp/lept/regout/insert7.pa");
        rp.CheckFile("/tmp/lept/regout/insert7.pa");  /* 7 */
        rp.CompareFiles(6, 7);  /* 8 */

            /* Move the last to the beginning; do it n times */
        pixa3 = pixa2.Copy(L.L_COPY);
        for (i = 0; i < n; i++) {
            pix = pixa3.GetPix(n - 1, L.L_CLONE);
            box = pixa3.GetBox(n - 1, L.L_CLONE);
            pixa3.InsertPix(0, pix, box);
            pixa3.RemovePix(n);
        }
        pixa3.Write("/tmp/lept/regout/insert9.pa");
        rp.CheckFile("/tmp/lept/regout/insert9.pa");  /* 9 */

            /* Move the first one to the end; do it n times */
        pixa4 = pixa3.Copy(L.L_COPY);
        for (i = 0; i < n; i++) {
            pix = pixa4.GetPix(0, L.L_CLONE);
            box = pixa4.GetBox(0, L.L_CLONE);
            pixa4.InsertPix(n, pix, box);  /* make sure insert works at end */
            pixa4.RemovePix(0);
        }
        pixa4.Write("/tmp/lept/regout/insert10.pa");
        rp.CheckFile("/tmp/lept/regout/insert10.pa");  /* 10 */
        rp.CompareFiles(9, 10);  /* 11 */

        Assert.Equal(11, rp.Index);
        rp.Cleanup();
    }
}
