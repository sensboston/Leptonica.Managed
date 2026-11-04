using System;

namespace Leptonica;

/*
 *  pixtiling.c: Splitting an image into tiles and painting tiles back.
 *
 *      PixTiling.Create (pixTilingCreate)
 *      PixTiling.GetCount (pixTilingGetCount), GetSize (pixTilingGetSize)
 *      PixTiling.GetTile (pixTilingGetTile)
 *      PixTiling.NoStripOnPaint (pixTilingNoStripOnPaint)
 *      PixTiling.PaintTile (pixTilingPaintTile)
 *
 *  Not ported: pixTilingDestroy (memory management only).
 *
 *  This provides a simple way to split an image into tiles
 *  and to perform operations independently on each tile.
 *
 *  The tile created with pixTilingGetTile() can have pixels in
 *  adjacent tiles for computation.  The number of extra pixels
 *  on each side of the tile is given by an 'overlap' parameter
 *  to pixTilingCreate().  For tiles at the boundary of
 *  the input image, quasi-overlap pixels are created by reflection
 *  symmetry into the tile.
 *
 *  Here's a typical intended usage.  Suppose you want to parallelize
 *  the operation on an image, by operating on tiles.  For each
 *  tile, you want to generate an in-place image result at the same
 *  resolution.  Suppose you choose a one-dimensional vertical tiling,
 *  where the desired tile width is 256 pixels and the overlap is
 *  30 pixels on left and right sides:
 *
 *    Pix pixd = Pix.CreateTemplate(pixs);  // output
 *    PixTiling pt = PixTiling.Create(pixs, 0, 1, 256, 30, 0);
 *    pt.GetCount(out int nx, out _);
 *    for (j = 0; j < nx; j++) {
 *        Pix pixt = pt.GetTile(0, j);
 *        SomeInPlaceOperation(pixt, 30, 0, ...);
 *        pt.PaintTile(pixd, 0, j, pixt);
 *    }
 *
 *  In this example, note the following:
 *   ~ The unspecfified in-place operation could instead generate
 *     a new pix.  If this is done, the resulting pix must be the
 *     same size as pixt, because pixTilingPaintTile() makes that
 *     assumption, removing the overlap pixels before painting
 *     into the destination.
 *   ~ The 'overlap' parameters have been included in your function,
 *     to indicate which pixels are not in the exterior overlap region.
 *     You will need to change only pixels that are not in the overlap
 *     region, because those are the pixels that will be painted
 *     into the destination.
 *   ~ For tiles on the outside of the image, mirrored pixels are
 *     added to substitute for the overlap that is added to interior
 *     tiles.  This allows you to implement your function without
 *     reference to which tile it is; no special coding is necessary
 *     for pixels that are near the image boundary.
 *   ~ The tiles are labeled by (i, j) = (row, column),
 *     and in this example there is one row and nx columns.
 */

/// <summary>Tiling of a pix (struct PIXTILING).</summary>
public sealed partial class PixTiling
{
    private readonly Pix pix;     /* input pix (a clone) */
    private readonly int nx;      /* number of tiles horizontally */
    private readonly int ny;      /* number of tiles vertically */
    private readonly int w;       /* tile width */
    private readonly int h;       /* tile height */
    private readonly int xoverlap;  /* overlap on left and right */
    private readonly int yoverlap;  /* overlap on top and bottom */
    private bool strip;           /* strip tile overlap when painting */

    private PixTiling(Pix pix, int nx, int ny, int w, int h, int xoverlap, int yoverlap)
    {
        this.pix = pix;
        this.nx = nx;
        this.ny = ny;
        this.w = w;
        this.h = h;
        this.xoverlap = xoverlap;
        this.yoverlap = yoverlap;
        strip = true;
    }

    /// <summary>Overlap into neighboring tiles on each side.</summary>
    public int XOverlap => xoverlap;

    /// <summary>Overlap into neighboring tiles above and below.</summary>
    public int YOverlap => yoverlap;

