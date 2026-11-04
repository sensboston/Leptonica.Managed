using System;

namespace Leptonica;

/*
 *  morph.c: Binary morphological operations with SELs.
 *
 *     Generic binary morphological ops implemented with rasterop
 *         Pix.Dilate, Erode, HMT, Open, Close, CloseSafe,
 *         OpenGeneralized, CloseGeneralized
 *
 *     Binary morphological (raster) ops with brick Sels
 *         Pix.DilateBrick, ErodeBrick, OpenBrick, CloseBrick, CloseSafeBrick
 *
 *     Binary composed morphological (raster) ops with brick Sels
 *         Pix.SelectComposableSels, SelectComposableSizes,
 *         DilateCompBrick, ErodeCompBrick, OpenCompBrick, CloseCompBrick,
 *         CloseSafeCompBrick
 *
 *     Functions associated with boundary conditions
 *         Pix.ResetMorphBoundaryCondition, GetMorphBorderPixelColor
 *
 *     Static helpers for arg processing
 *         ProcessMorphArgs1, ProcessMorphArgs2
 *
 *  A global constant MORPH_BC is used to set the boundary conditions
 *  for rasterop-based binary morphology.  MORPH_BC is set by default to
 *  ASYMMETRIC_MORPH_BC for a non-symmetric convention for boundary pixels
 *  in dilation and erosion: all pixels outside the image are assumed to be
 *  OFF for both dilation and erosion.  To use a symmetric definition, see
 *  the comments in Pix.Erode() and reset MORPH_BC to SYMMETRIC_MORPH_BC
 *  using Pix.ResetMorphBoundaryCondition().
 *
 *  Boundary artifacts are possible in closing when the non-symmetric
 *  boundary conditions are used, because foreground pixels very close
 *  to the edge can be removed.  This can be avoided by using either
 *  the symmetric boundary conditions or the function Pix.CloseSafe(),
 *  which adds a border before the operation and removes it afterwards.
 *
 *  The hit-miss transform (HMT) is the bit-and of 2 erosions:
 *     (erosion of the src by the hits)  &  (erosion of the bit-inverted
 *                                           src by the misses)
 *
 *  The 'generalized opening' is an HMT followed by a dilation that uses
 *  only the hits of the hit-miss Sel.
 *  The 'generalized closing' is a dilation (again, with the hits
 *  of a hit-miss Sel), followed by the HMT.
 *  Both of these 'generalized' functions are idempotent.
 *
 *  Error returns of the C code (NULL / 1) are thrown as exceptions, so the cleanup of intermediate Sels and Pix
 *  before an error return is not needed.
 */

public static partial class L
{
    /* Boundary conditions for rasterop-based binary morphology (morph.h) */
    /// <summary>Symmetric boundary condition: erosion treats pixels outside the image as ON and dilation treats them as OFF, so the two operations are dual.</summary>
    public const int SYMMETRIC_MORPH_BC = 0;
    /// <summary>Asymmetric boundary condition (the default): all pixels outside the image are treated as OFF for both dilation and erosion.</summary>
    public const int ASYMMETRIC_MORPH_BC = 1;

    /* Morphological operation types, used for border pixel color (morph.h) */
    /// <summary>Morphological operation type: dilation.</summary>
    public const int L_MORPH_DILATE = 1;
    /// <summary>Morphological operation type: erosion.</summary>
    public const int L_MORPH_ERODE = 2;
}

public sealed partial class Pix
{
    /* Global constant; initialized here.  Most callers should change it
     * using ResetMorphBoundaryCondition(). */
    internal static int MORPH_BC = L.ASYMMETRIC_MORPH_BC;

    /* We accept this cost in extra rasterops for decomposing exactly. */
    private const int ACCEPTABLE_COST = 5;

    /*-----------------------------------------------------------------*
     *    Generic binary morphological ops implemented with rasterop   *
     *-----------------------------------------------------------------*/
    // leptonica: pixDilate (morph.c)
    /// <summary>Dilates src using hits in the Sel.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="sel"></param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This dilates src using hits in Sel.
    /// (2) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (3) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.Dilate(null, pixs, ...);
    ///     (b) Pix.Dilate(pixs, pixs, ...);
    ///     (c) Pix.Dilate(pixd, pixs, ...);
    /// (4) The size of the result is determined by pixs.
    /// </remarks>
    public static Pix Dilate(Pix? pixd, Pix pixs, Sel sel)
    {
        int i, j, w, h, sx, sy, cx, cy, seldata;
        Pix pixt;

        pixd = ProcessMorphArgs1(pixd, pixs, sel, out pixt);

        pixs.GetDimensions(out w, out h, out _);
        sel.GetParameters(out sy, out sx, out cy, out cx);
        pixd.ClearAll();
        for (i = 0; i < sy; i++) {
            for (j = 0; j < sx; j++) {
                seldata = sel.Data[i][j];
                if (seldata == 1) {   /* src | dst */
                    pixd.Rasterop(j - cx, i - cy, w, h, L.PIX_SRC | L.PIX_DST,
                                  pixt, 0, 0);
                }
            }
        }

        return pixd;
    }

    // leptonica: pixErode (morph.c)
    /// <summary>Erodes src using hits in the Sel.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="sel"></param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This erodes src using hits in Sel.
    /// (2) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (3) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.Erode(null, pixs, ...);
    ///     (b) Pix.Erode(pixs, pixs, ...);
    ///     (c) Pix.Erode(pixd, pixs, ...);
    /// (4) The size of the result is determined by pixs.
    /// </remarks>
    public static Pix Erode(Pix? pixd, Pix pixs, Sel sel)
    {
        int i, j, w, h, sx, sy, cx, cy, seldata;
        int xp, yp, xn, yn;
        Pix pixt;

        pixd = ProcessMorphArgs1(pixd, pixs, sel, out pixt);

        pixs.GetDimensions(out w, out h, out _);
        sel.GetParameters(out sy, out sx, out cy, out cx);
        pixd.SetAll();
        for (i = 0; i < sy; i++) {
            for (j = 0; j < sx; j++) {
                seldata = sel.Data[i][j];
                if (seldata == 1) {   /* src & dst */
                    pixd.Rasterop(cx - j, cy - i, w, h, L.PIX_SRC & L.PIX_DST,
                                  pixt, 0, 0);
                }
            }
        }

            /* Clear near edges.  We do this for the asymmetric boundary
             * condition convention that implements erosion assuming all
             * pixels surrounding the image are OFF.  If you use a
             * use a symmetric b.c. convention, where the erosion is
             * implemented assuming pixels surrounding the image
             * are ON, these operations are omitted.  */
        if (MORPH_BC == L.ASYMMETRIC_MORPH_BC) {
            sel.FindMaxTranslations(out xp, out yp, out xn, out yn);
            if (xp > 0)
                pixd.Rasterop(0, 0, xp, h, L.PIX_CLR, null, 0, 0);
            if (xn > 0)
                pixd.Rasterop(w - xn, 0, xn, h, L.PIX_CLR, null, 0, 0);
            if (yp > 0)
                pixd.Rasterop(0, 0, w, yp, L.PIX_CLR, null, 0, 0);
            if (yn > 0)
                pixd.Rasterop(0, h - yn, w, yn, L.PIX_CLR, null, 0, 0);
        }

        return pixd;
    }

