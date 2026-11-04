using System;

namespace Leptonica;

/*
 *  numafunc1.c: Numa utilities: arithmetic operations, simple data analysis,
 *  special sequences, permutations, interpolation, sorting, data analysis
 *  requiring sorting, joins and rearrangements.
 *
 *  Differences from C:
 *    - No reference counting and no numaDestroy(): the GC owns the objects.
 *    - Functions that return an error code (ERROR_INT) throw instead; a single
 *      required output is the return value, several outputs are out parameters.
 *      Results documented as 1/0 flags (numaSimilar, numaHasOnlyIntegers,
 *      numaIsSorted) are returned as bool.
 *    - numaGetNonzeroRange() returns false (C: 1) when no nonzero range is found,
 *      and still sets the outputs as C does. Invalid input throws.
 *    - Optional destinations (nad) that must be null or equal to the source are
 *      kept as the first argument of a static method.
 *    - Optional output pointers are always computed (out parameters).
 *    - Where the C code ignores the error return of a callee and carries on
 *      (numaInterpolateEqxInterval(), numaSortGeneral() and numaChooseSortType()
 *      for an empty numa), the port reproduces the value the C callee leaves in
 *      its output.
 *
 *  Interim substitutes (TODO markers in the code):
 *    - ptra.c: numaGetBinSortIndex() uses a plain array of Numa for the L_PTRA.
 *    - rand()/srand(): numaPseudorandomSequence() uses a private copy of the MSVC
 *      CRT generator until CRand is available.
 *
 *  Stubs created for callees in files that are not ported yet:
 *    lept_roundftoi (utils1.c).
 */

/// <summary>Constants from morph.h, array.h and pix.h used by numafunc1.c.</summary>
public static partial class L
{
    /* Arithmetic and logical operator flags (morph.h) */
    /// <summary>Arithmetic operator: addition.</summary>
    public const int L_ARITH_ADD = 1;
    /// <summary>Arithmetic operator: subtraction.</summary>
    public const int L_ARITH_SUBTRACT = 2;
    /// <summary>Arithmetic operator: multiplication (for Numa only).</summary>
    public const int L_ARITH_MULTIPLY = 3;
    /// <summary>Arithmetic operator: division (for Numa only).</summary>
    public const int L_ARITH_DIVIDE = 4;
    /// <summary>Logical operator: union (for Numa only).</summary>
    public const int L_UNION = 5;
    /// <summary>Logical operator: intersection (for Numa only).</summary>
    public const int L_INTERSECTION = 6;
    /// <summary>Logical operator: subtraction (for Numa only).</summary>
    public const int L_SUBTRACTION = 7;
    /// <summary>Logical operator: exclusive or (for Numa only).</summary>
    public const int L_EXCLUSIVE_OR = 8;

    /* Numa interpolation (array.h) */
    /// <summary>Linear interpolation.</summary>
    public const int L_LINEAR_INTERP = 1;
    /// <summary>Quadratic interpolation.</summary>
    public const int L_QUADRATIC_INTERP = 2;

    /* Numa border adding (array.h) */
    /// <summary>Border extended with the same value as the nearest element.</summary>
    public const int L_CONTINUED_BORDER = 1;
    /// <summary>Border extended with a constant normal derivative.</summary>
    public const int L_SLOPE_BORDER = 2;
    /// <summary>Border filled by mirroring the array.</summary>
    public const int L_MIRRORED_BORDER = 3;

    /* Sort type (pix.h); L_SORT_INCREASING and L_SORT_DECREASING are in sarray1.c */
    /// <summary>Sort with the shell sort.</summary>
    public const int L_SHELL_SORT = 1;
    /// <summary>Sort with the bin sort.</summary>
    public const int L_BIN_SORT = 2;

    /* Threshold selection (pix.h) */

    /* Counts relative to zero (pix.h) */
    /// <summary>Select values less than zero.</summary>
    public const int L_LESS_THAN_ZERO = 1;
    /// <summary>Select values equal to zero.</summary>
    public const int L_EQUAL_TO_ZERO = 2;
    /// <summary>Select values greater than zero.</summary>
    public const int L_GREATER_THAN_ZERO = 3;

    /* Maximum initial size of a ptra (ptra.c); limits the bin sort */
    /// <summary>Maximum initial size of a pointer array; this limits the size of the bin sort.</summary>
    public const uint MaxInitPtraSize = 1000001;
}

/// <summary>Array of numbers (struct Numa).</summary>
public sealed partial class Numa
{
    /*----------------------------------------------------------------------*
     *                Arithmetic and logical ops on Numas                   *
     *----------------------------------------------------------------------*/
    // leptonica: numaArithOp (numafunc1.c)
    /// <summary>Applies an arithmetic operation to two numas of equal size.</summary>
    /// <param name="nad">[optional] can be null or equal to na1 (in-place)</param>
    /// <param name="na1">first operand</param>
    /// <param name="na2">second operand</param>
    /// <param name="op">L_ARITH_ADD, L_ARITH_SUBTRACT, L_ARITH_MULTIPLY, L_ARITH_DIVIDE</param>
    /// <returns>nad always: operation applied to na1 and na2</returns>
    /// <remarks>
    /// (1) The sizes of na1 and na2 must be equal.
    /// (2) nad can only null or equal to na1.
    /// (3) To add a constant to a numa, or to multiply a numa by
    ///     a constant, use numaTransform().
    /// </remarks>
    public static Numa ArithOp(Numa? nad, Numa na1, Numa na2, int op)
    {
        const string func = "numaArithOp";

        int n = na1.Count;
        if (n != na2.Count)
            throw Lept.Arg(func, "na1, na2 sizes differ");
        if (nad != null && nad != na1)
            throw Lept.Arg(func, "nad defined but not in-place");
        if (op != L.L_ARITH_ADD && op != L.L_ARITH_SUBTRACT &&
            op != L.L_ARITH_MULTIPLY && op != L.L_ARITH_DIVIDE)
            throw Lept.Arg(func, "invalid op");
        if (op == L.L_ARITH_DIVIDE) {
            for (int i = 0; i < n; i++) {
                float val2 = na2.GetFValue(i);
                if (val2 == 0.0)
                    throw Lept.Arg(func, "na2 has 0 element");
            }
        }

            /* If nad is not identical to na1, make it an identical copy */
        nad ??= na1.Copy();

        for (int i = 0; i < n; i++) {
            float val1 = nad.GetFValue(i);
            float val2 = na2.GetFValue(i);
            switch (op) {
            case L.L_ARITH_ADD:
                nad.SetValue(i, val1 + val2);
                break;
            case L.L_ARITH_SUBTRACT:
                nad.SetValue(i, val1 - val2);
                break;
            case L.L_ARITH_MULTIPLY:
                nad.SetValue(i, val1 * val2);
                break;
            case L.L_ARITH_DIVIDE:
                nad.SetValue(i, val1 / val2);
                break;
            default:
                CStreamText.Stderr(" Unknown arith op: %d\n", op);
                return nad;
            }
        }

        return nad;
    }

    // leptonica: numaLogicalOp (numafunc1.c)
    /// <summary>Applies a logical operation to two indicator numas of equal size.</summary>
    /// <param name="nad">[optional] can be null or equal to na1 (in-place)</param>
    /// <param name="na1">first operand</param>
    /// <param name="na2">second operand</param>
    /// <param name="op">L_UNION, L_INTERSECTION, L_SUBTRACTION, L_EXCLUSIVE_OR</param>
    /// <returns>nad always: operation applied to na1 and na2</returns>
    /// <remarks>
    /// (1) The sizes of na1 and na2 must be equal.
    /// (2) nad can only be null or equal to na1.
    /// (3) This is intended for use with indicator arrays (0s and 1s).
    ///     Input data is extracted as integers (0 == false, anything
    ///     else == true); output results are 0 and 1.
    /// (4) L_SUBTRACTION is subtraction of val2 from val1.  For bit logical
    ///     arithmetic this is (val1 &amp; ~val2), but because these values
    ///     are integers, we use (val1 &amp;&amp; !val2).
    /// </remarks>
    public static Numa LogicalOp(Numa? nad, Numa na1, Numa na2, int op)
    {
        const string func = "numaLogicalOp";

        int n = na1.Count;
        if (n != na2.Count)
            throw Lept.Arg(func, "na1, na2 sizes differ");
        if (nad != null && nad != na1)
            throw Lept.Arg(func, "nad defined; not in-place");
        if (op != L.L_UNION && op != L.L_INTERSECTION &&
            op != L.L_SUBTRACTION && op != L.L_EXCLUSIVE_OR)
            throw Lept.Arg(func, "invalid op");

            /* If nad is not identical to na1, make it an identical copy */
        nad ??= na1.Copy();

        for (int i = 0; i < n; i++) {
            int val1 = nad.GetIValue(i);
            int val2 = na2.GetIValue(i);
            val1 = (val1 == 0) ? 0 : 1;
            val2 = (val2 == 0) ? 0 : 1;
            int val;
            switch (op) {
            case L.L_UNION:
                val = (val1 != 0 || val2 != 0) ? 1 : 0;
                nad.SetValue(i, val);
                break;
            case L.L_INTERSECTION:
                val = (val1 != 0 && val2 != 0) ? 1 : 0;
                nad.SetValue(i, val);
                break;
            case L.L_SUBTRACTION:
                val = (val1 != 0 && val2 == 0) ? 1 : 0;
                nad.SetValue(i, val);
                break;
            case L.L_EXCLUSIVE_OR:
                val = (val1 != val2) ? 1 : 0;
                nad.SetValue(i, val);
                break;
            default:
                CStreamText.Stderr(" Unknown logical op: %d\n", op);
                return nad;
            }
        }

        return nad;
    }

    // leptonica: numaInvert (numafunc1.c)
    /// <summary>'Inverts' an indicator numa.</summary>
    /// <param name="nad">[optional] can be null or equal to nas (in-place)</param>
    /// <param name="nas">source numa</param>
    /// <returns>nad always: 'inverts' nas</returns>
    /// <remarks>
    /// (1) This is intended for use with indicator arrays (0s and 1s).
    ///     It gives a boolean-type output, taking the input as
    ///     an integer and inverting it:
    ///         0              --&gt;  1
    ///         anything else  --&gt;  0
    /// </remarks>
    public static Numa Invert(Numa? nad, Numa nas)
    {
        if (nad != null && nad != nas)
            throw Lept.Arg("numaInvert", "nad defined; not in-place");

        nad ??= nas.Copy();
        int n = nad.Count;
        for (int i = 0; i < n; i++) {
            int val = nad.GetIValue(i);
            if (val == 0)
                val = 1;
            else
                val = 0;
            nad.SetValue(i, val);
        }
        return nad;
    }

    // leptonica: numaSimilar (numafunc1.c)
    /// <summary>Tests if two numas are equal, up to a maximum difference per element.</summary>
    /// <param name="na2">second numa</param>
    /// <param name="maxdiff">use 0.0 for exact equality</param>
    /// <returns>true if similar; false if different</returns>
    /// <remarks>
    /// (1) Float values can differ slightly due to roundoff and
    ///     accumulated errors.  Using %maxdiff > 0.0 allows similar
    ///     arrays to be identified.
    /// </remarks>
    public bool Similar(Numa na2, float maxdiff)
    {
        maxdiff = (maxdiff < 0) ? -1 * maxdiff : maxdiff;   /* L_ABS */

        int n = Count;
        if (n != na2.Count) return false;

        for (int i = 0; i < n; i++) {
            float val1 = GetFValue(i);
            float val2 = na2.GetFValue(i);
            float diff = val1 - val2;
            if (((diff < 0) ? -1 * diff : diff) > maxdiff) return false;
        }

        return true;
    }

    // leptonica: numaAddToNumber (numafunc1.c)
    /// <summary>Adds a value to the element at the index.</summary>
    /// <param name="index">element to be changed</param>
    /// <param name="val">new value to be added</param>
    /// <remarks>
    /// (1) This is useful for accumulating sums, regardless of the index
    ///     order in which the values are made available.
    /// (2) Before use, the numa has to be filled up to %index.  This would
    ///     typically be used by creating the numa with the full sized
    ///     array, initialized to 0.0, using numaMakeConstant().
    /// </remarks>
    public void AddToNumber(int index, float val)
    {
        const string func = "numaAddToNumber";

        int n;
        if ((n = Count) == 0)
            throw Lept.Arg(func, "na is empty");
        if (index < 0 || index >= n)
            throw Lept.Arg(func, $"index {index} not in [0,...,{n - 1}]");

        array[index] += val;
    }

