using System;
using System.IO;
using System.Text;

namespace Leptonica;

/*
 *  pixafunc2.c: Pixa and Pixaa display, conversion of all pix, constrained
 *  selection, generation of a pixa from tiled images, N-up tiling.
 *
 *  Twelve pixaDisplay*() methods tile a pixa in a pix.  Some work for 1 bpp
 *  input; others for any input depth.  Some give an output depth that depends
 *  on the input depth; others give a different output depth or allow you to
 *  choose it.  Some use a boxes to determine where each pix goes; others tile
 *  onto a regular lattice; others tile onto an irregular lattice; one uses an
 *  associated index array to determine which column each pix goes into.
 *
 *    Display()
 *        This uses the boxes in the pixa to lay out each pix.  This
 *        can be used to reconstruct a pix that has been broken into
 *        components, if the boxes represents the positions of the
 *        components in the original image.
 *    DisplayRandomCmap()
 *        This also uses the boxes to lay out each pix.  However, it creates
 *        a colormapped dest, where each 1 bpp pix is given a randomly
 *        generated color (up to 256 are used).
 *    DisplayLinearly()
 *        This puts each pix, sequentially, in a line, either horizontally
 *        or vertically.
 *    DisplayOnLattice()
 *        This puts each pix, sequentially, onto a regular lattice,
 *        omitting any pix that are too big for the lattice size.
 *        This is useful, for example, to store bitmapped fonts,
 *        where all the characters are stored in a single image.
 *    DisplayUnsplit()
 *        This lays out a mosaic of tiles (the pix in the pixa) that
 *        are all of equal size.  (Don't use this for unequal sized pix!)
 *        For example, it can be used to invert the action of
 *        pixaSplitPix().
 *    DisplayTiled()
 *        Like pixaDisplayOnLattice(), this places each pix on a regular
 *        lattice, but here the lattice size is determined by the
 *        largest component, and no components are omitted.  This is
 *        dangerous if there are thousands of small components and
 *        one or more very large one, because the size of the resulting
 *        pix can be huge!
 *    DisplayTiledInRows()
 *        This puts each pix down in a series of rows, where the upper
 *        edges of each pix in a row are aligned and there is a uniform
 *        spacing between the pix.  The height of each row is determined
 *        by the tallest pix that was put in the row.  This function
 *        is a reasonably efficient way to pack the subimages.
 *        A boxa of the locations of each input pix is stored in the output.
 *    DisplayTiledInColumns()
 *        This puts each pix down in a series of rows, each row having
 *        a specified number of pix.  The upper edges of each pix in a
 *        row are aligned and there is a uniform spacing between the pix.
 *        The height of each row is determined by the tallest pix that
 *        was put in the row.  A boxa of the locations of each input
 *        pix is stored in the output.
 *    DisplayTiledAndScaled()
 *        This scales each pix to a given width and output depth, and then
 *        tiles them in rows with a given number placed in each row.
 *        This is useful for presenting a sequence of images that can be
 *        at different resolutions, but which are derived from the same
 *        initial image.
 *    DisplayTiledWithText()
 *        This is a version of pixaDisplayTiledInRows() that prints, below
 *        each pix, the text in the pix text field.  It renders a pixa
 *        to an image with white background that does not exceed a
 *        given value in width.
 *    DisplayTiledByIndex()
 *        This scales each pix to a given width and output depth,
 *        and then tiles them in columns corresponding to the value
 *        in an associated numa.  All pix with the same index value are
 *        rendered in the same column.  Text in the pix text field are
 *        rendered below the pix.
 *
 *  To render mosaics of images in a pixaa, display functions are
 *  provided that handle situations where the images are all scaled to
 *  the same size, or the number of images on each row needs to vary.
 *
 *  Differences from C:
 *    - Functions that return NULL or an error code in C throw instead.
 *    - Optional output pointers are out parameters.
 *    - pixaDisplay*() that store a serialized boxa in the text field of
 *      the result write it as ascii (see Boxa.WriteMem()).
 *    - The stdio and directory based functions (pixaSplitIntoFiles,
 *      convertToNUpFiles) use the temp directory functions of utils2.c.
 */

/// <summary>Constants from imageio.h used by pixafunc2.c.</summary>
public static partial class L
{
    /* Pdf encoding types (imageio.h) */
    /// <summary>Encode the pdf image data with CCITT Group 4 compression.</summary>
    public const int L_G4_ENCODE = 2;
}

public sealed partial class Pixa
{
    /// <summary>pixaGetBoxGeometry() for the callers that ignore its error return in C: zeros on failure.</summary>
    internal bool TryGetBoxGeometry(int index, out int x, out int y, out int w, out int h)
    {
        try {
            GetBoxGeometry(index, out x, out y, out w, out h);
            return true;
        } catch (ArgumentException) {
            x = y = w = h = 0;
            return false;
        }
    }

    /*---------------------------------------------------------------------*
     *                               Pixa Display                          *
     *---------------------------------------------------------------------*/
    // leptonica: pixaDisplay (pixafunc2.c)
    /// <param name="w">if set to 0, the size is determined from the bounding box of the components in pixa</param>
    /// <param name="h">if set to 0, the size is determined from the bounding box of the components in pixa</param>
    /// <remarks>
    /// (1) This uses the boxes to place each pix in the rendered composite.
    /// (2) Set w = h = 0 to use the b.b. of the components to determine
    ///     the size of the returned pix.
    /// (3) Uses the first pix in pixa to determine the depth.
    /// (4) The background is written "white".  On 1 bpp, each successive
    ///     pix is "painted" (adding foreground), whereas for grayscale
    ///     or color each successive pix is blitted with just the src.
    /// (5) If the pixa is empty, returns an empty 1 bpp pix.
    /// </remarks>
    public Pix Display(int w, int h)
    {
        const string func = "pixaDisplay";

        int n = Count;
        if (n == 0 && w == 0 && h == 0)
            throw Lept.Arg(func, "no components; no size");
        if (n == 0) {
            Log.Warning(func, "no components; returning empty 1 bpp pix");
            return Pix.Create(w, h, 1);
        }

            /* If w and h not input, determine the minimum size required
             * to contain the origin and all c.c. */
        if (w == 0 || h == 0) {
            Boxa boxa = GetBoxa(L.L_CLONE);
            boxa.GetExtent(out w, out h, out _);
            if (w == 0 || h == 0)
                throw Lept.Arg(func, "no associated boxa");
        }

            /* Use the first pix in pixa to determine depth and resolution  */
        Pix pix1 = GetPix(0, L.L_CLONE);
        int d = pix1.Depth;
        int res = pix1.XRes;

        Pix pixd = Pix.Create(w, h, d);
        pixd.SetResolution(res, res);
        if (d > 1)
            pixd.SetAll();
        for (int i = 0; i < n; i++) {
            if (!TryGetBoxGeometry(i, out int xb, out int yb, out int wb, out int hb)) {
                Log.Warning(func, "no box found!");
                continue;
            }
            pix1 = GetPix(i, L.L_CLONE);
            if (d == 1)
                pixd.Rasterop(xb, yb, wb, hb, L.PIX_PAINT, pix1, 0, 0);
            else
                pixd.Rasterop(xb, yb, wb, hb, L.PIX_SRC, pix1, 0, 0);
        }

        return pixd;
    }

    // leptonica: pixaDisplayRandomCmap (pixafunc2.c)
    /// <summary>Displays 1 bpp regions in random colors, in an 8 bpp colormapped pix.</summary>
    /// <param name="w">if set to 0, the size is determined from the bounding box of the components in pixa</param>
    /// <param name="h">if set to 0, the size is determined from the bounding box of the components in pixa</param>
    /// <returns>8 bpp, cmapped, with random colors assigned to each region</returns>
    /// <remarks>
    /// (1) This uses the boxes to place each pix in the rendered composite.
    ///     The fg of each pix in this pixa, such as a single connected
    ///     component or a line of text, is given a random color.
    /// (2) By default, the background color is black (cmap index 0).
    ///     This can be changed by pixcmapResetColor()
    /// </remarks>
    public Pix DisplayRandomCmap(int w, int h)
    {
        const string func = "pixaDisplayRandomCmap";

        int n = Count;
        if (n == 0)
            throw Lept.Arg(func, "no components");
        VerifyDepth(out _, out int maxd);
        if (maxd > 1)
            throw Lept.Arg(func, "not all components are 1 bpp");

            /* If w and h are not input, determine the minimum size required
             * to contain the origin and all c.c. */
        if (w == 0 || h == 0) {
            Boxa boxa = GetBoxa(L.L_CLONE);
            boxa.GetExtent(out w, out h, out _);
        }

            /* Set up an 8 bpp dest pix, with a colormap with 254 random colors */
        Pix pixd = Pix.Create(w, h, 8);
        PixColormap cmap = PixColormap.CreateRandom(8, true, true);
        pixd.SetColormap(cmap);

            /* Color each component and blit it in */
        int res = 0;
        for (int i = 0; i < n; i++) {
            int index = 1 + (i % 254);
            TryGetBoxGeometry(i, out int xb, out int yb, out int wb, out int hb);
            Pix pixs = GetPix(i, L.L_CLONE);
            if (i == 0) res = pixs.XRes;
            Pix pix1 = Pix.Convert1To8(null, pixs, 0, (byte)index);
            pixd.Rasterop(xb, yb, wb, hb, L.PIX_PAINT, pix1, 0, 0);
        }

        pixd.SetResolution(res, res);
        return pixd;
    }

    // leptonica: pixaDisplayLinearly (pixafunc2.c)
    /// <param name="direction">L_HORIZ or L_VERT</param>
    /// <param name="scalefactor">applied to every pix; use 1.0 for no scaling</param>
    /// <param name="background">0 for white, 1 for black; this is the color of the spacing
    ///                          between the images (not used)</param>
    /// <param name="spacing">between images, and on outside</param>
    /// <param name="border">width of black border added to each image; use 0 for no border</param>
    /// <param name="pboxa">location of images in output pix</param>
    /// <returns>pix of composite images</returns>
    /// <remarks>
    /// (1) This puts each pix, sequentially, in a line, either horizontally
    ///     or vertically.
    /// (2) If any pix has a colormap, all pix are rendered in rgb.
    /// (3) The boxa gives the location of each image.
    /// </remarks>
    public Pix DisplayLinearly(int direction, float scalefactor, int background,
                               int spacing, int border, out Boxa? pboxa)
    {
        const string func = "pixaDisplayLinearly";

        pboxa = null;
        if (direction != L.L_HORIZ && direction != L.L_VERT)
            throw Lept.Arg(func, "invalid direction");

            /* Make sure all pix are at the same depth */
        Pixa pixa1 = ConvertToSameDepth();
        pixa1.GetDepthInfo(out int depth, out _);

            /* Scale and add border if requested */
        int n = pixa1.Count;
        Pixa pixa2 = Create(n);
        int bordval = (depth == 1) ? 1 : 0;
        int x = 0, y = 0;
        for (int i = 0; i < n; i++) {
            Pix? pix1 = pixa1.GetPixOrNull(i);
            if (pix1 == null) {
                Log.Warning(func, $"missing pix at index {i}");
                continue;
            }

            Pix pix2;
            if (scalefactor != 1.0)
                pix2 = pix1.Scale(scalefactor, scalefactor);
            else
                pix2 = Pix.Clone(pix1);
            Pix pix3;
            if (border != 0)
                pix3 = pix2.AddBorder(border, (uint)bordval);
            else
                pix3 = Pix.Clone(pix2);

            pix3.GetDimensions(out int w, out int h, out _);
            Box box = Box.Create(x, y, w, h);
            if (direction == L.L_HORIZ)
                x += w + spacing;
            else  /* vertical */
                y += h + spacing;
            pixa2.AddPix(pix3, L.L_INSERT);
            pixa2.AddBox(box, L.L_INSERT);
        }
        Pix pixd = pixa2.Display(0, 0);

        pboxa = pixa2.GetBoxa(L.L_COPY);
        return pixd;
    }

