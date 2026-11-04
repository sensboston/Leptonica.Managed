using System;

namespace Leptonica;

/*
 *  runlength.c
 *
 *     Label pixels by membership in runs
 *           Pix.StrokeWidthTransform (pixStrokeWidthTransform)
 *           private Pix.FindMinRunsOrthogonal (pixFindMinRunsOrthogonal)
 *           Pix.RunlengthTransform (pixRunlengthTransform)
 *
 *     Find runs along horizontal and vertical lines
 *           Pix.FindHorizontalRuns (pixFindHorizontalRuns)
 *           Pix.FindVerticalRuns (pixFindVerticalRuns)
 *
 *     Find max runs along horizontal and vertical lines
 *           Pix.FindMaxRuns (pixFindMaxRuns)
 *           Pix.FindMaxHorizontalRunOnLine (pixFindMaxHorizontalRunOnLine)
 *           Pix.FindMaxVerticalRunOnLine (pixFindMaxVerticalRunOnLine)
 *
 *     Compute runlength-to-membership transform on a line
 *           L.RunlengthMembershipOnLine (runlengthMembershipOnLine)
 *
 *     Make byte position LUT
 *           L.MakeMSBitLocTab (makeMSBitLocTab)
 *
 *  Here we're handling runs of either black or white pixels on 1 bpp
 *  images.  The directions of the runs in the stroke width transform
 *  are selectable from given sets of angles.  Most of the other runs
 *  are oriented either horizontally along the raster lines or
 *  vertically along pixel columns.
 */

/// <summary>Constants from pix.h used by runlength.c.</summary>
public static partial class L
{
    /* Direction of runs (pix.h) */
    /// <summary>Determine the lengths of horizontal runs.</summary>
    public const int L_HORIZONTAL_RUNS = 0;
    /// <summary>Determine the lengths of vertical runs.</summary>
    public const int L_VERTICAL_RUNS = 1;

    /*-----------------------------------------------------------------------*
     *            Compute runlength-to-membership transform on a line        *
     *-----------------------------------------------------------------------*/
    // leptonica: runlengthMembershipOnLine (runlength.c)
    /// <summary>Converts a set of runlengths into a buffer of runlength membership values.</summary>
    /// <param name="buffer">into which full line of data is placed</param>
    /// <param name="size">full size of line; w or h</param>
    /// <param name="depth">8 or 16 bpp</param>
    /// <param name="start">array of start positions for fg runs</param>
    /// <param name="end">array of end positions for fg runs</param>
    /// <param name="n">the number of runs</param>
    /// <remarks>
    /// (1) Converts a set of runlengths into a buffer of
    ///     runlength membership values.
    /// (2) Initialization of the array gives pixels that are
    ///     not within a run the value 0.
    /// </remarks>
    public static void RunlengthMembershipOnLine(int[] buffer, int size, int depth,
                                                 int[] start, int[] end, int n)
    {
        int max;
        if (depth == 8)
            max = 0xff;
        else  /* depth == 16 */
            max = 0xffff;

        Array.Clear(buffer, 0, size);
        for (int i = 0; i < n; i++) {
            int first = start[i];
            int last = end[i];
            int diff = last - first + 1;
            diff = Math.Min(diff, max);
            for (int j = first; j <= last; j++)
                buffer[j] = diff;
        }
    }

    /*-----------------------------------------------------------------------*
     *                       Make byte position LUT                          *
     *-----------------------------------------------------------------------*/
    // leptonica: makeMSBitLocTab (runlength.c)
    /// <summary>Makes a table of the location of the most significant bit of a given color in a byte.</summary>
    /// <param name="bitval">either 0 or 1</param>
    /// <returns>table: for an input byte, the MS bit location, starting at 0
    ///          with the MSBit in the byte</returns>
    /// <remarks>
    /// (1) If bitval == 1, it finds the leftmost ON pixel in a byte;
    ///     otherwise if bitval == 0, it finds the leftmost OFF pixel.
    /// (2) If there are no pixels of the indicated color in the byte,
    ///     this returns 8.
    /// </remarks>
    public static int[] MakeMSBitLocTab(int bitval)
    {
        int[] tab = new int[256];
        for (int i = 0; i < 256; i++) {
            byte b = (byte)i;
            if (bitval == 0)
                b = (byte)~b;
            tab[i] = 8;
            byte mask = 0x80;
            for (int j = 0; j < 8; j++) {
                if ((b & mask) != 0) {
                    tab[i] = j;
                    break;
                }
                mask >>= 1;
            }
        }
        return tab;
    }
}

