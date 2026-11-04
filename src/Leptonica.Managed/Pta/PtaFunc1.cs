using System;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  ptafunc1.c: Pta utilities.
 *
 *      Simple rearrangements
 *      Geometric analysis
 *      Min/max and filtering
 *      Least squares fitting
 *      Interconversions with Pix and Numa
 *      Display into a pix
 *
 *  Differences from C:
 *    - No reference counting and no destroy calls: the GC owns the objects.
 *    - Functions that return an error code or NULL in C throw instead.
 *    - Optional output pointers become out parameters that are always set.
 *      The least squares fits of a line change the algorithm when a
 *      coefficient is not requested; they have one method for each case
 *      (see GetLinearLSF()).
 *    - Callees that are not ported yet (pixGetPixel(), pixMorphSequence(),
 *      numaGetMedian(), gplotSimple1(), ...) are stubs in their own files.
 */

/// <summary>Constants from pix.h, morph.h, environ.h and gplot.h used by ptafunc1.c.</summary>
public static partial class L
{
    /* Relation flags and selection types for ptaSelectByValue() (pix.h) */
                                              /* or yval) must satisfy constraint        */
                                              /* and yval) must satisfy constraint       */

    /* Flags for the boundary pixels (morph.h) */
    /// <summary>Assume background pixels outside the image.</summary>
    public const int L_BOUNDARY_BG = 1;
    /// <summary>Assume foreground pixels outside the image.</summary>
    public const int L_BOUNDARY_FG = 2;

    /* Search results (environ.h) */
    /// <summary>The searched-for item was not found.</summary>
    public const int L_NOT_FOUND = 0;
    /// <summary>The searched-for item was found.</summary>
    public const int L_FOUND = 1;

    /* Output formats of gnuplot (gplot.h) */
}

public sealed partial class Pta
{
    /*---------------------------------------------------------------------*
     *                        Simple rearrangements                        *
     *---------------------------------------------------------------------*/
    // leptonica: ptaSubsample (ptafunc1.c)
    /// <param name="subfactor">subsample factor, >= 1</param>
    /// <returns>ptad evenly sampled pt values from this pta</returns>
    public Pta Subsample(int subfactor)
    {
        if (subfactor < 1)
            throw Lept.Arg("ptaSubsample", "subfactor < 1");

        Pta ptad = Create(0);
        int n = Count;
        for (int i = 0; i < n; i++) {
            if (i % subfactor != 0) continue;
            GetPt(i, out float x, out float y);
            ptad.AddPt(x, y);
        }

        return ptad;
    }

    // leptonica: ptaJoin (ptafunc1.c)
    /// <summary>Adds the points of ptas to this (the destination) pta.</summary>
    /// <param name="ptas">source pta; add from this one; can be null</param>
    /// <param name="istart">starting index in ptas</param>
    /// <param name="iend">ending index in ptas; use -1 to cat all</param>
    /// <remarks>
    /// (1) istart &lt; 0 is taken to mean 'read from the start' (istart = 0)
    /// (2) iend &lt; 0 means 'read to the end'
    /// (3) if ptas == null, this is a no-op
    /// </remarks>
    public void Join(Pta? ptas, int istart, int iend)
    {
        if (ptas == null)
            return;

        if (istart < 0)
            istart = 0;
        int n = ptas.Count;
        if (iend < 0 || iend >= n)
            iend = n - 1;
        if (istart > iend)
            throw Lept.Arg("ptaJoin", "istart > iend; no pts");

        for (int i = istart; i <= iend; i++) {
            ptas.GetIPt(i, out int x, out int y);
            AddPt(x, y);
        }
    }
}

public sealed partial class Ptaa
{
    // leptonica: ptaaJoin (ptafunc1.c)
    /// <summary>Adds the pta of ptaas to this (the destination) ptaa.</summary>
    /// <param name="ptaas">source ptaa; add from this one; can be null</param>
    /// <param name="istart">starting index in ptaas</param>
    /// <param name="iend">ending index in ptaas; use -1 to cat all</param>
    /// <remarks>
    /// (1) istart &lt; 0 is taken to mean 'read from the start' (istart = 0)
    /// (2) iend &lt; 0 means 'read to the end'
    /// (3) if ptas == null, this is a no-op
    /// </remarks>
    public void Join(Ptaa? ptaas, int istart, int iend)
    {
        if (ptaas == null)
            return;

        if (istart < 0)
            istart = 0;
        int n = ptaas.Count;
        if (iend < 0 || iend >= n)
            iend = n - 1;
        if (istart > iend)
            throw Lept.Arg("ptaaJoin", "istart > iend; no pts");

        for (int i = istart; i <= iend; i++) {
            Pta pta = ptaas.GetPta(i, L.L_CLONE);
            AddPta(pta, L.L_INSERT);
        }
    }
}

public sealed partial class Pta
{
    // leptonica: ptaReverse (ptafunc1.c)
    /// <param name="type">0 for float values; 1 for integer values</param>
    /// <returns>ptad reversed pta</returns>
    public Pta Reverse(int type)
    {
        int n = Count;
        Pta ptad = Create(n);
        for (int i = n - 1; i >= 0; i--) {
            if (type == 0) {
                GetPt(i, out float x, out float y);
                ptad.AddPt(x, y);
            } else {  /* type == 1 */
                GetIPt(i, out int ix, out int iy);
                ptad.AddPt(ix, iy);
            }
        }

        return ptad;
    }

    // leptonica: ptaTranspose (ptafunc1.c)
    /// <returns>ptad with x and y values swapped</returns>
    public Pta Transpose()
    {
        int n = Count;
        Pta ptad = Create(n);
        for (int i = 0; i < n; i++) {
            GetPt(i, out float x, out float y);
            ptad.AddPt(y, x);
        }

        return ptad;
    }

    // leptonica: ptaCyclicPerm (ptafunc1.c)
    /// <param name="xs">start point; must be in this pta</param>
    /// <param name="ys">start point; must be in this pta</param>
    /// <returns>ptad cyclic permutation, starting and ending at (xs, ys)</returns>
    /// <remarks>
    /// (1) Check to insure that (a) ptas is a closed path where
    ///     the first and last points are identical, and (b) the
    ///     resulting pta also starts and ends on the same point
    ///     (which in this case is (xs, ys).
    /// </remarks>
    public Pta CyclicPerm(int xs, int ys)
    {
        int n = Count;

            /* Verify input data */
        if (n == 0) {
            /* C: ptaGetIPt() fails for both end points and leaves them at (0, 0);
             * they compare equal, and the search below finds nothing. */
            throw Lept.Arg("ptaCyclicPerm", "start pt not in ptas");
        }
        GetIPt(0, out int x1, out int y1);
        GetIPt(n - 1, out int x2, out int y2);
        if (x1 != x2 || y1 != y2)
            throw Lept.Arg("ptaCyclicPerm", "start and end pts not same");
        int state = L.L_NOT_FOUND;
        int i;
        int x, y;
        for (i = 0; i < n; i++) {
            GetIPt(i, out x, out y);
            if (x == xs && y == ys) {
                state = L.L_FOUND;
                break;
            }
        }
        if (state == L.L_NOT_FOUND)
            throw Lept.Arg("ptaCyclicPerm", "start pt not in ptas");

        Pta ptad = Create(n);
        for (int j = 0; j < n - 1; j++) {
            int index;
            if (i + j < n - 1)
                index = i + j;
            else
                index = (i + j + 1) % n;
            GetIPt(index, out x, out y);
            ptad.AddPt(x, y);
        }
        ptad.AddPt(xs, ys);

        return ptad;
    }

    // leptonica: ptaSelectRange (ptafunc1.c)
    /// <param name="first">use 0 to select from the beginning</param>
    /// <param name="last">use -1 to select to the end</param>
    public Pta SelectRange(int first, int last)
    {
        int n;
        if ((n = Count) == 0) {
            Log.Warning("ptaSelectRange", "ptas is empty");
            return Copy();
        }
        first = Math.Max(0, first);
        if (last < 0) last = n - 1;
        if (first >= n)
            throw Lept.Arg("ptaSelectRange", "invalid first");
        if (last >= n) {
            Log.Warning("ptaSelectRange", $"last = {last} is beyond max index = {n - 1}; adjusting");
            last = n - 1;
        }
        if (first > last)
            throw Lept.Arg("ptaSelectRange", "first > last");

        int npt = last - first + 1;
        Pta ptad = Create(npt);
        for (int i = first; i <= last; i++) {
            GetPt(i, out float x, out float y);
            ptad.AddPt(x, y);
        }
        return ptad;
    }

    /*---------------------------------------------------------------------*
     *                               Geometric                             *
     *---------------------------------------------------------------------*/
    // leptonica: ptaGetBoundingRegion (ptafunc1.c)
    /// <remarks>
    /// (1) This is used when the pta represents a set of points in
    ///     a two-dimensional image.  It returns the box of minimum
    ///     size containing the pts in the pta.
    /// </remarks>
    public Box GetBoundingRegion()
    {
        int minx = 10000000;
        int miny = 10000000;
        int maxx = -10000000;
        int maxy = -10000000;
        int n = Count;
        for (int i = 0; i < n; i++) {
            GetIPt(i, out int x, out int y);
            if (x < minx) minx = x;
            if (x > maxx) maxx = x;
            if (y < miny) miny = y;
            if (y > maxy) maxy = y;
        }

        return Box.Create(minx, miny, maxx - minx + 1, maxy - miny + 1);
    }

