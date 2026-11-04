using System;

namespace Leptonica;

/*
 *  boxfunc4.c: Box selection, transforms, extent.
 *
 *  Differences from C:
 *    - No reference counting and no destroy functions: the GC owns the objects.
 *    - Optional output pointers become out parameters.  A required single output
 *      is returned (boxaGetCoverage, boxaGetArea).  The outputs of
 *      boxaSizeRange(), boxaaSizeRange(), boxaGetSizes() and boxaGetExtent() are
 *      always computed, so the C error "no data can be returned" does not exist.
 *    - boxaPermuteRandom() is a static method taking the optional boxad first.
 *    - boxaPermuteRandom() with a single box: the C code calls
 *      boxaSwapBoxes(boxad, 0, 1), which fails and is ignored; the port logs
 *      the same error and carries on.
 *    - ptaConvertToBoxa(): where boxCreate() fails, the C code logs the error,
 *      boxaAddBox() fails on the NULL box and the loop carries on; the port
 *      logs both errors and carries on.
 *    - boxaGetExtent() always returns the box.  Where C would log the failure of
 *      boxCreate() and return a NULL box (all valid boxes lie entirely left of or
 *      above the +quad), the port throws.
 *    - boxaGetCoverage() with exactflag == 0 logs the boxClipToRectangle() error
 *      for a box outside the rectangle and goes on, as C does.
 *    - rand() is CRand.Next().
 *
 *  Stubs created for callees in files that are not ready yet:
 *    numaPseudorandomSequence (numafunc1.c); boxaSortByIndex (boxfunc2.c);
 *    ptaJoin (ptafunc1.c); pixSetBorderVal (pix2.c); pixCountPixels (pix3.c);
 *    pixaCreate, pixaGetCount, pixaGetPix (pixabasic.c);
 *    pixaDisplayTiledInRows (pixafunc2.c); pixRenderBoxArb (graphics.c);
 *    bmfCreate (bmf.c); pixAddSingleTextblock (textops.c).
 */

/// <summary>Constants from pix.h used by the box selection functions.</summary>
public static partial class L
{
    /* Size comparison (L_SELECT_IF_*) */
    /// <summary>Saves the item if the value is less than the threshold.</summary>
    public const int L_SELECT_IF_LT = 1;

    /// <summary>Saves the item if the value is greater than the threshold.</summary>
    public const int L_SELECT_IF_GT = 2;

    /// <summary>Saves the item if the value is less than or equal to the threshold.</summary>
    public const int L_SELECT_IF_LTE = 3;

    /* Location filter */

    /// <summary>The width must satisfy the constraint.</summary>
    public const int L_SELECT_WIDTH = 1;

    /// <summary>The height must satisfy the constraint.</summary>
    public const int L_SELECT_HEIGHT = 2;

    /// <summary>The x value must satisfy the constraint.</summary>
    public const int L_SELECT_XVAL = 3;

    /// <summary>The y value must satisfy the constraint.</summary>
    public const int L_SELECT_YVAL = 4;

    /// <summary>Either the width or the height (or either the x value or the y value) may satisfy the constraint.</summary>
    public const int L_SELECT_IF_EITHER = 5;

    /// <summary>Both the width and the height (or both the x value and the y value) must satisfy the constraint.</summary>
    public const int L_SELECT_IF_BOTH = 6;
}

