using System;

namespace Leptonica;

/*
 *  boxfunc1.c: Box geometry; Boxa combine and split.
 *
 *  Differences from C:
 *    - No reference counting and no boxDestroy(): the GC owns the objects.
 *    - Functions that return a Box that may legitimately not exist
 *      (boxOverlapRegion) return null; invalid input throws.
 *    - Boolean results (1 / 0 in C) are returned as bool.
 *    - Optional output pointers become out parameters, with one exception:
 *      boxaSimilar() has an overload without the nasim output, because
 *      the C code stops at the first mismatch only when pnasim is NULL.
 *    - Where the C code ignores the error return of a callee and carries on
 *      (boxaAdjustSides(), boxaAdjustBoxSides(), boxaSimilar()), the port
 *      logs the same error and carries on.
 *    - boxaFindNearestBoxes() throws for an invalid %dist_select, where the C
 *      code logs the error for each of the 4 * n calls and fills the output
 *      Numaa with the default values.
 *    - The debug output of boxaCombineOverlaps() and
 *      boxaCombineOverlapsInPair() needs pixRenderBoxaArb() (graphics.c),
 *      which is a stub for now.
 *
 *  Stubs created for callees in files that are not ported yet:
 *    numaCreate, numaAddNumber, numaGetIValue, numaSetValue, numaReplaceNumber,
 *    numaaCreate, numaaAddNuma (numabasic.c); numaMakeConstant (numafunc1.c);
 *    ptaCreate, ptaAddPt, ptaGetCount, ptaGetIPt (ptabasic.c);
 *    pixaAddPix (pixabasic.c); pixRenderBoxaArb (graphics.c);
 *    pixSetAll (pix2.c).
 */

/// <summary>Constants from pix.h used by the box geometry functions.</summary>
public static partial class L
{
    /* Sort and comparison types (L_SORT_BY_*) */
    /// <summary>Sorts boxes or connected components by the location of the left edge.</summary>
    public const int L_SORT_BY_X = 1;

    /// <summary>Sorts boxes or connected components by the location of the top edge.</summary>
    public const int L_SORT_BY_Y = 2;

    /// <summary>Sorts boxes or connected components by the location of the right edge.</summary>
    public const int L_SORT_BY_RIGHT = 3;

    /// <summary>Sorts boxes or connected components by the location of the bottom edge.</summary>
    public const int L_SORT_BY_BOT = 4;

    /// <summary>Sorts boxes or connected components by width.</summary>
    public const int L_SORT_BY_WIDTH = 5;

    /// <summary>Sorts boxes or connected components by height.</summary>
    public const int L_SORT_BY_HEIGHT = 6;

    /// <summary>Sorts boxes or connected components by the smaller of width and height.</summary>
    public const int L_SORT_BY_MIN_DIMENSION = 7;

    /// <summary>Sorts boxes or connected components by the larger of width and height.</summary>
    public const int L_SORT_BY_MAX_DIMENSION = 8;

    /// <summary>Sorts boxes or connected components by perimeter.</summary>
    public const int L_SORT_BY_PERIMETER = 9;

    /// <summary>Sorts boxes or connected components by area.</summary>
    public const int L_SORT_BY_AREA = 10;

    /// <summary>Sorts boxes or connected components by the ratio of width to height.</summary>
    public const int L_SORT_BY_ASPECT_RATIO = 11;

    /* Distance selection (L_NON_NEGATIVE, L_ALL) */

    /// <summary>Selects only values greater than or equal to zero.</summary>
    public const int L_NON_NEGATIVE = 2;

    /// <summary>Selects all values.</summary>
    public const int L_ALL = 6;

    /* Directions for finding the nearest box (L_FROM_*) */

    /// <summary>Scans from the left.</summary>
    public const int L_FROM_LEFT = 0;

    /// <summary>Scans from the right.</summary>
    public const int L_FROM_RIGHT = 1;

    /// <summary>Scans from the top.</summary>
    public const int L_FROM_TOP = 2;

    /// <summary>Scans from the bottom.</summary>
    public const int L_FROM_BOT = 3;

    /* Edge adjustment and edge setting (L_ADJUST_*, L_SET_*) */

    /// <summary>Does not adjust.</summary>
    public const int L_ADJUST_SKIP = 0;

    /// <summary>Adjusts the left edge.</summary>
    public const int L_ADJUST_LEFT = 1;

    /// <summary>Adjusts the right edge.</summary>
    public const int L_ADJUST_RIGHT = 2;

    /// <summary>Adjusts both the left and the right edges.</summary>
    public const int L_ADJUST_LEFT_AND_RIGHT = 3;

    /// <summary>Adjusts the top edge.</summary>
    public const int L_ADJUST_TOP = 4;

    /// <summary>Adjusts the bottom edge.</summary>
    public const int L_ADJUST_BOT = 5;

    /// <summary>Adjusts both the top and the bottom edges.</summary>
    public const int L_ADJUST_TOP_AND_BOT = 6;

    /// <summary>Chooses the smaller of the two median values.</summary>
    public const int L_ADJUST_CHOOSE_MIN = 7;

    /// <summary>Chooses the larger of the two median values.</summary>
    public const int L_ADJUST_CHOOSE_MAX = 8;

    /// <summary>Sets the left side to a given value.</summary>
    public const int L_SET_LEFT = 9;

    /// <summary>Sets the right side to a given value.</summary>
    public const int L_SET_RIGHT = 10;

    /// <summary>Sets the top side to a given value.</summary>
    public const int L_SET_TOP = 11;

    /// <summary>Sets the bottom side to a given value.</summary>
    public const int L_SET_BOT = 12;

    /* Handling of overlapping boxes (boxaHandleOverlaps) */

    /// <summary>Resizes the box to the bounding region and removes the smaller box.</summary>
    public const int L_COMBINE = 1;

    /// <summary>Only removes the smaller box.</summary>
    public const int L_REMOVE_SMALL = 2;
}

/*---------------------------------------------------------------------*
 *                             Box geometry                            *
 *---------------------------------------------------------------------*/
public sealed partial class Box
{
    /* C: L_ABS(x) = (((x) < 0) ? (-1 * (x)) : (x)); wraps for the minimum value */
    internal static int LAbs(int x)
    {
        return (x < 0) ? -1 * x : x;
    }

    // leptonica: boxContains (boxfunc1.c)
    /// <summary>Tests if box2 is entirely contained within this box.</summary>
    /// <param name="box2">the box tested for containment</param>
    /// <returns>true if box2 is entirely contained within this box; false otherwise</returns>
    public bool Contains(Box box2)
    {
        const string func = "boxContains";

        bool valid1 = IsValid();
        bool valid2 = box2.IsValid();
        if (!valid1 || !valid2)
            throw Lept.Arg(func, "boxes not both valid");

        GetGeometry(out int x1, out int y1, out int w1, out int h1);
        box2.GetGeometry(out int x2, out int y2, out int w2, out int h2);
        return x1 <= x2 && y1 <= y2 && (x1 + w1 >= x2 + w2) && (y1 + h1 >= y2 + h2);
    }

    // leptonica: boxIntersects (boxfunc1.c)
    /// <summary>Tests if any part of box2 is contained in this box.</summary>
    /// <param name="box2">the box tested for intersection</param>
    /// <returns>true if any part of box2 is contained in this box; false otherwise</returns>
    public bool Intersects(Box box2)
    {
        const string func = "boxIntersects";

        bool valid1 = IsValid();
        bool valid2 = box2.IsValid();
        if (!valid1 || !valid2)
            throw Lept.Arg(func, "boxes not both valid");

        GetGeometry(out int l1, out int t1, out int w1, out int h1);
        box2.GetGeometry(out int l2, out int t2, out int w2, out int h2);
        int r1 = l1 + w1 - 1;
        int r2 = l2 + w2 - 1;
        int b1 = t1 + h1 - 1;
        int b2 = t2 + h2 - 1;
        if (b2 < t1 || b1 < t2 || r1 < l2 || r2 < l1)
            return false;
        else
            return true;
    }
}

public sealed partial class Boxa
{
    // leptonica: boxaContainedInBox (boxfunc1.c)
    /// <summary>Selects the boxes that are entirely contained in a box.</summary>
    /// <param name="box">for containment</param>
    /// <returns>boxad  boxa with all boxes in this boxa that are entirely contained in box</returns>
    /// <remarks>
    /// (1) All boxes in %boxas that are entirely outside box are removed.
    /// (2) If %box is not valid, returns an empty boxa.
    /// </remarks>
    public Boxa ContainedInBox(Box box)
    {
        int n = Count;
        bool valid = box.IsValid();
        if (n == 0 || !valid)
            return Create(1);  /* empty */

        Boxa boxad = Create(0);
        for (int i = 0; i < n; i++) {
            Box? box1 = GetValidBox(i, L.L_CLONE);
            if (box1 == null)
                continue;
            bool val = box.Contains(box1);
            if (val)
                boxad.AddBox(box1, L.L_COPY);
        }

        return boxad;
    }

    // leptonica: boxaContainedInBoxCount (boxfunc1.c)
    /// <summary>Counts the boxes that are entirely contained in a box.</summary>
    /// <param name="box">for selecting contained boxes in this boxa</param>
    /// <returns>number of boxes contained in the box</returns>
    /// <remarks>
    /// (1) If %box is not valid, returns a zero count.
    /// </remarks>
    public int ContainedInBoxCount(Box box)
    {
        int count = 0;
        int n = Count;
        bool valid = box.IsValid();
        if (n == 0 || !valid)
            return 0;

        for (int i = 0; i < n; i++) {
            Box? box1 = GetValidBox(i, L.L_CLONE);
            if (box1 == null)
                continue;
            bool val = box.Contains(box1);
            if (val)
                count++;
        }
        return count;
    }

    // leptonica: boxaContainedInBoxa (boxfunc1.c)
    /// <summary>Tests if every box in boxa2 is contained in some box in this boxa.</summary>
    /// <param name="boxa2">the boxa whose boxes are tested for containment</param>
    /// <returns>true if every box in boxa2 is contained in some box in this boxa; false otherwise</returns>
    public bool ContainedInBoxa(Boxa boxa2)
    {
        Boxa boxa1 = this;

        int n1 = boxa1.Count;
        int n2 = boxa2.Count;
        for (int i = 0; i < n2; i++) {
            Box? box2 = boxa2.GetValidBox(i, L.L_CLONE);
            if (box2 == null)
                continue;
            bool cont = false;
            for (int j = 0; j < n1; j++) {
                Box? box1 = boxa1.GetValidBox(j, L.L_CLONE);
                if (box1 == null)
                    continue;
                bool result = box1.Contains(box2);
                if (result) {
                    cont = true;
                    break;
                }
            }
            if (!cont) return false;
        }

        return true;
    }

