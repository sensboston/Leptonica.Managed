using System;
using System.IO;
using System.Text;

namespace Leptonica;

/*
 *  ccbord.c: border tracing of connected components.
 *
 *     CCBORDA and CCBORD creation and destruction
 *         static CCBORDA  *ccbaCreate()
 *         static CCBORD   *ccbCreate()
 *
 *     CCBORDA addition
 *         static l_int32   ccbaAddCcb()
 *         static l_int32   ccbaExtendArray()
 *
 *     CCBORDA accessors
 *         ccbaGetCount, ccbaGetCcb
 *
 *     Top-level border-finding routines
 *         Pix.GetAllCCBorders (pixGetAllCCBorders)
 *         Pix.GetOuterBordersPtaa (pixGetOuterBordersPtaa)
 *
 *     Lower-level border location routines
 *         Pix.GetOuterBorder (pixGetOuterBorder)
 *
 *     Border conversions
 *         CCBorda.GenerateGlobalLocs, GenerateStepChains, StepChainsToPixCoords,
 *         GenerateSPGlobalLocs
 *
 *     Conversion to single path
 *         CCBorda.GenerateSinglePath
 *
 *     Border and full image rendering
 *         CCBorda.DisplayBorder, DisplaySPBorder, DisplayImage1, DisplayImage2
 *
 *     Serialize for I/O
 *         CCBorda.Write, WriteStream, Read, ReadStream
 *
 *     SVG output
 *         CCBorda.WriteSVG, WriteSVGString
 *
 *  Not ported: ccbaDestroy, ccbDestroy (memory management only).  The
 *  reference count of the CCBORD is not kept: ccbaGetCcb() returns the
 *  object itself.
 *
 *  Border finding is tricky because components can have holes, which also
 *  need to be traced out.  The outer border can be connected with all the
 *  hole borders, so that there is a single border for each component.
 *  [Alternatively, the connecting paths can be eliminated if you're willing
 *  to have a set of borders for each component (an exterior border and some
 *  number of interior ones), with "line to" operations tracing out each
 *  border and "move to" operations going from one border to the next.]
 *
 *  Here's the plan.  We get the pix for each connected component, and trace
 *  its exterior border.  We then find the holes (if any) in the pix, and
 *  separately trace out their borders, all using the same border-following
 *  rule that has ON pixels on the right side of the path.
 *
 *  Each border is a closed path that is traversed in such a way that the
 *  stuff inside the c.c. is on the right side of the traveller.  The border
 *  of a singly-connected component is thus traversed cw, and the border of
 *  the holes inside a c.c. are traversed ccw.
 *
 *  Reconstruction, Method 1: topological method using connected components.
 *  Fill the components within and including the cw borders and XOR them onto
 *  the destination; fill the components within but not including the ccw
 *  borders and XOR them onto the destination.
 *
 *  Reconstruction, Method 2: a variant of Method 1 in which all borders in a
 *  c.c. are treated equally.  One seed pixel is put outside each border, the
 *  seeds are filled into the region clipped by the border pixels, and the
 *  inverse of the result is the c.c. with its holes.  Method 2 is about 1.2x
 *  faster than Method 1 on text images, and about 2x faster on complex images.
 */

/// <summary>Constants from ccbord.h.</summary>
public static partial class L
{
    /* Coordinate type for the step chains */
    /// <summary>The step chains use coordinates local to the bounding box of each component.</summary>
    public const int CCB_LOCAL_COORDS = 1;

    /// <summary>The step chains use coordinates global to the image.</summary>
    public const int CCB_GLOBAL_COORDS = 2;

    /* Which points to save in the single path */

    /// <summary>Saves all the points in the single path.</summary>
    public const int CCB_SAVE_ALL_PTS = 1;

    /// <summary>Saves only the turning points in the single path.</summary>
    public const int CCB_SAVE_TURNING_PTS = 2;
}

/// <summary>A single CCBord for a connected component (struct CCBord).</summary>
public sealed class CCBord
{
    /// <summary>component bitmap (min size)</summary>
    public Pix? Pix { get; set; }
    /// <summary>regions of each closed curve</summary>
    public Boxa? Boxa { get; set; }
    /// <summary>initial border pixel locations</summary>
    public Pta? Start { get; set; }
    /// <summary>ptaa of chain pixels (local)</summary>
    public Ptaa? Local { get; set; }
    /// <summary>ptaa of chain pixels (global)</summary>
    public Ptaa? Global { get; set; }
    /// <summary>numaa of chain code (step dir)</summary>
    public Numaa? Step { get; set; }
    /// <summary>pta of single chain (local)</summary>
    public Pta? SpLocal { get; set; }
    /// <summary>pta of single chain (global)</summary>
    public Pta? SpGlobal { get; set; }

    // leptonica: ccbCreate (ccbord.c)
    /// <param name="pixs">[optional]; can be null</param>
    internal static CCBord Create(Pix? pixs)
    {
        if (pixs != null && pixs.Depth != 1)  /* pixs can be null */
            throw Lept.Arg("ccbCreate", "pixs defined and not 1bpp");

        CCBord ccb = new CCBord();
        if (pixs != null)
            ccb.Pix = Pix.Clone(pixs);
        ccb.Boxa = Boxa.Create(1);
        ccb.Start = Pta.Create(1);
        ccb.Local = Ptaa.Create(1);
        return ccb;
    }
}

/// <summary>Array of CCBord (struct CCBorda).</summary>
public sealed class CCBorda
{
    private const int InitialPtrArraySize = 20;    /* n'import quoi */

    /* In ccbaGenerateSinglePath(): don't save holes
     * in c.c. with ridiculously many small holes   */
    private const int NMaxHoles = 150;

    /*  Tables used to trace the border.
     *   - The 8 pixel positions of neighbors Q are labeled clockwise
     *     starting from the west:
     *                  1   2   3
     *                  0   P   4
     *                  7   6   5
     *     where the labels are the index offset [0, ... 7] of Q relative to P.
     *   - xpostab[] and ypostab[] give the actual x and y pixel offsets
     *     of Q relative to P, indexed by the index offset.
     *   - qpostab[pos] gives the new index offset of Q relative to P, at
     *     the time that a new P has been chosen to be in index offset
     *     position 'pos' relative to the previous P.   The relation
     *     between P and Q is always 4-connected.  */
    private static readonly int[] xpostab = { -1, -1, 0, 1, 1, 1, 0, -1 };
    private static readonly int[] ypostab = { 0, -1, -1, -1, 0, 1, 1, 1 };
    private static readonly int[] qpostab = { 6, 6, 0, 0, 2, 2, 4, 4 };

    private CCBord?[] ccb;

    /// <summary>input pix (may be null)</summary>
    public Pix? Pix { get; private set; }
    /// <summary>width of pix</summary>
    public int Width { get; private set; }
    /// <summary>height of pix</summary>
    public int Height { get; private set; }
    /// <summary>number of ccbord in ptr array</summary>
    public int Count { get; private set; }
    /// <summary>number of ccbord ptrs allocated</summary>
    public int Nalloc { get; private set; }

    private CCBorda(int n)
    {
        Count = 0;
        Nalloc = n;
        ccb = new CCBord?[n];
    }

    /*---------------------------------------------------------------------*
     *                   ccba and ccb creation and destruction             *
     *---------------------------------------------------------------------*/
    // leptonica: ccbaCreate (ccbord.c)
    /// <param name="pixs">1 bpp; can be null</param>
    /// <param name="n">initial number of ptrs</param>
    private static CCBorda Create(Pix? pixs, int n)
    {
        if (n <= 0)
            n = InitialPtrArraySize;

        CCBorda ccba = new CCBorda(n);
        if (pixs != null) {
            ccba.Pix = Pix.Clone(pixs);
            ccba.Width = pixs.Width;
            ccba.Height = pixs.Height;
        }
        return ccba;
    }

    /*---------------------------------------------------------------------*
     *                            ccba addition                            *
     *---------------------------------------------------------------------*/
    // leptonica: ccbaAddCcb (ccbord.c)
    /// <param name="ccbIn">to be added by insertion</param>
    private void AddCcb(CCBord ccbIn)
    {
        int n = Count;
        if (n >= Nalloc)
            ExtendArray();
        ccb[n] = ccbIn;
        Count++;
    }

