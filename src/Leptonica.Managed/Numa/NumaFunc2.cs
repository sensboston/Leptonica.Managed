using System;

namespace Leptonica;

/*
 *  numafunc2.c: Numa utilities for
 *     - morphological operations
 *     - arithmetic transforms
 *     - windowed statistical operations
 *     - histogram extraction
 *     - histogram comparison
 *     - extrema finding
 *     - frequency and crossing analysis
 *
 *  Things to remember when using the Numa:
 *
 *    (1) The numa is a struct, not an array.  Always use accessors
 *        (see numabasic.c), never the fields directly.
 *
 *    (2) The number array holds l_float32 values.  It can also
 *        be used to store l_int32 values.  See numabasic.c for
 *        details on using the accessors.  Integers larger than
 *        about 10M will lose accuracy due on retrieval due to round-off.
 *        For large integers, use the dna (array of l_float64) instead.
 *
 *    (3) Occasionally, in the comments we denote the i-th element of a
 *        numa by na[i].  This is conceptual only -- the numa is not an array!
 *
 *  Some general comments on histograms:
 *
 *    (1) Histograms are the generic statistical representation of
 *        the data about some attribute.  Typically they're not
 *        normalized -- they simply give the number of occurrences
 *        within each range of values of the attribute.  This range
 *        of values is referred to as a 'bucket'.  For example,
 *        the histogram could specify how many connected components
 *        are found for each value of their width; in that case,
 *        the bucket size is 1.
 *
 *    (2) In leptonica, all buckets have the same size.  Histograms
 *        are therefore specified by a numa of occurrences, along
 *        with two other numbers: the 'value' associated with the
 *        occupants of the first bucket and the size (i.e., 'width')
 *        of each bucket.  These two numbers then allow us to calculate
 *        the value associated with the occupants of each bucket.
 *        These numbers are fields in the numa, initialized to
 *        a startx value of 0.0 and a binsize of 1.0.  Accessors for
 *        these fields are functions numa*Parameters().  All histograms
 *        must have these two numbers properly set.
 *
 *  Differences from C:
 *    - Functions with a single required output return it.  Several outputs
 *      are out parameters in the C order.  Optional outputs are out parameters
 *      too and are always computed; callers that do not need one discard it
 *      with "out _".  Where a NULL output changes the result and not only the
 *      work (numaMakeHistogram() without &binstart, numaSplitDistribution()
 *      with &nascore) there are two overloads.
 *    - Functions that return an error code only to report that no result
 *      exists (numaFindLocForThreshold(), numaSelectCrossingThreshold())
 *      return a bool (true if OK), log the error, and set the outputs to 0.
 *      Invalid arguments throw.
 *    - The C code ignores the return value of the accessors numaGetFValue()
 *      and numaGetIValue(); where an index can be invalid there, the
 *      port gets 0 and logs the error, as C does (GetFValueOrZero).
 *    - grayHistogramsToEMD() and grayInterHistogramStats() are static methods
 *      of Numa.  genConstrainedNumaInRange() is a static method of Numa.
 *    - numaDestroy() is not ported (the GC owns the objects).  The debug output
 *      under DEBUG_HISTO, DEBUG_CROSSINGS and DEBUG_FREQUENCY (all 0 in C) is not ported.
 *    - The numa array (fa, fas, ...) is accessed through numaGetFArray(L_NOCOPY)
 *      as in C: the array of a Numa may be longer than its count.
 */

/// <summary>Constants from array.h and pix.h used by numafunc2.c and its callees.</summary>
public static partial class L
{
    /* Numa interpolation (array.h) */

    /* Numa border adding (array.h) */

    /* Sort mode (pix.h) */
}

/// <summary>Array of numbers (struct Numa).</summary>
public sealed partial class Numa
{
    /* bin sizes in numaMakeHistogram() */
    private static readonly int[] BinSizeArray = {
        2, 5, 10, 20, 50, 100, 200, 500, 1000,
        2000, 5000, 10000, 20000, 50000, 100000, 200000,
        500000, 1000000, 2000000, 5000000, 10000000,
        200000000, 50000000, 100000000 };  // NOTE(upstream): 200000000 is out of order (20000000?)
    private const int NBinSizes = 24;

    /* Gets na[index] where C ignores the return value of numaGetFValue():
     * an invalid index logs an error and the value stays 0. */
    private static float GetFValueOrZero(Numa na, int index)
    {
        if (index < 0 || index >= na.Count) {
            Log.Error("numaGetFValue", "index not valid");
            return 0.0f;
        }
        return na.GetFValue(index);
    }

    /*----------------------------------------------------------------------*
     *                     Morphological operations                         *
     *----------------------------------------------------------------------*/
    // leptonica: numaErode (numafunc2.c)
    /// <summary>Erodes the numa with a linear sel of all hits.</summary>
    /// <param name="size">of sel; greater than 0, odd.  The origin
    /// is implicitly in the center.</param>
    /// <returns>nad eroded</returns>
    /// <remarks>
    /// (1) The structuring element (sel) is linear, all "hits"
    /// (2) If size == 1, this returns a copy
    /// (3) General comment.  The morphological operations are equivalent
    ///     to those that would be performed on a 1-dimensional fpix.
    ///     However, because we have not implemented morphological
    ///     operations on fpix, we do this here.  Because it is only
    ///     1 dimensional, there is no reason to use the more
    ///     complicated van Herk/Gil-Werman algorithm, and we do it
    ///     by brute force.
    /// </remarks>
    public Numa Erode(int size)
    {
        const string func = "numaErode";
        int i, j, n, hsize, len;
        float minval;
        float[] fa, fas, fad;
        Numa nad;

        if (size <= 0)
            throw Lept.Arg(func, "size must be > 0");
        if ((size & 1) == 0) {
            Log.Warning(func, "sel size must be odd; increasing by 1");
            size++;
        }

        if (size == 1)
            return Copy();

            /* Make a source fa (fas) that has an added (size / 2) boundary
             * on left and right, contains a copy of nas in the interior region
             * (between 'size' and 'size + n', and has large values
             * inserted in the boundary (because it is an erosion). */
        n = Count;
        hsize = size / 2;
        len = n + 2 * hsize;
        fas = new float[len];
        for (i = 0; i < hsize; i++)
            fas[i] = 1.0e37f;
        for (i = hsize + n; i < len; i++)
            fas[i] = 1.0e37f;
        fa = GetFArray(L.L_NOCOPY);
        for (i = 0; i < n; i++)
            fas[hsize + i] = fa[i];

        nad = MakeConstant(0, n);
        nad.CopyParameters(this);
        fad = nad.GetFArray(L.L_NOCOPY);
        for (i = 0; i < n; i++) {
            minval = 1.0e37f;  /* start big */
            for (j = 0; j < size; j++)
                minval = (minval < fas[i + j]) ? minval : fas[i + j];  /* L_MIN */
            fad[i] = minval;
        }

        return nad;
    }

    // leptonica: numaDilate (numafunc2.c)
    /// <summary>Dilates the numa with a linear sel of all hits.</summary>
    /// <param name="size">of sel; greater than 0, odd.  The origin
    /// is implicitly in the center.</param>
    /// <returns>nad dilated</returns>
    /// <remarks>
    /// (1) The structuring element (sel) is linear, all "hits"
    /// (2) If size == 1, this returns a copy
    /// </remarks>
    public Numa Dilate(int size)
    {
        const string func = "numaDilate";
        int i, j, n, hsize, len;
        float maxval;
        float[] fa, fas, fad;
        Numa nad;

        if (size <= 0)
            throw Lept.Arg(func, "size must be > 0");
        if ((size & 1) == 0) {
            Log.Warning(func, "sel size must be odd; increasing by 1");
            size++;
        }

        if (size == 1)
            return Copy();

            /* Make a source fa (fas) that has an added (size / 2) boundary
             * on left and right, contains a copy of nas in the interior region
             * (between 'size' and 'size + n', and has small values
             * inserted in the boundary (because it is a dilation). */
        n = Count;
        hsize = size / 2;
        len = n + 2 * hsize;
        fas = new float[len];
        for (i = 0; i < hsize; i++)
            fas[i] = -1.0e37f;
        for (i = hsize + n; i < len; i++)
            fas[i] = -1.0e37f;
        fa = GetFArray(L.L_NOCOPY);
        for (i = 0; i < n; i++)
            fas[hsize + i] = fa[i];

        nad = MakeConstant(0, n);
        nad.CopyParameters(this);
        fad = nad.GetFArray(L.L_NOCOPY);
        for (i = 0; i < n; i++) {
            maxval = -1.0e37f;  /* start small */
            for (j = 0; j < size; j++)
                maxval = (maxval > fas[i + j]) ? maxval : fas[i + j];  /* L_MAX */
            fad[i] = maxval;
        }

        return nad;
    }

    // leptonica: numaOpen (numafunc2.c)
    /// <summary>Opens the numa with a linear sel of all hits.</summary>
    /// <param name="size">of sel; greater than 0, odd.  The origin
    /// is implicitly in the center.</param>
    /// <returns>nad opened</returns>
    /// <remarks>
    /// (1) The structuring element (sel) is linear, all "hits"
    /// (2) If size == 1, this returns a copy
    /// </remarks>
    public Numa Open(int size)
    {
        const string func = "numaOpen";
        Numa nat, nad;

        if (size <= 0)
            throw Lept.Arg(func, "size must be > 0");
        if ((size & 1) == 0) {
            Log.Warning(func, "sel size must be odd; increasing by 1");
            size++;
        }

        if (size == 1)
            return Copy();

        nat = Erode(size);
        nad = nat.Dilate(size);
        return nad;
    }

    // leptonica: numaClose (numafunc2.c)
    /// <summary>Closes the numa with a linear sel of all hits.</summary>
    /// <param name="size">of sel; greater than 0, odd.  The origin
    /// is implicitly in the center.</param>
    /// <returns>nad closed</returns>
    /// <remarks>
    /// (1) The structuring element (sel) is linear, all "hits"
    /// (2) If size == 1, this returns a copy
    /// (3) We add a border before doing this operation, for the same
    ///     reason that we add a border to a pix before doing a safe closing.
    ///     Without the border, a small component near the border gets
    ///     clipped at the border on dilation, and can be entirely removed
    ///     by the following erosion, violating the basic extensivity
    ///     property of closing.
    /// </remarks>
    public Numa Close(int size)
    {
        const string func = "numaClose";
        Numa nab, nat1, nat2, nad;

        if (size <= 0)
            throw Lept.Arg(func, "size must be > 0");
        if ((size & 1) == 0) {
            Log.Warning(func, "sel size must be odd; increasing by 1");
            size++;
        }

        if (size == 1)
            return Copy();

        nab = AddBorder(size, size, 0);  /* to preserve extensivity */
        nat1 = nab.Dilate(size);
        nat2 = nat1.Erode(size);
        nad = nat2.RemoveBorder(size, size);
        return nad;
    }

    /*----------------------------------------------------------------------*
     *                            Other transforms                          *
     *----------------------------------------------------------------------*/
    // leptonica: numaTransform (numafunc2.c)
    /// <summary>Shifts and scales all the numbers.</summary>
    /// <param name="shift">add this to each number</param>
    /// <param name="scale">multiply each number by this</param>
    /// <returns>nad with all values shifted and scaled</returns>
    /// <remarks>(1) Each number is shifted before scaling.</remarks>
    public Numa Transform(float shift, float scale)
    {
        int i, n;
        float val;
        Numa nad;

        n = Count;
        nad = Create(n);
        nad.CopyParameters(this);
        for (i = 0; i < n; i++) {
            val = GetFValue(i);
            val = scale * (val + shift);
            nad.AddNumber(val);
        }
        return nad;
    }

