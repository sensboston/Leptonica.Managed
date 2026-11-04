using System;
using System.IO;

namespace Leptonica;

/*
 *  pixabasic.c: Pixa and Pixaa creation, accessors, modifiers and
 *  serialization.
 *
 *  Differences from C:
 *    - No reference counting: pixaDestroy(), pixaaDestroy() are not
 *      ported; the GC owns the objects.  pixaCopy(L_CLONE) and
 *      pixaaGetPixa(L_CLONE) return the same reference.
 *    - FILE* becomes Stream.  Text is written as ASCII with invariant
 *      culture and "\n" line ends, followed by the png data, byte for byte
 *      as the C code writes it.  Reading follows the fscanf formats of the
 *      C code.  A stream that cannot seek is read completely into memory
 *      first, because the png data follows the text.
 *    - The *Mem variants take a ReadOnlySpan&lt;byte&gt; and return a byte[]
 *      (without the terminating NUL that C appends to its buffer).
 *    - pixaGetPixArray() returns the actual array, with null entries for
 *      missing pix.  pixaGetLinePtrs() returns, for each pix, the word
 *      offsets of its lines (see Pix.GetLinePtrs()).
 *    - Functions that return an error code or NULL in C throw instead,
 *      except where the C code logs and carries on (noted at the place).
 */

/// <summary>Constants from pix.h and pix_internal.h used by Pixa and Pixaa.</summary>
public static partial class L
{
    /// <summary>Version number of the serialized <see cref="Pixa"/> format.</summary>
    public const int PIXA_VERSION_NUMBER = 2;
    /// <summary>Version number of the serialized <see cref="Pixaa"/> format.</summary>
    public const int PIXAA_VERSION_NUMBER = 2;

    /* Consecutive or skip-by selection (pix.h) */
    /// <summary>Select <c>n</c> consecutive items.</summary>
    public const int L_CHOOSE_CONSECUTIVE = 1;
    /// <summary>Select items at intervals of <c>n</c>.</summary>
    public const int L_CHOOSE_SKIP_BY = 2;
}

/// <summary>Array of Pix, with an optional array of boxes (struct Pixa).</summary>
public sealed partial class Pixa
{
    /* Bounds on array sizes */
    private const int MaxInitPtrArraySize = 100000;
    private const int MaxPixaPtrArraySize = 5000000;
    private const int InitialPtrArraySize = 20;      /* n'importe quoi */

    private Pix?[] pix;    /* the array of ptrs to pix; its length is nalloc */
    private int n;         /* number of Pix in ptr array                     */

    /// <summary>Boxes of the pix in this array (pixa->boxa).</summary>
    public Boxa Boxa { get; internal set; }

    private Pixa(int nalloc)
    {
        pix = new Pix?[nalloc];
        n = 0;
        Boxa = Boxa.Create(nalloc);
    }

    /// <summary>Number of allocated pix ptrs (pixa->nalloc).</summary>
    public int Nalloc => pix.Length;

    /*---------------------------------------------------------------------*
     *                    Pixa creation, destruction, copy                 *
     *---------------------------------------------------------------------*/
    // leptonica: pixaCreate (pixabasic.c)
    /// <param name="n">initial number of ptrs</param>
    /// <remarks>(1) This creates an empty boxa.</remarks>
    public static Pixa Create(int n)
    {
        if (n <= 0 || n > MaxInitPtrArraySize)
            n = InitialPtrArraySize;

        return new Pixa(n);
    }

    // leptonica: pixaCreateFromPix (pixabasic.c)
    /// <param name="pixs">with individual components on a lattice</param>
    /// <param name="n">number of components</param>
    /// <param name="cellw">width of each cell</param>
    /// <param name="cellh">height of each cell</param>
    /// <remarks>
    /// (1) For bpp = 1, we truncate each retrieved pix to the ON
    ///     pixels, which we assume for now start at (0,0)
    /// </remarks>
    public static Pixa CreateFromPix(Pix pixs, int n, int cellw, int cellh)
    {
        if (n <= 0)
            throw Lept.Arg("pixaCreateFromPix", "n must be > 0");

        Pixa pixa = Create(n);
        pixs.GetDimensions(out int w, out int h, out int d);
        Pix pix1 = Pix.Create(cellw, cellh, d);

        int nw = (w + cellw - 1) / cellw;
        int nh = (h + cellh - 1) / cellh;
        for (int i = 0, index = 0; i < nh; i++) {
            for (int j = 0; j < nw && index < n; j++, index++) {
                pix1.Rasterop(0, 0, cellw, cellh, L.PIX_SRC, pixs, j * cellw, i * cellh);
                Pix? pix2 = null;
                if (d == 1)
                    pix1.ClipToForeground(out pix2, out _);
                if (pix2 != null)
                    pixa.AddPix(pix2, L.L_INSERT);
                else
                    pixa.AddPix(pix1, L.L_COPY);
            }
        }

        return pixa;
    }

    // leptonica: pixaCreateFromBoxa (pixabasic.c)
    /// <param name="pixs"></param>
    /// <param name="boxa"></param>
    /// <param name="start">first box to use</param>
    /// <param name="num">number of boxes; use 0 to go to the end</param>
    /// <param name="cropwarn">true if the boxa extent is larger than pixs</param>
    /// <remarks>
    /// (1) This simply extracts from pixs the region corresponding to each
    ///     box in the boxa.  To extract all the regions, set both %start
    ///     and %num to 0.
    /// (2) The 5th arg is optional.  If the extent of the boxa exceeds the
    ///     size of the pixa, so that some boxes are either clipped
    ///     or entirely outside the pix, a warning is returned as true.
    /// (3) pixad will have only the properly clipped elements, and
    ///     the internal boxa will be correct.
    /// </remarks>
    public static Pixa CreateFromBoxa(Pix pixs, Boxa boxa, int start, int num, out bool cropwarn)
    {
        const string func = "pixaCreateFromBoxa";
        if (num < 0)
            throw Lept.Arg(func, "num must be >= 0");

        int n = boxa.Count;
        int end = (num == 0) ? n - 1 : Math.Min(start + num - 1, n - 1);
        Pixa pixad = Create(end - start + 1);

        boxa.GetExtent(out int wbox, out int hbox, out _);
        pixs.GetDimensions(out int w, out int h, out _);
        cropwarn = false;
        if (wbox > w || hbox > h)
            cropwarn = true;

        for (int i = start; i <= end; i++) {
            Box box = boxa.GetBox(i, L.L_COPY);
            if (cropwarn) {  /* if box is outside pixs, pixd is null */
                Pix? pixd = pixs.ClipRectangle(box, out Box? boxc);  /* may be null */
                if (pixd != null) {
                    pixad.AddPix(pixd, L.L_INSERT);
                    pixad.AddBox(boxc!, L.L_INSERT);
                }
            } else {
                Pix? pixd = pixs.ClipRectangle(box, out _);
                if (pixd != null)
                    pixad.AddPix(pixd, L.L_INSERT);
                else  /* C: pixaAddPix() fails and the box is added anyway */
                    Log.Error("pixaAddPix", "pix not defined");
                pixad.AddBox(box, L.L_INSERT);
            }
        }

        return pixad;
    }

    // leptonica: pixaSplitPix (pixabasic.c)
    /// <param name="pixs">with individual components on a lattice</param>
    /// <param name="nx">number of mosaic cells horizontally</param>
    /// <param name="ny">number of mosaic cells vertically</param>
    /// <param name="borderwidth">of added border on all sides</param>
    /// <param name="bordercolor">in our RGBA format: 0xrrggbbaa</param>
    /// <remarks>
    /// (1) This is a variant on pixaCreateFromPix(), where we
    ///     simply divide the image up into (approximately) equal
    ///     subunits.  If you want the subimages to have essentially
    ///     the same aspect ratio as the input pix, use nx = ny.
    /// (2) If borderwidth is 0, we ignore the input bordercolor and
    ///     redefine it to white.
    /// (3) The bordercolor is always used to initialize each tiled pix,
    ///     so that if the src is clipped, the unblitted part will
    ///     be this color.  This avoids 1 pixel wide black stripes at the
    ///     left and lower edges.
    /// </remarks>
    public static Pixa SplitPix(Pix pixs, int nx, int ny, int borderwidth, uint bordercolor)
    {
        if (nx <= 0 || ny <= 0)
            throw Lept.Arg("pixaSplitPix", "nx and ny must be > 0");
        borderwidth = Math.Max(0, borderwidth);

        Pixa pixa = Create(nx * ny);
        pixs.GetDimensions(out int w, out int h, out int d);
        int cellw = (w + nx - 1) / nx;  /* round up */
        int cellh = (h + ny - 1) / ny;

        for (int i = 0; i < ny; i++) {
            for (int j = 0; j < nx; j++) {
                Pix pix1 = Pix.Create(cellw + 2 * borderwidth, cellh + 2 * borderwidth, d);
                pix1.CopyColormap(pixs);
                if (borderwidth == 0) {  /* initialize full image to white */
                    if (d == 1)
                        pix1.ClearAll();
                    else
                        pix1.SetAll();
                } else {
                    pix1.SetAllArbitrary(bordercolor);
                }
                pix1.Rasterop(borderwidth, borderwidth, cellw, cellh,
                              L.PIX_SRC, pixs, j * cellw, i * cellh);
                pixa.AddPix(pix1, L.L_INSERT);
            }
        }

        return pixa;
    }

