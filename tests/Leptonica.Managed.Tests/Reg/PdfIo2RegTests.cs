using System;
using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// pdfio2_reg.c: segmented mixed-raster PDF generation, multi-page PDF concatenation,
/// and corrupted PDF trailer recovery.
/// </summary>
public class PdfIo2RegTests
{
    [RegFact]
    public void PdfIo2()
    {
        var rp = new RegTest("pdfio2");
        string progDir = TestPaths.ProgDir;

        PdfIo.SetDateAndVersion(0);
        Utils.LeptMkdir("lept/pdf2");

        Pix pix1 = TestPaths.ReadInput("rabi.png");
        Pix pix2 = pix1.ScaleToGray2();
        Pix.Write("/tmp/lept/pdf2/rabi8.jpg", pix2, L.IFF_JFIF_JPEG);
        Pix pix3 = pix2.ThresholdTo4bpp(16, true);
        Pix.Write("/tmp/lept/pdf2/rabi4.png", pix3, L.IFF_PNG);
        pix1.SetResolution(300, 300);
        pix2 = pix1.GenerateHalftoneMask(out _, null);
        pix3 = pix2.MorphSequence("c20.1 + c1.20", 0);
        Boxa boxa1 = pix3.ConnComp(8);
        Boxa boxa2 = boxa1.Transform(0, 0, 0.5f, 0.5f);

        string rabiPng = Path.Combine(progDir, "rabi.png");
        PdfIo.ConvertToPdfSegmented(rabiPng, 300, L.L_G4_ENCODE, 128, boxa1, 0, 0.25f, null, "/tmp/lept/pdf2/file00.pdf");
        PdfIo.ConvertToPdfSegmented(rabiPng, 300, L.L_JPEG_ENCODE, 128, boxa1, 0, 0.25f, null, "/tmp/lept/pdf2/file01.pdf");
        PdfIo.ConvertToPdfSegmented(rabiPng, 300, L.L_FLATE_ENCODE, 128, boxa1, 0, 0.25f, null, "/tmp/lept/pdf2/file02.pdf");

        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/rabi8.jpg", 150, L.L_G4_ENCODE, 128, boxa2, 0, 0.5f, null, "/tmp/lept/pdf2/file03.pdf");
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/rabi8.jpg", 150, L.L_JPEG_ENCODE, 128, boxa2, 0, 0.5f, null, "/tmp/lept/pdf2/file04.pdf");
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/rabi8.jpg", 150, L.L_FLATE_ENCODE, 128, boxa2, 0, 0.5f, null, "/tmp/lept/pdf2/file05.pdf");

        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/rabi4.png", 150, L.L_G4_ENCODE, 128, boxa2, 0, 0.5f, null, "/tmp/lept/pdf2/file06.pdf");
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/rabi4.png", 150, L.L_JPEG_ENCODE, 128, boxa2, 0, 0.5f, null, "/tmp/lept/pdf2/file07.pdf");
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/rabi4.png", 150, L.L_FLATE_ENCODE, 128, boxa2, 0, 0.5f, null, "/tmp/lept/pdf2/file08.pdf");

        Utils.BinaryWrite("/tmp/lept/pdf2/file09.pdf", "w",
            PdfIo.ConvertToPdfDataSegmented("/tmp/lept/pdf2/rabi4.png", 150, L.L_G4_ENCODE, 128, boxa2, 0, 0.5f, null));
        Utils.BinaryWrite("/tmp/lept/pdf2/file10.pdf", "w",
            PdfIo.ConvertToPdfDataSegmented("/tmp/lept/pdf2/rabi4.png", 150, L.L_JPEG_ENCODE, 128, boxa2, 0, 0.5f, null));
        Utils.BinaryWrite("/tmp/lept/pdf2/file11.pdf", "w",
            PdfIo.ConvertToPdfDataSegmented("/tmp/lept/pdf2/rabi4.png", 150, L.L_FLATE_ENCODE, 128, boxa2, 0, 0.5f, null));

        pix1 = TestPaths.ReadInput("candelabrum.011.jpg");
        pix2 = pix1.Scale(3.0f, 3.0f);
        Pix.Write("/tmp/lept/pdf2/candelabrum3.jpg", pix2, L.IFF_JFIF_JPEG);
        GetImageMask(pix2, out boxa1, "/tmp/lept/pdf2/seg1.jpg");
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/candelabrum3.jpg", 200, L.L_G4_ENCODE, 100, boxa1, 0, 0.25f, null, "/tmp/lept/pdf2/file12.pdf");
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/candelabrum3.jpg", 200, L.L_JPEG_ENCODE, 100, boxa1, 0, 0.25f, null, "/tmp/lept/pdf2/file13.pdf");
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/candelabrum3.jpg", 200, L.L_FLATE_ENCODE, 100, boxa1, 0, 0.25f, null, "/tmp/lept/pdf2/file14.pdf");

        pix1 = TestPaths.ReadInput("lion-page.00016.jpg");
        pix2 = pix1.Scale(3.0f, 3.0f);
        Pix.Write("/tmp/lept/pdf2/lion16.jpg", pix2, L.IFF_JFIF_JPEG);
        pix3 = TestPaths.ReadInput("lion-mask.00016.tif");
        boxa1 = pix3.ConnComp(8);
        boxa2 = boxa1.Transform(0, 0, 3.0f, 3.0f);
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/lion16.jpg", 200, L.L_G4_ENCODE, 190, boxa2, 0, 0.5f, null, "/tmp/lept/pdf2/file15.pdf");
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/lion16.jpg", 200, L.L_JPEG_ENCODE, 190, boxa2, 0, 0.5f, null, "/tmp/lept/pdf2/file16.pdf");
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/lion16.jpg", 200, L.L_FLATE_ENCODE, 190, boxa2, 0, 0.5f, null, "/tmp/lept/pdf2/file17.pdf");

        Pix pix4 = pix3.Scale(3.0f, 3.0f);
        Pix pix5 = QuantizeNonImageRegion(pix2, pix4, 12);
        Pix.Write("/tmp/lept/pdf2/lion16-quant.png", pix5, L.IFF_PNG);
        PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/lion16-quant.png", 200, L.L_FLATE_ENCODE, 190, boxa2, 0, 0.5f, null, "/tmp/lept/pdf2/file18.pdf");

        Utils.LeptMkdir("lept/image");
        pix1 = TestPaths.ReadInput("feyn.tif");
        pix2 = TestPaths.ReadInput("rabi.png");
        pix3 = pix1.ScaleToGray3();
        pix4 = pix2.ScaleToGray3();
        pix5 = pix1.Scale(0.33f, 0.33f);
        Pix pix6 = TestPaths.ReadInput("test24.jpg");
        Pix.Write("/tmp/lept/image/file1.png", pix3, L.IFF_PNG);
        Pix.Write("/tmp/lept/image/file2.jpg", pix4, L.IFF_JFIF_JPEG);
        Pix.Write("/tmp/lept/image/file3.tif", pix5, L.IFF_TIFF_G4);
        Pix.Write("/tmp/lept/image/file4.jpg", pix6, L.IFF_JFIF_JPEG);
        PdfIo.ConvertFilesToPdf("/tmp/lept/image", "file", 100, 0.8f, 0, 75, "4 file test", "/tmp/lept/pdf2/file19.pdf");

        for (int i = 0; i <= 19; i++)
            rp.CheckFile($"/tmp/lept/pdf2/file{i:D2}.pdf");

        PdfIo.ConcatenatePdf("/tmp/lept/pdf2", "file", "/tmp/lept/pdf2/cat_lept.pdf");
        RunCorruptionRecoveryTest(progDir);
        rp.Cleanup();
    }

