using System;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  shear.c: Horizontal and vertical shear.
 *
 *  About arbitrary lines
 *      pixHShear, pixVShear
 *  About special 'points': UL corner and center
 *      pixHShearCorner, pixVShearCorner, pixHShearCenter, pixVShearCenter
 *  In place about arbitrary lines
 *      pixHShearIP, pixVShearIP
 *  Linear interpolated shear about arbitrary lines
 *      pixHShearLI, pixVShearLI
 *  Static helper
 *      normalizeAngleForShear
 *
 *  Differences from C:
 *    - The C pointers into the image data (lines, lined) are word offsets
 *      into Pix.Data; the ArrayAccess calls take a span that starts at the line.
 *    - The in-place functions (pixHShearIP, pixVShearIP) return void and throw
 *      on invalid arguments.
 *    - The C DEBUG output is not ported.
 *    - Float arithmetic follows C: tan() is the double function applied to the
 *      float angle, and the results are stored in float variables.
 *
 *  Not ported: nothing.
 */
public sealed partial class Pix
{
        /* Shear angle must not get too close to -pi/2 or pi/2 */
    private const float MinDiffFromHalfPi = 0.04f;

    /*-------------------------------------------------------------*
     *                    About arbitrary lines                    *
     *-------------------------------------------------------------*/
    // leptonica: pixHShear (shear.c)
    /// <summary>Horizontal shear about the line at y = yloc.</summary>
    /// <param name="pixd">[optional] this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">any depth; cmap ok</param>
    /// <param name="yloc">location of horizontal line, measured from origin</param>
    /// <param name="radang">angle in radians</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd, always</returns>
    /// <remarks>
    /// (1) There are 3 cases:
    ///       (a) pixd == null (make a new pixd)
    ///       (b) pixd == pixs (in-place)
    ///       (c) pixd != pixs
    /// (2) For these three cases, use these patterns, respectively:
    ///         pixd = pixHShear(NULL, pixs, ...);
    ///         pixHShear(pixs, pixs, ...);
    ///         pixHShear(pixd, pixs, ...);
    /// (3) This shear leaves the horizontal line of pixels at y = yloc
    ///     invariant.  For a positive shear angle, pixels above this
    ///     line are shoved to the right, and pixels below this line
    ///     move to the left.
    /// (4) With positive shear angle, this can be used, along with
    ///     pixVShear(), to perform a cw rotation, either with 2 shears
    ///     (for small angles) or in the general case with 3 shears.
    /// (5) Changing the value of yloc is equivalent to translating
    ///     the result horizontally.
    /// (6) This brings in %incolor pixels from outside the image.
    /// (7) In-place shears do not work on cmapped pix, because the
    ///     in-place operation cannot initialize to the requested %incolor,
    ///     so we shear from a copy.
    /// (8) The angle is brought into the range [-pi, -pi].  It is
    ///     not permitted to be within MinDiffFromHalfPi radians
    ///     from either -pi/2 or pi/2.
    /// </remarks>
    public static Pix HShear(Pix? pixd, Pix pixs, int yloc, float radang, int incolor)
    {
        const string func = "pixHShear";
        int sign, w, h;
        int y, yincr, inityincr, hshift;
        float tanangle, invangle;

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor value");

        if (pixd == pixs) {  /* in place */
            if (pixs.Colormap == null) {
                pixd!.HShearIP(yloc, radang, incolor);
            } else {  /* can't do in-place with a colormap */
                Pix pix1 = Copy(null, pixs);
                HShear(pixd, pix1, yloc, radang, incolor);
            }
            return pixd!;
        }

            /* Make sure pixd exists and is same size as pixs */
        if (pixd == null) {
            pixd = CreateTemplate(pixs);
        } else {  /* pixd != pixs */
            pixd.ResizeImageData(pixs);
        }

            /* Normalize angle.  If no rotation, return a copy */
        radang = NormalizeAngleForShear(radang, MinDiffFromHalfPi);
        if (radang == 0.0 || Math.Tan(radang) == 0.0)
            return Copy(pixd, pixs);

            /* Initialize to value of incoming pixels */
        pixd.SetBlackOrWhite(incolor);

        pixs.GetDimensions(out w, out h, out _);
        sign = radang < 0 ? -1 : 1;   /* L_SIGN */
        tanangle = (float)Math.Tan(radang);
        invangle = (float)Math.Abs(1.0 / tanangle);   /* L_ABS */
        inityincr = (int)(invangle / 2.0);
        yincr = (int)invangle;
        pixd.Rasterop(0, yloc - inityincr, w, 2 * inityincr, L.PIX_SRC,
                      pixs, 0, yloc - inityincr);

        for (hshift = 1, y = yloc + inityincr; y < h; hshift++) {
            yincr = (int)(invangle * (hshift + 0.5) + 0.5) - (y - yloc);
            if (h - y < yincr)  /* reduce for last one if req'd */
                yincr = h - y;
            pixd.Rasterop(-sign * hshift, y, w, yincr, L.PIX_SRC, pixs, 0, y);
            y += yincr;
        }

        for (hshift = -1, y = yloc - inityincr; y > 0; hshift--) {
            yincr = (y - yloc) - (int)(invangle * (hshift - 0.5) + 0.5);
            if (y < yincr)  /* reduce for last one if req'd */
                yincr = y;
            pixd.Rasterop(-sign * hshift, y - yincr, w, yincr, L.PIX_SRC,
                          pixs, 0, y - yincr);
            y -= yincr;
        }

        return pixd;
    }


