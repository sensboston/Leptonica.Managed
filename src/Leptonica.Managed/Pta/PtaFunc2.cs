using System;

namespace Leptonica;

/*
 *  ptafunc2.c: Pta utilities: sorting, ordered set operations, hash map operations.
 *
 *      Sorting
 *           PTA        *ptaSort()
 *           l_int32     ptaGetSortIndex()
 *           PTA        *ptaSortByIndex()
 *           PTAA       *ptaaSortByIndex()
 *           l_int32     ptaGetRankValue()
 *           PTA        *ptaSort2d()
 *           l_int32     ptaEqual()
 *
 *      Set operations using aset (rbtree)
 *           L_ASET     *l_asetCreateFromPta()
 *           PTA        *ptaRemoveDupsByAset()
 *           PTA        *ptaUnionByAset()
 *           PTA        *ptaIntersectionByAset()
 *
 *      Hashmap operations
 *          L_HASHMAP   *l_hmapCreateFromPta()
 *          l_int32      ptaRemoveDupsByHmap()
 *          l_int32      ptaUnionByHmap()
 *          l_int32      ptaIntersectionByHmap()
 *
 *  We have two implementations of set operations on an array of points:
 *
 *    (1) Using an underlying tree (rbtree)
 *        This uses a good 64 bit hashing function for the key,
 *        that is not expected to have hash collisions (and we do
 *        not test for them).  The tree is built up of the keys,
 *        values, and is traversed looking for the key in O(log n).
 *
 *    (2) Building a hashmap from the keys (hashmap)
 *        This uses a fast 64 bit hashing function for the key, which
 *        is then hashed into a hashtable.  Collisions of hashkeys are
 *        very rare, but the hashtable is designed to allow more than one
 *        hashitem in a table entry.  The hashitems are put in a list at
 *        each hashtable entry, which is traversed looking for the key.
 *
 *  Differences from C:
 *    - No ptaDestroy(), numaDestroy(), l_asetDestroy(), l_hmapDestroy(): the GC
 *      owns the objects (not ported).
 *    - Functions that return an error code or NULL in C throw instead.
 *    - Output pointers become return values or out parameters:
 *      the "set" functions return the resulting Pta.
 */

public sealed partial class Pta
{
    /*---------------------------------------------------------------------*
     *                               Sorting                               *
     *---------------------------------------------------------------------*/
    // leptonica: ptaSort (ptafunc2.c)
    /// <param name="sorttype">L_SORT_BY_X, L_SORT_BY_Y</param>
    /// <param name="sortorder">L_SORT_INCREASING, L_SORT_DECREASING</param>
    /// <param name="naindex">index of sorted order into original array</param>
    /// <returns>ptad sorted version of this pta</returns>
    public Pta Sort(int sorttype, int sortorder, out Numa naindex)
    {
        const string func = "ptaSort";
        Pta ptas = this;

        if (sorttype != L.L_SORT_BY_X && sorttype != L.L_SORT_BY_Y)
            throw Lept.Arg(func, "invalid sort type");
        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg(func, "invalid sort order");

        naindex = ptas.GetSortIndex(sorttype, sortorder);

        Pta ptad = ptas.SortByIndex(naindex);
        return ptad;
    }

    // leptonica: ptaGetSortIndex (ptafunc2.c)
    /// <param name="sorttype">L_SORT_BY_X, L_SORT_BY_Y</param>
    /// <param name="sortorder">L_SORT_INCREASING, L_SORT_DECREASING</param>
    /// <returns>naindex   index of sorted order into original array</returns>
    public Numa GetSortIndex(int sorttype, int sortorder)
    {
        const string func = "ptaGetSortIndex";
        Pta ptas = this;

        if (sorttype != L.L_SORT_BY_X && sorttype != L.L_SORT_BY_Y)
            throw Lept.Arg(func, "invalid sort type");
        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg(func, "invalid sort order");

            /* Build up numa of specific data */
        int n = ptas.Count;
        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            ptas.GetPt(i, out float x, out float y);
            if (sorttype == L.L_SORT_BY_X)
                na.AddNumber(x);
            else
                na.AddNumber(y);
        }

