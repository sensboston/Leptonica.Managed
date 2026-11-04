using System;

namespace Leptonica;

/*
 *  map.c
 *
 *  This is an interface for map and set functions, based on using
 *  red-black binary search trees.  Because these trees are sorted,
 *  they are O(nlogn) to build.  They allow logn insertion, find
 *  and deletion of elements.
 *
 *  Both the map and set are ordered by key value, with unique keys.
 *  For the map, the elements are key/value pairs.
 *  For the set we only store unique, ordered keys, and the value
 *  (set to 0 in the implementation) is ignored.
 *
 *  The keys for the map and set can be any of the three types in the
 *  l_rbtree_keytype enum.  The values stored can be any of the
 *  types in the rb_type union.
 *
 *  In-order forward and reverse iterators are provided for maps and sets.
 *  To forward iterate over the map for any type of key (in this example,
 *  uint32), extracting integer values:
 *
 *      L_Amap m = L_Amap.Create(L.L_UINT_TYPE);
 *      [add elements to the map ...]
 *      L_RbtreeNode? n = m.GetFirst();
 *      while (n != null) {
 *          int val = (int)n.Value.Itype;
 *          // do something ...
 *          n = L_Amap.GetNext(n);
 *      }
 *
 *  If the nodes are deleted during the iteration:
 *
 *      L_Amap m = L_Amap.Create(L.L_UINT_TYPE);
 *      [add elements to the map ...]
 *      L_RbtreeNode? n = m.GetFirst();
 *      while (n != null) {
 *          L_RbtreeNode? nn = L_Amap.GetNext(n);
 *          int val = (int)n.Value.Itype;
 *          uint key = (uint)n.Key.Utype;
 *          // do something ...
 *          m.Delete(n.Key);
 *          n = nn;
 *      }
 *
 *  Interface to (a) map using a general key and storing general values
 *           L_Amap.Create (l_amapCreate)
 *           L_Amap.Find (l_amapFind)
 *           L_Amap.Insert (l_amapInsert)
 *           L_Amap.Delete (l_amapDelete)
 *           L_Amap.GetFirst (l_amapGetFirst)
 *           L_Amap.GetNext (l_amapGetNext)
 *           L_Amap.GetLast (l_amapGetLast)
 *           L_Amap.GetPrev (l_amapGetPrev)
 *           L_Amap.Size (l_amapSize)
 *
 *  Interface to (a) set using a general key
 *           L_Aset.Create (l_asetCreate)
 *           L_Aset.Find (l_asetFind)
 *           L_Aset.Insert (l_asetInsert)
 *           L_Aset.Delete (l_asetDelete)
 *           L_Aset.GetFirst (l_asetGetFirst)
 *           L_Aset.GetNext (l_asetGetNext)
 *           L_Aset.GetLast (l_asetGetLast)
 *           L_Aset.GetPrev (l_asetGetPrev)
 *           L_Aset.Size (l_asetSize)
 *
 *  Differences from C:
 *    - L_AMAP and L_ASET are typedefs of L_RBTREE in C; here they are
 *      classes that wrap an L_Rbtree.  The nodes are L_RbtreeNode.
 *    - l_amapDestroy and l_asetDestroy are not ported (the GC owns the objects).
 *    - Find returns a copy of the value (null if not found).
 */

/// <summary>Ordered map (L_AMAP), an rbtree in disguise.</summary>
public sealed partial class L_Amap
{
    private readonly L_Rbtree tree;

    private L_Amap(L_Rbtree tree)
    {
        this.tree = tree;
    }

    // leptonica: l_amapCreate (map.c)
    /// <param name="keytype">L_INT_TYPE, L_UINT_TYPE or L_FLOAT_TYPE</param>
    public static L_Amap Create(int keytype)
    {
        if (keytype != L.L_INT_TYPE && keytype != L.L_UINT_TYPE &&
            keytype != L.L_FLOAT_TYPE)
            throw Lept.Arg("l_amapCreate", "invalid keytype");

        return new L_Amap(L_Rbtree.Create(keytype));
    }

    // leptonica: l_amapFind (map.c)
    /// <returns>the value if the key is found; null if not found</returns>
    public RbType? Find(RbType key)
    {
        return tree.Lookup(key);
    }

    // leptonica: l_amapInsert (map.c)
    /// <summary>Inserts a key and value into the map.</summary>
    /// <param name="key">The key to insert.</param>
    /// <param name="value">The value to store with the key.</param>
    /// <remarks>If the key is already in the map, only its value is updated.</remarks>
    public void Insert(RbType key, RbType value)
    {
        tree.Insert(key, value);
    }

    // leptonica: l_amapDelete (map.c)
    /// <summary>Removes the entry with the given key from the map.</summary>
    /// <param name="key">The key of the entry to remove.</param>
    public void Delete(RbType key)
    {
        tree.Delete(key);
    }

