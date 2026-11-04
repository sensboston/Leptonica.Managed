using System;
using System.IO;

namespace Leptonica;

/*
 *  baseline.c: text baselines and local skew.
 *
 *      Locate text baselines in an image
 *           Pix.FindBaselines (pixFindBaselines)
 *           Pix.FindBaselinesGen (pixFindBaselinesGen)
 *
 *      Projective transform to remove local skew
 *           Pix.DeskewLocal (pixDeskewLocal)
 *
 *      Determine local skew
 *           Pix.GetLocalSkewTransform (pixGetLocalSkewTransform)
 *           Pix.GetLocalSkewAngles (pixGetLocalSkewAngles)
 *
 *  We have two apparently different functions here:
 *    ~ finding baselines
 *    ~ finding a projective transform to remove keystone warping
 *  The function pixGetLocalSkewAngles() returns an array of angles,
 *  one for each raster line, and the baselines of the text lines
 *  should intersect the left edge of the image with that angle.
 *
 *  Differences from C:
 *    - The plots of the debug output are only generated if gnuplot ran (it is
 *      not run by the port); the plot images are then not
 *      added to %pixadb.
 */
public sealed partial class Pix
{
        /* Default minimum textblock width */
    private const int BaselineDefaultMinBlockWidth = 80;

        /* Minimum distance to travel after finding max before abandoning peak.
         * If BaselineMinDistFromPeak < 25, this risks bogus lines at the xheight. */
    private const int BaselineMinDistFromPeak = 30;

        /* Thresholds for peaks and zeros, relative to the max peak.
         * If BaselinePeakThresholdRatio < 40, this risks not identifying lines.
         * Results appear insensitive to the value of BaselineZeroThresholdRatio.  */
    private const int BaselinePeakThresholdRatio = 80;
    private const int BaselineZeroThresholdRatio = 100;

        /* Default values for determining local skew */
    private const int BaselineDefaultSlices = 10;
    private const int BaselineDefaultSweepReduction = 2;
    private const int BaselineDefaultBsReduction = 1;
    private const float BaselineDefaultSweepRange = 5.0f;     /* degrees */
    private const float BaselineDefaultSweepDelta = 1.0f;     /* degrees */
    private const float BaselineDefaultMinbsDelta = 0.01f;    /* degrees */

        /* Overlap slice fraction added to top and bottom of each slice */
    private const float BaselineOverlapFraction = 0.5f;

        /* Minimum allowed confidence (ratio) for accepting a value */
    private const float BaselineMinAllowedConfidence = 3.0f;

    /*---------------------------------------------------------------------*
     *                    Locate text baselines in an image                *
     *---------------------------------------------------------------------*/
    // leptonica: pixFindBaselines (baseline.c)
    /// <param name="pta">pairs of pts corresponding to approx. ends of each text line</param>
    /// <param name="pixadb">for debug output; use null to skip</param>
    /// <returns>na of baseline y values; null if there are no components after filtering; pixs is 1 bpp, 300 ppi</returns>
    /// <remarks>
    /// (1) This is a simplified interface to pixFindBaselinesGen().
    ///     See Notes there.
    /// </remarks>
    public Numa? FindBaselines(out Pta? pta, Pixa? pixadb)
    {
        return FindBaselinesGen(BaselineDefaultMinBlockWidth, out pta, pixadb);
    }

