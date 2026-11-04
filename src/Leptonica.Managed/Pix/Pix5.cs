using System;

namespace Leptonica;

/*
 *  pix5.c: Measurement in a pix.
 *
 *    This file has these operations:
 *
 *      (1) Measurement of 1 bpp image properties
 *      (2) Extract rectangular regions
 *      (3) Clip to foreground
 *      (4) Extract pixel averages, reversals and variance along lines
 *      (5) Rank row and column transforms
 *
 *  The pixa functions are instance methods of Pixa.
 *
 *  Notes on the port:
 *    - Functions that return an error code in C and have exactly one result
 *      return that result; invalid input throws.
 *    - Functions whose C return value says "nothing found" (pixScanForForeground,
 *      pixScanForEdge) return false in that case.  pixClipToForeground,
 *      pixClipBoxToForeground and pixClipBoxToEdges return null outputs when
 *      there are no fg pixels (or no edges), without throwing.
 *    - pixClipRectangle and pixClipMasked return null when the clipping
 *      region does not intersect the pix (C: logged warning and NULL).
 *    - The debug output of pixaFindAreaFractionMasked() goes through
 *      pixDisplay().
 *
 *  Stubs created for callees in files that are not ported yet:
 *    pixErodeBrick (morph.c); pixExtractBorderConnComps (seedfill.c);
 *    pixEqual (compare.c); pixSetMaskedCmap (paintcmap.c);
 *    pixcmapGetNearestIndex (colormap.c); generatePtaLine (graphics.c);
 *    pixDisplay (writefile.c); pixaAddBox, pixaGetBox, pixaIsFull
 *    (pixabasic.c); pixaSort (pixafunc1.c); pixaDisplay (pixafunc2.c).
 */

/// <summary>Constants from pix.h and morph.h used by pix5.c.</summary>
public static partial class L
{
    /* Size Selection */
    /// <summary>Select components by width; for 1 bpp images.</summary>
    public const int L_SELECT_BY_WIDTH = 1;
    /// <summary>Select components by height; for 1 bpp images.</summary>
    public const int L_SELECT_BY_HEIGHT = 2;
    /// <summary>Select components by the larger of width and height; for 1 bpp images.</summary>
    public const int L_SELECT_BY_MAX_DIMENSION = 3;
    /// <summary>Select components by foreground area; for 1 bpp images.</summary>
    public const int L_SELECT_BY_AREA = 4;
    /// <summary>Select components by perimeter; for 1 bpp images.</summary>
    public const int L_SELECT_BY_PERIMETER = 5;

    /* Line orientation */

    /* Scan direction */
    /// <summary>Scan in the negative direction.</summary>
    public const int L_SCAN_NEGATIVE = 4;
    /// <summary>Scan in the positive direction.</summary>
    public const int L_SCAN_POSITIVE = 5;
    /// <summary>Scan in both directions.</summary>
    public const int L_SCAN_BOTH = 6;

    /* Flags for making simple masks */
    /// <summary>Select the interior part.</summary>
    public const int L_USE_INNER = 1;
    /// <summary>Select the outer part, for example a frame.</summary>
    public const int L_USE_OUTER = 2;

    /* Direction flags (morph.h) */
}

public sealed partial class Pixa
{
    /*-------------------------------------------------------------*
     *                 Measurement of properties                   *
     *-------------------------------------------------------------*/
    // leptonica: pixaFindDimensions (pix5.c)
    /// <param name="naw">numa of pix widths</param>
    /// <param name="nah">numa of pix heights</param>
    public void FindDimensions(out Numa naw, out Numa nah)
    {
        int n = Count;
        naw = Numa.Create(n);
        nah = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pixt = GetPix(i, L.L_CLONE);
            pixt.GetDimensions(out int w, out int h, out _);
            naw.AddNumber(w);
            nah.AddNumber(h);
        }
    }

    // leptonica: pixaFindPerimToAreaRatio (pix5.c)
    /// <returns>na of perimeter/area ratio for each pix</returns>
    /// <remarks>
    /// (1) This is typically used for a pixa consisting of
    ///     1 bpp connected components.
    /// </remarks>
    public Numa FindPerimToAreaRatio()
    {
        int n = Count;
        Numa na = Numa.Create(n);
        int[] tab = Pix.MakePixelSumTab8();
        for (int i = 0; i < n; i++) {
            Pix pixt = GetPix(i, L.L_CLONE);
            float fract = pixt.FindPerimToAreaRatio(tab);
            na.AddNumber(fract);
        }
        return na;
    }

    // leptonica: pixaFindPerimSizeRatio (pix5.c)
    /// <returns>na of fg perimeter/(2*(w+h)) ratio for each pix</returns>
    /// <remarks>
    /// (1) This is typically used for a pixa consisting of
    ///     1 bpp connected components.
    /// (2) This has a minimum value for a circle of pi/4; a value for
    ///     a rectangle component of approx. 1.0; and a value much larger
    ///     than 1.0 for a component with a highly irregular boundary.
    /// </remarks>
    public Numa FindPerimSizeRatio()
    {
        int n = Count;
        Numa na = Numa.Create(n);
        int[] tab = Pix.MakePixelSumTab8();
        for (int i = 0; i < n; i++) {
            Pix pixt = GetPix(i, L.L_CLONE);
            float ratio = pixt.FindPerimSizeRatio(tab);
            na.AddNumber(ratio);
        }
        return na;
    }

    // leptonica: pixaFindAreaFraction (pix5.c)
    /// <returns>na of area fractions for each pix</returns>
    /// <remarks>
    /// (1) This is typically used for a pixa consisting of
    ///     1 bpp connected components.
    /// </remarks>
    public Numa FindAreaFraction()
    {
        int n = Count;
        Numa na = Numa.Create(n);
        int[] tab = Pix.MakePixelSumTab8();
        for (int i = 0; i < n; i++) {
            Pix pixt = GetPix(i, L.L_CLONE);
            float fract = pixt.FindAreaFraction(tab);
            na.AddNumber(fract);
        }
        return na;
    }

    // leptonica: pixaFindAreaFractionMasked (pix5.c)
    /// <param name="pixm">mask image</param>
    /// <param name="debug">1 for output, 0 to suppress</param>
    /// <returns>na of ratio masked/total fractions for each pix</returns>
    /// <remarks>
    /// (1) This is typically used for a pixa consisting of
    ///     1 bpp connected components, which has an associated
    ///     boxa giving the location of the components relative
    ///     to the mask origin.
    /// (2) The debug flag displays in green and red the masked and
    ///     unmasked parts of the image from which pixa was derived.
    /// </remarks>
    public Numa FindAreaFractionMasked(Pix pixm, int debug)
    {
        if (pixm.Depth != 1)
            throw Lept.Arg("pixaFindAreaFractionMasked", "pixm undefined or not 1 bpp");

        int n = Count;
        Numa na = Numa.Create(n);
        int[] tab = Pix.MakePixelSumTab8();
        IsFull(out _, out bool full);  /* check boxa */
        Box? box = null;
        for (int i = 0; i < n; i++) {
            Pix pix = GetPix(i, L.L_CLONE);
            if (full)
                box = GetBox(i, L.L_CLONE);
            float fract = pix.FindAreaFractionMasked(box, pixm, tab);
            na.AddNumber(fract);
            box = null;
        }

        if (debug != 0) {
            pixm.GetDimensions(out int w, out int h, out _);
            Pix pix1 = Display(w, h);  /* recover original image */
            Pix pix2 = Pix.Create(w, h, 8);  /* make an 8 bpp white image ... */
            pix2.SetColormap(PixColormap.Create(8));  /* that's cmapped ... */
            pix2.SetBlackOrWhite(L.L_SET_WHITE);  /* and init to white */
            pix2.SetMaskedCmap(pix1, 0, 0, 255, 0, 0);  /* color all fg red */
            pix1.Rasterop(0, 0, w, h, L.PIX_MASK, pixm, 0, 0);
            pix2.SetMaskedCmap(pix1, 0, 0, 0, 255, 0);  /* turn masked green */
            pix2.Display(100, 100);
        }

        return na;
    }

    // leptonica: pixaFindWidthHeightRatio (pix5.c)
    /// <returns>na of width/height ratios for each pix</returns>
    /// <remarks>
    /// (1) This is typically used for a pixa consisting of
    ///     1 bpp connected components.
    /// </remarks>
    public Numa FindWidthHeightRatio()
    {
        int n = Count;
        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pixt = GetPix(i, L.L_CLONE);
            pixt.GetDimensions(out int w, out int h, out _);
            na.AddNumber((float)w / (float)h);
        }
        return na;
    }

    // leptonica: pixaFindWidthHeightProduct (pix5.c)
    /// <returns>na of width*height products for each pix</returns>
    /// <remarks>
    /// (1) This is typically used for a pixa consisting of
    ///     1 bpp connected components.
    /// </remarks>
    public Numa FindWidthHeightProduct()
    {
        int n = Count;
        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pixt = GetPix(i, L.L_CLONE);
            pixt.GetDimensions(out int w, out int h, out _);
            na.AddNumber(w * h);
        }
        return na;
    }
}

public sealed partial class Pix
{
    // leptonica: pixFindAreaPerimRatio (pix5.c)
    /// <summary>Area/perimeter ratio of a 1 bpp image.</summary>
    /// <param name="tab">[optional] pixel sum table, can be null</param>
    /// <returns>area/perimeter ratio</returns>
    /// <remarks>
    /// (1) The area is the number of fg pixels that are not on the
    ///     boundary (i.e., are not 8-connected to a bg pixel), and the
    ///     perimeter is the number of fg boundary pixels.  Returns
    ///     0.0 if there are no fg pixels.
    /// (2) This function is retained because clients are using it.
    /// </remarks>
    public float FindAreaPerimRatio(int[]? tab)
    {
        if (Depth != 1)
            throw Lept.Arg("pixFindAreaPerimRatio", "pixs not defined or not 1 bpp");

        int[] tab8 = tab ?? MakePixelSumTab8();

        Pix pixt = ErodeBrick(null, this, 3, 3);
        int nfg = pixt.CountPixels(tab8);
        if (nfg == 0)
            return 0.0f;
        Xor(pixt, pixt, this);
        int nbound = pixt.CountPixels(tab8);
        return (float)nfg / (float)nbound;
    }

    // leptonica: pixFindPerimToAreaRatio (pix5.c)
    /// <summary>Perimeter/area ratio of a 1 bpp image.</summary>
    /// <param name="tab">[optional] pixel sum table, can be null</param>
    /// <returns>perimeter/area ratio</returns>
    /// <remarks>
    /// (1) The perimeter is the number of fg boundary pixels, and the
    ///     area is the number of fg pixels.  This returns 0.0 if
    ///     there are no fg pixels.
    /// (2) Unlike pixFindAreaPerimRatio(), this uses the full set of
    ///     fg pixels for the area, and the ratio is taken in the opposite
    ///     order.
    /// (3) This is typically used for a single connected component.
    ///     This always has a value &lt;= 1.0, and if the average distance
    ///     of a fg pixel from the nearest bg pixel is d, this has
    ///     a value ~1/d.
    /// </remarks>
    public float FindPerimToAreaRatio(int[]? tab)
    {
        if (Depth != 1)
            throw Lept.Arg("pixFindPerimToAreaRatio", "pixs not defined or not 1 bpp");

        int[] tab8 = tab ?? MakePixelSumTab8();

        int nfg = CountPixels(tab8);
        if (nfg == 0)
            return 0.0f;
        Pix pixt = ErodeBrick(null, this, 3, 3);
        Xor(pixt, pixt, this);
        int nbound = pixt.CountPixels(tab8);
        return (float)nbound / (float)nfg;
    }

