using System;

namespace Leptonica;

/*
 *  boxfunc2.c: Box and Boxa transforms, orthogonal rotation, sorting,
 *  statistics, array extraction; other Boxaa functions.
 *
 *  Differences from C:
 *    - No reference counting and no destroy functions: the GC owns the objects.
 *    - Optional output pointers become out parameters that are always
 *      computed.  Consequently the "no output requested" / "no ptrs defined"
 *      errors of boxaExtractAsNuma(), boxaExtractAsPta() and boxaaGetExtent()
 *      cannot occur.
 *    - boxaaAlignBox() returns the index (its only output); boxaGetAverageSize()
 *      and boxaaGetExtent() return their results through out parameters.
 *    - Calls that fail return an error code in C (ERROR_INT / ERROR_PTR); here
 *      they throw.  In particular, where C logs an error for an invalid index
 *      in numaGetIValue() and carries on with index 0 (boxaSortByIndex(),
 *      boxaSort2d(), boxaSort2dByIndex()), the port throws.
 *    - The static constant MinCompsForBinSort is a private const.
 *
 *  Stubs created for callees in files that are not ported yet:
 *    numaGetSortIndex, numaGetBinSortIndex, numaSortByIndex, numaGetRankValue,
 *    numaJoin (numafunc1.c).
 *    boxaGetExtent (boxfunc4.c) is a stub from an earlier port.
 */

/// <summary>Constants from pix.h used by the box transforms and extraction functions.</summary>
public static partial class L
{
    /* Order of transforms (L_TR_SC_RO, ...) */
    /// <summary>Applies the transforms in the order translate, scale, rotate.</summary>
    public const int L_TR_SC_RO = 1;

    /// <summary>Applies the transforms in the order scale, rotate, translate.</summary>
    public const int L_SC_RO_TR = 2;

    /// <summary>Applies the transforms in the order rotate, translate, scale.</summary>
    public const int L_RO_TR_SC = 3;

    /// <summary>Applies the transforms in the order translate, rotate, scale.</summary>
    public const int L_TR_RO_SC = 4;

    /// <summary>Applies the transforms in the order rotate, scale, translate.</summary>
    public const int L_RO_SC_TR = 5;

    /// <summary>Applies the transforms in the order scale, translate, rotate.</summary>
    public const int L_SC_TR_RO = 6;

    /* Location of a point relative to a box */

    /// <summary>The upper left corner.</summary>
    public const int L_UPPER_LEFT = 1;

    /// <summary>The upper right corner.</summary>
    public const int L_UPPER_RIGHT = 2;

    /// <summary>The lower left corner.</summary>
    public const int L_LOWER_LEFT = 3;

    /// <summary>The lower right corner.</summary>
    public const int L_LOWER_RIGHT = 4;

    /// <summary>The center of the box.</summary>
    public const int L_BOX_CENTER = 5;
}

/*---------------------------------------------------------------------*
 *      Boxa/Box transform (shift, scale) and orthogonal rotation      *
 *---------------------------------------------------------------------*/
public sealed partial class Box
{
    /* C: L_MAX(a, b) = (((a) > (b)) ? (a) : (b)), with a double operand */
    private static double LMax(double a, double b)
    {
        return (a > b) ? a : b;
    }

    /* C: L_ABS(x) for a float operand */
    private static float LAbs(float x)
    {
        return (x < 0) ? -1 * x : x;
    }

    // leptonica: boxTransform (boxfunc2.c)
    /// <summary>Shifts, then scales, a box.</summary>
    /// <param name="shiftx">shift in x</param>
    /// <param name="shifty">shift in y</param>
    /// <param name="scalex">scale factor in x</param>
    /// <param name="scaley">scale factor in y</param>
    /// <returns>boxd</returns>
    /// <remarks>
    /// (1) This is a very simple function that first shifts, then scales.
    /// (2) If the box is invalid, a new invalid box is returned.
    /// (3) The UL corner coordinates must be non-negative, and the
    ///     width and height of valid boxes must be at least 1.
    /// </remarks>
    public Box Transform(int shiftx, int shifty, float scalex, float scaley)
    {
        Box box = this;

        if (box.Width <= 0 || box.Height <= 0)
            return Create(0, 0, 0, 0);
        else
            return Create((int)LMax(0, scalex * (box.X + shiftx) + 0.5),
                          (int)LMax(0, scaley * (box.Y + shifty) + 0.5),
                          (int)LMax(1.0, scalex * box.Width + 0.5),
                          (int)LMax(1.0, scaley * box.Height + 0.5));
    }