    // leptonica: pixFindBaselinesGen (baseline.c)
    /// <param name="minw">approx min block width returned baselines, in pixels</param>
    /// <param name="ppta">pairs of pts corresponding to approx. ends of each text line</param>
    /// <param name="pixadb">for debug output; use null to skip</param>
    /// <returns>na of baseline y values; null if there are no components after filtering; pixs is 1 bpp, 300 ppi</returns>
    /// <remarks>
    /// (1) Input binary image must have text lines already aligned
    ///     horizontally.  This can be done by either rotating the
    ///     image with pixDeskew(), or, if a projective transform
    ///     is required, by doing pixDeskewLocal() first.
    /// (2) The pta will come in pairs of points (left and right end
    ///     of each baseline).
    /// (3) Very short text blocks are ignored.  Use the parameter %minw
    ///     to specify the (approx.) minimum length baseline for a text block
    ///     that is returned.  Suggest using minw = 80 pixels to skip small
    ///     text blocks consisting of up to 3 characters.
    /// (4) This function returns the locations of baselines for which
    ///     the end points of the the text are found.  Return of those
    ///     end points is optional.
    /// (5) This function was designed to identify short and long text lines
    ///     without using dangerous thresholds on the peak heights.  It does
    ///     this by combining the differential signal with a morphological
    ///     analysis of the locations of the text lines.  One can also
    ///     combine this data to normalize the peak heights, by weighting
    ///     the differential signal in the region of each baseline
    ///     by the inverse of the width of the text line found there.
    /// (6) Caution: this will not work properly on text with multiple
    ///     columns, where the lines are not aligned between columns.
    ///     If there are multiple columns, they should be extracted
    ///     separately before finding the baselines.
    /// </remarks>
    public Numa? FindBaselinesGen(int minw, out Pta? ppta, Pixa? pixadb)
    {
        Pix pixs = this;
        int val1, val2, max = 0, maxloc = 0, mintosearch = 0;
        Pix pix1, pix2;

        ppta = null;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixFindBaselinesGen", "pixs undefined or not 1 bpp");

            /* minw / 6 must be >= 1 */
        if (minw < 6) minw = 6;

            /* Close up the text characters, removing noise */
        pix1 = pixs.MorphSequence("c25.1 + e15.1", 0);

            /* Estimate the resolution */
        pixadb?.AddPix(pix1.Scale(0.25f, 0.25f), L.L_INSERT);

            /* Save the difference of adjacent row sums.
             * The high positive-going peaks are the baselines */
        Numa nasum = pix1.CountPixelsByRow(null);
        int h = pixs.Height;
        Numa nadiff = Numa.Create(h);
        val2 = nasum.GetIValue(0);
        for (int i = 0; i < h - 1; i++) {
            val1 = val2;
            val2 = nasum.GetIValue(i + 1);
            nadiff.AddNumber(val1 - val2);
        }

        if (pixadb != null) {  /* show the difference signal */
            Utils.LeptMkdir("lept/baseline");
            Gplot.Simple1(nadiff, L.GPLOT_PNG, "/tmp/lept/baseline/diff", "Diff Sig");
            if (File.Exists(Utils.GenPathname("/tmp/lept/baseline/diff.png", null))) {
                pix2 = Read("/tmp/lept/baseline/diff.png");
                pixadb.AddPix(pix2, L.L_INSERT);
            }
        }

            /* Use the zeroes of the profile to locate each baseline. */
        int[] array = nadiff.GetIArray();
        int ndiff = nadiff.Count;
        nadiff.GetMax(out float maxval, out _);

            /* Use this to begin locating a new peak: */
        int peakthresh = (int)maxval / BaselinePeakThresholdRatio;
            /* Use this to begin a region between peaks: */
        int zerothresh = (int)maxval / BaselineZeroThresholdRatio;

        Numa naloc = Numa.Create(0);
        Numa naval = Numa.Create(0);
        bool inpeak = false;
        for (int i = 0; i < ndiff; i++) {
            if (!inpeak) {
                if (array[i] > peakthresh) {  /* transition to in-peak */
                    inpeak = true;
                    mintosearch = i + BaselineMinDistFromPeak; /* accept no zeros
                                                        * between i and mintosearch */
                    max = array[i];
                    maxloc = i;
                }
            } else {  /* inpeak == TRUE; look for max */
                if (array[i] > max) {
                    max = array[i];
                    maxloc = i;
                    mintosearch = i + BaselineMinDistFromPeak;
                } else if (i >= mintosearch && array[i] <= zerothresh) {
                                         /* leave and store previous peak */
                    inpeak = false;
                    naval.AddNumber(max);
                    naloc.AddNumber(maxloc);
                }
            }
        }

            /* If array[ndiff-1] is max, eg. no descenders, baseline at bottom */
        if (inpeak) {
            naval.AddNumber(max);
            naloc.AddNumber(maxloc);
        }

        if (pixadb != null) {  /* show the raster locations for the peaks */
            Gplot gplot = Gplot.Create("/tmp/lept/baseline/loc", L.GPLOT_PNG, "Peak locs",
                                       "rasterline", "height");
            gplot.AddPlot(naloc, naval, L.GPLOT_POINTS, "locs");
            gplot.MakeOutput();
            if (File.Exists(Utils.GenPathname("/tmp/lept/baseline/loc.png", null))) {
                pix2 = Read("/tmp/lept/baseline/loc.png");
                pixadb.AddPix(pix2, L.L_INSERT);
            }
        }

            /* Generate an approximate profile of text line width.
             * First, consolidate and filter the boxes of text.
             * The horizontal opening removes text blocks with width
             * less than about 'minw' pixels at full resolution. */
        string cmd = $"r11 + c20.1 + o{minw / 6}.1";
        pix2 = pix1.MorphSequence(cmd, 0);
        pixadb?.AddPix(pix2, L.L_COPY);
        Boxa boxa1 = pix2.ConnComp(4);
        if (boxa1.Count == 0) {
            Log.Info("pixFindBaselinesGen", "no components after filtering");
            return null;
        }
        Boxa boxa2 = boxa1.Transform(0, 0, 4.0f, 4.0f);
        Boxa boxa3 = boxa2.Sort(L.L_SORT_BY_Y, L.L_SORT_INCREASING, out _);

            /* For each baseline, find the corresponding textboxes.
             * There may be more than one textbox to a baseline.
             * Bogus textboxes of very small height may have been
             * generated, and these are removed.  Bogus textboxes can
             * also be eliminated if the bottom is too far from any of
             * the baselines.  If there are no valid textboxes for a
             * baseline, that baseline is removed.
             * Note that the boxes have been expanded from 4x reduction,
             * so box parameters are multiples of 4. */
        Pta pta = Pta.Create(0);
        int nloc = naloc.Count;
        int nbox = boxa3.Count;
        int nremoved = 0;  /* keeps track of baselines removed */
        for (int i = 0; i < nloc; i++) {
            int locval = naloc.GetIValue(i);
            bool found = false;
            for (int j = 0; j < nbox; j++) {
                boxa3.GetBoxGeometry(j, out int bx, out int by, out int bw, out int bh);
                if (bh > 12 && Math.Abs(locval - (by + bh)) <= 24) {
                    pta.AddPt(bx, locval);
                    pta.AddPt(bx + bw, locval);
                    found = true;
                }
            }
            if (!found) {  /* no textbox corresponding to this baseline */
                Log.Info("pixFindBaselinesGen",
                         $"short baseline {i + nremoved} at y = {locval} removed");
                naloc.RemoveNumber(i);
                nremoved++;
                i--;
                nloc--;
            }
        }

        ppta = pta;
        if (pixadb != null) {  /* display baselines */
            pix1 = pixs.ConvertTo32();
            int npts = pta.Count;
            for (int i = 0; i < npts; i += 2) {
                pta.GetIPt(i, out int x1, out int y1);
                pta.GetIPt(i + 1, out int x2, out int y2);
                pix1.RenderLineArb(x1, y1, x2, y2, 2, 255, 0, 0);
            }
            pix1.WriteDebug("/tmp/lept/baseline/baselines.png", L.IFF_PNG);
            pixadb.AddPix(pix1.Scale(0.25f, 0.25f), L.L_INSERT);
        }

        return naloc;
    }