public sealed partial class Boxa
{
    /*---------------------------------------------------------------------*
     *                     Boxa and boxaa range selection                  *
     *---------------------------------------------------------------------*/
    // leptonica: boxaSelectRange (boxfunc4.c)
    /// <summary>Selects the boxes in the index range [first, last].</summary>
    /// <param name="first">use 0 to select from the beginning</param>
    /// <param name="last">use -1 to select to the end</param>
    /// <param name="copyflag">L_COPY, L_CLONE</param>
    /// <returns>boxad</returns>
    /// <remarks>
    /// (1) The copyflag specifies what we do with each box from boxas.
    ///     Specifically, L_CLONE inserts a clone into boxad of each
    ///     selected box from boxas.
    /// </remarks>
    public Boxa SelectRange(int first, int last, int copyflag)
    {
        const string func = "boxaSelectRange";
        Boxa boxas = this;

        if (copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg(func, "invalid copyflag");
        int n;
        if ((n = boxas.Count) == 0) {
            Log.Warning(func, "boxas is empty");
            return boxas.Copy(copyflag);
        }
        first = Math.Max(0, first);
        if (last < 0) last = n - 1;
        if (first >= n)
            throw Lept.Arg(func, "invalid first");
        if (last >= n) {
            Log.Warning(func, $"last = {last} is beyond max index = {n - 1}; adjusting");
            last = n - 1;
        }
        if (first > last)
            throw Lept.Arg(func, "first > last");

        int nbox = last - first + 1;
        Boxa boxad = Create(nbox);
        for (int i = first; i <= last; i++) {
            Box box = boxas.GetBox(i, copyflag);
            boxad.AddBox(box, L.L_INSERT);
        }
        return boxad;
    }
}

public sealed partial class Boxaa
{
    // leptonica: boxaaSelectRange (boxfunc4.c)
    /// <summary>Selects the boxa in the index range [first, last].</summary>
    /// <param name="first">use 0 to select from the beginning</param>
    /// <param name="last">use -1 to select to the end</param>
    /// <param name="copyflag">L_COPY, L_CLONE</param>
    /// <returns>baad</returns>
    /// <remarks>
    /// (1) The copyflag specifies what we do with each boxa from baas.
    ///     Specifically, L_CLONE inserts a clone into baad of each
    ///     selected boxa from baas.
    /// </remarks>
    public Boxaa SelectRange(int first, int last, int copyflag)
    {
        const string func = "boxaaSelectRange";
        Boxaa baas = this;

        if (copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg(func, "invalid copyflag");
        int n;
        if ((n = baas.Count) == 0)
            throw Lept.Arg(func, "empty baas");
        first = Math.Max(0, first);
        if (last < 0) last = n - 1;
        if (first >= n)
            throw Lept.Arg(func, "invalid first");
        if (last >= n) {
            Log.Warning(func, $"last = {last} is beyond max index = {n - 1}; adjusting");
            last = n - 1;
        }
        if (first > last)
            throw Lept.Arg(func, "first > last");

        int nboxa = last - first + 1;
        Boxaa baad = Create(nboxa);
        for (int i = first; i <= last; i++) {
            Boxa boxa = baas.GetBoxa(i, copyflag);
            baad.AddBoxa(boxa, L.L_INSERT);
        }
        return baad;
    }
}

public sealed partial class Boxa
{
    /*---------------------------------------------------------------------*
     *                          Boxa size selection                        *
     *---------------------------------------------------------------------*/
    // leptonica: boxaSelectBySize (boxfunc4.c)
    /// <summary>Selects the boxes by width and/or height.</summary>
    /// <param name="width">threshold dimension</param>
    /// <param name="height">threshold dimension</param>
    /// <param name="type">L_SELECT_WIDTH, L_SELECT_HEIGHT, L_SELECT_IF_EITHER, L_SELECT_IF_BOTH</param>
    /// <param name="relation">L_SELECT_IF_LT, L_SELECT_IF_GT, L_SELECT_IF_LTE, L_SELECT_IF_GTE</param>
    /// <param name="changed">true if changed; false if a copy was returned</param>
    /// <returns>boxad filtered set</returns>
    /// <remarks>
    /// (1) The args specify constraints on the size of the
    ///     components that are kept.
    /// (2) Uses box copies in the new boxa.
    /// (3) If the selection type is L_SELECT_WIDTH, the input
    ///     height is ignored, and v.v.
    /// (4) To keep small components, use relation = L_SELECT_IF_LT or
    ///     L_SELECT_IF_LTE.
    ///     To keep large components, use relation = L_SELECT_IF_GT or
    ///     L_SELECT_IF_GTE.
    /// </remarks>
    public Boxa SelectBySize(int width, int height, int type, int relation, out bool changed)
    {
        const string func = "boxaSelectBySize";
        Boxa boxas = this;

        changed = false;
        if (boxas.Count == 0) {
            Log.Warning(func, "boxas is empty");
            return boxas.Copy(L.L_COPY);
        }
        if (type != L.L_SELECT_WIDTH && type != L.L_SELECT_HEIGHT &&
            type != L.L_SELECT_IF_EITHER && type != L.L_SELECT_IF_BOTH)
            throw Lept.Arg(func, "invalid type");
        if (relation != L.L_SELECT_IF_LT && relation != L.L_SELECT_IF_GT &&
            relation != L.L_SELECT_IF_LTE && relation != L.L_SELECT_IF_GTE)
            throw Lept.Arg(func, "invalid relation");

            /* Compute the indicator array for saving components */
        Numa na = boxas.MakeSizeIndicator(width, height, type, relation);

            /* Filter to get output */
        Boxa boxad = boxas.SelectWithIndicator(na, out changed);

        return boxad;
    }

