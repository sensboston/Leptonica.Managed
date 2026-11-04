using System;
using System.Globalization;
using System.Text;

namespace Leptonica;

/*
 *  sarray2.c: sort, set operations and miscellaneous operations on string arrays.
 *
 *      Sort
 *          sarraySort, sarraySortByIndex, stringCompareLexical
 *
 *      Set operations using aset (rbtree)
 *          l_asetCreateFromSarray, sarrayRemoveDupsByAset, sarrayUnionByAset,
 *          sarrayIntersectionByAset
 *
 *      Hashmap operations
 *          l_hmapCreateFromSarray, sarrayRemoveDupsByHmap, sarrayUnionByHmap,
 *          sarrayIntersectionByHmap
 *
 *      Miscellaneous operations
 *          sarrayGenerateIntegers, sarrayLookupCSKV
 *
 *  We have two implementations of set operations on an array of strings:
 *
 *   (1) Using an underlying tree (rbtree).
 *       This uses a good 64 bit hashing function for the key,
 *       that is not expected to have hash collisions (and we do
 *       not test for them).  The tree is built up of the hash
 *       values, and if the hash is found in the tree, it is
 *       assumed that the string has already been found.
 *
 *   (2) Building a hashmap from the keys (hashmap).
 *       This uses a fast 64 bit hashing function for the key, which
 *       is then hashed into a hashtable.  Collisions of hashkeys are
 *       very rare, but the hashtable is designed to allow more than one
 *       hashitem in a table entry.  The hashitems are put in a list at
 *       each hashtable entry, which is traversed looking for the key.
 *
 *  Callees in files that are not ported yet (stubs): l_asetCreate, l_asetFind,
 *  l_asetInsert (map.c), l_hmapCreate, l_hmapLookup (hashmap.c),
 *  l_hashStringToUint64Fast (utils1.c).
 */

public static partial class L
{
    /* Key types of the rbtree (rbtree.h) */

    /* Hashmap lookup operations (hashmap.h) */
}

public sealed partial class Sarray
{
    /*----------------------------------------------------------------------*
     *                                   Sort                               *
     *----------------------------------------------------------------------*/
    // leptonica: sarraySort (sarray2.c)
    /// <summary>Sorts a string array by ascii value (Shell sort).</summary>
    /// <param name="saout">output sarray; can be null or equal to sain</param>
    /// <param name="sain">input sarray</param>
    /// <param name="sortorder">L_SORT_INCREASING or L_SORT_DECREASING</param>
    /// <returns>saout output sarray, sorted by ascii value</returns>
    /// <remarks>
    /// (1) Set saout = sain for in-place; otherwise, set naout = NULL.
    /// (2) Shell sort, modified from K&amp;R, 2nd edition, p.62.
    ///     Slow but simple O(n logn) sort.
    /// </remarks>
    public static Sarray Sort(Sarray? saout, Sarray sain, int sortorder)
    {
            /* Make saout if necessary; otherwise do in-place */
        if (saout == null)
            saout = sain.Copy();
        else if (sain != saout)
            throw Lept.Arg("sarraySort", "invalid: not in-place");
        string?[] array = saout.array;  /* operate directly on the array */
        int n = saout.Count;

            /* Shell sort */
        for (int gap = n / 2; gap > 0; gap = gap / 2) {
            for (int i = gap; i < n; i++) {
                for (int j = i - gap; j >= 0; j -= gap) {
                    if ((sortorder == L.L_SORT_INCREASING &&
                         StringCompareLexical(array[j]!, array[j + gap]!) != 0) ||
                        (sortorder == L.L_SORT_DECREASING &&
                         StringCompareLexical(array[j + gap]!, array[j]!) != 0))
                    {
                        string? tmp = array[j];
                        array[j] = array[j + gap];
                        array[j + gap] = tmp;
                    }
                }
            }
        }

        return saout;
    }

    // leptonica: sarraySortByIndex (sarray2.c)
    /// <summary>Returns a new sarray with the strings of sain in the order given by naindex.</summary>
    /// <param name="sain">input sarray</param>
    /// <param name="naindex">na that maps from the new sarray to the input sarray</param>
    /// <returns>saout sorted</returns>
    public static Sarray SortByIndex(Sarray sain, Numa naindex)
    {
        int n = sain.Count;
        Sarray saout = Create(n);
        for (int i = 0; i < n; i++) {
            int index = naindex.GetIValue(i);
            string str = sain.GetString(index, L.L_COPY);
            saout.AddString(str, L.L_INSERT);
        }

        return saout;
    }