    // leptonica: boxaIntersectsBox (boxfunc1.c)
    /// <summary>Selects the boxes that intersect a box.</summary>
    /// <param name="box">for intersecting</param>
    /// <returns>boxad  boxa with all boxes in this boxa that intersect box</returns>
    /// <remarks>
    /// (1) All boxes in boxa that intersect with box (i.e., are completely
    ///     or partially contained in box) are retained.
    /// </remarks>
    public Boxa IntersectsBox(Box box)
    {
        int n = Count;
        bool valid = box.IsValid();
        if (n == 0 || !valid)
            return Create(1);  /* empty */

        Boxa boxad = Create(0);
        for (int i = 0; i < n; i++) {
            Box? box1 = GetValidBox(i, L.L_CLONE);
            if (box1 == null)
                continue;
            bool val = box.Intersects(box1);
            if (val)
                boxad.AddBox(box1, L.L_COPY);
        }

        return boxad;
    }

    // leptonica: boxaIntersectsBoxCount (boxfunc1.c)
    /// <summary>Counts the boxes that intersect a box.</summary>
    /// <param name="box">for selecting intersecting boxes in this boxa</param>
    /// <returns>number of boxes intersecting the box</returns>
    public int IntersectsBoxCount(Box box)
    {
        int count = 0;
        int n = Count;
        bool valid = box.IsValid();
        if (n == 0 || !valid)
            return 0;

        for (int i = 0; i < n; i++) {
            Box? box1 = GetValidBox(i, L.L_CLONE);
            if (box1 == null)
                continue;
            bool val = box.Intersects(box1);
            if (val)
                count++;
        }
        return count;
    }

    // leptonica: boxaClipToBox (boxfunc1.c)
    /// <summary>Clips the boxes to a box, removing those that do not intersect it.</summary>
    /// <param name="box">for clipping</param>
    /// <returns>boxad  boxa with boxes in this boxa clipped to box</returns>
    /// <remarks>
    /// (1) All boxes in boxa not intersecting with box are removed, and
    ///     the remaining boxes are clipped to box.
    /// </remarks>
    public Boxa ClipToBox(Box box)
    {
        int n = Count;
        bool valid = box.IsValid();
        if (n == 0 || !valid)
            return Create(1);  /* empty */

        Boxa boxad = Create(0);
        for (int i = 0; i < n; i++) {
            Box? box1 = GetValidBox(i, L.L_CLONE);
            if (box1 == null)
                continue;
            Box? boxo = box.OverlapRegion(box1);
            if (boxo != null)
                boxad.AddBox(boxo, L.L_INSERT);
        }

        return boxad;
    }

    // leptonica: boxaCombineOverlaps (boxfunc1.c)
    /// <summary>Combines each set of overlapping boxes into their bounding box.</summary>
    /// <param name="pixadb">[optional] debug output; use an empty Pixa</param>
    /// <returns>boxad  where each set of boxes in this boxa that overlap are combined
    ///                 into a single bounding box in boxad</returns>
    /// <remarks>
    /// (1) If there are no overlapping boxes, it simply returns a copy
    ///     of %boxas.
    /// (2) Input an empty %pixadb, using pixaCreate(0), for debug output.
    ///     The output gives 2 visualizations of the boxes per iteration;
    ///     boxes in red before, and added boxes in green after. Note that
    ///     all pixels in the red boxes are contained in the green ones.
    /// (3) The alternative method of painting each rectangle and finding
    ///     the 4-connected components gives a different result in
    ///     general, because two non-overlapping (but touching)
    ///     rectangles, when rendered, are 4-connected and will be joined.
    /// (4) A bad case computationally is to have n boxes, none of which
    ///     overlap.  Then you have one iteration with O(n^2) compares.
    ///     This is still faster than painting each rectangle and finding
    ///     the bounding boxes of the connected components, even for
    ///     thousands of rectangles.
    /// </remarks>
    public Boxa CombineOverlaps(Pixa? pixadb = null)
    {
        const string func = "boxaCombineOverlaps";
        Boxa boxas = this;
        int w = 0, h = 0;
        Pix? pix1 = null;

        if (pixadb != null) boxas.GetExtent(out w, out h, out _);

        Boxa boxa1 = boxas.Copy(L.L_COPY);
        int n1 = boxa1.Count;
        int niters = 0;
        while (true) {  /* loop until no change from previous iteration */
            niters++;
            if (pixadb != null) {
                pix1 = Pix.Create(w + 5, h + 5, 32);
                pix1.SetAll();
                pix1.RenderBoxaArb(boxa1, 2, 255, 0, 0);
                pixadb.AddPix(pix1, L.L_COPY);
            }

                /* Combine overlaps for this iteration */
            for (int i = 0; i < n1; i++) {
                Box? box1 = boxa1.GetValidBox(i, L.L_COPY);
                if (box1 == null)
                    continue;
                for (int j = i + 1; j < n1; j++) {
                    Box? box2 = boxa1.GetValidBox(j, L.L_COPY);
                    if (box2 == null)
                        continue;
                    bool overlap = box1.Intersects(box2);
                    if (overlap) {
                        Box box3 = box1.BoundingRegion(box2);
                        boxa1.ReplaceBox(i, box3);
                        boxa1.ReplaceBox(j, Box.Create(0, 0, 0, 0));
                        box1 = box3.Copy();
                    }
                }
            }
            Boxa boxa2 = boxa1.SaveValid(L.L_COPY);
            int n2 = boxa2.Count;
            boxa1 = boxa2;
            if (n1 == n2) {
                break;
            }
            n1 = n2;
            if (pixadb != null) {
                pix1!.RenderBoxaArb(boxa1, 2, 0, 255, 0);
                pixadb.AddPix(pix1, L.L_INSERT);
            }
        }

        if (pixadb != null)
            Log.Info(func, $"number of iterations: {niters}\n");
        return boxa1;
    }

    // leptonica: boxaCombineOverlapsInPair (boxfunc1.c)
    /// <summary>Combines the overlapping boxes of this boxa and of a second one.</summary>
    /// <param name="boxas2">input boxa2</param>
    /// <param name="boxad1">output boxa1</param>
    /// <param name="boxad2">output boxa2</param>
    /// <param name="pixadb">[optional] debug output; use an empty Pixa</param>
    /// <remarks>
    /// (1) One of three things happens to each box in %boxa1 and %boxa2:
    ///      * it gets absorbed into a larger box that it overlaps with
    ///      * it absorbs a smaller (by area) box that it overlaps with
    ///        and gets larger, using the bounding region of the 2 boxes
    ///      * it is unchanged (including absorbing smaller boxes that
    ///        are contained within it).
    /// (2) If all the boxes from one of the input boxa are absorbed, this
    ///     returns an empty boxa.
    /// (3) Input an empty %pixadb, using pixaCreate(0), for debug output
    /// (4) This is useful if different operations are to be carried out
    ///     on possibly overlapping rectangular regions, and it is desired
    ///     to have only one operation on any rectangular region.
    /// </remarks>
    public void CombineOverlapsInPair(Boxa boxas2, out Boxa boxad1, out Boxa boxad2,
                                      Pixa? pixadb = null)
    {
        const string func = "boxaCombineOverlapsInPair";
        Boxa boxas1 = this;
        int w = 0, h = 0;

        if (pixadb != null) {
            boxas1.GetExtent(out w, out h, out _);
            boxas2.GetExtent(out int w2, out int h2, out _);
            w = Math.Max(w, w2);
            h = Math.Max(h, w2);  // NOTE(upstream): should be h2
        }

            /* Let the boxa with the largest area have first crack at the other */
        int area1 = boxas1.GetArea();
        int area2 = boxas2.GetArea();
        Boxa boxac1, boxac2;
        if (area1 >= area2) {
            boxac1 = boxas1.Copy(L.L_COPY);
            boxac2 = boxas2.Copy(L.L_COPY);
        } else {
            boxac1 = boxas2.Copy(L.L_COPY);
            boxac2 = boxas1.Copy(L.L_COPY);
        }

        int n1i = boxac1.Count;
        int n2i = boxac2.Count;
        int niters = 0;
        while (true) {
            niters++;
            if (pixadb != null) {
                Pix pix1 = Pix.Create(w + 5, h + 5, 32);
                pix1.SetAll();
                pix1.RenderBoxaArb(boxac1, 2, 255, 0, 0);
                pix1.RenderBoxaArb(boxac2, 2, 0, 255, 0);
                pixadb.AddPix(pix1, L.L_INSERT);
            }

                /* First combine boxes in each set */
            Boxa boxa1 = boxac1.CombineOverlaps(null);
            Boxa boxa2 = boxac2.CombineOverlaps(null);

                /* Now combine boxes between sets */
            int n1 = boxa1.Count;
            int n2 = boxa2.Count;
            for (int i = 0; i < n1; i++) {  /* 1 eats 2 */
                Box? box1 = boxa1.GetValidBox(i, L.L_COPY);
                if (box1 == null)
                    continue;
                for (int j = 0; j < n2; j++) {
                    Box? box2 = boxa2.GetValidBox(j, L.L_COPY);
                    if (box2 == null)
                        continue;
                    bool overlap = box1.Intersects(box2);
                    int bigger = box1.CompareSize(box2, L.L_SORT_BY_AREA);
                    if (overlap && (bigger == 1)) {
                        Box box3 = box1.BoundingRegion(box2);
                        boxa1.ReplaceBox(i, box3);
                        boxa2.ReplaceBox(j, Box.Create(0, 0, 0, 0));
                        box1 = box3.Copy();
                    }
                }
            }
            for (int i = 0; i < n2; i++) {  /* 2 eats 1 */
                Box? box2 = boxa2.GetValidBox(i, L.L_COPY);
                if (box2 == null)
                    continue;
                for (int j = 0; j < n1; j++) {
                    Box? box1 = boxa1.GetValidBox(j, L.L_COPY);
                    if (box1 == null)
                        continue;
                    bool overlap = box1.Intersects(box2);
                    int bigger = box2.CompareSize(box1, L.L_SORT_BY_AREA);
                    if (overlap && (bigger == 1)) {
                        Box box3 = box1.BoundingRegion(box2);
                        boxa2.ReplaceBox(i, box3);
                        boxa1.ReplaceBox(j, Box.Create(0, 0, 0, 0));
                        box2 = box3.Copy();
                    }
                }
            }
            boxac1 = boxa1.SaveValid(L.L_COPY);  /* remove invalid boxes */
            boxac2 = boxa2.SaveValid(L.L_COPY);
            n1 = boxac1.Count;
            n2 = boxac2.Count;
            if (n1 == n1i && n2 == n2i) break;
            n1i = n1;
            n2i = n2;
            if (pixadb != null) {
                Pix pix1 = Pix.Create(w + 5, h + 5, 32);
                pix1.SetAll();
                pix1.RenderBoxaArb(boxac1, 2, 255, 0, 0);
                pix1.RenderBoxaArb(boxac2, 2, 0, 255, 0);
                pixadb.AddPix(pix1, L.L_INSERT);
            }
        }

        if (pixadb != null)
            Log.Info(func, $"number of iterations: {niters}\n");
        boxad1 = boxac1;
        boxad2 = boxac2;
    }
}

