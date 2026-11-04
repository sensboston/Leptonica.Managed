using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// pdfio1_reg.c: high-level and low-level PDF generation tests (single images,
/// multi-image single pages, segmented pages without image regions, and 1 bpp images
/// with/without colormaps).
/// </summary>
public class PdfIo1RegTests
{
    [RegFact]
    public void PdfIo1()
    {
        var rp = new RegTest("pdfio1");
        string progDir = TestPaths.ProgDir;

        PdfIo.SetDateAndVersion(0);
        Utils.LeptMkdir("lept/pdf1");

        /* ---------------  Single image tests  ------------------- */
        PdfIo.ConvertToPdf(Path.Combine(progDir, "weasel2.4c.png"), L.L_FLATE_ENCODE, 0,
                           "/tmp/lept/pdf1/file00.pdf", 0, 0, 72, "weasel2.4c.png");
        PdfIo.ConvertToPdf(Path.Combine(progDir, "test24.jpg"), L.L_JPEG_ENCODE, 0,
                           "/tmp/lept/pdf1/file01.pdf", 0, 0, 72, "test24.jpg");
        PdfIo.ConvertToPdf(Path.Combine(progDir, "feyn.tif"), L.L_G4_ENCODE, 0,
                           "/tmp/lept/pdf1/file02.pdf", 0, 0, 300, "feyn.tif");

        Pix pixs = TestPaths.ReadInput("feyn.tif");
        pixs.ConvertToPdf(L.L_G4_ENCODE, 0, "/tmp/lept/pdf1/file03.pdf", 0, 0, 300, "feyn.tif");

        pixs = TestPaths.ReadInput("test24.jpg");
        pixs.ConvertToPdf(L.L_JPEG_ENCODE, 5, "/tmp/lept/pdf1/file04.pdf", 0, 0, 72, "test24.jpg");

        pixs = TestPaths.ReadInput("feyn.tif");
        Pix pixt = pixs.ScaleToGray2();
        Pix.Write("/tmp/lept/pdf1/feyn8.png", pixt, L.IFF_PNG);
        PdfIo.ConvertToPdf("/tmp/lept/pdf1/feyn8.png", L.L_JPEG_ENCODE, 0,
                           "/tmp/lept/pdf1/file05.pdf", 0, 0, 150, "feyn8.png");

        PdfIo.ConvertToPdf(Path.Combine(progDir, "weasel4.16g.png"), L.L_FLATE_ENCODE, 0,
                           "/tmp/lept/pdf1/file06.pdf", 0, 0, 30, "weasel4.16g.png");

        pixs = TestPaths.ReadInput("test24.jpg");
        Pix pixg = pixs.ConvertTo8(false);
        Box box = Box.Create(100, 100, 100, 100);
        Pix pixc = pixs.ClipRectangle(box, out _)!;
        Pix pixgc = pixg.ClipRectangle(box, out _)!;
        Pix.Write("/tmp/lept/pdf1/pix32.jpg", pixc, L.IFF_JFIF_JPEG);
        Pix.Write("/tmp/lept/pdf1/pix8.jpg", pixgc, L.IFF_JFIF_JPEG);
        PdfIo.ConvertToPdf("/tmp/lept/pdf1/pix32.jpg", L.L_FLATE_ENCODE, 0,
                           "/tmp/lept/pdf1/file07.pdf", 0, 0, 72, "pix32.jpg");
        PdfIo.ConvertToPdf("/tmp/lept/pdf1/pix8.jpg", L.L_FLATE_ENCODE, 0,
                           "/tmp/lept/pdf1/file08.pdf", 0, 0, 72, "pix8.jpg");

        /* ---------------  Multiple image tests  ------------------- */
        Pix pix1 = TestPaths.ReadInput("feyn-fract.tif");
        Pix pix2 = TestPaths.ReadInput("weasel8.240c.png");
        L_Pdf_Data? lpd = null;
        for (int i = 0; i < 5; i++) {
            for (int j = 0; j < 10; j++) {
                int seq = (i == 0 && j == 0) ? L.L_FIRST_IMAGE : L.L_NEXT_IMAGE;
                string? title = (i == 0 && j == 0) ? "feyn-fract.tif" : null;
                pix2.ConvertToPdf(L.L_FLATE_ENCODE, 0, null, 100 * j, 100 * i, 70, title, ref lpd, seq);
            }
        }
        pix1.ConvertToPdf(L.L_G4_ENCODE, 0, "/tmp/lept/pdf1/file09.pdf", 0, 0, 80, null, ref lpd, L.L_LAST_IMAGE);

        PdfIo.SetG4ImageMask(0);
        for (int i = 0; i < 5; i++) {
            for (int j = 0; j < 10; j++) {
                int seq = (i == 0 && j == 0) ? L.L_FIRST_IMAGE : L.L_NEXT_IMAGE;
                string? title = (i == 0 && j == 0) ? "feyn-fract.tif" : null;
                pix2.ConvertToPdf(L.L_FLATE_ENCODE, 0, null, 100 * j, 100 * i, 70, title, ref lpd, seq);
            }
        }
        pix1.ConvertToPdf(L.L_G4_ENCODE, 0, "/tmp/lept/pdf1/file10.pdf", 0, 0, 80, null, ref lpd, L.L_LAST_IMAGE);
        PdfIo.SetG4ImageMask(1);

        /* -------- pdf convert segmented with no image regions -------- */
        pix1 = TestPaths.ReadInput("rabi.png");
        pix2 = pix1.ScaleToGray2();
        Pix.Write("/tmp/lept/pdf1/rabi8.jpg", pix2, L.IFF_JFIF_JPEG);
        Pix pix3 = pix2.ThresholdTo4bpp(16, true);
        Pix.Write("/tmp/lept/pdf1/rabi4.png", pix3, L.IFF_PNG);

        string rabiPng = Path.Combine(progDir, "rabi.png");
        PdfIo.ConvertToPdfSegmented(rabiPng, 300, L.L_G4_ENCODE, 128, null, 0, 0.0f, null, "/tmp/lept/pdf1/file11.pdf");
        PdfIo.ConvertToPdfSegmented(rabiPng, 300, L.L_JPEG_ENCODE, 128, null, 0, 0.0f, null, "/tmp/lept/pdf1/file12.pdf");
        PdfIo.ConvertToPdfSegmented(rabiPng, 300, L.L_FLATE_ENCODE, 128, null, 0, 0.0f, null, "/tmp/lept/pdf1/file13.pdf");

        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf1/rabi8.jpg", 150, L.L_G4_ENCODE, 128, null, 0, 0.0f, null, "/tmp/lept/pdf1/file14.pdf");
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf1/rabi8.jpg", 150, L.L_JPEG_ENCODE, 128, null, 0, 0.0f, null, "/tmp/lept/pdf1/file15.pdf");
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf1/rabi8.jpg", 150, L.L_FLATE_ENCODE, 128, null, 0, 0.0f, null, "/tmp/lept/pdf1/file16.pdf");

        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf1/rabi4.png", 150, L.L_G4_ENCODE, 128, null, 0, 0.0f, null, "/tmp/lept/pdf1/file17.pdf");
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf1/rabi4.png", 150, L.L_JPEG_ENCODE, 128, null, 0, 0.0f, null, "/tmp/lept/pdf1/file18.pdf");
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf1/rabi4.png", 150, L.L_FLATE_ENCODE, 128, null, 0, 0.0f, null, "/tmp/lept/pdf1/file19.pdf");

        Generate1BppHighAndLowLevel();

        for (int idx = 0; idx <= 26; idx++)
            rp.CheckFile($"/tmp/lept/pdf1/file{idx:D2}.pdf");
        rp.Cleanup();
    }