    // leptonica: stringCompareLexical (sarray2.c)
    /// <summary>Compares two strings lexically.</summary>
    /// <returns>1 if str1 > str2 lexically; 0 otherwise</returns>
    /// <remarks>
    /// (1) If the lexical values are identical, return a 0, to
    ///     indicate that no swapping is required to sort the strings.
    /// (2) Strings are compared as the bytes of their UTF-8 encoding, like the
    ///     C code does on the chars of the C string.  For ASCII strings this is
    ///     the same as an ordinal comparison.
    /// </remarks>
    // NOTE(upstream): the C code compares plain chars, which are signed on the
    // MSVC and x86 gcc targets, so a byte >= 0x80 sorts below all ASCII bytes.
    // Ported as-is (see docs/UPSTREAM_NOTES.md).
    public static int StringCompareLexical(string str1, string str2)
    {
        str1 = CStr(str1);
        str2 = CStr(str2);

            /* Fast path: ASCII characters are single bytes in UTF-8 */
        int clen = Math.Min(str1.Length, str2.Length);
        for (int i = 0; i < clen; i++) {
            char c1 = str1[i];
            char c2 = str2[i];
            if (c1 == c2)
                continue;
            if (c1 < 0x80 && c2 < 0x80)
                return c1 > c2 ? 1 : 0;
            return CompareBytes(Encoding.UTF8.GetBytes(str1), Encoding.UTF8.GetBytes(str2));
        }

            /* One string is a prefix of the other (or they are equal) */
        return str1.Length > str2.Length ? 1 : 0;
    }

    /// <summary>The loop of stringCompareLexical() on the bytes of the two C strings.</summary>
    private static int CompareBytes(byte[] b1, byte[] b2)
    {
        int len1 = b1.Length;
        int len2 = b2.Length;
        int len = Math.Min(len1, len2);

        for (int i = 0; i < len; i++) {
            if (b1[i] == b2[i])
                continue;
            if ((sbyte)b1[i] > (sbyte)b2[i])
                return 1;
            else
                return 0;
        }

        if (len1 > len2)
            return 1;
        else
            return 0;
    }

    /*----------------------------------------------------------------------*
     *                   Set operations using aset (rbtree)                 *
     *----------------------------------------------------------------------*/
    // leptonica: l_asetCreateFromSarray (sarray2.c)
    /// <summary>Makes a set using a string hash into a uint64 as the key.</summary>
    public L_Aset CreateAset()
    {
        L_Aset set = L_Aset.Create(L.L_UINT_TYPE);
        int n = Count;
        for (int i = 0; i < n; i++) {
            string str = GetString(i, L.L_NOCOPY);
            Utils.HashStringToUint64Fast(str, out ulong hash);
            RbType key = default;
            key.Utype = hash;
            set.Insert(key);
        }

        return set;
    }

    // leptonica: sarrayRemoveDupsByAset (sarray2.c)
    /// <summary>Returns a copy of this array with duplicates removed.</summary>
    /// <returns>sad with duplicates removed, in order of first occurrence</returns>
    /// <remarks>
    /// (1) This is O(nlogn), considerably slower than
    ///     sarrayRemoveDupsByHmap() for large string arrays.
    /// (2) The key for each string is a 64-bit hash.
    /// (3) Build a set, using hashed strings as keys.  As the set is
    ///     built, first do a find; if not found, add the key to the
    ///     set and add the string to the output sarray.
    /// </remarks>
    public Sarray RemoveDupsByAset()
    {
        L_Aset set = L_Aset.Create(L.L_UINT_TYPE);
        Sarray sad = Create(0);
        int n = Count;
        for (int i = 0; i < n; i++) {
            string str = GetString(i, L.L_NOCOPY);
            Utils.HashStringToUint64Fast(str, out ulong hash);
            RbType key = default;
            key.Utype = hash;
            if (set.Find(key) == null) {
                sad.AddString(str, L.L_COPY);
                set.Insert(key);
            }
        }

        return sad;
    }

