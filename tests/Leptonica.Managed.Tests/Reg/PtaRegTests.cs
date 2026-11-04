using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// pta_reg.c: tests boundary pixels, rendering, rotation, cyclic permutation,
/// reversal, equality, and polygon convexity on Pta and Ptaa.
/// </summary>
public class PtaRegTests
{
    [RegFact]
    public void Pta_Reg()
    {
        var rp = new RegTest("pta");

        Pix pixs = TestPaths.ReadInput("feyn-fract.tif");
        Boxa boxa = pixs.ConnComp(8);
        int nbox = boxa.Count;
        rp.CompareValues(nbox, 464, 0);  /* 0 */

            /* Get fg and bg boundary pixels */
        Pix pixfg = pixs.MorphSequence("e3.3", 0);
        Pix.Xor(pixfg, pixfg, pixs);
        int fgcount = pixfg.CountPixels(null);
        rp.CompareValues(fgcount, 58764, 0);  /* 1 */

        Pix pixbg = pixs.MorphSequence("d3.3", 0);
        Pix.Xor(pixbg, pixbg, pixs);
        int bgcount = pixbg.CountPixels(null);
        rp.CompareValues(bgcount, 60335, 0);  /* 2 */

            /* Get ptaa of fg pixels */
        Ptaa ptaafg = Ptaa.GetBoundaryPixels(pixs, L.L_BOUNDARY_FG, 8, out _, out _);
        int npta = ptaafg.Count;
        rp.CompareValues(npta, nbox, 0);  /* 3 */
        int count = 0;
        for (int i = 0; i < npta; i++) {
            Pta pta = ptaafg.GetPta(i, L.L_CLONE);
            count += pta.Count;
        }
        rp.CompareValues(fgcount, count, 0);  /* 4 */

            /* Get ptaa of bg pixels */
        Ptaa ptaabg = Ptaa.GetBoundaryPixels(pixs, L.L_BOUNDARY_BG, 8, out _, out _);
        npta = ptaabg.Count;
        rp.CompareValues(npta, nbox, 0);  /* 5 */
        count = 0;
        for (int i = 0; i < npta; i++) {
            Pta pta = ptaabg.GetPta(i, L.L_CLONE);
            count += pta.Count;
        }
        rp.CompareValues(count, 60602, 0);  /* 6 */

            /* Render the fg boundary pixels on top of pixs */
        Pixa pixa = Pixa.Create(4);
        Pix pixc = pixs.RenderRandomCmapPtaa(ptaafg, 0, 0, 0);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 7 */
        pixa.AddPix(pixc, L.L_INSERT);

            /* Render the bg boundary pixels on top of pixs */
        pixc = pixs.RenderRandomCmapPtaa(ptaabg, 0, 0, 0);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 8 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixs.ClearAll();

            /* Render the fg boundary pixels alone */
        pixc = pixs.RenderRandomCmapPtaa(ptaafg, 0, 0, 0);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 9 */
        pixa.AddPix(pixc, L.L_INSERT);

        Pix pixb = pixc.ConvertTo1(255);
        rp.ComparePix(pixb, pixfg);  /* 10 */

            /* Render the bg boundary pixels alone */
        pixc = pixs.RenderRandomCmapPtaa(ptaabg, 0, 0, 0);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 11 */
        pixa.AddPix(pixc, L.L_INSERT);

        pixb = pixc.ConvertTo1(255);
        rp.ComparePix(pixb, pixbg);  /* 12 */

            /* Test rotation */
        Pix pix1 = TestPaths.ReadInput("feyn-word.tif");
        Pix pix2 = pix1.AddBorderGeneral(200, 200, 200, 200, 0);
        pixa = Pixa.Create(0);
        pixa.AddPix(PtaDisplayRotate(pix2, 0, 0), L.L_INSERT);
        pixa.AddPix(PtaDisplayRotate(pix2, 500, 100), L.L_INSERT);
        pixa.AddPix(PtaDisplayRotate(pix2, 100, 410), L.L_INSERT);
        pixa.AddPix(PtaDisplayRotate(pix2, 500, 410), L.L_INSERT);
        Pix pix4 = pixa.DisplayTiledInRows(32, 1500, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 13 */

            /* Test pta sort and pta equality */
        pix1 = TestPaths.ReadInput("feyn-word.tif");
        Pta pta1 = Pta.GetPixelsFromPix(pix1, null);
        pta1.GetIPt(0, out int x, out int y);
        pta1.AddPt(x, y);
        Pta pta2 = pta1.CyclicPerm(x, y);
        bool same = pta1.Equal(pta2);
        rp.CompareValues(same ? 1 : 0, 1, 0.0f);  /* 14 */
        Pta pta3 = pta2.Reverse(1);
        same = pta1.Equal(pta3);
        rp.CompareValues(same ? 1 : 0, 1, 0.0f);  /* 15 */

            /* Test if polygon is a convex hull */
        pta1 = Pta.Create(0);
        pta2 = Pta.Create(0);
        pta3 = Pta.Create(0);
        int n = 30;
        for (int i = 0; i < n; i++) {
            float ang = (float)(-2.0 * 3.14159265 * i / (float)n);
            float fx = (float)(50.0 + 27.3 * Math.Cos(ang));
            float fy = (float)(50.0 + 27.3 * Math.Sin(ang));
            pta1.AddPt(fx, fy);
            if (i == n / 2) fx -= 5.0f;
            pta2.AddPt(fx, fy);
            if (i == n / 2) fx += 10.0f;
            pta3.AddPt(fx, fy);
        }
        bool convex1 = pta1.PolygonIsConvex();
        bool convex2 = pta2.PolygonIsConvex();
        bool convex3 = pta3.PolygonIsConvex();
        rp.CompareValues(1, convex1 ? 1 : 0, 0.0f);  /* 16 */
        rp.CompareValues(0, convex2 ? 1 : 0, 0.0f);  /* 17 */
        rp.CompareValues(0, convex3 ? 1 : 0, 0.0f);  /* 18 */

        pixa = Pixa.Create(3);
        pix1 = Pix.Create(100, 100, 1);
        pix1.RenderPta(pta1, L.L_SET_PIXELS);
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = Pix.Create(100, 100, 1);
        pix1.RenderPta(pta2, L.L_SET_PIXELS);
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = Pix.Create(100, 100, 1);
        pix1.RenderPta(pta3, L.L_SET_PIXELS);
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = pixa.DisplayTiledInColumns(3, 5.0f, 30, 3);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 19 */

        Assert.Equal(19, rp.Index);
        rp.Cleanup();
    }

    private static Pix PtaDisplayRotate(Pix pixs, float xc, float yc)
    {
        Pta pta1 = Pta.GetPixelsFromPix(pixs, null);
        Ptaa ptaa = Ptaa.Create(0);
        for (int i = 0; i < 9; i++) {
            Pta pta2 = pta1.Rotate(xc, yc, (float)(-0.8 + 0.2 * i));
            ptaa.AddPta(pta2, L.L_INSERT);
        }

        pixs.GetDimensions(out int w, out int h, out _);
        Pix pix1 = Pix.Create(w, h, 32);
        pix1.SetAll();
        Pta pta3 = Pix.GeneratePtaFilledCircle(4);
        Pta pta4 = pta3.Translate(xc, yc);
        pix1.RenderPtaArb(pta4, 255, 0, 0);
        return pix1.DisplayPtaa(ptaa);
    }
}
