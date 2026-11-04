using System;

namespace Leptonica;

/*
 *  ptra.c
 *
 *      Ptra creation
 *          L_Ptra<T>.Create (ptraCreate)
 *
 *      Add/insert/remove/replace generic ptr object
 *          L_Ptra<T>.Add (ptraAdd)
 *          private L_Ptra<T>.ExtendArray (ptraExtendArray)
 *          L_Ptra<T>.Insert (ptraInsert)
 *          L_Ptra<T>.Remove (ptraRemove)
 *          L_Ptra<T>.RemoveLast (ptraRemoveLast)
 *          L_Ptra<T>.Replace (ptraReplace)
 *          L_Ptra<T>.Swap (ptraSwap)
 *          L_Ptra<T>.CompactArray (ptraCompactArray)
 *
 *      Other array operations
 *          L_Ptra<T>.Reverse (ptraReverse)
 *          L_Ptra<T>.Join (ptraJoin)
 *
 *      Simple Ptra accessors
 *          L_Ptra<T>.MaxIndex (ptraGetMaxIndex)
 *          L_Ptra<T>.ActualCount (ptraGetActualCount)
 *          L_Ptra<T>.GetPtrToItem (ptraGetPtrToItem)
 *
 *      Ptraa creation
 *          L_Ptraa<T>.Create (ptraaCreate)
 *
 *      Ptraa accessors
 *          L_Ptraa<T>.Size (ptraaGetSize)
 *          L_Ptraa<T>.InsertPtra (ptraaInsertPtra)
 *          L_Ptraa<T>.GetPtra (ptraaGetPtra)
 *
 *      Ptraa conversion
 *          L_Ptraa<T>.FlattenToPtra (ptraaFlattenToPtra)
 *
 *    Notes on the Ptra:
 *
 *    (1) The Ptra is a struct, not an array.  Always use the accessors
 *        in this file, never the fields directly.
 *    (2) Items can be placed anywhere in the allocated ptr array,
 *        including one index beyond the last ptr (in which case the
 *        ptr array is realloc'd).
 *    (3) Thus, the items on the ptr array need not be compacted.  In
 *        general there will be null pointers in the ptr array.
 *    (4) A compacted array will remain compacted on removal if
 *        arbitrary items are removed with compaction, or if items
 *        are removed from the end of the array.
 *    (5) For addition to and removal from the end of the array, this
 *        functions exactly like a stack, and with the same O(1) cost.
 *    (6) This differs from the generic stack in that we allow
 *        random access for insertion, removal and replacement.
 *        Removal can be done without compacting the array.
 *        Insertion into a null ptr in the array has no effect on
 *        the other pointers, but insertion into a location already
 *        occupied by an item has a cost proportional to the
 *        distance to the next null ptr in the array.
 *    (7) Null ptrs are valid input args for both insertion and
 *        replacement; this allows arbitrary swapping.
 *    (8) The item in the array with the largest index is at pa->imax.
 *        This can be any value from -1 (initialized; all array ptrs
 *        are null) up to pa->nalloc - 1 (the last ptr in the array).
 *    (9) In referring to the array: the first ptr is the "top" or
 *        "beginning"; the last pointer is the "bottom" or "end";
 *        items are shifted "up" towards the top when compaction occurs;
 *        and items are shifted "down" towards the bottom when forced to
 *        move due to an insertion.
 *   (10) It should be emphasized that insertion, removal and replacement
 *        are general:
 *         * You can insert an item into any ptr location in the
 *           allocated ptr array, as well as into the next ptr address
 *           beyond the allocated array (in which case a realloc will occur).
 *         * You can remove or replace an item from any ptr location
 *           in the allocated ptr array.
 *         * When inserting into an occupied location, you have
 *           three options for downshifting.
 *         * When removing, you can either leave the ptr null or
 *           compact the array.
 *
 *    Notes on the Ptraa:
 *
 *    (1) The Ptraa is a fixed size ptr array for holding Ptra.
 *        In that respect, it is different from other pointer arrays, which
 *        are extensible and grow using the *Add*() functions.
 *    (2) In general, the Ptra ptrs in the Ptraa can be randomly occupied.
 *        A typical usage is to allow an O(n) horizontal sort of Pix,
 *        where the size of the Ptra array is the width of the image,
 *        and each Ptra is an array of all the Pix at a specific x location.
 *
 *  Differences from C:
 *    - The ptr arrays are generic over a reference type (void* in C).
 *    - ptraDestroy and ptraaDestroy are not ported (the GC owns the items).
 *    - ptraReplace has no "free the old item" mode that frees memory: with
 *      freeflag true the old item is dropped and null is returned.
 *    - Errors that return NULL or 1 in C throw.
 */