    // leptonica: boxaMakeSizeIndicator (boxfunc4.c)
    /// <summary>Makes the indicator array for the boxes that satisfy a size constraint.</summary>
    /// <param name="width">threshold dimension</param>
    /// <param name="height">threshold dimension</param>
    /// <param name="type">L_SELECT_WIDTH, L_SELECT_HEIGHT, L_SELECT_IF_EITHER, L_SELECT_IF_BOTH</param>
    /// <param name="relation">L_SELECT_IF_LT, L_SELECT_IF_GT, L_SELECT_IF_LTE, L_SELECT_IF_GTE</param>
    /// <returns>na indicator array</returns>
    /// <remarks>
    /// (1) The args specify constraints on the size of the
    ///     components that are kept.
    /// (2) If the selection type is L_SELECT_WIDTH, the input
    ///     height is ignored, and v.v.
    /// (3) To keep small components, use relation = L_SELECT_IF_LT or
    ///     L_SELECT_IF_LTE.
    ///     To keep large components, use relation = L_SELECT_IF_GT or
    ///     L_SELECT_IF_GTE.
    /// </remarks>
    public Numa MakeSizeIndicator(int width, int height, int type, int relation)
    {
        const string func = "boxaMakeSizeIndicator";
        Boxa boxa = this;

        int n;
        if ((n = boxa.Count) == 0)
            throw Lept.Arg(func, "boxa is empty");
        if (type != L.L_SELECT_WIDTH && type != L.L_SELECT_HEIGHT &&
            type != L.L_SELECT_IF_EITHER && type != L.L_SELECT_IF_BOTH)
            throw Lept.Arg(func, "invalid type");
        if (relation != L.L_SELECT_IF_LT && relation != L.L_SELECT_IF_GT &&
            relation != L.L_SELECT_IF_LTE && relation != L.L_SELECT_IF_GTE)
            throw Lept.Arg(func, "invalid relation");

        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            int ival = 0;
            boxa.GetBoxGeometry(i, out _, out _, out int w, out int h);
            switch (type) {
            case L.L_SELECT_WIDTH:
                if ((relation == L.L_SELECT_IF_LT && w < width) ||
                    (relation == L.L_SELECT_IF_GT && w > width) ||
                    (relation == L.L_SELECT_IF_LTE && w <= width) ||
                    (relation == L.L_SELECT_IF_GTE && w >= width))
                    ival = 1;
                break;
            case L.L_SELECT_HEIGHT:
                if ((relation == L.L_SELECT_IF_LT && h < height) ||
                    (relation == L.L_SELECT_IF_GT && h > height) ||
                    (relation == L.L_SELECT_IF_LTE && h <= height) ||
                    (relation == L.L_SELECT_IF_GTE && h >= height))
                    ival = 1;
                break;
            case L.L_SELECT_IF_EITHER:
                if (((relation == L.L_SELECT_IF_LT) && (w < width || h < height)) ||
                    ((relation == L.L_SELECT_IF_GT) && (w > width || h > height)) ||
                    ((relation == L.L_SELECT_IF_LTE) && (w <= width || h <= height)) ||
                    ((relation == L.L_SELECT_IF_GTE) && (w >= width || h >= height)))
                    ival = 1;
                break;
            case L.L_SELECT_IF_BOTH:
                if (((relation == L.L_SELECT_IF_LT) && (w < width && h < height)) ||
                    ((relation == L.L_SELECT_IF_GT) && (w > width && h > height)) ||
                    ((relation == L.L_SELECT_IF_LTE) && (w <= width && h <= height)) ||
                    ((relation == L.L_SELECT_IF_GTE) && (w >= width && h >= height)))
                    ival = 1;
                break;
            default:
                Log.Warning(func, "can't get here!");
                break;
            }
            na.AddNumber(ival);
        }

        return na;
    }

    // leptonica: boxaSelectByArea (boxfunc4.c)
    /// <summary>Selects the boxes by area.</summary>
    /// <param name="area">threshold value of width * height</param>
    /// <param name="relation">L_SELECT_IF_LT, L_SELECT_IF_GT, L_SELECT_IF_LTE, L_SELECT_IF_GTE</param>
    /// <param name="changed">true if changed; false if a copy was returned</param>
    /// <returns>boxad filtered set</returns>
    /// <remarks>
    /// (1) Uses box copies in the new boxa.
    /// (2) To keep small components, use relation = L_SELECT_IF_LT or
    ///     L_SELECT_IF_LTE.
    ///     To keep large components, use relation = L_SELECT_IF_GT or
    ///     L_SELECT_IF_GTE.
    /// </remarks>
    public Boxa SelectByArea(int area, int relation, out bool changed)
    {
        const string func = "boxaSelectByArea";
        Boxa boxas = this;

        changed = false;
        if (boxas.Count == 0) {
            Log.Warning(func, "boxas is empty");
            return boxas.Copy(L.L_COPY);
        }
        if (relation != L.L_SELECT_IF_LT && relation != L.L_SELECT_IF_GT &&
            relation != L.L_SELECT_IF_LTE && relation != L.L_SELECT_IF_GTE)
            throw Lept.Arg(func, "invalid relation");

            /* Compute the indicator array for saving components */
        Numa na = boxas.MakeAreaIndicator(area, relation);

            /* Filter to get output */
        Boxa boxad = boxas.SelectWithIndicator(na, out changed);

        return boxad;
    }

    // leptonica: boxaMakeAreaIndicator (boxfunc4.c)
    /// <summary>Makes the indicator array for the boxes that satisfy an area constraint.</summary>
    /// <param name="area">threshold value of width * height</param>
    /// <param name="relation">L_SELECT_IF_LT, L_SELECT_IF_GT, L_SELECT_IF_LTE, L_SELECT_IF_GTE</param>
    /// <returns>na indicator array</returns>
    /// <remarks>
    /// (1) To keep small components, use relation = L_SELECT_IF_LT or
    ///     L_SELECT_IF_LTE.
    ///     To keep large components, use relation = L_SELECT_IF_GT or
    ///     L_SELECT_IF_GTE.
    /// </remarks>
    public Numa MakeAreaIndicator(int area, int relation)
    {
        const string func = "boxaMakeAreaIndicator";
        Boxa boxa = this;

        int n;
        if ((n = boxa.Count) == 0)
            throw Lept.Arg(func, "boxa is empty");
        if (relation != L.L_SELECT_IF_LT && relation != L.L_SELECT_IF_GT &&
            relation != L.L_SELECT_IF_LTE && relation != L.L_SELECT_IF_GTE)
            throw Lept.Arg(func, "invalid relation");

        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            int ival = 0;
            boxa.GetBoxGeometry(i, out _, out _, out int w, out int h);

            if ((relation == L.L_SELECT_IF_LT && w * h < area) ||
                (relation == L.L_SELECT_IF_GT && w * h > area) ||
                (relation == L.L_SELECT_IF_LTE && w * h <= area) ||
                (relation == L.L_SELECT_IF_GTE && w * h >= area))
                ival = 1;
            na.AddNumber(ival);
        }

        return na;
    }

