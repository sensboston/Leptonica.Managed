using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// affine_reg.c: tests affine transforms, including invertability and large distortions.
/// </summary>
public class AffineRegTests
{
        /* Sample values.
         *    1-3: invertability tests
         *    4: comparison between sampling and sequential
         *    5: test with large distortion
         */
    private static readonly int[] x1 = { 300,  300,  300,  95,    32};
    private static readonly int[] y1 = {1200, 1200, 1250, 2821,  934};
    private static readonly int[] x2 = {1200, 1200, 1125, 1432,  487};
    private static readonly int[] y2 = {1100, 1100, 1100, 2682,  934};
    private static readonly int[] x3 = { 200,  200,  200,  232,   32};
    private static readonly int[] y3 = { 200,  200,  200,  657,   67};

    private static readonly int[] xp1 = { 500,  300,  350,  117,   32};
    private static readonly int[] yp1 = {1700, 1400, 1400, 2629,  934};
    private static readonly int[] xp2 = {850, 1400, 1400, 1464,  487};
    private static readonly int[] yp2 = {850, 1500, 1500, 2432,  804};
    private static readonly int[] xp3 = { 450,  200,  400,  183,   61};
    private static readonly int[] yp3 = { 300,  300,  400,  490,   83};

    private const int SHIFTX = 44;
    private const int SHIFTY = 39;
    private const float SCALEX = 0.83f;
    private const float SCALEY = 0.78f;
    private const float ROTATION = 0.11f;   /* radian */

    private const int ADDED_BORDER_PIXELS = 1000;

