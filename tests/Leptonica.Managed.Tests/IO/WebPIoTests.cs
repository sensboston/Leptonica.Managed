using System;
using System.IO;
using System.Text;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

[Collection("LeptDebugOK")]
public class WebPIoTests : IDisposable
{
    private readonly string dir;

    public WebPIoTests()
    {
        dir = Path.Combine(Path.GetTempPath(), "lepsharp_webp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Fact]
    public void LosslessAndLossy_RgbAndRgba_HeadersAndRoundTrip()
    {
        Pix rgba = Pix.Create(64, 48, 32);
        rgba.Spp = 4;
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < 64; x++) {
                Pix.ComposeRGBAPixel((x * 4) & 255, (y * 5) & 255, ((x + y) * 2) & 255, ((x * 3 + y) & 127) + 64, out uint px);
                rgba.SetPixel(x, y, px);
            }

        /* Lossless RGBA */
        byte[] losslessMem = Pix.WriteMemWebP(rgba, 0, 1);
        Pix.ReadHeaderMemWebP(losslessMem, out int lw, out int lh, out int lspp);
        Assert.Equal((64, 48, 4), (lw, lh, lspp));
        Pix losslessBack = Pix.ReadMemWebP(losslessMem);
        Assert.Equal(4, losslessBack.Spp);
        Assert.True(Pix.Equal(rgba, losslessBack));

        /* Lossy RGBA (VP8X + ALPH + VP8) preserves exact lossless alpha */
        string lossyAlphaPath = Path.Combine(dir, "lossy_alpha.webp");
        Pix.WriteWebP(lossyAlphaPath, rgba, 85, 0);
        Pix.ReadHeaderWebP(lossyAlphaPath, out int aw, out int ah, out int aspp);
        Assert.Equal((64, 48, 4), (aw, ah, aspp));
        Pix lossyAlphaBack = Pix.Read(lossyAlphaPath);
        Assert.Equal(4, lossyAlphaBack.Spp);
        Assert.True(Pix.Equal(rgba.GetRGBComponent(L.L_ALPHA_CHANNEL),
                              lossyAlphaBack.GetRGBComponent(L.L_ALPHA_CHANNEL)));
        Assert.True(rgba.GetPSNR(lossyAlphaBack, 1) > 35.0f);

        /* Animated WebP (VP8X + ANIM + ANMF) */
        Pixa frames = Pixa.Create(2);
        frames.AddPix(rgba, L.L_COPY);
        frames.AddPix(Pix.Rotate180(null, rgba), L.L_INSERT);
        byte[] animLossy = Pixa.WriteMemWebPAnim(frames, 3, 120, 80, 0);
        byte[] animLossless = Pixa.WriteMemWebPAnim(frames, 0, 100, 75, 1);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(animLossy, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(animLossy, 8, 4));
        Assert.Equal("VP8X", Encoding.ASCII.GetString(animLossy, 12, 4));
        Assert.Contains("ANIM", Encoding.Latin1.GetString(animLossy));
        Assert.Contains("ANMF", Encoding.Latin1.GetString(animLossy));

        Pix firstFrameLossless = Pix.ReadMemWebP(animLossless);
        Assert.True(Pix.Equal(rgba, firstFrameLossless));
    }

    [Fact]
    public void UpstreamProgWebPSuite_PsnrAndSimilarityTargets()
    {
        if (!Directory.Exists(TestPaths.ProgDir))
            return;

        foreach (string fname in new[] { "weasel2.4c.png", "weasel8.240c.png", "karen8.jpg", "test24.jpg" }) {
            Pix pixs = Pix.Read(TestPaths.ProgFile(fname));
            byte[] mem = Pix.WriteMem(pixs, L.IFF_WEBP);
            Pix pix1 = Pix.ReadMem(mem);
            Pix pix2 = pixs.ConvertTo32();
            int factor = Math.Max(1, Math.Min(Math.Max(pix1.Width, pix1.Height) / 400, 4));
            Assert.True(pix1.TestForSimilarity(pix2, factor, 20, 0.1f, 0.0f, 0),
                        $"WebP similarity failed on {fname}");
        }

        /* Exact webpio_reg.c DoWebpTest2 PSNR targets on test24.jpg */
        Pix test24 = Pix.Read(TestPaths.ProgFile("test24.jpg"));
        foreach ((int q, int lossless, float expected, float delta) in new[] {
            (50, 0, 43.50f, 1.0f),
            (75, 0, 46.07f, 1.0f),
            (90, 0, 51.09f, 2.0f),
            (100, 0, 54.979f, 5.0f),
            (0, 1, 1000.0f, 0.1f),
        }) {
            byte[] wmem = Pix.WriteMemWebP(test24, q, lossless);
            Pix back = Pix.ReadMemWebP(wmem);
            float psnr = test24.GetPSNR(back, 4);
            Assert.True(Math.Abs(expected - psnr) <= delta,
                        $"WebP q={q} lossless={lossless}: expected {expected}±{delta}, got {psnr}");
            if (lossless == 1)
                Assert.True(Pix.Equal(test24, back));
        }
    }
}
