using System;
using System.Runtime.CompilerServices;

namespace Leptonica;

/// <summary>
/// Pixel access within a raster line, from arrayaccess.h and arrayaccess.c.
/// </summary>
/// <remarks>
/// <para>
/// A line is a span of 32-bit words. Pixels are stored MSB-first within each
/// word, so pixel 0 of a 1 bpp line is bit 31 of word 0. For byte and 16-bit
/// access the index is interpreted in the same big-endian order as the
/// little-endian C macros, which address byte (n ^ 3) and 16-bit word (n ^ 1).
/// </para>
/// <para>
/// The word overloads (<c>uint word, int n</c>) replace the C idiom of
/// applying a macro to a pointer to a single 32-bit pixel, e.g.
/// <c>GET_DATA_BYTE(ppixel, COLOR_RED)</c>.
/// </para>
/// </remarks>
public static class ArrayAccess
{
    /*-------------------------------------------------------------------------*
     *                            1 bit access                                 *
     *-------------------------------------------------------------------------*/
    // leptonica: GET_DATA_BIT (arrayaccess.h)
    // leptonica: l_getDataBit (arrayaccess.c)
    /// <summary>Gets a 1-bit pixel from a raster line.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    /// <returns>The pixel value, 0 or 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetDataBit(ReadOnlySpan<uint> line, int n)
    {
        return (int)((line[n >> 5] >> (31 - (n & 31))) & 1);
    }

    // leptonica: SET_DATA_BIT (arrayaccess.h)
    // leptonica: l_setDataBit (arrayaccess.c)
    /// <summary>Sets a 1-bit pixel in a raster line to 1.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetDataBit(Span<uint> line, int n)
    {
        line[n >> 5] |= 0x80000000 >> (n & 31);
    }

    // leptonica: CLEAR_DATA_BIT (arrayaccess.h)
    // leptonica: l_clearDataBit (arrayaccess.c)
    /// <summary>Clears a 1-bit pixel in a raster line to 0.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ClearDataBit(Span<uint> line, int n)
    {
        line[n >> 5] &= ~(0x80000000 >> (n & 31));
    }

    // leptonica: SET_DATA_BIT_VAL (arrayaccess.h)
    // leptonica: l_setDataBitVal (arrayaccess.c)
    /// <summary>Sets a 1-bit pixel in a raster line to the given value.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    /// <param name="val">The new pixel value, 0 or 1.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetDataBitVal(Span<uint> line, int n, int val)
    {
        ref uint w = ref line[n >> 5];
        w = (w & ~(0x80000000 >> (n & 31))) | ((uint)val << (31 - (n & 31)));
    }

    /*-------------------------------------------------------------------------*
     *                            2 bit access                                 *
     *-------------------------------------------------------------------------*/
    // leptonica: GET_DATA_DIBIT (arrayaccess.h)
    // leptonica: l_getDataDibit (arrayaccess.c)
    /// <summary>Gets a 2-bit pixel from a raster line.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    /// <returns>The pixel value, 0 to 3.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetDataDibit(ReadOnlySpan<uint> line, int n)
    {
        return (int)((line[n >> 4] >> (2 * (15 - (n & 15)))) & 3);
    }

    // leptonica: SET_DATA_DIBIT (arrayaccess.h)
    // leptonica: l_setDataDibit (arrayaccess.c)
    /// <summary>Sets a 2-bit pixel in a raster line.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    /// <param name="val">The new pixel value; only the low 2 bits are used.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetDataDibit(Span<uint> line, int n, int val)
    {
        ref uint w = ref line[n >> 4];
        w = (w & ~(0xc0000000 >> (2 * (n & 15)))) | ((uint)(val & 3) << (30 - 2 * (n & 15)));
    }

    // leptonica: CLEAR_DATA_DIBIT (arrayaccess.h)
    // leptonica: l_clearDataDibit (arrayaccess.c)
    /// <summary>Clears a 2-bit pixel in a raster line to 0.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ClearDataDibit(Span<uint> line, int n)
    {
        line[n >> 4] &= ~(0xc0000000 >> (2 * (n & 15)));
    }

