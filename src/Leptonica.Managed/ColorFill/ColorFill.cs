using System;

namespace Leptonica;

/*
 *  colorfill.c
 *
 *      Determine color content using proximity.  What can we say about the
 *      color in an image from growing regions with nearly the same color?
 *
 *         L_Colorfill.Create (l_colorfillCreate)
 *
 *         Pix.ColorContentByLocation (pixColorContentByLocation)
 *         Pix.ColorFill (pixColorFill)
 *
 *      Generate data for testing
 *         Pix.MakeColorfillTestData (makeColorfillTestData)
 *
 *      Static helpers
 *         private ColorEl (struct ColorEl, colorelCreate)
 *         private Pix.ColorFillFromSeed (pixColorFillFromSeed)
 *         private Pix.GetVisitedNeighbors (pixGetVisitedNeighbors)
 *         private Pix.FindNextUnvisited (findNextUnvisited)
 *         private Pix.ColorsAreSimilarForFill (colorsAreSimilarForFill)
 *         private Pix.PixelColorIsValid (pixelColorIsValid)
 *         private Pix.PixelIsOnColorBoundary (pixelIsOnColorBoundary)
 *         private L_Colorfill.EvalColorfillData (evalColorfillData)
 *
 *  See colorcontent.c for location-independent measures of the amount
 *  of color in an image.
 *
 *  Differences from C:
 *    - l_colorfillDestroy is not ported (the GC owns the objects).
 *    - pixColorContentByLocation() takes the colorfill as its first argument
 *      and is a static method of Pix.
 */

/// <summary>Colorfill data (struct L_Colorfill).</summary>
public sealed class L_Colorfill
{
    /// <summary>Gets a clone of the input image.</summary>
    public Pix Pixs { get; private set; }

    /// <summary>Gets the optional color-shifted version of the input image.</summary>
    public Pix? Pixst { get; internal set; }

    /// <summary>Gets the number of tiles in each row.</summary>
    public int Nx { get; private set; }

    /// <summary>Gets the number of tiles in each column.</summary>
    public int Ny { get; private set; }

    /// <summary>Gets the width of each tile.</summary>
    public int Tw { get; private set; }

    /// <summary>Gets the height of each tile.</summary>
    public int Th { get; private set; }

    /// <summary>Gets the boxes for the tiles.</summary>
    public Boxa Boxas { get; private set; }

    /// <summary>Gets the images of the tiles.</summary>
    public Pixa? Pixas { get; internal set; }

    /// <summary>Gets the images of the color masks.</summary>
    public Pixa? Pixam { get; internal set; }

    /// <summary>Gets the sizes of the color regions in each tile.</summary>
    public Numaa Naa { get; private set; }

    /// <summary>Gets the colors of the regions in each tile.</summary>
    public L_Dnaa Dnaa { get; private set; }

    /// <summary>Gets the debug output images.</summary>
    public Pixa Pixadb { get; private set; }

    private L_Colorfill(Pix pixs, int nx, int ny, int tw, int th, Boxa boxas, int ntiles)
    {
        Pixs = pixs;
        Nx = nx;
        Ny = ny;
        Tw = tw;
        Th = th;
        Boxas = boxas;
        Naa = Numaa.Create(ntiles);
        Dnaa = L_Dnaa.Create(ntiles);
        Pixadb = Pixa.Create(0);
    }

