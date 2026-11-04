using System;

namespace Leptonica;

/*
 *  boxfunc5.c
 *
 *      Boxa sequence fitting
 *           Boxa.SmoothSequenceMedian (boxaSmoothSequenceMedian)
 *           Boxa.WindowedMedian (boxaWindowedMedian)
 *           Boxa.ModifyWithBoxa (boxaModifyWithBoxa)
 *           Boxa.ReconcilePairWidth (boxaReconcilePairWidth)
 *           Boxa.SizeConsistency (boxaSizeConsistency)
 *           Boxa.ReconcileAllByMedian (boxaReconcileAllByMedian)
 *           Boxa.ReconcileSidesByMedian (boxaReconcileSidesByMedian)
 *    static Boxa.AdjustSidePlotName (adjustSidePlotName)  -- debug
 *           Boxa.ReconcileSizeByMedian (boxaReconcileSizeByMedian)
 *           Boxa.PlotSides (boxaPlotSides)   [for debugging]
 *           Boxa.PlotSizes (boxaPlotSizes)   [for debugging]
 *           Boxa.FillSequence (boxaFillSequence)
 *    static Boxa.FillAll (boxaFillAll)
 *           Boxa.SizeVariation (boxaSizeVariation)
 *           Boxa.MedianDimensions (boxaMedianDimensions)
 *
 *  Differences from C:
 *    - Optional output pointers become plain out parameters that are always
 *      computed.  Errors throw; where C returns a copy of the input boxa
 *      with a warning, so does the port.
 *    - boxaPlotSides() and boxaPlotSizes() need the gnuplot output of
 *      the gplot port (which does not run gnuplot); they throw if the plot
 *      image has not been produced.
 */

public static partial class L
{
    /* Box boundary modification (pix.h) */
    /// <summary>Uses the boundaries that give the minimum size.</summary>
    public const int L_USE_MINSIZE = 1;

    /// <summary>Uses the boundaries that give the maximum size.</summary>
    public const int L_USE_MAXSIZE = 2;

    /// <summary>Modifies the boundary if the location difference is large.</summary>
    public const int L_SUB_ON_LOC_DIFF = 3;

    /// <summary>Modifies the boundary if the size difference is large.</summary>
    public const int L_SUB_ON_SIZE_DIFF = 4;

    /// <summary>Modifies the boundary using a capped minimum.</summary>
    public const int L_USE_CAPPED_MIN = 5;

    /// <summary>Modifies the boundary using a capped maximum.</summary>
    public const int L_USE_CAPPED_MAX = 6;

    /* Boxa check (pix.h) */

    /// <summary>Checks, and possibly modifies, the width.</summary>
    public const int L_CHECK_WIDTH = 1;

    /// <summary>Checks, and possibly modifies, the height.</summary>
    public const int L_CHECK_HEIGHT = 2;

    /// <summary>Checks, and possibly modifies, both the width and the height.</summary>
    public const int L_CHECK_BOTH = 3;

    /* Box replacement (pix.h) */

    /// <summary>Considers all boxes in the sequence.</summary>
    public const int L_USE_ALL_BOXES = 1;

    /// <summary>Considers only the boxes with the same parity.</summary>
    public const int L_USE_SAME_PARITY_BOXES = 2;
}

public sealed partial class Boxa
{
    private static int boxaPlotSidesId = 0;
    private static int boxaPlotSizesId = 0;

