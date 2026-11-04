using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Leptonica;

/*
 *  rbtree.c
 *
 *  Modified from the excellent code here:
 *      http://en.literateprograms.org/Red-black_tree_(C)?oldid=19567
 *  which has been placed in the public domain under the Creative Commons
 *  CC0 1.0 waiver (http://creativecommons.org/publicdomain/zero/1.0/).
 *
 *  Basic functions for using red-black trees.  These are "nearly" balanced
 *  sorted trees with ordering by key that allows insertion, lookup and
 *  deletion of key/value pairs in log(n) time.
 *
 *  We use red-black trees to implement our version of:
 *    * a map: a function that maps keys to values (e.g., int64 --> int64).
 *    * a set: a collection that is sorted by unique keys (without
 *      associated values)
 *
 *  There are 5 invariant properties of RB trees:
 *  (1) Each node is either red or black.
 *  (2) The root node is black.
 *  (3) All leaves are black and contain no data (null).
 *  (4) Every red node has two children and both are black.  This is
 *      equivalent to requiring the parent of every red node to be black.
 *  (5) All paths from any given node to its leaf nodes contain the
 *      same number of black nodes.
 *
 *  Interface to red-black tree
 *           L_Rbtree.Create (l_rbtreeCreate)
 *           L_Rbtree.Lookup (l_rbtreeLookup)
 *           L_Rbtree.Insert (l_rbtreeInsert)
 *           L_Rbtree.Delete (l_rbtreeDelete)
 *           L_Rbtree.GetFirst (l_rbtreeGetFirst)
 *           L_Rbtree.GetNext (l_rbtreeGetNext)
 *           L_Rbtree.GetLast (l_rbtreeGetLast)
 *           L_Rbtree.GetPrev (l_rbtreeGetPrev)
 *           L_Rbtree.GetCount (l_rbtreeGetCount)
 *           L_Rbtree.Print (l_rbtreePrint)
 *
 *  General comparison function
 *           private L_Rbtree.CompareKeys (compareKeys)
 *
 *  Differences from C:
 *    - l_rbtreeDestroy is not ported (the GC owns the nodes).
 *    - l_rbtreeLookup returns a copy of the value (RbType?), not a pointer
 *      to it; use Insert to change the value stored under a key.
 *    - The void* member of union Rb_Type is not represented.
 *    - The debugging verification (VERIFY_RBTREE is 0 in the C build) is not ported.
 */

public static partial class L
{
    /* Keytypes (rbtree.h) */
    /// <summary>Keys and values are signed integers (<see cref="RbType.Itype"/>).</summary>
    public const int L_INT_TYPE = 1;
    /// <summary>Keys and values are unsigned integers (<see cref="RbType.Utype"/>).</summary>
    public const int L_UINT_TYPE = 2;
    /// <summary>Keys and values are floating-point numbers (<see cref="RbType.Ftype"/>).</summary>
    public const int L_FLOAT_TYPE = 3;
}

/// <summary>
/// Key and value type of the rbtree (union Rb_Type).  The void* member (ptype)
/// is not represented.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct RbType
{
    /// <summary>The value interpreted as a signed integer.</summary>
    [FieldOffset(0)] public long Itype;
    /// <summary>The value interpreted as an unsigned integer.</summary>
    [FieldOffset(0)] public ulong Utype;
    /// <summary>The value interpreted as a floating-point number.</summary>
    [FieldOffset(0)] public double Ftype;
}

/// <summary>Node of a red-black tree (struct L_Rbtree_Node; also L_AMAP_NODE, L_ASET_NODE).</summary>
public sealed class L_RbtreeNode
{
    /// <summary>The key of this node.</summary>
    public RbType Key;
    /// <summary>The value stored under the key.</summary>
    public RbType Value;
    /// <summary>The left child, or <c>null</c> if there is none.</summary>
    public L_RbtreeNode? Left;
    /// <summary>The right child, or <c>null</c> if there is none.</summary>
    public L_RbtreeNode? Right;
    /// <summary>The parent node, or <c>null</c> for the root.</summary>
    public L_RbtreeNode? Parent;
    /// <summary>The node color (red or black), used to keep the tree balanced.</summary>
    public int Color;
}

