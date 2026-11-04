using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Leptonica;

/*
 *  skew.c: Page skew detection and deskewing.
 *
 *  Skew is determined by pixel profiles, which are computed
 *  as pixel sums along the raster line for each line in the
 *  image.  By vertically shearing the image by a given angle,
 *  the sums can be computed quickly along the raster lines
 *  rather than along lines at that angle.  The score is
 *  computed from these line sums by taking the square of
 *  the DIFFERENCE between adjacent line sums, summed over
 *  all lines.  The skew angle is then found as the angle
 *  that maximizes the score.  The actual computation for
 *  any sheared image is done in the function
 *  pixFindDifferentialSquareSum().
 *
 *  The search for the angle that maximizes this score is
 *  most efficiently performed by first sweeping coarsely
 *  over angles, using a significantly reduced image (say, 4x
 *  reduction), to find the approximate maximum within a half
 *  degree or so, and then doing an interval-halving binary
 *  search at higher resolution to get the skew angle to
 *  within 1/20 degree or better.
 *
 *  The differential signal is used (rather than just using
 *  that variance of line sums) because it rejects the
 *  background noise due to total number of black pixels,
 *  and has maximum contributions from the baselines and
 *  x-height lines of text when the textlines are aligned
 *  with the raster lines.  It also works well in multicolumn
 *  pages where the textlines do not line up across columns.
 *
 *  The method is fast, accurate to within an angle (in radians)
 *  of approximately the inverse width in pixels of the image,
 *  and will work on a surprisingly small amount of text data
 *  (just a couple of text lines).  Consequently, it can
 *  also be used to find local skew if the skew were to vary
 *  significantly over the page.  Local skew determination
 *  is not very important except for locating lines of
 *  handwritten text that may be mixed with printed text.
 *
 *  Differences from C:
 *    - The angle-finding functions that return l_ok return a bool: true if
 *      the result is valid (C: 0), false if the measurement is not valid
 *      (C: 1), e.g. for an image without fg pixels.  Invalid arguments throw.
 *      The outputs %pangle and %pconf are out parameters, set to 0.0 when the
 *      result is not valid.
 *    - pixFindSkewOrthogonalRange returns void: it can only fail by invalid
 *      arguments, which throw.
 *    - pixFindNormalizedSquareSum always computes both ratios and the fg
 *      fraction (the C code skips the ones whose output pointer is NULL).
 *      Its return value is true if there are fg pixels (C: 0), false if not
 *      (C: 1).
 *    - pixDeskewGeneral treats a failure of pixRotate as C does, by returning
 *      the input pix.
 *    - The DEBUG_* printouts and gnuplot plots are not ported.
 *
 *  Not ported: nothing.
 *
 *  Stubs for callees in other files: none.
 */

/// <summary>Constants from pix.h used by skew.c.</summary>
public static partial class L
{
    /* Shear pivot locations */
    /// <summary>Shear the image about its upper-left corner.</summary>
    public const int L_SHEAR_ABOUT_CORNER = 1;
    /// <summary>Shear the image about its center.</summary>
    public const int L_SHEAR_ABOUT_CENTER = 2;
}

public sealed partial class Pix
{
    /* Default sweep angle parameters for pixFindSkew() */
    private const float DefaultSweepRange = 7.0f;   /* degrees */
    private const float DefaultSweepDelta = 1.0f;   /* degrees */

    /* Default final angle difference parameter for binary
     * search in pixFindSkew().  The expected accuracy is
     * not better than the inverse image width in pixels,
     * say, 1/2000 radians, or about 0.03 degrees. */
    private const float DefaultMinbsDelta = 0.01f;  /* degrees */

    /* Default scale factors for pixFindSkew() */
    private const int DefaultSweepReduction = 4;  /* sweep part; 4 is good */
    private const int DefaultBsReduction = 2;  /* binary search part */

    /* Minimum angle for deskewing in pixDeskew() */
    private const float MinDeskewAngle = 0.1f;  /* degree */

    /* Minimum allowed confidence (ratio) for deskewing in pixDeskew() */
    private const float MinAllowedConfidence = 3.0f;

    /* Minimum allowed maxscore to give nonzero confidence */
    private const int MinValidMaxscore = 10000;

    /* Constant setting threshold for minimum allowed minscore
     * to give nonzero confidence; multiply this constant by
     *  (height * width^2) */
    private const float MinscoreThreshFactor = 0.000002f;

    /* Default binarization threshold value.
     * This is set deliberately above 130 to capture light foreground
     * with poor printing or images that are out of focus.  */
    private const int DefaultBinaryThreshold = 160;

    /*-----------------------------------------------------------------------*
     *                       Top-level deskew interfaces                     *
     *-----------------------------------------------------------------------*/
    // leptonica: pixDeskewBoth (skew.c)
    /// <summary>Deskews horizontally and vertically.</summary>
    /// <param name="redsearch">for binary search: reduction factor = 1, 2 or 4; use 0 for default</param>
    /// <returns>pixd deskewed pix</returns>
    /// <remarks>
    /// (1) This binarizes if necessary and does both horizontal
    ///     and vertical deskewing, using the default parameters in
    ///     the underlying pixDeskew().  See usage there.
    /// </remarks>
    public Pix DeskewBoth(int redsearch)
    {
        Pix pix1, pix2, pix3, pix4;

        if (redsearch == 0)
            redsearch = DefaultBsReduction;
        else if (redsearch != 1 && redsearch != 2 && redsearch != 4)
            throw Lept.Arg("pixDeskewBoth", "redsearch not in {1,2,4}");

        pix1 = Deskew(redsearch);
        pix2 = pix1.Rotate90(1);
        pix3 = pix2.Deskew(redsearch);
        pix4 = pix3.Rotate90(-1);
        return pix4;
    }