    // leptonica: sarrayUnionByAset (sarray2.c)
    /// <summary>Returns the union of the two string arrays.</summary>
    /// <param name="sa2">second array</param>
    /// <returns>sad union of the two string arrays</returns>
    /// <remarks>
    /// (1) Duplicates are removed from the concatenation of the two arrays.
    /// (2) The key for each string is a 64-bit hash.
    /// (2) Algorithm: Concatenate the two sarrays.  Then build a set,
    ///     using hashed strings as keys.  As the set is built, first do
    ///     a find; if not found, add the key to the set and add the string
    ///     to the output sarray.  This is O(nlogn).
    /// </remarks>
    public Sarray UnionByAset(Sarray sa2)
    {
            /* Join */
        Sarray sa3 = Copy();
        sa3.Join(sa2);

            /* Eliminate duplicates */
        return sa3.RemoveDupsByAset();
    }

    // leptonica: sarrayIntersectionByAset (sarray2.c)
    /// <summary>Returns the intersection of the two string arrays.</summary>
    /// <param name="sa2">second array</param>
    /// <returns>sad intersection of the two string arrays</returns>
    /// <remarks>
    /// (1) Algorithm: put the larger sarray into a set, using the string
    ///     hashes as the key values.  Then run through the smaller sarray,
    ///     building an output sarray and a second set from the strings
    ///     in the larger array: if a string is in the first set but
    ///     not in the second, add the string to the output sarray and hash
    ///     it into the second set.  The second set is required to make
    ///     sure only one instance of each string is put into the output sarray.
    ///     This is O(mlogn), {m,n} = sizes of {smaller,larger} input arrays.
    /// </remarks>
    public Sarray IntersectionByAset(Sarray sa2)
    {
        Sarray sa1 = this;

            /* Put the elements of the biggest array into a set */
        int n1 = sa1.Count;
        int n2 = sa2.Count;
        Sarray sa_small = (n1 < n2) ? sa1 : sa2;
        Sarray sa_big = (n1 < n2) ? sa2 : sa1;
        L_Aset set1 = sa_big.CreateAset();

            /* Build up the intersection of strings */
        Sarray sad = Create(0);
        int n = sa_small.Count;
        L_Aset set2 = L_Aset.Create(L.L_UINT_TYPE);
        for (int i = 0; i < n; i++) {
            string str = sa_small.GetString(i, L.L_NOCOPY);
            Utils.HashStringToUint64Fast(str, out ulong hash);
            RbType key = default;
            key.Utype = hash;
            if (set1.Find(key) != null && set2.Find(key) == null) {
                sad.AddString(str, L.L_COPY);
                set2.Insert(key);
            }
        }

        return sad;
    }

    /*----------------------------------------------------------------------*
     *                          Hashmap operations                          *
     *----------------------------------------------------------------------*/
    // leptonica: l_hmapCreateFromSarray (sarray2.c)
    /// <summary>Makes a hashmap with the string hashes of this array as keys.</summary>
    /// <returns>hmap hashmap</returns>
    public L_Hashmap CreateHashMap()
    {
        int n = Count;
        L_Hashmap hmap = L_Hashmap.Create((int)(0.51 * n), 2);
        for (int i = 0; i < n; i++) {
            string str = GetString(i, L.L_NOCOPY);
            Utils.HashStringToUint64Fast(str, out ulong key);
            hmap.Lookup(key, (ulong)i, L.L_HMAP_CREATE);
        }
        return hmap;
    }

    // leptonica: sarrayRemoveDupsByHmap (sarray2.c)
    /// <summary>Returns the unique strings of this array, in hashtable order.</summary>
    /// <param name="hmap">hashmap used for lookup</param>
    /// <returns>sad hash set of unique values</returns>
    public Sarray RemoveDupsByHmap(out L_Hashmap hmap)
    {
            /* Traverse the hashtable lists */
        hmap = CreateHashMap();
        Sarray sad = Create(0);
        int tabsize = hmap.TabSize;
        for (int i = 0; i < tabsize; i++) {
            L_Hashitem? hitem = hmap.HashTab[i];
            while (hitem != null) {
                string str = GetString((int)hitem.Val, L.L_COPY);
                sad.AddString(str, L.L_INSERT);
                hitem = hitem.Next;
            }
        }

        return sad;
    }

