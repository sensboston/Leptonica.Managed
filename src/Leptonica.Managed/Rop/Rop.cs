using System;

namespace Leptonica;

/*
 *  rop.c: General rasterop and translation.
 *
 *  General rasterop
 *      pixRasterop
 *  In-place full band translation
 *      pixRasteropVip, pixRasteropHip
 *  Full image translation (general and in-place)
 *      pixTranslate, pixRasteropIP
 *  Full image rasterop with no translation
 *      pixRasteropFullImage
 *  Checking for invalid crop box
 *      checkRasteropCrop (static; its only uses are disabled in C)
 *
 *  Stubs: pixSetAllArbitrary (pix2.c), pixcmapGetRankIntensity (colormap.c);
 *  they are only reached for colormapped images.
 */

/// <summary>Rasterop codes and related flags from pix.h.</summary>
public static partial class L
{
    /*-------------------------------------------------------------------------*
     *                         Raster operation codes (pix.h)                  *
     *-------------------------------------------------------------------------*/
    /// <summary>Rasterop code: use the source pixels.</summary>
    public const int PIX_SRC = 0xc;
    /// <summary>Rasterop code: use the destination pixels.</summary>
    public const int PIX_DST = 0xa;
    /// <summary>Rasterop code: clear the pixels.</summary>
    public const int PIX_CLR = 0x0;
    /// <summary>Rasterop code: set the pixels.</summary>
    public const int PIX_SET = 0xf;

    /// <summary>PIX_NOT(op): inverts operation op.</summary>
    public static int PIX_NOT(int op) => op ^ 0x0f;

    /// <summary>Rasterop code: paint, <c>src | dst</c>.</summary>
    public const int PIX_PAINT = PIX_SRC | PIX_DST;
    /// <summary>Rasterop code: mask, <c>src &amp; dst</c>.</summary>
    public const int PIX_MASK = PIX_SRC & PIX_DST;
    /// <summary>Rasterop code: subtract, <c>dst &amp; ~src</c>.</summary>
    public const int PIX_SUBTRACT = PIX_DST & (PIX_SRC ^ 0x0f);
    /// <summary>Rasterop code: exclusive or, <c>src ^ dst</c>.</summary>
    public const int PIX_XOR = PIX_SRC ^ PIX_DST;

    /*-------------------------------------------------------------------------*
     *               Colors brought in by translation (pix.h)                  *
     *-------------------------------------------------------------------------*/
    /// <summary>Bring in white pixels from outside the image.</summary>
    public const int L_BRING_IN_WHITE = 1;
    /// <summary>Bring in black pixels from outside the image.</summary>
    public const int L_BRING_IN_BLACK = 2;
}