    // leptonica: pixVShear (shear.c)
    /// <summary>Vertical shear about the line at x = xloc.</summary>
    /// <param name="pixd">[optional], this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">any depth; cmap ok</param>
    /// <param name="xloc">location of vertical line, measured from origin</param>
    /// <param name="radang">angle in radians; not too close to +-(pi / 2)</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) There are 3 cases:
    ///       (a) pixd == null (make a new pixd)
    ///       (b) pixd == pixs (in-place)
    ///       (c) pixd != pixs
    /// (2) For these three cases, use these patterns, respectively:
    ///         pixd = pixVShear(NULL, pixs, ...);
    ///         pixVShear(pixs, pixs, ...);
    ///         pixVShear(pixd, pixs, ...);
    /// (3) This shear leaves the vertical line of pixels at x = xloc
    ///     invariant.  For a positive shear angle, pixels to the right
    ///     of this line are shoved downward, and pixels to the left
    ///     of the line move upward.
    /// (4) With positive shear angle, this can be used, along with
    ///     pixHShear(), to perform a cw rotation, either with 2 shears
    ///     (for small angles) or in the general case with 3 shears.
    /// (5) Changing the value of xloc is equivalent to translating
    ///     the result vertically.
    /// (6) This brings in %incolor pixels from outside the image.
    /// (7) In-place shears do not work on cmapped pix, because the
    ///     in-place operation cannot initialize to the requested %incolor,
    ///     so we shear from a copy.
    /// (8) The angle is brought into the range [-pi, -pi].  It is
    ///     not permitted to be within MinDiffFromHalfPi radians
    ///     from either -pi/2 or pi/2.
    /// </remarks>
    public static Pix VShear(Pix? pixd, Pix pixs, int xloc, float radang, int incolor)
    {
        const string func = "pixVShear";
        int sign, w, h;
        int x, xincr, initxincr, vshift;
        float tanangle, invangle;

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor value");

        if (pixd == pixs) {  /* in place */
            if (pixs.Colormap == null) {
                pixd!.VShearIP(xloc, radang, incolor);
            } else {  /* can't do in-place with a colormap */
                Pix pix1 = Copy(null, pixs);
                VShear(pixd, pix1, xloc, radang, incolor);
            }
            return pixd!;
        }

            /* Make sure pixd exists and is same size as pixs */
        if (pixd == null) {
            pixd = CreateTemplate(pixs);
        } else {  /* pixd != pixs */
            pixd.ResizeImageData(pixs);
        }

            /* Normalize angle.  If no rotation, return a copy */
        radang = NormalizeAngleForShear(radang, MinDiffFromHalfPi);
        if (radang == 0.0 || Math.Tan(radang) == 0.0)
            return Copy(pixd, pixs);

            /* Initialize to value of incoming pixels */
        pixd.SetBlackOrWhite(incolor);

        pixs.GetDimensions(out w, out h, out _);
        sign = radang < 0 ? -1 : 1;   /* L_SIGN */
        tanangle = (float)Math.Tan(radang);
        invangle = (float)Math.Abs(1.0 / tanangle);   /* L_ABS */
        initxincr = (int)(invangle / 2.0);
        xincr = (int)invangle;
        pixd.Rasterop(xloc - initxincr, 0, 2 * initxincr, h, L.PIX_SRC,
                      pixs, xloc - initxincr, 0);

        for (vshift = 1, x = xloc + initxincr; x < w; vshift++) {
            xincr = (int)(invangle * (vshift + 0.5) + 0.5) - (x - xloc);
            if (w - x < xincr)  /* reduce for last one if req'd */
                xincr = w - x;
            pixd.Rasterop(x, sign * vshift, xincr, h, L.PIX_SRC, pixs, x, 0);
            x += xincr;
        }

        for (vshift = -1, x = xloc - initxincr; x > 0; vshift--) {
            xincr = (x - xloc) - (int)(invangle * (vshift - 0.5) + 0.5);
            if (x < xincr)  /* reduce for last one if req'd */
                xincr = x;
            pixd.Rasterop(x - xincr, sign * vshift, xincr, h, L.PIX_SRC,
                          pixs, x - xincr, 0);
            x -= xincr;
        }

        return pixd;
    }


