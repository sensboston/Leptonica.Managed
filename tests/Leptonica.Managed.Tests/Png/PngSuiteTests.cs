using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Leptonica.Internal.Png;

namespace Leptonica.Managed.Tests.Png;

public class PngSuiteTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string f in Directory.GetFiles(TestPaths.PngSuiteDir, "*.png").OrderBy(x => x, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(f);
            yield return new object[] { name, "strip16" };
            yield return new object[] { name, "identity" };
        }
    }

    private static byte[] Load(string name) => File.ReadAllBytes(Path.Combine(TestPaths.PngSuiteDir, name + ".png"));

    private static JsonElement Expected(string name, string mode)
    {
        string path = Path.Combine(TestPaths.PngSuiteDir, "expected", $"{name}.{mode}.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    /// <summary>Compares the header and chunk fields shared by Read and ReadInfo.</summary>
    internal static void AssertInfo(JsonElement e, PngInfo info, bool pixelDataFields = true)
    {
        Assert.Equal(e.GetProperty("width").GetInt32(), info.Width);
        Assert.Equal(e.GetProperty("height").GetInt32(), info.Height);
        Assert.Equal(e.GetProperty("bitDepth").GetInt32(), info.BitDepth);
        Assert.Equal(e.GetProperty("colorType").GetInt32(), info.ColorType);
        Assert.Equal(e.GetProperty("channels").GetInt32(), info.Channels);
        Assert.Equal(e.GetProperty("rowBytes").GetInt32(), info.RowBytes);
        Assert.Equal(e.GetProperty("interlace").GetInt32(), info.Interlace);

        var pal = e.GetProperty("palette").EnumerateArray()
            .Select(p => new PngColor((byte)p[0].GetInt32(), (byte)p[1].GetInt32(), (byte)p[2].GetInt32())).ToArray();
        if (pal.Length == 0) Assert.Null(info.Palette);
        else Assert.Equal(pal, info.Palette);

        Assert.Equal(e.GetProperty("tRNS").GetBoolean(), info.HasTrns);
        Assert.Equal(e.GetProperty("trans").EnumerateArray().Select(x => (byte)x.GetInt32()).ToArray(), info.Trans);
        var tc = e.GetProperty("transColor").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Assert.Equal(new[] { tc[0], tc[1], tc[2], tc[3] },
            new[] { (int)info.TransGray, info.TransRed, info.TransGreen, info.TransBlue });

        var phys = e.GetProperty("pHYs");
        if (phys.ValueKind == JsonValueKind.Null)
        {
            Assert.False(info.HasPhys);
        }
        else
        {
            Assert.True(info.HasPhys);
            Assert.Equal(phys[0].GetUInt32(), info.PhysX);
            Assert.Equal(phys[1].GetUInt32(), info.PhysY);
            Assert.Equal(phys[2].GetInt32(), info.PhysUnit);
        }
        Assert.Equal(e.GetProperty("xPixelsPerMeter").GetUInt32(), info.XPixelsPerMeter);
        Assert.Equal(e.GetProperty("yPixelsPerMeter").GetUInt32(), info.YPixelsPerMeter);

        var gamma = e.GetProperty("gamma");
        if (gamma.ValueKind == JsonValueKind.Null) Assert.Null(info.Gamma);
        else
        {
            Assert.NotNull(info.Gamma);
            Assert.InRange(Math.Abs(gamma.GetDouble() - info.Gamma!.Value), 0, 1e-5);
        }

        var texts = e.GetProperty("text").EnumerateArray()
            .Select(t => new PngText(DecodeBytes(t[0].GetString()!), DecodeBytes(t[1].GetString()!))).ToList();
        Assert.Equal(texts, info.Texts);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReadMatchesLibpng(string name, string mode)
    {
        JsonElement e = Expected(name, mode);
        using var ms = new MemoryStream(Load(name));
        if (e.GetProperty("error").GetBoolean())
        {
            Assert.Throws<PngException>(() => PngDecoder.Read(ms, mode == "strip16"));
            return;
        }
        PngImage img = PngDecoder.Read(ms, mode == "strip16");
        AssertInfo(e, img.Info);
        byte[] bin = File.ReadAllBytes(Path.Combine(TestPaths.PngSuiteDir, "expected", $"{name}.{mode}.bin"));
        Assert.Equal(bin.Length, img.Pixels.Length);
        Assert.Equal(bin.Length, img.Pixels.Length);
        Assert.True(bin.AsSpan().SequenceEqual(img.Pixels), "pixel data differs");
    }

    /// <summary>The JSON escapes every byte as \u00XX; the bytes are UTF-8.</summary>
    private static string DecodeBytes(string s) =>
        System.Text.Encoding.UTF8.GetString(s.Select(c => (byte)c).ToArray());

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReadInfoMatchesRead(string name, string mode)
    {
        if (mode != "identity") return;
        JsonElement e = Expected(name, mode);
        using var ms = new MemoryStream(Load(name));
        // Images that fail only in IDAT (CRC, data) are fine for ReadInfo.
        if (e.GetProperty("error").GetBoolean()) return;
        PngInfo info = PngDecoder.ReadInfo(ms);
        PngImage full = PngDecoder.Read(new MemoryStream(Load(name)), false);
        AssertSameInfo(full.Info, info);
    }

    internal static void AssertSameInfo(PngInfo expected, PngInfo actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.BitDepth, actual.BitDepth);
        Assert.Equal(expected.ColorType, actual.ColorType);
        Assert.Equal(expected.Channels, actual.Channels);
        Assert.Equal(expected.RowBytes, actual.RowBytes);
        Assert.Equal(expected.Interlace, actual.Interlace);
        Assert.Equal(expected.Palette, actual.Palette);
        Assert.Equal(expected.HasTrns, actual.HasTrns);
        Assert.Equal(expected.Trans, actual.Trans);
        Assert.Equal(expected.TransGray, actual.TransGray);
        Assert.Equal(expected.TransRed, actual.TransRed);
        Assert.Equal(expected.TransGreen, actual.TransGreen);
        Assert.Equal(expected.TransBlue, actual.TransBlue);
        Assert.Equal(expected.HasPhys, actual.HasPhys);
        Assert.Equal(expected.PhysX, actual.PhysX);
        Assert.Equal(expected.PhysY, actual.PhysY);
        Assert.Equal(expected.PhysUnit, actual.PhysUnit);
        Assert.Equal(expected.XPixelsPerMeter, actual.XPixelsPerMeter);
        Assert.Equal(expected.YPixelsPerMeter, actual.YPixelsPerMeter);
        Assert.Equal(expected.Gamma, actual.Gamma);
        Assert.Equal(expected.Texts, actual.Texts);
    }
}
