using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Leptonica.Internal.Png;

namespace Leptonica.Managed.Tests.Png;

/// <summary>
/// Errors, warnings and chunk handling of the decoder. The expected behavior is that of
/// libpng 1.6 (checked by feeding the same files, see <c>PngBuilder.Dump</c>).
/// </summary>
public class PngDecoderBehaviorTests
{
    private static PngBuilder Rgb() => PngBuilder.FromSuite("basn2c08");       // IHDR gAMA IDAT IEND
    private static PngBuilder Gray() => PngBuilder.FromSuite("basn0g08");      // IHDR gAMA IDAT IEND
    private static PngBuilder Pal() => PngBuilder.FromSuite("basn3p04");       // IHDR gAMA sBIT PLTE IDAT IEND
    private static PngBuilder GrayAlpha() => PngBuilder.FromSuite("basn4a08");

    private static byte[] Ihdr(uint w, uint h, int depth, int color, int comp = 0, int filter = 0, int interlace = 0) =>
        [.. PngBuilder.BE32(w), .. PngBuilder.BE32(h), (byte)depth, (byte)color, (byte)comp, (byte)filter, (byte)interlace];

    /// <summary>A complete zlib stream of no data (the framework writes nothing for that).</summary>
    private static readonly byte[] EmptyZlib = [0x78, 0x9c, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];

