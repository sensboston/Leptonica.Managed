using System;
using System.Buffers.Binary;
using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

public class BmpIoTests
{
    private static Pix MakePattern(int w, int h, int d, int spp = 0)
    {
        Pix pix = Pix.Create(w, h, d);
        if (spp > 0)
            pix.Spp = spp;
        pix.XRes = 300;
        pix.YRes = 150;
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                uint val = d switch {
                    1 => (uint)((x + y) & 1),
                    2 => (uint)((x + 2 * y) & 3),
                    4 => (uint)((3 * x + 5 * y) & 0xf),
                    8 => (uint)((17 * x + 31 * y) & 0xff),
                    16 => (uint)((257 * x + 1021 * y) & 0xffff),
                    32 => MakeRgb(x, y, spp == 4),
                    _ => 0
                };
                pix.SetPixel(x, y, val);
            }
        }
        return pix;
    }

    private static uint MakeRgb(int x, int y, bool alpha)
    {
        int r = (37 * x + 13 * y) & 0xff;
        int g = (73 * x + 29 * y) & 0xff;
        int b = (19 * x + 97 * y) & 0xff;
        int a = alpha ? ((53 * x + 41 * y) & 0xff) : 255;
        Pix.ComposeRGBAPixel(r, g, b, a, out uint p);
        return p;
    }

    [Theory]
    [InlineData(1, 37, 9)]
    [InlineData(4, 15, 7)]
    [InlineData(8, 13, 6)]
    [InlineData(16, 11, 5)]
    [InlineData(32, 9, 5)]
    public void RoundTripMemAndStreamPreservesPixelsAndResolution(int d, int w, int h)
    {
        Pix src = MakePattern(w, h, d);
        byte[] bmp = Pix.WriteMemBmp(src);
        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);

        Pix dst = Pix.ReadMemBmp(bmp);
        Assert.Equal(L.IFF_BMP, dst.InputFormat);
        Assert.Equal(src.XRes, dst.XRes);
        Assert.Equal(src.YRes, dst.YRes);
        if (d == 1 || d == 16 || d == 32) {
            Assert.True(Pix.Equal(src, dst));
        } else {
            /* 4 and 8 bpp grayscale images acquire a linear grayscale colormap in BMP */
            Assert.NotNull(dst.Colormap);
            Pix dstNoCmap = dst.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
            Pix src8 = d == 4 ? src.Convert4To8(false) : src;
            Assert.True(Pix.Equal(src8, dstNoCmap));
        }

        using MemoryStream ms = new();
        Pix.WriteStreamBmp(ms, src);
        Pix fromStream = Pix.ReadStreamBmp(ms);
        Assert.True(Pix.Equal(dst, fromStream));
    }

    [Fact]
    public void Depth2ConvertsTo8BppOnWriteAnd2BppBmpCanStillBeRead()
    {
        Pix src2 = MakePattern(14, 5, 2);
        byte[] bmpFrom2 = Pix.WriteMemBmp(src2);
        Pix readBack = Pix.ReadMemBmp(bmpFrom2);
        Assert.Equal(8, readBack.Depth);
        Assert.True(Pix.Equal(src2.Convert2To8(0, 85, 170, 255, false),
                             readBack.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE)));

        /* Hand-crafted 2 bpp uncompressed BMP (valid on read per Note 2) */
        byte[] bmp2 = new byte[14 + 40 + 16 + 8];
        bmp2[0] = (byte)'B';
        bmp2[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp2.AsSpan(2, 4), bmp2.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bmp2.AsSpan(10, 4), 14 + 40 + 16);
        BinaryPrimitives.WriteInt32LittleEndian(bmp2.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bmp2.AsSpan(18, 4), 4);
        BinaryPrimitives.WriteInt32LittleEndian(bmp2.AsSpan(22, 4), 2);
        BinaryPrimitives.WriteInt16LittleEndian(bmp2.AsSpan(26, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bmp2.AsSpan(28, 2), 2);
        for (int i = 0; i < 4; i++) {
            bmp2[54 + 4 * i] = (byte)(i * 85);
            bmp2[54 + 4 * i + 1] = (byte)(i * 85);
            bmp2[54 + 4 * i + 2] = (byte)(i * 85);
            bmp2[54 + 4 * i + 3] = 255;
        }
        bmp2[70] = 0b11_10_01_00; /* bottom row: 3, 2, 1, 0 */
        bmp2[74] = 0b00_01_10_11; /* top row:    0, 1, 2, 3 */
        Pix parsed2 = Pix.ReadMemBmp(bmp2);
        Assert.Equal((4, 2, 2), (parsed2.Width, parsed2.Height, parsed2.Depth));
        parsed2.GetPixel(0, 0, out uint p00);
        parsed2.GetPixel(3, 0, out uint p30);
        parsed2.GetPixel(0, 1, out uint p01);
        Assert.Equal(0u, p00);
        Assert.Equal(3u, p30);
        Assert.Equal(3u, p01);
    }

    [Fact]
    public void Rgba32AndNegativeHeightTopDownBmpRoundTrip()
    {
        Pix rgba = MakePattern(7, 5, 32, spp: 4);
        byte[] bmp = Pix.WriteMemBmp(rgba);
        Pix dst = Pix.ReadMemBmp(bmp);
        Assert.Equal(4, dst.Spp);
        Assert.True(Pix.Equal(rgba, dst));
        for (int y = 0; y < 5; y++) {
            for (int x = 0; x < 7; x++) {
                rgba.GetPixel(x, y, out uint sv);
                dst.GetPixel(x, y, out uint dv);
                Assert.Equal(sv, dv);
            }
        }

        /* Flip BMP height sign to negative (top-down DIB): result is vertically flipped relative to bottom-up rows */
        byte[] topDown = (byte[])bmp.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(topDown.AsSpan(22, 4), -5);
        Pix flipped = Pix.ReadMemBmp(topDown);
        Assert.True(Pix.Equal(Pix.FlipTB(null, rgba), flipped));
    }

    [Fact]
    public void Colormapped1BppColorImageExpandsToRgbOnRead()
    {
        Pix pix1 = Pix.Create(8, 4, 1);
        PixColormap cmap = PixColormap.Create(1);
        cmap.AddColor(200, 10, 20);
        cmap.AddColor(10, 180, 240);
        pix1.SetColormap(cmap);
        pix1.SetPixel(0, 0, 1);
        pix1.SetPixel(1, 0, 0);

        byte[] bmp = Pix.WriteMemBmp(pix1);
        Pix readBack = Pix.ReadMemBmp(bmp);
        Assert.Equal(32, readBack.Depth);
        Assert.Null(readBack.Colormap);
        Assert.True(Pix.Equal(pix1.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC), readBack));
    }

    [Fact]
    public void InvalidHeadersAndArgumentsThrow()
    {
        Assert.Throws<ArgumentException>(() => Pix.ReadStreamBmp(null!));
        Assert.Throws<ArgumentException>(() => Pix.WriteMemBmp(null!));
        Assert.Throws<LeptonicaException>(() => Pix.ReadMemBmp(new byte[10]));

        byte[] badMagic = new byte[60];
        Assert.Throws<LeptonicaException>(() => Pix.ReadMemBmp(badMagic));

        Pix src = MakePattern(4, 4, 8);
        byte[] valid = Pix.WriteMemBmp(src);

        /* Compressed BMP rejected */
        byte[] compressed = (byte[])valid.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(compressed.AsSpan(30, 4), 1);
        Assert.Throws<LeptonicaException>(() => Pix.ReadMemBmp(compressed));

        /* Truncated payload rejected */
        Assert.Throws<LeptonicaException>(() => Pix.ReadMemBmp(valid.AsSpan(0, valid.Length - 1)));
    }
}