/// <summary>Red-black tree (struct L_Rbtree; L_AMAP and L_ASET are wrappers around it).</summary>
public sealed class L_Rbtree
{
    /* The node color enum is only needed in the rbtree implementation */
    private const int L_RED_NODE = 1;
    private const int L_BLACK_NODE = 2;

    private L_RbtreeNode? root;
    private readonly int keytype;

    /// <summary>Root node of the tree; null if the tree is empty.</summary>
    public L_RbtreeNode? Root => root;

    /// <summary>The key type, one of L_INT_TYPE, L_UINT_TYPE, L_FLOAT_TYPE.</summary>
    public int KeyType => keytype;

    private L_Rbtree(int keytype)
    {
        this.keytype = keytype;
    }

    /* ------------------------------------------------------------- *
     *                   Interface to Red-black Tree                 *
     * ------------------------------------------------------------- */
    // leptonica: l_rbtreeCreate (rbtree.c)
    /// <summary>Creates an empty red-black tree.</summary>
    /// <param name="keytype">defined by an enum for an RB_TYPE union</param>
    /// <returns>rbtree container with empty ptr to the root</returns>
    public static L_Rbtree Create(int keytype)
    {
        if (keytype != L.L_INT_TYPE && keytype != L.L_UINT_TYPE &&
            keytype != L.L_FLOAT_TYPE && keytype != 0)
            throw Lept.Arg("l_rbtreeCreate", "invalid keytype");

        return new L_Rbtree(keytype);
    }

    // leptonica: l_rbtreeLookup (rbtree.c)
    /// <summary>Finds the node with the given key.</summary>
    /// <param name="key">find a node with this key</param>
    /// <returns>the value, if the node exists; else null</returns>
    public RbType? Lookup(RbType key)
    {
        L_RbtreeNode? n = LookupNode(key);
        return n == null ? null : n.Value;
    }

    // leptonica: l_rbtreeInsert (rbtree.c)
    /// <summary>Inserts a node with the given key and value.</summary>
    /// <param name="key">insert a node with this key, if the key does not
    ///                   already exist in the tree</param>
    /// <param name="value">typically an int, used for an index</param>
    /// <remarks>
    /// (1) If a node with the key already exists, this just updates the value.
    /// </remarks>
    public void Insert(RbType key, RbType value)
    {
        L_RbtreeNode inserted_node = NewNode(key, value, L_RED_NODE, null, null);
        if (root == null) {
            root = inserted_node;
        } else {
            L_RbtreeNode n = root;
            while (true) {
                int comp_result = CompareKeys(keytype, key, n.Key);
                if (comp_result == 0) {
                    n.Value = value;
                    return;
                } else if (comp_result < 0) {
                    if (n.Left == null) {
                        n.Left = inserted_node;
                        break;
                    } else {
                        n = n.Left;
                    }
                } else {  /* comp_result > 0 */
                    if (n.Right == null) {
                        n.Right = inserted_node;
                        break;
                    } else {
                        n = n.Right;
                    }
                }
            }
            inserted_node.Parent = n;
        }
        InsertCase1(inserted_node);
    }

    // leptonica: l_rbtreeDelete (rbtree.c)
    /// <summary>Deletes the node with the given key.</summary>
    /// <param name="key">delete the node with this key</param>
    public void Delete(RbType key)
    {
        L_RbtreeNode? n = LookupNode(key);
        if (n == null) return;  /* Key not found, do nothing */
        if (n.Left != null && n.Right != null) {
                /* Copy key/value from predecessor and then delete it instead */
            L_RbtreeNode pred = MaximumNode(n.Left)!;
            n.Key = pred.Key;
            n.Value = pred.Value;
            n = pred;
        }

            /* n->left == NULL || n->right == NULL */
        L_RbtreeNode? child = n.Right == null ? n.Left : n.Right;
        if (NodeColor(n) == L_BLACK_NODE) {
            n.Color = NodeColor(child);
            DeleteCase1(n);
        }
        ReplaceNode(n, child);
        if (n.Parent == null && child != null)  /* root should be black */
            child.Color = L_BLACK_NODE;
    }

