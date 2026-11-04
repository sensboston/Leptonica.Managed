using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Rop;

/// <summary>
/// Pixel-by-pixel reference for the rasterop family.  It does not share any code with the
/// word-oriented implementation: pixels are read and written one at a time with plain
/// MSB-first bit arithmetic, and every op code is evaluated bit by bit from its truth table.
/// </summary>
internal static class NaiveRop
{
    public static uint GetPixel(Pix pix, int x, int y)
    {
        int d = pix.Depth;
        long bitpos = (long)x * d;
        uint word = pix.Data[y * pix.Wpl + (int)(bitpos >> 5)];
        int shift = 32 - d - (int)(bitpos & 31);
        return (word >> shift) & PixelMask(d);
    }

    public static void SetPixel(Pix pix, int x, int y, uint val)
    {
        int d = pix.Depth;
        long bitpos = (long)x * d;
        int index = y * pix.Wpl + (int)(bitpos >> 5);
        int shift = 32 - d - (int)(bitpos & 31);
        uint mask = PixelMask(d) << shift;
        pix.Data[index] = (pix.Data[index] & ~mask) | ((val << shift) & mask);
    }

    private static uint PixelMask(int d) => d == 32 ? 0xffffffffu : (1u << d) - 1;

    /// <summary>
    /// Result bit = bit (2*s + d) of the op code, for each bit of the pixel
    /// (PIX_SRC = 1100: s=1,d=1 -> 1; s=1,d=0 -> 1; s=0,d=1 -> 0; s=0,d=0 -> 0).
    /// </summary>
    public static uint Combine(int op, uint s, uint d, int depth)
    {
        uint result = 0;
        for (int b = 0; b < depth; b++) {
            int sb = (int)((s >> b) & 1);
            int db = (int)((d >> b) & 1);
            int idx = 2 * sb + db;
            result |= (uint)((op >> idx) & 1) << b;
        }
        return result;
    }

    public static bool UsesSrc(int op) =>
        op != L.PIX_CLR && op != L.PIX_SET && op != L.PIX_NOT(L.PIX_DST) && op != L.PIX_DST;

    /// <summary>
    /// A pixel (j, i) of the dest rectangle is processed when it lies inside pixd and, for
    /// ops that read the source, its source pixel lies inside pixs.  The source is
    /// snapshotted first so that dest/src in the same image behave like a copy.
    /// </summary>
    public static void Rasterop(Pix pixd, int dx, int dy, int dw, int dh, int op,
                                Pix? pixs, int sx, int sy)
    {
        if (op == L.PIX_DST)
            return;
        bool useSrc = UsesSrc(op);
        Pix? src = useSrc ? Pix.Copy(null, pixs!) : null;
        int depth = pixd.Depth;
        for (int i = 0; i < dh; i++) {
            for (int j = 0; j < dw; j++) {
                int x = dx + j, y = dy + i;
                if (x < 0 || y < 0 || x >= pixd.Width || y >= pixd.Height)
                    continue;
                uint s = 0;
                if (src != null) {
                    int x2 = sx + j, y2 = sy + i;
                    if (x2 < 0 || y2 < 0 || x2 >= src.Width || y2 >= src.Height)
                        continue;
                    s = GetPixel(src, x2, y2);
                }
                SetPixel(pixd, x, y, Combine(op, s, GetPixel(pixd, x, y), depth));
            }
        }
    }

    /// <summary>Shifts the column band [x, x + w) down by shift rows; incoming pixels are 0.</summary>
    public static void Vip(Pix pix, int x, int w, int shift)
    {
        Pix old = Pix.Copy(null, pix);
        int x0 = Math.Max(x, 0);
        int x1 = Math.Min(x + w, pix.Width);
        for (int xx = x0; xx < x1; xx++) {
            for (int y = 0; y < pix.Height; y++) {
                int ys = y - shift;
                SetPixel(pix, xx, y, (ys >= 0 && ys < pix.Height) ? GetPixel(old, xx, ys) : 0);
            }
        }
    }

    /// <summary>
    /// Shifts the row band [y, y + h) right by shift pixels (left if negative).
    /// Pixels brought in at the left edge are 0.  Pixels brought in at the right edge
    /// are undefined at the low level (they come from the pad bits), so they are
    /// reported through <paramref name="unchecked"/>; pad bits are not compared.
    /// </summary>
    public static void Hip(Pix pix, int y, int h, int shift, bool[,] @unchecked)
    {
        Pix old = Pix.Copy(null, pix);
        int y0 = Math.Max(y, 0);
        int y1 = Math.Min(y + h, pix.Height);
        for (int yy = y0; yy < y1; yy++) {
            for (int x = 0; x < pix.Width; x++) {
                int xs = x - shift;
                if (xs >= 0 && xs < pix.Width)
                    SetPixel(pix, x, yy, GetPixel(old, xs, yy));
                else if (shift > 0)
                    SetPixel(pix, x, yy, 0);
                else
                    @unchecked[yy, x] = true;
            }
        }
    }

    /// <summary>Fills all words, pad bits included, with random data.</summary>
    public static Pix RandomPix(Random rng, int w, int h, int d)
    {
        Pix pix = Pix.Create(w, h, d);
        uint[] data = pix.Data;
        for (int i = 0; i < data.Length; i++)
            data[i] = (uint)rng.NextInt64(0, 1L << 32);
        return pix;
    }

    /// <summary>Same as the dispatch in pixRasterop, but on the low-level functions.</summary>
    public static void DispatchLow(Pix pixd, int dx, int dy, int dw, int dh, int op,
                                   Pix? pixs, int sx, int sy)
    {
        if (op == L.PIX_DST)
            return;
        pixd.GetDimensions(out int dpw, out int dph, out int dpd);
        if (op == L.PIX_CLR || op == L.PIX_SET || op == L.PIX_NOT(L.PIX_DST)) {
            Pix.rasteropUniLow(pixd.Data, dpw, dph, dpd, pixd.Wpl, dx, dy, dw, dh, op);
            return;
        }
        pixs!.GetDimensions(out int spw, out int sph, out _);
        Pix.rasteropLow(pixd.Data, dpw, dph, dpd, pixd.Wpl, dx, dy, dw, dh, op,
                        pixs.Data, spw, sph, pixs.Wpl, sx, sy);
    }

    public static string FirstDifference(uint[] expected, uint[] actual)
    {
        if (expected.Length != actual.Length)
            return $"length {actual.Length}, expected {expected.Length}";
        for (int i = 0; i < expected.Length; i++) {
            if (expected[i] != actual[i])
                return $"word {i}: got {actual[i]:x8}, expected {expected[i]:x8}";
        }
        return "";
    }
}