    /*----------------------------------------------------------------------*
     *                         Simple extractions                           *
     *----------------------------------------------------------------------*/
    // leptonica: numaGetMin (numafunc1.c)
    /// <summary>Finds the minimum value and its location.</summary>
    /// <param name="minval">min value</param>
    /// <param name="iminloc">index of min location</param>
    public void GetMin(out float minval, out int iminloc)
    {
        minval = 0.0f;
        iminloc = 0;
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetMin", "na is empty");

        minval = 1000000000.0f;
        iminloc = 0;
        for (int i = 0; i < n; i++) {
            float val = GetFValue(i);
            if (val < minval) {
                minval = val;
                iminloc = i;
            }
        }
    }

    // leptonica: numaGetMax (numafunc1.c)
    /// <summary>Finds the maximum value and its location.</summary>
    /// <param name="maxval">max value</param>
    /// <param name="imaxloc">index of max location</param>
    public void GetMax(out float maxval, out int imaxloc)
    {
        maxval = 0.0f;
        imaxloc = 0;
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetMax", "na is empty");

        maxval = -1000000000.0f;
        imaxloc = 0;
        for (int i = 0; i < n; i++) {
            float val = GetFValue(i);
            if (val > maxval) {
                maxval = val;
                imaxloc = i;
            }
        }
    }

    // leptonica: numaGetSum (numafunc1.c)
    /// <summary>Sums the values.</summary>
    /// <returns>sum of values</returns>
    public float GetSum()
    {
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetSum", "na is empty");
        float sum = 0.0f;
        for (int i = 0; i < n; i++) {
            float val = GetFValue(i);
            sum += val;
        }
        return sum;
    }

    // leptonica: numaGetPartialSums (numafunc1.c)
    /// <summary>Returns the running sums.</summary>
    /// <returns>nasum</returns>
    /// <remarks>
    /// (1) nasum[i] is the sum for all j &lt;= i of na[j].
    ///     So nasum[0] = na[0].
    /// (2) If you want to generate a rank function, where rank[0] - 0.0,
    ///     insert a 0.0 at the beginning of the nasum array.
    /// </remarks>
    public Numa GetPartialSums()
    {
        int n;
        if ((n = Count) == 0)
            Log.Warning("numaGetPartialSums", "na is empty");
        Numa nasum = Create(n);
        float sum = 0.0f;
        for (int i = 0; i < n; i++) {
            float val = GetFValue(i);
            sum += val;
            nasum.AddNumber(sum);
        }
        return nasum;
    }

    // leptonica: numaGetSumOnInterval (numafunc1.c)
    /// <summary>Sums the values in an index interval.</summary>
    /// <param name="first">beginning index</param>
    /// <param name="last">final index; use -1 to go to the end</param>
    /// <returns>sum of values in the index interval range</returns>
    public float GetSumOnInterval(int first, int last)
    {
        float sum = 0.0f;
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetSumOnInterval", "na is empty");
        if (first < 0) first = 0;
        if (first >= n || last < -1)  /* not an error */
            return 0.0f;
        if (last == -1)
            last = n - 1;
        last = Math.Min(last, n - 1);

        for (int i = first; i <= last; i++) {
            float val = GetFValue(i);
            sum += val;
        }
        return sum;
    }

    // leptonica: numaHasOnlyIntegers (numafunc1.c)
    /// <summary>Tests if all values are integers.</summary>
    /// <returns>true if all values are ints; else false</returns>
    public bool HasOnlyIntegers()
    {
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaHasOnlyIntegers", "na is empty");
        for (int i = 0; i < n; i++) {
            float val = GetFValue(i);
            if (val != (int)val)
                return false;
        }
        return true;
    }

    // leptonica: numaGetMean (numafunc1.c)
    /// <summary>Computes the average of the values.</summary>
    /// <returns>average of values</returns>
    public float GetMean()
    {
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetMean", "na is empty");

        float sum = GetSum();
        return sum / n;
    }

    // leptonica: numaGetMeanAbsval (numafunc1.c)
    /// <summary>Computes the average of the absolute values.</summary>
    /// <returns>average of absolute values</returns>
    public float GetMeanAbsval()
    {
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetMeanAbsval", "na is empty");

        Numa na1 = MakeAbsval(null, this);
        return na1.GetMean();
    }

    // leptonica: numaSubsample (numafunc1.c)
    /// <summary>Takes every subfactor-th value.</summary>
    /// <param name="subfactor">subsample factor, >= 1</param>
    /// <returns>nad evenly sampled values from this numa</returns>
    public Numa Subsample(int subfactor)
    {
        if (subfactor < 1)
            throw Lept.Arg("numaSubsample", "subfactor < 1");

        Numa nad = Create(0);
        int n;
        if ((n = Count) == 0)
            Log.Warning("numaSubsample", "nas is empty");
        for (int i = 0; i < n; i++) {
            if (i % subfactor != 0) continue;
            float val = GetFValue(i);
            nad.AddNumber(val);
        }

        return nad;
    }

    // leptonica: numaMakeDelta (numafunc1.c)
    /// <summary>Makes the numa of successive differences.</summary>
    /// <returns>numa of difference values val[i+1] - val[i]</returns>
    public Numa MakeDelta()
    {
        int n;
        if ((n = Count) < 2) {
            Log.Warning("numaMakeDelta", "n < 2; returning empty numa");
            return Create(1);
        }

        Numa nad = Create(n - 1);
        float prev = GetFValue(0);
        for (int i = 1; i < n; i++) {
            float cur = GetFValue(i);
            nad.AddNumber(cur - prev);
            prev = cur;
        }
        return nad;
    }

    // leptonica: numaMakeSequence (numafunc1.c)
    /// <summary>Makes a numa of evenly spaced values.</summary>
    /// <param name="startval">first value</param>
    /// <param name="increment">difference between successive values</param>
    /// <param name="size">of sequence</param>
    /// <returns>numa of sequence of evenly spaced values</returns>
    public static Numa MakeSequence(float startval, float increment, int size)
    {
        Numa na = Create(size);

        for (int i = 0; i < size; i++) {
            float val = startval + i * increment;
            na.AddNumber(val);
        }
        return na;
    }

    // leptonica: numaMakeConstant (numafunc1.c)
    /// <summary>Makes a numa with all entries equal to a value.</summary>
    /// <param name="val">value of every entry</param>
    /// <param name="size">of numa</param>
    /// <returns>numa of given size with all entries equal to 'val'</returns>
    public static Numa MakeConstant(float val, int size)
    {
        return MakeSequence(val, 0.0f, size);
    }

    // leptonica: numaMakeAbsval (numafunc1.c)
    /// <summary>Takes the absolute value of every number.</summary>
    /// <param name="nad">can be null for new array, or the same as nas for inplace</param>
    /// <param name="nas">input numa</param>
    /// <returns>nad with all numbers being the absval of the input</returns>
    public static Numa MakeAbsval(Numa? nad, Numa nas)
    {
        if (nad != null && nad != nas)
            throw Lept.Arg("numaMakeAbsval", "nad and not in-place");

        nad ??= nas.Copy();
        int n = nad.Count;
        for (int i = 0; i < n; i++) {
            float val = nad.array[i];
            nad.array[i] = (val < 0) ? -1 * val : val;   /* L_ABS */
        }

        return nad;
    }

    // leptonica: numaAddBorder (numafunc1.c)
    /// <summary>Adds a constant border at the start and the end.</summary>
    /// <param name="left">number of elements to add before the start</param>
    /// <param name="right">number of elements to add after the end</param>
    /// <param name="val">initialize border elements</param>
    /// <returns>nad with added elements at left and right</returns>
    public Numa AddBorder(int left, int right, float val)
    {
        if (left < 0) left = 0;
        if (right < 0) right = 0;
        if (left == 0 && right == 0)
            return Copy();

        int n = Count;
        int len = n + left + right;
        Numa nad = MakeConstant(val, len);
        GetParameters(out float startx, out float delx);
        nad.SetParameters(startx - delx * left, delx);
        float[] fas = GetFArray(L.L_NOCOPY);
        float[] fad = nad.GetFArray(L.L_NOCOPY);
        for (int i = 0; i < n; i++)
            fad[left + i] = fas[i];

        return nad;
    }

    // leptonica: numaAddSpecifiedBorder (numafunc1.c)
    /// <summary>Adds a border of continued or mirrored values.</summary>
    /// <param name="left">number of elements to add before the start</param>
    /// <param name="right">number of elements to add after the end</param>
    /// <param name="type">L_CONTINUED_BORDER, L_MIRRORED_BORDER</param>
    /// <returns>nad with added elements at left and right</returns>
    public Numa AddSpecifiedBorder(int left, int right, int type)
    {
        const string func = "numaAddSpecifiedBorder";

        if (left < 0) left = 0;
        if (right < 0) right = 0;
        if (left == 0 && right == 0)
            return Copy();
        if (type != L.L_CONTINUED_BORDER && type != L.L_MIRRORED_BORDER)
            throw Lept.Arg(func, "invalid type");
        int n = Count;
        if (type == L.L_MIRRORED_BORDER && (left > n || right > n))
            throw Lept.Arg(func, "border too large");

        Numa nad = AddBorder(left, right, 0);
        n = nad.Count;
        float[] fa = nad.GetFArray(L.L_NOCOPY);
        if (type == L.L_CONTINUED_BORDER) {
            for (int i = 0; i < left; i++)
                fa[i] = fa[left];
            for (int i = n - right; i < n; i++)
                fa[i] = fa[n - right - 1];
        } else {  /* type == L_MIRRORED_BORDER */
            for (int i = 0; i < left; i++)
                fa[i] = fa[2 * left - 1 - i];
            for (int i = 0; i < right; i++)
                fa[n - right + i] = fa[n - right - i - 1];
        }

        return nad;
    }

    // leptonica: numaRemoveBorder (numafunc1.c)
    /// <summary>Removes elements at the start and the end.</summary>
    /// <param name="left">number of elements to remove from the start</param>
    /// <param name="right">number of elements to remove up to the end</param>
    /// <returns>nad with removed elements at left and right</returns>
    public Numa RemoveBorder(int left, int right)
    {
        if (left < 0) left = 0;
        if (right < 0) right = 0;
        if (left == 0 && right == 0)
            return Copy();

        int n = Count;
        int len;
        if ((len = n - left - right) < 0)
            throw Lept.Arg("numaRemoveBorder", "len < 0 after removal");
        Numa nad = MakeConstant(0, len);
        GetParameters(out float startx, out float delx);
        nad.SetParameters(startx + delx * left, delx);
        float[] fas = GetFArray(L.L_NOCOPY);
        float[] fad = nad.GetFArray(L.L_NOCOPY);
        for (int i = 0; i < len; i++)
            fad[i] = fas[left + i];

        return nad;
    }

    // leptonica: numaCountNonzeroRuns (numafunc1.c)
    /// <summary>Counts the runs of positive values.</summary>
    /// <returns>number of nonzero runs; e.g., of pixel counts in rows or columns</returns>
    public int CountNonzeroRuns()
    {
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaCountNonzeroRuns", "na is empty");

        int count = 0;
        bool inrun = false;
        for (int i = 0; i < n; i++) {
            int val = GetIValue(i);
            if (!inrun && val > 0) {
                count++;
                inrun = true;
            } else if (inrun && val == 0) {
                inrun = false;
            }
        }
        return count;
    }