public sealed partial class Pix
{
    /*-----------------------------------------------------------------------*
     *                   Label pixels by membership in runs                  *
     *-----------------------------------------------------------------------*/
    // leptonica: pixStrokeWidthTransform (runlength.c)
    /// <summary>Labels pixels by the width of the stroke in which they are members.</summary>
    /// <param name="color">0 for white runs, 1 for black runs</param>
    /// <param name="depth">of pixd: 8 or 16 bpp</param>
    /// <param name="nangles">2, 4, 6 or 8</param>
    /// <returns>pixd 8 or 16 bpp</returns>
    /// <remarks>
    /// (1) The dest Pix is 8 or 16 bpp, with the pixel values
    ///     equal to the stroke width in which it is a member.
    ///     The values are clipped to the max pixel value if necessary.
    /// (2) color determines if we're labelling white or black strokes.
    /// (3) A pixel that is not a member of the chosen color gets
    ///     value 0; it belongs to a width of length 0 of the
    ///     chosen color.
    /// (4) This chooses, for each dest pixel, the minimum of sets
    ///     of runlengths through each pixel.  Here are the sets:
    ///       nangles    increment          set
    ///       -------    ---------    --------------------------------
    ///          2          90       {0, 90}
    ///          4          45       {0, 45, 90, 135}
    ///          6          30       {0, 30, 60, 90, 120, 150}
    ///          8          22.5     {0, 22.5, 45, 67.5, 90, 112.5, 135, 157.5}
    /// (5) Runtime scales linearly with (nangles - 2).
    /// </remarks>
    public Pix StrokeWidthTransform(int color, int depth, int nangles)
    {
        const string func = "pixStrokeWidthTransform";
        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (depth != 8 && depth != 16)
            throw Lept.Arg(func, "depth must be 8 or 16 bpp");
        if (nangles != 2 && nangles != 4 && nangles != 6 && nangles != 8)
            throw Lept.Arg(func, "nangles not in {2,4,6,8}");

            /* Use fg runs for evaluation */
        Pix pixt;
        if (color == 0)
            pixt = Invert(null, this);
        else
            pixt = this;

            /* Find min length at 0 and 90 degrees */
        Pix pixh = pixt.RunlengthTransform(1, L.L_HORIZONTAL_RUNS, depth);
        Pix pixv = pixt.RunlengthTransform(1, L.L_VERTICAL_RUNS, depth);
        Pix pixg1 = MinOrMax(null, pixh, pixv, L.L_CHOOSE_MIN);

        Pix? pixg2 = null, pixg3 = null, pixg4 = null;
        float angle;
        float pi = 3.1415926535f;
        if (nangles == 4 || nangles == 8) {
                /* Find min length at +45 and -45 degrees */
            angle = pi / 4.0f;
            pixg2 = pixt.FindMinRunsOrthogonal(angle, depth);
        }

        if (nangles == 6) {
                /* Find min length at +30 and -60 degrees */
            angle = pi / 6.0f;
            pixg2 = pixt.FindMinRunsOrthogonal(angle, depth);

                /* Find min length at +60 and -30 degrees */
            angle = pi / 3.0f;
            pixg3 = pixt.FindMinRunsOrthogonal(angle, depth);
        }

        if (nangles == 8) {
                /* Find min length at +22.5 and -67.5 degrees */
            angle = pi / 8.0f;
            pixg3 = pixt.FindMinRunsOrthogonal(angle, depth);

                /* Find min length at +67.5 and -22.5 degrees */
            angle = (float)(3.0 * pi / 8.0f);
            pixg4 = pixt.FindMinRunsOrthogonal(angle, depth);
        }

            /* NOTE(upstream): rotation by shear fails for |angle| > 0.5 radians,
             * so pi/4, pi/6, pi/3 and 3*pi/8 give null here.  In C, pixMinOrMax()
             * then reports an error and leaves pixg1 unchanged; keep that. */
        if (nangles > 2 && pixg2 != null)
            MinOrMax(pixg1, pixg1, pixg2, L.L_CHOOSE_MIN);
        if (nangles > 4 && pixg3 != null)
            MinOrMax(pixg1, pixg1, pixg3, L.L_CHOOSE_MIN);
        if (nangles > 6 && pixg4 != null)
            MinOrMax(pixg1, pixg1, pixg4, L.L_CHOOSE_MIN);
        return pixg1;
    }

