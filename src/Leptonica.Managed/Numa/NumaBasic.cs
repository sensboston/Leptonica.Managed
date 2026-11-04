using System;
using System.IO;
using System.Text;

namespace Leptonica;

/*
 *  numabasic.c: Numa creation, destruction, copy, clone, etc., accessors
 *  and serialization, for Numa and Numaa.
 *
 *    (1) The Numa is a struct holding an array of floats.  It can also
 *        be used to store l_int32 values, with some loss of precision
 *        for floats larger than about 10 million.  Use the L_Dna instead
 *        if integers larger than a few million need to be stored.
 *
 *    (2) Always use the accessors in this file, never the fields directly.
 *
 *    (3) Storing and retrieving numbers:
 *
 *       * to append a new number to the array, use numaAddNumber().  If
 *         the number is an int, it will will automatically be converted
 *         to l_float32 and stored.
 *
 *       * to reset a value stored in the array, use numaSetValue().
 *
 *       * to increment or decrement a value stored in the array,
 *         use numaShiftValue().
 *
 *       * to obtain a value from the array, use either numaGetIValue()
 *         or numaGetFValue(), depending on whether you are retrieving
 *         an integer or a float.
 *
 *    (4) int <--> float conversions:
 *
 *        Tradition dictates that type conversions go automatically from
 *        l_int32 --> l_float32, even though it is possible to lose
 *        precision for large integers, whereas you must cast (l_int32)
 *        to go from l_float32 --> l_int32 because you're truncating
 *        to the integer value.
 *
 *    (5) As with other arrays in leptonica, the numa has both an allocated
 *        size and a count of the stored numbers.  When you add a number, it
 *        goes on the end of the array, and causes a realloc if the array
 *        is already filled.  However, in situations where you want to
 *        add numbers randomly into an array, such as when you build a
 *        histogram, you must set the count of stored numbers in advance.
 *        This is done with numaSetCount().  If you set a count larger
 *        than the allocated array, it does a realloc to the size requested.
 *
 *    (6) In situations where the data in a numa correspond to a function
 *        y(x), the values can be either at equal spacings in x or at
 *        arbitrary spacings.  For the former, we can represent all x values
 *        by two parameters: startx (corresponding to y[0]) and delx
 *        for the change in x for adjacent values y[i] and y[i+1].
 *        startx and delx are initialized to 0.0 and 1.0, rsp.
 *        For arbitrary spacings, we use a second numa, and the two
 *        numas are typically denoted nay and nax.
 *
 *    (7) The numa is also the basic struct used for histograms.  Every numa
 *        has startx and delx fields, initialized to 0.0 and 1.0, that can
 *        be used to represent the "x" value for the location of the
 *        first bin and the bin width, respectively.  Accessors are the
 *        numa*Parameters() functions.  All functions that make numa
 *        histograms must set these fields properly, and many functions
 *        that use numa histograms rely on the correctness of these values.
 *
 *  Differences from C:
 *    - No reference counting and no numaDestroy() and numaaDestroy(): the GC
 *      owns the objects (not ported).  numaClone() returns the same reference.
 *    - FILE* becomes Stream.  Text is written as ASCII, formatted with
 *      CFormat (the printf of the C runtime), so that float text is the same
 *      as the C output byte for byte.  Reading follows the fscanf formats of
 *      the C code, using CFormat.Sscanf.
 *    - numaWriteStream() with a null stream writes to stderr, as in C.
 *    - The *Mem variants take a ReadOnlySpan&lt;byte&gt; and return a byte[]
 *      (without the terminating NUL that C appends to its buffer).
 *    - fopenReadStream() and friends are replaced by System.IO.
 *    - Functions that return an error code or NULL in C throw instead,
 *      except where the C code logs and carries on (noted at the place).
 *      The accessors that return 0 on error in C throw as well.
 *    - Output values that are "set to 0 on error" in C are out parameters
 *      that are set to 0 before the exception is thrown.
 *    - numaGetIArray(), numaGetFArray() return arrays; numaaGetPtrArray()
 *      returns the internal array of the Numaa.
 */

/// <summary>Constants from array.h and array_internal.h used by numabasic.c.</summary>
public static partial class L
{
    /// <summary>Version number of the serialized Numa format.</summary>
    public const int NUMA_VERSION_NUMBER = 1;

    /* Numa data conversion */
    /// <summary>Convert values to integer.</summary>
    public const int L_INTEGER_VALUE = 1;
    /// <summary>Convert values to floating point.</summary>
    public const int L_FLOAT_VALUE = 2;
}

/// <summary>Array of floats (struct Numa).</summary>
public sealed partial class Numa
{
    /* Bounds on initial array size */
    private const uint MaxFloatArraySize = 100000000;  /* for numa */
    private const int InitialArraySize = 50;           /* n'importe quoi */

    private float[] array;   /* number array; its length is at least nalloc */
    private int nalloc;      /* size of allocated number array              */
    private int n;           /* number of numbers saved                     */
    private float startx;    /* x value corresponding to array[0]           */
    private float delx;      /* change in x value as i --> i + 1            */

    private Numa(int nalloc)
    {
        array = new float[nalloc];
        this.nalloc = nalloc;
        n = 0;
        startx = 0.0f;
        delx = 1.0f;
    }

