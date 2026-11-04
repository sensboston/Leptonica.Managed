using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// conncomp_reg.c: connected components (4 and 8 connected), regeneration of the
/// image from the components, boxa I/O, and iterative covering of components by
/// rectangles.
/// </summary>
public class ConnCompRegTests
{
    [RegFact]
    public void ConnComp()
    {
        int i, n1, n2, n3;
        Boxa boxa1, boxa2;
        Pix pix1, pix2, pix3;
        Pixa pixa1;
        var rp = new RegTest("conncomp");

        Pix pixs = TestPaths.ReadInput("feyn.tif");

        /* --------------------------------------------------------------- *
         *         Test pixConnComp() and pixCountConnComp(),              *
         *            with output to both boxa and pixa                    *
         * --------------------------------------------------------------- */
            /* First, test with 4-cc */
        boxa1 = pixs.ConnComp(out pixa1, 4);
        n1 = boxa1.Count;
        boxa2 = pixs.ConnComp(4);
        n2 = boxa2.Count;
        n3 = pixs.CountConnComp(4);
        rp.CompareValues(n1, n2, 0);  /* 0 */
        rp.CompareValues(n1, n3, 0);  /* 1 */
        rp.CompareValues(n1, 4452, 0);  /* 2 */
        pix1 = pixa1.Display(pixs.Width, pixs.Height);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 3 */
        rp.ComparePix(pixs, pix1);  /* 4 */

            /* Test with 8-cc */
        boxa1 = pixs.ConnComp(out pixa1, 8);
        n1 = boxa1.Count;
        boxa2 = pixs.ConnComp(8);
        n2 = boxa2.Count;
        n3 = pixs.CountConnComp(8);
        rp.CompareValues(n1, n2, 0);  /* 5 */
        rp.CompareValues(n1, n3, 0);  /* 6 */
        rp.CompareValues(n1, 4305, 0);  /* 7 */
        pix1 = pixa1.Display(pixs.Width, pixs.Height);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 8 */
        rp.ComparePix(pixs, pix1);  /* 9 */

        /* --------------------------------------------------------------- *
         *                        Test boxa I/O                            *
         * --------------------------------------------------------------- */
        Utils.LeptMkdir("lept/conn");
        boxa1 = pixs.ConnComp(4);
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/conn/boxa1.ba", "wb+"))
            boxa1.WriteStream(fp);
        using (FileStream fp = Utils.FopenReadStream("/tmp/lept/conn/boxa1.ba"))
            boxa2 = Boxa.ReadStream(fp);
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/conn/boxa2.ba", "wb+"))
            boxa2.WriteStream(fp);
        byte[] array1 = Utils.BinaryRead("/tmp/lept/conn/boxa1.ba");
        byte[] array2 = Utils.BinaryRead("/tmp/lept/conn/boxa2.ba");
        rp.CompareStrings(array1, array2);  /* 10 */

        /* --------------------------------------------------------------- *
         *    Just for fun, display each component as a random color in    *
         *    cmapped 8 bpp.  Background is color 0; it is set to white.   *
         * --------------------------------------------------------------- */
        boxa1 = pixs.ConnComp(out pixa1, 4);
        pix1 = pixa1.DisplayRandomCmap(pixs.Width, pixs.Height);
        PixColormap cmap = pix1.Colormap!;
        cmap.ResetColor(0, 255, 255, 255);  /* reset background to white */
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 11 */

        /* --------------------------------------------------------------- *
         *  Test iterative covering of connected components by rectangles  *
         * --------------------------------------------------------------- */
        pixa1 = Pixa.Create(0);
        pix1 = TestPaths.ReadInput("rabi.png");
        pix2 = pix1.ReduceRankBinaryCascade(1, 1, 1, 0);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 12 -  */
        pixa1.AddPix(pix2, L.L_INSERT);
        for (i = 1; i < 6; i++) {
            pix3 = pix2.MakeCoveringOfRectangles(i);
            rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 13 - 17 */
            pixa1.AddPix(pix3, L.L_INSERT);
        }
        pix3 = pixa1.DisplayTiledInRows(1, 2500, 1.0f, 0, 30, 0);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 18 */

        Assert.Equal(18, rp.Index);
        rp.Cleanup();
    }
}
