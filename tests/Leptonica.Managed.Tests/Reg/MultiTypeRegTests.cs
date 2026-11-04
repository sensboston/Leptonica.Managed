using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// multitype_reg.c: tests scaling, rotation, affine, projective, and bilinear
/// transforms across 11 different image types/depths.
/// </summary>
public class MultiTypeRegTests
{
    private static readonly string[] FNames = {
        "feyn-fract.tif", "speckle2.png",
        "weasel2.4g.png", "speckle4.png",
        "weasel4.16c.png", "dreyfus8.png",
        "weasel8.240c.png", "test8.jpg",
        "marge.jpg", "test-gray-alpha.png"
    };

    private static readonly int[] Xs = { 300, 1200, 225, 750 };
    private static readonly int[] Xd = { 330, 1225, 250, 870 };
    private static readonly int[] Ys = { 1250, 1120, 250, 200 };
    private static readonly int[] Yd = { 1150, 1200, 250, 290 };

    private const int PROJECTIVE = 1;
    private const int BILINEAR = 2;

    [RegFact]
    public void MultiType()
    {
        var rp = new RegTest("multitype");

        Pixa pixas = Pixa.Create(11);
        for (int i = 0; i < 10; i++) {
            Pix pix1 = TestPaths.ReadInput(FNames[i]);
            Pix pix2 = pix1.ScaleBySamplingToSize(250, 150);
            pixas.AddPix(pix2, L.L_INSERT);
        }

            /* Add a transparent grid over the rgb image */
        {
            Pix pix1 = pixas.GetPix(8, L.L_COPY);
            pix1.GetDimensions(out int w, out int h, out _);
            Pix pix2 = Pix.Create(w, h, 1);
            for (int i = 0; i < 5; i++) {
                int y = h * (i + 1) / 6;
                pix2.RenderLine(0, y, w, y, 3, L.L_SET_PIXELS);
            }
            for (int i = 0; i < 7; i++) {
                int x = w * (i + 1) / 8;
                pix2.RenderLine(x, 0, x, h, 3, L.L_SET_PIXELS);
            }
            Pix pix3 = pix2.ConvertTo8(false);
            pix1.SetRGBComponent(pix3, L.L_ALPHA_CHANNEL);
            pixas.AddPix(pix1, L.L_INSERT);
        }
        int n = pixas.Count;

            /* Display with and without removing alpha with white bg */
        Pix pixOut = pixas.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2);
        rp.WritePixAndCheck(pixOut, L.IFF_PNG);  /* 0 */

