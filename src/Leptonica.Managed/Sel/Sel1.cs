using System;
using System.IO;
using System.Text;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  sel1.c: Basic ops on Sels and Selas.
 *
 *  Usage notes:
 *    In this file we have seven functions that make sels:
 *      (1)  Sel.Create(), with input (h, w, [name])
 *           The generic function.  Roll your own, using SetElement().
 *      (2)  Sel.CreateBrick(), with input (h, w, cy, cx, val)
 *           The most popular function.  Makes a rectangular sel of
 *           all hits, misses or don't-cares.  We have many morphology
 *           operations that create a sel of all hits, use it, and
 *           destroy it.
 *      (3)  Sel.CreateFromString() with input (text, h, w, [name])
 *           Adam Langley's clever function, allows you to make a hit-miss
 *           sel from a string in code that is geometrically laid out
 *           just like the actual sel.
 *      (4)  Sela.CreateFromFile() with input (filename)
 *           This parses a simple file format to create an array of
 *           hit-miss sels.  The sel data uses the same encoding
 *           as in (3), with geometrical layout enforced.
 *      (5)  Sel.CreateFromPta() with input (pta, cy, cx, [name])
 *           Another way to make a sel with only hits.
 *      (6)  Sel.CreateFromPix() with input (pix, cy, cx, [name])
 *           Yet another way to make a sel from hits.
 *      (7)  Sel.CreateFromColorPix() with input (pix, name).
 *           Another way to make a general hit-miss sel, starting with
 *           an image editor.
 *    In addition, there are three functions in selgen.c that
 *    automatically generate a hit-miss sel from a pix and
 *    a number of parameters.  This is useful for problems like
 *    "find all patterns that look like this one."
 *
 *    Consistency, being the hobgoblin of small minds,
 *    is adhered to here in the dimensioning and accessing of sels.
 *    Everything is done in standard matrix (row, column) order.
 *    When we set specific elements in a sel, we likewise use
 *    (row, col) ordering:
 *         SetElement(), with input (row, col, type)
 *
 *  Differences from C:
 *    - No reference counting and no selDestroy() and selaDestroy():
 *      the GC owns the objects (not ported).
 *    - FILE* becomes Stream.  Text is written as UTF-8 with "\n" line ends,
 *      byte for byte as the C fprintf calls write it.  Reading follows the
 *      fscanf / fgets calls of the C code.
 *    - Functions that return an error code or NULL in C throw instead,
 *      except where the C code logs and carries on (noted at the place).
 *    - stringNew() copies are not needed: .NET strings are immutable.
 */

/// <summary>Constants from morph.h.</summary>
public static partial class L
{
    /// <summary>Version number of the serialized structuring element format.</summary>
    public const int SEL_VERSION_NUMBER = 1;

    /* Sel element types (enum in morph.h) */
    /// <summary>Sel element that is ignored by the operation.</summary>
    public const int SEL_DONT_CARE = 0;
    /// <summary>Sel element that must match a foreground pixel.</summary>
    public const int SEL_HIT = 1;
    /// <summary>Sel element that must match a background pixel.</summary>
    public const int SEL_MISS = 2;
}

/// <summary>
/// Structuring element for morphological operations (struct Sel).
/// </summary>
public sealed partial class Sel
{
    /* Bounds on kernel size */
    private const int MaxKernelSize = 10000;

    /* Bounds on pix template size */
    private const uint MaxPixTemplateSize = 300;
    private const uint MaxPixTemplateHits = 3000;

    /* TODO(pix3.c): replace by L.L_HORIZ / L.L_VERT once those constants are in the base */
    internal const int L_HORIZ = 1;
    internal const int L_VERT = 2;

    private readonly record struct CompParameterMap(int Size, int Size1, int Size2,
        string Selnameh1, string Selnameh2, string Selnamev1, string Selnamev2);

    private static readonly CompParameterMap[] comp_parameter_map =
    {
        new(2, 2, 1, "sel_2h", "", "sel_2v", ""),
        new(3, 3, 1, "sel_3h", "", "sel_3v", ""),
        new(4, 2, 2, "sel_2h", "sel_comb_4h", "sel_2v", "sel_comb_4v"),
        new(5, 5, 1, "sel_5h", "", "sel_5v", ""),
        new(6, 3, 2, "sel_3h", "sel_comb_6h", "sel_3v", "sel_comb_6v"),
        new(7, 7, 1, "sel_7h", "", "sel_7v", ""),
        new(8, 4, 2, "sel_4h", "sel_comb_8h", "sel_4v", "sel_comb_8v"),
        new(9, 3, 3, "sel_3h", "sel_comb_9h", "sel_3v", "sel_comb_9v"),
        new(10, 5, 2, "sel_5h", "sel_comb_10h", "sel_5v", "sel_comb_10v"),
        new(11, 4, 3, "sel_4h", "sel_comb_12h", "sel_4v", "sel_comb_12v"),
        new(12, 4, 3, "sel_4h", "sel_comb_12h", "sel_4v", "sel_comb_12v"),
        new(13, 4, 3, "sel_4h", "sel_comb_12h", "sel_4v", "sel_comb_12v"),
        new(14, 7, 2, "sel_7h", "sel_comb_14h", "sel_7v", "sel_comb_14v"),
        new(15, 5, 3, "sel_5h", "sel_comb_15h", "sel_5v", "sel_comb_15v"),
        new(16, 4, 4, "sel_4h", "sel_comb_16h", "sel_4v", "sel_comb_16v"),
        new(17, 4, 4, "sel_4h", "sel_comb_16h", "sel_4v", "sel_comb_16v"),
        new(18, 6, 3, "sel_6h", "sel_comb_18h", "sel_6v", "sel_comb_18v"),
        new(19, 5, 4, "sel_5h", "sel_comb_20h", "sel_5v", "sel_comb_20v"),
        new(20, 5, 4, "sel_5h", "sel_comb_20h", "sel_5v", "sel_comb_20v"),
        new(21, 7, 3, "sel_7h", "sel_comb_21h", "sel_7v", "sel_comb_21v"),
        new(22, 11, 2, "sel_11h", "sel_comb_22h", "sel_11v", "sel_comb_22v"),
        new(23, 6, 4, "sel_6h", "sel_comb_24h", "sel_6v", "sel_comb_24v"),
        new(24, 6, 4, "sel_6h", "sel_comb_24h", "sel_6v", "sel_comb_24v"),
        new(25, 5, 5, "sel_5h", "sel_comb_25h", "sel_5v", "sel_comb_25v"),
        new(26, 5, 5, "sel_5h", "sel_comb_25h", "sel_5v", "sel_comb_25v"),
        new(27, 9, 3, "sel_9h", "sel_comb_27h", "sel_9v", "sel_comb_27v"),
        new(28, 7, 4, "sel_7h", "sel_comb_28h", "sel_7v", "sel_comb_28v"),
        new(29, 6, 5, "sel_6h", "sel_comb_30h", "sel_6v", "sel_comb_30v"),
        new(30, 6, 5, "sel_6h", "sel_comb_30h", "sel_6v", "sel_comb_30v"),
        new(31, 6, 5, "sel_6h", "sel_comb_30h", "sel_6v", "sel_comb_30v"),
        new(32, 8, 4, "sel_8h", "sel_comb_32h", "sel_8v", "sel_comb_32v"),
        new(33, 11, 3, "sel_11h", "sel_comb_33h", "sel_11v", "sel_comb_33v"),
        new(34, 7, 5, "sel_7h", "sel_comb_35h", "sel_7v", "sel_comb_35v"),
        new(35, 7, 5, "sel_7h", "sel_comb_35h", "sel_7v", "sel_comb_35v"),
        new(36, 6, 6, "sel_6h", "sel_comb_36h", "sel_6v", "sel_comb_36v"),
        new(37, 6, 6, "sel_6h", "sel_comb_36h", "sel_6v", "sel_comb_36v"),
        new(38, 6, 6, "sel_6h", "sel_comb_36h", "sel_6v", "sel_comb_36v"),
        new(39, 13, 3, "sel_13h", "sel_comb_39h", "sel_13v", "sel_comb_39v"),
        new(40, 8, 5, "sel_8h", "sel_comb_40h", "sel_8v", "sel_comb_40v"),
        new(41, 7, 6, "sel_7h", "sel_comb_42h", "sel_7v", "sel_comb_42v"),
        new(42, 7, 6, "sel_7h", "sel_comb_42h", "sel_7v", "sel_comb_42v"),
        new(43, 7, 6, "sel_7h", "sel_comb_42h", "sel_7v", "sel_comb_42v"),
        new(44, 11, 4, "sel_11h", "sel_comb_44h", "sel_11v", "sel_comb_44v"),
        new(45, 9, 5, "sel_9h", "sel_comb_45h", "sel_9v", "sel_comb_45v"),
        new(46, 9, 5, "sel_9h", "sel_comb_45h", "sel_9v", "sel_comb_45v"),
        new(47, 8, 6, "sel_8h", "sel_comb_48h", "sel_8v", "sel_comb_48v"),
        new(48, 8, 6, "sel_8h", "sel_comb_48h", "sel_8v", "sel_comb_48v"),
        new(49, 7, 7, "sel_7h", "sel_comb_49h", "sel_7v", "sel_comb_49v"),
        new(50, 10, 5, "sel_10h", "sel_comb_50h", "sel_10v", "sel_comb_50v"),
        new(51, 10, 5, "sel_10h", "sel_comb_50h", "sel_10v", "sel_comb_50v"),
        new(52, 13, 4, "sel_13h", "sel_comb_52h", "sel_13v", "sel_comb_52v"),
        new(53, 9, 6, "sel_9h", "sel_comb_54h", "sel_9v", "sel_comb_54v"),
        new(54, 9, 6, "sel_9h", "sel_comb_54h", "sel_9v", "sel_comb_54v"),
        new(55, 11, 5, "sel_11h", "sel_comb_55h", "sel_11v", "sel_comb_55v"),
        new(56, 8, 7, "sel_8h", "sel_comb_56h", "sel_8v", "sel_comb_56v"),
        new(57, 8, 7, "sel_8h", "sel_comb_56h", "sel_8v", "sel_comb_56v"),
        new(58, 8, 7, "sel_8h", "sel_comb_56h", "sel_8v", "sel_comb_56v"),
        new(59, 10, 6, "sel_10h", "sel_comb_60h", "sel_10v", "sel_comb_60v"),
        new(60, 10, 6, "sel_10h", "sel_comb_60h", "sel_10v", "sel_comb_60v"),
        new(61, 10, 6, "sel_10h", "sel_comb_60h", "sel_10v", "sel_comb_60v"),
        new(62, 9, 7, "sel_9h", "sel_comb_63h", "sel_9v", "sel_comb_63v"),
        new(63, 9, 7, "sel_9h", "sel_comb_63h", "sel_9v", "sel_comb_63v"),
    };

