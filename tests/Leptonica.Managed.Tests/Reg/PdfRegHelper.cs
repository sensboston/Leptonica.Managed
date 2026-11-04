using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// Semantic verification helpers for generated PDFs whose raw compressed stream bytes
/// differ from native C libz/libjpeg/libtiff goldens while preserving identical
/// PDF structure, MediaBoxes, image dimensions, and decompressed pixel rasters.
/// </summary>
internal static class PdfRegHelper
{
    private static readonly Regex FlateStreamRegex = new(
        @"<<(?<hdr>(?:(?!(?:>>)).)*?/FlateDecode(?:(?!(?:>>)).)*?)>>\nstream\n",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LengthRegex = new(
        @"/Length\s+(?<len>\d+)",
        RegexOptions.Compiled);

    public static void AssertPdfStructureMatchesGolden(string actualPath, string goldenPath)
    {
        int actualPages = PdfIo.GetPageCount(actualPath);
        int goldenPages = PdfIo.GetPageCount(goldenPath);
        Assert.True(goldenPages > 0);
        Assert.Equal(goldenPages, actualPages);

        PdfIo.GetPageSizes(actualPath, out Numa aw, out Numa ah, out _, out _);
        PdfIo.GetPageSizes(goldenPath, out Numa gw, out Numa gh, out _, out _);
        Assert.Equal(gw.GetIArray(), aw.GetIArray());
        Assert.Equal(gh.GetIArray(), ah.GetIArray());

        PdfIo.GetMediaBoxSizes(actualPath, out Numa amw, out Numa amh, out _, out _);
        PdfIo.GetMediaBoxSizes(goldenPath, out Numa gmw, out Numa gmh, out _, out _);
        Assert.Equal(gmw.GetIArray(), amw.GetIArray());
        Assert.Equal(gmh.GetIArray(), amh.GetIArray());
    }

    public static void AssertFlatePdfEqualsGolden(string actualPath, string goldenPath)
    {
        AssertPdfStructureMatchesGolden(actualPath, goldenPath);

        List<(string NormalizedHeader, byte[] Raster)> actualStreams = ExtractFlateStreams(actualPath);
        List<(string NormalizedHeader, byte[] Raster)> goldenStreams = ExtractFlateStreams(goldenPath);

        Assert.NotEmpty(goldenStreams);
        Assert.Equal(goldenStreams.Count, actualStreams.Count);
        for (int i = 0; i < goldenStreams.Count; i++) {
            Assert.Equal(goldenStreams[i].NormalizedHeader, actualStreams[i].NormalizedHeader);
            Assert.Equal(goldenStreams[i].Raster, actualStreams[i].Raster);
        }
    }

    public static List<(string NormalizedHeader, byte[] Raster)> ExtractFlateStreams(string path)
    {
        byte[] bytes = File.ReadAllBytes(Utils.GenPathname(path, null));
        string text = Encoding.Latin1.GetString(bytes);
        var list = new List<(string, byte[])>();
        foreach (Match m in FlateStreamRegex.Matches(text)) {
            string hdr = m.Groups["hdr"].Value;
            Match lm = LengthRegex.Match(hdr);
            Assert.True(lm.Success, $"Missing /Length in Flate stream dictionary of {path}");
            int len = int.Parse(lm.Groups["len"].Value);
            int start = m.Index + m.Length;
            using var ms = new MemoryStream(bytes, start, len, writable: false);
            using var zs = new ZLibStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            zs.CopyTo(outMs);
            string normHdr = LengthRegex.Replace(hdr, "/Length N");
            list.Add((normHdr, outMs.ToArray()));
        }
        return list;
    }
}
