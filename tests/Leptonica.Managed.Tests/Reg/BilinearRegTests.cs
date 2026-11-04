using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// bilinear_reg.c: tests bilinear transforms, including invertability and large distortions.
/// </summary>
public class BilinearRegTests
{
        /* Sample values.
         *    1: test with relatively large distortion
         *    2-3: invertability tests
         */
    private static readonly int[] x1 = {  32,   32,   32};
    private static readonly int[] y1 = { 150,  150,  150};
    private static readonly int[] x2 = { 520,  520,  520};
    private static readonly int[] y2 = { 150,  150,  150};
    private static readonly int[] x3 = {  32,   32,   32};
    private static readonly int[] y3 = { 612,  612,  612};
    private static readonly int[] x4 = { 520,  520,  520};
    private static readonly int[] y4 = { 612,  612,  612};

    private static readonly int[] xp1 = {  32,   32,   32};
    private static readonly int[] yp1 = { 150,  150,  150};
    private static readonly int[] xp2 = { 520,  520,  520};
    private static readonly int[] yp2 = {  44,  124,  140};
    private static readonly int[] xp3 = {  32,   32,   32};
    private static readonly int[] yp3 = { 612,  612,  612};
    private static readonly int[] xp4 = { 520,  520,  520};
    private static readonly int[] yp4 = { 694,  624,  622};

    private const int ADDED_BORDER_PIXELS = 250;

    [RegFact]
    public void Bilinear()
    {
        int i;
        Pix pixs, pix1, pix2, pix3, pix4, pixd;
        Pix pixb, pixg, pixc, pixcs;
        Pixa pixa;
        Pta ptas, ptad;
        var rp = new RegTest("bilinear");

        pixs = TestPaths.ReadInput("feyn.tif");
        pixg = pixs.ScaleToGray(0.2f);

            /* Test non-invertability of sampling */
        pixa = Pixa.Create(0);
        for (i = 1; i < 3; i++) {
            pixb = pixg.AddBorder(ADDED_BORDER_PIXELS, 255);
            MakePtas(i, out ptas, out ptad);
            pix1 = pixb.BilinearSampledPta(ptad, ptas, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0,3,6 */
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pix1.BilinearSampledPta(ptas, ptad, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 1,4,7 */
            pixa.AddPix(pix2, L.L_INSERT);
            pixd = pix2.RemoveBorder(ADDED_BORDER_PIXELS);
            Pix.Invert(pixd, pixd);
            Pix.Xor(pixd, pixd, pixg);
            rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 2,5,8 */
            pixa.AddPix(pixd, L.L_INSERT);
        }

        pix1 = pixa.DisplayTiledInColumns(3, 0.5f, 20, 3);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 9 */

            /* Test invertability of grayscale interpolation */
        pixa = Pixa.Create(0);
        for (i = 1; i < 3; i++) {
            pixb = pixg.AddBorder(ADDED_BORDER_PIXELS, 255);
            MakePtas(i, out ptas, out ptad);
            pix1 = pixb.BilinearPta(ptad, ptas, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 10,13 */
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pix1.BilinearPta(ptas, ptad, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 11,14 */
            pixa.AddPix(pix2, L.L_INSERT);
            pixd = pix2.RemoveBorder(ADDED_BORDER_PIXELS);
            Pix.Invert(pixd, pixd);
            Pix.Xor(pixd, pixd, pixg);
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 12,15 */
            pixa.AddPix(pixd, L.L_INSERT);
        }

        pix1 = pixa.DisplayTiledInColumns(3, 0.5f, 20, 3);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 16 */

            /* Test invertability of color interpolation */
        pixa = Pixa.Create(0);
        pixc = TestPaths.ReadInput("test24.jpg");
        pixcs = pixc.Scale(0.3f, 0.3f);
        for (i = 1; i < 3; i++) {
            pixb = pixcs.AddBorder(ADDED_BORDER_PIXELS / 2, 0xffffff00);
            MakePtas(i, out ptas, out ptad);
            pix1 = pixb.BilinearPta(ptad, ptas, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 17,20 */
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pix1.BilinearPta(ptas, ptad, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 18,21 */
            pixa.AddPix(pix2, L.L_INSERT);
            pixd = pix2.RemoveBorder(ADDED_BORDER_PIXELS / 2);
            Pix.Xor(pixd, pixd, pixc);
            Pix.Invert(pixd, pixd);
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 19,22 */
            pixa.AddPix(pixd, L.L_INSERT);
        }

        pix1 = pixa.DisplayTiledInColumns(3, 0.5f, 20, 3);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 23 */

            /* Comparison between sampling and interpolated */
        MakePtas(2, out ptas, out ptad);
        pixa = Pixa.Create(0);

            /* Use sampled transform */
        pix1 = pixg.BilinearSampledPta(ptas, ptad, L.L_BRING_IN_WHITE);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 24 */
        pixa.AddPix(pix1, L.L_INSERT);

            /* Use interpolated transforms */
        pix2 = pixg.BilinearPta(ptas, ptad, L.L_BRING_IN_WHITE);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 25 */
        pixa.AddPix(pix2, L.L_COPY);

            /* Compare the results */
        Pix.Xor(pix2, pix2, pix1);
        Pix.Invert(pix2, pix2);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 26 */
        pixa.AddPix(pix2, L.L_INSERT);

        pix1 = pixa.DisplayTiledInColumns(3, 0.5f, 20, 3);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 27 */

            /* Large distortion with inversion */
        MakePtas(0, out ptas, out ptad);
        pixa = Pixa.Create(0);
        pixs = TestPaths.ReadInput("marge.jpg");
        pixg = pixs.ConvertTo8(false);

        pix1 = pixg.BilinearSampledPta(ptas, ptad, L.L_BRING_IN_WHITE);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 28 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix2 = pixg.BilinearPta(ptas, ptad, L.L_BRING_IN_WHITE);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 29 */
        pixa.AddPix(pix2, L.L_INSERT);

        pix3 = pix1.BilinearSampledPta(ptad, ptas, L.L_BRING_IN_WHITE);
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 30 */
        pixa.AddPix(pix3, L.L_INSERT);

        pix4 = pix2.BilinearPta(ptad, ptas, L.L_BRING_IN_WHITE);
        rp.WritePixAndCheck(pix4, L.IFF_JFIF_JPEG);  /* 31 */
        pixa.AddPix(pix4, L.L_INSERT);

        pix1 = pixa.DisplayTiledInColumns(4, 1.0f, 20, 0);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 32 */

        Assert.Equal(29, rp.Index);  // the C index comments above are off by 3 (loops run for i = 1, 2)
        rp.Cleanup();
    }

    private static void MakePtas(int i, out Pta pptas, out Pta pptad)
    {
        pptas = Pta.Create(4);
        pptas.AddPt(x1[i], y1[i]);
        pptas.AddPt(x2[i], y2[i]);
        pptas.AddPt(x3[i], y3[i]);
        pptas.AddPt(x4[i], y4[i]);
        pptad = Pta.Create(4);
        pptad.AddPt(xp1[i], yp1[i]);
        pptad.AddPt(xp2[i], yp2[i]);
        pptad.AddPt(xp3[i], yp3[i]);
        pptad.AddPt(xp4[i], yp4[i]);
    }
}