    // leptonica: pixaCopy (pixabasic.c)
    /// <param name="copyflag">see pix.h for details:
    ///   L_COPY makes a new pixa and copies each pix and each box;
    ///   L_CLONE returns the same pixa;
    ///   L_COPY_CLONE makes a new pixa and inserts clones of all pix and boxes</param>
    /// <returns>new pixa</returns>
    public Pixa Copy(int copyflag)
    {
        if (copyflag == L.L_CLONE)
            return this;

        if (copyflag != L.L_COPY && copyflag != L.L_COPY_CLONE)
            throw Lept.Arg("pixaCopy", "invalid copyflag");

        Pixa pixac = Create(n);
        int nb = BoxaCount;
        Box? boxc = null;
        for (int i = 0; i < n; i++) {
            Pix pixc;
            if (copyflag == L.L_COPY) {
                pixc = GetPix(i, L.L_COPY);
                if (i < nb) boxc = GetBox(i, L.L_COPY);
            } else {  /* copy-clone */
                pixc = GetPix(i, L.L_CLONE);
                if (i < nb) boxc = GetBox(i, L.L_CLONE);
            }
            pixac.AddPix(pixc, L.L_INSERT);
            if (i < nb) {
                if (boxc != null)
                    pixac.AddBox(boxc, L.L_INSERT);
                else  /* C: pixaAddBox() fails for a null box */
                    Log.Error("pixaAddBox", "box not defined");
            }
        }

        return pixac;
    }

    /*---------------------------------------------------------------------*
     *                              Pixa addition                          *
     *---------------------------------------------------------------------*/
    // leptonica: pixaAddPix (pixabasic.c)
    /// <param name="pix">to be added</param>
    /// <param name="copyflag">L_INSERT, L_COPY, L_CLONE</param>
    public void AddPix(Pix pix, int copyflag)
    {
        Pix pixc;
        if (copyflag == L.L_INSERT)
            pixc = pix;
        else if (copyflag == L.L_COPY)
            pixc = Pix.Copy(null, pix);
        else if (copyflag == L.L_CLONE)
            pixc = Pix.Clone(pix);
        else
            throw Lept.Arg("pixaAddPix", "invalid copyflag");

        int n = Count;
        if (n >= Nalloc)
            ExtendArray();
        this.pix[n] = pixc;
        this.n++;
    }

    // leptonica: pixaAddBox (pixabasic.c)
    /// <param name="box"></param>
    /// <param name="copyflag">L_INSERT, L_COPY, L_CLONE</param>
    public void AddBox(Box box, int copyflag)
    {
        if (copyflag != L.L_INSERT && copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg("pixaAddBox", "invalid copyflag");

        Boxa.AddBox(box, copyflag);
    }

    // leptonica: pixaExtendArray (pixabasic.c)
    /// <remarks>
    /// (1) Doubles the size of the pixa and boxa ptr arrays.
    /// (2) The max number of pix in the array is 5 million.
    /// </remarks>
    private void ExtendArray()
    {
        ExtendArrayToSize(2L * Nalloc);
    }

    // leptonica: pixaExtendArrayToSize (pixabasic.c)
    /// <param name="size">number of pix ptrs in new array</param>
    /// <remarks>
    /// (1) If necessary, reallocs new pixa and boxa ptrs arrays to %size.
    ///     The pixa and boxa ptr arrays must always be equal in size.
    /// (2) The max number of pix ptrs is 5M.
    /// </remarks>
    public void ExtendArrayToSize(long size)
    {
        if (Nalloc > MaxPixaPtrArraySize)  /* belt & suspenders */
            throw Lept.Fail("pixaExtendArrayToSize", "pixa has too many ptrs");
        /* size is a size_t in C, so a negative value is huge */
        if (size < 0 || size > MaxPixaPtrArraySize)
            throw Lept.Arg("pixaExtendArrayToSize", "size > 5M ptrs; too large");
        if (size <= Nalloc) {
            Log.Info("pixaExtendArrayToSize", "size too small; no extension");
            return;
        }

        Array.Resize(ref pix, (int)size);
        Boxa.ExtendArrayToSize(size);
    }

    /*---------------------------------------------------------------------*
     *                             Pixa accessors                          *
     *---------------------------------------------------------------------*/
    // leptonica: pixaGetCount (pixabasic.c)
    /// <summary>Number of pix in the array.</summary>
    public int Count => n;

    // leptonica: pixaGetPix (pixabasic.c)
    /// <param name="index">to the index-th pix</param>
    /// <param name="accessflag">L_COPY or L_CLONE</param>
    public Pix GetPix(int index, int accessflag)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("pixaGetPix", "index not valid");
        Pix? pix = this.pix[index];
        if (pix == null) {
            Log.Error("pixaGetPix", $"no pix at pixa[{index}]");
            throw Lept.Arg("pixaGetPix", "pix not found!");
        }

        if (accessflag == L.L_COPY)
            return Pix.Copy(null, pix);
        else if (accessflag == L.L_CLONE)
            return Pix.Clone(pix);
        else
            throw Lept.Arg("pixaGetPix", "invalid accesstype");
    }

    /// <summary>pixaGetPix(pixa, index, L_CLONE) for the callers that test the result for NULL in C.</summary>
    private Pix? GetPixOrNull(int index)
    {
        try {
            return GetPix(index, L.L_CLONE);
        } catch (ArgumentException) {
            return null;
        }
    }

    // leptonica: pixaGetPixDimensions (pixabasic.c)
    /// <summary>Gets the width, height and depth of one image in the array.</summary>
    /// <param name="index">Index of the image.</param>
    /// <param name="pw">Receives the image width in pixels.</param>
    /// <param name="ph">Receives the image height in pixels.</param>
    /// <param name="pd">Receives the image depth in bits per pixel.</param>
    /// <exception cref="ArgumentException"><paramref name="index"/> is out of range.</exception>
    public void GetPixDimensions(int index, out int pw, out int ph, out int pd)
    {
        pw = 0;
        ph = 0;
        pd = 0;
        if (index < 0 || index >= n)
            throw Lept.Arg("pixaGetPixDimensions", "index not valid");

        Pix pix = GetPix(index, L.L_CLONE);
        pix.GetDimensions(out pw, out ph, out pd);
    }

    // leptonica: pixaGetBoxa (pixabasic.c)
    /// <param name="accesstype">L_COPY, L_CLONE, L_COPY_CLONE</param>
    public Boxa GetBoxa(int accesstype)
    {
        if (accesstype != L.L_COPY && accesstype != L.L_CLONE && accesstype != L.L_COPY_CLONE)
            throw Lept.Arg("pixaGetBoxa", "invalid accesstype");

        return Boxa.Copy(accesstype);
    }

    // leptonica: pixaGetBoxaCount (pixabasic.c)
    /// <summary>Number of boxes in the boxa.</summary>
    public int BoxaCount => Boxa.Count;