public static partial class L
{
    /* Flags for removal from L_Ptra (ptra.h) */
    /// <summary>Clears the removed entry only, leaving a hole in the array.</summary>
    public const int L_NO_COMPACTION = 1;

    /// <summary>Compacts the array after removing an entry.</summary>
    public const int L_COMPACTION = 2;

    /* Flags for insertion into L_Ptra (ptra.h) */

    /// <summary>Chooses the downshift automatically, based on the number of holes.</summary>
    public const int L_AUTO_DOWNSHIFT = 0;

    /// <summary>Downshifts the minimum number of entries below the insertion point.</summary>
    public const int L_MIN_DOWNSHIFT = 1;

    /// <summary>Downshifts all the entries below the insertion point.</summary>
    public const int L_FULL_DOWNSHIFT = 2;

    /* Accessor flags for L_Ptraa (ptra.h) */

    /// <summary>Returns the array handle only; the caller may inspect it but does not own it.</summary>
    public const int L_HANDLE_ONLY = 0;

    /// <summary>Removes the array from the container; the caller owns it.</summary>
    public const int L_REMOVE = 1;
}

/// <summary>Generic array of references with holes (struct L_Ptra).</summary>
public sealed class L_Ptra<T> where T : class
{
        /* Bounds on initial array size */
    private const int DefaultInitPtraSize = 20;     /* n'importe quoi */

    private int nalloc;     /* size of allocated ptr array                */
    private int imax;       /* greatest valid index                       */
    private int nactual;    /* actual number of stored items              */
    private T?[] array;     /* ptr array                                  */

    private L_Ptra(int n)
    {
        array = new T?[n];
        nalloc = n;
        imax = -1;
        nactual = 0;
    }

    /*--------------------------------------------------------------------------*
     *                       Ptra creation and destruction                      *
     *--------------------------------------------------------------------------*/
    // leptonica: ptraCreate (ptra.c)
    /// <summary>Creates a ptra.</summary>
    /// <param name="n">size of ptr array to be alloc'd; use 0 for default</param>
    public static L_Ptra<T> Create(int n)
    {
        if (n > (int)L.MaxInitPtraSize) {
            Log.Error("ptraCreate", $"n = {n} > maxsize = {L.MaxInitPtraSize}\n");
            throw Lept.Fail("ptraCreate", "n too large");
        }
        if (n <= 0) n = DefaultInitPtraSize;

        return new L_Ptra<T>(n);
    }

    /*--------------------------------------------------------------------------*
     *               Add/insert/remove/replace generic ptr object               *
     *--------------------------------------------------------------------------*/
    // leptonica: ptraAdd (ptra.c)
    /// <summary>Adds an item to the next location beyond the largest occupied index.</summary>
    /// <param name="item">generic ptr to a struct</param>
    /// <remarks>
    /// (1) This adds the element to the next location beyond imax,
    ///     which is the largest occupied ptr in the array.  This is
    ///     what you expect from a stack, where all ptrs up to and
    ///     including imax are occupied, but here the occuption of
    ///     items in the array is entirely arbitrary.
    /// </remarks>
    public void Add(T item)
    {
        int imax = MaxIndex;
        if (imax >= nalloc - 1)
            ExtendArray();
        array[imax + 1] = item;
        this.imax++;
        nactual++;
    }

    // leptonica: ptraExtendArray (ptra.c)
    private void ExtendArray()
    {
        Array.Resize(ref array, 2 * nalloc);
        nalloc *= 2;
    }

