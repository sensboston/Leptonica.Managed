using System;
using System.IO;
using System.Text;

namespace Leptonica;

/*
 *  ptabasic.c: Basic 'class' functions for Pta and Ptaa, including
 *  accessors and serialization.
 *
 *  Differences from C:
 *    - No reference counting and no ptaDestroy() and ptaaDestroy(): the GC
 *      owns the objects (not ported).  ptaClone() and ptaaGetPta(L_CLONE)
 *      return the same reference.
 *    - FILE* becomes Stream.  Text is written as ASCII with "\n" line ends,
 *      formatted with CFormat so that it matches the C fprintf output byte
 *      for byte; reading uses the fscanf formats of the C code through
 *      CFormat.Sscanf.
 *    - The *Mem variants take a ReadOnlySpan&lt;byte&gt; and return a byte[]
 *      (without the terminating NUL that C appends to its buffer).
 *    - fopenReadStream() and friends are replaced by System.IO.
 *    - Functions that return an error code or NULL in C throw instead,
 *      except where the C code logs and carries on (noted at the place).
 */

/// <summary>Constants from pix_internal.h used by the serialization of points.</summary>
public static partial class L
{
    /// <summary>Version number of the serialized point array format.</summary>
    public const int PTA_VERSION_NUMBER = 1;
}

/// <summary>
/// Reads from a <see cref="Stream"/> with the semantics of C fscanf, for the
/// formats of the Pta and Ptaa serialization (whitespace, literals, %d, %f, %s).
/// </summary>
/// <remarks>
/// The rest of the stream is read once; each scan is a CFormat.Sscanf on a window
/// of the text.  After a successful scan, trailing white space of the format has
/// been consumed.  When done, <see cref="Release"/> repositions a seekable stream
/// right after the consumed text; a stream that cannot seek stays at its end.
/// </remarks>
internal sealed class PtaScanner
{
    private const int Window = 4096;

    private readonly Stream stream;
    private readonly long startPos;
    private readonly string text;
    private int pos;

    public PtaScanner(Stream stream)
    {
        this.stream = stream;
        startPos = stream.CanSeek ? stream.Position : 0;
        using MemoryStream ms = new MemoryStream();
        stream.CopyTo(ms);
        text = Encoding.Latin1.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private static bool IsSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r');

    private void SkipSpace()
    {
        while (pos < text.Length && IsSpace(text[pos]))
            pos++;
    }

    /// <summary>
    /// Like fscanf(fp, format, ...).  Returns the number of assigned items, or -1 (EOF)
    /// if the input ended before the first conversion.  On a failed scan the position
    /// is not advanced: all callers treat that as an error.
    /// </summary>
    public int Scan(string format, out object?[] values)
    {
        if (format.Length > 0 && IsSpace(format[0]))
            SkipSpace();
        int len = Math.Min(Window, text.Length - pos);
        string window = text.Substring(pos, len);
        int ret = CFormat.Sscanf(window, format + "%n", out object?[] v);
        values = v;
        if (v.Length > 0 && v[^1] is int consumed && ret >= 0) {
            pos += consumed;
            if (format.Length > 0 && IsSpace(format[^1]))
                SkipSpace();
        }
        return ret;
    }

    /// <summary>Repositions a seekable stream to the end of the consumed text.</summary>
    public void Release()
    {
        if (stream.CanSeek)
            stream.Seek(startPos + pos, SeekOrigin.Begin);
    }
}

/// <summary>
/// Array of points with float coordinates (struct Pta).
/// </summary>
public sealed partial class Pta
{
    /* Bounds on array sizes */
    private const uint MaxArraySize = 100000000;      /* 100 million */
    private const int InitialArraySize = 50;          /* n'importe quoi */

    private float[] x;  /* arrays of floats; their length is nalloc */
    private float[] y;
    private int n;      /* actual number of pts                     */

    private Pta(int nalloc)
    {
        x = new float[nalloc];
        y = new float[nalloc];
        n = 0;
    }

    /// <summary>Size of the allocated arrays (pta->nalloc).</summary>
    public int Nalloc => x.Length;

