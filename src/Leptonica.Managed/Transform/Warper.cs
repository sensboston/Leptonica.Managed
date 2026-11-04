using System;
using static Leptonica.ArrayAccess;
using static Leptonica.L;

namespace Leptonica;

/*
 *  warper.c: Warping transforms: random harmonic warping, stretching and
 *            quadratic shear, stereoscopic images.
 *
 *      High-level captcha interface
 *          pixSimpleCaptcha()
 *
 *      Random sinusoidal warping
 *          pixRandomHarmonicWarp()
 *
 *      Helper functions
 *          generateRandomNumberArray()
 *          applyWarpTransform()
 *
 *      Stereoscopic warping
 *          pixWarpStereoscopic()
 *
 *      Linear and quadratic horizontal stretching
 *          pixStretchHorizontal()
 *          pixStretchHorizontalSampled()
 *          pixStretchHorizontalLI()
 *
 *      Quadratic vertical shear
 *          pixQuadraticVShear()
 *          pixQuadraticVShearSampled()
 *          pixQuadraticVShearLI()
 *
 *      Stereo from a pair of images
 *          pixStereoFromPair()
 *
 *  Differences from C:
 *    - Functions that return NULL in C throw instead.
 *    - rand() / srand() are CRand.Next() / CRand.Seed().
 *    - The version with a LUT for sin (USE_SIN_TABLE == 0 in C: pixRandomHarmonicWarpLUT(),
 *      applyWarpTransformLUT(), makeSinLUT(), getSinFromLUT()) is compiled out in C and
 *      is not ported.
 *
 *  Not ported: pixRandomHarmonicWarpLUT() and its helpers (see above).
 */

/// <summary>Constants from pix.h used by warper.c.</summary>
public static partial class L
{
    /* Horiz Warp Stretch */
    /// <summary>Stretch or contraction increases toward the left.</summary>
    public const int L_WARP_TO_LEFT = 1;
    /// <summary>Stretch or contraction increases toward the right.</summary>
    public const int L_WARP_TO_RIGHT = 2;

    /* Horiz Warp Mode */
    /// <summary>The stretch or contraction grows linearly.</summary>
    public const int L_LINEAR_WARP = 1;
    /// <summary>The stretch or contraction grows quadratically.</summary>
    public const int L_QUADRATIC_WARP = 2;

    /* Pixel Selection */
    /// <summary>Use linear interpolation from the source pixels.</summary>
    public const int L_INTERPOLATED = 1;
    /// <summary>Use nearest-pixel sampling from the source only.</summary>
    public const int L_SAMPLED = 2;
}

public sealed partial class Pix
{
    /* Suggested input to pixStereoFromPair().  These are weighting
     * factors for input to the red channel from the left image. */
    private const float DefaultRedWeight = 0.0f;
    private const float DefaultGreenWeight = 0.7f;
    private const float DefaultBlueWeight = 0.3f;

    /*----------------------------------------------------------------------*
     *                High-level example captcha interface                  *
     *----------------------------------------------------------------------*/
    // leptonica: pixSimpleCaptcha (warper.c)
    /// <summary>Makes a captcha: a randomly warped and colorized image of the input.</summary>
    /// <param name="border">added white pixels on each side</param>
    /// <param name="nterms">number of x and y harmonic terms</param>
    /// <param name="seed">of random number generator</param>
    /// <param name="color">for colorizing; in 0xrrggbb00 format; use 0 for black</param>
    /// <param name="cmapflag">1 for colormap output; 0 for rgb</param>
    /// <returns>pixd 8 bpp cmap or 32 bpp rgb; pixs is 8 bpp, no colormap</returns>
    /// <remarks>
    /// (1) This uses typical default values for generating captchas.
    ///     The magnitudes of the harmonic warp are typically to be
    ///     smaller when more terms are used, even though the phases
    ///     are random.  See, for example, prog/warptest.c.
    /// </remarks>
    public Pix SimpleCaptcha(int border, int nterms, uint seed, uint color, int cmapflag)
    {
        float[] xmag = {7.0f, 5.0f, 4.0f, 3.0f};
        float[] ymag = {10.0f, 8.0f, 6.0f, 5.0f};
        float[] xfreq = {0.12f, 0.10f, 0.10f, 0.11f};
        float[] yfreq = {0.15f, 0.13f, 0.13f, 0.11f};

        if (nterms < 1 || nterms > 4)
            throw Lept.Arg("pixSimpleCaptcha", "nterms must be in {1,2,3,4}");

        int k = nterms - 1;
        Pix pixg = ConvertTo8(false);
        Pix pixgb = pixg.AddBorder(border, 255);
        Pix pixw = pixgb.RandomHarmonicWarp(xmag[k], ymag[k], xfreq[k], yfreq[k],
                                            nterms, nterms, seed, 255);
        Pix pixd = pixw.ColorizeGray(color, cmapflag);
        return pixd;
    }

