using System;
using System.IO;
using System.Text;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// jpegio_reg.c: regression test for jpeg I/O and metadata.
/// </summary>
public class JpegIoRegTests
{
    private static string ProgFile(string name) => TestPaths.ProgFile(name);

    [RegFact]
    public void JpegIo()
    {
        var rp = new RegTest("jpegio");

        DoJpegTest1(rp, "test8.jpg");
        DoJpegTest1(rp, "fish24.jpg");
        DoJpegTest1(rp, "test24.jpg");
        DoJpegTest2(rp, "weasel2.png");
        DoJpegTest2(rp, "weasel2.4g.png");
        DoJpegTest2(rp, "weasel4.png");
        DoJpegTest2(rp, "weasel4.5g.png");
        DoJpegTest2(rp, "weasel4.16c.png");
        DoJpegTest2(rp, "weasel8.16g.png");
        DoJpegTest2(rp, "weasel8.240c.png");
        DoJpegTest3(rp, "lucasta.150.jpg");
        DoJpegTest3(rp, "tetons.jpg");
        DoJpegTest4(rp, "karen8.jpg");

        rp.Cleanup();
    }

    private static void DoJpegTest1(RegTest rp, string fname)
    {
        string path = ProgFile(fname);
        Pix pixs = Pix.Read(path);
        string buf = $"/tmp/lept/regout/jpegio.{rp.Index + 1}.jpg";
        Pix.Write(buf, pixs, L.IFF_JFIF_JPEG);
        Pix pix1 = Pix.Read(buf);
        rp.CompareSimilarPix(pixs, pix1, 6, 0.01f, 0);

        byte[] data = Pix.WriteMemJpeg(pixs, 75, 0);
        Pix pix2 = Pix.ReadMem(data);
        rp.ComparePix(pix1, pix2);

        Pix pix3 = Pix.ReadJpeg(path, 0, 1, 0);
        rp.ComparePix(pixs, pix3);
        buf = $"/tmp/lept/regout/jpegio.{rp.Index + 1}.jpg";
        Pix.WriteJpeg(buf, pix3, 75, 0);
        Pix pix4 = Pix.ReadJpeg(buf, 0, 1, 0);
        rp.ComparePix(pix2, pix4);

        data = Pix.WriteMemJpeg(pixs, 75, 0);
        Pix pix5 = Pix.ReadMemJpeg(data, 0, 1, 0);
        rp.ComparePix(pix4, pix5);
    }

    private static void DoJpegTest2(RegTest rp, string fname)
    {
        string path = ProgFile(fname);
        Pix pixs = Pix.Read(path);
        string buf = $"/tmp/lept/regout/jpegio.{rp.Index + 1}.jpg";
        Pix.Write(buf, pixs, L.IFF_JFIF_JPEG);
        Pix pix1 = Pix.Read(buf);
        Pix pix2 = (pixs.Colormap != null)
            ? pixs.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC)
            : pixs.ConvertTo8(false);
        rp.CompareSimilarPix(pix1, pix2, 20, 0.2f, 0);

        byte[] data = Pix.WriteMemJpeg(pixs, 75, 0);
        Pix pix3 = Pix.ReadMem(data);
        rp.ComparePix(pix1, pix3);

        Pix pix4 = Pix.Read(path);
        buf = $"/tmp/lept/regout/jpegio.{rp.Index + 1}.jpg";
        Pix.WriteJpeg(buf, pix4, 75, 0);
        Pix pix5 = Pix.ReadJpeg(buf, 0, 1, 0);
        rp.ComparePix(pix5, pix5);

        data = Pix.WriteMemJpeg(pixs, 75, 0);
        Pix pix6 = Pix.ReadMemJpeg(data, 0, 1, 0);
        rp.ComparePix(pix5, pix6);
    }

    private static void DoJpegTest3(RegTest rp, string fname)
    {
        string path = ProgFile(fname);
        Pix.ReadHeaderJpeg(path, out int w1, out int h1, out int spp1, out _, out _);
        Pix pixs = Pix.Read(path);
        byte[] data = Pix.WriteMemJpeg(pixs, 75, 0);
        Pix.ReadHeaderMemJpeg(data, out int w2, out int h2, out int spp2, out _, out _);
        rp.CompareValues(w1, w2, 0.0f);
        rp.CompareValues(h1, h2, 0.0f);
        rp.CompareValues(spp1, spp2, 0.0f);

        Pix.ReadHeader(path, out int format1, out w1, out h1, out int bps1, out spp1, out _);
        data = Pix.WriteMem(pixs, L.IFF_JFIF_JPEG);
        Pix.ReadHeaderMem(data, out int format2, out w2, out h2, out int bps2, out spp2, out _);
        rp.CompareValues(format1, format2, 0.0f);
        rp.CompareValues(w1, w2, 0.0f);
        rp.CompareValues(h1, h2, 0.0f);
        rp.CompareValues(bps1, bps2, 0.0f);
        rp.CompareValues(bps1, 8, 0.0f);
        rp.CompareValues(spp1, spp2, 0.0f);
    }

    private static void DoJpegTest4(RegTest rp, string fname)
    {
        string path = ProgFile(fname);
        Pix pixs = Pix.Read(path);
        string comment1 = $"Test {rp.Index + 1}";
        pixs.Text = comment1;
        pixs.SetResolution(137, 137);
        string buf = $"/tmp/lept/regout/jpegio.{rp.Index + 1}.jpg";
        Pix.Write(buf, pixs, L.IFF_JFIF_JPEG);
        rp.CheckFile(buf);

        using (FileStream fp = File.OpenRead(Utils.GenPathname(buf, null))) {
            Pix.FgetJpegResolution(fp, out int xres, out int yres);
            Pix.FgetJpegComment(fp, out byte[]? comment2);
            comment2 ??= Array.Empty<byte>();
            rp.CompareValues(xres, 137, 0.0f);
            rp.CompareValues(yres, 137, 0.0f);
            rp.CompareStrings(Encoding.UTF8.GetBytes(comment1), comment2);
        }
    }
}