    // leptonica: l_rbtreeGetFirst (rbtree.c)
    /// <summary>Gets the first node in an in-order traversal.</summary>
    /// <returns>first node, or null if the tree is empty</returns>
    public L_RbtreeNode? GetFirst()
    {
        if (root == null) {
            Log.Info("l_rbtreeGetFirst", "tree is empty\n");
            return null;
        }

            /* Just go down the left side as far as possible */
        L_RbtreeNode? n = root;
        while (n != null && n.Left != null)
            n = n.Left;
        return n;
    }

    // leptonica: l_rbtreeGetNext (rbtree.c)
    /// <summary>Gets the next node, in an in-order traversal, from the current node.</summary>
    /// <param name="n">current node</param>
    /// <returns>next node, or null if it's the last node</returns>
    /// <remarks>
    /// (1) This finds the next node, in an in-order traversal, from
    ///     the current node.
    /// (2) It is useful as an iterator for a map.
    /// (3) Call l_rbtreeGetFirst() to get the first node.
    /// </remarks>
    public static L_RbtreeNode? GetNext(L_RbtreeNode n)
    {
            /* If there is a right child, go to it, and then go left all the
             * way to the end.  Otherwise go up to the parent; continue upward
             * as long as you're on the right branch, but stop at the parent
             * when you hit it from the left branch. */
        if (n.Right != null) {
            n = n.Right;
            while (n.Left != null)
                n = n.Left;
            return n;
        } else {
            while (n.Parent != null && n.Parent.Right == n)
                n = n.Parent;
            return n.Parent;
        }
    }

    // leptonica: l_rbtreeGetLast (rbtree.c)
    /// <summary>Gets the last node in an in-order traversal.</summary>
    /// <returns>last node, or null if the tree is empty</returns>
    public L_RbtreeNode? GetLast()
    {
        if (root == null) {
            Log.Info("l_rbtreeGetLast", "tree is empty\n");
            return null;
        }

            /* Just go down the right side as far as possible */
        L_RbtreeNode? n = root;
        while (n != null && n.Right != null)
            n = n.Right;
        return n;
    }

    // leptonica: l_rbtreeGetPrev (rbtree.c)
    /// <summary>Gets the previous node, in an in-order traversal, from the current node.</summary>
    /// <param name="n">current node</param>
    /// <returns>previous node, or null if it's the first node</returns>
    /// <remarks>
    /// (1) This finds the previous node, in an in-order traversal, from
    ///     the current node.
    /// (2) It is useful as an iterator for a map.
    /// (3) Call l_rbtreeGetLast() to get the last node.
    /// </remarks>
    public static L_RbtreeNode? GetPrev(L_RbtreeNode n)
    {
            /* If there is a left child, go to it, and then go right all the
             * way to the end.  Otherwise go up to the parent; continue upward
             * as long as you're on the left branch, but stop at the parent
             * when you hit it from the right branch. */
        if (n.Left != null) {
            n = n.Left;
            while (n.Right != null)
                n = n.Right;
            return n;
        } else {
            while (n.Parent != null && n.Parent.Left == n)
                n = n.Parent;
            return n.Parent;
        }
    }

    // leptonica: l_rbtreeGetCount (rbtree.c)
    /// <summary>Counts the nodes in the tree.</summary>
    /// <returns>the number of nodes in the tree</returns>
    public int GetCount()
    {
        int count = 0;
        CountHelper(root, ref count);
        return count;
    }

