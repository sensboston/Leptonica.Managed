using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// mtiff_reg.c: tests multipage tiff read/write, offset reading, custom tags,
/// and multipage tiff to PS/PDF conversion.
/// </summary>
public class MtiffRegTests
{
    private const string WeaselRev = "/tmp/lept/tiff/weasel_rev.tif";
    private const string WeaselRevRev = "/tmp/lept/tiff/weasel_rev_rev.tif";
    private const string WeaselOrig = "/tmp/lept/tiff/weasel_orig.tif";

    private static string ProgFile(string name) => TestPaths.ProgFile(name);

    [RegFact]
    public void Mtiff()
    {
        var rp = new RegTest("mtiff");

        Utils.LeptRmdir("lept/tiff");
        TestPaths.EnsureLeptTempDir("tiff");

            /* ---------------------- Test multipage I/O ----------------------- */
        Pix.WriteMultipageTiff(TestPaths.ProgDir, "weasel8.", "/tmp/lept/tiff/weasel8.tif");
        rp.CheckFile("/tmp/lept/tiff/weasel8.tif");  /* 0 */
        Pixa pixa = Pixa.ReadMultipageTiff("/tmp/lept/tiff/weasel8.tif");
        Pix pix1 = pixa.DisplayTiledInRows(1, 1200, 0.5f, 0, 15, 4);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 1 */
        pix1 = pixa.DisplayTiledInRows(8, 1200, 0.8f, 0, 15, 4);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 2 */
        pix1 = pixa.DisplayTiledInRows(32, 1200, 1.2f, 0, 15, 4);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 3 */

        long offset = 0;
        pixa = Pixa.Create(8);
        do {
            Pix? p = Pix.ReadFromMultipageTiff("/tmp/lept/tiff/weasel8.tif", ref offset);
            if (p != null)
                pixa.AddPix(p, L.L_INSERT);
        } while (offset != 0);
        pix1 = pixa.DisplayTiledInRows(32, 1200, 1.2f, 0, 15, 4);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 4 */

        offset = 0;
        pixa = Pixa.Create(8);
        byte[] data = Utils.BinaryRead("/tmp/lept/tiff/weasel8.tif");
        do {
            Pix? p = Pix.ReadMemFromMultipageTiff(data, ref offset);
            if (p != null)
                pixa.AddPix(p, L.L_INSERT);
        } while (offset != 0);
        pix1 = pixa.DisplayTiledInRows(32, 1200, 1.2f, 0, 15, 4);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 5 */
        rp.CompareFiles(3, 4);  /* 6 */
        rp.CompareFiles(3, 5);  /* 7 */

            /* 1000-image multipage tiff */
        pix1 = Pix.Read(ProgFile("char.tif"));
        Pix.WriteTiff("/tmp/lept/tiff/junkm.tif", pix1, L.IFF_TIFF_G4, "w");
        for (int i = 1; i < 1000; i++)
            Pix.WriteTiff("/tmp/lept/tiff/junkm.tif", pix1, L.IFF_TIFF_G4, "a");
        rp.CheckFile("/tmp/lept/tiff/junkm.tif");  /* 8 */

        offset = 0;
        int n = 0;
        do {
            Pix? p = Pix.ReadFromMultipageTiff("/tmp/lept/tiff/junkm.tif", ref offset);
            if (p != null)
                n++;
        } while (offset != 0);
        rp.CompareValues(1000, n, 0);  /* 9 */

        pixa = Pixa.ReadMultipageTiff("/tmp/lept/tiff/junkm.tif");
        pix1 = pixa.DisplayTiledInRows(8, 1500, 0.8f, 0, 15, 4);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 10 */

            /* Memory multipage round-trip */
        pix1 = Pix.Read(ProgFile("weasel8.240c.png"));
        Pixa pixa1 = Pixa.Create(10);
        for (int i = 0; i < 10; i++)
            pixa1.AddPix(pix1, L.L_COPY);
        Pixa.WriteMultipageTiff("/tmp/lept/tiff/junkm2.tif", pixa1);
        rp.CheckFile("/tmp/lept/tiff/junkm2.tif");  /* 11 */
        data = Utils.BinaryRead("/tmp/lept/tiff/junkm2.tif");
        Pixa pixa2 = Pixa.Create(10);
        offset = 0;
        n = 0;
        do {
            Pix? p = Pix.ReadMemFromMultipageTiff(data, ref offset);
            if (p != null) {
                pixa2.AddPix(p, L.L_INSERT);
                n++;
            }
        } while (offset != 0);
        rp.CompareValues(10, n, 0);  /* 12 */
        data = Pixa.WriteMemMultipageTiff(pixa2);
        Pixa pixa3 = Pixa.ReadMemMultipageTiff(data);
        pix1 = pixa3.DisplayTiledInRows(8, 1500, 0.8f, 0, 15, 4);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 13 */
        n = pixa3.Count;
        int success = 1;
        for (int i = 0; i < n; i++) {
            if (!Pix.Equal(pixa1.GetPix(i, L.L_CLONE), pixa3.GetPix(i, L.L_CLONE)))
                success = 0;
        }
        rp.CompareValues(1, success, 0);  /* 14 */

            /* Single-to-multipage I/O + PS/PDF */
        Sarray sa = Sarray.GetSortedPathnamesInDirectory(TestPaths.ProgDir, "weasel4.", 0, 4);
        Sarray.Sort(sa, sa, L.L_SORT_INCREASING);
        int npages = sa.Count;
        for (int i = 0; i < npages; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY)!;
            Pix? p = Pix.Read(fname);
            if (p == null) continue;
            Pix pix2 = p.ConvertTo1(128);
            Pix.WriteTiff("/tmp/lept/tiff/weasel4", pix2, L.IFF_TIFF_G4, i == 0 ? "w+" : "a");
        }
        rp.CheckFile("/tmp/lept/tiff/junkm2.tif");  /* 15 */

