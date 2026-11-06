#nullable disable
using System;
using System.Runtime.CompilerServices;

namespace BitMiracle.LibJpeg.Classic.Internal
{
    partial class huff_entropy_decoder
    {
        /// <summary>
        /// Turns the fast baseline decoder off on the current thread, so that tests and
        /// benchmarks can compare it with decode_mcu_full.
        /// </summary>
        [ThreadStatic]
        internal static bool DisableFastDecoder;

        private static bool UseFastDecoder => !DisableFastDecoder;

        private FastBaselineDecoder m_fast;

        private bool decode_mcu_fast(JBLOCK[] MCU_data)
        {
            /* Process restart marker if needed; our sources never suspend */
            if (m_cinfo.m_restart_interval != 0 && m_restarts_to_go == 0 && !process_restart())
                return false;

            /* If we've run out of data, just leave the MCU set to zeroes. */
            if (!m_insufficient_data)
                m_fast.DecodeMcu(MCU_data);

            m_restarts_to_go--;
            return true;
        }

        /// <summary>
        /// Baseline sequential Huffman decoding of full 8x8 blocks (decode_mcu_full with
        /// HUFF_DECODE, jpeg_huff_decode and jpeg_fill_bit_buffer), bit-exact with them,
        /// including warnings, zero padding and the byte at which a marker is found on
        /// damaged data.
        /// </summary>
        /// <remarks>
        /// The bit buffer is a 64-bit word holding the next bits left-justified, so a symbol is
        /// resolved by one table lookup and one shift.  Each 9-bit lookahead entry carries the
        /// code length, the symbol, and for AC codes the run and the size, so a whole (run, size)
        /// pair needs a single lookup; sign extension is branchless.
        /// <para>
        /// The valid bit count is kept exactly as decode_mcu_full keeps bits_left, so the
        /// decoder refills at the same points.  A refill reads one eight-byte word when none of
        /// its bytes is 0xFF: the bytes the reference would have taken become valid bits, the
        /// rest stay in the word as a read-ahead that later refills hand over without touching
        /// the source.  Those bytes hold no stuffing and no marker, so the marker reader would
        /// have skipped them as discarded data; <see cref="UnusedBytes"/> counts them in, and
        /// the discarded byte count and the source position after the scan stay the same.
        /// Stuffed bytes, markers and the end of the source buffer go byte by byte.
        /// </para>
        /// </remarks>
        private sealed class FastBaselineDecoder
        {
            /// <summary>Lookahead of the main table; codes of up to nine bits resolve in one step.</summary>
            private const int LOOKAHEAD = 9;

            /// <summary>
            /// decode_mcu_full refills when fewer than HUFF_LOOKAHEAD (8) bits are buffered and
            /// then resolves codes with an eight bit lookahead, so with exactly eight bits left
            /// an eight bit table must serve.
            /// </summary>
            private const int LOOKAHEAD_NARROW = JpegConstants.HUFF_LOOKAHEAD;

            private const int SHIFT = 64 - LOOKAHEAD;

            /// <summary>Codes longer than the lookahead, or missing from an incomplete table.</summary>
            private const int SLOW = int.MinValue;

            /// <summary>Derived decoding table, the fast counterpart of d_derived_tbl.</summary>
            private sealed class Derived
            {
                /// <summary>
                /// Lookahead entries: bits 0..7 the symbol, 8..12 the code length; for AC tables
                /// the run in bits 13..16 and the value size in bits 17..21.  SLOW otherwise.
                /// </summary>
                public readonly int[] look = CreateLookTable(LOOKAHEAD);
                public readonly int[] lookNarrow = CreateLookTable(LOOKAHEAD_NARROW);
                public readonly int[] maxcode = new int[18];

                /// <summary>Length search thresholds for <see cref="SlowSymbol"/>.</summary>
                public readonly int[] thr = new int[18];
                public readonly int[] valoffset = new int[17];
                public byte[] huffval;
            }

            private readonly huff_entropy_decoder m_owner;
            private readonly jpeg_decompress_struct m_cinfo;
            private readonly jpeg_source_mgr m_src;
            private readonly int m_blocks;
            private readonly Derived[] m_blkDc;
            private readonly Derived[] m_blkAc;
            private readonly int[] m_blkLimit;
            private readonly int[] m_blkComp;

            /// <summary>The next bits, left-justified; the top <see cref="m_bitsLeft"/> bits are valid.</summary>
            private ulong m_bitBuf;

            /// <summary>Valid bits, counted exactly as decode_mcu_full counts bits_left.</summary>
            private int m_bitsLeft;

