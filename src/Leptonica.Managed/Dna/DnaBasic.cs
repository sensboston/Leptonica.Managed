using System;
using System.IO;
using System.Text;

namespace Leptonica;

/*
 *  dnabasic.c: L_Dna (array of doubles) and L_Dnaa (array of L_Dna):
 *  creation, copy, accessors and serialization.
 *
 *  (1) The L_Dna is a struct holding an array of doubles.  It can also
 *      be used to store int values, up to the full precision
 *      of int32.  Always use it whenever integers larger than a
 *      few million need to be stored.
 *
 *  (2) Always use the accessors in this file, never the fields directly.
 *
 *  (3) Storing and retrieving numbers:
 *
 *     * to append a new number to the array, use AddNumber().  If
 *       the number is an int, it will will automatically be converted
 *       to double and stored.
 *
 *     * to reset a value stored in the array, use SetValue().
 *
 *     * to increment or decrement a value stored in the array,
 *       use ShiftValue().
 *
 *     * to obtain a value from the array, use either GetIValue()
 *       or GetDValue(), depending on whether you are retrieving
 *       an integer or a double.
 *
 *  (4) int <--> double conversions:
 *
 *      Conversions go automatically from int --> double,
 *      without loss of precision.  You must cast (int)
 *      to go from double --> int because you're truncating
 *      to the integer value.
 *
 *  (5) As with other arrays in leptonica, the L_Dna has both an allocated
 *      size and a count of the stored numbers.  When you add a number, it
 *      goes on the end of the array, and causes a realloc if the array
 *      is already filled.  However, in situations where you want to
 *      add numbers randomly into an array, such as when you build a
 *      histogram, you must set the count of stored numbers in advance.
 *      This is done by setting Count.  If you set a count larger
 *      than the allocated array, it does a realloc to the size requested.
 *
 *  (6) In situations where the data in a L_Dna correspond to a function
 *      y(x), the values can be either at equal spacings in x or at
 *      arbitrary spacings.  For the former, we can represent all x values
 *      by two parameters: startx (corresponding to y[0]) and delx
 *      for the change in x for adjacent values y[i] and y[i+1].
 *      startx and delx are initialized to 0.0 and 1.0, rsp.
 *      For arbitrary spacings, we use a second L_Dna, and the two
 *      L_Dnas are typically denoted dnay and dnax.
 *
 *  Differences from C:
 *    - No reference counting and no l_dnaDestroy(), l_dnaaDestroy(): the GC
 *      owns the objects (not ported).  l_dnaClone() and l_dnaaGetDna(L_CLONE)
 *      return the same reference.
 *    - FILE* becomes Stream.  Text is written as ASCII with the formats of the
 *      C fprintf calls, through CFormat.Sprintf, byte for byte.  Reading follows
 *      the fscanf formats of the C code, through CFormat.Sscanf.
 *    - l_dnaWriteStream() with a null stream writes to stderr, as in C.
 *    - The *Mem variants take a ReadOnlySpan&lt;byte&gt; and return a byte[]
 *      (without the terminating NUL that C appends to its buffer).
 *    - fopenReadStream() and friends are replaced by System.IO.
 *    - Functions that return an error code or NULL in C throw instead.
 *    - The array of an L_Dna is a double[] whose length is nalloc.
 *    - After a failed optional scan (the "startx = ..." line) the text position
 *      is where the scan started; fscanf may have consumed a partial match.
 */

/// <summary>Constants from array_internal.h used by the serialization of L_Dna and L_Dnaa.</summary>
public static partial class L
{
    /// <summary>Version number used in the serialized <see cref="L_Dna"/> and <see cref="L_Dnaa"/> formats.</summary>
    public const int DNA_VERSION_NUMBER = 1;
}

/// <summary>
/// Array of doubles (struct L_Dna).
/// </summary>
public sealed partial class L_Dna
{
    /* Bounds on initial array size */
    private const int MaxDoubleArraySize = 100000000;   /* for dna */
    private const int InitialArraySize = 50;            /* n'importe quoi */

    internal double[] array;   /* double array; its length is nalloc */
    internal int n;            /* number of numbers stored            */
    private double startx;     /* x value corresponding to array[0]   */
    private double delx;       /* x increment between array values    */

    private L_Dna(int nalloc)
    {
        array = new double[nalloc];
        n = 0;
        startx = 0.0;
        delx = 1.0;
    }

