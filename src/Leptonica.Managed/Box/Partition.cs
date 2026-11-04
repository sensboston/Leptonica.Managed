using System;

namespace Leptonica;

/*
 *  partition.c: Breuel whitespace rectangle partitioning and overlap pruning.
 */

public sealed partial class Boxa
{
    private const int DefaultMaxPops = 20000;

    private sealed class PartEl : IHeapItem
    {
        public float size;
        public Box box;
        public Boxa? boxa;

        public float HeapKey => size;

        public PartEl(Box box)
        {
            this.box = box;
        }
    }

    /*------------------------------------------------------------------*
     *                    Whitespace block extraction                   *
     *------------------------------------------------------------------*/
    /// <summary>Finds the largest rectangular regions of white space that do not intersect any of the boxes.</summary>
    /// <param name="box">
    /// The initial region, typically including all the boxes; if <c>null</c>, the minimal region that
    /// includes the origin and every box is used.
    /// </param>
    /// <param name="sortflag">
    /// The ordering of the regions: <see cref="L.L_SORT_BY_WIDTH"/>, <see cref="L.L_SORT_BY_HEIGHT"/>,
    /// <see cref="L.L_SORT_BY_MIN_DIMENSION"/>, <see cref="L.L_SORT_BY_MAX_DIMENSION"/>,
    /// <see cref="L.L_SORT_BY_PERIMETER"/> or <see cref="L.L_SORT_BY_AREA"/>.
    /// </param>
    /// <param name="maxboxes">The maximum number of white space boxes to return; for example, 100.</param>
    /// <param name="maxoverlap">
    /// The maximum fractional overlap of a box by any of the larger boxes; for example, 0.2.
    /// </param>
    /// <param name="maxperim">
    /// The maximum half-perimeter, in pixels, for which the pivot is selected by proximity to the
    /// box centroid; for example, 200.
    /// </param>
    /// <param name="fract">
    /// The fraction of the box diagonal that is an acceptable distance from the box centroid when
    /// selecting the pivot, in the range [0.0, 1.0]; for example, 0.2.
    /// </param>
    /// <param name="maxpops">The maximum number of pops from the heap; use 0 for the default.</param>
    /// <returns>The sorted white space boxes.</returns>
    /// <exception cref="ArgumentException">An argument is invalid.</exception>
    /// <remarks>
    /// <para>
    /// This uses the Breuel algorithm described in "Two Geometric Algorithms for Layout Analysis" (2002).
    /// It starts with the bounding boxes of the connected components in a region, along with the
    /// rectangle representing that region, and repeatedly divides the rectangle into four maximal
    /// rectangles that exclude a pivot rectangle. The pieces are kept in a priority queue ordered by
    /// <paramref name="sortflag"/>, and the "largest" set that has no intersection with the input boxes is returned.
    /// </para>
    /// <para>
    /// The actual number of boxes returned depends on the image and on <paramref name="maxoverlap"/> and
    /// <paramref name="maxpops"/>. To avoid all pruning use <paramref name="maxoverlap"/> = 1.0; to keep only
    /// boxes that do not overlap each other use 0.0.
    /// </para>
    /// <para>
    /// Choose <paramref name="maxperim"/> so that a component with a smaller half-perimeter is small enough
    /// that the white space inside it does not matter; for such components the pivot is chosen by proximity
    /// to the rectangle centroid. With <paramref name="fract"/> = 0.0 the small box nearest the centroid is used;
    /// with a larger value, permuting the boxes randomly first can reduce the search time.
    /// </para>
    /// <para>
    /// <paramref name="maxpops"/> indirectly limits the execution time. The heap typically holds about 2.5 times
    /// as many boxes as have been popped off.
    /// </para>
    /// <para>
    /// Only the bounding boxes of the components are used, so the interior of every box is excluded from the
    /// returned white space. It can help to remove very large boxes before the call.
    /// </para>
    /// </remarks>
    // leptonica: boxaGetWhiteblocks (partition.c)
    public Boxa GetWhiteblocks(Box? box, int sortflag, int maxboxes,
                               float maxoverlap, int maxperim,
                               float fract, int maxpops)
    {
        const string func = "boxaGetWhiteblocks";
        if (sortflag != L.L_SORT_BY_WIDTH && sortflag != L.L_SORT_BY_HEIGHT &&
            sortflag != L.L_SORT_BY_MIN_DIMENSION &&
            sortflag != L.L_SORT_BY_MAX_DIMENSION &&
            sortflag != L.L_SORT_BY_PERIMETER && sortflag != L.L_SORT_BY_AREA)
            throw Lept.Arg(func, "invalid sort flag");
        if (maxboxes < 1) {
            maxboxes = 1;
            Log.Warning(func, "setting maxboxes = 1");
        }
        if (maxoverlap < 0.0f || maxoverlap > 1.0f)
            throw Lept.Arg(func, "invalid maxoverlap");
        if (maxpops == 0)
            maxpops = DefaultMaxPops;

        Box regionBox;
        if (box == null) {
            GetExtent(out int w, out int h, out _);
            regionBox = Box.Create(0, 0, w, h);
        } else {
            regionBox = box;
        }

            /* Prime the heap */
        L_Heap lh = L_Heap.Create(20, L.L_SORT_DECREASING);
        PartEl partel = PartelCreate(regionBox);
        partel.boxa = Copy(L.L_CLONE);
        PartelSetSize(partel, sortflag);
        lh.Add(partel);

        int npop = 0;
        Boxa boxad = Create(0);
        while (true) {
            PartEl? cur = (PartEl?)lh.Remove();
            if (cur == null)
                break;

            npop++;
            if (npop > maxpops)
                break;

            Boxa curBoxa = cur.boxa!.Copy(L.L_CLONE);
            Box curBox = cur.box.Clone();

            int count = curBoxa.Count;
            if (count == 0) {
                if (CheckIfOverlapIsBig(curBox, boxad, maxoverlap) == 0)
                    boxad.AddBox(curBox, L.L_INSERT);
                if (boxad.Count >= maxboxes)
                    break;
                continue;
            }

            Boxa boxa4 = GenerateSubboxes(curBox, curBoxa, maxperim, fract);
            int nsub = boxa4.Count;
            for (int i = 0; i < nsub; i++) {
                Box boxsub = boxa4.GetBox(i, L.L_CLONE);
                Boxa boxasub = curBoxa.IntersectsBox(boxsub);
                PartEl subEl = PartelCreate(boxsub);
                subEl.boxa = boxasub;
                PartelSetSize(subEl, sortflag);
                lh.Add(subEl);
            }
        }

        return boxad;
    }