    // leptonica: ccbaExtendArray (ccbord.c)
    private void ExtendArray()
    {
        Array.Resize(ref ccb, 2 * Nalloc);
        Nalloc = 2 * Nalloc;
    }

    /*---------------------------------------------------------------------*
     *                            ccba accessors                           *
     *---------------------------------------------------------------------*/
    // leptonica: ccbaGetCount (ccbord.c)
    // (the count is the property Count)

    // leptonica: ccbaGetCcb (ccbord.c)
    /// <summary>Gets the ccb at the index.</summary>
    /// <remarks>(1) C returns a clone with an incremented refcount; here it is the object itself.</remarks>
    public CCBord GetCcb(int index)
    {
        if (index < 0 || index >= Count)
            throw Lept.Arg("ccbaGetCcb", "index out of bounds");

        return ccb[index]!;
    }

    /*---------------------------------------------------------------------*
     *                   Top-level border-finding routines                 *
     *---------------------------------------------------------------------*/
    // (implementation of pixGetAllCCBorders)
    /// <param name="pixs">1 bpp</param>
    /// <returns>ccborda</returns>
    internal static CCBorda GetAllCCBorders(Pix pixs)
    {
        if (pixs.Depth != 1)
            throw Lept.Arg("pixGetAllCCBorders", "pixs not binary");

        Boxa boxa = pixs.ConnComp(out Pixa pixa, 8);
        int n = boxa.Count;

        CCBorda ccba = Create(pixs, n);
        for (int i = 0; i < n; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            Box box = pixa.GetBox(i, L.L_CLONE)
                      ?? throw Lept.Arg("pixGetAllCCBorders", "box not found");
            CCBord ccb = GetCCBorders(pix, box);
            ccba.AddCcb(ccb);
        }

        return ccba;
    }

    // leptonica: pixGetCCBorders (ccbord.c)
    /// <param name="pixs">1 bpp, one 8-connected component</param>
    /// <param name="box">of %pixs, in global coords</param>
    /// <remarks>
    /// (1) We are finding the exterior and interior borders
    ///     of an 8-connected component.   This should be used
    ///     on a pix that has exactly one 8-connected component.
    /// (2) Typically, pixs is a c.c. in some larger pix.  The
    ///     input box gives its location in global coordinates.
    ///     This box is saved, as well as the boxes for the
    ///     borders of any holes within the c.c., but the latter
    ///     are given in relative coords within the c.c.
    /// (3) The calculations for the exterior border are done
    ///     on a pix with a 1-pixel
    ///     added border, but the saved pixel coordinates
    ///     are the correct (relative) ones for the input pix
    ///     (without a 1-pixel border)
    /// (4) For the definition of the three tables -- xpostab[], ypostab[]
    ///     and qpostab[] -- see above where they are defined.
    /// </remarks>
    private static CCBord GetCCBorders(Pix pixs, Box box)
    {
        int x, xh = 0, w, nh;
        int xs = 0, ys;   /* starting hole border pixel, relative in pixs */
        uint val;

        if (pixs.Depth != 1)
            throw Lept.Arg("pixGetCCBorders", "pixs not binary");

        if (pixs.Zero())
            throw Lept.Arg("pixGetCCBorders", "pixs all 0");

        CCBord ccb = CCBord.Create(pixs);

            /* Get the exterior border */
        Pix.GetOuterBorder(ccb, pixs, box);

            /* Find the holes, if any */
        Pix pixh = pixs.HolesByFilling(4);
        if (pixh.Zero())   /* no holes */
            return ccb;

            /* Get c.c. and locations of the holes */
        Boxa boxa = pixh.ConnComp(out Pixa pixa, 4);
        nh = boxa.Count;

            /* For each hole, find an interior pixel within the hole,
             * then march to the right and stop at the first border
             * pixel.  Save the bounding box of the border, which
             * is 1 pixel bigger on each side than the bounding box
             * of the hole itself.  Note that we use a pix of the
             * c.c. of the hole itself to be sure that we start
             * with a pixel in the hole of the proper component.
             * If we did everything from the parent component, it is
             * possible to start in a different hole that is within
             * the b.b. of a larger hole.  */
        w = pixs.Width;
        for (int i = 0; i < nh; i++) {
            Box boxt = boxa.GetBox(i, L.L_CLONE);
            Pix pixt = pixa.GetPix(i, L.L_CLONE);
            ys = boxt.Y;   /* there must be a hole pixel on this raster line */
            for (x = 0; x < boxt.Width; x++) {  /* look for (fg) hole pixel */
                pixt.GetPixel(x, 0, out val);
                if (val == 1) {
                    xh = x;
                    break;
                }
            }
            if (x == boxt.Width) {
                Log.Warning("pixGetCCBorders", "no hole pixel found!");
                continue;
            }
            for (x = xh + boxt.X; x < w; x++) {  /* look for (fg) border pixel */
                pixs.GetPixel(x, ys, out val);
                if (val == 1) {
                    xs = x;
                    break;
                }
            }
            Box boxe = Box.Create(boxt.X - 1, boxt.Y - 1, boxt.Width + 2, boxt.Height + 2);
            GetHoleBorder(ccb, pixs, boxe, xs, ys);
        }

        return ccb;
    }

    // (implementation of pixGetOuterBordersPtaa)
    /// <param name="pixs">1 bpp</param>
    /// <returns>ptaa of outer borders, in global coords</returns>
    internal static Ptaa GetOuterBordersPtaa(Pix pixs)
    {
        if (pixs.Depth != 1)
            throw Lept.Arg("pixGetOuterBordersPtaa", "pixs not binary");

        Boxa boxa = pixs.ConnComp(out Pixa pixa, 8);
        int n = boxa.Count;
        if (n == 0)
            throw Lept.Arg("pixGetOuterBordersPtaa", "pixs empty");

        Ptaa ptaa = Ptaa.Create(n);
        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            Pta pta = GetOuterBorderPta(pix, box);
            ptaa.AddPta(pta, L.L_INSERT);
        }

