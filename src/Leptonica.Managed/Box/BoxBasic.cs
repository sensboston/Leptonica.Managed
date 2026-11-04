using System;
using System.IO;

namespace Leptonica;

/*
 *  boxbasic.c: Basic 'class' functions for Box, Boxa and Boxaa,
 *  including accessors and serialization.
 *
 *  Most functions use only valid boxes, which are boxes that have both
 *  width and height > 0.  However, a few functions, such as
 *  boxaGetMedianVals() do not assume that all boxes are valid.  For any
 *  function that can use a boxa with invalid boxes, it is convenient
 *  to use these accessors:
 *      boxaGetValidCount()   :  count of valid boxes
 *      boxaGetValidBox()     :  returns null for invalid boxes
 *
 *  Differences from C:
 *    - No reference counting and no boxDestroy(), boxaDestroy() and
 *      boxaaDestroy(): the GC owns the objects (not ported).
 *      boxClone() and boxaCopy(L_CLONE) return the same reference.
 *    - FILE* becomes Stream.  Text is written as ASCII with invariant
 *      culture and "\n" line ends, byte for byte as the C fprintf calls
 *      write it.  Reading follows the fscanf formats of the C code.
 *    - boxaWriteStream() with a null stream writes to stderr, as in C.
 *    - The *Mem variants take a ReadOnlySpan&lt;byte&gt; and return a byte[]
 *      (without the terminating NUL that C appends to its buffer).
 *    - fopenReadStream() and friends are replaced by System.IO.
 *    - Functions that return an error code or NULL in C throw instead,
 *      except where the C code logs and carries on (noted at the place).
 */

/// <summary>Constants from pix_internal.h used by the serialization of boxes.</summary>
public static partial class L
{
    /// <summary>Version number used in the serialized <see cref="Boxa"/> format.</summary>
    public const int BOXA_VERSION_NUMBER = 2;

    /// <summary>Version number used in the serialized <see cref="Boxaa"/> format.</summary>
    public const int BOXAA_VERSION_NUMBER = 3;
}

/// <summary>
/// Rectangle with integer geometry (struct Box).
/// </summary>
public sealed partial class Box
{
    /// <summary>Left edge (box->x).</summary>
    public int X { get; set; }
    /// <summary>Top edge (box->y).</summary>
    public int Y { get; set; }
    /// <summary>Width (box->w).</summary>
    public int Width { get; set; }
    /// <summary>Height (box->h).</summary>
    public int Height { get; set; }

    private Box() { }

    /*---------------------------------------------------------------------*
     *                  Box creation, destruction and copy                 *
     *---------------------------------------------------------------------*/
    // leptonica: boxCreate (boxbasic.c)
    /// <summary>Creates a box, clipped to the +quad.</summary>
    /// <remarks>
    /// (1) This clips the box to the +quad.  If no part of the
    ///     box is in the +quad, this throws.
    /// (2) We allow you to make a box with w = 0 and/or h = 0.
    ///     This does not represent a valid region, but it is useful
    ///     as a placeholder in a boxa for which the index of the
    ///     box in the boxa is important.  This is an atypical
    ///     situation; usually you want to put only valid boxes with
    ///     nonzero width and height in a boxa.  If you have a boxa
    ///     with invalid boxes, the accessor boxaGetValidBox()
    ///     will return null on each invalid box.
    /// (3) If you want to create only valid boxes, use boxCreateValid(),
    ///     which throws if either w or h is 0.
    /// </remarks>
    public static Box Create(int x, int y, int w, int h)
    {
        if (w < 0 || h < 0)
            throw Lept.Arg("boxCreate", "w and h not both >= 0");
        if (x < 0) {  /* take part in +quad */
            w = w + x;
            x = 0;
            if (w <= 0)
                throw Lept.Arg("boxCreate", "x < 0 and box off +quad");
        }
        if (y < 0) {  /* take part in +quad */
            h = h + y;
            y = 0;
            if (h <= 0)
                throw Lept.Arg("boxCreate", "y < 0 and box off +quad");
        }

        Box box = new Box();
        box.SetGeometry(x, y, w, h);
        return box;
    }

