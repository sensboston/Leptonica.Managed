using System;

namespace Leptonica;

/*
 *  morphseq.c: Morphological sequence operations.
 *
 *     Run a sequence of binary rasterop morphological operations
 *         Pix.MorphSequence
 *
 *     Run a sequence of binary composite rasterop morphological operations
 *         Pix.MorphCompSequence
 *
 *     Run a sequence of binary dwa morphological operations
 *         Pix.MorphSequenceDwa
 *
 *     Run a sequence of binary composite dwa morphological operations
 *         Pix.MorphCompSequenceDwa
 *
 *     Parser verifier for binary morphological operations
 *         Pix.MorphSequenceVerify
 *
 *     Run a sequence of grayscale morphological operations
 *         Pix.GrayMorphSequence
 *
 *     Run a sequence of color morphological operations
 *         Pix.ColorMorphSequence
 *
 *  Error returns of the C code (NULL / FALSE) are thrown as exceptions, except in MorphSequenceVerify, which returns
 *  false for an invalid sequence, as in C.  The cleanup of intermediate Sarray
 *  and Pix before an error return is not needed.
 *
 *  The messages that the verifiers print with lept_stderr() go through
 *  Utils.LeptStderr().  The sscanf() calls use CFormat.Sscanf(); like in C,
 *  the variables that are not converted keep their previous values.
 */

/// <summary>Constants from morph.h and imageio.h used by morphseq.c.</summary>
public static partial class L
{
    /* Morphological operation types (morph.h); L_MORPH_DILATE and L_MORPH_ERODE
     * are defined in morph.c. */
    /// <summary>Morphological operation type: opening.</summary>
    public const int L_MORPH_OPEN = 3;
    /// <summary>Morphological operation type: closing.</summary>
    public const int L_MORPH_CLOSE = 4;

    /* Morphological tophat flags (morph.h) */
    /// <summary>Tophat that extracts small light features (the image minus its opening).</summary>
    public const int L_TOPHAT_WHITE = 0;
    /// <summary>Tophat that extracts small dark features (the closing of the image minus the image).</summary>
    public const int L_TOPHAT_BLACK = 1;

    /* Pdf encoding types (imageio.h) */
    /// <summary>PDF encoding type: Flate (zlib) compression.</summary>
    public const int L_FLATE_ENCODE = 3;
}

public sealed partial class Pix
{
    /// <summary>
    /// sscanf(str, "%d.%d", &amp;w, &amp;h): w and h are only assigned for the
    /// conversions that succeed.  Returns the result of sscanf.
    /// </summary>
    private static int Sscanf2(string str, ref int w, ref int h)
    {
        int n = CFormat.Sscanf(str, "%d.%d", out object?[] v);
        if (n >= 1)
            w = (int)v[0]!;
        if (n >= 2)
            h = (int)v[1]!;
        return n;
    }

    /// <summary>sscanf(str, "%d", &amp;val): val is only assigned if the conversion succeeds.</summary>
    private static int Sscanf1(string str, ref int val)
    {
        int n = CFormat.Sscanf(str, "%d", out object?[] v);
        if (n >= 1)
            val = (int)v[0]!;
        return n;
    }

    /// <summary>op[i] of a C string: '\0' at the terminator.</summary>
    private static char OpChar(string op, int i) => i < op.Length ? op[i] : '\0';

