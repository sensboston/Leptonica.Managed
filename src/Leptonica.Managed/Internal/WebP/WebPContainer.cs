using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Leptonica.Internal.WebP;

/// <summary>
/// RIFF/WEBP container parser and muxer supporting simple VP8, simple VP8L,
/// extended VP8X + ALPH, and animated VP8X + ANIM + ANMF streams.
/// </summary>
internal static class WebPContainer
{
    private const byte Vp8xFlagAlpha = 0x10;
    private const byte Vp8xFlagAnim = 0x02;

    public static bool TryGetFeatures(ReadOnlySpan<byte> data, out int width, out int height, out bool hasAlpha)
    {
        width = height = 0;
        hasAlpha = false;
        if (data.Length < 20)
            return false;

        if (data[0] != (byte)'R' || data[1] != (byte)'I' || data[2] != (byte)'F' || data[3] != (byte)'F' ||
            data[8] != (byte)'W' || data[9] != (byte)'E' || data[10] != (byte)'B' || data[11] != (byte)'P')
        {
            return false;
        }

        uint fourCc = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
        int chunkLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4));
        if (chunkLen < 0)
            return false;
        ReadOnlySpan<byte> payload = data.Slice(20, Math.Min(chunkLen, data.Length - 20));

        // 'VP8 ' = 0x20385056
        if (fourCc == 0x20385056U)
        {
            hasAlpha = false;
            return Vp8LossyCodec.TryGetHeader(payload, out width, out height);
        }

        // 'VP8L' = 0x4c385056
        if (fourCc == 0x4c385056U)
        {
            return Vp8LosslessCodec.TryGetHeader(payload, out width, out height, out hasAlpha);
        }

        // 'VP8X' = 0x58385056
        if (fourCc == 0x58385056U)
        {
            if (payload.Length < 10)
                return false;
            byte flags = payload[0];
            hasAlpha = (flags & Vp8xFlagAlpha) != 0;
            width = (payload[4] | (payload[5] << 8) | (payload[6] << 16)) + 1;
            height = (payload[7] | (payload[8] << 8) | (payload[9] << 16)) + 1;
            return width > 0 && width <= 16384 && height > 0 && height <= 16384;
        }

        return false;
    }

    public static bool TryDecodeRgba(ReadOnlySpan<byte> data, out byte[] rgba, out int width, out int height, out bool hasAlpha)
    {
        rgba = Array.Empty<byte>();
        if (!TryGetFeatures(data, out width, out height, out hasAlpha))
            return false;

        uint riffSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
        int limit = (int)Math.Min((long)data.Length, (long)riffSize + 8L);
        if (limit < 20)
            return false;

        int pos = 12;
        byte[]? alphChunk = null;
        while (pos + 8 <= limit)
        {
            uint fourCc = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos, 4));
            int chunkLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos + 4, 4));
            pos += 8;
            if (chunkLen < 0 || pos + chunkLen > data.Length)
                return false;

            ReadOnlySpan<byte> payload = data.Slice(pos, chunkLen);
            pos += chunkLen + (chunkLen & 1);

            if (fourCc == 0x48504c41U) // 'ALPH'
            {
                alphChunk = payload.ToArray();
            }
            else if (fourCc == 0x20385056U) // 'VP8 '
            {
                if (!Vp8LossyCodec.TryDecodeRgba(payload, out rgba, out int fw, out int fh))
                    return false;
                width = fw;
                height = fh;
                if (alphChunk != null)
                {
                    if (!ApplyAlphaChunk(alphChunk, width, height, rgba))
                        return false;
                    hasAlpha = true;
                }
                return true;
            }
            else if (fourCc == 0x4c385056U) // 'VP8L'
            {
                return Vp8LosslessCodec.TryDecodeRgba(payload, out rgba, out width, out height, out hasAlpha);
            }
            else if (fourCc == 0x464d4e41U) // 'ANMF' (animated WebP: decode first frame onto canvas)
            {
                return DecodeFirstAnimFrame(payload, width, height, out rgba);
            }
        }
        return false;
    }

    private static bool DecodeFirstAnimFrame(ReadOnlySpan<byte> anmfPayload, int canvasW, int canvasH, out byte[] canvasRgba)
    {
        canvasRgba = Array.Empty<byte>();
        if (anmfPayload.Length < 16)
            return false;

        int fx = (anmfPayload[0] | (anmfPayload[1] << 8) | (anmfPayload[2] << 16)) * 2;
        int fy = (anmfPayload[3] | (anmfPayload[4] << 8) | (anmfPayload[5] << 16)) * 2;
        int fw = (anmfPayload[6] | (anmfPayload[7] << 8) | (anmfPayload[8] << 16)) + 1;
        int fh = (anmfPayload[9] | (anmfPayload[10] << 8) | (anmfPayload[11] << 16)) + 1;

        int pos = 16;
        byte[]? alph = null;
        byte[]? frameRgba = null;
        while (pos + 8 <= anmfPayload.Length)
        {
            uint fourCc = BinaryPrimitives.ReadUInt32LittleEndian(anmfPayload.Slice(pos, 4));
            int sz = (int)BinaryPrimitives.ReadUInt32LittleEndian(anmfPayload.Slice(pos + 4, 4));
            pos += 8;
            if (sz < 0 || pos + sz > anmfPayload.Length)
                return false;
            ReadOnlySpan<byte> sub = anmfPayload.Slice(pos, sz);
            pos += sz + (sz & 1);

            if (fourCc == 0x48504c41U) // 'ALPH'
            {
                alph = sub.ToArray();
            }
            else if (fourCc == 0x20385056U) // 'VP8 '
            {
                if (!Vp8LossyCodec.TryDecodeRgba(sub, out frameRgba, out int dw, out int dh) || dw != fw || dh != fh)
                    return false;
                if (alph != null && !ApplyAlphaChunk(alph, fw, fh, frameRgba))
                    return false;
                break;
            }
            else if (fourCc == 0x4c385056U) // 'VP8L'
            {
                if (!Vp8LosslessCodec.TryDecodeRgba(sub, out frameRgba, out int dw, out int dh, out _) || dw != fw || dh != fh)
                    return false;
                break;
            }
        }

        if (frameRgba == null)
            return false;

        canvasRgba = new byte[canvasW * canvasH * 4];
        for (int y = 0; y < fh && fy + y < canvasH; y++)
        {
            int copyW = Math.Min(fw, canvasW - fx);
            if (copyW > 0)
            {
                frameRgba.AsSpan(y * fw * 4, copyW * 4)
                    .CopyTo(canvasRgba.AsSpan(((fy + y) * canvasW + fx) * 4, copyW * 4));
            }
        }
        return true;
    }

    private static bool ApplyAlphaChunk(ReadOnlySpan<byte> alphChunk, int width, int height, byte[] rgba)
    {
        if (alphChunk.Length < 1)
            return false;

        byte hdr = alphChunk[0];
        int method = hdr & 0x03;
        int filter = (hdr >> 2) & 0x03;
        int preproc = (hdr >> 4) & 0x03;
        if (method > 1 || preproc > 1 || (hdr >> 6) != 0)
            return false;

        ReadOnlySpan<byte> body = alphChunk.Slice(1);
        byte[]? plane;
        if (method == 0)
        {
            if (body.Length < width * height)
                return false;
            plane = body.Slice(0, width * height).ToArray();
        }
        else
        {
            plane = Vp8LosslessCodec.DecodeAlphaStream(body, width, height);
            if (plane == null)
                return false;
        }

        UnfilterAlphaPlane(filter, plane, width, height);
        for (int i = 0; i < plane.Length; i++)
            rgba[i * 4 + 3] = plane[i];
        return true;
    }

    private static void UnfilterAlphaPlane(int filter, byte[] plane, int width, int height)
    {
        if (filter == 0 || width <= 0 || height <= 0)
            return;

        // Row 0: horizontal running sum for all filters 1..3
        byte acc = 0;
        for (int x = 0; x < width; x++)
        {
            acc = (byte)(acc + plane[x]);
            plane[x] = acc;
        }

        for (int y = 1; y < height; y++)
        {
            int row = y * width;
            int prev = (y - 1) * width;
            if (filter == 1) // Horizontal
            {
                byte a = plane[prev];
                for (int x = 0; x < width; x++)
                {
                    a = (byte)(a + plane[row + x]);
                    plane[row + x] = a;
                }
            }
            else if (filter == 2) // Vertical
            {
                for (int x = 0; x < width; x++)
                    plane[row + x] = (byte)(plane[row + x] + plane[prev + x]);
            }
            else // Gradient
            {
                byte tl = plane[prev];
                byte left = tl;
                for (int x = 0; x < width; x++)
                {
                    byte top = plane[prev + x];
                    int grad = Math.Clamp((int)left + top - tl, 0, 255);
                    left = (byte)(plane[row + x] + grad);
                    tl = top;
                    plane[row + x] = left;
                }
            }
        }
    }

    private static byte[] EncodeAlphaChunk(ReadOnlySpan<byte> rgba, int width, int height, int stride)
    {
        byte[] rawAlpha = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = rgba.Slice(y * stride, width * 4);
            int dstRow = y * width;
            for (int x = 0; x < width; x++)
                rawAlpha[dstRow + x] = row[x * 4 + 3];
        }

        byte[] vp8lAlpha = Vp8LosslessCodec.EncodeAlphaStream(rawAlpha, width, height);
        if (vp8lAlpha.Length < rawAlpha.Length)
        {
            byte[] chunk = new byte[1 + vp8lAlpha.Length];
            chunk[0] = 0x01; // method = 1 (VP8L lossless), filter = 0
            Buffer.BlockCopy(vp8lAlpha, 0, chunk, 1, vp8lAlpha.Length);
            return chunk;
        }
        else
        {
            byte[] chunk = new byte[1 + rawAlpha.Length];
            chunk[0] = 0x00; // method = 0 (raw), filter = 0
            Buffer.BlockCopy(rawAlpha, 0, chunk, 1, rawAlpha.Length);
            return chunk;
        }
    }

    private static bool HasNonOpaqueAlpha(ReadOnlySpan<byte> rgba, int width, int height, int stride)
    {
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = rgba.Slice(y * stride, width * 4);
            for (int x = 0; x < width; x++)
            {
                if (row[x * 4 + 3] != 255)
                    return true;
            }
        }
        return false;
    }

    // -------------------------------------------------------------------------
    // Still & Animated WebP Encoding
    // -------------------------------------------------------------------------

    public static byte[] EncodeStillWebP(ReadOnlySpan<byte> rgba, int width, int height, int stride, int quality, bool lossless)
    {
        using MemoryStream ms = new MemoryStream();
        WriteRiffHeaderPlaceholder(ms);

        if (lossless)
        {
            byte[] vp8l = Vp8LosslessCodec.EncodeRgba(rgba, width, height, stride);
            WriteChunk(ms, "VP8L", vp8l);
        }
        else
        {
            bool hasAlpha = HasNonOpaqueAlpha(rgba, width, height, stride);
            byte[] vp8 = Vp8LossyCodec.EncodeVp8(rgba, width, height, stride, quality);
            if (!hasAlpha)
            {
                WriteChunk(ms, "VP8 ", vp8);
            }
            else
            {
                WriteChunk(ms, "VP8X", BuildVp8xPayload(Vp8xFlagAlpha, width, height));
                byte[] alph = EncodeAlphaChunk(rgba, width, height, stride);
                WriteChunk(ms, "ALPH", alph);
                WriteChunk(ms, "VP8 ", vp8);
            }
        }

        return FinalizeRiff(ms);
    }

    public static byte[] EncodeAnimatedWebP(IReadOnlyList<byte[]> framesRgba, int width, int height,
                                            int loopCount, int durationMs, int quality, bool lossless)
    {
        using MemoryStream ms = new MemoryStream();
        WriteRiffHeaderPlaceholder(ms);

        bool anyAlpha = false;
        int stride = width * 4;
        for (int i = 0; i < framesRgba.Count; i++)
        {
            if (HasNonOpaqueAlpha(framesRgba[i], width, height, stride))
            {
                anyAlpha = true;
                break;
            }
        }

        byte vp8xFlags = (byte)(Vp8xFlagAnim | (anyAlpha ? Vp8xFlagAlpha : 0));
        WriteChunk(ms, "VP8X", BuildVp8xPayload(vp8xFlags, width, height));

        // ANIM chunk: 4-byte background color (0 = transparent) + 2-byte loop count
        byte[] animPayload = new byte[6];
        ushort lc = (ushort)Math.Clamp(loopCount, 0, ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(animPayload.AsSpan(4, 2), lc);
        WriteChunk(ms, "ANIM", animPayload);

        int dur = Math.Clamp(durationMs, 0, 0x00ffffff);
        Span<byte> hdr = stackalloc byte[16];
        for (int i = 0; i < framesRgba.Count; i++)
        {
            byte[] frameRgba = framesRgba[i];
            using MemoryStream anmf = new MemoryStream();
            hdr.Clear();
            // x=0, y=0 (bytes 0..5 = 0)
            WriteU24Le(hdr.Slice(6, 3), width - 1);
            WriteU24Le(hdr.Slice(9, 3), height - 1);
            WriteU24Le(hdr.Slice(12, 3), dur);
            hdr[15] = 0x02; // do_not_blend = 1 (overwrite full frame), dispose = 0
            anmf.Write(hdr);

            if (lossless)
            {
                byte[] vp8l = Vp8LosslessCodec.EncodeRgba(frameRgba, width, height, stride);
                WriteChunk(anmf, "VP8L", vp8l);
            }
            else
            {
                if (HasNonOpaqueAlpha(frameRgba, width, height, stride))
                {
                    byte[] alph = EncodeAlphaChunk(frameRgba, width, height, stride);
                    WriteChunk(anmf, "ALPH", alph);
                }
                byte[] vp8 = Vp8LossyCodec.EncodeVp8(frameRgba, width, height, stride, quality);
                WriteChunk(anmf, "VP8 ", vp8);
            }

            WriteChunk(ms, "ANMF", anmf.ToArray());
        }

        return FinalizeRiff(ms);
    }

    private static byte[] BuildVp8xPayload(byte flags, int width, int height)
    {
        byte[] buf = new byte[10];
        buf[0] = flags;
        WriteU24Le(buf.AsSpan(4, 3), width - 1);
        WriteU24Le(buf.AsSpan(7, 3), height - 1);
        return buf;
    }

    private static void WriteU24Le(Span<byte> dst, int val)
    {
        dst[0] = (byte)(val & 0xff);
        dst[1] = (byte)((val >> 8) & 0xff);
        dst[2] = (byte)((val >> 16) & 0xff);
    }

    private static void WriteRiffHeaderPlaceholder(Stream s)
    {
        ReadOnlySpan<byte> hdr = "RIFF\0\0\0\0WEBP"u8;
        s.Write(hdr);
    }

    private static void WriteChunk(Stream s, string fourCc, byte[] payload)
    {
        Span<byte> hdr = stackalloc byte[8];
        Encoding.ASCII.GetBytes(fourCc, hdr.Slice(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.Slice(4, 4), (uint)payload.Length);
        s.Write(hdr);
        s.Write(payload, 0, payload.Length);
        if ((payload.Length & 1) != 0)
            s.WriteByte(0);
    }

    private static byte[] FinalizeRiff(MemoryStream ms)
    {
        byte[] bytes = ms.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)(bytes.Length - 8));
        return bytes;
    }
}