public sealed partial class Pix
{
    /*--------------------------------------------------------------------*
     *                General rasterop (basic pix interface)              *
     *--------------------------------------------------------------------*/
    // leptonica: pixRasterop (rop.c)
    /// <summary>The general rasterop: combines a rectangle of pixs into this (dest) pix.</summary>
    /// <param name="dx">x val of UL corner of dest rectangle</param>
    /// <param name="dy">y val of UL corner of dest rectangle</param>
    /// <param name="dw">width of dest rectangle</param>
    /// <param name="dh">height of dest rectangle</param>
    /// <param name="op">op code</param>
    /// <param name="pixs">src pix; may be null for ops that involve only the dest</param>
    /// <param name="sx">x val of UL corner of src rectangle</param>
    /// <param name="sy">y val of UL corner of src rectangle</param>
    /// <remarks>
    /// (1) This has the standard set of 9 args for rasterop.
    ///     This function is your friend; it is worth memorizing!
    /// (2) If the operation involves only dest, this calls
    ///     rasteropUniLow().  Otherwise, checks depth of the
    ///     src and dest, and if they match, calls rasteropLow().
    /// (3) For the two-image operation, where both pixs and pixd
    ///     are defined, they are typically different images.  However
    ///     there are cases, such as pixSetMirroredBorder(), where
    ///     in-place operations can be done, blitting pixels from
    ///     one part of pixd to another.  Consequently, we permit
    ///     such operations.  If you use them, be sure that there
    ///     is no overlap between the source and destination rectangles
    ///     in pixd (!)
    ///
    /// Background:
    /// -----------
    ///
    /// There are 18 operations, described by the op codes in pix.h.
    ///
    /// One, PIX_DST, is a no-op.
    ///
    /// Three, PIX_CLR, PIX_SET, and PIX_NOT(PIX_DST) operate only on the dest.
    /// These are handled by the low-level rasteropUniLow().
    ///
    /// The other 14 involve both the src and the dest, and depend on
    /// the bit values of either just the src or the bit values of both
    /// src and dest.  They are handled by rasteropLow():
    ///
    ///         PIX_SRC                             s
    ///         PIX_NOT(PIX_SRC)                   ~s
    ///         PIX_SRC | PIX_DST                   s | d
    ///         PIX_SRC &amp; PIX_DST                   s &amp; d
    ///         PIX_SRC ^ PIX_DST                   s ^ d
    ///         PIX_NOT(PIX_SRC) | PIX_DST         ~s | d
    ///         PIX_NOT(PIX_SRC) &amp; PIX_DST         ~s &amp; d
    ///         PIX_NOT(PIX_SRC) ^ PIX_DST         ~s ^ d
    ///         PIX_SRC | PIX_NOT(PIX_DST)          s | ~d
    ///         PIX_SRC &amp; PIX_NOT(PIX_DST)          s &amp; ~d
    ///         PIX_SRC ^ PIX_NOT(PIX_DST)          s ^ ~d
    ///         PIX_NOT(PIX_SRC | PIX_DST)         ~(s | d)
    ///         PIX_NOT(PIX_SRC &amp; PIX_DST)         ~(s &amp; d)
    ///         PIX_NOT(PIX_SRC ^ PIX_DST)         ~(s ^ d)
    ///
    /// Each of these is implemented with one of three low-level
    /// functions, depending on the alignment of the left edge
    /// of the src and dest rectangles:
    ///     * a fastest implementation if both left edges are
    ///       (32-bit) word aligned
    ///     * a very slightly slower implementation if both left
    ///       edges have the same relative (32-bit) word alignment
    ///     * the general routine that is invoked when
    ///       both left edges have different word alignment
    ///
    /// Of the 14 binary rasterops above, only 12 are unique
    /// logical combinations (out of a possible 16) of src
    /// and dst bits:
    ///
    ///       (sd)         (11)   (10)   (01)   (00)
    ///  -----------------------------------------------
    ///        s            1      1      0      0
    ///       ~s            0      1      0      1
    ///      s | d          1      1      1      0
    ///      s &amp; d          1      0      0      0
    ///      s ^ d          0      1      1      0
    ///     ~s | d          1      0      1      1
    ///     ~s &amp; d          0      0      1      0
    ///     ~s ^ d          1      0      0      1
    ///      s | ~d         1      1      0      1
    ///      s &amp; ~d         0      1      0      0
    ///      s ^ ~d         1      0      0      1
    ///     ~(s | d)        0      0      0      1
    ///     ~(s &amp; d)        0      1      1      1
    ///     ~(s ^ d)        1      0      0      1
    ///
    /// Note that the following three operations are equivalent:
    ///     ~(s ^ d)
    ///     ~s ^ d
    ///     s ^ ~d
    /// and in the implementation, we call them out with the first form;
    /// namely, ~(s ^ d).
    ///
    /// Of the 16 possible binary combinations of src and dest bits,
    /// the remaining 4 unique ones are independent of the src bit.
    /// They depend on either just the dest bit or on neither
    /// the src nor dest bits:
    ///
    ///        d            1      0      1      0    (indep. of s)
    ///       ~d            0      1      0      1    (indep. of s)
    ///       CLR           0      0      0      0    (indep. of both s &amp; d)
    ///       SET           1      1      1      1    (indep. of both s &amp; d)
    ///
    /// As mentioned above, three of these are implemented by
    /// rasteropUniLow(), and one is a no-op.
    ///
    /// How can these operation codes be represented by bits
    /// in such a way that when the basic operations are performed
    /// on the bits the results are unique for unique
    /// operations, and mimic the logic table given above?
    ///
    /// The answer is to choose a particular order of the pairings:
    ///        (sd)         (11)   (10)   (01)   (00)
    /// (which happens to be the same as in the above table)
    /// and to translate the result into 4-bit representations
    /// of s and d.  For example, the Sun rasterop choice
    /// (omitting the extra bit for clipping) is
    ///
    ///     PIX_SRC      0xc
    ///     PIX_DST      0xa
    ///
    /// This corresponds to our pairing order given above:
    ///        (sd)         (11)   (10)   (01)   (00)
    /// where for s = 1 we get the bit pattern
    ///      PIX_SRC:        1      1      0      0     (0xc)
    /// and for d = 1 we get the pattern
    ///      PIX_DST:         1      0      1      0    (0xa)
    ///
    /// OK, that's the pairing order that Sun chose.  How many different
    /// ways can we assign bit patterns to PIX_SRC and PIX_DST to get
    /// the boolean ops to work out?  Any of the 4 pairs can be put
    /// in the first position, any of the remaining 3 pairs can go
    /// in the second; and one of the remaining 2 pairs can go the the third.
    /// There is a total of 4*3*2 = 24 ways these pairs can be permuted.
    /// </remarks>
    public void Rasterop(int dx, int dy, int dw, int dh, int op,
                         Pix? pixs, int sx, int sy)
    {
        int dpw, dph, dpd, spw, sph, spd;

        if (op == L.PIX_DST)   /* no-op */
            return;

        GetDimensions(out dpw, out dph, out dpd);
#if false
        if (checkRasteropCrop(dpw, dph, dx, dy, dw, dh) != 0) {
            Log.Warning("pixRasterop", "dest crop box out of bounds");
            return;
        }
#endif

            /* Check if operation is only on dest */
        if (op == L.PIX_CLR || op == L.PIX_SET || op == L.PIX_NOT(L.PIX_DST)) {
            rasteropUniLow(Data, dpw, dph, dpd, Wpl,
                           dx, dy, dw, dh, op);
            return;
        }

            /* Two-image rasterop; the depths must match */
        if (pixs == null)
            throw Lept.Arg("pixRasterop", "pixs not defined");
        pixs.GetDimensions(out spw, out sph, out spd);
        if (dpd != spd)
            throw Lept.Arg("pixRasterop", "depths of pixs and pixd differ");
#if false
        if (checkRasteropCrop(spw, sph, sx, sy, dw, dh) != 0) {
            Log.Warning("pixRasterop", "source crop box out of bounds");
            return;
        }
#endif

        rasteropLow(Data, dpw, dph, dpd, Wpl,
                    dx, dy, dw, dh, op,
                    pixs.Data, spw, sph, pixs.Wpl, sx, sy);
    }

