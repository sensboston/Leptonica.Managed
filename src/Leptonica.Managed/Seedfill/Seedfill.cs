using System;
using System.Collections.Generic;

namespace Leptonica;

/*
 *  seedfill.c: Binary and grayscale seedfill, and distance function.
 *
 *      Binary seedfill (source: Luc Vincent)
 *               Pix.SeedfillBinary
 *               Pix.SeedfillBinaryRestricted
 *               (static seedfillBinaryLow: SeedfillLow.cs)
 *
 *      Applications of binary seedfill to find and fill holes,
 *      remove c.c. touching the border and fill bg from border:
 *               Pix.HolesByFilling
 *               Pix.FillClosedBorders
 *               Pix.ExtractBorderConnComps
 *               Pix.RemoveBorderConnComps
 *               Pix.FillBgFromBorder
 *
 *      Hole-filling of components to bounding rectangle
 *               Pix.FillHolesToBoundingRect
 *
 *      Gray seedfill (source: Luc Vincent:fast-hybrid-grayscale-reconstruction)
 *               Pix.SeedfillGray
 *               Pix.SeedfillGrayInv
 *               (static seedfillGrayLow, seedfillGrayInvLow: SeedfillLow.cs)
 *
 *      Gray seedfill (source: Luc Vincent: sequential-reconstruction algorithm)
 *               Pix.SeedfillGraySimple
 *               Pix.SeedfillGrayInvSimple
 *               (static seedfillGrayLowSimple, seedfillGrayInvLowSimple: SeedfillLow.cs)
 *
 *      Gray seedfill variations
 *               Pix.SeedfillGrayBasin
 *
 *      Distance function (source: Luc Vincent)
 *               Pix.DistanceFunction
 *               (static distanceFunctionLow: SeedfillLow.cs)
 *
 *      Seed spread (based on distance function)
 *               Pix.Seedspread
 *               (static seedspreadLow: SeedfillLow.cs)
 *
 *      Local extrema:
 *               Pix.LocalExtrema
 *               private Pix.QualifyLocalMinima
 *               Pix.SelectedLocalExtrema
 *               Pix.FindEqualValues
 *
 *      Selection of minima in mask of connected components
 *               Pix.SelectMinInConnComp
 *
 *      Removal of seeded connected components from a mask
 *               Pix.RemoveSeededComponents
 *
 *
 *           ITERATIVE RASTER-ORDER SEEDFILL
 *
 *      The basic method in the Vincent seedfill (aka reconstruction)
 *      algorithm is simple.  We describe here the situation for
 *      binary seedfill.  Pixels are sampled in raster order in
 *      the seed image.  If they are 4-connected to ON pixels
 *      either directly above or to the left, and are not masked
 *      out by the mask image, they are turned on (or remain on).
 *      (Ditto for 8-connected, except you need to check 3 pixels
 *      on the previous line as well as the pixel to the left
 *      on the current line.  This is extra computational work
 *      for relatively little gain, so it is preferable
 *      in most situations to use the 4-connected version.)
 *      The algorithm proceeds from UR to LL of the image, and
 *      then reverses and sweeps up from LL to UR.
 *      These double sweeps are iterated until there is no change.
 *      At this point, the seed has entirely filled the region it
 *      is allowed to, as delimited by the mask image.
 *
 *      The grayscale seedfill is a straightforward generalization
 *      of the binary seedfill, and is described in seedfillLowGray().
 *
 *      For some applications, the filled seed will later be OR'd
 *      with the negative of the mask.   This is used, for example,
 *      when you flood fill into a 4-connected region of OFF pixels
 *      and you want the result after those pixels are turned ON.
 *
 *      Note carefully that the mask we use delineates which pixels
 *      are allowed to be ON as the seed is filled.  We will call this
 *      a "filling mask".  As the seed expands, it is repeatedly
 *      ANDed with the filling mask: s & fm.  The process can equivalently
 *      be formulated using the inverse of the filling mask, which
 *      we will call a "blocking mask": bm = ~fm.   As the seed
 *      expands, the blocking mask is repeatedly used to prevent
 *      the seed from expanding into the blocking mask.  This is done
 *      by set subtracting the blocking mask from the expanded seed:
 *      s - bm.  Set subtraction of the blocking mask is equivalent
 *      to ANDing with the inverse of the blocking mask: s & (~bm).
 *      But from the inverse relation between blocking and filling
 *      masks, this is equal to s & fm, which proves the equivalence.
 *
 *      For efficiency, the pixels can be taken in larger units
 *      for processing, but still in raster order.  It is natural
 *      to take them in 32-bit words.  The outline of the work
 *      to be done for 4-cc (not including special cases for boundary
 *      words, such as the first line or the last word in each line)
 *      is as follows.  Let the filling mask be m.  The
 *      seed is to fill "under" the mask; i.e., limited by an AND
 *      with the mask.  Let the current word be w, the word
 *      in the line above be wa, and the previous word in the
 *      current line be wp.   Let t be a temporary word that
 *      is used in computation.  Note that masking is performed by
 *      w & m.  (If we had instead used a "blocking" mask, we
 *      would perform masking by the set subtraction operation,
 *      w - m, which is defined to be w & ~m.)
 *
 *      The entire operation can be implemented with shifts,
 *      logical operations and tests.  For each word in the seed image
 *      there are two steps.  The first step is to OR the word with
 *      the word above and with the rightmost pixel in wp (call it "x").
 *      Because wp is shifted one pixel to its right, "x" is ORed
 *      to the leftmost pixel of w.  We then clip to the ON pixels in
 *      the mask.  The result is
 *               t  <--  (w | wa | x000... ) & m
 *      We've now finished taking data from above and to the left.
 *      The second step is to allow filling to propagate horizontally
 *      in t, always making sure that it is properly masked at each
 *      step.  So if filling can be done (i.e., t is neither all 0s
 *      nor all 1s), iteratively take:
 *           t  <--  (t | (t >> 1) | (t << 1)) & m
 *      until t stops changing.  Then write t back into w.
 *
 *      Finally, the boundary conditions require we note that in doing
 *      the above steps:
 *          (a) The words in the first row have no wa
 *          (b) The first word in each row has no wp in that row
 *          (c) The last word in each row must be masked so that
 *              pixels don't propagate beyond the right edge of the
 *              actual image.  (This is easily accomplished by
 *              setting the out-of-bound pixels in m to OFF.)
 *
 *  Error returns of the C code (NULL / 1) are thrown as exceptions. Functions with optional output pointers
 *  (pixLocalExtrema, pixSelectMinInConnComp) always compute all outputs.
 */