    // leptonica: ptaGetRange (ptafunc1.c)
    /// <param name="pminx">min value of x</param>
    /// <param name="pmaxx">max value of x</param>
    /// <param name="pminy">min value of y</param>
    /// <param name="pmaxy">max value of y</param>
    /// <remarks>
    /// (1) We can use pts to represent pairs of floating values, that
    ///     are not necessarily tied to a two-dimension region.  For
    ///     example, the pts can represent a general function y(x).
    /// (2) Throws if the pta is empty.
    /// </remarks>
    public void GetRange(out float pminx, out float pmaxx, out float pminy, out float pmaxy)
    {
        pminx = 0;
        pmaxx = 0;
        pminy = 0;
        pmaxy = 0;
        int n;
        if ((n = Count) == 0)
            throw Lept.Arg("ptaGetRange", "no points in pta");

        GetPt(0, out float x, out float y);
        float minx = x;
        float maxx = x;
        float miny = y;
        float maxy = y;
        for (int i = 1; i < n; i++) {
            GetPt(i, out x, out y);
            if (x < minx) minx = x;
            if (x > maxx) maxx = x;
            if (y < miny) miny = y;
            if (y > maxy) maxy = y;
        }
        pminx = minx;
        pmaxx = maxx;
        pminy = miny;
        pmaxy = maxy;
    }

    // leptonica: ptaGetInsideBox (ptafunc1.c)
    /// <param name="box"></param>
    /// <returns>ptad of pts in this pta that are inside the box</returns>
    public Pta GetInsideBox(Box box)
    {
        int n = Count;
        Pta ptad = Create(0);
        for (int i = 0; i < n; i++) {
            GetPt(i, out float x, out float y);
            bool contains = box.ContainsPt(x, y);
            if (contains)
                ptad.AddPt(x, y);
        }

        return ptad;
    }
}

public sealed partial class Pix
{
    // leptonica: pixFindCornerPixels (ptafunc1.c)
    /// <summary>pixs: 1 bpp</summary>
    /// <remarks>
    /// (1) Finds the 4 corner-most pixels, as defined by a search
    ///     inward from each corner, using a 45 degree line.
    /// </remarks>
    public Pta FindCornerPixels()
    {
        if (Depth != 1)
            throw Lept.Arg("pixFindCornerPixels", "pixs not 1 bpp");

        int w = Width;
        int h = Height;
        int mindim = Math.Min(w, h);
        uint[] data = Data;
        int wpl = Wpl;

        Pta pta = Pta.Create(4);

        bool found;
        int i, j, x, y;
        for (found = false, i = 0; i < mindim; i++) {
            for (j = 0; j <= i; j++) {
                y = i - j;
                ReadOnlySpan<uint> line = data.AsSpan(y * wpl, wpl);
                if (GetDataBit(line, j) != 0) {
                    pta.AddPt(j, y);
                    found = true;
                    break;
                }
            }
            if (found)
                break;
        }

        for (found = false, i = 0; i < mindim; i++) {
            for (j = 0; j <= i; j++) {
                y = i - j;
                ReadOnlySpan<uint> line = data.AsSpan(y * wpl, wpl);
                x = w - 1 - j;
                if (GetDataBit(line, x) != 0) {
                    pta.AddPt(x, y);
                    found = true;
                    break;
                }
            }
            if (found)
                break;
        }

        for (found = false, i = 0; i < mindim; i++) {
            for (j = 0; j <= i; j++) {
                y = h - 1 - i + j;
                ReadOnlySpan<uint> line = data.AsSpan(y * wpl, wpl);
                if (GetDataBit(line, j) != 0) {
                    pta.AddPt(j, y);
                    found = true;
                    break;
                }
            }
            if (found)
                break;
        }

        for (found = false, i = 0; i < mindim; i++) {
            for (j = 0; j <= i; j++) {
                y = h - 1 - i + j;
                ReadOnlySpan<uint> line = data.AsSpan(y * wpl, wpl);
                x = w - 1 - j;
                if (GetDataBit(line, x) != 0) {
                    pta.AddPt(x, y);
                    found = true;
                    break;
                }
            }
            if (found)
                break;
        }

        return pta;
    }
}

public sealed partial class Pta
{
    // leptonica: ptaContainsPt (ptafunc1.c)
    /// <returns>true if the point (x, y) is contained in this pta</returns>
    public bool ContainsPt(int x, int y)
    {
        int n = Count;
        for (int i = 0; i < n; i++) {
            GetIPt(i, out int ix, out int iy);
            if (x == ix && y == iy)
                return true;
        }
        return false;
    }

    // leptonica: ptaTestIntersection (ptafunc1.c)
    /// <returns>true if they have any elements in common</returns>
    /// <remarks>
    /// NOTE(upstream): the inner loop over pta2 reads point i of pta2 instead of
    /// point j.  This is ported as is; when i >= n2, ptaGetIPt() fails in C
    /// (logging an error) and leaves (x2, y2) = (0, 0), which is reproduced here.
    /// </remarks>
    public bool TestIntersection(Pta pta2)
    {
        int n1 = Count;
        int n2 = pta2.Count;
        for (int i = 0; i < n1; i++) {
            GetIPt(i, out int x1, out int y1);
            for (int j = 0; j < n2; j++) {
                int x2, y2;
                if (i < n2) {
                    pta2.GetIPt(i, out x2, out y2);
                } else {
                    Log.Error("ptaGetIPt", "invalid index");
                    x2 = 0;
                    y2 = 0;
                }
                if (x1 == x2 && y1 == y2)
                    return true;
            }
        }

        return false;
    }

    // leptonica: ptaTransform (ptafunc1.c)
    /// <param name="shiftx"></param>
    /// <param name="shifty"></param>
    /// <param name="scalex"></param>
    /// <param name="scaley"></param>
    /// <remarks>
    /// (1) Shift first, then scale.
    /// </remarks>
    public Pta Transform(int shiftx, int shifty, float scalex, float scaley)
    {
        int n = Count;
        Pta ptad = Create(n);
        for (int i = 0; i < n; i++) {
            GetIPt(i, out int x, out int y);
            x = (int)(scalex * (x + shiftx) + 0.5);
            y = (int)(scaley * (y + shifty) + 0.5);
            ptad.AddPt(x, y);
        }

        return ptad;
    }

    // leptonica: ptaPtInsidePolygon (ptafunc1.c)
    /// <param name="x">point to be tested</param>
    /// <param name="y">point to be tested</param>
    /// <returns>true if inside; false if outside or on boundary</returns>
    /// <remarks>
    /// This pta holds the vertices of a polygon.
    /// The abs value of the sum of the angles subtended from a point by
    /// the sides of a polygon, when taken in order traversing the polygon,
    /// is 0 if the point is outside the polygon and 2*pi if inside.
    /// The sign will be positive if traversed cw and negative if ccw.
    /// </remarks>
    public bool PtInsidePolygon(float x, float y)
    {
            /* Think of (x1,y1) as the end point of a vector that starts
             * from the origin (0,0), and ditto for (x2,y2). */
        int n = Count;
        float sum = 0.0f;
        for (int i = 0; i < n; i++) {
            GetPt(i, out float xp1, out float yp1);
            GetPt((i + 1) % n, out float xp2, out float yp2);
            float x1 = xp1 - x;
            float y1 = yp1 - y;
            float x2 = xp2 - x;
            float y2 = yp2 - y;
            sum += AngleBetweenVectors(x1, y1, x2, y2);
        }

        return LAbs(sum) > Math.PI;
    }

    // leptonica: l_angleBetweenVectors (ptafunc1.c)
    /// <param name="x1">end point of first vector</param>
    /// <param name="y1">end point of first vector</param>
    /// <param name="x2">end point of second vector</param>
    /// <param name="y2">end point of second vector</param>
    /// <returns>angle in radians</returns>
    /// <remarks>
    /// (1) This gives the angle between two vectors, going between
    ///     vector1 (x1,y1) and vector2 (x2,y2).  The angle is swept
    ///     out from 1 --> 2.  If this is clockwise, the angle is
    ///     positive, but the result is folded into the interval [-pi, pi].
    /// </remarks>
    public static float AngleBetweenVectors(float x1, float y1, float x2, float y2)
    {
        double ang = Math.Atan2(y2, x2) - Math.Atan2(y1, x1);
        if (ang > Math.PI) ang -= 2.0 * Math.PI;
        if (ang < -Math.PI) ang += 2.0 * Math.PI;
        return (float)ang;
    }