    /// <summary>Size of the allocated number array (na->nalloc).</summary>
    public int Nalloc => nalloc;

    /*--------------------------------------------------------------------------*
     *               Numa creation, destruction, copy, clone, etc.              *
     *--------------------------------------------------------------------------*/
    // leptonica: numaCreate (numabasic.c)
    /// <summary>Creates an empty numa.</summary>
    /// <param name="n">size of number array to be alloc'd; 0 for default</param>
    public static Numa Create(int n)
    {
        if (n <= 0 || n > MaxFloatArraySize)
            n = InitialArraySize;

        return new Numa(n);
    }

    // leptonica: numaCreateFromIArray (numabasic.c)
    /// <summary>Creates a numa from an array of integers.</summary>
    /// <param name="iarray">integer array</param>
    /// <param name="size">of the array</param>
    /// <remarks>
    /// (1) We can't insert this int array into the numa, because a numa
    ///     takes a float array.  So this just copies the data from the
    ///     input array into the numa.  The input array continues to be
    ///     owned by the caller.
    /// </remarks>
    public static Numa CreateFromIArray(int[] iarray, int size)
    {
        if (size <= 0)
            throw Lept.Arg("numaCreateFromIArray", "size must be > 0");

        Numa na = Create(size);
        for (int i = 0; i < size; i++)
            na.AddNumber(iarray[i]);

        return na;
    }

    // leptonica: numaCreateFromFArray (numabasic.c)
    /// <summary>Creates a numa from an array of floats.</summary>
    /// <param name="farray">float array</param>
    /// <param name="size">of the array</param>
    /// <param name="copyflag">L_INSERT or L_COPY</param>
    /// <remarks>
    /// (1) With L_INSERT, ownership of the input array is transferred
    ///     to the returned numa, and all %size elements are considered
    ///     to be valid.
    /// </remarks>
    public static Numa CreateFromFArray(float[] farray, int size, int copyflag)
    {
        if (size <= 0)
            throw Lept.Arg("numaCreateFromFArray", "size must be > 0");
        if (copyflag != L.L_INSERT && copyflag != L.L_COPY)
            throw Lept.Arg("numaCreateFromFArray", "invalid copyflag");

        Numa na = Create(size);
        if (copyflag == L.L_INSERT) {
            /* C does not check the size of the array; here it must hold %size values */
            if (farray.Length < size)
                throw Lept.Arg("numaCreateFromFArray", "farray is smaller than size");
            na.array = farray;
            na.n = size;
        } else {  /* just copy the contents */
            for (int i = 0; i < size; i++)
                na.AddNumber(farray[i]);
        }

        return na;
    }

    // leptonica: numaCreateFromString (numabasic.c)
    /// <summary>Creates a numa from a string of comma-separated numbers.</summary>
    /// <param name="str">string of comma-separated numbers</param>
    /// <remarks>
    /// (1) The numbers can be ints or floats; they will be interpreted
    ///     and stored as floats.  To use them as integers (e.g., for
    ///     indexing into arrays), use numaGetIValue(...).
    /// </remarks>
    public static Numa CreateFromString(string str)
    {
        const string func = "numaCreateFromString";
        if (str.Length == 0)
            throw Lept.Arg(func, "str not defined or empty");

        Sarray sa = Sarray.Create(0);
        sa.SplitString(str, ",");
        int n = sa.Count;
        Numa na = Create(n);
        int nerrors = 0;
        for (int i = 0; i < n; i++) {
            string substr = sa.GetString(i, L.L_NOCOPY);
            if (CFormat.Sscanf(substr, "%f", out object?[] v) != 1) {
                Log.Error(func, $"substr {i} not float");
                nerrors++;
            } else {
                na.AddNumber((float)v[0]!);
            }
        }

        if (nerrors > 0)
            throw Lept.Arg(func, "non-floats in string");

        return na;
    }

    // leptonica: numaCopy (numabasic.c)
    /// <summary>Returns a copy of the numa.</summary>
    public Numa Copy()
    {
        Numa cna = Create(nalloc);
        cna.startx = startx;
        cna.delx = delx;

        for (int i = 0; i < n; i++)
            cna.AddNumber(array[i]);

        return cna;
    }

    // leptonica: numaClone (numabasic.c)
    /// <summary>Returns the same numa (a reference; changes are seen through both).</summary>
    public Numa Clone()
    {
        return this;
    }

    // leptonica: numaEmpty (numabasic.c)
    /// <remarks>
    /// (1) This does not change the allocation of the array.
    ///     It just clears the number of stored numbers, so that
    ///     the array appears to be empty.
    /// </remarks>
    public void Empty()
    {
        n = 0;
    }

    /*--------------------------------------------------------------------------*
     *                 Number array: add number and extend array                *
     *--------------------------------------------------------------------------*/
    // leptonica: numaAddNumber (numabasic.c)
    /// <param name="val">float or int to be added; stored as a float</param>
    public void AddNumber(float val)
    {
        int n = Count;
        if (n >= nalloc)
            ExtendArray();
        array[n] = val;
        this.n++;
    }