    /// <summary>Number of allocated doubles (da->nalloc).</summary>
    public int Nalloc => array.Length;

    /*--------------------------------------------------------------------------*
     *                 Dna creation, destruction, copy, clone, etc.             *
     *--------------------------------------------------------------------------*/
    // leptonica: l_dnaCreate (dnabasic.c)
    /// <param name="n">size of number array to be alloc'd; 0 for default</param>
    public static L_Dna Create(int n)
    {
        if (n <= 0 || n > MaxDoubleArraySize)
            n = InitialArraySize;

        return new L_Dna(n);
    }

    // leptonica: l_dnaCreateFromIArray (dnabasic.c)
    /// <param name="iarray">integer array</param>
    /// <param name="size">of the array</param>
    /// <remarks>
    /// (1) We can't insert this int array into the l_dna, because a l_dna
    ///     takes a double array.  So this just copies the data from the
    ///     input array into the l_dna.  The input array continues to be
    ///     owned by the caller.
    /// </remarks>
    public static L_Dna CreateFromIArray(int[] iarray, int size)
    {
        if (size <= 0)
            throw Lept.Arg("l_dnaCreateFromIArray", "size must be > 0");

        L_Dna da = Create(size);
        for (int i = 0; i < size; i++)
            da.AddNumber(iarray[i]);

        return da;
    }

    // leptonica: l_dnaCreateFromDArray (dnabasic.c)
    /// <param name="darray">float</param>
    /// <param name="size">of the array</param>
    /// <param name="copyflag">L_INSERT or L_COPY</param>
    /// <remarks>
    /// (1) With L_INSERT, ownership of the input array is transferred
    ///     to the returned l_dna, and all %size elements are considered
    ///     to be valid.
    /// (2) C# only: %size must not exceed the length of the array (the C code
    ///     reads or writes out of bounds); with L_INSERT the allocated size
    ///     of the l_dna is the length of the array.
    /// </remarks>
    public static L_Dna CreateFromDArray(double[] darray, int size, int copyflag)
    {
        if (size <= 0)
            throw Lept.Arg("l_dnaCreateFromDArray", "size must be > 0");
        if (copyflag != L.L_INSERT && copyflag != L.L_COPY)
            throw Lept.Arg("l_dnaCreateFromDArray", "invalid copyflag");
        if (size > darray.Length)
            throw Lept.Arg("l_dnaCreateFromDArray", "size > length of darray");

        L_Dna da = Create(size);
        if (copyflag == L.L_INSERT) {
            da.array = darray;
            da.n = size;
        } else {  /* just copy the contents */
            for (int i = 0; i < size; i++)
                da.AddNumber(darray[i]);
        }

        return da;
    }

    // leptonica: l_dnaMakeSequence (dnabasic.c)
    /// <summary>Makes a l_dna of evenly spaced values.</summary>
    /// <param name="startval"></param>
    /// <param name="increment"></param>
    /// <param name="size">of sequence</param>
    public static L_Dna MakeSequence(double startval, double increment, int size)
    {
        L_Dna da = Create(size);

        for (int i = 0; i < size; i++) {
            double val = startval + i * increment;
            da.AddNumber(val);
        }

        return da;
    }

    // leptonica: l_dnaCopy (dnabasic.c)
    /// <remarks>(1) This removes unused ptrs above da->n.</remarks>
    public L_Dna Copy()
    {
        L_Dna dac = Create(n);
        dac.startx = startx;
        dac.delx = delx;

        for (int i = 0; i < n; i++)
            dac.AddNumber(array[i]);

        return dac;
    }

    // leptonica: l_dnaClone (dnabasic.c)
    /// <summary>Returns the same l_dna (a reference; changes are seen through both).</summary>
    public L_Dna Clone()
    {
        return this;
    }

    // leptonica: l_dnaEmpty (dnabasic.c)
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
     *                  Dna: add/remove number and extend array                 *
     *--------------------------------------------------------------------------*/
    // leptonica: l_dnaAddNumber (dnabasic.c)
    /// <param name="val">float or int to be added; stored as a float</param>
    public void AddNumber(double val)
    {
        int n = Count;
        if (n >= Nalloc)
            ExtendArray();
        array[n] = val;
        this.n++;
    }