    // leptonica: pixaGetBox (pixabasic.c)
    /// <param name="index">to the index-th pix</param>
    /// <param name="accesstype">L_COPY or L_CLONE</param>
    /// <returns>box; null if there is no box at this index (not automatically an error)</returns>
    /// <remarks>
    /// (1) There is always a boxa with a pixa, and it is initialized so
    ///     that each box ptr is null.
    /// (2) In general, we expect that there is either a box associated
    ///     with each pix, or no boxes at all in the boxa.
    /// (3) Having no boxes is thus not an automatic error.  Whether it
    ///     is an actual error is determined by the calling program.
    ///     If the caller expects to get a box, it is an error; see, e.g.,
    ///     pixaGetBoxGeometry().
    /// </remarks>
    public Box? GetBox(int index, int accesstype)
    {
        if (index < 0 || index >= Boxa.Count)
            throw Lept.Arg("pixaGetBox", "index not valid");
        if (accesstype != L.L_COPY && accesstype != L.L_CLONE)
            throw Lept.Arg("pixaGetBox", "invalid accesstype");

        Box? box = Boxa.PeekBox(index);
        if (box != null) {
            if (accesstype == L.L_COPY)
                return box.Copy();
            else  /* accesstype == L_CLONE */
                return box.Clone();
        } else {
            return null;
        }
    }

    // leptonica: pixaGetBoxGeometry (pixabasic.c)
    /// <summary>Gets the location and size of one box in the array.</summary>
    /// <param name="index">Index of the box.</param>
    /// <param name="x">Receives the left coordinate of the box.</param>
    /// <param name="y">Receives the top coordinate of the box.</param>
    /// <param name="w">Receives the width of the box.</param>
    /// <param name="h">Receives the height of the box.</param>
    /// <exception cref="ArgumentException"><paramref name="index"/> is out of range.</exception>
    public void GetBoxGeometry(int index, out int x, out int y, out int w, out int h)
    {
        x = 0;
        y = 0;
        w = 0;
        h = 0;
        if (index < 0 || index >= n)
            throw Lept.Arg("pixaGetBoxGeometry", "index not valid");

        Box box = GetBox(index, L.L_CLONE) ?? throw Lept.Arg("pixaGetBoxGeometry", "box not found!");
        box.GetGeometry(out x, out y, out w, out h);
    }

    // leptonica: pixaSetBoxa (pixabasic.c)
    /// <param name="boxa"></param>
    /// <param name="accesstype">L_INSERT, L_COPY, L_CLONE</param>
    /// <remarks>(1) This destroys the existing boxa in the pixa.</remarks>
    public void SetBoxa(Boxa boxa, int accesstype)
    {
        if (accesstype != L.L_INSERT && accesstype != L.L_COPY && accesstype != L.L_CLONE)
            throw Lept.Arg("pixaSetBoxa", "invalid access type");

        if (accesstype == L.L_INSERT)
            Boxa = boxa;
        else
            Boxa = boxa.Copy(accesstype);
    }

    // leptonica: pixaGetPixArray (pixabasic.c)
    /// <returns>the actual array of pix, owned by the pixa; entries can be null</returns>
    /// <remarks>
    /// (1) This returns the actual array.  Its length is the number of
    ///     allocated ptrs, and only the first <see cref="Count"/> entries are in use.
    /// (2) The caller should always check if the entries are null
    ///     before accessing any of the pix in this array!
    /// </remarks>
    public Pix?[] GetPixArray()
    {
        return pix;
    }

    // leptonica: pixaVerifyDepth (pixabasic.c)
    /// <param name="same">true if depth is the same for all pix</param>
    /// <param name="maxd">max depth of all pix</param>
    /// <remarks>(1) It is considered to be an error if there are no pix.</remarks>
    public void VerifyDepth(out bool same, out int maxd)
    {
        same = false;
        maxd = 0;
        int n = Count;
        if (n == 0)
            throw Lept.Arg("pixaVerifyDepth", "no pix in pixa");

        bool same1 = true;
        GetPixDimensions(0, out _, out _, out maxd);
        for (int i = 1; i < n; i++) {
            GetPixDimensions(i, out _, out _, out int d);
            maxd = Math.Max(maxd, d);
            if (d != maxd)
                same1 = false;
        }
        same = same1;
    }

    // leptonica: pixaVerifyDimensions (pixabasic.c)
    /// <param name="same">true if dimensions are the same for all pix</param>
    /// <param name="maxw">max width of all pix</param>
    /// <param name="maxh">max height of all pix</param>
    /// <remarks>(1) It is considered to be an error if there are no pix.</remarks>
    public void VerifyDimensions(out bool same, out int maxw, out int maxh)
    {
        same = false;
        maxw = 0;
        maxh = 0;
        int n = Count;
        if (n == 0)
            throw Lept.Arg("pixaVerifyDimensions", "no pix in pixa");

        bool same1 = true;
        GetPixDimensions(0, out maxw, out maxh, out _);
        for (int i = 1; i < n; i++) {
            GetPixDimensions(i, out int w, out int h, out _);
            maxw = Math.Max(maxw, w);
            maxh = Math.Max(maxh, h);
            if (w != maxw || h != maxh)
                same1 = false;
        }
        same = same1;
    }

    // leptonica: pixaIsFull (pixabasic.c)
    /// <param name="pfullpa">true if all Pix are present</param>
    /// <param name="pfullba">true if the Boxa has a Box for each Pix</param>
    /// <remarks>
    /// (1) A pixa is "full" if the array of pix is fully
    ///     occupied from index 0 to index (pixa->n - 1).
    /// </remarks>
    public void IsFull(out bool pfullpa, out bool pfullba)
    {
        int n = Count;
        bool full = true;
        for (int i = 0; i < n; i++) {
            if (GetPixOrNull(i) == null) {
                full = false;
                break;
            }
        }
        pfullpa = full;

        Boxa boxa = GetBoxa(L.L_CLONE);
        pfullba = boxa.IsFull();
    }

    // leptonica: pixaCountText (pixabasic.c)
    /// <returns>number of pix with non-empty text strings</returns>
    /// <remarks>
    /// (1) All pix have non-empty text strings if the returned value
    ///     equals the pixa count.
    /// </remarks>
    public int CountText()
    {
        int ntext = 0;
        int n = Count;
        for (int i = 0; i < n; i++) {
            Pix? pix = GetPixOrNull(i);
            if (pix == null)
                continue;
            string? text = pix.Text;
            if (text != null && text.Length > 0)
                ntext++;
        }

        return ntext;
    }

    // leptonica: pixaSetText (pixabasic.c)
    /// <param name="text">[optional] single text string, to insert in each pix</param>
    /// <param name="sa">[optional] array of text strings, to insert in each pix</param>
    /// <remarks>
    /// (1) To clear all the text fields, use %sa == null and %text == null.
    /// (2) Otherwise, this replaces all text fields with a copy of a string,
    ///     either the same string or a string from %sa.
    /// (3) To set all the text fields to the same value %text, use %sa = null.
    /// (4) If %sa is defined, ignore %text and use the strings in %sa.
    ///     %sa must have the same count as %pixa.
    /// </remarks>
    public void SetText(string? text, Sarray? sa)
    {
        int n = Count;
        if (sa != null && sa.Count != n)
            throw Lept.Arg("pixaSetText", "pixa and sa sizes differ");

        if (sa == null) {
            for (int i = 0; i < n; i++) {
                Pix? pix = GetPixOrNull(i);
                if (pix == null)
                    continue;
                pix.Text = text;
            }
            return;
        }

        for (int i = 0; i < n; i++) {
            Pix? pix = GetPixOrNull(i);
            if (pix == null)
                continue;
            string str = sa.GetString(i, L.L_NOCOPY);
            pix.Text = str;
        }
    }

    // leptonica: pixaGetLinePtrs (pixabasic.c)
    /// <returns>for each pix, the word offsets of its lines in the pix data</returns>
    /// <remarks>
    /// (1) See <see cref="Pix.GetLinePtrs"/> for details.
    /// (2) It is best if all pix in the pixa are the same size.
    ///     The size of each line ptr array is equal to the height
    ///     of the pix that it refers to.
    /// </remarks>
    public int[][] GetLinePtrs()
    {
        VerifyDepth(out bool same, out _);
        if (!same)
            throw Lept.Arg("pixaGetLinePtrs", "pixa not all same depth");
        int n = Count;
        int[][] lineset = new int[n][];
        for (int i = 0; i < n; i++) {
            Pix pix = GetPix(i, L.L_CLONE);
            lineset[i] = pix.GetLinePtrs();
        }

        return lineset;
    }