public sealed partial class Box
{
    // leptonica: boxOverlapRegion (boxfunc1.c)
    /// <summary>Returns the overlap region of two boxes.</summary>
    /// <param name="box2">the other box</param>
    /// <returns>box of overlap region between this box and box2;
    ///          null if no overlap or if at least one box is invalid</returns>
    /// <remarks>
    /// (1) This is the geometric intersection of the two rectangles.
    /// </remarks>
    public Box? OverlapRegion(Box box2)
    {
        const string func = "boxOverlapRegion";

        bool valid1 = IsValid();
        bool valid2 = box2.IsValid();
        if (!valid1 || !valid2) {
            Log.Warning(func, "at least one box is invalid\n");
            return null;
        }

        GetGeometry(out int l1, out int t1, out int w1, out int h1);
        box2.GetGeometry(out int l2, out int t2, out int w2, out int h2);
        int r1 = l1 + w1 - 1;
        int r2 = l2 + w2 - 1;
        int b1 = t1 + h1 - 1;
        int b2 = t2 + h2 - 1;
        if (b2 < t1 || b1 < t2 || r1 < l2 || r2 < l1)
            return null;

        int ld = Math.Max(l1, l2);
        int td = Math.Max(t1, t2);
        int rd = Math.Min(r1, r2);
        int bd = Math.Min(b1, b2);
        return Create(ld, td, rd - ld + 1, bd - td + 1);
    }

    // leptonica: boxBoundingRegion (boxfunc1.c)
    /// <summary>Returns the bounding region of two boxes.</summary>
    /// <param name="box2">the other box</param>
    /// <returns>box of bounding region containing this box and box2</returns>
    /// <remarks>
    /// (1) This is the geometric union of the two rectangles.
    /// (2) Invalid boxes are ignored.  This returns an invalid box
    ///     if both input boxes are invalid.
    /// (3) For the geometric union of a boxa, use boxaGetExtent().
    /// </remarks>
    public Box BoundingRegion(Box box2)
    {
        const string func = "boxBoundingRegion";
        Box box1 = this;

        bool valid1 = box1.IsValid();
        bool valid2 = box2.IsValid();
        if (!valid1 && !valid2) {
            Log.Warning(func, "both boxes are invalid\n");
            return Create(0, 0, 0, 0);
        }
        if (valid1 && !valid2)
            return box1.Copy();
        if (!valid1 && valid2)
            return box2.Copy();

        box1.GetGeometry(out int l1, out int t1, out int w1, out int h1);
        box2.GetGeometry(out int l2, out int t2, out int w2, out int h2);
        int r1 = l1 + w1 - 1;
        int r2 = l2 + w2 - 1;
        int b1 = t1 + h1 - 1;
        int b2 = t2 + h2 - 1;
        int ld = Math.Min(l1, l2);
        int td = Math.Min(t1, t2);
        int rd = Math.Max(r1, r2);
        int bd = Math.Max(b1, b2);
        return Create(ld, td, rd - ld + 1, bd - td + 1);
    }

    // leptonica: boxOverlapFraction (boxfunc1.c)
    /// <summary>Returns the fraction of box2 that is overlapped by this box.</summary>
    /// <param name="box2">the box whose overlapped fraction is measured</param>
    /// <returns>the fraction of box2 overlapped by this box</returns>
    /// <remarks>
    /// (1) The result depends on the order of the input boxes,
    ///     because the overlap is taken as a fraction of box2.
    /// (2) If at least one box is not valid, there is no overlap.
    /// </remarks>
    public float OverlapFraction(Box box2)
    {
        const string func = "boxOverlapFraction";

        bool valid1 = IsValid();
        bool valid2 = box2.IsValid();
        if (!valid1 || !valid2) {
            Log.Warning(func, "boxes not both valid\n");
            return 0.0f;
        }

        Box? boxo = OverlapRegion(box2);
        if (boxo == null)  /* no overlap */
            return 0.0f;

        box2.GetGeometry(out _, out _, out int w2, out int h2);
        boxo.GetGeometry(out _, out _, out int w, out int h);
        return (float)(w * h) / (float)(w2 * h2);
    }

    // leptonica: boxOverlapArea (boxfunc1.c)
    /// <summary>Returns the number of pixels in the overlap of two boxes.</summary>
    /// <param name="box2">the other box</param>
    /// <returns>the number of pixels in the overlap</returns>
    public int OverlapArea(Box box2)
    {
        const string func = "boxOverlapArea";

        bool valid1 = IsValid();
        bool valid2 = box2.IsValid();
        if (!valid1 || !valid2)
            throw Lept.Arg(func, "boxes not both valid");

        Box? box = OverlapRegion(box2);
        if (box == null)  /* no overlap */
            return 0;

        box.GetGeometry(out _, out _, out int w, out int h);
        return w * h;
    }
}

public sealed partial class Boxa
{
    // leptonica: boxaHandleOverlaps (boxfunc1.c)
    /// <summary>Combines or removes the smaller of overlapping boxes.</summary>
    /// <param name="op">L_COMBINE, L_REMOVE_SMALL</param>
    /// <param name="range">forward distance over which overlaps are checked; &gt; 0</param>
    /// <param name="minOverlap">minimum fraction of smaller box required for
    ///                          overlap to count; 0.0 to ignore</param>
    /// <param name="maxRatio">maximum fraction of small/large areas for
    ///                        overlap to count; 1.0 to ignore</param>
    /// <param name="namap">combining map</param>
    /// <returns>boxad</returns>
    /// <remarks>
    /// (1) For all n(n-1)/2 box pairings, if two boxes overlap, either:
    ///     (a) op == L_COMBINE: get the bounding region for the two,
    ///         replace the larger with the bounding region, and remove
    ///         the smaller of the two, or
    ///     (b) op == L_REMOVE_SMALL: just remove the smaller.
    /// (2) If boxas is 2D sorted, range can be small, but if it is
    ///     not spatially sorted, range should be large to allow all
    ///     pairwise comparisons to be made.
    /// (3) The %min_overlap parameter allows ignoring small overlaps.
    ///     If %min_overlap == 1.0, only boxes fully contained in larger
    ///     boxes can be considered for removal; if %min_overlap == 0.0,
    ///     this constraint is ignored.
    /// (4) The %max_ratio parameter allows ignoring overlaps between
    ///     boxes that are not too different in size.  If %max_ratio == 0.0,
    ///     no boxes can be removed; if %max_ratio == 1.0, this constraint
    ///     is ignored.
    /// </remarks>
    public Boxa HandleOverlaps(int op, int range, float minOverlap, float maxRatio,
                               out Numa? namap)
    {
        const string func = "boxaHandleOverlaps";
        Boxa boxas = this;

        namap = null;
        if (op != L.L_COMBINE && op != L.L_REMOVE_SMALL)
            throw Lept.Arg(func, "invalid op");

        int n = boxas.Count;
        if (n == 0)
            return Create(1);  /* empty */
        if (range == 0) {
            Log.Warning(func, "range is 0\n");
            return boxas.Copy(L.L_COPY);
        }

            /* Identify smaller boxes in overlap pairs, and mark to eliminate. */
        Numa na = Numa.MakeConstant(-1, n);
        for (int i = 0; i < n; i++) {
            Box? box1 = boxas.GetValidBox(i, L.L_CLONE);
            if (box1 == null)
                continue;
            box1.GetGeometry(out _, out _, out int w, out int h);
            int area1 = w * h;
            if (area1 == 0)
                continue;
            for (int j = i + 1; j < i + 1 + range && j < n; j++) {
                Box? box2 = boxas.GetValidBox(j, L.L_CLONE);
                if (box2 == null)
                    continue;
                int overlap_area = box1.OverlapArea(box2);
                if (overlap_area > 0) {
                    box2.GetGeometry(out _, out _, out w, out h);
                    int area2 = w * h;
                    if (area2 == 0) {
                        /* do nothing */
                    } else if (area1 >= area2) {
                        float overlap_ratio = (float)overlap_area / (float)area2;
                        float area_ratio = (float)area2 / (float)area1;
                        if (overlap_ratio >= minOverlap &&
                            area_ratio <= maxRatio) {
                            na.SetValue(j, i);
                        }
                    } else {
                        float overlap_ratio = (float)overlap_area / (float)area1;
                        float area_ratio = (float)area1 / (float)area2;
                        if (overlap_ratio >= minOverlap &&
                            area_ratio <= maxRatio) {
                            na.SetValue(i, j);
                        }
                    }
                }
            }
        }

        Boxa boxat = boxas.Copy(L.L_COPY);
        if (op == L.L_COMBINE) {
                /* Resize the larger of the pair to the bounding region */
            for (int i = 0; i < n; i++) {
                int val = na.GetIValue(i);
                if (val >= 0) {
                    Box box1 = boxas.GetBox(i, L.L_CLONE);  /* smaller */
                    Box box2 = boxas.GetBox(val, L.L_CLONE);  /* larger */
                    Box box3 = box1.BoundingRegion(box2);
                    boxat.ReplaceBox(val, box3);
                }
            }
        }

            /* Remove the smaller of the pairs */
        Boxa boxad = Create(n);
        for (int i = 0; i < n; i++) {
            int val = na.GetIValue(i);
            if (val == -1) {
                Box box1 = boxat.GetBox(i, L.L_COPY);
                boxad.AddBox(box1, L.L_INSERT);
            }
        }
        namap = na;
        return boxad;
    }
}