    // leptonica: boxCreateValid (boxbasic.c)
    /// <summary>Creates a box like <see cref="Create"/>, but only with w &gt; 0 and h &gt; 0.</summary>
    /// <remarks>(1) This throws if either w = 0 or h = 0.</remarks>
    public static Box CreateValid(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0)
            throw Lept.Arg("boxCreateValid", "w and h not both > 0");
        return Create(x, y, w, h);
    }

    // leptonica: boxCopy (boxbasic.c)
    /// <summary>Returns a copy of the box.</summary>
    public Box Copy()
    {
        Box boxc = Create(X, Y, Width, Height);
        return boxc;
    }

    // leptonica: boxClone (boxbasic.c)
    /// <summary>Returns the same box (a reference; changes are seen through both).</summary>
    public Box Clone()
    {
        return this;
    }

    /*---------------------------------------------------------------------*
     *                              Box accessors                          *
     *---------------------------------------------------------------------*/
    /// <summary>Gets the location and size of the box.</summary>
    /// <param name="x">Receives the left edge.</param>
    /// <param name="y">Receives the top edge.</param>
    /// <param name="w">Receives the width.</param>
    /// <param name="h">Receives the height.</param>
    // leptonica: boxGetGeometry (boxbasic.c)
    public void GetGeometry(out int x, out int y, out int w, out int h)
    {
        x = X;
        y = Y;
        w = Width;
        h = Height;
    }

    // leptonica: boxSetGeometry (boxbasic.c)
    /// <summary>Sets the geometry of the box.</summary>
    /// <param name="x">use -1 to leave unchanged</param>
    /// <param name="y">use -1 to leave unchanged</param>
    /// <param name="w">use -1 to leave unchanged</param>
    /// <param name="h">use -1 to leave unchanged</param>
    public void SetGeometry(int x, int y, int w, int h)
    {
        if (x != -1) X = x;
        if (y != -1) Y = y;
        if (w != -1) Width = w;
        if (h != -1) Height = h;
    }

    // leptonica: boxGetSideLocations (boxbasic.c)
    /// <summary>Gets the left, right, top and bottom side locations.</summary>
    /// <remarks>(1) All returned values are within the box.</remarks>
    public void GetSideLocations(out int l, out int r, out int t, out int b)
    {
        GetGeometry(out int x, out int y, out int w, out int h);
        l = x;
        r = x + w - 1;
        t = y;
        b = y + h - 1;
    }

    // leptonica: boxSetSideLocations (boxbasic.c)
    /// <param name="l">use -1 to leave unchanged</param>
    /// <param name="r">use -1 to leave unchanged</param>
    /// <param name="t">use -1 to leave unchanged</param>
    /// <param name="b">use -1 to leave unchanged</param>
    public void SetSideLocations(int l, int r, int t, int b)
    {
        int x = (l != -1) ? l : X;
        int w = (r != -1) ? r - x + 1 : X + Width - x;
        int y = (t != -1) ? t : Y;
        int h = (b != -1) ? b - y + 1 : Y + Height - y;
        SetGeometry(x, y, w, h);
    }

    // leptonica: boxIsValid (boxbasic.c)
    /// <summary>True if both width and height are &gt; 0.</summary>
    public bool IsValid()
    {
        return Width > 0 && Height > 0;
    }
}

/// <summary>
/// Array of boxes (struct Boxa).
/// </summary>
public sealed partial class Boxa
{
    /* Bounds on array sizes */
    private const int MaxBoxaPtrArraySize = 10000000;
    private const int InitialPtrArraySize = 20;      /* n'importe quoi */

    private Box?[] boxes;  /* box ptr array; its length is nalloc    */
    private int n;         /* number of box in ptr array             */

    private Boxa(int nalloc)
    {
        boxes = new Box?[nalloc];
        n = 0;
    }

    /// <summary>Number of allocated box ptrs (boxa->nalloc).</summary>
    public int Nalloc => boxes.Length;

    /*---------------------------------------------------------------------*
     *             Boxa creation, destruction, copy, extension             *
     *---------------------------------------------------------------------*/
    // leptonica: boxaCreate (boxbasic.c)
    /// <param name="n">initial number of ptrs; 0 for default</param>
    public static Boxa Create(int n)
    {
        if (n <= 0 || n > MaxBoxaPtrArraySize)
            n = InitialPtrArraySize;

        return new Boxa(n);
    }

    // leptonica: boxaCopy (boxbasic.c)
    /// <summary>Copies or clones the boxa.</summary>
    /// <param name="copyflag">L_COPY, L_CLONE, L_COPY_CLONE</param>
    /// <remarks>
    /// (1) See pix.h for description of the copyflag.
    /// (2) The copy-clone makes a new boxa that holds clones of each box.
    /// </remarks>
    public Boxa Copy(int copyflag)
    {
        if (copyflag == L.L_CLONE)
            return this;

        if (copyflag != L.L_COPY && copyflag != L.L_COPY_CLONE)
            throw Lept.Arg("boxaCopy", "invalid copyflag");

        Boxa boxac = Create(Nalloc);
        for (int i = 0; i < n; i++) {
            Box boxc;
            if (copyflag == L.L_COPY)
                boxc = GetBox(i, L.L_COPY);
            else   /* copy-clone */
                boxc = GetBox(i, L.L_CLONE);
            boxac.AddBox(boxc, L.L_INSERT);
        }
        return boxac;
    }

    // leptonica: boxaAddBox (boxbasic.c)
    /// <param name="box">to be added</param>
    /// <param name="copyflag">L_INSERT, L_COPY, L_CLONE</param>
    public void AddBox(Box box, int copyflag)
    {
        Box boxc;
        if (copyflag == L.L_INSERT)
            boxc = box;
        else if (copyflag == L.L_COPY)
            boxc = box.Copy();
        else if (copyflag == L.L_CLONE)
            boxc = box.Clone();
        else
            throw Lept.Arg("boxaAddBox", "invalid copyflag");

        int n = Count;
        if (n >= Nalloc)
            ExtendArray();
        boxes[n] = boxc;
        this.n++;
    }

    // leptonica: boxaExtendArray (boxbasic.c)
    /// <remarks>(1) Reallocs with doubled size of ptr array.</remarks>
    public void ExtendArray()
    {
        ExtendArrayToSize(2L * Nalloc);
    }

    // leptonica: boxaExtendArrayToSize (boxbasic.c)
    /// <param name="size">new size of boxa ptr array</param>
    /// <remarks>
    /// (1) If necessary, reallocs new boxa ptr array to %size.
    /// (2) The max number of box ptrs is 10M.
    /// </remarks>
    public void ExtendArrayToSize(long size)
    {
        if (Nalloc > MaxBoxaPtrArraySize)  /* belt & suspenders */
            throw Lept.Fail("boxaExtendArrayToSize", "boxa has too many ptrs");
        /* size is a size_t in C, so a negative value is huge */
        if (size < 0 || size > MaxBoxaPtrArraySize)
            throw Lept.Arg("boxaExtendArrayToSize", "size > 10M box ptrs; too large");
        if (size <= Nalloc) {
            Log.Info("boxaExtendArrayToSize", "size too small; no extension");
            return;
        }

        Array.Resize(ref boxes, (int)size);
    }

