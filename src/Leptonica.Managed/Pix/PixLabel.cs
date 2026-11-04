using System;
using System.Collections.Generic;

namespace Leptonica;

/*
 *  pixlabel.c: Labelling of pixels.
 *
 *      Label pixels by an index for connected component membership
 *          Pix.ConnCompTransform (pixConnCompTransform)
 *
 *      Label pixels by the area of their connected component
 *          Pix.ConnCompAreaTransform (pixConnCompAreaTransform)
 *
 *      Label pixels to allow incremental computation of connected components
 *          Pix.ConnCompIncrInit (pixConnCompIncrInit)
 *          Pix.ConnCompIncrAdd (pixConnCompIncrAdd)
 *          Pix.GetSortedNeighborValues (pixGetSortedNeighborValues)
 *
 *      Label pixels with spatially-dependent color coding
 *          Pix.LocToColorTransform (pixLocToColorTransform)
 *
 *  Pixels get labelled in various ways throughout the leptonica library,
 *  but most of the labelling is implicit, where the new value isn't
 *  even considered to be a label -- it is just a transformed pixel value
 *  that may be transformed again by another operation.  Quantization
 *  by thresholding, and dilation by a structuring element, are examples
 *  of these typical image processing operations.
 *
 *  However, there are some explicit labelling procedures that are useful
 *  as end-points of analysis, where it typically would not make sense
 *  to do further image processing on the result.  Assigning false color
 *  based on pixel properties is an example of such labelling operations.
 *  Such operations typically have 1 bpp input images, and result
 *  in grayscale or color images.
 *
 *  The procedures in this file are concerned with such explicit labelling.
 *  Some of these labelling procedures are also in other places in leptonica:
 *
 *    runlength.c:
 *       This file has two labelling transforms based on runlengths:
 *       pixStrokeWidthTransform() and pixvRunlengthTransform().
 *       The pixels are labelled based on the width of the "stroke" to
 *       which they belong, or on the length of the horizontal or
 *       vertical run in which they are a member.  Runlengths can easily
 *       be filtered using a threshold.
 *
 *    pixafunc2.c:
 *       This file has an operation, pixaDisplayRandomCmap(), that
 *       randomly labels pix in a pixa (that are typically found using
 *       pixConnComp) with up to 256 values, and assigns each value to
 *       a random colormap color.
 *
 *    seedfill.c:
 *       This file has pixDistanceFunction(), that labels each pixel with
 *       its distance from either the foreground or the background.
 */
public sealed partial class Pix
{
    /*-----------------------------------------------------------------------*
     *      Label pixels by an index for connected component membership      *
     *-----------------------------------------------------------------------*/
    // leptonica: pixConnCompTransform (pixlabel.c)
    /// <summary>Labels the pixels of each connected component by the index of the component.</summary>
    /// <param name="connect">connectivity: 4 or 8</param>
    /// <param name="depth">of pixd: 8 or 16 bpp; use 0 for auto determination</param>
    /// <returns>pixd 8, 16 or 32 bpp</returns>
    /// <remarks>
    /// (1) pixd is 8, 16 or 32 bpp, and the pixel values label the
    ///     fg component, starting with 1.  Pixels in the bg are labelled 0.
    /// (2) If %depth = 0, the depth of pixd is 8 if the number of c.c.
    ///     is less than 254, 16 if the number of c.c is less than 0xfffe,
    ///     and 32 otherwise.
    /// (3) If %depth = 8, the assigned label for the n-th component is
    ///     1 + n % 254.  We use mod 254 because 0 is uniquely assigned
    ///     to black: e.g., see pixcmapCreateRandom().  Likewise,
    ///     if %depth = 16, the assigned label uses mod(2^16 - 2), and
    ///     if %depth = 32, no mod is taken.
    /// </remarks>
    public Pix ConnCompTransform(int connect, int depth)
    {
        const string func = "pixConnCompTransform";
        Pix pixs = this;
        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (connect != 4 && connect != 8)
            throw Lept.Arg(func, "connectivity must be 4 or 8");
        if (depth != 0 && depth != 8 && depth != 16 && depth != 32)
            throw Lept.Arg(func, "depth must be 0, 8, 16 or 32");

        pixs.ConnComp(out Pixa pixa, connect);
        int n = pixa.Count;
        pixs.GetDimensions(out int w, out int h, out _);
        if (depth == 0) {
            if (n < 254)
                depth = 8;
            else if (n < 0xfffe)
                depth = 16;
            else
                depth = 32;
        }
        Pix pixd = Create(w, h, depth);
        pixd.Spp = 1;
        if (n == 0)  /* no fg */
            return pixd;

           /* Label each component and blit it in */
        for (int i = 0; i < n; i++) {
            pixa.GetBoxGeometry(i, out int xb, out int yb, out int wb, out int hb);
            Pix pix1 = pixa.GetPix(i, L.L_CLONE);
            Pix pix2;
            int index;
            if (depth == 8) {
                index = 1 + (i % 254);
                pix2 = Convert1To8(null, pix1, 0, (byte)index);
            } else if (depth == 16) {
                index = 1 + (i % 0xfffe);
                pix2 = Convert1To16(null, pix1, 0, (ushort)index);
            } else {  /* depth == 32 */
                index = 1 + i;
                pix2 = Convert1To32(null, pix1, 0, (uint)index);
            }
            pixd.Rasterop(xb, yb, wb, hb, L.PIX_PAINT, pix2, 0, 0);
        }

        return pixd;
    }