            /* Get the sort index for data array */
        Numa nai = na.GetSortIndex(sortorder);
        return nai;
    }

    // leptonica: ptaSortByIndex (ptafunc2.c)
    /// <param name="naindex">na that maps from the new pta to this (input) pta</param>
    /// <returns>ptad sorted</returns>
    public Pta SortByIndex(Numa naindex)
    {
        Pta ptas = this;

            /* Build up sorted pta using sort index */
        int n = naindex.Count;
        Pta ptad = Create(n);
        for (int i = 0; i < n; i++) {
            int index = naindex.GetIValue(i);
            ptas.GetPt(index, out float x, out float y);
            ptad.AddPt(x, y);
        }

        return ptad;
    }

    // leptonica: ptaGetRankValue (ptafunc2.c)
    /// <param name="fract">use 0.0 for smallest, 1.0 for largest</param>
    /// <param name="ptasort">[optional] version of this pta sorted by sorttype</param>
    /// <param name="sorttype">L_SORT_BY_X, L_SORT_BY_Y</param>
    /// <returns>rankval: the x or y value at fract</returns>
    public float GetRankValue(float fract, Pta? ptasort, int sorttype)
    {
        const string func = "ptaGetRankValue";
        Pta pta = this;

        if (sorttype != L.L_SORT_BY_X && sorttype != L.L_SORT_BY_Y)
            throw Lept.Arg(func, "invalid sort type");
        if (fract < 0.0 || fract > 1.0)
            throw Lept.Arg(func, "fract not in [0.0 ... 1.0]");
        int n;
        if ((n = pta.Count) == 0)
            throw Lept.Arg(func, "pta empty");

        Pta ptas;
        if (ptasort != null)
            ptas = ptasort;
        else
            ptas = pta.Sort(sorttype, L.L_SORT_INCREASING, out _);

        int index = (int)(fract * (float)(n - 1) + 0.5);
        float val;
        if (sorttype == L.L_SORT_BY_X)
            ptas.GetPt(index, out val, out _);
        else  /* sort by y */
            ptas.GetPt(index, out _, out val);

        return val;
    }

    // leptonica: ptaSort2d (ptafunc2.c)
    /// <returns>ptad</returns>
    /// <remarks>
    /// (1) Sort increasing by row-major, scanning down from the UL corner,
    ///     where for each value of y, order the pts from left to right.
    /// </remarks>
    public Pta Sort2d()
    {
        Pta pta = this;
        float x = 0, y = 0, yp = 0;

            /* Sort by row-major (y first, then x).  After sort by y,
             * the x values at the same y are not sorted.  */
        Pta pta1 = pta.Sort(L.L_SORT_BY_Y, L.L_SORT_INCREASING, out _);

            /* Find start and ending indices with the same y value */
        int n = pta1.Count;
        Numa na1 = Numa.Create(0);  /* holds start index of sequence with same y */
        Numa na2 = Numa.Create(0);  /* holds end index of sequence with same y */
        na1.AddNumber(0);
        if (n > 0)
            pta1.GetPt(0, out x, out yp);
        else  /* C: ptaGetPt() logs an error and the function carries on */
            Log.Error("ptaGetPt", "invalid index");
        for (int i = 1; i < n; i++) {
            pta1.GetPt(i, out x, out y);
            if (y != yp) {
                na1.AddNumber(i);
                na2.AddNumber(i - 1);
            }
            yp = y;
        }
        na2.AddNumber(n - 1);

            /* Sort by increasing x each set with the same y value */
        Pta ptad = Create(n);
        int ny = na1.Count;   /* number of distinct y values */
        int index = 0;
        for (int i = 0; i < ny; i++) {
            int start = na1.GetIValue(i);
            int end = na2.GetIValue(i);
            int nx = end - start + 1;  /* number of points with current y value */
            if (nx == 1) {
                pta1.GetPt(index++, out x, out y);
                ptad.AddPt(x, y);
            } else {
                    /* More than 1 point; extract and sort the x values */
                Numa nax = Numa.Create(nx);
                for (int j = 0; j < nx; j++) {
                    pta1.GetPt(index + j, out x, out y);
                    nax.AddNumber(x);
                }
                Numa nas = Numa.Sort(null, nax, L.L_SORT_INCREASING);
                    /* Add the points with x sorted */
                for (int j = 0; j < nx; j++) {
                    float val = nas.GetFValue(j);
                    ptad.AddPt(val, y);
                }
                index += nx;
            }
        }
        return ptad;
    }

    // leptonica: ptaEqual (ptafunc2.c)
    /// <param name="pta2">the pta to compare with</param>
    /// <returns>true if same; false if different</returns>
    /// <remarks>
    /// (1) Equality is defined as having the same set of points,
    ///     independent of the order in which they are presented.
    /// </remarks>
    public bool Equal(Pta pta2)
    {
        Pta pta1 = this;

        int n1 = pta1.Count;
        int n2 = pta2.Count;
        if (n1 != n2) return false;

            /* 2d sort each and compare */
        Pta ptas1 = pta1.Sort2d();
        Pta ptas2 = pta2.Sort2d();
        for (int i = 0; i < n1; i++) {
            ptas1.GetPt(i, out float x1, out float y1);
            ptas2.GetPt(i, out float x2, out float y2);
            if (x1 != x2 || y1 != y2)
                return false;
        }

        return true;
    }

    /*---------------------------------------------------------------------*
     *                   Set operations using aset (rbtree)                *
     *---------------------------------------------------------------------*/
    // leptonica: ptaRemoveDupsByAset (ptafunc2.c)
    /// <returns>ptad   this pta (assumed to be integer values) without duplicates</returns>
    /// <remarks>
    /// (1) This is slower than ptaRemoveDupsByHmap(), mostly because
    ///     of the nlogn sort to build up the rbtree.  Do not use for
    ///     large numbers of points (say, > 100K).
    /// </remarks>
    public Pta RemoveDupsByAset()
    {
        Pta ptas = this;

        L_Aset set = L_Aset.Create(L.L_UINT_TYPE);
        int n = ptas.Count;
        Pta ptad = Create(n);
        for (int i = 0; i < n; i++) {
            ptas.GetIPt(i, out int x, out int y);
            ulong hash = Utils.HashPtToUint64(x, y);
            RbType key = default;
            key.Utype = hash;
            if (set.Find(key) == null) {
                ptad.AddPt(x, y);
                set.Insert(key);
            }
        }

        return ptad;
    }

    // leptonica: ptaUnionByAset (ptafunc2.c)
    /// <param name="pta2">second point array</param>
    /// <returns>ptad   union of the two point arrays</returns>
    /// <remarks>
    /// (1) See sarrayRemoveDupsByAset() for the approach.
    /// (2) The key is a 64-bit hash from the (x,y) pair.
    /// (3) This is slower than ptaUnionByHmap(), mostly because of the
    ///     nlogn sort to build up the rbtree.  Do not use for large
    ///     numbers of points (say, > 100K).
    /// (4) The *Aset() functions use the sorted l_Aset, which is just
    ///     an rbtree in disguise.
    /// </remarks>
    public Pta UnionByAset(Pta pta2)
    {
        Pta pta1 = this;

            /* Join */
        Pta pta3 = pta1.Copy();
        pta3.Join(pta2, 0, -1);

            /* Eliminate duplicates */
        Pta ptad = pta3.RemoveDupsByAset();
        return ptad;
    }

    // leptonica: ptaIntersectionByAset (ptafunc2.c)
    /// <param name="pta2">second point array</param>
    /// <returns>ptad   intersection of the two point arrays</returns>
    /// <remarks>
    /// (1) See sarrayIntersectionByAset() for the approach.
    /// (2) The key is a 64-bit hash from the (x,y) pair.
    /// (3) This is slower than ptaIntersectionByHmap(), mostly because
    ///     of the nlogn sort to build up the rbtree.  Do not use for
    ///     large numbers of points (say, > 100K).
    /// </remarks>
    public Pta IntersectionByAset(Pta pta2)
    {
        Pta pta1 = this;

            /* Put the elements of the biggest array into a set */
        int n1 = pta1.Count;
        int n2 = pta2.Count;
        Pta pta_small = (n1 < n2) ? pta1 : pta2;
        Pta pta_big = (n1 < n2) ? pta2 : pta1;
        L_Aset set1 = L_Aset.CreateFromPta(pta_big);

            /* Build up the intersection of points */
        Pta ptad = Create(0);
        int n = pta_small.Count;
        L_Aset set2 = L_Aset.Create(L.L_UINT_TYPE);
        for (int i = 0; i < n; i++) {
            pta_small.GetIPt(i, out int x, out int y);
            ulong hash = Utils.HashPtToUint64(x, y);
            RbType key = default;
            key.Utype = hash;
            if (set1.Find(key) != null && set2.Find(key) == null) {
                ptad.AddPt(x, y);
                set2.Insert(key);
            }
        }

        return ptad;
    }

    /*--------------------------------------------------------------------------*
     *                            Hashmap operations                            *
     *--------------------------------------------------------------------------*/
    // leptonica: ptaRemoveDupsByHmap (ptafunc2.c)
    /// <param name="ptad">set of unique values</param>
    /// <param name="hmap">hashmap used for lookup</param>
    /// <remarks>
    /// (1) Generates a set of (unique) points from this pta.
    /// </remarks>
    public void RemoveDupsByHmap(out Pta ptad, out L_Hashmap hmap)
    {
        Pta ptas = this;

            /* Traverse the hashtable lists */
        hmap = L_Hashmap.CreateFromPta(ptas);
        ptad = Create(0);
        int tabsize = hmap.TabSize;
        for (int i = 0; i < tabsize; i++) {
            L_Hashitem? hitem = hmap.HashTab[i];
            while (hitem != null) {
                ptas.GetIPt((int)hitem.Val, out int x, out int y);
                ptad.AddPt(x, y);
                hitem = hitem.Next;
            }
        }
    }

    // leptonica: ptaUnionByHmap (ptafunc2.c)
    /// <param name="pta2">second point array</param>
    /// <returns>ptad   union of the two point arrays</returns>
    /// <remarks>
    /// (1) Make pta with points found in either of the input arrays.
    /// </remarks>
    public Pta UnionByHmap(Pta pta2)
    {
        Pta pta1 = this;

        Pta pta3 = pta1.Copy();
        pta3.Join(pta2, 0, -1);   /* C: "pta join failed" error; here Join throws */
        pta3.RemoveDupsByHmap(out Pta ptad, out _);
        return ptad;
    }

    // leptonica: ptaIntersectionByHmap (ptafunc2.c)
    /// <param name="pta2">second point array</param>
    /// <returns>ptad   intersection of the two point arrays</returns>
    /// <remarks>
    /// (1) Make pta with pts common to both input arrays.
    /// </remarks>
    public Pta IntersectionByHmap(Pta pta2)
    {
        Pta pta1 = this;

            /* Make a hashmap for the elements of the biggest array */
        int n1 = pta1.Count;
        int n2 = pta2.Count;
        Pta pta_small = (n1 < n2) ? pta1 : pta2;
        Pta pta_big = (n1 < n2) ? pta2 : pta1;
        L_Hashmap hmap = L_Hashmap.CreateFromPta(pta_big);

            /* Go through the smallest array, doing a lookup of its (x,y) into
             * the big array hashmap.  If an hitem is returned, check the count.
             * If the count is 0, ignore; otherwise, add the point to the
             * output ptad and set the count in the hitem to 0, indicating
             * that the point has already been added. */
        Pta ptad = Create(0);
        int n = pta_small.Count;
        for (int i = 0; i < n; i++) {
            pta_small.GetIPt(i, out int x, out int y);
            ulong key = Utils.HashPtToUint64(x, y);
            L_Hashitem? hitem = hmap.Lookup(key, (ulong)i, L.L_HMAP_CHECK);
            if (hitem == null || hitem.Count == 0)
                continue;
            ptad.AddPt(x, y);
            hitem.Count = 0;
        }
        return ptad;
    }
}