    // leptonica: l_dnaExtendArray (dnabasic.c)
    /// <remarks>
    /// (1) Doubles the size of the array.
    /// (2) The max number of doubles is 100M.
    /// </remarks>
    private void ExtendArray()
    {
        if (Nalloc > MaxDoubleArraySize)
            throw Lept.Fail("l_dnaExtendArray", "da at maximum size; can't extend");
        int newalloc;
        if (Nalloc > MaxDoubleArraySize / 2)
            newalloc = MaxDoubleArraySize;
        else
            newalloc = 2 * Nalloc;
        // NOTE(upstream): with nalloc == MaxDoubleArraySize nothing grows, and the caller
        // then writes one element past the array; here that is an IndexOutOfRangeException.
        Array.Resize(ref array, newalloc);
    }

    // leptonica: l_dnaInsertNumber (dnabasic.c)
    /// <param name="index">location in da to insert new value</param>
    /// <param name="val">float64 or integer to be added</param>
    /// <remarks>
    /// (1) This shifts da[i] --> da[i + 1] for all i >= %index,
    ///     and then inserts %val as da[%index].
    /// (2) It should not be used repeatedly on large arrays,
    ///     because the function is O(n).
    /// </remarks>
    public void InsertNumber(int index, double val)
    {
        int n = Count;
        if (index < 0 || index > n)
            throw Lept.Arg("l_dnaInsertNumber", $"index {index} not in [0,...,{n}]");

        if (n >= Nalloc)
            ExtendArray();
        for (int i = n; i > index; i--)
            array[i] = array[i - 1];
        array[index] = val;
        this.n++;
    }

    // leptonica: l_dnaRemoveNumber (dnabasic.c)
    /// <param name="index">element to be removed</param>
    /// <remarks>
    /// (1) This shifts da[i] --> da[i - 1] for all i > %index.
    /// (2) It should not be used repeatedly on large arrays,
    ///     because the function is O(n).
    /// </remarks>
    public void RemoveNumber(int index)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("l_dnaRemoveNumber", $"index {index} not in [0,...,{n - 1}]");

