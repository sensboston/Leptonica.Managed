using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BitMiracle.LibTiff.Classic;
using BitMiracle.LibTiff.Classic.Internal;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

/// <summary>
/// The fast Group 4 decoder must behave exactly like the original Fax4Decode: the same rows,
/// the same messages, the same row at which reading fails, and an exception where the original
/// throws one, on clean files and on damaged ones.
/// </summary>
public class TiffFastFax4DecoderTests
{
    /// <summary>Records the messages of the file being decoded on this thread; passes the rest on.</summary>
    private sealed class RecordingHandler : TiffErrorHandler
    {
        [ThreadStatic] public static Tiff? Current;
        [ThreadStatic] public static List<string>? Messages;
        public TiffErrorHandler? Previous;

        public override void ErrorHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
        {
            if (tif != null && ReferenceEquals(tif, Current) && Messages != null)
                Messages.Add("error: " + method + ": " + string.Format(format, args));
            else
                Previous?.ErrorHandlerExt(tif!, clientData, method, format, args);
        }

        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
        {
            if (tif != null && ReferenceEquals(tif, Current) && Messages != null)
                Messages.Add("warning: " + method + ": " + string.Format(format, args));
            else
                Previous?.WarningHandlerExt(tif!, clientData, method, format, args);
        }

        public override void ErrorHandler(Tiff tif, string method, string format, params object[] args) =>
            ErrorHandlerExt(tif, null!, method, format, args);

        public override void WarningHandler(Tiff tif, string method, string format, params object[] args) =>
            WarningHandlerExt(tif, null!, method, format, args);
    }

    private static string Decode(byte[] file, bool fast)
    {
        bool saved = CCITTCodec.DisableFastFax4Decoder;
        CCITTCodec.DisableFastFax4Decoder = !fast;
        var messages = new List<string>();
        var sb = new StringBuilder();
        try
        {
            using Tiff tif = Tiff.ClientOpen("g4", "r", new MemoryStream(file), new TiffStream());
            RecordingHandler.Current = tif;
            RecordingHandler.Messages = messages;
            int h = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            var line = new byte[tif.ScanlineSize()];
            var all = new List<byte>();
            int row = 0;
            try
            {
                for (; row < h; row++)
                {
                    if (!tif.ReadScanline(line, row, 0))
                        break;
                    all.AddRange(line);
                }
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                sb.Append("exception at row ").Append(row).Append(' ');
            }
            sb.Append($"rows {row} of {h}, {Convert.ToHexString(SHA256.HashData(all.ToArray()))[..16]}");
        }
        finally
        {
            CCITTCodec.DisableFastFax4Decoder = saved;
            RecordingHandler.Current = null;
            RecordingHandler.Messages = null;
        }
        foreach (string m in messages)
            sb.Append(" | ").Append(m);
        return sb.ToString();
    }

    private static List<(string Name, byte[] Data)> G4Files()
    {
        var files = new List<(string, byte[])>();
        foreach (string f in Directory.GetFiles(TestPaths.ProgDir, "*.tif").OrderBy(f => f, StringComparer.Ordinal))
        {
            byte[] data = File.ReadAllBytes(f);
            using Tiff? tif = Tiff.ClientOpen("probe", "r", new MemoryStream(data), new TiffStream());
            if (tif != null && tif.GetField(TiffTag.COMPRESSION)?[0].ToInt() == (int)Compression.CCITTFAX4)
                files.Add((Path.GetFileName(f), data));
        }
        return files;
    }

    /// <summary>The strips of the first image: (offset, length) in the file.</summary>
    private static List<(int Offset, int Length)> Strips(byte[] file)
    {
        using Tiff tif = Tiff.ClientOpen("probe", "r", new MemoryStream(file), new TiffStream());
        int[] offsets = tif.GetField(TiffTag.STRIPOFFSETS)[0].ToIntArray();
        int[] counts = tif.GetField(TiffTag.STRIPBYTECOUNTS)[0].ToIntArray();
        return offsets.Zip(counts, (o, c) => (o, c)).ToList();
    }

    private static readonly string[] s_kinds = { "flip", "zerotail", "garbage", "zeros", "ones", "eol", "ext", "insertbits", "deletebits" };

