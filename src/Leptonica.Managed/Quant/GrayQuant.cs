using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  grayquant.c: Thresholding and quantization of gray images.
 *
 *  Thresholding from 8 bpp to 1 bpp
 *    Floyd-Steinberg dithering to binary:
 *      pixDitherToBinary, pixDitherToBinarySpec
 *    Simple (pixelwise) binarization with fixed threshold:
 *      pixThresholdToBinary
 *    Binarization with variable threshold:
 *      pixVarThresholdToBinary
 *    Binarization by adaptive mapping:
 *      pixAdaptThresholdToBinary, pixAdaptThresholdToBinaryGen
 *    Generate a binary mask from pixels of particular values:
 *      pixGenerateMaskByValue, pixGenerateMaskByBand
 *
 *  Thresholding from 8 bpp to 2 bpp
 *    pixDitherTo2bpp, pixDitherTo2bppSpec, pixThresholdTo2bpp
 *
 *  Thresholding from 8 bpp to 4 bpp
 *    pixThresholdTo4bpp
 *
 *  Quantization on 8 bpp grayscale
 *    pixThresholdOn8bpp, pixThresholdGrayArb
 *
 *  Quantization tables
 *    makeGrayQuantIndexTable, makeGrayQuantTableArb
 *
 *  Thresholding from 32 bpp rgb to 1 bpp
 *    pixGenerateMaskByBand32, pixGenerateMaskByDiscr32
 *
 *  Histogram-based and colormap-based grayscale quantization
 *    pixGrayQuantFromHisto, pixGrayQuantFromCmap
 *
 *  Differences from C:
 *    - Flags documented as 1/0 (cmapflag, usecmap, inband, use_average,
 *      setblack, setwhite) are bool.
 *    - The line functions ditherToBinaryLineLow and thresholdToBinaryLineLow,
 *      which are extern in C, are internal here and take spans.
 *    - numaFillCmapFromHisto keeps going when the colormap is full, as in C:
 *      the failure of pixcmapAddColor is logged and only its result is kept.
 *    - pixDitherToBinaryLUT and its helpers (#if 0 in C) are not ported.
 *    - makeGrayQuantTableArb and the other l_ok functions throw instead of
 *      returning 1.
 *
 *  Stubs for callees in other files: pixRemoveColormap (pixconv.c),
 *  pixBackgroundNormSimple (adaptmap.c), pixGammaTRC (enhance.c).
 */

/// <summary>Constants from pix.h used by grayquant.c.</summary>
public static partial class L
{
    /* Clipping distances for dithering (pix.h) */
    /// <summary>Default distance to black below which no error is propagated when dithering to 1 bpp.</summary>
    public const int DEFAULT_CLIP_LOWER_1 = 10;
    /// <summary>Default distance to white above which no error is propagated when dithering to 1 bpp.</summary>
    public const int DEFAULT_CLIP_UPPER_1 = 10;
    /// <summary>Default distance to black below which no error is propagated when dithering to 2 bpp.</summary>
    public const int DEFAULT_CLIP_LOWER_2 = 5;
    /// <summary>Default distance to white above which no error is propagated when dithering to 2 bpp.</summary>
    public const int DEFAULT_CLIP_UPPER_2 = 5;

    /* Distance flags (pix.h) */
    /// <summary>L1 (Manhattan) distance, for example in color space.</summary>
    public const int L_MANHATTAN_DISTANCE = 1;
    /// <summary>L2 (Euclidean) distance.</summary>
    public const int L_EUCLIDEAN_DISTANCE = 2;
}

public sealed partial class Pix
{
    /*------------------------------------------------------------------*
     *             Binarization by Floyd-Steinberg dithering            *
     *------------------------------------------------------------------*/
    // leptonica: pixDitherToBinary (grayquant.c)
    /// <summary>Floyd-Steinberg dithering of an 8 bpp image to binary.</summary>
    /// <returns>pixd dithered binary</returns>
    /// <remarks>
    /// The Floyd-Steinberg error diffusion dithering algorithm
    /// binarizes an 8 bpp grayscale image to a threshold of 128.
    /// If a pixel has a value above 127, it is binarized to white
    /// and the excess below 255 is subtracted from three
    /// neighboring pixels in the fractions 3/8 to i, j+1,
    /// 3/8 to i+1, j) and 1/4 to (i+1,j+1, truncating to 0
    /// if necessary.  Likewise, if it the pixel has a value
    /// below 128, it is binarized to black and the excess above 0
    /// is added to the neighboring pixels, truncating to 255 if necessary.
    ///
    /// This function differs from straight dithering in that it allows
    /// clipping of grayscale to 0 or 255 if the values are
    /// sufficiently close, without distribution of the excess.
    /// This uses default values to specify the range of lower
    /// and upper values near 0 and 255, rsp that are clipped
    /// to black and white without propagating the excess.
    /// Not propagating the excess has the effect of reducing the
    /// snake patterns in parts of the image that are nearly black or white;
    /// however, it also prevents the attempt to reproduce gray for those values.
    ///
    /// The implementation is straightforward.  It uses a pair of
    /// line buffers to avoid changing pixs.  It is about the same speed
    /// as pixDitherToBinaryLUT(), which uses three LUTs.
    /// </remarks>
    public Pix DitherToBinary()
    {
        if (Depth != 8)
            throw Lept.Arg("pixDitherToBinary", "must be 8 bpp for dithering");

        return DitherToBinarySpec(L.DEFAULT_CLIP_LOWER_1, L.DEFAULT_CLIP_UPPER_1);
    }

    // leptonica: pixDitherToBinarySpec (grayquant.c)
    /// <summary>Floyd-Steinberg dithering to binary with given clipping distances.</summary>
    /// <param name="lowerclip">lower clip distance to black; use 0 for default</param>
    /// <param name="upperclip">upper clip distance to white; use 0 for default</param>
    /// <returns>pixd dithered binary</returns>
    /// <remarks>
    /// (1) See comments above in pixDitherToBinary() for details.
    /// (2) The input parameters lowerclip and upperclip specify the range
    ///     of lower and upper values (near 0 and 255, rsp) that are
    ///     clipped to black and white without propagating the excess.
    ///     For that reason, lowerclip and upperclip should be small numbers.
    /// </remarks>
    public Pix DitherToBinarySpec(int lowerclip, int upperclip)
    {
        const string func = "pixDitherToBinarySpec";
        GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "must be 8 bpp for dithering");
        if (lowerclip < 0 || lowerclip > 255)
            throw Lept.Arg(func, "invalid value for lowerclip");
        if (upperclip < 0 || upperclip > 255)
            throw Lept.Arg(func, "invalid value for upperclip");

        Pix pixd = Create(w, h, 1);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

            /* Remove colormap if it exists */
        Pix pixt = RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;

            /* Two line buffers, 1 for current line and 2 for next line */
        uint[] bufs1 = new uint[wplt];
        uint[] bufs2 = new uint[wplt];

        DitherToBinaryLow(datad, w, h, wpld, datat, wplt, bufs1, bufs2,
                          lowerclip, upperclip);
        return pixd;
    }

    // leptonica: ditherToBinaryLow (grayquant.c)
    /// <summary>See comments in pixDitherToBinary().</summary>
    private static void DitherToBinaryLow(uint[] datad, int w, int h, int wpld,
                                          uint[] datas, int wpls,
                                          uint[] bufs1, uint[] bufs2,
                                          int lowerclip, int upperclip)
    {
            /* do all lines except last line */
        datas.AsSpan(0, wpls).CopyTo(bufs2);  /* prime the buffer */
        for (int i = 0; i < h - 1; i++) {
            bufs2.AsSpan(0, wpls).CopyTo(bufs1);
            datas.AsSpan((i + 1) * wpls, wpls).CopyTo(bufs2);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            DitherToBinaryLineLow(lined, w, bufs1, bufs2, lowerclip, upperclip, 0);
        }

            /* do last line */
        bufs2.AsSpan(0, wpls).CopyTo(bufs1);
        Span<uint> lastd = datad.AsSpan((h - 1) * wpld, wpld);
        DitherToBinaryLineLow(lastd, w, bufs1, bufs2, lowerclip, upperclip, 1);
    }

    // leptonica: ditherToBinaryLineLow (grayquant.c)
    /// <summary>Floyd-Steinberg error diffusion dithering for a single line.</summary>
    /// <param name="lined">dest line</param>
    /// <param name="w">width of image in pixels</param>
    /// <param name="bufs1">buffer of current source line</param>
    /// <param name="bufs2">buffer of next source line</param>
    /// <param name="lowerclip">lower clip distance to black</param>
    /// <param name="upperclip">upper clip distance to white</param>
    /// <param name="lastlineflag">0 if not last dest line, 1 if last dest line</param>
    /// <remarks>
    /// Dispatches FS error diffusion dithering for
    /// a single line of the image.  If lastlineflag == 0,
    /// both source buffers are used; otherwise, only bufs1
    /// is used.  We use source buffers because the error
    /// is propagated into them, and we don't want to change
    /// the input src image.
    ///
    /// We break dithering out line by line to make it
    /// easier to combine functions like interpolative
    /// scaling and error diffusion dithering, as such a
    /// combination of operations obviates the need to
    /// generate a 2x grayscale image as an intermediary.
    /// </remarks>
    internal static void DitherToBinaryLineLow(Span<uint> lined, int w, Span<uint> bufs1,
                                               Span<uint> bufs2, int lowerclip,
                                               int upperclip, int lastlineflag)
    {
        if (BitConverter.IsLittleEndian && w >= 2 && (uint)lowerclip <= 255
            && (uint)upperclip <= 255 && lined.Length >= ((w + 31) >> 5)
            && bufs1.Length * 4 >= w && (lastlineflag != 0 || bufs2.Length * 4 >= w)) {
            DitherLineLut(lined, w, bufs1, bufs2, lowerclip, upperclip, lastlineflag);
            return;
        }
        DitherToBinaryLineLowScalar(lined, w, bufs1, bufs2, lowerclip, upperclip, lastlineflag);
    }

    // Per-clip lookup tables: signed error (3/8 and 1/4 parts) sent to the
    // neighbors for each source value.  Immutable once published.
    private sealed class DitherTables
    {
        public readonly int Lower, Upper;
        public readonly int[] D1 = new int[256];
        public readonly int[] D2 = new int[256];

        public DitherTables(int lower, int upper)
        {
            Lower = lower; Upper = upper;
            for (int v = 0; v < 256; v++) {
                if (v > 127) {
                    int e = 255 - v;
                    if (e > upper) { D1[v] = -((3 * e) / 8); D2[v] = -(e / 4); }
                } else if (v > lower) {
                    D1[v] = (3 * v) / 8; D2[v] = v / 4;
                }
            }
        }
    }

    private static DitherTables? s_ditherTables;

    // Same algorithm as the scalar version, with the error terms taken from
    // lookup tables so the loop-carried dependency is short and branch-free.
    // Pixel j lives in byte (j ^ 3) of the little-endian view of the word
    // buffers.  All buffer side effects of the scalar version are preserved.
    private static void DitherLineLut(Span<uint> lined, int w, Span<uint> bufs1,
                                             Span<uint> bufs2, int lowerclip,
                                             int upperclip, int lastlineflag)
    {
        DitherTables? t = s_ditherTables;
        if (t == null || t.Lower != lowerclip || t.Upper != upperclip)
            s_ditherTables = t = new DitherTables(lowerclip, upperclip);
        ref int d1 = ref MemoryMarshal.GetArrayDataReference(t.D1);
        ref int d2 = ref MemoryMarshal.GetArrayDataReference(t.D2);
        ref byte p1 = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(bufs1));
        ref byte p2 = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(bufs2));
        ref uint pd = ref MemoryMarshal.GetReference(lined);
        int r = Unsafe.Add(ref p1, 3);
        uint acc = 0;
        int wd = 0;

        if (lastlineflag == 0) {
            int b = Unsafe.Add(ref p2, 3);
            for (int j = 0; j < w - 1; j++) {
                int f1 = Unsafe.Add(ref d1, r);
                int f2 = Unsafe.Add(ref d2, r);
                acc = (acc << 1) | (uint)((r >> 7) ^ 1);

                Unsafe.Add(ref p2, j ^ 3) = (byte)Math.Clamp(b + f1, 0, 255);
                r = Math.Clamp(Unsafe.Add(ref p1, (j + 1) ^ 3) + f1, 0, 255);
                Unsafe.Add(ref p1, (j + 1) ^ 3) = (byte)r;
                b = Math.Clamp(Unsafe.Add(ref p2, (j + 1) ^ 3) + f2, 0, 255);

                if ((j & 31) == 31) {
                    Unsafe.Add(ref pd, wd++) |= acc;
                    acc = 0;
                }
            }

                /* last column: only the pixel below receives error */
            Unsafe.Add(ref p2, (w - 1) ^ 3) = (byte)Math.Clamp(b + Unsafe.Add(ref d1, r), 0, 255);
            acc = (acc << 1) | (uint)((r >> 7) ^ 1);
        } else {
            for (int j = 0; j < w - 1; j++) {
                int f1 = Unsafe.Add(ref d1, r);
                acc = (acc << 1) | (uint)((r >> 7) ^ 1);
                r = Math.Clamp(Unsafe.Add(ref p1, (j + 1) ^ 3) + f1, 0, 255);
                Unsafe.Add(ref p1, (j + 1) ^ 3) = (byte)r;

                if ((j & 31) == 31) {
                    Unsafe.Add(ref pd, wd++) |= acc;
                    acc = 0;
                }
            }
            acc = (acc << 1) | (uint)((r >> 7) ^ 1);   /* last pixel */
        }

            /* flush the remaining bits (pixels wd*32 .. w-1) */
        int rem = w - (wd << 5);
        Unsafe.Add(ref pd, wd) |= acc << (32 - rem);
    }

    internal static void DitherToBinaryLineLowScalar(Span<uint> lined, int w, Span<uint> bufs1,
                                                    Span<uint> bufs2, int lowerclip,
                                                    int upperclip, int lastlineflag)
    {
        int j;
        ref uint bufs1Ref = ref MemoryMarshal.GetReference(bufs1);
        ref uint bufs2Ref = ref MemoryMarshal.GetReference(bufs2);
        int oval, eval;
        int fval1, fval2, rval, bval, dval;

        if (lastlineflag == 0) {
            for (j = 0; j < w - 1; j++) {
                oval = GetDataByteUnchecked(ref bufs1Ref, j);
                if (oval > 127) {   /* binarize to OFF */
                    if ((eval = 255 - oval) > upperclip) {
                            /* subtract from neighbors */
                        fval1 = (3 * eval) / 8;
                        fval2 = eval / 4;
                        rval = GetDataByteUnchecked(ref bufs1Ref, j + 1);
                        rval = Math.Max(0, rval - fval1);
                        SetDataByteUnchecked(ref bufs1Ref, j + 1, rval);
                        bval = GetDataByteUnchecked(ref bufs2Ref, j);
                        bval = Math.Max(0, bval - fval1);
                        SetDataByteUnchecked(ref bufs2Ref, j, bval);
                        dval = GetDataByteUnchecked(ref bufs2Ref, j + 1);
                        dval = Math.Max(0, dval - fval2);
                        SetDataByteUnchecked(ref bufs2Ref, j + 1, dval);
                    }
                } else {   /* oval <= 127; binarize to ON  */
                    SetDataBit(lined, j);   /* ON pixel */
                    if (oval > lowerclip) {
                            /* add to neighbors */
                        fval1 = (3 * oval) / 8;
                        fval2 = oval / 4;
                        rval = GetDataByteUnchecked(ref bufs1Ref, j + 1);
                        rval = Math.Min(255, rval + fval1);
                        SetDataByteUnchecked(ref bufs1Ref, j + 1, rval);
                        bval = GetDataByteUnchecked(ref bufs2Ref, j);
                        bval = Math.Min(255, bval + fval1);
                        SetDataByteUnchecked(ref bufs2Ref, j, bval);
                        dval = GetDataByteUnchecked(ref bufs2Ref, j + 1);
                        dval = Math.Min(255, dval + fval2);
                        SetDataByteUnchecked(ref bufs2Ref, j + 1, dval);
                    }
                }
            }

                /* do last column: j = w - 1 */
            oval = GetDataByteUnchecked(ref bufs1Ref, j);
            if (oval > 127) {  /* binarize to OFF */
                if ((eval = 255 - oval) > upperclip) {
                        /* subtract from neighbors */
                    fval1 = (3 * eval) / 8;
                    bval = GetDataByteUnchecked(ref bufs2Ref, j);
                    bval = Math.Max(0, bval - fval1);
                    SetDataByteUnchecked(ref bufs2Ref, j, bval);
                }
            } else {  /*oval <= 127; binarize to ON */
                SetDataBit(lined, j);   /* ON pixel */
                if (oval > lowerclip) {
                        /* add to neighbors */
                    fval1 = (3 * oval) / 8;
                    bval = GetDataByteUnchecked(ref bufs2Ref, j);
                    bval = Math.Min(255, bval + fval1);
                    SetDataByteUnchecked(ref bufs2Ref, j, bval);
                }
            }
        } else {   /* lastlineflag == 1 */
            for (j = 0; j < w - 1; j++) {
                oval = GetDataByteUnchecked(ref bufs1Ref, j);
                if (oval > 127) {   /* binarize to OFF */
                    if ((eval = 255 - oval) > upperclip) {
                            /* subtract from neighbors */
                        fval1 = (3 * eval) / 8;
                        rval = GetDataByteUnchecked(ref bufs1Ref, j + 1);
                        rval = Math.Max(0, rval - fval1);
                        SetDataByteUnchecked(ref bufs1Ref, j + 1, rval);
                    }
                } else {   /* oval <= 127; binarize to ON  */
                    SetDataBit(lined, j);   /* ON pixel */
                    if (oval > lowerclip) {
                            /* add to neighbors */
                        fval1 = (3 * oval) / 8;
                        rval = GetDataByteUnchecked(ref bufs1Ref, j + 1);
                        rval = Math.Min(255, rval + fval1);
                        SetDataByteUnchecked(ref bufs1Ref, j + 1, rval);
                    }
                }
            }

                /* do last pixel: (i, j) = (h - 1, w - 1) */
            oval = GetDataByteUnchecked(ref bufs1Ref, j);
            if (oval < 128)
                SetDataBit(lined, j);   /* ON pixel */
        }
    }

    /*------------------------------------------------------------------*
     *       Simple (pixelwise) binarization with fixed threshold       *
     *------------------------------------------------------------------*/
    // leptonica: pixThresholdToBinary (grayquant.c)
    /// <summary>Thresholds a 4 or 8 bpp image to binary with a fixed threshold.</summary>
    /// <param name="thresh">threshold value</param>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// (1) If the source pixel is less than the threshold value,
    ///     the dest will be 1; otherwise, it will be 0.
    /// (2) For example, for 8 bpp src pix, if %thresh == 256, the dest
    ///     1 bpp pix is all ones (fg), and if %thresh == 0, the dest
    ///     pix is all zeros (bg).
    /// </remarks>
    public Pix ThresholdToBinary(int thresh)
    {
        const string func = "pixThresholdToBinary";
        GetDimensions(out int w, out int h, out int d);
        if (d != 4 && d != 8)
            throw Lept.Arg(func, "pixs must be 4 or 8 bpp");
        if (thresh < 0)
            throw Lept.Arg(func, "thresh must be non-negative");
        if (d == 4 && thresh > 16)
            throw Lept.Arg(func, "4 bpp thresh not in {0-16}");
        if (d == 8 && thresh > 256)
            throw Lept.Arg(func, "8 bpp thresh not in {0-256}");

        Pix pixd = Create(w, h, 1);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

            /* Remove colormap if it exists.  If there is a colormap,
             * pixt will be 8 bpp regardless of the depth of pixs. */
        Pix pixt = RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;
        if (Colormap != null && d == 4) {  /* promoted to 8 bpp */
            d = 8;
            thresh *= 16;
        }

        ThresholdToBinaryLow(datad, w, h, wpld, datat, d, wplt, thresh);
        return pixd;
    }

    // leptonica: thresholdToBinaryLow (grayquant.c)
    /// <summary>If the source pixel is less than thresh, the dest will be 1; otherwise, it will be 0.</summary>
    private static void ThresholdToBinaryLow(uint[] datad, int w, int h, int wpld,
                                             uint[] datas, int d, int wpls, int thresh)
    {
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            ThresholdToBinaryLineLow(lined, w, lines, d, thresh);
        }
    }

    // leptonica: thresholdToBinaryLineLow (grayquant.c)
    internal static void ThresholdToBinaryLineLow(Span<uint> lined, int w,
                                                  ReadOnlySpan<uint> lines, int d, int thresh)
    {
        int j, k, gval, scount, dcount;
        uint sword = 0, dword;

        switch (d)
        {
        case 4:
                /* Unrolled as 4 source words, 1 dest word */
            for (j = 0, scount = 0, dcount = 0; j + 31 < w; j += 32) {
                dword = 0;
                for (k = 0; k < 4; k++) {
                    sword = lines[scount++];
                    dword <<= 8;
                    gval = (int)((sword >> 28) & 0xf);
                        /* Trick used here and below: if gval < thresh then
                         * gval - thresh < 0, so its high-order bit is 1, and
                         * ((gval - thresh) >> 31) & 1 == 1; likewise, if
                         * gval >= thresh, then ((gval - thresh) >> 31) & 1 == 0
                         * Doing it this way avoids a random (and thus easily
                         * mispredicted) branch on each pixel. */
                    dword |= (uint)(((gval - thresh) >> 24) & 128);
                    gval = (int)((sword >> 24) & 0xf);
                    dword |= (uint)(((gval - thresh) >> 25) & 64);
                    gval = (int)((sword >> 20) & 0xf);
                    dword |= (uint)(((gval - thresh) >> 26) & 32);
                    gval = (int)((sword >> 16) & 0xf);
                    dword |= (uint)(((gval - thresh) >> 27) & 16);
                    gval = (int)((sword >> 12) & 0xf);
                    dword |= (uint)(((gval - thresh) >> 28) & 8);
                    gval = (int)((sword >> 8) & 0xf);
                    dword |= (uint)(((gval - thresh) >> 29) & 4);
                    gval = (int)((sword >> 4) & 0xf);
                    dword |= (uint)(((gval - thresh) >> 30) & 2);
                    gval = (int)(sword & 0xf);
                    dword |= (uint)(((gval - thresh) >> 31) & 1);
                }
                lined[dcount++] = dword;
            }

            if (j < w) {
                dword = 0;
                for (; j < w; j++) {
                    if ((j & 7) == 0) {
                        sword = lines[scount++];
                    }
                    gval = (int)((sword >> 28) & 0xf);
                    sword <<= 4;
                    dword |= (uint)((((gval - thresh) >> 31) & 1) << (31 - (j & 31)));
                }
                lined[dcount] = dword;
            }
            break;
        case 8:
            j = 0; scount = 0; dcount = 0;
            if (System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated
                && BitConverter.IsLittleEndian && w >= 32 && thresh >= 0 && thresh <= 255)
            {
                    /* SIMD: 16 pixels per compare.  Source bytes are reversed
                     * inside each 32-bit word on little-endian hosts, so a fixed
                     * shuffle puts pixel 0 into the most significant mask bit. */
                int nd = w >> 5;
                ReadOnlySpan<byte> sb = MemoryMarshal.AsBytes(lines);
                var tv = System.Runtime.Intrinsics.Vector128.Create((byte)thresh);
                var idx = System.Runtime.Intrinsics.Vector128.Create(
                    (byte)12, 13, 14, 15, 8, 9, 10, 11, 4, 5, 6, 7, 0, 1, 2, 3);
                for (int n = 0; n < nd; n++) {
                    var v0 = System.Runtime.Intrinsics.Vector128.Shuffle(
                        System.Runtime.Intrinsics.Vector128.LoadUnsafe(
                            ref MemoryMarshal.GetReference(sb), (nuint)(n * 32)), idx);
                    var v1 = System.Runtime.Intrinsics.Vector128.Shuffle(
                        System.Runtime.Intrinsics.Vector128.LoadUnsafe(
                            ref MemoryMarshal.GetReference(sb), (nuint)(n * 32 + 16)), idx);
                        /* pixel p of the 16 -> lane 15-p, so pixel 0 is the MSB */
                    uint m0 = System.Runtime.Intrinsics.Vector128.ExtractMostSignificantBits(
                        System.Runtime.Intrinsics.Vector128.LessThan(v0, tv));
                    uint m1 = System.Runtime.Intrinsics.Vector128.ExtractMostSignificantBits(
                        System.Runtime.Intrinsics.Vector128.LessThan(v1, tv));
                    lined[n] = (m0 << 16) | m1;
                }
                j = nd * 32; scount = nd * 8; dcount = nd;
            }
                /* Unrolled as 8 source words, 1 dest word */
            for (; j + 31 < w; j += 32) {
                dword = 0;
                for (k = 0; k < 8; k++) {
                    sword = lines[scount++];
                    dword <<= 4;
                    gval = (int)((sword >> 24) & 0xff);
                    dword |= (uint)(((gval - thresh) >> 28) & 8);
                    gval = (int)((sword >> 16) & 0xff);
                    dword |= (uint)(((gval - thresh) >> 29) & 4);
                    gval = (int)((sword >> 8) & 0xff);
                    dword |= (uint)(((gval - thresh) >> 30) & 2);
                    gval = (int)(sword & 0xff);
                    dword |= (uint)(((gval - thresh) >> 31) & 1);
                }
                lined[dcount++] = dword;
            }

            if (j < w) {
                dword = 0;
                for (; j < w; j++) {
                    if ((j & 3) == 0) {
                        sword = lines[scount++];
                    }
                    gval = (int)((sword >> 24) & 0xff);
                    sword <<= 8;
                    dword |= (uint)((ulong)(((gval - thresh) >> 31) & 1)
                                    << (31 - (j & 31)));
                }
                lined[dcount] = dword;
            }
            break;
        default:
            Log.Error("thresholdToBinaryLineLow", "src depth not 4 or 8 bpp");
            break;
        }
    }

    /*------------------------------------------------------------------*
     *                Binarization with variable threshold              *
     *------------------------------------------------------------------*/
    // leptonica: pixVarThresholdToBinary (grayquant.c)
    /// <summary>Thresholds an 8 bpp image to binary with a per-pixel threshold.</summary>
    /// <param name="pixg">8 bpp; contains threshold values for each pixel</param>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// (1) If the pixel in pixs is less than the corresponding pixel
    ///     in pixg, the dest will be 1; otherwise it will be 0.
    /// </remarks>
    public Pix VarThresholdToBinary(Pix pixg)
    {
        const string func = "pixVarThresholdToBinary";
        if (!SizesEqual(this, pixg))
            throw Lept.Arg(func, "pix sizes not equal");
        GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "pixs must be 8 bpp");

        Pix pixd = Create(w, h, 1);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        uint[] datas = Data;
        int wpls = Wpl;
        uint[] datag = pixg.Data;
        int wplg = pixg.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            ReadOnlySpan<uint> lineg = datag.AsSpan(i * wplg, wplg);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                int vals = GetDataByte(lines, j);
                int valg = GetDataByte(lineg, j);
                if (vals < valg)
                    SetDataBit(lined, j);
            }
        }

        return pixd;
    }

    /*------------------------------------------------------------------*
     *                  Binarization by adaptive mapping                *
     *------------------------------------------------------------------*/
    // leptonica: pixAdaptThresholdToBinary (grayquant.c)
    /// <summary>Adaptive thresholding of an 8 bpp image with variable background.</summary>
    /// <param name="pixm">[optional] 1 bpp image mask; can be null</param>
    /// <param name="gamma">gamma correction; must be &gt; 0.0; typically ~1.0</param>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// (1) This is a simple convenience function for doing adaptive
    ///     thresholding on a grayscale image with variable background.
    ///     It uses default parameters appropriate for typical text images.
    ///     Other high-level adaptive thresholding functions are
    ///     pixConvertTo1Adaptive() and pixCleanImage().
    /// (2) %pixm is a 1 bpp mask over "image" regions, which are not
    ///     expected to have a white background.  The mask inhibits
    ///     background finding under the fg pixels of the mask.  For
    ///     images with both text and image, the image regions would
    ///     be binarized (or quantized) by a different set of operations.
    /// (3) As %gamma is increased, the foreground pixels are reduced.
    /// (4) Under the covers:  The default background value for normalization
    ///     is 200, so we choose 170 for 'maxval' in pixGammaTRC.  Likewise,
    ///     the default foreground threshold for normalization is 60,
    ///     so we choose 50 for 'minval' in pixGammaTRC.  Because
    ///     170 was mapped to 255, choosing 200 for the threshold is
    ///     quite safe for avoiding speckle noise from the background.
    /// </remarks>
    public Pix AdaptThresholdToBinary(Pix? pixm, float gamma)
    {
        if (Depth != 8)
            throw Lept.Arg("pixAdaptThresholdToBinary", "pixs undefined or not 8 bpp");

        return AdaptThresholdToBinaryGen(pixm, gamma, 50, 170, 200);
    }

    // leptonica: pixAdaptThresholdToBinaryGen (grayquant.c)
    /// <summary>Adaptive thresholding with explicit black, white and threshold values.</summary>
    /// <param name="pixm">[optional] 1 bpp image mask; can be null</param>
    /// <param name="gamma">gamma correction; must be &gt; 0.0; typically ~1.0</param>
    /// <param name="blackval">dark value to set to black (0)</param>
    /// <param name="whiteval">light value to set to white (255)</param>
    /// <param name="thresh">final threshold for binarization</param>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// (1) This is a convenience function for doing adaptive thresholding
    ///     on a grayscale image with variable background.  Also see notes
    ///     in pixAdaptThresholdToBinary().
    /// (2) Reducing %gamma increases the foreground (text) pixels.
    ///     Use a low value (e.g., 0.5) for images with light text.
    /// (3) For normal images, see default args in pixAdaptThresholdToBinary().
    ///     For images with very light text, these values are appropriate:
    ///        gamma     ~0.5
    ///        blackval  ~70
    ///        whiteval  ~190
    ///        thresh    ~200
    /// </remarks>
    public Pix AdaptThresholdToBinaryGen(Pix? pixm, float gamma, int blackval,
                                         int whiteval, int thresh)
    {
        if (Depth != 8)
            throw Lept.Arg("pixAdaptThresholdToBinaryGen", "pixs undefined or not 8 bpp");

        Pix pix1 = BackgroundNormSimple(pixm, null);
        GammaTRC(pix1, pix1, gamma, blackval, whiteval);
        return pix1.ThresholdToBinary(thresh);
    }

    /*--------------------------------------------------------------------*
     *       Generate a binary mask from pixels of particular value(s)    *
     *--------------------------------------------------------------------*/
    // leptonica: pixGenerateMaskByValue (grayquant.c)
    /// <summary>Generates a 1 bpp mask of the pixels with a given value.</summary>
    /// <param name="val">of pixels for which we set 1 in dest</param>
    /// <param name="usecmap">true to retain cmap values; false to convert to gray</param>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// (1) %val is the pixel value that we are selecting.  It can be
    ///     either a gray value or a colormap index.
    /// (2) If pixs is colormapped, %usecmap determines if the colormap
    ///     index values are used, or if the colormap is removed to gray and
    ///     the gray values are used.  For the latter, it generates
    ///     an approximate grayscale value for each pixel, and then looks
    ///     for gray pixels with the value %val.
    /// </remarks>
    public Pix GenerateMaskByValue(int val, bool usecmap)
    {
        const string func = "pixGenerateMaskByValue";
        int d = Depth;
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "not 2, 4 or 8 bpp");

        Pix pixg;
        if (!usecmap && Colormap != null)
            pixg = RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        else
            pixg = Clone(this);
        pixg.GetDimensions(out int w, out int h, out d);
        if (d == 8 && (val < 0 || val > 255))
            throw Lept.Arg(func, "val out of 8 bpp range");
        if (d == 4 && (val < 0 || val > 15))
            throw Lept.Arg(func, "val out of 4 bpp range");
        if (d == 2 && (val < 0 || val > 3))
            throw Lept.Arg(func, "val out of 2 bpp range");

        Pix pixd = Create(w, h, 1);
        pixd.CopyResolution(pixg);
        pixd.CopyInputFormat(this);
        uint[] datag = pixg.Data;
        int wplg = pixg.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lineg = datag.AsSpan(i * wplg, wplg);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                if (d == 8) {
                    if (GetDataByte(lineg, j) == val)
                        SetDataBit(lined, j);
                } else if (d == 4) {
                    if (GetDataQbit(lineg, j) == val)
                        SetDataBit(lined, j);
                } else {  /* d == 2 */
                    if (GetDataDibit(lineg, j) == val)
                        SetDataBit(lined, j);
                }
            }
        }

        return pixd;
    }

    // leptonica: pixGenerateMaskByBand (grayquant.c)
    /// <summary>Generates a 1 bpp mask of the pixels inside or outside a band of values.</summary>
    /// <param name="lower">lower value of the band</param>
    /// <param name="upper">upper value of the band; the range, either
    ///                     between (inband) or outside of (!inband),
    ///                     determines which pixels in pixs cause us to
    ///                     set a 1 in the dest mask</param>
    /// <param name="inband">true for finding pixels in [lower, upper];
    ///                      false for finding pixels in
    ///                      [0, lower) union (upper, 255]</param>
    /// <param name="usecmap">true to retain cmap values; false to convert to gray</param>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// (1) Generates a 1 bpp mask pixd, the same size as pixs, where
    ///     the fg pixels in the mask are those either within the specified
    ///     band (for inband == 1) or outside the specified band
    ///     (for inband == 0).
    /// (2) If pixs is colormapped, %usecmap determines if the colormap
    ///     values are used, or if the colormap is removed to gray and
    ///     the gray values are used.  For the latter, it generates
    ///     an approximate grayscale value for each pixel, and then looks
    ///     for gray pixels with the value %val.
    /// </remarks>
    public Pix GenerateMaskByBand(int lower, int upper, bool inband, bool usecmap)
    {
        const string func = "pixGenerateMaskByBand";
        int d = Depth;
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "not 2, 4 or 8 bpp");
        if (lower < 0 || lower > upper)
            throw Lept.Arg(func, "lower < 0 or lower > upper!");

        Pix pixg;
        if (!usecmap && Colormap != null)
            pixg = RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        else
            pixg = Clone(this);
        pixg.GetDimensions(out int w, out int h, out d);
        if (d == 8 && upper > 255)
            throw Lept.Arg(func, "d == 8 and upper > 255");
        if (d == 4 && upper > 15)
            throw Lept.Arg(func, "d == 4 and upper > 15");
        if (d == 2 && upper > 3)
            throw Lept.Arg(func, "d == 2 and upper > 3");

        Pix pixd = Create(w, h, 1);
        pixd.CopyResolution(pixg);
        pixd.CopyInputFormat(this);
        uint[] datag = pixg.Data;
        int wplg = pixg.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lineg = datag.AsSpan(i * wplg, wplg);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                int val;
                if (d == 8)
                    val = GetDataByte(lineg, j);
                else if (d == 4)
                    val = GetDataQbit(lineg, j);
                else  /* d == 2 */
                    val = GetDataDibit(lineg, j);
                if (inband) {
                    if (val >= lower && val <= upper)
                        SetDataBit(lined, j);
                } else {  /* out of band */
                    if (val < lower || val > upper)
                        SetDataBit(lined, j);
                }
            }
        }

        return pixd;
    }

    /*------------------------------------------------------------------*
     *                Thresholding to 2 bpp by dithering                *
     *------------------------------------------------------------------*/
    // leptonica: pixDitherTo2bpp (grayquant.c)
    /// <summary>Floyd-Steinberg-like dithering of an 8 bpp image to 2 bpp.</summary>
    /// <param name="cmapflag">true to generate a colormap</param>
    /// <returns>pixd dithered 2 bpp</returns>
    /// <remarks>
    /// An analog of the Floyd-Steinberg error diffusion dithering
    /// algorithm is used to "dibitize" an 8 bpp grayscale image
    /// to 2 bpp, using equally spaced gray values of 0, 85, 170, and 255,
    /// which are served by thresholds of 43, 128 and 213.
    /// If cmapflag == 1, the colormap values are set to 0, 85, 170 and 255.
    /// If a pixel has a value between 0 and 42, it is dibitized
    /// to 0, and the excess above 0 is added to the
    /// three neighboring pixels, in the fractions 3/8 to i, j+1,
    /// 3/8 to i+1, j) and 1/4 to (i+1, j+1, truncating to 255 if
    /// necessary.  If a pixel has a value between 43 and 127, it is
    /// dibitized to 1, and the excess above 85 is added to the three
    /// neighboring pixels as before.  If the value is below 85, the
    /// excess is subtracted.  With a value between 128
    /// and 212, it is dibitized to 2, with the excess on either side
    /// of 170 distributed as before.  Finally, with a value between
    /// 213 and 255, it is dibitized to 3, with the excess below 255
    /// subtracted from the neighbors.  We always truncate to 0 or 255.
    /// The details can be seen in the lookup table generation.
    ///
    /// This function differs from straight dithering in that it allows
    /// clipping of grayscale to 0 or 255 if the values are
    /// sufficiently close, without distribution of the excess.
    /// This uses default values from pix.h to specify the range of lower
    /// and upper values near 0 and 255, rsp that are clipped to black
    /// and white without propagating the excess.
    /// Not propagating the excess has the effect of reducing the snake
    /// patterns in parts of the image that are nearly black or white;
    /// however, it also prevents any attempt to reproduce gray for those values.
    ///
    /// The implementation uses 3 lookup tables for simplicity, and
    /// a pair of line buffers to avoid modifying pixs.
    /// </remarks>
    public Pix DitherTo2bpp(bool cmapflag)
    {
        if (Depth != 8)
            throw Lept.Arg("pixDitherTo2bpp", "must be 8 bpp for dithering");

        return DitherTo2bppSpec(L.DEFAULT_CLIP_LOWER_2, L.DEFAULT_CLIP_UPPER_2, cmapflag);
    }

    // leptonica: pixDitherTo2bppSpec (grayquant.c)
    /// <summary>Dithering of an 8 bpp image to 2 bpp with given clipping distances.</summary>
    /// <param name="lowerclip">lower clip distance to black; use 0 for default</param>
    /// <param name="upperclip">upper clip distance to white; use 0 for default</param>
    /// <param name="cmapflag">true to generate a colormap</param>
    /// <returns>pixd dithered 2 bpp</returns>
    /// <remarks>
    /// (1) See comments above in pixDitherTo2bpp() for details.
    /// (2) The input parameters lowerclip and upperclip specify the range
    ///     of lower and upper values (near 0 and 255, rsp) that are
    ///     clipped to black and white without propagating the excess.
    ///     For that reason, lowerclip and upperclip should be small numbers.
    /// </remarks>
    public Pix DitherTo2bppSpec(int lowerclip, int upperclip, bool cmapflag)
    {
        const string func = "pixDitherTo2bppSpec";
        GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "must be 8 bpp for dithering");
        if (lowerclip < 0 || lowerclip > 255)
            throw Lept.Arg(func, "invalid value for lowerclip");
        if (upperclip < 0 || upperclip > 255)
            throw Lept.Arg(func, "invalid value for upperclip");

        Pix pixd = Create(w, h, 2);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

            /* If there is a colormap, remove it */
        Pix pixt = RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;

            /* Two line buffers, 1 for current line and 2 for next line */
        uint[] bufs1 = new uint[wplt];
        uint[] bufs2 = new uint[wplt];

            /* 3 lookup tables: 2-bit value, (3/8)excess, and (1/4)excess */
        Make8To2DitherTables(out int[] tabval, out int[] tab38, out int[] tab14,
                             lowerclip, upperclip);

        DitherTo2bppLow(datad, w, h, wpld, datat, wplt, bufs1, bufs2,
                        tabval, tab38, tab14);

        if (cmapflag) {
            PixColormap cmap = PixColormap.CreateLinear(2, 4);
            pixd.SetColormap(cmap);
        }

        return pixd;
    }

    // leptonica: ditherTo2bppLow (grayquant.c)
    /// <summary>
    /// Low-level function for doing Floyd-Steinberg error diffusion
    /// dithering from 8 bpp (datas) to 2 bpp (datad).  Two source
    /// line buffers, bufs1 and bufs2, are provided, along with three
    /// 256-entry lookup tables: tabval gives the output pixel value,
    /// tab38 gives the extra (plus or minus) transferred to the pixels
    /// directly to the left and below, and tab14 gives the extra
    /// transferred to the diagonal below.  The choice of 3/8 and 1/4
    /// is traditional but arbitrary when you use a lookup table; the
    /// only constraint is that the sum is 1.  See other comments
    /// below and in grayquant.c.
    /// </summary>
    private static void DitherTo2bppLow(uint[] datad, int w, int h, int wpld,
                                        uint[] datas, int wpls, uint[] bufs1, uint[] bufs2,
                                        int[] tabval, int[] tab38, int[] tab14)
    {
            /* do all lines except last line */
        datas.AsSpan(0, wpls).CopyTo(bufs2);  /* prime the buffer */
        for (int i = 0; i < h - 1; i++) {
            bufs2.AsSpan(0, wpls).CopyTo(bufs1);
            datas.AsSpan((i + 1) * wpls, wpls).CopyTo(bufs2);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            DitherTo2bppLineLow(lined, w, bufs1, bufs2, tabval, tab38, tab14, 0);
        }

            /* do last line */
        bufs2.AsSpan(0, wpls).CopyTo(bufs1);
        Span<uint> lastd = datad.AsSpan((h - 1) * wpld, wpld);
        DitherTo2bppLineLow(lastd, w, bufs1, bufs2, tabval, tab38, tab14, 1);
    }

    // leptonica: ditherTo2bppLineLow (grayquant.c)
    /// <summary>Error diffusion dithering to 2 bpp for a single line.</summary>
    /// <param name="lined">dest line</param>
    /// <param name="w">width of image in pixels</param>
    /// <param name="bufs1">buffer of current source line</param>
    /// <param name="bufs2">buffer of next source line</param>
    /// <param name="tabval">value to assign for current pixel</param>
    /// <param name="tab38">excess value to give to neighboring 3/8 pixels</param>
    /// <param name="tab14">excess value to give to neighboring 1/4 pixel</param>
    /// <param name="lastlineflag">0 if not last dest line, 1 if last dest line</param>
    /// <remarks>
    /// Dispatches error diffusion dithering for
    /// a single line of the image.  If lastlineflag == 0,
    /// both source buffers are used; otherwise, only bufs1
    /// is used.  We use source buffers because the error
    /// is propagated into them, and we don't want to change
    /// the input src image.
    ///
    /// We break dithering out line by line to make it
    /// easier to combine functions like interpolative
    /// scaling and error diffusion dithering, as such a
    /// combination of operations obviates the need to
    /// generate a 2x grayscale image as an intermediary.
    /// </remarks>
    private static void DitherTo2bppLineLow(Span<uint> lined, int w, Span<uint> bufs1,
                                            Span<uint> bufs2, int[] tabval, int[] tab38,
                                            int[] tab14, int lastlineflag)
    {
        int j;
        int oval, tab38val, tab14val;
        int rval, bval, dval;

        if (lastlineflag == 0) {
            for (j = 0; j < w - 1; j++) {
                oval = GetDataByte(bufs1, j);
                SetDataDibit(lined, j, tabval[oval]);
                rval = GetDataByte(bufs1, j + 1);
                bval = GetDataByte(bufs2, j);
                dval = GetDataByte(bufs2, j + 1);
                tab38val = tab38[oval];
                tab14val = tab14[oval];
                if (tab38val < 0) {
                    rval = Math.Max(0, rval + tab38val);
                    bval = Math.Max(0, bval + tab38val);
                    dval = Math.Max(0, dval + tab14val);
                } else {
                    rval = Math.Min(255, rval + tab38val);
                    bval = Math.Min(255, bval + tab38val);
                    dval = Math.Min(255, dval + tab14val);
                }
                SetDataByte(bufs1, j + 1, rval);
                SetDataByte(bufs2, j, bval);
                SetDataByte(bufs2, j + 1, dval);
            }

                /* do last column: j = w - 1 */
            oval = GetDataByte(bufs1, j);
            SetDataDibit(lined, j, tabval[oval]);
            bval = GetDataByte(bufs2, j);
            tab38val = tab38[oval];
            if (tab38val < 0)
                bval = Math.Max(0, bval + tab38val);
            else
                bval = Math.Min(255, bval + tab38val);
            SetDataByte(bufs2, j, bval);
        } else {   /* lastlineflag == 1 */
            for (j = 0; j < w - 1; j++) {
                oval = GetDataByte(bufs1, j);
                SetDataDibit(lined, j, tabval[oval]);
                rval = GetDataByte(bufs1, j + 1);
                tab38val = tab38[oval];
                if (tab38val < 0)
                    rval = Math.Max(0, rval + tab38val);
                else
                    rval = Math.Min(255, rval + tab38val);
                SetDataByte(bufs1, j + 1, rval);
            }

                /* do last pixel: (i, j) = (h - 1, w - 1) */
            oval = GetDataByte(bufs1, j);
            SetDataDibit(lined, j, tabval[oval]);
        }
    }

    // leptonica: make8To2DitherTables (grayquant.c)
    /// <param name="tabval">value assigned to output pixel; 0, 1, 2 or 3</param>
    /// <param name="tab38">amount propagated to pixels left and below</param>
    /// <param name="tab14">amount propagated to pixel to left and down</param>
    /// <param name="cliptoblack">values near 0 where the excess is not propagated</param>
    /// <param name="cliptowhite">values near 255 where the deficit is not propagated</param>
    internal static void Make8To2DitherTables(out int[] tabval, out int[] tab38, out int[] tab14,
                                             int cliptoblack, int cliptowhite)
    {
            /* 3 lookup tables: 2-bit value, (3/8)excess, and (1/4)excess */
        tabval = new int[256];
        tab38 = new int[256];
        tab14 = new int[256];

        for (int i = 0; i < 256; i++) {
            if (i <= cliptoblack) {
                tabval[i] = 0;
                tab38[i] = 0;
                tab14[i] = 0;
            } else if (i < 43) {
                tabval[i] = 0;
                tab38[i] = (3 * i + 4) / 8;
                tab14[i] = (i + 2) / 4;
            } else if (i < 85) {
                tabval[i] = 1;
                tab38[i] = (3 * (i - 85) - 4) / 8;
                tab14[i] = ((i - 85) - 2) / 4;
            } else if (i < 128) {
                tabval[i] = 1;
                tab38[i] = (3 * (i - 85) + 4) / 8;
                tab14[i] = ((i - 85) + 2) / 4;
            } else if (i < 170) {
                tabval[i] = 2;
                tab38[i] = (3 * (i - 170) - 4) / 8;
                tab14[i] = ((i - 170) - 2) / 4;
            } else if (i < 213) {
                tabval[i] = 2;
                tab38[i] = (3 * (i - 170) + 4) / 8;
                tab14[i] = ((i - 170) + 2) / 4;
            } else if (i < 255 - cliptowhite) {
                tabval[i] = 3;
                tab38[i] = (3 * (i - 255) - 4) / 8;
                tab14[i] = ((i - 255) - 2) / 4;
            } else {  /* i >= 255 - cliptowhite */
                tabval[i] = 3;
                tab38[i] = 0;
                tab14[i] = 0;
            }
        }
    }

    /*--------------------------------------------------------------------*
     *  Simple (pixelwise) thresholding to 2 bpp with optional colormap   *
     *--------------------------------------------------------------------*/
    // leptonica: pixThresholdTo2bpp (grayquant.c)
    /// <summary>Thresholds an 8 bpp image to 2 bpp with equally spaced levels.</summary>
    /// <param name="nlevels">equally spaced; must be between 2 and 4</param>
    /// <param name="cmapflag">true to build colormap; false otherwise</param>
    /// <returns>pixd 2 bpp, optionally with colormap</returns>
    /// <remarks>
    /// (1) Valid values for nlevels is the set {2, 3, 4}.
    /// (2) Any colormap on the input pixs is removed to 8 bpp grayscale.
    /// (3) This function is typically invoked with cmapflag == 1.
    ///     In the situation where no colormap is desired, nlevels is
    ///     ignored and pixs is thresholded to 4 levels.
    /// (4) The target output colors are equally spaced, with the
    ///     darkest at 0 and the lightest at 255.  The thresholds are
    ///     chosen halfway between adjacent output values.  A table
    ///     is built that specifies the mapping from src to dest.
    /// (5) If cmapflag == 1, a colormap of size 'nlevels' is made,
    ///     and the pixel values in pixs are replaced by their
    ///     appropriate color indices.  The number of holdouts,
    ///     4 - nlevels, will be between 0 and 2.
    /// (6) If you don't want the thresholding to be equally spaced,
    ///     either first transform the 8 bpp src using pixGammaTRC().
    ///     or, if cmapflag == 1, after calling this function you can use
    ///     pixcmapResetColor() to change any individual colors.
    /// (7) If a colormap is generated, it will specify (to display
    ///     programs) exactly how each level is to be represented in RGB
    ///     space.  When representing text, 3 levels is far better than
    ///     2 because of the antialiasing of the single gray level,
    ///     and 4 levels (black, white and 2 gray levels) is getting
    ///     close to the perceptual quality of a (nearly continuous)
    ///     grayscale image.  With 2 bpp, you can set up a colormap
    ///     and allocate from 2 to 4 levels to represent antialiased text.
    ///     Any left over colormap entries can be used for coloring regions.
    ///     For the same number of levels, the file size of a 2 bpp image
    ///     is about 10% smaller than that of a 4 bpp result for the same
    ///     number of levels.  For both 2 bpp and 4 bpp, using 4 levels you
    ///     get compression far better than that of jpeg, because the
    ///     quantization to 4 levels will remove the jpeg ringing in the
    ///     background near character edges.
    /// </remarks>
    public Pix ThresholdTo2bpp(int nlevels, bool cmapflag)
    {
        const string func = "pixThresholdTo2bpp";
        GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (nlevels < 2 || nlevels > 4)
            throw Lept.Arg(func, "nlevels not in {2, 3, 4}");

        Pix pixd = Create(w, h, 2);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        if (cmapflag) {   /* hold out (4 - nlevels) cmap entries */
            PixColormap cmap = PixColormap.CreateLinear(2, nlevels);
            pixd.SetColormap(cmap);
        }

            /* If there is a colormap in the src, remove it */
        Pix pixt = RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;

            /* Make the appropriate table */
        int[] qtab;
        if (cmapflag)
            qtab = MakeGrayQuantIndexTable(nlevels);
        else
            qtab = MakeGrayQuantTargetTable(4, 2);

        ThresholdTo2bppLow(datad, h, wpld, datat, wplt, qtab);
        return pixd;
    }

    // leptonica: thresholdTo2bppLow (grayquant.c)
    /// <summary>
    /// Low-level function for thresholding from 8 bpp (datas) to
    /// 2 bpp (datad), using thresholds implicitly defined through %tab,
    /// a 256-entry lookup table that gives a 2-bit output value
    /// for each possible input.
    ///
    /// For each line, unroll the loop so that for each 32 bit src word,
    /// representing four consecutive 8-bit pixels, we compose one byte
    /// of output consisiting of four 2-bit pixels.
    /// </summary>
    private static void ThresholdTo2bppLow(uint[] datad, int h, int wpld,
                                           uint[] datas, int wpls, int[] tab)
    {
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < wpls; j++) {
                int k = 4 * j;
                byte sval1 = (byte)GetDataByte(lines, k);
                byte sval2 = (byte)GetDataByte(lines, k + 1);
                byte sval3 = (byte)GetDataByte(lines, k + 2);
                byte sval4 = (byte)GetDataByte(lines, k + 3);
                byte dval = (byte)((tab[sval1] << 6) | (tab[sval2] << 4) |
                                   (tab[sval3] << 2) | tab[sval4]);
                SetDataByte(lined, j, dval);
            }
        }
    }

    /*----------------------------------------------------------------------*
     *               Simple (pixelwise) thresholding to 4 bpp               *
     *----------------------------------------------------------------------*/
    // leptonica: pixThresholdTo4bpp (grayquant.c)
    /// <summary>Thresholds an 8 bpp image to 4 bpp with equally spaced levels.</summary>
    /// <param name="nlevels">equally spaced; must be between 2 and 16</param>
    /// <param name="cmapflag">true to build colormap; false otherwise</param>
    /// <returns>pixd 4 bpp, optionally with colormap</returns>
    /// <remarks>
    /// (1) Valid values for nlevels is the set {2, ... 16}.
    /// (2) Any colormap on the input pixs is removed to 8 bpp grayscale.
    /// (3) This function is typically invoked with cmapflag == 1.
    ///     In the situation where no colormap is desired, nlevels is
    ///     ignored and pixs is thresholded to 16 levels.
    /// (4) The target output colors are equally spaced, with the
    ///     darkest at 0 and the lightest at 255.  The thresholds are
    ///     chosen halfway between adjacent output values.  A table
    ///     is built that specifies the mapping from src to dest.
    /// (5) If cmapflag == 1, a colormap of size 'nlevels' is made,
    ///     and the pixel values in pixs are replaced by their
    ///     appropriate color indices.  The number of holdouts,
    ///     16 - nlevels, will be between 0 and 14.
    /// (6) If you don't want the thresholding to be equally spaced,
    ///     either first transform the 8 bpp src using pixGammaTRC().
    ///     or, if cmapflag == 1, after calling this function you can use
    ///     pixcmapResetColor() to change any individual colors.
    /// (7) If a colormap is generated, it will specify, to display
    ///     programs, exactly how each level is to be represented in RGB
    ///     space.  When representing text, 3 levels is far better than
    ///     2 because of the antialiasing of the single gray level,
    ///     and 4 levels (black, white and 2 gray levels) is getting
    ///     close to the perceptual quality of a (nearly continuous)
    ///     grayscale image.  Therefore, with 4 bpp, you can set up a
    ///     colormap, allocate a relatively small fraction of the 16
    ///     possible values to represent antialiased text, and use the
    ///     other colormap entries for other things, such as coloring
    ///     text or background.  Two other reasons for using a small number
    ///     of gray values for antialiased text are (1) PNG compression
    ///     gets worse as the number of levels that are used is increased,
    ///     and (2) using a small number of levels will filter out most of
    ///     the jpeg ringing that is typically introduced near sharp edges
    ///     of text.  This filtering is partly responsible for the improved
    ///     compression.
    /// </remarks>
    public Pix ThresholdTo4bpp(int nlevels, bool cmapflag)
    {
        const string func = "pixThresholdTo4bpp";
        GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (nlevels < 2 || nlevels > 16)
            throw Lept.Arg(func, "nlevels not in [2,...,16]");

        Pix pixd = Create(w, h, 4);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        if (cmapflag) {   /* hold out (16 - nlevels) cmap entries */
            PixColormap cmap = PixColormap.CreateLinear(4, nlevels);
            pixd.SetColormap(cmap);
        }

            /* If there is a colormap in the src, remove it */
        Pix pixt = RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;

            /* Make the appropriate table */
        int[] qtab;
        if (cmapflag)
            qtab = MakeGrayQuantIndexTable(nlevels);
        else
            qtab = MakeGrayQuantTargetTable(16, 4);

        ThresholdTo4bppLow(datad, h, wpld, datat, wplt, qtab);
        return pixd;
    }

    // leptonica: thresholdTo4bppLow (grayquant.c)
    /// <summary>
    /// Low-level function for thresholding from 8 bpp (datas) to
    /// 4 bpp (datad), using thresholds implicitly defined through %tab,
    /// a 256-entry lookup table that gives a 4-bit output value
    /// for each possible input.
    ///
    /// For each line, unroll the loop so that for each 32 bit src word,
    /// representing four consecutive 8-bit pixels, we compose two bytes
    /// of output consisiting of four 4-bit pixels.
    /// </summary>
    private static void ThresholdTo4bppLow(uint[] datad, int h, int wpld,
                                           uint[] datas, int wpls, int[] tab)
    {
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < wpls; j++) {
                int k = 4 * j;
                byte sval1 = (byte)GetDataByte(lines, k);
                byte sval2 = (byte)GetDataByte(lines, k + 1);
                byte sval3 = (byte)GetDataByte(lines, k + 2);
                byte sval4 = (byte)GetDataByte(lines, k + 3);
                ushort dval = (ushort)((tab[sval1] << 12) | (tab[sval2] << 8) |
                                       (tab[sval3] << 4) | tab[sval4]);
                SetDataTwoBytes(lined, j, dval);
            }
        }
    }

    /*----------------------------------------------------------------------*
     *    Simple (pixelwise) thresholding on 8 bpp with optional colormap   *
     *----------------------------------------------------------------------*/
    // leptonica: pixThresholdOn8bpp (grayquant.c)
    /// <summary>Quantizes an 8 bpp image to equally spaced levels, with an optional colormap.</summary>
    /// <param name="nlevels">equally spaced; must be between 2 and 256</param>
    /// <param name="cmapflag">true to build colormap; false otherwise</param>
    /// <returns>pixd 8 bpp, optionally with colormap</returns>
    /// <remarks>
    /// (1) Valid values for nlevels is the set {2,...,256}.
    /// (2) Any colormap on the input pixs is removed to 8 bpp grayscale.
    /// (3) If cmapflag == 1, a colormap of size 'nlevels' is made,
    ///     and the pixel values in pixs are replaced by their
    ///     appropriate color indices.  Otherwise, the pixel values
    ///     are the actual thresholded (i.e., quantized) grayscale values.
    /// (4) If you don't want the thresholding to be equally spaced,
    ///     first transform the input 8 bpp src using pixGammaTRC().
    /// </remarks>
    public Pix ThresholdOn8bpp(int nlevels, bool cmapflag)
    {
        const string func = "pixThresholdOn8bpp";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (nlevels < 2 || nlevels > 256)
            throw Lept.Arg(func, "nlevels not in [2,...,256]");

            /* Get a new pixd; if there is a colormap in the src, remove it */
        Pix pixd;
        if (Colormap != null)
            pixd = RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        else
            pixd = Copy(null, this);

        if (cmapflag) {   /* hold out (256 - nlevels) cmap entries */
            PixColormap cmap = PixColormap.CreateLinear(8, nlevels);
            pixd.SetColormap(cmap);
        }

        int[] qtab;  /* quantization table */
        if (cmapflag)
            qtab = MakeGrayQuantIndexTable(nlevels);
        else
            qtab = MakeGrayQuantTargetTable(nlevels, 8);

        pixd.GetDimensions(out int w, out int h, out _);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                int val = GetDataByte(lined, j);
                int newval = qtab[val];
                SetDataByte(lined, j, newval);
            }
        }

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *    Arbitrary (pixelwise) thresholding from 8 bpp to 2, 4 or 8 bpp    *
     *----------------------------------------------------------------------*/
    // leptonica: pixThresholdGrayArb (grayquant.c)
    /// <summary>Thresholds an 8 bpp image to 2, 4 or 8 bpp with arbitrary bin edges.</summary>
    /// <param name="edgevals">string giving edge value of each bin</param>
    /// <param name="outdepth">0, 2, 4 or 8 bpp; 0 is default for min depth</param>
    /// <param name="use_average">true if use the average pixel value in colormap</param>
    /// <param name="setblack">true if darkest color is set to black</param>
    /// <param name="setwhite">true if lightest color is set to white</param>
    /// <returns>pixd 2, 4 or 8 bpp quantized image with colormap</returns>
    /// <remarks>
    /// (1) This function allows exact specification of the quantization bins.
    ///     The string %edgevals is a space-separated set of values
    ///     specifying the dividing points between output quantization bins.
    ///     These threshold values are assigned to the bin with higher
    ///     values, so that each of them is the smallest value in their bin.
    /// (2) The output image (pixd) depth is specified by %outdepth.  The
    ///     number of bins is the number of edgevals + 1.  The
    ///     relation between outdepth and the number of bins is:
    ///          outdepth = 2       nbins &lt;= 4
    ///          outdepth = 4       nbins &lt;= 16
    ///          outdepth = 8       nbins &lt;= 256
    ///     With %outdepth == 0, the minimum required depth for the
    ///     given number of bins is used.
    ///     The output pixd has a colormap.
    /// (3) The last 3 args determine the specific values that go into
    ///     the colormap.
    /// (4) For %use_average:
    ///       ~ if TRUE, the average value of pixels falling in the bin is
    ///         chosen as the representative gray value.  Otherwise,
    ///       ~ if FALSE, the central value of each bin is chosen as
    ///         the representative value.
    ///     The colormap holds the representative value.
    /// (5) For %setblack, if TRUE the darkest color is set to (0,0,0).
    /// (6) For %setwhite, if TRUE the lightest color is set to (255,255,255).
    /// (7) An alternative to using this function to quantize to
    ///     unequally-spaced bins is to first transform the 8 bpp pixs
    ///     using pixGammaTRC(), and follow this with pixThresholdTo4bpp().
    /// </remarks>
    public Pix ThresholdGrayArb(string edgevals, int outdepth, bool use_average,
                                bool setblack, bool setwhite)
    {
        const string func = "pixThresholdGrayArb";
        GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (outdepth != 0 && outdepth != 2 && outdepth != 4 && outdepth != 8)
            throw Lept.Arg(func, "invalid outdepth");

            /* Parse and sort (if required) the bin edge values */
        Numa na = L_Kernel.ParseStringForNumbers(edgevals, " \t\n,");
        int n = na.Count;
        if (n > 255)
            throw Lept.Arg(func, "more than 256 levels");
        if (outdepth == 0) {
            if (n <= 3)
                outdepth = 2;
            else if (n <= 15)
                outdepth = 4;
            else
                outdepth = 8;
        } else if (n + 1 > (1 << outdepth)) {
            Log.Warning(func, "outdepth too small; setting to 8 bpp");
            outdepth = 8;
        }
        Numa.Sort(na, na, L.L_SORT_INCREASING);

            /* Make the quantization LUT and the colormap */
        MakeGrayQuantTableArb(na, outdepth, out int[] qtab, out PixColormap cmap);
        if (use_average) {  /* use the average value in each bin */
            cmap = MakeGrayQuantColormapArb(this, qtab, outdepth);
        }
        cmap.SetBlackAndWhite(setblack, setwhite);

        Pix pixd = Create(w, h, outdepth);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        pixd.SetColormap(cmap);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

            /* If there is a colormap in the src, remove it */
        Pix pixt = RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;

        if (outdepth == 2) {
            ThresholdTo2bppLow(datad, h, wpld, datat, wplt, qtab);
        } else if (outdepth == 4) {
            ThresholdTo4bppLow(datad, h, wpld, datat, wplt, qtab);
        } else {
            for (int i = 0; i < h; i++) {
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                ReadOnlySpan<uint> linet = datat.AsSpan(i * wplt, wplt);
                for (int j = 0; j < w; j++) {
                    int val = GetDataByte(linet, j);
                    int newval = qtab[val];
                    SetDataByte(lined, j, newval);
                }
            }
        }

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *     Quantization tables for linear thresholds of grayscale images    *
     *----------------------------------------------------------------------*/
    // leptonica: makeGrayQuantIndexTable (grayquant.c)
    /// <summary>Makes the table that maps input gray level to colormap index.</summary>
    /// <param name="nlevels">number of output levels</param>
    /// <returns>table maps input gray level to colormap index</returns>
    /// <remarks>
    /// (1) 'nlevels' is some number between 2 and 256 (typically 8 or less).
    /// (2) The table is typically used for quantizing 2, 4 and 8 bpp
    ///     grayscale src pix, and generating a colormapped dest pix.
    /// </remarks>
    public static int[] MakeGrayQuantIndexTable(int nlevels)
    {
        int[] tab = new int[256];
        for (int i = 0; i < 256; i++) {
            for (int j = 0; j < nlevels; j++) {
                int thresh = 255 * (2 * j + 1) / (2 * nlevels - 2);
                if (i <= thresh) {
                    tab[i] = j;
                    break;
                }
            }
        }
        return tab;
    }

    // leptonica: makeGrayQuantTargetTable (grayquant.c)
    /// <param name="nlevels">number of output levels</param>
    /// <param name="depth">of dest pix, in bpp; 2, 4 or 8 bpp</param>
    /// <returns>table maps input gray level to thresholded gray level</returns>
    /// <remarks>
    /// (1) nlevels is some number between 2 and 2^(depth)
    /// (2) The table is used in two similar ways:
    ///      ~ for 8 bpp, it quantizes to a given number of target levels
    ///      ~ for 2 and 4 bpp, it thresholds to appropriate target values
    ///        that will use the full dynamic range of the dest pix.
    /// (3) For depth = 8, the number of thresholds chosen is
    ///     ('nlevels' - 1), and the 'nlevels' values stored in the
    ///     table are at the two at the extreme ends, (0, 255), plus
    ///     plus ('nlevels' - 2) values chosen at equal intervals between.
    ///     For example, for depth = 8 and 'nlevels' = 3, the two
    ///     threshold values are 3f and bf, and the three target pixel
    ///     values are 0, 7f and ff.
    /// (4) For depth &lt; 8, we ignore nlevels, and always use the maximum
    ///     number of levels, which is 2^(depth).
    ///     If you want nlevels &lt; the maximum number, you should always
    ///     use a colormap.
    /// </remarks>
    internal static int[] MakeGrayQuantTargetTable(int nlevels, int depth)
    {
        int[] tab = new int[256];
        int maxval = (1 << depth) - 1;
        if (depth < 8)
            nlevels = 1 << depth;
        for (int i = 0; i < 256; i++) {
            for (int j = 0; j < nlevels; j++) {
                int thresh = 255 * (2 * j + 1) / (2 * nlevels - 2);
                if (i <= thresh) {
                    int quantval = maxval * j / (nlevels - 1);
                    tab[i] = quantval;
                    break;
                }
            }
        }
        return tab;
    }

    /*----------------------------------------------------------------------*
     *   Quantization table for arbitrary thresholding of grayscale images  *
     *----------------------------------------------------------------------*/
    // leptonica: makeGrayQuantTableArb (grayquant.c)
    /// <summary>Makes the table and the colormap for quantization to arbitrary bins.</summary>
    /// <param name="na">numa of bin boundaries</param>
    /// <param name="outdepth">of colormap: 1, 2, 4 or 8</param>
    /// <param name="tab">table mapping input gray level to cmap index</param>
    /// <param name="cmap">colormap</param>
    /// <remarks>
    /// (1) The number of bins is the count of %na + 1.
    /// (2) The bin boundaries in na must be sorted in increasing order.
    /// (3) The table is an inverse colormap: it maps input gray level
    ///     to colormap index (the bin number).
    /// (4) The colormap generated here has quantized values at the
    ///     center of each bin.  If you want to use the average gray
    ///     value of pixels within the bin, discard the colormap and
    ///     compute it using makeGrayQuantColormapArb().
    /// (5) Returns an error if there are not enough levels in the
    ///     output colormap for the number of bins.  The number
    ///     of bins must not exceed 2^outdepth.
    /// </remarks>
    public static void MakeGrayQuantTableArb(Numa na, int outdepth, out int[] tab,
                                             out PixColormap cmap)
    {
        const string func = "makeGrayQuantTableArb";
        tab = Array.Empty<int>();
        cmap = null!;
        int n = na.Count;
        if (n + 1 > (1 << outdepth))
            throw Lept.Arg(func, "more bins than cmap levels");

        cmap = PixColormap.Create(outdepth);
        tab = new int[256];

            /* First n bins */
        int jstart = 0;
        for (int i = 0; i < n; i++) {
            int val = na.GetIValue(i);
            int ave = (jstart + val) / 2;
            cmap.AddColor(ave, ave, ave);
            for (int j = jstart; j < val; j++)
                tab[j] = i;
            jstart = val;
        }

            /* Last bin */
        int lastave = (jstart + 255) / 2;
        cmap.AddColor(lastave, lastave, lastave);
        for (int j = jstart; j < 256; j++)
            tab[j] = n;
    }

    // leptonica: makeGrayQuantColormapArb (grayquant.c)
    /// <param name="pixs">8 bpp</param>
    /// <param name="tab">table mapping input gray level to cmap index</param>
    /// <param name="outdepth">of colormap: 1, 2, 4 or 8</param>
    /// <returns>colormap</returns>
    /// <remarks>
    /// (1) The table is a 256-entry inverse colormap: it maps input gray
    ///     level to colormap index (the bin number).  It is computed
    ///     using makeGrayQuantTableArb().
    /// (2) The colormap generated here has quantized values at the
    ///     average gray value of the pixels that are in each bin.
    /// (3) Returns an error if there are not enough levels in the
    ///     output colormap for the number of bins.  The number
    ///     of bins must not exceed 2^outdepth.
    /// </remarks>
    private static PixColormap MakeGrayQuantColormapArb(Pix pixs, int[] tab, int outdepth)
    {
        const string func = "makeGrayQuantColormapArb";
        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        int nbins = tab[255] + 1;
        if (nbins > (1 << outdepth))
            throw Lept.Arg(func, "more bins than cmap levels");

            /* Find the count and weighted count for each bin */
        int[] bincount = new int[nbins];
        int[] binave = new int[nbins];
        int factor = (int)(Math.Sqrt((double)(w * h) / 30000.0) + 0.5);
        factor = Math.Max(1, factor);
        uint[] data = pixs.Data;
        int wpl = pixs.Wpl;
        for (int i = 0; i < h; i += factor) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w; j += factor) {
                int val = GetDataByte(line, j);
                bincount[tab[val]]++;
                binave[tab[val]] += val;
            }
        }

            /* Find the smallest gray values in each bin */
        int[] binstart = new int[nbins];
        for (int i = 1, index = 1; i < 256; i++) {
            if (tab[i] < index) continue;
            if (tab[i] == index)
                binstart[index++] = i;
        }

            /* Get the averages.  If there are no samples in a bin, use
             * the center value of the bin. */
        PixColormap cmap = PixColormap.Create(outdepth);
        for (int i = 0; i < nbins; i++) {
            int val;
            if (bincount[i] != 0) {
                val = binave[i] / bincount[i];
            } else {  /* no samples in the bin */
                if (i < nbins - 1)
                    val = (binstart[i] + binstart[i + 1]) / 2;
                else  /* last bin */
                    val = (binstart[i] + 255) / 2;
            }
            cmap.AddColor(val, val, val);
        }

        return cmap;
    }

    /*--------------------------------------------------------------------*
     *                 Thresholding from 32 bpp rgb to 1 bpp              *
     *--------------------------------------------------------------------*/
    // leptonica: pixGenerateMaskByBand32 (grayquant.c)
    /// <summary>Generates a 1 bpp mask of the pixels within a band of rgb values.</summary>
    /// <param name="refval">reference rgb value</param>
    /// <param name="delm">max amount below the ref value for any component</param>
    /// <param name="delp">max amount above the ref value for any component</param>
    /// <param name="fractm">fractional amount below ref value for all components</param>
    /// <param name="fractp">fractional amount above ref value for all components</param>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// (1) Generates a 1 bpp mask pixd, the same size as pixs, where
    ///     the fg pixels in the mask within a band of rgb values
    ///     surrounding %refval.  The band can be chosen in two ways
    ///     for each component:
    ///     (a) Use (%delm, %delp) to specify how many levels down and up
    ///     (b) Use (%fractm, %fractp) to specify the fractional
    ///         distance toward 0 and 255, respectively.
    ///     Note that %delm and %delp must be in [0 ... 255], whereas
    ///     %fractm and %fractp must be in [0.0 - 1.0].
    /// (2) Either (%delm, %delp) or (%fractm, %fractp) can be used.
    ///     Set each value in the other pair to 0.
    /// </remarks>
    public Pix GenerateMaskByBand32(uint refval, int delm, int delp, float fractm, float fractp)
    {
        const string func = "pixGenerateMaskByBand32";
        GetDimensions(out int w, out int h, out int d);
        if (d != 32)
            throw Lept.Arg(func, "not 32 bpp");
        if (delm < 0 || delp < 0)
            throw Lept.Arg(func, "delm and delp must be >= 0");
        if (fractm < 0.0 || fractm > 1.0 || fractp < 0.0 || fractp > 1.0)
            throw Lept.Arg(func, "fractm and/or fractp invalid");

        int rmin, gmin, bmin, rmax, gmax, bmax;
        ExtractRGBValues(refval, out int rref, out int gref, out int bref);
        if (fractm == 0.0 && fractp == 0.0) {
            rmin = rref - delm;
            gmin = gref - delm;
            bmin = bref - delm;
            rmax = rref + delm;
            gmax = gref + delm;
            bmax = bref + delm;
        } else if (delm == 0 && delp == 0) {
            rmin = (int)((1.0 - fractm) * rref);
            gmin = (int)((1.0 - fractm) * gref);
            bmin = (int)((1.0 - fractm) * bref);
            rmax = rref + (int)(fractp * (255 - rref));
            gmax = gref + (int)(fractp * (255 - gref));
            bmax = bref + (int)(fractp * (255 - bref));
        } else {
            throw Lept.Arg(func, "bad input: either (delm, delp) or (fractm, fractp) " +
                                 "must be 0");
        }

        Pix pixd = Create(w, h, 1);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datas = Data;
        int wpls = Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                uint pixel = lines[j];
                int rval = (int)((pixel >> L.L_RED_SHIFT) & 0xff);
                if (rval < rmin || rval > rmax)
                    continue;
                int gval = (int)((pixel >> L.L_GREEN_SHIFT) & 0xff);
                if (gval < gmin || gval > gmax)
                    continue;
                int bval = (int)((pixel >> L.L_BLUE_SHIFT) & 0xff);
                if (bval < bmin || bval > bmax)
                    continue;
                SetDataBit(lined, j);
            }
        }

        return pixd;
    }

    // leptonica: pixGenerateMaskByDiscr32 (grayquant.c)
    /// <summary>Generates a 1 bpp mask of the pixels closer to refval1 than to refval2.</summary>
    /// <param name="refval1">reference rgb value</param>
    /// <param name="refval2">reference rgb value</param>
    /// <param name="distflag">L_MANHATTAN_DISTANCE, L_EUCLIDEAN_DISTANCE</param>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// (1) Generates a 1 bpp mask pixd, the same size as pixs, where
    ///     the fg pixels in the mask are those where the pixel in pixs
    ///     is "closer" to refval1 than to refval2.
    /// (2) "Closer" can be defined in several ways, such as:
    ///       ~ manhattan distance (L1)
    ///       ~ euclidean distance (L2)
    ///       ~ majority vote of the individual components
    ///     Here, we have a choice of L1 or L2.
    /// </remarks>
    public Pix GenerateMaskByDiscr32(uint refval1, uint refval2, int distflag)
    {
        const string func = "pixGenerateMaskByDiscr32";
        GetDimensions(out int w, out int h, out int d);
        if (d != 32)
            throw Lept.Arg(func, "not 32 bpp");
        if (distflag != L.L_MANHATTAN_DISTANCE && distflag != L.L_EUCLIDEAN_DISTANCE)
            throw Lept.Arg(func, "invalid distflag");

        ExtractRGBValues(refval1, out int rref1, out int gref1, out int bref1);
        ExtractRGBValues(refval2, out int rref2, out int gref2, out int bref2);
        Pix pixd = Create(w, h, 1);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datas = Data;
        int wpls = Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                uint pixel = lines[j];
                ExtractRGBValues(pixel, out int rval, out int gval, out int bval);
                uint dist1, dist2;
                if (distflag == L.L_MANHATTAN_DISTANCE) {
                    dist1 = (uint)Math.Abs(rref1 - rval);
                    dist2 = (uint)Math.Abs(rref2 - rval);
                    dist1 += (uint)Math.Abs(gref1 - gval);
                    dist2 += (uint)Math.Abs(gref2 - gval);
                    dist1 += (uint)Math.Abs(bref1 - bval);
                    dist2 += (uint)Math.Abs(bref2 - bval);
                } else {
                    dist1 = (uint)((rref1 - rval) * (rref1 - rval));
                    dist2 = (uint)((rref2 - rval) * (rref2 - rval));
                    dist1 += (uint)((gref1 - gval) * (gref1 - gval));
                    dist2 += (uint)((gref2 - gval) * (gref2 - gval));
                    dist1 += (uint)((bref1 - bval) * (bref1 - bval));
                    dist2 += (uint)((bref2 - bval) * (bref2 - bval));
                }
                if (dist1 < dist2)
                    SetDataBit(lined, j);
            }
        }

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *                Histogram-based grayscale quantization                *
     *----------------------------------------------------------------------*/
    // leptonica: pixGrayQuantFromHisto (grayquant.c)
    /// <summary>Quantizes an 8 bpp image to a colormap derived from its gray histogram.</summary>
    /// <param name="pixd">[optional] quantized pix with cmap; can be null</param>
    /// <param name="pixs">8 bpp gray input pix; not cmapped</param>
    /// <param name="pixm">[optional] mask over pixels in pixs to quantize</param>
    /// <param name="minfract">minimum fraction of pixels in a set of adjacent
    ///                        histo bins that causes the set to be automatically
    ///                        set aside as a color in the colormap; must be
    ///                        at least 0.01</param>
    /// <param name="maxsize">maximum number of adjacent bins allowed to represent
    ///                       a color, regardless of the population of pixels
    ///                       in the bins; must be at least 2</param>
    /// <returns>pixd 8 bpp, cmapped</returns>
    /// <remarks>
    /// (1) This is useful for quantizing images with relatively few
    ///     colors, but which may have both color and gray pixels.
    ///     If there are color pixels, it is assumed that an input
    ///     rgb image has been color quantized first so that:
    ///       ~ pixd has a colormap describing the color pixels
    ///       ~ pixm is a mask over the non-color pixels in pixd
    ///       ~ the colormap in pixd, and the color pixels in pixd,
    ///         have been repacked to go from 0 to n-1 (n colors)
    ///     If there are no color pixels, pixd and pixm are both null,
    ///     and all pixels in pixs are quantized to gray.
    /// (2) A 256-entry histogram is built of the gray values in pixs.
    ///     If pixm exists, the pixels contributing to the histogram are
    ///     restricted to the fg of pixm.  A colormap and LUT are generated
    ///     from this histogram.  We break up the array into a set
    ///     of intervals, each one constituting a color in the colormap:
    ///     An interval is identified by summing histogram bins until
    ///     either the sum equals or exceeds the %minfract of the total
    ///     number of pixels, or the span itself equals or exceeds %maxsize.
    ///     The color of each bin is always an average of the pixels
    ///     that constitute it.
    /// (3) Note that we do not specify the number of gray colors in
    ///     the colormap.  Instead, we specify two parameters that
    ///     describe the accuracy of the color assignments; this and
    ///     the actual image determine the number of resulting colors.
    /// (4) If a mask exists and it is not the same size as pixs, make
    ///     a new mask the same size as pixs, with the original mask
    ///     aligned at the UL corners.  Set all additional pixels
    ///     in the (larger) new mask set to 1, causing those pixels
    ///     in pixd to be set as gray.
    /// (5) We estimate the total number of colors (color plus gray);
    ///     if it exceeds 255, return null.
    /// </remarks>
    public static Pix GrayQuantFromHisto(Pix? pixd, Pix pixs, Pix? pixm, float minfract,
                                         int maxsize)
    {
        const string func = "pixGrayQuantFromHisto";
        if (pixs.Depth != 8)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");
        if (minfract < 0.01) {
            Log.Warning(func, "minfract < 0.01; setting to 0.05");
            minfract = 0.05f;
        }
        if (maxsize < 2) {
            Log.Warning(func, "maxsize < 2; setting to 10");
            maxsize = 10;
        }
        if ((pixd != null && pixm == null) || (pixd == null && pixm != null))
            throw Lept.Arg(func, "(pixd,pixm) not defined together");
        pixs.GetDimensions(out int w, out int h, out _);
        Pix? pixmr = null;  /* resized mask */
        PixColormap cmap;
        if (pixd != null) {
            if (pixm!.Depth != 1)
                throw Lept.Arg(func, "pixm not 1 bpp");
            if ((cmap = pixd.Colormap!) == null)
                throw Lept.Arg(func, "pixd not cmapped");
            pixd.GetDimensions(out int wd, out int hd, out _);
            if (w != wd || h != hd)
                throw Lept.Arg(func, "pixs, pixd sizes differ");
            int nc = cmap.Count;
            int nestim = nc + (int)(1.5 * 255 / maxsize);
            CStdio.Stderr($"nestim = {nestim}\n");
            if (nestim > 255) {
                Log.Error(func, $"Estimate {nestim} colors!");
                throw Lept.Arg(func, "probably too many colors");
            }
            pixm.GetDimensions(out int wm, out int hm, out _);
            if (w != wm || h != hm) {  /* resize the mask */
                Log.Warning(func, "mask and dest sizes not equal");
                pixmr = Create(w, h, 1);
                pixmr.Rasterop(0, 0, wm, hm, L.PIX_SRC, pixm, 0, 0);
                pixmr.Rasterop(wm, 0, w - wm, h, L.PIX_SET, null, 0, 0);
                pixmr.Rasterop(0, hm, wm, h - hm, L.PIX_SET, null, 0, 0);
            } else {
                pixmr = Clone(pixm);
            }
        } else {
            pixd = CreateTemplate(pixs);
            cmap = PixColormap.Create(8);
            pixd.SetColormap(cmap);
        }
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);

            /* Use original mask, if it exists, to select gray pixels */
        Numa na = pixs.GetGrayHistogramMasked(pixm, 0, 0, 1);

            /* Fill out the cmap with gray colors, and generate the lut
             * for pixel assignment.  Issue a warning on failure.  */
        if (NumaFillCmapFromHisto(na, cmap, minfract, maxsize, out int[] lut) != 0)
            Log.Error(func, "ran out of colors in cmap!");

            /* Assign the gray pixels to their cmap indices */
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        if (pixm == null) {
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    int vals = GetDataByte(lines, j);
                    int vald = lut[vals];
                    SetDataByte(lined, j, vald);
                }
            }
            return pixd;
        }

        uint[] datam = pixmr!.Data;
        int wplm = pixmr.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                if (GetDataBit(linem, j) == 0)
                    continue;
                int vals = GetDataByte(lines, j);
                int vald = lut[vals];
                SetDataByte(lined, j, vald);
            }
        }
        return pixd;
    }

    // leptonica: numaFillCmapFromHisto (grayquant.c)
    /// <param name="na">histogram of gray values</param>
    /// <param name="cmap">8 bpp cmap, possibly initialized with color value</param>
    /// <param name="minfract">minimum fraction of pixels in a set of adjacent
    ///                        histo bins that causes the set to be automatically
    ///                        set aside as a color in the colormap; must be
    ///                        at least 0.01</param>
    /// <param name="maxsize">maximum number of adjacent bins allowed to represent
    ///                       a color, regardless of the population of pixels
    ///                       in the bins; must be at least 2</param>
    /// <param name="lut">lookup table from gray value to colormap index</param>
    /// <returns>0 if OK, 1 if the colormap ran out of entries</returns>
    /// <remarks>
    /// (1) This static function must be called from pixGrayQuantFromHisto()
    /// </remarks>
    private static int NumaFillCmapFromHisto(Numa na, PixColormap cmap, float minfract,
                                             int maxsize, out int[] lut)
    {
        float total = na.GetSum();
        int mincount = (int)(minfract * total);
        int[] iahisto = na.GetIArray();
        lut = new int[256];
        int index = cmap.Count;  /* start with number of colors
                                  * already reserved */

            /* March through, associating colors with sets of adjacent
             * gray levels.  During the process, the LUT that gives
             * the colormap index for each gray level is computed.
             * To complete a color, either the total count must equal
             * or exceed %mincount, or the current span of colors must
             * equal or exceed %maxsize.  An empty span is not converted
             * into a color; it is simply ignored.  When a span is completed for a
             * color, the weighted color in the span is added to the colormap. */
        int sum = 0;
        int wtsum = 0;
        int istart = 0;
        int ret = 0;
        int span, val;
        for (int i = 0; i < 256; i++) {
            lut[i] = index;
            sum += iahisto[i];
            wtsum += i * iahisto[i];
            span = i - istart + 1;
            if (sum < mincount && span < maxsize)
                continue;

            if (sum == 0) {  /* empty span; don't save */
                istart = i + 1;
                continue;
            }

                /* Found new color; sum > 0 */
            val = (int)((float)wtsum / (float)sum + 0.5);
            ret = AddColorOrFail(cmap, val);
            istart = i + 1;
            sum = 0;
            wtsum = 0;
            index++;
        }
        if (istart < 256 && sum > 0) {  /* last one */
            span = 256 - istart;
            val = (int)((float)wtsum / (float)sum + 0.5);
            ret = AddColorOrFail(cmap, val);
        }

        return ret;
    }

    /// <summary>pixcmapAddColor with the result code of C: 1 and an error message when full.</summary>
    private static int AddColorOrFail(PixColormap cmap, int val)
    {
        if (cmap.FreeCount <= 0) {
            Log.Error("pixcmapAddColor", "no free color entries");
            return 1;
        }
        cmap.AddColor(val, val, val);
        return 0;
    }

    /*----------------------------------------------------------------------*
     *        Color quantize grayscale image using existing colormap        *
     *----------------------------------------------------------------------*/
    // leptonica: pixGrayQuantFromCmap (grayquant.c)
    /// <summary>Quantizes an 8 bpp image to the gray levels of an existing colormap.</summary>
    /// <param name="cmap">to quantize to; of dest pix</param>
    /// <param name="mindepth">minimum depth of pixd: can be 2, 4 or 8 bpp</param>
    /// <returns>pixd 2, 4 or 8 bpp, colormapped</returns>
    /// <remarks>
    /// (1) In use, pixs is an 8 bpp grayscale image without a colormap.
    ///     If there is an existing colormap, a warning is issued and
    ///     a copy of the input pixs is returned.
    /// </remarks>
    public Pix GrayQuantFromCmap(PixColormap cmap, int mindepth)
    {
        const string func = "pixGrayQuantFromCmap";
        if (Colormap != null) {
            Log.Warning(func, "pixs already has a colormap; returning a copy");
            return Copy(null, this);
        }
        GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (mindepth != 2 && mindepth != 4 && mindepth != 8)
            throw Lept.Arg(func, "invalid mindepth");

            /* Make sure the colormap is gray */
        PixColormap cmapd;
        if (cmap.HasColor()) {
            Log.Warning(func, "Converting colormap colors to gray");
            cmapd = cmap.ColorToGray(0.3f, 0.5f, 0.2f);
        } else {
            cmapd = cmap.Copy();
        }

            /* Make LUT into colormap */
        int[] tab = new int[256];
        for (int i = 0; i < 256; i++) {
            cmapd.GetNearestGrayIndex(i, out int index);
            tab[i] = index;
        }

        int depth = cmap.MinDepth;
        depth = Math.Max(depth, mindepth);
        Pix pixd = Create(w, h, depth);
        pixd.SetColormap(cmapd);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpls = Wpl;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                int vals = GetDataByte(lines, j);
                int vald = tab[vals];
                if (depth == 2)
                    SetDataDibit(lined, j, vald);
                else if (depth == 4)
                    SetDataQbit(lined, j, vald);
                else  /* depth == 8 */
                    SetDataByte(lined, j, vald);
            }
        }

        return pixd;
    }
}