public sealed partial class Box
{
    // leptonica: boxOverlapDistance (boxfunc1.c)
    /// <summary>Measures the horizontal and vertical overlap of two boxes.</summary>
    /// <param name="box2">the other box; the two boxes can be in any order</param>
    /// <param name="hOvl">horizontal overlap</param>
    /// <param name="vOvl">vertical overlap</param>
    /// <remarks>
    /// (1) This measures horizontal and vertical overlap of the
    ///     two boxes.  Horizontal and vertical overlap are measured
    ///     independently.  We need to consider several cases to clarify.
    /// (2) A positive horizontal overlap means that there is at least
    ///     one point on the the %box1 boundary with the same x-component
    ///     as some point on the %box2 boundary.  Conversely, with a zero
    ///     or negative horizontal overlap, there are no boundary pixels
    ///     in %box1 that share an x-component with a boundary pixel in %box2.
    /// (3) For a zero or negative horizontal overlap, o &lt;= 0, the minimum
    ///     difference in the x-component between pixels on the boundaries
    ///     of the two boxes is d = -o + 1.
    /// (4) Likewise for vertical overlaps.
    /// </remarks>
    public void OverlapDistance(Box box2, out int hOvl, out int vOvl)
    {
        const string func = "boxOverlapDistance";

        hOvl = 0;
        vOvl = 0;
        bool valid1 = IsValid();
        bool valid2 = box2.IsValid();
        if (!valid1 || !valid2)
            throw Lept.Arg(func, "boxes not both valid");

        GetGeometry(out int l1, out int t1, out int w1, out int h1);
        box2.GetGeometry(out int l2, out int t2, out int w2, out int h2);
        int r1 = l1 + w1;  /* 1 pixel to the right of box 1 */
        int r2 = l2 + w2;
        if (l2 >= l1)
            hOvl = r1 - l2;
        else
            hOvl = r2 - l1;
        int b1 = t1 + h1;  /* 1 pixel below box 1 */
        int b2 = t2 + h2;
        if (t2 >= t1)
            vOvl = b1 - t2;
        else
            vOvl = b2 - t1;
    }

    // leptonica: boxSeparationDistance (boxfunc1.c)
    /// <summary>Measures the horizontal and vertical separation of two boxes.</summary>
    /// <param name="box2">the other box; the two boxes can be in any order</param>
    /// <param name="hSep">horizontal separation</param>
    /// <param name="vSep">vertical separation</param>
    /// <remarks>
    /// (1) This measures the Manhattan distance between the closest points
    ///     on the boundaries of the two boxes.  When the boxes overlap
    ///     (including touching along a line or at a corner), the
    ///     horizontal and vertical distances are 0.
    /// (2) The distances represent the horizontal and vertical separation
    ///     of the two boxes.  The boxes have a nonzero intersection when
    ///     both the horizontal and vertical overlaps are positive, and
    ///     for that case both horizontal and vertical separation
    ///     distances are 0.
    /// (3) If the horizontal overlap of the boxes is positive, the
    ///     horizontal separation between nearest points on respective
    ///     boundaries is 0, and likewise for the vertical overlap.
    /// (4) If the horizontal overlap ho &lt;= 0, the horizontal
    ///     separation between nearest points is d = -ho + 1.
    ///     Likewise, if the vertical overlap vo &lt;= 0, the vertical
    ///     separation between nearest points is d = -vo + 1.
    /// </remarks>
    public void SeparationDistance(Box box2, out int hSep, out int vSep)
    {
        const string func = "boxSeparationDistance";

        hSep = 0;
        vSep = 0;
        bool valid1 = IsValid();
        bool valid2 = box2.IsValid();
        if (!valid1 || !valid2)
            throw Lept.Arg(func, "boxes not both valid");

        OverlapDistance(box2, out int h_ovl, out int v_ovl);
        if (h_ovl <= 0)
            hSep = -h_ovl + 1;
        if (v_ovl <= 0)
            vSep = -v_ovl + 1;
    }

    // leptonica: boxCompareSize (boxfunc1.c)
    /// <summary>Compares the sizes of two boxes.</summary>
    /// <param name="box2">the box compared with this one</param>
    /// <param name="type">L_SORT_BY_WIDTH, L_SORT_BY_HEIGHT,
    ///                    L_SORT_BY_MAX_DIMENSION, L_SORT_BY_PERIMETER,
    ///                    L_SORT_BY_AREA</param>
    /// <returns>1 if this box &gt; box2, 0 if the same, -1 if this box &lt; box2</returns>
    /// <remarks>
    /// (1) We're re-using the SORT enum for these comparisons.
    /// </remarks>
    public int CompareSize(Box box2, int type)
    {
        const string func = "boxCompareSize";

        int rel = 0;
        bool valid1 = IsValid();
        bool valid2 = box2.IsValid();
        if (!valid1 || !valid2)
            throw Lept.Arg(func, "boxes not both valid");
        if (type != L.L_SORT_BY_WIDTH && type != L.L_SORT_BY_HEIGHT &&
            type != L.L_SORT_BY_MAX_DIMENSION && type != L.L_SORT_BY_PERIMETER &&
            type != L.L_SORT_BY_AREA)
            throw Lept.Arg(func, "invalid compare type");

        GetGeometry(out _, out _, out int w1, out int h1);
        box2.GetGeometry(out _, out _, out int w2, out int h2);
        if (type == L.L_SORT_BY_WIDTH) {
            rel = (w1 > w2) ? 1 : ((w1 == w2) ? 0 : -1);
        } else if (type == L.L_SORT_BY_HEIGHT) {
            rel = (h1 > h2) ? 1 : ((h1 == h2) ? 0 : -1);
        } else if (type == L.L_SORT_BY_MAX_DIMENSION) {
            int size1 = Math.Max(w1, h1);
            int size2 = Math.Max(w2, h2);
            rel = (size1 > size2) ? 1 : ((size1 == size2) ? 0 : -1);
        } else if (type == L.L_SORT_BY_PERIMETER) {
            int size1 = w1 + h1;
            int size2 = w2 + h2;
            rel = (size1 > size2) ? 1 : ((size1 == size2) ? 0 : -1);
        } else if (type == L.L_SORT_BY_AREA) {
            int size1 = w1 * h1;
            int size2 = w2 * h2;
            rel = (size1 > size2) ? 1 : ((size1 == size2) ? 0 : -1);
        }
        return rel;
    }

    // leptonica: boxContainsPt (boxfunc1.c)
    /// <summary>Tests if the box contains a point.</summary>
    /// <param name="x">the point</param>
    /// <param name="y">the point</param>
    /// <returns>true if the box contains the point; false otherwise</returns>
    public bool ContainsPt(float x, float y)
    {
        GetGeometry(out int bx, out int by, out int bw, out int bh);
        if (x >= bx && x < bx + bw && y >= by && y < by + bh)
            return true;
        return false;
    }
}

public sealed partial class Boxa
{
    // leptonica: boxaGetNearestToPt (boxfunc1.c)
    /// <summary>Returns the box with the centroid closest to a point.</summary>
    /// <param name="x">point</param>
    /// <param name="y">point</param>
    /// <returns>box with centroid closest to the given point [x,y]; throws if no boxes in boxa</returns>
    /// <remarks>
    /// (1) Uses euclidean distance between centroid and point.
    /// </remarks>
    public Box GetNearestToPt(int x, int y)
    {
        int n = Count;
        if (n == 0)
            throw Lept.Arg("boxaGetNearestToPt", "n = 0");

        float mindist = 1000000000f;
        int minindex = 0;
        for (int i = 0; i < n; i++) {
            Box? box = GetValidBox(i, L.L_CLONE);
            if (box == null)
                continue;
            box.GetCenter(out float cx, out float cy);
            float delx = (float)(cx - x);
            float dely = (float)(cy - y);
            float dist = delx * delx + dely * dely;
            if (dist < mindist) {
                minindex = i;
                mindist = dist;
            }
        }

        return GetBox(minindex, L.L_COPY);
    }

    // leptonica: boxaGetNearestToLine (boxfunc1.c)
    /// <summary>Returns the box with the centroid closest to a horizontal or vertical line.</summary>
    /// <param name="x">(x = -1 for horiz line)</param>
    /// <param name="y">(y = -1 for vertical line)</param>
    /// <returns>box with centroid closest to the given line; throws if no boxes in boxa</returns>
    /// <remarks>
    /// (1) For a horizontal line at some value y, get the minimum of the
    ///     distance |yc - y| from the box centroid yc value to y;
    ///     likewise minimize |xc - x| for a vertical line at x.
    /// (2) Input y &lt; 0, x &gt;= 0 to indicate a vertical line at x, and
    ///     x &lt; 0, y &gt;= 0 for a horizontal line at y.
    /// </remarks>
    public Box GetNearestToLine(int x, int y)
    {
        const string func = "boxaGetNearestToLine";

        int n = Count;
        if (n == 0)
            throw Lept.Arg(func, "n = 0");
        if (y >= 0 && x >= 0)
            throw Lept.Arg(func, "either x or y must be < 0");
        if (y < 0 && x < 0)
            throw Lept.Arg(func, "either x or y must be >= 0");

        float mindist = 1000000000f;
        int minindex = 0;
        for (int i = 0; i < n; i++) {
            Box? box = GetValidBox(i, L.L_CLONE);
            if (box == null)
                continue;
            box.GetCenter(out float cx, out float cy);
            float dist;
            if (x >= 0)
                dist = MathF.Abs(cx - (float)x);
            else  /* y >= 0 */
                dist = MathF.Abs(cy - (float)y);
            if (dist < mindist) {
                minindex = i;
                mindist = dist;
            }
        }

        return GetBox(minindex, L.L_COPY);
    }

    // leptonica: boxaFindNearestBoxes (boxfunc1.c)
    /// <summary>Finds, for each box, the nearest box in each of the four directions.</summary>
    /// <param name="distSelect">L_NON_NEGATIVE, L_ALL</param>
    /// <param name="range">search distance from box i; use 0 to search
    ///                     entire boxa (e.g., if it's not 2D sorted)</param>
    /// <param name="naaindex">for each box in %boxa, contains a numa of 4
    ///                        box indices (per direction) of the nearest box</param>
    /// <param name="naadist">for each box in %boxa, this contains a numa</param>
    /// <remarks>
    /// (1) See boxaGetNearestByDirection() for usage of %dist_select
    ///     and %range.  This is for a boxa that is either unsorted, or
    ///     2D sorted in LR/TB scan order.
    /// </remarks>
    public void FindNearestBoxes(int distSelect, int range, out Numaa naaindex, out Numaa naadist)
    {
        int n = Count;
        Numaa naai = Numaa.Create(n);
        Numaa naad = Numaa.Create(n);
        for (int i = 0; i < n; i++) {
            Numa nai = Numa.Create(4);
            Numa nad = Numa.Create(4);
            GetNearestByDirection(i, L.L_FROM_LEFT, distSelect, range, out int index, out int dist);
            nai.AddNumber(index);
            nad.AddNumber(dist);
            GetNearestByDirection(i, L.L_FROM_RIGHT, distSelect, range, out index, out dist);
            nai.AddNumber(index);
            nad.AddNumber(dist);
            GetNearestByDirection(i, L.L_FROM_TOP, distSelect, range, out index, out dist);
            nai.AddNumber(index);
            nad.AddNumber(dist);
            GetNearestByDirection(i, L.L_FROM_BOT, distSelect, range, out index, out dist);
            nai.AddNumber(index);
            nad.AddNumber(dist);
            naai.AddNuma(nai, L.L_INSERT);
            naad.AddNuma(nad, L.L_INSERT);
        }
        naaindex = naai;
        naadist = naad;
    }