    // leptonica: pixHMT (morph.c)
    /// <summary>Hit-miss transform.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="sel"></param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) The hit-miss transform erodes the src, using both hits
    ///     and misses in the Sel.  It ANDs the shifted src for hits
    ///     and ANDs the inverted shifted src for misses.
    /// (2) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (3) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.HMT(null, pixs, ...);
    ///     (b) Pix.HMT(pixs, pixs, ...);
    ///     (c) Pix.HMT(pixd, pixs, ...);
    /// (4) The size of the result is determined by pixs.
    /// </remarks>
    public static Pix HMT(Pix? pixd, Pix pixs, Sel sel)
    {
        int i, j, w, h, sx, sy, cx, cy, seldata;
        bool firstrasterop;
        int xp, yp, xn, yn;
        Pix pixt;

        pixd = ProcessMorphArgs1(pixd, pixs, sel, out pixt);

        pixs.GetDimensions(out w, out h, out _);
        sel.GetParameters(out sy, out sx, out cy, out cx);
        firstrasterop = true;
        for (i = 0; i < sy; i++) {
            for (j = 0; j < sx; j++) {
                seldata = sel.Data[i][j];
                if (seldata == 1) {  /* hit */
                    if (firstrasterop) {  /* src only */
                        pixd.ClearAll();
                        pixd.Rasterop(cx - j, cy - i, w, h, L.PIX_SRC,
                                      pixt, 0, 0);
                        firstrasterop = false;
                    } else {   /* src & dst */
                        pixd.Rasterop(cx - j, cy - i, w, h, L.PIX_SRC & L.PIX_DST,
                                      pixt, 0, 0);
                    }
                } else if (seldata == 2) {  /* miss */
                    if (firstrasterop) {  /* ~src only */
                        pixd.SetAll();
                        pixd.Rasterop(cx - j, cy - i, w, h, L.PIX_NOT(L.PIX_SRC),
                                      pixt, 0, 0);
                        firstrasterop = false;
                    } else {  /* ~src & dst */
                        pixd.Rasterop(cx - j, cy - i, w, h,
                                      L.PIX_NOT(L.PIX_SRC) & L.PIX_DST,
                                      pixt, 0, 0);
                    }
                }
            }
        }

            /* Clear near edges */
        sel.FindMaxTranslations(out xp, out yp, out xn, out yn);
        if (xp > 0)
            pixd.Rasterop(0, 0, xp, h, L.PIX_CLR, null, 0, 0);
        if (xn > 0)
            pixd.Rasterop(w - xn, 0, xn, h, L.PIX_CLR, null, 0, 0);
        if (yp > 0)
            pixd.Rasterop(0, 0, w, yp, L.PIX_CLR, null, 0, 0);
        if (yn > 0)
            pixd.Rasterop(0, h - yn, w, yn, L.PIX_CLR, null, 0, 0);

        return pixd;
    }

    // leptonica: pixOpen (morph.c)
    /// <summary>Generic morphological opening, using hits in the Sel.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="sel"></param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Generic morphological opening, using hits in the Sel.
    /// (2) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (3) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.Open(null, pixs, ...);
    ///     (b) Pix.Open(pixs, pixs, ...);
    ///     (c) Pix.Open(pixd, pixs, ...);
    /// (4) The size of the result is determined by pixs.
    /// </remarks>
    public static Pix Open(Pix? pixd, Pix pixs, Sel sel)
    {
        Pix pixt;

        pixd = ProcessMorphArgs2(pixd, pixs, sel);

        pixt = Erode(null, pixs, sel);
        Dilate(pixd, pixt, sel);

        return pixd;
    }

    // leptonica: pixClose (morph.c)
    /// <summary>Generic morphological closing, using hits in the Sel.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="sel"></param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Generic morphological closing, using hits in the Sel.
    /// (2) This implementation is a strict dual of the opening if
    ///     symmetric boundary conditions are used (see notes at top
    ///     of this file).
    /// (3) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (4) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.Close(null, pixs, ...);
    ///     (b) Pix.Close(pixs, pixs, ...);
    ///     (c) Pix.Close(pixd, pixs, ...);
    /// (5) The size of the result is determined by pixs.
    /// </remarks>
    public static Pix Close(Pix? pixd, Pix pixs, Sel sel)
    {
        Pix pixt;

        pixd = ProcessMorphArgs2(pixd, pixs, sel);

        pixt = Dilate(null, pixs, sel);
        Erode(pixd, pixt, sel);

        return pixd;
    }

    // leptonica: pixCloseSafe (morph.c)
    /// <summary>Generic morphological closing that avoids boundary loss.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="sel"></param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Generic morphological closing, using hits in the Sel.
    /// (2) If non-symmetric boundary conditions are used, this
    ///     function adds a border of OFF pixels that is of
    ///     sufficient size to avoid losing pixels from the dilation,
    ///     and it removes the border after the operation is finished.
    ///     It thus enforces a correct extensive result for closing.
    /// (3) If symmetric b.c. are used, it is not necessary to add
    ///     and remove this border.
    /// (4) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (5) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.CloseSafe(null, pixs, ...);
    ///     (b) Pix.CloseSafe(pixs, pixs, ...);
    ///     (c) Pix.CloseSafe(pixd, pixs, ...);
    /// (6) The size of the result is determined by pixs.
    /// </remarks>
    public static Pix CloseSafe(Pix? pixd, Pix pixs, Sel sel)
    {
        const string func = "pixCloseSafe";
        int xp, yp, xn, yn, xmax, xbord;
        Pix pixt1, pixt2;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");

            /* Symmetric b.c. handles correctly without added pixels */
        if (MORPH_BC == L.SYMMETRIC_MORPH_BC)
            return Close(pixd, pixs, sel);

        sel.FindMaxTranslations(out xp, out yp, out xn, out yn);
        xmax = Math.Max(xp, xn);
        xbord = 32 * ((xmax + 31) / 32);  /* full 32 bit words */

        pixt1 = pixs.AddBorderGeneral(xbord, xbord, yp, yn, 0);
        Close(pixt1, pixt1, sel);
        pixt2 = pixt1.RemoveBorderGeneral(xbord, xbord, yp, yn);

        if (pixd == null)
            return pixt2;

        Copy(pixd, pixt2);
        return pixd;
    }