    /*------------------------------------------------------------------*
     *                               Helpers                            *
     *------------------------------------------------------------------*/
    // leptonica: partelCreate (partition.c)
    private static PartEl PartelCreate(Box box)
    {
        return new PartEl(box.Copy());
    }

    // leptonica: partelSetSize (partition.c)
    private static int PartelSetSize(PartEl partel, int sortflag)
    {
        const string func = "partelSetSize";
        partel.box.GetGeometry(out _, out _, out int w, out int h);
        if (sortflag == L.L_SORT_BY_WIDTH)
            partel.size = (float)w;
        else if (sortflag == L.L_SORT_BY_HEIGHT)
            partel.size = (float)h;
        else if (sortflag == L.L_SORT_BY_MIN_DIMENSION)
            partel.size = (float)Math.Min(w, h);
        else if (sortflag == L.L_SORT_BY_MAX_DIMENSION)
            partel.size = (float)Math.Max(w, h);
        else if (sortflag == L.L_SORT_BY_PERIMETER)
            partel.size = (float)(w + h);
        else if (sortflag == L.L_SORT_BY_AREA)
            partel.size = (float)w * (float)h;
        else {
            Log.Error(func, "invalid sortflag");
            return 1;
        }
        return 0;
    }

    // leptonica: boxaGenerateSubboxes (partition.c)
    private static Boxa GenerateSubboxes(Box box, Boxa boxa, int maxperim, float fract)
    {
        Boxa boxa4 = Create(4);
        Box boxp = SelectPivotBox(box, boxa, maxperim, fract);
        box.GetGeometry(out int x, out int y, out int w, out int h);
        boxp.GetGeometry(out int xp, out int yp, out int wp, out int hp);
        if (xp > x)
            boxa4.AddBox(Box.Create(x, y, xp - x, h), L.L_INSERT);
        if (yp > y)
            boxa4.AddBox(Box.Create(x, y, w, yp - y), L.L_INSERT);
        if (xp + wp < x + w)
            boxa4.AddBox(Box.Create(xp + wp, y, x + w - xp - wp, h), L.L_INSERT);
        if (yp + hp < y + h)
            boxa4.AddBox(Box.Create(x, yp + hp, w, y + h - yp - hp), L.L_INSERT);

        return boxa4;
    }

