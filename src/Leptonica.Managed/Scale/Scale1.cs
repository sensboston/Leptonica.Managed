using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  scale1.c: Scaling: linear interpolated, sampled, area map, smooth, binary.
 *
 *  Differences from C:
 *    - pixScaleToResolution returns the scaling factor actually used through
 *      an out parameter (%pscalefact).
 *    - The low-level static functions work on Span<uint> / ReadOnlySpan<uint>
 *      and an explicit line offset replaces the C pointer arithmetic.
 *      scaleSmoothLow, scaleBySamplingLow and scaleBinaryLow return void: their
 *      only error return (a failed allocation) cannot happen.
 *    - pixScaleGray2xLIDither and pixScaleGray4xLIDither throw for a source
 *      image that is only one line high: the C code reads and writes
 *      outside the buffers in that case (see docs/UPSTREAM_NOTES.md).
 *    - pixScaleRGBToGrayFast is declared in scale1.c (it was stubbed under
 *      scale2.c before).
 *
 *  Not ported: nothing.
 *
 *  Stubs for callees in other files: pixConvertTo8Or32 (pixconv.c),
 *  pixUnsharpMasking (enhance.c), pixScaleAndTransferAlpha (scale2.c),
 *  thresholdToBinaryLineLow and ditherToBinaryLineLow (grayquant.c).
 */

/// <summary>Constants from pix.h used by scale1.c.</summary>
public static partial class L
{
    /* Default clip distances for error diffusion dithering to 1 bpp */
}

public sealed partial class Pix
{
    /*------------------------------------------------------------------*
     *                    Top level scaling dispatcher                  *
     *------------------------------------------------------------------*/
    // leptonica: pixScale (scale1.c)
    /// <summary>Top level scaling dispatcher.</summary>
    /// <param name="scalex">horizontal scale factor</param>
    /// <param name="scaley">vertical scale factor</param>
    /// <returns>the scaled pix</returns>
    /// <remarks>
    /// This function scales 32 bpp RGB; 2, 4 or 8 bpp palette color;
    /// 2, 4, 8 or 16 bpp gray; and binary images.
    ///
    /// When the input has palette color, the colormap is removed and
    /// the result is either 8 bpp gray or 32 bpp RGB, depending on whether
    /// the colormap has color entries.  Images with 2, 4 or 16 bpp are
    /// converted to 8 bpp.
    ///
    /// Because pixScale is meant to be a very simple interface to a
    /// number of scaling functions, including the use of unsharp masking,
    /// the type of scaling and the sharpening parameters are chosen
    /// by default.  Grayscale and color images are scaled using one
    /// of five methods, depending on the scale factors:
    ///  1. antialiased subsampling (lowpass filtering followed by
    ///     subsampling, implemented by convolution, for tiny scale factors:
    ///       min(scalex, scaley) &lt; 0.02.
    ///  2. antialiased subsampling (implemented by area mapping, for
    ///     small scale factors:
    ///       max(scalex, scaley) &lt; 0.2 and min(scalex, scaley) &gt;= 0.02.
    ///  3. antialiased subsampling with sharpening, for scale factors
    ///     between 0.2 and 0.7
    ///  4. linear interpolation with sharpening, for scale factors between
    ///     0.7 and 1.4
    ///  5. linear interpolation without sharpening, for scale factors &gt;= 1.4.
    ///
    /// One could use subsampling for scale factors very close to 1.0,
    /// because it preserves sharp edges.  Linear interpolation blurs
    /// edges because the dest pixels will typically straddle two src edge
    /// pixels.  Subsmpling removes entire columns and rows, so the edge is
    /// not blurred.  However, there are two reasons for not doing this.
    /// First, it moves edges, so that a straight line at a large angle to
    /// both horizontal and vertical will have noticeable kinks where
    /// horizontal and vertical rasters are removed.  Second, although it
    /// is very fast, you get good results on sharp edges by applying
    /// a sharpening filter.
    ///
    /// For images with sharp edges, sharpening substantially improves the
    /// image quality for scale factors between about 0.2 and about 2.0.
    /// pixScale uses a small amount of sharpening by default because
    /// it strengthens edge pixels that are weak due to anti-aliasing.
    /// The default sharpening factors are:
    ///     * for scaling factors &lt; 0.7:   sharpfract = 0.2    sharpwidth = 1
    ///     * for scaling factors &gt;= 0.7:  sharpfract = 0.4    sharpwidth = 2
    /// The cases where the sharpening halfwidth is 1 or 2 have special
    /// implementations and are about twice as fast as the general case.
    ///
    /// However, sharpening is computationally expensive, and one needs
    /// to consider the speed-quality tradeoff:
    ///     * For upscaling of RGB images, linear interpolation plus default
    ///       sharpening is about 5 times slower than upscaling alone.
    ///     * For downscaling, area mapping plus default sharpening is
    ///       about 10 times slower than downscaling alone.
    /// When the scale factor is larger than 1.4, the cost of sharpening,
    /// which is proportional to image area, is very large compared to the
    /// incremental quality improvement, so we cut off the default use of
    /// sharpening at 1.4.  Thus, for scale factors greater than 1.4,
    /// pixScale only does linear interpolation.
    ///
    /// In many situations you will get a satisfactory result by scaling
    /// without sharpening: call pixScaleGeneral with %sharpfract = 0.0.
    /// Alternatively, if you wish to sharpen but not use the default
    /// value, first call pixScaleGeneral with %sharpfract = 0.0, and
    /// then sharpen explicitly using pixUnsharpMasking.
    ///
    /// Binary images are scaled to binary by sampling the closest pixel,
    /// without any low-pass filtering averaging of neighboring pixels.
    /// This will introduce aliasing for reductions.  Aliasing can be
    /// prevented by using pixScaleToGray instead.
    /// </remarks>
    public Pix Scale(float scalex, float scaley)
    {
        int sharpwidth;
        float maxscale, sharpfract;

        /* Reduce the default sharpening factors by 2 if maxscale < 0.7 */
        maxscale = Math.Max(scalex, scaley);
        sharpfract = (maxscale < 0.7) ? 0.2f : 0.4f;
        sharpwidth = (maxscale < 0.7) ? 1 : 2;

        return ScaleGeneral(scalex, scaley, sharpfract, sharpwidth);
    }

    // leptonica: pixScaleToSizeRel (scale1.c)
    /// <summary>Scales by a change in width and height, in pixels.</summary>
    /// <param name="delw">change in width, in pixels; 0 means no change</param>
    /// <param name="delh">change in height, in pixels; 0 means no change</param>
    public Pix ScaleToSizeRel(int delw, int delh)
    {
        const string func = "pixScaleToSizeRel";

        if (delw == 0 && delh == 0)
            return Copy(null, this);

        GetDimensions(out int w, out int h, out _);
        int wd = w + delw;
        int hd = h + delh;
        if (wd <= 0 || hd <= 0)
            throw Lept.Arg(func, "pix dimension reduced to 0");

        return ScaleToSize(wd, hd);
    }

    // leptonica: pixScaleToSize (scale1.c)
    /// <summary>Scales to a given width and/or height.</summary>
    /// <param name="wd">target width; use 0 if using height as target</param>
    /// <param name="hd">target height; use 0 if using width as target</param>
    /// <remarks>
    /// (1) The output scaled image has the dimension(s) you specify:
    ///     * To specify the width with isotropic scaling, set %hd = 0.
    ///     * To specify the height with isotropic scaling, set %wd = 0.
    ///     * If both %wd and %hd are specified, the image is scaled
    ///        (in general, anisotropically) to that size.
    ///     * It is an error to set both %wd and %hd to 0.
    /// </remarks>
    public Pix ScaleToSize(int wd, int hd)
    {
        const string func = "pixScaleToSize";
        float scalex, scaley;

        if (wd <= 0 && hd <= 0)
            throw Lept.Arg(func, "neither wd nor hd > 0");

        GetDimensions(out int w, out int h, out _);
        if (wd <= 0) {
            scaley = (float)hd / (float)h;
            scalex = scaley;
        } else if (hd <= 0) {
            scalex = (float)wd / (float)w;
            scaley = scalex;
        } else {
            scalex = (float)wd / (float)w;
            scaley = (float)hd / (float)h;
        }

        return Scale(scalex, scaley);
    }

    // leptonica: pixScaleToResolution (scale1.c)
    /// <summary>Scales so that the image has the target resolution.</summary>
    /// <param name="target">desired resolution</param>
    /// <param name="assumed">assumed resolution if not defined; typ. 300.</param>
    /// <param name="scalefact">actual scaling factor used</param>
    public Pix ScaleToResolution(float target, float assumed, out float scalefact)
    {
        const string func = "pixScaleToResolution";

        scalefact = 1.0f;
        if (target <= 0)
            throw Lept.Arg(func, "target resolution <= 0");

        int xres = XRes;
        if (xres <= 0) {
            if (assumed == 0)
                return Copy(null, this);
            xres = (int)assumed;
        }
        float factor = target / (float)xres;
        scalefact = factor;

        return Scale(factor, factor);
    }

    // leptonica: pixScaleGeneral (scale1.c)
    /// <summary>Scales with a choice of method and optional sharpening.</summary>
    /// <param name="scalex">must be &gt; 0.0</param>
    /// <param name="scaley">must be &gt; 0.0</param>
    /// <param name="sharpfract">use 0.0 to skip sharpening</param>
    /// <param name="sharpwidth">halfwidth of low-pass filter; typ. 1 or 2</param>
    /// <remarks>
    /// This pix is 1, 2, 4, 8, 16 and 32 bpp.
    /// (1) See pixScale() for usage.
    /// (2) This interface may change in the future, as other special
    ///     cases are added.
    /// (3) For tiny scaling factors
    ///       minscale &lt; 0.02:        use a simple lowpass filter
    /// (4) The actual sharpening factors used depend on the maximum
    ///     of the two scale factors (maxscale):
    ///       maxscale &lt;= 0.2:        no sharpening
    ///       0.2 &lt; maxscale &lt; 1.4:   uses the input parameters
    ///       maxscale &gt;= 1.4:        no sharpening
    /// (5) To avoid sharpening for grayscale and color images with
    ///     scaling factors between 0.2 and 1.4, call this function
    ///     with %sharpfract == 0.0.
    /// (6) To use arbitrary sharpening in conjunction with scaling,
    ///     call this function with %sharpfract = 0.0, and follow this
    ///     with a call to pixUnsharpMasking() with your chosen parameters.
    /// </remarks>
    public Pix ScaleGeneral(float scalex, float scaley, float sharpfract, int sharpwidth)
    {
        const string func = "pixScaleGeneral";
        int d;
        float maxscale, minscale;
        Pix pix1, pix2, pixd;

        d = Depth;
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg(func, "pixs not {1,2,4,8,16,32} bpp");
        if (scalex <= 0.0 || scaley <= 0.0)
            throw Lept.Arg(func, "scale factor <= 0");
        if (scalex == 1.0 && scaley == 1.0)
            return Copy(null, this);

        if (d == 1)
            return ScaleBinary(scalex, scaley);

        /* Remove colormap; clone if possible; result is either 8 or 32 bpp */
        pix1 = ConvertTo8Or32(L.L_CLONE, 0);

        /* Scale (up or down) */
        d = pix1.Depth;
        maxscale = Math.Max(scalex, scaley);
        minscale = Math.Min(scalex, scaley);
        if (maxscale < 0.7) {  /* use low-pass filter for anti-aliasing */
            if (minscale < 0.02) {  /* whole-pixel low-pass filter */
                pix2 = pix1.ScaleSmooth(scalex, scaley);
            } else {  /* fractional pixel low-pass filter */
                pix2 = pix1.ScaleAreaMap(scalex, scaley);
            }
            if (maxscale > 0.2 && sharpfract > 0.0 && sharpwidth > 0) {
                pixd = pix2.UnsharpMasking(sharpwidth, sharpfract);
            } else {
                pixd = pix2;
            }
        } else {  /* use linear interpolation */
            if (d == 8) {
                pix2 = pix1.ScaleGrayLI(scalex, scaley);
            } else {  /* d == 32 */
                pix2 = pix1.ScaleColorLI(scalex, scaley);
            }
            if (maxscale < 1.4 && sharpfract > 0.0 && sharpwidth > 0) {
                pixd = pix2.UnsharpMasking(sharpwidth, sharpfract);
            } else {
                pixd = pix2;
            }
        }

        pixd.CopyText(this);
        pixd.CopyInputFormat(this);
        return pixd;
    }

