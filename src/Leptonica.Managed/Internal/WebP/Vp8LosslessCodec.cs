using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Leptonica.Internal.WebP;

/// <summary>
/// Pure managed RFC 9649 VP8L (WebP Lossless) decoder and encoder, including
/// headerless green-channel alpha plane decoding/encoding for ALPH chunks.
/// </summary>
internal static class Vp8LosslessCodec
{
    internal const byte Vp8lMagicByte = 0x2f;
    private const int NumLiteralCodes = 256;
    private const int NumLengthCodes = 24;
    private const int NumDistanceCodes = 40;
    private const int CodeLengthCodes = 19;
    private const int MaxAllowedCodeLength = 15;
    private const int DefaultCodeLength = 8;
    private const int HuffmanTableBits = 8;
    private const int LengthsTableBits = 7;
    private const int MaxCacheBits = 11;
    private const uint HashMul = 0x1e35a7bdU;
    private const int CodeToPlaneCodes = 120;
    private const uint ArgbBlack = 0xff000000U;

    private const int PredictorTransform = 0;
    private const int CrossColorTransform = 1;
    private const int SubtractGreenTransform = 2;
    private const int ColorIndexingTransform = 3;

    private static readonly int[] AlphabetSizes = { 280, 256, 256, 256, 40 };

    private static readonly byte[] CodeLengthCodeOrder =
    {
        17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15
    };

    private static readonly byte[] CodeLengthExtraBits = { 2, 3, 7 };
    private static readonly byte[] CodeLengthRepeatOffsets = { 3, 3, 11 };