    private string? name;
    private readonly int[][] data;

    /// <summary>Sel height (sel->sy).</summary>
    public int Sy { get; private set; }
    /// <summary>Sel width (sel->sx).</summary>
    public int Sx { get; private set; }
    /// <summary>Row of the origin (sel->cy).</summary>
    public int Cy { get; private set; }
    /// <summary>Column of the origin (sel->cx).</summary>
    public int Cx { get; private set; }
    /// <summary>Elements, indexed [row][col] (sel->data).</summary>
    public int[][] Data => data;

    private Sel(int sy, int sx, int[][] data)
    {
        Sy = sy;
        Sx = sx;
        this.data = data;
    }

    /*------------------------------------------------------------------------*
     *                      Create / Destroy / Copy                           *
     *------------------------------------------------------------------------*/
    // leptonica: selCreate (sel1.c)
    /// <summary>Creates a sel with all elements set to SEL_DONT_CARE.</summary>
    /// <param name="height">sel height</param>
    /// <param name="width">sel width</param>
    /// <param name="name">[optional] sel name; can be null</param>
    /// <remarks>
    /// (1) selCreate() initializes all values to 0.
    /// (2) After this call, (cy,cx) and nonzero data values must be
    ///     assigned.  If a text name is not assigned here, it will
    ///     be needed later when the sel is put into a sela.
    /// </remarks>
    public static Sel Create(int height, int width, string? name = null)
    {
        int[][] data;
        try {
            data = Create2dIntArray(height, width);
        } catch (ArgumentException e) {
            throw new ArgumentException("Error in selCreate: data not allocated", e);
        }

        Sel sel = new Sel(height, width, data);
        if (name != null)
            sel.name = name;
        return sel;
    }

    // leptonica: selCopy (sel1.c)
    /// <summary>Returns a copy of the sel.</summary>
    public Sel Copy()
    {
        GetParameters(out int sy, out int sx, out int cy, out int cx);
        int[][] cdata;
        try {
            cdata = Create2dIntArray(sy, sx);
        } catch (ArgumentException e) {
            throw new ArgumentException("Error in selCopy: sel data not made", e);
        }
        Sel csel = new Sel(sy, sx, cdata);
        csel.Cy = cy;
        csel.Cx = cx;

        for (int i = 0; i < sy; i++)
            for (int j = 0; j < sx; j++)
                csel.data[i][j] = data[i][j];

        if (name != null)
            csel.name = name;

        return csel;
    }

    // leptonica: selCreateBrick (sel1.c)
    /// <summary>Creates a rectangular sel of all hits, misses or don't cares.</summary>
    /// <param name="h">height</param>
    /// <param name="w">width</param>
    /// <param name="cy">origin row, relative to UL corner at 0,0</param>
    /// <param name="cx">origin column, relative to UL corner at 0,0</param>
    /// <param name="type">SEL_HIT, SEL_MISS, or SEL_DONT_CARE</param>
    /// <remarks>
    /// (1) This is a rectangular sel of all hits, misses or don't cares.
    /// </remarks>
    public static Sel CreateBrick(int h, int w, int cy, int cx, int type)
    {
        if (h <= 0 || w <= 0)
            throw Lept.Arg("selCreateBrick", "h and w must both be > 0");
        if (type != L.SEL_HIT && type != L.SEL_MISS && type != L.SEL_DONT_CARE)
            throw Lept.Arg("selCreateBrick", "invalid sel element type");

        Sel sel;
        try {
            sel = Create(h, w, null);
        } catch (ArgumentException e) {
            throw new ArgumentException("Error in selCreateBrick: sel not made", e);
        }
        sel.SetOrigin(cy, cx);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                sel.data[i][j] = type;

        return sel;
    }

    // leptonica: selCreateComb (sel1.c)
    /// <summary>Creates a comb sel of hits.</summary>
    /// <param name="factor1">contiguous space between comb tines</param>
    /// <param name="factor2">number of comb tines</param>
    /// <param name="direction">L_HORIZ, L_VERT</param>
    /// <remarks>
    /// (1) This generates a comb Sel of hits with the origin as
    ///     near the center as possible.
    /// (2) In use, this is complemented by a brick sel of size %factor1,
    ///     Both brick and comb sels are made by selectComposableSels().
    /// </remarks>
    public static Sel CreateComb(int factor1, int factor2, int direction)
    {
        if (factor1 < 1 || factor2 < 1)
            throw Lept.Arg("selCreateComb", "factors must be >= 1");
        if (direction != L_HORIZ && direction != L_VERT)
            throw Lept.Arg("selCreateComb", "invalid direction");

        Sel sel;
        int size = factor1 * factor2;
        if (direction == L_HORIZ) {
            try {
                sel = Create(1, size, null);
            } catch (ArgumentException e) {
                throw new ArgumentException("Error in selCreateComb: horiz sel not made", e);
            }
            sel.SetOrigin(0, size / 2);
        } else {
            try {
                sel = Create(size, 1, null);
            } catch (ArgumentException e) {
                throw new ArgumentException("Error in selCreateComb: vert sel not made", e);
            }
            sel.SetOrigin(size / 2, 0);
        }

            /* Lay down the elements of the comb */
        for (int i = 0; i < factor2; i++) {
            int z = factor1 / 2 + i * factor1;
            if (direction == L_HORIZ)
                sel.SetElement(0, z, L.SEL_HIT);
            else
                sel.SetElement(z, 0, L.SEL_HIT);
        }

        return sel;
    }

    // leptonica: create2dIntArray (sel1.c)
    /// <summary>Makes a doubly indexed array of ints, indexed [row][col].</summary>
    /// <param name="sy">rows == height</param>
    /// <param name="sx">columns == width</param>
    /// <returns>an array of sy row arrays, each of which has sx ints</returns>
    /// <remarks>
    /// (1) The array[sy][sx] is indexed in standard "matrix notation",
    ///     with the row index first.
    /// </remarks>
    public static int[][] Create2dIntArray(int sy, int sx)
    {
        if (sx <= 0 || sx > MaxKernelSize)
            throw Lept.Arg("create2dIntArray", "sx out of bounds");
        if (sy <= 0 || sy > MaxKernelSize)
            throw Lept.Arg("create2dIntArray", "sy out of bounds");

        int[][] array = new int[sy][];
        for (int i = 0; i < sy; i++)
            array[i] = new int[sx];
        return array;
    }

