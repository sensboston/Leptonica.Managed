using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Leptonica;

/*
 *  graymorph.c: Grayscale morphology.
 *
 *      Top-level grayscale morphological operations (van Herk / Gil-Werman)
 *            Pix.ErodeGray
 *            Pix.DilateGray
 *            Pix.OpenGray
 *            Pix.CloseGray
 *
 *      Special operations for 1x3, 3x1 and 3x3 Sels  (direct)
 *            Pix.ErodeGray3
 *            private Pix.ErodeGray3h
 *            private Pix.ErodeGray3v
 *            Pix.DilateGray3
 *            private Pix.DilateGray3h
 *            private Pix.DilateGray3v
 *            Pix.OpenGray3
 *            Pix.CloseGray3
 *
 *      Low-level grayscale morphological operations
 *            private static Pix.DilateGrayLow, Pix.ErodeGrayLow  (GrayMorphLow.cs)
 *
 *
 *      Method: Algorithm by van Herk and Gil and Werman, 1992
 *
 *      Measured speed of the vH/G-W implementation is about 1 output
 *      pixel per 120 PIII clock cycles, for a horizontal or vertical
 *      erosion or dilation.  The computation time doubles for opening
 *      or closing, or for a square SE, as expected, and is independent
 *      of the size of the SE.
 *
 *      A faster implementation can be made directly for brick Sels
 *      of maximum size 3.  We unroll the computation for sets of 8 bytes.
 *      It needs to be called explicitly; the general functions do not
 *      default for the size 3 brick Sels.
 *
 *      We use the van Herk/Gil-Werman (vHGW) algorithm, [van Herk,
 *      Patt. Recog. Let. 13, pp. 517-521, 1992; Gil and Werman,
 *      IEEE Trans PAMI 15(5), pp. 504-507, 1993.]
 *      This was the first grayscale morphology
 *      algorithm to compute dilation and erosion with
 *      complexity independent of the size of the structuring
 *      element.  It is simple and elegant, and surprising that
 *      it was discovered as recently as 1992.  It works for
 *      SEs composed of horizontal and/or vertical lines.  The
 *      general case requires finding the Min or Max over an
 *      arbitrary set of pixels, and this requires a number of
 *      pixel comparisons equal to the SE "size" at each pixel
 *      in the image.  The vHGW algorithm requires not
 *      more than 3 comparisons at each point.  The algorithm has been
 *      recently refined by Gil and Kimmel ("Efficient Dilation
 *      Erosion, Opening and Closing Algorithms", in "Mathematical
 *      Morphology and its Applications to Image and Signal Processing",
 *      the proceedings of the International Symposium on Mathematical
 *      Morphology, Palo Alto, CA, June 2000, Kluwer Academic
 *      Publishers, pp. 301-310).  They bring this number down below
 *      1.5 comparisons per output pixel but at a cost of significantly
 *      increased complexity, so I don't bother with that here.
 *
 *      In brief, the method is as follows.  We evaluate the dilation
 *      in groups of "size" pixels, equal to the size of the SE.
 *      For horizontal, we start at x = "size"/2 and go
 *      (w - 2 * ("size"/2))/"size" steps.  This means that
 *      we don't evaluate the first 0.5 * "size" pixels and, worst
 *      case, the last 1.5 * "size" pixels.  Thus we embed the
 *      image in a larger image with these augmented dimensions, where
 *      the new border pixels are appropriately initialized (0 for
 *      dilation; 255 for erosion), and remove the boundary at the end.
 *      (For vertical, use h instead of w.)   Then for each group
 *      of "size" pixels, we form an array of length 2 * "size" + 1,
 *      consisting of backward and forward partial maxima (for
 *      dilation) or minima (for erosion).  This represents a
 *      jumping window computed from the source image, over which
 *      the SE will slide.  The center of the array gets the source
 *      pixel at the center of the SE.  Call this the center pixel
 *      of the window.  Array values to left of center get
 *      the maxima(minima) of the pixels from the center
 *      one and going to the left an equal distance.  Array
 *      values to the right of center get the maxima(minima) to
 *      the pixels from the center one and going to the right
 *      an equal distance.  These are computed sequentially starting
 *      from the center one.  The SE (of length "size") can slide over this
 *      window (of length 2 * "size + 1) at "size" different places.
 *      At each place, the maxima(minima) of the values in the window
 *      that correspond to the end points of the SE give the extremal
 *      values over that interval, and these are stored at the dest
 *      pixel corresponding to the SE center.  A picture is worth
 *      at least this many words, so if this isn't clear, see the
 *      leptonica documentation on grayscale morphology.
 *
 *  Error returns of the C code (NULL) are thrown as exceptions. Allocation of buffer and the min/max
 *  array cannot fail and needs no check.
 */