    /*-------------------------------------------------------------*
     *             Shears about UL corner and center               *
     *-------------------------------------------------------------*/
    // leptonica: pixHShearCorner (shear.c)
    /// <summary>Horizontal shear about the UL corner.</summary>
    /// <param name="pixd">[optional], if not null, must be equal to pixs</param>
    /// <param name="pixs">any depth</param>
    /// <param name="radang">angle in radians</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) See pixHShear() for usage.
    /// (2) This does a horizontal shear about the UL corner, with (+) shear
    ///     pushing increasingly leftward (-x) with increasing y.
    /// </remarks>
    public static Pix HShearCorner(Pix? pixd, Pix pixs, float radang, int incolor)
    {
        return HShear(pixd, pixs, 0, radang, incolor);
    }


    // leptonica: pixVShearCorner (shear.c)
    /// <summary>Vertical shear about the UL corner.</summary>
    /// <param name="pixd">[optional], if not null, must be equal to pixs</param>
    /// <param name="pixs">any depth</param>
    /// <param name="radang">angle in radians</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) See pixVShear() for usage.
    /// (2) This does a vertical shear about the UL corner, with (+) shear
    ///     pushing increasingly downward (+y) with increasing x.
    /// </remarks>
    public static Pix VShearCorner(Pix? pixd, Pix pixs, float radang, int incolor)
    {
        return VShear(pixd, pixs, 0, radang, incolor);
    }


    // leptonica: pixHShearCenter (shear.c)
    /// <summary>Horizontal shear about the center line.</summary>
    /// <param name="pixd">[optional] if not null, must be equal to pixs</param>
    /// <param name="pixs">any depth</param>
    /// <param name="radang">angle in radians</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) See pixHShear() for usage.
    /// (2) This does a horizontal shear about the center, with (+) shear
    ///     pushing increasingly leftward (-x) with increasing y.
    /// </remarks>
    public static Pix HShearCenter(Pix? pixd, Pix pixs, float radang, int incolor)
    {
        return HShear(pixd, pixs, pixs.Height / 2, radang, incolor);
    }