    /*----------------------------------------------------------------------*
     *                               Accessors                              *
     *----------------------------------------------------------------------*/
    // leptonica: selGetName, selSetName (sel1.c)
    /// <summary>The sel name; null if it has none.</summary>
    /// <remarks>
    /// (1) Setting always replaces the existing sel name, if defined.
    /// (2) Setting null just clears any existing sel name.
    /// </remarks>
    public string? Name
    {
        get => name;
        set => name = value;
    }

    // leptonica: selGetElement (sel1.c)
    /// <returns>SEL_HIT, SEL_MISS, SEL_DONT_CARE</returns>
    public int GetElement(int row, int col)
    {
        if (row < 0 || row >= Sy)
            throw Lept.Arg("selGetElement", "sel row out of bounds");
        if (col < 0 || col >= Sx)
            throw Lept.Arg("selGetElement", "sel col out of bounds");

        return data[row][col];
    }

    // leptonica: selSetElement (sel1.c)
    /// <summary>Sets the type of a single element of the Sel.</summary>
    /// <param name="row">Row of the element; must be in [0, <see cref="Sy"/>).</param>
    /// <param name="col">Column of the element; must be in [0, <see cref="Sx"/>).</param>
    /// <param name="type">One of <see cref="L.SEL_HIT"/>, <see cref="L.SEL_MISS"/> or <see cref="L.SEL_DONT_CARE"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="type"/>, <paramref name="row"/> or <paramref name="col"/> is invalid.</exception>
    /// <remarks>
    /// Because row and column are used to index into an array, they are always non-negative.
    /// The location of the origin (and the type of operation) determine the actual direction of the rasterop.
    /// </remarks>
    public void SetElement(int row, int col, int type)
    {
        if (type != L.SEL_HIT && type != L.SEL_MISS && type != L.SEL_DONT_CARE)
            throw Lept.Arg("selSetElement", "invalid sel element type");
        if (row < 0 || row >= Sy)
            throw Lept.Arg("selSetElement", "sel row out of bounds");
        if (col < 0 || col >= Sx)
            throw Lept.Arg("selSetElement", "sel col out of bounds");

        data[row][col] = type;
    }

    // leptonica: selGetParameters (sel1.c)
    /// <summary>Gets the dimensions and origin of the Sel.</summary>
    /// <param name="psy">Receives the number of rows.</param>
    /// <param name="psx">Receives the number of columns.</param>
    /// <param name="pcy">Receives the row of the origin.</param>
    /// <param name="pcx">Receives the column of the origin.</param>
    public void GetParameters(out int psy, out int psx, out int pcy, out int pcx)
    {
        psy = Sy;
        psx = Sx;
        pcy = Cy;
        pcx = Cx;
    }

    // leptonica: selSetOrigin (sel1.c)
    /// <summary>Sets the location of the origin of the Sel.</summary>
    /// <param name="cy">Row of the origin.</param>
    /// <param name="cx">Column of the origin.</param>
    public void SetOrigin(int cy, int cx)
    {
        Cy = cy;
        Cx = cx;
    }

    // leptonica: selGetTypeAtOrigin (sel1.c)
    /// <returns>SEL_HIT, SEL_MISS, SEL_DONT_CARE; throws if the origin is not found</returns>
    public int GetTypeAtOrigin()
    {
        GetParameters(out int sy, out int sx, out int cy, out int cx);
        for (int i = 0; i < sy; i++) {
            for (int j = 0; j < sx; j++) {
                if (i == cy && j == cx)
                    return GetElement(i, j);
            }
        }

        throw Lept.Arg("selGetTypeAtOrigin", "sel origin not found");
    }

    // leptonica: getCompositeParameters (sel1.c)
    /// <param name="size">in the range {2 ... 63}</param>
    /// <param name="psize1">brick factor size</param>
    /// <param name="psize2">comb factor size</param>
    /// <param name="pnameh1">name of horiz brick</param>
    /// <param name="pnameh2">name of horiz comb</param>
    /// <param name="pnamev1">name of vert brick</param>
    /// <param name="pnamev2">name of vert comb</param>
    /// <remarks>
    /// (1) This uses the big lookup table at the top of this file.
    /// </remarks>
    public static void GetCompositeParameters(int size, out int psize1, out int psize2,
                                              out string pnameh1, out string pnameh2,
                                              out string pnamev1, out string pnamev2)
    {
        if (size < 2 || size > 63)
            throw Lept.Arg("getCompositeParameters", "valid size range is {2 ... 63}");
        int index = size - 2;
        psize1 = comp_parameter_map[index].Size1;
        psize2 = comp_parameter_map[index].Size2;
        pnameh1 = comp_parameter_map[index].Selnameh1;
        pnameh2 = comp_parameter_map[index].Selnameh2;
        pnamev1 = comp_parameter_map[index].Selnamev1;
        pnamev2 = comp_parameter_map[index].Selnamev2;
    }

    /*----------------------------------------------------------------------*
     *                Max translations for erosion and hmt                  *
     *----------------------------------------------------------------------*/
    // leptonica: selFindMaxTranslations (sel1.c)
    /// <param name="pxp">max shift</param>
    /// <param name="pyp">max shift</param>
    /// <param name="pxn">max shift</param>
    /// <param name="pyn">max shift</param>
    /// <remarks>
    /// These are the maximum shifts for the erosion operation.
    /// For example, when j &lt; cx, the shift of the image
    /// is +x to the cx.  This is a positive xp shift.
    /// </remarks>
    public void FindMaxTranslations(out int pxp, out int pyp, out int pxn, out int pyn)
    {
        GetParameters(out int sy, out int sx, out int cy, out int cx);

        int maxxp, maxyp, maxxn, maxyn;
        maxxp = maxyp = maxxn = maxyn = 0;
        for (int i = 0; i < sy; i++) {
            for (int j = 0; j < sx; j++) {
                if (data[i][j] == 1) {
                    maxxp = Math.Max(maxxp, cx - j);
                    maxyp = Math.Max(maxyp, cy - i);
                    maxxn = Math.Max(maxxn, j - cx);
                    maxyn = Math.Max(maxyn, i - cy);
                }
            }
        }

        pxp = maxxp;
        pyp = maxyp;
        pxn = maxxn;
        pyn = maxyn;
    }

    /*----------------------------------------------------------------------*
     *                   Rotation by multiples of 90 degrees                *
     *----------------------------------------------------------------------*/
    // leptonica: selRotateOrth (sel1.c)
    /// <param name="quads">0 - 4; number of 90 degree cw rotations</param>
    public Sel RotateOrth(int quads)
    {
        int ni, nj, nsx, nsy, ncx, ncy;

        if (quads < 0 || quads > 4)
            throw Lept.Arg("selRotateOrth", "quads not in {0,1,2,3,4}");
        if (quads == 0 || quads == 4)
            return Copy();

        GetParameters(out int sy, out int sx, out int cy, out int cx);
        if (quads == 1) {  /* 90 degrees cw */
            nsx = sy;
            nsy = sx;
            ncx = sy - cy - 1;
            ncy = cx;
        } else if (quads == 2) {  /* 180 degrees cw */
            nsx = sx;
            nsy = sy;
            ncx = sx - cx - 1;
            ncy = sy - cy - 1;
        } else {  /* 270 degrees cw */
            nsx = sy;
            nsy = sx;
            ncx = cy;
            ncy = sx - cx - 1;
        }
        Sel seld = CreateBrick(nsy, nsx, ncy, ncx, L.SEL_DONT_CARE);
        if (name != null)
            seld.name = name;

        for (int i = 0; i < sy; i++) {
            for (int j = 0; j < sx; j++) {
                int type = GetElement(i, j);
                if (quads == 1) {
                    ni = j;
                    nj = sy - i - 1;
                } else if (quads == 2) {
                    ni = sy - i - 1;
                    nj = sx - j - 1;
                } else {  /* quads == 3 */
                    ni = sx - j - 1;
                    nj = i;
                }
                seld.SetElement(ni, nj, type);
            }
        }

        return seld;
    }