    // leptonica: boxTransformOrdered (boxfunc2.c)
    /// <summary>Applies shift, scaling and rotation in a specified order.</summary>
    /// <param name="shiftx">shift in x</param>
    /// <param name="shifty">shift in y</param>
    /// <param name="scalex">scale factor in x</param>
    /// <param name="scaley">scale factor in y</param>
    /// <param name="xcen">x center of rotation</param>
    /// <param name="ycen">y center of rotation</param>
    /// <param name="angle">in radians; clockwise is positive</param>
    /// <param name="order">one of 6 combinations: L_TR_SC_RO, ...</param>
    /// <returns>boxd</returns>
    /// <remarks>
    /// (1) This allows a sequence of linear transforms, composed of
    ///     shift, scaling and rotation, where the order of the
    ///     transforms is specified.
    /// (2) The rotation is taken about a point specified by (xcen, ycen).
    ///     Let the components of the vector from the center of rotation
    ///     to the box center be (xdif, ydif):
    ///       xdif = (bx + 0.5 * bw) - xcen
    ///       ydif = (by + 0.5 * bh) - ycen
    ///     Then the box center after rotation has new components:
    ///       bxcen = xcen + xdif * cosa + ydif * sina
    ///       bycen = ycen + ydif * cosa - xdif * sina
    ///     where cosa and sina are the cos and sin of the angle,
    ///     and the enclosing box for the rotated box has size:
    ///       rw = |bw * cosa| + |bh * sina|
    ///       rh = |bh * cosa| + |bw * sina|
    ///     where bw and bh are the unrotated width and height.
    ///     Then the box UL corner (rx, ry) is
    ///       rx = bxcen - 0.5 * rw
    ///       ry = bycen - 0.5 * rh
    /// (3) The center of rotation specified by args %xcen and %ycen
    ///     is the point BEFORE any translation or scaling.  If the
    ///     rotation is not the first operation, this function finds
    ///     the actual center at the time of rotation.  It does this
    ///     by making the following assumptions:
    ///        (1) Any scaling is with respect to the UL corner, so
    ///            that the center location scales accordingly.
    ///        (2) A translation does not affect the center of
    ///            the image; it just moves the boxes.
    ///     We always use assumption (1).  However, assumption (2)
    ///     will be incorrect if the apparent translation is due
    ///     to a clipping operation that, in effect, moves the
    ///     origin of the image.  In that case, you should NOT use
    ///     these simple functions.  Instead, use the functions
    ///     in affinecompose.c, where the rotation center can be
    ///     computed from the actual clipping due to translation
    ///     of the image origin.
    /// </remarks>
    public Box TransformOrdered(int shiftx, int shifty, float scalex, float scaley,
                                int xcen, int ycen, float angle, int order)
    {
        const string func = "boxTransformOrdered";
        Box boxs = this;
        int tx, ty, tw, th;
        int xcent, ycent;  /* transformed center of rotation due to scaling */
        float sina = 0, cosa = 0, xdif, ydif, rx, ry, rw, rh;
        Box boxd;

        if (order != L.L_TR_SC_RO && order != L.L_SC_RO_TR && order != L.L_RO_TR_SC &&
            order != L.L_TR_RO_SC && order != L.L_RO_SC_TR && order != L.L_SC_TR_RO)
            throw Lept.Arg(func, "order invalid");

        boxs.GetGeometry(out int bx, out int by, out int bw, out int bh);
        if (bw <= 0 || bh <= 0)  /* invalid */
            return Create(0, 0, 0, 0);
        if (angle != 0.0) {
            sina = (float)Math.Sin(angle);
            cosa = (float)Math.Cos(angle);
        }

        if (order == L.L_TR_SC_RO) {
            tx = (int)(scalex * (bx + shiftx) + 0.5);
            ty = (int)(scaley * (by + shifty) + 0.5);
            tw = (int)LMax(1.0, scalex * bw + 0.5);
            th = (int)LMax(1.0, scaley * bh + 0.5);
            xcent = (int)(scalex * xcen + 0.5);
            ycent = (int)(scaley * ycen + 0.5);
            if (angle == 0.0) {
                boxd = Create(tx, ty, tw, th);
            } else {
                xdif = (float)(tx + 0.5 * tw - xcent);
                ydif = (float)(ty + 0.5 * th - ycent);
                rw = LAbs(tw * cosa) + LAbs(th * sina);
                rh = LAbs(th * cosa) + LAbs(tw * sina);
                rx = (float)(xcent + xdif * cosa - ydif * sina - 0.5 * rw);
                ry = (float)(ycent + ydif * cosa + xdif * sina - 0.5 * rh);
                boxd = Create((int)rx, (int)ry, (int)rw, (int)rh);
            }
        } else if (order == L.L_SC_TR_RO) {
            tx = (int)(scalex * bx + shiftx + 0.5);
            ty = (int)(scaley * by + shifty + 0.5);
            tw = (int)LMax(1.0, scalex * bw + 0.5);
            th = (int)LMax(1.0, scaley * bh + 0.5);
            xcent = (int)(scalex * xcen + 0.5);
            ycent = (int)(scaley * ycen + 0.5);
            if (angle == 0.0) {
                boxd = Create(tx, ty, tw, th);
            } else {
                xdif = (float)(tx + 0.5 * tw - xcent);
                ydif = (float)(ty + 0.5 * th - ycent);
                rw = LAbs(tw * cosa) + LAbs(th * sina);
                rh = LAbs(th * cosa) + LAbs(tw * sina);
                rx = (float)(xcent + xdif * cosa - ydif * sina - 0.5 * rw);
                ry = (float)(ycent + ydif * cosa + xdif * sina - 0.5 * rh);
                boxd = Create((int)rx, (int)ry, (int)rw, (int)rh);
            }
        } else if (order == L.L_RO_TR_SC) {
            if (angle == 0.0) {
                rx = bx;
                ry = by;
                rw = bw;
                rh = bh;
            } else {
                xdif = (float)(bx + 0.5 * bw - xcen);
                ydif = (float)(by + 0.5 * bh - ycen);
                rw = LAbs(bw * cosa) + LAbs(bh * sina);
                rh = LAbs(bh * cosa) + LAbs(bw * sina);
                rx = (float)(xcen + xdif * cosa - ydif * sina - 0.5 * rw);
                ry = (float)(ycen + ydif * cosa + xdif * sina - 0.5 * rh);
            }
            tx = (int)(scalex * (rx + shiftx) + 0.5);
            ty = (int)(scaley * (ry + shifty) + 0.5);
            tw = (int)LMax(1.0, scalex * rw + 0.5);
            th = (int)LMax(1.0, scaley * rh + 0.5);
            boxd = Create(tx, ty, tw, th);
        } else if (order == L.L_RO_SC_TR) {
            if (angle == 0.0) {
                rx = bx;
                ry = by;
                rw = bw;
                rh = bh;
            } else {
                xdif = (float)(bx + 0.5 * bw - xcen);
                ydif = (float)(by + 0.5 * bh - ycen);
                rw = LAbs(bw * cosa) + LAbs(bh * sina);
                rh = LAbs(bh * cosa) + LAbs(bw * sina);
                rx = (float)(xcen + xdif * cosa - ydif * sina - 0.5 * rw);
                ry = (float)(ycen + ydif * cosa + xdif * sina - 0.5 * rh);
            }
            tx = (int)(scalex * rx + shiftx + 0.5);
            ty = (int)(scaley * ry + shifty + 0.5);
            tw = (int)LMax(1.0, scalex * rw + 0.5);
            th = (int)LMax(1.0, scaley * rh + 0.5);
            boxd = Create(tx, ty, tw, th);
        } else if (order == L.L_TR_RO_SC) {
            tx = bx + shiftx;
            ty = by + shifty;
            if (angle == 0.0) {
                rx = tx;
                ry = ty;
                rw = bw;
                rh = bh;
            } else {
                xdif = (float)(tx + 0.5 * bw - xcen);
                ydif = (float)(ty + 0.5 * bh - ycen);
                rw = LAbs(bw * cosa) + LAbs(bh * sina);
                rh = LAbs(bh * cosa) + LAbs(bw * sina);
                rx = (float)(xcen + xdif * cosa - ydif * sina - 0.5 * rw);
                ry = (float)(ycen + ydif * cosa + xdif * sina - 0.5 * rh);
            }
            tx = (int)(scalex * rx + 0.5);
            ty = (int)(scaley * ry + 0.5);
            tw = (int)LMax(1.0, scalex * rw + 0.5);
            th = (int)LMax(1.0, scaley * rh + 0.5);
            boxd = Create(tx, ty, tw, th);
        } else {  /* order == L_SC_RO_TR) */
            tx = (int)(scalex * bx + 0.5);
            ty = (int)(scaley * by + 0.5);
            tw = (int)LMax(1.0, scalex * bw + 0.5);
            th = (int)LMax(1.0, scaley * bh + 0.5);
            xcent = (int)(scalex * xcen + 0.5);
            ycent = (int)(scaley * ycen + 0.5);
            if (angle == 0.0) {
                rx = tx;
                ry = ty;
                rw = tw;
                rh = th;
            } else {
                xdif = (float)(tx + 0.5 * tw - xcent);
                ydif = (float)(ty + 0.5 * th - ycent);
                rw = LAbs(tw * cosa) + LAbs(th * sina);
                rh = LAbs(th * cosa) + LAbs(tw * sina);
                rx = (float)(xcent + xdif * cosa - ydif * sina - 0.5 * rw);
                ry = (float)(ycent + ydif * cosa + xdif * sina - 0.5 * rh);
            }
            tx = (int)(rx + shiftx + 0.5);
            ty = (int)(ry + shifty + 0.5);
            tw = (int)(rw + 0.5);
            th = (int)(rh + 0.5);
            boxd = Create(tx, ty, tw, th);
        }

        return boxd;
    }

    // leptonica: boxRotateOrth (boxfunc2.c)
    /// <summary>Rotates a box by a multiple of 90 degrees, clockwise.</summary>
    /// <param name="w">width of the image in which the box is embedded</param>
    /// <param name="h">height of the image in which the box is embedded</param>
    /// <param name="rotation">0 = noop, 1 = 90 deg, 2 = 180 deg, 3 = 270 deg;
    /// all rotations are clockwise</param>
    /// <returns>boxd</returns>
    /// <remarks>
    /// (1) Rotate the image with the embedded box by the specified amount.
    /// (2) After rotation, the rotated box is always measured with
    ///     respect to the UL corner of the image.
    /// </remarks>
    public Box RotateOrth(int w, int h, int rotation)
    {
        Box box = this;

        if (rotation < 0 || rotation > 3)
            throw Lept.Arg("boxRotateOrth", "rotation not in {0,1,2,3}");
        if (rotation == 0)
            return box.Copy();

        box.GetGeometry(out int bx, out int by, out int bw, out int bh);
        if (bw <= 0 || bh <= 0)  /* invalid */
            return Create(0, 0, 0, 0);
        int ydist = h - by - bh;  /* below box */
        int xdist = w - bx - bw;  /* to right of box */
        if (rotation == 1)   /* 90 deg cw */
            return Create(ydist, bx, bh, bw);
        else if (rotation == 2)  /* 180 deg cw */
            return Create(xdist, ydist, bw, bh);
        else  /*  rotation == 3, 270 deg cw */
            return Create(by, xdist, bh, bw);
    }
}