    // leptonica: pixDeskew (skew.c)
    /// <summary>Deskews a pix, binarizing it if necessary to find the angle.</summary>
    /// <param name="redsearch">for binary search: reduction factor = 1, 2 or 4; use 0 for default</param>
    /// <returns>pixd deskewed pix</returns>
    /// <remarks>
    /// (1) This binarizes if necessary and finds the skew angle.  If the
    ///     angle is large enough and there is sufficient confidence,
    ///     it returns a deskewed image; otherwise, it returns a clone.
    /// (2) Typical values at 300 ppi for %redsearch are 2 and 4.
    ///     At 75 ppi, one should use %redsearch = 1.
    /// </remarks>
    public Pix Deskew(int redsearch)
    {
        if (redsearch == 0)
            redsearch = DefaultBsReduction;
        else if (redsearch != 1 && redsearch != 2 && redsearch != 4)
            throw Lept.Arg("pixDeskew", "redsearch not in {1,2,4}");

        return DeskewGeneral(0, 0.0f, 0.0f, redsearch, 0, out _, out _);
    }

    // leptonica: pixFindSkewAndDeskew (skew.c)
    /// <summary>Deskews a pix and returns the angle and the confidence.</summary>
    /// <param name="redsearch">for binary search: reduction factor = 1, 2 or 4; use 0 for default</param>
    /// <param name="angle">angle required to deskew, in degrees</param>
    /// <param name="conf">conf value is ratio of max/min scores</param>
    /// <returns>pixd deskewed pix</returns>
    /// <remarks>
    /// (1) This binarizes if necessary and finds the skew angle.  If the
    ///     angle is large enough and there is sufficient confidence,
    ///     it returns a deskewed image; otherwise, it returns a clone.
    /// </remarks>
    public Pix FindSkewAndDeskew(int redsearch, out float angle, out float conf)
    {
        angle = 0.0f;
        conf = 0.0f;
        if (redsearch == 0)
            redsearch = DefaultBsReduction;
        else if (redsearch != 1 && redsearch != 2 && redsearch != 4)
            throw Lept.Arg("pixFindSkewAndDeskew", "redsearch not in {1,2,4}");

        return DeskewGeneral(0, 0.0f, 0.0f, redsearch, 0, out angle, out conf);
    }

    // leptonica: pixDeskewGeneral (skew.c)
    /// <summary>Deskews a pix with all the search parameters.</summary>
    /// <param name="redsweep">for linear search: reduction factor = 1, 2 or 4; use 0 for default</param>
    /// <param name="sweeprange">in degrees in each direction from 0; use 0.0 for default</param>
    /// <param name="sweepdelta">in degrees; use 0.0 for default</param>
    /// <param name="redsearch">for binary search: reduction factor = 1, 2 or 4; use 0 for default</param>
    /// <param name="thresh">for binarizing the image; use 0 for default</param>
    /// <param name="angle">angle required to deskew, in degrees</param>
    /// <param name="conf">conf value is ratio of max/min scores</param>
    /// <returns>pixd deskewed pix</returns>
    /// <remarks>
    /// (1) This binarizes if necessary and finds the skew angle.  If the
    ///     angle is large enough and there is sufficient confidence,
    ///     it returns a deskewed image; otherwise, it returns a clone.
    /// </remarks>
    public Pix DeskewGeneral(int redsweep, float sweeprange, float sweepdelta, int redsearch,
                             int thresh, out float angle, out float conf)
    {
        const string func = "pixDeskewGeneral";
        bool ok;
        int depth;
        float deg2rad;
        Pix pixb, pixd;

        angle = 0.0f;
        conf = 0.0f;
        if (redsweep == 0)
            redsweep = DefaultSweepReduction;
        else if (redsweep != 1 && redsweep != 2 && redsweep != 4)
            throw Lept.Arg(func, "redsweep not in {1,2,4}");
        if (sweeprange == 0.0)
            sweeprange = DefaultSweepRange;
        if (sweepdelta == 0.0)
            sweepdelta = DefaultSweepDelta;
        if (redsearch == 0)
            redsearch = DefaultBsReduction;
        else if (redsearch != 1 && redsearch != 2 && redsearch != 4)
            throw Lept.Arg(func, "redsearch not in {1,2,4}");
        if (thresh == 0)
            thresh = DefaultBinaryThreshold;

        deg2rad = 3.1415926535f / 180.0f;

        /* Binarize if necessary */
        depth = Depth;
        if (depth == 1)
            pixb = this;
        else
            pixb = ConvertTo1(thresh);

        /* Use the 1 bpp image to find the skew */
        if (redsearch > redsweep) {  /* an invalid combination; C: the call fails */
            Log.Error("pixFindSkewSweepAndSearchScorePivot", "redsearch must not exceed redsweep");
            ok = false;
        } else {
            ok = pixb.FindSkewSweepAndSearch(out angle, out conf, redsweep, redsearch,
                                             sweeprange, sweepdelta, DefaultMinbsDelta);
        }
        if (!ok)
            return Clone(this);

        if (Math.Abs(angle) < MinDeskewAngle || conf < MinAllowedConfidence)
            return Clone(this);

        try {
            pixd = Rotate(deg2rad * angle, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, 0, 0);
        } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
            return Clone(this);
        }
        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *                  Simple top-level angle-finding interface             *
     *-----------------------------------------------------------------------*/
    // leptonica: pixFindSkew (skew.c)
    /// <summary>Finds the skew angle of a 1 bpp pix, with default parameters.</summary>
    /// <param name="angle">angle required to deskew, in degrees</param>
    /// <param name="conf">confidence value is ratio max/min scores</param>
    /// <returns>true if OK; false if the angle measurement is not valid</returns>
    /// <remarks>
    /// (1) This is a simple high-level interface, that uses default
    ///     values of the parameters for reasonable speed and accuracy.
    /// (2) The angle returned is the negative of the skew angle of
    ///     the image.  It is the angle required for deskew.
    ///     Clockwise rotations are positive angles.
    /// </remarks>
    public bool FindSkew(out float angle, out float conf)
    {
        angle = 0.0f;
        conf = 0.0f;
        if (Depth != 1)
            throw Lept.Arg("pixFindSkew", "pixs not 1 bpp");

        return FindSkewSweepAndSearch(out angle, out conf,
                                      DefaultSweepReduction,
                                      DefaultBsReduction,
                                      DefaultSweepRange,
                                      DefaultSweepDelta,
                                      DefaultMinbsDelta);
    }