    // leptonica: boxaSelectByWHRatio (boxfunc4.c)
    /// <summary>Selects the boxes by width/height ratio.</summary>
    /// <param name="ratio">width/height threshold value</param>
    /// <param name="relation">L_SELECT_IF_LT, L_SELECT_IF_GT, L_SELECT_IF_LTE, L_SELECT_IF_GTE</param>
    /// <param name="changed">true if changed; false if a copy was returned</param>
    /// <returns>boxad filtered set</returns>
    /// <remarks>
    /// (1) Uses box copies in the new boxa.
    /// (2) To keep narrow components, use relation = L_SELECT_IF_LT or
    ///     L_SELECT_IF_LTE.
    ///     To keep wide components, use relation = L_SELECT_IF_GT or
    ///     L_SELECT_IF_GTE.
    /// </remarks>
    public Boxa SelectByWHRatio(float ratio, int relation, out bool changed)
    {
        const string func = "boxaSelectByWHRatio";
        Boxa boxas = this;

        changed = false;
        if (boxas.Count == 0) {
            Log.Warning(func, "boxas is empty");
            return boxas.Copy(L.L_COPY);
        }
        if (relation != L.L_SELECT_IF_LT && relation != L.L_SELECT_IF_GT &&
            relation != L.L_SELECT_IF_LTE && relation != L.L_SELECT_IF_GTE)
            throw Lept.Arg(func, "invalid relation");

            /* Compute the indicator array for saving components */
        Numa na = boxas.MakeWHRatioIndicator(ratio, relation);

            /* Filter to get output */
        Boxa boxad = boxas.SelectWithIndicator(na, out changed);

        return boxad;
    }

    // leptonica: boxaMakeWHRatioIndicator (boxfunc4.c)
    /// <summary>Makes the indicator array for the boxes that satisfy a width/height ratio constraint.</summary>
    /// <param name="ratio">width/height threshold value</param>
    /// <param name="relation">L_SELECT_IF_LT, L_SELECT_IF_GT, L_SELECT_IF_LTE, L_SELECT_IF_GTE</param>
    /// <returns>na indicator array</returns>
    /// <remarks>
    /// (1) To keep narrow components, use relation = L_SELECT_IF_LT or
    ///     L_SELECT_IF_LTE.
    ///     To keep wide components, use relation = L_SELECT_IF_GT or
    ///     L_SELECT_IF_GTE.
    /// </remarks>
    public Numa MakeWHRatioIndicator(float ratio, int relation)
    {
        const string func = "boxaMakeWHRatioIndicator";
        Boxa boxa = this;

        int n;
        if ((n = boxa.Count) == 0)
            throw Lept.Arg(func, "boxa is empty");
        if (relation != L.L_SELECT_IF_LT && relation != L.L_SELECT_IF_GT &&
            relation != L.L_SELECT_IF_LTE && relation != L.L_SELECT_IF_GTE)
            throw Lept.Arg(func, "invalid relation");

        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            int ival = 0;
            boxa.GetBoxGeometry(i, out _, out _, out int w, out int h);
            float whratio = (float)w / (float)h;

            if ((relation == L.L_SELECT_IF_LT && whratio < ratio) ||
                (relation == L.L_SELECT_IF_GT && whratio > ratio) ||
                (relation == L.L_SELECT_IF_LTE && whratio <= ratio) ||
                (relation == L.L_SELECT_IF_GTE && whratio >= ratio))
                ival = 1;
            na.AddNumber(ival);
        }