        for (int i = index + 1; i < n; i++)
            array[i - 1] = array[i];
        this.n--;
    }

    // leptonica: l_dnaReplaceNumber (dnabasic.c)
    /// <param name="index">element to be replaced</param>
    /// <param name="val">new value to replace old one</param>
    public void ReplaceNumber(int index, double val)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("l_dnaReplaceNumber", $"index {index} not in [0,...,{n - 1}]");

        array[index] = val;
    }

    /*----------------------------------------------------------------------*
     *                             Dna accessors                            *
     *----------------------------------------------------------------------*/
    // leptonica: l_dnaGetCount, l_dnaSetCount (dnabasic.c)
    /// <summary>Number of stored numbers.</summary>
    /// <remarks>
    /// (1) If the new count &lt;= Nalloc, setting it resets da->n.
    ///     Using a new count = 0 is equivalent to <see cref="Empty"/>.
    /// (2) If the new count > Nalloc, setting it causes a realloc
    ///     to a size Nalloc = the new count.
    /// (3) All the previously unused values in da are set to 0.0.
    /// </remarks>
    public int Count
    {
        get => n;
        set {
            if (value > Nalloc)
                Array.Resize(ref array, value);
            n = value;
        }
    }

    // leptonica: l_dnaGetDValue (dnabasic.c)
    /// <param name="index">into l_dna</param>
    public double GetDValue(int index)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("l_dnaGetDValue", "index not valid");

        return array[index];
    }

    // leptonica: l_dnaGetIValue (dnabasic.c)
    /// <param name="index">into l_dna</param>
    public int GetIValue(int index)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("l_dnaGetIValue", "index not valid");

        double val = array[index];
        return (int)(val + (val < 0 ? -1 : 1) * 0.5);   /* L_SIGN(val) */
    }

    // leptonica: l_dnaSetValue (dnabasic.c)
    /// <param name="index">to element to be set</param>
    /// <param name="val">to set element</param>
    public void SetValue(int index, double val)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("l_dnaSetValue", "index not valid");

        array[index] = val;
    }

    // leptonica: l_dnaShiftValue (dnabasic.c)
    /// <param name="index">to element to change relative to the current value</param>
    /// <param name="diff">increment if diff > 0 or decrement if diff &lt; 0</param>
    public void ShiftValue(int index, double diff)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("l_dnaShiftValue", "index not valid");

        array[index] += diff;
    }

    // leptonica: l_dnaGetIArray (dnabasic.c)
    /// <summary>Returns a copy of the internal array, integerized by rounding.</summary>
    /// <remarks>
    /// (1) A copy of the array is made, because we need to
    ///     generate an integer array from the bare double array.
    /// (2) The array size is determined by the number of stored numbers,
    ///     not by the size of the allocated array in the l_dna.
    /// (3) This function is provided to simplify calculations
    ///     using the bare internal array, rather than continually
    ///     calling accessors on the l_dna.  It is typically used
    ///     on an array of size 256.
    /// </remarks>
    public int[] GetIArray()
    {
        int n = Count;
        int[] iarray = new int[n];
        for (int i = 0; i < n; i++) {
            int ival = GetIValue(i);
            iarray[i] = ival;
        }

        return iarray;
    }

    // leptonica: l_dnaGetDArray (dnabasic.c)
    /// <summary>Returns either the bare internal array or a copy of it.</summary>
    /// <param name="copyflag">L_NOCOPY or L_COPY</param>
    /// <remarks>
    /// (1) If %copyflag == L_COPY, it makes a copy (of length Count).
    ///     Otherwise, it operates directly on the bare array of the l_dna
    ///     (of length Nalloc).
    /// (2) Very important: for L_NOCOPY, any writes to the array
    ///     will be in the l_dna.  Do not write beyond the size of
    ///     the count field, because it will not be accessible
    ///     from the l_dna!  If necessary, be sure to set the count
    ///     field to a larger number (such as the alloc size)
    ///     BEFORE calling this function.  Creating with l_dnaMakeConstant()
    ///     is another way to insure full initialization.
    /// </remarks>
    public double[] GetDArray(int copyflag)
    {
        double[] darray;

        if (copyflag == L.L_NOCOPY) {
            darray = array;
        } else {  /* copyflag == L_COPY */
            int n = Count;
            darray = new double[n];
            for (int i = 0; i < n; i++)
                darray[i] = array[i];
        }

        return darray;
    }

    // leptonica: l_dnaGetParameters (dnabasic.c)
    /// <param name="pstartx">startx</param>
    /// <param name="pdelx">delx</param>
    public void GetParameters(out double pstartx, out double pdelx)
    {
        pstartx = startx;
        pdelx = delx;
    }

    // leptonica: l_dnaSetParameters (dnabasic.c)
    /// <param name="startx">x value corresponding to da[0]</param>
    /// <param name="delx">difference in x values for the situation where the
    /// elements of da correspond to the evaluation of a
    /// function at equal intervals of size %delx</param>
    public void SetParameters(double startx, double delx)
    {
        this.startx = startx;
        this.delx = delx;
    }

    // leptonica: l_dnaCopyParameters (dnabasic.c)
    /// <summary>Copies startx and delx from the source to this (the destination).</summary>
    /// <param name="das">source</param>
    public void CopyParameters(L_Dna das)
    {
        das.GetParameters(out double start, out double binsize);
        SetParameters(start, binsize);
    }

    /*----------------------------------------------------------------------*
     *                        Serialize Dna for I/O                         *
     *----------------------------------------------------------------------*/
    /// <summary>Reads a serialized array of double values from a file.</summary>
    /// <param name="filename">The path of the file to read.</param>
    /// <returns>The array read from the file.</returns>
    /// <exception cref="ArgumentException">The file cannot be opened or its contents are not a valid serialized array.</exception>
    // leptonica: l_dnaRead (dnabasic.c)
    public static L_Dna Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("l_dnaRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in l_dnaRead: da not read: {filename}", e);
            }
        }
    }

    // leptonica: l_dnaReadStream (dnabasic.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// (1) fscanf takes %lf to read a double; fprintf takes %f to write it.
    /// (2) It is OK for the dna to be empty.
    /// (3) If the stream is not seekable, the reader consumes the rest of it.
    /// </remarks>
    public static L_Dna ReadStream(Stream fp)
    {
        DnaScanner sc = new DnaScanner(fp);
        try {
            return ReadStream(sc);
        } finally {
            sc.Release();
        }
    }

    internal static L_Dna ReadStream(DnaScanner sc)
    {
        const string func = "l_dnaReadStream";

        if (sc.Scan("\nL_Dna Version %d\n", out object?[] v) != 1)
            throw Lept.Arg(func, "not a l_dna file");
        int version = (int)v[0]!;
        if (version != L.DNA_VERSION_NUMBER)
            throw Lept.Arg(func, "invalid l_dna version");
        if (sc.Scan("Number of numbers = %d\n", out v) != 1)
            throw Lept.Arg(func, "invalid number of numbers");
        int n = (int)v[0]!;
        if (n < 0)
            throw Lept.Arg(func, "num doubles < 0");
        if (n > MaxDoubleArraySize)
            throw Lept.Arg(func, "too many doubles");
        if (n == 0) Log.Info(func, "the dna is empty");

        L_Dna da = Create(n);
        for (int i = 0; i < n; i++) {
            if (sc.Scan("  [%d] = %lf\n", out v) != 2)
                throw Lept.Arg(func, "bad input data");
            double val = (double)v[1]!;
            da.AddNumber(val);
        }

        /* Optional data */
        if (sc.Scan("startx = %lf, delx = %lf\n", out v) == 2) {
            double startx = (double)v[0]!;
            double delx = (double)v[1]!;
            da.SetParameters(startx, delx);
        }
        return da;
    }

    // leptonica: l_dnaReadMem (dnabasic.c)
    /// <param name="data">dna serialization; in ascii</param>
    public static L_Dna ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (ArgumentException) {
            Log.Error("l_dnaReadMem", "dna not read");
            throw;
        }
    }

    /// <summary>Writes the array of double values to a file in serialized form.</summary>
    /// <param name="filename">The path of the file to write.</param>
    /// <exception cref="ArgumentException">The file cannot be opened for writing.</exception>
    // leptonica: l_dnaWrite (dnabasic.c)
    public void Write(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, "wb");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("l_dnaWrite", $"stream not opened: {filename}");
        }
        using (fp) {
            WriteStream(fp);
        }
    }

    // leptonica: l_dnaWriteStream (dnabasic.c)
    /// <param name="fp">stream; use null to write to stderr</param>
    public void WriteStream(Stream? fp)
    {
        if (fp == null) {
            WriteStderr();
            return;
        }

        int n = Count;
        Fputs(fp, CFormat.Sprintf("\nL_Dna Version %d\n", L.DNA_VERSION_NUMBER));
        Fputs(fp, CFormat.Sprintf("Number of numbers = %d\n", n));
        for (int i = 0; i < n; i++)
            Fputs(fp, CFormat.Sprintf("  [%d] = %f\n", i, array[i]));
        Fputs(fp, "\n");

        /* Optional data */
        GetParameters(out double startx, out double delx);
        if (startx != 0.0 || delx != 1.0)
            Fputs(fp, CFormat.Sprintf("startx = %f, delx = %f\n", startx, delx));
    }

    /// <summary>Writes the array of double values in serialized form to the standard error stream.</summary>
    // leptonica: l_dnaWriteStderr (dnabasic.c)
    public void WriteStderr()
    {
        int n = Count;
        Console.Error.Write(CFormat.Sprintf("\nL_Dna Version %d\n", L.DNA_VERSION_NUMBER));
        Console.Error.Write(CFormat.Sprintf("Number of numbers = %d\n", n));
        for (int i = 0; i < n; i++)
            Console.Error.Write(CFormat.Sprintf("  [%d] = %f\n", i, array[i]));
        Console.Error.Write("\n");

        /* Optional data */
        GetParameters(out double startx, out double delx);
        if (startx != 0.0 || delx != 1.0)
            Console.Error.Write(CFormat.Sprintf("startx = %f, delx = %f\n", startx, delx));
    }

    // leptonica: l_dnaWriteMem (dnabasic.c)
    /// <summary>Serializes a dna in memory and returns the ascii bytes.</summary>
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }

    /// <summary>fputs() of ASCII text to a stream.</summary>
    internal static void Fputs(Stream fp, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        fp.Write(bytes, 0, bytes.Length);
    }
}

