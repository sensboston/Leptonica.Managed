using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Leptonica;

/*
 *  roplow.c: Low-level rasterops.
 *
 *  Low level dest-only
 *      rasteropUniLow, rasteropUniWordAlignedLow, rasteropUniGeneralLow
 *  Low level src and dest
 *      rasteropLow, rasteropWordAlignedLow, rasteropVAlignedLow,
 *      rasteropGeneralLow
 *  Low level in-place full height vertical block transfer
 *      rasteropVipLow
 *  Low level in-place full width horizontal block transfer
 *      rasteropHipLow, shiftDataHorizontalLow
 *
 *  The C functions take pointers to the image data.  Here the data is a
 *  uint[] and every pointer is an int index into it.  The src and dest
 *  arrays may be the same array (in-place operation), exactly as in C.
 *  Unlike C, an out-of-range access throws IndexOutOfRangeException.
 *
 *  The low-level functions are internal; use the Pix.Rasterop* methods.
 */
public sealed partial class Pix
{
    private const int SHIFT_LEFT = 0;
    private const int SHIFT_RIGHT = 1;

    // Op codes that are not plain constants in C (PIX_NOT is a macro)
    private const int OP_NOT_DST = L.PIX_DST ^ 0x0f;
    private const int OP_NOT_SRC = L.PIX_SRC ^ 0x0f;

    private static readonly uint[] lmask32 = {0x0,
        0x80000000, 0xc0000000, 0xe0000000, 0xf0000000,
        0xf8000000, 0xfc000000, 0xfe000000, 0xff000000,
        0xff800000, 0xffc00000, 0xffe00000, 0xfff00000,
        0xfff80000, 0xfffc0000, 0xfffe0000, 0xffff0000,
        0xffff8000, 0xffffc000, 0xffffe000, 0xfffff000,
        0xfffff800, 0xfffffc00, 0xfffffe00, 0xffffff00,
        0xffffff80, 0xffffffc0, 0xffffffe0, 0xfffffff0,
        0xfffffff8, 0xfffffffc, 0xfffffffe, 0xffffffff};

    // rmask32 is defined in Pix2.cs (same table as in roplow.c)

    private static uint COMBINE_PARTIAL(uint d, uint s, uint m) => (d & ~m) | (s & m);

    /*--------------------------------------------------------------------*
     *                     Low-level dest-only rasterops                  *
     *--------------------------------------------------------------------*/
    // leptonica: rasteropUniLow (roplow.c)
    /// <summary>Dest-only rasterop: scales width, performs clipping, checks alignment, and dispatches.</summary>
    /// <param name="datad">dest image data</param>
    /// <param name="dpixw">width of dest</param>
    /// <param name="dpixh">height of dest</param>
    /// <param name="depth">depth of src and dest</param>
    /// <param name="dwpl">wpl of dest</param>
    /// <param name="dx">x val of UL corner of dest rectangle</param>
    /// <param name="dy">y val of UL corner of dest rectangle</param>
    /// <param name="dw">width of dest rectangle</param>
    /// <param name="dh">height of dest rectangle</param>
    /// <param name="op">op code</param>
    internal static void rasteropUniLow(uint[] datad, int dpixw, int dpixh, int depth,
                                        int dwpl, int dx, int dy, int dw, int dh, int op)
    {
        int dhangw, dhangh;

       /* -------------------------------------------------------*
        *            scale horizontal dimensions by depth
        * -------------------------------------------------------*/
        if (depth != 1) {
            dpixw *= depth;
            dx *= depth;
            dw *= depth;
        }

       /* -------------------------------------------------------*
        *            clip rectangle to dest image
        * -------------------------------------------------------*/
           /* first, clip horizontally (dx, dw) */
        if (dx < 0) {
            dw += dx;  /* reduce dw */
            dx = 0;
        }
        dhangw = dx + dw - dpixw;  /* rect ovhang dest to right */
        if (dhangw > 0)
            dw -= dhangw;  /* reduce dw */

           /* then, clip vertically (dy, dh) */
        if (dy < 0) {
            dh += dy;  /* reduce dh */
            dy = 0;
        }
        dhangh = dy + dh - dpixh;  /* rect ovhang dest below */
        if (dhangh > 0)
            dh -= dhangh;  /* reduce dh */

            /* if clipped entirely, quit */
        if ((dw <= 0) || (dh <= 0))
            return;

       /* -------------------------------------------------------*
        *       dispatch to aligned or non-aligned blitters
        * -------------------------------------------------------*/
        if ((dx & 31) == 0)
            rasteropUniWordAlignedLow(datad, dwpl, dx, dy, dw, dh, op);
        else
            rasteropUniGeneralLow(datad, dwpl, dx, dy, dw, dh, op);
    }

