using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Leptonica.Internal.Png;

namespace Leptonica.Managed.Tests.Png;

/// <summary>Assembles PNG files chunk by chunk for the error handling tests.</summary>
internal sealed class PngBuilder
{
    public sealed class Chunk
    {
        public string Name;
        public byte[] Body;
        public bool BadCrc;

        public Chunk(string name, byte[] body) { Name = name; Body = body; }
    }

    public static readonly byte[] Signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public List<Chunk> Chunks { get; } = new();

    public static PngBuilder Parse(byte[] png)
    {
        var b = new PngBuilder();
        int p = 8;
        while (p + 8 <= png.Length)
        {
            int n = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(p));
            b.Chunks.Add(new Chunk(Encoding.ASCII.GetString(png, p + 4, 4), png.AsSpan(p + 8, n).ToArray()));
            p += 12 + n;
        }
        return b;
    }

    public static PngBuilder FromSuite(string name) =>
        Parse(File.ReadAllBytes(Path.Combine(TestPaths.PngSuiteDir, name + ".png")));

    public Chunk this[string name] => Chunks.First(c => c.Name == name);

    public int IndexOf(string name) => Chunks.FindIndex(c => c.Name == name);

    public PngBuilder Insert(int index, string name, params byte[] body)
    {
        Chunks.Insert(index, new Chunk(name, body));
        return this;
    }

    /// <summary>Inserts before the first IDAT chunk.</summary>
    public PngBuilder InsertBeforeIdat(string name, params byte[] body) => Insert(IndexOf("IDAT"), name, body);

    /// <summary>Inserts before IEND.</summary>
    public PngBuilder InsertAfterIdat(string name, params byte[] body) => Insert(IndexOf("IEND"), name, body);

    public PngBuilder InsertAfterIhdr(string name, params byte[] body) => Insert(1, name, body);

    public PngBuilder Remove(string name)
    {
        Chunks.RemoveAt(IndexOf(name));
        return this;
    }

    /// <summary>Concatenated IDAT payloads.</summary>
    public byte[] IdatData() => Chunks.Where(c => c.Name == "IDAT").SelectMany(c => c.Body).ToArray();

    /// <summary>Replaces all IDAT chunks by chunks of the given payloads.</summary>
    public PngBuilder SetIdat(params byte[][] pieces)
    {
        int at = IndexOf("IDAT");
        Chunks.RemoveAll(c => c.Name == "IDAT");
        for (int i = 0; i < pieces.Length; i++)
            Chunks.Insert(at + i, new Chunk("IDAT", pieces[i]));
        return this;
    }

    public static byte[] Zlib(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(data);
        return ms.ToArray();
    }

    public static byte[] Unzlib(byte[] data)
    {
        using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        var o = new MemoryStream();
        z.CopyTo(o);
        return o.ToArray();
    }

    public static byte[] BE32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        return b;
    }

    private static int s_counter;

    /// <summary>
    /// Serializes the file. With the environment variable LEPT_PNG_DUMP_DIR set the file is also
    /// written there, so that the same inputs can be fed to libpng for comparison.
    /// </summary>
    public byte[] Build([CallerMemberName] string? caller = null)
    {
        var ms = new MemoryStream();
        ms.Write(Signature);
        foreach (Chunk c in Chunks)
        {
            ms.Write(BE32((uint)c.Body.Length));
            byte[] name = Encoding.ASCII.GetBytes(c.Name);
            ms.Write(name);
            ms.Write(c.Body);
            uint crc = PngCrc.Update(PngCrc.Update(0, name), c.Body);
            if (c.BadCrc) crc ^= 0x1234;
            ms.Write(BE32(crc));
        }
        byte[] png = ms.ToArray();
        Dump(png, caller);
        return png;
    }

    public static void Dump(byte[] png, string? caller)
    {
        string? dir = Environment.GetEnvironmentVariable("LEPT_PNG_DUMP_DIR");
        if (!string.IsNullOrEmpty(dir))
            File.WriteAllBytes(Path.Combine(dir, $"{caller}_{System.Threading.Interlocked.Increment(ref s_counter):D4}.png"), png);
    }

    public PngImage Read(bool strip16 = false, [CallerMemberName] string? caller = null) =>
        PngDecoder.Read(new MemoryStream(Build(caller)), strip16);

    public PngInfo ReadInfo([CallerMemberName] string? caller = null) =>
        PngDecoder.ReadInfo(new MemoryStream(Build(caller)));

    public void AssertReadFails([CallerMemberName] string? caller = null) =>
        Assert.Throws<PngException>(() => PngDecoder.Read(new MemoryStream(Build(caller)), false));
}