    // leptonica: boxaGetNearestByDirection (boxfunc1.c)
    /// <summary>Finds the nearest box to box i in a given direction.</summary>
    /// <param name="i">box we test against</param>
    /// <param name="dir">direction to look: L_FROM_LEFT, L_FROM_RIGHT,
    ///                   L_FROM_TOP, L_FROM_BOT</param>
    /// <param name="distSelect">L_NON_NEGATIVE, L_ALL</param>
    /// <param name="range">search distance from box i; use 0 to search
    ///                     entire boxa (e.g., if it's not 2D sorted)</param>
    /// <param name="pindex">index in boxa of nearest box with overlapping
    ///                      coordinates in the indicated direction;
    ///                      -1 if there is no box</param>
    /// <param name="pdist">distance of the nearest box in the indicated
    ///                     direction; 100000 if no box</param>
    /// <remarks>
    /// (1) For efficiency, use a LR/TD sorted %boxa, which can be
    ///     made by flattening a 2D sorted boxaa.  In that case,
    ///     %range can be some positive integer like 50.
    /// (2) If boxes overlap, the distance will be &lt; 0.  Use %dist_select
    ///     to determine if these should count or not.  If L_ALL, then
    ///     one box will match as the nearest to another in 2 or more
    ///     directions.
    /// </remarks>
    public void GetNearestByDirection(int i, int dir, int distSelect, int range,
                                      out int pindex, out int pdist)
    {
        const string func = "boxaGetNearestByDirection";

        pindex = -1;
        pdist = 100000;
        if (dir != L.L_FROM_LEFT && dir != L.L_FROM_RIGHT &&
            dir != L.L_FROM_TOP && dir != L.L_FROM_BOT)
            throw Lept.Arg(func, "invalid dir");
        if (distSelect != L.L_NON_NEGATIVE && distSelect != L.L_ALL)
            throw Lept.Arg(func, "invalid dist_select");
        int n = Count;
        if (i < 0 || i >= n)
            throw Lept.Arg(func, "invalid box index");

        int jmin = (range <= 0) ? 0 : Math.Max(0, i - range);
        int jmax = (range <= 0) ? n - 1 : Math.Min(n - 1, i + range);
        GetBoxGeometry(i, out int x, out int y, out int w, out int h);
        int mindist = 100000;
        int index = -1;
        if (dir == L.L_FROM_LEFT || dir == L.L_FROM_RIGHT) {
            for (int j = jmin; j <= jmax; j++) {
                if (j == i) continue;
                GetBoxGeometry(j, out int bx, out int by, out int bw, out int bh);
                if ((bx >= x && dir == L.L_FROM_LEFT) ||  /* not to the left */
                    (x >= bx && dir == L.L_FROM_RIGHT))   /* not to the right */
                    continue;
                if (HasOverlapInXorY(y, h, by, bh) == 1) {
                    int dist = GetDistanceInXorY(x, w, bx, bw);
                    if (distSelect == L.L_NON_NEGATIVE && dist < 0) continue;
                    if (dist < mindist) {
                        mindist = dist;
                        index = j;
                    }
                }
            }
        } else if (dir == L.L_FROM_TOP || dir == L.L_FROM_BOT) {
            for (int j = jmin; j <= jmax; j++) {
                if (j == i) continue;
                GetBoxGeometry(j, out int bx, out int by, out int bw, out int bh);
                if ((by >= y && dir == L.L_FROM_TOP) ||  /* not above */
                    (y >= by && dir == L.L_FROM_BOT))   /* not below */
                    continue;
                if (HasOverlapInXorY(x, w, bx, bw) == 1) {
                    int dist = GetDistanceInXorY(y, h, by, bh);
                    if (distSelect == L.L_NON_NEGATIVE && dist < 0) continue;
                    if (dist < mindist) {
                        mindist = dist;
                        index = j;
                    }
                }
            }
        }
        pindex = index;
        pdist = mindist;
    }

    // leptonica: boxHasOverlapInXorY (boxfunc1.c)
    /// <param name="c1">left or top coordinate of box1</param>
    /// <param name="s1">width or height of box1</param>
    /// <param name="c2">left or top coordinate of box2</param>
    /// <param name="s2">width or height of box2</param>
    /// <returns>0 if no overlap; 1 if any overlap</returns>
    /// <remarks>
    /// (1) Like boxGetDistanceInXorY(), this is used for overlaps both in
    ///     x (which projected vertically) and in y (projected horizontally)
    /// </remarks>
    private static int HasOverlapInXorY(int c1, int s1, int c2, int s2)
    {
        int ovlp;

        if (c1 > c2)
            ovlp = c2 + s2 - 1 - c1;
        else
            ovlp = c1 + s1 - 1 - c2;
        return (ovlp < 0) ? 0 : 1;
    }

    // leptonica: boxGetDistanceInXorY (boxfunc1.c)
    /// <param name="c1">left or top coordinate of box1</param>
    /// <param name="s1">width or height of box1</param>
    /// <param name="c2">left or top coordinate of box2</param>
    /// <param name="s2">width or height of box2</param>
    /// <returns>distance between them (if &lt; 0, box2 overlaps box1 in the
    ///          dimension considered)</returns>
    private static int GetDistanceInXorY(int c1, int s1, int c2, int s2)
    {
        int dist;

        if (c1 > c2)
            dist = c1 - (c2 + s2 - 1);
        else
            dist = c2 - (c1 + s1 - 1);
        return dist;
    }
}

public sealed partial class Box
{
    // leptonica: boxGetCenter (boxfunc1.c)
    /// <summary>Gets the location of the center of the box.</summary>
    /// <param name="cx">location of center of box</param>
    /// <param name="cy">location of center of box</param>
    /// <returns>true if OK; false (with cx = cy = 0) if the box has no area</returns>
    public bool GetCenter(out float cx, out float cy)
    {
        cx = 0;
        cy = 0;
        GetGeometry(out int x, out int y, out int w, out int h);
        if (w == 0 || h == 0) return false;
        cx = (float)(x + 0.5 * w);
        cy = (float)(y + 0.5 * h);

        return true;
    }

    // leptonica: boxIntersectByLine (boxfunc1.c)
    /// <summary>Finds the intersection points of a line with the boundary of the box.</summary>
    /// <param name="x">point that line goes through</param>
    /// <param name="y">point that line goes through</param>
    /// <param name="slope">of line</param>
    /// <param name="x1">1st point of intersection with box</param>
    /// <param name="y1">1st point of intersection with box</param>
    /// <param name="x2">2nd point of intersection with box</param>
    /// <param name="y2">2nd point of intersection with box</param>
    /// <param name="n">number of points of intersection</param>
    /// <returns>true if OK; false (with all outputs 0) if the box has no area</returns>
    /// <remarks>
    /// (1) If the intersection is at only one point (a corner), the
    ///     coordinates are returned in (x1, y1).
    /// (2) Represent a vertical line by one with a large but finite slope.
    /// </remarks>
    public bool IntersectByLine(int x, int y, float slope, out int x1, out int y1,
                                out int x2, out int y2, out int n)
    {
        x1 = 0;
        x2 = 0;
        y1 = 0;
        y2 = 0;
        n = 0;
        GetGeometry(out int bx, out int by, out int bw, out int bh);
        if (bw == 0 || bh == 0) return false;

        if (slope == 0.0) {
            if (y >= by && y < by + bh) {
                y1 = y2 = y;
                x1 = bx;
                x2 = bx + bw - 1;
            }
            return true;
        }

        if (slope > 1000000.0) {
            if (x >= bx && x < bx + bw) {
                x1 = x2 = x;
                y1 = by;
                y2 = by + bh - 1;
            }
            return true;
        }

            /* Intersection with top and bottom lines of box */
        Pta pta = Pta.Create(2);
        float invslope = (float)(1.0 / slope);
        int xp = (int)(x + invslope * (y - by));
        if (xp >= bx && xp < bx + bw)
            pta.AddPt(xp, by);
        xp = (int)(x + invslope * (y - by - bh + 1));
        if (xp >= bx && xp < bx + bw)
            pta.AddPt(xp, by + bh - 1);

            /* Intersection with left and right lines of box */
        int yp = (int)(y + slope * (x - bx));
        if (yp >= by && yp < by + bh)
            pta.AddPt(bx, yp);
        yp = (int)(y + slope * (x - bx - bw + 1));
        if (yp >= by && yp < by + bh)
            pta.AddPt(bx + bw - 1, yp);

            /* There is a maximum of 2 unique points; remove duplicates.  */
        int npts = pta.Count;
        if (npts > 0) {
            pta.GetIPt(0, out x1, out y1);  /* accept the first one */
            n = 1;
        }
        for (int i = 1; i < npts; i++) {
            pta.GetIPt(i, out int xt, out int yt);
            if ((x1 != xt) || (y1 != yt)) {
                x2 = xt;
                y2 = yt;
                n = 2;
                break;
            }
        }

        return true;
    }

    // leptonica: boxClipToRectangle (boxfunc1.c)
    /// <summary>Clips the box to a rectangle with its upper left corner at (0, 0).</summary>
    /// <param name="wi">rectangle representing image</param>
    /// <param name="hi">rectangle representing image</param>
    /// <returns>part of box within given rectangle; throws if box is entirely
    ///          outside the rectangle</returns>
    /// <remarks>
    /// (1) This can be used to clip a rectangle to an image.
    ///     The clipping rectangle is assumed to have a UL corner at (0, 0),
    ///     and a LR corner at (wi - 1, hi - 1).
    /// </remarks>
    public Box ClipToRectangle(int wi, int hi)
    {
        return ClipToRectangleOrNull(wi, hi)
            ?? throw Lept.Arg("boxClipToRectangle", "box outside rectangle");
    }

    /* The C code returns NULL after logging an error; used where the caller reports it itself. */
    internal Box? ClipToRectangleOrNull(int wi, int hi)
    {
        if (X >= wi || Y >= hi ||
            X + Width <= 0 || Y + Height <= 0)
            return null;

        Box boxd = Copy();
        if (boxd.X < 0) {
            boxd.Width += boxd.X;
            boxd.X = 0;
        }
        if (boxd.Y < 0) {
            boxd.Height += boxd.Y;
            boxd.Y = 0;
        }
        if (boxd.X + boxd.Width > wi)
            boxd.Width = wi - boxd.X;
        if (boxd.Y + boxd.Height > hi)
            boxd.Height = hi - boxd.Y;
        return boxd;
    }

