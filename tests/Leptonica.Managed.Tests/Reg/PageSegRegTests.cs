using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// pageseg_reg.c: regression test for some of the page segmentation algorithms:
/// generic page segmentation, foreground finding, greedy whitespace rectangle finder,
/// table finder, text auto-inversion.
/// </summary>
public class PageSegRegTests
{
    [RegFact(Skip = "golden 21 (pageseg_golden.21.pdf) uses JPEG/G4 encoding via L_DEFAULT_ENCODE; all 36 non-PDF checks + structural PDF check run in PageSegNoCodecPdf")]
    public void PageSeg()
    {
        int w, h, istable, score;
        Box? box;
        Boxa boxa;
        Pix pixs, pix1, pix2, pix3, pix4, pix5;
        Pix pixhm, pixtm, pixtb, pixdb;
        Pixa pixadb;
        Pixacomp pixac;
        var rp = new RegTest("pageseg");

            /* Test the generic page segmentation */
        pixs = TestPaths.ReadInput("pageseg1.tif");
        pixadb = Pixa.Create(0);
        pixs.GetRegionsBinary(out pixhm, out pixtm, out pixtb, pixadb);

        int n = pixadb.Count;
        for (int i = 0; i < n; i++) {
            pix1 = pixadb.GetPix(i, L.L_CLONE);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 - 19 */
        }

            /* Test foreground finding */
        string[] fnames = {
            "lion-page.00010.jpg", "lion-page.00011.png", "lion-page.00012.png",
            "lion-page.00013.png", "lion-page.00016.jpg", "lion-page.00017.png"
        };
        n = fnames.Length;
        boxa = Boxa.Create(n);
        box = Box.Create(0, 0, 0, 0);  /* invalid box */
        boxa.InitFull(box);  /* Init to invalid boxes */
        pixac = Pixacomp.Create(n);
        for (int i = 0; i < n; i++) {
            pix1 = TestPaths.ReadInput(fnames[i]);
            box = pix1.FindPageForeground(170, 70, 30, 0, pixac);
            if (box == null)
                continue;
            boxa.ReplaceBox(i, box);
        }
        byte[] data = boxa.WriteMem();
        rp.WriteDataAndCheck(data, "ba");  /* 20 */
        PdfIo.SetDateAndVersion(0);
        data = pixac.ConvertToPdfData(0, 1.0f, L.L_DEFAULT_ENCODE, 0, "Page foreground");
        rp.WriteDataAndCheck(data, "pdf");  /* 21 */

            /* Test the greedy rectangle finder for white space */
        pix1 = pixs.Scale(0.5f, 0.5f);
        pix1.FindLargeRectangles(0, 20, out boxa, out pixdb);
        rp.WritePixAndCheck(pixdb, L.IFF_PNG);  /* 22 */

            /* Test the table finder */
        pix1 = TestPaths.ReadInput("table.15.tif");
        pixadb = Pixa.Create(0);
        score = pix1.DecideIfTable(null, L.L_PORTRAIT_MODE, pixadb);
        istable = (score >= 2) ? 1 : 0;
        rp.CompareValues(1.0f, istable, 0.0f);  /* 23 */
        pix2 = pixadb.DisplayTiledInRows(32, 2000, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 24 */

        pix1 = TestPaths.ReadInput("table.27.tif");
        pixadb = Pixa.Create(0);
        score = pix1.DecideIfTable(null, L.L_PORTRAIT_MODE, pixadb);
        istable = (score >= 2) ? 1 : 0;
        rp.CompareValues(1.0f, istable, 0.0f);  /* 25 */
        pix2 = pixadb.DisplayTiledInRows(32, 2000, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 26 */

        pix1 = TestPaths.ReadInput("table.150.png");
        pixadb = Pixa.Create(0);
        score = pix1.DecideIfTable(null, L.L_PORTRAIT_MODE, pixadb);
        istable = (score >= 2) ? 1 : 0;
        rp.CompareValues(1.0f, istable, 0.0f);  /* 27 */
        pix2 = pixadb.DisplayTiledInRows(32, 2000, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 28 */

        pix1 = TestPaths.ReadInput("toc.99.tif");  /* not a table */
        pixadb = Pixa.Create(0);
        score = pix1.DecideIfTable(null, L.L_PORTRAIT_MODE, pixadb);
        istable = (score >= 2) ? 1 : 0;
        rp.CompareValues(0.0f, istable, 0.0f);  /* 29 */
        pix2 = pixadb.DisplayTiledInRows(32, 2000, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 30 */

            /* Tests of auto-inversion of text */
        pix1 = TestPaths.ReadInput("zanotti-78.jpg");
        pix2 = pix1.ConvertRGBToLuminance();
        pixadb = Pixa.Create(0);
        pixadb.AddPix(pix2, L.L_COPY);
        pix2.GetDimensions(out w, out h, out _);
        pix2.Rasterop((int)(0.2 * w), (int)(0.08 * h), (int)(0.4 * w), (int)(0.23 * h),
                      L.PIX_NOT(L.PIX_DST), null, 0, 0);
        pix2.Rasterop((int)(0.6 * w), (int)(0.5 * h), (int)(0.2 * w), (int)(0.15 * h),
                      L.PIX_NOT(L.PIX_DST), null, 0, 0);
        pix3 = pix2.AutoPhotoinvert(128, out Pix? pixm4, pixadb);
        pix4 = pixm4!;
        pix5 = pixadb.DisplayTiledInColumns(5, 0.3f, 20, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 31 */
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 32 */
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 33 */

        pix1 = TestPaths.ReadInput("invertedtext.tif");
        pixadb = Pixa.Create(0);
        pixadb.AddPix(pix1, L.L_COPY);
        pix2 = pix1.AutoPhotoinvert(128, out Pix? pixm3, pixadb);
        pix3 = pixm3!;
        pix4 = pixadb.DisplayTiledInColumns(5, 1.0f, 20, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 34 */
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 35 */
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 36 */
        Assert.Equal(36, rp.Index);
        rp.Cleanup();
    }

    [RegFact]
    public void PageSegNoCodecPdf()
    {
        CRand.Seed(1);
        Pix.LeptDebugOK = true;
        TestPaths.EnsureLeptTempDir("pageseg");
        string goldenDir = TestPaths.GoldenDir!;

        static void CheckPngGolden(Pix actual, int index)
        {
            Pix golden;
            lock (TestPaths.PngReadLock)
                golden = Pix.Read(Path.Combine(TestPaths.GoldenDir!, $"pageseg_golden.{index:D2}.png"));
            Assert.True(Pix.Equal(golden, actual), $"pageseg golden {index:D2} mismatch");
        }

            /* Test the generic page segmentation (0 - 19) */
        Pix pixs = TestPaths.ReadInput("pageseg1.tif");
        Pixa pixadb = Pixa.Create(0);
        pixs.GetRegionsBinary(out _, out _, out _, pixadb);
        Assert.Equal(20, pixadb.Count);
        for (int i = 0; i < 20; i++)
            CheckPngGolden(pixadb.GetPix(i, L.L_CLONE), i);

            /* Test foreground finding (20 - 21) */
        string[] fnames = {
            "lion-page.00010.jpg", "lion-page.00011.png", "lion-page.00012.png",
            "lion-page.00013.png", "lion-page.00016.jpg", "lion-page.00017.png"
        };
        int n = fnames.Length;
        Boxa boxa = Boxa.Create(n);
        boxa.InitFull(Box.Create(0, 0, 0, 0));
        Pixacomp pixac = Pixacomp.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = TestPaths.ReadInput(fnames[i]);
            Box? box = pix1.FindPageForeground(170, 70, 30, 0, pixac);
            if (box != null)
                boxa.ReplaceBox(i, box);
        }
        Assert.Equal(File.ReadAllBytes(Path.Combine(goldenDir, "pageseg_golden.20.ba")),
                     boxa.WriteMem());

        byte[] pdfData;
        PdfIo.SetDateAndVersion(0);
        try {
            pdfData = pixac.ConvertToPdfData(0, 1.0f, L.L_DEFAULT_ENCODE, 0, "Page foreground");
        } finally {
            PdfIo.SetDateAndVersion(1);
        }
        string pdfOut = "/tmp/lept/pageseg/fg.pdf";
        File.WriteAllBytes(Utils.GenPathname(pdfOut, null), pdfData);
        PdfRegHelper.AssertPdfStructureMatchesGolden(pdfOut, Path.Combine(goldenDir, "pageseg_golden.21.pdf"));

            /* Test the greedy rectangle finder for white space (22) */
        Pix pixHalf = pixs.Scale(0.5f, 0.5f);
        pixHalf.FindLargeRectangles(0, 20, out _, out Pix pixdb);
        CheckPngGolden(pixdb, 22);

            /* Test the table finder (23 - 30) */
        (string File, int Expected, int PngIdx)[] tables = {
            ("table.15.tif", 1, 24),
            ("table.27.tif", 1, 26),
            ("table.150.png", 1, 28),
            ("toc.99.tif", 0, 30),
        };
        foreach (var (file, expected, pngIdx) in tables) {
            Pix pixTable = TestPaths.ReadInput(file);
            Pixa pixaTable = Pixa.Create(0);
            int score = pixTable.DecideIfTable(null, L.L_PORTRAIT_MODE, pixaTable);
            Assert.Equal(expected, (score >= 2) ? 1 : 0);
            Pix pixTiled = pixaTable.DisplayTiledInRows(32, 2000, 1.0f, 0, 30, 2);
            CheckPngGolden(pixTiled, pngIdx);
        }

            /* Tests of auto-inversion of text (31 - 36) */
        Pix pixZ = TestPaths.ReadInput("zanotti-78.jpg");
        Pix pix2 = pixZ.ConvertRGBToLuminance();
        pixadb = Pixa.Create(0);
        pixadb.AddPix(pix2, L.L_COPY);
        pix2.GetDimensions(out int w, out int h, out _);
        pix2.Rasterop((int)(0.2 * w), (int)(0.08 * h), (int)(0.4 * w), (int)(0.23 * h),
                      L.PIX_NOT(L.PIX_DST), null, 0, 0);
        pix2.Rasterop((int)(0.6 * w), (int)(0.5 * h), (int)(0.2 * w), (int)(0.15 * h),
                      L.PIX_NOT(L.PIX_DST), null, 0, 0);
        _ = pix2.AutoPhotoinvert(128, out Pix? pixm4, pixadb);
        Pix pix5 = pixadb.DisplayTiledInColumns(5, 0.3f, 20, 2);
        CheckPngGolden(pix2, 31);
        CheckPngGolden(pixm4!, 32);
        CheckPngGolden(pix5, 33);

        Pix pixInv = TestPaths.ReadInput("invertedtext.tif");
        pixadb = Pixa.Create(0);
        pixadb.AddPix(pixInv, L.L_COPY);
        _ = pixInv.AutoPhotoinvert(128, out Pix? pixm3, pixadb);
        Pix pix4 = pixadb.DisplayTiledInColumns(5, 1.0f, 20, 2);
        CheckPngGolden(pixInv, 34);
        CheckPngGolden(pixm3!, 35);
        CheckPngGolden(pix4, 36);
    }
}
