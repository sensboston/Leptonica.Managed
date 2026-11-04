using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// enhance_reg.c: tests global enhancement functions (TRC, HSV hue/saturation,
/// contrast, unsharp masking, constant-hue mapping, mosaic color shift, matrix color).
/// </summary>
[Collection("ConvolveSampling")]
public class EnhanceRegTests
{
    [RegFact(Skip = "needs gnuplot output (gplotSimple1/gplotSimple2, gplot.c); non-gnuplot checks run in EnhanceNoGnuplot")]
    public void Enhance()
    {
        RunEnhance(useGnuplot: true);
    }

    [RegFact]
    public void EnhanceNoGnuplot()
    {
        RunEnhance(useGnuplot: false);
    }

    private static void RunEnhance(bool useGnuplot)
    {
        var rp = new RegTest("enhance");

        try {
            Pix.SetConvolveSampling(1, 1);
            Utils.LeptMkdir("lept/enhance");
            Pix pix = TestPaths.ReadInput("test24.jpg");
            int w = pix.Width;
            float scalefact = 150.0f / w;
            Pix pixs = pix.Scale(scalefact, scalefact);
            w = pixs.Width;
            Pixa pixaf = Pixa.Create(5);

                /* TRC: vary gamma */
            Pixa pixa1 = Pixa.Create(20);
            for (int i = 0; i < 20; i++) {
                Pix pix0 = Pix.GammaTRC(null, pixs, (float)(0.3 + 0.15 * i), 0, 255);
                pixa1.AddPix(pix0, L.L_INSERT);
            }
            Pix pix1 = pixa1.DisplayTiledAndScaled(32, w, 5, 0, 10, 2);
            pixaf.AddPix(pix1, L.L_INSERT);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */

                /* TRC: vary black point */
            pixa1 = Pixa.Create(20);
            for (int i = 0; i < 20; i++) {
                Pix pix0 = Pix.GammaTRC(null, pixs, 1.0f, 5 * i, 255);
                pixa1.AddPix(pix0, L.L_INSERT);
            }
            pix1 = pixa1.DisplayTiledAndScaled(32, w, 5, 0, 10, 2);
            pixaf.AddPix(pix1, L.L_INSERT);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 1 */

                /* Vary hue */
            pixa1 = Pixa.Create(20);
            for (int i = 0; i < 20; i++) {
                Pix pix0 = Pix.ModifyHue(null, pixs, (float)(0.01 + 0.05 * i));
                pixa1.AddPix(pix0, L.L_INSERT);
            }
            pix1 = pixa1.DisplayTiledAndScaled(32, w, 5, 0, 10, 2);
            pixaf.AddPix(pix1, L.L_INSERT);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 2 */

                /* Vary saturation */
            pixa1 = Pixa.Create(20);
            Numa na1 = Numa.Create(20);
            for (int i = 0; i < 20; i++) {
                Pix pix0 = Pix.ModifySaturation(null, pixs, (float)(-0.9 + 0.1 * i));
                float sat = pix0.MeasureSaturation(1);
                pixa1.AddPix(pix0, L.L_INSERT);
                na1.AddNumber(sat);
            }
            pix1 = pixa1.DisplayTiledAndScaled(32, w, 5, 0, 10, 2);
            pixaf.AddPix(pix1, L.L_INSERT);
            Gplot.Simple1(na1, L.GPLOT_PNG, "/tmp/lept/regout/enhance.7", "Average Saturation");
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 3 */

                /* Vary contrast */
            pixa1 = Pixa.Create(20);
            for (int i = 0; i < 20; i++) {
                Pix pix0 = Pix.ContrastTRC(null, pixs, (float)(0.1 * i));
                pixa1.AddPix(pix0, L.L_INSERT);
            }
            pix1 = pixa1.DisplayTiledAndScaled(32, w, 5, 0, 10, 2);
            pixaf.AddPix(pix1, L.L_INSERT);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 4 */

                /* Vary sharpening */
            pixa1 = Pixa.Create(20);
            for (int i = 0; i < 20; i++) {
                Pix pix0 = pixs.UnsharpMasking(3, (float)(0.01 + 0.15 * i));
                pixa1.AddPix(pix0, L.L_INSERT);
            }
            pix1 = pixa1.DisplayTiledAndScaled(32, w, 5, 0, 10, 2);
            pixaf.AddPix(pix1, L.L_INSERT);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 5 */

                /* Hue constant mapping to lighter background */
            Pixa pixa2 = Pixa.Create(2);
            L_Bmf bmf10 = L_Bmf.Create(System.IO.Path.Combine(TestPaths.ProgDir, "fonts"), 10);
            Pix pixBg = TestPaths.ReadInput("candelabrum.011.jpg");
            Pix.ComposeRGBPixel(230, 185, 144, out uint srcval);
            for (int k = 1; k > -2; k -= 2) {
                pixa1 = Pixa.Create(11);
                for (int i = 0; i <= 10; i++) {
                    float fract = (float)(k * 0.10 * i);
                    pix1 = Pix.MapWithInvariantHue(null, pixBg, srcval, fract);
                    string textstr = CFormat.Sprintf("Fract = %5.1f", fract);
                    Pix pix2 = pix1.AddSingleTextblock(bmf10, textstr, 0xff000000, L.L_ADD_BELOW, out _);
                    pixa1.AddPix(pix2, L.L_INSERT);
                }
                Pix pix3Tile = pixa1.DisplayTiledInColumns(4, 1.0f, 30, 2);
                pixa2.AddPix(pix3Tile, L.L_INSERT);
            }
            Pix pixd = pixa2.DisplayTiledInColumns(2, 0.5f, 30, 2);
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 6 */

                /* Delayed testing of saturation plot */
            if (useGnuplot)
                rp.CheckFile("/tmp/lept/regout/enhance.7.png");  /* 7 */
            else
                rp.CompareValues(0, 0, 0);  /* advance index past gnuplot check 7 */

                /* Display results */
            pixd = pixaf.DisplayTiledInColumns(1, 1.0f, 20, 2);
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 8 */

                /* Test color shifts */
            pixd = pixs.MosaicColorShiftRGB(-0.1f, 0.0f, 0.0f, 0.0999f, 1);
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 9 */

                /* More trc testing */
            pix = TestPaths.ReadInput("test24.jpg");
            pixs = pix.Scale(0.3f, 0.3f);
            pixa1 = Pixa.Create(5);
            pixa1.AddPix(pixs, L.L_COPY);

            na1 = Numa.GammaTRC(0.6f, 40, 200);
            Numa na2 = Numa.GammaTRC(1.2f, 40, 225);
            Numa na3 = Numa.GammaTRC(0.6f, 40, 255);
            pixs.GetDimensions(out w, out int h, out _);
            pix1 = Pix.Copy(null, pixs);
            Pix pixMask = Pix.MakeSymmetricMask(w, h, 0.5f, 0.5f, L.L_USE_INNER);
            pixa1.AddPix(pixMask, L.L_COPY);
            pix1.TRCMapGeneral(pixMask, na1, na2, na3);
            pixa1.AddPix(pix1, L.L_COPY);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 10 */

            na1 = Numa.GammaTRC(1.0f, 0, 255);
            na2 = Numa.GammaTRC(1.0f, 0, 255);
            na3 = Numa.GammaTRC(1.0f, 0, 255);
            pix1 = Pix.Copy(null, pixs);
            pix1.TRCMapGeneral(null, na1, na2, na3);
            rp.ComparePix(pixs, pix1);  /* 11 */

            na1 = Numa.GammaTRC(1.7f, 150, 255);
            na2 = Numa.GammaTRC(0.7f, 0, 150);
            na3 = Numa.GammaTRC(1.2f, 80, 200);
            pixs.TRCMapGeneral(null, na1, na2, na3);
            rp.WritePixAndCheck(pixs, L.IFF_PNG);  /* 12 */
            pixa1.AddPix(pixs, L.L_COPY);

            na1 = Numa.GammaTRC(0.8f, 0, 220);
            na2 = Numa.GammaTRC(1.0f, 40, 220);
            Gplot.Simple2(na1, na2, L.GPLOT_PNG, "/tmp/lept/enhance/junkp", null);
            if (useGnuplot) {
                pix1 = Pix.Read("/tmp/lept/enhance/junkp.png");
                rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 13 */
                pixa1.AddPix(pix1, L.L_COPY);
            }

            pixd = pixa1.DisplayTiledInColumns(4, 1.0f, 30, 2);
            rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 14 (13 without gnuplot) */

                /* -----------------------------------------------*
                 *           Test global color transforms         *
                 * -----------------------------------------------*/
            pix = TestPaths.ReadInput("wet-day.jpg");
            Pix pixs1 = pix.OctreeColorQuant(200, 0);
            Pix pixs2 = pixs1.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
            rp.ComparePix(pixs1, pixs2);  /* 15 (14) */

                /* Make a diagonal color transform matrix */
            L_Kernel kel = L_Kernel.Create(3, 3);
            kel.SetElement(0, 0, 0.7f);
            kel.SetElement(1, 1, 0.4f);
            kel.SetElement(2, 2, 1.3f);

                /* Apply to both cmap and rgb images. */
            pix1 = pixs1.MultMatrixColor(kel);
            Pix pix2Mat = pixs2.MultMatrixColor(kel);
            rp.ComparePix(pix1, pix2Mat);  /* 16 (15) */

                /* Apply the same transform in the simpler interface */
            Pix pix3 = pixs1.MultConstantColor(0.7f, 0.4f, 1.3f);
            Pix pix4 = pixs2.MultConstantColor(0.7f, 0.4f, 1.3f);
            rp.ComparePix(pix3, pix4);  /* 17 (16) */
            rp.ComparePix(pix1, pix3);  /* 18 (17) */
            rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 19 (18) */

            Assert.Equal(useGnuplot ? 19 : 18, rp.Index);
        } finally {
            Pix.SetConvolveSampling(1, 1);
        }

        rp.Cleanup();
    }
}