    /// <summary>
    /// Note: in pixConvertToPdfDataSegmented (pdfio1.c), image sub-regions inside boxa
    /// are always encoded with L_JPEG_ENCODE, which falls back to L_FLATE_ENCODE when
    /// HAVE_LIBJPEG is absent! So file02, file08, file11, file18, and file19 (with PNG inputs)
    /// all succeed end-to-end and match the golden page counts and MediaBox sizes, AND the
    /// trailer corruption recovery test runs verbatim against testfile1.pdf &amp; testfile2.pdf.
    /// </summary>
    [RegFact]
    public void PdfIo2FlateAndCorruptionRecovery()
    {
        string progDir = TestPaths.ProgDir;
        string goldenDir = TestPaths.GoldenDir!;
        Utils.LeptRmdir("lept/pdf2");
        Utils.LeptMkdir("lept/pdf2");

        PdfIo.SetDateAndVersion(0);
        try {
            Pix pix1 = TestPaths.ReadInput("rabi.png");
            Pix pix2 = pix1.ScaleToGray2();
            Pix pix3 = pix2.ThresholdTo4bpp(16, true);
            Pix.Write("/tmp/lept/pdf2/rabi4.png", pix3, L.IFF_PNG);
            pix1.SetResolution(300, 300);
            pix2 = pix1.GenerateHalftoneMask(out _, null);
            pix3 = pix2.MorphSequence("c20.1 + c1.20", 0);
            Boxa boxa1 = pix3.ConnComp(8);
            Boxa boxa2 = boxa1.Transform(0, 0, 0.5f, 0.5f);

            string rabiPng = Path.Combine(progDir, "rabi.png");
            PdfIo.ConvertToPdfSegmented(rabiPng, 300, L.L_FLATE_ENCODE, 128, boxa1, 0, 0.25f, null, "/tmp/lept/pdf2/file02.pdf");
            PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/rabi4.png", 150, L.L_FLATE_ENCODE, 128, boxa2, 0, 0.5f, null, "/tmp/lept/pdf2/file08.pdf");
            Utils.BinaryWrite("/tmp/lept/pdf2/file11.pdf", "w",
                PdfIo.ConvertToPdfDataSegmented("/tmp/lept/pdf2/rabi4.png", 150, L.L_FLATE_ENCODE, 128, boxa2, 0, 0.5f, null));

            Pix pixLion = TestPaths.ReadInput("lion-page.00016.jpg").Scale(3.0f, 3.0f);
            Pix pixMask = TestPaths.ReadInput("lion-mask.00016.tif");
            Boxa boxaLion = pixMask.ConnComp(8).Transform(0, 0, 3.0f, 3.0f);
            Pix pixQuant = QuantizeNonImageRegion(pixLion, pixMask.Scale(3.0f, 3.0f), 12);
            Pix.Write("/tmp/lept/pdf2/lion16-quant.png", pixQuant, L.IFF_PNG);
            PdfIo.ConvertToPdfSegmented("/tmp/lept/pdf2/lion16-quant.png", 200, L.L_FLATE_ENCODE, 190, boxaLion, 0, 0.5f, null, "/tmp/lept/pdf2/file18.pdf");

            foreach (int idx in new[] { 2, 8, 11, 18 }) {
                PdfRegHelper.AssertPdfStructureMatchesGolden(
                    Utils.GenPathname($"/tmp/lept/pdf2/file{idx:D2}.pdf", null),
                    Path.Combine(goldenDir, $"pdfio2_golden.{idx:D2}.pdf"));
            }

            PdfIo.ConcatenatePdf("/tmp/lept/pdf2", "file", "/tmp/lept/pdf2/cat_lept.pdf");
            Assert.Equal(4, PdfIo.GetPageCount(Utils.GenPathname("/tmp/lept/pdf2/cat_lept.pdf", null)));

            RunCorruptionRecoveryTest(progDir);
        } finally {
            PdfIo.SetDateAndVersion(1);
        }
    }