    // leptonica: sarrayUnionByHmap (sarray2.c)
    /// <summary>Returns the union of the two string arrays.</summary>
    /// <param name="sa2">second array</param>
    /// <returns>sad union of the array values</returns>
    public Sarray UnionByHmap(Sarray sa2)
    {
        Sarray sa3 = Copy();
        sa3.Join(sa2);
        return sa3.RemoveDupsByHmap(out _);
    }

    // leptonica: sarrayIntersectionByHmap (sarray2.c)
    /// <summary>Returns the intersection of the two string arrays.</summary>
    /// <param name="sa2">second array</param>
    /// <returns>sad intersection of the array values</returns>
    public Sarray IntersectionByHmap(Sarray sa2)
    {
        Sarray sa1 = this;

            /* Make a hashmap for the elements of the biggest array */
        int n1 = sa1.Count;
        int n2 = sa2.Count;
        Sarray sa_small = (n1 < n2) ? sa1 : sa2;
        Sarray sa_big = (n1 < n2) ? sa2 : sa1;
        L_Hashmap hmap = sa_big.CreateHashMap();

            /* Remove duplicates from the smallest array.  Alternatively,
             * we can skip this step and avoid counting duplicates in
             * sa_small by modifying the count fields in the sa_big hashitems;
             * e.g., see l_hmapIntersectionDna(). */
        Sarray sa3 = sa_small.RemoveDupsByHmap(out _);

            /* Go through sa3, the set of strings derived from the smallest array,
             * hashing into the big array table.  Any string found belongs to both,
             * so add it to the output array. */
        Sarray sad = Create(0);
        int n = sa3.Count;
        for (int i = 0; i < n; i++) {
            string str = sa3.GetString(i, L.L_NOCOPY);
            Utils.HashStringToUint64Fast(str, out ulong key);
            L_Hashitem? hitem = hmap.Lookup(key, (ulong)i, L.L_HMAP_CHECK);
            if (hitem != null)
                sad.AddString(str, L.L_COPY);
        }
        return sad;
    }

    /*----------------------------------------------------------------------*
     *                      Miscellaneous operations                        *
     *----------------------------------------------------------------------*/
    // leptonica: sarrayGenerateIntegers (sarray2.c)
    /// <summary>Makes an array of the printed numbers 0 to n - 1.</summary>
    /// <returns>sa of printed numbers</returns>
    // NOTE(upstream): the C doc says "1 - n", but the loop prints 0 ... n - 1.
    public static Sarray GenerateIntegers(int n)
    {
        Sarray sa = Create(n);
        for (int i = 0; i < n; i++) {
            string buf = i.ToString(CultureInfo.InvariantCulture);
            sa.AddString(buf, L.L_COPY);
        }
        return sa;
    }

    // leptonica: sarrayLookupCSKV (sarray2.c)
    /// <summary>Finds the value string for a key in comma-separated key-value strings.</summary>
    /// <param name="keystring">an input string to match with each key in this array</param>
    /// <returns>the value string corresponding to the input key string, if found; otherwise null</returns>
    /// <remarks>
    /// (1) The input %sa can have other strings that are not in
    ///     comma-separated key-value format.  These will be ignored.
    /// (2) This returns a copy of the first value string in %sa whose
    ///     key string matches the input %keystring.
    /// (3) White space is not ignored; all white space before the ','
    ///     is used for the keystring in matching.  This allows the
    ///     key and val strings to have white space (e.g., multiple words).
    /// </remarks>
    public string? LookupCSKV(string keystring)
    {
        int n = Count;
        for (int i = 0; i < n; i++) {
            string str = GetString(i, L.L_NOCOPY);
            Sarray sa1 = Create(2);
            sa1.SplitString(str, ",");
            if (sa1.Count != 2)
                continue;
            string key = sa1.GetString(0, L.L_NOCOPY);
            string val = sa1.GetString(1, L.L_NOCOPY);
            if (string.CompareOrdinal(CStr(key), CStr(keystring)) == 0)
                return val;
        }

        return null;
    }
}