    /*---------------------------------------------------------------------*
     *               Projective transform to remove local skew             *
     *---------------------------------------------------------------------*/
    // leptonica: pixDeskewLocal (baseline.c)
    /// <param name="nslices">the number of horizontal overlapping slices;
    /// must be larger than 1 and not exceed 20; use 0 for default</param>
    /// <param name="redsweep">sweep reduction factor: 1, 2, 4 or 8; use 0 for default value</param>
    /// <param name="redsearch">search reduction factor: 1, 2, 4 or 8, and
    /// not larger than redsweep; use 0 for default value</param>
    /// <param name="sweeprange">half the full range, assumed about 0; in degrees; use 0.0 for default value</param>
    /// <param name="sweepdelta">angle increment of sweep; in degrees; use 0.0 for default value</param>
    /// <param name="minbsdelta">min binary search increment angle; in degrees; use 0.0 for default value</param>
    /// <returns>pixd; pixs is 1 bpp</returns>
    /// <remarks>
    /// (1) This function allows deskew of a page whose skew changes
    ///     approximately linearly with vertical position.  It uses
    ///     a projective transform that in effect does a differential
    ///     shear about the LHS of the page, and makes all text lines
    ///     horizontal.
    /// (2) The origin of the keystoning can be either a cheap document
    ///     feeder that rotates the page as it is passed through, or a
    ///     camera image taken from either the left or right side
    ///     of the vertical.
    /// (3) The image transformation is a projective warping,
    ///     not a rotation.  Apart from this function, the text lines
    ///     must be properly aligned vertically with respect to each
    ///     other.  This can be done by pre-processing the page; e.g.,
    ///     by rotating or horizontally shearing it.
    ///     Typically, this can be achieved by vertically aligning
    ///     the page edge.
    /// </remarks>
    public Pix DeskewLocal(int nslices, int redsweep, int redsearch, float sweeprange,
                           float sweepdelta, float minbsdelta)
    {
        Pix pixs = this;

        if (pixs.Depth != 1)
            throw Lept.Arg("pixDeskewLocal", "pixs undefined or not 1 bpp");

            /* Skew array gives skew angle (deg) as fctn of raster line
             * where it intersects the LHS of the image */
        pixs.GetLocalSkewTransform(nslices, redsweep, redsearch, sweeprange, sweepdelta,
                                   minbsdelta, out Pta ptas, out Pta ptad);

            /* Use a projective transform */
        return pixs.ProjectiveSampledPta(ptad, ptas, L.L_BRING_IN_WHITE);
    }