    // leptonica: pixOpenGeneralized (morph.c)
    /// <summary>Generalized morphological opening, using both hits and misses in the Sel.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="sel"></param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Generalized morphological opening, using both hits and
    ///     misses in the Sel.
    /// (2) This does a hit-miss transform, followed by a dilation
    ///     using the hits.
    /// (3) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (4) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.OpenGeneralized(null, pixs, ...);
    ///     (b) Pix.OpenGeneralized(pixs, pixs, ...);
    ///     (c) Pix.OpenGeneralized(pixd, pixs, ...);
    /// (5) The size of the result is determined by pixs.
    /// </remarks>
    public static Pix OpenGeneralized(Pix? pixd, Pix pixs, Sel sel)
    {
        Pix pixt;

        pixd = ProcessMorphArgs2(pixd, pixs, sel);

        pixt = HMT(null, pixs, sel);
        Dilate(pixd, pixt, sel);
        return pixd;
    }

    // leptonica: pixCloseGeneralized (morph.c)
    /// <summary>Generalized morphological closing, using both hits and misses in the Sel.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="sel"></param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Generalized morphological closing, using both hits and
    ///     misses in the Sel.
    /// (2) This does a dilation using the hits, followed by a
    ///     hit-miss transform.
    /// (3) This operation is a dual of the generalized opening.
    /// (4) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (5) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.CloseGeneralized(null, pixs, ...);
    ///     (b) Pix.CloseGeneralized(pixs, pixs, ...);
    ///     (c) Pix.CloseGeneralized(pixd, pixs, ...);
    /// (6) The size of the result is determined by pixs.
    /// </remarks>
    public static Pix CloseGeneralized(Pix? pixd, Pix pixs, Sel sel)
    {
        Pix pixt;

        pixd = ProcessMorphArgs2(pixd, pixs, sel);

        pixt = Dilate(null, pixs, sel);
        HMT(pixd, pixt, sel);

        return pixd;
    }

    /*-----------------------------------------------------------------*
     *          Binary morphological (raster) ops with brick Sels      *
     *-----------------------------------------------------------------*/
    // leptonica: pixDilateBrick (morph.c)
    /// <summary>Dilation with a brick Sel (separable if both sizes are &gt; 1).</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="hsize">width of brick Sel</param>
    /// <param name="vsize">height of brick Sel</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Sel is a brick with all elements being hits
    /// (2) The origin is at (x, y) = (hsize/2, vsize/2)
    /// (3) Do separably if both hsize and vsize are &gt; 1.
    /// (4) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (5) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.DilateBrick(null, pixs, ...);
    ///     (b) Pix.DilateBrick(pixs, pixs, ...);
    ///     (c) Pix.DilateBrick(pixd, pixs, ...);
    /// (6) The size of the result is determined by pixs.
    /// </remarks>
    public static Pix DilateBrick(Pix? pixd, Pix pixs, int hsize, int vsize)
    {
        const string func = "pixDilateBrick";
        Pix pixt;
        Sel sel, selh, selv;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize and vsize not >= 1");

        if (hsize == 1 && vsize == 1)
            return Copy(pixd, pixs);
        /* For sizeable bricks with exact basic DWA sels, use the packed-word
         * implementation instead of issuing one full-image rasterop per hit. */
        if ((hsize >= 5 || vsize >= 5 || (hsize >= 3 && vsize >= 3)) &&
            (hsize == 1 || BrickNameOrNull(hsize, 1) != null) &&
            (vsize == 1 || BrickNameOrNull(1, vsize) != null))
            return DilateBrickDwa(pixd, pixs, hsize, vsize);

        if (hsize == 1 || vsize == 1) {  /* no intermediate result */
            sel = Sel.CreateBrick(vsize, hsize, vsize / 2, hsize / 2, L.SEL_HIT);
            pixd = Dilate(pixd, pixs, sel);
        } else {
            selh = Sel.CreateBrick(1, hsize, 0, hsize / 2, L.SEL_HIT);
            selv = Sel.CreateBrick(vsize, 1, vsize / 2, 0, L.SEL_HIT);
            pixt = Dilate(null, pixs, selh);
            pixd = Dilate(pixd, pixt, selv);
        }

        return pixd;
    }

    // leptonica: pixErodeBrick (morph.c)
    /// <summary>Erosion with a brick Sel (separable if both sizes are &gt; 1).</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="hsize">width of brick Sel</param>
    /// <param name="vsize">height of brick Sel</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Sel is a brick with all elements being hits
    /// (2) The origin is at (x, y) = (hsize/2, vsize/2)
    /// (3) Do separably if both hsize and vsize are &gt; 1.
    /// (4) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (5) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.ErodeBrick(null, pixs, ...);
    ///     (b) Pix.ErodeBrick(pixs, pixs, ...);
    ///     (c) Pix.ErodeBrick(pixd, pixs, ...);
    /// (6) The size of the result is determined by pixs.
    /// </remarks>
    public static Pix ErodeBrick(Pix? pixd, Pix pixs, int hsize, int vsize)
    {
        const string func = "pixErodeBrick";
        Pix pixt;
        Sel sel, selh, selv;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize and vsize not >= 1");

        if (hsize == 1 && vsize == 1)
            return Copy(pixd, pixs);
        /* For sizeable bricks with exact basic DWA sels, use the packed-word
         * implementation instead of issuing one full-image rasterop per hit. */
        if ((hsize >= 5 || vsize >= 5 || (hsize >= 3 && vsize >= 3)) &&
            (hsize == 1 || BrickNameOrNull(hsize, 1) != null) &&
            (vsize == 1 || BrickNameOrNull(1, vsize) != null))
            return ErodeBrickDwa(pixd, pixs, hsize, vsize);

        if (hsize == 1 || vsize == 1) {  /* no intermediate result */
            sel = Sel.CreateBrick(vsize, hsize, vsize / 2, hsize / 2, L.SEL_HIT);
            pixd = Erode(pixd, pixs, sel);
        } else {
            selh = Sel.CreateBrick(1, hsize, 0, hsize / 2, L.SEL_HIT);
            selv = Sel.CreateBrick(vsize, 1, vsize / 2, 0, L.SEL_HIT);
            pixt = Erode(null, pixs, selh);
            pixd = Erode(pixd, pixt, selv);
        }

        return pixd;
    }

