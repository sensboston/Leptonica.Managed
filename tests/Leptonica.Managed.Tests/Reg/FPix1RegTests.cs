using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// fpix1_reg.c: regression test for FPix/DPix convolutions, arithmetic,
/// borders, contours, serialization, and affine/projective transforms.
/// </summary>
[Collection("ConvolveSampling")]
public class FPix1RegTests
{
    private static readonly int[] Xs1 = { 300,  300, 1100,  300,   32 };
    private static readonly int[] Ys1 = { 1200, 1200, 1200, 1250,  934 };
    private static readonly int[] Xs2 = { 1200, 1200,  325, 1300,  487 };
    private static readonly int[] Ys2 = { 1100, 1100, 1200, 1250,  934 };
    private static readonly int[] Xs3 = { 200,  200, 1200,  250,   32 };
    private static readonly int[] Ys3 = { 200,  200,  200,  300,   67 };
    private static readonly int[] Xs4 = { 1200, 1200, 1100, 1250,  332 };
    private static readonly int[] Ys4 = { 400,  200,  200,  300,   57 };

    private static readonly int[] Xd1 = { 300,  300, 1150,  300,   32 };
    private static readonly int[] Yd1 = { 1200, 1400, 1150, 1350,  934 };
    private static readonly int[] Xd2 = { 1100, 1400,  320, 1300,  487 };
    private static readonly int[] Yd2 = { 1000, 1500, 1300, 1200,  904 };
    private static readonly int[] Xd3 = { 250,  200, 1310,  300,   61 };
    private static readonly int[] Yd3 = { 200,  300,  250,  325,   83 };
    private static readonly int[] Xd4 = { 1250, 1200, 1140, 1250,  412 };
    private static readonly int[] Yd4 = { 300,  300,  250,  350,   83 };