public sealed partial class Pix
{
    /*-----------------------------------------------------------------*
     *           Top-level grayscale morphological operations          *
     *-----------------------------------------------------------------*/
    // leptonica: pixErodeGray (graymorph.c)
    /// <summary>Gray morphological erosion with a brick Sel (van Herk / Gil-Werman).</summary>
    /// <param name="hsize">of Sel; must be odd; origin implicitly in center</param>
    /// <param name="vsize">ditto</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// Notes:
    ///   (1) Sel is a brick with all elements being hits
    ///   (2) If hsize = vsize = 1, just returns a copy.
    /// </remarks>
    public Pix ErodeGray(int hsize, int vsize)
    {
        const string func = "pixErodeGray";
        int w, h, wplb, wplt;
        int leftpix, rightpix, toppix, bottompix, maxsize;
        uint[] datab, datat;
        Pix pixb, pixt, pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize or vsize < 1");
        if ((hsize & 1) == 0) {
            Log.Warning(func, "horiz sel size must be odd; increasing by 1");
            hsize++;
        }
        if ((vsize & 1) == 0) {
            Log.Warning(func, "vert sel size must be odd; increasing by 1");
            vsize++;
        }

        if (hsize == 1 && vsize == 1)
            return Copy(null, this);

        if (Colormap == null &&
            LeptGpu.TryGetActive((long)Width * Height, hsize + vsize, out IGpuAccelerator? gpuErode) &&
            gpuErode.TryMorphGrayBrick(this, hsize, vsize, dilate: false, out Pix? gpuEroded)) {
            return gpuEroded;
        }

        if (vsize == 1) {  /* horizontal sel */
            leftpix = (hsize + 1) / 2;
            rightpix = (3 * hsize + 1) / 2;
            toppix = 0;
            bottompix = 0;
        } else if (hsize == 1) {  /* vertical sel */
            leftpix = 0;
            rightpix = 0;
            toppix = (vsize + 1) / 2;
            bottompix = (3 * vsize + 1) / 2;
        } else {
            leftpix = (hsize + 1) / 2;
            rightpix = (3 * hsize + 1) / 2;
            toppix = (vsize + 1) / 2;
            bottompix = (3 * vsize + 1) / 2;
        }

        pixb = AddBorderGeneral(leftpix, rightpix, toppix, bottompix, 255);
        pixt = CreateTemplate(pixb);

        w = pixt.Width;
        h = pixt.Height;
        datab = pixb.Data;
        datat = pixt.Data;
        wplb = pixb.Wpl;
        wplt = pixt.Wpl;

        byte[] buffer = new byte[Math.Max(w, h)];
        maxsize = Math.Max(hsize, vsize);
        byte[] minarray = new byte[2 * maxsize];

        if (vsize == 1) {
            ErodeGrayLow(datat, w, h, wplt, datab, wplb, hsize, L.L_HORIZ,
                         buffer, minarray);
        } else if (hsize == 1) {
            ErodeGrayLow(datat, w, h, wplt, datab, wplb, vsize, L.L_VERT,
                         buffer, minarray);
        } else {
            ErodeGrayLow(datat, w, h, wplt, datab, wplb, hsize, L.L_HORIZ,
                         buffer, minarray);
            pixt.SetOrClearBorder(leftpix, rightpix, toppix, bottompix,
                                  L.PIX_SET);
            ErodeGrayLow(datab, w, h, wplb, datat, wplt, vsize, L.L_VERT,
                         buffer, minarray);
            pixt = Clone(pixb);
        }

        pixd = pixt.RemoveBorderGeneral(leftpix, rightpix, toppix, bottompix);
        return pixd;
    }

