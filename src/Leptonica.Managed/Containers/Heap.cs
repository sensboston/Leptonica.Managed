using System;
using System.IO;

namespace Leptonica;

/*
 *  heap.c: Priority queue (L_Heap).
 *
 *  The L_Heap is useful to implement a priority queue, that is sorted
 *  on a key in each element of the heap.  The heap is an array of items;
 *  the key of an item is a float, which in C is the first field of the
 *  struct that the item points to.
 *
 *  Internally, we keep track of the heap size, n.  The item at the
 *  root of the heap is at the head of the array.  Items are removed
 *  from the head of the array and added to the end of the array.
 *  When an item is removed from the head, the item at the end
 *  of the array is moved to the head.  When items are either
 *  added or removed, it is usually necessary to swap array items
 *  to restore the heap order.  It is guaranteed that the number
 *  of swaps does not exceed log(n).
 *
 *  Differences from C:
 *    - The items are object references that implement <see cref="IHeapItem"/>;
 *      the key is read from the item at each comparison, as C reads it
 *      through the pointer.
 *    - No lheapDestroy(): the GC owns the items (not ported).
 */

/// <summary>An item of an <see cref="L_Heap"/>; its key is the float on which the heap is sorted.</summary>
public interface IHeapItem
{
    /// <summary>The key of the item (the first field of the struct in C).</summary>
    float HeapKey { get; }
}

/// <summary>Priority queue (struct L_Heap).</summary>
public sealed class L_Heap
{
    /* Bounds on initial array size */
    private const uint MaxPtrArraySize = 100000;
    private const int InitialPtrArraySize = 20;      /* n'importe quoi */

    private IHeapItem?[] array;   /* ptr array */
    private int n;                /* number of elements stored in the heap */

    /// <summary>Allocated size of the array.</summary>
    public int NAlloc => array.Length;

    /// <summary>L_SORT_INCREASING or L_SORT_DECREASING.</summary>
    public int Direction { get; set; }

    private L_Heap(int nalloc, int direction)
    {
        array = new IHeapItem?[nalloc];
        n = 0;
        Direction = direction;
    }

    private void SwapItems(int i, int j)
    {
        IHeapItem? tempitem = array[i];
        array[i] = array[j];
        array[j] = tempitem;
    }

    /*--------------------------------------------------------------------------*
     *                          L_Heap create/destroy                           *
     *--------------------------------------------------------------------------*/

    // leptonica: lheapCreate (heap.c)
    /// <summary>Creates a heap.</summary>
    /// <param name="n">size of ptr array to be alloc'd; use 0 for default</param>
    /// <param name="direction">L_SORT_INCREASING, L_SORT_DECREASING</param>
    public static L_Heap Create(int n, int direction)
    {
        if (n < InitialPtrArraySize || n > MaxPtrArraySize)
            n = InitialPtrArraySize;

        return new L_Heap(n, direction);
    }

    /*--------------------------------------------------------------------------*
     *                Operations to add/remove to/from the heap                 *
     *--------------------------------------------------------------------------*/

    // leptonica: lheapAdd (heap.c)
    /// <summary>Adds an item at the tail of the heap and restores the heap order.</summary>
    /// <param name="item">to be added to the tail of the heap</param>
    public void Add(IHeapItem item)
    {
        if (item == null)
            throw Lept.Arg("lheapAdd", "item not defined");

            /* If necessary, expand the allocated array by a factor of 2 */
        if (n >= array.Length)
            ExtendArray();

            /* Add the item */
        array[n] = item;
        n++;

            /* Restore the heap */
        SwapUp(n - 1);
    }

    // leptonica: lheapExtendArray (heap.c)
    private void ExtendArray()
    {
        Array.Resize(ref array, 2 * array.Length);
    }

    // leptonica: lheapRemove (heap.c)
    /// <summary>Pops the item at the root of the heap.</summary>
    /// <returns>the item popped from the root of the heap, or null if the heap is empty</returns>
    public IHeapItem? Remove()
    {
        if (n == 0)
            return null;

        IHeapItem? item = array[0];
        array[0] = array[n - 1];  /* move last to the head */
        array[n - 1] = null;  /* set ptr to null */
        n--;

        SwapDown();  /* restore the heap */
        return item;
    }

    /*--------------------------------------------------------------------------*
     *                            Other accessors                               *
     *--------------------------------------------------------------------------*/

    // leptonica: lheapGetCount (heap.c)
    /// <summary>Number of items in the heap.</summary>
    public int Count => n;

    // leptonica: lheapGetElement (heap.c)
    /// <summary>Returns the element at array[index] without disturbing the heap.</summary>
    /// <param name="index">into the internal heap array</param>
    /// <remarks>
    /// (1) This is useful for retrieving an arbitrary element in the
    ///     heap array without disturbing the heap.  It allows all the
    ///     elements on the heap to be queried in linear time; for
    ///     example, to find the min or max of some value.
    /// </remarks>
    public IHeapItem GetElement(int index)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("lheapGetElement", "invalid index");

