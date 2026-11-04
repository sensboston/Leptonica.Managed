using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// graymorph1_reg.c: tests grayscale morphology, interpreter, tophat, hdome,
/// duality, and accumulate contrast enhancement.
/// </summary>
public class GrayMorph1RegTests
{
    private const int WSIZE = 7;
    private const int HSIZE = 7;

    [RegFact]
    public void GrayMorph1()
    {
        var rp = new RegTest("graymorph1");

        Pix pixs = TestPaths.ReadInput("aneurisms8.jpg");
        Pixa pixa = Pixa.Create(0);

        /* -------- Test gray morph, including interpreter ------------ */
        Pix pix1 = pixs.DilateGray(WSIZE, HSIZE);
        Pix pix2 = pixs.GrayMorphSequence($"D{WSIZE}.{HSIZE}", 0, 0);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */
        rp.ComparePix(pix1, pix2);  /* 1 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix1 = pixs.ErodeGray(WSIZE, HSIZE);
        pix2 = pixs.GrayMorphSequence($"E{WSIZE}.{HSIZE}", 0, 100);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 2 */
        rp.ComparePix(pix1, pix2);  /* 3 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix1 = pixs.OpenGray(WSIZE, HSIZE);
        pix2 = pixs.GrayMorphSequence($"O{WSIZE}.{HSIZE}", 0, 200);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 4 */
        rp.ComparePix(pix1, pix2);  /* 5 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix1 = pixs.CloseGray(WSIZE, HSIZE);
        pix2 = pixs.GrayMorphSequence($"C{WSIZE}.{HSIZE}", 0, 300);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 6 */
        rp.ComparePix(pix1, pix2);  /* 7 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix1 = pixs.Tophat(WSIZE, HSIZE, L.L_TOPHAT_WHITE);
        pix2 = pixs.GrayMorphSequence($"Tw{WSIZE}.{HSIZE}", 0, 400);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 8 */
        rp.ComparePix(pix1, pix2);  /* 9 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix1 = pixs.Tophat(WSIZE, HSIZE, L.L_TOPHAT_BLACK);
        pix2 = pixs.GrayMorphSequence($"Tb{WSIZE}.{HSIZE}", 0, 500);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 10 */
        rp.ComparePix(pix1, pix2);  /* 11 */
        pixa.AddPix(pix1, L.L_INSERT);

        /* ------------- Test erode/dilate duality -------------- */
        pix1 = pixs.DilateGray(WSIZE, HSIZE);
        pix2 = Pix.Invert(null, pixs);
        Pix pix3 = pix2.ErodeGray(WSIZE, HSIZE);
        Pix.Invert(pix3, pix3);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 12 */
        rp.ComparePix(pix1, pix3);  /* 13 */
        pixa.AddPix(pix1, L.L_INSERT);

        /* ------------- Test open/close duality -------------- */
        pix1 = pixs.OpenGray(WSIZE, HSIZE);
        pix2 = Pix.Invert(null, pixs);
        pix3 = pix2.CloseGray(WSIZE, HSIZE);
        Pix.Invert(pix3, pix3);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 14 */
        rp.ComparePix(pix1, pix3);  /* 15 */
        pixa.AddPix(pix1, L.L_INSERT);

        /* ------------- Test tophat duality -------------- */
        pix1 = pixs.Tophat(WSIZE, HSIZE, L.L_TOPHAT_WHITE);
        pix2 = Pix.Invert(null, pixs);
        pix3 = pix2.Tophat(WSIZE, HSIZE, L.L_TOPHAT_BLACK);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 16 */
        rp.ComparePix(pix1, pix3);  /* 17 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix1 = pixs.GrayMorphSequence("Tw9.5", 0, 100);
        pix2 = Pix.Invert(null, pixs);
        pix3 = pix2.GrayMorphSequence("Tb9.5", 0, 300);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 18 */
        rp.ComparePix(pix1, pix3);  /* 19 */
        pixa.AddPix(pix1, L.L_INSERT);

        /* ------------- Test opening/closing for large sels -------------- */
        pix1 = pixs.GrayMorphSequence(
            "C9.9 + C19.19 + C29.29 + C39.39 + C49.49", 0, 100);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 20 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixs.GrayMorphSequence(
            "O9.9 + O19.19 + O29.29 + O39.39 + O49.49", 0, 400);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 21 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix1 = pixa.DisplayTiledInColumns(4, 1.0f, 20, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 22 */

        /* =========================================================== */
        pixa = Pixa.Create(0);
        pix1 = pixs.CloseGray(9, 9);
        pix2 = pix1.Tophat(9, 9, L.L_TOPHAT_WHITE);
        pix3 = pixs.GrayMorphSequence("C9.9 + TW9.9", 0, 0);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 23 */
        rp.ComparePix(pix2, pix3);  /* 24 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pix2.MaxDynamicRange(L.L_LINEAR_SCALE);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 25 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix1 = pixs.CloseGray(29, 29);
        pix2 = pix1.Tophat(29, 29, L.L_TOPHAT_WHITE);
        pix3 = pixs.GrayMorphSequence("C29.29 + Tw29.29", 0, 0);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 26 */
        rp.ComparePix(pix2, pix3);  /* 27 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pix2.MaxDynamicRange(L.L_LINEAR_SCALE);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 28 */
        pixa.AddPix(pix1, L.L_INSERT);

        /* --------- hdome with parameter height = 100 ------------*/
        pix1 = pixs.HDome(100, 4);
        pix2 = pix1.MaxDynamicRange(L.L_LINEAR_SCALE);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 29 */
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 30 */
        pixa.AddPix(pix1, L.L_INSERT);
        pixa.AddPix(pix2, L.L_INSERT);

        /* ----- Contrast enhancement with morph parameters 9, 9 -------*/
        pixs.GetDimensions(out int w, out int h, out _);
        pix1 = Pix.InitAccumulate(w, h, 0x8000u);
        pix1.Accumulate(pixs, L.L_ARITH_ADD);
        pix1.MultConstAccumulate(3.0f, 0x8000u);
        pix2 = pixs.OpenGray(9, 9);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 31 */
        pixa.AddPix(pix2, L.L_INSERT);
        pix1.Accumulate(pix2, L.L_ARITH_SUBTRACT);

        pix2 = pixs.CloseGray(9, 9);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 32 */
        pixa.AddPix(pix2, L.L_INSERT);
        pix1.Accumulate(pix2, L.L_ARITH_SUBTRACT);
        pix2 = pix1.FinalAccumulate(0x8000u, 8);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 33 */
        pixa.AddPix(pix2, L.L_INSERT);

            /* Do the same thing with the Pixacc */
        Pixacc pacc = Pixacc.Create(w, h, 1);
        pacc.Add(pixs);
        pacc.MultConst(3.0f);
        pix1 = pixs.OpenGray(9, 9);
        pacc.Subtract(pix1);
        pix1 = pixs.CloseGray(9, 9);
        pacc.Subtract(pix1);
        pix1 = pacc.Final(8);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 34 */
        pixa.AddPix(pix1, L.L_INSERT);
        rp.ComparePix(pix1, pix2);  /* 35 */

        pix1 = pixa.DisplayTiledInColumns(4, 1.0f, 20, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 36 */

        /* =========================================================== */
        pixa = Pixa.Create(0);

        /* ----  Tophat result on feynman stamp, to extract diagrams ----- */
        pixs = TestPaths.ReadInput("feynman-stamp.jpg");
        pixs.GetDimensions(out w, out h, out _);

        pix1 = Pix.Create(5 * w + 18, h + 6, 32);
        pix1.SetAllArbitrary(0x0000ff00u);

        pix2 = pixs.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        pix1.Rasterop(3, 3, w, h, L.PIX_SRC, pix2, 0, 0);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 37 */
        pixa.AddPix(pix2, L.L_INSERT);

        pix2 = pixs.Colormap != null
            ? pixs.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE)
            : pixs.ConvertRGBToGray(0.33f, 0.34f, 0.33f);
        pix3 = pix2.ConvertTo32();
        pix1.Rasterop(w + 6, 3, w, h, L.PIX_SRC, pix3, 0, 0);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 38 */
        pixa.AddPix(pix3, L.L_INSERT);

        pix3 = pix2.Tophat(3, 3, L.L_TOPHAT_WHITE);
        Pix pix4 = pix3.MaxDynamicRange(L.L_LOG_SCALE);
        Pix pix5 = pix4.ConvertTo32();
        pix1.Rasterop(2 * w + 9, 3, w, h, L.PIX_SRC, pix5, 0, 0);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 39 */
        pixa.AddPix(pix5, L.L_INSERT);

        pix2 = Pix.GammaTRC(null, pix3, 1.0f, 0, 80);
        pix4 = pix2.ThresholdToBinary(70);
        pix5 = pix4.ConvertTo32();
        pix1.Rasterop(3 * w + 12, 3, w, h, L.PIX_SRC, pix5, 0, 0);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 40 */
        pixa.AddPix(pix5, L.L_INSERT);

        Pix.Invert(pix4, pix4);
        pix5 = pix4.ConvertTo32();
        pix1.Rasterop(4 * w + 15, 3, w, h, L.PIX_SRC, pix5, 0, 0);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 41 */
        pixa.AddPix(pix5, L.L_INSERT);

        pix1 = pixa.DisplayTiledInRows(32, 1700, 1.0f, 0, 20, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 42 */

        Assert.Equal(42, rp.Index);
        rp.Cleanup();
    }
}