public sealed partial class Pix
{
    /*-----------------------------------------------------------------------*
     *              Vincent's Iterative Binary Seedfill method               *
     *-----------------------------------------------------------------------*/
    // leptonica: pixSeedfillBinary (seedfill.c)
    /// <summary>Binary seedfill (aka "binary reconstruction").</summary>
    /// <param name="pixd">[optional]; can be null, equal to pixs, or different from pixs; 1 bpp</param>
    /// <param name="pixs">1 bpp seed</param>
    /// <param name="pixm">1 bpp filling mask</param>
    /// <param name="connectivity">4 or 8</param>
    /// <returns>pixd always</returns>
    /// <remarks>
    /// (1) This is for binary seedfill (aka "binary reconstruction").
    /// (2) There are 3 cases:
    ///       (a) pixd == null (make a new pixd)
    ///       (b) pixd == pixs (in-place)
    ///       (c) pixd != pixs
    /// (3) If you know the case, use these patterns for clarity:
    ///       (a) pixd = Pix.SeedfillBinary(null, pixs, ...);
    ///       (b) Pix.SeedfillBinary(pixs, pixs, ...);
    ///       (c) Pix.SeedfillBinary(pixd, pixs, ...);
    /// (4) The resulting pixd contains the filled seed.  For some
    ///     applications you want to OR it with the inverse of
    ///     the filling mask.
    /// (5) The input seed and mask images can be different sizes, but
    ///     in typical use the difference, if any, would be only
    ///     a few pixels in each direction.  If the sizes differ,
    ///     the clipping is handled by the low-level function
    ///     seedfillBinaryLow().
    /// </remarks>
    public static Pix SeedfillBinary(Pix? pixd, Pix pixs, Pix pixm, int connectivity)
    {
        const string func = "pixSeedfillBinary";
        int i;
        int hd, hm, wpld, wplm;
        uint[] datad, datam;
        Pix pixt;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not in {4,8}");

        /* Prepare pixd as a copy of pixs if not identical */
        pixd = Copy(pixd, pixs);
        pixd.SetPadBits(0);  /* be safe: */
        pixm.SetPadBits(0);  /* avoid using uninitialized memory */

        /* pixt is used to test for completion */
        pixt = CreateTemplate(pixs);

        hd = pixd.Height;
        hm = pixm.Height;  /* included so seedfillBinaryLow() can clip */
        datad = pixd.Data;
        datam = pixm.Data;
        wpld = pixd.Wpl;
        wplm = pixm.Wpl;

        // On equal-sized images, process only rows reached by the fill.  Keep
        // the original low-level path for differently sized images, whose
        // clipping semantics are defined by seedfillBinaryLow().
        if (pixd.Width == pixm.Width && hd == hm) {
            SeedfillBinaryRowFrontier(datad, datam, hd, wpld, connectivity);
            pixd.SetPadBits(0);
            return pixd;
        }

        for (i = 0; i < MaxIters; i++) {
            Copy(pixt, pixd);
            SeedfillBinaryLow(datad, hd, wpld, datam, hm, wplm, connectivity);
            if (Equal(pixd, pixt))
                break;
        }

        return pixd;
    }

    private static uint SeedfillExpandHorizontalWord(uint bits, uint mask)
    {
        bits &= mask;
        while (true) {
            uint expanded = (bits | (bits << 1) | (bits >> 1)) & mask;
            if (expanded == bits)
                return bits;
            bits = expanded;
        }
    }

    // Return pixels in one neighboring row that are 4- or 8-adjacent to
    // this row's pixels in word j. Bits are stored most-significant first.
    private static uint SeedfillVerticalNeighbors(uint[] data, int row, int j,
                                                   int wpl, int connectivity)
    {
        uint center = data[row + j];
        if (connectivity == 4)
            return center;

        uint neighbors = center | (center << 1) | (center >> 1);
        if (j > 0)
            neighbors |= data[row + j - 1] << 31;
        if (j + 1 < wpl)
            neighbors |= data[row + j + 1] >> 31;
        return neighbors;
    }

    private static void SeedfillBinaryRowFrontier(uint[] data, uint[] mask, int height,
                                                   int wpl, int connectivity)
    {
        Queue<int> rows = new Queue<int>();
        bool[] queued = new bool[height];
        bool[] seedRows = new bool[height];

        // Clip the seed to the mask and enqueue only rows containing seed bits.
        for (int y = 0; y < height; y++) {
            int line = y * wpl;
            bool hasSeed = false;
            for (int j = 0; j < wpl; j++) {
                uint word = data[line + j] & mask[line + j];
                data[line + j] = word;
                hasSeed |= word != 0;
            }
            if (hasSeed) {
                rows.Enqueue(y);
                queued[y] = true;
                seedRows[y] = true;
            }
        }

        while (rows.Count > 0) {
            int y = rows.Dequeue();
            queued[y] = false;
            int line = y * wpl;
            // Seed rows must activate their neighbors even when their own row
            // already contains the complete horizontal fill.
            bool changed = seedRows[y];
            seedRows[y] = false;

            // Left-to-right pass: close horizontal runs inside each word and
            // carry reachability across word boundaries from the left.
            for (int j = 0; j < wpl; j++) {
                int index = line + j;
                uint old = data[index];
                uint word = old;
                if (y > 0)
                    word |= SeedfillVerticalNeighbors(data, line - wpl, j, wpl, connectivity);
                if (y + 1 < height)
                    word |= SeedfillVerticalNeighbors(data, line + wpl, j, wpl, connectivity);
                if (j > 0)
                    word |= data[index - 1] << 31;
                word = SeedfillExpandHorizontalWord(word, mask[index]);
                if (word != old) {
                    data[index] = word;
                    changed = true;
                }
            }

            // Right-to-left pass carries reachability from the opposite edge.
            for (int j = wpl - 1; j >= 0; j--) {
                int index = line + j;
                uint old = data[index];
                uint word = old;
                if (y > 0)
                    word |= SeedfillVerticalNeighbors(data, line - wpl, j, wpl, connectivity);
                if (y + 1 < height)
                    word |= SeedfillVerticalNeighbors(data, line + wpl, j, wpl, connectivity);
                if (j + 1 < wpl)
                    word |= data[index + 1] >> 31;
                word = SeedfillExpandHorizontalWord(word, mask[index]);
                if (word != old) {
                    data[index] = word;
                    changed = true;
                }
            }

            if (changed) {
                if (y > 0 && !queued[y - 1]) {
                    rows.Enqueue(y - 1);
                    queued[y - 1] = true;
                }
                if (y + 1 < height && !queued[y + 1]) {
                    rows.Enqueue(y + 1);
                    queued[y + 1] = true;
                }
            }
        }
    }

    // leptonica: pixSeedfillBinaryRestricted (seedfill.c)
    /// <summary>Binary seedfill that limits the fill distance into the mask.</summary>
    /// <param name="pixd">[optional]; can be null, equal to pixs, or different from pixs; 1 bpp</param>
    /// <param name="pixs">1 bpp seed</param>
    /// <param name="pixm">1 bpp filling mask</param>
    /// <param name="connectivity">4 or 8</param>
    /// <param name="xmax">max distance in x direction of fill into mask</param>
    /// <param name="ymax">max distance in y direction of fill into mask</param>
    /// <returns>pixd always</returns>
    /// <remarks>
    /// (1) See usage for pixSeedfillBinary(), which has unrestricted fill.
    ///     In pixSeedfillBinary(), the filling distance is unrestricted
    ///     and can be larger than pixs, depending on the topology of
    ///     th mask.
    /// (2) There are occasions where it is useful not to permit the
    ///     fill to go more than a certain distance into the mask.
    ///     %xmax specifies the maximum horizontal distance allowed
    ///     in the fill; %ymax does likewise in the vertical direction.
    /// (3) Operationally, the max "distance" allowed for the fill
    ///     is a linear distance from the original seed, independent
    ///     of the actual mask topology.
    /// (4) Another formulation of this problem, not implemented,
    ///     would use the manhattan distance from the seed, as
    ///     determined by a breadth-first search starting at the seed
    ///     boundaries and working outward where the mask fg allows.
    ///     How this might use the constraints of separate xmax and ymax
    ///     is not clear.
    /// </remarks>
    public static Pix SeedfillBinaryRestricted(Pix? pixd, Pix pixs, Pix pixm,
                                               int connectivity, int xmax, int ymax)
    {
        const string func = "pixSeedfillBinaryRestricted";
        int w, h;
        Pix pix1, pix2;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not in {4,8}");
        if (xmax == 0 && ymax == 0)  /* no filling permitted */
            return Clone(pixs);
        if (xmax < 0 || ymax < 0) {
            Log.Error(func, "xmax and ymax must be non-negative");
            return Clone(pixs);
        }

        /* Full fill from the seed into the mask. */
        pix1 = SeedfillBinary(null, pixs, pixm, connectivity);

        /* Dilate the seed.  This gives the maximal region where changes
         * are permitted.  Invert to get the region where pixs is
         * not allowed to change.  */
        pix2 = DilateCompBrick(null, pixs, 2 * xmax + 1, 2 * ymax + 1);
        Invert(pix2, pix2);

        /* Blank the region of pix1 specified by the fg of pix2.
         * This is not yet the final result, because it may have fg pixels
         * that are not accessible from the seed in the restricted distance.
         * For example, such pixels may be connected to the original seed,
         * but through a path that goes outside the permitted region. */
        pixs.GetDimensions(out w, out h, out _);
        pix1.Rasterop(0, 0, w, h, L.PIX_DST & L.PIX_NOT(L.PIX_SRC), pix2, 0, 0);

        /* To get the accessible pixels in the restricted region, do
         * a second seedfill from the original seed, using pix1 as
         * a mask.  The result, in pixd, will not have any bad fg
         * pixels that were in pix1. */
        pixd = SeedfillBinary(pixd, pixs, pix1, connectivity);

        return pixd;
    }

