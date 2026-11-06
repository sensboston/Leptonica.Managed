using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BitMiracle.LibJpeg.Classic;
using BitMiracle.LibJpeg.Classic.Internal;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

/// <summary>
/// The fast baseline Huffman decoder must behave exactly like decode_mcu_full: the same
/// pixels, the same warnings (with their parameters, such as the number of extraneous bytes
/// before a marker) and the same fatal errors, on clean files and on damaged ones.
/// </summary>
public class JpegFastHuffmanDecoderTests
{
    private sealed class RecordingErrorMgr : jpeg_error_mgr
    {
        public readonly List<string> Messages = new();

        public override void emit_message(int msg_level)
        {
            if (msg_level < 0)
                Messages.Add(format_message());
            base.emit_message(msg_level);
        }

        public override void output_message() { }

        public override void error_exit() => throw new InvalidDataException(format_message());
    }

    private static string Decode(byte[] data, bool fast)
    {
        bool saved = huff_entropy_decoder.DisableFastDecoder;
        huff_entropy_decoder.DisableFastDecoder = !fast;
        var err = new RecordingErrorMgr();
        var sb = new StringBuilder();
        try
        {
            var cinfo = new jpeg_decompress_struct(err);
            try
            {
                cinfo.jpeg_stdio_src(new MemoryStream(data));
                cinfo.jpeg_read_header(true);
                cinfo.jpeg_start_decompress();
                int stride = cinfo.Output_width * cinfo.Output_components;
                var row = new byte[1][] { new byte[stride] };
                ulong hash = 14695981039346656037UL;
                while (cinfo.Output_scanline < cinfo.Output_height)
                {
                    cinfo.jpeg_read_scanlines(row, 1);
                    foreach (byte b in row[0])
                        hash = (hash ^ b) * 1099511628211UL;
                }
                cinfo.jpeg_finish_decompress();
                sb.Append($"{cinfo.Output_width}x{cinfo.Output_height}x{cinfo.Output_components} {hash:x16}");
            }
            catch (InvalidDataException e)
            {
                sb.Append("error: ").Append(e.Message);
            }
            finally
            {
                cinfo.jpeg_destroy();
            }
        }
        finally
        {
            huff_entropy_decoder.DisableFastDecoder = saved;
        }
        foreach (string m in err.Messages)
            sb.Append(" | ").Append(m);
        return sb.ToString();
    }

