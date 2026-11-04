using System;

namespace Leptonica;

/*
 *  dnafunc1.c
 *
 *      Rearrangements
 *          L_Dna.Join (l_dnaJoin)
 *          L_Dnaa.FlattenToDna (l_dnaaFlattenToDna)
 *          L_Dna.SelectRange (l_dnaSelectRange)
 *
 *      Conversion between numa and dna
 *          L_Dna.ConvertToNuma (l_dnaConvertToNuma)
 *          Numa.ConvertToDna (numaConvertToDna)
 *
 *      Conversion from pix data to dna
 *          Pix.ConvertDataToDna (pixConvertDataToDna)
 *
 *      Set operations using aset (rbtree)
 *          L_Aset.CreateFromDna (l_asetCreateFromDna)
 *          L_Dna.RemoveDupsByAset (l_dnaRemoveDupsByAset)
 *          L_Dna.UnionByAset (l_dnaUnionByAset)
 *          L_Dna.IntersectionByAset (l_dnaIntersectionByAset)
 *
 *      Hashmap operations
 *          L_Hashmap.CreateFromDna (l_hmapCreateFromDna)
 *          L_Dna.RemoveDupsByHmap (l_dnaRemoveDupsByHmap)
 *          L_Dna.UnionByHmap (l_dnaUnionByHmap)
 *          L_Dna.IntersectionByHmap (l_dnaIntersectionByHmap)
 *          L_Dna.MakeHistoByHmap (l_dnaMakeHistoByHmap)
 *
 *      Miscellaneous operations
 *          L_Dna.DiffAdjValues (l_dnaDiffAdjValues)
 *
 *
 * We have two implementations of set operations on an array of doubles:
 *
 *   (1) Using an underlying tree (rbtree)
 *       The key for each float64 value is the value itself.
 *       No collisions can occur.  The tree is sorted by the keys.
 *       Lookup is done in O(log n) by traversing from the root,
 *       looking for the key.
 *
 *   (2) Building a hashmap from the keys (hashmap)
 *       The keys are made from each float64 by casting into a uint64.
 *       The key is then hashed into a hashtable.  Collisions of hashkeys are
 *       very rare, and the hashtable is designed to allow more than one
 *       hashitem in a table entry.  The hashitems are put in a list at
 *       each hashtable entry, which is traversed looking for the key.
 *
 *  Differences from C:
 *    - Optional and required output pointers become return values or out
 *      parameters.  Errors throw.
 */
public sealed partial class L_Dna
{
    /*----------------------------------------------------------------------*
     *                            Rearrangements                            *
     *----------------------------------------------------------------------*/
    // leptonica: l_dnaJoin (dnafunc1.c)
    /// <summary>Appends a range of numbers of another dna to this one.</summary>
    /// <param name="das">[optional] source dna; add from this one</param>
    /// <param name="istart">starting index in das</param>
    /// <param name="iend">ending index in das; use -1 to cat all</param>
    /// <remarks>
    /// (1) istart &lt; 0 is taken to mean 'read from the start' (istart = 0)
    /// (2) iend &lt; 0 means 'read to the end'
    /// (3) if das == null, this is a no-op
    /// </remarks>
    public void Join(L_Dna? das, int istart, int iend)
    {
        if (das == null)
            return;

        if (istart < 0)
            istart = 0;
        int n = das.Count;
        if (iend < 0 || iend >= n)
            iend = n - 1;
        if (istart > iend)
            throw Lept.Arg("l_dnaJoin", "istart > iend; nothing to add");

        for (int i = istart; i <= iend; i++) {
            double val = das.GetDValue(i);
            AddNumber(val);
        }
    }

    // leptonica: l_dnaSelectRange (dnafunc1.c)
    /// <summary>Selects a range of numbers into a new dna.</summary>
    /// <param name="first">use 0 to select from the beginning</param>
    /// <param name="last">use -1 to select to the end</param>
    /// <returns>dad</returns>
    public L_Dna SelectRange(int first, int last)
    {
        const string func = "l_dnaSelectRange";
        int n = Count;
        if (n == 0) {
            Log.Warning(func, "das is empty\n");
            return Copy();
        }
        first = Math.Max(0, first);
        if (last < 0) last = n - 1;
        if (first >= n)
            throw Lept.Arg(func, "invalid first");
        if (last >= n) {
            Log.Warning(func, $"last = {last} is beyond max index = {n - 1}; adjusting\n");
            last = n - 1;
        }
        if (first > last)
            throw Lept.Arg(func, "first > last");

        L_Dna dad = Create(last - first + 1);
        for (int i = first; i <= last; i++) {
            double dval = GetDValue(i);
            dad.AddNumber(dval);
        }
        return dad;
    }

