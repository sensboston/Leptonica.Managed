using System;

namespace Leptonica;

/*
 *  selgen.c: Generation of hit-miss Sels from a bitmap.
 *
 *      This file contains functions that generate hit-miss Sels
 *      for doing a loose match to a small bitmap.  The hit-miss
 *      Sel is made from a given bitmap.  Several "knobs"
 *      are available to control the looseness of the match.
 *      In general, a tight match will have fewer false positives
 *      (bad matches) but more false negatives (missed patterns).
 *      The values to be used depend on the quality and variation
 *      of the image in which the pattern is to be searched,
 *      and the relative penalties of false positives and
 *      false negatives.  Default values for the three knobs --
 *      minimum distance to boundary pixels, number of extra pixels
 *      added to selected sides, and minimum acceptable runlength
 *      in eroded version -- are provided.
 *
 *      The generated hit-miss Sels can always be used in the
 *      rasterop implementation of binary morphology (in morph.h).
 *      If they are small enough (not more than 31 pixels extending
 *      in any direction from the Sel origin), they can also be used
 *      to auto-generate dwa code (fmorphauto.c).
 *
 *      In production, use pixGenerateSelBoundary().
 *
 *      Generate a subsampled structuring element
 *            Pix.GenerateSelBoundary
 *            Pix.GenerateSelWithRuns
 *            Pix.GenerateSelRandom
 *
 *      Accumulate data on runs along lines
 *            Pix.GetRunCentersOnLine
 *            Pix.GetRunsOnLine
 *
 *      Subsample boundary pixels in relatively ordered way
 *            Pix.SubsampleBoundaryPixels
 *            Pix.AdjacentOnPixelInRaster
 *
 *      Display generated sel with originating image
 *            Pix.DisplayHitMissSel
 *
 *  Notes on the port:
 *    - The optional output image of the generators (the input expanded by the
 *      extra pixels) is an out parameter.
 *    - pixGenerateSelRandom() uses rand(): CRand with the RAND_MAX of the CRT.
 */

public sealed partial class Pix
{
        /* Default minimum distance of a hit-miss pixel element to
         * a boundary pixel of its color. */
    private const int DefaultDistanceToBoundary = 1;
    private const int MaxDistanceToBoundary = 4;

        /* Default min runlength to accept a hit or miss element located
         * at its center */
    private const int DefaultMinRunlength = 3;

        /* Default scalefactor for displaying image and hit-miss sel
         * that is derived from it */
    private const int DefaultSelScalefactor = 7;
    private const int MaxSelScalefactor = 31;  /* should be big enough */