    // leptonica: numaSimpleStats (numafunc2.c)
    /// <summary>Mean, variance and rms deviation of a range of the numa.</summary>
    /// <param name="first">first element to use</param>
    /// <param name="last">last element to use; -1 to go to the end</param>
    /// <param name="mean">mean value</param>
    /// <param name="var">variance</param>
    /// <param name="rvar">rms deviation from the mean</param>
    public void SimpleStats(int first, int last, out float mean, out float var, out float rvar)
    {
        const string func = "numaSimpleStats";
        int i, n, ni;
        float sum, sumsq, val;

        mean = 0.0f;
        var = 0.0f;
        rvar = 0.0f;
        if ((n = Count) == 0)
            throw Lept.Arg(func, "na is empty");
        first = Math.Max(0, first);
        if (last < 0) last = n - 1;
        if (first >= n)
            throw Lept.Arg(func, "invalid first");
        if (last >= n) {
            Log.Warning(func, CFormat.Sprintf("last = %d is beyond max index = %d; adjusting", last, n - 1));
            last = n - 1;
        }
        if (first > last)
            throw Lept.Arg(func, "first > last\n");
        ni = last - first + 1;
        sum = sumsq = 0.0f;
        for (i = first; i <= last; i++) {
            val = GetFValue(i);
            sum += val;
            sumsq += val * val;
        }

        mean = sum / ni;
        var = sumsq / ni - mean * mean;
        rvar = MathF.Sqrt(var);
    }

    // leptonica: numaWindowedStats (numafunc2.c)
    /// <summary>Windowed mean, mean square, variance and rms deviation.</summary>
    /// <param name="wc">half width of the window</param>
    /// <param name="nam">mean value in window</param>
    /// <param name="nams">mean square value in window</param>
    /// <param name="nav">variance in window</param>
    /// <param name="narv">rms deviation from the mean</param>
    /// <remarks>
    /// (1) This is a high-level convenience function for calculating
    ///     any or all of these derived arrays.
    /// (2) These statistical measures over the values in the
    ///     rectangular window are:
    ///       ~ average value: [x]  (nam)
    ///       ~ average squared value: [x*x] (nams)
    ///       ~ variance: [(x - [x])*(x - [x])] = [x*x] - [x]*[x]  (nav)
    ///       ~ square-root of variance: (narv)
    ///     where the brackets [ .. ] indicate that the average value is
    ///     to be taken over the window.
    /// (3) Note that the variance is just the mean square difference from
    ///     the mean value; and the square root of the variance is the
    ///     root mean square difference from the mean, sometimes also
    ///     called the 'standard deviation'.
    /// (4) Internally, use mirrored borders to handle values near the
    ///     end of each array.
    /// </remarks>
    public void WindowedStats(int wc, out Numa nam, out Numa nams, out Numa nav, out Numa narv)
    {
        if (2 * wc + 1 > Count)
            Log.Warning("numaWindowedStats", "filter wider than input array!");

        nam = WindowedMean(wc);
        nams = WindowedMeanSquare(wc);
        nam.WindowedVariance(nams, out nav, out narv);
    }

    // leptonica: numaWindowedMean (numafunc2.c)
    /// <summary>Low-pass filter with a flat window.</summary>
    /// <param name="wc">half width of the convolution window</param>
    /// <returns>nad after low-pass filtering</returns>
    /// <remarks>
    /// (1) This is a convolution.  The window has width = 2 * %wc + 1.
    /// (2) We add a mirrored border of size %wc to each end of the array.
    /// </remarks>
    public Numa WindowedMean(int wc)
    {
        int i, n, n1, width;
        float sum, norm;
        float[] fa1, fad, suma;
        Numa na1, nad;

        n = Count;
        width = 2 * wc + 1;  /* filter width */
        if (width > n)
            Log.Warning("numaWindowedMean", "filter wider than input array!");

        na1 = AddSpecifiedBorder(wc, wc, L.L_MIRRORED_BORDER);
        n1 = n + 2 * wc;
        fa1 = na1.GetFArray(L.L_NOCOPY);
        nad = MakeConstant(0, n);
        fad = nad.GetFArray(L.L_NOCOPY);

            /* Make sum array; note the indexing */
        suma = new float[n1 + 1];
        sum = 0.0f;
        suma[0] = 0.0f;
        for (i = 0; i < n1; i++) {
            sum += fa1[i];
            suma[i + 1] = sum;
        }

        norm = 1.0f / (2 * wc + 1);
        for (i = 0; i < n; i++)
            fad[i] = norm * (suma[width + i] - suma[i]);

        return nad;
    }

    // leptonica: numaWindowedMeanSquare (numafunc2.c)
    /// <summary>Windowed mean of the squares of the numbers.</summary>
    /// <param name="wc">half width of the window</param>
    /// <returns>nad containing windowed mean square values</returns>
    /// <remarks>
    /// (1) The window has width = 2 * %wc + 1.
    /// (2) We add a mirrored border of size %wc to each end of the array.
    /// </remarks>
    public Numa WindowedMeanSquare(int wc)
    {
        int i, n, n1, width;
        float sum, norm;
        float[] fa1, fad, suma;
        Numa na1, nad;

        n = Count;
        width = 2 * wc + 1;  /* filter width */
        if (width > n)
            Log.Warning("numaWindowedMeanSquare", "filter wider than input array!");

        na1 = AddSpecifiedBorder(wc, wc, L.L_MIRRORED_BORDER);
        n1 = n + 2 * wc;
        fa1 = na1.GetFArray(L.L_NOCOPY);
        nad = MakeConstant(0, n);
        fad = nad.GetFArray(L.L_NOCOPY);

            /* Make sum array; note the indexing */
        suma = new float[n1 + 1];
        sum = 0.0f;
        suma[0] = 0.0f;
        for (i = 0; i < n1; i++) {
            sum += fa1[i] * fa1[i];
            suma[i + 1] = sum;
        }

        norm = 1.0f / (2 * wc + 1);
        for (i = 0; i < n; i++)
            fad[i] = norm * (suma[width + i] - suma[i]);

        return nad;
    }

    // leptonica: numaWindowedVariance (numafunc2.c)
    /// <summary>Windowed variance and rms deviation, from the windowed mean and mean square.</summary>
    /// <param name="nams">windowed mean square values (this numa holds the windowed mean values)</param>
    /// <param name="nav">numa of variance -- the ms deviation from the mean</param>
    /// <param name="narv">numa of rms deviation from the mean</param>
    /// <remarks>
    /// (1) The numas of windowed mean and mean square are precomputed,
    ///     using numaWindowedMean() and numaWindowedMeanSquare().
    /// (2) Either or both of the variance and square-root of variance
    ///     are returned, where the variance is the average over the
    ///     window of the mean square difference of the pixel value
    ///     from the mean:
    ///            [(x - [x])*(x - [x])] = [x*x] - [x]*[x]
    /// </remarks>
    public void WindowedVariance(Numa nams, out Numa nav, out Numa narv)
    {
        int i, nm, nms;
        float var;
        float[] fam, fams, fav, farv;

        nm = Count;
        nms = nams.Count;
        if (nm != nms)
            throw Lept.Arg("numaWindowedVariance", "sizes of nam and nams differ");

        nav = MakeConstant(0, nm);
        fav = nav.GetFArray(L.L_NOCOPY);
        narv = MakeConstant(0, nm);
        farv = narv.GetFArray(L.L_NOCOPY);
        fam = GetFArray(L.L_NOCOPY);
        fams = nams.GetFArray(L.L_NOCOPY);

        for (i = 0; i < nm; i++) {
            var = fams[i] - fam[i] * fam[i];
            fav[i] = var;
            farv[i] = MathF.Sqrt(var);
        }
    }

    // leptonica: numaWindowedMedian (numafunc2.c)
    /// <summary>Median filter with a window of width 2 * halfwin + 1.</summary>
    /// <param name="halfwin">half width of window over which the median is found</param>
    /// <returns>nad after windowed median filtering</returns>
    /// <remarks>
    /// (1) The requested window has width = 2 * %halfwin + 1.
    /// (2) If the input nas has less then 3 elements, return a copy.
    /// (3) If the filter is too small (%halfwin &lt;= 0), return a copy.
    /// (4) If the filter is too large, it is reduced in size.
    /// (5) We add a mirrored border of size %halfwin to each end of
    ///     the array to simplify the calculation by avoiding end-effects.
    /// </remarks>
    public Numa WindowedMedian(int halfwin)
    {
        const string func = "numaWindowedMedian";
        int i, n;
        float medval;
        Numa na1, na2, nad;

        if ((n = Count) < 3)
            return Copy();
        if (halfwin <= 0) {
            Log.Error(func, "filter too small; returning a copy");
            return Copy();
        }

        if (halfwin > (n - 1) / 2) {
            halfwin = (n - 1) / 2;
            Log.Info(func, CFormat.Sprintf("reducing filter to halfwin = %d", halfwin));
        }

            /* Add a border to both ends */
        na1 = AddSpecifiedBorder(halfwin, halfwin, L.L_MIRRORED_BORDER);

            /* Get the median value at the center of each window, corresponding
             * to locations in the input nas. */
        nad = Create(n);
        for (i = 0; i < n; i++) {
            na2 = na1.ClipToInterval(i, i + 2 * halfwin);
            medval = na2.GetMedian();
            nad.AddNumber(medval);
        }

        return nad;
    }

    // leptonica: numaConvertToInt (numafunc2.c)
    /// <summary>Rounds all values to the nearest integer.</summary>
    /// <returns>na with all values rounded to nearest integer</returns>
    public Numa ConvertToInt()
    {
        int i, n, ival;
        Numa nad;

        n = Count;
        nad = Create(n);
        nad.CopyParameters(this);
        for (i = 0; i < n; i++) {
            ival = GetIValue(i);
            nad.AddNumber(ival);
        }
        return nad;
    }

    /*----------------------------------------------------------------------*
     *                 Histogram generation and statistics                  *
     *----------------------------------------------------------------------*/
    // leptonica: numaMakeHistogram (numafunc2.c)
    /// <summary>Histogram of integerized values; the bins start at the minimum value.</summary>
    /// <param name="maxbins">max number of histogram bins</param>
    /// <param name="binsize">size of histogram bins</param>
    /// <param name="binstart">start val of minimum bin</param>
    /// <returns>na consisiting of histogram of integerized values</returns>
    /// <remarks>
    /// (1) This simple interface is designed for integer data.
    ///     The bins are of integer width and start on integer boundaries,
    ///     so the results on float data will not have high precision.
    /// (2) Specify the max number of input bins.   Then %binsize,
    ///     the size of bins necessary to accommodate the input data,
    ///     is returned.  It is one of the sequence:
    ///                {1, 2, 5, 10, 20, 50, ...}.
    /// (3) All values are accommodated, and the min value of the
    ///     starting bin is returned in <c>binstart</c>.  The overload without
    ///     <c>binstart</c> discards all negative values and starts the
    ///     histogram bins at 0.
    /// </remarks>
    public Numa MakeHistogram(int maxbins, out int binsize, out int binstart)
    {
        return MakeHistogramCore(maxbins, true, out binsize, out binstart);
    }

    /// <summary>Histogram of integerized values; negative values are discarded and the bins start at 0.</summary>
    /// <param name="maxbins">max number of histogram bins</param>
    /// <param name="binsize">size of histogram bins</param>
    /// <returns>na consisiting of histogram of integerized values</returns>
    /// <remarks>See the other overload, which also returns the start of the first bin.</remarks>
    public Numa MakeHistogram(int maxbins, out int binsize)
    {
        return MakeHistogramCore(maxbins, false, out binsize, out _);
    }