    // leptonica: ptaPolygonIsConvex (ptafunc1.c)
    /// <returns>true if convex</returns>
    /// <remarks>
    /// (1) A Pta of size n describes a polygon with n sides, where
    ///     the n-th side goes from point[n - 1] to point[0].
    /// (2) The pta must describe a CLOCKWISE traversal of the boundary
    ///     of the polygon.
    /// (3) Algorithm: traversing the boundary in a cw direction, the
    ///     polygon interior is always on the right.  If the polygon is
    ///     convex, for each set of 3 points, the third point is either
    ///     on the ray extending from the first point and going through
    ///     the second point, or to the right of it.
    /// </remarks>
    public bool PolygonIsConvex()
    {
        int n;
        if ((n = Count) < 3)
            throw Lept.Arg("ptaPolygonIsConvex", "pta has < 3 pts");

        for (int i = 0; i < n; i++) {
            GetPt(i, out float x0, out float y0);
            GetPt((i + 1) % n, out float x1, out float y1);
            GetPt((i + 2) % n, out float x2, out float y2);
                /* The vector v02 from p0 to p2 must be to the right of the
                   vector v01 from p0 to p1.  This is true if the cross
                   product v02 x v01 > 0.  In coordinates:
                       v02x * v01y - v01x * v02y, where
                       v01x = x1 - x0, v01y = y1 - y0,
                       v02x = x2 - x0, v02y = y2 - y0   */
            double cprod = (x2 - x0) * (y1 - y0) - (x1 - x0) * (y2 - y0);
            if (cprod < -0.0001)  /* small delta for float accuracy; test fails */
                return false;
        }
        return true;
    }

    /*---------------------------------------------------------------------*
     *                       Min/max and filtering                         *
     *---------------------------------------------------------------------*/
    // leptonica: ptaGetMinMax (ptafunc1.c)
    /// <param name="pxmin">min of x</param>
    /// <param name="pymin">min of y</param>
    /// <param name="pxmax">max of x</param>
    /// <param name="pymax">max of y</param>
    /// <remarks>If the pta is empty, the values are returned as -1.0.</remarks>
    public void GetMinMax(out float pxmin, out float pymin, out float pxmax, out float pymax)
    {
        pxmin = -1.0f;
        pymin = -1.0f;
        pxmax = -1.0f;
        pymax = -1.0f;
        int n;
        if ((n = Count) == 0) {
            Log.Warning("ptaGetMinMax", "pta is empty");
            return;
        }

        float xmin, ymin, xmax, ymax;
        xmin = ymin = 1.0e20f;
        xmax = ymax = -1.0e20f;
        for (int i = 0; i < n; i++) {
            GetPt(i, out float x, out float y);
            if (x < xmin) xmin = x;
            if (y < ymin) ymin = y;
            if (x > xmax) xmax = x;
            if (y > ymax) ymax = y;
        }
        pxmin = xmin;
        pymin = ymin;
        pxmax = xmax;
        pymax = ymax;
    }

    // leptonica: ptaSelectByValue (ptafunc1.c)
    /// <param name="xth">threshold value</param>
    /// <param name="yth">threshold value</param>
    /// <param name="type">L_SELECT_XVAL, L_SELECT_YVAL, L_SELECT_IF_EITHER, L_SELECT_IF_BOTH</param>
    /// <param name="relation">L_SELECT_IF_LT, L_SELECT_IF_GT, L_SELECT_IF_LTE, L_SELECT_IF_GTE</param>
    /// <returns>ptad filtered set</returns>
    public Pta SelectByValue(float xth, float yth, int type, int relation)
    {
        if (Count == 0) {
            Log.Warning("ptaSelectByValue", "ptas is empty");
            return Copy();
        }
        if (type != L.L_SELECT_XVAL && type != L.L_SELECT_YVAL &&
            type != L.L_SELECT_IF_EITHER && type != L.L_SELECT_IF_BOTH)
            throw Lept.Arg("ptaSelectByValue", "invalid type");
        if (relation != L.L_SELECT_IF_LT && relation != L.L_SELECT_IF_GT &&
            relation != L.L_SELECT_IF_LTE && relation != L.L_SELECT_IF_GTE)
            throw Lept.Arg("ptaSelectByValue", "invalid relation");

        int n = Count;
        Pta ptad = Create(n);
        for (int i = 0; i < n; i++) {
            GetPt(i, out float x, out float y);
            if (type == L.L_SELECT_XVAL) {
                if ((relation == L.L_SELECT_IF_LT && x < xth) ||
                    (relation == L.L_SELECT_IF_GT && x > xth) ||
                    (relation == L.L_SELECT_IF_LTE && x <= xth) ||
                    (relation == L.L_SELECT_IF_GTE && x >= xth))
                    ptad.AddPt(x, y);
            } else if (type == L.L_SELECT_YVAL) {
                if ((relation == L.L_SELECT_IF_LT && y < yth) ||
                    (relation == L.L_SELECT_IF_GT && y > yth) ||
                    (relation == L.L_SELECT_IF_LTE && y <= yth) ||
                    (relation == L.L_SELECT_IF_GTE && y >= yth))
                    ptad.AddPt(x, y);
            } else if (type == L.L_SELECT_IF_EITHER) {
                if (((relation == L.L_SELECT_IF_LT) && (x < xth || y < yth)) ||
                    ((relation == L.L_SELECT_IF_GT) && (x > xth || y > yth)) ||
                    ((relation == L.L_SELECT_IF_LTE) && (x <= xth || y <= yth)) ||
                    ((relation == L.L_SELECT_IF_GTE) && (x >= xth || y >= yth)))
                    ptad.AddPt(x, y);
            } else {  /* L_SELECT_IF_BOTH */
                if (((relation == L.L_SELECT_IF_LT) && (x < xth && y < yth)) ||
                    ((relation == L.L_SELECT_IF_GT) && (x > xth && y > yth)) ||
                    ((relation == L.L_SELECT_IF_LTE) && (x <= xth && y <= yth)) ||
                    ((relation == L.L_SELECT_IF_GTE) && (x >= xth && y >= yth)))
                    ptad.AddPt(x, y);
            }
        }

        return ptad;
    }

    // leptonica: ptaCropToMask (ptafunc1.c)
    /// <param name="pixm">1 bpp mask</param>
    /// <returns>ptad with only pts under the mask fg</returns>
    public Pta CropToMask(Pix pixm)
    {
        if (pixm.Depth != 1)
            throw Lept.Arg("ptaCropToMask", "pixm undefined or not 1 bpp");
        if (Count == 0) {
            Log.Info("ptaCropToMask", "ptas is empty");
            return Copy();
        }

        int n = Count;
        Pta ptad = Create(n);
        for (int i = 0; i < n; i++) {
            GetIPt(i, out int x, out int y);
            uint val;
            try {
                pixm.GetPixel(x, y, out val);
            } catch (ArgumentException) {
                /* C: pixGetPixel() logs an error and sets val to 0 */
                val = 0;
            }
            if (val == 1)
                ptad.AddPt(x, y);
        }
        return ptad;
    }

    /* L_ABS(): not Math.Abs(), which differs for -0.0 */
    private static float LAbs(float v) => v < 0 ? -v : v;

    /*---------------------------------------------------------------------*
     *                            Least Squares Fit                        *
     *---------------------------------------------------------------------*/
    // leptonica: ptaGetLinearLSF (ptafunc1.c)
    /// <summary>Least squares fit of a general line y = ax + b.</summary>
    /// <param name="a">slope a of least square fit: y = ax + b</param>
    /// <param name="b">intercept b of least square fit</param>
    /// <param name="nafit">numa of least square fit</param>
    /// <remarks>
    /// (1) In C, the type of line that is fit depends on which of &amp;a and &amp;b
    ///     are given; here each case has its own method: this one (both),
    ///     <see cref="GetLinearLSFThroughOrigin"/> (&amp;a only) and
    ///     <see cref="GetLinearLSFHorizontal"/> (&amp;b only, or neither).
    /// (2) This returns a and b that minimize:
    ///
    ///         sum (yi - axi -b)^2
    ///          i
    ///
    ///     The method is simple: differentiate this expression w/rt a and b,
    ///     and solve the resulting two equations for a and b in terms of
    ///     various sums over the input data (xi, yi).
    /// (3) nafit is an array of fitted values,
    ///     corresponding to the two implicit Numa arrays (nax and nay) in pta.
    ///     Thus, just as you can plot the data in pta as nay vs. nax,
    ///     you can plot the linear least square fit as nafit vs. nax.
    ///     Get the nax array using ptaGetArrays(pta, &amp;nax, NULL);
    /// </remarks>
    public void GetLinearLSF(out float a, out float b, out Numa nafit)
    {
        LinearLSF(true, true, out a, out b, out nafit);
    }

    /// <summary>
    /// The linear least square fit that goes through the origin (b = 0):
    /// ptaGetLinearLSF() with &amp;a given and &amp;b = NULL.
    /// </summary>
    public void GetLinearLSFThroughOrigin(out float a, out Numa nafit)
    {
        LinearLSF(true, false, out a, out _, out nafit);
    }

    /// <summary>
    /// The linear least square fit with zero slope (a = 0):
    /// ptaGetLinearLSF() with &amp;b given and &amp;a = NULL.
    /// </summary>
    public void GetLinearLSFHorizontal(out float b, out Numa nafit)
    {
        LinearLSF(false, true, out _, out b, out nafit);
    }