    // leptonica: numaGetNonzeroRange (numafunc1.c)
    /// <summary>Finds the interval of array indices where the values are nonzero.</summary>
    /// <param name="eps">largest value considered to be zero</param>
    /// <param name="first">first index of the nonzero interval</param>
    /// <param name="last">last index of the nonzero interval</param>
    /// <returns>true if a nonzero range is found; false otherwise (C: returns 1),
    /// in which case first = n - 1 and last = 0</returns>
    public bool GetNonzeroRange(float eps, out int first, out int last)
    {
        first = 0;
        last = 0;
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetNonzeroRange", "na is empty");

        bool found = false;
        int i;
        for (i = 0; i < n; i++) {
            float val = GetFValue(i);
            if (val > eps) {
                found = true;
                break;
            }
        }
        if (!found) {
            first = n - 1;
            last = 0;
            return false;
        }

        first = i;
        for (i = n - 1; i >= 0; i--) {
            float val = GetFValue(i);
            if (val > eps)
                break;
        }
        last = i;
        return true;
    }

    // leptonica: numaGetCountRelativeToZero (numafunc1.c)
    /// <summary>Counts the values less than, equal to or greater than zero.</summary>
    /// <param name="type">L_LESS_THAN_ZERO, L_EQUAL_TO_ZERO, L_GREATER_THAN_ZERO</param>
    /// <returns>count of values of given type</returns>
    public int GetCountRelativeToZero(int type)
    {
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetCountRelativeToZero", "na is empty");

        int count = 0;
        for (int i = 0; i < n; i++) {
            float val = GetFValue(i);
            if (type == L.L_LESS_THAN_ZERO && val < 0.0)
                count++;
            else if (type == L.L_EQUAL_TO_ZERO && val == 0.0)
                count++;
            else if (type == L.L_GREATER_THAN_ZERO && val > 0.0)
                count++;
        }
        return count;
    }

    // leptonica: numaClipToInterval (numafunc1.c)
    /// <summary>Clips the numa to an index interval.</summary>
    /// <param name="first">>= 0; &lt;= last</param>
    /// <param name="last">final index</param>
    /// <returns>numa with the same values as the input, but clipped
    /// to the specified interval</returns>
    /// <remarks>
    /// If you want the indices of the array values to be unchanged,
    /// use first = 0.
    /// Usage:
    /// This is useful to clip a histogram that has a few nonzero
    /// values to its nonzero range.
    /// </remarks>
    public Numa ClipToInterval(int first, int last)
    {
        const string func = "numaClipToInterval";

        int n;
        if ((n = Count) == 0)
            throw Lept.Arg(func, "nas is empty");
        if (first < 0 || first > last)
            throw Lept.Arg(func, "range not valid");
        if (first >= n)
            throw Lept.Arg(func, "no elements in range");

        last = Math.Min(last, n - 1);
        Numa nad = Create(last - first + 1);
        for (int i = first; i <= last; i++) {
            float val = GetFValue(i);
            nad.AddNumber(val);
        }
        GetParameters(out float startx, out float delx);
        nad.SetParameters(startx + first * delx, delx);
        return nad;
    }

    // leptonica: numaMakeThresholdIndicator (numafunc1.c)
    /// <summary>Makes an indicator numa from a threshold test.</summary>
    /// <param name="thresh">threshold value</param>
    /// <param name="type">L_SELECT_IF_LT, L_SELECT_IF_GT, L_SELECT_IF_LTE, L_SELECT_IF_GTE</param>
    /// <returns>nad : indicator array: values are 0 and 1</returns>
    /// <remarks>
    /// (1) For each element in nas, if the constraint given by 'type'
    ///     correctly specifies its relation to thresh, a value of 1
    ///     is recorded in nad.
    /// </remarks>
    public Numa MakeThresholdIndicator(float thresh, int type)
    {
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaMakeThresholdIndicator", "nas is empty");

        Numa nai = Create(n);
        for (int i = 0; i < n; i++) {
            float fval = GetFValue(i);
            int ival = 0;
            switch (type) {
            case L.L_SELECT_IF_LT:
                if (fval < thresh) ival = 1;
                break;
            case L.L_SELECT_IF_GT:
                if (fval > thresh) ival = 1;
                break;
            case L.L_SELECT_IF_LTE:
                if (fval <= thresh) ival = 1;
                break;
            case L.L_SELECT_IF_GTE:
                if (fval >= thresh) ival = 1;
                break;
            default:
                throw Lept.Arg("numaMakeThresholdIndicator", "invalid type");
            }
            nai.AddNumber(ival);
        }

        return nai;
    }

    // leptonica: numaUniformSampling (numafunc1.c)
    /// <summary>Resamples the values in nsamp equal divisions.</summary>
    /// <param name="nsamp">number of samples</param>
    /// <returns>nad : resampled array</returns>
    /// <remarks>
    /// (1) This resamples the values in the array, using %nsamp
    ///     equal divisions.
    /// </remarks>
    public Numa UniformSampling(int nsamp)
    {
        const string func = "numaUniformSampling";

        int n;
        if ((n = Count) == 0)
            throw Lept.Arg(func, "nas is empty");
        if (nsamp <= 0)
            throw Lept.Arg(func, "nsamp must be > 0");

        Numa nad = Create(nsamp);
        float[] array = GetFArray(L.L_NOCOPY);
        float binsize = (float)n / (float)nsamp;
        GetParameters(out float startx, out float delx);
        nad.SetParameters(startx, binsize * delx);
        float left = 0.0f;
        for (int i = 0; i < nsamp; i++) {
            float sum = 0.0f;
            float right = left + binsize;
            int ileft = (int)left;
            float lfract = 1.0f - left + ileft;
            /* NOTE(upstream): on a left bin boundary the fraction of the left sample
             * covered is 1.0, but it is set to 0.0, so that sample is dropped. */
            if (lfract >= 1.0)  /* on left bin boundary */
                lfract = 0.0f;
            int iright = (int)right;
            float rfract = right - iright;
            iright = Math.Min(iright, n - 1);
            if (ileft == iright) {  /* both are within the same original sample */
                sum += (lfract + rfract - 1.0f) * array[ileft];
            } else {
                if (lfract > 0.0001)  /* left fraction */
                    sum += lfract * array[ileft];
                if (rfract > 0.0001)  /* right fraction */
                    sum += rfract * array[iright];
                for (int j = ileft + 1; j < iright; j++)  /* entire pixels */
                    sum += array[j];
            }

            nad.AddNumber(sum);
            left = right;
        }
        return nad;
    }

    // leptonica: numaReverse (numafunc1.c)
    /// <summary>Reverses the order of the values, and the startx and delx fields.</summary>
    /// <param name="nad">[optional] can be null or equal to nas</param>
    /// <param name="nas">input numa</param>
    /// <returns>nad : reversed</returns>
    /// <remarks>
    /// (1) Usage:
    ///       Numa.Reverse(nas, nas);        // in-place
    ///       nad = Numa.Reverse(null, nas); // makes a new one
    /// </remarks>
    public static Numa Reverse(Numa? nad, Numa nas)
    {
        if (nad != null && nas != nad)
            throw Lept.Arg("numaReverse", "nad defined but != nas");

        int n = nas.Count;
        if (nad != null) {  /* in-place */
            for (int i = 0; i < n / 2; i++) {
                float val1 = nad.GetFValue(i);
                float val2 = nad.GetFValue(n - i - 1);
                nad.SetValue(i, val2);
                nad.SetValue(n - i - 1, val1);
            }
        } else {
            nad = Create(n);
            for (int i = n - 1; i >= 0; i--) {
                float val1 = nas.GetFValue(i);
                nad.AddNumber(val1);
            }
        }

            /* Reverse the startx and delx fields */
        nad.startx = nas.startx + (n - 1) * nas.delx;
        nad.delx = -nas.delx;
        return nad;
    }

    /*----------------------------------------------------------------------*
     *                       Signal feature extraction                      *
     *----------------------------------------------------------------------*/
    // leptonica: numaLowPassIntervals (numafunc1.c)
    /// <summary>Finds the intervals where the value is below a fraction of the maximum.</summary>
    /// <param name="thresh">threshold fraction of max; in [0.0 ... 1.0]</param>
    /// <param name="maxn">for normalizing; set maxn = 0.0 to use the max in nas</param>
    /// <returns>nad : interval abscissa pairs; the first entry is the max value</returns>
    /// <remarks>
    /// (1) For each interval where the value is less than a specified
    ///     fraction of the maximum, this records the left and right "x"
    ///     value.
    /// </remarks>
    public Numa LowPassIntervals(float thresh, float maxn)
    {
        const string func = "numaLowPassIntervals";

        int n;
        if ((n = Count) == 0)
            throw Lept.Arg(func, "nas is empty");
        if (thresh < 0.0 || thresh > 1.0)
            throw Lept.Arg(func, "invalid thresh");

            /* The input threshold is a fraction of the max.
             * The first entry in nad is the value of the max. */
        float maxval;
        if (maxn == 0.0)
            GetMax(out maxval, out _);
        else
            maxval = maxn;
        GetParameters(out float startx, out float delx);
        float threshval = thresh * maxval;
        Numa nad = Create(0);
        nad.AddNumber(maxval);

            /* Write pairs of pts (x0, x1) for the intervals */
        bool inrun = false;
        float x0 = 0.0f, x1;
        for (int i = 0; i < n; i++) {
            float fval = GetFValue(i);
            if (fval < threshval && inrun == false) {  /* start a new run */
                inrun = true;
                x0 = startx + i * delx;
            } else if (fval > threshval && inrun == true) {  /* end the run */
                inrun = false;
                x1 = startx + i * delx;
                nad.AddNumber(x0);
                nad.AddNumber(x1);
            }
        }
        if (inrun == true) {  /* must end the last run */
            x1 = startx + (n - 1) * delx;
            nad.AddNumber(x0);
            nad.AddNumber(x1);
        }

        return nad;
    }

    // leptonica: numaThresholdEdges (numafunc1.c)
    /// <summary>Finds the rising and falling edges between two thresholds.</summary>
    /// <param name="thresh1">low threshold as fraction of max; in [0.0 ... 1.0]</param>
    /// <param name="thresh2">high threshold as fraction of max; in [0.0 ... 1.0]</param>
    /// <param name="maxn">for normalizing; set maxn = 0.0 to use the max in nas</param>
    /// <returns>nad edge interval triplets; the first entry is the max value</returns>
    /// <remarks>
    /// (1) For each edge interval, where the value is less
    ///     than %thresh1 on one side, greater than %thresh2 on
    ///     the other, and between these thresholds throughout the
    ///     interval, this records a triplet of values: the
    ///     'left' and 'right' edges, and either +1 or -1, depending
    ///     on whether the edge is rising or falling.
    /// (2) No assumption is made about the value outside the array,
    ///     so if the value at the array edge is between the threshold
    ///     values, it is not considered part of an edge.  We start
    ///     looking for edge intervals only after leaving the thresholded
    ///     band.
    /// </remarks>
    public Numa ThresholdEdges(float thresh1, float thresh2, float maxn)
    {
        const string func = "numaThresholdEdges";

        int n;
        if ((n = Count) == 0)
            throw Lept.Arg(func, "nas is empty");
        if (thresh1 < 0.0 || thresh1 > 1.0 || thresh2 < 0.0 || thresh2 > 1.0)
            throw Lept.Arg(func, "invalid thresholds");
        if (thresh2 < thresh1)
            throw Lept.Arg(func, "thresh2 < thresh1");

            /* The input thresholds are fractions of the max.
             * The first entry in nad is the value of the max used
             * here for normalization. */
        float maxval;
        if (maxn == 0.0)
            GetMax(out maxval, out _);
        else
            maxval = maxn;
        /* NOTE(upstream): the max is determined again here, so %maxn has no effect. */
        GetMax(out maxval, out _);
        GetParameters(out float startx, out float delx);
        float threshval1 = thresh1 * maxval;
        float threshval2 = thresh2 * maxval;
        Numa nad = Create(0);
        nad.AddNumber(maxval);

            /* Write triplets of pts (x0, x1, sign) for the edges.
             * First make sure we start search from outside the band.
             * Only one of {belowlast, abovelast} is true. */
        int istart = 0;
        bool belowlast = false, abovelast = false;
        for (int i = 0; i < n; i++) {
            istart = i;
            float fval = GetFValue(i);
            belowlast = (fval < threshval1) ? true : false;
            abovelast = (fval > threshval2) ? true : false;
            if (belowlast == true || abovelast == true)
                break;
        }
        /* NOTE(upstream): istart is n - 1, not n, when the loop finds no value outside
         * the band, so this test never succeeds; the code below then finds no edge. */
        if (istart == n)  /* no intervals found */
            return nad;

            /* x0 and x1 can only be set from outside the edge.
             * They are the values just before entering the band,
             * and just after entering the band.  We can jump through
             * the band, in which case they differ by one index in nas. */
        bool inband = false;
        bool startbelow = belowlast; /* one of these is true */
        bool output = false;
        float x0 = startx + istart * delx;
        float x1 = 0.0f;
        int sign = 0;
        for (int i = istart + 1; i < n; i++) {
            float fval = GetFValue(i);
            bool below = (fval < threshval1) ? true : false;
            bool above = (fval > threshval2) ? true : false;
            if (!inband && belowlast && above) {  /* full jump up */
                x1 = startx + i * delx;
                sign = 1;
                startbelow = false;  /* for the next transition */
                output = true;
            } else if (!inband && abovelast && below) {  /* full jump down */
                x1 = startx + i * delx;
                sign = -1;
                startbelow = true;  /* for the next transition */
                output = true;
            } else if (inband && startbelow && above) {  /* exit rising; success */
                x1 = startx + i * delx;
                sign = 1;
                inband = false;
                startbelow = false;  /* for the next transition */
                output = true;
            } else if (inband && !startbelow && below) {
                    /* exit falling; success */
                x1 = startx + i * delx;
                sign = -1;
                inband = false;
                startbelow = true;  /* for the next transition */
                output = true;
            } else if (inband && !startbelow && above) {  /* exit rising; failure */
                x0 = startx + i * delx;
                inband = false;
            } else if (inband && startbelow && below) {  /* exit falling; failure */
                x0 = startx + i * delx;
                inband = false;
            } else if (!inband && !above && !below) {  /* enter */
                inband = true;
                startbelow = belowlast;
            } else if (!inband && (above || below)) {  /* outside and remaining */
                x0 = startx + i * delx;  /* update position */
            }
            belowlast = below;
            abovelast = above;
            if (output) {  /* we have exited; save new x0 */
                nad.AddNumber(x0);
                nad.AddNumber(x1);
                nad.AddNumber(sign);
                output = false;
                x0 = startx + i * delx;
            }
        }

        return nad;
    }

