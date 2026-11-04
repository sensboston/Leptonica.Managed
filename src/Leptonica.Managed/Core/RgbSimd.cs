using System;
using System.Runtime.Intrinsics;

namespace Leptonica;

/// <summary>Small vectorized helpers for rows of raster words.</summary>
/// <remarks>
/// The raster words are stored with the first pixel in the most significant
/// byte, so on a little-endian machine the bytes of a word are in memory in
/// reverse pixel order.
/// </remarks>
internal static class RgbSimd
{
    /// <summary>True if the vector paths can be used.</summary>
    internal static bool IsSupported => Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian;

    /// <summary>
    /// Copies the wpl words of a raster row to dst[doff..] with the pixels in
    /// memory order (byte k of the output is pixel k).  Writes whole words only.
    /// </summary>
    internal static void ToPixelOrder(uint[] src, int off, int wpl, byte[] dst, int doff)
    {
        int k = 0;
        if (IsSupported) {
            Vector128<byte> rev = Vector128.Create((byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12);
            for (; k + 4 <= wpl; k += 4) {
                Vector128<byte> v = Vector128.LoadUnsafe(ref src[off + k]).AsByte();
                Vector128.Shuffle(v, rev).StoreUnsafe(ref dst[doff + 4 * k]);
            }
        }
        for (; k < wpl; k++) {
            uint v = src[off + k];
            int p = doff + 4 * k;
            dst[p] = (byte)(v >> 24);
            dst[p + 1] = (byte)(v >> 16);
            dst[p + 2] = (byte)(v >> 8);
            dst[p + 3] = (byte)v;
        }
    }
}