    /*---------------------------------------------------------------------*
     *                Pta creation, destruction, copy, clone               *
     *---------------------------------------------------------------------*/
    // leptonica: ptaCreate (ptabasic.c)
    /// <param name="n">initial array sizes</param>
    public static Pta Create(int n)
    {
        if (n <= 0 || n > (int)MaxArraySize)
            n = InitialArraySize;

        return new Pta(n);
    }

    // leptonica: ptaCreateFromNuma (ptabasic.c)
    /// <param name="nax">[optional] can be null</param>
    /// <param name="nay"></param>
    public static Pta CreateFromNuma(Numa? nax, Numa nay)
    {
        int n = nay.Count;
        if (nax != null && nax.Count != n)
            throw Lept.Arg("ptaCreateFromNuma", "nax and nay sizes differ");

        Pta pta = Create(n);
        nay.GetParameters(out float startx, out float delx);
        for (int i = 0; i < n; i++) {
            float xval;
            if (nax != null)
                xval = nax.GetFValue(i);
            else  /* use implicit x values from nay */
                xval = startx + i * delx;
            float yval = nay.GetFValue(i);
            pta.AddPt(xval, yval);
        }

        return pta;
    }

    // leptonica: ptaCopy (ptabasic.c)
    /// <summary>Returns a copy of the pta.</summary>
    public Pta Copy()
    {
        Pta npta = Create(Nalloc);

        for (int i = 0; i < n; i++) {
            GetPt(i, out float xv, out float yv);
            npta.AddPt(xv, yv);
        }

        return npta;
    }

    // leptonica: ptaCopyRange (ptabasic.c)
    /// <param name="istart">starting index in this pta</param>
    /// <param name="iend">ending index in this pta; use 0 to copy to end</param>
    /// <remarks>
    /// The points are copied through ptaGetIPt(), so the coordinates of the
    /// copy are rounded to integers.
    /// </remarks>
    public Pta CopyRange(int istart, int iend)
    {
        int n = Count;
        if (istart < 0)
            istart = 0;
        if (istart >= n)
            throw Lept.Arg("ptaCopyRange", "istart out of bounds");
        if (iend <= 0 || iend >= n)
            iend = n - 1;
        if (istart > iend)
            throw Lept.Arg("ptaCopyRange", "istart > iend; no pts");

        Pta ptad = Create(iend - istart + 1);
        for (int i = istart; i <= iend; i++) {
            GetIPt(i, out int xv, out int yv);
            ptad.AddPt(xv, yv);
        }

        return ptad;
    }

    // leptonica: ptaClone (ptabasic.c)
    /// <summary>Returns the same pta (a reference; changes are seen through both).</summary>
    public Pta Clone()
    {
        return this;
    }

    // leptonica: ptaEmpty (ptabasic.c)
    /// <remarks>This only resets the Pta::n field, for reuse</remarks>
    public void Empty()
    {
        n = 0;
    }

    /*---------------------------------------------------------------------*
     *                         Pta array extension                         *
     *---------------------------------------------------------------------*/
    // leptonica: ptaAddPt (ptabasic.c)
    /// <summary>Appends a point to the array, growing the storage if needed.</summary>
    /// <param name="x">The x coordinate of the point.</param>
    /// <param name="y">The y coordinate of the point.</param>
    public void AddPt(float x, float y)
    {
        int n = this.n;
        if (n >= Nalloc)
            ExtendArrays();

        this.x[n] = x;
        this.y[n] = y;
        this.n++;
    }

    // leptonica: ptaExtendArrays (ptabasic.c)
    /// <remarks>
    /// (1) Doubles the size of the array.
    /// (2) The max number of points is 100M.
    /// </remarks>
    private void ExtendArrays()
    {
        if (Nalloc > (int)MaxArraySize)  /* belt & suspenders */
            throw Lept.Fail("ptaExtendArrays", "pta at maximum size; can't extend");
        int newalloc;
        if ((uint)Nalloc > MaxArraySize / 2)
            newalloc = (int)MaxArraySize;
        else
            newalloc = Nalloc * 2;
        Array.Resize(ref x, newalloc);
        Array.Resize(ref y, newalloc);
    }