    /*-----------------------------------------------------------------------*
     *         Label pixels by the area of their connected component         *
     *-----------------------------------------------------------------------*/
    // leptonica: pixConnCompAreaTransform (pixlabel.c)
    /// <summary>Labels the pixels of each connected component by the area of the component.</summary>
    /// <param name="connect">connectivity: 4 or 8</param>
    /// <returns>pixd 32 bpp, 1 spp</returns>
    /// <remarks>
    /// (1) The pixel values in pixd label the area of the fg component
    ///     to which the pixel belongs.  Pixels in the bg are labelled 0.
    /// (2) For purposes of visualization, the output can be converted
    ///     to 8 bpp, using pixConvert32To8() or pixMaxDynamicRange().
    /// </remarks>
    public Pix ConnCompAreaTransform(int connect)
    {
        const string func = "pixConnCompAreaTransform";
        Pix pixs = this;
        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (connect != 4 && connect != 8)
            throw Lept.Arg(func, "connectivity must be 4 or 8");

        pixs.ConnComp(out Pixa pixa, connect);
        int n = pixa.Count;
        pixs.GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 32);
        pixd.Spp = 1;
        if (n == 0)  /* no fg */
            return pixd;

           /* Label each component and blit it in */
        int[] tab8 = MakePixelSumTab8();
        for (int i = 0; i < n; i++) {
            pixa.GetBoxGeometry(i, out int xb, out int yb, out int wb, out int hb);
            Pix pix1 = pixa.GetPix(i, L.L_CLONE);
            int npix = pix1.CountPixels(tab8);
            Pix pix2 = Convert1To32(null, pix1, 0, (uint)npix);
            pixd.Rasterop(xb, yb, wb, hb, L.PIX_PAINT, pix2, 0, 0);
        }