    [RegFact]
    public void FPix1()
    {
        var rp = new RegTest("fpix1");

        try {
                /* Gaussian kernel */
            Pixa pixa = Pixa.Create(0);
            L_Kernel kel = L_Kernel.MakeGaussianKernel(5, 5, 3.0f, 4.0f);
            Pix pix0 = kel.DisplayInPix(41, 2);
            rp.WritePixAndCheck(pix0, L.IFF_PNG);  /* 0 */
            pixa.AddPix(pix0, L.L_INSERT);

                /* Separable gaussian kernel */
            L_Kernel.MakeGaussianKernelSep(5, 5, 3.0f, 4.0f, out L_Kernel kelx, out L_Kernel kely);
            pix0 = kelx.DisplayInPix(41, 2);
            rp.WritePixAndCheck(pix0, L.IFF_PNG);  /* 1 */
            pixa.AddPix(pix0, L.L_INSERT);
            pix0 = kely.DisplayInPix(41, 2);
            rp.WritePixAndCheck(pix0, L.IFF_PNG);  /* 2 */
            pixa.AddPix(pix0, L.L_INSERT);
            pix0 = pixa.DisplayTiledInColumns(4, 1.0f, 20, 2);
            rp.WritePixAndCheck(pix0, L.IFF_PNG);  /* 3 */

                /* Use pixRasterop() to generate source image */
            pixa = Pixa.Create(0);
            Pix pixs = TestPaths.ReadInput("test8.jpg");
            Pix pixs2 = TestPaths.ReadInput("karen8.jpg");
            pixs.Rasterop(150, 125, 150, 100, L.PIX_SRC, pixs2, 75, 100);
            rp.WritePixAndCheck(pixs, L.IFF_JFIF_JPEG);  /* 4 */

                /* Convolution directly with pix */
            Pix pix1 = pixs.Convolve(kel, 8, 1);
            rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 5 */
            pixa.AddPix(pix1, L.L_INSERT);
            Pix pix2 = pixs.ConvolveSep(kelx, kely, 8, 1);
            rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 6 */
            pixa.AddPix(pix2, L.L_INSERT);

                /* Convolution indirectly with fpix */
            FPix fpixs = pixs.ConvertToFPix(3);
            FPix fpixs2 = pixs2.ConvertToFPix(3);
            fpixs.Rasterop(150, 125, 150, 100, fpixs2, 75, 100);
            FPix fpix1 = fpixs.Convolve(kel, 1);
            Pix pix3 = fpix1.ConvertToPix(8, L.L_CLIP_TO_ZERO, 1);
            rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 7 */
            pixa.AddPix(pix3, L.L_INSERT);
            FPix fpix2 = fpixs.ConvolveSep(kelx, kely, 1);
            Pix pix4 = fpix2.ConvertToPix(8, L.L_CLIP_TO_ZERO, 1);
            rp.WritePixAndCheck(pix4, L.IFF_JFIF_JPEG);  /* 8 */
            pixa.AddPix(pix4, L.L_INSERT);

            pix1 = pixa.DisplayTiledInColumns(2, 1.0f, 20, 2);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 9 */

                /* Test arithmetic operations; add in a fraction rotated by 180 */
            pixa = Pixa.Create(0);
            Pix pixs3 = Pix.Rotate180(null, pixs);
            rp.WritePixAndCheck(pixs3, L.IFF_JFIF_JPEG);  /* 10 */
            pixa.AddPix(pixs3, L.L_INSERT);
            FPix fpixs3 = pixs3.ConvertToFPix(3);
            FPix fpixd = FPix.LinearCombination(null, fpixs, fpixs3, 20.0f, 5.0f);
            fpixd.AddMultConstant(0.0f, 23.174f);
            Pix pixd = fpixd.DisplayMaxDynamicRange();
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 11 */
            pixa.AddPix(pixd, L.L_INSERT);

            pixd = pixa.DisplayTiledInColumns(2, 1.0f, 20, 2);
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 12 */

                /* Test some more convolutions, with sampled output. First on pix */
            pixa = Pixa.Create(0);
            pixs = TestPaths.ReadInput("1555.007.jpg");
            Pix pixg = pixs.ConvertTo8(false);
            Pix.SetConvolveSampling(5, 5);
            pix1 = pixg.Convolve(kel, 8, 1);
            rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 13 */
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pixg.ConvolveSep(kelx, kely, 8, 1);
            rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 14 */
            pixa.AddPix(pix2, L.L_INSERT);
            pix3 = pixs.ConvolveRGB(kel);
            rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 15 */
            pixa.AddPix(pix3, L.L_INSERT);
            pix4 = pixs.ConvolveRGBSep(kelx, kely);
            rp.WritePixAndCheck(pix4, L.IFF_JFIF_JPEG);  /* 16 */
            pixa.AddPix(pix4, L.L_INSERT);

                /* Then on fpix */
            FPix fpixg = pixg.ConvertToFPix(1);
            fpix1 = fpixg.Convolve(kel, 1);
            Pix pix5 = fpix1.ConvertToPix(8, L.L_CLIP_TO_ZERO, 0);
            rp.WritePixAndCheck(pix5, L.IFF_JFIF_JPEG);  /* 17 */
            pixa.AddPix(pix5, L.L_INSERT);
            fpix2 = fpixg.ConvolveSep(kelx, kely, 1);
            Pix pix6 = fpix2.ConvertToPix(8, L.L_CLIP_TO_ZERO, 0);
            rp.WritePixAndCheck(pix6, L.IFF_JFIF_JPEG);  /* 18 */
            pixa.AddPix(pix6, L.L_INSERT);
            rp.CompareSimilarPix(pix1, pix5, 2, 0.00f, 0);  /* 19 */
            rp.CompareSimilarPix(pix2, pix6, 2, 0.00f, 0);  /* 20 */
            Pix.SetConvolveSampling(1, 1);

            pixd = pixa.DisplayTiledInColumns(2, 1.0f, 20, 2);
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 21 */

                /* Test extension (continued and slope) via Dewarp disparity */
            pixs = TestPaths.ReadInput("cat.035.jpg");
            Pix pixn = pixs.BackgroundNormSimple(null, null);
            pixg = pixn.ConvertRGBToGray(0.5f, 0.3f, 0.2f);
            Pix pixb = pixg.ThresholdToBinary(130);
            Dewarpa dewa = Dewarpa.Create(1, 30, 1, 15, 0);
            Dewarp dew = Dewarp.Create(pixb, 35);
            dewa.InsertDewarp(dew);
            dew.BuildPageModel(null);
            dew.PopulateFullRes(null, 0, 0);
            fpixs = dew.FullVDispar!;
            fpixs2 = fpixs.AddContinuedBorder(200, 200, 100, 300);
            fpixs3 = fpixs.AddSlopeBorder(200, 200, 100, 300);
            DPix dpix = fpixs3.ConvertToDPix();
            FPix fpixs4 = dpix.ConvertToFPix();
            pix1 = fpixs.RenderContours(2.0f, 0.2f);
            pix2 = fpixs2.RenderContours(2.0f, 0.2f);
            pix3 = fpixs3.RenderContours(2.0f, 0.2f);
            pix4 = fpixs4.RenderContours(2.0f, 0.2f);
            pix5 = TestPaths.ReadInput("karen8.jpg");
            DPix dpix2 = pix5.ConvertToDPix(1);
            pix6 = dpix2.ConvertToPix(8, L.L_CLIP_TO_ZERO, 0);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 22 */
            rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 23 */
            rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 24 */
            rp.ComparePix(pix3, pix4);  /* 25 */
            rp.ComparePix(pix5, pix6);  /* 26 */

                /* Test affine and projective transforms on fpix */
            dew.FullVDispar!.Write("/tmp/lept/regout/fpix1.fp");
            fpix1 = FPix.Read("/tmp/lept/regout/fpix1.fp");
            pix1 = fpix1.AutoRenderContours(40);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 27 */

            MakePtasAffine(1, out Pta ptas, out Pta ptad);
            fpix2 = fpix1.AffinePta(ptad, ptas, 200, 0.0f);
            pix2 = fpix2.AutoRenderContours(40);
            rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 28 */

            MakePtas(1, out ptas, out ptad);
            fpix2 = fpix1.ProjectivePta(ptad, ptas, 200, 0.0f);
            pix3 = fpix2.AutoRenderContours(40);
            rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 29 */
        } finally {
            Pix.SetConvolveSampling(1, 1);
        }

        Assert.Equal(29, rp.Index);
        rp.Cleanup();
    }

