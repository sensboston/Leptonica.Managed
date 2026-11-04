using System;

namespace Leptonica;

/*
 *  stack.c: Generic stack of object references (L_Stack).
 *
 *  Not ported: lstackDestroy() (the GC owns the objects) and lstackPrint()
 *  (prints raw pointer values).
 *
 *  Differences from C:
 *    - The stack holds object references instead of void pointers.
 *    - A stack can have an auxiliary stack, used as a reservoir of
 *      items for reuse (see conncomp.c).
 */

/// <summary>Stack of object references (struct L_Stack).</summary>
public sealed class L_Stack
{
    /* Bounds on initial array size */
    private const uint MaxPtrArraySize = 100000;
    private const int InitialPtrArraySize = 20;     /* n'importe quoi */

    private object[] array;
    private int n;

    /// <summary>Auxiliary stack, for recycling items (stack->auxstack).</summary>
    public L_Stack? AuxStack { get; set; }

    private L_Stack(int nalloc)
    {
        array = new object[nalloc];
        n = 0;
    }

    /*---------------------------------------------------------------------*
     *                          Create/Destroy                             *
     *---------------------------------------------------------------------*/
    // leptonica: lstackCreate (stack.c)
    /// <summary>Creates a stack.</summary>
    /// <param name="n">initial ptr array size; use 0 for default</param>
    public static L_Stack Create(int n)
    {
        if (n <= 0 || n > (int)MaxPtrArraySize)
            n = InitialPtrArraySize;

        return new L_Stack(n);
    }

    /*---------------------------------------------------------------------*
     *                               Accessors                             *
     *---------------------------------------------------------------------*/
    // leptonica: lstackAdd (stack.c)
    /// <summary>Pushes an item on the stack.</summary>
    /// <param name="item">to be added to the lstack</param>
    public void Add(object item)
    {
        if (item == null)
            throw Lept.Arg("lstackAdd", "item not defined");

            /* Do we need to extend the array? */
        if (n >= array.Length)
            ExtendArray();

            /* Store the new pointer */
        array[n] = item;
        n++;
    }

    // leptonica: lstackRemove (stack.c)
    /// <summary>Pops the item at the top of the stack.</summary>
    /// <returns>item popped from the top of the lstack, or null if the lstack is empty</returns>
    public object? Remove()
    {
        if (n == 0)
            return null;

        n--;
        return array[n];
    }

    // leptonica: lstackExtendArray (stack.c)
    private void ExtendArray()
    {
        Array.Resize(ref array, 2 * array.Length);
    }

    // leptonica: lstackGetCount (stack.c)
    /// <summary>Number of items on the stack.</summary>
    public int Count => n;

    // leptonica: lstackPrint (stack.c)
    /// <summary>Writes the stack summary to a stream, for debugging.</summary>
    public void Print(System.IO.Stream fp)
    {
        if (fp == null)
            throw Lept.Arg("lstackPrint", "stream not defined");

        CStreamText.Fprintf(fp, "\n Stack: nalloc = %d, n = %d\n", array.Length, n);
        for (int i = 0; i < n; i++)
            CStreamText.Fprintf(fp, "array[%d] = %s\n", i, array[i]?.ToString() ?? "null");
    }
}