    // leptonica: pixVShearCenter (shear.c)
    /// <summary>Vertical shear about the center line.</summary>
    /// <param name="pixd">[optional] if not null, must be equal to pixs</param>
    /// <param name="pixs">any depth</param>
    /// <param name="radang">angle in radians</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) See pixVShear() for usage.
    /// (2) This does a vertical shear about the center, with (+) shear
    ///     pushing increasingly downward (+y) with increasing x.
    /// </remarks>
    public static Pix VShearCenter(Pix? pixd, Pix pixs, float radang, int incolor)
    {
        return VShear(pixd, pixs, pixs.Width / 2, radang, incolor);
    }


    /*--------------------------------------------------------------------------*
     *                       In place about arbitrary lines                     *
     *--------------------------------------------------------------------------*/
    // leptonica: pixHShearIP (shear.c)
    /// <summary>In-place horizontal shear about the line at y = yloc.</summary>
    /// <param name="yloc">location of horizontal line, measured from origin</param>
    /// <param name="radang">angle in radians</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <remarks>
    /// (1) This is an in-place version of pixHShear(); see comments there.
    /// (2) This brings in 'incolor' pixels from outside the image.
    /// (3) pixs cannot be colormapped, because the in-place operation
    ///     only blits in 0 or 1 bits, not an arbitrary colormap index.
    /// (4) Does a horizontal full-band shear about the line with (+) shear
    ///     pushing increasingly leftward (-x) with increasing y.
    /// </remarks>
    public void HShearIP(int yloc, float radang, int incolor)
    {
        const string func = "pixHShearIP";
        int sign, w, h;
        int y, yincr, inityincr, hshift;
        float tanangle, invangle;

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor value");
        if (Colormap != null)
            throw Lept.Arg(func, "pixs is colormapped");

            /* Normalize angle */
        radang = NormalizeAngleForShear(radang, MinDiffFromHalfPi);
        if (radang == 0.0 || Math.Tan(radang) == 0.0)
            return;

        sign = radang < 0 ? -1 : 1;   /* L_SIGN */
        GetDimensions(out w, out h, out _);
        tanangle = (float)Math.Tan(radang);
        invangle = (float)Math.Abs(1.0 / tanangle);   /* L_ABS */
        inityincr = (int)(invangle / 2.0);
        yincr = (int)invangle;

        if (inityincr > 0)
            RasteropHip(yloc - inityincr, 2 * inityincr, 0, incolor);

        for (hshift = 1, y = yloc + inityincr; y < h; hshift++) {
            yincr = (int)(invangle * (hshift + 0.5) + 0.5) - (y - yloc);
            if (yincr == 0) continue;
            if (h - y < yincr)  /* reduce for last one if req'd */
                yincr = h - y;
            RasteropHip(y, yincr, -sign * hshift, incolor);
            y += yincr;
        }

        for (hshift = -1, y = yloc - inityincr; y > 0; hshift--) {
            yincr = (y - yloc) - (int)(invangle * (hshift - 0.5) + 0.5);
            if (yincr == 0) continue;
            if (y < yincr)  /* reduce for last one if req'd */
                yincr = y;
            RasteropHip(y - yincr, yincr, -sign * hshift, incolor);
            y -= yincr;
        }
    }