    // leptonica: pixFindMinRunsOrthogonal (runlength.c)
    /// <summary>Computes, for each fg pixel, the minimum of the runlengths through it at angle and angle + 90.</summary>
    /// <param name="angle">in radians</param>
    /// <param name="depth">of pixd: 8 or 16 bpp</param>
    /// <returns>pixd 8 or 16 bpp</returns>
    /// <remarks>
    /// (1) This computes, for each fg pixel in pixs, the minimum of
    ///     the runlengths going through that pixel in two orthogonal
    ///     directions: at angle and at (90 + angle).
    /// (2) We use rotation by shear because the forward and backward
    ///     rotations by the same angle are exact inverse operations.
    ///     As a result, the nonzero pixels in pixd correspond exactly
    ///     to the fg pixels in pixs.  This is not the case with
    ///     sampled rotation, due to spatial quantization.  Nevertheless,
    ///     the result suffers from lack of exact correspondence
    ///     between original and rotated pixels, also due to spatial
    ///     quantization, causing some boundary pixels to be
    ///     shifted from bg to fg or v.v.
    /// </remarks>
    private Pix? FindMinRunsOrthogonal(float angle, int depth)
    {
        const string func = "pixFindMinRunsOrthogonal";
        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");

            /* Rasterop into the center of a sufficiently large image
             * so we don't lose pixels for any rotation angle. */
        GetDimensions(out int w, out int h, out _);
        int diag = (int)(Math.Sqrt((double)(w * w + h * h)) + 2.5);
        int xoff = (diag - w) / 2;
        int yoff = (diag - h) / 2;
        Pix pixb = Create(diag, diag, 1);
        pixb.Rasterop(xoff, yoff, w, h, L.PIX_SRC, this, 0, 0);

            /* Rotate about the 'center', get the min of orthogonal transforms,
             * rotate back, and crop the part corresponding to pixs.  */
        Pix? pixr = pixb.RotateShear(diag / 2, diag / 2, angle, L.L_BRING_IN_WHITE);
        if (pixr == null)   /* angle too large; C continues with NULLs and returns NULL */
            return null;
        Pix pixh = pixr.RunlengthTransform(1, L.L_HORIZONTAL_RUNS, depth);
        Pix pixv = pixr.RunlengthTransform(1, L.L_VERTICAL_RUNS, depth);
        Pix pixg1 = MinOrMax(null, pixh, pixv, L.L_CHOOSE_MIN);
        Pix pixg2 = pixg1.RotateShear(diag / 2, diag / 2, -angle, L.L_BRING_IN_WHITE)!;
        Box box = Box.Create(xoff, yoff, w, h);
        Pix pixd = pixg2.ClipRectangle(box, out _)!;
        return pixd;
    }

