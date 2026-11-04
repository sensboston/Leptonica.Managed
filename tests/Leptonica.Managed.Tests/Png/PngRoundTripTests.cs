using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Leptonica.Internal.Png;

namespace Leptonica.Managed.Tests.Png;

public class PngRoundTripTests
{

    private static readonly (int ColorType, int[] Depths)[] s_combos =
    [
        (0, [1, 2, 4, 8, 16]),
        (2, [8, 16]),
        (3, [1, 2, 4, 8]),
        (4, [8, 16]),
        (6, [8, 16]),
    ];

    public static IEnumerable<object[]> Cases()
    {
        foreach (var (ct, depths) in s_combos)
            foreach (int depth in depths)
                foreach (int w in new[] { 1, 2, 7, 8, 31, 33 })
                    foreach (int h in new[] { 1, 5 })
                        yield return new object[] { ct, depth, w, h };
    }

    internal static int Channels(int colorType) => colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };

    /// <summary>Random rows with the pad bits of each row cleared (the decoder clears them).</summary>
    internal static byte[] RandomRows(Random rng, int w, int h, int colorType, int depth, int paletteSize)
    {
        int bits = w * Channels(colorType) * depth;
        int rb = (bits + 7) / 8;
        var rows = new byte[rb * h];
        rng.NextBytes(rows);
        int pad = bits & 7;
        for (int y = 0; y < h; y++)
        {
            if (colorType == 3)
            {
                // Palette indices, packed.
                Array.Clear(rows, y * rb, rb);
                for (int x = 0; x < w; x++)
                {
                    int idx = rng.Next(paletteSize);
                    int bit = x * depth;
                    rows[y * rb + (bit >> 3)] |= (byte)(idx << (8 - depth - (bit & 7)));
                }
            }
            else if (pad != 0)
            {
                rows[y * rb + rb - 1] &= (byte)(0xff << (8 - pad));
            }
        }
        return rows;
    }

    private static PngWriteInfo MakeInfo(Random rng, int ct, int depth, int w, int h, bool meta, int level)
    {
        var info = new PngWriteInfo { Width = w, Height = h, BitDepth = depth, ColorType = ct, CompressionLevel = level };
        if (ct == 3)
        {
            int n = 1 << depth;
            info.Palette = Enumerable.Range(0, n)
                .Select(_ => new PngColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256))).ToArray();
            if (meta)
            {
                var trans = new byte[rng.Next(1, n + 1)];
                rng.NextBytes(trans);
                info.Trans = trans;
            }
        }
        if (meta)
        {
            info.PhysX = 2835;
            info.PhysY = 3937;
            info.Gamma = 0.45455;
            info.Texts.Add(new PngText("Title", "round trip"));
            info.Texts.Add(new PngText("Comment", "café ÿ"));
            info.Texts.Add(new PngText("Empty", ""));
        }
        return info;
    }

    private static byte[] Encode(PngWriteInfo info, byte[] rows)
    {
        using var ms = new MemoryStream();
        PngEncoder.Write(ms, info, rows);
        byte[] png = ms.ToArray();
        // For comparing the output with libpng (see PngBuilder.Dump).
        if (info.CompressionLevel == -1) PngBuilder.Dump(png, "RoundTrip");
        return png;
    }

    internal static void AssertRoundTrip(PngWriteInfo w, byte[] rows, PngImage img)
    {
        PngInfo i = img.Info;
        Assert.Equal(w.Width, i.Width);
        Assert.Equal(w.Height, i.Height);
        Assert.Equal(w.BitDepth, i.BitDepth);
        Assert.Equal(w.ColorType, i.ColorType);
        Assert.Equal(Channels(w.ColorType), i.Channels);
        Assert.Equal((w.Width * Channels(w.ColorType) * w.BitDepth + 7) / 8, i.RowBytes);
        Assert.Equal(0, i.Interlace);
        if (w.ColorType == 3) Assert.Equal(w.Palette, i.Palette);
        else Assert.Null(i.Palette);
        bool trns = w.ColorType == 3 && w.Trans != null;
        Assert.Equal(trns, i.HasTrns);
        Assert.Equal(trns ? w.Trans! : [], i.Trans);
        bool phys = w.PhysX != 0 || w.PhysY != 0;
        Assert.Equal(phys, i.HasPhys);
        Assert.Equal(phys ? 1 : 0, i.PhysUnit);
        Assert.Equal(w.PhysX, i.PhysX);
        Assert.Equal(w.PhysY, i.PhysY);
        Assert.Equal(w.PhysX, i.XPixelsPerMeter);
        Assert.Equal(w.PhysY, i.YPixelsPerMeter);
        if (w.Gamma is double g) Assert.InRange(Math.Abs(g - i.Gamma!.Value), 0, 1e-5);
        else Assert.Null(i.Gamma);
        Assert.Equal(w.Texts, i.Texts);
        Assert.True(rows.AsSpan().SequenceEqual(img.Pixels), "pixel data differs");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RoundTrip(int ct, int depth, int w, int h)
    {
        var rng = new Random(1234 + ct * 100 + depth + w * 7 + h);
        foreach (int level in new[] { -1, 0, 1, 6, 9 })
            foreach (bool meta in new[] { false, true })
            {
                PngWriteInfo info = MakeInfo(rng, ct, depth, w, h, meta, level);
                byte[] rows = RandomRows(rng, w, h, ct, depth, ct == 3 ? 1 << depth : 0);
                byte[] png = Encode(info, rows);

                PngImage img = PngDecoder.Read(new MemoryStream(png), false);
                AssertRoundTrip(info, rows, img);

                PngInfo header = PngDecoder.ReadInfo(new MemoryStream(png));
                PngSuiteTests.AssertSameInfo(img.Info, header);
            }
    }

    [Theory]
    [InlineData(2, 16)]
    [InlineData(6, 16)]
    [InlineData(0, 16)]
    [InlineData(4, 16)]
    public void Strip16KeepsHighByte(int ct, int depth)
    {
        var rng = new Random(77);
        var info = new PngWriteInfo { Width = 9, Height = 4, BitDepth = depth, ColorType = ct };
        byte[] rows = RandomRows(rng, 9, 4, ct, depth, 0);
        byte[] png = Encode(info, rows);
        PngImage img = PngDecoder.Read(new MemoryStream(png), true);
        Assert.Equal(8, img.Info.BitDepth);
        Assert.Equal(9 * Channels(ct), img.Info.RowBytes);
        for (int i = 0; i < img.Pixels.Length; i++)
            Assert.Equal(rows[2 * i], img.Pixels[i]);
    }

    /*-----------------------------------------------------------------*
     *                    structure of the output                      *
     *-----------------------------------------------------------------*/
    private static List<(string Name, byte[] Body)> ParseChunks(byte[] png)
    {
        var list = new List<(string, byte[])>();
        int p = 8;
        while (p < png.Length)
        {
            int n = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(p));
            string name = System.Text.Encoding.ASCII.GetString(png, p + 4, 4);
            list.Add((name, png.AsSpan(p + 8, n).ToArray()));
            uint crc = PngCrc.Update(0, png.AsSpan(p + 4, 4 + n));
            Assert.Equal(crc, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(p + 8 + n)));
            p += 12 + n;
        }
        return list;
    }

    private static byte[] Inflate(List<(string Name, byte[] Body)> chunks)
    {
        var idat = new MemoryStream();
        foreach (var c in chunks.Where(c => c.Name == "IDAT")) idat.Write(c.Body);
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var o = new MemoryStream();
        z.CopyTo(o);
        return o.ToArray();
    }

    [Fact]
    public void ChunkOrderMatchesLibpng()
    {
        var rng = new Random(5);
        PngWriteInfo info = MakeInfo(rng, 3, 4, 10, 3, true, -1);
        byte[] png = Encode(info, RandomRows(rng, 10, 3, 3, 4, 16));
        Assert.True(png.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }));
        var names = ParseChunks(png).Select(c => c.Name).ToList();
        Assert.Equal(new[] { "IHDR", "gAMA", "PLTE", "tRNS", "pHYs", "tEXt", "tEXt", "tEXt", "IDAT", "IEND" }, names);
    }

    [Fact]
    public void NoPhysChunkWhenResolutionIsZero()
    {
        var info = new PngWriteInfo { Width = 4, Height = 4, BitDepth = 8, ColorType = 0 };
        var names = ParseChunks(Encode(info, new byte[16])).Select(c => c.Name).ToList();
        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, names);
    }

    [Theory]
    [InlineData(3, 8)]
    [InlineData(0, 4)]
    [InlineData(0, 1)]
    public void FilterNoneForPaletteAndLowDepth(int ct, int depth)
    {
        var rng = new Random(9);
        int w = 40, h = 12;
        PngWriteInfo info = MakeInfo(rng, ct, depth, w, h, false, -1);
        byte[] rows = RandomRows(rng, w, h, ct, depth, ct == 3 ? 1 << depth : 0);
        byte[] raw = Inflate(ParseChunks(Encode(info, rows)));
        int rb = (w * depth + 7) / 8;
        Assert.Equal(h * (rb + 1), raw.Length);
        for (int y = 0; y < h; y++) Assert.Equal(0, raw[y * (rb + 1)]);
    }

    [Fact]
    public void AdaptiveFilterPicksSubAndUpOnSmoothImage()
    {
        int w = 64, h = 16;
        var rows = new byte[w * 3 * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int c = 0; c < 3; c++)
                    rows[(y * w + x) * 3 + c] = (byte)(x * 3 + y * 5 + c);
        var info = new PngWriteInfo { Width = w, Height = h, BitDepth = 8, ColorType = 2 };
        byte[] raw = Inflate(ParseChunks(Encode(info, rows)));
        int rb = w * 3;
        var types = Enumerable.Range(0, h).Select(y => (int)raw[y * (rb + 1)]).ToArray();
        Assert.Contains(types, t => t != 0);
        Assert.True(types[0] is 0 or 1 or 3, "first row never uses Up or Paeth");
        PngImage img = PngDecoder.Read(new MemoryStream(Encode(info, rows)), false);
        Assert.True(rows.AsSpan().SequenceEqual(img.Pixels));
    }

    [Fact]
    public void RgbWithSuggestedPaletteAndGrayWithIgnoredPalette()
    {
        var pal = new[] { new PngColor(1, 2, 3), new PngColor(4, 5, 6) };
        var rgb = new PngWriteInfo { Width = 2, Height = 1, BitDepth = 8, ColorType = 2, Palette = pal };
        PngImage a = PngDecoder.Read(new MemoryStream(Encode(rgb, new byte[6])), false);
        Assert.Equal(pal, a.Info.Palette);

        var gray = new PngWriteInfo { Width = 2, Height = 1, BitDepth = 8, ColorType = 0, Palette = pal };
        PngImage b = PngDecoder.Read(new MemoryStream(Encode(gray, new byte[2])), false);
        Assert.Null(b.Info.Palette);
    }

    [Fact]
    public void KeywordsAreCleanedLikeLibpng()
    {
        var info = new PngWriteInfo { Width = 1, Height = 1, BitDepth = 8, ColorType = 0 };
        info.Texts.Add(new PngText("  My   Key \t", "v"));
        PngImage img = PngDecoder.Read(new MemoryStream(Encode(info, new byte[1])), false);
        Assert.Equal(new PngText("My Key", "v"), Assert.Single(img.Info.Texts));

        info.Texts.Clear();
        info.Texts.Add(new PngText("", "v"));
        Assert.Throws<PngException>(() => Encode(info, new byte[1]));
    }

    [Fact]
    public void InvalidParametersThrow()
    {
        byte[] one = new byte[1];
        Assert.Throws<PngException>(() => Encode(new PngWriteInfo { Width = 1, Height = 1, BitDepth = 8, ColorType = 1 }, one));
        Assert.Throws<PngException>(() => Encode(new PngWriteInfo { Width = 1, Height = 1, BitDepth = 4, ColorType = 2 }, one));
        Assert.Throws<PngException>(() => Encode(new PngWriteInfo { Width = 1, Height = 1, BitDepth = 8, ColorType = 3 }, one));
        Assert.Throws<PngException>(() => Encode(new PngWriteInfo { Width = 0, Height = 1, BitDepth = 8, ColorType = 0 }, one));
        Assert.Throws<PngException>(() => Encode(new PngWriteInfo { Width = 1, Height = 1, BitDepth = 8, ColorType = 0 }, new byte[2]));
        Assert.Throws<PngException>(() => Encode(new PngWriteInfo { Width = 1, Height = 1, BitDepth = 8, ColorType = 0, CompressionLevel = 10 }, one));
        var pal = new PngWriteInfo { Width = 1, Height = 1, BitDepth = 1, ColorType = 3 };
        pal.Palette = [new(0, 0, 0), new(1, 1, 1), new(2, 2, 2)];
        Assert.Throws<PngException>(() => Encode(pal, one));
    }

    [Fact]
    public void LargeImageSpansSeveralIdatChunks()
    {
        var rng = new Random(3);
        int w = 700, h = 300;
        var info = new PngWriteInfo { Width = w, Height = h, BitDepth = 8, ColorType = 6, CompressionLevel = 0 };
        byte[] rows = RandomRows(rng, w, h, 6, 8, 0);
        byte[] png = Encode(info, rows);
        Assert.True(ParseChunks(png).Count(c => c.Name == "IDAT") > 1);
        PngImage img = PngDecoder.Read(new MemoryStream(png), false);
        Assert.True(rows.AsSpan().SequenceEqual(img.Pixels));
    }
}