    /*---------------------------------------------------------------------*
     *                   Colorfill creation and destruction                *
     *---------------------------------------------------------------------*/
    // leptonica: l_colorfillCreate (colorfill.c)
    /// <summary>Creates a colorfill for the image.</summary>
    /// <param name="pixs">input RGB image</param>
    /// <param name="nx">requested number of tiles in each row</param>
    /// <param name="ny">requested number of tiles in each column</param>
    /// <remarks>
    /// (1) Tiles must at least 10 pixels in each dimension.
    /// </remarks>
    public static L_Colorfill Create(Pix pixs, int nx, int ny)
    {
        const string func = "l_colorfillCreate";
        if (pixs.Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");

        pixs.GetDimensions(out int w, out int h, out _);
        int tw = w / nx;
        int th = h / ny;
        if (tw < 10 || th < 10)
            throw Lept.Arg(func, "tile size too small");
        Boxa boxas = Boxa.Create(nx * ny);
        for (int i = 0; i < ny; i++) {
            for (int j = 0; j < nx; j++) {
                Box box = Box.Create(j * tw, i * th, tw, th);
                boxas.AddBox(box, L.L_INSERT);
            }
        }
        int ntiles = nx * ny;

        return new L_Colorfill(pixs, nx, ny, tw, th, boxas, ntiles);
    }

    // leptonica: evalColorfillData (colorfill.c)
    /// <summary>Finds the average color and size of each color region in each tile.</summary>
    /// <param name="debug">show segmented regions with their median color</param>
    internal void EvalColorfillData(int debug)
    {
        int[] tab = Pix.MakePixelSumTab8();
        int n = Nx * Ny;
        for (int i = 0; i < n; i++) {
            Pix pix1 = Pixas!.GetPix(i, L.L_CLONE);
            Pix pixm = Pixam!.GetPix(i, L.L_CLONE);
            pix1.GetDimensions(out int w, out int h, out _);
            pixm.ConnComp(out Pixa pixa1, 4);
            int nc = pixa1.Count;
            Numa na = Numa.Create(0);
            L_Dna da = L_Dna.Create(0);
            Pix? pixdb = (debug != 0) ? Pix.Create(w, h, 32) : null;
            for (int j = 0; j < nc; j++) {
                Pix pix2 = pixa1.GetPix(j, L.L_COPY);
                Box box1 = pixa1.GetBox(j, L.L_COPY)!;
                box1.GetGeometry(out int x, out int y, out _, out _);
                pix1.GetRankValueMaskedRGB(pix2, x, y, 1, 0.5f,
                                           out float rval, out float gval, out float bval);
                Pix.ComposeRGBPixel((int)rval, (int)gval, (int)bval, out uint pixel);

                da.AddNumber(pixel);
                int count = pix2.CountPixels(tab);
                na.AddNumber(count);
                if (debug != 0)
                    pixdb!.PaintThroughMask(pix2, x, y, pixel);
            }
            if (pixdb != null)   /* in C a null pix is reported and not added */
                Pixadb.AddPix(pixdb, L.L_INSERT);
            Naa.AddNuma(na, L.L_INSERT);
            Dnaa.AddDna(da, L.L_INSERT);
        }

        if (debug != 0) {  /* first tile */
            Numa na = Naa.GetNuma(0, L.L_CLONE);
            CStdio.Stderr($"Size of components in tile 0:");
            na.WriteStderr();
        }
    }
}

public sealed partial class Pix
{
        /* Ignore pixels with smaller max component */
    private const int DefaultMinMax = 70;

    private sealed class ColorEl
    {
        public int X;
        public int Y;
        public uint Color;

        // leptonica: colorelCreate (colorfill.c)
        public ColorEl(int x, int y, uint color)
        {
            X = x;
            Y = y;
            Color = color;
        }
    }