    // leptonica: pixOpenBrick (morph.c)
    /// <summary>Opening with a brick Sel (separable if both sizes are &gt; 1).</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="hsize">width of brick Sel</param>
    /// <param name="vsize">height of brick Sel</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Sel is a brick with all elements being hits
    /// (2) The origin is at (x, y) = (hsize/2, vsize/2)
    /// (3) Do separably if both hsize and vsize are &gt; 1.
    /// (4) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (5) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.OpenBrick(null, pixs, ...);
    ///     (b) Pix.OpenBrick(pixs, pixs, ...);
    ///     (c) Pix.OpenBrick(pixd, pixs, ...);
    /// (6) The size of the result is determined by pixs.
    /// </remarks>
    public static Pix OpenBrick(Pix? pixd, Pix pixs, int hsize, int vsize)
    {
        const string func = "pixOpenBrick";
        Pix pixt;
        Sel sel, selh, selv;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize and vsize not >= 1");

        if (hsize == 1 && vsize == 1)
            return Copy(pixd, pixs);
        /* For sizeable bricks with exact basic DWA sels, use the packed-word
         * implementation instead of issuing one full-image rasterop per hit. */
        if ((hsize >= 5 || vsize >= 5 || (hsize >= 3 && vsize >= 3)) &&
            (hsize == 1 || BrickNameOrNull(hsize, 1) != null) &&
            (vsize == 1 || BrickNameOrNull(1, vsize) != null))
            return OpenBrickDwa(pixd, pixs, hsize, vsize);

        if (hsize == 1 || vsize == 1) {  /* no intermediate result */
            sel = Sel.CreateBrick(vsize, hsize, vsize / 2, hsize / 2, L.SEL_HIT);
            pixd = Open(pixd, pixs, sel);
        } else {  /* do separably */
            selh = Sel.CreateBrick(1, hsize, 0, hsize / 2, L.SEL_HIT);
            selv = Sel.CreateBrick(vsize, 1, vsize / 2, 0, L.SEL_HIT);
            pixt = Erode(null, pixs, selh);
            pixd = Erode(pixd, pixt, selv);
            Dilate(pixt, pixd, selh);
            Dilate(pixd, pixt, selv);
        }

        return pixd;
    }

    // leptonica: pixCloseBrick (morph.c)
    /// <summary>Closing with a brick Sel (separable if both sizes are &gt; 1).</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="hsize">width of brick Sel</param>
    /// <param name="vsize">height of brick Sel</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Sel is a brick with all elements being hits
    /// (2) The origin is at (x, y) = (hsize/2, vsize/2)
    /// (3) Do separably if both hsize and vsize are &gt; 1.
    /// (4) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (5) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.CloseBrick(null, pixs, ...);
    ///     (b) Pix.CloseBrick(pixs, pixs, ...);
    ///     (c) Pix.CloseBrick(pixd, pixs, ...);
    /// (6) The size of the result is determined by pixs.
    /// </remarks>
    public static Pix CloseBrick(Pix? pixd, Pix pixs, int hsize, int vsize)
    {
        const string func = "pixCloseBrick";
        Pix pixt;
        Sel sel, selh, selv;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize and vsize not >= 1");

        if (hsize == 1 && vsize == 1)
            return Copy(pixd, pixs);
        if (hsize == 1 || vsize == 1) {  /* no intermediate result */
            sel = Sel.CreateBrick(vsize, hsize, vsize / 2, hsize / 2, L.SEL_HIT);
            pixd = Close(pixd, pixs, sel);
        } else {  /* do separably */
            selh = Sel.CreateBrick(1, hsize, 0, hsize / 2, L.SEL_HIT);
            selv = Sel.CreateBrick(vsize, 1, vsize / 2, 0, L.SEL_HIT);
            pixt = Dilate(null, pixs, selh);
            pixd = Dilate(pixd, pixt, selv);
            Erode(pixt, pixd, selh);
            Erode(pixd, pixt, selv);
        }

        return pixd;
    }

    // leptonica: pixCloseSafeBrick (morph.c)
    /// <summary>Closing with a brick Sel, using an added border so there are no boundary effects.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="hsize">width of brick Sel</param>
    /// <param name="vsize">height of brick Sel</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Sel is a brick with all elements being hits
    /// (2) The origin is at (x, y) = (hsize/2, vsize/2)
    /// (3) Do separably if both hsize and vsize are &gt; 1.
    /// (4) Safe closing adds a border of 0 pixels, of sufficient size so
    ///     that all pixels in input image are processed within
    ///     32-bit words in the expanded image.  As a result, there is
    ///     no special processing for pixels near the boundary, and there
    ///     are no boundary effects.  The border is removed at the end.
    /// (5) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (6) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.CloseBrick(null, pixs, ...);
    ///     (b) Pix.CloseBrick(pixs, pixs, ...);
    ///     (c) Pix.CloseBrick(pixd, pixs, ...);
    /// (7) The size of the result is determined by pixs.
    /// </remarks>
    public static Pix CloseSafeBrick(Pix? pixd, Pix pixs, int hsize, int vsize)
    {
        const string func = "pixCloseSafeBrick";
        int maxtrans, bordsize;
        Pix pixsb, pixt, pixdb;
        Sel sel, selh, selv;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize and vsize not >= 1");

        if (hsize == 1 && vsize == 1)
            return Copy(pixd, pixs);

        /* Safe DWA closing uses the same expanded zero-border semantics and
         * avoids one rasterop per hit for large supported brick Sels. */
        if (MORPH_BC == L.ASYMMETRIC_MORPH_BC &&
            (hsize >= 5 || vsize >= 5 || (hsize >= 3 && vsize >= 3)) &&
            (hsize == 1 || BrickNameOrNull(hsize, 1) != null) &&
            (vsize == 1 || BrickNameOrNull(1, vsize) != null))
            return CloseBrickDwa(pixd, pixs, hsize, vsize);

            /* Symmetric b.c. handles correctly without added pixels */
        if (MORPH_BC == L.SYMMETRIC_MORPH_BC)
            return CloseBrick(pixd, pixs, hsize, vsize);

        maxtrans = Math.Max(hsize / 2, vsize / 2);
        bordsize = 32 * ((maxtrans + 31) / 32);  /* full 32 bit words */
        pixsb = pixs.AddBorder(bordsize, 0);

        if (hsize == 1 || vsize == 1) {  /* no intermediate result */
            sel = Sel.CreateBrick(vsize, hsize, vsize / 2, hsize / 2, L.SEL_HIT);
            pixdb = Close(null, pixsb, sel);
        } else {  /* do separably */
            selh = Sel.CreateBrick(1, hsize, 0, hsize / 2, L.SEL_HIT);
            selv = Sel.CreateBrick(vsize, 1, vsize / 2, 0, L.SEL_HIT);
            pixt = Dilate(null, pixsb, selh);
            pixdb = Dilate(null, pixt, selv);
            Erode(pixt, pixdb, selh);
            Erode(pixdb, pixt, selv);
        }

        pixt = pixdb.RemoveBorder(bordsize);

        if (pixd == null) {
            pixd = pixt;
        } else {
            Copy(pixd, pixt);
        }
        return pixd;
    }