            /// <summary>
            /// Read-ahead bits below the valid ones, a whole number of bytes from a run with no
            /// 0xFF in it.
            /// </summary>
            private int m_extra;

            public FastBaselineDecoder(huff_entropy_decoder owner)
            {
                m_owner = owner;
                m_cinfo = owner.m_cinfo;
                m_src = m_cinfo.m_src;
                m_blocks = m_cinfo.m_blocks_in_MCU;

                // Blocks of one component share one derived table.
                m_blkDc = new Derived[m_blocks];
                m_blkAc = new Derived[m_blocks];
                m_blkLimit = new int[m_blocks];
                m_blkComp = new int[m_blocks];
                var dc = new Derived[JpegConstants.MAX_COMPS_IN_SCAN];
                var ac = new Derived[JpegConstants.MAX_COMPS_IN_SCAN];
                for (int b = 0; b < m_blocks; b++)
                {
                    int ci = m_cinfo.m_MCU_membership[b];
                    m_blkDc[b] = dc[ci] ??= MakeDerived(owner.m_dc_cur_tbls[b].pub, false);
                    m_blkAc[b] = ac[ci] ??= MakeDerived(owner.m_ac_cur_tbls[b].pub, true);
                    m_blkLimit[b] = owner.coef_limit[b];
                    m_blkComp[b] = ci;
                }
            }

            /// <summary>Unused whole bytes, the read-ahead included: what finish_pass_huff hands to next_marker.</summary>
            public int UnusedBytes => (m_bitsLeft + m_extra) >> 3;

            public void DiscardBits()
            {
                m_bitBuf = 0;
                m_bitsLeft = 0;
                m_extra = 0;
            }

