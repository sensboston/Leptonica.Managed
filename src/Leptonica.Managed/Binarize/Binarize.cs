using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  binarize.c: Special binarization methods, locally adaptive and global.
 *
 *  Image binarization algorithms are found in:
 *    grayquant.c:   standard, simple, general grayscale quantization
 *    adaptmap.c:    local adaptive; mostly gray-to-gray in preparation
 *                   for binarization
 *    binarize.c:    special binarization methods, locally adaptive and
 *                   global.
 *
 *      Adaptive Otsu-based thresholding
 *          l_int32       pixOtsuAdaptiveThreshold()       8 bpp
 *
 *      Otsu thresholding on adaptive background normalization
 *          PIX          *pixOtsuThreshOnBackgroundNorm()  8 bpp
 *
 *      Masking and Otsu estimate on adaptive background normalization
 *          PIX          *pixMaskedThreshOnBackgroundNorm()  8 bpp
 *
 *      Sauvola local thresholding
 *          l_int32       pixSauvolaBinarizeTiled()
 *          l_int32       pixSauvolaBinarize()
 *          static PIX   *pixSauvolaGetThreshold()
 *          static PIX   *pixApplyLocalThreshold();
 *
 *      Sauvola binarization on contrast normalization
 *          PIX          *pixSauvolaOnContrastNorm()  8 bpp
 *
 *      Contrast normalization followed by bg normalization and thresholding
 *          PIX          *pixThreshOnDoubleNorm()
 *
 *      Global thresholding using connected components
 *          PIX          *pixThresholdByConnComp()
 *
 *      Global thresholding by histogram
 *          PIX          *pixThresholdByHisto()
 *
 *  Notes:
 *      (1) pixOtsuAdaptiveThreshold() computes a global threshold over each
 *          tile and performs the threshold operation, resulting in a
 *          binary image for each tile.  These are stitched into the
 *          final result.
 *      (2) pixOtsuThreshOnBackgroundNorm() and
 *          pixMaskedThreshOnBackgroundNorm() are binarization functions
 *          that use background normalization with other techniques.
 *      (3) Sauvola binarization computes a local threshold based on
 *          the local average and square average.  It takes two constants:
 *          the window size for the measurement at each pixel and a
 *          parameter that determines the amount of normalized local
 *          standard deviation to subtract from the local average value.
 *      (4) pixThresholdByConnComp() uses the numbers of 4 and 8 connected
 *          components at different thresholding to determine if a
 *          global threshold can be used (for text or line-art) and the
 *          value it should have.
 *
 *  Optional output pointers (PIX **) become out parameters.  The functions
 *  that skip work for a NULL output in C (pixOtsuAdaptiveThreshold,
 *  pixSauvolaBinarize, pixSauvolaBinarizeTiled) have a private "Core"
 *  variant with flags telling which outputs are wanted; the public
 *  variant requests all of them.
 *
 *  Stubs for callees in other files: pixTilingCreate, pixTilingGetTile,
 *  pixTilingNoStripOnPaint, pixTilingPaintTile (pixtiling.c),
 *  pixBlockconv, pixWindowedMean, pixWindowedMeanSquare (convolve.c),
 *  pixBackgroundNorm, pixBackgroundNormFlex, pixBackgroundNormSimple,
 *  pixContrastNorm (adaptmap.c), pixCountConnComp (conncomp.c),
 *  pixMorphSequence (morphseq.c), gplotMakeOutput (gplot.c).
 */
public sealed partial class Pix
{
    /*------------------------------------------------------------------*
     *                 Adaptive Otsu-based thresholding                 *
     *------------------------------------------------------------------*/
    // leptonica: pixOtsuAdaptiveThreshold (binarize.c)
    /// <summary>Otsu thresholding with a threshold found for each tile.</summary>
    /// <param name="sx">desired tile dimensions; actual size may vary</param>
    /// <param name="sy">desired tile dimensions; actual size may vary</param>
    /// <param name="smoothx">half-width of convolution kernel applied to
    ///                       threshold array: use 0 for no smoothing</param>
    /// <param name="smoothy">half-height of convolution kernel applied to
    ///                       threshold array: use 0 for no smoothing</param>
    /// <param name="scorefract">fraction of the max Otsu score; typ. 0.1;
    ///                          use 0.0 for standard Otsu</param>
    /// <param name="pixth">array of threshold values found for each tile</param>
    /// <param name="pixd">thresholded input pixs, based on the threshold array</param>
    /// <remarks>
    /// (1) The Otsu method finds a single global threshold for an image.
    ///     This function allows a locally adapted threshold to be
    ///     found for each tile into which the image is broken up.
    /// (2) The array of threshold values, one for each tile, constitutes
    ///     a highly downscaled image.  This array is optionally
    ///     smoothed using a convolution.  The full width and height of the
    ///     convolution kernel are (2 * %smoothx + 1) and (2 * %smoothy + 1).
    /// (3) The minimum tile dimension allowed is 16.  If such small
    ///     tiles are used, it is recommended to use smoothing, because
    ///     without smoothing, each small tile determines the splitting
    ///     threshold independently.  A tile that is entirely in the
    ///     image bg will then hallucinate fg, resulting in a very noisy
    ///     binarization.  The smoothing should be large enough that no
    ///     tile is only influenced by one type (fg or bg) of pixels,
    ///     because it will force a split of its pixels.
    /// (4) To get a single global threshold for the entire image, use
    ///     input values of %sx and %sy that are larger than the image.
    ///     For this situation, the smoothing parameters are ignored.
    /// (5) The threshold values partition the image pixels into two classes:
    ///     one whose values are less than the threshold and another
    ///     whose values are greater than or equal to the threshold.
    ///     This is the same use of 'threshold' as in pixThresholdToBinary().
    /// (6) The scorefract is the fraction of the maximum Otsu score, which
    ///     is used to determine the range over which the histogram minimum
    ///     is searched.  See numaSplitDistribution() for details on the
    ///     underlying method of choosing a threshold.
    /// (7) This uses enables a modified version of the Otsu criterion for
    ///     splitting the distribution of pixels in each tile into a
    ///     fg and bg part.  The modification consists of searching for
    ///     a minimum in the histogram over a range of pixel values where
    ///     the Otsu score is within a defined fraction, %scorefract,
    ///     of the max score.  To get the original Otsu algorithm, set
    ///     %scorefract == 0.
    /// (8) N.B. This method is NOT recommended for images with weak text
    ///     and significant background noise, such as bleedthrough, because
    ///     of the problem noted in (3) above for tiling.  Use Sauvola.
    /// </remarks>
    public void OtsuAdaptiveThreshold(int sx, int sy, int smoothx, int smoothy, float scorefract,
                                      out Pix pixth, out Pix pixd)
    {
        OtsuAdaptiveThresholdCore(sx, sy, smoothx, smoothy, scorefract, true, true,
                                  out Pix? pth, out Pix? pd);
        pixth = pth!;
        pixd = pd!;
    }