    /*-----------------------------------------------------------------*
     *     Binary composed morphological (raster) ops with brick Sels  *
     *-----------------------------------------------------------------*/
    // leptonica: selectComposableSels (morph.c)
    /// <summary>Makes the two composable Sels (contiguous brick and comb) for a given size.</summary>
    /// <param name="size">of composed sel</param>
    /// <param name="direction">L_HORIZ, L_VERT</param>
    /// <param name="sel1">contiguous sel</param>
    /// <param name="sel2">comb sel</param>
    /// <remarks>
    /// (1) When using composable Sels, where the original Sel is
    ///     decomposed into two, the best you can do in terms
    ///     of reducing the computation is by a factor:
    ///
    ///          2 * sqrt(size) / size
    ///
    ///     In practice, you get quite close to this.  E.g.,
    ///
    ///        Sel size     |   Optimum reduction factor
    ///        --------         ------------------------
    ///           36        |          1/3
    ///           64        |          1/4
    ///          144        |          1/6
    ///          256        |          1/8
    /// </remarks>
    public static void SelectComposableSels(int size, int direction,
                                            out Sel sel1, out Sel sel2)
    {
        const string func = "selectComposableSels";
        int factor1, factor2;

        sel1 = null!;
        sel2 = null!;
        if (size < 1 || size > 10000)
            throw Lept.Arg(func, "size < 1 or size > 10000");
        if (direction != Sel.L_HORIZ && direction != Sel.L_VERT)
            throw Lept.Arg(func, "invalid direction");

        SelectComposableSizes(size, out factor1, out factor2);

        if (direction == Sel.L_HORIZ)
            sel1 = Sel.CreateBrick(1, factor1, 0, factor1 / 2, L.SEL_HIT);
        else
            sel1 = Sel.CreateBrick(factor1, 1, factor1 / 2, 0, L.SEL_HIT);
        sel2 = Sel.CreateComb(factor1, factor2, direction);
    }

    // leptonica: selectComposableSizes (morph.c)
    /// <summary>Finds the two factors of a composable decomposition of a Sel size.</summary>
    /// <param name="size">of sel to be decomposed</param>
    /// <param name="factor1">larger factor</param>
    /// <param name="factor2">smaller factor</param>
    /// <remarks>
    /// (1) This works for Sel sizes up to 10000, which seems sufficient.
    /// (2) The composable sel size is typically within +- 1 of
    ///     the requested size.  Up to size = 300, the maximum difference
    ///     is +- 2.
    /// (3) We choose an overall cost function where the penalty for
    ///     the size difference between input and actual is 4 times
    ///     the penalty for additional rasterops.
    /// (4) Returned values: factor1 &gt;= factor2
    ///     If size &gt; 1, then factor1 &gt; 1.
    /// </remarks>
    public static void SelectComposableSizes(int size, out int factor1, out int factor2)
    {
        const string func = "selectComposableSizes";
        int i, midval, val1, val2m, val2p;
        int index, prodm, prodp;
        int mincost, totcost, rastcostm, rastcostp, diffm, diffp;
        int[] lowval = new int[256];
        int[] hival = new int[256];
        int[] rastcost = new int[256];  /* excess in sum of sizes (extra rasterops) */
        int[] diff = new int[256];  /* diff between product (sel size) and input size */

        factor1 = factor2 = 0;
        if (size < 1 || size > 10000)
            throw Lept.Arg(func, "size < 1 or size > 10000");

        midval = (int)(Math.Sqrt((double)size) + 0.001);
        if (midval * midval == size) {
            factor1 = factor2 = midval;
            return;
        }

            /* Set up arrays.  For each val1, optimize for lowest diff,
             * and save the rastcost, the diff, and the two factors. */
        for (val1 = midval + 1, i = 0; val1 > 0; val1--, i++) {
            val2m = size / val1;
            val2p = val2m + 1;
            prodm = val1 * val2m;
            prodp = val1 * val2p;
            rastcostm = val1 + val2m - 2 * midval;
            rastcostp = val1 + val2p - 2 * midval;
            diffm = Math.Abs(size - prodm);
            diffp = Math.Abs(size - prodp);
            if (diffm <= diffp) {
                lowval[i] = Math.Min(val1, val2m);
                hival[i] = Math.Max(val1, val2m);
                rastcost[i] = rastcostm;
                diff[i] = diffm;
            } else {
                lowval[i] = Math.Min(val1, val2p);
                hival[i] = Math.Max(val1, val2p);
                rastcost[i] = rastcostp;
                diff[i] = diffp;
            }
        }

            /* Choose the optimum factors; use cost ratio 4 on diff */
        mincost = 10000;
        index = 1;  /* unimportant initial value */
        for (i = 0; i < midval + 1; i++) {
            if (diff[i] == 0 && rastcost[i] < ACCEPTABLE_COST) {
                factor1 = hival[i];
                factor2 = lowval[i];
                return;
            }
            totcost = 4 * diff[i] + rastcost[i];
            if (totcost < mincost) {
                mincost = totcost;
                index = i;
            }
        }
        factor1 = hival[index];
        factor2 = lowval[index];
    }

