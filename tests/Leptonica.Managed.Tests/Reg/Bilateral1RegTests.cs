using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// bilateral1_reg.c: bilateral (nonlinear) filtering, both separable with full
/// resolution intermediate images and exact.
/// </summary>
public class Bilateral1RegTests
{
    private const int ncomps = 10;

    [RegFact]
    public void Bilateral1()
    {
        var rp = new RegTest("bilateral1");

        Pix pixs = TestPaths.ReadInput("rock.png");
        DoTestsOnImage(pixs, rp, 2000);  /* 0 - 16 */

        pixs = TestPaths.ReadInput("church.png");
        DoTestsOnImage(pixs, rp, 1500);  /* 17 - 33 */

        pixs = TestPaths.ReadInput("color-wheel-hue.jpg");
        DoTestsOnImage(pixs, rp, 1000);  /* 34 - 50 */

        Assert.Equal(50, rp.Index);
        rp.Cleanup();
    }

    private static void DoTestsOnImage(Pix pixs, RegTest rp, int width)
    {
        Pix pix, pixd;
        Pixa pixa;

        pixa = Pixa.Create(0);
        pix = pixs.Bilateral(5.0f, 10.0f, ncomps, 1);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(5.0f, 20.0f, ncomps, 1);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(5.0f, 40.0f, ncomps, 1);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(5.0f, 60.0f, ncomps, 1);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(10.0f, 10.0f, ncomps, 1);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(10.0f, 20.0f, ncomps, 1);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(10.0f, 40.0f, ncomps, 1);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(10.0f, 60.0f, ncomps, 1);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(10.0f, 10.0f, ncomps, 2);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(10.0f, 20.0f, ncomps, 2);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(10.0f, 40.0f, ncomps, 2);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(10.0f, 60.0f, ncomps, 2);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.BlockBilateralExact(10.0f, 10.0f);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.BlockBilateralExact(10.0f, 20.0f);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.BlockBilateralExact(10.0f, 40.0f);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.BlockBilateralExact(10.0f, 60.0f);
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pixd = pixa.DisplayTiledInRows(32, width, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);
    }
}
