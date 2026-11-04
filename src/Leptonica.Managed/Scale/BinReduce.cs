using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Leptonica;

/*
 *  binreduce.c: Subsampled and rank filtered 2x reductions of 1 bpp images.
 */
public sealed partial class Pix
{
    private static readonly byte[] s_subsampleTab2x = MakeSubsampleTab2x();

    /*------------------------------------------------------------------*
     *                       Subsampled reduction                       *
     *------------------------------------------------------------------*/
    // leptonica: pixReduceBinary2 (binreduce.c)
    /// <summary>Reduces a 1 bpp pix by 2x, by subsampling.</summary>
    public Pix ReduceBinary2(byte[]? intab = null)
    {
        const string func = "pixReduceBinary2";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");

        GetDimensions(out int ws, out int hs, out _);
        if (hs <= 1)
            throw Lept.Arg(func, "hs must be at least 2");
        int wpls = Wpl;
        uint[] datas = Data;
        SetPadBits(0);

        int hd = hs / 2;
        Pix pixd = CreateNoInit(ws / 2, hd, 1);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(0.5f, 0.5f);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;

        byte[] tab = intab ?? s_subsampleTab2x;
        int wplsi = Math.Min(wpls, 2 * wpld);

        LeptParallel.ForRows(hd, (long)wplsi * 6L, (idStart, idEnd) => {
            ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
            ref byte tRef = ref MemoryMarshal.GetArrayDataReference(tab);
            for (int id = idStart; id < idEnd; id++) {
                ref uint rs = ref Unsafe.Add(ref baseS, (nuint)((id << 1) * wpls));
                ref uint rd = ref Unsafe.Add(ref baseD, (nuint)(id * wpld));
                int j = 0;
                for (; j + 2 <= wplsi; j += 2) {
                    uint s0 = FoldSubsampleWord(Unsafe.Add(ref rs, (nuint)j), ref tRef);
                    uint s1 = FoldSubsampleWord(Unsafe.Add(ref rs, (nuint)(j + 1)), ref tRef);
                    Unsafe.Add(ref rd, (nuint)(j >> 1)) = (s0 << 16) | s1;
                }
                if (j < wplsi)
                    Unsafe.Add(ref rd, (nuint)(j >> 1)) = FoldSubsampleWord(Unsafe.Add(ref rs, (nuint)j), ref tRef) << 16;
            }
        });

        return pixd;
    }

    /*------------------------------------------------------------------*
     *                   Rank filtered binary reductions                *
     *------------------------------------------------------------------*/
    // leptonica: pixReduceRankBinaryCascade (binreduce.c)
    /// <summary>Performs a cascade of up to four 2x rank order reductions of a 1 bpp pix.</summary>
    public Pix ReduceRankBinaryCascade(int level1, int level2, int level3, int level4)
    {
        const string func = "pixReduceRankBinaryCascade";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs must be binary");
        if (level1 > 4 || level2 > 4 || level3 > 4 || level4 > 4)
            throw Lept.Arg(func, "levels must not exceed 4");

        if (level1 <= 0) {
            Log.Warning(func, "no reduction because level1 not > 0");
            return Copy(null, this);
        }

        byte[] tab = s_subsampleTab2x;

        Pix pix1 = ReduceRankBinary2(level1, tab);
        if (level2 <= 0)
            return pix1;

        Pix pix2 = pix1.ReduceRankBinary2(level2, tab);
        if (level3 <= 0)
            return pix2;

        Pix pix3 = pix2.ReduceRankBinary2(level3, tab);
        if (level4 <= 0)
            return pix3;

        return pix3.ReduceRankBinary2(level4, tab);
    }