        return na;
    }

    // leptonica: boxaSelectWithIndicator (boxfunc4.c)
    /// <summary>Selects the boxes for which the indicator is 1.</summary>
    /// <param name="na">indicator numa</param>
    /// <param name="changed">true if changed; false if a copy was returned</param>
    /// <returns>boxad</returns>
    /// <remarks>
    /// (1) Returns a copy of the boxa if no components are removed.
    /// (2) Uses box copies in the new boxa.
    /// (3) The indicator numa has values 0 (ignore) and 1 (accept).
    /// (4) If all indicator values are 0, the returned boxa is empty.
    /// </remarks>
    public Boxa SelectWithIndicator(Numa na, out bool changed)
    {
        Boxa boxas = this;

        changed = false;
        int nsave = 0;
        int n = na.Count;
        for (int i = 0; i < n; i++) {
            int ival = na.GetIValue(i);
            if (ival == 1) nsave++;
        }

        if (nsave == n) {
            changed = false;
            return boxas.Copy(L.L_COPY);
        }
        changed = true;
        Boxa boxad = Create(nsave);
        for (int i = 0; i < n; i++) {
            int ival = na.GetIValue(i);
            if (ival == 0) continue;
            Box box = boxas.GetBox(i, L.L_COPY);
            boxad.AddBox(box, L.L_INSERT);
        }

        return boxad;
    }

    /*---------------------------------------------------------------------*
     *                           Boxa Permutation                          *
     *---------------------------------------------------------------------*/
    // leptonica: boxaPermutePseudorandom (boxfunc4.c)
    /// <summary>Permutes the boxes pseudorandomly.</summary>
    /// <returns>boxad with boxes permuted</returns>
    /// <remarks>
    /// (1) This does a pseudorandom in-place permutation of the boxes.
    /// (2) The result is guaranteed not to have any boxes in their
    ///     original position, but it is not very random.  If you
    ///     need randomness, use boxaPermuteRandom().
    /// </remarks>
    public Boxa PermutePseudorandom()
    {
        Boxa boxas = this;

        int n = boxas.Count;
        Numa na = Numa.PseudorandomSequence(n, 0);
        Boxa boxad = boxas.SortByIndex(na);
        return boxad;
    }

    // leptonica: boxaPermuteRandom (boxfunc4.c)
    /// <summary>Permutes the boxes randomly.</summary>
    /// <param name="boxad">[optional] can be null or equal to boxas</param>
    /// <param name="boxas">input boxa</param>
    /// <returns>boxad with boxes permuted</returns>
    /// <remarks>
    /// (1) If boxad is null, make a copy of boxas and permute the copy.
    ///     Otherwise, boxad must be equal to boxas, and the operation
    ///     is done in-place.
    /// (2) If boxas is empty, return an empty boxad.
    /// (3) This does a random in-place permutation of the boxes,
    ///     by swapping each box in turn with a random box.  The
    ///     result is almost guaranteed not to have any boxes in their
    ///     original position.
    /// (4) MSVC rand() has MAX_RAND = 2^15 - 1, so it will not do
    ///     a proper permutation is the number of boxes exceeds this.
    /// </remarks>
    public static Boxa PermuteRandom(Boxa? boxad, Boxa boxas)
    {
        const string func = "boxaPermuteRandom";

        if (boxad != null && !ReferenceEquals(boxad, boxas))
            throw Lept.Arg(func, "boxad defined but in-place");

        if (boxad == null)
            boxad = boxas.Copy(L.L_COPY);
        int n;
        if ((n = boxad.Count) == 0)
            return boxad;
        int index = (int)((uint)CRand.Next() % (uint)n);
        index = Math.Max(1, index);
        boxad.SwapBoxesOrLog(0, index);
        for (int i = 1; i < n; i++) {
            index = (int)((uint)CRand.Next() % (uint)n);
            if (index == i) index--;
            boxad.SwapBoxesOrLog(i, index);
        }

        return boxad;
    }

    // leptonica: boxaSwapBoxes (boxfunc4.c)
    /// <summary>Swaps two boxes.</summary>
    /// <param name="i">index of a box that is to be swapped</param>
    /// <param name="j">index of the other box that is to be swapped</param>
    public void SwapBoxes(int i, int j)
    {
        string? error = TrySwapBoxes(i, j);
        if (error != null)
            throw Lept.Arg("boxaSwapBoxes", error);
    }

    /* The C caller ignores the error return; log it and carry on. */
    private void SwapBoxesOrLog(int i, int j)
    {
        string? error = TrySwapBoxes(i, j);
        if (error != null)
            Log.Error("boxaSwapBoxes", error);
    }

    private string? TrySwapBoxes(int i, int j)
    {
        int n = Count;
        if (i < 0 || i >= n)
            return "i invalid";
        if (j < 0 || j >= n)
            return "j invalid";
        if (i == j)
            return "i == j";

        Box? box = boxes[i];
        boxes[i] = boxes[j];
        boxes[j] = box;
        return null;
    }

    /*---------------------------------------------------------------------*
     *                     Boxa and Box Conversions                        *
     *---------------------------------------------------------------------*/
    // leptonica: boxaConvertToPta (boxfunc4.c)
    /// <summary>Converts the boxa to a pta with 2 or 4 points for each box.</summary>
    /// <param name="ncorners">2 or 4 for the representation of each box</param>
    /// <returns>pta with %ncorners points for each box in the boxa</returns>
    /// <remarks>
    /// (1) If ncorners == 2, we select the UL and LR corners.
    ///     Otherwise we save all 4 corners in this order: UL, UR, LL, LR.
    /// (2) Other boxa --> pta functions are:
    ///     * boxaExtractAsPta(): allows extraction of any dimension
    ///       and/or side location, with each in a separate pta.
    ///     * boxaExtractCorners(): extracts any of the four corners as a pta.
    /// </remarks>
    public Pta ConvertToPta(int ncorners)
    {
        Boxa boxa = this;

        if (ncorners != 2 && ncorners != 4)
            throw Lept.Arg("boxaConvertToPta", "ncorners not 2 or 4");

        int n = boxa.Count;
        Pta pta = Pta.Create(n);
        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_COPY);
            Pta pta1 = box.ConvertToPta(ncorners);
            pta.Join(pta1, 0, -1);
        }

        return pta;
    }
}

public sealed partial class Pta
{
    // leptonica: ptaConvertToBoxa (boxfunc4.c)
    /// <summary>Converts the pta to a boxa, with one box for each 2 or 4 points.</summary>
    /// <param name="ncorners">2 or 4 for the representation of each box</param>
    /// <returns>boxa with one box for each 2 or 4 points in the pta</returns>
    /// <remarks>
    /// (1) For 2 corners, the order of the 2 points is UL, LR.
    ///     For 4 corners, the order of points is UL, UR, LL, LR.
    /// (2) Each derived box is the minimum size containing all corners.
    /// </remarks>
    public Boxa ConvertToBoxa(int ncorners)
    {
        Pta pta = this;

        if (ncorners != 2 && ncorners != 4)
            throw Lept.Arg("ptaConvertToBoxa", "ncorners not 2 or 4");
        int n = pta.Count;
        if (n % ncorners != 0)
            throw Lept.Arg("ptaConvertToBoxa", "size % ncorners != 0");
        int nbox = n / ncorners;
        Boxa boxa = Boxa.Create(nbox);
        for (int i = 0; i < n; i += ncorners) {
            pta.GetIPt(i, out int x1, out int y1);
            pta.GetIPt(i + 1, out int x2, out int y2);
            if (ncorners == 2) {
                AddBoxOrLog(boxa, x1, y1, x2 - x1 + 1, y2 - y1 + 1);
                continue;
            }
            pta.GetIPt(i + 2, out int x3, out int y3);
            pta.GetIPt(i + 3, out int x4, out int y4);
            int x = Math.Min(x1, x3);
            int y = Math.Min(y1, y2);
            int xmax = Math.Max(x2, x4);
            int ymax = Math.Max(y3, y4);
            AddBoxOrLog(boxa, x, y, xmax - x + 1, ymax - y + 1);
        }

        return boxa;
    }

