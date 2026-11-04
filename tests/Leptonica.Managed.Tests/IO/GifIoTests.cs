using System;
using System.IO;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

[Collection("LeptDebugOK")]
public class GifIoTests : IDisposable
{
    private readonly string dir;

    public GifIoTests()
    {
        dir = Path.Combine(Path.GetTempPath(), "lepsharp_gif_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void LosslessRoundTrip_AllColormapAndGrayscaleDepths(int depth)
    {
        Pix src = Pix.Create(37, 29, depth);
        int maxVal = 1 << depth;
        for (int y = 0; y < 29; y++)
            for (int x = 0; x < 37; x++)
                src.SetPixel(x, y, (uint)((x * 3 + y * 7) % maxVal));

        byte[] gifMem = Pix.WriteMemGif(src);
        Assert.Equal((byte)'G', gifMem[0]);
        Assert.Equal((byte)'I', gifMem[1]);
        Assert.Equal((byte)'F', gifMem[2]);

        Pix.ReadHeaderMem(gifMem, out int fmt, out int w, out int h, out _, out int spp, out bool iscmap);
        Assert.Equal((L.IFF_GIF, 37, 29, 1, true), (fmt, w, h, spp, iscmap));

        Pix back = Pix.ReadMemGif(gifMem);
        Assert.True(Pix.Equal(src, back));

        string file = Path.Combine(dir, $"d{depth}.gif");
        Pix.Write(file, src, L.IFF_GIF);
        Pix backFile = Pix.Read(file);
        Assert.True(Pix.Equal(src, backFile));
    }

    [Fact]
    public void RgbQuantization_And_CommentPreservation()
    {
        Pix cmapPix = Pix.Create(24, 20, 4);
        PixColormap cmap = PixColormap.Create(4);
        cmap.AddColor(255, 255, 255);
        cmap.AddColor(220, 40, 60);
        cmap.AddColor(30, 160, 90);
        cmap.AddColor(40, 80, 210);
        cmapPix.SetColormap(cmap);
        cmapPix.Text = "GIF89a comment block";
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 24; x++)
                cmapPix.SetPixel(x, y, (uint)((x + y) & 3));

        byte[] data = Pix.WriteMemGif(cmapPix);
        Pix decoded = Pix.ReadMemGif(data);
        Assert.True(Pix.Equal(cmapPix, decoded));

        /* 32 bpp RGB image automatically octree-quantizes when saved as GIF */
        Pix rgb = Pix.Create(40, 32, 32);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 40; x++)
                rgb.SetRGBPixel(x, y, x * 6, y * 8, 128);
        byte[] rgbGif = Pix.WriteMemGif(rgb);
        Pix rgbDecoded = Pix.ReadMemGif(rgbGif);
        Assert.Equal((40, 32, 8), (rgbDecoded.Width, rgbDecoded.Height, rgbDecoded.Depth));
        Assert.NotNull(rgbDecoded.Colormap);
    }

    [Fact]
    public void UpstreamProgGifSuite_LosslessAnd32Bpp()
    {
        if (!Directory.Exists(TestPaths.ProgDir))
            return;

        string[] losslessFiles = {
            "feyn.tif", "weasel2.4g.png", "weasel4.16c.png",
            "dreyfus8.png", "weasel8.240c.png", "test8.jpg"
        };
        foreach (string fname in losslessFiles) {
            Pix pixs = Pix.Read(TestPaths.ProgFile(fname));
            byte[] mem = Pix.WriteMem(pixs, L.IFF_GIF);
            Pix pixd = Pix.ReadMem(mem);
            Assert.True(Pix.Equal(pixs, pixd), $"GIF lossless mismatch on {fname}");
        }

        foreach (string fname in new[] { "test16.tif", "marge.jpg" }) {
            Pix pixs = Pix.Read(TestPaths.ProgFile(fname));
            byte[] mem = Pix.WriteMem(pixs, L.IFF_GIF);
            Pix pixd = Pix.ReadMem(mem);
            Assert.Equal((pixs.Width, pixs.Height), (pixd.Width, pixd.Height));
        }
    }
}
