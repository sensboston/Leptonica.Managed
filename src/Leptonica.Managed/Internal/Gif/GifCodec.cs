using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Leptonica.Internal.Gif;

internal sealed class GifException : Exception
{
    public bool IsAnimatedUnsupported { get; }

    public GifException(string message, bool animatedUnsupported = false) : base(message)
    {
        IsAnimatedUnsupported = animatedUnsupported;
    }
}

internal readonly struct GifDecodedImage
{
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] PaletteRgb { get; init; } // 3 * ColorCount bytes
    public int ColorCount { get; init; }
    public byte[] Indices { get; init; }    // Width * Height bytes
}

internal static class GifCodec
{
    private static readonly int[] InterlacedOffset = { 0, 4, 2, 1 };
    private static readonly int[] InterlacedJumps = { 8, 8, 4, 2 };

    public static GifDecodedImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 13)
            throw new GifException("GIF data too short");

        // Header: "GIF87a" or "GIF89a"
        if (data[0] != (byte)'G' || data[1] != (byte)'I' || data[2] != (byte)'F' ||
            data[3] != (byte)'8' || (data[4] != (byte)'7' && data[4] != (byte)'9') ||
            data[5] != (byte)'a')
        {
            throw new GifException("Invalid GIF signature");
        }

        int pos = 6;
        // Logical Screen Descriptor
        byte screenPacked = data[pos + 4];
        pos += 7;

        byte[]? globalPalette = null;
        int globalColorCount = 0;
        if ((screenPacked & 0x80) != 0)
        {
            globalColorCount = 1 << ((screenPacked & 0x07) + 1);
            int palBytes = globalColorCount * 3;
            if (pos + palBytes > data.Length)
                throw new GifException("Truncated global color table");
            globalPalette = data.Slice(pos, palBytes).ToArray();
            pos += palBytes;
        }

        bool hasImage = false;
        int imgW = 0, imgH = 0;
        byte[]? imgPalette = null;
        int imgColorCount = 0;
        byte[]? imgPixels = null;

        while (pos < data.Length)
        {
            byte introducer = data[pos++];
            if (introducer == 0x3B) // Trailer
                break;

            if (introducer == 0x00)
                continue;

            if (introducer == 0x21) // Extension
            {
                if (pos >= data.Length)
                    break;
                pos++; // extension label
                SkipSubBlocks(data, ref pos);
                continue;
            }

            if (introducer == 0x2C) // Image Descriptor
            {
                if (hasImage)
                    throw new GifException("Animated GIF with multiple images is not supported", animatedUnsupported: true);

                if (pos + 9 > data.Length)
                    throw new GifException("Truncated image descriptor");

                int w = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos + 4, 2));
                int h = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos + 6, 2));
                byte imgPacked = data[pos + 8];
                pos += 9;

                if (w <= 0 || h <= 0)
                    throw new GifException("Invalid GIF image dimensions");

                byte[]? activePalette = globalPalette;
                int activeColorCount = globalColorCount;
                if ((imgPacked & 0x80) != 0)
                {
                    activeColorCount = 1 << ((imgPacked & 0x07) + 1);
                    int palBytes = activeColorCount * 3;
                    if (pos + palBytes > data.Length)
                        throw new GifException("Truncated local color table");
                    activePalette = data.Slice(pos, palBytes).ToArray();
                    pos += palBytes;
                }

                if (activePalette == null || activeColorCount <= 0)
                    throw new GifException("Missing GIF color map");

                long totalPixelsLong = (long)w * h;
                if (totalPixelsLong > 256L * 1024 * 1024)
                    throw new GifException("GIF dimensions exceed limits");
                int totalPixels = (int)totalPixelsLong;

                byte[] raster = DecodeLzw(data, ref pos, totalPixels);

                if ((imgPacked & 0x40) != 0)
                    raster = Deinterlace(raster, w, h);

                hasImage = true;
                imgW = w;
                imgH = h;
                imgPalette = activePalette;
                imgColorCount = activeColorCount;
                imgPixels = raster;
                continue;
            }

            throw new GifException($"Unexpected GIF block introducer 0x{introducer:X2}");
        }

        if (!hasImage || imgPixels == null || imgPalette == null)
            throw new GifException("No image found in GIF");

        return new GifDecodedImage
        {
            Width = imgW,
            Height = imgH,
            PaletteRgb = imgPalette,
            ColorCount = imgColorCount,
            Indices = imgPixels,
        };
    }

    private static void SkipSubBlocks(ReadOnlySpan<byte> data, ref int pos)
    {
        while (pos < data.Length)
        {
            int len = data[pos++];
            if (len == 0)
                return;
            if (pos + len > data.Length)
                throw new GifException("Truncated GIF sub-block");
            pos += len;
        }
    }

    private static byte[] DecodeLzw(ReadOnlySpan<byte> data, ref int pos, int expectedPixels)
    {
        if (pos >= data.Length)
            throw new GifException("Missing LZW minimum code size");

        int minCodeSize = data[pos++];
        if (minCodeSize < 1 || minCodeSize > 11)
            throw new GifException($"Invalid LZW minimum code size: {minCodeSize}");

        const int MaxStackSize = 4096;
        const int NullCode = -1;

        int clearCode = 1 << minCodeSize;
        int endCode = clearCode + 1;
        int avail = clearCode + 2;
        int oldCode = NullCode;
        int codeSize = minCodeSize + 1;
        int codeMask = (1 << codeSize) - 1;

        short[] prefix = new short[MaxStackSize];
        byte[] suffix = new byte[MaxStackSize];
        byte[] pixelStack = new byte[MaxStackSize + 1];
        for (int code = 0; code < clearCode; code++)
            suffix[code] = (byte)code;

        byte[] output = new byte[expectedPixels];
        int outIdx = 0;
        int top = 0;
        int first = 0;

        int datum = 0;
        int bits = 0;
        int subBlockRemaining = 0;
        bool reachedTerminator = false;
        bool sawEndCode = false;

        while (outIdx < expectedPixels && !sawEndCode)
        {
            while (bits < codeSize)
            {
                if (subBlockRemaining == 0)
                {
                    if (pos >= data.Length)
                        throw new GifException("Unexpected EOF in GIF LZW stream");
                    subBlockRemaining = data[pos++];
                    if (subBlockRemaining == 0)
                    {
                        reachedTerminator = true;
                        break;
                    }
                    if (pos + subBlockRemaining > data.Length)
                        throw new GifException("Truncated GIF LZW sub-block");
                }
                datum |= data[pos++] << bits;
                bits += 8;
                subBlockRemaining--;
            }

            if (bits < codeSize)
                break;

            int code = datum & codeMask;
            datum >>= codeSize;
            bits -= codeSize;

            if (code > avail || code == endCode)
            {
                if (code == endCode)
                {
                    sawEndCode = true;
                    break;
                }
                throw new GifException("Corrupt LZW code in GIF");
            }

            if (code == clearCode)
            {
                codeSize = minCodeSize + 1;
                codeMask = (1 << codeSize) - 1;
                avail = clearCode + 2;
                oldCode = NullCode;
                continue;
            }

            if (oldCode == NullCode)
            {
                output[outIdx++] = suffix[code];
                oldCode = code;
                first = code;
                continue;
            }

            int inCode = code;
            if (code == avail)
            {
                pixelStack[top++] = (byte)first;
                code = oldCode;
            }

            while (code >= clearCode)
            {
                if (top >= MaxStackSize)
                    throw new GifException("GIF LZW prefix chain overflow");
                pixelStack[top++] = suffix[code];
                code = prefix[code];
            }

            first = suffix[code];
            pixelStack[top++] = (byte)first;

            if (avail < MaxStackSize)
            {
                prefix[avail] = (short)oldCode;
                suffix[avail] = (byte)first;
                avail++;
                if ((avail & codeMask) == 0 && avail < MaxStackSize)
                {
                    codeSize++;
                    codeMask += avail;
                }
            }

            oldCode = inCode;

            while (top > 0 && outIdx < expectedPixels)
                output[outIdx++] = pixelStack[--top];
        }

        // Advance past remaining sub-block bytes and terminator 0x00
        if (!reachedTerminator)
        {
            pos += subBlockRemaining;
            SkipSubBlocks(data, ref pos);
        }

        if (outIdx < expectedPixels)
            throw new GifException("GIF LZW decoded fewer pixels than expected");

        return output;
    }

    private static byte[] Deinterlace(byte[] src, int w, int h)
    {
        byte[] dst = new byte[src.Length];
        int srow = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            for (int drow = InterlacedOffset[pass]; drow < h; drow += InterlacedJumps[pass], srow++)
            {
                if (srow < h)
                    Buffer.BlockCopy(src, srow * w, dst, drow * w, w);
            }
        }
        return dst;
    }

    public static void Encode(Stream output, int width, int height, ReadOnlySpan<byte> paletteRgb,
                              int colorTableBits, ReadOnlySpan<byte> indices, string? comment)
    {
        if (width <= 0 || height <= 0 || width > ushort.MaxValue || height > ushort.MaxValue)
            throw new GifException("Invalid GIF dimensions");

        // Header ("GIF89a") + Logical Screen Descriptor (13 bytes total)
        Span<byte> header = stackalloc byte[13];
        header[0] = (byte)'G';
        header[1] = (byte)'I';
        header[2] = (byte)'F';
        header[3] = (byte)'8';
        header[4] = (byte)'9';
        header[5] = (byte)'a';
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(6, 2), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(8, 2), (ushort)height);
        int sizeField = Math.Clamp(colorTableBits - 1, 0, 7);
        header[10] = (byte)(0x80 | (sizeField << 4) | sizeField);
        header[11] = 0; // background color index
        header[12] = 0; // aspect ratio
        output.Write(header);

        // Global Color Table (padded to 3 * (1 << (sizeField + 1)))
        int tableEntries = 1 << (sizeField + 1);
        int tableBytes = tableEntries * 3;
        if (paletteRgb.Length >= tableBytes)
        {
            output.Write(paletteRgb.Slice(0, tableBytes));
        }
        else
        {
            byte[] padded = new byte[tableBytes];
            paletteRgb.CopyTo(padded);
            output.Write(padded);
        }

        // Image Descriptor (10 bytes)
        Span<byte> imgDesc = stackalloc byte[10];
        imgDesc[0] = 0x2C;
        BinaryPrimitives.WriteUInt16LittleEndian(imgDesc.Slice(1, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(imgDesc.Slice(3, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(imgDesc.Slice(5, 2), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(imgDesc.Slice(7, 2), (ushort)height);
        imgDesc[9] = 0x00; // no local color table, not interlaced
        output.Write(imgDesc);

        // LZW Image Data
        int minCodeSize = Math.Max(2, sizeField + 1);
        EncodeLzw(output, minCodeSize, indices);

        // Optional Comment Extension (0x21 0xFE)
        if (!string.IsNullOrEmpty(comment))
        {
            byte[] commentBytes = Encoding.Latin1.GetBytes(comment);
            Span<byte> extHdr = stackalloc byte[2] { 0x21, 0xFE };
            output.Write(extHdr);
            int cpos = 0;
            while (cpos < commentBytes.Length)
            {
                int chunk = Math.Min(255, commentBytes.Length - cpos);
                output.WriteByte((byte)chunk);
                output.Write(commentBytes, cpos, chunk);
                cpos += chunk;
            }
            output.WriteByte(0x00);
        }

        // Trailer
        output.WriteByte(0x3B);
    }

    private static void EncodeLzw(Stream output, int minCodeSize, ReadOnlySpan<byte> pixels)
    {
        output.WriteByte((byte)minCodeSize);

        const int HashSize = 8192;
        const int HashMask = HashSize - 1;
        int[] hashKeys = new int[HashSize];
        short[] hashCodes = new short[HashSize];
        Array.Fill(hashKeys, -1);

        int clearCode = 1 << minCodeSize;
        int endCode = clearCode + 1;
        int nextCode = endCode + 1;
        int codeSize = minCodeSize + 1;

        byte[] subBlock = new byte[256];
        int subBlockLen = 0;
        int bitBuf = 0;
        int bitCount = 0;

        void FlushSubBlock()
        {
            if (subBlockLen > 0)
            {
                output.WriteByte((byte)subBlockLen);
                output.Write(subBlock, 0, subBlockLen);
                subBlockLen = 0;
            }
        }

        void EmitCode(int code)
        {
            bitBuf |= code << bitCount;
            bitCount += codeSize;
            while (bitCount >= 8)
            {
                subBlock[subBlockLen++] = (byte)(bitBuf & 0xFF);
                if (subBlockLen == 255)
                    FlushSubBlock();
                bitBuf >>= 8;
                bitCount -= 8;
            }
        }

        EmitCode(clearCode);

        if (pixels.Length > 0)
        {
            int ent = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
            {
                int c = pixels[i];
                int fcode = (ent << 8) | c;
                int slot = ((c << 5) ^ ent) & HashMask;

                bool found = false;
                while (true)
                {
                    int key = hashKeys[slot];
                    if (key == fcode)
                    {
                        ent = hashCodes[slot];
                        found = true;
                        break;
                    }
                    if (key < 0)
                        break;
                    slot = (slot + 1) & HashMask;
                }

                if (found)
                    continue;

                EmitCode(ent);
                ent = c;

                if (nextCode < 4096)
                {
                    hashKeys[slot] = fcode;
                    hashCodes[slot] = (short)nextCode;
                    if (nextCode == (1 << codeSize) && codeSize < 12)
                        codeSize++;
                    nextCode++;
                }
                else
                {
                    EmitCode(clearCode);
                    Array.Fill(hashKeys, -1);
                    codeSize = minCodeSize + 1;
                    nextCode = endCode + 1;
                }
            }

            EmitCode(ent);
        }

        EmitCode(endCode);
        if (bitCount > 0)
        {
            subBlock[subBlockLen++] = (byte)(bitBuf & 0xFF);
        }
        FlushSubBlock();
        output.WriteByte(0x00); // Block terminator
    }
}