    /*-----------------------------------------------------------------*
     *           Generate a subsampled structuring element             *
     *-----------------------------------------------------------------*/
    // leptonica: pixGenerateSelBoundary (selgen.c)
    /// <summary>Generates a hit-miss Sel from the boundary pixels of a pattern.</summary>
    /// <param name="hitdist">min distance from fg boundary pixel</param>
    /// <param name="missdist">min distance from bg boundary pixel</param>
    /// <param name="hitskip">number of boundary pixels skipped between hits</param>
    /// <param name="missskip">number of boundary pixels skipped between misses</param>
    /// <param name="topflag">flag for extra pixels of bg added above</param>
    /// <param name="botflag">flag for extra pixels of bg added below</param>
    /// <param name="leftflag">flag for extra pixels of bg added to left</param>
    /// <param name="rightflag">flag for extra pixels of bg added to right</param>
    /// <param name="ppixe">input pix expanded by extra pixels</param>
    /// <returns>sel hit-miss for input pattern</returns>
    /// <remarks>
    /// (1) All fg elements selected are exactly hitdist pixels away from
    ///     the nearest fg boundary pixel, and ditto for bg elements.
    ///     Valid inputs of hitdist and missdist are 0, 1, 2, 3 and 4.
    ///     For example, a hitdist of 0 puts the hits at the fg boundary.
    ///     Usually, the distances should be > 0 avoid the effect of
    ///     noise at the boundary.
    /// (2) Set hitskip &lt; 0 if no hits are to be used.  Ditto for missskip.
    ///     If both hitskip and missskip are &lt; 0, the sel would be empty,
    ///     and an error is raised.
    /// (3) The 4 flags determine whether the sel is increased on that side
    ///     to allow bg misses to be placed all along that boundary.
    ///     The increase in sel size on that side is the minimum necessary
    ///     to allow the misses to be placed at mindist.  For text characters,
    ///     the topflag and botflag are typically set to 1, and the leftflag
    ///     and rightflag to 0.
    /// (4) The input pix, as extended by the extra pixels on selected sides,
    ///     can optionally be returned.  For debugging, call
    ///     pixDisplayHitMissSel() to visualize the hit-miss sel superimposed
    ///     on the generating bitmap.
    /// (5) This is the best of the three sel generators: it gives the most
    ///     flexibility with the smallest number of hits and misses.
    ///     The other two generators are presented as examples of other
    ///     approaches, but they should not be used in production.
    /// </remarks>
    public Sel GenerateSelBoundary(int hitdist, int missdist, int hitskip, int missskip,
                                   int topflag, int botflag, int leftflag, int rightflag,
                                   out Pix? ppixe)
    {
        const string func = "pixGenerateSelBoundary";
        int ws, hs, w, h, x, y, ix, iy, i, npt;
        Pix? pixt1;
        Pix pixt2, pixt3, pixfg, pixbg;
        Sel selh, selm, sel_3, sel;
        Pta? ptah = null, ptam = null;

        ppixe = null;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (hitdist < 0 || hitdist > 4 || missdist < 0 || missdist > 4)
            throw Lept.Arg(func, "dist not in {0 .. 4}");
        if (hitskip < 0 && missskip < 0)
            throw Lept.Arg(func, "no hits or misses");

            /* Locate the foreground */
        ClipToForeground(out pixt1, out _);
        if (pixt1 == null)
            throw Lept.Fail(func, "pixt1 not made");
        ws = pixt1.Width;
        hs = pixt1.Height;
        w = ws;
        h = hs;

            /* Crop out a region including the foreground, and add pixels
             * on sides depending on the side flags */
        if (topflag != 0 || botflag != 0 || leftflag != 0 || rightflag != 0) {
            x = y = 0;
            if (topflag != 0) {
                h += missdist + 1;
                y = missdist + 1;
            }
            if (botflag != 0)
                h += missdist + 1;
            if (leftflag != 0) {
                w += missdist + 1;
                x = missdist + 1;
            }
            if (rightflag != 0)
                w += missdist + 1;
            pixt2 = Create(w, h, 1);
            pixt2.Rasterop(x, y, ws, hs, L.PIX_SRC, pixt1, 0, 0);
        } else {
            pixt2 = Clone(pixt1);
        }
        ppixe = Clone(pixt2);

            /* Identify fg and bg pixels that are exactly hitdist and
             * missdist (rsp) away from the boundary pixels in their set.
             * Then get a subsampled set of these points. */
        sel_3 = Sel.CreateBrick(3, 3, 1, 1, L.SEL_HIT);
        if (hitskip >= 0) {
            selh = Sel.CreateBrick(2 * hitdist + 1, 2 * hitdist + 1,
                                   hitdist, hitdist, L.SEL_HIT);
            pixt3 = Erode(null, pixt2, selh);
            pixfg = Erode(null, pixt3, sel_3);
            Xor(pixfg, pixfg, pixt3);
            ptah = pixfg.SubsampleBoundaryPixels(hitskip);
        }
        if (missskip >= 0) {
            selm = Sel.CreateBrick(2 * missdist + 1, 2 * missdist + 1,
                                   missdist, missdist, L.SEL_HIT);
            pixt3 = Dilate(null, pixt2, selm);
            pixbg = Dilate(null, pixt3, sel_3);
            Xor(pixbg, pixbg, pixt3);
            ptam = pixbg.SubsampleBoundaryPixels(missskip);
        }

            /* Generate the hit-miss sel from these point */
        sel = Sel.CreateBrick(h, w, h / 2, w / 2, L.SEL_DONT_CARE);
        if (hitskip >= 0) {
            npt = ptah!.Count;
            for (i = 0; i < npt; i++) {
                ptah.GetIPt(i, out ix, out iy);
                sel.SetElement(iy, ix, L.SEL_HIT);
            }
        }
        if (missskip >= 0) {
            npt = ptam!.Count;
            for (i = 0; i < npt; i++) {
                ptam.GetIPt(i, out ix, out iy);
                sel.SetElement(iy, ix, L.SEL_MISS);
            }
        }

        return sel;
    }

