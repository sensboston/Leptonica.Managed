using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Leptonica;

/*
 *  seedfill.c (low-level part): the raster-order iterations.
 *
 *  In Leptonica 1.87.0 there is no separate seedfilllow.c; these are the
 *  static functions of seedfill.c.  They work on the data arrays directly:
 *
 *      seedfillBinaryLow()
 *      seedfillGrayLow()
 *      seedfillGrayInvLow()
 *      seedfillGrayLowSimple()
 *      seedfillGrayInvLowSimple()
 *      distanceFunctionLow()
 *      seedspreadLow()
 *
 *  A C pointer to a line (`lines`, `linem`, `lined`, `linet`) is represented
 *  by the word offset of that line in the data array; `lines - wpls` is the
 *  offset `ls - wpls`.  The byte and 16-bit accessors take the array and the
 *  line offset, with the pixel index as in the C macros.
 */

/// <summary>Pixel coordinates stored in the fifo queue (struct L_Pixel of seedfill.c).</summary>
internal sealed class L_Pixel
{
    public int x;
    public int y;
}

public sealed partial class Pix
{
    /* Two-way (UL --> LR, LR --> UL) sweep iterations; typically need only 4 */
    private const int MaxIters = 40;

    /*-----------------------------------------------------------------------*
     *                  GET/SET_DATA_* on a line given by offset             *
     *-----------------------------------------------------------------------*/
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetByte(uint[] data, int line, int n)
    {
        return ArrayAccess.GetDataByte(data.AsSpan(line), n);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetByte(uint[] data, int line, int n, int val)
    {
        ArrayAccess.SetDataByte(data.AsSpan(line), n, val);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetByteUnchecked(uint[] data, int line, int n)
    {
        ref uint baseRef = ref MemoryMarshal.GetArrayDataReference(data);
        ref uint lineRef = ref Unsafe.Add(ref baseRef, line);
        return ArrayAccess.GetDataByteUnchecked(ref lineRef, n);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetByteUnchecked(uint[] data, int line, int n, int val)
    {
        ref uint baseRef = ref MemoryMarshal.GetArrayDataReference(data);
        ref uint lineRef = ref Unsafe.Add(ref baseRef, line);
        ArrayAccess.SetDataByteUnchecked(ref lineRef, n, val);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetTwoBytes(uint[] data, int line, int n)
    {
        return ArrayAccess.GetDataTwoBytes(data.AsSpan(line), n);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetTwoBytes(uint[] data, int line, int n, int val)
    {
        ArrayAccess.SetDataTwoBytes(data.AsSpan(line), n, val);
    }

    // leptonica: seedfillBinaryLow (seedfill.c)
    /// <summary>One full cycle of the binary seedfill, UL -> LR and LR -> UL.</summary>
    /// <remarks>
    /// (1) This is an in-place fill, where the seed image is
    ///     filled, clipping to the filling mask, in one full
    ///     cycle of UL -> LR and LR -> UL raster scans.
    /// (2) Assume the mask is a filling mask, not a blocking mask.
    /// (3) Assume that the RHS pad bits of the mask
    ///     are properly set to 0.
    /// (4) Clip to the smallest dimensions to avoid invalid reads.
    /// </remarks>
    internal static void SeedfillBinaryLow(uint[] datas, int hs, int wpls,
                                           uint[] datam, int hm, int wplm,
                                           int connectivity)
    {
        int i, j, h, wpl;
        uint word, mask;
        uint wordabove, wordleft, wordbelow, wordright;
        uint wordprev;  /* test against this in previous iteration */
        int lines, linem;

        h = Math.Min(hs, hm);
        wpl = Math.Min(wpls, wplm);

        switch (connectivity) {
        case 4:
            /* UL --> LR scan */
            for (i = 0; i < h; i++) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = 0; j < wpl; j++) {
                    word = datas[lines + j];
                    mask = datam[linem + j];

                    /* OR from word above and from word to left; mask */
                    if (i > 0) {
                        wordabove = datas[lines - wpls + j];
                        word |= wordabove;
                    }
                    if (j > 0) {
                        wordleft = datas[lines + j - 1];
                        word |= wordleft << 31;
                    }
                    word &= mask;

                    /* No need to fill horizontally? */
                    if (word == 0 || ~word == 0) {
                        datas[lines + j] = word;
                        continue;
                    }

                    while (true) {
                        wordprev = word;
                        word = (word | (word >> 1) | (word << 1)) & mask;
                        if ((word ^ wordprev) == 0) {
                            datas[lines + j] = word;
                            break;
                        }
                    }
                }
            }

            /* LR --> UL scan */
            for (i = h - 1; i >= 0; i--) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = wpl - 1; j >= 0; j--) {
                    word = datas[lines + j];
                    mask = datam[linem + j];

                    /* OR from word below and from word to right; mask */
                    if (i < h - 1) {
                        wordbelow = datas[lines + wpls + j];
                        word |= wordbelow;
                    }
                    if (j < wpl - 1) {
                        wordright = datas[lines + j + 1];
                        word |= wordright >> 31;
                    }
                    word &= mask;

                    /* No need to fill horizontally? */
                    if (word == 0 || ~word == 0) {
                        datas[lines + j] = word;
                        continue;
                    }

                    while (true) {
                        wordprev = word;
                        word = (word | (word >> 1) | (word << 1)) & mask;
                        if ((word ^ wordprev) == 0) {
                            datas[lines + j] = word;
                            break;
                        }
                    }
                }
            }
            break;

        case 8:
            /* UL --> LR scan */
            for (i = 0; i < h; i++) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = 0; j < wpl; j++) {
                    word = datas[lines + j];
                    mask = datam[linem + j];

                    /* OR from words above and from word to left; mask */
                    if (i > 0) {
                        wordabove = datas[lines - wpls + j];
                        word |= (wordabove | (wordabove << 1) | (wordabove >> 1));
                        if (j > 0)
                            word |= datas[lines - wpls + j - 1] << 31;
                        if (j < wpl - 1)
                            word |= datas[lines - wpls + j + 1] >> 31;
                    }
                    if (j > 0) {
                        wordleft = datas[lines + j - 1];
                        word |= wordleft << 31;
                    }
                    word &= mask;

                    /* No need to fill horizontally? */
                    if (word == 0 || ~word == 0) {
                        datas[lines + j] = word;
                        continue;
                    }

                    while (true) {
                        wordprev = word;
                        word = (word | (word >> 1) | (word << 1)) & mask;
                        if ((word ^ wordprev) == 0) {
                            datas[lines + j] = word;
                            break;
                        }
                    }
                }
            }

            /* LR --> UL scan */
            for (i = h - 1; i >= 0; i--) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = wpl - 1; j >= 0; j--) {
                    word = datas[lines + j];
                    mask = datam[linem + j];

                    /* OR from words below and from word to right; mask */
                    if (i < h - 1) {
                        wordbelow = datas[lines + wpls + j];
                        word |= (wordbelow | (wordbelow << 1) | (wordbelow >> 1));
                        if (j > 0)
                            word |= datas[lines + wpls + j - 1] << 31;
                        if (j < wpl - 1)
                            word |= datas[lines + wpls + j + 1] >> 31;
                    }
                    if (j < wpl - 1) {
                        wordright = datas[lines + j + 1];
                        word |= wordright >> 31;
                    }
                    word &= mask;

                    /* No need to fill horizontally? */
                    if (word == 0 || ~word == 0) {
                        datas[lines + j] = word;
                        continue;
                    }

                    while (true) {
                        wordprev = word;
                        word = (word | (word >> 1) | (word << 1)) & mask;
                        if ((word ^ wordprev) == 0) {
                            datas[lines + j] = word;
                            break;
                        }
                    }
                }
            }
            break;

        default:
            Log.Error("seedfillBinaryLow", "connectivity must be 4 or 8\n");
            break;
        }
    }

    // leptonica: seedfillGrayLow (seedfill.c)
    /// <summary>Hybrid grayscale seedfill: raster, anti-raster, then fifo propagation.</summary>
    /// <remarks>
    /// (1) The pixels are numbered as follows:
    ///         1  2  3
    ///         4  x  5
    ///         6  7  8
    ///     This low-level filling operation consists of two scans,
    ///     raster and anti-raster, covering the entire seed image.
    ///     This is followed by a breadth-first propagation operation to
    ///     complete the fill.
    ///     During the anti-raster scan, every pixel p whose current value
    ///     could still be propagated after the anti-raster scan is put into
    ///     the FIFO queue.
    ///     The propagation step is a breadth-first fill to completion.
    ///     Unlike the simple grayscale seedfill pixSeedfillGraySimple(),
    ///     where at least two full raster/anti-raster iterations are required
    ///     for completion and verification, the hybrid method uses only a
    ///     single raster/anti-raster set of scans.
    /// (2) The filling action can be visualized from the following example.
    ///     Suppose the mask, which clips the fill, is a sombrero-shaped
    ///     surface, where the highest point is 200 and the low pixels
    ///     around the rim are 30.  Beyond the rim, the mask goes up a bit.
    ///     Suppose the seed, which is filled, consists of a single point
    ///     of height 150, located below the max of the mask, with
    ///     the rest 0.  Then in the raster scan, nothing happens until
    ///     the high seed point is encountered, and then this value is
    ///     propagated right and down, until it hits the side of the
    ///     sombrero.   The seed can never exceed the mask, so it fills
    ///     to the rim, going lower along the mask surface.  When it
    ///     passes the rim, the seed continues to fill at the rim
    ///     height to the edge of the seed image.  Then on the
    ///     anti-raster scan, the seed fills flat inside the
    ///     sombrero to the upper and left, and then out from the
    ///     rim as before.  The final result has a seed that is
    ///     flat outside the rim, and inside it fills the sombrero
    ///     but only up to 150.  If the rim height varies, the
    ///     filled seed outside the rim will be at the highest
    ///     point on the rim, which is a saddle point on the rim.
    /// (3) Reference paper :
    ///       L. Vincent, Morphological grayscale reconstruction in image
    ///       analysis: applications and efficient algorithms, IEEE Transactions
    ///       on  Image Processing, vol. 2, no. 2, pp. 176-201, 1993.
    /// </remarks>
    /// <summary>Allocation-light FIFO of grayscale seedfill coordinates.</summary>
    private sealed class SeedfillGrayQueue
    {
        private long[] _items;
        private int _head;
        private int _count;

        public SeedfillGrayQueue(int initialCapacity)
        {
            int capacity = 16;
            while (capacity < initialCapacity)
                capacity <<= 1;
            _items = new long[capacity];
        }

        public int Count => _count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int row, int column)
        {
            if (_count == _items.Length)
                Grow();

            int index = _head + _count;
            if (index >= _items.Length)
                index -= _items.Length;
            _items[index] = ((long)(uint)row << 32) | (uint)column;
            _count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(out int row, out int column)
        {
            long packed = _items[_head];
            if (++_head == _items.Length)
                _head = 0;
            _count--;
            row = (int)(packed >> 32);
            column = (int)packed;
        }

        private void Grow()
        {
            long[] expanded = new long[_items.Length * 2];
            int first = Math.Min(_count, _items.Length - _head);
            Array.Copy(_items, _head, expanded, 0, first);
            Array.Copy(_items, 0, expanded, first, _count - first);
            _items = expanded;
            _head = 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsZero16(ref byte row, int j)
    {
        var v = System.Runtime.Intrinsics.Vector128.LoadUnsafe(ref row, (nuint)j);
        return v == System.Runtime.Intrinsics.Vector128<byte>.Zero;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int RasterStep4(ref byte sr, ref byte mr, ref byte su, bool hasUp, int j, int prev)
    {
        int x = BitConverter.IsLittleEndian ? 3 : 0;
        int m = Unsafe.Add(ref mr, j ^ x);
        int v = Unsafe.Add(ref sr, j ^ x);
        if (m > 0) {
            int mx = Math.Max(prev, v);
            if (hasUp)
                mx = Math.Max(mx, Unsafe.Add(ref su, j ^ x));
            v = Math.Min(mx, m);
            Unsafe.Add(ref sr, j ^ x) = (byte)v;
        }
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AntiRasterStep4(ref byte sr, ref byte mr, ref byte sd, ref byte md,
                                       bool hasDown, int j, int jmax, int prev, int i,
                                       SeedfillGrayQueue q)
    {
        int x = BitConverter.IsLittleEndian ? 3 : 0;
        int m = Unsafe.Add(ref mr, j ^ x);
        int v = Unsafe.Add(ref sr, j ^ x);
        if (m > 0) {
            int mx = Math.Max(prev, v);
            int d = 0;
            if (hasDown) {
                d = Unsafe.Add(ref sd, j ^ x);
                mx = Math.Max(mx, d);
            }
            int val = Math.Min(mx, m);
            Unsafe.Add(ref sr, j ^ x) = (byte)val;
            bool push = false;
            if (hasDown && d < val && d < Unsafe.Add(ref md, j ^ x))
                push = true;
            if (!push && j < jmax && prev < val && prev < Unsafe.Add(ref mr, (j + 1) ^ x))
                push = true;
            if (push)
                q.Add(i, j);
            return val;
        }
        return v;
    }


    // Raster and anti-raster scans for 4-connectivity.  Works on a byte view of
    // the word data (pixel j is byte j ^ 3 on little-endian hosts) and skips
    // runs of 16 pixels where the seed, the already-visited neighbor row and the
    // carried value are all zero: such pixels cannot change or be queued.
    private static void SeedfillGrayScan4(uint[] datas, int w, int h, int wpls,
                                          uint[] datam, int wplm, SeedfillGrayQueue lq)
    {
        ref byte s0 = ref Unsafe.As<uint, byte>(ref MemoryMarshal.GetArrayDataReference(datas));
        ref byte m0 = ref Unsafe.As<uint, byte>(ref MemoryMarshal.GetArrayDataReference(datam));
        int strideS = wpls * 4, strideM = wplm * 4;
        int nb = w >> 4;
        int jmax = w - 1;
        var qr = lq;

        for (int i = 0; i < h; i++) {
            ref byte sr = ref Unsafe.Add(ref s0, i * strideS);
            ref byte mr = ref Unsafe.Add(ref m0, i * strideM);
            bool hasUp = i > 0;
            ref byte su = ref Unsafe.Add(ref s0, (hasUp ? i - 1 : i) * strideS);
            int prev = 0;
            for (int b = 0; b < nb; b++) {
                int jb = b << 4;
                if (prev == 0 && IsZero16(ref sr, jb) && (!hasUp || IsZero16(ref su, jb)))
                    continue;
                for (int j = jb; j < jb + 16; j++)
                    prev = RasterStep4(ref sr, ref mr, ref su, hasUp, j, prev);
            }
            for (int j = nb << 4; j < w; j++)
                prev = RasterStep4(ref sr, ref mr, ref su, hasUp, j, prev);
        }

        for (int i = h - 1; i >= 0; i--) {
            ref byte sr = ref Unsafe.Add(ref s0, i * strideS);
            ref byte mr = ref Unsafe.Add(ref m0, i * strideM);
            bool hasDown = i < h - 1;
            ref byte sd = ref Unsafe.Add(ref s0, (hasDown ? i + 1 : i) * strideS);
            ref byte md = ref Unsafe.Add(ref m0, (hasDown ? i + 1 : i) * strideM);
            int prev = 0;
            for (int j = jmax; j >= nb << 4; j--)
                prev = AntiRasterStep4(ref sr, ref mr, ref sd, ref md, hasDown, j, jmax, prev, i, qr);
            for (int b = nb - 1; b >= 0; b--) {
                int jb = b << 4;
                if (prev == 0 && IsZero16(ref sr, jb) && (!hasDown || IsZero16(ref sd, jb)))
                    continue;
                for (int j = jb + 15; j >= jb; j--)
                    prev = AntiRasterStep4(ref sr, ref mr, ref sd, ref md, hasDown, j, jmax, prev, i, qr);
            }
        }
    }

    internal static void SeedfillGrayLow(uint[] datas, int w, int h, int wpls,
                                         uint[] datam, int wplm, int connectivity)
    {
        int val1, val2, val3, val4, val5, val6, val7, val8;
        int val, maxval, maskval;
        bool boolval;
        int i, j, imax, jmax, queue_size;
        int lines, linem;
        SeedfillGrayQueue lq_pixel;

        if (connectivity != 4 && connectivity != 8) {
            Log.Error("seedfillGrayLow", "connectivity must be 4 or 8\n");
            return;
        }

        imax = h - 1;
        jmax = w - 1;

        /* In the worst case, most of the pixels could be pushed
         * onto the FIFO queue during anti-raster scan.  However this
         * will rarely happen, and we initialize the queue ptr size to
         * the image perimeter. */
        lq_pixel = new SeedfillGrayQueue(2 * (w + h));

        switch (connectivity) {
        case 4:
            SeedfillGrayScan4(datas, w, h, wpls, datam, wplm, lq_pixel);


            /* Propagation step:
             *        while fifo_empty = false
             *          p <- fifo_first()
             *          for every pixel (q) belong to neighbors of (p)
             *            if J(q) < J(p) and I(q) != J(q)
             *              J(q) <- min(J(p), I(q));
             *              fifo_add(q);
             *            end
             *          end
             *        end */
            queue_size = lq_pixel.Count;
            while (queue_size != 0) {
                lq_pixel.Remove(out i, out j);
                lines = i * wpls;
                linem = i * wplm;

                if ((val = GetByteUnchecked(datas, lines, j)) > 0) {
                    if (i > 0) {
                        val2 = GetByteUnchecked(datas, lines - wpls, j);
                        maskval = GetByteUnchecked(datam, linem - wplm, j);
                        if (val > val2 && val2 != maskval) {
                            SetByteUnchecked(datas, lines - wpls, j, Math.Min(val, maskval));
                            lq_pixel.Add(i - 1, j);
                        }

                    }
                    if (j > 0) {
                        val4 = GetByteUnchecked(datas, lines, j - 1);
                        maskval = GetByteUnchecked(datam, linem, j - 1);
                        if (val > val4 && val4 != maskval) {
                            SetByteUnchecked(datas, lines, j - 1, Math.Min(val, maskval));
                            lq_pixel.Add(i, j - 1);
                        }
                    }
                    if (i < imax) {
                        val7 = GetByteUnchecked(datas, lines + wpls, j);
                        maskval = GetByteUnchecked(datam, linem + wplm, j);
                        if (val > val7 && val7 != maskval) {
                            SetByteUnchecked(datas, lines + wpls, j, Math.Min(val, maskval));
                            lq_pixel.Add(i + 1, j);
                        }
                    }
                    if (j < jmax) {
                        val5 = GetByteUnchecked(datas, lines, j + 1);
                        maskval = GetByteUnchecked(datam, linem, j + 1);
                        if (val > val5 && val5 != maskval) {
                            SetByteUnchecked(datas, lines, j + 1, Math.Min(val, maskval));
                            lq_pixel.Add(i, j + 1);
                        }
                    }
                }

                queue_size = lq_pixel.Count;
            }
            break;

        case 8:
            /* UL --> LR scan  (Raster Order)
             * If I : mask image
             *    J : marker image
             * Let p be the currect pixel;
             * J(p) <- (max{J(p) union J(p) neighbors in raster order})
             *          intersection I(p) */
            for (i = 0; i < h; i++) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = 0; j < w; j++) {
                    if ((maskval = GetByte(datam, linem, j)) > 0) {
                        maxval = 0;
                        if (i > 0) {
                            if (j > 0)
                                maxval = GetByte(datas, lines - wpls, j - 1);
                            if (j < jmax) {
                                val3 = GetByte(datas, lines - wpls, j + 1);
                                maxval = Math.Max(maxval, val3);
                            }
                            val2 = GetByte(datas, lines - wpls, j);
                            maxval = Math.Max(maxval, val2);
                        }
                        if (j > 0) {
                            val4 = GetByte(datas, lines, j - 1);
                            maxval = Math.Max(maxval, val4);
                        }
                        val = GetByte(datas, lines, j);
                        maxval = Math.Max(maxval, val);
                        val = Math.Min(maxval, maskval);
                        SetByte(datas, lines, j, val);
                    }
                }
            }

            /* LR --> UL scan (anti-raster order)
             * Let p be the currect pixel;
             * J(p) <- (max{J(p) union J(p) neighbors in anti-raster order})
             *          intersection I(p) */
            for (i = imax; i >= 0; i--) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = jmax; j >= 0; j--) {
                    boolval = false;
                    if ((maskval = GetByte(datam, linem, j)) > 0) {
                        maxval = 0;
                        if (i < imax) {
                            if (j > 0) {
                                maxval = GetByte(datas, lines + wpls, j - 1);
                            }
                            if (j < jmax) {
                                val8 = GetByte(datas, lines + wpls, j + 1);
                                maxval = Math.Max(maxval, val8);
                            }
                            val7 = GetByte(datas, lines + wpls, j);
                            maxval = Math.Max(maxval, val7);
                        }
                        if (j < jmax) {
                            val5 = GetByte(datas, lines, j + 1);
                            maxval = Math.Max(maxval, val5);
                        }
                        val = GetByte(datas, lines, j);
                        maxval = Math.Max(maxval, val);
                        val = Math.Min(maxval, maskval);
                        SetByte(datas, lines, j, val);

                        /* If there exists a point (q) which belongs to J(p)
                         * neighbors in anti-raster order such that J(q) < J(p)
                         * and J(q) < I(q) then
                         * fifo_add(p) */
                        if (i < imax) {
                            if (j > 0) {
                                val6 = GetByte(datas, lines + wpls, j - 1);
                                if ((val6 < val) &&
                                    (val6 < GetByte(datam, linem + wplm, j - 1))) {
                                    boolval = true;
                                }
                            }
                            if (j < jmax) {
                                val8 = GetByte(datas, lines + wpls, j + 1);
                                if (!boolval && (val8 < val) &&
                                    (val8 < GetByte(datam, linem + wplm, j + 1))) {
                                    boolval = true;
                                }
                            }
                            val7 = GetByte(datas, lines + wpls, j);
                            if (!boolval && (val7 < val) &&
                                (val7 < GetByte(datam, linem + wplm, j))) {
                                boolval = true;
                            }
                        }
                        if (j < jmax) {
                            val5 = GetByte(datas, lines, j + 1);
                            if (!boolval && (val5 < val) &&
                                (val5 < GetByte(datam, linem, j + 1))) {
                                boolval = true;
                            }
                        }
                        if (boolval) {
                            lq_pixel.Add(i, j);
                        }
                    }
                }
            }

            /* Propagation step:
             *        while fifo_empty = false
             *          p <- fifo_first()
             *          for every pixel (q) belong to neighbors of (p)
             *            if J(q) < J(p) and I(q) != J(q)
             *              J(q) <- min(J(p), I(q));
             *              fifo_add(q);
             *            end
             *          end
             *        end */
            queue_size = lq_pixel.Count;
            while (queue_size != 0) {
                lq_pixel.Remove(out i, out j);
                lines = i * wpls;
                linem = i * wplm;

                if ((val = GetByte(datas, lines, j)) > 0) {
                    if (i > 0) {
                        if (j > 0) {
                            val1 = GetByte(datas, lines - wpls, j - 1);
                            maskval = GetByte(datam, linem - wplm, j - 1);
                            if (val > val1 && val1 != maskval) {
                                SetByte(datas, lines - wpls, j - 1,
                                        Math.Min(val, maskval));
                                lq_pixel.Add(i - 1, j - 1);
                            }
                        }
                        if (j < jmax) {
                            val3 = GetByte(datas, lines - wpls, j + 1);
                            maskval = GetByte(datam, linem - wplm, j + 1);
                            if (val > val3 && val3 != maskval) {
                                SetByte(datas, lines - wpls, j + 1,
                                        Math.Min(val, maskval));
                                lq_pixel.Add(i - 1, j + 1);
                            }
                        }
                        val2 = GetByte(datas, lines - wpls, j);
                        maskval = GetByte(datam, linem - wplm, j);
                        if (val > val2 && val2 != maskval) {
                            SetByte(datas, lines - wpls, j, Math.Min(val, maskval));
                            lq_pixel.Add(i - 1, j);
                        }

                    }
                    if (j > 0) {
                        val4 = GetByte(datas, lines, j - 1);
                        maskval = GetByte(datam, linem, j - 1);
                        if (val > val4 && val4 != maskval) {
                            SetByte(datas, lines, j - 1, Math.Min(val, maskval));
                            lq_pixel.Add(i, j - 1);
                        }
                    }
                    if (i < imax) {
                        if (j > 0) {
                            val6 = GetByte(datas, lines + wpls, j - 1);
                            maskval = GetByte(datam, linem + wplm, j - 1);
                            if (val > val6 && val6 != maskval) {
                                SetByte(datas, lines + wpls, j - 1,
                                        Math.Min(val, maskval));
                                lq_pixel.Add(i + 1, j - 1);
                            }
                        }
                        if (j < jmax) {
                            val8 = GetByte(datas, lines + wpls, j + 1);
                            maskval = GetByte(datam, linem + wplm, j + 1);
                            if (val > val8 && val8 != maskval) {
                                SetByte(datas, lines + wpls, j + 1,
                                        Math.Min(val, maskval));
                                lq_pixel.Add(i + 1, j + 1);
                            }
                        }
                        val7 = GetByte(datas, lines + wpls, j);
                        maskval = GetByte(datam, linem + wplm, j);
                        if (val > val7 && val7 != maskval) {
                            SetByte(datas, lines + wpls, j, Math.Min(val, maskval));
                            lq_pixel.Add(i + 1, j);
                        }
                    }
                    if (j < jmax) {
                        val5 = GetByte(datas, lines, j + 1);
                        maskval = GetByte(datam, linem, j + 1);
                        if (val > val5 && val5 != maskval) {
                            SetByte(datas, lines, j + 1, Math.Min(val, maskval));
                            lq_pixel.Add(i, j + 1);
                        }
                    }
                }

                queue_size = lq_pixel.Count;
            }
            break;

        default:
            Log.Error("seedfillGrayLow", "shouldn't get here!\n");
            break;
        }
    }

    // leptonica: seedfillGrayInvLow (seedfill.c)
    /// <summary>Hybrid grayscale seedfill where the mask stops the fill from below.</summary>
    /// <remarks>
    /// (1) The pixels are numbered as follows:
    ///         1  2  3
    ///         4  x  5
    ///         6  7  8
    ///     This low-level filling operation consists of two scans,
    ///     raster and anti-raster, covering the entire seed image.
    ///     During the anti-raster scan, every pixel p such that its
    ///     current value could still be propagated during the next
    ///     raster scanning is put into the FIFO-queue.
    ///     Next step is the propagation step where where we update
    ///     and propagate the values using FIFO structure created in
    ///     anti-raster scan.
    /// (2) The "Inv" signifies the fact that in this case, filling
    ///     of the seed only takes place when the seed value is
    ///     greater than the mask value.  The mask will act to stop
    ///     the fill when it is higher than the seed level.  (This is
    ///     in contrast to conventional grayscale filling where the
    ///     seed always fills below the mask.)
    /// (3) An example of use is a basin, described by the mask (pixm),
    ///     where within the basin, the seed pix (pixs) gets filled to the
    ///     height of the highest seed pixel that is above its
    ///     corresponding max pixel.  Filling occurs while the
    ///     propagating seed pixels in pixs are larger than the
    ///     corresponding mask values in pixm.
    /// (4) Reference paper :
    ///       L. Vincent, Morphological grayscale reconstruction in image
    ///       analysis: applications and efficient algorithms, IEEE Transactions
    ///       on  Image Processing, vol. 2, no. 2, pp. 176-201, 1993.
    /// </remarks>
    internal static void SeedfillGrayInvLow(uint[] datas, int w, int h, int wpls,
                                            uint[] datam, int wplm, int connectivity)
    {
        int val1, val2, val3, val4, val5, val6, val7, val8;
        int val, maxval, maskval;
        bool boolval;
        int i, j, imax, jmax, queue_size;
        int lines, linem;
        L_Pixel? pixel;
        L_Queue<L_Pixel> lq_pixel;

        if (connectivity != 4 && connectivity != 8) {
            Log.Error("seedfillGrayInvLow", "connectivity must be 4 or 8\n");
            return;
        }

        imax = h - 1;
        jmax = w - 1;

        /* In the worst case, most of the pixels could be pushed
         * onto the FIFO queue during anti-raster scan.  However this
         * will rarely happen, and we initialize the queue ptr size to
         * the image perimeter. */
        lq_pixel = L_Queue<L_Pixel>.Create(2 * (w + h));

        switch (connectivity) {
        case 4:
            /* UL --> LR scan  (Raster Order)
             * If I : mask image
             *    J : marker image
             * Let p be the currect pixel;
             * tmp <- max{J(p) union J(p) neighbors in raster order}
             * if (tmp > I(p))
             *   J(p) <- tmp
             * end */
            for (i = 0; i < h; i++) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = 0; j < w; j++) {
                    if ((maskval = GetByte(datam, linem, j)) < 255) {
                        maxval = GetByte(datas, lines, j);
                        if (i > 0) {
                            val2 = GetByte(datas, lines - wpls, j);
                            maxval = Math.Max(maxval, val2);
                        }
                        if (j > 0) {
                            val4 = GetByte(datas, lines, j - 1);
                            maxval = Math.Max(maxval, val4);
                        }
                        if (maxval > maskval)
                            SetByte(datas, lines, j, maxval);
                    }
                }
            }

            /* LR --> UL scan (anti-raster order)
             * If I : mask image
             *    J : marker image
             * Let p be the currect pixel;
             * tmp <- max{J(p) union J(p) neighbors in anti-raster order}
             * if (tmp > I(p))
             *   J(p) <- tmp
             * end */
            for (i = imax; i >= 0; i--) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = jmax; j >= 0; j--) {
                    boolval = false;
                    if ((maskval = GetByte(datam, linem, j)) < 255) {
                        val = maxval = GetByte(datas, lines, j);
                        if (i < imax) {
                            val7 = GetByte(datas, lines + wpls, j);
                            maxval = Math.Max(maxval, val7);
                        }
                        if (j < jmax) {
                            val5 = GetByte(datas, lines, j + 1);
                            maxval = Math.Max(maxval, val5);
                        }
                        if (maxval > maskval)
                            SetByte(datas, lines, j, maxval);
                        val = GetByte(datas, lines, j);

                        /*
                         * If there exists a point (q) which belongs to J(p)
                         * neighbors in anti-raster order such that J(q) < J(p)
                         * and J(p) > I(q) then
                         * fifo_add(p) */
                        if (i < imax) {
                            val7 = GetByte(datas, lines + wpls, j);
                            if ((val7 < val) &&
                                (val > GetByte(datam, linem + wplm, j))) {
                                boolval = true;
                            }
                        }
                        if (j < jmax) {
                            val5 = GetByte(datas, lines, j + 1);
                            if (!boolval && (val5 < val) &&
                                (val > GetByte(datam, linem, j + 1))) {
                                boolval = true;
                            }
                        }
                        if (boolval) {
                            pixel = new L_Pixel();
                            pixel.x = i;
                            pixel.y = j;
                            lq_pixel.Add(pixel);
                        }
                    }
                }
            }

            /* Propagation step:
             *        while fifo_empty = false
             *          p <- fifo_first()
             *          for every pixel (q) belong to neighbors of (p)
             *            if J(q) < J(p) and J(p) > I(q)
             *              J(q) <- min(J(p), I(q));
             *              fifo_add(q);
             *            end
             *          end
             *        end */
            queue_size = lq_pixel.Count;
            while (queue_size != 0) {
                pixel = lq_pixel.Remove()!;
                i = pixel.x;
                j = pixel.y;
                lines = i * wpls;
                linem = i * wplm;

                if ((val = GetByte(datas, lines, j)) > 0) {
                    if (i > 0) {
                        val2 = GetByte(datas, lines - wpls, j);
                        maskval = GetByte(datam, linem - wplm, j);
                        if (val > val2 && val > maskval) {
                            SetByte(datas, lines - wpls, j, val);
                            pixel = new L_Pixel();
                            pixel.x = i - 1;
                            pixel.y = j;
                            lq_pixel.Add(pixel);
                        }

                    }
                    if (j > 0) {
                        val4 = GetByte(datas, lines, j - 1);
                        maskval = GetByte(datam, linem, j - 1);
                        if (val > val4 && val > maskval) {
                            SetByte(datas, lines, j - 1, val);
                            pixel = new L_Pixel();
                            pixel.x = i;
                            pixel.y = j - 1;
                            lq_pixel.Add(pixel);
                        }
                    }
                    if (i < imax) {
                        val7 = GetByte(datas, lines + wpls, j);
                        maskval = GetByte(datam, linem + wplm, j);
                        if (val > val7 && val > maskval) {
                            SetByte(datas, lines + wpls, j, val);
                            pixel = new L_Pixel();
                            pixel.x = i + 1;
                            pixel.y = j;
                            lq_pixel.Add(pixel);
                        }
                    }
                    if (j < jmax) {
                        val5 = GetByte(datas, lines, j + 1);
                        maskval = GetByte(datam, linem, j + 1);
                        if (val > val5 && val > maskval) {
                            SetByte(datas, lines, j + 1, val);
                            pixel = new L_Pixel();
                            pixel.x = i;
                            pixel.y = j + 1;
                            lq_pixel.Add(pixel);
                        }
                    }
                }

                queue_size = lq_pixel.Count;
            }
            break;

        case 8:
            /* UL --> LR scan  (Raster Order)
             * If I : mask image
             *    J : marker image
             * Let p be the currect pixel;
             * tmp <- max{J(p) union J(p) neighbors in raster order}
             * if (tmp > I(p))
             *   J(p) <- tmp
             * end */
            for (i = 0; i < h; i++) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = 0; j < w; j++) {
                    if ((maskval = GetByte(datam, linem, j)) < 255) {
                        maxval = GetByte(datas, lines, j);
                        if (i > 0) {
                            if (j > 0) {
                                val1 = GetByte(datas, lines - wpls, j - 1);
                                maxval = Math.Max(maxval, val1);
                            }
                            if (j < jmax) {
                                val3 = GetByte(datas, lines - wpls, j + 1);
                                maxval = Math.Max(maxval, val3);
                            }
                            val2 = GetByte(datas, lines - wpls, j);
                            maxval = Math.Max(maxval, val2);
                        }
                        if (j > 0) {
                            val4 = GetByte(datas, lines, j - 1);
                            maxval = Math.Max(maxval, val4);
                        }
                        if (maxval > maskval)
                            SetByte(datas, lines, j, maxval);
                    }
                }
            }

            /* LR --> UL scan (anti-raster order)
             * If I : mask image
             *    J : marker image
             * Let p be the currect pixel;
             * tmp <- max{J(p) union J(p) neighbors in anti-raster order}
             * if (tmp > I(p))
             *   J(p) <- tmp
             * end */
            for (i = imax; i >= 0; i--) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = jmax; j >= 0; j--) {
                    boolval = false;
                    if ((maskval = GetByte(datam, linem, j)) < 255) {
                        maxval = GetByte(datas, lines, j);
                        if (i < imax) {
                            if (j > 0) {
                                val6 = GetByte(datas, lines + wpls, j - 1);
                                maxval = Math.Max(maxval, val6);
                            }
                            if (j < jmax) {
                                val8 = GetByte(datas, lines + wpls, j + 1);
                                maxval = Math.Max(maxval, val8);
                            }
                            val7 = GetByte(datas, lines + wpls, j);
                            maxval = Math.Max(maxval, val7);
                        }
                        if (j < jmax) {
                            val5 = GetByte(datas, lines, j + 1);
                            maxval = Math.Max(maxval, val5);
                        }
                        if (maxval > maskval)
                            SetByte(datas, lines, j, maxval);
                        val = GetByte(datas, lines, j);

                        /*
                         * If there exists a point (q) which belongs to J(p)
                         * neighbors in anti-raster order such that J(q) < J(p)
                         * and J(p) > I(q) then
                         * fifo_add(p) */
                        if (i < imax) {
                            if (j > 0) {
                                val6 = GetByte(datas, lines + wpls, j - 1);
                                if ((val6 < val) &&
                                    (val > GetByte(datam, linem + wplm, j - 1))) {
                                    boolval = true;
                                }
                            }
                            if (j < jmax) {
                                val8 = GetByte(datas, lines + wpls, j + 1);
                                if (!boolval && (val8 < val) &&
                                    (val > GetByte(datam, linem + wplm, j + 1))) {
                                    boolval = true;
                                }
                            }
                            val7 = GetByte(datas, lines + wpls, j);
                            if (!boolval && (val7 < val) &&
                                (val > GetByte(datam, linem + wplm, j))) {
                                boolval = true;
                            }
                        }
                        if (j < jmax) {
                            val5 = GetByte(datas, lines, j + 1);
                            if (!boolval && (val5 < val) &&
                                (val > GetByte(datam, linem, j + 1))) {
                                boolval = true;
                            }
                        }
                        if (boolval) {
                            pixel = new L_Pixel();
                            pixel.x = i;
                            pixel.y = j;
                            lq_pixel.Add(pixel);
                        }
                    }
                }
            }

            /* Propagation step:
             *        while fifo_empty = false
             *          p <- fifo_first()
             *          for every pixel (q) belong to neighbors of (p)
             *            if J(q) < J(p) and J(p) > I(q)
             *              J(q) <- min(J(p), I(q));
             *              fifo_add(q);
             *            end
             *          end
             *        end */
            queue_size = lq_pixel.Count;
            while (queue_size != 0) {
                pixel = lq_pixel.Remove()!;
                i = pixel.x;
                j = pixel.y;
                lines = i * wpls;
                linem = i * wplm;

                if ((val = GetByte(datas, lines, j)) > 0) {
                    if (i > 0) {
                        if (j > 0) {
                            val1 = GetByte(datas, lines - wpls, j - 1);
                            maskval = GetByte(datam, linem - wplm, j - 1);
                            if (val > val1 && val > maskval) {
                                SetByte(datas, lines - wpls, j - 1, val);
                                pixel = new L_Pixel();
                                pixel.x = i - 1;
                                pixel.y = j - 1;
                                lq_pixel.Add(pixel);
                            }
                        }
                        if (j < jmax) {
                            val3 = GetByte(datas, lines - wpls, j + 1);
                            maskval = GetByte(datam, linem - wplm, j + 1);
                            if (val > val3 && val > maskval) {
                                SetByte(datas, lines - wpls, j + 1, val);
                                pixel = new L_Pixel();
                                pixel.x = i - 1;
                                pixel.y = j + 1;
                                lq_pixel.Add(pixel);
                            }
                        }
                        val2 = GetByte(datas, lines - wpls, j);
                        maskval = GetByte(datam, linem - wplm, j);
                        if (val > val2 && val > maskval) {
                            SetByte(datas, lines - wpls, j, val);
                            pixel = new L_Pixel();
                            pixel.x = i - 1;
                            pixel.y = j;
                            lq_pixel.Add(pixel);
                        }

                    }
                    if (j > 0) {
                        val4 = GetByte(datas, lines, j - 1);
                        maskval = GetByte(datam, linem, j - 1);
                        if (val > val4 && val > maskval) {
                            SetByte(datas, lines, j - 1, val);
                            pixel = new L_Pixel();
                            pixel.x = i;
                            pixel.y = j - 1;
                            lq_pixel.Add(pixel);
                        }
                    }
                    if (i < imax) {
                        if (j > 0) {
                            val6 = GetByte(datas, lines + wpls, j - 1);
                            maskval = GetByte(datam, linem + wplm, j - 1);
                            if (val > val6 && val > maskval) {
                                SetByte(datas, lines + wpls, j - 1, val);
                                pixel = new L_Pixel();
                                pixel.x = i + 1;
                                pixel.y = j - 1;
                                lq_pixel.Add(pixel);
                            }
                        }
                        if (j < jmax) {
                            val8 = GetByte(datas, lines + wpls, j + 1);
                            maskval = GetByte(datam, linem + wplm, j + 1);
                            if (val > val8 && val > maskval) {
                                SetByte(datas, lines + wpls, j + 1, val);
                                pixel = new L_Pixel();
                                pixel.x = i + 1;
                                pixel.y = j + 1;
                                lq_pixel.Add(pixel);
                            }
                        }
                        val7 = GetByte(datas, lines + wpls, j);
                        maskval = GetByte(datam, linem + wplm, j);
                        if (val > val7 && val > maskval) {
                            SetByte(datas, lines + wpls, j, val);
                            pixel = new L_Pixel();
                            pixel.x = i + 1;
                            pixel.y = j;
                            lq_pixel.Add(pixel);
                        }
                    }
                    if (j < jmax) {
                        val5 = GetByte(datas, lines, j + 1);
                        maskval = GetByte(datam, linem, j + 1);
                        if (val > val5 && val > maskval) {
                            SetByte(datas, lines, j + 1, val);
                            pixel = new L_Pixel();
                            pixel.x = i;
                            pixel.y = j + 1;
                            lq_pixel.Add(pixel);
                        }
                    }
                }

                queue_size = lq_pixel.Count;
            }
            break;

        default:
            Log.Error("seedfillGrayInvLow", "shouldn't get here!\n");
            break;
        }
    }

    // leptonica: seedfillGrayLowSimple (seedfill.c)
    /// <summary>One raster plus one anti-raster scan of the simple grayscale seedfill.</summary>
    /// <remarks>
    /// (1) The pixels are numbered as follows:
    ///         1  2  3
    ///         4  x  5
    ///         6  7  8
    ///     This low-level filling operation consists of two scans,
    ///     raster and anti-raster, covering the entire seed image.
    ///     The caller typically iterates until the filling is
    ///     complete.
    /// (2) The filling action can be visualized from the following example.
    ///     Suppose the mask, which clips the fill, is a sombrero-shaped
    ///     surface, where the highest point is 200 and the low pixels
    ///     around the rim are 30.  Beyond the rim, the mask goes up a bit.
    ///     Suppose the seed, which is filled, consists of a single point
    ///     of height 150, located below the max of the mask, with
    ///     the rest 0.  Then in the raster scan, nothing happens until
    ///     the high seed point is encountered, and then this value is
    ///     propagated right and down, until it hits the side of the
    ///     sombrero.   The seed can never exceed the mask, so it fills
    ///     to the rim, going lower along the mask surface.  When it
    ///     passes the rim, the seed continues to fill at the rim
    ///     height to the edge of the seed image.  Then on the
    ///     anti-raster scan, the seed fills flat inside the
    ///     sombrero to the upper and left, and then out from the
    ///     rim as before.  The final result has a seed that is
    ///     flat outside the rim, and inside it fills the sombrero
    ///     but only up to 150.  If the rim height varies, the
    ///     filled seed outside the rim will be at the highest
    ///     point on the rim, which is a saddle point on the rim.
    /// </remarks>
    internal static void SeedfillGrayLowSimple(uint[] datas, int w, int h, int wpls,
                                               uint[] datam, int wplm, int connectivity)
    {
        int val2, val3, val4, val5, val7, val8;
        int val, maxval, maskval;
        int i, j, imax, jmax;
        int lines, linem;

        imax = h - 1;
        jmax = w - 1;

        switch (connectivity) {
        case 4:
            /* UL --> LR scan */
            for (i = 0; i < h; i++) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = 0; j < w; j++) {
                    if ((maskval = GetByte(datam, linem, j)) > 0) {
                        maxval = 0;
                        if (i > 0)
                            maxval = GetByte(datas, lines - wpls, j);
                        if (j > 0) {
                            val4 = GetByte(datas, lines, j - 1);
                            maxval = Math.Max(maxval, val4);
                        }
                        val = GetByte(datas, lines, j);
                        maxval = Math.Max(maxval, val);
                        val = Math.Min(maxval, maskval);
                        SetByte(datas, lines, j, val);
                    }
                }
            }

            /* LR --> UL scan */
            for (i = imax; i >= 0; i--) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = jmax; j >= 0; j--) {
                    if ((maskval = GetByte(datam, linem, j)) > 0) {
                        maxval = 0;
                        if (i < imax)
                            maxval = GetByte(datas, lines + wpls, j);
                        if (j < jmax) {
                            val5 = GetByte(datas, lines, j + 1);
                            maxval = Math.Max(maxval, val5);
                        }
                        val = GetByte(datas, lines, j);
                        maxval = Math.Max(maxval, val);
                        val = Math.Min(maxval, maskval);
                        SetByte(datas, lines, j, val);
                    }
                }
            }
            break;

        case 8:
            /* UL --> LR scan */
            for (i = 0; i < h; i++) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = 0; j < w; j++) {
                    if ((maskval = GetByte(datam, linem, j)) > 0) {
                        maxval = 0;
                        if (i > 0) {
                            if (j > 0)
                                maxval = GetByte(datas, lines - wpls, j - 1);
                            if (j < jmax) {
                                val2 = GetByte(datas, lines - wpls, j + 1);
                                maxval = Math.Max(maxval, val2);
                            }
                            val3 = GetByte(datas, lines - wpls, j);
                            maxval = Math.Max(maxval, val3);
                        }
                        if (j > 0) {
                            val4 = GetByte(datas, lines, j - 1);
                            maxval = Math.Max(maxval, val4);
                        }
                        val = GetByte(datas, lines, j);
                        maxval = Math.Max(maxval, val);
                        val = Math.Min(maxval, maskval);
                        SetByte(datas, lines, j, val);
                    }
                }
            }

            /* LR --> UL scan */
            for (i = imax; i >= 0; i--) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = jmax; j >= 0; j--) {
                    if ((maskval = GetByte(datam, linem, j)) > 0) {
                        maxval = 0;
                        if (i < imax) {
                            if (j > 0)
                                maxval = GetByte(datas, lines + wpls, j - 1);
                            if (j < jmax) {
                                val8 = GetByte(datas, lines + wpls, j + 1);
                                maxval = Math.Max(maxval, val8);
                            }
                            val7 = GetByte(datas, lines + wpls, j);
                            maxval = Math.Max(maxval, val7);
                        }
                        if (j < jmax) {
                            val5 = GetByte(datas, lines, j + 1);
                            maxval = Math.Max(maxval, val5);
                        }
                        val = GetByte(datas, lines, j);
                        maxval = Math.Max(maxval, val);
                        val = Math.Min(maxval, maskval);
                        SetByte(datas, lines, j, val);
                    }
                }
            }
            break;

        default:
            Log.Error("seedfillGrayLowSimple", "connectivity must be 4 or 8\n");
            break;
        }
    }

    // leptonica: seedfillGrayInvLowSimple (seedfill.c)
    /// <summary>One raster plus one anti-raster scan of the simple inverse grayscale seedfill.</summary>
    /// <remarks>
    /// (1) The pixels are numbered as follows:
    ///         1  2  3
    ///         4  x  5
    ///         6  7  8
    ///     This low-level filling operation consists of two scans,
    ///     raster and anti-raster, covering the entire seed image.
    ///     The caller typically iterates until the filling is
    ///     complete.
    /// (2) The "Inv" signifies the fact that in this case, filling
    ///     of the seed only takes place when the seed value is
    ///     greater than the mask value.  The mask will act to stop
    ///     the fill when it is higher than the seed level.  (This is
    ///     in contrast to conventional grayscale filling where the
    ///     seed always fills below the mask.)
    /// (3) An example of use is a basin, described by the mask (pixm),
    ///     where within the basin, the seed pix (pixs) gets filled to the
    ///     height of the highest seed pixel that is above its
    ///     corresponding max pixel.  Filling occurs while the
    ///     propagating seed pixels in pixs are larger than the
    ///     corresponding mask values in pixm.
    /// </remarks>
    internal static void SeedfillGrayInvLowSimple(uint[] datas, int w, int h, int wpls,
                                                  uint[] datam, int wplm, int connectivity)
    {
        int val1, val2, val3, val4, val5, val6, val7, val8;
        int maxval, maskval;
        int i, j, imax, jmax;
        int lines, linem;

        imax = h - 1;
        jmax = w - 1;

        switch (connectivity) {
        case 4:
            /* UL --> LR scan */
            for (i = 0; i < h; i++) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = 0; j < w; j++) {
                    if ((maskval = GetByte(datam, linem, j)) < 255) {
                        maxval = GetByte(datas, lines, j);
                        if (i > 0) {
                            val2 = GetByte(datas, lines - wpls, j);
                            maxval = Math.Max(maxval, val2);
                        }
                        if (j > 0) {
                            val4 = GetByte(datas, lines, j - 1);
                            maxval = Math.Max(maxval, val4);
                        }
                        if (maxval > maskval)
                            SetByte(datas, lines, j, maxval);
                    }
                }
            }

            /* LR --> UL scan */
            for (i = imax; i >= 0; i--) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = jmax; j >= 0; j--) {
                    if ((maskval = GetByte(datam, linem, j)) < 255) {
                        maxval = GetByte(datas, lines, j);
                        if (i < imax) {
                            val7 = GetByte(datas, lines + wpls, j);
                            maxval = Math.Max(maxval, val7);
                        }
                        if (j < jmax) {
                            val5 = GetByte(datas, lines, j + 1);
                            maxval = Math.Max(maxval, val5);
                        }
                        if (maxval > maskval)
                            SetByte(datas, lines, j, maxval);
                    }
                }
            }
            break;

        case 8:
            /* UL --> LR scan */
            for (i = 0; i < h; i++) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = 0; j < w; j++) {
                    if ((maskval = GetByte(datam, linem, j)) < 255) {
                        maxval = GetByte(datas, lines, j);
                        if (i > 0) {
                            if (j > 0) {
                                val1 = GetByte(datas, lines - wpls, j - 1);
                                maxval = Math.Max(maxval, val1);
                            }
                            if (j < jmax) {
                                val2 = GetByte(datas, lines - wpls, j + 1);
                                maxval = Math.Max(maxval, val2);
                            }
                            val3 = GetByte(datas, lines - wpls, j);
                            maxval = Math.Max(maxval, val3);
                        }
                        if (j > 0) {
                            val4 = GetByte(datas, lines, j - 1);
                            maxval = Math.Max(maxval, val4);
                        }
                        if (maxval > maskval)
                            SetByte(datas, lines, j, maxval);
                    }
                }
            }

            /* LR --> UL scan */
            for (i = imax; i >= 0; i--) {
                lines = i * wpls;
                linem = i * wplm;
                for (j = jmax; j >= 0; j--) {
                    if ((maskval = GetByte(datam, linem, j)) < 255) {
                        maxval = GetByte(datas, lines, j);
                        if (i < imax) {
                            if (j > 0) {
                                val6 = GetByte(datas, lines + wpls, j - 1);
                                maxval = Math.Max(maxval, val6);
                            }
                            if (j < jmax) {
                                val8 = GetByte(datas, lines + wpls, j + 1);
                                maxval = Math.Max(maxval, val8);
                            }
                            val7 = GetByte(datas, lines + wpls, j);
                            maxval = Math.Max(maxval, val7);
                        }
                        if (j < jmax) {
                            val5 = GetByte(datas, lines, j + 1);
                            maxval = Math.Max(maxval, val5);
                        }
                        if (maxval > maskval)
                            SetByte(datas, lines, j, maxval);
                    }
                }
            }
            break;

        default:
            Log.Error("seedfillGrayInvLowSimple", "connectivity must be 4 or 8\n");
            break;
        }
    }

    // leptonica: distanceFunctionLow (seedfill.c)
    /// <summary>One raster scan followed by one anti-raster scan of the distance function.</summary>
    internal static void DistanceFunctionLow(uint[] datad, int w, int h, int d,
                                             int wpld, int connectivity)
    {
        /* The 8 bpp sweep is the common OCR path. Cache horizontal neighbors
         * during each directional scan, avoiding repeated current-row loads. */
        if (d == 8 && w >= 3 && h >= 3 && (connectivity == 4 || connectivity == 8)) {
            DistanceFunctionLow8(datad, w, h, wpld, connectivity);
            return;
        }

        int val1, val2, val3, val4, val5, val6, val7, val8, minval, val;
        int i, j, imax, jmax;
        int lined;

        /* One raster scan followed by one anti-raster scan.
         * This does not re-set the 1-boundary of pixels that
         * were initialized to either 0 or maxval. */
        imax = h - 1;
        jmax = w - 1;
        switch (connectivity) {
        case 4:
            if (d == 8) {
                /* UL --> LR scan */
                for (i = 1; i < imax; i++) {
                    lined = i * wpld;
                    for (j = 1; j < jmax; j++) {
                        if ((val = GetByte(datad, lined, j)) > 0) {
                            val2 = GetByte(datad, lined - wpld, j);
                            val4 = GetByte(datad, lined, j - 1);
                            minval = Math.Min(val2, val4);
                            minval = Math.Min(minval, 254);
                            SetByte(datad, lined, j, minval + 1);
                        }
                    }
                }

                /* LR --> UL scan */
                for (i = imax - 1; i > 0; i--) {
                    lined = i * wpld;
                    for (j = jmax - 1; j > 0; j--) {
                        if ((val = GetByte(datad, lined, j)) > 0) {
                            val7 = GetByte(datad, lined + wpld, j);
                            val5 = GetByte(datad, lined, j + 1);
                            minval = Math.Min(val5, val7);
                            minval = Math.Min(minval + 1, val);
                            SetByte(datad, lined, j, minval);
                        }
                    }
                }
            } else {  /* d == 16 */
                /* UL --> LR scan */
                for (i = 1; i < imax; i++) {
                    lined = i * wpld;
                    for (j = 1; j < jmax; j++) {
                        if ((val = GetTwoBytes(datad, lined, j)) > 0) {
                            val2 = GetTwoBytes(datad, lined - wpld, j);
                            val4 = GetTwoBytes(datad, lined, j - 1);
                            minval = Math.Min(val2, val4);
                            minval = Math.Min(minval, 0xfffe);
                            SetTwoBytes(datad, lined, j, minval + 1);
                        }
                    }
                }

                /* LR --> UL scan */
                for (i = imax - 1; i > 0; i--) {
                    lined = i * wpld;
                    for (j = jmax - 1; j > 0; j--) {
                        if ((val = GetTwoBytes(datad, lined, j)) > 0) {
                            val7 = GetTwoBytes(datad, lined + wpld, j);
                            val5 = GetTwoBytes(datad, lined, j + 1);
                            minval = Math.Min(val5, val7);
                            minval = Math.Min(minval + 1, val);
                            SetTwoBytes(datad, lined, j, minval);
                        }
                    }
                }
            }
            break;

        case 8:
            if (d == 8) {
                /* UL --> LR scan */
                for (i = 1; i < imax; i++) {
                    lined = i * wpld;
                    for (j = 1; j < jmax; j++) {
                        if ((val = GetByte(datad, lined, j)) > 0) {
                            val1 = GetByte(datad, lined - wpld, j - 1);
                            val2 = GetByte(datad, lined - wpld, j);
                            val3 = GetByte(datad, lined - wpld, j + 1);
                            val4 = GetByte(datad, lined, j - 1);
                            minval = Math.Min(val1, val2);
                            minval = Math.Min(minval, val3);
                            minval = Math.Min(minval, val4);
                            minval = Math.Min(minval, 254);
                            SetByte(datad, lined, j, minval + 1);
                        }
                    }
                }

                /* LR --> UL scan */
                for (i = imax - 1; i > 0; i--) {
                    lined = i * wpld;
                    for (j = jmax - 1; j > 0; j--) {
                        if ((val = GetByte(datad, lined, j)) > 0) {
                            val8 = GetByte(datad, lined + wpld, j + 1);
                            val7 = GetByte(datad, lined + wpld, j);
                            val6 = GetByte(datad, lined + wpld, j - 1);
                            val5 = GetByte(datad, lined, j + 1);
                            minval = Math.Min(val8, val7);
                            minval = Math.Min(minval, val6);
                            minval = Math.Min(minval, val5);
                            minval = Math.Min(minval + 1, val);
                            SetByte(datad, lined, j, minval);
                        }
                    }
                }
            } else {  /* d == 16 */
                /* UL --> LR scan */
                for (i = 1; i < imax; i++) {
                    lined = i * wpld;
                    for (j = 1; j < jmax; j++) {
                        if ((val = GetTwoBytes(datad, lined, j)) > 0) {
                            val1 = GetTwoBytes(datad, lined - wpld, j - 1);
                            val2 = GetTwoBytes(datad, lined - wpld, j);
                            val3 = GetTwoBytes(datad, lined - wpld, j + 1);
                            val4 = GetTwoBytes(datad, lined, j - 1);
                            minval = Math.Min(val1, val2);
                            minval = Math.Min(minval, val3);
                            minval = Math.Min(minval, val4);
                            minval = Math.Min(minval, 0xfffe);
                            SetTwoBytes(datad, lined, j, minval + 1);
                        }
                    }
                }

                /* LR --> UL scan */
                for (i = imax - 1; i > 0; i--) {
                    lined = i * wpld;
                    for (j = jmax - 1; j > 0; j--) {
                        if ((val = GetTwoBytes(datad, lined, j)) > 0) {
                            val8 = GetTwoBytes(datad, lined + wpld, j + 1);
                            val7 = GetTwoBytes(datad, lined + wpld, j);
                            val6 = GetTwoBytes(datad, lined + wpld, j - 1);
                            val5 = GetTwoBytes(datad, lined, j + 1);
                            minval = Math.Min(val8, val7);
                            minval = Math.Min(minval, val6);
                            minval = Math.Min(minval, val5);
                            minval = Math.Min(minval + 1, val);
                            SetTwoBytes(datad, lined, j, minval);
                        }
                    }
                }
            }
            break;

        default:
            Log.Error("distanceFunctionLow", "connectivity must be 4 or 8\n");
            break;
        }
    }

    /// <summary>8 bpp distance sweep with rolling horizontal neighbor values.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void DistanceFunctionLow8(uint[] datad, int w, int h, int wpld,
                                             int connectivity)
    {
        int imax = h - 1;
        int jmax = w - 1;
        int x = BitConverter.IsLittleEndian ? 3 : 0;
        bool conn8 = connectivity == 8;
        ref byte p0 = ref Unsafe.As<uint, byte>(ref MemoryMarshal.GetArrayDataReference(datad));
        int stride = wpld * 4;

        /* Pixels equal to 0 never change and read as 0 for their neighbor, so
         * aligned groups of 16 pixels are visited through a bitmask of their
         * nonzero pixels.  lastj/lastv remember the most recent visited pixel:
         * it is the left (right) neighbor only if it is directly adjacent. */
        for (int i = 1; i < imax; i++) {
            ref byte cur = ref Unsafe.Add(ref p0, i * stride);
            ref byte up = ref Unsafe.Add(ref p0, (i - 1) * stride);
            int left = Unsafe.Add(ref cur, 0 ^ x);
            int j = 1;
            for (; j < jmax && (j & 15) != 0; j++)
                left = DistRasterStep(ref cur, ref up, j, left, x, conn8);
            int lastj = j - 1, lastv = left;
            for (; j + 16 <= jmax; j += 16) {
                uint m = NonZeroMask16(ref cur, j);
                while (m != 0) {
                    int jj = j + BitOperations.TrailingZeroCount(m);
                    m &= m - 1;
                    lastv = DistRasterStep(ref cur, ref up, jj, lastj == jj - 1 ? lastv : 0, x, conn8);
                    lastj = jj;
                }
            }
            left = lastj == j - 1 ? lastv : 0;
            for (; j < jmax; j++)
                left = DistRasterStep(ref cur, ref up, j, left, x, conn8);
        }

        for (int i = imax - 1; i > 0; i--) {
            ref byte cur = ref Unsafe.Add(ref p0, i * stride);
            ref byte dn = ref Unsafe.Add(ref p0, (i + 1) * stride);
            int right = Unsafe.Add(ref cur, jmax ^ x);
            int j = jmax - 1;
            for (; j > 0 && (j & 15) != 15; j--)
                right = DistAntiStep(ref cur, ref dn, j, right, x, conn8);
            int lastj = j + 1, lastv = right;
            for (; j - 15 >= 1; j -= 16) {
                int jb = j - 15;
                uint m = NonZeroMask16(ref cur, jb);
                while (m != 0) {
                    int k = BitOperations.Log2(m);
                    m ^= 1u << k;
                    int jj = jb + k;
                    lastv = DistAntiStep(ref cur, ref dn, jj, lastj == jj + 1 ? lastv : 0, x, conn8);
                    lastj = jj;
                }
            }
            right = lastj == j + 1 ? lastv : 0;
            for (; j > 0; j--)
                right = DistAntiStep(ref cur, ref dn, j, right, x, conn8);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint NonZeroMask16(ref byte row, int j)
    {
        var v = System.Runtime.Intrinsics.Vector128.LoadUnsafe(ref row, (nuint)j);
        if (BitConverter.IsLittleEndian)
            v = System.Runtime.Intrinsics.Vector128.Shuffle(v, System.Runtime.Intrinsics.Vector128.Create(
                (byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12));
        uint zeros = System.Runtime.Intrinsics.Vector128.ExtractMostSignificantBits(
            System.Runtime.Intrinsics.Vector128.Equals(v, System.Runtime.Intrinsics.Vector128<byte>.Zero));
        return ~zeros & 0xffffu;
    }

    // Returns the (possibly updated) value of pixel j.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DistRasterStep(ref byte cur, ref byte up, int j, int left, int x, bool conn8)
    {
        int value = Unsafe.Add(ref cur, j ^ x);
        if (value > 0) {
            int min = Math.Min(Unsafe.Add(ref up, j ^ x), left);
            if (conn8)
                min = Math.Min(min, Math.Min(Unsafe.Add(ref up, (j - 1) ^ x),
                                             Unsafe.Add(ref up, (j + 1) ^ x)));
            if (min > 254)
                min = 254;
            value = min + 1;
            Unsafe.Add(ref cur, j ^ x) = (byte)value;
        }
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DistAntiStep(ref byte cur, ref byte dn, int j, int right, int x, bool conn8)
    {
        int value = Unsafe.Add(ref cur, j ^ x);
        if (value > 0) {
            int min = Math.Min(Unsafe.Add(ref dn, j ^ x), right);
            if (conn8)
                min = Math.Min(min, Math.Min(Unsafe.Add(ref dn, (j - 1) ^ x),
                                             Unsafe.Add(ref dn, (j + 1) ^ x)));
            value = Math.Min(min + 1, value);
            Unsafe.Add(ref cur, j ^ x) = (byte)value;
        }
        return value;
    }

    // leptonica: seedspreadLow (seedfill.c)
    /// <summary>
    /// One raster plus one anti-raster scan of the seed spread; see pixSeedspread()
    /// for a brief description of the algorithm.
    /// </summary>
    internal static void SeedspreadLow(uint[] datad, int w, int h, int wpld,
                                       uint[] datat, int wplt, int connectivity)
    {
        int val1t, val2t, val3t, val4t, val5t, val6t, val7t, val8t;
        int i, j, imax, jmax, minval, valt, vald;
        int linet, lined;

        /* One raster scan followed by one anti-raster scan.
         * pixt is initialized to have 0 on pixels where the
         * input is specified in pixd, and to have 1 on all
         * other pixels.  We only change pixels in pixt and pixd
         * that are non-zero in pixt. */
        imax = h - 1;
        jmax = w - 1;
        switch (connectivity) {
        case 4:
            /* UL --> LR scan */
            for (i = 1; i < h; i++) {
                linet = i * wplt;
                lined = i * wpld;
                for (j = 1; j < jmax; j++) {
                    if ((valt = GetTwoBytes(datat, linet, j)) > 0) {
                        val2t = GetTwoBytes(datat, linet - wplt, j);
                        val4t = GetTwoBytes(datat, linet, j - 1);
                        minval = Math.Min(val2t, val4t);
                        minval = Math.Min(minval, 0xfffe);
                        SetTwoBytes(datat, linet, j, minval + 1);
                        if (val2t < val4t)
                            vald = GetByte(datad, lined - wpld, j);
                        else
                            vald = GetByte(datad, lined, j - 1);
                        SetByte(datad, lined, j, vald);
                    }
                }
            }

            /* LR --> UL scan */
            for (i = imax - 1; i > 0; i--) {
                linet = i * wplt;
                lined = i * wpld;
                for (j = jmax - 1; j > 0; j--) {
                    if ((valt = GetTwoBytes(datat, linet, j)) > 0) {
                        val7t = GetTwoBytes(datat, linet + wplt, j);
                        val5t = GetTwoBytes(datat, linet, j + 1);
                        minval = Math.Min(val5t, val7t);
                        minval = Math.Min(minval + 1, valt);
                        if (valt > minval) {  /* replace */
                            SetTwoBytes(datat, linet, j, minval);
                            if (val5t < val7t) {
                                vald = GetByte(datad, lined, j + 1);
                            } else {
                                /* NOTE(upstream): the line below is addressed
                                 * with wplt (the 16 bpp wpl of pixt) instead of
                                 * wpld; see docs/UPSTREAM_NOTES.md.  The C code
                                 * can read past the end of the data; that read
                                 * gives 0 here. */
                                int off = lined + wplt;
                                vald = (off + (j >> 2) < datad.Length)
                                    ? GetByte(datad, off, j) : 0;
                            }
                            SetByte(datad, lined, j, vald);
                        }
                    }
                }
            }
            break;
        case 8:
            /* UL --> LR scan */
            for (i = 1; i < h; i++) {
                linet = i * wplt;
                lined = i * wpld;
                for (j = 1; j < jmax; j++) {
                    if ((valt = GetTwoBytes(datat, linet, j)) > 0) {
                        val1t = GetTwoBytes(datat, linet - wplt, j - 1);
                        val2t = GetTwoBytes(datat, linet - wplt, j);
                        val3t = GetTwoBytes(datat, linet - wplt, j + 1);
                        val4t = GetTwoBytes(datat, linet, j - 1);
                        minval = Math.Min(val1t, val2t);
                        minval = Math.Min(minval, val3t);
                        minval = Math.Min(minval, val4t);
                        minval = Math.Min(minval, 0xfffe);
                        SetTwoBytes(datat, linet, j, minval + 1);
                        if (minval == val1t)
                            vald = GetByte(datad, lined - wpld, j - 1);
                        else if (minval == val2t)
                            vald = GetByte(datad, lined - wpld, j);
                        else if (minval == val3t)
                            vald = GetByte(datad, lined - wpld, j + 1);
                        else  /* minval == val4t */
                            vald = GetByte(datad, lined, j - 1);
                        SetByte(datad, lined, j, vald);
                    }
                }
            }

            /* LR --> UL scan */
            for (i = imax - 1; i > 0; i--) {
                linet = i * wplt;
                lined = i * wpld;
                for (j = jmax - 1; j > 0; j--) {
                    if ((valt = GetTwoBytes(datat, linet, j)) > 0) {
                        val8t = GetTwoBytes(datat, linet + wplt, j + 1);
                        val7t = GetTwoBytes(datat, linet + wplt, j);
                        val6t = GetTwoBytes(datat, linet + wplt, j - 1);
                        val5t = GetTwoBytes(datat, linet, j + 1);
                        minval = Math.Min(val8t, val7t);
                        minval = Math.Min(minval, val6t);
                        minval = Math.Min(minval, val5t);
                        minval = Math.Min(minval + 1, valt);
                        if (valt > minval) {  /* replace */
                            SetTwoBytes(datat, linet, j, minval);
                            if (minval == val5t + 1)
                                vald = GetByte(datad, lined, j + 1);
                            else if (minval == val6t + 1)
                                vald = GetByte(datad, lined + wpld, j - 1);
                            else if (minval == val7t + 1)
                                vald = GetByte(datad, lined + wpld, j);
                            else  /* minval == val8t + 1 */
                                vald = GetByte(datad, lined + wpld, j + 1);
                            SetByte(datad, lined, j, vald);
                        }
                    }
                }
            }
            break;
        default:
            Log.Error("seedspreadLow", "connectivity must be 4 or 8\n");
            break;
        }
    }
}