    // leptonica: pixaDisplayOnLattice (pixafunc2.c)
    /// <param name="cellw">lattice cell width</param>
    /// <param name="cellh">lattice cell height</param>
    /// <param name="pncols">number of columns in output lattice</param>
    /// <param name="pboxa">location of images in lattice</param>
    /// <returns>pix of composite images</returns>
    /// <remarks>
    /// (1) This places each pix on sequentially on a regular lattice
    ///     in the rendered composite.  If a pix is too large to fit in the
    ///     allocated lattice space, it is not rendered.
    /// (2) If any pix has a colormap, all pix are rendered in rgb.
    /// (3) This is useful when putting bitmaps of components,
    ///     such as characters, into a single image.
    /// (4) Save the number of tiled images in the text field of the pix,
    ///     in the format: n = %d.  This survives write/read into png files,
    ///     for example.
    /// (5) The boxa gives the location of each image.  The UL corner
    ///     of each image is on a lattice cell corner.  Omitted images
    ///     (due to size) are assigned an invalid width and height of 0.
    /// </remarks>
    public Pix DisplayOnLattice(int cellw, int cellh, out int pncols, out Boxa? pboxa)
    {
        const string func = "pixaDisplayOnLattice";

        pncols = 0;
        pboxa = null;

            /* If any pix have colormaps, or if the depths differ, generate rgb */
        int n = Count;
        if (n == 0)
            throw Lept.Arg(func, "no components");
        bool hascmap = AnyColormaps();
        VerifyDepth(out bool samedepth, out _);
        Pixa pixa1;
        if (hascmap || !samedepth) {
            pixa1 = Create(n);
            for (int i = 0; i < n; i++) {
                Pix pix = GetPix(i, L.L_CLONE);
                Pix pix2 = pix.ConvertTo32();
                pixa1.AddPix(pix2, L.L_INSERT);
            }
        } else {
            pixa1 = Copy(L.L_CLONE);
        }

            /* Have number of rows and columns approximately equal */
        int nw = (int)Math.Sqrt((double)n);
        int nh = (n + nw - 1) / nw;
        int w = cellw * nw;
        int h = cellh * nh;

            /* Use the first pix to determine output depth and resolution  */
        Pix pix1 = pixa1.GetPix(0, L.L_CLONE);
        int d = pix1.Depth;
        int res = pix1.XRes;
        Pix pixd = Pix.Create(w, h, d);
        pixd.SetBlackOrWhite(L.L_SET_WHITE);
        pixd.SetResolution(res, res);
        Boxa boxa = Boxa.Create(n);

            /* Tile the output */
        int index = 0;
        for (int i = 0; i < nh; i++) {
            for (int j = 0; j < nw && index < n; j++, index++) {
                pix1 = pixa1.GetPix(index, L.L_CLONE);
                pix1.GetDimensions(out int wt, out int ht, out _);
                if (wt > cellw || ht > cellh) {
                    Log.Info(func, $"pix({index}) omitted; size {wt}x{ht:x}");
                    Box box = Box.Create(0, 0, 0, 0);
                    boxa.AddBox(box, L.L_INSERT);
                    continue;
                }
                pixd.Rasterop(j * cellw, i * cellh, wt, ht,
                              L.PIX_SRC, pix1, 0, 0);
                Box box1 = Box.Create(j * cellw, i * cellh, wt, ht);
                boxa.AddBox(box1, L.L_INSERT);
            }
        }

            /* Save the number of tiles in the text field */
        pixd.Text = "n = " + boxa.Count;

        pncols = nw;
        pboxa = boxa;
        return pixd;
    }

    // leptonica: pixaDisplayUnsplit (pixafunc2.c)
    /// <param name="nx">number of mosaic cells horizontally</param>
    /// <param name="ny">number of mosaic cells vertically</param>
    /// <param name="borderwidth">of added border on all sides</param>
    /// <param name="bordercolor">in our RGBA format: 0xrrggbbaa</param>
    /// <returns>pix of tiled images</returns>
    /// <remarks>
    /// (1) This is a logical inverse of pixaSplitPix().  It
    ///     constructs a pix from a mosaic of tiles, all of equal size.
    /// (2) For added generality, a border of arbitrary color can
    ///     be added to each of the tiles.
    /// (3) In use, pixa will typically have either been generated
    ///     from pixaSplitPix() or will derived from a pixa that
    ///     was so generated.
    /// (4) All pix in the pixa must be of equal depth, and, if
    ///     colormapped, have the same colormap.
    /// </remarks>
    public Pix DisplayUnsplit(int nx, int ny, int borderwidth, uint bordercolor)
    {
        const string func = "pixaDisplayUnsplit";

        if (nx <= 0 || ny <= 0)
            throw Lept.Arg(func, "nx and ny must be > 0");
        int n = Count;
        if (n == 0)
            throw Lept.Arg(func, "no components");
        if (n != nx * ny)
            throw Lept.Arg(func, "n != nx * ny");
        borderwidth = Math.Max(0, borderwidth);

        GetPixDimensions(0, out int wt, out int ht, out int d);
        int w = nx * (wt + 2 * borderwidth);
        int h = ny * (ht + 2 * borderwidth);

        Pix pixd = Pix.Create(w, h, d);
        Pix pix1 = GetPix(0, L.L_CLONE);
        pixd.CopyColormap(pix1);
        if (borderwidth > 0)
            pixd.SetAllArbitrary(bordercolor);

        int y = borderwidth;
        for (int i = 0, k = 0; i < ny; i++) {
            int x = borderwidth;
            for (int j = 0; j < nx; j++, k++) {
                pix1 = GetPix(k, L.L_CLONE);
                pixd.Rasterop(x, y, wt, ht, L.PIX_SRC, pix1, 0, 0);
                x += wt + 2 * borderwidth;
            }
            y += ht + 2 * borderwidth;
        }

        return pixd;
    }

    // leptonica: pixaDisplayTiled (pixafunc2.c)
    /// <param name="maxwidth">of output image</param>
    /// <param name="background">0 for white, 1 for black</param>
    /// <param name="spacing"></param>
    /// <returns>pix of tiled images</returns>
    /// <remarks>
    /// (1) This renders a pixa to a single image of width not to
    ///     exceed maxwidth, with background color either white or black,
    ///     and with each subimage spaced on a regular lattice.
    /// (2) The lattice size is determined from the largest width and height,
    ///     separately, of all pix in the pixa.
    /// (3) All pix in the pixa must be of equal depth.
    /// (4) If any pix has a colormap, all pix are rendered in rgb.
    /// (5) Careful: because no components are omitted, this is
    ///     dangerous if there are thousands of small components and
    ///     one or more very large one, because the size of the
    ///     resulting pix can be huge!
    /// </remarks>
    public Pix DisplayTiled(int maxwidth, int background, int spacing)
    {
        const string func = "pixaDisplayTiled";

        spacing = Math.Max(spacing, 0);
        int n = Count;
        if (n == 0)
            throw Lept.Arg(func, "no components");

            /* If any pix have colormaps, generate rgb */
        bool hascmap = AnyColormaps();
        Pixa pixa1;
        if (hascmap) {
            pixa1 = Create(n);
            for (int i = 0; i < n; i++) {
                Pix pix = GetPix(i, L.L_CLONE);
                Pix pix2 = pix.ConvertTo32();
                pixa1.AddPix(pix2, L.L_INSERT);
            }
        } else {
            pixa1 = Copy(L.L_CLONE);
        }

            /* Find the max dimensions and depth subimages */
        pixa1.GetDepthInfo(out int d, out bool same);
        if (!same)
            throw Lept.Arg(func, "depths not equal");
        pixa1.SizeRange(out _, out _, out int wmax, out int hmax);

            /* Get the number of rows and columns and the output image size */
        int ncols = (int)((float)(maxwidth - spacing) /
                          (float)(wmax + spacing));
        ncols = Math.Max(ncols, 1);
        int nrows = (n + ncols - 1) / ncols;
        int wd = wmax * ncols + spacing * (ncols + 1);
        int hd = hmax * nrows + spacing * (nrows + 1);
        Pix pixd = Pix.Create(wd, hd, d);

            /* Reset the background color if necessary */
        if ((background == 1 && d == 1) || (background == 0 && d != 1))
            pixd.SetAll();

            /* Blit the images to the dest */
        int res = 0;
        for (int i = 0, ni = 0; i < nrows; i++) {
            int ystart = spacing + i * (hmax + spacing);
            for (int j = 0; j < ncols && ni < n; j++, ni++) {
                int xstart = spacing + j * (wmax + spacing);
                Pix pix1 = pixa1.GetPix(ni, L.L_CLONE);
                if (ni == 0) res = pix1.XRes;
                pix1.GetDimensions(out int wt, out int ht, out _);
                pixd.Rasterop(xstart, ystart, wt, ht, L.PIX_SRC, pix1, 0, 0);
            }
        }
        pixd.SetResolution(res, res);

        return pixd;
    }