    private Numa MakeHistogramCore(int maxbins, bool withstart, out int binsize, out int binstart)
    {
        const string func = "numaMakeHistogram";
        int i, n, ival, hval;
        int iminval, imaxval, range, nbins, ibin;
        float val, ratio;
        Numa nai, nahist;

        binsize = 0;
        binstart = 0;
        if (maxbins < 1)
            throw Lept.Arg(func, "maxbins < 1");

            /* Determine input range */
        /* NOTE(upstream): (int)(val + 0.5) truncates toward zero for negative values */
        GetMin(out val, out _);
        iminval = (int)(val + 0.5);
        GetMax(out val, out _);
        imaxval = (int)(val + 0.5);
        if (!withstart) {  /* clip negative vals; start from 0 */
            iminval = 0;
            if (imaxval < 0)
                throw Lept.Arg(func, "all values < 0");
        }

            /* Determine binsize */
        range = imaxval - iminval + 1;
        if (range > maxbins - 1) {
            ratio = (float)range / (float)maxbins;
            binsize = 0;
            for (i = 0; i < NBinSizes; i++) {
                if (ratio < BinSizeArray[i]) {
                    binsize = BinSizeArray[i];
                    break;
                }
            }
            if (binsize == 0)
                throw Lept.Arg(func, "numbers too large");
        } else {
            binsize = 1;
        }
        nbins = 1 + range / binsize;  /* +1 seems to be sufficient */

            /* Redetermine iminval */
        if (withstart && binsize > 1) {
            if (iminval >= 0)
                iminval = binsize * (iminval / binsize);
            else
                iminval = binsize * ((iminval - binsize + 1) / binsize);
        }
        if (withstart) binstart = iminval;

            /* Use integerized data for input */
        nai = ConvertToInt();
        n = nai.Count;

            /* Make histogram, converting value in input array
             * into a bin number for this histogram array. */
        nahist = Create(nbins);
        nahist.Count = nbins;
        nahist.SetParameters(iminval, binsize);
        for (i = 0; i < n; i++) {
            ival = nai.GetIValue(i);
            ibin = (ival - iminval) / binsize;
            if (ibin >= 0 && ibin < nbins) {
                hval = nahist.GetIValue(ibin);
                nahist.SetValue(ibin, hval + 1.0f);
            }
        }

        return nahist;
    }

    // leptonica: numaMakeHistogramAuto (numafunc2.c)
    /// <summary>Histogram of the values, binned exactly into at most maxbins bins.</summary>
    /// <param name="maxbins">max number of histogram bins; >= 1</param>
    /// <returns>na consisiting of histogram of quantized float values</returns>
    /// <remarks>
    /// (1) This simple interface is designed for accurate binning
    ///     of both integer and float data.
    /// (2) If the array data is integers, and the range of integers
    ///     is smaller than %maxbins, they are binned as they fall,
    ///     with binsize = 1.
    /// (3) If the range of data, (maxval - minval), is larger than
    ///     %maxbins, or if the data is floats, they are binned into
    ///     exactly %maxbins bins.
    /// (4) Unlike numaMakeHistogram(), these bins in general have
    ///     non-integer location and width, even for integer data.
    /// </remarks>
    public Numa MakeHistogramAuto(int maxbins)
    {
        int i, n, imin, imax, irange, ibin, ival;
        bool allints;
        float minval, maxval, range, binsize, fval;
        Numa nah;

        maxbins = Math.Max(1, maxbins);

            /* Determine input range */
        GetMin(out minval, out _);
        GetMax(out maxval, out _);

            /* Determine if values are all integers */
        n = Count;
        allints = HasOnlyIntegers();

            /* Do simple integer binning if possible */
        if (allints && (maxval - minval < maxbins)) {
            imin = (int)minval;
            imax = (int)maxval;
            irange = imax - imin + 1;
            nah = Create(irange);
            nah.Count = irange;  /* init */
            nah.SetParameters(minval, 1.0f);
            for (i = 0; i < n; i++) {
                ival = GetIValue(i);
                ibin = ival - imin;
                ival = nah.GetIValue(ibin);
                nah.SetValue(ibin, ival + 1.0f);
            }

            return nah;
        }

            /* Do float binning, even if the data is integers. */
        range = maxval - minval;
        binsize = range / (float)maxbins;
        if (range == 0.0) {
            nah = Create(1);
            nah.SetParameters(minval, binsize);
            nah.AddNumber(n);
            return nah;
        }
        nah = Create(maxbins);
        nah.Count = maxbins;
        nah.SetParameters(minval, binsize);
        for (i = 0; i < n; i++) {
            fval = GetFValue(i);
            ibin = (int)((fval - minval) / binsize);
            ibin = Math.Min(ibin, maxbins - 1);  /* "edge" case; stay in bounds */
            ival = nah.GetIValue(ibin);
            nah.SetValue(ibin, ival + 1.0f);
        }

        return nah;
    }

    // leptonica: numaMakeHistogramClipped (numafunc2.c)
    /// <summary>Histogram of the values in [0, min(maxsize, maxval)], in bins of size binsize.</summary>
    /// <param name="binsize">typically 1.0</param>
    /// <param name="maxsize">of histogram ordinate</param>
    /// <returns>na histogram of bins of size %binsize, starting with
    /// the na[0] (x = 0.0 and going up to a maximum of
    /// x = %maxsize, by increments of %binsize)</returns>
    /// <remarks>
    /// (1) This simple function generates a histogram of values
    ///     from na, discarding all values &lt; 0.0 or greater than
    ///     min(%maxsize, maxval), where maxval is the maximum value in na.
    ///     The histogram data is put in bins of size delx = %binsize,
    ///     starting at x = 0.0.  We use as many bins as are
    ///     needed to hold the data.
    /// </remarks>
    public Numa MakeHistogramClipped(float binsize, float maxsize)
    {
        int i, n, nbins, ival, ibin;
        float val, maxval;
        Numa nad;

        if (binsize <= 0.0)
            throw Lept.Arg("numaMakeHistogramClipped", "binsize must be > 0.0");
        if (binsize > maxsize)
            binsize = maxsize;  /* just one bin */

        GetMax(out maxval, out _);
        n = Count;
        maxsize = (maxsize < maxval) ? maxsize : maxval;  /* L_MIN */
        nbins = (int)(maxsize / binsize) + 1;

        nad = Create(nbins);
        nad.SetParameters(0.0f, binsize);
        nad.Count = nbins;  /* interpret zeroes in bins as data */
        for (i = 0; i < n; i++) {
            val = GetFValue(i);
            ibin = (int)(val / binsize);
            if (ibin >= 0 && ibin < nbins) {
                ival = nad.GetIValue(ibin);
                nad.SetValue(ibin, ival + 1.0f);
            }
        }

        return nad;
    }

    // leptonica: numaRebinHistogram (numafunc2.c)
    /// <summary>Makes a more coarsely binned histogram.</summary>
    /// <param name="newsize">number of old bins contained in each new bin</param>
    /// <returns>nad more coarsely re-binned histogram</returns>
    public Numa RebinHistogram(int newsize)
    {
        const string func = "numaRebinHistogram";
        int i, j, ns, nd, index, count, val;
        float start, oldsize;
        Numa nad;

        if (newsize <= 1)
            throw Lept.Arg(func, "newsize must be > 1");
        if ((ns = Count) == 0)
            throw Lept.Arg(func, "no bins in nas");

        nd = (ns + newsize - 1) / newsize;
        nad = Create(nd);
        /* NOTE(upstream): the parameters are read from nad (startx = 0.0,
         * delx = 1.0), not from nas; the source parameters are ignored. */
        nad.GetParameters(out start, out oldsize);
        nad.SetParameters(start, oldsize * newsize);

        for (i = 0; i < nd; i++) {  /* new bins */
            count = 0;
            index = i * newsize;
            for (j = 0; j < newsize; j++) {
                if (index < ns) {
                    val = GetIValue(index);
                    count += val;
                    index++;
                }
            }
            nad.AddNumber(count);
        }

        return nad;
    }

    // leptonica: numaNormalizeHistogram (numafunc2.c)
    /// <summary>Scales the histogram to the target sum.</summary>
    /// <param name="tsum">target sum of all numbers in dest histogram; e.g., use
    /// %tsum= 1.0 if this represents a probability distribution</param>
    /// <returns>nad normalized histogram</returns>
    public Numa NormalizeHistogram(float tsum)
    {
        const string func = "numaNormalizeHistogram";
        int i, ns;
        float sum, factor, fval;
        Numa nad;

        if (tsum <= 0.0)
            throw Lept.Arg(func, "tsum must be > 0.0");
        if ((ns = Count) == 0)
            throw Lept.Arg(func, "no bins in nas");

        sum = GetSum();
        factor = tsum / sum;

        nad = Create(ns);
        nad.CopyParameters(this);

        for (i = 0; i < ns; i++) {
            fval = GetFValue(i);
            fval *= factor;
            nad.AddNumber(fval);
        }

        return nad;
    }

    // leptonica: numaGetStatsUsingHistogram (numafunc2.c)
    /// <summary>Min, max, mean and variance of the numbers, without a histogram.</summary>
    /// <param name="min">min value of set</param>
    /// <param name="max">max value of set</param>
    /// <param name="mean">mean value of set</param>
    /// <param name="variance">variance</param>
    /// <remarks>This is numaGetStatsUsingHistogram() when no median, rank value
    /// or histogram is requested: the calculation is all performed on the
    /// input Numa.  See the other overload for the Notes.</remarks>
    public void GetStatsUsingHistogram(out float min, out float max, out float mean, out float variance)
    {
        int i, n;
        float minval, maxval, fval, sum;

        min = 0.0f;
        max = 0.0f;
        mean = 0.0f;
        variance = 0.0f;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetStatsUsingHistogram", "numa is empty");

        GetMin(out minval, out _);
        GetMax(out maxval, out _);
        min = minval;
        max = maxval;
        sum = 0.0f;
        for (i = 0; i < n; i++) {
            fval = GetFValue(i);
            sum += fval;
        }
        mean = sum / (float)n;
        sum = 0.0f;
        for (i = 0; i < n; i++) {
            fval = GetFValue(i);
            sum += fval * fval;
        }
        variance = sum / (float)n - mean * mean;
    }

    /// <summary>Statistics of the numbers, with the median and a rank value found from a histogram.</summary>
    /// <param name="maxbins">the maximum number of bins to be allowed in
    /// the histogram; use an integer larger than the
    /// largest number in %na for consecutive integer bins</param>
    /// <param name="min">min value of set</param>
    /// <param name="max">max value of set</param>
    /// <param name="mean">mean value of set</param>
    /// <param name="variance">variance</param>
    /// <param name="median">median value of set</param>
    /// <param name="rank">in [0.0 ... 1.0]; median has a rank 0.5</param>
    /// <param name="rval">value in na corresponding to %rank</param>
    /// <param name="histo">Numa histogram</param>
    /// <remarks>
    /// (1) This is a simple interface for gathering statistics
    ///     from a numa, where a histogram is used 'under the covers'
    ///     to avoid sorting if a rank value is requested.  In that case,
    ///     by using a histogram we are trading speed for accuracy, because
    ///     the values in %na are quantized to the center of a set of bins.
    /// (2) If the median, other rank value, or histogram are not requested,
    ///     the calculation is all performed on the input Numa (see the
    ///     overload without them).
    /// (3) The variance is the average of the square of the
    ///     difference from the mean.  The median is the value in na
    ///     with rank 0.5.
    /// (4) There are two situations where this gives rank results with
    ///     accuracy comparable to computing stastics directly on the input
    ///     data, without binning into a histogram:
    ///      (a) the data is integers and the range of data is less than
    ///          %maxbins, and
    ///      (b) the data is floats and the range is small compared to
    ///          %maxbins, so that the binsize is much less than 1.
    /// (5) If a histogram is used and the numbers in the Numa extend
    ///     over a large range, you can limit the required storage by
    ///     specifying the maximum number of bins in the histogram.
    ///     Use %maxbins == 0 to force the bin size to be 1.
    /// (6) This optionally returns the median and one arbitrary rank value.
    ///     If you need several rank values, return the histogram and use
    ///          numaHistogramGetValFromRank(nah, rank, &amp;rval)
    ///     multiple times.
    /// </remarks>
    public void GetStatsUsingHistogram(int maxbins, out float min, out float max, out float mean,
                                       out float variance, out float median, float rank,
                                       out float rval, out Numa histo)
    {
        GetStatsUsingHistogram(out min, out max, out mean, out variance);

        histo = MakeHistogramAuto(maxbins);
        median = histo.HistogramGetValFromRank(0.5f);
        rval = histo.HistogramGetValFromRank(rank);
    }