    // leptonica: pixDilateCompBrick (morph.c)
    /// <summary>Dilation with a composite brick Sel.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="hsize">width of brick Sel</param>
    /// <param name="vsize">height of brick Sel</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Sel is a brick with all elements being hits
    /// (2) The origin is at (x, y) = (hsize/2, vsize/2)
    /// (3) Do compositely for each dimension > 1.
    /// (4) Do separably if both hsize and vsize are &gt; 1.
    /// (5) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (6) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.DilateCompBrick(null, pixs, ...);
    ///     (b) Pix.DilateCompBrick(pixs, pixs, ...);
    ///     (c) Pix.DilateCompBrick(pixd, pixs, ...);
    /// (7) The dimensions of the resulting image are determined by pixs.
    /// (8) CAUTION: both hsize and vsize are being decomposed.
    ///     The decomposer chooses a product of sizes (call them
    ///     'terms') for each that is close to the input size,
    ///     but not necessarily equal to it.  It attempts to optimize:
    ///        (a) for consistency with the input values: the product
    ///            of terms is close to the input size
    ///        (b) for efficiency of the operation: the sum of the
    ///            terms is small; ideally about twice the square
    ///            root of the input size.
    ///     So, for example, if the input hsize = 37, which is
    ///     a prime number, the decomposer will break this into two
    ///     terms, 6 and 6, so that the net result is a dilation
    ///     with hsize = 36.
    /// </remarks>
    public static Pix DilateCompBrick(Pix? pixd, Pix pixs, int hsize, int vsize)
    {
        const string func = "pixDilateCompBrick";
        Pix pix1, pix2, pix3;
        Sel selh1 = null!, selh2 = null!, selv1 = null!, selv2 = null!;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize and vsize not >= 1");

        if (hsize == 1 && vsize == 1)
            return Copy(pixd, pixs);
        if (hsize > 1)
            SelectComposableSels(hsize, Sel.L_HORIZ, out selh1, out selh2);
        if (vsize > 1)
            SelectComposableSels(vsize, Sel.L_VERT, out selv1, out selv2);

        pix1 = pixs.AddBorder(32, 0);
        if (vsize == 1) {
            pix2 = Dilate(null, pix1, selh1);
            pix3 = Dilate(pix1, pix2, selh2);
        } else if (hsize == 1) {
            pix2 = Dilate(null, pix1, selv1);
            pix3 = Dilate(pix1, pix2, selv2);
        } else {
            pix2 = Dilate(null, pix1, selh1);
            pix3 = Dilate(pix1, pix2, selh2);
            Dilate(pix2, pix3, selv1);
            Dilate(pix3, pix2, selv2);
        }

        pix1 = pix3.RemoveBorder(32);
        if (pixd == null)
            return pix1;
        Copy(pixd, pix1);
        return pixd;
    }

    // leptonica: pixErodeCompBrick (morph.c)
    /// <summary>Erosion with a composite brick Sel.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="hsize">width of brick Sel</param>
    /// <param name="vsize">height of brick Sel</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Sel is a brick with all elements being hits
    /// (2) The origin is at (x, y) = (hsize/2, vsize/2)
    /// (3) Do compositely for each dimension > 1.
    /// (4) Do separably if both hsize and vsize are &gt; 1.
    /// (5) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (6) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.ErodeCompBrick(null, pixs, ...);
    ///     (b) Pix.ErodeCompBrick(pixs, pixs, ...);
    ///     (c) Pix.ErodeCompBrick(pixd, pixs, ...);
    /// (7) The dimensions of the resulting image are determined by pixs.
    /// (8) CAUTION: both hsize and vsize are being decomposed.
    ///     The decomposer chooses a product of sizes (call them
    ///     'terms') for each that is close to the input size,
    ///     but not necessarily equal to it.  It attempts to optimize:
    ///        (a) for consistency with the input values: the product
    ///            of terms is close to the input size
    ///        (b) for efficiency of the operation: the sum of the
    ///            terms is small; ideally about twice the square
    ///            root of the input size.
    ///     So, for example, if the input hsize = 37, which is
    ///     a prime number, the decomposer will break this into two
    ///     terms, 6 and 6, so that the net result is a dilation
    ///     with hsize = 36.
    /// </remarks>
    public static Pix ErodeCompBrick(Pix? pixd, Pix pixs, int hsize, int vsize)
    {
        const string func = "pixErodeCompBrick";
        Pix pixt;
        Sel selh1 = null!, selh2 = null!, selv1 = null!, selv2 = null!;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize and vsize not >= 1");

        if (hsize == 1 && vsize == 1)
            return Copy(pixd, pixs);
        if (hsize > 1)
            SelectComposableSels(hsize, Sel.L_HORIZ, out selh1, out selh2);
        if (vsize > 1)
            SelectComposableSels(vsize, Sel.L_VERT, out selv1, out selv2);

        if (vsize == 1) {
            pixt = Erode(null, pixs, selh1);
            pixd = Erode(pixd, pixt, selh2);
        } else if (hsize == 1) {
            pixt = Erode(null, pixs, selv1);
            pixd = Erode(pixd, pixt, selv2);
        } else {
            pixt = Erode(null, pixs, selh1);
            pixd = Erode(pixd, pixt, selh2);
            Erode(pixt, pixd, selv1);
            Erode(pixd, pixt, selv2);
        }

        return pixd;
    }

