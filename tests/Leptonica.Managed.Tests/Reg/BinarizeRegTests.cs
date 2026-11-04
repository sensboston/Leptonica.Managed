using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// binarize_reg.c: Sauvola binarization with and without tiling, contrast
/// normalization followed by Sauvola, and double normalization with thresholding.
/// </summary>
public class BinarizeRegTests
{
    [RegFact]
    public void Binarize()
    {
        Pix pix1, pix2;
        Pixa pixa;
        var rp = new RegTest("binarize");

        Pix pixs = TestPaths.ReadInput("w91frag.jpg");

            /* Compare Sauvola binarization with and without tiles */
        pix1 = PixTest1(pixs, 7, 0.34f, rp);  /* 0, 1 */
        pix2 = PixTest2(pixs, 7, 0.34f, 4, 4, rp);  /* 2, 3 */
        rp.ComparePix(pix1, pix2);  /* 4 */

            /* More comparisons of Sauvola with and without tiles. */
        PixTest3(pixs, 3, 0.20f, 2, 3, 0, rp);  /* 5 - 9 */
        PixTest3(pixs, 6, 0.20f, 100, 100, 1, rp);  /* 10 - 14 */
        PixTest3(pixs, 10, 0.40f, 10, 10, 2, rp);  /* 15 - 19 */
        PixTest3(pixs, 10, 0.40f, 20, 20, 3, rp);  /* 20 - 24 */
        PixTest3(pixs, 20, 0.34f, 30, 30, 4, rp);  /* 25 - 29 */

            /* Contrast normalization followed by Sauvola */
        pixa = Pixa.Create(0);
        pix1 = pixs.SauvolaOnContrastNorm(130, out _, out _);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 30 */
        pixa.AddPix(pix1, L.L_INSERT);

           /* Contrast normalization followed by background normalization
            * and thresholding. */
        pix1 = pixs.ThreshOnDoubleNorm(130);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 31 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = pixa.DisplayTiledInColumns(2, 0.5f, 30, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 32 */

        Assert.Equal(32, rp.Index);
        rp.Cleanup();
    }

    private static Pix PixTest1(Pix pixs, int size, float factor, RegTest rp)
    {
            /* Get results witout tiling */
        pixs.SauvolaBinarize(size, factor, 1, out Pix pixm, out Pix pixsd, out Pix pixth, out Pix pixd);
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(pixm, L.L_INSERT);
        pixa.AddPix(pixsd, L.L_INSERT);
        pixa.AddPix(pixth, L.L_INSERT);
        pixa.AddPix(pixd, L.L_COPY);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);
        Pix pix1 = pixa.DisplayTiledInColumns(2, 1.0f, 30, 2);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);
        return pixd;
    }

    private static Pix PixTest2(Pix pixs, int size, float factor, int nx, int ny, RegTest rp)
    {
            /* Get results with tiling */
        pixs.SauvolaBinarizeTiled(size, factor, nx, ny, out Pix pixth, out Pix pixd);
        rp.WritePixAndCheck(pixth, L.IFF_JFIF_JPEG);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);
        return pixd;
    }

    private static void PixTest3(Pix pixs, int size, float factor, int nx, int ny,
                                 int paircount, RegTest rp)
    {
            /* Compare with and without tiling */
        Pix pix1 = PixTest1(pixs, size, factor, rp);
        Pix pix2 = PixTest2(pixs, size, factor, nx, ny, rp);
        rp.ComparePix(pix1, pix2);
    }
}
