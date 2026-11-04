using System;
using System.Numerics;

namespace Leptonica;

/*
 *  conncomp.c: Connected component counting and extraction, using
 *  Heckbert's stack-based filling algorithm.
 *
 *    4- and 8-connected components: counts, bounding boxes and images
 *
 *    Top-level calls:
 *          Boxa    ConnComp()
 *          Boxa    ConnCompPixa()
 *          Boxa    ConnCompBB()
 *          int     CountConnComp()
 *
 *    Identify the next c.c. to be erased:
 *          bool    NextOnPixelInRaster()
 *
 *    Erase the c.c., saving the b.b.:
 *          Box     SeedfillBB()
 *          Box     Seedfill4BB()
 *          Box     Seedfill8BB()
 *
 *    Just erase the c.c.:
 *          void    Seedfill()
 *          void    Seedfill4()
 *          void    Seedfill8()
 *
 *  The basic method in ConnCompBB() is very simple.  We scan the
 *  image in raster order, looking for the next ON pixel.  When it
 *  is found, we erase it and every pixel of the 4- or 8-connected
 *  component to which it belongs, using Heckbert's seedfill
 *  algorithm.  As pixels are erased, we keep track of the
 *  minimum rectangle that encloses all erased pixels; after
 *  the connected component has been erased, we save its
 *  bounding box in an array of boxes.  When all pixels in the
 *  image have been erased, we have an array that describes every
 *  4- or 8-connected component in terms of its bounding box.
 *
 *  ConnCompPixa() is a slight variation on ConnCompBB(),
 *  where we additionally save an array of images (in a Pixa)
 *  of each of the 4- or 8-connected components.  This is done trivially
 *  by maintaining two temporary images.  We erase a component from one,
 *  and use the bounding box to extract the pixels within the b.b.
 *  from each of the two images.  An XOR between these subimages
 *  gives the erased component.  Then we erase the component from the
 *  second image using the XOR again, with the extracted component
 *  placed on the second image at the location of the bounding box.
 *  Rasterop does all the work.  At the end, we have an array
 *  of the 4- or 8-connected components, as well as an array of the
 *  bounding boxes that describe where they came from in the original image.
 *
 *  If you just want the number of connected components, CountConnComp()
 *  is a bit faster than ConnCompBB(), because it doesn't have to
 *  keep track of the bounding rectangles for each c.c.
 *
 *  Differences from C:
 *    - ConnComp() always returns the pixa (it is empty when the input
 *      has no ON pixels); the overload without the pixa output is the
 *      C call with a NULL &pixa.
 *    - The static stack helpers pushFillsegBB(), pushFillseg() and
 *      popFillseg() are private; the FillSeg struct is a private class
 *      that is recycled through the auxiliary stack, as in C.
 *    - The "goto skip" into the do-loop of the seedfill functions
 *      is expressed with a flag.
 *    - The DEBUG output (disabled in C) is not ported.
 */

public sealed partial class Pix
{
    /// <summary>
    /// Run of pixels waiting to be investigated by the Heckbert seedfill
    /// algorithm (struct FillSeg).
    /// </summary>
    private sealed class FillSeg
    {
        public int xleft;    /* left edge of run */
        public int xright;   /* right edge of run */
        public int y;        /* run y  */
        public int dy;       /* parent segment direction: 1 above, -1 below) */
    }