    /* C: boxCreate() returns NULL after logging, boxaAddBox() then fails on the
     * NULL box, and the caller ignores both. */
    private static void AddBoxOrLog(Boxa boxa, int x, int y, int w, int h)
    {
        Box box;
        try {
            box = Box.Create(x, y, w, h);
        } catch (ArgumentException e) {
            const string prefix = "Error in boxCreate: ";
            Log.Error("boxCreate", e.Message.StartsWith(prefix, StringComparison.Ordinal)
                                       ? e.Message.Substring(prefix.Length) : e.Message);
            Log.Error("boxaAddBox", "box not defined");
            return;
        }
        boxa.AddBox(box, L.L_INSERT);
    }
}

public sealed partial class Box
{
    // leptonica: boxConvertToPta (boxfunc4.c)
    /// <summary>Converts the box to a pta with 2 or 4 points.</summary>
    /// <param name="ncorners">2 or 4 for the representation of the box</param>
    /// <returns>pta with %ncorners points</returns>
    /// <remarks>
    /// (1) If ncorners == 2, we select the UL and LR corners.
    ///     Otherwise we save all 4 corners in this order: UL, UR, LL, LR.
    /// </remarks>
    public Pta ConvertToPta(int ncorners)
    {
        if (ncorners != 2 && ncorners != 4)
            throw Lept.Arg("boxConvertToPta", "ncorners not 2 or 4");

        Pta pta = Pta.Create(ncorners);
        GetGeometry(out int x, out int y, out int w, out int h);
        pta.AddPt(x, y);
        if (ncorners == 2) {
            pta.AddPt(x + w - 1, y + h - 1);
        } else {
            pta.AddPt(x + w - 1, y);
            pta.AddPt(x, y + h - 1);
            pta.AddPt(x + w - 1, y + h - 1);
        }

        return pta;
    }
}

public sealed partial class Pta
{
    // leptonica: ptaConvertToBox (boxfunc4.c)
    /// <summary>Converts a pta of 2 or 4 points to the minimum box containing them.</summary>
    /// <returns>box minimum containing all points in the pta</returns>
    /// <remarks>
    /// (1) For 2 corners, the order of the 2 points is UL, LR.
    ///     For 4 corners, the order of points is UL, UR, LL, LR.
    /// </remarks>
    public Box ConvertToBox()
    {
        Pta pta = this;

        int n = pta.Count;
        if (n != 2 && n != 4)
            throw Lept.Arg("ptaConvertToBox", "n must be 2 or 4");
        pta.GetIPt(0, out int x1, out int y1);
        pta.GetIPt(1, out int x2, out int y2);
        if (n == 2)
            return Box.Create(x1, y1, x2 - x1 + 1, y2 - y1 + 1);

            /* 4 corners */
        pta.GetIPt(2, out int x3, out int y3);
        pta.GetIPt(3, out int x4, out int y4);
        int x = Math.Min(x1, x3);
        int y = Math.Min(y1, y2);
        int xmax = Math.Max(x2, x4);
        int ymax = Math.Max(y3, y4);
        return Box.Create(x, y, xmax - x + 1, ymax - y + 1);
    }
}

public sealed partial class Boxa
{
    /*---------------------------------------------------------------------*
     *                    Miscellaneous Boxa functions                     *
     *---------------------------------------------------------------------*/
    // leptonica: boxaGetExtent (boxfunc4.c)
    /// <summary>Gets the size and the minimum box containing all valid boxes.</summary>
    /// <param name="w">width</param>
    /// <param name="h">height</param>
    /// <param name="box">minimum box containing all boxes in boxa</param>
    /// <remarks>
    /// (1) This computes the minimum rectangular bounding region
    ///     that contains all valid boxes in a boxa.
    /// (2) The returned w and h are the minimum size image
    ///     that would contain all boxes untranslated.
    /// (3) If there are no valid boxes, returned w and h are 0 and
    ///     all parameters in the returned box are 0.  This
    ///     is not an error, because an empty boxa is valid and
    ///     boxaGetExtent() is required for serialization.
    /// </remarks>
    public void GetExtent(out int w, out int h, out Box box)
    {
        Boxa boxa = this;

        w = 0;
        h = 0;
        int n = boxa.Count;
        int xmax = 0, ymax = 0;
        int xmin = 100000000, ymin = 100000000;
        bool found = false;
        for (int i = 0; i < n; i++) {
            boxa.GetBoxGeometry(i, out int x, out int y, out int bw, out int bh);
            if (bw <= 0 || bh <= 0)
                continue;
            found = true;
            xmin = Math.Min(xmin, x);
            ymin = Math.Min(ymin, y);
            xmax = Math.Max(xmax, x + bw);
            ymax = Math.Max(ymax, y + bh);
        }
        if (found == false)  /* no valid boxes in boxa */
            xmin = ymin = 0;
        w = xmax;
        h = ymax;
        box = Box.Create(xmin, ymin, xmax - xmin, ymax - ymin);
    }

