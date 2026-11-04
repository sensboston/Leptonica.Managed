using System;
using System.IO;
using System.Text;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.IO;

public class PsIoTests
{
    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lept_psiotests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void WriteStringAndMemPS_Uncompressed_1_8_32Bpp()
    {
        Pix pix1 = Pix.Create(16, 12, 1);
        pix1.SetPixel(2, 3, 1);
        string ps1 = pix1.WriteStringPS(null, 300, 1.0f);
        Assert.StartsWith("%!Adobe-PS\n", ps1);
        Assert.Contains("%%BoundingBox:", ps1);
        Assert.Contains("{1 exch sub} settransfer", ps1);
        Assert.Contains("showpage\n", ps1);

        Pix pix8 = Pix.Create(8, 6, 8);
        pix8.SetPixel(1, 1, 0xab);
        Box box = Box.Create(100, 200, 500, 400);
        byte[] ps8Bytes = Pix.WriteMemPS(pix8, box, 150, 1.0f);
        string ps8 = Encoding.Latin1.GetString(ps8Bytes);
        Assert.Contains("gsave", ps8);
        Assert.Contains("bind image", ps8);
        Assert.Contains("grestore", ps8);

        Pix pix32 = Pix.Create(4, 4, 32);
        pix32.SetRGBPixel(0, 0, 0x12, 0x34, 0x56);
        using var ms = new MemoryStream();
        Pix.WriteStreamPS(ms, pix32, null, 0, 0.5f);
        string ps32 = Encoding.Latin1.GetString(ms.ToArray());
        Assert.Contains("false 3 colorimage", ps32);
        Assert.Contains("123456", ps32);
    }

    [Fact]
    public void ConvertFlateToPS_And_PSEmbed_Level1_And_Level3()
    {
        string dir = NewTempDir();
        try {
            Pix pix = Pix.Create(24, 18, 8);
            PixColormap cmap = PixColormap.Create(8);
            cmap.AddColor(255, 255, 255);
            cmap.AddColor(20, 80, 180);
            pix.SetColormap(cmap);
            pix.SetPixel(4, 5, 1);

            string pngPath = Path.Combine(dir, "sample.png");
            Pix.Write(pngPath, pix, L.IFF_PNG);

            string psFlate = Path.Combine(dir, "sample_flate.ps");
            PsIo.ConvertFlateToPS(pngPath, psFlate, "w", 10, 20, 300, 1.0f, 1, true);
            string flateText = File.ReadAllText(psFlate, Encoding.Latin1);
            Assert.Contains("%!PS-Adobe-3.0 EPSF-3.0", flateText);
            Assert.Contains("/FlateDecode filter", flateText);
            Assert.Contains("/Indexed /DeviceRGB", flateText);

            string psEmbed1 = Path.Combine(dir, "embed1.ps");
            PsIo.ConvertToPSEmbed(pngPath, psEmbed1, 1);
            Assert.Contains("%!Adobe-PS", File.ReadAllText(psEmbed1, Encoding.Latin1));

            string psEmbed3 = Path.Combine(dir, "embed3.ps");
            PsIo.ConvertToPSEmbed(pngPath, psEmbed3, 3);
            Assert.Contains("/FlateDecode filter", File.ReadAllText(psEmbed3, Encoding.Latin1));
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConvertFilesToPS_And_PixaWriteCompressedToPS_Level3()
    {
        string dir = NewTempDir();
        try {
            string inDir = Path.Combine(dir, "in");
            Directory.CreateDirectory(inDir);

            Pixa pixa = Pixa.Create(2);
            for (int i = 0; i < 2; i++) {
                Pix pix = Pix.Create(20 + 4 * i, 16, 8);
                pix.SetAllArbitrary((uint)(40 + 50 * i));
                Pix.Write(Path.Combine(inDir, $"p{i}.png"), pix, L.IFF_PNG);
                pixa.AddPix(pix, L.L_INSERT);
            }

            string psDirOut = Path.Combine(dir, "dir.ps");
            PsIo.ConvertFilesToPS(inDir, ".png", 150, psDirOut);
            string text1 = File.ReadAllText(psDirOut, Encoding.Latin1);
            Assert.Contains("%%Page: 1 1", text1);
            Assert.Contains("%%Page: 2 2", text1);
            Assert.DoesNotContain("%%BoundingBox:", text1);

            string psFittedOut = Path.Combine(dir, "fitted.ps");
            PsIo.ConvertFilesFittedToPS(inDir, ".png", 612.0f, 792.0f, psFittedOut);
            Assert.Contains("%%Page: 2 2", File.ReadAllText(psFittedOut, Encoding.Latin1));

            string psPixaOut = Path.Combine(dir, "pixa.ps");
            pixa.WriteCompressedToPS(psPixaOut, 150, 3);
            Assert.Contains("%%Page: 2 2", File.ReadAllText(psPixaOut, Encoding.Latin1));
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolutionHelpers_And_BoundingBoxFlag()
    {
        int resLetter = PsIo.GetResLetterPage(2550, 3300, 0.0f);
        int resA4 = PsIo.GetResA4Page(2480, 3508, 0.0f);
        Assert.InRange(resLetter, 300, 330);
        Assert.InRange(resA4, 300, 330);

        PsIo.WriteBoundingBox(false);
        try {
            Assert.False(PsIo.ShouldWriteBoundingBox);
        } finally {
            PsIo.WriteBoundingBox(true);
        }
        Assert.True(PsIo.ShouldWriteBoundingBox);
    }
}