    // leptonica: numaGetHistogramStats (numafunc2.c)
    /// <summary>Mean, median, mode and variance of x for the histogram y(x).</summary>
    /// <param name="startx">x value of first bin: x(0)</param>
    /// <param name="deltax">x increment between bins; the bin size; x(1) - x(0)</param>
    /// <param name="xmean">mean value of histogram</param>
    /// <param name="xmedian">median value of histogram</param>
    /// <param name="xmode">mode value of histogram:
    /// xmode = x(imode), where y(xmode) >= y(x(i)) for all i != imode</param>
    /// <param name="xvariance">variance of x</param>
    /// <remarks>
    /// (1) If the histogram represents the relation y(x), the
    ///     computed values that are returned are the x values.
    ///     These are NOT the bucket indices i; they are related to the
    ///     bucket indices by
    ///           x(i) = startx + i * deltax
    /// </remarks>
    public void GetHistogramStats(float startx, float deltax, out float xmean, out float xmedian,
                                  out float xmode, out float xvariance)
    {
        GetHistogramStatsOnInterval(startx, deltax, 0, -1,
                                    out xmean, out xmedian, out xmode, out xvariance);
    }

    // leptonica: numaGetHistogramStatsOnInterval (numafunc2.c)
    /// <summary>Mean, median, mode and variance of x for the histogram y(x) on the bins [ifirst, ilast].</summary>
    /// <param name="startx">x value of first bin: x(0)</param>
    /// <param name="deltax">x increment between bins; the bin size; x(1) - x(0)</param>
    /// <param name="ifirst">first bin to use for collecting stats</param>
    /// <param name="ilast">last bin for collecting stats; -1 to go to the end</param>
    /// <param name="xmean">mean value of histogram</param>
    /// <param name="xmedian">median value of histogram</param>
    /// <param name="xmode">mode value of histogram:
    /// xmode = x(imode), where y(xmode) >= y(x(i)) for all i != imode</param>
    /// <param name="xvariance">variance of x</param>
    /// <remarks>
    /// (1) If the histogram represents the relation y(x), the
    ///     computed values that are returned are the x values.
    ///     These are NOT the bucket indices i; they are related to the
    ///     bucket indices by
    ///           x(i) = startx + i * deltax
    /// </remarks>
    public void GetHistogramStatsOnInterval(float startx, float deltax, int ifirst, int ilast,
                                            out float xmean, out float xmedian, out float xmode,
                                            out float xvariance)
    {
        const string func = "numaGetHistogramStatsOnInterval";
        int i, n, imax;
        float sum, sumval, halfsum, moment, var, x, y, ymax;

        xmean = 0.0f;
        xmedian = 0.0f;
        xmode = 0.0f;
        xvariance = 0.0f;

        n = Count;
        ifirst = Math.Max(0, ifirst);
        if (ilast < 0) ilast = n - 1;
        if (ifirst >= n)
            throw Lept.Arg(func, "invalid ifirst");
        if (ilast >= n) {
            Log.Warning(func, CFormat.Sprintf("ilast = %d is beyond max index = %d; adjusting", ilast, n - 1));
            ilast = n - 1;
        }
        if (ifirst > ilast)
            throw Lept.Arg(func, "ifirst > ilast");
        for (sum = 0.0f, moment = 0.0f, var = 0.0f, i = ifirst; i <= ilast; i++) {
            x = startx + i * deltax;
            y = GetFValue(i);
            sum += y;
            moment += x * y;
            var += x * x * y;
        }
        if (sum == 0.0) {
            Log.Info(func, "sum is 0");
            return;
        }

        xmean = moment / sum;
        xvariance = var / sum - moment * moment / (sum * sum);

        halfsum = sum / 2.0f;
        for (sumval = 0.0f, i = ifirst; i <= ilast; i++) {
            y = GetFValue(i);
            sumval += y;
            if (sumval >= halfsum) {
                xmedian = startx + i * deltax;
                break;
            }
        }

        imax = -1;
        ymax = -1.0e10f;
        for (i = ifirst; i <= ilast; i++) {
            y = GetFValue(i);
            if (y > ymax) {
                ymax = y;
                imax = i;
            }
        }
        xmode = startx + imax * deltax;
    }

    // leptonica: numaMakeRankFromHistogram (numafunc2.c)
    /// <summary>Makes the rank function (cumulative normalized histogram) of a histogram.</summary>
    /// <param name="startx">xval corresponding to first element in nay</param>
    /// <param name="deltax">x increment between array elements in nay</param>
    /// <param name="nasy">input histogram, assumed equally spaced</param>
    /// <param name="npts">number of points to evaluate rank function</param>
    /// <param name="nax">array of x values in range</param>
    /// <param name="nay">rank array of specified npts</param>
    public static void MakeRankFromHistogram(float startx, float deltax, Numa nasy, int npts,
                                             out Numa? nax, out Numa nay)
    {
        int i, n;
        float sum, fval;
        Numa nan, nar;

        if ((n = nasy.Count) == 0)
            throw Lept.Arg("numaMakeRankFromHistogram", "no bins in nas");

            /* Normalize and generate the rank array corresponding to
             * the binned histogram. */
        nan = nasy.NormalizeHistogram(1.0f);
        nar = Create(n + 1);  /* rank numa corresponding to nan */
        sum = 0.0f;
        nar.AddNumber(sum);  /* first element is 0.0 */
        for (i = 0; i < n; i++) {
            fval = nan.GetFValue(i);
            sum += fval;
            nar.AddNumber(sum);
        }

            /* Compute rank array on full range with specified
             * number of points and correspondence to x-values. */
        InterpolateEqxInterval(startx, deltax, nar, L.L_LINEAR_INTERP,
                               startx, startx + n * deltax, npts,
                               out nax, out nay);
    }

    // leptonica: numaHistogramGetRankFromVal (numafunc2.c)
    /// <summary>Rank of a value for this histogram.</summary>
    /// <param name="rval">value of input sample for which we want the rank</param>
    /// <returns>fraction of total samples below rval</returns>
    /// <remarks>
    /// (1) If we think of the histogram as a function y(x), normalized
    ///     to 1, for a given input value of x, this computes the
    ///     rank of x, which is the integral of y(x) from the start
    ///     value of x to the input value.
    /// (2) This function only makes sense when applied to a Numa that
    ///     is a histogram.  The values in the histogram can be ints and
    ///     floats, and are computed as floats.  The rank is returned
    ///     as a float between 0.0 and 1.0.
    /// (3) The numa parameters startx and binsize are used to
    ///     compute x from the Numa index i.
    /// </remarks>
    public float HistogramGetRankFromVal(float rval)
    {
        int i, ibinval, n;
        float startval, binsize, binval, maxval, fractval, total, sum, val;

        GetParameters(out startval, out binsize);
        n = Count;
        if (rval < startval)
            return 0.0f;
        maxval = startval + n * binsize;
        if (rval > maxval)
            return 1.0f;

        binval = (rval - startval) / binsize;
        ibinval = (int)binval;
        if (ibinval >= n)
            return 1.0f;
        fractval = binval - (float)ibinval;

        sum = 0.0f;
        for (i = 0; i < ibinval; i++) {
            val = GetFValue(i);
            sum += val;
        }
        val = GetFValue(ibinval);
        sum += fractval * val;
        total = GetSum();
        return sum / total;
    }

    // leptonica: numaHistogramGetValFromRank (numafunc2.c)
    /// <summary>Value (approximate bin value) for a rank of this histogram.</summary>
    /// <param name="rank">fraction of total samples</param>
    /// <returns>approx. to the bin value</returns>
    /// <remarks>
    /// (1) If we think of the histogram as a function y(x), this returns
    ///     the value x such that the integral of y(x) from the start
    ///     value to x gives the fraction 'rank' of the integral
    ///     of y(x) over all bins.
    /// (2) This function only makes sense when applied to a Numa that
    ///     is a histogram.  The values in the histogram can be ints and
    ///     floats, and are computed as floats.  The val is returned
    ///     as a float, even though the buckets are of integer width.
    /// (3) The numa parameters startx and binsize are used to
    ///     compute x from the Numa index i.
    /// </remarks>
    public float HistogramGetValFromRank(float rank)
    {
        const string func = "numaHistogramGetValFromRank";
        int i, n;
        float startval, binsize, rankcount, total, sum, fract, val;

        if (rank < 0.0) {
            Log.Warning(func, "rank < 0; setting to 0.0");
            rank = 0.0f;
        }
        if (rank > 1.0) {
            Log.Warning(func, "rank > 1.0; setting to 1.0");
            rank = 1.0f;
        }

        n = Count;
        GetParameters(out startval, out binsize);
        total = GetSum();
        rankcount = rank * total;  /* count that corresponds to rank */
        sum = 0.0f;
        val = 0.0f;
        for (i = 0; i < n; i++) {
            val = GetFValue(i);
            if (sum + val >= rankcount)
                break;
            sum += val;
        }
        if (val <= 0.0)  /* can == 0 if rank == 0.0 */
            fract = 0.0f;
        else  /* sum + fract * val = rankcount */
            fract = (rankcount - sum) / val;

            /* The use of the fraction of a bin allows a simple calculation
             * for the histogram value at the given rank. */
        return startval + binsize * ((float)i + fract);
    }

    // leptonica: numaDiscretizeSortedInBins (numafunc2.c)
    /// <summary>Average values in nbins equal-population bins of a sorted array.</summary>
    /// <param name="nbins">number of equal population bins (> 1)</param>
    /// <returns>nabinval: average "gray" values in each bin</returns>
    /// <remarks>
    /// (1) The input %na is sorted in increasing value.
    /// (2) The output array has the following mapping:
    ///        bin number  -->  average array value in bin (nabinval)
    /// (3) With %nbins == 100, nabinval is the average gray value in
    ///     each of the 100 equally populated bins.  It is the function
    ///           gray[100 * rank].
    ///     Thus it is the inverse of
    ///           rank[gray]
    /// (4) Contast with numaDiscretizeHistoInBins(), where the input %na
    ///     is a histogram.
    /// </remarks>
    public Numa DiscretizeSortedInBins(int nbins)
    {
        Numa nabinval;  /* average gray value in the bins */
        Numa naeach;
        int i, ntot, bincount, binindex, binsize;
        float sum, val, ave;

        if (nbins < 2)
            throw Lept.Arg("numaDiscretizeSortedInBins", "nbins must be > 1");

            /* Get the number of items in each bin */
        ntot = Count;
        naeach = GetUniformBinSizes(ntot, nbins);

            /* Get the average value in each bin */
        sum = 0.0f;
        bincount = 0;
        binindex = 0;
        binsize = naeach.GetIValue(0);
        nabinval = Create(nbins);
        for (i = 0; i < ntot; i++) {
            val = GetFValue(i);
            bincount++;
            sum += val;
            if (bincount == binsize) {  /* add bin entry */
                ave = sum / binsize;
                nabinval.AddNumber(ave);
                sum = 0.0f;
                bincount = 0;
                binindex++;
                if (binindex == nbins) break;
                binsize = naeach.GetIValue(binindex);
            }
        }

        return nabinval;
    }

    // leptonica: numaDiscretizeHistoInBins (numafunc2.c)
    /// <summary>Average values in nbins equal-population bins of a histogram.</summary>
    /// <param name="nbins">number of equal population bins (> 1)</param>
    /// <param name="nabinval">average "gray" values in each bin</param>
    /// <param name="narank">rank value of input histogram; this is a cumulative norm histogram</param>
    /// <remarks>
    /// (1) With %nbins == 100, nabinval is the average gray value in
    ///     each of the 100 equally populated bins.  It is the function
    ///           gray[100 * rank].
    ///     Thus it is the inverse of
    ///           rank[gray]
    ///     which is optionally returned in narank.
    /// (2) The "gray value" is the index into the input histogram.
    /// (3) The two output arrays give the following mappings, where the
    ///     input is an un-normalized histogram of array values:
    ///        bin number  -->  average array value in bin (nabinval)
    ///        array values     -->  cumulative normalized histogram (narank)
    /// </remarks>
    public void DiscretizeHistoInBins(int nbins, out Numa nabinval, out Numa narank)
    {
        const string func = "numaDiscretizeHistoInBins";
        Numa naeach, nan;
        int i, j, nxvals, occup, count, bincount, binindex, binsize;
        float sum, ave, ntot;

        if (nbins < 2)
            throw Lept.Arg(func, "nbins must be > 1");

        nxvals = Count;
        ntot = GetSum();
        occup = (int)(ntot / nxvals);
        if (occup < 1) Log.Info(func, CFormat.Sprintf("average occupancy %d < 1", occup));

            /* Get the number of items in each bin */
        naeach = GetUniformBinSizes((int)ntot, nbins);

            /* Get the average value in each bin */
        sum = 0.0f;
        bincount = 0;
        binindex = 0;
        binsize = naeach.GetIValue(0);
        nabinval = Create(nbins);
        for (i = 0; i < nxvals; i++) {
            count = GetIValue(i);
            for (j = 0; j < count; j++) {
                bincount++;
                sum += i;
                if (bincount == binsize) {  /* add bin entry */
                    ave = sum / binsize;
                    nabinval.AddNumber(ave);
                    sum = 0.0f;
                    bincount = 0;
                    binindex++;
                    if (binindex == nbins) break;
                    binsize = naeach.GetIValue(binindex);
                }
            }
            if (binindex == nbins) break;
        }
        if (binindex != nbins)
            Log.Error(func, CFormat.Sprintf("binindex = %d != nbins = %d", binindex, nbins));

            /* Get cumulative normalized histogram (rank[gray value]).
             * This is the partial sum operating on the normalized histogram. */
        nan = NormalizeHistogram(1.0f);
        narank = nan.GetPartialSums();
    }