    // leptonica: pixGenerateSelWithRuns (selgen.c)
    /// <summary>Generates a hit-miss Sel from the centers of runs along horizontal and vertical lines.</summary>
    /// <param name="nhlines">number of hor lines along which elements are found</param>
    /// <param name="nvlines">number of vert lines along which elements are found</param>
    /// <param name="distance">min distance from boundary pixel; use 0 for default</param>
    /// <param name="minlength">min runlength to set hit or miss; use 0 for default</param>
    /// <param name="toppix">number of extra pixels of bg added above</param>
    /// <param name="botpix">number of extra pixels of bg added below</param>
    /// <param name="leftpix">number of extra pixels of bg added to left</param>
    /// <param name="rightpix">number of extra pixels of bg added to right</param>
    /// <param name="ppixe">input pix expanded by extra pixels</param>
    /// <returns>sel hit-miss for input pattern</returns>
    /// <remarks>
    /// (1) The horizontal and vertical lines along which elements are
    ///     selected are roughly equally spaced.  The actual locations of
    ///     the hits and misses are the centers of respective run-lengths.
    /// (2) No elements are selected that are less than 'distance' pixels away
    ///     from a boundary pixel of the same color.  This makes the
    ///     match much more robust to edge noise.  Valid inputs of
    ///     'distance' are 0, 1, 2, 3 and 4.  If distance is either 0 or
    ///     greater than 4, we reset it to the default value.
    /// (3) The 4 numbers for adding rectangles of pixels outside the fg
    ///     can be use if the pattern is expected to be surrounded by bg
    ///     (white) pixels.  On the other hand, if the pattern may be near
    ///     other fg (black) components on some sides, use 0 for those sides.
    /// (4) The pixels added to a side allow you to have miss elements there.
    ///     There is a constraint between distance, minlength, and
    ///     the added pixels for this to work.  We illustrate using the
    ///     default values.  If you add 5 pixels to the top, and use a
    ///     distance of 1, then you end up with a vertical run of at least
    ///     4 bg pixels along the top edge of the image.  If you use a
    ///     minimum runlength of 3, each vertical line will always find
    ///     a miss near the center of its run.  However, if you use a
    ///     minimum runlength of 5, you will not get a miss on every vertical
    ///     line.  As another example, if you have 7 added pixels and a
    ///     distance of 2, you can use a runlength up to 5 to guarantee
    ///     that the miss element is recorded.  We give a warning if the
    ///     constraint does not guarantee a miss element outside the
    ///     image proper.
    /// (5) The input pix, as extended by the extra pixels on selected sides,
    ///     can optionally be returned.  For debugging, call
    ///     pixDisplayHitMissSel() to visualize the hit-miss sel superimposed
    ///     on the generating bitmap.
    /// (6) This method is inferior to the boundary method.
    /// </remarks>
    public Sel GenerateSelWithRuns(int nhlines, int nvlines, int distance, int minlength,
                                   int toppix, int botpix, int leftpix, int rightpix,
                                   out Pix? ppixe)
    {
        const string func = "pixGenerateSelWithRuns";
        int ws, hs, w, h, x, y, xval, yval, i, j, nh, nm;
        float delh, delw;
        Numa nah, nam;
        Pix? pixt1;
        Pix pixt2, pixfg, pixbg;
        Pta ptah, ptam;
        Sel seld, sel;

        ppixe = null;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (nhlines < 1 && nvlines < 1)
            throw Lept.Arg(func, "nvlines and nhlines both < 1");

        if (distance <= 0)
            distance = DefaultDistanceToBoundary;
        if (minlength <= 0)
            minlength = DefaultMinRunlength;
        if (distance > MaxDistanceToBoundary) {
            Log.Warning(func, "distance too large; setting to max value");
            distance = MaxDistanceToBoundary;
        }

            /* Locate the foreground */
        ClipToForeground(out pixt1, out _);
        if (pixt1 == null)
            throw Lept.Fail(func, "pixt1 not made");
        ws = pixt1.Width;
        hs = pixt1.Height;
        w = ws;
        h = hs;

            /* Crop out a region including the foreground, and add pixels
             * on sides depending on the side flags */
        if (toppix != 0 || botpix != 0 || leftpix != 0 || rightpix != 0) {
            x = y = 0;
            if (toppix != 0) {
                h += toppix;
                y = toppix;
                if (toppix < distance + minlength)
                    Log.Warning(func, "no miss elements in added top pixels");
            }
            if (botpix != 0) {
                h += botpix;
                if (botpix < distance + minlength)
                    Log.Warning(func, "no miss elements in added bot pixels");
            }
            if (leftpix != 0) {
                w += leftpix;
                x = leftpix;
                if (leftpix < distance + minlength)
                    Log.Warning(func, "no miss elements in added left pixels");
            }
            if (rightpix != 0) {
                w += rightpix;
                if (rightpix < distance + minlength)
                    Log.Warning(func, "no miss elements in added right pixels");
            }
            pixt2 = Create(w, h, 1);
            pixt2.Rasterop(x, y, ws, hs, L.PIX_SRC, pixt1, 0, 0);
        } else {
            pixt2 = Clone(pixt1);
        }
        ppixe = Clone(pixt2);

            /* Identify fg and bg pixels that are at least 'distance' pixels
             * away from the boundary pixels in their set */
        seld = Sel.CreateBrick(2 * distance + 1, 2 * distance + 1,
                               distance, distance, L.SEL_HIT);
        pixfg = Erode(null, pixt2, seld);
        pixbg = Dilate(null, pixt2, seld);
        Invert(pixbg, pixbg);

            /* Accumulate hit and miss points */
        ptah = Pta.Create(0);
        ptam = Pta.Create(0);
        if (nhlines >= 1) {
            delh = (float)h / (float)(nhlines + 1);
            for (i = 0, y = 0; i < nhlines; i++) {
                y += (int)(delh + 0.5);
                nah = pixfg.GetRunCentersOnLine(-1, y, minlength);
                nam = pixbg.GetRunCentersOnLine(-1, y, minlength);
                nh = nah.Count;
                nm = nam.Count;
                for (j = 0; j < nh; j++) {
                    xval = nah.GetIValue(j);
                    ptah.AddPt(xval, y);
                }
                for (j = 0; j < nm; j++) {
                    xval = nam.GetIValue(j);
                    ptam.AddPt(xval, y);
                }
            }
        }
        if (nvlines >= 1) {
            delw = (float)w / (float)(nvlines + 1);
            for (i = 0, x = 0; i < nvlines; i++) {
                x += (int)(delw + 0.5);
                nah = pixfg.GetRunCentersOnLine(x, -1, minlength);
                nam = pixbg.GetRunCentersOnLine(x, -1, minlength);
                nh = nah.Count;
                nm = nam.Count;
                for (j = 0; j < nh; j++) {
                    yval = nah.GetIValue(j);
                    ptah.AddPt(x, yval);
                }
                for (j = 0; j < nm; j++) {
                    yval = nam.GetIValue(j);
                    ptam.AddPt(x, yval);
                }
            }
        }

            /* Make the Sel with those points */
        sel = Sel.CreateBrick(h, w, h / 2, w / 2, L.SEL_DONT_CARE);
        nh = ptah.Count;
        for (i = 0; i < nh; i++) {
            ptah.GetIPt(i, out x, out y);
            sel.SetElement(y, x, L.SEL_HIT);
        }
        nm = ptam.Count;
        for (i = 0; i < nm; i++) {
            ptam.GetIPt(i, out x, out y);
            sel.SetElement(y, x, L.SEL_MISS);
        }

        return sel;
    }

