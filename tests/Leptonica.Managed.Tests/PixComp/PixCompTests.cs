using System;
using System.IO;
using System.Text;
using Leptonica;
using Leptonica.Managed.Tests.IOTests;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.PixCompTests;

public class PixCompTests
{
    [RegFact]
    public void NativePacGoldensAndPngEntryRoundTripBitIdentically()
    {
        foreach (int idx in new[] { 7, 8, 11, 12 }) {
            string path = Path.Combine(TestPaths.GoldenDir!, $"pixcomp_golden.{idx:D2}.pac");
            byte[] golden = File.ReadAllBytes(path);

            Pixacomp pac = Pixacomp.Read(path);
            Assert.Equal(4, pac.Count);
            byte[] rewritten = pac.WriteMem();
            Assert.Equal(golden, rewritten);

            Pixacomp pacMem = Pixacomp.ReadMem(rewritten);
            Assert.Equal(golden, pacMem.WriteMem());
        }

            /* Entry 3 in pixcomp_golden.07.pac is weasel4.11c.png (IFF_PNG) */
        Pixacomp pac07 = Pixacomp.Read(Path.Combine(TestPaths.GoldenDir!, "pixcomp_golden.07.pac"));
        Pix weaselFromPac = pac07.GetPix(3);
        Pix weaselInput = TestPaths.ReadInput("weasel4.11c.png");
        Assert.True(Pix.Equal(weaselInput, weaselFromPac));

        Pixcomp c1 = Pixcomp.CreateFromPix(weaselInput, L.IFF_PNG);
        Pix p2 = Pix.CreateFromPixcomp(c1);
        Pixcomp c2 = Pixcomp.CreateFromPix(p2, L.IFF_PNG);
        Pix p3 = Pix.CreateFromPixcomp(c2);
        Pix golden03 = Pix.Read(Path.Combine(TestPaths.GoldenDir!, "pixcomp_golden.03.png"));
        Assert.True(Pix.Equal(golden03, p3));
    }

    [Fact]
    public void DetermineFormatMatchesUpstreamRules()
    {
        Pixcomp.DetermineFormat(L.IFF_DEFAULT, 1, 0, out int f);
        Assert.Equal(L.IFF_TIFF_G4, f);
        Pixcomp.DetermineFormat(L.IFF_DEFAULT, 16, 0, out f);
        Assert.Equal(L.IFF_PNG, f);
        Pixcomp.DetermineFormat(L.IFF_DEFAULT, 8, 0, out f);
        Assert.Equal(L.IFF_JFIF_JPEG, f);
        Pixcomp.DetermineFormat(L.IFF_DEFAULT, 8, 1, out f);
        Assert.Equal(L.IFF_PNG, f);
        Pixcomp.DetermineFormat(L.IFF_DEFAULT, 4, 0, out f);
        Assert.Equal(L.IFF_PNG, f);
        Pixcomp.DetermineFormat(L.IFF_JFIF_JPEG, 4, 0, out f);
        Assert.Equal(L.IFF_PNG, f);
        Pixcomp.DetermineFormat(L.IFF_TIFF_G4, 8, 0, out f);
        Assert.Equal(L.IFF_PNG, f);
        Pixcomp.DetermineFormat(L.IFF_PNG, 32, 0, out f);
        Assert.Equal(L.IFF_PNG, f);
    }