    /*--------------------------------------------------------------------*
     *                    In-place full band translation                  *
     *--------------------------------------------------------------------*/
    // leptonica: pixRasteropVip (rop.c)
    /// <summary>In-place translation of a full-height vertical band of this pix.</summary>
    /// <param name="bx">left edge of vertical band</param>
    /// <param name="bw">width of vertical band</param>
    /// <param name="vshift">vertical shift of band; vshift > 0 is down</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <remarks>
    /// (1) This rasterop translates a vertical band of the
    ///     image either up or down, bringing in either white
    ///     or black pixels from outside the image.
    /// (2) The vertical band extends the full height of pixd.
    /// (3) If a colormap exists, the nearest color to white or black
    ///     is brought in.
    /// </remarks>
    public void RasteropVip(int bx, int bw, int vshift, int incolor)
    {
        int w, h, d, index, op;
        Pix pixt;
        PixColormap? cmap;

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg("pixRasteropVip", "invalid value for incolor");
        if (bw <= 0)
            throw Lept.Arg("pixRasteropVip", "bw must be > 0");

        if (vshift == 0)
            return;

        GetDimensions(out w, out h, out d);
        rasteropVipLow(Data, w, h, d, Wpl, bx, bw, vshift);

        cmap = Colormap;
        if (cmap == null) {
            if ((d == 1 && incolor == L.L_BRING_IN_BLACK) ||
                (d > 1 && incolor == L.L_BRING_IN_WHITE))
                op = L.PIX_SET;
            else
                op = L.PIX_CLR;

                /* Set the pixels brought in at top or bottom */
            if (vshift > 0)
                Rasterop(bx, 0, bw, vshift, op, null, 0, 0);
            else  /* vshift < 0 */
                Rasterop(bx, h + vshift, bw, -vshift, op, null, 0, 0);
            return;
        }

            /* Get the nearest index and fill with that */
        if (incolor == L.L_BRING_IN_BLACK)
            index = cmap.GetRankIntensity(0.0f);
        else  /* white */
            index = cmap.GetRankIntensity(1.0f);
        pixt = Create(bw, Math.Abs(vshift), d);
        pixt.SetAllArbitrary((uint)index);
        if (vshift > 0)
            Rasterop(bx, 0, bw, vshift, L.PIX_SRC, pixt, 0, 0);
        else  /* vshift < 0 */
            Rasterop(bx, h + vshift, bw, -vshift, L.PIX_SRC, pixt, 0, 0);
    }