            public void DecodeMcu(JBLOCK[] MCU_data)
            {
                // The bit buffer lives in locals for the whole MCU and is written back only
                // around the calls that may read from the source.
                ulong buf = m_bitBuf;
                int left = m_bitsLeft;

                int blocks = m_blocks;
                int[] membership = m_blkComp;
                int[] coefLimit = m_blkLimit;
                int[] lastDcVal = m_owner.m_saved.last_dc_val;
                int[] naturalOrder = JpegUtils.jpeg_natural_order;
                Derived[] dcTables = m_blkDc;
                Derived[] acTables = m_blkAc;

                for (int blkn = 0; blkn < blocks; blkn++)
                {
                    Derived dct = dcTables[blkn];
                    Derived act = acTables[blkn];
                    int limit = coefLimit[blkn];
                    int[] dcLook = dct.look, dcNarrow = dct.lookNarrow;
                    int[] acLook = act.look, acNarrow = act.lookNarrow;

                    /* Section F.2.2.1: decode the DC coefficient difference */
                    if (left < LOOKAHEAD_NARROW)
                    {
                        m_bitBuf = buf;
                        m_bitsLeft = left;
                        Fill(0);
                        buf = m_bitBuf;
                        left = m_bitsLeft;
                    }

                    int s;
                    int entry = left >= LOOKAHEAD
                        ? dcLook[(int)(buf >> SHIFT)]
                        : left >= LOOKAHEAD_NARROW
                            ? dcNarrow[(int)(buf >> (64 - LOOKAHEAD_NARROW))]
                            : -1;
                    if (entry >= 0)
                    {
                        int n = (entry >> 8) & 0x1F;
                        left -= n;
                        buf <<= n;
                        s = entry & 0xFF;
                    }
                    else
                    {
                        m_bitBuf = buf;
                        m_bitsLeft = left;
                        s = SlowSymbol(dct, left >= LOOKAHEAD_NARROW ? LOOKAHEAD_NARROW + 1 : 1);
                        buf = m_bitBuf;
                        left = m_bitsLeft;
                    }

                    int k = 1;

                    if (limit != 0)
                    {
                        if (s != 0)
                        {
                            if (left < s)
                            {
                                m_bitBuf = buf;
                                m_bitsLeft = left;
                                Fill(s);
                                buf = m_bitBuf;
                                left = m_bitsLeft;
                            }
                            int r = (int)(buf >> (64 - s));
                            left -= s;
                            buf <<= s;
                            s = Extend(r, s);
                        }
                        int ci = membership[blkn];
                        s += lastDcVal[ci];
                        lastDcVal[ci] = s;
                        Span<short> block = MCU_data[blkn].data;
                        block[0] = (short)s;

                        /* Section F.2.2.2: one lookup per (run, size) pair; store below the limit */
                        while (k < limit)
                        {
                            if (left < LOOKAHEAD_NARROW)
                            {
                                m_bitBuf = buf;
                                m_bitsLeft = left;
                                Fill(0);
                                buf = m_bitBuf;
                                left = m_bitsLeft;
                            }

                            entry = left >= LOOKAHEAD
                                ? acLook[(int)(buf >> SHIFT)]
                                : left >= LOOKAHEAD_NARROW
                                    ? acNarrow[(int)(buf >> (64 - LOOKAHEAD_NARROW))]
                                    : -1;
                            int size, run;
                            if (entry >= 0)
                            {
                                int n = (entry >> 8) & 0x1F;
                                left -= n;
                                buf <<= n;
                                size = (entry >> 17) & 0x1F;
                                run = (entry >> 13) & 0xF;
                            }
                            else
                            {
                                m_bitBuf = buf;
                                m_bitsLeft = left;
                                int sym = SlowSymbol(act, left >= LOOKAHEAD_NARROW ? LOOKAHEAD_NARROW + 1 : 1);
                                buf = m_bitBuf;
                                left = m_bitsLeft;
                                run = sym >> 4;
                                size = sym & 15;
                            }

                            if (size == 0)
                            {
                                if (run != 15)
                                    goto nextBlock;
                                k += 16;
                                continue;
                            }
                            k += run;
                            if (left < size)
                            {
                                m_bitBuf = buf;
                                m_bitsLeft = left;
                                Fill(size);
                                buf = m_bitBuf;
                                left = m_bitsLeft;
                            }
                            int val = (int)(buf >> (64 - size));
                            left -= size;
                            buf <<= size;
                            /* the extra entries of jpeg_natural_order save us if k >= 64 */
                            block[naturalOrder[k]] = (short)Extend(val, size);
                            k++;
                        }
                    }
                    else if (s != 0)
                    {
                        /* The block is not needed: only the DC value bits are skipped */
                        if (left < s)
                        {
                            m_bitBuf = buf;
                            m_bitsLeft = left;
                            Fill(s);
                            buf = m_bitBuf;
                            left = m_bitsLeft;
                        }
                        left -= s;
                        buf <<= s;
                    }

                    /* Coefficients past the limit: the values are discarded, the codes are not */
                    while (k < JpegConstants.DCTSIZE2)
                    {
                        if (left < LOOKAHEAD_NARROW)
                        {
                            m_bitBuf = buf;
                            m_bitsLeft = left;
                            Fill(0);
                            buf = m_bitBuf;
                            left = m_bitsLeft;
                        }

                        entry = left >= LOOKAHEAD
                            ? acLook[(int)(buf >> SHIFT)]
                            : left >= LOOKAHEAD_NARROW
                                ? acNarrow[(int)(buf >> (64 - LOOKAHEAD_NARROW))]
                                : -1;
                        int size, run;
                        if (entry >= 0)
                        {
                            int n = (entry >> 8) & 0x1F;
                            left -= n;
                            buf <<= n;
                            size = (entry >> 17) & 0x1F;
                            run = (entry >> 13) & 0xF;
                        }
                        else
                        {
                            m_bitBuf = buf;
                            m_bitsLeft = left;
                            int sym = SlowSymbol(act, left >= LOOKAHEAD_NARROW ? LOOKAHEAD_NARROW + 1 : 1);
                            buf = m_bitBuf;
                            left = m_bitsLeft;
                            run = sym >> 4;
                            size = sym & 15;
                        }

                        if (size == 0)
                        {
                            if (run != 15)
                                break;
                            k += 16;
                            continue;
                        }
                        k += run;
                        if (left < size)
                        {
                            m_bitBuf = buf;
                            m_bitsLeft = left;
                            Fill(size);
                            buf = m_bitBuf;
                            left = m_bitsLeft;
                        }
                        left -= size;
                        buf <<= size;
                        k++;
                    }

                nextBlock:
                    m_bitBuf = buf;
                    m_bitsLeft = left;
                }
            }

            /// <summary>HUFF_EXTEND without a branch: the value's top bit becomes a 0/-1 mask.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int Extend(int v, int size)
            {
                int mask = (v >> (size - 1)) - 1;
                return v - (mask & ((1 << size) - 1));
            }

