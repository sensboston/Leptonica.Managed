using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// pngio_reg.c: regression test for lossless read/write I/O in png format
/// across depths, colormaps, and alpha transparency.
/// </summary>
public class PngIoRegTests
{
    private const string FILE_1BPP = "rabi.png";
    private const string FILE_2BPP = "speckle2.png";
    private const string FILE_2BPP_C = "weasel2.4g.png";
    private const string FILE_4BPP = "speckle4.png";
    private const string FILE_4BPP_C = "weasel4.16c.png";
    private const string FILE_8BPP = "dreyfus8.png";
    private const string FILE_8BPP_C = "weasel8.240c.png";
    private const string FILE_16BPP = "test16.png";
    private const string FILE_32BPP = "weasel32.png";
    private const string FILE_32BPP_ALPHA = "test32-alpha.png";
    private const string FILE_CMAP_ALPHA = "test-cmap-alpha.png";
    private const string FILE_CMAP_ALPHA2 = "test-cmap-alpha2.png";
    private const string FILE_TRANS_ALPHA = "test-fulltrans-alpha.png";
    private const string FILE_GRAY_ALPHA = "test-gray-alpha.png";
    private const string FILE_TRANS_CMAP_2BPP = "trans-2bpp-cmap.png";
    private const string FILE_TRANS_CMAP_4BPP = "trans-4bpp-cmap.png";
    private const string FILE_TRANS_CMAP_8BPP = "trans-8bpp-cmap.png";

    private static readonly string[] AllFiles = {
        FILE_1BPP, FILE_2BPP, FILE_2BPP_C, FILE_4BPP, FILE_4BPP_C,
        FILE_8BPP, FILE_8BPP_C, FILE_16BPP, FILE_32BPP, FILE_32BPP_ALPHA,
        FILE_CMAP_ALPHA, FILE_CMAP_ALPHA2, FILE_TRANS_ALPHA, FILE_GRAY_ALPHA,
        FILE_TRANS_CMAP_2BPP, FILE_TRANS_CMAP_4BPP, FILE_TRANS_CMAP_8BPP
    };

    [RegFact]
    public void PngIo()
    {
        const string dirout = "/tmp/lept/pngio/";
        var rp = new RegTest("pngio");

            /* ------------ Part 1: Test lossless r/w to file ------------ */
        Utils.LeptRmdir("lept/pngio");
        Utils.LeptMkdir("lept/pngio");
        foreach (string fname in AllFiles) {
            Assert.False(TestFilePng(fname, dirout), $"File PNG r/w failed for {fname}");
        }

            /* ------------ Part 2: Test lossless r/w to memory ------------ */
        foreach (string fname in AllFiles) {
            Assert.False(TestMemPng(fname), $"Memory PNG r/w failed for {fname}");
        }

            /* ------------ Part 3: Test lossless 1, 2, 4 and 8 bpp r/w ------------ */
        Assert.True(Test1bppTrans());
        Assert.True(Test1bppColor());
        Assert.True(Test1bppGray());
        Assert.True(Test1bppBw1());
        Assert.True(Test1bppBw2());
        Assert.True(Test2bppCmapTrans());
        Assert.True(Test4bppCmapTrans());
        Assert.True(Test8bppCmapTrans());
        Assert.True(Test8bppTrans());

            /* -------------- Part 4: Read header information -------------- */
        foreach (string fname in AllFiles) {
            Assert.False(GetHeaderData(fname), $"Header read failed for {fname}");
        }

        rp.Cleanup();
    }

    private static Pix ReadPngLocked(string path)
    {
        lock (TestPaths.PngReadLock) {
            return Pix.Read(path);
        }
    }

    private static Pix ReadMemPngLocked(byte[] data)
    {
        lock (TestPaths.PngReadLock) {
            return Pix.ReadMemPng(data);
        }
    }

        /* File r/w test. Returns true on error */
    private static bool TestFilePng(string fname, string dirout)
    {
        Pix pixs = TestPaths.ReadInput(fname);
        string fileout = dirout + fname;
        Pix.Write(fileout, pixs, L.IFF_PNG);
        Pix pixd = ReadPngLocked(fileout);
        return !Pix.Equal(pixs, pixd);
    }