    /*-----------------------------------------------------------------------*
     *                Bounding boxes of 4 Connected Components               *
     *-----------------------------------------------------------------------*/
    // leptonica: pixConnComp (conncomp.c)
    /// <summary>Bounding boxes and images of the connected components.</summary>
    /// <param name="pixa">pixa of each c.c.</param>
    /// <param name="connectivity">4 or 8</param>
    /// <returns>boxa</returns>
    /// <remarks>
    /// (1) This is the top-level call for getting bounding boxes or
    ///     a pixa of the components, and it can be used instead
    ///     of either pixConnCompBB() or pixConnCompPixa(), rsp.
    /// </remarks>
    public Boxa ConnComp(out Pixa pixa, int connectivity)
    {
        if (Depth != 1)
            throw Lept.Arg("pixConnComp", "pixs undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg("pixConnComp", "connectivity not 4 or 8");

        return ConnCompPixa(out pixa, connectivity);
    }

    /// <summary>Bounding boxes of the connected components (C call with a NULL &amp;pixa).</summary>
    /// <param name="connectivity">4 or 8</param>
    /// <returns>boxa</returns>
    public Boxa ConnComp(int connectivity)
    {
        if (Depth != 1)
            throw Lept.Arg("pixConnComp", "pixs undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg("pixConnComp", "connectivity not 4 or 8");

        return ConnCompBB(connectivity);
    }

    // leptonica: pixConnCompPixa (conncomp.c)
    /// <summary>Bounding boxes of the 4- or 8-connected components, with an image of each.</summary>
    /// <param name="pixa">pixa of each c.c.</param>
    /// <param name="connectivity">4 or 8</param>
    /// <returns>boxa</returns>
    /// <remarks>
    /// (1) This finds bounding boxes of 4- or 8-connected components
    ///     in a binary image, and saves images of each c.c
    ///     in a pixa array.
    /// (2) It sets up 2 temporary pix, and for each c.c. that is
    ///     located in raster order, it erases the c.c. from one pix,
    ///     then uses the b.b. to extract the c.c. from the two pix using
    ///     an XOR, and finally erases the c.c. from the second pix.
    /// (3) A clone of the returned boxa (where all boxes in the array
    ///     are clones) is inserted into the pixa.
    /// (4) If the input is valid, this always returns a boxa and a pixa.
    ///     If pixs is empty, the boxa and pixa will be empty.
    /// </remarks>
    public Boxa ConnCompPixa(out Pixa pixa, int connectivity)
    {
        const string func = "pixConnCompPixa";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");

        pixa = Pixa.Create(0);
        bool iszero = Zero();
        if (iszero)
            return Boxa.Create(1);  /* return empty boxa and empty pixa */

        SetPadBits(0);
        Pix pix1 = Copy(null, this);
        Pix pix2 = Copy(null, this);

        int h = Height;
        L_Stack stack = L_Stack.Create(h);
        L_Stack auxstack = L_Stack.Create(0);
        stack.AuxStack = auxstack;
        Boxa boxa = Boxa.Create(0);

        int x, y;
        int xstart = 0;
        int ystart = 0;
        while (true) {
            if (!pix1.NextOnPixelInRaster(xstart, ystart, out x, out y))
                break;

            Box? box = pix1.SeedfillBB(stack, x, y, connectivity);
            if (box == null)
                throw Lept.Fail(func, "box not made");
            boxa.AddBox(box, L.L_INSERT);

                /* Save the c.c. and remove from pix2 as well */
            Pix pix3 = pix1.ClipRectangle(box, out _)!;
            Pix pix4 = pix2.ClipRectangle(box, out _)!;
            Xor(pix3, pix3, pix4);
            pix2.Rasterop(box.X, box.Y, box.Width, box.Height, L.PIX_SRC ^ L.PIX_DST,
                          pix3, 0, 0);
            pixa.AddPix(pix3, L.L_INSERT);

            xstart = x;
            ystart = y;
        }

            /* Remove old boxa of pixa and replace with a copy */
        pixa.Boxa = boxa.Copy(L.L_COPY);
        return boxa;
    }

    // leptonica: pixConnCompBB (conncomp.c)
    /// <summary>Bounding boxes of the 4- or 8-connected components.</summary>
    /// <param name="connectivity">4 or 8</param>
    /// <returns>boxa</returns>
    /// <remarks>
    /// (1) Finds bounding boxes of 4- or 8-connected components
    ///     in a binary image.
    /// (2) This works on a copy of the input pix.  The c.c. are located
    ///     in raster order and erased one at a time.  In the process,
    ///     the b.b. is computed and saved.
    /// </remarks>
    public Boxa ConnCompBB(int connectivity)
    {
        const string func = "pixConnCompBB";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");

        bool iszero = Zero();
        if (iszero)
            return Boxa.Create(1);  /* return empty boxa */

        SetPadBits(0);
        Pix pix1 = Copy(null, this);

        int h = Height;
        L_Stack stack = L_Stack.Create(h);
        L_Stack auxstack = L_Stack.Create(0);
        stack.AuxStack = auxstack;
        Boxa boxa = Boxa.Create(0);

        int x, y;
        int xstart = 0;
        int ystart = 0;
        while (true) {
            if (!pix1.NextOnPixelInRaster(xstart, ystart, out x, out y))
                break;

            Box? box = pix1.SeedfillBB(stack, x, y, connectivity);
            if (box == null)
                throw Lept.Fail(func, "box not made");
            boxa.AddBox(box, L.L_INSERT);

            xstart = x;
            ystart = y;
        }

        return boxa;
    }

    // leptonica: pixCountConnComp (conncomp.c)
    /// <summary>Number of 4- or 8-connected components.</summary>
    /// <param name="connectivity">4 or 8</param>
    /// <returns>count of components</returns>
    /// <remarks>
    /// (1) This is the top-level call for getting the number of
    ///     4- or 8-connected components in a 1 bpp image.
    /// (2) It works on a copy of the input pix.  The c.c. are located
    ///     in raster order and erased one at a time.
    /// </remarks>
    public int CountConnComp(int connectivity)
    {
        const string func = "pixCountConnComp";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");

        int count = 0;  /* initialize the count to 0 */
        bool iszero = Zero();
        if (iszero)
            return 0;

        SetPadBits(0);
        Pix pix1 = Copy(null, this);
        int h = Height;
        L_Stack stack = L_Stack.Create(h);
        L_Stack auxstack = L_Stack.Create(0);
        stack.AuxStack = auxstack;

        int x, y;
        int xstart = 0;
        int ystart = 0;
        while (true) {
            if (!pix1.NextOnPixelInRaster(xstart, ystart, out x, out y))
                break;

            pix1.Seedfill(stack, x, y, connectivity);
            count++;
            xstart = x;
            ystart = y;
        }

        return count;
    }

    // leptonica: nextOnPixelInRaster (conncomp.c)
    /// <summary>Finds the next ON pixel in raster order.</summary>
    /// <param name="xstart">starting point for search</param>
    /// <param name="ystart">starting point for search</param>
    /// <param name="px">coord value of next ON pixel</param>
    /// <param name="py">coord value of next ON pixel</param>
    /// <returns>true if a pixel is found; false otherwise</returns>
    public bool NextOnPixelInRaster(int xstart, int ystart, out int px, out int py)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 1) {
            Log.Error("nextOnPixelInRaster", "pixs not 1 bpp");
            px = 0;
            py = 0;
            return false;
        }

        int wpl = Wpl;
        uint[] data = Data;
        return NextOnPixelInRasterLow(data, w, h, wpl, xstart, ystart, out px, out py);
    }

