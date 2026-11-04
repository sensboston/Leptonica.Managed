using System;
using System.IO;
using System.Text;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

[Collection("LeptDebugOK")]
public class JpegIoTests : IDisposable
{
    private readonly string dir;

    public JpegIoTests()
    {
        dir = Path.Combine(Path.GetTempPath(), "lepsharp_jpeg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static Pix MakeSmoothRgb(int w, int h)
    {
        Pix pix = Pix.Create(w, h, 32);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pix.SetRGBPixel(x, y, (x * 255) / Math.Max(1, w - 1),
                                (y * 255) / Math.Max(1, h - 1),
                                ((x + y) * 127) / Math.Max(1, w + h - 2));
        return pix;
    }

    private static Pix MakeSmoothGray(int w, int h)
    {
        Pix pix = Pix.Create(w, h, 8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pix.SetPixel(x, y, (uint)(((x * 3 + y * 5) * 255) / Math.Max(1, 3 * w + 5 * h)));
        return pix;
    }

    [Fact]
    public void WriteAndRead_RgbAndGray_FileStreamAndMem_WithHeadersResolutionAndComment()
    {
        Pix rgb = MakeSmoothRgb(96, 64);
        rgb.SetResolution(300, 300);
        rgb.Text = "Leptonica.Managed JPEG comment";

        string jpgPath = Path.Combine(dir, "rgb.jpg");
        Pix.WriteJpeg(jpgPath, rgb, 90, 0);

        Pix.ReadHeaderJpeg(jpgPath, out int w, out int h, out int spp, out bool ycck, out bool cmyk);
        Assert.Equal((96, 64, 3, false, false), (w, h, spp, ycck, cmyk));

        using (FileStream fs = File.OpenRead(jpgPath)) {
            Pix.FgetJpegResolution(fs, out int xres, out int yres);
            Pix.FgetJpegComment(fs, out byte[]? comment);
            Assert.Equal((300, 300), (xres, yres));
            Assert.NotNull(comment);
            Assert.Equal("Leptonica.Managed JPEG comment", Encoding.UTF8.GetString(comment!));
        }

        byte[] mem = Pix.WriteMemJpeg(rgb, 90, 1); /* progressive */
        Pix.ReadHeaderMemJpeg(mem, out int mw, out int mh, out int mspp, out _, out _);
        Pix.ReadResolutionMemJpeg(mem, out int mxres, out int myres);
        Assert.Equal((96, 64, 3, 300, 300), (mw, mh, mspp, mxres, myres));

        Pix backFile = Pix.ReadJpeg(jpgPath, 0, 1, out int nwarn1, 0);
        Pix backMem = Pix.ReadMemJpeg(mem, 0, 1, out int nwarn2, 0);
        Assert.Equal(0, nwarn1);
        Assert.Equal(0, nwarn2);
        Assert.True(rgb.GetPSNR(backFile, 1) > 40.0f);
        Assert.True(rgb.GetPSNR(backMem, 1) > 40.0f);

        /* Reduced scale decoding (1/2, 1/4, 1/8) and luminance-only hint */
        Pix half = Pix.ReadMemJpeg(mem, 0, 2, 0);
        Pix quarter = Pix.ReadMemJpeg(mem, 0, 4, 0);
        Pix eighth = Pix.ReadMemJpeg(mem, 0, 8, 0);
        Assert.Equal((48, 32), (half.Width, half.Height));
        Assert.Equal((24, 16), (quarter.Width, quarter.Height));
        Assert.Equal((12, 8), (eighth.Width, eighth.Height));

        Pix lum = Pix.ReadMemJpeg(mem, 0, 1, L.L_JPEG_READ_LUMINANCE);
        Assert.Equal(8, lum.Depth);

        /* Chroma subsampling off (4:4:4) produces higher PSNR and larger file than 4:2:0 */
        Pix noisyRgb = Pix.Create(64, 64, 32);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                noisyRgb.SetRGBPixel(x, y, ((x & 1) == 0) ? 240 : 20, ((y & 1) == 0) ? 30 : 220, 128);
        byte[] sub420 = Pix.WriteMemJpeg(noisyRgb, 90, 0);
        noisyRgb.SetChromaSampling(0);
        byte[] sub444 = Pix.WriteMemJpeg(noisyRgb, 90, 0);
        Assert.True(sub444.Length > sub420.Length);
        Assert.True(noisyRgb.GetPSNR(Pix.ReadMemJpeg(sub444), 1) > noisyRgb.GetPSNR(Pix.ReadMemJpeg(sub420), 1));

        /* Grayscale + RGB colormapped read */
        Pix gray = MakeSmoothGray(80, 48);
        byte[] gmem = Pix.WriteMemJpeg(gray, 85, 0);
        Pix gback = Pix.ReadMemJpeg(gmem, 0, 1, 0);
        Assert.Equal((80, 48, 8), (gback.Width, gback.Height, gback.Depth));

        Pix rgbCmap = Pix.ReadMemJpeg(mem, 1, 1, 0);
        Assert.NotNull(rgbCmap.Colormap);
        Assert.Equal((96, 64, 8), (rgbCmap.Width, rgbCmap.Height, rgbCmap.Depth));
    }

    [Fact]
    public void UpstreamProgJpegSuite_MatchesExpectations()
    {
        if (!Directory.Exists(TestPaths.ProgDir))
            return;

        foreach (string fname in new[] { "test8.jpg", "fish24.jpg", "test24.jpg" }) {
            string path = TestPaths.ProgFile(fname);
            Pix pixs = Pix.Read(path);
            byte[] data = Pix.WriteMemJpeg(pixs, 75, 0);
            Pix pix1 = Pix.ReadMem(data);
            Pix pix2 = Pix.ReadJpeg(path, 0, 1, 0);
            Assert.True(Pix.Equal(pixs, pix2));
            Assert.True(pixs.TestForSimilarity(pix1, 2, 6, 0.01f, 0.0f, 0));
        }

        foreach (string fname in new[] { "weasel2.png", "weasel4.16c.png", "weasel8.240c.png" }) {
            Pix pixs = Pix.Read(TestPaths.ProgFile(fname));
            byte[] data = Pix.WriteMem(pixs, L.IFF_JFIF_JPEG);
            Pix pix1 = Pix.ReadMem(data);
            Pix pixRef = pixs.Colormap != null
                ? pixs.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC)
                : pixs.ConvertTo8(false);
            Assert.True(pix1.TestForSimilarity(pixRef, 1, 20, 0.2f, 0.0f, 0));
        }
    }
}