    private void OtsuAdaptiveThresholdCore(int sx, int sy, int smoothx, int smoothy, float scorefract,
                                           bool wantth, bool wantd, out Pix? ppixth, out Pix? ppixd)
    {
        ppixth = null;
        ppixd = null;
        if (!wantth && !wantd)
            throw Lept.Arg("pixOtsuAdaptiveThreshold", "neither &pixth nor &pixd defined");
        if (Depth != 8)
            throw Lept.Arg("pixOtsuAdaptiveThreshold", "pixs not defined or not 8 bpp");
        if (sx < 16 || sy < 16)
            throw Lept.Arg("pixOtsuAdaptiveThreshold", "sx and sy must be >= 16");

            /* Compute the threshold array for the tiles */
        GetDimensions(out int w, out int h, out _);
        int nx = Math.Max(1, w / sx);
        int ny = Math.Max(1, h / sy);
        smoothx = Math.Min(smoothx, (nx - 1) / 2);
        smoothy = Math.Min(smoothy, (ny - 1) / 2);
        PixTiling pt = PixTiling.Create(this, nx, ny, 0, 0, 0, 0);
        Pix pixthresh = Create(nx, ny, 8);
        for (int i = 0; i < ny; i++) {
            for (int j = 0; j < nx; j++) {
                Pix pixt = pt.GetTile(i, j);
                pixt.SplitDistributionFgBg(scorefract, 1, out int thresh, out _, out _);
                pixthresh.SetPixel(j, i, (uint)thresh);  /* see note (4) */
            }
        }

            /* Optionally smooth the threshold array */
        Pix pixth;
        if (smoothx > 0 || smoothy > 0)
            pixth = pixthresh.Blockconv(smoothx, smoothy);
        else
            pixth = Clone(pixthresh);

            /* Optionally apply the threshold array to binarize pixs */
        if (wantd) {
            Pix pixd = Create(w, h, 1);
            pixd.CopyResolution(this);
            for (int i = 0; i < ny; i++) {
                for (int j = 0; j < nx; j++) {
                    Pix pixt = pt.GetTile(i, j);
                    pixth.GetPixel(j, i, out uint val);
                    Pix pixb = pixt.ThresholdToBinary((int)val);
                    pt.PaintTile(pixd, i, j, pixb);
                }
            }
            ppixd = pixd;
        }

        if (wantth)
            ppixth = pixth;
    }