    // leptonica: nextOnPixelInRasterLow (conncomp.c)
    /// <param name="data">pix data</param>
    /// <param name="w">width</param>
    /// <param name="h">height</param>
    /// <param name="wpl">words per line</param>
    /// <param name="xstart">starting point for search</param>
    /// <param name="ystart">starting point for search</param>
    /// <param name="px">coord value of next ON pixel</param>
    /// <param name="py">coord value of next ON pixel</param>
    /// <returns>true if a pixel is found; false otherwise</returns>
    private static bool NextOnPixelInRasterLow(uint[] data, int w, int h, int wpl,
                                               int xstart, int ystart,
                                               out int px, out int py)
    {
        // Pixels are MSB-first in each 32-bit word. Skip zero words and use
        // LeadingZeroCount to locate the first set bit instead of testing up
        // to 32 individual pixels in every non-zero word.
        if (xstart < w) {
            int line = ystart * wpl;
            int wordIndex = xstart >> 5;
            int xbase = wordIndex << 5;
            uint word = data[line + wordIndex] & (uint.MaxValue >> (xstart & 31));
            int validBits = w - xbase;
            if (validBits < 32)
                word &= uint.MaxValue << (32 - validBits);
            if (word != 0) {
                px = xbase + BitOperations.LeadingZeroCount(word);
                py = ystart;
                return true;
            }

            // Remaining words on the starting row.
            for (wordIndex++, xbase += 32; xbase < w; wordIndex++, xbase += 32) {
                word = data[line + wordIndex];
                validBits = w - xbase;
                if (validBits < 32)
                    word &= uint.MaxValue << (32 - validBits);
                if (word != 0) {
                    px = xbase + BitOperations.LeadingZeroCount(word);
                    py = ystart;
                    return true;
                }
            }
        }

        // Search subsequent rows from their first word.
        for (int y = ystart + 1; y < h; y++) {
            int line = y * wpl;
            for (int wordIndex = 0, xbase = 0; xbase < w; wordIndex++, xbase += 32) {
                uint word = data[line + wordIndex];
                int validBits = w - xbase;
                if (validBits < 32)
                    word &= uint.MaxValue << (32 - validBits);
                if (word != 0) {
                    px = xbase + BitOperations.LeadingZeroCount(word);
                    py = y;
                    return true;
                }
            }
        }

        px = 0;
        py = 0;
        return false;
    }