    /*-------------------------------------------------------------------------*
     *                            4 bit access                                 *
     *-------------------------------------------------------------------------*/
    // leptonica: GET_DATA_QBIT (arrayaccess.h)
    // leptonica: l_getDataQbit (arrayaccess.c)
    /// <summary>Gets a 4-bit pixel from a raster line.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    /// <returns>The pixel value, 0 to 15.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetDataQbit(ReadOnlySpan<uint> line, int n)
    {
        return (int)((line[n >> 3] >> (4 * (7 - (n & 7)))) & 0xf);
    }

    // leptonica: SET_DATA_QBIT (arrayaccess.h)
    // leptonica: l_setDataQbit (arrayaccess.c)
    /// <summary>Sets a 4-bit pixel in a raster line.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    /// <param name="val">The new pixel value; only the low 4 bits are used.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetDataQbit(Span<uint> line, int n, int val)
    {
        ref uint w = ref line[n >> 3];
        w = (w & ~(0xf0000000 >> (4 * (n & 7)))) | ((uint)(val & 15) << (28 - 4 * (n & 7)));
    }

    // leptonica: CLEAR_DATA_QBIT (arrayaccess.h)
    // leptonica: l_clearDataQbit (arrayaccess.c)
    /// <summary>Clears a 4-bit pixel in a raster line to 0.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ClearDataQbit(Span<uint> line, int n)
    {
        line[n >> 3] &= ~(0xf0000000 >> (4 * (n & 7)));
    }

    /*-------------------------------------------------------------------------*
     *                            8 bit access                                 *
     *-------------------------------------------------------------------------*/
    // leptonica: GET_DATA_BYTE (arrayaccess.h)
    // leptonica: l_getDataByte (arrayaccess.c)
    /// <summary>Gets an 8-bit pixel from a raster line.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    /// <returns>The pixel value, 0 to 255.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetDataByte(ReadOnlySpan<uint> line, int n)
    {
        return (int)((line[n >> 2] >> (8 * (3 - (n & 3)))) & 0xff);
    }

    // leptonica: SET_DATA_BYTE (arrayaccess.h)
    // leptonica: l_setDataByte (arrayaccess.c)
    /// <summary>Sets an 8-bit pixel in a raster line.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    /// <param name="val">The new pixel value; only the low 8 bits are used.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetDataByte(Span<uint> line, int n, int val)
    {
        int shift = 8 * (3 - (n & 3));
        ref uint w = ref line[n >> 2];
        w = (w & ~(0xffu << shift)) | ((uint)(byte)val << shift);
    }

    /// <summary>GET_DATA_BYTE applied to a pointer to a single word; n in [0..3].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetDataByte(uint word, int n)
    {
        return (int)((word >> (8 * (3 - n))) & 0xff);
    }

    /// <summary>SET_DATA_BYTE applied to a pointer to a single word; n in [0..3].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetDataByte(ref uint word, int n, int val)
    {
        int shift = 8 * (3 - n);
        word = (word & ~(0xffu << shift)) | ((uint)(byte)val << shift);
    }

    /*-------------------------------------------------------------------------*
     *                           16 bit access                                 *
     *-------------------------------------------------------------------------*/
    // leptonica: GET_DATA_TWO_BYTES (arrayaccess.h)
    // leptonica: l_getDataTwoBytes (arrayaccess.c)
    /// <summary>Gets a 16-bit pixel from a raster line.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    /// <returns>The pixel value, 0 to 65535.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetDataTwoBytes(ReadOnlySpan<uint> line, int n)
    {
        return (int)((line[n >> 1] >> (16 * (1 - (n & 1)))) & 0xffff);
    }

    // leptonica: SET_DATA_TWO_BYTES (arrayaccess.h)
    // leptonica: l_setDataTwoBytes (arrayaccess.c)
    /// <summary>Sets a 16-bit pixel in a raster line.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    /// <param name="val">The new pixel value; only the low 16 bits are used.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetDataTwoBytes(Span<uint> line, int n, int val)
    {
        int shift = 16 * (1 - (n & 1));
        ref uint w = ref line[n >> 1];
        w = (w & ~(0xffffu << shift)) | ((uint)(ushort)val << shift);
    }