    // leptonica: pixaDisplayTiledInRows (pixafunc2.c)
    /// <param name="outdepth">output depth: 1, 8 or 32 bpp</param>
    /// <param name="maxwidth">of output image</param>
    /// <param name="scalefactor">applied to every pix; use 1.0 for no scaling</param>
    /// <param name="background">0 for white, 1 for black; this is the color of the spacing between the images</param>
    /// <param name="spacing">between images, and on outside</param>
    /// <param name="border">width of black border added to each image; use 0 for no border</param>
    /// <returns>pixd of tiled images</returns>
    /// <remarks>
    /// (1) This renders a pixa to a single image of width not to
    ///     exceed maxwidth, with background color either white or black,
    ///     and with each row tiled such that the top of each pix is
    ///     aligned and separated by 'spacing' from the next one.
    ///     A black border can be added to each pix.
    /// (2) All pix are converted to outdepth; existing colormaps are removed.
    /// (3) This does a reasonably spacewise-efficient job of laying
    ///     out the individual pix images into a tiled composite.
    /// (4) A serialized boxa giving the location in pixd of each input
    ///     pix (without added border) is stored in the text string of pixd.
    ///     This allows, e.g., regeneration of a pixa from pixd, using
    ///     pixaCreateFromBoxa().  If there is no scaling and the depth of
    ///     each input pix in the pixa is the same, this tiling operation
    ///     can be inverted using the boxa (except for loss of text in
    ///     each of the input pix):
    ///       Pix pix1 = pixa1.DisplayTiledInRows(1, 1500, 1.0f, 0, 30, 0);
    ///       Boxa boxa1 = Boxa.ReadMem(Encoding.ASCII.GetBytes(pix1.Text));
    ///       Pixa pixa2 = Pixa.CreateFromBoxa(pix1, boxa1, 0, 0, out _);
    /// </remarks>
    public Pix DisplayTiledInRows(int outdepth, int maxwidth, float scalefactor,
                                  int background, int spacing, int border)
    {
        const string func = "pixaDisplayTiledInRows";

        int h;  /* cumulative height over all the rows */
        int w;  /* cumulative height in the current row */
        int irow;  /* index of current pix in current row */
        int wmaxrow;  /* width of the largest row */
        int maxh;  /* max height in row */
        int wt, ht;
        if (outdepth != 1 && outdepth != 8 && outdepth != 32)
            throw Lept.Arg(func, "outdepth not in {1, 8, 32}");
        spacing = Math.Max(spacing, 0);
        border = Math.Max(border, 0);
        if (scalefactor <= 0.0) scalefactor = 1.0f;

        int n = Count;
        if (n == 0)
            throw Lept.Arg(func, "no components");

            /* Normalize depths, scale, remove colormaps; optionally add border */
        Pixa pixan = Create(n);
        int bordval = (outdepth == 1) ? 1 : 0;
        for (int i = 0; i < n; i++) {
            Pix? pix = GetPixOrNull(i);
            if (pix == null)
                continue;

            Pix pixn;
            if (outdepth == 1)
                pixn = pix.ConvertTo1(128);
            else if (outdepth == 8)
                pixn = pix.ConvertTo8(false);
            else  /* outdepth == 32 */
                pixn = pix.ConvertTo32();

            Pix pix1;
            if (scalefactor != 1.0)
                pix1 = pixn.Scale(scalefactor, scalefactor);
            else
                pix1 = Pix.Clone(pixn);
            Pix pixb;
            if (border != 0)
                pixb = pix1.AddBorder(border, (uint)bordval);
            else
                pixb = Pix.Clone(pix1);

            pixan.AddPix(pixb, L.L_INSERT);
        }
        if (pixan.Count != n) {
            n = pixan.Count;
            Log.Warning(func, $"only got {n} components");
            if (n == 0)
                throw Lept.Arg(func, "no components");
        }

            /* Compute parameters for layout */
        Numa nainrow = Numa.Create(0);  /* number of pix in the row */
        Numa namaxh = Numa.Create(0);   /* height of max pix in the row */
        wmaxrow = 0;
        w = h = spacing;
        maxh = 0;  /* max height in row */
        int k;
        for (k = 0, irow = 0; k < n; k++, irow++) {
            pixan.GetPixDimensions(k, out wt, out ht, out _);
            int wtry = w + wt + spacing;
            if (wtry > maxwidth) {  /* end the current row and start next one */
                nainrow.AddNumber(irow);
                namaxh.AddNumber(maxh);
                wmaxrow = Math.Max(wmaxrow, w);
                h += maxh + spacing;
                irow = 0;
                w = wt + 2 * spacing;
                maxh = ht;
            } else {
                w = wtry;
                maxh = Math.Max(maxh, ht);
            }
        }

            /* Enter the parameters for the last row */
        nainrow.AddNumber(irow);
        namaxh.AddNumber(maxh);
        wmaxrow = Math.Max(wmaxrow, w);
        h += maxh + spacing;

        Pix pixd = Pix.Create(wmaxrow, h, outdepth);

            /* Reset the background color if necessary */
        if ((background == 1 && outdepth == 1) ||
            (background == 0 && outdepth != 1))
            pixd.SetAll();

            /* Blit the images to the dest, and save the boxa identifying
             * the image regions that do not include the borders. */
        int nrows = nainrow.Count;
        int y = spacing;
        Boxa boxa = Boxa.Create(n);
        for (int i = 0, index = 0; i < nrows; i++) {  /* over rows */
            int ninrow = nainrow.GetIValue(i);
            maxh = namaxh.GetIValue(i);
            int x = spacing;
            for (int j = 0; j < ninrow; j++, index++) {   /* over pix in row */
                Pix pix = pixan.GetPix(index, L.L_CLONE);
                if (index == 0) {
                    int res = pix.XRes;
                    pixd.SetResolution(res, res);
                }
                pix.GetDimensions(out wt, out ht, out _);
                boxa.AddBox(Box.Create(x + border, y + border,
                    wt - 2 * border, ht - 2 * border), L.L_INSERT);
                pixd.Rasterop(x, y, wt, ht, L.PIX_SRC, pix, 0, 0);
                x += wt + spacing;
            }
            y += maxh + spacing;
        }
        pixd.Text = Encoding.ASCII.GetString(boxa.WriteMem());  /* data is ascii */

        return pixd;
    }
    // leptonica: pixaDisplayTiledInColumns (pixafunc2.c)
    /// <param name="nx">number of columns in output image</param>
    /// <param name="scalefactor">applied to every pix; use 1.0 for no scaling</param>
    /// <param name="spacing">between images, and on outside; can be &lt; 0</param>
    /// <param name="border">width of black border added to each image; use 0 for no border</param>
    /// <returns>pixd of tiled images</returns>
    /// <remarks>
    /// (1) This renders a pixa to a single image with &amp;nx columns of
    ///     subimages.  The background color is white, and each row
    ///     is tiled such that the top of each pix is aligned and
    ///     each pix is separated by 'spacing' from the next one.
    ///     A black border can be added to each pix.
    /// (2) The output depth is determined by the largest depth
    ///     required by the pix in the pixa.  Colormaps are removed.
    /// (3) A serialized boxa giving the location in pixd of each input
    ///     pix (without added border) is stored in the text string of pixd.
    ///     This allows, e.g., regeneration of a pixa from pixd, using
    ///     pixaCreateFromBoxa().  If there is no scaling and the depth of
    ///     each input pix in the pixa is the same, this tiling operation
    ///     can be inverted using the boxa (except for loss of text in
    ///     each of the input pix):
    ///       Pix pix1 = pixa1.DisplayTiledInColumns(3, 1.0f, 30, 2);
    ///       Boxa boxa1 = Boxa.ReadMem(Encoding.ASCII.GetBytes(pix1.Text));
    ///       Pixa pixa2 = Pixa.CreateFromBoxa(pix1, boxa1, 0, 0, out _);
    /// </remarks>
    public Pix DisplayTiledInColumns(int nx, float scalefactor, int spacing, int border)
    {
        const string func = "pixaDisplayTiledInColumns";

        border = Math.Max(border, 0);
        if (scalefactor <= 0.0) scalefactor = 1.0f;
        int n = Count;
        if (n == 0)
            throw Lept.Arg(func, "no components");

            /* Convert to same depth, if necessary */
        Pixa pixa1 = ConvertToSameDepth();
        pixa1.GetDepthInfo(out int maxd, out _);

            /* Scale and optionally add border */
        Pixa pixa2 = Create(n);
        int bordval = (maxd == 1) ? 1 : 0;
        int res = 0;
        for (int i = 0; i < n; i++) {
            Pix? pix1 = pixa1.GetPixOrNull(i);
            if (pix1 == null)
                continue;
            Pix pix2;
            if (scalefactor != 1.0)
                pix2 = pix1.Scale(scalefactor, scalefactor);
            else
                pix2 = Pix.Clone(pix1);
            Pix pix3;
            if (border != 0)
                pix3 = pix2.AddBorder(border, (uint)bordval);
            else
                pix3 = Pix.Clone(pix2);
            if (i == 0) res = pix3.XRes;
            pixa2.AddPix(pix3, L.L_INSERT);
        }
        if (pixa2.Count != n) {
            n = pixa2.Count;
            Log.Warning(func, $"only got {n} components");
            if (n == 0)
                throw Lept.Arg(func, "no components");
        }

            /* Compute layout parameters and save as a boxa */
        Boxa boxa = Boxa.Create(n);
        int nrows = (n + nx - 1) / nx;
        int y = spacing;
        for (int i = 0, index = 0; i < nrows; i++) {
            int x = spacing;
            int maxh = 0;
            for (int j = 0; j < nx && index < n; j++) {
                pixa2.GetPixDimensions(index, out int wb, out int hb, out _);
                Box box = Box.Create(x, y, wb, hb);
                boxa.AddBox(box, L.L_INSERT);
                maxh = Math.Max(maxh, hb + spacing);
                x += wb + spacing;
                index++;
            }
            y += maxh;
        }
        pixa2.SetBoxa(boxa, L.L_INSERT);

            /* Render the output pix */
        boxa.GetExtent(out int w, out int h, out _);
        Pix pixd = pixa2.Display(w + spacing, h + spacing);
        pixd.SetResolution(res, res);

            /* Save the boxa in the text field of the output pix */
        pixd.Text = Encoding.ASCII.GetString(boxa.WriteMem());  /* data is ascii */

        return pixd;
    }

