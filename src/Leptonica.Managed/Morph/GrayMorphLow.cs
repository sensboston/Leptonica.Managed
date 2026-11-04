using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Leptonica;

/*
 *  graymorph.c, low-level part: van Herk / Gil-Werman grayscale
 *  dilation and erosion along one direction.
 *
 *      Low-level grayscale morphological operations
 *            private static Pix.DilateGrayLow
 *            private static Pix.ErodeGrayLow
 *
 *  The two functions are static in graymorph.c; they are kept in a separate
 *  file here and are used by the top-level functions in GrayMorph.cs.
 */
public sealed partial class Pix
{
    /*-----------------------------------------------------------------*
     *              Low-level gray morphological operations            *
     *-----------------------------------------------------------------*/
    // leptonica: dilateGrayLow (graymorph.c)
    /// <summary>Low-level gray dilation along one direction (van Herk / Gil-Werman).</summary>
    private static void DilateGrayLow(uint[] datad, int w, int h, int wpld,
                                      uint[] datas, int wpls, int size,
                                      int direction, byte[] buffer, byte[] maxarray)
    {
        int hsize = size / 2;
        if (direction == L.L_HORIZ) {
            int nsteps = (w - 2 * hsize) / size;
            if (nsteps <= 0)
                return;
            int endCol = hsize + nsteps * size;

            LeptParallel.ForRows(h, (long)w * 8L, (yStart, yEnd) => {
                byte[] rented = ArrayPool<byte>.Shared.Rent(w * 2 + 2 * size + 64);
                try {
                    Span<byte> inBuf = rented.AsSpan(0, w);
                    Span<byte> outBuf = rented.AsSpan(w, w);
                    Span<byte> maxArr = rented.AsSpan(2 * w, 2 * size);
                    ref byte inRef = ref MemoryMarshal.GetReference(inBuf);
                    ref byte outRef = ref MemoryMarshal.GetReference(outBuf);
                    ref byte maxRef = ref MemoryMarshal.GetReference(maxArr);
                    ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
                    ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);

                    for (int i = yStart; i < yEnd; i++) {
                        UnpackMorphRow(ref Unsafe.Add(ref baseS, (nuint)(i * wpls)), w, ref inRef);

                        for (int step = 0; step < nsteps; step++) {
                            int startmax = (step + 1) * size - 1;
                            Unsafe.Add(ref maxRef, (nuint)(size - 1)) = Unsafe.Add(ref inRef, (nuint)startmax);
                            for (int k = 1; k < size; k++) {
                                Unsafe.Add(ref maxRef, (nuint)(size - 1 - k)) =
                                    Math.Max(Unsafe.Add(ref maxRef, (nuint)(size - k)), Unsafe.Add(ref inRef, (nuint)(startmax - k)));
                                Unsafe.Add(ref maxRef, (nuint)(size - 1 + k)) =
                                    Math.Max(Unsafe.Add(ref maxRef, (nuint)(size + k - 2)), Unsafe.Add(ref inRef, (nuint)(startmax + k)));
                            }

                            int startx = hsize + step * size;
                            Unsafe.Add(ref outRef, (nuint)startx) = maxRef;
                            Unsafe.Add(ref outRef, (nuint)(startx + size - 1)) = Unsafe.Add(ref maxRef, (nuint)(2 * size - 2));
                            for (int k = 1; k < size - 1; k++) {
                                Unsafe.Add(ref outRef, (nuint)(startx + k)) =
                                    Math.Max(Unsafe.Add(ref maxRef, (nuint)k), Unsafe.Add(ref maxRef, (nuint)(k + size - 1)));
                            }
                        }

                        PackMorphRowSlice(ref outRef, ref Unsafe.Add(ref baseD, (nuint)(i * wpld)), hsize, endCol);
                    }
                } finally {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            });
        } else {  /* direction == L_VERT */
            int nsteps = (h - 2 * hsize) / size;
            if (nsteps <= 0)
                return;
            int fullWords = w >> 2;

            if (fullWords > 0) {
                LeptParallel.ForRows(fullWords, (long)h * 32L, (wStart, wEnd) => {
                    ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
                    ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
                    int jw = wStart;

                    if (Vector256.IsHardwareAccelerated && jw + 8 <= wEnd) {
                        Vector256<byte>[] vArr = ArrayPool<Vector256<byte>>.Shared.Rent(2 * size);
                        try {
                            for (; jw + 8 <= wEnd; jw += 8) {
                                for (int step = 0; step < nsteps; step++) {
                                    int startmax = (step + 1) * size - 1;
                                    vArr[size - 1] = LoadVec256(ref baseS, startmax * wpls + jw);
                                    for (int k = 1; k < size; k++) {
                                        vArr[size - 1 - k] = Vector256.Max(vArr[size - k], LoadVec256(ref baseS, (startmax - k) * wpls + jw));
                                        vArr[size - 1 + k] = Vector256.Max(vArr[size + k - 2], LoadVec256(ref baseS, (startmax + k) * wpls + jw));
                                    }
                                    int starty = hsize + step * size;
                                    StoreVec256(ref baseD, starty * wpld + jw, vArr[0]);
                                    StoreVec256(ref baseD, (starty + size - 1) * wpld + jw, vArr[2 * size - 2]);
                                    for (int k = 1; k < size - 1; k++)
                                        StoreVec256(ref baseD, (starty + k) * wpld + jw, Vector256.Max(vArr[k], vArr[k + size - 1]));
                                }
                            }
                        } finally {
                            ArrayPool<Vector256<byte>>.Shared.Return(vArr);
                        }
                    }

                    if (Vector128.IsHardwareAccelerated && jw < wEnd) {
                        Vector128<byte>[] vArr128 = ArrayPool<Vector128<byte>>.Shared.Rent(2 * size);
                        try {
                            for (; jw + 4 <= wEnd; jw += 4) {
                                for (int step = 0; step < nsteps; step++) {
                                    int startmax = (step + 1) * size - 1;
                                    vArr128[size - 1] = LoadVec128(ref baseS, startmax * wpls + jw);
                                    for (int k = 1; k < size; k++) {
                                        vArr128[size - 1 - k] = Vector128.Max(vArr128[size - k], LoadVec128(ref baseS, (startmax - k) * wpls + jw));
                                        vArr128[size - 1 + k] = Vector128.Max(vArr128[size + k - 2], LoadVec128(ref baseS, (startmax + k) * wpls + jw));
                                    }
                                    int starty = hsize + step * size;
                                    StoreVec128(ref baseD, starty * wpld + jw, vArr128[0]);
                                    StoreVec128(ref baseD, (starty + size - 1) * wpld + jw, vArr128[2 * size - 2]);
                                    for (int k = 1; k < size - 1; k++)
                                        StoreVec128(ref baseD, (starty + k) * wpld + jw, Vector128.Max(vArr128[k], vArr128[k + size - 1]));
                                }
                            }
                            for (; jw < wEnd; jw++) {
                                for (int step = 0; step < nsteps; step++) {
                                    int startmax = (step + 1) * size - 1;
                                    vArr128[size - 1] = Vector128.CreateScalar(Unsafe.Add(ref baseS, (nuint)(startmax * wpls + jw))).AsByte();
                                    for (int k = 1; k < size; k++) {
                                        Vector128<byte> vUp = Vector128.CreateScalar(Unsafe.Add(ref baseS, (nuint)((startmax - k) * wpls + jw))).AsByte();
                                        Vector128<byte> vDn = Vector128.CreateScalar(Unsafe.Add(ref baseS, (nuint)((startmax + k) * wpls + jw))).AsByte();
                                        vArr128[size - 1 - k] = Vector128.Max(vArr128[size - k], vUp);
                                        vArr128[size - 1 + k] = Vector128.Max(vArr128[size + k - 2], vDn);
                                    }
                                    int starty = hsize + step * size;
                                    Unsafe.Add(ref baseD, (nuint)(starty * wpld + jw)) = vArr128[0].AsUInt32().ToScalar();
                                    Unsafe.Add(ref baseD, (nuint)((starty + size - 1) * wpld + jw)) = vArr128[2 * size - 2].AsUInt32().ToScalar();
                                    for (int k = 1; k < size - 1; k++)
                                        Unsafe.Add(ref baseD, (nuint)((starty + k) * wpld + jw)) = Vector128.Max(vArr128[k], vArr128[k + size - 1]).AsUInt32().ToScalar();
                                }
                            }
                        } finally {
                            ArrayPool<Vector128<byte>>.Shared.Return(vArr128);
                        }
                    } else if (jw < wEnd) {
                        byte[] colBuf = ArrayPool<byte>.Shared.Rent(h + 2 * size);
                        try {
                            Span<byte> bufSpan = colBuf.AsSpan(0, h);
                            Span<byte> maxSpan = colBuf.AsSpan(h, 2 * size);
                            for (int j = jw << 2; j < wEnd << 2; j++)
                                DilateVertColumnScalar(ref baseS, wpls, ref baseD, wpld, h, size, hsize, nsteps, j, bufSpan, maxSpan);
                        } finally {
                            ArrayPool<byte>.Shared.Return(colBuf);
                        }
                    }
                });
            }

            if ((fullWords << 2) < w) {
                ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
                for (int j = fullWords << 2; j < w; j++)
                    DilateVertColumnScalar(ref baseS, wpls, ref baseD, wpld, h, size, hsize, nsteps, j, buffer.AsSpan(0, h), maxarray.AsSpan(0, 2 * size));
            }
        }
    }