            /// <summary>jpeg_huff_decode: codes longer than the lookahead (Figure F.16).</summary>
            private int SlowSymbol(Derived tbl, int minBits)
            {
                int l = minBits;
                if (m_bitsLeft < l)
                    Fill(l);
                ulong buf = m_bitBuf;
                int left = m_bitsLeft;

                // The thresholds are sorted, so the number of them the next sixteen bits pass
                // is the length of the code: independent compares instead of a branch per bit.
                int look = (int)(buf >> 48);
                int[] thr = tbl.thr;
                int len = minBits
                    + (look > thr[9] ? 1 : 0) + (look > thr[10] ? 1 : 0)
                    + (look > thr[11] ? 1 : 0) + (look > thr[12] ? 1 : 0)
                    + (look > thr[13] ? 1 : 0) + (look > thr[14] ? 1 : 0)
                    + (look > thr[15] ? 1 : 0) + (look > thr[16] ? 1 : 0);
                if (minBits != LOOKAHEAD_NARROW + 1)
                {
                    // Entered with an almost empty buffer, so the code may be a short one.
                    len += 1 - minBits
                        + (look > thr[1] ? 1 : 0) + (look > thr[2] ? 1 : 0)
                        + (look > thr[3] ? 1 : 0) + (look > thr[4] ? 1 : 0)
                        + (look > thr[5] ? 1 : 0) + (look > thr[6] ? 1 : 0)
                        + (look > thr[7] ? 1 : 0) + (look > thr[8] ? 1 : 0);
                }
                if (len <= 16)
                {
                    int after = left - len;
                    if (after >= 0)
                    {
                        // Every bit of the code was buffered, so the reference's checks could
                        // not have refilled and the state after it is the same.
                        m_bitBuf = buf << len;
                        m_bitsLeft = after;
                        return tbl.huffval[(look >> (16 - len)) + tbl.valoffset[len]];
                    }

                    // The reference refills once, where the count reaches zero, and a refill
                    // from zero is four whole bytes; with that much read ahead it is two
                    // counter updates.
                    int ahead = m_extra;
                    if (ahead >= 32)
                    {
                        m_extra = ahead - 32;
                        m_bitBuf = buf << len;
                        m_bitsLeft = after + 32;
                        return tbl.huffval[(look >> (16 - len)) + tbl.valoffset[len]];
                    }
                }

                left -= l;
                int code = (int)(buf >> (64 - l));
                buf <<= l;

                int[] maxcode = tbl.maxcode;
                while (code > maxcode[l])
                {
                    code <<= 1;
                    if (left < 1)
                    {
                        m_bitBuf = buf;
                        m_bitsLeft = left;
                        Fill(1);
                        buf = m_bitBuf;
                        left = m_bitsLeft;
                    }
                    left -= 1;
                    code |= (int)(buf >> 63);
                    buf <<= 1;
                    l++;
                }

                m_bitBuf = buf;
                m_bitsLeft = left;

                /* With garbage input we may reach the sentinel value l = 17. */
                if (l > 16)
                {
                    m_cinfo.WARNMS(J_MESSAGE_CODE.JWRN_HUFF_BAD_CODE);
                    return 0;
                }
                return tbl.huffval[code + tbl.valoffset[l]];
            }

            /// <summary>
            /// jpeg_fill_bit_buffer: adds whole bytes until MIN_GET_BITS bits are valid, from the
            /// read-ahead when it covers them; zero-fills after a marker if fewer than
            /// <paramref name="nbits"/> remain, with one JWRN_HIT_MARKER per data segment.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void Fill(int nbits)
            {
                int left = m_bitsLeft;
                if (left < 0)
                    left = 0;

                // What the reference's loop would add: whole bytes until MIN_GET_BITS.
                int take = (32 - left) & ~7;
                int extra = m_extra;
                if (take <= extra)
                {
                    m_bitsLeft = left + take;
                    m_extra = extra - take;
                    return;
                }

                ulong buf = m_bitBuf;
                left += extra;      // the bytes read ahead come first in the stream
                extra = 0;
                bool noMoreBytes = false;
                if (m_cinfo.m_unread_marker == 0)
                {
                    jpeg_source_mgr src = m_src;

                    // One eight-byte load when none of those bytes is 0xFF: no stuffing and no
                    // marker in them, so the reference would have read the same bytes one at a
                    // time.  The first bring the valid bits up to MIN_GET_BITS, the rest become
                    // the new read-ahead.
                    if (src.TryPeek8(out ulong word))
                    {
                        ulong inverted = ~word;
                        if (((inverted - 0x0101010101010101UL) & ~inverted & 0x8080808080808080UL) == 0)
                        {
                            int take8 = (32 - left) & ~7;
                            int bits = take8 + ((64 - left - take8) & ~7);
                            buf |= (word >> (64 - bits)) << (64 - bits - left);
                            left += take8;
                            m_bitBuf = buf;
                            m_bitsLeft = left;
                            m_extra = bits - take8;
                            src.Consume(bits >> 3);
                            return;
                        }
                    }

                    while (left < MIN_GET_BITS)
                    {
                        src.GetByte(out int c);

                        /* If it's 0xFF, check and discard stuffed zero byte */
                        if (c == 0xFF)
                        {
                            /* FF..FF 00 is a single FF data byte; anything else after the run is a marker */
                            do
                            {
                                src.GetByte(out c);
                            }
                            while (c == 0xFF);

                            if (c == 0)
                            {
                                c = 0xFF;
                            }
                            else
                            {
                                m_cinfo.m_unread_marker = c;
                                noMoreBytes = true;
                                break;
                            }
                        }
                        buf |= (ulong)c << (56 - left);
                        left += 8;
                    }
                }
                else
                {
                    /* cannot advance past a marker */
                    noMoreBytes = true;
                }

                if (noMoreBytes && nbits > left)
                {
                    /* Report corrupted data once per segment and stuff zeroes into the stream */
                    if (!m_owner.m_insufficient_data)
                    {
                        m_cinfo.WARNMS(J_MESSAGE_CODE.JWRN_HIT_MARKER);
                        m_owner.m_insufficient_data = true;
                    }
                    // The valid bits already sit at the top of the word, so the zero bits are
                    // there too: only the count changes.
                    left = MIN_GET_BITS;
                }

                m_bitBuf = buf;
                m_bitsLeft = left;
                m_extra = extra;
            }

