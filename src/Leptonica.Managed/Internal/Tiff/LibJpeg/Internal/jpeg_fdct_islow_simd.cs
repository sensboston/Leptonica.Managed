#nullable disable
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace BitMiracle.LibJpeg.Classic.Internal
{
    /// <summary>
    /// jpeg_fdct_islow with Vector128: pass 1 handles 4 rows per vector, pass 2
    /// 4 columns per vector, with a 4x4 transpose in registers in between.  The
    /// integer operations are those of the scalar code (additions in a different
    /// grouping give the same 32-bit results), so the output is identical.
    /// </summary>
    partial class jpeg_forward_dct
    {
        private static void jpeg_fdct_islow_vector(int[] data, byte[][] sample_data, int start_row, int start_col)
        {
            /* Pass 1: rows 0..3 and 4..7.  e[k] holds column k of four rows. */
            Pass1(sample_data, start_row, start_col,
                out Vector128<int> a0, out Vector128<int> a1, out Vector128<int> a2, out Vector128<int> a3,
                out Vector128<int> a4, out Vector128<int> a5, out Vector128<int> a6, out Vector128<int> a7);
            Pass1(sample_data, start_row + 4, start_col,
                out Vector128<int> b0, out Vector128<int> b1, out Vector128<int> b2, out Vector128<int> b3,
                out Vector128<int> b4, out Vector128<int> b5, out Vector128<int> b6, out Vector128<int> b7);

            /* a_k / b_k: coefficient k of rows 0..3 / 4..7.  Transpose so that lanes are columns. */
            Transpose4(a0, a1, a2, a3, out Vector128<int> r0l, out Vector128<int> r1l, out Vector128<int> r2l, out Vector128<int> r3l);
            Transpose4(a4, a5, a6, a7, out Vector128<int> r0h, out Vector128<int> r1h, out Vector128<int> r2h, out Vector128<int> r3h);
            Transpose4(b0, b1, b2, b3, out Vector128<int> r4l, out Vector128<int> r5l, out Vector128<int> r6l, out Vector128<int> r7l);
            Transpose4(b4, b5, b6, b7, out Vector128<int> r4h, out Vector128<int> r5h, out Vector128<int> r6h, out Vector128<int> r7h);

            /* Pass 2: columns 0..3 and 4..7 */
            Pass2(data, 0, r0l, r1l, r2l, r3l, r4l, r5l, r6l, r7l);
            Pass2(data, 4, r0h, r1h, r2h, r3h, r4h, r5h, r6h, r7h);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Pass1(byte[][] s, int row, int col,
            out Vector128<int> d0, out Vector128<int> d1, out Vector128<int> d2, out Vector128<int> d3,
            out Vector128<int> d4, out Vector128<int> d5, out Vector128<int> d6, out Vector128<int> d7)
        {
            LoadRow(s[row], col, out Vector128<int> p0l, out Vector128<int> p0h);
            LoadRow(s[row + 1], col, out Vector128<int> p1l, out Vector128<int> p1h);
            LoadRow(s[row + 2], col, out Vector128<int> p2l, out Vector128<int> p2h);
            LoadRow(s[row + 3], col, out Vector128<int> p3l, out Vector128<int> p3h);
            Transpose4(p0l, p1l, p2l, p3l, out Vector128<int> e0, out Vector128<int> e1, out Vector128<int> e2, out Vector128<int> e3);
            Transpose4(p0h, p1h, p2h, p3h, out Vector128<int> e4, out Vector128<int> e5, out Vector128<int> e6, out Vector128<int> e7);

            Vector128<int> tmp0 = e0 + e7, tmp1 = e1 + e6, tmp2 = e2 + e5, tmp3 = e3 + e4;
            Vector128<int> tmp10 = tmp0 + tmp3, tmp12 = tmp0 - tmp3, tmp11 = tmp1 + tmp2, tmp13 = tmp1 - tmp2;
            tmp0 = e0 - e7;
            tmp1 = e1 - e6;
            tmp2 = e2 - e5;
            tmp3 = e3 - e4;

            d0 = (tmp10 + tmp11 - Vector128.Create(8 * JpegConstants.CENTERJSAMPLE)) << SLOW_INTEGER_PASS1_BITS;
            d4 = (tmp10 - tmp11) << SLOW_INTEGER_PASS1_BITS;

            const int sh = SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS;
            Vector128<int> round = Vector128.Create(1 << (sh - 1));
            Vector128<int> z1 = (tmp12 + tmp13) * SLOW_INTEGER_FIX_0_541196100 + round;
            d2 = (z1 + tmp12 * SLOW_INTEGER_FIX_0_765366865) >> sh;
            d6 = (z1 - tmp13 * SLOW_INTEGER_FIX_1_847759065) >> sh;

            Odd(tmp0, tmp1, tmp2, tmp3, round, out Vector128<int> o1, out Vector128<int> o3, out Vector128<int> o5, out Vector128<int> o7);
            d1 = o1 >> sh;
            d3 = o3 >> sh;
            d5 = o5 >> sh;
            d7 = o7 >> sh;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Pass2(int[] data, int h,
            Vector128<int> w0, Vector128<int> w1, Vector128<int> w2, Vector128<int> w3,
            Vector128<int> w4, Vector128<int> w5, Vector128<int> w6, Vector128<int> w7)
        {
            Vector128<int> tmp0 = w0 + w7, tmp1 = w1 + w6, tmp2 = w2 + w5, tmp3 = w3 + w4;
            Vector128<int> tmp10 = tmp0 + tmp3 + Vector128.Create(1 << (SLOW_INTEGER_PASS1_BITS - 1));
            Vector128<int> tmp12 = tmp0 - tmp3, tmp11 = tmp1 + tmp2, tmp13 = tmp1 - tmp2;
            tmp0 = w0 - w7;
            tmp1 = w1 - w6;
            tmp2 = w2 - w5;
            tmp3 = w3 - w4;

            ((tmp10 + tmp11) >> SLOW_INTEGER_PASS1_BITS).StoreUnsafe(ref data[JpegConstants.DCTSIZE * 0 + h]);
            ((tmp10 - tmp11) >> SLOW_INTEGER_PASS1_BITS).StoreUnsafe(ref data[JpegConstants.DCTSIZE * 4 + h]);

            const int sh = SLOW_INTEGER_CONST_BITS + SLOW_INTEGER_PASS1_BITS;
            Vector128<int> round = Vector128.Create(1 << (sh - 1));
            Vector128<int> z1 = (tmp12 + tmp13) * SLOW_INTEGER_FIX_0_541196100 + round;
            ((z1 + tmp12 * SLOW_INTEGER_FIX_0_765366865) >> sh).StoreUnsafe(ref data[JpegConstants.DCTSIZE * 2 + h]);
            ((z1 - tmp13 * SLOW_INTEGER_FIX_1_847759065) >> sh).StoreUnsafe(ref data[JpegConstants.DCTSIZE * 6 + h]);

            Odd(tmp0, tmp1, tmp2, tmp3, round, out Vector128<int> o1, out Vector128<int> o3, out Vector128<int> o5, out Vector128<int> o7);
            (o1 >> sh).StoreUnsafe(ref data[JpegConstants.DCTSIZE * 1 + h]);
            (o3 >> sh).StoreUnsafe(ref data[JpegConstants.DCTSIZE * 3 + h]);
            (o5 >> sh).StoreUnsafe(ref data[JpegConstants.DCTSIZE * 5 + h]);
            (o7 >> sh).StoreUnsafe(ref data[JpegConstants.DCTSIZE * 7 + h]);
        }

        /// <summary>Odd part of both passes (inputs tmp0..tmp3, rounding term already scaled).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Odd(Vector128<int> tmp0, Vector128<int> tmp1, Vector128<int> tmp2, Vector128<int> tmp3,
            Vector128<int> round, out Vector128<int> o1, out Vector128<int> o3, out Vector128<int> o5, out Vector128<int> o7)
        {
            Vector128<int> tmp12 = tmp0 + tmp2;
            Vector128<int> tmp13 = tmp1 + tmp3;
            Vector128<int> z1 = (tmp12 + tmp13) * SLOW_INTEGER_FIX_1_175875602 + round;
            tmp12 = tmp12 * (-SLOW_INTEGER_FIX_0_390180644) + z1;
            tmp13 = tmp13 * (-SLOW_INTEGER_FIX_1_961570560) + z1;

            z1 = (tmp0 + tmp3) * (-SLOW_INTEGER_FIX_0_899976223);
            o1 = tmp0 * SLOW_INTEGER_FIX_1_501321110 + z1 + tmp12;
            o7 = tmp3 * SLOW_INTEGER_FIX_0_298631336 + z1 + tmp13;

            z1 = (tmp1 + tmp2) * (-SLOW_INTEGER_FIX_2_562915447);
            o3 = tmp1 * SLOW_INTEGER_FIX_3_072711026 + z1 + tmp13;
            o5 = tmp2 * SLOW_INTEGER_FIX_2_053119869 + z1 + tmp12;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LoadRow(byte[] row, int col, out Vector128<int> lo, out Vector128<int> hi)
        {
            Vector128<ushort> w = Vector128.WidenLower(Vector128.Create(Vector64.LoadUnsafe(ref row[col]), Vector64<byte>.Zero));
            lo = Vector128.WidenLower(w).AsInt32();
            hi = Vector128.WidenUpper(w).AsInt32();
        }

        /// <summary>Transposes a 4x4 block of ints held in four vectors (rows a..d).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Transpose4(
            Vector128<int> a, Vector128<int> b, Vector128<int> c, Vector128<int> d,
            out Vector128<int> r0, out Vector128<int> r1, out Vector128<int> r2, out Vector128<int> r3)
        {
            Vector128<int> odd = Vector128.Create(0, -1, 0, -1);
            Vector128<int> x0 = Vector128.Create(a.GetLower(), c.GetLower());
            Vector128<int> x1 = Vector128.Create(b.GetLower(), d.GetLower());
            Vector128<int> x2 = Vector128.Create(a.GetUpper(), c.GetUpper());
            Vector128<int> x3 = Vector128.Create(b.GetUpper(), d.GetUpper());
            Vector128<int> dup02 = Vector128.Create(0, 0, 2, 2);
            Vector128<int> dup13 = Vector128.Create(1, 1, 3, 3);
            r0 = Vector128.ConditionalSelect(odd, Vector128.Shuffle(x1, dup02), x0);
            r1 = Vector128.ConditionalSelect(odd, x1, Vector128.Shuffle(x0, dup13));
            r2 = Vector128.ConditionalSelect(odd, Vector128.Shuffle(x3, dup02), x2);
            r3 = Vector128.ConditionalSelect(odd, x3, Vector128.Shuffle(x2, dup13));
        }
    }
}