    /*----------------------------------------------------------------------*
     *                   Conversion between numa and dna                    *
     *----------------------------------------------------------------------*/
    // leptonica: l_dnaConvertToNuma (dnafunc1.c)
    /// <summary>Converts the dna to a numa.</summary>
    /// <returns>na</returns>
    public Numa ConvertToNuma()
    {
        int n = Count;
        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            double val = GetDValue(i);
            na.AddNumber((float)val);
        }
        return na;
    }

    /*----------------------------------------------------------------------*
     *                   Set operations using aset (rbtree)                 *
     *----------------------------------------------------------------------*/
    // leptonica: l_dnaRemoveDupsByAset (dnafunc1.c)
    /// <summary>Removes duplicates, using a set.</summary>
    /// <returns>dad with duplicates removed</returns>
    public L_Dna RemoveDupsByAset()
    {
        L_Aset set = L_Aset.Create(L.L_FLOAT_TYPE);
        L_Dna dad = Create(0);
        int n = Count;
        for (int i = 0; i < n; i++) {
            double val = GetDValue(i);
            RbType key = default;
            key.Ftype = val;
            if (set.Find(key) == null) {
                dad.AddNumber(val);
                set.Insert(key);
            }
        }

        return dad;
    }

    // leptonica: l_dnaUnionByAset (dnafunc1.c)
    /// <summary>Returns the union of two dna, using a set.</summary>
    /// <param name="da2">second array</param>
    /// <returns>dad union of the two arrays</returns>
    /// <remarks>
    /// (1) See sarrayUnionByAset() for the approach.
    /// (2) Here, the key in building the sorted tree is the number itself.
    /// (3) Operations using an underlying tree are O(nlogn), which is
    ///     typically less efficient than hashing, which is O(n).
    /// </remarks>
    public L_Dna UnionByAset(L_Dna da2)
    {
            /* Join */
        L_Dna da3 = Copy();
        da3.Join(da2, 0, -1);

            /* Eliminate duplicates */
        return da3.RemoveDupsByAset();
    }

    // leptonica: l_dnaIntersectionByAset (dnafunc1.c)
    /// <summary>Returns the intersection of two dna, using a set.</summary>
    /// <param name="da2">second array</param>
    /// <returns>dad intersection of the two arrays</returns>
    /// <remarks>
    /// (1) See sarrayIntersection() for the approach.
    /// (2) Here, the key in building the sorted tree is the number itself.
    /// (3) Operations using an underlying tree are O(nlogn), which is
    ///     typically less efficient than hashing, which is O(n).
    /// </remarks>
    public L_Dna IntersectionByAset(L_Dna da2)
    {
        L_Dna da1 = this;

            /* Put the elements of the largest array into a set */
        int n1 = da1.Count;
        int n2 = da2.Count;
        L_Dna da_small = (n1 < n2) ? da1 : da2;
        L_Dna da_big = (n1 < n2) ? da2 : da1;
        L_Aset set1 = L_Aset.CreateFromDna(da_big);

            /* Build up the intersection of doubles */
        L_Dna dad = Create(0);
        int n = da_small.Count;
        L_Aset set2 = L_Aset.Create(L.L_FLOAT_TYPE);
        for (int i = 0; i < n; i++) {
            double val = da_small.GetDValue(i);
            RbType key = default;
            key.Ftype = val;
            if (set1.Find(key) != null && set2.Find(key) == null) {
                dad.AddNumber(val);
                set2.Insert(key);
            }
        }

        return dad;
    }

    /*--------------------------------------------------------------------------*
     *                           Hashmap operations                             *
     *--------------------------------------------------------------------------*/
    // leptonica: l_dnaRemoveDupsByHmap (dnafunc1.c)
    /// <summary>Generates the set of (unique) values, using a hashmap.</summary>
    /// <param name="hmap">hashmap used for lookup</param>
    /// <returns>dad hash set of unique values</returns>
    /// <remarks>
    /// (1) Generates the set of (unique) values from das.
    /// (2) The values in the hashitems are indices into das.
    /// </remarks>
    public L_Dna RemoveDupsByHmap(out L_Hashmap hmap)
    {
        L_Dna das = this;

            /* Traverse the hashtable lists */
        hmap = L_Hashmap.CreateFromDna(das);
        L_Dna dad = Create(0);
        int tabsize = hmap.TabSize;
        for (int i = 0; i < tabsize; i++) {
            L_Hashitem? hitem = hmap.HashTab[i];
            while (hitem != null) {
                double dval = das.GetDValue((int)hitem.Val);
                dad.AddNumber(dval);
                hitem = hitem.Next;
            }
        }

        return dad;
    }

    // leptonica: l_dnaUnionByHmap (dnafunc1.c)
    /// <summary>Returns the union of two dna, using a hashmap.</summary>
    /// <param name="da2">second array</param>
    /// <returns>dad union of the array values</returns>
    /// <remarks>
    /// (1) Make dna with numbers found in either of the input arrays.
    /// </remarks>
    public L_Dna UnionByHmap(L_Dna da2)
    {
        L_Dna da3 = Copy();
        da3.Join(da2, 0, -1);
        return da3.RemoveDupsByHmap(out _);
    }

    // leptonica: l_dnaIntersectionByHmap (dnafunc1.c)
    /// <summary>Returns the intersection of two dna, using a hashmap.</summary>
    /// <param name="da2">second array</param>
    /// <returns>dad intersection of the array values</returns>
    /// <remarks>
    /// (1) Make dna with numbers common to both input arrays.
    /// (2) Use the values in the dna as the hash keys.
    /// </remarks>
    public L_Dna IntersectionByHmap(L_Dna da2)
    {
        L_Dna da1 = this;

            /* Make a hashmap for the elements of the biggest array */
        int n1 = da1.Count;
        int n2 = da2.Count;
        L_Dna da_small = (n1 < n2) ? da1 : da2;
        L_Dna da_big = (n1 < n2) ? da2 : da1;
        L_Hashmap hmap = L_Hashmap.CreateFromDna(da_big);

            /* Go through the smallest array, doing a lookup of its dval into
             * the big array hashmap.  If an hitem is returned, check the count.
             * If the count is 0, ignore; otherwise, add the dval to the
             * output dad and set the count in the hitem to 0, indicating
             * that the dval has already been added. */
        L_Dna dad = Create(0);
        int n = da_small.Count;
        for (int i = 0; i < n; i++) {
            double dval = da_small.GetDValue(i);
            ulong key = Utils.HashFloat64ToUint64(dval);
            L_Hashitem? hitem = hmap.Lookup(key, (ulong)i, L.L_HMAP_CHECK);
            if (hitem == null || hitem.Count == 0)
                continue;
            dad.AddNumber(dval);
            hitem.Count = 0;
        }
        return dad;
    }

    // leptonica: l_dnaMakeHistoByHmap (dnafunc1.c)
    /// <summary>Generates a histogram of the values, using a hashmap.</summary>
    /// <param name="pdav">array (set) of unique values</param>
    /// <param name="pdac">array of counts, aligned with the array of values</param>
    /// <remarks>
    /// (1) Generates a histogram represented by two aligned arrays:
    ///     value and count.
    /// </remarks>
    public void MakeHistoByHmap(out L_Dna pdav, out L_Dna pdac)
    {
        L_Dna das = this;

            /* Traverse the hashtable lists */
        L_Hashmap hmap = L_Hashmap.CreateFromDna(das);
        L_Dna dav = Create(0);
        pdav = dav;
        L_Dna dac = Create(0);
        pdac = dac;
        int tabsize = hmap.TabSize;
        for (int i = 0; i < tabsize; i++) {
            L_Hashitem? hitem = hmap.HashTab[i];
            while (hitem != null) {
                double dval = das.GetDValue((int)hitem.Val);
                dav.AddNumber(dval);
                dac.AddNumber(hitem.Count);
                hitem = hitem.Next;
            }
        }
    }

    /*----------------------------------------------------------------------*
     *                       Miscellaneous operations                       *
     *----------------------------------------------------------------------*/
    // leptonica: l_dnaDiffAdjValues (dnafunc1.c)
    /// <summary>Computes the differences of adjacent values.</summary>
    /// <returns>dad of difference values val[i+1] - val[i]</returns>
    /// <remarks>
    /// NOTE(upstream): prev starts at 0 instead of the first value, so the
    /// first difference is val[1] - 0.
    /// </remarks>
    public L_Dna DiffAdjValues()
    {
        int n = Count;
        L_Dna dad = Create(n - 1);
        int prev = 0;
        for (int i = 1; i < n; i++) {
            int cur = GetIValue(i);
            dad.AddNumber(cur - prev);
            prev = cur;
        }
        return dad;
    }
}