public sealed partial class Boxa
{
    /* For more than this number of c.c. in a binarized image of
     * semi-perimeter (w + h) about 5000 or less, the O(n) binsort
     * is faster than the O(nlogn) shellsort.  */
    private const int MinCompsForBinSort = 200;

    // leptonica: boxaTransform (boxfunc2.c)
    /// <summary>Shifts, then scales, all boxes.</summary>
    /// <param name="shiftx">shift in x</param>
    /// <param name="shifty">shift in y</param>
    /// <param name="scalex">scale factor in x</param>
    /// <param name="scaley">scale factor in y</param>
    /// <returns>boxad</returns>
    /// <remarks>
    /// (1) This is a very simple function that first shifts, then scales.
    /// (2) The UL corner coordinates of all boxes in the output %boxad
    /// (3) For the boxes in the output %boxad, the UL corner coordinates
    ///     must be non-negative, and the width and height of valid
    ///     boxes must be at least 1.
    /// </remarks>
    public Boxa Transform(int shiftx, int shifty, float scalex, float scaley)
    {
        Boxa boxas = this;

        int n = boxas.Count;
        Boxa boxad = Create(n);
        for (int i = 0; i < n; i++) {
            Box boxs = boxas.GetBox(i, L.L_CLONE);
            Box boxd = boxs.Transform(shiftx, shifty, scalex, scaley);
            boxad.AddBox(boxd, L.L_INSERT);
        }

        return boxad;
    }

    // leptonica: boxaTransformOrdered (boxfunc2.c)
    /// <summary>Applies shift, scaling and rotation, in a specified order, to all boxes.</summary>
    /// <param name="shiftx">shift in x</param>
    /// <param name="shifty">shift in y</param>
    /// <param name="scalex">scale factor in x</param>
    /// <param name="scaley">scale factor in y</param>
    /// <param name="xcen">x center of rotation</param>
    /// <param name="ycen">y center of rotation</param>
    /// <param name="angle">in radians; clockwise is positive</param>
    /// <param name="order">one of 6 combinations: L_TR_SC_RO, ...</param>
    /// <returns>boxd</returns>
    /// <remarks>
    ///     shift, scaling and rotation, and the order of the
    ///     transforms is specified.
    /// (2) Although these operations appear to be on an infinite
    ///     2D plane, in practice the region of interest is clipped
    ///     to a finite image.  The center of rotation is usually taken
    ///     with respect to the image (either the UL corner or the
    ///     center).  A translation can have two very different effects:
    ///       (a) Moves the boxes across the fixed image region.
    ///       (b) Moves the image origin, causing a change in the image
    ///           region and an opposite effective translation of the boxes.
    ///     This function should only be used for (a), where the image
    ///     region is fixed on translation.  If the image region is
    ///     changed by the translation, use instead the functions
    ///     in affinecompose.c, where the image region and rotation
    ///     center can be computed from the actual clipping due to
    ///     translation of the image origin.
    /// (3) See boxTransformOrdered() for usage and implementation details.
    /// </remarks>
    public Boxa TransformOrdered(int shiftx, int shifty, float scalex, float scaley,
                                 int xcen, int ycen, float angle, int order)
    {
        Boxa boxas = this;

        int n = boxas.Count;
        Boxa boxad = Create(n);
        for (int i = 0; i < n; i++) {
            Box boxs = boxas.GetBox(i, L.L_CLONE);
            Box boxd = boxs.TransformOrdered(shiftx, shifty, scalex, scaley,
                                             xcen, ycen, angle, order);
            boxad.AddBox(boxd, L.L_INSERT);
        }

        return boxad;
    }

    // leptonica: boxaRotateOrth (boxfunc2.c)
    /// <summary>Rotates all boxes by a multiple of 90 degrees, clockwise.</summary>
    /// <param name="w">width of the image in which the boxa is embedded</param>
    /// <param name="h">height of the image in which the boxa is embedded</param>
    /// <param name="rotation">0 = noop, 1 = 90 deg, 2 = 180 deg, 3 = 270 deg;
    /// all rotations are clockwise</param>
    /// <returns>boxad</returns>
    /// <remarks>
    /// (1) See boxRotateOrth() for details.
    /// </remarks>
    public Boxa RotateOrth(int w, int h, int rotation)
    {
        Boxa boxas = this;

        if (rotation < 0 || rotation > 3)
            throw Lept.Arg("boxaRotateOrth", "rotation not in {0,1,2,3}");
        if (rotation == 0)
            return boxas.Copy(L.L_COPY);

        int n = boxas.Count;
        Boxa boxad = Create(n);
        for (int i = 0; i < n; i++) {
            Box boxs = boxas.GetBox(i, L.L_CLONE);
            Box boxd = boxs.RotateOrth(w, h, rotation);
            boxad.AddBox(boxd, L.L_INSERT);
        }

        return boxad;
    }

    // leptonica: boxaShiftWithPta (boxfunc2.c)
    /// <summary>Shifts the boxes by the points of a pta.</summary>
    /// <param name="pta">aligned with the boxes; determines shift amount</param>
    /// <param name="dir">+1 to shift by the values in pta; -1 to shift
    /// by the negative of the values in the pta.</param>
    /// <returns>boxad</returns>
    /// <remarks>
    /// (1) In use, %pta may come from the UL corners of of a boxa, each
    ///     of whose boxes contains the corresponding box of %boxas
    ///     within it.  The output %boxad is then a boxa in the (global)
    ///     coordinates of the containing boxa.  So the input %pta
    ///     could come from boxaExtractCorners().
    /// (2) The operations with %dir == 1 and %dir == -1 are inverses if
    ///     called in order (1, -1).  Starting with an input boxa and
    ///     calling twice with these values of %dir results in a boxa
    ///     identical to the input.  However, because box parameters can
    ///     never be negative, calling in the order (-1, 1) may result
    ///     in clipping at the left side and the top.
    /// </remarks>
    public Boxa ShiftWithPta(Pta pta, int dir)
    {
        const string func = "boxaShiftWithPta";
        Boxa boxas = this;

        bool full = boxas.IsFull();
        if (!full)
            throw Lept.Arg(func, "boxas not full");
        if (dir != 1 && dir != -1)
            throw Lept.Arg(func, "invalid dir");
        int n = boxas.Count;
        if (n != pta.Count)
            throw Lept.Arg(func, "boxas and pta not same size");

        Boxa boxad = Create(n);
        for (int i = 0; i < n; i++) {
            Box box1 = boxas.GetBox(i, L.L_COPY);
            pta.GetIPt(i, out int x, out int y);
            Box box2 = box1.Transform(dir * x, dir * y, 1.0f, 1.0f);
            boxad.AddBox(box2, L.L_INSERT);
        }
        return boxad;
    }

