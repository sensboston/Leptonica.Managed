using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  scale2.c: Scale-to-gray, mipmap scaling, replicated expansion, min/max/rank
 *            downscaling, scaling with alpha.
 *
 *  Differences from C:
 *    - The low-level static functions work on uint[] arrays and an explicit
 *      line offset replaces the C pointer arithmetic (lines + k * wpls).
 *      scaleToGray*Low and scaleMipmapLow return void: their only error
 *      return (a failed allocation) cannot happen.
 *    - The tables made by makeSumTabSG* and makeValTabSG* are built exactly as in C.
 *    - pixScaleAndTransferAlpha is an instance method of the destination pix.
 *
 *  Not ported: nothing.
 *
 *  Stubs for callees in other files: none.
 */
public sealed partial class Pix
{
    /*------------------------------------------------------------------*
     *      Scale-to-gray (1 bpp --> 8 bpp; arbitrary downscaling)      *
     *------------------------------------------------------------------*/
    // leptonica: pixScaleToGray (scale2.c)
    /// <summary>Scales a 1 bpp pix down to an 8 bpp gray pix.</summary>
    /// <param name="scalefactor">reduction: must be &gt; 0.0 and &lt; 1.0</param>
    /// <returns>pixd 8 bpp, scaled down by scalefactor in each direction</returns>
    /// <remarks>
    /// For faster scaling in the range of scalefactors from 0.0625 to 0.5,
    /// with very little difference in quality, use pixScaleToGrayFast().
    ///
    /// Binary images have sharp edges, so they intrinsically have very
    /// high frequency content.  To avoid aliasing, they must be low-pass
    /// filtered, which tends to blur the edges.  How can we keep relatively
    /// crisp edges without aliasing?  The trick is to do binary upscaling
    /// followed by a power-of-2 scaleToGray.  For large reductions, where
    /// you don't end up with much detail, some corners can be cut.
    ///
    /// The intent here is to get high quality reduced grayscale
    /// images with relatively little computation.  We do binary
    /// pre-scaling followed by scaleToGrayN() for best results,
    /// esp. to avoid excess blur when the scale factor is near
    /// an inverse power of 2.  Where a low-pass filter is required,
    /// we use simple convolution kernels: either the hat filter for
    /// linear interpolation or a flat filter for larger downscaling.
    /// Other choices, such as a perfect bandpass filter with infinite extent
    /// (the sinc) or various approximations to it (e.g., lanczos), are
    /// unnecessarily expensive.
    ///
    /// The choices made are as follows:
    ///     (1) Do binary upscaling before scaleToGrayN() for scalefactors &gt; 1/8
    ///     (2) Do binary downscaling before scaleToGray8() for scalefactors
    ///         between 1/16 and 1/8.
    ///     (3) Use scaleToGray16() before grayscale downscaling for
    ///         scalefactors less than 1/16
    /// Another reasonable choice would be to start binary downscaling
    /// for scalefactors below 1/4, rather than below 1/8 as we do here.
    ///
    /// The general scaling rules, not all of which are used here, go as follows:
    ///     (1) For grayscale upscaling, use pixScaleGrayLI().  However,
    ///         note that edges will be visibly blurred for scalefactors
    ///         near (but above) 1.0.  Replication will avoid edge blur,
    ///         and should be considered for factors very near 1.0.
    ///     (2) For grayscale downscaling with a scale factor larger than
    ///         about 0.7, use pixScaleGrayLI().  For scalefactors near
    ///         (but below) 1.0, you tread between Scylla and Charybdis.
    ///         pixScaleGrayLI() again gives edge blurring, but
    ///         pixScaleBySampling() gives visible aliasing.
    ///     (3) For grayscale downscaling with a scale factor smaller than
    ///         about 0.7, use pixScaleSmooth()
    ///     (4) For binary input images, do as much scale to gray as possible
    ///         using the special integer functions (2, 3, 4, 8 and 16).
    ///     (5) It is better to upscale in binary, followed by scaleToGrayN()
    ///         than to do scaleToGrayN() followed by an upscale using either
    ///         LI or oversampling.
    ///     (6) It may be better to downscale in binary, followed by
    ///         scaleToGrayN() than to first use scaleToGrayN() followed by
    ///         downscaling.  For downscaling between 8x and 16x, this is
    ///         a reasonable option.
    ///     (7) For reductions greater than 16x, it's reasonable to use
    ///         scaleToGray16() followed by further grayscale downscaling.
    /// </remarks>
    public Pix ScaleToGray(float scalefactor)
    {
        const string func = "pixScaleToGray";
        Pix pixt, pixd;
        float mag, red;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (scalefactor <= 0.0f)
            throw Lept.Arg(func, "scalefactor <= 0.0");
        if (scalefactor >= 1.0f)
            throw Lept.Arg(func, "scalefactor >= 1.0");
        GetDimensions(out int w, out int h, out _);
        int minsrc = Math.Min(w, h);
        int mindest = (int)((float)minsrc * scalefactor);
        if (mindest < 2)
            throw Lept.Arg(func, "scalefactor too small");

        if (scalefactor > 0.5f) {   /* see note (5) */
            mag = 2.0f * scalefactor;  /* will be < 2.0 */
            pixt = ScaleBinary(mag, mag);
            pixd = pixt.ScaleToGray2();
        } else if (scalefactor == 0.5f) {
            return ScaleToGray2();
        } else if (scalefactor > 0.33333f) {   /* see note (5) */
            mag = 3.0f * scalefactor;   /* will be < 1.5 */
            pixt = ScaleBinary(mag, mag);
            pixd = pixt.ScaleToGray3();
        } else if (scalefactor > 0.25f) {  /* see note (5) */
            mag = 4.0f * scalefactor;   /* will be < 1.3333 */
            pixt = ScaleBinary(mag, mag);
            pixd = pixt.ScaleToGray4();
        } else if (scalefactor == 0.25f) {
            return ScaleToGray4();
        } else if (scalefactor > 0.16667f) {  /* see note (5) */
            mag = 6.0f * scalefactor;   /* will be < 1.5 */
            pixt = ScaleBinary(mag, mag);
            pixd = pixt.ScaleToGray6();
        } else if (scalefactor == 0.16667f) {
            return ScaleToGray6();
        } else if (scalefactor > 0.125f) {  /* see note (5) */
            mag = 8.0f * scalefactor;   /*  will be < 1.3333  */
            pixt = ScaleBinary(mag, mag);
            pixd = pixt.ScaleToGray8();
        } else if (scalefactor == 0.125f) {
            return ScaleToGray8();
        } else if (scalefactor > 0.0625f) {  /* see note (6) */
            red = 8.0f * scalefactor;   /* will be > 0.5 */
            pixt = ScaleBinary(red, red);
            pixd = pixt.ScaleToGray8();
        } else if (scalefactor == 0.0625f) {
            return ScaleToGray16();
        } else {  /* see note (7) */
            red = 16.0f * scalefactor;  /* will be <= 1.0 */
            pixt = ScaleToGray16();
            if (red < 0.7f)
                pixd = pixt.ScaleSmooth(red, red);  /* see note (3) */
            else
                pixd = pixt.ScaleGrayLI(red, red);  /* see note (2) */
        }

        pixd.CopyInputFormat(this);
        return pixd;
    }

    // leptonica: pixScaleToGrayFast (scale2.c)
    /// <summary>Scales a 1 bpp pix down to an 8 bpp gray pix, faster than pixScaleToGray().</summary>
    /// <param name="scalefactor">reduction: must be &gt; 0.0 and &lt; 1.0</param>
    /// <returns>pixd 8 bpp, scaled down by scalefactor in each direction</returns>
    /// <remarks>
    /// (1) See notes in pixScaleToGray() for the basic approach.
    /// (2) This function is considerably less expensive than pixScaleToGray()
    ///     for scalefactor in the range (0.0625 ... 0.5), and the
    ///     quality is nearly as good.
    /// (3) Unlike pixScaleToGray(), which does binary upscaling before
    ///     downscaling for scale factors &gt;= 0.0625, pixScaleToGrayFast()
    ///     first downscales in binary for all scale factors &lt; 0.5, and
    ///     then does a 2x scale-to-gray as the final step.  For
    ///     scale factors &lt; 0.0625, both do a 16x scale-to-gray, followed
    ///     by further grayscale reduction.
    /// </remarks>
    public Pix ScaleToGrayFast(float scalefactor)
    {
        const string func = "pixScaleToGrayFast";
        Pix pixt, pixd;
        float factor;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (scalefactor <= 0.0f)
            throw Lept.Arg(func, "scalefactor <= 0.0");
        if (scalefactor >= 1.0f)
            throw Lept.Arg(func, "scalefactor >= 1.0");
        GetDimensions(out int w, out int h, out _);
        int minsrc = Math.Min(w, h);
        int mindest = (int)((float)minsrc * scalefactor);
        if (mindest < 2)
            throw Lept.Arg(func, "scalefactor too small");
        float eps = 0.0001f;

            /* Handle the special cases */
        if (scalefactor > 0.5f - eps && scalefactor < 0.5f + eps)
            return ScaleToGray2();
        else if (scalefactor > 0.33333f - eps && scalefactor < 0.33333f + eps)
            return ScaleToGray3();
        else if (scalefactor > 0.25f - eps && scalefactor < 0.25f + eps)
            return ScaleToGray4();
        else if (scalefactor > 0.16666f - eps && scalefactor < 0.16666f + eps)
            return ScaleToGray6();
        else if (scalefactor > 0.125f - eps && scalefactor < 0.125f + eps)
            return ScaleToGray8();
        else if (scalefactor > 0.0625f - eps && scalefactor < 0.0625f + eps)
            return ScaleToGray16();

        if (scalefactor > 0.0625f) {  /* scale binary first */
            factor = 2.0f * scalefactor;
            pixt = ScaleBinary(factor, factor);
            pixd = pixt.ScaleToGray2();
        } else {  /* scalefactor < 0.0625; scale-to-gray first */
            factor = 16.0f * scalefactor;  /* will be < 1.0 */
            pixt = ScaleToGray16();
            if (factor < 0.7f)
                pixd = pixt.ScaleSmooth(factor, factor);
            else
                pixd = pixt.ScaleGrayLI(factor, factor);
        }
        pixd.CopyInputFormat(this);
        return pixd;
    }