    // leptonica: boxaSelectPivotBox (partition.c)
    private static Box SelectPivotBox(Box box, Boxa boxa, int maxperim, float fract)
    {
        const string func = "boxaSelectPivotBox";
        int count = boxa.Count;
        if (count == 0)
            throw Lept.Arg(func, "no boxes in boxa");
        if (fract < 0.0f || fract > 1.0f) {
            Log.Warning(func, "fract out of bounds; using 0.0");
            fract = 0.0f;
        }

        box.GetGeometry(out _, out _, out int w, out int h);
        box.GetCenter(out float x, out float y);
        float threshdist = fract * ((float)w * w + (float)h * h);
        float mindist = 1000000000.0f;
        int minindex = 0;
        bool smallfound = false;
        for (int i = 0; i < count; i++) {
            Box boxt = boxa.GetBox(i, L.L_CLONE);
            boxt.GetGeometry(out _, out _, out int bw, out int bh);
            boxt.GetCenter(out float cx, out float cy);
            if (bw + bh > maxperim)
                continue;
            smallfound = true;
            float delx = cx - x;
            float dely = cy - y;
            float dist = delx * delx + dely * dely;
            if (dist <= threshdist)
                return boxa.GetBox(i, L.L_COPY);
            if (dist < mindist) {
                minindex = i;
                mindist = dist;
            }
        }

        if (smallfound)
            return boxa.GetBox(minindex, L.L_COPY);

        int minsize = 1000000000;
        minindex = 0;
        for (int i = 0; i < count; i++) {
            boxa.GetBoxGeometry(i, out _, out _, out int bw, out int bh);
            int perim = bw + bh;
            if (perim < minsize) {
                minsize = perim;
                minindex = i;
            }
        }
        return boxa.GetBox(minindex, L.L_COPY);
    }

    // leptonica: boxCheckIfOverlapIsBig (partition.c)
    private static int CheckIfOverlapIsBig(Box box, Boxa boxa, float maxoverlap)
    {
        if (maxoverlap < 0.0f || maxoverlap > 1.0f)
            return 1;

        int count = boxa.Count;
        if (count == 0 || maxoverlap == 1.0f)
            return 0;

        for (int i = 0; i < count; i++) {
            Box boxt = boxa.GetBox(i, L.L_CLONE);
            float fract = boxt.OverlapFraction(box);
            if (fract > maxoverlap)
                return 1;
        }
        return 0;
    }

    /// <summary>Removes the smaller boxes that are overlapped by a larger box by more than the given fraction.</summary>
    /// <param name="maxoverlap">The maximum fractional overlap of a box by any of the larger boxes, in the range [0.0, 1.0].</param>
    /// <returns>The pruned boxes; an empty array if there are no boxes.</returns>
    /// <exception cref="ArgumentException"><paramref name="maxoverlap"/> is outside the range [0.0, 1.0].</exception>
    /// <remarks>
    /// The boxes must be sorted by size in decreasing order. To avoid all pruning use
    /// <paramref name="maxoverlap"/> = 1.0; to keep only boxes that do not overlap each other use 0.0.
    /// </remarks>
    // leptonica: boxaPruneSortedOnOverlap (partition.c)
    public Boxa PruneSortedOnOverlap(float maxoverlap)
    {
        const string func = "boxaPruneSortedOnOverlap";
        if (maxoverlap < 0.0f || maxoverlap > 1.0f)
            throw Lept.Arg(func, "invalid maxoverlap");

        int count = Count;
        if (count == 0 || maxoverlap == 1.0f)
            return Copy(L.L_COPY);

        Boxa boxad = Create(0);
        boxad.AddBox(GetBox(0, L.L_COPY), L.L_INSERT);
        for (int j = 1; j < count; j++) {
            Box box2 = GetBox(j, L.L_COPY);
            bool remove = false;
            for (int i = 0; i < j; i++) {
                Box box1 = GetBox(i, L.L_CLONE);
                if (box1.OverlapFraction(box2) > maxoverlap) {
                    remove = true;
                    break;
                }
            }
            if (!remove)
                boxad.AddBox(box2, L.L_INSERT);
        }

        return boxad;
    }
}
