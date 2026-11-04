using System;
using System.Buffers.Binary;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  rotateorth.c: Orthogonal rotations and flips.
 *
 *  Top-level rotation by multiples of 90 degrees
 *      pixRotateOrth
 *  180-degree rotation
 *      pixRotate180
 *  90-degree rotation (both directions)
 *      pixRotate90
 *  Left-right flip
 *      pixFlipLR
 *  Top-bottom flip
 *      pixFlipTB
 *  Byte reverse tables
 *      makeReverseByteTab1, makeReverseByteTab2, makeReverseByteTab4
 *
 *  Differences from C:
 *    - The C pointers into the image data (lines, lined, datas, datad) are
 *      word offsets into Pix.Data; the ArrayAccess calls take a span that
 *      starts at the line.
 *    - The default branch of the depth switches is unreachable because the
 *      depth is validated first; it throws instead of returning NULL.
 *
 *  Not ported: nothing.
 */
public sealed partial class Pix
{
    /*------------------------------------------------------------------*
     *           Top-level rotation by multiples of 90 degrees          *
     *------------------------------------------------------------------*/
    // leptonica: pixRotateOrth (rotateorth.c)
    /// <summary>Rotates by 0, 90, 180 or 270 degrees clockwise.</summary>
    /// <param name="quads">0-3; number of 90 degree cw rotations</param>
    /// <returns>a new pix</returns>
    public Pix RotateOrth(int quads)
    {
        if (quads < 0 || quads > 3)
            throw Lept.Arg("pixRotateOrth", "quads not in {0,1,2,3}");

        if (quads == 0)
            return Copy(null, this);
        else if (quads == 1)
            return Rotate90(1);
        else if (quads == 2)
            return Rotate180(null, this);
        else /* quads == 3 */
            return Rotate90(-1);
    }


    /*------------------------------------------------------------------*
     *                          180 degree rotation                     *
     *------------------------------------------------------------------*/
    // leptonica: pixRotate180 (rotateorth.c)
    /// <summary>Rotates by 180 degrees about the image center.</summary>
    /// <param name="pixd">[optional]; can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">all depths</param>
    /// <remarks>
    /// (1) This does a 180 rotation of the image about the center,
    ///     which is equivalent to a left-right flip about a vertical
    ///     line through the image center, followed by a top-bottom
    ///     flip about a horizontal line through the image center.
    /// (2) There are 3 cases for input:
    ///     (a) pixd == null (creates a new pixd)
    ///     (b) pixd == pixs (in-place operation)
    ///     (c) pixd != pixs (existing pixd)
    /// (3) For clarity, use these three patterns, respectively:
    ///     (a) pixd = pixRotate180(NULL, pixs);
    ///     (b) pixRotate180(pixs, pixs);
    ///     (c) pixRotate180(pixd, pixs);
    /// </remarks>
    public static Pix Rotate180(Pix? pixd, Pix pixs)
    {
        int d = pixs.Depth;
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixRotate180", "pixs not in {1,2,4,8,16,32} bpp");

            /* Prepare pixd for in-place operation */
        pixd = Copy(pixd, pixs);

        FlipLR(pixd, pixd);
        FlipTB(pixd, pixd);
        return pixd;
    }