/// <summary>
/// Array of L_Dna (struct L_Dnaa).
/// </summary>
public sealed partial class L_Dnaa
{
    /* Bounds on initial array size */
    private const int MaxPtrArraySize = 1000000;   /* for dnaa */
    private const int InitialArraySize = 50;       /* n'importe quoi */

    private L_Dna?[] dna;   /* l_dna ptr array; its length is nalloc */
    private int n;          /* number of l_dna in ptr array          */

    private L_Dnaa(int nalloc)
    {
        dna = new L_Dna?[nalloc];
        n = 0;
    }

    /// <summary>Number of allocated l_dna ptrs (daa->nalloc).</summary>
    public int Nalloc => dna.Length;

    /*--------------------------------------------------------------------------*
     *                       Dnaa creation, destruction                         *
     *--------------------------------------------------------------------------*/
    // leptonica: l_dnaaCreate (dnabasic.c)
    /// <param name="n">size of l_dna ptr array to be alloc'd 0 for default</param>
    public static L_Dnaa Create(int n)
    {
        if (n <= 0 || n > MaxPtrArraySize)
            n = InitialArraySize;

        return new L_Dnaa(n);
    }

    // leptonica: l_dnaaCreateFull (dnabasic.c)
    /// <param name="nptr">size of dna ptr array to be alloc'd</param>
    /// <param name="n">size of individual dna arrays to be alloc'd 0 for default</param>
    /// <remarks>
    /// (1) This allocates a dnaa and fills the array with allocated dnas.
    ///     In use, after calling this function, use
    ///         daa.AddNumber(index, val);
    ///     to add val to the index-th dna in dnaa.
    /// </remarks>
    public static L_Dnaa CreateFull(int nptr, int n)
    {
        L_Dnaa daa = Create(nptr);
        for (int i = 0; i < nptr; i++) {
            L_Dna da = L_Dna.Create(n);
            daa.AddDna(da, L.L_INSERT);
        }

        return daa;
    }