public sealed partial class L_Dnaa
{
    // leptonica: l_dnaaFlattenToDna (dnafunc1.c)
    /// <summary>Flattens the dnaa to a dna, by joining successively each dna in the dnaa.</summary>
    /// <returns>dad</returns>
    /// <remarks>
    /// (1) This 'flattens' the dnaa to a dna, by joining successively
    ///     each dna in the dnaa.
    /// (2) It leaves the input dnaa unchanged.
    /// </remarks>
    public L_Dna FlattenToDna()
    {
        int n = Count;   /* the ptrs beyond the count are null */
        L_Dna dad = L_Dna.Create(0);
        for (int i = 0; i < n; i++) {
            L_Dna da = GetDna(i, L.L_CLONE);
            dad.Join(da, 0, -1);
        }

        return dad;
    }
}

public sealed partial class Numa
{
    // leptonica: numaConvertToDna (dnafunc1.c)
    /// <summary>Converts the numa to a dna.</summary>
    /// <returns>da</returns>
    public L_Dna ConvertToDna()
    {
        int n = Count;
        L_Dna da = L_Dna.Create(n);
        for (int i = 0; i < n; i++) {
            float val = GetFValue(i);
            da.AddNumber(val);
        }
        return da;
    }
}

public sealed partial class Pix
{
    /*----------------------------------------------------------------------*
     *                    Conversion from pix data to dna                   *
     *----------------------------------------------------------------------*/
    // leptonica: pixConvertDataToDna (dnafunc1.c)
    /// <summary>Converts the pixel data of a pix to a dna.</summary>
    /// <returns>da</returns>
    /// <remarks>
    /// (1) This writes the RGBA pixel values into the dna, in row-major order.
    /// </remarks>
    public L_Dna ConvertDataToDna()
    {
        if (Depth != 32)
            throw Lept.Arg("pixConvertDataToDna", "pix not 32 bpp");

        GetDimensions(out int w, out int h, out _);
        uint[] data = Data;
        int wpl = Wpl;
        L_Dna da = L_Dna.Create(w * h);
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w; j++)
                da.AddNumber((double)line[j]);
        }
        return da;
    }
}

