#nullable disable
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace BitMiracle.LibJpeg.Classic.Internal
{
    partial class huff_entropy_encoder
    {
        /// <summary>
        /// Baseline sequential Huffman encoding (encode_mcu_huff with
        /// encode_one_block and emit_bits), bit-exact with them, for scans without
        /// restart markers, with the standard 8x8 zigzag order and an output buffer
        /// that never suspends.
        /// </summary>
        /// <remarks>
        /// Two ideas carry most of the speed: the entropy-coded bits of an MCU are
        /// collected in a private scratch buffer, so the bit packing loop never has to
        /// look at the destination buffer or at byte stuffing, and only the nonzero AC
        /// coefficients of a block are visited, so zero runs are index differences
        /// instead of 63 tests.  A second pass copies the scratch bytes into the
        /// destination buffer and inserts the stuffing zeroes, eight bytes at a time
        /// whenever the next eight cannot contain a 0xFF.
        /// </remarks>
        private sealed class FastBaselineEncoder
        {
            /// <summary>
            /// Upper bound of entropy-coded (unstuffed) bytes per block: a 12 bit DC code
            /// plus its 11 value bits, and 63 AC coefficients of at most 16 code plus
            /// 10 value bits each, rounded up to a whole number of eight byte stores.
            /// </summary>
            private const int MAX_BYTES_PER_BLOCK = 256;

            private readonly jpeg_compress_struct m_cinfo;
            private readonly jpeg_destination_mgr m_dest;
            private readonly int m_blocksInMcu;

            /// <summary>Component index of each block of an MCU (DC predictor slot).</summary>
            private readonly int[] m_membership;

            /// <summary>Code and length per symbol, flattened per block position: [block * 256 + symbol].</summary>
            private readonly uint[] m_dcCode;
            private readonly byte[] m_dcSize;
            private readonly uint[] m_acCode;
            private readonly byte[] m_acSize;

            private readonly int[] m_lastDcVal = new int[JpegConstants.MAX_COMPS_IN_SCAN];

            /// <summary>
            /// Zigzag bit mask of each byte of a natural-order block, 8 rows of 256
            /// entries: s_zigzagBits[r * 256 + b] holds the zigzag bits of the natural
            /// coefficients r*8..r*8+7 that are nonzero in b (bit i of b selects the
            /// coefficient r*8+i).  One lookup per row replaces a bit loop over the
            /// nonzero coefficients of the block.
            /// </summary>
            private static readonly ulong[] s_zigzagBits = BuildZigzagBits();

            /// <summary>Entropy-coded bytes of the MCU being encoded, without stuffing.</summary>
            private readonly byte[] m_scratch;

            /// <summary>Pending bits, left-justified; m_bitCnt of them are valid.</summary>
            private ulong m_bitBuf;
            private int m_bitCnt;

            public FastBaselineEncoder(jpeg_compress_struct cinfo, c_derived_tbl[] dcTbls, c_derived_tbl[] acTbls)
            {
                m_cinfo = cinfo;
                m_dest = cinfo.m_dest;
                m_blocksInMcu = cinfo.m_blocks_in_MCU;
                m_membership = new int[m_blocksInMcu];
                m_scratch = new byte[m_blocksInMcu * MAX_BYTES_PER_BLOCK + 8];

                int n = m_blocksInMcu * 256;
                m_dcCode = new uint[n];
                m_dcSize = new byte[n];
                m_acCode = new uint[n];
                m_acSize = new byte[n];
                for (int b = 0; b < m_blocksInMcu; b++)
                {
                    int ci = cinfo.m_MCU_membership[b];
                    m_membership[b] = ci;
                    jpeg_component_info compptr = cinfo.Component_info[cinfo.m_cur_comp_info[ci]];
                    CopyTable(dcTbls[compptr.Dc_tbl_no], b * 256, m_dcCode, m_dcSize);
                    CopyTable(acTbls[compptr.Ac_tbl_no], b * 256, m_acCode, m_acSize);
                }
            }

            /// <summary>The fast path applies to this scan.</summary>
            public static bool Applies(jpeg_compress_struct cinfo)
            {
                return cinfo.m_restart_interval == 0 && cinfo.lim_Se == JpegConstants.DCTSIZE2 - 1 &&
                       ReferenceEquals(cinfo.natural_order, JpegUtils.jpeg_natural_order) &&
                       BitConverter.IsLittleEndian;
            }

            private static ulong[] BuildZigzagBits()
            {
                int[] naturalOrder = JpegUtils.jpeg_natural_order;
                var zigzagOfNatural = new int[64];
                for (int k = 1; k < 64; k++)
                    zigzagOfNatural[naturalOrder[k]] = k;

                var bits = new ulong[8 * 256];
                for (int r = 0; r < 8; r++)
                {
                    for (int b = 0; b < 256; b++)
                    {
                        ulong mask = 0;
                        for (int i = 0; i < 8; i++)
                        {
                            if ((b & (1 << i)) != 0)
                                mask |= 1UL << zigzagOfNatural[r * 8 + i];
                        }
                        bits[r * 256 + b] = mask;
                    }
                }
                return bits;
            }

            /// <summary>
            /// Copies a derived table into the flattened arrays, masking each code to
            /// its length exactly like emit_bits does.
            /// </summary>
            private static void CopyTable(c_derived_tbl table, int offset, uint[] codes, byte[] sizes)
            {
                for (int i = 0; i < 256; i++)
                {
                    int size = table.ehufsi[i];
                    int code = table.ehufco[i];
                    if (size < 32)
                        code &= (1 << size) - 1;
                    codes[offset + i] = (uint)code;
                    sizes[offset + i] = (byte)size;
                }
            }

            public void EncodeMcu(JBLOCK[][] MCU_data)
            {
                byte[] scratch = m_scratch;
                ulong bitBuf = m_bitBuf;
                int bitCnt = m_bitCnt;
                int spos = 0;

                int[] lastDcVal = m_lastDcVal;
                int blocks = m_blocksInMcu;
                int[] membership = m_membership;

                // Raw starts of the per-symbol tables and of the scratch buffer: no
                // bounds checks in the loops below.
                ref uint dcCode = ref MemoryMarshal.GetArrayDataReference(m_dcCode);
                ref byte dcSize = ref MemoryMarshal.GetArrayDataReference(m_dcSize);
                ref uint acCode = ref MemoryMarshal.GetArrayDataReference(m_acCode);
                ref byte acSize = ref MemoryMarshal.GetArrayDataReference(m_acSize);
                ref int order = ref MemoryMarshal.GetArrayDataReference(JpegUtils.jpeg_natural_order);
                ref byte dst = ref MemoryMarshal.GetArrayDataReference(scratch);

                for (int blkn = 0; blkn < blocks; blkn++)
                {
                    short[] block = MCU_data[blkn][0].data;
                    int t = blkn * 256;
                    ref uint acCodeT = ref Unsafe.Add(ref acCode, t);
                    ref byte acSizeT = ref Unsafe.Add(ref acSize, t);

                    // ----- DC coefficient: encode the difference to the previous one -----
                    int ci = membership[blkn];
                    int dcVal = block[0];
                    int diff = dcVal - lastDcVal[ci];
                    lastDcVal[ci] = dcVal;

                    int abs = diff < 0 ? -diff : diff;
                    int nbits = 32 - BitOperations.LeadingZeroCount((uint)abs);
                    if (nbits > MAX_COEF_BITS + 1)
                        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_BAD_DCT_COEF);

                    int size = Unsafe.Add(ref dcSize, t + nbits);
                    if (size == 0)
                        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_HUFF_MISSING_CODE);

                    // A negative value is coded as the one's complement of its magnitude;
                    // the code and the value bits go into the buffer as one word.
                    int value = diff + (diff >> 31);
                    Append(((ulong)Unsafe.Add(ref dcCode, t + nbits) << nbits) | (uint)(value & ((1 << nbits) - 1)),
                           size + nbits, ref bitBuf, ref bitCnt, ref spos, ref dst);

                    // ----- AC coefficients in zigzag order -----
                    // Bit k of the mask is set when the k-th coefficient in zigzag order
                    // is nonzero; only those coefficients are visited, so a zero run is
                    // the distance to the previous nonzero position.
                    ulong mask = NonzeroMask(block);
                    int prev = 1;
                    while (mask != 0)
                    {
                        int k = BitOperations.TrailingZeroCount(mask);
                        mask &= mask - 1;
                        int run = k - prev;
                        prev = k + 1;
                        int v = block[Unsafe.Add(ref order, k)];

                        // Every 16 zeros get a run-length-16 code of their own.
                        while (run > 15)
                        {
                            size = Unsafe.Add(ref acSizeT, 0xF0);
                            if (size == 0)
                                m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_HUFF_MISSING_CODE);
                            Append(Unsafe.Add(ref acCodeT, 0xF0), size, ref bitBuf, ref bitCnt, ref spos, ref dst);
                            run -= 16;
                        }

                        int av = v < 0 ? -v : v;
                        nbits = 32 - BitOperations.LeadingZeroCount((uint)av);
                        if (nbits > MAX_COEF_BITS)
                            m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_BAD_DCT_COEF);

                        int sym = (run << 4) | nbits;
                        size = Unsafe.Add(ref acSizeT, sym);
                        if (size == 0)
                            m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_HUFF_MISSING_CODE);

                        value = v + (v >> 31);
                        Append(((ulong)Unsafe.Add(ref acCodeT, sym) << nbits) | (uint)(value & ((1 << nbits) - 1)),
                               size + nbits, ref bitBuf, ref bitCnt, ref spos, ref dst);
                    }

                    // Zeroes at the end of the block are coded with a single end-of-block symbol.
                    if (prev <= 63)
                    {
                        size = Unsafe.Add(ref acSizeT, 0);
                        if (size == 0)
                            m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_HUFF_MISSING_CODE);
                        Append(Unsafe.Add(ref acCodeT, 0), size, ref bitBuf, ref bitCnt, ref spos, ref dst);
                    }
                }

                // Store the buffer once more so the bytes past the last complete one are
                // materialized, then drop the whole bytes: only the partial byte stays in
                // the buffer, so the next MCU starts with fewer than eight bits pending.
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, spos), BinaryPrimitives.ReverseEndianness(bitBuf));
                int whole = bitCnt >> 3;
                spos += whole;
                bitBuf <<= whole * 8;
                bitCnt -= whole * 8;

                m_bitBuf = bitBuf;
                m_bitCnt = bitCnt;

                WriteOut(spos);
            }

            /// <summary>
            /// Appends `size` bits of a code (right-justified) to the bit buffer and
            /// writes the eight bytes the buffer covers.  Bytes past the last complete
            /// one are garbage that the next call overwrites, so no branch on the number
            /// of pending bits is needed; `size` is at most 28, which keeps the buffer
            /// below 36 bits and the store inside the scratch buffer.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void Append(ulong value, int size, ref ulong bitBuf, ref int bitCnt, ref int spos, ref byte dst)
            {
                bitBuf |= value << (64 - bitCnt - size);
                bitCnt += size;
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref dst, spos), BinaryPrimitives.ReverseEndianness(bitBuf));
                int whole = bitCnt >> 3;
                spos += whole;
                bitBuf <<= whole * 8;
                bitCnt -= whole * 8;
            }

            /// <summary>flush_bits: fills the last partial byte with one bits and writes it out.</summary>
            public void Finish()
            {
                byte[] scratch = m_scratch;
                ulong bitBuf = m_bitBuf;
                int bitCnt = m_bitCnt;

                bitBuf |= 0x7FUL << (64 - bitCnt - 7);
                bitCnt += 7;
                Unsafe.WriteUnaligned(ref scratch[0], BinaryPrimitives.ReverseEndianness(bitBuf));
                int spos = bitCnt >> 3;

                // Only whole bytes reach the output; the rest is dropped.
                m_bitBuf = 0;
                m_bitCnt = 0;
                WriteOut(spos);
            }

            /// <summary>
            /// Bit mask of the nonzero coefficients, in zigzag order: bit k is set when
            /// the k-th coefficient of the zigzag sequence is nonzero.  One vector
            /// compare per row of eight coefficients finds the nonzero ones in natural
            /// order, and the row tables above turn that into zigzag bits.  The DC
            /// coefficient is not part of the AC scan and is masked off.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static ulong NonzeroMask(short[] block)
            {
                ref short r = ref MemoryMarshal.GetArrayDataReference(block);
                ref ulong t = ref MemoryMarshal.GetArrayDataReference(s_zigzagBits);
                Vector128<short> z = Vector128<short>.Zero;

                int m0 = (int)~Vector128.Equals(Vector128.LoadUnsafe(ref r, 0), z).ExtractMostSignificantBits() & 0xFF;
                int m1 = (int)~Vector128.Equals(Vector128.LoadUnsafe(ref r, 8), z).ExtractMostSignificantBits() & 0xFF;
                int m2 = (int)~Vector128.Equals(Vector128.LoadUnsafe(ref r, 16), z).ExtractMostSignificantBits() & 0xFF;
                int m3 = (int)~Vector128.Equals(Vector128.LoadUnsafe(ref r, 24), z).ExtractMostSignificantBits() & 0xFF;
                int m4 = (int)~Vector128.Equals(Vector128.LoadUnsafe(ref r, 32), z).ExtractMostSignificantBits() & 0xFF;
                int m5 = (int)~Vector128.Equals(Vector128.LoadUnsafe(ref r, 40), z).ExtractMostSignificantBits() & 0xFF;
                int m6 = (int)~Vector128.Equals(Vector128.LoadUnsafe(ref r, 48), z).ExtractMostSignificantBits() & 0xFF;
                int m7 = (int)~Vector128.Equals(Vector128.LoadUnsafe(ref r, 56), z).ExtractMostSignificantBits() & 0xFF;

                // The DC coefficient is not part of the AC scan: clear its bit in row 0.
                return (Unsafe.Add(ref t, 0 * 256 + (m0 & 0xFE))
                        | Unsafe.Add(ref t, 1 * 256 + m1))
                       | (Unsafe.Add(ref t, 2 * 256 + m2)
                        | Unsafe.Add(ref t, 3 * 256 + m3))
                       | (Unsafe.Add(ref t, 4 * 256 + m4)
                        | Unsafe.Add(ref t, 5 * 256 + m5))
                       | (Unsafe.Add(ref t, 6 * 256 + m6)
                        | Unsafe.Add(ref t, 7 * 256 + m7));
            }

            /// <summary>
            /// Moves `count` entropy-coded bytes from the scratch buffer to the
            /// destination buffer, inserting the 0x00 that follows every 0xFF (byte
            /// stuffing).  A full buffer goes to empty_output_buffer at once, as
            /// emit_byte does.
            /// </summary>
            private void WriteOut(int count)
            {
                jpeg_destination_mgr dest = m_dest;
                byte[] buf = dest.InternalBuffer;
                int pos = dest.InternalPosition;
                int free = dest.InternalFree;
                byte[] src = m_scratch;
                int i = 0;

                while (i < count)
                {
                    // Eight bytes in, at most sixteen out.
                    if (i + 8 <= count && free >= 16)
                    {
                        ulong word = Unsafe.ReadUnaligned<ulong>(ref src[i]);
                        ulong t = ~word;
                        if (((t - 0x0101010101010101UL) & ~t & 0x8080808080808080UL) == 0)
                        {
                            Unsafe.WriteUnaligned(ref buf[pos], word);
                            i += 8;
                            pos += 8;
                            free -= 8;
                            continue;
                        }
                    }

                    byte c = src[i++];
                    buf[pos++] = c;
                    if (--free == 0)
                        EmptyBuffer(dest, ref buf, ref pos, ref free);
                    if (c == 0xFF)
                    {
                        buf[pos++] = 0;
                        if (--free == 0)
                            EmptyBuffer(dest, ref buf, ref pos, ref free);
                    }
                }

                dest.InternalPosition = pos;
                dest.InternalFree = free;
            }

            private static void EmptyBuffer(jpeg_destination_mgr dest, ref byte[] buf, ref int pos, ref int free)
            {
                dest.InternalPosition = pos;
                dest.InternalFree = 0;
                dest.empty_output_buffer();
                buf = dest.InternalBuffer;
                pos = dest.InternalPosition;
                free = dest.InternalFree;
            }
        }
    }
}