    [RegFact]
    public void Affine()
    {
        int i, w, h;
        float[] mat1, mat2, mat3, mat1i, mat2i, mat3i, matdinv;
        float[] matd = new float[9];
        float[] matdi = new float[9];
        Boxa boxa, boxa2;
        Pix pix, pixs, pixb, pixg, pixc, pixcs;
        Pix pixd, pix1, pix2, pix3;
        Pixa pixa;
        Pta ptas, ptad;
        var rp = new RegTest("affine");

        pix = TestPaths.ReadInput("feyn.tif");
        pixs = pix.Scale(0.22f, 0.22f);

            /* Test invertability of sequential. */
        pixa = Pixa.Create(0);
        for (i = 0; i < 3; i++) {
            pixb = pixs.AddBorder(ADDED_BORDER_PIXELS, 0);
            MakePtas(i, out ptas, out ptad);
            pix1 = pixb.AffineSequential(ptad, ptas, 0, 0);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0,3,6 */
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pix1.AffineSequential(ptas, ptad, 0, 0);
            rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 1,4,7 */
            pixa.AddPix(pix2, L.L_INSERT);
            pixd = pix2.RemoveBorder(ADDED_BORDER_PIXELS);
            Pix.Xor(pixd, pixd, pixs);
            rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 2,5,8 */
            pixa.AddPix(pixd, L.L_INSERT);
        }

        pix1 = pixa.DisplayTiledInColumns(3, 1.0f, 20, 3);
        pix2 = pix1.ScaleToGray(0.2f);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 9 */

            /* Test invertability of sampling */
        pixa = Pixa.Create(0);
        for (i = 0; i < 3; i++) {
            pixb = pixs.AddBorder(ADDED_BORDER_PIXELS, 0);
            MakePtas(i, out ptas, out ptad);
            pix1 = pixb.AffineSampledPta(ptad, ptas, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 10,13,16 */
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pix1.AffineSampledPta(ptas, ptad, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 11,14,17 */
            pixa.AddPix(pix2, L.L_INSERT);
            pixd = pix2.RemoveBorder(ADDED_BORDER_PIXELS);
            Pix.Xor(pixd, pixd, pixs);
            rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 12,15,18 */
            pixa.AddPix(pixd, L.L_INSERT);
        }

        pix1 = pixa.DisplayTiledInColumns(3, 1.0f, 20, 3);
        pix2 = pix1.ScaleToGray(0.2f);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 19 */

            /* Test invertability of interpolation on grayscale */
        pix = TestPaths.ReadInput("feyn.tif");
        pixg = pix.ScaleToGray3();
        pixa = Pixa.Create(0);
        for (i = 0; i < 3; i++) {
            pixb = pixg.AddBorder(ADDED_BORDER_PIXELS / 3, 255);
            MakePtas(i, out ptas, out ptad);
            pix1 = pixb.AffinePta(ptad, ptas, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 20,23,26 */
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pix1.AffinePta(ptas, ptad, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 21,24,27 */
            pixa.AddPix(pix2, L.L_INSERT);
            pixd = pix2.RemoveBorder(ADDED_BORDER_PIXELS / 3);
            Pix.Xor(pixd, pixd, pixg);
            Pix.Invert(pixd, pixd);
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 22,25,28 */
            pixa.AddPix(pixd, L.L_INSERT);
        }

        pix1 = pixa.DisplayTiledInColumns(3, 1.0f, 20, 3);
        pix2 = pix1.Scale(0.2f, 0.2f);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 29 */

            /* Test invertability of interpolation on color */
        pixa = Pixa.Create(0);
        pixc = TestPaths.ReadInput("test24.jpg");
        pixcs = pixc.Scale(0.3f, 0.3f);
        for (i = 0; i < 3; i++) {
            pixb = pixcs.AddBorder(ADDED_BORDER_PIXELS / 4, 0xffffff00);
            MakePtas(i, out ptas, out ptad);
            pix1 = pixb.AffinePta(ptad, ptas, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 30,33,36 */
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pix1.AffinePta(ptas, ptad, L.L_BRING_IN_WHITE);
            rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 31,34,37 */
            pixa.AddPix(pix2, L.L_INSERT);
            pixd = pix2.RemoveBorder(ADDED_BORDER_PIXELS / 4);
            Pix.Xor(pixd, pixd, pixcs);
            Pix.Invert(pixd, pixd);
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 32,35,38 */
            pixa.AddPix(pixd, L.L_INSERT);
        }

        pix1 = pixa.DisplayTiledInColumns(3, 1.0f, 20, 3);
        pix2 = pix1.Scale(0.25f, 0.25f);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 39 */

           /* Comparison between sequential and sampling */
        pix = TestPaths.ReadInput("feyn.tif");
        pixs = pix.Scale(0.22f, 0.22f);

        MakePtas(3, out ptas, out ptad);
        pixa = Pixa.Create(0);

            /* Use sequential transforms */
        pix1 = pixs.AffineSequential(ptas, ptad, ADDED_BORDER_PIXELS, ADDED_BORDER_PIXELS);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 40 */
        pixa.AddPix(pix1, L.L_INSERT);

            /* Use sampled transform */
        pix2 = pixs.AffineSampledPta(ptas, ptad, L.L_BRING_IN_WHITE);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 41 */
        pixa.AddPix(pix2, L.L_COPY);

            /* Compare the results */
        Pix.Xor(pix2, pix2, pix1);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 42 */
        pixa.AddPix(pix2, L.L_INSERT);

        pix1 = pixa.DisplayTiledInColumns(3, 1.0f, 20, 3);
        pix2 = pix1.Scale(0.5f, 0.5f);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 43 */

           /* Test with large distortion */
        MakePtas(4, out ptas, out ptad);
        pixa = Pixa.Create(0);
        pix = TestPaths.ReadInput("feyn.tif");
        pixg = pix.ScaleToGray6();

        pix1 = pixg.AffineSequential(ptas, ptad, 0, 0);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 44 */
        pixa.AddPix(pix1, L.L_COPY);

        pix2 = pixg.AffineSampledPta(ptas, ptad, L.L_BRING_IN_WHITE);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 45 */
        pixa.AddPix(pix2, L.L_COPY);

        pix3 = pixg.AffinePta(ptas, ptad, L.L_BRING_IN_WHITE);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 46 */
        pixa.AddPix(pix3, L.L_INSERT);

        Pix.Xor(pix1, pix1, pix2);
        Pix.Invert(pix1, pix1);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 47 */
        pixa.AddPix(pix1, L.L_INSERT);
        Pix.Xor(pix2, pix2, pix3);
        Pix.Invert(pix2, pix2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 48 */
        pixa.AddPix(pix2, L.L_INSERT);

        pix1 = pixa.DisplayTiledInColumns(5, 1.0f, 20, 3);
        pix2 = pix1.Scale(0.8f, 0.8f);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 49 */

            /* Set up pix and boxa */
        pixa = Pixa.Create(0);
        pix = TestPaths.ReadInput("lucasta.1.300.tif");
        Pix.Translate(pix, pix, 70, 0, L.L_BRING_IN_WHITE);
        pix1 = Pix.CloseBrick(null, pix, 14, 5);
        Pix.OpenBrick(pix1, pix1, 1, 2);
        boxa = pix1.ConnComp(8);
        pixs = pix.ConvertTo32();
        w = pixs.Width;
        h = pixs.Height;
        pixc = Pix.Copy(null, pixs);
        RenderHashedBoxa(pixc, boxa, 113);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 50 */
        pixa.AddPix(pixc, L.L_INSERT);

            /* Set up an affine transform in matd, and apply it to boxa */
        mat1 = Pix.CreateMatrix2dTranslate(SHIFTX, SHIFTY);
        mat2 = Pix.CreateMatrix2dScale(SCALEX, SCALEY);
        mat3 = Pix.CreateMatrix2dRotate(w / 2, h / 2, ROTATION);
        Pix.ProductMat3(mat3, mat2, mat1, matd, 3);
        boxa2 = boxa.AffineTransform(matd);

            /* Set up the inverse transform --> matdi */
        mat1i = Pix.CreateMatrix2dTranslate(-SHIFTX, -SHIFTY);
        mat2i = Pix.CreateMatrix2dScale((float)(1.0 / SCALEX), (float)(1.0 / SCALEY));
        mat3i = Pix.CreateMatrix2dRotate(w / 2, h / 2, -ROTATION);
        Pix.ProductMat3(mat1i, mat2i, mat3i, matdi, 3);

            /* Invert the original affine transform --> matdinv */
        matdinv = Pix.AffineInvertXform(matd);

            /* Apply the inverted affine transform --> pixs */
        pixd = pixs.Affine(matdinv, L.L_BRING_IN_WHITE);
        RenderHashedBoxa(pixd, boxa2, 513);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 51 */
        pixa.AddPix(pixd, L.L_INSERT);

        pix1 = pixa.DisplayTiledInColumns(2, 1.0f, 30, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 52 */

        Assert.Equal(52, rp.Index);
        rp.Cleanup();
    }

    private static void MakePtas(int i, out Pta pptas, out Pta pptad)
    {
        pptas = Pta.Create(3);
        pptas.AddPt(x1[i], y1[i]);
        pptas.AddPt(x2[i], y2[i]);
        pptas.AddPt(x3[i], y3[i]);
        pptad = Pta.Create(3);
        pptad.AddPt(xp1[i], yp1[i]);
        pptad.AddPt(xp2[i], yp2[i]);
        pptad.AddPt(xp3[i], yp3[i]);
    }

    private static void RenderHashedBoxa(Pix pixt, Boxa boxa, int i)
    {
        int n = boxa.Count;
        int rval = (1413 * i) % 256;
        int gval = (4917 * i) % 256;
        int bval = (7341 * i) % 256;
        for (int j = 0; j < n; j++) {
            Box box = boxa.GetBox(j, L.L_CLONE);
            pixt.RenderHashBoxArb(box, 10, 3, i % 4, 1, rval, gval, bval);
        }
    }
}