    /*-----------------------------------------------------------------------*
     *                       Basic angle-finding functions                   *
     *-----------------------------------------------------------------------*/
    // leptonica: pixFindSkewSweep (skew.c)
    /// <summary>Finds the skew angle by a sweep over equally spaced angles.</summary>
    /// <param name="angle">angle required to deskew, in degrees</param>
    /// <param name="reduction">factor = 1, 2, 4 or 8</param>
    /// <param name="sweeprange">half the full range; assumed about 0; in degrees</param>
    /// <param name="sweepdelta">angle increment of sweep; in degrees</param>
    /// <returns>true if OK; false if the angle measurement is not valid</returns>
    /// <remarks>
    /// (1) This examines the 'score' for skew angles with equal intervals.
    /// (2) Caller must check the return value for validity of the result.
    /// </remarks>
    public bool FindSkewSweep(out float angle, int reduction, float sweeprange, float sweepdelta)
    {
        const string func = "pixFindSkewSweep";
        bool bzero;
        int i, nangles;
        float deg2rad, theta;
        float sum, maxscore, maxangle;
        Numa natheta, nascore;
        Pix pix, pixt;

        angle = 0.0f;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (reduction != 1 && reduction != 2 && reduction != 4 && reduction != 8)
            throw Lept.Arg(func, "reduction must be in {1,2,4,8}");

        deg2rad = 3.1415926535f / 180.0f;

        /* Generate reduced image, if requested */
        if (reduction == 1)
            pix = this;
        else if (reduction == 2)
            pix = ReduceRankBinaryCascade(1, 0, 0, 0);
        else if (reduction == 4)
            pix = ReduceRankBinaryCascade(1, 1, 0, 0);
        else /* reduction == 8 */
            pix = ReduceRankBinaryCascade(1, 1, 2, 0);

        bzero = pix.Zero();
        if (bzero)
            return false;

        if (Colormap == null &&
            LeptGpu.TryGetActive((long)Width * Height, 20, out IGpuAccelerator? gpuSkew) &&
            gpuSkew.TryFindSkewSweep(this, reduction, sweeprange, sweepdelta, out float gpuAngle)) {
            angle = gpuAngle;
            return true;
        }

        nangles = (int)((2.0 * sweeprange) / sweepdelta + 1);
        natheta = Numa.Create(nangles);
        nascore = Numa.Create(nangles);

        if (pix.Colormap == null && nangles > 0) {
            using var scoreBuf = new TempBuffer<float>(nangles);
            float[] scoreArr = scoreBuf.Array;
            int workPerAngle = Math.Max(1, (pix.Width * pix.Height) >> 4);
            LeptParallel.ForRows(nangles, workPerAngle, (aStart, aEnd) => {
                for (int idx = aStart; idx < aEnd; idx++) {
                    float th = -sweeprange + idx * sweepdelta;
                    scoreArr[idx] = VShearRowDiffSquareSum(pix, 0, deg2rad * th);
                }
            });
            for (i = 0; i < nangles; i++) {
                theta = -sweeprange + i * sweepdelta;
                nascore.AddNumber(scoreArr[i]);
                natheta.AddNumber(theta);
            }
        } else {
            pixt = CreateTemplate(pix);
            for (i = 0; i < nangles; i++) {
                theta = -sweeprange + i * sweepdelta;   /* degrees */

                /* Shear pix about the UL corner and put the result in pixt */
                VShearCorner(pixt, pix, deg2rad * theta, L.L_BRING_IN_WHITE);

                /* Get score */
                sum = pixt.FindDifferentialSquareSum();

                /* Save the result in the output arrays */
                nascore.AddNumber(sum);
                natheta.AddNumber(theta);
            }
        }

        /* Find the location of the maximum (i.e., the skew angle)
         * by fitting the largest data point and its two neighbors
         * to a quadratic, using lagrangian interpolation.  */
        nascore.FitMax(out maxscore, natheta, out maxangle);
        angle = maxangle;

        return true;
    }

    // leptonica: pixFindSkewSweepAndSearch (skew.c)
    /// <summary>Finds the skew angle by a sweep followed by a binary search.</summary>
    /// <param name="angle">angle required to deskew; in degrees</param>
    /// <param name="conf">confidence given by ratio of max/min score</param>
    /// <param name="redsweep">sweep reduction factor = 1, 2, 4 or 8</param>
    /// <param name="redsearch">binary search reduction factor = 1, 2, 4 or 8; and must not exceed redsweep</param>
    /// <param name="sweeprange">half the full range, assumed about 0; in degrees</param>
    /// <param name="sweepdelta">angle increment of sweep; in degrees</param>
    /// <param name="minbsdelta">min binary search increment angle; in degrees</param>
    /// <returns>true if OK; false if the angle measurement is not valid</returns>
    /// <remarks>
    /// (1) This finds the skew angle, doing first a sweep through a set
    ///     of equal angles, and then doing a binary search until
    ///     convergence.
    /// (2) Caller must check the return value for validity of the result.
    /// (3) In computing the differential line sum variance score, we sum
    ///     the result over scanlines, but we always skip:
    ///      ~ at least one scanline
    ///      ~ not more than 10% of the image height
    ///      ~ not more than 5% of the image width
    /// (4) See also notes in pixFindSkewSweepAndSearchScore()
    /// </remarks>
    public bool FindSkewSweepAndSearch(out float angle, out float conf, int redsweep, int redsearch,
                                       float sweeprange, float sweepdelta, float minbsdelta)
    {
        return FindSkewSweepAndSearchScore(out angle, out conf, out _,
                                           redsweep, redsearch, 0.0f, sweeprange,
                                           sweepdelta, minbsdelta);
    }