    /*---------------------------------------------------------------------*
     *                        Boxa sequence fitting                        *
     *---------------------------------------------------------------------*/
    // leptonica: boxaSmoothSequenceMedian (boxfunc5.c)
    /// <summary>Smooths the sequence of boxes with a windowed median filter on each side.</summary>
    /// <param name="halfwin">half-width of sliding window; used to find median</param>
    /// <param name="subflag">L_USE_MINSIZE, L_USE_MAXSIZE,
    ///                       L_SUB_ON_LOC_DIFF, L_SUB_ON_SIZE_DIFF,
    ///                       L_USE_CAPPED_MIN, L_USE_CAPPED_MAX</param>
    /// <param name="maxdiff">parameter used with L_SUB_ON_LOC_DIFF,
    ///                       L_SUB_ON_SIZE_DIFF, L_USE_CAPPED_MIN,
    ///                       L_USE_CAPPED_MAX</param>
    /// <param name="extrapixels">pixels added on all sides (or subtracted
    ///                           if extrapixels &lt; 0) when using
    ///                           L_SUB_ON_LOC_DIFF and L_SUB_ON_SIZE_DIFF</param>
    /// <param name="debug">1 for debug output</param>
    /// <returns>boxad fitted boxa</returns>
    /// <remarks>
    /// (1) The target width of the sliding window is 2 * halfwin + 1.
    ///     If necessary, this will be reduced by boxaWindowedMedian().
    /// (2) This returns a modified version of boxas by constructing
    ///     for each input box a box that has been smoothed with windowed
    ///     median filtering.  The filtering is done to each of the
    ///     box sides independently, and it is computed separately for
    ///     sequences of even and odd boxes.  The output boxad is
    ///     constructed from the input boxa and the filtered boxa,
    ///     depending on subflag.  See boxaModifyWithBoxa() for
    ///     details on the use of subflag, maxdiff and extrapixels.
    /// (3) This is useful for removing noise separately in the even
    ///     and odd sets, where the box edge locations can have
    ///     discontinuities but otherwise vary roughly linearly within
    ///     intervals of size halfwin or larger.
    /// (4) If you don't need to handle even and odd sets separately,
    ///     just do this:
    ///         boxam = boxaWindowedMedian(boxas, halfwin, debug);
    ///         boxad = boxaModifyWithBoxa(boxas, boxam, subflag, maxdiff,
    ///                                    extrapixels);
    /// </remarks>
    public Boxa SmoothSequenceMedian(int halfwin, int subflag, int maxdiff,
                                     int extrapixels, int debug)
    {
        const string func = "boxaSmoothSequenceMedian";
        Boxa boxas = this;
        if (halfwin <= 0) {
            Log.Warning(func, "halfwin must be > 0; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }
        if (maxdiff < 0) {
            Log.Warning(func, "maxdiff must be >= 0; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }
        if (subflag != L.L_USE_MINSIZE && subflag != L.L_USE_MAXSIZE &&
            subflag != L.L_SUB_ON_LOC_DIFF && subflag != L.L_SUB_ON_SIZE_DIFF &&
            subflag != L.L_USE_CAPPED_MIN && subflag != L.L_USE_CAPPED_MAX) {
            Log.Warning(func, "invalid subflag; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }
        if (boxas.Count < 6) {
            Log.Warning(func, "need at least 6 boxes; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }

        boxas.SplitEvenOdd(0, out Boxa boxae, out Boxa boxao);
        if (debug != 0) {
            Utils.LeptMkdir("lept/smooth");
            boxae.WriteDebug("/tmp/lept/smooth/boxae.ba");
            boxao.WriteDebug("/tmp/lept/smooth/boxao.ba");
        }

        Boxa boxamede = boxae.WindowedMedian(halfwin, debug);
        Boxa boxamedo = boxao.WindowedMedian(halfwin, debug);
        if (debug != 0) {
            boxamede.WriteDebug("/tmp/lept/smooth/boxamede.ba");
            boxamedo.WriteDebug("/tmp/lept/smooth/boxamedo.ba");
        }

        Boxa boxame = boxae.ModifyWithBoxa(boxamede, subflag, maxdiff, extrapixels);
        Boxa boxamo = boxao.ModifyWithBoxa(boxamedo, subflag, maxdiff, extrapixels);
        if (debug != 0) {
            boxame.WriteDebug("/tmp/lept/smooth/boxame.ba");
            boxamo.WriteDebug("/tmp/lept/smooth/boxamo.ba");
        }

        Boxa boxad = boxame.MergeEvenOdd(boxamo, 0);
        if (debug != 0) {
            boxas.PlotSides(null, out _, out _, out _, out _, out Pix pix1);
            Pix.Write("/tmp/lept/smooth/plotsides1.png", pix1, L.IFF_PNG);
            boxad.PlotSides(null, out _, out _, out _, out _, out pix1);
            Pix.Write("/tmp/lept/smooth/plotsides2.png", pix1, L.IFF_PNG);
            boxas.PlotSizes(null, out _, out _, out pix1);
            Pix.Write("/tmp/lept/smooth/plotsizes1.png", pix1, L.IFF_PNG);
            boxad.PlotSizes(null, out _, out _, out pix1);
            Pix.Write("/tmp/lept/smooth/plotsizes2.png", pix1, L.IFF_PNG);
        }

        return boxad;
    }

    // leptonica: boxaWindowedMedian (boxfunc5.c)
    /// <summary>Finds the windowed median of each side of the boxes.</summary>
    /// <param name="halfwin">half width of window over which the median is found</param>
    /// <param name="debug">1 for debug output</param>
    /// <returns>boxad smoothed boxa</returns>
    /// <remarks>
    /// (1) This finds a set of boxes (boxad) where each edge of each box is
    ///     a windowed median smoothed value to the edges of the
    ///     input set of boxes (boxas).
    /// (2) Invalid input boxes are filled from nearby ones.
    /// (3) The returned boxad can then be used in boxaModifyWithBoxa()
    ///     to selectively change the boxes in the source boxa.
    /// </remarks>
    public Boxa WindowedMedian(int halfwin, int debug)
    {
        const string func = "boxaWindowedMedian";
        Boxa boxas = this;
        int n = boxas.Count;
        if (n < 3) {
            Log.Warning(func, "less than 3 boxes; returning a copy\n");
            return boxas.Copy(L.L_COPY);
        }
        if (halfwin <= 0) {
            Log.Warning(func, "halfwin must be > 0; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }

            /* Fill invalid boxes in the input sequence */
        Boxa boxaf = boxas.FillSequence(L.L_USE_ALL_BOXES, debug);

            /* Get the windowed median output from each of the sides */
        boxaf.ExtractAsNuma(out Numa nal, out Numa nat, out Numa nar, out Numa nab,
                            out _, out _, 0);
        Numa naml = nal.WindowedMedian(halfwin);
        Numa namt = nat.WindowedMedian(halfwin);
        Numa namr = nar.WindowedMedian(halfwin);
        Numa namb = nab.WindowedMedian(halfwin);

        n = boxaf.Count;
        Boxa boxad = Create(n);
        for (int i = 0; i < n; i++) {
            int left = naml.GetIValue(i);
            int top = namt.GetIValue(i);
            int right = namr.GetIValue(i);
            int bot = namb.GetIValue(i);
            Box box = Box.Create(left, top, right - left + 1, bot - top + 1);
            boxad.AddBox(box, L.L_INSERT);
        }

        if (debug != 0) {
            Utils.LeptMkdir("lept/windowed");
            boxaf.PlotSides(null, out _, out _, out _, out _, out Pix pix1);
            Pix.Write("/tmp/lept/windowed/plotsides1.png", pix1, L.IFF_PNG);
            boxad.PlotSides(null, out _, out _, out _, out _, out pix1);
            Pix.Write("/tmp/lept/windowed/plotsides2.png", pix1, L.IFF_PNG);
            boxaf.PlotSizes(null, out _, out _, out pix1);
            Pix.Write("/tmp/lept/windowed/plotsizes1.png", pix1, L.IFF_PNG);
            boxad.PlotSizes(null, out _, out _, out pix1);
            Pix.Write("/tmp/lept/windowed/plotsizes2.png", pix1, L.IFF_PNG);
        }

        return boxad;
    }

    // leptonica: boxaModifyWithBoxa (boxfunc5.c)
    /// <summary>Modifies the boxes in this boxa, using the corresponding boxes in boxam.</summary>
    /// <param name="boxam">boxa with boxes used to modify those in boxas</param>
    /// <param name="subflag">L_USE_MINSIZE, L_USE_MAXSIZE,
    ///                       L_SUB_ON_LOC_DIFF, L_SUB_ON_SIZE_DIFF,
    ///                       L_USE_CAPPED_MIN, L_USE_CAPPED_MAX</param>
    /// <param name="maxdiff">parameter used with L_SUB_ON_LOC_DIFF,
    ///                       L_SUB_ON_SIZE_DIFF, L_USE_CAPPED_MIN,
    ///                       L_USE_CAPPED_MAX</param>
    /// <param name="extrapixels">pixels added on all sides (or subtracted
    ///                           if extrapixels &lt; 0) when using
    ///                           L_SUB_ON_LOC_DIFF and L_SUB_ON_SIZE_DIFF</param>
    /// <returns>boxad result after adjusting boxes in boxas</returns>
    /// <remarks>
    /// (1) This takes two input boxa (boxas, boxam) and constructs boxad,
    ///     where each box in boxad is generated from the corresponding
    ///     boxes in boxas and boxam.  The rule for constructing each
    ///     output box depends on subflag and maxdiff.  Let boxs be
    ///     a box from boxas and boxm be a box from boxam.
    ///     * If subflag == L_USE_MINSIZE: the output box is the intersection
    ///       of the two input boxes.
    ///     * If subflag == L_USE_MAXSIZE: the output box is the union of the
    ///       two input boxes; i.e., the minimum bounding rectangle for the
    ///       two input boxes.
    ///     * If subflag == L_SUB_ON_LOC_DIFF: each side of the output box
    ///       is found separately from the corresponding side of boxs and boxm.
    ///       Use the boxm side, expanded by extrapixels, if greater than
    ///       maxdiff pixels from the boxs side.
    ///     * If subflag == L_SUB_ON_SIZE_DIFF: the sides of the output box
    ///       are determined in pairs from the width and height of boxs
    ///       and boxm.  If the boxm width differs by more than maxdiff
    ///       pixels from boxs, use the boxm left and right sides,
    ///       expanded by extrapixels.  Ditto for the height difference.
    ///     For the last two flags, each side of the output box is found
    ///     separately from the corresponding side of boxs and boxm,
    ///     according to these rules, where "smaller"("bigger") mean in a
    ///     direction that decreases(increases) the size of the output box:
    ///     * If subflag == L_USE_CAPPED_MIN: use the Min of boxm
    ///       with the Max of (boxs, boxm +- maxdiff), where the sign
    ///       is adjusted to make the box smaller (e.g., use "+" on left side).
    ///     * If subflag == L_USE_CAPPED_MAX: use the Max of boxm
    ///       with the Min of (boxs, boxm +- maxdiff), where the sign
    ///       is adjusted to make the box bigger (e.g., use "-" on left side).
    ///     Use of the last 2 flags is further explained in (3) and (4).
    /// (2) boxas and boxam must be the same size.  If boxam == null,
    ///     this returns a copy of boxas with a warning.
    /// (3) If subflag == L_SUB_ON_LOC_DIFF, use boxm for each side
    ///     where the corresponding sides differ by more than maxdiff.
    ///     Two extreme cases:
    ///     (a) set maxdiff == 0 to use only values from boxam in boxad.
    ///     (b) set maxdiff == 10000 to ignore all values from boxam;
    ///         then boxad will be the same as boxas.
    /// (4) If subflag == L_USE_CAPPED_MAX: use boxm if boxs is smaller;
    ///     use boxs if boxs is bigger than boxm by an amount up to maxdiff;
    ///     and use boxm +- maxdiff (the 'capped' value) if boxs is
    ///     bigger than boxm by an amount larger than maxdiff.
    ///     Similarly, with interchange of Min/Max and sign of maxdiff,
    ///     for subflag == L_USE_CAPPED_MIN.
    /// (5) If either of corresponding boxes in boxas and boxam is invalid,
    ///     an invalid box is copied to the result.
    /// (6) Typical input for boxam may be the output of boxaLinearFit().
    ///     where outliers have been removed and each side is LS fit to a line.
    /// (7) Unlike boxaAdjustWidthToTarget() and boxaAdjustHeightToTarget(),
    ///     this uses two boxes and does not specify target dimensions.
    /// </remarks>
    public Boxa ModifyWithBoxa(Boxa? boxam, int subflag, int maxdiff, int extrapixels)
    {
        const string func = "boxaModifyWithBoxa";
        Boxa boxas = this;
        if (boxam == null) {
            Log.Warning(func, "boxam not defined; returning copy");
            return boxas.Copy(L.L_COPY);
        }
        if (subflag != L.L_USE_MINSIZE && subflag != L.L_USE_MAXSIZE &&
            subflag != L.L_SUB_ON_LOC_DIFF && subflag != L.L_SUB_ON_SIZE_DIFF &&
            subflag != L.L_USE_CAPPED_MIN && subflag != L.L_USE_CAPPED_MAX) {
            Log.Warning(func, "invalid subflag; returning copy");
            return boxas.Copy(L.L_COPY);
        }
        int n = boxas.Count;
        if (n != boxam.Count) {
            Log.Warning(func, "boxas and boxam sizes differ; returning copy");
            return boxas.Copy(L.L_COPY);
        }

        Boxa boxad = Create(n);
        Box boxempty = Box.Create(0, 0, 0, 0);  /* placeholders */
        for (int i = 0; i < n; i++) {
            Box? boxs = boxas.GetValidBox(i, L.L_CLONE);
            Box? boxm = boxam.GetValidBox(i, L.L_CLONE);
            if (boxs == null || boxm == null) {
                boxad.AddBox(boxempty, L.L_COPY);
            } else {
                int ld, td, rd, bd;
                boxs.GetGeometry(out int ls, out int ts, out int ws, out int hs);
                boxm.GetGeometry(out int lm, out int tm, out int wm, out int hm);
                int rs = ls + ws - 1;
                int bs = ts + hs - 1;
                int rm = lm + wm - 1;
                int bm = tm + hm - 1;
                if (subflag == L.L_USE_MINSIZE) {
                    ld = Math.Max(ls, lm);
                    rd = Math.Min(rs, rm);
                    td = Math.Max(ts, tm);
                    bd = Math.Min(bs, bm);
                } else if (subflag == L.L_USE_MAXSIZE) {
                    ld = Math.Min(ls, lm);
                    rd = Math.Max(rs, rm);
                    td = Math.Min(ts, tm);
                    bd = Math.Max(bs, bm);
                } else if (subflag == L.L_SUB_ON_LOC_DIFF) {
                    ld = (Math.Abs(lm - ls) <= maxdiff) ? ls : lm - extrapixels;
                    td = (Math.Abs(tm - ts) <= maxdiff) ? ts : tm - extrapixels;
                    rd = (Math.Abs(rm - rs) <= maxdiff) ? rs : rm + extrapixels;
                    bd = (Math.Abs(bm - bs) <= maxdiff) ? bs : bm + extrapixels;
                } else if (subflag == L.L_SUB_ON_SIZE_DIFF) {
                    ld = (Math.Abs(wm - ws) <= maxdiff) ? ls : lm - extrapixels;
                    td = (Math.Abs(hm - hs) <= maxdiff) ? ts : tm - extrapixels;
                    rd = (Math.Abs(wm - ws) <= maxdiff) ? rs : rm + extrapixels;
                    bd = (Math.Abs(hm - hs) <= maxdiff) ? bs : bm + extrapixels;
                } else if (subflag == L.L_USE_CAPPED_MIN) {
                    ld = Math.Max(lm, Math.Min(ls, lm + maxdiff));
                    td = Math.Max(tm, Math.Min(ts, tm + maxdiff));
                    rd = Math.Min(rm, Math.Max(rs, rm - maxdiff));
                    bd = Math.Min(bm, Math.Max(bs, bm - maxdiff));
                } else {  /* subflag == L_USE_CAPPED_MAX */
                    ld = Math.Min(lm, Math.Max(ls, lm - maxdiff));
                    td = Math.Min(tm, Math.Max(ts, tm - maxdiff));
                    rd = Math.Max(rm, Math.Min(rs, rm + maxdiff));
                    bd = Math.Max(bm, Math.Min(bs, bm + maxdiff));
                }
                Box boxd = Box.Create(ld, td, rd - ld + 1, bd - td + 1);
                boxad.AddBox(boxd, L.L_INSERT);
            }
        }

        return boxad;
    }

    // leptonica: boxaReconcilePairWidth (boxfunc5.c)
    /// <summary>Reconciles differences in the width of adjacent (even/odd) boxes.</summary>
    /// <param name="delw">threshold on adjacent width difference</param>
    /// <param name="op">L_ADJUST_CHOOSE_MIN, L_ADJUST_CHOOSE_MAX</param>
    /// <param name="factor">> 0.0, typically near 1.0</param>
    /// <param name="na">[optional] indicator array allowing change</param>
    /// <returns>boxad adjusted</returns>
    /// <remarks>
    /// (1) This reconciles differences in the width of adjacent boxes,
    ///     by moving one side of one of the boxes in each pair.
    ///     If the widths in the pair differ by more than some
    ///     threshold, move either the left side for even boxes or
    ///     the right side for odd boxes, depending on if we're choosing
    ///     the min or max.  If choosing min, the width of the max is
    ///     set to factor * (width of min).  If choosing max, the width
    ///     of the min is set to factor * (width of max).
    /// (2) If na exists, it is an indicator array corresponding to the
    ///     boxes in boxas.  If na != null, only boxes with an
    ///     indicator value of 1 are allowed to adjust; otherwise,
    ///     all boxes can adjust.
    /// (3) Typical input might be the output of boxaSmoothSequenceMedian(),
    ///     where even and odd boxa have been independently regulated.
    /// </remarks>
    public Boxa ReconcilePairWidth(int delw, int op, float factor, Numa? na)
    {
        const string func = "boxaReconcilePairWidth";
        Boxa boxas = this;
        if (factor <= 0.0) {
            Log.Warning(func, "invalid factor; setting to 1.0\n");
            factor = 1.0f;
        }

            /* Taking the boxes in pairs, if the difference in width reaches
             * the threshold delw, adjust the left or right side of one
             * of the pair. */
        boxas.SplitEvenOdd(0, out Boxa boxae, out Boxa boxao);
        int ne = boxae.Count;
        int no = boxao.Count;
        int nmin = Math.Min(ne, no);
        for (int i = 0; i < nmin; i++) {
                /* Set indicator values */
            int inde, indo;
            if (na != null) {
                inde = na.GetIValue(2 * i);
                indo = na.GetIValue(2 * i + 1);
            } else {
                inde = indo = 1;
            }
            if (inde == 0 && indo == 0) continue;

            Box boxe = boxae.GetBox(i, L.L_CLONE);
            Box boxo = boxao.GetBox(i, L.L_CLONE);
            boxe.GetGeometry(out int xe, out _, out int we, out _);
            boxo.GetGeometry(out _, out _, out int wo, out _);
            if (we == 0 || wo == 0) {  /* if either is invalid; skip */
                continue;
            } else if (Math.Abs(we - wo) > delw) {
                int w, x;
                if (op == L.L_ADJUST_CHOOSE_MIN) {
                    if (we > wo && inde == 1) {
                            /* move left side of even to the right */
                        w = (int)(factor * wo);
                        x = xe + (we - w);
                        boxe.SetGeometry(x, -1, w, -1);
                    } else if (we < wo && indo == 1) {
                            /* move right side of odd to the left */
                        w = (int)(factor * we);
                        boxo.SetGeometry(-1, -1, w, -1);
                    }
                } else {  /* maximize width */
                    if (we < wo && inde == 1) {
                            /* move left side of even to the left */
                        w = (int)(factor * wo);
                        x = Math.Max(0, xe + (we - w));
                        w = we + (xe - x);  /* covers both cases for the max */
                        boxe.SetGeometry(x, -1, w, -1);
                    } else if (we > wo && indo == 1) {
                            /* move right side of odd to the right */
                        w = (int)(factor * we);
                        boxo.SetGeometry(-1, -1, w, -1);
                    }
                }
            }
        }

        return boxae.MergeEvenOdd(boxao, 0);
    }

    // leptonica: boxaSizeConsistency (boxfunc5.c)
    /// <summary>Evaluates a boxa for dimensional variation.</summary>
    /// <param name="type">L_CHECK_WIDTH, L_CHECK_HEIGHT</param>
    /// <param name="threshp">threshold for pairwise fractional variation</param>
    /// <param name="threshm">threshold for fractional variation from median</param>
    /// <param name="pfvarp">average fractional pairwise variation</param>
    /// <param name="pfvarm">average fractional median variation</param>
    /// <param name="psame">decision for uniformity of page size (1, 0, -1)</param>
    /// <remarks>
    /// (1) This evaluates a boxa for particular types of dimensional
    ///     variation.  Select either width or height variation.  Then
    ///     it returns two numbers: one is based on pairwise (even/odd)
    ///     variation; the other is based on the average variation
    ///     from the boxa median.
    /// (2) For the pairwise variation, get the fraction of the absolute
    ///     difference in dimension of each pair of boxes, and take
    ///     the average value.  The median variation is simply the
    ///     the average of the fractional deviation from the median
    ///     of all the boxes.
    /// (3) Use 0 for default values of threshp and threshm.  They are
    ///       threshp:  0.02
    ///       threshm:  0.015
    /// (4) The intended application is that the boxes are a sequence of
    ///     page regions in a book scan, and we calculate two numbers
    ///     that can give an indication if the pages are approximately
    ///     the same size.  The pairwise variation should be small if
    ///     the boxes are correctly calculated.  If there are a
    ///     significant number of random or systematic outliers, the
    ///     pairwise variation will be large, and no decision will be made
    ///     (i.e., return same == -1).  Here are the possible outcomes:
    ///       Pairwise Var    Median Var    Decision
    ///       ------------    ----------    --------
    ///       small           small         same size  (1)
    ///       small           large         different size  (0)
    ///       large           small/large   unknown   (-1)
    /// </remarks>
    public void SizeConsistency(int type, float threshp, float threshm,
                                out float pfvarp, out float pfvarm, out int psame)
    {
        const string func = "boxaSizeConsistency";
        Boxa boxas = this;
        pfvarp = 0.0f;
        pfvarm = 0.0f;
        psame = -1;
        if (boxas.GetValidCount() < 6)
            throw Lept.Arg(func, "need a least 6 valid boxes");
        if (type != L.L_CHECK_WIDTH && type != L.L_CHECK_HEIGHT)
            throw Lept.Arg(func, "invalid type");
        if (threshp < 0.0 || threshp >= 0.5)
            throw Lept.Arg(func, "invalid threshp");
        if (threshm < 0.0 || threshm >= 0.5)
            throw Lept.Arg(func, "invalid threshm");
        if (threshp == 0.0) threshp = 0.02f;
        if (threshm == 0.0) threshm = 0.015f;

            /* Evaluate pairwise variation */
        int n = boxas.Count;
        Numa na1 = Numa.Create(0);
        int npairs = 0;
        float sumdiff = 0;
        float ave, fdiff;
        for (int i = 0; i < n - 1; i += 2) {
            boxas.GetBoxGeometry(i, out _, out _, out int bw1, out int bh1);
            boxas.GetBoxGeometry(i + 1, out _, out _, out int bw2, out int bh2);
            if (bw1 == 0 || bh1 == 0 || bw2 == 0 || bh2 == 0)
                continue;
            npairs++;
            if (type == L.L_CHECK_WIDTH) {
                ave = (float)((bw1 + bw2) / 2.0);
                fdiff = Math.Abs(bw1 - bw2) / ave;
                na1.AddNumber(bw1);
                na1.AddNumber(bw2);
            } else {  /* type == L_CHECK_HEIGHT) */
                ave = (float)((bh1 + bh2) / 2.0);
                fdiff = Math.Abs(bh1 - bh2) / ave;
                na1.AddNumber(bh1);
                na1.AddNumber(bh2);
            }
            sumdiff += fdiff;
        }
        float fvarp = sumdiff / npairs;
        pfvarp = fvarp;

            /* Evaluate the average abs fractional deviation from the median */
        float fvarm = 0.0f;   /* uninitialized in C if the median is 0 */
        float med = na1.GetMedian();
        if (med == 0.0) {
            Log.Warning(func, "median value is 0\n");
        } else {
            fvarm = na1.GetMeanDevFromMedian(med);
            fvarm /= med;
            pfvarm = fvarm;
        }

            /* Make decision */
        if (fvarp < threshp && fvarm < threshm)
            psame = 1;
        else if (fvarp < threshp && fvarm > threshm)
            psame = 0;
        else
            psame = -1;  /* unknown */
    }

    // leptonica: boxaReconcileAllByMedian (boxfunc5.c)
    /// <summary>Reconciles the sides of the even and odd boxes separately, using their medians.</summary>
    /// <param name="select1">L_ADJUST_LEFT_AND_RIGHT or L_ADJUST_SKIP</param>
    /// <param name="select2">L_ADJUST_TOP_AND_BOT or L_ADJUST_SKIP</param>
    /// <param name="thresh">threshold number of pixels to make adjustment</param>
    /// <param name="extra">extra pixels to add beyond median value</param>
    /// <param name="pixadb">use null to skip debug output</param>
    /// <returns>boxad possibly adjusted from boxas; a copy of boxas on error</returns>
    /// <remarks>
    /// (1) This uses boxaReconcileSidesByMedian() to reconcile
    ///     the left-and-right and/or top-and-bottom sides of the
    ///     even and odd boxes, separately.
    /// (2) See boxaReconcileSidesByMedian() for use of thresh and extra.
    /// (3) If all box sides are within thresh of the median value,
    ///     the returned box will be identical to boxas.
    /// </remarks>
    public Boxa ReconcileAllByMedian(int select1, int select2, int thresh, int extra,
                                     Pixa? pixadb)
    {
        const string func = "boxaReconcileAllByMedian";
        Boxa boxas = this;
        if (select1 != L.L_ADJUST_LEFT_AND_RIGHT && select1 != L.L_ADJUST_SKIP) {
            Log.Warning(func, "invalid select1; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }
        if (select2 != L.L_ADJUST_TOP_AND_BOT && select2 != L.L_ADJUST_SKIP) {
            Log.Warning(func, "invalid select2; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }
        if (thresh < 0) {
            Log.Warning(func, "thresh must be >= 0; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }
        if (boxas.GetValidCount() < 3) {
            Log.Warning(func, "need at least 3 valid boxes; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }

            /* Adjust even and odd box sides separately */
        boxas.SplitEvenOdd(0, out Boxa boxa1e, out Boxa boxa1o);
        Boxa boxa2e, boxa3e, boxa2o, boxa3o;
        int ncols = 1;
        if (select1 == L.L_ADJUST_LEFT_AND_RIGHT) {
            ncols += 2;
            boxa2e = boxa1e.ReconcileSidesByMedian(select1, thresh, extra, pixadb);
        } else {
            boxa2e = boxa1e.Copy(L.L_COPY);
        }
        if (select2 == L.L_ADJUST_TOP_AND_BOT) {
            ncols += 2;
            boxa3e = boxa2e.ReconcileSidesByMedian(select2, thresh, extra, pixadb);
        } else {
            boxa3e = boxa2e.Copy(L.L_COPY);
        }
        if (select1 == L.L_ADJUST_LEFT_AND_RIGHT)
            boxa2o = boxa1o.ReconcileSidesByMedian(select1, thresh, extra, pixadb);
        else
            boxa2o = boxa1o.Copy(L.L_COPY);
        if (select2 == L.L_ADJUST_TOP_AND_BOT)
            boxa3o = boxa2o.ReconcileSidesByMedian(select2, thresh, extra, pixadb);
        else
            boxa3o = boxa2o.Copy(L.L_COPY);
        Boxa boxad = boxa3e.MergeEvenOdd(boxa3o, 0);

            /* This generates 2 sets of 3 or 5 plots in a row, depending
             * on whether select1 and select2 are true (not skipping).
             * The top row is for even boxes; the bottom row is for odd boxes. */
        if (pixadb != null) {
            Utils.LeptMkdir("lept/boxa");
            Pix pix1 = pixadb.DisplayTiledInColumns(ncols, 1.0f, 30, 2);
            Pix.Write("/tmp/lept/boxa/recon_sides.png", pix1, L.IFF_PNG);
        }

        return boxad;
    }

    // leptonica: boxaReconcileSidesByMedian (boxfunc5.c)
    /// <summary>Moves box sides that differ significantly from the median to the median value.</summary>
    /// <param name="select">L_ADJUST_LEFT, L_ADJUST_RIGHT, etc.</param>
    /// <param name="thresh">threshold number of pixels to make adjustment</param>
    /// <param name="extra">extra pixels to add beyond median value</param>
    /// <param name="pixadb">use null to skip debug output</param>
    /// <returns>boxad possibly adjusted from boxas; a copy of boxas on error</returns>
    /// <remarks>
    /// (1) This modifies individual box sides if their location differs
    ///     significantly (>= thresh) from the median value.
    /// (2) select specifies which sides are to be checked.
    /// (3) thresh specifies the tolerance for different side locations.
    ///     Any box side that differs from the median by this much will
    ///     be set to the median value, plus the extra amount.
    /// (4) If extra is positive, the box dimensions are expanded.
    ///     For example, for the left side, a positive extra results in
    ///     moving the left side farther to the left (i.e., in a negative
    ///     direction).
    /// (5) If all box sides are within thresh - 1 of the median value,
    ///     the returned box will be identical to boxas.
    /// (6) N.B. If you expect that even and odd box sides should be
    ///     significantly different, this function must be called separately
    ///     on the even and odd boxes in boxas.  Note also that the
    ///     higher level function boxaReconcileAllByMedian() handles the
    ///     even and odd box sides separately.
    /// </remarks>
    public Boxa ReconcileSidesByMedian(int select, int thresh, int extra, Pixa? pixadb)
    {
        const string func = "boxaReconcileSidesByMedian";
        Boxa boxas = this;
        if (select != L.L_ADJUST_LEFT && select != L.L_ADJUST_RIGHT &&
            select != L.L_ADJUST_TOP && select != L.L_ADJUST_BOT &&
            select != L.L_ADJUST_LEFT_AND_RIGHT && select != L.L_ADJUST_TOP_AND_BOT) {
            Log.Warning(func, "invalid select; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }
        if (thresh < 0) {
            Log.Warning(func, "thresh must be >= 0; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }
        if (boxas.GetValidCount() < 3) {
            Log.Warning(func, "need at least 3 valid boxes; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }

        Boxa boxa1, boxad;
        if (select == L.L_ADJUST_LEFT_AND_RIGHT) {
            boxa1 = boxas.ReconcileSidesByMedian(L.L_ADJUST_LEFT, thresh, extra, pixadb);
            boxad = boxa1.ReconcileSidesByMedian(L.L_ADJUST_RIGHT, thresh, extra, pixadb);
            return boxad;
        }
        if (select == L.L_ADJUST_TOP_AND_BOT) {
            boxa1 = boxas.ReconcileSidesByMedian(L.L_ADJUST_TOP, thresh, extra, pixadb);
            boxad = boxa1.ReconcileSidesByMedian(L.L_ADJUST_BOT, thresh, extra, pixadb);
            return boxad;
        }

        string buf;
        Pix pix;
        if (pixadb != null) {
            int ndb = pixadb.Count;
            if (ndb == 0 || ndb == 5) {  /* first of even and odd box sets */
                buf = AdjustSidePlotName("init", select);
                boxas.PlotSides(buf, out _, out _, out _, out _, out pix);
                pixadb.AddPix(pix, L.L_INSERT);
            }
        }

        int n = boxas.Count;
        boxad = Create(n);
        int diff;
        if (select == L.L_ADJUST_LEFT) {
            boxas.GetMedianVals(out int medleft, out _, out _, out _, out _, out _);
            for (int i = 0; i < n; i++) {
                Box box = boxas.GetBox(i, L.L_COPY);
                box.GetSideLocations(out int left, out _, out _, out _);
                diff = medleft - left;
                if (Math.Abs(diff) >= thresh)
                    Box.AdjustSidesOrNull(box, box, diff - extra, 0, 0, 0);
                boxad.AddBox(box, L.L_INSERT);
            }
        } else if (select == L.L_ADJUST_RIGHT) {
            boxas.GetMedianVals(out _, out _, out int medright, out _, out _, out _);
            for (int i = 0; i < n; i++) {
                Box box = boxas.GetBox(i, L.L_COPY);
                box.GetSideLocations(out _, out int right, out _, out _);
                diff = medright - right;
                if (Math.Abs(diff) >= thresh)
                    Box.AdjustSidesOrNull(box, box, 0, diff + extra, 0, 0);
                boxad.AddBox(box, L.L_INSERT);
            }
        } else if (select == L.L_ADJUST_TOP) {
            boxas.GetMedianVals(out _, out int medtop, out _, out _, out _, out _);
            for (int i = 0; i < n; i++) {
                Box box = boxas.GetBox(i, L.L_COPY);
                box.GetSideLocations(out _, out _, out int top, out _);
                diff = medtop - top;
                if (Math.Abs(diff) >= thresh)
                    Box.AdjustSidesOrNull(box, box, 0, 0, diff - extra, 0);
                boxad.AddBox(box, L.L_INSERT);
            }
        } else {  /* select == L_ADJUST_BOT */
            boxas.GetMedianVals(out _, out _, out _, out int medbot, out _, out _);
            for (int i = 0; i < n; i++) {
                Box box = boxas.GetBox(i, L.L_COPY);
                box.GetSideLocations(out _, out _, out _, out int bot);
                diff = medbot - bot;
                if (Math.Abs(diff) >= thresh)
                    Box.AdjustSidesOrNull(box, box, 0, 0, 0, diff + extra);
                boxad.AddBox(box, L.L_INSERT);
            }
        }

        if (pixadb != null) {
            buf = AdjustSidePlotName("final", select);
            boxad.PlotSides(buf, out _, out _, out _, out _, out pix);
            pixadb.AddPix(pix, L.L_INSERT);
        }
        return boxad;
    }

    // leptonica: adjustSidePlotName (boxfunc5.c)
    private static string AdjustSidePlotName(string preface, int select)
    {
        string buf = preface;
        if (select == L.L_ADJUST_LEFT)
            buf += "-left";
        else if (select == L.L_ADJUST_RIGHT)
            buf += "-right";
        else if (select == L.L_ADJUST_TOP)
            buf += "-top";
        else if (select == L.L_ADJUST_BOT)
            buf += "-bot";
        return buf;
    }

    // leptonica: boxaReconcileSizeByMedian (boxfunc5.c)
    /// <summary>Reconciles the dimensions of outlier boxes with the median dimensions.</summary>
    /// <param name="type">L_CHECK_WIDTH, L_CHECK_HEIGHT, L_CHECK_BOTH</param>
    /// <param name="dfract">threshold fraction of dimensional variation from
    ///                      median; in range (0 ... 1); typ. about 0.05.</param>
    /// <param name="sfract">threshold fraction of side variation from median;
    ///                      in range (0 ... 1); typ. about 0.04.</param>
    /// <param name="factor">expansion for fixed box beyond median width;
    ///                      should be near 1.0.</param>
    /// <param name="pnadelw">diff from median width for boxes above threshold</param>
    /// <param name="pnadelh">diff from median height for boxes above threshold</param>
    /// <param name="pratiowh">ratio of median width/height of boxas</param>
    /// <returns>boxad possibly adjusted from boxas; a copy of boxas on error</returns>
    /// <remarks>
    /// (1) The basic idea is to identify significant differences in box
    ///     dimension (either width or height) and modify the outlier boxes.
    /// (2) type specifies if we are reconciling the width, height or both.
    /// (3) dfract specifies the tolerance for different dimensions. Any
    ///     box with a fractional difference from the median size that
    ///     exceeds dfract will be altered.
    /// (4) sfract specifies the tolerance for different side locations.
    ///     If a box has been marked by (3) for alteration, any side
    ///     location that differs from the median side location by
    ///     more than sfract of the median dimension (medw or medh)
    ///     will be moved.
    /// (5) Median width and height are found for all valid boxes (i.e.,
    ///     for all boxes with width and height > 0.
    ///     Median side locations are found separately for even and odd boxes,
    ///     using only boxes that are "inliers"; i.e., that have been
    ///     found by (3) to be within tolerance for width or height.
    /// (6) If all box dimensions are within threshold of the median size,
    ///     just return a copy.  Otherwise, box sides of the outliers
    ///     will be adjusted.
    /// (7) Using sfract, sides that are sufficiently far from the median
    ///     are first moved to the median value.  Then they are moved
    ///     together (in or out) so that the final box dimension
    ///     is factor times the median dimension.
    /// (8) The arrays that are the initial deviation from median size
    ///     (width and height) are optionally returned.  Also optionally
    ///     returned is the median w/h asperity ratio of the input boxas.
    /// </remarks>
    public Boxa ReconcileSizeByMedian(int type, float dfract, float sfract, float factor,
                                      out Numa? pnadelw, out Numa? pnadelh,
                                      out float pratiowh)
    {
        const string func = "boxaReconcileSizeByMedian";
        Boxa boxas = this;
        pnadelw = null;
        pnadelh = null;
        pratiowh = 0.0f;
        if (type != L.L_CHECK_WIDTH && type != L.L_CHECK_HEIGHT &&
            type != L.L_CHECK_BOTH) {
            Log.Warning(func, "invalid type; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }
        if (dfract <= 0.0 || dfract >= 0.5) {
            Log.Warning(func, "invalid dimensional fract; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }
        if (sfract <= 0.0 || sfract >= 0.5) {
            Log.Warning(func, "invalid side fract; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }
        if (factor < 0.8 || factor > 1.25)
            Log.Warning(func, CFormat.Sprintf("factor %5.3f is typ. closer to 1.0\n", factor));
        if (boxas.GetValidCount() < 6) {
            Log.Warning(func, "need at least 6 valid boxes; returning copy\n");
            return boxas.Copy(L.L_COPY);
        }

            /* If reconciling both width and height, optionally return array of
             * median deviations and even/odd ratio for width measurements */
        Boxa boxad;
        if (type == L.L_CHECK_BOTH) {
            Boxa boxa1 = boxas.ReconcileSizeByMedian(L.L_CHECK_WIDTH, dfract, sfract,
                                                     factor, out pnadelw, out _,
                                                     out pratiowh);
            boxad = boxa1.ReconcileSizeByMedian(L.L_CHECK_HEIGHT, dfract, sfract,
                                                factor, out _, out pnadelh, out _);
            return boxad;
        }

        int n = boxas.Count;
        Numa naind = Numa.Create(n);  /* outlier indicator array */
        Boxa boxae = Create(0);  /* even inliers */
        Boxa boxao = Create(0);  /* odd inliers */
        bool outfound = false;
        int ne, no, maxdel, medleft, medright, medtop, medbot;
        int medlefte, medrighte, medlefto, medrighto;
        int medtope, medbote, medtopo, medboto;
        if (type == L.L_CHECK_WIDTH) {
            boxas.MedianDimensions(out int medw, out int medh, out _, out _, out _, out _,
                                   out Numa? nadelw, out _);
            pratiowh = (float)medw / (float)medh;
            Log.Info(func, CFormat.Sprintf("median ratio w/h = %5.3f\n", pratiowh));
            pnadelw = nadelw;

                /* Check for outliers; assemble inliers */
            for (int i = 0; i < n; i++) {
                Box? box = boxas.GetValidBox(i, L.L_COPY);
                if (box == null) {
                    naind.AddNumber(0);
                    continue;
                }
                box.GetGeometry(out _, out _, out int bw, out _);
                float brat = (float)bw / (float)medw;
                if (brat < 1.0 - dfract || brat > 1.0 + dfract) {
                    outfound = true;
                    naind.AddNumber(1);
                } else {  /* add to inliers */
                    naind.AddNumber(0);
                    if (i % 2 == 0)
                        boxae.AddBox(box, L.L_INSERT);
                    else
                        boxao.AddBox(box, L.L_INSERT);
                }
            }
            if (!outfound) {  /* nothing to do */
                Log.Info(func, "no width outlier boxes found\n");
                return boxas.Copy(L.L_COPY);
            }

                /* Get left/right parameters from inliers.  Handle the case
                 * where there are no inliers for one of the sets.  For example,
                 * when all the even boxes have a different dimension from
                 * the odd boxes, and the median arbitrarily gets assigned
                 * to the even boxes, there are no odd inliers; in that case,
                 * use the even inliers sides to decide whether to adjust
                 * the left or the right sides of individual outliers. */
            Log.Info(func, "fixing width of outlier boxes\n");
            medlefte = medrighte = medlefto = medrighto = 0;
            if ((ne = boxae.GetValidCount()) > 0)
                boxae.GetMedianVals(out medlefte, out _, out medrighte, out _, out _, out _);
            if ((no = boxao.GetValidCount()) > 0)
                boxao.GetMedianVals(out medlefto, out _, out medrighto, out _, out _, out _);
            if (ne == 0) {  /* use odd inliers values for both */
                medlefte = medlefto;
                medrighte = medrighto;
            } else if (no == 0) {  /* use even inliers values for both */
                medlefto = medlefte;
                medrighto = medrighte;
            }

                /* Adjust the left and/or right sides of outliers.
                 * For each box that is a dimensional outlier, consider each side.
                 * Any side that differs fractionally from the median value
                 * by more than sfract times the median width (medw) is set to
                 * the median value for that side.  Then both sides are moved
                 * an equal distance in or out to make w = factor * medw. */
            boxad = Create(n);
            maxdel = (int)(sfract * medw + 0.5);
            for (int i = 0; i < n; i++) {
                Box box = boxas.GetBox(i, L.L_COPY);
                bool isvalid = box.IsValid();
                int ind = naind.GetIValue(i);
                medleft = (i % 2 == 0) ? medlefte : medlefto;
                medright = (i % 2 == 0) ? medrighte : medrighto;
                if (ind == 1 && isvalid) {  /* adjust sides */
                    box.GetSideLocations(out int left, out int right, out _, out _);
                    if (Math.Abs(left - medleft) > maxdel) left = medleft;
                    if (Math.Abs(right - medright) > maxdel) right = medright;
                    int del = (int)(factor * medw - (right - left)) / 2;
                    box.SetSide(L.L_SET_LEFT, left - del, 0);
                    box.SetSide(L.L_SET_RIGHT, right + del, 0);
                }
                boxad.AddBox(box, L.L_INSERT);
            }
        } else {  /* L_CHECK_HEIGHT */
            boxas.MedianDimensions(out int medw, out int medh, out _, out _, out _, out _,
                                   out _, out Numa? nadelh);
            pratiowh = (float)medw / (float)medh;
            Log.Info(func, CFormat.Sprintf("median ratio w/h = %5.3f\n", pratiowh));
            pnadelh = nadelh;

                /* Check for outliers; assemble inliers */
            for (int i = 0; i < n; i++) {
                Box? box = boxas.GetValidBox(i, L.L_COPY);
                if (box == null) {
                    naind.AddNumber(0);
                    continue;
                }
                box.GetGeometry(out _, out _, out _, out int bh);
                float brat = (float)bh / (float)medh;
                if (brat < 1.0 - dfract || brat > 1.0 + dfract) {
                    outfound = true;
                    naind.AddNumber(1);
                } else {  /* add to inliers */
                    naind.AddNumber(0);
                    if (i % 2 == 0)
                        boxae.AddBox(box, L.L_INSERT);
                    else
                        boxao.AddBox(box, L.L_INSERT);
                }
            }
            if (!outfound) {  /* nothing to do */
                Log.Info(func, "no height outlier boxes found\n");
                return boxas.Copy(L.L_COPY);
            }

                /* Get top/bot parameters from inliers.  Handle the case
                 * where there are no inliers for one of the sets.  For example,
                 * when all the even boxes have a different dimension from
                 * the odd boxes, and the median arbitrarily gets assigned
                 * to the even boxes, there are no odd inliers; in that case,
                 * use the even inlier sides to decide whether to adjust
                 * the top or the bottom sides of individual outliers. */
            Log.Info(func, "fixing height of outlier boxes\n");
            medtope = medbote = medtopo = medboto = 0;
            if ((ne = boxae.GetValidCount()) > 0)
                boxae.GetMedianVals(out _, out medtope, out _, out medbote, out _, out _);
            if ((no = boxao.GetValidCount()) > 0)
                boxao.GetMedianVals(out _, out medtopo, out _, out medboto, out _, out _);
            if (ne == 0) {  /* use odd inliers values for both */
                medtope = medtopo;
                medbote = medboto;
            } else if (no == 0) {  /* use even inliers values for both */
                medtopo = medtope;
                medboto = medbote;
            }

                /* Adjust the top and/or bottom sides of outliers.
                 * For each box that is a dimensional outlier, consider each side.
                 * Any side that differs fractionally from the median value
                 * by more than sfract times the median height (medh) is
                 * set to the median value for that that side.  Then both
                 * sides are moved an equal distance in or out to make
                 * h = factor * medh). */
            boxad = Create(n);
            maxdel = (int)(sfract * medh + 0.5);
            for (int i = 0; i < n; i++) {
                Box box = boxas.GetBox(i, L.L_COPY);
                bool isvalid = box.IsValid();
                int ind = naind.GetIValue(i);
                medtop = (i % 2 == 0) ? medtope : medtopo;
                medbot = (i % 2 == 0) ? medbote : medboto;
                if (ind == 1 && isvalid) {  /* adjust sides */
                    box.GetSideLocations(out _, out _, out int top, out int bot);
                    if (Math.Abs(top - medtop) > maxdel) top = medtop;
                    if (Math.Abs(bot - medbot) > maxdel) bot = medbot;
                    int del = (int)(factor * medh - (bot - top)) / 2;  /* typ > 0 */
                    box.SetSide(L.L_SET_TOP, Math.Max(0, top - del), 0);
                    box.SetSide(L.L_SET_BOT, bot + del, 0);
                }
                boxad.AddBox(box, L.L_INSERT);
            }
        }
        return boxad;
    }

    // leptonica: boxaPlotSides (boxfunc5.c)
    /// <summary>Plots the four sides of the boxes against the box index (debugging).</summary>
    /// <param name="plotname">[optional], can be null</param>
    /// <param name="pnal">na of left sides</param>
    /// <param name="pnat">na of top sides</param>
    /// <param name="pnar">na of right sides</param>
    /// <param name="pnab">na of bottom sides</param>
    /// <param name="ppixd">pix of the output plot</param>
    /// <remarks>
    /// (1) This debugging function shows the progression of the four
    ///     sides in the boxa.  There must be at least 2 boxes.
    /// (2) If there are invalid boxes (e.g., if only even or odd
    ///     indices have valid boxes), this will fill them with the
    ///     nearest valid box before plotting.
    /// (3) The plotfiles are put in /tmp/lept/plots/, and are named
    ///     either with plotname or, if null, a default name.  If
    ///     plotname is used, make sure it has no whitespace characters.
    /// </remarks>
    public void PlotSides(string? plotname, out Numa pnal, out Numa pnat,
                          out Numa pnar, out Numa pnab, out Pix ppixd)
    {
        Boxa boxa = this;
        int n = boxa.Count;
        if (n < 2)
            throw Lept.Arg("boxaPlotSides", "less than 2 boxes");

        Boxa boxat = boxa.FillSequence(L.L_USE_ALL_BOXES, 0);

            /* Build the numas for each side */
        Numa nal = Numa.Create(n);
        Numa nat = Numa.Create(n);
        Numa nar = Numa.Create(n);
        Numa nab = Numa.Create(n);

        for (int i = 0; i < n; i++) {
            boxat.GetBoxGeometry(i, out int left, out int top, out int w, out int h);
            int right = left + w - 1;
            int bot = top + h - 1;
            nal.AddNumber(left);
            nat.AddNumber(top);
            nar.AddNumber(right);
            nab.AddNumber(bot);
        }

        Utils.LeptMkdir("lept/plots");
        string buf, titlebuf;
        if (plotname != null) {
            buf = $"/tmp/lept/plots/sides.{plotname}";
            titlebuf = $"{plotname}: Box sides vs. box index";
        } else {
            buf = $"/tmp/lept/plots/sides.{boxaPlotSidesId++}";
            titlebuf = "Box sides vs. box index";
        }
        Gplot gplot = Gplot.Create(buf, L.GPLOT_PNG, titlebuf,
                                   "box index", "side location");
        gplot.AddPlot(null, nal, L.GPLOT_LINES, "left side");
        gplot.AddPlot(null, nat, L.GPLOT_LINES, "top side");
        gplot.AddPlot(null, nar, L.GPLOT_LINES, "right side");
        gplot.AddPlot(null, nab, L.GPLOT_LINES, "bottom side");
        ppixd = gplot.MakeOutputPix();

        pnal = nal;
        pnat = nat;
        pnar = nar;
        pnab = nab;
    }

    // leptonica: boxaPlotSizes (boxfunc5.c)
    /// <summary>Plots the width and height of the boxes against the box index (debugging).</summary>
    /// <param name="plotname">[optional], can be null</param>
    /// <param name="pnaw">na of widths</param>
    /// <param name="pnah">na of heights</param>
    /// <param name="ppixd">pix of the output plot</param>
    /// <remarks>
    /// (1) This debugging function shows the progression of box width
    ///     and height in the boxa.  There must be at least 2 boxes.
    /// (2) If there are invalid boxes (e.g., if only even or odd
    ///     indices have valid boxes), this will fill them with the
    ///     nearest valid box before plotting.
    /// (3) The plotfiles are put in /tmp/lept/plots/, and are named
    ///     either with plotname or, if null, a default name.  If
    ///     plotname is used, make sure it has no whitespace characters.
    /// </remarks>
    public void PlotSizes(string? plotname, out Numa pnaw, out Numa pnah, out Pix ppixd)
    {
        Boxa boxa = this;
        int n = boxa.Count;
        if (n < 2)
            throw Lept.Arg("boxaPlotSizes", "less than 2 boxes");

        Boxa boxat = boxa.FillSequence(L.L_USE_ALL_BOXES, 0);

            /* Build the numas for the width and height */
        Numa naw = Numa.Create(n);
        Numa nah = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            boxat.GetBoxGeometry(i, out _, out _, out int w, out int h);
            naw.AddNumber(w);
            nah.AddNumber(h);
        }

        Utils.LeptMkdir("lept/plots");
        string buf, titlebuf;
        if (plotname != null) {
            buf = $"/tmp/lept/plots/size.{plotname}";
            titlebuf = $"{plotname}: Box size vs. box index";
        } else {
            buf = $"/tmp/lept/plots/size.{boxaPlotSizesId++}";
            titlebuf = "Box size vs. box index";
        }
        Gplot gplot = Gplot.Create(buf, L.GPLOT_PNG, titlebuf,
                                   "box index", "box dimension");
        gplot.AddPlot(null, naw, L.GPLOT_LINES, "width");
        gplot.AddPlot(null, nah, L.GPLOT_LINES, "height");
        ppixd = gplot.MakeOutputPix();

        pnaw = naw;
        pnah = nah;
    }

    // leptonica: boxaFillSequence (boxfunc5.c)
    /// <summary>Replaces invalid boxes with a copy of the nearest valid box.</summary>
    /// <param name="useflag">L_USE_ALL_BOXES, L_USE_SAME_PARITY_BOXES</param>
    /// <param name="debug">1 for debug output</param>
    /// <returns>boxad filled boxa</returns>
    /// <remarks>
    /// (1) This simple function replaces invalid boxes with a copy of
    ///     the nearest valid box, selected from either the entire
    ///     sequence (L_USE_ALL_BOXES) or from the boxes with the
    ///     same parity (L_USE_SAME_PARITY_BOXES).  It returns a new boxa.
    /// (2) This is useful if you expect boxes in the sequence to
    ///     vary slowly with index.
    /// </remarks>
    public Boxa FillSequence(int useflag, int debug)
    {
        const string func = "boxaFillSequence";
        Boxa boxas = this;
        if (useflag != L.L_USE_ALL_BOXES && useflag != L.L_USE_SAME_PARITY_BOXES)
            throw Lept.Arg(func, "invalid useflag");

        int n = boxas.Count;
        int nv = boxas.GetValidCount();
        if (n == nv)
            return boxas.Copy(L.L_COPY);  /* all valid */
        if (debug != 0)
            Log.Info(func, $"{nv} valid boxes, {n - nv} invalid boxes\n");
        if (useflag == L.L_USE_SAME_PARITY_BOXES && n < 3) {
            Log.Warning(func, "n < 3; some invalid\n");
            return boxas.Copy(L.L_COPY);
        }

        Boxa boxad;
        if (useflag == L.L_USE_ALL_BOXES) {
            boxad = boxas.Copy(L.L_COPY);
            FillAll(boxad);
        } else {
            boxas.SplitEvenOdd(0, out Boxa boxae, out Boxa boxao);
            FillAll(boxae);
            FillAll(boxao);
            boxad = boxae.MergeEvenOdd(boxao, 0);
        }

        nv = boxad.GetValidCount();
        if (n != nv)
            Log.Warning(func, $"there are still {n - nv} invalid boxes\n");

        return boxad;
    }

    // leptonica: boxaFillAll (boxfunc5.c)
    /// <summary>Replaces every invalid box with the nearest valid box.</summary>
    /// <remarks>
    /// (1) This static function replaces every invalid box with the
    ///     nearest valid box.  If there are no valid boxes, it
    ///     issues a warning.
    /// </remarks>
    private static void FillAll(Boxa boxa)
    {
        const string func = "boxaFillAll";
        int n = boxa.Count;
        int nv = boxa.GetValidCount();
        if (n == nv) return;
        if (nv == 0) {
            Log.Warning(func, $"no valid boxes out of {n} boxes\n");
            return;
        }

            /* Make indicator array for valid boxes */
        int[] indic = new int[n];
        for (int i = 0; i < n; i++) {
            Box? box = boxa.GetValidBox(i, L.L_CLONE);
            if (box != null)
                indic[i] = 1;
        }

            /* Replace invalid boxes with the nearest valid one */
        for (int i = 0; i < n; i++) {
            Box? box = boxa.GetValidBox(i, L.L_CLONE);
            if (box == null) {
                int spandown = 10000000;
                int spanup = 10000000;
                for (int j = i - 1; j >= 0; j--) {
                    if (indic[j] == 1) {
                        spandown = i - j;
                        break;
                    }
                }
                for (int j = i + 1; j < n; j++) {
                    if (indic[j] == 1) {
                        spanup = j - i;
                        break;
                    }
                }
                Box boxt;
                if (spandown < spanup)
                    boxt = boxa.GetBox(i - spandown, L.L_COPY);
                else
                    boxt = boxa.GetBox(i + spanup, L.L_COPY);
                boxa.ReplaceBox(i, boxt);
            }
        }
    }

    // leptonica: boxaSizeVariation (boxfunc5.c)
    /// <summary>Gives several measures of the smoothness of the width or height of the boxes.</summary>
    /// <param name="type">L_SELECT_WIDTH, L_SELECT_HEIGHT</param>
    /// <param name="pdel_evenodd">average absolute value of (even - odd) size pairs</param>
    /// <param name="prms_even">rms deviation of even boxes</param>
    /// <param name="prms_odd">rms deviation of odd boxes</param>
    /// <param name="prms_all">rms deviation of all boxes</param>
    /// <remarks>
    /// (1) This gives several measures of the smoothness of either the
    ///     width or height of a sequence of boxes.
    ///     See boxaMedianDimensions() for some other measures.
    /// (2) Statistics can be found separately for even and odd boxes.
    ///     Additionally, the average pair-wise difference between
    ///     adjacent even and odd boxes can be returned.
    /// (3) The use case is bounding boxes for scanned page images,
    ///     where ideally the sizes should have little variance.
    /// </remarks>
    public void SizeVariation(int type, out float pdel_evenodd, out float prms_even,
                              out float prms_odd, out float prms_all)
    {
        const string func = "boxaSizeVariation";
        Boxa boxa = this;
        pdel_evenodd = 0.0f;
        prms_even = 0.0f;
        prms_odd = 0.0f;
        prms_all = 0.0f;
        if (type != L.L_SELECT_WIDTH && type != L.L_SELECT_HEIGHT)
            throw Lept.Arg(func, "invalid type");
        int n = boxa.Count;
        if (n < 4)
            throw Lept.Arg(func, "too few boxes");

        boxa.SplitEvenOdd(0, out Boxa boxae, out Boxa boxao);
        int ne = boxae.Count;
        int no = boxao.Count;
        int nmin = Math.Min(ne, no);
        if (nmin == 0)
            throw Lept.Arg(func, "either no even or no odd boxes");

        Numa nae, nao, na_all;
        if (type == L.L_SELECT_WIDTH) {
            boxae.GetSizes(out nae, out _);
            boxao.GetSizes(out nao, out _);
            boxa.GetSizes(out na_all, out _);
        } else {   /* L_SELECT_HEIGHT) */
            boxae.GetSizes(out _, out nae);
            boxao.GetSizes(out _, out nao);
            boxa.GetSizes(out _, out na_all);
        }

        float sum = 0.0f;
        for (int i = 0; i < nmin; i++) {
            int vale = nae.GetIValue(i);
            int valo = nao.GetIValue(i);
            sum += Math.Abs(vale - valo);
        }
        pdel_evenodd = sum / nmin;
        nae.SimpleStats(0, -1, out _, out _, out prms_even);
        nao.SimpleStats(0, -1, out _, out _, out prms_odd);
        na_all.SimpleStats(0, -1, out _, out _, out prms_all);
    }

    // leptonica: boxaMedianDimensions (boxfunc5.c)
    /// <summary>Finds the median width and height of the boxes, overall and of the even and odd ones.</summary>
    /// <param name="pmedw">median width of all boxes</param>
    /// <param name="pmedh">median height of all boxes</param>
    /// <param name="pmedwe">median width of even boxes</param>
    /// <param name="pmedwo">median width of odd boxes</param>
    /// <param name="pmedhe">median height of even boxes</param>
    /// <param name="pmedho">median height of odd boxes</param>
    /// <param name="pnadelw">width diff of each box from median</param>
    /// <param name="pnadelh">height diff of each box from median</param>
    /// <remarks>
    /// (1) This provides information that (1) allows identification of
    ///     boxes that have unusual (outlier) width or height, and (2) can
    ///     be used to regularize the sizes of the outlier boxes, assuming
    ///     that the boxes satisfy a fairly regular sequence and should
    ///     mostly have the same width and height.
    /// (2) This finds the median width and height, as well as separate
    ///     median widths and heights of even and odd boxes.  It also
    ///     generates arrays that give the difference in width and height
    ///     of each box from the median, which can be used to correct
    ///     individual boxes.
    /// </remarks>
    public void MedianDimensions(out int pmedw, out int pmedh, out int pmedwe,
                                 out int pmedwo, out int pmedhe, out int pmedho,
                                 out Numa? pnadelw, out Numa? pnadelh)
    {
        const string func = "boxaMedianDimensions";
        Boxa boxas = this;
        pmedw = pmedh = pmedwe = pmedwo = pmedhe = pmedho = 0;
        pnadelw = null;
        pnadelh = null;
        if (boxas.GetValidCount() < 6)
            throw Lept.Arg(func, "need at least 6 valid boxes");

            /* Require at least 3 valid boxes of both types */
        boxas.SplitEvenOdd(0, out Boxa boxae, out Boxa boxao);
        if (boxae.GetValidCount() < 3 || boxao.GetValidCount() < 3)
            throw Lept.Arg(func, "don't have 3+ valid boxes of each type");

            /* Get the relevant median widths and heights */
        boxas.GetMedianVals(out _, out _, out _, out _, out int medw, out int medh);
        boxae.GetMedianVals(out _, out _, out _, out _, out int medwe, out int medhe);
        boxao.GetMedianVals(out _, out _, out _, out _, out int medwo, out int medho);
        pmedw = medw;
        pmedh = medh;
        pmedwe = medwe;
        pmedwo = medwo;
        pmedhe = medhe;
        pmedho = medho;

            /* Find the variation from median dimension for each box */
        int n = boxas.Count;
        Numa nadelw = Numa.Create(n);
        Numa nadelh = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            boxas.GetBoxGeometry(i, out _, out _, out int bw, out int bh);
            if (bw == 0 || bh == 0) {  /* invalid box */
                nadelw.AddNumber(0);
                nadelh.AddNumber(0);
            } else {
                nadelw.AddNumber(bw - medw);
                nadelh.AddNumber(bh - medh);
            }
        }
        pnadelw = nadelw;
        pnadelh = nadelh;
    }
}