    // leptonica: l_dnaaTruncate (dnabasic.c)
    /// <remarks>
    /// (1) This identifies the largest index containing a dna that
    ///     has any numbers within it, destroys all dna beyond that
    ///     index, and resets the count.
    /// </remarks>
    public void Truncate()
    {
        int i;
        int n = Count;
        for (i = n - 1; i >= 0; i--) {
            if (dna[i] == null)
                continue;
            L_Dna da = GetDna(i, L.L_CLONE);
            int nn = da.Count;
            if (nn == 0)
                dna[i] = null;
            else
                break;
        }
        this.n = i + 1;
    }

    /*--------------------------------------------------------------------------*
     *                             Add Dna to Dnaa                              *
     *--------------------------------------------------------------------------*/
    // leptonica: l_dnaaAddDna (dnabasic.c)
    /// <param name="da">to be added</param>
    /// <param name="copyflag">L_INSERT, L_COPY, L_CLONE</param>
    public void AddDna(L_Dna da, int copyflag)
    {
        L_Dna dac;
        if (copyflag == L.L_INSERT)
            dac = da;
        else if (copyflag == L.L_COPY)
            dac = da.Copy();
        else if (copyflag == L.L_CLONE)
            dac = da.Clone();
        else
            throw Lept.Arg("l_dnaaAddDna", "invalid copyflag");

        int n = Count;
        if (n >= Nalloc)
            ExtendArray();
        dna[n] = dac;
        this.n++;
    }

    // leptonica: l_dnaaExtendArray (dnabasic.c)
    /// <remarks>
    /// (1) Doubles the number of dna ptrs.
    /// (2) The max size of the dna array is 1M ptrs.
    /// </remarks>
    private void ExtendArray()
    {
        if (Nalloc > MaxPtrArraySize)  /* belt & suspenders */
            throw Lept.Fail("l_dnaaExtendArray", "daa has too many ptrs");
        long oldsize = (long)Nalloc * 8;   /* sizeof(L_DNA *) in C */
        long newsize = 2 * oldsize;
        if (newsize > 8L * MaxPtrArraySize)
            throw Lept.Fail("l_dnaaExtendArray", "newsize > 8 MB; too large");

        Array.Resize(ref dna, Nalloc * 2);
    }

    /*----------------------------------------------------------------------*
     *                           DNumaa accessors                           *
     *----------------------------------------------------------------------*/
    // leptonica: l_dnaaGetCount (dnabasic.c)
    /// <summary>Number of l_dna; 0 if no l_dna.</summary>
    public int Count => n;

