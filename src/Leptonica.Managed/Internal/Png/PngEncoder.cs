using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Leptonica.Internal.Png;

/// <summary>
/// Managed replacement for the libpng write path used by Leptonica (non-interlaced only).
/// Chunk order as libpng: IHDR, gAMA, PLTE, tRNS, pHYs, tEXt, IDAT, IEND.
/// </summary>
internal static class PngEncoder
{
    private static readonly byte[] s_signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    private const int IdatChunkSize = 1 << 16;

    /// <param name="rows">Height rows of RowBytes bytes, RowBytes = (Width * BitDepth * channels + 7) / 8.</param>
    public static void Write(Stream stream, PngWriteInfo info, ReadOnlySpan<byte> rows)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(info);

        int channels = info.ColorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new PngException("Invalid image color type specified"),
        };
        bool depthOk = info.BitDepth is 1 or 2 or 4 or 8 or 16;
        if (info.ColorType is 2 or 4 or 6) depthOk &= info.BitDepth >= 8;
        if (info.ColorType == 3) depthOk &= info.BitDepth <= 8;
        if (!depthOk) throw new PngException("Invalid bit depth specified");
        if (info.Width <= 0 || info.Height <= 0 || info.Width > 1000000 || info.Height > 1000000)
            throw new PngException("Invalid image size specified");
        if (info.CompressionLevel < -1 || info.CompressionLevel > 9)
            throw new PngException("Invalid compression level");

        int rowBytes = (int)(((long)info.Width * channels * info.BitDepth + 7) >> 3);
        if (rows.Length != (long)info.Height * rowBytes)
            throw new PngException("Invalid row data size");

        PngColor[]? palette = info.Palette;
        if (info.ColorType == 3)
        {
            if (palette == null) throw new PngException("Valid palette required for paletted images");
            if (palette.Length == 0 || palette.Length > 1 << info.BitDepth)
                throw new PngException("Invalid number of colors in palette");
        }

        Span<byte> buf = stackalloc byte[13];
        stream.Write(s_signature);

        BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)info.Width);
        BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(4), (uint)info.Height);
        buf[8] = (byte)info.BitDepth;
        buf[9] = (byte)info.ColorType;
        buf[10] = 0;    // compression method
        buf[11] = 0;    // filter method
        buf[12] = 0;    // not interlaced
        WriteChunk(stream, "IHDR", buf);

        if (info.Gamma is double gamma)
        {
            BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)(int)Math.Floor(gamma * 100000.0 + 0.5));
            WriteChunk(stream, "gAMA", buf.Slice(0, 4));
        }

        // A palette is required for type 3, optional (suggested) for color, ignored for gray.
        if (palette != null && palette.Length is > 0 and <= 256 && info.ColorType is 2 or 3 or 6)
        {
            var plte = new byte[palette.Length * 3];
            for (int i = 0; i < palette.Length; i++)
            {
                plte[3 * i] = palette[i].Red;
                plte[3 * i + 1] = palette[i].Green;
                plte[3 * i + 2] = palette[i].Blue;
            }
            WriteChunk(stream, "PLTE", plte);

            // Invalid counts are dropped, as libpng does.
            if (info.ColorType == 3 && info.Trans != null && info.Trans.Length > 0 && info.Trans.Length <= palette.Length)
                WriteChunk(stream, "tRNS", info.Trans);
        }

        if (info.PhysX != 0 || info.PhysY != 0)
        {
            Span<byte> phys = stackalloc byte[9];
            BinaryPrimitives.WriteUInt32BigEndian(phys, info.PhysX);
            BinaryPrimitives.WriteUInt32BigEndian(phys.Slice(4), info.PhysY);
            phys[8] = 1;
            WriteChunk(stream, "pHYs", phys);
        }

        foreach (PngText t in info.Texts)
        {
            byte[] key = CheckKeyword(t.Key);
            byte[] text = ToLatin1(t.Text);
            var body = new byte[key.Length + 1 + text.Length];
            key.CopyTo(body, 0);
            text.CopyTo(body, key.Length + 1);
            WriteChunk(stream, "tEXt", body);
        }

        WriteImageData(stream, info, channels, rowBytes, rows);
        WriteChunk(stream, "IEND", default);
    }

    /*-----------------------------------------------------------------*
     *                            chunks                               *
     *-----------------------------------------------------------------*/
    private static void WriteChunk(Stream stream, string name, ReadOnlySpan<byte> data)
    {
        Span<byte> head = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(head, (uint)data.Length);
        for (int i = 0; i < 4; i++) head[4 + i] = (byte)name[i];
        uint crc = PngCrc.Update(0, head.Slice(4));
        crc = PngCrc.Update(crc, data);
        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, crc);
        stream.Write(head);
        stream.Write(data);
        stream.Write(tail);
    }

    private static byte[] ToLatin1(string s)
    {
        // A text ends at its first NUL (C string semantics of png_set_text).
        int z = s.IndexOf('\0');
        if (z >= 0) s = s.Substring(0, z);
        var b = new byte[s.Length];
        for (int i = 0; i < b.Length; i++) b[i] = s[i] <= 0xff ? (byte)s[i] : (byte)'?';
        return b;
    }

    /// <summary>The keyword cleanup of png_check_keyword.</summary>
    private static byte[] CheckKeyword(string key)
    {
        byte[] src = ToLatin1(key);
        var dst = new byte[Math.Min(src.Length, 79)];
        int n = 0;
        bool space = true;   // drops leading spaces, collapses runs
        foreach (byte c in src)
        {
            if (n == 79) break;
            if (c is >= 32 and <= 126 or >= 161)
            {
                if (c == 32)
                {
                    if (space) continue;
                    space = true;
                }
                else space = false;
                dst[n++] = c;
            }
            else if (!space)
            {
                dst[n++] = 32;
                space = true;
            }
        }
        if (n > 0 && dst[n - 1] == 32) n--;
        if (n == 0) throw new PngException("tEXt: invalid keyword");
        return dst.AsSpan(0, n).ToArray();
    }

    /*-----------------------------------------------------------------*
     *                          image data                             *
     *-----------------------------------------------------------------*/
    private static CompressionLevel MapLevel(int level) => level switch
    {
        0 => CompressionLevel.NoCompression,
        1 or 2 or 3 => CompressionLevel.Fastest,
        8 or 9 => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal,   // -1 (default = 6), 4..7
    };

    private static void WriteImageData(Stream stream, PngWriteInfo info, int channels, int rowBytes, ReadOnlySpan<byte> rows)
    {
        int bpp = Math.Max(1, (info.BitDepth * channels) >> 3);
        bool adaptive = info.ColorType != 3 && info.BitDepth >= 8;

        using var idat = new IdatWriter(stream);
        int stageLen = Math.Max(IdatChunkSize, rowBytes + 1);
        byte[] stage = ArrayPool<byte>.Shared.Rent(stageLen);
        byte[]? zeroRent = adaptive ? ArrayPool<byte>.Shared.Rent(rowBytes) : null;
        byte[]? bestRent = adaptive ? ArrayPool<byte>.Shared.Rent(rowBytes) : null;
        byte[]? trialRent = adaptive ? ArrayPool<byte>.Shared.Rent(rowBytes) : null;
        try
        {
            if (zeroRent != null)
                zeroRent.AsSpan(0, rowBytes).Clear();

            using var z = new ZLibStream(idat, MapLevel(info.CompressionLevel), leaveOpen: true);
            int fill = 0;

            for (int y = 0; y < info.Height; y++)
            {
                ReadOnlySpan<byte> row = rows.Slice(y * rowBytes, rowBytes);
                if (fill + rowBytes + 1 > stageLen)
                {
                    z.Write(stage, 0, fill);
                    fill = 0;
                }

                if (!adaptive)
                {
                    stage[fill] = PngFilter.None;
                    row.CopyTo(stage.AsSpan(fill + 1));
                }
                else
                {
                    ReadOnlySpan<byte> prev = y == 0 ? zeroRent.AsSpan(0, rowBytes) : rows.Slice((y - 1) * rowBytes, rowBytes);
                    int bestType = PngFilter.None;
                    int bestCost = PngFilter.Cost(row, int.MaxValue);
                    byte[] bestBuf = bestRent!;
                    byte[] trialBuf = trialRent!;
                    bool haveBest = false;
                    for (int type = PngFilter.Sub; type <= PngFilter.Paeth; type++)
                    {
                        if (y == 0 && type is PngFilter.Up or PngFilter.Paeth) continue;
                        Span<byte> trialSpan = trialBuf.AsSpan(0, rowBytes);
                        PngFilter.Apply(type, row, prev, bpp, trialSpan);
                        int cost = PngFilter.Cost(trialSpan, bestCost);
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            bestType = type;
                            (bestBuf, trialBuf) = (trialBuf, bestBuf);
                            haveBest = true;
                        }
                    }
                    stage[fill] = (byte)bestType;
                    if (haveBest) bestBuf.AsSpan(0, rowBytes).CopyTo(stage.AsSpan(fill + 1));
                    else row.CopyTo(stage.AsSpan(fill + 1));
                }
                fill += rowBytes + 1;
            }
            if (fill > 0) z.Write(stage, 0, fill);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(stage);
            if (zeroRent != null) ArrayPool<byte>.Shared.Return(zeroRent);
            if (bestRent != null) ArrayPool<byte>.Shared.Return(bestRent);
            if (trialRent != null) ArrayPool<byte>.Shared.Return(trialRent);
        }
        idat.Finish();
    }

    /// <summary>Collects compressed output and emits it as IDAT chunks.</summary>
    private sealed class IdatWriter : Stream
    {
        private readonly Stream _out;
        private byte[]? _buf = ArrayPool<byte>.Shared.Rent(IdatChunkSize);
        private int _fill;

        public IdatWriter(Stream output) { _out = output; }

        public override void Write(ReadOnlySpan<byte> data)
        {
            byte[] buf = _buf!;
            while (data.Length > 0)
            {
                int n = Math.Min(data.Length, IdatChunkSize - _fill);
                data.Slice(0, n).CopyTo(buf.AsSpan(_fill));
                _fill += n;
                data = data.Slice(n);
                if (_fill == IdatChunkSize) Emit();
            }
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        private void Emit()
        {
            if (_fill == 0) return;
            WriteChunk(_out, "IDAT", _buf!.AsSpan(0, _fill));
            _fill = 0;
        }

        public void Finish() => Emit();

        protected override void Dispose(bool disposing)
        {
            byte[]? b = _buf;
            _buf = null;
            if (b != null) ArrayPool<byte>.Shared.Return(b);
            base.Dispose(disposing);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