    /*---------------------------------------------------------------------*
     *                     Pta insertion and removal                       *
     *---------------------------------------------------------------------*/
    // leptonica: ptaInsertPt (ptabasic.c)
    /// <param name="index">at which pt is to be inserted</param>
    /// <param name="x">point value</param>
    /// <param name="y">point value</param>
    public void InsertPt(int index, int x, int y)
    {
        int n = Count;
        if (index < 0 || index > n)
            throw Lept.Arg("ptaInsertPt", $"index {index} not in [0,...,{n}]");

        /* NOTE(upstream): C tests n > nalloc, so for n == nalloc it writes one
         * element past the arrays.  Here n >= nalloc, which extends first. */
        if (n >= Nalloc)
            ExtendArrays();
        this.n++;
        for (int i = n; i > index; i--) {
            this.x[i] = this.x[i - 1];
            this.y[i] = this.y[i - 1];
        }
        this.x[index] = x;
        this.y[index] = y;
    }

    // leptonica: ptaRemovePt (ptabasic.c)
    /// <param name="index">of point to be removed</param>
    /// <remarks>
    /// (1) This shifts pta[i] --> pta[i - 1] for all i > index.
    /// (2) It should not be used repeatedly on large arrays,
    ///     because the function is O(n).
    /// </remarks>
    public void RemovePt(int index)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("ptaRemovePt", $"index {index} not in [0,...,{n - 1}]");

        /* Remove the point */
        for (int i = index + 1; i < n; i++) {
            x[i - 1] = x[i];
            y[i - 1] = y[i];
        }
        this.n--;
    }

    /*---------------------------------------------------------------------*
     *                           Pta accessors                             *
     *---------------------------------------------------------------------*/
    // leptonica: ptaGetCount (ptabasic.c)
    /// <summary>Number of points.</summary>
    public int Count => n;

    // leptonica: ptaGetPt (ptabasic.c)
    /// <param name="index">into arrays</param>
    /// <param name="px">float x value</param>
    /// <param name="py">float y value</param>
    public void GetPt(int index, out float px, out float py)
    {
        px = 0;
        py = 0;
        if (index < 0 || index >= n)
            throw Lept.Arg("ptaGetPt", "invalid index");

        px = x[index];
        py = y[index];
    }

    // leptonica: ptaGetIPt (ptabasic.c)
    /// <param name="index">into arrays</param>
    /// <param name="px">integer x value</param>
    /// <param name="py">integer y value</param>
    public void GetIPt(int index, out int px, out int py)
    {
        px = 0;
        py = 0;
        if (index < 0 || index >= n)
            throw Lept.Arg("ptaGetIPt", "invalid index");

        px = (int)(x[index] + 0.5);
        py = (int)(y[index] + 0.5);
    }

    // leptonica: ptaSetPt (ptabasic.c)
    /// <summary>Sets the coordinates of the point at the given index.</summary>
    /// <param name="index">Index into the point arrays.</param>
    /// <param name="x">The new x coordinate.</param>
    /// <param name="y">The new y coordinate.</param>
    /// <exception cref="ArgumentException"><paramref name="index"/> is out of range.</exception>
    public void SetPt(int index, float x, float y)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("ptaSetPt", "invalid index");