    /*---------------------------------------------------------------------*
     *                             Boxa accessors                          *
     *---------------------------------------------------------------------*/
    // leptonica: boxaGetCount (boxbasic.c)
    /// <summary>Count of all boxes; 0 if no boxes.</summary>
    public int Count => n;

    // leptonica: boxaGetValidCount (boxbasic.c)
    /// <summary>Count of valid boxes; 0 if no valid boxes.</summary>
    public int GetValidCount()
    {
        int n = Count;
        int count = 0;
        for (int i = 0; i < n; i++) {
            GetBoxGeometry(i, out _, out _, out int w, out int h);
            if (w > 0 && h > 0)
                count++;
        }
        return count;
    }

    // leptonica: boxaGetBox (boxbasic.c)
    /// <param name="index">to the index-th box</param>
    /// <param name="accessflag">L_COPY or L_CLONE</param>
    public Box GetBox(int index, int accessflag)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("boxaGetBox", "index not valid");

        if (accessflag == L.L_COPY)
            return (boxes[index] ?? throw Lept.Arg("boxCopy", "box not defined")).Copy();
        else if (accessflag == L.L_CLONE)
            return (boxes[index] ?? throw Lept.Arg("boxClone", "box not defined")).Clone();
        else
            throw Lept.Arg("boxaGetBox", "invalid accessflag");
    }

    /// <summary>The box ptr at index, which can be null (boxa->box[index]); the index is not checked.</summary>
    internal Box? PeekBox(int index) => boxes[index];

    // leptonica: boxaGetValidBox (boxbasic.c)
    /// <summary>Returns the box, or null if it is not valid.</summary>
    /// <param name="index">to the index-th box</param>
    /// <param name="accessflag">L_COPY or L_CLONE</param>
    /// <remarks>
    /// (1) This returns null for an invalid box in a boxa.
    ///     For a box to be valid, both the width and height must be > 0.
    /// (2) We allow invalid boxes, with w = 0 or h = 0, as placeholders
    ///     in boxa for which the index of the box in the boxa is important.
    ///     This is an atypical situation; usually you want to put only
    ///     valid boxes in a boxa.
    /// </remarks>
    public Box? GetValidBox(int index, int accessflag)
    {
        Box box = GetBox(index, accessflag);
        box.GetGeometry(out _, out _, out int w, out int h);
        if (w <= 0 || h <= 0)  /* not valid, but not necessarily an error */
            return null;
        return box;
    }

    // leptonica: boxaFindInvalidBoxes (boxbasic.c)
    /// <summary>Numa of invalid boxes; null if there are none.</summary>
    public Numa? FindInvalidBoxes()
    {
        int n = Count;
        if (GetValidCount() == n)
            return null;

        Numa na = Numa.MakeConstant(0, n);
        for (int i = 0; i < n; i++) {
            GetBoxGeometry(i, out _, out _, out int w, out int h);
            if (w == 0 || h == 0)
                na.SetValue(i, 1);
        }
        return na;
    }

    // leptonica: boxaGetBoxGeometry (boxbasic.c)
    /// <summary>Gets the location and size of the box at the given index.</summary>
    /// <param name="index">The index of the box.</param>
    /// <param name="x">Receives the left edge.</param>
    /// <param name="y">Receives the top edge.</param>
    /// <param name="w">Receives the width.</param>
    /// <param name="h">Receives the height.</param>
    /// <exception cref="ArgumentException"><paramref name="index"/> is not a valid box index.</exception>
    public void GetBoxGeometry(int index, out int x, out int y, out int w, out int h)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("boxaGetBoxGeometry", "index not valid");

        Box box = GetBox(index, L.L_CLONE);
        box.GetGeometry(out x, out y, out w, out h);
    }

    // leptonica: boxaIsFull (boxbasic.c)
    /// <summary>True if no box ptr within the count is null.</summary>
    public bool IsFull()
    {
        int n = Count;
        bool full = true;
        for (int i = 0; i < n; i++) {
            if (boxes[i] == null) {
                Log.Error("boxClone", "box not defined");  /* as boxaGetBox() fails in C */
                full = false;
                break;
            }
        }
        return full;
    }

    /*---------------------------------------------------------------------*
     *                        Boxa array modifiers                         *
     *---------------------------------------------------------------------*/
    // leptonica: boxaReplaceBox (boxbasic.c)
    /// <param name="index">to the index-th box</param>
    /// <param name="box">insert this box to replace existing one</param>
    /// <remarks>
    /// (1) In-place replacement of one box; the input %box is now
    ///     owned by the boxa.
    /// (2) The previous box at that location, if any, is dropped.
    /// </remarks>
    public void ReplaceBox(int index, Box box)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("boxaReplaceBox", "index not valid");

        boxes[index] = box;
    }

    // leptonica: boxaInsertBox (boxbasic.c)
    /// <param name="index">location in boxa to insert new value</param>
    /// <param name="box">new box to be inserted; the boxa now owns it</param>
    /// <remarks>
    /// (1) This shifts box[i] --> box[i + 1] for all i >= index,
    ///     and then inserts box as box[index].
    /// (2) To insert at the beginning of the array, set index = 0.
    /// (3) To append to the array, it's easier to use boxaAddBox().
    /// (4) This should not be used repeatedly to insert into large arrays,
    ///     because the function is O(n).
    /// </remarks>
    public void InsertBox(int index, Box box)
    {
        int n = Count;
        if (index < 0 || index > n)
            throw Lept.Arg("boxaInsertBox", $"index {index} not in [0,...,{n}]");

        if (n >= Nalloc)
            ExtendArray();
        Box?[] array = boxes;
        this.n++;
        for (int i = n; i > index; i--)
            array[i] = array[i - 1];
        array[index] = box;
    }

    // leptonica: boxaRemoveBox (boxbasic.c)
    /// <param name="index">of box to be removed</param>
    /// <remarks>
    /// (1) This removes box[index] and then shifts
    ///     box[i] --> box[i - 1] for all i > index.
    /// (2) It should not be used repeatedly to remove boxes from
    ///     large arrays, because the function is O(n).
    /// </remarks>
    public void RemoveBox(int index)
    {
        RemoveBoxAndSave(index, out _);
    }

    // leptonica: boxaRemoveBoxAndSave (boxbasic.c)
    /// <param name="index">of box to be removed</param>
    /// <param name="pbox">the removed box (a clone)</param>
    /// <remarks>
    /// (1) This removes box[index] and then shifts
    ///     box[i] --> box[i - 1] for all i > index.
    /// (2) It should not be used repeatedly to remove boxes from
    ///     large arrays, because the function is O(n).
    /// </remarks>
    public void RemoveBoxAndSave(int index, out Box? pbox)
    {
        pbox = null;
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("boxaRemoveBoxAndSave", $"index {index} not in [0,...,{n - 1}]");

        pbox = GetBox(index, L.L_CLONE);
        Box?[] array = boxes;
        for (int i = index + 1; i < n; i++)
            array[i - 1] = array[i];
        array[n - 1] = null;
        this.n--;
    }

    // leptonica: boxaSaveValid (boxbasic.c)
    /// <param name="copyflag">L_COPY or L_CLONE</param>
    /// <remarks>(1) This makes a copy/clone of each valid box.</remarks>
    public Boxa SaveValid(int copyflag)
    {
        if (copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg("boxaSaveValid", "invalid copyflag");

        int n = Count;
        Boxa boxad = Create(n);
        for (int i = 0; i < n; i++) {
            Box? box = GetValidBox(i, copyflag);
            if (box != null)
                boxad.AddBox(box, L.L_INSERT);
        }

        return boxad;
    }

    // leptonica: boxaInitFull (boxbasic.c)
    /// <param name="box">[optional] to be replicated into the entire ptr array</param>
    /// <remarks>
    /// (1) This initializes a boxa by filling up the entire box ptr array
    ///     with copies of %box.  If %box == null, use a placeholder box
    ///     of zero size.  Any existing boxes are destroyed.
    ///     After this opepration, the number of boxes is equal to
    ///     the number of allocated ptrs.
    /// (2) Note that we use boxaReplaceBox() instead of boxaInsertBox().
    ///     They both have the same effect when inserting into a NULL ptr
    ///     in the boxa ptr array:
    /// (3) Example usage.  This function is useful to prepare for a
    ///     random insertion (or replacement) of boxes into a boxa.
    ///     To randomly insert boxes into a boxa, up to some index "max":
    ///        Boxa boxa = Boxa.Create(max);
    ///        boxa.InitFull(null);
    ///     If you want placeholder boxes of non-zero size:
    ///        Boxa boxa = Boxa.Create(max);
    ///        Box box = Box.Create(...);
    ///        boxa.InitFull(box);
    ///     If we have an existing boxa with a smaller ptr array, it can
    ///     be reused for up to max boxes:
    ///        boxa.ExtendArrayToSize(max);
    ///        boxa.InitFull(null);
    ///     The initialization allows the boxa to always be properly
    ///     filled, even if all the boxes are not later replaced.
    ///     If you want to know which boxes have been replaced,
    ///     and you initialized with invalid zero-sized boxes,
    ///     use boxaGetValidBox() to return null for the invalid boxes.
    /// </remarks>
    public void InitFull(Box? box)
    {
        int n = Nalloc;
        this.n = n;
        for (int i = 0; i < n; i++) {
            Box boxt;
            if (box != null)
                boxt = box.Copy();
            else
                boxt = Box.Create(0, 0, 0, 0);
            ReplaceBox(i, boxt);
        }
    }

    // leptonica: boxaClear (boxbasic.c)
    /// <remarks>
    /// (1) This destroys all boxes in the boxa, setting the ptrs
    ///     to null.  The number of allocated boxes, n, is set to 0.
    /// </remarks>
    public void Clear()
    {
        int n = Count;
        for (int i = 0; i < n; i++)
            boxes[i] = null;
        this.n = 0;
    }

    /*---------------------------------------------------------------------*
     *                         Boxa serialized I/O                         *
     *---------------------------------------------------------------------*/
    /// <summary>Reads a serialized <see cref="Boxa"/> from a file.</summary>
    /// <param name="filename">The path of the file to read.</param>
    /// <returns>The boxes read from the file.</returns>
    /// <exception cref="ArgumentException">The file cannot be opened or its contents are not a valid serialized <see cref="Boxa"/>.</exception>
    // leptonica: boxaRead (boxbasic.c)
    public static Boxa Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("boxaRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in boxaRead: boxa not read: {filename}", e);
            }
        }
    }

    // leptonica: boxaReadStream (boxbasic.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// (1) It is OK for the boxa to be empty (n == 0).
    /// (2) If the stream is not seekable, the reader may consume one character
    ///     beyond the serialized data.
    /// </remarks>
    public static Boxa ReadStream(Stream fp)
    {
        CScanner sc = new CScanner(fp);
        try {
            return ReadStream(sc);
        } finally {
            sc.Release();
        }
    }

    internal static Boxa ReadStream(CScanner sc)
    {
        const string func = "boxaReadStream";
        Span<int> v = stackalloc int[5];

        if (sc.Scan("\nBoxa Version %d\n", v) != 1)
            throw Lept.Arg(func, "not a boxa file");
        int version = v[0];
        if (version != L.BOXA_VERSION_NUMBER)
            throw Lept.Arg(func, "invalid boxa version");
        if (sc.Scan("Number of boxes = %d\n", v) != 1)
            throw Lept.Arg(func, "not a boxa file");
        int n = v[0];
        if (n < 0)
            throw Lept.Arg(func, "num box ptrs < 0");
        if (n > MaxBoxaPtrArraySize)
            throw Lept.Arg(func, "too many box ptrs");
        if (n == 0) Log.Info(func, "the boxa is empty");

        Boxa boxa = Create(n);
        for (int i = 0; i < n; i++) {
            if (sc.Scan("  Box[%d]: x = %d, y = %d, w = %d, h = %d\n", v) != 5)
                throw Lept.Arg(func, "box descr not valid");
            int x = v[1], y = v[2], w = v[3], h = v[4];
            Box box;
            try {
                box = Box.Create(x, y, w, h);
            } catch (ArgumentException) {
                /* C: boxCreate() returns NULL and boxaAddBox() fails; reading goes on */
                Log.Error("boxaAddBox", "box not defined");
                continue;
            }
            boxa.AddBox(box, L.L_INSERT);
        }
        return boxa;
    }

    // leptonica: boxaReadMem (boxbasic.c)
    /// <param name="data">serialization of boxa; in ascii</param>
    public static Boxa ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (ArgumentException) {
            Log.Error("boxaReadMem", "boxa not read");
            throw;
        }
    }

    // leptonica: boxaWriteDebug (boxbasic.c)
    /// <remarks>
    /// (1) Debug version, intended for use in the library when writing
    ///     to files in a temp directory with names that are compiled in.
    ///     This is used instead of boxaWrite() for all such library calls.
    /// (2) The global variable LeptDebugOK defaults to false, and can be set
    ///     or cleared by the function setLeptDebugOK().
    /// </remarks>
    public void WriteDebug(string filename)
    {
        if (WriteFile.LeptDebugOK) {
            Write(filename);
        } else {
            Log.Info("boxaWriteDebug", $"write to named temp file {filename} is disabled");
        }
    }

    /// <summary>Writes the boxes to a file in the serialized <see cref="Boxa"/> format.</summary>
    /// <param name="filename">The path of the file to write.</param>
    /// <exception cref="ArgumentException">The file cannot be opened for writing.</exception>
    // leptonica: boxaWrite (boxbasic.c)
    public void Write(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, "wb");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("boxaWrite", $"stream not opened: {filename}");
        }
        using (fp) {
            WriteStream(fp);
        }
    }

    // leptonica: boxaWriteStream (boxbasic.c)
    /// <param name="fp">stream; use null for stderr</param>
    public void WriteStream(Stream? fp)
    {
        if (fp == null) {
            WriteStderr();
            return;
        }

        int n = Count;
        CStdio.Fprintf(fp, $"\nBoxa Version {L.BOXA_VERSION_NUMBER}\n");
        CStdio.Fprintf(fp, $"Number of boxes = {n}\n");
        for (int i = 0; i < n; i++) {
            Box box = GetBox(i, L.L_CLONE);
            CStdio.Fprintf(fp, $"  Box[{i}]: x = {box.X}, y = {box.Y}, w = {box.Width}, h = {box.Height}\n");
        }
    }

    /// <summary>Writes the boxes in the serialized <see cref="Boxa"/> format to the standard error stream.</summary>
    // leptonica: boxaWriteStderr (boxbasic.c)
    public void WriteStderr()
    {
        int n = Count;
        CStdio.Stderr($"\nBoxa Version {L.BOXA_VERSION_NUMBER}\n");
        CStdio.Stderr($"Number of boxes = {n}\n");
        for (int i = 0; i < n; i++) {
            Box box = GetBox(i, L.L_CLONE);
            CStdio.Stderr($"  Box[{i}]: x = {box.X}, y = {box.Y}, w = {box.Width}, h = {box.Height}\n");
        }
    }

    // leptonica: boxaWriteMem (boxbasic.c)
    /// <summary>Serializes a boxa in memory and returns the ascii bytes.</summary>
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }
}