    /*----------------------------------------------------------------------*
     *                       Sela and Sel serialized I/O                    *
     *----------------------------------------------------------------------*/
    // leptonica: selRead (sel1.c)
    /// <summary>Reads a serialized Sel from a file.</summary>
    /// <param name="fname">Path of the file to read.</param>
    /// <returns>The deserialized <see cref="Sel"/>.</returns>
    public static Sel Read(string fname)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(fname);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("selRead", $"stream not opened: {fname}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in selRead: sela not returned: {fname}", e);
            }
        }
    }

    // leptonica: selReadStream (sel1.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// If the stream is not seekable, the reader may consume one character
    /// beyond the serialized data.
    /// </remarks>
    public static Sel ReadStream(Stream fp)
    {
        SelScanner sc = new SelScanner(fp);
        try {
            return ReadStream(sc);
        } finally {
            sc.Release();
        }
    }

    internal static Sel ReadStream(SelScanner sc)
    {
        const string func = "selReadStream";
        Span<int> v = stackalloc int[4];
        Span<int> one = stackalloc int[1];

        if (sc.Scan("  Sel Version %d\n", v) != 1)
            throw Lept.Arg(func, "not a sel file");
        int version = v[0];
        if (version != L.SEL_VERSION_NUMBER)
            throw Lept.Arg(func, "invalid sel version");

        string? linebuf = sc.Gets(256);
        if (linebuf == null)
            throw Lept.Arg(func, "error reading into linebuf");
        /* NOTE(upstream): selname is left uninitialized in C when sscanf fails to match;
         * it is the empty string here. */
        string selname = "";
        CFormat.Sscanf(linebuf, "  ------  %200s  ------", out object?[] sv);
        if (sv.Length > 0 && sv[0] is string s)
            selname = s;

        if (sc.Scan("  sy = %d, sx = %d, cy = %d, cx = %d\n", v) != 4)
            throw Lept.Arg(func, "dimensions not read");
        int sy = v[0], sx = v[1], cy = v[2], cx = v[3];

        Sel sel;
        try {
            sel = Create(sy, sx, selname);
        } catch (ArgumentException e) {
            throw new ArgumentException("Error in selReadStream: sel not made", e);
        }
        sel.SetOrigin(cy, cx);

        for (int i = 0; i < sy; i++) {
            sc.Scan("    ", v);
            for (int j = 0; j < sx; j++) {
                if (sc.Scan("%1d", one) == 1)
                    sel.data[i][j] = one[0];
            }
            sc.Scan("\n", v);
        }
        sc.Scan("\n", v);

        return sel;
    }

    // leptonica: selWrite (sel1.c)
    /// <summary>Writes the Sel to a file in serialized text form.</summary>
    /// <param name="fname">Path of the file to write.</param>
    /// <exception cref="ArgumentException">The file cannot be opened for writing.</exception>
    public void Write(string fname)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(fname, "wb");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("selWrite", $"stream not opened: {fname}");
        }
        using (fp) {
            WriteStream(fp);
        }
    }

    // leptonica: selWriteStream (sel1.c)
    /// <summary>Writes the Sel to a stream in serialized text form.</summary>
    /// <param name="fp">The stream to write to.</param>
    public void WriteStream(Stream fp)
    {
        GetParameters(out int sy, out int sx, out int cy, out int cx);

        CStdio.Fprintf(fp, $"  Sel Version {L.SEL_VERSION_NUMBER}\n");
        string line = CFormat.Sprintf("  ------  %s  ------\n", Name);
        CStdio.Fprintf(fp, $"{line}");
        CStdio.Fprintf(fp, $"  sy = {sy}, sx = {sx}, cy = {cy}, cx = {cx}\n");
        for (int i = 0; i < sy; i++) {
            CStdio.Fprintf(fp, $"    ");
            for (int j = 0; j < sx; j++)
                CStdio.Fprintf(fp, $"{data[i][j]}");
            CStdio.Fprintf(fp, $"\n");
        }
        CStdio.Fprintf(fp, $"\n");
    }

    /*----------------------------------------------------------------------*
     *           Building custom hit-miss sels from compiled strings        *
     *----------------------------------------------------------------------*/
    // leptonica: selCreateFromString (sel1.c)
    /// <param name="text">sel elements in row-major order</param>
    /// <param name="h">height</param>
    /// <param name="w">width</param>
    /// <param name="name">[optional] sel name; can be null</param>
    /// <remarks>
    /// (1) The text is an array of chars (in row-major order) where
    ///     each char can be one of the following:
    ///        'x': hit
    ///        'o': miss
    ///        ' ': don't-care
    /// (2) When the origin falls on a hit or miss, use an upper case
    ///     char (e.g., 'X' or 'O') to indicate it.  When the origin
    ///     falls on a don't-care, indicate this with a 'C'.
    ///     The string must have exactly one origin specified.
    /// (3) The advantage of this method is that the text can be input
    ///     in a format that shows the 2D layout of the Sel; e.g.,
    /// <code>
    ///         static const char *seltext = "x    "
    ///                                      "x Oo "
    ///                                      "x    "
    ///                                      "xxxxx";
    /// </code>
    /// (4) If there is not exactly one origin, C logs an error and returns
    ///     NULL; this throws.
    /// </remarks>
    public static Sel CreateFromString(string text, int h, int w, string? name = null)
    {
        const string func = "selCreateFromString";

        if (text.Length == 0 || text[0] == '\0')
            throw Lept.Arg(func, "text undefined or empty");
        if (h < 1)
            throw Lept.Arg(func, "height must be > 0");
        if (w < 1)
            throw Lept.Arg(func, "width must be > 0");
        if (text.Length != (long)w * h)
            throw Lept.Arg(func, "text size != w * h");

        Sel sel = Create(h, w, name);
        int norig = 0;
        int pos = 0;
        for (int y = 0; y < h; ++y) {
            for (int x = 0; x < w; ++x) {
                char ch = text[pos++];
                switch (ch)
                {
                    case 'X':
                        norig++;
                        sel.SetOrigin(y, x);
                        sel.SetElement(y, x, L.SEL_HIT);
                        break;
                    case 'x':
                        sel.SetElement(y, x, L.SEL_HIT);
                        break;

                    case 'O':
                        norig++;
                        sel.SetOrigin(y, x);
                        sel.SetElement(y, x, L.SEL_MISS);
                        break;
                    case 'o':
                        sel.SetElement(y, x, L.SEL_MISS);
                        break;

                    case 'C':
                        norig++;
                        sel.SetOrigin(y, x);
                        sel.SetElement(y, x, L.SEL_DONT_CARE);
                        break;
                    case ' ':
                        sel.SetElement(y, x, L.SEL_DONT_CARE);
                        break;

                    case '\n':
                        /* ignored */
                        continue;

                    default:
                        throw Lept.Arg(func, "unknown char");
                }
            }
        }
        if (norig != 1) {
            Log.Error(func, $"Exactly one origin must be specified; this string has {norig}");
            throw Lept.Arg(func, $"Exactly one origin must be specified; this string has {norig}");
        }

        return sel;
    }

    // leptonica: selPrintToString (sel1.c)
    /// <returns>a string with one row per line, each ended by '\n'</returns>
    /// <remarks>
    /// (1) This is an inverse function of selCreateFromString.
    ///     It prints a textual representation of the SEL to a string.
    ///     The format is the same as selCreateFromString
    ///     except that newlines are inserted into the output
    ///     between rows.
    /// (2) This is useful for debugging.  However, if you want to
    ///     save some Sels in a file, put them in a Sela and write
    ///     them out with selaWrite().  They can then be read in
    ///     with selaRead().
    /// </remarks>
    public string PrintToString()
    {
        GetParameters(out int sy, out int sx, out int cy, out int cx);
        StringBuilder str = new StringBuilder(sy * (sx + 1) + 1);

        for (int y = 0; y < sy; ++y) {
            for (int x = 0; x < sx; ++x) {
                int type = GetElement(y, x);
                bool is_center = (x == cx && y == cy);
                switch (type) {
                    case L.SEL_HIT:
                        str.Append(is_center ? 'X' : 'x');
                        break;
                    case L.SEL_MISS:
                        str.Append(is_center ? 'O' : 'o');
                        break;
                    case L.SEL_DONT_CARE:
                        str.Append(is_center ? 'C' : ' ');
                        break;
                }
            }
            str.Append('\n');
        }

        return str.ToString();
    }

    /*----------------------------------------------------------------------*
     *         Building custom hit-miss sels from a simple file format      *
     *----------------------------------------------------------------------*/
    // leptonica: selCreateFromSArray (sel1.c)
    /// <param name="sa">lines of the sel file</param>
    /// <param name="first">line of sarray where Sel begins</param>
    /// <param name="last">line of sarray where Sel ends</param>
    /// <remarks>
    /// (1) The Sel contains the following lines:
    ///     ~ The first line is the selname
    ///     ~ The remaining lines contain the Sel data.  They must
    ///       be formatted similarly to the string format in
    ///       selCreateFromString(), with each line beginning and
    ///       ending with a double-quote, and showing the 2D layout.
    ///     ~ 'last' gives the last line in the Sel data.
    /// (2) See selCreateFromString() for a description of the string
    ///     format for the Sel data.  As an example, here are the lines
    ///     of is a valid file for a single Sel.  In the file, all lines
    ///     are left-justified:
    ///               # diagonal sel
    ///               sel_5diag
    ///               "x    "
    ///               " x   "
    ///               "  X  "
    ///               "   x "
    ///               "    x"
    /// </remarks>
    internal static Sel CreateFromSArray(Sarray sa, int first, int last)
    {
        const string func = "selCreateFromSArray";

        int n = sa.Count;
        if (first < 0 || first >= n || last <= first || last >= n)
            throw Lept.Arg(func, "invalid range");

        string name = sa.GetString(first, L.L_NOCOPY);
        int h = last - first;
        string line = sa.GetString(first + 1, L.L_NOCOPY);
        int len = line.Length;
        if (len == 0 || line[0] != '"' || line[len - 1] != '"')
            throw Lept.Arg(func, "invalid format");
        int w = len - 2;
        Sel sel;
        try {
            sel = Create(h, w, name);
        } catch (ArgumentException e) {
            throw new ArgumentException("Error in selCreateFromSArray: sel not made", e);
        }
        for (int i = first + 1; i <= last; i++) {
            line = sa.GetString(i, L.L_NOCOPY);
            int y = i - first - 1;
            for (int x = 0; x < w; ++x) {
                /* skip the leading double-quote; a short line reads as the C terminator */
                char ch = x + 1 < line.Length ? line[x + 1] : '\0';
                switch (ch)
                {
                    case 'X':
                        sel.SetOrigin(y, x);  /* set origin and hit */
                        sel.SetElement(y, x, L.SEL_HIT);
                        break;
                    case 'x':
                        sel.SetElement(y, x, L.SEL_HIT);
                        break;

                    case 'O':
                        sel.SetOrigin(y, x);  /* set origin and miss */
                        sel.SetElement(y, x, L.SEL_MISS);
                        break;
                    case 'o':
                        sel.SetElement(y, x, L.SEL_MISS);
                        break;

                    case 'C':
                        sel.SetOrigin(y, x);  /* set origin and don't-care */
                        sel.SetElement(y, x, L.SEL_DONT_CARE);
                        break;
                    case ' ':
                        sel.SetElement(y, x, L.SEL_DONT_CARE);
                        break;

                    default:
                        throw Lept.Arg(func, "unknown char");
                }
            }
        }

        return sel;
    }

    /*----------------------------------------------------------------------*
     *               Making hit-only SELs from Pta and Pix                  *
     *----------------------------------------------------------------------*/
    // leptonica: selCreateFromPta (sel1.c)
    /// <param name="pta">points that are hits</param>
    /// <param name="cy">origin of sel</param>
    /// <param name="cx">origin of sel</param>
    /// <param name="name">[optional] sel name; can be null</param>
    /// <returns>sel of minimum required size</returns>
    /// <remarks>
    /// (1) The origin and all points in the pta must be positive.
    /// </remarks>
    public static Sel CreateFromPta(Pta pta, int cy, int cx, string? name = null)
    {
        const string func = "selCreateFromPta";

        if (cy < 0 || cx < 0)
            throw Lept.Arg(func, "(cy, cx) not both >= 0");
        int n = pta.Count;
        if (n == 0)
            throw Lept.Arg(func, "no pts in pta");

        Box box = pta.GetBoundingRegion();
        box.GetGeometry(out int x, out int y, out int w, out int h);
        if (x < 0 || y < 0)
            throw Lept.Arg(func, "not all x and y >= 0");

        Sel sel = Create(y + h, x + w, name);
        sel.SetOrigin(cy, cx);
        for (int i = 0; i < n; i++) {
            pta.GetIPt(i, out x, out y);
            sel.SetElement(y, x, L.SEL_HIT);
        }

        return sel;
    }

    // leptonica: selCreateFromPix (sel1.c)
    /// <param name="pix">1 bpp; the hits</param>
    /// <param name="cy">origin of sel</param>
    /// <param name="cx">origin of sel</param>
    /// <param name="name">[optional] sel name; can be null</param>
    /// <remarks>
    /// (1) The origin must be positive.
    /// (2) The pix must not exceed MaxPixTemplateSize in either dimension.
    ///     and the total number of hits must not exceed MaxPixTemplateHits.
    /// (3) C logs an error and returns NULL for a template that is too large
    ///     or has too many hits; this throws.
    /// </remarks>
    public static Sel CreateFromPix(Pix pix, int cy, int cx, string? name = null)
    {
        const string func = "selCreateFromPix";

        if (cy < 0 || cx < 0)
            throw Lept.Arg(func, "(cy, cx) not both >= 0");
        pix.GetDimensions(out int w, out int h, out int d);
        if (d != 1)
            throw Lept.Arg(func, "pix not 1 bpp");
        if (w > MaxPixTemplateSize || h > MaxPixTemplateSize) {
            Log.Error(func, $"pix template too large (w = {w}, h = {h})");
            throw Lept.Arg(func, $"pix template too large (w = {w}, h = {h})");
        }
        if (w > MaxPixTemplateSize / 5 || h > MaxPixTemplateSize / 5)
            Log.Warning(func, $"large pix template: w = {w}, h = {h}");
        int nhits = pix.CountPixels(null);
        if (nhits > MaxPixTemplateHits) {
            Log.Error(func, $"too many hits ({nhits}) in pix template");
            throw Lept.Arg(func, $"too many hits ({nhits}) in pix template");
        }
        if (nhits > MaxPixTemplateHits / 5)
            Log.Warning(func, $"many hits ({nhits}) in pix template");

        Sel sel = Create(h, w, name);
        sel.SetOrigin(cy, cx);
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                pix.GetPixel(j, i, out uint val);
                if (val != 0)
                    sel.SetElement(i, j, L.SEL_HIT);
            }
        }

        return sel;
    }

    /*----------------------------------------------------------------------*
     *            Making hit-miss sels from color Pix and image files             *
     *----------------------------------------------------------------------*/
    // leptonica: selReadFromColorImage (sel1.c)
    /// <remarks>
    /// (1) Loads an image from a file and creates a (hit-miss) sel.
    /// (2) The sel name is taken from the pathname without the directory
    ///     and extension.
    /// </remarks>
    public static Sel ReadFromColorImage(string pathname)
    {
        Utils.SplitPathAtExtension(pathname, out string basename, out _);
        Utils.SplitPathAtDirectory(basename, out _, out string selname);

        Pix pix;
        try {
            pix = Pix.Read(pathname);
        } catch (ArgumentException e) {
            throw new ArgumentException("Error in selReadFromColorImage: pix not returned", e);
        }
        try {
            return CreateFromColorPix(pix, selname);
        } catch (ArgumentException) {
            Log.Error("selReadFromColorImage", "sel not made");
            throw;
        }
    }

    // leptonica: selCreateFromColorPix (sel1.c)
    /// <param name="pixs">cmapped or rgb</param>
    /// <param name="selname">[optional] sel name; can be null</param>
    /// <remarks>
    /// (1) The sel size is given by the size of pixs.
    /// (2) In pixs, hits are represented by green pixels, misses by red
    ///     pixels, and don't-cares by white pixels.
    /// (3) In pixs, there may be no misses, but there must be at least 1 hit.
    /// (4) At most there can be only one origin pixel, which is optionally
    ///     specified by using a lower-intensity pixel:
    ///       if a hit:  dark green
    ///       if a miss: dark red
    ///       if a don't care: gray
    ///     If there is no such pixel, the origin defaults to the approximate
    ///     center of the sel.
    /// </remarks>
    public static Sel CreateFromColorPix(Pix pixs, string? selname)
    {
        const string func = "selCreateFromColorPix";
        int red, green, blue;

        bool hascolor = false;
        PixColormap? cmap = pixs.Colormap;
        if (cmap != null)
            hascolor = cmap.HasColor();
        pixs.GetDimensions(out int w, out int h, out int d);
        if (hascolor == false && d != 32)
            throw Lept.Arg(func, "pixs has no color");

        Sel sel;
        try {
            sel = Create(h, w, null);
        } catch (ArgumentException e) {
            throw new ArgumentException("Error in selCreateFromColorPix: sel not made", e);
        }
        sel.SetOrigin(h / 2, w / 2);  /* default */
        sel.Name = selname;

        int num_origins = 0;
        bool nohits = true;
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                pixs.GetPixel(j, i, out uint pixval);

                if (cmap != null) {
                    cmap.GetColor((int)pixval, out red, out green, out blue);
                } else {
                    red = GetDataByte(pixval, L.COLOR_RED);
                    green = GetDataByte(pixval, L.COLOR_GREEN);
                    blue = GetDataByte(pixval, L.COLOR_BLUE);
                }

                if (red < 255 && green < 255 && blue < 255) {
                    num_origins++;
                    if (num_origins == 1)  /* first one found */
                        sel.SetOrigin(i, j);
                    if (num_origins == 2)
                        Log.Warning(func, "multiple origins in sel image");
                }
                if (red == 0 && green != 0 && blue == 0) {
                    nohits = false;
                    sel.SetElement(i, j, L.SEL_HIT);
                } else if (red != 0 && green == 0 && blue == 0) {
                    sel.SetElement(i, j, L.SEL_MISS);
                } else if (red != 0 && green != 0 && blue != 0) {
                    sel.SetElement(i, j, L.SEL_DONT_CARE);
                } else {
                    throw Lept.Arg(func, "invalid color");
                }
            }
        }

        if (nohits)
            throw Lept.Arg(func, "no hits in sel");
        return sel;
    }

    /*----------------------------------------------------------------------*
     *                     Printable display of sel                         *
     *----------------------------------------------------------------------*/
    // leptonica: selDisplayInPix (sel1.c)
    /// <param name="size">of grid interiors; odd; minimum size of 13 is enforced</param>
    /// <param name="gthick">grid thickness; minimum size of 2 is enforced</param>
    /// <returns>pix display of sel</returns>
    /// <remarks>
    /// (1) This gives a visual representation of a general (hit-miss) sel.
    /// (2) The empty sel is represented by a grid of intersecting lines.
    /// (3) Three different patterns are generated for the sel elements:
    ///     ~ hit (solid black circle)
    ///     ~ miss (black ring; inner radius is radius2)
    ///     ~ origin (cross, XORed with whatever is there)
    /// </remarks>
    public Pix DisplayInPix(int size, int gthick)
    {
        int type;

        if (size < 13) {
            Log.Warning("selDisplayInPix", "size < 13; setting to 13");
            size = 13;
        }
        if (size % 2 == 0)
            size++;
        if (gthick < 2) {
            Log.Warning("selDisplayInPix", "grid thickness < 2; setting to 2");
            gthick = 2;
        }
        GetParameters(out int sy, out int sx, out int cy, out int cx);
        int w = size * sx + gthick * (sx + 1);
        int h = size * sy + gthick * (sy + 1);
        Pix pixd = Pix.Create(w, h, 1);

            /* Generate grid lines */
        for (int i = 0; i <= sy; i++)
            pixd.RenderLine(0, gthick / 2 + i * (size + gthick),
                            w - 1, gthick / 2 + i * (size + gthick),
                            gthick, L.L_SET_PIXELS);
        for (int j = 0; j <= sx; j++)
            pixd.RenderLine(gthick / 2 + j * (size + gthick), 0,
                            gthick / 2 + j * (size + gthick), h - 1,
                            gthick, L.L_SET_PIXELS);

            /* Generate hit and miss patterns */
        int radius1 = (int)(0.85 * ((size - 1) / 2.0) + 0.5);  /* of hit */
        int radius2 = (int)(0.65 * ((size - 1) / 2.0) + 0.5);  /* of inner miss */
        Pta pta1 = Pix.GeneratePtaFilledCircle(radius1);
        Pta pta2 = Pix.GeneratePtaFilledCircle(radius2);
        int shift1 = (size - 1) / 2 - radius1;  /* center circle in square */
        int shift2 = (size - 1) / 2 - radius2;
        Pta pta1t = pta1.Transform(shift1, shift1, 1.0f, 1.0f);
        Pta pta2t = pta2.Transform(shift2, shift2, 1.0f, 1.0f);
        Pix pixh = Pix.GenerateFromPta(pta1t, size, size);  /* hits */
        Pix pix2 = Pix.GenerateFromPta(pta2t, size, size);
        Pix pixm = Pix.Subtract(null, pixh, pix2);

            /* Generate crossed lines for origin pattern */
        Pix pixorig = Pix.Create(size, size, 1);
        int width = size / 8;
        pixorig.RenderLine(size / 2, (int)(0.12 * size),
                           size / 2, (int)(0.88 * size),
                           width, L.L_SET_PIXELS);
        pixorig.RenderLine((int)(0.15 * size), size / 2,
                           (int)(0.85 * size), size / 2,
                           width, L.L_FLIP_PIXELS);
        pixorig.Rasterop(size / 2 - width, size / 2 - width,
                         2 * width, 2 * width, L.PIX_NOT(L.PIX_DST), null, 0, 0);

            /* Specialize origin pattern for this sel */
        try {
            type = GetTypeAtOrigin();
        } catch (ArgumentException) {
            type = L.SEL_DONT_CARE;  /* C ignores the error return and keeps the initial type */
        }
        if (type == L.SEL_HIT)
            Pix.Xor(pixorig, pixorig, pixh);
        else if (type == L.SEL_MISS)
            Pix.Xor(pixorig, pixorig, pixm);

            /* Paste the patterns in */
        int y0 = gthick;
        for (int i = 0; i < sy; i++) {
            int x0 = gthick;
            for (int j = 0; j < sx; j++) {
                type = GetElement(i, j);
                if (i == cy && j == cx)  /* origin */
                    pixd.Rasterop(x0, y0, size, size, L.PIX_SRC, pixorig, 0, 0);
                else if (type == L.SEL_HIT)
                    pixd.Rasterop(x0, y0, size, size, L.PIX_SRC, pixh, 0, 0);
                else if (type == L.SEL_MISS)
                    pixd.Rasterop(x0, y0, size, size, L.PIX_SRC, pixm, 0, 0);
                x0 += size + gthick;
            }
            y0 += size + gthick;
        }

        return pixd;
    }
}