        PsIo.ConvertTiffMultipageToPS("/tmp/lept/tiff/weasel4", "/tmp/lept/tiff/weasel4.ps", 0.95f);
        rp.CheckFile("/tmp/lept/tiff/weasel4.ps");  /* 16 */

        PdfIo.SetDateAndVersion(0);
        PdfIo.ConvertTiffMultipageToPdf("/tmp/lept/tiff/weasel4", "/tmp/lept/tiff/weasel4.pdf");
        rp.CheckFile("/tmp/lept/tiff/weasel4.pdf");  /* 17 */

            /* Page count and reversal */
        Pix.WriteMultipageTiff(TestPaths.ProgDir, "weasel2", WeaselOrig);
        rp.CheckFile(WeaselOrig);  /* 18 */
        using (FileStream fp = File.OpenRead(Utils.GenPathname(WeaselOrig, null))) {
            bool isTiff = Pix.FileFormatIsTiff(fp);
            rp.CompareValues(1, isTiff ? 1 : 0, 0);  /* 19 */
            if (isTiff) {
                Pix.TiffGetCount(fp, out npages);
                rp.CompareValues(5, npages, 0);  /* 20 */
            }
        }

        for (int i = 0; i < npages + 1; i++) {
            Pix? p = Pix.ReadTiff(WeaselOrig, i);
            if (p == null) continue;
            Pix.Write($"/tmp/lept/tiff/{i:D3}.tif", p, L.IFF_TIFF_ZIP);
        }

        for (int i = npages - 1; i >= 0; i--) {
            Pix p = Pix.Read($"/tmp/lept/tiff/{i:D3}.tif");
            Pix.WriteTiff(WeaselRev, p, L.IFF_TIFF_ZIP, i == npages - 1 ? "w+" : "a");
        }
        rp.CheckFile(WeaselRev);  /* 21 */

        pixa = Pixa.Create(npages);
        for (int i = 0; i < npages; i++)
            pixa.AddPix(Pix.ReadTiff(WeaselRev, i)!, L.L_INSERT);
        for (int i = npages - 1; i >= 0; i--) {
            Pix p = pixa.GetPix(i, L.L_CLONE);
            Pix.WriteTiff(WeaselRevRev, p, L.IFF_TIFF_ZIP, i == npages - 1 ? "w+" : "a");
        }
        rp.CheckFile(WeaselRevRev);  /* 22 */
        rp.CompareFiles(18, 22);  /* 23 */

            /* Custom public tags */
        pix1 = Pix.Read(ProgFile("feyn.tif"));
        Numa naflags = Numa.Create(10);
        Sarray savals = Sarray.Create(10);
        Sarray satypes = Sarray.Create(10);
        Numa nasizes = Numa.Create(10);

        const string xmp = "<xmp>This is a Fake XMP packet</xmp>\n<text>Guess what ...?</text>";
        naflags.AddNumber(700);
        savals.AddString(xmp, L.L_COPY);
        satypes.AddString("char*", L.L_COPY);
        nasizes.AddNumber(xmp.Length);

        naflags.AddNumber(269);
        savals.AddString("One silly title", L.L_COPY);
        satypes.AddString("const char*", L.L_COPY);

        naflags.AddNumber(270);
        savals.AddString("One page of text", L.L_COPY);
        satypes.AddString("const char*", L.L_COPY);

        naflags.AddNumber(281);
        savals.AddString("4", L.L_COPY);
        satypes.AddString("l_uint16", L.L_COPY);

        naflags.AddNumber(306);
        savals.AddString("2004:10:11 09:35:15", L.L_COPY);
        satypes.AddString("const char*", L.L_COPY);

        naflags.AddNumber(297);
        savals.AddString("1-412", L.L_COPY);
        satypes.AddString("l_uint16-l_uint16", L.L_COPY);

        Pix.WriteTiffCustom("/tmp/lept/tiff/tags.tif", pix1, L.IFF_TIFF_G4, "w",
                            naflags, savals, satypes, nasizes);
        using (var infoStream = new MemoryStream())
            Pix.FprintTiffInfo(infoStream, "/tmp/lept/tiff/tags.tif");

        rp.Cleanup();
    }
}