/// <summary>
/// Array of Boxa (struct Boxaa).
/// </summary>
public sealed partial class Boxaa
{
    /* Bounds on array sizes */
    private const int MaxBoxaaPtrArraySize = 1000000;
    private const int InitialPtrArraySize = 20;      /* n'importe quoi */

    private Boxa?[] boxas;  /* boxa ptr array; its length is nalloc   */
    private int n;          /* number of boxa in ptr array            */

    private Boxaa(int nalloc)
    {
        boxas = new Boxa?[nalloc];
        n = 0;
    }

    /// <summary>Number of allocated boxa ptrs (baa->nalloc).</summary>
    public int Nalloc => boxas.Length;

    /*--------------------------------------------------------------------------*
     *                     Boxaa creation, destruction                          *
     *--------------------------------------------------------------------------*/
    // leptonica: boxaaCreate (boxbasic.c)
    /// <param name="n">size of boxa ptr array to be alloc'd; 0 for default</param>
    public static Boxaa Create(int n)
    {
        if (n <= 0 || n > MaxBoxaaPtrArraySize)
            n = InitialPtrArraySize;

        return new Boxaa(n);
    }

    // leptonica: boxaaCopy (boxbasic.c)
    /// <param name="copyflag">L_COPY, L_CLONE</param>
    /// <returns>new boxaa, composed of copies or clones of the boxa in this one</returns>
    /// <remarks>
    /// (1) L_COPY makes a copy of each boxa in baas.
    ///     L_CLONE makes a clone of each boxa in baas.
    /// </remarks>
    public Boxaa Copy(int copyflag)
    {
        if (copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg("boxaaCopy", "invalid copyflag");

        int n = Count;
        Boxaa baad = Create(n);
        for (int i = 0; i < n; i++) {
            Boxa boxa = GetBoxa(i, copyflag);
            baad.AddBoxa(boxa, L.L_INSERT);
        }

        return baad;
    }

    /*--------------------------------------------------------------------------*
     *                              Add Boxa to Boxaa                           *
     *--------------------------------------------------------------------------*/
    // leptonica: boxaaAddBoxa (boxbasic.c)
    /// <param name="ba">to be added</param>
    /// <param name="copyflag">L_INSERT, L_COPY, L_CLONE</param>
    public void AddBoxa(Boxa ba, int copyflag)
    {
        if (copyflag != L.L_INSERT && copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg("boxaaAddBoxa", "invalid copyflag");

        Boxa bac;
        if (copyflag == L.L_INSERT)
            bac = ba;
        else
            bac = ba.Copy(copyflag);

        int n = Count;
        if (n >= Nalloc)
            ExtendArray();
        boxas[n] = bac;
        this.n++;
    }

    // leptonica: boxaaExtendArray (boxbasic.c)
    /// <remarks>
    /// (1) Doubles the size of the boxa ptr array.
    /// (2) The max number of boxa ptrs is 1 million.
    /// </remarks>
    public void ExtendArray()
    {
        ExtendArrayToSize(2 * Nalloc);
    }

    // leptonica: boxaaExtendArrayToSize (boxbasic.c)
    /// <param name="size">new size of boxa array</param>
    /// <remarks>
    /// (1) If necessary, reallocs the boxa ptr array to %size.
    /// (2) %size limited to 1M boxa ptrs.
    /// </remarks>
    public void ExtendArrayToSize(int size)
    {
        if (Nalloc > MaxBoxaaPtrArraySize)  /* belt & suspenders */
            throw Lept.Fail("boxaaExtendArrayToSize", "baa has too many ptrs");
        /* the comparison is with a size_t in C, so a negative size is huge */
        if (size < 0 || size > MaxBoxaaPtrArraySize)
            throw Lept.Arg("boxaaExtendArrayToSize", "size > 1M boxa ptrs; too large");
        if (size <= Nalloc) {
            Log.Info("boxaaExtendArrayToSize", "size too small; no extension");
            return;
        }

        Array.Resize(ref boxas, size);
    }

    /*----------------------------------------------------------------------*
     *                           Boxaa accessors                            *
     *----------------------------------------------------------------------*/
    // leptonica: boxaaGetCount (boxbasic.c)
    /// <summary>Number of boxa; 0 if no boxa.</summary>
    public int Count => n;

    // leptonica: boxaaGetBoxCount (boxbasic.c)
    /// <summary>Number of boxes in all the boxa; 0 if no boxes.</summary>
    public int GetBoxCount()
    {
        int n = Count;
        int sum = 0;
        for (int i = 0; i < n; i++) {
            Boxa boxa = GetBoxa(i, L.L_CLONE);
            sum += boxa.Count;
        }

        return sum;
    }

    // leptonica: boxaaGetBoxa (boxbasic.c)
    /// <param name="index">to the index-th boxa</param>
    /// <param name="accessflag">L_COPY or L_CLONE</param>
    public Boxa GetBoxa(int index, int accessflag)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("boxaaGetBoxa", "index not valid");
        if (accessflag != L.L_COPY && accessflag != L.L_CLONE)
            throw Lept.Arg("boxaaGetBoxa", "invalid accessflag");

        return (boxas[index] ?? throw Lept.Arg("boxaCopy", "boxa not defined")).Copy(accessflag);
    }

    // leptonica: boxaaGetBox (boxbasic.c)
    /// <summary>Returns the box, or null (after logging an error) if it cannot be retrieved.</summary>
    /// <param name="iboxa">index into the boxa array in the boxaa</param>
    /// <param name="ibox">index into the box array in the boxa</param>
    /// <param name="accessflag">L_COPY or L_CLONE</param>
    public Box? GetBox(int iboxa, int ibox, int accessflag)
    {
        Boxa boxa = GetBoxa(iboxa, L.L_CLONE);
        Box? box;
        try {
            box = boxa.GetBox(ibox, accessflag);
        } catch (ArgumentException) {
            /* C: boxaGetBox() returns NULL; this only logs and returns NULL */
            Log.Error("boxaaGetBox", "box not retrieved");
            box = null;
        }
        return box;
    }

    /*----------------------------------------------------------------------*
     *                           Boxaa array modifiers                      *
     *----------------------------------------------------------------------*/
    // leptonica: boxaaInitFull (boxbasic.c)
    /// <param name="boxa">to be replicated into the entire ptr array</param>
    /// <remarks>
    /// (1) This initializes a boxaa by filling up the entire boxa ptr array
    ///     with copies of %boxa.  Any existing boxa are destroyed.
    ///     After this operation, the number of boxa is equal to
    ///     the number of allocated ptrs.
    /// (2) Note that we use boxaaReplaceBoxa() which replaces a boxa,
    ///     instead of boxaaInsertBoxa(), which is O(n) and shifts all
    ///     the boxa pointers from the insertion point to the end.
    /// (3) Example usage.  This function is useful to prepare for a
    ///     random insertion (or replacement) of boxa into a boxaa.
    ///     To randomly insert boxa into a boxaa, up to some index "max":
    ///        Boxaa baa = Boxaa.Create(max);
    ///          // initialize the boxa
    ///        Boxa boxa = Boxa.Create(...);
    ///        ...  [optionally fill with boxes]
    ///        baa.InitFull(boxa);
    ///     A typical use is to initialize the array with empty boxa,
    ///     and to replace only a subset that must be aligned with
    ///     something else, such as a pixa.
    /// </remarks>
    public void InitFull(Boxa boxa)
    {
        int n = Nalloc;
        this.n = n;
        for (int i = 0; i < n; i++) {
            Boxa boxat = boxa.Copy(L.L_COPY);
            ReplaceBoxa(i, boxat);
        }
    }

    // leptonica: boxaaExtendWithInit (boxbasic.c)
    /// <param name="maxindex"></param>
    /// <param name="boxa">to be replicated into the extended ptr array</param>
    /// <remarks>
    /// (1) This should be used on an existing boxaa that has been
    ///     fully loaded with boxa.  It then extends the boxaa,
    ///     loading all the additional ptrs with copies of boxa.
    ///     Typically, boxa will be empty.
    /// </remarks>
    public void ExtendWithInit(int maxindex, Boxa boxa)
    {
        /* Extend the ptr array if necessary */
        int n = Count;
        if (maxindex < n) return;
        ExtendArrayToSize(maxindex + 1);

        /* Fill the new entries with copies of boxa */
        for (int i = n; i <= maxindex; i++)
            AddBoxa(boxa, L.L_COPY);
    }

    // leptonica: boxaaReplaceBoxa (boxbasic.c)
    /// <param name="index">to the index-th boxa</param>
    /// <param name="boxa">insert and replace any existing one</param>
    /// <remarks>
    /// (1) Any existing boxa is dropped, and the input one
    ///     is inserted in its place.
    /// (2) If the index is invalid, this throws (error)
    /// </remarks>
    public void ReplaceBoxa(int index, Boxa boxa)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("boxaaReplaceBoxa", "index not valid");

        boxas[index] = boxa;
    }