    /// <summary>Computes the linear least-squares fit <c>y = a * x + b</c> to the points.</summary>
    /// <param name="wantA">Whether the slope <c>a</c> is fitted; if false, <c>a</c> is taken as zero.</param>
    /// <param name="wantB">Whether the intercept <c>b</c> is fitted; if false, <c>b</c> is taken as zero.</param>
    /// <param name="pa">Receives the fitted slope.</param>
    /// <param name="pb">Receives the fitted intercept.</param>
    /// <param name="pnafit">Receives the fitted y values at each point.</param>
    private void LinearLSF(bool wantA, bool wantB, out float pa, out float pb, out Numa pnafit)
    {
        int n;
        if ((n = Count) < 2)
            throw Lept.Arg("ptaGetLinearLSF", "less than 2 pts found");

        float[] xa = x;  /* not a copy */
        float[] ya = y;  /* not a copy */
        float a, b, factor, sx, sy, sxx, sxy;
        sx = sy = sxx = sxy = 0.0f;
        if (wantA && wantB) {  /* general line */
            for (int i = 0; i < n; i++) {
                sx += xa[i];
                sy += ya[i];
                sxx += xa[i] * xa[i];
                sxy += xa[i] * ya[i];
            }
            factor = n * sxx - sx * sx;
            if (factor == 0.0)
                throw Lept.Fail("ptaGetLinearLSF", "no solution found");
            factor = 1.0f / factor;

            a = factor * ((float)n * sxy - sx * sy);
            b = factor * (sxx * sy - sx * sxy);
        } else if (wantA) {  /* b = 0; line through origin */
            for (int i = 0; i < n; i++) {
                sxx += xa[i] * xa[i];
                sxy += xa[i] * ya[i];
            }
            if (sxx == 0.0)
                throw Lept.Fail("ptaGetLinearLSF", "no solution found");
            a = sxy / sxx;
            b = 0.0f;
        } else {  /* a = 0; horizontal line */
            for (int i = 0; i < n; i++)
                sy += ya[i];
            a = 0.0f;
            b = sy / (float)n;
        }

        Numa nafit = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            float val = a * xa[i] + b;
            nafit.AddNumber(val);
        }