    private static void MakePtas(int i, out Pta pptas, out Pta pptad)
    {
        Pta ptas = Pta.Create(4);
        ptas.AddPt(Xs1[i], Ys1[i]);
        ptas.AddPt(Xs2[i], Ys2[i]);
        ptas.AddPt(Xs3[i], Ys3[i]);
        ptas.AddPt(Xs4[i], Ys4[i]);
        Pta ptad = Pta.Create(4);
        ptad.AddPt(Xd1[i], Yd1[i]);
        ptad.AddPt(Xd2[i], Yd2[i]);
        ptad.AddPt(Xd3[i], Yd3[i]);
        ptad.AddPt(Xd4[i], Yd4[i]);
        pptas = ptas;
        pptad = ptad;
    }

    private static void MakePtasAffine(int i, out Pta pptas, out Pta pptad)
    {
        Pta ptas = Pta.Create(3);
        ptas.AddPt(Xs1[i], Ys1[i]);
        ptas.AddPt(Xs2[i], Ys2[i]);
        ptas.AddPt(Xs3[i], Ys3[i]);
        Pta ptad = Pta.Create(3);
        ptad.AddPt(Xd1[i], Yd1[i]);
        ptad.AddPt(Xd2[i], Yd2[i]);
        ptad.AddPt(Xd3[i], Yd3[i]);
        pptas = ptas;
        pptad = ptad;
    }
}
