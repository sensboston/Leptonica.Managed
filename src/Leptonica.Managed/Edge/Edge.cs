using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Leptonica;

/*
 *  edge.c: Sobel and two-sided edge filters, and measurement of edge smoothness.
 *
 *  The Sobel edge detector uses these two simple gradient filters.
 *
 *       1    2    1             1    0   -1
 *       0    0    0             2    0   -2
 *      -1   -2   -1             1    0   -1
 *
 *      (horizontal)             (vertical)
 *
 *  To use both the vertical and horizontal filters, set the orientation
 *  flag to L_ALL_EDGES; this sums the abs. value of their outputs,
 *  clipped to 255.
 *
 *  Differences from C:
 *    - pixMeasureEdgeSmoothness has the three optional outputs as out parameters;
 *      they are 0.0 when the edge profile has fewer than 2 points.
 *      The "no output requested" error cannot occur.
 *    - The debug image of pixGetEdgeProfile is written with WritePng, as
 *      pixWrite(debugfile, pixt, IFF_PNG) is not ported yet.
 *    - pixGetLastOffPixelInRun and pixGetLastOnPixelInRun return the location
 *      and throw for an invalid direction.
 *
 *  Not ported: nothing.
 *
 *  Stubs for callees in other files: none.
 */
public sealed partial class Pix
{
    /*----------------------------------------------------------------------*
     *                    Sobel edge detecting filter                       *
     *----------------------------------------------------------------------*/
    // leptonica: pixSobelEdgeFilter (edge.c)
    /// <summary>Sobel edge filter of an 8 bpp image.</summary>
    /// <param name="orientflag">L_HORIZONTAL_EDGES, L_VERTICAL_EDGES, L_ALL_EDGES</param>
    /// <returns>pixd 8 bpp, edges are brighter</returns>
    /// <remarks>
    /// (1) Invert pixd to see larger gradients as darker (grayscale).
    /// (2) To generate a binary image of the edges, threshold
    ///     the result using pixThresholdToBinary().  If the high
    ///     edge values are to be fg (1), invert after running
    ///     pixThresholdToBinary().
    /// (3) Label the pixels as follows:
    ///         1    4    7
    ///         2    5    8
    ///         3    6    9
    ///     Read the data incrementally across the image and unroll
    ///     the loop.
    /// (4) This runs at about 45 Mpix/sec on a 3 GHz processor.
    /// </remarks>
    public Pix SobelEdgeFilter(int orientflag)
    {
        const string func = "pixSobelEdgeFilter";
        int w, h, d, wplt, wpld;
        Pix pixt, pixd;

        GetDimensions(out w, out h, out d);
        if (d != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (orientflag != L.L_HORIZONTAL_EDGES && orientflag != L.L_VERTICAL_EDGES &&
            orientflag != L.L_ALL_EDGES)
            throw Lept.Arg(func, "invalid orientflag");

        /* Add 1 pixel (mirrored) to each side of the image. */
        pixt = AddMirroredBorder(1, 1, 1, 1);

        /* Compute filter output at each location. */
        pixd = CreateTemplateNoInit(this);
        uint[] datat = pixt.Data;
        wplt = pixt.Wpl;
        uint[] datad = pixd.Data;
        wpld = pixd.Wpl;
        int wt = w + 2;

        LeptParallel.ForRows(h, (long)w * 12L, (yStart, yEnd) => {
            int stride = (wt + 31) & ~31;
            byte[] ring = ArrayPool<byte>.Shared.Rent(stride * 3);
            try {
                int s0 = 0, s1 = stride, s2 = stride * 2;
                UnpackGrayRow(datat, yStart * wplt, wt, ring, s0);
                UnpackGrayRow(datat, (yStart + 1) * wplt, wt, ring, s1);

                ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
                Vector256<short> vMax255 = Vector256.Create((short)255);

                for (int row = yStart; row < yEnd; row++) {
                    UnpackGrayRow(datat, (row + 2) * wplt, wt, ring, s2);
                    ref byte p0 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(ring), (nuint)s0);
                    ref byte p1 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(ring), (nuint)s1);
                    ref byte p2 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(ring), (nuint)s2);
                    ref uint rd = ref Unsafe.Add(ref baseD, (nuint)(row * wpld));

                    int col = 0;
                    if (Vector256.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
                        ref byte bd = ref Unsafe.As<uint, byte>(ref rd);
                        for (; col + 16 <= w; col += 16) {
                            Vector256<short> r00 = WidenToInt16(Vector128.LoadUnsafe(ref p0, (nuint)col));
                            Vector256<short> r02 = WidenToInt16(Vector128.LoadUnsafe(ref p0, (nuint)(col + 2)));
                            Vector256<short> r20 = WidenToInt16(Vector128.LoadUnsafe(ref p2, (nuint)col));
                            Vector256<short> r22 = WidenToInt16(Vector128.LoadUnsafe(ref p2, (nuint)(col + 2)));

                            Vector256<short> vOut;
                            if (orientflag == L.L_HORIZONTAL_EDGES) {
                                Vector256<short> r01 = WidenToInt16(Vector128.LoadUnsafe(ref p0, (nuint)(col + 1)));
                                Vector256<short> r21 = WidenToInt16(Vector128.LoadUnsafe(ref p2, (nuint)(col + 1)));
                                vOut = Vector256.Abs(r00 + (r01 << 1) + r02 - r20 - (r21 << 1) - r22) >> 3;
                            } else if (orientflag == L.L_VERTICAL_EDGES) {
                                Vector256<short> r10 = WidenToInt16(Vector128.LoadUnsafe(ref p1, (nuint)col));
                                Vector256<short> r12 = WidenToInt16(Vector128.LoadUnsafe(ref p1, (nuint)(col + 2)));
                                vOut = Vector256.Abs(r00 + (r10 << 1) + r20 - r02 - (r12 << 1) - r22) >> 3;
                            } else {
                                Vector256<short> r01 = WidenToInt16(Vector128.LoadUnsafe(ref p0, (nuint)(col + 1)));
                                Vector256<short> r21 = WidenToInt16(Vector128.LoadUnsafe(ref p2, (nuint)(col + 1)));
                                Vector256<short> r10 = WidenToInt16(Vector128.LoadUnsafe(ref p1, (nuint)col));
                                Vector256<short> r12 = WidenToInt16(Vector128.LoadUnsafe(ref p1, (nuint)(col + 2)));
                                Vector256<short> vgy = Vector256.Abs(r00 + (r01 << 1) + r02 - r20 - (r21 << 1) - r22) >> 3;
                                Vector256<short> vgx = Vector256.Abs(r00 + (r10 << 1) + r20 - r02 - (r12 << 1) - r22) >> 3;
                                vOut = Vector256.Min(vMax255, vgx + vgy);
                            }

                            Vector128<byte> vNarrow = Vector128.Narrow(vOut.AsUInt16().GetLower(), vOut.AsUInt16().GetUpper());
                            SimdEndian.SwapBytesInWords(vNarrow).StoreUnsafe(ref bd, (nuint)col);
                        }
                    }

                    for (; col < w; col++) {
                        int v1 = Unsafe.Add(ref p0, (nuint)col);
                        int v2 = Unsafe.Add(ref p1, (nuint)col);
                        int v3 = Unsafe.Add(ref p2, (nuint)col);
                        int v4 = Unsafe.Add(ref p0, (nuint)(col + 1));
                        int v6 = Unsafe.Add(ref p2, (nuint)(col + 1));
                        int v7 = Unsafe.Add(ref p0, (nuint)(col + 2));
                        int v8 = Unsafe.Add(ref p1, (nuint)(col + 2));
                        int v9 = Unsafe.Add(ref p2, (nuint)(col + 2));
                        int vd;
                        if (orientflag == L.L_HORIZONTAL_EDGES)
                            vd = Math.Abs(v1 + 2 * v4 + v7 - v3 - 2 * v6 - v9) >> 3;
                        else if (orientflag == L.L_VERTICAL_EDGES)
                            vd = Math.Abs(v1 + 2 * v2 + v3 - v7 - 2 * v8 - v9) >> 3;
                        else {
                            int gx0 = Math.Abs(v1 + 2 * v2 + v3 - v7 - 2 * v8 - v9) >> 3;
                            int gy0 = Math.Abs(v1 + 2 * v4 + v7 - v3 - 2 * v6 - v9) >> 3;
                            vd = Math.Min(255, gx0 + gy0);
                        }
                        ArrayAccess.SetDataByteUnchecked(ref rd, col, vd);
                    }

                    int tmp = s0;
                    s0 = s1;
                    s1 = s2;
                    s2 = tmp;
                }
            } finally {
                ArrayPool<byte>.Shared.Return(ring);
            }
        });

        return pixd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<short> WidenToInt16(Vector128<byte> v)
    {
        (Vector128<ushort> lo, Vector128<ushort> hi) = Vector128.Widen(v);
        return Vector256.Create(lo, hi).AsInt16();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UnpackGrayRow(uint[] src, int rowWordOffset, int width, byte[] dst, int dstOffset)
    {
        ref uint rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), (nuint)rowWordOffset);
        ref byte rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), (nuint)dstOffset);
        int j = 0;
        if (Vector256.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
            ref byte bs = ref Unsafe.As<uint, byte>(ref rs);
            for (; j + 32 <= width; j += 32)
                SimdEndian.SwapBytesInWords(Vector256.LoadUnsafe(ref bs, (nuint)j)).StoreUnsafe(ref rd, (nuint)j);
        }
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
            ref byte bs = ref Unsafe.As<uint, byte>(ref rs);
            for (; j + 16 <= width; j += 16)
                SimdEndian.SwapBytesInWords(Vector128.LoadUnsafe(ref bs, (nuint)j)).StoreUnsafe(ref rd, (nuint)j);
        }
        for (; j < width; j++)
            Unsafe.Add(ref rd, (nuint)j) = (byte)ArrayAccess.GetDataByteUnchecked(ref rs, j);
    }

    /*----------------------------------------------------------------------*
     *                   Two-sided edge gradient filter                     *
     *----------------------------------------------------------------------*/
    // leptonica: pixTwoSidedEdgeFilter (edge.c)
    /// <summary>Two-sided edge filter of an 8 bpp image.</summary>
    /// <param name="orientflag">L_HORIZONTAL_EDGES, L_VERTICAL_EDGES</param>
    /// <returns>pixd 8 bpp, edges are brighter</returns>
    /// <remarks>
    /// (1) For detecting vertical edges, this considers the
    ///     difference of the central pixel from those on the left
    ///     and right.  For situations where the gradient is the same
    ///     sign on both sides, this computes and stores the minimum
    ///     (absolute value of the) difference.  The reason for
    ///     checking the sign is that we are looking for pixels within
    ///     a transition.  By contrast, for single pixel noise, the pixel
    ///     value is either larger than or smaller than its neighbors,
    ///     so the gradient would change direction on each side.  Horizontal
    ///     edges are handled similarly, looking for vertical gradients.
    /// (2) To generate a binary image of the edges, threshold
    ///     the result using pixThresholdToBinary().  If the high
    ///     edge values are to be fg (1), invert after running
    ///     pixThresholdToBinary().
    /// (3) This runs at about 60 Mpix/sec on a 3 GHz processor.
    ///     It is about 30% faster than Sobel, and the results are
    ///     similar.
    /// </remarks>
    public Pix TwoSidedEdgeFilter(int orientflag)
    {
        const string func = "pixTwoSidedEdgeFilter";
        int w, h, d, i, j, wpls, wpld;
        int cval, rval, bval, val, lgrad, rgrad, tgrad, bgrad;
        Pix pixd;

        GetDimensions(out w, out h, out d);
        if (d != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (orientflag != L.L_HORIZONTAL_EDGES && orientflag != L.L_VERTICAL_EDGES)
            throw Lept.Arg(func, "invalid orientflag");

        pixd = CreateTemplate(this);
        uint[] datas = Data;
        wpls = Wpl;
        uint[] datad = pixd.Data;
        wpld = pixd.Wpl;
        ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
        ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
        if (orientflag == L.L_VERTICAL_EDGES) {
            for (i = 0; i < h; i++) {
                ref uint rs = ref Unsafe.Add(ref baseS, (nuint)(i * wpls));
                ref uint rd = ref Unsafe.Add(ref baseD, (nuint)(i * wpld));
                cval = ArrayAccess.GetDataByteUnchecked(ref rs, 1);
                lgrad = cval - ArrayAccess.GetDataByteUnchecked(ref rs, 0);
                for (j = 1; j < w - 1; j++) {
                    rval = ArrayAccess.GetDataByteUnchecked(ref rs, j + 1);
                    rgrad = rval - cval;
                    if (lgrad * rgrad > 0) {
                        val = lgrad < 0 ? -Math.Max(lgrad, rgrad) : Math.Min(lgrad, rgrad);
                        ArrayAccess.SetDataByteUnchecked(ref rd, j, val);
                    }
                    lgrad = rgrad;
                    cval = rval;
                }
            }
        }
        else {  /* L_HORIZONTAL_EDGES — row-major traversal for L1 cache locality */
            for (i = 1; i < h - 1; i++) {
                ref uint rsPrev = ref Unsafe.Add(ref baseS, (nuint)((i - 1) * wpls));
                ref uint rsCur = ref Unsafe.Add(ref baseS, (nuint)(i * wpls));
                ref uint rsNext = ref Unsafe.Add(ref baseS, (nuint)((i + 1) * wpls));
                ref uint rd = ref Unsafe.Add(ref baseD, (nuint)(i * wpld));
                for (j = 0; j < w; j++) {
                    cval = ArrayAccess.GetDataByteUnchecked(ref rsCur, j);
                    tgrad = cval - ArrayAccess.GetDataByteUnchecked(ref rsPrev, j);
                    bval = ArrayAccess.GetDataByteUnchecked(ref rsNext, j);
                    bgrad = bval - cval;
                    if (tgrad * bgrad > 0) {
                        val = tgrad < 0 ? -Math.Max(tgrad, bgrad) : Math.Min(tgrad, bgrad);
                        ArrayAccess.SetDataByteUnchecked(ref rd, j, val);
                    }
                }
            }
        }

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *                   Measurement of edge smoothness                     *
     *----------------------------------------------------------------------*/
    // leptonica: pixMeasureEdgeSmoothness (edge.c)
    /// <summary>Measures the smoothness of the edge of a connected component.</summary>
    /// <param name="side">L_FROM_LEFT, L_FROM_RIGHT, L_FROM_TOP, L_FROM_BOT</param>
    /// <param name="minjump">minimum jump to be counted; &gt;= 1</param>
    /// <param name="minreversal">minimum reversal size for new peak or valley</param>
    /// <param name="jpl">jumps/length: number of jumps, normalized to length of component side</param>
    /// <param name="jspl">jumpsum/length: sum of all sufficiently large jumps,
    /// normalized to length of component side</param>
    /// <param name="rpl">reversals/length: number of peak-to-valley or valley-to-peak
    /// reversals, normalized to length of component side</param>
    /// <param name="debugfile">[optional] displays constructed edge; use null for no output</param>
    /// <remarks>
    /// (1) This computes three measures of smoothness of the edge of a
    ///     connected component:
    ///       * jumps/length: (jpl) the number of jumps of size >= %minjump,
    ///         normalized to the length of the side
    ///       * jump sum/length: (jspl) the sum of all jump lengths of
    ///         size >= %minjump, normalized to the length of the side
    ///       * reversals/length: (rpl) the number of peak &lt;--&gt; valley
    ///         reversals, using %minreverse as a minimum deviation of
    ///         the peak or valley from its preceding extremum,
    ///         normalized to the length of the side
    /// (2) The input pix should be a single connected component, but
    ///     this is not required.
    /// </remarks>
    public void MeasureEdgeSmoothness(int side, int minjump, int minreversal,
                                      out float jpl, out float jspl, out float rpl,
                                      string? debugfile = null)
    {
        const string func = "pixMeasureEdgeSmoothness";
        int i, n, val, nval, diff, njumps, jumpsum, nreversal;
        Numa na, nae;

        jpl = 0.0f;
        jspl = 0.0f;
        rpl = 0.0f;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");
        if (side != L.L_FROM_LEFT && side != L.L_FROM_RIGHT &&
            side != L.L_FROM_TOP && side != L.L_FROM_BOT)
            throw Lept.Arg(func, "invalid side");
        if (minjump < 1)
            throw Lept.Arg(func, "invalid minjump; must be >= 1");
        if (minreversal < 1)
            throw Lept.Arg(func, "invalid minreversal; must be >= 1");

        na = GetEdgeProfile(side, debugfile);
        if ((n = na.Count) < 2)
            return;

        jumpsum = 0;
        njumps = 0;
        val = na.GetIValue(0);
        for (i = 1; i < n; i++) {
            nval = na.GetIValue(i);
            diff = Box.LAbs(nval - val);
            if (diff >= minjump) {
                njumps++;
                jumpsum += diff;
            }
            val = nval;
        }
        jpl = (float)njumps / (float)(n - 1);
        jspl = (float)jumpsum / (float)(n - 1);

        nae = na.FindExtrema((float)minreversal, out _);
        nreversal = nae.Count - 1;  // NOTE(upstream): -1 when there are no extrema
        rpl = (float)nreversal / (float)(n - 1);
    }

    // leptonica: pixGetEdgeProfile (edge.c)
    /// <summary>Gets the profile of the foreground edge pixel locations on one side.</summary>
    /// <param name="side">L_FROM_LEFT, L_FROM_RIGHT, L_FROM_TOP, L_FROM_BOT</param>
    /// <param name="debugfile">[optional] displays constructed edge; use null for no output</param>
    /// <returns>na of fg edge pixel locations</returns>
    public Numa GetEdgeProfile(int side, string? debugfile = null)
    {
        const string func = "pixGetEdgeProfile";
        int x, y, w, h, loc, index, ival;
        uint val;
        Numa na;
        Pix pixt;
        PixColormap? cmap;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (side != L.L_FROM_LEFT && side != L.L_FROM_RIGHT &&
            side != L.L_FROM_TOP && side != L.L_FROM_BOT)
            throw Lept.Arg(func, "invalid side");

        GetDimensions(out w, out h, out _);
        if (side == L.L_FROM_LEFT || side == L.L_FROM_RIGHT)
            na = Numa.Create(h);
        else
            na = Numa.Create(w);
        if (side == L.L_FROM_LEFT) {
            loc = GetLastOffPixelInRun(0, 0, L.L_FROM_LEFT);
            loc = (loc == w - 1) ? 0 : loc + 1;  /* back to the left edge */
            na.AddNumber(loc);
            for (y = 1; y < h; y++) {
                GetPixel(loc, y, out val);
                if (val == 1) {
                    loc = GetLastOnPixelInRun(loc, y, L.L_FROM_RIGHT);
                } else {
                    loc = GetLastOffPixelInRun(loc, y, L.L_FROM_LEFT);
                    loc = (loc == w - 1) ? 0 : loc + 1;
                }
                na.AddNumber(loc);
            }
        }
        else if (side == L.L_FROM_RIGHT) {
            loc = GetLastOffPixelInRun(w - 1, 0, L.L_FROM_RIGHT);
            loc = (loc == 0) ? w - 1 : loc - 1;  /* back to the right edge */
            na.AddNumber(loc);
            for (y = 1; y < h; y++) {
                GetPixel(loc, y, out val);
                if (val == 1) {
                    loc = GetLastOnPixelInRun(loc, y, L.L_FROM_LEFT);
                } else {
                    loc = GetLastOffPixelInRun(loc, y, L.L_FROM_RIGHT);
                    loc = (loc == 0) ? w - 1 : loc - 1;
                }
                na.AddNumber(loc);
            }
        }
        else if (side == L.L_FROM_TOP) {
            loc = GetLastOffPixelInRun(0, 0, L.L_FROM_TOP);
            loc = (loc == h - 1) ? 0 : loc + 1;  /* back to the top edge */
            na.AddNumber(loc);
            for (x = 1; x < w; x++) {
                GetPixel(x, loc, out val);
                if (val == 1) {
                    loc = GetLastOnPixelInRun(x, loc, L.L_FROM_BOT);
                } else {
                    loc = GetLastOffPixelInRun(x, loc, L.L_FROM_TOP);
                    loc = (loc == h - 1) ? 0 : loc + 1;
                }
                na.AddNumber(loc);
            }
        }
        else {  /* side == L_FROM_BOT */
            loc = GetLastOffPixelInRun(0, h - 1, L.L_FROM_BOT);
            loc = (loc == 0) ? h - 1 : loc - 1;  /* back to the bottom edge */
            na.AddNumber(loc);
            for (x = 1; x < w; x++) {
                GetPixel(x, loc, out val);
                if (val == 1) {
                    loc = GetLastOnPixelInRun(x, loc, L.L_FROM_TOP);
                } else {
                    loc = GetLastOffPixelInRun(x, loc, L.L_FROM_BOT);
                    loc = (loc == 0) ? h - 1 : loc - 1;
                }
                na.AddNumber(loc);
            }
        }

        if (debugfile != null) {
            pixt = ConvertTo8(true);
            cmap = pixt.Colormap;
            cmap!.AddColor(255, 0, 0);
            index = cmap.Count - 1;
            if (side == L.L_FROM_LEFT || side == L.L_FROM_RIGHT) {
                for (y = 0; y < h; y++) {
                    ival = na.GetIValue(y);
                    pixt.SetPixel(ival, y, (uint)index);
                }
            } else {  /* L_FROM_TOP or L_FROM_BOT */
                for (x = 0; x < w; x++) {
                    ival = na.GetIValue(x);
                    pixt.SetPixel(x, ival, (uint)index);
                }
            }
            // TODO(writefile.c): replace by pixWrite(debugfile, pixt, IFF_PNG)
            WritePng(debugfile, pixt, 0.0f);
        }

        return na;
    }

    // leptonica: pixGetLastOffPixelInRun (edge.c)
    /// <summary>Gets the location of the last OFF pixel in the run starting at (x, y).</summary>
    /// <param name="x">starting location</param>
    /// <param name="y">starting location</param>
    /// <param name="direction">L_FROM_LEFT, L_FROM_RIGHT, L_FROM_TOP, L_FROM_BOT</param>
    /// <returns>location in scan direction coordinate of last OFF pixel found</returns>
    /// <remarks>
    /// (1) Search starts from the pixel at (x, y), which is OFF.
    /// (2) It returns the location in the scan direction of the last
    ///     pixel in the current run that is OFF.
    /// (3) The interface for these pixel run functions is cleaner when
    ///     you ask for the last pixel in the current run, rather than the
    ///     first pixel of opposite polarity that is found, because the
    ///     current run may go to the edge of the image, in which case
    ///     no pixel of opposite polarity is found.
    /// </remarks>
    public int GetLastOffPixelInRun(int x, int y, int direction)
    {
        const string func = "pixGetLastOffPixelInRun";
        int loc, w, h;
        uint val;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (direction != L.L_FROM_LEFT && direction != L.L_FROM_RIGHT &&
            direction != L.L_FROM_TOP && direction != L.L_FROM_BOT)
            throw Lept.Arg(func, "invalid side");

        GetDimensions(out w, out h, out _);
        if (direction == L.L_FROM_LEFT) {
            for (loc = x; loc < w; loc++) {
                GetPixel(loc, y, out val);
                if (val == 1)
                    break;
            }
            return loc - 1;
        } else if (direction == L.L_FROM_RIGHT) {
            for (loc = x; loc >= 0; loc--) {
                GetPixel(loc, y, out val);
                if (val == 1)
                    break;
            }
            return loc + 1;
        }
        else if (direction == L.L_FROM_TOP) {
            for (loc = y; loc < h; loc++) {
                GetPixel(x, loc, out val);
                if (val == 1)
                    break;
            }
            return loc - 1;
        }
        else {  /* direction == L_FROM_BOT */
            for (loc = y; loc >= 0; loc--) {
                GetPixel(x, loc, out val);
                if (val == 1)
                    break;
            }
            return loc + 1;
        }
    }

    // leptonica: pixGetLastOnPixelInRun (edge.c)
    /// <summary>Gets the location of the last ON pixel in the run starting at (x, y).</summary>
    /// <param name="x">starting location</param>
    /// <param name="y">starting location</param>
    /// <param name="direction">L_FROM_LEFT, L_FROM_RIGHT, L_FROM_TOP, L_FROM_BOT</param>
    /// <returns>location in scan direction coordinate of last ON pixel found</returns>
    /// <remarks>
    /// (1) Search starts from the pixel at (x, y), which is ON.
    /// (2) It returns the location in the scan direction of the last
    ///     pixel in the current run that is ON.
    /// </remarks>
    public int GetLastOnPixelInRun(int x, int y, int direction)
    {
        const string func = "pixGetLastOnPixelInRun";
        int loc, w, h;
        uint val;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (direction != L.L_FROM_LEFT && direction != L.L_FROM_RIGHT &&
            direction != L.L_FROM_TOP && direction != L.L_FROM_BOT)
            throw Lept.Arg(func, "invalid side");

        GetDimensions(out w, out h, out _);
        if (direction == L.L_FROM_LEFT) {
            for (loc = x; loc < w; loc++) {
                GetPixel(loc, y, out val);
                if (val == 0)
                    break;
            }
            return loc - 1;
        } else if (direction == L.L_FROM_RIGHT) {
            for (loc = x; loc >= 0; loc--) {
                GetPixel(loc, y, out val);
                if (val == 0)
                    break;
            }
            return loc + 1;
        }
        else if (direction == L.L_FROM_TOP) {
            for (loc = y; loc < h; loc++) {
                GetPixel(x, loc, out val);
                if (val == 0)
                    break;
            }
            return loc - 1;
        }
        else {  /* direction == L_FROM_BOT */
            for (loc = y; loc >= 0; loc--) {
                GetPixel(x, loc, out val);
                if (val == 0)
                    break;
            }
            return loc + 1;
        }
    }
}