    // leptonica: pixFindSkewSweepAndSearchScore (skew.c)
    /// <summary>Finds the skew angle by a sweep followed by a binary search, and returns the max score.</summary>
    /// <param name="angle">angle required to deskew; in degrees</param>
    /// <param name="conf">confidence given by ratio of max/min score</param>
    /// <param name="endscore">max score</param>
    /// <param name="redsweep">sweep reduction factor = 1, 2, 4 or 8</param>
    /// <param name="redsearch">binary search reduction factor = 1, 2, 4 or 8; and must not exceed redsweep</param>
    /// <param name="sweepcenter">angle about which sweep is performed; in degrees</param>
    /// <param name="sweeprange">half the full range, taken about sweepcenter; in degrees</param>
    /// <param name="sweepdelta">angle increment of sweep; in degrees</param>
    /// <param name="minbsdelta">min binary search increment angle; in degrees</param>
    /// <returns>true if OK; false if the angle measurement is not valid</returns>
    /// <remarks>
    /// (1) This finds the skew angle, doing first a sweep through a set
    ///     of equal angles, and then doing a binary search until convergence.
    /// (2) There are two built-in constants that determine if the
    ///     returned confidence is nonzero:
    ///       ~ MinValidMaxscore (minimum allowed maxscore)
    ///       ~ MinscoreThreshFactor (determines minimum allowed
    ///            minscore, by multiplying by (height * width^2)
    ///     If either of these conditions is not satisfied, the returned
    ///     confidence value will be zero.  The maxscore is optionally
    ///     returned in this function to allow evaluation of the
    ///     resulting angle by a method that is independent of the
    ///     returned confidence value.
    /// (3) The larger the confidence value, the greater the probability
    ///     that the proper alignment is given by the angle that maximizes
    ///     variance.  It should be compared to a threshold, which depends
    ///     on the application.  Values between 3.0 and 6.0 are common.
    /// (4) By default, the shear is about the UL corner.
    /// </remarks>
    public bool FindSkewSweepAndSearchScore(out float angle, out float conf, out float endscore,
                                            int redsweep, int redsearch, float sweepcenter,
                                            float sweeprange, float sweepdelta, float minbsdelta)
    {
        // NOTE(upstream): %sweepcenter is ignored; the sweep is always about 0.0
        return FindSkewSweepAndSearchScorePivot(out angle, out conf, out endscore,
                                                redsweep, redsearch, 0.0f,
                                                sweeprange, sweepdelta,
                                                minbsdelta,
                                                L.L_SHEAR_ABOUT_CORNER);
    }