    [RegFact]
    public void PdfIo1FlateAndLowLevel()
    {
        string progDir = TestPaths.ProgDir;
        string goldenDir = TestPaths.GoldenDir!;
        Utils.LeptMkdir("lept/pdf1");

        PdfIo.SetDateAndVersion(0);
        try {
                /* Single PNG Flate tests (00 and 06) */
            PdfIo.ConvertToPdf(Path.Combine(progDir, "weasel2.4c.png"), L.L_FLATE_ENCODE, 0,
                               "/tmp/lept/pdf1/file00.pdf", 0, 0, 72, "weasel2.4c.png");
            PdfIo.ConvertToPdf(Path.Combine(progDir, "weasel4.16g.png"), L.L_FLATE_ENCODE, 0,
                               "/tmp/lept/pdf1/file06.pdf", 0, 0, 30, "weasel4.16g.png");

                /* Segmented with no image regions, Flate (13 and 19) */
            Pix pix1 = TestPaths.ReadInput("rabi.png");
            Pix pix2 = pix1.ScaleToGray2();
            Pix pix3 = pix2.ThresholdTo4bpp(16, true);
            Pix.Write("/tmp/lept/pdf1/rabi4.png", pix3, L.IFF_PNG);

            PdfIo.ConvertToPdfSegmented(Path.Combine(progDir, "rabi.png"), 300, L.L_FLATE_ENCODE,
                                        128, null, 0, 0.0f, null, "/tmp/lept/pdf1/file13.pdf");
            PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf1/rabi4.png", 150, L.L_FLATE_ENCODE,
                                        128, null, 0, 0.0f, null, "/tmp/lept/pdf1/file19.pdf");

                /* 1 bpp high-level (20..22) and low-level (23..26) */
            Generate1BppHighAndLowLevel();

                /* Multi-image single page (09 and 10; G4 falls back to Flate, structure matches golden) */
            Pix pixFract = TestPaths.ReadInput("feyn-fract.tif");
            Pix pixWeasel = TestPaths.ReadInput("weasel8.240c.png");
            L_Pdf_Data? lpd = null;
            for (int i = 0; i < 5; i++) {
                for (int j = 0; j < 10; j++) {
                    int seq = (i == 0 && j == 0) ? L.L_FIRST_IMAGE : L.L_NEXT_IMAGE;
                    string? title = (i == 0 && j == 0) ? "feyn-fract.tif" : null;
                    pixWeasel.ConvertToPdf(L.L_FLATE_ENCODE, 0, null, 100 * j, 100 * i, 70, title, ref lpd, seq);
                }
            }
            pixFract.ConvertToPdf(L.L_FLATE_ENCODE, 0, "/tmp/lept/pdf1/file09.pdf", 0, 0, 80, null, ref lpd, L.L_LAST_IMAGE);
        } finally {
            PdfIo.SetDateAndVersion(1);
        }

