using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Leptonica.Internal.Png;

/// <summary>PNG scanline filters (None, Sub, Up, Average, Paeth).</summary>
internal static class PngFilter
{
    public const int None = 0, Sub = 1, Up = 2, Avg = 3, Paeth = 4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PaethPredictor(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    /// <summary>Paeth predictor on lanes holding byte values widened to 16 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<ushort> PaethWide(Vector<ushort> a, Vector<ushort> b, Vector<ushort> c)
    {
        Vector<short> sa = Vector.AsVectorInt16(a), sb = Vector.AsVectorInt16(b), sc = Vector.AsVectorInt16(c);
        Vector<short> pa = Vector.Abs(sb - sc);
        Vector<short> pb = Vector.Abs(sa - sc);
        Vector<short> pc = Vector.Abs(sa + sb - sc - sc);
        Vector<short> useA = Vector.BitwiseAnd(Vector.LessThanOrEqual(pa, pb), Vector.LessThanOrEqual(pa, pc));
        Vector<short> useB = Vector.LessThanOrEqual(pb, pc);
        Vector<short> bc = Vector.ConditionalSelect(useB, sb, sc);
        return Vector.AsVectorUInt16(Vector.ConditionalSelect(useA, sa, bc));
    }

    /// <summary>Reverses a filter in place. <paramref name="prev"/> is the previous unfiltered row (zeros for the first).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Unfilter(int type, Span<byte> row, ReadOnlySpan<byte> prev, int bpp)
    {
        int n = row.Length;
        if (type < None || type > Paeth)
            throw new PngException("bad adaptive filter value");
        if (type == None)
            return;
        if (prev.Length < n)
            throw new ArgumentException("previous row is shorter than the row", nameof(prev));

        int done = 0;
        if (Vector128.IsHardwareAccelerated && (bpp == 3 || bpp == 4) && n >= 8)
        {
            ref byte r = ref MemoryMarshal.GetReference(row);
            ref byte p = ref MemoryMarshal.GetReference(prev);
            switch (type)
            {
                case Sub: done = UnfilterSubPixels(ref r, n, bpp); break;
                case Up: break;
                case Avg: done = UnfilterAvgPixels(ref r, ref p, n, bpp); break;
                default: done = UnfilterPaethPixels(ref r, ref p, n, bpp); break;
            }
        }
        UnfilterScalar(type, row, prev, bpp, done);
    }

    /// <summary>Scalar unfiltering of the bytes from <paramref name="start"/> to the end of the row.</summary>
    private static void UnfilterScalar(int type, Span<byte> row, ReadOnlySpan<byte> prev, int bpp, int start)
    {
        int n = row.Length;
        int i = start;
        switch (type)
        {
            case Sub:
                i = Math.Max(i, bpp);
                for (; i < n; i++)
                    row[i] = (byte)(row[i] + row[i - bpp]);
                break;
            case Up:
                {
                    int vs = Vector<byte>.Count;
                    for (; i + vs <= n; i += vs)
                        (new Vector<byte>(row.Slice(i)) + new Vector<byte>(prev.Slice(i))).CopyTo(row.Slice(i));
                    for (; i < n; i++)
                        row[i] = (byte)(row[i] + prev[i]);
                    break;
                }
            case Avg:
                for (; i < bpp && i < n; i++)
                    row[i] = (byte)(row[i] + (prev[i] >> 1));
                for (; i < n; i++)
                    row[i] = (byte)(row[i] + ((row[i - bpp] + prev[i]) >> 1));
                break;
            case Paeth:
                for (; i < bpp && i < n; i++)
                    row[i] = (byte)(row[i] + prev[i]);
                for (; i < n; i++)
                    row[i] = (byte)(row[i] + PaethPredictor(row[i - bpp], prev[i], prev[i - bpp]));
                break;
        }
    }

    /// <summary>
    /// Lane mask for a pixel of 3 or 4 bytes held in the low 4 lanes of a 16-bit vector:
    /// the fourth lane of a 3-byte pixel belongs to the next pixel and must not take part.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> PixelMask(int bpp) =>
        Vector128.Create((short)0xff, 0xff, 0xff, bpp == 4 ? (short)0xff : (short)0, 0, 0, 0, 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Load4(ref byte r, int i, uint keep) =>
        Vector128.WidenLower(Vector128.CreateScalarUnsafe(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref r, i)) & keep).AsByte()).AsInt16();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store4(ref byte r, int i, Vector128<short> v)
    {
        Vector128<ushort> u = v.AsUInt16();
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref r, i), Vector128.Narrow(u, u).AsUInt32().ToScalar());
    }

    /// <summary>Sub filter, one pixel per step. Returns the offset of the first byte left to the scalar code.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int UnfilterSubPixels(ref byte r, int n, int bpp)
    {
        Vector128<short> keepA = PixelMask(bpp);
        Vector128<short> m255 = Vector128.Create((short)0xff);
        Vector128<short> a = Vector128<short>.Zero;
        int i = 0;
        for (; i + 4 <= n; i += bpp)
        {
            // The fourth lane of a 3-byte pixel is the next pixel's data; a is zero there, so it is stored back unchanged.
            Vector128<short> m = (Load4(ref r, i, 0xffffffffu) + a) & m255;
            Store4(ref r, i, m);
            a = m & keepA;
        }
        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int UnfilterAvgPixels(ref byte r, ref byte p, int n, int bpp)
    {
        Vector128<short> keepA = PixelMask(bpp);
        uint keep = bpp == 4 ? 0xffffffffu : 0x00ffffffu;
        Vector128<short> m255 = Vector128.Create((short)0xff);
        Vector128<short> a = Vector128<short>.Zero;
        int i = 0;
        for (; i + 4 <= n; i += bpp)
        {
            Vector128<short> b = Load4(ref p, i, keep);
            Vector128<short> res = Load4(ref r, i, 0xffffffffu) + Vector128.ShiftRightLogical(a + b, 1);
            Vector128<short> m = res & m255;
            Store4(ref r, i, m);
            a = m & keepA;
        }
        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int UnfilterPaethPixels(ref byte r, ref byte p, int n, int bpp)
    {
        Vector128<short> keepA = PixelMask(bpp);
        uint keep = bpp == 4 ? 0xffffffffu : 0x00ffffffu;
        Vector128<short> m255 = Vector128.Create((short)0xff);
        Vector128<short> a = Vector128<short>.Zero;
        Vector128<short> c = Vector128<short>.Zero;
        int i = 0;
        for (; i + 4 <= n; i += bpp)
        {
            Vector128<short> b = Load4(ref p, i, keep);
            Vector128<short> d1 = a - c;      // p - b
            Vector128<short> d2 = b - c;      // p - a
            Vector128<short> pa = Vector128.Abs(d2);
            Vector128<short> pb = Vector128.Abs(d1);
            Vector128<short> pc = Vector128.Abs(d1 + d2);
            Vector128<short> useA = Vector128.LessThanOrEqual(pa, pb) & Vector128.LessThanOrEqual(pa, pc);
            Vector128<short> useB = Vector128.LessThanOrEqual(pb, pc);
            Vector128<short> pred = Vector128.ConditionalSelect(useA, a, Vector128.ConditionalSelect(useB, b, c));
            Vector128<short> m = (Load4(ref r, i, 0xffffffffu) + pred) & m255;
            Store4(ref r, i, m);
            a = m & keepA;
            c = b;
        }
        return i;
    }

    /// <summary>
    /// Applies a filter: <paramref name="dst"/> receives the filtered bytes of <paramref name="row"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Apply(int type, ReadOnlySpan<byte> row, ReadOnlySpan<byte> prev, int bpp, Span<byte> dst)
    {
        int n = row.Length;
        switch (type)
        {
            case None:
                row.CopyTo(dst);
                break;
            case Sub:
                {
                    int m = Math.Min(bpp, n);
                    for (int i = 0; i < m; i++) dst[i] = row[i];
                    int j = bpp;
                    if (Vector.IsHardwareAccelerated)
                    {
                        int vs = Vector<byte>.Count;
                        for (; j + vs <= n; j += vs)
                            (new Vector<byte>(row.Slice(j)) - new Vector<byte>(row.Slice(j - bpp))).CopyTo(dst.Slice(j));
                    }
                    for (; j < n; j++)
                        dst[j] = (byte)(row[j] - row[j - bpp]);
                    break;
                }
            case Up:
                {
                    int i = 0;
                    int vs = Vector<byte>.Count;
                    for (; i + vs <= n; i += vs)
                        (new Vector<byte>(row.Slice(i)) - new Vector<byte>(prev.Slice(i))).CopyTo(dst.Slice(i));
                    for (; i < n; i++)
                        dst[i] = (byte)(row[i] - prev[i]);
                    break;
                }
            case Avg:
                {
                    int m = Math.Min(bpp, n);
                    for (int i = 0; i < m; i++)
                        dst[i] = (byte)(row[i] - (prev[i] >> 1));
                    int j = bpp;
                    if (Vector.IsHardwareAccelerated)
                    {
                        int vs = Vector<byte>.Count;
                        for (; j + vs <= n; j += vs)
                        {
                            Vector<byte> a = new(row.Slice(j - bpp));
                            Vector<byte> b = new(prev.Slice(j));
                            // floor((a + b) / 2) without widening
                            Vector<byte> avg = (a & b) + Vector.ShiftRightLogical(a ^ b, 1);
                            (new Vector<byte>(row.Slice(j)) - avg).CopyTo(dst.Slice(j));
                        }
                    }
                    for (; j < n; j++)
                        dst[j] = (byte)(row[j] - ((row[j - bpp] + prev[j]) >> 1));
                    break;
                }
            case Paeth:
                {
                    int m = Math.Min(bpp, n);
                    for (int i = 0; i < m; i++)
                        dst[i] = (byte)(row[i] - prev[i]);
                    int j = bpp;
                    if (Vector.IsHardwareAccelerated)
                    {
                        int vs = Vector<byte>.Count;
                        for (; j + vs <= n; j += vs)
                        {
                            Vector<byte> x = new(row.Slice(j));
                            Vector<byte> a = new(row.Slice(j - bpp));
                            Vector<byte> b = new(prev.Slice(j));
                            Vector<byte> c = new(prev.Slice(j - bpp));
                            Vector.Widen(a, out Vector<ushort> aLo, out Vector<ushort> aHi);
                            Vector.Widen(b, out Vector<ushort> bLo, out Vector<ushort> bHi);
                            Vector.Widen(c, out Vector<ushort> cLo, out Vector<ushort> cHi);
                            Vector<byte> pred = Vector.Narrow(PaethWide(aLo, bLo, cLo), PaethWide(aHi, bHi, cHi));
                            (x - pred).CopyTo(dst.Slice(j));
                        }
                    }
                    for (; j < n; j++)
                        dst[j] = (byte)(row[j] - PaethPredictor(row[j - bpp], prev[j], prev[j - bpp]));
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    /// <summary>
    /// Sum of absolute values of the filtered bytes viewed as signed, as libpng's
    /// heuristic; stops early once the sum exceeds <paramref name="limit"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int Cost(ReadOnlySpan<byte> filtered, int limit)
    {
        int sum = 0;
        int i = 0;
        int n = filtered.Length;
        ref byte src = ref MemoryMarshal.GetReference(filtered);
        if (Avx2.IsSupported && n >= 32)
        {
            Vector256<byte> zero = Vector256<byte>.Zero;
            for (; i + 32 <= n; i += 32)
            {
                Vector256<sbyte> sb = Vector256.LoadUnsafe(ref src, (nuint)i).AsSByte();
                Vector256<byte> ab = Vector256.Abs(sb).AsByte();
                Vector256<ulong> sad = Avx2.SumAbsoluteDifferences(ab, zero).AsUInt64();
                sum += (int)Vector256.Sum(sad);
                if (sum > limit) return sum;
            }
        }
        else if (Vector.IsHardwareAccelerated)
        {
            int vs = Vector<byte>.Count;
            while (i + vs <= n)
            {
                int count = Math.Min((n - i) / vs, 255);
                Vector<ushort> accLo = default, accHi = default;
                for (int k = 0; k < count; k++, i += vs)
                {
                    Vector<byte> a = Vector.AsVectorByte(Vector.Abs(Vector.AsVectorSByte(new Vector<byte>(filtered.Slice(i)))));
                    Vector.Widen(a, out Vector<ushort> lo, out Vector<ushort> hi);
                    accLo += lo;
                    accHi += hi;
                }
                Vector.Widen(accLo, out Vector<uint> l0, out Vector<uint> l1);
                Vector.Widen(accHi, out Vector<uint> h0, out Vector<uint> h1);
                sum += (int)Vector.Sum(l0 + l1 + h0 + h1);
                if (sum > limit) return sum;
            }
        }
        for (; i < n; i++)
        {
            int v = Unsafe.Add(ref src, (nuint)i);
            sum += v < 128 ? v : 256 - v;
            if (sum > limit) break;
        }
        return sum;
    }
}
