using System;

namespace Leptonica;

/*
 *  queue.c: Expandable queue (fifo) of items.
 *
 *      Create L_Queue
 *          L_Queue<T>.Create (lqueueCreate)
 *
 *      Operations to add/remove to/from a L_Queue
 *          L_Queue<T>.Add (lqueueAdd), ExtendArray (lqueueExtendArray), Remove (lqueueRemove)
 *
 *      Accessors
 *          L_Queue<T>.Count (lqueueGetCount)
 *
 *  The lqueue is a fifo that implements a queue of items (void* pointers in C;
 *  here a generic reference type).
 *  Internally, it maintains two counters:
 *      nhead:  location of head (in items) from the beginning
 *              of the buffer
 *      nelem:  number of items stored in the queue
 *  As items are added to the queue, nelem increases.
 *  As items are removed, nhead increases and nelem decreases.
 *  Any time the tail reaches the end of the allocated buffer,
 *    all the items are shifted to the left, so that the head
 *    is at the beginning of the array.
 *  If the buffer becomes more than 3/4 full, it doubles in size.
 *
 *  [A circular queue would allow us to skip the shifting and
 *  to resize only when the buffer is full.  For most applications,
 *  the extra work we do for a linear queue is not significant.]
 *
 *  Not ported: lqueueDestroy (memory management only), lqueuePrint (prints
 *  pointer values), and the auxiliary stack field (it is only touched by
 *  lqueueDestroy; stack.c is not needed by the callers of the queue so far).
 */

/// <summary>Expandable fifo queue (struct L_Queue).</summary>
public sealed class L_Queue<T> where T : class
{
    private const int MIN_BUFFER_SIZE = 20;             /* n'importe quoi */
    private const int INITIAL_BUFFER_ARRAYSIZE = 1024;  /* n'importe quoi */

    private int nalloc;     /* size of allocated array                          */
    private int nhead;      /* location of head from the beginning of the array */
    private int nelem;      /* number of elements stored in the queue           */
    private T?[] array;     /* item array                                       */

    private L_Queue(int nalloc)
    {
        this.nalloc = nalloc;
        array = new T?[nalloc];
        nhead = nelem = 0;
    }

    // leptonica: lqueueCreate (queue.c)
    /// <summary>Creates a queue.</summary>
    /// <param name="nalloc">size of array to be allocated; 0 for default</param>
    /// <remarks>(1) Allocates an array of given size, and initializes counters.</remarks>
    public static L_Queue<T> Create(int nalloc)
    {
        if (nalloc < MIN_BUFFER_SIZE)
            nalloc = INITIAL_BUFFER_ARRAYSIZE;

        return new L_Queue<T>(nalloc);
    }

    /*--------------------------------------------------------------------------*
     *                                  Accessors                               *
     *--------------------------------------------------------------------------*/
    // leptonica: lqueueAdd (queue.c)
    /// <summary>Adds an item to the tail of the queue.</summary>
    /// <param name="item">to be added to the tail of the queue</param>
    /// <remarks>
    /// (1) The algorithm is as follows.  If the queue is populated
    ///     to the end of the allocated array, shift all items toward
    ///     the beginning of the array, so that the head of the queue
    ///     is at the beginning of the array.  Then, if the array is
    ///     more than 0.75 full, realloc with double the array size.
    ///     Finally, add the item to the tail of the queue.
    /// </remarks>
    public void Add(T item)
    {
        /* If filled to the end and the items can be shifted to the left,
         * shift them. */
        if ((nhead + nelem >= nalloc) && (nhead != 0)) {
            Array.Copy(array, nhead, array, 0, nelem);   /* memmove */
            nhead = 0;
        }

        /* If necessary, expand the allocated array by a factor of 2 */
        if (nelem > 0.75 * nalloc)
            ExtendArray();

        /* Now add the item */
        array[nhead + nelem] = item;
        nelem++;
    }

    // leptonica: lqueueExtendArray (queue.c)
    private void ExtendArray()
    {
        Array.Resize(ref array, 2 * nalloc);
        nalloc = 2 * nalloc;
    }

    // leptonica: lqueueRemove (queue.c)
    /// <summary>Pops the item from the head of the queue.</summary>
    /// <returns>item popped from the head of the queue, or null if the queue is empty</returns>
    /// <remarks>
    /// (1) If this is the last item on the queue, so that the queue
    ///     becomes empty, nhead is reset to the beginning of the array.
    /// </remarks>
    public T? Remove()
    {
        if (nelem == 0)
            return null;
        T? item = array[nhead];
        array[nhead] = null;
        if (nelem == 1)
            nhead = 0;  /* reset head ptr */
        else
            nhead++;    /* can't go off end of array because nelem > 1 */
        nelem--;
        return item;
    }

    // leptonica: lqueueGetCount (queue.c)
    /// <summary>Number of items in the queue.</summary>
    public int Count => nelem;

    // leptonica: lqueuePrint (queue.c)
    /// <summary>Writes the queue summary to a stream, for debugging.</summary>
    public void Print(System.IO.Stream fp)
    {
        if (fp == null)
            throw Lept.Arg("lqueuePrint", "stream not defined");

        CStreamText.Fprintf(fp, "\n L_Queue: nalloc = %d, nhead = %d, nelem = %d\n", nalloc, nhead, nelem);
        for (int i = nhead; i < nhead + nelem; i++)
            CStreamText.Fprintf(fp, "array[%d] = %s\n", i, array[i]?.ToString() ?? "null");
    }
}