    // leptonica: boxaGetCoverage (boxfunc4.c)
    /// <summary>Gets the fraction of a rectangle that is covered by the boxes.</summary>
    /// <param name="wc">width of overall clipping rectangle with UL corner at (0, 0)
    ///                  that is covered by the boxes</param>
    /// <param name="hc">height of overall clipping rectangle</param>
    /// <param name="exactflag">1 for guaranteeing an exact result; 0 for getting
    ///                         an exact result only if the boxes do not overlap</param>
    /// <returns>sum of box area as fraction of w * h</returns>
    /// <remarks>
    /// (1) The boxes in boxa are clipped to the input rectangle.
    /// (2) * When %exactflag == 1, we generate a 1 bpp pix of size
    ///       wc x hc, paint all the boxes black, and count the fg pixels.
    ///       This can take 1 msec on a large page with many boxes.
    ///     * When %exactflag == 0, we clip each box to the wc x hc region
    ///       and sum the resulting areas.  This is faster.
    ///     * The results are the same when none of the boxes overlap
    ///       within the wc x hc region.
    /// </remarks>
    public float GetCoverage(int wc, int hc, int exactflag)
    {
        Boxa boxa = this;

        int n = boxa.Count;
        if (n == 0)
            throw Lept.Arg("boxaGetCoverage", "no boxes in boxa");

        int sum;
        if (exactflag == 0) {  /* quick and dirty */
            sum = 0;
            for (int i = 0; i < n; i++) {
                Box box = boxa.GetBox(i, L.L_CLONE);
                Box? boxc;
                if ((boxc = box.ClipToRectangleOrNull(wc, hc)) != null) {
                    boxc.GetGeometry(out _, out _, out int w, out int h);
                    sum += w * h;
                } else {
                    Log.Error("boxClipToRectangle", "box outside rectangle");
                }
            }
        } else {  /* slower and exact */
            Pix pixt = Pix.Create(wc, hc, 1);
            for (int i = 0; i < n; i++) {
                Box box = boxa.GetBox(i, L.L_CLONE);
                box.GetGeometry(out int x, out int y, out int w, out int h);
                pixt.Rasterop(x, y, w, h, L.PIX_SET, null, 0, 0);
            }
            sum = pixt.CountPixels(null);
        }

        return (float)sum / (float)(wc * hc);
    }
}

public sealed partial class Boxaa
{
    // leptonica: boxaaSizeRange (boxfunc4.c)
    /// <summary>Gets the range of widths and heights of all boxes.</summary>
    /// <param name="minw">min width of all boxes</param>
    /// <param name="minh">min height of all boxes</param>
    /// <param name="maxw">max width of all boxes</param>
    /// <param name="maxh">max height of all boxes</param>
    public void SizeRange(out int minw, out int minh, out int maxw, out int maxh)
    {
        Boxaa baa = this;

        minw = minh = 100000000;
        maxw = maxh = 0;
        int n = baa.Count;
        for (int i = 0; i < n; i++) {
            Boxa boxa = baa.GetBoxa(i, L.L_CLONE);
            boxa.SizeRange(out int minbw, out int minbh, out int maxbw, out int maxbh);
            if (minbw < minw)
                minw = minbw;
            if (minbh < minh)
                minh = minbh;
            if (maxbw > maxw)
                maxw = maxbw;
            if (maxbh > maxh)
                maxh = maxbh;
        }
    }
}

public sealed partial class Boxa
{
    // leptonica: boxaSizeRange (boxfunc4.c)
    /// <summary>Gets the range of widths and heights of all boxes.</summary>
    /// <param name="minw">min width of all boxes</param>
    /// <param name="minh">min height of all boxes</param>
    /// <param name="maxw">max width of all boxes</param>
    /// <param name="maxh">max height of all boxes</param>
    public void SizeRange(out int minw, out int minh, out int maxw, out int maxh)
    {
        Boxa boxa = this;

        minw = minh = 100000000;
        maxw = maxh = 0;
        int n = boxa.Count;
        for (int i = 0; i < n; i++) {
            boxa.GetBoxGeometry(i, out _, out _, out int w, out int h);
            if (w < minw)
                minw = w;
            if (h < minh)
                minh = h;
            if (w > maxw)
                maxw = w;
            if (h > maxh)
                maxh = h;
        }
    }

    // leptonica: boxaLocationRange (boxfunc4.c)
    /// <summary>Gets the range of the UL corner locations of all boxes.</summary>
    /// <param name="minx">min (UL corner) x value of all boxes</param>
    /// <param name="miny">min (UL corner) y value of all boxes</param>
    /// <param name="maxx">max (UL corner) x value of all boxes</param>
    /// <param name="maxy">max (UL corner) y value of all boxes</param>
    public void LocationRange(out int minx, out int miny, out int maxx, out int maxy)
    {
        Boxa boxa = this;

        minx = miny = 100000000;
        maxx = maxy = 0;
        int n = boxa.Count;
        for (int i = 0; i < n; i++) {
            boxa.GetBoxGeometry(i, out int x, out int y, out _, out _);
            if (x < minx)
                minx = x;
            if (y < miny)
                miny = y;
            if (x > maxx)
                maxx = x;
            if (y > maxy)
                maxy = y;
        }
    }