    // leptonica: pixGenerateSelRandom (selgen.c)
    /// <summary>Generates a hit-miss Sel from randomly chosen pixels away from the boundary.</summary>
    /// <param name="hitfract">fraction of allowable fg pixels that are hits</param>
    /// <param name="missfract">fraction of allowable bg pixels that are misses</param>
    /// <param name="distance">min distance from boundary pixel; use 0 for default</param>
    /// <param name="toppix">number of extra pixels of bg added above</param>
    /// <param name="botpix">number of extra pixels of bg added below</param>
    /// <param name="leftpix">number of extra pixels of bg added to left</param>
    /// <param name="rightpix">number of extra pixels of bg added to right</param>
    /// <param name="ppixe">input pix expanded by extra pixels</param>
    /// <returns>sel hit-miss for input pattern</returns>
    /// <remarks>
    /// (1) Either of hitfract and missfract can be zero.  If both are zero,
    ///     the sel would be empty, and an error is raised.
    /// (2) No elements are selected that are less than 'distance' pixels away
    ///     from a boundary pixel of the same color.  This makes the
    ///     match much more robust to edge noise.  Valid inputs of
    ///     'distance' are 0, 1, 2, 3 and 4.  If distance is either 0 or
    ///     greater than 4, we reset it to the default value.
    /// (3) The 4 numbers for adding rectangles of pixels outside the fg
    ///     can be use if the pattern is expected to be surrounded by bg
    ///     (white) pixels.  On the other hand, if the pattern may be near
    ///     other fg (black) components on some sides, use 0 for those sides.
    /// (4) The input pix, as extended by the extra pixels on selected sides,
    ///     can optionally be returned.  For debugging, call
    ///     pixDisplayHitMissSel() to visualize the hit-miss sel superimposed
    ///     on the generating bitmap.
    /// (5) This method is inferior to both the boundary and run based methods.
    /// </remarks>
    public Sel GenerateSelRandom(float hitfract, float missfract, int distance,
                                 int toppix, int botpix, int leftpix, int rightpix,
                                 out Pix? ppixe)
    {
        const string func = "pixGenerateSelRandom";
        int ws, hs, w, h, x, y, i, j, thresh;
        uint val;
        Pix? pixt1;
        Pix pixt2, pixfg, pixbg;
        Sel seld, sel;

        ppixe = null;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (hitfract <= 0.0 && missfract <= 0.0)
            throw Lept.Arg(func, "no hits or misses");
        if (hitfract > 1.0 || missfract > 1.0)
            throw Lept.Arg(func, "fraction can't be > 1.0");

        if (distance <= 0)
            distance = DefaultDistanceToBoundary;
        if (distance > MaxDistanceToBoundary) {
            Log.Warning(func, "distance too large; setting to max value");
            distance = MaxDistanceToBoundary;
        }

            /* Locate the foreground */
        ClipToForeground(out pixt1, out _);
        if (pixt1 == null)
            throw Lept.Fail(func, "pixt1 not made");
        ws = pixt1.Width;
        hs = pixt1.Height;
        w = ws;
        h = hs;

            /* Crop out a region including the foreground, and add pixels
             * on sides depending on the side flags */
        if (toppix != 0 || botpix != 0 || leftpix != 0 || rightpix != 0) {
            x = y = 0;
            if (toppix != 0) {
                h += toppix;
                y = toppix;
            }
            if (botpix != 0)
                h += botpix;
            if (leftpix != 0) {
                w += leftpix;
                x = leftpix;
            }
            if (rightpix != 0)
                w += rightpix;
            pixt2 = Create(w, h, 1);
            pixt2.Rasterop(x, y, ws, hs, L.PIX_SRC, pixt1, 0, 0);
        } else {
            pixt2 = Clone(pixt1);
        }
        ppixe = Clone(pixt2);

            /* Identify fg and bg pixels that are at least 'distance' pixels
             * away from the boundary pixels in their set */
        seld = Sel.CreateBrick(2 * distance + 1, 2 * distance + 1,
                               distance, distance, L.SEL_HIT);
        pixfg = Erode(null, pixt2, seld);
        pixbg = Dilate(null, pixt2, seld);
        Invert(pixbg, pixbg);

            /* Generate the sel from a random selection of these points */
        sel = Sel.CreateBrick(h, w, h / 2, w / 2, L.SEL_DONT_CARE);
        if (hitfract > 0.0) {
            thresh = (int)(hitfract * (double)CRand.RandMax);
            for (i = 0; i < h; i++) {
                for (j = 0; j < w; j++) {
                    pixfg.GetPixel(j, i, out val);
                    if (val != 0) {
                        if (CRand.Next() < thresh)
                            sel.SetElement(i, j, L.SEL_HIT);
                    }
                }
            }
        }
        if (missfract > 0.0) {
            thresh = (int)(missfract * (double)CRand.RandMax);
            for (i = 0; i < h; i++) {
                for (j = 0; j < w; j++) {
                    pixbg.GetPixel(j, i, out val);
                    if (val != 0) {
                        if (CRand.Next() < thresh)
                            sel.SetElement(i, j, L.SEL_MISS);
                    }
                }
            }
        }

        return sel;
    }