    // leptonica: pixFindPerimSizeRatio (pix5.c)
    /// <summary>Perimeter/size ratio of a 1 bpp image.</summary>
    /// <param name="tab">[optional] pixel sum table, can be null</param>
    /// <returns>perimeter/size ratio</returns>
    /// <remarks>
    /// (1) We take the 'size' as twice the sum of the width and
    ///     height of pixs, and the perimeter is the number of fg
    ///     boundary pixels.  We use the fg pixels of the boundary
    ///     because the pix may be clipped to the boundary, so an
    ///     erosion is required to count all boundary pixels.
    /// (2) This has a large value for dendritic, fractal-like components
    ///     with highly irregular boundaries.
    /// (3) This is typically used for a single connected component.
    ///     It has a value of about 1.0 for rectangular components with
    ///     relatively smooth boundaries.
    /// </remarks>
    public float FindPerimSizeRatio(int[]? tab)
    {
        if (Depth != 1)
            throw Lept.Arg("pixFindPerimSizeRatio", "pixs not defined or not 1 bpp");

        int[] tab8 = tab ?? MakePixelSumTab8();

        Pix pixt = ErodeBrick(null, this, 3, 3);
        Xor(pixt, pixt, this);
        int nbound = pixt.CountPixels(tab8);
        GetDimensions(out int w, out int h, out _);
        return (0.5f * nbound) / (float)(w + h);
    }

    // leptonica: pixFindAreaFraction (pix5.c)
    /// <summary>Fraction of fg pixels in a 1 bpp image.</summary>
    /// <param name="tab">[optional] pixel sum table, can be null</param>
    /// <returns>fg area/size ratio</returns>
    /// <remarks>
    /// (1) This finds the ratio of the number of fg pixels to the
    ///     size of the pix (w * h).  It is typically used for a
    ///     single connected component.
    /// </remarks>
    public float FindAreaFraction(int[]? tab)
    {
        if (Depth != 1)
            throw Lept.Arg("pixFindAreaFraction", "pixs not defined or not 1 bpp");

        int[] tab8 = tab ?? MakePixelSumTab8();
        GetDimensions(out int w, out int h, out _);
        int sum = CountPixels(tab8);
        return (float)sum / (float)(w * h);
    }

