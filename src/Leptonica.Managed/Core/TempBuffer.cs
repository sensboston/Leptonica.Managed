using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Leptonica;

/// <summary>
/// Scoped wrapper over <see cref="ArrayPool{T}.Shared"/> for strictly internal temporary buffers.
/// Never expose the rented backing array on a returned <see cref="Pix"/>, <see cref="FPix"/>, or <see cref="DPix"/>.
/// </summary>
internal ref struct TempBuffer<T> where T : unmanaged
{
    private T[]? _rented;
    private readonly int _length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TempBuffer(int length, bool clear = false)
    {
        _length = length;
        if (length > 0)
        {
            _rented = ArrayPool<T>.Shared.Rent(length);
            if (clear)
                _rented.AsSpan(0, length).Clear();
        }
        else
        {
            _rented = null;
        }
    }

    public readonly int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length;
    }

    public readonly T[] Array
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _rented ?? System.Array.Empty<T>();
    }

    public readonly Span<T> Span
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _rented != null ? _rented.AsSpan(0, _length) : Span<T>.Empty;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        T[]? arr = _rented;
        _rented = null;
        if (arr != null)
            ArrayPool<T>.Shared.Return(arr);
    }
}

/// <summary>
/// Cross-platform SIMD helpers for converting between Leptonica's MSB-first 32-bit word layout
/// (bytes <c>[p3, p2, p1, p0]</c> on little-endian CPUs, halfwords <c>[p1, p0]</c>) and linear pixel order.
/// </summary>
internal static class SimdEndian
{
    public static readonly Vector128<byte> ByteSwapMask128 = Vector128.Create(
        (byte)3, 2, 1, 0,
        7, 6, 5, 4,
        11, 10, 9, 8,
        15, 14, 13, 12);

    public static readonly Vector256<byte> ByteSwapMask256 = Vector256.Create(
        (byte)3, 2, 1, 0,
        7, 6, 5, 4,
        11, 10, 9, 8,
        15, 14, 13, 12,
        19, 18, 17, 16,
        23, 22, 21, 20,
        27, 26, 25, 24,
        31, 30, 29, 28);

    public static readonly Vector128<byte> HalfwordSwapMask128 = Vector128.Create(
        (byte)2, 3, 0, 1,
        6, 7, 4, 5,
        10, 11, 8, 9,
        14, 15, 12, 13);

    public static readonly Vector256<byte> HalfwordSwapMask256 = Vector256.Create(
        (byte)2, 3, 0, 1,
        6, 7, 4, 5,
        10, 11, 8, 9,
        14, 15, 12, 13,
        18, 19, 16, 17,
        22, 23, 20, 21,
        26, 27, 24, 25,
        30, 31, 28, 29);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<byte> SwapBytesInWords(Vector128<byte> v)
        => BitConverter.IsLittleEndian ? Vector128.Shuffle(v, ByteSwapMask128) : v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<byte> SwapBytesInWords(Vector256<byte> v)
        => BitConverter.IsLittleEndian ? Vector256.Shuffle(v, ByteSwapMask256) : v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<ushort> SwapHalfwordsInWords(Vector128<ushort> v)
        => BitConverter.IsLittleEndian ? Vector128.Shuffle(v.AsByte(), HalfwordSwapMask128).AsUInt16() : v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<ushort> SwapHalfwordsInWords(Vector256<ushort> v)
        => BitConverter.IsLittleEndian ? Vector256.Shuffle(v.AsByte(), HalfwordSwapMask256).AsUInt16() : v;
}
