using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// colormask_reg.c: tests identifying regions in HSV color space by analyzing
/// the HS histogram and building masks that cover peaks in HS.
/// </summary>
public class ColorMaskRegTests
{
    [RegFact(Skip = "needs gnuplot output (gplotSimple1, gplot.c); non-gnuplot checks run in ColorMaskNoGnuplot")]
    public void ColorMask()
    {
        var rp = new RegTest("colormask");

            /* Make a graded frame color */
        Pix pixs = Pix.Create(650, 900, 32);
        for (int i = 0; i < 900; i++) {
            int rval = 40 + i / 30;
            for (int j = 0; j < 650; j++) {
                int gval = 255 - j / 30;
                int bval = 70 + j / 30;
                Pix.ComposeRGBPixel(rval, gval, bval, out uint pixel1);
                pixs.SetPixel(j, i, pixel1);
            }
        }

            /* Place an image inside the frame and convert to HSV */
        Pix pix1 = TestPaths.ReadInput("1555.003.jpg");
        Pix pix2 = pix1.Scale(0.5f, 0.5f);
        pixs.Rasterop(100, 100, 2000, 2000, L.PIX_SRC, pix2, 0, 0);
        Pixa pixa = Pixa.Create(0);
        Pix pixhsv = Pix.ConvertRGBToHSV(null, pixs);

            /* Work in the HS projection of HSV */
        Pix pixh = pixhsv.MakeHistoHS(5, out Numa nahue, out Numa nasat);
        Pix pixg = pixh.MaxDynamicRange(L.L_LOG_SCALE);
        Pix pixf = pixg.ConvertGrayToFalseColor(1.0f);
        rp.WritePixAndCheck(pixf, L.IFF_PNG);   /* 0 */
        pixa.AddPix(pixs, L.L_COPY);
        pixa.AddPix(pixhsv, L.L_INSERT);
        pixa.AddPix(pixg, L.L_INSERT);
        pixa.AddPix(pixf, L.L_INSERT);
        Gplot.Simple1(nahue, L.GPLOT_PNG, "/tmp/lept/regout/junkhue",
                      "Histogram of hue values");
        Pix pix3 = Pix.Read("/tmp/lept/regout/junkhue.png");
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 1 */
        pixa.AddPix(pix3, L.L_INSERT);
        Gplot.Simple1(nasat, L.GPLOT_PNG, "/tmp/lept/regout/junksat",
                      "Histogram of saturation values");
        pix3 = Pix.Read("/tmp/lept/regout/junksat.png");
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 2 */
        pixa.AddPix(pix3, L.L_INSERT);
        Pix pixd = pixa.DisplayTiledAndScaled(32, 270, 7, 0, 30, 3);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 3 */

            /* Find all the peaks */
        Pixa pixapk = Pixa.Create(0);
        pixh.FindHistoPeaksHSV(L.L_HS_HISTO, 20, 20, 6, 2.0f,
                               out Pta ptapk, out _, pixapk);
        pixd = pixapk.DisplayTiledInRows(32, 1400, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 4 */

            /* Make masks for each of the peaks */
        pixa = Pixa.Create(0);
        Pix pixr = pixs.ScaleBySampling(0.4f, 0.4f);
        for (int i = 0; i < 6; i++) {
            ptapk.GetIPt(i, out int x, out int y);
            pix1 = pixr.MakeRangeMaskHS(y, 20, x, 20, L.L_INCLUDE_REGION);
            pixa.AddPix(pix1, L.L_INSERT);
            pixr.GetAverageMaskedRGB(pix1, 0, 0, 1, L.L_MEAN_ABSVAL,
                                     out float frval, out float fgval, out float fbval);
            Pix.ComposeRGBPixel((int)frval, (int)fgval, (int)fbval, out uint pixel1);
            uint pixel2 = pixr.GetPixelAverage(pix1, 0, 0, 1);
            rp.CompareValues(pixel1 >> 8, pixel2 >> 8, 0.0f);  /* 5 - 10 */
            pix2 = Pix.CreateTemplate(pixr);
            pix2.SetAll();
            pix2.PaintThroughMask(pix1, 0, 0, pixel1);
            pixa.AddPix(pix2, L.L_INSERT);
            pix3 = Pix.CreateTemplate(pixr);
            pix3.SetAllArbitrary(pixel1);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        pixd = pixa.DisplayTiledAndScaled(32, 225, 3, 0, 30, 3);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 11 */

        Assert.Equal(11, rp.Index);
        rp.Cleanup();
    }

    [RegFact]
    public void ColorMaskNoGnuplot()
    {
        var rp = new RegTest("colormask");

            /* Make a graded frame color */
        Pix pixs = Pix.Create(650, 900, 32);
        for (int i = 0; i < 900; i++) {
            int rval = 40 + i / 30;
            for (int j = 0; j < 650; j++) {
                int gval = 255 - j / 30;
                int bval = 70 + j / 30;
                Pix.ComposeRGBPixel(rval, gval, bval, out uint pixel1);
                pixs.SetPixel(j, i, pixel1);
            }
        }

            /* Place an image inside the frame and convert to HSV */
        Pix pix1 = TestPaths.ReadInput("1555.003.jpg");
        Pix pix2 = pix1.Scale(0.5f, 0.5f);
        pixs.Rasterop(100, 100, 2000, 2000, L.PIX_SRC, pix2, 0, 0);
        Pixa pixa = Pixa.Create(0);
        Pix pixhsv = Pix.ConvertRGBToHSV(null, pixs);

            /* Work in the HS projection of HSV (goldens in testdata/golden were built without gnuplot) */
        Pix pixh = pixhsv.MakeHistoHS(5, out Numa nahue, out Numa nasat);
        Pix pixg = pixh.MaxDynamicRange(L.L_LOG_SCALE);
        Pix pixf = pixg.ConvertGrayToFalseColor(1.0f);
        rp.WritePixAndCheck(pixf, L.IFF_PNG);   /* 0 */
        pixa.AddPix(pixs, L.L_COPY);
        pixa.AddPix(pixhsv, L.L_INSERT);
        pixa.AddPix(pixg, L.L_INSERT);
        pixa.AddPix(pixf, L.L_INSERT);
        Gplot.Simple1(nahue, L.GPLOT_PNG, "/tmp/lept/regout/junkhue", "Histogram of hue values");
        Gplot.Simple1(nasat, L.GPLOT_PNG, "/tmp/lept/regout/junksat", "Histogram of saturation values");
        Pix pixd = pixa.DisplayTiledAndScaled(32, 270, 7, 0, 30, 3);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 1 */

            /* Find all the peaks */
        Pixa pixapk = Pixa.Create(0);
        pixh.FindHistoPeaksHSV(L.L_HS_HISTO, 20, 20, 6, 2.0f,
                               out Pta ptapk, out _, pixapk);
        pixd = pixapk.DisplayTiledInRows(32, 1400, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 2 */

            /* Make masks for each of the peaks */
        pixa = Pixa.Create(0);
        Pix pixr = pixs.ScaleBySampling(0.4f, 0.4f);
        for (int i = 0; i < 6; i++) {
            ptapk.GetIPt(i, out int x, out int y);
            pix1 = pixr.MakeRangeMaskHS(y, 20, x, 20, L.L_INCLUDE_REGION);
            pixa.AddPix(pix1, L.L_INSERT);
            pixr.GetAverageMaskedRGB(pix1, 0, 0, 1, L.L_MEAN_ABSVAL,
                                     out float frval, out float fgval, out float fbval);
            Pix.ComposeRGBPixel((int)frval, (int)fgval, (int)fbval, out uint pixel1);
            uint pixel2 = pixr.GetPixelAverage(pix1, 0, 0, 1);
            rp.CompareValues(pixel1 >> 8, pixel2 >> 8, 0.0f);  /* 3 - 8 */
            pix2 = Pix.CreateTemplate(pixr);
            pix2.SetAll();
            pix2.PaintThroughMask(pix1, 0, 0, pixel1);
            pixa.AddPix(pix2, L.L_INSERT);
            Pix pix3 = Pix.CreateTemplate(pixr);
            pix3.SetAllArbitrary(pixel1);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        pixd = pixa.DisplayTiledAndScaled(32, 225, 3, 0, 30, 3);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 9 */

        Assert.Equal(9, rp.Index);
        rp.Cleanup();
    }
}