    // leptonica: pixRasteropHip (rop.c)
    /// <summary>In-place translation of a full-width horizontal band of this pix.</summary>
    /// <param name="by">top of horizontal band</param>
    /// <param name="bh">height of horizontal band</param>
    /// <param name="hshift">horizontal shift of band; hshift > 0 is to right</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <remarks>
    /// (1) This rasterop translates a horizontal band of the
    ///     image either left or right, bringing in either white
    ///     or black pixels from outside the image.
    /// (2) The horizontal band extends the full width of pixd.
    /// (3) If a colormap exists, the nearest color to white or black
    ///     is brought in.
    /// </remarks>
    public void RasteropHip(int by, int bh, int hshift, int incolor)
    {
        int w, h, d, index, op;
        Pix pixt;
        PixColormap? cmap;

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg("pixRasteropHip", "invalid value for incolor");
        if (bh <= 0)
            throw Lept.Arg("pixRasteropHip", "bh must be > 0");

        if (hshift == 0)
            return;

        GetDimensions(out w, out h, out d);
        rasteropHipLow(Data, h, d, Wpl, by, bh, hshift);

        cmap = Colormap;
        if (cmap == null) {
            if ((d == 1 && incolor == L.L_BRING_IN_BLACK) ||
                (d > 1 && incolor == L.L_BRING_IN_WHITE))
                op = L.PIX_SET;
            else
                op = L.PIX_CLR;

                /* Set the pixels brought in at left or right */
            if (hshift > 0)
                Rasterop(0, by, hshift, bh, op, null, 0, 0);
            else  /* hshift < 0 */
                Rasterop(w + hshift, by, -hshift, bh, op, null, 0, 0);
            return;
        }

            /* Get the nearest index and fill with that */
        if (incolor == L.L_BRING_IN_BLACK)
            index = cmap.GetRankIntensity(0.0f);
        else  /* white */
            index = cmap.GetRankIntensity(1.0f);
        pixt = Create(Math.Abs(hshift), bh, d);
        pixt.SetAllArbitrary((uint)index);
        if (hshift > 0)
            Rasterop(0, by, hshift, bh, L.PIX_SRC, pixt, 0, 0);
        else  /* hshift < 0 */
            Rasterop(w + hshift, by, -hshift, bh, L.PIX_SRC, pixt, 0, 0);
    }