    // leptonica: pixaDisplayTiledAndScaled (pixafunc2.c)
    /// <param name="outdepth">output depth: 1, 8 or 32 bpp</param>
    /// <param name="tilewidth">each pix is scaled to this width</param>
    /// <param name="ncols">number of tiles in each row</param>
    /// <param name="background">0 for white, 1 for black; this is the color of the spacing between the images</param>
    /// <param name="spacing">between images, and on outside</param>
    /// <param name="border">width of additional black border on each image; use 0 for no border</param>
    /// <returns>pix of tiled images</returns>
    /// <remarks>
    /// (1) This can be used to tile a number of renderings of
    ///     an image that are at different scales and depths.
    /// (2) Each image, after scaling and optionally adding the
    ///     black border, has width 'tilewidth'.  Thus, the border does
    ///     not affect the spacing between the image tiles.  The
    ///     maximum allowed border width is tilewidth / 5.
    /// </remarks>
    public Pix DisplayTiledAndScaled(int outdepth, int tilewidth, int ncols,
                                     int background, int spacing, int border)
    {
        const string func = "pixaDisplayTiledAndScaled";

        if (outdepth != 1 && outdepth != 8 && outdepth != 32)
            throw Lept.Arg(func, "outdepth not in {1, 8, 32}");
        if (ncols <= 0)
            throw Lept.Arg(func, "ncols must be > 0");
        spacing = Math.Max(spacing, 0);
        if (border < 0 || border > tilewidth / 5)
            border = 0;
        int n = Count;
        if (n == 0)
            throw Lept.Arg(func, "no components");

            /* Normalize scale and depth for each pix; optionally add border */
        Pixa pixan = Create(n);
        int bordval = (outdepth == 1) ? 1 : 0;
        for (int i = 0; i < n; i++) {
            Pix? pix = GetPixOrNull(i);
            if (pix == null)
                continue;

            pix.GetDimensions(out int w, out int h, out int d);
            float scalefact = (float)(tilewidth - 2 * border) / (float)w;
            Pix pix1;
            if (d == 1 && outdepth > 1 && scalefact < 1.0)
                pix1 = pix.ScaleToGray(scalefact);
            else
                pix1 = pix.Scale(scalefact, scalefact);

            Pix pixn;
            if (outdepth == 1)
                pixn = pix1.ConvertTo1(128);
            else if (outdepth == 8)
                pixn = pix1.ConvertTo8(false);
            else  /* outdepth == 32 */
                pixn = pix1.ConvertTo32();

            Pix pixb;
            if (border != 0)
                pixb = pixn.AddBorder(border, (uint)bordval);
            else
                pixb = Pix.Clone(pixn);

            pixan.AddPix(pixb, L.L_INSERT);
        }
        n = pixan.Count;
        if (n == 0)  /* should not have changed! */
            throw Lept.Arg(func, "no components");

            /* Determine the size of each row and of pixd */
        int wd = tilewidth * ncols + spacing * (ncols + 1);
        int nrows = (n + ncols - 1) / ncols;
        int[] rowht = new int[nrows];
        int maxht = 0;
        int ninrow = 0;
        int irow = 0;
        for (int i = 0; i < n; i++) {
            Pix pix = pixan.GetPix(i, L.L_CLONE);
            ninrow++;
            pix.GetDimensions(out _, out int h, out _);
            maxht = Math.Max(h, maxht);
            if (ninrow == ncols) {
                rowht[irow] = maxht;
                maxht = ninrow = 0;  /* reset */
                irow++;
            }
        }
        if (ninrow > 0) {   /* last fencepost */
            rowht[irow] = maxht;
            irow++;  /* total number of rows */
        }
        nrows = irow;
        int hd = spacing * (nrows + 1);
        for (int i = 0; i < nrows; i++)
            hd += rowht[i];

        Pix pixd = Pix.Create(wd, hd, outdepth);
        if ((background == 1 && outdepth == 1) ||
            (background == 0 && outdepth != 1))
            pixd.SetAll();

            /* Now blit images to pixd */
        int x = spacing, y = spacing;
        irow = 0;
        for (int i = 0; i < n; i++) {
            Pix pix = pixan.GetPix(i, L.L_CLONE);
            if (i == 0) {
                int res = pix.XRes;
                pixd.SetResolution(res, res);
            }
            pix.GetDimensions(out int w, out int h, out _);
            if (i != 0 && ((i % ncols) == 0)) {  /* start new row */
                x = spacing;
                y += spacing + rowht[irow];
                irow++;
            }
            pixd.Rasterop(x, y, w, h, L.PIX_SRC, pix, 0, 0);
            x += tilewidth + spacing;
        }

        return pixd;
    }

    // leptonica: pixaDisplayTiledWithText (pixafunc2.c)
    /// <param name="maxwidth">of output image</param>
    /// <param name="scalefactor">applied to every pix; use 1.0 for no scaling</param>
    /// <param name="spacing">between images, and on outside</param>
    /// <param name="border">width of black border added to each image; use 0 for no border</param>
    /// <param name="fontsize">4, 6, ... 20</param>
    /// <param name="textcolor">0xrrggbb00</param>
    /// <returns>pixd of tiled images</returns>
    /// <remarks>
    /// (1) This is a version of pixaDisplayTiledInRows() that prints, below
    ///     each pix, the text in the pix text field.  Up to 127 chars
    ///     of text in the pix text field are rendered below each pix.
    /// (2) It renders a pixa to a single image of width not to
    ///     exceed %maxwidth, with white background color, with each row
    ///     tiled such that the top of each pix is aligned and separated
    ///     by %spacing from the next one.
    /// (3) All pix are converted to 32 bpp.
    /// (4) This does a reasonably spacewise-efficient job of laying
    ///     out the individual pix images into a tiled composite.
    /// </remarks>
    public Pix DisplayTiledWithText(int maxwidth, float scalefactor, int spacing,
                                    int border, int fontsize, uint textcolor)
    {
        const string func = "pixaDisplayTiledWithText";

        int n = Count;
        if (n == 0)
            throw Lept.Arg(func, "no components");
        if (maxwidth <= 0)
            throw Lept.Arg(func, "invalid maxwidth");
        spacing = Math.Max(spacing, 0);
        border = Math.Max(border, 0);
        if (scalefactor <= 0.0) scalefactor = 1.0f;
        if (fontsize < 4 || fontsize > 20 || (fontsize & 1) != 0) {
            int fsize = Math.Max(Math.Min(fontsize, 20), 4);
            if ((fsize & 1) != 0) fsize--;
            Log.Warning(func, $"changed fontsize from {fontsize} to {fsize}");
            fontsize = fsize;
        }

            /* Be sure the width can accommodate a single column of images */
        SizeRange(out _, out _, out int maxw, out _);
        float wneeded = scalefactor * (maxw + 2 * spacing + 2 * border);
        maxwidth = (int)((maxwidth > wneeded) ? (float)maxwidth : wneeded);  /* L_MAX of an int and a float */

        L_Bmf bmf = L_Bmf.Create(null, fontsize);
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.ConvertTo32();
            Pix pix3 = pix2.AddBorderGeneral(spacing / 2, spacing / 2, spacing / 2,
                                             spacing / 2, 0xffffff00);
            string? textstr = pix1.Text;
            Pix pix4;
            if (textstr != null && textstr.Length > 0) {
                string buf = textstr.Length > 127 ? textstr.Substring(0, 127) : textstr;  /* char buf[128] */
                pix4 = pix3.AddSingleTextblock(bmf, buf, textcolor, L.L_ADD_BELOW, out _);
            } else {
                pix4 = Pix.Clone(pix3);
            }
            pixad.AddPix(pix4, L.L_INSERT);
        }