    // leptonica: ptraInsert (ptra.c)
    /// <summary>Inserts an item at the given location, shifting items down if necessary.</summary>
    /// <param name="index">location in ptra to insert new value</param>
    /// <param name="item">generic ptr to a struct; can be null</param>
    /// <param name="shiftflag">L_AUTO_DOWNSHIFT, L_MIN_DOWNSHIFT, L_FULL_DOWNSHIFT</param>
    /// <remarks>
    /// (1) This checks first to see if the location is valid, and
    ///     then if there is presently an item there.  If there is not,
    ///     it is simply inserted into that location.
    /// (2) If there is an item at the insert location, items must be
    ///     moved down to make room for the insert.  In the downward
    ///     shift there are three options, given by shiftflag.
    ///       ~ If shiftflag == L_AUTO_DOWNSHIFT, a decision is made
    ///         whether, in a cascade of items, to downshift a minimum
    ///         amount or for all items above index.  The decision is
    ///         based on the expectation of finding holes (null ptrs)
    ///         between index and the bottom of the array.
    ///         Assuming the holes are distributed uniformly, if 2 or more
    ///         holes are expected, we do a minimum shift.
    ///       ~ If shiftflag == L_MIN_DOWNSHIFT, the downward shifting
    ///         cascade of items progresses a minimum amount, until
    ///         the first empty slot is reached.  This mode requires
    ///         some computation before the actual shifting is done.
    ///       ~ If shiftflag == L_FULL_DOWNSHIFT, a shifting cascade is
    ///         performed where pa[i] --> pa[i + 1] for all i >= index.
    ///         Then, the item is inserted at pa[index].
    /// (3) If you are not using L_AUTO_DOWNSHIFT, the rule of thumb is
    ///     to use L_FULL_DOWNSHIFT if the array is compacted (each
    ///     element points to an item), and to use L_MIN_DOWNSHIFT
    ///     if there are a significant number of null pointers.
    ///     There is no penalty to using L_MIN_DOWNSHIFT for a
    ///     compacted array, however, because the full shift is required
    ///     and we don't do the O(n) computation to look for holes.
    /// (4) This should not be used repeatedly on large arrays,
    ///     because the function is generally O(n).
    /// (5) However, it can be used repeatedly if we start with an empty
    ///     ptr array and insert only once at each location.  For example,
    ///     you can support an array of Numa, where at each ptr location
    ///     you store either 0 or 1 Numa, and the Numa can be added
    ///     randomly to the ptr array.
    /// </remarks>
    public void Insert(int index, T? item, int shiftflag)
    {
        const string func = "ptraInsert";
        if (index < 0 || index > nalloc)
            throw Lept.Arg(func, "index not in [0 ... nalloc]");
        if (shiftflag != L.L_AUTO_DOWNSHIFT && shiftflag != L.L_MIN_DOWNSHIFT &&
            shiftflag != L.L_FULL_DOWNSHIFT)
            throw Lept.Arg(func, "invalid shiftflag");

        if (item != null) nactual++;
        if (index == nalloc)  /* can happen when index == n */
            ExtendArray();

            /* We are inserting into a hole or adding to the end of the array.
             * No existing items are moved. */
        int imax = MaxIndex;
        if (array[index] == null) {
            array[index] = item;
            if (item != null && index > imax)  /* new item put beyond max so far */
                this.imax = index;
            return;
        }

            /* We are inserting at the location of an existing item,
             * forcing the existing item and those below to shift down.
             * First, extend the array automatically if the last element
             * (nalloc - 1) is occupied (imax).  This may not be necessary
             * in every situation, but only an anomalous sequence of insertions
             * into the array would cause extra ptr allocation.  */
        if (imax >= nalloc - 1)
            ExtendArray();

            /* If there are no holes, do a full downshift.
             * Otherwise, if L_AUTO_DOWNSHIFT, use the expected number
             * of holes between index and n to determine the shift mode */
        if (imax + 1 == nactual) {
            shiftflag = L.L_FULL_DOWNSHIFT;
        } else if (shiftflag == L.L_AUTO_DOWNSHIFT) {
            if (imax < 10) {
                shiftflag = L.L_FULL_DOWNSHIFT;  /* no big deal */
            } else {
                float nexpected = (float)(imax - nactual) *
                                  (float)((imax - index) / imax);
                shiftflag = (nexpected > 2.0) ? L.L_MIN_DOWNSHIFT : L.L_FULL_DOWNSHIFT;
            }
        }

        int ihole;
        if (shiftflag == L.L_MIN_DOWNSHIFT) {  /* run down looking for a hole */
            for (ihole = index + 1; ihole <= imax; ihole++) {
                if (array[ihole] == null)
                    break;
            }
        } else {  /* L_FULL_DOWNSHIFT */
            ihole = imax + 1;
        }

        for (int i = ihole; i > index; i--)
            array[i] = array[i - 1];
        array[index] = item;
        if (ihole == imax + 1)  /* the last item was shifted down */
            this.imax++;
    }