    // leptonica: pixTilingCreate (pixtiling.c)
    /// <summary>Creates a tiling of a pix.</summary>
    /// <param name="pixs">pix to be tiled; any depth; colormap OK</param>
    /// <param name="nx">number of tiles across image; use 0 if &quot;w&quot; is given</param>
    /// <param name="ny">number of tiles down image; use 0 if &quot;h&quot; is given</param>
    /// <param name="w">desired width of each tile; use 0 if &quot;nx&quot; is given</param>
    /// <param name="h">desired height of each tile; use 0 if &quot;ny&quot; is given</param>
    /// <param name="xoverlap">overlap into neighboring tiles on each side</param>
    /// <param name="yoverlap">overlap into neighboring tiles above and below</param>
    /// <remarks>
    /// (1) We put a clone of pixs in the PixTiling.
    /// (2) The input to pixTilingCreate() for horizontal tiling can be
    ///     either the number of tiles across the image or the approximate
    ///     width of the tiles.  If the latter, the actual width will be
    ///     determined by making all tiles but the last of equal width, and
    ///     making the last as close to the others as possible.  The same
    ///     consideration is applied independently to the vertical tiling.
    ///     To specify tile width, set nx = 0; to specify the number of
    ///     tiles horizontally across the image, set w = 0.
    /// (3) If pixs is to be tiled in one-dimensional strips, use ny = 1 for
    ///     vertical strips and nx = 1 for horizontal strips.
    /// (4) The overlap must not be larger than the width or height of
    ///     the leftmost or topmost tile(s).
    /// </remarks>
    public static PixTiling Create(Pix pixs, int nx, int ny, int w, int h, int xoverlap, int yoverlap)
    {
        const string func = "pixTilingCreate";
        if (nx < 1 && w < 1)
            throw Lept.Arg(func, "invalid width spec");
        if (ny < 1 && h < 1)
            throw Lept.Arg(func, "invalid height spec");

            /* Find the tile width and number of tiles.  All tiles except the
             * rightmost ones have the same width.  The width of the
             * rightmost ones are at least the width of the others and
             * less than twice that width.  Ditto for tile height. */
        pixs.GetDimensions(out int width, out int height, out _);
        if (nx == 0)
            nx = Math.Max(1, width / w);
        w = width / nx;  /* possibly reset */
        if (ny == 0)
            ny = Math.Max(1, height / h);
        h = height / ny;  /* possibly reset */
        if (xoverlap > w || yoverlap > h) {
            Log.Info(func, CFormat.Sprintf("tile width = %d, tile height = %d", w, h));
            throw Lept.Arg(func, "overlap too large");
        }

        return new PixTiling(Pix.Clone(pixs), nx, ny, w, h, xoverlap, yoverlap);
    }

    // leptonica: pixTilingGetCount (pixtiling.c)
    /// <param name="nx">number of tiles across the image</param>
    /// <param name="ny">number of tiles down the image</param>
    public void GetCount(out int nx, out int ny)
    {
        nx = this.nx;
        ny = this.ny;
    }

    // leptonica: pixTilingGetSize (pixtiling.c)
    /// <param name="w">tile width</param>
    /// <param name="h">tile height</param>
    public void GetSize(out int w, out int h)
    {
        w = this.w;
        h = this.h;
    }