        int[] flateIndices = { 0, 6, 13, 19, 20, 21, 22, 23, 24, 25, 26 };
        foreach (int idx in flateIndices) {
            PdfRegHelper.AssertFlatePdfEqualsGolden(
                Utils.GenPathname($"/tmp/lept/pdf1/file{idx:D2}.pdf", null),
                Path.Combine(goldenDir, $"pdfio1_golden.{idx:D2}.pdf"));
        }

        PdfRegHelper.AssertPdfStructureMatchesGolden(
            Utils.GenPathname("/tmp/lept/pdf1/file09.pdf", null),
            Path.Combine(goldenDir, "pdfio1_golden.09.pdf"));
    }

    private static void Generate1BppHighAndLowLevel()
    {
        Pix pix1 = TestPaths.ReadInput("feyn-fract.tif");
        Pix.Write("/tmp/lept/pdf1/feyn-nocmap.png", pix1, L.IFF_PNG);
        Pix pix2 = Pix.Copy(null, pix1);
        PixColormap cmap = PixColormap.Create(1);
        cmap.AddColor(0, 0, 0);
        cmap.AddColor(255, 255, 255);
        pix2.SetColormap(cmap);
        Pix.Write("/tmp/lept/pdf1/feyn-cmap1.png", pix2, L.IFF_PNG);
        cmap = PixColormap.Create(1);
        cmap.AddColor(200, 0, 0);
        cmap.AddColor(255, 255, 255);
        pix1.SetColormap(cmap);
        Pix.Write("/tmp/lept/pdf1/feyn-cmap2.png", pix1, L.IFF_PNG);

        PdfIo.ConvertToPdf("/tmp/lept/pdf1/feyn-nocmap.png", L.L_FLATE_ENCODE, 0, "/tmp/lept/pdf1/file20.pdf", 0, 0, 0, null);
        PdfIo.ConvertToPdf("/tmp/lept/pdf1/feyn-cmap1.png", L.L_FLATE_ENCODE, 0, "/tmp/lept/pdf1/file21.pdf", 0, 0, 0, null);
        PdfIo.ConvertToPdf("/tmp/lept/pdf1/feyn-cmap2.png", L.L_FLATE_ENCODE, 0, "/tmp/lept/pdf1/file22.pdf", 0, 0, 0, null);

        pix1 = TestPaths.ReadInput("cat-and-mouse.png");
        pix2 = pix1.ConvertRGBToCmapLossless()!;

        cmap = PixColormap.Create(1);
        cmap.AddColor(255, 255, 255);
        cmap.AddColor(0, 0, 0);
        pix2.SetColormap(cmap);
        Pix.Write("/tmp/lept/pdf1/cat-and-mouse-cmap1.png", pix2, L.IFF_PNG);

        L_Compressed_Data cid = pix2.GenerateCIData(L.L_FLATE_ENCODE, 0, 0);
        Assert.Equal(2, cid.Ncolors);
        Utils.BinaryWrite("/tmp/lept/pdf1/file23.pdf", "w", PdfIo.CidConvertToPdfData(cid, "with colormap"));

        cid = PdfIo.GenerateCIDataForPdf("/tmp/lept/pdf1/cat-and-mouse-cmap1.png", null, 75);
        Assert.Equal(0, cid.Ncolors);
        Utils.BinaryWrite("/tmp/lept/pdf1/file24.pdf", "w", PdfIo.CidConvertToPdfData(cid, "no colormap"));

        cmap = PixColormap.Create(1);
        cmap.AddColor(254, 240, 185);
        cmap.AddColor(50, 50, 130);
        pix2.SetColormap(cmap);
        Pix.Write("/tmp/lept/pdf1/cat-and-mouse-cmap2.png", pix2, L.IFF_PNG);

        cid = pix2.GenerateCIData(L.L_FLATE_ENCODE, 0, 0);
        Assert.Equal(2, cid.Ncolors);
        Utils.BinaryWrite("/tmp/lept/pdf1/file25.pdf", "w", PdfIo.CidConvertToPdfData(cid, "with colormap"));

        cid = PdfIo.GenerateCIDataForPdf("/tmp/lept/pdf1/cat-and-mouse-cmap2.png", null, 75);
        Assert.Equal(0, cid.Ncolors);
        Utils.BinaryWrite("/tmp/lept/pdf1/file26.pdf", "w", PdfIo.CidConvertToPdfData(cid, "no colormap"));
    }
}
