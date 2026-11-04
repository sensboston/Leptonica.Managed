using System;
using System.IO;
using System.Text;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.IO;

public class PdfIoTests
{
    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lept_pdfiotests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void SingleImageAndStreamPdf_MetadataAndDimensions()
    {
        string dir = NewTempDir();
        try {
            Pix pix = Pix.Create(120, 80, 32);
            pix.SetRGBPixel(10, 10, 200, 100, 50);

            PdfIo.SetDateAndVersion(0);
            byte[] memPdf;
            try {
                memPdf = Pix.WriteMemPdf(pix, 150, "Unit Test PDF");
            } finally {
                PdfIo.SetDateAndVersion(1);
            }

            string pdfPath = Path.Combine(dir, "single.pdf");
            File.WriteAllBytes(pdfPath, memPdf);

            Assert.Equal(1, PdfIo.GetPageCount(pdfPath));
            PdfIo.GetPageSizes(pdfPath, out Numa nw, out Numa nh, out _, out _);
            Assert.Equal(new[] { 120 }, nw.GetIArray());
            Assert.Equal(new[] { 80 }, nh.GetIArray());

            PdfIo.GetMediaBoxSizes(pdfPath, out Numa mw, out Numa mh, out _, out _);
            Assert.Equal(new[] { 58 }, mw.GetIArray());  /* round(120 * 72 / 150) = 58 */
            Assert.Equal(new[] { 38 }, mh.GetIArray());  /* round(80 * 72 / 150) = 38 */

            string pdfText = Encoding.Latin1.GetString(memPdf);
            Assert.StartsWith("%PDF-1.5\n", pdfText);
            Assert.EndsWith("%%EOF\n", pdfText);
            Assert.DoesNotContain("/CreationDate", pdfText);
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MultiImageSinglePage_And_Concatenation_WithColormap()
    {
        string dir = NewTempDir();
        try {
            Pix pix1 = Pix.Create(60, 40, 8);
            PixColormap cmap = PixColormap.Create(8);
            cmap.AddColor(255, 255, 255);
            cmap.AddColor(240, 20, 20);
            pix1.SetColormap(cmap);
            pix1.SetPixel(5, 5, 1);

            Pix pix2 = Pix.Create(50, 30, 1);
            pix2.SetPixel(2, 2, 1);

            string multiImgPdf = Path.Combine(dir, "multi_img_page.pdf");
            L_Pdf_Data? lpd = null;
            pix1.ConvertToPdf(L.L_FLATE_ENCODE, 0, null, 10, 10, 100, "MultiImg", ref lpd, L.L_FIRST_IMAGE);
            Assert.NotNull(lpd);
            pix2.ConvertToPdf(L.L_FLATE_ENCODE, 0, multiImgPdf, 80, 60, 100, null, ref lpd, L.L_LAST_IMAGE);
            Assert.Null(lpd);
            Assert.Equal(1, PdfIo.GetPageCount(multiImgPdf));

            string secondPagePdf = Path.Combine(dir, "second_page.pdf");
            using (FileStream fs = File.Create(secondPagePdf))
                Pix.WriteStreamPdf(fs, pix2, 100, "Second");

            var sa = Sarray.Create(2);
            sa.AddString(multiImgPdf, L.L_COPY);
            sa.AddString(secondPagePdf, L.L_COPY);

            string concatPdf = Path.Combine(dir, "concat.pdf");
            PdfIo.SaConcatenatePdf(sa, concatPdf);
            Assert.Equal(2, PdfIo.GetPageCount(concatPdf));
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConvertToPdfSegmented_And_PixacompConvertToPdfData_And_PdfApp()
    {
        string dir = NewTempDir();
        try {
            Pix pix = Pix.Create(200, 160, 8);
            pix.SetAllArbitrary(240);
            pix.Rasterop(20, 20, 60, 40, L.PIX_CLR, null, 0, 0);
            pix.Rasterop(100, 80, 60, 50, L.PIX_SET, null, 0, 0);

            Boxa boxa = Boxa.Create(1);
            boxa.AddBox(Box.Create(95, 75, 70, 60), L.L_INSERT);

            byte[] segPdf = pix.ConvertToPdfDataSegmented(150, L.L_FLATE_ENCODE, 128, boxa, 75, 0.5f, "Segmented");
            string segPath = Path.Combine(dir, "seg.pdf");
            File.WriteAllBytes(segPath, segPdf);
            Assert.Equal(1, PdfIo.GetPageCount(segPath));

            Pixacomp pixac = Pixacomp.Create(2);
            pixac.AddPix(pix, L.IFF_PNG);
            pixac.AddPix(pix.ConvertTo1(128), L.IFF_PNG);
            byte[] pixacPdf = pixac.ConvertToPdfData(150, 1.0f, L.L_FLATE_ENCODE, 0, "PixacompPDF");
            string pixacPath = Path.Combine(dir, "pixac.pdf");
            File.WriteAllBytes(pixacPath, pixacPdf);
            Assert.Equal(2, PdfIo.GetPageCount(pixacPath));

            string imgPath = Path.Combine(dir, "page0.png");
            Pix.Write(imgPath, pix, L.IFF_PNG);
            Sarray sa = Sarray.Create(1);
            sa.AddString(imgPath, L.L_COPY);

            string compOut = Path.Combine(dir, "compressed.pdf");
            PdfIo.CompressFilesToPdf(sa, 1, 0, 1.0f, 50, "Compressed", compOut);
            Assert.Equal(1, PdfIo.GetPageCount(compOut));

            string cleanOut = Path.Combine(dir, "cleaned.pdf");
            PdfIo.CleanTo1bppFilesToPdf(sa, 300, 3, 0, 1, "Cleaned", cleanOut);
            Assert.Equal(1, PdfIo.GetPageCount(cleanOut));
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }
}