    /*------------------------------------------------------------------*
     *                           90 degree rotation                     *
     *------------------------------------------------------------------*/
    // leptonica: pixRotate90 (rotateorth.c)
    /// <summary>Rotates by 90 degrees about the image center, clockwise or counterclockwise.</summary>
    /// <param name="direction">clockwise = 1, counterclockwise = -1</param>
    /// <returns>a new pix</returns>
    /// <remarks>
    /// (1) This does a 90 degree rotation of the image about the center,
    ///     either cw or ccw, returning a new pix.
    /// (2) The direction must be either 1 (cw) or -1 (ccw).
    /// </remarks>
    public Pix Rotate90(int direction)
    {
        int wd, hd, d, wpls, wpld;
        int i, j;
        int val;
        int lines, lined;
        Pix pixd;

        Pix pixs = this;
        pixs.GetDimensions(out hd, out wd, out d);  /* note: reversed */
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixRotate90", "pixs not in {1,2,4,8,16,32} bpp");
        if (direction != 1 && direction != -1)
            throw Lept.Arg("pixRotate90", "invalid direction");

        pixd = Create(wd, hd, d);
        pixd.CopyColormap(pixs);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        pixd.CopySpp(pixs);

        uint[] datas = pixs.Data;
        wpls = pixs.Wpl;
        uint[] datad = pixd.Data;
        wpld = pixd.Wpl;

        if (direction == 1) {  /* clockwise */
            switch (d)
            {
                case 32:
                    Rotate90Low32(datas, wpls, datad, wpld, wd, hd, true);
                    break;
                case 16:
                    for (i = 0; i < hd; i++) {
                        lined = i * wpld;
                        lines = (wd - 1) * wpls;
                        for (j = 0; j < wd; j++) {
                            if ((val = GetDataTwoBytes(datas.AsSpan(lines), i)) != 0)
                                SetDataTwoBytes(datad.AsSpan(lined), j, val);
                            lines -= wpls;
                        }
                    }
                    break;
                case 8:
                    for (i = 0; i < hd; i++) {
                        lined = i * wpld;
                        lines = (wd - 1) * wpls;
                        for (j = 0; j < wd; j++) {
                            if ((val = GetDataByte(datas.AsSpan(lines), i)) != 0)
                                SetDataByte(datad.AsSpan(lined), j, val);
                            lines -= wpls;
                        }
                    }
                    break;
                case 4:
                    for (i = 0; i < hd; i++) {
                        lined = i * wpld;
                        lines = (wd - 1) * wpls;
                        for (j = 0; j < wd; j++) {
                            if ((val = GetDataQbit(datas.AsSpan(lines), i)) != 0)
                                SetDataQbit(datad.AsSpan(lined), j, val);
                            lines -= wpls;
                        }
                    }
                    break;
                case 2:
                    for (i = 0; i < hd; i++) {
                        lined = i * wpld;
                        lines = (wd - 1) * wpls;
                        for (j = 0; j < wd; j++) {
                            if ((val = GetDataDibit(datas.AsSpan(lines), i)) != 0)
                                SetDataDibit(datad.AsSpan(lined), j, val);
                            lines -= wpls;
                        }
                    }
                    break;
                case 1:
                    Rotate90Low1(datas, wpls, datad, wpld, wd, hd, true);
                    break;
                default:
                    Log.Error("pixRotate90", $"illegal depth: {d}");
                    throw Lept.Fail("pixRotate90", "illegal depth");
            }
        } else  {     /* direction counter-clockwise */
            switch (d)
            {
                case 32:
                    Rotate90Low32(datas, wpls, datad, wpld, wd, hd, false);
                    break;
                case 16:
                    for (i = 0; i < hd; i++) {
                        lined = i * wpld;
                        lines = 0;
                        for (j = 0; j < wd; j++) {
                            if ((val = GetDataTwoBytes(datas.AsSpan(lines), hd - 1 - i)) != 0)
                                SetDataTwoBytes(datad.AsSpan(lined), j, val);
                            lines += wpls;
                        }
                    }
                    break;
                case 8:
                    for (i = 0; i < hd; i++) {
                        lined = i * wpld;
                        lines = 0;
                        for (j = 0; j < wd; j++) {
                            if ((val = GetDataByte(datas.AsSpan(lines), hd - 1 - i)) != 0)
                                SetDataByte(datad.AsSpan(lined), j, val);
                            lines += wpls;
                        }
                    }
                    break;
                case 4:
                    for (i = 0; i < hd; i++) {
                        lined = i * wpld;
                        lines = 0;
                        for (j = 0; j < wd; j++) {
                            if ((val = GetDataQbit(datas.AsSpan(lines), hd - 1 - i)) != 0)
                                SetDataQbit(datad.AsSpan(lined), j, val);
                            lines += wpls;
                        }
                    }
                    break;
                case 2:
                    for (i = 0; i < hd; i++) {
                        lined = i * wpld;
                        lines = 0;
                        for (j = 0; j < wd; j++) {
                            if ((val = GetDataDibit(datas.AsSpan(lines), hd - 1 - i)) != 0)
                                SetDataDibit(datad.AsSpan(lined), j, val);
                            lines += wpls;
                        }
                    }
                    break;
                case 1:
                    Rotate90Low1(datas, wpls, datad, wpld, wd, hd, false);
                    break;
                default:
                    Log.Error("pixRotate90", $"illegal depth: {d}");
                    throw Lept.Fail("pixRotate90", "illegal depth");
            }
        }

        return pixd;
    }


