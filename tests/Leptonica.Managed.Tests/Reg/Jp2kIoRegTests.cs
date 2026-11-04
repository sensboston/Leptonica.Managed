using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// jp2kio_reg.c: regression test for lossy read/write I/O in jp2k format.
/// </summary>
public class Jp2kIoRegTests
{
    private static string ProgFile(string name) => TestPaths.ProgFile(name);

    [RegFact]
    public void Jp2kIo()
    {
        var rp = new RegTest("jp2kio");

        DoJp2kTest1(rp, "karen8.jpg");
        DoJp2kTest1(rp, "test24.jpg");
        DoJp2kTest2(rp, "test24.jpg");
        DoJp2kTest3(rp, "wyom.jpg");

        rp.Cleanup();
    }

    private static void DoJp2kTest1(RegTest rp, string fname)
    {
        Pix pix0 = Pix.Read(ProgFile(fname));
        Pix pix1 = pix0.Scale(0.5f, 0.5f);
        int w = pix1.Width;
        int h = pix1.Height;
        rp.WritePixAndCheck(pix1, L.IFF_JP2);  /* 0, 5 */
        string name = rp.GenLocalFilename(-1, L.IFF_JP2);
        Pix pix2 = Pix.Read(name);
        rp.WritePixAndCheck(pix2, L.IFF_JP2);  /* 1, 6 */

        Box box = Box.Create(w / 4, h / 4, w / 2, h / 2);
        pix1 = Pix.ReadJp2k(name, 1, box, 0, 0);
        string buf = $"/tmp/lept/regout/jp2kio.{rp.Index + 1:D2}.jp2";
        Pix.WriteJp2k(buf, pix1, 38, 0, 0, 0);
        rp.CheckFile(buf);  /* 2, 7 */
        pix2 = Pix.Read(buf);
        rp.WritePixAndCheck(pix2, L.IFF_JP2);  /* 3, 8 */
        Pix pix3 = Pix.ReadJp2k(buf, 2, null, 0, 0);
        rp.WritePixAndCheck(pix3, L.IFF_JP2);  /* 4, 9 */
    }

    private static void DoJp2kTest2(RegTest rp, string fname)
    {
        Pix pix0 = Pix.Read(ProgFile(fname));
        Pix pix1 = pix0.Scale(0.5f, 0.5f);
        int w = pix1.Width;
        int h = pix1.Height;
        rp.WritePixAndCheck(pix1, L.IFF_JP2);  /* 10 */
        string name = rp.GenLocalFilename(-1, L.IFF_JP2);
        Pix pix2 = Pix.Read(name);
        rp.WritePixAndCheck(pix2, L.IFF_JP2);  /* 11 */
        byte[] data = Utils.BinaryRead(name);
        Pix pix3 = Pix.ReadMemJp2k(data, 1, null, 0, 0);
        rp.WritePixAndCheck(pix3, L.IFF_JP2);  /* 12 */

        Box box = Box.Create(w / 3, h / 3, w / 3, h / 3);
        pix1 = Pix.ReadJp2k(name, 1, box, 0, 0);
        string buf = $"/tmp/lept/regout/jp2kio.{rp.Index + 1:D2}.jp2";
        Pix.WriteJp2k(buf, pix1, 38, 0, 0, 0);
        rp.CheckFile(buf);  /* 13 */
        data = Utils.BinaryRead(buf);
        pix2 = Pix.ReadMemJp2k(data, 1, null, 0, 0);
        rp.WritePixAndCheck(pix2, L.IFF_JP2);  /* 14 */
        pix3 = Pix.ReadMemJp2k(data, 2, null, 0, 0);
        rp.WritePixAndCheck(pix3, L.IFF_JP2);  /* 15 */
    }

    private static void DoJp2kTest3(RegTest rp, string fname)
    {
        Utils.LeptMkdir("lept/jp2k");
        Pix pix0 = Pix.Read(ProgFile(fname));
        string outPath = Utils.GenPathname("/tmp/lept/jp2k/wyom.j2k", null);
        using (FileStream fp = File.Create(outPath))
            Pix.WriteStreamJp2k(fp, pix0, 34, 6, L.L_J2K_CODEC, 0, 0);
        Pix pix1 = Pix.Read("/tmp/lept/jp2k/wyom.j2k");
        rp.CompareSimilarPix(pix0, pix1, 20, 0.01f, 0);  /* 16 */
    }
}