    [Fact]
    public void PngPixcompAndPixacompOperationsRoundTrip()
    {
        Pix p1 = Pix.Create(24, 16, 8);
        p1.SetResolution(300, 300);
        p1.Text = "hello";
        p1.SetPixel(5, 6, 123);

        Pix p2 = Pix.Create(18, 12, 32);
        p2.SetResolution(150, 150);
        Pix.ComposeRGBPixel(200, 100, 50, out uint rgb);
        p2.SetPixel(3, 4, rgb);

        Pixcomp c1 = Pixcomp.CreateFromPix(p1, L.IFF_PNG);
        c1.GetDimensions(out int cw, out int ch, out int cd);
        Assert.Equal((24, 16, 8), (cw, ch, cd));
        c1.GetParameters(out int xr, out int yr, out int ct, out int cmf);
        Assert.Equal((300, 300, L.IFF_PNG, 0), (xr, yr, ct, cmf));

        Pixcomp fromStr = Pixcomp.CreateFromString(c1.Data, L.L_COPY);
        Assert.Equal((24, 16, 8, L.IFF_PNG), (fromStr.Width, fromStr.Height, fromStr.Depth, fromStr.CompType));

        Pixacomp pac = Pixacomp.CreateWithInit(2, 10, p1, L.IFF_PNG);
        Assert.Equal(2, pac.Count);
        Assert.Equal(10, pac.Offset);
        pac.ReplacePix(11, p2, L.IFF_PNG);
        pac.AddBox(Box.Create(1, 2, 24, 16), L.L_INSERT);
        pac.AddBox(Box.Create(3, 4, 18, 12), L.L_INSERT);

        pac.GetPixDimensions(10, out int w10, out int h10, out int d10);
        Assert.Equal((24, 16, 8), (w10, h10, d10));
        Assert.True(Pix.Equal(p1, pac.GetPix(10)));
        Assert.True(Pix.Equal(p2, pac.GetPix(11)));
        Assert.Equal("hello", pac.GetPix(10).Text);

            /* Pixa <-> Pixacomp */
        Pixa pixa = Pixa.CreateFromPixacomp(pac, L.L_COPY);
        Assert.Equal(2, pixa.Count);
        Assert.Equal(10, pac.Offset); /* restored */
        Assert.True(Pix.Equal(p1, pixa.GetPix(0, L.L_CLONE)));
        Assert.True(Pix.Equal(p2, pixa.GetPix(1, L.L_CLONE)));

        Pixacomp pacFromPixa = Pixacomp.CreateFromPixa(pixa, L.IFF_PNG, L.L_COPY);
        Assert.Equal(2, pacFromPixa.Count);
        Assert.Equal(2, pacFromPixa.BoxaCount);

            /* Join and Interleave */
        Pixacomp joined = Pixacomp.Create(1);
        joined.Join(pacFromPixa, 0, -1);
        Assert.Equal(2, joined.Count);

        Pixacomp inter = Pixacomp.Interleave(pacFromPixa, joined);
        Assert.Equal(4, inter.Count);

            /* DisplayTiledAndScaled & WriteStreamInfo */
        Pix tiled = pacFromPixa.DisplayTiledAndScaled(32, 64, 2, 0, 4, 1);
        Assert.True(tiled.Width > 64 && tiled.Height > 0);

        using MemoryStream infoMs = new();
        pacFromPixa.WriteStreamInfo(infoMs, "unit");
        string infoText = Encoding.UTF8.GetString(infoMs.ToArray());
        Assert.Contains("Pixacomp Info for unit:", infoText);
        Assert.Contains("comptype = png (3)", infoText);
    }

    [Fact]
    public void CreateFromFileAndWriteFilesRoundTrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lept_pixcomp_unit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            Pix src = Pix.Create(12, 10, 4);
            PixColormap cmap = PixColormap.CreateLinear(4, 8);
            src.SetColormap(cmap);
            src.SetPixel(2, 3, 5);
            string pngPath = Path.Combine(dir, "a.png");
            Pix.Write(pngPath, src, L.IFF_PNG);

            Pixacomp pac = Pixacomp.CreateFromFiles(dir, ".png", L.IFF_DEFAULT);
            Assert.Equal(1, pac.Count);
            Assert.True(Pix.Equal(src, pac.GetPix(0)));

            string pacPath = Path.Combine(dir, "out.pac");
            pac.Write(pacPath);
            Pixa viaReadBoth = Pixa.ReadBoth(pacPath);
            Assert.Equal(1, viaReadBoth.Count);
            Assert.True(Pix.Equal(src, viaReadBoth.GetPix(0, L.L_CLONE)));

            using (new LeptDebugScope(true)) {
                pac.WriteFiles("lept/pixcomp_unit");
            }
        } finally {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