    /*---------------------------------------------------------------------*
     *                         Pixa output info                            *
     *---------------------------------------------------------------------*/
    // leptonica: pixaWriteStreamInfo (pixabasic.c)
    /// <param name="fp">output stream</param>
    /// <remarks>
    /// (1) For each pix in the pixa, write out the pix dimensions, spp,
    ///     text string (if it exists), and cmap info.
    /// </remarks>
    public void WriteStreamInfo(Stream fp)
    {
        int n = Count;
        for (int i = 0; i < n; i++) {
            Pix? pix = GetPixOrNull(i);
            if (pix == null) {
                CStdio.Fprintf(fp, $"{i}: no pix at this index\n");
                continue;
            }
            pix.GetDimensions(out int w, out int h, out int d);
            int spp = pix.Spp;
            string? text = pix.Text;
            bool hastext = text != null && text.Length > 0;
            PixColormap? cmap = pix.Colormap;
            CStdio.Fprintf(fp, $"Pix {i}: w = {w}, h = {h}, d = {d}, spp = {spp}");
            if (cmap != null) CStdio.Fprintf(fp, $", cmap({cmap.Count} colors)");
            if (hastext) CStdio.Fprintf(fp, $", text = {text}");
            CStdio.Fprintf(fp, $"\n");
        }
    }

    /*---------------------------------------------------------------------*
     *                       Pixa array modifiers                          *
     *---------------------------------------------------------------------*/
    // leptonica: pixaReplacePix (pixabasic.c)
    /// <param name="index">to the index-th pix</param>
    /// <param name="pix">insert to replace existing one</param>
    /// <param name="box">[optional] insert to replace existing</param>
    /// <remarks>
    /// (1) In-place replacement of one pix.
    /// (2) The previous pix at that location is dropped.
    /// </remarks>
    public void ReplacePix(int index, Pix pix, Box? box)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("pixaReplacePix", "index not valid");

        this.pix[index] = pix;

