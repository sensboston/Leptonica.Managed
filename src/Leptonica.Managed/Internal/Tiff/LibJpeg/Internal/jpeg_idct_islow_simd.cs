#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
/*
 * Vector128 version of the slow-but-accurate integer inverse DCT (jidctint).
 *
 * It performs exactly the same 32-bit integer operations as the scalar code in
 * jpeg_inverse_dct.jpeg_idct_islow, so the results are identical for every input,
 * including out-of-range coefficients: both passes use lanes as independent 1-D
 * transforms (columns in pass 1, rows in pass 2), the intermediate 8x8 matrix is
 * transposed in registers, the "all AC terms are zero" shortcuts of the scalar
 * code are reproduced per lane with selects (they differ from the full computation
 * when the 32-bit arithmetic overflows), and the range limit table lookup is
 * replaced by the equivalent clamp of the masked index.
 */

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace BitMiracle.LibJpeg.Classic.Internal
{
    static class jpeg_idct_islow_simd
    {
        private const int CONST_BITS = 13;
        private const int PASS1_BITS = 2;

        private const int FIX_0_298631336 = 2446;
        private const int FIX_0_390180644 = 3196;
        private const int FIX_0_541196100 = 4433;
        private const int FIX_0_765366865 = 6270;
        private const int FIX_0_899976223 = 7373;
        private const int FIX_1_175875602 = 9633;
        private const int FIX_1_501321110 = 12299;
        private const int FIX_1_847759065 = 15137;
        private const int FIX_1_961570560 = 16069;
        private const int FIX_2_053119869 = 16819;
        private const int FIX_2_562915447 = 20995;
        private const int FIX_3_072711026 = 25172;

        private const int RANGE_MASK = JpegConstants.MAXJSAMPLE * 4 + 3;
        private const int RANGE_CENTER = JpegConstants.MAXJSAMPLE * 2 + 2;
        private const int RANGE_SUBSET = RANGE_CENTER - JpegConstants.CENTERJSAMPLE;

        public static bool IsSupported
        {
            get { return Vector128.IsHardwareAccelerated; }
        }

        /// <summary>
        /// One 1-D inverse DCT on all lanes at once. tmp0e/tmp1e are the even-part
        /// terms of the DC and y4 inputs (their scaling differs between the passes).
        /// o0..o7 receive the eight outputs before the final descale.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Core(
            Vector128<int> tmp0e, Vector128<int> tmp1e,
            Vector128<int> y1, Vector128<int> y2, Vector128<int> y3,
            Vector128<int> y5, Vector128<int> y6, Vector128<int> y7,
            out Vector128<int> o0, out Vector128<int> o1, out Vector128<int> o2, out Vector128<int> o3,
            out Vector128<int> o4, out Vector128<int> o5, out Vector128<int> o6, out Vector128<int> o7)
        {
            /* Even part */
            Vector128<int> z1 = (y2 + y6) * FIX_0_541196100;
            Vector128<int> tmp2 = z1 + y2 * FIX_0_765366865;
            Vector128<int> tmp3 = z1 - y6 * FIX_1_847759065;

            Vector128<int> tmp10 = tmp0e + tmp2;
            Vector128<int> tmp13 = tmp0e - tmp2;
            Vector128<int> tmp11 = tmp1e + tmp3;
            Vector128<int> tmp12 = tmp1e - tmp3;

            /* Odd part: tmp0..tmp3 of the scalar code are y7, y5, y3, y1 */
            Vector128<int> z2 = y7 + y3;
            Vector128<int> z3 = y5 + y1;
            z1 = (z2 + z3) * FIX_1_175875602;
            z2 = z2 * (-FIX_1_961570560) + z1;
            z3 = z3 * (-FIX_0_390180644) + z1;

            z1 = (y7 + y1) * (-FIX_0_899976223);
            Vector128<int> a0 = y7 * FIX_0_298631336 + (z1 + z2);
            Vector128<int> a3 = y1 * FIX_1_501321110 + (z1 + z3);

            z1 = (y5 + y3) * (-FIX_2_562915447);
            Vector128<int> a1 = y5 * FIX_2_053119869 + (z1 + z3);
            Vector128<int> a2 = y3 * FIX_3_072711026 + (z1 + z2);

            o0 = tmp10 + a3;
            o7 = tmp10 - a3;
            o1 = tmp11 + a2;
            o6 = tmp11 - a2;
            o2 = tmp12 + a1;
            o5 = tmp12 - a1;
            o3 = tmp13 + a0;
            o4 = tmp13 - a0;
        }

        /// <summary>
        /// Pass 1 on four columns (lanes): rows r0..r7 are the raw coefficients of
        /// those columns, q0..q7 the matching multipliers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Pass1(
            Vector128<int> c0, Vector128<int> c1, Vector128<int> c2, Vector128<int> c3,
            Vector128<int> c4, Vector128<int> c5, Vector128<int> c6, Vector128<int> c7,
            Vector128<int> acZero,
            ref int q, int qStride,
            out Vector128<int> w0, out Vector128<int> w1, out Vector128<int> w2, out Vector128<int> w3,
            out Vector128<int> w4, out Vector128<int> w5, out Vector128<int> w6, out Vector128<int> w7)
        {
            Vector128<int> i0 = c0 * Vector128.LoadUnsafe(ref q);
            Vector128<int> i1 = c1 * Vector128.LoadUnsafe(ref Unsafe.Add(ref q, qStride));
            Vector128<int> i2 = c2 * Vector128.LoadUnsafe(ref Unsafe.Add(ref q, qStride * 2));
            Vector128<int> i3 = c3 * Vector128.LoadUnsafe(ref Unsafe.Add(ref q, qStride * 3));
            Vector128<int> i4 = c4 * Vector128.LoadUnsafe(ref Unsafe.Add(ref q, qStride * 4));
            Vector128<int> i5 = c5 * Vector128.LoadUnsafe(ref Unsafe.Add(ref q, qStride * 5));
            Vector128<int> i6 = c6 * Vector128.LoadUnsafe(ref Unsafe.Add(ref q, qStride * 6));
            Vector128<int> i7 = c7 * Vector128.LoadUnsafe(ref Unsafe.Add(ref q, qStride * 7));

            /* Add fudge factor here for final descale. */
            Vector128<int> z2 = Vector128.ShiftLeft(i0, CONST_BITS) + Vector128.Create(1 << (CONST_BITS - PASS1_BITS - 1));
            Vector128<int> z3 = Vector128.ShiftLeft(i4, CONST_BITS);

            Core(z2 + z3, z2 - z3, i1, i2, i3, i5, i6, i7,
                out w0, out w1, out w2, out w3, out w4, out w5, out w6, out w7);

            const int shift = CONST_BITS - PASS1_BITS;
            Vector128<int> dc = Vector128.ShiftLeft(i0, PASS1_BITS);
            w0 = Vector128.ConditionalSelect(acZero, dc, Vector128.ShiftRightArithmetic(w0, shift));
            w1 = Vector128.ConditionalSelect(acZero, dc, Vector128.ShiftRightArithmetic(w1, shift));
            w2 = Vector128.ConditionalSelect(acZero, dc, Vector128.ShiftRightArithmetic(w2, shift));
            w3 = Vector128.ConditionalSelect(acZero, dc, Vector128.ShiftRightArithmetic(w3, shift));
            w4 = Vector128.ConditionalSelect(acZero, dc, Vector128.ShiftRightArithmetic(w4, shift));
            w5 = Vector128.ConditionalSelect(acZero, dc, Vector128.ShiftRightArithmetic(w5, shift));
            w6 = Vector128.ConditionalSelect(acZero, dc, Vector128.ShiftRightArithmetic(w6, shift));
            w7 = Vector128.ConditionalSelect(acZero, dc, Vector128.ShiftRightArithmetic(w7, shift));
        }

        /// <summary>
        /// Transposes a 4x4 block of ints held in four vectors (rows a..d).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Transpose4(
            Vector128<int> a, Vector128<int> b, Vector128<int> c, Vector128<int> d,
            out Vector128<int> r0, out Vector128<int> r1, out Vector128<int> r2, out Vector128<int> r3)
        {
            Vector128<int> odd = Vector128.Create(0, -1, 0, -1);
            Vector128<int> x0 = Vector128.Create(a.GetLower(), c.GetLower());   /* a0 a1 c0 c1 */
            Vector128<int> x1 = Vector128.Create(b.GetLower(), d.GetLower());   /* b0 b1 d0 d1 */
            Vector128<int> x2 = Vector128.Create(a.GetUpper(), c.GetUpper());   /* a2 a3 c2 c3 */
            Vector128<int> x3 = Vector128.Create(b.GetUpper(), d.GetUpper());   /* b2 b3 d2 d3 */
            Vector128<int> dup02 = Vector128.Create(0, 0, 2, 2);
            Vector128<int> dup13 = Vector128.Create(1, 1, 3, 3);
            r0 = Vector128.ConditionalSelect(odd, Vector128.Shuffle(x1, dup02), x0);
            r1 = Vector128.ConditionalSelect(odd, x1, Vector128.Shuffle(x0, dup13));
            r2 = Vector128.ConditionalSelect(odd, Vector128.Shuffle(x3, dup02), x2);
            r3 = Vector128.ConditionalSelect(odd, x3, Vector128.Shuffle(x2, dup13));
        }

        /// <summary>
        /// The post-IDCT range limit of the scalar code,
        /// limit[(limitOffset + t) &amp; RANGE_MASK], for the standard table
        /// (zero below tableOffset, identity for 256 entries, 255 above, 1280 entries).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<int> Limit(Vector128<int> t, Vector128<int> idxOffset, Vector128<int> tableOffset)
        {
            Vector128<int> idx = (t + idxOffset) & Vector128.Create(RANGE_MASK);
            return Vector128.Min(Vector128.Max(idx - tableOffset, Vector128<int>.Zero), Vector128.Create((int)JpegConstants.MAXJSAMPLE));
        }

        /// <summary>
        /// Pass 2 on four rows (lanes): v0..v7 are the workspace values of those rows.
        /// Returns the eight output samples (0..255 per lane) of every row.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Pass2(
            Vector128<int> v0, Vector128<int> v1, Vector128<int> v2, Vector128<int> v3,
            Vector128<int> v4, Vector128<int> v5, Vector128<int> v6, Vector128<int> v7,
            Vector128<int> idxOffset, Vector128<int> tableOffset,
            out Vector128<int> o0, out Vector128<int> o1, out Vector128<int> o2, out Vector128<int> o3,
            out Vector128<int> o4, out Vector128<int> o5, out Vector128<int> o6, out Vector128<int> o7)
        {
            /* Add range center and fudge factor for final descale and range-limit. */
            Vector128<int> z2 = v0 + Vector128.Create((RANGE_CENTER << (PASS1_BITS + 3)) + (1 << (PASS1_BITS + 2)));
            Vector128<int> acZero = Vector128.Equals(v1 | v2 | v3 | v4 | v5 | v6 | v7, Vector128<int>.Zero);

            Core(Vector128.ShiftLeft(z2 + v4, CONST_BITS), Vector128.ShiftLeft(z2 - v4, CONST_BITS),
                v1, v2, v3, v5, v6, v7,
                out o0, out o1, out o2, out o3, out o4, out o5, out o6, out o7);

            const int shift = CONST_BITS + PASS1_BITS + 3;
            o0 = Limit(Vector128.ShiftRightArithmetic(o0, shift), idxOffset, tableOffset);
            o1 = Limit(Vector128.ShiftRightArithmetic(o1, shift), idxOffset, tableOffset);
            o2 = Limit(Vector128.ShiftRightArithmetic(o2, shift), idxOffset, tableOffset);
            o3 = Limit(Vector128.ShiftRightArithmetic(o3, shift), idxOffset, tableOffset);
            o4 = Limit(Vector128.ShiftRightArithmetic(o4, shift), idxOffset, tableOffset);
            o5 = Limit(Vector128.ShiftRightArithmetic(o5, shift), idxOffset, tableOffset);
            o6 = Limit(Vector128.ShiftRightArithmetic(o6, shift), idxOffset, tableOffset);
            o7 = Limit(Vector128.ShiftRightArithmetic(o7, shift), idxOffset, tableOffset);

            /* AC terms all zero in a row: the scalar code masks first and adds the offset
             * afterwards (no wrap-around of the index). */
            Vector128<int> dcIdx = idxOffset + (Vector128.ShiftRightArithmetic(z2, PASS1_BITS + 3) & Vector128.Create(RANGE_MASK));
            Vector128<int> dcv = Vector128.Min(Vector128.Max(dcIdx - tableOffset, Vector128<int>.Zero), Vector128.Create((int)JpegConstants.MAXJSAMPLE));
            o0 = Vector128.ConditionalSelect(acZero, dcv, o0);
            o1 = Vector128.ConditionalSelect(acZero, dcv, o1);
            o2 = Vector128.ConditionalSelect(acZero, dcv, o2);
            o3 = Vector128.ConditionalSelect(acZero, dcv, o3);
            o4 = Vector128.ConditionalSelect(acZero, dcv, o4);
            o5 = Vector128.ConditionalSelect(acZero, dcv, o5);
            o6 = Vector128.ConditionalSelect(acZero, dcv, o6);
            o7 = Vector128.ConditionalSelect(acZero, dcv, o7);
        }

        /// <summary>
        /// Packs four samples per lane into one 32-bit word (a in the low byte).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<uint> Pack4(Vector128<int> a, Vector128<int> b, Vector128<int> c, Vector128<int> d)
        {
            return (a | Vector128.ShiftLeft(b, 8) | Vector128.ShiftLeft(c, 16) | Vector128.ShiftLeft(d, 24)).AsUInt32();
        }

        /// <summary>
        /// Dequantizes and inverse-transforms one 8x8 block and writes the 8x8 samples.
        /// </summary>
        /// <param name="coef">64 coefficients in natural order.</param>
        /// <param name="quant">64 multipliers in natural order.</param>
        /// <param name="sampleRangeLimitOffset">Offset of the zero point in the range limit table.</param>
        public static void Idct(short[] coef, int[] quant, int sampleRangeLimitOffset,
            ComponentBuffer output, int outputRow, int outputCol)
        {
            if (coef.Length < JpegConstants.DCTSIZE2 || quant.Length < JpegConstants.DCTSIZE2)
                throw new ArgumentException("block too short");

            ref short cp = ref coef[0];
            ref int qp = ref quant[0];

            Vector128<short> s0 = Vector128.LoadUnsafe(ref cp);
            Vector128<short> s1 = Vector128.LoadUnsafe(ref cp, 8);
            Vector128<short> s2 = Vector128.LoadUnsafe(ref cp, 16);
            Vector128<short> s3 = Vector128.LoadUnsafe(ref cp, 24);
            Vector128<short> s4 = Vector128.LoadUnsafe(ref cp, 32);
            Vector128<short> s5 = Vector128.LoadUnsafe(ref cp, 40);
            Vector128<short> s6 = Vector128.LoadUnsafe(ref cp, 48);
            Vector128<short> s7 = Vector128.LoadUnsafe(ref cp, 56);

            Vector128<short> ac = s1 | s2 | s3 | s4 | s5 | s6 | s7;

            int limitBase = sampleRangeLimitOffset - RANGE_SUBSET;

            if (ac == Vector128<short>.Zero && (s0 & Vector128.Create((short)0, -1, -1, -1, -1, -1, -1, -1)) == Vector128<short>.Zero)
            {
                /* Only the DC coefficient is present: all 64 samples are equal. */
                int z2 = ((coef[0] * quant[0]) << PASS1_BITS)
                    + (RANGE_CENTER << (PASS1_BITS + 3)) + (1 << (PASS1_BITS + 2));
                int idx = limitBase + ((z2 >> (PASS1_BITS + 3)) & RANGE_MASK);
                int dcIdx = idx - sampleRangeLimitOffset;
                byte dcval = (byte)(dcIdx < 0 ? 0 : dcIdx > JpegConstants.MAXJSAMPLE ? JpegConstants.MAXJSAMPLE : dcIdx);
                for (int r = 0; r < JpegConstants.DCTSIZE; r++)
                    output[outputRow + r].AsSpan(outputCol, JpegConstants.DCTSIZE).Fill(dcval);
                return;
            }

            /* Pass 1: columns. Lanes 0..3 are columns 0..3, the "hi" set columns 4..7. */
            Vector128<int> acZeroLo = Vector128.Equals(Vector128.WidenLower(ac), Vector128<int>.Zero);
            Vector128<int> acZeroHi = Vector128.Equals(Vector128.WidenUpper(ac), Vector128<int>.Zero);

            Pass1(Vector128.WidenLower(s0), Vector128.WidenLower(s1), Vector128.WidenLower(s2), Vector128.WidenLower(s3),
                Vector128.WidenLower(s4), Vector128.WidenLower(s5), Vector128.WidenLower(s6), Vector128.WidenLower(s7),
                acZeroLo, ref qp, 8,
                out Vector128<int> a0, out Vector128<int> a1, out Vector128<int> a2, out Vector128<int> a3,
                out Vector128<int> a4, out Vector128<int> a5, out Vector128<int> a6, out Vector128<int> a7);
            Pass1(Vector128.WidenUpper(s0), Vector128.WidenUpper(s1), Vector128.WidenUpper(s2), Vector128.WidenUpper(s3),
                Vector128.WidenUpper(s4), Vector128.WidenUpper(s5), Vector128.WidenUpper(s6), Vector128.WidenUpper(s7),
                acZeroHi, ref Unsafe.Add(ref qp, 4), 8,
                out Vector128<int> b0, out Vector128<int> b1, out Vector128<int> b2, out Vector128<int> b3,
                out Vector128<int> b4, out Vector128<int> b5, out Vector128<int> b6, out Vector128<int> b7);

            /* a_k / b_k: workspace row k, columns 0..3 / 4..7. Transpose so that lanes are rows. */
            Transpose4(a0, a1, a2, a3, out Vector128<int> t0l, out Vector128<int> t1l, out Vector128<int> t2l, out Vector128<int> t3l);
            Transpose4(b0, b1, b2, b3, out Vector128<int> t4l, out Vector128<int> t5l, out Vector128<int> t6l, out Vector128<int> t7l);
            Transpose4(a4, a5, a6, a7, out Vector128<int> t0h, out Vector128<int> t1h, out Vector128<int> t2h, out Vector128<int> t3h);
            Transpose4(b4, b5, b6, b7, out Vector128<int> t4h, out Vector128<int> t5h, out Vector128<int> t6h, out Vector128<int> t7h);

            /* Pass 2: rows. tXl holds column X of rows 0..3, tXh of rows 4..7. */
            Vector128<int> idxOffset = Vector128.Create(limitBase);
            Vector128<int> tableOffset = Vector128.Create(sampleRangeLimitOffset);

            Pass2(t0l, t1l, t2l, t3l, t4l, t5l, t6l, t7l, idxOffset, tableOffset,
                out Vector128<int> p0, out Vector128<int> p1, out Vector128<int> p2, out Vector128<int> p3,
                out Vector128<int> p4, out Vector128<int> p5, out Vector128<int> p6, out Vector128<int> p7);
            Pass2(t0h, t1h, t2h, t3h, t4h, t5h, t6h, t7h, idxOffset, tableOffset,
                out Vector128<int> q0, out Vector128<int> q1, out Vector128<int> q2, out Vector128<int> q3,
                out Vector128<int> q4, out Vector128<int> q5, out Vector128<int> q6, out Vector128<int> q7);

            /* pN: output column N of rows 0..3; qN: of rows 4..7. Build 8 bytes per row. */
            Vector128<uint> wl0 = Pack4(p0, p1, p2, p3);   /* columns 0..3, rows 0..3 */
            Vector128<uint> wl1 = Pack4(p4, p5, p6, p7);   /* columns 4..7, rows 0..3 */
            Vector128<uint> wh0 = Pack4(q0, q1, q2, q3);
            Vector128<uint> wh1 = Pack4(q4, q5, q6, q7);

            Vector128<ulong> r01 = Vector128.WidenLower(wl0) | Vector128.ShiftLeft(Vector128.WidenLower(wl1), 32);
            Vector128<ulong> r23 = Vector128.WidenUpper(wl0) | Vector128.ShiftLeft(Vector128.WidenUpper(wl1), 32);
            Vector128<ulong> r45 = Vector128.WidenLower(wh0) | Vector128.ShiftLeft(Vector128.WidenLower(wh1), 32);
            Vector128<ulong> r67 = Vector128.WidenUpper(wh0) | Vector128.ShiftLeft(Vector128.WidenUpper(wh1), 32);

            Store(output, outputRow, outputCol, r01, r23, r45, r67);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Store(ComponentBuffer output, int row, int col,
            Vector128<ulong> r01, Vector128<ulong> r23, Vector128<ulong> r45, Vector128<ulong> r67)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(output[row].AsSpan(col, 8), r01.GetElement(0));
            BinaryPrimitives.WriteUInt64LittleEndian(output[row + 1].AsSpan(col, 8), r01.GetElement(1));
            BinaryPrimitives.WriteUInt64LittleEndian(output[row + 2].AsSpan(col, 8), r23.GetElement(0));
            BinaryPrimitives.WriteUInt64LittleEndian(output[row + 3].AsSpan(col, 8), r23.GetElement(1));
            BinaryPrimitives.WriteUInt64LittleEndian(output[row + 4].AsSpan(col, 8), r45.GetElement(0));
            BinaryPrimitives.WriteUInt64LittleEndian(output[row + 5].AsSpan(col, 8), r45.GetElement(1));
            BinaryPrimitives.WriteUInt64LittleEndian(output[row + 6].AsSpan(col, 8), r67.GetElement(0));
            BinaryPrimitives.WriteUInt64LittleEndian(output[row + 7].AsSpan(col, 8), r67.GetElement(1));
        }
    }
}