    // leptonica: pixVShearIP (shear.c)
    /// <summary>In-place vertical shear about the line at x = xloc.</summary>
    /// <param name="xloc">location of vertical line, measured from origin</param>
    /// <param name="radang">angle in radians</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <remarks>
    /// (1) This is an in-place version of pixVShear(); see comments there.
    /// (2) This brings in 'incolor' pixels from outside the image.
    /// (3) pixs cannot be colormapped, because the in-place operation
    ///     only blits in 0 or 1 bits, not an arbitrary colormap index.
    /// (4) Does a vertical full-band shear about the line with (+) shear
    ///     pushing increasingly downward (+y) with increasing x.
    /// </remarks>
    public void VShearIP(int xloc, float radang, int incolor)
    {
        const string func = "pixVShearIP";
        int sign, w, h;
        int x, xincr, initxincr, vshift;
        float tanangle, invangle;

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor value");
        if (Colormap != null)
            throw Lept.Arg(func, "pixs is colormapped");

            /* Normalize angle */
        radang = NormalizeAngleForShear(radang, MinDiffFromHalfPi);
        if (radang == 0.0 || Math.Tan(radang) == 0.0)
            return;

        sign = radang < 0 ? -1 : 1;   /* L_SIGN */
        GetDimensions(out w, out h, out _);
        tanangle = (float)Math.Tan(radang);
        invangle = (float)Math.Abs(1.0 / tanangle);   /* L_ABS */
        initxincr = (int)(invangle / 2.0);
        xincr = (int)invangle;

        if (initxincr > 0)
            RasteropVip(xloc - initxincr, 2 * initxincr, 0, incolor);

        for (vshift = 1, x = xloc + initxincr; x < w; vshift++) {
            xincr = (int)(invangle * (vshift + 0.5) + 0.5) - (x - xloc);
            if (xincr == 0) continue;
            if (w - x < xincr)  /* reduce for last one if req'd */
                xincr = w - x;
            RasteropVip(x, xincr, sign * vshift, incolor);
            x += xincr;
        }

        for (vshift = -1, x = xloc - initxincr; x > 0; vshift--) {
            xincr = (x - xloc) - (int)(invangle * (vshift - 0.5) + 0.5);
            if (xincr == 0) continue;
            if (x < xincr)  /* reduce for last one if req'd */
                xincr = x;
            RasteropVip(x - xincr, xincr, sign * vshift, incolor);
            x -= xincr;
        }
    }