        if (box != null) {
            Boxa boxa = Boxa;
            if (index > boxa.Count)
                throw Lept.Arg("pixaReplacePix", "boxa index not valid");
            boxa.ReplaceBox(index, box);
        }
    }

    // leptonica: pixaInsertPix (pixabasic.c)
    /// <param name="index">at which pix is to be inserted</param>
    /// <param name="pixs">new pix to be inserted</param>
    /// <param name="box">[optional] new box to be inserted</param>
    /// <remarks>
    /// (1) This shifts pixa[i] --> pixa[i + 1] for all i >= index,
    ///     and then inserts at pixa[index].
    /// (2) To insert at the beginning of the array, set index = 0.
    /// (3) It should not be used repeatedly on large arrays,
    ///     because the function is O(n).
    /// (4) To append a pix to a pixa, it's easier to use pixaAddPix().
    /// </remarks>
    public void InsertPix(int index, Pix pixs, Box? box)
    {
        int n = Count;
        if (index < 0 || index > n)
            throw Lept.Arg("pixaInsertPix", $"index {index} not in [0,...,{n}]");

        if (n >= Nalloc) {  /* extend both ptr arrays */
            ExtendArray();
            Boxa.ExtendArray();
        }
        this.n++;
        for (int i = n; i > index; i--)
            pix[i] = pix[i - 1];
        pix[index] = pixs;

            /* Optionally, insert the box */
        if (box != null)
            Boxa.InsertBox(index, box);
    }

    // leptonica: pixaRemovePix (pixabasic.c)
    /// <param name="index">of pix to be removed</param>
    /// <remarks>
    /// (1) This shifts pixa[i] --> pixa[i - 1] for all i > index.
    /// (2) It should not be used repeatedly on large arrays,
    ///     because the function is O(n).
    /// (3) The corresponding box is removed as well, if it exists.
    /// </remarks>
    public void RemovePix(int index)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("pixaRemovePix", $"index {index} not in [0,...,{n - 1}]");

            /* Remove the pix */
        Pix?[] array = pix;
        for (int i = index + 1; i < n; i++)
            array[i - 1] = array[i];
        array[n - 1] = null;
        this.n--;

            /* Remove the box if it exists */
        Boxa boxa = Boxa;
        int nbox = boxa.Count;
        if (index < nbox)
            boxa.RemoveBox(index);
    }

    // leptonica: pixaRemovePixAndSave (pixabasic.c)
    /// <param name="index">of pix to be removed</param>
    /// <param name="ppix">removed pix; null if there was none</param>
    /// <param name="pbox">removed box; null if there was none</param>
    /// <remarks>
    /// (1) This shifts pixa[i] --> pixa[i - 1] for all i > index.
    /// (2) It should not be used repeatedly on large arrays,
    ///     because the function is O(n).
    /// (3) The corresponding box is removed as well, if it exists.
    /// </remarks>
    public void RemovePixAndSave(int index, out Pix? ppix, out Box? pbox)
    {
        ppix = null;
        pbox = null;
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("pixaRemovePixAndSave", $"index {index} not in [0,...,{n - 1}]");

            /* Remove the pix */
        Pix?[] array = pix;
        ppix = GetPixOrNull(index);
        for (int i = index + 1; i < n; i++)
            array[i - 1] = array[i];
        array[n - 1] = null;
        this.n--;

            /* Remove the box if it exists  */
        Boxa boxa = Boxa;
        int nbox = boxa.Count;
        if (index < nbox)
            boxa.RemoveBoxAndSave(index, out pbox);
    }

    // leptonica: pixaRemoveSelected (pixabasic.c)
    /// <param name="naindex">numa of indices of pix to be removed</param>
    /// <remarks>(1) This gives error messages for invalid indices</remarks>
    public void RemoveSelected(Numa naindex)
    {
        int n = naindex.Count;
        if (n == 0)
            throw Lept.Arg("pixaRemoveSelected", "naindex is empty");

            /* Remove from highest indices first */
        Numa na1 = Numa.Sort(null, naindex, L.L_SORT_DECREASING);
        for (int i = 0; i < n; i++) {
            int index = na1.GetIValue(i);
            RemovePix(index);
        }
    }

    // leptonica: pixaInitFull (pixabasic.c)
    /// <param name="pix">[optional] to be replicated to the entire pixa ptr array</param>
    /// <param name="box">[optional] to be replicated to the entire boxa ptr array</param>
    /// <remarks>
    /// (1) This initializes a pixa by filling up the entire pix ptr array
    ///     with copies of %pix.  If %pix == null, we use a tiny placeholder
    ///     pix (w = h = d = 1).  Any existing pix are dropped.
    ///     It also optionally fills the boxa with copies of %box.
    ///     After this operation, the numbers of pix and (optionally)
    ///     boxes are equal to the number of allocated ptrs.
    /// (2) Note that we use pixaReplacePix() instead of pixaInsertPix().
    ///     They both have the same effect when inserting into a null ptr
    ///     in the pixa ptr array:
    /// (3) If the boxa is not initialized (i.e., filled with boxes),
    ///     later insertion of boxes will cause an error, because the
    ///     'n' field is 0.
    /// (4) Example usage.  This function is useful to prepare for a
    ///     random insertion (or replacement) of pix into a pixa.
    ///     To randomly insert pix into a pixa, without boxes, up to
    ///     some index "max":
    ///        Pixa pixa = Pixa.Create(max);
    ///        pixa.InitFull(null, null);
    ///     An existing pixa with a smaller ptr array can also be reused:
    ///        pixa.ExtendArrayToSize(max);
    ///        pixa.InitFull(null, null);
    ///     The initialization allows the pixa to always be properly
    ///     filled, even if all pix (and boxes) are not later replaced.
    /// </remarks>
    public void InitFull(Pix? pix, Box? box)
    {
        int n = Nalloc;
        this.n = n;
        for (int i = 0; i < n; i++) {
            Pix pix1;
            if (pix != null)
                pix1 = Pix.Copy(null, pix);
            else
                pix1 = Pix.Create(1, 1, 1);
            ReplacePix(i, pix1, null);
        }
        if (box != null)
            Boxa.InitFull(box);
    }

    // leptonica: pixaClear (pixabasic.c)
    /// <remarks>
    /// (1) This drops all pix in the pixa, as well as
    ///     all boxes in the boxa.  The ptrs in the pix ptr array
    ///     are all null'd.  The number of allocated pix, n, is set to 0.
    /// </remarks>
    public void Clear()
    {
        int n = Count;
        for (int i = 0; i < n; i++)
            pix[i] = null;
        this.n = 0;
        Boxa.Clear();
    }

    /*---------------------------------------------------------------------*
     *                      Pixa and Pixaa combination                     *
     *---------------------------------------------------------------------*/
    // leptonica: pixaJoin (pixabasic.c)
    /// <param name="pixas">[optional] source pixa; add from this one</param>
    /// <param name="istart">starting index in pixas</param>
    /// <param name="iend">ending index in pixas; use -1 to cat all</param>
    /// <remarks>
    /// (1) This appends a clone of each indicated pix in pixas to this pixa
    /// (2) istart &lt; 0 is taken to mean 'read from the start' (istart = 0)
    /// (3) iend &lt; 0 means 'read to the end'
    /// (4) If pixas is null or contains no pix, this is a no-op.
    /// </remarks>
    public void Join(Pixa? pixas, int istart, int iend)
    {
        Pixa pixad = this;
        int n;
        if (pixas == null || ((n = pixas.Count) == 0))
            return;

        if (istart < 0)
            istart = 0;
        if (iend < 0 || iend >= n)
            iend = n - 1;
        if (istart > iend)
            throw Lept.Arg("pixaJoin", "istart > iend; nothing to add");

        for (int i = istart; i <= iend; i++) {
            Pix pix = pixas.GetPix(i, L.L_CLONE);
            pixad.AddPix(pix, L.L_INSERT);
        }

        Boxa boxas = pixas.GetBoxa(L.L_CLONE);
        Boxa boxad = pixad.GetBoxa(L.L_CLONE);
        int nb = pixas.BoxaCount;
        iend = Math.Min(iend, nb - 1);
        try {
            boxad.Join(boxas, istart, iend);
        } catch (ArgumentException e) {
            /* C: the error return of boxaJoin() is not checked */
            Log.Error("pixaJoin", e.Message);
        }
    }

    // leptonica: pixaInterleave (pixabasic.c)
    /// <param name="pixa2">second src pixa; this is the first</param>
    /// <param name="copyflag">L_CLONE, L_COPY</param>
    /// <returns>pixa interleaved from sources</returns>
    /// <remarks>
    /// (1) %copyflag determines if the pix are copied or cloned.
    ///     The boxes, if they exist, are copied.
    /// (2) If the two pixa have different sizes, a warning is issued,
    ///     and the number of pairs returned is the minimum size.
    /// </remarks>
    public Pixa Interleave(Pixa pixa2, int copyflag)
    {
        Pixa pixa1 = this;
        if (copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg("pixaInterleave", "invalid copyflag");
        int n1 = pixa1.Count;
        int n2 = pixa2.Count;
        int n = Math.Min(n1, n2);
        if (n == 0)
            throw Lept.Arg("pixaInterleave", "at least one input pixa is empty");
        if (n1 != n2)
            Log.Warning("pixaInterleave", $"counts differ: {n1} != {n2}");

        Pixa pixad = Create(2 * n);
        int nb1 = pixa1.BoxaCount;
        int nb2 = pixa2.BoxaCount;
        for (int i = 0; i < n; i++) {
            Pix pix = pixa1.GetPix(i, copyflag);
            pixad.AddPix(pix, L.L_INSERT);
            if (i < nb1) {
                Box? box = pixa1.GetBox(i, L.L_COPY);
                if (box != null)
                    pixad.AddBox(box, L.L_INSERT);
                else  /* C: pixaAddBox() fails for a null box */
                    Log.Error("pixaAddBox", "box not defined");
            }
            pix = pixa2.GetPix(i, copyflag);
            pixad.AddPix(pix, L.L_INSERT);
            if (i < nb2) {
                Box? box = pixa2.GetBox(i, L.L_COPY);
                if (box != null)
                    pixad.AddBox(box, L.L_INSERT);
                else
                    Log.Error("pixaAddBox", "box not defined");
            }
        }

        return pixad;
    }

    /*---------------------------------------------------------------------*
     *                          Pixa serialized I/O                        *
     *---------------------------------------------------------------------*/
    // leptonica: pixaRead (pixabasic.c)
    /// <remarks>(1) The pix are stored in the file as png.</remarks>
    public static Pixa Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("pixaRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
                throw new ArgumentException($"Error in pixaRead: pixa not read: {filename}", e);
            }
        }
    }

    // leptonica: pixaReadStream (pixabasic.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// (1) The pix are stored in the file as png.
    /// (2) It is OK for the pixa to be empty.
    /// (3) A stream that cannot seek is read completely into memory first.
    /// </remarks>
    public static Pixa ReadStream(Stream fp)
    {
        fp = Seekable(fp);
        CScanner sc = new CScanner(fp);
        try {
            return ReadStream(fp, sc);
        } finally {
            sc.Release();
        }
    }

    /// <summary>
    /// The png reader needs the stream positioned exactly after the text, so that the
    /// character that fscanf() reads ahead can be given back: this needs a seekable stream.
    /// </summary>
    internal static Stream Seekable(Stream fp)
    {
        if (fp.CanSeek)
            return fp;
        MemoryStream ms = new MemoryStream();
        fp.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }

    internal static Pixa ReadStream(Stream fp, CScanner sc)
    {
        const string func = "pixaReadStream";
        Span<int> v = stackalloc int[3];

        if (sc.Scan("\nPixa Version %d\n", v) != 1)
            throw Lept.Arg(func, "not a pixa file");
        int version = v[0];
        if (version != L.PIXA_VERSION_NUMBER)
            throw Lept.Arg(func, "invalid pixa version");
        if (sc.Scan("Number of pix = %d\n", v) != 1)
            throw Lept.Arg(func, "not a pixa file");
        int n = v[0];
        if (n < 0)
            throw Lept.Arg(func, "num pix ptrs < 0");
        if (n > MaxPixaPtrArraySize)
            throw Lept.Arg(func, "too many pix ptrs");
        if (n == 0) Log.Info(func, "the pixa is empty");

        Boxa boxa = Boxa.ReadStream(sc);
        Pixa pixa = Create(n);
        pixa.Boxa = boxa;

        for (int i = 0; i < n; i++) {
            if (sc.Scan(" pix[%d]: xres = %d, yres = %d\n", v) != 3)
                throw Lept.Arg(func, "res reading error");
            int xres = v[1], yres = v[2];
            sc.Release();  /* the png data follows */
            Pix pix = Pix.ReadStreamPng(fp);
            pix.XRes = xres;
            pix.YRes = yres;
            pixa.AddPix(pix, L.L_INSERT);
        }
        return pixa;
    }

    // leptonica: pixaReadMem (pixabasic.c)
    /// <param name="data">of serialized pixa</param>
    public static Pixa ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
            Log.Error("pixaReadMem", "pixa not read");
            throw;
        }
    }

    // leptonica: pixaWriteDebug (pixabasic.c)
    /// <remarks>
    /// (1) Debug version, intended for use in the library when writing
    ///     to files in a temp directory with names that are compiled in.
    ///     This is used instead of pixaWrite() for all such library calls.
    /// (2) The global variable LeptDebugOK defaults to false, and can be set
    ///     or cleared by the function setLeptDebugOK().
    /// </remarks>
    public void WriteDebug(string fname)
    {
        if (WriteFile.LeptDebugOK) {
            Write(fname);
        } else {
            Log.Info("pixaWriteDebug", $"write to named temp file {fname} is disabled");
        }
    }

    // leptonica: pixaWrite (pixabasic.c)
    /// <remarks>(1) The pix are stored in the file as png.</remarks>
    public void Write(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, "wb");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("pixaWrite", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                WriteStream(fp);
            } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
                throw new ArgumentException($"Error in pixaWrite: pixa not written to stream: {filename}", e);
            }
        }
    }

    // leptonica: pixaWriteStream (pixabasic.c)
    /// <param name="fp">output stream</param>
    /// <remarks>(1) The pix are stored in the file as png.</remarks>
    public void WriteStream(Stream fp)
    {
        int n = Count;
        CStdio.Fprintf(fp, $"\nPixa Version {L.PIXA_VERSION_NUMBER}\n");
        CStdio.Fprintf(fp, $"Number of pix = {n}\n");
        Boxa.WriteStream(fp);
        for (int i = 0; i < n; i++) {
            Pix pix = GetPix(i, L.L_CLONE);
            CStdio.Fprintf(fp, $" pix[{i}]: xres = {pix.XRes}, yres = {pix.YRes}\n");
            Pix.WriteStreamPng(fp, pix, 0.0f);
        }
    }

    // leptonica: pixaWriteMem (pixabasic.c)
    /// <summary>Serializes a pixa in memory and returns the bytes.</summary>
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }

    // leptonica: pixaReadBoth (pixabasic.c)
    /// <remarks>
    /// (1) This reads serialized files of either a pixa or a pixacomp,
    ///     and returns a pixa in memory.  It requires png and jpeg libraries.
    /// </remarks>
    public static Pixa ReadBoth(string filename)
    {
        const string func = "pixaReadBoth";

        string sname = StringCode.GetStructStrFromFile(filename, L.L_STR_NAME)
            ?? throw Lept.Arg(func, "struct name not found");
        string buf = sname.Length > 31 ? sname.Substring(0, 31) : sname;  /* char buf[32] */

        Pixa pixa;
        if (buf == "Pixacomp") {
            Pixacomp pac = Pixacomp.Read(filename);
            pixa = CreateFromPixacomp(pac, L.L_COPY);
        } else if (buf == "Pixa") {
            pixa = Read(filename);
        } else {
            throw Lept.Arg(func, "invalid file type");
        }
        return pixa;
    }
}

