using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// projective_reg.c: tests projective transforms, including invertability and large distortions.
/// </summary>
public class ProjectiveRegTests
{
        /* Sample values.
         *    1-3: invertability tests
         *    4: comparison between sampling and sequential
         *    5: test with large distortion
         */
    private static readonly int[] x1 = { 300,  300,  300,  300,   32};
    private static readonly int[] y1 = {1200, 1200, 1250, 1250,  934};
    private static readonly int[] x2 = {1200, 1200, 1125, 1300,  487};
    private static readonly int[] y2 = {1100, 1100, 1100, 1250,  934};
    private static readonly int[] x3 = { 200,  200,  200,  250,   32};
    private static readonly int[] y3 = { 200,  200,  200,  300,   67};
    private static readonly int[] x4 = {1200, 1200, 1300, 1250,  332};
    private static readonly int[] y4 = { 400,  200,  200,  300,   57};

    private static readonly int[] xp1 = { 300,  300, 1150,  300,   32};
    private static readonly int[] yp1 = {1200, 1400, 1150, 1350,  934};
    private static readonly int[] xp2 = {1100, 1400,  320, 1300,  487};
    private static readonly int[] yp2 = {1000, 1500, 1300, 1200,  904};
    private static readonly int[] xp3 = { 250,  200, 1310,  300,   61};
    private static readonly int[] yp3 = { 200,  300,  250,  325,   83};
    private static readonly int[] xp4 = {1250, 1200,  240, 1250,  412};
    private static readonly int[] yp4 = { 300,  300,  250,  350,   83};

    private const int ADDED_BORDER_PIXELS = 250;

    [RegFact]
    public void Projective()
    {
        int i;
        Pix pixs, pixsc, pixb, pixg, pixc, pixcs, pix1, pix2, pixd;
        Pixa pixa;
        Pta ptas, ptad;
        var rp = new RegTest("projective");

        pixs = TestPaths.ReadInput("feyn.tif");
        pixsc = pixs.Scale(0.3f, 0.3f);

            /* Test invertability of sampling */
        pixa = Pixa.Create(0);
        for (i = 0; i < 3; i++) {
            pixb = pixsc.AddBorder(ADDED_BORDER_PIXELS, 0);
            MakePtas(i, out ptas, out ptad);
            pix1 = pixb.ProjectiveSampledPta(ptad, ptas, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0,3,6 */
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pix1.ProjectiveSampledPta(ptas, ptad, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 1,4,7 */
            pixa.AddPix(pix2, L.L_INSERT);
            pixd = pix2.RemoveBorder(ADDED_BORDER_PIXELS);
            Pix.Xor(pixd, pixd, pixsc);
            rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 2,5,8 */
            pixa.AddPix(pixd, L.L_INSERT);
        }

        pix1 = pixa.DisplayTiledInColumns(3, 0.5f, 20, 3);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 9 */

            /* Test invertability of interpolation on grayscale */
        pixa = Pixa.Create(0);
        pixg = pixs.ScaleToGray(0.2f);
        for (i = 0; i < 2; i++) {
            pixb = pixg.AddBorder(ADDED_BORDER_PIXELS / 2, 255);
            MakePtas(i, out ptas, out ptad);
            pix1 = pixb.ProjectivePta(ptad, ptas, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 10,13 */
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pix1.ProjectivePta(ptas, ptad, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 11,14 */
            pixa.AddPix(pix2, L.L_INSERT);
            pixd = pix2.RemoveBorder(ADDED_BORDER_PIXELS / 2);
            Pix.Xor(pixd, pixd, pixg);
            Pix.Invert(pixd, pixd);
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 12,15 */
            pixa.AddPix(pixd, L.L_INSERT);
        }

        pix1 = pixa.DisplayTiledInColumns(3, 0.5f, 20, 3);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 16 */

            /* Test invertability of interpolation on color */
        pixa = Pixa.Create(0);
        pixc = TestPaths.ReadInput("test24.jpg");
        pixcs = pixc.Scale(0.3f, 0.3f);
        for (i = 0; i < 5; i++) {
            if (i == 2) continue;
            pixb = pixcs.AddBorder(ADDED_BORDER_PIXELS / 2, 0xffffff00);
            MakePtas(i, out ptas, out ptad);
            pix1 = pixb.ProjectivePta(ptad, ptas, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 17,20,23,26 */
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pix1.ProjectivePta(ptas, ptad, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 18,21,24,27 */
            pixa.AddPix(pix2, L.L_INSERT);
            pixd = pix2.RemoveBorder(ADDED_BORDER_PIXELS / 2);
            Pix.Xor(pixd, pixd, pixcs);
            Pix.Invert(pixd, pixd);
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 19,22,25,28 */
            pixa.AddPix(pixd, L.L_INSERT);
        }

        pix1 = pixa.DisplayTiledInColumns(3, 0.5f, 20, 3);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 29 */

            /* Comparison between sampling and interpolated */
        MakePtas(3, out ptas, out ptad);
        pixa = Pixa.Create(0);
        pixg = pixs.ScaleToGray(0.2f);

            /* Use sampled transform */
        pix1 = pixg.ProjectiveSampledPta(ptas, ptad, L.L_BRING_IN_WHITE);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 30 */
        pixa.AddPix(pix1, L.L_INSERT);

            /* Use interpolated transforms */
        pix2 = pixg.ProjectivePta(ptas, ptad, L.L_BRING_IN_WHITE);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 31 */
        pixa.AddPix(pix2, L.L_COPY);

            /* Compare the results */
        Pix.Xor(pix2, pix2, pix1);
        Pix.Invert(pix2, pix2);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 32 */
        pixa.AddPix(pix2, L.L_INSERT);

        pix1 = pixa.DisplayTiledInColumns(3, 0.5f, 20, 3);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 33 */

        Assert.Equal(33, rp.Index);
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