    private static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);

    private static byte[] Text(string key, string value) => [.. Latin1(key), 0, .. Latin1(value)];

    /*-----------------------------------------------------------------*
     *                  signature, truncation, lengths                 *
     *-----------------------------------------------------------------*/
    [Fact]
    public void BadSignatureIsAnError()
    {
        byte[] good = Rgb().Build();
        Assert.NotNull(PngDecoder.Read(new MemoryStream(good), false));

        for (int i = 0; i < 8; i++)
        {
            byte[] bad = (byte[])good.Clone();
            bad[i] ^= 0x40;
            Assert.Throws<PngException>(() => PngDecoder.Read(new MemoryStream(bad), false));
            Assert.Throws<PngException>(() => PngDecoder.ReadInfo(new MemoryStream(bad)));
        }
    }

    [Fact]
    public void AsciiDamagedSignatureIsAnError()
    {
        byte[] good = Rgb().Build();
        // CR LF turned into LF, as by a text mode transfer.
        byte[] lf = [.. good.Take(4), 0x0a, .. good.Skip(6)];
        Assert.Throws<PngException>(() => PngDecoder.Read(new MemoryStream(lf), false));
        // LF turned into CR LF.
        byte[] crlf = [.. good.Take(7), 0x0d, 0x0a, .. good.Skip(8)];
        Assert.Throws<PngException>(() => PngDecoder.Read(new MemoryStream(crlf), false));
    }

    [Theory]
    [InlineData("basn2c08")]
    [InlineData("basi2c08")]
    [InlineData("basn3p04")]
    public void EveryTruncationIsAnError(string name)
    {
        byte[] png = PngBuilder.FromSuite(name).Build();
        Assert.NotNull(PngDecoder.Read(new MemoryStream(png), false));
        for (int len = 0; len < png.Length; len++)
        {
            var part = new MemoryStream(png, 0, len);
            Assert.Throws<PngException>(() => PngDecoder.Read(part, false));
        }
    }

    [Fact]
    public void MissingIendIsAnError()
    {
        Rgb().Remove("IEND").AssertReadFails();
    }

    [Fact]
    public void DataAfterIendIsIgnored()
    {
        byte[] png = [.. Rgb().Build(), .. Latin1("trailing garbage")];
        PngImage img = PngDecoder.Read(new MemoryStream(png), false);
        Assert.Equal(32, img.Info.Width);
    }

    [Fact]
    public void ChunkLengthAbove31BitsIsAnError()
    {
        byte[] png = Rgb().Build();
        // The length of the gAMA chunk follows the 8 + 25 bytes of signature and IHDR.
        Array.Copy(PngBuilder.BE32(0x80000000), 0, png, 33, 4);
        Assert.Throws<PngException>(() => PngDecoder.Read(new MemoryStream(png), false));
        Array.Copy(PngBuilder.BE32(0xffffffff), 0, png, 33, 4);
        Assert.Throws<PngException>(() => PngDecoder.Read(new MemoryStream(png), false));
    }

    [Fact]
    public void ChunkLengthPastTheEndOfDataIsAnError()
    {
        byte[] png = Rgb().Build();
        Array.Copy(PngBuilder.BE32(100000), 0, png, 33, 4);
        Assert.Throws<PngException>(() => PngDecoder.Read(new MemoryStream(png), false));
        Array.Copy(PngBuilder.BE32(0x7fffffff), 0, png, 33, 4);
        Assert.Throws<PngException>(() => PngDecoder.Read(new MemoryStream(png), false));
    }

    [Fact]
    public void InvalidChunkTypeIsAnError()
    {
        Rgb().InsertBeforeIdat("ab1d", 1).AssertReadFails();
        Rgb().InsertBeforeIdat("aécd", 1).AssertReadFails();
    }

    /*-----------------------------------------------------------------*
     *                              CRC                                *
     *-----------------------------------------------------------------*/
    [Theory]
    [InlineData("IHDR")]
    [InlineData("PLTE")]
    [InlineData("IDAT")]
    [InlineData("IEND")]
    public void CrcErrorInCriticalChunkIsAnError(string name)
    {
        PngBuilder b = Pal();
        b[name].BadCrc = true;
        b.AssertReadFails();
    }

    [Fact]
    public void CrcErrorInIdatIsNotSeenByReadInfo()
    {
        PngBuilder b = Rgb();
        b["IDAT"].BadCrc = true;
        Assert.Equal(32, b.ReadInfo().Width);
        b["IHDR"].BadCrc = true;
        Assert.Throws<PngException>(() => b.ReadInfo());
    }

    [Fact]
    public void CrcErrorInSecondIdatIsAnError()
    {
        PngBuilder b = Rgb();
        byte[] data = b.IdatData();
        b.SetIdat(data[..20], data[20..]);
        b.Chunks[b.Chunks.FindLastIndex(c => c.Name == "IDAT")].BadCrc = true;
        b.AssertReadFails();
    }

    [Fact]
    public void CrcErrorInAncillaryChunksDropsTheChunk()
    {
        PngBuilder b = Rgb();
        b.InsertBeforeIdat("tEXt", Text("Title", "x"));
        b.InsertBeforeIdat("pHYs", [.. PngBuilder.BE32(2835), .. PngBuilder.BE32(2835), 1]);
        b.InsertBeforeIdat("tIME", 1, 2, 3, 4, 5, 6, 7);
        PngImage ok = b.Read();
        Assert.Single(ok.Info.Texts);
        Assert.True(ok.Info.HasPhys);
        Assert.NotNull(ok.Info.Gamma);

        foreach (string name in new[] { "tEXt", "pHYs", "gAMA", "tIME" })
            b[name].BadCrc = true;
        PngImage img = b.Read();
        Assert.Empty(img.Info.Texts);
        Assert.False(img.Info.HasPhys);
        Assert.Null(img.Info.Gamma);
        Assert.Equal(ok.Pixels, img.Pixels);
    }

    [Fact]
    public void CrcErrorInTrnsDropsTheChunk()
    {
        PngBuilder b = PngBuilder.FromSuite("tbrn2c08");
        Assert.True(b.Read().Info.HasTrns);
        b["tRNS"].BadCrc = true;
        Assert.False(b.Read().Info.HasTrns);
    }

    /*-----------------------------------------------------------------*
     *                              IHDR                               *
     *-----------------------------------------------------------------*/
    public static IEnumerable<object[]> InvalidHeaders()
    {
        yield return new object[] { Ihdr(0, 32, 8, 2) };                  // zero width
        yield return new object[] { Ihdr(32, 0, 8, 2) };                  // zero height
        yield return new object[] { Ihdr(0x80000000, 32, 8, 2) };         // 32 bit width
        yield return new object[] { Ihdr(1000001, 1, 8, 0) };             // beyond the user limit
        yield return new object[] { Ihdr(1, 1000001, 8, 0) };
        yield return new object[] { Ihdr(32, 32, 8, 1) };                 // color type 1
        yield return new object[] { Ihdr(32, 32, 8, 5) };
        yield return new object[] { Ihdr(32, 32, 8, 7) };
        yield return new object[] { Ihdr(32, 32, 8, 9) };
        yield return new object[] { Ihdr(32, 32, 0, 2) };                 // bit depth 0
        yield return new object[] { Ihdr(32, 32, 3, 0) };
        yield return new object[] { Ihdr(32, 32, 99, 2) };
        yield return new object[] { Ihdr(32, 32, 4, 2) };                 // rgb needs 8 or 16
        yield return new object[] { Ihdr(32, 32, 1, 6) };
        yield return new object[] { Ihdr(32, 32, 2, 4) };
        yield return new object[] { Ihdr(32, 32, 16, 3) };                // palette up to 8
        yield return new object[] { Ihdr(32, 32, 8, 2, comp: 1) };
        yield return new object[] { Ihdr(32, 32, 8, 2, filter: 1) };
        yield return new object[] { Ihdr(32, 32, 8, 2, interlace: 2) };
        yield return new object[] { Ihdr(32, 32, 8, 2)[..12] };           // wrong length
        yield return new object[] { new byte[14] };
    }

    [Theory]
    [MemberData(nameof(InvalidHeaders))]
    public void InvalidHeaderIsAnError(byte[] ihdr)
    {
        PngBuilder b = Rgb();
        b["IHDR"].Body = ihdr;
        b.AssertReadFails();
        Assert.Throws<PngException>(() => b.ReadInfo());
    }

    [Fact]
    public void DuplicateIhdrIsAnError()
    {
        PngBuilder b = Rgb();
        b.Insert(1, "IHDR", b["IHDR"].Body);
        b.AssertReadFails();
    }

    /*-----------------------------------------------------------------*
     *                      chunk order and presence                   *
     *-----------------------------------------------------------------*/
    [Fact]
    public void IendBeforeIdatIsAnError()
    {
        PngBuilder b = Rgb();
        b.Chunks.Insert(b.IndexOf("IDAT"), new PngBuilder.Chunk("IEND", []));
        b.AssertReadFails();
        Assert.Throws<PngException>(() => b.ReadInfo());
    }

    [Fact]
    public void MissingIdatIsAnError()
    {
        Rgb().Remove("IDAT").AssertReadFails();
    }

    [Fact]
    public void IdatBeforeIhdrIsAnError()
    {
        PngBuilder b = Rgb();
        b.Insert(0, "IDAT", b["IDAT"].Body);
        b.AssertReadFails();
    }

    [Fact]
    public void FirstChunkMustBeIhdrExceptForUnknownAncillaryChunks()
    {
        foreach (string name in new[] { "gAMA", "tEXt", "pHYs", "sRGB", "tIME", "PLTE", "IEND", "tRNS", "iCCP", "cHRM" })
            Rgb().Insert(0, name, 0).AssertReadFails();

        PngBuilder ok = Rgb().Insert(0, "prVt", 1, 2, 3, 4).Insert(0, "zzzz");
        Assert.Equal(32, ok.Read().Info.Width);
        Assert.Equal(32, ok.ReadInfo().Width);
        // Unknown critical chunks are errors wherever they are.
        Rgb().Insert(0, "PrVt", 1).AssertReadFails();
    }

    [Fact]
    public void UnknownCriticalChunkIsAnError()
    {
        Rgb().InsertBeforeIdat("ABCD", 1, 2).AssertReadFails();
        Rgb().InsertAfterIdat("ABCD", 1, 2).AssertReadFails();
    }

    [Fact]
    public void UnknownAncillaryChunksAreSkipped()
    {
        PngBuilder b = Rgb();
        b.InsertBeforeIdat("abCD", 1, 2, 3);
        b.InsertAfterIdat("xyZw");
        PngImage img = b.Read();
        Assert.True(img.Pixels.SequenceEqual(Rgb().Read().Pixels));
    }

    [Fact]
    public void PaletteImageNeedsPlteBeforeIdat()
    {
        Pal().Remove("PLTE").AssertReadFails();
        Assert.Throws<PngException>(() => Pal().Remove("PLTE").ReadInfo());
    }

    [Fact]
    public void DuplicatePlteIsAnError()
    {
        PngBuilder b = Pal();
        b.Insert(b.IndexOf("PLTE"), "PLTE", b["PLTE"].Body);
        b.AssertReadFails();
        // Also when the second one comes after IDAT.
        PngBuilder c = Pal();
        c.InsertAfterIdat("PLTE", c["PLTE"].Body);
        c.AssertReadFails();
        // Also for color images, whose PLTE is only a suggestion.
        PngBuilder d = Rgb();
        d.InsertBeforeIdat("PLTE", 1, 2, 3).InsertBeforeIdat("PLTE", 1, 2, 3).AssertReadFails();
    }

    [Fact]
    public void PlteOfDifferentKindsOfImages()
    {
        // A suggested palette in a color image is read.
        PngInfo rgb = Rgb().InsertBeforeIdat("PLTE", 1, 2, 3, 4, 5, 6).Read().Info;
        Assert.Equal(new[] { new PngColor(1, 2, 3), new PngColor(4, 5, 6) }, rgb.Palette);

        // After IDAT it is out of place and ignored.
        Assert.Null(Rgb().InsertAfterIdat("PLTE", 1, 2, 3).Read().Info.Palette);

        // Gray images ignore it.
        Assert.Null(Gray().InsertBeforeIdat("PLTE", 1, 2, 3).Read().Info.Palette);

        // Invalid length: ignored in color images, an error in palette images.
        Assert.Null(Rgb().InsertBeforeIdat("PLTE", 1, 2, 3, 4).Read().Info.Palette);
        Pal().Remove("PLTE").InsertBeforeIdat("PLTE", 1, 2, 3, 4).AssertReadFails();
        Pal().Remove("PLTE").InsertBeforeIdat("PLTE", new byte[3 * 257]).AssertReadFails();

        // Fewer entries than the bit depth allows, and more: extra entries are dropped.
        PngInfo few = Pal().Remove("PLTE").InsertBeforeIdat("PLTE", 9, 8, 7, 6, 5, 4).Read().Info;
        Assert.Equal(new[] { new PngColor(9, 8, 7), new PngColor(6, 5, 4) }, few.Palette);
        PngInfo many = Pal().Remove("PLTE").InsertBeforeIdat("PLTE", Enumerable.Range(0, 3 * 20).Select(i => (byte)i).ToArray()).Read().Info;
        Assert.Equal(16, many.Palette!.Length);
        Assert.Equal(new PngColor(45, 46, 47), many.Palette[15]);
    }

    /*-----------------------------------------------------------------*
     *                          image data                             *
     *-----------------------------------------------------------------*/
    [Fact]
    public void SplitIdatChunksGiveTheSameImage()
    {
        PngImage expected = Rgb().Read();
        byte[] data = Rgb().IdatData();

        PngBuilder two = Rgb();
        two.SetIdat(data[..10], data[10..]);
        Assert.True(expected.Pixels.SequenceEqual(two.Read().Pixels));

        PngBuilder many = Rgb();
        many.SetIdat(data.Select(x => new[] { x }).ToArray());
        Assert.True(expected.Pixels.SequenceEqual(many.Read().Pixels));

        PngBuilder withEmpty = Rgb();
        withEmpty.SetIdat(data[..5], [], data[5..40], [], [], data[40..]);
        Assert.True(expected.Pixels.SequenceEqual(withEmpty.Read().Pixels));
    }

    [Fact]
    public void ChunkBetweenIdatChunksBeforeTheImageIsCompleteIsAnError()
    {
        PngBuilder b = Rgb();
        byte[] data = b.IdatData();
        b.SetIdat(data[..30], data[30..]);
        b.Insert(b.IndexOf("IDAT") + 1, "tEXt", Text("a", "b"));
        b.AssertReadFails();
    }

    [Fact]
    public void ExtraIdatChunksAfterTheImageAreIgnored()
    {
        PngImage expected = Rgb().Read();
        PngBuilder b = Rgb();
        b.InsertAfterIdat("tEXt", Text("a", "b"));
        b.InsertAfterIdat("IDAT", 1, 2, 3);
        PngImage img = b.Read();
        Assert.True(expected.Pixels.SequenceEqual(img.Pixels));
        Assert.Single(img.Info.Texts);

        PngBuilder c = Rgb();
        c.Chunks.Insert(c.IndexOf("IDAT") + 1, new PngBuilder.Chunk("IDAT", [1, 2, 3]));
        Assert.True(expected.Pixels.SequenceEqual(c.Read().Pixels));
    }

    [Fact]
    public void ExtraBytesAfterTheZlibStreamAreIgnored()
    {
        PngImage expected = Rgb().Read();
        PngBuilder b = Rgb();
        b.SetIdat([.. b.IdatData(), 1, 2, 3, 4, 5]);
        Assert.True(expected.Pixels.SequenceEqual(b.Read().Pixels));
    }

    [Fact]
    public void ZlibErrorsAreErrors()
    {
        byte[] raw = PngBuilder.Unzlib(Rgb().IdatData());

        // Truncated: the end of the stream (Adler-32 and more) is missing.
        byte[] z = Rgb().IdatData();
        foreach (int cut in new[] { 1, 2, 4, 5, 10, z.Length / 2, z.Length - 3 })
            Rgb().SetIdat(z[..^cut]).AssertReadFails();

        // Bad Adler-32.
        byte[] adler = (byte[])z.Clone();
        adler[^1] ^= 0xff;
        Rgb().SetIdat(adler).AssertReadFails();

        // Bad zlib header.
        byte[] header = (byte[])z.Clone();
        header[0] = 0x00;
        Rgb().SetIdat(header).AssertReadFails();

        // Corrupt deflate data.
        byte[] data = (byte[])z.Clone();
        data[z.Length / 2] ^= 0xff;
        Rgb().SetIdat(data).AssertReadFails();

        // Valid zlib data, but one row short.
        int rb = 1 + 32 * 3;
        Rgb().SetIdat(PngBuilder.Zlib(raw[..^rb])).AssertReadFails();
        Rgb().SetIdat(PngBuilder.Zlib(raw[..^1])).AssertReadFails();
        Rgb().SetIdat(EmptyZlib).AssertReadFails();
        Rgb().SetIdat().AssertReadFails();

        // Complete data is accepted, extra rows are ignored.
        Assert.NotNull(Rgb().SetIdat(PngBuilder.Zlib(raw)).Read());
        Assert.NotNull(Rgb().SetIdat(PngBuilder.Zlib([.. raw, .. raw])).Read());
    }

    [Fact]
    public void InvalidFilterTypeIsAnError()
    {
        byte[] raw = PngBuilder.Unzlib(Rgb().IdatData());
        int rb = 1 + 32 * 3;
        foreach (byte f in new byte[] { 5, 9, 255 })
        {
            byte[] bad = (byte[])raw.Clone();
            bad[7 * rb] = f;
            Rgb().SetIdat(PngBuilder.Zlib(bad)).AssertReadFails();
        }
    }

    [Fact]
    public void InterlacedImageWithMissingPassIsAnError()
    {
        PngBuilder b = PngBuilder.FromSuite("basi2c08");
        byte[] raw = PngBuilder.Unzlib(b.IdatData());
        b.SetIdat(PngBuilder.Zlib(raw[..^10]));
        b.AssertReadFails();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(5, 1)]
    [InlineData(1, 6)]
    [InlineData(9, 9)]
    public void InterlacedImagesWithEmptyPassesDecode(int w, int h)
    {
        // Random rgb pixels, interlaced here by hand: sizes for which some Adam7 passes are empty.
        var rng = new Random(w * 10 + h);
        var rows = new byte[w * h * 3];
        rng.NextBytes(rows);
        PngBuilder b = Rgb();
        b["IHDR"].Body = Ihdr((uint)w, (uint)h, 8, 2, interlace: 1);

        int[] xoff = [0, 4, 0, 2, 0, 1, 0], yoff = [0, 0, 4, 0, 2, 0, 1];
        int[] xinc = [8, 8, 4, 4, 2, 2, 1], yinc = [8, 8, 8, 4, 4, 2, 2];
        var raw = new List<byte>();
        for (int pass = 0; pass < 7; pass++)
            for (int y = yoff[pass]; y < h; y += yinc[pass])
            {
                if (xoff[pass] >= w) continue;
                raw.Add(0);
                for (int x = xoff[pass]; x < w; x += xinc[pass])
                    raw.AddRange(rows.AsSpan((y * w + x) * 3, 3).ToArray());
            }
        b.SetIdat(PngBuilder.Zlib(raw.ToArray()));
        PngImage img = b.Read();
        Assert.Equal(1, img.Info.Interlace);
        Assert.True(rows.AsSpan().SequenceEqual(img.Pixels));
    }

    /*-----------------------------------------------------------------*
     *                    transparency and resolution                  *
     *-----------------------------------------------------------------*/
    [Fact]
    public void TrnsOfGrayAndRgbImages()
    {
        PngInfo gray = Gray().InsertBeforeIdat("tRNS", 0, 77).Read().Info;
        Assert.True(gray.HasTrns);
        Assert.Equal(77, gray.TransGray);
        Assert.Empty(gray.Trans);

        PngInfo rgb = Rgb().InsertBeforeIdat("tRNS", 0, 1, 0, 2, 1, 0).Read().Info;
        Assert.True(rgb.HasTrns);
        Assert.Equal((0, 1, 2, 256), ((int)rgb.TransGray, (int)rgb.TransRed, (int)rgb.TransGreen, (int)rgb.TransBlue));

        // Wrong lengths are ignored.
        Assert.False(Gray().InsertBeforeIdat("tRNS", 0, 77, 1).Read().Info.HasTrns);
        Assert.False(Gray().InsertBeforeIdat("tRNS", 5).Read().Info.HasTrns);
        Assert.False(Rgb().InsertBeforeIdat("tRNS", 0, 1, 0, 2).Read().Info.HasTrns);
        Assert.False(Gray().InsertBeforeIdat("tRNS").Read().Info.HasTrns);

        // Out-of-range samples are only a warning.
        Assert.True(Gray().InsertBeforeIdat("tRNS", 0xff, 0xff).Read().Info.HasTrns);
    }

    [Fact]
    public void TrnsOfPaletteImages()
    {
        PngInfo pal = Pal().InsertBeforeIdat("tRNS", 255, 128, 0).Read().Info;
        Assert.True(pal.HasTrns);
        Assert.Equal(new byte[] { 255, 128, 0 }, pal.Trans);

        // More entries than the palette has (15 here), none, or before PLTE: ignored.
        Assert.False(Pal().InsertBeforeIdat("tRNS", new byte[16]).Read().Info.HasTrns);
        Assert.False(Pal().InsertBeforeIdat("tRNS").Read().Info.HasTrns);
        PngBuilder early = Pal();
        early.Insert(early.IndexOf("PLTE"), "tRNS", 1, 2);
        Assert.False(early.Read().Info.HasTrns);
        Assert.Equal(15, early.Read().Info.Palette!.Length);

        // Exactly as many as the palette has.
        Assert.Equal(15, Pal().InsertBeforeIdat("tRNS", new byte[15]).Read().Info.Trans.Length);
    }

    [Fact]
    public void TrnsInImagesWithAlphaIsIgnored()
    {
        Assert.False(GrayAlpha().InsertBeforeIdat("tRNS", 0, 1).Read().Info.HasTrns);
    }

    [Fact]
    public void TrnsAfterIdatAndDuplicatesAreIgnored()
    {
        Assert.False(Gray().InsertAfterIdat("tRNS", 0, 77).Read().Info.HasTrns);

        PngBuilder b = Gray();
        b.InsertBeforeIdat("tRNS", 0, 5).InsertBeforeIdat("tRNS", 0, 9);
        Assert.Equal(5, b.Read().Info.TransGray);
    }

    [Fact]
    public void PhysChunk()
    {
        byte[] Phys(uint x, uint y, byte unit) => [.. PngBuilder.BE32(x), .. PngBuilder.BE32(y), unit];

        PngInfo m = Gray().InsertBeforeIdat("pHYs", Phys(2835, 3937, 1)).Read().Info;
        Assert.True(m.HasPhys);
        Assert.Equal((2835u, 3937u, 1), (m.PhysX, m.PhysY, m.PhysUnit));
        Assert.Equal((2835u, 3937u), (m.XPixelsPerMeter, m.YPixelsPerMeter));

        // Unknown unit: stored, but no pixels per meter.
        PngInfo u = Gray().InsertBeforeIdat("pHYs", Phys(72, 96, 0)).Read().Info;
        Assert.True(u.HasPhys);
        Assert.Equal((72u, 96u, 0), (u.PhysX, u.PhysY, u.PhysUnit));
        Assert.Equal((0u, 0u), (u.XPixelsPerMeter, u.YPixelsPerMeter));
        Assert.Equal(0u, Gray().InsertBeforeIdat("pHYs", Phys(72, 96, 2)).Read().Info.XPixelsPerMeter);

        // Large values keep all 32 bits.
        Assert.Equal(0xffffffffu, Gray().InsertBeforeIdat("pHYs", Phys(0xffffffff, 1, 1)).Read().Info.XPixelsPerMeter);

        // First one wins, wrong length and out of place are ignored.
        PngBuilder dup = Gray();
        dup.InsertBeforeIdat("pHYs", Phys(1, 2, 1)).InsertBeforeIdat("pHYs", Phys(3, 4, 1));
        Assert.Equal(1u, dup.Read().Info.PhysX);
        Assert.False(Gray().InsertBeforeIdat("pHYs", 1, 2, 3).Read().Info.HasPhys);
        Assert.False(Gray().InsertAfterIdat("pHYs", Phys(1, 2, 1)).Read().Info.HasPhys);
    }

    /*-----------------------------------------------------------------*
     *                       gamma and colorspace                      *
     *-----------------------------------------------------------------*/
    private static double? GammaOf(PngBuilder b) => b.Read().Info.Gamma;

    [Fact]
    public void Gamma()
    {
        Assert.Equal(1.0, GammaOf(Gray()));
        Assert.Null(GammaOf(Gray().Remove("gAMA")));
        Assert.Equal(0.45455, GammaOf(Gray().Remove("gAMA").InsertBeforeIdat("gAMA", PngBuilder.BE32(45455))));

        // Limits of the accepted range.
        double? Of(uint v) => GammaOf(Gray().Remove("gAMA").InsertBeforeIdat("gAMA", PngBuilder.BE32(v)));
        Assert.Null(Of(0));
        Assert.Null(Of(15));
        Assert.Equal(0.00016, Of(16));
        Assert.Equal(6250.0, Of(625000000));
        Assert.Null(Of(625000001));
        Assert.Null(Of(0x7fffffff));
        Assert.Null(Of(0x80000000));
        Assert.Null(Of(0xffffffff));

        // Wrong length, after PLTE, after IDAT: ignored.
        Assert.Null(GammaOf(Gray().Remove("gAMA").InsertBeforeIdat("gAMA", 0, 1, 0)));
        Assert.Null(GammaOf(Gray().Remove("gAMA").InsertAfterIdat("gAMA", PngBuilder.BE32(45455))));
        PngBuilder pal = Pal();
        pal.Remove("gAMA");
        pal.InsertBeforeIdat("gAMA", PngBuilder.BE32(45455));
        Assert.Null(GammaOf(pal));
    }

    [Fact]
    public void OnlyTheFirstGammaChunkCounts()
    {
        byte[] g(uint v) => PngBuilder.BE32(v);
        PngBuilder b = Gray().Remove("gAMA");
        b.InsertBeforeIdat("gAMA", g(1000)).InsertBeforeIdat("gAMA", g(2000));
        Assert.Equal(0.01, GammaOf(b));

        // A later invalid one does not invalidate the first.
        b = Gray().Remove("gAMA");
        b.InsertBeforeIdat("gAMA", g(1000)).InsertBeforeIdat("gAMA", g(0xffffffff));
        Assert.Equal(0.01, GammaOf(b));

        // An invalid first one disables the gamma for good.
        b = Gray().Remove("gAMA");
        b.InsertBeforeIdat("gAMA", g(0xffffffff)).InsertBeforeIdat("gAMA", g(1000));
        Assert.Null(GammaOf(b));
    }

    [Fact]
    public void SrgbSetsAndOverridesGamma()
    {
        // Without gAMA.
        Assert.Equal(0.45455, GammaOf(Gray().Remove("gAMA").InsertBeforeIdat("sRGB", 0)));
        // After a gAMA chunk: the sRGB value wins.
        Assert.Equal(0.45455, GammaOf(Gray().InsertBeforeIdat("sRGB", 2)));
        // Before it: a later gAMA is ignored.
        Assert.Equal(0.45455, GammaOf(Gray().Remove("gAMA").InsertAfterIhdr("sRGB", 1).InsertBeforeIdat("gAMA", PngBuilder.BE32(1000))));
        // Wrong length is ignored.
        Assert.Equal(1.0, GammaOf(Gray().InsertBeforeIdat("sRGB", 0, 0)));
        // After IDAT: ignored.
        Assert.Equal(1.0, GammaOf(Gray().InsertAfterIdat("sRGB", 0)));
    }

    [Fact]
    public void InvalidSrgbDisablesLaterGammaChunks()
    {
        // The gamma that was already reported stays.
        Assert.Equal(1.0, GammaOf(Gray().InsertBeforeIdat("sRGB", 4)));
        // Without an earlier gAMA the colorspace is invalid for good.
        Assert.Null(GammaOf(Gray().Remove("gAMA").InsertAfterIhdr("sRGB", 4).InsertBeforeIdat("gAMA", PngBuilder.BE32(1000))));
        Assert.Null(GammaOf(Gray().Remove("gAMA").InsertAfterIhdr("sRGB", 9).InsertBeforeIdat("sRGB", 0)));
    }

    [Fact]
    public void ChromaticitiesMayInvalidateTheColorspace()
    {
        byte[] Chrm(params uint[] v) => v.SelectMany(x => PngBuilder.BE32(x)).ToArray();
        byte[] good = Chrm(31270, 32900, 64000, 33000, 30000, 60000, 15000, 6000);

        Assert.Equal(1.0, GammaOf(Gray().InsertAfterIhdr("cHRM", good)));
        Assert.Equal(1.0, GammaOf(Gray().InsertAfterIhdr("cHRM", good).InsertBeforeIdat("cHRM", good)));

        // Invalid values (out of range, zero white point, x + y > 1) before gAMA: no gamma.
        foreach (byte[] bad in new[]
        {
            Chrm(31270, 32900, 64000, 33000, 30000, 60000, 15000, 1048575),
            Chrm(0, 0, 0, 0, 0, 0, 0, 0),
            Chrm(0, 32900, 64000, 33000, 30000, 60000, 15000, 6000),
            Chrm(60000, 60000, 64000, 33000, 30000, 60000, 15000, 6000),
        })
            Assert.Null(GammaOf(Gray().Remove("gAMA").InsertAfterIhdr("cHRM", bad).InsertBeforeIdat("gAMA", PngBuilder.BE32(1000))));

        // A different second cHRM invalidates it as well.
        byte[] other = Chrm(31270, 32900, 64000, 33000, 30000, 60000, 15000, 6001);
        Assert.Null(GammaOf(Gray().Remove("gAMA").InsertAfterIhdr("cHRM", good).InsertBeforeIdat("cHRM", other).InsertBeforeIdat("gAMA", PngBuilder.BE32(1000))));

        // Wrong length, or a value that is no 31 bit fixed point number: ignored.
        Assert.Equal(1.0, GammaOf(Gray().InsertAfterIhdr("cHRM", good[..31])));
        byte[] huge = Chrm(31270, 32900, 0x80000000, 33000, 30000, 60000, 15000, 6000);
        Assert.Equal(0.01, GammaOf(Gray().Remove("gAMA").InsertAfterIhdr("cHRM", huge).InsertBeforeIdat("gAMA", PngBuilder.BE32(1000))));
    }

    /*-----------------------------------------------------------------*
     *                          text chunks                            *
     *-----------------------------------------------------------------*/
    private static List<PngText> TextsOf(params (string Name, byte[] Body)[] chunks)
    {
        PngBuilder b = Gray();
        foreach (var (name, body) in chunks) b.InsertBeforeIdat(name, body);
        return b.Read().Info.Texts;
    }

    [Fact]
    public void TextChunk()
    {
        Assert.Equal(new[] { new PngText("Key", "value") }, TextsOf(("tEXt", Text("Key", "value"))));
        // No NUL: key only. NUL last: empty text. Empty chunk: empty key.
        Assert.Equal(new[] { new PngText("Key", "") }, TextsOf(("tEXt", Latin1("Key"))));
        Assert.Equal(new[] { new PngText("Key", "") }, TextsOf(("tEXt", Latin1("Key\0"))));
        Assert.Equal(new[] { new PngText("", "") }, TextsOf(("tEXt", [])));
        // The text ends at its first NUL.
        Assert.Equal(new[] { new PngText("K", "a") }, TextsOf(("tEXt", Latin1("K\0a\0b"))));
        // Latin-1 bytes are kept one per char.
        Assert.Equal(new[] { new PngText("Ké", "ÿ\u0080") }, TextsOf(("tEXt", [(byte)'K', 0xe9, 0, 0xff, 0x80])));
        // File order, also with text after IDAT.
        PngBuilder b = Gray();
        b.InsertBeforeIdat("tEXt", Text("a", "1")).InsertAfterIdat("tEXt", Text("c", "3")).InsertBeforeIdat("tEXt", Text("b", "2"));
        Assert.Equal(new[] { new PngText("a", "1"), new PngText("b", "2"), new PngText("c", "3") }, b.Read().Info.Texts);
        // ReadInfo stops at IDAT.
        Assert.Equal(new[] { new PngText("a", "1"), new PngText("b", "2") }, b.ReadInfo().Texts);
    }

    [Fact]
    public void CompressedTextChunk()
    {
        byte[] Ztxt(string key, byte method, byte[] data) => [.. Latin1(key), 0, method, .. data];
        byte[] z = PngBuilder.Zlib(Latin1("hello\0world"));

        Assert.Equal(new[] { new PngText("Key", "hello") }, TextsOf(("zTXt", Ztxt("Key", 0, z))));
        Assert.Equal(new[] { new PngText("Key", "") }, TextsOf(("zTXt", Ztxt("Key", 0, EmptyZlib))));

        // Dropped: unknown method, no text, no NUL, bad or truncated data.
        Assert.Empty(TextsOf(("zTXt", Ztxt("Key", 1, z))));
        Assert.Empty(TextsOf(("zTXt", [.. Latin1("Key"), 0])));
        Assert.Empty(TextsOf(("zTXt", Latin1("Key"))));
        Assert.Empty(TextsOf(("zTXt", Ztxt("Key", 0, []))));
        Assert.Empty(TextsOf(("zTXt", Ztxt("Key", 0, z[..^1]))));
        Assert.Empty(TextsOf(("zTXt", Ztxt("Key", 0, z[..^4]))));
        Assert.Empty(TextsOf(("zTXt", Ztxt("Key", 0, z[..5]))));
        byte[] bad = (byte[])z.Clone();
        bad[^1] ^= 1;
        Assert.Empty(TextsOf(("zTXt", Ztxt("Key", 0, bad))));

        // A good chunk after bad ones is still read.
        Assert.Equal(new[] { new PngText("Ok", "hello") },
            TextsOf(("zTXt", Ztxt("Key", 0, z[..^1])), ("zTXt", Ztxt("Ok", 0, z))));
    }

    [Fact]
    public void InternationalTextChunk()
    {
        byte[] Itxt(string key, byte flag, byte method, string lang, string translated, byte[] text) =>
            [.. Latin1(key), 0, flag, method, .. Latin1(lang), 0, .. Encoding.UTF8.GetBytes(translated), 0, .. text];
        byte[] utf8 = Encoding.UTF8.GetBytes("café 日本");

        Assert.Equal(new[] { new PngText("Key", "café 日本") },
            TextsOf(("iTXt", Itxt("Key", 0, 0, "ja", "key", utf8))));
        Assert.Equal(new[] { new PngText("Key", "café 日本") },
            TextsOf(("iTXt", Itxt("Key", 1, 0, "", "", PngBuilder.Zlib(utf8)))));
        Assert.Equal(new[] { new PngText("Key", "") }, TextsOf(("iTXt", Itxt("Key", 0, 0, "", "", []))));

        // Dropped: bad flag, bad method with compression, bad data, truncated header.
        Assert.Empty(TextsOf(("iTXt", Itxt("Key", 2, 0, "", "", utf8))));
        Assert.Empty(TextsOf(("iTXt", Itxt("Key", 1, 1, "", "", PngBuilder.Zlib(utf8)))));
        Assert.Empty(TextsOf(("iTXt", Itxt("Key", 1, 0, "", "", PngBuilder.Zlib(utf8)[..^2]))));
        Assert.Empty(TextsOf(("iTXt", Itxt("Key", 1, 0, "", "", []))));
        Assert.Empty(TextsOf(("iTXt", [.. Latin1("Key"), 0, 0, 0])));
        Assert.Empty(TextsOf(("iTXt", Latin1("Key"))));
    }

    /*-----------------------------------------------------------------*
     *                            ReadInfo                             *
     *-----------------------------------------------------------------*/
    [Fact]
    public void ReadInfoNeedsOnlyTheDataBeforeIdat()
    {
        PngBuilder b = Pal();
        b.InsertBeforeIdat("pHYs", [.. PngBuilder.BE32(2835), .. PngBuilder.BE32(2835), 1]);
        b.InsertBeforeIdat("tRNS", 1, 2);
        byte[] png = b.Build();
        int idat = 8 + b.Chunks.TakeWhile(c => c.Name != "IDAT").Sum(c => 12 + c.Body.Length);

        PngInfo full = PngDecoder.Read(new MemoryStream(png), false).Info;
        // Up to and including the header of the first IDAT chunk.
        PngInfo info = PngDecoder.ReadInfo(new MemoryStream(png, 0, idat + 8));
        PngSuiteTests.AssertSameInfo(full, info);
        Assert.Equal(15, info.Palette!.Length);
        Assert.Equal(new byte[] { 1, 2 }, info.Trans);
        Assert.Equal(2835u, info.XPixelsPerMeter);

        // One byte less and the header is cut.
        Assert.Throws<PngException>(() => PngDecoder.ReadInfo(new MemoryStream(png, 0, idat + 7)));
        // Garbage after the header is not looked at.
        byte[] garbage = [.. png[..(idat + 8)], .. new byte[50]];
        Assert.Equal(32, PngDecoder.ReadInfo(new MemoryStream(garbage)).Width);
    }

    [Fact]
    public void ReadInfoStopsAtIdatAndReadFinishesTheFile()
    {
        PngBuilder b = Gray();
        b.InsertAfterIdat("tEXt", Text("late", "x"));
        Assert.Empty(b.ReadInfo().Texts);
        Assert.Single(b.Read().Info.Texts);
    }

    [Fact]
    public void ReadInfoReportsHeaderOfAllKinds()
    {
        foreach (string name in new[] { "basn0g16", "basn2c16", "basn4a16", "basn6a16", "basi3p02", "basn0g01" })
        {
            byte[] png = File.ReadAllBytes(Path.Combine(TestPaths.PngSuiteDir, name + ".png"));
            PngInfo i = PngDecoder.ReadInfo(new MemoryStream(png));
            PngInfo f = PngDecoder.Read(new MemoryStream(png), false).Info;
            PngSuiteTests.AssertSameInfo(f, i);
        }
    }
}