    // leptonica: pixFindSkewSweepAndSearchScorePivot (skew.c)
    /// <summary>Finds the skew angle by a sweep followed by a binary search, with a choice of the shear pivot.</summary>
    /// <param name="angle">angle required to deskew; in degrees</param>
    /// <param name="conf">confidence given by ratio of max/min score</param>
    /// <param name="endscore">max score</param>
    /// <param name="redsweep">sweep reduction factor = 1, 2, 4 or 8</param>
    /// <param name="redsearch">binary search reduction factor = 1, 2, 4 or 8; and must not exceed redsweep</param>
    /// <param name="sweepcenter">angle about which sweep is performed; in degrees</param>
    /// <param name="sweeprange">half the full range, taken about sweepcenter; in degrees</param>
    /// <param name="sweepdelta">angle increment of sweep; in degrees</param>
    /// <param name="minbsdelta">min binary search increment angle; in degrees</param>
    /// <param name="pivot">L_SHEAR_ABOUT_CORNER, L_SHEAR_ABOUT_CENTER</param>
    /// <returns>true if OK; false if the angle measurement is not valid</returns>
    /// <remarks>
    /// (1) See notes in pixFindSkewSweepAndSearchScore().
    /// (2) This allows choice of shear pivoting from either the UL corner
    ///     or the center.  For small angles, the ability to discriminate
    ///     angles is better with shearing from the UL corner.  However,
    ///     for large angles (say, greater than 20 degrees), it is better
    ///     to shear about the center because a shear from the UL corner
    ///     loses too much of the image.
    /// </remarks>
    public bool FindSkewSweepAndSearchScorePivot(out float angle, out float conf, out float endscore,
                                                 int redsweep, int redsearch, float sweepcenter,
                                                 float sweeprange, float sweepdelta, float minbsdelta,
                                                 int pivot)
    {
        const string func = "pixFindSkewSweepAndSearchScorePivot";
        bool bzero;
        int i, nangles, n, ratio, maxindex, minloc;
        int width, height;
        float deg2rad, theta, delta;
        float sum, maxscore, maxangle;
        float centerangle, leftcenterangle, rightcenterangle;
        float lefttemp, righttemp;
        float[] bsearchscore = new float[5];
        float minscore, minthresh;
        float rangeleft;
        Numa natheta, nascore;
        Pix pixsw, pixsch, pixt1, pixt2;

        endscore = 0.0f;
        angle = 0.0f;
        conf = 0.0f;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");
        if (redsweep != 1 && redsweep != 2 && redsweep != 4 && redsweep != 8)
            throw Lept.Arg(func, "redsweep must be in {1,2,4,8}");
        if (redsearch != 1 && redsearch != 2 && redsearch != 4 && redsearch != 8)
            throw Lept.Arg(func, "redsearch must be in {1,2,4,8}");
        if (redsearch > redsweep)
            throw Lept.Arg(func, "redsearch must not exceed redsweep");
        if (pivot != L.L_SHEAR_ABOUT_CORNER && pivot != L.L_SHEAR_ABOUT_CENTER)
            throw Lept.Arg(func, "invalid pivot");

        deg2rad = 3.1415926535f / 180.0f;

        /* Generate reduced image for binary search, if requested */
        if (redsearch == 1)
            pixsch = this;
        else if (redsearch == 2)
            pixsch = ReduceRankBinaryCascade(1, 0, 0, 0);
        else if (redsearch == 4)
            pixsch = ReduceRankBinaryCascade(1, 1, 0, 0);
        else  /* redsearch == 8 */
            pixsch = ReduceRankBinaryCascade(1, 1, 2, 0);

        bzero = pixsch.Zero();
        if (bzero)
            return false;

        /* Generate reduced image for sweep, if requested */
        ratio = redsweep / redsearch;
        if (ratio == 1) {
            pixsw = pixsch;
        } else {  /* ratio > 1 */
            if (ratio == 2)
                pixsw = pixsch.ReduceRankBinaryCascade(1, 0, 0, 0);
            else if (ratio == 4)
                pixsw = pixsch.ReduceRankBinaryCascade(1, 2, 0, 0);
            else  /* ratio == 8 */
                pixsw = pixsch.ReduceRankBinaryCascade(1, 2, 2, 0);
        }

        nangles = (int)((2.0 * sweeprange) / sweepdelta + 1);
        natheta = Numa.Create(nangles);
        nascore = Numa.Create(nangles);

        /* Do sweep */
        rangeleft = sweepcenter - sweeprange;
        bool fastShear = pixsw.Colormap == null && pixsch.Colormap == null;
        int xlocSw = pivot == L.L_SHEAR_ABOUT_CORNER ? 0 : pixsw.Width / 2;
        int xlocSch = pivot == L.L_SHEAR_ABOUT_CORNER ? 0 : pixsch.Width / 2;

        if (fastShear && nangles > 0) {
            pixt1 = null!;
            pixt2 = null!;
            using var scoreBuf = new TempBuffer<float>(nangles);
            float[] scoreArr = scoreBuf.Array;
            int workPerAngle = Math.Max(1, (pixsw.Width * pixsw.Height) >> 4);
            LeptParallel.ForRows(nangles, workPerAngle, (aStart, aEnd) => {
                for (int idx = aStart; idx < aEnd; idx++) {
                    float th = rangeleft + idx * sweepdelta;
                    scoreArr[idx] = VShearRowDiffSquareSum(pixsw, xlocSw, deg2rad * th);
                }
            });
            for (i = 0; i < nangles; i++) {
                theta = rangeleft + i * sweepdelta;
                nascore.AddNumber(scoreArr[i]);
                natheta.AddNumber(theta);
            }
        } else {
            pixt1 = CreateTemplate(pixsw);
            pixt2 = ratio == 1 ? pixt1 : CreateTemplate(pixsch);
            for (i = 0; i < nangles; i++) {
                theta = rangeleft + i * sweepdelta;   /* degrees */

                /* Shear pix and put the result in pixt1 */
                if (pivot == L.L_SHEAR_ABOUT_CORNER)
                    VShearCorner(pixt1, pixsw, deg2rad * theta, L.L_BRING_IN_WHITE);
                else
                    VShearCenter(pixt1, pixsw, deg2rad * theta, L.L_BRING_IN_WHITE);

                /* Get score */
                sum = pixt1.FindDifferentialSquareSum();

                /* Save the result in the output arrays */
                nascore.AddNumber(sum);
                natheta.AddNumber(theta);
            }
        }

        /* Find the largest of the set (maxscore at maxangle) */
        nascore.GetMax(out maxscore, out maxindex);
        maxangle = natheta.GetFValue(maxindex);

        /* Check if the max is at the end of the sweep. */
        n = natheta.Count;
        if (maxindex == 0 || maxindex == n - 1) {
            Log.Warning(func, "max found at sweep edge");
            return true;
        }

        /* Empty the numas for re-use */
        nascore.Empty();
        natheta.Empty();

        /* Do binary search to find skew angle.
         * First, set up initial three points. */
        centerangle = maxangle;
        if (fastShear) {
            bsearchscore[2] = VShearRowDiffSquareSum(pixsch, xlocSch, deg2rad * centerangle);
            bsearchscore[0] = VShearRowDiffSquareSum(pixsch, xlocSch, deg2rad * (centerangle - sweepdelta));
            bsearchscore[4] = VShearRowDiffSquareSum(pixsch, xlocSch, deg2rad * (centerangle + sweepdelta));
        } else if (pivot == L.L_SHEAR_ABOUT_CORNER) {
            VShearCorner(pixt2, pixsch, deg2rad * centerangle, L.L_BRING_IN_WHITE);
            bsearchscore[2] = pixt2.FindDifferentialSquareSum();
            VShearCorner(pixt2, pixsch, deg2rad * (centerangle - sweepdelta),
                         L.L_BRING_IN_WHITE);
            bsearchscore[0] = pixt2.FindDifferentialSquareSum();
            VShearCorner(pixt2, pixsch, deg2rad * (centerangle + sweepdelta),
                         L.L_BRING_IN_WHITE);
            bsearchscore[4] = pixt2.FindDifferentialSquareSum();
        } else {
            VShearCenter(pixt2, pixsch, deg2rad * centerangle, L.L_BRING_IN_WHITE);
            bsearchscore[2] = pixt2.FindDifferentialSquareSum();
            VShearCenter(pixt2, pixsch, deg2rad * (centerangle - sweepdelta),
                         L.L_BRING_IN_WHITE);
            bsearchscore[0] = pixt2.FindDifferentialSquareSum();
            VShearCenter(pixt2, pixsch, deg2rad * (centerangle + sweepdelta),
                         L.L_BRING_IN_WHITE);
            bsearchscore[4] = pixt2.FindDifferentialSquareSum();
        }

        nascore.AddNumber(bsearchscore[2]);
        natheta.AddNumber(centerangle);
        nascore.AddNumber(bsearchscore[0]);
        natheta.AddNumber(centerangle - sweepdelta);
        nascore.AddNumber(bsearchscore[4]);
        natheta.AddNumber(centerangle + sweepdelta);

        /* Start the search */
        delta = 0.5f * sweepdelta;
        while (delta >= minbsdelta)
        {
            /* Get the left intermediate score */
            leftcenterangle = centerangle - delta;
            rightcenterangle = centerangle + delta;
            if (fastShear) {
                bsearchscore[1] = VShearRowDiffSquareSum(pixsch, xlocSch, deg2rad * leftcenterangle);
                bsearchscore[3] = VShearRowDiffSquareSum(pixsch, xlocSch, deg2rad * rightcenterangle);
            } else {
                if (pivot == L.L_SHEAR_ABOUT_CORNER)
                    VShearCorner(pixt2, pixsch, deg2rad * leftcenterangle,
                                 L.L_BRING_IN_WHITE);
                else
                    VShearCenter(pixt2, pixsch, deg2rad * leftcenterangle,
                                 L.L_BRING_IN_WHITE);
                bsearchscore[1] = pixt2.FindDifferentialSquareSum();

                if (pivot == L.L_SHEAR_ABOUT_CORNER)
                    VShearCorner(pixt2, pixsch, deg2rad * rightcenterangle,
                                 L.L_BRING_IN_WHITE);
                else
                    VShearCenter(pixt2, pixsch, deg2rad * rightcenterangle,
                                 L.L_BRING_IN_WHITE);
                bsearchscore[3] = pixt2.FindDifferentialSquareSum();
            }
            nascore.AddNumber(bsearchscore[1]);
            natheta.AddNumber(leftcenterangle);
            nascore.AddNumber(bsearchscore[3]);
            natheta.AddNumber(rightcenterangle);

            /* Find the maximum of the five scores and its location.
             * Note that the maximum must be in the center
             * three values, not in the end two. */
            maxscore = bsearchscore[1];
            maxindex = 1;
            for (i = 2; i < 4; i++) {
                if (bsearchscore[i] > maxscore) {
                    maxscore = bsearchscore[i];
                    maxindex = i;
                }
            }

            /* Set up score array to interpolate for the next iteration */
            lefttemp = bsearchscore[maxindex - 1];
            righttemp = bsearchscore[maxindex + 1];
            bsearchscore[2] = maxscore;
            bsearchscore[0] = lefttemp;
            bsearchscore[4] = righttemp;

            /* Get new center angle and delta for next iteration */
            centerangle = centerangle + delta * (maxindex - 2);
            delta = 0.5f * delta;
        }
        angle = centerangle;

        endscore = bsearchscore[2];

        /* Return the ratio of Max score over Min score
         * as a confidence value.  Don't trust if the Min score
         * is too small, which can happen if the image is all black
         * with only a few white pixels interspersed.  In that case,
         * we get a contribution from the top and bottom edges when
         * vertically sheared, but this contribution becomes zero when
         * the shear angle is zero.  For zero shear angle, the only
         * contribution will be from the white pixels.  We expect that
         * the signal goes as the product of the (height * width^2),
         * so we compute a (hopefully) normalized minimum threshold as
         * a function of these dimensions.  */
        nascore.GetMin(out minscore, out minloc);
        width = pixsch.Width;
        height = pixsch.Height;
        minthresh = MinscoreThreshFactor * width * width * height;

        if (minscore > minthresh)
            conf = maxscore / minscore;
        else
            conf = 0.0f;

        /* Don't trust it if too close to the edge of the sweep
         * range or if maxscore is small */
        if ((centerangle > rangeleft + 2 * sweeprange - sweepdelta) ||
            (centerangle < rangeleft + sweepdelta) ||
            (maxscore < MinValidMaxscore))
            conf = 0.0f;

        return true;
    }

