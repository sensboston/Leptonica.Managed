using System;
using System.IO;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

[Collection("LeptDebugOK")]
public class Jp2kIoTests : IDisposable
{
    private readonly string dir;

    public Jp2kIoTests()
    {
        dir = Path.Combine(Path.GetTempPath(), "lepsharp_jp2k_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Fact]
    public void LosslessAndLossy_Jp2AndJ2kCodestream_BoxCropAndReduction()
    {
        Pix rgb = Pix.Create(128, 96, 32);
        for (int y = 0; y < 96; y++)
            for (int x = 0; x < 128; x++)
                rgb.SetRGBPixel(x, y, (x * 2) & 255, (y * 2 + 20) & 255, ((x + y) & 255));

        /* Lossless JP2 (quality = 100) is bit-exact */
        byte[] losslessJp2 = Pix.WriteMemJp2k(rgb, 100, 4, 0, 0);
        Pix.ReadHeaderMem(losslessJp2, out int fmt, out int w, out int h, out int bps, out int spp, out _);
        Assert.Equal((L.IFF_JP2, 128, 96, 8, 3), (fmt, w, h, bps, spp));
        Pix backLossless = Pix.ReadMemJp2k(losslessJp2, 1, null, 0, 0);
        Assert.True(Pix.Equal(rgb, backLossless));

        /* Reduced resolution (1/2, 1/4) and cropped box decoding */
        Pix half = Pix.ReadMemJp2k(losslessJp2, 2, null, 0, 0);
        Pix quarter = Pix.ReadMemJp2k(losslessJp2, 4, null, 0, 0);
        Assert.Equal((64, 48), (half.Width, half.Height));
        Assert.Equal((32, 24), (quarter.Width, quarter.Height));

        Box cropBox = Box.Create(32, 24, 64, 48);
        Pix cropped = Pix.ReadMemJp2k(losslessJp2, 1, cropBox, 0, 0);
        Assert.Equal((64, 48), (cropped.Width, cropped.Height));
        Pix refCrop = rgb.ClipRectangle(cropBox, out _)!;
        Assert.True(Pix.Equal(refCrop, cropped));

        /* Raw J2K codestream (L_J2K_CODEC) and RGBA 4-component JP2 */
        string j2kPath = Path.Combine(dir, "raw.j2k");
        using (FileStream fs = File.Create(j2kPath))
            Pix.WriteStreamJp2k(fs, rgb, 34, 5, L.L_J2K_CODEC, 0, 0);
        Pix backJ2k = Pix.Read(j2kPath);
        Assert.True(rgb.GetPSNR(backJ2k, 1) > 38.0f);

        Pix rgba = Pix.Copy(null, rgb);
        rgba.Spp = 4;
        rgba.SetComponentArbitrary(L.L_ALPHA_CHANNEL, 195);
        byte[] rgbaJp2 = Pix.WriteMemJp2k(rgba, 100, 4, 0, 0);
        Pix rgbaBack = Pix.ReadMemJp2k(rgbaJp2, 1, null, 0, 0);
        Assert.Equal(4, rgbaBack.Spp);
        Assert.True(Pix.Equal(rgba, rgbaBack));
    }

    [Fact]
    public void UpstreamProgJp2kSuite_GrayscaleAndColor()
    {
        if (!Directory.Exists(TestPaths.ProgDir))
            return;

        foreach (string fname in new[] { "karen8.jpg", "wyom.jpg" }) {
            Pix pix0 = Pix.Read(TestPaths.ProgFile(fname));
            byte[] jp2 = Pix.WriteMemJp2k(pix0, 34, 5, 0, 0);
            Pix pix1 = Pix.ReadMemJp2k(jp2, 1, null, 0, 0);
            Assert.True(pix0.TestForSimilarity(pix1, 2, 20, 0.01f, 0.0f, 0),
                        $"JP2K similarity failed on {fname}");
        }
    }
}