    // leptonica: ptraRemove (ptra.c)
    /// <summary>Removes the item at the given location.</summary>
    /// <param name="index">element to be removed</param>
    /// <param name="flag">L_NO_COMPACTION, L_COMPACTION</param>
    /// <returns>item, or null if there was none at the location</returns>
    /// <remarks>
    /// (1) If flag == L_NO_COMPACTION, this removes the item and
    ///     nulls the ptr on the array.  If it takes the last item
    ///     in the array, pa->n is reduced to the next item.
    /// (2) If flag == L_COMPACTION, this compacts the array for
    ///     for all i >= index.  It should not be used repeatedly on
    ///     large arrays, because compaction is O(n).  (NOTE(upstream): the
    ///     ptrs beyond the new maximum index are not nulled.)
    /// (3) The ability to remove without automatic compaction allows
    ///     removal with cost O(1).
    /// </remarks>
    public T? Remove(int index, int flag)
    {
        int imax = MaxIndex;
        if (index < 0 || index > imax)
            throw Lept.Arg("ptraRemove", "index not in [0 ... imax]");

        T? item = array[index];
        if (item != null)
            nactual--;
        array[index] = null;

            /* If we took the last item, need to reduce pa->n */
        bool fromend = (index == imax);
        if (fromend) {
            int i;
            for (i = index - 1; i >= 0; i--) {
                if (array[i] != null)
                    break;
            }
            this.imax = i;
        }

            /* Compact from index to the end of the array */
        if (!fromend && flag == L.L_COMPACTION) {
            int icurrent = index;
            for (int i = index + 1; i <= imax; i++) {
                if (array[i] != null)
                    array[icurrent++] = array[i];
            }
            this.imax = icurrent - 1;
        }
        return item;
    }

    // leptonica: ptraRemoveLast (ptra.c)
    /// <summary>Removes the last item.</summary>
    /// <returns>item, or null if the array is empty</returns>
    public T? RemoveLast()
    {
            /* Remove the last item in the array.  No compaction is required. */
        int imax = MaxIndex;
        if (imax >= 0)
            return Remove(imax, L.L_NO_COMPACTION);
        else  /* empty */
            return null;
    }

    // leptonica: ptraReplace (ptra.c)
    /// <summary>Replaces the item at the given location.</summary>
    /// <param name="index">element to be replaced</param>
    /// <param name="item">new generic ptr to a struct; can be null</param>
    /// <param name="freeflag">true to drop the old item; false to return it</param>
    /// <returns>old item, if it exists and is not dropped</returns>
    public T? Replace(int index, T? item, bool freeflag)
    {
        int imax = MaxIndex;
        if (index < 0 || index > imax)
            throw Lept.Arg("ptraReplace", "index not in [0 ... imax]");

        T? olditem = array[index];
        array[index] = item;
        if (item == null && olditem != null)
            nactual--;
        else if (item != null && olditem == null)
            nactual++;

        if (freeflag == false)
            return olditem;

        return null;
    }

    // leptonica: ptraSwap (ptra.c)
    /// <summary>Swaps two items.</summary>
    public void Swap(int index1, int index2)
    {
        if (index1 == index2)
            return;
        int imax = MaxIndex;
        if (index1 < 0 || index1 > imax || index2 < 0 || index2 > imax)
            throw Lept.Arg("ptraSwap", "invalid index: not in [0 ... imax]");

        T? item = Remove(index1, L.L_NO_COMPACTION);
        item = Replace(index2, item, false);
        Insert(index1, item, L.L_MIN_DOWNSHIFT);
    }