    // leptonica: numaGetSpanValues (numafunc1.c)
    /// <summary>Gets the start and end of a span in the output of numaLowPassIntervals().</summary>
    /// <param name="span">span number, zero-based</param>
    /// <param name="start">location of start of transition</param>
    /// <param name="end">location of end of transition</param>
    public void GetSpanValues(int span, out int start, out int end)
    {
        start = 0;
        end = 0;
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetSpanValues", "na is empty");
        if (n % 2 != 1)
            throw Lept.Arg("numaGetSpanValues", "n is not odd");
        int nspans = n / 2;
        if (nspans < 0 || span >= nspans)
            throw Lept.Arg("numaGetSpanValues", "invalid span");

        start = GetIValue(2 * span + 1);
        end = GetIValue(2 * span + 2);
    }

    // leptonica: numaGetEdgeValues (numafunc1.c)
    /// <summary>Gets the start, end and sign of an edge in the output of numaThresholdEdges().</summary>
    /// <param name="edge">edge number, zero-based</param>
    /// <param name="start">location of start of transition</param>
    /// <param name="end">location of end of transition</param>
    /// <param name="sign">transition sign: +1 is rising, -1 is falling</param>
    public void GetEdgeValues(int edge, out int start, out int end, out int sign)
    {
        start = 0;
        end = 0;
        sign = 0;
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetEdgeValues", "na is empty");
        if (n % 3 != 1)
            throw Lept.Arg("numaGetEdgeValues", "n % 3 is not 1");
        int nedges = (n - 1) / 3;
        if (edge < 0 || edge >= nedges)
            throw Lept.Arg("numaGetEdgeValues", "invalid edge");

        start = GetIValue(3 * edge + 1);
        end = GetIValue(3 * edge + 2);
        sign = GetIValue(3 * edge + 3);
    }

    /*----------------------------------------------------------------------*
     *                             Interpolation                            *
     *----------------------------------------------------------------------*/
    // leptonica: numaInterpolateEqxVal (numafunc1.c)
    /// <summary>Interpolates a value of a function given at equal x intervals.</summary>
    /// <param name="startx">xval corresponding to first element in array</param>
    /// <param name="deltax">x increment between array elements</param>
    /// <param name="nay">numa of ordinate values, assumed equally spaced</param>
    /// <param name="type">L_LINEAR_INTERP, L_QUADRATIC_INTERP</param>
    /// <param name="xval">abscissa where the function is evaluated</param>
    /// <returns>interpolated value; throws if xval is outside the range</returns>
    /// <remarks>
    /// (1) Considering nay as a function of x, the x values
    ///     are equally spaced
    /// (2) Caller should check for valid return.
    ///
    /// For linear Lagrangian interpolation (through 2 data pts):
    ///        y(x) = y1(x-x2)/(x1-x2) + y2(x-x1)/(x2-x1)
    ///
    /// For quadratic Lagrangian interpolation (through 3 data pts):
    ///        y(x) = y1(x-x2)(x-x3)/((x1-x2)(x1-x3)) +
    ///               y2(x-x1)(x-x3)/((x2-x1)(x2-x3)) +
    ///               y3(x-x1)(x-x2)/((x3-x1)(x3-x2))
    /// </remarks>
    public static float InterpolateEqxVal(float startx, float deltax, Numa nay, int type,
                                          float xval)
    {
        string? error = InterpolateEqxValImpl(startx, deltax, nay, type, xval, out float yval);
        if (error != null)
            throw Lept.Arg("numaInterpolateEqxVal", error);
        return yval;
    }

    /* The body of numaInterpolateEqxVal(), with the error as a return value, because
     * numaInterpolateEqxInterval() carries on after an error. */
    private static string? InterpolateEqxValImpl(float startx, float deltax, Numa nay, int type,
                                                 float xval, out float yval)
    {
        yval = 0.0f;
        if (deltax <= 0.0)
            return "deltax not > 0";
        if (type != L.L_LINEAR_INTERP && type != L.L_QUADRATIC_INTERP)
            return "invalid interp type";
        int n;
        if ((n = nay.Count) < 2)
            return "not enough points";
        if (type == L.L_QUADRATIC_INTERP && n == 2) {
            type = L.L_LINEAR_INTERP;
            Log.Warning("numaInterpolateEqxVal", "only 2 points; using linear interp");
        }
        float maxx = startx + deltax * (n - 1);
        if (xval < startx || xval > maxx)
            return "xval is out of bounds";

        float[] fa = nay.GetFArray(L.L_NOCOPY);
        float fi = (xval - startx) / deltax;
        int i = (int)fi;
        float del = fi - i;
        if (del == 0.0) {  /* no interpolation required */
            yval = fa[i];
            return null;
        }

        if (type == L.L_LINEAR_INTERP) {
            yval = fa[i] + del * (fa[i + 1] - fa[i]);
            return null;
        }

            /* Quadratic interpolation */
        float d1, d3;
        d1 = d3 = 0.5f / (deltax * deltax);
        float d2 = -2.0f * d1;
        int i1, i2, i3;
        if (i == 0) {
            i1 = i;
            i2 = i + 1;
            i3 = i + 2;
        } else {
            i1 = i - 1;
            i2 = i;
            i3 = i + 1;
        }
        float x1 = startx + i1 * deltax;
        float x2 = startx + i2 * deltax;
        float x3 = startx + i3 * deltax;
        float fy1 = d1 * fa[i1];
        float fy2 = d2 * fa[i2];
        float fy3 = d3 * fa[i3];
        yval = fy1 * (xval - x2) * (xval - x3) +
               fy2 * (xval - x1) * (xval - x3) +
               fy3 * (xval - x1) * (xval - x2);
        return null;
    }

    // leptonica: numaInterpolateArbxVal (numafunc1.c)
    /// <summary>Interpolates a value of a function given at arbitrary x values.</summary>
    /// <param name="nay">numa of ordinate values, corresponding to this numa (nax)</param>
    /// <param name="type">L_LINEAR_INTERP, L_QUADRATIC_INTERP</param>
    /// <param name="xval">abscissa where the function is evaluated</param>
    /// <returns>interpolated value; throws if xval is outside the range</returns>
    /// <remarks>
    /// (1) The values in nax must be sorted in increasing order.
    ///     If, additionally, they are equally spaced, you can use
    ///     numaInterpolateEqxVal().
    /// (2) Caller should check for valid return.
    /// (3) Uses lagrangian interpolation.  See numaInterpolateEqxVal()
    ///     for formulas.
    /// </remarks>
    public float InterpolateArbxVal(Numa nay, int type, float xval)
    {
        const string func = "numaInterpolateArbxVal";
        Numa nax = this;

        if (type != L.L_LINEAR_INTERP && type != L.L_QUADRATIC_INTERP)
            throw Lept.Arg(func, "invalid interp type");
        int ny = nay.Count;
        int nx = nax.Count;
        if (nx != ny)
            throw Lept.Arg(func, "nax and nay not same size arrays");
        if (ny < 2)
            throw Lept.Arg(func, "not enough points");
        if (type == L.L_QUADRATIC_INTERP && ny == 2) {
            type = L.L_LINEAR_INTERP;
            Log.Warning(func, "only 2 points; using linear interp");
        }
        float minx = nax.GetFValue(0);
        float maxx = nax.GetFValue(nx - 1);
        if (xval < minx || xval > maxx)
            throw Lept.Arg(func, "xval is out of bounds");

        float[] fax = nax.GetFArray(L.L_NOCOPY);
        float[] fay = nay.GetFArray(L.L_NOCOPY);

            /* Linear search for interval.  We are guaranteed
             * to either return or break out of the loop.
             * In addition, we are assured that fax[i] - fax[im] > 0.0 */
        if (xval == fax[0])
            return fay[0];
        int im = 0;
        float dell = 0.0f;
        int i;
        for (i = 1; i < nx; i++) {
            float delu = fax[i] - xval;
            if (delu >= 0.0) {  /* we've passed it */
                if (delu == 0.0)
                    return fay[i];
                im = i - 1;
                dell = xval - fax[im];  /* >= 0 */
                break;
            }
        }
        float fract = dell / (fax[i] - fax[im]);

        /* NOTE(upstream): fract is the position in [fax[im], fax[i]], but the linear
         * interpolation is done between fay[i] and fay[i + 1]. */
        if (type == L.L_LINEAR_INTERP)
            return fay[i] + fract * (fay[i + 1] - fay[i]);

            /* Quadratic interpolation */
        int i1, i2, i3;
        if (im == 0) {
            i1 = im;
            i2 = im + 1;
            i3 = im + 2;
        } else {
            i1 = im - 1;
            i2 = im;
            i3 = im + 1;
        }
        float d1 = (fax[i1] - fax[i2]) * (fax[i1] - fax[i3]);
        float d2 = (fax[i2] - fax[i1]) * (fax[i2] - fax[i3]);
        float d3 = (fax[i3] - fax[i1]) * (fax[i3] - fax[i2]);
        return fay[i1] * (xval - fax[i2]) * (xval - fax[i3]) / d1 +
               fay[i2] * (xval - fax[i1]) * (xval - fax[i3]) / d2 +
               fay[i3] * (xval - fax[i1]) * (xval - fax[i2]) / d3;
    }