    // leptonica: pixFindAreaFractionMasked (pix5.c)
    /// <summary>Fraction of the fg pixels of a component that are under a mask.</summary>
    /// <param name="box">[optional] for pixs relative to pixm</param>
    /// <param name="pixm">1 bpp mask, typically over the entire image from
    ///                    which the component pixs was extracted</param>
    /// <param name="tab">[optional] pixel sum table, can be null</param>
    /// <returns>fg area/size ratio</returns>
    /// <remarks>
    /// (1) This finds the ratio of the number of masked fg pixels
    ///     in pixs to the total number of fg pixels in pixs.
    ///     It is typically used for a single connected component.
    ///     If there are no fg pixels, this returns a ratio of 0.0.
    /// (2) The box gives the location of the pix relative to that
    ///     of the UL corner of the mask.  Therefore, the rasterop
    ///     is performed with the pix translated to its location
    ///     (x, y) in the mask before ANDing.
    ///     If box == null, the UL corners of pixs and pixm are aligned.
    /// </remarks>
    public float FindAreaFractionMasked(Box? box, Pix pixm, int[]? tab)
    {
        const string func = "pixFindAreaFractionMasked";
        if (Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");
        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm not defined or not 1 bpp");

        int[] tab8 = tab ?? MakePixelSumTab8();
        int x = 0, y = 0;
        if (box != null)
            box.GetGeometry(out x, out y, out _, out _);
        GetDimensions(out int w, out int h, out _);

        Pix pix1 = Copy(null, this);
        pix1.Rasterop(0, 0, w, h, L.PIX_MASK, pixm, x, y);
        int sum = CountPixels(tab8);
        if (sum == 0)
            return 0.0f;
        int masksum = pix1.CountPixels(tab8);
        return (float)masksum / (float)sum;
    }

    // leptonica: pixFindOverlapFraction (pix5.c)
    /// <summary>Ratio of the fg intersection to the fg union of two 1 bpp images.</summary>
    /// <param name="pixs2">1 bpp</param>
    /// <param name="x2">location in this pix of UL corner of pixs2</param>
    /// <param name="y2">location in this pix of UL corner of pixs2</param>
    /// <param name="tab">[optional] pixel sum table, can be null</param>
    /// <param name="pratio">ratio fg intersection to fg union</param>
    /// <param name="pnoverlap">number of overlapping pixels</param>
    /// <remarks>
    /// (1) The UL corner of pixs2 is placed at (x2, y2) in pixs1.
    /// (2) This measure is similar to the correlation.
    /// </remarks>
    public void FindOverlapFraction(Pix pixs2, int x2, int y2, int[]? tab,
                                    out float pratio, out int pnoverlap)
    {
        const string func = "pixFindOverlapFraction";
        pnoverlap = 0;
        pratio = 0.0f;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs1 not defined or not 1 bpp");
        if (pixs2.Depth != 1)
            throw Lept.Arg(func, "pixs2 not defined or not 1 bpp");

        int[] tab8 = tab ?? MakePixelSumTab8();

        pixs2.GetDimensions(out int w, out int h, out _);
        Pix pixt = Copy(null, this);
        // NOTE(upstream): only the footprint of pixs2 is ANDed; fg pixels of pixs1 outside
        // the footprint stay in pixt and are counted as "intersection".
        pixt.Rasterop(x2, y2, w, h, L.PIX_MASK, pixs2, 0, 0);  /* AND */
        int nintersect = pixt.CountPixels(tab8);
        pnoverlap = nintersect;
        Copy(pixt, this);
        pixt.Rasterop(x2, y2, w, h, L.PIX_PAINT, pixs2, 0, 0);  /* OR */
        int nunion = pixt.CountPixels(tab8);

        if (nunion > 0)
            pratio = (float)nintersect / (float)nunion;
    }

    // leptonica: pixFindRectangleComps (pix5.c)
    /// <summary>Finds the 8-connected components that conform to a rectangle.</summary>
    /// <param name="dist">max distance allowed between bounding box
    ///                    and nearest foreground pixel within it</param>
    /// <param name="minw">minimum size in each direction as a requirement
    ///                    for a conforming rectangle</param>
    /// <param name="minh">minimum size in each direction as a requirement
    ///                    for a conforming rectangle</param>
    /// <returns>boxa of components that conform</returns>
    /// <remarks>
    /// (1) This applies the function pixConformsToRectangle() to
    ///     each 8-c.c. in pixs, and returns a boxa containing the
    ///     regions of all components that are conforming.
    /// (2) Conforming components must satisfy both the size constraint
    ///     given by %minsize and the slop in conforming to a rectangle
    ///     determined by %dist.
    /// </remarks>
    public Boxa FindRectangleComps(int dist, int minw, int minh)
    {
        const string func = "pixFindRectangleComps";
        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (dist < 0)
            throw Lept.Arg(func, "dist must be >= 0");
        if (minw <= 2 * dist && minh <= 2 * dist)
            throw Lept.Arg(func, "invalid parameters");

        Boxa boxa = ConnComp(out Pixa pixa, 8);
        Boxa boxad = Boxa.Create(0);
        int n = pixa.Count;
        for (int i = 0; i < n; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            pix.GetDimensions(out int w, out int h, out _);
            if (w < minw || h < minh)
                continue;
            bool conforms = pix.ConformsToRectangle(null, dist);
            if (conforms) {
                Box box = boxa.GetBox(i, L.L_COPY);
                boxad.AddBox(box, L.L_INSERT);
            }
        }
        return boxad;
    }

    // leptonica: pixConformsToRectangle (pix5.c)
    /// <summary>Tests if a component conforms to its bounding rectangle.</summary>
    /// <param name="box">[optional] if null, use the entire pixs</param>
    /// <param name="dist">max distance allowed between bounding box and
    ///                    nearest foreground pixel within it</param>
    /// <returns>false if not conforming; true if conforming</returns>
    /// <remarks>
    /// (1) There are several ways to test if a connected component has
    ///     an essentially rectangular boundary, such as:
    ///      a. Fraction of fill into the bounding box
    ///      b. Max-min distance of fg pixel from periphery of bounding box
    ///      c. Max depth of bg intrusions into component within bounding box
    ///     The weakness of (a) is that it is highly sensitive to holes
    ///     within the c.c.  The weakness of (b) is that it can have
    ///     arbitrarily large intrusions into the c.c.  Method (c) tests
    ///     the integrity of the outer boundary of the c.c., with respect
    ///     to the enclosing bounding box, so we use it.
    /// (2) This tests if the connected component within the box conforms
    ///     to the box at all points on the periphery within %dist.
    ///     Inside, at a distance from the box boundary that is greater
    ///     than %dist, we don't care about the pixels in the c.c.
    /// (3) We can think of the conforming condition as follows:
    ///     No pixel inside a distance %dist from the boundary
    ///     can connect to the boundary through a path through the bg.
    ///     To implement this, we need to do a flood fill.  We can go
    ///     either from inside toward the boundary, or the other direction.
    ///     It's easiest to fill from the boundary, and then verify that
    ///     there are no filled pixels farther than %dist from the boundary.
    /// </remarks>
    public bool ConformsToRectangle(Box? box, int dist)
    {
        const string func = "pixConformsToRectangle";
        if (Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");
        if (dist < 0)
            throw Lept.Arg(func, "dist must be >= 0");
        GetDimensions(out int w, out int h, out _);
        if (w <= 2 * dist || h <= 2 * dist) {
            Log.Warning(func, "automatic conformation: distance too large");
            return true;
        }

            /* Extract the region, if necessary */
        Pix? pix1;
        if (box != null)
            pix1 = ClipRectangle(box, out _);
        else
            pix1 = Copy(null, this);
        if (pix1 == null)  /* C: the failing calls below leave conforms = 0 */
            return false;

            /* Invert and fill from the boundary into the interior.
             * Because we're considering the connected component in an
             * 8-connected sense, we do the background filling as 4 c.c. */
        Invert(pix1, pix1);
        Pix pix2 = pix1.ExtractBorderConnComps(4);

            /* Mask out all pixels within a distance %dist from the box
             * boundary.  Any remaining pixels are from filling that goes
             * more than %dist from the boundary.  If no pixels remain,
             * the component conforms to the bounding rectangle within
             * a distance %dist. */
        pix2.SetOrClearBorder(dist, dist, dist, dist, L.PIX_CLR);
        bool empty = pix2.Zero();
        return empty;
    }

    /*-----------------------------------------------------------------------*
     *                      Extract rectangular regions                      *
     *-----------------------------------------------------------------------*/
    // leptonica: pixExtractRectangularRegions (pix5.c)
    /// <summary>Returns a pix with the given rectangular regions clipped from this pix.</summary>
    /// <param name="boxa">regions to extract</param>
    /// <returns>pix with extracted regions</returns>
    /// <remarks>
    /// (1) The returned pix has the rectangular regions clipped from
    ///     the input pixs.
    /// (2) We could equally well do this operation using a mask of 1's over
    ///     the regions determined by the boxa:
    ///       pix1 = pixCreateTemplate(pixs);
    ///       pixMaskBoxa(pix1, pix1, boxa, L_SET_PIXELS);
    ///       pixAnd(pix1, pix1, pixs);
    /// </remarks>
    public Pix ExtractRectangularRegions(Boxa boxa)
    {
        Pixa pixa1 = ClipRectangles(boxa);
        GetDimensions(out int w, out int h, out _);
        Pix pix1 = pixa1.Display(w, h);
        return pix1;
    }

    // leptonica: pixClipRectangles (pix5.c)
    /// <param name="boxa">requested clipping regions</param>
    /// <returns>pixa consisting of requested regions</returns>
    /// <remarks>
    /// (1) The boxa in the returned pixa has the regions clipped from
    ///     the input pixs.
    /// </remarks>
    public Pixa ClipRectangles(Boxa boxa)
    {
        int n = boxa.Count;
        Pixa pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            Pix? pix = ClipRectangle(box, out Box? boxc);
                /* In C, adding a NULL pix or box (no overlap) only reports an error */
            if (pix != null)
                pixa.AddPix(pix, L.L_INSERT);
            if (boxc != null)
                pixa.AddBox(boxc, L.L_INSERT);
        }

        return pixa;
    }

    // leptonica: pixClipRectangle (pix5.c)
    /// <param name="box">requested clipping region; const</param>
    /// <param name="pboxc">actual box of clipped region</param>
    /// <returns>clipped pix, or null if rectangle doesn't intersect pixs</returns>
    /// <remarks>
    /// This should be simple, but there are choices to be made.
    /// The box is defined relative to the pix coordinates.  However,
    /// if the box is not contained within the pix, we have two choices:
    ///
    ///     (1) clip the box to the pix
    ///     (2) make a new pix equal to the full box dimensions,
    ///         but let rasterop do the clipping and positioning
    ///         of the src with respect to the dest
    ///
    /// Choice (2) immediately brings up the problem of what pixel values
    /// to use that were not taken from the src.  For example, on a grayscale
    /// image, do you want the pixels not taken from the src to be black
    /// or white or something else?  To implement choice 2, one needs to
    /// specify the color of these extra pixels.
    ///
    /// So we adopt (1), and clip the box first, if necessary,
    /// before making the dest pix and doing the rasterop.  But there
    /// is another issue to consider.  If you want to paste the
    /// clipped pix back into pixs, it must be properly aligned, and
    /// it is necessary to use the clipped box for alignment.
    /// Accordingly, this function has a third (optional) argument, which is
    /// the input box clipped to the src pix.
    /// </remarks>
    public Pix? ClipRectangle(Box box, out Box? pboxc)
    {
        pboxc = null;

            /* Clip the input box to the pix */
        GetDimensions(out int w, out int h, out int d);
        Box? boxc = box.ClipToRectangleOrNull(w, h);
        if (boxc == null) {
            Log.Warning("pixClipRectangle", "box doesn't overlap pix");
            return null;
        }
        boxc.GetGeometry(out int bx, out int by, out int bw, out int bh);

            /* Extract the block */
        Pix pixd = Create(bw, bh, d);
        pixd.CopyResolution(this);
        pixd.CopyColormap(this);
        pixd.CopyText(this);
        pixd.Rasterop(0, 0, bw, bh, L.PIX_SRC, this, bx, by);

        pboxc = boxc;
        return pixd;
    }

    // leptonica: pixClipRectangleWithBorder (pix5.c)
    /// <param name="box">requested clipping region; const</param>
    /// <param name="maxbord">maximum amount of border to include</param>
    /// <param name="pboxn">box in coordinates of returned pix</param>
    /// <returns>under-clipped pix, or null if rectangle doesn't intersect pixs</returns>
    /// <remarks>
    /// (1) This underclips by an amount determined by the minimum of
    ///     %maxbord and the amount of border that can be included
    ///     equally on all 4 sides.
    /// (2) If part of the rectangle lies outside the pix, no border
    ///     is included on any side.
    /// </remarks>
    public Pix? ClipRectangleWithBorder(Box box, int maxbord, out Box? pboxn)
    {
        pboxn = null;

            /* Determine the border width */
        GetDimensions(out int w, out int h, out _);
        box.GetGeometry(out int bx, out int by, out int bw, out int bh);
        int bord = Math.Min(bx, by);
        bord = Math.Min(bord, w - bx - bw);
        bord = Math.Min(bord, h - by - bh);
        bord = Math.Min(bord, maxbord);

        if (bord <= 0) {  /* standard clipping */
            Pix? pix1 = ClipRectangle(box, out _);
            w = h = 0;  /* C: pixGetDimensions() on a null pix gives 0, 0 */
            pix1?.GetDimensions(out w, out h, out _);
            pboxn = Box.Create(0, 0, w, h);
            return pix1;
        }

            /* There is a positive border */
        Box box1 = Box.AdjustSides(null, box, -bord, bord, -bord, bord);
        Pix? pix2 = ClipRectangle(box1, out _);
        pboxn = Box.Create(bord, bord, bw, bh);
        return pix2;
    }

    // leptonica: pixClipMasked (pix5.c)
    /// <param name="pixm">clipping mask, 1 bpp</param>
    /// <param name="x">origin of clipping mask relative to pixs</param>
    /// <param name="y">origin of clipping mask relative to pixs</param>
    /// <param name="outval">val to use for pixels that are outside the mask</param>
    /// <returns>pixd, clipped pix, or null if pixm doesn't intersect pixs</returns>
    /// <remarks>
    /// (1) If pixs has a colormap, it is preserved in pixd.
    /// (2) The depth of pixd is the same as that of pixs.
    /// (3) If the depth of pixs is 1, use %outval = 0 for white background
    ///     and 1 for black; otherwise, use the max value for white
    ///     and 0 for black.  If pixs has a colormap, the max value for
    ///     %outval is 0xffffffff; otherwise, it is 2^d - 1.
    /// (4) When using 1 bpp pixs, this is a simple clip and
    ///     blend operation.  For example, if both pix1 and pix2 are
    ///     black text on white background, and you want to OR the
    ///     fg on the two images, let pixm be the inverse of pix2.
    ///     Then the operation takes all of pix1 that's in the bg of
    ///     pix2, and for the remainder (which are the pixels
    ///     corresponding to the fg of the pix2), paint them black
    ///     (1) in pix1.  The function call looks like
    ///        pixClipMasked(pix2, pixInvert(pix1, pix1), x, y, 1);
    /// </remarks>
    public Pix? ClipMasked(Pix pixm, int x, int y, uint outval)
    {
        if (pixm.Depth != 1)
            throw Lept.Arg("pixClipMasked", "pixm undefined or not 1 bpp");

            /* Clip out the region specified by pixm and (x,y) */
        pixm.GetDimensions(out int wm, out int hm, out _);
        Box box = Box.Create(x, y, wm, hm);
        Pix? pixd = ClipRectangle(box, out _);
        if (pixd == null)  /* C: the painting below fails and pixd stays null */
            return null;

            /* Paint 'outval' (or something close to it if cmapped) through
             * the pixels not masked by pixm */
        PixColormap? cmap = pixd.Colormap;
        Pix pixmi = Invert(null, pixm);
        if (cmap != null) {
            ExtractRGBValues(outval, out int rval, out int gval, out int bval);
            cmap.GetNearestIndex(rval, gval, bval, out int index);
            cmap.GetColor(index, out rval, out gval, out bval);
            ComposeRGBPixel(rval, gval, bval, out uint pixel);
            pixd.PaintThroughMask(pixmi, 0, 0, pixel);
        } else {
            pixd.PaintThroughMask(pixmi, 0, 0, outval);
        }

        return pixd;
    }

    // leptonica: pixCropToMatch (pix5.c)
    /// <param name="pixs2">any depth, colormap OK</param>
    /// <param name="ppixd1">may be a clone</param>
    /// <param name="ppixd2">may be a clone</param>
    /// <remarks>
    /// (1) This resizes pixs1 and/or pixs2 by cropping at the right
    ///     and bottom, so that they're the same size.
    /// (2) If a pix doesn't need to be cropped, a clone is returned.
    /// (3) Note: the images are implicitly aligned to the UL corner.
    /// </remarks>
    public void CropToMatch(Pix pixs2, out Pix ppixd1, out Pix ppixd2)
    {
        GetDimensions(out int w1, out int h1, out _);
        pixs2.GetDimensions(out int w2, out int h2, out _);
        int w = Math.Min(w1, w2);
        int h = Math.Min(h1, h2);

        ppixd1 = CropToSize(w, h);
        ppixd2 = pixs2.CropToSize(w, h);
    }

    // leptonica: pixCropToSize (pix5.c)
    /// <param name="w">max width of cropped image</param>
    /// <param name="h">max height of cropped image</param>
    /// <returns>pixd cropped if necessary</returns>
    /// <remarks>
    /// (1) If either w or h is smaller than the corresponding dimension
    ///     of pixs, this returns a cropped image; otherwise it returns
    ///     a clone of pixs.
    /// </remarks>
    public Pix CropToSize(int w, int h)
    {
        GetDimensions(out int ws, out int hs, out int d);
        if (ws <= w && hs <= h)  /* no cropping necessary */
            return Clone(this);

        int wd = Math.Min(ws, w);
        int hd = Math.Min(hs, h);
        Pix pixd = Create(wd, hd, d);
        pixd.CopyResolution(this);
        pixd.CopyColormap(this);
        pixd.CopyText(this);
        pixd.CopyInputFormat(this);
        pixd.Rasterop(0, 0, wd, hd, L.PIX_SRC, this, 0, 0);
        return pixd;
    }

    // leptonica: pixResizeToMatch (pix5.c)
    /// <param name="pixt">can be null; we use only the size</param>
    /// <param name="w">ignored if pixt is defined</param>
    /// <param name="h">ignored if pixt is defined</param>
    /// <returns>pixd resized to match</returns>
    /// <remarks>
    /// (1) This resizes pixs to make pixd, without scaling, by either
    ///     cropping or extending separately in both width and height.
    ///     Extension is done by replicating the last row or column.
    ///     This is useful in a situation where, due to scaling
    ///     operations, two images that are expected to be the
    ///     same size can differ slightly in each dimension.
    /// (2) You can use either an existing pixt or specify
    ///     both %w and %h.  If pixt is defined, the values
    ///     in %w and %h are ignored.
    /// (3) If pixt is larger than pixs (or if w and/or d is larger
    ///     than the dimension of pixs, replicate the outer row and
    ///     column of pixels in pixs into pixd.
    /// </remarks>
    public Pix ResizeToMatch(Pix? pixt, int w, int h)
    {
        if (pixt == null && (w <= 0 || h <= 0))
            throw Lept.Arg("pixResizeToMatch", "both w and h not > 0");

        if (pixt != null)  /* redefine w, h */
            pixt.GetDimensions(out w, out h, out _);
        GetDimensions(out int ws, out int hs, out int d);
        if (ws == w && hs == h)
            return Copy(null, this);

        Pix pixd = Create(w, h, d);
        pixd.CopyResolution(this);
        pixd.CopyColormap(this);
        pixd.CopyText(this);
        pixd.CopyInputFormat(this);
        pixd.Rasterop(0, 0, ws, hs, L.PIX_SRC, this, 0, 0);
        if (ws >= w && hs >= h)
            return pixd;

            /* Replicate the last column and then the last row */
        if (ws < w) {
            for (int j = ws; j < w; j++)
                pixd.Rasterop(j, 0, 1, h, L.PIX_SRC, pixd, ws - 1, 0);
        }
        if (hs < h) {
            for (int i = hs; i < h; i++)
                pixd.Rasterop(0, i, w, 1, L.PIX_SRC, pixd, 0, hs - 1);
        }

        return pixd;
    }

    /*---------------------------------------------------------------------*
     *                Select a connected component by size                 *
     *---------------------------------------------------------------------*/
    // leptonica: pixSelectComponentBySize (pix5.c)
    /// <param name="rankorder">in decreasing size: 0 for largest</param>
    /// <param name="type">L_SELECT_BY_WIDTH, L_SELECT_BY_HEIGHT,
    ///                    L_SELECT_BY_MAX_DIMENSION,
    ///                    L_SELECT_BY_AREA, L_SELECT_BY_PERIMETER</param>
    /// <param name="connectivity">4 or 8</param>
    /// <param name="pbox">location of returned component</param>
    /// <returns>pix of rank order connected component</returns>
    /// <remarks>
    /// (1) This selects the Nth largest connected component, based on
    ///     the selection type and connectivity.
    /// (2) Note that %rankorder is an integer.  Use %rankorder = 0 for
    ///     the largest component and %rankorder = -1 for the smallest.
    ///     If %rankorder >= number of components, select the smallest.
    /// </remarks>
    public Pix SelectComponentBySize(int rankorder, int type, int connectivity, out Box? pbox)
    {
        const string func = "pixSelectComponentBySize";
        pbox = null;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        int sorttype;
        if (type == L.L_SELECT_BY_WIDTH)
            sorttype = L.L_SORT_BY_WIDTH;
        else if (type == L.L_SELECT_BY_HEIGHT)
            sorttype = L.L_SORT_BY_HEIGHT;
        else if (type == L.L_SELECT_BY_MAX_DIMENSION)
            sorttype = L.L_SORT_BY_MAX_DIMENSION;
        else if (type == L.L_SELECT_BY_AREA)
            sorttype = L.L_SORT_BY_AREA;
        else if (type == L.L_SELECT_BY_PERIMETER)
            sorttype = L.L_SORT_BY_PERIMETER;
        else
            throw Lept.Arg(func, "invalid selection type");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");
        bool empty = Zero();
        if (empty)
            throw Lept.Arg(func, "no foreground pixels");

        Boxa boxa1 = ConnComp(out Pixa pixa1, connectivity);
        int n = boxa1.Count;
        if (rankorder < 0 || rankorder >= n)
            rankorder = n - 1;  /* smallest */
        Pixa pixa2 = pixa1.Sort(sorttype, L.L_SORT_DECREASING, out Numa? naindex, L.L_CLONE);
        Pix pixd = pixa2.GetPix(rankorder, L.L_COPY);
        int index = naindex!.GetIValue(rankorder);
        pbox = boxa1.GetBox(index, L.L_COPY);

        return pixd;
    }

    // leptonica: pixFilterComponentBySize (pix5.c)
    /// <param name="rankorder">in decreasing size: 0 for largest</param>
    /// <param name="type">L_SELECT_BY_WIDTH, L_SELECT_BY_HEIGHT,
    ///                    L_SELECT_BY_MAX_DIMENSION,
    ///                    L_SELECT_BY_AREA, L_SELECT_BY_PERIMETER</param>
    /// <param name="connectivity">4 or 8</param>
    /// <param name="pbox">location of returned component</param>
    /// <returns>pix with all other components removed</returns>
    /// <remarks>
    /// (1) See notes in pixSelectComponentBySize().
    /// (2) This returns a copy of %pixs, with all components removed
    ///     except for the selected one.
    /// </remarks>
    public Pix FilterComponentBySize(int rankorder, int type, int connectivity, out Box? pbox)
    {
        pbox = null;
        if (Depth != 1)
            throw Lept.Arg("pixFilterComponentBySize", "pixs undefined or not 1 bpp");

        Pix pix1 = SelectComponentBySize(rankorder, type, connectivity, out Box? box);

            /* Put the selected component in a new pix at the same
             * location as it had in %pixs */
        box!.GetGeometry(out int x, out int y, out int w, out int h);
        Pix pix2 = CreateTemplate(this);
        pix2.Rasterop(x, y, w, h, L.PIX_SRC, pix1, 0, 0);
        pbox = box;
        return pix2;
    }

    /*---------------------------------------------------------------------*
     *                         Make special masks                          *
     *---------------------------------------------------------------------*/
    // leptonica: pixMakeSymmetricMask (pix5.c)
    /// <param name="w">dimensions of output 1 bpp pix</param>
    /// <param name="h">dimensions of output 1 bpp pix</param>
    /// <param name="hf">horizontal fraction of half-width</param>
    /// <param name="vf">vertical fraction of half-height</param>
    /// <param name="type">L_USE_INNER, L_USE_OUTER</param>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// (1) This is a convenience function for generating masks with
    ///     horizontal and vertical reflection symmetry, over either
    ///     the inner or outer parts of an image.
    /// (2) Using L_USE_INNER to generate a mask over the inner part
    ///     of the image, the mask is a solid rectangle, and the fractions
    ///     describe the distance between the boundary of the image and
    ///     the rectangle boundary.  For example, with hf == vf == 0.0,
    ///     the mask covers the full image.
    /// (3) Using L_USE_OUTER to generate a mask over an outer frame
    ///     of the image, the mask touches the boundary of the image,
    ///     and the fractions describe the location of the inner
    ///     boundary of the frame.  For example, with hf == vf == 1.0,
    ///     the inner boundary is at the center of the image, so the
    ///     mask covers the full image.
    /// (4) More examples:
    ///      * mask covering the inner 70%: hf = vf = 0.3, type = L_USE_INNER
    ///      * frame covering the outer 30%: hf = vf = 0.3, type = L_USE_OUTER
    /// </remarks>
    public static Pix MakeSymmetricMask(int w, int h, float hf, float vf, int type)
    {
        const string func = "pixMakeSymmetricMask";
        if (w <= 0 || h <= 0)
            throw Lept.Arg(func, "mask size 0");
        if (hf < 0.0 || hf > 1.0)
            throw Lept.Arg(func, "invalid horiz fractions");
        if (vf < 0.0 || vf > 1.0)
            throw Lept.Arg(func, "invalid vert fractions");

        if (type == L.L_USE_INNER)
            return MakeFrameMask(w, h, hf, 1.0f, vf, 1.0f);
        else if (type == L.L_USE_OUTER)
            return MakeFrameMask(w, h, 0.0f, hf, 0.0f, vf);
        else
            throw Lept.Arg(func, "invalid type");
    }

    // leptonica: pixMakeFrameMask (pix5.c)
    /// <param name="w">dimensions of output 1 bpp pix</param>
    /// <param name="h">dimensions of output 1 bpp pix</param>
    /// <param name="hf1">horizontal fraction of half-width at outer frame bdry</param>
    /// <param name="hf2">horizontal fraction of half-width at inner frame bdry</param>
    /// <param name="vf1">vertical fraction of half-width at outer frame bdry</param>
    /// <param name="vf2">vertical fraction of half-width at inner frame bdry</param>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// (1) This makes an arbitrary 1-component mask with a centered fg
    ///     frame, which can have both an inner and an outer boundary.
    ///     All input fractional distances are measured from the image
    ///     border to the frame boundary, in units of the image half-width
    ///     for hf1 and hf2 and the image half-height for vf1 and vf2.
    ///     The distances to the outer frame boundary are given by hf1
    ///     and vf1; to the inner frame boundary, by hf2 and vf2.
    ///     Input fractions are thus in [0.0 ... 1.0], with hf1 &lt;= hf2
    ///     and vf1 &lt;= vf2.  Horizontal and vertical frame widths are
    ///     thus independently specified.
    /// (2) Special cases:
    ///      * full fg mask: hf1 = vf1 = 0.0, hf2 = vf2 = 1.0.
    ///      * empty fg (zero width) mask: set  hf1 = hf2  and vf1 = vf2.
    ///      * fg rectangle with no hole: set hf2 = vf2 = 1.0.
    ///      * frame touching outer boundary: set hf1 = vf1 = 0.0.
    /// (3) The vertical thickness of the horizontal mask parts
    ///     is 0.5 * (vf2 - vf1) * h.  The horizontal thickness of the
    ///     vertical mask parts is 0.5 * (hf2 - hf1) * w.
    /// </remarks>
    public static Pix MakeFrameMask(int w, int h, float hf1, float hf2, float vf1, float vf2)
    {
        const string func = "pixMakeFrameMask";
        if (w <= 0 || h <= 0)
            throw Lept.Arg(func, "mask size 0");
        if (hf1 < 0.0 || hf1 > 1.0 || hf2 < 0.0 || hf2 > 1.0)
            throw Lept.Arg(func, "invalid horiz fractions");
        if (vf1 < 0.0 || vf1 > 1.0 || vf2 < 0.0 || vf2 > 1.0)
            throw Lept.Arg(func, "invalid vert fractions");
        if (hf1 > hf2 || vf1 > vf2)
            throw Lept.Arg(func, "invalid relative sizes");

        Pix pixd = Create(w, h, 1);

            /* Special cases */
        if (hf1 == 0.0 && vf1 == 0.0 && hf2 == 1.0 && vf2 == 1.0) {  /* full */
            pixd.SetAll();
            return pixd;
        }
        if (hf1 == hf2 && vf1 == vf2) {  /* empty */
            return pixd;
        }

            /* General case */
        int h1 = (int)(0.5f * hf1 * w);
        int h2 = (int)(0.5f * hf2 * w);
        int v1 = (int)(0.5f * vf1 * h);
        int v2 = (int)(0.5f * vf2 * h);
        pixd.Rasterop(h1, v1, w - 2 * h1, h - 2 * v1, L.PIX_SET, null, 0, 0);
        if (hf2 < 1.0 && vf2 < 1.0)
            pixd.Rasterop(h2, v2, w - 2 * h2, h - 2 * v2, L.PIX_CLR, null, 0, 0);
        return pixd;
    }

    /*---------------------------------------------------------------------*
     *     Generate a covering of rectangles over connected components     *
     *---------------------------------------------------------------------*/
    // leptonica: pixMakeCoveringOfRectangles (pix5.c)
    /// <param name="maxiters">max iterations: use 0 to iterate to completion</param>
    /// <remarks>
    /// (1) This iteratively finds the bounding boxes of the connected
    ///     components and generates a mask from them.  Two iterations
    ///     should suffice for most situations.
    /// (2) Returns an empty pix if %pixs is empty.
    /// (3) If there are many small components in proximity, it may
    ///     be useful to merge them with a morphological closing before
    ///     calling this one.
    /// </remarks>
    public Pix MakeCoveringOfRectangles(int maxiters)
    {
        const string func = "pixMakeCoveringOfRectangles";
        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (maxiters < 0)
            throw Lept.Arg(func, "maxiters must be >= 0");
        if (maxiters == 0) maxiters = 50;  /* ridiculously large number */

        bool empty = Zero();
        Pix pix1 = CreateTemplate(this);
        if (empty) return pix1;

            /* Do first iteration */
        Boxa boxa = ConnCompBB(8);
        MaskBoxa(pix1, pix1, boxa, L.L_SET_PIXELS);
        if (maxiters == 1) return pix1;

        int niters = 1;
        while (niters < maxiters) {  /* continue to add pixels to pix1 */
            niters++;
            boxa = pix1.ConnCompBB(8);
            Pix pix2 = Copy(null, pix1);
            MaskBoxa(pix1, pix1, boxa, L.L_SET_PIXELS);
            bool same = Equal(pix1, pix2);
            if (same) {
                Log.Info(func, $"{niters - 1} iterations");
                return pix1;
            }
        }
        Log.Info(func, $"maxiters = {niters} reached");
        return pix1;
    }

    /*---------------------------------------------------------------------*
     *                 Fraction of Fg pixels under a mask                  *
     *---------------------------------------------------------------------*/
    // leptonica: pixFractionFgInMask (pix5.c)
    /// <param name="pix2">1 bpp</param>
    /// <returns>fraction of fg pixels in this pix that are aligned with the fg of pix2</returns>
    /// <remarks>
    /// (1) This gives the fraction of fg pixels in pix1 that are in
    ///     the intersection (i.e., under the fg) of pix2:
    ///     |1 &amp; 2|/|1|, where |...| means the number of fg pixels.
    ///     Note that this is different from the situation where
    ///     pix1 and pix2 are reversed.
    /// (2) Both pix1 and pix2 are registered to the UL corners.  A warning
    ///     is issued if pix1 and pix2 have different sizes.
    /// (3) This can also be used to find the fraction of fg pixels in pix1
    ///     that are NOT under the fg of pix2: 1.0 - |1 &amp; 2|/|1|
    /// (4) If pix1 or pix2 are empty, this returns %fract = 0.0.
    /// (5) For example, pix2 could be a frame around the outside of the
    ///     image, made from pixMakeFrameMask().
    /// </remarks>
    public float FractionFgInMask(Pix pix2)
    {
        const string func = "pixFractionFgInMask";
        if (Depth != 1)
            throw Lept.Arg(func, "pix1 not defined or not 1 bpp");
        if (pix2.Depth != 1)
            throw Lept.Arg(func, "pix2 not defined or not 1 bpp");

        GetDimensions(out int w1, out int h1, out _);
        pix2.GetDimensions(out int w2, out int h2, out _);
        if (w1 != w2 || h1 != h2) {
            Log.Info(func, $"sizes unequal: (w1,w2) = ({w1},{w2}), (h1,h2) = ({h1},{h2})");
        }
        bool empty = Zero();
        if (empty) return 0.0f;
        empty = pix2.Zero();
        if (empty) return 0.0f;

        Pix pix3 = Copy(null, this);
        And(pix3, pix3, pix2);
        int count1 = CountPixels(null);  /* |1| */
        int count3 = pix3.CountPixels(null);  /* |1 & 2| */
        return (float)count3 / (float)count1;
    }

    /*---------------------------------------------------------------------*
     *                           Clip to Foreground                        *
     *---------------------------------------------------------------------*/
    // leptonica: pixClipToForeground (pix5.c)
    /// <summary>Clips a 1 bpp image to its foreground.</summary>
    /// <param name="ppixd">clipped pix returned; null if there are no fg pixels</param>
    /// <param name="pbox">bounding box; null if there are no fg pixels</param>
    /// <remarks>
    /// (1) C: at least one of {&amp;pixd, &amp;box} must be specified.
    /// (2) If there are no fg pixels, the returned objects are null.
    /// </remarks>
    public void ClipToForeground(out Pix? ppixd, out Box? pbox)
    {
        ppixd = null;
        pbox = null;
        if (Depth != 1)
            throw Lept.Arg("pixClipToForeground", "pixs not defined or not 1 bpp");

        GetDimensions(out int w, out int h, out _);
        int nfullwords = w / 32;
        int extra = w & 31;
        uint mask = ~rmask32[32 - extra];
        int wpl = Wpl;
        uint[] data = Data;

        int i, j, miny, maxy, minx, maxx;
        uint result = 0;
        for (i = 0, miny = 0; i < h; i++, miny++) {
            int line = i * wpl;
            for (j = 0; j < nfullwords; j++)
                result |= data[line + j];
            if (extra != 0)
                result |= (data[line + nfullwords] & mask);
            if (result != 0)
                break;
        }
        if (miny == h)  /* no ON pixels */
            return;

        result = 0;
        for (i = h - 1, maxy = h - 1; i >= 0; i--, maxy--) {
            int line = i * wpl;
            for (j = 0; j < nfullwords; j++)
                result |= data[line + j];
            if (extra != 0)
                result |= (data[line + nfullwords] & mask);
            if (result != 0)
                break;
        }

        for (j = 0, minx = 0; j < w; j++, minx++) {
            for (i = 0; i < h; i++) {
                if (ArrayAccess.GetDataBit(data.AsSpan(i * wpl), j) != 0)
                    goto minx_found;
            }
        }

minx_found:
        for (j = w - 1, maxx = w - 1; j >= 0; j--, maxx--) {
            for (i = 0; i < h; i++) {
                if (ArrayAccess.GetDataBit(data.AsSpan(i * wpl), j) != 0)
                    goto maxx_found;
            }
        }

maxx_found:
        Box box = Box.Create(minx, miny, maxx - minx + 1, maxy - miny + 1);

        ppixd = ClipRectangle(box, out _);
        pbox = box;
    }

    // leptonica: pixTestClipToForeground (pix5.c)
    /// <summary>Tests if a 1 bpp image can be further cropped without loss of fg pixels.</summary>
    /// <returns>true if fg does not extend to all four edges</returns>
    /// <remarks>
    /// (1) This is a lightweight test to determine if a 1 bpp image
    ///     can be further cropped without loss of fg pixels.
    ///     If it cannot, canclip is set to false.
    /// (2) It does not test for the existence of any fg pixels.
    ///     If there are no fg pixels, it will return %canclip = true.
    ///     Check the output of the subsequent call to pixClipToForeground().
    /// </remarks>
    public bool TestClipToForeground()
    {
        if (Depth != 1)
            throw Lept.Arg("pixTestClipToForeground", "pixs not defined or not 1 bpp");

            /* Check top and bottom raster lines */
        GetDimensions(out int w, out int h, out _);
        uint[] data = Data;
        int wpl = Wpl;
        bool found = false;
        for (int j = 0; !found && j < w; j++)
            found = ArrayAccess.GetDataBit(data, j) != 0;
        if (!found)
            return true;

        int line = (h - 1) * wpl;
        found = false;
        // NOTE(upstream): the bottom raster line is never tested; the scan reads
        // the first line (data) instead of the last (line), so this check always succeeds.
        for (int j = 0; !found && j < w; j++)
            found = ArrayAccess.GetDataBit(data, j) != 0;
        if (!found)
            return true;

            /* Check left and right edges */
        found = false;
        for (int i = 0, ofs = 0; !found && i < h; ofs += wpl, i++)
            found = ArrayAccess.GetDataBit(data.AsSpan(ofs), 0) != 0;
        if (!found)
            return true;

        found = false;
        for (int i = 0, ofs = 0; !found && i < h; ofs += wpl, i++)
            found = ArrayAccess.GetDataBit(data.AsSpan(ofs), w - 1) != 0;
        if (!found)
            return true;

        _ = line;
        return false;  /* fg pixels found on all edges */
    }

    // leptonica: pixClipBoxToForeground (pix5.c)
    /// <summary>Clips a box within a 1 bpp image to its foreground.</summary>
    /// <param name="boxs">[optional] use full image if null</param>
    /// <param name="ppixd">clipped pix returned; null if there are no fg pixels</param>
    /// <param name="pboxd">bounding box; null if there are no fg pixels</param>
    /// <remarks>
    /// (1) C: at least one of {&amp;pixd, &amp;boxd} must be specified.
    /// (2) If there are no fg pixels, the returned objects are null.
    /// </remarks>
    public void ClipBoxToForeground(Box? boxs, out Pix? ppixd, out Box? pboxd)
    {
        ppixd = null;
        pboxd = null;
        if (Depth != 1)
            throw Lept.Arg("pixClipBoxToForeground", "pixs not defined or not 1 bpp");

        if (boxs == null) {
            ClipToForeground(out ppixd, out pboxd);
            return;
        }

        GetDimensions(out int w, out int h, out _);
        boxs.GetGeometry(out int bx, out int by, out int bw, out int bh);
        int cbw = Math.Min(bw, w - bx);
        int cbh = Math.Min(bh, h - by);
        if (cbw < 0 || cbh < 0)
            throw Lept.Arg("pixClipBoxToForeground", "box not within image");
        Box boxt = Box.Create(bx, by, cbw, cbh);

        if (!ScanForForeground(boxt, L.L_FROM_LEFT, out int left))
            return;
        ScanForForeground(boxt, L.L_FROM_RIGHT, out int right);
        ScanForForeground(boxt, L.L_FROM_TOP, out int top);
        ScanForForeground(boxt, L.L_FROM_BOT, out int bottom);

        Box boxd = Box.Create(left, top, right - left + 1, bottom - top + 1);
        ppixd = ClipRectangle(boxd, out _);
        pboxd = boxd;
    }

    // leptonica: pixScanForForeground (pix5.c)
    /// <summary>Finds the location of the first fg pixel scanning from one side.</summary>
    /// <param name="box">[optional] within which the search is conducted</param>
    /// <param name="scanflag">direction of scan; e.g., L_FROM_LEFT</param>
    /// <param name="ploc">location in scan direction of first black pixel</param>
    /// <returns>true if a fg pixel was found; false if there are no fg pixels</returns>
    /// <remarks>
    /// (1) If there are no fg pixels, the position is set to 0.
    ///     Caller must check the return value!
    /// (2) Use %box == null to scan from edge of pixs
    /// </remarks>
    public bool ScanForForeground(Box? box, int scanflag, out int ploc)
    {
        const string func = "pixScanForForeground";
        ploc = 0;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");

            /* Clip box to pixs if it exists */
        GetDimensions(out int bw, out int bh, out _);
        int bx, by;
        if (box != null) {
            Box? boxt = box.ClipToRectangleOrNull(bw, bh);
            if (boxt == null)
                throw Lept.Arg(func, "invalid box");
            boxt.GetGeometry(out bx, out by, out bw, out bh);
        } else {
            bx = by = 0;
        }
        int xstart = bx;
        int ystart = by;
        int xend = bx + bw - 1;
        int yend = by + bh - 1;

        uint[] data = Data;
        int wpl = Wpl;
        if (scanflag == L.L_FROM_LEFT) {
            for (int x = xstart; x <= xend; x++) {
                for (int y = ystart; y <= yend; y++) {
                    if (ArrayAccess.GetDataBit(data.AsSpan(y * wpl), x) != 0) {
                        ploc = x;
                        return true;
                    }
                }
            }
        } else if (scanflag == L.L_FROM_RIGHT) {
            for (int x = xend; x >= xstart; x--) {
                for (int y = ystart; y <= yend; y++) {
                    if (ArrayAccess.GetDataBit(data.AsSpan(y * wpl), x) != 0) {
                        ploc = x;
                        return true;
                    }
                }
            }
        } else if (scanflag == L.L_FROM_TOP) {
            for (int y = ystart; y <= yend; y++) {
                ReadOnlySpan<uint> line = data.AsSpan(y * wpl);
                for (int x = xstart; x <= xend; x++) {
                    if (ArrayAccess.GetDataBit(line, x) != 0) {
                        ploc = y;
                        return true;
                    }
                }
            }
        } else if (scanflag == L.L_FROM_BOT) {
            for (int y = yend; y >= ystart; y--) {
                ReadOnlySpan<uint> line = data.AsSpan(y * wpl);
                for (int x = xstart; x <= xend; x++) {
                    if (ArrayAccess.GetDataBit(line, x) != 0) {
                        ploc = y;
                        return true;
                    }
                }
            }
        } else {
            throw Lept.Arg(func, "invalid scanflag");
        }

        return false;  /* no fg found */
    }

    // leptonica: pixClipBoxToEdges (pix5.c)
    /// <summary>Clips a box within a 1 bpp image to rectangular regions with well-defined edges.</summary>
    /// <param name="boxs">[optional]; use full image if null</param>
    /// <param name="lowthresh">threshold to choose clipping location</param>
    /// <param name="highthresh">threshold required to find an edge</param>
    /// <param name="maxwidth">max allowed width between low and high thresh locs</param>
    /// <param name="factor">sampling factor along pixel counting direction</param>
    /// <param name="ppixd">clipped pix returned; null if a fg edge is not found from all four sides</param>
    /// <param name="pboxd">bounding box; null if a fg edge is not found from all four sides</param>
    /// <remarks>
    /// (1) C: at least one of {&amp;pixd, &amp;boxd} must be specified.
    /// (2) If there are no fg pixels, the returned objects are null.
    /// (3) This function attempts to locate rectangular "image" regions
    ///     of high-density fg pixels, that have well-defined edges
    ///     on the four sides.
    /// (4) Edges are searched for on each side, iterating in order
    ///     from left, right, top and bottom.  As each new edge is
    ///     found, the search box is resized to use that location.
    ///     Once an edge is found, it is held.  If no more edges
    ///     are found in one iteration, the search fails.
    /// (5) See pixScanForEdge() for usage of the thresholds and %maxwidth.
    /// (6) The thresholds must be at least 1, and the low threshold
    ///     cannot be larger than the high threshold.
    /// (7) If the low and high thresholds are both 1, this is equivalent
    ///     to pixClipBoxToForeground().
    /// </remarks>
    public void ClipBoxToEdges(Box? boxs, int lowthresh, int highthresh, int maxwidth,
                               int factor, out Pix? ppixd, out Box? pboxd)
    {
        const string func = "pixClipBoxToEdges";
        ppixd = null;
        pboxd = null;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");
        if (lowthresh < 1 || highthresh < 1 ||
            lowthresh > highthresh || maxwidth < 1)
            throw Lept.Arg(func, "invalid thresholds");
        // NOTE(upstream): L_MIN(1, factor) limits the sampling factor to at most 1;
        // L_MAX was probably intended.  See docs/UPSTREAM_NOTES.md.
        factor = Math.Min(1, factor);

        if (lowthresh == 1 && highthresh == 1) {
            ClipBoxToForeground(boxs, out ppixd, out pboxd);
            return;
        }

        GetDimensions(out int w, out int h, out _);
        Box boxt;
        if (boxs != null) {
            boxs.GetGeometry(out int bx, out int by, out int bw, out int bh);
            int cbw = Math.Min(bw, w - bx);
            int cbh = Math.Min(bh, h - by);
            if (cbw < 0 || cbh < 0)
                throw Lept.Arg(func, "box not within image");
            boxt = Box.Create(bx, by, cbw, cbh);
        } else {
            boxt = Box.Create(0, 0, w, h);
        }

        int left = 0, right = 0, top = 0, bottom = 0;
        int lfound = 0, rfound = 0, tfound = 0, bfound = 0;
        int change = 0;
        while (lfound == 0 || rfound == 0 || tfound == 0 || bfound == 0) {
            change = 0;
            if (lfound == 0) {
                if (ScanForEdge(boxt, lowthresh, highthresh, maxwidth,
                                factor, L.L_FROM_LEFT, out left)) {
                    lfound = 1;
                    change = 1;
                    Box.RelocateOneSide(boxt, boxt, left, L.L_FROM_LEFT);
                }
            }
            if (rfound == 0) {
                if (ScanForEdge(boxt, lowthresh, highthresh, maxwidth,
                                factor, L.L_FROM_RIGHT, out right)) {
                    rfound = 1;
                    change = 1;
                    Box.RelocateOneSide(boxt, boxt, right, L.L_FROM_RIGHT);
                }
            }
            if (tfound == 0) {
                if (ScanForEdge(boxt, lowthresh, highthresh, maxwidth,
                                factor, L.L_FROM_TOP, out top)) {
                    tfound = 1;
                    change = 1;
                    Box.RelocateOneSide(boxt, boxt, top, L.L_FROM_TOP);
                }
            }
            if (bfound == 0) {
                if (ScanForEdge(boxt, lowthresh, highthresh, maxwidth,
                                factor, L.L_FROM_BOT, out bottom)) {
                    bfound = 1;
                    change = 1;
                    Box.RelocateOneSide(boxt, boxt, bottom, L.L_FROM_BOT);
                }
            }

            if (change == 0) break;
        }

        if (change == 0) {
            Log.Error(func, "not all edges found");
            return;
        }

        Box boxd = Box.Create(left, top, right - left + 1, bottom - top + 1);
        ppixd = ClipRectangle(boxd, out _);
        pboxd = boxd;
    }

    // leptonica: pixScanForEdge (pix5.c)
    /// <summary>Finds the location of an edge in a 1 bpp image, scanning from one side.</summary>
    /// <param name="box">[optional] within which the search is conducted</param>
    /// <param name="lowthresh">threshold to choose clipping location</param>
    /// <param name="highthresh">threshold required to find an edge</param>
    /// <param name="maxwidth">max allowed width between low and high thresh locs</param>
    /// <param name="factor">sampling factor along pixel counting direction</param>
    /// <param name="scanflag">direction of scan; e.g., L_FROM_LEFT</param>
    /// <param name="ploc">location in scan direction of first black pixel</param>
    /// <returns>true if the edge is found; false if it is not found</returns>
    /// <remarks>
    /// (1) If there are no fg pixels, the position is set to 0.
    ///     Caller must check the return value!
    /// (2) Use %box == null to scan from edge of pixs
    /// (3) As the scan progresses, the location where the sum of
    ///     pixels equals or excees %lowthresh is noted (loc).  The
    ///     scan is stopped when the sum of pixels equals or exceeds
    ///     %highthresh.  If the scan distance between loc and that
    ///     point does not exceed %maxwidth, an edge is found and
    ///     its position is taken to be loc.  %maxwidth implicitly
    ///     sets a minimum on the required gradient of the edge.
    /// (4) The thresholds must be at least 1, and the low threshold
    ///     cannot be larger than the high threshold.
    /// </remarks>
    public bool ScanForEdge(Box? box, int lowthresh, int highthresh, int maxwidth,
                            int factor, int scanflag, out int ploc)
    {
        const string func = "pixScanForEdge";
        ploc = 0;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");
        if (lowthresh < 1 || highthresh < 1 ||
            lowthresh > highthresh || maxwidth < 1)
            throw Lept.Arg(func, "invalid thresholds");
        // NOTE(upstream): L_MIN(1, factor) limits the sampling factor to at most 1;
        // L_MAX was probably intended.  A factor <= 0 makes the loops below endless.
        factor = Math.Min(1, factor);

            /* Clip box to pixs if it exists */
        GetDimensions(out int bw, out int bh, out _);
        int bx, by;
        if (box != null) {
            Box? boxt = box.ClipToRectangleOrNull(bw, bh);
            if (boxt == null)
                throw Lept.Arg(func, "invalid box");
            boxt.GetGeometry(out bx, out by, out bw, out bh);
        } else {
            bx = by = 0;
        }
        int xstart = bx;
        int ystart = by;
        int xend = bx + bw - 1;
        int yend = by + bh - 1;

        uint[] data = Data;
        int wpl = Wpl;
        int foundmin = 0;
        int loc = 0;
        int sum;
        if (scanflag == L.L_FROM_LEFT) {
            for (int x = xstart; x <= xend; x++) {
                sum = 0;
                for (int y = ystart; y <= yend; y += factor) {
                    if (ArrayAccess.GetDataBit(data.AsSpan(y * wpl), x) != 0)
                        sum++;
                }
                if (foundmin == 0 && sum < lowthresh)
                    continue;
                if (foundmin == 0) {  /* save the loc of the beginning of the edge */
                    foundmin = 1;
                    loc = x;
                }
                if (sum >= highthresh) {
                    if (x - loc < maxwidth) {
                        ploc = loc;
                        return true;
                    } else {
                        return false;
                    }
                }
            }
        } else if (scanflag == L.L_FROM_RIGHT) {
            for (int x = xend; x >= xstart; x--) {
                sum = 0;
                for (int y = ystart; y <= yend; y += factor) {
                    if (ArrayAccess.GetDataBit(data.AsSpan(y * wpl), x) != 0)
                        sum++;
                }
                if (foundmin == 0 && sum < lowthresh)
                    continue;
                if (foundmin == 0) {
                    foundmin = 1;
                    loc = x;
                }
                if (sum >= highthresh) {
                    if (loc - x < maxwidth) {
                        ploc = loc;
                        return true;
                    } else {
                        return false;
                    }
                }
            }
        } else if (scanflag == L.L_FROM_TOP) {
            for (int y = ystart; y <= yend; y++) {
                sum = 0;
                ReadOnlySpan<uint> line = data.AsSpan(y * wpl);
                for (int x = xstart; x <= xend; x += factor) {
                    if (ArrayAccess.GetDataBit(line, x) != 0)
                        sum++;
                }
                if (foundmin == 0 && sum < lowthresh)
                    continue;
                if (foundmin == 0) {
                    foundmin = 1;
                    loc = y;
                }
                if (sum >= highthresh) {
                    if (y - loc < maxwidth) {
                        ploc = loc;
                        return true;
                    } else {
                        return false;
                    }
                }
            }
        } else if (scanflag == L.L_FROM_BOT) {
            for (int y = yend; y >= ystart; y--) {
                sum = 0;
                ReadOnlySpan<uint> line = data.AsSpan(y * wpl);
                for (int x = xstart; x <= xend; x += factor) {
                    if (ArrayAccess.GetDataBit(line, x) != 0)
                        sum++;
                }
                if (foundmin == 0 && sum < lowthresh)
                    continue;
                if (foundmin == 0) {
                    foundmin = 1;
                    loc = y;
                }
                if (sum >= highthresh) {
                    if (loc - y < maxwidth) {
                        ploc = loc;
                        return true;
                    } else {
                        return false;
                    }
                }
            }
        } else {
            throw Lept.Arg(func, "invalid scanflag");
        }

        return false;  /* edge not found */
    }

    /*---------------------------------------------------------------------*
     *           Extract pixel averages and reversals along lines          *
     *---------------------------------------------------------------------*/
    // leptonica: pixExtractOnLine (pix5.c)
    /// <param name="x1">one end point for line</param>
    /// <param name="y1">one end point for line</param>
    /// <param name="x2">another end pt for line</param>
    /// <param name="y2">another end pt for line</param>
    /// <param name="factor">sampling; >= 1</param>
    /// <returns>na of pixel values along line</returns>
    /// <remarks>
    /// (1) Input end points are clipped to the pix.
    /// (2) If the line is either horizontal, or closer to horizontal
    ///     than to vertical, the points will be extracted from left
    ///     to right in the pix.  Likewise, if the line is vertical,
    ///     or closer to vertical than to horizontal, the points will
    ///     be extracted from top to bottom.
    /// (3) Can be used with numaCountReverals(), for example, to
    ///     characterize the intensity smoothness along a line.
    /// </remarks>
    public Numa ExtractOnLine(int x1, int y1, int x2, int y2, int factor)
    {
        const string func = "pixExtractOnLine";
        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 8)
            throw Lept.Arg(func, "d not 1 or 8 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pixs has a colormap");
        if (factor < 1) {
            Log.Warning(func, "factor must be >= 1; setting to 1");
            factor = 1;
        }

            /* Clip line to the image */
        x1 = Math.Max(0, Math.Min(x1, w - 1));
        x2 = Math.Max(0, Math.Min(x2, w - 1));
        y1 = Math.Max(0, Math.Min(y1, h - 1));
        y2 = Math.Max(0, Math.Min(y2, h - 1));

        uint val;
        Numa na;
        if (x1 == x2 && y1 == y2) {
            GetPixel(x1, y1, out val);
            na = Numa.Create(1);
            na.AddNumber(val);
            return na;
        }

        int direction;
        if (y1 == y2)
            direction = L.L_HORIZONTAL_LINE;
        else if (x1 == x2)
            direction = L.L_VERTICAL_LINE;
        else
            direction = L.L_OBLIQUE_LINE;

        int xmin, ymin, xmax, ymax;
        na = Numa.Create(0);
        if (direction == L.L_HORIZONTAL_LINE) {  /* plot against x */
            xmin = Math.Min(x1, x2);
            xmax = Math.Max(x1, x2);
            na.SetParameters(xmin, factor);
            for (int i = xmin; i <= xmax; i += factor) {
                GetPixel(i, y1, out val);
                na.AddNumber(val);
            }
        } else if (direction == L.L_VERTICAL_LINE) {  /* plot against y */
            ymin = Math.Min(y1, y2);
            ymax = Math.Max(y1, y2);
            na.SetParameters(ymin, factor);
            for (int i = ymin; i <= ymax; i += factor) {
                GetPixel(x1, i, out val);
                na.AddNumber(val);
            }
        } else {  /* direction == L_OBLIQUE_LINE */
            Pta pta;
            // NOTE(upstream): the quotient is an integer division, so the slope
            // is truncated toward zero before it is converted to double.
            double slope = (double)((y2 - y1) / (x2 - x1));
            if (((slope < 0) ? -slope : slope) < 1.0) {  /* quasi-horizontal */
                xmin = Math.Min(x1, x2);
                xmax = Math.Max(x1, x2);
                ymin = (xmin == x1) ? y1 : y2;  /* pt that goes with xmin */
                ymax = (ymin == y1) ? y2 : y1;  /* pt that goes with xmax */
                pta = GeneratePtaLine(xmin, ymin, xmax, ymax);
                na.SetParameters(xmin, (float)factor);
            } else {  /* quasi-vertical */
                ymin = Math.Min(y1, y2);
                ymax = Math.Max(y1, y2);
                xmin = (ymin == y1) ? x1 : x2;  /* pt that goes with ymin */
                xmax = (xmin == x1) ? x2 : x1;  /* pt that goes with ymax */
                pta = GeneratePtaLine(xmin, ymin, xmax, ymax);
                na.SetParameters(ymin, (float)factor);
            }
            int npts = pta.Count;
            for (int i = 0; i < npts; i += factor) {
                pta.GetPt(i, out float x, out float y);
                GetPixel((int)x, (int)y, out val);
                na.AddNumber(val);
            }
        }

        return na;
    }