    private static void RunCorruptionRecoveryTest(string progDir)
    {
        string tf1 = Path.Combine(progDir, "testfile1.pdf");
        string tf2 = Path.Combine(progDir, "testfile2.pdf");

        Utils.LeptRmdir("lept/good");
        Utils.LeptMkdir("lept/good");
        Utils.LeptCp(tf1, "lept/good", null, out _);
        Utils.LeptCp(tf2, "lept/good", null, out _);
        PdfIo.ConcatenatePdf("/tmp/lept/good", "file", "/tmp/lept/pdf2/good.pdf");

        Utils.LeptRmdir("lept/bad");
        Utils.LeptMkdir("lept/bad");
        L_Bytea ba = L_Bytea.InitFromFile(tf2);
        byte[] data = ba.GetData(out int nbytes);
        Utils.BinaryWrite("/tmp/lept/bad/testfile0.notpdf.pdf", "w", data.AsSpan(10, nbytes - 10));

        data[2297] = (byte)'2';  /* munge trailer object 6: change 458 --> 428 */
        Utils.BinaryWrite("/tmp/lept/bad/testfile2.bad.pdf", "w", data.AsSpan(0, nbytes));

        Utils.LeptCp(tf1, "lept/bad", null, out _);
        PdfIo.ConcatenatePdf("/tmp/lept/bad", "file", "/tmp/lept/pdf2/bad.pdf");

        bool same = Utils.FilesAreIdentical(
            Utils.GenPathname("/tmp/lept/pdf2/good.pdf", null),
            Utils.GenPathname("/tmp/lept/pdf2/bad.pdf", null));
        Assert.True(same, "Concatenated PDF after trailer corruption recovery should match good.pdf byte-for-byte");
    }

    private static void GetImageMask(Pix pixs, out Boxa pboxa, string? debugfile)
    {
        pixs.SetResolution(200, 200);
        Pix pix1 = pixs.ConvertTo1(100);
        Pix pix2 = pix1.GenerateHalftoneMask(out _, null);
        Pix pix3 = pix2.MorphSequence("c20.1 + c1.20", 0);
        pboxa = pix3.ConnComp(8);
        if (debugfile != null) {
            Pixa pixa = Pixa.Create(0);
            pixa.AddPix(pixs, L.L_COPY);
            pixa.AddPix(pix1, L.L_INSERT);
            pixa.AddPix(pix2, L.L_INSERT);
            pixa.AddPix(pix3, L.L_INSERT);
            Pix pix4 = pixa.DisplayTiledInRows(32, 1800, 0.25f, 0, 25, 2);
            Pix.Write(debugfile, pix4, L.IFF_JFIF_JPEG);
        }
    }

    private static Pix QuantizeNonImageRegion(Pix pixs, Pix pixm, int levels)
    {
        Pix pix1 = pixs.ConvertTo8(false);
        Pix pix2 = pix1.ThresholdOn8bpp(levels, true);
        Pix pixd = pix2.ConvertTo32();
        pixd.CombineMasked(pixs, pixm);
        return pixd;
    }
}