    // leptonica: boxaaInsertBoxa (boxbasic.c)
    /// <param name="index">location in boxaa to insert new boxa</param>
    /// <param name="boxa">new boxa to be inserted</param>
    /// <remarks>
    /// (1) This shifts boxa[i] --> boxa[i + 1] for all i >= index,
    ///     and then inserts boxa as boxa[index].  It is typically used
    ///     when %baa is full of boxa.
    /// (2) To insert at the beginning of the array, set %index = 0.
    /// (3) To append to the array, it is equivalent to boxaaAddBoxa().
    /// (4) This should not be used repeatedly to insert into large arrays,
    ///     because the function is O(n).
    /// </remarks>
    public void InsertBoxa(int index, Boxa boxa)
    {
        int n = Count;
        if (index < 0 || index > n)
            throw Lept.Arg("boxaaInsertBoxa", $"index {index} not in [0,...,{n}]");

        if (n >= Nalloc)
            ExtendArray();
        Boxa?[] array = boxas;
        this.n++;
        for (int i = n; i > index; i--)
            array[i] = array[i - 1];
        array[index] = boxa;
    }

    // leptonica: boxaaRemoveBoxa (boxbasic.c)
    /// <param name="index">of the boxa to be removed</param>
    /// <remarks>
    /// (1) This removes boxa[index] and then shifts
    ///     boxa[i] --> boxa[i - 1] for all i > index.
    /// (2) The removed boxaa is dropped.
    /// (2) This should not be used repeatedly on large arrays,
    ///     because the function is O(n).
    /// </remarks>
    public void RemoveBoxa(int index)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("boxaaRemoveBoxa", "index not valid");

