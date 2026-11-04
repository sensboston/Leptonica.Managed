using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  pixarith.c: Grayscale arithmetic, accumulators, min/max, dynamic range.
 *
 *  The image accumulator operations are used when you expect
 *  overflow from 8 bits on intermediate results.  For example,
 *  you might want a tophat contrast operator which is
 *     3*I - opening(I,S) - closing(I,S)
 *  To use these operations, first use the init to generate
 *  a 16 bpp image, use the accumulate to add or subtract 8 bpp
 *  images from that, or the multiply constant to multiply
 *  by a small constant (much less than 256 -- we don't want
 *  overflow from the 16 bit images!), and when you're finished
 *  use final to bring the result back to 8 bpp, clipped
 *  if necessary.  There is also a divide function, which
 *  can be used to divide one image by another, scaling the
 *  result for maximum dynamic range, and giving back the
 *  8 bpp result.
 *
 *  A simpler interface to the arithmetic operations is
 *  provided in pixacc.c.
 *
 *  Stubs for callees in other files: pixGetExtremeValue (pix4.c),
 *  pixRemoveColormap (pixconv.c).
 */

/// <summary>Constants from pix.h and morph.h used by pixarith.c.</summary>
public static partial class L
{
    /* Choice of component for extreme values (pix.h) */

    /* Scaling for maximum dynamic range (morph.h) */
    /// <summary>Linear scaling.</summary>
    public const int L_LINEAR_SCALE = 1;
    /// <summary>Logarithmic scaling.</summary>
    public const int L_LOG_SCALE = 2;
}

public sealed partial class Pix
{
    /*-------------------------------------------------------------*
     *          One-image grayscale arithmetic operations          *
     *-------------------------------------------------------------*/
    // leptonica: pixAddConstantGray (pixarith.c)
    /// <summary>Adds a constant to each pixel of an 8, 16 or 32 bpp image, in place.</summary>
    /// <param name="val">amount to add to each pixel</param>
    /// <remarks>
    /// (1) In-place operation.
    /// (2) No clipping for 32 bpp.
    /// (3) For 8 and 16 bpp, if val &gt; 0 the result is clipped
    ///     to 0xff and 0xffff, rsp.
    /// (4) For 8 and 16 bpp, if val &lt; 0 the result is clipped to 0.
    /// </remarks>
    public void AddConstantGray(int val)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixAddConstantGray", "pixs not 8, 16 or 32 bpp");
        if (val == 0)
            return;