    /*------------------------------------------------------------------*
     *                            Left-right flip                       *
     *------------------------------------------------------------------*/
    // leptonica: pixFlipLR (rotateorth.c)
    /// <summary>Left-right flip.</summary>
    /// <param name="pixd">[optional]; can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">all depths</param>
    /// <remarks>
    /// (1) This does a left-right flip of the image, which is
    ///     equivalent to a rotation out of the plane about a
    ///     vertical line through the image center.
    /// (2) There are 3 cases for input:
    ///     (a) pixd == null (creates a new pixd)
    ///     (b) pixd == pixs (in-place operation)
    ///     (c) pixd != pixs (existing pixd)
    /// (3) For clarity, use these three patterns, respectively:
    ///     (a) pixd = pixFlipLR(NULL, pixs);
    ///     (b) pixFlipLR(pixs, pixs);
    ///     (c) pixFlipLR(pixd, pixs);
    /// (4) If an existing pixd is not the same size as pixs, the
    ///     image data will be reallocated.
    /// (5) The pixel access routines allow a trivial implementation.
    ///     However, for d &lt; 8, it is more efficient to right-justify
    ///     each line to a 32-bit boundary and then extract bytes and
    ///     do pixel reversing.   In those cases, as in the 180 degree
    ///     rotation, we right-shift the data (if necessary) to
    ///     right-justify on the 32 bit boundary, and then read the
    ///     bytes off each raster line in reverse order, reversing
    ///     the pixels in each byte using a table.  These functions
    ///     for 1, 2 and 4 bpp were tested against the "trivial"
    ///     version (shown here for 4 bpp):
    ///         for (i = 0; i &lt; h; i++) {
    ///             line = data + i * wpl;
    ///             memcpy(buffer, line, bpl);
    ///               for (j = 0; j &lt; w; j++) {
    ///                 val = GET_DATA_QBIT(buffer, w - 1 - j);
    ///                   SET_DATA_QBIT(line, j, val);
    ///             }
    ///         }
    /// </remarks>
    public static Pix FlipLR(Pix? pixd, Pix pixs)
    {
        byte[]? tab;
        int w, h, d, wpl;
        int extra, shift, databpl, bpl, i, j;
        uint val;

        pixs.GetDimensions(out w, out h, out d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixFlipLR", "pixs not in {1,2,4,8,16,32} bpp");

            /* Prepare pixd for in-place operation */
        pixd = Copy(pixd, pixs);

        uint[] data = pixd.Data;
        wpl = pixd.Wpl;
        switch (d)
        {
        case 1:
            tab = makeReverseByteTab1();
            break;
        case 2:
            tab = makeReverseByteTab2();
            break;
        case 4:
            tab = makeReverseByteTab4();
            break;
        default:
            tab = null;
            break;
        }

        uint[] buffer = new uint[wpl];

        bpl = 4 * wpl;
        int line;
        switch (d)
        {
            case 32:
                for (i = 0; i < h; i++) {
                    line = i * wpl;
                    Array.Copy(data, line, buffer, 0, wpl);
                    for (j = 0; j < w; j++)
                        data[line + j] = buffer[w - 1 - j];
                }
                break;
            case 16:
                for (i = 0; i < h; i++) {
                    line = i * wpl;
                    Array.Copy(data, line, buffer, 0, wpl);
                    for (j = 0; j < w; j++) {
                        val = (uint)GetDataTwoBytes(buffer, w - 1 - j);
                        SetDataTwoBytes(data.AsSpan(line), j, (int)val);
                    }
                }
                break;
            case 8:
                for (i = 0; i < h; i++) {
                    line = i * wpl;
                    Array.Copy(data, line, buffer, 0, wpl);
                    int j8 = 0;
                    Span<uint> dstLine8 = data.AsSpan(line, wpl);
                    int byteOffset = (w & 3) * 8;
                    for (; j8 + 3 < w; j8 += 4) {
                        int srcWord = (w - 4 - j8) >> 2;
                        uint pixels = buffer[srcWord];
                        if (byteOffset != 0)
                            pixels = (pixels << byteOffset) | (buffer[srcWord + 1] >> (32 - byteOffset));
                        data[line + (j8 >> 2)] = BinaryPrimitives.ReverseEndianness(pixels);
                    }
                    for (; j8 < w; j8++) {
                        val = (uint)GetDataByte(buffer, w - 1 - j8);
                        SetDataByte(dstLine8, j8, (int)val);
                    }
                }
                break;
            case 4:
                extra = (w * d) & 31;
                if (extra != 0)
                    shift = 8 - extra / 4;
                else
                    shift = 0;
                if (shift != 0)
                    rasteropHipLow(data, h, d, wpl, 0, h, shift);

                databpl = (w + 1) / 2;
                for (i = 0; i < h; i++) {
                    line = i * wpl;
                    Array.Copy(data, line, buffer, 0, wpl);
                    for (j = 0; j < databpl; j++) {
                        val = (uint)GetDataByte(buffer, bpl - 1 - j);
                        SetDataByte(data.AsSpan(line), j, tab![val]);
                    }
                }
                break;
            case 2:
                extra = (w * d) & 31;
                if (extra != 0)
                    shift = 16 - extra / 2;
                else
                    shift = 0;
                if (shift != 0)
                    rasteropHipLow(data, h, d, wpl, 0, h, shift);

                databpl = (w + 3) / 4;
                for (i = 0; i < h; i++) {
                    line = i * wpl;
                    Array.Copy(data, line, buffer, 0, wpl);
                    for (j = 0; j < databpl; j++) {
                        val = (uint)GetDataByte(buffer, bpl - 1 - j);
                        SetDataByte(data.AsSpan(line), j, tab![val]);
                    }
                }
                break;
            case 1:
                extra = (w * d) & 31;
                if (extra != 0)
                    shift = 32 - extra;
                else
                    shift = 0;
                if (shift != 0)
                    rasteropHipLow(data, h, d, wpl, 0, h, shift);

                databpl = (w + 7) / 8;
                for (i = 0; i < h; i++) {
                    line = i * wpl;
                    Array.Copy(data, line, buffer, 0, wpl);
                    for (j = 0; j < databpl; j++) {
                        val = (uint)GetDataByte(buffer, bpl - 1 - j);
                        SetDataByte(data.AsSpan(line), j, tab![val]);
                    }
                }
                break;
            default:
                Log.Error("pixFlipLR", $"illegal depth: {d}");
                throw Lept.Fail("pixFlipLR", "illegal depth");
        }

        return pixd;
    }


    /*------------------------------------------------------------------*
     *                            Top-bottom flip                       *
     *------------------------------------------------------------------*/
    // leptonica: pixFlipTB (rotateorth.c)
    /// <summary>Top-bottom flip.</summary>
    /// <param name="pixd">[optional]; can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">all depths</param>
    /// <remarks>
    /// (1) This does a top-bottom flip of the image, which is
    ///     equivalent to a rotation out of the plane about a
    ///     horizontal line through the image center.
    /// (2) There are 3 cases for input:
    ///     (a) pixd == null (creates a new pixd)
    ///     (b) pixd == pixs (in-place operation)
    ///     (c) pixd != pixs (existing pixd)
    /// (3) For clarity, use these three patterns, respectively:
    ///     (a) pixd = pixFlipTB(NULL, pixs);
    ///     (b) pixFlipTB(pixs, pixs);
    ///     (c) pixFlipTB(pixd, pixs);
    /// (4) If an existing pixd is not the same size as pixs, the
    ///     image data will be reallocated.
    /// (5) This is simple and fast.  We use the memcpy function
    ///     to do all the work on aligned data, regardless of pixel
    ///     depth.
    /// </remarks>
    public static Pix FlipTB(Pix? pixd, Pix pixs)
    {
        int h, d, wpl, i, k, h2;
        int linet, lineb;

        pixs.GetDimensions(out _, out h, out d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixFlipTB", "pixs not in {1,2,4,8,16,32} bpp");

            /* Prepare pixd for in-place operation */
        pixd = Copy(pixd, pixs);

        uint[] data = pixd.Data;
        wpl = pixd.Wpl;
        uint[] buffer = new uint[wpl];

        h2 = h / 2;
        for (i = 0, k = h - 1; i < h2; i++, k--) {
            linet = i * wpl;
            lineb = k * wpl;
            Array.Copy(data, linet, buffer, 0, wpl);
            Array.Copy(data, lineb, data, linet, wpl);
            Array.Copy(buffer, 0, data, lineb, wpl);
        }

        return pixd;
    }


    /*------------------------------------------------------------------*
     *                      Static byte reverse tables                  *
     *------------------------------------------------------------------*/
    // leptonica: makeReverseByteTab1 (rotateorth.c)
    /// <remarks>
    /// (1) This generates an 8 bit lookup table for reversing
    ///     the order of eight 1-bit pixels.
    /// </remarks>
    private static byte[] makeReverseByteTab1()
    {
        int i;
        byte[] tab;

        tab = new byte[256];
        for (i = 0; i < 256; i++)
            tab[i] = (byte)(((0x80 & i) >> 7) |
                            ((0x40 & i) >> 5) |
                            ((0x20 & i) >> 3) |
                            ((0x10 & i) >> 1) |
                            ((0x08 & i) << 1) |
                            ((0x04 & i) << 3) |
                            ((0x02 & i) << 5) |
                            ((0x01 & i) << 7));
        return tab;
    }

    // leptonica: makeReverseByteTab2 (rotateorth.c)
    /// <remarks>
    /// (1) This generates an 8 bit lookup table for reversing
    ///     the order of four 2-bit pixels.
    /// </remarks>
    private static byte[] makeReverseByteTab2()
    {
        int i;
        byte[] tab;

        tab = new byte[256];
        for (i = 0; i < 256; i++)
            tab[i] = (byte)(((0xc0 & i) >> 6) |
                            ((0x30 & i) >> 2) |
                            ((0x0c & i) << 2) |
                            ((0x03 & i) << 6));
        return tab;
    }

    // leptonica: makeReverseByteTab4 (rotateorth.c)
    /// <remarks>
    /// (1) This generates an 8 bit lookup table for reversing
    ///     the order of two 4-bit pixels.
    /// </remarks>
    private static byte[] makeReverseByteTab4()
    {
        int i;
        byte[] tab;

        tab = new byte[256];
        for (i = 0; i < 256; i++)
            tab[i] = (byte)(((0xf0 & i) >> 4) | ((0x0f & i) << 4));
        return tab;
    }
}
