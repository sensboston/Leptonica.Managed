using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// smallpix_reg.c: scaling and rotation of a very small pix; where, if anywhere,
/// do we add 0.5 in the quantization?
/// </summary>
public class SmallPixRegTests
{
    [RegFact]
    public void SmallPix()
    {
        int i;
        float pi, scale, angle;
        Pix pixc, pixm, pix1, pix2, pix3;
        Pixa pixa;
        Pta pta1, pta2, pta3, pta4;
        var rp = new RegTest("smallpix");

            /* Make a small test image, the hard way! */
        pi = (float)3.1415926535;
        pixc = Pix.Create(9, 9, 32);
        pixm = Pix.Create(9, 9, 1);
        pta1 = Pix.GeneratePtaLineFromPt(4, 4, 3.1, 0.0);
        pta2 = Pix.GeneratePtaLineFromPt(4, 4, 3.1, 0.5 * pi);
        pta3 = Pix.GeneratePtaLineFromPt(4, 4, 3.1, pi);
        pta4 = Pix.GeneratePtaLineFromPt(4, 4, 3.1, 1.5 * pi);
        pta1.Join(pta2, 0, -1);
        pta1.Join(pta3, 0, -1);
        pta1.Join(pta4, 0, -1);
        pixm.RenderPta(pta1, L.L_SET_PIXELS);
        pixc.PaintThroughMask(pixm, 0, 0, 0x00ff0000);

            /* Results differ for scaleSmoothLow() w/ and w/out + 0.5. */
        pixa = Pixa.Create(11);
        pix1 = pixc.ExpandReplicate(2);
        for (i = 0; i < 11; i++) {
            scale = (float)(0.30 + 0.035 * (float)i);
            pix2 = pix1.ScaleSmooth(scale, scale);
            pix3 = pix2.ExpandReplicate(6);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        SaveAndDisplayPix(rp, pixa);  /* 0 */

        pixa = Pixa.Create(11);
        pix1 = pixc.ExpandReplicate(2);
        for (i = 0; i < 11; i++) {
            scale = (float)(0.30 + 0.035 * (float)i);
            pix2 = pix1.ScaleAreaMap(scale, scale);
            pix3 = pix2.ExpandReplicate(6);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        SaveAndDisplayPix(rp, pixa);  /* 1 */

        pixa = Pixa.Create(11);
        pix1 = pixc.ExpandReplicate(2);
        for (i = 0; i < 11; i++) {
            scale = (float)(0.30 + 0.035 * (float)i);
            pix2 = pix1.ScaleBySampling(scale, scale);
            pix3 = pix2.ExpandReplicate(6);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        SaveAndDisplayPix(rp, pixa);  /* 2 */

        pixa = Pixa.Create(11);
        pix1 = pixc.ExpandReplicate(1);
        for (i = 0; i < 11; i++) {
            angle = (float)(0.10 + 0.05 * (float)i);
            pix2 = pix1.RotateAM(angle, L.L_BRING_IN_BLACK);
            pix3 = pix2.ExpandReplicate(8);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        SaveAndDisplayPix(rp, pixa);  /* 3 */

        pixa = Pixa.Create(11);
        pix1 = pixc.ExpandReplicate(1);
        for (i = 0; i < 11; i++) {
            angle = (float)(0.10 + 0.05 * (float)i);
            pix2 = pix1.RotateBySampling(4, 4, angle, L.L_BRING_IN_BLACK);
            pix3 = pix2.ExpandReplicate(8);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        SaveAndDisplayPix(rp, pixa);  /* 4 */

        pixa = Pixa.Create(11);
        pix1 = pixc.ExpandReplicate(1);
        for (i = 0; i < 11; i++) {
            angle = (float)(0.10 + 0.05 * (float)i);
            pix2 = pix1.RotateAMCorner(angle, L.L_BRING_IN_BLACK);
            pix3 = pix2.ExpandReplicate(8);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        SaveAndDisplayPix(rp, pixa);  /* 5 */

        pixa = Pixa.Create(11);
        pix1 = pixc.ExpandReplicate(1);
        for (i = 0; i < 11; i++) {
            angle = (float)(0.10 + 0.05 * (float)i);
            pix2 = pix1.RotateAMColorFast(angle, 0);
            pix3 = pix2.ExpandReplicate(8);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        SaveAndDisplayPix(rp, pixa);  /* 6 */

        pixa = Pixa.Create(11);
        pix1 = pixc.ExpandReplicate(1);
        for (i = 0; i < 11; i++) {
            scale = (float)(1.0 + 0.2 * (float)i);
            pix2 = pix1.ScaleColorLI(scale, scale);
            pix3 = pix2.ExpandReplicate(4);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        SaveAndDisplayPix(rp, pixa);  /* 7 */

        pixa = Pixa.Create(11);
        pix1 = pixc.ExpandReplicate(1);
        for (i = 0; i < 11; i++) {
            scale = (float)(1.0 + 0.2 * (float)i);
            pix2 = pix1.ScaleLI(scale, scale);
            pix3 = pix2.ExpandReplicate(4);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        SaveAndDisplayPix(rp, pixa);  /* 8 */

        Assert.Equal(8, rp.Index);
        rp.Cleanup();
    }

    private static void SaveAndDisplayPix(RegTest rp, Pixa pixa)
    {
        Pix pix1 = pixa.DisplayTiledInColumns(12, 1.0f, 20, 0);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);
    }
}