    // leptonica: l_dnaaGetDnaCount (dnabasic.c)
    /// <summary>Count of numbers in the referenced l_dna.</summary>
    /// <param name="index">of l_dna in daa</param>
    public int GetDnaCount(int index)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("l_dnaaGetDnaCount", "invalid index into daa");
        return dna[index]!.Count;
    }

    // leptonica: l_dnaaGetNumberCount (dnabasic.c)
    /// <summary>Total number of numbers in the l_dnaa; 0 if no numbers.</summary>
    public int GetNumberCount()
    {
        int n = Count;
        int sum = 0;
        for (int i = 0; i < n; i++) {
            L_Dna da = GetDna(i, L.L_CLONE);
            sum += da.Count;
        }

        return sum;
    }

    // leptonica: l_dnaaGetDna (dnabasic.c)
    /// <param name="index">to the index-th l_dna</param>
    /// <param name="accessflag">L_COPY or L_CLONE</param>
    public L_Dna GetDna(int index, int accessflag)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("l_dnaaGetDna", "index not valid");

        if (accessflag == L.L_COPY)
            return (dna[index] ?? throw Lept.Arg("l_dnaCopy", "da not defined")).Copy();
        else if (accessflag == L.L_CLONE)
            return (dna[index] ?? throw Lept.Arg("l_dnaClone", "da not defined")).Clone();
        else
            throw Lept.Arg("l_dnaaGetDna", "invalid accessflag");
    }

    // leptonica: l_dnaaReplaceDna (dnabasic.c)
    /// <param name="index">to the index-th l_dna</param>
    /// <param name="da">insert and replace any existing one</param>
    /// <remarks>
    /// (1) Any existing l_dna is destroyed, and the input one
    ///     is inserted in its place.
    /// (2) If %index is invalid, this throws.
    /// </remarks>
    public void ReplaceDna(int index, L_Dna da)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("l_dnaaReplaceDna", "index not valid");

        dna[index] = da;
    }

    // leptonica: l_dnaaGetValue (dnabasic.c)
    /// <summary>Gets the double value at index j of the i-th l_dna.</summary>
    /// <param name="i">index of l_dna within l_dnaa</param>
    /// <param name="j">index into l_dna</param>
    public double GetValue(int i, int j)
    {
        int n = Count;
        if (i < 0 || i >= n)
            throw Lept.Arg("l_dnaaGetValue", "invalid index into daa");
        L_Dna da = dna[i]!;
        if (j < 0 || j >= da.n)
            throw Lept.Arg("l_dnaaGetValue", "invalid index into da");
        return da.array[j];
    }

    // leptonica: l_dnaaAddNumber (dnabasic.c)
    /// <param name="index">of l_dna within l_dnaa</param>
    /// <param name="val">number to be added; stored as a double</param>
    /// <remarks>(1) Adds to an existing l_dna only.</remarks>
    public void AddNumber(int index, double val)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("l_dnaaAddNumber", "invalid index in daa");

        L_Dna da = GetDna(index, L.L_CLONE);
        da.AddNumber(val);
    }

    /*----------------------------------------------------------------------*
     *                        Serialize Dna for I/O                         *
     *----------------------------------------------------------------------*/
    /// <summary>Reads a serialized array of arrays of double values from a file.</summary>
    /// <param name="filename">The path of the file to read.</param>
    /// <returns>The array of arrays read from the file.</returns>
    /// <exception cref="ArgumentException">The file cannot be opened or its contents are not a valid serialized array.</exception>
    // leptonica: l_dnaaRead (dnabasic.c)
    public static L_Dnaa Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("l_dnaaRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in l_dnaaRead: daa not read: {filename}", e);
            }
        }
    }

    // leptonica: l_dnaaReadStream (dnabasic.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// (1) It is OK for the dnaa to be empty.
    /// (2) If the stream is not seekable, the reader consumes the rest of it.
    /// </remarks>
    public static L_Dnaa ReadStream(Stream fp)
    {
        DnaScanner sc = new DnaScanner(fp);
        try {
            return ReadStream(sc);
        } finally {
            sc.Release();
        }
    }

    private static L_Dnaa ReadStream(DnaScanner sc)
    {
        const string func = "l_dnaaReadStream";

        if (sc.Scan("\nL_Dnaa Version %d\n", out object?[] v) != 1)
            throw Lept.Arg(func, "not a l_dna file");
        int version = (int)v[0]!;
        if (version != L.DNA_VERSION_NUMBER)
            throw Lept.Arg(func, "invalid l_dnaa version");
        if (sc.Scan("Number of L_Dna = %d\n\n", out v) != 1)
            throw Lept.Arg(func, "invalid number of l_dna");
        int n = (int)v[0]!;
        if (n < 0)
            throw Lept.Arg(func, "num l_dna <= 0");
        if (n > MaxPtrArraySize)
            throw Lept.Arg(func, "too many l_dna");
        if (n == 0) Log.Info(func, "the dnaa is empty");

        L_Dnaa daa = Create(n);
        for (int i = 0; i < n; i++) {
            if (sc.Scan("L_Dna[%d]:", out v) != 1)
                throw Lept.Arg(func, "invalid l_dna header");
            L_Dna da;
            try {
                da = L_Dna.ReadStream(sc);
            } catch (ArgumentException) {
                throw Lept.Arg(func, "da not made");
            }
            daa.AddDna(da, L.L_INSERT);
        }

        return daa;
    }

    // leptonica: l_dnaaReadMem (dnabasic.c)
    /// <param name="data">dnaa serialization; in ascii</param>
    public static L_Dnaa ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (ArgumentException) {
            Log.Error("l_dnaaReadMem", "daa not read");
            throw;
        }
    }

    /// <summary>Writes the array of arrays of double values to a file in serialized form.</summary>
    /// <param name="filename">The path of the file to write.</param>
    /// <exception cref="ArgumentException">The file cannot be opened for writing.</exception>
    // leptonica: l_dnaaWrite (dnabasic.c)
    public void Write(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, "wb");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("l_dnaaWrite", $"stream not opened: {filename}");
        }
        using (fp) {
            WriteStream(fp);
        }
    }

    // leptonica: l_dnaaWriteStream (dnabasic.c)
    /// <param name="fp">output stream</param>
    public void WriteStream(Stream fp)
    {
        int n = Count;
        L_Dna.Fputs(fp, CFormat.Sprintf("\nL_Dnaa Version %d\n", L.DNA_VERSION_NUMBER));
        L_Dna.Fputs(fp, CFormat.Sprintf("Number of L_Dna = %d\n\n", n));
        for (int i = 0; i < n; i++) {
            L_Dna da = GetDna(i, L.L_CLONE);
            L_Dna.Fputs(fp, CFormat.Sprintf("L_Dna[%d]:", i));
            da.WriteStream(fp);
        }
    }

    // leptonica: l_dnaaWriteMem (dnabasic.c)
    /// <summary>Serializes a dnaa in memory and returns the ascii bytes.</summary>
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }
}