    // leptonica: pixHolesByFilling (seedfill.c)
    /// <summary>Finds the holes of the 1 bpp image by filling the background from the border.</summary>
    /// <param name="connectivity">4 or 8</param>
    /// <returns>pixd  inverted image of all holes</returns>
    /// <remarks>
    /// Action:
    ///     1 Start with 1-pixel black border on otherwise white pixd
    ///     2 Use the inverted pixs as the filling mask to fill in
    ///         all the pixels from the border to the pixs foreground
    ///     3 OR the result with pixs to have an image with all
    ///         ON pixels except for the holes.
    ///     4 Invert the result to get the holes as foreground
    ///
    /// (1) To get 4-c.c. holes of the 8-c.c. as foreground, use
    ///     4-connected filling; to get 8-c.c. holes of the 4-c.c.
    ///     as foreground, use 8-connected filling.
    /// </remarks>
    public Pix HolesByFilling(int connectivity)
    {
        const string func = "pixHolesByFilling";
        Pix pixsi, pixd;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");

        pixd = CreateTemplate(this);
        pixsi = Invert(null, this);

        pixd.SetOrClearBorder(1, 1, 1, 1, L.PIX_SET);
        SeedfillBinary(pixd, pixd, pixsi, connectivity);
        Or(pixd, pixd, this);
        Invert(pixd, pixd);
        return pixd;
    }

    // leptonica: pixFillClosedBorders (seedfill.c)
    /// <summary>Fills all topologically outer closed borders as connected components.</summary>
    /// <param name="connectivity">filling connectivity 4 or 8</param>
    /// <returns>pixd  all topologically outer closed borders are filled as connected comonents</returns>
    /// <remarks>
    /// (1) Start with 1-pixel black border on otherwise white pixd
    /// (2) Subtract input pixs to remove border pixels that were
    ///     also on the closed border
    /// (3) Use the inverted pixs as the filling mask to fill in
    ///     all the pixels from the outer border to the closed border
    ///     on pixs
    /// (4) Invert the result to get the filled component, including
    ///     the input border
    /// (5) If the borders are 4-c.c., use 8-c.c. filling, and v.v.
    /// (6) Closed borders within c.c. that represent holes, etc., are filled.
    /// </remarks>
    public Pix FillClosedBorders(int connectivity)
    {
        const string func = "pixFillClosedBorders";
        Pix pixsi, pixd;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");

        pixd = CreateTemplate(this);
        pixd.SetOrClearBorder(1, 1, 1, 1, L.PIX_SET);
        Subtract(pixd, pixd, this);
        pixsi = Invert(null, this);

        SeedfillBinary(pixd, pixd, pixsi, connectivity);
        Invert(pixd, pixd);

        return pixd;
    }

    // leptonica: pixExtractBorderConnComps (seedfill.c)
    /// <summary>Extracts all components of the 1 bpp image that touch the border.</summary>
    /// <param name="connectivity">filling connectivity 4 or 8</param>
    /// <returns>pixd  all pixels in the src that are in connected components touching the border</returns>
    public Pix ExtractBorderConnComps(int connectivity)
    {
        const string func = "pixExtractBorderConnComps";
        Pix pixd;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");

        /* Start with 1 pixel wide black border as seed in pixd */
        pixd = CreateTemplate(this);
        pixd.SetOrClearBorder(1, 1, 1, 1, L.PIX_SET);

        /* Fill in pixd from the seed, using pixs as the filling mask.
         * This fills all components from pixs that are touching the border. */
        SeedfillBinary(pixd, pixd, this, connectivity);

        return pixd;
    }

    // leptonica: pixRemoveBorderConnComps (seedfill.c)
    /// <summary>Removes all components of the 1 bpp image that touch the border.</summary>
    /// <param name="connectivity">filling connectivity 4 or 8</param>
    /// <returns>pixd  all pixels in the src that are not touching the border</returns>
    /// <remarks>(1) This removes all fg components touching the border.</remarks>
    public Pix RemoveBorderConnComps(int connectivity)
    {
        const string func = "pixRemoveBorderConnComps";
        Pix pixd;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");

        /* Fill from a 1 pixel wide seed at the border into all components
         * in pixs (the filling mask) that are touching the border */
        pixd = ExtractBorderConnComps(connectivity);

        /* Save in pixd only those components in pixs not touching the border */
        Xor(pixd, pixd, this);
        return pixd;
    }

