using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Leptonica.Internal.Png;

/// <summary>
/// Managed replacement for the libpng read path used by Leptonica:
/// <c>png_read_png</c> with PNG_TRANSFORM_IDENTITY or PNG_TRANSFORM_STRIP_16,
/// and <c>png_read_info</c> alone.
/// </summary>
internal static class PngDecoder
{
    /// <summary>Decodes the whole image (rows deinterlaced, optional 16 to 8 bit strip).</summary>
    public static PngImage Read(Stream stream, bool strip16)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var parser = new Parser(stream);
        parser.ReadInfoPhase();
        PngInfo info = parser.Info;

        bool strip = strip16 && info.BitDepth == 16;
        int outBitDepth = strip ? 8 : info.BitDepth;
        int outRowBytes = strip ? info.Width * info.Channels : info.RowBytes;
        long total = (long)info.Height * outRowBytes;
        if (total > Array.MaxLength)
            throw new PngException("Image is too large");
        var pixels = new byte[total];

        parser.DecodeImage(pixels, strip, outRowBytes);
        parser.ReadEndPhase();

        info.BitDepth = outBitDepth;
        info.RowBytes = outRowBytes;
        return new PngImage { Info = info, Pixels = pixels };
    }

    /// <summary>Reads everything up to the first IDAT chunk (<c>png_read_info</c>).</summary>
    public static PngInfo ReadInfo(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var parser = new Parser(stream);
        parser.ReadInfoPhase();
        return parser.Info;
    }

    private static readonly byte[] s_signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    private static uint Name(string s) =>
        (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);

    private static readonly uint IHDR = Name("IHDR"), PLTE = Name("PLTE"), IDAT = Name("IDAT"),
        IEND = Name("IEND"), tRNS = Name("tRNS"), gAMA = Name("gAMA"), sRGB = Name("sRGB"),
        cHRM = Name("cHRM"), pHYs = Name("pHYs"), tEXt = Name("tEXt"), zTXt = Name("zTXt"),
        iTXt = Name("iTXt");

    // Chunk types libpng has handlers for (those not parsed here are skipped).
    private static readonly HashSet<uint> s_knownChunks = new[]
    {
        "IHDR", "PLTE", "IDAT", "IEND", "bKGD", "cHRM", "eXIf", "gAMA", "hIST", "iCCP", "iTXt",
        "oFFs", "pCAL", "pHYs", "sBIT", "sCAL", "sPLT", "sRGB", "tEXt", "tIME", "tRNS", "zTXt",
        "cICP", "mDCV", "cLLI",
    }.Select(Name).ToHashSet();

    private static bool IsKnownChunk(uint name) => s_knownChunks.Contains(name);

    private static readonly int[] s_xoff = [0, 4, 0, 2, 0, 1, 0];
    private static readonly int[] s_yoff = [0, 0, 4, 0, 2, 0, 1];
    private static readonly int[] s_xinc = [8, 8, 4, 4, 2, 2, 1];
    private static readonly int[] s_yinc = [8, 8, 8, 4, 4, 2, 2];

    private sealed class Parser
    {
        private readonly Stream _s;
        public readonly PngInfo Info = new();

        private bool _haveIhdr, _havePlte, _haveIdat;
        private int _numPalette;
        private bool _hasPending;
        private uint _pendingLen, _pendingName;

        // Colorspace state of libpng (png_ptr->colorspace), reduced to what decides
        // whether png_get_gAMA reports a value: once invalid, gAMA/sRGB/cHRM are ignored.
        private bool _csInvalid, _csHaveGamma, _csFromSrgb, _csHaveEndpoints;
        private int _srgbIntent;
        private int[]? _chrm;

        public Parser(Stream s) { _s = s; }

        /*---------------------------------------------------------------*
         *                      low level input                          *
         *---------------------------------------------------------------*/
        private void ReadExact(Span<byte> dst)
        {
            while (dst.Length > 0)
            {
                int n = _s.Read(dst);
                if (n <= 0) throw new PngException("Read Error");
                dst = dst.Slice(n);
            }
        }

        internal void ReadHeader(out uint length, out uint name)
        {
            Span<byte> h = stackalloc byte[8];
            ReadExact(h);
            length = BinaryPrimitives.ReadUInt32BigEndian(h);
            if (length > 0x7fffffff)
                throw new PngException("PNG unsigned integer out of range");
            name = BinaryPrimitives.ReadUInt32BigEndian(h.Slice(4));
            for (int i = 0; i < 4; i++)
            {
                int c = h[4 + i];
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')))
                    throw new PngException("invalid chunk type");
            }
        }

        private static uint NameCrc(uint name)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(b, name);
            return PngCrc.Update(0, b);
        }

        /// <summary>Reads (or skips) a chunk body and its CRC.</summary>
        private byte[] ReadBody(uint length, uint name, bool keep, out bool crcOk)
        {
            uint crc = NameCrc(name);
            byte[] body;
            if (keep && length <= (1 << 20))
            {
                body = new byte[length];
                ReadExact(body);
                crc = PngCrc.Update(crc, body);
            }
            else
            {
                var ms = keep ? new MemoryStream() : null;
                var block = new byte[Math.Min(length, 65536)];
                long left = length;
                while (left > 0)
                {
                    int n = (int)Math.Min(left, block.Length);
                    ReadExact(block.AsSpan(0, n));
                    crc = PngCrc.Update(crc, block.AsSpan(0, n));
                    ms?.Write(block, 0, n);
                    left -= n;
                }
                body = ms?.ToArray() ?? [];
            }
            Span<byte> c = stackalloc byte[4];
            ReadExact(c);
            crcOk = BinaryPrimitives.ReadUInt32BigEndian(c) == crc;
            return body;
        }

        /// <summary>Body of a critical chunk: a CRC mismatch is an error.</summary>
        private byte[] ReadCritical(uint length, uint name)
        {
            byte[] body = ReadBody(length, name, true, out bool ok);
            if (!ok) throw new PngException("CRC error");
            return body;
        }

        /*---------------------------------------------------------------*
         *                       chunk dispatch                          *
         *---------------------------------------------------------------*/
        public void ReadInfoPhase()
        {
            Span<byte> sig = stackalloc byte[8];
            ReadExact(sig);
            if (!sig.SequenceEqual(s_signature))
                throw new PngException("Not a PNG file");

            for (;;)
            {
                ReadHeader(out uint length, out uint name);
                if (name == IDAT)
                {
                    if (!_haveIhdr) throw new PngException("Missing IHDR before IDAT");
                    if (Info.ColorType == 3 && !_havePlte)
                        throw new PngException("Missing PLTE before IDAT");
                    _haveIdat = true;
                    _hasPending = true;
                    _pendingLen = length;
                    _pendingName = name;
                    return;
                }
                HandleChunk(length, name);
            }
        }

        public void ReadEndPhase()
        {
            for (;;)
            {
                uint length, name;
                if (_hasPending)
                {
                    length = _pendingLen;
                    name = _pendingName;
                    _hasPending = false;
                }
                else
                {
                    ReadHeader(out length, out name);
                }

                if (name == IDAT)
                {
                    // Too many IDATs: libpng only warns and skips the chunk.
                    ReadCritical(length, name);
                    continue;
                }
                HandleChunk(length, name);
                if (name == IEND) return;
            }
        }

        private void HandleChunk(uint length, uint name)
        {
            if (name == IHDR) { HandleIhdr(length); return; }
            // Chunks libpng knows are an error before IHDR; others are skipped (or fail
            // below when critical).
            if (!_haveIhdr && IsKnownChunk(name)) throw new PngException("missing IHDR");

            if (name == PLTE) HandlePlte(length);
            else if (name == IEND) HandleIend(length);
            else if (name == tRNS) HandleTrns(length);
            else if (name == gAMA) HandleGama(length);
            else if (name == sRGB) HandleSrgb(length);
            else if (name == cHRM) HandleChrm(length);
            else if (name == pHYs) HandlePhys(length);
            else if (name == tEXt) HandleText(length);
            else if (name == zTXt) HandleZtxt(length);
            else if (name == iTXt) HandleItxt(length);
            else
            {
                // Bit 5 of the first letter clear: critical chunk we do not know.
                if ((name & 0x20000000) == 0)
                    throw new PngException("unknown critical chunk");
                ReadBody(length, name, false, out _);
            }
        }

        private void HandleIhdr(uint length)
        {
            if (_haveIhdr) throw new PngException("IHDR: out of place");
            if (length != 13) throw new PngException("IHDR: invalid");
            byte[] b = ReadCritical(length, IHDR);

            uint w = BinaryPrimitives.ReadUInt32BigEndian(b);
            uint h = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(4));
            if (w > 0x7fffffff || h > 0x7fffffff)
                throw new PngException("PNG unsigned integer out of range");
            int depth = b[8], color = b[9], comp = b[10], filter = b[11], interlace = b[12];

            // png_check_IHDR; PNG_USER_WIDTH_MAX = PNG_USER_HEIGHT_MAX = 1000000.
            if (w == 0 || w > 1000000 || h == 0 || h > 1000000)
                throw new PngException("Invalid IHDR data");
            bool depthOk = depth is 1 or 2 or 4 or 8 or 16;
            int channels;
            switch (color)
            {
                case 0: channels = 1; break;
                case 2: channels = 3; depthOk &= depth >= 8; break;
                case 3: channels = 1; depthOk &= depth <= 8; break;
                case 4: channels = 2; depthOk &= depth >= 8; break;
                case 6: channels = 4; depthOk &= depth >= 8; break;
                default: throw new PngException("Invalid IHDR data");
            }
            if (!depthOk || comp != 0 || filter != 0 || interlace > 1)
                throw new PngException("Invalid IHDR data");

            Info.Width = (int)w;
            Info.Height = (int)h;
            Info.BitDepth = depth;
            Info.ColorType = color;
            Info.Channels = channels;
            Info.Interlace = interlace;
            Info.RowBytes = (int)(((long)w * channels * depth + 7) >> 3);
            _haveIhdr = true;
        }

        private void HandlePlte(uint length)
        {
            if (_havePlte) throw new PngException("PLTE: duplicate");
            if (_haveIdat)
            {
                ReadBody(length, PLTE, false, out bool ok);
                if (!ok) throw new PngException("CRC error");
                return; // out of place (warning)
            }
            _havePlte = true;
            if (Info.ColorType is 0 or 4)
            {
                ReadBody(length, PLTE, false, out bool ok);
                if (!ok) throw new PngException("CRC error");
                return; // ignored in grayscale PNG (warning)
            }
            if (length > 3 * 256 || length % 3 != 0)
            {
                ReadBody(length, PLTE, false, out bool ok);
                if (!ok) throw new PngException("CRC error");
                if (Info.ColorType == 3) throw new PngException("PLTE: invalid");
                return;
            }
            byte[] b = ReadCritical(length, PLTE);
            int num = (int)length / 3;
            int max = Info.ColorType == 3 ? 1 << Info.BitDepth : 256;
            if (num > max) num = max; // extra entries are dropped silently
            var pal = new PngColor[num];
            for (int i = 0; i < num; i++)
                pal[i] = new PngColor(b[3 * i], b[3 * i + 1], b[3 * i + 2]);
            Info.Palette = pal;
            _numPalette = num;
        }

        private void HandleIend(uint length)
        {
            if (!_haveIdat) throw new PngException("IEND: out of place");
            ReadCritical(length, IEND);
        }

        private void HandleTrns(uint length)
        {
            byte[] b = ReadBody(length, tRNS, true, out bool crcOk);
            if (_haveIdat || Info.HasTrns) return;   // out of place / duplicate
            PngInfo info = Info;
            ushort g = 0, r = 0, gr = 0, bl = 0;
            byte[] trans = [];
            switch (info.ColorType)
            {
                case 0:
                    if (length != 2) return;
                    g = BinaryPrimitives.ReadUInt16BigEndian(b);
                    break;
                case 2:
                    if (length != 6) return;
                    r = BinaryPrimitives.ReadUInt16BigEndian(b);
                    gr = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(2));
                    bl = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(4));
                    break;
                case 3:
                    if (!_havePlte) return;
                    if (length > _numPalette || length > 256 || length == 0) return;
                    trans = b;
                    break;
                default:
                    return; // invalid with alpha channel
            }
            if (!crcOk) return;
            info.HasTrns = true;
            info.Trans = trans;
            info.TransGray = g;
            info.TransRed = r;
            info.TransGreen = gr;
            info.TransBlue = bl;
        }

        // gAMA, sRGB and cHRM follow png_colorspace_*: only the first gAMA counts, an
        // out-of-range value or an invalid sRGB/cHRM invalidates the colorspace for good
        // (what was already reported stays), and a valid sRGB overrides the gamma.
        private void HandleGama(uint length)
        {
            byte[] b = ReadBody(length, gAMA, true, out bool crcOk);
            if (_havePlte || _haveIdat) return;      // out of place
            if (length != 4 || !crcOk) return;
            if (_csInvalid || _csHaveGamma) return;
            uint v = BinaryPrimitives.ReadUInt32BigEndian(b);
            if (v < 16 || v > 625000000)
            {
                _csInvalid = true;
                return;
            }
            _csHaveGamma = true;
            Info.Gamma = v / 100000.0;
        }

        private void HandleSrgb(uint length)
        {
            byte[] b = ReadBody(length, sRGB, true, out bool crcOk);
            if (_havePlte || _haveIdat) return;
            if (length != 1 || !crcOk) return;
            if (_csInvalid) return;
            int intent = b[0];
            if (intent >= 4)
            {
                _csInvalid = true;
                return;
            }
            if (_csFromSrgb)
            {
                if (intent != _srgbIntent) _csInvalid = true;
                return;
            }
            _csFromSrgb = true;
            _srgbIntent = intent;
            _csHaveGamma = true;
            Info.Gamma = 45455 / 100000.0;
        }

        private void HandleChrm(uint length)
        {
            byte[] b = ReadBody(length, cHRM, true, out bool crcOk);
            if (_havePlte || _haveIdat) return;
            if (length != 32 || !crcOk) return;
            if (_csInvalid) return;

            var v = new int[8];
            for (int i = 0; i < 8; i++)
            {
                uint u = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(4 * i));
                if (u > 0x7fffffff) return;     // not a fixed point value: chunk ignored
                v[i] = (int)u;
            }
            // v: white x,y; red x,y; green x,y; blue x,y (in 1/100000).
            bool valid = v[0] > 0 && v[1] > 0;
            for (int i = 0; valid && i < 8; i += 2)
                valid = v[i] >= 0 && v[i + 1] >= 0 && (long)v[i] + v[i + 1] <= 100000;
            // The red, green and blue points must not be collinear.
            valid = valid && ((long)(v[2] - v[6]) * (v[5] - v[7]) - (long)(v[4] - v[6]) * (v[3] - v[7])) != 0;
            if (!valid)
            {
                _csInvalid = true;
                return;
            }
            if (_csHaveEndpoints)
            {
                if (!v.AsSpan().SequenceEqual(_chrm)) _csInvalid = true;
                return;
            }
            _csHaveEndpoints = true;
            _chrm = v;
        }

        private void HandlePhys(uint length)
        {
            byte[] b = ReadBody(length, pHYs, true, out bool crcOk);
            if (_haveIdat || Info.HasPhys) return;
            if (length != 9 || !crcOk) return;
            PngInfo info = Info;
            info.PhysX = BinaryPrimitives.ReadUInt32BigEndian(b);
            info.PhysY = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(4));
            info.PhysUnit = b[8];
            info.HasPhys = true;
            if (info.PhysUnit == 1)
            {
                info.XPixelsPerMeter = info.PhysX;
                info.YPixelsPerMeter = info.PhysY;
            }
        }

        /*---------------------------------------------------------------*
         *                        text chunks                            *
         *---------------------------------------------------------------*/
        private static string Latin1(ReadOnlySpan<byte> b) => Encoding.Latin1.GetString(b);

        /// <summary>Bytes up to the first NUL (C string semantics).</summary>
        private static ReadOnlySpan<byte> CString(ReadOnlySpan<byte> b)
        {
            int z = b.IndexOf((byte)0);
            return z < 0 ? b : b.Slice(0, z);
        }

        private void HandleText(uint length)
        {
            byte[] b = ReadBody(length, tEXt, true, out bool crcOk);
            if (!crcOk) return;
            int z = Array.IndexOf(b, (byte)0);
            ReadOnlySpan<byte> key = z < 0 ? b : b.AsSpan(0, z);
            ReadOnlySpan<byte> text = z < 0 ? default : CString(b.AsSpan(z + 1));
            Info.Texts.Add(new PngText(Latin1(key), Latin1(text)));
        }

        private void HandleZtxt(uint length)
        {
            byte[] b = ReadBody(length, zTXt, true, out bool crcOk);
            if (!crcOk) return;
            int z = Array.IndexOf(b, (byte)0);
            if (z < 0) return;                              // no text
            if (z + 1 >= b.Length || b[z + 1] != 0) return; // unknown compression type
            byte[]? text = Inflate(b.AsSpan(z + 2));
            if (text == null) return;
            Info.Texts.Add(new PngText(Latin1(b.AsSpan(0, z)), Latin1(CString(text))));
        }

        private void HandleItxt(uint length)
        {
            byte[] b = ReadBody(length, iTXt, true, out bool crcOk);
            if (!crcOk) return;
            int z = Array.IndexOf(b, (byte)0);
            if (z < 0 || z + 5 > b.Length) return;          // truncated
            int flag = b[z + 1], method = b[z + 2];
            if (flag > 1 || (flag == 1 && method != 0)) return;
            int p = z + 3;
            int langEnd = Array.IndexOf(b, (byte)0, p);
            if (langEnd < 0) return;
            int tkEnd = Array.IndexOf(b, (byte)0, langEnd + 1);
            if (tkEnd < 0) return;
            ReadOnlySpan<byte> text = b.AsSpan(tkEnd + 1);
            if (flag == 1)
            {
                // A compressed text that fails to inflate drops the chunk.
                byte[]? inflated = Inflate(text);
                if (inflated == null) return;
                text = inflated;
            }
            // iTXt text is UTF-8; the other text chunks are kept byte per char.
            Info.Texts.Add(new PngText(Latin1(b.AsSpan(0, z)), Encoding.UTF8.GetString(CString(text))));
        }

        /// <summary>Inflates a complete zlib stream; null on any error.</summary>
        private static byte[]? Inflate(ReadOnlySpan<byte> data)
        {
            if (data.Length == 0) return null;
            try
            {
                using var input = new ProbeStream(data.ToArray());
                using var z = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                // Not CopyTo: that reads the input to its end whether or not the zlib stream
                // is complete, which would hide a truncated stream.
                var block = new byte[4096];
                int n;
                while ((n = z.Read(block, 0, block.Length)) > 0)
                    output.Write(block, 0, n);
                // A truncated stream makes the inflater ask for input past the end.
                return input.EofHit ? null : output.ToArray();
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        /// <summary>A memory stream that records a read at its end.</summary>
        private sealed class ProbeStream : MemoryStream
        {
            public bool EofHit;

            public ProbeStream(byte[] data) : base(data, false) { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = base.Read(buffer, offset, count);
                if (n == 0 && count > 0) EofHit = true;
                return n;
            }

            public override int Read(Span<byte> buffer)
            {
                int n = base.Read(buffer);
                if (n == 0 && buffer.Length > 0) EofHit = true;
                return n;
            }
        }

        /*---------------------------------------------------------------*
         *                         image data                            *
         *---------------------------------------------------------------*/
        /// <summary>The concatenated IDAT chunk payloads as one stream.</summary>
        private sealed class IdatStream : Stream
        {
            private readonly Parser _p;
            private uint _left;
            private uint _crc;
            private bool _inChunk;
            public bool Done;

            /// <summary>
            /// The inflater asked for more input after the last IDAT chunk: the zlib stream
            /// is truncated (the framework's inflater would otherwise report just the end).
            /// </summary>
            public bool EofHit;

            public IdatStream(Parser p, uint firstLength)
            {
                _p = p;
                _left = firstLength;
                _crc = NameCrc(IDAT);
                _inChunk = true;
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                Read(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> buffer)
            {
                if (buffer.Length == 0) return 0;
                while (!Done)
                {
                    if (_left > 0)
                    {
                        int n = (int)Math.Min((uint)buffer.Length, _left);
                        _p.ReadExact(buffer.Slice(0, n));
                        _crc = PngCrc.Update(_crc, buffer.Slice(0, n));
                        _left -= (uint)n;
                        return n;
                    }
                    EndChunk();
                }
                EofHit = true;
                return 0;
            }

            /// <summary>Verifies the CRC of the current chunk and advances to the next header.</summary>
            private void EndChunk()
            {
                if (_inChunk)
                {
                    Span<byte> c = stackalloc byte[4];
                    _p.ReadExact(c);
                    if (BinaryPrimitives.ReadUInt32BigEndian(c) != _crc)
                        throw new PngException("CRC error");
                    _inChunk = false;
                }
                _p.ReadHeader(out uint length, out uint name);
                if (name == IDAT)
                {
                    _left = length;
                    _crc = NameCrc(IDAT);
                    _inChunk = true;
                }
                else
                {
                    _p._hasPending = true;
                    _p._pendingLen = length;
                    _p._pendingName = name;
                    Done = true;
                }
            }

            /// <summary>Consumes the rest of the IDAT run, leaving the next header pending.</summary>
            public void Drain()
            {
                var skip = new byte[4096];
                while (!Done)
                {
                    if (_left > 0)
                    {
                        int n = (int)Math.Min((uint)skip.Length, _left);
                        _p.ReadExact(skip.AsSpan(0, n));
                        _crc = PngCrc.Update(_crc, skip.AsSpan(0, n));
                        _left -= (uint)n;
                    }
                    else EndChunk();
                }
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <summary>Buffered access to the inflated data that never asks zlib for more than the image needs.</summary>
        private sealed class InflateSource
        {
            private readonly Stream _z;
            private readonly byte[] _buf = new byte[1 << 16];
            private int _pos, _end;
            private long _remaining;

            public InflateSource(Stream z, long total) { _z = z; _remaining = total; }

            private int Fetch(Span<byte> dst)
            {
                int want = (int)Math.Min(dst.Length, _remaining);
                if (want == 0) throw new PngException("Not enough image data");
                int got;
                try { got = _z.Read(dst.Slice(0, want)); }
                catch (InvalidDataException e) { throw new PngException("zlib error: " + e.Message); }
                if (got <= 0) throw new PngException("Not enough image data");
                _remaining -= got;
                return got;
            }

            public byte ReadByte()
            {
                if (_pos == _end)
                {
                    _end = Fetch(_buf);
                    _pos = 0;
                }
                return _buf[_pos++];
            }

            public void Read(Span<byte> dst)
            {
                while (dst.Length > 0)
                {
                    if (_pos == _end)
                    {
                        if (dst.Length >= 4096)
                        {
                            int n = Fetch(dst);
                            dst = dst.Slice(n);
                            continue;
                        }
                        _end = Fetch(_buf);
                        _pos = 0;
                    }
                    int c = Math.Min(dst.Length, _end - _pos);
                    _buf.AsSpan(_pos, c).CopyTo(dst);
                    _pos += c;
                    dst = dst.Slice(c);
                }
            }
        }

        public void DecodeImage(byte[] pixels, bool strip, int outRowBytes)
        {
            PngInfo info = Info;
            int w = info.Width, h = info.Height;
            int pixBits = info.BitDepth * info.Channels;
            int bpp = Math.Max(1, pixBits >> 3);

            long total = 0;
            if (info.Interlace == 0)
            {
                total = (long)h * (1 + info.RowBytes);
            }
            else
            {
                for (int pass = 0; pass < 7; pass++)
                {
                    int pw = (w + s_xinc[pass] - 1 - s_xoff[pass]) / s_xinc[pass];
                    int ph = (h + s_yinc[pass] - 1 - s_yoff[pass]) / s_yinc[pass];
                    if (pw > 0 && ph > 0)
                        total += (long)ph * (1 + ((pw * pixBits + 7) >> 3));
                }
            }

            var idat = new IdatStream(this, _pendingLen);
            _hasPending = false;
            InflateSource src;
            ZLibStream z;
            try
            {
                z = new ZLibStream(idat, CompressionMode.Decompress);
            }
            catch (InvalidDataException e)
            {
                throw new PngException("zlib error: " + e.Message);
            }
            using (z)
            {
                src = new InflateSource(z, total);

                if (info.Interlace == 0 && !strip)
                {
                    int rb = info.RowBytes;
                    var zero = new byte[rb];
                    for (int y = 0; y < h; y++)
                    {
                        int f = src.ReadByte();
                        Span<byte> row = pixels.AsSpan(y * rb, rb);
                        src.Read(row);
                        ReadOnlySpan<byte> prev = y == 0 ? zero : pixels.AsSpan((y - 1) * rb, rb);
                        PngFilter.Unfilter(f, row, prev, bpp);
                    }
                }
                else if (info.Interlace == 0)
                {
                    int rb = info.RowBytes;
                    var cur = new byte[rb];
                    var prev = new byte[rb];
                    for (int y = 0; y < h; y++)
                    {
                        int f = src.ReadByte();
                        src.Read(cur);
                        PngFilter.Unfilter(f, cur, prev, bpp);
                        Span<byte> o = pixels.AsSpan(y * outRowBytes, outRowBytes);
                        for (int j = 0; j < o.Length; j++) o[j] = cur[2 * j];
                        (cur, prev) = (prev, cur);
                    }
                }
                else
                {
                    DecodeInterlaced(src, pixels, strip, outRowBytes, bpp, pixBits);
                }

                // Pad bits of the last byte of each row are cleared (libpng leaves them
                // indeterminate). Done after unfiltering, as the next row's filter needs
                // them intact.
                int padBits = (w * pixBits) & 7;
                if (info.Interlace == 0 && !strip && padBits != 0)
                {
                    int rb = info.RowBytes;
                    byte keep = (byte)(0xff << (8 - padBits));
                    for (int y = 0; y < h; y++)
                        pixels[y * rb + rb - 1] &= keep;
                }

                // Consume the trailer of the zlib stream (Adler-32 check).
                try
                {
                    Span<byte> tail = stackalloc byte[64];
                    while (z.Read(tail) > 0) { }
                }
                catch (InvalidDataException e)
                {
                    throw new PngException("zlib error: " + e.Message);
                }
                if (idat.EofHit) throw new PngException("Not enough image data");
            }
            idat.Drain();
        }

        private void DecodeInterlaced(InflateSource src, byte[] pixels, bool strip, int outRowBytes, int bpp, int pixBits)
        {
            PngInfo info = Info;
            int w = info.Width, h = info.Height;
            int depth = info.BitDepth, ch = info.Channels;
            int maxRb = (w * pixBits + 7) >> 3;
            var cur = new byte[maxRb];
            var prev = new byte[maxRb];

            for (int pass = 0; pass < 7; pass++)
            {
                int xoff = s_xoff[pass], xinc = s_xinc[pass], yoff = s_yoff[pass], yinc = s_yinc[pass];
                int pw = (w + xinc - 1 - xoff) / xinc;
                int ph = (h + yinc - 1 - yoff) / yinc;
                if (pw == 0 || ph == 0) continue;
                int rb = (pw * pixBits + 7) >> 3;
                prev.AsSpan(0, rb).Clear();

                for (int r = 0; r < ph; r++)
                {
                    int f = src.ReadByte();
                    Span<byte> row = cur.AsSpan(0, rb);
                    src.Read(row);
                    PngFilter.Unfilter(f, row, prev.AsSpan(0, rb), bpp);

                    Span<byte> dst = pixels.AsSpan((yoff + r * yinc) * outRowBytes, outRowBytes);
                    if (depth < 8)
                    {
                        int mask = (1 << depth) - 1;
                        for (int i = 0; i < pw; i++)
                        {
                            int sb = i * depth;
                            int v = (row[sb >> 3] >> (8 - depth - (sb & 7))) & mask;
                            int db = (xoff + i * xinc) * depth;
                            dst[db >> 3] |= (byte)(v << (8 - depth - (db & 7)));
                        }
                    }
                    else if (strip)
                    {
                        for (int i = 0; i < pw; i++)
                        {
                            int d = (xoff + i * xinc) * ch;
                            int s = i * ch * 2;
                            for (int c = 0; c < ch; c++) dst[d + c] = row[s + 2 * c];
                        }
                    }
                    else
                    {
                        int ps = pixBits >> 3;
                        for (int i = 0; i < pw; i++)
                            row.Slice(i * ps, ps).CopyTo(dst.Slice((xoff + i * xinc) * ps, ps));
                    }
                    (cur, prev) = (prev, cur);
                }
            }
        }
    }
}
