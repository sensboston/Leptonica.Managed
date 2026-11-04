using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// adaptmap_reg.c: adaptive mappings in both gray and color, and pixFillMapHoles().
/// </summary>
public class AdaptMapRegTests
{
        /* Location of image region in wet-day.jpg */
    private const int XS = 151;
    private const int YS = 225;
    private const int WS = 913;
    private const int HS = 1285;

    private const int SIZE_X = 10;
    private const int SIZE_Y = 30;
    private const int BINTHRESH = 50;
    private const int MINCOUNT = 30;

    private const int BGVAL = 200;
    private const int SMOOTH_X = 2;
    private const int SMOOTH_Y = 1;

    [RegFact]
    public void AdaptMap()
    {
        var rp = new RegTest("adaptmap");

        Pix pixs = TestPaths.ReadInput("wet-day.jpg");
        Pixa pixa = Pixa.Create(0);
        Pix pixg = pixs.ConvertRGBToGray(0.33f, 0.34f, 0.33f);
        pixa.AddPix(pixs, L.L_INSERT);
        pixa.AddPix(pixg, L.L_INSERT);
        pixs.GetDimensions(out int w, out int h, out _);

            /* Process in grayscale */
        Pix pixim = Pix.Create(w, h, 1);
        pixim.Rasterop(XS, YS, WS, HS, L.PIX_SET, null, 0, 0);
        Pix pixgm = pixg.GetBackgroundGrayMap(pixim, SIZE_X, SIZE_Y, BINTHRESH, MINCOUNT)!;
        rp.WritePixAndCheck(pixgm, L.IFF_PNG);  /* 0 */
        pixa.AddPix(pixgm, L.L_INSERT);

        Pix pixmi = pixgm.GetInvBackgroundMap(BGVAL, SMOOTH_X, SMOOTH_Y);
        rp.WritePixAndCheck(pixmi, L.IFF_PNG);  /* 1 */
        pixa.AddPix(pixmi, L.L_INSERT);

        Pix pix1 = pixg.ApplyInvBackgroundGrayMap(pixmi, SIZE_X, SIZE_Y);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 2 */
        pixa.AddPix(pix1, L.L_INSERT);

        Pix pix2 = Pix.GammaTRCMasked(null, pix1, pixim, 1.0f, 0, 190);
        Pix.Invert(pixim, pixim);
        Pix.GammaTRCMasked(pix2, pix2, pixim, 1.0f, 60, 190);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 3 */
        pixa.AddPix(pix2, L.L_INSERT);

            /* Process in color */
        pixim = Pix.Create(w, h, 1);
        pixim.Rasterop(XS, YS, WS, HS, L.PIX_SET, null, 0, 0);
        pixs.GetBackgroundRGBMap(pixim, null, SIZE_X, SIZE_Y, BINTHRESH, MINCOUNT,
                                 out Pix? pixmr, out Pix? pixmg, out Pix? pixmb);
        rp.WritePixAndCheck(pixmr!, L.IFF_PNG);  /* 4 */
        rp.WritePixAndCheck(pixmg!, L.IFF_PNG);  /* 5 */
        rp.WritePixAndCheck(pixmb!, L.IFF_PNG);  /* 6 */
        pixa.AddPix(pixmr!, L.L_INSERT);
        pixa.AddPix(pixmg!, L.L_INSERT);
        pixa.AddPix(pixmb!, L.L_INSERT);

        Pix pixmri = pixmr!.GetInvBackgroundMap(BGVAL, SMOOTH_X, SMOOTH_Y);
        Pix pixmgi = pixmg!.GetInvBackgroundMap(BGVAL, SMOOTH_X, SMOOTH_Y);
        Pix pixmbi = pixmb!.GetInvBackgroundMap(BGVAL, SMOOTH_X, SMOOTH_Y);
        rp.WritePixAndCheck(pixmri, L.IFF_PNG);  /* 7 */
        rp.WritePixAndCheck(pixmgi, L.IFF_PNG);  /* 8 */
        rp.WritePixAndCheck(pixmbi, L.IFF_PNG);  /* 9 */
        pixa.AddPix(pixmri, L.L_INSERT);
        pixa.AddPix(pixmgi, L.L_INSERT);
        pixa.AddPix(pixmbi, L.L_INSERT);

        pix1 = pixs.ApplyInvBackgroundRGBMap(pixmri, pixmgi, pixmbi, SIZE_X, SIZE_Y);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 10 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix2 = Pix.GammaTRCMasked(null, pix1, pixim, 1.0f, 0, 190);
        Pix.Invert(pixim, pixim);
        Pix.GammaTRCMasked(pix2, pix2, pixim, 1.0f, 60, 190);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 11 */
        pixa.AddPix(pix2, L.L_INSERT);

            /* Process at higher level in color */
        pixim = Pix.Create(w, h, 1);
        pixim.Rasterop(XS, YS, WS, HS, L.PIX_SET, null, 0, 0);
        pix1 = pixs.BackgroundNorm(pixim, null, 5, 10, BINTHRESH, 20,
                                   BGVAL, SMOOTH_X, SMOOTH_Y);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 12 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix2 = Pix.GammaTRCMasked(null, pix1, pixim, 1.0f, 0, 190);
        Pix.Invert(pixim, pixim);
        Pix.GammaTRCMasked(pix2, pix2, pixim, 1.0f, 60, 190);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 13 */
        pixa.AddPix(pix2, L.L_INSERT);

            /* Check pixFillMapHoles() */
        pixa = Pixa.Create(3);
        pix1 = TestPaths.ReadInput("weasel8.png");  /* use this as the map */
        Pix.GammaTRC(pix1, pix1, 1.0f, 0, 270);  /* darken white pixels */
        pixa.AddPix(pix1, L.L_COPY);
        pix1.GetDimensions(out w, out h, out _);
        pix1.Rasterop(0, 0, 5, h, L.PIX_SET, null, 0, 0);  /* add white holes */
        pix1.Rasterop(20, 0, 2, h, L.PIX_SET, null, 0, 0);
        pix1.Rasterop(40, 0, 3, h, L.PIX_SET, null, 0, 0);
        pix1.Rasterop(0, 0, w, 3, L.PIX_SET, null, 0, 0);
        pix1.Rasterop(0, 15, w, 3, L.PIX_SET, null, 0, 0);
        pix1.Rasterop(0, 35, w, 2, L.PIX_SET, null, 0, 0);
        pixa.AddPix(pix1, L.L_COPY);
        pix1.FillMapHoles(w, h, L.L_FILL_WHITE);
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = pixa.DisplayTiledInColumns(3, 1.0f, 20, 1);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 14 */

            /* An even simpler check of pixFillMapHoles() */
        pixa = Pixa.Create(2);
        pix1 = Pix.Create(3, 3, 8);
        pix1.SetPixel(1, 0, 128);
        pix2 = pix1.ExpandReplicate(25);
        pixa.AddPix(pix2, L.L_INSERT);
        pix1.FillMapHoles(3, 3, L.L_FILL_BLACK);
        pix2 = pix1.ExpandReplicate(25);
        pixa.AddPix(pix2, L.L_INSERT);
        pix1 = pixa.DisplayTiledInColumns(2, 1.0f, 20, 0);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 15 */

        Assert.Equal(15, rp.Index);
        rp.Cleanup();
    }
}