    // leptonica: pixOpenCompBrick (morph.c)
    /// <summary>Opening with a composite brick Sel.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="hsize">width of brick Sel</param>
    /// <param name="vsize">height of brick Sel</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Sel is a brick with all elements being hits
    /// (2) The origin is at (x, y) = (hsize/2, vsize/2)
    /// (3) Do compositely for each dimension > 1.
    /// (4) Do separably if both hsize and vsize are &gt; 1.
    /// (5) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (6) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.OpenCompBrick(null, pixs, ...);
    ///     (b) Pix.OpenCompBrick(pixs, pixs, ...);
    ///     (c) Pix.OpenCompBrick(pixd, pixs, ...);
    /// (7) The dimensions of the resulting image are determined by pixs.
    /// (8) CAUTION: both hsize and vsize are being decomposed.
    ///     The decomposer chooses a product of sizes (call them
    ///     'terms') for each that is close to the input size,
    ///     but not necessarily equal to it.  It attempts to optimize:
    ///        (a) for consistency with the input values: the product
    ///            of terms is close to the input size
    ///        (b) for efficiency of the operation: the sum of the
    ///            terms is small; ideally about twice the square
    ///            root of the input size.
    ///     So, for example, if the input hsize = 37, which is
    ///     a prime number, the decomposer will break this into two
    ///     terms, 6 and 6, so that the net result is a dilation
    ///     with hsize = 36.
    /// </remarks>
    public static Pix OpenCompBrick(Pix? pixd, Pix pixs, int hsize, int vsize)
    {
        const string func = "pixOpenCompBrick";
        Pix pixt;
        Sel selh1 = null!, selh2 = null!, selv1 = null!, selv2 = null!;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize and vsize not >= 1");

        if (hsize == 1 && vsize == 1)
            return Copy(pixd, pixs);
        if (hsize > 1)
            SelectComposableSels(hsize, Sel.L_HORIZ, out selh1, out selh2);
        if (vsize > 1)
            SelectComposableSels(vsize, Sel.L_VERT, out selv1, out selv2);

        if (vsize == 1) {
            pixt = Erode(null, pixs, selh1);
            pixd = Erode(pixd, pixt, selh2);
            Dilate(pixt, pixd, selh1);
            Dilate(pixd, pixt, selh2);
        } else if (hsize == 1) {
            pixt = Erode(null, pixs, selv1);
            pixd = Erode(pixd, pixt, selv2);
            Dilate(pixt, pixd, selv1);
            Dilate(pixd, pixt, selv2);
        } else {  /* do separably */
            pixt = Erode(null, pixs, selh1);
            pixd = Erode(pixd, pixt, selh2);
            Erode(pixt, pixd, selv1);
            Erode(pixd, pixt, selv2);
            Dilate(pixt, pixd, selh1);
            Dilate(pixd, pixt, selh2);
            Dilate(pixt, pixd, selv1);
            Dilate(pixd, pixt, selv2);
        }

        return pixd;
    }

    // leptonica: pixCloseCompBrick (morph.c)
    /// <summary>Closing with a composite brick Sel.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="hsize">width of brick Sel</param>
    /// <param name="vsize">height of brick Sel</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Sel is a brick with all elements being hits
    /// (2) The origin is at (x, y) = (hsize/2, vsize/2)
    /// (3) Do compositely for each dimension > 1.
    /// (4) Do separably if both hsize and vsize are &gt; 1.
    /// (5) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (6) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.CloseCompBrick(null, pixs, ...);
    ///     (b) Pix.CloseCompBrick(pixs, pixs, ...);
    ///     (c) Pix.CloseCompBrick(pixd, pixs, ...);
    /// (7) The dimensions of the resulting image are determined by pixs.
    /// (8) CAUTION: both hsize and vsize are being decomposed.
    ///     The decomposer chooses a product of sizes (call them
    ///     'terms') for each that is close to the input size,
    ///     but not necessarily equal to it.  It attempts to optimize:
    ///        (a) for consistency with the input values: the product
    ///            of terms is close to the input size
    ///        (b) for efficiency of the operation: the sum of the
    ///            terms is small; ideally about twice the square
    ///            root of the input size.
    ///     So, for example, if the input hsize = 37, which is
    ///     a prime number, the decomposer will break this into two
    ///     terms, 6 and 6, so that the net result is a dilation
    ///     with hsize = 36.
    /// </remarks>
    public static Pix CloseCompBrick(Pix? pixd, Pix pixs, int hsize, int vsize)
    {
        const string func = "pixCloseCompBrick";
        Pix pixt;
        Sel selh1 = null!, selh2 = null!, selv1 = null!, selv2 = null!;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize and vsize not >= 1");

        if (hsize == 1 && vsize == 1)
            return Copy(pixd, pixs);
        if (hsize > 1)
            SelectComposableSels(hsize, Sel.L_HORIZ, out selh1, out selh2);
        if (vsize > 1)
            SelectComposableSels(vsize, Sel.L_VERT, out selv1, out selv2);

        if (vsize == 1) {
            pixt = Dilate(null, pixs, selh1);
            pixd = Dilate(pixd, pixt, selh2);
            Erode(pixt, pixd, selh1);
            Erode(pixd, pixt, selh2);
        } else if (hsize == 1) {
            pixt = Dilate(null, pixs, selv1);
            pixd = Dilate(pixd, pixt, selv2);
            Erode(pixt, pixd, selv1);
            Erode(pixd, pixt, selv2);
        } else {  /* do separably */
            pixt = Dilate(null, pixs, selh1);
            pixd = Dilate(pixd, pixt, selh2);
            Dilate(pixt, pixd, selv1);
            Dilate(pixd, pixt, selv2);
            Erode(pixt, pixd, selh1);
            Erode(pixd, pixt, selh2);
            Erode(pixt, pixd, selv1);
            Erode(pixd, pixt, selv2);
        }

        return pixd;
    }