    /*----------------------------------------------------------------------*
     *                     Random sinusoidal warping                        *
     *----------------------------------------------------------------------*/
    // leptonica: pixRandomHarmonicWarp (warper.c)
    /// <summary>Warps an 8 bpp image with a sum of random sinusoidal terms.</summary>
    /// <param name="xmag">maximum magnitude of x displacement</param>
    /// <param name="ymag">maximum magnitude of y displacement</param>
    /// <param name="xfreq">maximum magnitude of x frequency</param>
    /// <param name="yfreq">maximum magnitude of y frequency</param>
    /// <param name="nx">number of x harmonic terms</param>
    /// <param name="ny">number of y harmonic terms</param>
    /// <param name="seed">of random number generator</param>
    /// <param name="grayval">color brought in from the outside; 0 for black, 255 for white</param>
    /// <returns>pixd 8 bpp; no colormap</returns>
    /// <remarks>
    /// (1) To generate the warped image p(x',y'), set up the transforms
    ///     that are in getWarpTransform().  For each (x',y') in the
    ///     dest, the warp function computes the originating location
    ///     (x, y) in the src.  The differences (x - x') and (y - y')
    ///     are given as a sum of products of sinusoidal terms.  Each
    ///     term is multiplied by a maximum amplitude (in pixels), and the
    ///     angle is determined by a frequency and phase, and depends
    ///     on the (x', y') value of the dest.  Random numbers with
    ///     a variable input seed are used to allow the warping to be
    ///     unpredictable.  A linear interpolation is used to find
    ///     the value for the source at (x, y); this value is written
    ///     into the dest.
    /// (2) This can be used to generate 'captcha's, which are somewhat
    ///     randomly distorted images of text.  A typical set of parameters
    ///     for a captcha are:
    ///               xmag = 4.0     ymag = 6.0
    ///               xfreq = 0.10   yfreq = 0.13
    ///               nx = 3         ny = 3
    ///     Other examples can be found in prog/warptest.c.
    /// </remarks>
    public Pix RandomHarmonicWarp(float xmag, float ymag, float xfreq, float yfreq,
                                  int nx, int ny, uint seed, int grayval)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg("pixRandomHarmonicWarp", "pixs not 8 bpp");