    // leptonica: pixAverageOnLine (pix5.c)
    /// <param name="x1">starting pt for line</param>
    /// <param name="y1">starting pt for line</param>
    /// <param name="x2">end pt for line</param>
    /// <param name="y2">end pt for line</param>
    /// <param name="factor">sampling; >= 1</param>
    /// <returns>average of pixel values along line</returns>
    /// <remarks>
    /// (1) The line must be either horizontal or vertical, so either
    ///     y1 == y2 (horizontal) or x1 == x2 (vertical).
    /// (2) If horizontal, x1 must be &lt;= x2.
    ///     If vertical, y1 must be &lt;= y2.
    ///     characterize the intensity smoothness along a line.
    /// (3) Input end points are clipped to the pix.
    /// </remarks>
    public float AverageOnLine(int x1, int y1, int x2, int y2, int factor)
    {
        const string func = "pixAverageOnLine";
        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 8)
            throw Lept.Arg(func, "d not 1 or 8 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pixs has a colormap");
        if (x1 > x2 || y1 > y2)
            throw Lept.Arg(func, "x1 > x2 or y1 > y2");

        int direction;
        if (y1 == y2) {
            x1 = Math.Max(0, x1);
            x2 = Math.Min(w - 1, x2);
            y1 = Math.Max(0, Math.Min(y1, h - 1));
            direction = L.L_HORIZONTAL_LINE;
        } else if (x1 == x2) {
            y1 = Math.Max(0, y1);
            y2 = Math.Min(h - 1, y2);
            x1 = Math.Max(0, Math.Min(x1, w - 1));
            direction = L.L_VERTICAL_LINE;
        } else {
            throw Lept.Arg(func, "line neither horiz nor vert");
        }

