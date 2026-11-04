using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// italic_reg.c: demonstrates binary reconstruction for finding italic text.
/// It also tests debug output of word masking.
/// </summary>
/// <remarks>
/// The C goldens were made without gnuplot, so the histogram plot check
/// (C index 3: pixRead("/tmp/lept/ital/runhisto.png") returns null there, which
/// does not advance the index, and makes C's final regTestCompareFiles(rp, 7, 9)
/// fail without adding a golden) is dropped.
/// The golden indices are then: C 0..2 -> 0..2, C 4..9 -> 3..8.
/// </remarks>
public class ItalicRegTests
{
    [RegFact(Skip = "raw Flate stream bytes in PDF goldens 01 and 03 come from C libz (.NET ZLibStream differs in compressed bytes); full check + decompressed PDF raster check runs in ItalicSemanticPdf")]
    public void Italic()
    {
        int size;
        Boxa boxa1, boxa2, boxa3, boxa4;
        Pix pixs, pixm, pix1;
        Pixa pixadb;
        var rp = new RegTest("italic");

        TestPaths.EnsureLeptTempDir("ital");
        pixs = TestPaths.ReadInput("italic.png");

            /* Basic functionality with debug flag */
        pixs.ItalicWords(null, null, out boxa1, 1);
        boxa1.Write("/tmp/lept/ital/ital1.ba");
        rp.CheckFile("/tmp/lept/ital/ital1.ba");  /* 0 */
        rp.CheckFile("/tmp/lept/ital/ital.3.pdf");  /* 1 */
        pix1 = Pix.Read("/tmp/lept/ital/ital.3.png");
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 2 */

            /* Generate word mask */
        pixadb = Pixa.Create(5);
        pixs.WordMaskByDilation(out _, out size, pixadb);
        PdfIo.SetDateAndVersion(0);
        pixadb.ConvertToPdf(100, 1.0f, L.L_FLATE_ENCODE, 0, "Word Mask",
                            "/tmp/lept/ital/wordmask.pdf");
        rp.CheckFile("/tmp/lept/ital/wordmask.pdf");  /* 4 */
        pix1 = pixadb.DisplayTiledInColumns(1, 1.0f, 25, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 5 */
        Log.Info("italic_reg", $"dilation size = {size}");
        string opstring = $"d1.5 + c{size}.1";
        pixm = pixs.MorphSequence(opstring, 0);
        rp.WritePixAndCheck(pixm, L.IFF_PNG);  /* 6 */

            /* Re-run italic finder using the word mask */
        pixs.ItalicWords(null, pixm, out boxa2, 1);
        boxa2.Write("/tmp/lept/ital/ital2.ba");
        rp.CheckFile("/tmp/lept/ital/ital2.ba");  /* 7 */

            /* Re-run italic finder using word mask bounding boxes */
        boxa3 = pixm.ConnComp(8);
        pixs.ItalicWords(boxa3, null, out boxa4, 1);
        boxa3.Write("/tmp/lept/ital/ital3.ba");
        rp.CheckFile("/tmp/lept/ital/ital3.ba");  /* 8 */
        boxa4.Write("/tmp/lept/ital/ital4.ba");
        rp.CheckFile("/tmp/lept/ital/ital4.ba");  /* 9 */
        rp.CompareFiles(6, 8);  /* 10 */

        Assert.Equal(9, rp.Index);
        rp.Cleanup();
    }

    [RegFact]
    public void ItalicSemanticPdf()
    {
        var rp = new RegTest("italic");
        string goldenDir = TestPaths.GoldenDir!;

        TestPaths.EnsureLeptTempDir("ital");
        Pix pixs = TestPaths.ReadInput("italic.png");

            /* Basic functionality with debug flag */
        pixs.ItalicWords(null, null, out Boxa boxa1, 1);
        boxa1.Write("/tmp/lept/ital/ital1.ba");
        rp.CheckFile("/tmp/lept/ital/ital1.ba");  /* 0 */

        string g01 = Path.Combine(goldenDir, "italic_golden.01.pdf");
        PdfRegHelper.AssertFlatePdfEqualsGolden("/tmp/lept/ital/ital.3.pdf", g01);
        rp.CheckFile(g01);  /* 1 */

        Pix pix1 = Pix.Read("/tmp/lept/ital/ital.3.png");
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 2 */

            /* Generate word mask */
        Pixa pixadb = Pixa.Create(5);
        pixs.WordMaskByDilation(out _, out int size, pixadb);
        PdfIo.SetDateAndVersion(0);
        try {
            pixadb.ConvertToPdf(100, 1.0f, L.L_FLATE_ENCODE, 0, "Word Mask",
                                "/tmp/lept/ital/wordmask.pdf");
        } finally {
            PdfIo.SetDateAndVersion(1);
        }
        string g03 = Path.Combine(goldenDir, "italic_golden.03.pdf");
        PdfRegHelper.AssertFlatePdfEqualsGolden("/tmp/lept/ital/wordmask.pdf", g03);
        rp.CheckFile(g03);  /* 3 */

        pix1 = pixadb.DisplayTiledInColumns(1, 1.0f, 25, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 4 */

        string opstring = $"d1.5 + c{size}.1";
        Pix pixm = pixs.MorphSequence(opstring, 0);
        rp.WritePixAndCheck(pixm, L.IFF_PNG);  /* 5 */

            /* Re-run italic finder using the word mask */
        pixs.ItalicWords(null, pixm, out Boxa boxa2, 1);
        boxa2.Write("/tmp/lept/ital/ital2.ba");
        rp.CheckFile("/tmp/lept/ital/ital2.ba");  /* 6 */

            /* Re-run italic finder using word mask bounding boxes */
        Boxa boxa3 = pixm.ConnComp(8);
        pixs.ItalicWords(boxa3, null, out Boxa boxa4, 1);
        boxa3.Write("/tmp/lept/ital/ital3.ba");
        rp.CheckFile("/tmp/lept/ital/ital3.ba");  /* 7 */
        boxa4.Write("/tmp/lept/ital/ital4.ba");
        rp.CheckFile("/tmp/lept/ital/ital4.ba");  /* 8 */
        rp.CompareFiles(6, 8);

        Assert.Equal(9, rp.Index);
        rp.Cleanup();
    }
}
