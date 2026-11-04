using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// compare_reg.c: tests comparison of images that are translated with respect to each other
/// (pixBestCorrelation, pixCompareWithTranslation) or slightly different in content
/// (pixGetPerceptualDiff).
/// </summary>
public class CompareRegTests
{
    [RegFact(Skip = "raw Flate stream bytes in PDF goldens 05 and 06 come from C libz (.NET ZLibStream differs in compressed bytes); all 13 checks + decompressed PDF raster checks run in CompareSemanticPdf")]
    public void Compare()
    {
        RunCompare(semanticPdfCheck: false);
    }

    [RegFact]
    public void CompareSemanticPdf()
    {
        RunCompare(semanticPdfCheck: true);
    }

    private static void RunCompare(bool semanticPdfCheck)
    {
        var rp = new RegTest("compare");
        string goldenDir = TestPaths.GoldenDir!;

        /* ------------ Test of pixBestCorrelation() --------------- */
        Pix pix0 = TestPaths.ReadInput("harmoniam100-11.png");
        Pix pix1 = pix0.ConvertTo1(160);
        pix1.GetDimensions(out int w, out int h, out _);

        Pix pix2 = Pix.Create(w - 10, h, 1);
        pix2.Rasterop(0, 0, w, h, L.PIX_SRC, pix1, 32, 12);

        int[] stab = Pix.MakePixelSumTab8();
        int[] ctab = Pix.MakePixelCentroidTab8();
        int area1 = pix1.CountPixels(stab);
        int area2 = pix2.CountPixels(stab);
        pix1.Centroid(ctab, stab, out float cx1, out float cy1);
        pix2.Centroid(ctab, stab, out float cx2, out float cy2);
        int etransx = Utils.RoundFToI(cx1 - cx2);
        int etransy = Utils.RoundFToI(cy1 - cy2);

        pix1.BestCorrelation(pix2, area1, area2, etransx, etransy,
                             4, stab, out int delx, out int dely, out _, 5);
        rp.CompareValues(32, delx, 0.0f);   /* 0 */
        rp.CompareValues(12, dely, 0.0f);   /* 1 */
        Utils.LeptMv("/tmp/lept/comp/correl_5.png", "lept/regout", null, out _);
        rp.CheckFile("/tmp/lept/regout/correl_5.png");   /* 2 */

        /* ------------ Test of pixCompareWithTranslation() ------------ */
        pix1 = TestPaths.ReadInput("harmoniam-11.tif");
        pix2 = Pix.Translate(null, pix1, -45, 25, L.L_BRING_IN_WHITE);
        PdfIo.SetDateAndVersion(0);
        try {
            pix1.CompareWithTranslation(pix2, 160, out delx, out dely, out _, true);
        } finally {
            PdfIo.SetDateAndVersion(1);
        }
        rp.CompareValues(45, delx, 0.0f);    /* 3 */
        rp.CompareValues(-25, dely, 0.0f);   /* 4 */
        Utils.LeptMv("/tmp/lept/comp/correl.pdf", "lept/regout", null, out _);
        Utils.LeptMv("/tmp/lept/comp/compare.pdf", "lept/regout", null, out _);

        if (semanticPdfCheck) {
            string g05 = Path.Combine(goldenDir, "compare_golden.05.pdf");
            string g06 = Path.Combine(goldenDir, "compare_golden.06.pdf");
            PdfRegHelper.AssertFlatePdfEqualsGolden(Utils.GenPathname("/tmp/lept/regout/compare.pdf", null), g05);
            rp.CheckFile(g05);  /* 5 */
            PdfRegHelper.AssertFlatePdfEqualsGolden(Utils.GenPathname("/tmp/lept/regout/correl.pdf", null), g06);
            rp.CheckFile(g06);  /* 6 */
        } else {
            rp.CheckFile("/tmp/lept/regout/compare.pdf");   /* 5 */
            rp.CheckFile("/tmp/lept/regout/correl.pdf");    /* 6 */
        }

        /* ------------ Test of pixGetPerceptualDiff() --------------- */
        pix0 = TestPaths.ReadInput("greencover.jpg");
        pix1 = TestPaths.ReadInput("redcover.jpg");
        pix0.GetPerceptualDiff(pix1, 1, 3, 20, out float fract, out Pix? pixd2, out Pix? pixd3);
        rp.CompareValues(0.061252f, fract, 0.01f);          /* 7 */
        rp.WritePixAndCheck(pixd2!, L.IFF_JFIF_JPEG);       /* 8 */
        rp.WritePixAndCheck(pixd3!, L.IFF_TIFF_G4);         /* 9 */

        pix2 = pix0.ConvertTo8(false);
        Pix pix3 = pix1.ConvertTo8(false);
        pix2.GetPerceptualDiff(pix3, 1, 3, 20, out fract, out Pix? pix4, out Pix? pix5);
        rp.CompareValues(0.046928f, fract, 0.0002f);        /* 10 */
        rp.WritePixAndCheck(pix4!, L.IFF_JFIF_JPEG);        /* 11 */
        rp.WritePixAndCheck(pix5!, L.IFF_TIFF_G4);          /* 12 */

        Assert.Equal(12, rp.Index);
        rp.Cleanup();
    }
}
