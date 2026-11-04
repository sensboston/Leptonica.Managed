using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;

namespace Leptonica.Internal.Png;

/// <summary>CRC-32 (ISO 3309) as used by PNG chunks, hardware ARM64 CRC32 + Slicing-by-8.</summary>
internal static class PngCrc
{
    private static readonly uint[] s_table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[8 * 256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        for (int n = 0; n < 256; n++)
        {
            uint c = t[n];
            for (int k = 1; k < 8; k++)
            {
                c = t[c & 0xff] ^ (c >> 8);
                t[k * 256 + n] = c;
            }
        }
        return t;
    }

    /// <summary>Continues a CRC; start with 0 for a new one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        uint c = ~crc;
        int i = 0;
        int n = data.Length;
        ref byte src = ref MemoryMarshal.GetReference(data);

        if (Crc32.Arm64.IsSupported)
        {
            for (; i + 8 <= n; i += 8)
                c = Crc32.Arm64.ComputeCrc32(c, Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref src, (nuint)i)));
            for (; i < n; i++)
                c = Crc32.ComputeCrc32(c, Unsafe.Add(ref src, (nuint)i));
            return ~c;
        }

        ref uint t = ref MemoryMarshal.GetArrayDataReference(s_table);
        if (BitConverter.IsLittleEndian)
        {
            for (; i + 8 <= n; i += 8)
            {
                uint lo = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref src, (nuint)i)) ^ c;
                uint hi = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref src, (nuint)(i + 4)));
                c = Unsafe.Add(ref t, (nuint)(1792 + (lo & 0xff)))
                  ^ Unsafe.Add(ref t, (nuint)(1536 + ((lo >> 8) & 0xff)))
                  ^ Unsafe.Add(ref t, (nuint)(1280 + ((lo >> 16) & 0xff)))
                  ^ Unsafe.Add(ref t, (nuint)(1024 + (lo >> 24)))
                  ^ Unsafe.Add(ref t, (nuint)(768 + (hi & 0xff)))
                  ^ Unsafe.Add(ref t, (nuint)(512 + ((hi >> 8) & 0xff)))
                  ^ Unsafe.Add(ref t, (nuint)(256 + ((hi >> 16) & 0xff)))
                  ^ Unsafe.Add(ref t, (nuint)(hi >> 24));
            }
        }
        for (; i < n; i++)
            c = Unsafe.Add(ref t, (nuint)((c ^ Unsafe.Add(ref src, (nuint)i)) & 0xff)) ^ (c >> 8);
        return ~c;
    }
}