    /// <summary>A synthetic image encoded with restart markers, in color (4:2:0) or gray.</summary>
    private static byte[] EncodeWithRestarts(int width, int height, bool color, int restartInterval, int quality, int seed)
    {
        var rnd = new Random(seed);
        int comps = color ? 3 : 1;
        var ms = new MemoryStream();
        var cinfo = new jpeg_compress_struct(new RecordingErrorMgr());
        cinfo.jpeg_stdio_dest(ms);
        cinfo.Image_width = width;
        cinfo.Image_height = height;
        cinfo.Input_components = comps;
        cinfo.In_color_space = color ? J_COLOR_SPACE.JCS_RGB : J_COLOR_SPACE.JCS_GRAYSCALE;
        cinfo.jpeg_set_defaults();
        cinfo.jpeg_set_quality(quality, true);
        cinfo.Restart_interval = restartInterval;
        cinfo.jpeg_start_compress(true);
        var row = new byte[1][] { new byte[width * comps] };
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width * comps; x++)
                row[0][x] = (byte)((x * 7 + y * 3 + ((x / 13 + y / 11) % 2) * 90 + rnd.Next(48)) & 0xFF);
            cinfo.jpeg_write_scanlines(row, 1);
        }
        cinfo.jpeg_finish_compress();
        return ms.ToArray();
    }

    /// <summary>Offset of the first byte of entropy-coded data, or -1.</summary>
    private static int ScanStart(byte[] d)
    {
        for (int i = 2; i + 3 < d.Length; i++)
        {
            if (d[i] == 0xFF && d[i + 1] == 0xDA)
                return i + 2 + ((d[i + 2] << 8) | d[i + 3]);
        }
        return -1;
    }

    private static IEnumerable<byte[]> Damage(byte[] d, int seed, int count)
    {
        int start = ScanStart(d);
        if (start < 0 || start >= d.Length - 4)
            yield break;
        var rnd = new Random(seed);
        for (int n = 0; n < count; n++)
        {
            int pos = rnd.Next(start, d.Length - 2);
            var list = new List<byte>(d);
            switch (n % 8)
            {
                case 0: // bit flips
                    for (int i = 0; i < 1 + rnd.Next(4); i++)
                    {
                        int p = rnd.Next(start, d.Length - 2);
                        list[p] ^= (byte)(1 << rnd.Next(8));
                    }
                    break;
                case 1: // truncation
                    list.RemoveRange(pos, list.Count - pos);
                    break;
                case 2: // inserted marker: RSTn, DHT, EOI or a stray one
                    byte[] markers = { 0xD0, 0xD3, 0xD7, 0xC4, 0xD9, 0xE1, 0x01 };
                    list.InsertRange(pos, new byte[] { 0xFF, markers[rnd.Next(markers.Length)] });
                    break;
                case 3: // garbage
                    for (int i = 0; i < 1 + rnd.Next(64) && pos + i < list.Count - 2; i++)
                        list[pos + i] = (byte)rnd.Next(256);
                    break;
                case 4: // broken stuffing: drop the 00 after an FF, or add FF runs
                    int ff = list.FindIndex(pos, b => b == 0xFF);
                    if (ff > 0 && ff + 1 < list.Count - 2 && list[ff + 1] == 0)
                        list.RemoveAt(ff + 1);
                    else
                        list.InsertRange(pos, new byte[] { 0xFF, 0xFF, 0xFF, 0x00 });
                    break;
                case 5: // a run of 0xFF
                    for (int i = 0; i < 1 + rnd.Next(24) && pos + i < list.Count - 2; i++)
                        list[pos + i] = 0xFF;
                    break;
                case 6: // a run of zeros
                    for (int i = 0; i < 1 + rnd.Next(200) && pos + i < list.Count - 2; i++)
                        list[pos + i] = 0;
                    break;
                default: // stuffed FF 00 pairs and extraneous bytes before the trailing marker
                    list.InsertRange(pos, new byte[] { 0xFF, 0x00, 0x12, 0xFF, 0x00 });
                    list.InsertRange(list.Count - 2, Enumerable.Repeat((byte)0x5A, 1 + rnd.Next(9)));
                    break;
            }
            yield return list.ToArray();
        }
    }

    private static List<(string Name, byte[] Data)> Inputs()
    {
        var inputs = new List<(string, byte[])>();
        if (Directory.Exists(TestPaths.ProgDir))
        {
            foreach (string f in Directory.GetFiles(TestPaths.ProgDir, "*.jpg").OrderBy(f => f, StringComparer.Ordinal))
                inputs.Add((Path.GetFileName(f), File.ReadAllBytes(f)));
        }
        inputs.Add(("restart-color", EncodeWithRestarts(333, 250, true, 3, 85, 1)));
        inputs.Add(("restart-gray", EncodeWithRestarts(301, 197, false, 1, 97, 2)));
        inputs.Add(("restart-color-q30", EncodeWithRestarts(160, 120, true, 7, 30, 3)));
        return inputs;
    }

    [Fact]
    public void CleanFiles_MatchReferenceDecoder()
    {
        var differ = new List<string>();
        foreach (var (name, data) in Inputs())
        {
            string slow = Decode(data, false), fast = Decode(data, true);
            if (slow != fast)
                differ.Add($"{name}: {slow} <> {fast}");
        }
        Assert.Empty(differ);
    }

    [Fact]
    public void DamagedFiles_MatchReferenceDecoder()
    {
        var differ = new List<string>();
        int checks = 0, withWarnings = 0;
        int seed = 0;
        foreach (var (name, data) in Inputs())
        {
            foreach (byte[] bad in Damage(data, ++seed, 16))
            {
                string slow = Decode(bad, false), fast = Decode(bad, true);
                checks++;
                if (slow.Contains(" | ") || slow.StartsWith("error"))
                    withWarnings++;
                if (slow != fast)
                    differ.Add($"{name} #{checks}: {slow} <> {fast}");
            }
        }
        Assert.True(checks > 1000, $"only {checks} damaged files");
        Assert.True(withWarnings > checks / 2, $"only {withWarnings} of {checks} damaged files gave warnings");
        Assert.Empty(differ);
    }
}