        return array[index]!;
    }

    /*--------------------------------------------------------------------------*
     *                                 Heap sort                                *
     *--------------------------------------------------------------------------*/

    // leptonica: lheapSort (heap.c)
    /// <summary>Sorts the internal array into heap order.</summary>
    /// <remarks>
    /// (1) This sorts an array into heap order.  If the heap is already
    ///     in heap order for the direction given, this has no effect.
    /// </remarks>
    public void Sort()
    {
        for (int i = 0; i < n; i++)
            SwapUp(i);
    }

    // leptonica: lheapSortStrictOrder (heap.c)
    /// <summary>Sorts a heap into strict order.</summary>
    /// <remarks>
    /// (1) This sorts a heap into strict order.
    /// (2) For each element, starting at the end of the array and
    ///     working forward, the element is swapped with the head
    ///     element and then allowed to swap down onto a heap of
    ///     size reduced by one.  The result is that the heap is
    ///     reversed but in strict order.  The array elements are
    ///     then reversed to put it in the original order.
    /// </remarks>
    public void SortStrictOrder()
    {
            /* Start from a sorted heap */
        Sort();

        int size = n;  /* save the actual size */
        for (int i = 0; i < size; i++) {
            int index = size - i;
            SwapItems(0, index - 1);
            n--;  /* reduce the apparent heap size by 1 */
            SwapDown();
        }
        n = size;  /* restore the size */

        for (int i = 0; i < size / 2; i++)  /* reverse */
            SwapItems(i, size - i - 1);
    }

    /*--------------------------------------------------------------------------*
     *                         Low-level heap operations                        *
     *--------------------------------------------------------------------------*/

    // leptonica: lheapSwapUp (heap.c)
    /// <remarks>
    /// (1) This is called after a new item is put on the heap, at the
    ///     bottom of a complete tree.
    /// (2) To regain the heap order, we let it bubble up,
    ///     iteratively swapping with its parent, until it either
    ///     reaches the root of the heap or it finds a parent that
    ///     is in the correct position already vis-a-vis the child.
    /// </remarks>
    private void SwapUp(int index)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("lheapSwapUp", "invalid index");

        int ic = index + 1;  /* index into heap: add 1 to array index */
        if (Direction == L.L_SORT_INCREASING) {
            while (true) {
                if (ic == 1)  /* root of heap */
                    break;
                int ip = ic / 2;  /* index to heap for parent; 1 larger than array index */
                float valc = array[ic - 1]!.HeapKey;
                float valp = array[ip - 1]!.HeapKey;
                if (valp <= valc)
                    break;
                SwapItems(ip - 1, ic - 1);
                ic = ip;
            }
        } else {  /* Direction == L_SORT_DECREASING */
            while (true) {
                if (ic == 1)  /* root of heap */
                    break;
                int ip = ic / 2;
                float valc = array[ic - 1]!.HeapKey;
                float valp = array[ip - 1]!.HeapKey;
                if (valp >= valc)
                    break;
                SwapItems(ip - 1, ic - 1);
                ic = ip;
            }
        }
    }

    // leptonica: lheapSwapDown (heap.c)
    /// <remarks>
    /// (1) This is called after an item has been popped off the
    ///     root of the heap, and the last item in the heap has
    ///     been placed at the root.
    /// (2) To regain the heap order, we let it bubble down,
    ///     iteratively swapping with one of its children.  For a
    ///     decreasing sort, it swaps with the largest child; for
    ///     an increasing sort, the smallest.  This continues until
    ///     it either reaches the lowest level in the heap, or the
    ///     parent finds that neither child should swap with it
    ///     (e.g., for a decreasing heap, the parent is larger
    ///     than or equal to both children).
    /// </remarks>
    private void SwapDown()
    {
        if (Count < 1)
            return;

        int ip = 1;  /* index into top of heap: corresponds to array[0] */
        if (Direction == L.L_SORT_INCREASING) {
            while (true) {
                int icl = 2 * ip;  /* index into heap for left child */
                if (icl > n)
                    break;
                float valp = array[ip - 1]!.HeapKey;
                float valcl = array[icl - 1]!.HeapKey;
                int icr = icl + 1;  /* index into heap for right child */
                if (icr > n) {  /* only a left child; no iters below */
                    if (valp > valcl)
                        SwapItems(ip - 1, icl - 1);
                    break;
                } else {  /* both children exist; swap with the smallest if bigger */
                    float valcr = array[icr - 1]!.HeapKey;
                    if (valp <= valcl && valp <= valcr)  /* smaller than both */
                        break;
                    if (valcl <= valcr) {  /* left smaller; swap */
                        SwapItems(ip - 1, icl - 1);
                        ip = icl;
                    } else { /* right smaller; swap */
                        SwapItems(ip - 1, icr - 1);
                        ip = icr;
                    }
                }
            }
        } else {  /* Direction == L_SORT_DECREASING */
            while (true) {
                int icl = 2 * ip;
                if (icl > n)
                    break;
                float valp = array[ip - 1]!.HeapKey;
                float valcl = array[icl - 1]!.HeapKey;
                int icr = icl + 1;
                if (icr > n) {  /* only a left child; no iters below */
                    if (valp < valcl)
                        SwapItems(ip - 1, icl - 1);
                    break;
                } else {  /* both children exist; swap with the biggest if smaller */
                    float valcr = array[icr - 1]!.HeapKey;
                    if (valp >= valcl && valp >= valcr)  /* bigger than both */
                        break;
                    if (valcl >= valcr) {  /* left bigger; swap */
                        SwapItems(ip - 1, icl - 1);
                        ip = icl;
                    } else {  /* right bigger; swap */
                        SwapItems(ip - 1, icr - 1);
                        ip = icr;
                    }
                }
            }
        }
    }

    /*---------------------------------------------------------------------*
     *                            Debug output                             *
     *---------------------------------------------------------------------*/

    // leptonica: lheapPrint (heap.c)
    /// <summary>Writes the heap to a stream, for debugging.</summary>
    /// <remarks>The array address, printed by C with %p, is not printed.</remarks>
    public void Print(Stream fp)
    {
        CStreamText.Fprintf(fp, "\n L_Heap: nalloc = %d, n = %d\n", NAlloc, n);
        for (int i = 0; i < n; i++)
            CStreamText.Fprintf(fp, "keyval[%d] = %f\n", i, (double)array[i]!.HeapKey);
    }
}