    // leptonica: numaGetRankBinValues (numafunc2.c)
    /// <summary>Mean values in nbins rank bins of the array.</summary>
    /// <param name="nbins">number of bins at which the rank is divided</param>
    /// <returns>nam: mean intensity in a bin vs rank bin value,
    /// with %nbins of discretized rank values</returns>
    /// <remarks>
    /// (1) Simple interface for getting a binned rank representation
    ///     of an input array of values.  This returns:
    ///        rank bin number -->  average array value in each rank bin (nam)
    /// (2) Uses bins either a sorted array or a histogram, depending on
    ///     the values in the array and the size of the array.
    /// </remarks>
    public Numa GetRankBinValues(int nbins)
    {
        const string func = "numaGetRankBinValues";
        Numa na1, nam;
        int maxbins, type;
        float maxval, delx;

        if (Count == 0)
            throw Lept.Arg(func, "na is empty");
        if (nbins < 2)
            throw Lept.Arg(func, "nbins must be > 1");

            /* Choose between sorted array and a histogram.
             * If the input array is has a small number of numbers with
             * a large maximum, we will sort it.  At the other extreme, if
             * the array has many numbers with a small maximum, such as the
             * values of pixels in an 8 bpp grayscale image, generate a histogram.
             * If type comes back as L_BIN_SORT, use a histogram. */
        type = ChooseSortType();
        if (type == L.L_SHELL_SORT) {  /* sort the array */
            Log.Info(func, CFormat.Sprintf("sort the array: input size = %d", Count));
            na1 = Sort(null, this, L.L_SORT_INCREASING);
            return na1.DiscretizeSortedInBins(nbins);
        }

            /* Make the histogram.  Assuming there are no negative values
             * in the array, if the max value in the array does not exceed
             * about 100000, the bin size for generating the histogram will
             * be 1; maxbins refers to the number of entries in the histogram. */
        Log.Info(func, CFormat.Sprintf("use a histogram: input size = %d", Count));
        GetMax(out maxval, out _);
        maxbins = Math.Min(100002, (int)maxval + 2);
        na1 = MakeHistogram(maxbins, out _);

            /* Warn if there is a scale change.  This shouldn't happen
             * unless the max value is above 100000.  */
        na1.GetParameters(out _, out delx);
        if (delx > 1.0)
            Log.Warning(func, CFormat.Sprintf("scale change: delx = %6.2f", delx));

            /* Rank bin the results */
        na1.DiscretizeHistoInBins(nbins, out nam, out _);
        return nam;
    }

    // leptonica: numaGetUniformBinSizes (numafunc2.c)
    /// <summary>Splits ntotal values into nbins bins whose sizes differ by at most 1.</summary>
    /// <param name="ntotal">number of values to be split up</param>
    /// <param name="nbins">number of bins</param>
    /// <returns>naeach: number of values to go in each bin</returns>
    /// <remarks>
    /// (1) The numbers in the bins can differ by 1.  The sum of
    ///     bin numbers in %naeach is %ntotal.
    /// </remarks>
    public static Numa GetUniformBinSizes(int ntotal, int nbins)
    {
        const string func = "numaGetUniformBinSizes";
        int i, start, end;
        Numa naeach;

        if (ntotal <= 0)
            throw Lept.Arg(func, "ntotal <= 0");
        if (nbins <= 0)
            throw Lept.Arg(func, "nbins <= 0");

        naeach = Create(nbins);

        if (ntotal < nbins) {  /* put 1 in each of %ntotal bins */
            for (i = 0; i < ntotal; i++)
                naeach.AddNumber(1);
            return naeach;
        }

        start = 0;
        for (i = 0; i < nbins; i++) {
            end = ntotal * (i + 1) / nbins;
            naeach.AddNumber(end - start);
            start = end;
        }
        return naeach;
    }

    /*----------------------------------------------------------------------*
     *                      Splitting a distribution                        *
     *----------------------------------------------------------------------*/
    // leptonica: numaSplitDistribution (numafunc2.c)
    /// <summary>Splits a histogram into two distributions (Otsu method with a histogram-minimum refinement).</summary>
    /// <param name="scorefract">fraction of the max score, used to determine
    /// range over which the histogram min is searched</param>
    /// <param name="splitindex">index for splitting</param>
    /// <param name="ave1">average of lower distribution</param>
    /// <param name="ave2">average of upper distribution</param>
    /// <param name="num1">population of lower distribution</param>
    /// <param name="num2">population of upper distribution</param>
    /// <remarks>
    /// (1) This function is intended to be used on a distribution of
    ///     values that represent two sets, such as a histogram of
    ///     pixel values for an image with a fg and bg, and the goal
    ///     is to determine the averages of the two sets and the
    ///     best splitting point.
    /// (2) The Otsu method finds a split point that divides the distribution
    ///     into two parts by maximizing a score function that is the
    ///     product of two terms:
    ///       (a) the square of the difference of centroids, (ave1 - ave2)^2
    ///       (b) fract1 * (1 - fract1)
    ///     where fract1 is the fraction in the lower distribution.
    /// (3) This works well for images where the fg and bg are
    ///     each relatively homogeneous and well-separated in color.
    ///     However, if the actual fg and bg sets are very different
    ///     in size, and the bg is highly varied, as can occur in some
    ///     scanned document images, this will bias the split point
    ///     into the larger "bump" (i.e., toward the point where the
    ///     (b) term reaches its maximum of 0.25 at fract1 = 0.5.
    ///     To avoid this, we define a range of values near the
    ///     maximum of the score function, and choose the value within
    ///     this range such that the histogram itself has a minimum value.
    ///     The range is determined by scorefract: we include all abscissa
    ///     values to the left and right of the value that maximizes the
    ///     score, such that the score stays above (1 - scorefract) * maxscore.
    ///     The intuition behind this modification is to try to find
    ///     a split point that both has a high variance score and is
    ///     at or near a minimum in the histogram, so that the histogram
    ///     slope is small at the split point.
    /// (4) We normalize the score so that if the two distributions
    ///     were of equal size and at opposite ends of the numa, the
    ///     score would be 1.0.
    /// </remarks>
    public void SplitDistribution(float scorefract, out int splitindex, out float ave1, out float ave2,
                                  out float num1, out float num2)
    {
        SplitDistributionCore(scorefract, false, out splitindex, out ave1, out ave2, out num1, out num2, out _);
    }

    /// <summary>Splits a histogram into two distributions, as in the other overload, and also
    /// returns the score of each split. In this debug mode it prints the minimum and maximum of
    /// the search range and writes a plot of the score with gnuplot.</summary>
    /// <param name="scorefract">Fraction of the maximum score, used to determine the range over
    /// which the histogram minimum is searched.</param>
    /// <param name="splitindex">Receives the index for splitting.</param>
    /// <param name="ave1">Receives the average of the lower distribution.</param>
    /// <param name="ave2">Receives the average of the upper distribution.</param>
    /// <param name="num1">Receives the population of the lower distribution.</param>
    /// <param name="num2">Receives the population of the upper distribution.</param>
    /// <param name="nascore">Receives the score for each split, for debugging.</param>
    public void SplitDistribution(float scorefract, out int splitindex, out float ave1, out float ave2,
                                  out float num1, out float num2, out Numa nascore)
    {
        SplitDistributionCore(scorefract, true, out splitindex, out ave1, out ave2, out num1, out num2, out nascore);
    }

    private void SplitDistributionCore(float scorefract, bool debug, out int splitindex, out float rave1,
                                       out float rave2, out float rnum1, out float rnum2, out Numa nascoreout)
    {
        int i, n, bestsplit, minrange, maxrange, maxindex;
        float ave1, ave2, ave1prev, ave2prev;
        float num1, num2, num1prev, num2prev;
        float val, minval, sum, fract1;
        float norm, score, minscore, maxscore;
        Numa nascore, naave1, naave2, nanum1, nanum2;

        splitindex = 0;
        rave1 = 0.0f;
        rave2 = 0.0f;
        rnum1 = 0.0f;
        rnum2 = 0.0f;
        nascoreout = null!;

        n = Count;
        if (n <= 1)
            throw Lept.Arg("numaSplitDistribution", "n = 1 in histogram");
        sum = GetSum();
        if (sum <= 0.0)
            throw Lept.Arg("numaSplitDistribution", "sum <= 0.0");
        norm = 4.0f / ((float)(n - 1) * (n - 1));
        ave1prev = 0.0f;
        GetHistogramStats(0.0f, 1.0f, out ave2prev, out _, out _, out _);
        num1prev = 0.0f;
        num2prev = sum;
        maxindex = n / 2;  /* initialize with something */

            /* Split the histogram with [0 ... i] in the lower part
             * and [i+1 ... n-1] in upper part.  First, compute an otsu
             * score for each possible splitting.  */
        nascore = Create(n);
        naave1 = Create(n);
        naave2 = Create(n);
        nanum1 = Create(n);
        nanum2 = Create(n);
        maxscore = 0.0f;
        for (i = 0; i < n; i++) {
            val = GetFValue(i);
            num1 = num1prev + val;
            if (num1 == 0)
                ave1 = ave1prev;
            else
                ave1 = (num1prev * ave1prev + i * val) / num1;
            num2 = num2prev - val;
            if (num2 == 0)
                ave2 = ave2prev;
            else
                ave2 = (num2prev * ave2prev - i * val) / num2;
            fract1 = num1 / sum;
            score = norm * (fract1 * (1 - fract1)) * (ave2 - ave1) * (ave2 - ave1);
            nascore.AddNumber(score);
            naave1.AddNumber(ave1);
            naave2.AddNumber(ave2);
            nanum1.AddNumber(num1);
            nanum2.AddNumber(num2);
            if (score > maxscore) {
                maxscore = score;
                maxindex = i;
            }
            num1prev = num1;
            num2prev = num2;
            ave1prev = ave1;
            ave2prev = ave2;
        }

            /* Next, for all contiguous scores within a specified fraction
             * of the max, choose the split point as the value with the
             * minimum in the histogram. */
        minscore = (1.0f - scorefract) * maxscore;
        for (i = maxindex - 1; i >= 0; i--) {
            val = nascore.GetFValue(i);
            if (val < minscore)
                break;
        }
        minrange = i + 1;
        for (i = maxindex + 1; i < n; i++) {
            val = nascore.GetFValue(i);
            if (val < minscore)
                break;
        }
        maxrange = i - 1;
        minval = GetFValue(minrange);
        bestsplit = minrange;
        for (i = minrange + 1; i <= maxrange; i++) {
            val = GetFValue(i);
            if (val < minval) {
                minval = val;
                bestsplit = i;
            }
        }

            /* Add one to the bestsplit value to get the threshold value,
             * because when we take a threshold, as in pixThresholdToBinary(),
             * we always choose the set with values below the threshold. */
        bestsplit = Math.Min(255, bestsplit + 1);

            /* NOTE(upstream): bestsplit can be n for a histogram with fewer than
             * 256 bins; C then ignores the failure of numaGetFValue() and returns 0. */
        splitindex = bestsplit;
        rave1 = GetFValueOrZero(naave1, bestsplit);
        rave2 = GetFValueOrZero(naave2, bestsplit);
        rnum1 = GetFValueOrZero(nanum1, bestsplit);
        rnum2 = GetFValueOrZero(nanum2, bestsplit);

        if (debug) {
            CStreamText.Stderr("minrange = %d, maxrange = %d\n", minrange, maxrange);
            CStreamText.Stderr("minval = %10.0f\n", minval);
            Gplot.Simple1(nascore, L.GPLOT_PNG, "/tmp/lept/nascore",
                          "Score for split distribution");
            nascoreout = nascore;
        }
    }