    // leptonica: ptraCompactArray (ptra.c)
    /// <summary>Compacts the items on the array, filling any empty ptrs.</summary>
    /// <remarks>
    /// (1) This compacts the items on the array, filling any empty ptrs.
    /// (2) This does not change the size of the array of ptrs.
    /// (3) NOTE(upstream): the ptrs beyond the new maximum index are not
    ///     nulled, so they still hold the stale references.
    /// </remarks>
    public void CompactArray()
    {
        int imax = MaxIndex;
        int nactual = ActualCount;
        if (imax + 1 == nactual) return;

            /* Compact the array */
        int index = 0;
        for (int i = 0; i <= imax; i++) {
            if (array[i] != null)
                array[index++] = array[i];
        }
        this.imax = index - 1;
        if (nactual != index)
            Log.Error("ptraCompactArray", $"index = {index}; != nactual\n");
    }

    /*----------------------------------------------------------------------*
     *                        Other array operations                        *
     *----------------------------------------------------------------------*/
    // leptonica: ptraReverse (ptra.c)
    /// <summary>Reverses the order of the items.</summary>
    public void Reverse()
    {
        int imax = MaxIndex;

        for (int i = 0; i < (imax + 1) / 2; i++)
            Swap(i, imax - i);
    }

    // leptonica: ptraJoin (ptra.c)
    /// <summary>Appends the items of pa2 to this ptra, and empties pa2.</summary>
    /// <param name="pa2">appended to this one, and emptied of items; can be null</param>
    public void Join(L_Ptra<T>? pa2)
    {
        if (pa2 == null)
            return;

        int imax = pa2.MaxIndex;
        for (int i = 0; i <= imax; i++) {
            T? item = pa2.Remove(i, L.L_NO_COMPACTION);
            if (item != null)
                Add(item);
            else
                Log.Error("ptraAdd", "item not defined");
        }
    }

    /*----------------------------------------------------------------------*
     *                        Simple ptra accessors                         *
     *----------------------------------------------------------------------*/
    // leptonica: ptraGetMaxIndex (ptra.c)
    /// <summary>Index of the last item in the array.</summary>
    /// <remarks>
    /// (1) The largest index to an item in the array is maxindex.
    ///     maxindex is one less than the number of items that would be
    ///     in the array if there were no null pointers between 0
    ///     and maxindex - 1.  However, because the internal ptr array
    ///     need not be compacted, there may be NULL pointers at
    ///     indices below maxindex; for example, if items have
    ///     been removed.
    /// (2) When an item is added to the end of the array, it goes
    ///     into pa->array[maxindex + 1], and maxindex is then
    ///     incremented by 1.
    /// (3) If there are no items in the array, this returns maxindex = -1.
    /// </remarks>
    public int MaxIndex => imax;

    // leptonica: ptraGetActualCount (ptra.c)
    /// <summary>Actual number of items on the ptr array.</summary>
    /// <remarks>
    /// (1) The actual number of items on the ptr array, pa->nactual,
    ///     will be smaller than pa->n if the array is not compacted.
    /// </remarks>
    public int ActualCount => nactual;

    // leptonica: ptraGetPtrToItem (ptra.c)
    /// <summary>Gets the item at the given index.</summary>
    /// <param name="index">of element to be retrieved</param>
    /// <returns>the element, or null if there is none at the location</returns>
    /// <remarks>
    /// (1) Do not destroy the returned item; it belongs to the Ptra.
    /// (2) This can access all possible items on the ptr array.
    ///     If an item doesn't exist, it returns null.
    /// (3) An index outside the allocated array is an error in C (reported,
    ///     returning NULL), and callers such as numaGetBinSortIndex() rely on
    ///     continuing; here the error is logged and null is returned.
    /// </remarks>
    public T? GetPtrToItem(int index)
    {
        if (index < 0 || index >= nalloc) {
            Log.Error("ptraGetPtrToItem", "index not in [0 ... nalloc-1]");
            return null;
        }

        return array[index];
    }
}

