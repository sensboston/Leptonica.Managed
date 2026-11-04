using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// partition_reg.c: whitespace rectangle partitioning via boxaGetWhiteblocks().
/// </summary>
public class PartitionRegTests
{
    [RegFact(Skip = "raw Flate stream bytes in PDF goldens 00..03 come from C libz (.NET ZLibStream differs in compressed bytes); decompressed PDF raster check runs in PartitionSemanticPdf")]
    public void Partition_Reg()
    {
        var rp = new RegTest("partition");

        PdfIo.SetDateAndVersion(0);
        TestPaths.EnsureLeptTempDir("part");

        Pixa pixad = Pixa.Create(4);
        TestPartition(rp, "test8.jpg", L.L_SORT_BY_HEIGHT, 20, 0.0f, "test0.pdf", pixad);
        TestPartition(rp, "test8.jpg", L.L_SORT_BY_AREA, 20, 0.0f, "test1.pdf", pixad);
        TestPartition(rp, "test8.jpg", L.L_SORT_BY_AREA, 20, 0.4f, "test2.pdf", pixad);
        TestPartition(rp, "feyn-fract.tif", L.L_SORT_BY_AREA, 20, 0.0f, "test3.pdf", pixad);

        rp.Cleanup();
    }

    [RegFact]
    public void PartitionSemanticPdf()
    {
        CRand.Seed(1);
        TestPaths.EnsureLeptTempDir("part");
        string goldenDir = TestPaths.GoldenDir!;

        PdfIo.SetDateAndVersion(0);
        try {
            Pixa pixad = Pixa.Create(4);
            RunPartitionCase("test8.jpg", L.L_SORT_BY_HEIGHT, 20, 0.0f, "test0.pdf", pixad,
                             Path.Combine(goldenDir, "partition_golden.00.pdf"));
            RunPartitionCase("test8.jpg", L.L_SORT_BY_AREA, 20, 0.0f, "test1.pdf", pixad,
                             Path.Combine(goldenDir, "partition_golden.01.pdf"));
            RunPartitionCase("test8.jpg", L.L_SORT_BY_AREA, 20, 0.4f, "test2.pdf", pixad,
                             Path.Combine(goldenDir, "partition_golden.02.pdf"));
            RunPartitionCase("feyn-fract.tif", L.L_SORT_BY_AREA, 20, 0.0f, "test3.pdf", pixad,
                             Path.Combine(goldenDir, "partition_golden.03.pdf"));
        } finally {
            PdfIo.SetDateAndVersion(1);
        }
    }

    private static void RunPartitionCase(string fname, int sorttype,
                                         int maxboxes, float ovlap, string fileout,
                                         Pixa pixad, string goldenPdf)
    {
        _ = pixad;
        Pixa pixa = BuildPartitionPixa(fname, sorttype, maxboxes, ovlap);
        string pathout = $"/tmp/lept/part/{fileout}";
        pixa.ConvertToPdf(300, 1.0f, L.L_FLATE_ENCODE, 0, fileout, pathout);
        PdfRegHelper.AssertFlatePdfEqualsGolden(pathout, goldenPdf);
    }

    private static void TestPartition(RegTest rp, string fname, int sorttype,
                                      int maxboxes, float ovlap, string fileout,
                                      Pixa pixad)
    {
        _ = pixad;
        Pixa pixa = BuildPartitionPixa(fname, sorttype, maxboxes, ovlap);
        string pathout = $"/tmp/lept/part/{fileout}";
        pixa.ConvertToPdf(300, 1.0f, L.L_FLATE_ENCODE, 0, fileout, pathout);
        rp.CheckFile(pathout);
    }

    private static Pixa BuildPartitionPixa(string fname, int sorttype, int maxboxes, float ovlap)
    {
        Pixa pixa = Pixa.Create(0);
        Pix pix = TestPaths.ReadInput(fname);
        pixa.AddPix(pix, L.L_COPY);
        Pix pix1 = pix.ConvertTo1(128);
        Pix.DilateBrick(pix1, pix1, 5, 5);
        pixa.AddPix(pix1, L.L_COPY);
        Boxa boxa1 = pix1.ConnComp(4);
        pix1.GetDimensions(out int w, out int h, out _);
        Box box = Box.Create(0, 0, w, h);
        Boxa.PermuteRandom(boxa1, boxa1);
        Boxa boxa2 = boxa1.SelectBySize(500, 500, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_LT, out _);
        Pix pix2 = pix1.CopyWithBoxa(boxa2, L.L_SET_WHITE);
        pixa.AddPix(pix2, L.L_COPY);
        Boxa boxa3 = boxa2.GetWhiteblocks(box, sorttype, maxboxes, ovlap, 200, 0.15f, 20000);

        Pix pix3 = pix2.DrawBoxaRandom(boxa3, 7);
        pixa.AddPix(pix3, L.L_INSERT);

        pix3 = pix2.PaintBoxaRandom(boxa3);
        pixa.AddPix(pix3, L.L_INSERT);
        return pixa;
    }
}