/// <summary>
/// Scans the text of a stream with the semantics of consecutive fscanf calls, on top of
/// <see cref="CFormat.Sscanf"/>.  The remaining bytes of the stream are read once (each
/// byte becomes one char), and every scan continues where the previous one stopped.
/// </summary>
internal sealed class DnaScanner
{
    private const int InitialWindow = 256;

    private readonly Stream stream;
    private readonly string text;
    private readonly long start;   /* stream position of text[0]; -1 if not seekable */
    private int pos;               /* next unread char of text */

    public DnaScanner(Stream stream)
    {
        this.stream = stream;
        start = stream.CanSeek ? stream.Position : -1;
        using MemoryStream ms = new MemoryStream();
        stream.CopyTo(ms);
        text = Encoding.Latin1.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    /// <summary>Gives the unread part of the text back to the stream, if that is possible.</summary>
    public void Release()
    {
        if (start >= 0)
            stream.Position = start + pos;
    }

    private static bool IsSpace(int c) => c == ' ' || (c >= '\t' && c <= '\r');

    /// <summary>
    /// Like fscanf(fp, format, ...) for a format without %n: returns the number of assigned
    /// items (-1 if the input ended before the first conversion), the values as in
    /// <see cref="CFormat.Sscanf"/>.  The position moves only if the whole format matched.
    /// </summary>
    public int Scan(string format, out object?[] values)
    {
        /* A leading literal that does not match fails before anything is consumed */
        char first = format[0];
        if (first != '%' && !IsSpace(first) && pos < text.Length && text[pos] != first) {
            values = Array.Empty<object?>();
            return 0;
        }

        /* Scan a window of the text; if the scan reached the end of the window,
           the result may depend on text beyond it, so scan again with a larger one. */
        string fmt = format + "%n";
        for (int window = InitialWindow; ; window *= 2) {
            int len = Math.Min(window, text.Length - pos);
            int ret = CFormat.Sscanf(text.Substring(pos, len), fmt, out values);
            bool whole = pos + len >= text.Length;
            if (values[^1] is int consumed) {
                if (whole || consumed < len) {
                    pos += consumed;
                    return ret;
                }
            } else if (whole) {
                return ret;
            }
        }
    }
}