    // leptonica: numaInterpolateEqxInterval (numafunc1.c)
    /// <summary>Interpolates the values of a function given at equal x intervals over an interval.</summary>
    /// <param name="startx">xval corresponding to first element in nas</param>
    /// <param name="deltax">x increment between array elements in nas</param>
    /// <param name="nasy">numa of ordinate values, assumed equally spaced</param>
    /// <param name="type">L_LINEAR_INTERP, L_QUADRATIC_INTERP</param>
    /// <param name="x0">start value of interval</param>
    /// <param name="x1">end value of interval</param>
    /// <param name="npts">number of points to evaluate function in interval</param>
    /// <param name="nax">array of x values in interval</param>
    /// <param name="nay">array of y values in interval</param>
    /// <remarks>
    /// (1) Considering nasy as a function of x, the x values
    ///     are equally spaced.
    /// (2) This creates nay (and optionally nax) of interpolated
    ///     values over the specified interval (x0, x1).
    /// (3) If the interval (x0, x1) lies partially outside the array
    ///     nasy (as interpreted by startx and deltax), it is an
    ///     error and returns 1.
    /// (4) Note that deltax is the intrinsic x-increment for the input
    ///     array nasy, whereas delx is the intrinsic x-increment for the
    ///     output interpolated array nay.
    /// </remarks>
    public static void InterpolateEqxInterval(float startx, float deltax, Numa nasy, int type,
                                              float x0, float x1, int npts,
                                              out Numa nax, out Numa nay)
    {
        const string func = "numaInterpolateEqxInterval";

        int n;
        if ((n = nasy.Count) < 2)
            throw Lept.Arg(func, "n < 2");
        if (deltax <= 0.0)
            throw Lept.Arg(func, "deltax not > 0");
        if (type != L.L_LINEAR_INTERP && type != L.L_QUADRATIC_INTERP)
            throw Lept.Arg(func, "invalid interp type");
        if (type == L.L_QUADRATIC_INTERP && n == 2) {
            type = L.L_LINEAR_INTERP;
            Log.Warning(func, "only 2 points; using linear interp");
        }
        float maxx = startx + deltax * (n - 1);
        if (x0 < startx || x1 > maxx || x1 <= x0)
            throw Lept.Arg(func, "[x0 ... x1] is not valid");
        if (npts < 3)
            throw Lept.Arg(func, "npts < 3");
        float delx = (x1 - x0) / (float)(npts - 1);  /* delx is for output nay */

        nay = Create(npts);
        nay.SetParameters(x0, delx);
        nax = Create(npts);

        for (int i = 0; i < npts; i++) {
            float x = x0 + i * delx;
            nax.AddNumber(x);
            /* The C code ignores an error here (e.g., x beyond the last point because of
             * roundoff at the end of the interval); yval is then 0.0. */
            string? error = InterpolateEqxValImpl(startx, deltax, nasy, type, x, out float yval);
            if (error != null)
                Log.Error("numaInterpolateEqxVal", error);
            nay.AddNumber(yval);
        }
    }

    // leptonica: numaInterpolateArbxInterval (numafunc1.c)
    /// <summary>Interpolates the values of a function given at arbitrary x values over an interval.</summary>
    /// <param name="nay">numa of ordinate values, corresponding to this numa (nax)</param>
    /// <param name="type">L_LINEAR_INTERP, L_QUADRATIC_INTERP</param>
    /// <param name="x0">start value of interval</param>
    /// <param name="x1">end value of interval</param>
    /// <param name="npts">number of points to evaluate function in interval</param>
    /// <param name="nadx">array of x values in interval</param>
    /// <param name="nady">array of y values in interval</param>
    /// <remarks>
    /// (1) The values in nax must be sorted in increasing order.
    ///     If they are not sorted, we do it here, and complain.
    /// (2) If the values in nax are equally spaced, you can use
    ///     numaInterpolateEqxInterval().
    /// (3) Caller should check for valid return.
    /// (4) We don't call numaInterpolateArbxVal() for each output
    ///     point, because that requires an O(n) search for
    ///     each point.  Instead, we do a single O(n) pass through
    ///     nax, saving the indices to be used for each output yval.
    /// (5) Uses lagrangian interpolation.  See numaInterpolateEqxVal()
    ///     for formulas.
    /// </remarks>
    public void InterpolateArbxInterval(Numa nay, int type, float x0, float x1, int npts,
                                        out Numa nadx, out Numa nady)
    {
        const string func = "numaInterpolateArbxInterval";
        Numa nax = this;

        if (type != L.L_LINEAR_INTERP && type != L.L_QUADRATIC_INTERP)
            throw Lept.Arg(func, "invalid interp type");
        if (x0 > x1)
            throw Lept.Arg(func, "x0 > x1");
        int ny = nay.Count;
        int nx = nax.Count;
        if (nx != ny)
            throw Lept.Arg(func, "nax and nay not same size arrays");
        if (ny < 2)
            throw Lept.Arg(func, "not enough points");
        if (type == L.L_QUADRATIC_INTERP && ny == 2) {
            type = L.L_LINEAR_INTERP;
            Log.Warning(func, "only 2 points; using linear interp");
        }
        nax.GetMin(out float minx, out _);
        nax.GetMax(out float maxx, out _);
        if (x0 < minx || x1 > maxx)
            throw Lept.Arg(func, "xval is out of bounds");

            /* Make sure that nax is sorted in increasing order */
        Numa nasx, nasy;
        bool sorted = nax.IsSorted(L.L_SORT_INCREASING);
        if (!sorted) {
            Log.Warning(func, "we are sorting nax in increasing order");
            nax.SortPair(nay, L.L_SORT_INCREASING, out nasx, out nasy);
        } else {
            nasx = nax.Clone();
            nasy = nay.Clone();
        }

        float[] fax = nasx.GetFArray(L.L_NOCOPY);
        float[] fay = nasy.GetFArray(L.L_NOCOPY);

            /* Get array of indices into fax for interpolated locations */
        if (npts < 0)
            throw Lept.Fail(func, "ind not made");
        int[] index = new int[npts];
        float del = (x1 - x0) / (npts - 1.0f);
        for (int i = 0, j = 0; j < nx && i < npts; i++) {
            float xval = x0 + i * del;
            while (j < nx - 1 && xval > fax[j])
                j++;
            if (xval == fax[j])
                index[i] = Math.Min(j, nx - 1);
            else    /* the index of fax[] is just below xval */
                index[i] = Math.Max(j - 1, 0);
        }

            /* For each point to be interpolated, get the y value */
        nady = Create(npts);
        nadx = Create(npts);
        for (int i = 0; i < npts; i++) {
            float xval = x0 + i * del;
            nadx.AddNumber(xval);
            int im = index[i];
            float excess = xval - fax[im];
            if (excess == 0.0) {
                nady.AddNumber(fay[im]);
                continue;
            }
            float fract = excess / (fax[im + 1] - fax[im]);

            float yval;
            if (type == L.L_LINEAR_INTERP) {
                yval = fay[im] + fract * (fay[im + 1] - fay[im]);
                nady.AddNumber(yval);
                continue;
            }

                /* Quadratic interpolation */
            int i1, i2, i3;
            if (im == 0) {
                i1 = im;
                i2 = im + 1;
                i3 = im + 2;
            } else {
                i1 = im - 1;
                i2 = im;
                i3 = im + 1;
            }
            float d1 = (fax[i1] - fax[i2]) * (fax[i1] - fax[i3]);
            float d2 = (fax[i2] - fax[i1]) * (fax[i2] - fax[i3]);
            float d3 = (fax[i3] - fax[i1]) * (fax[i3] - fax[i2]);
            yval = fay[i1] * (xval - fax[i2]) * (xval - fax[i3]) / d1 +
                   fay[i2] * (xval - fax[i1]) * (xval - fax[i3]) / d2 +
                   fay[i3] * (xval - fax[i1]) * (xval - fax[i2]) / d3;
            nady.AddNumber(yval);
        }
    }

    // leptonica: numaFitMax (numafunc1.c)
    /// <summary>Fits a parabola through the maximum and its two neighbors.</summary>
    /// <param name="maxval">max value</param>
    /// <param name="naloc">[optional] associated numa of abscissa values</param>
    /// <param name="maxloc">abscissa value that gives max value in na;
    /// if naloc == null, this is given as an interpolated index value</param>
    /// <remarks>
    /// If %naloc is given, there is no requirement that the
    /// data points are evenly spaced.  Lagrangian interpolation
    /// handles that.  The only requirement is that the
    /// data points are ordered so that the values in naloc
    /// are either increasing or decreasing.  We test to make
    /// sure that the sizes of na and naloc are equal, and it
    /// is assumed that the correspondences %na[i] as a function
    /// of %naloc[i] are properly arranged for all i.
    ///
    /// The formula for Lagrangian interpolation through 3 data pts is:
    ///      y(x) = y1(x-x2)(x-x3)/((x1-x2)(x1-x3)) +
    ///             y2(x-x1)(x-x3)/((x2-x1)(x2-x3)) +
    ///             y3(x-x1)(x-x2)/((x3-x1)(x3-x2))
    ///
    /// Then the derivative, using the constants (c1,c2,c3) defined below,
    /// is set to 0:
    ///      y'(x) = 2x(c1+c2+c3) - c1(x2+x3) - c2(x1+x3) - c3(x1+x2) = 0
    /// </remarks>
    public void FitMax(out float maxval, Numa? naloc, out float maxloc)
    {
        maxval = 0.0f;
        maxloc = 0.0f;
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaFitMax", "na is empty");
        if (naloc != null) {
            if (n != naloc.Count)
                throw Lept.Arg("numaFitMax", "na and naloc of unequal size");
        }

        GetMax(out float smaxval, out int imaxloc);  /* start value of maximum sample, before interpolating */

            /* Simple case: max is at end point */
        float val;
        if (imaxloc == 0 || imaxloc == n - 1) {
            maxval = smaxval;
            if (naloc != null) {
                val = naloc.GetFValue(imaxloc);
                maxloc = val;
            } else {
                maxloc = imaxloc;
            }
            return;
        }

            /* Interior point; use quadratic interpolation */
        float x1, x2, x3;
        float y2 = smaxval;
        val = GetFValue(imaxloc - 1);
        float y1 = val;
        val = GetFValue(imaxloc + 1);
        float y3 = val;
        if (naloc != null) {
            val = naloc.GetFValue(imaxloc - 1);
            x1 = val;
            val = naloc.GetFValue(imaxloc);
            x2 = val;
            val = naloc.GetFValue(imaxloc + 1);
            x3 = val;
        } else {
            x1 = imaxloc - 1;
            x2 = imaxloc;
            x3 = imaxloc + 1;
        }

            /* Can't interpolate; just use the max val in na
             * and the corresponding one in naloc */
        if (x1 == x2 || x1 == x3 || x2 == x3) {
            maxval = y2;
            maxloc = x2;
            return;
        }

            /* Use lagrangian interpolation; set dy/dx = 0 */
        float c1 = y1 / ((x1 - x2) * (x1 - x3));
        float c2 = y2 / ((x2 - x1) * (x2 - x3));
        float c3 = y3 / ((x3 - x1) * (x3 - x2));
        float a = c1 + c2 + c3;
        float b = c1 * (x2 + x3) + c2 * (x1 + x3) + c3 * (x1 + x2);
        float xmax = b / (2 * a);
        float ymax = c1 * (xmax - x2) * (xmax - x3) +
                     c2 * (xmax - x1) * (xmax - x3) +
                     c3 * (xmax - x1) * (xmax - x2);
        maxval = ymax;
        maxloc = xmax;
    }

