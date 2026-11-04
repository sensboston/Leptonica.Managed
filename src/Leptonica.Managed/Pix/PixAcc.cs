using System;

namespace Leptonica;

/*
 *  pixacc.c: Pixel accumulator.
 *
 *      Pixacc creation
 *          Pixacc.Create (pixaccCreate), CreateFromPix (pixaccCreateFromPix)
 *
 *      Pixacc finalization
 *          Pixacc.Final (pixaccFinal)
 *
 *      Pixacc accessors
 *          Pixacc.GetPix (pixaccGetPix), GetOffset (pixaccGetOffset)
 *
 *      Pixacc accumulators
 *          Pixacc.Add (pixaccAdd), Subtract (pixaccSubtract), MultConst (pixaccMultConst),
 *          MultConstAccumulate (pixaccMultConstAccumulate)
 *
 *  Not ported: pixaccDestroy (memory management only).
 *
 *  This is a simple interface for some of the pixel arithmetic operations
 *  in pixarith.c.  These are easy to code up, but not as fast as
 *  hand-coded functions that do arithmetic on corresponding pixels.
 *
 *  Suppose you want to make a linear combination of pix1 and pix2:
 *     pixd = 0.4 * pix1 + 0.6 * pix2
 *  where pix1 and pix2 are the same size and have depth 'd'.  Then:
 *     Pixacc pacc = Pixacc.CreateFromPix(pix1, 0);  // first; addition only
 *     pacc.MultConst(0.4f);
 *     pacc.MultConstAccumulate(pix2, 0.6f);  // Add in 0.6 of the second
 *     Pix pixd = pacc.Final(d);  // Get the result
 */

/// <summary>Pixel accumulator (struct PixAccumulator).</summary>
public sealed partial class Pixacc
{
    private readonly int w;          /* width of the internal pix  */
    private readonly int h;          /* height of the internal pix */
    private readonly int offset;     /* used to allow negative values */
    private readonly Pix pix;        /* the 32 bpp internal pix    */

    private Pixacc(int w, int h, int offset, Pix pix)
    {
        this.w = w;
        this.h = h;
        this.offset = offset;
        this.pix = pix;
    }

    /*---------------------------------------------------------------------*
     *                          Pixacc creation                            *
     *---------------------------------------------------------------------*/
    // leptonica: pixaccCreate (pixacc.c)
    /// <summary>Creates a pixel accumulator.</summary>
    /// <param name="w">of 32 bpp internal Pix</param>
    /// <param name="h">of 32 bpp internal Pix</param>
    /// <param name="negflag">0 if only positive numbers are involved;
    ///     1 if there will be negative numbers</param>
    /// <remarks>
    /// (1) Use %negflag = 1 for safety if any negative numbers are going
    ///     to be used in the chain of operations.  Negative numbers
    ///     arise, e.g., by subtracting a pix, or by adding a pix
    ///     that has been pre-multiplied by a negative number.
    /// (2) Initializes the internal 32 bpp pix, similarly to the
    ///     initialization in pixInitAccumulate().
    /// </remarks>
    public static Pixacc Create(int w, int h, int negflag)
    {
        int offset = 0;
        Pix pix;
        if (negflag != 0) {
            offset = 0x40000000;
            pix = Pix.CreateRawNoInit(w, h, 32);
            pix.Data.AsSpan(0, pix.Wpl * h).Fill((uint)offset);
        } else {
            pix = Pix.Create(w, h, 32);
        }

        return new Pixacc(w, h, offset, pix);
    }

    // leptonica: pixaccCreateFromPix (pixacc.c)
    /// <summary>Creates a pixel accumulator, with the pix added.</summary>
    /// <param name="pix">to be added</param>
    /// <param name="negflag">0 if only positive numbers are involved;
    ///     1 if there will be negative numbers</param>
    /// <remarks>(1) See pixaccCreate()</remarks>
    public static Pixacc CreateFromPix(Pix pix, int negflag)
    {
        pix.GetDimensions(out int w, out int h, out _);
        Pixacc pixacc = Create(w, h, negflag);
        pixacc.Add(pix);
        return pixacc;
    }

    /*---------------------------------------------------------------------*
     *                          Pixacc finalization                        *
     *---------------------------------------------------------------------*/
    // leptonica: pixaccFinal (pixacc.c)
    /// <summary>Returns the accumulated result as a pix.</summary>
    /// <param name="outdepth">8, 16 or 32 bpp</param>
    /// <returns>pixd 8, 16 or 32 bpp</returns>
    public Pix Final(int outdepth)
    {
        return GetPix().FinalAccumulate((uint)GetOffset(), outdepth);
    }

    /*---------------------------------------------------------------------*
     *                          Pixacc accessors                           *
     *---------------------------------------------------------------------*/
    // leptonica: pixaccGetPix (pixacc.c)
    /// <summary>The internal 32 bpp pix.</summary>
    public Pix GetPix()
    {
        return pix;
    }

    // leptonica: pixaccGetOffset (pixacc.c)
    /// <summary>The offset that allows negative values in the internal pix.</summary>
    public int GetOffset()
    {
        return offset;
    }

    /*---------------------------------------------------------------------*
     *                        Pixacc accumulators                          *
     *---------------------------------------------------------------------*/
    // leptonica: pixaccAdd (pixacc.c)
    /// <summary>Adds a pix to the accumulator.</summary>
    /// <param name="pix">to be added</param>
    public void Add(Pix pix)
    {
        GetPix().Accumulate(pix, L.L_ARITH_ADD);
    }

    // leptonica: pixaccSubtract (pixacc.c)
    /// <summary>Subtracts a pix from the accumulator.</summary>
    /// <param name="pix">to be subtracted</param>
    public void Subtract(Pix pix)
    {
        GetPix().Accumulate(pix, L.L_ARITH_SUBTRACT);
    }

    // leptonica: pixaccMultConst (pixacc.c)
    /// <summary>Multiplies the accumulator by a constant.</summary>
    public void MultConst(float factor)
    {
        GetPix().MultConstAccumulate(factor, (uint)GetOffset());
    }

    // leptonica: pixaccMultConstAccumulate (pixacc.c)
    /// <summary>Adds a pix, multiplied by a constant, to the accumulator.</summary>
    /// <remarks>
    /// (1) This creates a temp pix that is %pix multiplied by the
    ///     constant %factor.  It then adds that into %pixacc.
    /// </remarks>
    public void MultConstAccumulate(Pix pix, float factor)
    {
        if (factor == 0.0) return;

        pix.GetDimensions(out int w, out int h, out int d);
        int negflag = (factor > 0.0) ? 0 : 1;
        Pixacc pacct = Create(w, h, negflag);
        pacct.Add(pix);
        pacct.MultConst(factor);
        Pix pixt = pacct.Final(d);
        Add(pixt);
    }
}