    // leptonica: erodeGrayLow (graymorph.c)
    /// <summary>Low-level gray erosion along one direction (van Herk / Gil-Werman).</summary>
    private static void ErodeGrayLow(uint[] datad, int w, int h, int wpld,
                                     uint[] datas, int wpls, int size,
                                     int direction, byte[] buffer, byte[] minarray)
    {
        int hsize = size / 2;
        if (direction == L.L_HORIZ) {
            int nsteps = (w - 2 * hsize) / size;
            if (nsteps <= 0)
                return;
            int endCol = hsize + nsteps * size;

            LeptParallel.ForRows(h, (long)w * 8L, (yStart, yEnd) => {
                byte[] rented = ArrayPool<byte>.Shared.Rent(w * 2 + 2 * size + 64);
                try {
                    Span<byte> inBuf = rented.AsSpan(0, w);
                    Span<byte> outBuf = rented.AsSpan(w, w);
                    Span<byte> minArr = rented.AsSpan(2 * w, 2 * size);
                    ref byte inRef = ref MemoryMarshal.GetReference(inBuf);
                    ref byte outRef = ref MemoryMarshal.GetReference(outBuf);
                    ref byte minRef = ref MemoryMarshal.GetReference(minArr);
                    ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
                    ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);

                    for (int i = yStart; i < yEnd; i++) {
                        UnpackMorphRow(ref Unsafe.Add(ref baseS, (nuint)(i * wpls)), w, ref inRef);

                        for (int step = 0; step < nsteps; step++) {
                            int startmin = (step + 1) * size - 1;
                            Unsafe.Add(ref minRef, (nuint)(size - 1)) = Unsafe.Add(ref inRef, (nuint)startmin);
                            for (int k = 1; k < size; k++) {
                                Unsafe.Add(ref minRef, (nuint)(size - 1 - k)) =
                                    Math.Min(Unsafe.Add(ref minRef, (nuint)(size - k)), Unsafe.Add(ref inRef, (nuint)(startmin - k)));
                                Unsafe.Add(ref minRef, (nuint)(size - 1 + k)) =
                                    Math.Min(Unsafe.Add(ref minRef, (nuint)(size + k - 2)), Unsafe.Add(ref inRef, (nuint)(startmin + k)));
                            }

                            int startx = hsize + step * size;
                            Unsafe.Add(ref outRef, (nuint)startx) = minRef;
                            Unsafe.Add(ref outRef, (nuint)(startx + size - 1)) = Unsafe.Add(ref minRef, (nuint)(2 * size - 2));
                            for (int k = 1; k < size - 1; k++) {
                                Unsafe.Add(ref outRef, (nuint)(startx + k)) =
                                    Math.Min(Unsafe.Add(ref minRef, (nuint)k), Unsafe.Add(ref minRef, (nuint)(k + size - 1)));
                            }
                        }

                        PackMorphRowSlice(ref outRef, ref Unsafe.Add(ref baseD, (nuint)(i * wpld)), hsize, endCol);
                    }
                } finally {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            });
        } else {  /* direction == L_VERT */
            int nsteps = (h - 2 * hsize) / size;
            if (nsteps <= 0)
                return;
            int fullWords = w >> 2;

            if (fullWords > 0) {
                LeptParallel.ForRows(fullWords, (long)h * 32L, (wStart, wEnd) => {
                    ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
                    ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
                    int jw = wStart;

                    if (Vector256.IsHardwareAccelerated && jw + 8 <= wEnd) {
                        Vector256<byte>[] vArr = ArrayPool<Vector256<byte>>.Shared.Rent(2 * size);
                        try {
                            for (; jw + 8 <= wEnd; jw += 8) {
                                for (int step = 0; step < nsteps; step++) {
                                    int startmin = (step + 1) * size - 1;
                                    vArr[size - 1] = LoadVec256(ref baseS, startmin * wpls + jw);
                                    for (int k = 1; k < size; k++) {
                                        vArr[size - 1 - k] = Vector256.Min(vArr[size - k], LoadVec256(ref baseS, (startmin - k) * wpls + jw));
                                        vArr[size - 1 + k] = Vector256.Min(vArr[size + k - 2], LoadVec256(ref baseS, (startmin + k) * wpls + jw));
                                    }
                                    int starty = hsize + step * size;
                                    StoreVec256(ref baseD, starty * wpld + jw, vArr[0]);
                                    StoreVec256(ref baseD, (starty + size - 1) * wpld + jw, vArr[2 * size - 2]);
                                    for (int k = 1; k < size - 1; k++)
                                        StoreVec256(ref baseD, (starty + k) * wpld + jw, Vector256.Min(vArr[k], vArr[k + size - 1]));
                                }
                            }
                        } finally {
                            ArrayPool<Vector256<byte>>.Shared.Return(vArr);
                        }
                    }

                    if (Vector128.IsHardwareAccelerated && jw < wEnd) {
                        Vector128<byte>[] vArr128 = ArrayPool<Vector128<byte>>.Shared.Rent(2 * size);
                        try {
                            for (; jw + 4 <= wEnd; jw += 4) {
                                for (int step = 0; step < nsteps; step++) {
                                    int startmin = (step + 1) * size - 1;
                                    vArr128[size - 1] = LoadVec128(ref baseS, startmin * wpls + jw);
                                    for (int k = 1; k < size; k++) {
                                        vArr128[size - 1 - k] = Vector128.Min(vArr128[size - k], LoadVec128(ref baseS, (startmin - k) * wpls + jw));
                                        vArr128[size - 1 + k] = Vector128.Min(vArr128[size + k - 2], LoadVec128(ref baseS, (startmin + k) * wpls + jw));
                                    }
                                    int starty = hsize + step * size;
                                    StoreVec128(ref baseD, starty * wpld + jw, vArr128[0]);
                                    StoreVec128(ref baseD, (starty + size - 1) * wpld + jw, vArr128[2 * size - 2]);
                                    for (int k = 1; k < size - 1; k++)
                                        StoreVec128(ref baseD, (starty + k) * wpld + jw, Vector128.Min(vArr128[k], vArr128[k + size - 1]));
                                }
                            }
                            for (; jw < wEnd; jw++) {
                                for (int step = 0; step < nsteps; step++) {
                                    int startmin = (step + 1) * size - 1;
                                    vArr128[size - 1] = Vector128.CreateScalar(Unsafe.Add(ref baseS, (nuint)(startmin * wpls + jw))).AsByte();
                                    for (int k = 1; k < size; k++) {
                                        Vector128<byte> vUp = Vector128.CreateScalar(Unsafe.Add(ref baseS, (nuint)((startmin - k) * wpls + jw))).AsByte();
                                        Vector128<byte> vDn = Vector128.CreateScalar(Unsafe.Add(ref baseS, (nuint)((startmin + k) * wpls + jw))).AsByte();
                                        vArr128[size - 1 - k] = Vector128.Min(vArr128[size - k], vUp);
                                        vArr128[size - 1 + k] = Vector128.Min(vArr128[size + k - 2], vDn);
                                    }
                                    int starty = hsize + step * size;
                                    Unsafe.Add(ref baseD, (nuint)(starty * wpld + jw)) = vArr128[0].AsUInt32().ToScalar();
                                    Unsafe.Add(ref baseD, (nuint)((starty + size - 1) * wpld + jw)) = vArr128[2 * size - 2].AsUInt32().ToScalar();
                                    for (int k = 1; k < size - 1; k++)
                                        Unsafe.Add(ref baseD, (nuint)((starty + k) * wpld + jw)) = Vector128.Min(vArr128[k], vArr128[k + size - 1]).AsUInt32().ToScalar();
                                }
                            }
                        } finally {
                            ArrayPool<Vector128<byte>>.Shared.Return(vArr128);
                        }
                    } else if (jw < wEnd) {
                        byte[] colBuf = ArrayPool<byte>.Shared.Rent(h + 2 * size);
                        try {
                            Span<byte> bufSpan = colBuf.AsSpan(0, h);
                            Span<byte> minSpan = colBuf.AsSpan(h, 2 * size);
                            for (int j = jw << 2; j < wEnd << 2; j++)
                                ErodeVertColumnScalar(ref baseS, wpls, ref baseD, wpld, h, size, hsize, nsteps, j, bufSpan, minSpan);
                        } finally {
                            ArrayPool<byte>.Shared.Return(colBuf);
                        }
                    }
                });
            }

            if ((fullWords << 2) < w) {
                ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
                for (int j = fullWords << 2; j < w; j++)
                    ErodeVertColumnScalar(ref baseS, wpls, ref baseD, wpld, h, size, hsize, nsteps, j, buffer.AsSpan(0, h), minarray.AsSpan(0, 2 * size));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<byte> LoadVec256(ref uint baseRef, int wordOffset)
        => Vector256.LoadUnsafe(ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref baseRef, (nuint)wordOffset)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreVec256(ref uint baseRef, int wordOffset, Vector256<byte> vec)
        => vec.StoreUnsafe(ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref baseRef, (nuint)wordOffset)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> LoadVec128(ref uint baseRef, int wordOffset)
        => Vector128.LoadUnsafe(ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref baseRef, (nuint)wordOffset)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreVec128(ref uint baseRef, int wordOffset, Vector128<byte> vec)
        => vec.StoreUnsafe(ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref baseRef, (nuint)wordOffset)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UnpackMorphRow(ref uint srcRow, int width, ref byte dstRow)
    {
        int j = 0;
        if (Vector256.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
            ref byte bs = ref Unsafe.As<uint, byte>(ref srcRow);
            for (; j + 32 <= width; j += 32)
                SimdEndian.SwapBytesInWords(Vector256.LoadUnsafe(ref bs, (nuint)j)).StoreUnsafe(ref dstRow, (nuint)j);
        }
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
            ref byte bs = ref Unsafe.As<uint, byte>(ref srcRow);
            for (; j + 16 <= width; j += 16)
                SimdEndian.SwapBytesInWords(Vector128.LoadUnsafe(ref bs, (nuint)j)).StoreUnsafe(ref dstRow, (nuint)j);
        }
        for (; j < width; j++)
            Unsafe.Add(ref dstRow, (nuint)j) = (byte)ArrayAccess.GetDataByteUnchecked(ref srcRow, j);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PackMorphRowSlice(ref byte srcRow, ref uint dstRow, int startCol, int endCol)
    {
        int j = startCol;
        for (; j < endCol && (j & 3) != 0; j++)
            ArrayAccess.SetDataByteUnchecked(ref dstRow, j, Unsafe.Add(ref srcRow, (nuint)j));

        if (Vector256.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
            ref byte bd = ref Unsafe.As<uint, byte>(ref dstRow);
            for (; j + 32 <= endCol; j += 32)
                SimdEndian.SwapBytesInWords(Vector256.LoadUnsafe(ref srcRow, (nuint)j)).StoreUnsafe(ref bd, (nuint)j);
        }
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
            ref byte bd = ref Unsafe.As<uint, byte>(ref dstRow);
            for (; j + 16 <= endCol; j += 16)
                SimdEndian.SwapBytesInWords(Vector128.LoadUnsafe(ref srcRow, (nuint)j)).StoreUnsafe(ref bd, (nuint)j);
        }
        for (; j + 4 <= endCol; j += 4) {
            uint b0 = Unsafe.Add(ref srcRow, (nuint)j);
            uint b1 = Unsafe.Add(ref srcRow, (nuint)(j + 1));
            uint b2 = Unsafe.Add(ref srcRow, (nuint)(j + 2));
            uint b3 = Unsafe.Add(ref srcRow, (nuint)(j + 3));
            Unsafe.Add(ref dstRow, (nuint)(j >> 2)) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
        }
        for (; j < endCol; j++)
            ArrayAccess.SetDataByteUnchecked(ref dstRow, j, Unsafe.Add(ref srcRow, (nuint)j));
    }

    private static void DilateVertColumnScalar(ref uint baseS, int wpls, ref uint baseD, int wpld,
                                               int h, int size, int hsize, int nsteps, int j,
                                               Span<byte> buffer, Span<byte> maxarray)
    {
        for (int i = 0; i < h; i++)
            buffer[i] = (byte)ArrayAccess.GetDataByteUnchecked(ref Unsafe.Add(ref baseS, (nuint)(i * wpls)), j);

        for (int step = 0; step < nsteps; step++) {
            int startmax = (step + 1) * size - 1;
            maxarray[size - 1] = buffer[startmax];
            for (int k = 1; k < size; k++) {
                maxarray[size - 1 - k] = Math.Max(maxarray[size - k], buffer[startmax - k]);
                maxarray[size - 1 + k] = Math.Max(maxarray[size + k - 2], buffer[startmax + k]);
            }
            int starty = hsize + step * size;
            int lined = starty * wpld;
            ArrayAccess.SetDataByteUnchecked(ref Unsafe.Add(ref baseD, (nuint)lined), j, maxarray[0]);
            ArrayAccess.SetDataByteUnchecked(ref Unsafe.Add(ref baseD, (nuint)(lined + (size - 1) * wpld)), j, maxarray[2 * size - 2]);
            for (int k = 1; k < size - 1; k++)
                ArrayAccess.SetDataByteUnchecked(ref Unsafe.Add(ref baseD, (nuint)(lined + wpld * k)), j, Math.Max(maxarray[k], maxarray[k + size - 1]));
        }
    }

    private static void ErodeVertColumnScalar(ref uint baseS, int wpls, ref uint baseD, int wpld,
                                              int h, int size, int hsize, int nsteps, int j,
                                              Span<byte> buffer, Span<byte> minarray)
    {
        for (int i = 0; i < h; i++)
            buffer[i] = (byte)ArrayAccess.GetDataByteUnchecked(ref Unsafe.Add(ref baseS, (nuint)(i * wpls)), j);

        for (int step = 0; step < nsteps; step++) {
            int startmin = (step + 1) * size - 1;
            minarray[size - 1] = buffer[startmin];
            for (int k = 1; k < size; k++) {
                minarray[size - 1 - k] = Math.Min(minarray[size - k], buffer[startmin - k]);
                minarray[size - 1 + k] = Math.Min(minarray[size + k - 2], buffer[startmin + k]);
            }
            int starty = hsize + step * size;
            int lined = starty * wpld;
            ArrayAccess.SetDataByteUnchecked(ref Unsafe.Add(ref baseD, (nuint)lined), j, minarray[0]);
            ArrayAccess.SetDataByteUnchecked(ref Unsafe.Add(ref baseD, (nuint)(lined + (size - 1) * wpld)), j, minarray[2 * size - 2]);
            for (int k = 1; k < size - 1; k++)
                ArrayAccess.SetDataByteUnchecked(ref Unsafe.Add(ref baseD, (nuint)(lined + wpld * k)), j, Math.Min(minarray[k], minarray[k + size - 1]));
        }
    }
}