        /* preorder DFS */
    private static void CountHelper(L_RbtreeNode? n, ref int pcount)
    {
        if (n != null)
            pcount++;
        else
            return;

        CountHelper(n.Left, ref pcount);
        CountHelper(n.Right, ref pcount);
    }

    // leptonica: l_rbtreePrint (rbtree.c)
    /// <summary>Prints the tree, rotated by 90 degrees, to the stream.</summary>
    /// <param name="fp">file stream</param>
    public void Print(Stream fp)
    {
        PrintTreeHelper(fp, root, keytype, 0);
        CStdio.Fprintf(fp, $"\n");
    }

    private const int INDENT_STEP = 4;

    private static void PrintTreeHelper(Stream fp, L_RbtreeNode? n, int keytype, int indent)
    {
        if (n == null) {
            CStdio.Fprintf(fp, $"<empty tree>");
            return;
        }
        if (n.Right != null) {
            PrintTreeHelper(fp, n.Right, keytype, indent + INDENT_STEP);
        }
        for (int i = 0; i < indent; i++)
            CStdio.Fprintf(fp, $" ");
        if (n.Color == L_BLACK_NODE) {
            if (keytype == L.L_INT_TYPE)
                CStdio.Fprintf(fp, $"{CFormat.Sprintf("%lld\n", n.Key.Itype)}");
            else if (keytype == L.L_UINT_TYPE)
                CStdio.Fprintf(fp, $"{CFormat.Sprintf("%llx\n", n.Key.Utype)}");
            else if (keytype == L.L_FLOAT_TYPE)
                CStdio.Fprintf(fp, $"{CFormat.Sprintf("%f\n", n.Key.Ftype)}");
        } else {
            if (keytype == L.L_INT_TYPE)
                CStdio.Fprintf(fp, $"{CFormat.Sprintf("<%lld>\n", n.Key.Itype)}");
            else if (keytype == L.L_UINT_TYPE)
                CStdio.Fprintf(fp, $"{CFormat.Sprintf("<%llx>\n", n.Key.Utype)}");
            else if (keytype == L.L_FLOAT_TYPE)
                CStdio.Fprintf(fp, $"{CFormat.Sprintf("<%f>\n", n.Key.Ftype)}");
        }
        if (n.Left != null) {
            PrintTreeHelper(fp, n.Left, keytype, indent + INDENT_STEP);
        }
    }

    /* ------------------------------------------------------------- *
     *                Static key comparison function                 *
     * ------------------------------------------------------------- */
    // leptonica: compareKeys (rbtree.c)
    private static int CompareKeys(int keytype, RbType left, RbType right)
    {
        if (keytype == L.L_INT_TYPE) {
            if (left.Itype < right.Itype)
                return -1;
            else if (left.Itype > right.Itype)
                return 1;
            else {  /* equality */
                return 0;
            }
        } else if (keytype == L.L_UINT_TYPE) {
            if (left.Utype < right.Utype)
                return -1;
            else if (left.Utype > right.Utype)
                return 1;
            else {  /* equality */
                return 0;
            }
        } else if (keytype == L.L_FLOAT_TYPE) {
            if (left.Ftype < right.Ftype)
                return -1;
            else if (left.Ftype > right.Ftype)
                return 1;
            else {  /* equality */
                return 0;
            }
        } else {
            Log.Error("compareKeys", $"unknown keytype {keytype}\n");
            return 0;
        }
    }

    /* ------------------------------------------------------------- *
     *                  Static red-black tree helpers                *
     * ------------------------------------------------------------- */
    private static L_RbtreeNode? Grandparent(L_RbtreeNode? n)
    {
        if (n == null || n.Parent == null || n.Parent.Parent == null) {
            Log.Error("grandparent", "root and child of root have no grandparent\n");
            return null;
        }
        return n.Parent.Parent;
    }

    private static L_RbtreeNode? Sibling(L_RbtreeNode? n)
    {
        if (n == null || n.Parent == null) {
            Log.Error("sibling", "root has no sibling\n");
            return null;
        }
        if (n == n.Parent.Left)
            return n.Parent.Right;
        else
            return n.Parent.Left;
    }