        /* Memory r/w test. Returns true on error */
    private static bool TestMemPng(string fname)
    {
        Pix pixs = TestPaths.ReadInput(fname);
        byte[] data = Pix.WriteMemPng(pixs, 0.0f);
        Pix pixd = ReadMemPngLocked(data);
        return !Pix.Equal(pixs, pixd);
    }

        /* Retrieve header data from file and from array in memory. Returns true on error */
    private static bool GetHeaderData(string filename)
    {
        string filepath = Path.Combine(TestPaths.ProgDir, filename);
        long nbytes1 = Utils.NbytesInFile(filepath);
        bool ok1 = Pix.FindFileFormat(filepath, out int format1);
        Pix.ReadHeaderPng(filepath, out int w1, out int h1, out int bps1, out int spp1, out bool iscmap1);
        int d1 = bps1 * spp1;
        if (d1 == 24) d1 = 32;
        if (!ok1 || format1 != L.IFF_PNG)
            return true;

        byte[] data = Utils.BinaryRead(filepath);
        long nbytes2 = data.Length;
        bool ok2 = Pix.FindFileFormatBuffer(data, out int format2);
        Pix.ReadHeaderMemPng(data, out int w2, out int h2, out int bps2, out int spp2, out bool iscmap2);
        int d2 = bps2 * spp2;
        if (d2 == 24) d2 = 32;
        if (!ok2)
            return true;

        if (nbytes1 != nbytes2 || format1 != format2 || w1 != w2 ||
            h1 != h2 || d1 != d2 || bps1 != bps2 || spp1 != spp2 ||
            iscmap1 != iscmap2)
            return true;

        return false;
    }

    private static bool Test1bppTrans()
    {
        Pix pix1 = TestPaths.ReadInput("feyn-fract2.tif");
        PixColormap cmap = PixColormap.Create(1);
        pix1.SetColormap(cmap);
        cmap.AddRGBA(180, 130, 220, 0);    /* transparent */
        cmap.AddRGBA(20, 120, 0, 255);     /* opaque */
        Pix.Write("/tmp/lept/pngio/1bpp-trans.png", pix1, L.IFF_PNG);
        Pix pix2 = ReadPngLocked("/tmp/lept/pngio/1bpp-trans.png");
        bool same = Pix.Equal(pix1, pix2);
        using FileStream fp = Utils.FopenReadStream("/tmp/lept/pngio/1bpp-trans.png");
        Pix.FgetPngColormapInfo(fp, out _, out bool transp);
        return same && transp;
    }

    private static bool Test1bppColor()
    {
        Pix pix1 = TestPaths.ReadInput("feyn-fract2.tif");
        PixColormap cmap = PixColormap.Create(1);
        pix1.SetColormap(cmap);
        cmap.AddRGBA(180, 130, 220, 255);  /* color, opaque */
        cmap.AddRGBA(20, 120, 0, 255);     /* color, opaque */
        Pix.Write("/tmp/lept/pngio/1bpp-color.png", pix1, L.IFF_PNG);
        Pix pix2 = ReadPngLocked("/tmp/lept/pngio/1bpp-color.png");
        bool same = Pix.Equal(pix1, pix2);
        using FileStream fp = Utils.FopenReadStream("/tmp/lept/pngio/1bpp-color.png");
        Pix.FgetPngColormapInfo(fp, out _, out bool transp);
        return same && !transp;
    }

    private static bool Test1bppGray()
    {
        Pix pix1 = TestPaths.ReadInput("feyn-fract2.tif");
        PixColormap cmap = PixColormap.Create(1);
        pix1.SetColormap(cmap);
        cmap.AddRGBA(180, 180, 180, 255);  /* light, opaque */
        cmap.AddRGBA(60, 60, 60, 255);     /* dark, opaque */
        Pix.Write("/tmp/lept/pngio/1bpp-gray.png", pix1, L.IFF_PNG);
        Pix pix2 = ReadPngLocked("/tmp/lept/pngio/1bpp-gray.png");
        return Pix.Equal(pix1, pix2);
    }