    // leptonica: numaExtendArray (numabasic.c)
    /// <remarks>(1) The max number of floats is 100M.</remarks>
    private void ExtendArray()
    {
        if (nalloc > MaxFloatArraySize)  /* belt & suspenders */
            throw Lept.Fail("numaExtendArray", "na has too many ptrs");
        long oldsize = (long)nalloc * sizeof(float);
        long newsize = 2 * oldsize;
        if (newsize > 4L * MaxFloatArraySize)
            throw Lept.Fail("numaExtendArray", "newsize > 400 MB; too large");

        Array.Resize(ref array, (int)(newsize / sizeof(float)));
        nalloc *= 2;
    }

    // leptonica: numaInsertNumber (numabasic.c)
    /// <param name="index">location in na to insert new value</param>
    /// <param name="val">float32 or integer to be added</param>
    /// <remarks>
    /// (1) This shifts na[i] --> na[i + 1] for all i >= index,
    ///     and then inserts val as na[index].
    /// (2) It should not be used repeatedly on large arrays,
    ///     because the function is O(n).
    /// </remarks>
    public void InsertNumber(int index, float val)
    {
        int n = Count;
        if (index < 0 || index > n)
            throw Lept.Arg("numaInsertNumber", $"index {index} not in [0,...,{n}]");

        if (n >= nalloc)
            ExtendArray();
        for (int i = n; i > index; i--)
            array[i] = array[i - 1];
        array[index] = val;
        this.n++;
    }

    // leptonica: numaRemoveNumber (numabasic.c)
    /// <param name="index">element to be removed</param>
    /// <remarks>
    /// (1) This shifts na[i] --> na[i - 1] for all i > index.
    /// (2) It should not be used repeatedly on large arrays,
    ///     because the function is O(n).
    /// </remarks>
    public void RemoveNumber(int index)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("numaRemoveNumber", $"index {index} not in [0,...,{n - 1}]");