/// <summary>
/// Array of Sel (struct Sela).
/// </summary>
public sealed partial class Sela
{
    /* Bounds on sel ptr array size */
    private const int MaxPtrArraySize = 10000;
    private const int InitialPtrArraySize = 50;      /* n'importe quoi */

    private Sel?[] sels;
    private int n;

    private Sela(int nalloc)
    {
        sels = new Sel?[nalloc];
        n = 0;
    }

    /// <summary>Number of allocated sel ptrs (sela->nalloc).</summary>
    public int Nalloc => sels.Length;

    /*------------------------------------------------------------------------*
     *                      Create / Destroy / Copy                           *
     *------------------------------------------------------------------------*/
    // leptonica: selaCreate (sel1.c)
    /// <param name="n">initial number of sel ptrs; use 0 for default</param>
    public static Sela Create(int n)
    {
        if (n <= 0 || n > MaxPtrArraySize)
            n = InitialPtrArraySize;

        return new Sela(n);
    }

    /*------------------------------------------------------------------------*
     *                           Extension of sela                            *
     *------------------------------------------------------------------------*/
    // leptonica: selaAddSel (sel1.c)
    /// <param name="sel">to be added</param>
    /// <param name="selname">ignored if already defined in sel;
    /// req'd in sel when added to a sela</param>
    /// <param name="copyflag">L_INSERT or L_COPY</param>
    /// <remarks>
    /// (1) This adds a sel, either inserting or making a copy.
    /// (2) Because every sel in a sela must have a name, it copies
    ///     the input name if necessary.  You can input null for
    ///     selname if the sel already has a name.
    /// </remarks>
    public void AddSel(Sel sel, string? selname, int copyflag)
    {
        if (sel.Name == null && selname == null)
            throw Lept.Arg("selaAddSel", "added sel must have name");
        if (copyflag != L.L_INSERT && copyflag != L.L_COPY)
            throw Lept.Arg("selaAddSel", "invalid copyflag");

        Sel csel;
        if (copyflag == L.L_COPY) {
            try {
                csel = sel.Copy();
            } catch (ArgumentException e) {
                throw new ArgumentException("Error in selaAddSel: csel not made", e);
            }
        } else {  /* copyflag == L_INSERT */
            csel = sel;
        }
        if (csel.Name == null)
            csel.Name = selname;

        int n = Count;
        if (n >= Nalloc)
            ExtendArray();

        sels[n] = csel;
        this.n++;
    }