public sealed partial class Ptaa
{
    // leptonica: ptaaSortByIndex (ptafunc2.c)
    /// <param name="naindex">na that maps from the new ptaa to this (input) ptaa</param>
    /// <returns>ptaad sorted</returns>
    public Ptaa SortByIndex(Numa naindex)
    {
        Ptaa ptaas = this;

        int n = ptaas.Count;
        if (naindex.Count != n)
            throw Lept.Arg("ptaaSortByIndex", "numa and ptaa sizes differ");
        Ptaa ptaad = Create(n);
        for (int i = 0; i < n; i++) {
            int index = naindex.GetIValue(i);
            Pta pta = ptaas.GetPta(index, L.L_COPY);
            ptaad.AddPta(pta, L.L_INSERT);
        }

        return ptaad;
    }
}

public sealed partial class L_Aset
{
    // leptonica: l_asetCreateFromPta (ptafunc2.c)
    /// <returns>set using a 64-bit hash of (x,y) as the key</returns>
    public static L_Aset CreateFromPta(Pta pta)
    {
        L_Aset set = Create(L.L_UINT_TYPE);
        int n = pta.Count;
        for (int i = 0; i < n; i++) {
            pta.GetIPt(i, out int x, out int y);
            ulong hash = Utils.HashPtToUint64(x, y);
            RbType key = default;
            key.Utype = hash;
            set.Insert(key);
        }

        return set;
    }
}

public sealed partial class L_Hashmap
{
    // leptonica: l_hmapCreateFromPta (ptafunc2.c)
    /// <param name="pta">input pta</param>
    /// <returns>hmap    hashmap</returns>
    /// <remarks>
    /// (1) The indices into pta are stored in the val field of the hashitems.
    ///     This is necessary so that hmap and pta can be used together.
    /// </remarks>
    public static L_Hashmap CreateFromPta(Pta pta)
    {
        int n = pta.Count;
        L_Hashmap hmap = Create((int)(0.51 * n), 2);
        for (int i = 0; i < n; i++) {
            pta.GetIPt(i, out int x, out int y);
            ulong key = Utils.HashPtToUint64(x, y);
            hmap.Lookup(key, (ulong)i, L.L_HMAP_CREATE);
        }
        return hmap;
    }
}
