using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// blackwhite_reg.c: functions that handle black and white pixels in an image.
/// </summary>
public class BlackWhiteRegTests
{
    private static readonly string[] fnames = {"test1.png", "speckle2.png", "weasel2.4g.png",
                                               "speckle4.png", "weasel4.11c.png",
                                               "dreyfus8.png", "weasel8.240c.png",
                                               "test16.tif", "marge.jpg",
                                               "test-cmap-alpha.png", "test-gray-alpha.png"};
    private const int setsize = 11;

    [RegFact]
    public void BlackWhite()
    {
        int i, spp;
        uint bval, wval;
        Pix pixs, pix1, pix2, pix3, pixd;
        Pixa pixa;
        var rp = new RegTest("blackwhite");

            /* Scale each image and add a white boundary */
        pixa = Pixa.Create(setsize);
        for (i = 0; i < setsize; i++) {
            pixs = fnames[i] == "test16.tif" ? TestPaths.ReadInput16(fnames[i])
                                             : TestPaths.ReadInput(fnames[i]);
            spp = pixs.Spp;
            wval = pixs.GetBlackOrWhiteVal(L.L_GET_WHITE_VAL);
            bval = pixs.GetBlackOrWhiteVal(L.L_GET_BLACK_VAL);
            if (spp == 4)  /* remove alpha, using white background */
                pix1 = pixs.AlphaBlendUniform(wval);
            else
                pix1 = Pix.Clone(pixs);
            pix2 = pix1.ScaleToSize(150, 150);
            wval = pix2.GetBlackOrWhiteVal(L.L_GET_WHITE_VAL);
            pix3 = pix2.AddBorderGeneral(30, 30, 20, 20, wval);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        pixd = pixa.DisplayTiledInRows(32, 1200, 1.0f, 1, 30, 0);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 0 */

            /* Scale each image and add a black boundary */
        pixa = Pixa.Create(setsize);
        for (i = 0; i < setsize; i++) {
            pixs = fnames[i] == "test16.tif" ? TestPaths.ReadInput16(fnames[i])
                                             : TestPaths.ReadInput(fnames[i]);
            spp = pixs.Spp;
            wval = pixs.GetBlackOrWhiteVal(L.L_GET_WHITE_VAL);
            bval = pixs.GetBlackOrWhiteVal(L.L_GET_BLACK_VAL);
            if (spp == 4)  /* remove alpha, using white background */
                pix1 = pixs.AlphaBlendUniform(wval);
            else
                pix1 = Pix.Clone(pixs);
            pix2 = pix1.ScaleToSize(150, 150);
            bval = pixs.GetBlackOrWhiteVal(L.L_GET_BLACK_VAL);
            pix3 = pix2.AddBorderGeneral(30, 30, 20, 20, bval);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        pixd = pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 30, 0);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 1 */

        Assert.Equal(1, rp.Index);
        rp.Cleanup();
    }
}