    // leptonica: pixDilateGray (graymorph.c)
    /// <summary>Gray morphological dilation with a brick Sel (van Herk / Gil-Werman).</summary>
    /// <param name="hsize">of Sel; must be odd; origin implicitly in center</param>
    /// <param name="vsize">ditto</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// Notes:
    ///   (1) Sel is a brick with all elements being hits
    ///   (2) If hsize = vsize = 1, just returns a copy.
    /// </remarks>
    public Pix DilateGray(int hsize, int vsize)
    {
        const string func = "pixDilateGray";
        int w, h, wplb, wplt;
        int leftpix, rightpix, toppix, bottompix, maxsize;
        uint[] datab, datat;
        Pix pixb, pixt, pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize or vsize < 1");
        if ((hsize & 1) == 0) {
            Log.Warning(func, "horiz sel size must be odd; increasing by 1");
            hsize++;
        }
        if ((vsize & 1) == 0) {
            Log.Warning(func, "vert sel size must be odd; increasing by 1");
            vsize++;
        }

        if (hsize == 1 && vsize == 1)
            return Copy(null, this);

        if (Colormap == null &&
            LeptGpu.TryGetActive((long)Width * Height, hsize + vsize, out IGpuAccelerator? gpuDilate) &&
            gpuDilate.TryMorphGrayBrick(this, hsize, vsize, dilate: true, out Pix? gpuDilated)) {
            return gpuDilated;
        }

        if (vsize == 1) {  /* horizontal sel */
            leftpix = (hsize + 1) / 2;
            rightpix = (3 * hsize + 1) / 2;
            toppix = 0;
            bottompix = 0;
        } else if (hsize == 1) {  /* vertical sel */
            leftpix = 0;
            rightpix = 0;
            toppix = (vsize + 1) / 2;
            bottompix = (3 * vsize + 1) / 2;
        } else {
            leftpix = (hsize + 1) / 2;
            rightpix = (3 * hsize + 1) / 2;
            toppix = (vsize + 1) / 2;
            bottompix = (3 * vsize + 1) / 2;
        }

        pixb = AddBorderGeneral(leftpix, rightpix, toppix, bottompix, 0);
        pixt = CreateTemplate(pixb);

        w = pixt.Width;
        h = pixt.Height;
        datab = pixb.Data;
        datat = pixt.Data;
        wplb = pixb.Wpl;
        wplt = pixt.Wpl;

        byte[] buffer = new byte[Math.Max(w, h)];
        maxsize = Math.Max(hsize, vsize);
        byte[] maxarray = new byte[2 * maxsize];

        if (vsize == 1) {
            DilateGrayLow(datat, w, h, wplt, datab, wplb, hsize, L.L_HORIZ,
                          buffer, maxarray);
        } else if (hsize == 1) {
            DilateGrayLow(datat, w, h, wplt, datab, wplb, vsize, L.L_VERT,
                          buffer, maxarray);
        } else {
            DilateGrayLow(datat, w, h, wplt, datab, wplb, hsize, L.L_HORIZ,
                          buffer, maxarray);
            pixt.SetOrClearBorder(leftpix, rightpix, toppix, bottompix,
                                  L.PIX_CLR);
            DilateGrayLow(datab, w, h, wplb, datat, wplt, vsize, L.L_VERT,
                          buffer, maxarray);
            pixt = Clone(pixb);
        }

        pixd = pixt.RemoveBorderGeneral(leftpix, rightpix, toppix, bottompix);
        return pixd;
    }

    // leptonica: pixOpenGray (graymorph.c)
    /// <summary>Gray morphological opening with a brick Sel (van Herk / Gil-Werman).</summary>
    /// <param name="hsize">of Sel; must be odd; origin implicitly in center</param>
    /// <param name="vsize">ditto</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// Notes:
    ///   (1) Sel is a brick with all elements being hits
    ///   (2) If hsize = vsize = 1, just returns a copy.
    /// </remarks>
    public Pix OpenGray(int hsize, int vsize)
    {
        const string func = "pixOpenGray";
        int w, h, wplb, wplt;
        int leftpix, rightpix, toppix, bottompix, maxsize;
        uint[] datab, datat;
        Pix pixb, pixt, pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize or vsize < 1");
        if ((hsize & 1) == 0) {
            Log.Warning(func, "horiz sel size must be odd; increasing by 1");
            hsize++;
        }
        if ((vsize & 1) == 0) {
            Log.Warning(func, "vert sel size must be odd; increasing by 1");
            vsize++;
        }

        if (hsize == 1 && vsize == 1)
            return Copy(null, this);

        if (Colormap == null &&
            LeptGpu.TryGetActive((long)Width * Height, (hsize + vsize) * 2, out IGpuAccelerator? gpuOpen) &&
            gpuOpen.TryOpenCloseGrayBrick(this, hsize, vsize, open: true, out Pix? gpuOpened)) {
            return gpuOpened;
        }

        if (vsize == 1) {  /* horizontal sel */
            leftpix = (hsize + 1) / 2;
            rightpix = (3 * hsize + 1) / 2;
            toppix = 0;
            bottompix = 0;
        } else if (hsize == 1) {  /* vertical sel */
            leftpix = 0;
            rightpix = 0;
            toppix = (vsize + 1) / 2;
            bottompix = (3 * vsize + 1) / 2;
        } else {
            leftpix = (hsize + 1) / 2;
            rightpix = (3 * hsize + 1) / 2;
            toppix = (vsize + 1) / 2;
            bottompix = (3 * vsize + 1) / 2;
        }

        pixb = AddBorderGeneral(leftpix, rightpix, toppix, bottompix, 255);
        pixt = CreateTemplate(pixb);

        w = pixt.Width;
        h = pixt.Height;
        datab = pixb.Data;
        datat = pixt.Data;
        wplb = pixb.Wpl;
        wplt = pixt.Wpl;

        byte[] buffer = new byte[Math.Max(w, h)];
        maxsize = Math.Max(hsize, vsize);
        byte[] array = new byte[2 * maxsize];  /* used to find either min or max in interval */

        if (vsize == 1) {
            ErodeGrayLow(datat, w, h, wplt, datab, wplb, hsize, L.L_HORIZ,
                         buffer, array);
            pixt.SetOrClearBorder(leftpix, rightpix, toppix, bottompix,
                                  L.PIX_CLR);
            DilateGrayLow(datab, w, h, wplb, datat, wplt, hsize, L.L_HORIZ,
                          buffer, array);
        }
        else if (hsize == 1) {
            ErodeGrayLow(datat, w, h, wplt, datab, wplb, vsize, L.L_VERT,
                         buffer, array);
            pixt.SetOrClearBorder(leftpix, rightpix, toppix, bottompix,
                                  L.PIX_CLR);
            DilateGrayLow(datab, w, h, wplb, datat, wplt, vsize, L.L_VERT,
                          buffer, array);
        } else {
            ErodeGrayLow(datat, w, h, wplt, datab, wplb, hsize, L.L_HORIZ,
                         buffer, array);
            pixt.SetOrClearBorder(leftpix, rightpix, toppix, bottompix,
                                  L.PIX_SET);
            ErodeGrayLow(datab, w, h, wplb, datat, wplt, vsize, L.L_VERT,
                         buffer, array);
            pixb.SetOrClearBorder(leftpix, rightpix, toppix, bottompix,
                                  L.PIX_CLR);
            DilateGrayLow(datat, w, h, wplt, datab, wplb, hsize, L.L_HORIZ,
                          buffer, array);
            pixt.SetOrClearBorder(leftpix, rightpix, toppix, bottompix,
                                  L.PIX_CLR);
            DilateGrayLow(datab, w, h, wplb, datat, wplt, vsize, L.L_VERT,
                          buffer, array);
        }

        pixd = pixb.RemoveBorderGeneral(leftpix, rightpix, toppix, bottompix);
        return pixd;
    }

