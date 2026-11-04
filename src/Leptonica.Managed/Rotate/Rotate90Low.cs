using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Leptonica;

/*
 *  Low-level 90 degree rotations for 1 bpp and 32 bpp images.  They produce
 *  exactly the same pixels as the per-pixel loops of pixRotate90; the 32 bpp
 *  version walks the image in tiles, and the 1 bpp version transposes 32 x 32
 *  bit blocks.
 */
public sealed partial class Pix
{
    private const int Rot90Tile = 16;

    /// <summary>Rotates 32 bpp data by 90 degrees; wd and hd are the dest width and height.</summary>
    internal static void Rotate90Low32(uint[] datas, int wpls, uint[] datad, int wpld,
                                       int wd, int hd, bool clockwise)
    {
        ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
        ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
        for (int jb = 0; jb < wd; jb += Rot90Tile) {
            int jn = Math.Min(Rot90Tile, wd - jb);
            for (int ib = 0; ib < hd; ib += Rot90Tile) {
                int iend = Math.Min(hd, ib + Rot90Tile);
                for (int i = ib; i < iend; i++) {
                    ref uint dst = ref Unsafe.Add(ref baseD, (nint)i * wpld + jb);
                    if (clockwise) {
                        // dest (i, j) = src (wd - 1 - j, i)
                        ref uint src = ref Unsafe.Add(ref baseS, (nint)(wd - 1 - jb) * wpls + i);
                        for (int j = 0; j < jn; j++) {
                            Unsafe.Add(ref dst, j) = src;
                            src = ref Unsafe.Subtract(ref src, wpls);
                        }
                    } else {
                        // dest (i, j) = src (j, hd - 1 - i)
                        ref uint src = ref Unsafe.Add(ref baseS, (nint)jb * wpls + (hd - 1 - i));
                        for (int j = 0; j < jn; j++) {
                            Unsafe.Add(ref dst, j) = src;
                            src = ref Unsafe.Add(ref src, wpls);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Transposes a 32 x 32 bit matrix in place.  Row r is a[r]; column c of the
    /// row is bit 31 - c (the leftmost pixel is the most significant bit).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Transpose32(Span<uint> a)
    {
        uint m = 0x0000ffffu;
        for (int j = 16; j != 0; j >>= 1, m ^= m << j) {
            for (int k = 0; k < 32; k = (k + j + 1) & ~j) {
                uint t = (a[k] ^ (a[k + j] >> j)) & m;
                a[k] ^= t;
                a[k + j] ^= t << j;
            }
        }
    }

    /// <summary>Rotates 1 bpp data by 90 degrees; wd and hd are the dest width and height.</summary>
    /// <remarks>
    /// The dest must be cleared.  Each 32 x 32 block of source bits becomes one block
    /// of dest bits; blocks that are all 0 are skipped.
    /// </remarks>
    internal static void Rotate90Low1(uint[] datas, int wpls, uint[] datad, int wpld,
                                      int wd, int hd, bool clockwise)
    {
        Span<uint> a = stackalloc uint[32];
        int nk = wpls;      // source word columns, i.e. blocks of 32 dest rows
        int njw = wpld;     // dest word columns, i.e. blocks of 32 source rows
        for (int jw = 0; jw < njw; jw++) {
            int bmax = Math.Min(32, wd - 32 * jw);
            for (int k = 0; k < nk; k++) {
                uint any = 0;
                for (int b = 0; b < bmax; b++) {
                    int j = 32 * jw + b;
                    int row = clockwise ? wd - 1 - j : j;
                    uint v = datas[row * wpls + k];
                    a[b] = v;
                    any |= v;
                }
                if (any == 0)
                    continue;
                for (int b = bmax; b < 32; b++)
                    a[b] = 0;
                Transpose32(a);
                int cmax = Math.Min(32, hd - 32 * k);
                for (int c = 0; c < cmax; c++) {
                    int i = 32 * k + c;
                    int drow = clockwise ? i : hd - 1 - i;
                    datad[drow * wpld + jw] = a[c];
                }
            }
        }
    }
}