    // leptonica: numaDifferentiateInterval (numafunc1.c)
    /// <summary>Computes the derivative of a function given at arbitrary x values over an interval.</summary>
    /// <param name="nay">numa of ordinate values, corresponding to this numa (nax)</param>
    /// <param name="x0">start value of interval</param>
    /// <param name="x1">end value of interval</param>
    /// <param name="npts">number of points to evaluate function in interval</param>
    /// <param name="nadx">array of x values in interval</param>
    /// <param name="nady">array of derivatives in interval</param>
    /// <remarks>
    /// (1) The values in nax must be sorted in increasing order.
    ///     If they are not sorted, it is done in the interpolation
    ///     step, and a warning is issued.
    /// (2) Caller should check for valid return.
    /// </remarks>
    public void DifferentiateInterval(Numa nay, float x0, float x1, int npts,
                                      out Numa nadx, out Numa nady)
    {
        const string func = "numaDifferentiateInterval";
        Numa nax = this;

        if (x0 > x1)
            throw Lept.Arg(func, "x0 > x1");
        int ny = nay.Count;
        int nx = nax.Count;
        if (nx != ny)
            throw Lept.Arg(func, "nax and nay not same size arrays");
        if (ny < 2)
            throw Lept.Arg(func, "not enough points");
        nax.GetMin(out float minx, out _);
        nax.GetMax(out float maxx, out _);
        if (x0 < minx || x1 > maxx)
            throw Lept.Arg(func, "xval is out of bounds");
        if (npts < 2)
            throw Lept.Arg(func, "npts < 2");

            /* Generate interpolated array over specified interval */
        nax.InterpolateArbxInterval(nay, L.L_LINEAR_INTERP, x0, x1, npts, out nadx, out Numa naiy);

        nady = Create(npts);
        float invdel = 0.5f * ((float)npts - 1.0f) / (x1 - x0);
        float[] fay = naiy.GetFArray(L.L_NOCOPY);

            /* Compute and save derivatives */
        float der = 0.5f * invdel * (fay[1] - fay[0]);
        nady.AddNumber(der);
        for (int i = 1; i < npts - 1; i++) {
            der = invdel * (fay[i + 1] - fay[i - 1]);
            nady.AddNumber(der);
        }
        der = 0.5f * invdel * (fay[npts - 1] - fay[npts - 2]);
        nady.AddNumber(der);
    }

    // leptonica: numaIntegrateInterval (numafunc1.c)
    /// <summary>Computes the integral of a function given at arbitrary x values over an interval.</summary>
    /// <param name="nay">numa of ordinate values, corresponding to this numa (nax)</param>
    /// <param name="x0">start value of interval</param>
    /// <param name="x1">end value of interval</param>
    /// <param name="npts">number of points to evaluate function in interval</param>
    /// <returns>integral of function over interval</returns>
    /// <remarks>
    /// (1) The values in nax must be sorted in increasing order.
    ///     If they are not sorted, it is done in the interpolation
    ///     step, and a warning is issued.
    /// (2) Caller should check for valid return.
    /// </remarks>
    public float IntegrateInterval(Numa nay, float x0, float x1, int npts)
    {
        const string func = "numaIntegrateInterval";
        Numa nax = this;

        if (x0 > x1)
            throw Lept.Arg(func, "x0 > x1");
        if (npts < 2)
            throw Lept.Arg(func, "npts < 2");
        int ny = nay.Count;
        int nx = nax.Count;
        if (nx != ny)
            throw Lept.Arg(func, "nax and nay not same size arrays");
        if (ny < 2)
            throw Lept.Arg(func, "not enough points");
        nax.GetMin(out float minx, out _);
        nax.GetMax(out float maxx, out _);
        if (x0 < minx || x1 > maxx)
            throw Lept.Arg(func, "xval is out of bounds");

            /* Generate interpolated array over specified interval */
        nax.InterpolateArbxInterval(nay, L.L_LINEAR_INTERP, x0, x1, npts, out _, out Numa naiy);

        float del = (x1 - x0) / ((float)npts - 1.0f);
        float[] fay = naiy.GetFArray(L.L_NOCOPY);

            /* Compute integral (simple trapezoid) */
        float sum = 0.5f * (fay[0] + fay[npts - 1]);
        for (int i = 1; i < npts - 1; i++)
            sum += fay[i];
        return del * sum;
    }

    /*----------------------------------------------------------------------*
     *                                Sorting                               *
     *----------------------------------------------------------------------*/
    // leptonica: numaSortGeneral (numafunc1.c)
    /// <summary>Sorts the numa and optionally returns the index maps.</summary>
    /// <param name="nasort">sorted numa</param>
    /// <param name="naindex">index of elements in na associated with each element of nasort</param>
    /// <param name="nainvert">index of elements in nasort associated with each element of na</param>
    /// <param name="sortorder">L_SORT_INCREASING or L_SORT_DECREASING</param>
    /// <param name="sorttype">L_SHELL_SORT or L_BIN_SORT</param>
    /// <remarks>
    /// (1) Sorting can be confusing.  Here's an array of five values with
    ///     the results shown for the 3 output arrays.
    ///
    ///     na      nasort   naindex   nainvert
    ///     -----------------------------------
    ///     3         9         2         3
    ///     4         6         3         2
    ///     9         4         1         0
    ///     6         3         0         1
    ///     1         1         4         4
    ///
    ///     Note that naindex is a LUT into na for the sorted array values,
    ///     and nainvert directly gives the sorted index values for the
    ///     input array.  It is useful to view naindex is as a map:
    ///            0  --&gt;  2
    ///            1  --&gt;  3
    ///            2  --&gt;  1
    ///            3  --&gt;  0
    ///            4  --&gt;  4
    ///     and nainvert, the inverse of this map:
    ///            0  --&gt;  3
    ///            1  --&gt;  2
    ///            2  --&gt;  0
    ///            3  --&gt;  1
    ///            4  --&gt;  4
    ///
    ///     We can write these relations symbolically as:
    ///         nasort[i] = na[naindex[i]]
    ///         na[i] = nasort[nainvert[i]]
    /// </remarks>
    public void SortGeneral(out Numa nasort, out Numa naindex, out Numa nainvert,
                            int sortorder, int sorttype)
    {
        const string func = "numaSortGeneral";
        Numa na = this;

        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg(func, "invalid sort order");
        if (sorttype != L.L_SHELL_SORT && sorttype != L.L_BIN_SORT)
            throw Lept.Arg(func, "invalid sort type");

        Numa? index = null;
        if (sorttype == L.L_BIN_SORT) {
            /* The C code ignores the error of numaGetMax() for an empty numa, which
             * leaves size = 0.0 */
            float size = 0.0f;
            if (na.Count > 0)
                na.GetMax(out size, out _);
            int isize = (int)size;
            if ((uint)isize > L.MaxInitPtraSize - 1) {  /* l_int32 compared to l_uint32 */
                Log.Warning(func, "array too large; using shell sort");
                sorttype = L.L_SHELL_SORT;
            } else {
                index = na.GetBinSortIndex(sortorder);
            }
        }

        if (sorttype == L.L_SHELL_SORT)
            index = na.GetSortIndex(sortorder);

        nasort = na.SortByIndex(index!);
        nainvert = index!.InvertMap();
        naindex = index;
    }

    // leptonica: numaSortAutoSelect (numafunc1.c)
    /// <summary>Sorts the numa by a shell sort or a bin sort, whichever is better suited.</summary>
    /// <param name="sortorder">L_SORT_INCREASING or L_SORT_DECREASING</param>
    /// <returns>naout output sorted numa</returns>
    /// <remarks>
    /// (1) This does either a shell sort or a bin sort, depending on
    ///     the number of elements in nas and the dynamic range.
    /// </remarks>
    public Numa SortAutoSelect(int sortorder)
    {
        const string func = "numaSortAutoSelect";

        if (Count == 0) {
            Log.Warning(func, "nas is empty; returning copy");
            return Copy();
        }
        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg(func, "invalid sort order");

        int type = ChooseSortType();
        if (type != L.L_SHELL_SORT && type != L.L_BIN_SORT)
            throw Lept.Arg(func, "invalid sort type");

        if (type == L.L_BIN_SORT)
            return BinSort(sortorder);
        else  /* shell sort */
            return Sort(null, this, sortorder);
    }

    // leptonica: numaSortIndexAutoSelect (numafunc1.c)
    /// <summary>Gets the sort index by a shell sort or a bin sort, whichever is better suited.</summary>
    /// <param name="sortorder">L_SORT_INCREASING or L_SORT_DECREASING</param>
    /// <returns>nad indices of nas, sorted by value in nas</returns>
    /// <remarks>
    /// (1) This does either a shell sort or a bin sort, depending on
    ///     the number of elements in nas and the dynamic range.
    /// </remarks>
    public Numa SortIndexAutoSelect(int sortorder)
    {
        const string func = "numaSortIndexAutoSelect";

        if (Count == 0) {
            Log.Warning(func, "nas is empty; returning copy");
            return Copy();
        }
        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg(func, "invalid sort order");
        int type = ChooseSortType();
        if (type != L.L_SHELL_SORT && type != L.L_BIN_SORT)
            throw Lept.Arg(func, "invalid sort type");

        if (type == L.L_BIN_SORT)
            return GetBinSortIndex(sortorder);
        else  /* shell sort */
            return GetSortIndex(sortorder);
    }

    // leptonica: numaChooseSortType (numafunc1.c)
    /// <summary>Selects a shell sort or a bin sort.</summary>
    /// <returns>sorttype  L_SHELL_SORT or L_BIN_SORT</returns>
    /// <remarks>
    /// (1) This selects either a shell sort or a bin sort, depending on
    ///     the number of elements in nas and the dynamic range.
    /// (2) If there are negative values in nas, it selects shell sort.
    /// </remarks>
    public int ChooseSortType()
    {
            /* If small histogram or negative values; use shell sort.
             * The C code ignores the error of numaGetMin() for an empty numa,
             * which leaves minval = 0.0 */
        float minval = 0.0f;
        if (Count > 0)
            GetMin(out minval, out _);
        int n = Count;
        if (minval < 0.0 || n < 200)
            return L.L_SHELL_SORT;

            /* If large maxval, use shell sort */
        GetMax(out float maxval, out _);
        if (maxval > L.MaxInitPtraSize - 1)
            return L.L_SHELL_SORT;

            /* Otherwise, need to compare nlog(n) with maxval.
             * The factor of 0.003 was determined by comparing times for
             * different histogram sizes and maxval.  It is very small
             * because binsort is fast and shell sort gets slow for large n. */
        if (n * Math.Log((float)n) < 0.003 * maxval)
            return L.L_SHELL_SORT;
        else
            return L.L_BIN_SORT;
    }

    // leptonica: numaSort (numafunc1.c)
    /// <summary>Sorts a numa by a shell sort.</summary>
    /// <param name="naout">output numa; can be null or equal to nain</param>
    /// <param name="nain">input numa</param>
    /// <param name="sortorder">L_SORT_INCREASING or L_SORT_DECREASING</param>
    /// <returns>naout   output sorted numa</returns>
    /// <remarks>
    /// (1) Set naout = nain for in-place; otherwise, set naout = null.
    /// (2) Source: Shell sort, modified from K&amp;R, 2nd edition, p.62.
    ///     Slow but simple O(n logn) sort.
    /// </remarks>
    public static Numa Sort(Numa? naout, Numa nain, int sortorder)
    {
        const string func = "numaSort";

        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg(func, "invalid sort order");

            /* Make naout if necessary; otherwise do in-place */
        if (naout == null)
            naout = nain.Copy();
        else if (nain != naout)
            throw Lept.Arg(func, "invalid: not in-place");
        int n;
        if ((n = naout.Count) == 0) {
            Log.Warning(func, "naout is empty");
            return naout;
        }
        float[] array = naout.array;  /* operate directly on the array */
        n = naout.Count;

            /* Shell sort */
        for (int gap = n / 2; gap > 0; gap = gap / 2) {
            for (int i = gap; i < n; i++) {
                for (int j = i - gap; j >= 0; j -= gap) {
                    if ((sortorder == L.L_SORT_INCREASING &&
                         array[j] > array[j + gap]) ||
                        (sortorder == L.L_SORT_DECREASING &&
                         array[j] < array[j + gap]))
                    {
                        float tmp = array[j];
                        array[j] = array[j + gap];
                        array[j + gap] = tmp;
                    }
                }
            }
        }

        return naout;
    }

    // leptonica: numaBinSort (numafunc1.c)
    /// <summary>Sorts a numa of non-negative integers by a bin sort.</summary>
    /// <param name="sortorder">L_SORT_INCREASING or L_SORT_DECREASING</param>
    /// <returns>na   sorted; this numa is of non-negative integers with a max that can
    /// not exceed (MaxInitPtraSize - 1)</returns>
    /// <remarks>
    /// (1) Because this uses a bin sort with buckets of size 1, it
    ///     is not appropriate for sorting either small arrays or
    ///     arrays containing very large integer values.  For such
    ///     arrays, use a standard general sort function like
    ///     numaSort().
    /// (2) You can use numaSortAutoSelect() to decide which sorting
    ///     method to use.
    /// </remarks>
    public Numa BinSort(int sortorder)
    {
        const string func = "numaBinSort";

        if (Count == 0) {
            Log.Warning(func, "nas is empty; returning copy");
            return Copy();
        }
        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg(func, "invalid sort order");

        Numa nat = GetBinSortIndex(sortorder);
        Numa nad = SortByIndex(nat);
        return nad;
    }