    // leptonica: pixRunlengthTransform (runlength.c)
    /// <summary>Labels pixels by the length of the run in which they are members.</summary>
    /// <param name="color">0 for white runs, 1 for black runs</param>
    /// <param name="direction">L_HORIZONTAL_RUNS, L_VERTICAL_RUNS</param>
    /// <param name="depth">8 or 16 bpp</param>
    /// <returns>pixd 8 or 16 bpp</returns>
    /// <remarks>
    /// (1) The dest Pix is 8 or 16 bpp, with the pixel values
    ///     equal to the runlength in which it is a member.
    ///     The length is clipped to the max pixel value if necessary.
    /// (2) color determines if we're labelling white or black runs.
    /// (3) A pixel that is not a member of the chosen color gets
    ///     value 0; it belongs to a run of length 0 of the
    ///     chosen color.
    /// (4) To convert for maximum dynamic range, either linear or
    ///     log, use pixMaxDynamicRange().
    /// </remarks>
    public Pix RunlengthTransform(int color, int direction, int depth)
    {
        const string func = "pixRunlengthTransform";
        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (depth != 8 && depth != 16)
            throw Lept.Arg(func, "depth must be 8 or 16 bpp");

        GetDimensions(out int w, out int h, out _);
        int maxsize;
        if (direction == L.L_HORIZONTAL_RUNS)
            maxsize = 1 + w / 2;
        else if (direction == L.L_VERTICAL_RUNS)
            maxsize = 1 + h / 2;
        else
            throw Lept.Arg(func, "invalid direction");
        int bufsize = Math.Max(w, h);
        if (bufsize > 1000000) {
            Log.Error(func, $"largest image dimension = {bufsize}; too big\n");
            throw Lept.Fail(func, "image too big");
        }

        Pix pixd = Create(w, h, depth);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        int[] start = new int[maxsize];
        int[] end = new int[maxsize];
        int[] buffer = new int[bufsize];

            /* Use fg runs for evaluation */
        Pix pixt;
        if (color == 0)
            pixt = Invert(null, this);
        else
            pixt = this;

        int n;
        if (direction == L.L_HORIZONTAL_RUNS) {
            for (int i = 0; i < h; i++) {
                pixt.FindHorizontalRuns(i, start, end, out n);
                L.RunlengthMembershipOnLine(buffer, w, depth, start, end, n);
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                if (depth == 8) {
                    for (int j = 0; j < w; j++)
                        ArrayAccess.SetDataByte(lined, j, buffer[j]);
                } else {  /* depth == 16 */
                    for (int j = 0; j < w; j++)
                        ArrayAccess.SetDataTwoBytes(lined, j, buffer[j]);
                }
            }
        } else {  /* L_VERTICAL_RUNS */
            for (int j = 0; j < w; j++) {
                pixt.FindVerticalRuns(j, start, end, out n);
                L.RunlengthMembershipOnLine(buffer, h, depth, start, end, n);
                if (depth == 8) {
                    for (int i = 0; i < h; i++) {
                        Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                        ArrayAccess.SetDataByte(lined, j, buffer[i]);
                    }
                } else {  /* depth == 16 */
                    for (int i = 0; i < h; i++) {
                        Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                        ArrayAccess.SetDataTwoBytes(lined, j, buffer[i]);
                    }
                }
            }
        }

        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *               Find runs along horizontal and vertical lines           *
     *-----------------------------------------------------------------------*/
    // leptonica: pixFindHorizontalRuns (runlength.c)
    /// <summary>Finds foreground horizontal runs on a single scanline.</summary>
    /// <param name="y">line to traverse</param>
    /// <param name="xstart">returns array of start positions for fg runs</param>
    /// <param name="xend">returns array of end positions for fg runs</param>
    /// <param name="n">the number of runs found</param>
    /// <remarks>
    /// (1) This finds foreground horizontal runs on a single scanline.
    /// (2) To find background runs, use pixInvert() before applying
    ///     this function.
    /// (3) xstart and xend arrays are input.  They should be
    ///     of size w/2 + 1 to insure that they can hold
    ///     the maximum number of runs in the raster line.
    /// </remarks>
    public void FindHorizontalRuns(int y, int[] xstart, int[] xend, out int n)
    {
        const string func = "pixFindHorizontalRuns";
        n = 0;
        GetDimensions(out int w, out int h, out int d);
        if (d != 1)
            throw Lept.Arg(func, "pix not 1 bpp");
        if (y < 0 || y >= h)
            throw Lept.Arg(func, "y not in [0 ... h - 1]");

        ReadOnlySpan<uint> line = Data.AsSpan(y * Wpl, Wpl);

        bool inrun = false;
        int index = 0;
        for (int j = 0; j < w; j++) {
            int val = ArrayAccess.GetDataBit(line, j);
            if (!inrun) {
                if (val != 0) {
                    xstart[index] = j;
                    inrun = true;
                }
            } else {
                if (val == 0) {
                    xend[index++] = j - 1;
                    inrun = false;
                }
            }
        }

            /* Finish last run if necessary */
        if (inrun)
            xend[index++] = w - 1;

        n = index;
    }

    // leptonica: pixFindVerticalRuns (runlength.c)
    /// <summary>Finds foreground vertical runs on a single column.</summary>
    /// <param name="x">line to traverse</param>
    /// <param name="ystart">returns array of start positions for fg runs</param>
    /// <param name="yend">returns array of end positions for fg runs</param>
    /// <param name="n">the number of runs found</param>
    /// <remarks>
    /// (1) This finds foreground vertical runs on a single scanline.
    /// (2) To find background runs, use pixInvert() before applying
    ///     this function.
    /// (3) ystart and yend arrays are input.  They should be
    ///     of size h/2 + 1 to insure that they can hold
    ///     the maximum number of runs in the raster line.
    /// </remarks>
    public void FindVerticalRuns(int x, int[] ystart, int[] yend, out int n)
    {
        const string func = "pixFindVerticalRuns";
        n = 0;
        GetDimensions(out int w, out int h, out int d);
        if (d != 1)
            throw Lept.Arg(func, "pix not 1 bpp");
        if (x < 0 || x >= w)
            throw Lept.Arg(func, "x not in [0 ... w - 1]");

        int wpl = Wpl;
        uint[] data = Data;

        bool inrun = false;
        int index = 0;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            int val = ArrayAccess.GetDataBit(line, x);
            if (!inrun) {
                if (val != 0) {
                    ystart[index] = i;
                    inrun = true;
                }
            } else {
                if (val == 0) {
                    yend[index++] = i - 1;
                    inrun = false;
                }
            }
        }

            /* Finish last run if necessary */
        if (inrun)
            yend[index++] = h - 1;

        n = index;
    }