        Pix pixd = pixad.DisplayTiledInRows(32, maxwidth, scalefactor,
                                            0, spacing, border);
        return pixd;
    }

    // leptonica: pixaDisplayTiledByIndex (pixafunc2.c)
    /// <param name="na">numa with indices corresponding to the pix in pixa</param>
    /// <param name="width">each pix is scaled to this width</param>
    /// <param name="spacing">between images, and on outside</param>
    /// <param name="border">width of black border added to each image; use 0 for no border</param>
    /// <param name="fontsize">4, 6, ... 20</param>
    /// <param name="textcolor">0xrrggbb00</param>
    /// <returns>pixd of tiled images</returns>
    /// <remarks>
    /// (1) This renders a pixa to a single image with white
    ///     background color, where the pix are placed in columns
    ///     given by the index value in the numa.  Each pix
    ///     is separated by %spacing from the adjacent ones, and
    ///     an optional border is placed around them.
    /// (2) Up to 127 chars of text in the pix text field are rendered
    ///     below each pix.  Use newlines in the text field to write
    ///     the text in multiple lines that fit within the pix width.
    /// (3) To avoid having empty columns, if there are N different
    ///     index values, they should be in [0 ... N-1].
    /// (4) All pix are converted to 32 bpp.
    /// </remarks>
    public Pix DisplayTiledByIndex(Numa na, int width, int spacing, int border,
                                   int fontsize, uint textcolor)
    {
        const string func = "pixaDisplayTiledByIndex";

        int n = Count;
        if (n == 0)
            throw Lept.Arg(func, "no pixa components");
        if (n != na.Count)
            throw Lept.Arg(func, "pixa and na counts differ");
        if (width <= 0)
            throw Lept.Arg(func, "invalid width");
        if (width < 20)
            Log.Warning(func, $"very small width: {width}");
        spacing = Math.Max(spacing, 0);
        border = Math.Max(border, 0);
        if (fontsize < 4 || fontsize > 20 || (fontsize & 1) != 0) {
            int fsize = Math.Max(Math.Min(fontsize, 20), 4);
            if ((fsize & 1) != 0) fsize--;
            Log.Warning(func, $"changed fontsize from {fontsize} to {fsize}");
            fontsize = fsize;
        }

            /* The pix will be rendered in the order they occupy in pixa. */
        L_Bmf bmf = L_Bmf.Create(null, fontsize);
        Pixa pixad = Create(n);
        na.GetMax(out float maxindex, out _);
        Numa nay = Numa.MakeConstant(spacing, Utils.RoundFToI(maxindex) + 1);  /* top of the next pix to add in that column */
        for (int i = 0; i < n; i++) {
            int index = na.GetIValue(i);
            int yval = nay.GetIValue(index);
            Pix pix1 = GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.ConvertTo32();
            Pix pix3 = pix2.ScaleToSize(width, 0);
            Pix pix4 = pix3.AddBorderGeneral(border, border, border, border, 0);
            string? textstr = pix1.Text;
            Pix pix5;
            if (textstr != null && textstr.Length > 0) {
                pix5 = pix4.AddTextlines(bmf, textstr, textcolor, L.L_ADD_BELOW);
            } else {
                pix5 = Pix.Clone(pix4);
            }
            pixad.AddPix(pix5, L.L_INSERT);
            int x = spacing + border + index * (2 * border + width + spacing);
            int y = yval;
            pix5.GetDimensions(out int w, out int h, out _);
            yval += h + spacing;
            nay.SetValue(index, yval);
            Box box = Box.Create(x, y, w, h);
            pixad.AddBox(box, L.L_INSERT);
        }

        Pix pixd = pixad.Display(0, 0);
        return pixd;
    }

    /*---------------------------------------------------------------------*
     *                         Pixa pair display                           *
     *---------------------------------------------------------------------*/
    // leptonica: pixaDisplayPairTiledInColumns (pixafunc2.c)
    /// <param name="pixas2">second pixa; this is the first</param>
    /// <param name="nx">number of columns in output image</param>
    /// <param name="scalefactor">applied to every pix; use 1.0 for no scaling</param>
    /// <param name="spacing1">between images within a pair</param>
    /// <param name="spacing2">between image pairs, and on outside</param>
    /// <param name="border1">width of black border added to each image; use 0 for no border</param>
    /// <param name="border2">width of black border added to each image pair; use 0 for no border</param>
    /// <param name="fontsize">to print index below each pair. Valid set is
    ///                        {4,6,8,10,12,14,16,18,20}.  Use 0 to disable.</param>
    /// <param name="startindex">index for the first pair; ignore if %fontsize= 0</param>
    /// <param name="sa">[optional] array of text strings to display</param>
    /// <returns>pixd of tiled images</returns>
    /// <remarks>
    /// (1) This renders a pair of pixa in a single image with &amp;nx columns of
    ///     tiled pairs.  The background color is white, and each row
    ///     is tiled such that the top of each pix is aligned.
    ///     The pix are displayed in pairs, taken from the input pixas.
    ///     Input %pixas1 and %pixas2 must have the same count of pix.
    /// (2) If %fontsize != 0, text is displayed below each pair, and the
    ///     output depth is 32 bpp.  If %sa is defined, the text is taken
    ///     sequentially from %sa; otherwise, an integer is displayed with
    ///     numbers chosen consecutively starting with %startindex.
    /// (3) If %fontsize == 0, the output depth is determined by the largest
    ///     depth required by the pix in the pixa.  Colormaps are removed.
    /// (4) Start with these values and tune for aesthetics:
    ///       %nx = 5, %spacing1 = %spacing2 = 15, %border1 = %border2 = 2,
    ///       %fontsize = 8.
    /// </remarks>
    public Pix DisplayPairTiledInColumns(Pixa pixas2, int nx, float scalefactor,
                                         int spacing1, int spacing2, int border1,
                                         int border2, int fontsize, int startindex,
                                         Sarray? sa)
    {
        const string func = "pixaDisplayPairTiledInColumns";

        Pixa pixas1 = this;
        spacing1 = Math.Max(spacing1, 0);
        spacing2 = Math.Max(spacing2, 0);
        border1 = Math.Max(border1, 0);
        border2 = Math.Max(border2, 0);
        if (scalefactor <= 0.0) scalefactor = 1.0f;
        int n = pixas1.Count;
        if (n == 0)
            throw Lept.Arg(func, "no components");
        if (n != pixas2.Count)
            throw Lept.Arg(func, "pixa sizes differ");
        bool text = fontsize > 0;
        if (text && (fontsize < 4 || fontsize > 20 || (fontsize & 1) != 0)) {
            int fsize = Math.Max(Math.Min(fontsize, 20), 4);
            if ((fsize & 1) != 0) fsize--;
            Log.Warning(func, $"changed fontsize from {fontsize} to {fsize}");
            fontsize = fsize;
        }

            /* Convert to same depth, if necessary */
        int maxd;
        if (text) {  /* adding color text; convert to 32 bpp */
            maxd = 32;
        } else {
            int maxd1 = pixas1.GetRenderingDepth();
            int maxd2 = pixas2.GetRenderingDepth();
            maxd = Math.Max(maxd1, maxd2);
        }

            /* Optionally scale and add borders to each pair;
               then combine the pairs and add outer border.  */
        Pixa pixa1 = Create(n);
        for (int i = 0; i < n; i++) {
            Pix? pixs1 = pixas1.GetPixOrNull(i);
            Pix? pixs2 = pixas2.GetPixOrNull(i);
            if (pixs1 == null || pixs2 == null) continue;
            Pix pix1, pix2;
            if (maxd == 1) {
                pix1 = Pix.Clone(pixs1);
                pix2 = Pix.Clone(pixs2);
            } else if (maxd == 8) {
                pix1 = pixs1.ConvertTo8(false);
                pix2 = pixs2.ConvertTo8(false);
            } else {  /* maxd == 32 */
                pix1 = pixs1.ConvertTo32();
                pix2 = pixs2.ConvertTo32();
            }
            Pix pix3, pix4;
            if (scalefactor != 1.0) {
                pix3 = pix1.Scale(scalefactor, scalefactor);
                pix4 = pix2.Scale(scalefactor, scalefactor);
            } else {
                pix3 = Pix.Clone(pix1);
                pix4 = Pix.Clone(pix2);
            }
            Pix pix5, pix6;
            if (border1 != 0) {
                pix5 = pix3.AddBlackOrWhiteBorder(border1, border1, border1,
                                                  border1, L.L_GET_BLACK_VAL);
                pix6 = pix4.AddBlackOrWhiteBorder(border1, border1, border1,
                                                  border1, L.L_GET_BLACK_VAL);
            } else {
                pix5 = Pix.Clone(pix3);
                pix6 = Pix.Clone(pix4);
            }
            Pix pix7, pix8;
            if (spacing1 != 0) {  /* white border */
                pix7 = pix5.AddBlackOrWhiteBorder(spacing1 / 2, spacing1 / 2,
                                    spacing1 / 2, spacing1 / 2, L.L_GET_WHITE_VAL);
                pix8 = pix6.AddBlackOrWhiteBorder(spacing1 / 2, spacing1 / 2,
                                    spacing1 / 2, spacing1 / 2, L.L_GET_WHITE_VAL);
            } else {
                pix7 = Pix.Clone(pix5);
                pix8 = Pix.Clone(pix6);
            }
            Pixa pixa2 = Create(2);
            pixa2.AddPix(pix7, L.L_INSERT);
            pixa2.AddPix(pix8, L.L_INSERT);
            Pix pix9 = pixa2.DisplayTiledInColumns(2, 1.0f, 0, 0);
            pixa1.AddPix(pix9, L.L_INSERT);
        }

        Pix pixd;
        if (!text) {
            pixd = pixa1.DisplayTiledInColumns(nx, 1.0f, spacing2, border2);
        } else {
            if (sa != null) {
                pixa1.SetText(null, sa);
            } else {
                n = pixa1.Count;
                Numa na = Numa.MakeSequence(startindex, 1, n);
                Sarray sa1 = na.ConvertToSarray(4, 0, 0, L.L_INTEGER_VALUE);
                pixa1.SetText(null, sa1);
            }
            pixa1.SizeRange(out _, out _, out int w, out _);
            pixd = pixa1.DisplayTiledWithText(w * (nx + 1), 1.0f, spacing2,
                                              border2, fontsize, 0xff000000);
        }
        return pixd;
    }

    /*---------------------------------------------------------------------*
     *         Conversion of all pix to specified type (e.g., depth)       *
     *---------------------------------------------------------------------*/
    // leptonica: pixaConvertTo1 (pixafunc2.c)
    /// <param name="thresh">threshold for final binarization from 8 bpp gray</param>
    /// <returns>pixad</returns>
    public Pixa ConvertTo1(int thresh)
    {
        Pixa pixas = this;
        int n = pixas.Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.ConvertTo1(thresh);
            pixad.AddPix(pix2, L.L_INSERT);
        }

        Boxa boxa = pixas.GetBoxa(L.L_COPY);
        pixad.SetBoxa(boxa, L.L_INSERT);
        return pixad;
    }

    // leptonica: pixaConvertTo8 (pixafunc2.c)
    /// <param name="cmapflag">true to give pixd a colormap; false otherwise</param>
    /// <returns>pixad each pix is 8 bpp</returns>
    /// <remarks>(1) See notes for pixConvertTo8(), applied to each pix in pixas.</remarks>
    public Pixa ConvertTo8(bool cmapflag)
    {
        Pixa pixas = this;
        int n = pixas.Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.ConvertTo8(cmapflag);
            pixad.AddPix(pix2, L.L_INSERT);
        }

        Boxa boxa = pixas.GetBoxa(L.L_COPY);
        pixad.SetBoxa(boxa, L.L_INSERT);
        return pixad;
    }

    // leptonica: pixaConvertTo8Colormap (pixafunc2.c)
    /// <param name="dither">1 to dither if necessary; 0 otherwise</param>
    /// <returns>pixad each pix is 8 bpp</returns>
    /// <remarks>(1) See notes for pixConvertTo8Colormap(), applied to each pix in pixas.</remarks>
    public Pixa ConvertTo8Colormap(int dither)
    {
        Pixa pixas = this;
        int n = pixas.Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.ConvertTo8Colormap(dither);
            pixad.AddPix(pix2, L.L_INSERT);
        }

        Boxa boxa = pixas.GetBoxa(L.L_COPY);
        pixad.SetBoxa(boxa, L.L_INSERT);
        return pixad;
    }

    // leptonica: pixaConvertTo32 (pixafunc2.c)
    /// <returns>pixad 32 bpp rgb</returns>
    /// <remarks>
    /// (1) See notes for pixConvertTo32(), applied to each pix in pixas.
    /// (2) This can be used to allow 1 bpp pix in a pixa to be displayed
    ///     with color.
    /// </remarks>
    public Pixa ConvertTo32()
    {
        Pixa pixas = this;
        int n = pixas.Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.ConvertTo32();
            pixad.AddPix(pix2, L.L_INSERT);
        }

        Boxa boxa = pixas.GetBoxa(L.L_COPY);
        pixad.SetBoxa(boxa, L.L_INSERT);
        return pixad;
    }

    /*---------------------------------------------------------------------*
     *                        Pixa constrained selection                   *
     *---------------------------------------------------------------------*/
    // leptonica: pixaConstrainedSelect (pixafunc2.c)
    /// <param name="first">first index to choose; >= 0</param>
    /// <param name="last">biggest possible index to reach;
    ///                    use -1 to go to the end; otherwise, last >= first</param>
    /// <param name="nmax">maximum number of pix to select; > 0</param>
    /// <param name="use_pairs">1 = select pairs of adjacent pix; 0 = select individual pix</param>
    /// <param name="copyflag">L_COPY, L_CLONE</param>
    /// <returns>pixad</returns>
    /// <remarks>
    /// (1) See notes in genConstrainedNumaInRange() for how selection
    ///     is made.
    /// (2) This returns a selection of the pix in the input pixa.
    /// (3) Use copyflag == L_COPY if you don't want changes in the pix
    ///     in the returned pixa to affect those in the input pixa.
    /// </remarks>
    public Pixa ConstrainedSelect(int first, int last, int nmax, int use_pairs, int copyflag)
    {
        const string func = "pixaConstrainedSelect";

        Pixa pixas = this;
        int n = pixas.Count;
        first = Math.Max(0, first);
        last = (last < 0) ? n - 1 : Math.Min(n - 1, last);
        if (last < first)
            throw Lept.Arg(func, "last < first!");
        if (nmax < 1)
            throw Lept.Arg(func, "nmax < 1!");

        Numa na = Numa.GenConstrainedNumaInRange(first, last, nmax, use_pairs);
        int nselect = na.Count;
        Pixa pixad = Create(nselect);
        for (int i = 0; i < nselect; i++) {
            int index = na.GetIValue(i);
            Pix pix1 = pixas.GetPix(index, copyflag);
            pixad.AddPix(pix1, L.L_INSERT);
        }
        return pixad;
    }

    // leptonica: pixaSelectToPdf (pixafunc2.c)
    /// <param name="first">first index to choose; >= 0</param>
    /// <param name="last">biggest possible index to reach;
    ///                    use -1 to go to the end; otherwise, last >= first</param>
    /// <param name="res">override the resolution of each input image, in ppi;
    ///                   use 0 to respect the resolution embedded in the input</param>
    /// <param name="scalefactor">scaling factor applied to each image; > 0.0</param>
    /// <param name="type">encoding type (L_JPEG_ENCODE, L_G4_ENCODE, L_FLATE_ENCODE, or 0 for default</param>
    /// <param name="quality">used for JPEG only; 0 for default (75)</param>
    /// <param name="color">of numbers added to each image (e.g., 0xff000000)</param>
    /// <param name="fontsize">to print number below each image.  The valid set
    ///                        is {4,6,8,10,12,14,16,18,20}.  Use 0 to disable.</param>
    /// <param name="fileout">pdf file of all images</param>
    /// <remarks>
    /// (1) This writes a pdf of the selected images from this pixa, one to
    ///     a page.  They are optionally scaled and annotated with the
    ///     index printed to the left of the image.
    /// (2) If the input images are 1 bpp and you want the numbers to be
    ///     in color, first promote each pix to 8 bpp with a colormap:
    ///           pixa1 = pixas.ConvertTo8(true);
    ///     and then call this function with the specified color
    /// </remarks>
    public void SelectToPdf(int first, int last, int res, float scalefactor,
                            int type, int quality, uint color, int fontsize,
                            string fileout)
    {
        const string func = "pixaSelectToPdf";

        Pixa pixas = this;
        if (type < 0 || type > L.L_FLATE_ENCODE) {
            Log.Warning(func, "invalid compression type; using default");
            type = 0;
        }

            /* Select from given range */
        int n = pixas.Count;
        first = Math.Max(0, first);
        last = (last < 0) ? n - 1 : Math.Min(n - 1, last);
        if (first > last)
            throw Lept.Arg(func, $"first = {first} > last = {last}");
        Pixa pixa1 = pixas.SelectRange(first, last, L.L_CLONE);

            /* Optionally add index numbers */
        L_Bmf? bmf = (fontsize <= 0) ? null : L_Bmf.Create(null, fontsize);
        Pixa pixa2;
        if (bmf != null) {
            Numa na = Numa.MakeSequence(first, 1.0f, last - first + 1);
            pixa2 = pixa1.AddTextNumber(bmf, na, color, L.L_ADD_LEFT);
        } else {
            pixa2 = pixa1.Copy(L.L_CLONE);
        }

        pixa2.ConvertToPdf(res, scalefactor, type, quality, null, fileout);
    }

    /*---------------------------------------------------------------------*
     *                    Generate pixa from tiled images                  *
     *---------------------------------------------------------------------*/
    // leptonica: pixaMakeFromTiledPixa (pixafunc2.c)
    /// <param name="w">width of samples (use 0 for default = 20)</param>
    /// <param name="h">height of samples (use 0 for default = 30)</param>
    /// <param name="nsamp">number of requested samples (use 0 for default = 100)</param>
    /// <returns>pixa of individual, scaled templates</returns>
    /// <remarks>
    /// (1) This converts from a compressed representation of 1 bpp digit
    ///     templates to a pixa where each pix has a single labeled template.
    /// (2) The mosaics hold 100 templates each, and the number of templates
    ///     %nsamp selected for each digit can be between 1 and 100.
    /// (3) Each mosaic has the number of images written in the text field,
    ///     and the i-th pix contains samples of the i-th digit.  That value
    ///     is written into the text field of each template in the output.
    /// </remarks>
    public Pixa MakeFromTiledPixa(int w, int h, int nsamp)
    {
        Pixa pixas = this;
        if (nsamp > 1000)
            throw Lept.Arg("pixaMakeFromTiledPixa", "nsamp too large; typ. 100");

        if (w <= 0) w = 20;
        if (h <= 0) h = 30;
        if (nsamp <= 0) nsamp = 100;

            /* pixas has 10 pix of mosaic'd digits.  Each of these images
             * must be extracted into a pixa of templates, where each template
             * is labeled with the digit value, and then selectively
             * concatenated into an output pixa. */
        Pixa pixad = Create(10 * nsamp);
        for (int i = 0; i < 10; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            int ntiles = pix1.GetTileCount();
            if (nsamp > ntiles)
                Log.Warning("pixaMakeFromTiledPixa", $"requested {nsamp}; only {ntiles} tiles");
            Pixa pixa1 = MakeFromTiledPix(pix1, w, h, 0, nsamp, null);
            pixa1.SetText(i.ToString(), null);
            pixad.Join(pixa1, 0, -1);
        }
        return pixad;
    }

    // leptonica: pixaMakeFromTiledPix (pixafunc2.c)
    /// <param name="pixs">any depth; colormap OK</param>
    /// <param name="w">width of each tile</param>
    /// <param name="h">height of each tile</param>
    /// <param name="start">first tile to use</param>
    /// <param name="num">number of tiles; use 0 to go to the end</param>
    /// <param name="boxa">[optional] location of rectangular regions to be extracted</param>
    /// <remarks>
    /// (1) Operations that generate a pix by tiling from a pixa, and
    ///     the inverse that generate a pixa from tiles of a pix,
    ///     are useful.  One such pair is pixaDisplayUnsplit() and
    ///     pixaSplitPix().  This function is a very simple one that
    ///     generates a pixa from tiles of a pix. There are two cases:
    ///       - the tiles can all be the same size (the inverse of
    ///         pixaDisplayOnLattice(), or
    ///       - the tiles can differ in size, where there is an
    ///         associated boxa (the inverse of pixaCreateFromBoxa().
    /// (2) If all tiles are the same size, %w by %h, use %boxa = null.
    ///     If the tiles differ in size, use %boxa to extract the
    ///     individual images (%w and %h are then ignored).
    /// (3) If the pix was made by pixaDisplayOnLattice(), the number
    ///     of tiled images is written into the text field, in the format
    ///          n = &lt;number&gt;.
    /// (4) Typical usage: a set of character templates all scaled to
    ///     the same size can be stored on a lattice of that size in
    ///     a pix, and this function can regenerate the pixa.  If the
    ///     templates differ in size, a boxa generated when the tiled
    ///     pix was made can be used to indicate the location of
    ///     the templates.
    /// </remarks>
    public static Pixa MakeFromTiledPix(Pix pixs, int w, int h, int start, int num, Boxa? boxa)
    {
        const string func = "pixaMakeFromTiledPix";

        if (boxa == null && (w <= 0 || h <= 0))
            throw Lept.Arg(func, "w and h must be > 0");

        if (boxa != null)  /* general case */
            return CreateFromBoxa(pixs, boxa, start, num, out _);

            /* All tiles are the same size */
        pixs.GetDimensions(out int ws, out int hs, out int d);
        int nx = ws / w;
        int ny = hs / h;
        if (nx < 1 || ny < 1)
            throw Lept.Arg(func, "invalid dimensions");
        if (nx * w != ws || ny * h != hs)
            Log.Warning(func, "some tiles will be clipped");

            /* Check the text field of the pix.  It may tell how many
             * tiles hold valid data.  If a valid value is not found,
             * assume all (nx * ny) tiles are valid.  */
        int n = pixs.GetTileCount();
        bool n_isvalid = (n <= nx * ny && n > nx * (ny - 1)) ? true : false;
        int ntiles = (n_isvalid) ? n : nx * ny;
        int nmax = ntiles - start;  /* max available from start */
        num = (num == 0) ? nmax : Math.Min(num, nmax);

            /* Extract the tiles */
        Pixa pixa1 = Create(num);
        PixColormap? cmap = pixs.Colormap;
        for (int i = 0, k = 0; i < ny; i++) {
            for (int j = 0; j < nx; j++, k++) {
                if (k < start) continue;
                if (k >= start + num) break;
                Pix pix1 = Pix.Create(w, h, d);
                if (cmap != null) pix1.SetColormap(cmap.Copy());
                pix1.Rasterop(0, 0, w, h, L.PIX_SRC, pixs, j * w, i * h);
                pixa1.AddPix(pix1, L.L_INSERT);
            }
        }
        return pixa1;
    }

    /*---------------------------------------------------------------------*
     *                    Pixa display into multiple tiles                 *
     *---------------------------------------------------------------------*/
    // leptonica: pixaDisplayMultiTiled (pixafunc2.c)
    /// <param name="nx">in [1, ... 50], tiling factor horizontally</param>
    /// <param name="ny">in [1, ... 50], tiling factor vertically</param>
    /// <param name="maxw">max width to keep</param>
    /// <param name="maxh">max height to keep</param>
    /// <param name="scalefactor">scale each image by this</param>
    /// <param name="spacing">between images, and on outside</param>
    /// <param name="border">width of additional black border on each image; use 0 for no border</param>
    /// <returns>pixad</returns>
    /// <remarks>
    /// (1) Each set of %nx * %ny images is optionally scaled and saved
    ///     into a new pix, and then aggregated.
    /// (2) Set %maxw = %maxh = 0 if you want to include all pix from %pixs.
    /// (3) This is useful for generating a pdf from the output pixa, where
    ///     each page is a tile of (%nx * %ny) images from the input pixa.
    /// </remarks>
    public Pixa DisplayMultiTiled(int nx, int ny, int maxw, int maxh,
                                  float scalefactor, int spacing, int border)
    {
        const string func = "pixaDisplayMultiTiled";

        Pixa pixas = this;
        if (nx < 1 || ny < 1 || nx > 50 || ny > 50)
            throw Lept.Arg(func, "invalid tiling factor(s)");
        int n = pixas.Count;
        if (n == 0)
            throw Lept.Arg(func, "pixas is empty");

            /* Filter out large ones if requested */
        Pixa pixa1;
        if (maxw == 0 && maxh == 0) {
            pixa1 = pixas.Copy(L.L_CLONE);
        } else {
            maxw = (maxw == 0) ? 1000000 : maxw;
            maxh = (maxh == 0) ? 1000000 : maxh;
            pixa1 = pixas.SelectBySize(maxw, maxh, L.L_SELECT_IF_BOTH,
                                       L.L_SELECT_IF_LTE, out _);
            n = pixa1.Count;
        }

        int ntile = nx * ny;
        int nout = Math.Max(1, (n + ntile - 1) / ntile);
        Pixa pixad = Create(nout);
        for (int i = 0, index = 0; i < nout; i++) {  /* over tiles */
            Pixa pixa2 = Create(ntile);
            for (int j = 0; j < ntile && index < n; j++, index++) {
                Pix pix1 = pixa1.GetPix(index, L.L_COPY);
                pixa2.AddPix(pix1, L.L_INSERT);
            }
            Pix pix2 = pixa2.DisplayTiledInColumns(nx, scalefactor, spacing,
                                                   border);
            pixad.AddPix(pix2, L.L_INSERT);
        }

        return pixad;
    }

    /*---------------------------------------------------------------------*
     *                       Split pixa into files                         *
     *---------------------------------------------------------------------*/
    // leptonica: pixaSplitIntoFiles (pixafunc2.c)
    /// <param name="nsplit">split pixas into this number of pixa; >= 2</param>
    /// <param name="scale">scalefactor applied to each pix</param>
    /// <param name="outwidth">the maxwidth parameter of tiled images for write_pix</param>
    /// <param name="write_pixa">1 to write the split pixa as separate files</param>
    /// <param name="write_pix">1 to write tiled images of the split pixa</param>
    /// <param name="write_pdf">1 to write pdfs of the split pixa</param>
    /// <remarks>
    /// (1) For each requested output, %nsplit files are written into
    ///     directory /tmp/lept/split/.
    /// (2) This is useful when a pixa is so large that the images
    ///     are not conveniently displayed as a single tiled image at
    ///     full resolution.
    /// </remarks>
    public void SplitIntoFiles(int nsplit, float scale, int outwidth,
                               int write_pixa, int write_pix, int write_pdf)
    {
        const string func = "pixaSplitIntoFiles";

        Pixa pixas = this;
        if (nsplit <= 1)
            throw Lept.Arg(func, "nsplit must be >= 2");
        int nt = pixas.Count;
        if (nt == 0)
            throw Lept.Arg(func, "pixas is empty");
        if (write_pixa == 0 && write_pix == 0 && write_pdf == 0)
            throw Lept.Arg(func, "no output is requested");

        Utils.LeptMkdir("lept/split");
        int n = (nt + nsplit - 1) / nsplit;
        Utils.LeptStderr("nt = %d, n = %d, nsplit = %d\n", nt, n, nsplit);
        for (int i = 0, index = 0; i < nsplit; i++) {
            Pixa pixa1 = Create(n);
            for (int j = 0; j < n && index < nt; j++, index++) {
                Pix pix1 = pixas.GetPix(index, L.L_CLONE);
                Pix pix2 = pix1.Scale(scale, scale);
                pixa1.AddPix(pix2, L.L_INSERT);
            }
            if (write_pixa != 0) {
                string buf = $"/tmp/lept/split/split{i + 1}.pa";
                pixa1.WriteDebug(buf);
            }
            if (write_pix != 0) {
                string buf = $"/tmp/lept/split/split{i + 1}.tif";
                Pix pix1 = pixa1.DisplayTiledInRows(1, outwidth, 1.0f, 0, 20, 2);
                pix1.WriteDebug(buf, L.IFF_TIFF_G4);
            }
            if (write_pdf != 0) {
                string buf = $"/tmp/lept/split/split{i + 1}.pdf";
                pixa1.ConvertToPdf(0, 1.0f, L.L_G4_ENCODE, 0, buf, buf);
            }
        }
    }

    /*---------------------------------------------------------------------*
     *                               Tile N-Up                             *
     *---------------------------------------------------------------------*/
    // leptonica: convertToNUpFiles (pixafunc2.c)
    /// <param name="dir">full path to directory of images</param>
    /// <param name="substr">[optional] can be null</param>
    /// <param name="nx">in [1, ... 50], tiling factor horizontally</param>
    /// <param name="ny">in [1, ... 50], tiling factor vertically</param>
    /// <param name="tw">target width, in pixels; must be >= 20</param>
    /// <param name="spacing">between images, and on outside</param>
    /// <param name="border">width of additional black border on each image; use 0 for no border</param>
    /// <param name="fontsize">to print tail of filename with image.  Valid set is
    ///                        {4,6,8,10,12,14,16,18,20}.  Use 0 to disable.</param>
    /// <param name="outdir">subdirectory of /tmp to put N-up tiled images</param>
    /// <remarks>
    /// (1) Each set of %nx * %ny images is scaled and tiled into a single
    ///     image, that is written out to %outdir.
    /// (2) All images in each %nx * %ny set are scaled to the same
    ///     width, %tw.  This is typically used when all images are
    ///     roughly the same size.
    /// (3) This is useful for generating a pdf from the set of input
    ///     files, where each page is a tile of (%nx * %ny) input images.
    ///     Typical values for %nx and %ny are in the range [2 ... 5].
    /// (4) If %fontsize != 0, each image has the tail of its filename
    ///     rendered below it.
    /// </remarks>
    public static void ConvertToNUpFiles(string dir, string? substr, int nx, int ny,
                                         int tw, int spacing, int border,
                                         int fontsize, string outdir)
    {
        const string func = "convertToNUpFiles";

        if (nx < 1 || ny < 1 || nx > 50 || ny > 50)
            throw Lept.Arg(func, "invalid tiling N-factor");
        if (fontsize < 0 || fontsize > 20 || (fontsize & 1) != 0 || fontsize == 2)
            throw Lept.Arg(func, "invalid fontsize");

        Pixa pixa = ConvertToNUpPixa(dir, substr, nx, ny, tw, spacing, border,
                                     fontsize);

        Utils.LeptRmdir(outdir);
        Utils.LeptMkdir(outdir);
        int d = pixa.GetRenderingDepth();
        int format = (d == 1) ? L.IFF_TIFF_G4 : L.IFF_JFIF_JPEG;
        string rootpath = Utils.MakeTempDirname(outdir);
        rootpath = Utils.ModifyTrailingSlash(rootpath, L.L_ADD_TRAIL_SLASH);
        WriteFiles(rootpath, pixa, format);
    }

    // leptonica: convertToNUpPixa (pixafunc2.c)
    /// <param name="dir">full path to directory of images</param>
    /// <param name="substr">[optional] can be null</param>
    /// <param name="nx">in [1, ... 50], tiling factor horizontally</param>
    /// <param name="ny">in [1, ... 50], tiling factor vertically</param>
    /// <param name="tw">target width, in pixels; must be >= 20</param>
    /// <param name="spacing">between images, and on outside</param>
    /// <param name="border">width of additional black border on each image; use 0 for no border</param>
    /// <param name="fontsize">to print tail of filename with image.  Valid set is
    ///                        {4,6,8,10,12,14,16,18,20}.  Use 0 to disable.</param>
    /// <remarks>(1) See notes for convertToNUpFiles()</remarks>
    public static Pixa ConvertToNUpPixa(string dir, string? substr, int nx, int ny,
                                        int tw, int spacing, int border, int fontsize)
    {
        const string func = "convertToNUpPixa";

        if (nx < 1 || ny < 1 || nx > 50 || ny > 50)
            throw Lept.Arg(func, "invalid tiling N-factor");
        if (tw < 20)
            throw Lept.Arg(func, "tw must be >= 20");
        if (fontsize < 0 || fontsize > 20 || (fontsize & 1) != 0 || fontsize == 2)
            throw Lept.Arg(func, "invalid fontsize");

        Sarray sa1 = Sarray.GetSortedPathnamesInDirectory(dir, substr, 0, 0);
        Pixa pixa1 = ReadFilesSA(sa1);
        int n = sa1.Count;
        Sarray sa2 = Sarray.Create(n);
        for (int i = 0; i < n; i++) {
            string fname = sa1.GetString(i, L.L_NOCOPY);
            Utils.SplitPathAtDirectory(fname, out _, out string tail);
            sa2.AddString(tail, L.L_INSERT);
        }
        Pixa pixa2 = pixa1.ConvertToNUpPixa(sa2, nx, ny, tw, spacing,
                                            border, fontsize);
        return pixa2;
    }

    // leptonica: pixaConvertToNUpPixa (pixafunc2.c)
    /// <param name="sa">[optional] array of strings associated with each pix</param>
    /// <param name="nx">in [1, ... 50], tiling factor horizontally</param>
    /// <param name="ny">in [1, ... 50], tiling factor vertically</param>
    /// <param name="tw">target width, in pixels; must be >= 20</param>
    /// <param name="spacing">between images, and on outside</param>
    /// <param name="border">width of additional black border on each image; use 0 for no border</param>
    /// <param name="fontsize">to print string with each image.  Valid set is
    ///                        {4,6,8,10,12,14,16,18,20}.  Use 0 to disable.</param>
    /// <returns>pixad</returns>
    /// <remarks>
    /// (1) This takes an input pixa and an optional array of strings, and
    ///     generates a pixa of NUp tiles from the input, labeled with
    ///     the strings if they exist and %fontsize != 0.
    /// (2) See notes for convertToNUpFiles()
    /// </remarks>
    public Pixa ConvertToNUpPixa(Sarray? sa, int nx, int ny, int tw, int spacing,
                                 int border, int fontsize)
    {
        const string func = "pixaConvertToNUpPixa";

        Pixa pixas = this;
        if (nx < 1 || ny < 1 || nx > 50 || ny > 50)
            throw Lept.Arg(func, "invalid tiling N-factor");
        if (tw < 20)
            throw Lept.Arg(func, "tw must be >= 20");
        if (fontsize < 0 || fontsize > 20 || (fontsize & 1) != 0 || fontsize == 2)
            throw Lept.Arg(func, "invalid fontsize");

        int nt = pixas.Count;
        if (sa != null && (sa.Count != nt)) {
            Log.Warning(func, $"pixa size {nt} not equal to sarray size {sa.Count}");
        }

        int n2 = nx * ny;
        int nout = (nt + n2 - 1) / n2;
        Pixa pixad = Create(nout);
        L_Bmf? bmf = (fontsize == 0) ? null : L_Bmf.Create(null, fontsize);
        for (int i = 0, j = 0; i < nout; i++) {
            Pixa pixa1 = Create(n2);
            for (int k = 0; k < n2 && j < nt; j++, k++) {
                Pix pix1 = pixas.GetPix(j, L.L_CLONE);
                Pix pix2 = pix1.ScaleToSize(tw, 0);  /* all images have width tw */
                Pix pix3;
                if (bmf != null && sa != null) {
                    string str = sa.GetString(j, L.L_NOCOPY);
                    pix3 = pix2.AddTextlines(bmf, str, 0xff000000,
                                             L.L_ADD_BELOW);
                } else {
                    pix3 = Pix.Clone(pix2);
                }
                pixa1.AddPix(pix3, L.L_INSERT);
            }
            if (pixa1.Count == 0)  /* probably won't happen */
                continue;

                /* Add 2 * border to image width to prevent scaling */
            int d = pixa1.GetRenderingDepth();
            Pix pix4 = pixa1.DisplayTiledAndScaled(d, tw + 2 * border, nx, 0,
                                                   spacing, border);
            pixad.AddPix(pix4, L.L_INSERT);
        }

        return pixad;
    }

    /*---------------------------------------------------------------------*
     *            Render two pixa side-by-side for comparison              *
     *---------------------------------------------------------------------*/
    // leptonica: pixaCompareInPdf (pixafunc2.c)
    /// <param name="pixa2">second pixa; this is the first</param>
    /// <param name="nx">in [1, ... 20], tiling factor horizontally</param>
    /// <param name="ny">in [1, ... 20], tiling factor vertically</param>
    /// <param name="tw">target width, in pixels; must be >= 20</param>
    /// <param name="spacing">between images, and on outside</param>
    /// <param name="border">width of additional black border on each image and on each pair; use 0 for no border</param>
    /// <param name="fontsize">to print index of each pair of images.  Valid set
    ///                        is {4,6,8,10,12,14,16,18,20}.  Use 0 to disable.</param>
    /// <param name="fileout">output pdf file</param>
    /// <remarks>
    /// (1) This takes two pixa and renders them interleaved, side-by-side
    ///     in a pdf.  A warning is issued if the input pixa arrays
    ///     have different lengths.
    /// (2) %nx and %ny specify how many side-by-side pairs are displayed
    ///     on each pdf page.  For example, if %nx = 1 and %ny = 2, then
    ///     two pairs are shown, one above the other, on each page.
    /// (3) The input pix are scaled to a target width of %tw, and
    ///     then paired with optional %spacing between and optional
    ///     black border of width %border.
    /// (4) After a pixa is generated of these tiled images, it is
    ///     written to %fileout as a pdf.
    /// (5) Typical numbers for the input parameters are:
    ///       %nx = small integer (1 - 4)
    ///       %ny = 2 * %nx
    ///       %tw = 200 - 500 pixels
    ///       %spacing = 10
    ///       %border = 2
    ///       %fontsize = 10
    /// (6) If %fontsize != 0, the index of the pix pair in their pixa
    ///     is printed out below each pair.
    /// </remarks>
    public void CompareInPdf(Pixa pixa2, int nx, int ny, int tw, int spacing,
                             int border, int fontsize, string fileout)
    {
        const string func = "pixaCompareInPdf";

        Pixa pixa1 = this;
        if (nx < 1 || ny < 1 || nx > 20 || ny > 20)
            throw Lept.Arg(func, "invalid tiling factors");
        if (tw < 20)
            throw Lept.Arg(func, "invalid tw; tw must be >= 20");
        if (fontsize < 0 || fontsize > 20 || (fontsize & 1) != 0 || fontsize == 2)
            throw Lept.Arg(func, "invalid fontsize");
        int n1 = pixa1.Count;
        int n2 = pixa2.Count;
        if (n1 == 0 || n2 == 0)
            throw Lept.Arg(func, "at least one pixa is empty");
        if (n1 != n2)
            Log.Warning(func, $"sizes ({n1}, {n2}) differ; using the minimum in interleave");

            /* Interleave the input pixa */
        Pixa pixa3 = pixa1.Interleave(pixa2, L.L_CLONE);

            /* Scale the images if necessary and pair them up side/by/side */
        Pixa pixa4 = pixa3.ConvertToNUpPixa(null, 2, 1, tw, spacing, border, 0);

            /* Label the pairs and mosaic into pages without further scaling */
        int npairs = pixa4.Count;
        Sarray? sa = (fontsize > 0) ? Sarray.GenerateIntegers(npairs) : null;
        Pixa pixa5 = pixa4.ConvertToNUpPixa(sa, nx, ny,
                                            2 * tw + 4 * border + spacing,
                                            spacing, border, fontsize);

            /* Output as pdf without scaling */
        pixa5.ConvertToPdf(0, 1.0f, 0, 0, null, fileout);
    }
}

