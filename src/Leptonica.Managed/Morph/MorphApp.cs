using System;

namespace Leptonica;

/*
 *  morphapp.c: Morphological applications
 *
 *      These are some useful and/or illustrative composite
 *      image processing operations, using morphology.
 *
 *      Extraction of boundary pixels
 *            Pix.ExtractBoundary
 *
 *      Selective morph sequence operation under mask
 *            Pix.MorphSequenceMasked
 *
 *      Selective morph sequence operation on pixa components
 *            Pix.MorphSequenceByComponent
 *            Pixa.MorphSequenceByComponent
 *
 *      Selective morph sequence operation on pixa regions
 *            Pix.MorphSequenceByRegion
 *            Pixa.MorphSequenceByRegion
 *
 *      Union and intersection of parallel composite operations
 *            Pix.UnionOfMorphOps
 *            Pix.IntersectionOfMorphOps
 *
 *      Selective connected component filling
 *            Pix.SelectiveConnCompFill
 *
 *      Removal of matched patterns
 *            Pix.RemoveMatchedPattern
 *
 *      Display of matched patterns
 *            Pix.DisplayMatchedPattern
 *
 *      Extension by iterative erosion or dilation (and by scaling)
 *            Pixa.ExtendByMorph
 *            Pixa.ExtendByScaling
 *
 *      Iterative morphological seed filling
 *            Pix.SeedfillMorph
 *
 *      Granulometry on binary images
 *            Pix.RunHistogramMorph
 *
 *      Composite operations on grayscale images
 *            Pix.Tophat
 *            Pix.HDome
 *            Pix.FastTophat
 *            Pix.MorphGradient
 *
 *      Centroid of component
 *            Pixa.Centroids
 *            Pix.Centroid
 */

public static partial class L
{
    /* Run types (pix.h) */
    /// <summary>Run of OFF (background) pixels.</summary>
    public const int L_RUN_OFF = 0;
    /// <summary>Run of ON (foreground) pixels.</summary>
    public const int L_RUN_ON = 1;
}

public sealed partial class Pix
{
    /*-----------------------------------------------------------------*
     *                   Extraction of boundary pixels                 *
     *-----------------------------------------------------------------*/
    // leptonica: pixExtractBoundary (morphapp.c)
    /// <summary>Extracts the boundary pixels of a 1 bpp image.</summary>
    /// <param name="type">0 for background pixels; 1 for foreground pixels</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Extracts the fg or bg boundary pixels for each component.
    ///     Components are assumed to end at the boundary of pixs.
    /// </remarks>
    public Pix ExtractBoundary(int type)
    {
        Pix pixd;

        if (type == 0)
            pixd = DilateBrick(null, this, 3, 3);
        else
            pixd = ErodeBrick(null, this, 3, 3);
        Xor(pixd, pixd, this);
        return pixd;
    }

    /*-----------------------------------------------------------------*
     *           Selective morph sequence operation under mask         *
     *-----------------------------------------------------------------*/
    // leptonica: pixMorphSequenceMasked (morphapp.c)
    /// <summary>Applies the morph sequence to the image, but only allows changes under the background of pixm.</summary>
    /// <param name="pixm">[optional] 1 bpp mask</param>
    /// <param name="sequence">string specifying sequence of operations</param>
    /// <param name="dispsep">horizontal separation in pixels between successive displays; use zero to suppress display</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (5) If pixm is null, this is just <see cref="MorphSequence"/>.
    /// </remarks>
    public Pix MorphSequenceMasked(Pix? pixm, string sequence, int dispsep)
    {
        Pix pixd;

        pixd = MorphSequence(sequence, dispsep);
        pixd.CombineMasked(this, pixm);  /* restore src pixels under mask fg */
        return pixd;
    }

    /*-----------------------------------------------------------------*
     *             Morph sequence operation on each component          *
     *-----------------------------------------------------------------*/
    // leptonica: pixMorphSequenceByComponent (morphapp.c)
    /// <summary>Applies the morph sequence to each connected component of the image separately.</summary>
    /// <param name="sequence">string specifying sequence</param>
    /// <param name="connectivity">4 or 8</param>
    /// <param name="minw">min width to consider; use 0 or 1 for any width</param>
    /// <param name="minh">min height to consider; use 0 or 1 for any height</param>
    /// <param name="pboxa">boxa of c.c. in pixs</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) See pixMorphSequence() for composing operation sequences.
    /// (2) This operates separately on each c.c. in the input pix.
    /// (3) The dilation does NOT increase the c.c. size; it is clipped
    ///     to the size of the original c.c.   This is necessary to
    ///     keep the c.c. independent after the operation.
    /// (4) You can specify that the width and/or height must equal
    ///     or exceed a minimum size for the operation to take place.
    /// (5) The boxa is always returned; the caller can ignore it.
    /// </remarks>
    public Pix MorphSequenceByComponent(string sequence, int connectivity,
                                        int minw, int minh, out Boxa pboxa)
    {
        int n, i, x, y, w, h;
        Boxa boxa;
        Pix pix, pixd;
        Pixa pixas, pixad;

        if (minw <= 0) minw = 1;
        if (minh <= 0) minh = 1;

            /* Get the c.c. */
        boxa = ConnComp(out pixas, connectivity);

            /* Operate on each c.c. independently */
        pixad = pixas.MorphSequenceByComponent(sequence, minw, minh);

            /* Display the result out into pixd */
        pixd = CreateTemplate(this);
        n = pixad.Count;
        for (i = 0; i < n; i++) {
            pixad.GetBoxGeometry(i, out x, out y, out w, out h);
            pix = pixad.GetPix(i, L.L_CLONE);
            pixd.Rasterop(x, y, w, h, L.PIX_PAINT, pix, 0, 0);
        }

        pboxa = pixad.GetBoxa(L.L_CLONE);
        return pixd;
    }