    // leptonica: pixCloseGray (graymorph.c)
    /// <summary>Gray morphological closing with a brick Sel (van Herk / Gil-Werman).</summary>
    /// <param name="hsize">of Sel; must be odd; origin implicitly in center</param>
    /// <param name="vsize">ditto</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// Notes:
    ///   (1) Sel is a brick with all elements being hits
    ///   (2) If hsize = vsize = 1, just returns a copy.
    /// </remarks>
    public Pix CloseGray(int hsize, int vsize)
    {
        const string func = "pixCloseGray";
        int w, h, wplb, wplt;
        int leftpix, rightpix, toppix, bottompix, maxsize;
        uint[] datab, datat;
        Pix pixb, pixt, pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize or vsize < 1");
        if ((hsize & 1) == 0) {
            Log.Warning(func, "horiz sel size must be odd; increasing by 1");
            hsize++;
        }
        if ((vsize & 1) == 0) {
            Log.Warning(func, "vert sel size must be odd; increasing by 1");
            vsize++;
        }

        if (hsize == 1 && vsize == 1)
            return Copy(null, this);

        if (Colormap == null &&
            LeptGpu.TryGetActive((long)Width * Height, (hsize + vsize) * 2, out IGpuAccelerator? gpuClose) &&
            gpuClose.TryOpenCloseGrayBrick(this, hsize, vsize, open: false, out Pix? gpuClosed)) {
            return gpuClosed;
        }

        if (vsize == 1) {  /* horizontal sel */
            leftpix = (hsize + 1) / 2;
            rightpix = (3 * hsize + 1) / 2;
            toppix = 0;
            bottompix = 0;
        } else if (hsize == 1) {  /* vertical sel */
            leftpix = 0;
            rightpix = 0;
            toppix = (vsize + 1) / 2;
            bottompix = (3 * vsize + 1) / 2;
        } else {
            leftpix = (hsize + 1) / 2;
            rightpix = (3 * hsize + 1) / 2;
            toppix = (vsize + 1) / 2;
            bottompix = (3 * vsize + 1) / 2;
        }

        pixb = AddBorderGeneral(leftpix, rightpix, toppix, bottompix, 0);
        pixt = CreateTemplate(pixb);

        w = pixt.Width;
        h = pixt.Height;
        datab = pixb.Data;
        datat = pixt.Data;
        wplb = pixb.Wpl;
        wplt = pixt.Wpl;

        byte[] buffer = new byte[Math.Max(w, h)];
        maxsize = Math.Max(hsize, vsize);
        byte[] array = new byte[2 * maxsize];  /* used to find either min or max in interval */

        if (vsize == 1) {
            DilateGrayLow(datat, w, h, wplt, datab, wplb, hsize, L.L_HORIZ,
                          buffer, array);
            pixt.SetOrClearBorder(leftpix, rightpix, toppix, bottompix,
                                  L.PIX_SET);
            ErodeGrayLow(datab, w, h, wplb, datat, wplt, hsize, L.L_HORIZ,
                         buffer, array);
        } else if (hsize == 1) {
            DilateGrayLow(datat, w, h, wplt, datab, wplb, vsize, L.L_VERT,
                          buffer, array);
            pixt.SetOrClearBorder(leftpix, rightpix, toppix, bottompix,
                                  L.PIX_SET);
            ErodeGrayLow(datab, w, h, wplb, datat, wplt, vsize, L.L_VERT,
                         buffer, array);
        } else {
            DilateGrayLow(datat, w, h, wplt, datab, wplb, hsize, L.L_HORIZ,
                          buffer, array);
            pixt.SetOrClearBorder(leftpix, rightpix, toppix, bottompix,
                                  L.PIX_CLR);
            DilateGrayLow(datab, w, h, wplb, datat, wplt, vsize, L.L_VERT,
                          buffer, array);
            pixb.SetOrClearBorder(leftpix, rightpix, toppix, bottompix,
                                  L.PIX_SET);
            ErodeGrayLow(datat, w, h, wplt, datab, wplb, hsize, L.L_HORIZ,
                         buffer, array);
            pixt.SetOrClearBorder(leftpix, rightpix, toppix, bottompix,
                                  L.PIX_SET);
            ErodeGrayLow(datab, w, h, wplb, datat, wplt, vsize, L.L_VERT,
                         buffer, array);
        }

        pixd = pixb.RemoveBorderGeneral(leftpix, rightpix, toppix, bottompix);
        return pixd;
    }