    // leptonica: pixReduceRankBinary2 (binreduce.c)
    /// <summary>Reduces a 1 bpp pix by 2x, with a rank threshold.</summary>
    public Pix ReduceRankBinary2(int level, byte[]? intab = null)
    {
        const string func = "pixReduceRankBinary2";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs not binary");
        if (level < 1 || level > 4)
            throw Lept.Arg(func, "level must be in set {1,2,3,4}");

        GetDimensions(out int ws, out int hs, out _);
        if (hs <= 1)
            throw Lept.Arg(func, "hs must be at least 2");
        int wpls = Wpl;
        uint[] datas = Data;
        SetPadBits(0);

        int hd = hs / 2;
        Pix pixd = CreateNoInit(ws / 2, hd, 1);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(0.5f, 0.5f);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;

        byte[] tab = intab ?? s_subsampleTab2x;
        int wplsi = Math.Min(wpls, 2 * wpld);

        LeptParallel.ForRows(hd, (long)wplsi * 8L, (idStart, idEnd) => {
            ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
            ref byte tRef = ref MemoryMarshal.GetArrayDataReference(tab);

            for (int id = idStart; id < idEnd; id++) {
                ref uint rs0 = ref Unsafe.Add(ref baseS, (nuint)((id << 1) * wpls));
                ref uint rs1 = ref Unsafe.Add(ref rs0, (nuint)wpls);
                ref uint rd = ref Unsafe.Add(ref baseD, (nuint)(id * wpld));
                int j = 0;

                switch (level) {
                case 1:
                    for (; j + 2 <= wplsi; j += 2) {
                        uint s0 = FoldRank1(Unsafe.Add(ref rs0, (nuint)j), Unsafe.Add(ref rs1, (nuint)j), ref tRef);
                        uint s1 = FoldRank1(Unsafe.Add(ref rs0, (nuint)(j + 1)), Unsafe.Add(ref rs1, (nuint)(j + 1)), ref tRef);
                        Unsafe.Add(ref rd, (nuint)(j >> 1)) = (s0 << 16) | s1;
                    }
                    if (j < wplsi)
                        Unsafe.Add(ref rd, (nuint)(j >> 1)) = FoldRank1(Unsafe.Add(ref rs0, (nuint)j), Unsafe.Add(ref rs1, (nuint)j), ref tRef) << 16;
                    break;
                case 2:
                    for (; j + 2 <= wplsi; j += 2) {
                        uint s0 = FoldRank2(Unsafe.Add(ref rs0, (nuint)j), Unsafe.Add(ref rs1, (nuint)j), ref tRef);
                        uint s1 = FoldRank2(Unsafe.Add(ref rs0, (nuint)(j + 1)), Unsafe.Add(ref rs1, (nuint)(j + 1)), ref tRef);
                        Unsafe.Add(ref rd, (nuint)(j >> 1)) = (s0 << 16) | s1;
                    }
                    if (j < wplsi)
                        Unsafe.Add(ref rd, (nuint)(j >> 1)) = FoldRank2(Unsafe.Add(ref rs0, (nuint)j), Unsafe.Add(ref rs1, (nuint)j), ref tRef) << 16;
                    break;
                case 3:
                    for (; j + 2 <= wplsi; j += 2) {
                        uint s0 = FoldRank3(Unsafe.Add(ref rs0, (nuint)j), Unsafe.Add(ref rs1, (nuint)j), ref tRef);
                        uint s1 = FoldRank3(Unsafe.Add(ref rs0, (nuint)(j + 1)), Unsafe.Add(ref rs1, (nuint)(j + 1)), ref tRef);
                        Unsafe.Add(ref rd, (nuint)(j >> 1)) = (s0 << 16) | s1;
                    }
                    if (j < wplsi)
                        Unsafe.Add(ref rd, (nuint)(j >> 1)) = FoldRank3(Unsafe.Add(ref rs0, (nuint)j), Unsafe.Add(ref rs1, (nuint)j), ref tRef) << 16;
                    break;
                default:
                    for (; j + 2 <= wplsi; j += 2) {
                        uint s0 = FoldRank4(Unsafe.Add(ref rs0, (nuint)j), Unsafe.Add(ref rs1, (nuint)j), ref tRef);
                        uint s1 = FoldRank4(Unsafe.Add(ref rs0, (nuint)(j + 1)), Unsafe.Add(ref rs1, (nuint)(j + 1)), ref tRef);
                        Unsafe.Add(ref rd, (nuint)(j >> 1)) = (s0 << 16) | s1;
                    }
                    if (j < wplsi)
                        Unsafe.Add(ref rd, (nuint)(j >> 1)) = FoldRank4(Unsafe.Add(ref rs0, (nuint)j), Unsafe.Add(ref rs1, (nuint)j), ref tRef) << 16;
                    break;
                }
            }
        });

        return pixd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FoldSubsampleWord(uint word, ref byte tRef)
    {
        word &= 0xaaaaaaaau;
        word |= word << 7;
        return ((uint)Unsafe.Add(ref tRef, (nuint)(word >> 24)) << 8)
             | Unsafe.Add(ref tRef, (nuint)((word >> 8) & 0xff));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FoldRank1(uint w1, uint w2, ref byte tRef)
    {
        uint w = w1 | w2;
        return FoldSubsampleWord(w | (w << 1), ref tRef);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FoldRank2(uint w1, uint w2, ref byte tRef)
    {
        uint w3 = w1 & w2;
        w3 |= w3 << 1;
        uint w4 = w1 | w2;
        w4 &= w4 << 1;
        return FoldSubsampleWord(w3 | w4, ref tRef);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FoldRank3(uint w1, uint w2, ref byte tRef)
    {
        uint w3 = w1 & w2;
        w3 |= w3 << 1;
        uint w4 = w1 | w2;
        w4 &= w4 << 1;
        return FoldSubsampleWord(w3 & w4, ref tRef);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FoldRank4(uint w1, uint w2, ref byte tRef)
    {
        uint w = w1 & w2;
        return FoldSubsampleWord(w & (w << 1), ref tRef);
    }

    // leptonica: makeSubsampleTab2x (binreduce.c)
    /// <summary>Makes the permutation table for 2x rank binary reduction.</summary>
    public static byte[] MakeSubsampleTab2x()
    {
        byte[] tab = new byte[256];
        for (int i = 0; i < 256; i++) {
            tab[i] = (byte)(((i & 0x01)     ) |    /* 7 */
                            ((i & 0x04) >> 1) |    /* 6 */
                            ((i & 0x10) >> 2) |    /* 5 */
                            ((i & 0x40) >> 3) |    /* 4 */
                            ((i & 0x02) << 3) |    /* 3 */
                            ((i & 0x08) << 2) |    /* 2 */
                            ((i & 0x20) << 1) |    /* 1 */
                            ((i & 0x80)     ));    /* 0 */
        }
        return tab;
    }
}
