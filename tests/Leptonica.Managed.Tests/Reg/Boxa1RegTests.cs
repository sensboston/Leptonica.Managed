using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// boxa1_reg.c: various operations on boxa, including region comparison,
/// transforms and display.
/// </summary>
public class Boxa1RegTests
{
    [RegFact]
    public void Boxa1()
    {
        byte[] data1, data2;
        bool same;
        float diffarea, diffxor;
        Box box;
        Boxa boxa1, boxa2, boxa3;
        Pix pix1;
        Pta pta;
        var rp = new RegTest("boxa1");

        Utils.LeptMkdir("lept/boxa");

            /* Make a boxa and display its contents */
        boxa1 = Boxa.Create(6);
        box = Box.Create(60, 60, 40, 20);
        boxa1.AddBox(box, L.L_INSERT);
        box = Box.Create(120, 50, 20, 50);
        boxa1.AddBox(box, L.L_INSERT);
        box = Box.Create(50, 140, 46, 60);
        boxa1.AddBox(box, L.L_INSERT);
        box = Box.Create(166, 130, 64, 28);
        boxa1.AddBox(box, L.L_INSERT);
        box = Box.Create(64, 224, 44, 34);
        boxa1.AddBox(box, L.L_INSERT);
        box = Box.Create(117, 206, 26, 74);
        boxa1.AddBox(box, L.L_INSERT);
        pix1 = DisplayBoxa(boxa1);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */

        boxa1.CompareRegions(boxa1, 100, out same, out diffarea, out diffxor);
        rp.CompareValues(1, same ? 1 : 0, 0.0f);  /* 1 */
        rp.CompareValues(0.0f, diffarea, 0.0f);  /* 2 */
        rp.CompareValues(0.0f, diffxor, 0.0f);  /* 3 */

        boxa2 = boxa1.Transform(-13, -13, 1.0f, 1.0f);
        boxa1.CompareRegions(boxa2, 10, out same, out diffarea, out diffxor);
        rp.CompareValues(1, same ? 1 : 0, 0.0f);  /* 4 */
        rp.CompareValues(0.0f, diffarea, 0.0f);  /* 5 */
        rp.CompareValues(0.0f, diffxor, 0.0f);  /* 6 */

        pta = boxa1.ExtractCorners(L.L_UPPER_LEFT);
        boxa2 = boxa1.ShiftWithPta(pta, 1);  /* shift */
        boxa3 = boxa2.ShiftWithPta(pta, -1);  /* inverse shift */
        data1 = boxa1.WriteMem();
        data2 = boxa3.WriteMem();
        rp.CompareStrings(data1, data2);  /* 7 */

            /* Test serialized boxa I/O to and from memory */
        data1 = Utils.BinaryRead(System.IO.Path.Combine(TestPaths.ProgDir, "boxa2.ba"));
        boxa1 = Boxa.ReadMem(data1);
        data2 = boxa1.WriteMem();
        boxa2 = Boxa.ReadMem(data2);
        boxa1.Write("/tmp/lept/boxa/boxa1.ba");
        boxa2.Write("/tmp/lept/boxa/boxa2.ba");
        same = Utils.FilesAreIdentical("/tmp/lept/boxa/boxa1.ba", "/tmp/lept/boxa/boxa2.ba");
        rp.CompareValues(1, same ? 1 : 0, 0.0f);  /* 8 */

        Assert.Equal(8, rp.Index);
        rp.Cleanup();
    }

    private static Pix DisplayBoxa(Boxa boxa)
    {
        int w, h;
        Box box;
        Pix pix1, pix2, pix3;
        Pixa pixa;

        pixa = Pixa.Create(2);
        boxa.GetExtent(out w, out h, out box);
        pix1 = Pix.Create(w, h, 1);
        Pix.MaskBoxa(pix1, pix1, boxa, L.L_SET_PIXELS);
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = Pix.Create(w, h, 32);
        pix2.SetAll();
        pix2.RenderBoxaArb(boxa, 2, 0, 255, 0);
        pix2.RenderBoxArb(box, 3, 255, 0, 0);
        pixa.AddPix(pix2, L.L_INSERT);
        pix3 = pixa.DisplayTiledInRows(32, 1000, 1.0f, 0, 30, 2);
        return pix3;
    }
}