    private static readonly uint[] s_sumTabSG2 = MakeSumTabSG2();
    private static readonly byte[] s_valTabSG2 = MakeValTabSG2();
    private static readonly uint[] s_sumTabSG3 = MakeSumTabSG3();
    private static readonly byte[] s_valTabSG3 = MakeValTabSG3();
    private static readonly uint[] s_sumTabSG4 = MakeSumTabSG4();
    private static readonly byte[] s_valTabSG4 = MakeValTabSG4();
    private static readonly byte[] s_valTabSG6 = MakeValTabSG6();
    private static readonly byte[] s_valTabSG8 = MakeValTabSG8();
    private static readonly int[] s_tab8SG = MakePixelSumTab8();

    /*-----------------------------------------------------------------------*
     *          Scale-to-gray (1 bpp --> 8 bpp; integer downscaling)         *
     *-----------------------------------------------------------------------*/
    // leptonica: pixScaleToGray2 (scale2.c)
    /// <summary>Scales a 1 bpp pix down by 2x to an 8 bpp gray pix.</summary>
    /// <returns>pixd 8 bpp, scaled down by 2x in each direction</returns>
    public Pix ScaleToGray2()
    {
        const string func = "pixScaleToGray2";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs must be 1 bpp");

        GetDimensions(out int ws, out int hs, out _);
        int wd = ws / 2;
        int hd = hs / 2;
        if (wd == 0 || hd == 0)
            throw Lept.Arg(func, "pixs too small");

        Pix pixd = CreateNoInit(wd, hd, 8);
        SetPadBits(0);
        pixd.CopyInputFormat(this);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(0.5f, 0.5f);

        ScaleToGray2Low(pixd.Data, wd, hd, pixd.Wpl, Data, Wpl, s_sumTabSG2, s_valTabSG2);
        return pixd;
    }

    // leptonica: pixScaleToGray3 (scale2.c)
    /// <summary>Scales a 1 bpp pix down by 3x to an 8 bpp gray pix.</summary>
    /// <returns>pixd 8 bpp, scaled down by 3x in each direction</returns>
    /// <remarks>
    /// (1) Speed is about 100 x 10^6 src-pixels/sec/GHz.
    ///     Another way to express this is it processes 1 src pixel
    ///     in about 10 cycles.
    /// (2) The width of pixd is truncated is truncated to a factor of 8.
    /// </remarks>
    public Pix ScaleToGray3()
    {
        const string func = "pixScaleToGray3";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");

        GetDimensions(out int ws, out int hs, out _);
        int wd = (ws / 3) & unchecked((int)0xfffffff8);    /* truncate to factor of 8 */
        int hd = hs / 3;
        if (wd == 0 || hd == 0)
            throw Lept.Arg(func, "pixs too small");

        Pix pixd = CreateNoInit(wd, hd, 8);
        pixd.CopyInputFormat(this);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(0.33333f, 0.33333f);

        ScaleToGray3Low(pixd.Data, wd, hd, pixd.Wpl, Data, Wpl, s_sumTabSG3, s_valTabSG3);
        return pixd;
    }

    // leptonica: pixScaleToGray4 (scale2.c)
    /// <summary>Scales a 1 bpp pix down by 4x to an 8 bpp gray pix.</summary>
    /// <returns>pixd 8 bpp, scaled down by 4x in each direction</returns>
    /// <remarks>
    /// (1) The width of pixd is truncated is truncated to a factor of 2.
    /// </remarks>
    public Pix ScaleToGray4()
    {
        const string func = "pixScaleToGray4";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs must be 1 bpp");

        GetDimensions(out int ws, out int hs, out _);
        int wd = (ws / 4) & unchecked((int)0xfffffffe);    /* truncate to factor of 2 */
        int hd = hs / 4;
        if (wd == 0 || hd == 0)
            throw Lept.Arg(func, "pixs too small");

        Pix pixd = CreateNoInit(wd, hd, 8);
        pixd.CopyInputFormat(this);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(0.25f, 0.25f);

        ScaleToGray4Low(pixd.Data, wd, hd, pixd.Wpl, Data, Wpl, s_sumTabSG4, s_valTabSG4);
        return pixd;
    }

    // leptonica: pixScaleToGray6 (scale2.c)
    /// <summary>Scales a 1 bpp pix down by 6x to an 8 bpp gray pix.</summary>
    /// <returns>pixd 8 bpp, scaled down by 6x in each direction</returns>
    /// <remarks>
    /// (1) The width of pixd is truncated is truncated to a factor of 8.
    /// </remarks>
    public Pix ScaleToGray6()
    {
        const string func = "pixScaleToGray6";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");

        GetDimensions(out int ws, out int hs, out _);
        int wd = (ws / 6) & unchecked((int)0xfffffff8);    /* truncate to factor of 8 */
        int hd = hs / 6;
        if (wd == 0 || hd == 0)
            throw Lept.Arg(func, "pixs too small");

        Pix pixd = CreateNoInit(wd, hd, 8);
        pixd.CopyInputFormat(this);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(0.16667f, 0.16667f);

        ScaleToGray6Low(pixd.Data, wd, hd, pixd.Wpl, Data, Wpl, s_tab8SG, s_valTabSG6);
        return pixd;
    }

    // leptonica: pixScaleToGray8 (scale2.c)
    /// <summary>Scales a 1 bpp pix down by 8x to an 8 bpp gray pix.</summary>
    /// <returns>pixd 8 bpp, scaled down by 8x in each direction</returns>
    public Pix ScaleToGray8()
    {
        const string func = "pixScaleToGray8";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs must be 1 bpp");

        GetDimensions(out int ws, out int hs, out _);
        int wd = ws / 8;  /* truncate to nearest dest byte */
        int hd = hs / 8;
        if (wd == 0 || hd == 0)
            throw Lept.Arg(func, "pixs too small");

        Pix pixd = CreateNoInit(wd, hd, 8);
        pixd.CopyInputFormat(this);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(0.125f, 0.125f);

        ScaleToGray8Low(pixd.Data, wd, hd, pixd.Wpl, Data, Wpl, s_tab8SG, s_valTabSG8);
        return pixd;
    }

    // leptonica: pixScaleToGray16 (scale2.c)
    /// <summary>Scales a 1 bpp pix down by 16x to an 8 bpp gray pix.</summary>
    /// <returns>pixd 8 bpp, scaled down by 16x in each direction</returns>
    public Pix ScaleToGray16()
    {
        const string func = "pixScaleToGray16";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs must be 1 bpp");

        GetDimensions(out int ws, out int hs, out _);
        int wd = ws / 16;
        int hd = hs / 16;
        if (wd == 0 || hd == 0)
            throw Lept.Arg(func, "pixs too small");

        Pix pixd = Create(wd, hd, 8);
        pixd.CopyInputFormat(this);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(0.0625f, 0.0625f);

        int[] tab8 = MakePixelSumTab8();
        ScaleToGray16Low(pixd.Data, wd, hd, pixd.Wpl, Data, Wpl, tab8);
        return pixd;
    }

    /*------------------------------------------------------------------*
     *    Scale-to-gray mipmap(1 bpp --> 8 bpp, arbitrary reduction)    *
     *------------------------------------------------------------------*/
    // leptonica: pixScaleToGrayMipmap (scale2.c)
    /// <summary>Scales a 1 bpp pix down to 8 bpp gray, using mipmap interpolation.</summary>
    /// <param name="scalefactor">reduction: must be &gt; 0.0 and &lt; 1.0</param>
    /// <returns>pixd 8 bpp, scaled down by scalefactor in each direction</returns>
    /// <remarks>
    /// This function is here mainly for pedagogical reasons.
    /// Mip-mapping is widely used in graphics for texture mapping, because
    /// the texture changes smoothly with scale.  This is accomplished by
    /// constructing a multiresolution pyramid and, for each pixel,
    /// doing a linear interpolation between corresponding pixels in
    /// the two planes of the pyramid that bracket the desired resolution.
    /// The computation is very efficient, and is implemented in hardware
    /// in high-end graphics cards.
    ///
    /// We can use mip-mapping for scale-to-gray by using two scale-to-gray
    /// reduced images (we don't need the entire pyramid) selected from
    /// the set {2x, 4x, ... 16x}, and interpolating.  However, we get
    /// severe aliasing, probably because we are subsampling from the
    /// higher resolution image.  The method is very fast, but the result
    /// is very poor.  In fact, the results don't look any better than
    /// either subsampling off the higher-res grayscale image or oversampling
    /// on the lower-res image.  Consequently, this method should NOT be used
    /// for generating reduced images, scale-to-gray or otherwise.
    /// </remarks>
    public Pix ScaleToGrayMipmap(float scalefactor)
    {
        const string func = "pixScaleToGrayMipmap";
        Pix pixs1, pixs2, pixt, pixd;
        float red;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (scalefactor <= 0.0f)
            throw Lept.Arg(func, "scalefactor <= 0.0");
        if (scalefactor >= 1.0f)
            throw Lept.Arg(func, "scalefactor >= 1.0");
        GetDimensions(out int w, out int h, out _);
        int minsrc = Math.Min(w, h);
        int mindest = (int)((float)minsrc * scalefactor);
        if (mindest < 2)
            throw Lept.Arg(func, "scalefactor too small");

        if (scalefactor > 0.5f) {
            pixs1 = Convert1To8(null, this, 255, 0);
            pixs2 = ScaleToGray2();
            red = scalefactor;
        } else if (scalefactor == 0.5f) {
            return ScaleToGray2();
        } else if (scalefactor > 0.25f) {
            pixs1 = ScaleToGray2();
            pixs2 = ScaleToGray4();
            red = 2.0f * scalefactor;
        } else if (scalefactor == 0.25f) {
            return ScaleToGray4();
        } else if (scalefactor > 0.125f) {
            pixs1 = ScaleToGray4();
            pixs2 = ScaleToGray8();
            red = 4.0f * scalefactor;
        } else if (scalefactor == 0.125f) {
            return ScaleToGray8();
        } else if (scalefactor > 0.0625f) {
            pixs1 = ScaleToGray8();
            pixs2 = ScaleToGray16();
            red = 8.0f * scalefactor;
        } else if (scalefactor == 0.0625f) {
            return ScaleToGray16();
        } else {  /* end of the pyramid; just do it */
            red = 16.0f * scalefactor;  /* will be <= 1.0 */
            pixt = ScaleToGray16();
            if (red < 0.7f)
                pixd = pixt.ScaleSmooth(red, red);
            else
                pixd = pixt.ScaleGrayLI(red, red);
            return pixd;
        }

        pixd = pixs1.ScaleMipmap(pixs2, red);
        pixd.CopyInputFormat(this);
        return pixd;
    }