    private static bool Test1bppBw1()
    {
        Pix pix1 = TestPaths.ReadInput("feyn-fract2.tif");
        PixColormap cmap = PixColormap.Create(1);
        pix1.SetColormap(cmap);
        cmap.AddRGBA(0, 0, 0, 255);        /* black, opaque */
        cmap.AddRGBA(255, 255, 255, 255);  /* white, opaque */
        Pix.Write("/tmp/lept/pngio/1bpp-bw1.png", pix1, L.IFF_PNG);
        Pix pix2 = ReadPngLocked("/tmp/lept/pngio/1bpp-bw1.png");
        return Pix.Equal(pix1, pix2);
    }

    private static bool Test1bppBw2()
    {
        Pix pix1 = TestPaths.ReadInput("feyn-fract2.tif");
        PixColormap cmap = PixColormap.Create(1);
        pix1.SetColormap(cmap);
        cmap.AddRGBA(255, 255, 255, 255);  /* white, opaque */
        cmap.AddRGBA(0, 0, 0, 255);        /* black, opaque */
        Pix.Write("/tmp/lept/pngio/1bpp-bw2.png", pix1, L.IFF_PNG);
        Pix pix2 = ReadPngLocked("/tmp/lept/pngio/1bpp-bw2.png");
        return Pix.Equal(pix1, pix2);
    }

    private static bool Test2bppCmapTrans()
    {
        Pix pix1 = TestPaths.ReadInput(FILE_TRANS_CMAP_2BPP);
        pix1.GetDimensions(out int w, out int h, out _);
        if (w != 82 || h != 73)
            return false;

        pix1 = TestPaths.ReadInput("weasel2.4g.png");
        PixColormap cmap = pix1.Colormap!;
        cmap.SetAlpha(2, 100);
        Pix.Write("/tmp/lept/pngio/2bpp-cmap-trans.png", pix1, L.IFF_PNG);
        Pix pix2 = ReadPngLocked("/tmp/lept/pngio/2bpp-cmap-trans.png");
        return Pix.Equal(pix1, pix2);
    }

    private static bool Test4bppCmapTrans()
    {
        Pix pix1 = TestPaths.ReadInput(FILE_TRANS_CMAP_4BPP);
        pix1.GetDimensions(out int w, out int h, out _);
        if (w != 82 || h != 73)
            return false;

        pix1 = TestPaths.ReadInput("weasel4.5g.png");
        PixColormap cmap = pix1.Colormap!;
        cmap.SetAlpha(2, 60);
        Pix.Write("/tmp/lept/pngio/4bpp-cmap-trans.png", pix1, L.IFF_PNG);
        Pix pix2 = ReadPngLocked("/tmp/lept/pngio/4bpp-cmap-trans.png");
        return Pix.Equal(pix1, pix2);
    }

    private static bool Test8bppCmapTrans()
    {
        Pix pix1 = TestPaths.ReadInput(FILE_TRANS_CMAP_8BPP);
        pix1.GetDimensions(out int w, out int h, out _);
        if (w != 82 || h != 73)
            return false;

        pix1 = TestPaths.ReadInput("weasel8.5g.png");
        PixColormap cmap = pix1.Colormap!;
        cmap.SetAlpha(2, 80);
        cmap.SetAlpha(3, 80);
        Pix.Write("/tmp/lept/pngio/8bpp-cmap-trans.png", pix1, L.IFF_PNG);
        Pix pix2 = ReadPngLocked("/tmp/lept/pngio/8bpp-cmap-trans.png");
        return Pix.Equal(pix1, pix2);
    }

    private static bool Test8bppTrans()
    {
        Pix pix1 = TestPaths.ReadInput("wyom.jpg");
        Pix pix2 = pix1.ColorSegment(75, 10, 8, 7, 0);
        PixColormap cmap = pix2.Colormap!;
        cmap.SetAlpha(0, 0);  /* set blueish sky color to transparent */
        Pix.Write("/tmp/lept/pngio/8bpp-trans.png", pix2, L.IFF_PNG);
        Pix pix3 = ReadPngLocked("/tmp/lept/pngio/8bpp-trans.png");
        bool same = Pix.Equal(pix2, pix3);
        using FileStream fp = Utils.FopenReadStream("/tmp/lept/pngio/8bpp-trans.png");
        Pix.FgetPngColormapInfo(fp, out _, out bool transp);
        return same && transp;
    }
}