    /*------------------------------------------------------------------*
     *                  Scaling by linear interpolation                 *
     *------------------------------------------------------------------*/
    // leptonica: pixScaleLI (scale1.c)
    /// <summary>Scales by linear interpolation (usually for upscaling).</summary>
    /// <param name="scalex">must be &gt;= 0.7</param>
    /// <param name="scaley">must be &gt;= 0.7</param>
    /// <remarks>
    /// This pix is 2, 4, 8 or 32 bpp; with or without colormap.
    /// (1) This function should only be used when the scale factors are
    ///     greater than or equal to 0.7, and typically greater than 1.
    ///     If both scale factors are smaller than 0.7, we issue a warning
    ///     and call pixScaleGeneral(), which will invoke area mapping
    ///     without sharpening.
    /// (2) This works on 2, 4, 8, 16 and 32 bpp images, as well as on
    ///     2, 4 and 8 bpp images that have a colormap.  If there is a
    ///     colormap, it is removed to either gray or RGB, depending
    ///     on the colormap.
    /// (3) This does a linear interpolation on the src image.
    /// (4) It dispatches to much faster implementations for
    ///     the special cases of 2x and 4x expansion.
    /// </remarks>
    public Pix ScaleLI(float scalex, float scaley)
    {
        const string func = "pixScaleLI";
        int d;
        float maxscale;
        Pix pixt, pixd;

        if (Depth == 1)
            throw Lept.Arg(func, "pixs not defined or 1 bpp");
        maxscale = Math.Max(scalex, scaley);
        if (maxscale < 0.7) {
            Log.Warning(func, "scaling factors < 0.7; do regular scaling\n");
            return ScaleGeneral(scalex, scaley, 0.0f, 0);
        }
        d = Depth;
        if (d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg(func, "pixs not {2,4,8,16,32} bpp");

        /* Remove colormap; clone if possible; result is either 8 or 32 bpp */
        pixt = ConvertTo8Or32(L.L_CLONE, 0);

        d = pixt.Depth;
        if (d == 8)
            pixd = pixt.ScaleGrayLI(scalex, scaley);
        else  /* d == 32 */
            pixd = pixt.ScaleColorLI(scalex, scaley);

        pixd.CopyInputFormat(this);
        return pixd;
    }

    // leptonica: pixScaleColorLI (scale1.c)
    /// <summary>Scales a 32 bpp rgb image by linear interpolation.</summary>
    /// <param name="scalex">must be &gt;= 0.7</param>
    /// <param name="scaley">must be &gt;= 0.7</param>
    /// <remarks>
    /// This pix is 32 bpp, representing rgb.
    /// (1) If both scale factors are smaller than 0.7, we issue a warning
    ///     and call pixScaleGeneral(), which will invoke area mapping
    ///     without sharpening.  This is particularly important for
    ///     document images with sharp edges.
    /// (2) For the general case, it's about 4x faster to manipulate
    ///     the color pixels directly, rather than to make images
    ///     out of each of the 3 components, scale each component
    ///     using the pixScaleGrayLI(), and combine the results back
    ///     into an rgb image.
    /// </remarks>
    public Pix ScaleColorLI(float scalex, float scaley)
    {
        const string func = "pixScaleColorLI";
        float maxscale;
        Pix pixd;

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        maxscale = Math.Max(scalex, scaley);
        if (maxscale < 0.7) {
            Log.Warning(func, "scaling factors < 0.7; do regular scaling\n");
            return ScaleGeneral(scalex, scaley, 0.0f, 0);
        }

        /* Do fast special cases if possible */
        if (scalex == 1.0 && scaley == 1.0)
            return Copy(null, this);
        if (scalex == 2.0 && scaley == 2.0)
            return ScaleColor2xLI();
        if (scalex == 4.0 && scaley == 4.0)
            return ScaleColor4xLI();

        /* General case */
        GetDimensions(out int ws, out int hs, out _);
        if (Colormap == null && Spp != 4 &&
            LeptGpu.TryGetActive((long)(scalex * ws) * (long)(scaley * hs), 16, out IGpuAccelerator? gpuColorLI) &&
            gpuColorLI.TryScaleLI(this, scalex, scaley, out Pix? gpuPixColorLI)) {
            gpuPixColorLI.CopyInputFormat(this);
            return gpuPixColorLI;
        }
        int wpls = Wpl;
        int wd = (int)(scalex * (float)ws + 0.5);
        int hd = (int)(scaley * (float)hs + 0.5);
        pixd = CreateNoInit(wd, hd, 32);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(scalex, scaley);
        int wpld = pixd.Wpl;
        ScaleColorLILow(pixd.Data, wd, hd, wpld, Data, ws, hs, wpls);
        if (Spp == 4)
            pixd.ScaleAndTransferAlpha(this, scalex, scaley);

        pixd.CopyInputFormat(this);
        return pixd;
    }

    // leptonica: pixScaleColor2xLI (scale1.c)
    /// <summary>Scales a 32 bpp rgb image 2x by linear interpolation.</summary>
    /// <remarks>
    /// This pix is 32 bpp, representing rgb.
    /// (1) This is a special case of linear interpolated scaling,
    ///     for 2x upscaling.  It is about 8x faster than using
    ///     the generic pixScaleColorLI(), and about 4x faster than
    ///     using the special 2x scale function pixScaleGray2xLI()
    ///     on each of the three components separately.
    /// </remarks>
    public Pix ScaleColor2xLI()
    {
        const string func = "pixScaleColor2xLI";
        Pix pixd;

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");

        GetDimensions(out int ws, out int hs, out _);
        int wpls = Wpl;
        pixd = CreateNoInit(2 * ws, 2 * hs, 32);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(2.0f, 2.0f);
        int wpld = pixd.Wpl;
        ScaleColor2xLILow(pixd.Data, wpld, Data, ws, hs, wpls);
        if (Spp == 4)
            pixd.ScaleAndTransferAlpha(this, 2.0f, 2.0f);

        pixd.CopyInputFormat(this);
        return pixd;
    }

    // leptonica: pixScaleColor4xLI (scale1.c)
    /// <summary>Scales a 32 bpp rgb image 4x by linear interpolation.</summary>
    /// <remarks>
    /// This pix is 32 bpp, representing rgb.
    /// (1) This is a special case of color linear interpolated scaling,
    ///     for 4x upscaling.  It is about 3x faster than using
    ///     the generic pixScaleColorLI().
    /// (2) This scales each component separately, using pixScaleGray4xLI().
    ///     It would be about 4x faster to inline the color code properly,
    ///     in analogy to scaleColor4xLILow(), and I leave this as
    ///     an exercise for someone who really needs it.
    /// </remarks>
    public Pix ScaleColor4xLI()
    {
        const string func = "pixScaleColor4xLI";
        Pix pixr, pixg, pixb;
        Pix pixrs, pixgs, pixbs;
        Pix pixd;

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");

        pixr = GetRGBComponent(L.COLOR_RED);
        pixrs = pixr.ScaleGray4xLI();
        pixg = GetRGBComponent(L.COLOR_GREEN);
        pixgs = pixg.ScaleGray4xLI();
        pixb = GetRGBComponent(L.COLOR_BLUE);
        pixbs = pixb.ScaleGray4xLI();

        pixd = CreateRGBImage(pixrs, pixgs, pixbs);
        if (Spp == 4)
            pixd.ScaleAndTransferAlpha(this, 4.0f, 4.0f);
        pixd.CopyInputFormat(this);

        return pixd;
    }

    // leptonica: pixScaleGrayLI (scale1.c)
    /// <summary>Scales an 8 bpp gray image by linear interpolation.</summary>
    /// <param name="scalex">must be &gt;= 0.7</param>
    /// <param name="scaley">must be &gt;= 0.7</param>
    /// <remarks>
    /// This pix is 8 bpp grayscale, no cmap.
    /// (1) This function is appropriate for upscaling magnification, where the
    ///     scale factor is &gt; 1, as well as for a small amount of downscaling
    ///     reduction, with scale factor &gt;= 0.7.  If the scale factor is &lt; 0.7,
    ///     the best result is obtained by area mapping.
    /// (2) Here are some details:
    ///     - For each pixel in the dest, this does a linear
    ///       interpolation of 4 neighboring pixels in the src.
    ///       Specifically, consider the UL corner of src and
    ///       dest pixels.  The UL corner of the dest falls within
    ///       a src pixel, whose four corners are the UL corners
    ///       of 4 adjacent src pixels.  The value of the dest
    ///       is taken by linear interpolation using the values of
    ///       the four src pixels and the distance of the UL corner
    ///       of the dest from each corner.
    ///     - If the image is expanded so that the dest pixel is
    ///       smaller than the src pixel, such interpolation
    ///       is a reasonable approach.  This interpolation is
    ///       also good for a small image reduction factor that
    ///       is not more than a 2x reduction.
    ///     - The linear interpolation algorithm for scaling is
    ///       identical in form to the area-mapping algorithm
    ///       for grayscale rotation.  The latter corresponds to a
    ///       translation of each pixel without scaling.
    ///     - This function is NOT optimal if the scaling involves
    ///       a large reduction.  If the image is significantly
    ///       reduced, so that the dest pixel is much larger than
    ///       the src pixels, this interpolation, which is over src
    ///       pixels only near the UL corner of the dest pixel,
    ///       is not going to give a good area-mapping average.
    ///       Because area mapping for image scaling is considerably
    ///       more computationally intensive than linear interpolation,
    ///       we choose not to use it.  For large image reduction,
    ///       linear interpolation over adjacent src pixels
    ///       degenerates asymptotically to subsampling.  But
    ///       subsampling without a low-pass pre-filter causes
    ///       aliasing by the nyquist theorem.  To avoid aliasing,
    ///       a low-pass filter e.g., an averaging filter of
    ///       size roughly equal to the dest pixel i.e., the reduction
    ///       factor should be applied to the src before subsampling.
    ///     - As an alternative to low-pass filtering and subsampling
    ///       for large reduction factors, linear interpolation can
    ///       also be done between the widely separated src pixels in
    ///       which the corners of the dest pixel lie.  This also is
    ///       not optimal, as it samples src pixels only near the
    ///       corners of the dest pixel, and it is not implemented.
    /// </remarks>
    public Pix ScaleGrayLI(float scalex, float scaley)
    {
        const string func = "pixScaleGrayLI";
        float maxscale;
        Pix pixd;

        if (Depth != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs undefined, cmapped or not 8 bpp");
        maxscale = Math.Max(scalex, scaley);
        if (maxscale < 0.7) {
            Log.Warning(func, "scaling factors < 0.7; do regular scaling\n");
            return ScaleGeneral(scalex, scaley, 0.0f, 0);
        }

        /* Do fast special cases if possible */
        if (scalex == 1.0 && scaley == 1.0)
            return Copy(null, this);
        if (scalex == 2.0 && scaley == 2.0)
            return ScaleGray2xLI();
        if (scalex == 4.0 && scaley == 4.0)
            return ScaleGray4xLI();

        /* General case */
        GetDimensions(out int ws, out int hs, out _);
        if (Colormap == null &&
            LeptGpu.TryGetActive((long)(scalex * ws) * (long)(scaley * hs), 10, out IGpuAccelerator? gpuGrayLI) &&
            gpuGrayLI.TryScaleLI(this, scalex, scaley, out Pix? gpuPixGrayLI)) {
            gpuPixGrayLI.CopyText(this);
            gpuPixGrayLI.CopyInputFormat(this);
            return gpuPixGrayLI;
        }
        int wpls = Wpl;
        int wd = (int)(scalex * (float)ws + 0.5);
        int hd = (int)(scaley * (float)hs + 0.5);
        pixd = CreateNoInit(wd, hd, 8);
        pixd.CopyText(this);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        pixd.ScaleResolution(scalex, scaley);
        int wpld = pixd.Wpl;
        ScaleGrayLILow(pixd.Data, wd, hd, wpld, Data, ws, hs, wpls);
        return pixd;
    }

    // leptonica: pixScaleGray2xLI (scale1.c)
    /// <summary>Scales an 8 bpp gray image 2x by linear interpolation.</summary>
    /// <remarks>
    /// This pix is 8 bpp grayscale, not cmapped.
    /// (1) This is a special case of gray linear interpolated scaling,
    ///     for 2x upscaling.  It is about 6x faster than using
    ///     the generic pixScaleGrayLI().
    /// </remarks>
    public Pix ScaleGray2xLI()
    {
        const string func = "pixScaleGray2xLI";
        Pix pixd;

        if (Depth != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs undefined, cmapped or not 8 bpp");

        GetDimensions(out int ws, out int hs, out _);
        int wpls = Wpl;
        pixd = Create(2 * ws, 2 * hs, 8);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        pixd.ScaleResolution(2.0f, 2.0f);
        int wpld = pixd.Wpl;
        ScaleGray2xLILow(pixd.Data, wpld, Data, ws, hs, wpls);
        return pixd;
    }

    // leptonica: pixScaleGray4xLI (scale1.c)
    /// <summary>Scales an 8 bpp gray image 4x by linear interpolation.</summary>
    /// <remarks>
    /// This pix is 8 bpp grayscale, not cmapped.
    /// (1) This is a special case of gray linear interpolated scaling,
    ///     for 4x upscaling.  It is about 12x faster than using
    ///     the generic pixScaleGrayLI().
    /// </remarks>
    public Pix ScaleGray4xLI()
    {
        const string func = "pixScaleGray4xLI";
        Pix pixd;

        if (Depth != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs undefined, cmapped or not 8 bpp");

        GetDimensions(out int ws, out int hs, out _);
        int wpls = Wpl;
        pixd = Create(4 * ws, 4 * hs, 8);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        pixd.ScaleResolution(4.0f, 4.0f);
        int wpld = pixd.Wpl;
        ScaleGray4xLILow(pixd.Data, wpld, Data, ws, hs, wpls);
        return pixd;
    }

    /*------------------------------------------------------------------*
     *                Scale 2x followed by binarization                 *
     *------------------------------------------------------------------*/
    // leptonica: pixScaleGray2xLIThresh (scale1.c)
    /// <summary>Scales 2x by linear interpolation, then thresholds to binary.</summary>
    /// <param name="thresh">between 0 and 256</param>
    /// <returns>1 bpp pix</returns>
    /// <remarks>
    /// This pix is 8 bpp, not cmapped.
    /// (1) This does 2x upscale on pixs, using linear interpolation,
    ///     followed by thresholding to binary.
    /// (2) Buffers are used to avoid making a large grayscale image.
    /// </remarks>
    public Pix ScaleGray2xLIThresh(int thresh)
    {
        const string func = "pixScaleGray2xLIThresh";
        int i;
        Pix pixd;

        if (Depth != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs undefined, not 8 bpp, or cmapped");
        if (thresh < 0 || thresh > 256)
            throw Lept.Arg(func, "thresh must be in [0, ... 256]");

        GetDimensions(out int ws, out int hs, out _);
        int wd = 2 * ws;
        int hd = 2 * hs;
        int hsm = hs - 1;
        uint[] datas = Data;
        int wpls = Wpl;

        /* Make line buffer for 2 lines of virtual intermediate image */
        int wplb = (wd + 3) / 4;
        uint[] lineb = new uint[2 * wplb];

        /* Make dest binary image */
        pixd = Create(wd, hd, 1);
        pixd.CopyInputFormat(this);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(2.0f, 2.0f);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;

        /* Do all but last src line */
        for (i = 0; i < hsm; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls);
            int lined = 2 * i * wpld;  /* do 2 dest lines at a time */
            ScaleGray2xLILineLow(lineb, wplb, lines, ws, wpls, 0);
            ThresholdToBinaryLineLow(datad.AsSpan(lined), wd, lineb, 8, thresh);
            ThresholdToBinaryLineLow(datad.AsSpan(lined + wpld), wd, lineb.AsSpan(wplb), 8, thresh);
        }

        /* Do last src line */
        {
            ReadOnlySpan<uint> lines = datas.AsSpan(hsm * wpls);
            int lined = 2 * hsm * wpld;
            ScaleGray2xLILineLow(lineb, wplb, lines, ws, wpls, 1);
            ThresholdToBinaryLineLow(datad.AsSpan(lined), wd, lineb, 8, thresh);
            ThresholdToBinaryLineLow(datad.AsSpan(lined + wpld), wd, lineb.AsSpan(wplb), 8, thresh);
        }

        return pixd;
    }

    // leptonica: pixScaleGray2xLIDither (scale1.c)
    /// <summary>Scales 2x by linear interpolation, then dithers to binary.</summary>
    /// <returns>1 bpp pix</returns>
    /// <remarks>
    /// This pix is 8 bpp, not cmapped.
    /// (1) This does 2x upscale on pixs, using linear interpolation,
    ///     followed by Floyd-Steinberg dithering to binary.
    /// (2) Buffers are used to avoid making a large grayscale image.
    ///     ~ Two line buffers are used for the src, required for the 2x
    ///       LI upscale.
    ///     ~ Three line buffers are used for the intermediate image.
    ///       Two are filled with each 2xLI row operation; the third is
    ///       needed because the upscale and dithering ops are out of sync.
    /// </remarks>
    public Pix ScaleGray2xLIDither()
    {
        const string func = "pixScaleGray2xLIDither";
        int i;
        Pix pixd;

        if (Depth != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs undefined, not 8 bpp, or cmapped");

        GetDimensions(out int ws, out int hs, out _);
        int wd = 2 * ws;
        int hd = 2 * hs;
        int hsm = hs - 1;
        uint[] datas = Data;
        int wpls = Wpl;

        /* NOTE(upstream): for hs == 1 the C code reads a second src line that
         * does not exist and writes 4 dest lines into an image with 2 lines. */
        if (hs < 2)
            throw Lept.Arg(func, "pixs height must be at least 2");

        /* Make line buffers for 2 lines of src image */
        uint[] bufs = new uint[2 * wpls];

        /* Make line buffer for 2 lines of virtual intermediate image */
        int wplb = (wd + 3) / 4;
        uint[] lineb = new uint[2 * wplb];

        /* Make line buffer for 1 line of virtual intermediate image */
        uint[] linebp = new uint[wplb];

        /* Make dest binary image */
        pixd = Create(wd, hd, 1);
        pixd.CopyInputFormat(this);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(2.0f, 2.0f);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;

        /* Start with the first src and the first dest line */
        datas.AsSpan(0, wpls).CopyTo(bufs);   /* first src line */
        datas.AsSpan(wpls, wpls).CopyTo(bufs.AsSpan(wpls));  /* 2nd src line */
        ScaleGray2xLILineLow(lineb, wplb, bufs, ws, wpls, 0);  /* 2 i lines */
        int lined = 0;
        DitherToBinaryLineLow(datad.AsSpan(lined), wd, lineb, lineb.AsSpan(wplb),
                              L.DEFAULT_CLIP_LOWER_1, L.DEFAULT_CLIP_UPPER_1, 0);
                                                    /* 1st d line */

        /* Do all but last src line */
        for (i = 1; i < hsm; i++) {
            datas.AsSpan(i * wpls, wpls).CopyTo(bufs);  /* i-th src line */
            datas.AsSpan((i + 1) * wpls, wpls).CopyTo(bufs.AsSpan(wpls));
            lineb.AsSpan(wplb, wplb).CopyTo(linebp);
            ScaleGray2xLILineLow(lineb, wplb, bufs, ws, wpls, 0);  /* 2 i lines */
            lined = 2 * i * wpld;
            DitherToBinaryLineLow(datad.AsSpan(lined - wpld), wd, linebp, lineb,
                                  L.DEFAULT_CLIP_LOWER_1, L.DEFAULT_CLIP_UPPER_1, 0);
                                                       /* odd dest line */
            DitherToBinaryLineLow(datad.AsSpan(lined), wd, lineb, lineb.AsSpan(wplb),
                                  L.DEFAULT_CLIP_LOWER_1, L.DEFAULT_CLIP_UPPER_1, 0);
                                                       /* even dest line */
        }

        /* Do the last src line and the last 3 dest lines */
        datas.AsSpan(hsm * wpls, wpls).CopyTo(bufs);  /* hsm-th src line */
        lineb.AsSpan(wplb, wplb).CopyTo(linebp);   /* 1 i line */
        ScaleGray2xLILineLow(lineb, wplb, bufs, ws, wpls, 1);  /* 2 i lines */
        DitherToBinaryLineLow(datad.AsSpan(lined + wpld), wd, linebp, lineb,
                              L.DEFAULT_CLIP_LOWER_1, L.DEFAULT_CLIP_UPPER_1, 0);
                                                       /* odd dest line */
        DitherToBinaryLineLow(datad.AsSpan(lined + 2 * wpld), wd, lineb, lineb.AsSpan(wplb),
                              L.DEFAULT_CLIP_LOWER_1, L.DEFAULT_CLIP_UPPER_1, 0);
                                                       /* even dest line */
        DitherToBinaryLineLow(datad.AsSpan(lined + 3 * wpld), wd, lineb.AsSpan(wplb), default,
                              L.DEFAULT_CLIP_LOWER_1, L.DEFAULT_CLIP_UPPER_1, 1);
                                                       /* last dest line */

        return pixd;
    }

    /*------------------------------------------------------------------*
     *                Scale 4x followed by binarization                 *
     *------------------------------------------------------------------*/
    // leptonica: pixScaleGray4xLIThresh (scale1.c)
    /// <summary>Scales 4x by linear interpolation, then thresholds to binary.</summary>
    /// <param name="thresh">between 0 and 256</param>
    /// <returns>1 bpp pix</returns>
    /// <remarks>
    /// This pix is 8 bpp.
    /// (1) This does 4x upscale on pixs, using linear interpolation,
    ///     followed by thresholding to binary.
    /// (2) Buffers are used to avoid making a large grayscale image.
    /// (3) If a full 4x expanded grayscale image can be kept in memory,
    ///     this function is only about 10% faster than separately doing
    ///     a linear interpolation to a large grayscale image, followed
    ///     by thresholding to binary.
    /// </remarks>
    public Pix ScaleGray4xLIThresh(int thresh)
    {
        const string func = "pixScaleGray4xLIThresh";
        int i, j;
        Pix pixd;

        if (Depth != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs undefined, not 8 bpp, or cmapped");
        if (thresh < 0 || thresh > 256)
            throw Lept.Arg(func, "thresh must be in [0, ... 256]");

        GetDimensions(out int ws, out int hs, out _);
        int wd = 4 * ws;
        int hd = 4 * hs;
        int hsm = hs - 1;
        uint[] datas = Data;
        int wpls = Wpl;

        /* Make line buffer for 4 lines of virtual intermediate image */
        int wplb = (wd + 3) / 4;
        uint[] lineb = new uint[4 * wplb];

        /* Make dest binary image */
        pixd = Create(wd, hd, 1);
        pixd.CopyInputFormat(this);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(4.0f, 4.0f);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;

        /* Do all but last src line */
        for (i = 0; i < hsm; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls);
            int lined = 4 * i * wpld;  /* do 4 dest lines at a time */
            ScaleGray4xLILineLow(lineb, wplb, lines, ws, wpls, 0);
            for (j = 0; j < 4; j++) {
                ThresholdToBinaryLineLow(datad.AsSpan(lined + j * wpld), wd,
                                         lineb.AsSpan(j * wplb), 8, thresh);
            }
        }

        /* Do last src line */
        {
            ReadOnlySpan<uint> lines = datas.AsSpan(hsm * wpls);
            int lined = 4 * hsm * wpld;
            ScaleGray4xLILineLow(lineb, wplb, lines, ws, wpls, 1);
            for (j = 0; j < 4; j++) {
                ThresholdToBinaryLineLow(datad.AsSpan(lined + j * wpld), wd,
                                         lineb.AsSpan(j * wplb), 8, thresh);
            }
        }

        return pixd;
    }

    // leptonica: pixScaleGray4xLIDither (scale1.c)
    /// <summary>Scales 4x by linear interpolation, then dithers to binary.</summary>
    /// <returns>1 bpp pix</returns>
    /// <remarks>
    /// This pix is 8 bpp, not cmapped.
    /// (1) This does 4x upscale on pixs, using linear interpolation,
    ///     followed by Floyd-Steinberg dithering to binary.
    /// (2) Buffers are used to avoid making a large grayscale image.
    ///     ~ Two line buffers are used for the src, required for the
    ///       4xLI upscale.
    ///     ~ Five line buffers are used for the intermediate image.
    ///       Four are filled with each 4xLI row operation; the fifth
    ///       is needed because the upscale and dithering ops are
    ///       out of sync.
    /// (3) If a full 4x expanded grayscale image can be kept in memory,
    ///     this function is only about 5% faster than separately doing
    ///     a linear interpolation to a large grayscale image, followed
    ///     by error-diffusion dithering to binary.
    /// </remarks>
    public Pix ScaleGray4xLIDither()
    {
        const string func = "pixScaleGray4xLIDither";
        int i, j;
        Pix pixd;

        if (Depth != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs undefined, not 8 bpp, or cmapped");

        GetDimensions(out int ws, out int hs, out _);
        int wd = 4 * ws;
        int hd = 4 * hs;
        int hsm = hs - 1;
        uint[] datas = Data;
        int wpls = Wpl;

        /* NOTE(upstream): for hs == 1 the C code reads a second src line that
         * does not exist and writes into dest lines below the image. */
        if (hs < 2)
            throw Lept.Arg(func, "pixs height must be at least 2");

        /* Make line buffers for 2 lines of src image */
        uint[] bufs = new uint[2 * wpls];

        /* Make line buffer for 4 lines of virtual intermediate image */
        int wplb = (wd + 3) / 4;
        uint[] lineb = new uint[4 * wplb];

        /* Make line buffer for 1 line of virtual intermediate image */
        uint[] linebp = new uint[wplb];

        /* Make dest binary image */
        pixd = Create(wd, hd, 1);
        pixd.CopyInputFormat(this);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(4.0f, 4.0f);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;

        /* Start with the first src and the first 3 dest lines */
        datas.AsSpan(0, wpls).CopyTo(bufs);   /* first src line */
        datas.AsSpan(wpls, wpls).CopyTo(bufs.AsSpan(wpls));  /* 2nd src line */
        ScaleGray4xLILineLow(lineb, wplb, bufs, ws, wpls, 0);  /* 4 b lines */
        int lined = 0;
        for (j = 0; j < 3; j++) {  /* first 3 d lines of Q */
            DitherToBinaryLineLow(datad.AsSpan(lined + j * wpld), wd, lineb.AsSpan(j * wplb),
                                  lineb.AsSpan((j + 1) * wplb),
                                  L.DEFAULT_CLIP_LOWER_1, L.DEFAULT_CLIP_UPPER_1, 0);
        }

        /* Do all but last src line */
        for (i = 1; i < hsm; i++) {
            datas.AsSpan(i * wpls, wpls).CopyTo(bufs);  /* i-th src line */
            datas.AsSpan((i + 1) * wpls, wpls).CopyTo(bufs.AsSpan(wpls));
            lineb.AsSpan(3 * wplb, wplb).CopyTo(linebp);
            ScaleGray4xLILineLow(lineb, wplb, bufs, ws, wpls, 0);  /* 4 b lines */
            lined = 4 * i * wpld;
            DitherToBinaryLineLow(datad.AsSpan(lined - wpld), wd, linebp, lineb,
                                  L.DEFAULT_CLIP_LOWER_1, L.DEFAULT_CLIP_UPPER_1, 0);
                                                         /* 4th dest line of Q */
            for (j = 0; j < 3; j++) {  /* next 3 d lines of Quad */
                DitherToBinaryLineLow(datad.AsSpan(lined + j * wpld), wd, lineb.AsSpan(j * wplb),
                                      lineb.AsSpan((j + 1) * wplb),
                                      L.DEFAULT_CLIP_LOWER_1, L.DEFAULT_CLIP_UPPER_1, 0);
            }
        }

        /* Do the last src line and the last 5 dest lines */
        datas.AsSpan(hsm * wpls, wpls).CopyTo(bufs);  /* hsm-th src line */
        lineb.AsSpan(3 * wplb, wplb).CopyTo(linebp);   /* 1 b line */
        ScaleGray4xLILineLow(lineb, wplb, bufs, ws, wpls, 1);  /* 4 b lines */
        lined = 4 * hsm * wpld;
        DitherToBinaryLineLow(datad.AsSpan(lined - wpld), wd, linebp, lineb,
                              L.DEFAULT_CLIP_LOWER_1, L.DEFAULT_CLIP_UPPER_1, 0);
                                                       /* 4th dest line of Q */
        for (j = 0; j < 3; j++) {  /* next 3 d lines of Quad */
            DitherToBinaryLineLow(datad.AsSpan(lined + j * wpld), wd, lineb.AsSpan(j * wplb),
                                  lineb.AsSpan((j + 1) * wplb),
                                  L.DEFAULT_CLIP_LOWER_1, L.DEFAULT_CLIP_UPPER_1, 0);
        }
        /* And finally, the last dest line */
        DitherToBinaryLineLow(datad.AsSpan(lined + 3 * wpld), wd, lineb.AsSpan(3 * wplb), default,
                              L.DEFAULT_CLIP_LOWER_1, L.DEFAULT_CLIP_UPPER_1, 1);

        return pixd;
    }

    /*------------------------------------------------------------------*
     *                  Scaling by closest pixel sampling               *
     *------------------------------------------------------------------*/
    // leptonica: pixScaleBySampling (scale1.c)
    /// <summary>Scales by sampling the closest source pixel.</summary>
    /// <param name="scalex">must be &gt; 0.0</param>
    /// <param name="scaley">must be &gt; 0.0</param>
    /// <remarks>
    /// This pix is 1, 2, 4, 8, 16, 32 bpp.
    /// (1) This function samples from the source without
    ///     filtering.  As a result, aliasing will occur for
    ///     subsampling (%scalex and/or %scaley &lt; 1.0).
    /// (2) If %scalex == 1.0 and %scaley == 1.0, returns a copy.
    /// (3) For upscaling by an integer, use pixExpandReplicate().
    /// (4) By default, indexing for the sampled source pixel is done
    ///     by rounding.  This shifts the source pixel sampling down
    ///     and to the right by half a pixel, which has the effect of
    ///     shifting the destination image up and to the left by a
    ///     number of pixels approximately equal to half the scaling
    ///     factor.  To avoid this shift in the destination image,
    ///     call pixScalebySamplingWithShift() using 0 for both shifts.
    /// </remarks>
    public Pix ScaleBySampling(float scalex, float scaley)
    {
        return ScaleBySamplingWithShift(scalex, scaley, 0.5f, 0.5f);
    }

    // leptonica: pixScaleBySamplingWithShift (scale1.c)
    /// <summary>Scales by sampling the closest source pixel, with a sampling shift.</summary>
    /// <param name="scalex">must be &gt; 0.0</param>
    /// <param name="scaley">must be &gt; 0.0</param>
    /// <param name="shiftx">0.5 for default; 0.0 to mihimize edge effects</param>
    /// <param name="shifty">0.5 for default; 0.0 to mihimize edge effects</param>
    /// <remarks>
    /// This pix is 1, 2, 4, 8, 16, 32 bpp.
    /// (1) The %shiftx and %shifty parameters are usually unimportant.
    ///     Visible artifacts are minimized by using 0.0.
    ///     Allowed values are 0.0 and 0.5.
    /// </remarks>
    public Pix ScaleBySamplingWithShift(float scalex, float scaley, float shiftx, float shifty)
    {
        const string func = "pixScaleBySamplingWithShift";
        int d;
        Pix pixd;

        if (scalex <= 0.0 || scaley <= 0.0)
            throw Lept.Arg(func, "scale factor <= 0");
        if (scalex == 1.0 && scaley == 1.0)
            return Copy(null, this);
        if (shiftx != 0.0 && shiftx != 0.5)
            throw Lept.Arg(func, "shiftx != 0.0 or 0.5");
        if (shifty != 0.0 && shifty != 0.5)
            throw Lept.Arg(func, "shifty != 0.0 or 0.5");
        if ((d = Depth) == 1)
            return ScaleBinaryWithShift(scalex, scaley, shiftx, shifty);

        GetDimensions(out int ws, out int hs, out _);
        uint[] datas = Data;
        int wpls = Wpl;
        int wd = (int)(scalex * (float)ws + 0.5);
        int hd = (int)(scaley * (float)hs + 0.5);
        // at 32 bpp the low-level code writes every dest word
        pixd = d == 32 ? CreateRawNoInit(wd, hd, d) : Create(wd, hd, d);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(scalex, scaley);
        pixd.CopyColormap(this);
        pixd.CopyText(this);
        pixd.CopyInputFormat(this);
        pixd.CopySpp(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        ScaleBySamplingLow(datad, wd, hd, wpld, datas, ws, hs, d, wpls,
                           shiftx, shifty);
        if (d == 32 && Spp == 4)
            pixd.ScaleAndTransferAlpha(this, scalex, scaley);

        return pixd;
    }

    // leptonica: pixScaleBySamplingToSize (scale1.c)
    /// <summary>Scales by sampling to a given width and/or height.</summary>
    /// <param name="wd">target width; use 0 if using height as target</param>
    /// <param name="hd">target height; use 0 if using width as target</param>
    /// <remarks>
    /// This pix is 1, 2, 4, 8, 16 and 32 bpp.
    /// (1) This guarantees that the output scaled image has the
    ///     dimension(s) you specify.
    ///     ~ To specify the width with isotropic scaling, set %hd = 0.
    ///     ~ To specify the height with isotropic scaling, set %wd = 0.
    ///     ~ If both %wd and %hd are specified, the image is scaled
    ///       (in general, anisotropically) to that size.
    ///     ~ It is an error to set both %wd and %hd to 0.
    /// </remarks>
    public Pix ScaleBySamplingToSize(int wd, int hd)
    {
        const string func = "pixScaleBySamplingToSize";
        float scalex, scaley;

        if (wd <= 0 && hd <= 0)
            throw Lept.Arg(func, "neither wd nor hd > 0");

        GetDimensions(out int w, out int h, out _);
        if (wd <= 0) {
            scaley = (float)hd / (float)h;
            scalex = scaley;
        } else if (hd <= 0) {
            scalex = (float)wd / (float)w;
            scaley = scalex;
        } else {
            scalex = (float)wd / (float)w;
            scaley = (float)hd / (float)h;
        }

        return ScaleBySampling(scalex, scaley);
    }

    // leptonica: pixScaleByIntSampling (scale1.c)
    /// <summary>Scales by integer subsampling.</summary>
    /// <param name="factor">integer subsampling; &gt;= 1</param>
    /// <remarks>
    /// This pix is 1, 2, 4, 8, 16, 32 bpp  (all depths).
    /// (1) Simple interface to pixScaleBySampling(), for isotropic
    ///     integer reduction.  If %factor == 1, returns a copy.
    /// </remarks>
    public Pix ScaleByIntSampling(int factor)
    {
        const string func = "pixScaleByIntSampling";

        if (factor <= 1) {
            if (factor < 1)
                Log.Error(func, "factor must be >= 1; returning a copy\n");
            return Copy(null, this);
        }

        float scale = 1.0f / (float)factor;
        return ScaleBySampling(scale, scale);
    }

    /*------------------------------------------------------------------*
     *            Fast integer factor subsampling RGB to gray           *
     *------------------------------------------------------------------*/
    // leptonica: pixScaleRGBToGrayFast (scale1.c)
    /// <summary>Subsamples by an integer factor and extracts one color component.</summary>
    /// <param name="factor">integer reduction factor &gt;= 1</param>
    /// <param name="color">one of COLOR_RED, COLOR_GREEN, COLOR_BLUE</param>
    /// <returns>8 bpp pix</returns>
    /// <remarks>
    /// This pix is 32 bpp rgb.
    /// (1) This does simultaneous subsampling by an integer factor and
    ///     extraction of the color from the RGB pix.
    /// (2) It is designed for maximum speed, and is used for quickly
    ///     generating a downsized grayscale image from a higher resolution
    ///     RGB image.  This would typically be used for image analysis.
    /// (3) The standard color byte order (RGBA) is assumed.
    /// </remarks>
    public Pix ScaleRGBToGrayFast(int factor, int color)
    {
        const string func = "pixScaleRGBToGrayFast";
        int byteval, shift;
        int i, j;
        float scale;
        Pix pixd;

        if (Depth != 32)
            throw Lept.Arg(func, "depth not 32 bpp");
        if (factor < 1)
            throw Lept.Arg(func, "factor must be >= 1");

        if (color == L.COLOR_RED)
            shift = L.L_RED_SHIFT;
        else if (color == L.COLOR_GREEN)
            shift = L.L_GREEN_SHIFT;
        else if (color == L.COLOR_BLUE)
            shift = L.L_BLUE_SHIFT;
        else
            throw Lept.Arg(func, "invalid color");

        GetDimensions(out int ws, out int hs, out _);
        uint[] datas = Data;
        int wpls = Wpl;

        int wd = ws / factor;
        int hd = hs / factor;
        pixd = Create(wd, hd, 8);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        scale = 1.0f / (float)factor;
        pixd.ScaleResolution(scale, scale);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        for (i = 0; i < hd; i++) {
            int words = i * factor * wpls;
            Span<uint> lined = datad.AsSpan(i * wpld);
            for (j = 0; j < wd; j++, words += factor) {
                byteval = (int)((datas[words] >> shift) & 0xff);
                SetDataByte(lined, j, byteval);
            }
        }

        return pixd;
    }

    // leptonica: pixScaleRGBToBinaryFast (scale1.c)
    /// <summary>Subsamples by an integer factor and binarizes the green channel.</summary>
    /// <param name="factor">integer reduction factor &gt;= 1</param>
    /// <param name="thresh">binarization threshold</param>
    /// <returns>1 bpp pix</returns>
    /// <remarks>
    /// This pix is 32 bpp RGB.
    /// (1) This does simultaneous subsampling by an integer factor and
    ///     conversion from RGB to gray to binary.
    /// (2) It is designed for maximum speed, and is used for quickly
    ///     generating a downsized binary image from a higher resolution
    ///     RGB image.  This would typically be used for image analysis.
    /// (3) It uses the green channel to represent the RGB pixel intensity.
    /// </remarks>
    public Pix ScaleRGBToBinaryFast(int factor, int thresh)
    {
        const string func = "pixScaleRGBToBinaryFast";
        int byteval;
        int i, j;
        float scale;
        Pix pixd;

        if (factor < 1)
            throw Lept.Arg(func, "factor must be >= 1");
        if (Depth != 32)
            throw Lept.Arg(func, "depth not 32 bpp");

        GetDimensions(out int ws, out int hs, out _);
        uint[] datas = Data;
        int wpls = Wpl;

        int wd = ws / factor;
        int hd = hs / factor;
        pixd = Create(wd, hd, 1);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        scale = (float)(1.0 / (float)factor);
        pixd.ScaleResolution(scale, scale);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        for (i = 0; i < hd; i++) {
            int words = i * factor * wpls;
            Span<uint> lined = datad.AsSpan(i * wpld);
            for (j = 0; j < wd; j++, words += factor) {
                byteval = (int)((datas[words] >> L.L_GREEN_SHIFT) & 0xff);
                if (byteval < thresh)
                    SetDataBit(lined, j);
            }
        }

        return pixd;
    }

    // leptonica: pixScaleGrayToBinaryFast (scale1.c)
    /// <summary>Subsamples by an integer factor and thresholds to binary.</summary>
    /// <param name="factor">integer reduction factor &gt;= 1</param>
    /// <param name="thresh">binarization threshold</param>
    /// <returns>1 bpp pix</returns>
    /// <remarks>
    /// This pix is 8 bpp grayscale.
    /// (1) This does simultaneous subsampling by an integer factor and
    ///     thresholding from gray to binary.
    /// (2) It is designed for maximum speed, and is used for quickly
    ///     generating a downsized binary image from a higher resolution
    ///     gray image.  This would typically be used for image analysis.
    /// </remarks>
    public Pix ScaleGrayToBinaryFast(int factor, int thresh)
    {
        const string func = "pixScaleGrayToBinaryFast";
        int byteval;
        int i, j, sj;
        float scale;
        Pix pixd;

        if (factor < 1)
            throw Lept.Arg(func, "factor must be >= 1");
        if (Depth != 8)
            throw Lept.Arg(func, "depth not 8 bpp");

        GetDimensions(out int ws, out int hs, out _);
        uint[] datas = Data;
        int wpls = Wpl;

        int wd = ws / factor;
        int hd = hs / factor;
        pixd = Create(wd, hd, 1);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        scale = 1.0f / (float)factor;
        pixd.ScaleResolution(scale, scale);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        for (i = 0; i < hd; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * factor * wpls);
            Span<uint> lined = datad.AsSpan(i * wpld);
            for (j = 0, sj = 0; j < wd; j++, sj += factor) {
                byteval = GetDataByte(lines, sj);
                if (byteval < thresh)
                    SetDataBit(lined, j);
            }
        }

        return pixd;
    }

    /*------------------------------------------------------------------*
     *               Downscaling with (antialias) smoothing             *
     *------------------------------------------------------------------*/
    // leptonica: pixScaleSmooth (scale1.c)
    /// <summary>Downscales with a flat (whole-pixel) low-pass filter.</summary>
    /// <param name="scalex">must be &lt; 0.7</param>
    /// <param name="scaley">must be &lt; 0.7</param>
    /// <remarks>
    /// This pix is 2, 4, 8 or 32 bpp; and 2, 4, 8 bpp with colormap.
    /// (1) This function should only be used when the scale factors are less
    ///     than 0.7.  If either scale factor is &gt;= 0.7, issue a warning
    ///     and call pixScaleGeneral(), which will invoke linear interpolation
    ///     without sharpening.
    /// (2) This works only on 2, 4, 8 and 32 bpp images, and if there is
    ///     a colormap, it is removed by converting to RGB.
    /// (3) It does simple (flat filter) convolution, with a filter size
    ///     commensurate with the amount of reduction, to avoid antialiasing.
    /// (4) It does simple subsampling after smoothing, which is appropriate
    ///     for this range of scaling.  Linear interpolation gives essentially
    ///     the same result with more computation for these scale factors,
    ///     so we don't use it.
    /// (5) The result is the same as doing a full block convolution followed by
    ///     subsampling, but this is faster because the results of the block
    ///     convolution are only computed at the subsampling locations.
    ///     In fact, the computation time is approximately independent of
    ///     the scale factor, because the convolution kernel is adjusted
    ///     so that each source pixel is summed approximately once.
    /// </remarks>
    public Pix ScaleSmooth(float scalex, float scaley)
    {
        const string func = "pixScaleSmooth";
        int d, isize;
        float minscale, size;
        Pix pixs, pixd;

        if (scalex >= 0.7 || scaley >= 0.7) {
            Log.Warning(func, "scaling factor not < 0.7; do regular scaling\n");
            return ScaleGeneral(scalex, scaley, 0.0f, 0);
        }
        d = Depth;
        if (d != 2 && d != 4 && d != 8 && d != 32)
            throw Lept.Arg(func, "pix not 2, 4, 8 or 32 bpp");

        /* Remove colormap; clone if possible; result is either 8 or 32 bpp */
        pixs = ConvertTo8Or32(L.L_CLONE, 0);
        d = pixs.Depth;

        /* If 1.42 < 1/minscale < 2.5, use isize = 2
         * If 2.5 =< 1/minscale < 3.5, use isize = 3, etc.
         * Under no conditions use isize < 2  */
        minscale = Math.Min(scalex, scaley);
        size = 1.0f / minscale;   /* ideal filter full width */
        isize = Math.Min(10000, Math.Max(2, (int)(size + 0.5)));

        pixs.GetDimensions(out int ws, out int hs, out _);
        if ((ws < isize) || (hs < isize)) {
            pixd = Create(1, 1, d);
            pixs.GetPixel(ws / 2, hs / 2, out uint val);
            pixd.SetPixel(0, 0, val);
            Log.Warning(func, CFormat.Sprintf("ridiculously small scaling factor %f\n", minscale));
            return pixd;
        }

        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        int wd = Math.Max(1, (int)(scalex * (float)ws + 0.5));
        int hd = Math.Max(1, (int)(scaley * (float)hs + 0.5));
        pixd = CreateNoInit(wd, hd, d);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        pixd.ScaleResolution(scalex, scaley);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        ScaleSmoothLow(datad, wd, hd, wpld, datas, ws, hs, d, wpls, isize);
        if (d == 32 && pixs.Spp == 4)
            pixd.ScaleAndTransferAlpha(pixs, scalex, scaley);

        return pixd;
    }

    // leptonica: pixScaleSmoothToSize (scale1.c)
    /// <summary>Downscales with a flat low-pass filter to a given width and/or height.</summary>
    /// <param name="wd">target width; use 0 if using height as target</param>
    /// <param name="hd">target height; use 0 if using width as target</param>
    /// <remarks>
    /// This pix is 2, 4, 8 or 32 bpp; and 2, 4, 8 bpp with colormap.
    /// (1) See notes in pixScaleSmooth().
    /// (2) The output scaled image has the dimension(s) you specify:
    ///     - To specify the width with isotropic scaling, set %hd = 0.
    ///     - To specify the height with isotropic scaling, set %wd = 0.
    ///     - If both %wd and %hd are specified, the image is scaled
    ///        (in general, anisotropically) to that size.
    ///     - It is an error to set both %wd and %hd to 0.
    /// </remarks>
    public Pix ScaleSmoothToSize(int wd, int hd)
    {
        const string func = "pixScaleSmoothToSize";
        float scalex, scaley;

        if (wd <= 0 && hd <= 0)
            throw Lept.Arg(func, "neither wd nor hd > 0");

        GetDimensions(out int w, out int h, out _);
        if (wd <= 0) {
            scaley = (float)hd / (float)h;
            scalex = scaley;
        } else if (hd <= 0) {
            scalex = (float)wd / (float)w;
            scaley = scalex;
        } else {
            scalex = (float)wd / (float)w;
            scaley = (float)hd / (float)h;
        }

        return ScaleSmooth(scalex, scaley);
    }

    // leptonica: pixScaleRGBToGray2 (scale1.c)
    /// <summary>Reduces a 32 bpp rgb image 2x to 8 bpp gray with the given weights.</summary>
    /// <param name="rwt">red weight; the three weights must sum to 1.0</param>
    /// <param name="gwt">green weight</param>
    /// <param name="bwt">blue weight</param>
    /// <returns>8 bpp, 2x reduced</returns>
    public Pix ScaleRGBToGray2(float rwt, float gwt, float bwt)
    {
        const string func = "pixScaleRGBToGray2";
        Pix pixd;

        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (rwt + gwt + bwt < 0.98 || rwt + gwt + bwt > 1.02)
            throw Lept.Arg(func, "sum of wts should be 1.0");

        int wd = Width / 2;
        int hd = Height / 2;
        int wpls = Wpl;
        uint[] datas = Data;
        pixd = CreateNoInit(wd, hd, 8);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        pixd.ScaleResolution(0.5f, 0.5f);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        ScaleRGBToGray2Low(datad, wd, hd, wpld, datas, wpls, rwt, gwt, bwt);
        return pixd;
    }

    /*------------------------------------------------------------------*
     *             Downscaling with (antialias) area mapping            *
     *------------------------------------------------------------------*/
    // leptonica: pixScaleAreaMap (scale1.c)
    /// <summary>Downscales with a low-pass filter that averages over fractional pixels.</summary>
    /// <param name="scalex">must be &lt; 0.7; minimum is 0.02</param>
    /// <param name="scaley">must be &lt; 0.7; minimum is 0.02</param>
    /// <remarks>
    /// This pix is 2, 4, 8 or 32 bpp; and 2, 4, 8 bpp with colormap.
    /// (1) This is a low-pass filter that averages over fractional pixels.
    ///     It should only be used when the scale factors are less than 0.7.
    ///     If either scale factor is greater than or equal to 0.7, we
    ///     issue a warning and call pixScaleGeneral(), which will invoke
    ///     linear interpolation without sharpening.
    /// (2) The minimum scale factor allowed for area mapping reduction
    ///     is 0.02.  Various overflows will occur when scale factors are
    ///     less than about 1/256.  If a scale factor smaller than 0.02
    ///     is given, we use pixScaleSmooth(), which is a low-pass filter
    ///     that averages over entire pixels.
    /// (3) This works only on 2, 4, 8 and 32 bpp images.  If there is
    ///     a colormap, it is removed by converting to RGB.  In other
    ///     cases, we issue a warning and call pixScaleGeneral().
    /// (4) This is faster than pixScale() because it does not do sharpening.
    /// (5) It does a relatively expensive area mapping computation, to
    ///     avoid antialiasing.  It is about 2x slower than pixScaleSmooth(),
    ///     but the results are much better on fine text.
    /// (6) pixScaleAreaMap2() is typically about 7x faster for the special
    ///     case of 2x reduction for color images, and about 9x faster
    ///     for grayscale images.  Surprisingly, the improvement in speed
    ///     when using a cascade of 2x reductions for small scale factors is
    ///     less than one might expect, and in most situations gives
    ///     poorer image quality.  But see (6).
    /// (7) For reductions between 0.35 and 0.5, a 2x area map reduction
    ///     followed by using pixScaleGeneral() on a 2x larger scalefactor
    ///     (which further reduces the image size using bilinear interpolation)
    ///     would give a significant speed increase, with little loss of
    ///     quality, but this is not enabled as it would break too many tests.
    ///     For scaling factors below 0.35, scaling atomically is nearly
    ///     as fast as using a cascade of 2x scalings, and gives
    ///     better results.
    /// </remarks>
    public Pix ScaleAreaMap(float scalex, float scaley)
    {
        const string func = "pixScaleAreaMap";
        int d;
        float maxscale, minscale;
        Pix pixs, pixd, pix1, pix2, pix3;

        d = Depth;
        if (d != 2 && d != 4 && d != 8 && d != 32)
            throw Lept.Arg(func, "pix not 2, 4, 8 or 32 bpp");

        minscale = Math.Min(scalex, scaley);
        if (minscale < 0.02) {  /* too small for area mapping */
            Log.Warning(func, "tiny scaling factor; using pixScaleSmooth()\n");
            return ScaleSmooth(scalex, scaley);
        }

        maxscale = Math.Max(scalex, scaley);
        if (maxscale >= 0.7) {  /* too large for area mapping */
            Log.Warning(func, "scaling factor >= 0.7; do regular scaling\n");
            return ScaleGeneral(scalex, scaley, 0.0f, 0);
        }

        /* Special cases: 2x, 4x, 8x, 16x reduction */
        if (scalex == 0.5 && scaley == 0.5)
            return ScaleAreaMap2();
        if (scalex == 0.25 && scaley == 0.25) {
            pix1 = ScaleAreaMap2();
            pixd = pix1.ScaleAreaMap2();
            return pixd;
        }
        if (scalex == 0.125 && scaley == 0.125) {
            pix1 = ScaleAreaMap2();
            pix2 = pix1.ScaleAreaMap2();
            pixd = pix2.ScaleAreaMap2();
            return pixd;
        }
        if (scalex == 0.0625 && scaley == 0.0625) {
            pix1 = ScaleAreaMap2();
            pix2 = pix1.ScaleAreaMap2();
            pix3 = pix2.ScaleAreaMap2();
            pixd = pix3.ScaleAreaMap2();
            return pixd;
        }

        /* Remove colormap if necessary.
         * If 2 bpp or 4 bpp gray, convert to 8 bpp */
        if ((d == 2 || d == 4 || d == 8) && Colormap != null) {
            Log.Warning(func, "pix has colormap; removing\n");
            pixs = RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            d = pixs.Depth;
        } else if (d == 2 || d == 4) {
            pixs = ConvertTo8(false);
            d = 8;
        } else {
            pixs = Clone(this);
        }

        pixs.GetDimensions(out int ws, out int hs, out _);
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        int wd = (int)(scalex * (float)ws + 0.5);
        int hd = (int)(scaley * (float)hs + 0.5);
        if (wd < 1 || hd < 1)
            throw Lept.Arg(func, "pixd too small");
        pixd = CreateNoInit(wd, hd, d);
        pixd.CopyInputFormat(pixs);
        pixd.CopyResolution(pixs);
        pixd.ScaleResolution(scalex, scaley);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        if (d == 8) {
            ScaleGrayAreaMapLow(datad, wd, hd, wpld, datas, ws, hs, wpls);
        } else {  /* RGB, d == 32 */
            ScaleColorAreaMapLow(datad, wd, hd, wpld, datas, ws, hs, wpls);
            if (pixs.Spp == 4)
                pixd.ScaleAndTransferAlpha(pixs, scalex, scaley);
        }

        return pixd;
    }

    // leptonica: pixScaleAreaMap2 (scale1.c)
    /// <summary>Reduces 2x by area mapping (average of 2x2 pixels).</summary>
    /// <remarks>
    /// This pix is 2, 4, 8 or 32 bpp; and 2, 4, 8 bpp with colormap.
    /// (1) This function does an area mapping (average) for 2x
    ///     reduction.
    /// (2) This works only on 2, 4, 8 and 32 bpp images.  If there is
    ///     a colormap, it is removed by converting to RGB.
    /// (3) Compared to the general pixScaleAreaMap(), for this function
    ///     gray processing is about 14x faster and color processing
    ///     is about 4x faster.  Consequently, pixScaleAreaMap2() is
    ///     incorporated into the general area map scaling function,
    ///     for the special cases of 2x, 4x, 8x and 16x reduction.
    /// </remarks>
    public Pix ScaleAreaMap2()
    {
        const string func = "pixScaleAreaMap2";
        int d;
        Pix pixs, pixd;

        d = Depth;
        if (d != 2 && d != 4 && d != 8 && d != 32)
            throw Lept.Arg(func, "pix not 2, 4, 8 or 32 bpp");

        /* Remove colormap if necessary.
         * If 2 bpp or 4 bpp gray, convert to 8 bpp */
        if ((d == 2 || d == 4 || d == 8) && Colormap != null) {
            Log.Warning(func, "pix has colormap; removing\n");
            pixs = RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            d = pixs.Depth;
        } else if (d == 2 || d == 4) {
            pixs = ConvertTo8(false);
            d = 8;
        } else {
            pixs = Clone(this);
        }

        int wd = pixs.Width / 2;
        int hd = pixs.Height / 2;
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        pixd = Create(wd, hd, d);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        pixd.CopyInputFormat(pixs);
        pixd.CopyResolution(pixs);
        pixd.ScaleResolution(0.5f, 0.5f);
        ScaleAreaMapLow2(datad, wd, hd, wpld, datas, d, wpls);
        if (pixs.Spp == 4)
            pixd.ScaleAndTransferAlpha(pixs, 0.5f, 0.5f);
        return pixd;
    }

    // leptonica: pixScaleAreaMapToSize (scale1.c)
    /// <summary>Downscales by area mapping to a given width and/or height.</summary>
    /// <param name="wd">target width; use 0 if using height as target</param>
    /// <param name="hd">target height; use 0 if using width as target</param>
    /// <remarks>
    /// This pix is 2, 4, 8 or 32 bpp; and 2, 4, 8 bpp with colormap.
    /// (1) See notes in pixScaleAreaMap().
    /// (2) The output scaled image has the dimension(s) you specify:
    ///     - To specify the width with isotropic scaling, set %hd = 0.
    ///     - To specify the height with isotropic scaling, set %wd = 0.
    ///     - If both %wd and %hd are specified, the image is scaled
    ///        (in general, anisotropically) to that size.
    ///     - It is an error to set both %wd and %hd to 0.
    /// </remarks>
    public Pix ScaleAreaMapToSize(int wd, int hd)
    {
        const string func = "pixScaleAreaMapToSize";
        float scalex, scaley;

        if (wd <= 0 && hd <= 0)
            throw Lept.Arg(func, "neither wd nor hd > 0");

        GetDimensions(out int w, out int h, out _);
        if (wd <= 0) {
            scaley = (float)hd / (float)h;
            scalex = scaley;
        } else if (hd <= 0) {
            scalex = (float)wd / (float)w;
            scaley = scalex;
        } else {
            scalex = (float)wd / (float)w;
            scaley = (float)hd / (float)h;
        }

        return ScaleAreaMap(scalex, scaley);
    }

    /*------------------------------------------------------------------*
     *               Binary scaling by closest pixel sampling           *
     *------------------------------------------------------------------*/
    // leptonica: pixScaleBinary (scale1.c)
    /// <summary>Scales a 1 bpp image by sampling the closest source pixel.</summary>
    /// <param name="scalex">must be &gt; 0.0</param>
    /// <param name="scaley">must be &gt; 0.0</param>
    /// <remarks>
    /// This pix is 1 bpp.
    /// (1) This function samples from the source without
    ///     filtering.  As a result, aliasing will occur for
    ///     subsampling (scalex and scaley &lt; 1.0).
    /// (2) By default, indexing for the sampled source pixel is done
    ///     by rounding.  This shifts the source pixel sampling down
    ///     and to the right by half a pixel, which has the effect of
    ///     shifting the destination image up and to the left by a
    ///     number of pixels approximately equal to half the scaling
    ///     factor.  To avoid this shift in the destination image,
    ///     call pixScalebySamplingWithShift() using 0 for both shifts.
    /// </remarks>
    public Pix ScaleBinary(float scalex, float scaley)
    {
        if (Depth != 1)
            throw Lept.Arg("pixScaleBinary", "pixs must be 1 bpp");
        return ScaleBinaryWithShift(scalex, scaley, 0.5f, 0.5f);
    }

    // leptonica: pixScaleBinaryWithShift (scale1.c)
    /// <summary>Scales a 1 bpp image by sampling the closest source pixel, with a sampling shift.</summary>
    /// <param name="scalex">must be &gt; 0.0</param>
    /// <param name="scaley">must be &gt; 0.0</param>
    /// <param name="shiftx">0.5 for default; 0.0 to mihimize edge effects</param>
    /// <param name="shifty">0.5 for default; 0.0 to mihimize edge effects</param>
    /// <remarks>
    /// This pix is 1 bpp.
    /// (1) The %shiftx and %shifty parameters are usually unimportant.
    ///     Visible artifacts are minimized by using 0.0.
    ///     Allowed values are 0.0 and 0.5.
    /// </remarks>
    public Pix ScaleBinaryWithShift(float scalex, float scaley, float shiftx, float shifty)
    {
        const string func = "pixScaleBinaryWithShift";
        Pix pixd;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs must be 1 bpp");
        if (scalex <= 0.0 || scaley <= 0.0)
            throw Lept.Arg(func, "scale factor <= 0");
        if (scalex == 1.0 && scaley == 1.0)
            return Copy(null, this);
        if (shiftx != 0.0 && shiftx != 0.5)
            throw Lept.Arg(func, "shiftx != 0.0 or 0.5");
        if (shifty != 0.0 && shifty != 0.5)
            throw Lept.Arg(func, "shifty != 0.0 or 0.5");

        GetDimensions(out int ws, out int hs, out _);
        uint[] datas = Data;
        int wpls = Wpl;
        int wd = (int)(scalex * (float)ws + 0.5);
        int hd = (int)(scaley * (float)hs + 0.5);
        pixd = Create(wd, hd, 1);
        pixd.CopyColormap(this);
        pixd.CopyText(this);
        pixd.CopyInputFormat(this);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(scalex, scaley);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        ScaleBinaryLow(datad, wd, hd, wpld, datas, ws, hs, wpls, shiftx, shifty);
        return pixd;
    }

    /* ================================================================ *
     *                    Low level static functions                    *
     * ================================================================ */

    /*------------------------------------------------------------------*
     *            General linear interpolated color scaling             *
     *------------------------------------------------------------------*/
    // leptonica: scaleColorLILow (scale1.c)
    /// <remarks>
    /// (1) We choose to divide each pixel into 16 x 16 sub-pixels.
    ///     Linear interpolation is equivalent to finding the
    ///     fractional area (i.e., number of sub-pixels divided
    ///     by 256) associated with each of the four nearest src pixels,
    ///     and weighting each pixel value by this fractional area.
    /// </remarks>
    private static void ScaleColorLILow(uint[] datad, int wd, int hd, int wpld,
                                        uint[] datas, int ws, int hs, int wpls)
    {
        float scx = 16.0f * (float)ws / (float)wd;
        float scy = 16.0f * (float)hs / (float)hd;
        int wm2 = ws - 2;
        int hm2 = hs - 2;

        LeptParallel.ForRows(hd, wd, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                int ypm = (int)(scy * (float)i);
                int yp = ypm >> 4;
                int yf = ypm & 0x0f;
                int wy0 = 16 - yf;
                int lined = i * wpld;
                int lines = yp * wpls;
                int rowStride = yp > hm2 ? 0 : wpls;

                for (int j = 0; j < wd; j++) {
                    int xpm = (int)(scx * (float)j);
                    int xp = xpm >> 4;
                    int xf = xpm & 0x0f;
                    int wx0 = 16 - xf;
                    int colStride = xp > wm2 ? 0 : 1;

                    int p0 = lines + xp;
                    uint p00 = Unsafe.Add(ref datasRef, p0);
                    uint p10 = Unsafe.Add(ref datasRef, p0 + colStride);
                    uint p01 = Unsafe.Add(ref datasRef, p0 + rowStride);
                    uint p11 = Unsafe.Add(ref datasRef, p0 + rowStride + colStride);

                    uint w00 = (uint)(wx0 * wy0);
                    uint w10 = (uint)(xf * wy0);
                    uint w01 = (uint)(wx0 * yf);
                    uint w11 = (uint)(xf * yf);

                    uint rb = (w00 * ((p00 >> 8) & 0x00ff00ffu) +
                               w10 * ((p10 >> 8) & 0x00ff00ffu) +
                               w01 * ((p01 >> 8) & 0x00ff00ffu) +
                               w11 * ((p11 >> 8) & 0x00ff00ffu) + 0x00800080u) & 0xff00ff00u;
                    uint g = ((w00 * ((p00 >> 16) & 0xffu) +
                               w10 * ((p10 >> 16) & 0xffu) +
                               w01 * ((p01 >> 16) & 0xffu) +
                               w11 * ((p11 >> 16) & 0xffu) + 128u) << 8) & 0x00ff0000u;
                    Unsafe.Add(ref datadRef, lined + j) = rb | g;
                }
            }
        });
    }

    /*------------------------------------------------------------------*
     *            General linear interpolated gray scaling              *
     *------------------------------------------------------------------*/
    // leptonica: scaleGrayLILow (scale1.c)
    /// <remarks>
    /// (1) We choose to divide each pixel into 16 x 16 sub-pixels.
    ///     Linear interpolation is equivalent to finding the
    ///     fractional area (i.e., number of sub-pixels divided
    ///     by 256) associated with each of the four nearest src pixels,
    ///     and weighting each pixel value by this fractional area.
    /// </remarks>
    private static void ScaleGrayLILow(uint[] datad, int wd, int hd, int wpld,
                                       uint[] datas, int ws, int hs, int wpls)
    {
        float scx = 16.0f * (float)ws / (float)wd;
        float scy = 16.0f * (float)hs / (float)hd;
        int wm2 = ws - 2;
        int hm2 = hs - 2;
        int wd4 = wd & ~3;

        /* The source column and weights depend only on the dest column,
         * not on the row: compute them once, with the same float expression. */
        int[] colXp = new int[wd];     // first src column
        int[] colXp2 = new int[wd];    // second src column
        int[] colXf = new int[wd];     // weight of the second column, in 1/16
        for (int j = 0; j < wd; j++) {
            int xpm = (int)(scx * (float)j);
            int xp = xpm >> 4;
            colXp[j] = xp;
            colXp2[j] = xp > wm2 ? xp : xp + 1;
            colXf[j] = xpm & 0x0f;
        }

        LeptParallel.ForRows(hd, wd, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            ref int rXp = ref MemoryMarshal.GetArrayDataReference(colXp);
            ref int rXp2 = ref MemoryMarshal.GetArrayDataReference(colXp2);
            ref int rXf = ref MemoryMarshal.GetArrayDataReference(colXf);
            for (int i = yStart; i < yEnd; i++) {
                int ypm = (int)(scy * (float)i);
                int yp = ypm >> 4;
                int yf = ypm & 0x0f;
                int wy0 = 16 - yf;
                ref uint dstLineRef = ref Unsafe.Add(ref datadRef, i * wpld);
                ref uint line0Ref = ref Unsafe.Add(ref datasRef, yp * wpls);
                ref uint line1Ref = ref Unsafe.Add(ref line0Ref, yp > hm2 ? 0 : wpls);

                int j = 0;
                for (; j < wd4; j += 4) {
                    uint b0 = ScaleGrayLICol(ref line0Ref, ref line1Ref, wy0, yf, ref rXp, ref rXp2, ref rXf, j);
                    uint b1 = ScaleGrayLICol(ref line0Ref, ref line1Ref, wy0, yf, ref rXp, ref rXp2, ref rXf, j + 1);
                    uint b2 = ScaleGrayLICol(ref line0Ref, ref line1Ref, wy0, yf, ref rXp, ref rXp2, ref rXf, j + 2);
                    uint b3 = ScaleGrayLICol(ref line0Ref, ref line1Ref, wy0, yf, ref rXp, ref rXp2, ref rXf, j + 3);
                    Unsafe.Add(ref dstLineRef, j >> 2) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                }
                if (j < wd) {
                    Unsafe.Add(ref dstLineRef, wd >> 2) = 0;
                    for (; j < wd; j++) {
                        uint b = ScaleGrayLICol(ref line0Ref, ref line1Ref, wy0, yf, ref rXp, ref rXp2, ref rXf, j);
                        SetDataByteUnchecked(ref dstLineRef, j, (int)b);
                    }
                }
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ScaleGrayLICol(ref uint line0Ref, ref uint line1Ref, int wy0, int yf,
                                       ref int rXp, ref int rXp2, ref int rXf, int j)
    {
        int xp = Unsafe.Add(ref rXp, j);
        int xp2 = Unsafe.Add(ref rXp2, j);
        int xf = Unsafe.Add(ref rXf, j);
        int wx0 = 16 - xf;
        int v00 = wx0 * wy0 * GetDataByteUnchecked(ref line0Ref, xp);
        int v10 = xf * wy0 * GetDataByteUnchecked(ref line0Ref, xp2);
        int v01 = wx0 * yf * GetDataByteUnchecked(ref line1Ref, xp);
        int v11 = xf * yf * GetDataByteUnchecked(ref line1Ref, xp2);
        return (uint)((v00 + v01 + v10 + v11 + 128) >> 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ScaleGrayLISample(ref uint line0Ref, ref uint line1Ref,
                                          int wm2, int wy0, int yf, float scx, int j)
    {
        int xpm = (int)(scx * (float)j);
        int xp = xpm >> 4;
        int xf = xpm & 0x0f;
        int wx0 = 16 - xf;
        int xp2 = xp > wm2 ? xp : xp + 1;
        int v00 = wx0 * wy0 * GetDataByteUnchecked(ref line0Ref, xp);
        int v10 = xf * wy0 * GetDataByteUnchecked(ref line0Ref, xp2);
        int v01 = wx0 * yf * GetDataByteUnchecked(ref line1Ref, xp);
        int v11 = xf * yf * GetDataByteUnchecked(ref line1Ref, xp2);
        return (uint)((v00 + v01 + v10 + v11 + 128) >> 8);
    }

    /*------------------------------------------------------------------*
     *                2x linear interpolated color scaling              *
     *------------------------------------------------------------------*/
    // leptonica: scaleColor2xLILow (scale1.c)
    private static void ScaleColor2xLILow(uint[] datad, int wpld, uint[] datas,
                                          int ws, int hs, int wpls)
    {
        int hsm = hs - 1;
        if (hsm > 0) {
            LeptParallel.ForRows(hsm, ws * 4, (yStart, yEnd) => {
                for (int i = yStart; i < yEnd; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls);
                    Span<uint> lined = datad.AsSpan(2 * i * wpld);
                    ScaleColor2xLILineLow(lined, wpld, lines, ws, wpls, 0);
                }
            });
        }
        if (hsm >= 0) {
            ReadOnlySpan<uint> lines = datas.AsSpan(hsm * wpls);
            Span<uint> lined = datad.AsSpan(2 * hsm * wpld);
            ScaleColor2xLILineLow(lined, wpld, lines, ws, wpls, 1);
        }
    }

    // leptonica: scaleColor2xLILineLow (scale1.c)
    /// <param name="lined">top destline, to be made from current src line</param>
    /// <param name="wpld">words per line of the dest</param>
    /// <param name="lines">current src line</param>
    /// <param name="ws">width of the src</param>
    /// <param name="wpls">words per line of the src</param>
    /// <param name="lastlineflag">1 if last src line; 0 otherwise</param>
    private static void ScaleColor2xLILineLow(Span<uint> lined, int wpld, ReadOnlySpan<uint> lines,
                                              int ws, int wpls, int lastlineflag)
    {
        int j, jd, wsm;
        uint rval1, rval2, rval3, rval4, gval1, gval2, gval3, gval4;
        uint bval1, bval2, bval3, bval4;
        uint pixels1, pixels2, pixels3, pixels4, pixel;

        wsm = ws - 1;

        if (lastlineflag == 0) {
            ReadOnlySpan<uint> linesp = lines.Slice(wpls);
            Span<uint> linedp = lined.Slice(wpld);
            pixels1 = lines[0];
            pixels3 = linesp[0];

            /* initialize with v(2) and v(4) */
            rval2 = pixels1 >> 24;
            gval2 = (pixels1 >> 16) & 0xff;
            bval2 = (pixels1 >> 8) & 0xff;
            rval4 = pixels3 >> 24;
            gval4 = (pixels3 >> 16) & 0xff;
            bval4 = (pixels3 >> 8) & 0xff;

            for (j = 0, jd = 0; j < wsm; j++, jd += 2) {
                /* shift in previous src values */
                rval1 = rval2;
                gval1 = gval2;
                bval1 = bval2;
                rval3 = rval4;
                gval3 = gval4;
                bval3 = bval4;
                /* get new src values */
                pixels2 = lines[j + 1];
                pixels4 = linesp[j + 1];
                rval2 = pixels2 >> 24;
                gval2 = (pixels2 >> 16) & 0xff;
                bval2 = (pixels2 >> 8) & 0xff;
                rval4 = pixels4 >> 24;
                gval4 = (pixels4 >> 16) & 0xff;
                bval4 = (pixels4 >> 8) & 0xff;
                /* save dest values */
                pixel = (rval1 << 24 | gval1 << 16 | bval1 << 8);
                lined[jd] = pixel;                               /* pix 1 */
                pixel = ((((rval1 + rval2) << 23) & 0xff000000) |
                         (((gval1 + gval2) << 15) & 0x00ff0000) |
                         (((bval1 + bval2) << 7) & 0x0000ff00));
                lined[jd + 1] = pixel;                           /* pix 2 */
                pixel = ((((rval1 + rval3) << 23) & 0xff000000) |
                         (((gval1 + gval3) << 15) & 0x00ff0000) |
                         (((bval1 + bval3) << 7) & 0x0000ff00));
                linedp[jd] = pixel;                              /* pix 3 */
                pixel = ((((rval1 + rval2 + rval3 + rval4) << 22) & 0xff000000) |
                         (((gval1 + gval2 + gval3 + gval4) << 14) & 0x00ff0000) |
                         (((bval1 + bval2 + bval3 + bval4) << 6) & 0x0000ff00));
                linedp[jd + 1] = pixel;                          /* pix 4 */
            }
            /* last src pixel on line */
            rval1 = rval2;
            gval1 = gval2;
            bval1 = bval2;
            rval3 = rval4;
            gval3 = gval4;
            bval3 = bval4;
            pixel = (rval1 << 24 | gval1 << 16 | bval1 << 8);
            lined[2 * wsm] = pixel;                        /* pix 1 */
            lined[2 * wsm + 1] = pixel;                    /* pix 2 */
            pixel = ((((rval1 + rval3) << 23) & 0xff000000) |
                     (((gval1 + gval3) << 15) & 0x00ff0000) |
                     (((bval1 + bval3) << 7) & 0x0000ff00));
            linedp[2 * wsm] = pixel;                       /* pix 3 */
            linedp[2 * wsm + 1] = pixel;                   /* pix 4 */
        } else {   /* last row of src pixels: lastlineflag == 1 */
            Span<uint> linedp = lined.Slice(wpld);
            pixels2 = lines[0];
            rval2 = pixels2 >> 24;
            gval2 = (pixels2 >> 16) & 0xff;
            bval2 = (pixels2 >> 8) & 0xff;
            for (j = 0, jd = 0; j < wsm; j++, jd += 2) {
                rval1 = rval2;
                gval1 = gval2;
                bval1 = bval2;
                pixels2 = lines[j + 1];
                rval2 = pixels2 >> 24;
                gval2 = (pixels2 >> 16) & 0xff;
                bval2 = (pixels2 >> 8) & 0xff;
                pixel = (rval1 << 24 | gval1 << 16 | bval1 << 8);
                lined[jd] = pixel;                            /* pix 1 */
                linedp[jd] = pixel;                           /* pix 2 */
                pixel = ((((rval1 + rval2) << 23) & 0xff000000) |
                         (((gval1 + gval2) << 15) & 0x00ff0000) |
                         (((bval1 + bval2) << 7) & 0x0000ff00));
                lined[jd + 1] = pixel;                        /* pix 3 */
                linedp[jd + 1] = pixel;                       /* pix 4 */
            }
            rval1 = rval2;
            gval1 = gval2;
            bval1 = bval2;
            pixel = (rval1 << 24 | gval1 << 16 | bval1 << 8);
            lined[2 * wsm] = pixel;                           /* pix 1 */
            lined[2 * wsm + 1] = pixel;                       /* pix 2 */
            linedp[2 * wsm] = pixel;                          /* pix 3 */
            linedp[2 * wsm + 1] = pixel;                      /* pix 4 */
        }
    }

    /*------------------------------------------------------------------*
     *                2x linear interpolated gray scaling               *
     *------------------------------------------------------------------*/
    // leptonica: scaleGray2xLILow (scale1.c)
    /// <remarks>
    /// (1) This is a special case of 2x expansion by linear
    ///     interpolation.  Each src pixel contains 4 dest pixels.
    ///     The 4 dest pixels in src pixel 1 are numbered at
    ///     their UL corners.  The 4 dest pixels in src pixel 1
    ///     are related to that src pixel and its 3 neighboring
    ///     src pixels as follows:
    ///
    ///        1-----2-----|-----|-----|
    ///        |     |     |     |     |
    ///        |     |     |     |     |
    /// src 1 --&gt;  3-----4-----|     |     |  &lt;-- src 2
    ///        |     |     |     |     |
    ///        |     |     |     |     |
    ///        |-----|-----|-----|-----|
    ///        |     |     |     |     |
    ///        |     |     |     |     |
    /// src 3 --&gt;  |     |     |     |     |  &lt;-- src 4
    ///        |     |     |     |     |
    ///        |     |     |     |     |
    ///        |-----|-----|-----|-----|
    ///
    ///      dest      src
    ///      ----      ---
    ///      dp1    =  sp1
    ///      dp2    =  (sp1 + sp2) / 2
    ///      dp3    =  (sp1 + sp3) / 2
    ///      dp4    =  (sp1 + sp2 + sp3 + sp4) / 4
    ///
    /// (2) We iterate over the src pixels, and unroll the calculation
    ///     for each set of 4 dest pixels corresponding to that src
    ///     pixel, caching pixels for the next src pixel whenever possible.
    /// </remarks>
    private static void ScaleGray2xLILow(uint[] datad, int wpld, uint[] datas,
                                         int ws, int hs, int wpls)
    {
        int hsm = hs - 1;
        if (hsm > 0) {
            LeptParallel.ForRows(hsm, ws * 4, (yStart, yEnd) => {
                for (int i = yStart; i < yEnd; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls);
                    Span<uint> lined = datad.AsSpan(2 * i * wpld);
                    ScaleGray2xLILineLow(lined, wpld, lines, ws, wpls, 0);
                }
            });
        }
        if (hsm >= 0) {
            ReadOnlySpan<uint> lines = datas.AsSpan(hsm * wpls);
            Span<uint> lined = datad.AsSpan(2 * hsm * wpld);
            ScaleGray2xLILineLow(lined, wpld, lines, ws, wpls, 1);
        }
    }

    /// <summary>
    /// 2x linear interpolation of 16 src pixels (4 words, starting at word w) of two
    /// adjacent src lines into 32 dest pixels (8 words) of two dest lines.
    /// The pixel after the group must exist in both src lines.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ScaleGray2xLIVec(ref uint rl, ref uint rlp, ref uint rd, ref uint rdp, int w)
    {
        // reverses the bytes of every word: memory order <-> pixel order
        Vector128<byte> rev = Vector128.Create((byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12);
        Vector128<byte> shift1 = Vector128.Create((byte)1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 0x80);

        Vector128<byte> a = Vector128.Shuffle(Vector128.LoadUnsafe(ref rl, (nuint)w).AsByte(), rev);
        Vector128<byte> b = Vector128.Shuffle(Vector128.LoadUnsafe(ref rlp, (nuint)w).AsByte(), rev);
        byte na = (byte)(Unsafe.Add(ref rl, w + 4) >> 24);
        byte nb = (byte)(Unsafe.Add(ref rlp, w + 4) >> 24);
        Vector128<byte> a1 = Vector128.Shuffle(a, shift1).WithElement(15, na);   // right neighbors
        Vector128<byte> b1 = Vector128.Shuffle(b, shift1).WithElement(15, nb);

        // (x + y) >> 1 per byte
        Vector128<byte> one = Vector128.Create((byte)0x7f);
        Vector128<byte> h0 = (a & a1) + (Vector128.ShiftRightLogical(a ^ a1, 1) & one);
        Vector128<byte> v = (a & b) + (Vector128.ShiftRightLogical(a ^ b, 1) & one);

        // (a + a1 + b + b1) >> 2
        Vector128<ushort> qlo = Vector128.ShiftRightLogical(
            Vector128.WidenLower(a) + Vector128.WidenLower(a1) + Vector128.WidenLower(b) + Vector128.WidenLower(b1), 2);
        Vector128<ushort> qhi = Vector128.ShiftRightLogical(
            Vector128.WidenUpper(a) + Vector128.WidenUpper(a1) + Vector128.WidenUpper(b) + Vector128.WidenUpper(b1), 2);
        Vector128<byte> q = Vector128.Narrow(qlo, qhi);

        // interleave (src, interpolated) into dest pixel pairs, then back to word byte order
        int d = w * 2;
        Vector128.Shuffle((Vector128.WidenLower(a) | Vector128.ShiftLeft(Vector128.WidenLower(h0), 8)).AsByte(), rev)
            .AsUInt32().StoreUnsafe(ref rd, (nuint)d);
        Vector128.Shuffle((Vector128.WidenUpper(a) | Vector128.ShiftLeft(Vector128.WidenUpper(h0), 8)).AsByte(), rev)
            .AsUInt32().StoreUnsafe(ref rd, (nuint)(d + 4));
        Vector128.Shuffle((Vector128.WidenLower(v) | Vector128.ShiftLeft(Vector128.WidenLower(q), 8)).AsByte(), rev)
            .AsUInt32().StoreUnsafe(ref rdp, (nuint)d);
        Vector128.Shuffle((Vector128.WidenUpper(v) | Vector128.ShiftLeft(Vector128.WidenUpper(q), 8)).AsByte(), rev)
            .AsUInt32().StoreUnsafe(ref rdp, (nuint)(d + 4));
    }

    // leptonica: scaleGray2xLILineLow (scale1.c)
    /// <param name="lined">top destline, to be made from current src line</param>
    /// <param name="wpld">words per line of the dest</param>
    /// <param name="lines">current src line</param>
    /// <param name="ws">width of the src</param>
    /// <param name="wpls">words per line of the src</param>
    /// <param name="lastlineflag">1 if last src line; 0 otherwise</param>
    private static void ScaleGray2xLILineLow(Span<uint> lined, int wpld, ReadOnlySpan<uint> lines,
                                             int ws, int wpls, int lastlineflag)
    {
        int j, jd, wsm, w;
        uint sval1, sval2, sval3, sval4;
        uint words, wordsp, wordd, worddp;

        wsm = ws - 1;

        if (lastlineflag == 0) {
            ReadOnlySpan<uint> linesp = lines.Slice(wpls);
            Span<uint> linedp = lined.Slice(wpld);

            /* Unroll the loop 4x and work on full words */
            words = lines[0];
            wordsp = linesp[0];
            sval2 = (words >> 24) & 0xff;
            sval4 = (wordsp >> 24) & 0xff;
            j = 0;
            jd = 0;
            w = 0;

            /* Groups of 16 src pixels with 128-bit vectors; every pixel of a
             * group has a right neighbor inside the line. */
            int nvec = RgbSimd.IsSupported ? wsm / 16 : 0;
            if (nvec > 0) {
                ref uint rl = ref MemoryMarshal.GetReference(lines);
                ref uint rlp = ref MemoryMarshal.GetReference(linesp);
                ref uint rd = ref MemoryMarshal.GetReference(lined);
                ref uint rdp = ref MemoryMarshal.GetReference(linedp);
                for (int g = 0; g < nvec; g++)
                    ScaleGray2xLIVec(ref rl, ref rlp, ref rd, ref rdp, g * 4);
                j = nvec * 16;
                jd = j * 2;
                w = nvec * 4;
                words = lines[w];
                wordsp = linesp[w];
                sval2 = (words >> 24) & 0xff;
                sval4 = (wordsp >> 24) & 0xff;
            }
            for (; j + 3 < wsm; j += 4, jd += 8, w++) {
                /* At the top of the loop,
                 * words == lines[w], wordsp == linesp[w]
                 * and the top bytes of those have been loaded into
                 * sval2 and sval4. */
                sval1 = sval2;
                sval2 = (words >> 16) & 0xff;
                sval3 = sval4;
                sval4 = (wordsp >> 16) & 0xff;
                wordd = (sval1 << 24) | (((sval1 + sval2) >> 1) << 16);
                worddp = (((sval1 + sval3) >> 1) << 24) |
                    (((sval1 + sval2 + sval3 + sval4) >> 2) << 16);

                sval1 = sval2;
                sval2 = (words >> 8) & 0xff;
                sval3 = sval4;
                sval4 = (wordsp >> 8) & 0xff;
                wordd |= (sval1 << 8) | ((sval1 + sval2) >> 1);
                worddp |= (((sval1 + sval3) >> 1) << 8) |
                    ((sval1 + sval2 + sval3 + sval4) >> 2);
                lined[w * 2] = wordd;
                linedp[w * 2] = worddp;

                sval1 = sval2;
                sval2 = words & 0xff;
                sval3 = sval4;
                sval4 = wordsp & 0xff;
                wordd = (sval1 << 24) |                              /* pix 1 */
                    (((sval1 + sval2) >> 1) << 16);                  /* pix 2 */
                worddp = (((sval1 + sval3) >> 1) << 24) |            /* pix 3 */
                    (((sval1 + sval2 + sval3 + sval4) >> 2) << 16);  /* pix 4 */

                /* Load the next word as we need its first byte */
                words = lines[w + 1];
                wordsp = linesp[w + 1];
                sval1 = sval2;
                sval2 = (words >> 24) & 0xff;
                sval3 = sval4;
                sval4 = (wordsp >> 24) & 0xff;
                wordd |= (sval1 << 8) |                              /* pix 1 */
                    ((sval1 + sval2) >> 1);                          /* pix 2 */
                worddp |= (((sval1 + sval3) >> 1) << 8) |            /* pix 3 */
                    ((sval1 + sval2 + sval3 + sval4) >> 2);          /* pix 4 */
                lined[w * 2 + 1] = wordd;
                linedp[w * 2 + 1] = worddp;
            }

            /* Finish up the last word */
            for (; j < wsm; j++, jd += 2) {
                sval1 = sval2;
                sval3 = sval4;
                sval2 = (uint)GetDataByte(lines, j + 1);
                sval4 = (uint)GetDataByte(linesp, j + 1);
                SetDataByte(lined, jd, (int)sval1);                     /* pix 1 */
                SetDataByte(lined, jd + 1, (int)((sval1 + sval2) / 2));   /* pix 2 */
                SetDataByte(linedp, jd, (int)((sval1 + sval3) / 2));      /* pix 3 */
                SetDataByte(linedp, jd + 1,
                            (int)((sval1 + sval2 + sval3 + sval4) / 4));  /* pix 4 */
            }
            sval1 = sval2;
            sval3 = sval4;
            SetDataByte(lined, 2 * wsm, (int)sval1);                     /* pix 1 */
            SetDataByte(lined, 2 * wsm + 1, (int)sval1);                 /* pix 2 */
            SetDataByte(linedp, 2 * wsm, (int)((sval1 + sval3) / 2));      /* pix 3 */
            SetDataByte(linedp, 2 * wsm + 1, (int)((sval1 + sval3) / 2));  /* pix 4 */
        } else {  /* last row of src pixels: lastlineflag == 1 */
            Span<uint> linedp = lined.Slice(wpld);
            sval2 = (uint)GetDataByte(lines, 0);
            for (j = 0, jd = 0; j < wsm; j++, jd += 2) {
                sval1 = sval2;
                sval2 = (uint)GetDataByte(lines, j + 1);
                SetDataByte(lined, jd, (int)sval1);                       /* pix 1 */
                SetDataByte(linedp, jd, (int)sval1);                      /* pix 3 */
                SetDataByte(lined, jd + 1, (int)((sval1 + sval2) / 2));     /* pix 2 */
                SetDataByte(linedp, jd + 1, (int)((sval1 + sval2) / 2));    /* pix 4 */
            }
            sval1 = sval2;
            SetDataByte(lined, 2 * wsm, (int)sval1);                     /* pix 1 */
            SetDataByte(lined, 2 * wsm + 1, (int)sval1);                 /* pix 2 */
            SetDataByte(linedp, 2 * wsm, (int)sval1);                    /* pix 3 */
            SetDataByte(linedp, 2 * wsm + 1, (int)sval1);                /* pix 4 */
        }
    }

    /*------------------------------------------------------------------*
     *               4x linear interpolated gray scaling                *
     *------------------------------------------------------------------*/
    // leptonica: scaleGray4xLILow (scale1.c)
    /// <remarks>
    /// (1) This is a special case of 4x expansion by linear
    ///     interpolation.  Each src pixel contains 16 dest pixels.
    ///     The 16 dest pixels in src pixel 1 are numbered at
    ///     their UL corners.  The 16 dest pixels in src pixel 1
    ///     are related to that src pixel and its 3 neighboring
    ///     src pixels as follows:
    ///
    ///        1---2---3---4---|---|---|---|---|
    ///        |   |   |   |   |   |   |   |   |
    ///        5---6---7---8---|---|---|---|---|
    ///        |   |   |   |   |   |   |   |   |
    /// src 1 --&gt;  9---a---b---c---|---|---|---|---|  &lt;-- src 2
    ///        |   |   |   |   |   |   |   |   |
    ///        d---e---f---g---|---|---|---|---|
    ///        |   |   |   |   |   |   |   |   |
    ///        |===|===|===|===|===|===|===|===|
    ///        |   |   |   |   |   |   |   |   |
    ///        |---|---|---|---|---|---|---|---|
    ///        |   |   |   |   |   |   |   |   |
    /// src 3 --&gt;  |---|---|---|---|---|---|---|---|  &lt;-- src 4
    ///        |   |   |   |   |   |   |   |   |
    ///        |---|---|---|---|---|---|---|---|
    ///        |   |   |   |   |   |   |   |   |
    ///        |---|---|---|---|---|---|---|---|
    ///
    ///      dest      src
    ///      ----      ---
    ///      dp1    =  sp1
    ///      dp2    =  (3 * sp1 + sp2) / 4
    ///      dp3    =  (sp1 + sp2) / 2
    ///      dp4    =  (sp1 + 3 * sp2) / 4
    ///      dp5    =  (3 * sp1 + sp3) / 4
    ///      dp6    =  (9 * sp1 + 3 * sp2 + 3 * sp3 + sp4) / 16
    ///      dp7    =  (3 * sp1 + 3 * sp2 + sp3 + sp4) / 8
    ///      dp8    =  (3 * sp1 + 9 * sp2 + 1 * sp3 + 3 * sp4) / 16
    ///      dp9    =  (sp1 + sp3) / 2
    ///      dp10   =  (3 * sp1 + sp2 + 3 * sp3 + sp4) / 8
    ///      dp11   =  (sp1 + sp2 + sp3 + sp4) / 4
    ///      dp12   =  (sp1 + 3 * sp2 + sp3 + 3 * sp4) / 8
    ///      dp13   =  (sp1 + 3 * sp3) / 4
    ///      dp14   =  (3 * sp1 + sp2 + 9 * sp3 + 3 * sp4) / 16
    ///      dp15   =  (sp1 + sp2 + 3 * sp3 + 3 * sp4) / 8
    ///      dp16   =  (sp1 + 3 * sp2 + 3 * sp3 + 9 * sp4) / 16
    ///
    /// (2) We iterate over the src pixels, and unroll the calculation
    ///     for each set of 16 dest pixels corresponding to that src
    ///     pixel, caching pixels for the next src pixel whenever possible.
    /// </remarks>
    private static void ScaleGray4xLILow(Span<uint> datad, int wpld, ReadOnlySpan<uint> datas,
                                         int ws, int hs, int wpls)
    {
        int i, hsm;

        hsm = hs - 1;

        /* We're taking 2 src and 4 dest lines at a time,
         * and for each src line, we're computing 4 dest lines.
         * Call these 4 dest lines:  destline1 - destline4.
         * The first src line is used for destline 1.
         * Two src lines are used for all other dest lines,
         * except for the last 4 dest lines, which are computed
         * using only the last src line. */

        /* iterate over all but the last src line */
        for (i = 0; i < hsm; i++) {
            ReadOnlySpan<uint> lines = datas.Slice(i * wpls);
            Span<uint> lined = datad.Slice(4 * i * wpld);
            ScaleGray4xLILineLow(lined, wpld, lines, ws, wpls, 0);
        }

        /* last src line */
        {
            ReadOnlySpan<uint> lines = datas.Slice(hsm * wpls);
            Span<uint> lined = datad.Slice(4 * hsm * wpld);
            ScaleGray4xLILineLow(lined, wpld, lines, ws, wpls, 1);
        }
    }

    // leptonica: scaleGray4xLILineLow (scale1.c)
    /// <param name="lined">top destline, to be made from current src line</param>
    /// <param name="wpld">words per line of the dest</param>
    /// <param name="lines">current src line</param>
    /// <param name="ws">width of the src</param>
    /// <param name="wpls">words per line of the src</param>
    /// <param name="lastlineflag">1 if last src line; 0 otherwise</param>
    private static void ScaleGray4xLILineLow(Span<uint> lined, int wpld, ReadOnlySpan<uint> lines,
                                             int ws, int wpls, int lastlineflag)
    {
        int j, jd, wsm, wsm4;
        int s1, s2, s3, s4, s1t, s2t, s3t, s4t;

        wsm = ws - 1;
        wsm4 = 4 * wsm;

        if (lastlineflag == 0) {
            ReadOnlySpan<uint> linesp = lines.Slice(wpls);
            Span<uint> linedp1 = lined.Slice(wpld);
            Span<uint> linedp2 = lined.Slice(2 * wpld);
            Span<uint> linedp3 = lined.Slice(3 * wpld);
            s2 = GetDataByte(lines, 0);
            s4 = GetDataByte(linesp, 0);
            for (j = 0, jd = 0; j < wsm; j++, jd += 4) {
                s1 = s2;
                s3 = s4;
                s2 = GetDataByte(lines, j + 1);
                s4 = GetDataByte(linesp, j + 1);
                s1t = 3 * s1;
                s2t = 3 * s2;
                s3t = 3 * s3;
                s4t = 3 * s4;
                SetDataByte(lined, jd, s1);                             /* d1 */
                SetDataByte(lined, jd + 1, (s1t + s2) / 4);             /* d2 */
                SetDataByte(lined, jd + 2, (s1 + s2) / 2);              /* d3 */
                SetDataByte(lined, jd + 3, (s1 + s2t) / 4);             /* d4 */
                SetDataByte(linedp1, jd, (s1t + s3) / 4);                /* d5 */
                SetDataByte(linedp1, jd + 1, (9*s1 + s2t + s3t + s4) / 16); /*d6*/
                SetDataByte(linedp1, jd + 2, (s1t + s2t + s3 + s4) / 8); /* d7 */
                SetDataByte(linedp1, jd + 3, (s1t + 9*s2 + s3 + s4t) / 16);/*d8*/
                SetDataByte(linedp2, jd, (s1 + s3) / 2);                /* d9 */
                SetDataByte(linedp2, jd + 1, (s1t + s2 + s3t + s4) / 8);/* d10 */
                SetDataByte(linedp2, jd + 2, (s1 + s2 + s3 + s4) / 4);  /* d11 */
                SetDataByte(linedp2, jd + 3, (s1 + s2t + s3 + s4t) / 8);/* d12 */
                SetDataByte(linedp3, jd, (s1 + s3t) / 4);               /* d13 */
                SetDataByte(linedp3, jd + 1, (s1t + s2 + 9*s3 + s4t) / 16);/*d14*/
                SetDataByte(linedp3, jd + 2, (s1 + s2 + s3t + s4t) / 8); /* d15 */
                SetDataByte(linedp3, jd + 3, (s1 + s2t + s3t + 9*s4) / 16);/*d16*/
            }
            s1 = s2;
            s3 = s4;
            s1t = 3 * s1;
            s3t = 3 * s3;
            SetDataByte(lined, wsm4, s1);                               /* d1 */
            SetDataByte(lined, wsm4 + 1, s1);                           /* d2 */
            SetDataByte(lined, wsm4 + 2, s1);                           /* d3 */
            SetDataByte(lined, wsm4 + 3, s1);                           /* d4 */
            SetDataByte(linedp1, wsm4, (s1t + s3) / 4);                 /* d5 */
            SetDataByte(linedp1, wsm4 + 1, (s1t + s3) / 4);             /* d6 */
            SetDataByte(linedp1, wsm4 + 2, (s1t + s3) / 4);             /* d7 */
            SetDataByte(linedp1, wsm4 + 3, (s1t + s3) / 4);             /* d8 */
            SetDataByte(linedp2, wsm4, (s1 + s3) / 2);                  /* d9 */
            SetDataByte(linedp2, wsm4 + 1, (s1 + s3) / 2);              /* d10 */
            SetDataByte(linedp2, wsm4 + 2, (s1 + s3) / 2);              /* d11 */
            SetDataByte(linedp2, wsm4 + 3, (s1 + s3) / 2);              /* d12 */
            SetDataByte(linedp3, wsm4, (s1 + s3t) / 4);                 /* d13 */
            SetDataByte(linedp3, wsm4 + 1, (s1 + s3t) / 4);             /* d14 */
            SetDataByte(linedp3, wsm4 + 2, (s1 + s3t) / 4);             /* d15 */
            SetDataByte(linedp3, wsm4 + 3, (s1 + s3t) / 4);             /* d16 */
        } else {   /* last row of src pixels: lastlineflag == 1 */
            Span<uint> linedp1 = lined.Slice(wpld);
            Span<uint> linedp2 = lined.Slice(2 * wpld);
            Span<uint> linedp3 = lined.Slice(3 * wpld);
            s2 = GetDataByte(lines, 0);
            for (j = 0, jd = 0; j < wsm; j++, jd += 4) {
                s1 = s2;
                s2 = GetDataByte(lines, j + 1);
                s1t = 3 * s1;
                s2t = 3 * s2;
                SetDataByte(lined, jd, s1);                            /* d1 */
                SetDataByte(lined, jd + 1, (s1t + s2) / 4 );           /* d2 */
                SetDataByte(lined, jd + 2, (s1 + s2) / 2 );            /* d3 */
                SetDataByte(lined, jd + 3, (s1 + s2t) / 4 );           /* d4 */
                SetDataByte(linedp1, jd, s1);                          /* d5 */
                SetDataByte(linedp1, jd + 1, (s1t + s2) / 4 );         /* d6 */
                SetDataByte(linedp1, jd + 2, (s1 + s2) / 2 );          /* d7 */
                SetDataByte(linedp1, jd + 3, (s1 + s2t) / 4 );         /* d8 */
                SetDataByte(linedp2, jd, s1);                          /* d9 */
                SetDataByte(linedp2, jd + 1, (s1t + s2) / 4 );         /* d10 */
                SetDataByte(linedp2, jd + 2, (s1 + s2) / 2 );          /* d11 */
                SetDataByte(linedp2, jd + 3, (s1 + s2t) / 4 );         /* d12 */
                SetDataByte(linedp3, jd, s1);                          /* d13 */
                SetDataByte(linedp3, jd + 1, (s1t + s2) / 4 );         /* d14 */
                SetDataByte(linedp3, jd + 2, (s1 + s2) / 2 );          /* d15 */
                SetDataByte(linedp3, jd + 3, (s1 + s2t) / 4 );         /* d16 */
            }
            s1 = s2;
            SetDataByte(lined, wsm4, s1);                              /* d1 */
            SetDataByte(lined, wsm4 + 1, s1);                          /* d2 */
            SetDataByte(lined, wsm4 + 2, s1);                          /* d3 */
            SetDataByte(lined, wsm4 + 3, s1);                          /* d4 */
            SetDataByte(linedp1, wsm4, s1);                            /* d5 */
            SetDataByte(linedp1, wsm4 + 1, s1);                        /* d6 */
            SetDataByte(linedp1, wsm4 + 2, s1);                        /* d7 */
            SetDataByte(linedp1, wsm4 + 3, s1);                        /* d8 */
            SetDataByte(linedp2, wsm4, s1);                            /* d9 */
            SetDataByte(linedp2, wsm4 + 1, s1);                        /* d10 */
            SetDataByte(linedp2, wsm4 + 2, s1);                        /* d11 */
            SetDataByte(linedp2, wsm4 + 3, s1);                        /* d12 */
            SetDataByte(linedp3, wsm4, s1);                            /* d13 */
            SetDataByte(linedp3, wsm4 + 1, s1);                        /* d14 */
            SetDataByte(linedp3, wsm4 + 2, s1);                        /* d15 */
            SetDataByte(linedp3, wsm4 + 3, s1);                        /* d16 */
        }
    }

    /*------------------------------------------------------------------*
     *       Grayscale and color scaling by closest pixel sampling      *
     *------------------------------------------------------------------*/
    // leptonica: scaleBySamplingLow (scale1.c)
    /// <remarks>
    /// (1) The dest must be cleared prior to this operation,
    ///     and we clear it here in the low-level code.
    /// (2) We reuse dest pixels and dest pixel rows whenever
    ///     possible.  This speeds the upscaling; downscaling
    ///     is done by strict subsampling and is unaffected.
    /// (3) Because we are sampling and not interpolating, this
    ///     routine works directly, without conversion to full
    ///     RGB color, for 2, 4 or 8 bpp palette color images.
    /// </remarks>
    private static void ScaleBySamplingLow(Span<uint> datad, int wd, int hd, int wpld,
                                           ReadOnlySpan<uint> datas, int ws, int hs, int d, int wpls,
                                           float shiftx, float shifty)
    {
        const string func = "scaleBySamplingLow";
        int i, j;
        int xs, prevxs, sval;
        int[] srow, scol;
        uint csval;
        int prevlines;
        float wratio, hratio;

        if (d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg(func, "pixel depth not supported");

        /* Clear dest; not needed at 32 bpp, where every dest word is written */
        if (d != 32)
            datad.Slice(0, hd * wpld).Clear();

        /* the source row corresponding to dest row i ==> srow[i]
         * the source col corresponding to dest col j ==> scol[j]  */
        srow = new int[hd];
        scol = new int[wd];

        wratio = (float)ws / (float)wd;
        hratio = (float)hs / (float)hd;
        for (i = 0; i < hd; i++)
            srow[i] = Math.Min((int)(hratio * i + shifty), hs - 1);
        for (j = 0; j < wd; j++)
            scol[j] = Math.Min((int)(wratio * j + shiftx), ws - 1);

        prevlines = -1;
        for (i = 0; i < hd; i++) {
            int lines = srow[i] * wpls;
            int lined = i * wpld;
            if (lines != prevlines) {  /* make dest from new source row */
                ReadOnlySpan<uint> lineS = datas.Slice(lines);
                Span<uint> lineD = datad.Slice(lined);
                prevxs = -1;
                sval = 0;
                csval = 0;
                if (d == 2) {
                    for (j = 0; j < wd; j++) {
                        xs = scol[j];
                        if (xs != prevxs) {  /* get dest pix from source col */
                            sval = GetDataDibit(lineS, xs);
                            SetDataDibit(lineD, j, sval);
                            prevxs = xs;
                        } else {  /* copy prev dest pix */
                            SetDataDibit(lineD, j, sval);
                        }
                    }
                } else if (d == 4) {
                    for (j = 0; j < wd; j++) {
                        xs = scol[j];
                        if (xs != prevxs) {  /* get dest pix from source col */
                            sval = GetDataQbit(lineS, xs);
                            SetDataQbit(lineD, j, sval);
                            prevxs = xs;
                        } else {  /* copy prev dest pix */
                            SetDataQbit(lineD, j, sval);
                        }
                    }
                } else if (d == 8) {
                    for (j = 0; j < wd; j++) {
                        xs = scol[j];
                        if (xs != prevxs) {  /* get dest pix from source col */
                            sval = GetDataByte(lineS, xs);
                            SetDataByte(lineD, j, sval);
                            prevxs = xs;
                        } else {  /* copy prev dest pix */
                            SetDataByte(lineD, j, sval);
                        }
                    }
                } else if (d == 16) {
                    for (j = 0; j < wd; j++) {
                        xs = scol[j];
                        if (xs != prevxs) {  /* get dest pix from source col */
                            sval = GetDataTwoBytes(lineS, xs);
                            SetDataTwoBytes(lineD, j, sval);
                            prevxs = xs;
                        } else {  /* copy prev dest pix */
                            SetDataTwoBytes(lineD, j, sval);
                        }
                    }
                } else {  /* d == 32 */
                    if (wratio >= 1.0f) {
                        /* During reduction, sampled columns are strictly
                         * increasing; skip the duplicate-column branch.
                         * All scol[j] are < ws <= the line length. */
                        ref uint rs = ref MemoryMarshal.GetReference(lineS);
                        ref uint rdd = ref MemoryMarshal.GetReference(lineD);
                        ref int rc = ref MemoryMarshal.GetArrayDataReference(scol);
                        for (j = 0; j + 3 < wd; j += 4) {
                            uint s0 = Unsafe.Add(ref rs, Unsafe.Add(ref rc, j));
                            uint s1 = Unsafe.Add(ref rs, Unsafe.Add(ref rc, j + 1));
                            uint s2 = Unsafe.Add(ref rs, Unsafe.Add(ref rc, j + 2));
                            uint s3 = Unsafe.Add(ref rs, Unsafe.Add(ref rc, j + 3));
                            Unsafe.Add(ref rdd, j) = s0;
                            Unsafe.Add(ref rdd, j + 1) = s1;
                            Unsafe.Add(ref rdd, j + 2) = s2;
                            Unsafe.Add(ref rdd, j + 3) = s3;
                        }
                        for (; j < wd; j++)
                            Unsafe.Add(ref rdd, j) = Unsafe.Add(ref rs, Unsafe.Add(ref rc, j));
                    } else {
                        for (j = 0; j < wd; j++) {
                            xs = scol[j];
                            if (xs != prevxs) {  /* get dest pix from source col */
                                csval = lineS[xs];
                                lineD[j] = csval;
                                prevxs = xs;
                            } else {  /* copy prev dest pix */
                                lineD[j] = csval;
                            }
                        }
                    }
                }
            } else {  /* lines == prevlines; copy prev dest row */
                int prevlined = lined - wpld;
                datad.Slice(prevlined, wpld).CopyTo(datad.Slice(lined, wpld));
            }
            prevlines = lines;
        }
    }

    /*------------------------------------------------------------------*
     *    Color and grayscale downsampling with (antialias) smoothing   *
     *------------------------------------------------------------------*/
    // leptonica: scaleSmoothLow (scale1.c)
    /// <remarks>
    /// (1) This function is called on 8 or 32 bpp src and dest images.
    /// (2) size is the full width of the lowpass smoothing filter.
    ///     It is correlated with the reduction ratio, being the
    ///     nearest integer such that size is approximately equal to hs / hd.
    /// </remarks>
    private static void ScaleSmoothLow(uint[] datad, int wd, int hd, int wpld,
                                       uint[] datas, int ws, int hs, int d, int wpls,
                                       int size)
    {
        using var srowBuf = new TempBuffer<int>(hd);
        using var scolBuf = new TempBuffer<int>(wd);
        int[] srow = srowBuf.Array;
        int[] scol = scolBuf.Array;

        float norm = 1.0f / (float)(size * size);
        float wratio = (float)ws / (float)wd;
        float hratio = (float)hs / (float)hd;
        for (int i = 0; i < hd; i++)
            srow[i] = Math.Min((int)(hratio * i), hs - size);
        for (int j = 0; j < wd; j++)
            scol[j] = Math.Min((int)(wratio * j), ws - size);

        int workPerRow = wd * size * size;
        if (d == 8) {
            LeptParallel.ForRows(hd, workPerRow, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
                for (int i = yStart; i < yEnd; i++) {
                    int srcBaseRow = srow[i] * wpls;
                    ref uint dstLineRef = ref Unsafe.Add(ref datadRef, i * wpld);
                    if ((wd & 3) != 0)
                        Unsafe.Add(ref dstLineRef, wd >> 2) = 0;
                    for (int j = 0; j < wd; j++) {
                        int xstart = scol[j];
                        int val = 0;
                        for (int m = 0; m < size; m++) {
                            ref uint lineRef = ref Unsafe.Add(ref datasRef, srcBaseRow + m * wpls);
                            for (int n = 0; n < size; n++)
                                val += GetDataByteUnchecked(ref lineRef, xstart + n);
                        }
                        SetDataByteUnchecked(ref dstLineRef, j, (int)((float)val * norm));
                    }
                }
            });
        } else {  /* d == 32 */
            LeptParallel.ForRows(hd, workPerRow, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
                for (int i = yStart; i < yEnd; i++) {
                    int srcBaseRow = srow[i] * wpls;
                    int dstRow = i * wpld;
                    for (int j = 0; j < wd; j++) {
                        int xstart = scol[j];
                        int rval = 0, gval = 0, bval = 0;
                        for (int m = 0; m < size; m++) {
                            ref uint ppixelRef = ref Unsafe.Add(ref datasRef, srcBaseRow + m * wpls + xstart);
                            for (int n = 0; n < size; n++) {
                                uint pixel = Unsafe.Add(ref ppixelRef, n);
                                rval += (int)(pixel >> 24);
                                gval += (int)((pixel >> 16) & 0xffu);
                                bval += (int)((pixel >> 8) & 0xffu);
                            }
                        }
                        rval = (int)((float)rval * norm);
                        gval = (int)((float)gval * norm);
                        bval = (int)((float)bval * norm);
                        Unsafe.Add(ref datadRef, dstRow + j) = (uint)((rval << 24) | (gval << 16) | (bval << 8));
                    }
                }
            });
        }
    }

    // leptonica: scaleRGBToGray2Low (scale1.c)
    private static void ScaleRGBToGray2Low(uint[] datad, int wd, int hd, int wpld,
                                           uint[] datas, int wpls,
                                           float rwt, float gwt, float bwt)
    {
        rwt = (float)(rwt * 0.25);
        gwt = (float)(gwt * 0.25);
        bwt = (float)(bwt * 0.25);
        LeptParallel.ForRows(hd, wd * 4, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                int sRow = 2 * i * wpls;
                ref uint dstLineRef = ref Unsafe.Add(ref datadRef, i * wpld);
                if ((wd & 3) != 0)
                    Unsafe.Add(ref dstLineRef, wd >> 2) = 0;
                for (int j = 0; j < wd; j++) {
                    int p = sRow + 2 * j;
                    uint p0 = Unsafe.Add(ref datasRef, p);
                    uint p1 = Unsafe.Add(ref datasRef, p + 1);
                    uint p2 = Unsafe.Add(ref datasRef, p + wpls);
                    uint p3 = Unsafe.Add(ref datasRef, p + wpls + 1);
                    int rval = (int)((p0 >> 24) + (p1 >> 24) + (p2 >> 24) + (p3 >> 24));
                    int gval = (int)(((p0 >> 16) & 0xffu) + ((p1 >> 16) & 0xffu) + ((p2 >> 16) & 0xffu) + ((p3 >> 16) & 0xffu));
                    int bval = (int)(((p0 >> 8) & 0xffu) + ((p1 >> 8) & 0xffu) + ((p2 >> 8) & 0xffu) + ((p3 >> 8) & 0xffu));
                    int val = (int)(rwt * rval + gwt * gval + bwt * bval);
                    SetDataByteUnchecked(ref dstLineRef, j, val);
                }
            }
        });
    }

    /*------------------------------------------------------------------*
     *                  General area mapped gray scaling                *
     *------------------------------------------------------------------*/
    // leptonica: scaleColorAreaMapLow (scale1.c)
    private static void ScaleColorAreaMapLow(uint[] datad, int wd, int hd, int wpld,
                                             uint[] datas, int ws, int hs, int wpls)
    {
        float scx = 16.0f * (float)ws / (float)wd;
        float scy = 16.0f * (float)hs / (float)hd;
        int wm2 = ws - 2;
        int hm2 = hs - 2;

        LeptParallel.ForRows(hd, wd * 4, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                int yu = (int)(scy * i);
                int yl = (int)(scy * (i + 1.0));
                int yup = yu >> 4;
                int yuf = yu & 0x0f;
                int ylp = yl >> 4;
                int ylf = yl & 0x0f;
                int dely = ylp - yup;
                int lined = i * wpld;
                int lines = yup * wpls;
                int botOff = dely * wpls;
                int wy0 = 16 - yuf;
                int areat = 16 * wy0;
                int areab = 16 * ylf;
                int areaY = wy0 + 16 * (dely - 1) + ylf;

                for (int j = 0; j < wd; j++) {
                    int xu = (int)(scx * j);
                    int xl = (int)(scx * (j + 1.0));
                    int xup = xu >> 4;
                    int xlp = xl >> 4;

                    if (xlp > wm2 || ylp > hm2) {
                        Unsafe.Add(ref datadRef, lined + j) = Unsafe.Add(ref datasRef, lines + xup);
                        continue;
                    }

                    int xuf = xu & 0x0f;
                    int xlf = xl & 0x0f;
                    int delx = xlp - xup;
                    int wx0 = 16 - xuf;
                    int area = (wx0 + 16 * (delx - 1) + xlf) * areaY;

                    uint p00 = Unsafe.Add(ref datasRef, lines + xup);
                    uint p10 = Unsafe.Add(ref datasRef, lines + xlp);
                    uint p01 = Unsafe.Add(ref datasRef, lines + botOff + xup);
                    uint p11 = Unsafe.Add(ref datasRef, lines + botOff + xlp);
                    int a00 = wx0 * wy0;
                    int a10 = xlf * wy0;
                    int a01 = wx0 * ylf;
                    int a11 = xlf * ylf;

                    int rsum = a00 * (int)(p00 >> 24) + a10 * (int)(p10 >> 24) + a01 * (int)(p01 >> 24) + a11 * (int)(p11 >> 24);
                    int gsum = a00 * (int)((p00 >> 16) & 0xffu) + a10 * (int)((p10 >> 16) & 0xffu) + a01 * (int)((p01 >> 16) & 0xffu) + a11 * (int)((p11 >> 16) & 0xffu);
                    int bsum = a00 * (int)((p00 >> 8) & 0xffu) + a10 * (int)((p10 >> 8) & 0xffu) + a01 * (int)((p01 >> 8) & 0xffu) + a11 * (int)((p11 >> 8) & 0xffu);

                    if (dely > 1 && delx > 1) {
                        int ir = 0, ig = 0, ib = 0;
                        for (int k = 1; k < dely; k++) {
                            int rowOff = lines + k * wpls + xup;
                            for (int m = 1; m < delx; m++) {
                                uint px = Unsafe.Add(ref datasRef, rowOff + m);
                                ir += (int)(px >> 24);
                                ig += (int)((px >> 16) & 0xffu);
                                ib += (int)((px >> 8) & 0xffu);
                            }
                        }
                        rsum += ir << 8;
                        gsum += ig << 8;
                        bsum += ib << 8;
                    }

                    if (dely > 1) {
                        int lr = 0, lg = 0, lb = 0, rr = 0, rg = 0, rb = 0;
                        for (int k = 1; k < dely; k++) {
                            uint pl = Unsafe.Add(ref datasRef, lines + k * wpls + xup);
                            uint pr = Unsafe.Add(ref datasRef, lines + k * wpls + xlp);
                            lr += (int)(pl >> 24); lg += (int)((pl >> 16) & 0xffu); lb += (int)((pl >> 8) & 0xffu);
                            rr += (int)(pr >> 24); rg += (int)((pr >> 16) & 0xffu); rb += (int)((pr >> 8) & 0xffu);
                        }
                        int areal = wx0 * 16, arear = xlf * 16;
                        rsum += areal * lr + arear * rr;
                        gsum += areal * lg + arear * rg;
                        bsum += areal * lb + arear * rb;
                    }

                    if (delx > 1) {
                        int tr = 0, tg = 0, tb = 0, br = 0, bg = 0, bb = 0;
                        int topOff = lines + xup;
                        int btmOff = lines + botOff + xup;
                        for (int m = 1; m < delx; m++) {
                            uint pt = Unsafe.Add(ref datasRef, topOff + m);
                            uint pb = Unsafe.Add(ref datasRef, btmOff + m);
                            tr += (int)(pt >> 24); tg += (int)((pt >> 16) & 0xffu); tb += (int)((pt >> 8) & 0xffu);
                            br += (int)(pb >> 24); bg += (int)((pb >> 16) & 0xffu); bb += (int)((pb >> 8) & 0xffu);
                        }
                        rsum += areat * tr + areab * br;
                        gsum += areat * tg + areab * bg;
                        bsum += areat * tb + areab * bb;
                    }

                    int rval = (rsum + 128) / area;
                    int gval = (gsum + 128) / area;
                    int bval = (bsum + 128) / area;
                    Unsafe.Add(ref datadRef, lined + j) = (uint)((rval << 24) | (gval << 16) | (bval << 8));
                }
            }
        });
    }

    // leptonica: scaleGrayAreaMapLow (scale1.c)
    private static void ScaleGrayAreaMapLow(uint[] datad, int wd, int hd, int wpld,
                                            uint[] datas, int ws, int hs, int wpls)
    {
        float scx = 16.0f * (float)ws / (float)wd;
        float scy = 16.0f * (float)hs / (float)hd;
        int wm2 = ws - 2;
        int hm2 = hs - 2;

        LeptParallel.ForRows(hd, wd * 4, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                int yu = (int)(scy * i);
                int yl = (int)(scy * (i + 1.0));
                int yup = yu >> 4;
                int yuf = yu & 0x0f;
                int ylp = yl >> 4;
                int ylf = yl & 0x0f;
                int dely = ylp - yup;
                ref uint dstLineRef = ref Unsafe.Add(ref datadRef, i * wpld);
                if ((wd & 3) != 0)
                    Unsafe.Add(ref dstLineRef, wd >> 2) = 0;
                ref uint topLineRef = ref Unsafe.Add(ref datasRef, yup * wpls);
                ref uint botLineRef = ref Unsafe.Add(ref topLineRef, dely * wpls);
                int wy0 = 16 - yuf;
                int areat = 16 * wy0;
                int areab = 16 * ylf;
                int areaY = wy0 + 16 * (dely - 1) + ylf;

                for (int j = 0; j < wd; j++) {
                    int xu = (int)(scx * j);
                    int xl = (int)(scx * (j + 1.0));
                    int xup = xu >> 4;
                    int xlp = xl >> 4;

                    if (xlp > wm2 || ylp > hm2) {
                        SetDataByteUnchecked(ref dstLineRef, j, GetDataByteUnchecked(ref topLineRef, xup));
                        continue;
                    }

                    int xuf = xu & 0x0f;
                    int xlf = xl & 0x0f;
                    int delx = xlp - xup;
                    int wx0 = 16 - xuf;
                    int area = (wx0 + 16 * (delx - 1) + xlf) * areaY;

                    int sum = wx0 * wy0 * GetDataByteUnchecked(ref topLineRef, xup) +
                              xlf * wy0 * GetDataByteUnchecked(ref topLineRef, xlp) +
                              wx0 * ylf * GetDataByteUnchecked(ref botLineRef, xup) +
                              xlf * ylf * GetDataByteUnchecked(ref botLineRef, xlp);

                    if (dely > 1 && delx > 1) {
                        int inSum = 0;
                        for (int k = 1; k < dely; k++) {
                            ref uint kLineRef = ref Unsafe.Add(ref topLineRef, k * wpls);
                            for (int m = 1; m < delx; m++)
                                inSum += GetDataByteUnchecked(ref kLineRef, xup + m);
                        }
                        sum += inSum << 8;
                    }

                    if (dely > 1) {
                        int lSum = 0, rSum = 0;
                        for (int k = 1; k < dely; k++) {
                            ref uint kLineRef = ref Unsafe.Add(ref topLineRef, k * wpls);
                            lSum += GetDataByteUnchecked(ref kLineRef, xup);
                            rSum += GetDataByteUnchecked(ref kLineRef, xlp);
                        }
                        sum += wx0 * 16 * lSum + xlf * 16 * rSum;
                    }

                    if (delx > 1) {
                        int tSum = 0, bSum = 0;
                        for (int m = 1; m < delx; m++) {
                            tSum += GetDataByteUnchecked(ref topLineRef, xup + m);
                            bSum += GetDataByteUnchecked(ref botLineRef, xup + m);
                        }
                        sum += areat * tSum + areab * bSum;
                    }

                    SetDataByteUnchecked(ref dstLineRef, j, (sum + 128) / area);
                }
            }
        });
    }

    /*------------------------------------------------------------------*
     *                     2x area mapped downscaling                   *
     *------------------------------------------------------------------*/
    // leptonica: scaleAreaMapLow2 (scale1.c)
    /// <remarks>
    /// (1) This function is called with either 8 bpp gray or 32 bpp RGB.
    ///     The result is a 2x reduced dest.
    /// </remarks>
    private static void ScaleAreaMapLow2(Span<uint> datad, int wd, int hd, int wpld,
                                         ReadOnlySpan<uint> datas, int d, int wpls)
    {
        int i, j, val, rval, gval, bval;
        uint pixel;

        if (d == 8) {
            for (i = 0; i < hd; i++) {
                ReadOnlySpan<uint> lines = datas.Slice(2 * i * wpls);
                Span<uint> lined = datad.Slice(i * wpld);
                for (j = 0; j < wd; j++) {
                    /* Average each dest pixel using 4 src pixels */
                    val = GetDataByte(lines, 2 * j);
                    val += GetDataByte(lines, 2 * j + 1);
                    val += GetDataByte(lines.Slice(wpls), 2 * j);
                    val += GetDataByte(lines.Slice(wpls), 2 * j + 1);
                    val >>= 2;
                    SetDataByte(lined, j, val);
                }
            }
        } else {  /* d == 32 */
            for (i = 0; i < hd; i++) {
                ReadOnlySpan<uint> lines = datas.Slice(2 * i * wpls);
                Span<uint> lined = datad.Slice(i * wpld);
                for (j = 0; j < wd; j++) {
                    /* Average each of the color components from 4 src pixels */
                    pixel = lines[2 * j];
                    rval = (int)((pixel >> L.L_RED_SHIFT) & 0xff);
                    gval = (int)((pixel >> L.L_GREEN_SHIFT) & 0xff);
                    bval = (int)((pixel >> L.L_BLUE_SHIFT) & 0xff);
                    pixel = lines[2 * j + 1];
                    rval += (int)((pixel >> L.L_RED_SHIFT) & 0xff);
                    gval += (int)((pixel >> L.L_GREEN_SHIFT) & 0xff);
                    bval += (int)((pixel >> L.L_BLUE_SHIFT) & 0xff);
                    pixel = lines[wpls + 2 * j];
                    rval += (int)((pixel >> L.L_RED_SHIFT) & 0xff);
                    gval += (int)((pixel >> L.L_GREEN_SHIFT) & 0xff);
                    bval += (int)((pixel >> L.L_BLUE_SHIFT) & 0xff);
                    pixel = lines[wpls + 2 * j + 1];
                    rval += (int)((pixel >> L.L_RED_SHIFT) & 0xff);
                    gval += (int)((pixel >> L.L_GREEN_SHIFT) & 0xff);
                    bval += (int)((pixel >> L.L_BLUE_SHIFT) & 0xff);
                    ComposeRGBPixel(rval >> 2, gval >> 2, bval >> 2, out pixel);
                    lined[j] = pixel;
                }
            }
        }
    }

    /*------------------------------------------------------------------*
     *              Binary scaling by closest pixel sampling            *
     *------------------------------------------------------------------*/
    /// <summary>
    /// Collects the bits for columns 0, 2, 4, ... 30 of a 1 bpp word (bits 31, 29, ... 1)
    /// into the low 16 bits, column 0 in bit 15.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint KeepEvenColumns(uint x)
    {
        x = (x >> 1) & 0x55555555u;
        x = (x | (x >> 1)) & 0x33333333u;
        x = (x | (x >> 2)) & 0x0f0f0f0fu;
        x = (x | (x >> 4)) & 0x00ff00ffu;
        x = (x | (x >> 8)) & 0x0000ffffu;
        return x;
    }

    // leptonica: scaleBinaryLow (scale1.c)
    /// <remarks>
    /// (1) The dest must be cleared prior to this operation,
    ///     and we clear it here in the low-level code.
    /// (2) We reuse dest pixels and dest pixel rows whenever
    ///     possible for upscaling; downscaling is done by
    ///     strict subsampling.
    /// </remarks>
    private static void ScaleBinaryLow(Span<uint> datad, int wd, int hd, int wpld,
                                       ReadOnlySpan<uint> datas, int ws, int hs, int wpls,
                                       float shiftx, float shifty)
    {
        int i, j;
        int xs, prevxs, sval;
        int[] srow, scol;
        int prevlines;
        float wratio, hratio;

        /* Clear dest */
        datad.Slice(0, hd * wpld).Clear();

        /* The source row corresponding to dest row i ==> srow[i]
         * The source col corresponding to dest col j ==> scol[j]  */
        srow = new int[hd];
        scol = new int[wd];

        wratio = (float)ws / (float)wd;
        hratio = (float)hs / (float)hd;
        for (i = 0; i < hd; i++)
            srow[i] = Math.Min((int)(hratio * i + shifty), hs - 1);
        for (j = 0; j < wd; j++)
            scol[j] = Math.Min((int)(wratio * j + shiftx), ws - 1);

        /* Exact 2x reduction: dest col j is source col 2j, which lets a dest
         * word be made from two source words by keeping every other bit. */
        bool step2 = true;
        for (j = 0; j < wd; j++) {
            if (scol[j] != 2 * j) {
                step2 = false;
                break;
            }
        }

        // NOTE(upstream): scale1.c retains prevxs and sval across source rows;
        // a one-pixel destination row can reuse the preceding row's pixel.
        prevlines = -1;
        prevxs = -1;
        sval = 0;
        for (i = 0; i < hd; i++) {
            int lines = srow[i] * wpls;
            int lined = i * wpld;
            if (lines != prevlines) {  /* make dest from new source row */
                ReadOnlySpan<uint> lineS = datas.Slice(lines);
                Span<uint> lineD = datad.Slice(lined);
                int jStart = 0;
                if (step2) {
                    int nfull = wd >> 5;
                    for (int k = 0; k < nfull; k++)
                        lineD[k] = (KeepEvenColumns(lineS[2 * k]) << 16) | KeepEvenColumns(lineS[2 * k + 1]);
                    jStart = nfull << 5;
                    if (jStart > 0)
                        prevxs = scol[jStart - 1];
                }
                for (j = jStart; j < wd; j++) {
                    xs = scol[j];
                    if (xs != prevxs) {  /* get dest pix from source col */
                        if ((sval = GetDataBit(lineS, xs)) != 0)
                            SetDataBit(lineD, j);
                        prevxs = xs;
                    } else {  /* copy prev dest pix, if set */
                        if (sval != 0)
                            SetDataBit(lineD, j);
                    }
                }
            } else {  /* lines == prevlines; copy prev dest row */
                int prevlined = lined - wpld;
                datad.Slice(prevlined, wpld).CopyTo(datad.Slice(lined, wpld));
            }
            prevlines = lines;
        }
    }

}