    private static L_RbtreeNode? Uncle(L_RbtreeNode? n)
    {
        if (n == null || n.Parent == null || n.Parent.Parent == null) {
            Log.Error("uncle", "root and child of root have no uncle\n");
            return null;
        }
        return Sibling(n.Parent);
    }

    private static int NodeColor(L_RbtreeNode? n)
    {
        return n == null ? L_BLACK_NODE : n.Color;
    }

    private static L_RbtreeNode NewNode(RbType key, RbType value, int node_color,
                                        L_RbtreeNode? left, L_RbtreeNode? right)
    {
        L_RbtreeNode result = new L_RbtreeNode();
        result.Key = key;
        result.Value = value;
        result.Color = node_color;
        result.Left = left;
        result.Right = right;
        if (left != null) left.Parent = result;
        if (right != null) right.Parent = result;
        result.Parent = null;
        return result;
    }

    private L_RbtreeNode? LookupNode(RbType key)
    {
        L_RbtreeNode? n = root;
        while (n != null) {
            int comp_result = CompareKeys(keytype, key, n.Key);
            if (comp_result == 0) {
                return n;
            } else if (comp_result < 0) {
                n = n.Left;
            } else {  /* comp_result > 0 */
                n = n.Right;
            }
        }
        return n;
    }

    private void RotateLeft(L_RbtreeNode n)
    {
        L_RbtreeNode r = n.Right!;
        ReplaceNode(n, r);
        n.Right = r.Left;
        if (r.Left != null) {
            r.Left.Parent = n;
        }
        r.Left = n;
        n.Parent = r;
    }

    private void RotateRight(L_RbtreeNode n)
    {
        L_RbtreeNode L = n.Left!;
        ReplaceNode(n, L);
        n.Left = L.Right;
        if (L.Right != null) {
            L.Right.Parent = n;
        }
        L.Right = n;
        n.Parent = L;
    }

    private void ReplaceNode(L_RbtreeNode oldn, L_RbtreeNode? newn)
    {
        if (oldn.Parent == null) {
            root = newn;
        } else {
            if (oldn == oldn.Parent.Left)
                oldn.Parent.Left = newn;
            else
                oldn.Parent.Right = newn;
        }
        if (newn != null) {
            newn.Parent = oldn.Parent;
        }
    }

    private void InsertCase1(L_RbtreeNode n)
    {
        if (n.Parent == null)
            n.Color = L_BLACK_NODE;
        else
            InsertCase2(n);
    }

    private void InsertCase2(L_RbtreeNode n)
    {
        if (NodeColor(n.Parent) == L_BLACK_NODE)
            return; /* Tree is still valid */
        else
            InsertCase3(n);
    }

    private void InsertCase3(L_RbtreeNode n)
    {
        if (NodeColor(Uncle(n)) == L_RED_NODE) {
            n.Parent!.Color = L_BLACK_NODE;
            Uncle(n)!.Color = L_BLACK_NODE;
            Grandparent(n)!.Color = L_RED_NODE;
            InsertCase1(Grandparent(n)!);
        } else {
            InsertCase4(n);
        }
    }

    private void InsertCase4(L_RbtreeNode n)
    {
        if (n == n.Parent!.Right && n.Parent == Grandparent(n)!.Left) {
            RotateLeft(n.Parent);
            n = n.Left!;
        } else if (n == n.Parent.Left && n.Parent == Grandparent(n)!.Right) {
            RotateRight(n.Parent);
            n = n.Right!;
        }
        InsertCase5(n);
    }

    private void InsertCase5(L_RbtreeNode n)
    {
        n.Parent!.Color = L_BLACK_NODE;
        Grandparent(n)!.Color = L_RED_NODE;
        if (n == n.Parent.Left && n.Parent == Grandparent(n)!.Left) {
            RotateRight(Grandparent(n)!);
        } else if (n == n.Parent.Right && n.Parent == Grandparent(n)!.Right) {
            RotateLeft(Grandparent(n)!);
        } else {
            Log.Error("insert_case5", "identity confusion\n");
        }
    }