            private static int[] CreateLookTable(int bits)
            {
                var t = new int[1 << bits];
                Array.Fill(t, SLOW);
                return t;
            }

            private static int MakeEntry(int len, int sym, bool isAC)
            {
                int run = isAC ? sym >> 4 : 0;
                int size = isAC ? sym & 15 : sym;
                return sym | (len << 8) | (run << 13) | (size << 17);
            }

            /// <summary>
            /// The fast tables of a Huffman table that jpeg_make_d_derived_tbl has already
            /// validated for this scan.
            /// </summary>
            private static Derived MakeDerived(JHUFF_TBL htbl, bool isAC)
            {
                byte[] bitsTbl = htbl.Bits;
                byte[] values = htbl.Huffval;
                var dtbl = new Derived { huffval = values };

                /* Figure C.1: make table of Huffman code length for each symbol */
                int p = 0;
                var huffsize = new int[257];
                for (int l = 1; l <= 16; l++)
                {
                    int i = bitsTbl[l];
                    while (i-- != 0)
                        huffsize[p++] = l;
                }
                huffsize[p] = 0;

                /* Figure C.2: generate the codes themselves */
                int code = 0;
                int si = huffsize[0];
                var huffcode = new int[257];
                p = 0;
                while (huffsize[p] != 0)
                {
                    while (huffsize[p] == si)
                    {
                        huffcode[p++] = code;
                        code++;
                    }
                    code <<= 1;
                    si++;
                }

                /* Figure F.15: generate decoding tables for bit-sequential decoding */
                p = 0;
                for (int l = 1; l <= 16; l++)
                {
                    if (bitsTbl[l] != 0)
                    {
                        dtbl.valoffset[l] = p - huffcode[p];
                        p += bitsTbl[l];
                        dtbl.maxcode[l] = huffcode[p - 1];
                    }
                    else
                    {
                        dtbl.maxcode[l] = -1;
                    }
                }
                dtbl.maxcode[17] = 0xFFFFF;

                // thr[l] is the largest sixteen-bit window whose first l bits are still within
                // the codes of length l.  Canonical codes make the sequence sorted, and a length
                // with no codes repeats the one before it.
                int prev = -1;
                for (int l = 1; l <= 16; l++)
                {
                    int t = dtbl.maxcode[l] < 0 ? prev : (int)(((uint)dtbl.maxcode[l] + 1) << (16 - l)) - 1;
                    dtbl.thr[l] = t < prev ? prev : t;
                    prev = dtbl.thr[l];
                }

                /* Compute lookahead tables to speed up decoding */
                p = 0;
                for (int l = 1; l <= LOOKAHEAD; l++)
                {
                    for (int i = 1; i <= bitsTbl[l]; i++, p++)
                    {
                        int entry = MakeEntry(l, values[p], isAC);
                        int lookbits = huffcode[p] << (LOOKAHEAD - l);
                        for (int ctr = 1 << (LOOKAHEAD - l); ctr > 0; ctr--, lookbits++)
                            dtbl.look[lookbits] = entry;
                        if (l <= LOOKAHEAD_NARROW)
                        {
                            lookbits = huffcode[p] << (LOOKAHEAD_NARROW - l);
                            for (int ctr = 1 << (LOOKAHEAD_NARROW - l); ctr > 0; ctr--, lookbits++)
                                dtbl.lookNarrow[lookbits] = entry;
                        }
                    }
                }
                return dtbl;
            }
        }
    }
}