    // leptonica: numaGetSortIndex (numafunc1.c)
    /// <summary>Gets the array of indices that would sort the numa.</summary>
    /// <param name="sortorder">L_SORT_INCREASING or L_SORT_DECREASING</param>
    /// <returns>na  giving an array of indices that would sort the input array</returns>
    public Numa GetSortIndex(int sortorder)
    {
        const string func = "numaGetSortIndex";

        if (Count == 0) {
            Log.Warning(func, "na is empty");
            return Create(1);
        }
        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg(func, "invalid sortorder");

        int n = Count;
        float[] array = GetFArray(L.L_COPY);   /* copy of input array */
        float[] iarray = new float[n];         /* array of indices */
        for (int i = 0; i < n; i++)
            iarray[i] = i;

            /* Shell sort */
        for (int gap = n / 2; gap > 0; gap = gap / 2) {
            for (int i = gap; i < n; i++) {
                for (int j = i - gap; j >= 0; j -= gap) {
                    if ((sortorder == L.L_SORT_INCREASING &&
                         array[j] > array[j + gap]) ||
                        (sortorder == L.L_SORT_DECREASING &&
                         array[j] < array[j + gap]))
                    {
                        float tmp = array[j];
                        array[j] = array[j + gap];
                        array[j + gap] = tmp;
                        tmp = iarray[j];
                        iarray[j] = iarray[j + gap];
                        iarray[j + gap] = tmp;
                    }
                }
            }
        }

        Numa naisort = Create(n);
        for (int i = 0; i < n; i++)
            naisort.AddNumber(iarray[i]);

        return naisort;
    }

    // leptonica: numaGetBinSortIndex (numafunc1.c)
    /// <summary>Gets the array of indices that would sort a numa of non-negative integers.</summary>
    /// <param name="sortorder">L_SORT_INCREASING or L_SORT_DECREASING</param>
    /// <returns>na  sorted; this numa is of non-negative integers with a max that can
    /// not exceed (MaxInitPtraSize - 1)</returns>
    /// <remarks>
    /// (1) This creates an array (or lookup table) that contains
    ///     the sorted position of the elements in the input Numa.
    /// (2) Because it uses a bin sort with buckets of size 1, it
    ///     is not appropriate for sorting either small arrays or
    ///     arrays containing very large integer values.  For such
    ///     arrays, use a standard general sort function like
    ///     numaGetSortIndex().
    /// (3) You can use numaSortIndexAutoSelect() to decide which
    ///     sorting method to use.
    /// </remarks>
    public Numa GetBinSortIndex(int sortorder)
    {
        const string func = "numaGetBinSortIndex";
        Numa nas = this;

        if (nas.Count == 0) {
            Log.Warning(func, "nas is empty");
            return Create(1);
        }
        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg(func, "invalid sort order");
        nas.GetMin(out float minsize, out _);
        if (minsize < 0)
            throw Lept.Arg(func, "nas has negative numbers");
        nas.GetMax(out float size, out _);
        int isize = (int)size;
        if ((uint)isize > L.MaxInitPtraSize - 1)  /* l_int32 compared to l_uint32 */
            throw Lept.Arg(func, $"array too large: {isize} elements > max size = {L.MaxInitPtraSize - 1}");

            /* Set up a ptra holding numa at indices for which there
             * are values in nas.  Suppose nas has the value 230 at index
             * 7355.  A numa holding the index 7355 is created and stored
             * at the ptra index 230.  If there is another value of 230
             * in nas, its index is added to the same numa (at index 230
             * in the ptra).  When finished, the ptra can be scanned for numa,
             * and the original indices in the nas can be read out.  In this
             * way, the ptra effectively sorts the input numbers in the nas. */
        L_Ptra<Numa> paindex = L_Ptra<Numa>.Create(isize + 1);
        int n = nas.Count;
        for (int i = 0; i < n; i++) {
            int ival = nas.GetIValue(i);
            Numa? nai = paindex.GetPtrToItem(ival);
            if (nai == null) {  /* make it; no shifting will occur */
                nai = Create(1);
                paindex.Insert(ival, nai, L.L_MIN_DOWNSHIFT);
            }
            nai.AddNumber(i);
        }

            /* Sort by scanning the ptra, extracting numas and pulling
             * the (index into nas) numbers out of each numa, taken
             * successively in requested order. */
        int imax = paindex.MaxIndex;
        Numa nad = Create(0);
        if (sortorder == L.L_SORT_INCREASING) {
            for (int i = 0; i <= imax; i++) {
                Numa? na = paindex.Remove(i, L.L_NO_COMPACTION);
                if (na == null) continue;
                nad.Join(na, 0, -1);
            }
        } else {  /* L_SORT_DECREASING */
            for (int i = imax; i >= 0; i--) {
                Numa? na = paindex.RemoveLast();
                if (na == null) break;  /* they've all been removed */
                nad.Join(na, 0, -1);
            }
        }

        return nad;
    }

    // leptonica: numaSortByIndex (numafunc1.c)
    /// <summary>Reorders the numa by an index map.</summary>
    /// <param name="naindex">na that maps from the new numa to the input numa</param>
    /// <returns>nad  sorted</returns>
    public Numa SortByIndex(Numa naindex)
    {
        int n = Count;
        int ni = naindex.Count;
        if (n != ni)
            throw Lept.Arg("numaSortByIndex", "numa sizes differ");
        if (n == 0) {
            Log.Warning("numaSortByIndex", "nas is empty");
            return Copy();
        }

        Numa nad = Create(n);
        for (int i = 0; i < n; i++) {
            int index = naindex.GetIValue(i);
            float val = GetFValue(index);
            nad.AddNumber(val);
        }

        return nad;
    }

    // leptonica: numaIsSorted (numafunc1.c)
    /// <summary>Tests if the numa is sorted.</summary>
    /// <param name="sortorder">L_SORT_INCREASING or L_SORT_DECREASING</param>
    /// <returns>true if sorted; false if not</returns>
    /// <remarks>
    /// (1) This is a quick O(n) test if nas is sorted.  It is useful
    ///     in situations where the array is likely to be already
    ///     sorted, and a sort operation can be avoided.
    /// </remarks>
    public bool IsSorted(int sortorder)
    {
        int n;
        if ((n = Count) == 0) {
            Log.Warning("numaIsSorted", "nas is empty");
            return true;
        }
        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg("numaIsSorted", "invalid sortorder");

        n = Count;
        /* NOTE(upstream): prevval is never updated, so each value is compared with
         * the first one only, not with its predecessor. */
        float prevval = GetFValue(0);
        for (int i = 1; i < n; i++) {
            float val = GetFValue(i);
            if ((sortorder == L.L_SORT_INCREASING && val < prevval) ||
                (sortorder == L.L_SORT_DECREASING && val > prevval))
                return false;
        }

        return true;
    }

    // leptonica: numaSortPair (numafunc1.c)
    /// <summary>Sorts two numas together, using this numa (nax) as the key.</summary>
    /// <param name="nay">second array</param>
    /// <param name="sortorder">L_SORT_INCREASING or L_SORT_DECREASING</param>
    /// <param name="nasx">sorted</param>
    /// <param name="nasy">sorted exactly in order of nasx</param>
    /// <remarks>
    /// (1) This function sorts the two input arrays, nax and nay,
    ///     together, using nax as the key for sorting.
    /// </remarks>
    public void SortPair(Numa nay, int sortorder, out Numa nasx, out Numa nasy)
    {
        Numa nax = this;

        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg("numaSortPair", "invalid sortorder");

        bool sorted = nax.IsSorted(sortorder);
        if (sorted == true) {
            nasx = nax.Copy();
            nasy = nay.Copy();
        } else {
            Numa naindex = nax.GetSortIndex(sortorder);
            nasx = nax.SortByIndex(naindex);
            nasy = nay.SortByIndex(naindex);
        }
    }

    // leptonica: numaInvertMap (numafunc1.c)
    /// <summary>Inverts an index map.</summary>
    /// <returns>nad  the inverted map; throws if not invertible</returns>
    /// <remarks>
    /// (1) This requires that nas contain each integer from 0 to n-1.
    ///     The array is typically an index array into a sort or permutation
    ///     of another array.
    /// </remarks>
    public Numa InvertMap()
    {
        int n;
        if ((n = Count) == 0) {
            Log.Warning("numaInvertMap", "nas is empty");
            return Copy();
        }

        Numa nad = MakeConstant(0.0f, n);
        int[] test = new int[n];
        bool error = false;
        for (int i = 0; i < n; i++) {
            int val = GetIValue(i);
            if (val >= n) {
                error = true;
                break;
            }
            nad.ReplaceNumber(val, i);
            if (test[val] == 0) {
                test[val] = 1;
            } else {
                error = true;
                break;
            }
        }

        if (error)
            throw Lept.Arg("numaInvertMap", "nas not invertible");

        return nad;
    }

    // leptonica: numaAddSorted (numafunc1.c)
    /// <summary>Inserts a value into a sorted numa, keeping it sorted.</summary>
    /// <param name="val">value to be inserted in sorted order</param>
    /// <remarks>
    /// (1) The input %na is sorted.  This function determines the
    ///     sort order of %na and inserts %val into the array.
    /// </remarks>
    public void AddSorted(float val)
    {
        int index = FindSortedLoc(val);
        InsertNumber(index, val);
    }

    // leptonica: numaFindSortedLoc (numafunc1.c)
    /// <summary>Finds the location where a value is to be inserted into a sorted numa.</summary>
    /// <param name="val">value to be inserted in sorted order</param>
    /// <returns>index location to insert val</returns>
    /// <remarks>
    /// (1) The input %na is sorted.  This determines the sort order of @na,
    ///     either increasing or decreasing, and does a binary search for the
    ///     location to insert %val into the array.  The search is O(log n).
    /// (2) The index returned is the location to insert into the array.
    ///     The value at the index, and all values to the right, are
    ///     moved to the right (increasing their index location by 1).
    /// (3) If n is the size of %na, the index can be anything in [0 ... n].
    ///     if index == 0, the value is inserted at the beginning of the
    ///     array; if index == n, it is inserted at the end.
    /// (4) If the size of %na is 1, insert with an increasing sort.
    /// </remarks>
    public int FindSortedLoc(float val)
    {
        int n = Count;
        if (n == 0) return 0;
        float val0 = GetFValue(0);
        if (n == 1) {  /* use increasing sort order */
            if (val >= val0)
                return 1;
            return 0;
        }

            /* -----------------  n >= 2 ----------------- */
        float valn = GetFValue(n - 1);
        int increasing = (valn >= val0) ? 1 : 0;  /* sort order */

            /* Check if outside bounds of existing array */
        if (increasing != 0) {
            if (val < val0)
                return 0;
            else if (val > valn)
                return n;
        } else {  /* decreasing */
            if (val > val0)
                return 0;
            else if (val < valn)
                return n;
        }

            /* Within bounds of existing array; search */
        int lindex = 0;
        int rindex = n - 1;
        while (true) {
            int midindex = (lindex + rindex) / 2;
            if (midindex == lindex || midindex == rindex) break;
            float valmid = GetFValue(midindex);
            if (increasing != 0) {
                if (val > valmid)
                    lindex = midindex;
                else
                    rindex = midindex;
            } else {  /* decreasing */
                if (val > valmid)
                    rindex = midindex;
                else
                    lindex = midindex;
            }
        }
        return rindex;
    }

    /*----------------------------------------------------------------------*
     *                          Random permutation                          *
     *----------------------------------------------------------------------*/
    /* TODO(CRand): replace by CRand.  This is the generator of the MSVC runtime,
     * one state per thread as in the CRT; RAND_MAX is 0x7fff. */
    private const int RandMax = 0x7fff;
    [ThreadStatic] private static uint s_randState;

    private static void Srand(uint seed)
    {
        s_randState = seed;
    }