    /*---------------------------------------------------------------------*
     *                              Boxa sort                              *
     *---------------------------------------------------------------------*/
    // leptonica: boxaSort (boxfunc2.c)
    /// <summary>Sorts the boxes by a box dimension or location.</summary>
    /// <param name="sorttype">L_SORT_BY_X, L_SORT_BY_Y,
    /// L_SORT_BY_RIGHT, L_SORT_BY_BOT,
    /// L_SORT_BY_WIDTH, L_SORT_BY_HEIGHT,
    /// L_SORT_BY_MIN_DIMENSION, L_SORT_BY_MAX_DIMENSION,
    /// L_SORT_BY_PERIMETER, L_SORT_BY_AREA,
    /// L_SORT_BY_ASPECT_RATIO</param>
    /// <param name="sortorder">L_SORT_INCREASING, L_SORT_DECREASING</param>
    /// <param name="naindex">index of sorted order into original array;
    /// null if the boxa is empty</param>
    /// <returns>boxad sorted version of this boxa</returns>
    /// <remarks>
    /// (1) An empty boxa returns a copy, with a warning.
    /// </remarks>
    public Boxa Sort(int sorttype, int sortorder, out Numa? naindex)
    {
        const string func = "boxaSort";
        Boxa boxas = this;

        naindex = null;
        int n;
        if ((n = boxas.Count) == 0) {
            Log.Warning(func, "boxas is empty\n");
            return boxas.Copy(L.L_COPY);
        }
        if (sorttype != L.L_SORT_BY_X && sorttype != L.L_SORT_BY_Y &&
            sorttype != L.L_SORT_BY_RIGHT && sorttype != L.L_SORT_BY_BOT &&
            sorttype != L.L_SORT_BY_WIDTH && sorttype != L.L_SORT_BY_HEIGHT &&
            sorttype != L.L_SORT_BY_MIN_DIMENSION &&
            sorttype != L.L_SORT_BY_MAX_DIMENSION &&
            sorttype != L.L_SORT_BY_PERIMETER &&
            sorttype != L.L_SORT_BY_AREA &&
            sorttype != L.L_SORT_BY_ASPECT_RATIO)
            throw Lept.Arg(func, "invalid sort type");
        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg(func, "invalid sort order");

            /* Use O(n) binsort if possible */
        if (n > MinCompsForBinSort &&
            ((sorttype == L.L_SORT_BY_X) || (sorttype == L.L_SORT_BY_Y) ||
             (sorttype == L.L_SORT_BY_WIDTH) || (sorttype == L.L_SORT_BY_HEIGHT) ||
             (sorttype == L.L_SORT_BY_PERIMETER)))
            return boxas.BinSort(sorttype, sortorder, out naindex);

            /* Build up numa of specific data */
        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            boxas.GetBoxGeometry(i, out int x, out int y, out int w, out int h);
            int size;
            switch (sorttype)
            {
            case L.L_SORT_BY_X:
                na.AddNumber(x);
                break;
            case L.L_SORT_BY_Y:
                na.AddNumber(y);
                break;
            case L.L_SORT_BY_RIGHT:
                na.AddNumber(x + w - 1);
                break;
            case L.L_SORT_BY_BOT:
                na.AddNumber(y + h - 1);
                break;
            case L.L_SORT_BY_WIDTH:
                na.AddNumber(w);
                break;
            case L.L_SORT_BY_HEIGHT:
                na.AddNumber(h);
                break;
            case L.L_SORT_BY_MIN_DIMENSION:
                size = (w < h) ? w : h;
                na.AddNumber(size);
                break;
            case L.L_SORT_BY_MAX_DIMENSION:
                size = (w > h) ? w : h;
                na.AddNumber(size);
                break;
            case L.L_SORT_BY_PERIMETER:
                size = w + h;
                na.AddNumber(size);
                break;
            case L.L_SORT_BY_AREA:
                size = w * h;
                na.AddNumber(size);
                break;
            case L.L_SORT_BY_ASPECT_RATIO:
                na.AddNumber((float)w / (float)h);
                break;
            default:
                Log.Warning(func, "invalid sort type\n");
                break;
            }
        }

            /* Get the sort index for data array */
        Numa naindex1 = na.GetSortIndex(sortorder);

            /* Build up sorted boxa using sort index */
        Boxa boxad = boxas.SortByIndex(naindex1);