    /*-----------------------------------------------------------------*
     *              Accumulate data on runs along lines                *
     *-----------------------------------------------------------------*/
    // leptonica: pixGetRunCentersOnLine (selgen.c)
    /// <summary>Centers of the fg runs of at least a minimum length along a horizontal or vertical line.</summary>
    /// <param name="x">set one of x and y to -1; see notes</param>
    /// <param name="y">set one of x and y to -1; see notes</param>
    /// <param name="minlength">minimum length of acceptable run</param>
    /// <returns>numa of fg runs</returns>
    /// <remarks>
    /// (1) Action: this function computes the fg (black) and bg (white)
    ///     pixel runlengths along the specified horizontal or vertical line,
    ///     and returns a Numa of the "center" pixels of each fg run
    ///     whose length equals or exceeds the minimum length.
    /// (2) This only works on horizontal and vertical lines.
    /// (3) For horizontal runs, set x = -1 and y to the value
    ///     for all points along the raster line.  For vertical runs,
    ///     set y = -1 and x to the value for all points along the
    ///     pixel column.
    /// (4) For horizontal runs, the points in the Numa are the x
    ///     values in the center of fg runs that are of length at
    ///     least 'minlength'.  For vertical runs, the points in the
    ///     Numa are the y values in the center of fg runs, again
    ///     of length 'minlength' or greater.
    /// (5) If there are no fg runs along the line that satisfy the
    ///     minlength constraint, the returned Numa is empty.  This
    ///     is not an error.
    /// </remarks>
    public Numa GetRunCentersOnLine(int x, int y, int minlength)
    {
        const string func = "pixGetRunCentersOnLine";
        int w, h, i, r, nruns, len;
        Numa naruns, nad;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (x != -1 && y != -1)
            throw Lept.Arg(func, "x or y must be -1");
        if (x == -1 && y == -1)
            throw Lept.Arg(func, "x or y cannot both be -1");

        nad = Numa.Create(0);
        w = Width;
        h = Height;
        if (x == -1) {  /* horizontal run */
            if (y < 0 || y >= h)
                return nad;
            naruns = GetRunsOnLine(0, y, w - 1, y);
        } else {  /* vertical run */
            if (x < 0 || x >= w)
                return nad;
            naruns = GetRunsOnLine(x, 0, x, h - 1);
        }
        nruns = naruns.Count;

            /* extract run center values; the first run is always bg */
        r = 0;  /* cumulative distance along line */
        for (i = 0; i < nruns; i++) {
            if (i % 2 == 0) {  /* bg run */
                len = naruns.GetIValue(i);
                r += len;
                continue;
            } else {
                len = naruns.GetIValue(i);
                if (len >= minlength)
                    nad.AddNumber(r + len / 2);
                r += len;
            }
        }

        return nad;
    }