public sealed partial class Pix
{
    // leptonica: pixGetTileCount (pixafunc2.c)
    /// <returns>number embedded in the pix text field; 0 if there is none</returns>
    /// <remarks>
    /// (1) If the pix was made by pixaDisplayOnLattice(), the number
    ///     of tiled images is written into the text field, in the format
    ///          n = &lt;number&gt;.
    /// (2) This returns 0 if the data is not in the text field.
    /// </remarks>
    public int GetTileCount()
    {
        int n = 0;
        string? text = Text;
        if (text != null && text.Length > 4) {
            using MemoryStream ms = new MemoryStream(Encoding.ASCII.GetBytes(text), false);
            CScanner sc = new CScanner(ms);
            Span<int> v = stackalloc int[1];
            if (sc.Scan("n = %d", v) == 1)  /* sscanf */
                n = v[0];
        }
        return n;
    }
}

public sealed partial class Pixaa
{
    /*---------------------------------------------------------------------*
     *                              Pixaa Display                          *
     *---------------------------------------------------------------------*/
    // leptonica: pixaaDisplay (pixafunc2.c)
    /// <param name="w">if set to 0, the size is determined from the bounding box of the components in pixa</param>
    /// <param name="h">if set to 0, the size is determined from the bounding box of the components in pixa</param>
    /// <remarks>
    /// (1) Each pix of the paa is displayed at the location given by
    ///     its box, translated by the box of the containing pixa
    ///     if it exists.
    /// </remarks>
    public Pix Display(int w, int h)
    {
        const string func = "pixaaDisplay";

        Pixaa paa = this;
        int n = paa.Count;
        if (n == 0)
            throw Lept.Arg(func, "no components");

            /* If w and h not input, determine the minimum size required
             * to contain the origin and all c.c. */
        Boxa boxa1 = paa.GetBoxa(L.L_CLONE);  /* top-level boxa */
        int nbox = boxa1.Count;
        if (w == 0 || h == 0) {
            if (nbox == n) {
                boxa1.GetExtent(out w, out h, out _);
            } else {  /* have to use the lower-level boxa for each pixa */
                int wmax = 0, hmax = 0;
                for (int i = 0; i < n; i++) {
                    Pixa pixa = paa.GetPixa(i, L.L_CLONE);
                    Boxa boxa = pixa.GetBoxa(L.L_CLONE);
                    boxa.GetExtent(out w, out h, out _);
                    wmax = Math.Max(wmax, w);
                    hmax = Math.Max(hmax, h);
                }
                w = wmax;
                h = hmax;
            }
        }

            /* Get depth from first pix */
        Pixa pixa0 = paa.GetPixa(0, L.L_CLONE);
        Pix pix0 = pixa0.GetPix(0, L.L_CLONE);
        int d = pix0.Depth;

        Pix pixd = Pix.Create(w, h, d);

        int x = 0, y = 0;
        for (int i = 0; i < n; i++) {
            Pixa pixa = paa.GetPixa(i, L.L_CLONE);
            if (nbox == n)
                boxa1.GetBoxGeometry(i, out x, out y, out _, out _);
            int na = pixa.Count;
            for (int j = 0; j < na; j++) {
                pixa.TryGetBoxGeometry(j, out int xb, out int yb, out int wb, out int hb);
                Pix pix1 = pixa.GetPix(j, L.L_CLONE);
                pixd.Rasterop(x + xb, y + yb, wb, hb, L.PIX_PAINT, pix1, 0, 0);
            }
        }

        return pixd;
    }