    private static int Rand()
    {
        s_randState = s_randState * 214013u + 2531011u;
        return (int)((s_randState >> 16) & 0x7fff);
    }

    // leptonica: numaPseudorandomSequence (numafunc1.c)
    /// <summary>Makes a pseudorandom permutation of the integers 0 ... size - 1.</summary>
    /// <param name="size">of sequence</param>
    /// <param name="seed">for random number generation</param>
    /// <returns>na  pseudorandom on [0,...,size - 1]</returns>
    /// <remarks>
    /// (1) This uses the Durstenfeld shuffle.
    ///     See: http://en.wikipedia.org/wiki/Fisher–Yates_shuffle.
    ///     Result is a pseudorandom permutation of the sequence of integers
    ///     from 0 to size - 1.
    /// </remarks>
    public static Numa PseudorandomSequence(int size, int seed)
    {
        if (size <= 0)
            throw Lept.Arg("numaPseudorandomSequence", "size <= 0");

        int[] array = new int[size];
        for (int i = 0; i < size; i++)
            array[i] = i;
        Srand((uint)seed);
        for (int i = size - 1; i > 0; i--) {
            int index = (int)((i + 1) * ((double)Rand() / (double)RandMax));
            index = Math.Min(index, i);
            int temp = array[i];
            array[i] = array[index];
            array[index] = temp;
        }

        return CreateFromIArray(array, size);
    }

    // leptonica: numaRandomPermutation (numafunc1.c)
    /// <summary>Randomly shuffles the numa.</summary>
    /// <param name="seed">for random number generation</param>
    /// <returns>nas  randomly shuffled array</returns>
    public Numa RandomPermutation(int seed)
    {
        int size;
        if ((size = Count) == 0) {
            Log.Warning("numaRandomPermutation", "nas is empty");
            return Copy();
        }

        Numa naindex = PseudorandomSequence(size, seed);
        Numa nad = Create(size);
        for (int i = 0; i < size; i++) {
            int index = naindex.GetIValue(i);
            float val = GetFValue(index);
            nad.AddNumber(val);
        }
        return nad;
    }

    /*----------------------------------------------------------------------*
     *                     Functions requiring sorting                      *
     *----------------------------------------------------------------------*/
    // leptonica: numaGetRankValue (numafunc1.c)
    /// <summary>Gets the value at a given rank in the sorted numa.</summary>
    /// <param name="fract">use 0.0 for smallest, 1.0 for largest</param>
    /// <param name="nasort">[optional] increasing sorted version of na</param>
    /// <param name="usebins">0 for general sort; 1 for bin sort</param>
    /// <returns>rank val</returns>
    /// <remarks>
    /// (1) Computes the rank value of a number in the %na, which is
    ///     the number that is a fraction %fract from the small
    ///     end of the sorted version of %na.
    /// (2) If you do this multiple times for different rank values,
    ///     sort the array in advance and use that for %nasort;
    ///     if you're only calling this once, input %nasort == null.
    /// (3) If %usebins == 1, this uses a bin sorting method.
    ///     Use this only where:
    ///      * the numbers are non-negative integers
    ///      * there are over 100 numbers
    ///      * the maximum value is less than about 50,000
    /// (4) The advantage of using a bin sort is that it is O(n),
    ///     instead of O(nlogn) for general sort routines.
    /// </remarks>
    public float GetRankValue(float fract, Numa? nasort, int usebins)
    {
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetRankValue", "na empty");
        if (fract < 0.0 || fract > 1.0)
            throw Lept.Arg("numaGetRankValue", "fract not in [0.0 ... 1.0]");

        Numa nas;
        if (nasort != null) {
            nas = nasort;
        } else {
            if (usebins == 0)
                nas = Sort(null, this, L.L_SORT_INCREASING);
            else
                nas = BinSort(L.L_SORT_INCREASING);
        }
        int index = (int)(fract * (float)(n - 1) + 0.5);
        return nas.GetFValue(index);
    }

    // leptonica: numaGetMedian (numafunc1.c)
    /// <summary>Computes the median value.</summary>
    /// <returns>median value</returns>
    /// <remarks>
    /// (1) Computes the median value of the numbers in the numa, by
    ///     sorting and finding the middle value in the sorted array.
    /// </remarks>
    public float GetMedian()
    {
        if (Count == 0)
            throw Lept.Arg("numaGetMedian", "na not defined or empty");

        return GetRankValue(0.5f, null, 0);
    }

    // leptonica: numaGetBinnedMedian (numafunc1.c)
    /// <summary>Computes the median value by a bin sort.</summary>
    /// <returns>integer median value</returns>
    /// <remarks>
    /// (1) Computes the median value of the numbers in the numa,
    ///     using bin sort and finding the middle value in the sorted array.
    /// (2) See numaGetRankValue() for conditions on na for which
    ///     this should be used.  Otherwise, use numaGetMedian().
    /// </remarks>
    public int GetBinnedMedian()
    {
        if (Count == 0)
            throw Lept.Arg("numaGetBinnedMedian", "na not defined or empty");

        float fval = GetRankValue(0.5f, null, 1);
        return Utils.RoundFToI(fval);
    }

    // leptonica: numaGetMeanDevFromMedian (numafunc1.c)
    /// <summary>Computes the average absolute deviation from a given median value.</summary>
    /// <param name="med">median value</param>
    /// <returns>average absolute value deviation from median value</returns>
    public float GetMeanDevFromMedian(float med)
    {
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetMeanDevFromMedian", "na is empty");

        float dev = 0.0f;
        for (int i = 0; i < n; i++) {
            float val = GetFValue(i);
            float diff = val - med;
            dev += (diff < 0) ? -1 * diff : diff;   /* L_ABS */
        }
        return dev / (float)n;
    }

    // leptonica: numaGetMedianDevFromMedian (numafunc1.c)
    /// <summary>Computes the median and the median absolute deviation from it.</summary>
    /// <param name="med">median value</param>
    /// <param name="dev">median deviation from median val</param>
    /// <remarks>
    /// (1) Finds the median of the absolute value of the deviation from
    ///     the median value in the array.  Why take the absolute value?
    ///     Consider the case where you have values equally distributed
    ///     about both sides of a median value.  Without taking the absolute
    ///     value of the differences, you will get 0 for the deviation,
    ///     and this is not useful.
    /// </remarks>
    public void GetMedianDevFromMedian(out float med, out float dev)
    {
        med = 0.0f;
        dev = 0.0f;
        if (Count == 0)
            throw Lept.Arg("numaGetMedianDevFromMedian", "na not defined or empty");

        med = GetMedian();
        int n = Count;
        Numa nadev = Create(n);
        for (int i = 0; i < n; i++) {
            float val = GetFValue(i);
            float diff = val - med;
            nadev.AddNumber((diff < 0) ? -1 * diff : diff);   /* L_ABS */
        }
        dev = nadev.GetMedian();
    }

    // leptonica: numaGetMode (numafunc1.c)
    /// <summary>Computes the mode value and its count.</summary>
    /// <param name="val">mode val</param>
    /// <param name="count">mode count</param>
    /// <remarks>
    /// (1) Computes the mode value of the numbers in the numa, by
    ///     sorting and finding the value of the number with the
    ///     largest count.
    /// (2) Optionally, also returns that count.
    /// </remarks>
    public void GetMode(out float val, out int count)
    {
        val = 0.0f;
        count = 0;
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("numaGetMode", "na is empty");

        Numa nasort = Sort(null, this, L.L_SORT_DECREASING);
        float[] array = nasort.GetFArray(L.L_NOCOPY);

            /* Initialize with array[0] */
        float prevval = array[0];
        int prevcount = 1;
        float maxval = prevval;
        int maxcount = prevcount;

            /* Scan the sorted array, aggregating duplicates */
        for (int i = 1; i < n; i++) {
            float v = array[i];
            if (v == prevval) {
                prevcount++;
            } else {  /* new value */
                if (prevcount > maxcount) {  /* new max */
                    maxcount = prevcount;
                    maxval = prevval;
                }
                prevval = v;
                prevcount = 1;
            }
        }

            /* Was the mode the last run of elements? */
        if (prevcount > maxcount) {
            maxcount = prevcount;
            maxval = prevval;
        }

        val = maxval;
        count = maxcount;
    }

    /*----------------------------------------------------------------------*
     *                            Rearrangements                            *
     *----------------------------------------------------------------------*/
    // leptonica: numaJoin (numafunc1.c)
    /// <summary>Appends values of another numa to this (destination) numa.</summary>
    /// <param name="nas">[optional] source numa; add from this one</param>
    /// <param name="istart">starting index in nas</param>
    /// <param name="iend">ending index in nas; use -1 to cat all</param>
    /// <remarks>
    /// (1) istart &lt; 0 is taken to mean 'read from the start' (istart = 0)
    /// (2) iend &lt; 0 means 'read to the end'
    /// (3) if nas == null, this is a no-op
    /// </remarks>
    public void Join(Numa? nas, int istart, int iend)
    {
        string? error = JoinImpl(nas, istart, iend);
        if (error != null)
            throw Lept.Arg("numaJoin", error);
    }

    /* The body of numaJoin(), with the error as a return value, because
     * numaaFlattenToNuma() carries on after an error. */
    private string? JoinImpl(Numa? nas, int istart, int iend)
    {
        if (nas == null)
            return null;

        if (istart < 0)
            istart = 0;
        int n = nas.Count;
        if (iend < 0 || iend >= n)
            iend = n - 1;
        if (istart > iend)
            return "istart > iend; nothing to add";

        for (int i = istart; i <= iend; i++) {
            float val = nas.GetFValue(i);
            AddNumber(val);
        }

        return null;
    }

    /// <summary>Joins a numa to this numa, as numaJoin() does, but logs an error
    /// and carries on if there is nothing to add (numaaFlattenToNuma() ignores the
    /// error return of numaJoin()).</summary>
    internal void JoinIgnoringError(Numa? nas)
    {
        string? error = JoinImpl(nas, 0, -1);
        if (error != null)
            Log.Error("numaJoin", error);
    }
}

public sealed partial class Numaa
{
    // leptonica: numaaJoin (numafunc1.c)
    /// <summary>Appends the numas of another numaa to this (destination) numaa.</summary>
    /// <param name="naas">[optional] source naa; add from this one</param>
    /// <param name="istart">starting index in nas</param>
    /// <param name="iend">ending index in naas; use -1 to cat all</param>
    /// <remarks>
    /// (1) istart &lt; 0 is taken to mean 'read from the start' (istart = 0)
    /// (2) iend &lt; 0 means 'read to the end'
    /// (3) if naas == null, this is a no-op
    /// </remarks>
    public void Join(Numaa? naas, int istart, int iend)
    {
        if (naas == null)
            return;

        if (istart < 0)
            istart = 0;
        int n = naas.Count;
        if (iend < 0 || iend >= n)
            iend = n - 1;
        if (istart > iend)
            throw Lept.Arg("numaaJoin", "istart > iend; nothing to add");

        for (int i = istart; i <= iend; i++) {
            Numa na = naas.GetNuma(i, L.L_CLONE);
            AddNuma(na, L.L_INSERT);
        }
    }

    // leptonica: numaaFlattenToNuma (numafunc1.c)
    /// <summary>Joins all the numas into one numa.</summary>
    /// <returns>numa</returns>
    /// <remarks>
    /// (1) This 'flattens' the Numaa to a Numa, by joining successively
    ///     each Numa in the Numaa.
    /// (2) It doesn't make any assumptions about the location of the
    ///     Numas in the Numaa array, unlike most Numaa functions.
    /// (3) It leaves the input Numaa unchanged.
    /// </remarks>
    public Numa FlattenToNuma()
    {
        int nalloc = Nalloc;
        /* NOTE(upstream): numaaGetPtrArray() sets the count of the numaa to nalloc,
         * so the input is changed after all. */
        Numa?[] array = GetPtrArray();
        Numa nad = Numa.Create(0);
        for (int i = 0; i < nalloc; i++) {
            Numa? na = array[i];
            if (na == null) continue;
            nad.JoinIgnoringError(na);
        }

        return nad;
    }

}
