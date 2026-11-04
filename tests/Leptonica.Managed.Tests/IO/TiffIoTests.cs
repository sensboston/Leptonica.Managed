using System;
using System.IO;
using System.Text;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

[Collection("LeptDebugOK")]
public class TiffIoTests : IDisposable
{
    private readonly string dir;

    public TiffIoTests()
    {
        dir = Path.Combine(Path.GetTempPath(), "lepsharp_tiff_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(L.IFF_TIFF)]
    [InlineData(L.IFF_TIFF_RLE)]
    [InlineData(L.IFF_TIFF_PACKBITS)]
    [InlineData(L.IFF_TIFF_G3)]
    [InlineData(L.IFF_TIFF_G4)]
    [InlineData(L.IFF_TIFF_LZW)]
    [InlineData(L.IFF_TIFF_ZIP)]
    public void Binary1Bpp_AllTiffCompressions_RoundTripAndDetectFormat(int comptype)
    {
        Pix src = Pix.Create(73, 51, 1);
        src.SetResolution(200, 200);
        for (int y = 0; y < 51; y++)
            for (int x = 0; x < 73; x++)
                if (((x * 3 + y * 5) % 7) < 2)
                    src.SetPixel(x, y, 1);

        string path = Path.Combine(dir, $"b1_{comptype}.tif");
        Pix.Write(path, src, comptype);

        using (FileStream fs = File.OpenRead(path)) {
            Assert.True(Pix.FileFormatIsTiff(fs));
            Pix.FindTiffCompression(fs, out int foundComp);
            Assert.Equal(comptype, foundComp);
            Pix.GetTiffResolution(fs, out int xr, out int yr);
            Assert.Equal((200, 200), (xr, yr));
        }

        Pix backFile = Pix.Read(path);
        Assert.True(Pix.Equal(src, backFile));

        byte[] mem = Pix.WriteMemTiff(src, comptype);
        Pix.ReadHeaderMemTiff(mem, 0, out int mw, out int mh, out int mbps, out int mspp, out _, out _, out int mfmt);
        Assert.Equal((73, 51, 1, 1, comptype), (mw, mh, mbps, mspp, mfmt));
        Pix backMem = Pix.ReadMemTiff(mem, 0)!;
        Assert.True(Pix.Equal(src, backMem));

        if (comptype == L.IFF_TIFF_G4) {
            byte[] g4 = Pix.ExtractG4DataFromFile(path, out int gw, out int gh, out int minisblack);
            Assert.NotEmpty(g4);
            Assert.Equal((73, 51, 0), (gw, gh, minisblack));
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void MultiDepth_LzwZipUncompressedAndRgba_RoundTrip(int depth)
    {
        Pix src = Pix.Create(44, 30, depth);
        if (depth == 32) {
            src.Spp = 4;
            for (int y = 0; y < 30; y++)
                for (int x = 0; x < 44; x++) {
                    Pix.ComposeRGBAPixel((x * 5) & 255, (y * 7) & 255, ((x + y) * 3) & 255, (x * 4 + 31) & 255, out uint px);
                    src.SetPixel(x, y, px);
                }
        } else {
            int mod = depth == 16 ? 65536 : (1 << depth);
            for (int y = 0; y < 30; y++)
                for (int x = 0; x < 44; x++)
                    src.SetPixel(x, y, (uint)((x * 257 + y * 513) % mod));
        }

        foreach (int fmt in new[] { L.IFF_TIFF, L.IFF_TIFF_LZW, L.IFF_TIFF_ZIP }) {
            byte[] mem = Pix.WriteMemTiff(src, fmt);
            Pix back = Pix.ReadMemTiff(mem, 0)!;
            Assert.True(Pix.Equal(src, back), $"Mismatch for depth={depth}, fmt={fmt}");
        }

        if (depth == 8 || depth == 32) {
            Pix rgbNoAlpha = depth == 32 ? Pix.Copy(null, src) : src;
            if (depth == 32) rgbNoAlpha.Spp = 3;
            byte[] tiffJpeg = Pix.WriteMemTiff(rgbNoAlpha, L.IFF_TIFF_JPEG);
            Pix.ReadHeaderMemTiff(tiffJpeg, 0, out int jw, out int jh, out _, out _, out _, out _, out int jfmt);
            Assert.Equal((44, 30, L.IFF_TIFF_JPEG), (jw, jh, jfmt));
            Pix backJpeg = Pix.ReadMemTiff(tiffJpeg, 0)!;
            Assert.Equal((44, 30, depth), (backJpeg.Width, backJpeg.Height, backJpeg.Depth));
        }
    }

    [Fact]
    public void MultipageTiff_FileMemoryOffsetCustomTagsAndPsPdfConversion()
    {
        Pixa srcPixa = Pixa.Create(4);
        for (int i = 0; i < 4; i++) {
            Pix p = Pix.Create(32 + i * 4, 24 + i * 2, i % 2 == 0 ? 1 : 8);
            p.SetPixel(2, 2, i % 2 == 0 ? 1u : (uint)(50 + i * 40));
            srcPixa.AddPix(p, L.L_INSERT);
        }

        string mpPath = Path.Combine(dir, "mp.tif");
        Pixa.WriteMultipageTiff(mpPath, srcPixa);

        using (FileStream fs = File.OpenRead(mpPath)) {
            Pix.TiffGetCount(fs, out int count);
            Assert.Equal(4, count);
        }

        Pixa readAll = Pixa.ReadMultipageTiff(mpPath);
        Assert.Equal(4, readAll.Count);
        for (int i = 0; i < 4; i++)
            Assert.True(Pix.Equal(srcPixa.GetPix(i, L.L_CLONE), readAll.GetPix(i, L.L_CLONE)));

        /* Offset-based multipage reading from file and memory */
        long offset = 0;
        int pageCount = 0;
        do {
            Pix? page = Pix.ReadFromMultipageTiff(mpPath, ref offset);
            Assert.NotNull(page);
            Assert.True(Pix.Equal(srcPixa.GetPix(pageCount, L.L_CLONE), page!));
            pageCount++;
        } while (offset != 0);
        Assert.Equal(4, pageCount);

        byte[] mpMem = Pixa.WriteMemMultipageTiff(srcPixa);
        Pixa readMemAll = Pixa.ReadMemMultipageTiff(mpMem);
        Assert.Equal(4, readMemAll.Count);

        offset = 0;
        pageCount = 0;
        do {
            Pix? page = Pix.ReadMemFromMultipageTiff(mpMem, ref offset);
            Assert.NotNull(page);
            Assert.True(Pix.Equal(srcPixa.GetPix(pageCount, L.L_CLONE), page!));
            pageCount++;
        } while (offset != 0);
        Assert.Equal(4, pageCount);

        /* Multipage 1bpp G4 -> PS and PDF */
        string mp1Path = Path.Combine(dir, "mp1.tif");
        for (int i = 0; i < 3; i++) {
            Pix p1 = Pix.Create(48, 36, 1);
            p1.SetPixel(4 + i, 4 + i, 1);
            Pix.WriteTiff(mp1Path, p1, L.IFF_TIFF_G4, i == 0 ? "w" : "a");
        }
        string psOut = Path.Combine(dir, "mp1.ps");
        PsIo.ConvertTiffMultipageToPS(mp1Path, psOut, 0.95f);
        Assert.True(new FileInfo(psOut).Length > 100);

        string pdfOut = Path.Combine(dir, "mp1.pdf");
        PdfIo.ConvertTiffMultipageToPdf(mp1Path, pdfOut);
        Assert.Equal(3, PdfIo.GetPageCount(pdfOut));

        /* Custom tags + FprintTiffInfo */
        Numa naflags = Numa.Create(3);
        Sarray savals = Sarray.Create(3);
        Sarray satypes = Sarray.Create(3);
        Numa nasizes = Numa.Create(3);
        const string xmp = "<xmp>Test</xmp>";
        naflags.AddNumber(700); savals.AddString(xmp, L.L_COPY); satypes.AddString("char*", L.L_COPY); nasizes.AddNumber(xmp.Length);
        naflags.AddNumber(269); savals.AddString("DocTitle", L.L_COPY); satypes.AddString("const char*", L.L_COPY);
        naflags.AddNumber(297); savals.AddString("2-10", L.L_COPY); satypes.AddString("l_uint16-l_uint16", L.L_COPY);
        Pix tagPix = Pix.Create(32, 32, 1);
        string tagPath = Path.Combine(dir, "tags.tif");
        Pix.WriteTiffCustom(tagPath, tagPix, L.IFF_TIFF_G4, "w", naflags, savals, satypes, nasizes);

        using var infoMs = new MemoryStream();
        Pix.FprintTiffInfo(infoMs, tagPath);
        string infoText = Encoding.UTF8.GetString(infoMs.ToArray());
        Assert.Contains("DocTitle", infoText);
    }

    [Fact]
    public void UpstreamProgSpecialTiffs_GrayAlphaRgb16AndBuffer()
    {
        if (!Directory.Exists(TestPaths.ProgDir))
            return;

        Pix grayAlpha = Pix.Read(TestPaths.ProgFile("gray-alpha.tif"));
        Assert.Equal((100, 100, 32, 4), (grayAlpha.Width, grayAlpha.Height, grayAlpha.Depth, grayAlpha.Spp));
        byte[] gaMem = Pix.WriteMemTiff(grayAlpha, L.IFF_TIFF_ZIP);
        Assert.True(Pix.Equal(grayAlpha, Pix.ReadMemTiff(gaMem, 0)!));

        Pix rgb16 = Pix.Read(TestPaths.ProgFile("rgb16.tif"));
        Assert.Equal((129, 90, 32, 3), (rgb16.Width, rgb16.Height, rgb16.Depth, rgb16.Spp));

        Pix bufTif = Pix.Read(TestPaths.ProgFile("testbuffer.tif"));
        Assert.Equal((659, 799, 32), (bufTif.Width, bufTif.Height, bufTif.Depth));
    }
}