    // leptonica: pixGetRunsOnLine (selgen.c)
    /// <summary>Run lengths of the fg and bg runs along a line, starting with a bg run.</summary>
    /// <param name="x1">line end points</param>
    /// <param name="y1">line end points</param>
    /// <param name="x2">line end points</param>
    /// <param name="y2">line end points</param>
    /// <returns>numa</returns>
    /// <remarks>
    /// (1) Action: this function uses the bresenham algorithm to compute
    ///     the pixels along the specified line.  It returns a Numa of the
    ///     runlengths of the fg (black) and bg (white) runs, always
    ///     starting with a white run.
    /// (2) If the first pixel on the line is black, the length of the
    ///     first returned run (which is white) is 0.
    /// </remarks>
    public Numa GetRunsOnLine(int x1, int y1, int x2, int y2)
    {
        const string func = "pixGetRunsOnLine";
        int w, h, x, y, npts;
        int i, runlen, preval;
        uint val;
        Numa numa;
        Pta pta;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");

        w = Width;
        h = Height;
        if (x1 < 0 || x1 >= w)
            throw Lept.Arg(func, "x1 not valid");
        if (x2 < 0 || x2 >= w)
            throw Lept.Arg(func, "x2 not valid");
        if (y1 < 0 || y1 >= h)
            throw Lept.Arg(func, "y1 not valid");
        if (y2 < 0 || y2 >= h)
            throw Lept.Arg(func, "y2 not valid");

        pta = GeneratePtaLine(x1, y1, x2, y2);
        if ((npts = pta.Count) == 0)
            throw Lept.Fail(func, "pta has no pts");
        numa = Numa.Create(0);

        preval = 0;
        runlen = 0;
        for (i = 0; i < npts; i++) {
            pta.GetIPt(i, out x, out y);
            GetPixel(x, y, out val);
            if (i == 0) {
                if (val == 1) {  /* black pixel; append white run of size 0 */
                    numa.AddNumber(0);
                }
                preval = (int)val;
                runlen = 1;
                continue;
            }
            if (val == preval) {  /* extend current run */
                preval = (int)val;
                runlen++;
            } else {  /* end previous run */
                numa.AddNumber(runlen);
                preval = (int)val;
                runlen = 1;
            }
        }
        numa.AddNumber(runlen);  /* append last run */

        return numa;
    }