        for (int i = index + 1; i < n; i++)
            array[i - 1] = array[i];
        this.n--;
    }

    // leptonica: numaReplaceNumber (numabasic.c)
    /// <param name="index">element to be replaced</param>
    /// <param name="val">new value to replace old one</param>
    public void ReplaceNumber(int index, float val)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("numaReplaceNumber", $"index {index} not in [0,...,{n - 1}]");

        array[index] = val;
    }

    /*----------------------------------------------------------------------*
     *                            Numa accessors                            *
     *----------------------------------------------------------------------*/
    // leptonica: numaGetCount, numaSetCount (numabasic.c)
    /// <summary>Count of stored numbers; 0 if no numbers.</summary>
    /// <remarks>
    /// Setting the count (numaSetCount):
    /// (1) If newcount &lt;= na->nalloc, this resets na->n.
    ///     Using newcount = 0 is equivalent to numaEmpty().
    /// (2) If newcount > na->nalloc, this causes a realloc
    ///     to a size na->nalloc = newcount.
    /// (3) All the previously unused values in na are set to 0.0.
    /// </remarks>
    public int Count
    {
        get => n;
        set {
            int newcount = value;
            /* NOTE(upstream): the values beyond n are zero only if they were never
             * used; after numaEmpty() or numaRemoveNumber() the old values reappear.
             * A negative newcount is not checked. */
            if (newcount > nalloc) {
                if (array.Length < newcount)
                    Array.Resize(ref array, newcount);
                nalloc = newcount;
            }
            n = newcount;
        }
    }

    // leptonica: numaGetFValue (numabasic.c)
    /// <summary>Gets the float value at the index.</summary>
    /// <param name="index">into numa</param>
    public float GetFValue(int index)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("numaGetFValue", "index not valid");

        return array[index];
    }

    // leptonica: numaGetIValue (numabasic.c)
    /// <summary>Gets the value at the index, rounded to an integer.</summary>
    /// <param name="index">into numa</param>
    public int GetIValue(int index)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("numaGetIValue", "index not valid");

        float val = array[index];
        int sign = (val < 0) ? -1 : 1;   /* L_SIGN */
        return (int)(val + sign * 0.5);
    }

    // leptonica: numaSetValue (numabasic.c)
    /// <param name="index">to element to be set</param>
    /// <param name="val">to set</param>
    public void SetValue(int index, float val)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("numaSetValue", "index not valid");

        array[index] = val;
    }

    // leptonica: numaShiftValue (numabasic.c)
    /// <param name="index">to element to change relative to the current value</param>
    /// <param name="diff">increment if diff > 0 or decrement if diff &lt; 0</param>
    public void ShiftValue(int index, float diff)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("numaShiftValue", "index not valid");

        array[index] += diff;
    }

    // leptonica: numaGetIArray (numabasic.c)
    /// <summary>Returns a copy of the internal array, integerized by rounding.</summary>
    /// <remarks>
    /// (1) A copy of the array is always made, because we need to
    ///     generate an integer array from the bare float array.
    /// (2) The array size is determined by the number of stored numbers,
    ///     not by the size of the allocated array in the Numa.
    /// (3) This function is provided to simplify calculations
    ///     using the bare internal array, rather than continually
    ///     calling accessors on the numa.  It is typically used
    ///     on an array of size 256.
    /// </remarks>
    public int[] GetIArray()
    {
        int n = Count;
        if (n == 0)
            throw Lept.Arg("numaGetIArray", "na is empty");

        int[] iarray = new int[n];
        for (int i = 0; i < n; i++)
            iarray[i] = GetIValue(i);

        return iarray;
    }

    // leptonica: numaGetFArray (numabasic.c)
    /// <summary>Returns either the internal array or a copy of it.</summary>
    /// <param name="copyflag">L_NOCOPY or L_COPY</param>
    /// <remarks>
    /// (1) If copyflag == L_COPY, it makes a copy of the n stored numbers.
    ///     Otherwise, it operates directly on the array of the numa,
    ///     whose length is at least the allocated size.
    /// (2) Very important: for L_NOCOPY, any writes to the array
    ///     will be in the numa.  Do not write beyond the size of
    ///     the count field, because it will not be accessible
    ///     from the numa!  If necessary, be sure to set the count
    ///     field to a larger number (such as the alloc size)
    ///     BEFORE calling this function.  Creating with numaMakeConstant()
    ///     is another way to insure full initialization.
    /// </remarks>
    public float[] GetFArray(int copyflag)
    {
        if (copyflag == L.L_NOCOPY)
            return array;

        /* copyflag == L_COPY */
        int n = Count;
        if (n == 0)
            throw Lept.Arg("numaGetFArray", "na is empty");
        float[] farray = new float[n];
        for (int i = 0; i < n; i++)
            farray[i] = array[i];

        return farray;
    }

    // leptonica: numaGetParameters (numabasic.c)
    /// <param name="startx">x value corresponding to na[0]</param>
    /// <param name="delx">difference in x values for adjacent elements</param>
    public void GetParameters(out float startx, out float delx)
    {
        startx = this.startx;
        delx = this.delx;
    }

    // leptonica: numaSetParameters (numabasic.c)
    /// <param name="startx">x value corresponding to na[0]</param>
    /// <param name="delx">difference in x values for the situation where the
    /// elements of na correspond to the evaluation of a function at equal
    /// intervals of size %delx</param>
    public void SetParameters(float startx, float delx)
    {
        this.startx = startx;
        this.delx = delx;
    }

    // leptonica: numaCopyParameters (numabasic.c)
    /// <summary>Copies startx and delx from the source numa to this (destination) numa.</summary>
    /// <param name="nas">source Numa</param>
    public void CopyParameters(Numa nas)
    {
        nas.GetParameters(out float start, out float binsize);
        SetParameters(start, binsize);
    }

    /*----------------------------------------------------------------------*
     *                      Convert to string array                         *
     *----------------------------------------------------------------------*/
    // leptonica: numaConvertToSarray (numabasic.c)
    /// <summary>Converts the values to strings.</summary>
    /// <param name="size1">size of conversion field</param>
    /// <param name="size2">for float conversion: size of field to the right of the decimal point</param>
    /// <param name="addzeros">for integer conversion: to add lead zeros</param>
    /// <param name="type">L_INTEGER_VALUE, L_FLOAT_VALUE</param>
    /// <returns>a sarray of the float values converted to strings
    /// representing either integer or float values</returns>
    /// <remarks>(1) For integer conversion, size2 is ignored.
    /// For float conversion, addzeroes is ignored.</remarks>
    public Sarray ConvertToSarray(int size1, int size2, int addzeros, int type)
    {
        if (type != L.L_INTEGER_VALUE && type != L.L_FLOAT_VALUE)
            throw Lept.Arg("numaConvertToSarray", "invalid type");

        string fmt;
        if (type == L.L_INTEGER_VALUE) {
            if (addzeros != 0)
                fmt = CFormat.Sprintf("%%0%dd", size1);
            else
                fmt = CFormat.Sprintf("%%%dd", size1);
        } else {  /* L_FLOAT_VALUE */
            fmt = CFormat.Sprintf("%%%d.%df", size1, size2);
        }

        int n = Count;
        Sarray sa = Sarray.Create(n);

        for (int i = 0; i < n; i++) {
            string strbuf;
            if (type == L.L_INTEGER_VALUE) {
                int ival = GetIValue(i);
                strbuf = CFormat.Sprintf(fmt, ival);
            } else {  /* L_FLOAT_VALUE */
                float fval = GetFValue(i);
                strbuf = CFormat.Sprintf(fmt, fval);
            }
            /* snprintf into char strbuf[64] */
            if (strbuf.Length > 63)
                strbuf = strbuf.Substring(0, 63);
            sa.AddString(strbuf, L.L_COPY);
        }

        return sa;
    }

    /*----------------------------------------------------------------------*
     *                       Serialize numa for I/O                         *
     *----------------------------------------------------------------------*/
    // leptonica: numaRead (numabasic.c)
    /// <summary>Reads a serialized <see cref="Numa"/> from a file.</summary>
    /// <param name="filename">Path of the file to read.</param>
    /// <returns>The array read from the file.</returns>
    /// <exception cref="System.ArgumentException">The file cannot be opened, or it does not contain a valid serialized array.</exception>
    public static Numa Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("numaRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in numaRead: na not read: {filename}", e);
            }
        }
    }

    // leptonica: numaReadStream (numabasic.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// (1) The text is read ahead from the stream.  If the stream is seekable,
    ///     it is left after the serialized data (and the whitespace that
    ///     follows it); otherwise the rest of the stream is consumed.
    /// </remarks>
    public static Numa ReadStream(Stream fp)
    {
        CFloatScanner sc = new CFloatScanner(fp);
        try {
            return ReadStream(sc);
        } finally {
            sc.Release();
        }
    }

    internal static Numa ReadStream(CFloatScanner sc)
    {
        const string func = "numaReadStream";

        int ret = sc.Scan("\nNuma Version %d\n", out object?[] v);
        if (ret != 1)
            throw Lept.Arg(func, "not a numa file");
        int version = (int)v[0]!;
        if (version != L.NUMA_VERSION_NUMBER)
            throw Lept.Arg(func, "invalid numa version");
        if (sc.Scan("Number of numbers = %d\n", out v) != 1)
            throw Lept.Arg(func, "invalid number of numbers");
        int n = (int)v[0]!;

        if ((uint)n > MaxFloatArraySize) {  /* C compares an l_int32 with a l_uint32 */
            Log.Error(func, $"n = {n} > {MaxFloatArraySize}");
            throw Lept.Arg(func, "n too large");
        }
        Numa na = Create(n);

        for (int i = 0; i < n; i++) {
            if (sc.Scan("  [%d] = %f\n", out v) != 2)
                throw Lept.Arg(func, "bad input data");
            na.AddNumber((float)v[1]!);
        }

            /* Optional data */
        if (sc.Scan("startx = %f, delx = %f\n", out v) == 2) {
            float startx = (float)v[0]!;
            float delx = (float)v[1]!;
            na.SetParameters(startx, delx);
        }

        return na;
    }

    // leptonica: numaReadMem (numabasic.c)
    /// <param name="data">numa serialization; in ascii</param>
    public static Numa ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (ArgumentException) {
            Log.Error("numaReadMem", "numa not read");
            throw;
        }
    }

    // leptonica: numaWriteDebug (numabasic.c)
    /// <remarks>
    /// (1) Debug version, intended for use in the library when writing
    ///     to files in a temp directory with names that are compiled in.
    ///     This is used instead of numaWrite() for all such library calls.
    /// (2) The global variable LeptDebugOK defaults to false, and can be set
    ///     or cleared by the function setLeptDebugOK().
    /// </remarks>
    public void WriteDebug(string filename)
    {
        if (WriteFile.LeptDebugOK) {
            Write(filename);
        } else {
            Log.Info("numaWriteDebug", $"write to named temp file {filename} is disabled");
        }
    }

    // leptonica: numaWrite (numabasic.c)
    /// <summary>Writes the array to a file in the serialized text format.</summary>
    /// <param name="filename">Path of the file to write.</param>
    /// <exception cref="System.ArgumentException">The file cannot be opened for writing.</exception>
    public void Write(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, "wb");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("numaWrite", $"stream not opened: {filename}");
        }
        using (fp) {
            WriteStream(fp);
        }
    }

    // leptonica: numaWriteStream (numabasic.c)
    /// <param name="fp">output stream; use null to write to stderr</param>
    public void WriteStream(Stream? fp)
    {
        if (fp == null) {
            WriteStderr();
            return;
        }

        int n = Count;
        CStreamText.Fprintf(fp, "\nNuma Version %d\n", L.NUMA_VERSION_NUMBER);
        CStreamText.Fprintf(fp, "Number of numbers = %d\n", n);
        for (int i = 0; i < n; i++)
            CStreamText.Fprintf(fp, "  [%d] = %f\n", i, array[i]);
        CStreamText.Fprintf(fp, "\n");

            /* Optional data */
        GetParameters(out float startx, out float delx);
        if (startx != 0.0 || delx != 1.0)
            CStreamText.Fprintf(fp, "startx = %f, delx = %f\n", startx, delx);
    }

    // leptonica: numaWriteStderr (numabasic.c)
    /// <summary>Writes the array in the serialized text format to the standard error stream.</summary>
    public void WriteStderr()
    {
        int n = Count;
        CStreamText.Stderr("\nNuma Version %d\n", L.NUMA_VERSION_NUMBER);
        CStreamText.Stderr("Number of numbers = %d\n", n);
        for (int i = 0; i < n; i++)
            CStreamText.Stderr("  [%d] = %f\n", i, array[i]);
        CStreamText.Stderr("\n");

            /* Optional data */
        GetParameters(out float startx, out float delx);
        if (startx != 0.0 || delx != 1.0)
            CStreamText.Stderr("startx = %f, delx = %f\n", startx, delx);
    }

    // leptonica: numaWriteMem (numabasic.c)
    /// <summary>Serializes a numa in memory and returns the ascii bytes.</summary>
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }
}

