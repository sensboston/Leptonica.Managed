using System;

namespace Leptonica;

/*
 *  list.c
 *
 *      Inserting and removing elements
 *
 *           L_Dllist<T>.AddToHead (listAddToHead)
 *           L_Dllist<T>.AddToTail (listAddToTail)
 *           L_Dllist<T>.InsertBefore (listInsertBefore)
 *           L_Dllist<T>.InsertAfter (listInsertAfter)
 *           L_Dllist<T>.RemoveElement (listRemoveElement)
 *           L_Dllist<T>.RemoveFromHead (listRemoveFromHead)
 *           L_Dllist<T>.RemoveFromTail (listRemoveFromTail)
 *
 *      Other list operations
 *
 *           L_Dllist<T>.FindElement (listFindElement)
 *           L_Dllist<T>.FindTail (listFindTail)
 *           L_Dllist<T>.GetCount (listGetCount)
 *           L_Dllist<T>.Reverse (listReverse)
 *           L_Dllist<T>.Join (listJoin)
 *
 *      Lists are much harder to handle than arrays.  There is
 *      more overhead for the programmer, both cognitive and
 *      codewise, and more likelihood that an error can be made.
 *      For that reason, lists should only be used when it is
 *      inefficient to use arrays, such as when elements are
 *      routinely inserted or deleted from inside arrays whose
 *      average size is greater than about 10.
 *
 *      We use lists that are composed of doubly-linked
 *      cells with data structures hanging off the cells.
 *      We use doubly-linked cells to simplify insertion
 *      and deletion, and to allow operations to proceed in either
 *      direction along the list.  With doubly-linked lists,
 *      it is tempting to make them circular, by setting head->prev
 *      to the tail of the list and tail->next to the head.
 *      The circular list costs nothing extra in storage, and
 *      allows operations to proceed from either end of the list
 *      with equal speed.  However, the circular link adds
 *      cognitive overhead for the application programmer in
 *      general, and it greatly complicates list traversal when
 *      arbitrary list elements can be added or removed as you
 *      move through.  It can be done, but in the spirit of
 *      simplicity, we avoid the temptation.  The price to be paid
 *      is the extra cost to find the tail of a list -- a full
 *      traversal -- before the tail can be used.  This is a
 *      cheap price to pay to avoid major headaches and buggy code.
 *
 *      When you are only applying some function to each element
 *      in a list, you can go either forwards or backwards.
 *      To run through a list forwards, use:
 *
 *          for (elem = head; elem != null; elem = nextelem) {
 *              nextelem = elem.Next;   (in case we remove elem)
 *              <do something with elem.Data>
 *          }
 *
 *      To run through a list backwards, find the tail and use:
 *
 *          for (elem = tail; elem != null; elem = prevelem) {
 *              prevelem = elem.Prev;   (in case we remove elem)
 *              <do something with elem.Data>
 *          }
 *
 *      Special cases are simpler.  For example, when removing all
 *      items from the head of the list, you can use
 *
 *          while (head != null) {
 *              obj = L_Dllist<T>.RemoveFromHead(ref head);
 *              <do something with obj>
 *          }
 *
 *      All the Remove*() functions hand you the object, drop the
 *      list cell to which it was attached, and reset the list
 *      pointers if necessary.
 *
 *  Differences from C:
 *    - The list is generic over the type of the data hanging off the cells
 *      (void* in C); the head and tail are passed by reference (DLLIST **).
 *    - listDestroy is not ported (the GC owns the cells), and the traversal
 *      macros of list.h are not needed.
 *    - Errors that return 1 or NULL in C throw.
 */

/// <summary>Doubly-linked list cell (struct DoubleLinkedList, DLLIST).</summary>
public sealed class L_Dllist<T> where T : class
{
    private L_Dllist<T>? prev;
    private L_Dllist<T>? next;
    private T data;

    private L_Dllist(T data)
    {
        this.data = data;
    }

    /// <summary>Previous cell.</summary>
    public L_Dllist<T>? Prev => prev;

    /// <summary>Next cell.</summary>
    public L_Dllist<T>? Next => next;

    /// <summary>The data hanging off the cell.</summary>
    public T Data
    {
        get => data;
        set => data = value;
    }

    /*---------------------------------------------------------------------*
     *                    Inserting and removing elements                  *
     *---------------------------------------------------------------------*/
    // leptonica: listAddToHead (list.c)
    /// <summary>Adds a new cell with the data to the head of the list.</summary>
    /// <param name="head">input head; updated</param>
    /// <param name="data">to be added</param>
    /// <remarks>
    /// (1) This makes a new cell, attaches data, and adds the
    ///     cell to the head of the list.
    /// (2) When consing from null, be sure to initialize head to null
    ///     before calling this function.
    /// </remarks>
    public static void AddToHead(ref L_Dllist<T>? head, T data)
    {
        L_Dllist<T> cell = new L_Dllist<T>(data);
        if (head == null) {  /* start the list; initialize the ptrs */
            cell.prev = null;
            cell.next = null;
        } else {
            cell.prev = null;
            cell.next = head;
            head.prev = cell;
        }
        head = cell;
    }