    /*-------------------------------------------------------------------------*
     *         Run a sequence of binary rasterop morphological operations      *
     *-------------------------------------------------------------------------*/
    // leptonica: pixMorphSequence (morphseq.c)
    /// <summary>Runs a sequence of binary rasterop morphological operations.</summary>
    /// <param name="sequence">string specifying sequence of operations</param>
    /// <param name="dispsep">controls debug display results in the sequence:
    ///     0: no output;
    ///     &gt; 0: gives horizontal separation in pixels between successive displays;
    ///     &lt; 0: pdf output; abs(dispsep) is used for naming</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This does rasterop morphology on binary images.
    /// (2) This runs a pipeline of operations; no branching is allowed.
    /// (3) This only uses brick Sels, which are created on the fly.
    ///     In the future this will be generalized to extract Sels from
    ///     a Sela by name.
    /// (4) A new image is always produced; the input image is not changed.
    /// (5) This contains an interpreter, allowing sequences to be
    ///     generated and run.
    /// (6) The format of the sequence string is defined below.
    /// (7) In addition to morphological operations, rank order reduction
    ///     and replicated expansion allow operations to take place
    ///     downscaled by a power of 2.
    /// (8) Intermediate results can optionally be displayed.
    /// (9) Thanks to Dar-Shyang Lee, who had the idea for this and
    ///     built the first implementation.
    /// (10) The sequence string is formatted as follows:
    ///      ~ An arbitrary number of operations,  each separated
    ///        by a '+' character.  White space is ignored.
    ///      ~ Each operation begins with a case-independent character
    ///        specifying the operation:
    ///           d or D  (dilation)
    ///           e or E  (erosion)
    ///           o or O  (opening)
    ///           c or C  (closing)
    ///           r or R  (rank binary reduction)
    ///           x or X  (replicative binary expansion)
    ///           b or B  (add a border of 0 pixels of this size)
    ///      ~ The args to the morphological operations are bricks of hits,
    ///        and are formatted as a.b, where a and b are horizontal and
    ///        vertical dimensions, rsp.
    ///      ~ The args to the reduction are a sequence of up to 4 integers,
    ///        each from 1 to 4.
    ///      ~ The arg to the expansion is a power of two, in the set
    ///        {2, 4, 8, 16}.
    /// (11) An example valid sequence is:
    ///          "b32 + o1.3 + C3.1 + r23 + e2.2 + D3.2 + X4"
    ///      In this example, the following operation sequence is carried out:
    ///        * b32: Add a 32 pixel border around the input image
    ///        * o1.3: Opening with vert sel of length 3 (e.g., 1 x 3)
    ///        * C3.1: Closing with horiz sel of length 3  (e.g., 3 x 1)
    ///        * r23: Two successive 2x2 reductions with rank 2 in the first
    ///               and rank 3 in the second.  The result is a 4x reduced pix.
    ///        * e2.2: Erosion with a 2x2 sel (origin will be at x,y: 0,0)
    ///        * d3.2: Dilation with a 3x2 sel (origin will be at x,y: 1,0)
    ///        * X4: 4x replicative expansion, back to original resolution
    /// (12) The safe closing is used.  However, if you implement a
    ///      closing as separable dilations followed by separable erosions,
    ///      it will not be safe.  For that situation, you need to add
    ///      a sufficiently large border as the first operation in
    ///      the sequence.  This will be removed automatically at the
    ///      end.  There are two cautions:
    ///         ~ When computing what is sufficient, remember that if
    ///           reductions are carried out, the border is also reduced.
    ///         ~ The border is removed at the end, so if a border is
    ///           added at the beginning, the result must be at the
    ///           same resolution as the input!
    /// </remarks>
    public Pix MorphSequence(string sequence, int dispsep)
    {
        const string func = "pixMorphSequence";
        Pix pixs = this;
        string rawop, op;
        string fname;
        int nops, i, j, nred, fact = 0, w = 0, h = 0, x, border, pdfout;
        int[] level = new int[4];
        Pix? pix1, pix2;
        Pixa? pixa;
        Sarray sa;

            /* Split sequence into individual operations */
        sa = Sarray.Create(0);
        sa.SplitString(sequence, "+");
        nops = sa.Count;
        pdfout = (dispsep < 0) ? 1 : 0;
        if (!MorphSequenceVerify(sa))
            throw Lept.Arg(func, "sequence not valid");

            /* Parse and operate */
        pixa = null;
        if (pdfout != 0) {
            pixa = Pixa.Create(0);
            pixa.AddPix(pixs, L.L_CLONE);
        }
        border = 0;
        pix1 = Copy(null, pixs);
        pix2 = null;
        x = 0;
        for (i = 0; i < nops; i++) {
            rawop = sa.GetString(i, L.L_NOCOPY);
            op = Utils.StringRemoveChars(rawop, " \n\t");
            switch (OpChar(op, 0))
            {
            case 'd':
            case 'D':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = DilateBrick(null, pix1!, w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'e':
            case 'E':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = ErodeBrick(null, pix1!, w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'o':
            case 'O':
                Sscanf2(op.Substring(1), ref w, ref h);
                OpenBrick(pix1, pix1!, w, h);
                break;
            case 'c':
            case 'C':
                Sscanf2(op.Substring(1), ref w, ref h);
                CloseSafeBrick(pix1, pix1!, w, h);
                break;
            case 'r':
            case 'R':
                nred = op.Length - 1;
                for (j = 0; j < nred; j++)
                    level[j] = op[j + 1] - '0';
                for (j = nred; j < 4; j++)
                    level[j] = 0;
                pix2 = pix1!.ReduceRankBinaryCascade(level[0], level[1],
                                                     level[2], level[3]);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'x':
            case 'X':
                Sscanf1(op.Substring(1), ref fact);
                pix2 = pix1!.ExpandReplicate(fact);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'b':
            case 'B':
                Sscanf1(op.Substring(1), ref border);
                pix2 = pix1!.AddBorder(border, 0);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            default:
                /* All invalid ops are caught in the first pass */
                break;
            }

                /* Debug output */
            if (dispsep > 0) {
                pix1!.Display(x, 0);
                x += dispsep;
            }
            if (pdfout != 0)
                pixa!.AddPix(pix1!, L.L_COPY);
        }
        if (border > 0) {
            pix2 = pix1!.RemoveBorder(border);
            SwapAndDestroy(ref pix1, ref pix2);
        }

        if (pdfout != 0) {
            fname = $"/tmp/lept/seq_output_{Math.Abs(dispsep)}.pdf";
            pixa!.ConvertToPdf(0, 1.0f, L.L_FLATE_ENCODE, 0, fname, fname);
        }

        return pix1!;
    }


    /*-------------------------------------------------------------------------*
     *   Run a sequence of binary composite rasterop morphological operations  *
     *-------------------------------------------------------------------------*/
    // leptonica: pixMorphCompSequence (morphseq.c)
    /// <summary>Runs a sequence of binary composite rasterop morphological operations.</summary>
    /// <param name="sequence">string specifying sequence of operations</param>
    /// <param name="dispsep">controls debug display of results in the sequence:
    ///     0: no output;
    ///     &gt; 0: gives horizontal separation in pixels between successive displays;
    ///     &lt; 0: pdf output; abs(dispsep) is used for naming</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This does rasterop morphology on binary images, using composite
    ///     operations for extra speed on large Sels.
    /// (2) Safe closing is used atomically.  However, if you implement a
    ///     closing as a sequence with a dilation followed by an
    ///     erosion, it will not be safe, and to ensure that you have
    ///     no boundary effects you must add a border in advance and
    ///     remove it at the end.
    /// (3) For other usage details, see the notes for MorphSequence().
    /// (4) The sequence string is formatted as follows:
    ///      ~ An arbitrary number of operations,  each separated
    ///        by a '+' character.  White space is ignored.
    ///      ~ Each operation begins with a case-independent character
    ///        specifying the operation:
    ///           d or D  (dilation)
    ///           e or E  (erosion)
    ///           o or O  (opening)
    ///           c or C  (closing)
    ///           r or R  (rank binary reduction)
    ///           x or X  (replicative binary expansion)
    ///           b or B  (add a border of 0 pixels of this size)
    ///      ~ The args to the morphological operations are bricks of hits,
    ///        and are formatted as a.b, where a and b are horizontal and
    ///        vertical dimensions, rsp.
    ///      ~ The args to the reduction are a sequence of up to 4 integers,
    ///        each from 1 to 4.
    ///      ~ The arg to the expansion is a power of two, in the set
    ///        {2, 4, 8, 16}.
    /// </remarks>
    public Pix MorphCompSequence(string sequence, int dispsep)
    {
        const string func = "pixMorphCompSequence";
        Pix pixs = this;
        string rawop, op;
        string fname;
        int nops, i, j, nred, fact = 0, w = 0, h = 0, x, border, pdfout;
        int[] level = new int[4];
        Pix? pix1, pix2;
        Pixa? pixa;
        Sarray sa;

            /* Split sequence into individual operations */
        sa = Sarray.Create(0);
        sa.SplitString(sequence, "+");
        nops = sa.Count;
        pdfout = (dispsep < 0) ? 1 : 0;

        if (!MorphSequenceVerify(sa))
            throw Lept.Arg(func, "sequence not valid");

            /* Parse and operate */
        pixa = null;
        if (pdfout != 0) {
            pixa = Pixa.Create(0);
            pixa.AddPix(pixs, L.L_CLONE);
        }
        border = 0;
        pix1 = Copy(null, pixs);
        pix2 = null;
        x = 0;
        for (i = 0; i < nops; i++) {
            rawop = sa.GetString(i, L.L_NOCOPY);
            op = Utils.StringRemoveChars(rawop, " \n\t");
            switch (OpChar(op, 0))
            {
            case 'd':
            case 'D':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = DilateCompBrick(null, pix1!, w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'e':
            case 'E':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = ErodeCompBrick(null, pix1!, w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'o':
            case 'O':
                Sscanf2(op.Substring(1), ref w, ref h);
                OpenCompBrick(pix1, pix1!, w, h);
                break;
            case 'c':
            case 'C':
                Sscanf2(op.Substring(1), ref w, ref h);
                CloseSafeCompBrick(pix1, pix1!, w, h);
                break;
            case 'r':
            case 'R':
                nred = op.Length - 1;
                for (j = 0; j < nred; j++)
                    level[j] = op[j + 1] - '0';
                for (j = nred; j < 4; j++)
                    level[j] = 0;
                pix2 = pix1!.ReduceRankBinaryCascade(level[0], level[1],
                                                     level[2], level[3]);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'x':
            case 'X':
                Sscanf1(op.Substring(1), ref fact);
                pix2 = pix1!.ExpandReplicate(fact);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'b':
            case 'B':
                Sscanf1(op.Substring(1), ref border);
                pix2 = pix1!.AddBorder(border, 0);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            default:
                /* All invalid ops are caught in the first pass */
                break;
            }

                /* Debug output */
            if (dispsep > 0) {
                pix1!.Display(x, 0);
                x += dispsep;
            }
            if (pdfout != 0)
                pixa!.AddPix(pix1!, L.L_COPY);
        }
        if (border > 0) {
            pix2 = pix1!.RemoveBorder(border);
            SwapAndDestroy(ref pix1, ref pix2);
        }

        if (pdfout != 0) {
            fname = $"/tmp/lept/seq_output_{Math.Abs(dispsep)}.pdf";
            pixa!.ConvertToPdf(0, 1.0f, L.L_FLATE_ENCODE, 0, fname, fname);
        }

        return pix1!;
    }


    /*-------------------------------------------------------------------------*
     *           Run a sequence of binary dwa morphological operations         *
     *-------------------------------------------------------------------------*/
    // leptonica: pixMorphSequenceDwa (morphseq.c)
    /// <summary>Runs a sequence of binary dwa morphological operations.</summary>
    /// <param name="sequence">string specifying sequence of operations</param>
    /// <param name="dispsep">controls debug display of results in the sequence:
    ///     0: no output;
    ///     &gt; 0: gives horizontal separation in pixels between successive displays;
    ///     &lt; 0: pdf output; abs(dispsep) is used for naming</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This does dwa morphology on binary images.
    /// (2) This runs a pipeline of operations; no branching is allowed.
    /// (3) This only uses brick Sels that have been pre-compiled with
    ///     dwa code.
    /// (4) A new image is always produced; the input image is not changed.
    /// (5) This contains an interpreter, allowing sequences to be
    ///     generated and run.
    /// (6) See MorphSequence() for further information about usage.
    /// </remarks>
    public Pix MorphSequenceDwa(string sequence, int dispsep)
    {
        const string func = "pixMorphSequenceDwa";
        Pix pixs = this;
        string rawop, op;
        string fname;
        int nops, i, j, nred, fact = 0, w = 0, h = 0, x, border, pdfout;
        int[] level = new int[4];
        Pix? pix1, pix2;
        Pixa? pixa;
        Sarray sa;

            /* Split sequence into individual operations */
        sa = Sarray.Create(0);
        sa.SplitString(sequence, "+");
        nops = sa.Count;
        pdfout = (dispsep < 0) ? 1 : 0;

        if (!MorphSequenceVerify(sa))
            throw Lept.Arg(func, "sequence not valid");

            /* Parse and operate */
        pixa = null;
        if (pdfout != 0) {
            pixa = Pixa.Create(0);
            pixa.AddPix(pixs, L.L_CLONE);
        }
        border = 0;
        pix1 = Copy(null, pixs);
        pix2 = null;
        x = 0;
        for (i = 0; i < nops; i++) {
            rawop = sa.GetString(i, L.L_NOCOPY);
            op = Utils.StringRemoveChars(rawop, " \n\t");
            switch (OpChar(op, 0))
            {
            case 'd':
            case 'D':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = DilateBrickDwa(null, pix1!, w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'e':
            case 'E':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = ErodeBrickDwa(null, pix1!, w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'o':
            case 'O':
                Sscanf2(op.Substring(1), ref w, ref h);
                OpenBrickDwa(pix1, pix1!, w, h);
                break;
            case 'c':
            case 'C':
                Sscanf2(op.Substring(1), ref w, ref h);
                CloseBrickDwa(pix1, pix1!, w, h);
                break;
            case 'r':
            case 'R':
                nred = op.Length - 1;
                for (j = 0; j < nred; j++)
                    level[j] = op[j + 1] - '0';
                for (j = nred; j < 4; j++)
                    level[j] = 0;
                pix2 = pix1!.ReduceRankBinaryCascade(level[0], level[1],
                                                     level[2], level[3]);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'x':
            case 'X':
                Sscanf1(op.Substring(1), ref fact);
                pix2 = pix1!.ExpandReplicate(fact);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'b':
            case 'B':
                Sscanf1(op.Substring(1), ref border);
                pix2 = pix1!.AddBorder(border, 0);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            default:
                /* All invalid ops are caught in the first pass */
                break;
            }

                /* Debug output */
            if (dispsep > 0) {
                pix1!.Display(x, 0);
                x += dispsep;
            }
            if (pdfout != 0)
                pixa!.AddPix(pix1!, L.L_COPY);
        }
        if (border > 0) {
            pix2 = pix1!.RemoveBorder(border);
            SwapAndDestroy(ref pix1, ref pix2);
        }

        if (pdfout != 0) {
            fname = $"/tmp/lept/seq_output_{Math.Abs(dispsep)}.pdf";
            pixa!.ConvertToPdf(0, 1.0f, L.L_FLATE_ENCODE, 0, fname, fname);
        }

        return pix1!;
    }


    /*-------------------------------------------------------------------------*
     *      Run a sequence of binary composite dwa morphological operations    *
     *-------------------------------------------------------------------------*/
    // leptonica: pixMorphCompSequenceDwa (morphseq.c)
    /// <summary>Runs a sequence of binary composite dwa morphological operations.</summary>
    /// <param name="sequence">string specifying sequence of operations</param>
    /// <param name="dispsep">controls debug display of results in the sequence:
    ///     0: no output;
    ///     &gt; 0: gives horizontal separation in pixels between successive displays;
    ///     &lt; 0: pdf output; abs(dispsep) is used for naming</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This does dwa morphology on binary images, using brick Sels.
    /// (2) This runs a pipeline of operations; no branching is allowed.
    /// (3) It implements all brick Sels that have dimensions up to 63
    ///     on each side, using a composite (linear + comb) when useful.
    /// (4) A new image is always produced; the input image is not changed.
    /// (5) This contains an interpreter, allowing sequences to be
    ///     generated and run.
    /// (6) See MorphSequence() for further information about usage.
    /// </remarks>
    public Pix MorphCompSequenceDwa(string sequence, int dispsep)
    {
        const string func = "pixMorphCompSequenceDwa";
        Pix pixs = this;
        string rawop, op;
        string fname;
        int nops, i, j, nred, fact = 0, w = 0, h = 0, x, border, pdfout;
        int[] level = new int[4];
        Pix? pix1, pix2;
        Pixa? pixa;
        Sarray sa;

            /* Split sequence into individual operations */
        sa = Sarray.Create(0);
        sa.SplitString(sequence, "+");
        nops = sa.Count;
        pdfout = (dispsep < 0) ? 1 : 0;

        if (!MorphSequenceVerify(sa))
            throw Lept.Arg(func, "sequence not valid");

            /* Parse and operate */
        pixa = null;
        if (pdfout != 0) {
            pixa = Pixa.Create(0);
            pixa.AddPix(pixs, L.L_CLONE);
        }
        border = 0;
        pix1 = Copy(null, pixs);
        pix2 = null;
        x = 0;
        for (i = 0; i < nops; i++) {
            rawop = sa.GetString(i, L.L_NOCOPY);
            op = Utils.StringRemoveChars(rawop, " \n\t");
            switch (OpChar(op, 0))
            {
            case 'd':
            case 'D':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = DilateCompBrickDwa(null, pix1!, w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'e':
            case 'E':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = ErodeCompBrickDwa(null, pix1!, w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'o':
            case 'O':
                Sscanf2(op.Substring(1), ref w, ref h);
                OpenCompBrickDwa(pix1, pix1!, w, h);
                break;
            case 'c':
            case 'C':
                Sscanf2(op.Substring(1), ref w, ref h);
                CloseCompBrickDwa(pix1, pix1!, w, h);
                break;
            case 'r':
            case 'R':
                nred = op.Length - 1;
                for (j = 0; j < nred; j++)
                    level[j] = op[j + 1] - '0';
                for (j = nred; j < 4; j++)
                    level[j] = 0;
                pix2 = pix1!.ReduceRankBinaryCascade(level[0], level[1],
                                                     level[2], level[3]);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'x':
            case 'X':
                Sscanf1(op.Substring(1), ref fact);
                pix2 = pix1!.ExpandReplicate(fact);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'b':
            case 'B':
                Sscanf1(op.Substring(1), ref border);
                pix2 = pix1!.AddBorder(border, 0);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            default:
                /* All invalid ops are caught in the first pass */
                break;
            }

                /* Debug output */
            if (dispsep > 0) {
                pix1!.Display(x, 0);
                x += dispsep;
            }
            if (pdfout != 0)
                pixa!.AddPix(pix1!, L.L_COPY);
        }
        if (border > 0) {
            pix2 = pix1!.RemoveBorder(border);
            SwapAndDestroy(ref pix1, ref pix2);
        }

        if (pdfout != 0) {
            fname = $"/tmp/lept/seq_output_{Math.Abs(dispsep)}.pdf";
            pixa!.ConvertToPdf(0, 1.0f, L.L_FLATE_ENCODE, 0, fname, fname);
        }

        return pix1!;
    }


    /*-------------------------------------------------------------------------*
     *            Parser verifier for binary morphological operations          *
     *-------------------------------------------------------------------------*/
    // leptonica: morphSequenceVerify (morphseq.c)
    /// <summary>Verifies a binary morphological operation sequence.</summary>
    /// <param name="sa">string array of operation sequence</param>
    /// <returns>true if valid; false otherwise</returns>
    /// <remarks>
    /// (1) This does verification of valid binary morphological
    ///     operation sequences.
    /// (2) See MorphSequence() for notes on valid operations
    ///     in the sequence.
    /// </remarks>
    public static bool MorphSequenceVerify(Sarray sa)
    {
        string rawop;
        string? op = null;
        int nops, i, j, nred, fact = 0, w = 0, h = 0, netred, border;
        bool valid;
        int[] level = new int[4];
        int[] intlogbase2 = { 1, 2, 3, 0, 4 };  /* of arg/4 */

        nops = sa.Count;
        valid = true;
        netred = 0;
        border = 0;
        for (i = 0; i < nops; i++) {
            rawop = sa.GetString(i, L.L_NOCOPY);
            op = Utils.StringRemoveChars(rawop, " \n\t");
            switch (OpChar(op, 0))
            {
            case 'd':
            case 'D':
            case 'e':
            case 'E':
            case 'o':
            case 'O':
            case 'c':
            case 'C':
                if (Sscanf2(op.Substring(1), ref w, ref h) != 2) {
                    Utils.LeptStderr("*** op: %s invalid\n", op);
                    valid = false;
                    break;
                }
                if (w <= 0 || h <= 0) {
                    Utils.LeptStderr("*** op: %s; w = %d, h = %d; must both be > 0\n",
                                     op, w, h);
                    valid = false;
                    break;
                }
                break;
            case 'r':
            case 'R':
                nred = op.Length - 1;
                netred += nred;
                if (nred < 1 || nred > 4) {
                    Utils.LeptStderr(
                            "*** op = %s; num reduct = %d; must be in {1,2,3,4}\n",
                            op, nred);
                    valid = false;
                    break;
                }
                for (j = 0; j < nred; j++) {
                    level[j] = op[j + 1] - '0';
                    if (level[j] < 1 || level[j] > 4) {
                        Utils.LeptStderr("*** op = %s; level[%d] = %d is invalid\n",
                                         op, j, level[j]);
                        valid = false;
                        break;
                    }
                }
                if (!valid)
                    break;
                for (j = 0; j < nred; j++) {
                    level[j] = op[j + 1] - '0';
                }
                break;
            case 'x':
            case 'X':
                if (Sscanf1(op.Substring(1), ref fact) != 1) {
                    Utils.LeptStderr("*** op: %s; fact invalid\n", op);
                    valid = false;
                    break;
                }
                if (fact != 2 && fact != 4 && fact != 8 && fact != 16) {
                    Utils.LeptStderr("*** op = %s; invalid fact = %d\n", op, fact);
                    valid = false;
                    break;
                }
                netred -= intlogbase2[fact / 4];
                break;
            case 'b':
            case 'B':
                if (Sscanf1(op.Substring(1), ref fact) != 1) {
                    Utils.LeptStderr("*** op: %s; fact invalid\n", op);
                    valid = false;
                    break;
                }
                if (i > 0) {
                    Utils.LeptStderr("*** op = %s; must be first op\n", op);
                    valid = false;
                    break;
                }
                if (fact < 1) {
                    Utils.LeptStderr("*** op = %s; invalid fact = %d\n", op, fact);
                    valid = false;
                    break;
                }
                border = fact;
                break;
            default:
                Utils.LeptStderr("*** nonexistent op = %s\n", op);
                valid = false;
                break;
            }
        }

        if (border != 0 && netred != 0) {
            /* NOTE(upstream): C prints op after LEPT_FREE(op), a use after free.
             * Here it is the last operation of the sequence. */
            Utils.LeptStderr("*** op = %s; border added but net reduction not 0\n", op);
            valid = false;
        }
        return valid;
    }


    /*-----------------------------------------------------------------*
     *       Run a sequence of grayscale morphological operations      *
     *-----------------------------------------------------------------*/
    // leptonica: pixGrayMorphSequence (morphseq.c)
    /// <summary>Runs a sequence of grayscale morphological operations.</summary>
    /// <param name="sequence">string specifying sequence of operations</param>
    /// <param name="dispsep">controls debug display of results in the sequence:
    ///     0: no output;
    ///     &gt; 0: gives horizontal separation in pixels between successive displays;
    ///     &lt; 0: pdf output; abs(dispsep) is used for naming</param>
    /// <param name="dispy">if dispsep &gt; 0, this gives the y-value of the
    ///     UL corner for display; otherwise it is ignored</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This works on 8 bpp grayscale images.
    /// (2) This runs a pipeline of operations; no branching is allowed.
    /// (3) This only uses brick SELs.
    /// (4) A new image is always produced; the input image is not changed.
    /// (5) This contains an interpreter, allowing sequences to be
    ///     generated and run.
    /// (6) The format of the sequence string is defined below.
    /// (7) In addition to morphological operations, the composite
    ///     morph/subtract tophat can be performed.
    /// (8) Sel sizes (width, height) must each be odd numbers.
    /// (9) Intermediate results can optionally be displayed
    /// (10) The sequence string is formatted as follows:
    ///      ~ An arbitrary number of operations,  each separated
    ///        by a '+' character.  White space is ignored.
    ///      ~ Each operation begins with a case-independent character
    ///        specifying the operation:
    ///           d or D  (dilation)
    ///           e or E  (erosion)
    ///           o or O  (opening)
    ///           c or C  (closing)
    ///           t or T  (tophat)
    ///      ~ The args to the morphological operations are bricks of hits,
    ///        and are formatted as a.b, where a and b are horizontal and
    ///        vertical dimensions, rsp. (each must be an odd number)
    ///      ~ The args to the tophat are w or W (for white tophat)
    ///        or b or B (for black tophat), followed by a.b as for
    ///        the dilation, erosion, opening and closing.
    ///     Example valid sequences are:
    ///       "c5.3 + o7.5"
    ///       "c9.9 + tw9.9"
    /// </remarks>
    public Pix GrayMorphSequence(string sequence, int dispsep, int dispy)
    {
        const string func = "pixGrayMorphSequence";
        Pix pixs = this;
        string rawop, op;
        string fname;
        int nops, i, w = 0, h = 0, x, pdfout;
        bool valid;
        Pix? pix1, pix2;
        Pixa? pixa;
        Sarray sa;

            /* Split sequence into individual operations */
        sa = Sarray.Create(0);
        sa.SplitString(sequence, "+");
        nops = sa.Count;
        pdfout = (dispsep < 0) ? 1 : 0;

            /* Verify that the operation sequence is valid */
        valid = true;
        for (i = 0; i < nops; i++) {
            rawop = sa.GetString(i, L.L_NOCOPY);
            op = Utils.StringRemoveChars(rawop, " \n\t");
            switch (OpChar(op, 0))
            {
            case 'd':
            case 'D':
            case 'e':
            case 'E':
            case 'o':
            case 'O':
            case 'c':
            case 'C':
                if (Sscanf2(op.Substring(1), ref w, ref h) != 2) {
                    Utils.LeptStderr("*** op: %s invalid\n", op);
                    valid = false;
                    break;
                }
                if (w < 1 || (w & 1) == 0 || h < 1 || (h & 1) == 0) {
                    Utils.LeptStderr("*** op: %s; w = %d, h = %d; must both be odd\n",
                                     op, w, h);
                    valid = false;
                    break;
                }
                break;
            case 't':
            case 'T':
                if (OpChar(op, 1) != 'w' && OpChar(op, 1) != 'W' &&
                    OpChar(op, 1) != 'b' && OpChar(op, 1) != 'B') {
                    Utils.LeptStderr(
                            "*** op = %s; arg %c must be 'w' or 'b'\n", op, OpChar(op, 1));
                    valid = false;
                    break;
                }
                Sscanf2(op.Substring(2), ref w, ref h);
                if (w < 1 || (w & 1) == 0 || h < 1 || (h & 1) == 0) {
                    Utils.LeptStderr("*** op: %s; w = %d, h = %d; must both be odd\n",
                                     op, w, h);
                    valid = false;
                    break;
                }
                break;
            default:
                Utils.LeptStderr("*** nonexistent op = %s\n", op);
                valid = false;
                break;
            }
        }
        if (!valid)
            throw Lept.Arg(func, "sequence invalid");

            /* Parse and operate */
        pixa = null;
        if (pdfout != 0) {
            pixa = Pixa.Create(0);
            pixa.AddPix(pixs, L.L_CLONE);
        }
        pix1 = Copy(null, pixs);
        pix2 = null;
        x = 0;
        for (i = 0; i < nops; i++) {
            rawop = sa.GetString(i, L.L_NOCOPY);
            op = Utils.StringRemoveChars(rawop, " \n\t");
            switch (OpChar(op, 0))
            {
            case 'd':
            case 'D':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = pix1!.DilateGray(w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'e':
            case 'E':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = pix1!.ErodeGray(w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'o':
            case 'O':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = pix1!.OpenGray(w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'c':
            case 'C':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = pix1!.CloseGray(w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 't':
            case 'T':
                Sscanf2(op.Substring(2), ref w, ref h);
                if (OpChar(op, 1) == 'w' || OpChar(op, 1) == 'W')
                    pix2 = pix1!.Tophat(w, h, L.L_TOPHAT_WHITE);
                else   /* 'b' or 'B' */
                    pix2 = pix1!.Tophat(w, h, L.L_TOPHAT_BLACK);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            default:
                /* All invalid ops are caught in the first pass */
                break;
            }

                /* Debug output */
            if (dispsep > 0) {
                pix1!.Display(x, dispy);
                x += dispsep;
            }
            if (pdfout != 0)
                pixa!.AddPix(pix1!, L.L_COPY);
        }

        if (pdfout != 0) {
            fname = $"/tmp/lept/seq_output_{Math.Abs(dispsep)}.pdf";
            pixa!.ConvertToPdf(0, 1.0f, L.L_FLATE_ENCODE, 0, fname, fname);
        }

        return pix1!;
    }


    /*-----------------------------------------------------------------*
     *         Run a sequence of color morphological operations        *
     *-----------------------------------------------------------------*/
    // leptonica: pixColorMorphSequence (morphseq.c)
    /// <summary>Runs a sequence of color morphological operations.</summary>
    /// <param name="sequence">string specifying sequence of operations</param>
    /// <param name="dispsep">controls debug display of results in the sequence:
    ///     0: no output;
    ///     &gt; 0: gives horizontal separation in pixels between successive displays;
    ///     &lt; 0: pdf output; abs(dispsep) is used for naming</param>
    /// <param name="dispy">if dispsep &gt; 0, this gives the y-value of the
    ///     UL corner for display; otherwise it is ignored</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This works on 32 bpp rgb images.
    /// (2) Each component is processed separately.
    /// (3) This runs a pipeline of operations; no branching is allowed.
    /// (4) This only uses brick SELs.
    /// (5) A new image is always produced; the input image is not changed.
    /// (6) This contains an interpreter, allowing sequences to be
    ///     generated and run.
    /// (7) Sel sizes (width, height) must each be odd numbers.
    /// (8) The format of the sequence string is defined below.
    /// (9) Intermediate results can optionally be displayed.
    /// (10) The sequence string is formatted as follows:
    ///      ~ An arbitrary number of operations,  each separated
    ///        by a '+' character.  White space is ignored.
    ///      ~ Each operation begins with a case-independent character
    ///        specifying the operation:
    ///           d or D  (dilation)
    ///           e or E  (erosion)
    ///           o or O  (opening)
    ///           c or C  (closing)
    ///      ~ The args to the morphological operations are bricks of hits,
    ///        and are formatted as a.b, where a and b are horizontal and
    ///        vertical dimensions, rsp. (each must be an odd number)
    ///     Example valid sequences are:
    ///       "c5.3 + o7.5"
    ///       "D9.1"
    /// </remarks>
    public Pix ColorMorphSequence(string sequence, int dispsep, int dispy)
    {
        const string func = "pixColorMorphSequence";
        Pix pixs = this;
        string rawop, op;
        string fname;
        int nops, i, w = 0, h = 0, x, pdfout;
        bool valid;
        Pix? pix1, pix2;
        Pixa? pixa;
        Sarray sa;

            /* Split sequence into individual operations */
        sa = Sarray.Create(0);
        sa.SplitString(sequence, "+");
        nops = sa.Count;
        pdfout = (dispsep < 0) ? 1 : 0;

            /* Verify that the operation sequence is valid */
        valid = true;
        for (i = 0; i < nops; i++) {
            rawop = sa.GetString(i, L.L_NOCOPY);
            op = Utils.StringRemoveChars(rawop, " \n\t");
            switch (OpChar(op, 0))
            {
            case 'd':
            case 'D':
            case 'e':
            case 'E':
            case 'o':
            case 'O':
            case 'c':
            case 'C':
                if (Sscanf2(op.Substring(1), ref w, ref h) != 2) {
                    Utils.LeptStderr("*** op: %s invalid\n", op);
                    valid = false;
                    break;
                }
                if (w < 1 || (w & 1) == 0 || h < 1 || (h & 1) == 0) {
                    Utils.LeptStderr("*** op: %s; w = %d, h = %d; must both be odd\n",
                                     op, w, h);
                    valid = false;
                    break;
                }
                break;
            default:
                Utils.LeptStderr("*** nonexistent op = %s\n", op);
                valid = false;
                break;
            }
        }
        if (!valid)
            throw Lept.Arg(func, "sequence invalid");

            /* Parse and operate */
        pixa = null;
        if (pdfout != 0) {
            pixa = Pixa.Create(0);
            pixa.AddPix(pixs, L.L_CLONE);
        }
        pix1 = Copy(null, pixs);
        pix2 = null;
        x = 0;
        for (i = 0; i < nops; i++) {
            rawop = sa.GetString(i, L.L_NOCOPY);
            op = Utils.StringRemoveChars(rawop, " \n\t");
            switch (OpChar(op, 0))
            {
            case 'd':
            case 'D':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = pix1!.ColorMorph(L.L_MORPH_DILATE, w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'e':
            case 'E':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = pix1!.ColorMorph(L.L_MORPH_ERODE, w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'o':
            case 'O':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = pix1!.ColorMorph(L.L_MORPH_OPEN, w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            case 'c':
            case 'C':
                Sscanf2(op.Substring(1), ref w, ref h);
                pix2 = pix1!.ColorMorph(L.L_MORPH_CLOSE, w, h);
                SwapAndDestroy(ref pix1, ref pix2);
                break;
            default:
                /* All invalid ops are caught in the first pass */
                break;
            }

                /* Debug output */
            if (dispsep > 0) {
                pix1!.Display(x, dispy);
                x += dispsep;
            }
            if (pdfout != 0)
                pixa!.AddPix(pix1!, L.L_COPY);
        }

        if (pdfout != 0) {
            fname = $"/tmp/lept/seq_output_{Math.Abs(dispsep)}.pdf";
            pixa!.ConvertToPdf(0, 1.0f, L.L_FLATE_ENCODE, 0, fname, fname);
        }

        return pix1!;
    }
}