        pnafit = nafit;
        pa = a;
        pb = b;
    }

    // leptonica: ptaGetQuadraticLSF (ptafunc1.c)
    /// <param name="a">coeff a of LSF: y = ax^2 + bx + c</param>
    /// <param name="b">coeff b of LSF: y = ax^2 + bx + c</param>
    /// <param name="c">coeff c of LSF: y = ax^2 + bx + c</param>
    /// <param name="nafit">numa of least square fit</param>
    /// <remarks>
    /// (1) This does a quadratic least square fit to the set of points
    ///     in this pta.  That is, it finds coefficients a, b and c that minimize:
    ///
    ///         sum (yi - a*xi*xi -b*xi -c)^2
    ///          i
    ///
    ///     The method is simple: differentiate this expression w/rt
    ///     a, b and c, and solve the resulting three equations for these
    ///     coefficients in terms of various sums over the input data (xi, yi).
    ///     The three equations are in the form:
    ///        f[0][0]a + f[0][1]b + f[0][2]c = g[0]
    ///        f[1][0]a + f[1][1]b + f[1][2]c = g[1]
    ///        f[2][0]a + f[2][1]b + f[2][2]c = g[2]
    /// (2) nafit is an array of fitted values,
    ///     corresponding to the two implicit Numa arrays (nax and nay) in pta.
    ///     Thus, just as you can plot the data in pta as nay vs. nax,
    ///     you can plot the linear least square fit as nafit vs. nax.
    ///     Get the nax array using ptaGetArrays(pta, &amp;nax, NULL);
    /// </remarks>
    public void GetQuadraticLSF(out float a, out float b, out float c, out Numa nafit)
    {
        int n;
        if ((n = Count) < 3)
            throw Lept.Arg("ptaGetQuadraticLSF", "less than 3 pts found");

        float[] xa = x;  /* not a copy */
        float[] ya = y;  /* not a copy */
        float sx, sy, sx2, sx3, sx4, sxy, sx2y;
        sx = sy = sx2 = sx3 = sx4 = sxy = sx2y = 0.0f;
        for (int i = 0; i < n; i++) {
            float xv = xa[i];
            float yv = ya[i];
            sx += xv;
            sy += yv;
            sx2 += xv * xv;
            sx3 += xv * xv * xv;
            sx4 += xv * xv * xv * xv;
            sxy += xv * yv;
            sx2y += xv * xv * yv;
        }

        float[][] f = new float[3][];
        for (int i = 0; i < 3; i++)
            f[i] = new float[3];
        float[] g = new float[3];
        f[0][0] = sx4;
        f[0][1] = sx3;
        f[0][2] = sx2;
        f[1][0] = sx3;
        f[1][1] = sx2;
        f[1][2] = sx;
        f[2][0] = sx2;
        f[2][1] = sx;
        f[2][2] = n;
        g[0] = sx2y;
        g[1] = sxy;
        g[2] = sy;

            /* Solve for the unknowns, also putting f-inverse into f */
        bool ok = Pix.GaussJordan(f, g, 3);
        if (!ok)
            throw Lept.Fail("ptaGetQuadraticLSF", "quadratic solution failed");

        a = g[0];
        b = g[1];
        c = g[2];
        nafit = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            float xv = xa[i];
            float yv = g[0] * xv * xv + g[1] * xv + g[2];
            nafit.AddNumber(yv);
        }
    }

    // leptonica: ptaGetCubicLSF (ptafunc1.c)
    /// <param name="a">coeff a of LSF: y = ax^3 + bx^2 + cx + d</param>
    /// <param name="b">coeff b of LSF</param>
    /// <param name="c">coeff c of LSF</param>
    /// <param name="d">coeff d of LSF</param>
    /// <param name="nafit">numa of least square fit</param>
    /// <remarks>
    /// (1) This does a cubic least square fit to the set of points
    ///     in this pta.  That is, it finds coefficients a, b, c and d
    ///     that minimize:
    ///
    ///         sum (yi - a*xi*xi*xi -b*xi*xi -c*xi - d)^2
    ///          i
    ///
    ///     Differentiate this expression w/rt a, b, c and d, and solve
    ///     the resulting four equations for these coefficients in
    ///     terms of various sums over the input data (xi, yi).
    ///     The four equations are in the form:
    ///        f[0][0]a + f[0][1]b + f[0][2]c + f[0][3] = g[0]
    ///        f[1][0]a + f[1][1]b + f[1][2]c + f[1][3] = g[1]
    ///        f[2][0]a + f[2][1]b + f[2][2]c + f[2][3] = g[2]
    ///        f[3][0]a + f[3][1]b + f[3][2]c + f[3][3] = g[3]
    /// (2) nafit is an array of fitted values,
    ///     corresponding to the two implicit Numa arrays (nax and nay) in pta.
    ///     Thus, just as you can plot the data in pta as nay vs. nax,
    ///     you can plot the linear least square fit as nafit vs. nax.
    ///     Get the nax array using ptaGetArrays(pta, &amp;nax, NULL);
    /// </remarks>
    public void GetCubicLSF(out float a, out float b, out float c, out float d, out Numa nafit)
    {
        int n;
        if ((n = Count) < 4)
            throw Lept.Arg("ptaGetCubicLSF", "less than 4 pts found");

        float[] xa = x;  /* not a copy */
        float[] ya = y;  /* not a copy */
        float sx, sy, sx2, sx3, sx4, sx5, sx6, sxy, sx2y, sx3y;
        sx = sy = sx2 = sx3 = sx4 = sx5 = sx6 = sxy = sx2y = sx3y = 0.0f;
        for (int i = 0; i < n; i++) {
            float xv = xa[i];
            float yv = ya[i];
            sx += xv;
            sy += yv;
            sx2 += xv * xv;
            sx3 += xv * xv * xv;
            sx4 += xv * xv * xv * xv;
            sx5 += xv * xv * xv * xv * xv;
            sx6 += xv * xv * xv * xv * xv * xv;
            sxy += xv * yv;
            sx2y += xv * xv * yv;
            sx3y += xv * xv * xv * yv;
        }

        float[][] f = new float[4][];
        for (int i = 0; i < 4; i++)
            f[i] = new float[4];
        float[] g = new float[4];
        f[0][0] = sx6;
        f[0][1] = sx5;
        f[0][2] = sx4;
        f[0][3] = sx3;
        f[1][0] = sx5;
        f[1][1] = sx4;
        f[1][2] = sx3;
        f[1][3] = sx2;
        f[2][0] = sx4;
        f[2][1] = sx3;
        f[2][2] = sx2;
        f[2][3] = sx;
        f[3][0] = sx3;
        f[3][1] = sx2;
        f[3][2] = sx;
        f[3][3] = n;
        g[0] = sx3y;
        g[1] = sx2y;
        g[2] = sxy;
        g[3] = sy;

            /* Solve for the unknowns, also putting f-inverse into f */
        bool ok = Pix.GaussJordan(f, g, 4);
        if (!ok)
            throw Lept.Fail("ptaGetCubicLSF", "cubic solution failed");

        a = g[0];
        b = g[1];
        c = g[2];
        d = g[3];
        nafit = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            float xv = xa[i];
            float yv = g[0] * xv * xv * xv + g[1] * xv * xv + g[2] * xv + g[3];
            nafit.AddNumber(yv);
        }
    }

    // leptonica: ptaGetQuarticLSF (ptafunc1.c)
    /// <param name="a">coeff a of LSF: y = ax^4 + bx^3 + cx^2 + dx + e</param>
    /// <param name="b">coeff b of LSF</param>
    /// <param name="c">coeff c of LSF</param>
    /// <param name="d">coeff d of LSF</param>
    /// <param name="e">coeff e of LSF</param>
    /// <param name="nafit">numa of least square fit</param>
    /// <remarks>
    /// (1) This does a quartic least square fit to the set of points
    ///     in this pta.  That is, it finds coefficients a, b, c, d and 3
    ///     that minimize:
    ///
    ///         sum (yi - a*xi*xi*xi*xi -b*xi*xi*xi -c*xi*xi - d*xi - e)^2
    ///          i
    ///
    ///     Differentiate this expression w/rt a, b, c, d and e, and solve
    ///     the resulting five equations for these coefficients in
    ///     terms of various sums over the input data (xi, yi).
    ///     The five equations are in the form:
    ///        f[0][0]a + f[0][1]b + f[0][2]c + f[0][3] + f[0][4] = g[0]
    ///        f[1][0]a + f[1][1]b + f[1][2]c + f[1][3] + f[1][4] = g[1]
    ///        f[2][0]a + f[2][1]b + f[2][2]c + f[2][3] + f[2][4] = g[2]
    ///        f[3][0]a + f[3][1]b + f[3][2]c + f[3][3] + f[3][4] = g[3]
    ///        f[4][0]a + f[4][1]b + f[4][2]c + f[4][3] + f[4][4] = g[4]
    /// (2) nafit is an array of fitted values,
    ///     corresponding to the two implicit Numa arrays (nax and nay) in pta.
    ///     Thus, just as you can plot the data in pta as nay vs. nax,
    ///     you can plot the linear least square fit as nafit vs. nax.
    ///     Get the nax array using ptaGetArrays(pta, &amp;nax, NULL);
    /// </remarks>
    public void GetQuarticLSF(out float a, out float b, out float c, out float d, out float e,
                              out Numa nafit)
    {
        int n;
        if ((n = Count) < 5)
            throw Lept.Arg("ptaGetQuarticLSF", "less than 5 pts found");

        float[] xa = x;  /* not a copy */
        float[] ya = y;  /* not a copy */
        float sx, sy, sx2, sx3, sx4, sx5, sx6, sx7, sx8;
        float sxy, sx2y, sx3y, sx4y;
        sx = sy = sx2 = sx3 = sx4 = sx5 = sx6 = sx7 = sx8 = 0;
        sxy = sx2y = sx3y = sx4y = 0.0f;
        for (int i = 0; i < n; i++) {
            float xv = xa[i];
            float yv = ya[i];
            sx += xv;
            sy += yv;
            sx2 += xv * xv;
            sx3 += xv * xv * xv;
            sx4 += xv * xv * xv * xv;
            sx5 += xv * xv * xv * xv * xv;
            sx6 += xv * xv * xv * xv * xv * xv;
            sx7 += xv * xv * xv * xv * xv * xv * xv;
            sx8 += xv * xv * xv * xv * xv * xv * xv * xv;
            sxy += xv * yv;
            sx2y += xv * xv * yv;
            sx3y += xv * xv * xv * yv;
            sx4y += xv * xv * xv * xv * yv;
        }

        float[][] f = new float[5][];
        for (int i = 0; i < 5; i++)
            f[i] = new float[5];
        float[] g = new float[5];
        f[0][0] = sx8;
        f[0][1] = sx7;
        f[0][2] = sx6;
        f[0][3] = sx5;
        f[0][4] = sx4;
        f[1][0] = sx7;
        f[1][1] = sx6;
        f[1][2] = sx5;
        f[1][3] = sx4;
        f[1][4] = sx3;
        f[2][0] = sx6;
        f[2][1] = sx5;
        f[2][2] = sx4;
        f[2][3] = sx3;
        f[2][4] = sx2;
        f[3][0] = sx5;
        f[3][1] = sx4;
        f[3][2] = sx3;
        f[3][3] = sx2;
        f[3][4] = sx;
        f[4][0] = sx4;
        f[4][1] = sx3;
        f[4][2] = sx2;
        f[4][3] = sx;
        f[4][4] = n;
        g[0] = sx4y;
        g[1] = sx3y;
        g[2] = sx2y;
        g[3] = sxy;
        g[4] = sy;

            /* Solve for the unknowns, also putting f-inverse into f */
        bool ok = Pix.GaussJordan(f, g, 5);
        if (!ok)
            throw Lept.Fail("ptaGetQuarticLSF", "quartic solution failed");

        a = g[0];
        b = g[1];
        c = g[2];
        d = g[3];
        e = g[4];
        nafit = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            float xv = xa[i];
            float yv = g[0] * xv * xv * xv * xv + g[1] * xv * xv * xv + g[2] * xv * xv
                       + g[3] * xv + g[4];
            nafit.AddNumber(yv);
        }
    }

    // leptonica: ptaNoisyLinearLSF (ptafunc1.c)
    /// <summary>Linear least square fit of a general line, with outliers removed.</summary>
    /// <param name="factor">reject outliers with error greater than this number of medians; typically ~ 3</param>
    /// <param name="ptad">with outliers removed</param>
    /// <param name="a">slope a of least square fit: y = ax + b</param>
    /// <param name="b">intercept b of least square fit</param>
    /// <param name="mederr">median error</param>
    /// <param name="nafit">numa of least square fit to ptad</param>
    /// <remarks>
    /// (1) This does a linear least square fit to the set of points
    ///     in this pta.  It then evaluates the errors and removes points
    ///     whose error is >= factor * median_error.  It then re-runs
    ///     the linear LSF on the resulting points.
    /// (2) As for <see cref="GetLinearLSF"/>, the cases where only a or
    ///     only b is requested in C are
    ///     <see cref="NoisyLinearLSFThroughOrigin"/> and
    ///     <see cref="NoisyLinearLSFHorizontal"/>.
    /// (3) The median error can give an indication of how good the fit
    ///     is likely to be.
    /// </remarks>
    public void NoisyLinearLSF(float factor, out Pta ptad, out float a, out float b,
                               out float mederr, out Numa nafit)
    {
        NoisyLinearLSF(true, true, factor, out ptad, out a, out b, out mederr, out nafit);
    }

    /// <summary>
    /// The noisy linear fit through the origin (b = 0):
    /// ptaNoisyLinearLSF() with &amp;a given and &amp;b = NULL.
    /// </summary>
    public void NoisyLinearLSFThroughOrigin(float factor, out Pta ptad, out float a,
                                            out float mederr, out Numa nafit)
    {
        NoisyLinearLSF(true, false, factor, out ptad, out a, out _, out mederr, out nafit);
    }

    /// <summary>
    /// The noisy linear fit with zero slope (a = 0):
    /// ptaNoisyLinearLSF() with &amp;b given and &amp;a = NULL.
    /// </summary>
    public void NoisyLinearLSFHorizontal(float factor, out Pta ptad, out float b,
                                         out float mederr, out Numa nafit)
    {
        NoisyLinearLSF(false, true, factor, out ptad, out _, out b, out mederr, out nafit);
    }

    private void NoisyLinearLSF(bool wantA, bool wantB, float factor, out Pta pptad, out float pa,
                                out float pb, out float pmederr, out Numa pnafit)
    {
        if (factor <= 0.0)
            throw Lept.Arg("ptaNoisyLinearLSF", "factor must be > 0.0");
        int n;
        if ((n = Count) < 3)
            throw Lept.Arg("ptaNoisyLinearLSF", "less than 2 pts found");

        Numa nafit;
        try {
            LinearLSF(wantA, wantB, out _, out _, out nafit);
        } catch (LeptonicaException) {
            throw Lept.Fail("ptaNoisyLinearLSF", "error in linear LSF");
        }

            /* Get the median error */
        Numa naerror = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            GetPt(i, out float xv, out float yv);
            float yf = nafit.GetFValue(i);
            naerror.AddNumber(LAbs(yv - yf));
        }
        float mederr = naerror.GetMedian();
        pmederr = mederr;

            /* Remove outliers */
        Pta ptad = Create(n);
        for (int i = 0; i < n; i++) {
            GetPt(i, out float xv, out float yv);
            float val = naerror.GetFValue(i);
            if (val <= factor * mederr)  /* <= in case mederr = 0 */
                ptad.AddPt(xv, yv);
        }

           /* Do LSF again */
        ptad.LinearLSF(wantA, wantB, out pa, out pb, out pnafit);
        pptad = ptad;
    }

    // leptonica: ptaNoisyQuadraticLSF (ptafunc1.c)
    /// <param name="factor">reject outliers with error greater than this number of medians; typically ~ 3</param>
    /// <param name="ptad">with outliers removed</param>
    /// <param name="a">coeff a of LSF: y = ax^2 + bx + c</param>
    /// <param name="b">coeff b of LSF: y = ax^2 + bx + c</param>
    /// <param name="c">coeff c of LSF: y = ax^2 + bx + c</param>
    /// <param name="mederr">median error</param>
    /// <param name="nafit">numa of least square fit to ptad</param>
    /// <remarks>
    /// (1) This does a quadratic least square fit to the set of points
    ///     in this pta.  It then evaluates the errors and removes points
    ///     whose error is >= factor * median_error.  It then re-runs
    ///     a quadratic LSF on the resulting points.
    /// </remarks>
    public void NoisyQuadraticLSF(float factor, out Pta ptad, out float a, out float b,
                                  out float c, out float mederr, out Numa nafit)
    {
        if (factor <= 0.0)
            throw Lept.Arg("ptaNoisyQuadraticLSF", "factor must be > 0.0");
        int n;
        if ((n = Count) < 3)
            throw Lept.Arg("ptaNoisyQuadraticLSF", "less than 3 pts found");

        Numa nafit1;
        try {
            GetQuadraticLSF(out _, out _, out _, out nafit1);
        } catch (LeptonicaException) {
            throw Lept.Fail("ptaNoisyQuadraticLSF", "error in quadratic LSF");
        }

            /* Get the median error */
        Numa naerror = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            GetPt(i, out float xv, out float yv);
            float yf = nafit1.GetFValue(i);
            naerror.AddNumber(LAbs(yv - yf));
        }
        float me = naerror.GetMedian();
        mederr = me;

            /* Remove outliers */
        Pta ptat = Create(n);
        for (int i = 0; i < n; i++) {
            GetPt(i, out float xv, out float yv);
            float val = naerror.GetFValue(i);
            if (val <= factor * me)  /* <= in case mederr = 0 */
                ptat.AddPt(xv, yv);
        }
        if ((n = ptat.Count) < 3)
            throw Lept.Arg("ptaNoisyQuadraticLSF", "less than 3 pts found");

           /* Do LSF again */
        ptat.GetQuadraticLSF(out a, out b, out c, out nafit);
        ptad = ptat;
    }

    // leptonica: applyLinearFit (ptafunc1.c)
    /// <param name="a">linear fit coefficient</param>
    /// <param name="b">linear fit coefficient</param>
    /// <param name="x"></param>
    /// <returns>y = a * x + b</returns>
    public static float ApplyLinearFit(float a, float b, float x)
    {
        return a * x + b;
    }

    // leptonica: applyQuadraticFit (ptafunc1.c)
    /// <param name="a">quadratic fit coefficient</param>
    /// <param name="b">quadratic fit coefficient</param>
    /// <param name="c">quadratic fit coefficient</param>
    /// <param name="x"></param>
    /// <returns>y = a * x^2 + b * x + c</returns>
    public static float ApplyQuadraticFit(float a, float b, float c, float x)
    {
        return a * x * x + b * x + c;
    }

    // leptonica: applyCubicFit (ptafunc1.c)
    /// <param name="a">cubic fit coefficient</param>
    /// <param name="b">cubic fit coefficient</param>
    /// <param name="c">cubic fit coefficient</param>
    /// <param name="d">cubic fit coefficient</param>
    /// <param name="x"></param>
    /// <returns>y = a * x^3 + b * x^2  + c * x + d</returns>
    public static float ApplyCubicFit(float a, float b, float c, float d, float x)
    {
        return a * x * x * x + b * x * x + c * x + d;
    }

    // leptonica: applyQuarticFit (ptafunc1.c)
    /// <param name="a">quartic fit coefficient</param>
    /// <param name="b">quartic fit coefficient</param>
    /// <param name="c">quartic fit coefficient</param>
    /// <param name="d">quartic fit coefficient</param>
    /// <param name="e">quartic fit coefficient</param>
    /// <param name="x"></param>
    /// <returns>y = a * x^4 + b * x^3  + c * x^2 + d * x + e</returns>
    public static float ApplyQuarticFit(float a, float b, float c, float d, float e, float x)
    {
        float x2 = x * x;
        return a * x2 * x2 + b * x2 * x + c * x2 + d * x + e;
    }
}