    // leptonica: pixaaDisplayByPixa (pixafunc2.c)
    /// <param name="maxnx">maximum number of columns for rendering each pixa</param>
    /// <param name="scalefactor">applied to every pix; use 1.0 for no scaling</param>
    /// <param name="hspacing">between images on a row (in the pixa)</param>
    /// <param name="vspacing">between tiles rows, each corresponding to a pixa</param>
    /// <param name="border">width of black border added to each image; use 0 for no border</param>
    /// <returns>pixd of images in this paa, tiled by pixa in row-major order</returns>
    /// <remarks>
    /// (1) This renders a pixaa into a single image.  The pix from each pixa
    ///     are rendered on a row.  If the number of pix in the pixa is
    ///     larger than %maxnx, the pix will be rendered into more than 1 row.
    ///     To insure that each pixa is rendered into one row, use %maxnx
    ///     at least as large as the max number of pix in the pixa.
    /// (2) Each row is tiled such that the top of each pix is aligned and
    ///     each pix is separated by %hspacing from the next one.
    ///     A black border can be added to each pix.
    /// (3) The resulting pix from each row are then rendered vertically,
    ///     separated by %vspacing from each other.
    /// (4) The output depth is determined by the largest depth of all
    ///     the pix in %paa. Colormaps are removed.
    /// </remarks>
    public Pix DisplayByPixa(int maxnx, float scalefactor, int hspacing,
                             int vspacing, int border)
    {
        Pixaa paa = this;
        if (scalefactor <= 0.0) scalefactor = 1.0f;
        if (hspacing < 0) hspacing = 0;
        if (vspacing < 0) vspacing = 0;
        if (border < 0) border = 0;

        int n = paa.Count;
        if (n == 0)
            throw Lept.Arg("pixaaDisplayByPixa", "no components");

            /* Vertical spacing of amount %hspacing is also added at this step */
        Pixa pixa2 = Pixa.Create(0);
        for (int i = 0; i < n; i++) {
            Pixa pixa1 = paa.GetPixa(i, L.L_CLONE);
            Pix pix1 = pixa1.DisplayTiledInColumns(maxnx, scalefactor,
                                                   hspacing, border);
            pixa2.AddPix(pix1, L.L_INSERT);
        }

        int vs = vspacing - 2 * hspacing;
        Pix pix2 = pixa2.DisplayTiledInColumns(1, scalefactor, vs, 0);
        return pix2;
    }