    /* ----------------------------------------------------------------------- *
     *    Determine color content using proximity.  What do we get when        *
     *    growing regions with nearly the same color?                          *
     * ----------------------------------------------------------------------- */
    // leptonica: pixColorContentByLocation (colorfill.c)
    /// <summary>Computes color fill information in each tile of the colorfill.</summary>
    /// <param name="cf">colorfill</param>
    /// <param name="rref">reference value for red component</param>
    /// <param name="gref">reference value for green component</param>
    /// <param name="bref">reference value for blue component</param>
    /// <param name="minmax">min of max component for possible color region</param>
    /// <param name="maxdiff">max component diff to be in same color region</param>
    /// <param name="minarea">min number of pixels for a color region</param>
    /// <param name="smooth">low-pass kernel size (1,3,5); use 1 to skip</param>
    /// <param name="debug">generates debug images and fill info</param>
    /// <remarks>
    /// (1) This computes color fill information in each tile, identifying
    ///     regions of approximately constant color.  It does this
    ///     independently for each tile, using flood fills.  Regions
    ///     of low intensity are considered 'not colorful'.
    /// (2) The three numbers (rref, gref and bref) can be thought
    ///     of in two ways:
    ///       (a) as rgb values in the image corresponding to white,
    ///           to compensate for an unbalanced color white point.
    ///       (b) as the median or mean values of the background color
    ///           of a scan.
    ///     The gamma TRC transformation, which does not change hue, is used
    ///     to modify all colors so that these reference values become white.
    ///     These three numbers must either be all 0 or all non-zero.
    ///     To skip the TRC transform, set them all to 0.
    /// (3) If the maximum component after white point correction,
    ///     max(r,g,b), is less than minmax, the pixel color is invalid, and it
    ///     is assigned its neighbor's value in the filling operation.
    ///     Use minmax = 0 for a default value.
    /// </remarks>
    public static void ColorContentByLocation(L_Colorfill cf, int rref, int gref, int bref,
                                              int minmax, int maxdiff, int minarea,
                                              int smooth, int debug)
    {
        if (minmax <= 0) minmax = DefaultMinMax;
        if (minmax > 200)
            throw Lept.Arg("pixColorContentByLocation", "minmax > 200; unreasonably large");

            /* Do the optional linear color map; this checks the ref vals
             * and uses them if valid.  Use {0,0,0} to skip this operation. */
        Pix pix1 = cf.Pixs.ColorShiftWhitePoint(rref, gref, bref);
        cf.Pixst = pix1;

            /* Break the image up into small tiles */
        Pixa pixas = Pixa.CreateFromBoxa(pix1, cf.Boxas, 0, 0, out _);
        cf.Pixas = pixas;

            /* Find regions of similar color in each tile */
        int n = pixas.Count;
        Pixa pixam = Pixa.Create(n);
        cf.Pixam = pixam;
        for (int i = 0; i < n; i++) {
            Pix pix2 = pixas.GetPix(i, L.L_COPY);
            Pix pix3 = pix2.ColorFill(minmax, maxdiff, smooth, minarea, 0);
            pixam.AddPix(pix3, L.L_INSERT);
        }

            /* Evaluate color components.  Find the average color in each
             * component and determine if there is more than one color in
             * each of the tiles. */
        cf.EvalColorfillData(debug);
    }

    // leptonica: pixColorFill (colorfill.c)
    /// <summary>Basic color filling operation: grows 8-connected regions of similar color.</summary>
    /// <param name="minmax">min of max component for possible color region</param>
    /// <param name="maxdiff">max component diff to be in same color region</param>
    /// <param name="smooth">low-pass kernel size (1,3,5); use 1 to skip</param>
    /// <param name="minarea">min number of pixels for a color region</param>
    /// <param name="debug">generates debug images and fill info</param>
    /// <returns>pixm mask showing connected regions of similar color</returns>
    /// <remarks>
    /// (1) This is the basic color filling operation.  It sets the
    ///     non-color pixel to black, optionally does a low-pass filter,
    ///     and grows the 8-connected color components.  Finally, it
    ///     removes components that have a small area.
    /// (2) NOTE: the non-color pixels of this pix are set to 0.
    /// </remarks>
    public Pix ColorFill(int minmax, int maxdiff, int smooth, int minarea, int debug)
    {
        Pix pixs = this;
        if (pixs.Depth != 32)
            throw Lept.Arg("pixColorFill", "pixs undefined or not 32 bpp");

            /* Set the non-color pixels to 0; generate a mask representing them */
        pixs.GetDimensions(out int w, out int h, out _);
        Pix pixnc = Create(w, h, 1);  /* mask for no color */
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                pixs.GetPixel(x, y, out uint val);
                if (!PixelColorIsValid(val, minmax)) {
                    pixnc.SetPixel(x, y, 1);
                    pixs.SetPixel(x, y, 0x0);
                }
            }
        }

            /* Optionally, dilate the no-color mask */
        Pix pixncd = DilateBrick(null, pixnc, smooth, smooth);

            /* Do a low-pass filter on pixs.  This will make bad pixels
             * near the zeroed non-color pixels, but any components made
             * from these pixels will be removed at the end by the
             * (optionally dilated) no-color mask. */
        Pix pixss;
        if (smooth > 1) {
            L_Kernel kel = L_Kernel.MakeFlatKernel(smooth, smooth, smooth / 2, smooth / 2);
            pixss = pixs.ConvolveRGBSep(kel, kel);
        } else {
            pixss = Copy(null, pixs);
        }

            /* Paint through everything under pixncd */
        pixss.PaintThroughMask(pixncd, 0, 0, 0);