        Boxa?[] array = boxas;
        for (int i = index + 1; i < n; i++)
            array[i - 1] = array[i];
        array[n - 1] = null;
        this.n--;
    }

    // leptonica: boxaaAddBox (boxbasic.c)
    /// <param name="index">of boxa with boxaa</param>
    /// <param name="box">to be added</param>
    /// <param name="accessflag">L_INSERT, L_COPY or L_CLONE</param>
    /// <remarks>(1) Adds to an existing boxa only.</remarks>
    public void AddBox(int index, Box box, int accessflag)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("boxaaAddBox", "index not valid");
        if (accessflag != L.L_INSERT && accessflag != L.L_COPY && accessflag != L.L_CLONE)
            throw Lept.Arg("boxaaAddBox", "invalid accessflag");

        Boxa boxa = GetBoxa(index, L.L_CLONE);
        boxa.AddBox(box, accessflag);
    }

    /*---------------------------------------------------------------------*
     *                        Boxaa serialized I/O                         *
     *---------------------------------------------------------------------*/
    // leptonica: boxaaReadFromFiles (boxbasic.c)
    /// <param name="dirname">directory</param>
    /// <param name="substr">[optional] substring filter on filenames; can be null</param>
    /// <param name="first">0-based</param>
    /// <param name="nfiles">use 0 for everything from %first to the end</param>
    /// <remarks>
    /// (1) The files must be serialized boxa files (e.g., *.ba).
    ///     If some files cannot be read, warnings are issued.
    /// (2) Use %substr to filter filenames in the directory.  If
    ///     %substr == null, this takes all files.
    /// (3) After filtering, use %first and %nfiles to select
    ///     a contiguous set of files, that have been lexically
    ///     sorted in increasing order.
    /// </remarks>
    public static Boxaa ReadFromFiles(string dirname, string? substr, int first, int nfiles)
    {
        const string func = "boxaaReadFromFiles";

        Sarray sa = Sarray.GetSortedPathnamesInDirectory(dirname, substr, first, nfiles);
        int n = sa.Count;
        if (n == 0)
            throw Lept.Arg(func, "no pixa files found");

        Boxaa baa = Create(n);
        for (int i = 0; i < n; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            Boxa boxa;
            try {
                boxa = Boxa.Read(fname);
            } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
                Log.Error(func, $"boxa not read for {i}-th file");
                continue;
            }
            baa.AddBoxa(boxa, L.L_INSERT);
        }

        return baa;
    }

    /// <summary>Reads a serialized <see cref="Boxaa"/> from a file.</summary>
    /// <param name="filename">The path of the file to read.</param>
    /// <returns>The array of box arrays read from the file.</returns>
    /// <exception cref="ArgumentException">The file cannot be opened or its contents are not a valid serialized <see cref="Boxaa"/>.</exception>
    // leptonica: boxaaRead (boxbasic.c)
    public static Boxaa Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("boxaaRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in boxaaRead: boxaa not read: {filename}", e);
            }
        }
    }

    // leptonica: boxaaReadStream (boxbasic.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// (1) It is OK for the boxaa to be empty (n == 0).
    /// (2) If the stream is not seekable, the reader may consume one character
    ///     beyond the serialized data.
    /// </remarks>
    public static Boxaa ReadStream(Stream fp)
    {
        CScanner sc = new CScanner(fp);
        try {
            return ReadStream(sc);
        } finally {
            sc.Release();
        }
    }

    private static Boxaa ReadStream(CScanner sc)
    {
        const string func = "boxaaReadStream";
        Span<int> v = stackalloc int[5];

        if (sc.Scan("\nBoxaa Version %d\n", v) != 1)
            throw Lept.Arg(func, "not a boxaa file");
        int version = v[0];
        if (version != L.BOXAA_VERSION_NUMBER)
            throw Lept.Arg(func, "invalid boxa version");
        if (sc.Scan("Number of boxa = %d\n", v) != 1)
            throw Lept.Arg(func, "not a boxaa file");
        int n = v[0];
        if (n < 0)
            throw Lept.Arg(func, "num boxa ptrs < 0");
        if (n > MaxBoxaaPtrArraySize)
            throw Lept.Arg(func, "too many boxa ptrs");
        if (n == 0) Log.Info(func, "the boxaa is empty");

        Boxaa baa = Create(n);
        for (int i = 0; i < n; i++) {
            /* the extent values (x, y, w, h) are read and ignored */
            if (sc.Scan("\nBoxa[%d] extent: x = %d, y = %d, w = %d, h = %d", v) != 5)
                throw Lept.Arg(func, "boxa descr not valid");
            Boxa boxa = Boxa.ReadStream(sc);
            baa.AddBoxa(boxa, L.L_INSERT);
        }
        return baa;
    }

    // leptonica: boxaaReadMem (boxbasic.c)
    /// <param name="data">serialization of boxaa; in ascii</param>
    public static Boxaa ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (ArgumentException) {
            Log.Error("boxaaReadMem", "baa not read");
            throw;
        }
    }

    /// <summary>Writes the array of box arrays to a file in the serialized <see cref="Boxaa"/> format.</summary>
    /// <param name="filename">The path of the file to write.</param>
    /// <exception cref="ArgumentException">The file cannot be opened for writing.</exception>
    // leptonica: boxaaWrite (boxbasic.c)
    public void Write(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, "wb");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("boxaaWrite", $"stream not opened: {filename}");
        }
        using (fp) {
            WriteStream(fp);
        }
    }

    // leptonica: boxaaWriteStream (boxbasic.c)
    /// <param name="fp">output stream</param>
    public void WriteStream(Stream fp)
    {
        int n = Count;
        CStdio.Fprintf(fp, $"\nBoxaa Version {L.BOXAA_VERSION_NUMBER}\n");
        CStdio.Fprintf(fp, $"Number of boxa = {n}\n");

        for (int i = 0; i < n; i++) {
            Boxa boxa = GetBoxa(i, L.L_CLONE);
            boxa.GetExtent(out _, out _, out Box box);
            box.GetGeometry(out int x, out int y, out int w, out int h);
            CStdio.Fprintf(fp, $"\nBoxa[{i}] extent: x = {x}, y = {y}, w = {w}, h = {h}");
            boxa.WriteStream(fp);
        }
    }

    // leptonica: boxaaWriteMem (boxbasic.c)
    /// <summary>Serializes a boxaa in memory and returns the ascii bytes.</summary>
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }
}

public sealed partial class Box
{
    /*---------------------------------------------------------------------*
     *                            Debug printing                           *
     *---------------------------------------------------------------------*/
    // leptonica: boxPrintStreamInfo (boxbasic.c)
    /// <param name="fp">output stream; use null for stderr</param>
    /// <remarks>
    /// (1) This outputs debug info.  Use serialization functions to
    ///     write to file if you want to read the data back.
    /// </remarks>
    public void PrintStreamInfo(Stream? fp)
    {
        if (fp == null) {  /* output to stderr */
            CStdio.Stderr($" Box: x = {X}, y = {Y}, w = {Width}, h = {Height}\n");
        } else {
            CStdio.Fprintf(fp, $" Box: x = {X}, y = {Y}, w = {Width}, h = {Height}\n");
        }
    }
}
