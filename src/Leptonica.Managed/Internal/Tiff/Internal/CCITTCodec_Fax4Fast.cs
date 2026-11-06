#nullable disable
/*
 * Fast CCITT Group 4 decoding: the same results as Fax4Decode with EXPAND2D and
 * fax3FillRuns, including messages, zero padding at the end of a strip and the row at which
 * damaged data stops decoding, with a 64-bit bit buffer, packed state tables, the decoding
 * state in locals and a row fill that clears the row and draws only the black runs.
 *
 * Decoder support is derived, with permission, from the code in Frank Cringle's viewfax
 * program; Copyright (C) 1990, 1995 Frank D. Cringle.
 */
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace BitMiracle.LibTiff.Classic.Internal
{
    partial class CCITTCodec
    {
        /// <summary>
        /// Turns the fast Group 4 decoder off on the current thread, so that tests and
        /// benchmarks can compare it with the original Fax4Decode.
        /// </summary>
        [ThreadStatic]
        internal static bool DisableFastFax4Decoder;

        /* State tables packed into one int per entry for the fast decoder */
        private static readonly int[] m_fastMainTable = PackMainTable();
        private static readonly int[] m_fastWhiteTable = PackTable(m_faxWhiteTable);
        private static readonly int[] m_fastBlackTable = PackTable(m_faxBlackTable);
        private static readonly int[] shortBlackMasks = BuildShortBlackMasks();

        /* buffered input bits of the fast decoder, least significant bit first */
        private ulong m_fastData;

        private static int[] BuildShortBlackMasks()
        {
            int[] masks = new int[8 * 16];
            for (int bit = 0; bit < 8; bit++)
                for (int length = 1; length < 16; length++)
                    masks[(bit << 4) | length] = ((1 << length) - 1) << (24 - bit - length);
            return masks;
        }

        private static int[] PackMainTable()
        {
            int[] packed = PackTable(m_faxMainTable);
            for (int i = 0; i < packed.Length; i++)
            {
                int entry = packed[i];
                if ((entry & 15) == S_VL)
                    packed[i] = (entry & 255) | (-(entry >> 8) << 8);
            }
            return packed;
        }

        private static int[] PackTable(int[] source)
        {
            int[] packed = new int[source.Length / 3];
            for (int i = 0; i < packed.Length; i++)
                packed[i] = source[i * 3] | (source[i * 3 + 1] << 4) | (source[i * 3 + 2] << 8);
            return packed;
        }

        private static void fax3FillRunsFast(byte[] buffer, int offset, int[] runs,
            int thisRunOffset, int nextRunOffset, int width)
        {
            int rowBytes = width >= 0 ? (width >> 3) + ((width & 7) != 0 ? 1 : 0) : 0;
            bool odd = ((nextRunOffset - thisRunOffset) & 1) != 0;
            if (width < 0 || thisRunOffset < 0 || nextRunOffset < thisRunOffset ||
                nextRunOffset > runs.Length || (odd && nextRunOffset == runs.Length) ||
                offset < 0 || offset > buffer.Length || rowBytes > buffer.Length - offset)
            {
                fax3FillRunsIncremental(buffer, offset, runs, thisRunOffset, nextRunOffset, width);
                return;
            }

            // A damaged row can leave pixels untouched. Check coverage before clearing, so
            // those rows and any range errors keep the incremental routine's behavior.
            int covered = 0;
            for (int i = thisRunOffset; i < nextRunOffset; i++)
            {
                int run = runs[i];
                if ((uint)covered + (uint)run > (uint)width || (uint)run > (uint)width)
                    run = width - covered;
                covered += run;
                if (covered < 0 || covered > width)
                {
                    fax3FillRunsIncremental(buffer, offset, runs, thisRunOffset, nextRunOffset, width);
                    return;
                }
            }
            if (covered != width)
            {
                fax3FillRunsIncremental(buffer, offset, runs, thisRunOffset, nextRunOffset, width);
                return;
            }

            if (odd)
                runs[nextRunOffset++] = 0;

            int padding = width & 7;
            byte savedPadding = padding == 0 ? (byte)0 :
                (byte)(buffer[offset + rowBytes - 1] & (0xff >> padding));
            buffer.AsSpan(offset, rowBytes).Clear();
            if (padding != 0)
                buffer[offset + rowBytes - 1] = savedPadding;

            int x = 0;
            for (int i = thisRunOffset; i < nextRunOffset; i += 2)
            {
                int white = runs[i];
                if ((uint)x + (uint)white > (uint)width || (uint)white > (uint)width)
                    runs[i] = white = width - x;
                x += white;

                int black = runs[i + 1];
                if ((uint)x + (uint)black > (uint)width || (uint)black > (uint)width)
                    runs[i + 1] = black = width - x;
                int start = x;
                x += black;
                if (black == 0)
                    continue;

                DrawBlackRun(buffer, offset, start, black);
            }
            Debug.Assert(x == width);
        }

        private static void fax3FillRunsIncremental(byte[] buffer, int offset, int[] runs,
            int thisRunOffset, int nextRunOffset, int width)
        {
            if (((nextRunOffset - thisRunOffset) & 1) != 0)
            {
                runs[nextRunOffset] = 0;
                nextRunOffset++;
            }

            int x = 0;
            for (; thisRunOffset < nextRunOffset; thisRunOffset += 2)
            {
                int run = runs[thisRunOffset];

                // should cast 'run' to unsigned in order to discard values bigger than int.MaxValue
                // for such value 'run' become negative and following condition is not met
                if ((uint)x + (uint)run > (uint)width || (uint)run > (uint)width)
                {
                    runs[thisRunOffset] = width - x;
                    run = runs[thisRunOffset];
                }

                if (run != 0)
                {
                    int cp = offset + (x >> 3);
                    int bx = x & 7;
                    if (run > 8 - bx)
                    {
                        if (bx != 0)
                        {
                            // align to byte boundary
                            buffer[cp] &= (byte)(0xff << (8 - bx));
                            cp++;
                            run -= 8 - bx;
                        }

                        int n = run >> 3;
                        if (n != 0)
                        {
                            // multiple bytes to fill
                            if ((n / sizeof(int)) > 1)
                            {
                                // Align to longword boundary and fill.
                                for ( ; n != 0 && !isLongAligned(cp); n--)
                                {
                                    buffer[cp] = 0x00;
                                    cp++;
                                }

                                int bytesToFill = n - (n % sizeof(int));
                                n -= bytesToFill;

                                buffer.AsSpan(cp, bytesToFill).Clear();
                                cp += bytesToFill;
                            }

                            FILL(n, buffer, ref cp, 0);
                            run &= 7;
                        }

                        if (run != 0)
                            buffer[cp] &= (byte)(0xff >> run);
                    }
                    else
                        buffer[cp] &= (byte)(~(fillMasks[run] >> bx));

                    x += runs[thisRunOffset];
                }

                run = runs[thisRunOffset + 1];

                // should cast 'run' to unsigned in order to discard values bigger than int.MaxValue
                // for such value 'run' become negative and following condition is not met
                if ((uint)x + (uint)run > (uint)width || (uint)run > (uint)width)
                {
                    runs[thisRunOffset + 1] = width - x;
                    run = runs[thisRunOffset + 1];
                }

                if (run != 0)
                {
                    int cp = offset + (x >> 3);
                    int bx = x & 7;
                    if (run > 8 - bx)
                    {
                        if (bx != 0)
                        {
                            // align to byte boundary
                            buffer[cp] |= (byte)(0xff >> bx);
                            cp++;
                            run -= 8 - bx;
                        }

                        int n = run >> 3;
                        if (n != 0)
                        {
                            // multiple bytes to fill
                            if ((n / sizeof(int)) > 1)
                            {
                                // Align to longword boundary and fill.
                                for ( ; n != 0 && !isLongAligned(cp); n--)
                                {
                                    buffer[cp] = 0xff;
                                    cp++;
                                }

                                int bytesToFill = n - (n % sizeof(int));
                                n -= bytesToFill;

                                buffer.AsSpan(cp, bytesToFill).Fill(0xff);
                                cp += bytesToFill;
                            }

                            FILL(n, buffer, ref cp, 0xff);
                            run &= 7;
                        }

                        if (run != 0)
                            buffer[cp] |= (byte)(0xff00 >> run);
                    }
                    else
                        buffer[cp] |= (byte)(fillMasks[run] >> bx);

                    x += runs[thisRunOffset + 1];
                }
            }

            Debug.Assert(x == width);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawBlackRun(byte[] row, int offset, int start, int length)
        {
            if (length == 0)
                return;
            int cp = offset + (start >> 3);
            int bit = start & 7;
            if (length < 16)
            {
                // The mask is aligned to three big-endian bytes; a short run can
                // touch at most three bytes, including an unaligned start.
                int mask = shortBlackMasks[(bit << 4) | length];
                if (bit + length <= 8)
                    row[cp] |= (byte)(mask >> 16);
                else
                {
                    Span<byte> pair = row.AsSpan(cp, 2);
                    ushort value = BinaryPrimitives.ReadUInt16BigEndian(pair);
                    BinaryPrimitives.WriteUInt16BigEndian(pair, (ushort)(value | (mask >> 8)));
                    if (bit + length > 16)
                        row[cp + 2] |= (byte)mask;
                }
                return;
            }
            if (bit != 0)
            {
                int n = Math.Min(length, 8 - bit);
                row[cp++] |= (byte)((0xff >> bit) & (0xff << (8 - bit - n)));
                length -= n;
            }
            int wholeBytes = length >> 3;
            if (wholeBytes != 0)
            {
                row.AsSpan(cp, wholeBytes).Fill(0xff);
                cp += wholeBytes;
                length &= 7;
            }
            if (length != 0)
                row[cp] |= (byte)(0xff << (8 - length));
        }

        /// <summary>
        /// Decode the requested amount of G4-encoded data.
        /// </summary>
        private bool Fax4DecodeFast(byte[] buffer, int offset, int count)
        {
            const string module = "Fax4Decode";
            int rowpixels = m_rowpixels;
            int[] runs = m_runs;
            ReadOnlySpan<int> mainTable = m_fastMainTable.AsSpan(0, 128);
            ReadOnlySpan<int> whiteTable = m_fastWhiteTable.AsSpan(0, 4096);
            ReadOnlySpan<int> blackTable = m_fastBlackTable.AsSpan(0, 8192);
            byte[] raw = m_tif.m_rawdata;
            byte[] bitmap = m_bitmap;
            int rawEnd = m_tif.m_rawcc;
            bool reverse = ReferenceEquals(bitmap, Tiff.GetBitRevTable(true));
            ulong bits = m_fastData;
            int bitCount = m_bit, rawcp = m_tif.m_rawcp;
            int curruns = m_curruns, refruns = m_refruns;
            int a0 = m_a0, runLength = 0, thisrun = 0, pa = 0, pb = 0;
            int b1 = 0, state = 0, param = 0, entry = 0, consumed = 0;
            int needed = 0, resume = 0, x = 0;
            bool black = false, second = false, failed = false, decodingDone = false;


            try
            {
            NextRow:
                if (count <= 0)
                    return true;

                a0 = 0;
                runLength = 0;
                thisrun = curruns;
                pa = curruns;
                pb = refruns;
                b1 = runs[pb++];
                failed = false;
                decodingDone = false;

            // The reference keeps processing modes after a bad code or EOL.
            NextMode:
                if (a0 >= rowpixels)
                    goto EndModes;
                if (bitCount < 7)
                {
                    needed = 7;
                    resume = 1;
                    goto Refill;
                }
            ReadMode:
                entry = mainTable[(int)(bits & 127)];
                state = entry & 15;
                consumed = (entry >> 4) & 15;
                param = entry >> 8;
                bits >>= consumed;
                bitCount -= consumed;

                switch (state)
                {
                    case S_Pass:
                        if (pa != thisrun)
                            while (b1 <= a0 && b1 < rowpixels)
                            {
                                b1 += runs[pb] + runs[pb + 1];
                                pb += 2;
                            }
                        b1 += runs[pb++];
                        runLength += b1 - a0;
                        a0 = b1;
                        b1 += runs[pb++];
                        goto NextMode;

                    case S_Horiz:
                        black = ((pa - thisrun) & 1) != 0;
                        second = false;
                        goto ReadRun;

                    case S_V0:
                    case S_VR:
                    case S_VL:
                        if (pa != thisrun)
                            while (b1 <= a0 && b1 < rowpixels)
                            {
                                b1 += runs[pb] + runs[pb + 1];
                                pb += 2;
                            }
                        // VL offsets are stored as negative values in the main table.
                        x = b1 - a0 + param;
                        runs[pa++] = runLength + x;
                        a0 += x;
                        runLength = 0;
                        if (state == S_VL)
                            b1 -= runs[--pb];
                        else
                            b1 += runs[pb++];
                        goto NextMode;

                    case S_Ext:
                        runs[pa++] = rowpixels - a0;
                        m_a0 = a0;
                        Fax3Extension(module);
                        decodingDone = true;
                        goto NextMode;

                    case S_EOL:
                        runs[pa++] = rowpixels - a0;
                        if (bitCount < 4)
                        {
                            needed = 4;
                            resume = 3;
                            goto Refill;
                        }
                        goto ReadEol;

                    default:
                        m_a0 = a0;
                        Fax3Unexpected(module);
                        decodingDone = true;
                        goto NextMode;
                }

            ReadRun:
                needed = black ? 13 : 12;
                if (bitCount < needed)
                {
                    resume = 2;
                    goto Refill;
                }
            ReadRunBits:
                entry = black ? blackTable[(int)(bits & 8191)] : whiteTable[(int)(bits & 4095)];
                state = entry & 15;
                consumed = (entry >> 4) & 15;
                param = entry >> 8;
                bits >>= consumed;
                bitCount -= consumed;
                if (state == (black ? S_TermB : S_TermW))
                {
                    runs[pa++] = runLength + param;
                    a0 += param;
                    runLength = 0;
                    if (decodingDone)
                        goto NextMode;
                    if (!second)
                    {
                        second = true;
                        black = !black;
                        goto ReadRun;
                    }
                    if (pa != thisrun)
                        while (b1 <= a0 && b1 < rowpixels)
                        {
                            b1 += runs[pb] + runs[pb + 1];
                            pb += 2;
                        }
                    goto NextMode;
                }
                if (state == (black ? S_MakeUpB : S_MakeUpW) || state == S_MakeUp)
                {
                    a0 += param;
                    runLength += param;
                    if (decodingDone)
                        goto NextMode;
                    goto ReadRun;
                }
                m_a0 = a0;
                Fax3Unexpected(module);
                decodingDone = true;
                goto NextMode;

            ReadEol:
                if ((bits & 15) != 0)
                {
                    m_a0 = a0;
                    Fax3Unexpected(module);
                }
                bits >>= 4;
                bitCount -= 4;
                m_EOLcnt = 1;
                decodingDone = true;
                goto NextMode;

            EndModes:
                if (!decodingDone && runLength != 0)
                {
                    if (runLength + a0 < rowpixels)
                    {
                        if (bitCount < 1)
                        {
                            needed = 1;
                            resume = 4;
                            goto Refill;
                        }
                        goto ReadFinalV0;
                    }
                    goto FinishRun;
                }
                goto Cleanup;

            ReadFinalV0:
                if ((bits & 1) == 0)
                {
                    m_a0 = a0;
                    Fax3Unexpected(module);
                    goto Cleanup;
                }
                bits >>= 1;
                bitCount--;
            FinishRun:
                runs[pa++] = runLength;
                runLength = 0;
                goto Cleanup;

            PrematureEof:
                m_a0 = a0;
                Fax3PrematureEOF(module);
                failed = true;

            Cleanup:
                if (runLength != 0)
                {
                    runs[pa++] = runLength;
                    runLength = 0;
                }
                if (a0 != rowpixels)
                {
                    m_a0 = a0;
                    Fax3BadLength(module);
                    while (a0 > rowpixels && pa > thisrun)
                        a0 -= runs[--pa];
                    if (a0 < rowpixels)
                    {
                        if (a0 < 0)
                            a0 = 0;
                        if (((pa - thisrun) & 1) != 0)
                            runs[pa++] = 0;
                        x = rowpixels - a0;
                        runs[pa++] = x;
                        a0 += x;
                    }
                    else if (a0 > rowpixels)
                    {
                        runs[pa++] = rowpixels;
                        a0 += rowpixels;
                        runs[pa++] = 0;
                    }
                }

                if (failed || m_EOLcnt != 0)
                {
                    if (bitCount < 13)
                    {
                        needed = 13;
                        resume = 5;
                        goto Refill;
                    }
                    goto FailedRow;
                }
                fax3FillRunsFast(buffer, offset, runs, thisrun, pa, rowpixels);
                runs[pa++] = 0; // imaginary change for reference
                x = curruns;
                curruns = refruns;
                refruns = x;
                offset += m_rowbytes;
                count -= m_rowbytes;
                m_line++;
                goto NextRow;

            FailedRow:
                bits >>= 13;
                bitCount -= 13;
                fax3FillRunsFast(buffer, offset, runs, thisrun, pa, rowpixels);
                return false;

            Refill:
                // Read eight bytes but append six, leaving room for up to 12 live bits.
                // The two overlapping bytes are read again at the next refill.
                if (rawEnd - rawcp >= 8 && bitCount >= 0)
                {
                    ulong word = BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(rawcp, 8));
                    if (reverse)
                        word = ReverseBitsInBytes(word);
                    bits |= (word & 0x0000FFFFFFFFFFFFUL) << bitCount;
                    rawcp += 6;
                    bitCount += 48;
                }
                else
                {
                    while (rawcp < rawEnd && bitCount <= 56)
                    {
                        bits |= (ulong)bitmap[raw[rawcp++]] << bitCount;
                        bitCount += 8;
                    }
                }
                if (bitCount < needed)
                {
                    if (bitCount == 0)
                    {
                        // The failure path ignores NeedBits16's return value.
                        if (resume == 5)
                            goto FailedRow;
                        goto PrematureEof;
                    }
                    bitCount = needed; // Pad only when some bits remain.
                }
                switch (resume)
                {
                    case 1: goto ReadMode;
                    case 2: goto ReadRunBits;
                    case 3: goto ReadEol;
                    case 4: goto ReadFinalV0;
                    default: goto FailedRow;
                }
            }
            finally
            {
                m_fastData = bits;
                m_bit = bitCount;
                m_tif.m_rawcp = rawcp;
                m_curruns = curruns;
                m_refruns = refruns;
                m_a0 = a0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ReverseBitsInBytes(ulong value)
        {
            value = ((value >> 1) & 0x5555555555555555UL) | ((value & 0x5555555555555555UL) << 1);
            value = ((value >> 2) & 0x3333333333333333UL) | ((value & 0x3333333333333333UL) << 2);
            return ((value >> 4) & 0x0F0F0F0F0F0F0F0FUL) | ((value & 0x0F0F0F0F0F0F0F0FUL) << 4);
        }
    }
}