            /* Find the color components */
        Pix pixv = Create(w, h, 1);  /* visited pixels */
        Or(pixv, pixv, pixncd);  /* consider non-color as visited */
        pixv.SetBorderRingVal(1, 1);
        Pix pixm = Create(w, h, 1);  /* color components */
        L_Queue<ColorEl> lq = L_Queue<ColorEl>.Create(0);
        int sx = 1, sy = 1;  /* first row and column have been marked as visited */
        while (FindNextUnvisited(pixv, ref sx, ref sy)) {
                /* Flood fill this component, starting from (x,y) */
            if (debug != 0) CStdio.Stderr($"Start: x = {sx}, y = {sy}\n");
            Pta? pta1 = ColorFillFromSeed(pixss, pixv, sx, sy, lq, maxdiff, minarea, debug);
            if (pta1 != null) {  /* erode and add the pixels to pixm */
                Pix pixm1 = GenerateFromPta(pta1, w, h);
                ErodeBrick(pixm1, pixm1, 3, 3);
                Or(pixm, pixm, pixm1);
            }
        }

            /* Remove everything under pixncd */
        Subtract(pixm, pixm, pixncd);

            /* Remove remaining small stuff */
        Pix pixf = pixm.SelectByArea(minarea, 4, L.L_SELECT_IF_GTE, out _);