/// <summary>Array of Numa (struct Numaa).</summary>
public sealed partial class Numaa
{
    /* Bounds on initial array size */
    private const uint MaxPtrArraySize = 1000000;  /* for numaa */
    private const int InitialArraySize = 50;       /* n'importe quoi */

    private Numa?[] numa;   /* array of Numa; its length is nalloc */
    private int n;          /* number of Numa in ptr array         */

    private Numaa(int nalloc)
    {
        numa = new Numa?[nalloc];
        n = 0;
    }

    /// <summary>Gets the number of slots allocated in the array of <see cref="Numa"/> objects.</summary>
    public int Nalloc => numa.Length;

    /*--------------------------------------------------------------------------*
     *                     Numaa creation, destruction                          *
     *--------------------------------------------------------------------------*/
    // leptonica: numaaCreate (numabasic.c)
    /// <summary>Creates an empty array of arrays.</summary>
    /// <param name="n">Number of <see cref="Numa"/> slots to allocate; 0 for the default.</param>
    /// <returns>The new array of arrays.</returns>
    public static Numaa Create(int n)
    {
        if (n <= 0 || n > MaxPtrArraySize)
            n = InitialArraySize;

        return new Numaa(n);
    }

    // leptonica: numaaCreateFull (numabasic.c)
    /// <param name="nptr">Number of <see cref="Numa"/> slots to allocate.</param>
    /// <param name="n">Size of each individual <see cref="Numa"/> to allocate; 0 for the default.</param>
    /// <remarks>
    /// (1) This allocates numaa and fills the array with allocated numas.
    ///     In use, after calling this function, use
    ///         naa.AddNumber(index, val);
    ///     to add val to the index-th numa in naa.
    /// </remarks>
    public static Numaa CreateFull(int nptr, int n)
    {
        Numaa naa = Create(nptr);
        for (int i = 0; i < nptr; i++) {
            Numa na = Numa.Create(n);
            naa.AddNuma(na, L.L_INSERT);
        }

        return naa;
    }