    /*-------------------------------------------------------------------------*
     *              Linear interpolated shear about arbitrary lines            *
     *-------------------------------------------------------------------------*/
    // leptonica: pixHShearLI (shear.c)
    /// <summary>Horizontal shear with linear interpolation.</summary>
    /// <param name="yloc">location of horizontal line, measured from origin</param>
    /// <param name="radang">angle in radians, in range (-pi/2 ... pi/2)</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd sheared</returns>
    /// <remarks>
    /// (1) This does horizontal shear with linear interpolation for
    ///     accurate results on 8 bpp gray, 32 bpp rgb, or cmapped images.
    ///     It is relatively slow compared to the sampled version
    ///     implemented by rasterop, but the result is much smoother.
    /// (2) This shear leaves the horizontal line of pixels at y = yloc
    ///     invariant.  For a positive shear angle, pixels above this
    ///     line are shoved to the right, and pixels below this line
    ///     move to the left.
    /// (3) Any colormap is removed.
    /// (4) The angle is brought into the range [-pi/2 + del, pi/2 - del],
    ///     where del == MinDiffFromHalfPi.
    /// </remarks>
    public Pix HShearLI(int yloc, float radang, int incolor)
    {
        const string func = "pixHShearLI";
        int i, jd, x, xp, xf, w, h, d, wm, wpls, wpld, val, rval, gval, bval;
        uint word0, word1;
        float tanangle, xshift;
        Pix pix, pixd;

        GetDimensions(out w, out h, out d);
        if (d != 8 && d != 32 && Colormap == null)
            throw Lept.Arg(func, "pixs not 8, 32 bpp, or cmap");
        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor value");
        if (yloc < 0 || yloc >= h)
            throw Lept.Arg(func, "yloc not in [0 ... h-1]");

        if (Colormap != null)
            pix = RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        else
            pix = Clone(this);

            /* Normalize angle.  If no rotation, return a copy */
        radang = NormalizeAngleForShear(radang, MinDiffFromHalfPi);
        if (radang == 0.0 || Math.Tan(radang) == 0.0)
            return Copy(null, this);

            /* Initialize to value of incoming pixels */
        pixd = CreateTemplate(pix);
        pixd.SetBlackOrWhite(incolor);

            /* Standard linear interp: subdivide each pixel into 64 parts */
        d = pixd.Depth;  /* 8 or 32 */
        uint[] datas = pix.Data;
        uint[] datad = pixd.Data;
        wpls = pix.Wpl;
        wpld = pixd.Wpl;
        tanangle = (float)Math.Tan(radang);
        for (i = 0; i < h; i++) {
            int lines = i * wpls;
            int lined = i * wpld;
            xshift = (yloc - i) * tanangle;
            for (jd = 0; jd < w; jd++) {
                x = (int)(64.0 * (-xshift + jd) + 0.5);
                xp = x / 64;
                xf = x & 63;
                wm = w - 1;
                if (xp < 0 || xp > wm) continue;
                if (d == 8) {
                    if (xp < wm) {
                        val = ((63 - xf) * GetDataByte(datas.AsSpan(lines), xp) +
                               xf * GetDataByte(datas.AsSpan(lines), xp + 1) + 31) / 63;
                    } else {  /* xp == wm */
                        val = GetDataByte(datas.AsSpan(lines), xp);
                    }
                    SetDataByte(datad.AsSpan(lined), jd, val);
                } else {  /* d == 32 */
                    if (xp < wm) {
                        word0 = datas[lines + xp];
                        word1 = datas[lines + xp + 1];
                        rval = ((63 - xf) * (int)((word0 >> L.L_RED_SHIFT) & 0xff) +
                               xf * (int)((word1 >> L.L_RED_SHIFT) & 0xff) + 31) / 63;
                        gval = ((63 - xf) * (int)((word0 >> L.L_GREEN_SHIFT) & 0xff) +
                               xf * (int)((word1 >> L.L_GREEN_SHIFT) & 0xff) + 31) / 63;
                        bval = ((63 - xf) * (int)((word0 >> L.L_BLUE_SHIFT) & 0xff) +
                               xf * (int)((word1 >> L.L_BLUE_SHIFT) & 0xff) + 31) / 63;
                        ComposeRGBPixel(rval, gval, bval, out datad[lined + jd]);
                    } else {  /* xp == wm */
                        datad[lined + jd] = datas[lines + xp];
                    }
                }
            }
        }

        return pixd;
    }