    /*-----------------------------------------------------------------*
     *              Morph sequence operation on each region            *
     *-----------------------------------------------------------------*/
    // leptonica: pixMorphSequenceByRegion (morphapp.c)
    /// <summary>Applies the morph sequence to the region of the image under each connected component of the mask.</summary>
    /// <param name="pixm">mask specifying regions</param>
    /// <param name="sequence">string specifying sequence</param>
    /// <param name="connectivity">4 or 8, used on mask</param>
    /// <param name="minw">min width to consider; use 0 or 1 for any width</param>
    /// <param name="minh">min height to consider; use 0 or 1 for any height</param>
    /// <param name="pboxa">boxa of c.c. in pixm</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) See pixMorphCompSequence() for composing operation sequences.
    /// (2) This operates separately on the region in pixs corresponding
    ///     to each c.c. in the mask pixm.  It differs from
    ///     pixMorphSequenceByComponent() in that the latter does not have
    ///     a pixm (mask), but instead operates independently on each
    ///     component in pixs.
    /// (3) Dilation will NOT increase the region size; the result
    ///     is clipped to the size of the mask region.  This is necessary
    ///     to make regions independent after the operation.
    /// (4) You can specify that the width and/or height of a region must
    ///     equal or exceed a minimum size for the operation to take place.
    /// (5) The boxa is always returned; the caller can ignore it.
    /// </remarks>
    public Pix MorphSequenceByRegion(Pix pixm, string sequence, int connectivity,
                                     int minw, int minh, out Boxa? pboxa)
    {
        const string func = "pixMorphSequenceByRegion";
        int n, i, x, y, w, h;
        Boxa boxa;
        Pix pix, pixd;
        Pixa pixam, pixad;

        pboxa = null;
        if (Depth != 1 || pixm.Depth != 1)
            throw Lept.Arg(func, "pixs and pixm not both 1 bpp");

        if (minw <= 0) minw = 1;
        if (minh <= 0) minh = 1;

            /* Get the c.c. of the mask */
        boxa = pixm.ConnComp(out pixam, connectivity);

            /* Operate on each region in pixs independently */
        pixad = MorphSequenceByRegion(pixam, sequence, minw, minh);

            /* Display the result out into pixd */
        pixd = CreateTemplate(this);
        n = pixad.Count;
        for (i = 0; i < n; i++) {
            pixad.GetBoxGeometry(i, out x, out y, out w, out h);
            pix = pixad.GetPix(i, L.L_CLONE);
            pixd.Rasterop(x, y, w, h, L.PIX_PAINT, pix, 0, 0);
        }

        pboxa = pixad.GetBoxa(L.L_CLONE);
        return pixd;
    }

    // leptonica: pixaMorphSequenceByRegion (morphapp.c)
    /// <summary>Applies the morph sequence to the region of the image under each component of pixam.</summary>
    /// <param name="pixam">of 1 bpp mask elements</param>
    /// <param name="sequence">string specifying sequence</param>
    /// <param name="minw">min width to consider; use 0 or 1 for any width</param>
    /// <param name="minh">min height to consider; use 0 or 1 for any height</param>
    /// <returns>pixad</returns>
    /// <remarks>
    /// (1) See pixMorphSequence() for composing operation sequences.
    /// (2) This operates separately on each region in the input pixs
    ///     defined by the components in pixam.
    /// (3) You can specify that the width and/or height of a mask
    ///     component must equal or exceed a minimum size for the
    ///     operation to take place.
    /// (4) The input pixam should have a boxa giving the locations
    ///     of the regions in pixs.
    /// </remarks>
    public Pixa MorphSequenceByRegion(Pixa pixam, string sequence, int minw, int minh)
    {
        const string func = "pixaMorphSequenceByRegion";
        int n, i, w, h, maxd;
        bool same, fullpa, fullba;
        Box? box;
        Pix pix1, pix2, pix3;
        Pixa pixad;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        pixam.VerifyDepth(out same, out maxd);
        if (maxd != 1)
            throw Lept.Arg(func, "mask depth not 1 bpp");
        pixam.IsFull(out fullpa, out fullba);
        if (!fullpa || !fullba)
            throw Lept.Arg(func, "missing comps in pixam");
        n = pixam.Count;
        if (minw <= 0) minw = 1;
        if (minh <= 0) minh = 1;

        pixad = Pixa.Create(n);

            /* Use the rectangle to remove the appropriate part of pixs;
             * then AND with the mask component to get the actual fg
             * of pixs that is under the mask component. */
        for (i = 0; i < n; i++) {
            pixam.GetPixDimensions(i, out w, out h, out _);
            if (w >= minw && h >= minh) {
                pix1 = pixam.GetPix(i, L.L_CLONE);
                box = pixam.GetBox(i, L.L_COPY);
                pix2 = ClipRectangle(box!, out _)!;
                And(pix2, pix2, pix1);
                pix3 = pix2.MorphCompSequence(sequence, 0);
                pixad.AddPix(pix3, L.L_INSERT);
                pixad.AddBox(box!, L.L_INSERT);
            }
        }

        return pixad;
    }

    /*-----------------------------------------------------------------*
     *      Union and intersection of parallel composite operations    *
     *-----------------------------------------------------------------*/
    // leptonica: pixUnionOfMorphOps (morphapp.c)
    /// <summary>Union of the results of a morphological operation with each Sel of a Sela.</summary>
    /// <param name="sela"></param>
    /// <param name="type">L_MORPH_DILATE, etc.</param>
    /// <returns>pixd union of the specified morphological operation on pixs for each Sel in the Sela</returns>
    public Pix UnionOfMorphOps(Sela sela, int type)
    {
        const string func = "pixUnionOfMorphOps";
        int n, i;
        Pix pixt, pixd;
        Sel sel;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        n = sela.Count;
        if (n == 0)
            throw Lept.Arg(func, "no sels in sela");
        if (type != L.L_MORPH_DILATE && type != L.L_MORPH_ERODE &&
            type != L.L_MORPH_OPEN && type != L.L_MORPH_CLOSE &&
            type != L.L_MORPH_HMT)
            throw Lept.Arg(func, "invalid type");

        pixd = CreateTemplate(this);
        for (i = 0; i < n; i++) {
            sel = sela.GetSel(i);
            if (type == L.L_MORPH_DILATE)
                pixt = Dilate(null, this, sel);
            else if (type == L.L_MORPH_ERODE)
                pixt = Erode(null, this, sel);
            else if (type == L.L_MORPH_OPEN)
                pixt = Open(null, this, sel);
            else if (type == L.L_MORPH_CLOSE)
                pixt = Close(null, this, sel);
            else  /* type == L_MORPH_HMT */
                pixt = HMT(null, this, sel);
            Or(pixd, pixd, pixt);
        }

        return pixd;
    }