    // leptonica: boxaGetSizes (boxfunc4.c)
    /// <summary>Gets the widths and heights of the valid boxes.</summary>
    /// <param name="naw">widths of valid boxes</param>
    /// <param name="nah">heights of valid boxes</param>
    /// <remarks>
    /// NOTE(upstream): the loop runs over the first n indices, where n is the
    /// number of valid boxes, not over all boxes.  If the boxa has invalid
    /// boxes, valid boxes beyond index n - 1 are not reported.
    /// </remarks>
    public void GetSizes(out Numa naw, out Numa nah)
    {
        Boxa boxa = this;

        // NOTE(upstream): n is the number of valid boxes, but it is used as the
        // loop limit over all indices (see docs/UPSTREAM_NOTES.md).
        int n = boxa.GetValidCount();
        naw = Numa.Create(n);
        nah = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            Box? box = boxa.GetValidBox(i, L.L_COPY);
            if (box != null) {
                box.GetGeometry(out _, out _, out int w, out int h);
                naw.AddNumber(w);
                nah.AddNumber(h);
            }
        }
    }

    // leptonica: boxaGetArea (boxfunc4.c)
    /// <summary>Gets the total area of the boxes.</summary>
    /// <returns>total area of all boxes</returns>
    /// <remarks>
    /// (1) Measures the total area of the boxes, without regard to overlaps.
    /// </remarks>
    public int GetArea()
    {
        Boxa boxa = this;

        int area = 0;
        int n = boxa.Count;
        for (int i = 0; i < n; i++) {
            boxa.GetBoxGeometry(i, out _, out _, out int w, out int h);
            area += w * h;
        }
        return area;
    }

    // leptonica: boxaDisplayTiled (boxfunc4.c)
    /// <summary>Displays each box separately in a tiled 32 bpp image.</summary>
    /// <param name="pixa">[optional] background for each box</param>
    /// <param name="first">index of first box</param>
    /// <param name="last">index of last box; use -1 to go to end</param>
    /// <param name="maxwidth">of output image</param>
    /// <param name="linewidth">width of box outlines, before scaling</param>
    /// <param name="scalefactor">applied to every box; use 1.0 for no scaling</param>
    /// <param name="background">0 for white, 1 for black; this is the color
    ///                          of the spacing between the images</param>
    /// <param name="spacing">between images, and on outside</param>
    /// <param name="border">width of black border added to each image;
    ///                      use 0 for no border</param>
    /// <returns>pixd of tiled images of boxes</returns>
    /// <remarks>
    /// (1) Displays each box separately in a tiled 32 bpp image.
    /// (2) If pixa is defined, it must have the same count as the boxa,
    ///     and it will be a background over with each box is rendered.
    ///     If pixa is not defined, the boxes will be rendered over
    ///     blank images of identical size.
    /// (3) See pixaDisplayTiledInRows() for other parameters.
    /// </remarks>
    public Pix DisplayTiled(Pixa? pixa, int first, int last, int maxwidth, int linewidth,
                            float scalefactor, int background, int spacing, int border)
    {
        const string func = "boxaDisplayTiled";
        Boxa boxas = this;

        Boxa boxa = boxas.SaveValid(L.L_COPY);
        int n = boxa.Count;
        if (pixa != null) {
            int npix = pixa.Count;
            if (n != npix)
                throw Lept.Arg(func, "boxa and pixa counts differ");
        }
        first = Math.Max(0, first);
        if (last < 0) last = n - 1;
        if (first >= n)
            throw Lept.Arg(func, "invalid first");
        if (last >= n) {
            Log.Warning(func, $"last = {last} is beyond max index = {n - 1}; adjusting");
            last = n - 1;
        }
        if (first > last)
            throw Lept.Arg(func, "first > last");

            /* Because the bitmap font will be reduced when tiled, choose the
             * font size inversely with the scale factor. */
        int fontsize;
        if (scalefactor > 0.8)
            fontsize = 6;
        else if (scalefactor > 0.6)
            fontsize = 10;
        else if (scalefactor > 0.4)
            fontsize = 14;
        else if (scalefactor > 0.3)
            fontsize = 18;
        else fontsize = 20;
        L_Bmf bmf = L_Bmf.Create(null, fontsize);

        Pixa pixat = Pixa.Create(n);
        boxa.GetExtent(out int w, out int h, out _);
        for (int i = first; i <= last; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            Pix pix1;
            if (pixa == null) {
                pix1 = Pix.Create(w, h, 32);
                pix1.SetAll();
            } else {
                pix1 = pixa.GetPix(i, L.L_COPY);
            }
            pix1.SetBorderVal(0, 0, 0, 2, 0x0000ff00);
            string buf = CFormat.Sprintf("%d", i);
            Pix pix2 = pix1.AddSingleTextblock(bmf, buf, 0x00ff0000, L.L_ADD_BELOW, out _);
            pix2.RenderBoxArb(box, linewidth, 255, 0, 0);
            pixat.AddPix(pix2, L.L_INSERT);
        }

        Pix pixd = pixat.DisplayTiledInRows(32, maxwidth, scalefactor, background,
                                            spacing, border);
        return pixd;
    }

}