/// <summary>Array of Pixa (struct Pixaa).</summary>
/// <remarks>
/// A Pixaa provides a 2-level hierarchy of images.  A common use is for
/// segmentation masks, which are inexpensive to store in png format.
/// </remarks>
public sealed partial class Pixaa
{
    /* Bounds on array sizes */
    private const int MaxInitPtrArraySize = 100000;
    private const int MaxPixaaPtrArraySize = 5000000;
    private const int InitialPtrArraySize = 20;      /* n'importe quoi */

    private Pixa?[] pixa;  /* array of ptrs to pixa; its length is nalloc */
    private int n;         /* number of Pixa in ptr array                 */

    /// <summary>Boxa for the support regions of the pixa (paa->boxa).</summary>
    public Boxa Boxa { get; internal set; }

    private Pixaa(int nalloc)
    {
        pixa = new Pixa?[nalloc];
        n = 0;
        Boxa = Boxa.Create(nalloc);
    }

    /// <summary>Number of allocated pixa ptrs (paa->nalloc).</summary>
    public int Nalloc => pixa.Length;

    /*---------------------------------------------------------------------*
     *                    Pixaa creation and destruction                   *
     *---------------------------------------------------------------------*/
    // leptonica: pixaaCreate (pixabasic.c)
    /// <param name="n">initial number of pixa ptrs</param>
    /// <remarks>
    /// (1) A pixaa provides a 2-level hierarchy of images.
    ///     A common use is for segmentation masks, which are
    ///     inexpensive to store in png format.
    /// (2) For example, suppose you want a mask for each textline
    ///     in a two-column page.  The textline masks for each column
    ///     can be represented by a pixa, of which there are 2 in the pixaa.
    ///     The boxes for the textline mask components within a column
    ///     can have their origin referred to the column rather than the page.
    ///     Then the boxa field can be used to represent the two box (regions)
    ///     for the columns, and the (x,y) components of each box can
    ///     be used to get the absolute position of the textlines on
    ///     the page.
    /// </remarks>
    public static Pixaa Create(int n)
    {
        if (n <= 0 || n > MaxInitPtrArraySize)
            n = InitialPtrArraySize;

        return new Pixaa(n);
    }

    // leptonica: pixaaCreateFromPixa (pixabasic.c)
    /// <param name="pixa"></param>
    /// <param name="n">number specifying subdivision of pixa</param>
    /// <param name="type">L_CHOOSE_CONSECUTIVE, L_CHOOSE_SKIP_BY</param>
    /// <param name="copyflag">L_CLONE, L_COPY</param>
    /// <remarks>
    /// (1) This subdivides a pixa into a set of smaller pixa that
    ///     are accumulated into a pixaa.
    /// (2) If type == L_CHOOSE_CONSECUTIVE, the first 'n' pix are
    ///     put in a pixa and added to pixaa, then the next 'n', etc.
    ///     If type == L_CHOOSE_SKIP_BY, the first pixa is made by
    ///     aggregating pix[0], pix[n], pix[2*n], etc.
    /// (3) The copyflag specifies if each new pix is a copy or a clone.
    /// </remarks>
    public static Pixaa CreateFromPixa(Pixa pixa, int n, int type, int copyflag)
    {
        const string func = "pixaaCreateFromPixa";
        int count = pixa.Count;
        if (count == 0)
            throw Lept.Arg(func, "no pix in pixa");
        if (n <= 0)
            throw Lept.Arg(func, "n must be > 0");
        if (type != L.L_CHOOSE_CONSECUTIVE && type != L.L_CHOOSE_SKIP_BY)
            throw Lept.Arg(func, "invalid type");
        if (copyflag != L.L_CLONE && copyflag != L.L_COPY)
            throw Lept.Arg(func, "invalid copyflag");

        int npixa;
        if (type == L.L_CHOOSE_CONSECUTIVE)
            npixa = (count + n - 1) / n;
        else  /* L_CHOOSE_SKIP_BY */
            npixa = Math.Min(n, count);
        Pixaa paa = Create(npixa);
        Pixa? pixat = null;
        if (type == L.L_CHOOSE_CONSECUTIVE) {
            int i;
            for (i = 0; i < count; i++) {
                if (i % n == 0)
                    pixat = Pixa.Create(n);
                Pix pix = pixa.GetPix(i, copyflag);
                pixat!.AddPix(pix, L.L_INSERT);
                if (i % n == n - 1)
                    paa.AddPixa(pixat, L.L_INSERT);
            }
            if (i % n != 0)
                paa.AddPixa(pixat!, L.L_INSERT);
        } else {  /* L_CHOOSE_SKIP_BY */
            for (int i = 0; i < npixa; i++) {
                pixat = Pixa.Create(count / npixa + 1);
                for (int j = i; j < count; j += n) {
                    Pix pix = pixa.GetPix(j, copyflag);
                    pixat.AddPix(pix, L.L_INSERT);
                }
                paa.AddPixa(pixat, L.L_INSERT);
            }
        }

        return paa;
    }

    /*---------------------------------------------------------------------*
     *                             Pixaa addition                          *
     *---------------------------------------------------------------------*/
    // leptonica: pixaaAddPixa (pixabasic.c)
    /// <param name="pixa">to be added</param>
    /// <param name="copyflag">
    ///   L_INSERT inserts the pixa directly;
    ///   L_COPY makes a new pixa and copies each pix and each box;
    ///   L_CLONE gives the same pixa;
    ///   L_COPY_CLONE makes a new pixa and inserts clones of all pix and boxes</param>
    public void AddPixa(Pixa pixa, int copyflag)
    {
        if (copyflag != L.L_INSERT && copyflag != L.L_COPY &&
            copyflag != L.L_CLONE && copyflag != L.L_COPY_CLONE)
            throw Lept.Arg("pixaaAddPixa", "invalid copyflag");

        Pixa pixac;
        if (copyflag == L.L_INSERT)
            pixac = pixa;
        else
            pixac = pixa.Copy(copyflag);

        int n = Count;
        if (n >= Nalloc)
            ExtendArray();
        this.pixa[n] = pixac;
        this.n++;
    }

    // leptonica: pixaaExtendArray (pixabasic.c)
    /// <remarks>
    /// (1) The max number of pixa ptrs is 5M.  The reason it is so large
    ///     is that some applications, like jbig2enc, can create a very
    ///     large array of Pixa, each representing a character class
    ///     that contains one or a few tiny bitmaps.
    /// </remarks>
    private void ExtendArray()
    {
        if (Nalloc > MaxPixaaPtrArraySize)  /* belt & suspenders */
            throw Lept.Fail("pixaaExtendArray", "paa has too many ptrs");
        long oldsize = (long)Nalloc * 8;  /* sizeof(PIXA *) */
        long newsize = 2 * oldsize;
        if (newsize > 8L * MaxPixaaPtrArraySize) {
            Log.Error("pixaaExtendArray", $"newsize = {newsize} > 40 MB; too large");
            throw Lept.Fail("pixaaExtendArray", "array too large");
        }

        Array.Resize(ref pixa, 2 * Nalloc);
    }