    /*---------------------------------------------------------------------*
     *    Search over arbitrary range of angles in orthogonal directions   *
     *---------------------------------------------------------------------*/
    // leptonica: pixFindSkewOrthogonalRange (skew.c)
    /// <summary>Finds the skew angle over the range about 0 and about 90 degrees.</summary>
    /// <param name="angle">angle required to deskew; in degrees cw</param>
    /// <param name="conf">confidence given by ratio of max/min score</param>
    /// <param name="redsweep">sweep reduction factor = 1, 2, 4 or 8</param>
    /// <param name="redsearch">binary search reduction factor = 1, 2, 4 or 8; and must not exceed redsweep</param>
    /// <param name="sweeprange">half the full range in each orthogonal direction, taken about 0, in degrees</param>
    /// <param name="sweepdelta">angle increment of sweep; in degrees</param>
    /// <param name="minbsdelta">min binary search increment angle; in degrees</param>
    /// <param name="confprior">amount by which confidence of 90 degree rotated
    /// result is reduced when comparing with unrotated confidence value</param>
    /// <remarks>
    /// (1) This searches for the skew angle, first in the range
    ///     [-sweeprange, sweeprange], and then in
    ///     [90 - sweeprange, 90 + sweeprange], with angles measured
    ///     clockwise.  For exploring the full range of possibilities,
    ///     suggest using sweeprange = 47.0 degrees, giving some overlap
    ///     at 45 and 135 degrees.  From these results, and discounting
    ///     the the second confidence by %confprior, it selects the
    ///     angle for maximal differential variance.  If the angle
    ///     is larger than pi/4, the angle found after 90 degree rotation
    ///     is selected.
    /// (2) The larger the confidence value, the greater the probability
    ///     that the proper alignment is given by the angle that maximizes
    ///     variance.  It should be compared to a threshold, which depends
    ///     on the application.  Values between 3.0 and 6.0 are common.
    /// (3) Allowing for both portrait and landscape searches is more
    ///     difficult, because if the signal from the text lines is weak,
    ///     a signal from vertical rules can be larger!
    ///     The most difficult documents to deskew have some or all of:
    ///       (a) Multiple columns, not aligned
    ///       (b) Black lines along the vertical edges
    ///       (c) Text from two pages, and at different angles
    ///     Rule of thumb for resolution:
    ///       (a) If the margins are clean, you can work at 75 ppi,
    ///           although 100 ppi is safer.
    ///       (b) If there are vertical lines in the margins, do not
    ///           work below 150 ppi.  The signal from the text lines must
    ///           exceed that from the margin lines.
    /// (4) Choosing the %confprior parameter depends on knowing something
    ///     about the source of image.  However, we're not using
    ///     real probabilities here, so its use is qualitative.
    ///     If landscape and portrait are equally likely, use
    ///     %confprior = 0.0.  If the likelihood of portrait (non-rotated)
    ///     is 100 times higher than that of landscape, we want to reduce
    ///     the chance that we rotate to landscape in a situation where
    ///     the landscape signal is accidentally larger than the
    ///     portrait signal.  To do this use a positive value of
    ///     %confprior; say 1.5.
    /// </remarks>
    public void FindSkewOrthogonalRange(out float angle, out float conf, int redsweep, int redsearch,
                                        float sweeprange, float sweepdelta, float minbsdelta,
                                        float confprior)
    {
        float angle1, conf1, score1, angle2, conf2, score2;
        Pix pixr;

        angle = 0.0f;
        conf = 0.0f;
        if (Depth != 1)
            throw Lept.Arg("pixFindSkewOrthogonalRange", "pixs not defined or not 1 bpp");

        FindSkewSweepAndSearchScorePivot(out angle1, out conf1, out score1,
                                         redsweep, redsearch, 0.0f,
                                         sweeprange, sweepdelta, minbsdelta,
                                         L.L_SHEAR_ABOUT_CORNER);
        pixr = RotateOrth(1);
        pixr.FindSkewSweepAndSearchScorePivot(out angle2, out conf2, out score2,
                                         redsweep, redsearch, 0.0f,
                                         sweeprange, sweepdelta, minbsdelta,
                                         L.L_SHEAR_ABOUT_CORNER);

        if (conf1 > conf2 - confprior) {
            angle = angle1;
            conf = conf1;
        } else {
            angle = -90.0f + angle2;
            conf = conf2;
        }
    }