    // leptonica: listAddToTail (list.c)
    /// <summary>Adds a new cell with the data to the tail of the list.</summary>
    /// <param name="head">may be updated, can be null</param>
    /// <param name="tail">updated, can be null</param>
    /// <param name="data">to be hung on tail cons cell</param>
    /// <remarks>
    /// (1) This makes a new cell, attaches data, and adds the
    ///     cell to the tail of the list.
    /// (2) head is passed by reference to allow the list to be "cons'd" up from null.
    /// (3) tail is passed by reference to allow the tail to be updated
    ///     for efficient sequential operation with this function.
    /// (4) We assume that if head and/or tail are not null,
    ///     then they are valid cells.  Therefore:
    ///      (a) when consing from null, be sure to initialize both
    ///          head and tail to null.
    ///      (b) when tail == null for an existing list, the tail
    ///          will be found and updated.
    /// </remarks>
    public static void AddToTail(ref L_Dllist<T>? head, ref L_Dllist<T>? tail, T data)
    {
        L_Dllist<T> cell = new L_Dllist<T>(data);
        if (head == null) {  /*   Start the list and initialize the ptrs.  tail
                              *   should also have been initialized to null */
            cell.prev = null;
            cell.next = null;
            head = cell;
            tail = cell;
        } else {
            L_Dllist<T> t = tail ?? FindTail(head);
            cell.prev = t;
            cell.next = null;
            t.next = cell;
            tail = cell;
        }
    }

    // leptonica: listInsertBefore (list.c)
    /// <summary>Inserts a new cell with the data before the given element.</summary>
    /// <param name="head">input head; updated</param>
    /// <param name="elem">list element to be inserted in front of;
    ///                    must be null if head is null</param>
    /// <param name="data">to be added</param>
    /// <remarks>
    /// (1) This can be called on a null list, in which case both
    ///     head and elem must be null.
    /// (2) If you are searching through a list, looking for a condition
    ///     to add an element, you can traverse the list and call this
    ///     when the element is identified.
    /// </remarks>
    public static void InsertBefore(ref L_Dllist<T>? head, L_Dllist<T>? elem, T data)
    {
        if ((head == null && elem != null) || (head != null && elem == null))
            throw Lept.Arg("listInsertBefore", "head and elem not consistent");

            /* New cell to insert */
        L_Dllist<T> cell = new L_Dllist<T>(data);
        if (head == null) {  /* start the list; initialize the ptrs */
            cell.prev = null;
            cell.next = null;
            head = cell;
        } else if (head == elem) {  /* insert before head of list */
            cell.prev = null;
            cell.next = head;
            head.prev = cell;
            head = cell;
        } else  {   /* insert before elem and after head of list */
            cell.prev = elem!.prev;
            cell.next = elem;
            elem.prev!.next = cell;
            elem.prev = cell;
        }
    }

    // leptonica: listInsertAfter (list.c)
    /// <summary>Inserts a new cell with the data after the given element.</summary>
    /// <param name="head">input head; updated</param>
    /// <param name="elem">list element to be inserted after;
    ///                    must be null if head is null</param>
    /// <param name="data">to be added</param>
    /// <remarks>
    /// (1) This can be called on a null list, in which case both
    ///     head and elem must be null.  The head is included
    ///     in the call to allow "consing" up from null.
    /// (2) If you are searching through a list, looking for a condition
    ///     to add an element, you can traverse the list and call this
    ///     when the element is identified.
    /// </remarks>
    public static void InsertAfter(ref L_Dllist<T>? head, L_Dllist<T>? elem, T data)
    {
        if ((head == null && elem != null) || (head != null && elem == null))
            throw Lept.Arg("listInsertAfter", "head and elem not consistent");

            /* New cell to insert */
        L_Dllist<T> cell = new L_Dllist<T>(data);
        if (head == null) {  /* start the list; initialize the ptrs */
            cell.prev = null;
            cell.next = null;
            head = cell;
        } else if (elem!.next == null) {  /* insert after last */
            cell.prev = elem;
            cell.next = null;
            elem.next = cell;
        } else  {  /* insert after elem and before the end */
            cell.prev = elem;
            cell.next = elem.next;
            elem.next.prev = cell;
            elem.next = cell;
        }
    }

    // leptonica: listRemoveElement (list.c)
    /// <summary>Removes the given cell from the list.</summary>
    /// <param name="head">input head; can be changed</param>
    /// <param name="elem">list element to be removed</param>
    /// <returns>data on the cell</returns>
    public static T RemoveElement(ref L_Dllist<T>? head, L_Dllist<T> elem)
    {
        if (head == null)
            throw Lept.Arg("listRemoveElement", "head not defined");

        T data = elem.data;

        if (head.next == null) {  /* only one */
            if (elem != head)
                throw Lept.Arg("listRemoveElement", "elem must be head");
            head = null;
        } else if (head == elem) {   /* first one */
            elem.next!.prev = null;
            head = elem.next;
        } else if (elem.next == null) {   /* last one */
            elem.prev!.next = null;
        } else {  /* neither the first nor the last one */
            elem.next.prev = elem.prev;
            elem.prev!.next = elem.next;
        }

        return data;
    }