    // leptonica: boxClipToRectangleParams (boxfunc1.c)
    /// <summary>Gets the loop limits for the pixels of a box clipped to a rectangle.</summary>
    /// <param name="box">[optional] requested box; can be null</param>
    /// <param name="w">clipping box size; typ. the size of an image</param>
    /// <param name="h">clipping box size; typ. the size of an image</param>
    /// <param name="xstart">start x coordinate</param>
    /// <param name="ystart">start y coordinate</param>
    /// <param name="xend">one pixel beyond clipping box</param>
    /// <param name="yend">one pixel beyond clipping box</param>
    /// <param name="bw">clipped width</param>
    /// <param name="bh">clipped height</param>
    /// <remarks>
    /// (1) This throws if the box is outside the rectangle or the clipped
    ///     box has no area.  With a null %box, the whole rectangle is returned.
    /// (2) This simplifies the selection of pixel locations within
    ///     a given rectangle:
    ///        for (i = ystart; i &lt; yend; i++ {
    ///            ...
    ///            for (j = xstart; j &lt; xend; j++ {
    ///                ....
    /// </remarks>
    public static void ClipToRectangleParams(Box? box, int w, int h, out int xstart,
                                             out int ystart, out int xend, out int yend,
                                             out int bw, out int bh)
    {
        const string func = "boxClipToRectangleParams";

        xstart = 0;
        ystart = 0;
        xend = w;
        yend = h;
        bw = w;
        bh = h;
        if (box == null) return;

        Box boxc = box.ClipToRectangleOrNull(w, h)
            ?? throw Lept.Arg(func, "box outside image");
        boxc.GetGeometry(out xstart, out ystart, out bw, out bh);

        if (bw == 0 || bh == 0)
            throw Lept.Arg(func, "invalid clipping box");
        xend = xstart + bw;  /* 1 past the end */
        yend = ystart + bh;  /* 1 past the end */
    }

    // leptonica: boxRelocateOneSide (boxfunc1.c)
    /// <summary>Moves one side of a box to a new location.</summary>
    /// <param name="boxd">[optional]; this can be null, equal to boxs,
    ///                    or different from boxs</param>
    /// <param name="boxs">starting box; to have one side relocated</param>
    /// <param name="loc">new location of the side that is changing</param>
    /// <param name="sideflag">L_FROM_LEFT, etc., indicating the side that moves</param>
    /// <returns>boxd</returns>
    /// <remarks>
    /// (1) Set boxd == null to get new box; boxd == boxs for in-place;
    ///     or otherwise to resize existing boxd.
    /// (2) For usage, suggest one of these:
    ///          boxd = Box.RelocateOneSide(null, boxs, ...);   // new
    ///          Box.RelocateOneSide(boxs, boxs, ...);          // in-place
    ///          Box.RelocateOneSide(boxd, boxs, ...);          // other
    /// </remarks>
    public static Box RelocateOneSide(Box? boxd, Box boxs, int loc, int sideflag)
    {
        if (boxd == null)
            boxd = boxs.Copy();

        boxs.GetGeometry(out int x, out int y, out int w, out int h);
        if (w == 0 || h == 0)
            return boxd;
        if (sideflag == L.L_FROM_LEFT)
            boxd.SetGeometry(loc, -1, w + x - loc, -1);
        else if (sideflag == L.L_FROM_RIGHT)
            boxd.SetGeometry(-1, -1, loc - x + 1, -1);
        else if (sideflag == L.L_FROM_TOP)
            boxd.SetGeometry(-1, loc, -1, h + y - loc);
        else if (sideflag == L.L_FROM_BOT)
            boxd.SetGeometry(-1, -1, -1, loc - y + 1);
        return boxd;
    }
}

public sealed partial class Boxa
{
    // leptonica: boxaAdjustSides (boxfunc1.c)
    /// <summary>Moves the sides of each box by the given amounts.</summary>
    /// <param name="delleft">changes in location of each side for each box</param>
    /// <param name="delright">changes in location of each side for each box</param>
    /// <param name="deltop">changes in location of each side for each box</param>
    /// <param name="delbot">changes in location of each side for each box</param>
    /// <returns>boxad</returns>
    /// <remarks>
    /// (1) New box dimensions are cropped at left and top to x &gt;= 0 and y &gt;= 0.
    /// (2) If the width or height of a box goes to 0, we generate a box with
    ///     w == 1 and h == 1, as a placeholder.
    /// (3) See boxAdjustSides().
    /// </remarks>
    public Boxa AdjustSides(int delleft, int delright, int deltop, int delbot)
    {
        int n = Count;
        Boxa boxad = Create(n);
        for (int i = 0; i < n; i++) {
            Box box1 = GetBox(i, L.L_COPY);
            Box? box2 = Box.AdjustSidesOrNull(null, box1, delleft, delright, deltop, delbot);
            if (box2 == null) {
                Log.Error("boxAdjustSides", "boxd has 0 area");
                box1.GetGeometry(out int x, out int y, out _, out _);
                box2 = Box.Create(x, y, 1, 1);
            }
            boxad.AddBox(box2, L.L_INSERT);
        }

        return boxad;
    }

    // leptonica: boxaAdjustBoxSides (boxfunc1.c)
    /// <summary>Moves the sides of one box in the boxa, in place.</summary>
    /// <param name="index">of the box to adjust</param>
    /// <param name="delleft">changes to box side locs</param>
    /// <param name="delright">changes to box side locs</param>
    /// <param name="deltop">changes to box side locs</param>
    /// <param name="delbot">changes to box side locs</param>
    /// <remarks>
    /// (1) In-place operation on a box in a boxa.
    /// (2) New box dimensions are cropped at left and top to x &gt;= 0 and y &gt;= 0.
    /// (3) If a box ends up with no area, an error message is emitted,
    ///     but the box dimensions are not changed.
    /// (4) See boxaAdjustSides().
    /// </remarks>
    public void AdjustBoxSides(int index, int delleft, int delright, int deltop, int delbot)
    {
        if (index < 0 || index >= Count)
            throw Lept.Arg("boxaAdjustBoxSides", "invalid index");
        Box box = GetBox(index, L.L_CLONE);

        if (Box.AdjustSidesOrNull(box, box, delleft, delright, deltop, delbot) == null)
            Log.Error("boxAdjustSides", "boxd has 0 area");
    }
}

public sealed partial class Box
{
    // leptonica: boxAdjustSides (boxfunc1.c)
    /// <summary>Moves the sides of a box by the given amounts.</summary>
    /// <param name="boxd">[optional]; this can be null, equal to boxs,
    ///                    or different from boxs</param>
    /// <param name="boxs">starting box; to have sides adjusted</param>
    /// <param name="delleft">changes in location of each side</param>
    /// <param name="delright">changes in location of each side</param>
    /// <param name="deltop">changes in location of each side</param>
    /// <param name="delbot">changes in location of each side</param>
    /// <returns>boxd; throws if the computed boxd has width or height &lt;= 0</returns>
    /// <remarks>
    /// (1) Set boxd == null to get new box; boxd == boxs for in-place;
    ///     or otherwise to resize existing boxd.
    /// (2) For usage, suggest one of these:
    ///          boxd = Box.AdjustSides(null, boxs, ...);   // new
    ///          Box.AdjustSides(boxs, boxs, ...);          // in-place
    ///          Box.AdjustSides(boxd, boxs, ...);          // other
    /// (3) New box dimensions are cropped at left and top to x &gt;= 0 and y &gt;= 0.
    /// (4) For example, to expand in-place by 20 pixels on each side, use
    ///        Box.AdjustSides(box, box, -20, 20, -20, 20);
    /// </remarks>
    public static Box AdjustSides(Box? boxd, Box boxs, int delleft, int delright,
                                  int deltop, int delbot)
    {
        return AdjustSidesOrNull(boxd, boxs, delleft, delright, deltop, delbot)
            ?? throw Lept.Arg("boxAdjustSides", "boxd has 0 area");
    }

    /* The C code returns NULL for a box with no area; the callers that go on report it themselves. */
    internal static Box? AdjustSidesOrNull(Box? boxd, Box boxs, int delleft, int delright,
                                           int deltop, int delbot)
    {
        boxs.GetGeometry(out int x, out int y, out int w, out int h);
        int xl = Math.Max(0, x + delleft);
        int yt = Math.Max(0, y + deltop);
        int xr = x + w + delright;  /* one pixel beyond right edge */
        int yb = y + h + delbot;    /* one pixel below bottom edge */
        int wnew = xr - xl;
        int hnew = yb - yt;

        if (wnew < 1 || hnew < 1)
            return null;
        if (boxd == null)
            return Create(xl, yt, wnew, hnew);

        boxd.SetGeometry(xl, yt, wnew, hnew);
        return boxd;
    }
}

public sealed partial class Boxa
{
    // leptonica: boxaSetSide (boxfunc1.c)
    /// <summary>Sets the given side of each box.</summary>
    /// <param name="boxad">use null to get a new one; same as boxas for in-place</param>
    /// <param name="boxas">source boxa</param>
    /// <param name="side">L_SET_LEFT, L_SET_RIGHT, L_SET_TOP, L_SET_BOT</param>
    /// <param name="val">location to set for given side, for each box</param>
    /// <param name="thresh">min abs difference to cause resetting to %val</param>
    /// <returns>boxad</returns>
    /// <remarks>
    /// (1) Sets the given side of each box.  Use boxad == null for a new
    ///     boxa, and boxad == boxas for in-place.
    /// (2) Use one of these:
    ///          boxad = Boxa.SetSide(null, boxas, ...);   // new
    ///          Boxa.SetSide(boxas, boxas, ...);  // in-place
    /// </remarks>
    public static Boxa SetSide(Boxa? boxad, Boxa boxas, int side, int val, int thresh)
    {
        const string func = "boxaSetSide";

        if (boxad != null && !ReferenceEquals(boxas, boxad))
            throw Lept.Arg(func, "not in-place");
        if (side != L.L_SET_LEFT && side != L.L_SET_RIGHT &&
            side != L.L_SET_TOP && side != L.L_SET_BOT)
            throw Lept.Arg(func, "invalid side");
        if (val < 0)
            throw Lept.Arg(func, "val < 0");

        if (boxad == null)
            boxad = boxas.Copy(L.L_COPY);
        int n = boxad.Count;
        for (int i = 0; i < n; i++) {
            Box box = boxad.GetBox(i, L.L_CLONE);
            box.SetSide(side, val, thresh);
        }

        return boxad;
    }
}