    // leptonica: pixVShearLI (shear.c)
    /// <summary>Vertical shear with linear interpolation.</summary>
    /// <param name="xloc">location of vertical line, measured from origin</param>
    /// <param name="radang">angle in radians, in range (-pi/2 ... pi/2)</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd sheared</returns>
    /// <remarks>
    /// (1) This does vertical shear with linear interpolation for
    ///     accurate results on 8 bpp gray, 32 bpp rgb, or cmapped images.
    ///     It is relatively slow compared to the sampled version
    ///     implemented by rasterop, but the result is much smoother.
    /// (2) This shear leaves the vertical line of pixels at x = xloc
    ///     invariant.  For a positive shear angle, pixels to the right
    ///     of this line are shoved downward, and pixels to the left
    ///     of the line move upward.
    /// (3) Any colormap is removed.
    /// (4) The angle is brought into the range [-pi/2 + del, pi/2 - del],
    ///     where del == MinDiffFromHalfPi.
    /// </remarks>
    public Pix VShearLI(int xloc, float radang, int incolor)
    {
        const string func = "pixVShearLI";
        int id, y, yp, yf, j, w, h, d, hm, wpls, wpld, val, rval, gval, bval;
        uint word0, word1;
        float tanangle, yshift;
        Pix pix, pixd;

        GetDimensions(out w, out h, out d);
        if (d != 8 && d != 32 && Colormap == null)
            throw Lept.Arg(func, "pixs not 8, 32 bpp, or cmap");
        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor value");
        if (xloc < 0 || xloc >= w)
            throw Lept.Arg(func, "xloc not in [0 ... w-1]");

        if (Colormap != null)
            pix = RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        else
            pix = Clone(this);

            /* Normalize angle.  If no rotation, return a copy */
        radang = NormalizeAngleForShear(radang, MinDiffFromHalfPi);
        if (radang == 0.0 || Math.Tan(radang) == 0.0)
            return Copy(null, this);

            /* Initialize to value of incoming pixels */
        pixd = CreateTemplate(pix);
        pixd.SetBlackOrWhite(incolor);

            /* Standard linear interp: subdivide each pixel into 64 parts */
        d = pixd.Depth;  /* 8 or 32 */
        uint[] datas = pix.Data;
        uint[] datad = pixd.Data;
        wpls = pix.Wpl;
        wpld = pixd.Wpl;
        tanangle = (float)Math.Tan(radang);
        for (j = 0; j < w; j++) {
            yshift = (j - xloc) * tanangle;
            for (id = 0; id < h; id++) {
                y = (int)(64.0 * (-yshift + id) + 0.5);
                yp = y / 64;
                yf = y & 63;
                hm = h - 1;
                if (yp < 0 || yp > hm) continue;
                int lines = yp * wpls;
                int lined = id * wpld;
                if (d == 8) {
                    if (yp < hm) {
                        val = ((63 - yf) * GetDataByte(datas.AsSpan(lines), j) +
                               yf * GetDataByte(datas.AsSpan(lines + wpls), j) + 31) / 63;
                    } else {  /* yp == hm */
                        val = GetDataByte(datas.AsSpan(lines), j);
                    }
                    SetDataByte(datad.AsSpan(lined), j, val);
                } else {  /* d == 32 */
                    if (yp < hm) {
                        word0 = datas[lines + j];
                        word1 = datas[lines + wpls + j];
                        rval = ((63 - yf) * (int)((word0 >> L.L_RED_SHIFT) & 0xff) +
                               yf * (int)((word1 >> L.L_RED_SHIFT) & 0xff) + 31) / 63;
                        gval = ((63 - yf) * (int)((word0 >> L.L_GREEN_SHIFT) & 0xff) +
                               yf * (int)((word1 >> L.L_GREEN_SHIFT) & 0xff) + 31) / 63;
                        bval = ((63 - yf) * (int)((word0 >> L.L_BLUE_SHIFT) & 0xff) +
                               yf * (int)((word1 >> L.L_BLUE_SHIFT) & 0xff) + 31) / 63;
                        ComposeRGBPixel(rval, gval, bval, out datad[lined + j]);
                    } else {  /* yp == hm */
                        datad[lined + j] = datas[lines + j];
                    }
                }
            }
        }

        return pixd;
    }


    /*-------------------------------------------------------------------------*
     *                           Angle normalization                           *
     *-------------------------------------------------------------------------*/
    // leptonica: normalizeAngleForShear (shear.c)
    private static float NormalizeAngleForShear(float radang, float mindif)
    {
        float pi2;

           /* Bring angle into range [-pi/2, pi/2] */
        pi2 = 3.14159265f / 2.0f;
        if (radang < -pi2 || radang > pi2)
            radang = radang - (int)(radang / pi2) * pi2;

           /* If angle is too close to pi/2 or -pi/2, move it */
        if (radang > pi2 - mindif) {
            Log.Warning("normalizeAngleForShear", "angle close to pi/2; shifting away");
            radang = pi2 - mindif;
        } else if (radang < -pi2 + mindif) {
            Log.Warning("normalizeAngleForShear", "angle close to -pi/2; shifting away");
            radang = -pi2 + mindif;
        }

        return radang;
    }
}