    /*-----------------------------------------------------------------------*
     *            Find max runs along horizontal and vertical lines          *
     *-----------------------------------------------------------------------*/
    // leptonica: pixFindMaxRuns (runlength.c)
    /// <summary>Finds the longest foreground runs by row or column.</summary>
    /// <param name="direction">L_HORIZONTAL_RUNS or L_VERTICAL_RUNS</param>
    /// <param name="pnastart">start locations of longest runs</param>
    /// <returns>na of lengths of runs</returns>
    /// <remarks>
    /// (1) This finds the longest foreground runs by row or column
    /// (2) To find background runs, use pixInvert() before applying
    ///     this function.
    /// </remarks>
    public Numa FindMaxRuns(int direction, out Numa pnastart)
    {
        const string func = "pixFindMaxRuns";
        if (direction != L.L_HORIZONTAL_RUNS && direction != L.L_VERTICAL_RUNS)
            throw Lept.Arg(func, "direction invalid");
        if (Depth != 1)
            throw Lept.Arg(func, "pix undefined or not 1 bpp");

        GetDimensions(out int w, out int h, out _);
        Numa nasize = Numa.Create(w);
        pnastart = Numa.Create(w);
        if (direction == L.L_HORIZONTAL_RUNS) {
            for (int i = 0; i < h; i++) {
                FindMaxHorizontalRunOnLine(i, out int start, out int size);
                nasize.AddNumber(size);
                pnastart.AddNumber(start);
            }
        } else {  /* vertical scans */
            for (int i = 0; i < w; i++) {
                FindMaxVerticalRunOnLine(i, out int start, out int size);
                nasize.AddNumber(size);
                pnastart.AddNumber(start);
            }
        }

        return nasize;
    }