        naindex = naindex1;
        return boxad;
    }

    // leptonica: boxaBinSort (boxfunc2.c)
    /// <summary>Sorts the boxes with an O(n) bin sort.</summary>
    /// <param name="sorttype">L_SORT_BY_X, L_SORT_BY_Y, L_SORT_BY_WIDTH,
    /// L_SORT_BY_HEIGHT, L_SORT_BY_PERIMETER</param>
    /// <param name="sortorder">L_SORT_INCREASING, L_SORT_DECREASING</param>
    /// <param name="naindex">index of sorted order into original array;
    /// null if the boxa is empty</param>
    /// <returns>boxad sorted version of this boxa</returns>
    /// <remarks>
    /// (1) For a large number of boxes (say, greater than 1000), this
    ///     O(n) binsort is much faster than the O(nlogn) shellsort.
    ///     For 5000 components, this is over 20x faster than boxaSort().
    /// (2) Consequently, boxaSort() calls this function if it will
    ///     likely go much faster.
    /// </remarks>
    public Boxa BinSort(int sorttype, int sortorder, out Numa? naindex)
    {
        const string func = "boxaBinSort";
        Boxa boxas = this;

        naindex = null;
        int n;
        if ((n = boxas.Count) == 0) {
            Log.Warning(func, "boxas is empty\n");
            return boxas.Copy(L.L_COPY);
        }
        if (sorttype != L.L_SORT_BY_X && sorttype != L.L_SORT_BY_Y &&
            sorttype != L.L_SORT_BY_WIDTH && sorttype != L.L_SORT_BY_HEIGHT &&
            sorttype != L.L_SORT_BY_PERIMETER)
            throw Lept.Arg(func, "invalid sort type");
        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg(func, "invalid sort order");

            /* Generate Numa of appropriate box dimensions */
        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            boxas.GetBoxGeometry(i, out int x, out int y, out int w, out int h);
            switch (sorttype)
            {
            case L.L_SORT_BY_X:
                na.AddNumber(x);
                break;
            case L.L_SORT_BY_Y:
                na.AddNumber(y);
                break;
            case L.L_SORT_BY_WIDTH:
                na.AddNumber(w);
                break;
            case L.L_SORT_BY_HEIGHT:
                na.AddNumber(h);
                break;
            case L.L_SORT_BY_PERIMETER:
                na.AddNumber(w + h);
                break;
            default:
                Log.Warning(func, "invalid sort type\n");
                break;
            }
        }

            /* Get the sort index for data array */
        Numa naindex1 = na.GetBinSortIndex(sortorder);

            /* Build up sorted boxa using the sort index */
        Boxa boxad = boxas.SortByIndex(naindex1);

        naindex = naindex1;
        return boxad;
    }

    // leptonica: boxaSortByIndex (boxfunc2.c)
    /// <summary>Returns the boxes ordered by an index array.</summary>
    /// <param name="naindex">na that maps from the new boxa to the input boxa</param>
    /// <returns>boxad sorted</returns>
    public Boxa SortByIndex(Numa naindex)
    {
        const string func = "boxaSortByIndex";
        Boxa boxas = this;

        int n;
        if ((n = boxas.Count) == 0) {
            Log.Warning(func, "boxas is empty\n");
            return boxas.Copy(L.L_COPY);
        }

        Boxa boxad = Create(n);
        for (int i = 0; i < n; i++) {
            int index = naindex.GetIValue(i);
            Box box = boxas.GetBox(index, L.L_COPY);
            boxad.AddBox(box, L.L_INSERT);
        }

        return boxad;
    }

    // leptonica: boxaSort2d (boxfunc2.c)
    /// <summary>Sorts the boxes into rows (left to right), and the rows from top to bottom.</summary>
    /// <param name="naad">numaa with sorted indices whose values are the
    /// indices of the input array</param>
    /// <param name="delta1">min separation that permits aggregation of a box
    /// onto a boxa of horizontally-aligned boxes; pass 1</param>
    /// <param name="delta2">min separation that permits aggregation of a box
    /// onto a boxa of horizontally-aligned boxes; pass 2</param>
    /// <param name="minh1">components less than this height either join an
    /// existing boxa or are set aside for pass 2</param>
    /// <returns>baa 2d sorted version of this boxa</returns>
    /// <remarks>
    /// (1) The final result is a sort where the 'fast scan' direction is
    ///     left to right, and the 'slow scan' direction is from top
    ///     to bottom.  Each boxa in the baa represents a sorted set
    ///     of boxes from left to right.
    /// (2) Three passes are used to aggregate the boxas, which can correspond
    ///     to characters or words in a line of text.  In pass 1, only
    ///     taller components, which correspond to xheight or larger,
    ///     are permitted to start a new boxa.  In pass 2, the remaining
    ///     vertically-challenged components are allowed to join an
    ///     existing boxa or start a new one.  In pass 3, boxa whose extent
    ///     is overlapping are joined.  After that, the boxes in each
    ///     boxa are sorted horizontally, and finally the boxa are
    ///     sorted vertically.
    /// (3) If %delta1 > 0, the first pass allows aggregation when
    ///     boxes in the same boxa do not overlap vertically.  In fact,
    ///     %delta1 is the max distance by which they can miss and still
    ///     be aggregated.  If %delta1 &lt; 0, the box must have vertical
    ///     overlap of at least abs(%delta1) with the boxa before it
    ///     can be merged.  Similar for delta2 on the second pass.
    /// (4) On the first pass, any component of height less than minh1
    ///     cannot start a new boxa; it's put aside for later insertion.
    /// (5) On the second pass, any small component that doesn't align
    ///     with an existing boxa can start a new one.
    /// (6) This can be used to identify lines of text from
    ///     character or word bounding boxes.
    /// (7) Typical values for the input parameters on 300 ppi text are:
    ///            delta1 ~ 0
    ///            delta2 ~ 0
    ///            minh1 ~ 5
    /// </remarks>
    public Boxaa Sort2d(out Numaa naad, int delta1, int delta2, int minh1)
    {
        const string func = "boxaSort2d";
        Boxa boxas = this;
        int i, index, h, nt, ne, n, m, ival;
        Box box;
        Boxa boxa, boxae, boxan, boxa1, boxa2, boxa3, boxav, boxavs;
        Boxaa baa, baa1, baad;
        Numa nae, nan, na1, na2, nad;
        Numa? nah, nav;
        Numaa naa, naa1;

        if (boxas.Count == 0)
            throw Lept.Arg(func, "boxas is empty");

            /* Sort from left to right */
        boxa = boxas.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out Numa? naindex);

            /* First pass: assign taller boxes to boxa by row */
        nt = boxa.Count;
        baa = Boxaa.Create(0);
        naa = Numaa.Create(0);
        boxae = Create(0);  /* save small height boxes here */
        nae = Numa.Create(0);  /* keep track of small height boxes */
        for (i = 0; i < nt; i++) {
            box = boxa.GetBox(i, L.L_CLONE);
            box.GetGeometry(out _, out _, out _, out h);
            if (h < minh1) {  /* save for 2nd pass */
                boxae.AddBox(box, L.L_INSERT);
                nae.AddNumber(i);
            } else {
                n = baa.Count;
                index = baa.AlignBox(box, delta1);
                if (index < n) {  /* append to an existing boxa */
                    baa.AddBox(index, box, L.L_INSERT);
                } else {  /* doesn't align, need new boxa */
                    boxan = Create(0);
                    boxan.AddBox(box, L.L_INSERT);
                    baa.AddBoxa(boxan, L.L_INSERT);
                    nan = Numa.Create(0);
                    naa.AddNuma(nan, L.L_INSERT);
                }
                ival = naindex!.GetIValue(i);
                naa.AddNumber(index, ival);
            }
        }

            /* Second pass: feed in small height boxes */
        ne = boxae.Count;
        for (i = 0; i < ne; i++) {
            box = boxae.GetBox(i, L.L_CLONE);
            n = baa.Count;
            index = baa.AlignBox(box, delta2);
            if (index < n) {  /* append to an existing boxa */
                baa.AddBox(index, box, L.L_INSERT);
            } else {  /* doesn't align, need new boxa */
                boxan = Create(0);
                boxan.AddBox(box, L.L_INSERT);
                baa.AddBoxa(boxan, L.L_INSERT);
                nan = Numa.Create(0);
                naa.AddNuma(nan, L.L_INSERT);
            }
            ival = nae.GetIValue(i);  /* NOTE(upstream): this is the position in the x-sorted boxa, not in the original boxas */
            naa.AddNumber(index, ival);
        }

            /* Third pass: merge some boxa whose extent is overlapping.
             * Think of these boxa as text lines, where the bounding boxes
             * of the text lines can overlap, but likely won't have
             * a huge overlap.
             * First do a greedy find of pairs of overlapping boxa, where
             * the two boxa overlap by at least 50% of the smaller, and
             * the smaller is not more than half the area of the larger.
             * For such pairs, call the larger one the primary boxa.  The
             * boxes in the smaller one are appended to those in the primary
             * in pass 3a, and the primaries are extracted in pass 3b.
             * In this way, all boxes in the original baa are saved. */
        n = baa.Count;
        baa.GetExtent(out _, out _, out _, out boxa3);
        boxa1 = boxa3.HandleOverlaps(L.L_REMOVE_SMALL, 1000, 0.5f, 0.5f, out Numa? namap);
        for (i = 0; i < n; i++) {  /* Pass 3a: join selected copies of boxa */
            ival = namap!.GetIValue(i);
            if (ival >= 0) {  /* join current to primary boxa[ival] */
                boxa1 = baa.GetBoxa(i, L.L_COPY);
                boxa2 = baa.GetBoxa(ival, L.L_CLONE);
                boxa2.Join(boxa1, 0, -1);
                na1 = naa.GetNuma(i, L.L_COPY);
                na2 = naa.GetNuma(ival, L.L_CLONE);
                na2.Join(na1, 0, -1);
            }
        }
        baa1 = Boxaa.Create(n);
        naa1 = Numaa.Create(n);
        for (i = 0; i < n; i++) {  /* Pass 3b: save primary boxa */
            ival = namap!.GetIValue(i);
            if (ival == -1) {
                boxa1 = baa.GetBoxa(i, L.L_CLONE);
                baa1.AddBoxa(boxa1, L.L_INSERT);
                na1 = naa.GetNuma(i, L.L_CLONE);
                naa1.AddNuma(na1, L.L_INSERT);
            }
        }
        baa = baa1;
        naa = naa1;

            /* Sort the boxes in each boxa horizontally */
        m = baa.Count;
        for (i = 0; i < m; i++) {
            boxa1 = baa.GetBoxa(i, L.L_CLONE);
            boxa2 = boxa1.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out nah);
            baa.ReplaceBoxa(i, boxa2);
            na1 = naa.GetNuma(i, L.L_CLONE);
            na2 = na1.SortByIndex(nah!);
            naa.ReplaceNuma(i, na2);
        }

            /* Sort the boxa vertically within boxaa, using the first box
             * in each boxa. */
        m = baa.Count;
        boxav = Create(m);  /* holds first box in each boxa in baa */
        naad = Numaa.Create(m);
        baad = Boxaa.Create(m);
        for (i = 0; i < m; i++) {
            boxa1 = baa.GetBoxa(i, L.L_CLONE);
            box = boxa1.GetBox(0, L.L_CLONE);
            boxav.AddBox(box, L.L_INSERT);
        }
        boxavs = boxav.Sort(L.L_SORT_BY_Y, L.L_SORT_INCREASING, out nav);
        for (i = 0; i < m; i++) {
            index = nav!.GetIValue(i);
            boxa = baa.GetBoxa(index, L.L_CLONE);
            baad.AddBoxa(boxa, L.L_INSERT);
            nad = naa.GetNuma(index, L.L_CLONE);
            naad.AddNuma(nad, L.L_INSERT);
        }