public sealed partial class Box
{
    // leptonica: boxSetSide (boxfunc1.c)
    /// <summary>Sets the given side of the box, in place.</summary>
    /// <param name="side">L_SET_LEFT, L_SET_RIGHT, L_SET_TOP, L_SET_BOT</param>
    /// <param name="val">location to set for given side, for each box</param>
    /// <param name="thresh">min abs difference to cause resetting to %val</param>
    /// <remarks>
    /// (1) In-place operation.
    /// (2) Use %thresh = 0 to definitely set the side to %val.
    /// </remarks>
    public void SetSide(int side, int val, int thresh)
    {
        const string func = "boxSetSide";

        if (side != L.L_SET_LEFT && side != L.L_SET_RIGHT &&
            side != L.L_SET_TOP && side != L.L_SET_BOT)
            throw Lept.Arg(func, "invalid side");
        if (val < 0)
            throw Lept.Arg(func, "val < 0");

        Box boxs = this;
        boxs.GetGeometry(out int x, out int y, out int w, out int h);
        int diff;
        if (side == L.L_SET_LEFT) {
            diff = x - val;
            if (LAbs(diff) >= thresh)
                boxs.SetGeometry(val, y, w + diff, h);
        } else if (side == L.L_SET_RIGHT) {
            diff = x + w - 1 - val;
            if (LAbs(diff) >= thresh)
                boxs.SetGeometry(x, y, val - x + 1, h);
        } else if (side == L.L_SET_TOP) {
            diff = y - val;
            if (LAbs(diff) >= thresh)
                boxs.SetGeometry(x, val, w, h + diff);
        } else { /* side == L_SET_BOT */
            diff = y + h - 1 - val;
            if (LAbs(diff) >= thresh)
                boxs.SetGeometry(x, y, w, val - y + 1);
        }
    }
}

public sealed partial class Boxa
{
    // leptonica: boxaAdjustWidthToTarget (boxfunc1.c)
    /// <summary>Conditionally adjusts the width of each box to a target width.</summary>
    /// <param name="boxad">use null to get a new one; same as boxas for in-place</param>
    /// <param name="boxas">source boxa</param>
    /// <param name="sides">L_ADJUST_LEFT, L_ADJUST_RIGHT, L_ADJUST_LEFT_AND_RIGHT</param>
    /// <param name="target">target width if differs by more than thresh</param>
    /// <param name="thresh">min abs difference in width to cause adjustment</param>
    /// <returns>boxad</returns>
    /// <remarks>
    /// (1) Conditionally adjusts the width of each box, by moving
    ///     the indicated edges (left and/or right) if the width differs
    ///     by %thresh or more from %target.
    /// (2) Use boxad == null for a new boxa, and boxad == boxas for in-place.
    ///     Use one of these:
    ///          boxad = Boxa.AdjustWidthToTarget(null, boxas, ...);   // new
    ///          Boxa.AdjustWidthToTarget(boxas, boxas, ...);  // in-place
    /// </remarks>
    public static Boxa AdjustWidthToTarget(Boxa? boxad, Boxa boxas, int sides, int target,
                                           int thresh)
    {
        const string func = "boxaAdjustWidthToTarget";

        if (boxad != null && !ReferenceEquals(boxas, boxad))
            throw Lept.Arg(func, "not in-place");
        if (sides != L.L_ADJUST_LEFT && sides != L.L_ADJUST_RIGHT &&
            sides != L.L_ADJUST_LEFT_AND_RIGHT)
            throw Lept.Arg(func, "invalid sides");
        if (target < 1)
            throw Lept.Arg(func, "target < 1");

        if (boxad == null)
            boxad = boxas.Copy(L.L_COPY);
        int n = boxad.Count;
        for (int i = 0; i < n; i++) {
            Box? box = boxad.GetValidBox(i, L.L_CLONE);
            if (box == null)
                continue;
            box.GetGeometry(out int x, out int y, out int w, out int h);
            int diff = w - target;
            if (sides == L.L_ADJUST_LEFT) {
                if (Box.LAbs(diff) >= thresh)
                    box.SetGeometry(Math.Max(0, x + diff), y, target, h);
            } else if (sides == L.L_ADJUST_RIGHT) {
                if (Box.LAbs(diff) >= thresh)
                    box.SetGeometry(x, y, target, h);
            } else { /* sides == L_ADJUST_LEFT_AND_RIGHT */
                if (Box.LAbs(diff) >= thresh)
                    box.SetGeometry(Math.Max(0, x + diff / 2), y, target, h);
            }
        }

        return boxad;
    }

    // leptonica: boxaAdjustHeightToTarget (boxfunc1.c)
    /// <summary>Conditionally adjusts the height of each box to a target height.</summary>
    /// <param name="boxad">use null to get a new one; same as boxas for in-place</param>
    /// <param name="boxas">source boxa</param>
    /// <param name="sides">L_ADJUST_TOP, L_ADJUST_BOT, L_ADJUST_TOP_AND_BOT</param>
    /// <param name="target">target height if differs by more than thresh</param>
    /// <param name="thresh">min abs difference in height to cause adjustment</param>
    /// <returns>boxad</returns>
    /// <remarks>
    /// (1) Conditionally adjusts the height of each box, by moving
    ///     the indicated edges (top and/or bot) if the height differs
    ///     by %thresh or more from %target.
    /// (2) Use boxad == null for a new boxa, and boxad == boxas for in-place.
    ///     Use one of these:
    ///          boxad = Boxa.AdjustHeightToTarget(null, boxas, ...);   // new
    ///          Boxa.AdjustHeightToTarget(boxas, boxas, ...);  // in-place
    /// </remarks>
    public static Boxa AdjustHeightToTarget(Boxa? boxad, Boxa boxas, int sides, int target,
                                            int thresh)
    {
        const string func = "boxaAdjustHeightToTarget";

        if (boxad != null && !ReferenceEquals(boxas, boxad))
            throw Lept.Arg(func, "not in-place");
        if (sides != L.L_ADJUST_TOP && sides != L.L_ADJUST_BOT &&
            sides != L.L_ADJUST_TOP_AND_BOT)
            throw Lept.Arg(func, "invalid sides");
        if (target < 1)
            throw Lept.Arg(func, "target < 1");

        if (boxad == null)
            boxad = boxas.Copy(L.L_COPY);
        int n = boxad.Count;
        for (int i = 0; i < n; i++) {
            Box? box = boxad.GetValidBox(i, L.L_CLONE);
            if (box == null)
                continue;
            box.GetGeometry(out int x, out int y, out int w, out int h);
            int diff = h - target;
            if (sides == L.L_ADJUST_TOP) {
                if (Box.LAbs(diff) >= thresh)
                    box.SetGeometry(x, Math.Max(0, y + diff), w, target);
            } else if (sides == L.L_ADJUST_BOT) {
                if (Box.LAbs(diff) >= thresh)
                    box.SetGeometry(x, y, w, target);
            } else { /* sides == L_ADJUST_TOP_AND_BOT */
                if (Box.LAbs(diff) >= thresh)
                    box.SetGeometry(x, Math.Max(0, y + diff / 2), w, target);
            }
        }

        return boxad;
    }
}

public sealed partial class Box
{
    // leptonica: boxEqual (boxfunc1.c)
    /// <summary>Tests if two boxes have the same geometry.</summary>
    /// <param name="box2">the other box</param>
    /// <returns>true if equal; false otherwise</returns>
    public bool Equal(Box box2)
    {
        Box box1 = this;
        if (box1.X == box2.X && box1.Y == box2.Y &&
            box1.Width == box2.Width && box1.Height == box2.Height)
            return true;
        return false;
    }
}

public sealed partial class Boxa
{
    // leptonica: boxaEqual (boxfunc1.c)
    /// <summary>Tests if two boxa contain the same boxes, within a maximum displacement.</summary>
    /// <param name="boxa2">the other boxa</param>
    /// <param name="maxdist">the maximum difference in position in the two boxa</param>
    /// <param name="naindex">[optional] index array of correspondences; set only if
    ///                       the boxa are equal, otherwise null</param>
    /// <returns>true if equal; false otherwise</returns>
    /// <remarks>
    /// (1) The two boxa are the "same" if they contain the same
    ///     boxes and each box is within %maxdist of its counterpart
    ///     in their positions within the boxa.  This allows for
    ///     small rearrangements.  Use 0 for maxdist if the boxa
    ///     must be identical.
    /// (2) This applies only to geometry and ordering; refcounts
    ///     are not considered.
    /// (3) %maxdist allows some latitude in the ordering of the boxes.
    ///     For the boxa to be the "same", corresponding boxes must
    ///     be within %maxdist of each other.  Note that for large
    ///     %maxdist, we should use a hash function for efficiency.
    /// (4) naindex[i] gives the position of the box in boxa2 that
    ///     corresponds to box i in boxa1.  It is only returned if the
    ///     boxa are equal.
    /// </remarks>
    public bool Equal(Boxa boxa2, int maxdist, out Numa? naindex)
    {
        Boxa boxa1 = this;

        naindex = null;
        int n = boxa1.Count;
        if (n != boxa2.Count)
            return false;

        int[] countarray = new int[n];
        Numa na = Numa.MakeConstant(0.0f, n);

        for (int i = 0; i < n; i++) {
            Box box1 = boxa1.GetBox(i, L.L_CLONE);
            int jstart = Math.Max(0, i - maxdist);
            int jend = Math.Min(n - 1, i + maxdist);
            bool found = false;
            for (int j = jstart; j <= jend; j++) {
                Box box2 = boxa2.GetBox(j, L.L_CLONE);
                bool samebox = box1.Equal(box2);
                if (samebox && countarray[j] == 0) {
                    countarray[j] = 1;
                    na.ReplaceNumber(i, j);
                    found = true;
                    break;
                }
            }
            if (!found)
                return false;
        }

        naindex = na;
        return true;
    }
}

public sealed partial class Box
{
    // leptonica: boxSimilar (boxfunc1.c)
    /// <summary>Tests if the sides of two boxes are within the given distances.</summary>
    /// <param name="box2">the other box</param>
    /// <param name="leftdiff">maximum allowed deviation of the left sides</param>
    /// <param name="rightdiff">maximum allowed deviation of the right sides</param>
    /// <param name="topdiff">maximum allowed deviation of the top sides</param>
    /// <param name="botdiff">maximum allowed deviation of the bottom sides</param>
    /// <returns>true if similar; false otherwise</returns>
    /// <remarks>
    /// (1) The values of leftdiff (etc) are the maximum allowed deviations
    ///     between the locations of the left (etc) sides.  If any side
    ///     pairs differ by more than this amount, the boxes are not similar.
    /// </remarks>
    public bool Similar(Box box2, int leftdiff, int rightdiff, int topdiff, int botdiff)
    {
        bool valid1 = IsValid();
        bool valid2 = box2.IsValid();
        if (!valid1 || !valid2)
            throw Lept.Arg("boxSimilar", "boxes not both valid");

        return SimilarValid(box2, leftdiff, rightdiff, topdiff, botdiff);
    }