    // leptonica: l_amapGetFirst (map.c)
    /// <summary>Gets the node with the smallest key.</summary>
    /// <returns>The first node in key order, or <see langword="null"/> if the map is empty.</returns>
    public L_RbtreeNode? GetFirst()
    {
        return tree.GetFirst();
    }

    // leptonica: l_amapGetNext (map.c)
    /// <summary>Gets the node that follows the given node in key order.</summary>
    /// <param name="n">The current node.</param>
    /// <returns>The next node, or <see langword="null"/> if <paramref name="n"/> is the last node.</returns>
    public static L_RbtreeNode? GetNext(L_RbtreeNode n)
    {
        return L_Rbtree.GetNext(n);
    }

    // leptonica: l_amapGetLast (map.c)
    /// <summary>Gets the node with the largest key.</summary>
    /// <returns>The last node in key order, or <see langword="null"/> if the map is empty.</returns>
    public L_RbtreeNode? GetLast()
    {
        return tree.GetLast();
    }

    // leptonica: l_amapGetPrev (map.c)
    /// <summary>Gets the node that precedes the given node in key order.</summary>
    /// <param name="n">The current node.</param>
    /// <returns>The previous node, or <see langword="null"/> if <paramref name="n"/> is the first node.</returns>
    public static L_RbtreeNode? GetPrev(L_RbtreeNode n)
    {
        return L_Rbtree.GetPrev(n);
    }

    // leptonica: l_amapSize (map.c)
    /// <summary>Gets the number of entries in the map.</summary>
    public int Size => tree.GetCount();
}

/// <summary>Ordered set (L_ASET), an rbtree in disguise.</summary>
public sealed partial class L_Aset
{
    private readonly L_Rbtree tree;

    private L_Aset(L_Rbtree tree)
    {
        this.tree = tree;
    }

    // leptonica: l_asetCreate (map.c)
    /// <param name="keytype">L_INT_TYPE, L_UINT_TYPE or L_FLOAT_TYPE</param>
    public static L_Aset Create(int keytype)
    {
        if (keytype != L.L_INT_TYPE && keytype != L.L_UINT_TYPE &&
            keytype != L.L_FLOAT_TYPE)
            throw Lept.Arg("l_asetCreate", "invalid keytype");

        return new L_Aset(L_Rbtree.Create(keytype));
    }

    // leptonica: l_asetFind (map.c)
    /// <returns>the key if found; null if not found.  In the latter case,
    /// the value stored has no significance.</returns>
    public RbType? Find(RbType key)
    {
        return tree.Lookup(key);
    }

    // leptonica: l_asetInsert (map.c)
    /// <summary>Inserts a key into the set.</summary>
    /// <param name="key">The key to insert.</param>
    /// <remarks>If the key is already in the set, nothing changes.</remarks>
    public void Insert(RbType key)
    {
        RbType value = default;
        value.Itype = 0;  /* meaningless */
        tree.Insert(key, value);
    }

    // leptonica: l_asetDelete (map.c)
    /// <summary>Removes a key from the set.</summary>
    /// <param name="key">The key to remove.</param>
    public void Delete(RbType key)
    {
        tree.Delete(key);
    }

    // leptonica: l_asetGetFirst (map.c)
    /// <summary>Gets the node with the smallest key.</summary>
    /// <returns>The first node in key order, or <see langword="null"/> if the set is empty.</returns>
    public L_RbtreeNode? GetFirst()
    {
        return tree.GetFirst();
    }

    // leptonica: l_asetGetNext (map.c)
    /// <summary>Gets the node that follows the given node in key order.</summary>
    /// <param name="n">The current node.</param>
    /// <returns>The next node, or <see langword="null"/> if <paramref name="n"/> is the last node.</returns>
    public static L_RbtreeNode? GetNext(L_RbtreeNode n)
    {
        return L_Rbtree.GetNext(n);
    }

    // leptonica: l_asetGetLast (map.c)
    /// <summary>Gets the node with the largest key.</summary>
    /// <returns>The last node in key order, or <see langword="null"/> if the set is empty.</returns>
    public L_RbtreeNode? GetLast()
    {
        return tree.GetLast();
    }

    // leptonica: l_asetGetPrev (map.c)
    /// <summary>Gets the node that precedes the given node in key order.</summary>
    /// <param name="n">The current node.</param>
    /// <returns>The previous node, or <see langword="null"/> if <paramref name="n"/> is the first node.</returns>
    public static L_RbtreeNode? GetPrev(L_RbtreeNode n)
    {
        return L_Rbtree.GetPrev(n);
    }

    // leptonica: l_asetSize (map.c)
    /// <summary>Gets the number of keys in the set.</summary>
    public int Size => tree.GetCount();
}