/*    lept_stderr("box count = %d, numaa count = %d\n", nt,
            numaaGetNumberCount(naad)); */

        return baad;
    }

    // leptonica: boxaSort2dByIndex (boxfunc2.c)
    /// <summary>Returns the boxes arranged into a boxaa by an index numaa.</summary>
    /// <param name="naa">numaa that maps from the new baa to the input boxa</param>
    /// <returns>baa sorted boxaa</returns>
    /// <remarks>
    /// NOTE(upstream): the inner loop reads numaGetIValue(na, i, ...) instead
    /// of numaGetIValue(na, j, ...); ported as is.  See docs/UPSTREAM_NOTES.md.
    /// </remarks>
    public Boxaa Sort2dByIndex(Numaa naa)
    {
        const string func = "boxaSort2dByIndex";
        Boxa boxas = this;
        int boxtot;

        if ((boxtot = boxas.Count) == 0)
            throw Lept.Arg(func, "boxas is empty");

            /* Check counts */
        int ntot = naa.GetNumberCount();
        if (ntot != boxtot)
            throw Lept.Arg(func, "element count mismatch");

        int n = naa.Count;
        Boxaa baa = Boxaa.Create(n);
        for (int i = 0; i < n; i++) {
            Numa na = naa.GetNuma(i, L.L_CLONE);
            int nn = na.Count;
            Boxa boxa = Create(nn);
            for (int j = 0; j < nn; j++) {
                int index = na.GetIValue(i);  // NOTE(upstream): should be j
                Box box = boxas.GetBox(index, L.L_COPY);
                boxa.AddBox(box, L.L_INSERT);
            }
            baa.AddBoxa(boxa, L.L_INSERT);
        }

        return baa;
    }

    /*---------------------------------------------------------------------*
     *                        Boxa array extraction                        *
     *---------------------------------------------------------------------*/
    // leptonica: boxaExtractAsNuma (boxfunc2.c)
    /// <summary>Extracts the box sides and dimensions as arrays.</summary>
    /// <param name="nal">array of left locations</param>
    /// <param name="nat">array of top locations</param>
    /// <param name="nar">array of right locations</param>
    /// <param name="nab">array of bottom locations</param>
    /// <param name="naw">array of widths</param>
    /// <param name="nah">array of heights</param>
    /// <param name="keepinvalid">1 to keep invalid boxes; 0 to remove them</param>
    /// <remarks>
    /// (1) If you are counting or sorting values, such as determining
    ///     rank order, you must remove invalid boxes.
    /// (2) If you are parametrizing the values, or doing an evaluation
    ///     where the position in the boxa sequence is important, you
    ///     must replace the invalid boxes with valid ones before
    ///     doing the extraction. This is easily done with boxaFillSequence().
    /// </remarks>
    public void ExtractAsNuma(out Numa nal, out Numa nat, out Numa nar, out Numa nab,
                              out Numa naw, out Numa nah, int keepinvalid)
    {
        Boxa boxa = this;

        if (keepinvalid == 0 && boxa.GetValidCount() == 0)
            throw Lept.Arg("boxaExtractAsNuma", "no valid boxes");

        int n = boxa.Count;
        nal = Numa.Create(n);
        nat = Numa.Create(n);
        nar = Numa.Create(n);
        nab = Numa.Create(n);
        naw = Numa.Create(n);
        nah = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            boxa.GetBoxGeometry(i, out int left, out int top, out int w, out int h);
            if (keepinvalid == 0 && (w <= 0 || h <= 0))
                continue;
            int right = left + w - 1;
            int bot = top + h - 1;
            nal.AddNumber(left);
            nat.AddNumber(top);
            nar.AddNumber(right);
            nab.AddNumber(bot);
            naw.AddNumber(w);
            nah.AddNumber(h);
        }
    }

    // leptonica: boxaExtractAsPta (boxfunc2.c)
    /// <summary>Extracts the box sides and dimensions as pta, indexed by box number.</summary>
    /// <param name="ptal">array of left locations vs. index</param>
    /// <param name="ptat">array of top locations vs. index</param>
    /// <param name="ptar">array of right locations vs. index</param>
    /// <param name="ptab">array of bottom locations vs. index</param>
    /// <param name="ptaw">array of widths vs. index</param>
    /// <param name="ptah">array of heights vs. index</param>
    /// <param name="keepinvalid">1 to keep invalid boxes; 0 to remove them</param>
    /// <remarks>
    /// (1) For most applications, such as counting, sorting, fitting
    ///     to some parametrized form, plotting or filtering in general,
    ///     you should remove the invalid boxes.  Each pta saves the
    ///     box index in the x array, so replacing invalid boxes by
    ///     filling with boxaFillSequence(), which is required for
    ///     boxaExtractAsNuma(), is not necessary.
    /// (2) If invalid boxes are retained, each one will result in
    ///     entries (typically 0) in all selected output pta.
    /// (3) Other boxa --> pta functions are:
    ///     * boxaExtractCorners(): extracts any of the four corners as a pta.
    ///     * boxaConvertToPta(): extracts sufficient number of corners
    ///       to allow reconstruction of the original boxa from the pta.
    /// </remarks>
    public void ExtractAsPta(out Pta ptal, out Pta ptat, out Pta ptar, out Pta ptab,
                             out Pta ptaw, out Pta ptah, int keepinvalid)
    {
        Boxa boxa = this;

        if (keepinvalid == 0 && boxa.GetValidCount() == 0)
            throw Lept.Arg("boxaExtractAsPta", "no valid boxes");

        int n = boxa.Count;
        ptal = Pta.Create(n);
        ptat = Pta.Create(n);
        ptar = Pta.Create(n);
        ptab = Pta.Create(n);
        ptaw = Pta.Create(n);
        ptah = Pta.Create(n);
        for (int i = 0; i < n; i++) {
            boxa.GetBoxGeometry(i, out int left, out int top, out int w, out int h);
            if (keepinvalid == 0 && (w <= 0 || h <= 0))
                continue;
            int right = left + w - 1;
            int bot = top + h - 1;
            ptal.AddPt(i, left);
            ptat.AddPt(i, top);
            ptar.AddPt(i, right);
            ptab.AddPt(i, bot);
            ptaw.AddPt(i, w);
            ptah.AddPt(i, h);
        }
    }

    // leptonica: boxaExtractCorners (boxfunc2.c)
    /// <summary>Extracts one corner (or the center) of each box as a pta.</summary>
    /// <param name="loc">L_UPPER_LEFT, L_UPPER_RIGHT, L_LOWER_LEFT,
    /// L_LOWER_RIGHT, L_BOX_CENTER</param>
    /// <returns>pta of requested coordinates</returns>
    /// <remarks>
    /// (1) Extracts (0,0) for invalid boxes.
    /// (2) Other boxa --> pta functions are:
    ///     * boxaExtractAsPta(): allows extraction of any dimension
    ///       and/or side location, with each in a separate pta.
    ///     * boxaConvertToPta(): extracts sufficient number of corners
    ///       to allow reconstruction of the original boxa from the pta.
    /// </remarks>
    public Pta ExtractCorners(int loc)
    {
        Boxa boxa = this;

        if (loc != L.L_UPPER_LEFT && loc != L.L_UPPER_RIGHT && loc != L.L_LOWER_LEFT &&
            loc != L.L_LOWER_RIGHT && loc != L.L_BOX_CENTER)
            throw Lept.Arg("boxaExtractCorners", "invalid location");

        int n = boxa.Count;
        Pta pta = Pta.Create(n);

        for (int i = 0; i < n; i++) {
            boxa.GetBoxGeometry(i, out int left, out int top, out int w, out int h);
            int right = left + w - 1;
            int bot = top + h - 1;
            if (w == 0 || h == 0) {  /* invalid */
                left = 0;
                top = 0;
                right = 0;
                bot = 0;
            }
            if (loc == L.L_UPPER_LEFT)
                pta.AddPt(left, top);
            else if (loc == L.L_UPPER_RIGHT)
                pta.AddPt(right, top);
            else if (loc == L.L_LOWER_LEFT)
                pta.AddPt(left, bot);
            else if (loc == L.L_LOWER_RIGHT)
                pta.AddPt(right, bot);
            else if (loc == L.L_BOX_CENTER)
                pta.AddPt((left + right) / 2, (top + bot) / 2);
        }

        return pta;
    }

    /*---------------------------------------------------------------------*
     *                            Boxa statistics                          *
     *---------------------------------------------------------------------*/
    // leptonica: boxaGetRankVals (boxfunc2.c)
    /// <summary>Gets the rank values of the box sides and dimensions.</summary>
    /// <param name="fract">use 0.0 for smallest, 1.0 for largest width and height</param>
    /// <param name="x">rank value of x (left side)</param>
    /// <param name="y">rank value of y (top side)</param>
    /// <param name="r">rank value of right side</param>
    /// <param name="b">rank value of bottom side</param>
    /// <param name="w">rank value of width</param>
    /// <param name="h">rank value of height</param>
    /// <remarks>
    /// Throws if the boxa has no valid boxes.
    /// (1) This function does not assume that all boxes in the boxa are valid
    /// (2) The six box parameters are sorted independently.
    ///     For rank order, the width and height are sorted in increasing
    ///     order.  But what does it mean to sort x and y in "rank order"?
    ///     If the boxes are of comparable size and somewhat
    ///     aligned (e.g., from multiple images), it makes some sense
    ///     to give a "rank order" for x and y by sorting them in
    ///     decreasing order.  (By the same argument, we choose to sort
    ///     the r and b sides in increasing order.)  In general, the
    ///     interpretation of a rank order on x and y (or on r and b)
    ///     is highly application dependent.  In summary:
    ///        ~ x and y are sorted in decreasing order
    ///        ~ r and b are sorted in increasing order
    ///        ~ w and h are sorted in increasing order
    /// </remarks>
    public void GetRankVals(float fract, out int x, out int y, out int r, out int b,
                            out int w, out int h)
    {
        const string func = "boxaGetRankVals";
        Boxa boxa = this;

        x = 0;
        y = 0;
        r = 0;
        b = 0;
        w = 0;
        h = 0;
        if (fract < 0.0 || fract > 1.0)
            throw Lept.Arg(func, "fract not in [0.0 ... 1.0]");
        if (boxa.GetValidCount() == 0)
            throw Lept.Arg(func, "no valid boxes in boxa");

            /* Use only the valid boxes */
        boxa.ExtractAsNuma(out Numa nax, out Numa nay, out Numa nar, out Numa nab,
                           out Numa naw, out Numa nah, 0);

        float xval = nax.GetRankValue((float)(1.0 - fract), null, 1);
        x = (int)xval;
        float yval = nay.GetRankValue((float)(1.0 - fract), null, 1);
        y = (int)yval;
        float rval = nar.GetRankValue(fract, null, 1);
        r = (int)rval;
        float bval = nab.GetRankValue(fract, null, 1);
        b = (int)bval;
        float wval = naw.GetRankValue(fract, null, 1);
        w = (int)wval;
        float hval = nah.GetRankValue(fract, null, 1);
        h = (int)hval;
    }

    // leptonica: boxaGetMedianVals (boxfunc2.c)
    /// <summary>Gets the median values of the box sides and dimensions.</summary>
    /// <param name="x">median value of x (left side)</param>
    /// <param name="y">median value of y (top side)</param>
    /// <param name="r">median value of right side</param>
    /// <param name="b">median value of bottom side</param>
    /// <param name="w">median value of width</param>
    /// <param name="h">median value of height</param>
    /// <remarks>
    /// Throws if the boxa is empty or has no valid boxes.
    /// (1) See boxaGetRankVals()
    /// </remarks>
    public void GetMedianVals(out int x, out int y, out int r, out int b,
                              out int w, out int h)
    {
        Boxa boxa = this;

        if (boxa.GetValidCount() == 0)
            throw Lept.Arg("boxaGetMedianVals", "no valid boxes in boxa");

        boxa.GetRankVals(0.5f, out x, out y, out r, out b, out w, out h);
    }

    // leptonica: boxaGetAverageSize (boxfunc2.c)
    /// <summary>Gets the average width and height of the boxes.</summary>
    /// <param name="w">average width</param>
    /// <param name="h">average height</param>
    /// <remarks>Throws if the boxa is empty.</remarks>
    public void GetAverageSize(out float w, out float h)
    {
        Boxa boxa = this;

        w = 0.0f;
        h = 0.0f;
        int n;
        if ((n = boxa.Count) == 0)
            throw Lept.Arg("boxaGetAverageSize", "boxa is empty");

        float sumw, sumh;
        sumw = sumh = 0.0f;
        for (int i = 0; i < n; i++) {
            boxa.GetBoxGeometry(i, out _, out _, out int bw, out int bh);
            sumw += bw;
            sumh += bh;
        }

        w = sumw / n;
        h = sumh / n;
    }

    // leptonica: boxaEncapsulateAligned (boxfunc2.c)
    /// <summary>Puts %num consecutive boxes into each boxa of a new boxaa.</summary>
    /// <param name="num">number put into each boxa in the baa</param>
    /// <param name="copyflag">L_COPY or L_CLONE</param>
    /// <returns>baa</returns>
    /// <remarks>
    /// (1) This puts %num boxes from the input %boxa into each of a
    ///     set of boxa within an output baa.
    /// (2) This assumes that the boxes in %boxa are in sets of %num each.
    /// </remarks>
    public Boxaa EncapsulateAligned(int num, int copyflag)
    {
        const string func = "boxaEncapsulateAligned";
        Boxa boxa = this;

        if (copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg(func, "invalid copyflag");

        int n = boxa.Count;
        int nbaa = n / num;
        if (num * nbaa != n)
            Log.Error(func, "inconsistent alignment: num doesn't divide n\n");
        Boxaa baa = Boxaa.Create(nbaa);
        int index = 0;
        for (int i = 0; i < nbaa; i++) {
            Boxa boxat = Create(num);
            for (int j = 0; j < num; j++, index++) {
                Box box = boxa.GetBox(index, copyflag);
                boxat.AddBox(box, L.L_INSERT);
            }
            baa.AddBoxa(boxat, L.L_INSERT);
        }

        return baa;
    }
}