        if (factor < 1) {
            Log.Warning(func, "factor must be >= 1; setting to 1");
            factor = 1;
        }

        uint[] data = Data;
        int wpl = Wpl;
        float sum = 0;
        int count = 0;
        int i, j;
        if (direction == L.L_HORIZONTAL_LINE) {
            ReadOnlySpan<uint> line = data.AsSpan(y1 * wpl);
            for (j = x1, count = 0; j <= x2; count++, j += factor) {
                if (d == 1)
                    sum += ArrayAccess.GetDataBit(line, j);
                else  /* d == 8 */
                    sum += ArrayAccess.GetDataByte(line, j);
            }
        } else if (direction == L.L_VERTICAL_LINE) {
            for (i = y1, count = 0; i <= y2; count++, i += factor) {
                ReadOnlySpan<uint> line = data.AsSpan(i * wpl);
                if (d == 1)
                    sum += ArrayAccess.GetDataBit(line, x1);
                else  /* d == 8 */
                    sum += ArrayAccess.GetDataByte(line, x1);
            }
        }

        return sum / (float)count;
    }

    // leptonica: pixAverageIntensityProfile (pix5.c)
    /// <param name="fract">fraction of image width or height to be used</param>
    /// <param name="dir">averaging direction: L_HORIZONTAL_LINE or L_VERTICAL_LINE</param>
    /// <param name="first">span of rows or columns to measure</param>
    /// <param name="last">span of rows or columns to measure</param>
    /// <param name="factor1">sampling along fast scan direction; >= 1</param>
    /// <param name="factor2">sampling along slow scan direction; >= 1</param>
    /// <returns>na of the average intensity profile</returns>
    /// <remarks>
    /// (1) If d != 1 bpp, colormaps are removed and the result
    ///     is converted to 8 bpp.
    /// (2) If %dir == L_HORIZONTAL_LINE, the intensity is averaged
    ///     along each horizontal raster line (sampled by %factor1),
    ///     and the profile is the array of these averages in the
    ///     vertical direction between %first and %last raster lines,
    ///     and sampled by %factor2.
    /// (3) If %dir == L_VERTICAL_LINE, the intensity is averaged
    ///     along each vertical line (sampled by %factor1),
    ///     and the profile is the array of these averages in the
    ///     horizontal direction between %first and %last columns,
    ///     and sampled by %factor2.
    /// (4) The averages are measured over the central %fract of the image.
    ///     Use %fract == 1.0 to average across the entire width or height.
    /// </remarks>
    public Numa AverageIntensityProfile(float fract, int dir, int first, int last,
                                        int factor1, int factor2)
    {
        const string func = "pixAverageIntensityProfile";
        if (fract < 0.0 || fract > 1.0)
            throw Lept.Arg(func, "fract < 0.0 or > 1.0");
        if (dir != L.L_HORIZONTAL_LINE && dir != L.L_VERTICAL_LINE)
            throw Lept.Arg(func, "invalid direction");
        if (first < 0) first = 0;
        if (last < first)
            throw Lept.Arg(func, "last must be >= first");
        if (factor1 < 1) {
            Log.Warning(func, "factor1 must be >= 1; setting to 1");
            factor1 = 1;
        }
        if (factor2 < 1) {
            Log.Warning(func, "factor2 must be >= 1; setting to 1");
            factor2 = 1;
        }

            /* Use 1 or 8 bpp, without colormap */
        Pix pixr;
        if (Colormap != null)
            pixr = RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        else
            pixr = Clone(this);
        pixr.GetDimensions(out int w, out int h, out int d);
        Pix pixg;
        if (d == 1)
            pixg = Clone(pixr);
        else
            pixg = pixr.ConvertTo8(false);

        Numa nad = Numa.Create(0);  /* output: samples in slow scan direction */
        nad.SetParameters(0, factor2);
        if (dir == L.L_HORIZONTAL_LINE) {
            int start = (int)(0.5 * (1.0 - fract) * (float)w);
            int end = w - start;
            if (last > h - 1) {
                Log.Warning(func, "last > h - 1; clipping");
                last = h - 1;
            }
            for (int i = first; i <= last; i += factor2) {
                float ave = pixg.AverageOnLine(start, i, end, i, factor1);
                nad.AddNumber(ave);
            }
        } else if (dir == L.L_VERTICAL_LINE) {
            int start = (int)(0.5 * (1.0 - fract) * (float)h);
            int end = h - start;
            if (last > w - 1) {
                Log.Warning(func, "last > w - 1; clipping");
                last = w - 1;
            }
            for (int j = first; j <= last; j += factor2) {
                float ave = pixg.AverageOnLine(j, start, j, end, factor1);
                nad.AddNumber(ave);
            }
        }

        return nad;
    }

    // leptonica: pixReversalProfile (pix5.c)
    /// <param name="fract">fraction of image width or height to be used</param>
    /// <param name="dir">profile direction: L_HORIZONTAL_LINE or L_VERTICAL_LINE</param>
    /// <param name="first">span of rows or columns to measure</param>
    /// <param name="last">span of rows or columns to measure</param>
    /// <param name="minreversal">minimum change in intensity to trigger a reversal</param>
    /// <param name="factor1">sampling along raster line (fast scan); >= 1</param>
    /// <param name="factor2">sampling of raster lines (slow scan); >= 1</param>
    /// <returns>na of reversal profile</returns>
    /// <remarks>
    /// (1) If d != 1 bpp, colormaps are removed and the result
    ///     is converted to 8 bpp.
    /// (2) If %dir == L_HORIZONTAL_LINE, the the reversals are counted
    ///     along each horizontal raster line (sampled by %factor1),
    ///     and the profile is the array of these sums in the
    ///     vertical direction between %first and %last raster lines,
    ///     and sampled by %factor2.
    /// (3) If %dir == L_VERTICAL_LINE, the the reversals are counted
    ///     along each vertical column (sampled by %factor1),
    ///     and the profile is the array of these sums in the
    ///     horizontal direction between %first and %last columns,
    ///     and sampled by %factor2.
    /// (4) For each row or column, the reversals are summed over the
    ///     central %fract of the image.  Use %fract == 1.0 to sum
    ///     across the entire width (of row) or height (of column).
    /// (5) %minreversal is the relative change in intensity that is
    ///     required to resolve peaks and valleys.  A typical number for
    ///     locating text in 8 bpp might be 50.  For 1 bpp, minreversal
    ///     must be 1.
    /// (6) The reversal profile is simply the number of reversals
    ///     in a row or column, vs the row or column index.
    /// </remarks>
    public Numa ReversalProfile(float fract, int dir, int first, int last,
                                int minreversal, int factor1, int factor2)
    {
        const string func = "pixReversalProfile";
        if (fract < 0.0 || fract > 1.0)
            throw Lept.Arg(func, "fract < 0.0 or > 1.0");
        if (dir != L.L_HORIZONTAL_LINE && dir != L.L_VERTICAL_LINE)
            throw Lept.Arg(func, "invalid direction");
        if (first < 0) first = 0;
        if (last < first)
            throw Lept.Arg(func, "last must be >= first");
        if (factor1 < 1) {
            Log.Warning(func, "factor1 must be >= 1; setting to 1");
            factor1 = 1;
        }
        if (factor2 < 1) {
            Log.Warning(func, "factor2 must be >= 1; setting to 1");
            factor2 = 1;
        }

            /* Use 1 or 8 bpp, without colormap */
        Pix pixr;
        if (Colormap != null)
            pixr = RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        else
            pixr = Clone(this);
        pixr.GetDimensions(out int w, out int h, out int d);
        Pix pixg;
        if (d == 1) {
            pixg = Clone(pixr);
            minreversal = 1;  /* enforce this */
        } else {
            pixg = pixr.ConvertTo8(false);
        }

        Numa nad = Numa.Create(0);  /* output: samples in slow scan direction */
        nad.SetParameters(0, factor2);
        if (dir == L.L_HORIZONTAL_LINE) {
            int start = (int)(0.5 * (1.0 - fract) * (float)w);
            int end = w - start;
            if (last > h - 1) {
                Log.Warning(func, "last > h - 1; clipping");
                last = h - 1;
            }
            for (int i = first; i <= last; i += factor2) {
                Numa naline = pixg.ExtractOnLine(start, i, end, i, factor1);
                naline.CountReversals(minreversal, out int nr, out _);
                nad.AddNumber(nr);
            }
        } else if (dir == L.L_VERTICAL_LINE) {
            int start = (int)(0.5 * (1.0 - fract) * (float)h);
            int end = h - start;
            if (last > w - 1) {
                Log.Warning(func, "last > w - 1; clipping");
                last = w - 1;
            }
            for (int j = first; j <= last; j += factor2) {
                Numa naline = pixg.ExtractOnLine(j, start, j, end, factor1);
                naline.CountReversals(minreversal, out int nr, out _);
                nad.AddNumber(nr);
            }
        }

        return nad;
    }

    /*---------------------------------------------------------------------*
     *                 Extract windowed variance along a line              *
     *---------------------------------------------------------------------*/
    // leptonica: pixWindowedVarianceOnLine (pix5.c)
    /// <param name="dir">L_HORIZONTAL_LINE or L_VERTICAL_LINE</param>
    /// <param name="loc">location of the constant coordinate for the line</param>
    /// <param name="c1">end point coordinates for the line</param>
    /// <param name="c2">end point coordinates for the line</param>
    /// <param name="size">window size; must be > 1</param>
    /// <returns>windowed square root of variance</returns>
    /// <remarks>
    /// (1) The returned variance array traverses the line starting
    ///     from the smallest coordinate, min(c1,c2).
    /// (2) Line end points are clipped to pixs.
    /// (3) The reference point for the variance calculation is the center of
    ///     the window.  Therefore, the numa start parameter from
    ///     pixExtractOnLine() is incremented by %size/2,
    ///     to align the variance values with the pixel coordinate.
    /// (4) The square root of the variance is the RMS deviation from the mean.
    /// </remarks>
    public Numa WindowedVarianceOnLine(int dir, int loc, int c1, int c2, int size)
    {
        const string func = "pixWindowedVarianceOnLine";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not defined or not 8bpp");
        if (size < 2)
            throw Lept.Arg(func, "window size must be > 1");
        if (dir != L.L_HORIZONTAL_LINE && dir != L.L_VERTICAL_LINE)
            throw Lept.Arg(func, "invalid direction");
        GetDimensions(out int w, out int h, out _);
        int maxloc = (dir == L.L_HORIZONTAL_LINE) ? h - 1 : w - 1;
        if (loc < 0 || loc > maxloc)
            throw Lept.Arg(func, "invalid line position");

            /* Clip line to the image */
        int cmin = Math.Min(c1, c2);
        int cmax = Math.Max(c1, c2);
        maxloc = (dir == L.L_HORIZONTAL_LINE) ? w - 1 : h - 1;
        cmin = Math.Max(0, Math.Min(cmin, maxloc));
        cmax = Math.Max(0, Math.Min(cmax, maxloc));
        int n = cmax - cmin + 1;

            /* Generate pta along the line */
        Pta pta = Pta.Create(n);
        if (dir == L.L_HORIZONTAL_LINE) {
            for (int i = cmin; i <= cmax; i++)
                pta.AddPt(i, loc);
        } else {  /* vertical line */
            for (int i = cmin; i <= cmax; i++)
                pta.AddPt(loc, i);
        }

            /* Get numa of pixel values on the line */
        Numa na1 = Numa.Create(n);
        na1.SetParameters(cmin, 1);
        for (int i = 0; i < n; i++) {
            pta.GetIPt(i, out int x, out int y);
            GetPixel(x, y, out uint val);
            na1.AddNumber(val);
        }
        float[] array = na1.GetFArray(L.L_NOCOPY);

            /* Compute root variance on overlapping windows */
        Numa nad = Numa.Create(n);
        nad.SetParameters(cmin + size / 2, 1);
        float norm = 1.0f / (float)size;
        for (int i = 0; i < n - size; i++) {  /* along the line */
            double sum1 = 0, sum2 = 0;
            for (int j = 0; j < size; j++) {  /* over the window */
                uint val = (uint)array[i + j];
                sum1 += val;
                sum2 += (double)(val) * val;
            }
            double ave = norm * sum1;
            double var = norm * sum2 - ave * ave;
            if (var < 0)  /* avoid small negative values from rounding effects */
                var = 0.0;
            float rootvar = (float)Math.Sqrt(var);
            nad.AddNumber(rootvar);
        }

        return nad;
    }

    /*---------------------------------------------------------------------*
     *              Extract min/max of pixel values near lines             *
     *---------------------------------------------------------------------*/
    // leptonica: pixMinMaxNearLine (pix5.c)
    /// <param name="x1">starting pt for line</param>
    /// <param name="y1">starting pt for line</param>
    /// <param name="x2">end pt for line</param>
    /// <param name="y2">end pt for line</param>
    /// <param name="dist">distance to search from line in each direction</param>
    /// <param name="direction">L_SCAN_NEGATIVE, L_SCAN_POSITIVE, L_SCAN_BOTH</param>
    /// <param name="pnamin">minimum values</param>
    /// <param name="pnamax">maximum values</param>
    /// <param name="pminave">average of minimum values</param>
    /// <param name="pmaxave">average of maximum values</param>
    /// <remarks>
    /// (1) If the line is more horizontal than vertical, the values
    ///     are computed for [x1, x2], and the pixels are taken
    ///     below and/or above the local y-value.  Otherwise, the
    ///     values are computed for [y1, y2] and the pixels are taken
    ///     to the left and/or right of the local x value.
    /// (2) %direction specifies which side (or both sides) of the
    ///     line are scanned for min and max values.
    /// (3) There are two ways to tell if the returned values of min
    ///     and max averages are valid: the returned values cannot be
    ///     negative and the function must return 0.
    ///     (C: the function reports an error, here an exception, if there
    ///     are no sampled points within the image.)
    /// (4) All accessed pixels are clipped to the pix.
    /// </remarks>
    public void MinMaxNearLine(int x1, int y1, int x2, int y2, int dist, int direction,
                               out Numa? pnamin, out Numa? pnamax,
                               out float pminave, out float pmaxave)
    {
        const string func = "pixMinMaxNearLine";
        pnamin = null;
        pnamax = null;
        pminave = L.UNDEF;
        pmaxave = L.UNDEF;
        GetDimensions(out int w, out int h, out int d);
        if (d != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs not 8 bpp or has colormap");
        dist = (dist < 0) ? -1 * dist : dist;  /* L_ABS */
        if (direction != L.L_SCAN_NEGATIVE && direction != L.L_SCAN_POSITIVE &&
            direction != L.L_SCAN_BOTH)
            throw Lept.Arg(func, "invalid direction");

        Pta pta = GeneratePtaLine(x1, y1, x2, y2);
        int n = pta.Count;
        int dx = x1 - x2;
        int dir = (((dx < 0) ? -1 * dx : dx) == n - 1) ? L.L_HORIZ : L.L_VERT;
        Numa namin = Numa.Create(n);
        Numa namax = Numa.Create(n);
        int negloc = -dist;
        int posloc = dist;
        if (direction == L.L_SCAN_NEGATIVE)
            posloc = 0;
        else if (direction == L.L_SCAN_POSITIVE)
            negloc = 0;
        for (int i = 0; i < n; i++) {
            pta.GetIPt(i, out int x, out int y);
            int minval = 255;
            int maxval = 0;
            bool found = false;
            uint val;
            if (dir == L.L_HORIZ) {
                if (x < 0 || x >= w) continue;
                for (int j = negloc; j <= posloc; j++) {
                    if (y + j < 0 || y + j >= h) continue;
                    GetPixel(x, y + j, out val);
                    found = true;
                    if (val < minval) minval = (int)val;
                    if (val > maxval) maxval = (int)val;
                }
            } else {  /* dir == L_VERT */
                if (y < 0 || y >= h) continue;
                for (int j = negloc; j <= posloc; j++) {
                    if (x + j < 0 || x + j >= w) continue;
                    GetPixel(x + j, y, out val);
                    found = true;
                    if (val < minval) minval = (int)val;
                    if (val > maxval) maxval = (int)val;
                }
            }
            if (found) {
                namin.AddNumber(minval);
                namax.AddNumber(maxval);
            }
        }

        n = namin.Count;
        if (n == 0)
            throw Lept.Fail(func, "no output from this line");

        float sum = namin.GetSum();
        pminave = sum / n;
        sum = namax.GetSum();
        pmaxave = sum / n;
        pnamin = namin;
        pnamax = namax;
    }

    /*---------------------------------------------------------------------*
     *                     Rank row and column transforms                  *
     *---------------------------------------------------------------------*/
    // leptonica: pixRankRowTransform (pix5.c)
    /// <returns>pixd with pixels sorted in each row, from min to max value</returns>
    /// <remarks>
    /// (1) The time is O(n) in the number of pixels and runs about
    ///     100 Mpixels/sec on a 3 GHz machine.
    /// </remarks>
    public Pix RankRowTransform()
    {
        const string func = "pixRankRowTransform";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pixs has a colormap");

        GetDimensions(out int w, out int h, out _);
        Pix pixd = CreateTemplate(this);
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpl = Wpl;
        int[] histo = new int[256];
        for (int i = 0; i < h; i++) {
            Array.Clear(histo);
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpl, wpl);
            Span<uint> lined = datad.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w; j++) {
                int val = ArrayAccess.GetDataByte(lines, j);
                histo[val]++;
            }
            for (int m = 0, j = 0; m < 256; m++) {
                for (int k = 0; k < histo[m]; k++, j++)
                    ArrayAccess.SetDataByte(lined, j, m);
            }
        }

        return pixd;
    }

    // leptonica: pixRankColumnTransform (pix5.c)
    /// <returns>pixd with pixels sorted in each column, from min to max value</returns>
    /// <remarks>
    /// (1) The time is O(n) in the number of pixels and runs about
    ///     50 Mpixels/sec on a 3 GHz machine.
    /// </remarks>
    public Pix RankColumnTransform()
    {
        const string func = "pixRankColumnTransform";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pixs has a colormap");

        GetDimensions(out int w, out int h, out _);
        Pix pixd = CreateTemplate(this);
        int wpl = Wpl;
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int[] lines8 = GetLinePtrs();
        int[] lined8 = pixd.GetLinePtrs();
        int[] histo = new int[256];
        for (int j = 0; j < w; j++) {
            Array.Clear(histo);
            for (int i = 0; i < h; i++) {
                int val = ArrayAccess.GetDataByte(datas.AsSpan(lines8[i], wpl), j);
                histo[val]++;
            }
            for (int m = 0, i = 0; m < 256; m++) {
                for (int k = 0; k < histo[m]; k++, i++)
                    ArrayAccess.SetDataByte(datad.AsSpan(lined8[i], wpl), j, m);
            }
        }

        return pixd;
    }

}
