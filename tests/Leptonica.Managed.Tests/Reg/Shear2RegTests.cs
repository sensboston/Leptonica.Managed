using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// shear2_reg.c: regression test for quadratic shear, both sampled and interpolated.
/// </summary>
public class Shear2RegTests
{
    [RegFact]
    public void Shear2()
    {
        var rp = new RegTest("shear2");

        L_Bmf bmf = L_Bmf.Create("./fonts", 8);
        Pix pixs1 = Pix.Create(301, 301, 32);
        Pix pixs2 = Pix.Create(601, 601, 32);
        pixs1.SetAll();
        pixs2.SetAll();
        pixs1.RenderLineArb(0, 20, 300, 20, 5, 0, 0, 255);
        pixs1.RenderLineArb(0, 70, 300, 70, 5, 0, 255, 0);
        pixs1.RenderLineArb(0, 120, 300, 120, 5, 0, 255, 255);
        pixs1.RenderLineArb(0, 170, 300, 170, 5, 255, 0, 0);
        pixs1.RenderLineArb(0, 220, 300, 220, 5, 255, 0, 255);
        pixs1.RenderLineArb(0, 270, 300, 270, 5, 255, 255, 0);
        pixs2.RenderLineArb(0, 20, 300, 20, 5, 0, 0, 255);
        pixs2.RenderLineArb(0, 70, 300, 70, 5, 0, 255, 0);
        pixs2.RenderLineArb(0, 120, 300, 120, 5, 0, 255, 255);
        pixs2.RenderLineArb(0, 170, 300, 170, 5, 255, 0, 0);
        pixs2.RenderLineArb(0, 220, 300, 220, 5, 255, 0, 255);
        pixs2.RenderLineArb(0, 270, 300, 270, 5, 255, 255, 0);

            /* Color, small pix */
        Pixa pixa = Pixa.Create(0);
        Pix pixt = pixs1.QuadraticVShear(L.L_WARP_TO_LEFT, 60, -20, L.L_SAMPLED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "sampled-left");
        pixt = pixs1.QuadraticVShear(L.L_WARP_TO_RIGHT, 60, -20, L.L_SAMPLED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "sampled-right");
        pixt = pixs1.QuadraticVShear(L.L_WARP_TO_LEFT, 60, -20, L.L_INTERPOLATED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "interpolated-left");
        pixt = pixs1.QuadraticVShear(L.L_WARP_TO_RIGHT, 60, -20, L.L_INTERPOLATED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "interpolated-right");
        Pix pixd = pixa.DisplayTiledInColumns(2, 1.0f, 20, 0);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 0 */

            /* Grayscale, small pix */
        Pix pixg = pixs1.ConvertTo8(false);
        pixa = Pixa.Create(0);
        pixt = pixg.QuadraticVShear(L.L_WARP_TO_LEFT, 60, -20, L.L_SAMPLED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "sampled-left");
        pixt = pixg.QuadraticVShear(L.L_WARP_TO_RIGHT, 60, -20, L.L_SAMPLED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "sampled-right");
        pixt = pixg.QuadraticVShear(L.L_WARP_TO_LEFT, 60, -20, L.L_INTERPOLATED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "interpolated-left");
        pixt = pixg.QuadraticVShear(L.L_WARP_TO_RIGHT, 60, -20, L.L_INTERPOLATED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "interpolated-right");
        pixd = pixa.DisplayTiledInColumns(2, 1.0f, 20, 0);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 1 */

            /* Color, larger pix */
        pixa = Pixa.Create(0);
        pixt = pixs2.QuadraticVShear(L.L_WARP_TO_LEFT, 120, -40, L.L_SAMPLED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "sampled-left");
        pixt = pixs2.QuadraticVShear(L.L_WARP_TO_RIGHT, 120, -40, L.L_SAMPLED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "sampled-right");
        pixt = pixs2.QuadraticVShear(L.L_WARP_TO_LEFT, 120, -40, L.L_INTERPOLATED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "interpolated-left");
        pixt = pixs2.QuadraticVShear(L.L_WARP_TO_RIGHT, 120, -40, L.L_INTERPOLATED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "interpolated-right");
        pixd = pixa.DisplayTiledInColumns(2, 1.0f, 20, 0);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 2 */

            /* Grayscale, larger pix */
        pixg = pixs2.ConvertTo8(false);
        pixa = Pixa.Create(0);
        pixt = pixg.QuadraticVShear(L.L_WARP_TO_LEFT, 60, -20, L.L_SAMPLED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "sampled-left");
        pixt = pixg.QuadraticVShear(L.L_WARP_TO_RIGHT, 60, -20, L.L_SAMPLED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "sampled-right");
        pixt = pixg.QuadraticVShear(L.L_WARP_TO_LEFT, 60, -20, L.L_INTERPOLATED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "interpolated-left");
        pixt = pixg.QuadraticVShear(L.L_WARP_TO_RIGHT, 60, -20, L.L_INTERPOLATED, L.L_BRING_IN_WHITE);
        PixSave(pixt, pixa, bmf, "interpolated-right");
        pixd = pixa.DisplayTiledInColumns(2, 1.0f, 20, 0);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 3 */

        Assert.Equal(3, rp.Index);
        rp.Cleanup();
    }

    private static void PixSave(Pix pixs, Pixa pixa, L_Bmf bmf, string textstr)
    {
        Pix pix1 = pixs.ConvertTo32();
        Pix pix2 = pix1.AddBorder(3, 0);
        Pix pix3 = pix2.AddSingleTextblock(bmf, textstr, 0xff000000, L.L_ADD_BELOW, out _);
        pixa.AddPix(pix3, L.L_INSERT);
    }
}