    // leptonica: pixFindMaxHorizontalRunOnLine (runlength.c)
    /// <summary>Finds the longest foreground horizontal run on a scanline.</summary>
    /// <param name="y">line to traverse</param>
    /// <param name="pxstart">start position</param>
    /// <param name="psize">the size of the run</param>
    /// <remarks>
    /// (1) This finds the longest foreground horizontal run on a scanline.
    /// (2) To find background runs, use pixInvert() before applying
    ///     this function.
    /// </remarks>
    public void FindMaxHorizontalRunOnLine(int y, out int pxstart, out int psize)
    {
        const string func = "pixFindMaxHorizontalRunOnLine";
        pxstart = 0;
        psize = 0;
        if (Depth != 1)
            throw Lept.Arg(func, "pix not defined or not 1 bpp");
        GetDimensions(out int w, out int h, out _);
        if (y < 0 || y >= h)
            throw Lept.Arg(func, "y not in [0 ... h - 1]");

        ReadOnlySpan<uint> line = Data.AsSpan(y * Wpl, Wpl);
        bool inrun = false;
        int start = 0;
        int maxstart = 0;
        int maxsize = 0;
        int length;
        int j;
        for (j = 0; j < w; j++) {
            int val = ArrayAccess.GetDataBit(line, j);
            if (!inrun) {
                if (val != 0) {
                    start = j;
                    inrun = true;
                }
            } else if (val == 0) {  /* run just ended */
                length = j - start;
                if (length > maxsize) {
                    maxsize = length;
                    maxstart = start;
                }
                inrun = false;
            }
        }

        if (inrun) {  /* a run has continued to the end of the row */
            length = j - start;
            if (length > maxsize) {
                maxsize = length;
                maxstart = start;
            }
        }
        pxstart = maxstart;
        psize = maxsize;
    }

    // leptonica: pixFindMaxVerticalRunOnLine (runlength.c)
    /// <summary>Finds the longest foreground vertical run on a column.</summary>
    /// <param name="x">column to traverse</param>
    /// <param name="pystart">start position</param>
    /// <param name="psize">the size of the run</param>
    /// <remarks>
    /// (1) This finds the longest foreground vertical run on a scanline.
    /// (2) To find background runs, use pixInvert() before applying
    ///     this function.
    /// </remarks>
    public void FindMaxVerticalRunOnLine(int x, out int pystart, out int psize)
    {
        const string func = "pixFindMaxVerticalRunOnLine";
        pystart = 0;
        psize = 0;
        if (Depth != 1)
            throw Lept.Arg(func, "pix not defined or not 1 bpp");
        GetDimensions(out int w, out int h, out _);
        if (x < 0 || x >= w)
            throw Lept.Arg(func, "x not in [0 ... w - 1]");

        int wpl = Wpl;
        uint[] data = Data;
        bool inrun = false;
        int start = 0;
        int maxstart = 0;
        int maxsize = 0;
        int length;
        int i;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            int val = ArrayAccess.GetDataBit(line, x);
            if (!inrun) {
                if (val != 0) {
                    start = i;
                    inrun = true;
                }
            } else if (val == 0) {  /* run just ended */
                length = i - start;
                if (length > maxsize) {
                    maxsize = length;
                    maxstart = start;
                }
                inrun = false;
            }
        }

        if (inrun) {  /* a run has continued to the end of the column */
            length = i - start;
            if (length > maxsize) {
                maxsize = length;
                maxstart = start;
            }
        }
        pystart = maxstart;
        psize = maxsize;
    }
}
