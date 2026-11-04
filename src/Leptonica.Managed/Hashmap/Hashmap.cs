using System;

namespace Leptonica;

/*
 *  hashmap.c
 *
 *      Hashmap creation
 *          L_Hashmap.Create (l_hmapCreate)
 *
 *      Hashmap: Accessors and modifiers
 *          L_Hashmap.Lookup (l_hmapLookup)
 *          L_Hashmap.Rehash (l_hmapRehash)
 *
 *    (1) See also hashmap.h for a brief description of the design.
 *    (2) In a typical use, a set of objects (in an array or associated
 *        with image pixels) is represented by a hashmap.  A uint64 key is
 *        produced for each object.  This integer is then hashed into an
 *        index in a hashtable, using the mod function with the table size
 *        which is a prime number.  Each entry in the hash table is a list
 *        of hash items.  In lookup, the appropriate list is traversed,
 *        looking for the object key found earlier.
 *    (3) Hash functions that map points, strings and float64 to uint64
 *        are given in utils1.c.
 *    (4) Use of the hashmap on points, strings and float64 data are
 *        given in ptafunc2.c, sarray2.c and dnafunc1.c.
 *    (5) Useful rule of thumb for hashing collisions:
 *        For a random hashing function (say, from strings to l_uint64),
 *        the probability of a collision increases as N^2 for N much
 *        less than 2^32.  The quadratic behavior switches over to
 *        approaching 1.0 around 2^32, which is the square root of 2^64.
 *        So, for example, if you have 10^7 strings, the probability
 *        of a single collision using an l_uint64 key is on the order of
 *            (10^7/4x10^9)^2 ~ 10^-5.
 *        For a million strings, collisions are very rare (~10^-7 probability).
 *
 *  Not ported: l_hmapDestroy (memory management only).
 */

public static partial class L
{
    /* Operations for l_hmapLookup() (hashmap.h) */
    /// <summary>Check whether this key and value have been stored.</summary>
    public const int L_HMAP_CHECK = 1;
    /// <summary>Create and store a hash item if it is not found.</summary>
    public const int L_HMAP_CREATE = 2;
}

/// <summary>Item in a hashmap list (struct L_Hashitem).</summary>
public sealed class L_Hashitem
{
    /// <summary>Gets or sets the key, which is hashed into an index into the hash table.</summary>
    public ulong Key { get; set; }
    /// <summary>Gets or sets the number stored with the key.</summary>
    public ulong Val { get; set; }
    /// <summary>Gets or sets the number of elements seen with this key.</summary>
    public int Count { get; set; }
    /// <summary>Gets or sets the next item in the list, or <see langword="null"/> at the end of the list.</summary>
    public L_Hashitem? Next { get; set; }
}

/// <summary>Hash map (struct L_Hashmap).</summary>
public sealed partial class L_Hashmap
{
        /* Limit on the hashtable size */
    private const uint MaxTabsize = 50000000;
        /* Default values for creating the hashmap. */
    private const int DefaultInitNItems = 2000;
    private const int DefaultMaxOcc = 2;

    /// <summary>Gets or sets the number of stored items.</summary>
    public int NItems { get; set; }
    /// <summary>Gets or sets the number of items that can still be stored before the table is resized.</summary>
    public int NToGo { get; set; }
    /// <summary>Gets or sets the maximum average occupancy allowed for each hash bucket.</summary>
    public int MaxOcc { get; set; }
    /// <summary>Gets or sets the hash table; each element is the head of a list of items.</summary>
    public L_Hashitem?[] HashTab { get; set; } = Array.Empty<L_Hashitem?>();
    /// <summary>Gets or sets the size of the hash table.</summary>
    public int TabSize { get; set; }

    /*--------------------------------------------------------------------------*
     *                     Hashmap creation and destruction                     *
     *--------------------------------------------------------------------------*/
    // leptonica: l_hmapCreate (hashmap.c)
    /// <summary>Creates a hashmap.</summary>
    /// <param name="ninit">initial estimate of the number of items to be stored;
    ///                     use 0 for default value.</param>
    /// <param name="maxocc">max average occupancy of each list of hashitme;
    ///                      it should be in range [1 ... 5]; use 0 for default</param>
    /// <returns>new hashmap</returns>
    /// <remarks>
    /// (1) If the maximum number n of items to be hashed is known in
    ///     advance, suggested values are:
    ///            nint = 0.51 * n
    ///            maxocc = 2
    ///     With these values, the table will not need to be rehashed,
    ///     even if all items have unique keys.
    /// (2) The actual initial size of the hashtab is the first prime
    ///     number larger than ninit/maxocc.
    /// (3) Each entry into the hashtab points to alist of hash items
    ///     (key, val, count).
    /// </remarks>
    public static L_Hashmap Create(int ninit, int maxocc)
    {
        const string func = "l_hmapCreate";
        ninit = Math.Max(ninit, DefaultInitNItems);
        if (maxocc <= 0) maxocc = DefaultMaxOcc;
        if (maxocc > 5) {
            Log.Warning(func, $"maxocc = {maxocc}; non-optimal value. Set to default = {DefaultMaxOcc}\n");
            maxocc = DefaultMaxOcc;
        }
        uint size = (uint)(ninit / maxocc);
        if (size > MaxTabsize) {
            Log.Error(func, $"ninit/maxocc = {size} > MaxTabsize = {MaxTabsize}\n");
            throw Lept.Fail(func, "ninit/maxocc too big");
        }

        L_Hashmap hmap = new L_Hashmap();
        uint tabsize = Utils.FindNextLargerPrime((int)size);  /* at least 101 */
        hmap.HashTab = new L_Hashitem?[tabsize];

        hmap.NItems = 0;
        hmap.NToGo = ninit;
        hmap.MaxOcc = maxocc;
        hmap.TabSize = (int)tabsize;
        return hmap;
    }