/// <summary>Fixed size array of ptras (struct L_Ptraa).</summary>
public sealed class L_Ptraa<T> where T : class
{
    private readonly int nalloc;            /* size of allocated ptr array  */
    private readonly L_Ptra<T>?[] ptra;     /* array of ptra                */

    private L_Ptraa(int n)
    {
        ptra = new L_Ptra<T>?[n];
        nalloc = n;
    }

    /*--------------------------------------------------------------------------*
     *                      Ptraa creation and destruction                      *
     *--------------------------------------------------------------------------*/
    // leptonica: ptraaCreate (ptra.c)
    /// <summary>Creates a ptraa.</summary>
    /// <param name="n">size of ptr array to be alloc'd</param>
    /// <remarks>
    /// (1) The ptraa is generated with a fixed size, that can not change.
    ///     The ptra can be generated and inserted randomly into this array.
    /// </remarks>
    public static L_Ptraa<T> Create(int n)
    {
        if (n <= 0)
            throw Lept.Arg("ptraaCreate", "n must be > 0");

        return new L_Ptraa<T>(n);
    }

    /*--------------------------------------------------------------------------*
     *                             Ptraa accessors                              *
     *--------------------------------------------------------------------------*/
    // leptonica: ptraaGetSize (ptra.c)
    /// <summary>Size of the ptr array.</summary>
    public int Size => nalloc;

    // leptonica: ptraaInsertPtra (ptra.c)
    /// <summary>Inserts a ptra at the given location.</summary>
    /// <param name="index">location in array for insertion</param>
    /// <param name="pa">to be inserted</param>
    public void InsertPtra(int index, L_Ptra<T> pa)
    {
        const string func = "ptraaInsertPtra";
        if (index < 0 || index >= nalloc)
            throw Lept.Arg(func, "invalid index");
        if (ptra[index] != null)
            throw Lept.Arg(func, "ptra already stored at index");

        ptra[index] = pa;
    }

    // leptonica: ptraaGetPtra (ptra.c)
    /// <summary>Gets the ptra at the given location.</summary>
    /// <param name="index">location in array</param>
    /// <param name="accessflag">L_HANDLE_ONLY, L_REMOVE</param>
    /// <returns>ptra at index location, or null if there is no ptra there</returns>
    /// <remarks>
    /// (1) If accessflag == L_HANDLE_ONLY, the ptra is left on the ptraa.
    ///     If accessflag == L_REMOVE, the ptr in the ptraa is set to null,
    ///     and the caller is responsible for disposing of the ptra (either
    ///     putting it back on the ptraa, or dropping it).
    /// (2) This returns null if there is no Ptra at the index location.
    /// </remarks>
    public L_Ptra<T>? GetPtra(int index, int accessflag)
    {
        const string func = "ptraaGetPtra";
        if (index < 0 || index >= nalloc)
            throw Lept.Arg(func, "invalid index");
        if (accessflag != L.L_HANDLE_ONLY && accessflag != L.L_REMOVE)
            throw Lept.Arg(func, "invalid accessflag");

        L_Ptra<T>? pa = ptra[index];
        if (accessflag == L.L_REMOVE)
            ptra[index] = null;
        return pa;
    }

    /*--------------------------------------------------------------------------*
     *                             Ptraa conversion                             *
     *--------------------------------------------------------------------------*/
    // leptonica: ptraaFlattenToPtra (ptra.c)
    /// <summary>Flattens the ptraa to a ptra.</summary>
    /// <returns>ptra</returns>
    /// <remarks>
    /// (1) This 'flattens' the ptraa to a ptra, taking the items in
    ///     each ptra, in order, starting with the first ptra, etc.
    /// (2) As a side-effect, the ptra are all removed from the ptraa
    ///     and emptied, leaving an empty ptraa.
    /// </remarks>
    public L_Ptra<T> FlattenToPtra()
    {
        L_Ptra<T> pad = L_Ptra<T>.Create(0);
        for (int i = 0; i < nalloc; i++) {
            L_Ptra<T>? pat = GetPtra(i, L.L_REMOVE);
            if (pat == null) continue;
            pad.Join(pat);
        }

        return pad;
    }
}