    // leptonica: selaExtendArray (sel1.c)
    private void ExtendArray()
    {
        Array.Resize(ref sels, 2 * sels.Length);
    }

    /*----------------------------------------------------------------------*
     *                               Accessors                              *
     *----------------------------------------------------------------------*/
    // leptonica: selaGetCount (sel1.c)
    /// <summary>Gets the number of Sels in the array.</summary>
    public int Count => n;

    // leptonica: selaGetSel (sel1.c)
    /// <param name="i">index of sel to be retrieved not copied</param>
    /// <remarks>
    /// (1) This returns the sel, not a copy.
    /// </remarks>
    public Sel GetSel(int i)
    {
        if (i < 0 || i >= n)
            throw Lept.Arg("selaGetSel", "invalid index");
        return sels[i]!;
    }

    // leptonica: selaFindSelByName (sel1.c)
    /// <param name="name">sel name</param>
    /// <param name="index">index of the sel; -1 if not found</param>
    /// <param name="sel">the sel (not a copy); null if not found</param>
    /// <returns>true if found</returns>
    public bool FindSelByName(string name, out int index, out Sel? sel)
    {
        index = -1;
        sel = null;

        int n = Count;
        for (int i = 0; i < n; i++)
        {
            Sel? s = sels[i];
            if (s == null) {
                Log.Warning("selaFindSelByName", "missing sel");
                continue;
            }

            string? sname = s.Name;
            if (sname != null && string.Equals(name, sname, StringComparison.Ordinal)) {
                index = i;
                sel = s;
                return true;
            }
        }

        return false;
    }

