using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// translate_reg.c: pixTranslate() of images of different depths and with
/// white or black brought in.
/// </summary>
public class TranslateRegTests
{
    [RegFact]
    public void Translate()
    {
        Pix pixd;
        Pixa pixa;
        var rp = new RegTest("translate");

            /* Set up images */
        Pix pix1 = TestPaths.ReadInput("weasel2.4c.png");
        Pix pix2 = pix1.ScaleBySampling(3.0f, 3.0f);
        Box box = Box.Create(0, 0, 209, 214);
        Pix pixs = pix2.ClipRectangle(box, out _)!;
        pix1 = pixs.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        pix2 = pixs.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        Pix pix3 = pixs.ConvertTo1(128);
        Pix pix4 = pix1.RotateAM(0.25f, L.L_BRING_IN_BLACK);
        Pix pix5 = pix1.RotateAM(-0.25f, L.L_BRING_IN_WHITE);
        Pix pix6 = pix2.RotateAM(-0.15f, L.L_BRING_IN_BLACK);
        Pix pix7 = pix2.RotateAM(+0.15f, L.L_BRING_IN_WHITE);

        pixa = Pixa.Create(0);
        TranslateAndSave1(pixa, 32, pixs, 30, 30);
        TranslateAndSave1(pixa, 32, pix1, 35, 20);
        TranslateAndSave1(pixa, 32, pix2, 20, 35);
        TranslateAndSave1(pixa, 32, pix3, 20, 35);
        pixd = pixa.DisplayTiledInColumns(4, 1.0f, 30, 3);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 0 */

        pixa = Pixa.Create(0);
        TranslateAndSave1(pixa, 8, pix1, 35, 20);
        TranslateAndSave1(pixa, 8, pix4, 35, 20);
        pixd = pixa.DisplayTiledInColumns(4, 1.0f, 30, 3);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 1 */

        pixa = Pixa.Create(0);
        TranslateAndSave2(pixa, pixs, 30, 30);
        TranslateAndSave2(pixa, pix1, 30, 30);
        TranslateAndSave2(pixa, pix2, 35, 20);
        TranslateAndSave2(pixa, pix3, 20, 35);
        TranslateAndSave2(pixa, pix4, 25, 25);
        TranslateAndSave2(pixa, pix5, 25, 25);
        TranslateAndSave2(pixa, pix6, 25, 25);
        TranslateAndSave2(pixa, pix7, 25, 25);
        pixd = pixa.DisplayTiledInColumns(4, 1.0f, 30, 3);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 2 */

        Assert.Equal(2, rp.Index);
        rp.Cleanup();
    }

    private static void TranslateAndSave1(Pixa pixa, int depth, Pix pix, int xshift, int yshift)
    {
        Pix pix1 = Pix.Translate(null, pix, xshift, yshift, L.L_BRING_IN_WHITE);
        Pix pix2 = Pix.Translate(null, pix, xshift, yshift, L.L_BRING_IN_BLACK);
        Pix pix3 = Pix.Translate(null, pix, -xshift, -yshift, L.L_BRING_IN_WHITE);
        Pix pix4 = Pix.Translate(null, pix, -xshift, -yshift, L.L_BRING_IN_BLACK);
        pixa.AddPix(pix1, L.L_INSERT);
        pixa.AddPix(pix2, L.L_INSERT);
        pixa.AddPix(pix3, L.L_INSERT);
        pixa.AddPix(pix4, L.L_INSERT);
    }

    private static void TranslateAndSave2(Pixa pixa, Pix pix, int xshift, int yshift)
    {
        Pix pix1 = Pix.Translate(null, pix, xshift, yshift, L.L_BRING_IN_WHITE);
        Pix pix2 = Pix.Translate(null, pix, xshift, yshift, L.L_BRING_IN_BLACK);
        Pix pix3 = Pix.Translate(null, pix, -xshift, -yshift, L.L_BRING_IN_WHITE);
        Pix pix4 = Pix.Translate(null, pix, -xshift, -yshift, L.L_BRING_IN_BLACK);
        pixa.AddPix(pix1, L.L_INSERT);
        pixa.AddPix(pix2, L.L_INSERT);
        pixa.AddPix(pix3, L.L_INSERT);
        pixa.AddPix(pix4, L.L_INSERT);
    }
}
