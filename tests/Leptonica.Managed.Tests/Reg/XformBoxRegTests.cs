using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// xformbox_reg.c: tests ordered box transforms (rotation, scaling, translation)
/// and the various box hashing graphics operations.
/// </summary>
public class XformBoxRegTests
{
        /* Consts for second set */
    private const int SHIFTX_2 = 50;
    private const int SHIFTY_2 = 70;
    private const float SCALEX_2 = 1.17f;
    private const float SCALEY_2 = 1.13f;
    private const float ROTATION_2 = 0.10f;   /* radian */

        /* Consts for third set */
    private const int SHIFTX_3 = 44;
    private const int SHIFTY_3 = 39;
    private const float SCALEX_3 = 0.83f;
    private const float SCALEY_3 = 0.78f;
    private const float ROTATION_3 = 0.11f;   /* radian */

    [RegFact]
    public void XformBox()
    {
        var rp = new RegTest("xformbox");

            /* ----------------------------------------------------------- *
             *                Test hash rendering in 3 modes               *
             * ----------------------------------------------------------- */
        Pix pixs = TestPaths.ReadInput("feyn.tif");
        Box box = Box.Create(461, 429, 1393, 342);
        Pix pix1 = pixs.ClipRectangle(box, out _)!;
        Boxa boxa = pix1.ConnComp(out _, 8);
        int n = boxa.Count;
        Pix pix2 = pix1.ConvertTo8(true);
        Pix pix3 = pix1.ConvertTo32();
        for (int i = 0; i < n; i++) {
            Box boxt = boxa.GetBox(i, L.L_CLONE);
            int rval = (1413 * (i + 1)) % 256;
            int gval = (4917 * (i + 1)) % 256;
            int bval = (7341 * (i + 1)) % 256;
            pix1.RenderHashBox(boxt, 8, 2, i % 4, 1, L.L_SET_PIXELS);
            pix2.RenderHashBoxArb(boxt, 7, 2, i % 4, 1, rval, gval, bval);
            pix3.RenderHashBoxBlend(boxt, 7, 2, i % 4, 1, rval, gval, bval, 0.5f);
        }
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 1 */
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 2 */

            /* ----------------------------------------------------------- *
             *        Test orthogonal box rotation and hash rendering      *
             * ----------------------------------------------------------- */
        pixs = TestPaths.ReadInput("feyn.tif");
        box = Box.Create(461, 429, 1393, 342);
        pix1 = pixs.ClipRectangle(box, out _)!;
        Pix pixc = pix1.ConvertTo32();
        pix1.GetDimensions(out int w, out int h, out _);
        Boxa boxa1 = pix1.ConnComp(out _, 8);
        Pixa pixa = Pixa.Create(4);
        for (int i = 0; i < 4; i++) {
            pix2 = pixc.RotateOrth(i);
            Boxa boxa2 = boxa1.RotateOrth(w, h, i);
            int rval = (1413 * (i + 4)) % 256;
            int gval = (4917 * (i + 4)) % 256;
            int bval = (7341 * (i + 4)) % 256;
            pix2.RenderHashBoxaArb(boxa2, 10, 3, i, 1, rval, gval, bval);
            pixa.AddPix(pix2, L.L_INSERT);
        }
        pix3 = pixa.DisplayTiledInRows(32, 1200, 0.7f, 0, 30, 3);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 3 */

            /* ----------------------------------------------------------- *
             *    Test box transforms with either translation or scaling   *
             *    combined with rotation, using the simple 'ordered'       *
             *    function.  Show that the order of the operations does    *
             *    not matter; different hashing schemes end up in the      *
             *    identical boxes.                                         *
             * ----------------------------------------------------------- */
        Pix pix = TestPaths.ReadInput("feyn.tif");
        box = Box.Create(420, 360, 1500, 465);
        Pix pixt = pix.ClipRectangle(box, out _)!;
        pixs = pixt.AddBorderGeneral(0, 200, 0, 0, 0);
        boxa = pixs.ConnComp(out _, 8);
        pixa = Pixa.Create(0);

        pixt = pixs.ConvertTo32();
        for (int i = 0; i < 3; i++) {
            int order = i == 0 ? L.L_TR_SC_RO : (i == 1 ? L.L_TR_RO_SC : L.L_SC_TR_RO);
            Boxa boxat = boxa.TransformOrdered(SHIFTX_2, SHIFTY_2, 1.0f, 1.0f,
                                               450, 250, ROTATION_2, order);
            RenderTransformedBoxa(pixt, boxat, i);
        }
        pixa.AddPix(pixt, L.L_INSERT);

        pixt = pixs.ConvertTo32();
        for (int i = 0; i < 3; i++) {
            int order = i == 0 ? L.L_RO_TR_SC : (i == 1 ? L.L_RO_SC_TR : L.L_SC_RO_TR);
            Boxa boxat = boxa.TransformOrdered(SHIFTX_2, SHIFTY_2, 1.0f, 1.0f,
                                               450, 250, ROTATION_2, order);
            RenderTransformedBoxa(pixt, boxat, i + 4);
        }
        pixa.AddPix(pixt, L.L_INSERT);

        pixt = pixs.ConvertTo32();
        for (int i = 0; i < 3; i++) {
            int order = i == 0 ? L.L_TR_SC_RO : (i == 1 ? L.L_SC_RO_TR : L.L_SC_TR_RO);
            Boxa boxat = boxa.TransformOrdered(0, 0, SCALEX_2, SCALEY_2,
                                               450, 250, ROTATION_2, order);
            RenderTransformedBoxa(pixt, boxat, i + 8);
        }
        pixa.AddPix(pixt, L.L_INSERT);

        pixt = pixs.ConvertTo32();
        for (int i = 0; i < 3; i++) {
            int order = i == 0 ? L.L_RO_TR_SC : (i == 1 ? L.L_RO_SC_TR : L.L_TR_RO_SC);
            Boxa boxat = boxa.TransformOrdered(0, 0, SCALEX_2, SCALEY_2,
                                               450, 250, ROTATION_2, order);
            RenderTransformedBoxa(pixt, boxat, i + 12);
        }
        pixa.AddPix(pixt, L.L_INSERT);

        pixt = pixa.DisplayTiledInColumns(1, 0.5f, 20, 0);
        rp.WritePixAndCheck(pixt, L.IFF_PNG);  /* 4 */

            /* ----------------------------------------------------------- *
             *    Do more testing of box and pta transforms.  Show that    *
             *    resulting boxes are identical by three methods.          *
             * ----------------------------------------------------------- */
            /* Set up pix and boxa */
        pixa = Pixa.Create(0);
        pix = TestPaths.ReadInput("lucasta.1.300.tif");
        Pix.Translate(pix, pix, 70, 0, L.L_BRING_IN_WHITE);
        pixt = Pix.CloseBrick(null, pix, 14, 5);
        Pix.OpenBrick(pixt, pixt, 1, 2);
        boxa = pixt.ConnComp(out _, 8);
        pixs = pix.ConvertTo32();
        pixc = Pix.Copy(null, pixs);
        RenderTransformedBoxa(pixc, boxa, 113);
        pixa.AddPix(pixc, L.L_INSERT);

            /* (a) Do successive discrete operations: shift, scale, rotate */
        pix1 = Pix.Translate(null, pixs, SHIFTX_3, SHIFTY_3, L.L_BRING_IN_WHITE);
        boxa1 = boxa.Translate(SHIFTX_3, SHIFTY_3);
        pixc = Pix.Copy(null, pix1);
        RenderTransformedBoxa(pixc, boxa1, 213);
        pixa.AddPix(pixc, L.L_INSERT);

        pix2 = pix1.Scale(SCALEX_3, SCALEY_3);
        Boxa boxa2Discrete = boxa1.Scale(SCALEX_3, SCALEY_3);
        pixc = Pix.Copy(null, pix2);
        RenderTransformedBoxa(pixc, boxa2Discrete, 313);
        pixa.AddPix(pixc, L.L_INSERT);

        pix2.GetDimensions(out w, out h, out _);
        pix3 = pix2.RotateAM(ROTATION_3, L.L_BRING_IN_WHITE);
        Boxa boxa3 = boxa2Discrete.Rotate(w / 2, h / 2, ROTATION_3);
        pixc = Pix.Copy(null, pix3);
        RenderTransformedBoxa(pixc, boxa3, 413);
        pixa.AddPix(pixc, L.L_INSERT);

            /* (b) Set up and use the composite transform */
        float[] mat1 = Pix.CreateMatrix2dTranslate(SHIFTX_3, SHIFTY_3);
        float[] mat2 = Pix.CreateMatrix2dScale(SCALEX_3, SCALEY_3);
        float[] mat3 = Pix.CreateMatrix2dRotate(w / 2, h / 2, ROTATION_3);
        float[] matd = new float[9];
        Pix.ProductMat3(mat3, mat2, mat1, matd, 3);
        Boxa boxa4 = boxa.AffineTransform(matd);
        pixc = Pix.Copy(null, pix3);
        RenderTransformedBoxa(pixc, boxa4, 513);
        pixa.AddPix(pixc, L.L_INSERT);

            /* (c) Use the special 'ordered' function */
        pixs.GetDimensions(out int ws, out int hs, out _);
        Boxa boxa5 = boxa.TransformOrdered(SHIFTX_3, SHIFTY_3,
                                           SCALEX_3, SCALEY_3,
                                           ws / 2, hs / 2, ROTATION_3, L.L_TR_SC_RO);
        pixc = Pix.Copy(null, pix3);
        RenderTransformedBoxa(pixc, boxa5, 613);
        pixa.AddPix(pixc, L.L_INSERT);

        pixt = pixa.DisplayTiledInColumns(2, 0.5f, 20, 0);
        rp.WritePixAndCheck(pixt, L.IFF_PNG);  /* 5 */

        Assert.Equal(5, rp.Index);
        rp.Cleanup();
    }

    private static void RenderTransformedBoxa(Pix pixt, Boxa boxa, int i)
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