        uint[] data = Data;
        int wpl = Wpl;
        if (d == 8) {
            int fullwords = w >> 2;
            int fullBytes = fullwords << 2;
            if (val < 0) {
                byte sub8 = (byte)Math.Min(255, -val);
                Vector256<byte> vSub = Vector256.Create(sub8);
                for (int i = 0; i < h; i++) {
                    ref uint lineRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(data), (nuint)(i * wpl));
                    ref byte bRef = ref Unsafe.As<uint, byte>(ref lineRef);
                    int b = 0;
                    if (Vector256.IsHardwareAccelerated) {
                        for (; b + 32 <= fullBytes; b += 32) {
                            Vector256<byte> v = Vector256.LoadUnsafe(ref bRef, (nuint)b);
                            (Vector256.Max(v, vSub) - vSub).StoreUnsafe(ref bRef, (nuint)b);
                        }
                    }
                    for (; b < fullBytes; b++) {
                        int cur = Unsafe.Add(ref bRef, (nuint)b);
                        Unsafe.Add(ref bRef, (nuint)b) = (byte)Math.Max(0, cur - sub8);
                    }
                    for (int j = fullwords << 2; j < w; j++) {
                        int pval = GetDataByteUnchecked(ref lineRef, j);
                        SetDataByteUnchecked(ref lineRef, j, Math.Max(0, pval + val));
                    }
                }
            } else {
                byte add8 = (byte)Math.Min(255, val);
                byte cap8 = (byte)(255 - add8);
                Vector256<byte> vAdd = Vector256.Create(add8);
                Vector256<byte> vCap = Vector256.Create(cap8);
                for (int i = 0; i < h; i++) {
                    ref uint lineRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(data), (nuint)(i * wpl));
                    ref byte bRef = ref Unsafe.As<uint, byte>(ref lineRef);
                    int b = 0;
                    if (Vector256.IsHardwareAccelerated) {
                        for (; b + 32 <= fullBytes; b += 32) {
                            Vector256<byte> v = Vector256.LoadUnsafe(ref bRef, (nuint)b);
                            (Vector256.Min(v, vCap) + vAdd).StoreUnsafe(ref bRef, (nuint)b);
                        }
                    }
                    for (; b < fullBytes; b++) {
                        int cur = Unsafe.Add(ref bRef, (nuint)b);
                        Unsafe.Add(ref bRef, (nuint)b) = (byte)Math.Min(255, cur + add8);
                    }
                    for (int j = fullwords << 2; j < w; j++) {
                        int pval = GetDataByteUnchecked(ref lineRef, j);
                        SetDataByteUnchecked(ref lineRef, j, Math.Min(255, pval + val));
                    }
                }
            }
        } else if (d == 16) {
            int fullwords = w >> 1;
            int fullHalfs = fullwords << 1;
            if (val < 0) {
                ushort sub16 = (ushort)Math.Min(0xffffL, -(long)val);
                Vector256<ushort> vSub = Vector256.Create(sub16);
                for (int i = 0; i < h; i++) {
                    ref uint lineRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(data), (nuint)(i * wpl));
                    ref ushort uRef = ref Unsafe.As<uint, ushort>(ref lineRef);
                    int k = 0;
                    if (Vector256.IsHardwareAccelerated) {
                        for (; k + 16 <= fullHalfs; k += 16) {
                            Vector256<ushort> v = Vector256.LoadUnsafe(ref uRef, (nuint)k);
                            (Vector256.Max(v, vSub) - vSub).StoreUnsafe(ref uRef, (nuint)k);
                        }
                    }
                    for (; k < fullHalfs; k++) {
                        int cur = Unsafe.Add(ref uRef, (nuint)k);
                        Unsafe.Add(ref uRef, (nuint)k) = (ushort)Math.Max(0, cur - sub16);
                    }
                    for (int j = fullwords << 1; j < w; j++) {
                        int pval = GetDataTwoBytesUnchecked(ref lineRef, j);
                        SetDataTwoBytesUnchecked(ref lineRef, j, Math.Max(0, pval + val));
                    }
                }
            } else {
                ushort add16 = (ushort)Math.Min(0xffff, val);
                ushort cap16 = (ushort)(0xffff - add16);
                Vector256<ushort> vAdd = Vector256.Create(add16);
                Vector256<ushort> vCap = Vector256.Create(cap16);
                for (int i = 0; i < h; i++) {
                    ref uint lineRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(data), (nuint)(i * wpl));
                    ref ushort uRef = ref Unsafe.As<uint, ushort>(ref lineRef);
                    int k = 0;
                    if (Vector256.IsHardwareAccelerated) {
                        for (; k + 16 <= fullHalfs; k += 16) {
                            Vector256<ushort> v = Vector256.LoadUnsafe(ref uRef, (nuint)k);
                            (Vector256.Min(v, vCap) + vAdd).StoreUnsafe(ref uRef, (nuint)k);
                        }
                    }
                    for (; k < fullHalfs; k++) {
                        int cur = Unsafe.Add(ref uRef, (nuint)k);
                        Unsafe.Add(ref uRef, (nuint)k) = (ushort)Math.Min(0xffff, cur + add16);
                    }
                    for (int j = fullwords << 1; j < w; j++) {
                        int pval = GetDataTwoBytesUnchecked(ref lineRef, j);
                        SetDataTwoBytesUnchecked(ref lineRef, j, Math.Min(0xffff, pval + val));
                    }
                }
            }
        } else {
            uint uval = (uint)val;
            Vector256<uint> vAdd = Vector256.Create(uval);
            for (int i = 0; i < h; i++) {
                ref uint lineRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(data), (nuint)(i * wpl));
                int j = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; j + 8 <= w; j += 8)
                        (Vector256.LoadUnsafe(ref lineRef, (nuint)j) + vAdd).StoreUnsafe(ref lineRef, (nuint)j);
                }
                for (; j < w; j++)
                    Unsafe.Add(ref lineRef, (nuint)j) += uval;
            }
        }
    }

    // leptonica: pixMultConstantGray (pixarith.c)
    /// <summary>Multiplies each pixel of an 8, 16 or 32 bpp image by a constant, in place.</summary>
    /// <param name="val">&gt;= 0.0; amount to multiply by each pixel</param>
    /// <remarks>
    /// (1) In-place operation; val must be &gt;= 0.
    /// (2) No clipping for 32 bpp.
    /// (3) For 8 and 16 bpp, the result is clipped to 0xff and 0xffff, rsp.
    /// </remarks>
    public void MultConstantGray(float val)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixMultConstantGray", "pixs not 8, 16 or 32 bpp");
        if (val < 0.0)
            throw Lept.Arg("pixMultConstantGray", "val < 0.0");

        uint[] data = Data;
        int wpl = Wpl;
        for (int i = 0; i < h; i++) {
            Span<uint> line = data.AsSpan(i * wpl, wpl);
            if (d == 8) {
                for (int j = 0; j < w; j++) {
                    int pval = GetDataByte(line, j);
                    pval = (int)(val * pval);
                    pval = Math.Min(255, pval);
                    SetDataByte(line, j, pval);
                }
            } else if (d == 16) {
                for (int j = 0; j < w; j++) {
                    int pval = GetDataTwoBytes(line, j);
                    pval = (int)(val * pval);
                    pval = Math.Min(0xffff, pval);
                    SetDataTwoBytes(line, j, pval);
                }
            } else {  /* d == 32; no clipping */
                for (int j = 0; j < w; j++) {
                    uint upval = line[j];
                    upval = (uint)(val * upval);
                    line[j] = upval;
                }
            }
        }
    }

    /*-------------------------------------------------------------*
     *             Two-image grayscale arithmetic ops              *
     *-------------------------------------------------------------*/
    // leptonica: pixAddGray (pixarith.c)
    /// <summary>Adds two 8, 16 or 32 bpp images.</summary>
    /// <param name="pixd">Optional destination; can be <c>null</c>, the same object as <paramref name="pixs1"/>, or a different image.</param>
    /// <param name="pixs1">The first source image; can be the same object as <paramref name="pixd"/>.</param>
    /// <param name="pixs2">The second source image; must differ from both <paramref name="pixd"/> and <paramref name="pixs1"/>.</param>
    /// <returns>The destination image: a new image, or <paramref name="pixd"/>.</returns>
    /// <exception cref="ArgumentException">The sources are not valid, or <paramref name="pixs2"/> is the same object as <paramref name="pixs1"/> or <paramref name="pixd"/>.</exception>
    /// <remarks>
    /// <para>For 8 and 16 bpp, explicit clipping to 0xff and 0xffff, respectively, is done.</para>
    /// <para>Alignment is to the upper-left corner.</para>
    /// <para>The result goes to a new image if <paramref name="pixd"/> is <c>null</c>, in place to
    /// <paramref name="pixs1"/> if <paramref name="pixd"/> is the same object, or to the given
    /// <paramref name="pixd"/> otherwise.</para>
    /// </remarks>
    public static Pix AddGray(Pix? pixd, Pix pixs1, Pix pixs2)
    {
        if (pixs2 == pixs1)
            throw Lept.Arg("pixAddGray", "pixs2 and pixs1 must differ");
        if (pixs2 == pixd)
            throw Lept.Arg("pixAddGray", "pixs2 and pixd must differ");
        int d = pixs1.Depth;
        if (d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixAddGray", "pix are not 8, 16 or 32 bpp");
        if (pixs2.Depth != d)
            throw Lept.Arg("pixAddGray", "depths differ (pixs1, pixs2)");
        if (pixd != null && pixd.Depth != d)
            throw Lept.Arg("pixAddGray", "depths differ (pixs1, pixd)");

        if (!SizesEqual(pixs1, pixs2))
            Log.Warning("pixAddGray", "pixs1 and pixs2 not equal in size\n");
        if (pixd != null && !SizesEqual(pixs1, pixd))
            Log.Warning("pixAddGray", "pixs1 and pixd not equal in size\n");

        if (pixs1 != pixd)
            pixd = Copy(pixd, pixs1);

            /* pixd + pixs2 ==> pixd  */
        uint[] datas = pixs2.Data;
        uint[] datad = pixd!.Data;
        int wpls = pixs2.Wpl;
        int wpld = pixd.Wpl;
        pixs2.GetDimensions(out int ws, out int hs, out _);
        pixd.GetDimensions(out int w, out int h, out _);
        w = Math.Min(ws, w);
        h = Math.Min(hs, h);
        if (d == 8) {
            int fullwords = w >> 2;
            int fullBytes = fullwords << 2;
            Vector256<byte> vMax = Vector256.Create((byte)255);
            for (int i = 0; i < h; i++) {
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
                ref byte bd = ref Unsafe.As<uint, byte>(ref rd);
                ref byte bs = ref Unsafe.As<uint, byte>(ref rs);
                int b = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; b + 32 <= fullBytes; b += 32) {
                        Vector256<byte> vd = Vector256.LoadUnsafe(ref bd, (nuint)b);
                        Vector256<byte> vs = Vector256.LoadUnsafe(ref bs, (nuint)b);
                        (vs + Vector256.Min(vd, vMax - vs)).StoreUnsafe(ref bd, (nuint)b);
                    }
                }
                for (; b < fullBytes; b++)
                    Unsafe.Add(ref bd, (nuint)b) = (byte)Math.Min(255, Unsafe.Add(ref bd, (nuint)b) + Unsafe.Add(ref bs, (nuint)b));
                for (int j = fullwords << 2; j < w; j++)
                    SetDataByteUnchecked(ref rd, j, Math.Min(255, GetDataByteUnchecked(ref rs, j) + GetDataByteUnchecked(ref rd, j)));
            }
        } else if (d == 16) {
            int fullwords = w >> 1;
            int fullHalfs = fullwords << 1;
            Vector256<ushort> vMax = Vector256.Create((ushort)0xffff);
            for (int i = 0; i < h; i++) {
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
                ref ushort ud = ref Unsafe.As<uint, ushort>(ref rd);
                ref ushort us = ref Unsafe.As<uint, ushort>(ref rs);
                int k = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; k + 16 <= fullHalfs; k += 16) {
                        Vector256<ushort> vd = Vector256.LoadUnsafe(ref ud, (nuint)k);
                        Vector256<ushort> vs = Vector256.LoadUnsafe(ref us, (nuint)k);
                        (vs + Vector256.Min(vd, vMax - vs)).StoreUnsafe(ref ud, (nuint)k);
                    }
                }
                for (; k < fullHalfs; k++)
                    Unsafe.Add(ref ud, (nuint)k) = (ushort)Math.Min(0xffff, Unsafe.Add(ref ud, (nuint)k) + Unsafe.Add(ref us, (nuint)k));
                for (int j = fullwords << 1; j < w; j++)
                    SetDataTwoBytesUnchecked(ref rd, j, Math.Min(0xffff, GetDataTwoBytesUnchecked(ref rs, j) + GetDataTwoBytesUnchecked(ref rd, j)));
            }
        } else {
            for (int i = 0; i < h; i++) {
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
                int j = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; j + 8 <= w; j += 8)
                        (Vector256.LoadUnsafe(ref rd, (nuint)j) + Vector256.LoadUnsafe(ref rs, (nuint)j)).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) += Unsafe.Add(ref rs, (nuint)j);
            }
        }

        return pixd;
    }

    // leptonica: pixSubtractGray (pixarith.c)
    /// <summary>Subtracts one 8, 16 or 32 bpp image from another.</summary>
    /// <param name="pixd">Optional destination; can be <c>null</c>, the same object as <paramref name="pixs1"/>, or a different image.</param>
    /// <param name="pixs1">The image to subtract from; can be the same object as <paramref name="pixd"/>.</param>
    /// <param name="pixs2">The image to subtract; must differ from both <paramref name="pixd"/> and <paramref name="pixs1"/>.</param>
    /// <returns>The destination image: a new image, or <paramref name="pixd"/>.</returns>
    /// <exception cref="ArgumentException">The sources are not valid, or <paramref name="pixs2"/> is the same object as <paramref name="pixs1"/> or <paramref name="pixd"/>.</exception>
    /// <remarks>
    /// <para><paramref name="pixs2"/> is always subtracted from <paramref name="pixs1"/>, with explicit clipping to 0.</para>
    /// <para>Alignment is to the upper-left corner.</para>
    /// <para>The result goes to a new image if <paramref name="pixd"/> is <c>null</c>, in place to
    /// <paramref name="pixs1"/> if <paramref name="pixd"/> is the same object, or to the given
    /// <paramref name="pixd"/> otherwise.</para>
    /// </remarks>
    public static Pix SubtractGray(Pix? pixd, Pix pixs1, Pix pixs2)
    {
        if (pixs2 == pixs1)
            throw Lept.Arg("pixSubtractGray", "pixs2 and pixs1 must differ");
        if (pixs2 == pixd)
            throw Lept.Arg("pixSubtractGray", "pixs2 and pixd must differ");
        int d = pixs1.Depth;
        if (d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixSubtractGray", "pix are not 8, 16 or 32 bpp");
        if (pixs2.Depth != d)
            throw Lept.Arg("pixSubtractGray", "depths differ (pixs1, pixs2)");
        if (pixd != null && pixd.Depth != d)
            throw Lept.Arg("pixSubtractGray", "depths differ (pixs1, pixd)");

        if (!SizesEqual(pixs1, pixs2))
            Log.Warning("pixSubtractGray", "pixs1 and pixs2 not equal in size\n");
        if (pixd != null && !SizesEqual(pixs1, pixd))
            Log.Warning("pixSubtractGray", "pixs1 and pixd not equal in size\n");

        if (pixs1 != pixd)
            pixd = Copy(pixd, pixs1);

            /* pixd - pixs2 ==> pixd  */
        uint[] datas = pixs2.Data;
        uint[] datad = pixd!.Data;
        int wpls = pixs2.Wpl;
        int wpld = pixd.Wpl;
        pixs2.GetDimensions(out int ws, out int hs, out _);
        pixd.GetDimensions(out int w, out int h, out _);
        w = Math.Min(ws, w);
        h = Math.Min(hs, h);
        if (d == 8) {
            int fullwords = w >> 2;
            int fullBytes = fullwords << 2;
            for (int i = 0; i < h; i++) {
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
                ref byte bd = ref Unsafe.As<uint, byte>(ref rd);
                ref byte bs = ref Unsafe.As<uint, byte>(ref rs);
                int b = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; b + 32 <= fullBytes; b += 32) {
                        Vector256<byte> vd = Vector256.LoadUnsafe(ref bd, (nuint)b);
                        Vector256<byte> vs = Vector256.LoadUnsafe(ref bs, (nuint)b);
                        (Vector256.Max(vd, vs) - vs).StoreUnsafe(ref bd, (nuint)b);
                    }
                }
                for (; b < fullBytes; b++)
                    Unsafe.Add(ref bd, (nuint)b) = (byte)Math.Max(0, Unsafe.Add(ref bd, (nuint)b) - Unsafe.Add(ref bs, (nuint)b));
                for (int j = fullwords << 2; j < w; j++)
                    SetDataByteUnchecked(ref rd, j, Math.Max(0, GetDataByteUnchecked(ref rd, j) - GetDataByteUnchecked(ref rs, j)));
            }
        } else if (d == 16) {
            int fullwords = w >> 1;
            int fullHalfs = fullwords << 1;
            for (int i = 0; i < h; i++) {
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
                ref ushort ud = ref Unsafe.As<uint, ushort>(ref rd);
                ref ushort us = ref Unsafe.As<uint, ushort>(ref rs);
                int k = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; k + 16 <= fullHalfs; k += 16) {
                        Vector256<ushort> vd = Vector256.LoadUnsafe(ref ud, (nuint)k);
                        Vector256<ushort> vs = Vector256.LoadUnsafe(ref us, (nuint)k);
                        (Vector256.Max(vd, vs) - vs).StoreUnsafe(ref ud, (nuint)k);
                    }
                }
                for (; k < fullHalfs; k++)
                    Unsafe.Add(ref ud, (nuint)k) = (ushort)Math.Max(0, Unsafe.Add(ref ud, (nuint)k) - Unsafe.Add(ref us, (nuint)k));
                for (int j = fullwords << 1; j < w; j++)
                    SetDataTwoBytesUnchecked(ref rd, j, Math.Max(0, GetDataTwoBytesUnchecked(ref rd, j) - GetDataTwoBytesUnchecked(ref rs, j)));
            }
        } else {
            for (int i = 0; i < h; i++) {
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
                int j = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; j + 8 <= w; j += 8)
                        (Vector256.LoadUnsafe(ref rd, (nuint)j) - Vector256.LoadUnsafe(ref rs, (nuint)j)).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) -= Unsafe.Add(ref rs, (nuint)j);
            }
        }

        return pixd;
    }

    // leptonica: pixMultiplyGray (pixarith.c)
    /// <summary>Multiplies a 32 bpp rgb or 8 bpp gray image by an 8 bpp gray image.</summary>
    /// <param name="pixg">8 bpp gray</param>
    /// <param name="norm">multiplicative factor to avoid overflow; 0 for default</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This function can be used for correcting a scanned image
    ///     under non-uniform illumination.  For that application,
    ///     %pixs is the scanned image, %pixg is an image whose values
    ///     are inversely related to light from a uniform (say, white)
    ///     target, and %norm is typically the inverse of the maximum
    ///     pixel value in %pixg.
    /// (2) Set norm = 0 to get the default value, which is the inverse
    ///     of the max value in %pixg.  This avoids overflow in the product.
    /// (3) For 32 bpp %pixs, all 3 components are multiplied by the
    ///     same number.
    /// (4) Alignment is to UL corner.
    /// </remarks>
    public Pix MultiplyGray(Pix pixg, float norm)
    {
        Pix pixs = this;
        pixs.GetDimensions(out int ws, out int hs, out int ds);
        if (ds != 8 && ds != 32)
            throw Lept.Arg("pixMultiplyGray", "pixs not 8 or 32 bpp");
        pixg.GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg("pixMultiplyGray", "pixg not 8 bpp");

        if (norm <= 0.0) {
            pixg.GetExtremeValue(1, L.L_SELECT_MAX, out _, out _, out _, out int maxgray);
            norm = (maxgray > 0) ? 1.0f / (float)maxgray : 1.0f;
        }

        Pix pixd = CreateTemplate(pixs);
        uint[] datas = pixs.Data;
        uint[] datag = pixg.Data;
        uint[] datad = pixd.Data;
        int wpls = pixs.Wpl;
        int wplg = pixg.Wpl;
        int wpld = pixd.Wpl;
        w = Math.Min(ws, w);
        h = Math.Min(hs, h);
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            ReadOnlySpan<uint> lineg = datag.AsSpan(i * wplg, wplg);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            if (ds == 8) {
                for (int j = 0; j < w; j++) {
                    int vals = GetDataByte(lines, j);
                    int valg = GetDataByte(lineg, j);
                    int val = (int)(vals * valg * norm + 0.5);
                    val = Math.Min(255, val);
                    SetDataByte(lined, j, val);
                }
            } else {  /* ds == 32 */
                for (int j = 0; j < w; j++) {
                    uint val32 = lines[j];
                    ExtractRGBValues(val32, out int rval, out int gval, out int bval);
                    int valg = GetDataByte(lineg, j);
                    int rval2 = (int)(rval * valg * norm + 0.5);
                    rval2 = Math.Min(255, rval2);
                    int gval2 = (int)(gval * valg * norm + 0.5);
                    gval2 = Math.Min(255, gval2);
                    int bval2 = (int)(bval * valg * norm + 0.5);
                    bval2 = Math.Min(255, bval2);
                    ComposeRGBPixel(rval2, gval2, bval2, out lined[j]);
                }
            }
        }

        return pixd;
    }

    /*-------------------------------------------------------------*
     *                Grayscale threshold operation                *
     *-------------------------------------------------------------*/
    // leptonica: pixThresholdToValue (pixarith.c)
    /// <summary>Sets pixels at or beyond a threshold to a given value.</summary>
    /// <param name="pixd">Optional destination; if not <c>null</c>, it must be the same object as <paramref name="pixs"/>.</param>
    /// <param name="pixs">The source image: 8, 16 or 32 bpp.</param>
    /// <param name="threshval">The threshold value.</param>
    /// <param name="setval">The value assigned to pixels at or beyond the threshold.</param>
    /// <returns>The destination image: a new image, or <paramref name="pixd"/>.</returns>
    /// <exception cref="ArgumentException">The image depth is not 8, 16 or 32 bpp, or <paramref name="pixd"/> is neither <c>null</c> nor <paramref name="pixs"/>.</exception>
    /// <remarks>
    /// <para>The operation can be in place (<paramref name="pixs"/> is <paramref name="pixd"/>) or to a new image.</para>
    /// <list type="bullet">
    /// <item><description>If <paramref name="setval"/> is greater than <paramref name="threshval"/>, pixels with a value greater than or equal to <paramref name="threshval"/> are set to <paramref name="setval"/>.</description></item>
    /// <item><description>If <paramref name="setval"/> is less than <paramref name="threshval"/>, pixels with a value less than or equal to <paramref name="threshval"/> are set to <paramref name="setval"/>.</description></item>
    /// <item><description>If the two values are equal, nothing is changed.</description></item>
    /// </list>
    /// </remarks>
    public static Pix ThresholdToValue(Pix? pixd, Pix pixs, int threshval, int setval)
    {
        int d = pixs.Depth;
        if (d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixThresholdToValue", "pixs not 8, 16 or 32 bpp");
        if (pixd != null && pixs != pixd)
            throw Lept.Arg("pixThresholdToValue", "pixd exists and is not pixs");
        if (threshval < 0 || setval < 0)
            throw Lept.Arg("pixThresholdToValue", "threshval & setval not < 0");
        if (d == 8 && setval > 255)
            throw Lept.Arg("pixThresholdToValue", "setval > 255 for 8 bpp");
        if (d == 16 && setval > 0xffff)
            throw Lept.Arg("pixThresholdToValue", "setval > 0xffff for 16 bpp");

        if (pixd == null)
            pixd = Copy(null, pixs);
        if (setval == threshval) {
            Log.Warning("pixThresholdToValue", "setval == threshval; no operation\n");
            return pixd;
        }

        uint[] datad = pixd.Data;
        pixd.GetDimensions(out int w, out int h, out _);
        int wpld = pixd.Wpl;
        bool setabove = setval > threshval;

        for (int i = 0; i < h; i++) {
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            if (setabove) {
                if (d == 8) {
                    for (int j = 0; j < w; j++) {
                        if (GetDataByte(lined, j) - threshval >= 0)
                            SetDataByte(lined, j, setval);
                    }
                } else if (d == 16) {
                    for (int j = 0; j < w; j++) {
                        if (GetDataTwoBytes(lined, j) - threshval >= 0)
                            SetDataTwoBytes(lined, j, setval);
                    }
                } else {  /* d == 32 */
                    for (int j = 0; j < w; j++) {
                        if (lined[j] >= (uint)threshval)
                            lined[j] = (uint)setval;
                    }
                }
            } else { /* set if below or at threshold */
                if (d == 8) {
                    for (int j = 0; j < w; j++) {
                        if (GetDataByte(lined, j) - threshval <= 0)
                            SetDataByte(lined, j, setval);
                    }
                } else if (d == 16) {
                    for (int j = 0; j < w; j++) {
                        if (GetDataTwoBytes(lined, j) - threshval <= 0)
                            SetDataTwoBytes(lined, j, setval);
                    }
                } else {  /* d == 32 */
                    for (int j = 0; j < w; j++) {
                        if (lined[j] <= (uint)threshval)
                            lined[j] = (uint)setval;
                    }
                }
            }
        }

        return pixd;
    }

    /*-------------------------------------------------------------*
     *            Image accumulator arithmetic operations          *
     *-------------------------------------------------------------*/
    // leptonica: pixInitAccumulate (pixarith.c)
    /// <summary>Makes a 32 bpp accumulator image.</summary>
    /// <param name="w">width of accumulate array</param>
    /// <param name="h">height of accumulate array</param>
    /// <param name="offset">initialize the 32 bpp to have this
    ///                      value; not more than 0x40000000</param>
    /// <returns>pixd, 32 bpp</returns>
    /// <remarks>
    /// (1) %offset must be &gt;= 0.
    /// (2) %offset is used so that we can do arithmetic
    ///     with negative number results on l_uint32 data; it
    ///     prevents the l_uint32 data from going negative.
    /// (3) Because we use l_int32 intermediate data results,
    ///     these should never exceed the max of l_int32 (0x7fffffff).
    ///     We do not permit the offset to be above 0x40000000,
    ///     which is half way between 0 and the max of l_int32.
    /// (4) The same offset should be used for initialization,
    ///     multiplication by a constant, and final extraction!
    /// (5) If you're only adding positive values, %offset can be 0.
    /// </remarks>
    public static Pix InitAccumulate(int w, int h, uint offset)
    {
        if (offset > 0x40000000)
            offset = 0x40000000;
        if (offset == 0)
            return Create(w, h, 32);
        Pix pixd = CreateRawNoInit(w, h, 32);
        pixd.Data.AsSpan(0, pixd.Wpl * h).Fill(offset);
        return pixd;
    }

    // leptonica: pixFinalAccumulate (pixarith.c)
    /// <summary>Converts a 32 bpp accumulator back to 8, 16 or 32 bpp, with clipping.</summary>
    /// <param name="offset">same as used for initialization</param>
    /// <param name="depth">8, 16 or 32 bpp, of destination</param>
    /// <returns>pixd, 8, 16 or 32 bpp</returns>
    /// <remarks>
    /// (1) %offset must be &gt;= 0 and should not exceed 0x40000000.
    /// (2) %offset is subtracted from the src 32 bpp image
    /// (3) For 8 bpp dest, the result is clipped to [0, 0xff]
    /// (4) For 16 bpp dest, the result is clipped to [0, 0xffff]
    /// </remarks>
    public Pix FinalAccumulate(uint offset, int depth)
    {
        Pix pixs = this;
        if (pixs.Depth != 32)
            throw Lept.Arg("pixFinalAccumulate", "pixs not 32 bpp");
        if (depth != 8 && depth != 16 && depth != 32)
            throw Lept.Arg("pixFinalAccumulate", "dest depth not 8, 16, 32 bpp");
        if (offset > 0x40000000)
            offset = 0x40000000;

        pixs.GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, depth);
        pixd.CopyResolution(pixs);  /* but how did pixs get it initially? */
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        if (depth == 8) {
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    int val = (int)(lines[j] - offset);
                    val = Math.Max(0, val);
                    val = Math.Min(255, val);
                    SetDataByte(lined, j, (byte)val);
                }
            }
        } else if (depth == 16) {
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    int val = (int)(lines[j] - offset);
                    val = Math.Max(0, val);
                    val = Math.Min(0xffff, val);
                    SetDataTwoBytes(lined, j, (ushort)val);
                }
            }
        } else {  /* depth == 32 */
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++)
                    lined[j] = lines[j] - offset;
            }
        }

        return pixd;
    }

    // leptonica: pixFinalAccumulateThreshold (pixarith.c)
    /// <summary>Converts a 32 bpp accumulator to 1 bpp by thresholding.</summary>
    /// <param name="offset">same as used for initialization</param>
    /// <param name="threshold">values less than this are set in the destination</param>
    /// <returns>pixd, 1 bpp</returns>
    /// <remarks>
    /// (1) %offset must be &gt;= 0 and should not exceed 0x40000000.
    /// (2) %offset is subtracted from the src 32 bpp image
    /// </remarks>
    public Pix FinalAccumulateThreshold(uint offset, uint threshold)
    {
        Pix pixs = this;
        if (pixs.Depth != 32)
            throw Lept.Arg("pixFinalAccumulateThreshold", "pixs not 32 bpp");
        if (offset > 0x40000000)
            offset = 0x40000000;

        pixs.GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 1);
        pixd.CopyResolution(pixs);  /* but how did pixs get it initially? */
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                int val = (int)(lines[j] - offset);
                if ((uint)val >= threshold) {
                    SetDataBit(lined, j);
                }
            }
        }

        return pixd;
    }

    // leptonica: pixAccumulate (pixarith.c)
    /// <summary>Adds or subtracts each pixs value from this 32 bpp accumulator.</summary>
    /// <param name="pixs">1, 8, 16 or 32 bpp</param>
    /// <param name="op">L_ARITH_ADD or L_ARITH_SUBTRACT</param>
    /// <remarks>
    /// (1) This adds or subtracts each pixs value from pixd.
    /// (2) This clips to the minimum of pixs and pixd, so they
    ///     do not need to be the same size.
    /// (3) The alignment is to the origin [UL corner] of pixs and pixd.
    /// </remarks>
    public void Accumulate(Pix pixs, int op)
    {
        Pix pixd = this;
        if (pixd.Depth != 32)
            throw Lept.Arg("pixAccumulate", "pixd not defined or not 32 bpp");
        int d = pixs.Depth;
        if (d != 1 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixAccumulate", "pixs not 1, 8, 16 or 32 bpp");
        if (op != L.L_ARITH_ADD && op != L.L_ARITH_SUBTRACT)
            throw Lept.Arg("pixAccumulate", "op must be in {L_ARITH_ADD, L_ARITH_SUBTRACT}");

        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        pixs.GetDimensions(out int w, out int h, out _);
        pixd.GetDimensions(out int wd, out int hd, out _);
        w = Math.Min(w, wd);
        h = Math.Min(h, hd);
        if (d == 1) {
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                if (op == L.L_ARITH_ADD) {
                    for (int j = 0; j < w; j++)
                        lined[j] += (uint)GetDataBit(lines, j);
                } else {  /* op == L_ARITH_SUBTRACT */
                    for (int j = 0; j < w; j++)
                        lined[j] -= (uint)GetDataBit(lines, j);
                }
            }
        } else if (d == 8) {
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                if (op == L.L_ARITH_ADD) {
                    for (int j = 0; j < w; j++)
                        lined[j] += (uint)GetDataByte(lines, j);
                } else {  /* op == L_ARITH_SUBTRACT */
                    for (int j = 0; j < w; j++)
                        lined[j] -= (uint)GetDataByte(lines, j);
                }
            }
        } else if (d == 16) {
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                if (op == L.L_ARITH_ADD) {
                    for (int j = 0; j < w; j++)
                        lined[j] += (uint)GetDataTwoBytes(lines, j);
                } else {  /* op == L_ARITH_SUBTRACT */
                    for (int j = 0; j < w; j++)
                        lined[j] -= (uint)GetDataTwoBytes(lines, j);
                }
            }
        } else {  /* d == 32 */
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                if (op == L.L_ARITH_ADD) {
                    for (int j = 0; j < w; j++)
                        lined[j] += lines[j];
                } else {  /* op == L_ARITH_SUBTRACT */
                    for (int j = 0; j < w; j++)
                        lined[j] -= lines[j];
                }
            }
        }
    }

    // leptonica: pixMultConstAccumulate (pixarith.c)
    /// <summary>Multiplies each pixel of a 32 bpp accumulator, relative to offset, by a factor.</summary>
    /// <param name="factor">multiplication factor</param>
    /// <param name="offset">same as used for initialization</param>
    /// <remarks>
    /// (1) %offset must be &gt;= 0 and should not exceed 0x40000000.
    /// (2) This multiplies each pixel, relative to offset, by %factor.
    /// (3) The result is returned with %offset back in place.
    /// </remarks>
    public void MultConstAccumulate(float factor, uint offset)
    {
        if (Depth != 32)
            throw Lept.Arg("pixMultConstAccumulate", "pixs not 32 bpp");
        if (offset > 0x40000000)
            offset = 0x40000000;

        GetDimensions(out int w, out int h, out _);
        uint[] data = Data;
        int wpl = Wpl;
        for (int i = 0; i < h; i++) {
            Span<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w; j++) {
                int val = (int)(line[j] - offset);
                val = (int)(val * factor);
                val += (int)offset;
                line[j] = (uint)val;
            }
        }
    }

    /*-----------------------------------------------------------------------*
     *                      Absolute value of difference                     *
     *-----------------------------------------------------------------------*/
    // leptonica: pixAbsDifference (pixarith.c)
    /// <summary>Absolute value of the difference of two images, component-wise.</summary>
    /// <param name="pixs2">both either 8 or 16 bpp gray, or 32 bpp RGB</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) The depth of pixs1 and pixs2 must be equal.
    /// (2) Clips computation to the min size, aligning the UL corners
    /// (3) For 8 and 16 bpp, assumes one gray component.
    /// (4) For 32 bpp, assumes 3 color components, and ignores the
    ///     LSB of each word (the alpha channel)
    /// (5) Computes the absolute value of the difference between
    ///     each component value.
    /// </remarks>
    public Pix AbsDifference(Pix pixs2)
    {
        Pix pixs1 = this;
        int d = pixs1.Depth;
        if (d != pixs2.Depth)
            throw Lept.Arg("pixAbsDifference", "src1 and src2 depths unequal");
        if (d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixAbsDifference", "depths not in {8, 16, 32}");

        pixs1.GetDimensions(out int w, out int h, out _);
        pixs2.GetDimensions(out int w2, out int h2, out _);
        w = Math.Min(w, w2);
        h = Math.Min(h, h2);
        Pix pixd = CreateNoInit(w, h, d);
        pixd.CopyResolution(pixs1);
        uint[] datas1 = pixs1.Data;
        uint[] datas2 = pixs2.Data;
        uint[] datad = pixd.Data;
        int wpls1 = pixs1.Wpl;
        int wpls2 = pixs2.Wpl;
        int wpld = pixd.Wpl;
        if (d == 8) {
            int fullwords = w >> 2;
            int fullBytes = fullwords << 2;
            for (int i = 0; i < h; i++) {
                ref uint r1 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas1), (nuint)(i * wpls1));
                ref uint r2 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas2), (nuint)(i * wpls2));
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
                ref byte b1 = ref Unsafe.As<uint, byte>(ref r1);
                ref byte b2 = ref Unsafe.As<uint, byte>(ref r2);
                ref byte bd = ref Unsafe.As<uint, byte>(ref rd);
                int b = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; b + 32 <= fullBytes; b += 32) {
                        Vector256<byte> v1 = Vector256.LoadUnsafe(ref b1, (nuint)b);
                        Vector256<byte> v2 = Vector256.LoadUnsafe(ref b2, (nuint)b);
                        (Vector256.Max(v1, v2) - Vector256.Min(v1, v2)).StoreUnsafe(ref bd, (nuint)b);
                    }
                }
                for (; b < fullBytes; b++)
                    Unsafe.Add(ref bd, (nuint)b) = (byte)Math.Abs(Unsafe.Add(ref b1, (nuint)b) - Unsafe.Add(ref b2, (nuint)b));
                for (int j = fullwords << 2; j < w; j++)
                    SetDataByteUnchecked(ref rd, j, Math.Abs(GetDataByteUnchecked(ref r1, j) - GetDataByteUnchecked(ref r2, j)));
            }
        } else if (d == 16) {
            int fullwords = w >> 1;
            int fullHalfs = fullwords << 1;
            for (int i = 0; i < h; i++) {
                ref uint r1 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas1), (nuint)(i * wpls1));
                ref uint r2 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas2), (nuint)(i * wpls2));
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
                ref ushort u1 = ref Unsafe.As<uint, ushort>(ref r1);
                ref ushort u2 = ref Unsafe.As<uint, ushort>(ref r2);
                ref ushort ud = ref Unsafe.As<uint, ushort>(ref rd);
                int k = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; k + 16 <= fullHalfs; k += 16) {
                        Vector256<ushort> v1 = Vector256.LoadUnsafe(ref u1, (nuint)k);
                        Vector256<ushort> v2 = Vector256.LoadUnsafe(ref u2, (nuint)k);
                        (Vector256.Max(v1, v2) - Vector256.Min(v1, v2)).StoreUnsafe(ref ud, (nuint)k);
                    }
                }
                for (; k < fullHalfs; k++)
                    Unsafe.Add(ref ud, (nuint)k) = (ushort)Math.Abs(Unsafe.Add(ref u1, (nuint)k) - Unsafe.Add(ref u2, (nuint)k));
                for (int j = fullwords << 1; j < w; j++)
                    SetDataTwoBytesUnchecked(ref rd, j, Math.Abs(GetDataTwoBytesUnchecked(ref r1, j) - GetDataTwoBytesUnchecked(ref r2, j)));
            }
        } else {  /* d == 32 */
            Vector256<byte> vRgbMask = Vector256.Create(0xffffff00u).AsByte();
            for (int i = 0; i < h; i++) {
                ref uint r1 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas1), (nuint)(i * wpls1));
                ref uint r2 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas2), (nuint)(i * wpls2));
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
                int j = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; j + 8 <= w; j += 8) {
                        Vector256<byte> v1 = Vector256.LoadUnsafe(ref r1, (nuint)j).AsByte();
                        Vector256<byte> v2 = Vector256.LoadUnsafe(ref r2, (nuint)j).AsByte();
                        ((Vector256.Max(v1, v2) - Vector256.Min(v1, v2)) & vRgbMask).AsUInt32().StoreUnsafe(ref rd, (nuint)j);
                    }
                }
                for (; j < w; j++) {
                    ExtractRGBValues(Unsafe.Add(ref r1, (nuint)j), out int rval1, out int gval1, out int bval1);
                    ExtractRGBValues(Unsafe.Add(ref r2, (nuint)j), out int rval2, out int gval2, out int bval2);
                    ComposeRGBPixel(Math.Abs(rval1 - rval2), Math.Abs(gval1 - gval2), Math.Abs(bval1 - bval2), out Unsafe.Add(ref rd, (nuint)j));
                }
            }
        }

        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *                           Sum of color images                         *
     *-----------------------------------------------------------------------*/
    // leptonica: pixAddRGB (pixarith.c)
    /// <summary>Component-wise sum of two 32 bpp RGB or colormapped images.</summary>
    /// <param name="pixs2">32 bpp RGB, or colormapped (as is this image)</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Clips computation to the minimum size, aligning the UL corners.
    /// (2) Removes any colormap to RGB, and ignores the LSB of each
    ///     pixel word (the alpha channel).
    /// (3) Adds each component value, pixelwise, clipping to 255.
    /// (4) This is useful to combine two images where most of the
    ///     pixels are essentially black, such as in pixPerceptualDiff().
    /// </remarks>
    public Pix AddRGB(Pix pixs2)
    {
        Pix pixs1 = this;
        pixs1.GetDimensions(out int w, out int h, out int d);
        pixs2.GetDimensions(out int w2, out int h2, out int d2);
        if (pixs1.Colormap == null && d != 32)
            throw Lept.Arg("pixAddRGB", "pixs1 not cmapped or rgb");
        if (pixs2.Colormap == null && d2 != 32)
            throw Lept.Arg("pixAddRGB", "pixs2 not cmapped or rgb");
        Pix pixc1 = pixs1.Colormap != null ? pixs1.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR) : pixs1;
        Pix pixc2 = pixs2.Colormap != null ? pixs2.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR) : pixs2;

        w = Math.Min(w, w2);
        h = Math.Min(h, h2);
        Pix pixd = CreateRawNoInit(w, h, 32);
        pixd.CopyResolution(pixs1);
        uint[] datac1 = pixc1.Data;
        uint[] datac2 = pixc2.Data;
        uint[] datad = pixd.Data;
        int wplc1 = pixc1.Wpl;
        int wplc2 = pixc2.Wpl;
        int wpld = pixd.Wpl;
        Vector256<byte> vMax = Vector256.Create((byte)255);
        Vector256<byte> vRgbMask = Vector256.Create(0xffffff00u).AsByte();
        for (int i = 0; i < h; i++) {
            ref uint r1 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datac1), (nuint)(i * wplc1));
            ref uint r2 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datac2), (nuint)(i * wplc2));
            ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
            int j = 0;
            if (Vector256.IsHardwareAccelerated) {
                for (; j + 8 <= w; j += 8) {
                    Vector256<byte> v1 = Vector256.LoadUnsafe(ref r1, (nuint)j).AsByte();
                    Vector256<byte> v2 = Vector256.LoadUnsafe(ref r2, (nuint)j).AsByte();
                    ((v2 + Vector256.Min(v1, vMax - v2)) & vRgbMask).AsUInt32().StoreUnsafe(ref rd, (nuint)j);
                }
            }
            for (; j < w; j++) {
                ExtractRGBValues(Unsafe.Add(ref r1, (nuint)j), out int rval1, out int gval1, out int bval1);
                ExtractRGBValues(Unsafe.Add(ref r2, (nuint)j), out int rval2, out int gval2, out int bval2);
                ComposeRGBPixel(Math.Min(255, rval1 + rval2), Math.Min(255, gval1 + gval2), Math.Min(255, bval1 + bval2), out Unsafe.Add(ref rd, (nuint)j));
            }
        }

        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *             Two-image min and max operations (8 and 16 bpp)           *
     *-----------------------------------------------------------------------*/
    // leptonica: pixMinOrMax (pixarith.c)
    /// <summary>Computes the component-wise minimum or maximum of two images.</summary>
    /// <param name="pixd">Optional destination; can be <c>null</c>, the same object as <paramref name="pixs1"/>, or a different image.</param>
    /// <param name="pixs1">The first source image; can be the same object as <paramref name="pixd"/>.</param>
    /// <param name="pixs2">The second source image; must differ from <paramref name="pixs1"/>.</param>
    /// <param name="type"><see cref="L.L_CHOOSE_MIN"/> or <see cref="L.L_CHOOSE_MAX"/>.</param>
    /// <returns>The destination image: a new image, or <paramref name="pixd"/>.</returns>
    /// <exception cref="ArgumentException">The sources are not valid or <paramref name="type"/> is invalid.</exception>
    /// <remarks>
    /// <para>The depth can be 8 or 16 bpp for one component, and 32 bpp for a three-component image.
    /// For 32 bpp, the least significant byte of each word (the alpha channel) is ignored.</para>
    /// <para>The result goes to a new image if <paramref name="pixd"/> is <c>null</c>, in place to
    /// <paramref name="pixs1"/> if <paramref name="pixd"/> is the same object, or to the given
    /// <paramref name="pixd"/> otherwise.</para>
    /// </remarks>
    public static Pix MinOrMax(Pix? pixd, Pix pixs1, Pix pixs2, int type)
    {
        if (pixs1 == pixs2)
            throw Lept.Arg("pixMinOrMax", "pixs1 and pixs2 must differ");
        if (type != L.L_CHOOSE_MIN && type != L.L_CHOOSE_MAX)
            throw Lept.Arg("pixMinOrMax", "invalid type");
        int d = pixs1.Depth;
        if (pixs2.Depth != d)
            throw Lept.Arg("pixMinOrMax", "depths unequal");
        if (d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixMinOrMax", "depth not 8, 16 or 32 bpp");

        if (pixs1 != pixd)
            pixd = Copy(pixd, pixs1);

        pixs2.GetDimensions(out int ws, out int hs, out _);
        pixd!.GetDimensions(out int w, out int h, out _);
        w = Math.Min(w, ws);
        h = Math.Min(h, hs);
        uint[] datas = pixs2.Data;
        uint[] datad = pixd.Data;
        int wpls = pixs2.Wpl;
        int wpld = pixd.Wpl;
        bool isMin = type == L.L_CHOOSE_MIN;
        if (d == 8) {
            int fullwords = w >> 2;
            int fullBytes = fullwords << 2;
            for (int i = 0; i < h; i++) {
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
                ref byte bs = ref Unsafe.As<uint, byte>(ref rs);
                ref byte bd = ref Unsafe.As<uint, byte>(ref rd);
                int b = 0;
                if (Vector256.IsHardwareAccelerated) {
                    if (isMin) {
                        for (; b + 32 <= fullBytes; b += 32)
                            Vector256.Min(Vector256.LoadUnsafe(ref bd, (nuint)b), Vector256.LoadUnsafe(ref bs, (nuint)b)).StoreUnsafe(ref bd, (nuint)b);
                    } else {
                        for (; b + 32 <= fullBytes; b += 32)
                            Vector256.Max(Vector256.LoadUnsafe(ref bd, (nuint)b), Vector256.LoadUnsafe(ref bs, (nuint)b)).StoreUnsafe(ref bd, (nuint)b);
                    }
                }
                for (; b < fullBytes; b++) {
                    byte v1 = Unsafe.Add(ref bd, (nuint)b), v2 = Unsafe.Add(ref bs, (nuint)b);
                    Unsafe.Add(ref bd, (nuint)b) = isMin ? Math.Min(v1, v2) : Math.Max(v1, v2);
                }
                for (int j = fullwords << 2; j < w; j++) {
                    int vals = GetDataByteUnchecked(ref rs, j);
                    int vald = GetDataByteUnchecked(ref rd, j);
                    SetDataByteUnchecked(ref rd, j, isMin ? Math.Min(vals, vald) : Math.Max(vals, vald));
                }
            }
        } else if (d == 16) {
            int fullwords = w >> 1;
            int fullHalfs = fullwords << 1;
            for (int i = 0; i < h; i++) {
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
                ref ushort us = ref Unsafe.As<uint, ushort>(ref rs);
                ref ushort ud = ref Unsafe.As<uint, ushort>(ref rd);
                int k = 0;
                if (Vector256.IsHardwareAccelerated) {
                    if (isMin) {
                        for (; k + 16 <= fullHalfs; k += 16)
                            Vector256.Min(Vector256.LoadUnsafe(ref ud, (nuint)k), Vector256.LoadUnsafe(ref us, (nuint)k)).StoreUnsafe(ref ud, (nuint)k);
                    } else {
                        for (; k + 16 <= fullHalfs; k += 16)
                            Vector256.Max(Vector256.LoadUnsafe(ref ud, (nuint)k), Vector256.LoadUnsafe(ref us, (nuint)k)).StoreUnsafe(ref ud, (nuint)k);
                    }
                }
                for (; k < fullHalfs; k++) {
                    ushort v1 = Unsafe.Add(ref ud, (nuint)k), v2 = Unsafe.Add(ref us, (nuint)k);
                    Unsafe.Add(ref ud, (nuint)k) = isMin ? Math.Min(v1, v2) : Math.Max(v1, v2);
                }
                for (int j = fullwords << 1; j < w; j++) {
                    int vals = GetDataTwoBytesUnchecked(ref rs, j);
                    int vald = GetDataTwoBytesUnchecked(ref rd, j);
                    SetDataTwoBytesUnchecked(ref rd, j, isMin ? Math.Min(vals, vald) : Math.Max(vals, vald));
                }
            }
        } else {
            Vector256<byte> vRgbMask = Vector256.Create(0xffffff00u).AsByte();
            for (int i = 0; i < h; i++) {
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
                int j = 0;
                if (Vector256.IsHardwareAccelerated) {
                    if (isMin) {
                        for (; j + 8 <= w; j += 8)
                            (Vector256.Min(Vector256.LoadUnsafe(ref rd, (nuint)j).AsByte(), Vector256.LoadUnsafe(ref rs, (nuint)j).AsByte()) & vRgbMask).AsUInt32().StoreUnsafe(ref rd, (nuint)j);
                    } else {
                        for (; j + 8 <= w; j += 8)
                            (Vector256.Max(Vector256.LoadUnsafe(ref rd, (nuint)j).AsByte(), Vector256.LoadUnsafe(ref rs, (nuint)j).AsByte()) & vRgbMask).AsUInt32().StoreUnsafe(ref rd, (nuint)j);
                    }
                }
                for (; j < w; j++) {
                    ExtractRGBValues(Unsafe.Add(ref rs, (nuint)j), out int rval1, out int gval1, out int bval1);
                    ExtractRGBValues(Unsafe.Add(ref rd, (nuint)j), out int rval2, out int gval2, out int bval2);
                    if (isMin)
                        ComposeRGBPixel(Math.Min(rval1, rval2), Math.Min(gval1, gval2), Math.Min(bval1, bval2), out Unsafe.Add(ref rd, (nuint)j));
                    else
                        ComposeRGBPixel(Math.Max(rval1, rval2), Math.Max(gval1, gval2), Math.Max(bval1, bval2), out Unsafe.Add(ref rd, (nuint)j));
                }
            }
        }

        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *                    Scale for maximum dynamic range                    *
     *-----------------------------------------------------------------------*/
    // L_MAX(int, l_uint32): the int operand is converted to unsigned for the
    // comparison, and the result is stored back into an l_int32.
    private static int MaxIntUint(int a, uint b)
    {
        return ((uint)a > b) ? a : (int)b;
    }

    // leptonica: pixMaxDynamicRange (pixarith.c)
    /// <summary>Scales pixel values of a 4, 8, 16 or 32 bpp image to fit maximally within an 8 bpp image.</summary>
    /// <param name="type">L_LINEAR_SCALE or L_LOG_SCALE</param>
    /// <returns>pixd, 8 bpp</returns>
    /// <remarks>
    /// (1) Scales pixel values to fit maximally within the dest 8 bpp pixd
    /// (2) Assumes the source 'pixels' are a 1-component scalar.  For
    ///     a 32 bpp source, each pixel is treated as a single number --
    ///     not as a 3-component rgb pixel value.
    /// (3) Uses a LUT for log scaling.
    /// </remarks>
    public Pix MaxDynamicRange(int type)
    {
        Pix pixs = this;
        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixMaxDynamicRange", "pixs not in {4,8,16,32} bpp");
        if (type != L.L_LINEAR_SCALE && type != L.L_LOG_SCALE)
            throw Lept.Arg("pixMaxDynamicRange", "invalid type");

        Pix pixd = Create(w, h, 8);
        pixd.CopyResolution(pixs);
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;

            /* Get max */
        int max = 0;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            for (int j = 0; j < wpls; j++) {
                uint word = lines[j];
                if (d == 4) {
                    max = MaxIntUint(max, word >> 28);
                    max = MaxIntUint(max, (word >> 24) & 0xf);
                    max = MaxIntUint(max, (word >> 20) & 0xf);
                    max = MaxIntUint(max, (word >> 16) & 0xf);
                    max = MaxIntUint(max, (word >> 12) & 0xf);
                    max = MaxIntUint(max, (word >> 8) & 0xf);
                    max = MaxIntUint(max, (word >> 4) & 0xf);
                    max = MaxIntUint(max, word & 0xf);
                } else if (d == 8) {
                    max = MaxIntUint(max, word >> 24);
                    max = MaxIntUint(max, (word >> 16) & 0xff);
                    max = MaxIntUint(max, (word >> 8) & 0xff);
                    max = MaxIntUint(max, word & 0xff);
                } else if (d == 16) {
                    max = MaxIntUint(max, word >> 16);
                    max = MaxIntUint(max, word & 0xffff);
                } else {  /* d == 32 (rgb) */
                    max = MaxIntUint(max, word);
                }
            }
        }

            /* Map to the full dynamic range */
        float factor;
        float[] tab;
        if (d == 4) {
            if (type == L.L_LINEAR_SCALE) {
                factor = 255.0f / (float)max;
                for (int i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    for (int j = 0; j < w; j++) {
                        uint sval = (uint)GetDataQbit(lines, j);
                        byte dval = (byte)(int)(factor * (float)sval + 0.5);
                        // NOTE(upstream): pixd is 8 bpp, but the value is stored as a 4 bpp pixel.
                        SetDataQbit(lined, j, dval);
                    }
                }
            } else {  /* type == L_LOG_SCALE) */
                tab = MakeLogBase2Tab();
                factor = 255.0f / GetLogBase2(max, tab);
                for (int i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    for (int j = 0; j < w; j++) {
                        uint sval = (uint)GetDataQbit(lines, j);
                        byte dval = (byte)(int)(factor * GetLogBase2((int)sval, tab) + 0.5);
                        SetDataByte(lined, j, dval);
                    }
                }
            }
        } else if (d == 8) {
            if (type == L.L_LINEAR_SCALE) {
                factor = 255.0f / (float)max;
                for (int i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    for (int j = 0; j < w; j++) {
                        uint sval = (uint)GetDataByte(lines, j);
                        byte dval = (byte)(int)(factor * (float)sval + 0.5);
                        SetDataByte(lined, j, dval);
                    }
                }
            } else {  /* type == L_LOG_SCALE) */
                tab = MakeLogBase2Tab();
                factor = 255.0f / GetLogBase2(max, tab);
                for (int i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    for (int j = 0; j < w; j++) {
                        uint sval = (uint)GetDataByte(lines, j);
                        byte dval = (byte)(int)(factor * GetLogBase2((int)sval, tab) + 0.5);
                        SetDataByte(lined, j, dval);
                    }
                }
            }
        } else if (d == 16) {
            if (type == L.L_LINEAR_SCALE) {
                factor = 255.0f / (float)max;
                for (int i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    for (int j = 0; j < w; j++) {
                        uint sval = (uint)GetDataTwoBytes(lines, j);
                        byte dval = (byte)(int)(factor * (float)sval + 0.5);
                        SetDataByte(lined, j, dval);
                    }
                }
            } else {  /* type == L_LOG_SCALE) */
                tab = MakeLogBase2Tab();
                factor = 255.0f / GetLogBase2(max, tab);
                for (int i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    for (int j = 0; j < w; j++) {
                        uint sval = (uint)GetDataTwoBytes(lines, j);
                        byte dval = (byte)(int)(factor * GetLogBase2((int)sval, tab) + 0.5);
                        SetDataByte(lined, j, dval);
                    }
                }
            }
        } else {  /* d == 32 */
            if (type == L.L_LINEAR_SCALE) {
                factor = 255.0f / (float)max;
                for (int i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    for (int j = 0; j < w; j++) {
                        uint sval = lines[j];
                        byte dval = (byte)(int)(factor * (float)sval + 0.5);
                        SetDataByte(lined, j, dval);
                    }
                }
            } else {  /* type == L_LOG_SCALE) */
                tab = MakeLogBase2Tab();
                factor = 255.0f / GetLogBase2(max, tab);
                for (int i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    for (int j = 0; j < w; j++) {
                        uint sval = lines[j];
                        byte dval = (byte)(int)(factor * GetLogBase2((int)sval, tab) + 0.5);
                        SetDataByte(lined, j, dval);
                    }
                }
            }
        }

        return pixd;
    }

    // leptonica: pixMaxDynamicRangeRGB (pixarith.c)
    /// <summary>Scales the components of a 32 bpp rgb image to fit maximally within 8 bits.</summary>
    /// <param name="type">L_LINEAR_SCALE or L_LOG_SCALE</param>
    /// <returns>pixd, 32 bpp</returns>
    /// <remarks>
    /// (1) Scales pixel values to fit maximally within a 32 bpp dest pixd
    /// (2) All color components are scaled with the same factor, based
    ///     on the maximum r, g or b component in the image.  This should
    ///     not be used if the 32-bit value is a single number (e.g., a
    ///     count in a histogram generated by pixMakeHistoHS()).
    /// (3) Uses a LUT for log scaling.
    /// </remarks>
    public Pix MaxDynamicRangeRGB(int type)
    {
        Pix pixs = this;
        if (pixs.Depth != 32)
            throw Lept.Arg("pixMaxDynamicRangeRGB", "pixs undefined or not 32 bpp");
        if (type != L.L_LINEAR_SCALE && type != L.L_LOG_SCALE)
            throw Lept.Arg("pixMaxDynamicRangeRGB", "invalid type");

            /* Get max */
        Pix pixd = CreateTemplate(pixs);
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        pixs.GetDimensions(out int w, out int h, out _);
        int max = 0;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            for (int j = 0; j < wpls; j++) {
                uint word = lines[j];
                max = MaxIntUint(max, word >> 24);
                max = MaxIntUint(max, (word >> 16) & 0xff);
                max = MaxIntUint(max, (word >> 8) & 0xff);
            }
        }
        if (max == 0) {
            Log.Warning("pixMaxDynamicRangeRGB", "max = 0; setting to 1\n");
            max = 1;
        }

            /* Map to the full dynamic range */
        float factor;
        if (type == L.L_LINEAR_SCALE) {
            factor = 255.0f / (float)max;
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    uint sval = lines[j];
                    uint dval = LinearScaleRGBVal(sval, factor);
                    lined[j] = dval;
                }
            }
        } else {  /* type == L_LOG_SCALE) */
            float[] tab = MakeLogBase2Tab();
            factor = 255.0f / GetLogBase2(max, tab);
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    uint sval = lines[j];
                    uint dval = LogScaleRGBVal(sval, tab, factor);
                    lined[j] = dval;
                }
            }
        }

        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *                         RGB pixel value scaling                       *
     *-----------------------------------------------------------------------*/
    // leptonica: linearScaleRGBVal (pixarith.c)
    /// <summary>Linearly scales the r, g and b components of a 32-bit rgb pixel.</summary>
    /// <param name="sval">32-bit rgb pixel value</param>
    /// <param name="factor">multiplication factor on each component</param>
    /// <returns>dval  linearly scaled version of %sval</returns>
    /// <remarks>
    /// (1) %factor must be chosen to be not greater than (255 / maxcomp),
    ///     where maxcomp is the maximum value of the pixel components.
    ///     Otherwise, the product will overflow a uint8.  In use, factor
    ///     is the same for all pixels in a pix.
    /// (2) No scaling is performed on the transparency ("A") component.
    /// </remarks>
    public static uint LinearScaleRGBVal(uint sval, float factor)
    {
        uint dval = ((uint)((byte)(int)(factor * (sval >> 24) + 0.5f) << 24)) |
                    ((uint)((byte)(int)(factor * ((sval >> 16) & 0xff) + 0.5f) << 16)) |
                    ((uint)((byte)(int)(factor * ((sval >> 8) & 0xff) + 0.5f) << 8)) |
                    (sval & 0xff);
        return dval;
    }

    // leptonica: logScaleRGBVal (pixarith.c)
    /// <summary>Log-scales the r, g and b components of a 32-bit rgb pixel.</summary>
    /// <param name="sval">32-bit rgb pixel value</param>
    /// <param name="tab">256 entry log-base-2 table</param>
    /// <param name="factor">multiplication factor on each component</param>
    /// <returns>dval  log scaled version of %sval</returns>
    /// <remarks>
    /// (1) %tab is made with makeLogBase2Tab().
    /// (2) %factor must be chosen to be not greater than
    ///     255.0 / log[base2](maxcomp), where maxcomp is the maximum
    ///     value of the pixel components.  Otherwise, the product
    ///     will overflow a uint8.  In use, factor is the same for
    ///     all pixels in a pix.
    /// (3) No scaling is performed on the transparency ("A") component.
    /// </remarks>
    public static uint LogScaleRGBVal(uint sval, float[] tab, float factor)
    {
        uint dval = ((uint)((byte)(int)(factor * GetLogBase2((int)(sval >> 24), tab) + 0.5f) << 24)) |
                    ((uint)((byte)(int)(factor * GetLogBase2((int)((sval >> 16) & 0xff), tab) + 0.5f)
                              << 16)) |
                    ((uint)((byte)(int)(factor * GetLogBase2((int)((sval >> 8) & 0xff), tab) + 0.5f)
                              << 8)) |
                    (sval & 0xff);
        return dval;
    }

    /*-----------------------------------------------------------------------*
     *                            Log base2 lookup                           *
     *-----------------------------------------------------------------------*/
    // leptonica: makeLogBase2Tab (pixarith.c)
    /// <returns>tab   table giving the log[base2] of values from 1 to 255</returns>
    public static float[] MakeLogBase2Tab()
    {
        float[] tab = new float[256];

        float log2 = (float)Math.Log((double)(float)2);
        for (int i = 0; i < 256; i++)
            tab[i] = (float)Math.Log((double)(float)i) / log2;

        return tab;
    }

    // leptonica: getLogBase2 (pixarith.c)
    /// <param name="val">in range [0 ... 255]</param>
    /// <param name="logtab">256-entry table of logs</param>
    /// <returns>logval    log[base2] of %val</returns>
    public static float GetLogBase2(int val, float[] logtab)
    {
        if (val < 0x100)
            return logtab[val];
        else if (val < 0x10000)
            return 8.0f + logtab[val >> 8];
        else if (val < 0x1000000)
            return 16.0f + logtab[val >> 16];
        else
            return 24.0f + logtab[val >> 24];
    }
}