        return pixd;
    }

    /*-------------------------------------------------------------------------*
     *  Label pixels to allow incremental computation of connected components  *
     *-------------------------------------------------------------------------*/
    // leptonica: pixConnCompIncrInit (pixlabel.c)
    /// <summary>Labels the connected components and indexes the pixel locations of each.</summary>
    /// <param name="conn">connectivity: 4 or 8</param>
    /// <param name="pixd">32 bpp, with c.c. labelled</param>
    /// <param name="ptaa">with pixel locations indexed by c.c.</param>
    /// <param name="ncc">initial number of c.c.</param>
    /// <remarks>
    /// (1) This labels the connected components in a 1 bpp pix, and
    ///     additionally sets up a ptaa that lists the locations of pixels
    ///     in each of the components.
    /// (2) It can be used to initialize the output image and arrays for
    ///     an application that maintains information about connected
    ///     components incrementally as pixels are added.
    /// (3) pixs can be empty or have some foreground pixels.
    /// (4) The connectivity is stored in pixd->special.
    /// (5) Always initialize with the first pta in ptaa being empty
    ///     and representing the background value (index 0) in the pix.
    /// </remarks>
    public void ConnCompIncrInit(int conn, out Pix pixd, out Ptaa ptaa, out int ncc)
    {
        const string func = "pixConnCompIncrInit";
        Pix pixs = this;
        pixd = null!;
        ptaa = null!;
        ncc = 0;
        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (conn != 4 && conn != 8)
            throw Lept.Arg(func, "connectivity must be 4 or 8");

        pixs.GetDimensions(out int w, out int h, out _);
        bool empty = pixs.Zero();
        if (empty) {
            pixd = Create(w, h, 32);
            pixd.Spp = 1;
            pixd.Special = conn;
            ptaa = Ptaa.Create(0);
            Pta pta = Pta.Create(1);
            ptaa.AddPta(pta, L.L_INSERT);  /* reserve index 0 for background */
            return;
        }

            /* Set up the initial labeled image and indexed pixel arrays */
        pixd = pixs.ConnCompTransform(conn, 32);
        pixd.Special = conn;
        ptaa = Ptaa.IndexLabeledPixels(pixd, out ncc);
    }

    // leptonica: pixConnCompIncrAdd (pixlabel.c)
    /// <summary>Adds a pixel and updates the labeled connected components.</summary>
    /// <param name="ptaa">with each pta of pixel locations indexed by c.c.</param>
    /// <param name="ncc">number of c.c.; updated</param>
    /// <param name="x">location of added pixel</param>
    /// <param name="y">location of added pixel</param>
    /// <param name="debug">0 for no output; otherwise output whenever
    ///     debug &lt;= nvals, up to debug == 3</param>
    /// <returns>false if nothing happens; true if a pixel is added</returns>
    /// <remarks>
    /// (1) This adds a pixel and updates the labeled connected components.
    ///     Before calling this function, initialize the process using
    ///     pixConnCompIncrInit().
    /// (2) As a result of adding a pixel, one of the following can happen,
    ///     depending on the number of neighbors with non-zero value:
    ///     (a) nothing: the pixel is already a member of a c.c.
    ///     (b) no neighbors: a new component is added, increasing the
    ///         number of c.c.
    ///     (c) one neighbor: the pixel is added to an existing c.c.
    ///     (d) more than one neighbor: the added pixel causes joining of
    ///         two or more c.c., reducing the number of c.c.  A maximum
    ///         of 4 c.c. can be joined.
    /// (3) When two c.c. are joined, the pixels in the larger index are
    ///     relabeled to those of the smaller in pixs, and their locations
    ///     are transferred to the pta with the smaller index in the ptaa.
    ///     The pta corresponding to the larger index is then deleted.
    /// (4) This is an efficient implementation of a "union-find" operation,
    ///     which supports the generation and merging of disjoint sets
    ///     of pixels.  This function can be called about 1.3 million times
    ///     per second.
    /// </remarks>
    public bool ConnCompIncrAdd(Ptaa ptaa, ref int ncc, float x, float y, int debug)
    {
        const string func = "pixConnCompIncrAdd";
        Pix pixs = this;
        if (pixs.Depth != 32)
            throw Lept.Arg(func, "pixs not defined or not 32 bpp");
        int conn = pixs.Special;
        if (conn != 4 && conn != 8)
            throw Lept.Arg(func, "connectivity must be 4 or 8");
        pixs.GetDimensions(out int w, out int h, out _);
        if (x < 0 || x >= w)
            throw Lept.Arg(func, "invalid x pixel location");
        if (y < 0 || y >= h)
            throw Lept.Arg(func, "invalid y pixel location");

        pixs.GetPixel((int)x, (int)y, out uint val);
        if (val > 0)  /* already belongs to a set */
            return false;

            /* Find unique neighbor pixel values in increasing order of value.
             * If %nvals > 0, these are returned in the %neigh array, which
             * is of size %nvals.  Note that the pixel values in each
             * connected component are used as the index into the pta
             * array of the ptaa, giving the pixel locations. */
        int[] neigh = pixs.GetSortedNeighborValues((int)x, (int)y, conn);
        int nvals = neigh.Length;

            /* If there are no neighbors, just add a new component */
        if (nvals == 0) {
            int count = ptaa.Count;
            pixs.SetPixel((int)x, (int)y, (uint)count);
            Pta ptas = Pta.Create(1);
            ptas.AddPt(x, y);
            ptaa.AddPta(ptas, L.L_INSERT);
            ncc += 1;
            return true;
        }

            /* Otherwise, there is at least one neighbor.  Add the pixel
             * to the first neighbor c.c. */
        int firstindex = neigh[0];
        pixs.SetPixel((int)x, (int)y, (uint)firstindex);
        ptaa.AddPt(neigh[0], x, y);
        if (nvals == 1) {
            if (debug == 1)
                Utils.LeptStderr("nvals = %d: neigh = (%d)\n", nvals, neigh[0]);
            return true;
        }

            /* If nvals > 1, there are at least 2 neighbors, so this pixel
             * joins at least one pair of existing c.c.  Join each component
             * to the first component in the list, which is the one with
             * the smallest integer label.  This is done in two steps:
             *  (a) re-label the pixels in the component to the label of the
             *      first component, and
             *  (b) save the pixel locations in the pta for the first component. */
        if (nvals == 2) {
            if (debug >= 1 && debug <= 2) {
                Utils.LeptStderr("nvals = %d: neigh = (%d,%d)\n", nvals,
                                 neigh[0], neigh[1]);
            }
        } else if (nvals == 3) {
            if (debug >= 1 && debug <= 3) {
                Utils.LeptStderr("nvals = %d: neigh = (%d,%d,%d)\n", nvals,
                                 neigh[0], neigh[1], neigh[2]);
            }
        } else {  /* nvals == 4 */
            if (debug >= 1 && debug <= 4) {
                Utils.LeptStderr("nvals = %d: neigh = (%d,%d,%d,%d)\n", nvals,
                                 neigh[0], neigh[1], neigh[2], neigh[3]);
            }
        }
        Pta ptad = ptaa.GetPta(firstindex, L.L_CLONE);
        for (int i = 1; i < nvals; i++) {
            Pta ptas = ptaa.GetPta(neigh[i], L.L_CLONE);
            int ns = ptas.Count;
            for (int j = 0; j < ns; j++) {  /* relabel pixels */
                ptas.GetPt(j, out x, out y);
                pixs.SetPixel((int)x, (int)y, (uint)firstindex);
            }
            ptad.Join(ptas, 0, -1);  /* add relabeled pixel locations */
            ncc -= 1;
            ptaa.DestroyPta(neigh[i]);
        }
        return true;
    }

    // leptonica: pixGetSortedNeighborValues (pixlabel.c)
    /// <summary>Unique non-zero values of the neighbors of a pixel, in increasing order.</summary>
    /// <param name="x">location of pixel</param>
    /// <param name="y">location of pixel</param>
    /// <param name="conn">4 or 8 connected neighbors</param>
    /// <returns>array of integers with the values of the neighbors; empty if there are none</returns>
    /// <remarks>
    /// (1) The returned %neigh array is the unique set of neighboring
    ///     pixel values, of size nvals, sorted from smallest to largest.
    ///     The value 0, which represents background pixels that do
    ///     not belong to any set of connected components, is discarded.
    /// (2) If there are no neighbors, this returns an empty array
    ///     (%neigh = NULL in C).
    /// (3) For either 4 or 8 connectivity, the maximum number of unique
    ///     neighbor values is 4.
    /// </remarks>
    public int[] GetSortedNeighborValues(int x, int y, int conn)
    {
        const string func = "pixGetSortedNeighborValues";
        Pix pixs = this;
        if (pixs.Depth < 8)
            throw Lept.Arg(func, "pixs not defined or depth < 8");

            /* Identify the locations of nearest neighbor pixels */
        Pta pta = Pta.GetNeighborPixLocs(pixs, x, y, conn);

            /* Find the pixel values and insert into a set as keys */
        // TODO(map.c): replace by l_asetCreate(L_UINT_TYPE) and l_asetInsert()
        var aset = new SortedSet<uint>();
        int npt = pta.Count;
        for (int i = 0; i < npt; i++) {
            pta.GetPt(i, out float fx, out float fy);
            pixs.GetPixel((int)fx, (int)fy, out uint val);
            aset.Add(val);
        }

            /* Extract the set keys and put them into the %neigh array.
             * Omit the value 0, which indicates the pixel doesn't
             * belong to one of the sets of connected components. */
        var neigh = new List<int>(4);
        foreach (uint val in aset) {
            if (val > 0)
                neigh.Add((int)val);
        }
        return neigh.ToArray();
    }

    /*-----------------------------------------------------------------------*
     *          Label pixels with spatially-dependent color coding           *
     *-----------------------------------------------------------------------*/
    // leptonica: pixLocToColorTransform (pixlabel.c)
    /// <summary>Colors the foreground pixels by location and by the size of their component.</summary>
    /// <returns>pixd 32 bpp rgb</returns>
    /// <remarks>
    /// (1) This generates an RGB image where each component value
    ///     is coded depending on the (x.y) location and the size
    ///     of the fg connected component that the pixel in pixs belongs to.
    ///     It is independent of the 4-fold orthogonal orientation, and
    ///     only weakly depends on translations and small angle rotations.
    ///     Background pixels are black.
    /// (2) Such encodings can be compared between two 1 bpp images
    ///     by performing this transform and calculating the
    ///     "earth-mover" distance on the resulting R,G,B histograms.
    /// </remarks>
    public Pix LocToColorTransform()
    {
        const string func = "pixLocToColorTransform";
        Pix pixs = this;
        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");

            /* Label each pixel with the area of the c.c. to which it belongs.
             * Clip the result to 255 in an 8 bpp pix. This is used for
             * the blue component of pixd.  */
        pixs.GetDimensions(out int w, out int h, out _);
        int w2 = w / 2;
        int h2 = h / 2;
        float invw2 = 255.0f / (float)w2;
        float invh2 = 255.0f / (float)h2;
        Pix pix1 = pixs.ConnCompAreaTransform(8);
        Pix pixcc = pix1.Convert32To8(L.L_LS_TWO_BYTES, L.L_CLIP_TO_FF);

            /* Label the red and green components depending on the location
             * of the fg pixels, in a way that is 4-fold rotationally invariant. */
        Pix pixr = Create(w, h, 8);
        Pix pixg = Create(w, h, 8);
        Pix pixb = Create(w, h, 8);
        int wpls = pixs.Wpl;
        int wplr = pixr.Wpl;
        int wplg = pixg.Wpl;
        int wplb = pixb.Wpl;
        int wplcc = pixcc.Wpl;
        uint[] datas = pixs.Data;
        uint[] datar = pixr.Data;
        uint[] datag = pixg.Data;
        uint[] datab = pixb.Data;
        uint[] datacc = pixcc.Data;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> liner = datar.AsSpan(i * wplr, wplr);
            Span<uint> lineg = datag.AsSpan(i * wplg, wplg);
            Span<uint> lineb = datab.AsSpan(i * wplb, wplb);
            ReadOnlySpan<uint> linecc = datacc.AsSpan(i * wplcc, wplcc);
            for (int j = 0; j < w; j++) {
                if (ArrayAccess.GetDataBit(lines, j) == 0) continue;
                int rval, gval;
                if (w < h) {
                    rval = (int)(invh2 * Math.Abs((float)(i - h2)));
                    gval = (int)(invw2 * Math.Abs((float)(j - w2)));
                } else {
                    rval = (int)(invw2 * Math.Abs((float)(j - w2)));
                    gval = (int)(invh2 * Math.Abs((float)(i - h2)));
                }
                int bval = ArrayAccess.GetDataByte(linecc, j);
                ArrayAccess.SetDataByte(liner, j, rval);
                ArrayAccess.SetDataByte(lineg, j, gval);
                ArrayAccess.SetDataByte(lineb, j, bval);
            }
        }
        return CreateRGBImage(pixr, pixg, pixb);
    }
}

public sealed partial class Ptaa
{
    /// <summary>Sets the pta at the index to null (ptaDestroy(&amp;ptaa->pta[index]) in C).</summary>
    internal void DestroyPta(int index)
    {
        pta[index] = null;
    }
}