        this.x[index] = x;
        this.y[index] = y;
    }

    // leptonica: ptaGetArrays (ptabasic.c)
    /// <param name="nax">numa of x array</param>
    /// <param name="nay">numa of y array</param>
    /// <remarks>
    /// (1) This copies the internal arrays into new Numas.
    /// (2) Throws if the pta is empty.
    /// (3) Both outputs are always made; the C function makes only those
    ///     that are requested.
    /// </remarks>
    public void GetArrays(out Numa nax, out Numa nay)
    {
        int n = Count;
        if (n == 0)
            throw Lept.Arg("ptaGetArrays", "pta is empty");

        nax = Numa.Create(n);
        for (int i = 0; i < n; i++)
            nax.AddNumber(x[i]);
        nay = Numa.Create(n);
        for (int i = 0; i < n; i++)
            nay.AddNumber(y[i]);
    }

    /*---------------------------------------------------------------------*
     *                       Pta serialized for I/O                        *
     *---------------------------------------------------------------------*/
    // leptonica: ptaRead (ptabasic.c)
    /// <summary>Reads a serialized point array from a file.</summary>
    /// <param name="filename">Path of the file to read.</param>
    /// <returns>The deserialized <see cref="Pta"/>.</returns>
    public static Pta Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("ptaRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in ptaRead: pta not read: {filename}", e);
            }
        }
    }

    // leptonica: ptaReadStream (ptabasic.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// (1) It is OK for the pta to be empty (n == 0).
    /// (2) If the stream is not seekable, it is read to the end.
    /// </remarks>
    public static Pta ReadStream(Stream fp)
    {
        PtaScanner sc = new PtaScanner(fp);
        try {
            return ReadStream(sc);
        } finally {
            sc.Release();
        }
    }

    internal static Pta ReadStream(PtaScanner sc)
    {
        const string func = "ptaReadStream";

        if (sc.Scan("\n Pta Version %d\n", out object?[] v) != 1)
            throw Lept.Arg(func, "not a pta file");
        int version = (int)v[0]!;
        if (version != L.PTA_VERSION_NUMBER)
            throw Lept.Arg(func, "invalid pta version");
        if (sc.Scan(" Number of pts = %d; format = %127s\n", out v) != 2)
            throw Lept.Arg(func, "not a pta file");
        int n = (int)v[0]!;
        string typestr = (string)v[1]!;
        if (n < 0)
            throw Lept.Arg(func, "num pts <= 0");
        if (n > (int)MaxArraySize)
            throw Lept.Arg(func, "too many pts");
        if (n == 0) Log.Info(func, "the pta is empty");

        int type;
        if (typestr == "float")
            type = 0;
        else  /* typestr is "integer" */
            type = 1;
        Pta pta = Create(n);
        for (int i = 0; i < n; i++) {
            if (type == 0) {  /* data is float */
                if (sc.Scan("   (%f, %f)\n", out v) != 2)
                    throw Lept.Arg(func, "error reading floats");
                pta.AddPt((float)v[0]!, (float)v[1]!);
            } else {   /* data is integer */
                if (sc.Scan("   (%d, %d)\n", out v) != 2)
                    throw Lept.Arg(func, "error reading ints");
                pta.AddPt((int)v[0]!, (int)v[1]!);
            }
        }

        return pta;
    }

    // leptonica: ptaReadMem (ptabasic.c)
    /// <param name="data">serialization in ascii</param>
    public static Pta ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (ArgumentException) {
            Log.Error("ptaReadMem", "pta not read");
            throw;
        }
    }

    // leptonica: ptaWriteDebug (ptabasic.c)
    /// <param name="filename"></param>
    /// <param name="type">0 for float values; 1 for integer values</param>
    /// <remarks>
    /// (1) Debug version, intended for use in the library when writing
    ///     to files in a temp directory with names that are compiled in.
    ///     This is used instead of ptaWrite() for all such library calls.
    /// (2) The global variable LeptDebugOK defaults to false, and can be set
    ///     or cleared by the function setLeptDebugOK().
    /// </remarks>
    public void WriteDebug(string filename, int type)
    {
        if (WriteFile.LeptDebugOK) {
            Write(filename, type);
        } else {
            Log.Info("ptaWriteDebug", $"write to named temp file {filename} is disabled");
        }
    }

    // leptonica: ptaWrite (ptabasic.c)
    /// <param name="filename"></param>
    /// <param name="type">0 for float values; 1 for integer values</param>
    public void Write(string filename, int type)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, "wb");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("ptaWrite", $"stream not opened: {filename}");
        }
        using (fp) {
            WriteStream(fp, type);
        }
    }

    // leptonica: ptaWriteStream (ptabasic.c)
    /// <param name="fp">output stream</param>
    /// <param name="type">0 for float values; 1 for integer values</param>
    public void WriteStream(Stream fp, int type)
    {
        int n = Count;
        Fprintf(fp, CFormat.Sprintf("\n Pta Version %d\n", L.PTA_VERSION_NUMBER));
        if (type == 0)
            Fprintf(fp, CFormat.Sprintf(" Number of pts = %d; format = float\n", n));
        else  /* type == 1 */
            Fprintf(fp, CFormat.Sprintf(" Number of pts = %d; format = integer\n", n));
        for (int i = 0; i < n; i++) {
            if (type == 0) {  /* data is float */
                GetPt(i, out float xv, out float yv);
                Fprintf(fp, CFormat.Sprintf("   (%f, %f)\n", xv, yv));
            } else {   /* data is integer */
                GetIPt(i, out int ix, out int iy);
                Fprintf(fp, CFormat.Sprintf("   (%d, %d)\n", ix, iy));
            }
        }
    }

    // leptonica: ptaWriteMem (ptabasic.c)
    /// <summary>Serializes a pta in memory and returns the ascii bytes.</summary>
    /// <param name="type">0 for float values; 1 for integer values</param>
    public byte[] WriteMem(int type)
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp, type);
        return fp.ToArray();
    }

    /// <summary>fprintf of text that is plain ASCII.</summary>
    internal static void Fprintf(Stream fp, string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        fp.Write(bytes, 0, bytes.Length);
    }
}

