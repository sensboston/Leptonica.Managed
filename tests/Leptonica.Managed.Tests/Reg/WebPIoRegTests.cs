using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// webpio_reg.c: regression test for lossy and lossless read/write I/O in webp format.
/// </summary>
public class WebPIoRegTests
{
    private static string ProgFile(string name) => TestPaths.ProgFile(name);

    [RegFact]
    public void WebPIo()
    {
        var rp = new RegTest("webpio");

        Utils.LeptRmdir("lept/webp");
        Utils.LeptMkdir("lept/webp");

        DoWebpTest1(rp, "weasel2.4c.png");
        DoWebpTest1(rp, "weasel8.240c.png");
        DoWebpTest1(rp, "karen8.jpg");
        DoWebpTest1(rp, "test24.jpg");

        DoWebpTest2(rp, "test24.jpg", 50, 0, 43.50f, 1.0f);
        DoWebpTest2(rp, "test24.jpg", 75, 0, 46.07f, 1.0f);
        DoWebpTest2(rp, "test24.jpg", 90, 0, 51.09f, 2.0f);
        DoWebpTest2(rp, "test24.jpg", 100, 0, 54.979f, 5.0f);
        DoWebpTest2(rp, "test24.jpg", 0, 1, 1000.0f, 0.1f);

        rp.Cleanup();
    }

    private static void DoWebpTest1(RegTest rp, string fname)
    {
        Pix pixs = Pix.Read(ProgFile(fname));
        string buf = $"/tmp/lept/webp/webpio.{rp.Index + 1}.webp";
        Pix.Write(buf, pixs, L.IFF_WEBP);
        rp.CheckFile(buf);
        Pix pix1 = Pix.Read(buf);
        Pix pix2 = pixs.ConvertTo32();
        rp.CompareSimilarPix(pix1, pix2, 20, 0.1f, 0);
    }

    private static void DoWebpTest2(RegTest rp, string fname, int quality, int lossless, float expected, float delta)
    {
        Pix pixs = Pix.Read(ProgFile(fname));
        Pix.WriteWebP("/tmp/lept/webp/junk.webp", pixs, quality, lossless);
        Pix pix1 = Pix.Read("/tmp/lept/webp/junk.webp");
        float psnr = pixs.GetPSNR(pix1, 4);
        rp.CompareValues(expected, psnr, delta);
    }
}