/*---------------------------------------------------------------------*
 *                        Other Boxaa functions                        *
 *---------------------------------------------------------------------*/
public sealed partial class Boxaa
{
    // leptonica: boxaaGetExtent (boxfunc2.c)
    /// <summary>Gets the extent of all boxes in the boxaa.</summary>
    /// <param name="w">width</param>
    /// <param name="h">height</param>
    /// <param name="box">minimum box containing all boxa in boxaa</param>
    /// <param name="boxa">boxa containing all boxes in each boxa in the boxaa</param>
    /// <remarks>
    /// (1) The returned w and h are the minimum size image
    ///     that would contain all boxes untranslated.
    /// (2) Each box in the returned boxa is the minimum box required to
    ///     hold all the boxes in the respective boxa of baa.
    /// (3) If there are no valid boxes in a boxa, the box corresponding
    ///     to its extent has all fields set to 0 (an invalid box).
    /// </remarks>
    public void GetExtent(out int w, out int h, out Box box, out Boxa boxa)
    {
        Boxaa baa = this;

        w = 0;
        h = 0;
        int n = baa.Count;
        if (n == 0)
            throw Lept.Arg("boxaaGetExtent", "no boxa in baa");

        boxa = Boxa.Create(n);
        int xmax = 0;
        int ymax = 0;
        int xmin = 100000000;
        int ymin = 100000000;
        bool found = false;
        for (int i = 0; i < n; i++) {
            Boxa boxa1 = baa.GetBoxa(i, L.L_CLONE);
            boxa1.GetExtent(out _, out _, out Box box1);
            box1.GetGeometry(out int x, out int y, out int bw, out int bh);
            if (bw > 0 && bh > 0) {  /* a valid extent box */
                found = true;  /* found at least one valid extent box */
                xmin = (xmin < x) ? xmin : x;
                ymin = (ymin < y) ? ymin : y;
                xmax = (xmax > x + bw) ? xmax : x + bw;
                ymax = (ymax > y + bh) ? ymax : y + bh;
            }
            boxa.AddBox(box1, L.L_INSERT);
        }
        if (found == false)  /* no valid extent boxes */
            xmin = ymin = 0;

        w = xmax;
        h = ymax;
        box = Box.Create(xmin, ymin, xmax - xmin, ymax - ymin);
    }