    // leptonica: pixaaDisplayTiledAndScaled (pixafunc2.c)
    /// <param name="outdepth">output depth: 1, 8 or 32 bpp</param>
    /// <param name="tilewidth">each pix is scaled to this width</param>
    /// <param name="ncols">number of tiles in each row</param>
    /// <param name="background">0 for white, 1 for black; this is the color of the spacing between the images</param>
    /// <param name="spacing">between images, and on outside</param>
    /// <param name="border">width of additional black border on each image; use 0 for no border</param>
    /// <returns>pixa of tiled images, one image for each pixa in the paa</returns>
    /// <remarks>
    /// (1) For each pixa, this generates from all the pix a
    ///     tiled/scaled output pix, and puts it in the output pixa.
    /// (2) See comments in pixaDisplayTiledAndScaled().
    /// </remarks>
    public Pixa DisplayTiledAndScaled(int outdepth, int tilewidth, int ncols,
                                      int background, int spacing, int border)
    {
        const string func = "pixaaDisplayTiledAndScaled";

        Pixaa paa = this;
        if (outdepth != 1 && outdepth != 8 && outdepth != 32)
            throw Lept.Arg(func, "outdepth not in {1, 8, 32}");
        if (ncols <= 0)
            throw Lept.Arg(func, "ncols must be > 0");
        if (border < 0 || border > tilewidth / 5)
            border = 0;

        int n = paa.Count;
        if (n == 0)
            throw Lept.Arg(func, "no components");

        Pixa pixad = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pixa pixa = paa.GetPixa(i, L.L_CLONE);
            Pix pix = pixa.DisplayTiledAndScaled(outdepth, tilewidth, ncols,
                                                 background, spacing, border);
            pixad.AddPix(pix, L.L_INSERT);
        }

        return pixad;
    }
}
