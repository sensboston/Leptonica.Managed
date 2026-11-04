using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// findpattern2_reg.c: demonstrates three methods (boundary, runs, random) for generating
/// hit-miss SELs from a 1 bpp pattern image, and pattern matching/removal.
/// </summary>
public class FindPattern2RegTests
{
    private const uint HitColor = 0x33aa4400u;
    private const uint MissColor = 0xaa44bb00u;

    [RegFact(Skip = "raw Flate stream bytes in PDF goldens 04, 06, 08 come from C libz (.NET ZLibStream differs in compressed bytes); all 11 checks + decompressed PDF raster checks run in FindPattern2SemanticPdf")]
    public void FindPattern2()
    {
        RunFindPattern2(semanticPdfCheck: false);
    }

    [RegFact]
    public void FindPattern2SemanticPdf()
    {
        RunFindPattern2(semanticPdfCheck: true);
    }

    private static void RunFindPattern2(bool semanticPdfCheck)
    {
        var rp = new RegTest("findpattern2");
        string goldenDir = TestPaths.GoldenDir!;

        TestPaths.EnsureLeptTempDir("pattern");

        Pix pixs = TestPaths.ReadInput("asterisk.png");
        Pix pixt = TestPaths.ReadInput("one-asterisk.png");

            /* Boundary method */
        Pixa pixa1 = Pixa.Create(7);
        for (int i = 0; i <= 6; i++) {
            Sel selAst = pixt.GenerateSelBoundary(2, 2, i, i, 1, 0, 1, 1, out Pix? pixe);
            Pix pixSel = pixe!.DisplayHitMissSel(selAst, 7, HitColor, MissColor);
            pixa1.AddPix(pixSel, L.L_INSERT);
        }
        Pix pix2 = pixa1.DisplayTiledInColumns(7, 1.0f, 25, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 0 */

            /* Run method */
        pixa1 = Pixa.Create(7);
        for (int i = 9; i <= 16; i++) {
            Sel selAst = pixt.GenerateSelWithRuns(i, i, 1, 3, 0, 0, 0, 0, out Pix? pixe);
            Pix pixSel = pixe!.DisplayHitMissSel(selAst, 7, HitColor, MissColor);
            pixa1.AddPix(pixSel, L.L_INSERT);
        }
        pix2 = pixa1.DisplayTiledInColumns(8, 1.0f, 25, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 1 */

            /* Random method */
        Sel selRand = pixt.GenerateSelRandom(0.15f, 0.10f, 1, 3, 0, 3, 3, out Pix? pixRandE);
        Pix pixRandSel = pixRandE!.DisplayHitMissSel(selRand, 7, HitColor, MissColor);
        rp.WritePixAndCheck(pixRandSel, L.IFF_PNG);  /* 2 */

        PdfIo.SetDateAndVersion(0);
        try {
                /* Boundary match */
            Sel sel1 = pixt.GenerateSelBoundary(2, 2, 5, 5, 1, 0, 1, 1, out Pix? pix1);
            DoPatternMatch(pixs, pix1!, sel1, "/tmp/lept/pattern/match1.pdf", rp);  /* 3 */
            CheckPdf(rp, "/tmp/lept/pattern/match1.pdf", Path.Combine(goldenDir, "findpattern2_golden.04.pdf"), semanticPdfCheck);  /* 4 */

                /* Run match */
            sel1 = pixt.GenerateSelWithRuns(11, 11, 1, 3, 0, 0, 0, 0, out pix1);
            DoPatternMatch(pixs, pix1!, sel1, "/tmp/lept/pattern/match2.pdf", rp);  /* 5 */
            CheckPdf(rp, "/tmp/lept/pattern/match2.pdf", Path.Combine(goldenDir, "findpattern2_golden.06.pdf"), semanticPdfCheck);  /* 6 */

                /* Random match */
            sel1 = pixt.GenerateSelRandom(0.15f, 0.12f, 1, 0, 0, 0, 0, out pix1);
            DoPatternMatch(pixs, pix1!, sel1, "/tmp/lept/pattern/match3.pdf", rp);  /* 7 */
            CheckPdf(rp, "/tmp/lept/pattern/match3.pdf", Path.Combine(goldenDir, "findpattern2_golden.08.pdf"), semanticPdfCheck);  /* 8 */
        } finally {
            PdfIo.SetDateAndVersion(1);
        }

            /* Brute-force pattern removal */
        Sel selB = pixt.GenerateSelBoundary(2, 2, 5, 5, 1, 0, 1, 1, out Pix? pixB);
        pix2 = Pix.HMT(null, pixs, selB);
        selB.GetParameters(out _, out _, out int cy, out int cx);
        Sel sel2 = Sel.CreateFromPix(pixB!, cy, cx, null);
        Pix pix3 = Pix.Dilate(null, pix2, sel2);
        Pix.DilateBrick(pix3, pix3, 4, 4);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 9 */
        Pix pix4 = Pix.Subtract(null, pixs, pix3);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 10 */

        Assert.Equal(10, rp.Index);
        rp.Cleanup();
    }

    private static void CheckPdf(RegTest rp, string actual, string golden, bool semantic)
    {
        if (semantic) {
            PdfRegHelper.AssertFlatePdfEqualsGolden(actual, golden);
            rp.CheckFile(golden);
        } else {
            rp.CheckFile(actual);
        }
    }

    private static void DoPatternMatch(Pix pixs, Pix pixt, Sel sel, string fname, RegTest rp)
    {
        Pixa pixa1 = Pixa.Create(7);
        pixa1.AddPix(pixs, L.L_COPY);
        Pix pixt1 = pixt.Scale(8.0f, 8.0f);
        pixa1.AddPix(pixt1, L.L_INSERT);
        Pix pixsel = pixt.DisplayHitMissSel(sel, 7, HitColor, MissColor);
        pixa1.AddPix(pixsel, L.L_INSERT);

        Pix pix1 = Pix.HMT(null, pixs, sel);
        pixa1.AddPix(pix1, L.L_INSERT);
        sel.GetParameters(out _, out _, out int cy, out int cx);
        Pix? pix2 = pixs.DisplayMatchedPattern(pixt, pix1, cx, cy, 0x0000ff00u, 1.0f, 5);
        pixa1.AddPix(pix2!, L.L_INSERT);

        Pix pix3 = Pix.Copy(null, pixs);
        pix3.RemoveMatchedPattern(pixt, pix1, cx, cy, 2);
        pixa1.AddPix(pix3, L.L_INSERT);

        Pix pix4 = pixa1.DisplayTiledInColumns(7, 1.0f, 15, 2);
        pixa1.AddPix(pix4, L.L_INSERT);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);
        pixa1.ConvertToPdf(100, 1.0f, L.L_FLATE_ENCODE, 50, null, fname);
    }
}