        /* Compute filter output at each location.  We iterate over
         * the destination pixels.  For each dest pixel, use the
         * warp function to compute the four source pixels that
         * contribute, at the location (x, y).  Each source pixel
         * is divided into 16 x 16 subpixels to get an approximate value. */
        CRand.Seed(seed);
        double[] randa = generateRandomNumberArray(5 * (nx + ny));
        Pix pixd = CreateTemplate(this);
        uint[] datas = Data;
        int wpls = Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                applyWarpTransform(xmag, ymag, xfreq, yfreq, randa, nx, ny,
                                   j, i, out float x, out float y);
                int val = LinearInterpolatePixelGray(datas, wpls, w, h, x, y, grayval);
                SetDataByte(lined, j, val);
            }
        }

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *                         Static helper functions                      *
     *----------------------------------------------------------------------*/
    // leptonica: generateRandomNumberArray (warper.c)
    private static double[] generateRandomNumberArray(int size)
    {
        double[] randa = new double[size];

        /* Return random values between 0.5 and 1.0 */
        for (int i = 0; i < size; i++)
            randa[i] = 0.5 * (1.0 + (double)CRand.Next() / (double)CRand.RandMax);
        return randa;
    }

    // leptonica: applyWarpTransform (warper.c)
    /// <remarks>
    /// (1) Uses the internal sin function.
    /// </remarks>
    private static void applyWarpTransform(float xmag, float ymag, float xfreq, float yfreq,
                                           double[] randa, int nx, int ny, int xp, int yp,
                                           out float px, out float py)
    {
        double twopi = 6.283185;
        double x, y, anglex, angley;
        int i;
        for (i = 0, x = xp; i < nx; i++) {
            anglex = xfreq * randa[3 * i + 1] * xp + twopi * randa[3 * i + 2];
            angley = yfreq * randa[3 * i + 3] * yp + twopi * randa[3 * i + 4];
            x += xmag * randa[3 * i] * Math.Sin(anglex) * Math.Sin(angley);
        }
        for (i = nx, y = yp; i < nx + ny; i++) {
            angley = yfreq * randa[3 * i + 1] * yp + twopi * randa[3 * i + 2];
            anglex = xfreq * randa[3 * i + 3] * xp + twopi * randa[3 * i + 4];
            y += ymag * randa[3 * i] * Math.Sin(angley) * Math.Sin(anglex);
        }
        px = (float)x;
        py = (float)y;
    }

    /*---------------------------------------------------------------------------*
     *                          Stereoscopic warping                             *
     *---------------------------------------------------------------------------*/
    // leptonica: pixWarpStereoscopic (warper.c)
    /// <summary>Makes a stereoscopic (red/cyan) image from a single image by warping the red channel.</summary>
    /// <param name="zbend">horizontal separation in pixels of red and cyan at the left and right
    /// sides, that gives rise to quadratic curvature out of the image plane</param>
    /// <param name="zshiftt">uniform pixel translation difference between red and cyan, that pushes
    /// the top of the image plane away from the viewer (zshiftt &gt; 0) or towards the viewer (zshiftt &lt; 0)</param>
    /// <param name="zshiftb">uniform pixel translation difference between red and cyan, that pushes
    /// the bottom of the image plane away from the viewer (zshiftb &gt; 0) or towards the viewer (zshiftb &lt; 0)</param>
    /// <param name="ybendt">max vertical displacement at the left or right edge at the top:
    /// y = ybendt * (2x/w - 1)^2</param>
    /// <param name="ybendb">same at the bottom</param>
    /// <param name="redleft">1 if the red filter is over the left eye; 0 otherwise</param>
    /// <returns>pixd 32 bpp</returns>
    /// <remarks>
    /// (1) This function splits out the red channel, mucks around with
    ///     it, then recombines with the unmolested cyan channel.
    /// (2) By using a quadratically increasing shift of the red
    ///     pixels horizontally and away from the vertical centerline,
    ///     the image appears to bend quadratically out of the image
    ///     plane, symmetrically with respect to the vertical center
    ///     line.  A positive value of zbend causes the plane to be
    ///     curved away from the viewer.  We use linearly interpolated
    ///     stretching to avoid the appearance of kinks in the curve.
    /// (3) The parameters zshiftt and zshiftb tilt the image plane
    ///     about a horizontal line through the center, and at the
    ///     same time move that line either in toward the viewer or away.
    ///     This is implemented by a combination of horizontal shear
    ///     about the center line (for the tilt) and horizontal
    ///     translation (to move the entire plane in or out).
    ///     A positive value of zshiftt moves the top of the plane
    ///     away from the viewer, and a positive value of zshiftb
    ///     moves the bottom of the plane away.  We use linear interpolated
    ///     shear to avoid visible vertical steps in the tilted image.
    /// (4) The image can be bent in the plane and about the vertical
    ///     centerline.  The centerline does not shift, and the
    ///     parameter ybend gives the relative shift at left and right
    ///     edges, with a downward shift for positive values of ybend.
    /// (6) When writing out a steroscopic (red/cyan) image in jpeg,
    ///     first call pixSetChromaSampling(pix, 0) to get sufficient
    ///     resolution in the red channel.
    /// (7) Typical values are:
    ///        zbend = 20
    ///        zshiftt = 15
    ///        zshiftb = -15
    ///        ybendt = 30
    ///        ybendb = 0
    ///     If the disparity z-values are too large, it is difficult for
    ///     the brain to register the two images.
    /// (8) This function has been cleverly reimplemented by Jeff Breidenbach.
    ///     The original implementation used two 32 bpp rgb images,
    ///     and merged them at the end.  The result is somewhat faded,
    ///     and has a parameter "thresh" that controls the amount of
    ///     color in the result.  (The present implementation avoids these
    ///     two problems, skipping both the colorization and the alpha
    ///     blending at the end, and is about 3x faster)
    /// </remarks>
    public Pix WarpStereoscopic(int zbend, int zshiftt, int zshiftb, int ybendt, int ybendb, int redleft)
    {
        Pix pix1, pix2, pix3, pix4, pixrs, pixrss;

        /* Convert to the output depth, 32 bpp. */
        pix1 = ConvertTo32();

        /* If requested, do a quad vertical shearing, pushing pixels up
         * or down, depending on their distance from the centerline. */
        GetDimensions(out int w, out int h, out _);
        Box boxleft = Box.Create(0, 0, w / 2, h);
        Box boxright = Box.Create(w / 2, 0, w - w / 2, h);
        if (ybendt != 0 || ybendb != 0) {
            Pix pixv1 = pix1.ClipRectangle(boxleft, out _)!;
            Pix pixv2 = pix1.ClipRectangle(boxright, out _)!;
            Pix pixv3 = pixv1.QuadraticVShear(L_WARP_TO_LEFT, ybendt, ybendb,
                                              L_INTERPOLATED, L_BRING_IN_WHITE);
            Pix pixv4 = pixv2.QuadraticVShear(L_WARP_TO_RIGHT, ybendt, ybendb,
                                              L_INTERPOLATED, L_BRING_IN_WHITE);
            pix2 = Create(w, h, 32);
            pix2.Rasterop(0, 0, w / 2, h, PIX_SRC, pixv3, 0, 0);
            pix2.Rasterop(w / 2, 0, w - w / 2, h, PIX_SRC, pixv4, 0, 0);
        } else {
            pix2 = Clone(pix1);
        }

        /* Split out the 3 components */
        Pix pixr = pix2.GetRGBComponent(COLOR_RED);
        Pix pixg = pix2.GetRGBComponent(COLOR_GREEN);
        Pix pixb = pix2.GetRGBComponent(COLOR_BLUE);

        /* The direction of the stereo disparity below is set
         * for the red filter to be over the left eye.  If the red
         * filter is over the right eye, invert the horizontal shifts. */
        if (redleft != 0) {
            zbend = -zbend;
            zshiftt = -zshiftt;
            zshiftb = -zshiftb;
        }

        /* Shift the red pixels horizontally by an amount that
         * increases quadratically from the centerline. */
        if (zbend == 0) {
            pixrs = Clone(pixr);
        } else {
            pix1 = pixr.ClipRectangle(boxleft, out _)!;
            pix2 = pixr.ClipRectangle(boxright, out _)!;
            pix3 = pix1.StretchHorizontal(L_WARP_TO_LEFT, L_QUADRATIC_WARP,
                                          zbend, L_INTERPOLATED, L_BRING_IN_WHITE);
            pix4 = pix2.StretchHorizontal(L_WARP_TO_RIGHT, L_QUADRATIC_WARP,
                                          zbend, L_INTERPOLATED, L_BRING_IN_WHITE);
            pixrs = Create(w, h, 8);
            pixrs.Rasterop(0, 0, w / 2, h, PIX_SRC, pix3, 0, 0);
            pixrs.Rasterop(w / 2, 0, w - w / 2, h, PIX_SRC, pix4, 0, 0);
        }

        /* Perform a combination of horizontal shift and shear of
         * red pixels.  The causes the plane of the image to tilt and
         * also move forward or backward. */
        if (zshiftt == 0 && zshiftb == 0) {
            pixrss = Clone(pixrs);
        } else if (zshiftt == zshiftb) {
            pixrss = Translate(null, pixrs, zshiftt, 0, L_BRING_IN_WHITE);
        } else {
            float angle = (float)(zshiftb - zshiftt) /
                          Math.Max(1.0f, (float)pixrs.Height);
            int zshift = (zshiftt + zshiftb) / 2;
            pix1 = Translate(null, pixrs, zshift, 0, L_BRING_IN_WHITE);
            pixrss = pix1.HShearLI(h / 2, angle, L_BRING_IN_WHITE);
        }

        /* Combine the unchanged cyan (g,b) image with the shifted red */
        Pix pixd = CreateRGBImage(pixrss, pixg, pixb);
        return pixd;
    }

    /*----------------------------------------------------------------------*
     *              Linear and quadratic horizontal stretching              *
     *----------------------------------------------------------------------*/
    // leptonica: pixStretchHorizontal (warper.c)
    /// <summary>Stretches or compresses horizontally, linearly or quadratically.</summary>
    /// <param name="dir">L_WARP_TO_LEFT or L_WARP_TO_RIGHT</param>
    /// <param name="type">L_LINEAR_WARP or L_QUADRATIC_WARP</param>
    /// <param name="hmax">horizontal displacement at edge</param>
    /// <param name="operation">L_SAMPLED or L_INTERPOLATED</param>
    /// <param name="incolor">L_BRING_IN_WHITE or L_BRING_IN_BLACK</param>
    /// <returns>pixd stretched/compressed; pixs is 1, 8 or 32 bpp</returns>
    /// <remarks>
    /// (1) If hmax > 0, this is an increase in the coordinate value of
    ///     pixels in pixd, relative to the same pixel in pixs.
    /// (2) If dir == L_WARP_TO_LEFT, the pixels on the right edge of
    ///     the image are not moved. So, for example, if hmax > 0
    ///     and dir == L_WARP_TO_LEFT, the pixels in pixd are
    ///     contracted toward the right edge of the image, relative
    ///     to those in pixs.
    /// (3) If type == L_LINEAR_WARP, the pixel positions are moved
    ///     to the left or right by an amount that varies linearly with
    ///     the horizontal location.
    /// (4) If operation == L_SAMPLED, the dest pixels are taken from
    ///     the nearest src pixel.  Otherwise, we use linear interpolation
    ///     between pairs of sampled pixels.
    /// </remarks>
    public Pix StretchHorizontal(int dir, int type, int hmax, int operation, int incolor)
    {
        const string func = "pixStretchHorizontal";

        int d = Depth;
        if (d != 1 && d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 1, 8 or 32 bpp");
        if (dir != L_WARP_TO_LEFT && dir != L_WARP_TO_RIGHT)
            throw Lept.Arg(func, "invalid direction");
        if (type != L_LINEAR_WARP && type != L_QUADRATIC_WARP)
            throw Lept.Arg(func, "invalid type");
        if (operation != L_SAMPLED && operation != L_INTERPOLATED)
            throw Lept.Arg(func, "invalid operation");
        if (incolor != L_BRING_IN_WHITE && incolor != L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor");

        if (d == 1 && operation == L_INTERPOLATED) {
            Log.Warning(func, "Using sampling for 1 bpp");
            // NOTE(upstream): assigns L_INTERPOLATED, not L_SAMPLED, so 1 bpp input still goes to the LI version
            operation = L_INTERPOLATED;
        }

        if (operation == L_SAMPLED)
            return StretchHorizontalSampled(dir, type, hmax, incolor);
        else
            return StretchHorizontalLI(dir, type, hmax, incolor);
    }

    // leptonica: pixStretchHorizontalSampled (warper.c)
    /// <summary>Stretches or compresses horizontally, taking the nearest source pixel.</summary>
    /// <param name="dir">L_WARP_TO_LEFT or L_WARP_TO_RIGHT</param>
    /// <param name="type">L_LINEAR_WARP or L_QUADRATIC_WARP</param>
    /// <param name="hmax">horizontal displacement at edge</param>
    /// <param name="incolor">L_BRING_IN_WHITE or L_BRING_IN_BLACK</param>
    /// <returns>pixd stretched/compressed</returns>
    /// <remarks>
    /// (1) See pixStretchHorizontal() for details.
    /// </remarks>
    public Pix StretchHorizontalSampled(int dir, int type, int hmax, int incolor)
    {
        const string func = "pixStretchHorizontalSampled";

        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 1, 8 or 32 bpp");
        if (dir != L_WARP_TO_LEFT && dir != L_WARP_TO_RIGHT)
            throw Lept.Arg(func, "invalid direction");
        if (type != L_LINEAR_WARP && type != L_QUADRATIC_WARP)
            throw Lept.Arg(func, "invalid type");
        if (incolor != L_BRING_IN_WHITE && incolor != L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor");

        Pix pixd = CreateTemplate(this);
        pixd.SetBlackOrWhite(L_BRING_IN_WHITE);
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpls = Wpl;
        int wpld = pixd.Wpl;
        int wm = w - 1;
        int j = 0;
        for (int jd = 0; jd < w; jd++) {
            if (dir == L_WARP_TO_LEFT) {
                if (type == L_LINEAR_WARP)
                    j = jd - (hmax * (wm - jd)) / wm;
                else  /* L_QUADRATIC_WARP */
                    j = jd - (hmax * (wm - jd) * (wm - jd)) / (wm * wm);
            } else if (dir == L_WARP_TO_RIGHT) {
                if (type == L_LINEAR_WARP)
                    j = jd - (hmax * jd) / wm;
                else  /* L_QUADRATIC_WARP */
                    j = jd - (hmax * jd * jd) / (wm * wm);
            }
            if (j < 0 || j > w - 1) continue;
            switch (d) {
                case 1:
                    for (int i = 0; i < h; i++) {
                        ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                        Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                        int val = GetDataBit(lines, j);
                        if (val != 0)
                            SetDataBit(lined, jd);
                    }
                    break;
                case 8:
                    for (int i = 0; i < h; i++) {
                        ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                        Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                        int val = GetDataByte(lines, j);
                        SetDataByte(lined, jd, val);
                    }
                    break;
                case 32:
                    for (int i = 0; i < h; i++) {
                        datad[i * wpld + jd] = datas[i * wpls + j];
                    }
                    break;
                default:
                    throw Lept.Arg(func, $"invalid depth: {d}");
            }
        }

        return pixd;
    }

    // leptonica: pixStretchHorizontalLI (warper.c)
    /// <summary>Stretches or compresses horizontally, with linear interpolation.</summary>
    /// <param name="dir">L_WARP_TO_LEFT or L_WARP_TO_RIGHT</param>
    /// <param name="type">L_LINEAR_WARP or L_QUADRATIC_WARP</param>
    /// <param name="hmax">horizontal displacement at edge</param>
    /// <param name="incolor">L_BRING_IN_WHITE or L_BRING_IN_BLACK</param>
    /// <returns>pixd stretched/compressed; pixs is 8 or 32 bpp</returns>
    /// <remarks>
    /// (1) See pixStretchHorizontal() for details.
    /// </remarks>
    public Pix StretchHorizontalLI(int dir, int type, int hmax, int incolor)
    {
        const string func = "pixStretchHorizontalLI";

        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 8 or 32 bpp");
        if (dir != L_WARP_TO_LEFT && dir != L_WARP_TO_RIGHT)
            throw Lept.Arg(func, "invalid direction");
        if (type != L_LINEAR_WARP && type != L_QUADRATIC_WARP)
            throw Lept.Arg(func, "invalid type");
        if (incolor != L_BRING_IN_WHITE && incolor != L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor");

        /* Standard linear interpolation, subdividing each pixel into 64 */
        Pix pixd = CreateTemplate(this);
        pixd.SetBlackOrWhite(L_BRING_IN_WHITE);
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpls = Wpl;
        int wpld = pixd.Wpl;
        int wm = w - 1;
        int j = 0;
        for (int jd = 0; jd < w; jd++) {
            if (dir == L_WARP_TO_LEFT) {
                if (type == L_LINEAR_WARP)
                    j = 64 * jd - 64 * (hmax * (wm - jd)) / wm;
                else  /* L_QUADRATIC_WARP */
                    j = 64 * jd - 64 * (hmax * (wm - jd) * (wm - jd)) / (wm * wm);
            } else if (dir == L_WARP_TO_RIGHT) {
                if (type == L_LINEAR_WARP)
                    j = 64 * jd - 64 * (hmax * jd) / wm;
                else  /* L_QUADRATIC_WARP */
                    j = 64 * jd - 64 * (hmax * jd * jd) / (wm * wm);
            }
            int jp = j / 64;
            int jf = j & 0x3f;
            if (jp < 0 || jp > wm) continue;
            switch (d) {
                case 8:
                    if (jp < wm) {
                        for (int i = 0; i < h; i++) {
                            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                            int val = ((63 - jf) * GetDataByte(lines, jp) +
                                       jf * GetDataByte(lines, jp + 1) + 31) / 63;
                            SetDataByte(lined, jd, val);
                        }
                    } else {  /* jp == wm */
                        for (int i = 0; i < h; i++) {
                            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                            int val = GetDataByte(lines, jp);
                            SetDataByte(lined, jd, val);
                        }
                    }
                    break;
                case 32:
                    if (jp < wm) {
                        for (int i = 0; i < h; i++) {
                            int lines = i * wpls;
                            int lined = i * wpld;
                            uint word0 = datas[lines + jp];
                            uint word1 = datas[lines + jp + 1];
                            int rval = (int)((63 - jf) * ((word0 >> L_RED_SHIFT) & 0xff) +
                                       jf * ((word1 >> L_RED_SHIFT) & 0xff) + 31) / 63;
                            int gval = (int)((63 - jf) * ((word0 >> L_GREEN_SHIFT) & 0xff) +
                                       jf * ((word1 >> L_GREEN_SHIFT) & 0xff) + 31) / 63;
                            int bval = (int)((63 - jf) * ((word0 >> L_BLUE_SHIFT) & 0xff) +
                                       jf * ((word1 >> L_BLUE_SHIFT) & 0xff) + 31) / 63;
                            ComposeRGBPixel(rval, gval, bval, out datad[lined + jd]);
                        }
                    } else {  /* jp == wm */
                        for (int i = 0; i < h; i++) {
                            datad[i * wpld + jd] = datas[i * wpls + jp];
                        }
                    }
                    break;
                default:
                    throw Lept.Arg(func, $"invalid depth: {d}");
            }
        }

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *                       Quadratic vertical shear                       *
     *----------------------------------------------------------------------*/
    // leptonica: pixQuadraticVShear (warper.c)
    /// <summary>Quadratic vertical shear (bending up or down as one moves left or right).</summary>
    /// <param name="dir">L_WARP_TO_LEFT or L_WARP_TO_RIGHT</param>
    /// <param name="vmaxt">max vertical displacement at edge and at top</param>
    /// <param name="vmaxb">max vertical displacement at edge and at bottom</param>
    /// <param name="operation">L_SAMPLED or L_INTERPOLATED</param>
    /// <param name="incolor">L_BRING_IN_WHITE or L_BRING_IN_BLACK</param>
    /// <returns>pixd stretched; pixs is 1, 8 or 32 bpp</returns>
    /// <remarks>
    /// (1) This gives a quadratic bending, upward or downward, as you
    ///     move to the left or right.
    /// (2) If dir == L_WARP_TO_LEFT, the right edge is unchanged, and
    ///     the left edge pixels are moved maximally up or down.
    /// (3) Parameters vmaxt and vmaxb control the maximum amount of
    ///     vertical pixel shear at the top and bottom, respectively.
    ///     If vmaxt > 0, the vertical displacement of pixels at the
    ///     top is downward.  Likewise, if vmaxb > 0, the vertical
    ///     displacement of pixels at the bottom is downward.
    /// (4) If operation == L_SAMPLED, the dest pixels are taken from
    ///     the nearest src pixel.  Otherwise, we use linear interpolation
    ///     between pairs of sampled pixels.
    /// (5) This is for quadratic shear.  For uniform (linear) shear,
    ///     use the standard shear operators.
    /// </remarks>
    public Pix QuadraticVShear(int dir, int vmaxt, int vmaxb, int operation, int incolor)
    {
        const string func = "pixQuadraticVShear";

        int d = Depth;
        if (d != 1 && d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 1, 8 or 32 bpp");
        if (dir != L_WARP_TO_LEFT && dir != L_WARP_TO_RIGHT)
            throw Lept.Arg(func, "invalid direction");
        if (operation != L_SAMPLED && operation != L_INTERPOLATED)
            throw Lept.Arg(func, "invalid operation");
        if (incolor != L_BRING_IN_WHITE && incolor != L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor");
        if (vmaxt == 0 && vmaxb == 0)
            return Copy(null, this);

        if (operation == L_INTERPOLATED && d == 1) {
            Log.Warning(func, "no interpolation for 1 bpp; using sampling");
            operation = L_SAMPLED;
        }

        if (operation == L_SAMPLED)
            return QuadraticVShearSampled(dir, vmaxt, vmaxb, incolor);
        else  /* operation == L_INTERPOLATED */
            return QuadraticVShearLI(dir, vmaxt, vmaxb, incolor);
    }

    // leptonica: pixQuadraticVShearSampled (warper.c)
    /// <summary>Quadratic vertical shear, taking the nearest source pixel.</summary>
    /// <param name="dir">L_WARP_TO_LEFT or L_WARP_TO_RIGHT</param>
    /// <param name="vmaxt">max vertical displacement at edge and at top</param>
    /// <param name="vmaxb">max vertical displacement at edge and at bottom</param>
    /// <param name="incolor">L_BRING_IN_WHITE or L_BRING_IN_BLACK</param>
    /// <returns>pixd stretched</returns>
    /// <remarks>
    /// (1) See pixQuadraticVShear() for details.
    /// </remarks>
    public Pix QuadraticVShearSampled(int dir, int vmaxt, int vmaxb, int incolor)
    {
        const string func = "pixQuadraticVShearSampled";

        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 1, 8 or 32 bpp");
        if (dir != L_WARP_TO_LEFT && dir != L_WARP_TO_RIGHT)
            throw Lept.Arg(func, "invalid direction");
        if (incolor != L_BRING_IN_WHITE && incolor != L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor");
        if (vmaxt == 0 && vmaxb == 0)
            return Copy(null, this);

        Pix pixd = CreateTemplate(this);
        pixd.SetBlackOrWhite(L_BRING_IN_WHITE);
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpls = Wpl;
        int wpld = pixd.Wpl;
        int wm = w - 1;
        int hm = h - 1;
        float denom1 = 1.0f / (float)h;
        float denom2 = 1.0f / (float)(wm * wm);
        float delrowt = 0, delrowb = 0;
        for (int j = 0; j < w; j++) {
            if (dir == L_WARP_TO_LEFT) {
                delrowt = (float)(vmaxt * (wm - j) * (wm - j)) * denom2;
                delrowb = (float)(vmaxb * (wm - j) * (wm - j)) * denom2;
            } else if (dir == L_WARP_TO_RIGHT) {
                delrowt = (float)(vmaxt * j * j) * denom2;
                delrowb = (float)(vmaxb * j * j) * denom2;
            }
            switch (d) {
                case 1:
                    for (int id = 0; id < h; id++) {
                        float dely = (delrowt * (hm - id) + delrowb * id) * denom1;
                        int i = id - (int)(dely + 0.5);
                        if (i < 0 || i > hm) continue;
                        ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                        Span<uint> lined = datad.AsSpan(id * wpld, wpld);
                        int val = GetDataBit(lines, j);
                        if (val != 0)
                            SetDataBit(lined, j);
                    }
                    break;
                case 8:
                    for (int id = 0; id < h; id++) {
                        float dely = (delrowt * (hm - id) + delrowb * id) * denom1;
                        int i = id - (int)(dely + 0.5);
                        if (i < 0 || i > hm) continue;
                        ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                        Span<uint> lined = datad.AsSpan(id * wpld, wpld);
                        int val = GetDataByte(lines, j);
                        SetDataByte(lined, j, val);
                    }
                    break;
                case 32:
                    for (int id = 0; id < h; id++) {
                        float dely = (delrowt * (hm - id) + delrowb * id) * denom1;
                        int i = id - (int)(dely + 0.5);
                        if (i < 0 || i > hm) continue;
                        datad[id * wpld + j] = datas[i * wpls + j];
                    }
                    break;
                default:
                    throw Lept.Arg(func, $"invalid depth: {d}");
            }
        }

        return pixd;
    }

    // leptonica: pixQuadraticVShearLI (warper.c)
    /// <summary>Quadratic vertical shear, with linear interpolation.</summary>
    /// <param name="dir">L_WARP_TO_LEFT or L_WARP_TO_RIGHT</param>
    /// <param name="vmaxt">max vertical displacement at edge and at top</param>
    /// <param name="vmaxb">max vertical displacement at edge and at bottom</param>
    /// <param name="incolor">L_BRING_IN_WHITE or L_BRING_IN_BLACK</param>
    /// <returns>pixd stretched; pixs is 8 or 32 bpp, or colormapped</returns>
    /// <remarks>
    /// (1) See pixQuadraticVShear() for details.
    /// </remarks>
    public Pix QuadraticVShearLI(int dir, int vmaxt, int vmaxb, int incolor)
    {
        const string func = "pixQuadraticVShearLI";

        GetDimensions(out int w, out int h, out int d);
        if (d == 1)
            throw Lept.Arg(func, "pixs is 1 bpp");
        PixColormap? cmap = Colormap;
        if (d != 8 && d != 32 && cmap == null)
            throw Lept.Arg(func, "pixs not 8, 32 bpp, or cmap");
        if (dir != L_WARP_TO_LEFT && dir != L_WARP_TO_RIGHT)
            throw Lept.Arg(func, "invalid direction");
        if (incolor != L_BRING_IN_WHITE && incolor != L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor");
        if (vmaxt == 0 && vmaxb == 0)
            return Copy(null, this);

        /* Remove any existing colormap */
        Pix pix;
        if (cmap != null)
            pix = RemoveColormap(REMOVE_CMAP_BASED_ON_SRC);
        else
            pix = Clone(this);
        d = pix.Depth;
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "invalid depth");

        /* Standard linear interp: subdivide each pixel into 64 parts */
        Pix pixd = CreateTemplate(pix);
        pixd.SetBlackOrWhite(L_BRING_IN_WHITE);
        uint[] datas = pix.Data;
        uint[] datad = pixd.Data;
        int wpls = pix.Wpl;
        int wpld = pixd.Wpl;
        int wm = w - 1;
        int hm = h - 1;
        float denom1 = 1.0f / (float)h;
        float denom2 = 1.0f / (float)(wm * wm);
        float delrowt = 0, delrowb = 0;
        for (int j = 0; j < w; j++) {
            if (dir == L_WARP_TO_LEFT) {
                delrowt = (float)(vmaxt * (wm - j) * (wm - j)) * denom2;
                delrowb = (float)(vmaxb * (wm - j) * (wm - j)) * denom2;
            } else if (dir == L_WARP_TO_RIGHT) {
                delrowt = (float)(vmaxt * j * j) * denom2;
                delrowb = (float)(vmaxb * j * j) * denom2;
            }
            switch (d) {
                case 8:
                    for (int id = 0; id < h; id++) {
                        float dely = (delrowt * (hm - id) + delrowb * id) * denom1;
                        int i = 64 * id - (int)(64.0 * dely);
                        int yp = i / 64;
                        int yf = i & 63;
                        if (yp < 0 || yp > hm) continue;
                        ReadOnlySpan<uint> lines = datas.AsSpan(yp * wpls, wpls);
                        Span<uint> lined = datad.AsSpan(id * wpld, wpld);
                        int val;
                        if (yp < hm) {
                            val = ((63 - yf) * GetDataByte(lines, j) +
                                   yf * GetDataByte(datas.AsSpan(yp * wpls + wpls, wpls), j) + 31) / 63;
                        } else {  /* yp == hm */
                            val = GetDataByte(lines, j);
                        }
                        SetDataByte(lined, j, val);
                    }
                    break;
                case 32:
                    for (int id = 0; id < h; id++) {
                        float dely = (delrowt * (hm - id) + delrowb * id) * denom1;
                        int i = 64 * id - (int)(64.0 * dely);
                        int yp = i / 64;
                        int yf = i & 63;
                        if (yp < 0 || yp > hm) continue;
                        int lines = yp * wpls;
                        int lined = id * wpld;
                        if (yp < hm) {
                            uint word0 = datas[lines + j];
                            uint word1 = datas[lines + wpls + j];
                            int rval = (int)((63 - yf) * ((word0 >> L_RED_SHIFT) & 0xff) +
                                       yf * ((word1 >> L_RED_SHIFT) & 0xff) + 31) / 63;
                            int gval = (int)((63 - yf) * ((word0 >> L_GREEN_SHIFT) & 0xff) +
                                       yf * ((word1 >> L_GREEN_SHIFT) & 0xff) + 31) / 63;
                            int bval = (int)((63 - yf) * ((word0 >> L_BLUE_SHIFT) & 0xff) +
                                       yf * ((word1 >> L_BLUE_SHIFT) & 0xff) + 31) / 63;
                            ComposeRGBPixel(rval, gval, bval, out datad[lined + j]);
                        } else {  /* yp == hm */
                            datad[lined + j] = datas[lines + j];
                        }
                    }
                    break;
                default:
                    throw Lept.Arg(func, $"invalid depth: {d}");
            }
        }

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *                     Stereo from a pair of images                     *
     *----------------------------------------------------------------------*/
    // leptonica: pixStereoFromPair (warper.c)
    /// <summary>Makes a red/cyan stereo image from a pair of 32 bpp images.</summary>
    /// <param name="pix1">32 bpp rgb; used to determine the output red channel</param>
    /// <param name="pix2">32 bpp rgb; used for the output green and blue channels</param>
    /// <param name="rwt">weight for r component in pix1 to determine the output red channel</param>
    /// <param name="gwt">weight for g component in pix1 to determine the output red channel</param>
    /// <param name="bwt">weight for b component in pix1 to determine the output red channel</param>
    /// <returns>pixd stereo enhanced</returns>
    /// <remarks>
    /// (1) pix1 and pix2 are a pair of stereo images, ideally taken
    ///     concurrently in the same plane, with some lateral translation.
    /// (2) The output red channel is determined from pix1.
    ///     The output green and blue channels are taken from the green
    ///     and blue channels, respectively, of pix2.
    /// (3) The weights determine how much of each component in pix1
    ///     goes into the output red channel.  The sum of weights
    ///     must be 1.0.  If it's not, we scale the weights to
    ///     satisfy this criterion.
    /// (4) The most general pixel mapping allowed here is:
    ///       rval = rwt * r1 + gwt * g1 + bwt * b1  (from pix1)
    ///       gval = g2   (from pix2)
    ///       bval = b2   (from pix2)
    /// (5) The simplest method is to use rwt = 1.0, gwt = 0.0, bwt = 0.0,
    ///     but this causes unpleasant visual artifacts with red in the image.
    ///     Use of green and blue from pix1 in the red channel,
    ///     instead of red, tends to fix that problem.
    /// </remarks>
    public static Pix StereoFromPair(Pix pix1, Pix pix2, float rwt, float gwt, float bwt)
    {
        const string func = "pixStereoFromPair";

        if (pix1.Depth != 32 || pix2.Depth != 32)
            throw Lept.Arg(func, "pix1, pix2 not both 32 bpp");

        /* Make sure the sum of weights is 1.0; otherwise, you can get
         * overflow in the gray value. */
        if (rwt == 0.0 && gwt == 0.0 && bwt == 0.0) {
            rwt = DefaultRedWeight;
            gwt = DefaultGreenWeight;
            bwt = DefaultBlueWeight;
        }
        float sum = rwt + gwt + bwt;
        if (Math.Abs(sum - 1.0) > 0.0001) {  /* maintain ratios with sum == 1.0 */
            Log.Warning(func, "weights don't sum to 1; maintaining ratios");
            rwt = rwt / sum;
            gwt = gwt / sum;
            bwt = bwt / sum;
        }

        pix1.GetDimensions(out int w, out int h, out _);
        Pix pixd = CreateTemplate(pix1);
        uint[] data1 = pix1.Data;
        uint[] data2 = pix2.Data;
        uint[] datad = pixd.Data;
        int wpl1 = pix1.Wpl;
        int wpl2 = pix2.Wpl;
        for (int i = 0; i < h; i++) {
            int line1 = i * wpl1;
            int line2 = i * wpl2;
            int lined = i * wpl1;  /* wpl1 works for pixd */
            for (int j = 0; j < w; j++) {
                uint word1 = data1[line1 + j];
                uint word2 = data2[line2 + j];
                int rval = (int)(rwt * ((word1 >> L_RED_SHIFT) & 0xff) +
                                 gwt * ((word1 >> L_GREEN_SHIFT) & 0xff) +
                                 bwt * ((word1 >> L_BLUE_SHIFT) & 0xff) + 0.5);
                int gval = (int)((word2 >> L_GREEN_SHIFT) & 0xff);
                int bval = (int)((word2 >> L_BLUE_SHIFT) & 0xff);
                ComposeRGBPixel(rval, gval, bval, out datad[lined + j]);
            }
        }

        return pixd;
    }
}