    /*--------------------------------------------------------------------*
     *           Static low-level uni rasterop with word alignment        *
     *--------------------------------------------------------------------*/
    // leptonica: rasteropUniWordAlignedLow (roplow.c)
    /// <remarks>
    /// This is called when the dest rect is left aligned
    /// on 32-bit word boundaries.   That is: dx &amp; 31 == 0.
    ///
    /// We make an optimized implementation of this because
    /// it is a common case: e.g., operating on a full dest image.
    /// </remarks>
    private static void rasteropUniWordAlignedLow(uint[] datad, int dwpl, int dx, int dy,
                                                  int dw, int dh, int op)
    {
        int nfullw;     /* number of full words */
        int pfword;     /* index of first word */
        int lwbits;     /* number of ovrhang bits in last partial word */
        uint lwmask = 0;  /* mask for last partial word */
        int lined;
        int i, j;

        /*--------------------------------------------------------*
         *                Preliminary calculations                *
         *--------------------------------------------------------*/
        nfullw = dw >> 5;
        lwbits = dw & 31;
        if (lwbits != 0)
            lwmask = lmask32[lwbits];
        pfword = dwpl * dy + (dx >> 5);

        /*--------------------------------------------------------*
         *            Now we're ready to do the ops               *
         *--------------------------------------------------------*/
        switch (op)
        {
        case L.PIX_CLR:
            for (i = 0; i < dh; i++) {
                lined = pfword + i * dwpl;
                if (nfullw > 0)
                    datad.AsSpan(lined, nfullw).Clear();
                if (lwbits != 0)
                    datad[lined + nfullw] = COMBINE_PARTIAL(datad[lined + nfullw], 0x0, lwmask);
            }
            break;
        case L.PIX_SET:
            for (i = 0; i < dh; i++) {
                lined = pfword + i * dwpl;
                if (nfullw > 0)
                    datad.AsSpan(lined, nfullw).Fill(0xffffffffu);
                if (lwbits != 0)
                    datad[lined + nfullw] = COMBINE_PARTIAL(datad[lined + nfullw], 0xffffffff, lwmask);
            }
            break;
        case OP_NOT_DST:
            for (i = 0; i < dh; i++) {
                lined = pfword + i * dwpl;
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(uint)lined);
                j = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; j + 8 <= nfullw; j += 8)
                        Vector256.OnesComplement(Vector256.LoadUnsafe(ref rd, (nuint)j)).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < nfullw; j++)
                    Unsafe.Add(ref rd, (nuint)j) = ~Unsafe.Add(ref rd, (nuint)j);
                if (lwbits != 0)
                    datad[lined + nfullw] = COMBINE_PARTIAL(datad[lined + nfullw], ~datad[lined + nfullw], lwmask);
            }
            break;
        default:
            Log.Error("rasteropUniWordAlignedLow", $"Operation {op} not permitted here!");
            break;
        }
    }

    /*--------------------------------------------------------------------*
     *        Static low-level uni rasterop without word alignment        *
     *--------------------------------------------------------------------*/
    // leptonica: rasteropUniGeneralLow (roplow.c)
    private static void rasteropUniGeneralLow(uint[] datad, int dwpl, int dx, int dy,
                                              int dw, int dh, int op)
    {
        int dfwpartb;   /* boolean (1, 0) if first dest word is partial */
        int dfwpart2b;  /* boolean (1, 0) if first dest word is doubly partial */
        uint dfwmask;   /* mask for first partial dest word */
        int dfwbits;    /* first word dest bits in ovrhang */
        int pdfwpart = 0;   /* index of first partial dest word */
        int dfwfullb;   /* boolean (1, 0) if there exists a full dest word */
        int dnfullw;    /* number of full words in dest */
        int pdfwfull = 0;   /* index of first full dest word */
        int dlwpartb;   /* boolean (1, 0) if last dest word is partial */
        uint dlwmask = 0;   /* mask for last partial dest word */
        int dlwbits;    /* last word dest bits in ovrhang */
        int pdlwpart = 0;   /* index of last partial dest word */
        int i, j;

        /*--------------------------------------------------------*
         *                Preliminary calculations                *
         *--------------------------------------------------------*/
            /* is the first word partial? */
        dfwmask = 0;
        if ((dx & 31) == 0) {  /* if not */
            dfwpartb = 0;
            dfwbits = 0;
        } else {  /* if so */
            dfwpartb = 1;
            dfwbits = 32 - (dx & 31);
            dfwmask = rmask32[dfwbits];
            pdfwpart = dwpl * dy + (dx >> 5);
        }

            /* is the first word doubly partial? */
        if (dw >= dfwbits) {  /* if not */
            dfwpart2b = 0;
        } else {  /* if so */
            dfwpart2b = 1;
            dfwmask &= lmask32[32 - dfwbits + dw];
        }

            /* is there a full dest word? */
        if (dfwpart2b == 1) {  /* not */
            dfwfullb = 0;
            dnfullw = 0;
        } else {
            dnfullw = (dw - dfwbits) >> 5;
            if (dnfullw == 0) {  /* if not */
                dfwfullb = 0;
            } else {  /* if so */
                dfwfullb = 1;
                if (dfwpartb != 0)
                    pdfwfull = pdfwpart + 1;
                else
                    pdfwfull = dwpl * dy + (dx >> 5);
            }
        }

            /* is the last word partial? */
        dlwbits = (dx + dw) & 31;
        if (dfwpart2b == 1 || dlwbits == 0) {  /* if not */
            dlwpartb = 0;
        } else {
            dlwpartb = 1;
            dlwmask = lmask32[dlwbits];
            if (dfwpartb != 0)
                pdlwpart = pdfwpart + 1 + dnfullw;
            else
                pdlwpart = dwpl * dy + (dx >> 5) + dnfullw;
        }

        /*--------------------------------------------------------*
         *            Now we're ready to do the ops               *
         *--------------------------------------------------------*/
        switch (op)
        {
        case L.PIX_CLR:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart], 0x0, dfwmask);
                    pdfwpart += dwpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] = 0x0;
                    pdfwfull += dwpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart], 0x0, dlwmask);
                    pdlwpart += dwpl;
                }
            }
            break;
        case L.PIX_SET:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart], 0xffffffff, dfwmask);
                    pdfwpart += dwpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] = 0xffffffff;
                    pdfwfull += dwpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart], 0xffffffff, dlwmask);
                    pdlwpart += dwpl;
                }
            }
            break;
        case OP_NOT_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart], ~datad[pdfwpart], dfwmask);
                    pdfwpart += dwpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] = ~datad[pdfwfull + j];
                    pdfwfull += dwpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart], ~datad[pdlwpart], dlwmask);
                    pdlwpart += dwpl;
                }
            }
            break;
        default:
            Log.Error("rasteropUniGeneralLow", $"Operation {op} not permitted here!");
            break;
        }
    }

    /*--------------------------------------------------------------------*
     *                   Low-level src and dest rasterops                 *
     *--------------------------------------------------------------------*/
    // leptonica: rasteropLow (roplow.c)
    /// <summary>Src and dest rasterop: scales width, performs clipping, checks alignment, and dispatches.</summary>
    /// <param name="datad">dest image data</param>
    /// <param name="dpixw">width of dest</param>
    /// <param name="dpixh">height of dest</param>
    /// <param name="depth">depth of src and dest</param>
    /// <param name="dwpl">wpl of dest</param>
    /// <param name="dx">x val of UL corner of dest rectangle</param>
    /// <param name="dy">y val of UL corner of dest rectangle</param>
    /// <param name="dw">width of dest rectangle</param>
    /// <param name="dh">height of dest rectangle</param>
    /// <param name="op">op code</param>
    /// <param name="datas">src image data</param>
    /// <param name="spixw">width of src</param>
    /// <param name="spixh">height of src</param>
    /// <param name="swpl">wpl of src</param>
    /// <param name="sx">x val of UL corner of src rectangle</param>
    /// <param name="sy">y val of UL corner of src rectangle</param>
    /// <remarks>
    /// Warning: the two images must have equal depth.  This is not checked.
    /// </remarks>
    internal static void rasteropLow(uint[] datad, int dpixw, int dpixh, int depth, int dwpl,
                                     int dx, int dy, int dw, int dh, int op,
                                     uint[] datas, int spixw, int spixh, int swpl, int sx, int sy)
    {
        int dhangw, shangw, dhangh, shangh;

       /* -------------------------------------------------------*
        *            Scale horizontal dimensions by depth        *
        * -------------------------------------------------------*/
        if (depth != 1) {
            dpixw *= depth;
            dx *= depth;
            dw *= depth;
            spixw *= depth;
            sx *= depth;
        }

       /* -------------------------------------------------------*
        *      Clip to max rectangle within both src and dest    *
        * -------------------------------------------------------*/
           /* Clip horizontally (sx, dx, dw) */
        if (dx < 0) {
            sx -= dx;  /* increase sx */
            dw += dx;  /* reduce dw */
            dx = 0;
        }
        if (sx < 0) {
            dx -= sx;  /* increase dx */
            dw += sx;  /* reduce dw */
            sx = 0;
        }
        dhangw = dx + dw - dpixw;  /* rect ovhang dest to right */
        if (dhangw > 0)
            dw -= dhangw;  /* reduce dw */
        shangw = sx + dw - spixw;   /* rect ovhang src to right */
        if (shangw > 0)
            dw -= shangw;  /* reduce dw */

           /* Clip vertically (sy, dy, dh) */
        if (dy < 0) {
            sy -= dy;  /* increase sy */
            dh += dy;  /* reduce dh */
            dy = 0;
        }
        if (sy < 0) {
            dy -= sy;  /* increase dy */
            dh += sy;  /* reduce dh */
            sy = 0;
        }
        dhangh = dy + dh - dpixh;  /* rect ovhang dest below */
        if (dhangh > 0)
            dh -= dhangh;  /* reduce dh */
        shangh = sy + dh - spixh;  /* rect ovhang src below */
        if (shangh > 0)
            dh -= shangh;  /* reduce dh */

            /* If clipped entirely, quit */
        if ((dw <= 0) || (dh <= 0))
            return;

       /* -------------------------------------------------------*
        *       Dispatch to aligned or non-aligned blitters      *
        * -------------------------------------------------------*/
        if (((dx & 31) == 0) && ((sx & 31) == 0))
            rasteropWordAlignedLow(datad, dwpl, dx, dy, dw, dh, op,
                                   datas, swpl, sx, sy);
        else if ((dx & 31) == (sx & 31))
            rasteropVAlignedLow(datad, dwpl, dx, dy, dw, dh, op,
                                datas, swpl, sx, sy);
        else
            rasteropGeneralLow(datad, dwpl, dx, dy, dw, dh, op,
                               datas, swpl, sx, sy);
    }

    /*--------------------------------------------------------------------*
     *        Static low-level rasterop with vertical word alignment      *
     *--------------------------------------------------------------------*/
    // leptonica: rasteropWordAlignedLow (roplow.c)
    /// <remarks>
    /// This is called when both the src and dest rects
    /// are left aligned on 32-bit word boundaries.
    /// That is: dx &amp; 31 == 0 and sx &amp; 31 == 0
    ///
    /// We make an optimized implementation of this because
    /// it is a common case: e.g., two images are rasterop'd
    /// starting from their UL corners 0,0.
    /// </remarks>
    private static void rasteropWordAlignedLow(uint[] datad, int dwpl, int dx, int dy,
                                               int dw, int dh, int op,
                                               uint[] datas, int swpl, int sx, int sy)
    {
        int nfullw;     /* number of full words */
        int psfword;    /* index of first src word */
        int pdfword;    /* index of first dest word */
        int lwbits;     /* number of ovrhang bits in last partial word */
        uint lwmask = 0;  /* mask for last partial word */
        int lines, lined;
        int i, j;

        /*--------------------------------------------------------*
         *                Preliminary calculations                *
         *--------------------------------------------------------*/
        nfullw = dw >> 5;
        lwbits = dw & 31;
        if (lwbits != 0)
            lwmask = lmask32[lwbits];
        psfword = swpl * sy + (sx >> 5);
        pdfword = dwpl * dy + (dx >> 5);

        /*--------------------------------------------------------*
         *            Now we're ready to do the ops               *
         *--------------------------------------------------------*/
        bool sameBuf = ReferenceEquals(datad, datas);
        switch (op)
        {
        case L.PIX_SRC:
            for (i = 0; i < dh; i++) {
                lines = psfword + i * swpl;
                lined = pdfword + i * dwpl;
                if (nfullw > 0) {
                    if (!sameBuf || lined <= lines || lined >= lines + nfullw) {
                        datas.AsSpan(lines, nfullw).CopyTo(datad.AsSpan(lined, nfullw));
                    } else {
                        for (j = 0; j < nfullw; j++)
                            datad[lined + j] = datas[lines + j];
                    }
                }
                if (lwbits != 0)
                    datad[lined + nfullw] = COMBINE_PARTIAL(datad[lined + nfullw], datas[lines + nfullw], lwmask);
            }
            break;
        case OP_NOT_SRC:
            for (i = 0; i < dh; i++) {
                lines = psfword + i * swpl;
                lined = pdfword + i * dwpl;
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(uint)lines);
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(uint)lined);
                j = 0;
                if (Vector256.IsHardwareAccelerated && (!sameBuf || lined <= lines || lined >= lines + nfullw)) {
                    for (; j + 8 <= nfullw; j += 8)
                        Vector256.OnesComplement(Vector256.LoadUnsafe(ref rs, (nuint)j)).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < nfullw; j++)
                    Unsafe.Add(ref rd, (nuint)j) = ~Unsafe.Add(ref rs, (nuint)j);
                if (lwbits != 0)
                    datad[lined + nfullw] = COMBINE_PARTIAL(datad[lined + nfullw], ~datas[lines + nfullw], lwmask);
            }
            break;
        case L.PIX_SRC | L.PIX_DST:
            for (i = 0; i < dh; i++) {
                lines = psfword + i * swpl;
                lined = pdfword + i * dwpl;
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(uint)lines);
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(uint)lined);
                j = 0;
                if (Vector256.IsHardwareAccelerated && (!sameBuf || lined <= lines || lined >= lines + nfullw)) {
                    for (; j + 8 <= nfullw; j += 8)
                        (Vector256.LoadUnsafe(ref rs, (nuint)j) | Vector256.LoadUnsafe(ref rd, (nuint)j)).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < nfullw; j++)
                    Unsafe.Add(ref rd, (nuint)j) |= Unsafe.Add(ref rs, (nuint)j);
                if (lwbits != 0)
                    datad[lined + nfullw] = COMBINE_PARTIAL(datad[lined + nfullw], (datas[lines + nfullw] | datad[lined + nfullw]), lwmask);
            }
            break;
        case L.PIX_SRC & L.PIX_DST:
            for (i = 0; i < dh; i++) {
                lines = psfword + i * swpl;
                lined = pdfword + i * dwpl;
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(uint)lines);
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(uint)lined);
                j = 0;
                if (Vector256.IsHardwareAccelerated && (!sameBuf || lined <= lines || lined >= lines + nfullw)) {
                    for (; j + 8 <= nfullw; j += 8)
                        (Vector256.LoadUnsafe(ref rs, (nuint)j) & Vector256.LoadUnsafe(ref rd, (nuint)j)).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < nfullw; j++)
                    Unsafe.Add(ref rd, (nuint)j) &= Unsafe.Add(ref rs, (nuint)j);
                if (lwbits != 0)
                    datad[lined + nfullw] = COMBINE_PARTIAL(datad[lined + nfullw], (datas[lines + nfullw] & datad[lined + nfullw]), lwmask);
            }
            break;
        case L.PIX_SRC ^ L.PIX_DST:
            for (i = 0; i < dh; i++) {
                lines = psfword + i * swpl;
                lined = pdfword + i * dwpl;
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(uint)lines);
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(uint)lined);
                j = 0;
                if (Vector256.IsHardwareAccelerated && (!sameBuf || lined <= lines || lined >= lines + nfullw)) {
                    for (; j + 8 <= nfullw; j += 8)
                        (Vector256.LoadUnsafe(ref rs, (nuint)j) ^ Vector256.LoadUnsafe(ref rd, (nuint)j)).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < nfullw; j++)
                    Unsafe.Add(ref rd, (nuint)j) ^= Unsafe.Add(ref rs, (nuint)j);
                if (lwbits != 0)
                    datad[lined + nfullw] = COMBINE_PARTIAL(datad[lined + nfullw], (datas[lines + nfullw] ^ datad[lined + nfullw]), lwmask);
            }
            break;
        case OP_NOT_SRC | L.PIX_DST:
            for (i = 0; i < dh; i++) {
                lines = psfword + i * swpl;
                lined = pdfword + i * dwpl;
                for (j = 0; j < nfullw; j++) {
                    datad[lined] = (~datas[lines] | datad[lined]);
                    lined++;
                    lines++;
                }
                if (lwbits != 0)
                    datad[lined] = COMBINE_PARTIAL(datad[lined], (~datas[lines] | datad[lined]), lwmask);
            }
            break;
        case OP_NOT_SRC & L.PIX_DST:
            for (i = 0; i < dh; i++) {
                lines = psfword + i * swpl;
                lined = pdfword + i * dwpl;
                ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(uint)lines);
                ref uint rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(uint)lined);
                j = 0;
                if (Vector256.IsHardwareAccelerated && (!sameBuf || lined <= lines || lined >= lines + nfullw)) {
                    for (; j + 8 <= nfullw; j += 8)
                        Vector256.AndNot(Vector256.LoadUnsafe(ref rd, (nuint)j), Vector256.LoadUnsafe(ref rs, (nuint)j)).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < nfullw; j++)
                    Unsafe.Add(ref rd, (nuint)j) &= ~Unsafe.Add(ref rs, (nuint)j);
                if (lwbits != 0)
                    datad[lined + nfullw] = COMBINE_PARTIAL(datad[lined + nfullw], (~datas[lines + nfullw] & datad[lined + nfullw]), lwmask);
            }
            break;
        case L.PIX_SRC | OP_NOT_DST:
            for (i = 0; i < dh; i++) {
                lines = psfword + i * swpl;
                lined = pdfword + i * dwpl;
                for (j = 0; j < nfullw; j++) {
                    datad[lined] = (datas[lines] | ~datad[lined]);
                    lined++;
                    lines++;
                }
                if (lwbits != 0)
                    datad[lined] = COMBINE_PARTIAL(datad[lined], (datas[lines] | ~datad[lined]), lwmask);
            }
            break;
        case L.PIX_SRC & OP_NOT_DST:
            for (i = 0; i < dh; i++) {
                lines = psfword + i * swpl;
                lined = pdfword + i * dwpl;
                for (j = 0; j < nfullw; j++) {
                    datad[lined] = (datas[lines] & ~datad[lined]);
                    lined++;
                    lines++;
                }
                if (lwbits != 0)
                    datad[lined] = COMBINE_PARTIAL(datad[lined], (datas[lines] & ~datad[lined]), lwmask);
            }
            break;
        case (L.PIX_SRC | L.PIX_DST) ^ 0x0f:
            for (i = 0; i < dh; i++) {
                lines = psfword + i * swpl;
                lined = pdfword + i * dwpl;
                for (j = 0; j < nfullw; j++) {
                    datad[lined] = ~(datas[lines] | datad[lined]);
                    lined++;
                    lines++;
                }
                if (lwbits != 0)
                    datad[lined] = COMBINE_PARTIAL(datad[lined], ~(datas[lines] | datad[lined]), lwmask);
            }
            break;
        case (L.PIX_SRC & L.PIX_DST) ^ 0x0f:
            for (i = 0; i < dh; i++) {
                lines = psfword + i * swpl;
                lined = pdfword + i * dwpl;
                for (j = 0; j < nfullw; j++) {
                    datad[lined] = ~(datas[lines] & datad[lined]);
                    lined++;
                    lines++;
                }
                if (lwbits != 0)
                    datad[lined] = COMBINE_PARTIAL(datad[lined], ~(datas[lines] & datad[lined]), lwmask);
            }
            break;
            /* this is three cases: ~(s ^ d), ~s ^ d, s ^ ~d  */
        case (L.PIX_SRC ^ L.PIX_DST) ^ 0x0f:
            for (i = 0; i < dh; i++) {
                lines = psfword + i * swpl;
                lined = pdfword + i * dwpl;
                for (j = 0; j < nfullw; j++) {
                    datad[lined] = ~(datas[lines] ^ datad[lined]);
                    lined++;
                    lines++;
                }
                if (lwbits != 0)
                    datad[lined] = COMBINE_PARTIAL(datad[lined], ~(datas[lines] ^ datad[lined]), lwmask);
            }
            break;
        default:
            Log.Error("rasteropWordAlignedLow", $"Operation {op} invalid");
            break;
        }
    }

    /*--------------------------------------------------------------------*
     *        Static low-level rasterop with vertical word alignment      *
     *--------------------------------------------------------------------*/
    // leptonica: rasteropVAlignedLow (roplow.c)
    /// <remarks>
    /// This is called when the left side of the src and dest
    /// rects have the same alignment relative to 32-bit word
    /// boundaries; i.e., (dx &amp; 31) == (sx &amp; 31)
    /// </remarks>
    private static void rasteropVAlignedLow(uint[] datad, int dwpl, int dx, int dy,
                                            int dw, int dh, int op,
                                            uint[] datas, int swpl, int sx, int sy)
    {
        int dfwpartb;   /* boolean (1, 0) if first dest word is partial */
        int dfwpart2b;  /* boolean (1, 0) if first dest word is doubly partial */
        uint dfwmask;   /* mask for first partial dest word */
        int dfwbits;    /* first word dest bits in ovrhang */
        int pdfwpart = 0;   /* index of first partial dest word */
        int psfwpart = 0;   /* index of first partial src word */
        int dfwfullb;   /* boolean (1, 0) if there exists a full dest word */
        int dnfullw;    /* number of full words in dest */
        int pdfwfull = 0;   /* index of first full dest word */
        int psfwfull = 0;   /* index of first full src word */
        int dlwpartb;   /* boolean (1, 0) if last dest word is partial */
        uint dlwmask = 0;   /* mask for last partial dest word */
        int dlwbits;    /* last word dest bits in ovrhang */
        int pdlwpart = 0;   /* index of last partial dest word */
        int pslwpart = 0;   /* index of last partial src word */
        int i, j;

        /*--------------------------------------------------------*
         *                Preliminary calculations                *
         *--------------------------------------------------------*/
            /* is the first word partial? */
        dfwmask = 0;
        if ((dx & 31) == 0) {  /* if not */
            dfwpartb = 0;
            dfwbits = 0;
        } else {  /* if so */
            dfwpartb = 1;
            dfwbits = 32 - (dx & 31);
            dfwmask = rmask32[dfwbits];
            pdfwpart = dwpl * dy + (dx >> 5);
            psfwpart = swpl * sy + (sx >> 5);
        }

            /* is the first word doubly partial? */
        if (dw >= dfwbits) {  /* if not */
            dfwpart2b = 0;
        } else {  /* if so */
            dfwpart2b = 1;
            dfwmask &= lmask32[32 - dfwbits + dw];
        }

            /* is there a full dest word? */
        if (dfwpart2b == 1) {  /* not */
            dfwfullb = 0;
            dnfullw = 0;
        } else {
            dnfullw = (dw - dfwbits) >> 5;
            if (dnfullw == 0) {  /* if not */
                dfwfullb = 0;
            } else {  /* if so */
                dfwfullb = 1;
                if (dfwpartb != 0) {
                    pdfwfull = pdfwpart + 1;
                    psfwfull = psfwpart + 1;
                } else {
                    pdfwfull = dwpl * dy + (dx >> 5);
                    psfwfull = swpl * sy + (sx >> 5);
                }
            }
        }

            /* is the last word partial? */
        dlwbits = (dx + dw) & 31;
        if (dfwpart2b == 1 || dlwbits == 0) {  /* if not */
            dlwpartb = 0;
        } else {
            dlwpartb = 1;
            dlwmask = lmask32[dlwbits];
            if (dfwpartb != 0) {
                pdlwpart = pdfwpart + 1 + dnfullw;
                pslwpart = psfwpart + 1 + dnfullw;
            } else {
                pdlwpart = dwpl * dy + (dx >> 5) + dnfullw;
                pslwpart = swpl * sy + (sx >> 5) + dnfullw;
            }
        }

        /*--------------------------------------------------------*
         *            Now we're ready to do the ops               *
         *--------------------------------------------------------*/
        switch (op)
        {
        case L.PIX_SRC:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart], datas[psfwpart], dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] = datas[psfwfull + j];
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart], datas[pslwpart], dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case OP_NOT_SRC:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart], ~datas[psfwpart], dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] = ~datas[psfwfull + j];
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart], ~datas[pslwpart], dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case L.PIX_SRC | L.PIX_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                        (datas[psfwpart] | datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] |= datas[psfwfull + j];
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                         (datas[pslwpart] | datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case L.PIX_SRC & L.PIX_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                        (datas[psfwpart] & datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] &= datas[psfwfull + j];
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                         (datas[pslwpart] & datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case L.PIX_SRC ^ L.PIX_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                        (datas[psfwpart] ^ datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] ^= datas[psfwfull + j];
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                         (datas[pslwpart] ^ datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case OP_NOT_SRC | L.PIX_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                        (~datas[psfwpart] | datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] |= ~datas[psfwfull + j];
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                         (~datas[pslwpart] | datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case OP_NOT_SRC & L.PIX_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                        (~datas[psfwpart] & datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] &= ~datas[psfwfull + j];
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                         (~datas[pslwpart] & datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case L.PIX_SRC | OP_NOT_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                        (datas[psfwpart] | ~datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] = datas[psfwfull + j] | ~datad[pdfwfull + j];
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                         (datas[pslwpart] | ~datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case L.PIX_SRC & OP_NOT_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                        (datas[psfwpart] & ~datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] = datas[psfwfull + j] & ~datad[pdfwfull + j];
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                         (datas[pslwpart] & ~datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case (L.PIX_SRC | L.PIX_DST) ^ 0x0f:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                        ~(datas[psfwpart] | datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] = ~(datas[psfwfull + j] | datad[pdfwfull + j]);
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                         ~(datas[pslwpart] | datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case (L.PIX_SRC & L.PIX_DST) ^ 0x0f:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                        ~(datas[psfwpart] & datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] = ~(datas[psfwfull + j] & datad[pdfwfull + j]);
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                         ~(datas[pslwpart] & datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
            /* this is three cases: ~(s ^ d), ~s ^ d, s ^ ~d  */
        case (L.PIX_SRC ^ L.PIX_DST) ^ 0x0f:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                        ~(datas[psfwpart] ^ datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++)
                        datad[pdfwfull + j] = ~(datas[psfwfull + j] ^ datad[pdfwfull + j]);
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                         ~(datas[pslwpart] ^ datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        default:
            Log.Error("rasteropVAlignedLow", $"Operation {op:x} invalid");
            break;
        }
    }

    /*--------------------------------------------------------------------*
     *     Static low-level rasterop without vertical word alignment      *
     *--------------------------------------------------------------------*/
    // leptonica: rasteropGeneralLow (roplow.c)
    /// <remarks>
    /// This is called when the src and dest rects are
    /// do not have the same 32-bit word alignment.
    ///
    /// The method is a generalization of rasteropVAlignLow.
    /// There, the src image pieces were directly merged
    /// with the dest.  Here, we shift the source bits
    /// to fill words that are aligned with the dest, and
    /// then use those "source words" exactly in place
    /// of the source words that were used in rasteropVAlignLow.
    ///
    /// The critical parameter is thus the shift required
    /// for the src.  Consider the left edge of the rectangle.
    /// The overhang into the src and dest words are found,
    /// and the difference is exactly this shift.  There are
    /// two separate cases, depending on whether the src pixels
    /// are shifted left or right.  If the src overhang is
    /// larger than the dest overhang, the src is shifted to
    /// the right, and a number of pixels equal to the shift are
    /// left over for filling the next dest word, if necessary.
    ///
    /// But if the dest overhang is larger than the src overhang,
    /// the src is shifted to the left, and depending on the width of
    /// transferred pixels, it may also be necessary to shift pixels
    /// in from the next src word, in order to fill the dest word.
    /// An interesting case is where the src overhang equals the width,
    /// dw, of the block.  Then all the pixels necessary to fill the first
    /// dest word can be taken from the first src word, up to the last
    /// src pixel in the word, and no pixels from the next src word are
    /// required.  Consider this simple example, where a single pixel from
    /// the src is transferred to the dest:
    ///    pix1 = pixCreate(32, 1, 1);
    ///    pix2 = pixCreate(32, 1, 1);
    ///    pixRasterop(pix1, 30, 0, 1, 1, PIX_SRC, pix2, 31, 0);
    /// Here, the pixel at the right end of the src image (sx = 31)
    /// is shifted one bit to the left (to dx = 30).  The width (1) equals
    /// the src overhang (1), and no pixels from the next word are required.
    /// (This must be true because there is only one src word.)
    /// </remarks>
    private static void rasteropGeneralLow(uint[] datad, int dwpl, int dx, int dy,
                                           int dw, int dh, int op,
                                           uint[] datas, int swpl, int sx, int sy)
    {
        int dfwpartb;    /* boolean (1, 0) if first dest word is partial      */
        int dfwpart2b;   /* boolean (1, 0) if 1st dest word is doubly partial */
        uint dfwmask;    /* mask for first partial dest word                  */
        int dfwbits;     /* first word dest bits in overhang; 0-31            */
        int dhang;       /* dest overhang in first partial word,              */
                         /* or 0 if dest is word aligned (same as dfwbits)    */
        int pdfwpart = 0;    /* index of first partial dest word              */
        int psfwpart = 0;    /* index of first partial src word               */
        int dfwfullb;    /* boolean (1, 0) if there exists a full dest word   */
        int dnfullw;     /* number of full words in dest                      */
        int pdfwfull = 0;    /* index of first full dest word                 */
        int psfwfull = 0;    /* index of first full src word                  */
        int dlwpartb;    /* boolean (1, 0) if last dest word is partial       */
        uint dlwmask = 0;    /* mask for last partial dest word               */
        int dlwbits;     /* last word dest bits in ovrhang                    */
        int pdlwpart = 0;    /* index of last partial dest word               */
        int pslwpart = 0;    /* index of last partial src word                */
        uint sword;      /* compose src word aligned with the dest words      */
        int sfwbits;     /* first word src bits in overhang (1-32),           */
                         /* or 32 if src is word aligned                      */
        int shang;       /* source overhang in the first partial word,        */
                         /* or 0 if src is word aligned (not same as sfwbits) */
        int sleftshift;  /* bits to shift left for source word to align       */
                         /* with the dest.  Also the number of bits that      */
                         /* get shifted to the right to align with the dest.  */
        int srightshift; /* bits to shift right for source word to align      */
                         /* with dest.  Also, the number of bits that get     */
                         /* shifted left to align with the dest.              */
        uint srightmask; /* mask for selecting sleftshift bits that have      */
                         /* been shifted right by srightshift bits            */
                         /* (l_int32 in C; only its bit pattern is used)      */
        int sfwshiftdir = 0; /* either SHIFT_LEFT or SHIFT_RIGHT              */
        int sfwaddb = 0;     /* boolean: do we need an additional sfw right shift? */
        int slwaddb = 0;     /* boolean: do we need an additional slw right shift? */
        int i, j;

        /*--------------------------------------------------------*
         *                Preliminary calculations                *
         *--------------------------------------------------------*/
            /* To get alignment of src with dst (e.g., in the
             * full words) the src must do a left shift of its
             * relative overhang in the current src word,
             * and OR that with a right shift of
             * (31 -  relative overhang) from the next src word.
             * We find the absolute overhangs, the relative overhangs,
             * the required shifts and the src mask */
        if ((sx & 31) == 0)
            shang = 0;
        else
            shang = 32 - (sx & 31);
        if ((dx & 31) == 0)
            dhang = 0;
        else
            dhang = 32 - (dx & 31);

        if (shang == 0 && dhang == 0) {  /* this should be treated by an
                                            aligned operation, not by
                                            this general rasterop! */
            sleftshift = 0;
            srightshift = 0;
            srightmask = rmask32[0];
        } else {
            if (dhang > shang)
                sleftshift = dhang - shang;
            else
                sleftshift = 32 - (shang - dhang);
            srightshift = 32 - sleftshift;
            srightmask = rmask32[sleftshift];
        }

            /* Is the first dest word partial? */
        dfwmask = 0;
        if ((dx & 31) == 0) {  /* if not */
            dfwpartb = 0;
            dfwbits = 0;
        } else {  /* if so */
            dfwpartb = 1;
            dfwbits = 32 - (dx & 31);
            dfwmask = rmask32[dfwbits];
            pdfwpart = dwpl * dy + (dx >> 5);
            psfwpart = swpl * sy + (sx >> 5);
            sfwbits = 32 - (sx & 31);
            if (dfwbits > sfwbits) {
                sfwshiftdir = SHIFT_LEFT;  /* shift by sleftshift */
                    /* Do we have enough bits from the current src word? */
                if (dw <= shang)
                    sfwaddb = 0;  /* yes: we have enough bits */
                else
                    sfwaddb = 1;  /* no: rshift in next src word by srightshift */
            } else {
                sfwshiftdir = SHIFT_RIGHT;  /* shift by srightshift */
            }
        }

            /* Is the first dest word doubly partial? */
        if (dw >= dfwbits) {  /* if not */
            dfwpart2b = 0;
        } else {  /* if so */
            dfwpart2b = 1;
            dfwmask &= lmask32[32 - dfwbits + dw];
        }

            /* Is there a full dest word? */
        if (dfwpart2b == 1) {  /* not */
            dfwfullb = 0;
            dnfullw = 0;
        } else {
            dnfullw = (dw - dfwbits) >> 5;
            if (dnfullw == 0) {  /* if not */
                dfwfullb = 0;
            } else {  /* if so */
                dfwfullb = 1;
                pdfwfull = dwpl * dy + ((dx + dhang) >> 5);
                psfwfull = swpl * sy + ((sx + dhang) >> 5); /* yes, dhang */
            }
        }

            /* Is the last dest word partial? */
        dlwbits = (dx + dw) & 31;
        if (dfwpart2b == 1 || dlwbits == 0) {  /* if not */
            dlwpartb = 0;
        } else {
            dlwpartb = 1;
            dlwmask = lmask32[dlwbits];
            pdlwpart = dwpl * dy + ((dx + dhang) >> 5) + dnfullw;
            pslwpart = swpl * sy + ((sx + dhang) >> 5) + dnfullw;
            if (dlwbits <= srightshift)   /* must be <= here !!! */
                slwaddb = 0;  /* we got enough bits from current src word */
            else
                slwaddb = 1;  /* must rshift in next src word by srightshift */
        }

        /*--------------------------------------------------------*
         *            Now we're ready to do the ops               *
         *--------------------------------------------------------*/
        switch (op)
        {
        case L.PIX_SRC:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++)
                {
                    if (sfwshiftdir == SHIFT_LEFT) {
                        sword = datas[psfwpart] << sleftshift;
                        if (sfwaddb != 0)
                            sword = COMBINE_PARTIAL(sword,
                                          datas[psfwpart + 1] >> srightshift,
                                           srightmask);
                    } else {  /* shift right */
                        sword = datas[psfwpart] >> srightshift;
                    }

                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart], sword, dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                bool canVectorize = !ReferenceEquals(datad, datas) &&
                                    (Vector256.IsHardwareAccelerated || Vector128.IsHardwareAccelerated);
                for (i = 0; i < dh; i++) {
                    j = 0;
                    if (canVectorize) {
                        ref uint srcRowRef = ref datas[psfwfull];
                        ref uint dstRowRef = ref datad[pdfwfull];
                        int srcWordsRemaining = swpl - (psfwfull % swpl);
                        if (Vector256.IsHardwareAccelerated) {
                            for (; j + Vector256<uint>.Count <= dnfullw &&
                                   j + Vector256<uint>.Count < srcWordsRemaining; j += Vector256<uint>.Count) {
                                var src0 = Vector256.LoadUnsafe(ref srcRowRef, (nuint)j);
                                var src1 = Vector256.LoadUnsafe(ref srcRowRef, (nuint)(j + 1));
                                var shifted = Vector256.ShiftLeft(src0, sleftshift) |
                                              Vector256.ShiftRightLogical(src1, srightshift);
                                shifted.StoreUnsafe(ref dstRowRef, (nuint)j);
                            }
                        }
                        if (Vector128.IsHardwareAccelerated) {
                            for (; j + Vector128<uint>.Count <= dnfullw &&
                                   j + Vector128<uint>.Count < srcWordsRemaining; j += Vector128<uint>.Count) {
                                var src0 = Vector128.LoadUnsafe(ref srcRowRef, (nuint)j);
                                var src1 = Vector128.LoadUnsafe(ref srcRowRef, (nuint)(j + 1));
                                var shifted = Vector128.ShiftLeft(src0, sleftshift) |
                                              Vector128.ShiftRightLogical(src1, srightshift);
                                shifted.StoreUnsafe(ref dstRowRef, (nuint)j);
                            }
                        }
                    }
                    for (; j < dnfullw; j++) {
                        sword = COMBINE_PARTIAL(datas[psfwfull + j] << sleftshift,
                                       datas[psfwfull + j + 1] >> srightshift,
                                       srightmask);
                        datad[pdfwfull + j] = sword;
                    }
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    sword = datas[pslwpart] << sleftshift;
                    if (slwaddb != 0)
                        sword = COMBINE_PARTIAL(sword,
                                      datas[pslwpart + 1] >> srightshift,
                                      srightmask);

                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart], sword, dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case OP_NOT_SRC:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++)
                {
                    if (sfwshiftdir == SHIFT_LEFT) {
                        sword = datas[psfwpart] << sleftshift;
                        if (sfwaddb != 0)
                            sword = COMBINE_PARTIAL(sword,
                                          datas[psfwpart + 1] >> srightshift,
                                           srightmask);
                    } else {  /* shift right */
                        sword = datas[psfwpart] >> srightshift;
                    }

                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart], ~sword, dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++) {
                        sword = COMBINE_PARTIAL(datas[psfwfull + j] << sleftshift,
                                       datas[psfwfull + j + 1] >> srightshift,
                                       srightmask);
                        datad[pdfwfull + j] = ~sword;
                    }
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    sword = datas[pslwpart] << sleftshift;
                    if (slwaddb != 0)
                        sword = COMBINE_PARTIAL(sword,
                                      datas[pslwpart + 1] >> srightshift,
                                      srightmask);

                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart], ~sword, dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case L.PIX_SRC | L.PIX_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++)
                {
                    if (sfwshiftdir == SHIFT_LEFT) {
                        sword = datas[psfwpart] << sleftshift;
                        if (sfwaddb != 0)
                            sword = COMBINE_PARTIAL(sword,
                                          datas[psfwpart + 1] >> srightshift,
                                           srightmask);
                    } else {  /* shift right */
                        sword = datas[psfwpart] >> srightshift;
                    }

                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                     (sword | datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    j = 0;
                    // Aliased rasterops retain their original scalar traversal.
                    if (!ReferenceEquals(datad, datas)) {
                        if (Vector256.IsHardwareAccelerated) {
                            for (; j <= dnfullw - Vector256<uint>.Count; j += Vector256<uint>.Count) {
                                var left = Vector256.LoadUnsafe(ref datas[psfwfull + j]);
                                var right = Vector256.LoadUnsafe(ref datas[psfwfull + j + 1]);
                                var dest = Vector256.LoadUnsafe(ref datad[pdfwfull + j]);
                                var source = Vector256.ShiftLeft(left, sleftshift) |
                                             Vector256.ShiftRightLogical(right, srightshift);
                                (dest | source).StoreUnsafe(ref datad[pdfwfull + j]);
                            }
                        }
                        if (Vector128.IsHardwareAccelerated) {
                            for (; j <= dnfullw - Vector128<uint>.Count; j += Vector128<uint>.Count) {
                                var left = Vector128.LoadUnsafe(ref datas[psfwfull + j]);
                                var right = Vector128.LoadUnsafe(ref datas[psfwfull + j + 1]);
                                var dest = Vector128.LoadUnsafe(ref datad[pdfwfull + j]);
                                var source = Vector128.ShiftLeft(left, sleftshift) |
                                             Vector128.ShiftRightLogical(right, srightshift);
                                (dest | source).StoreUnsafe(ref datad[pdfwfull + j]);
                            }
                        }
                    }
                    for (; j < dnfullw; j++) {
                        sword = COMBINE_PARTIAL(datas[psfwfull + j] << sleftshift,
                                       datas[psfwfull + j + 1] >> srightshift,
                                       srightmask);
                        datad[pdfwfull + j] |= sword;
                    }
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    sword = datas[pslwpart] << sleftshift;
                    if (slwaddb != 0)
                        sword = COMBINE_PARTIAL(sword,
                                      datas[pslwpart + 1] >> srightshift,
                                      srightmask);

                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                     (sword | datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case L.PIX_SRC & L.PIX_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++)
                {
                    if (sfwshiftdir == SHIFT_LEFT) {
                        sword = datas[psfwpart] << sleftshift;
                        if (sfwaddb != 0)
                            sword = COMBINE_PARTIAL(sword,
                                          datas[psfwpart + 1] >> srightshift,
                                           srightmask);
                    } else {  /* shift right */
                        sword = datas[psfwpart] >> srightshift;
                    }

                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                     (sword & datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    j = 0;
                    // Aliased rasterops retain their original scalar traversal.
                    if (!ReferenceEquals(datad, datas)) {
                        if (Vector256.IsHardwareAccelerated) {
                            for (; j <= dnfullw - Vector256<uint>.Count; j += Vector256<uint>.Count) {
                                var left = Vector256.LoadUnsafe(ref datas[psfwfull + j]);
                                var right = Vector256.LoadUnsafe(ref datas[psfwfull + j + 1]);
                                var dest = Vector256.LoadUnsafe(ref datad[pdfwfull + j]);
                                var source = Vector256.ShiftLeft(left, sleftshift) |
                                             Vector256.ShiftRightLogical(right, srightshift);
                                (dest & source).StoreUnsafe(ref datad[pdfwfull + j]);
                            }
                        }
                        if (Vector128.IsHardwareAccelerated) {
                            for (; j <= dnfullw - Vector128<uint>.Count; j += Vector128<uint>.Count) {
                                var left = Vector128.LoadUnsafe(ref datas[psfwfull + j]);
                                var right = Vector128.LoadUnsafe(ref datas[psfwfull + j + 1]);
                                var dest = Vector128.LoadUnsafe(ref datad[pdfwfull + j]);
                                var source = Vector128.ShiftLeft(left, sleftshift) |
                                             Vector128.ShiftRightLogical(right, srightshift);
                                (dest & source).StoreUnsafe(ref datad[pdfwfull + j]);
                            }
                        }
                    }
                    for (; j < dnfullw; j++) {
                        sword = COMBINE_PARTIAL(datas[psfwfull + j] << sleftshift,
                                       datas[psfwfull + j + 1] >> srightshift,
                                       srightmask);
                        datad[pdfwfull + j] &= sword;
                    }
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    sword = datas[pslwpart] << sleftshift;
                    if (slwaddb != 0)
                        sword = COMBINE_PARTIAL(sword,
                                      datas[pslwpart + 1] >> srightshift,
                                      srightmask);

                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                     (sword & datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case L.PIX_SRC ^ L.PIX_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++)
                {
                    if (sfwshiftdir == SHIFT_LEFT) {
                        sword = datas[psfwpart] << sleftshift;
                        if (sfwaddb != 0)
                            sword = COMBINE_PARTIAL(sword,
                                          datas[psfwpart + 1] >> srightshift,
                                           srightmask);
                    } else {  /* shift right */
                        sword = datas[psfwpart] >> srightshift;
                    }

                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                     (sword ^ datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    j = 0;
                    // Preserve the scalar traversal for aliased images: stores may
                    // affect subsequent source words in an overlapping rasterop.
                    if (!ReferenceEquals(datad, datas)) {
                        if (Vector256.IsHardwareAccelerated) {
                            for (; j <= dnfullw - Vector256<uint>.Count; j += Vector256<uint>.Count) {
                                var left = Vector256.LoadUnsafe(ref datas[psfwfull + j]);
                                var right = Vector256.LoadUnsafe(ref datas[psfwfull + j + 1]);
                                var dest = Vector256.LoadUnsafe(ref datad[pdfwfull + j]);
                                var source = Vector256.ShiftLeft(left, sleftshift) |
                                             Vector256.ShiftRightLogical(right, srightshift);
                                (dest ^ source).StoreUnsafe(ref datad[pdfwfull + j]);
                            }
                        }
                        if (Vector128.IsHardwareAccelerated) {
                            for (; j <= dnfullw - Vector128<uint>.Count; j += Vector128<uint>.Count) {
                                var left = Vector128.LoadUnsafe(ref datas[psfwfull + j]);
                                var right = Vector128.LoadUnsafe(ref datas[psfwfull + j + 1]);
                                var dest = Vector128.LoadUnsafe(ref datad[pdfwfull + j]);
                                var source = Vector128.ShiftLeft(left, sleftshift) |
                                             Vector128.ShiftRightLogical(right, srightshift);
                                (dest ^ source).StoreUnsafe(ref datad[pdfwfull + j]);
                            }
                        }
                    }
                    for (; j < dnfullw; j++) {
                        sword = COMBINE_PARTIAL(datas[psfwfull + j] << sleftshift,
                                       datas[psfwfull + j + 1] >> srightshift,
                                       srightmask);
                        datad[pdfwfull + j] ^= sword;
                    }
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    sword = datas[pslwpart] << sleftshift;
                    if (slwaddb != 0)
                        sword = COMBINE_PARTIAL(sword,
                                      datas[pslwpart + 1] >> srightshift,
                                      srightmask);

                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                     (sword ^ datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case OP_NOT_SRC | L.PIX_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++)
                {
                    if (sfwshiftdir == SHIFT_LEFT) {
                        sword = datas[psfwpart] << sleftshift;
                        if (sfwaddb != 0)
                            sword = COMBINE_PARTIAL(sword,
                                          datas[psfwpart + 1] >> srightshift,
                                           srightmask);
                    } else {  /* shift right */
                        sword = datas[psfwpart] >> srightshift;
                    }

                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                     (~sword | datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++) {
                        sword = COMBINE_PARTIAL(datas[psfwfull + j] << sleftshift,
                                       datas[psfwfull + j + 1] >> srightshift,
                                       srightmask);
                        datad[pdfwfull + j] |= ~sword;
                    }
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    sword = datas[pslwpart] << sleftshift;
                    if (slwaddb != 0)
                        sword = COMBINE_PARTIAL(sword,
                                      datas[pslwpart + 1] >> srightshift,
                                      srightmask);

                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                     (~sword | datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case OP_NOT_SRC & L.PIX_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++)
                {
                    if (sfwshiftdir == SHIFT_LEFT) {
                        sword = datas[psfwpart] << sleftshift;
                        if (sfwaddb != 0)
                            sword = COMBINE_PARTIAL(sword,
                                          datas[psfwpart + 1] >> srightshift,
                                           srightmask);
                    } else {  /* shift right */
                        sword = datas[psfwpart] >> srightshift;
                    }

                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                     (~sword & datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++) {
                        sword = COMBINE_PARTIAL(datas[psfwfull + j] << sleftshift,
                                       datas[psfwfull + j + 1] >> srightshift,
                                       srightmask);
                        datad[pdfwfull + j] &= ~sword;
                    }
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    sword = datas[pslwpart] << sleftshift;
                    if (slwaddb != 0)
                        sword = COMBINE_PARTIAL(sword,
                                      datas[pslwpart + 1] >> srightshift,
                                      srightmask);

                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                     (~sword & datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case L.PIX_SRC | OP_NOT_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++)
                {
                    if (sfwshiftdir == SHIFT_LEFT) {
                        sword = datas[psfwpart] << sleftshift;
                        if (sfwaddb != 0)
                            sword = COMBINE_PARTIAL(sword,
                                          datas[psfwpart + 1] >> srightshift,
                                           srightmask);
                    } else {  /* shift right */
                        sword = datas[psfwpart] >> srightshift;
                    }

                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                     (sword | ~datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++) {
                        sword = COMBINE_PARTIAL(datas[psfwfull + j] << sleftshift,
                                       datas[psfwfull + j + 1] >> srightshift,
                                       srightmask);
                        datad[pdfwfull + j] = sword | ~datad[pdfwfull + j];
                    }
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    sword = datas[pslwpart] << sleftshift;
                    if (slwaddb != 0)
                        sword = COMBINE_PARTIAL(sword,
                                      datas[pslwpart + 1] >> srightshift,
                                      srightmask);

                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                     (sword | ~datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case L.PIX_SRC & OP_NOT_DST:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++)
                {
                    if (sfwshiftdir == SHIFT_LEFT) {
                        sword = datas[psfwpart] << sleftshift;
                        if (sfwaddb != 0)
                            sword = COMBINE_PARTIAL(sword,
                                          datas[psfwpart + 1] >> srightshift,
                                           srightmask);
                    } else {  /* shift right */
                        sword = datas[psfwpart] >> srightshift;
                    }

                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                     (sword & ~datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++) {
                        sword = COMBINE_PARTIAL(datas[psfwfull + j] << sleftshift,
                                       datas[psfwfull + j + 1] >> srightshift,
                                       srightmask);
                        datad[pdfwfull + j] = sword & ~datad[pdfwfull + j];
                    }
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    sword = datas[pslwpart] << sleftshift;
                    if (slwaddb != 0)
                        sword = COMBINE_PARTIAL(sword,
                                      datas[pslwpart + 1] >> srightshift,
                                      srightmask);

                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                     (sword & ~datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case (L.PIX_SRC | L.PIX_DST) ^ 0x0f:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++)
                {
                    if (sfwshiftdir == SHIFT_LEFT) {
                        sword = datas[psfwpart] << sleftshift;
                        if (sfwaddb != 0)
                            sword = COMBINE_PARTIAL(sword,
                                          datas[psfwpart + 1] >> srightshift,
                                           srightmask);
                    } else {  /* shift right */
                        sword = datas[psfwpart] >> srightshift;
                    }

                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                     ~(sword | datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++) {
                        sword = COMBINE_PARTIAL(datas[psfwfull + j] << sleftshift,
                                       datas[psfwfull + j + 1] >> srightshift,
                                       srightmask);
                        datad[pdfwfull + j] = ~(sword | datad[pdfwfull + j]);
                    }
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    sword = datas[pslwpart] << sleftshift;
                    if (slwaddb != 0)
                        sword = COMBINE_PARTIAL(sword,
                                      datas[pslwpart + 1] >> srightshift,
                                      srightmask);

                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                     ~(sword | datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        case (L.PIX_SRC & L.PIX_DST) ^ 0x0f:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++)
                {
                    if (sfwshiftdir == SHIFT_LEFT) {
                        sword = datas[psfwpart] << sleftshift;
                        if (sfwaddb != 0)
                            sword = COMBINE_PARTIAL(sword,
                                          datas[psfwpart + 1] >> srightshift,
                                           srightmask);
                    } else {  /* shift right */
                        sword = datas[psfwpart] >> srightshift;
                    }

                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                     ~(sword & datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++) {
                        sword = COMBINE_PARTIAL(datas[psfwfull + j] << sleftshift,
                                       datas[psfwfull + j + 1] >> srightshift,
                                       srightmask);
                        datad[pdfwfull + j] = ~(sword & datad[pdfwfull + j]);
                    }
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    sword = datas[pslwpart] << sleftshift;
                    if (slwaddb != 0)
                        sword = COMBINE_PARTIAL(sword,
                                      datas[pslwpart + 1] >> srightshift,
                                      srightmask);

                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                     ~(sword & datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
            /* this is three cases: ~(s ^ d), ~s ^ d, s ^ ~d  */
        case (L.PIX_SRC ^ L.PIX_DST) ^ 0x0f:
                /* do the first partial word */
            if (dfwpartb != 0) {
                for (i = 0; i < dh; i++)
                {
                    if (sfwshiftdir == SHIFT_LEFT) {
                        sword = datas[psfwpart] << sleftshift;
                        if (sfwaddb != 0)
                            sword = COMBINE_PARTIAL(sword,
                                          datas[psfwpart + 1] >> srightshift,
                                           srightmask);
                    } else {  /* shift right */
                        sword = datas[psfwpart] >> srightshift;
                    }

                    datad[pdfwpart] = COMBINE_PARTIAL(datad[pdfwpart],
                                     ~(sword ^ datad[pdfwpart]), dfwmask);
                    pdfwpart += dwpl;
                    psfwpart += swpl;
                }
            }

                /* do the full words */
            if (dfwfullb != 0) {
                for (i = 0; i < dh; i++) {
                    for (j = 0; j < dnfullw; j++) {
                        sword = COMBINE_PARTIAL(datas[psfwfull + j] << sleftshift,
                                       datas[psfwfull + j + 1] >> srightshift,
                                       srightmask);
                        datad[pdfwfull + j] = ~(sword ^ datad[pdfwfull + j]);
                    }
                    pdfwfull += dwpl;
                    psfwfull += swpl;
                }
            }

                /* do the last partial word */
            if (dlwpartb != 0) {
                for (i = 0; i < dh; i++) {
                    sword = datas[pslwpart] << sleftshift;
                    if (slwaddb != 0)
                        sword = COMBINE_PARTIAL(sword,
                                      datas[pslwpart + 1] >> srightshift,
                                      srightmask);

                    datad[pdlwpart] = COMBINE_PARTIAL(datad[pdlwpart],
                                     ~(sword ^ datad[pdlwpart]), dlwmask);
                    pdlwpart += dwpl;
                    pslwpart += swpl;
                }
            }
            break;
        default:
            Log.Error("rasteropGeneralLow", $"Operation {op:x} invalid");
            break;
        }
    }

    /*--------------------------------------------------------------------*
     *        Low level in-place full height vertical block transfer      *
     *--------------------------------------------------------------------*/
    // leptonica: rasteropVipLow (roplow.c)
    /// <summary>In-place translation of a full-height vertical band.</summary>
    /// <param name="data">image data</param>
    /// <param name="pixw">width</param>
    /// <param name="pixh">height</param>
    /// <param name="depth">depth</param>
    /// <param name="wpl">wpl</param>
    /// <param name="x">x val of UL corner of rectangle</param>
    /// <param name="w">width of rectangle</param>
    /// <param name="shift">+ shifts data downward in vertical column</param>
    /// <remarks>
    /// (1) This clears the pixels that are left exposed after the
    ///     translation.  You can consider them as pixels that are
    ///     shifted in from outside the image.  This can be later
    ///     overridden by the incolor parameter in higher-level functions
    ///     that call this.  For example, for images with depth > 1,
    ///     these pixels are cleared to black; to be white they
    ///     must later be SET to white.  See, e.g., pixRasteropVip().
    /// (2) This function scales the width to accommodate any depth,
    ///     performs clipping, and then does the in-place rasterop.
    /// </remarks>
    internal static void rasteropVipLow(uint[] data, int pixw, int pixh, int depth, int wpl,
                                        int x, int w, int shift)
    {
        int fwpartb;    /* boolean (1, 0) if first word is partial */
        int fwpart2b;   /* boolean (1, 0) if first word is doubly partial */
        uint fwmask = 0;    /* mask for first partial word */
        int fwbits;     /* first word bits in ovrhang */
        int pdfwpart = 0;   /* index of first partial dest word */
        int psfwpart = 0;   /* index of first partial src word */
        int fwfullb;    /* boolean (1, 0) if there exists a full word */
        int nfullw;     /* number of full words */
        int pdfwfull = 0;   /* index of first full dest word */
        int psfwfull = 0;   /* index of first full src word */
        int lwpartb;    /* boolean (1, 0) if last word is partial */
        uint lwmask = 0;    /* mask for last partial word */
        int lwbits;     /* last word bits in ovrhang */
        int pdlwpart = 0;   /* index of last partial dest word */
        int pslwpart = 0;   /* index of last partial src word */
        int dirwpl;     /* directed wpl (-wpl * sign(shift)) */
        int absshift;   /* absolute value of shift; for use in iterator */
        int vlimit;     /* vertical limit value for iterations */
        int i, j;

       /*--------------------------------------------------------*
        *            Scale horizontal dimensions by depth        *
        *--------------------------------------------------------*/
        if (depth != 1) {
            pixw *= depth;
            x *= depth;
            w *= depth;
        }

       /*--------------------------------------------------------*
        *                   Clip horizontally                    *
        *--------------------------------------------------------*/
        if (x < 0) {
            w += x;    /* reduce w */
            x = 0;     /* clip to x = 0 */
        }
        if (x >= pixw || w <= 0)  /* no part of vertical slice is in the image */
            return;

        if (x + w > pixw)
            w = pixw - x;   /* clip to x + w = pixw */

        /*--------------------------------------------------------*
         *                Preliminary calculations                *
         *--------------------------------------------------------*/
            /* is the first word partial? */
        if ((x & 31) == 0) {  /* if not */
            fwpartb = 0;
            fwbits = 0;
        } else {  /* if so */
            fwpartb = 1;
            fwbits = 32 - (x & 31);
            fwmask = rmask32[fwbits];
            if (shift >= 0) { /* go up from bottom */
                pdfwpart = wpl * (pixh - 1) + (x >> 5);
                psfwpart = wpl * (pixh - 1 - shift) + (x >> 5);
            } else {  /* go down from top */
                pdfwpart = (x >> 5);
                psfwpart = -wpl * shift + (x >> 5);
            }
        }

            /* is the first word doubly partial? */
        if (w >= fwbits) {  /* if not */
            fwpart2b = 0;
        } else {  /* if so */
            fwpart2b = 1;
            fwmask &= lmask32[32 - fwbits + w];
        }

            /* is there a full dest word? */
        if (fwpart2b == 1) {  /* not */
            fwfullb = 0;
            nfullw = 0;
        } else {
            nfullw = (w - fwbits) >> 5;
            if (nfullw == 0) {  /* if not */
                fwfullb = 0;
            } else {  /* if so */
                fwfullb = 1;
                if (fwpartb != 0) {
                    pdfwfull = pdfwpart + 1;
                    psfwfull = psfwpart + 1;
                } else if (shift >= 0) { /* go up from bottom */
                    pdfwfull = wpl * (pixh - 1) + (x >> 5);
                    psfwfull = wpl * (pixh - 1 - shift) + (x >> 5);
                } else {  /* go down from top */
                    pdfwfull = (x >> 5);
                    psfwfull = -wpl * shift + (x >> 5);
                }
            }
        }

            /* is the last word partial? */
        lwbits = (x + w) & 31;
        if (fwpart2b == 1 || lwbits == 0) {  /* if not */
            lwpartb = 0;
        } else {
            lwpartb = 1;
            lwmask = lmask32[lwbits];
            if (fwpartb != 0) {
                pdlwpart = pdfwpart + 1 + nfullw;
                pslwpart = psfwpart + 1 + nfullw;
            } else if (shift >= 0) { /* go up from bottom */
                pdlwpart = wpl * (pixh - 1) + (x >> 5) + nfullw;
                pslwpart = wpl * (pixh - 1 - shift) + (x >> 5) + nfullw;
            } else {  /* go down from top */
                pdlwpart = (x >> 5) + nfullw;
                pslwpart = -wpl * shift + (x >> 5) + nfullw;
            }
        }

            /* determine the direction of flow from the shift
             * If the shift >= 0, data flows downard from src
             * to dest, starting at the bottom and working up.
             * If shift < 0, data flows upward from src to
             * dest, starting at the top and working down. */
        dirwpl = (shift >= 0) ? -wpl : wpl;
        absshift = (shift < 0) ? -shift : shift;  /* L_ABS */
        vlimit = (0 > pixh - absshift) ? 0 : pixh - absshift;  /* L_MAX */

        /*--------------------------------------------------------*
         *            Now we're ready to do the ops               *
         *--------------------------------------------------------*/

            /* Do the first partial word */
        if (fwpartb != 0) {
            for (i = 0; i < vlimit; i++) {
                data[pdfwpart] = COMBINE_PARTIAL(data[pdfwpart], data[psfwpart], fwmask);
                pdfwpart += dirwpl;
                psfwpart += dirwpl;
            }

                /* Clear the incoming pixels */
            for (i = vlimit; i < pixh; i++) {
                data[pdfwpart] = COMBINE_PARTIAL(data[pdfwpart], 0x0, fwmask);
                pdfwpart += dirwpl;
            }
        }

            /* Do the full words */
        if (fwfullb != 0) {
            for (i = 0; i < vlimit; i++) {
                for (j = 0; j < nfullw; j++)
                    data[pdfwfull + j] = data[psfwfull + j];
                pdfwfull += dirwpl;
                psfwfull += dirwpl;
            }

                /* Clear the incoming pixels */
            for (i = vlimit; i < pixh; i++) {
                for (j = 0; j < nfullw; j++)
                    data[pdfwfull + j] = 0x0;
                pdfwfull += dirwpl;
            }
        }

            /* Do the last partial word */
        if (lwpartb != 0) {
            for (i = 0; i < vlimit; i++) {
                data[pdlwpart] = COMBINE_PARTIAL(data[pdlwpart], data[pslwpart], lwmask);
                pdlwpart += dirwpl;
                pslwpart += dirwpl;
            }

                /* Clear the incoming pixels */
            for (i = vlimit; i < pixh; i++) {
                data[pdlwpart] = COMBINE_PARTIAL(data[pdlwpart], 0x0, lwmask);
                pdlwpart += dirwpl;
            }
        }
    }

    /*--------------------------------------------------------------------*
     *       Low level in-place full width horizontal block transfer      *
     *--------------------------------------------------------------------*/
    // leptonica: rasteropHipLow (roplow.c)
    /// <summary>In-place translation of a full-width horizontal band.</summary>
    /// <param name="data">image data</param>
    /// <param name="pixh">height</param>
    /// <param name="depth">depth</param>
    /// <param name="wpl">wpl</param>
    /// <param name="y">y val of UL corner of rectangle</param>
    /// <param name="h">height of rectangle</param>
    /// <param name="shift">+ shifts data to the left in a horizontal column</param>
    /// <remarks>
    /// (1) This clears the pixels that are left exposed after the rasterop.
    ///     Therefore, for Pix with depth > 1, these pixels become black,
    ///     and must be subsequently SET if they are to be white.
    ///     For example, see pixRasteropHip().
    /// (2) This function performs clipping and calls shiftDataHorizontalLow()
    ///     to do the in-place rasterop on each line.
    /// </remarks>
    internal static void rasteropHipLow(uint[] data, int pixh, int depth, int wpl,
                                        int y, int h, int shift)
    {
        int i;
        int line;

            /* clip band if necessary */
        if (y < 0) {
            h += y;  /* reduce h */
            y = 0;   /* clip to y = 0 */
        }
        if (h <= 0 || y > pixh)  /* no part of horizontal slice is in the image */
            return;

        if (y + h > pixh)
            h = pixh - y;   /* clip to y + h = pixh */

        for (i = y; i < y + h; i++) {
            line = i * wpl;
            shiftDataHorizontalLow(data, line, wpl, data, line, wpl, shift * depth);
        }
    }

    // leptonica: shiftDataHorizontalLow (roplow.c)
    /// <param name="datad">dest array</param>
    /// <param name="lined0">index of the beginning of the dest line</param>
    /// <param name="wpld">wpl of dest</param>
    /// <param name="datas">src array</param>
    /// <param name="lines0">index of the beginning of the src line</param>
    /// <param name="wpls">wpl of src</param>
    /// <param name="shift">horizontal shift of block; >0 is to right</param>
    /// <remarks>
    /// (1) This can also be used for in-place operation; see, e.g.,
    ///     rasteropHipLow().
    /// (2) We are clearing the pixels that are shifted in from
    ///     outside the image.  This can be overridden by the
    ///     incolor parameter in higher-level functions that call this.
    /// </remarks>
    // NOTE(upstream): if the shift is so large that firstdw >= wpld, the C code
    // touches words outside the line (here: IndexOutOfRangeException for the
    // first line).  Callers must keep |shift| below the line width.
    private static void shiftDataHorizontalLow(uint[] datad, int lined0, int wpld,
                                               uint[] datas, int lines0, int wpls, int shift)
    {
        int j, firstdw, wpl, rshift, lshift;
        int lined, lines;

        lined = lined0;
        lines = lines0;

        if (shift >= 0) {   /* src shift to right; data flows to
                             * right, starting at right edge and
                             * progressing leftward. */
            firstdw = shift / 32;
            wpl = (wpls < wpld - firstdw) ? wpls : wpld - firstdw;  /* L_MIN */
            lined += firstdw + wpl - 1;
            lines += wpl - 1;
            rshift = shift & 31;
            if (rshift == 0) {
                for (j = 0; j < wpl; j++)
                    datad[lined--] = datas[lines--];

                    /* clear out the rest to the left edge */
                for (j = 0; j < firstdw; j++)
                    datad[lined--] = 0;
            } else {
                lshift = 32 - rshift;
                for (j = 1; j < wpl; j++) {
                    datad[lined--] = datas[lines - 1] << lshift | datas[lines] >> rshift;
                    lines--;
                }
                datad[lined] = datas[lines] >> rshift;  /* partial first */

                    /* clear out the rest to the left edge */
                datad[lined] &= ~lmask32[rshift];
                lined--;
                for (j = 0; j < firstdw; j++)
                    datad[lined--] = 0;
            }
        } else {  /* src shift to left; data flows to left, starting
                 * at left edge and progressing rightward. */
            firstdw = (-shift) / 32;
            wpl = (wpls - firstdw < wpld) ? wpls - firstdw : wpld;  /* L_MIN */
            lines += firstdw;
            lshift = (-shift) & 31;
            if (lshift == 0) {
                for (j = 0; j < wpl; j++)
                    datad[lined++] = datas[lines++];

                    /* clear out the rest to the right edge */
                for (j = 0; j < firstdw; j++)
                    datad[lined++] = 0;
            } else {
                rshift = 32 - lshift;
                for (j = 1; j < wpl; j++) {
                    datad[lined++] = datas[lines] << lshift | datas[lines + 1] >> rshift;
                    lines++;
                }
                datad[lined] = datas[lines] << lshift;  /* partial last */

                    /* clear out the rest to the right edge */
                    /* first clear the lshift pixels of this partial word */
                datad[lined] &= ~rmask32[lshift];
                lined++;
                    /* then the remaining words to the right edge */
                for (j = 0; j < firstdw; j++)
                    datad[lined++] = 0;
            }
        }
    }
}