    /*---------------------------------------------------------------------*
     *                       Determine the local skew                      *
     *---------------------------------------------------------------------*/
    // leptonica: pixGetLocalSkewTransform (baseline.c)
    /// <param name="nslices">the number of horizontal overlapping slices;
    /// must be larger than 1 and not exceed 20; use 0 for default</param>
    /// <param name="redsweep">sweep reduction factor: 1, 2, 4 or 8; use 0 for default value</param>
    /// <param name="redsearch">search reduction factor: 1, 2, 4 or 8, and not
    /// larger than redsweep; use 0 for default value</param>
    /// <param name="sweeprange">half the full range, assumed about 0; in degrees; use 0.0 for default value</param>
    /// <param name="sweepdelta">angle increment of sweep; in degrees; use 0.0 for default value</param>
    /// <param name="minbsdelta">min binary search increment angle; in degrees; use 0.0 for default value</param>
    /// <param name="ptas">4 points in the source</param>
    /// <param name="ptad">the corresponding 4 pts in the dest</param>
    /// <remarks>
    /// (1) This generates two pairs of points in the src, each pair
    ///     corresponding to a pair of points that would lie along
    ///     the same raster line in a transformed (dewarped) image.
    /// (2) The sets of 4 src and 4 dest points returned by this function
    ///     can then be used, in a projective or bilinear transform,
    ///     to remove keystoning in the src.
    /// </remarks>
    public void GetLocalSkewTransform(int nslices, int redsweep, int redsearch, float sweeprange,
                                      float sweepdelta, float minbsdelta, out Pta ptas, out Pta ptad)
    {
        Pix pixs = this;
        int i;
        float angd, angr, dely;

        ptas = null!;
        ptad = null!;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixGetLocalSkewTransform", "pixs not defined or not 1 bpp");
        if (nslices < 2 || nslices > 20)
            nslices = BaselineDefaultSlices;
        if (redsweep < 1 || redsweep > 8)
            redsweep = BaselineDefaultSweepReduction;
        if (redsearch < 1 || redsearch > redsweep)
            redsearch = BaselineDefaultBsReduction;
        if (sweeprange == 0.0)
            sweeprange = BaselineDefaultSweepRange;
        if (sweepdelta == 0.0)
            sweepdelta = BaselineDefaultSweepDelta;
        if (minbsdelta == 0.0)
            minbsdelta = BaselineDefaultMinbsDelta;

        Numa naskew = pixs.GetLocalSkewAngles(nslices, redsweep, redsearch, sweeprange,
                                              sweepdelta, minbsdelta, out _, out _, 0);

        float deg2rad = 3.14159265f / 180.0f;
        int w = pixs.Width;
        int h = pixs.Height;
        ptas = Pta.Create(4);
        ptad = Pta.Create(4);

            /* Find i for skew line that intersects LHS at i and RHS at h / 20 */
        dely = 0;
        for (i = 0; i < h; i++) {
            angd = naskew.GetFValue(i);
            angr = angd * deg2rad;
            dely = (float)(w * Math.Tan(angr));
            if (i - dely > 0.05 * h)
                break;
        }
        ptas.AddPt(0, i);
        ptas.AddPt(w - 1, i - dely);
        ptad.AddPt(0, i);
        ptad.AddPt(w - 1, i);

            /* Find i for skew line that intersects LHS at i and RHS at 19h / 20 */
        for (i = h - 1; i > 0; i--) {
            angd = naskew.GetFValue(i);
            angr = angd * deg2rad;
            dely = (float)(w * Math.Tan(angr));
            if (i - dely < 0.95 * h)
                break;
        }
        ptas.AddPt(0, i);
        ptas.AddPt(w - 1, i - dely);
        ptad.AddPt(0, i);
        ptad.AddPt(w - 1, i);
    }