    /*-----------------------------------------------------------------*
     *           Special operations for 1x3, 3x1 and 3x3 Sels          *
     *-----------------------------------------------------------------*/
    // leptonica: pixErodeGray3 (graymorph.c)
    /// <summary>Gray erosion with a 1x3, 3x1 or 3x3 brick Sel.</summary>
    /// <param name="hsize">1 or 3</param>
    /// <param name="vsize">1 or 3</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// Notes:
    ///   (1) Special case for 1x3, 3x1 or 3x3 brick sel (all hits)
    ///   (2) If hsize = vsize = 1, just returns a copy.
    ///   (3) It would be nice not to add a border, but it is required
    ///       if we want the same results as from the general case.
    ///       We add 4 bytes on the left to speed up the copying, and
    ///       8 bytes at the right and bottom to allow unrolling of
    ///       the computation of 8 pixels.
    /// </remarks>
    public Pix ErodeGray3(int hsize, int vsize)
    {
        const string func = "pixErodeGray3";
        Pix pixt, pixb, pixbd, pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pix has colormap");
        if ((hsize != 1 && hsize != 3) ||
            (vsize != 1 && vsize != 3))
            throw Lept.Arg(func, "invalid size: must be 1 or 3");

        if (hsize == 1 && vsize == 1)
            return Copy(null, this);

        pixb = AddBorderGeneral(4, 8, 2, 8, 255);

        if (vsize == 1)
            pixbd = pixb.ErodeGray3h();
        else if (hsize == 1)
            pixbd = pixb.ErodeGray3v();
        else {  /* vize == hsize == 3 */
            pixt = pixb.ErodeGray3h();
            pixbd = pixt.ErodeGray3v();
        }

        pixd = pixbd.RemoveBorderGeneral(4, 8, 2, 8);
        return pixd;
    }

