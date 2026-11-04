using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  binexpand.c: Replicated expansion (integer scaling) of 1 bpp images.
 */
public sealed partial class Pix
{
    private static readonly uint[] expandtab16 = { 0x00000000, 0x0000ffff, 0xffff0000, 0xffffffff };
    private static ushort[]? expandTab2x;
    private static uint[]? expandTab4x;
    private static uint[]? expandTab8x;

    /*------------------------------------------------------------------*
     *              Replicated expansion (integer scaling)              *
     *------------------------------------------------------------------*/
    // leptonica: pixExpandBinaryReplicate (binexpand.c)
    /// <summary>Expands a 1 bpp pix by integer factors, replicating pixels.</summary>
    public Pix ExpandBinaryReplicate(int xfact, int yfact)
    {
        const string func = "pixExpandBinaryReplicate";

        GetDimensions(out int w, out int h, out int d);
        if (d != 1)
            throw Lept.Arg(func, "pixs not binary");
        if (xfact <= 0 || yfact <= 0)
            throw Lept.Arg(func, "invalid scale factor: <= 0");

        if (xfact == yfact) {
            if (xfact == 1)
                return Copy(null, this);
            if (xfact == 2 || xfact == 4 || xfact == 8 || xfact == 16)
                return ExpandBinaryPower2(xfact);
        }

        int wpls = Wpl;
        uint[] datas = Data;
        int wd = xfact * w;
        int hd = yfact * h;
        Pix pixd = Create(wd, hd, 1);
        pixd.CopyResolution(this);
        pixd.ScaleResolution((float)xfact, (float)yfact);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;

        LeptParallel.ForRows(h, (long)w * xfact, (yStart, yEnd) => {
            ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                ref uint rs = ref Unsafe.Add(ref baseS, (nuint)(i * wpls));
                int lined = yfact * i * wpld;
                ref uint rd = ref Unsafe.Add(ref baseD, (nuint)lined);
                for (int j = 0; j < w; j++) {
                    if (GetDataBitUnchecked(ref rs, j) != 0) {
                        int start = xfact * j;
                        for (int k = 0; k < xfact; k++)
                            SetDataBitUnchecked(ref rd, start + k);
                    }
                }
                ReadOnlySpan<uint> srcRow = datad.AsSpan(lined, wpld);
                for (int k = 1; k < yfact; k++)
                    srcRow.CopyTo(datad.AsSpan(lined + k * wpld, wpld));
            }
        });