    // leptonica: pixIntersectionOfMorphOps (morphapp.c)
    /// <summary>Intersection of the results of a morphological operation with each Sel of a Sela.</summary>
    /// <param name="sela"></param>
    /// <param name="type">L_MORPH_DILATE, etc.</param>
    /// <returns>pixd intersection of the specified morphological operation on pixs for each Sel in the Sela</returns>
    public Pix IntersectionOfMorphOps(Sela sela, int type)
    {
        const string func = "pixIntersectionOfMorphOps";
        int n, i;
        Pix pixt, pixd;
        Sel sel;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        n = sela.Count;
        if (n == 0)
            throw Lept.Arg(func, "no sels in sela");
        if (type != L.L_MORPH_DILATE && type != L.L_MORPH_ERODE &&
            type != L.L_MORPH_OPEN && type != L.L_MORPH_CLOSE &&
            type != L.L_MORPH_HMT)
            throw Lept.Arg(func, "invalid type");

        pixd = CreateTemplate(this);
        pixd.SetAll();
        for (i = 0; i < n; i++) {
            sel = sela.GetSel(i);
            if (type == L.L_MORPH_DILATE)
                pixt = Dilate(null, this, sel);
            else if (type == L.L_MORPH_ERODE)
                pixt = Erode(null, this, sel);
            else if (type == L.L_MORPH_OPEN)
                pixt = Open(null, this, sel);
            else if (type == L.L_MORPH_CLOSE)
                pixt = Close(null, this, sel);
            else  /* type == L_MORPH_HMT */
                pixt = HMT(null, this, sel);
            And(pixd, pixd, pixt);
        }

        return pixd;
    }

    /*-----------------------------------------------------------------*
     *             Selective connected component filling               *
     *-----------------------------------------------------------------*/
    // leptonica: pixSelectiveConnCompFill (morphapp.c)
    /// <summary>Fills the holes of the connected components of at least a given size.</summary>
    /// <param name="connectivity">4 or 8</param>
    /// <param name="minw">min width to consider; use 0 or 1 for any width</param>
    /// <param name="minh">min height to consider; use 0 or 1 for any height</param>
    /// <returns>pix with holes filled in selected c.c.</returns>
    public Pix SelectiveConnCompFill(int connectivity, int minw, int minh)
    {
        const string func = "pixSelectiveConnCompFill";
        int n, i, x, y, w, h;
        Boxa boxa;
        Pix pix1, pix2, pixd;
        Pixa pixa;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (minw <= 0) minw = 1;
        if (minh <= 0) minh = 1;

        boxa = ConnComp(out pixa, connectivity);
        n = boxa.Count;
        pixd = Copy(null, this);
        for (i = 0; i < n; i++) {
            boxa.GetBoxGeometry(i, out x, out y, out w, out h);
            if (w >= minw && h >= minh) {
                pix1 = pixa.GetPix(i, L.L_CLONE);
                pix2 = pix1.HolesByFilling(12 - connectivity);
                pixd.Rasterop(x, y, w, h, L.PIX_PAINT, pix2, 0, 0);
            }
        }

        return pixd;
    }

    /*-----------------------------------------------------------------*
     *                    Removal of matched patterns                  *
     *-----------------------------------------------------------------*/
    // leptonica: pixRemoveMatchedPattern (morphapp.c)
    /// <summary>Removes the matched patterns, in place.</summary>
    /// <param name="pixp">pattern to be removed from image, 1 bpp</param>
    /// <param name="pixe">image after erosion by Sel that approximates pixp</param>
    /// <param name="x0">center of Sel</param>
    /// <param name="y0">center of Sel</param>
    /// <param name="dsize">number of pixels on each side by which pixp is dilated before being subtracted from pixs; valid values are {0, 1, 2, 3, 4}</param>
    /// <remarks>
    /// (1) This is in-place.
    /// (2) You can use various functions in selgen to create a Sel
    ///     that is used to generate pixe from pixs.
    /// (3) This function is applied after pixe has been computed.
    ///     It finds the centroid of each c.c., and subtracts
    ///     (the appropriately dilated version of) pixp, with the center
    ///     of the Sel used to align pixp with pixs.
    /// </remarks>
    public void RemoveMatchedPattern(Pix pixp, Pix pixe, int x0, int y0, int dsize)
    {
        const string func = "pixRemoveMatchedPattern";
        int i, nc, x, y, w, h, xb, yb;
        Boxa boxa;
        Pix pix1, pix2;
        Pixa pixa;
        Pta pta;
        Sel sel;

        if (Depth != 1 || pixp.Depth != 1 || pixe.Depth != 1)
            throw Lept.Arg(func, "all input pix not 1 bpp");
        if (dsize < 0 || dsize > 4)
            throw Lept.Arg(func, "dsize not in {0,1,2,3,4}");

            /* Find the connected components and their centroids */
        boxa = pixe.ConnComp(out pixa, 8);
        if ((nc = boxa.Count) == 0) {
            Log.Warning(func, "no matched patterns");
            return;
        }
        pta = pixa.Centroids();

            /* Optionally dilate the pattern, first adding a border that
             * is large enough to accommodate the dilated pixels */
        if (dsize > 0) {
            sel = Sel.CreateBrick(2 * dsize + 1, 2 * dsize + 1, dsize, dsize,
                                  L.SEL_HIT);
            pix1 = pixp.AddBorder(dsize, 0);
            pix2 = Dilate(null, pix1, sel);
        } else {
            pix2 = Clone(pixp);
        }

            /* Subtract out each dilated pattern.  The centroid of each
             * component is located at:
             *       (box->x + x, box->y + y)
             * and the 'center' of the pattern used in making pixe is located at
             *       (x0 + dsize, (y0 + dsize)
             * relative to the UL corner of the pattern.  The center of the
             * pattern is placed at the center of the component. */
        pix2.GetDimensions(out w, out h, out _);
        for (i = 0; i < nc; i++) {
            pta.GetIPt(i, out x, out y);
            boxa.GetBoxGeometry(i, out xb, out yb, out _, out _);
            Rasterop(xb + x - x0 - dsize, yb + y - y0 - dsize,
                     w, h, L.PIX_DST & L.PIX_NOT(L.PIX_SRC), pix2, 0, 0);
        }
    }