    // leptonica: pixTilingGetTile (pixtiling.c)
    /// <summary>Returns a tile with appropriate boundary (overlap) pixels added.</summary>
    /// <param name="i">tile row index</param>
    /// <param name="j">tile column index</param>
    public Pix GetTile(int i, int j)
    {
        const string func = "pixTilingGetTile";
        Pix pixs = pix;
        GetCount(out int nx, out int ny);
        if (i < 0 || i >= ny)
            throw Lept.Arg(func, "invalid row index i");
        if (j < 0 || j >= nx)
            throw Lept.Arg(func, "invalid column index j");

            /* Grab the tile with as much overlap as exists within the
             * input pix.   First, compute the (left, top) coordinates.  */
        pixs.GetDimensions(out int wpix, out int hpix, out _);
        GetSize(out int wt, out int ht);
        int xoverlap = this.xoverlap;
        int yoverlap = this.yoverlap;
        int wtlast = wpix - wt * (nx - 1);
        int htlast = hpix - ht * (ny - 1);
        int left = Math.Max(0, j * wt - xoverlap);
        int top = Math.Max(0, i * ht - yoverlap);

            /* Get the width and height of the tile, including whatever
             * overlap is available. */
        int width, height;
        if (nx == 1)
            width = wpix;
        else if (j == 0)
            width = wt + xoverlap;
        else if (j == nx - 1)
            width = wtlast + xoverlap;
        else
            width = wt + 2 * xoverlap;

        if (ny == 1)
            height = hpix;
        else if (i == 0)
            height = ht + yoverlap;
        else if (i == ny - 1)
            height = htlast + yoverlap;
        else
            height = ht + 2 * yoverlap;
        Box box = Box.Create(left, top, width, height);
        Pix pixt = pixs.ClipRectangle(box, out _)!;

            /* If no overlap, do not add any special case borders */
        if (xoverlap == 0 && yoverlap == 0)
            return pixt;

            /* Add overlap as a mirrored border, in the 8 special cases where
             * the tile touches the border of the input pix.  The xtratop (etc)
             * parameters are required where the tile is either full width
             * or full height.  */
        int xtratop = 0, xtrabot = 0, xtraleft = 0, xtraright = 0;
        if (nx == 1)
            xtraleft = xtraright = xoverlap;
        if (ny == 1)
            xtratop = xtrabot = yoverlap;
        Pix pixd;
        if (i == 0 && j == 0)
            pixd = pixt.AddMirroredBorder(xoverlap, xtraright,
                                          yoverlap, xtrabot);
        else if (i == 0 && j == nx - 1)
            pixd = pixt.AddMirroredBorder(xtraleft, xoverlap,
                                          yoverlap, xtrabot);
        else if (i == ny - 1 && j == 0)
            pixd = pixt.AddMirroredBorder(xoverlap, xtraright,
                                          xtratop, yoverlap);
        else if (i == ny - 1 && j == nx - 1)
            pixd = pixt.AddMirroredBorder(xtraleft, xoverlap,
                                          xtratop, yoverlap);
        else if (i == 0)
            pixd = pixt.AddMirroredBorder(0, 0, yoverlap, xtrabot);
        else if (i == ny - 1)
            pixd = pixt.AddMirroredBorder(0, 0, xtratop, yoverlap);
        else if (j == 0)
            pixd = pixt.AddMirroredBorder(xoverlap, xtraright, 0, 0);
        else if (j == nx - 1)
            pixd = pixt.AddMirroredBorder(xtraleft, xoverlap, 0, 0);
        else
            pixd = Pix.Clone(pixt);

        return pixd;
    }

    // leptonica: pixTilingNoStripOnPaint (pixtiling.c)
    /// <remarks>
    /// (1) The default for paint is to strip out the overlap pixels
    ///     that are added by pixTilingGetTile().  However, some
    ///     operations will generate an image with these pixels
    ///     stripped off.  This tells the paint operation not
    ///     to strip the added boundary pixels when painting.
    /// </remarks>
    public void NoStripOnPaint()
    {
        strip = false;
    }

    // leptonica: pixTilingPaintTile (pixtiling.c)
    /// <summary>Paints a tile onto the destination, without overlap.</summary>
    /// <param name="pixd">dest: paint tile onto this, without overlap</param>
    /// <param name="i">tile row index</param>
    /// <param name="j">tile column index</param>
    /// <param name="pixs">source: tile to be painted from</param>
    public void PaintTile(Pix pixd, int i, int j, Pix pixs)
    {
        const string func = "pixTilingPaintTile";
        if (i < 0 || i >= ny)
            throw Lept.Arg(func, "invalid row index i");
        if (j < 0 || j >= nx)
            throw Lept.Arg(func, "invalid column index j");

            /* Strip added border pixels off if requested */
        pixs.GetDimensions(out int pw, out int ph, out _);
        if (strip) {
            pixd.Rasterop(j * w, i * h,
                          pw - 2 * xoverlap, ph - 2 * yoverlap, L.PIX_SRC,
                          pixs, xoverlap, yoverlap);
        } else {
            pixd.Rasterop(j * w, i * h, pw, ph, L.PIX_SRC, pixs, 0, 0);
        }
    }
}