        return ptaa;
    }

    // leptonica: pixGetOuterBorderPta (ccbord.c)
    /// <param name="pixs">1 bpp, one 8-connected component</param>
    /// <param name="box">[optional] of %pixs, in global coordinates</param>
    /// <returns>pta of outer border, in global coords</returns>
    /// <remarks>
    /// (1) We are finding the exterior border of a single 8-connected
    ///     component.
    /// (2) If box is NULL, the outline returned is in the local coords
    ///     of the input pix.  Otherwise, box is assumed to give the
    ///     location of the pix in global coordinates, and the returned
    ///     pta will be in those global coordinates.
    /// </remarks>
    private static Pta GetOuterBorderPta(Pix pixs, Box? box)
    {
        if (pixs.Depth != 1)
            throw Lept.Arg("pixGetOuterBorderPta", "pixs not binary");

        if (pixs.Zero())
            throw Lept.Arg("pixGetOuterBorderPta", "pixs all 0");

        CCBord ccb = CCBord.Create(pixs);
        Box boxt;
        if (box == null)
            boxt = Box.Create(0, 0, pixs.Width, pixs.Height);
        else
            boxt = box.Clone();

            /* Get the exterior border in local coords */
        Pix.GetOuterBorder(ccb, pixs, boxt);
        Pta ptaloc = ccb.Local!.GetPta(0, L.L_CLONE);

            /* Transform to global coordinates, if they are given */
        Pta ptad;
        if (box != null) {
            box.GetGeometry(out int x, out int y, out _, out _);
            ptad = ptaloc.Transform(x, y, 1.0f, 1.0f);
        } else {
            ptad = ptaloc.Clone();
        }

        return ptad;
    }

    /*---------------------------------------------------------------------*
     *                   Lower-level border-finding routines               *
     *---------------------------------------------------------------------*/
    // (implementation of pixGetOuterBorder)
    /// <param name="ccb">unfilled</param>
    /// <param name="pixs">for the component at hand</param>
    /// <param name="box">for the component, in global coords</param>
    /// <remarks>
    /// (1) the border is saved in relative coordinates within
    ///     the c.c. (pixs).  Because the calculation is done
    ///     in pixb with added 1 pixel border, we must subtract
    ///     1 from each pixel value before storing it.
    /// (2) the stopping condition is that after the first pixel is
    ///     returned to, the next pixel is the second pixel.  Having
    ///     these 2 pixels recur in sequence proves the path is closed,
    ///     and we do not store the second pixel again.
    /// </remarks>
    internal static void GetOuterBorderImpl(CCBord ccb, Pix pixs, Box box)
    {
        int fpx, fpy, spx, spy, qpos;
        int px, py, npx, npy;
        int w, h, wpl;

            /* Add 1-pixel border all around, and find start pixel */
        Pix pixb = pixs.AddBorder(1, 0);  /* with 1 pixel border */
        if (!pixb.NextOnPixelInRaster(1, 1, out px, out py))
            throw Lept.Fail("pixGetOuterBorder", "no start pixel found");
        qpos = 0;   /* relative to p */
        fpx = px;  /* save location of first pixel on border */
        fpy = py;

            /* Save box and start pixel in relative coords */
        ccb.Boxa!.AddBox(box, L.L_COPY);
        ccb.Start!.AddPt(px - 1, py - 1);

        Pta pta = Pta.Create(0);
        ccb.Local!.AddPta(pta, L.L_INSERT);
        pta.AddPt(px - 1, py - 1);   /* initial point */
        pixb.GetDimensions(out w, out h, out _);
        uint[] data = pixb.Data;
        wpl = pixb.Wpl;

            /* Get the second point; if there is none, return */
        if (FindNextBorderPixel(w, h, data, wpl, px, py, ref qpos, out npx, out npy))
            return;

        spx = npx;  /* save location of second pixel on border */
        spy = npy;
        pta.AddPt(npx - 1, npy - 1);   /* second point */
        px = npx;
        py = npy;

        while (true) {
            FindNextBorderPixel(w, h, data, wpl, px, py, ref qpos, out npx, out npy);
            if (px == fpx && py == fpy && npx == spx && npy == spy)
                break;
            pta.AddPt(npx - 1, npy - 1);
            px = npx;
            py = npy;
        }
    }

    // leptonica: pixGetHoleBorder (ccbord.c)
    /// <param name="ccb">the exterior border is already made</param>
    /// <param name="pixs">for the connected component at hand</param>
    /// <param name="box">for the specific hole border, in relative coordinates to the c.c.</param>
    /// <param name="xs">first pixel on hole border, relative to c.c.</param>
    /// <param name="ys">first pixel on hole border, relative to c.c.</param>
    /// <remarks>
    /// (1) we trace out hole border on pixs without addition
    ///     of single pixel added border to pixs
    /// (2) therefore all coordinates are relative within the c.c. (pixs)
    /// (3) same position tables and stopping condition as for
    ///     exterior borders
    /// </remarks>
    private static void GetHoleBorder(CCBord ccb, Pix pixs, Box box, int xs, int ys)
    {
        int fpx, fpy, spx, spy, qpos;
        int px, py, npx, npy;

            /* Add border and find start pixel */
        qpos = 0;   /* orientation of Q relative to P */
        fpx = xs;  /* save location of first pixel on border */
        fpy = ys;

            /* Save box and start pixel */
        ccb.Boxa!.AddBox(box, L.L_COPY);
        ccb.Start!.AddPt(xs, ys);

        Pta pta = Pta.Create(0);
        ccb.Local!.AddPta(pta, L.L_INSERT);
        pta.AddPt(xs, ys);   /* initial pixel */

        int w = pixs.Width;
        int h = pixs.Height;
        uint[] data = pixs.Data;
        int wpl = pixs.Wpl;

            /* Get the second point; there should always be at least 4 pts
             * in a minimal hole border!  */
        if (FindNextBorderPixel(w, h, data, wpl, xs, ys, ref qpos, out npx, out npy))
            throw Lept.Fail("pixGetHoleBorder", "isolated hole border point!");

        spx = npx;  /* save location of second pixel on border */
        spy = npy;
        pta.AddPt(npx, npy);   /* second pixel */
        px = npx;
        py = npy;

        while (true) {
            FindNextBorderPixel(w, h, data, wpl, px, py, ref qpos, out npx, out npy);
            if (px == fpx && py == fpy && npx == spx && npy == spy)
                break;
            pta.AddPt(npx, npy);
            px = npx;
            py = npy;
        }
    }

    // leptonica: findNextBorderPixel (ccbord.c)
    /// <param name="w">width</param>
    /// <param name="h">height</param>
    /// <param name="data">pix data</param>
    /// <param name="wpl">words per line</param>
    /// <param name="px">current P</param>
    /// <param name="py">current P</param>
    /// <param name="pqpos">input current Q; new Q</param>
    /// <param name="pnpx">new P</param>
    /// <param name="pnpy">new P</param>
    /// <returns>false if next pixel found; true otherwise (C: 0 if found; 1 otherwise)</returns>
    /// <remarks>
    /// (1) qpos increases clockwise from 0 to 7, with 0 at
    ///     location with Q to left of P:   Q P
    /// (2) this is a low-level function that does not check input
    ///     parameters.  All calling functions should check them.
    /// </remarks>
    private static bool FindNextBorderPixel(int w, int h, uint[] data, int wpl, int px, int py,
                                            ref int pqpos, out int pnpx, out int pnpy)
    {
        int qpos = pqpos;
        pnpx = 0;
        pnpy = 0;
        for (int i = 1; i < 8; i++) {
            int pos = (qpos + i) % 8;
            int npx = px + xpostab[pos];
            int npy = py + ypostab[pos];
            if (npx < 0 || npx >= w || npy < 0 || npy >= h)
                continue;
            ReadOnlySpan<uint> line = data.AsSpan(npy * wpl);
            int val = ArrayAccess.GetDataBit(line, npx);
            if (val != 0) {
                pnpx = npx;
                pnpy = npy;
                pqpos = qpostab[pos];
                return false;
            }
        }

        return true;
    }

    // leptonica: locateOutsideSeedPixel (ccbord.c)
    /// <param name="fpx">location of first pixel</param>
    /// <param name="fpy">location of first pixel</param>
    /// <param name="spx">location of second pixel</param>
    /// <param name="spy">location of second pixel</param>
    /// <param name="pxs">seed pixel to be returned</param>
    /// <param name="pys">seed pixel to be returned</param>
    /// <remarks>
    /// (1) The first and second pixels must be 8-adjacent,
    ///     so |dx| &lt;= 1 and |dy| &lt;= 1 and both dx and dy
    ///     cannot be 0.  There are 8 possible cases.
    /// (2) The seed pixel is OUTSIDE the foreground of the c.c.
    /// (3) These rules are for the situation where the INSIDE
    ///     of the c.c. is on the right as you follow the border:
    ///     cw for an exterior border and ccw for a hole border.
    /// </remarks>
    private static void LocateOutsideSeedPixel(int fpx, int fpy, int spx, int spy,
                                               out int pxs, out int pys)
    {
        int dx = spx - fpx;
        int dy = spy - fpy;

        if (dx * dy == 1) {
            pxs = fpx + dx;
            pys = fpy;
        } else if (dx * dy == -1) {
            pxs = fpx;
            pys = fpy + dy;
        } else if (dx == 0) {
            pxs = fpx + dy;
            pys = fpy + dy;
        } else  /* dy == 0 */ {
            pxs = fpx + dx;
            pys = fpy - dx;
        }
    }

    /*---------------------------------------------------------------------*
     *                            Border conversions                       *
     *---------------------------------------------------------------------*/
    // leptonica: ccbaGenerateGlobalLocs (ccbord.c)
    /// <summary>Generates the global pixel locations from the local ones.</summary>
    /// <remarks>
    /// (1) This uses the pixel locs in the local ptaa, which are all
    ///     relative to each c.c., to find the global pixel locations,
    ///     and stores them in the global ptaa.
    /// </remarks>
    public void GenerateGlobalLocs()
    {
        int ncc = Count;  /* number of c.c. */
        for (int i = 0; i < ncc; i++) {
            CCBord ccb = GetCcb(i);

                /* Get the UL corner in global coords, (xul, yul), of the c.c. */
            ccb.Boxa!.GetBoxGeometry(0, out int xul, out int yul, out _, out _);

                /* Make a new global ptaa, removing any old one */
            Ptaa ptaal = ccb.Local!;
            int nb = ptaal.Count;   /* number of borders */
            Ptaa ptaag = Ptaa.Create(nb);
            ccb.Global = ptaag;  /* save new one */

                /* Iterate through the borders for this c.c. */
            for (int j = 0; j < nb; j++) {
                Pta ptal = ptaal.GetPta(j, L.L_CLONE);
                int n = ptal.Count;   /* number of pixels in border */
                Pta ptag = Pta.Create(n);
                ptaag.AddPta(ptag, L.L_INSERT);
                for (int k = 0; k < n; k++) {
                    ptal.GetIPt(k, out int x, out int y);
                    ptag.AddPt(x + xul, y + yul);
                }
            }
        }
    }

    // leptonica: ccbaGenerateStepChains (ccbord.c)
    /// <summary>Generates the step chains from the local pixel locations.</summary>
    /// <remarks>
    /// (1) This uses the pixel locs in the local ptaa,
    ///     which are all relative to each c.c., to find
    ///     the step directions for successive pixels in
    ///     the chain, and stores them in the step numaa.
    /// (2) To get the step direction, use
    ///         1   2   3
    ///         0   P   4
    ///         7   6   5
    ///     where P is the previous pixel at (px, py).  The step direction
    ///     is the number (from 0 through 7) for each relative location
    ///     of the current pixel at (cx, cy).  It is easily found by
    ///     indexing into a 2-d 3x3 array (dirtab).
    /// </remarks>
    public void GenerateStepChains()
    {
        int[][] dirtab = { new[] { 1, 2, 3 }, new[] { 0, -1, 4 }, new[] { 7, 6, 5 } };

        int ncc = Count;  /* number of c.c. */
        for (int i = 0; i < ncc; i++) {
            CCBord ccb = GetCcb(i);

                /* Make a new step numaa, removing any old one */
            Ptaa ptaal = ccb.Local!;  /* local chain code */
            int nb = ptaal.Count;  /* number of borders */
            Numaa naa = Numaa.Create(nb);   /* step chain code; to be made */
            ccb.Step = naa;  /* save new one */

                /* Iterate through the borders for this c.c. */
            for (int j = 0; j < nb; j++) {
                Pta ptal = ptaal.GetPta(j, L.L_CLONE);
                int n = ptal.Count;   /* number of pixels in border */
                Numa na;
                if (n == 1) {  /* isolated pixel */
                    na = Numa.Create(1);   /* but leave it empty */
                } else {   /* trace out the boundary */
                    na = Numa.Create(n);
                    ptal.GetIPt(0, out int px, out int py);
                    for (int k = 1; k < n; k++) {
                        ptal.GetIPt(k, out int cx, out int cy);
                        int stepdir = dirtab[1 + cy - py][1 + cx - px];
                        na.AddNumber(stepdir);
                        px = cx;
                        py = cy;
                    }
                }
                naa.AddNuma(na, L.L_INSERT);
            }
        }
    }

    // leptonica: ccbaStepChainsToPixCoords (ccbord.c)
    /// <summary>Generates the local or global pixel locations from the step chains.</summary>
    /// <param name="coordtype">CCB_GLOBAL_COORDS or CCB_LOCAL_COORDS</param>
    /// <remarks>
    /// (1) This uses the step chain data in each ccb to determine
    ///     the pixel locations, either global or local,
    ///     and stores them in the appropriate ptaa,
    ///     either global or local.  For the latter, the
    ///     pixel locations are relative to the c.c.
    /// </remarks>
    public void StepChainsToPixCoords(int coordtype)
    {
        int xul, yul;

        if (coordtype != L.CCB_GLOBAL_COORDS && coordtype != L.CCB_LOCAL_COORDS)
            throw Lept.Arg("ccbaStepChainsToPixCoords", "coordtype not valid");

        int ncc = Count;  /* number of c.c. */
        for (int i = 0; i < ncc; i++) {
            CCBord ccb = GetCcb(i);
            Numaa naa = ccb.Step
                        ?? throw Lept.Arg("ccbaStepChainsToPixCoords", "step numaa not found");
            Boxa boxa = ccb.Boxa
                        ?? throw Lept.Arg("ccbaStepChainsToPixCoords", "boxa not found");
            Pta ptas = ccb.Start
                       ?? throw Lept.Arg("ccbaStepChainsToPixCoords", "start pta not found");

                /* For global coords, get the (xul, yul) of the c.c.;
                 * otherwise, use relative coords. */
            if (coordtype == L.CCB_LOCAL_COORDS) {
                xul = 0;
                yul = 0;
            } else {  /* coordtype == CCB_GLOBAL_COORDS */
                    /* Get UL corner in global coords */
                boxa.GetBoxGeometry(0, out xul, out yul, out _, out _);
            }

                /* Make a new ptaa, removing any old one */
            int nb = naa.Count;   /* number of borders */
            Ptaa ptaan = Ptaa.Create(nb);  /* new pix coord ptaa */
            if (coordtype == L.CCB_LOCAL_COORDS)
                ccb.Local = ptaan;  /* save new local chain */
            else   /* coordtype == CCB_GLOBAL_COORDS */
                ccb.Global = ptaan;  /* save new global chain */

                /* Iterate through the borders for this c.c. */
            for (int j = 0; j < nb; j++) {
                Numa na = naa.GetNuma(j, L.L_CLONE);
                int n = na.Count;   /* number of steps in border */
                Pta ptan = Pta.Create(n + 1);
                ptaan.AddPta(ptan, L.L_INSERT);
                ptas.GetIPt(j, out int xstart, out int ystart);
                int x = xul + xstart;
                int y = yul + ystart;
                ptan.AddPt(x, y);
                for (int k = 0; k < n; k++) {
                    int stepdir = na.GetIValue(k);
                    x += xpostab[stepdir];
                    y += ypostab[stepdir];
                    ptan.AddPt(x, y);
                }
            }
        }
    }

    // leptonica: ccbaGenerateSPGlobalLocs (ccbord.c)
    /// <summary>Generates the global locations of the single path.</summary>
    /// <param name="ptsflag">CCB_SAVE_ALL_PTS or CCB_SAVE_TURNING_PTS</param>
    /// <remarks>
    /// (1) This calculates the splocal rep if not yet made.
    /// (2) It uses the local pixel values in splocal, the single
    ///     path pta, which are all relative to each c.c., to find
    ///     the corresponding global pixel locations, and stores
    ///     them in the spglobal pta.
    /// (3) This lists only the turning points: it both makes a
    ///     valid svg file and is typically about half the size
    ///     when all border points are listed.
    /// </remarks>
    public void GenerateSPGlobalLocs(int ptsflag)
    {
            /* Make sure we have a local single path representation */
        CCBord ccb0 = GetCcb(0);
        if (ccb0.SpLocal == null)
            GenerateSinglePath();

        int ncc = Count;  /* number of c.c. */
        for (int i = 0; i < ncc; i++) {
            CCBord ccb = GetCcb(i);

                /* Get the UL corner in global coords, (xul, yul), of the c.c. */
            ccb.Boxa!.GetBoxGeometry(0, out int xul, out int yul, out _, out _);

                /* Make a new spglobal pta, removing any old one */
            Pta ptal = ccb.SpLocal!;
            int npt = ptal.Count;   /* number of points */
            Pta ptag = Pta.Create(npt);
            ccb.SpGlobal = ptag;  /* save new one */

                /* Convert local to global */
            int x, y;
            if (ptsflag == L.CCB_SAVE_ALL_PTS) {
                for (int j = 0; j < npt; j++) {
                    ptal.GetIPt(j, out x, out y);
                    ptag.AddPt(x + xul, y + yul);
                }
            } else {   /* ptsflag = CCB_SAVE_TURNING_PTS */
                ptal.GetIPt(0, out int xp, out int yp);   /* get the 1st pt */
                ptag.AddPt(xp + xul, yp + yul);   /* save the 1st pt */
                if (npt == 2) {  /* get and save the 2nd pt  */
                    ptal.GetIPt(1, out x, out y);
                    ptag.AddPt(x + xul, y + yul);
                } else if (npt > 2) {
                    ptal.GetIPt(1, out x, out y);
                    int delxp = x - xp;   /* prev point and increments */
                    int delyp = y - yp;
                    xp = x;
                    yp = y;
                    for (int j = 2; j < npt; j++) {
                        ptal.GetIPt(j, out x, out y);
                        int delx = x - xp;
                        int dely = y - yp;
                        if (delx != delxp || dely != delyp)
                            ptag.AddPt(xp + xul, yp + yul);
                        xp = x;
                        yp = y;
                        delxp = delx;
                        delyp = dely;
                    }
                    ptag.AddPt(xp + xul, yp + yul);
                }
            }
        }
    }

    /*---------------------------------------------------------------------*
     *                       Conversion to single path                     *
     *---------------------------------------------------------------------*/
    // leptonica: ccbaGenerateSinglePath (ccbord.c)
    /// <summary>Generates a single border in local pixel coordinates for each c.c.</summary>
    /// <remarks>
    /// (1) Generates a single border in local pixel coordinates.
    ///     For each c.c., if there is just an outer border, copy it.
    ///     If there are also hole borders, for each hole border,
    ///     determine the smallest horizontal or vertical
    ///     distance from the border to the outside of the c.c.,
    ///     and find a path through the c.c. for this cut.
    ///     We do this in a way that guarantees a pixel from the
    ///     hole border is the starting point of the path, and
    ///     we must verify that the path intersects the outer
    ///     border (if it intersects it, then it ends on it).
    ///     One can imagine pathological cases, but they may not
    ///     occur in images of text characters and un-textured
    ///     line graphics.
    /// (2) Once it is verified that the path through the c.c.
    ///     intersects both the hole and outer borders, we
    ///     generate the full single path for all borders in the
    ///     c.c.  Starting at the start point on the outer
    ///     border, when we hit a line on a cut, we take
    ///     the cut, do the hole border, and return on the cut
    ///     to the outer border.  We compose a pta of the
    ///     outer border pts that are on cut paths, and for
    ///     every point on the outer border (as we go around),
    ///     we check against this pta.  When we find a matching
    ///     point in the pta, we do its cut path and hole border.
    ///     The single path is saved in the ccb.
    /// </remarks>
    public void GenerateSinglePath()
    {
        int x, y;

        int ncc = Count;   /* number of c.c. */
        int lostholes = 0;
        for (int i = 0; i < ncc; i++) {
            CCBord ccb = GetCcb(i);
            Ptaa? ptaa = ccb.Local;
            if (ptaa == null) {
                Log.Warning("ccbaGenerateSinglePath", "local pixel loc array not found");
                continue;
            }
            int nb = ptaa.Count;   /* number of borders in the c.c.  */

                /* Prepare the output pta */
            Pta ptas = Pta.Create(0);   /* output result: new single path for c.c. */
            ccb.SpLocal = ptas;

                /* If no holes, just concat the outer border */
            Pta pta = ptaa.GetPta(0, L.L_CLONE);
            if (nb == 1 || nb > NMaxHoles + 1) {
                ptas.Join(pta, 0, -1);
                continue;
            }

                /* Find the (nb - 1) cut paths that connect holes
                 * with outer border */
            Boxa boxa = ccb.Boxa!;
            Ptaa ptaap = Ptaa.Create(nb - 1);   /* ptaa for all paths between borders */
            Pta ptaf = Pta.Create(nb - 1);   /* points on the hole borders that intersect with cuts */
            Pta ptal = Pta.Create(nb - 1);   /* points on outer border that intersect with cuts */
            for (int j = 1; j < nb; j++) {
                Box boxinner = boxa.GetBox(j, L.L_CLONE);

                    /* Find a short path and store it */
                Pta ptac = GetCutPathForHole(ccb.Pix!, pta, boxinner, out _, out int len);
                if (len == 0) {  /* lost the hole */
                    lostholes++;
                }
                ptaap.AddPta(ptac, L.L_INSERT);

                    /* Store the first and last points in the cut path,
                     * which must be on a hole border and the outer
                     * border, respectively */
                int ncut = ptac.Count;
                if (ncut == 0) {   /* missed hole; neg coords won't match */
                    ptaf.AddPt(-1, -1);
                    ptal.AddPt(-1, -1);
                } else {
                    ptac.GetIPt(0, out x, out y);
                    ptaf.AddPt(x, y);
                    ptac.GetIPt(ncut - 1, out x, out y);
                    ptal.AddPt(x, y);
                }
            }

                /* Make a single path for the c.c. using these connections */
            int npt = pta.Count;  /* outer border pts */
            for (int k = 0; k < npt; k++) {
                pta.GetIPt(k, out x, out y);
                if (k == 0) {   /* if there is a cut at the first point,
                                 * we can wait until the end to take it */
                    ptas.AddPt(x, y);
                    continue;
                }
                int state = L.L_NOT_FOUND;
                for (int j = 0; j < nb - 1; j++) {  /* iterate over cut end pts */
                    ptal.GetIPt(j, out int xl, out int yl);  /* cut point on outer border */
                    if (x == xl && y == yl) {  /* take this cut to the hole */
                        state = L.L_FOUND;
                        Pta ptap = ptaap.GetPta(j, L.L_CLONE);
                        Pta ptarp = ptap.Reverse(1);
                            /* Cut point on hole border: */
                        ptaf.GetIPt(j, out int xf, out int yf);
                            /* Hole border: */
                        Pta ptah = ptaa.GetPta(j + 1, L.L_CLONE);
                        Pta ptahc = ptah.CyclicPerm(xf, yf);
                        ptas.Join(ptarp, 0, -1);
                        ptas.Join(ptahc, 0, -1);
                        ptas.Join(ptap, 0, -1);
                        break;
                    }
                }
                if (state == L.L_NOT_FOUND)
                    ptas.AddPt(x, y);
            }
        }

        if (lostholes > 0)
            Log.Info("ccbaGenerateSinglePath", $"***** {lostholes} lost holes *****");
    }

    // leptonica: getCutPathForHole (ccbord.c)
    /// <param name="pix">1 bpp, of c.c.</param>
    /// <param name="pta">of outer border</param>
    /// <param name="boxinner">bounding box of hole path</param>
    /// <param name="pdir">direction (0-3), returned; only needed for debug</param>
    /// <param name="plen">length of path, returned</param>
    /// <returns>pta of pts on cut path from the hole border
    /// to the outer border, including end points on both borders</returns>
    /// <remarks>
    /// (1) If we don't find a path, we return a pta with no pts
    ///     in it and len = 0.
    /// (2) The goal is to get a reasonably short path between the
    ///     inner and outer borders, that goes entirely within the fg of
    ///     the pix.  This function is cheap-and-dirty, may fail for some
    ///     holes in complex topologies such as those you might find in a
    ///     moderately dark scanned halftone.  If it fails to find a
    ///     path to any particular hole, the hole will not be rendered.
    ///     Nevertheless, the image can be perfectly reconstructed
    ///     from the boundary representation.
    /// </remarks>
    private static Pta GetCutPathForHole(Pix pix, Pta pta, Box boxinner, out int pdir, out int plen)
    {
        int x, y, nc, xl, yl;
        uint val;

        pix.GetDimensions(out int w, out int h, out _);
        Pta ptac = Pta.Create(4);
        int xmid = boxinner.X + boxinner.Width / 2;
        int ymid = boxinner.Y + boxinner.Height / 2;
        pdir = 0;

            /* try top first */
        for (y = ymid; y >= 0; y--) {
            pix.GetPixel(xmid, y, out val);
            if (val == 1) {
                ptac.AddPt(xmid, y);
                break;
            }
        }
        for (y = y - 1; y >= 0; y--) {
            pix.GetPixel(xmid, y, out val);
            if (val == 1)
                ptac.AddPt(xmid, y);
            else
                break;
        }
        nc = ptac.Count;
        LastPt(ptac, nc, out xl, out yl);
        if (pta.ContainsPt(xl, yl)) {
            pdir = 1;
            plen = nc;
            return ptac;
        }

            /* Next try bottom */
        ptac.Empty();
        for (y = ymid; y < h; y++) {
            pix.GetPixel(xmid, y, out val);
            if (val == 1) {
                ptac.AddPt(xmid, y);
                break;
            }
        }
        for (y = y + 1; y < h; y++) {
            pix.GetPixel(xmid, y, out val);
            if (val == 1)
                ptac.AddPt(xmid, y);
            else
                break;
        }
        nc = ptac.Count;
        LastPt(ptac, nc, out xl, out yl);
        if (pta.ContainsPt(xl, yl)) {
            pdir = 3;
            plen = nc;
            return ptac;
        }

            /* Next try left */
        ptac.Empty();
        for (x = xmid; x >= 0; x--) {
            pix.GetPixel(x, ymid, out val);
            if (val == 1) {
                ptac.AddPt(x, ymid);
                break;
            }
        }
        for (x = x - 1; x >= 0; x--) {
            pix.GetPixel(x, ymid, out val);
            if (val == 1)
                ptac.AddPt(x, ymid);
            else
                break;
        }
        nc = ptac.Count;
        LastPt(ptac, nc, out xl, out yl);
        if (pta.ContainsPt(xl, yl)) {
            pdir = 0;
            plen = nc;
            return ptac;
        }

            /* Finally try right */
        ptac.Empty();
        for (x = xmid; x < w; x++) {
            pix.GetPixel(x, ymid, out val);
            if (val == 1) {
                ptac.AddPt(x, ymid);
                break;
            }
        }
        for (x = x + 1; x < w; x++) {
            pix.GetPixel(x, ymid, out val);
            if (val == 1)
                ptac.AddPt(x, ymid);
            else
                break;
        }
        nc = ptac.Count;
        LastPt(ptac, nc, out xl, out yl);
        if (pta.ContainsPt(xl, yl)) {
            pdir = 2;
            plen = nc;
            return ptac;
        }

            /* Sometimes, there is nothing. */
        ptac.Empty();
        plen = 0;
        return ptac;
    }

    /* C: ptaGetIPt(ptac, nc - 1, &xl, &yl) fails for an empty pta and leaves (0, 0) */
    private static void LastPt(Pta ptac, int nc, out int xl, out int yl)
    {
        if (nc > 0) {
            ptac.GetIPt(nc - 1, out xl, out yl);
        } else {
            xl = 0;
            yl = 0;
        }
    }

    /*---------------------------------------------------------------------*
     *                            Border rendering                         *
     *---------------------------------------------------------------------*/
    // leptonica: ccbaDisplayBorder (ccbord.c)
    /// <returns>pix of border pixels</returns>
    /// <remarks>
    /// (1) Uses global ptaa, which gives each border pixel in
    ///     global coordinates, and must be computed in advance
    ///     by calling ccbaGenerateGlobalLocs().
    /// </remarks>
    public Pix DisplayBorder()
    {
        Pix pixd = Pix.Create(Width, Height, 1);
        int ncc = Count;   /* number of c.c. */
        for (int i = 0; i < ncc; i++) {
            CCBord ccb = GetCcb(i);
            Ptaa? ptaa = ccb.Global;
            if (ptaa == null) {
                Log.Warning("ccbaDisplayBorder", "global pixel loc array not found");
                continue;
            }
            int nb = ptaa.Count;   /* number of borders in the c.c.  */
            for (int j = 0; j < nb; j++) {
                Pta pta = ptaa.GetPta(j, L.L_CLONE);
                int n = pta.Count;   /* number of pixels in the border */
                for (int k = 0; k < n; k++) {
                    pta.GetIPt(k, out int x, out int y);
                    pixd.SetPixel(x, y, 1);
                }
            }
        }

        return pixd;
    }

    // leptonica: ccbaDisplaySPBorder (ccbord.c)
    /// <returns>pix of border pixels</returns>
    /// <remarks>
    /// (1) Uses spglobal pta, which gives each border pixel in
    ///     global coordinates, one path per c.c., and must
    ///     be computed in advance by calling ccbaGenerateSPGlobalLocs().
    /// </remarks>
    public Pix DisplaySPBorder()
    {
        Pix pixd = Pix.Create(Width, Height, 1);
        int ncc = Count;   /* number of c.c. */
        for (int i = 0; i < ncc; i++) {
            CCBord ccb = GetCcb(i);
            Pta? ptag = ccb.SpGlobal;
            if (ptag == null) {
                Log.Warning("ccbaDisplaySPBorder", "spglobal pixel loc array not found");
                continue;
            }
            int npt = ptag.Count;   /* number of pixels on path */
            for (int j = 0; j < npt; j++) {
                ptag.GetIPt(j, out int x, out int y);
                pixd.SetPixel(x, y, 1);
            }
        }

        return pixd;
    }

    // leptonica: ccbaDisplayImage1 (ccbord.c)
    /// <returns>pix of image</returns>
    /// <remarks>
    /// (1) Uses local ptaa, which gives each border pixel in
    ///     local coordinates, so the actual pixel positions must
    ///     be computed using all offsets.
    /// (2) For the holes, use coordinates relative to the c.c.
    /// (3) This is slower than Method 2.
    /// (4) This uses topological properties (Method 1) to do scan
    ///     conversion to raster
    ///
    /// This algorithm deserves some commentary.
    ///
    /// I first tried the following:
    ///   ~ outer borders: 4-fill from outside, stopping at the
    ///        border, using pixFillClosedBorders()
    ///   ~ inner borders: 4-fill from outside, stopping again
    ///        at the border, XOR with the border, and invert
    ///        to get the hole.  This did not work, because if
    ///        you have a hole border where a pixel inside the
    ///        border is enclosed by other border pixels (an 'o'
    ///        pixel surrounded by 'x' pixels on an elbow), a
    ///        4-fill from the outside will not fill it; XORing
    ///        with the border leaves it OFF; inverting then gives
    ///        a single bad ON pixel that is not actually part of the hole.
    ///
    /// So what you must do instead is 4-fill the holes from inside.
    /// You can do this from a seedfill, using a pix with the hole
    /// border as the filling mask.  But you need to start with a
    /// pixel inside the hole.  How is this determined?  The best
    /// way is from the contour.  We have a right-hand shoulder
    /// rule for inside (i.e., the filled region).   Take the
    /// first 2 pixels of the hole border, and compute dx and dy
    /// (second coord minus first coord:  dx = sx - fx, dy = sy - fy).
    /// There are 8 possibilities, depending on the values of dx and
    /// dy (which can each be -1, 0, and +1, but not both 0).
    /// These 8 cases can be broken into 4; see the simple algorithm below.
    /// Once you have an interior seed pixel, you fill from the seed,
    /// clipping with the hole border pix by filling into its invert.
    ///
    /// You then successively XOR these interior filled components, in any order.
    /// </remarks>
    public Pix DisplayImage1()
    {
        int xul = 0, yul = 0, xoff, yoff, w, h;
        int fpx = 0, fpy = 0, spx = 0, spy = 0, xs, ys;

        Pix pixd = Pix.Create(Width, Height, 1);
        int ncc = Count;
        for (int i = 0; i < ncc; i++) {
            CCBord ccb = GetCcb(i);
            Boxa boxa = ccb.Boxa
                        ?? throw Lept.Arg("ccbaDisplayImage1", "boxa not found");

                /* Render border in pixt */
            Ptaa? ptaa = ccb.Local;
            if (ptaa == null) {
                Log.Warning("ccbaDisplayImage1", "local chain array not found");
                continue;
            }

            int nb = ptaa.Count;   /* number of borders in the c.c.  */
            for (int j = 0; j < nb; j++) {
                Box box = boxa.GetBox(j, L.L_CLONE);
                if (j == 0) {
                    box.GetGeometry(out xul, out yul, out w, out h);
                    xoff = yoff = 0;
                } else {
                    box.GetGeometry(out xoff, out yoff, out w, out h);
                }

                    /* Render the border in a minimum-sized pix;
                     * subtract xoff and yoff because the pixel
                     * location is stored relative to the c.c., but
                     * we need it relative to just the hole border. */
                Pix pixt = Pix.Create(w, h, 1);
                Pta pta = ptaa.GetPta(j, L.L_CLONE);
                int n = pta.Count;   /* number of pixels in the border */
                for (int k = 0; k < n; k++) {
                    pta.GetIPt(k, out int x, out int y);
                    pixt.SetPixel(x - xoff, y - yoff, 1);
                    if (j > 0) {   /* need this for finding hole border pixel */
                        if (k == 0) {
                            fpx = x - xoff;
                            fpy = y - yoff;
                        }
                        if (k == 1) {
                            spx = x - xoff;
                            spy = y - yoff;
                        }
                    }
                }

                    /* Get the filled component */
                Pix pixh;
                if (j == 0) {  /* if outer border, fill from outer boundary */
                    pixh = pixt.FillClosedBorders(4);
                } else {   /* fill the hole from inside */
                        /* get the location of a seed pixel in the hole */
                    LocateOutsideSeedPixel(fpx, fpy, spx, spy, out xs, out ys);

                        /* Put seed in hole and fill interior of hole,
                         * using pixt as clipping mask */
                    pixh = Pix.CreateTemplate(pixt);
                    pixh.SetPixel(xs, ys, 1);  /* put seed pixel in hole */
                    Pix.Invert(pixt, pixt);  /* to make filling mask */
                    Pix.SeedfillBinary(pixh, pixh, pixt, 4);  /* 4-fill hole */
                }

                    /* XOR into the dest */
                pixd.Rasterop(xul + xoff, yul + yoff, w, h, L.PIX_XOR, pixh, 0, 0);
            }
        }

        return pixd;
    }

    // leptonica: ccbaDisplayImage2 (ccbord.c)
    /// <returns>pix of image</returns>
    /// <remarks>
    /// (1) Uses local chain ptaa, which gives each border pixel in
    ///     local coordinates, so the actual pixel positions must
    ///     be computed using all offsets.
    /// (2) Treats exterior and hole borders on equivalent
    ///     footing, and does all calculations on a pix
    ///     that spans the c.c. with a 1 pixel added boundary.
    /// (3) This uses topological properties (Method 2) to do scan
    ///     conversion to raster
    /// (4) The algorithm is described at the top of this file (Method 2).
    ///     It is preferred to Method 1 because it is between 1.2x and 2x
    ///     faster than Method 1.
    /// </remarks>
    public Pix DisplayImage2()
    {
        int fpx = 0, fpy = 0, spx = 0, spy = 0, xs, ys;

        Pix pixd = Pix.Create(Width, Height, 1);
        int ncc = Count;
        for (int i = 0; i < ncc; i++) {
                /* Generate clipping mask from border pixels and seed image
                 * from one seed for each closed border. */
            CCBord ccb = GetCcb(i);
            Boxa boxa = ccb.Boxa
                        ?? throw Lept.Arg("ccbaDisplayImage2", "boxa not found");
            boxa.GetBoxGeometry(0, out int xul, out int yul, out int w, out int h);
            Pix pixc = Pix.Create(w + 2, h + 2, 1);
            Pix pixs = Pix.CreateTemplate(pixc);

            Ptaa? ptaa = ccb.Local;
            if (ptaa == null) {
                Log.Warning("ccbaDisplayImage2", "local chain array not found");
                continue;
            }
            int nb = ptaa.Count;   /* number of borders in the c.c.  */
            for (int j = 0; j < nb; j++) {
                Pta pta = ptaa.GetPta(j, L.L_CLONE);
                int n = pta.Count;   /* number of pixels in the border */

                    /* Render border pixels in pixc */
                for (int k = 0; k < n; k++) {
                    pta.GetIPt(k, out int x, out int y);
                    pixc.SetPixel(x + 1, y + 1, 1);
                    if (k == 0) {
                        fpx = x + 1;
                        fpy = y + 1;
                    } else if (k == 1) {
                        spx = x + 1;
                        spy = y + 1;
                    }
                }

                    /* Get and set seed pixel for this border in pixs */
                if (n > 1)
                    LocateOutsideSeedPixel(fpx, fpy, spx, spy, out xs, out ys);
                else  /* isolated c.c. */
                    xs = ys = 0;
                pixs.SetPixel(xs, ys, 1);
            }

                /* Fill from seeds in pixs, using pixc as the clipping mask,
                 * to reconstruct the c.c. */
            Pix.Invert(pixc, pixc);  /* to convert clipping -> filling mask */
            Pix.SeedfillBinary(pixs, pixs, pixc, 4);  /* 4-fill */
            Pix.Invert(pixs, pixs);  /* to make the c.c. */

                /* XOR into the dest */
            pixd.Rasterop(xul, yul, w, h, L.PIX_XOR, pixs, 1, 1);
        }

        return pixd;
    }

    /*---------------------------------------------------------------------*
     *                            Serialize for I/O                        *
     *---------------------------------------------------------------------*/
    /// <summary>Writes the array of component borders to a file in serialized form.</summary>
    /// <param name="filename">The path of the file to write.</param>
    /// <exception cref="ArgumentException">The file cannot be opened for writing.</exception>
    // leptonica: ccbaWrite (ccbord.c)
    public void Write(string filename)
    {
        using FileStream fp = Utils.FopenWriteStream(filename, "wb+");
        WriteStream(fp);
    }

    // leptonica: ccbaWriteStream (ccbord.c)
    /// <param name="fp">file stream</param>
    /// <remarks>
    /// Format:
    /// <code>
    ///          ccba: %7d cc\n num. c.c.) (ascii)   (18B
    ///          pix width 4B
    ///          pix height 4B
    ///          [for i = 1, ncc]
    ///              ulx  4B
    ///              uly  4B
    ///              w    4B       -- not req'd for reconstruction
    ///              h    4B       -- not req'd for reconstruction
    ///              number of borders 4B
    ///              [for j = 1, nb]
    ///                  startx  4B
    ///                  starty  4B
    ///                  [for k = 1, nb]
    ///                       2 steps 1B
    ///                  end in z8 or 88  1B
    /// </code>
    /// </remarks>
    public void WriteStream(Stream fp)
    {
        byte bval = 0;

        L_BBuffer bbuf = L_BBuffer.Create(ReadOnlySpan<byte>.Empty, 1000);

        int ncc = Count;
        string strbuf = $"ccba: {ncc,7} cc\n";   /* 17 bytes; C writes 18, including the terminating NUL */
        bbuf.Read(Encoding.Latin1.GetBytes(strbuf + "\0"), 18);
        uint w = (uint)Pix!.Width;
        uint h = (uint)Pix.Height;
        bbuf.Read(BitConverter.GetBytes(w), 4);  /* width */
        bbuf.Read(BitConverter.GetBytes(h), 4);  /* height */
        for (int i = 0; i < ncc; i++) {
            CCBord ccb = GetCcb(i);
            ccb.Boxa!.GetBoxGeometry(0, out int bx, out int by, out int bw, out int bh);
            bbuf.Read(BitConverter.GetBytes(bx), 4);  /* ulx of c.c. */
            bbuf.Read(BitConverter.GetBytes(by), 4);  /* uly of c.c. */
            bbuf.Read(BitConverter.GetBytes(bw), 4);  /* w of c.c. */
            bbuf.Read(BitConverter.GetBytes(bh), 4);  /* h of c.c. */
            Numaa? naa = ccb.Step;
            if (naa == null) {
                GenerateStepChains();
                naa = ccb.Step;
            }
            int nb = naa!.Count;
            bbuf.Read(BitConverter.GetBytes(nb), 4);  /* number of borders in c.c. */
            Pta pta = ccb.Start!;
            for (int j = 0; j < nb; j++) {
                pta.GetIPt(j, out int startx, out int starty);
                bbuf.Read(BitConverter.GetBytes(startx), 4); /* starting x in border */
                bbuf.Read(BitConverter.GetBytes(starty), 4); /* starting y in border */
                Numa na = naa.GetNuma(j, L.L_CLONE);
                int n = na.Count;
                for (int k = 0; k < n; k++) {
                    int val = na.GetIValue(k);
                    if (k % 2 == 0)
                        bval = (byte)((byte)val << 4);
                    else
                        bval |= (byte)val;
                    if (k % 2 == 1)
                        bbuf.Read(new[] { bval }, 1); /* 2 border steps */
                }
                if (n % 2 == 1) {
                    bval |= 0x8;
                    bbuf.Read(new[] { bval }, 1); /* end with 0xz8,   */
                                                  /* where z = {0..7} */
                } else {  /* n % 2 == 0 */
                    bval = 0x88;
                    bbuf.Read(new[] { bval }, 1);   /* end with 0x88 */
                }
            }
        }

        byte[] datain = bbuf.DestroyAndSaveData();
        byte[] dataout = ZlibMem.ZlibCompress(datain);
        fp.Write(dataout, 0, dataout.Length);
    }

    /// <summary>Reads a serialized array of component borders from a file.</summary>
    /// <param name="filename">The path of the file to read.</param>
    /// <returns>The component borders read from the file.</returns>
    /// <exception cref="ArgumentException">The file cannot be opened or its contents are invalid.</exception>
    // leptonica: ccbaRead (ccbord.c)
    public static CCBorda Read(string filename)
    {
        using FileStream fp = Utils.FopenReadStream(filename);
        return ReadStream(fp);
    }

    // leptonica: ccbaReadStream (ccbord.c)
    /// <param name="fp">file stream</param>
    /// <remarks>
    /// Format:  ccba: %7d cc\n num. c.c.) (ascii)   (17B
    ///          pix width 4B
    ///          pix height 4B
    ///          [for i = 1, ncc]
    ///              ulx  4B
    ///              uly  4B
    ///              w    4B       -- not req'd for reconstruction
    ///              h    4B       -- not req'd for reconstruction
    ///              number of borders 4B
    ///              [for j = 1, nb]
    ///                  startx  4B
    ///                  starty  4B
    ///                  [for k = 1, nb]
    ///                       2 steps 1B
    ///                  end in z8 or 88  1B
    /// </remarks>
    public static CCBorda ReadStream(Stream fp)
    {
        byte[] datain = Utils.BinaryReadStream(fp);
        byte[] dataout = ZlibMem.ZlibUncompress(datain);

        int offset = 18;
        string strbuf = Encoding.Latin1.GetString(dataout, 0, 17);
        if (!strbuf.StartsWith("ccba:", StringComparison.Ordinal))
            throw Lept.Arg("ccbaReadStream", "file not type ccba");
        int ncc = int.Parse(strbuf.Substring(6, 7).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        CCBorda ccba = Create(null, ncc);

        uint width = BitConverter.ToUInt32(dataout, offset);
        offset += 4;
        uint height = BitConverter.ToUInt32(dataout, offset);
        offset += 4;
        ccba.Width = (int)width;
        ccba.Height = (int)height;

        for (int i = 0; i < ncc; i++) {  /* should be ncc */
            CCBord ccb = CCBord.Create(null);
            ccba.AddCcb(ccb);

            uint xoff = BitConverter.ToUInt32(dataout, offset);
            offset += 4;
            uint yoff = BitConverter.ToUInt32(dataout, offset);
            offset += 4;
            uint w = BitConverter.ToUInt32(dataout, offset);
            offset += 4;
            uint h = BitConverter.ToUInt32(dataout, offset);
            offset += 4;
            Box box = Box.Create((int)xoff, (int)yoff, (int)w, (int)h);
            ccb.Boxa!.AddBox(box, L.L_INSERT);

            int nb = BitConverter.ToInt32(dataout, offset);
            offset += 4;
            Numaa step = Numaa.Create(nb);
            ccb.Step = step;

            for (int j = 0; j < nb; j++) {  /* should be nb */
                int startx = BitConverter.ToInt32(dataout, offset);
                offset += 4;
                int starty = BitConverter.ToInt32(dataout, offset);
                offset += 4;
                ccb.Start!.AddPt(startx, starty);
                Numa na = Numa.Create(0);
                step.AddNuma(na, L.L_INSERT);

                while (true) {
                    byte bval = dataout[offset];
                    offset++;
                    int nib1 = bval >> 4;
                    int nib2 = bval & 0xf;
                    if (nib1 != 8)
                        na.AddNumber(nib1);
                    else
                        break;
                    if (nib2 != 8)
                        na.AddNumber(nib2);
                    else
                        break;
                }
            }
        }
        return ccba;
    }

    /*---------------------------------------------------------------------*
     *                                SVG Output                           *
     *---------------------------------------------------------------------*/
    /// <summary>Writes the component borders to a file as a Scalable Vector Graphics (SVG) document.</summary>
    /// <param name="filename">The path of the file to write.</param>
    /// <exception cref="ArgumentException">The file cannot be opened for writing.</exception>
    // leptonica: ccbaWriteSVG (ccbord.c)
    public void WriteSVG(string filename)
    {
        string svgstr = WriteSVGString();
        Utils.BinaryWrite(filename, "w", Encoding.Latin1.GetBytes(svgstr));
    }

    // leptonica: ccbaWriteSVGString (ccbord.c)
    /// <returns>string in svg-formatted, that can be written to file</returns>
    public string WriteSVGString()
    {
        const string line0 = "<?xml version=\"1.0\" encoding=\"iso-8859-1\"?>";
        const string line1 = "<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 20000303 Stylable//EN\" \"http://www.w3.org/TR/2000/03/WD-SVG-20000303/DTD/svg-20000303-stylable.dtd\">";
        const string line2 = "<svg>";
        const string line3 = "<polygon style=\"stroke-width:1;stroke:black;\" points=\"";
        const string line4 = "\" />";
        const string line5 = "</svg>";
        const string space = " ";

        Sarray sa = Sarray.Create(0);
        sa.AddString(line0, L.L_COPY);
        sa.AddString(line1, L.L_COPY);
        sa.AddString(line2, L.L_COPY);
        int ncc = Count;
        for (int i = 0; i < ncc; i++) {
            CCBord ccb = GetCcb(i);
            Pta pta = ccb.SpGlobal
                      ?? throw Lept.Arg("ccbaWriteSVGString", "spglobal not made");
            sa.AddString(line3, L.L_COPY);
            int npt = pta.Count;
            for (int j = 0; j < npt; j++) {
                pta.GetIPt(j, out int x, out int y);
                sa.AddString($"{x},{y}", L.L_COPY);
            }
            sa.AddString(line4, L.L_COPY);
        }
        sa.AddString(line5, L.L_COPY);
        sa.AddString(space, L.L_COPY);

        return sa.ToString(1);
    }
}

public sealed partial class Pix
{
    // leptonica: pixGetAllCCBorders (ccbord.c)
    /// <summary>Gets the borders of all 8-connected components.</summary>
    /// <returns>ccborda</returns>
    public CCBorda GetAllCCBorders() => CCBorda.GetAllCCBorders(this);

    // leptonica: pixGetOuterBordersPtaa (ccbord.c)
    /// <returns>ptaa of outer borders, in global coords</returns>
    public Ptaa GetOuterBordersPtaa() => CCBorda.GetOuterBordersPtaa(this);

    // leptonica: pixGetOuterBorder (ccbord.c)
    /// <summary>Finds the outer border of a component and adds it to the ccb.</summary>
    /// <param name="ccb">unfilled</param>
    /// <param name="pixs">for the component at hand</param>
    /// <param name="box">for the component, in global coords</param>
    public static void GetOuterBorder(CCBord ccb, Pix pixs, Box box)
        => CCBorda.GetOuterBorderImpl(ccb, pixs, box);
}