    // leptonica: boxaaFlattenToBoxa (boxfunc2.c)
    /// <summary>Flattens the boxaa to a boxa.</summary>
    /// <param name="naindex">the boxa index in the baa for each box</param>
    /// <param name="copyflag">L_COPY or L_CLONE</param>
    /// <returns>boxa</returns>
    /// <remarks>
    /// (1) This 'flattens' the baa to a boxa, taking the boxes in
    ///     order in the first boxa, then the second, etc.
    /// (2) If a boxa is empty, we generate an invalid, placeholder box
    ///     of zero size.  This is useful when converting from a baa
    ///     where each boxa has either 0 or 1 boxes, and it is necessary
    ///     to maintain a 1:1 correspondence between the initial
    ///     boxa array and the resulting box array.
    /// (3) The Numa %naindex gives, for each box in the baa, the index
    ///     of the boxa to which it belongs.
    /// </remarks>
    public Boxa FlattenToBoxa(out Numa naindex, int copyflag)
    {
        Boxaa baa = this;

        naindex = Numa.Create(0);
        if (copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg("boxaaFlattenToBoxa", "invalid copyflag");

        int n = baa.Count;
        Boxa boxa = Boxa.Create(n);
        for (int i = 0; i < n; i++) {
            Boxa boxat = baa.GetBoxa(i, L.L_CLONE);
            int m = boxat.Count;
            if (m == 0) {  /* placeholder box */
                Box box = Box.Create(0, 0, 0, 0);
                boxa.AddBox(box, L.L_INSERT);
                naindex.AddNumber(i);  /* save 'row' number */
            } else {
                for (int j = 0; j < m; j++) {
                    Box box = boxat.GetBox(j, copyflag);
                    boxa.AddBox(box, L.L_INSERT);
                    naindex.AddNumber(i);  /* save 'row' number */
                }
            }
        }

        return boxa;
    }

    // leptonica: boxaaFlattenAligned (boxfunc2.c)
    /// <summary>Flattens the boxaa to a boxa, taking the first %num boxes of each boxa.</summary>
    /// <param name="num">number extracted from each</param>
    /// <param name="fillerbox">[optional] that fills if necessary; can be null</param>
    /// <param name="copyflag">L_COPY or L_CLONE</param>
    /// <returns>boxa</returns>
    /// <remarks>
    /// (1) This 'flattens' the baa to a boxa, taking the first %num
    ///     boxes from each boxa.
    /// (2) In each boxa, if there are less than %num boxes, we preserve
    ///     the alignment between the input baa and the output boxa
    ///     by inserting one or more fillerbox(es) or, if %fillerbox == null,
    ///     one or more invalid placeholder boxes.
    /// </remarks>
    public Boxa FlattenAligned(int num, Box? fillerbox, int copyflag)
    {
        Boxaa baa = this;

        if (copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg("boxaaFlattenAligned", "invalid copyflag");

        int n = baa.Count;
        Boxa boxad = Boxa.Create(n);
        for (int i = 0; i < n; i++) {
            Boxa boxat = baa.GetBoxa(i, L.L_CLONE);
            int m = boxat.Count;
            int mval = (m < num) ? m : num;
            int nshort = num - mval;
            for (int j = 0; j < mval; j++) {  /* take the first %num if possible */
                Box box = boxat.GetBox(j, copyflag);
                boxad.AddBox(box, L.L_INSERT);
            }
            for (int j = 0; j < nshort; j++) {  /* add fillers if necessary */
                if (fillerbox != null) {
                    boxad.AddBox(fillerbox, L.L_COPY);
                } else {
                    Box box = Box.Create(0, 0, 0, 0);  /* invalid placeholder box */
                    boxad.AddBox(box, L.L_INSERT);
                }
            }
        }

        return boxad;
    }

    // leptonica: boxaaTranspose (boxfunc2.c)
    /// <summary>Transposes the boxaa, viewed as a 2D array of boxes.</summary>
    /// <returns>baad</returns>
    /// <remarks>
    /// (1) If you think of a boxaa as a 2D array of boxes that is accessed
    ///     row major, then each row is represented by one of the boxa.
    ///     This function creates a new boxaa related to the input boxaa
    ///     as a column major traversal of the input boxaa.
    /// (2) For example, if %baas has 2 boxa, each with 10 boxes, then
    ///     %baad will have 10 boxa, each with 2 boxes.
    /// (3) Require for this transpose operation that each boxa in
    ///     %baas has the same number of boxes.  This operation is useful
    ///     when the i-th boxes in each boxa are meaningfully related.
    /// </remarks>
    public Boxaa Transpose()
    {
        const string func = "boxaaTranspose";
        Boxaa baas = this;
        int ny, nbox = 0;

        if ((ny = baas.Count) == 0)
            throw Lept.Arg(func, "baas empty");

            /* Make sure that each boxa in baas has the same number of boxes */
        for (int i = 0; i < ny; i++) {
            Boxa boxa = baas.GetBoxa(i, L.L_CLONE);
            int nb = boxa.Count;
            if (i == 0)
                nbox = nb;
            else if (nb != nbox)
                throw Lept.Arg(func, "boxa are not all the same size");
        }

            /* baad[i][j] = baas[j][i] */
        Boxaa baad = Create(nbox);
        for (int i = 0; i < nbox; i++) {
            Boxa boxa = Boxa.Create(ny);
            for (int j = 0; j < ny; j++) {
                Box box = baas.GetBox(j, i, L.L_COPY)!;
                boxa.AddBox(box, L.L_INSERT);
            }
            baad.AddBoxa(boxa, L.L_INSERT);
        }
        return baad;
    }

    // leptonica: boxaaAlignBox (boxfunc2.c)
    /// <summary>Finds the boxa whose vertical extent best overlaps a box.</summary>
    /// <param name="box">to be aligned with next boxa in the baa, if possible</param>
    /// <param name="delta">amount by which consecutive components can miss
    /// in overlap and still be included in the array</param>
    /// <returns>index of boxa with best overlap, or if none match,
    /// this is the index of the next boxa to be generated</returns>
    /// <remarks>
    /// (1) This is not greedy.  It finds the boxa whose vertical
    ///     extent has the closest overlap with the input box.
    /// </remarks>
    public int AlignBox(Box box, int delta)
    {
        Boxaa baa = this;
        int maxindex = 0;  /* NOTE(upstream): uninitialized in C */

        int n = baa.Count;
        box.GetGeometry(out _, out int y, out _, out int h);
        int maxovlp = -10000000;
        for (int i = 0; i < n; i++) {
            Boxa boxa = baa.GetBoxa(i, L.L_CLONE);
            int m;
            if ((m = boxa.Count) == 0) {
                Log.Warning("boxaaAlignBox", "no boxes in boxa\n");
                continue;
            }
            boxa.GetExtent(out _, out _, out Box boxt);
            boxt.GetGeometry(out _, out int yt, out _, out int ht);

                /* Overlap < 0 means the components do not overlap vertically */
            int ovlp;
            if (yt >= y)
                ovlp = y + h - 1 - yt;
            else
                ovlp = yt + ht - 1 - y;
            if (ovlp > maxovlp) {
                maxovlp = ovlp;
                maxindex = i;
            }
        }

        if (maxovlp + delta >= 0)
            return maxindex;
        else
            return n;
    }
}
