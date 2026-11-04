using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// blend5_reg.c: regression test for pixSnapColor() and pixLinearEdgeFade().
/// </summary>
public class Blend5RegTests
{
    private const uint LEPTONICA_YELLOW = 0xffffe400u;

    [RegFact]
    public void Blend5()
    {
        var rp = new RegTest("blend5");
        Pixa pixa = Pixa.Create(0);

            /* First, snap the color directly on the input rgb image. */
        Pix pixs = TestPaths.ReadInput("Leptonica.jpg");
        pixa.AddPix(pixs, L.L_COPY);
        Pix pix1 = Pix.SnapColor(null, pixs, 0xffffff00u, LEPTONICA_YELLOW, 30);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 0 */

            /* Then make a colormapped version and snap the color */
        pix1 = pixs.OctreeQuantNumColors(250, 0);
        pixa.AddPix(pix1, L.L_COPY);
        Pix.SnapColor(pix1, pix1, 0xffffff00u, LEPTONICA_YELLOW, 30);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 1 */

            /* Set the background of the google searchbox to yellow. */
        pixs = TestPaths.ReadInput("google-searchbox.png");
        pixa.AddPix(pixs, L.L_INSERT);
        pix1 = Pix.SnapColor(null, pixs, 0xffffff00u, LEPTONICA_YELLOW, 30);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 2 */

            /* A couple of more, setting pixels near white to strange colors */
        pixs = TestPaths.ReadInput("weasel4.11c.png");
        pixa.AddPix(pixs, L.L_INSERT);
        pix1 = Pix.SnapColor(null, pixs, 0xfefefe00u, 0x80800000u, 50);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 3 */

        pixs = TestPaths.ReadInput("wyom.jpg");
        pix1 = pixs.FixedOctcubeQuant256(0);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 4 */
        Pix pix2 = Pix.SnapColor(null, pix1, 0xf0f0f000u, 0x80008000u, 100);
        pixa.AddPix(pix2, L.L_INSERT);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 5 */

            /* --- Display results --- */
        pix1 = pixa.DisplayTiledInColumns(2, 1.0f, 25, 2);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 6 */

            /* Test linear fade to black */
        Pix.ComposeRGBPixel(90, 170, 145, out uint val32);
        pix1 = Pix.Create(300, 300, 32);
        pix1.SetAllArbitrary(val32);
        pix1.LinearEdgeFade(L.L_FROM_LEFT, L.L_BLEND_TO_BLACK, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_RIGHT, L.L_BLEND_TO_BLACK, 0.5f, 0.8f);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 7 */

        pix1 = Pix.Create(300, 300, 32);
        pix1.SetAllArbitrary(val32);
        pix1.LinearEdgeFade(L.L_FROM_TOP, L.L_BLEND_TO_BLACK, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_BOT, L.L_BLEND_TO_BLACK, 0.5f, 0.8f);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 8 */

        pix1 = Pix.Create(300, 300, 32);
        pix1.SetAllArbitrary(val32);
        pix1.LinearEdgeFade(L.L_FROM_LEFT, L.L_BLEND_TO_BLACK, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_RIGHT, L.L_BLEND_TO_BLACK, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_TOP, L.L_BLEND_TO_BLACK, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_BOT, L.L_BLEND_TO_BLACK, 0.5f, 0.8f);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 9 */

        pix1 = Pix.Create(300, 300, 8);   /* 8 bpp */
        pix1.SetAll();
        pix1.LinearEdgeFade(L.L_FROM_LEFT, L.L_BLEND_TO_BLACK, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_RIGHT, L.L_BLEND_TO_BLACK, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_TOP, L.L_BLEND_TO_BLACK, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_BOT, L.L_BLEND_TO_BLACK, 0.5f, 0.8f);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 10 */

            /* Test linear fade to white */
        Pix.ComposeRGBPixel(170, 110, 200, out val32);
        pix1 = Pix.Create(300, 300, 32);
        pix1.SetAllArbitrary(val32);
        pix1.LinearEdgeFade(L.L_FROM_LEFT, L.L_BLEND_TO_WHITE, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_RIGHT, L.L_BLEND_TO_WHITE, 0.5f, 0.8f);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 11 */

        pix1 = Pix.Create(300, 300, 32);
        pix1.SetAllArbitrary(val32);
        pix1.LinearEdgeFade(L.L_FROM_TOP, L.L_BLEND_TO_WHITE, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_BOT, L.L_BLEND_TO_WHITE, 0.5f, 0.8f);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 12 */

        pix1 = Pix.Create(300, 300, 32);
        pix1.SetAllArbitrary(val32);
        pix1.LinearEdgeFade(L.L_FROM_LEFT, L.L_BLEND_TO_WHITE, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_RIGHT, L.L_BLEND_TO_WHITE, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_TOP, L.L_BLEND_TO_WHITE, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_BOT, L.L_BLEND_TO_WHITE, 0.5f, 0.8f);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 13 */

        pix1 = Pix.Create(300, 300, 8);   /* 8 bpp */
        pix1.LinearEdgeFade(L.L_FROM_LEFT, L.L_BLEND_TO_WHITE, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_RIGHT, L.L_BLEND_TO_WHITE, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_TOP, L.L_BLEND_TO_WHITE, 0.5f, 0.8f);
        pix1.LinearEdgeFade(L.L_FROM_BOT, L.L_BLEND_TO_WHITE, 0.5f, 0.8f);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 14 */

        Assert.Equal(14, rp.Index);
        rp.Cleanup();
    }
}