    /*-----------------------------------------------------------------*
     *                    Display of matched patterns                  *
     *-----------------------------------------------------------------*/
    // leptonica: pixDisplayMatchedPattern (morphapp.c)
    /// <summary>Paints the matched patterns with a color, on a 4 bpp colormapped copy of the image.</summary>
    /// <param name="pixp">pattern to be removed from image, 1 bpp</param>
    /// <param name="pixe">image after erosion by Sel that approximates pixp</param>
    /// <param name="x0">center of Sel</param>
    /// <param name="y0">center of Sel</param>
    /// <param name="color">to paint the matched patterns; 0xrrggbb00</param>
    /// <param name="scale">reduction factor for output pixd</param>
    /// <param name="nlevels">if scale &lt; 1.0, threshold to this number of levels</param>
    /// <returns>pixd 8 bpp, colormapped; null if there are no matched patterns</returns>
    /// <remarks>
    /// (1) A 4 bpp colormapped image is generated.
    /// (2) If scale &lt;= 1.0, do scale to gray for the output, and threshold
    ///     to nlevels of gray.
    /// (3) You can use various functions in selgen to create a Sel
    ///     that will generate pixe from pixs.
    /// (4) This function is applied after pixe has been computed.
    ///     It finds the centroid of each c.c., and colors the output
    ///     pixels using pixp (appropriately aligned) as a stencil.
    ///     Alignment is done using the origin of the Sel and the
    ///     centroid of the eroded image to place the stencil pixp.
    /// </remarks>
    public Pix? DisplayMatchedPattern(Pix pixp, Pix pixe, int x0, int y0,
                                      uint color, float scale, int nlevels)
    {
        const string func = "pixDisplayMatchedPattern";
        int i, nc, xb, yb, x, y, xi, yi, rval, gval, bval;
        Boxa boxa;
        Pix pixd, pixt, pixps;
        Pixa pixa;
        Pta pta;
        PixColormap cmap;

        if (Depth != 1 || pixp.Depth != 1 || pixe.Depth != 1)
            throw Lept.Arg(func, "all input pix not 1 bpp");
        if (scale > 1.0 || scale <= 0.0) {
            Log.Warning(func, "scale > 1.0 or < 0.0; setting to 1.0");
            scale = 1.0f;
        }

            /* Find the connected components and their centroids */
        boxa = pixe.ConnComp(out pixa, 8);
        if ((nc = boxa.Count) == 0) {
            Log.Warning(func, "no matched patterns");
            return null;
        }
        pta = pixa.Centroids();

        ExtractRGBValues(color, out rval, out gval, out bval);
        if (scale == 1.0) {  /* output 4 bpp at full resolution */
            pixd = Convert1To4(null, this, 0, 1);
            cmap = PixColormap.Create(4);
            cmap.AddColor(255, 255, 255);
            cmap.AddColor(0, 0, 0);
            pixd.SetColormap(cmap);

            /* Paint through pixp for each match location.  The centroid of each
             * component in pixe is located at:
             *       (box->x + x, box->y + y)
             * and the 'center' of the pattern used in making pixe is located at
             *       (x0, y0)
             * relative to the UL corner of the pattern.  The center of the
             * pattern is placed at the center of the component. */
            for (i = 0; i < nc; i++) {
                pta.GetIPt(i, out x, out y);
                boxa.GetBoxGeometry(i, out xb, out yb, out _, out _);
                pixd.SetMaskedCmap(pixp, xb + x - x0, yb + y - y0,
                                   rval, gval, bval);
            }
        } else {  /* output 4 bpp downscaled */
            pixt = ScaleToGray(scale);
            pixd = pixt.ThresholdTo4bpp(nlevels, true);
            pixps = pixp.ScaleBySampling(scale, scale);

            for (i = 0; i < nc; i++) {
                pta.GetIPt(i, out x, out y);
                boxa.GetBoxGeometry(i, out xb, out yb, out _, out _);
                xi = (int)(scale * (xb + x - x0));
                yi = (int)(scale * (yb + y - y0));
                pixd.SetMaskedCmap(pixps, xi, yi, rval, gval, bval);
            }
        }

        return pixd;
    }

    /*-----------------------------------------------------------------*
     *             Iterative morphological seed filling                *
     *-----------------------------------------------------------------*/
    // leptonica: pixSeedfillMorph (morphapp.c)
    /// <summary>Fills from the seed into the mask by iterated dilation, clipped by the mask.</summary>
    /// <param name="pixm">mask</param>
    /// <param name="maxiters">use 0 to go to completion</param>
    /// <param name="connectivity">4 or 8</param>
    /// <returns>pixd after filling into the mask</returns>
    /// <remarks>
    /// (1) This is in general a very inefficient method for filling
    ///     from a seed into a mask.  Use it for a small number of iterations,
    ///     but if you expect more than a few iterations, use
    ///     pixSeedfillBinary().
    /// (2) We use a 3x3 brick SEL for 8-cc filling and a 3x3 plus SEL for 4-cc.
    /// </remarks>
    public Pix SeedfillMorph(Pix pixm, int maxiters, int connectivity)
    {
        const string func = "pixSeedfillMorph";
        int i;
        bool same;
        Pix pixt, pixd, temp;
        Sel sel_3;

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not in {4,8}");
        if (maxiters <= 0) maxiters = 1000;
        if (SizesEqual(this, pixm) == false)
            throw Lept.Arg(func, "pix sizes unequal");

        sel_3 = Sel.CreateBrick(3, 3, 1, 1, L.SEL_HIT);
        if (connectivity == 4) {  /* remove corner hits to make a '+' */
            sel_3.SetElement(0, 0, L.SEL_DONT_CARE);
            sel_3.SetElement(2, 2, L.SEL_DONT_CARE);
            sel_3.SetElement(2, 0, L.SEL_DONT_CARE);
            sel_3.SetElement(0, 2, L.SEL_DONT_CARE);
        }

        pixt = Copy(null, this);
        pixd = CreateTemplate(this);
        for (i = 1; i <= maxiters; i++) {
            Dilate(pixd, pixt, sel_3);
            And(pixd, pixd, pixm);
            same = Equal(pixd, pixt);
            if (same || i == maxiters)
                break;
            else {
                temp = pixt; pixt = pixd; pixd = temp;
            }
        }
        Utils.LeptStderr(" Num iters in binary reconstruction = %d\n", i);

        return pixd;
    }