    // leptonica: pixaaAddPix (pixabasic.c)
    /// <param name="index">index of pixa in paa</param>
    /// <param name="pix">to be added</param>
    /// <param name="box">[optional] to be added</param>
    /// <param name="copyflag">L_INSERT, L_COPY, L_CLONE</param>
    public void AddPix(int index, Pix pix, Box? box, int copyflag)
    {
        Pixa pixa = GetPixa(index, L.L_CLONE);
        pixa.AddPix(pix, copyflag);
        if (box != null) pixa.AddBox(box, copyflag);
    }

    // leptonica: pixaaAddBox (pixabasic.c)
    /// <param name="box"></param>
    /// <param name="copyflag">L_INSERT, L_COPY, L_CLONE</param>
    /// <remarks>
    /// (1) The box can be used, for example, to hold the support region
    ///     of a pixa that is being added to the pixaa.
    /// </remarks>
    public void AddBox(Box box, int copyflag)
    {
        if (copyflag != L.L_INSERT && copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg("pixaaAddBox", "invalid copyflag");

        Boxa.AddBox(box, copyflag);
    }

    /*---------------------------------------------------------------------*
     *                            Pixaa accessors                          *
     *---------------------------------------------------------------------*/
    /// <summary>Number of pixa in the array (pixaaGetCount() without the numa).</summary>
    public int Count => n;

    // leptonica: pixaaGetCount (pixabasic.c)
    /// <param name="pna">number of pix in each pixa</param>
    /// <returns>count of pixa</returns>
    /// <remarks>(1) If paa is empty, the returned na will also be empty.</remarks>
    public int GetCount(out Numa pna)
    {
        int n = Count;
        Numa na = Numa.Create(n);
        pna = na;
        for (int i = 0; i < n; i++) {
            Pixa pixa = GetPixa(i, L.L_CLONE);
            na.AddNumber(pixa.Count);
        }
        return n;
    }

    // leptonica: pixaaGetPixa (pixabasic.c)
    /// <param name="index">to the index-th pixa</param>
    /// <param name="accesstype">L_COPY, L_CLONE, L_COPY_CLONE</param>
    /// <remarks>
    /// (1) L_COPY makes a new pixa with a copy of every pix
    /// (2) L_CLONE just returns the same pixa.  You would use this, for example,
    ///     when you need to extract some data from a pix within a
    ///     pixa within a pixaa.
    /// (3) L_COPY_CLONE makes a new pixa with a clone of every pix
    ///     and box
    /// </remarks>
    public Pixa GetPixa(int index, int accesstype)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("pixaaGetPixa", "index not valid");
        if (accesstype != L.L_COPY && accesstype != L.L_CLONE && accesstype != L.L_COPY_CLONE)
            throw Lept.Arg("pixaaGetPixa", "invalid accesstype");

        Pixa? pixa = this.pixa[index];
        if (pixa == null) {  /* shouldn't happen! */
            Log.Error("pixaaGetPixa", $"missing pixa[{index}]");
            throw Lept.Arg("pixaaGetPixa", "pixa not found at index");
        }
        return pixa.Copy(accesstype);
    }

    // leptonica: pixaaGetBoxa (pixabasic.c)
    /// <param name="accesstype">L_COPY, L_CLONE</param>
    /// <remarks>(1) L_COPY returns a copy; L_CLONE returns the same boxa.</remarks>
    public Boxa GetBoxa(int accesstype)
    {
        if (accesstype != L.L_COPY && accesstype != L.L_CLONE)
            throw Lept.Arg("pixaaGetBoxa", "invalid access type");

        return Boxa.Copy(accesstype);
    }

    // leptonica: pixaaGetPix (pixabasic.c)
    /// <param name="index">index into the pixa array in the pixaa</param>
    /// <param name="ipix">index into the pix array in the pixa</param>
    /// <param name="accessflag">L_COPY or L_CLONE</param>
    public Pix GetPix(int index, int ipix, int accessflag)
    {
        Pixa pixa;
        try {
            pixa = GetPixa(index, L.L_CLONE);
        } catch (ArgumentException e) {
            throw new ArgumentException("Error in pixaaGetPix: pixa not retrieved", e);
        }
        try {
            return pixa.GetPix(ipix, accessflag);
        } catch (ArgumentException e) {
            Log.Error("pixaaGetPix", "pix not retrieved");
            throw new ArgumentException("Error in pixaaGetPix: pix not retrieved", e);
        }
    }

    // leptonica: pixaaVerifyDepth (pixabasic.c)
    /// <param name="same">true if all pix have the same depth</param>
    /// <param name="maxd">max depth of all pix in pixaa</param>
    /// <remarks>
    /// (1) It is considered to be an error if any pixa have no pix.
    /// </remarks>
    public void VerifyDepth(out bool same, out int maxd)
    {
        same = false;
        maxd = 0;
        int n = Count;
        if (n == 0)
            throw Lept.Arg("pixaaVerifyDepth", "no pixa in paa");

        /* NOTE(upstream): the C code ignores the error return of pixaVerifyDepth() for a pixa
         * without pix and then uses uninitialized values; here it throws. */
        Pixa pixa = GetPixa(0, L.L_CLONE);
        pixa.VerifyDepth(out bool same1, out int maxd1);  /* init same, maxd with first pixa */
        for (int i = 1; i < n; i++) {
            pixa = GetPixa(i, L.L_CLONE);
            pixa.VerifyDepth(out bool samed, out int d);
            maxd1 = Math.Max(maxd1, d);
            if (!samed || maxd1 != d)
                same1 = false;
        }
        same = same1;
        maxd = maxd1;
    }

    // leptonica: pixaaVerifyDimensions (pixabasic.c)
    /// <param name="same">true if all pix have the same dimensions</param>
    /// <param name="maxw">max width of all pix in pixaa</param>
    /// <param name="maxh">max height of all pix in pixaa</param>
    /// <remarks>
    /// (1) It is considered to be an error if any pixa have no pix.
    /// </remarks>
    public void VerifyDimensions(out bool same, out int maxw, out int maxh)
    {
        same = false;
        maxw = 0;
        maxh = 0;
        int n = Count;
        if (n == 0)
            throw Lept.Arg("pixaaVerifyDimensions", "no pixa in paa");

            /* Init same; init maxw and maxh from first pixa */
        Pixa pixa = GetPixa(0, L.L_CLONE);
        pixa.VerifyDimensions(out bool same1, out int maxw1, out int maxh1);

        for (int i = 1; i < n; i++) {
            pixa = GetPixa(i, L.L_CLONE);
            pixa.VerifyDimensions(out bool same2, out int w, out int h);
            maxw1 = Math.Max(maxw1, w);
            maxh1 = Math.Max(maxh1, h);
            if (!same2 || maxw1 != w || maxh1 != h)
                same1 = false;
        }
        same = same1;
        maxw = maxw1;
        maxh = maxh1;
    }

    // leptonica: pixaaIsFull (pixabasic.c)
    /// <returns>true if all pixa in the paa have full pix arrays</returns>
    /// <remarks>(1) Does not require boxa associated with each pixa to be full.</remarks>
    public bool IsFull()
    {
        int n = Count;
        bool full = true;
        for (int i = 0; i < n; i++) {
            Pixa pixa = GetPixa(i, L.L_CLONE);
            pixa.IsFull(out full, out _);
            if (!full) break;
        }
        return full;
    }

    /*---------------------------------------------------------------------*
     *                         Pixaa array modifiers                       *
     *---------------------------------------------------------------------*/
    // leptonica: pixaaInitFull (pixabasic.c)
    /// <param name="pixa">to be replicated into the entire pixa ptr array</param>
    /// <remarks>
    /// (1) This initializes a pixaa by filling up the entire pixa ptr array
    ///     with copies of %pixa.  Any existing pixa are dropped.
    /// (2) Example usage.  This function is useful to prepare for a
    ///     random insertion (or replacement) of pixa into a pixaa.
    ///     To randomly insert pixa into a pixaa, up to some index "max":
    ///        Pixaa paa = Pixaa.Create(max);
    ///        Pixa pixa = Pixa.Create(1);  // if you want little memory
    ///        paa.InitFull(pixa);  // copy it to entire array
    ///     The initialization allows the pixaa to always be properly filled.
    /// </remarks>
    public void InitFull(Pixa pixa)
    {
        int n = Nalloc;
        this.n = n;
        for (int i = 0; i < n; i++) {
            Pixa pixat = pixa.Copy(L.L_COPY);
            ReplacePixa(i, pixat);
        }
    }