    // leptonica: pixErodeGray3h (graymorph.c)
    /// <summary>Horizontal 3x1 gray erosion.</summary>
    /// <returns>pixd</returns>
    /// <remarks>
    /// Notes:
    ///   (1) Special case for horizontal 3x1 brick Sel;
    ///       also used as the first step for the 3x3 brick Sel.
    /// </remarks>
    private Pix ErodeGray3h()
    {
        const string func = "pixErodeGray3h";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");

        Pix pixd = CreateTemplate(this);
        int w = Width;
        int h = Height;
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpl = Wpl;
        int nBlocks = w > 9 ? (w - 9 + 7) / 8 : 0;
        if (nBlocks <= 0)
            return pixd;
        int endCol = 1 + nBlocks * 8;

        byte[] rented = ArrayPool<byte>.Shared.Rent(w * 2 + 64);
        try {
            ref byte inRef = ref MemoryMarshal.GetArrayDataReference(rented);
            ref byte outRef = ref Unsafe.Add(ref inRef, (nuint)w);
            ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);

            for (int i = 0; i < h; i++) {
                UnpackMorphRow(ref Unsafe.Add(ref baseS, (nuint)(i * wpl)), w, ref inRef);
                int j = 1;
                if (Vector256.IsHardwareAccelerated) {
                    for (; j + 32 <= endCol; j += 32) {
                        Vector256<byte> vL = Vector256.LoadUnsafe(ref inRef, (nuint)(j - 1));
                        Vector256<byte> vM = Vector256.LoadUnsafe(ref inRef, (nuint)j);
                        Vector256<byte> vR = Vector256.LoadUnsafe(ref inRef, (nuint)(j + 1));
                        Vector256.Min(vL, Vector256.Min(vM, vR)).StoreUnsafe(ref outRef, (nuint)j);
                    }
                }
                for (; j < endCol; j++) {
                    byte bL = Unsafe.Add(ref inRef, (nuint)(j - 1));
                    byte bM = Unsafe.Add(ref inRef, (nuint)j);
                    byte bR = Unsafe.Add(ref inRef, (nuint)(j + 1));
                    Unsafe.Add(ref outRef, (nuint)j) = Math.Min(bL, Math.Min(bM, bR));
                }
                PackMorphRowSlice(ref outRef, ref Unsafe.Add(ref baseD, (nuint)(i * wpl)), 1, endCol);
            }
        } finally {
            ArrayPool<byte>.Shared.Return(rented);
        }
        return pixd;
    }

    // leptonica: pixErodeGray3v (graymorph.c)
    /// <summary>Vertical 1x3 gray erosion.</summary>
    /// <returns>pixd</returns>
    /// <remarks>
    /// Notes:
    ///   (1) Special case for vertical 1x3 brick Sel;
    ///       also used as the second step for the 3x3 brick Sel.
    ///   (2) Surprisingly, this is faster than setting up the
    ///       lineptrs array and accessing into it; e.g.,
    ///           val4 = GET_DATA_BYTE(lines8[i + 3], j);
    /// </remarks>
    private Pix ErodeGray3v()
    {
        const string func = "pixErodeGray3v";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");

        Pix pixd = CreateTemplate(this);
        int w = Width;
        int h = Height;
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpl = Wpl;
        int fullWords = w >> 2;
        ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
        ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);

        for (int i = 1; i < h - 8; i += 8) {
            for (int r = i; r < i + 8; r++) {
                int r0 = (r - 1) * wpl, r1 = r * wpl, r2 = (r + 1) * wpl;
                int jw = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; jw + 8 <= fullWords; jw += 8) {
                        Vector256<byte> v0 = LoadVec256(ref baseS, r0 + jw);
                        Vector256<byte> v1 = LoadVec256(ref baseS, r1 + jw);
                        Vector256<byte> v2 = LoadVec256(ref baseS, r2 + jw);
                        StoreVec256(ref baseD, r1 + jw, Vector256.Min(v0, Vector256.Min(v1, v2)));
                    }
                }
                if (Vector128.IsHardwareAccelerated) {
                    for (; jw + 4 <= fullWords; jw += 4) {
                        Vector128<byte> v0 = LoadVec128(ref baseS, r0 + jw);
                        Vector128<byte> v1 = LoadVec128(ref baseS, r1 + jw);
                        Vector128<byte> v2 = LoadVec128(ref baseS, r2 + jw);
                        StoreVec128(ref baseD, r1 + jw, Vector128.Min(v0, Vector128.Min(v1, v2)));
                    }
                    for (; jw < fullWords; jw++) {
                        Vector128<byte> v0 = Vector128.CreateScalar(Unsafe.Add(ref baseS, (nuint)(r0 + jw))).AsByte();
                        Vector128<byte> v1 = Vector128.CreateScalar(Unsafe.Add(ref baseS, (nuint)(r1 + jw))).AsByte();
                        Vector128<byte> v2 = Vector128.CreateScalar(Unsafe.Add(ref baseS, (nuint)(r2 + jw))).AsByte();
                        Unsafe.Add(ref baseD, (nuint)(r1 + jw)) = Vector128.Min(v0, Vector128.Min(v1, v2)).AsUInt32().ToScalar();
                    }
                }
                ref uint rs0 = ref Unsafe.Add(ref baseS, (nuint)r0);
                ref uint rs1 = ref Unsafe.Add(ref baseS, (nuint)r1);
                ref uint rs2 = ref Unsafe.Add(ref baseS, (nuint)r2);
                ref uint rd1 = ref Unsafe.Add(ref baseD, (nuint)r1);
                for (int j = jw << 2; j < w; j++) {
                    int b0 = ArrayAccess.GetDataByteUnchecked(ref rs0, j);
                    int b1 = ArrayAccess.GetDataByteUnchecked(ref rs1, j);
                    int b2 = ArrayAccess.GetDataByteUnchecked(ref rs2, j);
                    ArrayAccess.SetDataByteUnchecked(ref rd1, j, Math.Min(b0, Math.Min(b1, b2)));
                }
            }
        }
        return pixd;
    }

    // leptonica: pixDilateGray3 (graymorph.c)
    /// <summary>Gray dilation with a 1x3, 3x1 or 3x3 brick Sel.</summary>
    /// <param name="hsize">1 or 3</param>
    /// <param name="vsize">1 or 3</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// Notes:
    ///   (1) Special case for 1x3, 3x1 or 3x3 brick sel (all hits)
    ///   (2) If hsize = vsize = 1, just returns a copy.
    /// </remarks>
    public Pix DilateGray3(int hsize, int vsize)
    {
        const string func = "pixDilateGray3";
        Pix pixt, pixb, pixbd, pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pix has colormap");
        if ((hsize != 1 && hsize != 3) ||
            (vsize != 1 && vsize != 3))
            throw Lept.Arg(func, "invalid size: must be 1 or 3");

        if (hsize == 1 && vsize == 1)
            return Copy(null, this);

        pixb = AddBorderGeneral(4, 8, 2, 8, 0);

        if (vsize == 1)
            pixbd = pixb.DilateGray3h();
        else if (hsize == 1)
            pixbd = pixb.DilateGray3v();
        else {  /* vize == hsize == 3 */
            pixt = pixb.DilateGray3h();
            pixbd = pixt.DilateGray3v();
        }

        pixd = pixbd.RemoveBorderGeneral(4, 8, 2, 8);
        return pixd;
    }

    // leptonica: pixDilateGray3h (graymorph.c)
    /// <summary>Horizontal 3x1 gray dilation.</summary>
    /// <returns>pixd</returns>
    /// <remarks>
    /// Notes:
    ///   (1) Special case for horizontal 3x1 brick Sel;
    ///       also used as the first step for the 3x3 brick Sel.
    /// </remarks>
    private Pix DilateGray3h()
    {
        const string func = "pixDilateGray3h";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");

        Pix pixd = CreateTemplate(this);
        int w = Width;
        int h = Height;
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpl = Wpl;
        int nBlocks = w > 9 ? (w - 9 + 7) / 8 : 0;
        if (nBlocks <= 0)
            return pixd;
        int endCol = 1 + nBlocks * 8;

        byte[] rented = ArrayPool<byte>.Shared.Rent(w * 2 + 64);
        try {
            ref byte inRef = ref MemoryMarshal.GetArrayDataReference(rented);
            ref byte outRef = ref Unsafe.Add(ref inRef, (nuint)w);
            ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);

            for (int i = 0; i < h; i++) {
                UnpackMorphRow(ref Unsafe.Add(ref baseS, (nuint)(i * wpl)), w, ref inRef);
                int j = 1;
                if (Vector256.IsHardwareAccelerated) {
                    for (; j + 32 <= endCol; j += 32) {
                        Vector256<byte> vL = Vector256.LoadUnsafe(ref inRef, (nuint)(j - 1));
                        Vector256<byte> vM = Vector256.LoadUnsafe(ref inRef, (nuint)j);
                        Vector256<byte> vR = Vector256.LoadUnsafe(ref inRef, (nuint)(j + 1));
                        Vector256.Max(vL, Vector256.Max(vM, vR)).StoreUnsafe(ref outRef, (nuint)j);
                    }
                }
                for (; j < endCol; j++) {
                    byte bL = Unsafe.Add(ref inRef, (nuint)(j - 1));
                    byte bM = Unsafe.Add(ref inRef, (nuint)j);
                    byte bR = Unsafe.Add(ref inRef, (nuint)(j + 1));
                    Unsafe.Add(ref outRef, (nuint)j) = Math.Max(bL, Math.Max(bM, bR));
                }
                PackMorphRowSlice(ref outRef, ref Unsafe.Add(ref baseD, (nuint)(i * wpl)), 1, endCol);
            }
        } finally {
            ArrayPool<byte>.Shared.Return(rented);
        }
        return pixd;
    }

    // leptonica: pixDilateGray3v (graymorph.c)
    /// <summary>Vertical 1x3 gray dilation.</summary>
    /// <returns>pixd</returns>
    /// <remarks>
    /// Notes:
    ///   (1) Special case for vertical 1x3 brick Sel;
    ///       also used as the second step for the 3x3 brick Sel.
    /// </remarks>
    private Pix DilateGray3v()
    {
        const string func = "pixDilateGray3v";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");

        Pix pixd = CreateTemplate(this);
        int w = Width;
        int h = Height;
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpl = Wpl;
        int fullWords = w >> 2;
        ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
        ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);

        for (int i = 1; i < h - 8; i += 8) {
            for (int r = i; r < i + 8; r++) {
                int r0 = (r - 1) * wpl, r1 = r * wpl, r2 = (r + 1) * wpl;
                int jw = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; jw + 8 <= fullWords; jw += 8) {
                        Vector256<byte> v0 = LoadVec256(ref baseS, r0 + jw);
                        Vector256<byte> v1 = LoadVec256(ref baseS, r1 + jw);
                        Vector256<byte> v2 = LoadVec256(ref baseS, r2 + jw);
                        StoreVec256(ref baseD, r1 + jw, Vector256.Max(v0, Vector256.Max(v1, v2)));
                    }
                }
                if (Vector128.IsHardwareAccelerated) {
                    for (; jw + 4 <= fullWords; jw += 4) {
                        Vector128<byte> v0 = LoadVec128(ref baseS, r0 + jw);
                        Vector128<byte> v1 = LoadVec128(ref baseS, r1 + jw);
                        Vector128<byte> v2 = LoadVec128(ref baseS, r2 + jw);
                        StoreVec128(ref baseD, r1 + jw, Vector128.Max(v0, Vector128.Max(v1, v2)));
                    }
                    for (; jw < fullWords; jw++) {
                        Vector128<byte> v0 = Vector128.CreateScalar(Unsafe.Add(ref baseS, (nuint)(r0 + jw))).AsByte();
                        Vector128<byte> v1 = Vector128.CreateScalar(Unsafe.Add(ref baseS, (nuint)(r1 + jw))).AsByte();
                        Vector128<byte> v2 = Vector128.CreateScalar(Unsafe.Add(ref baseS, (nuint)(r2 + jw))).AsByte();
                        Unsafe.Add(ref baseD, (nuint)(r1 + jw)) = Vector128.Max(v0, Vector128.Max(v1, v2)).AsUInt32().ToScalar();
                    }
                }
                ref uint rs0 = ref Unsafe.Add(ref baseS, (nuint)r0);
                ref uint rs1 = ref Unsafe.Add(ref baseS, (nuint)r1);
                ref uint rs2 = ref Unsafe.Add(ref baseS, (nuint)r2);
                ref uint rd1 = ref Unsafe.Add(ref baseD, (nuint)r1);
                for (int j = jw << 2; j < w; j++) {
                    int b0 = ArrayAccess.GetDataByteUnchecked(ref rs0, j);
                    int b1 = ArrayAccess.GetDataByteUnchecked(ref rs1, j);
                    int b2 = ArrayAccess.GetDataByteUnchecked(ref rs2, j);
                    ArrayAccess.SetDataByteUnchecked(ref rd1, j, Math.Max(b0, Math.Max(b1, b2)));
                }
            }
        }
        return pixd;
    }

    // leptonica: pixOpenGray3 (graymorph.c)
    /// <summary>Gray opening with a 1x3, 3x1 or 3x3 brick Sel.</summary>
    /// <param name="hsize">1 or 3</param>
    /// <param name="vsize">1 or 3</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// Notes:
    ///   (1) Special case for 1x3, 3x1 or 3x3 brick sel (all hits)
    ///   (2) If hsize = vsize = 1, just returns a copy.
    ///   (3) It would be nice not to add a border, but it is required
    ///       to get the same results as for the general case.
    /// </remarks>
    public Pix OpenGray3(int hsize, int vsize)
    {
        const string func = "pixOpenGray3";
        Pix pixt, pixb, pixbd, pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pix has colormap");
        if ((hsize != 1 && hsize != 3) ||
            (vsize != 1 && vsize != 3))
            throw Lept.Arg(func, "invalid size: must be 1 or 3");

        if (hsize == 1 && vsize == 1)
            return Copy(null, this);

        pixb = AddBorderGeneral(4, 8, 2, 8, 255);  /* set to max */

        if (vsize == 1) {
            pixt = pixb.ErodeGray3h();
            pixt.SetBorderVal(4, 8, 2, 8, 0);  /* set to min */
            pixbd = pixt.DilateGray3h();
        } else if (hsize == 1) {
            pixt = pixb.ErodeGray3v();
            pixt.SetBorderVal(4, 8, 2, 8, 0);
            pixbd = pixt.DilateGray3v();
        } else {  /* vize == hsize == 3 */
            pixt = pixb.ErodeGray3h();
            pixbd = pixt.ErodeGray3v();
            pixbd.SetBorderVal(4, 8, 2, 8, 0);
            pixt = pixbd.DilateGray3h();
            pixbd = pixt.DilateGray3v();
        }

        pixd = pixbd.RemoveBorderGeneral(4, 8, 2, 8);
        return pixd;
    }

    // leptonica: pixCloseGray3 (graymorph.c)
    /// <summary>Gray closing with a 1x3, 3x1 or 3x3 brick Sel.</summary>
    /// <param name="hsize">1 or 3</param>
    /// <param name="vsize">1 or 3</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// Notes:
    ///   (1) Special case for 1x3, 3x1 or 3x3 brick sel (all hits)
    ///   (2) If hsize = vsize = 1, just returns a copy.
    /// </remarks>
    public Pix CloseGray3(int hsize, int vsize)
    {
        const string func = "pixCloseGray3";
        Pix pixt, pixb, pixbd, pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pix has colormap");
        if ((hsize != 1 && hsize != 3) ||
            (vsize != 1 && vsize != 3))
            throw Lept.Arg(func, "invalid size: must be 1 or 3");

        if (hsize == 1 && vsize == 1)
            return Copy(null, this);

        pixb = AddBorderGeneral(4, 8, 2, 8, 0);  /* set to min */

        if (vsize == 1) {
            pixt = pixb.DilateGray3h();
            pixt.SetBorderVal(4, 8, 2, 8, 255);  /* set to max */
            pixbd = pixt.ErodeGray3h();
        } else if (hsize == 1) {
            pixt = pixb.DilateGray3v();
            pixt.SetBorderVal(4, 8, 2, 8, 255);
            pixbd = pixt.ErodeGray3v();
        } else {  /* vize == hsize == 3 */
            pixt = pixb.DilateGray3h();
            pixbd = pixt.DilateGray3v();
            pixbd.SetBorderVal(4, 8, 2, 8, 255);
            pixt = pixbd.ErodeGray3h();
            pixbd = pixt.ErodeGray3v();
        }

        pixd = pixbd.RemoveBorderGeneral(4, 8, 2, 8);
        return pixd;
    }
}