    /*-----------------------------------------------------------------*
     *                   Granulometry on binary images                 *
     *-----------------------------------------------------------------*/
    // leptonica: pixRunHistogramMorph (morphapp.c)
    /// <summary>Histogram of run lengths, by iterated erosion with a 2-pixel Sel.</summary>
    /// <param name="runtype">L_RUN_OFF, L_RUN_ON</param>
    /// <param name="direction">L_HORIZ, L_VERT</param>
    /// <param name="maxsize">size of largest runlength counted</param>
    /// <returns>numa of run-lengths</returns>
    public Numa RunHistogramMorph(int runtype, int direction, int maxsize)
    {
        const string func = "pixRunHistogramMorph";
        int count, i, size;
        float val;
        Numa na, nah;
        Pix pix1, pix2, pix3;
        Sel sel_2a;

        if (runtype != L.L_RUN_OFF && runtype != L.L_RUN_ON)
            throw Lept.Arg(func, "invalid run type");
        if (direction != L.L_HORIZ && direction != L.L_VERT)
            throw Lept.Arg(func, "direction not in {L_HORIZ, L_VERT}");
        if (Depth != 1)
            throw Lept.Arg(func, "pixs must be binary");

        if (direction == L.L_HORIZ)
            sel_2a = Sel.CreateBrick(1, 2, 0, 0, L.SEL_HIT);
        else   /* direction == L_VERT */
            sel_2a = Sel.CreateBrick(2, 1, 0, 0, L.SEL_HIT);

        if (runtype == L.L_RUN_OFF) {
            pix1 = Copy(null, this);
            Invert(pix1, pix1);
        } else {  /* runtype == L_RUN_ON */
            pix1 = Clone(this);
        }

            /* Get pixel counts at different stages of erosion */
        na = Numa.Create(0);
        pix2 = CreateTemplate(this);
        pix3 = CreateTemplate(this);
        count = pix1.CountPixels(null);
        na.AddNumber(count);
        Erode(pix2, pix1, sel_2a);
        count = pix2.CountPixels(null);
        na.AddNumber(count);
        for (i = 0; i < maxsize / 2; i++) {
            Erode(pix3, pix2, sel_2a);
            count = pix3.CountPixels(null);
            na.AddNumber(count);
            Erode(pix2, pix3, sel_2a);
            count = pix2.CountPixels(null);
            na.AddNumber(count);
        }

            /* Compute length histogram */
        size = na.Count;
        nah = Numa.Create(size);
        nah.AddNumber(0); /* number at length 0 */
        for (i = 1; i < size - 1; i++) {
            val = na.GetFValue(i + 1) - 2 * na.GetFValue(i) + na.GetFValue(i - 1);
            nah.AddNumber(val);
        }

        return nah;
    }

    /*-----------------------------------------------------------------*
     *            Composite operations on grayscale images             *
     *-----------------------------------------------------------------*/
    // leptonica: pixTophat (morphapp.c)
    /// <summary>Gray tophat: the difference between the image and its opening (white) or closing (black).</summary>
    /// <param name="hsize">of Sel; must be odd; origin implicitly in center</param>
    /// <param name="vsize">ditto</param>
    /// <param name="type">L_TOPHAT_WHITE: image - opening; L_TOPHAT_BLACK: closing - image</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Sel is a brick with all elements being hits
    /// (2) If hsize = vsize = 1, returns an image with all 0 data.
    /// (3) The L_TOPHAT_WHITE flag emphasizes small bright regions,
    ///     whereas the L_TOPHAT_BLACK flag emphasizes small dark regions.
    ///     The L_TOPHAT_WHITE tophat can be accomplished by doing a
    ///     L_TOPHAT_BLACK tophat on the inverse, or v.v.
    /// </remarks>
    public Pix Tophat(int hsize, int vsize, int type)
    {
        const string func = "pixTophat";
        Pix pixt, pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize or vsize < 1");
        if ((hsize & 1) == 0) {
            Log.Warning(func, "horiz sel size must be odd; increasing by 1");
            hsize++;
        }
        if ((vsize & 1) == 0) {
            Log.Warning(func, "vert sel size must be odd; increasing by 1");
            vsize++;
        }
        if (type != L.L_TOPHAT_WHITE && type != L.L_TOPHAT_BLACK)
            throw Lept.Arg(func, "type must be L_TOPHAT_BLACK or L_TOPHAT_WHITE");

        if (hsize == 1 && vsize == 1)
            return CreateTemplate(this);

        switch (type)
        {
        case L.L_TOPHAT_WHITE:
            pixt = OpenGray(hsize, vsize);
            pixd = SubtractGray(null, this, pixt);
            break;
        case L.L_TOPHAT_BLACK:
            pixd = CloseGray(hsize, vsize);
            SubtractGray(pixd, pixd, this);
            break;
        default:
            throw Lept.Arg(func, "invalid type");
        }

        return pixd;
    }