    // leptonica: listRemoveFromHead (list.c)
    /// <summary>Removes the head cell from the list.</summary>
    /// <param name="head">head of list; updated</param>
    /// <returns>data on the cell</returns>
    public static T RemoveFromHead(ref L_Dllist<T>? head)
    {
        L_Dllist<T>? h = head;
        if (h == null)
            throw Lept.Arg("listRemoveFromHead", "head not defined");

        if (h.next == null) {  /* only one */
            head = null;
        } else {
            h.next.prev = null;
            head = h.next;
        }

        return h.data;
    }

    // leptonica: listRemoveFromTail (list.c)
    /// <summary>Removes the tail cell from the list.</summary>
    /// <param name="head">list head must NOT be null; may be changed</param>
    /// <param name="tail">list tail may be null; always updated</param>
    /// <returns>data on the cell</returns>
    /// <remarks>
    /// (1) We include head so that it can be set to null if
    ///     the only element in the list is removed.
    /// (2) The function is relying on the fact that if tail is
    ///     not null, then it is a valid cell.  You can use
    ///     this function with tail == null for an existing list, in
    ///     which case the tail is found and updated, and the
    ///     removed element is returned.
    /// </remarks>
    public static T RemoveFromTail(ref L_Dllist<T>? head, ref L_Dllist<T>? tail)
    {
        L_Dllist<T>? h = head;
        if (h == null)
            throw Lept.Arg("listRemoveFromTail", "head not defined");
        L_Dllist<T> t = tail ?? FindTail(h);

        if (h.next == null) { /* only one */
            head = null;
            tail = null;
        } else {
            t.prev!.next = null;
            tail = t.prev;
        }

        return t.data;
    }

    /*---------------------------------------------------------------------*
     *                         Other list operations                       *
     *---------------------------------------------------------------------*/
    // leptonica: listFindElement (list.c)
    /// <summary>Finds the cell that holds the given data.</summary>
    /// <param name="head">list head</param>
    /// <param name="data">address, to be searched for</param>
    /// <returns>the containing cell, or null if not found</returns>
    /// <remarks>
    /// (1) This returns the cell, which is still embedded in the list.
    /// </remarks>
    public static L_Dllist<T>? FindElement(L_Dllist<T> head, T data)
    {
        for (L_Dllist<T>? cell = head; cell != null; cell = cell.next) {
            if (ReferenceEquals(cell.data, data))
                return cell;
        }

        return null;
    }

    // leptonica: listFindTail (list.c)
    /// <summary>Finds the last cell of the list.</summary>
    /// <param name="head">list head</param>
    /// <returns>tail</returns>
    public static L_Dllist<T> FindTail(L_Dllist<T> head)
    {
        for (L_Dllist<T>? cell = head; cell != null; cell = cell.next) {
            if (cell.next == null)
                return cell;
        }

        throw Lept.Fail("listFindTail", "tail not found !!");
    }

    // leptonica: listGetCount (list.c)
    /// <summary>Counts the cells in the list.</summary>
    /// <param name="head">of list</param>
    /// <returns>number of elements</returns>
    public static int GetCount(L_Dllist<T> head)
    {
        int count = 0;
        for (L_Dllist<T>? elem = head; elem != null; elem = elem.next)
            count++;

        return count;
    }

    // leptonica: listReverse (list.c)
    /// <summary>Reverses the list in-place.</summary>
    /// <param name="head">list head; may be changed</param>
    public static void Reverse(ref L_Dllist<T>? head)
    {
        if (head == null)
            throw Lept.Arg("listReverse", "head not defined");

        L_Dllist<T>? h = head;
        L_Dllist<T>? rhead = null;
        while (h != null) {
            T obj = RemoveFromHead(ref h);
            AddToHead(ref rhead, obj);
        }

        head = rhead;
    }

    // leptonica: listJoin (list.c)
    /// <summary>Concatenates the second list onto the first.</summary>
    /// <param name="head1">head of first list; may be changed</param>
    /// <param name="head2">head of second list; to be nulled</param>
    /// <remarks>
    /// (1) The concatenated list is returned with head1 as the new head.
    /// (2) Either list can be null.
    /// </remarks>
    public static void Join(ref L_Dllist<T>? head1, ref L_Dllist<T>? head2)
    {
            /* If no list2, just return list1 unchanged */
        L_Dllist<T>? h2 = head2;
        if (h2 == null)
            return;

            /* If no list1, just return list2 */
        L_Dllist<T>? h1 = head1;
        if (h1 == null) {
            head1 = h2;
            head2 = null;
            return;
        }

            /* General case for concatenation into list 1 */
        L_Dllist<T>? tail1 = FindTail(h1);
        while (h2 != null) {
            T obj = RemoveFromHead(ref h2);
            AddToTail(ref h1, ref tail1, obj);
        }
        head2 = null;
    }
}
