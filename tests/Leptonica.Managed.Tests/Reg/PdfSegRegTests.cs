using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// pdfseg_reg.c: generates segmented images across 6 diverse pages and encodes them
/// as a mixed-raster multi-page PDF.
/// </summary>
public class PdfSegRegTests
{
    private const int Width = 800;

    [RegFact]
    public void PdfSeg()
    {
        var rp = new RegTest("pdfseg");

        Utils.LeptRmdir("lept/pdfseg");
        Utils.LeptMkdir("lept/pdfseg");
        Boxaa baa = BuildPagesAndBoxaa(writeJpegAndCheck: true, rp: rp);

        baa.Write("/tmp/lept/pdfseg/images.baa");
        rp.CheckFile("/tmp/lept/pdfseg/images.baa");   /* 6 */

        PdfIo.SetDateAndVersion(0);
        PdfIo.ConvertSegmentedFilesToPdf("/tmp/lept/pdfseg", "jpg", 100, L.L_G4_ENCODE,
                                         140, baa, 75, 0.6f, "Segmentation Test",
                                         "/tmp/lept/regout/pdfseg.7.pdf");
        rp.CheckFile("/tmp/lept/regout/pdfseg.7.pdf");   /* 7 */

        Assert.Equal(7, rp.Index);
        rp.Cleanup();
    }

    [RegFact]
    public void PdfSegBoxaaAndFlatePdf()
    {
        Pix.LeptDebugOK = true;
        CRand.Seed(1);
        string goldenDir = TestPaths.GoldenDir!;

        Utils.LeptRmdir("lept/pdfseg");
        Utils.LeptMkdir("lept/pdfseg");
        Utils.LeptMkdir("lept/regout");

        Boxaa baa = BuildPagesAndBoxaa(writeJpegAndCheck: false, rp: null);
        string baaPath = Utils.GenPathname("/tmp/lept/pdfseg/images.baa", null);
        baa.Write(baaPath);

        string actualBaa = File.ReadAllText(baaPath).Replace("\r\n", "\n");
        string goldenBaa = File.ReadAllText(Path.Combine(goldenDir, "pdfseg_golden.06.baa")).Replace("\r\n", "\n");
        Assert.Equal(goldenBaa, actualBaa);

        string pdfOut = Utils.GenPathname("/tmp/lept/regout/pdfseg.7.pdf", null);
        PdfIo.SetDateAndVersion(0);
        try {
            PdfIo.ConvertSegmentedFilesToPdf("/tmp/lept/pdfseg", "png", 100, L.L_FLATE_ENCODE,
                                             140, baa, 75, 0.6f, "Segmentation Test", pdfOut);
        } finally {
            PdfIo.SetDateAndVersion(1);
        }

        Assert.Equal(6, PdfIo.GetPageCount(pdfOut));
        PdfIo.GetMediaBoxSizes(pdfOut, out Numa amw, out Numa amh, out _, out _);
        PdfIo.GetMediaBoxSizes(Path.Combine(goldenDir, "pdfseg_golden.07.pdf"), out Numa gmw, out Numa gmh, out _, out _);
        Assert.Equal(gmw.GetIArray(), amw.GetIArray());
        Assert.Equal(gmh.GetIArray(), amh.GetIArray());
    }

    private static Boxaa BuildPagesAndBoxaa(bool writeJpegAndCheck, RegTest? rp)
    {
        Boxaa baa = Boxaa.Create(5);

        static void SavePage(Pix pix, int idx, bool asJpeg, RegTest? r)
        {
            if (asJpeg) {
                string p = $"/tmp/lept/pdfseg/{idx}.jpg";
                Pix.Write(p, pix, L.IFF_JFIF_JPEG);
                r!.CheckFile(p);
            } else {
                Pix.Write($"/tmp/lept/pdfseg/{idx}.png", pix, L.IFF_PNG);
            }
        }

            /* 0: Image region input */
        Pix pix1 = TestPaths.ReadInput("wet-day.jpg");
        Pix pix2 = pix1.ScaleToSize(Width, 0);
        SavePage(pix2, 0, writeJpegAndCheck, rp);
        Boxa boxa1 = Boxa.Create(1);
        boxa1.AddBox(Box.Create(105, 161, 620, 872), L.L_INSERT);
        baa.AddBoxa(boxa1, L.L_INSERT);

            /* 1: Compute image region at w = 2 * Width */
        pix1 = TestPaths.ReadInput("candelabrum.011.jpg");
        pix2 = pix1.ScaleToSize(Width, 0);
        Pix pix3 = pix2.ConvertTo1(100);
        Pix pix4 = pix3.ExpandBinaryPower2(2);
        Pix pix5 = pix4.GenerateHalftoneMask(out _, null);
        Pix pix6 = pix5.MorphSequence("c20.1 + c1.20", 0);
        Pix pix7 = pix6.MaskConnComp(8, out Boxa? boxaMask);
        Pix pix8 = pix7.ReduceBinary2(null);
        Pix pix9 = pix2.BackgroundNormSimple(pix8, null);
        SavePage(pix9, 1, writeJpegAndCheck, rp);
        Boxa boxa2 = boxaMask!.Transform(0, 0, 0.5f, 0.5f);
        baa.AddBoxa(boxa2, L.L_INSERT);

            /* 2: Use mask to find image region */
        pix1 = TestPaths.ReadInput("lion-page.00016.jpg");
        pix2 = pix1.ScaleToSize(Width, 0);
        SavePage(pix2, 2, writeJpegAndCheck, rp);
        pix3 = TestPaths.ReadInput("lion-mask.00016.tif");
        pix4 = pix3.ScaleToSize(Width, 0);
        boxa1 = pix4.ConnComp(8);
        baa.AddBoxa(boxa1, L.L_INSERT);

            /* 3: Compute image region at full res */
        pix1 = TestPaths.ReadInput("rabi.png");
        float scalefactor = (float)Width / (float)pix1.Width;
        pix2 = pix1.ScaleToGray(scalefactor);
        SavePage(pix2, 3, writeJpegAndCheck, rp);
        pix3 = pix1.GenerateHalftoneMask(out _, null);
        pix4 = pix3.MorphSequence("c20.1 + c1.20", 0);
        boxa1 = pix4.ConnComp(8);
        boxa2 = boxa1.Transform(0, 0, scalefactor, scalefactor);
        baa.AddBoxa(boxa2, L.L_INSERT);

            /* 4: Page with no image regions */
        pix1 = TestPaths.ReadInput("lucasta.047.jpg");
        pix2 = pix1.ScaleToSize(Width, 0);
        boxa1 = Boxa.Create(1);
        SavePage(pix2, 4, writeJpegAndCheck, rp);
        baa.AddBoxa(boxa1, L.L_INSERT);

            /* 5: Page that is all image */
        pix1 = TestPaths.ReadInput("map1.jpg");
        pix2 = pix1.ScaleToSize(Width, 0);
        SavePage(pix2, 5, writeJpegAndCheck, rp);
        int h = pix2.Height;
        boxa1 = Boxa.Create(1);
        boxa1.AddBox(Box.Create(0, 0, Width, h), L.L_INSERT);
        baa.AddBoxa(boxa1, L.L_INSERT);

        return baa;
    }
}