    // leptonica: pixHDome (morphapp.c)
    /// <summary>Extracts the peaks of the image that are not higher than a given value (height dome).</summary>
    /// <param name="height">of seed below the filling maskhdome; must be &gt;= 0</param>
    /// <param name="connectivity">4 or 8</param>
    /// <returns>pixd 8 bpp</returns>
    /// <remarks>
    /// (1) It is more efficient to use a connectivity of 4 for the fill.
    /// (2) This fills bumps to some level, and extracts the unfilled
    ///     part of the bump.  To extract the troughs of basins, first
    ///     invert pixs and then apply pixHDome().
    /// (3) It is useful to compare the HDome operation with the TopHat.
    ///     The latter extracts peaks or valleys that have a width
    ///     not exceeding the size of the structuring element used
    ///     in the opening or closing, rsp.  The height of the peak is
    ///     irrelevant.  By contrast, for the HDome, the gray seedfill
    ///     is used to extract all peaks that have a height not exceeding
    ///     a given value, regardless of their width!
    /// (4) Slightly more precisely, suppose you set 'height' = 40.
    ///     Then all bumps in pixs with a height greater than or equal
    ///     to 40 become, in pixd, bumps with a max value of exactly 40.
    ///     All shorter bumps have a max value in pixd equal to the height
    ///     of the bump.
    /// (5) The method: the filling mask, pixs, is the image whose peaks
    ///     are to be extracted.  The height of a peak is the distance
    ///     between the top of the peak and the highest "leak" to the
    ///     outside -- think of a sombrero, where the leak occurs
    ///     at the highest point on the rim.
    ///       (a) Generate a seed, pixd, by subtracting some value, p, from
    ///           each pixel in the filling mask, pixs.  The value p is
    ///           the 'height' input to this function.
    ///       (b) Fill in pixd starting with this seed, clipping by pixs,
    ///           in the way described in seedfillGrayLow().  The filling
    ///           stops before the peaks in pixs are filled.
    ///           For peaks that have a height > p, pixd is filled to
    ///           the level equal to the (top-of-the-peak - p).
    ///           For peaks of height &lt; p, the peak is left unfilled
    ///           from its highest saddle point (the leak to the outside).
    ///       (c) Subtract the filled seed (pixd) from the filling mask (pixs).
    ///     Note that in this procedure, everything is done starting
    ///     with the filling mask, pixs.
    /// (6) For segmentation, the resulting image, pixd, can be thresholded
    ///     and used as a seed for another filling operation.
    /// </remarks>
    public Pix HDome(int height, int connectivity)
    {
        const string func = "pixHDome";
        Pix pixsd, pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (height < 0)
            throw Lept.Arg(func, "height not >= 0");
        if (height == 0)
            return CreateTemplate(this);

        pixsd = Copy(null, this);
        pixsd.AddConstantGray(-height);
        pixsd.SeedfillGray(this, connectivity);
        pixd = SubtractGray(null, this, pixsd);
        return pixd;
    }

    // leptonica: pixFastTophat (morphapp.c)
    /// <summary>Fast tophat-like operation, using a downscaled min or max.</summary>
    /// <param name="xsize">width of max/min op, smoothing; any integer &gt;= 1</param>
    /// <param name="ysize">height of max/min op, smoothing; any integer &gt;= 1</param>
    /// <param name="type">L_TOPHAT_WHITE: image - min; L_TOPHAT_BLACK: max - image</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Don't be fooled. This is NOT a tophat.  It is a tophat-like
    ///     operation, where the result is similar to what you'd get
    ///     if you used an erosion instead of an opening, or a dilation
    ///     instead of a closing.
    /// (2) Instead of opening or closing at full resolution, it does
    ///     a fast downscale/minmax operation, then a quick small smoothing
    ///     at low res, a replicative expansion of the "background"
    ///     to full res, and finally a removal of the background level
    ///     from the input image.  The smoothing step may not be important.
    /// (3) It does not remove noise as well as a tophat, but it is
    ///     5 to 10 times faster.
    ///     If you need the preciseness of the tophat, don't use this.
    /// (4) The L_TOPHAT_WHITE flag emphasizes small bright regions,
    ///     whereas the L_TOPHAT_BLACK flag emphasizes small dark regions.
    /// </remarks>
    public Pix FastTophat(int xsize, int ysize, int type)
    {
        const string func = "pixFastTophat";
        Pix pix1, pix2, pix3, pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (xsize < 1 || ysize < 1)
            throw Lept.Arg(func, "size < 1");
        if (type != L.L_TOPHAT_WHITE && type != L.L_TOPHAT_BLACK)
            throw Lept.Arg(func, "type must be L_TOPHAT_BLACK or L_TOPHAT_WHITE");

        if (xsize == 1 && ysize == 1)
            return CreateTemplate(this);

        switch (type)
        {
        case L.L_TOPHAT_WHITE:
            pix1 = ScaleGrayMinMax(xsize, ysize, L.L_CHOOSE_MIN);
            pix2 = pix1.Blockconv(1, 1);  /* small smoothing */
            pix3 = pix2.ScaleBySampling(xsize, ysize);
            pixd = SubtractGray(null, this, pix3);
            break;
        case L.L_TOPHAT_BLACK:
            pix1 = ScaleGrayMinMax(xsize, ysize, L.L_CHOOSE_MAX);
            pix2 = pix1.Blockconv(1, 1);  /* small smoothing */
            pixd = pix2.ScaleBySampling(xsize, ysize);
            SubtractGray(pixd, pixd, this);
            break;
        default:
            throw Lept.Arg(func, "invalid type");
        }

        return pixd;
    }

    // leptonica: pixMorphGradient (morphapp.c)
    /// <summary>Morphological gradient: dilation minus the (optionally smoothed) image.</summary>
    /// <param name="hsize">sel width; must be odd; origin implicitly in center</param>
    /// <param name="vsize">sel height</param>
    /// <param name="smoothing">half-width of convolution smoothing filter. The width is (2 * smoothing + 1, so 0 is no-op.</param>
    /// <returns>pixd</returns>
    public Pix MorphGradient(int hsize, int vsize, int smoothing)
    {
        const string func = "pixMorphGradient";
        Pix pixg, pixd;

        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize or vsize < 1");
        if ((hsize & 1) == 0) {
            Log.Warning(func, "horiz sel size must be odd; increasing by 1");
            hsize++;
        }
        if ((vsize & 1) == 0) {
            Log.Warning(func, "vert sel size must be odd; increasing by 1");
            vsize++;
        }

            /* Optionally smooth first to remove noise.
             * If smoothing is 0, just get a copy */
        pixg = BlockconvGray(null, smoothing, smoothing);

            /* This gives approximately the gradient of a transition */
        pixd = pixg.DilateGray(hsize, vsize);
        SubtractGray(pixd, pixd, pixg);
        return pixd;
    }