    // leptonica: selaGetBrickName (sel1.c)
    /// <param name="hsize">of brick sel</param>
    /// <param name="vsize">of brick sel</param>
    /// <returns>sel name, or null if the sel has no name</returns>
    public string? GetBrickName(int hsize, int vsize)
    {
        int nsels = Count;
        for (int i = 0; i < nsels; i++) {
            Sel sel = GetSel(i);
            sel.GetParameters(out int sy, out int sx, out _, out _);
            if (hsize == sx && vsize == sy)
                return sel.Name;
        }

        throw Lept.Arg("selaGetBrickName", "sel not found");
    }

    // leptonica: selaGetCombName (sel1.c)
    /// <param name="size">the product of sizes of the brick and comb parts</param>
    /// <param name="direction">L_HORIZ, L_VERT</param>
    /// <remarks>
    /// (1) Combs are by definition 1-dimensional, either horiz or vert.
    /// (2) Use this with comb Sels; e.g., from selaAddDwaCombs().
    /// </remarks>
    public string GetCombName(int size, int direction)
    {
        if (direction != Sel.L_HORIZ && direction != Sel.L_VERT)
            throw Lept.Arg("selaGetCombName", "invalid direction");

            /* Derive the comb name we're looking for */
        string combname;
        if (direction == Sel.L_HORIZ)
            combname = CFormat.Sprintf("sel_comb_%dh", size);
        else  /* direction == L_VERT */
            combname = CFormat.Sprintf("sel_comb_%dv", size);

        string? selname = null;
        bool found = false;
        int nsels = Count;
        for (int i = 0; i < nsels; i++) {
            Sel sel = GetSel(i);
            sel.GetParameters(out int sy, out int sx, out _, out _);
            if (sy != 1 && sx != 1)  /* 2-D; not a comb */
                continue;
            selname = sel.Name;
            if (string.Equals(selname, combname, StringComparison.Ordinal)) {
                found = true;
                break;
            }
        }

        if (found)
            return selname!;
        else
            throw Lept.Arg("selaGetCombName", "sel not found");
    }

    // leptonica: selaGetSelnames (sel1.c)
    /// <returns>sa of all sel names</returns>
    public Sarray GetSelnames()
    {
        int n = Count;
        if (n == 0)
            throw Lept.Arg("selaGetSelnames", "no sels in sela");

        Sarray sa = Sarray.Create(n);
        for (int i = 0; i < n; i++) {
            Sel sel = GetSel(i);
            string? selname = sel.Name;
            sa.AddString(selname!, L.L_COPY);
        }

        return sa;
    }