        Pixa pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            pixa.AddPix(pix1.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 1 */

            /* Setting to gray */
        pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            pix1.SetAllGray(170);
            pixa.AddPix(pix1.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 2 */

            /* General scaling */
        pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            Pix pix2 = pix1.ScaleToSize(350, 650);
            Pix pix3 = pix2.ScaleToSize(200, 200);
            pixa.AddPix(pix3.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 3 */

            /* Scaling by sampling */
        pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            Pix pix2 = pix1.ScaleBySamplingToSize(350, 650);
            Pix pix3 = pix2.ScaleBySamplingToSize(200, 200);
            pixa.AddPix(pix3.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 4 */

            /* Rotation by area mapping; no embedding */
        pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            Pix pix2 = pix1.Rotate(0.25f, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, 0, 0);
            Pix pix3 = pix2.Rotate(-0.35f, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, 0, 0);
            pixa.AddPix(pix3.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 5 */

            /* Rotation by area mapping; with embedding */
        pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            Pix pix2 = pix1.Rotate(0.25f, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, 250, 150);
            Pix pix3 = pix2.Rotate(-0.35f, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, 250, 150);
            Pix pix4 = pix3.RemoveBorderToSize(250, 150);
            pixa.AddPix(pix4.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 6 */

            /* Rotation by 3-shear; no embedding */
        pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            Pix pix2 = pix1.Rotate(0.25f, L.L_ROTATE_SHEAR, L.L_BRING_IN_WHITE, 0, 0);
            Pix pix3 = pix2.Rotate(-0.35f, L.L_ROTATE_SHEAR, L.L_BRING_IN_WHITE, 0, 0);
            pixa.AddPix(pix3.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 7 */

            /* Rotation by 3-shear; with embedding */
        pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            Pix pix2 = pix1.Rotate(0.25f, L.L_ROTATE_SHEAR, L.L_BRING_IN_WHITE, 250, 150);
            Pix pix3 = pix2.Rotate(-0.35f, L.L_ROTATE_SHEAR, L.L_BRING_IN_WHITE, 250, 150);
            Pix pix4 = pix3.RemoveBorderToSize(250, 150);
            pixa.AddPix(pix4.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 8 */

            /* Rotation by 2-shear about the center */
        pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            pix1.GetDimensions(out int w, out int h, out _);
            Pix pix2 = pix1.Rotate2Shear(w / 2, h / 2, 0.25f, L.L_BRING_IN_WHITE)!;
            Pix pix3 = pix2.Rotate2Shear(w / 2, h / 2, -0.35f, L.L_BRING_IN_WHITE)!;
            pixa.AddPix(pix3.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 9 */

            /* Rotation by sampling; no embedding */
        pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            Pix pix2 = pix1.Rotate(0.25f, L.L_ROTATE_SAMPLING, L.L_BRING_IN_WHITE, 0, 0);
            Pix pix3 = pix2.Rotate(-0.35f, L.L_ROTATE_SAMPLING, L.L_BRING_IN_WHITE, 0, 0);
            pixa.AddPix(pix3.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 10 */

            /* Rotation by sampling; with embedding */
        pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            Pix pix2 = pix1.Rotate(0.25f, L.L_ROTATE_SAMPLING, L.L_BRING_IN_WHITE, 250, 150);
            Pix pix3 = pix2.Rotate(-0.35f, L.L_ROTATE_SAMPLING, L.L_BRING_IN_WHITE, 250, 150);
            Pix pix4 = pix3.RemoveBorderToSize(250, 150);
            pixa.AddPix(pix4.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 11 */

            /* Rotation by area mapping at corner */
        pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            Pix pix2 = pix1.RotateAMCorner(0.25f, L.L_BRING_IN_WHITE);
            Pix pix3 = pix2.RotateAMCorner(-0.35f, L.L_BRING_IN_WHITE);
            pixa.AddPix(pix3.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 12 */

            /* Affine transform by interpolation */
        pixa = Pixa.Create(n);
        float[] vc = Generate3PtTransformVector();
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            Pix pix2 = pix1.Affine(vc, L.L_BRING_IN_WHITE);
            pixa.AddPix(pix2.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 13 */

            /* Projective transform by sampling */
        pixa = Pixa.Create(n);
        vc = Generate4PtTransformVector(PROJECTIVE);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            Pix pix2 = pix1.ProjectiveSampled(vc, L.L_BRING_IN_WHITE);
            pixa.AddPix(pix2.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 14 */

            /* Projective transform by interpolation */
        pixa = Pixa.Create(n);
        vc = Generate4PtTransformVector(PROJECTIVE);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            Pix pix2 = pix1.Projective(vc, L.L_BRING_IN_WHITE);
            pixa.AddPix(pix2.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 15 */

            /* Bilinear transform by interpolation */
        pixa = Pixa.Create(n);
        vc = Generate4PtTransformVector(BILINEAR);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_COPY);
            Pix pix2 = pix1.Bilinear(vc, L.L_BRING_IN_WHITE);
            pixa.AddPix(pix2.RemoveAlpha(), L.L_INSERT);
        }
        rp.WritePixAndCheck(pixa.DisplayTiledInRows(32, 1200, 1.0f, 0, 25, 2), L.IFF_PNG);  /* 16 */

        Assert.Equal(16, rp.Index);
        rp.Cleanup();
    }

    private static float[] Generate3PtTransformVector()
    {
        Pta ptas = Pta.Create(3);
        Pta ptad = Pta.Create(3);
        for (int i = 0; i < 3; i++) {
            ptas.AddPt(Xs[i], Ys[i]);
            ptad.AddPt(Xd[i], Yd[i]);
        }
        return Pix.GetAffineXformCoeffs(ptad, ptas);
    }

    private static float[] Generate4PtTransformVector(int type)
    {
        Pta ptas = Pta.Create(4);
        Pta ptad = Pta.Create(4);
        for (int i = 0; i < 4; i++) {
            ptas.AddPt(Xs[i], Ys[i]);
            ptad.AddPt(Xd[i], Yd[i]);
        }
        return type == PROJECTIVE
            ? Pix.GetProjectiveXformCoeffs(ptad, ptas)
            : Pix.GetBilinearXformCoeffs(ptad, ptas);
    }
}