    /*----------------------------------------------------------------*
     *                  Differential square sum function              *
     *----------------------------------------------------------------*/
    // leptonica: pixFindDifferentialSquareSum (skew.c)
    /// <summary>Sums the squares of the differences of the fg pixel counts of adjacent rows.</summary>
    /// <returns>the sum</returns>
    /// <remarks>
    /// (1) At the top and bottom, we skip:
    ///      ~ at least one scanline
    ///      ~ not more than 10% of the image height
    ///      ~ not more than 5% of the image width
    /// </remarks>
    public float FindDifferentialSquareSum()
    {
        if (Depth != 1)
            throw Lept.Arg("pixCountPixelsByRow", "pix undefined or not 1 bpp");

        int w = Width;
        int h = Height;
        int skiph = (int)(0.05 * w);  /* skip for max shear of 0.025 radians */
        int skip = Math.Min(h / 10, skiph);  /* don't remove more than 10% of image */
        int nskip = Math.Max(skip / 2, 1);  /* at top & bot; skip at least one line */
        int endRow = h - nskip;
        if (nskip >= endRow)
            return 0.0f;

        uint[] data = Data;
        int wpl = Wpl;
        int fullwords = w >> 5;
        int endbits = w & 31;
        uint endmask = endbits == 0 ? 0u : (0xffffffffu << (32 - endbits));
        ref uint dataRef = ref MemoryMarshal.GetArrayDataReference(data);

        float val1 = PopCountRowUnchecked(ref dataRef, (nskip - 1) * wpl, fullwords, endmask);
        float sum = 0.0f;
        for (int i = nskip; i < endRow; i++) {
            float val2 = PopCountRowUnchecked(ref dataRef, i * wpl, fullwords, endmask);
            float diff = val2 - val1;
            sum += diff * diff;
            val1 = val2;
        }
        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PopCountRowUnchecked(ref uint dataRef, int rowOff, int fullwords, uint endmask)
    {
        ref uint r = ref Unsafe.Add(ref dataRef, rowOff);
        int sum = 0;
        int j = 0;
        for (; j + 2 <= fullwords; j += 2) {
            ulong pair = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref r, j)));
            sum += BitOperations.PopCount(pair);
        }
        for (; j < fullwords; j++)
            sum += BitOperations.PopCount(Unsafe.Add(ref r, j));
        if (endmask != 0u)
            sum += BitOperations.PopCount(Unsafe.Add(ref r, fullwords) & endmask);
        return sum;
    }

    private static float VShearRowDiffSquareSum(Pix pixs, int xloc, float radang)
    {
        radang = NormalizeAngleForShear(radang, MinDiffFromHalfPi);
        if (radang == 0.0f || Math.Tan(radang) == 0.0)
            return pixs.FindDifferentialSquareSum();

        int w = pixs.Width;
        int h = pixs.Height;
        int skiph = (int)(0.05 * w);
        int skip = Math.Min(h / 10, skiph);
        int nskip = Math.Max(skip / 2, 1);
        int endRow = h - nskip;
        if (nskip >= endRow)
            return 0.0f;

        int yMin = nskip - 1;
        int yMax = endRow - 1;
        int nRows = yMax - yMin + 1;

        using var countsBuf = nRows <= 1024 ? default : new TempBuffer<int>(nRows);
        Span<int> counts = nRows <= 1024 ? stackalloc int[nRows] : countsBuf.Span;
        counts.Clear();

        uint[] datas = pixs.Data;
        int wpl = pixs.Wpl;
        ref uint dataRef = ref MemoryMarshal.GetArrayDataReference(datas);
        ref int countsRef = ref MemoryMarshal.GetReference(counts);

        int sign = radang < 0 ? -1 : 1;
        float tanangle = (float)Math.Tan(radang);
        float invangle = (float)Math.Abs(1.0 / tanangle);
        int initxincr = (int)(invangle / 2.0);

        AccumulateStripPopCount(ref dataRef, wpl, w, h, xloc - initxincr, 2 * initxincr, 0, yMin, yMax, ref countsRef);

        for (int vshift = 1, x = xloc + initxincr; x < w; vshift++) {
            int xincr = (int)(invangle * (vshift + 0.5) + 0.5) - (x - xloc);
            if (w - x < xincr)
                xincr = w - x;
            AccumulateStripPopCount(ref dataRef, wpl, w, h, x, xincr, sign * vshift, yMin, yMax, ref countsRef);
            x += xincr;
        }

        for (int vshift = -1, x = xloc - initxincr; x > 0; vshift--) {
            int xincr = (x - xloc) - (int)(invangle * (vshift - 0.5) + 0.5);
            if (x < xincr)
                xincr = x;
            AccumulateStripPopCount(ref dataRef, wpl, w, h, x - xincr, xincr, sign * vshift, yMin, yMax, ref countsRef);
            x -= xincr;
        }

        float sum = 0.0f;
        float prev = Unsafe.Add(ref countsRef, 0);
        for (int k = 1; k < nRows; k++) {
            float cur = Unsafe.Add(ref countsRef, k);
            float diff = cur - prev;
            sum += diff * diff;
            prev = cur;
        }
        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AccumulateStripPopCount(ref uint dataRef, int wpl, int w, int h,
                                                int x0, int width, int dy,
                                                int yMin, int yMax, ref int countsRef)
    {
        if (x0 < 0) {
            width += x0;
            x0 = 0;
        }
        if (x0 + width > w)
            width = w - x0;
        if (width <= 0)
            return;

        int ydStart = Math.Max(yMin, dy);
        int ydEnd = Math.Min(yMax, h - 1 + dy);
        if (ydStart > ydEnd)
            return;

        int x1 = x0 + width;
        int w0 = x0 >> 5;
        int w1 = (x1 - 1) >> 5;
        uint leftMask = 0xffffffffu >> (x0 & 31);
        int rightRem = x1 & 31;
        uint rightMask = rightRem == 0 ? 0xffffffffu : (0xffffffffu << (32 - rightRem));

        int srcOff = (ydStart - dy) * wpl + w0;
        int dstIdx = ydStart - yMin;
        int count = ydEnd - ydStart + 1;

        if (w0 == w1) {
            uint mask = leftMask & rightMask;
            for (int r = 0; r < count; r++, srcOff += wpl, dstIdx++)
                Unsafe.Add(ref countsRef, dstIdx) += BitOperations.PopCount(Unsafe.Add(ref dataRef, srcOff) & mask);
        } else {
            int midWords = w1 - w0 - 1;
            int tailDelta = w1 - w0;
            for (int r = 0; r < count; r++, srcOff += wpl, dstIdx++) {
                int c = BitOperations.PopCount(Unsafe.Add(ref dataRef, srcOff) & leftMask);
                int m = 0;
                for (; m + 2 <= midWords; m += 2) {
                    ulong pair = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref dataRef, srcOff + 1 + m)));
                    c += BitOperations.PopCount(pair);
                }
                for (; m < midWords; m++)
                    c += BitOperations.PopCount(Unsafe.Add(ref dataRef, srcOff + 1 + m));
                c += BitOperations.PopCount(Unsafe.Add(ref dataRef, srcOff + tailDelta) & rightMask);
                Unsafe.Add(ref countsRef, dstIdx) += c;
            }
        }
    }

    /*----------------------------------------------------------------*
     *                        Normalized square sum                   *
     *----------------------------------------------------------------*/
    // leptonica: pixFindNormalizedSquareSum (skew.c)
    /// <summary>Measures how far the row and column fg pixel distributions are from uniform.</summary>
    /// <param name="hratio">ratio of normalized horiz square sum to result if the pixel distribution were uniform</param>
    /// <param name="vratio">ratio of normalized vert square sum to result if the pixel distribution were uniform</param>
    /// <param name="fract">ratio of fg pixels to total pixels</param>
    /// <returns>true if OK; false if there are no fg pixels</returns>
    /// <remarks>
    /// (1) Let the image have h scanlines and N fg pixels.
    ///     If the pixels were uniformly distributed on scanlines,
    ///     the sum of squares of fg pixels on each scanline would be
    ///     h * (N / h)^2.  However, if the pixels are not uniformly
    ///     distributed (e.g., for text), the sum of squares of fg
    ///     pixels will be larger.  We return in hratio and vratio the
    ///     ratio of these two values.
    /// (2) If there are no fg pixels, hratio and vratio are returned as 0.0.
    /// </remarks>
    public bool FindNormalizedSquareSum(out float hratio, out float vratio, out float fract)
    {
        int i, w, h;
        bool empty;
        float sum, sumsq, uniform, val;
        Numa na;

        hratio = 0.0f;
        vratio = 0.0f;
        fract = 0.0f;
        if (Depth != 1)
            throw Lept.Arg("pixFindNormalizedSquareSum", "pixs not defined or not 1 bpp");
        GetDimensions(out w, out h, out _);

        empty = false;
        na = CountPixelsByRow(null);
        sum = na.GetSum();  /* fg pixels */
        fract = sum / (float)(w * h);
        if (sum != 0.0) {
            uniform = sum * sum / h;   /*  h*(sum / h)^2  */
            sumsq = 0.0f;
            for (i = 0; i < h; i++) {
                val = na.GetFValue(i);
                sumsq += val * val;
            }
            hratio = sumsq / uniform;
        } else {
            empty = true;
        }

        if (empty)
            return false;
        na = CountPixelsByColumn();
        sum = na.GetSum();
        fract = sum / (float)(w * h);
        if (sum != 0.0) {
            uniform = sum * sum / w;
            sumsq = 0.0f;
            for (i = 0; i < w; i++) {
                val = na.GetFValue(i);
                sumsq += val * val;
            }
            vratio = sumsq / uniform;
        } else {
            empty = true;
        }

        return !empty;
    }
}