    // leptonica: pixaaReplacePixa (pixabasic.c)
    /// <param name="index">to the index-th pixa</param>
    /// <param name="pixa">insert to replace existing one</param>
    /// <remarks>
    /// (1) This allows random insertion of a pixa into a pixaa, with
    ///     dropping of any existing pixa at that location.
    ///     The input pixa is now owned by the pixaa.
    /// (2) No other pixa in the array are affected.
    /// (3) The index must be within the allowed set.
    /// </remarks>
    public void ReplacePixa(int index, Pixa pixa)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("pixaaReplacePixa", "index not valid");

        this.pixa[index] = pixa;
    }

    // leptonica: pixaaClear (pixabasic.c)
    /// <remarks>
    /// (1) This drops all pixa in the pixaa, and nulls the ptrs
    ///     in the pixa ptr array.
    /// </remarks>
    public void Clear()
    {
        int n = Count;
        for (int i = 0; i < n; i++)
            pixa[i] = null;
        this.n = 0;
    }

    // leptonica: pixaaTruncate (pixabasic.c)
    /// <remarks>
    /// (1) This identifies the largest index containing a pixa that
    ///     has any pix within it, drops all pixa above that index,
    ///     and resets the count.
    /// </remarks>
    public void Truncate()
    {
        int n = Count;
        for (int i = n - 1; i >= 0; i--) {
            Pixa pixa;
            try {
                pixa = GetPixa(i, L.L_CLONE);
            } catch (ArgumentException) {
                this.n--;
                continue;
            }
            int np = pixa.Count;
            if (np == 0) {
                this.pixa[i] = null;
                this.n--;
            } else {
                break;
            }
        }
    }

    /*---------------------------------------------------------------------*
     *                         Pixaa serialized I/O                        *
     *---------------------------------------------------------------------*/
    // leptonica: pixaaReadFromFiles (pixabasic.c)
    /// <param name="dirname">directory</param>
    /// <param name="substr">[optional] substring filter on filenames; can be null</param>
    /// <param name="first">0-based</param>
    /// <param name="nfiles">use 0 for everything from %first to the end</param>
    /// <remarks>
    /// (1) The files must be serialized pixa files (e.g., *.pa)
    ///     If some files cannot be read, warnings are issued.
    /// (2) Use %substr to filter filenames in the directory.  If
    ///     %substr == null, this takes all files.
    /// (3) After filtering, use %first and %nfiles to select
    ///     a contiguous set of files, that have been lexically
    ///     sorted in increasing order.
    /// </remarks>
    public static Pixaa ReadFromFiles(string dirname, string? substr, int first, int nfiles)
    {
        const string func = "pixaaReadFromFiles";

        Sarray sa = Sarray.GetSortedPathnamesInDirectory(dirname, substr, first, nfiles);
        int n = sa.Count;
        if (n == 0)
            throw Lept.Arg(func, "no pixa files found");

        Pixaa paa = Create(n);
        for (int i = 0; i < n; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            Pixa pixa;
            try {
                pixa = Pixa.Read(fname);
            } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
                Log.Error(func, $"pixa not read for {i}-th file");
                continue;
            }
            paa.AddPixa(pixa, L.L_INSERT);
        }

        return paa;
    }

    // leptonica: pixaaRead (pixabasic.c)
    /// <remarks>(1) The pix are stored in the file as png.</remarks>
    public static Pixaa Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("pixaaRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
                throw new ArgumentException($"Error in pixaaRead: paa not read: {filename}", e);
            }
        }
    }

    // leptonica: pixaaReadStream (pixabasic.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// (1) The pix are stored in the file as png.
    /// (2) It is OK for the pixaa to be empty.
    /// (3) A stream that cannot seek is read completely into memory first.
    /// </remarks>
    public static Pixaa ReadStream(Stream fp)
    {
        const string func = "pixaaReadStream";
        Span<int> v = stackalloc int[1];

        fp = Pixa.Seekable(fp);
        CScanner sc = new CScanner(fp);
        try {
            if (sc.Scan("\nPixaa Version %d\n", v) != 1)
                throw Lept.Arg(func, "not a pixaa file");
            int version = v[0];
            if (version != L.PIXAA_VERSION_NUMBER)
                throw Lept.Arg(func, "invalid pixaa version");
            if (sc.Scan("Number of pixa = %d\n", v) != 1)
                throw Lept.Arg(func, "not a pixaa file");
            int n = v[0];
            if (n < 0)
                throw Lept.Arg(func, "num pixa ptrs < 0");
            if (n > MaxPixaaPtrArraySize)
                throw Lept.Arg(func, "too many pixa ptrs");
            if (n == 0) Log.Info(func, "the pixaa is empty");

            Pixaa paa = Create(n);
            paa.Boxa = Boxa.ReadStream(sc);

            for (int i = 0; i < n; i++) {
                if (sc.Scan("\n\n --------------- pixa[%d] ---------------\n", v) != 1)
                    throw Lept.Arg(func, "text reading");
                Pixa pixa = Pixa.ReadStream(fp, sc);
                paa.AddPixa(pixa, L.L_INSERT);
            }

            return paa;
        } finally {
            sc.Release();
        }
    }

    // leptonica: pixaaReadMem (pixabasic.c)
    /// <param name="data">of serialized pixaa</param>
    public static Pixaa ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
            Log.Error("pixaaReadMem", "paa not read");
            throw;
        }
    }

    // leptonica: pixaaWrite (pixabasic.c)
    /// <remarks>(1) The pix are stored in the file as png.</remarks>
    public void Write(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, "wb");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("pixaaWrite", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                WriteStream(fp);
            } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
                throw new ArgumentException($"Error in pixaaWrite: paa not written to stream: {filename}", e);
            }
        }
    }

    // leptonica: pixaaWriteStream (pixabasic.c)
    /// <param name="fp">output stream</param>
    /// <remarks>(1) The pix are stored in the file as png.</remarks>
    public void WriteStream(Stream fp)
    {
        int n = Count;
        CStdio.Fprintf(fp, $"\nPixaa Version {L.PIXAA_VERSION_NUMBER}\n");
        CStdio.Fprintf(fp, $"Number of pixa = {n}\n");
        Boxa.WriteStream(fp);
        for (int i = 0; i < n; i++) {
            Pixa pixa = GetPixa(i, L.L_CLONE);
            CStdio.Fprintf(fp, $"\n\n --------------- pixa[{i}] ---------------\n");
            pixa.WriteStream(fp);
        }
    }

    // leptonica: pixaaWriteMem (pixabasic.c)
    /// <summary>Serializes a pixaa in memory and returns the bytes.</summary>
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }
}

/// <summary>The pixaa-level combination, which needs the Pixaa counterpart of <see cref="Pixa.Join"/>.</summary>
public sealed partial class Pixaa
{
    // leptonica: pixaaJoin (pixabasic.c)
    /// <param name="paas">[optional] source pixaa; add from this one</param>
    /// <param name="istart">starting index in paas</param>
    /// <param name="iend">ending index in paas; use -1 to cat all</param>
    /// <remarks>
    /// (1) This appends a clone of each indicated pixa in paas to this pixaa
    /// (2) istart &lt; 0 is taken to mean 'read from the start' (istart = 0)
    /// (3) iend &lt; 0 means 'read to the end'
    /// </remarks>
    public void Join(Pixaa? paas, int istart, int iend)
    {
        Pixaa paad = this;
        if (paas == null)
            return;

        if (istart < 0)
            istart = 0;
        int n = paas.Count;
        if (iend < 0 || iend >= n)
            iend = n - 1;
        if (istart > iend)
            throw Lept.Arg("pixaaJoin", "istart > iend; nothing to add");

        for (int i = istart; i <= iend; i++) {
            Pixa pixa = paas.GetPixa(i, L.L_CLONE);
            paad.AddPixa(pixa, L.L_INSERT);
        }
    }
}