    /*-----------------------------------------------------------------*
     *                       Centroid of component                     *
     *-----------------------------------------------------------------*/
    // leptonica: pixCentroid (morphapp.c)
    /// <summary>Centroid of the foreground pixels of a 1 bpp image, or of the gray values of an 8 bpp image.</summary>
    /// <param name="centtab">[optional] table for finding centroids; can be null</param>
    /// <param name="sumtab">[optional] table for finding pixel sums; can be null</param>
    /// <param name="xave">x coordinate of centroid, relative to the UL corner of the pix</param>
    /// <param name="yave">y coordinate of centroid, relative to the UL corner of the pix</param>
    /// <remarks>
    /// (1) The sum and centroid tables are only used for 1 bpp.
    /// (2) Any table not passed in will be made internally and destroyed
    ///     after use.
    /// </remarks>
    public void Centroid(int[]? centtab, int[]? sumtab, out float xave, out float yave)
    {
        const string func = "pixCentroid";
        int w, h, d, i, j, wpl, pixsum, rowsum, val;
        float xsum, ysum;
        uint word;
        byte b;
        int[]? ctab, stab;

        xave = yave = 0.0f;
        GetDimensions(out w, out h, out d);
        if (d != 1 && d != 8)
            throw Lept.Arg(func, "pix not 1 or 8 bpp");

        ctab = centtab;
        stab = sumtab;
        if (d == 1) {
            SetPadBits(0);
            if (centtab == null)
                ctab = MakePixelCentroidTab8();
            if (sumtab == null)
                stab = MakePixelSumTab8();
        }

        uint[] data = Data;
        wpl = Wpl;
        xsum = ysum = 0.0f;
        pixsum = 0;
        if (d == 1) {
            for (i = 0; i < h; i++) {
                    /* The body of this loop computes the sum of the set
                     * (1) bits on this row, weighted by their distance
                     * from the left edge of pix, and accumulates that into
                     * xsum; it accumulates their distance from the top
                     * edge of pix into ysum, and their total count into
                     * pixsum.  It's equivalent to
                     * for (j = 0; j < w; j++) {
                     *     if (GET_DATA_BIT(line, j)) {
                     *         xsum += j;
                     *         ysum += i;
                     *         pixsum++;
                     *     }
                     * }
                     */
                int line = wpl * i;
                rowsum = 0;
                for (j = 0; j < wpl; j++) {
                    word = data[line + j];
                    if (word != 0) {
                        b = (byte)(word & 0xff);
                        rowsum += stab![b];
                        xsum += ctab![b] + (j * 32 + 24) * stab[b];
                        b = (byte)((word >> 8) & 0xff);
                        rowsum += stab[b];
                        xsum += ctab[b] + (j * 32 + 16) * stab[b];
                        b = (byte)((word >> 16) & 0xff);
                        rowsum += stab[b];
                        xsum += ctab[b] + (j * 32 + 8) * stab[b];
                        b = (byte)((word >> 24) & 0xff);
                        rowsum += stab[b];
                        xsum += ctab[b] + j * 32 * stab[b];
                    }
                }
                pixsum += rowsum;
                ysum += rowsum * i;
            }
            if (pixsum == 0) {
                Log.Warning(func, "no ON pixels in pix");
            } else {
                xave = xsum / (float)pixsum;
                yave = ysum / (float)pixsum;
            }
        } else {  /* d == 8 */
            for (i = 0; i < h; i++) {
                ReadOnlySpan<uint> line = data.AsSpan(wpl * i, wpl);
                for (j = 0; j < w; j++) {
                    val = ArrayAccess.GetDataByte(line, j);
                    xsum += val * j;
                    ysum += val * i;
                    pixsum += val;
                }
            }
            if (pixsum == 0) {
                Log.Warning(func, "all pixels are 0");
            } else {
                xave = xsum / (float)pixsum;
                yave = ysum / (float)pixsum;
            }
        }
    }
}

public sealed partial class Pixa
{
    // leptonica: pixaMorphSequenceByComponent (morphapp.c)
    /// <summary>Applies the morph sequence to each component of the pixa separately.</summary>
    /// <param name="sequence">string specifying sequence</param>
    /// <param name="minw">min width to consider; use 0 or 1 for any width</param>
    /// <param name="minh">min height to consider; use 0 or 1 for any height</param>
    /// <returns>pixad</returns>
    /// <remarks>
    /// (1) See pixMorphSequence() for composing operation sequences.
    /// (2) This operates separately on each c.c. in the input pixa.
    /// (3) You can specify that the width and/or height must equal
    ///     or exceed a minimum size for the operation to take place.
    /// (4) The input pixa should have a boxa giving the locations
    ///     of the pix components.
    /// </remarks>
    public Pixa MorphSequenceByComponent(string sequence, int minw, int minh)
    {
        const string func = "pixaMorphSequenceByComponent";
        int n, i, w, h, d;
        Box? box;
        Pix pix1, pix2;
        Pixa pixad;

        if ((n = Count) == 0)
            throw Lept.Arg(func, "no pix in pixas");
        if (n != BoxaCount)
            Log.Warning(func, "boxa size != n");
        GetPixDimensions(0, out _, out _, out d);
        if (d != 1)
            throw Lept.Arg(func, "depth not 1 bpp");

        if (minw <= 0) minw = 1;
        if (minh <= 0) minh = 1;

        pixad = Create(n);
        for (i = 0; i < n; i++) {
            GetPixDimensions(i, out w, out h, out _);
            if (w >= minw && h >= minh) {
                pix1 = GetPix(i, L.L_CLONE);
                pix2 = pix1.MorphCompSequence(sequence, 0);
                pixad.AddPix(pix2, L.L_INSERT);
                box = GetBox(i, L.L_COPY);
                pixad.AddBox(box!, L.L_INSERT);
            }
        }

        return pixad;
    }