    // leptonica: numaaTruncate (numabasic.c)
    /// <remarks>
    /// (1) This identifies the largest index containing a numa that
    ///     has any numbers within it, destroys all numa beyond that
    ///     index, and resets the count.
    /// </remarks>
    public void Truncate()
    {
        int n = Count;
        int i;
        for (i = n - 1; i >= 0; i--) {
            Numa? na = numa[i];
            if (na == null) {
                /* C: numaaGetNuma() logs an error and returns NULL */
                Log.Error("numaClone", "na not defined");
                continue;
            }
            int nn = na.Count;
            if (nn == 0)
                numa[i] = null;
            else
                break;
        }
        this.n = i + 1;
    }

    /*--------------------------------------------------------------------------*
     *                              Add Numa to Numaa                           *
     *--------------------------------------------------------------------------*/
    // leptonica: numaaAddNuma (numabasic.c)
    /// <param name="na">to be added</param>
    /// <param name="copyflag">L_INSERT, L_COPY, L_CLONE</param>
    public void AddNuma(Numa na, int copyflag)
    {
        Numa nac;
        if (copyflag == L.L_INSERT)
            nac = na;
        else if (copyflag == L.L_COPY)
            nac = na.Copy();
        else if (copyflag == L.L_CLONE)
            nac = na.Clone();
        else
            throw Lept.Arg("numaaAddNuma", "invalid copyflag");

        int n = Count;
        if (n >= Nalloc)
            ExtendArray();
        numa[n] = nac;
        this.n++;
    }

    // leptonica: numaaExtendArray (numabasic.c)
    /// <remarks>(1) The max number of numa ptrs is 1M.</remarks>
    private void ExtendArray()
    {
        if (Nalloc > MaxPtrArraySize)  /* belt & suspenders */
            throw Lept.Fail("numaaExtendArray", "naa has too many ptrs");
        long oldsize = (long)Nalloc * 8;   /* sizeof(NUMA *) */
        long newsize = 2 * oldsize;
        if (newsize > 8L * MaxPtrArraySize)
            throw Lept.Fail("numaaExtendArray", "newsize > 8 MB; too large");

        Array.Resize(ref numa, Nalloc * 2);
    }

    /*----------------------------------------------------------------------*
     *                           Numaa accessors                            *
     *----------------------------------------------------------------------*/
    // leptonica: numaaGetCount (numabasic.c)
    /// <summary>Count of numa; 0 if no numa.</summary>
    public int Count => n;