    // leptonica: pixCloseSafeCompBrick (morph.c)
    /// <summary>Closing with a composite brick Sel, using an added border so there are no boundary effects.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="hsize">width of brick Sel</param>
    /// <param name="vsize">height of brick Sel</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Sel is a brick with all elements being hits
    /// (2) The origin is at (x, y) = (hsize/2, vsize/2)
    /// (3) Do compositely for each dimension > 1.
    /// (4) Do separably if both hsize and vsize are &gt; 1.
    /// (5) Safe closing adds a border of 0 pixels, of sufficient size so
    ///     that all pixels in input image are processed within
    ///     32-bit words in the expanded image.  As a result, there is
    ///     no special processing for pixels near the boundary, and there
    ///     are no boundary effects.  The border is removed at the end.
    /// (6) There are three cases:
    ///     (a) pixd == null   (result into new pixd)
    ///     (b) pixd == pixs   (in-place; writes result back to pixs)
    ///     (c) pixd != pixs   (puts result into existing pixd)
    /// (7) For clarity, if the case is known, use these patterns:
    ///     (a) pixd = Pix.CloseSafeCompBrick(null, pixs, ...);
    ///     (b) Pix.CloseSafeCompBrick(pixs, pixs, ...);
    ///     (c) Pix.CloseSafeCompBrick(pixd, pixs, ...);
    /// (8) The dimensions of the resulting image are determined by pixs.
    /// (9) CAUTION: both hsize and vsize are being decomposed.
    ///     The decomposer chooses a product of sizes (call them
    ///     'terms') for each that is close to the input size,
    ///     but not necessarily equal to it.  It attempts to optimize:
    ///        (a) for consistency with the input values: the product
    ///            of terms is close to the input size
    ///        (b) for efficiency of the operation: the sum of the
    ///            terms is small; ideally about twice the square
    ///            root of the input size.
    ///     So, for example, if the input hsize = 37, which is
    ///     a prime number, the decomposer will break this into two
    ///     terms, 6 and 6, so that the net result is a dilation
    ///     with hsize = 36.
    /// </remarks>
    public static Pix CloseSafeCompBrick(Pix? pixd, Pix pixs, int hsize, int vsize)
    {
        const string func = "pixCloseSafeCompBrick";
        int maxtrans, bordsize;
        Pix pixsb, pixt, pixdb;
        Sel selh1 = null!, selh2 = null!, selv1 = null!, selv2 = null!;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (hsize < 1 || vsize < 1)
            throw Lept.Arg(func, "hsize and vsize not >= 1");

        if (hsize == 1 && vsize == 1)
            return Copy(pixd, pixs);

            /* Symmetric b.c. handles correctly without added pixels */
        if (MORPH_BC == L.SYMMETRIC_MORPH_BC)
            return CloseCompBrick(pixd, pixs, hsize, vsize);

        if (hsize > 1)
            SelectComposableSels(hsize, Sel.L_HORIZ, out selh1, out selh2);
        if (vsize > 1)
            SelectComposableSels(vsize, Sel.L_VERT, out selv1, out selv2);

        maxtrans = Math.Max(hsize / 2, vsize / 2);
        bordsize = 32 * ((maxtrans + 31) / 32);  /* full 32 bit words */
        pixsb = pixs.AddBorder(bordsize, 0);

        if (vsize == 1) {
            pixt = Dilate(null, pixsb, selh1);
            pixdb = Dilate(null, pixt, selh2);
            Erode(pixt, pixdb, selh1);
            Erode(pixdb, pixt, selh2);
        } else if (hsize == 1) {
            pixt = Dilate(null, pixsb, selv1);
            pixdb = Dilate(null, pixt, selv2);
            Erode(pixt, pixdb, selv1);
            Erode(pixdb, pixt, selv2);
        } else {  /* do separably */
            pixt = Dilate(null, pixsb, selh1);
            pixdb = Dilate(null, pixt, selh2);
            Dilate(pixt, pixdb, selv1);
            Dilate(pixdb, pixt, selv2);
            Erode(pixt, pixdb, selh1);
            Erode(pixdb, pixt, selh2);
            Erode(pixt, pixdb, selv1);
            Erode(pixdb, pixt, selv2);
        }

        pixt = pixdb.RemoveBorder(bordsize);

        if (pixd == null) {
            pixd = pixt;
        } else {
            Copy(pixd, pixt);
        }

        return pixd;
    }

    /*-----------------------------------------------------------------*
     *           Functions associated with boundary conditions         *
     *-----------------------------------------------------------------*/
    // leptonica: resetMorphBoundaryCondition (morph.c)
    /// <param name="bc">SYMMETRIC_MORPH_BC, ASYMMETRIC_MORPH_BC</param>
    public static void ResetMorphBoundaryCondition(int bc)
    {
        if (bc != L.SYMMETRIC_MORPH_BC && bc != L.ASYMMETRIC_MORPH_BC) {
            Log.Warning("resetMorphBoundaryCondition", "invalid bc; using asymmetric");
            bc = L.ASYMMETRIC_MORPH_BC;
        }
        MORPH_BC = bc;
    }

    // leptonica: getMorphBorderPixelColor (morph.c)
    /// <param name="type">L_MORPH_DILATE, L_MORPH_ERODE</param>
    /// <param name="depth">of pix</param>
    /// <returns>color of border pixels for this operation</returns>
    public static uint GetMorphBorderPixelColor(int type, int depth)
    {
        const string func = "getMorphBorderPixelColor";

        if (type != L.L_MORPH_DILATE && type != L.L_MORPH_ERODE)
            throw Lept.Arg(func, "invalid type");
        if (depth != 1 && depth != 2 && depth != 4 && depth != 8 &&
            depth != 16 && depth != 32)
            throw Lept.Arg(func, "invalid depth");

        if (MORPH_BC == L.ASYMMETRIC_MORPH_BC || type == L.L_MORPH_DILATE)
            return 0;

            /* Symmetric & erosion */
        if (depth < 32)
            return (uint)((1 << depth) - 1);
        else  /* depth == 32 */
            return 0xffffff00;
    }

    /*-----------------------------------------------------------------*
     *               Static helpers for arg processing                 *
     *-----------------------------------------------------------------*/
    // leptonica: processMorphArgs1 (morph.c)
    /// <summary>Used for generic erosion, dilation and HMT.</summary>
    /// <param name="pixd">[optional]; this can be null, equal to pixs, or different from pixs</param>
    /// <param name="pixs">1 bpp</param>
    /// <param name="sel"></param>
    /// <param name="pixt">copy or clone of %pixs</param>
    /// <returns>pixd</returns>
    private static Pix ProcessMorphArgs1(Pix? pixd, Pix pixs, Sel sel, out Pix pixt)
    {
        const string func = "processMorphArgs1";
        int sx, sy;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");

        sel.GetParameters(out sx, out sy, out _, out _);
        if (sx == 0 || sy == 0)
            throw Lept.Arg(func, "sel of size 0");

            /* We require pixd to exist and to be the same size as pixs.
             * Further, pixt must be a copy (or clone) of pixs.  */
        if (pixd == null) {
            pixd = CreateTemplate(pixs);
            pixt = Clone(pixs);
        } else {
            pixd.ResizeImageData(pixs);
            if (pixd == pixs) {  /* in-place; must make a copy of pixs */
                pixt = Copy(null, pixs);
            } else {
                pixt = Clone(pixs);
            }
        }
        return pixd;
    }

    // leptonica: processMorphArgs2 (morph.c)
    /// <summary>Used for generic openings and closings.</summary>
    private static Pix ProcessMorphArgs2(Pix? pixd, Pix pixs, Sel sel)
    {
        const string func = "processMorphArgs2";
        int sx, sy;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");

        sel.GetParameters(out sx, out sy, out _, out _);
        if (sx == 0 || sy == 0)
            throw Lept.Arg(func, "sel of size 0");

        if (pixd == null)
            return CreateTemplate(pixs);
        pixd.ResizeImageData(pixs);
        return pixd;
    }

}