    // leptonica: pixFillBgFromBorder (seedfill.c)
    /// <summary>Fills all background components touching the border to foreground.</summary>
    /// <param name="connectivity">filling connectivity 4 or 8</param>
    /// <returns>pixd with the background c.c. touching the border filled to foreground</returns>
    /// <remarks>
    /// (1) This fills all bg components touching the border to fg.
    ///     It is the photometric inverse of pixRemoveBorderConnComps().
    /// (2) Invert the result to get the "holes" left after this fill.
    ///     This can be done multiple times, extracting holes within
    ///     holes after each pair of fillings.  Specifically, this code
    ///     peels away n successive embeddings of components:
    ///         pix1 = &lt;initial image&gt;
    ///         for (i = 0; i &lt; 2 * n; i++) {
    ///              pix2 = pix1.FillBgFromBorder(8);
    ///              Pix.Invert(pix2, pix2);
    ///              pix1 = pix2;
    ///         }
    /// </remarks>
    public Pix FillBgFromBorder(int connectivity)
    {
        const string func = "pixFillBgFromBorder";
        Pix pixd;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");

        /* Invert to turn bg touching the border to a fg component.
         * Extract this by filling from a 1 pixel wide seed at the border. */
        Invert(this, this);
        pixd = ExtractBorderConnComps(connectivity);
        Invert(this, this);  /* restore pixs */

        /* Bit-or the filled bg component with pixs */
        Or(pixd, pixd, this);
        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *            Hole-filling of components to bounding rectangle           *
     *-----------------------------------------------------------------------*/
    // leptonica: pixFillHolesToBoundingRect (seedfill.c)
    /// <summary>Fills holes of the components, or the components to their bounding rectangles.</summary>
    /// <param name="minsize">min number of pixels in the hole</param>
    /// <param name="maxhfract">max hole area as fraction of fg pixels in the cc</param>
    /// <param name="minfgfract">min fg area as fraction of bounding rectangle</param>
    /// <returns>pixd   with some holes possibly filled and some c.c. possibly expanded to their bounding rects</returns>
    /// <remarks>
    /// (1) This does not fill holes that are smaller in area than 'minsize'.
    ///     Use %minsize = 0 and %maxhfract = 1.0 to fill all holes.
    /// (2) This does not fill holes with an area larger than
    ///     %maxhfract times the fg area of the c.c.
    ///     Use 1.0 to fill all holes.
    /// (3) This does not expand the fg of the c.c. to bounding rect if
    ///     the fg area is less than %minfgfract times the area of the
    ///     bounding rect.  Use 1.0 to skip expanding to the bounding rect.
    /// (4) The decisions are made as follows:
    ///      ~ Decide if we are filling the holes; if so, when using
    ///        the fg area, include the filled holes.
    ///      ~ Decide based on the fg area if we are filling to a bounding rect.
    ///        If so, do it.
    ///        If not, fill the holes if the condition is satisfied.
    /// (5) The choice of %minsize depends on the resolution.
    /// (6) For solidifying image mask regions on printed materials,
    ///     which tend to be rectangular, values for %maxhfract
    ///     and %minfgfract around 0.5 are reasonable.
    /// </remarks>
    public Pix FillHolesToBoundingRect(int minsize, float maxhfract, float minfgfract)
    {
        const string func = "pixFillHolesToBoundingRect";
        int i, x, y, w, h, n, nfg, nh, ntot, area;
        int[] tab;
        float hfract;   /* measured hole fraction */
        float fgfract;  /* measured fg fraction */
        Boxa boxa;
        Pix pixd, pixfg, pixh;
        Pixa pixa;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        maxhfract = (float)Math.Min(Math.Max((double)maxhfract, 0.0), 1.0);
        minfgfract = (float)Math.Min(Math.Max((double)minfgfract, 0.0), 1.0);

        pixd = Copy(null, this);
        boxa = pixd.ConnComp(out pixa, 8);
        n = boxa.Count;
        tab = MakePixelSumTab8();
        for (i = 0; i < n; i++) {
            boxa.GetBoxGeometry(i, out x, out y, out w, out h);
            area = w * h;
            if (area < minsize)
                continue;
            pixfg = pixa.GetPix(i, L.L_COPY);
            pixh = pixfg.HolesByFilling(4);  /* holes only */
            nfg = pixfg.CountPixels(tab);
            nh = pixh.CountPixels(tab);
            hfract = (float)nh / (float)nfg;
            ntot = nfg;
            if (hfract <= maxhfract)  /* we will fill the holes (at least) */
                ntot = nfg + nh;
            fgfract = (float)ntot / (float)area;
            if (fgfract >= minfgfract) {  /* fill to bounding rect */
                pixfg.SetAll();
                pixd.Rasterop(x, y, w, h, L.PIX_SRC, pixfg, 0, 0);
            } else if (hfract <= maxhfract) {  /* fill just the holes */
                pixd.Rasterop(x, y, w, h, L.PIX_DST | L.PIX_SRC, pixh, 0, 0);
            }
        }
        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *               Vincent's hybrid Grayscale Seedfill method              *
     *-----------------------------------------------------------------------*/
    // leptonica: pixSeedfillGray (seedfill.c)
    /// <summary>Grayscale seedfill, in place on this seed; the mask is above or at the seed.</summary>
    /// <param name="pixm">8 bpp filling mask</param>
    /// <param name="connectivity">4 or 8</param>
    /// <remarks>
    /// (1) This is an in-place filling operation on the seed, pixs,
    ///     where the clipping mask is always above or at the level
    ///     of the seed as it is filled.
    /// (2) For details of the operation, see the description in
    ///     seedfillGrayLow() and the code there.
    /// (3) As an example of use, see the description in pixHDome().
    ///     There, the seed is an image where each pixel is a fixed
    ///     amount smaller than the corresponding mask pixel.
    /// (4) Reference paper :
    ///       L. Vincent, Morphological grayscale reconstruction in image
    ///       analysis: applications and efficient algorithms, IEEE Transactions
    ///       on  Image Processing, vol. 2, no. 2, pp. 176-201, 1993.
    /// </remarks>
    public void SeedfillGray(Pix pixm, int connectivity)
    {
        const string func = "pixSeedfillGray";
        int h, w, wpls, wplm;
        uint[] datas, datam;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not defined or not 8 bpp");
        if (pixm.Depth != 8)
            throw Lept.Arg(func, "pixm not defined or not 8 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not in {4,8}");

        /* Make sure the sizes of seed and mask images are the same */
        if (!SizesEqual(this, pixm))
            throw Lept.Arg(func, "pixs and pixm sizes differ");

        datas = Data;
        datam = pixm.Data;
        wpls = Wpl;
        wplm = pixm.Wpl;
        GetDimensions(out w, out h, out _);
        SeedfillGrayLow(datas, w, h, wpls, datam, wplm, connectivity);
    }

    // leptonica: pixSeedfillGrayInv (seedfill.c)
    /// <summary>Grayscale seedfill, in place on this seed; the mask is below or at the seed.</summary>
    /// <param name="pixm">8 bpp filling mask</param>
    /// <param name="connectivity">4 or 8</param>
    /// <remarks>
    /// (1) This is an in-place filling operation on the seed, pixs,
    ///     where the clipping mask is always below or at the level
    ///     of the seed as it is filled.  Think of filling up a basin
    ///     to a particular level, given by the maximum seed value
    ///     in the basin.  Outside the filled region, the mask
    ///     is above the filling level.
    /// (2) Contrast this with pixSeedfillGray(), where the clipping mask
    ///     is always above or at the level of the fill.  An example
    ///     of its use is the hdome fill, where the seed is an image
    ///     where each pixel is a fixed amount smaller than the
    ///     corresponding mask pixel.
    /// (3) The basin fill, pixSeedfillGrayBasin(), is a special case
    ///     where the seed pixel values are generated from the mask,
    ///     and where the implementation uses pixSeedfillGray() by
    ///     inverting both the seed and mask.
    /// </remarks>
    public void SeedfillGrayInv(Pix pixm, int connectivity)
    {
        const string func = "pixSeedfillGrayInv";
        int h, w, wpls, wplm;
        uint[] datas, datam;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not defined or not 8 bpp");
        if (pixm.Depth != 8)
            throw Lept.Arg(func, "pixm not defined or not 8 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not in {4,8}");

        /* Make sure the sizes of seed and mask images are the same */
        if (!SizesEqual(this, pixm))
            throw Lept.Arg(func, "pixs and pixm sizes differ");

        datas = Data;
        datam = pixm.Data;
        wpls = Wpl;
        wplm = pixm.Wpl;
        GetDimensions(out w, out h, out _);
        SeedfillGrayInvLow(datas, w, h, wpls, datam, wplm, connectivity);
    }

    /*-----------------------------------------------------------------------*
     *             Vincent's Iterative Grayscale Seedfill method             *
     *-----------------------------------------------------------------------*/
    // leptonica: pixSeedfillGraySimple (seedfill.c)
    /// <summary>Iterative grayscale seedfill, in place on this seed; the mask is above or at the seed.</summary>
    /// <param name="pixm">8 bpp filling mask</param>
    /// <param name="connectivity">4 or 8</param>
    /// <remarks>
    /// (1) This is an in-place filling operation on the seed, pixs,
    ///     where the clipping mask is always above or at the level
    ///     of the seed as it is filled.
    /// (2) For details of the operation, see the description in
    ///     seedfillGrayLowSimple() and the code there.
    /// (3) As an example of use, see the description in pixHDome().
    ///     There, the seed is an image where each pixel is a fixed
    ///     amount smaller than the corresponding mask pixel.
    /// (4) Reference paper :
    ///       L. Vincent, Morphological grayscale reconstruction in image
    ///       analysis: applications and efficient algorithms, IEEE Transactions
    ///       on  Image Processing, vol. 2, no. 2, pp. 176-201, 1993.
    /// </remarks>
    public void SeedfillGraySimple(Pix pixm, int connectivity)
    {
        const string func = "pixSeedfillGraySimple";
        int i, h, w, wpls, wplm;
        uint[] datas, datam;
        Pix pixt;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not defined or not 8 bpp");
        if (pixm.Depth != 8)
            throw Lept.Arg(func, "pixm not defined or not 8 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not in {4,8}");

        /* Make sure the sizes of seed and mask images are the same */
        if (!SizesEqual(this, pixm))
            throw Lept.Arg(func, "pixs and pixm sizes differ");

        /* This is used to test for completion */
        pixt = CreateTemplate(this);

        datas = Data;
        datam = pixm.Data;
        wpls = Wpl;
        wplm = pixm.Wpl;
        GetDimensions(out w, out h, out _);
        for (i = 0; i < MaxIters; i++) {
            Copy(pixt, this);
            SeedfillGrayLowSimple(datas, w, h, wpls, datam, wplm, connectivity);
            if (Equal(this, pixt))
                break;
        }
    }

    // leptonica: pixSeedfillGrayInvSimple (seedfill.c)
    /// <summary>Iterative grayscale seedfill, in place on this seed; the mask is below or at the seed.</summary>
    /// <param name="pixm">8 bpp filling mask</param>
    /// <param name="connectivity">4 or 8</param>
    /// <remarks>
    /// (1) This is an in-place filling operation on the seed, pixs,
    ///     where the clipping mask is always below or at the level
    ///     of the seed as it is filled.  Think of filling up a basin
    ///     to a particular level, given by the maximum seed value
    ///     in the basin.  Outside the filled region, the mask
    ///     is above the filling level.
    /// (2) Contrast this with pixSeedfillGraySimple(), where the clipping mask
    ///     is always above or at the level of the fill.  An example
    ///     of its use is the hdome fill, where the seed is an image
    ///     where each pixel is a fixed amount smaller than the
    ///     corresponding mask pixel.
    /// </remarks>
    public void SeedfillGrayInvSimple(Pix pixm, int connectivity)
    {
        const string func = "pixSeedfillGrayInvSimple";
        int i, h, w, wpls, wplm;
        uint[] datas, datam;
        Pix pixt;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not defined or not 8 bpp");
        if (pixm.Depth != 8)
            throw Lept.Arg(func, "pixm not defined or not 8 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not in {4,8}");

        /* Make sure the sizes of seed and mask images are the same */
        if (!SizesEqual(this, pixm))
            throw Lept.Arg(func, "pixs and pixm sizes differ");

        /* This is used to test for completion */
        pixt = CreateTemplate(this);

        datas = Data;
        datam = pixm.Data;
        wpls = Wpl;
        wplm = pixm.Wpl;
        GetDimensions(out w, out h, out _);
        for (i = 0; i < MaxIters; i++) {
            Copy(pixt, this);
            SeedfillGrayInvLowSimple(datas, w, h, wpls, datam, wplm, connectivity);
            if (Equal(this, pixt))
                break;
        }
    }

    /*-----------------------------------------------------------------------*
     *                         Gray seedfill variations                      *
     *-----------------------------------------------------------------------*/
    // leptonica: pixSeedfillGrayBasin (seedfill.c)
    /// <summary>Fills from a seed within basins defined by a filling mask.</summary>
    /// <param name="pixm">8 bpp basin-type filling mask</param>
    /// <param name="delta">amount of seed value above mask</param>
    /// <param name="connectivity">4 or 8</param>
    /// <returns>pixd filled seed</returns>
    /// <remarks>
    /// This is called on pixb, the binary mask giving seed locations.
    /// (1) This fills from a seed within basins defined by a filling mask.
    ///     The seed value(s) are greater than the corresponding
    ///     filling mask value, and the result has the bottoms of
    ///     the basins raised by the initial seed value.
    /// (2) The seed has value 255 except where pixb has fg (1), which
    ///     are the seed 'locations'.  At the seed locations, the seed
    ///     value is the corresponding value of the mask pixel in pixm
    ///     plus %delta.  If %delta == 0, we return a copy of pixm.
    /// (3) The actual filling is done using the standard grayscale filling
    ///     operation on the inverse of the mask and using the inverse
    ///     of the seed image.  After filling, we return the inverse of
    ///     the filled seed.
    /// (4) As an example of use: pixm can describe a grayscale image
    ///     of text, where the (dark) text pixels are basins of
    ///     low values; pixb can identify the local minima in pixm (say, at
    ///     the bottom of the basins); and delta is the amount that we wish
    ///     to raise (lighten) the basins.  We construct the seed
    ///     (a.k.a marker) image from pixb, pixm and %delta.
    /// </remarks>
    public Pix SeedfillGrayBasin(Pix pixm, int delta, int connectivity)
    {
        const string func = "pixSeedfillGrayBasin";
        Pix pixbi, pixmi, pixsd;

        if (Depth != 1)
            throw Lept.Arg(func, "pixb undefined or not 1 bpp");
        if (pixm.Depth != 8)
            throw Lept.Arg(func, "pixm undefined or not 8 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not in {4,8}");

        if (delta <= 0) {
            Log.Warning(func, "delta <= 0; returning a copy of pixm\n");
            return Copy(null, pixm);
        }

        /* Add delta to every pixel in pixm */
        pixsd = Copy(null, pixm);
        pixsd.AddConstantGray(delta);

        /* Prepare the seed.  Write 255 in all pixels of
         * ([pixm] + delta) where pixb is 0. */
        pixbi = Invert(null, this);
        pixsd.SetMasked(pixbi, 255);

        /* Fill the inverse seed, using the inverse clipping mask */
        pixmi = Invert(null, pixm);
        Invert(pixsd, pixsd);
        pixsd.SeedfillGray(pixmi, connectivity);

        /* Re-invert the filled seed */
        Invert(pixsd, pixsd);

        return pixsd;
    }

    /*-----------------------------------------------------------------------*
     *                   Vincent's Distance Function method                  *
     *-----------------------------------------------------------------------*/
    // leptonica: pixDistanceFunction (seedfill.c)
    /// <summary>Computes the distance of each pixel from the nearest background pixel.</summary>
    /// <param name="connectivity">4 or 8</param>
    /// <param name="outdepth">8 or 16 bits for pixd</param>
    /// <param name="boundcond">L_BOUNDARY_BG, L_BOUNDARY_FG</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This computes the distance of each pixel from the nearest
    ///     background pixel.  All bg pixels therefore have a distance of 0,
    ///     and the fg pixel distances increase linearly from 1 at the
    ///     boundary.  It can also be used to compute the distance of
    ///     each pixel from the nearest fg pixel, by inverting the input
    ///     image before calling this function.  Then all fg pixels have
    ///     a distance 0 and the bg pixel distances increase linearly
    ///     from 1 at the boundary.
    /// (2) The algorithm, described in Leptonica on the page on seed
    ///     filling and connected components, is due to Luc Vincent.
    ///     In brief, we generate an 8 or 16 bpp image, initialized
    ///     with the fg pixels of the input pix set to 1 and the
    ///     1-boundary pixels (i.e., the boundary pixels of width 1 on
    ///     the four sides set as either:
    ///       * L_BOUNDARY_BG: 0
    ///       * L_BOUNDARY_FG:  max
    ///     where max = 0xff for 8 bpp and 0xffff for 16 bpp.
    ///     Then do raster/anti-raster sweeps over all pixels interior
    ///     to the 1-boundary, where the value of each new pixel is
    ///     taken to be 1 more than the minimum of the previously-seen
    ///     connected pixels (using either 4 or 8 connectivity).
    ///     Finally, set the 1-boundary pixels using the mirrored method;
    ///     this removes the max values there.
    /// (3) Using L_BOUNDARY_BG clamps the distance to 0 at the
    ///     boundary.  Using L_BOUNDARY_FG allows the distance
    ///     at the image boundary to "float".
    /// (4) For 4-connected, one could initialize only the left and top
    ///     1-boundary pixels, and go all the way to the right
    ///     and bottom; then coming back reset left and top.  But we
    ///     instead use a method that works for both 4- and 8-connected.
    /// </remarks>
    public Pix DistanceFunction(int connectivity, int outdepth, int boundcond)
    {
        const string func = "pixDistanceFunction";
        int w, h, wpld;
        uint[] datad;
        Pix pixd;

        if (Depth != 1)
            throw Lept.Arg(func, "!pixs or pixs not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");
        if (outdepth != 8 && outdepth != 16)
            throw Lept.Arg(func, "outdepth not 8 or 16 bpp");
        if (boundcond != L.L_BOUNDARY_BG && boundcond != L.L_BOUNDARY_FG)
            throw Lept.Arg(func, "invalid boundcond");

        GetDimensions(out w, out h, out _);
        pixd = Create(w, h, outdepth);
        datad = pixd.Data;
        wpld = pixd.Wpl;

        /* Initialize the fg pixels to 1 and the bg pixels to 0 */
        pixd.SetMasked(this, 1);

        if (boundcond == L.L_BOUNDARY_BG) {
            DistanceFunctionLow(datad, w, h, outdepth, wpld, connectivity);
        } else {  /* L_BOUNDARY_FG: set boundary pixels to max val */
            pixd.Rasterop(0, 0, w, 1, L.PIX_SET, null, 0, 0);   /* top */
            pixd.Rasterop(0, h - 1, w, 1, L.PIX_SET, null, 0, 0);   /* bot */
            pixd.Rasterop(0, 0, 1, h, L.PIX_SET, null, 0, 0);   /* left */
            pixd.Rasterop(w - 1, 0, 1, h, L.PIX_SET, null, 0, 0);   /* right */

            DistanceFunctionLow(datad, w, h, outdepth, wpld, connectivity);

            /* Set each boundary pixel equal to the pixel next to it */
            pixd.SetMirroredBorder(1, 1, 1, 1);
        }

        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *                Seed spread (based on distance function)               *
     *-----------------------------------------------------------------------*/
    // leptonica: pixSeedspread (seedfill.c)
    /// <summary>Spreads the nonzero pixel values to fill the image with the nearest one.</summary>
    /// <param name="connectivity">4 or 8</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) The raster/anti-raster method for implementing this filling
    ///     operation was suggested by Ray Smith.
    /// (2) This takes an arbitrary set of nonzero pixels in pixs, which
    ///     can be sparse, and spreads (extrapolates) the values to
    ///     fill all the pixels in pixd with the nonzero value it is
    ///     closest to in pixs.  This is similar (though not completely
    ///     equivalent) to doing a Voronoi tiling of the image, with a
    ///     tile surrounding each pixel that has a nonzero value.
    ///     All pixels within a tile are then closer to its "central"
    ///     pixel than to any others.  Then assign the value of the
    ///     "central" pixel to each pixel in the tile.
    /// (3) This is implemented by computing a distance function in parallel
    ///     with the fill.  The distance function uses free boundary
    ///     conditions (assumed maxval outside), and it controls the
    ///     propagation of the pixels in pixd away from the nonzero
    ///     (seed) values.  This is done in 2 traversals (raster/antiraster).
    ///     In the raster direction, whenever the distance function
    ///     is nonzero, the spread pixel takes on the value of its
    ///     predecessor that has the minimum distance value.  In the
    ///     antiraster direction, whenever the distance function is nonzero
    ///     and its value is replaced by a smaller value, the spread
    ///     pixel takes the value of the predecessor with the minimum
    ///     distance value.
    /// (4) At boundaries where a pixel is equidistant from two
    ///     nearest nonzero (seed) pixels, the decision of which value
    ///     to use is arbitrary (greedy in search for minimum distance).
    ///     This can give rise to strange-looking results, particularly
    ///     for 4-connectivity where the L1 distance is computed from
    ///     steps in N,S,E and W directions (no diagonals).
    /// </remarks>
    public Pix Seedspread(int connectivity)
    {
        const string func = "pixSeedspread";
        int w, h, wplt, wplg;
        uint[] datat, datag;
        Pix pixm, pixt, pixg, pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "!pixs or pixs not 8 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");

        /* Add a 4 byte border to pixs.  This simplifies the computation. */
        pixg = AddBorder(4, 0);
        pixg.GetDimensions(out w, out h, out _);

        /* Initialize distance function pixt.  Threshold pixs to get
         * a 0 at the seed points where the pixs pixel is nonzero, and
         * a 1 at all points that need to be filled.  Use this as a
         * mask to set a 1 in pixt at all non-seed points.  Also, set all
         * pixt pixels in an interior boundary of width 1 to the
         * maximum value.   For debugging, to view the distance function,
         * use pixConvert16To8(pixt, L_LS_BYTE) on small images.  */
        pixm = pixg.ThresholdToBinary(1);
        pixt = Create(w, h, 16);
        pixt.SetMasked(pixm, 1);
        pixt.Rasterop(0, 0, w, 1, L.PIX_SET, null, 0, 0);   /* top */
        pixt.Rasterop(0, h - 1, w, 1, L.PIX_SET, null, 0, 0);   /* bot */
        pixt.Rasterop(0, 0, 1, h, L.PIX_SET, null, 0, 0);   /* left */
        pixt.Rasterop(w - 1, 0, 1, h, L.PIX_SET, null, 0, 0);   /* right */
        datat = pixt.Data;
        wplt = pixt.Wpl;

        /* Do the interpolation and remove the border. */
        datag = pixg.Data;
        wplg = pixg.Wpl;
        SeedspreadLow(datag, w, h, wplg, datat, wplt, connectivity);
        pixd = pixg.RemoveBorder(4);

        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *                              Local extrema                            *
     *-----------------------------------------------------------------------*/
    // leptonica: pixLocalExtrema (seedfill.c)
    /// <summary>Finds the local minima and maxima of the 8 bpp image.</summary>
    /// <param name="maxmin">max allowed for the min in a 3x3 neighborhood; use 0 for default which is to have no upper bound</param>
    /// <param name="minmax">min allowed for the max in a 3x3 neighborhood; use 0 for default which is to have no lower bound</param>
    /// <param name="pixmin">mask of local minima</param>
    /// <param name="pixmax">mask of local maxima</param>
    /// <remarks>
    /// (1) This gives the actual local minima and maxima.
    ///     A local minimum is a pixel whose surrounding pixels all
    ///     have values at least as large, and likewise for a local
    ///     maximum.  For the local minima, %maxmin is the upper
    ///     bound for the value of pixs.  Likewise, for the local maxima,
    ///     %minmax is the lower bound for the value of pixs.
    /// (2) The minima are found by starting with the erosion-and-equality
    ///     approach of pixSelectedLocalExtrema().  This is followed
    ///     by a qualification step, where each c.c. in the resulting
    ///     minimum mask is extracted, the pixels bordering it are
    ///     located, and they are queried.  If all of those pixels
    ///     are larger than the value of that minimum, it is a true
    ///     minimum and its c.c. is saved; otherwise the c.c. is
    ///     rejected.  Note that if a bordering pixel has the
    ///     same value as the minimum, it must then have a
    ///     neighbor that is smaller, so the component is not a
    ///     true minimum.
    /// (3) The maxima are found by inverting the image and looking
    ///     for the minima there.
    /// (4) The generated masks can be used as markers for
    ///     further operations.
    /// </remarks>
    public void LocalExtrema(int maxmin, int minmax, out Pix pixmin, out Pix pixmax)
    {
        const string func = "pixLocalExtrema";
        Pix pixt1, pixt2;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not defined or not 8 bpp");
        if (maxmin <= 0) maxmin = 254;
        if (minmax <= 0) minmax = 1;

        pixt1 = ErodeGray(3, 3);
        pixmin = FindEqualValues(pixt1);
        QualifyLocalMinima(pixmin, maxmin);

        pixt1 = Invert(null, this);
        pixt2 = pixt1.ErodeGray(3, 3);
        pixmax = pixt1.FindEqualValues(pixt2);
        pixt1.QualifyLocalMinima(pixmax, 255 - minmax);
    }

    // leptonica: pixQualifyLocalMinima (seedfill.c)
    /// <summary>Removes from pixm, in place, all c.c. that are not true local minima of this image.</summary>
    /// <param name="pixm">1 bpp mask of values equal to min in 3x3 neighborhood</param>
    /// <param name="maxval">max allowed for the min in a 3x3 neighborhood; use 0 for default which is to have no upper bound</param>
    /// <remarks>
    /// (1) This function acts in-place to remove all c.c. in pixm
    ///     that are not true local minima in pixs.  As seen in
    ///     pixLocalExtrema(), the input pixm are found by selecting those
    ///     pixels of pixs whose values do not change with a 3x3
    ///     grayscale erosion.  Here, we require that for each c.c.
    ///     in pixm, all pixels in pixs that correspond to the exterior
    ///     boundary pixels of the c.c. have values that are greater
    ///     than the value within the c.c.
    /// (2) The maximum allowed value for each local minimum can be
    ///     bounded with %maxval.  Use 0 for default, which is to have
    ///     no upper bound (equivalent to maxval == 254).
    /// </remarks>
    private void QualifyLocalMinima(Pix pixm, int maxval)
    {
        const string func = "pixQualifyLocalMinima";
        int n, i, j, k, x, y, w, h, xc, yc, wc, hc, xon, yon;
        int vals, wpls, wplc;
        bool ismin;
        uint val;
        uint[] datas, datac;
        int lines, linec;
        Boxa boxa;
        Pix pix1, pix2, pix3;
        Pixa pixa;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not defined or not 8 bpp");
        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm not defined or not 1 bpp");
        if (maxval <= 0) maxval = 254;

        GetDimensions(out w, out h, out _);
        datas = Data;
        wpls = Wpl;
        boxa = pixm.ConnComp(out pixa, 8);
        n = pixa.Count;
        for (k = 0; k < n; k++) {
            boxa.GetBoxGeometry(k, out xc, out yc, out wc, out hc);
            pix1 = pixa.GetPix(k, L.L_COPY);
            pix2 = pix1.AddBorder(1, 0);
            pix3 = DilateBrick(null, pix2, 3, 3);
            Xor(pix3, pix3, pix2);  /* exterior boundary pixels */
            datac = pix3.Data;
            wplc = pix3.Wpl;
            pix1.NextOnPixelInRaster(0, 0, out xon, out yon);
            GetPixel(xc + xon, yc + yon, out val);
            if (val > (uint)maxval) {  /* too large; erase */
                pixm.Rasterop(xc, yc, wc, hc, L.PIX_XOR, pix1, 0, 0);
                continue;
            }
            ismin = true;

            /* Check all values in pixs that correspond to the exterior
             * boundary pixels of the c.c. in pixm.  Verify that the
             * value in the c.c. is always less. */
            for (i = 0, y = yc - 1; i < hc + 2 && y >= 0 && y < h; i++, y++) {
                lines = y * wpls;
                linec = i * wplc;
                for (j = 0, x = xc - 1; j < wc + 2 && x >= 0 && x < w; j++, x++) {
                    if (ArrayAccess.GetDataBit(datac.AsSpan(linec), j) != 0) {
                        vals = GetByte(datas, lines, x);
                        if ((uint)vals <= val) {  /* not a minimum! */
                            ismin = false;
                            break;
                        }
                    }
                }
                if (!ismin)
                    break;
            }
            if (!ismin)  /* erase it */
                pixm.Rasterop(xc, yc, wc, hc, L.PIX_XOR, pix1, 0, 0);
        }
    }

    // leptonica: pixSelectedLocalExtrema (seedfill.c)
    /// <summary>Selects the local 3x3 minima and maxima that are far enough from each other.</summary>
    /// <param name="mindist">-1 for keeping all pixels; >= 0 specifies distance</param>
    /// <param name="pixmin">mask of local minima</param>
    /// <param name="pixmax">mask of local maxima</param>
    /// <remarks>
    /// (1) This selects those local 3x3 minima that are at least a
    ///     specified distance from the nearest local 3x3 maxima, and v.v.
    ///     for the selected set of local 3x3 maxima.
    ///     The local 3x3 minima is the set of pixels whose value equals
    ///     the value after a 3x3 brick erosion, and the local 3x3 maxima
    ///     is the set of pixels whose value equals the value after
    ///     a 3x3 brick dilation.
    /// (2) mindist is the minimum distance allowed between
    ///     local 3x3 minima and local 3x3 maxima, in an 8-connected sense.
    ///     mindist == 1 keeps all pixels found in step 1.
    ///     mindist == 0 removes all pixels from each mask that are
    ///     both a local 3x3 minimum and a local 3x3 maximum.
    ///     mindist == 1 removes any local 3x3 minimum pixel that touches a
    ///     local 3x3 maximum pixel, and likewise for the local maxima.
    ///     To make the decision, visualize each local 3x3 minimum pixel
    ///     as being surrounded by a square of size (2 * mindist + 1)
    ///     on each side, such that no local 3x3 maximum pixel is within
    ///     that square; and v.v.
    /// (3) The generated masks can be used as markers for further operations.
    /// </remarks>
    public void SelectedLocalExtrema(int mindist, out Pix pixmin, out Pix pixmax)
    {
        const string func = "pixSelectedLocalExtrema";
        Pix pixt, pixtmin, pixtmax;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not defined or not 8 bpp");

        pixt = ErodeGray(3, 3);
        pixmin = FindEqualValues(pixt);
        pixt = DilateGray(3, 3);
        pixmax = FindEqualValues(pixt);

        /* Remove all points that are within the prescribed distance
         * from each other. */
        if (mindist < 0) {  /* remove no points */
            /* pixmin and pixmax are the results */
        } else if (mindist == 0) {  /* remove points belonging to both sets */
            pixt = And(null, pixmin, pixmax);
            pixmin = Subtract(pixmin, pixmin, pixt);
            pixmax = Subtract(pixmax, pixmax, pixt);
        } else {
            pixtmin = DilateBrick(null, pixmin,
                                  2 * mindist + 1, 2 * mindist + 1);
            pixtmax = DilateBrick(null, pixmax,
                                  2 * mindist + 1, 2 * mindist + 1);
            pixmin = Subtract(pixmin, pixmin, pixtmax);
            pixmax = Subtract(pixmax, pixmax, pixtmin);
        }
    }

    // leptonica: pixFindEqualValues (seedfill.c)
    /// <summary>Returns a 1 bpp mask with ON pixels where the two 8 bpp images have equal values.</summary>
    /// <param name="pixs2">8 bpp</param>
    /// <returns>pixd 1 bpp mask</returns>
    /// <remarks>
    /// This is called on pixs1 (8 bpp).
    /// (1) The two images are aligned at the UL corner, and the returned
    ///     image has ON pixels where the pixels in pixs1 and pixs2
    ///     have equal values.
    /// </remarks>
    public Pix FindEqualValues(Pix pixs2)
    {
        const string func = "pixFindEqualValues";
        int w1, h1, w2, h2, w, h;
        int i, j, val1, val2, wpls1, wpls2, wpld;
        uint[] datas1, datas2, datad;
        int lines1, lines2, lined;
        Pix pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs1 undefined or not 8 bpp");
        if (pixs2.Depth != 8)
            throw Lept.Arg(func, "pixs2 undefined or not 8 bpp");
        GetDimensions(out w1, out h1, out _);
        pixs2.GetDimensions(out w2, out h2, out _);
        w = Math.Min(w1, w2);
        h = Math.Min(h1, h2);
        pixd = Create(w, h, 1);
        datas1 = Data;
        datas2 = pixs2.Data;
        datad = pixd.Data;
        wpls1 = Wpl;
        wpls2 = pixs2.Wpl;
        wpld = pixd.Wpl;

        for (i = 0; i < h; i++) {
            lines1 = i * wpls1;
            lines2 = i * wpls2;
            lined = i * wpld;
            for (j = 0; j < w; j++) {
                val1 = GetByte(datas1, lines1, j);
                val2 = GetByte(datas2, lines2, j);
                if (val1 == val2)
                    ArrayAccess.SetDataBit(datad.AsSpan(lined), j);
            }
        }

        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *             Selection of minima in mask connected components          *
     *-----------------------------------------------------------------------*/
    // leptonica: pixSelectMinInConnComp (seedfill.c)
    /// <summary>Finds a pixel of lowest value of this 8 bpp image in each 8-connected component of pixm.</summary>
    /// <param name="pixm">1 bpp</param>
    /// <param name="pta">pta of min pixel locations</param>
    /// <param name="nav">numa of minima values</param>
    /// <remarks>
    /// (1) For each 8 connected component in pixm, this finds
    ///     a pixel in pixs that has the lowest value, and saves
    ///     it in a Pta.  If several pixels in pixs have the same
    ///     minimum value, it picks the first one found.
    /// (2) For a mask pixm of true local minima, all pixels in each
    ///     connected component have the same value in pixs, so it is
    ///     fastest to select one of them using a special seedfill
    ///     operation.  Not yet implemented.
    /// </remarks>
    public void SelectMinInConnComp(Pix pixm, out Pta pta, out Numa nav)
    {
        const string func = "pixSelectMinInConnComp";
        int bx, by, bw, bh, i, j, c, n;
        int xs, ys, minx, miny, wpls, wplt, val, minval;
        uint[] datas, datat;
        int lines, linet;
        Boxa boxa;
        Pix pixt, pixs2, pixm2;
        Pixa pixa;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");
        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm undefined or not 1 bpp");

        /* Crop to the min size if necessary */
        CropToMatch(pixm, out pixs2, out pixm2);

        /* Find value and location of min value pixel in each component */
        boxa = pixm2.ConnComp(out pixa, 8);
        n = boxa.Count;
        pta = Pta.Create(n);
        nav = Numa.Create(n);
        datas = pixs2.Data;
        wpls = pixs2.Wpl;
        for (c = 0; c < n; c++) {
            pixt = pixa.GetPix(c, L.L_CLONE);
            boxa.GetBoxGeometry(c, out bx, out by, out bw, out bh);
            if (bw == 1 && bh == 1) {
                pta.AddPt(bx, by);
                nav.AddNumber(GetByte(datas, by * wpls, bx));
                continue;
            }
            datat = pixt.Data;
            wplt = pixt.Wpl;
            minx = miny = 1000000;
            minval = 256;
            for (i = 0; i < bh; i++) {
                ys = by + i;
                lines = ys * wpls;
                linet = i * wplt;
                for (j = 0; j < bw; j++) {
                    xs = bx + j;
                    if (ArrayAccess.GetDataBit(datat.AsSpan(linet), j) != 0) {
                        val = GetByte(datas, lines, xs);
                        if (val < minval) {
                            minval = val;
                            minx = xs;
                            miny = ys;
                        }
                    }
                }
            }
            pta.AddPt(minx, miny);
            nav.AddNumber(GetByte(datas, miny * wpls, minx));
        }
    }

    /*-----------------------------------------------------------------------*
     *            Removal of seeded connected components from a mask         *
     *-----------------------------------------------------------------------*/
    // leptonica: pixRemoveSeededComponents (seedfill.c)
    /// <summary>Removes each component in pixm for which there is at least one seed in pixs.</summary>
    /// <param name="pixd">[optional]; can be null or equal to pixm; 1 bpp</param>
    /// <param name="pixs">1 bpp seed</param>
    /// <param name="pixm">1 bpp filling mask</param>
    /// <param name="connectivity">4 or 8</param>
    /// <param name="bordersize">amount of border clearing</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This removes each component in pixm for which there is
    ///     at least one seed in pixs.  If pixd == null, this returns
    ///     the result in a new pixd.  Otherwise, it is an in-place
    ///     operation on pixm.  In no situation is pixs altered,
    ///     because we do the filling with a copy of pixs.
    /// (2) If bordersize > 0, it also clears all pixels within a
    ///     distance %bordersize of the edge of pixd.  This is here
    ///     because pixLocalExtrema() typically finds local minima
    ///     at the border.  Use %bordersize >= 2 to remove these.
    /// </remarks>
    public static Pix RemoveSeededComponents(Pix? pixd, Pix pixs, Pix pixm,
                                             int connectivity, int bordersize)
    {
        const string func = "pixRemoveSeededComponents";
        Pix pixt;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm undefined or not 1 bpp");
        if (pixd != null && pixd != pixm)
            throw Lept.Arg(func, "operation not inplace");

        pixt = Copy(null, pixs);
        SeedfillBinary(pixt, pixt, pixm, connectivity);
        pixd = Xor(pixd, pixm, pixt);
        if (bordersize > 0)
            pixd.SetOrClearBorder(bordersize, bordersize, bordersize,
                                  bordersize, L.PIX_CLR);
        return pixd;
    }
}