    // leptonica: numaaGetNumaCount (numabasic.c)
    /// <summary>Count of numbers in the referenced numa.</summary>
    /// <param name="index">of numa in naa</param>
    public int GetNumaCount(int index)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("numaaGetNumaCount", "invalid index into naa");
        Numa? na = numa[index];
        if (na == null) {
            /* C: numaGetCount() logs an error and returns 0 */
            Log.Error("numaGetCount", "na not defined");
            return 0;
        }
        return na.Count;
    }

    // leptonica: numaaGetNumberCount (numabasic.c)
    /// <summary>Total number of numbers in the numaa; 0 if no numbers.</summary>
    public int GetNumberCount()
    {
        int n = Count;
        int sum = 0;
        for (int i = 0; i < n; i++) {
            Numa? na = numa[i];
            if (na == null) {
                /* C: numaaGetNuma() and numaGetCount() log errors; the count is 0 */
                Log.Error("numaClone", "na not defined");
                Log.Error("numaGetCount", "na not defined");
                continue;
            }
            sum += na.Count;
        }

        return sum;
    }

    // leptonica: numaaGetPtrArray (numabasic.c)
    /// <summary>Returns the internal array of Numa.</summary>
    /// <remarks>
    /// (1) This function is convenient for doing direct manipulation on
    ///     a fixed size array of Numas.  To do this, it sets the count
    ///     to the full size of the allocated array of Numa ptrs.
    ///     The originating Numaa owns this array.
    /// (2) Intended usage:
    ///       Numaa naa = Numaa.Create(n);
    ///       Numa?[] array = naa.GetPtrArray();
    ///        ...  [manipulate Numas directly on the array]
    /// (3) Cautions:
    ///      ~ Do not call any functions on the Numaa, other than
    ///        reading when you're finished with the array.
    ///        Adding a Numa will force a resize, replacing the internal array.
    ///      ~ Do not address the array outside its allocated size.
    /// </remarks>
    public Numa?[] GetPtrArray()
    {
        n = Nalloc;
        return numa;
    }

    // leptonica: numaaGetNuma (numabasic.c)
    /// <param name="index">to the index-th numa</param>
    /// <param name="accessflag">L_COPY or L_CLONE</param>
    public Numa GetNuma(int index, int accessflag)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("numaaGetNuma", "index not valid");

        if (accessflag == L.L_COPY)
            return (numa[index] ?? throw Lept.Arg("numaCopy", "na not defined")).Copy();
        else if (accessflag == L.L_CLONE)
            return (numa[index] ?? throw Lept.Arg("numaClone", "na not defined")).Clone();
        else
            throw Lept.Arg("numaaGetNuma", "invalid accessflag");
    }

    // leptonica: numaaReplaceNuma (numabasic.c)
    /// <param name="index">to the index-th numa</param>
    /// <param name="na">insert and replace any existing one</param>
    /// <remarks>
    /// (1) Any existing numa is dropped, and the input one
    ///     is inserted in its place.
    /// (2) If the index is invalid, this throws.
    /// </remarks>
    public void ReplaceNuma(int index, Numa na)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("numaaReplaceNuma", "index not valid");

        numa[index] = na;
    }

    // leptonica: numaaGetValue (numabasic.c)
    /// <param name="i">index of numa within numaa</param>
    /// <param name="j">index into numa</param>
    /// <param name="fval">float value</param>
    /// <param name="ival">int value (the float value truncated)</param>
    public void GetValue(int i, int j, out float fval, out int ival)
    {
        fval = 0.0f;
        ival = 0;
        int n = Count;
        if (i < 0 || i >= n)
            throw Lept.Arg("numaaGetValue", "invalid index into naa");
        Numa na = numa[i] ?? throw Lept.Arg("numaaGetValue", "na not defined");
        if (j < 0 || j >= na.Count)
            throw Lept.Arg("numaaGetValue", "invalid index into na");
        float[] array = na.GetFArray(L.L_NOCOPY);
        fval = array[j];
        ival = (int)(array[j]);
    }

    // leptonica: numaaAddNumber (numabasic.c)
    /// <param name="index">of numa within numaa</param>
    /// <param name="val">float or int to be added; stored as a float</param>
    /// <remarks>(1) Adds to an existing numa only.</remarks>
    public void AddNumber(int index, float val)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("numaaAddNumber", "invalid index in naa");

        Numa na = GetNuma(index, L.L_CLONE);
        na.AddNumber(val);
    }

    /*----------------------------------------------------------------------*
     *                      Serialize numaa for I/O                         *
     *----------------------------------------------------------------------*/
    // leptonica: numaaRead (numabasic.c)
    /// <summary>Reads a serialized <see cref="Numaa"/> from a file.</summary>
    /// <param name="filename">Path of the file to read.</param>
    /// <returns>The array of arrays read from the file.</returns>
    /// <exception cref="System.ArgumentException">The file cannot be opened, or it does not contain a valid serialized array of arrays.</exception>
    public static Numaa Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("numaaRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in numaaRead: naa not read: {filename}", e);
            }
        }
    }

    // leptonica: numaaReadStream (numabasic.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// (1) The text is read ahead from the stream.  If the stream is seekable,
    ///     it is left after the serialized data (and the whitespace that
    ///     follows it); otherwise the rest of the stream is consumed.
    /// </remarks>
    public static Numaa ReadStream(Stream fp)
    {
        CFloatScanner sc = new CFloatScanner(fp);
        try {
            return ReadStream(sc);
        } finally {
            sc.Release();
        }
    }

    private static Numaa ReadStream(CFloatScanner sc)
    {
        const string func = "numaaReadStream";

        int ret = sc.Scan("\nNumaa Version %d\n", out object?[] v);
        if (ret != 1)
            throw Lept.Arg(func, "not a numa file");
        int version = (int)v[0]!;
        if (version != L.NUMA_VERSION_NUMBER)
            throw Lept.Arg(func, "invalid numaa version");
        if (sc.Scan("Number of numa = %d\n\n", out v) != 1)
            throw Lept.Arg(func, "invalid number of numa");
        int n = (int)v[0]!;

        if ((uint)n > MaxPtrArraySize) {  /* C compares an l_int32 with a l_uint32 */
            Log.Error(func, $"n = {n} > {MaxPtrArraySize}");
            throw Lept.Arg(func, "n too large");
        }
        Numaa naa = Create(n);

        for (int i = 0; i < n; i++) {
            if (sc.Scan("Numa[%d]:", out v) != 1)
                throw Lept.Arg(func, "invalid numa header");
            Numa na;
            try {
                na = Numa.ReadStream(sc);
            } catch (ArgumentException) {
                throw Lept.Arg(func, "na not made");
            }
            naa.AddNuma(na, L.L_INSERT);
        }

        return naa;
    }

    // leptonica: numaaReadMem (numabasic.c)
    /// <param name="data">numaa serialization; in ascii</param>
    public static Numaa ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (ArgumentException) {
            Log.Error("numaaReadMem", "naa not read");
            throw;
        }
    }

    // leptonica: numaaWrite (numabasic.c)
    /// <summary>Writes the array of arrays to a file in the serialized text format.</summary>
    /// <param name="filename">Path of the file to write.</param>
    /// <exception cref="System.ArgumentException">The file cannot be opened for writing.</exception>
    public void Write(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, "wb");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("numaaWrite", $"stream not opened: {filename}");
        }
        using (fp) {
            WriteStream(fp);
        }
    }

    // leptonica: numaaWriteStream (numabasic.c)
    /// <param name="fp">output stream</param>
    public void WriteStream(Stream fp)
    {
        int n = Count;
        CStreamText.Fprintf(fp, "\nNumaa Version %d\n", L.NUMA_VERSION_NUMBER);
        CStreamText.Fprintf(fp, "Number of numa = %d\n\n", n);
        for (int i = 0; i < n; i++) {
            Numa na = GetNuma(i, L.L_CLONE);
            CStreamText.Fprintf(fp, "Numa[%d]:", i);
            na.WriteStream(fp);
        }
    }

    // leptonica: numaaWriteMem (numabasic.c)
    /// <summary>Serializes a numaa in memory and returns the ascii bytes.</summary>
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }
}