public sealed partial class Pix
{
    /*---------------------------------------------------------------------*
     *                        Interconversions with Pix                    *
     *---------------------------------------------------------------------*/
    private static int plotCount = 0;  /* require separate temp files for each call */

    // leptonica: pixPlotAlongPta (ptafunc1.c)
    /// <summary>Plots the pixel values of this pix (any depth) along the points of pta.</summary>
    /// <param name="pta">set of points on which to plot</param>
    /// <param name="outformat">GPLOT_PNG, GPLOT_PS, GPLOT_EPS, GPLOT_LATEX</param>
    /// <param name="title">for plot; can be null</param>
    /// <remarks>
    /// (1) This is a debugging function.
    /// (2) Removes existing colormaps and clips the pta to the input pix.
    /// (3) If the image is RGB, three separate plots are generated.
    /// </remarks>
    public void PlotAlongPta(Pta pta, int outformat, string? title)
    {
        Utils.LeptMkdir("lept/plot");

        if (outformat != L.GPLOT_PNG && outformat != L.GPLOT_PS &&
            outformat != L.GPLOT_EPS && outformat != L.GPLOT_LATEX) {
            Log.Warning("pixPlotAlongPta", "outformat invalid; using GPLOT_PNG");
            outformat = L.GPLOT_PNG;
        }

        Pix pixt = RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        int d = pixt.Depth;
        int w = pixt.Width;
        int h = pixt.Height;
        int npts = pta.Count;
        string buffer;
        if (d == 32) {
            Numa nar = Numa.Create(npts);
            Numa nag = Numa.Create(npts);
            Numa nab = Numa.Create(npts);
            for (int i = 0; i < npts; i++) {
                pta.GetIPt(i, out int x, out int y);
                if (x < 0 || x >= w)
                    continue;
                if (y < 0 || y >= h)
                    continue;
                pixt.GetPixel(x, y, out uint val);
                int rval = GetDataByte(val, L.COLOR_RED);
                int gval = GetDataByte(val, L.COLOR_GREEN);
                int bval = GetDataByte(val, L.COLOR_BLUE);
                nar.AddNumber(rval);
                nag.AddNumber(gval);
                nab.AddNumber(bval);
            }

            buffer = CFormat.Sprintf("/tmp/lept/plot/%03d", plotCount++);
            string rtitle = Utils.StringJoin("Red: ", title);
            Gplot.Simple1(nar, outformat, buffer, rtitle);
            buffer = CFormat.Sprintf("/tmp/lept/plot/%03d", plotCount++);
            string gtitle = Utils.StringJoin("Green: ", title);
            Gplot.Simple1(nag, outformat, buffer, gtitle);
            buffer = CFormat.Sprintf("/tmp/lept/plot/%03d", plotCount++);
            string btitle = Utils.StringJoin("Blue: ", title);
            Gplot.Simple1(nab, outformat, buffer, btitle);
        } else {
            Numa na = Numa.Create(npts);
            for (int i = 0; i < npts; i++) {
                pta.GetIPt(i, out int x, out int y);
                if (x < 0 || x >= w)
                    continue;
                if (y < 0 || y >= h)
                    continue;
                pixt.GetPixel(x, y, out uint val);
                na.AddNumber((float)val);
            }

            buffer = CFormat.Sprintf("/tmp/lept/plot/%03d", plotCount++);
            Gplot.Simple1(na, outformat, buffer, title);
        }
    }
}

public sealed partial class Pta
{
    // leptonica: ptaGetPixelsFromPix (ptafunc1.c)
    /// <param name="pixs">1 bpp</param>
    /// <param name="box">can be null</param>
    /// <remarks>
    /// (1) Generates a pta of fg pixels in the pix, within the box.
    ///     If box == null, it uses the entire pix.
    /// (2) The box must lie within pixs; C reads outside the image otherwise.
    /// </remarks>
    public static Pta GetPixelsFromPix(Pix pixs, Box? box)
    {
        if (pixs.Depth != 1)
            throw Lept.Arg("ptaGetPixelsFromPix", "pixs undefined or not 1 bpp");

        pixs.GetDimensions(out int w, out int h, out _);
        uint[] data = pixs.Data;
        int wpl = pixs.Wpl;
        int xstart = 0;
        int ystart = 0;
        int xend = w - 1;
        int yend = h - 1;
        if (box != null) {
            box.GetGeometry(out xstart, out ystart, out int bw, out int bh);
            xend = xstart + bw - 1;
            yend = ystart + bh - 1;
        }

        Pta pta = Create(0);
        for (int i = ystart; i <= yend; i++) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = xstart; j <= xend; j++) {
                if (GetDataBit(line, j) != 0)
                    pta.AddPt(j, i);
            }
        }

        return pta;
    }
}

public sealed partial class Pix
{
    // leptonica: pixGenerateFromPta (ptafunc1.c)
    /// <param name="pta"></param>
    /// <param name="w">of pix</param>
    /// <param name="h">of pix</param>
    /// <returns>pix 1 bpp</returns>
    /// <remarks>
    /// (1) Points are rounded to nearest ints.
    /// (2) Any points outside (w,h) are silently discarded.
    /// (3) Output 1 bpp pix has values 1 for each point in the pta.
    /// </remarks>
    public static Pix GenerateFromPta(Pta pta, int w, int h)
    {
        Pix pix = Create(w, h, 1);
        int n = pta.Count;
        for (int i = 0; i < n; i++) {
            pta.GetIPt(i, out int x, out int y);
            if (x < 0 || x >= w || y < 0 || y >= h)
                continue;
            pix.SetPixel(x, y, 1);
        }

        return pix;
    }
}

