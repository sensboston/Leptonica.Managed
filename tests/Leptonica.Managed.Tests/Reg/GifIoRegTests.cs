using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// gifio_reg.c: regression test for lossless read/write I/O in gif format.
/// </summary>
public class GifIoRegTests
{
    private const string FILE_1BPP = "feyn.tif";
    private const string FILE_2BPP = "weasel2.4g.png";
    private const string FILE_4BPP = "weasel4.16c.png";
    private const string FILE_8BPP_1 = "dreyfus8.png";
    private const string FILE_8BPP_2 = "weasel8.240c.png";
    private const string FILE_8BPP_3 = "test8.jpg";
    private const string FILE_16BPP = "test16.tif";
    private const string FILE_32BPP = "marge.jpg";

    private static string ProgFile(string name) => TestPaths.ProgFile(name);

    [RegFact]
    public void GifIo()
    {
        var rp = new RegTest("gifio");

        Utils.LeptRmdir("lept/gif");
        Utils.LeptMkdir("lept/gif");

            /* ------------ Part 1: Test lossless r/w to file ------------ */
        TestGif(FILE_1BPP, rp);
        TestGif(FILE_2BPP, rp);
        TestGif(FILE_4BPP, rp);
        TestGif(FILE_8BPP_1, rp);
        TestGif(FILE_8BPP_2, rp);
        TestGif(FILE_8BPP_3, rp);
        TestGif(FILE_16BPP, rp);
        TestGif(FILE_32BPP, rp);

            /* ------------ Part 2: Test lossless r/w to memory ------------ */
        Assert.False(TestMemGif(FILE_1BPP, 0));
        Assert.False(TestMemGif(FILE_2BPP, 1));
        Assert.False(TestMemGif(FILE_4BPP, 2));
        Assert.False(TestMemGif(FILE_8BPP_1, 3));
        Assert.False(TestMemGif(FILE_8BPP_2, 4));
        Assert.False(TestMemGif(FILE_8BPP_3, 5));
        Assert.False(TestMemGif(FILE_16BPP, 6));
        Assert.False(TestMemGif(FILE_32BPP, 7));

        rp.Cleanup();
    }

    private static void TestGif(string fname, RegTest rp)
    {
        Pix pixs = TestPaths.ReadInput(fname);
        string bufA = $"/tmp/lept/gif/gifio-a.{rp.Index + 1}.gif";
        Pix.Write(bufA, pixs, L.IFF_GIF);
        Pix pix1 = Pix.Read(bufA);
        string bufB = $"/tmp/lept/gif/gifio-b.{rp.Index + 1}.gif";
        Pix.Write(bufB, pix1, L.IFF_GIF);
        Pix pix2 = Pix.Read(bufB);
        rp.WritePixAndCheck(pix2, L.IFF_GIF);
        bool same = Pix.Equal(pixs, pix2);
        if (rp.Index < 6)
            Assert.True(same, $"GIF file round-trip mismatch for {fname}");
    }

    private static bool TestMemGif(string fname, int index)
    {
        Pix pixs = TestPaths.ReadInput(fname);
        byte[] data = Pix.WriteMem(pixs, L.IFF_GIF);
        Pix pixd = Pix.ReadMem(data);
        bool same = Pix.Equal(pixs, pixd);
        return !same && index < 6;
    }
}