    /*----------------------------------------------------------------------*
     *                         Comparing histograms                         *
     *----------------------------------------------------------------------*/
    // leptonica: grayHistogramsToEMD (numafunc2.c)
    /// <summary>Earth mover distances between corresponding 256-element histograms of two numaa.</summary>
    /// <param name="naa1">numaa with one or more 256-element histograms</param>
    /// <param name="naa2">numaa with one or more 256-element histograms</param>
    /// <returns>nad of EM distances for each histogram</returns>
    /// <remarks>
    /// (1) The two numaas must be the same size and have corresponding
    ///    256-element histograms.  Pairs do not need to be normalized
    ///    to the same sum.
    /// (2) This is typically used on two sets of histograms from
    ///    corresponding tiles of two images.  The similarity of two
    ///    images can be found with the scoring function used in
    ///    pixCompareGrayByHisto():
    ///        score S = 1.0 - k * D, where
    ///            k is a constant, say in the range 5-10
    ///            D = EMD
    ///        for each tile; for multiple tiles, take the Min(S) over
    ///        the set of tiles to be the final score.
    /// </remarks>
    public static Numa GrayHistogramsToEMD(Numaa naa1, Numaa naa2)
    {
        const string func = "grayHistogramsToEMD";
        int i, n, nt;
        float dist;
        Numa na1, na2, nad;

        n = naa1.Count;
        if (n != naa2.Count)
            throw Lept.Arg(func, "naa1 and naa2 numa counts differ");
        nt = naa1.GetNumberCount();
        if (nt != naa2.GetNumberCount())
            throw Lept.Arg(func, "naa1 and naa2 number counts differ");
        if (256 * n != nt)  /* good enough check */
            throw Lept.Arg(func, "na sizes must be 256");

        nad = Create(n);
        for (i = 0; i < n; i++) {
            na1 = naa1.GetNuma(i, L.L_CLONE);
            na2 = naa2.GetNuma(i, L.L_CLONE);
            dist = na1.EarthMoverDistance(na2);
            nad.AddNumber(dist / 255.0f);  /* normalize to [0.0 - 1.0] */
        }
        return nad;
    }

    // leptonica: numaEarthMoverDistance (numafunc2.c)
    /// <summary>Earth mover distance between two numas of the same size, typically histograms.</summary>
    /// <param name="na2">second numa, of the same size as this one</param>
    /// <returns>earthmover distance</returns>
    /// <remarks>
    /// (1) The two numas must have the same size.  They do not need to be
    ///     normalized to the same sum before applying the function.
    /// (2) For a 1D discrete function, the implementation of the EMD
    ///     is trivial.  Just keep filling or emptying buckets in one numa
    ///     to match the amount in the other, moving sequentially along
    ///     both arrays.
    /// (3) We divide the sum of the absolute value of everything moved
    ///     (by 1 unit at a time) by the sum of the numa (amount of "earth")
    ///     to get the average distance that the "earth" was moved.
    ///     This is the value returned here.
    /// (4) The caller can do a further normalization, by the number of
    ///     buckets (minus 1), to get the EM distance as a fraction of
    ///     the maximum possible distance, which is n-1.  This fraction
    ///     is 1.0 for the situation where all the 'earth' in the first
    ///     array is at one end, and all in the second array is at the
    ///     other end.
    /// </remarks>
    public float EarthMoverDistance(Numa na2)
    {
        int n, norm, i;
        float sum1, sum2, diff, total;
        float[] array1, array3;
        Numa na3;

        n = Count;
        if (n != na2.Count)
            throw Lept.Arg("numaEarthMoverDistance", "na1 and na2 have different size");

            /* Generate na3; normalize to na1 if necessary */
        sum1 = GetSum();
        sum2 = na2.GetSum();
        norm = (Math.Abs(sum1 - sum2) < 0.00001 * Math.Abs(sum1)) ? 1 : 0;
        if (norm == 0)
            na3 = na2.Transform(0, sum1 / sum2);
        else
            na3 = na2.Copy();
        array1 = GetFArray(L.L_NOCOPY);
        array3 = na3.GetFArray(L.L_NOCOPY);

            /* Move earth in n3 from array elements, to match n1 */
        total = 0;
        for (i = 1; i < n; i++) {
            diff = array1[i - 1] - array3[i - 1];
            array3[i] -= diff;
            total += Math.Abs(diff);
        }
        return total / sum1;
    }

    // leptonica: grayInterHistogramStats (numafunc2.c)
    /// <summary>Column-wise statistics across a set of 256-element histograms.</summary>
    /// <param name="naa">numaa with two or more 256-element histograms</param>
    /// <param name="wc">half-width of the smoothing window</param>
    /// <param name="nam">mean values</param>
    /// <param name="nams">mean square values</param>
    /// <param name="nav">variances</param>
    /// <param name="narv">rms deviations from the mean</param>
    /// <remarks>
    /// (1) The %naa has two or more 256-element numa histograms, which
    ///     are to be compared value-wise at each of the 256 gray levels.
    ///     The result are stats (mean, mean square, variance, root variance)
    ///     aggregated across the set of histograms, and each is output
    ///     as a 256 entry numa.  Think of these histograms as a matrix,
    ///     where each histogram is one row of the array.  The stats are
    ///     then aggregated column-wise, between the histograms.
    /// (2) These stats are:
    ///        ~ average value: &lt;v>  (nam)
    ///        ~ average squared value: &lt;v*v> (nams)
    ///        ~ variance: &lt;(v - &lt;v>)*(v - &lt;v>)> = &lt;v*v> - &lt;v>*&lt;v>  (nav)
    ///        ~ square-root of variance: (narv)
    ///     where the brackets &lt; .. > indicate that the average value is
    ///     to be taken over each column of the array.
    /// (3) The input histograms are optionally smoothed before these
    ///     statistical operations.
    /// (4) The input histograms are normalized to a sum of 10000.  By
    ///     doing this, the resulting numbers are independent of the
    ///     number of samples used in building the individual histograms.
    /// (5) A typical application is on a set of histograms from tiles
    ///     of an image, to distinguish between text/tables and photo
    ///     regions.  If the tiles are much larger than the text line
    ///     spacing, text/table regions typically have smaller variance
    ///     across tiles than photo regions.  For this application, it
    ///     may be useful to ignore values near white, which are large for
    ///     text and would magnify the variance due to variations in
    ///     illumination.  However, because the variance of a drawing or
    ///     a light photo can be similar to that of grayscale text, this
    ///     function is only a discriminator between darker photos/drawings
    ///     and light photos/text/line-graphics.
    /// </remarks>
    public static void GrayInterHistogramStats(Numaa naa, int wc, out Numa nam, out Numa nams,
                                               out Numa nav, out Numa narv)
    {
        int i, j, n, nn;
        float[][] arrays;
        float mean, var, rvar;
        Numa na1, na2, na3, na4;

        n = naa.Count;
        for (i = 0; i < n; i++) {
            nn = naa.GetNumaCount(i);
            if (nn != 256) {
                Log.Error("grayInterHistogramStats", CFormat.Sprintf("%d numbers in numa[%d]", nn, i));
                throw Lept.Arg("grayInterHistogramStats", CFormat.Sprintf("%d numbers in numa[%d]", nn, i));
            }
        }

        nam = Create(256);
        nams = Create(256);
        nav = Create(256);
        narv = Create(256);

            /* First, use mean smoothing, normalize each histogram,
             * and save all results in a 2D matrix. */
        arrays = new float[n][];
        for (i = 0; i < n; i++) {
            na1 = naa.GetNuma(i, L.L_CLONE);
            na2 = na1.WindowedMean(wc);
            na3 = na2.NormalizeHistogram(10000.0f);
            arrays[i] = na3.GetFArray(L.L_COPY);
        }

            /* Get stats between histograms */
        for (j = 0; j < 256; j++) {
            na4 = Create(n);
            for (i = 0; i < n; i++) {
                na4.AddNumber(arrays[i][j]);
            }
            if (n > 0) {
                na4.SimpleStats(0, -1, out mean, out var, out rvar);
            } else {
                /* C ignores the error of numaSimpleStats() on an empty numa; the results are 0 */
                Log.Error("numaSimpleStats", "na is empty");
                mean = var = rvar = 0.0f;
            }
            nam.AddNumber(mean);
            nams.AddNumber(mean * mean);
            nav.AddNumber(var);
            narv.AddNumber(rvar);
        }
    }

    /*----------------------------------------------------------------------*
     *                             Extrema finding                          *
     *----------------------------------------------------------------------*/
    // leptonica: numaFindPeaks (numafunc2.c)
    /// <summary>Finds the largest peaks of the numa.</summary>
    /// <param name="nmax">max number of peaks to be found</param>
    /// <param name="fract1">min fraction of peak value</param>
    /// <param name="fract2">min slope</param>
    /// <returns>peak na</returns>
    /// <remarks>
    /// (1) The returned na consists of sets of four numbers representing
    ///     the peak, in the following order:
    ///        left edge; peak center; right edge; normalized peak area
    /// </remarks>
    public Numa FindPeaks(int nmax, float fract1, float fract2)
    {
        int i, k, n, maxloc, lloc, rloc;
        float fmaxval, sum, total, newtotal, val, lastval;
        float peakfract;
        Numa na, napeak;

        n = Count;
        total = GetSum();

            /* We munge this copy */
        na = Copy();
        napeak = Create(4 * nmax);

        for (k = 0; k < nmax; k++) {
            newtotal = na.GetSum();
            if (newtotal == 0.0)   /* sanity check */
                break;
            na.GetMax(out fmaxval, out maxloc);
            sum = fmaxval;
            lastval = fmaxval;
            lloc = 0;
            for (i = maxloc - 1; i >= 0; --i) {
                val = na.GetFValue(i);
                if (val == 0.0) {
                    lloc = i + 1;
                    break;
                }
                if (val > fract1 * fmaxval) {
                    sum += val;
                    lastval = val;
                    continue;
                }
                if (lastval - val > fract2 * lastval) {
                    sum += val;
                    lastval = val;
                    continue;
                }
                lloc = i;
                break;
            }
            lastval = fmaxval;
            rloc = n - 1;
            for (i = maxloc + 1; i < n; ++i) {
                val = na.GetFValue(i);
                if (val == 0.0) {
                    rloc = i - 1;
                    break;
                }
                if (val > fract1 * fmaxval) {
                    sum += val;
                    lastval = val;
                    continue;
                }
                if (lastval - val > fract2 * lastval) {
                    sum += val;
                    lastval = val;
                    continue;
                }
                rloc = i;
                break;
            }
            peakfract = sum / total;
            napeak.AddNumber(lloc);
            napeak.AddNumber(maxloc);
            napeak.AddNumber(rloc);
            napeak.AddNumber(peakfract);

            for (i = lloc; i <= rloc; i++)
                na.SetValue(i, 0.0f);
        }

        return napeak;
    }