    /* The comparison of boxSimilar() once both boxes are known to be valid. */
    private bool SimilarValid(Box box2, int leftdiff, int rightdiff, int topdiff, int botdiff)
    {
        GetSideLocations(out int l1, out int r1, out int t1, out int b1);
        box2.GetSideLocations(out int l2, out int r2, out int t2, out int b2);
        if (LAbs(l1 - l2) > leftdiff)
            return false;
        if (LAbs(r1 - r2) > rightdiff)
            return false;
        if (LAbs(t1 - t2) > topdiff)
            return false;
        if (LAbs(b1 - b2) > botdiff)
            return false;

        return true;
    }

    /* boxSimilar() as called by boxaSimilar(), which goes on after the error return. */
    internal bool SimilarOrFalse(Box box2, int leftdiff, int rightdiff, int topdiff, int botdiff)
    {
        if (!IsValid() || !box2.IsValid()) {
            Log.Error("boxSimilar", "boxes not both valid");
            return false;
        }
        return SimilarValid(box2, leftdiff, rightdiff, topdiff, botdiff);
    }
}

public sealed partial class Boxa
{
    // leptonica: boxaSimilar (boxfunc1.c)
    /// <summary>Tests if corresponding boxes of two boxa are similar.</summary>
    /// <param name="boxa2">the other boxa</param>
    /// <param name="leftdiff">see <see cref="Box.Similar"/></param>
    /// <param name="rightdiff">see <see cref="Box.Similar"/></param>
    /// <param name="topdiff">see <see cref="Box.Similar"/></param>
    /// <param name="botdiff">see <see cref="Box.Similar"/></param>
    /// <param name="debug">output details of non-similar boxes</param>
    /// <returns>true if similar; false otherwise</returns>
    /// <remarks>
    /// (1) See boxSimilar() for parameter usage.
    /// (2) Corresponding boxes are taken in order in the two boxa.
    /// (3) %nasim is an indicator array with a (0/1) for each box pair.
    /// (4) With %nasim or debug == 1, boxes continue to be tested
    ///     after failure.
    /// (5) This overload corresponds to the C call with pnasim == NULL: it stops at the
    ///     first mismatch unless debug is set.  Throws if the boxa counts differ.
    /// </remarks>
    public bool Similar(Boxa boxa2, int leftdiff, int rightdiff, int topdiff, int botdiff,
                        int debug)
    {
        return SimilarImpl(boxa2, leftdiff, rightdiff, topdiff, botdiff, debug, false, out _);
    }

    /// <summary>Tests if corresponding boxes of two boxa are similar, with an indicator array.</summary>
    /// <param name="boxa2">the other boxa</param>
    /// <param name="leftdiff">see <see cref="Box.Similar"/></param>
    /// <param name="rightdiff">see <see cref="Box.Similar"/></param>
    /// <param name="topdiff">see <see cref="Box.Similar"/></param>
    /// <param name="botdiff">see <see cref="Box.Similar"/></param>
    /// <param name="debug">output details of non-similar boxes</param>
    /// <param name="nasim">na containing 1 if similar; else 0</param>
    /// <returns>true if similar; false otherwise</returns>
    /// <remarks>See <see cref="Similar(Boxa, int, int, int, int, int)"/>.</remarks>
    public bool Similar(Boxa boxa2, int leftdiff, int rightdiff, int topdiff, int botdiff,
                        int debug, out Numa nasim)
    {
        bool similar = SimilarImpl(boxa2, leftdiff, rightdiff, topdiff, botdiff, debug, true,
                                   out Numa? na);
        nasim = na!;
        return similar;
    }

    private bool SimilarImpl(Boxa boxa2, int leftdiff, int rightdiff, int topdiff, int botdiff,
                             int debug, bool wantNasim, out Numa? nasim)
    {
        const string func = "boxaSimilar";
        Boxa boxa1 = this;

        nasim = null;
        int n1 = boxa1.Count;
        int n2 = boxa2.Count;
        if (n1 != n2)
            throw Lept.Arg(func, $"boxa counts differ: {n1} vs {n2}");
        if (wantNasim) nasim = Numa.Create(n1);

        bool mismatch = false;
        for (int i = 0; i < n1; i++) {
            Box box1 = boxa1.GetBox(i, L.L_CLONE);
            Box box2 = boxa2.GetBox(i, L.L_CLONE);
            bool match = box1.SimilarOrFalse(box2, leftdiff, rightdiff, topdiff, botdiff);
            if (nasim != null)
                nasim.AddNumber(match ? 1 : 0);
            if (!match) {
                mismatch = true;
                if (debug == 0 && nasim == null)
                    return false;
                else if (debug != 0)
                    Log.Info(func, $"box {i} not similar\n");
            }
        }

        return !mismatch;
    }

    /*----------------------------------------------------------------------*
     *                      Boxa combine and split                          *
     *----------------------------------------------------------------------*/
    // leptonica: boxaJoin (boxfunc1.c)
    /// <summary>Appends clones of the indicated boxes of a source boxa to this boxa.</summary>
    /// <param name="boxas">[optional] source boxa; add from this one</param>
    /// <param name="istart">starting index in boxas</param>
    /// <param name="iend">ending index in boxas; use -1 to cat all</param>
    /// <remarks>
    /// (1) This appends a clone of each indicated box in boxas to boxad
    /// (2) istart &lt; 0 is taken to mean 'read from the start' (istart = 0)
    /// (3) iend &lt; 0 means 'read to the end'
    /// (4) if boxas == null or has no boxes, this is a no-op.
    /// </remarks>
    public void Join(Boxa? boxas, int istart, int iend)
    {
        Boxa boxad = this;
        int n;
        if (boxas == null || ((n = boxas.Count) == 0))
            return;

        if (istart < 0)
            istart = 0;
        if (iend < 0 || iend >= n)
            iend = n - 1;
        if (istart > iend)
            throw Lept.Arg("boxaJoin", "istart > iend; nothing to add");

        for (int i = istart; i <= iend; i++) {
            Box box = boxas.GetBox(i, L.L_CLONE);
            boxad.AddBox(box, L.L_INSERT);
        }
    }
}

public sealed partial class Boxaa
{
    // leptonica: boxaaJoin (boxfunc1.c)
    /// <summary>Appends clones of the indicated boxa of a source boxaa to this boxaa.</summary>
    /// <param name="baas">[optional] source boxaa; add from this one</param>
    /// <param name="istart">starting index in baas</param>
    /// <param name="iend">ending index in baas; use -1 to cat all</param>
    /// <remarks>
    /// (1) This appends a clone of each indicated boxa in baas to baad
    /// (2) istart &lt; 0 is taken to mean 'read from the start' (istart = 0)
    /// (3) iend &lt; 0 means 'read to the end'
    /// (4) if baas == null, this is a no-op.
    /// </remarks>
    public void Join(Boxaa? baas, int istart, int iend)
    {
        Boxaa baad = this;
        if (baas == null)
            return;

        if (istart < 0)
            istart = 0;
        int n = baas.Count;
        if (iend < 0 || iend >= n)
            iend = n - 1;
        if (istart > iend)
            throw Lept.Arg("boxaaJoin", "istart > iend; nothing to add");

        for (int i = istart; i <= iend; i++) {
            Boxa boxa = baas.GetBoxa(i, L.L_CLONE);
            baad.AddBoxa(boxa, L.L_INSERT);
        }
    }
}

public sealed partial class Boxa
{
    // leptonica: boxaSplitEvenOdd (boxfunc1.c)
    /// <summary>Splits the boxa into the boxes at even and at odd positions.</summary>
    /// <param name="fillflag">1 to put invalid boxes in place; 0 to omit</param>
    /// <param name="boxae">save even boxes, setting the odd ones to invalid boxes</param>
    /// <param name="boxao">save odd boxes, setting the even ones to invalid boxes</param>
    /// <remarks>
    /// (1) If %fillflag == 1, boxae has copies of the even boxes
    ///     in their original location, and nvalid boxes are placed
    ///     in the odd array locations.  And v.v.
    /// (2) If %fillflag == 0, boxae has only copies of the even boxes.
    /// </remarks>
    public void SplitEvenOdd(int fillflag, out Boxa boxae, out Boxa boxao)
    {
        int n = Count;
        boxae = Create(n);
        boxao = Create(n);
        if (fillflag == 0) {
                /* don't fill with invalid boxes; end up with half-size boxa */
            for (int i = 0; i < n; i++) {
                Box box = GetBox(i, L.L_COPY);
                if ((i & 1) == 0)
                    boxae.AddBox(box, L.L_INSERT);
                else
                    boxao.AddBox(box, L.L_INSERT);
            }
        } else {
            for (int i = 0; i < n; i++) {
                Box box = GetBox(i, L.L_COPY);
                Box box1 = Box.Create(0, 0, 0, 0);  /* empty placeholder */
                if ((i & 1) == 0) {
                    boxae.AddBox(box, L.L_INSERT);
                    boxao.AddBox(box1, L.L_INSERT);
                } else {
                    boxae.AddBox(box1, L.L_INSERT);
                    boxao.AddBox(box, L.L_INSERT);
                }
            }
        }
    }

    // leptonica: boxaMergeEvenOdd (boxfunc1.c)
    /// <summary>Merges the boxes at even and at odd positions into one boxa.</summary>
    /// <param name="boxao">boxes to go in odd positions in merged boxa</param>
    /// <param name="fillflag">1 if there are invalid boxes in placeholders</param>
    /// <returns>boxad merged; this boxa holds the boxes to go in even positions</returns>
    /// <remarks>
    /// (1) This is essentially the inverse of boxaSplitEvenOdd().
    ///     Typically, boxae and boxao were generated by boxaSplitEvenOdd(),
    ///     and the value of %fillflag needs to be the same in both calls.
    /// (2) If %fillflag == 1, both boxae and boxao are of the same size;
    ///     otherwise boxae may have one more box than boxao.
    /// </remarks>
    public Boxa MergeEvenOdd(Boxa boxao, int fillflag)
    {
        Boxa boxae = this;
        int ne = boxae.Count;
        int no = boxao.Count;
        if (ne < no || ne > no + 1)
            throw Lept.Arg("boxaMergeEvenOdd", "boxa sizes invalid");

        Boxa boxad = Create(ne);
        if (fillflag == 0) {  /* both are approx. half-sized; all valid boxes */
            int n = ne + no;
            for (int i = 0; i < n; i++) {
                Box box;
                if ((i & 1) == 0)
                    box = boxae.GetBox(i / 2, L.L_COPY);
                else
                    box = boxao.GetBox(i / 2, L.L_COPY);
                boxad.AddBox(box, L.L_INSERT);
            }
        } else {  /* both are full size and have invalid placeholders */
            for (int i = 0; i < ne; i++) {
                Box box;
                if ((i & 1) == 0)
                    box = boxae.GetBox(i, L.L_COPY);
                else
                    box = boxao.GetBox(i, L.L_COPY);
                boxad.AddBox(box, L.L_INSERT);
            }
        }
        return boxad;
    }
}