/// <summary>
/// Array of Pta (struct Ptaa).
/// </summary>
public sealed partial class Ptaa
{
    /* Bounds on array sizes */
    private const uint MaxPtrArraySize = 10000000;    /* 10 million */
    private const int InitialArraySize = 50;          /* n'importe quoi */

    private Pta?[] pta;  /* pta ptr array; its length is nalloc */
    private int n;       /* number of pta in ptr array          */

    private Ptaa(int nalloc)
    {
        pta = new Pta?[nalloc];
        n = 0;
    }

    /// <summary>Number of allocated pta ptrs (ptaa->nalloc).</summary>
    public int Nalloc => pta.Length;

    /*---------------------------------------------------------------------*
     *                     PTAA creation, destruction                      *
     *---------------------------------------------------------------------*/
    // leptonica: ptaaCreate (ptabasic.c)
    /// <param name="n">initial number of ptrs</param>
    public static Ptaa Create(int n)
    {
        if (n <= 0 || n > (int)MaxPtrArraySize)
            n = InitialArraySize;

        return new Ptaa(n);
    }

    /*---------------------------------------------------------------------*
     *                          PTAA array extension                       *
     *---------------------------------------------------------------------*/
    // leptonica: ptaaAddPta (ptabasic.c)
    /// <param name="pta">to be added</param>
    /// <param name="copyflag">L_INSERT, L_COPY, L_CLONE</param>
    public void AddPta(Pta pta, int copyflag)
    {
        Pta ptac;
        if (copyflag == L.L_INSERT)
            ptac = pta;
        else if (copyflag == L.L_COPY)
            ptac = pta.Copy();
        else if (copyflag == L.L_CLONE)
            ptac = pta.Clone();
        else
            throw Lept.Arg("ptaaAddPta", "invalid copyflag");

        int n = Count;
        if (n >= Nalloc)
            ExtendArray();

        this.pta[n] = ptac;
        this.n++;
    }

    // leptonica: ptaaExtendArray (ptabasic.c)
    /// <remarks>
    /// (1) This doubles the pta ptr array size.
    /// (2) The max number of pta ptrs is 10M.
    /// </remarks>
    private void ExtendArray()
    {
        /* the ptr array is 8 bytes per ptr in the 64-bit C build */
        long oldsize = (long)Nalloc * 8;
        long newsize = 2 * oldsize;
        if (newsize > 8L * MaxPtrArraySize)
            throw Lept.Fail("ptaaExtendArray", "newsize > 80 MB; too large");

        Array.Resize(ref pta, Nalloc * 2);
    }

    /*---------------------------------------------------------------------*
     *                          Ptaa accessors                             *
     *---------------------------------------------------------------------*/
    // leptonica: ptaaGetCount (ptabasic.c)
    /// <summary>Number of pta; 0 if no pta.</summary>
    public int Count => n;