    private static L_RbtreeNode? MaximumNode(L_RbtreeNode? n)
    {
        if (n == null) {
            Log.Error("maximum_node", "n not defined\n");
            return null;
        }
        while (n.Right != null) {
            n = n.Right;
        }
        return n;
    }

    private void DeleteCase1(L_RbtreeNode n)
    {
        if (n.Parent == null)
            return;
        else
            DeleteCase2(n);
    }

    private void DeleteCase2(L_RbtreeNode n)
    {
        if (NodeColor(Sibling(n)) == L_RED_NODE) {
            n.Parent!.Color = L_RED_NODE;
            Sibling(n)!.Color = L_BLACK_NODE;
            if (n == n.Parent.Left)
                RotateLeft(n.Parent);
            else
                RotateRight(n.Parent);
        }
        DeleteCase3(n);
    }

    private void DeleteCase3(L_RbtreeNode n)
    {
        if (NodeColor(n.Parent) == L_BLACK_NODE &&
            NodeColor(Sibling(n)) == L_BLACK_NODE &&
            NodeColor(Sibling(n)!.Left) == L_BLACK_NODE &&
            NodeColor(Sibling(n)!.Right) == L_BLACK_NODE) {
            Sibling(n)!.Color = L_RED_NODE;
            DeleteCase1(n.Parent!);
        } else {
            DeleteCase4(n);
        }
    }

    private void DeleteCase4(L_RbtreeNode n)
    {
        if (NodeColor(n.Parent) == L_RED_NODE &&
            NodeColor(Sibling(n)) == L_BLACK_NODE &&
            NodeColor(Sibling(n)!.Left) == L_BLACK_NODE &&
            NodeColor(Sibling(n)!.Right) == L_BLACK_NODE) {
            Sibling(n)!.Color = L_RED_NODE;
            n.Parent!.Color = L_BLACK_NODE;
        } else {
            DeleteCase5(n);
        }
    }

    private void DeleteCase5(L_RbtreeNode n)
    {
        if (n == n.Parent!.Left &&
            NodeColor(Sibling(n)) == L_BLACK_NODE &&
            NodeColor(Sibling(n)!.Left) == L_RED_NODE &&
            NodeColor(Sibling(n)!.Right) == L_BLACK_NODE) {
            Sibling(n)!.Color = L_RED_NODE;
            Sibling(n)!.Left!.Color = L_BLACK_NODE;
            RotateRight(Sibling(n)!);
        } else if (n == n.Parent.Right &&
                   NodeColor(Sibling(n)) == L_BLACK_NODE &&
                   NodeColor(Sibling(n)!.Right) == L_RED_NODE &&
                   NodeColor(Sibling(n)!.Left) == L_BLACK_NODE) {
            Sibling(n)!.Color = L_RED_NODE;
            Sibling(n)!.Right!.Color = L_BLACK_NODE;
            RotateLeft(Sibling(n)!);
        }
        DeleteCase6(n);
    }

    private void DeleteCase6(L_RbtreeNode n)
    {
        Sibling(n)!.Color = NodeColor(n.Parent);
        n.Parent!.Color = L_BLACK_NODE;
        if (n == n.Parent.Left) {
            if (NodeColor(Sibling(n)!.Right) != L_RED_NODE) {
                Log.Error("delete_case6", "right sibling is not RED");
                return;
            }
            Sibling(n)!.Right!.Color = L_BLACK_NODE;
            RotateLeft(n.Parent);
        } else {
            if (NodeColor(Sibling(n)!.Left) != L_RED_NODE) {
                Log.Error("delete_case6", "left sibling is not RED");
                return;
            }
            Sibling(n)!.Left!.Color = L_BLACK_NODE;
            RotateRight(n.Parent);
        }
    }
}