public sealed partial class Pta
{
    // leptonica: ptaGetBoundaryPixels (ptafunc1.c)
    /// <param name="pixs">1 bpp</param>
    /// <param name="type">L_BOUNDARY_FG, L_BOUNDARY_BG</param>
    /// <remarks>
    /// (1) This generates a pta of either fg or bg boundary pixels.
    /// (2) See also pixGeneratePtaBoundary() for rendering of
    ///     fg boundary pixels.
    /// </remarks>
    public static Pta GetBoundaryPixels(Pix pixs, int type)
    {
        if (pixs.Depth != 1)
            throw Lept.Arg("ptaGetBoundaryPixels", "pixs undefined or not 1 bpp");
        if (type != L.L_BOUNDARY_FG && type != L.L_BOUNDARY_BG)
            throw Lept.Arg("ptaGetBoundaryPixels", "invalid type");

        Pix pixt;
        if (type == L.L_BOUNDARY_FG)
            pixt = pixs.MorphSequence("e3.3", 0);
        else
            pixt = pixs.MorphSequence("d3.3", 0);
        Pix.Xor(pixt, pixt, pixs);
        Pta pta = GetPixelsFromPix(pixt, null);

        return pta;
    }
}

public sealed partial class Ptaa
{
    // leptonica: ptaaGetBoundaryPixels (ptafunc1.c)
    /// <param name="pixs">1 bpp</param>
    /// <param name="type">L_BOUNDARY_FG, L_BOUNDARY_BG</param>
    /// <param name="connectivity">4 or 8</param>
    /// <param name="boxa">bounding boxes of the c.c.</param>
    /// <param name="pixa">pixa of the c.c.</param>
    /// <remarks>
    /// (1) This generates a ptaa of either fg or bg boundary pixels,
    ///     where each pta has the boundary pixels for a connected
    ///     component.
    /// (2) We can't simply find all the boundary pixels and then select
    ///     those within the bounding box of each component, because
    ///     bounding boxes can overlap.  It is necessary to extract and
    ///     dilate or erode each component separately.  Note also that
    ///     special handling is required for bg pixels when the
    ///     component touches the pix boundary.
    /// </remarks>
    public static Ptaa GetBoundaryPixels(Pix pixs, int type, int connectivity, out Boxa boxa,
                                         out Pixa pixa)
    {
        boxa = null!;
        pixa = null!;
        if (pixs.Depth != 1)
            throw Lept.Arg("ptaaGetBoundaryPixels", "pixs undefined or not 1 bpp");
        if (type != L.L_BOUNDARY_FG && type != L.L_BOUNDARY_BG)
            throw Lept.Arg("ptaaGetBoundaryPixels", "invalid type");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg("ptaaGetBoundaryPixels", "connectivity not 4 or 8");

        pixs.GetDimensions(out int w, out int h, out _);
        Boxa boxac = pixs.ConnComp(out Pixa pixac, connectivity);
        int n = boxac.Count;
        Ptaa ptaa = Create(0);
        for (int i = 0; i < n; i++) {
            Pix pixt1 = pixac.GetPix(i, L.L_CLONE);
            boxac.GetBoxGeometry(i, out int x, out int y, out int bw, out int bh);
            int left = 0, right = 0, top = 0, bot = 0;
            Pix pixt2;
            if (type == L.L_BOUNDARY_BG) {
                if (x > 0) left = 1;
                if (y > 0) top = 1;
                if (x + bw < w) right = 1;
                if (y + bh < h) bot = 1;
                pixt2 = pixt1.AddBorderGeneral(left, right, top, bot, 0);
            } else {
                pixt2 = Pix.Clone(pixt1);
            }
            Pta pta1 = Pta.GetBoundaryPixels(pixt2, type);
            Pta pta2 = pta1.Transform(x - left, y - top, 1.0f, 1.0f);
            ptaa.AddPta(pta2, L.L_INSERT);
        }

        boxa = boxac;
        pixa = pixac;
        return ptaa;
    }

    // leptonica: ptaaIndexLabeledPixels (ptafunc1.c)
    /// <param name="pixs">32 bpp, of indices of c.c.</param>
    /// <param name="ncc">number of connected components</param>
    /// <remarks>
    /// (1) The pixel values in pixs are the index of the connected component
    ///     to which the pixel belongs; pixs is typically generated from
    ///     a 1 bpp pix by pixConnCompTransform().  Background pixels in
    ///     the generating 1 bpp pix are represented in pixs by 0.
    ///     We do not check that the pixel values are correctly labelled.
    /// (2) Each pta in the returned ptaa gives the pixel locations
    ///     corresponding to a connected component, with the label of each
    ///     given by the index of the pta into the ptaa.
    /// (3) Initialize with the first pta in ptaa being empty and
    ///     representing the background value (index 0) in the pix.
    /// </remarks>
    public static Ptaa IndexLabeledPixels(Pix pixs, out int ncc)
    {
        ncc = 0;
        if (pixs.Depth != 32)
            throw Lept.Arg("ptaaIndexLabeledPixels", "pixs undef or not 32 bpp");

            /* The number of c.c. is the maximum pixel value.  Use this to
             * initialize ptaa with sufficient pta arrays */
        pixs.GetMaxValueInRect(null, out uint maxval, out _, out _);
        ncc = (int)maxval;
        Pta pta = Pta.Create(1);
        Ptaa ptaa = Create((int)(maxval + 1));
        ptaa.InitFull(pta);

            /* Sweep over pixs, saving the pixel coordinates of each pixel
             * with nonzero value in the appropriate pta, indexed by that value. */
        pixs.GetDimensions(out int w, out int h, out _);
        uint[] data = pixs.Data;
        int wpl = pixs.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> line = data.AsSpan(wpl * i, wpl);
            for (int j = 0; j < w; j++) {
                int index = (int)line[j];
                if (index > 0)
                    ptaa.AddPt(index, j, i);
            }
        }

        return ptaa;
    }
}

public sealed partial class Pta
{
    // leptonica: ptaGetNeighborPixLocs (ptafunc1.c)
    /// <param name="pixs">any depth</param>
    /// <param name="x">pixel from which we search for nearest neighbors</param>
    /// <param name="y">pixel from which we search for nearest neighbors</param>
    /// <param name="conn">4 or 8 connectivity</param>
    /// <remarks>
    /// (1) Generates a pta of all valid neighbor pixel locations.
    /// </remarks>
    public static Pta GetNeighborPixLocs(Pix pixs, int x, int y, int conn)
    {
        pixs.GetDimensions(out int w, out int h, out _);
        if (x < 0 || x >= w || y < 0 || y >= h)
            throw Lept.Arg("ptaGetNeighborPixLocs", "(x,y) not in pixs");
        if (conn != 4 && conn != 8)
            throw Lept.Arg("ptaGetNeighborPixLocs", "conn not 4 or 8");

        Pta pta = Create(conn);
        if (x > 0)
            pta.AddPt(x - 1, y);
        if (x < w - 1)
            pta.AddPt(x + 1, y);
        if (y > 0)
            pta.AddPt(x, y - 1);
        if (y < h - 1)
            pta.AddPt(x, y + 1);
        if (conn == 8) {
            if (x > 0) {
                if (y > 0)
                    pta.AddPt(x - 1, y - 1);
                if (y < h - 1)
                    pta.AddPt(x - 1, y + 1);
            }
            if (x < w - 1) {
                if (y > 0)
                    pta.AddPt(x + 1, y - 1);
                if (y < h - 1)
                    pta.AddPt(x + 1, y + 1);
            }
        }

        return pta;
    }
}

public sealed partial class Numa
{
    /*---------------------------------------------------------------------*
     *                    Interconversion with Numa                        *
     *---------------------------------------------------------------------*/
    // leptonica: numaConvertToPta1 (ptafunc1.c)
    /// <summary>Converts this numa, with implicit y(x), to a pta.</summary>
    public Pta ConvertToPta1()
    {
        int n = Count;
        Pta pta = Pta.Create(n);
        GetParameters(out float startx, out float delx);
        for (int i = 0; i < n; i++) {
            float val = GetFValue(i);
            pta.AddPt(startx + i * delx, val);
        }
        return pta;
    }

    // leptonica: numaConvertToPta2 (ptafunc1.c)
    /// <summary>Converts this numa (nax) and nay to a pta of (nax[i], nay[i]).</summary>
    /// <param name="nay"></param>
    public Pta ConvertToPta2(Numa nay)
    {
        int nx = Count;
        int ny = nay.Count;
        int n = Math.Min(nx, ny);
        if (nx != ny)
            Log.Warning("numaConvertToPta2", $"nx = {nx} does not equal ny = {ny}");
        Pta pta = Pta.Create(n);
        for (int i = 0; i < n; i++) {
            float valx = GetFValue(i);
            float valy = nay.GetFValue(i);
            pta.AddPt(valx, valy);
        }
        return pta;
    }
}

public sealed partial class Pta
{
    // leptonica: ptaConvertToNuma (ptafunc1.c)
    /// <param name="nax">x values</param>
    /// <param name="nay">y values</param>
    public void ConvertToNuma(out Numa nax, out Numa nay)
    {
        int n = Count;
        nax = Numa.Create(n);
        nay = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            GetPt(i, out float valx, out float valy);
            nax.AddNumber(valx);
            nay.AddNumber(valy);
        }
    }
}