public sealed partial class L_Aset
{
    // leptonica: l_asetCreateFromDna (dnafunc1.c)
    /// <param name="da">source dna</param>
    /// <returns>set using the doubles in da as keys</returns>
    public static L_Aset CreateFromDna(L_Dna da)
    {
        L_Aset set = Create(L.L_FLOAT_TYPE);
        int n = da.Count;
        for (int i = 0; i < n; i++) {
            double val = da.GetDValue(i);
            RbType key = default;
            key.Ftype = val;
            set.Insert(key);
        }

        return set;
    }
}

public sealed partial class L_Hashmap
{
    // leptonica: l_hmapCreateFromDna (dnafunc1.c)
    /// <param name="da">input dna</param>
    /// <returns>hmap hashmap</returns>
    /// <remarks>
    /// (1) Derive the hash keys from the values in da.
    /// (2) The indices into da are stored in the val field of the hashitems.
    ///     This is necessary so that hmap and da can be used together.
    /// </remarks>
    public static L_Hashmap CreateFromDna(L_Dna da)
    {
        int n = da.Count;
        L_Hashmap hmap = Create(0, 0);
        for (int i = 0; i < n; i++) {
            double dval = da.GetDValue(i);
            ulong key = Utils.HashFloat64ToUint64(dval);
            hmap.Lookup(key, (ulong)i, L.L_HMAP_CREATE);
        }
        return hmap;
    }
}
