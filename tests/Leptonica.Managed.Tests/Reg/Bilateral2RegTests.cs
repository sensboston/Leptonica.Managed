using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// bilateral2_reg.c: bilateral (nonlinear) filtering, separable with
/// intermediate images at 4x reduction.
/// </summary>
public class Bilateral2RegTests
{
    private const int ncomps = 10;

    [RegFact]
    public void Bilateral2()
    {
        var rp = new RegTest("bilateral2");

        Pix pixs = TestPaths.ReadInput("test24.jpg");
        DoTestsOnImage(pixs, rp);  /* 0 - 7 */

        Assert.Equal(7, rp.Index);
        rp.Cleanup();
    }

    private static void DoTestsOnImage(Pix pixs, RegTest rp)
    {
        Pix pix;
        Pixa pixa;

        pixa = Pixa.Create(0);
        pix = pixs.Bilateral(5.0f, 10.0f, ncomps, 4);  /* 0 */
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(5.0f, 20.0f, ncomps, 4);  /* 1 */
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(5.0f, 40.0f, ncomps, 4);  /* 2 */
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(5.0f, 60.0f, ncomps, 4);  /* 3 */
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(10.0f, 10.0f, ncomps, 4);  /* 4 */
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(10.0f, 20.0f, ncomps, 4);  /* 5 */
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(10.0f, 40.0f, ncomps, 4);  /* 6 */
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
        pix = pixs.Bilateral(10.0f, 60.0f, ncomps, 4);  /* 7 */
        rp.WritePixAndCheck(pix, L.IFF_JFIF_JPEG);
        pixa.AddPix(pix, L.L_INSERT);
    }
}