    // leptonica: ptaaGetPta (ptabasic.c)
    /// <param name="index">to the i-th pta</param>
    /// <param name="accessflag">L_COPY or L_CLONE</param>
    public Pta GetPta(int index, int accessflag)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("ptaaGetPta", "index not valid");

        if (accessflag == L.L_COPY)
            return (pta[index] ?? throw Lept.Arg("ptaCopy", "pta not defined")).Copy();
        else if (accessflag == L.L_CLONE)
            return (pta[index] ?? throw Lept.Arg("ptaClone", "pta not defined")).Clone();
        else
            throw Lept.Arg("ptaaGetPta", "invalid accessflag");
    }

    // leptonica: ptaaGetPt (ptabasic.c)
    /// <param name="ipta">to the i-th pta</param>
    /// <param name="jpt">index to the j-th pt in the pta</param>
    /// <param name="px">float x value</param>
    /// <param name="py">float y value</param>
    public void GetPt(int ipta, int jpt, out float px, out float py)
    {
        px = 0;
        py = 0;
        if (ipta < 0 || ipta >= n)
            throw Lept.Arg("ptaaGetPt", "index ipta not valid");

        Pta p = GetPta(ipta, L.L_CLONE);
        if (jpt < 0 || jpt >= p.Count)
            throw Lept.Arg("ptaaGetPt", "index jpt not valid");

        p.GetPt(jpt, out px, out py);
    }

    /*---------------------------------------------------------------------*
     *                        Ptaa array modifiers                         *
     *---------------------------------------------------------------------*/
    // leptonica: ptaaInitFull (ptabasic.c)
    /// <param name="pta">to be replicated into the entire ptr array</param>
    /// <remarks>This ptaa can have non-null ptrs in the ptr array.</remarks>
    public void InitFull(Pta pta)
    {
        int n = Nalloc;
        this.n = n;
        for (int i = 0; i < n; i++) {
            Pta ptat = pta.Copy();
            ReplacePta(i, ptat);
        }
    }

    // leptonica: ptaaReplacePta (ptabasic.c)
    /// <param name="index">to the index-th pta</param>
    /// <param name="pta">insert and replace any existing one</param>
    /// <remarks>
    /// (1) Any existing pta is dropped, and the input one
    ///     is inserted in its place.
    /// (2) If %index is invalid, this throws (error)
    /// </remarks>
    public void ReplacePta(int index, Pta pta)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("ptaaReplacePta", "index not valid");

        this.pta[index] = pta;
    }

    // leptonica: ptaaAddPt (ptabasic.c)
    /// <param name="ipta">to the i-th pta</param>
    /// <param name="x">point coordinate</param>
    /// <param name="y">point coordinate</param>
    public void AddPt(int ipta, float x, float y)
    {
        if (ipta < 0 || ipta >= n)
            throw Lept.Arg("ptaaAddPt", "index ipta not valid");

        Pta p = GetPta(ipta, L.L_CLONE);
        p.AddPt(x, y);
    }

    // leptonica: ptaaTruncate (ptabasic.c)
    /// <remarks>
    /// (1) This identifies the largest index containing a pta that
    ///     has any points within it, destroys all pta above that index,
    ///     and resets the count.
    /// </remarks>
    public void Truncate()
    {
        int n = Count;
        for (int i = n - 1; i >= 0; i--) {
            Pta p;
            try {
                p = GetPta(i, L.L_CLONE);
            } catch (ArgumentException) {
                /* C: ptaaGetPta() returns NULL after logging an error */
                Log.Error("ptaClone", "pta not defined");
                this.n--;
                continue;
            }
            int np = p.Count;
            if (np == 0) {
                pta[i] = null;
                this.n--;
            } else {
                break;
            }
        }
    }

    /*---------------------------------------------------------------------*
     *                       Ptaa serialized for I/O                       *
     *---------------------------------------------------------------------*/
    // leptonica: ptaaRead (ptabasic.c)
    /// <summary>Reads a serialized array of point arrays from a file.</summary>
    /// <param name="filename">Path of the file to read.</param>
    /// <returns>The deserialized <see cref="Ptaa"/>.</returns>
    public static Ptaa Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("ptaaRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in ptaaRead: ptaa not read: {filename}", e);
            }
        }
    }

    // leptonica: ptaaReadStream (ptabasic.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// (1) It is OK for the ptaa to be empty (n == 0).
    /// (2) If the stream is not seekable, it is read to the end.
    /// </remarks>
    public static Ptaa ReadStream(Stream fp)
    {
        PtaScanner sc = new PtaScanner(fp);
        try {
            return ReadStream(sc);
        } finally {
            sc.Release();
        }
    }

    private static Ptaa ReadStream(PtaScanner sc)
    {
        const string func = "ptaaReadStream";

        if (sc.Scan("\nPtaa Version %d\n", out object?[] v) != 1)
            throw Lept.Arg(func, "not a ptaa file");
        int version = (int)v[0]!;
        if (version != L.PTA_VERSION_NUMBER)
            throw Lept.Arg(func, "invalid ptaa version");
        if (sc.Scan("Number of Pta = %d\n", out v) != 1)
            throw Lept.Arg(func, "not a ptaa file");
        int n = (int)v[0]!;
        if (n < 0)
            throw Lept.Arg(func, "num pta ptrs <= 0");
        if (n > (int)MaxPtrArraySize)
            throw Lept.Arg(func, "too many pta ptrs");
        if (n == 0) Log.Info(func, "the ptaa is empty");

        Ptaa ptaa = Create(n);
        for (int i = 0; i < n; i++) {
            Pta pta;
            try {
                pta = Pta.ReadStream(sc);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in {func}: error reading pta", e);
            }
            ptaa.AddPta(pta, L.L_INSERT);
        }

        return ptaa;
    }

    // leptonica: ptaaReadMem (ptabasic.c)
    /// <param name="data">serialization in ascii</param>
    public static Ptaa ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (ArgumentException) {
            Log.Error("ptaaReadMem", "ptaa not read");
            throw;
        }
    }

    // leptonica: ptaaWriteDebug (ptabasic.c)
    /// <param name="filename"></param>
    /// <param name="type">0 for float values; 1 for integer values</param>
    /// <remarks>
    /// (1) Debug version, intended for use in the library when writing
    ///     to files in a temp directory with names that are compiled in.
    ///     This is used instead of ptaaWrite() for all such library calls.
    /// (2) The global variable LeptDebugOK defaults to false, and can be set
    ///     or cleared by the function setLeptDebugOK().
    /// </remarks>
    public void WriteDebug(string filename, int type)
    {
        if (WriteFile.LeptDebugOK) {
            Write(filename, type);
        } else {
            Log.Info("ptaaWriteDebug", $"write to named temp file {filename} is disabled");
        }
    }

    // leptonica: ptaaWrite (ptabasic.c)
    /// <param name="filename"></param>
    /// <param name="type">0 for float values; 1 for integer values</param>
    public void Write(string filename, int type)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, "wb");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("ptaaWrite", $"stream not opened: {filename}");
        }
        using (fp) {
            WriteStream(fp, type);
        }
    }

    // leptonica: ptaaWriteStream (ptabasic.c)
    /// <param name="fp">output stream</param>
    /// <param name="type">0 for float values; 1 for integer values</param>
    public void WriteStream(Stream fp, int type)
    {
        int n = Count;
        Pta.Fprintf(fp, CFormat.Sprintf("\nPtaa Version %d\n", L.PTA_VERSION_NUMBER));
        Pta.Fprintf(fp, CFormat.Sprintf("Number of Pta = %d\n", n));
        for (int i = 0; i < n; i++) {
            Pta p = GetPta(i, L.L_CLONE);
            p.WriteStream(fp, type);
        }
    }

    // leptonica: ptaaWriteMem (ptabasic.c)
    /// <summary>Serializes a ptaa in memory and returns the ascii bytes.</summary>
    /// <param name="type">0 for float values; 1 for integer values</param>
    public byte[] WriteMem(int type)
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp, type);
        return fp.ToArray();
    }
}