    /*-----------------------------------------------------------------*
     *        Subsample boundary pixels in relatively ordered way      *
     *-----------------------------------------------------------------*/
    // leptonica: pixSubsampleBoundaryPixels (selgen.c)
    /// <summary>Subsamples the boundary pixels, traversing the boundaries in a regular way.</summary>
    /// <param name="skip">number to skip between samples as you traverse boundary</param>
    /// <returns>pta</returns>
    /// <remarks>
    /// (1) If skip = 0, we take all the fg pixels.
    /// (2) We try to traverse the boundaries in a regular way.
    ///     Some pixels may be missed, and these are then subsampled
    ///     randomly with a fraction determined by 'skip'.
    /// (3) The most natural approach is to use a depth first (stack-based)
    ///     method to find the fg pixels.  However, the pixel runs are
    ///     4-connected and there are relatively few branches.  So
    ///     instead of doing a proper depth-first search, we get nearly
    ///     the same result using two nested while loops: the outer
    ///     one continues a raster-based search for the next fg pixel,
    ///     and the inner one does a reasonable job running along
    ///     each 4-connected coutour.
    /// </remarks>
    public Pta SubsampleBoundaryPixels(int skip)
    {
        const string func = "pixSubsampleBoundaryPixels";
        int x, y, xn, yn, xs, ys, xa, ya, count;
        Pix pixt;
        Pta pta;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (skip < 0)
            throw Lept.Arg(func, "skip < 0");

        if (skip == 0)
            return Pta.GetPixelsFromPix(this, null);

        pta = Pta.Create(0);
        pixt = Copy(null, this);
        xs = ys = 0;
        while (pixt.NextOnPixelInRaster(xs, ys, out xn, out yn)) {  /* new series */
            xs = xn;
            ys = yn;

                /* Add first point in this series */
            pta.AddPt(xs, ys);

                /* Trace out boundary, erasing all and saving every (skip + 1)th */
            x = xs;
            y = ys;
            pixt.SetPixel(x, y, 0);
            count = 0;
            while (pixt.AdjacentOnPixelInRaster(x, y, out xa, out ya)) {
                x = xa;
                y = ya;
                pixt.SetPixel(x, y, 0);
                if (count == skip) {
                    pta.AddPt(x, y);
                    count = 0;
                } else {
                    count++;
                }
            }
        }

        return pta;
    }