    /*-------------------------------------------------------------------------*
     *                           32 bit access                                 *
     *-------------------------------------------------------------------------*/
    // leptonica: GET_DATA_FOUR_BYTES (arrayaccess.h)
    // leptonica: l_getDataFourBytes (arrayaccess.c)
    /// <summary>Gets a 32-bit pixel from a raster line.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    /// <returns>The pixel value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDataFourBytes(ReadOnlySpan<uint> line, int n)
    {
        return line[n];
    }

    // leptonica: SET_DATA_FOUR_BYTES (arrayaccess.h)
    // leptonica: l_setDataFourBytes (arrayaccess.c)
    /// <summary>Sets a 32-bit pixel in a raster line.</summary>
    /// <param name="line">The raster line, as 32-bit words.</param>
    /// <param name="n">Index of the pixel within the line.</param>
    /// <param name="val">The new pixel value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetDataFourBytes(Span<uint> line, int n, uint val)
    {
        line[n] = val;
    }

    /*-------------------------------------------------------------------------*
     *          Unchecked ref-based fast paths for pre-validated loops         *
     *-------------------------------------------------------------------------*/
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetDataBitUnchecked(ref uint lineRef, int n)
    {
        uint word = Unsafe.Add(ref lineRef, (nuint)(uint)n >> 5);
        return (int)((word >> (31 - (n & 31))) & 1u);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void SetDataBitUnchecked(ref uint lineRef, int n)
    {
        Unsafe.Add(ref lineRef, (nuint)(uint)n >> 5) |= 0x80000000u >> (n & 31);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void ClearDataBitUnchecked(ref uint lineRef, int n)
    {
        Unsafe.Add(ref lineRef, (nuint)(uint)n >> 5) &= ~(0x80000000u >> (n & 31));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void SetDataBitValUnchecked(ref uint lineRef, int n, int val)
    {
        ref uint w = ref Unsafe.Add(ref lineRef, (nuint)(uint)n >> 5);
        w = (w & ~(0x80000000u >> (n & 31))) | ((uint)val << (31 - (n & 31)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetDataDibitUnchecked(ref uint lineRef, int n)
    {
        uint word = Unsafe.Add(ref lineRef, (nuint)(uint)n >> 4);
        return (int)((word >> (2 * (15 - (n & 15)))) & 3u);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void SetDataDibitUnchecked(ref uint lineRef, int n, int val)
    {
        ref uint w = ref Unsafe.Add(ref lineRef, (nuint)(uint)n >> 4);
        w = (w & ~(0xc0000000u >> (2 * (n & 15)))) | ((uint)(val & 3) << (30 - 2 * (n & 15)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetDataQbitUnchecked(ref uint lineRef, int n)
    {
        uint word = Unsafe.Add(ref lineRef, (nuint)(uint)n >> 3);
        return (int)((word >> (4 * (7 - (n & 7)))) & 0xfu);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void SetDataQbitUnchecked(ref uint lineRef, int n, int val)
    {
        ref uint w = ref Unsafe.Add(ref lineRef, (nuint)(uint)n >> 3);
        w = (w & ~(0xf0000000u >> (4 * (n & 7)))) | ((uint)(val & 15) << (28 - 4 * (n & 7)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetDataByteUnchecked(ref uint lineRef, int n)
    {
        uint word = Unsafe.Add(ref lineRef, (nuint)(uint)n >> 2);
        return (int)((word >> (8 * (3 - (n & 3)))) & 0xffu);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void SetDataByteUnchecked(ref uint lineRef, int n, int val)
    {
        int shift = 8 * (3 - (n & 3));
        ref uint w = ref Unsafe.Add(ref lineRef, (nuint)(uint)n >> 2);
        w = (w & ~(0xffu << shift)) | ((uint)(byte)val << shift);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetDataTwoBytesUnchecked(ref uint lineRef, int n)
    {
        uint word = Unsafe.Add(ref lineRef, (nuint)(uint)n >> 1);
        return (int)((word >> (16 * (1 - (n & 1)))) & 0xffffu);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void SetDataTwoBytesUnchecked(ref uint lineRef, int n, int val)
    {
        int shift = 16 * (1 - (n & 1));
        ref uint w = ref Unsafe.Add(ref lineRef, (nuint)(uint)n >> 1);
        w = (w & ~(0xffffu << shift)) | ((uint)(ushort)val << shift);
    }
}
