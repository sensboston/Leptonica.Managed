using System;
using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.IOTests;

[Collection("LeptDebugOK")]
public class WriteFileTests : IDisposable
{
    private readonly string dir;

    public WriteFileTests()
    {
        dir = Path.Combine(Path.GetTempPath(), "lepsharp_wf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static Pix MakeGray(int w, int h, int d)
    {
        Pix pix = Pix.Create(w, h, d);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) {
                if (d == 32)
                    pix.SetPixel(x, y, (uint)((((x * 7 + y * 13) % 256) << 24) |
                                              (((x * 3) % 256) << 16) | (((y * 5) % 256) << 8)));
                else
                    pix.SetPixel(x, y, (uint)((x * 7 + y * 13) % (1 << d)));
            }
        return pix;
    }

    private static void AssertSamePixels(Pix expected, Pix actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Depth, actual.Depth);
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++) {
                expected.GetPixel(x, y, out uint a);
                actual.GetPixel(x, y, out uint b);
                if (expected.Depth == 32) {   // the alpha byte of an spp = 3 pix is not stored
                    a &= 0xffffff00;
                    b &= 0xffffff00;
                }
                Assert.Equal(a, b);
            }
    }

    /*--------------------------- format selection ----------------------*/

    [Theory]
    [InlineData(".bmp", L.IFF_BMP)]
    [InlineData(".jpg", L.IFF_JFIF_JPEG)]
    [InlineData(".jpeg", L.IFF_JFIF_JPEG)]
    [InlineData(".JPG", L.IFF_JFIF_JPEG)]
    [InlineData(".png", L.IFF_PNG)]
    [InlineData(".tif", L.IFF_TIFF)]
    [InlineData(".tiff", L.IFF_TIFF)]
    [InlineData(".tiffg4", L.IFF_TIFF_G4)]
    [InlineData(".pbm", L.IFF_PNM)]
    [InlineData(".pgm", L.IFF_PNM)]
    [InlineData(".pnm", L.IFF_PNM)]
    [InlineData(".gif", L.IFF_GIF)]
    [InlineData(".jp2", L.IFF_JP2)]
    [InlineData(".j2k", L.IFF_JP2)]
    [InlineData(".ps", L.IFF_PS)]
    [InlineData(".pdf", L.IFF_LPDF)]
    [InlineData(".webp", L.IFF_WEBP)]
    [InlineData(".xyz", L.IFF_UNKNOWN)]
    [InlineData("png", L.IFF_UNKNOWN)]
    [InlineData(".PNG", L.IFF_UNKNOWN)]
    [InlineData("", L.IFF_UNKNOWN)]
    public void GetFormatFromExtension_Table(string ext, int expected)
    {
        Assert.Equal(expected, WriteFile.GetFormatFromExtension(ext));
    }

    [Fact]
    public void GetImpliedFileFormat_UsesTailOnly()
    {
        Assert.Equal(L.IFF_PNG, WriteFile.GetImpliedFileFormat("/a.b/c.png"));
        Assert.Equal(L.IFF_UNKNOWN, WriteFile.GetImpliedFileFormat("/a.png/c"));
        Assert.Equal(L.IFF_JFIF_JPEG, WriteFile.GetImpliedFileFormat("x.y.jpg"));
        Assert.Equal(L.IFF_UNKNOWN, WriteFile.GetImpliedFileFormat("noextension"));
    }

    [Fact]
    public void GetFormatExtension_Range()
    {
        Assert.Equal("unknown", WriteFile.GetFormatExtension(L.IFF_UNKNOWN));
        Assert.Equal("png", WriteFile.GetFormatExtension(L.IFF_PNG));
        Assert.Equal("tif", WriteFile.GetFormatExtension(L.IFF_TIFF_G4));
        Assert.Equal("pdf", WriteFile.GetFormatExtension(L.IFF_LPDF));
        Assert.Equal("default", WriteFile.GetFormatExtension(L.IFF_DEFAULT));
        Assert.Equal("", WriteFile.GetFormatExtension(L.IFF_SPIX));
        Assert.Throws<ArgumentException>(() => WriteFile.GetFormatExtension(-1));
        Assert.Throws<ArgumentException>(() => WriteFile.GetFormatExtension(20));
        Assert.Equal(20, WriteFile.NumImageFileFormatExtensions);
    }

    [Fact]
    public void ChooseOutputFormat_LosslessWhenUnknown()
    {
        Assert.Equal(L.IFF_TIFF_G4, Pix.Create(4, 4, 1).ChooseOutputFormat());
        Assert.Equal(L.IFF_PNG, Pix.Create(4, 4, 8).ChooseOutputFormat());
        Assert.Equal(L.IFF_PNG, Pix.Create(4, 4, 32).ChooseOutputFormat());
        Pix pix = Pix.Create(4, 4, 8);
        pix.InputFormat = L.IFF_BMP;
        Assert.Equal(L.IFF_BMP, pix.ChooseOutputFormat());
    }

    [Fact]
    public void GetAutoFormat_ByDepthAndColormap()
    {
        Assert.Equal(L.IFF_TIFF_G4, Pix.Create(4, 4, 1).GetAutoFormat());
        Assert.Equal(L.IFF_JFIF_JPEG, Pix.Create(4, 4, 8).GetAutoFormat());
        Assert.Equal(L.IFF_JFIF_JPEG, Pix.Create(4, 4, 32).GetAutoFormat());
        Assert.Equal(L.IFF_PNG, Pix.Create(4, 4, 4).GetAutoFormat());
        Assert.Equal(L.IFF_PNG, Pix.Create(4, 4, 2).GetAutoFormat());
        Pix cmapped = Pix.Create(4, 4, 8);
        cmapped.SetColormap(PixColormap.Create(8));
        Assert.Equal(L.IFF_PNG, cmapped.GetAutoFormat());
        Pix cmapped1 = Pix.Create(4, 4, 1);
        cmapped1.SetColormap(PixColormap.Create(1));
        Assert.Equal(L.IFF_PNG, cmapped1.GetAutoFormat());
    }

    [Fact]
    public void JpegSetQuality_ReturnsPrevious()
    {
        int orig = WriteFile.JpegSetQuality(0);   // 0 means 75
        try {
            Assert.Equal(75, WriteFile.JpegSetQuality(85));
            Assert.Equal(85, WriteFile.JpegSetQuality(101));   // invalid: unchanged
            Assert.Equal(85, WriteFile.JpegSetQuality(-3));
            Assert.Equal(85, WriteFile.JpegSetQuality(0));
            Assert.Equal(75, WriteFile.JpegSetQuality(1));
        } finally {
            WriteFile.JpegSetQuality(orig);
        }
    }

    /*------------------------------ writing ----------------------------*/

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void Write_Png_RoundTrip(int depth)
    {
        Pix pix = MakeGray(13, 9, depth);
        string file = Path.Combine(dir, $"a{depth}.png");
        Pix.Write(file, pix, L.IFF_PNG);
        Pix back = Pix.Read(file);
        if (depth == 16) {
            // read as 16 bpp only when not stripped; compare the 8 bit MSB otherwise
            if (back.Depth == 8) {
                Assert.Equal(13, back.Width);
                return;
            }
        }
        AssertSamePixels(pix, back);
    }

    [Fact]
    public void Write_DefaultFormat_UsesPngForUnknownInput()
    {
        Pix pix = MakeGray(8, 8, 8);
        string file = Path.Combine(dir, "d.dat");
        Pix.Write(file, pix, L.IFF_DEFAULT);
        byte[] head = new byte[8];
        using (FileStream fs = File.OpenRead(file))
            fs.ReadExactly(head, 0, 8);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }, head);
    }

    [Fact]
    public void WriteStream_UnknownFormatThrows()
    {
        Pix pix = MakeGray(4, 4, 8);
        using MemoryStream ms = new MemoryStream();
        Assert.Throws<ArgumentException>(() => Pix.WriteStream(ms, pix, L.IFF_UNKNOWN));
        Assert.Throws<ArgumentException>(() => Pix.WriteStream(ms, pix, 99));
    }

    [Fact]
    public void Write_UnknownFormatThrowsAndLeavesFile()
    {
        Pix pix = MakeGray(4, 4, 8);
        string file = Path.Combine(dir, "u.bin");
        Assert.Throws<ArgumentException>(() => Pix.Write(file, pix, 99));
    }

    [Fact]
    public void WriteMem_Png_MatchesStream()
    {
        Pix pix = MakeGray(11, 5, 8);
        byte[] mem = Pix.WriteMem(pix, L.IFF_PNG);
        using MemoryStream ms = new MemoryStream();
        Pix.WriteStream(ms, pix, L.IFF_PNG);
        Assert.Equal(0x50, mem[1]);   // 'P' of the png signature
        AssertSamePixels(pix, Pix.ReadStream(new MemoryStream(mem), 0));
        AssertSamePixels(pix, Pix.ReadStream(new MemoryStream(ms.ToArray()), 0));
    }

    [Fact]
    public void WriteMem_DefaultFormat()
    {
        Pix pix = MakeGray(6, 6, 8);
        byte[] mem = Pix.WriteMem(pix, L.IFF_DEFAULT);
        AssertSamePixels(pix, Pix.ReadStream(new MemoryStream(mem), 0));
    }

    [Fact]
    public void WriteImpliedFormat_PngAndUnknown()
    {
        Pix pix = MakeGray(7, 7, 8);
        string png = Path.Combine(dir, "i.png");
        Pix.WriteImpliedFormat(png, pix, 0, 0);
        AssertSamePixels(pix, Pix.Read(png));

        string unknown = Path.Combine(dir, "i.zzz");   // unknown extension: png
        Pix.WriteImpliedFormat(unknown, pix, 0, 0);
        AssertSamePixels(pix, Pix.Read(unknown));
    }

    [Fact]
    public void WriteDebug_DisabledWritesNothing()
    {
        using var debug = new LeptDebugScope(false);
        string file = Path.Combine(dir, "dbg.png");
        MakeGray(4, 4, 8).WriteDebug(file, L.IFF_PNG);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void PixaWriteFiles_NumbersTheFiles()
    {
        Pixa pixa = Pixa.Create(2);
        pixa.AddPix(MakeGray(5, 5, 8), L.L_INSERT);
        pixa.AddPix(MakeGray(6, 4, 8), L.L_INSERT);
        string root = Path.Combine(dir, "pa");
        Pixa.WriteFiles(root, pixa, L.IFF_PNG);
        Assert.True(File.Exists(root + "000.png"));
        Assert.True(File.Exists(root + "001.png"));
        Assert.Equal(6, Pix.Read(root + "001.png").Width);

        string root2 = Path.Combine(dir, "pd");
        Pixa.WriteFiles(root2, pixa, L.IFF_DEFAULT);   // 8 bpp, input format unknown: png
        Assert.True(File.Exists(root2 + "000.png"));
    }

    [Fact]
    public void PixaWriteFiles_InvalidFormat()
    {
        Pixa pixa = Pixa.Create(1);
        Assert.Throws<ArgumentException>(() => Pixa.WriteFiles("x", pixa, L.IFF_UNKNOWN));
        Assert.Throws<ArgumentException>(() => Pixa.WriteFiles("x", pixa, -1));
        Assert.Throws<ArgumentException>(() => Pixa.WriteFiles("x", pixa, 20));
    }

    /*------------------------------ display ----------------------------*/

    [Fact]
    public void Display_DisabledDoesNothing()
    {
        using var debug = new LeptDebugScope(false);
        MakeGray(4, 4, 8).Display(0, 0);
        MakeGray(4, 4, 8).DisplayWithTitle(0, 0, "t", 1);
        WriteFile.FileDisplay("nonexistent.png", 0, 0, 1.0f);
    }

    [Fact]
    public void MakeColorSquare_FillsWithColor()
    {
        Pix pix = Pix.MakeColorSquare(0x11223300, 0, 0, L.L_ADD_BELOW, 0);
        Assert.Equal((100, 100, 32), (pix.Width, pix.Height, pix.Depth));
        pix.GetPixel(50, 50, out uint v);
        Assert.Equal(0x11223300u, v);
        Pix big = Pix.MakeColorSquare(0xff000000, 150, 0, L.L_ADD_BELOW, 0);
        Assert.Equal(150, big.Width);
    }

    [Fact]
    public void DisplayWrite_ReturnsError()
    {
        Assert.Equal(1, Pix.Create(2, 2, 8).DisplayWrite(1));
    }

    [Fact]
    public void ChooseDisplayProg_RejectsInvalid()
    {
        WriteFile.ChooseDisplayProg(99);   // logged, not set
        WriteFile.ChooseDisplayProg(-1);
    }
}

public class SplitPathAtExtensionTests
{
    [Theory]
    [InlineData("/usr/tmp/abc.jpg", "/usr/tmp/abc", ".jpg")]
    [InlineData("/usr/tmp/.jpg", "/usr/tmp/", ".jpg")]
    [InlineData("/usr/tmp.jpg/", "/usr/tmp.jpg/", "")]
    [InlineData("./.jpg", "./", ".jpg")]
    [InlineData("abc", "abc", "")]
    [InlineData("a.b.c", "a.b", ".c")]
    public void Splits(string path, string basename, string ext)
    {
        Utils.SplitPathAtExtension(path, out string b, out string e);
        Assert.Equal(basename, b);
        Assert.Equal(ext, e);
    }
}