    // leptonica: adjacentOnPixelInRaster (selgen.c)
    /// <summary>Finds an adjacent ON pixel, searching 4-connected directions first.</summary>
    /// <param name="x">current pixel</param>
    /// <param name="y">current pixel</param>
    /// <param name="pxa">adjacent ON pixel, found by simple CCW search</param>
    /// <param name="pya">adjacent ON pixel, found by simple CCW search</param>
    /// <returns>true if a pixel is found</returns>
    /// <remarks>
    /// (1) Search is in 4-connected directions first; then on diagonals.
    ///     This allows traversal along a 4-connected boundary.
    /// </remarks>
    public bool AdjacentOnPixelInRaster(int x, int y, out int pxa, out int pya)
    {
        const string func = "adjacentOnPixelInRaster";
        int w, h, i, xa, ya;
        bool found;
        int[] xdel = { -1, 0, 1, 0, -1, 1, 1, -1 };
        int[] ydel = { 0, 1, 0, -1, 1, 1, -1, -1 };
        uint val;

        pxa = pya = 0;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        w = Width;
        h = Height;
        found = false;
        for (i = 0; i < 8; i++) {
            xa = x + xdel[i];
            ya = y + ydel[i];
            if (xa < 0 || xa >= w || ya < 0 || ya >= h)
                continue;
            GetPixel(xa, ya, out val);
            if (val == 1) {
                found = true;
                pxa = xa;
                pya = ya;
                break;
            }
        }
        return found;
    }

    /*-----------------------------------------------------------------*
     *          Display generated sel with originating image           *
     *-----------------------------------------------------------------*/
    // leptonica: pixDisplayHitMissSel (selgen.c)
    /// <summary>Displays a hit-miss Sel superimposed on its generating image.</summary>
    /// <param name="sel">hit-miss in general</param>
    /// <param name="scalefactor">an integer >= 1; use 0 for default</param>
    /// <param name="hitcolor">RGB0 color for center of hit pixels</param>
    /// <param name="misscolor">RGB0 color for center of miss pixels</param>
    /// <returns>pixd RGB showing both pixs and sel</returns>
    /// <remarks>
    /// (1) We don't allow scalefactor to be larger than MaxSelScalefactor
    /// (2) The colors are conveniently given as 4 bytes in hex format,
    ///     such as 0xff008800.  The least significant byte is ignored.
    /// </remarks>
    public Pix DisplayHitMissSel(Sel sel, int scalefactor, uint hitcolor, uint misscolor)
    {
        const string func = "pixDisplayHitMissSel";
        int i, j, type;
        float fscale;
        Pix pixt, pixd;
        PixColormap cmap;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");

        if (scalefactor <= 0)
            scalefactor = DefaultSelScalefactor;
        if (scalefactor > MaxSelScalefactor) {
            Log.Warning(func, "scalefactor too large; using max value");
            scalefactor = MaxSelScalefactor;
        }

            /* Generate a version of pixs with a colormap */
        pixt = Convert1To8(null, this, 0, 1);
        cmap = PixColormap.Create(8);
        cmap.AddColor(255, 255, 255);
        cmap.AddColor(0, 0, 0);
        cmap.AddColor((int)(hitcolor >> 24), (int)((hitcolor >> 16) & 0xff),
                      (int)((hitcolor >> 8) & 0xff));
        cmap.AddColor((int)(misscolor >> 24), (int)((misscolor >> 16) & 0xff),
                      (int)((misscolor >> 8) & 0xff));
        pixt.SetColormap(cmap);

            /* Color the hits and misses */
        for (i = 0; i < sel.Sy; i++) {
            for (j = 0; j < sel.Sx; j++) {
                type = sel.GetElement(i, j);
                if (type == L.SEL_DONT_CARE)
                    continue;
                if (type == L.SEL_HIT)
                    pixt.SetPixel(j, i, 2);
                else  /* type == SEL_MISS */
                    pixt.SetPixel(j, i, 3);
            }
        }

            /* Scale it up */
        fscale = (float)scalefactor;
        pixd = pixt.ScaleBySampling(fscale, fscale);

        return pixd;
    }
}
