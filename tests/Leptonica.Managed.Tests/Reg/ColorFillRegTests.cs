using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// colorfill_reg.c: the utility that does color segmentation by region growing.
/// </summary>
public class ColorFillRegTests
{
    [RegFact]
    public void ColorFill()
    {
        L_Colorfill cf;
        Pix pix1, pix2, pix3, pix4;
        Pixa pixa1, pixa2;
        var rp = new RegTest("colorfill");
        CRand.Seed(1);  /* a fresh C process starts with srand(1); the state is per thread */

            /* Test on a small image */
        pix1 = MakeSmallTestPix(0x3070A000, 0xA0703000);
        pix2 = pix1.ExpandReplicate(15);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 0 */
        cf = L_Colorfill.Create(pix1, 1, 1);
        Pix.ColorContentByLocation(cf, 0, 0, 0, 70, 15, 3, 1, 1);
        pix2 = cf.Pixadb.DisplayTiledInColumns(cf.Nx, 1.0f, 10, 1);
        pix3 = pix2.ExpandReplicate(10);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 1 */

            /* Test on simple random image with many colors (1 tile and 4 tiles */
        pixa1 = Pix.MakeColorfillTestData(350, 350, 35, 100);
        pixa2 = Pixa.Create(5);
        pix1 = pixa1.GetPix(0, L.L_COPY);
        pix2 = pix1.ConvertTo32();
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 2 */
        pixa2.AddPix(pix2, L.L_INSERT);
        cf = L_Colorfill.Create(pix2, 1, 1);  /* 1 tile */
        Pix.ColorContentByLocation(cf, 0, 0, 0, 70, 30, 500, 1, 1);
        pix3 = cf.Pixam!.DisplayTiledInColumns(cf.Nx, 1.0f, 10, 1);
        pix4 = cf.Pixadb.DisplayTiledInColumns(cf.Nx, 1.0f, 10, 1);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 3 */
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 4 */
        pixa2.AddPix(pix3, L.L_INSERT);
        pixa2.AddPix(pix4, L.L_INSERT);

        cf = L_Colorfill.Create(pix2, 2, 2);  /* 4 tiles */
        Pix.ColorContentByLocation(cf, 0, 0, 0, 70, 30, 500, 1, 1);
        pix3 = cf.Pixam!.DisplayTiledInColumns(cf.Nx, 1.0f, 10, 1);
        pix4 = cf.Pixadb.DisplayTiledInColumns(cf.Nx, 1.0f, 10, 1);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 5 */
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 6 */
        pixa2.AddPix(pix3, L.L_INSERT);
        pixa2.AddPix(pix4, L.L_INSERT);

            /* Test on an image with lots of color (with 1 tile and 9 tiles) */
        pix1 = TestPaths.ReadInput("lyra.005.jpg");
        pix2 = pix1.Scale(0.5f, 0.5f);
        cf = L_Colorfill.Create(pix2, 1, 1);  /* 1 tile */
        Pix.ColorContentByLocation(cf, 0, 0, 0, 70, 30, 100, 1, 1);
        pix3 = cf.Pixam!.DisplayTiledInColumns(cf.Nx, 1.0f, 10, 1);
        pix4 = cf.Pixadb.DisplayTiledInColumns(cf.Nx, 1.0f, 10, 1);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 7 */
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 8 */
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 9 */

        cf = L_Colorfill.Create(pix2, 3, 3);  /* 9 tiles */
        Pix.ColorContentByLocation(cf, 0, 0, 0, 70, 30, 100, 1, 1);
        pix3 = cf.Pixam!.DisplayTiledInColumns(cf.Nx, 1.0f, 10, 1);
        pix4 = cf.Pixadb.DisplayTiledInColumns(cf.Nx, 1.0f, 10, 1);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 10 */
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 11 */

        Assert.Equal(11, rp.Index);
        rp.Cleanup();
    }

    private static Pix MakeSmallTestPix(uint c1, uint c2)
    {
        int i, j;
        Pix pix1;

        pix1 = Pix.Create(17, 17, 32);
        pix1.SetAllArbitrary(c1);
        for (i = 0; i < 15; i++) {
            for (j = 0; j < i; j++)
                pix1.SetPixel(j, i, c2);
        }
        for (i = 0; i < 15; i++) {
            for (j = 17 - i; j < 17; j++)
                pix1.SetPixel(j, i, c2);
        }
        for (i = 9; i < 17; i++)
            pix1.SetPixel(8, i, c1);
        return pix1;
    }
}