    /// <summary>
    /// Damages a strip on the bit level, in place: inserted and deleted bits shift the rest of
    /// the strip, and the result is cut or padded back to the strip's length.
    /// </summary>
    private static void Damage(byte[] file, int offset, int length, string kind, Random rnd)
    {
        var bits = new List<bool>(length * 8);
        for (int i = 0; i < length * 8; i++)
            bits.Add((file[offset + (i >> 3)] & (0x80 >> (i & 7))) != 0);
        int Pos() => rnd.Next(0, Math.Max(1, bits.Count));
        void Insert(string pattern) => bits.InsertRange(Math.Min(Pos(), bits.Count), pattern.Select(c => c == '1'));
        switch (kind)
        {
            case "flip":
                for (int n = rnd.Next(1, 12); n > 0 && bits.Count > 0; n--) { int p = Pos(); bits[p] = !bits[p]; }
                break;
            case "zerotail":
                for (int i = Pos(); i < bits.Count; i++) bits[i] = false;
                break;
            case "garbage":
            {
                int at = Pos();
                for (int i = 0; i < rnd.Next(1, 400) && at + i < bits.Count; i++) bits[at + i] = rnd.Next(2) == 1;
                break;
            }
            case "zeros": Insert(new string('0', rnd.Next(1, 300))); break;
            case "ones": Insert(new string('1', rnd.Next(1, 300))); break;
            case "eol": Insert(rnd.Next(3) switch { 0 => "000000000001", 1 => "0000000000011", _ => "000000000001000000000001" }); break;
            case "ext": Insert(rnd.Next(2) == 0 ? "0000001111" : "000000001111"); break;
            case "insertbits":
                for (int n = rnd.Next(1, 4); n > 0; n--) Insert(rnd.Next(2) == 1 ? "1" : "0");
                break;
            case "deletebits":
                for (int n = rnd.Next(1, 4); n > 0 && bits.Count > 0; n--) bits.RemoveAt(Pos() % bits.Count);
                break;
        }
        for (int i = 0; i < length; i++)
        {
            int v = 0;
            for (int b = 0; b < 8; b++)
                if (8 * i + b < bits.Count && bits[8 * i + b])
                    v |= 0x80 >> b;
            file[offset + i] = (byte)v;
        }
    }

    [Fact]
    public void CleanFiles_MatchOriginalDecoder()
    {
        TiffErrorHandler handler = Install();
        try
        {
            var files = G4Files();
            Assert.True(files.Count > 50, $"only {files.Count} G4 files in prog");
            var differ = new List<string>();
            foreach (var (name, data) in files)
            {
                string slow = Decode(data, false), fast = Decode(data, true);
                if (slow != fast)
                    differ.Add($"{name}: {slow} <> {fast}");
            }
            Assert.Empty(differ);
        }
        finally
        {
            Uninstall(handler);
        }
    }

    [Fact]
    public void DamagedFiles_MatchOriginalDecoder()
    {
        TiffErrorHandler handler = Install();
        try
        {
            var differ = new List<string>();
            int checks = 0, failing = 0;
            int seed = 0;
            foreach (var (name, data) in G4Files())
            {
                var strips = Strips(data);
                for (int n = 0; n < 9; n++)
                {
                    var rnd = new Random(++seed);
                    byte[] bad = (byte[])data.Clone();
                    foreach (var (offset, length) in strips)
                        Damage(bad, offset, length, s_kinds[n], rnd);
                    string slow = Decode(bad, false), fast = Decode(bad, true);
                    checks++;
                    if (slow.Contains(" | ") || slow.StartsWith("exception", StringComparison.Ordinal))
                        failing++;
                    if (slow != fast)
                        differ.Add($"{name} {s_kinds[n]} seed {seed}: {slow} <> {fast}");
                }
            }
            Assert.True(failing > checks / 2, $"only {failing} of {checks} damaged files gave messages");
            Assert.Empty(differ);
        }
        finally
        {
            Uninstall(handler);
        }
    }

    private static TiffErrorHandler Install()
    {
        var handler = new RecordingHandler();
        handler.Previous = Tiff.SetErrorHandler(handler);
        return handler;
    }

    private static void Uninstall(TiffErrorHandler handler) => Tiff.SetErrorHandler(((RecordingHandler)handler).Previous!);
}