    private static readonly byte[] CodeToPlane =
    {
        0x18, 0x07, 0x17, 0x19, 0x28, 0x06, 0x27, 0x29, 0x16, 0x1a,
        0x26, 0x2a, 0x38, 0x05, 0x37, 0x39, 0x15, 0x1b, 0x36, 0x3a,
        0x25, 0x2b, 0x48, 0x04, 0x47, 0x49, 0x14, 0x1c, 0x35, 0x3b,
        0x46, 0x4a, 0x24, 0x2c, 0x58, 0x45, 0x4b, 0x34, 0x3c, 0x03,
        0x57, 0x59, 0x13, 0x1d, 0x56, 0x5a, 0x23, 0x2d, 0x44, 0x4c,
        0x55, 0x5b, 0x33, 0x3d, 0x68, 0x02, 0x67, 0x69, 0x12, 0x1e,
        0x66, 0x6a, 0x22, 0x2e, 0x54, 0x5c, 0x43, 0x4d, 0x65, 0x6b,
        0x32, 0x3e, 0x78, 0x01, 0x77, 0x79, 0x53, 0x5d, 0x11, 0x1f,
        0x64, 0x6c, 0x42, 0x4e, 0x76, 0x7a, 0x21, 0x2f, 0x75, 0x7b,
        0x31, 0x3f, 0x63, 0x6d, 0x52, 0x5e, 0x00, 0x74, 0x7c, 0x41,
        0x4f, 0x10, 0x20, 0x62, 0x6e, 0x30, 0x73, 0x7d, 0x51, 0x5f,
        0x40, 0x72, 0x7e, 0x61, 0x6f, 0x50, 0x71, 0x7f, 0x60, 0x70,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SubSampleSize(int size, int bits) => (size + (1 << bits) - 1) >> bits;

    // -------------------------------------------------------------------------
    // LSB-first BitReader / BitWriter
    // -------------------------------------------------------------------------

    private ref struct Vp8lBitReader
    {
        private readonly ReadOnlySpan<byte> _buf;
        private int _pos;
        private ulong _val;
        private int _bits;
        public bool Eos;

        public Vp8lBitReader(ReadOnlySpan<byte> buf)
        {
            _buf = buf;
            _pos = 0;
            _val = 0;
            _bits = 0;
            Eos = false;
            Fill();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Fill()
        {
            while (_bits <= 56 && _pos < _buf.Length)
            {
                _val |= (ulong)_buf[_pos++] << _bits;
                _bits += 8;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint PeekBits(int n)
        {
            if (_bits < n)
                Fill();
            return (uint)(_val & ((1UL << n) - 1UL));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Consume(int n)
        {
            if (_bits < n)
            {
                Fill();
                if (_bits < n)
                {
                    Eos = true;
                    _val = 0;
                    _bits = 0;
                    return;
                }
            }
            _val >>= n;
            _bits -= n;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadBits(int n)
        {
            if (n == 0)
                return 0;
            uint v = PeekBits(n);
            Consume(n);
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBit() => (int)ReadBits(1);
    }

    private sealed class Vp8lBitWriter
    {
        private readonly List<byte> _bytes;
        private ulong _buf;
        private int _bits;

        public Vp8lBitWriter(int initialCapacity = 4096)
        {
            _bytes = new List<byte>(initialCapacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBits(uint value, int nBits)
        {
            if (nBits <= 0)
                return;
            _buf |= ((ulong)value & ((1UL << nBits) - 1UL)) << _bits;
            _bits += nBits;
            while (_bits >= 8)
            {
                _bytes.Add((byte)(_buf & 0xff));
                _buf >>= 8;
                _bits -= 8;
            }
        }

        public byte[] Finish()
        {
            if (_bits > 0)
            {
                _bytes.Add((byte)(_buf & 0xff));
                _buf = 0;
                _bits = 0;
            }
            return _bytes.ToArray();
        }
    }

    // -------------------------------------------------------------------------
    // ColorCache
    // -------------------------------------------------------------------------

    private sealed class ColorCache
    {
        private readonly uint[] _colors;
        private readonly int _hashShift;

        public ColorCache(int hashBits)
        {
            _colors = new uint[1 << hashBits];
            _hashShift = 32 - hashBits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Insert(uint argb)
        {
            _colors[(argb * HashMul) >> _hashShift] = argb;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Lookup(int key) => _colors[key];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetIndex(uint argb) => (int)((argb * HashMul) >> _hashShift);
    }

    // -------------------------------------------------------------------------
    // Two-level Huffman decode table + encoder canonical Huffman
    // -------------------------------------------------------------------------

    private readonly struct HuffmanCode
    {
        public readonly byte Bits;
        public readonly ushort Value;
        public HuffmanCode(byte bits, ushort value)
        {
            Bits = bits;
            Value = value;
        }
    }

    private sealed class HuffmanTable
    {
        private readonly HuffmanCode[] _arena;
        private readonly int _rootBits;

        private HuffmanTable(HuffmanCode[] arena, int rootBits)
        {
            _arena = arena;
            _rootBits = rootBits;
        }

        private static uint GetNextKey(uint key, int len)
        {
            uint step = 1u << (len - 1);
            while ((key & step) != 0)
                step >>= 1;
            return step != 0 ? (key & (step - 1)) + step : key;
        }

        private static void ReplicateValue(List<HuffmanCode> table, int baseIdx, int step, int end, HuffmanCode code)
        {
            int e = end;
            do
            {
                e -= step;
                table[baseIdx + e] = code;
            } while (e > 0);
        }

        private static int NextTableBitSize(ReadOnlySpan<int> count, int len, int rootBits)
        {
            int left = 1 << (len - rootBits);
            while (len < MaxAllowedCodeLength)
            {
                left -= count[len];
                if (left <= 0)
                    break;
                len++;
                left <<= 1;
            }
            return len - rootBits;
        }

        public static HuffmanTable? Build(ReadOnlySpan<int> codeLengths, int rootBits)
        {
            Span<int> count = stackalloc int[MaxAllowedCodeLength + 1];
            count.Clear();
            int numUsed = 0;
            for (int i = 0; i < codeLengths.Length; i++)
            {
                int cl = codeLengths[i];
                if ((uint)cl > MaxAllowedCodeLength)
                    return null;
                count[cl]++;
                if (cl > 0)
                    numUsed++;
            }
            if (numUsed == 0)
                return null;

            Span<int> offset = stackalloc int[MaxAllowedCodeLength + 1];
            offset.Clear();
            for (int len = 1; len < MaxAllowedCodeLength; len++)
            {
                if (count[len] > (1 << len))
                    return null;
                offset[len + 1] = offset[len] + count[len];
            }

            ushort[] sorted = new ushort[numUsed];
            Span<int> cursor = stackalloc int[MaxAllowedCodeLength + 1];
            offset.CopyTo(cursor);
            for (int sym = 0; sym < codeLengths.Length; sym++)
            {
                int cl = codeLengths[sym];
                if (cl > 0)
                    sorted[cursor[cl]++] = (ushort)sym;
            }

            int rootSize = 1 << rootBits;
            if (numUsed == 1)
            {
                HuffmanCode[] single = new HuffmanCode[rootSize];
                Array.Fill(single, new HuffmanCode(0, sorted[0]));
                return new HuffmanTable(single, rootBits);
            }

            List<HuffmanCode> arena = new List<HuffmanCode>(rootSize * 2);
            for (int i = 0; i < rootSize; i++)
                arena.Add(default);

            uint mask = (uint)(rootSize - 1);
            uint key = 0;
            int symbolIdx = 0;
            int numNodes = 1;
            int numOpen = 1;
            int tableBase = 0;
            int tableSize = rootSize;
            long low = -1;

            int step = 2;
            for (int len = 1; len <= rootBits; len++)
            {
                numOpen <<= 1;
                numNodes += numOpen;
                numOpen -= count[len];
                if (numOpen < 0)
                    return null;
                while (count[len] > 0)
                {
                    HuffmanCode code = new HuffmanCode((byte)len, sorted[symbolIdx++]);
                    ReplicateValue(arena, (int)key, step, tableSize, code);
                    key = GetNextKey(key, len);
                    count[len]--;
                }
                step <<= 1;
            }

            step = 2;
            for (int len = rootBits + 1; len <= MaxAllowedCodeLength; len++)
            {
                numOpen <<= 1;
                numNodes += numOpen;
                numOpen -= count[len];
                if (numOpen < 0)
                    return null;
                while (count[len] > 0)
                {
                    if ((long)(key & mask) != low)
                    {
                        tableBase += tableSize;
                        int tableBits = NextTableBitSize(count, len, rootBits);
                        tableSize = 1 << tableBits;
                        while (arena.Count < tableBase + tableSize)
                            arena.Add(default);
                        low = (long)(key & mask);
                        arena[(int)low] = new HuffmanCode(
                            (byte)(tableBits + rootBits),
                            (ushort)(tableBase - (int)(key & mask)));
                    }
                    HuffmanCode code = new HuffmanCode((byte)(len - rootBits), sorted[symbolIdx++]);
                    ReplicateValue(arena, tableBase + (int)(key >> rootBits), step, tableSize, code);
                    key = GetNextKey(key, len);
                    count[len]--;
                }
                step <<= 1;
            }

            if (numNodes != 2 * numUsed - 1)
                return null;

            return new HuffmanTable(arena.ToArray(), rootBits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadSymbol(ref Vp8lBitReader br)
        {
            int rootIndex = (int)br.PeekBits(_rootBits);
            HuffmanCode entry = _arena[rootIndex];
            if (entry.Bits <= _rootBits)
            {
                br.Consume(entry.Bits);
                return entry.Value;
            }
            br.Consume(_rootBits);
            int extraBits = entry.Bits - _rootBits;
            int subIndex = rootIndex + entry.Value + (int)br.PeekBits(extraBits);
            HuffmanCode leaf = _arena[subIndex];
            br.Consume(leaf.Bits);
            return leaf.Value;
        }
    }

    private static HuffmanTable? ReadHuffmanCode(ref Vp8lBitReader br, int alphabetSize)
    {
        int[] codeLengths = new int[alphabetSize];
        if (br.ReadBit() != 0)
        {
            int numSymbols = br.ReadBit() + 1;
            int firstBits = br.ReadBit() == 0 ? 1 : 8;
            int sym0 = (int)br.ReadBits(firstBits);
            if (sym0 >= alphabetSize)
                return null;
            codeLengths[sym0] = 1;
            if (numSymbols == 2)
            {
                int sym1 = (int)br.ReadBits(8);
                if (sym1 >= alphabetSize)
                    return null;
                codeLengths[sym1] = 1;
            }
        }
        else
        {
            int numCodeLengths = (int)br.ReadBits(4) + 4;
            if (numCodeLengths > CodeLengthCodes)
                return null;
            Span<int> clCodeLengths = stackalloc int[CodeLengthCodes];
            clCodeLengths.Clear();
            for (int i = 0; i < numCodeLengths; i++)
                clCodeLengths[CodeLengthCodeOrder[i]] = (int)br.ReadBits(3);

            HuffmanTable? metaTable = HuffmanTable.Build(clCodeLengths, LengthsTableBits);
            if (metaTable == null)
                return null;

            int maxSymbol = alphabetSize;
            if (br.ReadBit() != 0)
            {
                int lengthNBits = 2 + 2 * (int)br.ReadBits(3);
                maxSymbol = 2 + (int)br.ReadBits(lengthNBits);
                if (maxSymbol > alphabetSize)
                    return null;
            }

            int prevLen = DefaultCodeLength;
            int symbol = 0;
            while (symbol < alphabetSize && maxSymbol > 0)
            {
                maxSymbol--;
                int codeLen = metaTable.ReadSymbol(ref br);
                if (br.Eos)
                    return null;
                if (codeLen < 16)
                {
                    codeLengths[symbol++] = codeLen;
                    if (codeLen != 0)
                        prevLen = codeLen;
                }
                else
                {
                    int slot = codeLen - 16;
                    int repeat = (int)br.ReadBits(CodeLengthExtraBits[slot]) + CodeLengthRepeatOffsets[slot];
                    if (symbol + repeat > alphabetSize)
                        return null;
                    int fill = (codeLen == 16) ? prevLen : 0;
                    for (int r = 0; r < repeat; r++)
                        codeLengths[symbol++] = fill;
                }
            }
        }

        return HuffmanTable.Build(codeLengths, HuffmanTableBits);
    }

    // -------------------------------------------------------------------------
    // Pixel arithmetic + Inverse Transforms
    // -------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AddPixels(uint a, uint b)
    {
        uint alphaAndGreen = (a & 0xff00ff00U) + (b & 0xff00ff00U);
        uint redAndBlue = (a & 0x00ff00ffU) + (b & 0x00ff00ffU);
        return (alphaAndGreen & 0xff00ff00U) | (redAndBlue & 0x00ff00ffU);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint SubPixels(uint a, uint b)
    {
        uint alphaAndGreen = 0x00ff00ffU + (a & 0xff00ff00U) - (b & 0xff00ff00U);
        uint redAndBlue = 0xff00ff00U + (a & 0x00ff00ffU) - (b & 0x00ff00ffU);
        return (alphaAndGreen & 0xff00ff00U) | (redAndBlue & 0x00ff00ffU);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Average2(uint a, uint b) => (((a ^ b) & 0xfefefefeU) >> 1) + (a & b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Average3(uint a, uint b, uint c) => Average2(Average2(a, c), b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Average4(uint a, uint b, uint c, uint d) => Average2(Average2(a, b), Average2(c, d));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Clip255(int v) => (byte)Math.Clamp(v, 0, 255);

    private static uint ClampedAddSubtractFull(uint c0, uint c1, uint c2)
    {
        uint b = Clip255((int)(c0 & 0xff) + (int)(c1 & 0xff) - (int)(c2 & 0xff));
        uint g = Clip255((int)((c0 >> 8) & 0xff) + (int)((c1 >> 8) & 0xff) - (int)((c2 >> 8) & 0xff));
        uint r = Clip255((int)((c0 >> 16) & 0xff) + (int)((c1 >> 16) & 0xff) - (int)((c2 >> 16) & 0xff));
        uint a = Clip255((int)((c0 >> 24) & 0xff) + (int)((c1 >> 24) & 0xff) - (int)((c2 >> 24) & 0xff));
        return b | (g << 8) | (r << 16) | (a << 24);
    }

    private static uint ClampedAddSubtractHalf(uint c0, uint c1, uint c2)
    {
        uint ave = Average2(c0, c1);
        int b0 = (int)(ave & 0xff), b2 = (int)(c2 & 0xff);
        int g0 = (int)((ave >> 8) & 0xff), g2 = (int)((c2 >> 8) & 0xff);
        int r0 = (int)((ave >> 16) & 0xff), r2 = (int)((c2 >> 16) & 0xff);
        int a0 = (int)((ave >> 24) & 0xff), a2 = (int)((c2 >> 24) & 0xff);
        uint b = Clip255(b0 + (b0 - b2) / 2);
        uint g = Clip255(g0 + (g0 - g2) / 2);
        uint r = Clip255(r0 + (r0 - r2) / 2);
        uint a = Clip255(a0 + (a0 - a2) / 2);
        return b | (g << 8) | (r << 16) | (a << 24);
    }

    private static uint SelectPredictor(uint a, uint b, uint c)
    {
        int sum = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int av = (int)((a >> shift) & 0xff);
            int bv = (int)((b >> shift) & 0xff);
            int cv = (int)((c >> shift) & 0xff);
            sum += Math.Abs(bv - cv) - Math.Abs(av - cv);
        }
        return sum <= 0 ? a : b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PredictMode(uint mode, uint left, uint tl, uint t, uint tr)
    {
        return mode switch
        {
            1 => left,
            2 => t,
            3 => tr,
            4 => tl,
            5 => Average3(left, t, tr),
            6 => Average2(left, tl),
            7 => Average2(left, t),
            8 => Average2(tl, t),
            9 => Average2(t, tr),
            10 => Average4(left, tl, t, tr),
            11 => SelectPredictor(t, left, tl),
            12 => ClampedAddSubtractFull(left, t, tl),
            13 => ClampedAddSubtractHalf(left, t, tl),
            _ => ArgbBlack,
        };
    }

    private sealed class TransformSpec
    {
        public int Type;
        public int Bits;
        public int Width;
        public uint[]? Data;
    }

    private sealed class HuffmanGroup
    {
        public required HuffmanTable Green;
        public required HuffmanTable Red;
        public required HuffmanTable Blue;
        public required HuffmanTable Alpha;
        public required HuffmanTable Dist;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadPrefixValue(int symbol, ref Vp8lBitReader br)
    {
        if (symbol < 4)
            return symbol + 1;
        int extraBits = (symbol - 2) >> 1;
        int offset = (2 + (symbol & 1)) << extraBits;
        return offset + (int)br.ReadBits(extraBits) + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PlaneCodeToDistance(int xsize, int planeCode)
    {
        if (planeCode > CodeToPlaneCodes)
            return planeCode - CodeToPlaneCodes;
        int dc = CodeToPlane[planeCode - 1];
        int yoffset = dc >> 4;
        int xoffset = 8 - (dc & 0x0f);
        int dist = yoffset * xsize + xoffset;
        return dist >= 1 ? dist : 1;
    }

    // -------------------------------------------------------------------------
    // Public Decoder Entry Points
    // -------------------------------------------------------------------------

    public static bool TryGetHeader(ReadOnlySpan<byte> payload, out int width, out int height, out bool hasAlpha)
    {
        width = height = 0;
        hasAlpha = false;
        if (payload.Length < 5 || payload[0] != Vp8lMagicByte)
            return false;

        uint bits = (uint)payload[1]
                  | ((uint)payload[2] << 8)
                  | ((uint)payload[3] << 16)
                  | ((uint)payload[4] << 24);
        width = (int)(bits & 0x3fffU) + 1;
        height = (int)((bits >> 14) & 0x3fffU) + 1;
        hasAlpha = ((bits >> 28) & 1U) != 0;
        uint version = (bits >> 29) & 0x7U;
        return version == 0;
    }

    public static bool TryDecodeRgba(ReadOnlySpan<byte> payload, out byte[] rgba, out int width, out int height, out bool hasAlpha)
    {
        rgba = Array.Empty<byte>();
        if (!TryGetHeader(payload, out width, out height, out hasAlpha))
            return false;

        Vp8lBitReader br = new Vp8lBitReader(payload);
        br.ReadBits(8 + 14 + 14 + 1 + 3); // consume 5-byte VP8L header
        if (br.Eos)
            return false;

        uint[]? argb = DecodeImageStream(ref br, width, height, isLevel0: true);
        if (argb == null || argb.Length != width * height)
            return false;

        rgba = new byte[width * height * 4];
        for (int i = 0; i < argb.Length; i++)
        {
            uint p = argb[i];
            int idx = i * 4;
            rgba[idx] = (byte)((p >> 16) & 0xff);     // R
            rgba[idx + 1] = (byte)((p >> 8) & 0xff);  // G
            rgba[idx + 2] = (byte)(p & 0xff);         // B
            rgba[idx + 3] = (byte)((p >> 24) & 0xff); // A
        }
        return true;
    }

    public static byte[]? DecodeAlphaStream(ReadOnlySpan<byte> payload, int width, int height)
    {
        Vp8lBitReader br = new Vp8lBitReader(payload);
        uint[]? argb = DecodeImageStream(ref br, width, height, isLevel0: true);
        if (argb == null || argb.Length != width * height)
            return null;

        byte[] alpha = new byte[width * height];
        for (int i = 0; i < argb.Length; i++)
            alpha[i] = (byte)((argb[i] >> 8) & 0xff);
        return alpha;
    }

    private static uint[]? DecodeImageStream(ref Vp8lBitReader br, int xsize, int ysize, bool isLevel0)
    {
        List<TransformSpec>? transforms = isLevel0 ? new List<TransformSpec>(4) : null;
        int workW = xsize;

        if (isLevel0)
        {
            int seen = 0;
            while (br.ReadBit() != 0)
            {
                int ty = (int)br.ReadBits(2);
                if ((seen & (1 << ty)) != 0)
                    return null;
                seen |= 1 << ty;

                if (ty == PredictorTransform || ty == CrossColorTransform)
                {
                    int bits = (int)br.ReadBits(3) + 2;
                    int tw = SubSampleSize(workW, bits);
                    int th = SubSampleSize(ysize, bits);
                    uint[]? tileData = DecodeImageStream(ref br, tw, th, isLevel0: false);
                    if (tileData == null)
                        return null;
                    transforms!.Add(new TransformSpec { Type = ty, Bits = bits, Width = workW, Data = tileData });
                }
                else if (ty == SubtractGreenTransform)
                {
                    transforms!.Add(new TransformSpec { Type = ty, Width = workW });
                }
                else // ColorIndexingTransform
                {
                    int numColors = (int)br.ReadBits(8) + 1;
                    int bits = numColors > 16 ? 0 : (numColors > 4 ? 1 : (numColors > 2 ? 2 : 3));
                    uint[]? rawMap = DecodeImageStream(ref br, numColors, 1, isLevel0: false);
                    if (rawMap == null)
                        return null;
                    int finalColors = 1 << (8 >> bits);
                    uint[] palette = new uint[finalColors];
                    int n = Math.Min(numColors, Math.Min(finalColors, rawMap.Length));
                    if (n > 0)
                    {
                        palette[0] = rawMap[0];
                        for (int k = 1; k < n; k++)
                            palette[k] = AddPixels(palette[k - 1], rawMap[k]);
                    }
                    transforms!.Add(new TransformSpec { Type = ty, Bits = bits, Width = workW, Data = palette });
                    workW = SubSampleSize(workW, bits);
                }
            }
        }

        int cacheBits = 0;
        if (br.ReadBit() != 0)
        {
            cacheBits = (int)br.ReadBits(4);
            if (cacheBits < 1 || cacheBits > MaxCacheBits)
                return null;
        }

        ushort[]? entropyData = null;
        int entropyXSize = 0;
        int entropyBits = 0;
        int numGroupsMax = 1;
        int[]? groupRemap = null;
        int numUsedGroups = 1;

        if (isLevel0 && br.ReadBit() != 0)
        {
            entropyBits = (int)br.ReadBits(3) + 2;
            entropyXSize = SubSampleSize(workW, entropyBits);
            int entropyYSize = SubSampleSize(ysize, entropyBits);
            uint[]? entropyArgb = DecodeImageStream(ref br, entropyXSize, entropyYSize, isLevel0: false);
            if (entropyArgb == null)
                return null;

            ushort[] raw = new ushort[entropyArgb.Length];
            for (int i = 0; i < entropyArgb.Length; i++)
            {
                ushort g = (ushort)((entropyArgb[i] >> 8) & 0xffff);
                raw[i] = g;
                if (g + 1 > numGroupsMax)
                    numGroupsMax = g + 1;
            }
            groupRemap = new int[numGroupsMax];
            Array.Fill(groupRemap, -1);
            numUsedGroups = 0;
            entropyData = new ushort[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                int g = raw[i];
                if (groupRemap[g] < 0)
                    groupRemap[g] = numUsedGroups++;
                entropyData[i] = (ushort)groupRemap[g];
            }
        }

        int greenAlphabet = AlphabetSizes[0] + (cacheBits > 0 ? (1 << cacheBits) : 0);
        HuffmanGroup[] groups = new HuffmanGroup[numUsedGroups];
        for (int g = 0; g < numGroupsMax; g++)
        {
            HuffmanTable? green = ReadHuffmanCode(ref br, greenAlphabet);
            HuffmanTable? red = ReadHuffmanCode(ref br, AlphabetSizes[1]);
            HuffmanTable? blue = ReadHuffmanCode(ref br, AlphabetSizes[2]);
            HuffmanTable? alpha = ReadHuffmanCode(ref br, AlphabetSizes[3]);
            HuffmanTable? dist = ReadHuffmanCode(ref br, AlphabetSizes[4]);
            if (green == null || red == null || blue == null || alpha == null || dist == null)
                return null;

            int dstSlot = groupRemap != null ? groupRemap[g] : 0;
            if (dstSlot >= 0)
            {
                groups[dstSlot] = new HuffmanGroup
                {
                    Green = green,
                    Red = red,
                    Blue = blue,
                    Alpha = alpha,
                    Dist = dist
                };
            }
        }

        long totalLong = (long)workW * ysize;
        if (totalLong <= 0 || totalLong > int.MaxValue / 4)
            return null;
        int total = (int)totalLong;
        uint[] data = new uint[total];
        ColorCache? cache = cacheBits > 0 ? new ColorCache(cacheBits) : null;
        const int cacheOffset = NumLiteralCodes + NumLengthCodes;

        int pos = 0;
        while (pos < total)
        {
            HuffmanGroup grp = entropyData == null
                ? groups[0]
                : groups[entropyData[((pos / workW) >> entropyBits) * entropyXSize + ((pos % workW) >> entropyBits)]];

            int code = grp.Green.ReadSymbol(ref br);
            if (br.Eos)
                return null;

            if (code < NumLiteralCodes)
            {
                uint r = grp.Red.ReadSymbol(ref br);
                uint b = grp.Blue.ReadSymbol(ref br);
                uint a = grp.Alpha.ReadSymbol(ref br);
                if (br.Eos)
                    return null;
                uint pixel = (a << 24) | (r << 16) | ((uint)code << 8) | b;
                data[pos++] = pixel;
                cache?.Insert(pixel);
            }
            else if (code < cacheOffset)
            {
                int lengthSym = code - NumLiteralCodes;
                int length = ReadPrefixValue(lengthSym, ref br);
                int distSym = grp.Dist.ReadSymbol(ref br);
                int planeCode = ReadPrefixValue(distSym, ref br);
                if (br.Eos)
                    return null;
                int dist = PlaneCodeToDistance(workW, planeCode);
                if (dist > pos || pos + length > total)
                    return null;
                for (int k = 0; k < length; k++)
                {
                    uint p = data[pos - dist];
                    data[pos++] = p;
                    cache?.Insert(p);
                }
            }
            else
            {
                int key = code - cacheOffset;
                uint p = cache!.Lookup(key);
                data[pos++] = p;
                cache.Insert(p);
            }
        }

        if (transforms != null)
        {
            for (int t = transforms.Count - 1; t >= 0; t--)
                data = ApplyInverseTransform(data, transforms[t]);
        }
        return data;
    }

    private static uint[] ApplyInverseTransform(uint[] argb, TransformSpec tr)
    {
        switch (tr.Type)
        {
            case SubtractGreenTransform:
                for (int i = 0; i < argb.Length; i++)
                {
                    uint p = argb[i];
                    uint g = (p >> 8) & 0xff;
                    uint r = (((p >> 16) & 0xff) + g) & 0xff;
                    uint b = ((p & 0xff) + g) & 0xff;
                    argb[i] = (p & 0xff00ff00U) | (r << 16) | b;
                }
                return argb;

            case PredictorTransform:
            {
                int w = tr.Width;
                int h = argb.Length / w;
                int bits = tr.Bits;
                int tilesPerRow = SubSampleSize(w, bits);
                uint[] tileData = tr.Data!;
                argb[0] = AddPixels(argb[0], ArgbBlack);
                for (int x = 1; x < w; x++)
                    argb[x] = AddPixels(argb[x], argb[x - 1]);

                for (int y = 1; y < h; y++)
                {
                    int row = y * w;
                    argb[row] = AddPixels(argb[row], argb[row - w]);
                    int tileRow = (y >> bits) * tilesPerRow;
                    for (int x = 1; x < w; x++)
                    {
                        int i = row + x;
                        uint mode = (tileData[tileRow + (x >> bits)] >> 8) & 0x0f;
                        uint pred = PredictMode(mode, argb[i - 1], argb[i - w - 1], argb[i - w], argb[i - w + 1]);
                        argb[i] = AddPixels(argb[i], pred);
                    }
                }
                return argb;
            }

            case CrossColorTransform:
            {
                int w = tr.Width;
                int h = argb.Length / w;
                int bits = tr.Bits;
                int tilesPerRow = SubSampleSize(w, bits);
                uint[] tileData = tr.Data!;
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    int tileRow = (y >> bits) * tilesPerRow;
                    for (int x = 0; x < w; x++)
                    {
                        uint code = tileData[tileRow + (x >> bits)];
                        sbyte g2r = (sbyte)(code & 0xff);
                        sbyte g2b = (sbyte)((code >> 8) & 0xff);
                        sbyte r2b = (sbyte)((code >> 16) & 0xff);
                        uint p = argb[row + x];
                        sbyte green = (sbyte)((p >> 8) & 0xff);
                        int newRed = ((int)((p >> 16) & 0xff) + (((int)g2r * green) >> 5)) & 0xff;
                        int newBlue = (int)(p & 0xff) + (((int)g2b * green) >> 5);
                        newBlue = (newBlue + (((int)r2b * (sbyte)newRed) >> 5)) & 0xff;
                        argb[row + x] = (p & 0xff00ff00U) | ((uint)newRed << 16) | (uint)newBlue;
                    }
                }
                return argb;
            }

            case ColorIndexingTransform:
            {
                int dstW = tr.Width;
                int bits = tr.Bits;
                int srcW = SubSampleSize(dstW, bits);
                int h = argb.Length / srcW;
                uint[] palette = tr.Data!;
                uint[] dst = new uint[dstW * h];
                int bpp = 8 >> bits;
                if (bpp == 8)
                {
                    for (int y = 0; y < h; y++)
                    {
                        int srcRow = y * srcW;
                        int dstRow = y * dstW;
                        for (int x = 0; x < dstW; x++)
                        {
                            uint idx = (argb[srcRow + x] >> 8) & 0xff;
                            dst[dstRow + x] = idx < (uint)palette.Length ? palette[idx] : 0U;
                        }
                    }
                }
                else
                {
                    int ppb = 1 << bits;
                    int countMask = ppb - 1;
                    uint bitMask = (1u << bpp) - 1u;
                    for (int y = 0; y < h; y++)
                    {
                        int srcRow = y * srcW;
                        int dstRow = y * dstW;
                        int si = 0;
                        uint packed = 0;
                        for (int x = 0; x < dstW; x++)
                        {
                            if ((x & countMask) == 0)
                                packed = (argb[srcRow + si++] >> 8) & 0xff;
                            uint idx = packed & bitMask;
                            dst[dstRow + x] = idx < (uint)palette.Length ? palette[idx] : 0U;
                            packed >>= bpp;
                        }
                    }
                }
                return dst;
            }

            default:
                return argb;
        }
    }

    // -------------------------------------------------------------------------
    // Lossless VP8L Encoder
    // -------------------------------------------------------------------------

    public static byte[] EncodeRgba(ReadOnlySpan<byte> rgba, int width, int height, int stride)
    {
        uint[] argb = new uint[width * height];
        bool hasAlpha = false;
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = rgba.Slice(y * stride, width * 4);
            int dstBase = y * width;
            for (int x = 0; x < width; x++)
            {
                int k = x * 4;
                byte r = row[k];
                byte g = row[k + 1];
                byte b = row[k + 2];
                byte a = row[k + 3];
                if (a != 255)
                    hasAlpha = true;
                argb[dstBase + x] = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
            }
        }

        Vp8lBitWriter bw = new Vp8lBitWriter(width * height * 2 + 256);
        bw.WriteBits(Vp8lMagicByte, 8);
        bw.WriteBits((uint)(width - 1), 14);
        bw.WriteBits((uint)(height - 1), 14);
        bw.WriteBits(hasAlpha ? 1u : 0u, 1);
        bw.WriteBits(0u, 3); // version = 0

        EncodeImageStream(bw, argb, width, height, isLevel0: true);
        return bw.Finish();
    }

    public static byte[] EncodeAlphaStream(ReadOnlySpan<byte> alphaPlane, int width, int height)
    {
        uint[] argb = new uint[width * height];
        for (int i = 0; i < argb.Length; i++)
            argb[i] = (uint)alphaPlane[i] << 8;

        Vp8lBitWriter bw = new Vp8lBitWriter(width * height + 128);
        // Headerless level-0 VP8L stream: no transforms (ALPH header already filters plane),
        // optional color cache, single Huffman group, then tokenized pixels.
        bw.WriteBits(0, 1); // no transforms
        int cacheBits = argb.Length >= 64 ? 7 : 0;
        if (cacheBits > 0)
        {
            bw.WriteBits(1, 1);
            bw.WriteBits((uint)cacheBits, 4);
        }
        else
        {
            bw.WriteBits(0, 1);
        }
        bw.WriteBits(0, 1); // no meta-Huffman
        TokenizeAndWritePixels(bw, argb, width, cacheBits);
        return bw.Finish();
    }

    private static void EncodeImageStream(Vp8lBitWriter bw, uint[] pixels, int width, int height, bool isLevel0)
    {
        uint[] working = pixels;
        if (isLevel0)
        {
            // Transform 1: SubtractGreen
            bw.WriteBits(1, 1);
            bw.WriteBits(SubtractGreenTransform, 2);
            uint[] sg = new uint[working.Length];
            for (int i = 0; i < working.Length; i++)
            {
                uint p = working[i];
                uint g = (p >> 8) & 0xff;
                uint r = (((p >> 16) & 0xff) - g) & 0xff;
                uint b = ((p & 0xff) - g) & 0xff;
                sg[i] = (p & 0xff00ff00U) | (r << 16) | b;
            }
            working = sg;

            // Transform 2: Spatial Predictor (tile bits = 4 -> 16x16 tiles)
            const int predBits = 4;
            bw.WriteBits(1, 1);
            bw.WriteBits(PredictorTransform, 2);
            bw.WriteBits((uint)(predBits - 2), 3);

            int tw = SubSampleSize(width, predBits);
            int th = SubSampleSize(height, predBits);
            uint[] tileModes = new uint[tw * th];
            for (int ty = 0; ty < th; ty++)
            {
                for (int tx = 0; tx < tw; tx++)
                {
                    uint bestMode = ChooseBestTileMode(working, width, height, predBits, tx, ty);
                    tileModes[ty * tw + tx] = bestMode << 8;
                }
            }
            EncodeImageStream(bw, tileModes, tw, th, isLevel0: false);

            uint[] residual = new uint[working.Length];
            residual[0] = SubPixels(working[0], ArgbBlack);
            for (int x = 1; x < width; x++)
                residual[x] = SubPixels(working[x], working[x - 1]);
            for (int y = 1; y < height; y++)
            {
                int row = y * width;
                residual[row] = SubPixels(working[row], working[row - width]);
                int tileRow = (y >> predBits) * tw;
                for (int x = 1; x < width; x++)
                {
                    int i = row + x;
                    uint mode = (tileModes[tileRow + (x >> predBits)] >> 8) & 0x0f;
                    uint pred = PredictMode(mode, working[i - 1], working[i - width - 1], working[i - width], working[i - width + 1]);
                    residual[i] = SubPixels(working[i], pred);
                }
            }
            working = residual;

            // End of transform list
            bw.WriteBits(0, 1);
        }

        // Color cache (7 bits = 128 slots on top-level images >= 64 px)
        int cacheBits = (isLevel0 && working.Length >= 64) ? 7 : 0;
        if (cacheBits > 0)
        {
            bw.WriteBits(1, 1);
            bw.WriteBits((uint)cacheBits, 4);
        }
        else
        {
            bw.WriteBits(0, 1);
        }

        // Single Huffman group (meta-Huffman = 0 on level 0)
        if (isLevel0)
            bw.WriteBits(0, 1);

        // Tokenize pixels with LZ77 + ColorCache
        TokenizeAndWritePixels(bw, working, width, cacheBits);
    }

    private static uint ChooseBestTileMode(uint[] argb, int width, int height, int bits, int tx, int ty)
    {
        // Fast SAD evaluation across core modes {1 (L), 2 (T), 7 (avg L,T), 11 (Select), 12 (ClampedGrad)}
        ReadOnlySpan<uint> candidateModes = stackalloc uint[] { 1, 2, 7, 11, 12 };
        int tile = 1 << bits;
        int x0 = Math.Max(1, tx << bits);
        int y0 = Math.Max(1, ty << bits);
        int x1 = Math.Min(width, (tx << bits) + tile);
        int y1 = Math.Min(height, (ty << bits) + tile);
        if (x0 >= x1 || y0 >= y1)
            return 1;

        uint bestMode = 1;
        long bestCost = long.MaxValue;
        foreach (uint m in candidateModes)
        {
            long cost = 0;
            for (int y = y0; y < y1; y++)
            {
                int row = y * width;
                for (int x = x0; x < x1; x++)
                {
                    int i = row + x;
                    uint diff = SubPixels(argb[i], PredictMode(m, argb[i - 1], argb[i - width - 1], argb[i - width], argb[i - width + 1]));
                    for (int s = 0; s < 32; s += 8)
                    {
                        int d = (int)((diff >> s) & 0xff);
                        cost += Math.Min(d, 256 - d);
                    }
                }
            }
            if (cost < bestCost)
            {
                bestCost = cost;
                bestMode = m;
            }
        }
        return bestMode;
    }

    private readonly struct EncodedToken
    {
        public readonly ushort GreenOrLenOrCache;
        public readonly byte Red;
        public readonly byte Blue;
        public readonly byte Alpha;
        public readonly byte LenExtraBits;
        public readonly ushort LenExtraVal;
        public readonly byte DistSym;
        public readonly byte DistExtraBits;
        public readonly uint DistExtraVal;

        public EncodedToken(ushort sym, byte r, byte b, byte a)
        {
            GreenOrLenOrCache = sym;
            Red = r;
            Blue = b;
            Alpha = a;
            LenExtraBits = 0;
            LenExtraVal = 0;
            DistSym = 0;
            DistExtraBits = 0;
            DistExtraVal = 0;
        }

        public EncodedToken(ushort lenSym, byte lenExtraBits, ushort lenExtraVal, byte distSym, byte distExtraBits, uint distExtraVal)
        {
            GreenOrLenOrCache = lenSym;
            Red = Blue = Alpha = 0;
            LenExtraBits = lenExtraBits;
            LenExtraVal = lenExtraVal;
            DistSym = distSym;
            DistExtraBits = distExtraBits;
            DistExtraVal = distExtraVal;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PrefixEncode(int value, out int symbol, out int extraBits, out uint extraVal)
    {
        if (value <= 4)
        {
            symbol = value - 1;
            extraBits = 0;
            extraVal = 0;
            return;
        }
        uint d = (uint)(value - 1);
        int highest = BitOperations.Log2(d);
        int second = (int)((d >> (highest - 1)) & 1U);
        extraBits = highest - 1;
        extraVal = d & ((1u << extraBits) - 1u);
        symbol = 2 * highest + second;
    }

    private static int DistanceToPlaneCode(int xsize, int dist, ReadOnlySpan<(int dist, int code)> nearMap)
    {
        for (int i = 0; i < nearMap.Length; i++)
        {
            if (nearMap[i].dist == dist)
                return nearMap[i].code;
        }
        return dist + CodeToPlaneCodes;
    }

    private static void TokenizeAndWritePixels(Vp8lBitWriter bw, uint[] pixels, int width, int cacheBits)
    {
        int n = pixels.Length;
        int greenAlphabet = 280 + (cacheBits > 0 ? (1 << cacheBits) : 0);
        int[] histG = new int[greenAlphabet];
        int[] histR = new int[256];
        int[] histB = new int[256];
        int[] histA = new int[256];
        int[] histD = new int[40];

        Span<(int dist, int code)> nearMap = stackalloc (int, int)[CodeToPlaneCodes];
        for (int i = 0; i < CodeToPlaneCodes; i++)
            nearMap[i] = (PlaneCodeToDistance(width, i + 1), i + 1);

        ColorCache? cache = cacheBits > 0 ? new ColorCache(cacheBits) : null;
        bool[] cacheValid = cacheBits > 0 ? new bool[1 << cacheBits] : Array.Empty<bool>();

        const int hashBits = 15;
        int[] head = new int[1 << hashBits];
        int[] prev = new int[n];
        Array.Fill(head, -1);

        List<EncodedToken> tokens = new List<EncodedToken>(n);
        const int maxWindow = (1 << 20) - 120;

        int pos = 0;
        while (pos < n)
        {
            int bestLen = 0;
            int bestDist = 0;

            if (pos + 4 <= n)
            {
                int maxLen = Math.Min(4096, n - pos);
                uint h = ((pixels[pos] * 0x1e35a7bdU) ^ (pixels[pos + 1] * 0x9e3779b1U)) >> (32 - hashBits);
                int cand = head[h];
                int hops = 0;
                while (cand >= 0 && pos - cand <= maxWindow && hops < 24)
                {
                    hops++;
                    if (bestLen < maxLen && pixels[cand + bestLen] == pixels[pos + bestLen] && pixels[cand] == pixels[pos])
                    {
                        int len = 1;
                        while (len < maxLen && pixels[cand + len] == pixels[pos + len])
                            len++;
                        if (len >= 4 && len > bestLen)
                        {
                            bestLen = len;
                            bestDist = pos - cand;
                            if (bestLen == maxLen)
                                break;
                        }
                    }
                    cand = prev[cand];
                }
            }

            if (bestLen >= 4)
            {
                PrefixEncode(bestLen, out int lenSym, out int lenExtraBits, out uint lenExtraVal);
                int planeCode = DistanceToPlaneCode(width, bestDist, nearMap);
                PrefixEncode(planeCode, out int distSym, out int distExtraBits, out uint distExtraVal);

                ushort gSym = (ushort)(NumLiteralCodes + lenSym);
                tokens.Add(new EncodedToken(gSym, (byte)lenExtraBits, (ushort)lenExtraVal, (byte)distSym, (byte)distExtraBits, distExtraVal));
                histG[gSym]++;
                histD[distSym]++;

                for (int k = 0; k < bestLen; k++)
                {
                    int cur = pos + k;
                    if (cur + 1 < n)
                    {
                        uint hh = ((pixels[cur] * 0x1e35a7bdU) ^ (pixels[cur + 1] * 0x9e3779b1U)) >> (32 - hashBits);
                        prev[cur] = head[hh];
                        head[hh] = cur;
                    }
                    if (cache != null)
                    {
                        int idx = cache.GetIndex(pixels[cur]);
                        cache.Insert(pixels[cur]);
                        cacheValid[idx] = true;
                    }
                }
                pos += bestLen;
                continue;
            }

            uint px = pixels[pos];
            if (pos + 1 < n)
            {
                uint hh = ((px * 0x1e35a7bdU) ^ (pixels[pos + 1] * 0x9e3779b1U)) >> (32 - hashBits);
                prev[pos] = head[hh];
                head[hh] = pos;
            }

            if (cache != null)
            {
                int key = cache.GetIndex(px);
                if (cacheValid[key] && cache.Lookup(key) == px)
                {
                    ushort gSym = (ushort)(280 + key);
                    tokens.Add(new EncodedToken(gSym, 0, 0, 0));
                    histG[gSym]++;
                    cache.Insert(px);
                    pos++;
                    continue;
                }
                cache.Insert(px);
                cacheValid[key] = true;
            }

            byte a = (byte)((px >> 24) & 0xff);
            byte r = (byte)((px >> 16) & 0xff);
            byte g = (byte)((px >> 8) & 0xff);
            byte b = (byte)(px & 0xff);
            tokens.Add(new EncodedToken(g, r, b, a));
            histG[g]++;
            histR[r]++;
            histB[b]++;
            histA[a]++;
            pos++;
        }

        // Ensure every Huffman alphabet has >= 1 symbol so even unused Dist/Red/Blue/Alpha serialize cleanly
        EnsureNonEmptyHistogram(histG);
        EnsureNonEmptyHistogram(histR);
        EnsureNonEmptyHistogram(histB);
        EnsureNonEmptyHistogram(histA);
        EnsureNonEmptyHistogram(histD);

        int[] lenG = BuildCodeLengths(histG, MaxAllowedCodeLength);
        int[] lenR = BuildCodeLengths(histR, MaxAllowedCodeLength);
        int[] lenB = BuildCodeLengths(histB, MaxAllowedCodeLength);
        int[] lenA = BuildCodeLengths(histA, MaxAllowedCodeLength);
        int[] lenD = BuildCodeLengths(histD, MaxAllowedCodeLength);

        WriteHuffmanCode(bw, lenG);
        WriteHuffmanCode(bw, lenR);
        WriteHuffmanCode(bw, lenB);
        WriteHuffmanCode(bw, lenA);
        WriteHuffmanCode(bw, lenD);

        (uint code, int len)[] codesG = BuildEmitCodes(lenG);
        (uint code, int len)[] codesR = BuildEmitCodes(lenR);
        (uint code, int len)[] codesB = BuildEmitCodes(lenB);
        (uint code, int len)[] codesA = BuildEmitCodes(lenA);
        (uint code, int len)[] codesD = BuildEmitCodes(lenD);

        for (int i = 0; i < tokens.Count; i++)
        {
            EncodedToken tk = tokens[i];
            var cg = codesG[tk.GreenOrLenOrCache];
            bw.WriteBits(cg.code, cg.len);

            if (tk.GreenOrLenOrCache < NumLiteralCodes)
            {
                var cr = codesR[tk.Red];
                var cb = codesB[tk.Blue];
                var ca = codesA[tk.Alpha];
                bw.WriteBits(cr.code, cr.len);
                bw.WriteBits(cb.code, cb.len);
                bw.WriteBits(ca.code, ca.len);
            }
            else if (tk.GreenOrLenOrCache < 280)
            {
                bw.WriteBits(tk.LenExtraVal, tk.LenExtraBits);
                var cd = codesD[tk.DistSym];
                bw.WriteBits(cd.code, cd.len);
                bw.WriteBits(tk.DistExtraVal, tk.DistExtraBits);
            }
        }
    }

    private static void EnsureNonEmptyHistogram(int[] hist)
    {
        for (int i = 0; i < hist.Length; i++)
        {
            if (hist[i] != 0)
                return;
        }
        hist[0] = 1;
    }

    private static int[] BuildCodeLengths(int[] histogram, int limit)
    {
        int numUsed = 0;
        int firstUsed = -1;
        for (int i = 0; i < histogram.Length; i++)
        {
            if (histogram[i] > 0)
            {
                numUsed++;
                if (firstUsed < 0)
                    firstUsed = i;
            }
        }

        int[] lengths = new int[histogram.Length];
        if (numUsed == 0)
            return lengths;
        if (numUsed == 1)
        {
            lengths[firstUsed] = 1;
            return lengths;
        }

        long countMin = 1;
        while (true)
        {
            int[] candidate = BuildHuffmanLengthsWithFloor(histogram, countMin);
            int maxLen = 0;
            for (int i = 0; i < candidate.Length; i++)
                if (candidate[i] > maxLen)
                    maxLen = candidate[i];
            if (maxLen <= limit)
                return candidate;
            countMin <<= 1;
        }
    }

    private struct HuffBuildNode
    {
        public long Count;
        public int Left;
        public int Right;
        public int Symbol;
    }

    private static int[] BuildHuffmanLengthsWithFloor(int[] histogram, long countMin)
    {
        List<HuffBuildNode> pool = new List<HuffBuildNode>(histogram.Length * 2);
        List<int> active = new List<int>(histogram.Length);
        for (int sym = 0; sym < histogram.Length; sym++)
        {
            if (histogram[sym] > 0)
            {
                active.Add(pool.Count);
                pool.Add(new HuffBuildNode
                {
                    Count = Math.Max(histogram[sym], countMin),
                    Left = -1,
                    Right = -1,
                    Symbol = sym
                });
            }
        }

        while (active.Count > 1)
        {
            int min1 = 0, min2 = 1;
            if (CompareNodes(pool, active[1], active[0]) < 0)
            {
                min1 = 1;
                min2 = 0;
            }
            for (int p = 2; p < active.Count; p++)
            {
                if (CompareNodes(pool, active[p], active[min1]) < 0)
                {
                    min2 = min1;
                    min1 = p;
                }
                else if (CompareNodes(pool, active[p], active[min2]) < 0)
                {
                    min2 = p;
                }
            }

            int a = active[min1];
            int b = active[min2];
            int parent = pool.Count;
            pool.Add(new HuffBuildNode
            {
                Count = pool[a].Count + pool[b].Count,
                Left = a,
                Right = b,
                Symbol = -1
            });

            int hi = Math.Max(min1, min2);
            int lo = Math.Min(min1, min2);
            active.RemoveAt(hi);
            active.RemoveAt(lo);
            active.Add(parent);
        }

        int[] lengths = new int[histogram.Length];
        Stack<(int idx, int depth)> stack = new Stack<(int, int)>();
        stack.Push((active[0], 0));
        while (stack.Count > 0)
        {
            var (idx, depth) = stack.Pop();
            HuffBuildNode node = pool[idx];
            if (node.Symbol < 0)
            {
                stack.Push((node.Left, depth + 1));
                stack.Push((node.Right, depth + 1));
            }
            else
            {
                lengths[node.Symbol] = depth;
            }
        }
        return lengths;
    }

    private static int CompareNodes(List<HuffBuildNode> pool, int a, int b)
    {
        int c = pool[a].Count.CompareTo(pool[b].Count);
        return c != 0 ? c : a.CompareTo(b);
    }

    private static uint ReverseBits(uint v, int len)
    {
        uint res = 0;
        for (int i = 0; i < len; i++)
            res |= ((v >> i) & 1u) << (len - 1 - i);
        return res;
    }

    private static (uint code, int len)[] BuildEmitCodes(int[] lengths)
    {
        int used = 0;
        int maxLen = 0;
        for (int i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] > 0)
            {
                used++;
                if (lengths[i] > maxLen)
                    maxLen = lengths[i];
            }
        }

        (uint code, int len)[] emit = new (uint, int)[lengths.Length];
        if (used <= 1)
            return emit; // single-symbol alphabet emits 0 bits per occurrence!

        Span<uint> count = stackalloc uint[maxLen + 1];
        count.Clear();
        for (int i = 0; i < lengths.Length; i++)
            if (lengths[i] > 0)
                count[lengths[i]]++;

        Span<uint> next = stackalloc uint[maxLen + 2];
        next.Clear();
        uint code = 0;
        for (int len = 1; len <= maxLen; len++)
        {
            code = (code + count[len - 1]) << 1;
            next[len] = code;
        }

        for (int sym = 0; sym < lengths.Length; sym++)
        {
            int l = lengths[sym];
            if (l > 0)
            {
                uint c = next[l]++;
                emit[sym] = (ReverseBits(c, l), l);
            }
        }
        return emit;
    }

    private static void WriteHuffmanCode(Vp8lBitWriter bw, int[] lengths)
    {
        int count = 0;
        int sym0 = 0, sym1 = 0;
        for (int i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] != 0)
            {
                if (count == 0) sym0 = i;
                else if (count == 1) sym1 = i;
                count++;
                if (count == 3)
                    break;
            }
        }

        if (count <= 2 && sym0 < 256 && sym1 < 256)
        {
            bw.WriteBits(1, 1); // simple code
            bw.WriteBits(count == 2 ? 1u : 0u, 1);
            if (sym0 <= 1)
            {
                bw.WriteBits(0, 1);
                bw.WriteBits((uint)sym0, 1);
            }
            else
            {
                bw.WriteBits(1, 1);
                bw.WriteBits((uint)sym0, 8);
            }
            if (count == 2)
                bw.WriteBits((uint)sym1, 8);
            return;
        }

        bw.WriteBits(0, 1); // full code
        List<(int code, uint extra)> rle = CompressCodeLengths(lengths);
        int[] clHist = new int[CodeLengthCodes];
        for (int i = 0; i < rle.Count; i++)
            clHist[rle[i].code]++;
        EnsureNonEmptyHistogram(clHist);

        int[] clLengths = BuildCodeLengths(clHist, LengthsTableBits);
        int codesToStore = CodeLengthCodes;
        while (codesToStore > 4 && clLengths[CodeLengthCodeOrder[codesToStore - 1]] == 0)
            codesToStore--;

        bw.WriteBits((uint)(codesToStore - 4), 4);
        for (int i = 0; i < codesToStore; i++)
            bw.WriteBits((uint)clLengths[CodeLengthCodeOrder[i]], 3);

        var clEmit = BuildEmitCodes(clLengths);
        bw.WriteBits(0, 1); // no max_symbol trim

        for (int i = 0; i < rle.Count; i++)
        {
            var (c, ex) = rle[i];
            bw.WriteBits(clEmit[c].code, clEmit[c].len);
            if (c >= 16)
                bw.WriteBits(ex, CodeLengthExtraBits[c - 16]);
        }
    }

    private static List<(int code, uint extra)> CompressCodeLengths(int[] lengths)
    {
        List<(int code, uint extra)> tokens = new List<(int, uint)>(lengths.Length);
        int prevVal = DefaultCodeLength;
        int i = 0;
        while (i < lengths.Length)
        {
            int val = lengths[i];
            int k = i + 1;
            while (k < lengths.Length && lengths[k] == val)
                k++;
            int runs = k - i;
            if (val == 0)
            {
                while (runs >= 1)
                {
                    if (runs < 3)
                    {
                        for (int r = 0; r < runs; r++)
                            tokens.Add((0, 0));
                        break;
                    }
                    if (runs < 11)
                    {
                        tokens.Add((17, (uint)(runs - 3)));
                        break;
                    }
                    if (runs < 139)
                    {
                        tokens.Add((18, (uint)(runs - 11)));
                        break;
                    }
                    tokens.Add((18, 0x7f));
                    runs -= 138;
                }
            }
            else
            {
                if (val != prevVal)
                {
                    tokens.Add((val, 0));
                    runs--;
                }
                while (runs >= 1)
                {
                    if (runs < 3)
                    {
                        for (int r = 0; r < runs; r++)
                            tokens.Add((val, 0));
                        break;
                    }
                    if (runs < 7)
                    {
                        tokens.Add((16, (uint)(runs - 3)));
                        break;
                    }
                    tokens.Add((16, 3));
                    runs -= 6;
                }
                prevVal = val;
            }
            i = k;
        }
        return tokens;
    }
}