        return pixf;
    }

    /* ----------------------------------------------------------------------- *
     *                         Generate data for testing                       *
     * ----------------------------------------------------------------------- */
    // leptonica: makeColorfillTestData (colorfill.c)
    /// <summary>Generates test data: images with regions of random color.</summary>
    /// <param name="w">width of generated pix</param>
    /// <param name="h">height of generated pix</param>
    /// <param name="nseeds">number of regions</param>
    /// <param name="range">of color component values</param>
    /// <returns>pixa various pix with filled regions of random color</returns>
    /// <remarks>
    /// (1) The seeds are random points.  The colors are assigned
    ///     randomly from a restricted range of component values,
    ///     in [128 - range/2 ... 128 + range/2]
    /// (2) Output is pixa:
    ///     * pixa[0] cmapped, with color regions shown
    ///     - pixa[1] cmapped, additionally with boundary pixels set to black
    ///     - pixa[2] cmapped, as in pixa[1] with all non-black pixels
    ///               in the same color
    /// </remarks>
    public static Pixa MakeColorfillTestData(int w, int h, int nseeds, int range)
    {
        int rval = 0, gval = 0, bval = 0;

            /* Generate data for seeds */
        Pta pta = Pta.Create(nseeds);  /* for seed locations */
        L_Dna da = L_Dna.Create(nseeds);  /* for colors */
        CRand.Seed(4);
        int start = 128 - range / 2;
        int end = 128 + (range - 1) / 2;
        for (int i = 0; i < nseeds; i++) {
            int x = Utils.GenRandomIntOnInterval(0, w - 1, 0);
            int y = Utils.GenRandomIntOnInterval(0, h - 1, 0);
            pta.AddPt(x, y);
            rval = Utils.GenRandomIntOnInterval(start, end, 0);
            gval = Utils.GenRandomIntOnInterval(start, end, 0);
            bval = Utils.GenRandomIntOnInterval(start, end, 0);
            ComposeRGBPixel(rval, gval, bval, out uint color);
            da.AddNumber(color);
        }

            /* Generate the 8 bpp seed image */
        Pix pix1 = Create(w, h, 8);
        for (int i = 0; i < nseeds; i++) {
            pta.GetIPt(i, out int x, out int y);
            pix1.SetPixel(x, y, (uint)(i + 1));  /* all seeds have non-zero values */
        }

            /* Spread seed values to all pixels that are nearest to
             * the seed pixel from which they take their value. */
        Pix pix2 = pix1.Seedspread(4);

            /* Add a colormap for the random colors, using 0 for black */
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(0, 0, 0);
        for (int i = 0; i < nseeds; i++) {
            double dval = da.GetDValue(i);
            ExtractRGBValues((uint)dval, out rval, out gval, out bval);
            cmap.AddColor(rval, gval, bval);
        }
        pix2.SetColormap(cmap);

            /* Add to output; no black boundaries */
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(pix2, L.L_COPY);

           /* Make pixels on the color boundaries black */
        Pix pix3 = Copy(null, pix2);
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                if (PixelIsOnColorBoundary(pix2, j, i))
                    pix3.SetPixel(j, i, 0);  /* black */
            }
        }
        pixa.AddPix(pix3, L.L_INSERT);

            /* Have all the non-black regions be the same color */
        cmap = PixColormap.Create(8);
        cmap.AddColor(0, 0, 0);
        for (int i = 0; i < nseeds; i++)
            cmap.AddColor(rval, gval, bval);
        Pix pix4 = Copy(null, pix3);
        pix4.SetColormap(cmap);
        pixa.AddPix(pix4, L.L_INSERT);

        return pixa;
    }

    /* ----------------------------------------------------------------------- *
     *                             Static helpers                              *
     * ----------------------------------------------------------------------- */
    // leptonica: pixColorFillFromSeed (colorfill.c)
    /// <summary>Traces out an 8-connected region of similar color from a seed pixel.</summary>
    /// <param name="pixs">32 bpp rgb</param>
    /// <param name="pixv">1 bpp labeling visited pixels</param>
    /// <param name="x">starting x coord for fill (seed)</param>
    /// <param name="y">starting y coord for fill (seed)</param>
    /// <param name="lq">head of queue holding pixels</param>
    /// <param name="maxdiff">max component diff allowed for similar pixels</param>
    /// <param name="minarea">min size of component to keep</param>
    /// <param name="debug">output some text data</param>
    /// <returns>points visited with similar colors during fill; null if the component is too small</returns>
    /// <remarks>
    /// (1) Use 8-connected filling.  It is faster because it reduces the
    ///     number of single-pixel noise components near color boundaries.
    /// (2) The seed pixel at (x,y) is unvisited, and can never be on the
    ///     exterior boundary of the tile pixs.
    /// (3) If the size of the connected component >= minarea, we return
    ///     the array of pixel locations; otherwise, return null for the pta.
    /// </remarks>
    private static Pta? ColorFillFromSeed(Pix pixs, Pix pixv, int x, int y, L_Queue<ColorEl> lq,
                                          int maxdiff, int minarea, int debug)
    {
        uint[] visited = new uint[8];  /* W, N, E, S, NW, NE, SW, SE */

            /* Prime the queue with this pixel */
        pixs.GetPixel(x, y, out uint val);
        ColorEl el = new ColorEl(x, y, val);
        lq.Add(el);
        pixv.SetPixel(x, y, 1);  /* visited */
        Pta pta = Pta.Create(0);
        pta.AddPt(x, y);

            /* Trace out the color component.  Each pixel on the queue has
             * a color.  Pop from the queue and for each of its 8 neighbors,
             * for those that have color:
             * - If the pixel has a similar color, add to the pta array for
             *   the component, using the color of its parent.
             * - Mark visited so that it will not be included in another
             *   component -- this effectively separates the growing component
             *   from all others. */
        while (lq.Count > 0) {
            el = lq.Remove()!;
            x = el.X;
            y = el.Y;
            uint color = el.Color;
            GetVisitedNeighbors(pixv, x, y, visited);
            if (visited[0] == 0) {  /* check W */
                pixs.GetPixel(x - 1, y, out val);
                if (ColorsAreSimilarForFill(color, val, maxdiff)) {
                    lq.Add(new ColorEl(x - 1, y, color));
                    pta.AddPt(x - 1, y);  /* added to component */
                    pixv.SetPixel(x - 1, y, 1);  /* visited */
                }
            }
            if (visited[1] == 0) {  /* check N */
                pixs.GetPixel(x, y - 1, out val);
                if (ColorsAreSimilarForFill(color, val, maxdiff)) {
                    lq.Add(new ColorEl(x, y - 1, color));
                    pta.AddPt(x, y - 1);
                    pixv.SetPixel(x, y - 1, 1);
                }
            }
            if (visited[2] == 0) {  /* check E */
                pixs.GetPixel(x + 1, y, out val);
                if (ColorsAreSimilarForFill(color, val, maxdiff)) {
                    lq.Add(new ColorEl(x + 1, y, color));
                    pta.AddPt(x + 1, y);
                    pixv.SetPixel(x + 1, y, 1);
                }
            }
            if (visited[3] == 0) {  /* check S */
                pixs.GetPixel(x, y + 1, out val);
                if (ColorsAreSimilarForFill(color, val, maxdiff)) {
                    lq.Add(new ColorEl(x, y + 1, color));
                    pta.AddPt(x, y + 1);
                    pixv.SetPixel(x, y + 1, 1);
                }
            }
            if (visited[4] == 0) {  /* check NW */
                pixs.GetPixel(x - 1, y - 1, out val);
                if (ColorsAreSimilarForFill(color, val, maxdiff)) {
                    lq.Add(new ColorEl(x - 1, y - 1, color));
                    pta.AddPt(x - 1, y - 1);
                    pixv.SetPixel(x - 1, y - 1, 1);
                }
            }
            if (visited[5] == 0) {  /* check NE */
                pixs.GetPixel(x + 1, y - 1, out val);
                if (ColorsAreSimilarForFill(color, val, maxdiff)) {
                    lq.Add(new ColorEl(x + 1, y - 1, color));
                    pta.AddPt(x + 1, y - 1);
                    pixv.SetPixel(x + 1, y - 1, 1);
                }
            }
            if (visited[6] == 0) {  /* check SW */
                pixs.GetPixel(x - 1, y + 1, out val);
                if (ColorsAreSimilarForFill(color, val, maxdiff)) {
                    lq.Add(new ColorEl(x - 1, y + 1, color));
                    pta.AddPt(x - 1, y + 1);
                    pixv.SetPixel(x - 1, y + 1, 1);
                }
            }
            if (visited[7] == 0) {  /* check SE */
                pixs.GetPixel(x + 1, y + 1, out val);
                if (ColorsAreSimilarForFill(color, val, maxdiff)) {
                    lq.Add(new ColorEl(x + 1, y + 1, color));
                    pta.AddPt(x + 1, y + 1);
                    pixv.SetPixel(x + 1, y + 1, 1);
                }
            }
        }

            /* If there are not enough pixels, do not return the pta.
             * Otherwise, if a pta is returned, the caller will generate
             * a component and put it in the mask. */
        int np = pta.Count;
        if (np < minarea) {
            if (debug != 0) CStdio.Stderr($"  Too small. End: x = {x}, y = {y}, np = {np}\n");
            return null;
        } else {
            if (debug != 0) CStdio.Stderr($"  Keep. End: x = {x}, y = {y}, np = {np}\n");
            return pta;
        }
    }

    // leptonica: pixGetVisitedNeighbors (colorfill.c)
    /// <summary>Gets the visited values of the 8 neighbors of a pixel.</summary>
    /// <param name="pixs">1 bpp, representing visited locations</param>
    /// <param name="x">x coord of pixel</param>
    /// <param name="y">y coord of pixel</param>
    /// <param name="visited">array of 8 values</param>
    /// <remarks>
    /// (1) The values of the neighbors of (x,y) in pixs, given in
    ///     order {W,N,E,S,NW,NE,SW,SE}, are returned in visited.
    ///     A "1" value means that pixel has been visited.  Initialize
    ///     each neighbor to 1 (visited).
    /// (2) The input point (x,y) is never on the outer boundary of pixs,
    ///     (e.g., x >= 1, y >= 1), so no checking is required.
    /// </remarks>
    private static void GetVisitedNeighbors(Pix pixs, int x, int y, uint[] visited)
    {
        pixs.GetPixel(x - 1, y, out visited[0]);  /* W */
        pixs.GetPixel(x, y - 1, out visited[1]);  /* N */
        pixs.GetPixel(x + 1, y, out visited[2]);  /* E */
        pixs.GetPixel(x, y + 1, out visited[3]);  /* S */
        pixs.GetPixel(x - 1, y - 1, out visited[4]);  /* NW */
        pixs.GetPixel(x + 1, y - 1, out visited[5]);  /* NE */
        pixs.GetPixel(x - 1, y + 1, out visited[6]);  /* SW */
        pixs.GetPixel(x + 1, y + 1, out visited[7]);  /* SE */
    }

    // leptonica: findNextUnvisited (colorfill.c)
    /// <summary>Finds the next unvisited pixel.</summary>
    /// <param name="pixv">visited pixels</param>
    /// <param name="px">x-coord of next seed</param>
    /// <param name="py">input start scanline; output y-coord of next seed</param>
    /// <returns>true if new seed point is found; false if there are none</returns>
    /// <remarks>
    /// (1) Start the search at the beginning of the raster line
    ///     for the pixel that ended the previous search.
    /// </remarks>
    private static bool FindNextUnvisited(Pix pixv, ref int px, ref int py)
    {
        Pix pix1 = Copy(null, pixv);
        Invert(pix1, pix1);   /* After inversion, ON pixels are unvisited */
        bool ret = pix1.NextOnPixelInRaster(1, py, out px, out py);
        return ret;
    }

    // leptonica: colorsAreSimilarForFill (colorfill.c)
    /// <summary>Tests whether two colors are similar enough to be in the same region.</summary>
    /// <param name="val1">color of pixel 1, as 0xrrggbb00</param>
    /// <param name="val2">color of pixel 2, as 0xrrggbb00</param>
    /// <param name="maxdiff">max of difference function to be similar</param>
    /// <returns>true if val1 and val2 are similar</returns>
    /// <remarks>
    /// (1) An example will explain the approach.  Suppose we have:
    ///       val1 = {100, 130, 70}
    ///       val2 = {90, 135, 62}
    ///     First find that red is the color with largest abs(difference):
    ///       rdiff = val1 - val2 = 10
    ///     Find the green and blue differences
    ///       gdiff = 130 - 135 = -5
    ///       bdiff = 70 - 62 = 8
    ///     and subtract each from rdiff:
    ///       rdiff - gdiff = 15
    ///       rdiff - bdiff = 7
    ///     The max of these is 15, which is then compared with maxdiff
    /// </remarks>
    private static bool ColorsAreSimilarForFill(uint val1, uint val2, int maxdiff)
    {
        int[] v1 = new int[3];
        int[] v2 = new int[3];
        ExtractRGBValues(val1, out v1[0], out v1[1], out v1[2]);
        ExtractRGBValues(val2, out v2[0], out v2[1], out v2[2]);
        int rdiff = v1[0] - v2[0];
        int gdiff = v1[1] - v2[1];
        int bdiff = v1[2] - v2[2];
        int maxindex = 0;
        if (Math.Abs(gdiff) > Math.Abs(rdiff))
            maxindex = 1;
        if (Math.Abs(bdiff) > Math.Abs(rdiff) && Math.Abs(bdiff) > Math.Abs(gdiff))
            maxindex = 2;
        int del1 = v1[maxindex] - v2[maxindex];
        int del2 = v1[(maxindex + 1) % 3] - v2[(maxindex + 1) % 3];
        int del3 = v1[(maxindex + 2) % 3] - v2[(maxindex + 2) % 3];
        int maxdel = Math.Max(Math.Abs(del1 - del2), Math.Abs(del1 - del3));
        return maxdel <= maxdiff;
    }

    // leptonica: pixelColorIsValid (colorfill.c)
    /// <param name="val">color, as 0xrrggbb00</param>
    /// <param name="minmax">max component must be &lt; minmax to be valid</param>
    /// <returns>false if max component &lt; minmax; true otherwise</returns>
    private static bool PixelColorIsValid(uint val, int minmax)
    {
        ExtractRGBValues(val, out int rval, out int gval, out int bval);
        if (rval < minmax && gval < minmax && bval < minmax)
            return false;  /* maximum component is less than threshold */
        else
            return true;
    }

    // leptonica: pixelIsOnColorBoundary (colorfill.c)
    /// <param name="pixs">32 bpp rgb or 8 bpp with colormap</param>
    /// <param name="x">location of pixel of interest</param>
    /// <param name="y">location of pixel of interest</param>
    /// <returns>true if at least one neighboring pixel had a different color</returns>
    private static bool PixelIsOnColorBoundary(Pix pixs, int x, int y)
    {
        pixs.GetDimensions(out int w, out int h, out _);
        pixs.GetPixel(x, y, out uint val);
        uint neigh;
        if (x > 0) {
            pixs.GetPixel(x - 1, y, out neigh);  /* W */
            if (neigh != val) return true;
        }
        if (x < w - 1) {
            pixs.GetPixel(x + 1, y, out neigh);  /* E */
            if (neigh != val) return true;
        }
        if (y > 0) {
            pixs.GetPixel(x, y - 1, out neigh);  /* N */
            if (neigh != val) return true;
        }
        if (y < h - 1) {
            pixs.GetPixel(x, y + 1, out neigh);  /* S */
            if (neigh != val) return true;
        }
        return false;
    }
}