    // leptonica: numaFindExtrema (numafunc2.c)
    /// <summary>Finds the locations of the peaks and valleys that differ by at least delta.</summary>
    /// <param name="delta">relative amount to resolve peaks and valleys</param>
    /// <param name="nav">values of extrema</param>
    /// <returns>nad (locations of extrema)</returns>
    /// <remarks>
    /// (1) This returns a sequence of extrema (peaks and valleys).
    /// (2) The algorithm is analogous to that for determining
    ///     mountain peaks.  Suppose we have a local peak, with
    ///     bumps on the side.  Under what conditions can we consider
    ///     those 'bumps' to be actual peaks?  The answer: if the
    ///     bump is separated from the peak by a saddle that is at
    ///     least 500 feet below the bump.
    /// (3) Operationally, suppose we are trying to identify a peak.
    ///     We have a previous valley, and also the largest value that
    ///     we have seen since that valley.  We can identify this as
    ///     a peak if we find a value that is delta BELOW it.  When
    ///     we find such a value, label the peak, use the current
    ///     value to label the starting point for the search for
    ///     a valley, and do the same operation in reverse.  Namely,
    ///     keep track of the lowest point seen, and look for a value
    ///     that is delta ABOVE it.  Once found, the lowest point is
    ///     labeled the valley, and continue, looking for the next peak.
    /// </remarks>
    public Numa FindExtrema(float delta, out Numa nav)
    {
        int i, n, loc, direction;
        bool found;
        float startval, val, maxval, minval;
        Numa nad;

        if (delta < 0.0)
            throw Lept.Arg("numaFindExtrema", "delta < 0");

        n = Count;
        nad = Create(0);
        nav = Create(0);

            /* We don't know if we'll find a peak or valley first,
             * but use the first element of nas as the reference point.
             * Break when we deviate by 'delta' from the first point. */
        startval = 0.0f;
        if (n > 0)
            startval = GetFValue(0);
        else  /* C ignores the failure of numaGetFValue(); the result is empty */
            Log.Error("numaGetFValue", "index not valid");
        found = false;
        val = 0.0f;
        for (i = 1; i < n; i++) {
            val = GetFValue(i);
            if (Math.Abs(val - startval) >= delta) {
                found = true;
                break;
            }
        }

        if (!found)
            return nad;  /* it's empty */

            /* Are we looking for a peak or a valley? */
        maxval = 0.0f;
        minval = 0.0f;
        if (val > startval) {  /* peak */
            direction = 1;
            maxval = val;
        } else {
            direction = -1;
            minval = val;
        }
        loc = i;

            /* Sweep through the rest of the array, recording alternating
             * peak/valley extrema. */
        for (i = i + 1; i < n; i++) {
            val = GetFValue(i);
            if (direction == 1 && val > maxval) {  /* new local max */
                maxval = val;
                loc = i;
            } else if (direction == -1 && val < minval) {  /* new local min */
                minval = val;
                loc = i;
            } else if (direction == 1 && (maxval - val >= delta)) {
                nad.AddNumber(loc);  /* save the current max location */
                nav.AddNumber(maxval);
                direction = -1;  /* reverse: start looking for a min */
                minval = val;
                loc = i;  /* current min location */
            } else if (direction == -1 && (val - minval >= delta)) {
                nad.AddNumber(loc);  /* save the current min location */
                nav.AddNumber(minval);
                direction = 1;  /* reverse: start looking for a max */
                maxval = val;
                loc = i;  /* current max location */
            }
        }

            /* Save the final extremum */
        /*    numaAddNumber(nad, loc); */
        return nad;
    }

    // leptonica: numaFindLocForThreshold (numafunc2.c)
    /// <summary>Finds a good place to set a threshold for a histogram with two peaks.</summary>
    /// <param name="skip">look-ahead distance to avoid false mininma; use 0 for default</param>
    /// <param name="thresh">threshold value</param>
    /// <param name="fract">fraction below or at threshold</param>
    /// <returns>true if OK; false (logged as an error, outputs 0) if no threshold can be found</returns>
    /// <remarks>
    /// (1) This finds a good place to set a threshold for a histogram
    ///     of values that has two peaks.  The peaks can differ greatly
    ///     in area underneath them.  The number of buckets in the
    ///     histogram is expected to be 256 (e.g, from an 8 bpp gray image).
    /// (2) The input histogram should have been smoothed with a window
    ///     to avoid false peak and valley detection due to noise.  For
    ///     example, see pixThresholdByHisto().
    /// (3) A skip value can be input to determine the look-ahead distance
    ///     to ignore a false peak on the rise or descent from the first peak.
    ///     Input 0 to use the default value (it assumes a histo size of 256).
    /// (4) Optionally, the fractional area under the first peak can
    ///     be returned.
    /// </remarks>
    public bool FindLocForThreshold(int skip, out int thresh, out float fract)
    {
        const string func = "numaFindLocForThreshold";
        int i, n, start, index, minloc;
        bool found;
        float val, pval, jval, minval, maxval, sum, partsum;
        float[] fa;

        fract = 0.0f;
        thresh = 0;
        if (skip <= 0) skip = 20;

            /* Test for constant value */
        GetMin(out minval, out _);
        GetMax(out maxval, out _);
        if (minval == maxval) {
            Log.Error(func, "all array values are the same");
            return false;
        }

            /* Look for the top of the first peak */
        n = Count;
        if (n < 256)
            Log.Warning(func, CFormat.Sprintf("array size %d < 256", n));
        fa = GetFArray(L.L_NOCOPY);
        pval = fa[0];
        for (i = 1; i < n; i++) {
            val = fa[i];
            index = Math.Min(i + skip, n - 1);
            jval = fa[index];
            if (val < pval && jval < pval)  /* near the top if not there */
                break;
            pval = val;
        }

        if (i > n - 5) {  /* just an increasing function */
            Log.Error(func, "top of first peak not found");
            return false;
        }

            /* Look for the low point in the valley */
        found = false;
        index = 0;
        start = i;
        pval = fa[start];
        for (i = start + 1; i < n; i++) {
            val = fa[i];
            if (val <= pval) {  /* appears to be going down */
                pval = val;
            } else {  /* appears to be going up */
                index = Math.Min(i + skip, n - 1);
                jval = fa[index];  /* junp ahead by 'skip' */
                if (val > jval) {  /* still going down; jump ahead */
                    pval = jval;
                    i = index;
                } else {  /* really going up; passed the min */
                    found = true;
                    break;
                }
            }
        }
        if (!found) {
            Log.Error(func, "no minimum found");
            return false;
        }

            /* Find the location of the minimum in the interval */
        minloc = index;  /* likely passed the min; look backward */
        minval = fa[index];
        for (i = index - 1; i > index - skip; i--) {
            if (fa[i] < minval) {
                minval = fa[i];
                minloc = i;
            }
        }

            /* Is the minimum very near the end of the array? */
        if (minloc > n - 10) {
            Log.Error(func, "minimum at end of array; invalid");
            return false;
        }
        thresh = minloc;

            /* Find the fraction under the first peak */
        partsum = GetSumOnInterval(0, minloc);
        sum = GetSum();
        if (sum > 0.0)
            fract = partsum / sum;
        return true;
    }

    // leptonica: numaCountReversals (numafunc2.c)
    /// <summary>Counts the reversals (peaks and valleys that differ by at least minreversal).</summary>
    /// <param name="minreversal">relative amount to resolve peaks and valleys</param>
    /// <param name="nr">number of reversals</param>
    /// <param name="rd">reversal density: reversals/length</param>
    /// <remarks>
    /// (1) The input numa is can be generated from pixExtractAlongLine().
    ///     If so, the x parameters can be used to find the reversal
    ///     frequency along a line.
    /// (2) If the input numa was generated from a 1 bpp pix, the
    ///     values will be 0 and 1.  Use %minreversal == 1 to get
    ///     the number of pixel flips.  If the only values are 0 and 1,
    ///     but %minreversal > 1, set the reversal count to 0 and
    ///     issue a warning.
    /// </remarks>
    public void CountReversals(float minreversal, out int nr, out float rd)
    {
        const string func = "numaCountReversals";
        int i, n, ival;
        bool binvals;
        int[] ia;
        float fval, delx, len;
        Numa nat;

        nr = 0;
        rd = 0.0f;
        if ((n = Count) == 0) {
            Log.Info(func, "nas is empty");
            return;
        }
        if (minreversal < 0.0)
            throw Lept.Arg(func, "minreversal < 0");

            /* Decide if the only values are 0 and 1 */
        binvals = true;
        for (i = 0; i < n; i++) {
            fval = GetFValue(i);
            if (fval != 0.0 && fval != 1.0) {
                binvals = false;
                break;
            }
        }

        if (binvals) {
            if (minreversal > 1.0) {
                Log.Warning(func, "binary values but minreversal > 1");
            } else {
                ia = GetIArray();
                ival = ia[0];
                for (i = 1; i < n; i++) {
                    if (ia[i] != ival) {
                        nr++;
                        ival = ia[i];
                    }
                }
            }
        } else {
            nat = FindExtrema(minreversal, out _);
            nr = nat.Count;
        }
        GetParameters(out _, out delx);
        len = delx * n;
        rd = (float)nr / len;
    }

    /*----------------------------------------------------------------------*
     *                Threshold crossings and frequency analysis            *
     *----------------------------------------------------------------------*/
    // leptonica: numaSelectCrossingThreshold (numafunc2.c)
    /// <summary>Selects a robust threshold at the center of the plateau of maximum crossings.</summary>
    /// <param name="nax">numa of abscissa values; can be null</param>
    /// <param name="nay">signal</param>
    /// <param name="estthresh">estimated pixel threshold for crossing:
    /// e.g., for images, white &lt;--> black; typ. ~120</param>
    /// <param name="bestthresh">robust estimate of threshold to use</param>
    /// <returns>true if OK; false on a warning (the count of nay is less than 2; bestthresh is 0)</returns>
    /// <remarks>
    /// (1) When a valid threshold is used, the number of crossings is
    ///     a maximum, because none are missed.  If no threshold intersects
    ///     all the crossings, the crossings must be determined with
    ///     numaCrossingsByPeaks().
    /// (2) %estthresh is an input estimate of the threshold that should
    ///     be used.  We compute the crossings with 41 thresholds
    ///     (20 below and 20 above).  There is a range in which the
    ///     number of crossings is a maximum.  Return a threshold
    ///     in the center of this stable plateau of crossings.
    ///     This can then be used with numaCrossingsByThreshold()
    ///     to get a good estimate of crossing locations.
    /// (3) If the count of nay is less than 2, a warning is issued.
    /// </remarks>
    public static bool SelectCrossingThreshold(Numa? nax, Numa nay, float estthresh, out float bestthresh)
    {
        int i, istart, iend, maxstart, maxend, runlen, maxrunlen;
        int val, maxval, nmax, count;
        bool inrun;
        float thresh, fmaxval, fmodeval;
        Numa nat, nac;

        bestthresh = 0.0f;
        if (nay.Count < 2) {
            Log.Warning("numaSelectCrossingThreshold", "nay count < 2; no threshold crossing");
            return false;
        }

            /* Compute the number of crossings for different thresholds */
        nat = Create(41);
        for (i = 0; i < 41; i++) {
            thresh = estthresh - 80.0f + 4.0f * i;
            nac = CrossingsByThreshold(nax, nay, thresh);
            nat.AddNumber(nac.Count);
        }

            /* Find the center of the plateau of max crossings, which
             * extends from thresh[istart] to thresh[iend]. */
        nat.GetMax(out fmaxval, out _);
        maxval = (int)fmaxval;
        nmax = 0;
        for (i = 0; i < 41; i++) {
            val = nat.GetIValue(i);
            if (val == maxval)
                nmax++;
        }
        if (nmax < 3) {  /* likely accidental max; try the mode */
            nat.GetMode(out fmodeval, out count);
            if (count > nmax && fmodeval > 0.5 * fmaxval)
                maxval = (int)fmodeval;  /* use the mode */
        }

        inrun = false;
        istart = 0;
        iend = 40;
        maxrunlen = 0; maxstart = 0; maxend = 0;
        for (i = 0; i < 41; i++) {
            val = nat.GetIValue(i);
            if (val == maxval) {
                if (!inrun) {
                    istart = i;
                    inrun = true;
                }
                continue;
            }
            if (inrun && (val != maxval)) {
                iend = i - 1;
                runlen = iend - istart + 1;
                inrun = false;
                if (runlen > maxrunlen) {
                    maxstart = istart;
                    maxend = iend;
                    maxrunlen = runlen;
                }
            }
        }
        if (inrun) {
            runlen = i - istart;
            if (runlen > maxrunlen) {
                maxstart = istart;
                maxend = i - 1;
                maxrunlen = runlen;
            }
        }

        bestthresh = estthresh - 80.0f + 2.0f * (float)(maxstart + maxend);
        return true;
    }