/// <summary>fprintf / lept_stderr with the C runtime formatting of <see cref="CFormat"/>.</summary>
internal static class CStreamText
{
    /// <summary>fprintf(fp, format, ...): writes the formatted text as bytes.</summary>
    public static void Fprintf(Stream fp, string format, params object?[] args)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(CFormat.Sprintf(format, args));
        fp.Write(bytes, 0, bytes.Length);
    }

    /// <summary>lept_stderr(format, ...): writes the formatted text to the standard error stream.</summary>
    public static void Stderr(string format, params object?[] args)
    {
        Console.Error.Write(CFormat.Sprintf(format, args));
    }
}

/// <summary>
/// Reads from a <see cref="Stream"/> with the semantics of C fscanf for the formats of
/// the Numa and Numaa serialization (whitespace, literal text, %d and %f), using
/// <see cref="CFormat.Sscanf"/>.
/// </summary>
/// <remarks>
/// The stream is read to its end when the scanner is created.  <see cref="Release"/>
/// positions a seekable stream after the text consumed by the scans (as the FILE* would
/// be); a stream that cannot seek stays at its end.
/// </remarks>
internal sealed class CFloatScanner
{
    private readonly Stream stream;
    private readonly long origin;   /* stream position at the start; -1 if not seekable */
    private readonly string text;   /* one char per input byte */
    private int pos;

    public CFloatScanner(Stream stream)
    {
        this.stream = stream;
        origin = stream.CanSeek ? stream.Position : -1;
        using MemoryStream ms = new MemoryStream();
        stream.CopyTo(ms);
        text = Encoding.Latin1.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        pos = 0;
    }

    // Same set as C isspace() in the "C" locale.
    private static bool IsSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r');

    /// <summary>Positions a seekable stream after the consumed text.</summary>
    public void Release()
    {
        if (origin >= 0)
            stream.Position = origin + pos;
    }

    /// <summary>
    /// Like fscanf(fp, format, ...): the characters matched are consumed.  Nothing is
    /// consumed if the scan stops before the first conversion completes.
    /// </summary>
    /// <param name="format">the fscanf format</param>
    /// <param name="values">one slot per conversion, as in <see cref="CFormat.Sscanf"/></param>
    /// <returns>number of items assigned; -1 if the input ended before the first conversion</returns>
    public int Scan(string format, out object?[] values)
    {
        /* A window of the text that is large enough for the format: the whitespace
         * that the format can skip at the start, the lines that it spans, and the
         * whitespace that a trailing whitespace of the format can skip. */
        int start = pos;
        int fi = 0;
        while (fi < format.Length && IsSpace(format[fi]))
            fi++;
        if (fi > 0) {
            while (start < text.Length && IsSpace(text[start]))
                start++;
        }
        int lines = 0;
        for (int i = fi; i < format.Length; i++) {
            if (format[i] == '\n')
                lines++;
        }
        if (lines == 0)
            lines = 1;
        int end = start;
        while (end < text.Length && lines > 0) {
            if (text[end++] == '\n')
                lines--;
        }
        while (end < text.Length && IsSpace(text[end]))
            end++;

        string window = text.Substring(pos, end - pos);
        int ret = CFormat.Sscanf(window, format + "%n", out values);
        if (values.Length > 0 && values[^1] is int consumed)
            pos += consumed;
        return ret;
    }
}