    // leptonica: pixSeedfillBB (conncomp.c)
    /// <summary>Erases the connected component at the seed and returns its bounding box.</summary>
    /// <param name="stack">for holding fillsegs</param>
    /// <param name="x">location of seed pixel</param>
    /// <param name="y">location of seed pixel</param>
    /// <param name="connectivity">4 or 8</param>
    /// <returns>box, or null if the seed pixel is not ON</returns>
    /// <remarks>
    /// (1) This is the high-level interface to Paul Heckbert's
    ///     stack-based seedfill algorithm.
    /// </remarks>
    public Box? SeedfillBB(L_Stack stack, int x, int y, int connectivity)
    {
        if (Depth != 1)
            throw Lept.Arg("pixSeedfillBB", "pixs undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg("pixSeedfillBB", "connectivity not 4 or 8");

        if (connectivity == 4)
            return Seedfill4BB(stack, x, y);
        else  /* connectivity == 8 */
            return Seedfill8BB(stack, x, y);
    }

    // leptonica: pixSeedfill4BB (conncomp.c)
    /// <summary>Heckbert's 4-cc seedfill, returning the bounding box of the erased component.</summary>
    /// <param name="stack">for holding fillsegs</param>
    /// <param name="x">location of seed pixel</param>
    /// <param name="y">location of seed pixel</param>
    /// <returns>box, or null if the seed pixel is not ON</returns>
    /// <remarks>
    /// (1) This is Paul Heckbert's stack-based 4-cc seedfill algorithm.
    /// (2) This operates on the input 1 bpp pix to remove the fg seed
    ///     pixel, at (x,y), and all pixels that are 4-connected to it.
    ///     The seed pixel at (x,y) must initially be ON.
    /// (3) Returns the bounding box of the erased 4-cc component.
    /// (4) Reference: see Paul Heckbert's stack-based seed fill algorithm
    ///     in "Graphic Gems", ed. Andrew Glassner, Academic
    ///     Press, 1990.  The algorithm description is given
    ///     on pp. 275-277; working C code is on pp. 721-722.)
    ///     The code here follows Heckbert's exactly, except
    ///     we use function calls instead of macros for
    ///     pushing data on and popping data off the stack.
    ///     This makes sense to do because Heckbert's fixed-size
    ///     stack with macros is dangerous: images exist that
    ///     will overrun the stack and crash.   The stack utility
    ///     here grows dynamically as needed, and the fillseg
    ///     structures that are not in use are stored in another
    ///     stack for reuse.  It should be noted that the
    ///     overhead in the function calls (vs. macros) is negligible.
    /// </remarks>
    public Box? Seedfill4BB(L_Stack stack, int x, int y)
    {
        int xstart, x1 = 0, x2 = 0, dy = 0;
        int minx, maxx, miny, maxy;  /* for bounding box of this c.c. */

        if (Depth != 1)
            throw Lept.Arg("pixSeedfill4BB", "pixs undefined or not 1 bpp");
        if (stack.AuxStack == null)
            stack.AuxStack = L_Stack.Create(0);

        GetDimensions(out int w, out int h, out _);
        int xmax = w - 1;
        int ymax = h - 1;
        uint[] data = Data;
        int wpl = Wpl;

            /* Check pix value of seed; must be within the image and ON */
        if (x < 0 || x > xmax || y < 0 || y > ymax
            || ArrayAccess.GetDataBit(data.AsSpan(y * wpl), x) == 0)
            return null;

            /* Init stack to seed:
             * Must first init b.b. values to prevent valgrind from complaining;
             * then init b.b. boundaries correctly to seed.  */
        minx = miny = 100000;
        maxx = maxy = 0;
        PushFillsegBB(stack, x, x, y, 1, ymax, ref minx, ref maxx, ref miny, ref maxy);
        PushFillsegBB(stack, x, x, y + 1, -1, ymax, ref minx, ref maxx, ref miny, ref maxy);
        minx = maxx = x;
        miny = maxy = y;

        while (stack.Count > 0) {
                /* Pop segment off stack and fill a neighboring scan line */
            PopFillseg(stack, ref x1, ref x2, ref y, ref dy);
            Span<uint> line = data.AsSpan(y * wpl, wpl);

                /* A segment of scanline y - dy for x1 <= x <= x2 was
                 * previously filled.  We now explore adjacent pixels
                 * in scan line y.  There are three regions: to the
                 * left of x1 - 1, between x1 and x2, and to the right of x2.
                 * These regions are handled differently.  Leaks are
                 * possible expansions beyond the previous segment and
                 * going back in the -dy direction.  These can happen
                 * for x < x1 - 1 and for x > x2 + 1.  Any "leak" segments
                 * are plugged with a push in the -dy (opposite) direction.
                 * And any segments found anywhere are always extended
                 * in the +dy direction.  */
            for (x = x1; x >= 0 && (ArrayAccess.GetDataBit(line, x) == 1); x--)
                ArrayAccess.ClearDataBit(line, x);
            bool skip = x >= x1;  /* pix at x1 was off and was not cleared */
            if (!skip) {
                xstart = x + 1;
                if (xstart < x1 - 1)   /* leak on left? */
                    PushFillsegBB(stack, xstart, x1 - 1, y, -dy,
                                  ymax, ref minx, ref maxx, ref miny, ref maxy);

                x = x1 + 1;
            } else {
                xstart = 0;  /* set below, before it is used */
            }
            do {
                if (!skip) {
                    for (; x <= xmax && (ArrayAccess.GetDataBit(line, x) == 1); x++)
                        ArrayAccess.ClearDataBit(line, x);
                    PushFillsegBB(stack, xstart, x - 1, y, dy,
                                  ymax, ref minx, ref maxx, ref miny, ref maxy);
                    if (x > x2 + 1)   /* leak on right? */
                        PushFillsegBB(stack, x2 + 1, x - 1, y, -dy,
                                      ymax, ref minx, ref maxx, ref miny, ref maxy);
                }
                skip = false;
                for (x++; x <= x2 &&
                          x <= xmax &&
                          (ArrayAccess.GetDataBit(line, x) == 0); x++)
                    ;
                xstart = x;
            } while (x <= x2 && x <= xmax);
        }

        return Box.Create(minx, miny, maxx - minx + 1, maxy - miny + 1);
    }

    // leptonica: pixSeedfill8BB (conncomp.c)
    /// <summary>Heckbert's 8-cc seedfill, returning the bounding box of the erased component.</summary>
    /// <param name="stack">for holding fillsegs</param>
    /// <param name="x">location of seed pixel</param>
    /// <param name="y">location of seed pixel</param>
    /// <returns>box, or null if the seed pixel is not ON</returns>
    /// <remarks>
    /// (1) This is Paul Heckbert's stack-based 8-cc seedfill algorithm.
    /// (2) This operates on the input 1 bpp pix to remove the fg seed
    ///     pixel, at (x,y), and all pixels that are 8-connected to it.
    ///     The seed pixel at (x,y) must initially be ON.
    /// (3) Returns the bounding box of the erased 8-cc component.
    /// (4) Reference: see Paul Heckbert's stack-based seed fill algorithm
    ///     in "Graphic Gems", ed. Andrew Glassner, Academic
    ///     Press, 1990.  The algorithm description is given
    ///     on pp. 275-277; working C code is on pp. 721-722.)
    ///     The code here follows Heckbert's closely, except
    ///     the leak checks are changed for 8 connectivity.
    ///     See comments on pixSeedfill4BB() for more details.
    /// </remarks>
    public Box? Seedfill8BB(L_Stack stack, int x, int y)
    {
        int xstart, x1 = 0, x2 = 0, dy = 0;
        int minx, maxx, miny, maxy;  /* for bounding box of this c.c. */

        if (Depth != 1)
            throw Lept.Arg("pixSeedfill8BB", "pixs undefined or not 1 bpp");
        if (stack.AuxStack == null)
            stack.AuxStack = L_Stack.Create(0);

        GetDimensions(out int w, out int h, out _);
        int xmax = w - 1;
        int ymax = h - 1;
        uint[] data = Data;
        int wpl = Wpl;

            /* Check pix value of seed; must be ON */
        if (x < 0 || x > xmax || y < 0 || y > ymax
            || ArrayAccess.GetDataBit(data.AsSpan(y * wpl), x) == 0)
            return null;

            /* Init stack to seed:
             * Must first init b.b. values to prevent valgrind from complaining;
             * then init b.b. boundaries correctly to seed.  */
        minx = miny = 100000;
        maxx = maxy = 0;
        PushFillsegBB(stack, x, x, y, 1, ymax, ref minx, ref maxx, ref miny, ref maxy);
        PushFillsegBB(stack, x, x, y + 1, -1, ymax, ref minx, ref maxx, ref miny, ref maxy);
        minx = maxx = x;
        miny = maxy = y;

        while (stack.Count > 0) {
                /* Pop segment off stack and fill a neighboring scan line */
            PopFillseg(stack, ref x1, ref x2, ref y, ref dy);
            Span<uint> line = data.AsSpan(y * wpl, wpl);

                /* A segment of scanline y - dy for x1 <= x <= x2 was
                 * previously filled.  We now explore adjacent pixels
                 * in scan line y.  There are three regions: to the
                 * left of x1, between x1 and x2, and to the right of x2.
                 * These regions are handled differently.  Leaks are
                 * possible expansions beyond the previous segment and
                 * going back in the -dy direction.  These can happen
                 * for x < x1 and for x > x2.  Any "leak" segments
                 * are plugged with a push in the -dy (opposite) direction.
                 * And any segments found anywhere are always extended
                 * in the +dy direction.  */
            for (x = x1 - 1; x >= 0 && (ArrayAccess.GetDataBit(line, x) == 1); x--)
                ArrayAccess.ClearDataBit(line, x);
            bool skip = x >= x1 - 1;  /* pix at x1 - 1 was off and was not cleared */
            if (!skip) {
                xstart = x + 1;
                if (xstart < x1)   /* leak on left? */
                    PushFillsegBB(stack, xstart, x1 - 1, y, -dy,
                                  ymax, ref minx, ref maxx, ref miny, ref maxy);

                x = x1;
            } else {
                xstart = 0;  /* set below, before it is used */
            }
            do {
                if (!skip) {
                    for (; x <= xmax && (ArrayAccess.GetDataBit(line, x) == 1); x++)
                        ArrayAccess.ClearDataBit(line, x);
                    PushFillsegBB(stack, xstart, x - 1, y, dy,
                                  ymax, ref minx, ref maxx, ref miny, ref maxy);
                    if (x > x2)   /* leak on right? */
                        PushFillsegBB(stack, x2 + 1, x - 1, y, -dy,
                                      ymax, ref minx, ref maxx, ref miny, ref maxy);
                }
                skip = false;
                for (x++; x <= x2 + 1 &&
                          x <= xmax &&
                          (ArrayAccess.GetDataBit(line, x) == 0); x++)
                    ;
                xstart = x;
            } while (x <= x2 + 1 && x <= xmax);
        }

        return Box.Create(minx, miny, maxx - minx + 1, maxy - miny + 1);
    }

    // leptonica: pixSeedfill (conncomp.c)
    /// <summary>Erases the connected component containing the seed pixel.</summary>
    /// <param name="stack">for holding fillsegs</param>
    /// <param name="x">location of seed pixel</param>
    /// <param name="y">location of seed pixel</param>
    /// <param name="connectivity">4 or 8</param>
    /// <remarks>
    /// (1) This removes the component from pixs with a fg pixel at (x,y).
    /// (2) See pixSeedfill4() and pixSeedfill8() for details.
    /// </remarks>
    public void Seedfill(L_Stack stack, int x, int y, int connectivity)
    {
        if (Depth != 1)
            throw Lept.Arg("pixSeedfill", "pixs not defined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg("pixSeedfill", "connectivity not 4 or 8");

        if (connectivity == 4)
            Seedfill4(stack, x, y);
        else  /* connectivity == 8  */
            Seedfill8(stack, x, y);
    }

    // leptonica: pixSeedfill4 (conncomp.c)
    /// <summary>Heckbert's 4-cc seedfill.</summary>
    /// <param name="stack">for holding fillsegs</param>
    /// <param name="x">location of seed pixel</param>
    /// <param name="y">location of seed pixel</param>
    /// <remarks>
    /// (1) This is Paul Heckbert's stack-based 4-cc seedfill algorithm.
    /// (2) This operates on the input 1 bpp pix to remove the fg seed
    ///     pixel, at (x,y), and all pixels that are 4-connected to it.
    ///     The seed pixel at (x,y) must initially be ON.
    /// (3) Reference: see pixSeedFill4BB()
    /// </remarks>
    public void Seedfill4(L_Stack stack, int x, int y)
    {
        int xstart, x1 = 0, x2 = 0, dy = 0;

        if (Depth != 1)
            throw Lept.Arg("pixSeedfill4", "pixs not defined or not 1 bpp");
        if (stack.AuxStack == null)
            stack.AuxStack = L_Stack.Create(0);

        GetDimensions(out int w, out int h, out _);
        int xmax = w - 1;
        int ymax = h - 1;
        uint[] data = Data;
        int wpl = Wpl;

            /* Check pix value of seed; must be within the image and ON */
        if (x < 0 || x > xmax || y < 0 || y > ymax
            || ArrayAccess.GetDataBit(data.AsSpan(y * wpl), x) == 0)
            return;

            /* Init stack to seed */
        PushFillseg(stack, x, x, y, 1, ymax);
        PushFillseg(stack, x, x, y + 1, -1, ymax);

        while (stack.Count > 0) {
                /* Pop segment off stack and fill a neighboring scan line */
            PopFillseg(stack, ref x1, ref x2, ref y, ref dy);
            Span<uint> line = data.AsSpan(y * wpl, wpl);

                /* A segment of scanline y - dy for x1 <= x <= x2 was
                 * previously filled.  We now explore adjacent pixels
                 * in scan line y.  There are three regions: to the
                 * left of x1 - 1, between x1 and x2, and to the right of x2.
                 * These regions are handled differently.  Leaks are
                 * possible expansions beyond the previous segment and
                 * going back in the -dy direction.  These can happen
                 * for x < x1 - 1 and for x > x2 + 1.  Any "leak" segments
                 * are plugged with a push in the -dy (opposite) direction.
                 * And any segments found anywhere are always extended
                 * in the +dy direction.  */
            for (x = x1; x >= 0 && (ArrayAccess.GetDataBit(line, x) == 1); x--)
                ArrayAccess.ClearDataBit(line, x);
            bool skip = x >= x1;  /* pix at x1 was off and was not cleared */
            if (!skip) {
                xstart = x + 1;
                if (xstart < x1 - 1)   /* leak on left? */
                    PushFillseg(stack, xstart, x1 - 1, y, -dy, ymax);

                x = x1 + 1;
            } else {
                xstart = 0;  /* set below, before it is used */
            }
            do {
                if (!skip) {
                    for (; x <= xmax && (ArrayAccess.GetDataBit(line, x) == 1); x++)
                        ArrayAccess.ClearDataBit(line, x);
                    PushFillseg(stack, xstart, x - 1, y, dy, ymax);
                    if (x > x2 + 1)   /* leak on right? */
                        PushFillseg(stack, x2 + 1, x - 1, y, -dy, ymax);
                }
                skip = false;
                for (x++; x <= x2 &&
                          x <= xmax &&
                          (ArrayAccess.GetDataBit(line, x) == 0); x++)
                    ;
                xstart = x;
            } while (x <= x2 && x <= xmax);
        }
    }

    // leptonica: pixSeedfill8 (conncomp.c)
    /// <summary>Heckbert's 8-cc seedfill.</summary>
    /// <param name="stack">for holding fillsegs</param>
    /// <param name="x">location of seed pixel</param>
    /// <param name="y">location of seed pixel</param>
    /// <remarks>
    /// (1) This is Paul Heckbert's stack-based 8-cc seedfill algorithm.
    /// (2) This operates on the input 1 bpp pix to remove the fg seed
    ///     pixel, at (x,y), and all pixels that are 8-connected to it.
    ///     The seed pixel at (x,y) must initially be ON.
    /// (3) Reference: see pixSeedFill8BB()
    /// </remarks>
    public void Seedfill8(L_Stack stack, int x, int y)
    {
        int xstart, x1 = 0, x2 = 0, dy = 0;

        if (Depth != 1)
            throw Lept.Arg("pixSeedfill8", "pixs not defined or not 1 bpp");
        if (stack.AuxStack == null)
            stack.AuxStack = L_Stack.Create(0);

        GetDimensions(out int w, out int h, out _);
        int xmax = w - 1;
        int ymax = h - 1;
        uint[] data = Data;
        int wpl = Wpl;

            /* Check pix value of seed; must be ON */
        if (x < 0 || x > xmax || y < 0 || y > ymax
            || ArrayAccess.GetDataBit(data.AsSpan(y * wpl), x) == 0)
            return;

            /* Init stack to seed */
        PushFillseg(stack, x, x, y, 1, ymax);
        PushFillseg(stack, x, x, y + 1, -1, ymax);

        while (stack.Count > 0) {
                /* Pop segment off stack and fill a neighboring scan line */
            PopFillseg(stack, ref x1, ref x2, ref y, ref dy);
            Span<uint> line = data.AsSpan(y * wpl, wpl);

                /* A segment of scanline y - dy for x1 <= x <= x2 was
                 * previously filled.  We now explore adjacent pixels
                 * in scan line y.  There are three regions: to the
                 * left of x1, between x1 and x2, and to the right of x2.
                 * These regions are handled differently.  Leaks are
                 * possible expansions beyond the previous segment and
                 * going back in the -dy direction.  These can happen
                 * for x < x1 and for x > x2.  Any "leak" segments
                 * are plugged with a push in the -dy (opposite) direction.
                 * And any segments found anywhere are always extended
                 * in the +dy direction.  */
            for (x = x1 - 1; x >= 0 && (ArrayAccess.GetDataBit(line, x) == 1); x--)
                ArrayAccess.ClearDataBit(line, x);
            bool skip = x >= x1 - 1;  /* pix at x1 - 1 was off and was not cleared */
            if (!skip) {
                xstart = x + 1;
                if (xstart < x1)   /* leak on left? */
                    PushFillseg(stack, xstart, x1 - 1, y, -dy, ymax);

                x = x1;
            } else {
                xstart = 0;  /* set below, before it is used */
            }
            do {
                if (!skip) {
                    for (; x <= xmax && (ArrayAccess.GetDataBit(line, x) == 1); x++)
                        ArrayAccess.ClearDataBit(line, x);
                    PushFillseg(stack, xstart, x - 1, y, dy, ymax);
                    if (x > x2)   /* leak on right? */
                        PushFillseg(stack, x2 + 1, x - 1, y, -dy, ymax);
                }
                skip = false;
                for (x++; x <= x2 + 1 &&
                          x <= xmax &&
                          (ArrayAccess.GetDataBit(line, x) == 0); x++)
                    ;
                xstart = x;
            } while (x <= x2 + 1 && x <= xmax);
        }
    }

    /*-----------------------------------------------------------------------*
     *          Static stack helper functions: push and pop fillsegs         *
     *-----------------------------------------------------------------------*/
    // leptonica: pushFillsegBB (conncomp.c)
    /// <summary>Adds a line segment to the stack and updates the bounding box.</summary>
    /// <remarks>
    /// (1) This adds a line segment to the stack, and returns its size.
    /// (2) The auxiliary stack is used as a storage area to recycle
    ///     fillsegs that are no longer in use.  We only allocate new
    ///     fillsegs if the auxiliary stack is empty.
    /// </remarks>
    private static void PushFillsegBB(L_Stack stack, int xleft, int xright, int y, int dy,
                                      int ymax, ref int pminx, ref int pmaxx,
                                      ref int pminy, ref int pmaxy)
    {
        FillSeg fseg;
        L_Stack? auxstack;

        pminx = Math.Min(pminx, xleft);
        pmaxx = Math.Max(pmaxx, xright);
        pminy = Math.Min(pminy, y);
        pmaxy = Math.Max(pmaxy, y);

        if (y + dy >= 0 && y + dy <= ymax) {
            if ((auxstack = stack.AuxStack) == null) {
                Log.Error("pushFillsegBB", "auxstack not defined");
                return;
            }

                /* Get a fillseg to use */
            if (auxstack.Count > 0)
                fseg = (FillSeg)auxstack.Remove()!;
            else
                fseg = new FillSeg();
            fseg.xleft = xleft;
            fseg.xright = xright;
            fseg.y = y;
            fseg.dy = dy;
            stack.Add(fseg);
        }
    }

    // leptonica: pushFillseg (conncomp.c)
    /// <remarks>
    /// (1) This adds a line segment to the stack.
    /// (2) The auxiliary stack is used as a storage area to recycle
    ///     fillsegs that are no longer in use.  We only allocate new
    ///     fillsegs if the auxiliary stack is empty.
    /// </remarks>
    private static void PushFillseg(L_Stack stack, int xleft, int xright, int y, int dy,
                                    int ymax)
    {
        FillSeg fseg;
        L_Stack? auxstack;

        if (y + dy >= 0 && y + dy <= ymax) {
            if ((auxstack = stack.AuxStack) == null) {
                Log.Error("pushFillseg", "auxstack not defined");
                return;
            }

                /* Get a fillseg to use */
            if (auxstack.Count > 0)
                fseg = (FillSeg)auxstack.Remove()!;
            else
                fseg = new FillSeg();
            fseg.xleft = xleft;
            fseg.xright = xright;
            fseg.y = y;
            fseg.dy = dy;
            stack.Add(fseg);
        }
    }

    // leptonica: popFillseg (conncomp.c)
    /// <remarks>
    /// (1) This removes a line segment from the stack, and returns its size.
    /// (2) The surplussed fillseg is placed on the auxiliary stack
    ///     for future use.
    /// </remarks>
    private static void PopFillseg(L_Stack stack, ref int pxleft, ref int pxright,
                                   ref int py, ref int pdy)
    {
        L_Stack? auxstack;

        if ((auxstack = stack.AuxStack) == null) {
            Log.Error("popFillseg", "auxstack not defined");
            return;
        }

        FillSeg? fseg = (FillSeg?)stack.Remove();
        if (fseg == null)
            return;

        pxleft = fseg.xleft;
        pxright = fseg.xright;
        py = fseg.y + fseg.dy;  /* this now points to the new line */
        pdy = fseg.dy;

            /* Save it for re-use */
        auxstack.Add(fseg);
    }

}