    // leptonica: pixGetLocalSkewAngles (baseline.c)
    /// <param name="nslices">the number of horizontal overlapping slices;
    /// must be larger than 1 and not exceed 20; use 0 for default</param>
    /// <param name="redsweep">sweep reduction factor: 1, 2, 4 or 8; use 0 for default value</param>
    /// <param name="redsearch">search reduction factor: 1, 2, 4 or 8, and not
    /// larger than redsweep; use 0 for default value</param>
    /// <param name="sweeprange">half the full range, assumed about 0; in degrees; use 0.0 for default value</param>
    /// <param name="sweepdelta">angle increment of sweep; in degrees; use 0.0 for default value</param>
    /// <param name="minbsdelta">min binary search increment angle; in degrees; use 0.0 for default value</param>
    /// <param name="a">slope of skew as fctn of y</param>
    /// <param name="b">intercept at y = 0 of skew, as a function of y</param>
    /// <param name="debug">1 for generating plot of skew angle vs. y; 0 otherwise</param>
    /// <returns>naskew; pixs is 1 bpp</returns>
    /// <remarks>
    /// (1) The local skew is measured in a set of overlapping strips.
    ///     We then do a least square linear fit parameters to get
    ///     the slope and intercept parameters a and b in
    ///         skew-angle = a * y + b  (degrees)
    ///     for the local skew as a function of raster line y.
    ///     This is then used to make naskew, which can be interpreted
    ///     as the computed skew angle (in degrees) at the left edge
    ///     of each raster line.
    /// (2) naskew can then be used to find the baselines of text, because
    ///     each text line has a baseline that should intersect
    ///     the left edge of the image with the angle given by this
    ///     array, evaluated at the raster line of intersection.
    /// </remarks>
    public Numa GetLocalSkewAngles(int nslices, int redsweep, int redsearch, float sweeprange,
                                   float sweepdelta, float minbsdelta, out float a, out float b,
                                   int debug)
    {
        Pix pixs = this;

        a = 0;
        b = 0;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixGetLocalSkewAngles", "pixs undefined or not 1 bpp");
        if (nslices < 2 || nslices > 20)
            nslices = BaselineDefaultSlices;
        if (redsweep < 1 || redsweep > 8)
            redsweep = BaselineDefaultSweepReduction;
        if (redsearch < 1 || redsearch > redsweep)
            redsearch = BaselineDefaultBsReduction;
        if (sweeprange == 0.0)
            sweeprange = BaselineDefaultSweepRange;
        if (sweepdelta == 0.0)
            sweepdelta = BaselineDefaultSweepDelta;
        if (minbsdelta == 0.0)
            minbsdelta = BaselineDefaultMinbsDelta;

        pixs.GetDimensions(out int w, out int h, out _);
        int hs = h / nslices;
        int ovlap = (int)(BaselineOverlapFraction * hs);
        Pta pta = Pta.Create(nslices);
        for (int i = 0; i < nslices; i++) {
            int ystart = Math.Max(0, hs * i - ovlap);
            int yend = Math.Min(h - 1, hs * (i + 1) + ovlap);
            float ycenter = (float)(ystart + yend) / 2;
            Box box = Box.Create(0, ystart, w, yend - ystart + 1);
            Pix pix = pixs.ClipRectangle(box, out _)
                      ?? throw Lept.Fail("pixGetLocalSkewAngles", "slice not clipped");
            pix.FindSkewSweepAndSearch(out float angle, out float conf, redsweep, redsearch,
                                       sweeprange, sweepdelta, minbsdelta);
            if (conf > BaselineMinAllowedConfidence)
                pta.AddPt(ycenter, angle);
        }

            /* Do linear least squares fit */
        if (pta.Count < 2)
            throw Lept.Fail("pixGetLocalSkewAngles", "can't fit skew");
        pta.GetLinearLSF(out a, out b, out _);

            /* Make skew angle array as function of raster line */
        Numa naskew = Numa.Create(h);
        for (int i = 0; i < h; i++) {
            float angle = a * i + b;
            naskew.AddNumber(angle);
        }

        if (debug != 0) {
            Utils.LeptMkdir("lept/baseline");
            pta.GetArrays(out Numa nax, out Numa nay);
            Gplot gplot = Gplot.Create("/tmp/lept/baseline/skew", L.GPLOT_PNG,
                                       "skew as fctn of y", "y (in raster lines from top)",
                                       "angle (in degrees)");
            gplot.AddPlot(null, naskew, L.GPLOT_POINTS, "linear lsf");
            gplot.AddPlot(nax, nay, L.GPLOT_POINTS, "actual data pts");
            gplot.MakeOutput();
        }

        return naskew;
    }
}