    /*------------------------------------------------------------------*
     *      Otsu thresholding on adaptive background normalization      *
     *------------------------------------------------------------------*/
    // leptonica: pixOtsuThreshOnBackgroundNorm (binarize.c)
    /// <summary>Background normalization followed by Otsu thresholding.</summary>
    /// <param name="pixim">[optional] 1 bpp 'image' mask; can be null</param>
    /// <param name="sx">tile size in pixels</param>
    /// <param name="sy">tile size in pixels</param>
    /// <param name="thresh">threshold for determining foreground</param>
    /// <param name="mincount">min threshold on counts in a tile</param>
    /// <param name="bgval">target bg val; typ. &gt; 128</param>
    /// <param name="smoothx">half-width of block convolution kernel width</param>
    /// <param name="smoothy">half-width of block convolution kernel height</param>
    /// <param name="scorefract">fraction of the max Otsu score; typ. 0.1</param>
    /// <param name="pthresh">threshold value that was used on the normalized image</param>
    /// <returns>pixd 1 bpp thresholded image</returns>
    /// <remarks>
    /// (1) This does background normalization followed by Otsu
    ///     thresholding.  Otsu binarization attempts to split the
    ///     image into two roughly equal sets of pixels, and it does
    ///     a very poor job when there are large amounts of dark
    ///     background.  By doing a background normalization first,
    ///     to get the background near 255, we remove this problem.
    ///     Then we use a modified Otsu to estimate the best global
    ///     threshold on the normalized image.
    /// (2) See pixBackgroundNorm() for meaning and typical values
    ///     of input parameters.  For a start, you can try:
    ///       sx, sy = 10, 15
    ///       thresh = 100
    ///       mincount = 50
    ///       bgval = 255
    ///       smoothx, smoothy = 2
    /// </remarks>
    public Pix OtsuThreshOnBackgroundNorm(Pix? pixim, int sx, int sy, int thresh, int mincount,
                                          int bgval, int smoothx, int smoothy, float scorefract,
                                          out int pthresh)
    {
        pthresh = 0;
        if (Depth != 8)
            throw Lept.Arg("pixOtsuThreshOnBackgroundNorm", "pixs undefined or not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg("pixOtsuThreshOnBackgroundNorm", "pixs is colormapped");
        if (sx < 4 || sy < 4)
            throw Lept.Arg("pixOtsuThreshOnBackgroundNorm", "sx and sy must be >= 4");
        if (mincount > sx * sy) {
            Log.Warning("pixOtsuThreshOnBackgroundNorm", "mincount too large for tile size\n");
            mincount = (sx * sy) / 3;
        }

        Pix pixn = BackgroundNorm(pixim, null, sx, sy, thresh,
                                  mincount, bgval, smoothx, smoothy);

            /* Just use 1 tile for a global threshold, which is stored
             * as a single pixel in pixt. */
        pixn.GetDimensions(out int w, out int h, out _);
        pixn.OtsuAdaptiveThresholdCore(w, h, 0, 0, scorefract, true, true,
                                       out Pix? pixt, out Pix? pixd);

        if (pixt != null) {
            pixt.GetPixel(0, 0, out uint val);
            pthresh = (int)val;
        }

        return pixd!;
    }

    /*----------------------------------------------------------------------*
     *    Masking and Otsu estimate on adaptive background normalization    *
     *----------------------------------------------------------------------*/
    // leptonica: pixMaskedThreshOnBackgroundNorm (binarize.c)
    /// <summary>Background normalization, then differential thresholding under a mask.</summary>
    /// <param name="pixim">[optional] 1 bpp 'image' mask; can be null</param>
    /// <param name="sx">tile size in pixels</param>
    /// <param name="sy">tile size in pixels</param>
    /// <param name="thresh">threshold for determining foreground</param>
    /// <param name="mincount">min threshold on counts in a tile</param>
    /// <param name="smoothx">half-width of block convolution kernel width</param>
    /// <param name="smoothy">half-width of block convolution kernel height</param>
    /// <param name="scorefract">fraction of the max Otsu score; typ. ~ 0.1</param>
    /// <param name="pthresh">threshold value that was used on the normalized image</param>
    /// <returns>pixd 1 bpp thresholded image</returns>
    /// <remarks>
    /// (1) This begins with a standard background normalization.
    ///     Additionally, there is a flexible background norm, that
    ///     will adapt to a rapidly varying background, and this
    ///     puts white pixels in the background near regions with
    ///     significant foreground.  The white pixels are turned into
    ///     a 1 bpp selection mask by binarization followed by dilation.
    ///     Otsu thresholding is performed on the input image to get an
    ///     estimate of the threshold in the non-mask regions.
    ///     The background normalized image is thresholded with two
    ///     different values, and the result is combined using
    ///     the selection mask.
    /// (2) Note that the numbers 255 (for bgval target) and 190 (for
    ///     thresholding on pixn) are tied together, and explicitly
    ///     defined in this function.
    /// (3) See pixBackgroundNorm() for meaning and typical values
    ///     of input parameters.  For a start, you can try:
    ///       sx, sy = 10, 15
    ///       thresh = 100
    ///       mincount = 50
    ///       smoothx, smoothy = 2
    /// </remarks>
    public Pix MaskedThreshOnBackgroundNorm(Pix? pixim, int sx, int sy, int thresh, int mincount,
                                            int smoothx, int smoothy, float scorefract,
                                            out int pthresh)
    {
        pthresh = 0;
        if (Depth != 8)
            throw Lept.Arg("pixMaskedThreshOnBackgroundNorm", "pixs undefined or not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg("pixMaskedThreshOnBackgroundNorm", "pixs is colormapped");
        if (sx < 4 || sy < 4)
            throw Lept.Arg("pixMaskedThreshOnBackgroundNorm", "sx and sy must be >= 4");
        if (mincount > sx * sy) {
            Log.Warning("pixMaskedThreshOnBackgroundNorm", "mincount too large for tile size\n");
            mincount = (sx * sy) / 3;
        }

            /* Standard background normalization */
        Pix pixn = BackgroundNorm(pixim, null, sx, sy, thresh,
                                  mincount, 255, smoothx, smoothy);

            /* Special background normalization for adaptation to quickly
             * varying background.  Threshold on the very light parts,
             * which tend to be near significant edges, and dilate to
             * form a mask over regions that are typically text.  The
             * dilation size is chosen to cover the text completely,
             * except for very thick fonts. */
        Pix pix1 = BackgroundNormFlex(7, 7, 1, 1, 20);
        Pix pix2 = pix1.ThresholdToBinary(240);
        Invert(pix2, pix2);
        Pix pixm = pix2.MorphSequence("d21.21", 0);

            /* Use Otsu to get a global threshold estimate for the image,
             * which is stored as a single pixel in pix3. */
        GetDimensions(out int w, out int h, out _);
        OtsuAdaptiveThresholdCore(w, h, 0, 0, scorefract, true, false,
                                  out Pix? pix3, out _);
        pix3!.GetPixel(0, 0, out uint val);
        pthresh = (int)val;

            /* Threshold the background normalized images differentially,
             * using a high value correlated with the background normalization
             * for the part of the image under the mask (i.e., near the
             * darker, thicker foreground), and a value that depends on the Otsu
             * threshold for the rest of the image.  This gives a solid
             * (high) thresholding for the foreground parts of the image,
             * while allowing the background and light foreground to be
             * reasonably well cleaned using a threshold adapted to the
             * input image. */
        int highthresh = (int)Math.Min(256u, val + 30);
        Pix pixd = pixn.ThresholdToBinary(highthresh);  /* for bg and light fg */
        Pix pix4 = pixn.ThresholdToBinary(190);  /* for heavier fg */
        pixd.CombineMasked(pix4, pixm);

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *                           Sauvola binarization                       *
     *----------------------------------------------------------------------*/
    // leptonica: pixSauvolaBinarizeTiled (binarize.c)
    /// <summary>Sauvola binarization, processing the image in tiles.</summary>
    /// <param name="whsize">window half-width for measuring local statistics</param>
    /// <param name="factor">factor for reducing threshold due to variance; &gt;= 0</param>
    /// <param name="nx">subdivision into tiles; &gt;= 1</param>
    /// <param name="ny">subdivision into tiles; &gt;= 1</param>
    /// <param name="pixth">Sauvola threshold values</param>
    /// <param name="pixd">thresholded image</param>
    /// <remarks>
    /// (1) The window width and height are 2 * %whsize + 1.  The minimum
    ///     value for %whsize is 2; typically it is &gt;= 7.
    /// (2) For nx == ny == 1, this defaults to pixSauvolaBinarize().
    /// (3) Why a tiled version?
    ///     (a) A uint32 is used for the mean value accumulator, so
    ///         overflow can occur for an image with more than 16M pixels.
    ///     (b) A dpix is used to accumulate mean square values, and it
    ///         can only accommodate images with less than 2^28 pixels.
    ///         Using tiles reduces the size of all the arrays.
    ///     (c) Each tile can be processed independently, in parallel,
    ///         on a multicore processor.
    /// (4) The Sauvola threshold is determined from the formula:
    ///         t = m * (1 - k * (1 - s / 128))
    ///     See pixSauvolaBinarize() for details.
    /// </remarks>
    public void SauvolaBinarizeTiled(int whsize, float factor, int nx, int ny,
                                     out Pix pixth, out Pix pixd)
    {
        SauvolaBinarizeTiledCore(whsize, factor, nx, ny, true, true, out Pix? pth, out Pix? pd);
        pixth = pth!;
        pixd = pd!;
    }

    private void SauvolaBinarizeTiledCore(int whsize, float factor, int nx, int ny,
                                          bool wantth, bool wantd, out Pix? ppixth, out Pix? ppixd)
    {
        ppixth = null;
        ppixd = null;
        if (!wantth && !wantd)
            throw Lept.Arg("pixSauvolaBinarizeTiled", "no outputs");
        if (Depth != 8)
            throw Lept.Arg("pixSauvolaBinarizeTiled", "pixs undefined or not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg("pixSauvolaBinarizeTiled", "pixs is cmapped");
        GetDimensions(out int w, out int h, out _);
        if (whsize < 2)
            throw Lept.Arg("pixSauvolaBinarizeTiled", "whsize must be >= 2");
        if (w < 2 * whsize + 3 || h < 2 * whsize + 3)
            throw Lept.Arg("pixSauvolaBinarizeTiled", "whsize too large for image");
        if (factor < 0.0)
            throw Lept.Arg("pixSauvolaBinarizeTiled", "factor must be >= 0");

        if (nx <= 1 && ny <= 1) {
            SauvolaBinarizeCore(whsize, factor, 1, false, false, wantth, wantd,
                                out _, out _, out ppixth, out ppixd);
            return;
        }

            /* Test to see if the tiles are too small.  The required
             * condition is that the tile dimensions must be at least
             * (whsize + 2) x (whsize + 2).  */
        int xrat = w / nx;
        int yrat = h / ny;
        if (xrat < whsize + 2) {
            nx = w / (whsize + 2);
            Log.Warning("pixSauvolaBinarizeTiled",
                        CFormat.Sprintf("tile width too small; nx reduced to %d\n", nx));
        }
        if (yrat < whsize + 2) {
            ny = h / (whsize + 2);
            Log.Warning("pixSauvolaBinarizeTiled",
                        CFormat.Sprintf("tile height too small; ny reduced to %d\n", ny));
        }
        if (nx <= 1 && ny <= 1) {
            SauvolaBinarizeCore(whsize, factor, 1, false, false, wantth, wantd,
                                out _, out _, out ppixth, out ppixd);
            return;
        }

            /* We can use pixtiling for painting both outputs, if requested */
        Pix? pixth = null;
        Pix? pixd = null;
        if (wantth) {
            pixth = Create(w, h, 8);
            ppixth = pixth;
        }
        if (wantd) {
            pixd = Create(w, h, 1);
            ppixd = pixd;
        }
        PixTiling pt = PixTiling.Create(this, nx, ny, 0, 0, whsize + 1, whsize + 1);
        pt.NoStripOnPaint();  /* pixSauvolaBinarize() does the stripping */

        for (int i = 0; i < ny; i++) {
            for (int j = 0; j < nx; j++) {
                Pix pixt = pt.GetTile(i, j);
                pixt.SauvolaBinarizeCore(whsize, factor, 0, false, false, wantth, wantd,
                                         out _, out _, out Pix? tileth, out Pix? tiled);
                if (wantth) {  /* do not strip */
                    pt.PaintTile(pixth!, i, j, tileth!);
                }
                if (wantd) {
                    pt.PaintTile(pixd!, i, j, tiled!);
                }
            }
        }
    }

    // leptonica: pixSauvolaBinarize (binarize.c)
    /// <summary>Sauvola binarization using the local mean and standard deviation.</summary>
    /// <param name="whsize">window half-width for measuring local statistics</param>
    /// <param name="factor">factor for reducing threshold due to variance; &gt;= 0</param>
    /// <param name="addborder">1 to add border of width (%whsize + 1) on all sides</param>
    /// <param name="pixm">local mean values</param>
    /// <param name="pixsd">local standard deviation values</param>
    /// <param name="pixth">threshold values</param>
    /// <param name="pixd">thresholded image</param>
    /// <remarks>
    /// (1) The window width and height are 2 * %whsize + 1.  The minimum
    ///     value for %whsize is 2; typically it is &gt;= 7..
    /// (2) The local statistics, measured over the window, are the
    ///     average and standard deviation.
    /// (3) The measurements of the mean and standard deviation are
    ///     performed inside a border of (%whsize + 1) pixels.  If pixs does
    ///     not have these added border pixels, use %addborder = 1 to add
    ///     it here; otherwise use %addborder = 0.
    /// (4) The Sauvola threshold is determined from the formula:
    ///       t = m * (1 - k * (1 - s / 128))
    ///     where:
    ///       t = local threshold
    ///       m = local mean
    ///       k = %factor (&gt;= 0)   [ typ. 0.35 ]
    ///       s = local standard deviation, which is maximized at
    ///           127.5 when half the samples are 0 and half are 255.
    /// (5) The basic idea of Niblack and Sauvola binarization is that
    ///     the local threshold should be less than the median value,
    ///     and the larger the variance, the closer to the median
    ///     it should be chosen.  Typical values for k are between
    ///     0.2 and 0.5.
    /// </remarks>
    public void SauvolaBinarize(int whsize, float factor, int addborder,
                                out Pix pixm, out Pix pixsd, out Pix pixth, out Pix pixd)
    {
        SauvolaBinarizeCore(whsize, factor, addborder, true, true, true, true,
                            out Pix? pm, out Pix? psd, out Pix? pth, out Pix? pd);
        pixm = pm!;
        pixsd = psd!;
        pixth = pth!;
        pixd = pd!;
    }

    private void SauvolaBinarizeCore(int whsize, float factor, int addborder,
                                     bool wantm, bool wantsd, bool wantth, bool wantd,
                                     out Pix? ppixm, out Pix? ppixsd, out Pix? ppixth, out Pix? ppixd)
    {
        ppixm = null;
        ppixsd = null;
        ppixth = null;
        ppixd = null;
        if (!wantm && !wantsd && !wantth && !wantd)
            throw Lept.Arg("pixSauvolaBinarize", "no outputs");
        if (Depth != 8)
            throw Lept.Arg("pixSauvolaBinarize", "pixs undefined or not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg("pixSauvolaBinarize", "pixs is cmapped");
        GetDimensions(out int w, out int h, out _);
        if (whsize < 2)
            throw Lept.Arg("pixSauvolaBinarize", "whsize must be >= 2");
        if (w < 2 * whsize + 3 || h < 2 * whsize + 3)
            throw Lept.Arg("pixSauvolaBinarize", "whsize too large for image");
        if (factor < 0.0)
            throw Lept.Arg("pixSauvolaBinarize", "factor must be >= 0");

        Pix pixg, pixsc;
        if (addborder != 0) {
            pixg = AddMirroredBorder(whsize + 1, whsize + 1, whsize + 1, whsize + 1);
            pixsc = Clone(this);
        } else {
            pixg = Clone(this);
            pixsc = RemoveBorder(whsize + 1);
        }

        Pix? pixm = null, pixms = null, pixth = null, pixd = null;

            /* All these functions strip off the border pixels. */
        if (wantm || wantth || wantd)
            pixm = pixg.WindowedMean(whsize, whsize, 1, 1);
        if (wantsd || wantth || wantd)
            pixms = pixg.WindowedMeanSquare(whsize, whsize, 1);
        if (wantth || wantd)
            pixth = SauvolaGetThreshold(pixm!, pixms!, factor, wantsd, out ppixsd);
        if (wantd) {
            pixd = ApplyLocalThreshold(pixsc, pixth!);
            pixd.CopyResolution(this);
        }

        if (wantm)
            ppixm = pixm;
        if (wantth)
            ppixth = pixth;
        if (wantd)
            ppixd = pixd;
    }

    // leptonica: pixSauvolaGetThreshold (binarize.c)
    /// <summary>Computes the Sauvola threshold from the local mean and mean square (static in C).</summary>
    /// <param name="pixm">8 bpp grayscale; not colormapped</param>
    /// <param name="pixms">32 bpp</param>
    /// <param name="factor">factor for reducing threshold due to variance; &gt;= 0</param>
    /// <param name="wantsd">true if the local standard deviation is wanted (&amp;pixsd != NULL in C)</param>
    /// <param name="pixsd">local standard deviation; null if not %wantsd</param>
    /// <returns>pixd 8 bpp, sauvola threshold values</returns>
    /// <remarks>
    /// (1) The Sauvola threshold is determined from the formula:
    ///       t = m * (1 - k * (1 - s / 128))
    ///     where:
    ///       t = local threshold
    ///       m = local mean
    ///       k = %factor (&gt;= 0)   [ typ. 0.35 ]
    ///       s = local standard deviation, which is maximized at
    ///           127.5 when half the samples are 0 and half are 255.
    /// (2) See pixSauvolaBinarize() for other details.
    /// (3) Important definitions and relations for computing averages:
    ///       v == pixel value
    ///       E(p) == expected value of p == average of p over some pixel set
    ///       S(v) == square of v == v * v
    ///       mv == E(v) == expected pixel value == mean value
    ///       ms == E(S(v)) == expected square of pixel values
    ///          == mean square value
    ///       var == variance == expected square of deviation from mean
    ///           == E(S(v - mv)) = E(S(v) - 2 * S(v * mv) + S(mv))
    ///                           = E(S(v)) - S(mv)
    ///                           = ms - mv * mv
    ///       s == standard deviation = sqrt(var)
    ///     So for evaluating the standard deviation in the Sauvola
    ///     threshold, we take
    ///       s = sqrt(ms - mv * mv)
    /// </remarks>
    internal static Pix SauvolaGetThreshold(Pix pixm, Pix pixms, float factor, bool wantsd,
                                            out Pix? pixsd)
    {
        pixsd = null;
        if (pixm.Depth != 8)
            throw Lept.Arg("pixSauvolaGetThreshold", "pixm undefined or not 8 bpp");
        if (pixm.Colormap != null)
            throw Lept.Arg("pixSauvolaGetThreshold", "pixm is colormapped");
        if (pixms.Depth != 32)
            throw Lept.Arg("pixSauvolaGetThreshold", "pixms undefined or not 32 bpp");
        if (factor < 0.0)
            throw Lept.Arg("pixSauvolaGetThreshold", "factor must be >= 0");

        pixm.GetDimensions(out int w, out int h, out _);
        if (LeptGpu.TryGetActive((long)w * h, 10, out IGpuAccelerator? gpuSauvola) &&
            gpuSauvola.TrySauvolaGetThreshold(pixm, pixms, factor, wantsd, out Pix? gpuThresh, out Pix? gpuSd)) {
            pixsd = gpuSd;
            return gpuThresh;
        }
        Pix pixd = CreateNoInit(w, h, 8);
        Pix? sdPix = wantsd ? CreateNoInit(w, h, 8) : null;
        pixsd = sdPix;
        uint[] datam = pixm.Data;
        uint[] datams = pixms.Data;
        uint[] datasd = wantsd ? sdPix!.Data : Array.Empty<uint>();
        uint[] datad = pixd.Data;
        int wplm = pixm.Wpl;
        int wplms = pixms.Wpl;
        int wplsd = wantsd ? sdPix!.Wpl : 0;
        int wpld = pixd.Wpl;
        int w4 = w & ~3;

        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint mRef = ref MemoryMarshal.GetArrayDataReference(datam);
            ref uint msRef = ref MemoryMarshal.GetArrayDataReference(datams);
            ref uint sdRef = ref MemoryMarshal.GetArrayDataReference(datasd);
            ref uint dRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                ref uint linemRef = ref Unsafe.Add(ref mRef, i * wplm);
                ref uint linemsRef = ref Unsafe.Add(ref msRef, i * wplms);
                ref uint linedRef = ref Unsafe.Add(ref dRef, i * wpld);
                if (!wantsd) {
                    int j = 0;
                    for (; j < w4; j += 4) {
                        uint wm = Unsafe.Add(ref linemRef, j >> 2);
                        int mv0 = (int)(wm >> 24);
                        int mv1 = (int)((wm >> 16) & 0xffu);
                        int mv2 = (int)((wm >> 8) & 0xffu);
                        int mv3 = (int)(wm & 0xffu);
                        float sd0 = MathF.Sqrt((float)((int)Unsafe.Add(ref linemsRef, j) - mv0 * mv0));
                        float sd1 = MathF.Sqrt((float)((int)Unsafe.Add(ref linemsRef, j + 1) - mv1 * mv1));
                        float sd2 = MathF.Sqrt((float)((int)Unsafe.Add(ref linemsRef, j + 2) - mv2 * mv2));
                        float sd3 = MathF.Sqrt((float)((int)Unsafe.Add(ref linemsRef, j + 3) - mv3 * mv3));
                        uint t0 = (uint)((int)(mv0 * (1.0 - factor * (1.0 - sd0 / 128.0))) & 0xff);
                        uint t1 = (uint)((int)(mv1 * (1.0 - factor * (1.0 - sd1 / 128.0))) & 0xff);
                        uint t2 = (uint)((int)(mv2 * (1.0 - factor * (1.0 - sd2 / 128.0))) & 0xff);
                        uint t3 = (uint)((int)(mv3 * (1.0 - factor * (1.0 - sd3 / 128.0))) & 0xff);
                        Unsafe.Add(ref linedRef, j >> 2) = (t0 << 24) | (t1 << 16) | (t2 << 8) | t3;
                    }
                    if (j < w) {
                        Unsafe.Add(ref linedRef, w >> 2) = 0;
                        for (; j < w; j++) {
                            int mv = GetDataByteUnchecked(ref linemRef, j);
                            float sd = MathF.Sqrt((float)((int)Unsafe.Add(ref linemsRef, j) - mv * mv));
                            int thresh = (int)(mv * (1.0 - factor * (1.0 - sd / 128.0)));
                            SetDataByteUnchecked(ref linedRef, j, thresh);
                        }
                    }
                } else {
                    ref uint linesdRef = ref Unsafe.Add(ref sdRef, i * wplsd);
                    if ((w & 3) != 0) {
                        Unsafe.Add(ref linedRef, w >> 2) = 0;
                        Unsafe.Add(ref linesdRef, w >> 2) = 0;
                    }
                    for (int j = 0; j < w; j++) {
                        int mv = GetDataByteUnchecked(ref linemRef, j);
                        float sd = MathF.Sqrt((float)((int)Unsafe.Add(ref linemsRef, j) - mv * mv));
                        SetDataByteUnchecked(ref linesdRef, j, (int)sd);
                        int thresh = (int)(mv * (1.0 - factor * (1.0 - sd / 128.0)));
                        SetDataByteUnchecked(ref linedRef, j, thresh);
                    }
                }
            }
        });

        return pixd;
    }

    private static readonly byte[] s_bitRev8 = InitBitRev8();

    private static byte[] InitBitRev8()
    {
        byte[] t = new byte[256];
        for (int i = 0; i < 256; i++) {
            int r = ((i & 0x01) << 7) | ((i & 0x02) << 5) | ((i & 0x04) << 3) | ((i & 0x08) << 1) |
                    ((i & 0x10) >> 1) | ((i & 0x20) >> 3) | ((i & 0x40) >> 5) | ((i & 0x80) >> 7);
            t[i] = (byte)r;
        }
        return t;
    }

    // leptonica: pixApplyLocalThreshold (binarize.c)
    /// <summary>Applies an array of local thresholds to an 8 bpp image (static in C).</summary>
    /// <param name="pixs">8 bpp grayscale; not colormapped</param>
    /// <param name="pixth">8 bpp array of local thresholds</param>
    /// <returns>pixd 1 bpp, thresholded image</returns>
    internal static Pix ApplyLocalThreshold(Pix pixs, Pix pixth)
    {
        if (pixs.Depth != 8)
            throw Lept.Arg("pixApplyLocalThreshold", "pixs undefined or not 8 bpp");
        if (pixs.Colormap != null)
            throw Lept.Arg("pixApplyLocalThreshold", "pixs is colormapped");
        if (pixth.Depth != 8)
            throw Lept.Arg("pixApplyLocalThreshold", "pixth undefined or not 8 bpp");

        pixs.GetDimensions(out int w, out int h, out _);
        if (LeptGpu.TryGetActive((long)w * h, 4, out IGpuAccelerator? gpuLocalTh) &&
            gpuLocalTh.TryApplyLocalThreshold(pixs, pixth, out Pix? gpuBin)) {
            return gpuBin;
        }
        Pix pixd = CreateNoInit(w, h, 1);
        uint[] datas = pixs.Data;
        uint[] datat = pixth.Data;
        uint[] datad = pixd.Data;
        int wpls = pixs.Wpl;
        int wplt = pixth.Wpl;
        int wpld = pixd.Wpl;
        int full32 = w >> 5;

        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint sRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint tRef = ref MemoryMarshal.GetArrayDataReference(datat);
            ref uint dRef = ref MemoryMarshal.GetArrayDataReference(datad);
            ref byte revRef = ref MemoryMarshal.GetArrayDataReference(s_bitRev8);
            Vector256<byte> bias256 = Vector256.Create((byte)0x80);

            for (int i = yStart; i < yEnd; i++) {
                ref uint linesRef = ref Unsafe.Add(ref sRef, i * wpls);
                ref uint linetRef = ref Unsafe.Add(ref tRef, i * wplt);
                ref uint linedRef = ref Unsafe.Add(ref dRef, i * wpld);

                int dw = 0;
                if (Vector256.IsHardwareAccelerated) {
                    for (; dw < full32; dw++) {
                        int sw = dw << 3;
                        Vector256<byte> vs = SimdEndian.SwapBytesInWords(
                            Vector256.LoadUnsafe(ref Unsafe.Add(ref linesRef, sw)).AsByte());
                        Vector256<byte> vt = SimdEndian.SwapBytesInWords(
                            Vector256.LoadUnsafe(ref Unsafe.Add(ref linetRef, sw)).AsByte());
                        Vector256<sbyte> cmp = Vector256.LessThan(
                            (vs ^ bias256).AsSByte(), (vt ^ bias256).AsSByte());
                        uint mask = cmp.AsByte().ExtractMostSignificantBits();
                        uint packed = ((uint)Unsafe.Add(ref revRef, (int)(mask & 0xffu)) << 24) |
                                      ((uint)Unsafe.Add(ref revRef, (int)((mask >> 8) & 0xffu)) << 16) |
                                      ((uint)Unsafe.Add(ref revRef, (int)((mask >> 16) & 0xffu)) << 8) |
                                      Unsafe.Add(ref revRef, (int)(mask >> 24));
                        Unsafe.Add(ref linedRef, dw) = packed;
                    }
                } else {
                    for (; dw < full32; dw++) {
                        uint word = 0;
                        int baseX = dw << 5;
                        for (int b = 0; b < 32; b++) {
                            if (GetDataByteUnchecked(ref linesRef, baseX + b) < GetDataByteUnchecked(ref linetRef, baseX + b))
                                word |= 0x80000000u >> b;
                        }
                        Unsafe.Add(ref linedRef, dw) = word;
                    }
                }

                int remStart = full32 << 5;
                if (remStart < w) {
                    uint tailWord = 0;
                    for (int j = remStart; j < w; j++) {
                        if (GetDataByteUnchecked(ref linesRef, j) < GetDataByteUnchecked(ref linetRef, j))
                            tailWord |= 0x80000000u >> (j & 31);
                    }
                    Unsafe.Add(ref linedRef, full32) = tailWord;
                }
            }
        });

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *      Contrast normalization followed by Sauvola binarization         *
     *----------------------------------------------------------------------*/
    // leptonica: pixSauvolaOnContrastNorm (binarize.c)
    /// <summary>Contrast normalization followed by tiled Sauvola binarization.</summary>
    /// <param name="mindiff">minimum diff to accept as valid in contrast
    ///                       normalization.  Use ~130 for noisy images.</param>
    /// <param name="pixn">intermediate output from contrast normalization</param>
    /// <param name="pixth">threshold array for binarization</param>
    /// <returns>pixd 1 bpp thresholded image</returns>
    /// <remarks>
    /// (1) This composite operation is good for adaptively removing
    ///     dark background.
    /// </remarks>
    public Pix SauvolaOnContrastNorm(int mindiff, out Pix pixn, out Pix pixth)
    {
        int d = Depth;
        if (d < 8)
            throw Lept.Arg("pixSauvolaOnContrastNorm", "pixs undefined or d < 8 bpp");
        Pix pixg;
        if (d == 32)
            pixg = ConvertRGBToGray(0.3f, 0.4f, 0.3f);
        else
            pixg = ConvertTo8(false);

        Pix pix1 = ContrastNorm(null, pixg, 50, 50, mindiff, 2, 2);

            /* Use tiles of size approximately 250 x 250 */
        pix1.GetDimensions(out int w, out int h, out _);
        int nx = Math.Max(1, (w + 125) / 250);
        int ny = Math.Max(1, (h + 125) / 250);
        pix1.SauvolaBinarizeTiledCore(25, 0.40f, nx, ny, true, true,
                                      out Pix? pth, out Pix? pixd);
        pixn = pix1;
        pixth = pth!;
        return pixd!;
    }

    /*----------------------------------------------------------------------*
     *      Contrast normalization followed by background normalization     *
     *                            and thresholding                          *
     *----------------------------------------------------------------------*/
    // leptonica: pixThreshOnDoubleNorm (binarize.c)
    /// <summary>Contrast normalization, then background normalization and thresholding.</summary>
    /// <param name="mindiff">minimum diff to accept as valid in contrast
    ///                       normalization.  Use ~130 for noisy images.</param>
    /// <returns>pixd 1 bpp thresholded image</returns>
    /// <remarks>
    /// (1) This composite operation is good for adaptively removing
    ///     dark background.
    /// (2) The threshold for the binarization uses an estimate based
    ///     on Otsu adaptive thresholding.
    /// </remarks>
    public Pix ThreshOnDoubleNorm(int mindiff)
    {
        int d = Depth;
        if (d < 8)
            throw Lept.Arg("pixThreshOnDoubleNorm", "pixs undefined or d < 8 bpp");
        Pix pixg;
        if (d == 32)
            pixg = ConvertRGBToGray(0.3f, 0.4f, 0.3f);
        else
            pixg = ConvertTo8(false);

            /* Use the entire image for the estimate; pix1 is 1x1 */
        pixg.OtsuAdaptiveThresholdCore(5000, 5000, 0, 0, 0.1f, true, false,
                                       out Pix? pix1, out _);
        pix1!.GetPixel(0, 0, out uint val);
        int ival = (int)val;
        ival = Math.Min(ival, 110);

            /* Double normalization */
        ContrastNorm(pixg, pixg, 50, 50, mindiff, 2, 2);
        pix1 = pixg.BackgroundNormSimple(null, null);

/*    lept_stderr("ival = %d\n", ival); */
        Pix pixd = pix1.ThresholdToBinary(ival);
        return pixd;
    }

    /*----------------------------------------------------------------------*
     *            Global thresholding using connected components            *
     *----------------------------------------------------------------------*/
    // leptonica: pixThresholdByConnComp (binarize.c)
    /// <summary>Finds a global threshold from the numbers of 4 and 8 connected components.</summary>
    /// <param name="pixm">[optional] 1 bpp mask giving region to ignore
    ///                    by setting pixels to white; use null if no mask</param>
    /// <param name="start">binarization threshold level to start at</param>
    /// <param name="end">binarization threshold level to end at</param>
    /// <param name="incr">binarization threshold increment</param>
    /// <param name="thresh48">threshold on normalized difference between the
    ///                        numbers of 4 and 8 connected components</param>
    /// <param name="threshdiff">threshold on normalized difference between the
    ///                          number of 4 cc at successive iterations</param>
    /// <param name="globthresh">best global threshold; 0 if no threshold is found</param>
    /// <param name="pixd">image thresholded to binary, or null if no threshold is found</param>
    /// <param name="debugflag">1 for plotted results</param>
    /// <returns>true if a threshold is found; false if the component count
    /// is insufficient or no threshold is found</returns>
    /// <remarks>
    /// (1) This finds a global threshold based on connected components.
    ///     Although slow, it is reasonable to use it in a situation where
    ///     (a) the background in the image is relatively uniform, and
    ///     (b) the result will be fed to an OCR program that accepts 1 bpp
    ///         images and works best with easily segmented characters.
    ///     The reason for (b) is that this selects a threshold with a
    ///     minimum number of both broken characters and merged characters.
    /// (2) If the pix has color, it is converted to gray using the
    ///     max component.
    /// (3) Input 0 to use default values for any of these inputs:
    ///     %start, %end, %incr, %thresh48, %threshdiff.
    /// (4) This approach can be understood as follows.  When the
    ///     binarization threshold is varied, the numbers of c.c. identify
    ///     four regimes:
    ///     (a) For low thresholds, text is broken into small pieces, and
    ///         the number of c.c. is large, with the 4 c.c. significantly
    ///         exceeding the 8 c.c.
    ///     (b) As the threshold rises toward the optimum value, the text
    ///         characters coalesce and there is very little difference
    ///         between the numbers of 4 and 8 c.c, which both go
    ///         through a minimum.
    ///     (c) Above this, the image background gets noisy because some
    ///         pixels are(thresholded to foreground, and the numbers
    ///         of c.c. quickly increase, with the 4 c.c. significantly
    ///         larger than the 8 c.c.
    ///     (d) At even higher thresholds, the image background noise
    ///         coalesces as it becomes mostly foreground, and the
    ///         number of c.c. drops quickly.
    /// (5) If there is no global threshold that distinguishes foreground
    ///     text from background (e.g., weak text over a background that
    ///     has significant variation and/or bleedthrough), this returns false,
    ///     which the caller should check.
    /// </remarks>
    public bool ThresholdByConnComp(Pix? pixm, int start, int end, int incr, float thresh48,
                                    float threshdiff, out int globthresh, out Pix? pixd,
                                    int debugflag)
    {
        globthresh = 0;
        pixd = null;
        if (Depth == 1)
            throw Lept.Arg("pixThresholdByConnComp", "pixs undefined or 1 bpp");
        if (pixm != null && pixm.Depth != 1)
            throw Lept.Arg("pixThresholdByConnComp", "pixm must be 1 bpp");

            /* Assign default values if requested */
        if (start <= 0) start = 80;
        if (end <= 0) end = 200;
        if (incr <= 0) incr = 10;
        if (thresh48 <= 0.0) thresh48 = 0.01f;
        if (threshdiff <= 0.0) threshdiff = 0.01f;
        if (start > end)
            throw Lept.Arg("pixThresholdByConnComp", "invalid start,end");

            /* Make 8 bpp, using the max component if color. */
        Pix pix1, pix2, pix3;
        if (Colormap != null)
            pix1 = RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        else
            pix1 = Clone(this);
        if (pix1.Depth == 32)
            pix2 = pix1.ConvertRGBToGrayMinMax(L.L_CHOOSE_MAX);
        else
            pix2 = pix1.ConvertTo8(false);

            /* Mask out any non-text regions.  Do this in-place, because pix2
             * can never be the same pix as pixs. */
        if (pixm != null)
            pix2.SetMasked(pixm, 255);

            /* Make sure there are enough components to get a valid signal */
        pix3 = pix2.ConvertTo1(start);
        int n4 = pix3.CountConnComp(4);
        int mincounts = 500;
        if (n4 < mincounts) {
            Log.Info("pixThresholdByConnComp",
                     CFormat.Sprintf("Insufficient component count: %d\n", n4));
            return false;
        }

            /* Compute the c.c. data */
        Numa na4 = Numa.Create(0);
        Numa na8 = Numa.Create(0);
        na4.SetParameters(start, incr);
        na8.SetParameters(start, incr);
        int thresh, i;
        for (thresh = start, i = 0; thresh <= end; thresh += incr, i++) {
            pix3 = pix2.ConvertTo1(thresh);
            n4 = pix3.CountConnComp(4);
            int n8 = pix3.CountConnComp(8);
            na4.AddNumber(n4);
            na8.AddNumber(n8);
        }
        if (debugflag != 0) {
            Utils.LeptMkdir("lept/binarize");
            Gplot gplot = Gplot.Create("/tmp/lept/binarize", L.GPLOT_PNG,
                                       "number of cc vs. threshold",
                                       "threshold", "number of cc");
            gplot.AddPlot(null, na4, L.GPLOT_LINES, "plot 4cc");
            gplot.AddPlot(null, na8, L.GPLOT_LINES, "plot 8cc");
            gplot.MakeOutput();
        }

        int n = na4.Count;
        bool found = false;
        float firstcount4 = 0.0f, prevcount4 = 0.0f;
        for (i = 0; i < n; i++) {
            if (i == 0) {
                firstcount4 = na4.GetFValue(i);
                prevcount4 = firstcount4;
            } else {
                float count4 = na4.GetFValue(i);
                float count8 = na8.GetFValue(i);
                float diff48 = (count4 - count8) / firstcount4;
                float diff4 = MathF.Abs(prevcount4 - count4) / firstcount4;
                if (debugflag != 0) {
                    Utils.LeptStderr("diff48 = %7.3f, diff4 = %7.3f\n",
                                     diff48, diff4);
                }
                if (diff48 < thresh48 && diff4 < threshdiff) {
                    found = true;
                    break;
                }
                prevcount4 = count4;
            }
        }

        if (found) {
            int gt = start + i * incr;
            globthresh = gt;
            pixd = pix2.ConvertTo1(gt);
            pixd.CopyResolution(this);
            if (debugflag != 0) Utils.LeptStderr("global threshold = %d\n", gt);
            return true;
        }

        if (debugflag != 0) Utils.LeptStderr("no global threshold found\n");
        return false;
    }

    /*----------------------------------------------------------------------*
     *                   Global thresholding by histogram                   *
     *----------------------------------------------------------------------*/
    // leptonica: pixThresholdByHisto (binarize.c)
    /// <summary>Finds a global threshold from the smoothed histogram of gray values.</summary>
    /// <param name="factor">subsampling factor &gt;= 1</param>
    /// <param name="halfw">half of window width for smoothing; use 0 for default</param>
    /// <param name="skip">look-ahead distance to avoid false minima; use 0 for default</param>
    /// <param name="thresh">best global threshold; 0 if no threshold is found</param>
    /// <param name="pixd">thresholded 1 bpp pix; null if no threshold is found</param>
    /// <param name="nahisto">rescaled histogram of gray values; null if no threshold is found</param>
    /// <param name="pixhisto">plot of rescaled histogram; null unless %wanthisto</param>
    /// <param name="wanthisto">true to make the plot of the histogram (&amp;pixhisto != NULL in C)</param>
    /// <returns>true if a threshold is found; false otherwise</returns>
    /// <remarks>
    /// (1) This finds a global threshold.  It is best for an image that
    ///     has a fairly well-defined fg and bg.
    /// (2) If it finds a good threshold, the binarized
    ///     image is returned in %pixd; otherwise it return null.
    /// (3) See numaFindLocForThreshold() for use of %skip.
    /// (4) Suggest using default values (20) for %half and %skip.
    /// (5) Returns 0 in %thresh if it can't find a good threshold.
    /// </remarks>
    public bool ThresholdByHisto(int factor, int halfw, int skip, out int thresh, out Pix? pixd,
                                 out Numa? nahisto, bool wanthisto, out Pix? pixhisto)
    {
        pixd = null;
        nahisto = null;
        pixhisto = null;
        thresh = 0;
        if (Depth != 8)
            throw Lept.Arg("pixThresholdByHisto", "pixs undefined or not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg("pixThresholdByHisto", "pixs has colormap");
        if (factor < 1)
            throw Lept.Arg("pixThresholdByHisto", "sampling must be >= 1");
        if (halfw <= 0) halfw = 20;
        if (skip <= 0) skip = 20;

            /* Make a histogram of pixel values where the largest peak
             * is normalized to a value of 1.0. */
        Numa na1 = GetGrayHistogram(factor);
        Numa na2 = na1.WindowedMean(halfw);  /* smoothing */
        na2.GetMax(out float maxval, out _);
        Numa na3 = na2.Transform(0.0f, (float)(1.0 / maxval));  /* rescale to max of 1.0 */

        if (!na3.FindLocForThreshold(skip, out thresh, out float fract)) {
            Log.Error("pixThresholdByHisto", "failure to find threshold");
            return false;
        }
        Log.Info("pixThresholdByHisto",
                 CFormat.Sprintf("fractional area under first peak: %5.3f\n", fract));

        if (wanthisto) {
            Utils.LeptMkdir("lept/histo");
            Gplot.Simple1(na3, L.GPLOT_PNG, "/tmp/lept/histo/histo", null);
            pixhisto = Read("/tmp/lept/histo/histo.png");
        }
        nahisto = na3;
        if (thresh > 0)
            pixd = ThresholdToBinary(thresh);
        return true;
    }
}