    /*----------------------------------------------------------------------*
     *                       Sela and Sel serialized I/O                    *
     *----------------------------------------------------------------------*/
    // leptonica: selaRead (sel1.c)
    /// <summary>Reads a serialized Sel array from a file.</summary>
    /// <param name="fname">Path of the file to read.</param>
    /// <returns>The deserialized <see cref="Sela"/>.</returns>
    public static Sela Read(string fname)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(fname);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("selaRead", $"stream not opened: {fname}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in selaRead: sela not returned: {fname}", e);
            }
        }
    }

    // leptonica: selaReadStream (sel1.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// If the stream is not seekable, the reader may consume one character
    /// beyond the serialized data.
    /// </remarks>
    public static Sela ReadStream(Stream fp)
    {
        SelScanner sc = new SelScanner(fp);
        try {
            return ReadStream(sc);
        } finally {
            sc.Release();
        }
    }

    internal static Sela ReadStream(SelScanner sc)
    {
        const string func = "selaReadStream";
        Span<int> v = stackalloc int[1];

        if (sc.Scan("\nSela Version %d\n", v) != 1)
            throw Lept.Arg(func, "not a sela file");
        int version = v[0];
        if (version != L.SEL_VERSION_NUMBER)
            throw Lept.Arg(func, "invalid sel version");
        if (sc.Scan("Number of Sels = %d\n\n", v) != 1)
            throw Lept.Arg(func, "not a sela file");
        int n = v[0];

        Sela sela = Create(n);
        /* NOTE(upstream): C also sets sela->nalloc = n here, overriding the array size;
         * nalloc is the array length in this port. */

        for (int i = 0; i < n; i++) {
            Sel sel;
            try {
                sel = Sel.ReadStream(sc);
            } catch (ArgumentException e) {
                throw new ArgumentException("Error in selaReadStream: sel not read", e);
            }
            sela.AddSel(sel, null, L.L_INSERT);
        }

        return sela;
    }

    // leptonica: selaWrite (sel1.c)
    /// <summary>Writes the Sel array to a file in serialized text form.</summary>
    /// <param name="fname">Path of the file to write.</param>
    /// <exception cref="ArgumentException">The file cannot be opened for writing.</exception>
    public void Write(string fname)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(fname, "wb");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("selaWrite", $"stream not opened: {fname}");
        }
        using (fp) {
            WriteStream(fp);
        }
    }

    // leptonica: selaWriteStream (sel1.c)
    /// <summary>Writes the Sel array to a stream in serialized text form.</summary>
    /// <param name="fp">The stream to write to.</param>
    public void WriteStream(Stream fp)
    {
        int n = Count;
        CStdio.Fprintf(fp, $"\nSela Version {L.SEL_VERSION_NUMBER}\n");
        CStdio.Fprintf(fp, $"Number of Sels = {n}\n\n");
        for (int i = 0; i < n; i++) {
            Sel? sel = sels[i];
            if (sel == null)
                continue;
            sel.WriteStream(fp);
        }
    }

    /*----------------------------------------------------------------------*
     *         Building custom hit-miss sels from a simple file format      *
     *----------------------------------------------------------------------*/
    // leptonica: selaCreateFromFile (sel1.c)
    /// <remarks>
    /// (1) The file contains a sequence of Sel descriptions.
    /// (2) Each Sel is formatted as follows:
    ///      ~ Any number of comment lines starting with '#' are ignored
    ///      ~ The next line contains the selname
    ///      ~ The next lines contain the Sel data.  They must be
    ///        formatted similarly to the string format in
    ///        selCreateFromString(), with each line beginning and
    ///        ending with a double-quote, and showing the 2D layout.
    ///      ~ Each Sel ends when a blank line, a comment line, or
    ///        the end of file is reached.
    /// (3) See selCreateFromString() for a description of the string
    ///     format for the Sel data.  As an example, here are the lines
    ///     of is a valid file for a single Sel.  In the file, all lines
    ///     are left-justified:
    ///               # diagonal sel
    ///               sel_5diag
    ///               "x    "
    ///               " x   "
    ///               "  X  "
    ///               "   x "
    ///               "    x"
    /// </remarks>
    public static Sela CreateFromFile(string filename)
    {
        byte[] filebytes = Utils.BinaryRead(filename);
        string filestr = Encoding.UTF8.GetString(filebytes);
        Sarray sa = Sarray.CreateLinesFromString(filestr, 1);
        int n = sa.Count;
        Sela sela = Create(0);

            /* Find the start and end lines for each Sel.
             * We allow the "blank" lines to be null strings or
             * to have standard whitespace (' ','\t',\'n') or be '#'. */
        Numa nafirst = Numa.Create(0);
        Numa nalast = Numa.Create(0);
        bool insel = false;
        for (int i = 0; i < n; i++) {
            string line = sa.GetString(i, L.L_NOCOPY);
            char c0 = line.Length > 0 ? line[0] : '\0';
            if (!insel &&
                (c0 != '\0' && c0 != ' ' &&
                 c0 != '\t' && c0 != '\n' && c0 != '#')) {
                nafirst.AddNumber(i);
                insel = true;
                continue;
            }
            if (insel &&
                (c0 == '\0' || c0 == ' ' ||
                 c0 == '\t' || c0 == '\n' || c0 == '#')) {
                nalast.AddNumber(i - 1);
                insel = false;
                continue;
            }
        }
        if (insel)  /* fell off the end of the file */
            nalast.AddNumber(n - 1);

            /* Extract sels */
        int nsel = nafirst.Count;
        for (int i = 0; i < nsel; i++) {
            int first = nafirst.GetIValue(i);
            int last = nalast.GetIValue(i);
            Sel sel;
            try {
                sel = Sel.CreateFromSArray(sa, first, last);
            } catch (ArgumentException e) {
                CStdio.Stderr($"Error reading sel from {first} to {last}\n");
                throw new ArgumentException("Error in selaCreateFromFile: bad sela file", e);
            }
            sela.AddSel(sel, null, L.L_INSERT);
        }

        return sela;
    }

    /*----------------------------------------------------------------------*
     *            Making hit-miss sels from color Pix and image files             *
     *----------------------------------------------------------------------*/
    // leptonica: selaCreateFromColorPixa (sel1.c)
    /// <param name="pixa">color pixa representing the sels</param>
    /// <param name="sa">sarray of sel names</param>
    /// <remarks>
    /// (1) See notes in selCreateFromColorPix()
    /// (2) sa is required because all sels that are put in a sela
    ///     must have a name.
    /// (3) As in C, a pix that does not make a sel is skipped.
    /// </remarks>
    public static Sela CreateFromColorPixa(Pixa pixa, Sarray sa)
    {
        int n = pixa.Count;
        Sela sela = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            try {
                string str = sa.GetString(i, L.L_NOCOPY);
                Sel sel = Sel.CreateFromColorPix(pix, str);
                sela.AddSel(sel, null, L.L_INSERT);
            } catch (ArgumentException e) {
                Log.Error("selaCreateFromColorPixa", e.Message);
            }
        }
        return sela;
    }

    /*----------------------------------------------------------------------*
     *                     Printable display of sel                         *
     *----------------------------------------------------------------------*/
    // leptonica: selaDisplayInPix (sel1.c)
    /// <param name="size">of grid interiors; odd; minimum size of 13 is enforced</param>
    /// <param name="gthick">grid thickness; minimum size of 2 is enforced</param>
    /// <param name="spacing">between sels, both horizontally and vertically</param>
    /// <param name="ncols">number of sels per "line"</param>
    /// <returns>pix display of all sels in sela</returns>
    /// <remarks>
    /// (1) This gives a visual representation of all the sels in a sela.
    /// (2) See notes in selDisplayInPix() for display params of each sel.
    /// (3) This gives the nicest results when all sels in the sela
    ///     are the same size.
    /// </remarks>
    public Pix DisplayInPix(int size, int gthick, int spacing, int ncols)
    {
        if (size < 13) {
            Log.Warning("selaDisplayInPix", "size < 13; setting to 13");
            size = 13;
        }
        if (size % 2 == 0)
            size++;
        if (gthick < 2) {
            Log.Warning("selaDisplayInPix", "grid thickness < 2; setting to 2");
            gthick = 2;
        }
        if (spacing < 5) {
            Log.Warning("selaDisplayInPix", "spacing < 5; setting to 5");
            spacing = 5;
        }

            /* Accumulate the pix of each sel */
        int nsels = Count;
        Pixa pixa = Pixa.Create(nsels);
        for (int i = 0; i < nsels; i++) {
            Sel sel = GetSel(i);
            Pix pixt = sel.DisplayInPix(size, gthick);
            pixa.AddPix(pixt, L.L_INSERT);
        }

            /* Find the tiled output width, using just the first
             * ncols pix in the pixa.   If all pix have the same width,
             * they will align properly in columns. */
        int width = 0;
        ncols = Math.Min(nsels, ncols);
        for (int i = 0; i < ncols; i++) {
            Pix pixt = pixa.GetPix(i, L.L_CLONE);
            pixt.GetDimensions(out int w, out _, out _);
            width += w;
        }
        width += (ncols + 1) * spacing;  /* add spacing all around as well */

        Pix pixd = pixa.DisplayTiledInRows(1, width, 1.0f, 0, spacing, 0);
        return pixd;
    }
}

/// <summary>
/// Reads from a <see cref="Stream"/> with the semantics of the C fscanf and fgets
/// calls of sel1.c: formats made of whitespace, literal characters and %d
/// (optionally with a width).
/// </summary>
/// <remarks>
/// fscanf reads one character beyond what it consumes and pushes it back
/// (ungetc). That character is kept here between calls. Call
/// <see cref="Release"/> when done: if the stream is seekable, the pushed-back
/// character is given back to it; otherwise it is lost.
/// </remarks>
internal sealed class SelScanner
{
    private const int None = -2;
    private const int Eof = -1;

    private readonly Stream stream;
    private int pending = None;

    public SelScanner(Stream stream)
    {
        this.stream = stream;
    }

    private int Read()
    {
        if (pending != None) {
            int c = pending;
            pending = None;
            return c;
        }
        return stream.ReadByte();
    }

    private void Unread(int c)
    {
        pending = c;
    }

    // Same set as C isspace() in the "C" locale.
    private static bool IsSpace(int c) => c == ' ' || (c >= '\t' && c <= '\r');

    private void SkipSpace()
    {
        int c;
        do {
            c = Read();
        } while (IsSpace(c));
        Unread(c);
    }

    /// <summary>Gives the pushed-back character back to the stream, if that is possible.</summary>
    public void Release()
    {
        if (pending >= 0 && stream.CanSeek)
            stream.Seek(-1, SeekOrigin.Current);
        pending = None;
    }

    /// <summary>
    /// Like fscanf(fp, format, ...) for %d and %Nd conversions only.
    /// </summary>
    /// <param name="format">whitespace matches any amount of whitespace, %d reads an int,
    /// any other character must match exactly</param>
    /// <param name="values">receives the converted values, in order</param>
    /// <returns>number of values assigned; -1 (EOF) if the input ended before the first
    /// conversion</returns>
    public int Scan(string format, Span<int> values)
    {
        int count = 0;
        for (int k = 0; k < format.Length; k++) {
            char f = format[k];
            if (IsSpace(f)) {
                SkipSpace();
            } else if (f == '%') {
                int width = 0;
                while (k + 1 < format.Length && format[k + 1] >= '0' && format[k + 1] <= '9')
                    width = width * 10 + (format[++k] - '0');
                k++;
                if (k >= format.Length || format[k] != 'd')
                    throw new NotSupportedException("SelScanner: only %d is supported");
                int maxChars = width > 0 ? width : int.MaxValue;

                SkipSpace();
                int c = Read();
                if (c == Eof)
                    return count == 0 ? Eof : count;
                int used = 0;
                bool negative = false;
                if (c == '+' || c == '-') {
                    negative = c == '-';
                    used++;
                    if (used >= maxChars)
                        return count;
                    c = Read();
                }
                if (c < '0' || c > '9') {
                    Unread(c);
                    return count;
                }
                long val = c - '0';
                used++;
                while (used < maxChars) {
                    c = Read();
                    if (c < '0' || c > '9') {
                        Unread(c);
                        break;
                    }
                    if (val < (1L << 40))
                        val = val * 10 + (c - '0');
                    used++;
                }
                values[count++] = unchecked((int)(negative ? -val : val));
            } else {
                int c = Read();
                if (c != f) {
                    Unread(c);
                    return count == 0 && c == Eof ? Eof : count;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Like fgets(buf, size, fp): reads up to size - 1 characters, stopping after a newline.
    /// Returns null if the end of the file is reached before any character is read.
    /// </summary>
    public string? Gets(int size)
    {
        MemoryStream line = new MemoryStream();
        while (line.Length < size - 1) {
            int c = Read();
            if (c == Eof) {
                Unread(Eof);
                break;
            }
            line.WriteByte((byte)c);
            if (c == '\n')
                break;
        }
        if (line.Length == 0)
            return null;
        return Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length);
    }
}