    // leptonica: numaCrossingsByThreshold (numafunc2.c)
    /// <summary>Abscissa values where the signal crosses the threshold.</summary>
    /// <param name="nax">numa of abscissa values; can be null</param>
    /// <param name="nay">numa of ordinate values, corresponding to nax</param>
    /// <param name="thresh">threshold value for nay</param>
    /// <returns>nad abscissa pts at threshold</returns>
    /// <remarks>
    /// (1) If nax == null, we use startx and delx from nay to compute
    ///     the crossing values in nad.
    /// </remarks>
    public static Numa CrossingsByThreshold(Numa? nax, Numa nay, float thresh)
    {
        int i, n;
        float startx, delx;
        float xval1, xval2, yval1, yval2, delta1, delta2, crossval, fract;
        Numa nad;

        n = nay.Count;

        if (nax != null && (nax.Count != n))
            throw Lept.Arg("numaCrossingsByThreshold", "nax and nay sizes differ");

        nad = Create(0);
        if (n < 2) return nad;
        yval1 = nay.GetFValue(0);
        nay.GetParameters(out startx, out delx);
        if (nax != null)
            xval1 = nax.GetFValue(0);
        else
            xval1 = startx;
        for (i = 1; i < n; i++) {
            yval2 = nay.GetFValue(i);
            if (nax != null)
                xval2 = nax.GetFValue(i);
            else
                xval2 = startx + i * delx;
            delta1 = yval1 - thresh;
            delta2 = yval2 - thresh;
            if (delta1 == 0.0) {
                nad.AddNumber(xval1);
            } else if (delta2 == 0.0) {
                nad.AddNumber(xval2);
            } else if (delta1 * delta2 < 0.0) {  /* crossing */
                fract = Math.Abs(delta1) / Math.Abs(yval1 - yval2);
                crossval = xval1 + fract * (xval2 - xval1);
                nad.AddNumber(crossval);
            }
            xval1 = xval2;
            yval1 = yval2;
        }

        return nad;
    }

    // leptonica: numaCrossingsByPeaks (numafunc2.c)
    /// <summary>Abscissa values where the signal crosses the midlevel between successive extrema.</summary>
    /// <param name="nax">numa of abscissa values; can be null</param>
    /// <param name="nay">numa of ordinate values, corresponding to nax</param>
    /// <param name="delta">parameter used to identify when a new peak can be found</param>
    /// <returns>nad abscissa pts at threshold</returns>
    /// <remarks>
    /// (1) If nax == null, we use startx and delx from nay to compute
    ///     the crossing values in nad.
    /// </remarks>
    public static Numa CrossingsByPeaks(Numa? nax, Numa nay, float delta)
    {
        int i, j, n, np, previndex, curindex;
        float startx, delx;
        float xval1, xval2, yval1, yval2, delta1, delta2;
        float prevval, curval, thresh, crossval, fract;
        Numa nap, nad;

        n = nay.Count;
        if (nax != null && (nax.Count != n))
            throw Lept.Arg("numaCrossingsByPeaks", "nax and nay sizes differ");

            /* Find the extrema.  Also add last point in nay to get
             * the last transition (from the last peak to the end).
             * The number of crossings is 1 more than the number of extrema. */
        nap = nay.FindExtrema(delta, out _);
        nap.AddNumber(n - 1);
        np = nap.Count;
        Log.Info("numaCrossingsByPeaks", CFormat.Sprintf("Number of crossings: %d", np));

            /* Do all computation in index units of nax or the delx of nay */
        nad = Create(np);  /* output crossing locations, in nax units */
        previndex = 0;  /* prime the search with 1st point */
        prevval = nay.GetFValue(0);  /* prime the search with 1st point */
        nay.GetParameters(out startx, out delx);
        for (i = 0; i < np; i++) {
            curindex = nap.GetIValue(i);
            curval = nay.GetFValue(curindex);
            thresh = (prevval + curval) / 2.0f;
            if (nax != null)
                xval1 = nax.GetFValue(previndex);
            else
                xval1 = startx + previndex * delx;
            yval1 = nay.GetFValue(previndex);
            for (j = previndex + 1; j <= curindex; j++) {
                if (nax != null)
                    xval2 = nax.GetFValue(j);
                else
                    xval2 = startx + j * delx;
                yval2 = nay.GetFValue(j);
                delta1 = yval1 - thresh;
                delta2 = yval2 - thresh;
                if (delta1 == 0.0) {
                    nad.AddNumber(xval1);
                    break;
                } else if (delta2 == 0.0) {
                    nad.AddNumber(xval2);
                    break;
                } else if (delta1 * delta2 < 0.0) {  /* crossing */
                    fract = Math.Abs(delta1) / Math.Abs(yval1 - yval2);
                    crossval = xval1 + fract * (xval2 - xval1);
                    nad.AddNumber(crossval);
                    break;
                }
                xval1 = xval2;
                yval1 = yval2;
            }
            previndex = curindex;
            prevval = curval;
        }

        return nad;
    }

    // leptonica: numaEvalBestHaarParameters (numafunc2.c)
    /// <summary>Finds the width and shift of the Haar-like comb that gives the largest score.</summary>
    /// <param name="relweight">relative weight of (-1 comb) / (+1 comb)
    /// contributions to the 'convolution'.  In effect,
    /// the convolution kernel is a comb consisting of
    /// alternating +1 and -weight.</param>
    /// <param name="nwidth">number of widths to consider</param>
    /// <param name="nshift">number of shifts to consider for each width</param>
    /// <param name="minwidth">smallest width to consider</param>
    /// <param name="maxwidth">largest width to consider</param>
    /// <param name="bestwidth">width giving largest score</param>
    /// <param name="bestshift">shift giving largest score</param>
    /// <param name="bestscore">convolution with "Haar"-like comb</param>
    /// <remarks>
    /// (1) This does a linear sweep of widths, evaluating at %nshift
    ///     shifts for each width, computing the score from a convolution
    ///     with a long comb, and finding the (width, shift) pair that
    ///     gives the maximum score.  The best width is the "half-wavelength"
    ///     of the signal.
    /// (2) The convolving function is a comb of alternating values
    ///     +1 and -1 * relweight, separated by the width and phased by
    ///     the shift.  This is similar to a Haar transform, except
    ///     there the convolution is performed with a square wave.
    /// (3) The function is useful for finding the line spacing
    ///     and strength of line signal from pixel sum projections.
    /// (4) The score is normalized to the size of nas divided by
    ///     the number of half-widths.  For image applications, the input is
    ///     typically an array of pixel projections, so one should
    ///     normalize by dividing the score by the image width in the
    ///     pixel projection direction.
    /// </remarks>
    public void EvalBestHaarParameters(float relweight, int nwidth, int nshift, float minwidth,
                                       float maxwidth, out float bestwidth, out float bestshift,
                                       out float bestscore)
    {
        int i, j;
        float delwidth, delshift, width, shift, score;

        bestscore = bestwidth = bestshift = 0.0f;
        delwidth = (maxwidth - minwidth) / (nwidth - 1.0f);
        for (i = 0; i < nwidth; i++) {
            width = minwidth + delwidth * i;
            delshift = width / (float)(nshift);
            for (j = 0; j < nshift; j++) {
                shift = j * delshift;
                /* C ignores the error of numaEvalHaarSum(), which leaves score = 0 */
                EvalHaarSumCore(width, shift, relweight, out score);
                if (score > bestscore) {
                    bestscore = score;
                    bestwidth = width;
                    bestshift = shift;
                }
            }
        }
    }

    // leptonica: numaEvalHaarSum (numafunc2.c)
    /// <summary>Convolution with a comb of alternating +1 and -relweight.</summary>
    /// <param name="width">distance between +1 and -1 in convolution comb</param>
    /// <param name="shift">phase of the comb: location of first +1</param>
    /// <param name="relweight">relative weight of (-1 comb) / (+1 comb)
    /// contributions to the 'convolution'.  In effect,
    /// the convolution kernel is a comb consisting of
    /// alternating +1 and -weight.</param>
    /// <returns>convolution with "Haar"-like comb</returns>
    /// <remarks>
    /// (1) This does a convolution with a comb of alternating values
    ///     +1 and -relweight, separated by the width and phased by the shift.
    ///     This is similar to a Haar transform, except that for Haar,
    ///       (1) the convolution kernel is symmetric about 0, so the
    ///           relweight is 1.0, and
    ///       (2) the convolution is performed with a square wave.
    /// (2) The score is normalized to the size of nas divided by
    ///     twice the "width".  For image applications, the input is
    ///     typically an array of pixel projections, so one should
    ///     normalize by dividing the score by the image width in the
    ///     pixel projection direction.
    /// (3) To get a Haar-like result, use relweight = 1.0.  For detecting
    ///     signals where you expect every other sample to be close to
    ///     zero, as with barcodes or filtered text lines, you can
    ///     use relweight > 1.0.
    /// </remarks>
    public float EvalHaarSum(float width, float shift, float relweight)
    {
        if (!EvalHaarSumCore(width, shift, relweight, out float score))
            throw Lept.Arg("numaEvalHaarSum", "nas size too small");
        return score;
    }

    /* numaEvalHaarSum() without the exception: false (score = 0) if nas is too small */
    private bool EvalHaarSumCore(float width, float shift, float relweight, out float pscore)
    {
        int i, n, nsamp, index;
        float score, weight, val;

        pscore = 0.0f;
        if ((n = Count) < 2 * width) {
            Log.Error("numaEvalHaarSum", "nas size too small");
            return false;
        }

        score = 0.0f;
        nsamp = (int)((n - shift) / width);
        for (i = 0; i < nsamp; i++) {
            index = (int)(shift + i * width);
            weight = (i % 2) != 0 ? 1.0f : -1.0f * relweight;
            val = GetFValue(index);
            score += weight * val;
        }

        pscore = 2.0f * width * score / (float)n;
        return true;
    }

    /*----------------------------------------------------------------------*
     *            Generating numbers in a range under constraints           *
     *----------------------------------------------------------------------*/
    // leptonica: genConstrainedNumaInRange (numafunc2.c)
    /// <summary>Generates numbers selected uniformly in a range, under constraints.</summary>
    /// <param name="first">first number to choose; >= 0</param>
    /// <param name="last">biggest possible number to reach; >= first</param>
    /// <param name="nmax">maximum number of numbers to select; > 0</param>
    /// <param name="use_pairs">1 = select pairs of adjacent numbers;
    /// 0 = select individual numbers</param>
    /// <returns>na of the selected numbers</returns>
    /// <remarks>
    /// (1) Selection is made uniformly in the range.  This can be used
    ///     to select pages distributed as uniformly as possible
    ///     through a book, where you are constrained to:
    ///      ~ choose between [first, ... biggest],
    ///      ~ choose no more than nmax numbers, and
    ///     and you have the option of requiring pairs of adjacent numbers.
    /// </remarks>
    public static Numa GenConstrainedNumaInRange(int first, int last, int nmax, int use_pairs)
    {
        const string func = "genConstrainedNumaInRange";
        int i, nsets, val;
        float delta;
        Numa na;

        first = Math.Max(0, first);
        if (last < first)
            throw Lept.Arg(func, "last < first!");
        if (nmax < 1)
            throw Lept.Arg(func, "nmax < 1!");

        nsets = Math.Min(nmax, last - first + 1);
        if (use_pairs == 1)
            nsets = nsets / 2;
        if (nsets == 0)
            throw Lept.Arg(func, "nsets == 0");

            /* Select delta so that selection covers the full range if possible */
        if (nsets == 1) {
            delta = 0.0f;
        } else {
            if (use_pairs == 0)
                delta = (float)(last - first) / (nsets - 1);
            else
                delta = (float)(last - first - 1) / (nsets - 1);
        }

        na = Create(nsets);
        for (i = 0; i < nsets; i++) {
            val = (int)(first + i * delta + 0.5);
            na.AddNumber(val);
            if (use_pairs == 1)
                na.AddNumber(val + 1);
        }

        return na;
    }
}