public sealed partial class Pix
{
    /*---------------------------------------------------------------------*
     *                          Display Pta and Ptaa                       *
     *---------------------------------------------------------------------*/
    // leptonica: pixDisplayPta (ptafunc1.c)
    /// <param name="pixd">can be same as pixs or null; 32 bpp if in-place</param>
    /// <param name="pixs">1, 2, 4, 8, 16 or 32 bpp</param>
    /// <param name="pta">of path to be plotted</param>
    /// <returns>pixd 32 bpp RGB version of pixs, with path in green.</returns>
    /// <remarks>
    /// (1) To write on an existing pixs, pixs must be 32 bpp and
    ///     call with pixd == pixs:
    ///        pixDisplayPta(pixs, pixs, pta);
    ///     To write to a new pix, use pixd == null and call:
    ///        pixd = pixDisplayPta(null, pixs, pta);
    /// (2) The start point is red, the end point is blue.
    /// </remarks>
    public static Pix DisplayPta(Pix? pixd, Pix pixs, Pta pta)
    {
        if (pixd != null && (!ReferenceEquals(pixd, pixs) || pixd.Depth != 32))
            throw Lept.Arg("pixDisplayPta", "invalid pixd");

        if (pixd == null)
            pixd = pixs.ConvertTo32();
        pixd.GetDimensions(out int w, out int h, out _);
        ComposeRGBPixel(255, 0, 0, out uint rpixel);  /* start point */
        ComposeRGBPixel(0, 255, 0, out uint gpixel);
        ComposeRGBPixel(0, 0, 255, out uint bpixel);  /* end point */

        int n = pta.Count;
        for (int i = 0; i < n; i++) {
            pta.GetIPt(i, out int x, out int y);
            if (x < 0 || x >= w || y < 0 || y >= h)
                continue;
            if (i == 0)
                pixd.SetPixel(x, y, rpixel);
            else if (i < n - 1)
                pixd.SetPixel(x, y, gpixel);
            else
                pixd.SetPixel(x, y, bpixel);
        }

        return pixd;
    }

    // leptonica: pixDisplayPtaaPattern (ptafunc1.c)
    /// <param name="pixd">32 bpp</param>
    /// <param name="pixs">1, 2, 4, 8, 16 or 32 bpp; 32 bpp if in place</param>
    /// <param name="ptaa">giving locations at which the pattern is displayed</param>
    /// <param name="pixp">1 bpp pattern to be placed such that its reference point co-locates with each point in pta</param>
    /// <param name="cx">reference point in pattern</param>
    /// <param name="cy">reference point in pattern</param>
    /// <returns>pixd 32 bpp RGB version of pixs.</returns>
    /// <remarks>
    /// (1) To write on an existing pixs, pixs must be 32 bpp and
    ///     call with pixd == pixs:
    ///        pixDisplayPtaPattern(pixs, pixs, pta, ...);
    ///     To write to a new pix, use pixd == null and call:
    ///        pixd = pixDisplayPtaPattern(null, pixs, pta, ...);
    /// (2) Puts a random color on each pattern associated with a pta.
    /// (3) A typical pattern to be used is a circle, generated with
    ///        generatePtaFilledCircle()
    /// </remarks>
    public static Pix DisplayPtaaPattern(Pix? pixd, Pix pixs, Ptaa ptaa, Pix pixp, int cx, int cy)
    {
        if (pixd != null && (!ReferenceEquals(pixd, pixs) || pixd.Depth != 32))
            throw Lept.Arg("pixDisplayPtaaPattern", "invalid pixd");

        if (pixd == null)
            pixd = pixs.ConvertTo32();

            /* Use 256 random colors */
        PixColormap cmap = PixColormap.CreateRandom(8, false, false);
        int n = ptaa.Count;
        for (int i = 0; i < n; i++) {
            uint color = cmap.GetColor32(i % 256);
            Pta pta = ptaa.GetPta(i, L.L_CLONE);
            DisplayPtaPattern(pixd, pixd, pta, pixp, cx, cy, color);
        }

        return pixd;
    }

    // leptonica: pixDisplayPtaPattern (ptafunc1.c)
    /// <param name="pixd">can be same as pixs or null; 32 bpp if in-place</param>
    /// <param name="pixs">1, 2, 4, 8, 16 or 32 bpp</param>
    /// <param name="pta">giving locations at which the pattern is displayed</param>
    /// <param name="pixp">1 bpp pattern to be placed such that its reference point co-locates with each point in pta</param>
    /// <param name="cx">reference point in pattern</param>
    /// <param name="cy">reference point in pattern</param>
    /// <param name="color">in 0xrrggbb00 format</param>
    /// <returns>pixd 32 bpp RGB version of pixs.</returns>
    /// <remarks>
    /// (1) To write on an existing pixs, pixs must be 32 bpp and
    ///     call with pixd == pixs:
    ///        pixDisplayPtaPattern(pixs, pixs, pta, ...);
    ///     To write to a new pix, use pixd == null and call:
    ///        pixd = pixDisplayPtaPattern(null, pixs, pta, ...);
    /// (2) A typical pattern to be used is a circle, generated with
    ///        generatePtaFilledCircle()
    /// </remarks>
    public static Pix DisplayPtaPattern(Pix? pixd, Pix pixs, Pta pta, Pix pixp, int cx, int cy,
                                        uint color)
    {
        if (pixd != null && (!ReferenceEquals(pixd, pixs) || pixd.Depth != 32))
            throw Lept.Arg("pixDisplayPtaPattern", "invalid pixd");

        if (pixd == null)
            pixd = pixs.ConvertTo32();
        pixs.GetDimensions(out int w, out int h, out _);
        Pta ptat = pta.ReplicatePattern(pixp, null, cx, cy, w, h);

        int n = ptat.Count;
        for (int i = 0; i < n; i++) {
            ptat.GetIPt(i, out int x, out int y);
            if (x < 0 || x >= w || y < 0 || y >= h)
                continue;
            pixd.SetPixel(x, y, color);
        }

        return pixd;
    }
}

public sealed partial class Pta
{
    // leptonica: ptaReplicatePattern (ptafunc1.c)
    /// <summary>Replicates a pattern at each point of this "sparse" pta.</summary>
    /// <param name="pixp">1 bpp pattern, to be replicated in output pta; can be null</param>
    /// <param name="ptap">set of pts, to be replicated in output pta; can be null</param>
    /// <param name="cx">reference point in pattern</param>
    /// <param name="cy">reference point in pattern</param>
    /// <param name="w">clipping sizes for output pta</param>
    /// <param name="h">clipping sizes for output pta</param>
    /// <returns>ptad with all points of replicated pattern</returns>
    /// <remarks>
    /// (1) You can use either the image pixp or the set of pts ptap.
    /// (2) The pattern is placed with its reference point at each point
    ///     in ptas, and all the fg pixels are colleced into ptad.
    ///     For pixp, this is equivalent to blitting pixp at each point
    ///     in ptas, and then converting the resulting pix to a pta.
    /// </remarks>
    public Pta ReplicatePattern(Pix? pixp, Pta? ptap, int cx, int cy, int w, int h)
    {
        if (pixp == null && ptap == null)
            throw Lept.Arg("ptaReplicatePattern", "no pattern is defined");
        if (pixp != null && ptap != null)
            Log.Warning("ptaReplicatePattern", "pixp and ptap defined; using ptap");

        int n = Count;
        Pta ptad = Create(n);
        Pta ptat;
        if (ptap != null)
            ptat = ptap.Clone();
        else
            ptat = GetPixelsFromPix(pixp!, null);
        int np = ptat.Count;
        for (int i = 0; i < n; i++) {
            GetIPt(i, out int x, out int y);
            for (int j = 0; j < np; j++) {
                ptat.GetIPt(j, out int xp, out int yp);
                int xf = x - cx + xp;
                int yf = y - cy + yp;
                if (xf >= 0 && xf < w && yf >= 0 && yf < h)
                    ptad.AddPt(xf, yf);
            }
        }

        return ptad;
    }
}

public sealed partial class Pix
{
    // leptonica: pixDisplayPtaa (ptafunc1.c)
    /// <param name="ptaa">array of paths to be plotted</param>
    /// <returns>pixd 32 bpp RGB version of pixs, with paths plotted in different colors</returns>
    public Pix DisplayPtaa(Ptaa ptaa)
    {
        int npta = ptaa.Count;
        if (npta == 0)
            throw Lept.Arg("pixDisplayPtaa", "no pta");

        Pix pixd = ConvertTo32();
        pixd.GetDimensions(out int w, out int h, out _);

            /* Make a colormap for the paths */
        uint[] pixela = new uint[npta];
        Numa na1 = Numa.PseudorandomSequence(256, 14657);
        Numa na2 = Numa.PseudorandomSequence(256, 34631);
        Numa na3 = Numa.PseudorandomSequence(256, 54617);
        for (int i = 0; i < npta; i++) {
            int rv = na1.GetIValue(i % 256);
            int gv = na2.GetIValue(i % 256);
            int bv = na3.GetIValue(i % 256);
            ComposeRGBPixel(rv, gv, bv, out pixela[i]);
        }

        for (int i = 0; i < npta; i++) {
            Pta pta = ptaa.GetPta(i, L.L_CLONE);
            int npt = pta.Count;
            for (int j = 0; j < npt; j++) {
                pta.GetIPt(j, out int x, out int y);
                if (x < 0 || x >= w || y < 0 || y >= h)
                    continue;
                pixd.SetPixel(x, y, pixela[i]);
            }
        }

        return pixd;
    }
}