    /*------------------------------------------------------------------------*
     *   Extension of pixa by iterative erosion or dilation (and by scaling)  *
     *------------------------------------------------------------------------*/
    // leptonica: pixaExtendByMorph (morphapp.c)
    /// <summary>Dilates or erodes every pix iteratively and collects the results.</summary>
    /// <param name="type">L_MORPH_DILATE, L_MORPH_ERODE</param>
    /// <param name="niters"></param>
    /// <param name="sel">used for dilation, erosion; uses 2x2 if null</param>
    /// <param name="include">1 to include a copy of the input pixas in pixad; 0 to omit</param>
    /// <returns>pixad with derived pix, using all iterations</returns>
    /// <remarks>
    /// (1) This dilates or erodes every pix in %pixas, iteratively,
    ///     using the input Sel (or, if null, a 2x2 Sel by default),
    ///     and puts the results in %pixad.
    /// (2) If %niters &lt;= 0, this is a no-op; it returns a clone of pixas.
    /// (3) If %include == 1, the output %pixad contains all the pix
    ///     in %pixas.  Otherwise, it doesn't, but pixaJoin() can be
    ///     used later to join pixas with pixad.
    /// </remarks>
    public Pixa ExtendByMorph(int type, int niters, Sel? sel, int include)
    {
        const string func = "pixaExtendByMorph";
        int maxdepth, i, j, n;
        bool same;
        Pix pix0, pix1, pix2;
        Sel selt;
        Pixa pixad;

        if (niters <= 0) {
            Log.Info(func, $"niters = {niters}; nothing to do");
            return Copy(L.L_CLONE);
        }
        if (type != L.L_MORPH_DILATE && type != L.L_MORPH_ERODE)
            throw Lept.Arg(func, "invalid type");
        GetDepthInfo(out maxdepth, out same);
        if (maxdepth > 1)
            throw Lept.Arg(func, "some pix have bpp > 1");

        if (sel == null)
            selt = Sel.CreateBrick(2, 2, 0, 0, L.SEL_HIT);  /* default */
        else
            selt = sel.Copy();
        n = Count;
        pixad = Create(n * niters);
        for (i = 0; i < n; i++) {
            pix1 = GetPix(i, L.L_CLONE);
            if (include != 0) pixad.AddPix(pix1, L.L_COPY);
            pix0 = pix1;  /* need to keep the handle to destroy the clone */
            for (j = 0; j < niters; j++) {
                if (type == L.L_MORPH_DILATE) {
                    pix2 = Pix.Dilate(null, pix1, selt);
                } else {  /* L_MORPH_ERODE */
                    pix2 = Pix.Erode(null, pix1, selt);
                }
                pixad.AddPix(pix2, L.L_INSERT);
                pix1 = pix2;  /* owned by pixad; do not destroy */
            }
        }

        return pixad;
    }

    // leptonica: pixaExtendByScaling (morphapp.c)
    /// <summary>Scales every pix by each factor in nasc and collects the results.</summary>
    /// <param name="nasc">numa of scaling factors</param>
    /// <param name="type">L_HORIZ, L_VERT, L_BOTH_DIRECTIONS</param>
    /// <param name="include">1 to include a copy of the input pixas in pixad; 0 to omit</param>
    /// <returns>pixad with derived pix, using all scalings</returns>
    /// <remarks>
    /// (1) This scales every pix in %pixas by each factor in %nasc.
    ///     and puts the results in %pixad.
    /// (2) If %include == 1, the output %pixad contains all the pix
    ///     in %pixas.  Otherwise, it doesn't, but pixaJoin() can be
    ///     used later to join pixas with pixad.
    /// </remarks>
    public Pixa ExtendByScaling(Numa nasc, int type, int include)
    {
        const string func = "pixaExtendByScaling";
        int i, j, n, nsc, w, h, scalew, scaleh;
        float scalefact;
        Pix pix1, pix2;
        Pixa pixad;

        if (nasc.Count == 0)
            throw Lept.Arg(func, "nasc undefined or empty");
        if (type != L.L_HORIZ && type != L.L_VERT && type != L.L_BOTH_DIRECTIONS)
            throw Lept.Arg(func, "invalid type");

        n = Count;
        nsc = nasc.Count;
        pixad = Create(n * (nsc + 1));
        for (i = 0; i < n; i++) {
            pix1 = GetPix(i, L.L_CLONE);
            if (include != 0) pixad.AddPix(pix1, L.L_COPY);
            pix1.GetDimensions(out w, out h, out _);
            for (j = 0; j < nsc; j++) {
                scalefact = nasc.GetFValue(j);
                scalew = w;
                scaleh = h;
                if (type == L.L_HORIZ || type == L.L_BOTH_DIRECTIONS)
                    scalew = (int)(w * scalefact);
                if (type == L.L_VERT || type == L.L_BOTH_DIRECTIONS)
                    scaleh = (int)(h * scalefact);
                pix2 = pix1.ScaleToSize(scalew, scaleh);
                pixad.AddPix(pix2, L.L_INSERT);
            }
        }
        return pixad;
    }

    /*-----------------------------------------------------------------*
     *                       Centroid of component                     *
     *-----------------------------------------------------------------*/
    // leptonica: pixaCentroids (morphapp.c)
    /// <summary>Centroids of the components, relative to the UL corner of each pix.</summary>
    /// <returns>pta of centroids relative to the UL corner of each pix</returns>
    /// <remarks>
    /// (1) An error message is returned if any pix has something other
    ///     than 1 bpp or 8 bpp depth, and the centroid from that pix
    ///     is saved as (0, 0).
    /// </remarks>
    public Pta Centroids()
    {
        const string func = "pixaCentroids";
        int i, n;
        int[] centtab, sumtab;
        float x, y;
        Pix pix;
        Pta pta;

        if ((n = Count) == 0)
            throw Lept.Arg(func, "no pix in pixa");

        pta = Pta.Create(n);
        centtab = Pix.MakePixelCentroidTab8();
        sumtab = Pix.MakePixelSumTab8();

        for (i = 0; i < n; i++) {
            pix = GetPix(i, L.L_CLONE);
            try {
                pix.Centroid(centtab, sumtab, out x, out y);
            } catch (ArgumentException) {
                Log.Error(func, $"centroid failure for pix {i}");
                x = y = 0.0f;
            }
            pta.AddPt(x, y);
        }

        return pta;
    }
}