    /*------------------------------------------------------------------*
     *                  Grayscale scaling using mipmap                  *
     *------------------------------------------------------------------*/
    // leptonica: pixScaleMipmap (scale2.c)
    /// <summary>Scales an 8 bpp pix down using linear interpolation between two resolutions.</summary>
    /// <param name="pixs2">low res -- 2x reduced -- 8 bpp, no cmap</param>
    /// <param name="scale">reduction with respect to high res image, &gt; 0.5</param>
    /// <returns>8 bpp pix, scaled down by reduction in each direction.
    /// This pix is the high res image pixs1, 8 bpp, no cmap.</returns>
    /// <remarks>
    /// (1) See notes in pixScaleToGrayMipmap().
    /// (2) This function suffers from aliasing effects that are
    ///     easily seen in document images.
    /// </remarks>
    public Pix ScaleMipmap(Pix pixs2, float scale)
    {
        const string func = "pixScaleMipmap";

        if (Depth != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs1 underdefined, not 8 bpp, or cmapped");
        if (pixs2.Depth != 8 || pixs2.Colormap != null)
            throw Lept.Arg(func, "pixs2 underdefined, not 8 bpp, or cmapped");
        GetDimensions(out int ws1, out int hs1, out _);
        pixs2.GetDimensions(out int ws2, out int hs2, out _);
        if (scale > 1.0f || scale < 0.5f)
            throw Lept.Arg(func, "scale not in [0.5, 1.0]");
        if (ws1 < 2 * ws2)
            throw Lept.Arg(func, "invalid width ratio");
        if (hs1 < 2 * hs2)
            throw Lept.Arg(func, "invalid height ratio");

            /* Generate wd and hd from the lower resolution dimensions,
             * to guarantee staying within both src images */
        uint[] datas1 = Data;
        int wpls1 = Wpl;
        uint[] datas2 = pixs2.Data;
        int wpls2 = pixs2.Wpl;
        int wd = (int)(2 * scale * pixs2.Width);
        int hd = (int)(2 * scale * pixs2.Height);
        Pix pixd = Create(wd, hd, 8);
        pixd.CopyInputFormat(this);
        pixd.CopyResolution(this);
        pixd.ScaleResolution(scale, scale);

        ScaleMipmapLow(pixd.Data, wd, hd, pixd.Wpl, datas1, wpls1, datas2, wpls2, scale);
        return pixd;
    }

    /*------------------------------------------------------------------*
     *                  Replicated (integer) expansion                  *
     *------------------------------------------------------------------*/
    // leptonica: pixExpandReplicate (scale2.c)
    /// <summary>Expands the pix by an integer factor, replicating pixels.</summary>
    /// <param name="factor">integer scale factor for replicative expansion</param>
    /// <returns>pixd scaled up</returns>
    public Pix ExpandReplicate(int factor)
    {
        const string func = "pixExpandReplicate";
        int start, i, j, k;

        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg(func, "depth not in {1,2,4,8,16,32}");
        if (factor <= 0)
            throw Lept.Arg(func, "factor <= 0; invalid");
        if (factor == 1)
            return Copy(null, this);

        if (d == 1)
            return ExpandBinaryReplicate(factor, factor);

        int wd = factor * w;
        int hd = factor * h;
        Pix pixd = Create(wd, hd, d);
        pixd.CopyColormap(this);
        pixd.CopyInputFormat(this);
        pixd.CopyResolution(this);
        pixd.ScaleResolution((float)factor, (float)factor);
        uint[] datas = Data;
        int wpls = Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        switch (d) {
        case 2:
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                int offd = factor * i * wpld;
                Span<uint> lined = datad.AsSpan(offd, wpld);
                for (j = 0; j < w; j++) {
                    byte sval = (byte)GetDataDibit(lines, j);
                    start = factor * j;
                    for (k = 0; k < factor; k++)
                        SetDataDibit(lined, start + k, sval);
                }
                for (k = 1; k < factor; k++)
                    lined.CopyTo(datad.AsSpan(offd + k * wpld, wpld));
            }
            break;
        case 4:
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                int offd = factor * i * wpld;
                Span<uint> lined = datad.AsSpan(offd, wpld);
                for (j = 0; j < w; j++) {
                    byte sval = (byte)GetDataQbit(lines, j);
                    start = factor * j;
                    for (k = 0; k < factor; k++)
                        SetDataQbit(lined, start + k, sval);
                }
                for (k = 1; k < factor; k++)
                    lined.CopyTo(datad.AsSpan(offd + k * wpld, wpld));
            }
            break;
        case 8:
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                int offd = factor * i * wpld;
                Span<uint> lined = datad.AsSpan(offd, wpld);
                for (j = 0; j < w; j++) {
                    byte sval = (byte)GetDataByte(lines, j);
                    start = factor * j;
                    for (k = 0; k < factor; k++)
                        SetDataByte(lined, start + k, sval);
                }
                for (k = 1; k < factor; k++)
                    lined.CopyTo(datad.AsSpan(offd + k * wpld, wpld));
            }
            break;
        case 16:
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                int offd = factor * i * wpld;
                Span<uint> lined = datad.AsSpan(offd, wpld);
                for (j = 0; j < w; j++) {
                    ushort sval16 = (ushort)GetDataTwoBytes(lines, j);
                    start = factor * j;
                    for (k = 0; k < factor; k++)
                        SetDataTwoBytes(lined, start + k, sval16);
                }
                for (k = 1; k < factor; k++)
                    lined.CopyTo(datad.AsSpan(offd + k * wpld, wpld));
            }
            break;
        case 32:
            for (i = 0; i < h; i++) {
                int offs = i * wpls;
                int offd = factor * i * wpld;
                Span<uint> lined = datad.AsSpan(offd, wpld);
                for (j = 0; j < w; j++) {
                    uint sval32 = datas[offs + j];
                    start = factor * j;
                    for (k = 0; k < factor; k++)
                        lined[start + k] = sval32;
                }
                for (k = 1; k < factor; k++)
                    lined.CopyTo(datad.AsSpan(offd + k * wpld, wpld));
            }
            break;
        default:
            Log.Error(func, "invalid depth");
            break;
        }

        if (d == 32 && Spp == 4)
            pixd.ScaleAndTransferAlpha(this, (float)factor, (float)factor);
        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *                    Downscaling using min or max                       *
     *-----------------------------------------------------------------------*/
    // leptonica: pixScaleGrayMinMax (scale2.c)
    /// <summary>Downscales an 8 bpp pix by choosing the min, max or max - min of each cell.</summary>
    /// <param name="xfact">x downscaling factor; integer</param>
    /// <param name="yfact">y downscaling factor; integer</param>
    /// <param name="type">L_CHOOSE_MIN, L_CHOOSE_MAX, L_CHOOSE_MAXDIFF</param>
    /// <returns>pixd 8 bpp</returns>
    /// <remarks>
    /// (1) The downscaled pixels in pixd are the min, max or (max - min)
    ///     of the corresponding set of xfact * yfact pixels in pixs.
    /// (2) Using L_CHOOSE_MIN is equivalent to a grayscale erosion,
    ///     using a brick Sel of size (xfact * yfact), followed by
    ///     subsampling within each (xfact * yfact) cell.  Using
    ///     L_CHOOSE_MAX is equivalent to the corresponding dilation.
    /// (3) Using L_CHOOSE_MAXDIFF finds the difference between max
    ///     and min values in each cell.
    /// (4) For the special case of downscaling by 2x in both directions,
    ///     pixScaleGrayMinMax2() is about 2x more efficient.
    /// </remarks>
    public Pix ScaleGrayMinMax(int xfact, int yfact, int type)
    {
        const string func = "pixScaleGrayMinMax";
        int i, j, k, m;
        int minval = 0, maxval = 0, val;

        if (Depth != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs undefined, not 8 bpp, or cmapped");
        GetDimensions(out int ws, out int hs, out _);
        if (type != L.L_CHOOSE_MIN && type != L.L_CHOOSE_MAX &&
            type != L.L_CHOOSE_MAXDIFF)
            throw Lept.Arg(func, "invalid type");
        if (xfact < 1 || yfact < 1)
            throw Lept.Arg(func, "xfact and yfact must be >= 1");

        if (xfact == 2 && yfact == 2)
            return ScaleGrayMinMax2(type);

        int wd = ws / xfact;
        if (wd == 0) {  /* single tile */
            wd = 1;
            xfact = ws;
        }
        int hd = hs / yfact;
        if (hd == 0) {  /* single tile */
            hd = 1;
            yfact = hs;
        }
        Pix pixd = Create(wd, hd, 8);
        pixd.CopyInputFormat(this);
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpls = Wpl;
        int wpld = pixd.Wpl;
        for (i = 0; i < hd; i++) {
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < wd; j++) {
                if (type == L.L_CHOOSE_MIN || type == L.L_CHOOSE_MAXDIFF) {
                    minval = 255;
                    for (k = 0; k < yfact; k++) {
                        ReadOnlySpan<uint> lines = datas.AsSpan((yfact * i + k) * wpls, wpls);
                        for (m = 0; m < xfact; m++) {
                            val = GetDataByte(lines, xfact * j + m);
                            if (val < minval)
                                minval = val;
                        }
                    }
                }
                if (type == L.L_CHOOSE_MAX || type == L.L_CHOOSE_MAXDIFF) {
                    maxval = 0;
                    for (k = 0; k < yfact; k++) {
                        ReadOnlySpan<uint> lines = datas.AsSpan((yfact * i + k) * wpls, wpls);
                        for (m = 0; m < xfact; m++) {
                            val = GetDataByte(lines, xfact * j + m);
                            if (val > maxval)
                                maxval = val;
                        }
                    }
                }
                if (type == L.L_CHOOSE_MIN)
                    SetDataByte(lined, j, minval);
                else if (type == L.L_CHOOSE_MAX)
                    SetDataByte(lined, j, maxval);
                else  /* type == L_CHOOSE_MAXDIFF */
                    SetDataByte(lined, j, maxval - minval);
            }
        }

        return pixd;
    }

    // leptonica: pixScaleGrayMinMax2 (scale2.c)
    /// <summary>Downscales an 8 bpp pix by 2x, choosing the min, max or max - min of each 2x2 cell.</summary>
    /// <param name="type">L_CHOOSE_MIN, L_CHOOSE_MAX, L_CHOOSE_MAXDIFF</param>
    /// <returns>pixd 8 bpp downscaled by 2x</returns>
    /// <remarks>
    /// (1) Special version for 2x reduction.  The downscaled pixels
    ///     in pixd are the min, max or (max - min) of the corresponding
    ///     set of 4 pixels in pixs.
    /// (2) The max and min operations are a special case (for levels 1
    ///     and 4) of grayscale analog to the binary rank scaling operation
    ///     pixReduceRankBinary2().  Note, however, that because of
    ///     the photometric definition that higher gray values are
    ///     lighter, the erosion-like L_CHOOSE_MIN will darken
    ///     the resulting image, corresponding to a threshold level 1
    ///     in the binary case.  Likewise, L_CHOOSE_MAX will lighten
    ///     the pixd, corresponding to a threshold level of 4.
    /// (3) To choose any of the four rank levels in a 2x grayscale
    ///     reduction, use pixScaleGrayRank2().
    /// (4) This runs at about 70 MPix/sec/GHz of source data for
    ///     erosion and dilation.
    /// </remarks>
    public Pix ScaleGrayMinMax2(int type)
    {
        const string func = "pixScaleGrayMinMax2";
        int i, j, k;
        int minval = 0, maxval = 0;
        int[] val = new int[4];

        if (Depth != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs undefined, not 8 bpp, or cmapped");
        GetDimensions(out int ws, out int hs, out _);
        if (ws < 2 || hs < 2)
            throw Lept.Arg(func, "too small: ws < 2 or hs < 2");
        if (type != L.L_CHOOSE_MIN && type != L.L_CHOOSE_MAX &&
            type != L.L_CHOOSE_MAXDIFF)
            throw Lept.Arg(func, "invalid type");

        int wd = ws / 2;
        int hd = hs / 2;
        Pix pixd = Create(wd, hd, 8);
        pixd.CopyInputFormat(this);
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpls = Wpl;
        int wpld = pixd.Wpl;
        for (i = 0; i < hd; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(2 * i * wpls, wpls);
            ReadOnlySpan<uint> lines1 = datas.AsSpan((2 * i + 1) * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < wd; j++) {
                val[0] = GetDataByte(lines, 2 * j);
                val[1] = GetDataByte(lines, 2 * j + 1);
                val[2] = GetDataByte(lines1, 2 * j);
                val[3] = GetDataByte(lines1, 2 * j + 1);
                if (type == L.L_CHOOSE_MIN || type == L.L_CHOOSE_MAXDIFF) {
                    minval = 255;
                    for (k = 0; k < 4; k++) {
                        if (val[k] < minval)
                            minval = val[k];
                    }
                }
                if (type == L.L_CHOOSE_MAX || type == L.L_CHOOSE_MAXDIFF) {
                    maxval = 0;
                    for (k = 0; k < 4; k++) {
                        if (val[k] > maxval)
                            maxval = val[k];
                    }
                }
                if (type == L.L_CHOOSE_MIN)
                    SetDataByte(lined, j, minval);
                else if (type == L.L_CHOOSE_MAX)
                    SetDataByte(lined, j, maxval);
                else  /* type == L_CHOOSE_MAXDIFF */
                    SetDataByte(lined, j, maxval - minval);
            }
        }

        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *                  Grayscale downscaling using rank value               *
     *-----------------------------------------------------------------------*/
    // leptonica: pixScaleGrayRankCascade (scale2.c)
    /// <summary>Performs up to four cascaded 2x rank reductions of an 8 bpp pix.</summary>
    /// <param name="level1">rank threshold, in set {0, 1, 2, 3, 4}</param>
    /// <param name="level2">rank threshold, in set {0, 1, 2, 3, 4}</param>
    /// <param name="level3">rank threshold, in set {0, 1, 2, 3, 4}</param>
    /// <param name="level4">rank threshold, in set {0, 1, 2, 3, 4}</param>
    /// <returns>pixd 8 bpp, downscaled by up to 16x</returns>
    /// <remarks>
    /// (1) This performs up to four cascaded 2x rank reductions.
    /// (2) Use level = 0 to truncate the cascade.
    /// </remarks>
    public Pix ScaleGrayRankCascade(int level1, int level2, int level3, int level4)
    {
        const string func = "pixScaleGrayRankCascade";
        Pix pixt1, pixt2, pixt3, pixt4;

        if (Depth != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs undefined, not 8 bpp, or cmapped");
        if (level1 > 4 || level2 > 4 || level3 > 4 || level4 > 4)
            throw Lept.Arg(func, "levels must not exceed 4");

        if (level1 <= 0) {
            Log.Warning(func, "no reduction because level1 not > 0");
            return Copy(null, this);
        }

        pixt1 = ScaleGrayRank2(level1);
        if (level2 <= 0)
            return pixt1;

        pixt2 = pixt1.ScaleGrayRank2(level2);
        if (level3 <= 0)
            return pixt2;

        pixt3 = pixt2.ScaleGrayRank2(level3);
        if (level4 <= 0)
            return pixt3;

        pixt4 = pixt3.ScaleGrayRank2(level4);
        return pixt4;
    }

    // leptonica: pixScaleGrayRank2 (scale2.c)
    /// <summary>Downscales an 8 bpp pix by 2x, choosing the rank value of each 2x2 cell.</summary>
    /// <param name="rank">1 (darkest), 2, 3, 4 (lightest)</param>
    /// <returns>pixd 8 bpp, downscaled by 2x</returns>
    /// <remarks>
    /// (1) Rank 2x reduction.  If rank == 1(4), the downscaled pixels
    ///     in pixd are the min(max) of the corresponding set of
    ///     4 pixels in pixs.  Values 2 and 3 are intermediate.
    /// (2) This is the grayscale analog to the binary rank scaling operation
    ///     pixReduceRankBinary2().  Here, because of the photometric
    ///     definition that higher gray values are lighter, rank 1 gives
    ///     the darkest pixel, whereas rank 4 gives the lightest pixel.
    ///     This is opposite to the binary rank operation.
    /// (3) For rank = 1 and 4, this calls pixScaleGrayMinMax2(),
    ///     which runs at about 70 MPix/sec/GHz of source data.
    ///     For rank 2 and 3, this runs 3x slower, at about 25 MPix/sec/GHz.
    /// </remarks>
    public Pix ScaleGrayRank2(int rank)
    {
        const string func = "pixScaleGrayRank2";
        int i, j, k, m;
        int rankval;
        int[] val = new int[4];
        int[] midval = new int[4];  /* should only use 2 of these */

        if (Depth != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs undefined, not 8 bpp, or cmapped");
        if (rank < 1 || rank > 4)
            throw Lept.Arg(func, "invalid rank");

        if (rank == 1)
            return ScaleGrayMinMax2(L.L_CHOOSE_MIN);
        if (rank == 4)
            return ScaleGrayMinMax2(L.L_CHOOSE_MAX);

        GetDimensions(out int ws, out int hs, out _);
        int wd = ws / 2;
        int hd = hs / 2;
        Pix pixd = Create(wd, hd, 8);
        pixd.CopyInputFormat(this);
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpls = Wpl;
        int wpld = pixd.Wpl;
        for (i = 0; i < hd; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(2 * i * wpls, wpls);
            ReadOnlySpan<uint> lines1 = datas.AsSpan((2 * i + 1) * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < wd; j++) {
                val[0] = GetDataByte(lines, 2 * j);
                val[1] = GetDataByte(lines, 2 * j + 1);
                val[2] = GetDataByte(lines1, 2 * j);
                val[3] = GetDataByte(lines1, 2 * j + 1);
                int minval, maxval, minindex, maxindex;
                minval = maxval = val[0];
                minindex = maxindex = 0;
                for (k = 1; k < 4; k++) {
                    if (val[k] < minval) {
                        minval = val[k];
                        minindex = k;
                        continue;
                    }
                    if (val[k] > maxval) {
                        maxval = val[k];
                        maxindex = k;
                    }
                }
                for (k = 0, m = 0; k < 4; k++) {
                    if (k == minindex || k == maxindex)
                        continue;
                    midval[m++] = val[k];
                }
                if (m > 2)  /* minval == maxval; all val[k] are the same */
                    rankval = minval;
                else if (rank == 2)
                    rankval = Math.Min(midval[0], midval[1]);
                else  /* rank == 3 */
                    rankval = Math.Max(midval[0], midval[1]);
                SetDataByte(lined, j, rankval);
            }
        }

        return pixd;
    }

    /*------------------------------------------------------------------------*
     *           Helper function for transferring alpha with scaling          *
     *------------------------------------------------------------------------*/
    // leptonica: pixScaleAndTransferAlpha (scale2.c)
    /// <summary>Scales the alpha channel of pixs and sets it in this 32 bpp pix.</summary>
    /// <param name="pixs">32 bpp, original unscaled image, spp == 4</param>
    /// <param name="scalex">must be &gt; 0.0</param>
    /// <param name="scaley">must be &gt; 0.0</param>
    /// <remarks>
    /// (1) This scales the alpha component of pixs and inserts into pixd.
    /// </remarks>
    public void ScaleAndTransferAlpha(Pix pixs, float scalex, float scaley)
    {
        const string func = "pixScaleAndTransferAlpha";

        if (pixs.Depth != 32 || pixs.Spp != 4)
            throw Lept.Arg(func, "pixs not 32 bpp and 4 spp");
        if (Depth != 32)
            throw Lept.Arg(func, "pixd not 32 bpp");

        if (scalex == 1.0f && scaley == 1.0f) {
            CopyRGBComponent(pixs, L.L_ALPHA_CHANNEL);
            return;
        }

        Pix pix1 = pixs.GetRGBComponent(L.L_ALPHA_CHANNEL);
        Pix pix2 = pix1.Scale(scalex, scaley);
        SetRGBComponent(pix2, L.L_ALPHA_CHANNEL);
    }

    /*------------------------------------------------------------------------*
     *    RGB scaling including alpha (blend) component and gamma transform   *
     *------------------------------------------------------------------------*/
    // leptonica: pixScaleWithAlpha (scale2.c)
    /// <summary>Scales a 32 bpp rgb or cmapped pix, with a scaled alpha layer added.</summary>
    /// <param name="scalex">must be &gt; 0.0</param>
    /// <param name="scaley">must be &gt; 0.0</param>
    /// <param name="pixg">[optional] 8 bpp, can be null</param>
    /// <param name="fract">between 0.0 and 1.0, with 0.0 fully transparent
    /// and 1.0 fully opaque</param>
    /// <returns>pixd 32 bpp rgba</returns>
    /// <remarks>
    /// (1) The alpha channel is transformed separately from pixs,
    ///     and aligns with it, being fully transparent outside the
    ///     boundary of the transformed pixs.  For pixels that are fully
    ///     transparent, a blending function like pixBlendWithGrayMask()
    ///     will give zero weight to corresponding pixels in pixs.
    /// (2) Scaling is done with area mapping or linear interpolation,
    ///     depending on the scale factors.  Default sharpening is done.
    /// (3) If pixg is NULL, it is generated as an alpha layer that is
    ///     partially opaque, using %fract.  Otherwise, it is cropped
    ///     to pixs if required, and %fract is ignored.  The alpha
    ///     channel in pixs is never used.
    /// (4) Colormaps are removed to 32 bpp.
    /// (5) The default setting for the border values in the alpha channel
    ///     is 0 (transparent) for the outermost ring of pixels and
    ///     (0.5 * fract * 255) for the second ring.  When blended over
    ///     a second image, this
    ///     (a) shrinks the visible image to make a clean overlap edge
    ///         with an image below, and
    ///     (b) softens the edges by weakening the aliasing there.
    ///     Use l_setAlphaMaskBorder() to change these values.
    /// (6) A subtle use of gamma correction is to remove gamma correction
    ///     before scaling and restore it afterwards.  This is done
    ///     by sandwiching this function between a gamma/inverse-gamma
    ///     photometric transform:
    ///         pixt = pixGammaTRCWithAlpha(NULL, pixs, 1.0 / gamma, 0, 255);
    ///         pixd = pixScaleWithAlpha(pixt, scalex, scaley, NULL, fract);
    ///         pixGammaTRCWithAlpha(pixd, pixd, gamma, 0, 255);
    ///         pixDestroy(&amp;pixt);
    ///     This has the side-effect of producing artifacts in the very
    ///     dark regions.
    /// </remarks>
    public Pix ScaleWithAlpha(float scalex, float scaley, Pix? pixg, float fract)
    {
        const string func = "pixScaleWithAlpha";
        Pix pixd, pix32, pixg2, pixgs;

        GetDimensions(out int ws, out int hs, out int d);
        if (d != 32 && Colormap == null)
            throw Lept.Arg(func, "pixs not cmapped or 32 bpp");
        if (scalex <= 0.0f || scaley <= 0.0f)
            throw Lept.Arg(func, "scale factor <= 0.0");
        if (pixg != null && pixg.Depth != 8) {
            Log.Warning(func, "pixg not 8 bpp; using 'fract' transparent alpha");
            pixg = null;
        }
        if (pixg == null && (fract < 0.0f || fract > 1.0f)) {
            Log.Warning(func, "invalid fract; using fully opaque");
            fract = 1.0f;
        }
        if (pixg == null && fract == 0.0f)
            Log.Warning(func, "transparent alpha; image will not be blended");

            /* Make sure input to scaling is 32 bpp rgb, and scale it */
        if (d != 32)
            pix32 = ConvertTo32();
        else
            pix32 = Clone(this);
        int spp = pix32.Spp;
        pix32.Spp = 3;  /* ignore the alpha channel for scaling */
        try {
            pixd = pix32.Scale(scalex, scaley);
        } finally {
            pix32.Spp = spp;  /* restore initial value in case it's a clone */
        }

            /* Set up alpha layer with a fading border and scale it */
        if (pixg == null) {
            pixg2 = Create(ws, hs, 8);
            if (fract == 1.0f)
                pixg2.SetAll();
            else if (fract > 0.0f)
                pixg2.SetAllArbitrary((uint)(int)(255 * fract));
        } else {
            pixg2 = pixg.ResizeToMatch(null, ws, hs);
        }
        if (ws > 10 && hs > 10) {  /* see note 4 */
            pixg2.SetBorderRingVal(1,
                                   (uint)(int)(255 * fract * AlphaMaskBorderVals[0]));
            pixg2.SetBorderRingVal(2,
                                   (uint)(int)(255 * fract * AlphaMaskBorderVals[1]));
        }
        pixgs = pixg2.ScaleGeneral(scalex, scaley, 0.0f, 0);

            /* Combine into a 4 spp result */
        pixd.SetRGBComponent(pixgs, L.L_ALPHA_CHANNEL);
        pixd.CopyInputFormat(this);
        return pixd;
    }

    /* ================================================================ *
     *                    Low level static functions                    *
     * ================================================================ */

    /*------------------------------------------------------------------*
     *                         Scale-to-gray 2x                         *
     *------------------------------------------------------------------*/
    // leptonica: scaleToGray2Low (scale2.c)
    /// <summary>Scale-to-gray 2x low-level function.</summary>
    /// <param name="datad">dest data</param>
    /// <param name="wd">dest width</param>
    /// <param name="hd">dest height</param>
    /// <param name="wpld">dest words/line</param>
    /// <param name="datas">src data</param>
    /// <param name="wpls">src words/line</param>
    /// <param name="sumtab">made from makeSumTabSG2()</param>
    /// <param name="valtab">made from makeValTabSG2()</param>
    /// <remarks>
    /// (1) The output is processed in sets of 4 output bytes on a row,
    ///     corresponding to 4 2x2 bit-blocks in the input image.
    ///     Two lookup tables are used.  The first, sumtab, gets the
    ///     sum of ON pixels in 4 sets of two adjacent bits,
    ///     storing the result in 4 adjacent bytes.  After sums from
    ///     two rows have been added, the second table, valtab,
    ///     converts from the sum of ON pixels in the 2x2 block to
    ///     an 8 bpp grayscale value between 0 for 4 bits ON
    ///     and 255 for 0 bits ON.
    /// </remarks>
    private static void ScaleToGray2Low(uint[] datad, int wd, int hd, int wpld,
                                        uint[] datas, int wpls,
                                        uint[] sumtab, byte[] valtab)
    {
        int wd4 = wd & unchecked((int)0xfffffffc);
        int extra = wd - wd4;
        LeptParallel.ForRows(hd, wd * 2, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            ref uint sumRef = ref MemoryMarshal.GetArrayDataReference(sumtab);
            ref byte valRef = ref MemoryMarshal.GetArrayDataReference(valtab);
            for (int i = yStart; i < yEnd; i++) {
                int l = 2 * i;
                ref uint line0Ref = ref Unsafe.Add(ref datasRef, l * wpls);
                ref uint line1Ref = ref Unsafe.Add(ref line0Ref, wpls);
                ref uint dstLineRef = ref Unsafe.Add(ref datadRef, i * wpld);
                int j = 0, k = 0;
                for (; j < wd4; j += 4, k++) {
                    int s1 = GetDataByteUnchecked(ref line0Ref, k);
                    int s2 = GetDataByteUnchecked(ref line1Ref, k);
                    uint sum = Unsafe.Add(ref sumRef, s1) + Unsafe.Add(ref sumRef, s2);
                    uint b0 = Unsafe.Add(ref valRef, (int)(sum >> 24));
                    uint b1 = Unsafe.Add(ref valRef, (int)((sum >> 16) & 0xffu));
                    uint b2 = Unsafe.Add(ref valRef, (int)((sum >> 8) & 0xffu));
                    uint b3 = Unsafe.Add(ref valRef, (int)(sum & 0xffu));
                    Unsafe.Add(ref dstLineRef, j >> 2) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                }
                if (extra > 0) {
                    Unsafe.Add(ref dstLineRef, wd >> 2) = 0;
                    int s1 = GetDataByteUnchecked(ref line0Ref, k);
                    int s2 = GetDataByteUnchecked(ref line1Ref, k);
                    uint sum = Unsafe.Add(ref sumRef, s1) + Unsafe.Add(ref sumRef, s2);
                    for (int m = 0; m < extra; m++)
                        SetDataByteUnchecked(ref dstLineRef, j + m, Unsafe.Add(ref valRef, (int)((sum >> (24 - 8 * m)) & 0xffu)));
                }
            }
        });
    }

    // leptonica: makeSumTabSG2 (scale2.c)
    /// <remarks>
    /// (1) Returns a table of 256 l_uint32s, giving the four output
    ///     8-bit grayscale sums corresponding to 8 input bits of a binary
    ///     image, for a 2x scale-to-gray op.  The sums from two
    ///     adjacent scanlines are then added and transformed to
    ///     output four 8 bpp pixel values, using makeValTabSG2().
    /// </remarks>
    private static uint[] MakeSumTabSG2()
    {
        int i;
        int[] sum = { 0, 1, 1, 2 };

            /* Pack the four sums separately in four bytes */
        uint[] tab = new uint[256];
        for (i = 0; i < 256; i++) {
            tab[i] = (uint)(sum[i & 0x3] | sum[(i >> 2) & 0x3] << 8 |
                            sum[(i >> 4) & 0x3] << 16 | sum[(i >> 6) & 0x3] << 24);
        }
        return tab;
    }

    // leptonica: makeValTabSG2 (scale2.c)
    /// <remarks>
    /// (1) Returns an 8 bit value for the sum of ON pixels
    ///     in a 2x2 square, according to
    ///          val = 255 - (255 * sum)/4
    ///     where sum is in set {0,1,2,3,4}
    /// </remarks>
    private static byte[] MakeValTabSG2()
    {
        int i;

        byte[] tab = new byte[5];
        for (i = 0; i < 5; i++)
            tab[i] = (byte)(255 - (i * 255) / 4);
        return tab;
    }

    /*------------------------------------------------------------------*
     *                         Scale-to-gray 3x                         *
     *------------------------------------------------------------------*/
    // leptonica: scaleToGray3Low (scale2.c)
    /// <summary>Scale-to-gray 3x low-level function.</summary>
    /// <param name="datad">dest data</param>
    /// <param name="wd">dest width</param>
    /// <param name="hd">dest height</param>
    /// <param name="wpld">dest words/line</param>
    /// <param name="datas">src data</param>
    /// <param name="wpls">src words/line</param>
    /// <param name="sumtab">made from makeSumTabSG3()</param>
    /// <param name="valtab">made from makeValTabSG3()</param>
    /// <remarks>
    /// (1) Each set of 8 3x3 bit-blocks in the source image, which
    ///     consist of 72 pixels arranged 24 pixels wide by 3 scanlines,
    ///     is converted to a row of 8 8-bit pixels in the dest image.
    ///     These 72 pixels of the input image are runs of 24 pixels
    ///     in three adjacent scanlines.  Each run of 24 pixels is
    ///     stored in the 24 LSbits of a 32-bit word.  We use 2 LUTs.
    ///     The first, sumtab, takes 6 of these bits and stores
    ///     sum, taken 3 bits at a time, in two bytes.  (See
    ///     makeSumTabSG3).  This is done for each of the 3 scanlines,
    ///     and the results are added.  We now have the sum of ON pixels
    ///     in the first two 3x3 blocks in two bytes.  The valtab LUT
    ///     then converts these values (which go from 0 to 9) to
    ///     grayscale values between between 255 and 0.  (See makeValTabSG3).
    ///     This process is repeated for each of the other 3 sets of
    ///     6x3 input pixels, giving 8 output pixels in total.
    /// (2) Note: because the input image is processed in groups of
    ///      24 x 3 pixels, the process clips the input height to
    ///      (h - h % 3) and the input width to (w - w % 24).
    /// </remarks>
    private static void ScaleToGray3Low(uint[] datad, int wd, int hd, int wpld,
                                        uint[] datas, int wpls,
                                        uint[] sumtab, byte[] valtab)
    {
        int i, j, l, k;
        uint threebytes1, threebytes2, threebytes3, sum;

            /* i indexes the dest lines
             * l indexes the source lines
             * j indexes the dest bytes
             * k indexes the source bytes
             * We take 9 bytes from the source (72 binary pixels
             * in three lines of 24 pixels each) and convert it
             * into 8 bytes of the dest (8 8bpp pixels in one line)   */
        for (i = 0, l = 0; i < hd; i++, l += 3) {
            ReadOnlySpan<uint> lines = datas.AsSpan(l * wpls, wpls);
            ReadOnlySpan<uint> lines1 = datas.AsSpan((l + 1) * wpls, wpls);
            ReadOnlySpan<uint> lines2 = datas.AsSpan((l + 2) * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0, k = 0; j < wd; j += 8, k += 3) {
                threebytes1 = (uint)((GetDataByte(lines, k) << 16) |
                                     (GetDataByte(lines, k + 1) << 8) |
                                     GetDataByte(lines, k + 2));
                threebytes2 = (uint)((GetDataByte(lines1, k) << 16) |
                                     (GetDataByte(lines1, k + 1) << 8) |
                                     GetDataByte(lines1, k + 2));
                threebytes3 = (uint)((GetDataByte(lines2, k) << 16) |
                                     (GetDataByte(lines2, k + 1) << 8) |
                                     GetDataByte(lines2, k + 2));

                sum = sumtab[(threebytes1 >> 18)] +
                      sumtab[(threebytes2 >> 18)] +
                      sumtab[(threebytes3 >> 18)];
                SetDataByte(lined, j, valtab[GetDataByte(sum, 2)]);
                SetDataByte(lined, j + 1, valtab[GetDataByte(sum, 3)]);

                sum = sumtab[((threebytes1 >> 12) & 0x3f)] +
                      sumtab[((threebytes2 >> 12) & 0x3f)] +
                      sumtab[((threebytes3 >> 12) & 0x3f)];
                SetDataByte(lined, j + 2, valtab[GetDataByte(sum, 2)]);
                SetDataByte(lined, j + 3, valtab[GetDataByte(sum, 3)]);

                sum = sumtab[((threebytes1 >> 6) & 0x3f)] +
                      sumtab[((threebytes2 >> 6) & 0x3f)] +
                      sumtab[((threebytes3 >> 6) & 0x3f)];
                SetDataByte(lined, j + 4, valtab[GetDataByte(sum, 2)]);
                SetDataByte(lined, j + 5, valtab[GetDataByte(sum, 3)]);

                sum = sumtab[(threebytes1 & 0x3f)] +
                      sumtab[(threebytes2 & 0x3f)] +
                      sumtab[(threebytes3 & 0x3f)];
                SetDataByte(lined, j + 6, valtab[GetDataByte(sum, 2)]);
                SetDataByte(lined, j + 7, valtab[GetDataByte(sum, 3)]);
            }
        }
    }

    // leptonica: makeSumTabSG3 (scale2.c)
    /// <remarks>
    /// (1) Returns a table of 64 l_uint32s, giving the two output
    ///     8-bit grayscale sums corresponding to 6 input bits of a binary
    ///     image, for a 3x scale-to-gray op.  In practice, this would
    ///     be used three times (on adjacent scanlines), and the sums would
    ///     be added and then transformed to output 8 bpp pixel values,
    ///     using makeValTabSG3().
    /// </remarks>
    private static uint[] MakeSumTabSG3()
    {
        int i;
        int[] sum = { 0, 1, 1, 2, 1, 2, 2, 3 };

            /* Pack the two sums separately in two bytes */
        uint[] tab = new uint[64];
        for (i = 0; i < 64; i++) {
            tab[i] = (uint)((sum[i & 0x07]) | (sum[(i >> 3) & 0x07] << 8));
        }
        return tab;
    }

    // leptonica: makeValTabSG3 (scale2.c)
    /// <remarks>
    /// (1) Returns an 8 bit value for the sum of ON pixels
    ///     in a 3x3 square, according to
    ///          val = 255 - (255 * sum)/9
    ///     where sum is in [0,...,9]
    /// </remarks>
    private static byte[] MakeValTabSG3()
    {
        int i;

        byte[] tab = new byte[10];
        for (i = 0; i < 10; i++)
            tab[i] = (byte)(0xff - (i * 255) / 9);
        return tab;
    }

    /*------------------------------------------------------------------*
     *                         Scale-to-gray 4x                         *
     *------------------------------------------------------------------*/
    // leptonica: scaleToGray4Low (scale2.c)
    /// <summary>Scale-to-gray 4x low-level function.</summary>
    /// <param name="datad">dest data</param>
    /// <param name="wd">dest width</param>
    /// <param name="hd">dest height</param>
    /// <param name="wpld">dest words/line</param>
    /// <param name="datas">src data</param>
    /// <param name="wpls">src words/line</param>
    /// <param name="sumtab">made from makeSumTabSG4()</param>
    /// <param name="valtab">made from makeValTabSG4()</param>
    /// <remarks>
    /// (1) The output is processed in sets of 2 output bytes on a row,
    ///     corresponding to 2 4x4 bit-blocks in the input image.
    ///     Two lookup tables are used.  The first, sumtab, gets the
    ///     sum of ON pixels in two sets of four adjacent bits,
    ///     storing the result in 2 adjacent bytes.  After sums from
    ///     four rows have been added, the second table, valtab,
    ///     converts from the sum of ON pixels in the 4x4 block to
    ///     an 8 bpp grayscale value between 0 for 16 bits ON
    ///     and 255 for 0 bits ON.
    /// </remarks>
    internal static void ScaleToGray4Low(uint[] datad, int wd, int hd, int wpld,
                                         uint[] datas, int wpls,
                                         uint[] sumtab, byte[] valtab)
    {
        int i, j, l, k;
        uint sbyte1, sbyte2, sbyte3, sbyte4, sum;

            /* Number of groups of 8 dest words (4 source words per line)
             * that are handled with 128-bit vectors; the rest of each
             * row goes through the scalar table code below. */
        int nvec = (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian
                    && valtab.Length >= 16) ? (wd >> 2) >> 3 : 0;
        Vector128<byte> valvec = nvec > 0 ? Vector128.Create(valtab.AsSpan(0, 16)) : default;

            /* i indexes the dest lines
             * l indexes the source lines
             * j indexes the dest bytes
             * k indexes the source bytes
             * We take four bytes from the source (in 4 lines of 8 pixels
             * each) and convert it into two 8 bpp bytes of the dest. */
        for (i = 0, l = 0; i < hd; i++, l += 4) {
            ReadOnlySpan<uint> lines = datas.AsSpan(l * wpls, wpls);
            ReadOnlySpan<uint> lines1 = datas.AsSpan((l + 1) * wpls, wpls);
            ReadOnlySpan<uint> lines2 = datas.AsSpan((l + 2) * wpls, wpls);
            ReadOnlySpan<uint> lines3 = datas.AsSpan((l + 3) * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            j = 0;
            k = 0;
            if (nvec > 0) {
                ref uint r0 = ref MemoryMarshal.GetReference(lines);
                ref uint r1 = ref MemoryMarshal.GetReference(lines1);
                ref uint r2 = ref MemoryMarshal.GetReference(lines2);
                ref uint r3 = ref MemoryMarshal.GetReference(lines3);
                ref uint rd = ref MemoryMarshal.GetReference(lined);
                for (int g = 0; g < nvec; g++) {
                    nuint so = (nuint)(g * 4);
                    ScaleToGray4Vec(ref r0, ref r1, ref r2, ref r3, so, valvec,
                                    ref rd, (nuint)(g * 8));
                }
                j = nvec * 32;
                k = nvec * 16;
            }
            for (; j + 3 < wd; j += 4, k += 2) {
                sbyte1 = (uint)GetDataByte(lines, k);
                sbyte2 = (uint)GetDataByte(lines1, k);
                sbyte3 = (uint)GetDataByte(lines2, k);
                sbyte4 = (uint)GetDataByte(lines3, k);
                sum = sumtab[sbyte1] + sumtab[sbyte2] +
                      sumtab[sbyte3] + sumtab[sbyte4];
                uint p0 = valtab[GetDataByte(sum, 2)];
                uint p1 = valtab[GetDataByte(sum, 3)];

                sbyte1 = (uint)GetDataByte(lines, k + 1);
                sbyte2 = (uint)GetDataByte(lines1, k + 1);
                sbyte3 = (uint)GetDataByte(lines2, k + 1);
                sbyte4 = (uint)GetDataByte(lines3, k + 1);
                sum = sumtab[sbyte1] + sumtab[sbyte2] +
                      sumtab[sbyte3] + sumtab[sbyte4];
                uint p2 = valtab[GetDataByte(sum, 2)];
                uint p3 = valtab[GetDataByte(sum, 3)];
                lined[j >> 2] = (p0 << 24) | (p1 << 16) | (p2 << 8) | p3;
            }

            if (j < wd) {
                sbyte1 = (uint)GetDataByte(lines, k);
                sbyte2 = (uint)GetDataByte(lines1, k);
                sbyte3 = (uint)GetDataByte(lines2, k);
                sbyte4 = (uint)GetDataByte(lines3, k);
                sum = sumtab[sbyte1] + sumtab[sbyte2] +
                      sumtab[sbyte3] + sumtab[sbyte4];
                SetDataByte(lined, j, valtab[GetDataByte(sum, 2)]);
                SetDataByte(lined, j + 1, valtab[GetDataByte(sum, 3)]);
            }
        }
    }

    /// <summary>Number of ON bits in each nibble of every lane (values 0..4).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> NibbleCounts(Vector128<uint> w)
    {
        Vector128<uint> m1 = Vector128.Create(0x55555555u);
        Vector128<uint> m2 = Vector128.Create(0x33333333u);
        w -= Vector128.ShiftRightLogical(w, 1) & m1;
        return (w & m2) + (Vector128.ShiftRightLogical(w, 2) & m2);
    }

    /// <summary>
    /// Converts 4 source words from each of 4 lines (128 source pixels wide)
    /// into 8 dest words (32 gray pixels).  Same result as the table version:
    /// the ON bits of every 4x4 block are counted and mapped through valtab.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ScaleToGray4Vec(ref uint r0, ref uint r1, ref uint r2, ref uint r3,
                                        nuint so, Vector128<byte> valvec,
                                        ref uint rd, nuint dof)
    {
        Vector128<uint> m4 = Vector128.Create(0x0F0F0F0Fu);
        Vector128<uint> p01 = NibbleCounts(Vector128.LoadUnsafe(ref r0, so))
                            + NibbleCounts(Vector128.LoadUnsafe(ref r1, so));
        Vector128<uint> p23 = NibbleCounts(Vector128.LoadUnsafe(ref r2, so))
                            + NibbleCounts(Vector128.LoadUnsafe(ref r3, so));

            /* Per byte: counts of the low nibble (even) and of the high nibble (odd),
             * each in [0..16] after adding the four lines. */
        Vector128<byte> even = ((p01 & m4) + (p23 & m4)).AsByte();
        Vector128<byte> odd = ((Vector128.ShiftRightLogical(p01, 4) & m4)
                             + (Vector128.ShiftRightLogical(p23, 4) & m4)).AsByte();

            /* Interleave to byte pairs (even, odd); counts of 16 are out of range
             * for the shuffle and give 0, which is the value of valtab[16]. */
        Vector128<ushort> lo = Vector128.WidenLower(even) | Vector128.ShiftLeft(Vector128.WidenLower(odd), 8);
        Vector128<ushort> hi = Vector128.WidenUpper(even) | Vector128.ShiftLeft(Vector128.WidenUpper(odd), 8);
        Vector128<uint> swap = Vector128.Create(1u, 0u, 3u, 2u);
        Vector128<uint> vlo = Vector128.Shuffle(Vector128.Shuffle(valvec, lo.AsByte()).AsUInt32(), swap);
        Vector128<uint> vhi = Vector128.Shuffle(Vector128.Shuffle(valvec, hi.AsByte()).AsUInt32(), swap);
        vlo.StoreUnsafe(ref rd, dof);
        vhi.StoreUnsafe(ref rd, dof + 4);
    }

    // leptonica: makeSumTabSG4 (scale2.c)
    /// <remarks>
    /// (1) Returns a table of 256 l_uint32s, giving the two output
    ///     8-bit grayscale sums corresponding to 8 input bits of a
    ///     binary image, for a 4x scale-to-gray op.  The sums from
    ///     four adjacent scanlines are then added and transformed to
    ///     output 8 bpp pixel values, using makeValTabSG4().
    /// </remarks>
    private static uint[] MakeSumTabSG4()
    {
        int i;
        int[] sum = { 0, 1, 1, 2, 1, 2, 2, 3, 1, 2, 2, 3, 2, 3, 3, 4 };

            /* Pack the two sums separately in two bytes */
        uint[] tab = new uint[256];
        for (i = 0; i < 256; i++) {
            tab[i] = (uint)((sum[i & 0xf]) | (sum[(i >> 4) & 0xf] << 8));
        }
        return tab;
    }

    // leptonica: makeValTabSG4 (scale2.c)
    /// <remarks>
    /// (1) Returns an 8 bit value for the sum of ON pixels
    ///     in a 4x4 square, according to
    ///         val = 255 - (255 * sum)/16
    ///     where sum is in [0,...,16]
    /// </remarks>
    private static byte[] MakeValTabSG4()
    {
        int i;

        byte[] tab = new byte[17];
        for (i = 0; i < 17; i++)
            tab[i] = (byte)(0xff - (i * 255) / 16);
        return tab;
    }

    /*------------------------------------------------------------------*
     *                         Scale-to-gray 6x                         *
     *------------------------------------------------------------------*/
    // leptonica: scaleToGray6Low (scale2.c)
    /// <summary>Scale-to-gray 6x low-level function.</summary>
    /// <param name="datad">dest data</param>
    /// <param name="wd">dest width</param>
    /// <param name="hd">dest height</param>
    /// <param name="wpld">dest words/line</param>
    /// <param name="datas">src data</param>
    /// <param name="wpls">src words/line</param>
    /// <param name="tab8">made from makePixelSumTab8()</param>
    /// <param name="valtab">made from makeValTabSG6()</param>
    /// <remarks>
    /// (1) Each set of 4 6x6 bit-blocks in the source image, which
    ///     consist of 144 pixels arranged 24 pixels wide by 6 scanlines,
    ///     is converted to a row of 4 8-bit pixels in the dest image.
    ///     These 144 pixels of the input image are runs of 24 pixels
    ///     in six adjacent scanlines.  Each run of 24 pixels is
    ///     stored in the 24 LSbits of a 32-bit word.  We use 2 LUTs.
    ///     The first, tab8, takes 6 of these bits and stores
    ///     sum in one byte.  This is done for each of the 6 scanlines,
    ///     and the results are added.
    ///     We now have the sum of ON pixels in the first 6x6 block.  The
    ///     valtab LUT then converts these values (which go from 0 to 36) to
    ///     grayscale values between between 255 and 0.  (See makeValTabSG6).
    ///     This process is repeated for each of the other 3 sets of
    ///     6x6 input pixels, giving 4 output pixels in total.
    /// (2) Note: because the input image is processed in groups of
    ///     24 x 6 pixels, the process clips the input height to
    ///     (h - h % 6) and the input width to (w - w % 24).
    /// </remarks>
    private static void ScaleToGray6Low(uint[] datad, int wd, int hd, int wpld,
                                        uint[] datas, int wpls,
                                        int[] tab8, byte[] valtab)
    {
        int i, j, l, k;
        uint threebytes1, threebytes2, threebytes3;
        uint threebytes4, threebytes5, threebytes6, sum;

            /* i indexes the dest lines
             * l indexes the source lines
             * j indexes the dest bytes
             * k indexes the source bytes
             * We take 18 bytes from the source (144 binary pixels
             * in six lines of 24 pixels each) and convert it
             * into 4 bytes of the dest (four 8 bpp pixels in one line)   */
        for (i = 0, l = 0; i < hd; i++, l += 6) {
            ReadOnlySpan<uint> lines = datas.AsSpan(l * wpls, wpls);
            ReadOnlySpan<uint> lines1 = datas.AsSpan((l + 1) * wpls, wpls);
            ReadOnlySpan<uint> lines2 = datas.AsSpan((l + 2) * wpls, wpls);
            ReadOnlySpan<uint> lines3 = datas.AsSpan((l + 3) * wpls, wpls);
            ReadOnlySpan<uint> lines4 = datas.AsSpan((l + 4) * wpls, wpls);
            ReadOnlySpan<uint> lines5 = datas.AsSpan((l + 5) * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0, k = 0; j < wd; j += 4, k += 3) {
                    /* First grab the 18 bytes, 3 at a time, and put each set
                     * of 3 bytes into the LS bytes of a 32-bit word. */
                threebytes1 = (uint)((GetDataByte(lines, k) << 16) |
                                     (GetDataByte(lines, k + 1) << 8) |
                                     GetDataByte(lines, k + 2));
                threebytes2 = (uint)((GetDataByte(lines1, k) << 16) |
                                     (GetDataByte(lines1, k + 1) << 8) |
                                     GetDataByte(lines1, k + 2));
                threebytes3 = (uint)((GetDataByte(lines2, k) << 16) |
                                     (GetDataByte(lines2, k + 1) << 8) |
                                     GetDataByte(lines2, k + 2));
                threebytes4 = (uint)((GetDataByte(lines3, k) << 16) |
                                     (GetDataByte(lines3, k + 1) << 8) |
                                     GetDataByte(lines3, k + 2));
                threebytes5 = (uint)((GetDataByte(lines4, k) << 16) |
                                     (GetDataByte(lines4, k + 1) << 8) |
                                     GetDataByte(lines4, k + 2));
                threebytes6 = (uint)((GetDataByte(lines5, k) << 16) |
                                     (GetDataByte(lines5, k + 1) << 8) |
                                     GetDataByte(lines5, k + 2));

                    /* Sum first set of 36 bits and convert to 0-255 */
                sum = (uint)(tab8[(threebytes1 >> 18)] +
                             tab8[(threebytes2 >> 18)] +
                             tab8[(threebytes3 >> 18)] +
                             tab8[(threebytes4 >> 18)] +
                             tab8[(threebytes5 >> 18)] +
                             tab8[(threebytes6 >> 18)]);
                SetDataByte(lined, j, valtab[GetDataByte(sum, 3)]);

                    /* Ditto for second set */
                sum = (uint)(tab8[((threebytes1 >> 12) & 0x3f)] +
                             tab8[((threebytes2 >> 12) & 0x3f)] +
                             tab8[((threebytes3 >> 12) & 0x3f)] +
                             tab8[((threebytes4 >> 12) & 0x3f)] +
                             tab8[((threebytes5 >> 12) & 0x3f)] +
                             tab8[((threebytes6 >> 12) & 0x3f)]);
                SetDataByte(lined, j + 1, valtab[GetDataByte(sum, 3)]);

                sum = (uint)(tab8[((threebytes1 >> 6) & 0x3f)] +
                             tab8[((threebytes2 >> 6) & 0x3f)] +
                             tab8[((threebytes3 >> 6) & 0x3f)] +
                             tab8[((threebytes4 >> 6) & 0x3f)] +
                             tab8[((threebytes5 >> 6) & 0x3f)] +
                             tab8[((threebytes6 >> 6) & 0x3f)]);
                SetDataByte(lined, j + 2, valtab[GetDataByte(sum, 3)]);

                sum = (uint)(tab8[(threebytes1 & 0x3f)] +
                             tab8[(threebytes2 & 0x3f)] +
                             tab8[(threebytes3 & 0x3f)] +
                             tab8[(threebytes4 & 0x3f)] +
                             tab8[(threebytes5 & 0x3f)] +
                             tab8[(threebytes6 & 0x3f)]);
                SetDataByte(lined, j + 3, valtab[GetDataByte(sum, 3)]);
            }
        }
    }

    // leptonica: makeValTabSG6 (scale2.c)
    /// <remarks>
    /// (1) Returns an 8 bit value for the sum of ON pixels
    ///     in a 6x6 square, according to
    ///         val = 255 - (255 * sum)/36
    ///     where sum is in [0,...,36]
    /// </remarks>
    private static byte[] MakeValTabSG6()
    {
        int i;

        byte[] tab = new byte[37];
        for (i = 0; i < 37; i++)
            tab[i] = (byte)(0xff - (i * 255) / 36);
        return tab;
    }

    /*------------------------------------------------------------------*
     *                         Scale-to-gray 8x                         *
     *------------------------------------------------------------------*/
    // leptonica: scaleToGray8Low (scale2.c)
    /// <summary>Scale-to-gray 8x low-level function.</summary>
    /// <param name="datad">dest data</param>
    /// <param name="wd">dest width</param>
    /// <param name="hd">dest height</param>
    /// <param name="wpld">dest words/line</param>
    /// <param name="datas">src data</param>
    /// <param name="wpls">src words/line</param>
    /// <param name="tab8">made from makePixelSumTab8()</param>
    /// <param name="valtab">made from makeValTabSG8()</param>
    /// <remarks>
    /// (1) The output is processed one dest byte at a time,
    ///     corresponding to 8 rows of src bytes in the input image.
    ///     Two lookup tables are used.  The first, %tab8, gets the
    ///     sum of ON pixels in a byte.  After sums from 8 rows have
    ///     been added, the second table, %valtab, converts from this
    ///     value which is between 0 and 64 to an 8 bpp grayscale
    ///     value between 0 and 255: 0 for all 64 bits ON and 255
    ///     for all 64 bits OFF.
    /// </remarks>
    private static void ScaleToGray8Low(uint[] datad, int wd, int hd, int wpld,
                                        uint[] datas, int wpls,
                                        int[] tab8, byte[] valtab)
    {
        _ = tab8;
        LeptParallel.ForRows(hd, wd * 8, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            ref byte valRef = ref MemoryMarshal.GetArrayDataReference(valtab);
            for (int i = yStart; i < yEnd; i++) {
                int k = 8 * i;
                ref uint l0 = ref Unsafe.Add(ref datasRef, k * wpls);
                ref uint l1 = ref Unsafe.Add(ref l0, wpls);
                ref uint l2 = ref Unsafe.Add(ref l1, wpls);
                ref uint l3 = ref Unsafe.Add(ref l2, wpls);
                ref uint l4 = ref Unsafe.Add(ref l3, wpls);
                ref uint l5 = ref Unsafe.Add(ref l4, wpls);
                ref uint l6 = ref Unsafe.Add(ref l5, wpls);
                ref uint l7 = ref Unsafe.Add(ref l6, wpls);
                ref uint dstLineRef = ref Unsafe.Add(ref datadRef, i * wpld);
                if ((wd & 3) != 0)
                    Unsafe.Add(ref dstLineRef, wd >> 2) = 0;
                for (int j = 0; j < wd; j++) {
                    uint lo4 = (uint)(GetDataByteUnchecked(ref l0, j) |
                                      (GetDataByteUnchecked(ref l1, j) << 8) |
                                      (GetDataByteUnchecked(ref l2, j) << 16) |
                                      (GetDataByteUnchecked(ref l3, j) << 24));
                    uint hi4 = (uint)(GetDataByteUnchecked(ref l4, j) |
                                      (GetDataByteUnchecked(ref l5, j) << 8) |
                                      (GetDataByteUnchecked(ref l6, j) << 16) |
                                      (GetDataByteUnchecked(ref l7, j) << 24));
                    int sum = BitOperations.PopCount(((ulong)hi4 << 32) | lo4);
                    SetDataByteUnchecked(ref dstLineRef, j, Unsafe.Add(ref valRef, sum));
                }
            }
        });
    }

    // leptonica: makeValTabSG8 (scale2.c)
    /// <remarks>
    /// (1) Returns an 8 bit value for the sum of ON pixels
    ///     in an 8x8 square, according to
    ///         val = 255 - (255 * sum)/64
    ///     where sum is in [0,...,64]
    /// </remarks>
    private static byte[] MakeValTabSG8()
    {
        int i;

        byte[] tab = new byte[65];
        for (i = 0; i < 65; i++)
            tab[i] = (byte)(0xff - (i * 255) / 64);
        return tab;
    }

    /*------------------------------------------------------------------*
     *                         Scale-to-gray 16x                        *
     *------------------------------------------------------------------*/
    // leptonica: scaleToGray16Low (scale2.c)
    /// <summary>Scale-to-gray 16x low-level function.</summary>
    /// <param name="datad">dest data</param>
    /// <param name="wd">dest width</param>
    /// <param name="hd">dest height</param>
    /// <param name="wpld">dest words/line</param>
    /// <param name="datas">src data</param>
    /// <param name="wpls">src words/line</param>
    /// <param name="tab8">made from makePixelSumTab8()</param>
    /// <remarks>
    /// (1) The output is processed one dest byte at a time, corresponding
    ///     to 16 rows consisting each of 2 src bytes in the input image.
    ///     This uses one lookup table, tab8, which gives the sum of
    ///     ON pixels in a byte.  After summing for all ON pixels in the
    ///     32 src bytes, which is between 0 and 256, this is converted
    ///     to an 8 bpp grayscale value between 0 for 255 or 256 bits ON
    ///     and 255 for 0 bits ON.
    /// </remarks>
    private static void ScaleToGray16Low(uint[] datad, int wd, int hd, int wpld,
                                         uint[] datas, int wpls, int[] tab8)
    {
        int i, j, k, m, r;
        int sum;

            /* i indexes the dest lines
             * k indexes the source lines
             * j indexes the dest bytes
             * m indexes the src bytes
             * We take 32 bytes from the source (in 16 lines of 16 pixels
             * each) and convert it into one 8 bpp byte of the dest. */
        for (i = 0, k = 0; i < hd; i++, k += 16) {
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < wd; j++) {
                m = 2 * j;
                sum = 0;
                for (r = 0; r < 16; r++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan((k + r) * wpls, wpls);
                    sum += tab8[GetDataByte(lines, m)];
                    sum += tab8[GetDataByte(lines, m + 1)];
                }
                sum = Math.Min(sum, 255);
                SetDataByte(lined, j, 255 - sum);
            }
        }
    }

    /*------------------------------------------------------------------*
     *                         Grayscale mipmap                         *
     *------------------------------------------------------------------*/
    // leptonica: scaleMipmapLow (scale2.c)
    /// <remarks>
    /// (1) See notes in scale.c for pixScaleToGrayMipmap().  This function
    ///     is here for pedagogical reasons.  It gives poor results on document
    ///     images because of aliasing.
    /// </remarks>
    private static void ScaleMipmapLow(uint[] datad, int wd, int hd, int wpld,
                                       uint[] datas1, int wpls1,
                                       uint[] datas2, int wpls2, float red)
    {
        int i, j, val1, val2, val, row2, col2;
        float ratio, w1, w2;

            /* Clear dest */
        Array.Clear(datad, 0, wpld * hd);

            /* Each dest pixel at (j,i) is computed by interpolating
               between the two src images at the corresponding location.
               We store the UL corner locations of the square of
               src pixels in thelower-resolution image that correspond
               to dest pixel (j,i).  The are labeled by the arrays
               srow[i], scol[j].  The UL corner locations of the higher
               resolution src pixels are obtained from these arrays
               by multiplying by 2. */
        int[] srow = new int[hd];
        int[] scol = new int[wd];
        ratio = 1.0f / (2.0f * red);  /* 0.5 for red = 1, 1 for red = 0.5 */
        for (i = 0; i < hd; i++)
            srow[i] = (int)(ratio * i);
        for (j = 0; j < wd; j++)
            scol[j] = (int)(ratio * j);

            /* Get weights for linear interpolation: these are the
             * 'distances' of the dest image plane from the two
             * src image planes. */
        w1 = 2.0f * red - 1.0f;   /* w1 --> 1 as red --> 1 */
        w2 = 1.0f - w1;

            /* For each dest pixel, compute linear interpolation */
        for (i = 0; i < hd; i++) {
            row2 = srow[i];
            ReadOnlySpan<uint> lines1 = datas1.AsSpan(2 * row2 * wpls1, wpls1);
            ReadOnlySpan<uint> lines2 = datas2.AsSpan(row2 * wpls2, wpls2);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < wd; j++) {
                col2 = scol[j];
                val1 = GetDataByte(lines1, 2 * col2);
                val2 = GetDataByte(lines2, col2);
                val = (int)(w1 * val1 + w2 * val2);
                SetDataByte(lined, j, val);
            }
        }
    }

}