    /*--------------------------------------------------------------------------*
     *                    Hashmap: Accessors and modifiers                      *
     *--------------------------------------------------------------------------*/
    // leptonica: l_hmapLookup (hashmap.c)
    /// <summary>Looks up a key in the hashmap, optionally creating the hashitem.</summary>
    /// <param name="key">to be hashed into an index in the hashtab</param>
    /// <param name="val">to be stored in the hitem if creating it</param>
    /// <param name="op">L_HMAP_CHECK, L_HMAP_CREATE</param>
    /// <returns>the hashitem, or null if not found with op == L_HMAP_CHECK</returns>
    /// <remarks>
    /// (1) This lookup function will also create a new hitem if requested.
    /// (2) The op parameter does the following:
    ///     op == L_HMAP_CHECK: return the hitem, or null if not found
    ///     op == L_HMAP_CREATE: if found, increment the count; otherwise, make
    ///                          and store a new hitem; always return the hitem.
    /// (3) The key is a uint64.  It is made by hashing some data in the object.
    /// (4) The value is an index into an array of the objects from which
    ///     the hashtable has been constructed.
    /// (5) If an hitem is found, a reference to it is returned.  It is owned
    ///     by the hashtable.
    /// </remarks>
    public L_Hashitem? Lookup(ulong key, ulong val, int op)
    {
        const string func = "l_hmapLookup";
        if (op != L.L_HMAP_CHECK && op != L.L_HMAP_CREATE)
            throw Lept.Arg(func, "invalid op");

            /* If found, return the hitem (not a copy) */
        uint index = (uint)(key % (ulong)(uint)TabSize);  /* into hashtab */
        L_Hashitem? hlist = HashTab[index];  /* head of the list */
        for (L_Hashitem? hitem = hlist; hitem != null; hitem = hitem.Next) {
            if (key == hitem.Key) {
                if (op == L.L_HMAP_CREATE) hitem.Count++;
                return hitem;
            }
        }
        if (op == L.L_HMAP_CHECK) return null;

            /* Not found and op == L_HMAP_CREATE.
             * Make a new hitem and add to the head of the list */
        L_Hashitem newitem = new L_Hashitem();
        newitem.Key = key;
        newitem.Val = val;
        newitem.Count = 1;
        newitem.Next = hlist;
        HashTab[index] = newitem;
        NItems++;
        NToGo--;

            /* If hmap is full based on average occupancy, rehash */
        if (NToGo == 0)
            Rehash();

        return newitem;
    }

    // leptonica: l_hmapRehash (hashmap.c)
    /// <summary>Doubles the size of the hashtab and reuses the hash items.</summary>
    /// <remarks>
    /// (1) This is called when the average occupancy reaches maxocc.
    ///     It doubles the size of the hashtab, and reuses the hash items.
    /// </remarks>
    public void Rehash()
    {
            /* Put hash items in temporary storage in a single list,
             * successively adding each to the list head. */
        L_Hashitem? hstore = null;  /* ptr to resulting list */
        L_Hashitem? next;
        for (int i = 0; i < TabSize; i++) {
            for (L_Hashitem? hitem = HashTab[i]; hitem != null; hitem = next) {
                next = hitem.Next;
                hitem.Next = hstore;
                hstore = hitem;
            }
        }

            /* Destroy the old hashtab and make a new one that is twice as big */
        uint tabsize = Utils.FindNextLargerPrime(2 * TabSize);
        TabSize = (int)tabsize;
        HashTab = new L_Hashitem?[tabsize];
        NToGo = (int)((uint)MaxOcc * tabsize - (uint)NItems);

            /* Populate with the stored hash items */
        for (L_Hashitem? hitem = hstore; hitem != null; hitem = next) {
            next = hitem.Next;
            uint index = (uint)(hitem.Key % tabsize);  /* into new hashtab */
            hitem.Next = HashTab[index];  /* link to head of existing list */
            HashTab[index] = hitem;  /* put at head */
        }
    }
}