        return pixd;
    }

    /*------------------------------------------------------------------*
     *                      Power of 2 expansion                        *
     *------------------------------------------------------------------*/
    // leptonica: pixExpandBinaryPower2 (binexpand.c)
    /// <summary>Expands a 1 bpp pix by a power of 2, replicating pixels.</summary>
    public Pix ExpandBinaryPower2(int factor)
    {
        const string func = "pixExpandBinaryPower2";

        GetDimensions(out int w, out int h, out int d);
        if (d != 1)
            throw Lept.Arg(func, "pixs not binary");
        if (factor == 1)
            return Copy(null, this);
        if (factor != 2 && factor != 4 && factor != 8 && factor != 16)
            throw Lept.Arg(func, "factor must be in {2,4,8,16}");

        int wpls = Wpl;
        uint[] datas = Data;
        int wd = factor * w;
        int hd = factor * h;
        Pix pixd = Create(wd, hd, 1);
        pixd.CopyResolution(this);
        pixd.ScaleResolution((float)factor, (float)factor);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;

        if (factor == 2) {
            ushort[] tab2 = expandTab2x ??= MakeExpandTab2x();
            int sbytes = (w + 7) / 8;
            int fullSrcWords = sbytes >> 2;
            LeptParallel.ForRows(h, (long)sbytes * 4L, (yStart, yEnd) => {
                ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
                ref ushort tRef = ref MemoryMarshal.GetArrayDataReference(tab2);
                for (int i = yStart; i < yEnd; i++) {
                    ref uint rs = ref Unsafe.Add(ref baseS, (nuint)(i * wpls));
                    int lined = 2 * i * wpld;
                    ref uint rd = ref Unsafe.Add(ref baseD, (nuint)lined);
                    for (int jw = 0; jw < fullSrcWords; jw++) {
                        uint sw = Unsafe.Add(ref rs, (nuint)jw);
                        uint u0 = Unsafe.Add(ref tRef, (nuint)(sw >> 24));
                        uint u1 = Unsafe.Add(ref tRef, (nuint)((sw >> 16) & 0xff));
                        uint u2 = Unsafe.Add(ref tRef, (nuint)((sw >> 8) & 0xff));
                        uint u3 = Unsafe.Add(ref tRef, (nuint)(sw & 0xff));
                        Unsafe.Add(ref rd, (nuint)(jw << 1)) = (u0 << 16) | u1;
                        Unsafe.Add(ref rd, (nuint)((jw << 1) + 1)) = (u2 << 16) | u3;
                    }
                    for (int j = fullSrcWords << 2; j < sbytes; j++) {
                        int sval = GetDataByteUnchecked(ref rs, j);
                        SetDataTwoBytesUnchecked(ref rd, j, Unsafe.Add(ref tRef, (nuint)sval));
                    }
                    datad.AsSpan(lined, wpld).CopyTo(datad.AsSpan(lined + wpld, wpld));
                }
            });
        } else if (factor == 4) {
            uint[] tab4 = expandTab4x ??= MakeExpandTab4x();
            int sbytes = (w + 7) / 8;
            int fullSrcWords = sbytes >> 2;
            LeptParallel.ForRows(h, (long)sbytes * 8L, (yStart, yEnd) => {
                ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
                ref uint tRef = ref MemoryMarshal.GetArrayDataReference(tab4);
                for (int i = yStart; i < yEnd; i++) {
                    ref uint rs = ref Unsafe.Add(ref baseS, (nuint)(i * wpls));
                    int lined = 4 * i * wpld;
                    ref uint rd = ref Unsafe.Add(ref baseD, (nuint)lined);
                    for (int jw = 0; jw < fullSrcWords; jw++) {
                        uint sw = Unsafe.Add(ref rs, (nuint)jw);
                        int dj = jw << 2;
                        Unsafe.Add(ref rd, (nuint)dj) = Unsafe.Add(ref tRef, (nuint)(sw >> 24));
                        Unsafe.Add(ref rd, (nuint)(dj + 1)) = Unsafe.Add(ref tRef, (nuint)((sw >> 16) & 0xff));
                        Unsafe.Add(ref rd, (nuint)(dj + 2)) = Unsafe.Add(ref tRef, (nuint)((sw >> 8) & 0xff));
                        Unsafe.Add(ref rd, (nuint)(dj + 3)) = Unsafe.Add(ref tRef, (nuint)(sw & 0xff));
                    }
                    for (int j = fullSrcWords << 2; j < sbytes; j++) {
                        int sval = GetDataByteUnchecked(ref rs, j);
                        Unsafe.Add(ref rd, (nuint)j) = Unsafe.Add(ref tRef, (nuint)sval);
                    }
                    ReadOnlySpan<uint> srcRow = datad.AsSpan(lined, wpld);
                    for (int k = 1; k < 4; k++)
                        srcRow.CopyTo(datad.AsSpan(lined + k * wpld, wpld));
                }
            });
        } else if (factor == 8) {
            uint[] tab8 = expandTab8x ??= MakeExpandTab8x();
            int sqbits = (w + 3) / 4;
            int fullSrcWords = sqbits >> 3;
            LeptParallel.ForRows(h, (long)sqbits * 12L, (yStart, yEnd) => {
                ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
                ref uint tRef = ref MemoryMarshal.GetArrayDataReference(tab8);
                for (int i = yStart; i < yEnd; i++) {
                    ref uint rs = ref Unsafe.Add(ref baseS, (nuint)(i * wpls));
                    int lined = 8 * i * wpld;
                    ref uint rd = ref Unsafe.Add(ref baseD, (nuint)lined);
                    for (int jw = 0; jw < fullSrcWords; jw++) {
                        uint sw = Unsafe.Add(ref rs, (nuint)jw);
                        int dj = jw << 3;
                        Unsafe.Add(ref rd, (nuint)dj) = Unsafe.Add(ref tRef, (nuint)(sw >> 28));
                        Unsafe.Add(ref rd, (nuint)(dj + 1)) = Unsafe.Add(ref tRef, (nuint)((sw >> 24) & 0xf));
                        Unsafe.Add(ref rd, (nuint)(dj + 2)) = Unsafe.Add(ref tRef, (nuint)((sw >> 20) & 0xf));
                        Unsafe.Add(ref rd, (nuint)(dj + 3)) = Unsafe.Add(ref tRef, (nuint)((sw >> 16) & 0xf));
                        Unsafe.Add(ref rd, (nuint)(dj + 4)) = Unsafe.Add(ref tRef, (nuint)((sw >> 12) & 0xf));
                        Unsafe.Add(ref rd, (nuint)(dj + 5)) = Unsafe.Add(ref tRef, (nuint)((sw >> 8) & 0xf));
                        Unsafe.Add(ref rd, (nuint)(dj + 6)) = Unsafe.Add(ref tRef, (nuint)((sw >> 4) & 0xf));
                        Unsafe.Add(ref rd, (nuint)(dj + 7)) = Unsafe.Add(ref tRef, (nuint)(sw & 0xf));
                    }
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls);
                    for (int j = fullSrcWords << 3; j < sqbits; j++)
                        Unsafe.Add(ref rd, (nuint)j) = Unsafe.Add(ref tRef, (nuint)GetDataQbit(lines, j));
                    ReadOnlySpan<uint> srcRow = datad.AsSpan(lined, wpld);
                    for (int k = 1; k < 8; k++)
                        srcRow.CopyTo(datad.AsSpan(lined + k * wpld, wpld));
                }
            });
        } else {  /* factor == 16 */
            int sdibits = (w + 1) / 2;
            LeptParallel.ForRows(h, (long)sdibits * 20L, (yStart, yEnd) => {
                ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
                ref uint tRef = ref MemoryMarshal.GetArrayDataReference(expandtab16);
                for (int i = yStart; i < yEnd; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls);
                    int lined = 16 * i * wpld;
                    ref uint rd = ref Unsafe.Add(ref baseD, (nuint)lined);
                    for (int j = 0; j < sdibits; j++)
                        Unsafe.Add(ref rd, (nuint)j) = Unsafe.Add(ref tRef, (nuint)GetDataDibit(lines, j));
                    ReadOnlySpan<uint> srcRow = datad.AsSpan(lined, wpld);
                    for (int k = 1; k < 16; k++)
                        srcRow.CopyTo(datad.AsSpan(lined + k * wpld, wpld));
                }
            });
        }

        return pixd;
    }

    /*-------------------------------------------------------------------*
     *             Expansion tables for 2x, 4x and 8x expansion          *
     *-------------------------------------------------------------------*/
    // leptonica: makeExpandTab2x (binexpand.c)
    private static ushort[] MakeExpandTab2x()
    {
        ushort[] tab = new ushort[256];
        for (int i = 0; i < 256; i++) {
            if ((i & 0x01) != 0) tab[i] = 0x3;
            if ((i & 0x02) != 0) tab[i] |= 0xc;
            if ((i & 0x04) != 0) tab[i] |= 0x30;
            if ((i & 0x08) != 0) tab[i] |= 0xc0;
            if ((i & 0x10) != 0) tab[i] |= 0x300;
            if ((i & 0x20) != 0) tab[i] |= 0xc00;
            if ((i & 0x40) != 0) tab[i] |= 0x3000;
            if ((i & 0x80) != 0) tab[i] |= 0xc000;
        }
        return tab;
    }

    // leptonica: makeExpandTab4x (binexpand.c)
    private static uint[] MakeExpandTab4x()
    {
        uint[] tab = new uint[256];
        for (int i = 0; i < 256; i++) {
            if ((i & 0x01) != 0) tab[i] = 0xf;
            if ((i & 0x02) != 0) tab[i] |= 0xf0;
            if ((i & 0x04) != 0) tab[i] |= 0xf00;
            if ((i & 0x08) != 0) tab[i] |= 0xf000;
            if ((i & 0x10) != 0) tab[i] |= 0xf0000;
            if ((i & 0x20) != 0) tab[i] |= 0xf00000;
            if ((i & 0x40) != 0) tab[i] |= 0xf000000;
            if ((i & 0x80) != 0) tab[i] |= 0xf0000000;
        }
        return tab;
    }

    // leptonica: makeExpandTab8x (binexpand.c)
    private static uint[] MakeExpandTab8x()
    {
        uint[] tab = new uint[16];
        for (int i = 0; i < 16; i++) {
            if ((i & 0x01) != 0) tab[i] = 0xff;
            if ((i & 0x02) != 0) tab[i] |= 0xff00;
            if ((i & 0x04) != 0) tab[i] |= 0xff0000;
            if ((i & 0x08) != 0) tab[i] |= 0xff000000;
        }
        return tab;
    }
}
