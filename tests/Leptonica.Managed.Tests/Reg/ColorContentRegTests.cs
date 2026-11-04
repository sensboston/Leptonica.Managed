using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// colorcontent_reg.c: tests various color content functions, including simple color quantization,
/// gray color counting, background color region detection, and RGB gamut plane masks.
/// </summary>
public class ColorContentRegTests
{
    [RegFact(Skip = "raw Flate stream bytes in golden 18 (maskgen.pdf) come from C libz (.NET ZLibStream differs in compressed bytes); all 19 checks + decompressed PDF raster check run in ColorContentSemanticPdf")]
    public void ColorContent()
    {
        RunColorContent(semanticPdfCheck: false);
    }

    [RegFact]
    public void ColorContentSemanticPdf()
    {
        RunColorContent(semanticPdfCheck: true);
    }

    private static void RunColorContent(bool semanticPdfCheck)
    {
        const string fname = "/tmp/lept/colorcontent/maskgen.pdf";
        var rp = new RegTest("colorcontent");

            /* Find the most populated colors */
        Pix pix1 = TestPaths.ReadInput("fish24.jpg");
        pix1.GetMostPopulatedColors(2, 3, 10, out uint[] colors, out _);
        Pix pix2 = Pix.DisplayColorArray(colors, 10, 200, 5, 6);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 0 */

            /* Do a simple color quantization with sigbits = 2 */
        pix2 = pix1.SimpleColorQuantize(2, 3, 10);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 1 */
        Pix pix3 = pix2.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        rp.ComparePix(pix2, pix3);  /* 2 */
        int ncolors = pix3.NumColors(1);
        rp.CompareValues(ncolors, 10, 0.0f);  /* 3 */

            /* Do a simple color quantization with sigbits = 3 */
        pix1 = TestPaths.ReadInput("wyom.jpg");
        ncolors = pix1.NumColors(1);
        rp.CompareValues(ncolors, 132165, 10000.0f);  /* 4 */
        pix2 = pix1.SimpleColorQuantize(3, 3, 20);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 5 */
        ncolors = pix2.Colormap!.Count;
        rp.CompareValues(ncolors, 20, 0.0f);  /* 6 */

            /* Find the number of perceptually significant gray intensities */
        pix1 = TestPaths.ReadInput("marge.jpg");
        pix2 = pix1.ConvertTo8(false);
        ncolors = pix2.NumSignificantGrayColors(20, 236, 0.0001f, 1);
        rp.CompareValues(ncolors, 219, 0.0f);  /* 7 */

            /* Find background color in image with light color regions */
        pix1 = TestPaths.ReadInput("map.057.jpg");
        Pixa pixa1 = Pixa.Create(0);
        pix1.FindColorRegions(null, 4, 200, 70, 10, 90, 0.05f,
                              out _, out Pix? pixMask, out _, pixa1);
        rp.WritePixAndCheck(pixMask!, L.IFF_PNG);  /* 8 */
        pix3 = pixa1.DisplayTiledInColumns(5, 0.3f, 20, 2);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 9 */

            /* Show binary classification of RGB colors using a plane */
        pix1 = Pix.MakeGamutRGB(3);
        pix2 = pix1.MakeArbMaskFromRGB(-0.5f, -0.5f, 1.0f, 20.0f);
        pix1.GetDimensions(out int w, out int h, out _);
        pix3 = Pix.Create(w, h, 32);
        pix3.SetAll();
        pix3.CombineMasked(pix1, pix2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 10 */
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 11 */
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 12 */

            /* Show use of more than one plane to further restrict the allowed region of RGB color space */
        pixa1 = Pixa.Create(0);
        pix1 = Pix.MakeGamutRGB(3);
        pix2 = pix1.MakeArbMaskFromRGB(-0.5f, -0.5f, 1.0f, 20.0f);
        pix3 = pix1.MakeArbMaskFromRGB(1.5f, -0.5f, -1.0f, 0.0f);
        Pix pix4 = pix1.MakeArbMaskFromRGB(0.4f, 0.3f, 0.3f, 60.0f);
        Pix.Invert(pix4, pix4);
        Pix pix5 = Pix.Subtract(null, pix2, pix3);
        Pix pix6 = Pix.Subtract(null, pix5, pix4);
        pix1.GetDimensions(out w, out h, out _);
        Pix pix7 = Pix.Create(w, h, 32);
        pix7.SetAll();
        pix7.CombineMasked(pix1, pix6);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 13 */
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 14 */
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 15 */
        rp.WritePixAndCheck(pix6, L.IFF_PNG);  /* 16 */
        rp.WritePixAndCheck(pix7, L.IFF_PNG);  /* 17 */
        pixa1.AddPix(pix1, L.L_INSERT);
        pixa1.AddPix(pix2, L.L_INSERT);
        pixa1.AddPix(pix3, L.L_INSERT);
        pixa1.AddPix(pix4, L.L_INSERT);
        pixa1.AddPix(pix5, L.L_INSERT);
        pixa1.AddPix(pix6, L.L_INSERT);
        pixa1.AddPix(pix7, L.L_INSERT);
        TestPaths.EnsureLeptTempDir("colorcontent");
        PdfIo.SetDateAndVersion(0);
        try {
            pixa1.ConvertToPdf(0, 0.5f, L.L_FLATE_ENCODE, 0, null, fname);
        } finally {
            PdfIo.SetDateAndVersion(1);
        }

        if (semanticPdfCheck) {
            string golden18 = Path.Combine(TestPaths.GoldenDir!, "colorcontent_golden.18.pdf");
            PdfRegHelper.AssertFlatePdfEqualsGolden(fname, golden18);
            rp.CheckFile(golden18);  /* 18 */
        } else {
            rp.CheckFile(fname);  /* 18 */
        }

        Assert.Equal(18, rp.Index);
        rp.Cleanup();
    }
}