    /*--------------------------------------------------------------------*
     *             Full image translation (general and in-place)          *
     *--------------------------------------------------------------------*/
    // leptonica: pixTranslate (rop.c)
    /// <summary>Translates the whole image, bringing in white or black pixels.</summary>
    /// <param name="pixd">optional destination: this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">source</param>
    /// <param name="hshift">horizontal shift; hshift > 0 is to right</param>
    /// <param name="vshift">vertical shift; vshift > 0 is down</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) The general pattern is:
    ///       pixd = pixTranslate(pixd, pixs, ...);
    ///     For clarity, when you know the case, use one of these:
    ///       pixd = pixTranslate(NULL, pixs, ...);  // new
    ///       pixTranslate(pixs, pixs, ...);         // in-place
    ///       pixTranslate(pixd, pixs, ...);         // to existing pixd
    /// (2) If an existing pixd is not the same size as pixs, the
    ///     image data will be reallocated.
    /// </remarks>
    public static Pix Translate(Pix? pixd, Pix pixs, int hshift, int vshift, int incolor)
    {
            /* Prepare pixd for in-place operation */
        pixd = Copy(pixd, pixs);

        pixd.RasteropIP(hshift, vshift, incolor);
        return pixd;
    }

    // leptonica: pixRasteropIP (rop.c)
    /// <summary>In-place translation of this pix.</summary>
    /// <param name="hshift">horizontal shift; hshift > 0 is to right</param>
    /// <param name="vshift">vertical shift; vshift > 0 is down</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    public void RasteropIP(int hshift, int vshift, int incolor)
    {
        int w, h;

        GetDimensions(out w, out h, out _);
        RasteropHip(0, h, hshift, incolor);
        RasteropVip(0, w, vshift, incolor);
    }

    /*--------------------------------------------------------------------*
     *                 Full image rasterop with no shifts                 *
     *--------------------------------------------------------------------*/
    // leptonica: pixRasteropFullImage (rop.c)
    /// <summary>Rasterop of the whole pixs into this pix with aligned UL corners.</summary>
    /// <param name="pixs">source</param>
    /// <param name="op">any of the op-codes</param>
    /// <remarks>
    /// ~ this is a wrapper for a common 2-image raster operation
    /// ~ both pixs and pixd must be defined
    /// ~ the operation is performed with aligned UL corners of pixs and pixd
    /// ~ the operation clips to the smallest pix; if the width or height
    ///   of pixd is larger than pixs, some pixels in pixd will be unchanged
    /// </remarks>
    public void RasteropFullImage(Pix pixs, int op)
    {
        Rasterop(0, 0, Width, Height, op, pixs, 0, 0);
    }

    /*--------------------------------------------------------------------*
     *                    Checking for invalid crop box                   *
     *--------------------------------------------------------------------*/
    // leptonica: checkRasteropCrop (rop.c)
    /// <summary>Checks the crop box against the pix dimensions.</summary>
    /// <param name="pixw">pix width</param>
    /// <param name="pixh">pix height</param>
    /// <param name="x">crop box x</param>
    /// <param name="y">crop box y</param>
    /// <param name="w">crop box width</param>
    /// <param name="h">crop box height</param>
    /// <returns>0 if OK, 1 if the crop box does not intersect with the pix.</returns>
    /// <remarks>
    /// (1) The widths and heights must all be positive, but %x and %y
    ///     can take on any value.
    /// (2) This works for checking both the source and dest regions.
    /// (3) This has been used to verify rasteropLow() cropping is correct.
    ///     It is not needed for pre-filtering in pixRasterop().
    /// </remarks>
    private static int checkRasteropCrop(int pixw, int pixh, int x, int y, int w, int h)
    {
        if (pixw < 1 || pixh < 1 || w < 1 || h < 1) {
            Log.Error("checkRasteropCrop", "dimension is <= 0");
            return 1;
        }

        if (x + w <= 0 || y + h <= 0) {
            Log.Error("checkRasteropCrop", "box to left or above pix");
            return 1;
        }

        if (x >= pixw || y >= pixh) {
            Log.Error("checkRasteropCrop", "box to right or below pix");
            return 1;
        }

        return 0;
    }
}
