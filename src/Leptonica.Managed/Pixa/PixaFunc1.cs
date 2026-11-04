using System;

namespace Leptonica;

/*
 *  pixafunc1.c: Pixa and Pixaa selection, sorting, scaling, rotation,
 *  borders, size and depth information, depth conversion, comparison.
 *
 *  Differences from C:
 *    - No reference counting; clones are the same reference.
 *    - Functions that return NULL or an error code in C throw instead,
 *      except where the C code logs and carries on (noted at the place).
 *    - Optional output pointers are out parameters.
 *    - pixaSort() works on the boxa of the pixa; as in C it can replace
 *      an empty boxa by one with the full size of each pix.
 */

public sealed partial class Pix
{
    /*---------------------------------------------------------------------*
     *                      Pix selection by component                     *
     *---------------------------------------------------------------------*/
    /// <summary>The rendering step shared by the pixSelectBy*() functions.</summary>
    private Pix RenderSelected(Pixa pixad)
    {
        Pix pixs = this;
        GetDimensions(out int w, out int h, out _);
        int count = pixad.Count;
        Pix pixd;
        if (count == 0) {  /* return empty pix */
            pixd = CreateTemplate(pixs);
        } else {
            pixd = pixad.Display(w, h);
            pixd.CopyResolution(pixs);
            pixd.CopyColormap(pixs);
            pixd.CopyText(pixs);
            pixd.CopyInputFormat(pixs);
        }
        return pixd;
    }

    // leptonica: pixSelectBySize (pixafunc1.c)
    /// <param name="width">threshold dimensions</param>
    /// <param name="height">threshold dimensions</param>
    /// <param name="connectivity">4 or 8</param>
    /// <param name="type">L_SELECT_WIDTH, L_SELECT_HEIGHT, L_SELECT_IF_EITHER, L_SELECT_IF_BOTH</param>
    /// <param name="relation">L_SELECT_IF_LT, L_SELECT_IF_GT, L_SELECT_IF_LTE, L_SELECT_IF_GTE</param>
    /// <param name="pchanged">true if changed</param>
    /// <returns>filtered pixd</returns>
    /// <remarks>
    /// (1) The args specify constraints on the size of the components that are kept.
    /// (2) If unchanged, returns a copy of pixs.  Otherwise, returns a new pix with the filtered components.
    /// (3) If the selection type is L_SELECT_WIDTH, the input height is ignored, and v.v.
    /// (4) To keep small components, use relation = L_SELECT_IF_LT or L_SELECT_IF_LTE.
    ///     To keep large components, use relation = L_SELECT_IF_GT or L_SELECT_IF_GTE.
    /// </remarks>
    public Pix SelectBySize(int width, int height, int connectivity, int type,
                            int relation, out bool pchanged)
    {
        const string func = "pixSelectBySize";
        Pix pixs = this;
        pchanged = false;
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");
        if (type != L.L_SELECT_WIDTH && type != L.L_SELECT_HEIGHT &&
            type != L.L_SELECT_IF_EITHER && type != L.L_SELECT_IF_BOTH)
            throw Lept.Arg(func, "invalid type");
        if (relation != L.L_SELECT_IF_LT && relation != L.L_SELECT_IF_GT &&
            relation != L.L_SELECT_IF_LTE && relation != L.L_SELECT_IF_GTE)
            throw Lept.Arg(func, "invalid relation");

            /* Check if any components exist */
        if (pixs.Zero())
            return Copy(null, pixs);

            /* Identify and select the components */
        pixs.ConnComp(out Pixa pixas, connectivity);
        Pixa pixad = pixas.SelectBySize(width, height, type, relation, out bool changed);

        if (!changed)
            return Copy(null, pixs);

            /* Render the result */
        pchanged = true;
        return RenderSelected(pixad);
    }

    // leptonica: pixSelectByPerimToAreaRatio (pixafunc1.c)
    /// <summary>Filters the connected components of a 1 bpp image by the ratio of boundary pixels to foreground pixels.</summary>
    /// <param name="thresh">Threshold ratio of foreground boundary pixels to foreground pixels.</param>
    /// <param name="connectivity">Connectivity of the components: 4 or 8.</param>
    /// <param name="type">Comparison: <see cref="L.L_SELECT_IF_LT"/>, <see cref="L.L_SELECT_IF_GT"/>, <see cref="L.L_SELECT_IF_LTE"/> or <see cref="L.L_SELECT_IF_GTE"/>.</param>
    /// <param name="pchanged">Receives true if components were removed; false if a copy of the input is returned.</param>
    /// <returns>A copy of the image if nothing changed, otherwise a new image with the filtered components.</returns>
    /// <remarks>
    /// <para>This filters "thick" components, where a thick component is defined to have a ratio of
    /// boundary to interior pixels that is smaller than a given threshold value.</para>
    /// <para>Use <see cref="L.L_SELECT_IF_LT"/> or <see cref="L.L_SELECT_IF_LTE"/> to save the thicker components,
    /// and <see cref="L.L_SELECT_IF_GT"/> or <see cref="L.L_SELECT_IF_GTE"/> to remove them.</para>
    /// </remarks>
    public Pix SelectByPerimToAreaRatio(float thresh, int connectivity, int type, out bool pchanged)
    {
        return SelectByRatio("pixSelectByPerimToAreaRatio", 0, thresh, connectivity, type, out pchanged);
    }

    // leptonica: pixSelectByPerimSizeRatio (pixafunc1.c)
    /// <summary>Filters the connected components of a 1 bpp image by the ratio of boundary pixels to the bounding-box circumference.</summary>
    /// <param name="thresh">Threshold ratio of foreground boundary pixels to the bounding-box circumference.</param>
    /// <param name="connectivity">Connectivity of the components: 4 or 8.</param>
    /// <param name="type">Comparison: <see cref="L.L_SELECT_IF_LT"/>, <see cref="L.L_SELECT_IF_GT"/>, <see cref="L.L_SELECT_IF_LTE"/> or <see cref="L.L_SELECT_IF_GTE"/>.</param>
    /// <param name="pchanged">Receives true if components were removed; false if a copy of the input is returned.</param>
    /// <returns>A copy of the image if nothing changed, otherwise a new image with the filtered components.</returns>
    /// <remarks>
    /// <para>This filters components with smooth versus dendritic shape, using the ratio of the foreground boundary
    /// pixels to the circumference of the bounding box, and comparing it to a threshold value.</para>
    /// <para>Use <see cref="L.L_SELECT_IF_LT"/> or <see cref="L.L_SELECT_IF_LTE"/> to save the smooth boundary components,
    /// and <see cref="L.L_SELECT_IF_GT"/> or <see cref="L.L_SELECT_IF_GTE"/> to remove them.</para>
    /// </remarks>
    public Pix SelectByPerimSizeRatio(float thresh, int connectivity, int type, out bool pchanged)
    {
        return SelectByRatio("pixSelectByPerimSizeRatio", 1, thresh, connectivity, type, out pchanged);
    }

    // leptonica: pixSelectByAreaFraction (pixafunc1.c)
    /// <summary>Filters the connected components of a 1 bpp image by the fraction of foreground pixels in their bounding box.</summary>
    /// <param name="thresh">Threshold ratio of foreground pixels to bounding-box area (width times height).</param>
    /// <param name="connectivity">Connectivity of the components: 4 or 8.</param>
    /// <param name="type">Comparison: <see cref="L.L_SELECT_IF_LT"/>, <see cref="L.L_SELECT_IF_GT"/>, <see cref="L.L_SELECT_IF_LTE"/> or <see cref="L.L_SELECT_IF_GTE"/>.</param>
    /// <param name="pchanged">Receives true if components were removed; false if a copy of the input is returned.</param>
    /// <returns>A copy of the image if nothing changed, otherwise a new image with the filtered components.</returns>
    /// <remarks>
    /// Use <see cref="L.L_SELECT_IF_LT"/> or <see cref="L.L_SELECT_IF_LTE"/> to save components with less than the threshold
    /// fraction of foreground, and <see cref="L.L_SELECT_IF_GT"/> or <see cref="L.L_SELECT_IF_GTE"/> to remove them.
    /// </remarks>
    public Pix SelectByAreaFraction(float thresh, int connectivity, int type, out bool pchanged)
    {
        return SelectByRatio("pixSelectByAreaFraction", 2, thresh, connectivity, type, out pchanged);
    }

    // leptonica: pixSelectByArea (pixafunc1.c)
    /// <summary>Filters the connected components of a 1 bpp image by the number of foreground pixels in each.</summary>
    /// <param name="thresh">Threshold number of foreground pixels.</param>
    /// <param name="connectivity">Connectivity of the components: 4 or 8.</param>
    /// <param name="type">Comparison: <see cref="L.L_SELECT_IF_LT"/>, <see cref="L.L_SELECT_IF_GT"/>, <see cref="L.L_SELECT_IF_LTE"/> or <see cref="L.L_SELECT_IF_GTE"/>.</param>
    /// <param name="pchanged">Receives true if components were removed; false if a copy of the input is returned.</param>
    /// <returns>A copy of the image if nothing changed, otherwise a new image with the filtered components.</returns>
    /// <remarks>
    /// Use <see cref="L.L_SELECT_IF_LT"/> or <see cref="L.L_SELECT_IF_LTE"/> to save components with less than the threshold
    /// number of foreground pixels, and <see cref="L.L_SELECT_IF_GT"/> or <see cref="L.L_SELECT_IF_GTE"/> to remove them.
    /// </remarks>
    public Pix SelectByArea(float thresh, int connectivity, int type, out bool pchanged)
    {
        return SelectByRatio("pixSelectByArea", 3, thresh, connectivity, type, out pchanged);
    }

    // leptonica: pixSelectByWidthHeightRatio (pixafunc1.c)
    /// <summary>Filters the connected components of a 1 bpp image by their width-to-height ratio.</summary>
    /// <param name="thresh">Threshold ratio of width to height.</param>
    /// <param name="connectivity">Connectivity of the components: 4 or 8.</param>
    /// <param name="type">Comparison: <see cref="L.L_SELECT_IF_LT"/>, <see cref="L.L_SELECT_IF_GT"/>, <see cref="L.L_SELECT_IF_LTE"/> or <see cref="L.L_SELECT_IF_GTE"/>.</param>
    /// <param name="pchanged">Receives true if components were removed; false if a copy of the input is returned.</param>
    /// <returns>A copy of the image if nothing changed, otherwise a new image with the filtered components.</returns>
    /// <remarks>
    /// Use <see cref="L.L_SELECT_IF_LT"/> or <see cref="L.L_SELECT_IF_LTE"/> to save components with less than the threshold ratio,
    /// and <see cref="L.L_SELECT_IF_GT"/> or <see cref="L.L_SELECT_IF_GTE"/> to remove them.
    /// </remarks>
    public Pix SelectByWidthHeightRatio(float thresh, int connectivity, int type, out bool pchanged)
    {
        return SelectByRatio("pixSelectByWidthHeightRatio", 4, thresh, connectivity, type, out pchanged);
    }

    /// <summary>The common body of the five pixSelectBy*() functions that filter by a threshold.</summary>
    private Pix SelectByRatio(string func, int kind, float thresh, int connectivity, int type,
                              out bool pchanged)
    {
        Pix pixs = this;
        pchanged = false;
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");
        if (type != L.L_SELECT_IF_LT && type != L.L_SELECT_IF_GT &&
            type != L.L_SELECT_IF_LTE && type != L.L_SELECT_IF_GTE)
            throw Lept.Arg(func, "invalid type");

            /* Check if any components exist */
        if (pixs.Zero())
            return Copy(null, pixs);

            /* Filter components */
        pixs.ConnComp(out Pixa pixas, connectivity);
        Pixa pixad;
        bool changed;
        switch (kind) {
        case 0: pixad = pixas.SelectByPerimToAreaRatio(thresh, type, out changed); break;
        case 1: pixad = pixas.SelectByPerimSizeRatio(thresh, type, out changed); break;
        case 2: pixad = pixas.SelectByAreaFraction(thresh, type, out changed); break;
        case 3: pixad = pixas.SelectByArea(thresh, type, out changed); break;
        default: pixad = pixas.SelectByWidthHeightRatio(thresh, type, out changed); break;
        }

        if (!changed)
            return Copy(null, pixs);

            /* Render the result */
        pchanged = true;
        return RenderSelected(pixad);
    }

    // leptonica: pixRemoveWithIndicator (pixafunc1.c)
    /// <param name="pixa">of connected components in this pix</param>
    /// <param name="na">numa indicator: remove components corresponding to 1s</param>
    /// <remarks>
    /// (1) This complements pixAddWithIndicator().  Here, the selected components are subtracted from this pix.
    /// (2) This pix is 1 bpp; it is modified in place.
    /// </remarks>
    public void RemoveWithIndicator(Pixa pixa, Numa na)
    {
        Pix pixs = this;
        int n = pixa.Count;
        if (n != na.Count)
            throw Lept.Arg("pixRemoveWithIndicator", "pixa and na sizes not equal");

        for (int i = 0; i < n; i++) {
            int ival = na.GetIValue(i);
            if (ival == 1) {
                Pix pix = pixa.GetPix(i, L.L_CLONE);
                Box box = pixa.GetBox(i, L.L_CLONE)
                    ?? throw Lept.Arg("pixRemoveWithIndicator", "box not defined");
                box.GetGeometry(out int x, out int y, out int w, out int h);
                pixs.Rasterop(x, y, w, h, L.PIX_DST & L.PIX_NOT(L.PIX_SRC), pix, 0, 0);
            }
        }
    }

    // leptonica: pixAddWithIndicator (pixafunc1.c)
    /// <param name="pixa">of connected components, some of which will be put into this pix</param>
    /// <param name="na">numa indicator: add components corresponding to 1s</param>
    /// <remarks>(1) This complements pixRemoveWithIndicator().  Here, the selected components are added to this pix.</remarks>
    public void AddWithIndicator(Pixa pixa, Numa na)
    {
        Pix pixs = this;
        int n = pixa.Count;
        if (n != na.Count)
            throw Lept.Arg("pixAddWithIndicator", "pixa and na sizes not equal");

        for (int i = 0; i < n; i++) {
            int ival = na.GetIValue(i);
            if (ival == 1) {
                Pix pix = pixa.GetPix(i, L.L_CLONE);
                Box box = pixa.GetBox(i, L.L_CLONE)
                    ?? throw Lept.Arg("pixAddWithIndicator", "box not defined");
                box.GetGeometry(out int x, out int y, out int w, out int h);
                pixs.Rasterop(x, y, w, h, L.PIX_SRC | L.PIX_DST, pix, 0, 0);
            }
        }
    }
}

public sealed partial class Pixa
{
    /* Minimum number of components for which the O(n) binsort is used */
    private const int MinCompsForBinSort = 200;
    private const float MinAngleToRotate = 0.001f;  /* radians; ~0.06 deg */

    /*---------------------------------------------------------------------*
     *                           Pixa selection                            *
     *---------------------------------------------------------------------*/
    private static void CheckSizeSelect(string func, int type, int relation)
    {
        if (type != L.L_SELECT_WIDTH && type != L.L_SELECT_HEIGHT &&
            type != L.L_SELECT_IF_EITHER && type != L.L_SELECT_IF_BOTH)
            throw Lept.Arg(func, "invalid type");
        if (relation != L.L_SELECT_IF_LT && relation != L.L_SELECT_IF_GT &&
            relation != L.L_SELECT_IF_LTE && relation != L.L_SELECT_IF_GTE)
            throw Lept.Arg(func, "invalid relation");
    }

    private static void CheckThreshType(string func, int type)
    {
        if (type != L.L_SELECT_IF_LT && type != L.L_SELECT_IF_GT &&
            type != L.L_SELECT_IF_LTE && type != L.L_SELECT_IF_GTE)
            throw Lept.Arg(func, "invalid type");
    }

    // leptonica: pixaSelectBySize (pixafunc1.c)
    /// <param name="width">threshold dimensions</param>
    /// <param name="height">threshold dimensions</param>
    /// <param name="type">L_SELECT_WIDTH, L_SELECT_HEIGHT, L_SELECT_IF_EITHER, L_SELECT_IF_BOTH</param>
    /// <param name="relation">L_SELECT_IF_LT, L_SELECT_IF_GT, L_SELECT_IF_LTE, L_SELECT_IF_GTE</param>
    /// <param name="changed">true if the output differs from the input</param>
    /// <remarks>
    /// (1) The args specify constraints on the size of the components that are kept.
    /// (2) Uses pix and box clones in the new pixa.
    /// (3) If the selection type is L_SELECT_WIDTH, the input height is ignored, and v.v.
    /// (4) To keep small components, use relation = L_SELECT_IF_LT or L_SELECT_IF_LTE.
    ///     To keep large components, use relation = L_SELECT_IF_GT or L_SELECT_IF_GTE.
    /// </remarks>
    public Pixa SelectBySize(int width, int height, int type, int relation, out bool changed)
    {
        CheckSizeSelect("pixaSelectBySize", type, relation);

            /* Compute the indicator array for saving components */
        Numa na = MakeSizeIndicator(width, height, type, relation);

            /* Filter to get output */
        return SelectWithIndicator(na, out changed);
    }

    // leptonica: pixaMakeSizeIndicator (pixafunc1.c)
    /// <returns>na indicator array</returns>
    /// <remarks>
    /// (1) The args specify constraints on the size of the components that are kept.
    /// (2) If the selection type is L_SELECT_WIDTH, the input height is ignored, and v.v.
    /// </remarks>
    public Numa MakeSizeIndicator(int width, int height, int type, int relation)
    {
        CheckSizeSelect("pixaMakeSizeIndicator", type, relation);

        int n = Count;
        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            int ival = 0;
            GetPixDimensions(i, out int w, out int h, out _);
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
                Log.Warning("pixaMakeSizeIndicator", "can't get here!");
                break;
            }
            na.AddNumber(ival);
        }

        return na;
    }

    // leptonica: pixaSelectByPerimToAreaRatio (pixafunc1.c)
    /// <summary>Selects the images whose ratio of foreground boundary pixels to foreground pixels satisfies a threshold test.</summary>
    /// <param name="thresh">Threshold ratio of foreground boundary pixels to foreground pixels.</param>
    /// <param name="type">Comparison: <see cref="L.L_SELECT_IF_LT"/>, <see cref="L.L_SELECT_IF_GT"/>, <see cref="L.L_SELECT_IF_LTE"/> or <see cref="L.L_SELECT_IF_GTE"/>.</param>
    /// <param name="pchanged">Receives true if images were removed; false if a clone is returned.</param>
    /// <returns>A clone of this array if nothing is removed, otherwise a new array that uses clones of the selected images and boxes.</returns>
    public Pixa SelectByPerimToAreaRatio(float thresh, int type, out bool pchanged)
    {
        CheckThreshType("pixaSelectByPerimToAreaRatio", type);
        Numa na = FindPerimToAreaRatio();
        Numa nai = na.MakeThresholdIndicator(thresh, type);
        return SelectWithIndicator(nai, out pchanged);
    }

    // leptonica: pixaSelectByPerimSizeRatio (pixafunc1.c)
    /// <summary>Selects the images whose ratio of foreground boundary pixels to bounding-box circumference satisfies a threshold test.</summary>
    /// <param name="thresh">Threshold ratio of foreground boundary pixels to the bounding-box circumference.</param>
    /// <param name="type">Comparison: <see cref="L.L_SELECT_IF_LT"/>, <see cref="L.L_SELECT_IF_GT"/>, <see cref="L.L_SELECT_IF_LTE"/> or <see cref="L.L_SELECT_IF_GTE"/>.</param>
    /// <param name="pchanged">Receives true if images were removed; false if a clone is returned.</param>
    /// <returns>A clone of this array if nothing is removed, otherwise a new array that uses clones of the selected images and boxes.</returns>
    public Pixa SelectByPerimSizeRatio(float thresh, int type, out bool pchanged)
    {
        CheckThreshType("pixaSelectByPerimSizeRatio", type);
        Numa na = FindPerimSizeRatio();
        Numa nai = na.MakeThresholdIndicator(thresh, type);
        return SelectWithIndicator(nai, out pchanged);
    }

    // leptonica: pixaSelectByAreaFraction (pixafunc1.c)
    /// <summary>Selects the images whose fraction of foreground pixels in their bounding box satisfies a threshold test.</summary>
    /// <param name="thresh">Threshold ratio of foreground pixels to image area (width times height).</param>
    /// <param name="type">Comparison: <see cref="L.L_SELECT_IF_LT"/>, <see cref="L.L_SELECT_IF_GT"/>, <see cref="L.L_SELECT_IF_LTE"/> or <see cref="L.L_SELECT_IF_GTE"/>.</param>
    /// <param name="pchanged">Receives true if images were removed; false if a clone is returned.</param>
    /// <returns>A clone of this array if nothing is removed, otherwise a new array that uses clones of the selected images and boxes.</returns>
    public Pixa SelectByAreaFraction(float thresh, int type, out bool pchanged)
    {
        CheckThreshType("pixaSelectByAreaFraction", type);
        Numa na = FindAreaFraction();
        Numa nai = na.MakeThresholdIndicator(thresh, type);
        return SelectWithIndicator(nai, out pchanged);
    }

    // leptonica: pixaSelectByArea (pixafunc1.c)
    /// <summary>Selects the images whose number of foreground pixels satisfies a threshold test.</summary>
    /// <param name="thresh">Threshold number of foreground pixels.</param>
    /// <param name="type">Comparison: <see cref="L.L_SELECT_IF_LT"/>, <see cref="L.L_SELECT_IF_GT"/>, <see cref="L.L_SELECT_IF_LTE"/> or <see cref="L.L_SELECT_IF_GTE"/>.</param>
    /// <param name="pchanged">Receives true if images were removed; false if a clone is returned.</param>
    /// <returns>A clone of this array if nothing is removed, otherwise a new array that uses clones of the selected images and boxes.</returns>
    public Pixa SelectByArea(float thresh, int type, out bool pchanged)
    {
        CheckThreshType("pixaSelectByArea", type);
        Numa na = CountPixels();
        Numa nai = na.MakeThresholdIndicator(thresh, type);
        return SelectWithIndicator(nai, out pchanged);
    }

    // leptonica: pixaSelectByWidthHeightRatio (pixafunc1.c)
    /// <summary>Selects the images whose width-to-height ratio satisfies a threshold test.</summary>
    /// <param name="thresh">Threshold ratio of width to height.</param>
    /// <param name="type">Comparison: <see cref="L.L_SELECT_IF_LT"/>, <see cref="L.L_SELECT_IF_GT"/>, <see cref="L.L_SELECT_IF_LTE"/> or <see cref="L.L_SELECT_IF_GTE"/>.</param>
    /// <param name="pchanged">Receives true if images were removed; false if a clone is returned.</param>
    /// <returns>A clone of this array if nothing is removed, otherwise a new array that uses clones of the selected images and boxes.</returns>
    public Pixa SelectByWidthHeightRatio(float thresh, int type, out bool pchanged)
    {
        CheckThreshType("pixaSelectByWidthHeightRatio", type);
        Numa na = FindWidthHeightRatio();
        Numa nai = na.MakeThresholdIndicator(thresh, type);
        return SelectWithIndicator(nai, out pchanged);
    }

    // leptonica: pixaSelectByNumConnComp (pixafunc1.c)
    /// <param name="nmin">minimum number of components</param>
    /// <param name="nmax">maximum number of components</param>
    /// <param name="connectivity">4 or 8</param>
    /// <param name="pchanged">true if changed; false if a clone is returned</param>
    /// <remarks>(1) Returns a pixa clone if no components are removed. (2) This filters by the number of connected components in a given range.</remarks>
    public Pixa SelectByNumConnComp(int nmin, int nmax, int connectivity, out bool pchanged)
    {
        pchanged = false;
        if (nmin > nmax)
            throw Lept.Arg("pixaSelectByNumConnComp", "nmin > nmax");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg("pixaSelectByNumConnComp", "connectivity not 4 or 8");

            /* Get indicator array based on number of c.c. */
        int n = Count;
        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix = GetPix(i, L.L_CLONE);
            int count = pix.CountConnComp(connectivity);
            if (count >= nmin && count <= nmax)
                na.AddNumber(1);
            else
                na.AddNumber(0);
        }

            /* Filter to get output */
        return SelectWithIndicator(na, out pchanged);
    }

    // leptonica: pixaSelectWithIndicator (pixafunc1.c)
    /// <param name="na">indicator numa</param>
    /// <param name="pchanged">true if changed; false if a clone is returned</param>
    /// <remarks>
    /// (1) Returns a pixa clone if no components are removed.
    /// (2) Uses pix and box clones in the new pixa.
    /// (3) The indicator numa has values 0 (ignore) and 1 (accept).
    /// (4) If the source boxa is not fully populated, it is left empty in the dest pixa.
    /// </remarks>
    public Pixa SelectWithIndicator(Numa na, out bool pchanged)
    {
        Pixa pixas = this;
        int nsave = 0;
        int n = na.Count;
        for (int i = 0; i < n; i++) {
            int ival = na.GetIValue(i);
            if (ival == 1) nsave++;
        }

        if (nsave == n) {
            pchanged = false;
            return pixas.Copy(L.L_CLONE);
        }
        pchanged = true;
        Pixa pixad = Create(nsave);
        int nbox = pixas.BoxaCount;
        for (int i = 0; i < n; i++) {
            int ival = na.GetIValue(i);
            if (ival == 0) continue;
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            pixad.AddPix(pix1, L.L_INSERT);
            if (nbox == n) {   /* fully populated boxa */
                Box? box = pixas.GetBox(i, L.L_CLONE);
                if (box != null)
                    pixad.AddBox(box, L.L_INSERT);
                else
                    Log.Error("pixaAddBox", "box not defined");
            }
        }

        return pixad;
    }

    // leptonica: pixaSelectWithString (pixafunc1.c)
    /// <param name="str">string of indices into pixa, giving the pix to be selected</param>
    /// <param name="perror">true if any indices are invalid</param>
    /// <remarks>
    /// (1) Returns a pixa with copies of selected pix.
    /// (2) Associated boxes are also copied, if fully populated.
    /// </remarks>
    public Pixa SelectWithString(string str, out bool perror)
    {
        const string func = "pixaSelectWithString";
        Pixa pixas = this;
        perror = false;

        Numa na = Numa.CreateFromString(str);
        int nval = na.Count;
        if (nval == 0)
            throw Lept.Arg(func, "no indices found");
        na.GetMax(out float maxval, out _);
        int imaxval = (int)(maxval + 0.1);
        int nbox = pixas.BoxaCount;
        int npix = pixas.Count;
        if (imaxval >= npix) {
            perror = true;
            Log.Error(func, $"max index = {imaxval}, size of pixa = {npix}");
        }

        Pixa pixad = Create(nval);
        for (int i = 0; i < nval; i++) {
            int val = na.GetIValue(i);
            if (val < 0 || val >= npix) {
                Log.Error(func, $"index {val} out of range of pix");
                continue;
            }
            Pix pix1 = pixas.GetPix(val, L.L_COPY);
            pixad.AddPix(pix1, L.L_INSERT);
            if (nbox == npix) {   /* fully populated boxa */
                Box? box = pixas.GetBox(val, L.L_COPY);
                if (box != null)
                    pixad.AddBox(box, L.L_INSERT);
                else
                    Log.Error("pixaAddBox", "box not defined");
            }
        }
        return pixad;
    }

    // leptonica: pixaRenderComponent (pixafunc1.c)
    /// <param name="pixs">[optional] 1 bpp pix</param>
    /// <param name="pixa">of 1 bpp connected components, one of which will be rendered in pixs,
    ///                    with its origin determined by the associated box</param>
    /// <param name="index">of component to be rendered</param>
    /// <remarks>
    /// (1) If pixs is null, this generates an empty pix of a size determined by union of the
    ///     component bounding boxes, and including the origin.
    /// (2) The selected component is blitted into pixs.
    /// </remarks>
    public static Pix RenderComponent(Pix? pixs, Pixa pixa, int index)
    {
        const string func = "pixaRenderComponent";
        int n = pixa.Count;
        if (index < 0 || index >= n)
            throw Lept.Arg(func, "invalid index");
        if (pixs != null && pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        pixa.VerifyDepth(out _, out int maxd);
        if (maxd > 1)
            throw Lept.Arg(func, "not all pix with d == 1");

        Boxa boxa = pixa.GetBoxa(L.L_CLONE);
        int w, h;
        if (pixs == null) {
            boxa.GetExtent(out w, out h, out _);
            pixs = Pix.Create(w, h, 1);
        }

        Pix pix = pixa.GetPix(index, L.L_CLONE);
        Box box = boxa.GetBox(index, L.L_CLONE);
        box.GetGeometry(out int x, out int y, out w, out h);
        pixs.Rasterop(x, y, w, h, L.PIX_SRC | L.PIX_DST, pix, 0, 0);

        return pixs;
    }

    /*---------------------------------------------------------------------*
     *                              Sort functions                         *
     *---------------------------------------------------------------------*/
    // leptonica: pixaSort (pixafunc1.c)
    /// <param name="sorttype">L_SORT_BY_X, L_SORT_BY_Y, L_SORT_BY_WIDTH,
    ///                        L_SORT_BY_HEIGHT, L_SORT_BY_MIN_DIMENSION,
    ///                        L_SORT_BY_MAX_DIMENSION, L_SORT_BY_PERIMETER,
    ///                        L_SORT_BY_AREA, L_SORT_BY_ASPECT_RATIO</param>
    /// <param name="sortorder">L_SORT_INCREASING, L_SORT_DECREASING</param>
    /// <param name="pnaindex">index of sorted order into original array</param>
    /// <param name="copyflag">L_COPY, L_CLONE</param>
    /// <returns>pixad sorted version of this pixa</returns>
    /// <remarks>
    /// (1) This sorts based on the data in the boxa.  If the boxa count is not the same as the
    ///     pixa count, this returns an error.
    /// (2) If the boxa is empty, it makes one corresponding to the dimensions of each pix,
    ///     which allows meaningful sorting on all types except x and y.
    /// (3) The copyflag refers to the pix and box copies that are inserted into the sorted pixa.
    ///     These are either L_COPY or L_CLONE.
    /// </remarks>
    public Pixa Sort(int sorttype, int sortorder, out Numa? pnaindex, int copyflag)
    {
        const string func = "pixaSort";
        Pixa pixas = this;
        pnaindex = null;
        if (sorttype != L.L_SORT_BY_X && sorttype != L.L_SORT_BY_Y &&
            sorttype != L.L_SORT_BY_WIDTH && sorttype != L.L_SORT_BY_HEIGHT &&
            sorttype != L.L_SORT_BY_MIN_DIMENSION &&
            sorttype != L.L_SORT_BY_MAX_DIMENSION &&
            sorttype != L.L_SORT_BY_PERIMETER &&
            sorttype != L.L_SORT_BY_AREA &&
            sorttype != L.L_SORT_BY_ASPECT_RATIO)
            throw Lept.Arg(func, "invalid sort type");
        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg(func, "invalid sort order");
        if (copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg(func, "invalid copy flag");

            /* Check the pixa and boxa counts. Make a boxa if required. */
        int n = pixas.Count;
        if (n == 0) {
            Log.Info(func, "no pix in pixa");
            return pixas.Copy(copyflag);
        }
        Boxa boxa = pixas.Boxa;   /* not owned */
        int nb = boxa.Count;
        if (nb == 0) {
            pixas.SetFullSizeBoxa();
            nb = n;
            boxa = pixas.Boxa;
            if (sorttype == L.L_SORT_BY_X || sorttype == L.L_SORT_BY_Y)
                Log.Warning(func, "sort by x or y where all values are 0");
        }
        if (nb != n)
            throw Lept.Arg(func, "boxa and pixa counts differ");

            /* Use O(n) binsort if possible */
        if (n > MinCompsForBinSort &&
            ((sorttype == L.L_SORT_BY_X) || (sorttype == L.L_SORT_BY_Y) ||
             (sorttype == L.L_SORT_BY_WIDTH) || (sorttype == L.L_SORT_BY_HEIGHT) ||
             (sorttype == L.L_SORT_BY_PERIMETER)))
            return pixas.BinSort(sorttype, sortorder, out pnaindex, copyflag);

            /* Build up numa of specific data */
        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            boxa.GetBoxGeometry(i, out int x, out int y, out int w, out int h);
            switch (sorttype) {
            case L.L_SORT_BY_X:
                na.AddNumber(x);
                break;
            case L.L_SORT_BY_Y:
                na.AddNumber(y);
                break;
            case L.L_SORT_BY_WIDTH:
                na.AddNumber(w);
                break;
            case L.L_SORT_BY_HEIGHT:
                na.AddNumber(h);
                break;
            case L.L_SORT_BY_MIN_DIMENSION:
                na.AddNumber(Math.Min(w, h));
                break;
            case L.L_SORT_BY_MAX_DIMENSION:
                na.AddNumber(Math.Max(w, h));
                break;
            case L.L_SORT_BY_PERIMETER:
                na.AddNumber(w + h);
                break;
            case L.L_SORT_BY_AREA:
                na.AddNumber(w * h);
                break;
            case L.L_SORT_BY_ASPECT_RATIO:
                na.AddNumber((float)w / (float)h);
                break;
            default:
                Log.Warning(func, "invalid sort type");
                break;
            }
        }

            /* Get the sort index for data array */
        Numa naindex = na.GetSortIndex(sortorder);

            /* Build up sorted pixa using sort index */
        Pixa pixad = pixas.SortByIndex(naindex, copyflag);
        pnaindex = naindex;
        return pixad;
    }

    // leptonica: pixaBinSort (pixafunc1.c)
    /// <summary>Sorts the array using a bin sort on a geometric property of the boxes.</summary>
    /// <param name="sorttype">Sort key: <see cref="L.L_SORT_BY_X"/>, <see cref="L.L_SORT_BY_Y"/>, <see cref="L.L_SORT_BY_WIDTH"/>, <see cref="L.L_SORT_BY_HEIGHT"/> or <see cref="L.L_SORT_BY_PERIMETER"/>.</param>
    /// <param name="sortorder">Sort order: <see cref="L.L_SORT_INCREASING"/> or <see cref="L.L_SORT_DECREASING"/>.</param>
    /// <param name="pnaindex">Receives the sort index that maps old positions to new ones.</param>
    /// <param name="copyflag">Whether the images and boxes in the sorted array are copied or cloned: <see cref="L.L_COPY"/> or <see cref="L.L_CLONE"/>.</param>
    /// <returns>The sorted array.</returns>
    /// <exception cref="ArgumentException">An argument is invalid, or the box count differs from the image count.</exception>
    /// <remarks>
    /// <para>This sorts based on the data in the box array.</para>
    /// <para>For a large number of boxes (say, greater than 1000), this O(n) bin sort is much faster
    /// than the O(n log n) shell sort.</para>
    /// </remarks>
    public Pixa BinSort(int sorttype, int sortorder, out Numa? pnaindex, int copyflag)
    {
        const string func = "pixaBinSort";
        Pixa pixas = this;
        pnaindex = null;
        if (sorttype != L.L_SORT_BY_X && sorttype != L.L_SORT_BY_Y &&
            sorttype != L.L_SORT_BY_WIDTH && sorttype != L.L_SORT_BY_HEIGHT &&
            sorttype != L.L_SORT_BY_PERIMETER)
            throw Lept.Arg(func, "invalid sort type");
        if (sortorder != L.L_SORT_INCREASING && sortorder != L.L_SORT_DECREASING)
            throw Lept.Arg(func, "invalid sort order");
        if (copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg(func, "invalid copy flag");

            /* Verify that the pixa and its boxa have the same count */
        Boxa boxa = pixas.Boxa;   /* not owned */
        int n = pixas.Count;
        if (boxa.Count != n)
            throw Lept.Arg(func, "boxa and pixa counts differ");

            /* Generate Numa of appropriate box dimensions */
        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            boxa.GetBoxGeometry(i, out int x, out int y, out int w, out int h);
            switch (sorttype) {
            case L.L_SORT_BY_X:
                na.AddNumber(x);
                break;
            case L.L_SORT_BY_Y:
                na.AddNumber(y);
                break;
            case L.L_SORT_BY_WIDTH:
                na.AddNumber(w);
                break;
            case L.L_SORT_BY_HEIGHT:
                na.AddNumber(h);
                break;
            case L.L_SORT_BY_PERIMETER:
                na.AddNumber(w + h);
                break;
            default:
                Log.Warning(func, "invalid sort type");
                break;
            }
        }

            /* Get the sort index for data array */
        Numa naindex = na.GetBinSortIndex(sortorder);

            /* Build up sorted pixa using sort index */
        Pixa pixad = pixas.SortByIndex(naindex, copyflag);
        pnaindex = naindex;
        return pixad;
    }

    // leptonica: pixaSortByIndex (pixafunc1.c)
    /// <param name="naindex">na that maps from the new pixa to the input pixa</param>
    /// <param name="copyflag">L_COPY, L_CLONE</param>
    public Pixa SortByIndex(Numa naindex, int copyflag)
    {
        Pixa pixas = this;
        if (copyflag != L.L_CLONE && copyflag != L.L_COPY)
            throw Lept.Arg("pixaSortByIndex", "invalid copyflag");

        int n = pixas.Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            int index = naindex.GetIValue(i);
            Pix pix = pixas.GetPix(index, copyflag);
            Box? box = pixas.GetBox(index, copyflag);
            pixad.AddPix(pix, L.L_INSERT);
            if (box != null)
                pixad.AddBox(box, L.L_INSERT);
            else
                Log.Error("pixaAddBox", "box not defined");
        }
        return pixad;
    }

    // leptonica: pixaSort2dByIndex (pixafunc1.c)
    /// <param name="naa">numaa that maps from the new pixaa to the input pixa</param>
    /// <param name="copyflag">L_CLONE or L_COPY</param>
    /// <remarks>(1) The sum of the numbers in naa must equal the number of pix in this pixa.</remarks>
    public Pixaa Sort2dByIndex(Numaa naa, int copyflag)
    {
        Pixa pixas = this;
        int ntot = naa.GetNumberCount();
        int pixtot = pixas.Count;
        if (ntot != pixtot)
            throw Lept.Arg("pixaSort2dByIndex", "element count mismatch");

        int n = naa.Count;
        Pixaa paa = Pixaa.Create(n);
        for (int i = 0; i < n; i++) {
            Numa na = naa.GetNuma(i, L.L_CLONE);
            int nn = na.Count;
            Pixa pixa = Create(nn);
            for (int j = 0; j < nn; j++) {
                int index = na.GetIValue(j);
                Pix pix = pixas.GetPix(index, copyflag);
                Box? box = pixas.GetBox(index, copyflag);
                pixa.AddPix(pix, L.L_INSERT);
                if (box != null)
                    pixa.AddBox(box, L.L_INSERT);
                else
                    Log.Error("pixaAddBox", "box not defined");
            }
            paa.AddPixa(pixa, L.L_INSERT);
        }
        return paa;
    }

    // leptonica: pixaSelectRange (pixafunc1.c)
    /// <param name="first">use 0 to select from the beginning</param>
    /// <param name="last">use -1 to select to the end</param>
    /// <param name="copyflag">L_COPY, L_CLONE</param>
    /// <remarks>
    /// (1) The copyflag specifies what we do with each pix from pixas.  Specifically,
    ///     L_CLONE inserts a clone into pixad of the selected pix from pixas.
    /// (2) The boxes are not carried over.
    /// </remarks>
    public Pixa SelectRange(int first, int last, int copyflag)
    {
        const string func = "pixaSelectRange";
        Pixa pixas = this;
        if (copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg(func, "invalid copyflag");
        int n = pixas.Count;
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

        int npix = last - first + 1;
        Pixa pixad = Create(npix);
        for (int i = first; i <= last; i++) {
            Pix pix = pixas.GetPix(i, copyflag);
            pixad.AddPix(pix, L.L_INSERT);
        }
        return pixad;
    }

    /*---------------------------------------------------------------------*
     *                  Scaling, rotation, translation, borders            *
     *---------------------------------------------------------------------*/
    // leptonica: pixaScaleToSize (pixafunc1.c)
    /// <param name="wd">target width; use 0 if using height as target</param>
    /// <param name="hd">target height; use 0 if using width as target</param>
    /// <remarks>
    /// (1) See pixScaleToSize() for usage and function.
    /// (2) If neither wd nor hd is positive, returns a clone.
    /// </remarks>
    public Pixa ScaleToSize(int wd, int hd)
    {
        Pixa pixas = this;
        if (wd <= 0 && hd <= 0)  /* no scaling requested */
            return pixas.Copy(L.L_CLONE);

        int n = pixas.Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.ScaleToSize(wd, hd);
            pix2.CopyText(pix1);
            pixad.AddPix(pix2, L.L_INSERT);
        }
        return pixad;
    }

    // leptonica: pixaScaleToSizeRel (pixafunc1.c)
    /// <param name="delw">change in width, in pixels; 0 means no change</param>
    /// <param name="delh">change in height, in pixels; 0 means no change</param>
    /// <remarks>(1) If the relative scaling of a pix fails, a copy of it is used.</remarks>
    public Pixa ScaleToSizeRel(int delw, int delh)
    {
        Pixa pixas = this;
        int n = pixas.Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            Pix? pix2 = null;
            try {
                pix2 = pix1.ScaleToSizeRel(delw, delh);
            } catch (ArgumentException) {
                /* C: pixScaleToSizeRel() returns NULL */
            }
            if (pix2 != null) {
                pixad.AddPix(pix2, L.L_INSERT);
            } else {
                Log.Warning("pixaScaleToSizeRel", "relative scale to size failed; use a copy");
                pixad.AddPix(pix1, L.L_COPY);
            }
        }
        return pixad;
    }

    // leptonica: pixaScale (pixafunc1.c)
    /// <remarks>
    /// (1) Scales each pix and, if the boxa is fully populated, transforms the boxes.
    /// </remarks>
    public Pixa Scale(float scalex, float scaley)
    {
        Pixa pixas = this;
        if (scalex <= 0.0 || scaley <= 0.0)
            throw Lept.Arg("pixaScale", "invalid scaling parameters");

        int n = pixas.Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.Scale(scalex, scaley);
            pix2.CopyText(pix1);
            pixad.AddPix(pix2, L.L_INSERT);
        }
        Boxa boxa1 = pixas.GetBoxa(L.L_CLONE);
        int nb = boxa1.Count;
        if (nb == n) {
            Boxa boxa2 = boxa1.Transform(0, 0, scalex, scaley);
            pixad.SetBoxa(boxa2, L.L_INSERT);
        }
        return pixad;
    }

    // leptonica: pixaScaleBySampling (pixafunc1.c)
    /// <summary>Scales every image in the array by sampling, and scales the boxes to match.</summary>
    /// <param name="scalex">Horizontal scale factor.</param>
    /// <param name="scaley">Vertical scale factor.</param>
    /// <returns>A new array with the scaled images and boxes.</returns>
    public Pixa ScaleBySampling(float scalex, float scaley)
    {
        Pixa pixas = this;
        if (scalex <= 0.0 || scaley <= 0.0)
            throw Lept.Arg("pixaScaleBySampling", "invalid scaling parameters");

        int n = pixas.Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.ScaleBySampling(scalex, scaley);
            pix2.CopyText(pix1);
            pixad.AddPix(pix2, L.L_INSERT);
        }
        Boxa boxa1 = pixas.GetBoxa(L.L_CLONE);
        int nb = boxa1.Count;
        if (nb == n) {
            Boxa boxa2 = boxa1.Transform(0, 0, scalex, scaley);
            pixad.SetBoxa(boxa2, L.L_INSERT);
        }
        return pixad;
    }

    // leptonica: pixaRotate (pixafunc1.c)
    /// <param name="angle">radians; clockwise is positive</param>
    /// <param name="type">L_ROTATE_SHEAR, L_ROTATE_AREA_MAP, L_ROTATE_SAMPLING</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <param name="width">original width; use 0 to avoid embedding</param>
    /// <param name="height">original height; use 0 to avoid embedding</param>
    /// <remarks>
    /// (1) Rotates each pix.  The boxes are not carried over.
    /// (2) If the angle is smaller than MinAngleToRotate, returns a copy.
    /// </remarks>
    public Pixa Rotate(float angle, int type, int incolor, int width, int height)
    {
        const string func = "pixaRotate";
        Pixa pixas = this;
        if (type != L.L_ROTATE_SHEAR && type != L.L_ROTATE_AREA_MAP &&
            type != L.L_ROTATE_SAMPLING)
            throw Lept.Arg(func, "invalid type");
        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor");
        if (Math.Abs(angle) < MinAngleToRotate)
            return pixas.Copy(L.L_COPY);

        int n = pixas.Count;
        Pixa pixad = Create(n);
        Boxa boxa = pixad.GetBoxa(L.L_COPY);
        pixad.SetBoxa(boxa, L.L_INSERT);
        for (int i = 0; i < n; i++) {
            Pix pixs = pixas.GetPix(i, L.L_CLONE);
            Pix pixd = pixs.Rotate(angle, type, incolor, width, height);
            pixad.AddPix(pixd, L.L_INSERT);
        }
        return pixad;
    }

    // leptonica: pixaRotateOrth (pixafunc1.c)
    /// <param name="rotation">0 = noop, 1 = 90 deg, 2 = 180 deg, 3 = 270 deg; all clockwise</param>
    /// <remarks>(1) Rotates each pix and, if the boxa is fully populated, each box.</remarks>
    public Pixa RotateOrth(int rotation)
    {
        Pixa pixas = this;
        if (rotation < 0 || rotation > 3)
            throw Lept.Arg("pixaRotateOrth", "rotation not in {0,1,2,3}");
        if (rotation == 0)
            return pixas.Copy(L.L_COPY);

        int n = pixas.Count;
        int nb = pixas.BoxaCount;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pixs = pixas.GetPix(i, L.L_CLONE);
            Pix pixd = pixs.RotateOrth(rotation);
            pixad.AddPix(pixd, L.L_INSERT);
            if (n == nb) {
                Box boxs = pixas.GetBox(i, L.L_COPY)
                    ?? throw Lept.Arg("pixaRotateOrth", "box not defined");
                pixs.GetDimensions(out int w, out int h, out _);
                Box boxd = boxs.RotateOrth(w, h, rotation);
                pixad.AddBox(boxd, L.L_INSERT);
            }
        }
        return pixad;
    }

    // leptonica: pixaTranslate (pixafunc1.c)
    /// <param name="hshift">horizontal shift; hshift > 0 is to the right</param>
    /// <param name="vshift">vertical shift; vshift > 0 is down</param>
    /// <param name="incolor">L_BRING_IN_WHITE or L_BRING_IN_BLACK</param>
    public Pixa Translate(int hshift, int vshift, int incolor)
    {
        Pixa pixas = this;
        if (hshift == 0 && vshift == 0)
            return pixas.Copy(L.L_COPY);

        int n = pixas.Count;
        int nb = pixas.BoxaCount;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pixs = pixas.GetPix(i, L.L_CLONE);
            Pix pixd = Pix.Translate(null, pixs, hshift, vshift, incolor);
            pixad.AddPix(pixd, L.L_INSERT);
        }
        if (n == nb) {
            Boxa boxas = pixas.GetBoxa(L.L_CLONE);
            Boxa boxad = boxas.Transform(hshift, vshift, 1.0f, 1.0f);
            pixad.SetBoxa(boxad, L.L_INSERT);
        }
        return pixad;
    }

    // leptonica: pixaAddBorderGeneral (pixafunc1.c)
    /// <param name="pixad">[optional] can be null or equal to pixas</param>
    /// <param name="pixas"></param>
    /// <param name="left">number of pixels added on each side</param>
    /// <param name="right"></param>
    /// <param name="top"></param>
    /// <param name="bot"></param>
    /// <param name="val">color of added pixels</param>
    /// <returns>pixad, or a new pixa if pixad is null</returns>
    /// <remarks>
    /// (1) In-place, with pixad == pixas, or a new pixa with pixad == null.
    /// (2) The boxes are adjusted to include the border.
    /// </remarks>
    public static Pixa AddBorderGeneral(Pixa? pixad, Pixa pixas, int left, int right,
                                        int top, int bot, uint val)
    {
        if (left < 0 || right < 0 || top < 0 || bot < 0)
            throw Lept.Arg("pixaAddBorderGeneral", "negative border added!");
        if (pixad != null && (pixad != pixas))
            throw Lept.Arg("pixaAddBorderGeneral", "pixad defined but != pixas");

        int n = pixas.Count;
        if (pixad == null)
            pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pixs = pixas.GetPix(i, L.L_CLONE);
            Pix pixd = pixs.AddBorderGeneral(left, right, top, bot, val);
            if (pixad == pixas)  /* replace */
                pixad.ReplacePix(i, pixd, null);
            else
                pixad.AddPix(pixd, L.L_INSERT);
        }

        int nbox = pixas.BoxaCount;
        Boxa boxad = pixad.GetBoxa(L.L_CLONE);
        for (int i = 0; i < nbox; i++) {
            Box? box = pixas.GetBox(i, L.L_COPY);
            if (box == null) {
                Log.Warning("pixaAddBorderGeneral", $"box {i} not found");
                break;
            }
            Box.AdjustSides(box, box, -left, right, -top, bot);
            if (pixad == pixas)  /* replace */
                boxad.ReplaceBox(i, box);
            else
                boxad.AddBox(box, L.L_INSERT);
        }
        return pixad;
    }

    // leptonica: pixaClipToPix (pixafunc1.c)
    /// <param name="pixs"></param>
    /// <returns>pixad</returns>
    /// <remarks>
    /// (1) This is intended for use in situations where pixas was originally generated from
    ///     the input pixs.
    /// (2) Returns a pixad where each pix in pixas is ANDed with the portion of pixs
    ///     that is located in the region given by its box.
    /// </remarks>
    public Pixa ClipToPix(Pix pixs)
    {
        Pixa pixas = this;
        int n = pixas.Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix = pixas.GetPix(i, L.L_CLONE);
            Box box = pixas.GetBox(i, L.L_COPY)
                ?? throw Lept.Arg("pixaClipToPix", "box not defined");
            Pix? pixc = pixs.ClipRectangle(box, out _);
            if (pixc != null) {
                Pix.And(pixc, pixc, pix);
                pixad.AddPix(pixc, L.L_INSERT);
            } else {  /* C: pixAnd() and pixaAddPix() fail for a null pix */
                Log.Error("pixaAddPix", "pix not defined");
            }
            pixad.AddBox(box, L.L_INSERT);
        }
        return pixad;
    }

    // leptonica: pixaClipToForeground (pixafunc1.c)
    /// <param name="ppixad">pixa of the clipped pix</param>
    /// <param name="pboxa">boxa of the clipping boxes</param>
    /// <remarks>
    /// (1) The input pix must all be 1 bpp.
    /// (2) A pix with no foreground is not added to the outputs (as in C, where the add fails).
    /// </remarks>
    public void ClipToForeground(out Pixa ppixad, out Boxa pboxa)
    {
        Pixa pixas = this;
        int n = pixas.Count;
        ppixad = Create(n);
        pboxa = Boxa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            pix1.ClipToForeground(out Pix? pix2, out Box? box1);
            if (pix2 != null)
                ppixad.AddPix(pix2, L.L_INSERT);
            else
                Log.Error("pixaAddPix", "pix not defined");
            if (box1 != null)
                pboxa.AddBox(box1, L.L_INSERT);
            else
                Log.Error("boxaAddBox", "box not defined");
        }
    }

    /*---------------------------------------------------------------------*
     *                  Size and depth information, conversion             *
     *---------------------------------------------------------------------*/
    // leptonica: pixaSizeRange (pixafunc1.c)
    /// <summary>Gets the minimum and maximum width and height over all images in the array.</summary>
    /// <param name="minw">Receives the minimum width.</param>
    /// <param name="minh">Receives the minimum height.</param>
    /// <param name="maxw">Receives the maximum width.</param>
    /// <param name="maxh">Receives the maximum height.</param>
    public void SizeRange(out int minw, out int minh, out int maxw, out int maxh)
    {
        minw = minh = 1000000;
        maxw = maxh = 0;
        int n = Count;
        for (int i = 0; i < n; i++) {
            Pix pix = GetPix(i, L.L_CLONE);
            int w = pix.Width;
            int h = pix.Height;
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

    // leptonica: pixaGetRenderingDepth (pixafunc1.c)
    /// <returns>depth required to render all pix: 1, 8 or 32</returns>
    public int GetRenderingDepth()
    {
        if (HasColor())
            return 32;

        GetDepthInfo(out int maxdepth, out _);
        if (maxdepth == 1)
            return 1;
        else  /* 2, 4, 8 or 16 */
            return 8;
    }

    // leptonica: pixaHasColor (pixafunc1.c)
    /// <returns>true if any pix is 32 bpp or has a colormap with color</returns>
    public bool HasColor()
    {
        int n = Count;
        bool hascolor = false;
        for (int i = 0; i < n; i++) {
            Pix pix = GetPix(i, L.L_CLONE);
            PixColormap? cmap = pix.Colormap;
            if (cmap != null)
                hascolor = cmap.HasColor();
            int d = pix.Depth;
            if (d == 32 || hascolor)
                return true;
        }
        return false;
    }

    // leptonica: pixaAnyColormaps (pixafunc1.c)
    /// <returns>true if any pix has a colormap</returns>
    public bool AnyColormaps()
    {
        int n = Count;
        for (int i = 0; i < n; i++) {
            Pix pix = GetPix(i, L.L_CLONE);
            if (pix.Colormap != null)
                return true;
        }
        return false;
    }

    // leptonica: pixaGetDepthInfo (pixafunc1.c)
    /// <param name="maxdepth">max depth of all pix</param>
    /// <param name="same">true if all pix have the same depth</param>
    public void GetDepthInfo(out int maxdepth, out bool same)
    {
        maxdepth = 0;
        same = true;
        int n = Count;
        if (n == 0)
            throw Lept.Arg("pixaGetDepthInfo", "pixa is empty");

        bool same1 = true;
        int maxd = 0;
        int d0 = 0;
        for (int i = 0; i < n; i++) {
            GetPixDimensions(i, out _, out _, out int d);
            if (i == 0)
                d0 = d;
            else if (d != d0)
                same1 = false;
            if (d > maxd) maxd = d;
        }

        maxdepth = maxd;
        same = same1;
    }

    // leptonica: pixaConvertToSameDepth (pixafunc1.c)
    /// <returns>pixad with all pix at the same depth: 8 or 32 bpp, or 1 bpp if all are 1 bpp</returns>
    /// <remarks>
    /// (1) If any pix has a colormap, it is removed.
    /// (2) The boxa is copied.
    /// </remarks>
    public Pixa ConvertToSameDepth()
    {
        Pixa pixas = this;
        int n = pixas.Count;
        if (n == 0)
            throw Lept.Arg("pixaConvertToSameDepth", "no components");

            /* Remove colormaps if necessary */
        int depth = pixas.GetRenderingDepth();
        bool hascmap = pixas.AnyColormaps();
        Pixa pixa1;
        if (hascmap) {
            pixa1 = Create(n);
            for (int i = 0; i < n; i++) {
                Pix pix1 = pixas.GetPix(i, L.L_CLONE);
                Pix pix2;
                if (depth == 32)
                    pix2 = pix1.ConvertTo32();
                else  /* depth = 8 */
                    pix2 = pix1.ConvertTo8(false);
                pixa1.AddPix(pix2, L.L_INSERT);
            }
        } else {
            pixa1 = pixas.Copy(L.L_CLONE);
        }

        pixa1.GetDepthInfo(out int maxdepth, out bool same);
        Pixa pixad;
        if (!same) {  /* at least one pix has depth < maxdepth */
            pixad = Create(n);
            for (int i = 0; i < n; i++) {
                Pix pix1 = pixa1.GetPix(i, L.L_CLONE);
                Pix pix2;
                if (maxdepth <= 16)
                    pix2 = pix1.ConvertTo8(false);
                else
                    pix2 = pix1.ConvertTo32();
                pixad.AddPix(pix2, L.L_INSERT);
            }
        } else {
            pixad = pixa1.Copy(L.L_CLONE);
        }

        Boxa boxa = pixas.GetBoxa(L.L_COPY);
        pixad.SetBoxa(boxa, L.L_INSERT);
        return pixad;
    }

    // leptonica: pixaConvertToGivenDepth (pixafunc1.c)
    /// <param name="depth">8 or 32 bpp</param>
    /// <remarks>
    /// (1) This is useful when all the pix are to be rendered at the same depth.
    ///     Lossy conversions and 1 bpp input give a warning.
    /// (2) The boxa is copied.
    /// </remarks>
    public Pixa ConvertToGivenDepth(int depth)
    {
        const string func = "pixaConvertToGivenDepth";
        Pixa pixas = this;
        int n = pixas.Count;
        if (n == 0)
            throw Lept.Arg(func, "no components");
        if (depth != 8 && depth != 32)
            throw Lept.Arg(func, "depth not 8 or 32");

            /* Warn with 1 --> {8,32} or lossy conversions */
        int maxd = pixas.GetRenderingDepth();
        if (maxd == 1)
            Log.Warning(func, $"All pix are 1 bpp; converting to {depth} bpp");
        if (maxd > depth)
            Log.Warning(func, $"Lossy conversion: max rendering depth {maxd} > input {depth}");

        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            Pix pix2;
            if (depth == 32) {
                pix2 = (pix1.Depth == 32) ? Pix.Clone(pix1) : pix1.ConvertTo32();
            } else {  /* depth = 8 */
                pix2 = pix1.ConvertTo8(false);
            }
            pixad.AddPix(pix2, L.L_INSERT);
        }

        Boxa boxa = pixas.GetBoxa(L.L_COPY);
        pixad.SetBoxa(boxa, L.L_INSERT);
        return pixad;
    }

    // leptonica: pixaEqual (pixafunc1.c)
    /// <param name="pixa2"></param>
    /// <param name="maxdist">maximum allowed distance between corresponding boxes</param>
    /// <param name="pnaindex">mapping array from the pix of pixa2 to those of this pixa;
    ///                        null if the boxas are empty</param>
    /// <returns>true if the two pixa are equal</returns>
    /// <remarks>
    /// (1) The two pixa are the same if they have the same number of pix, and each pix is
    ///     identical to its counterpart; if both have boxa, the boxes must be within %maxdist
    ///     and the pix are matched through the box mapping.
    /// (2) If either boxa is empty and the other is not, the pixa are not equal.
    /// </remarks>
    public bool Equal(Pixa pixa2, int maxdist, out Numa? pnaindex)
    {
        Pixa pixa1 = this;
        pnaindex = null;
        Numa? na = null;
        int n = pixa1.Count;
        if (n != pixa2.Count)
            return false;

            /* If there are no boxes, strict ordering of the pix in each pixa is required. */
        Boxa boxa1 = pixa1.GetBoxa(L.L_CLONE);
        Boxa boxa2 = pixa2.GetBoxa(L.L_CLONE);
        bool empty1 = boxa1.Count == 0;
        bool empty2 = boxa2.Count == 0;
        if (!empty1 && !empty2) {
            bool sameboxa = boxa1.Equal(boxa2, maxdist, out na);
            if (!sameboxa)
                return false;
        }
        if ((!empty1 && empty2) || (empty1 && !empty2))
            return false;

        for (int i = 0; i < n; i++) {
            Pix pix1 = pixa1.GetPix(i, L.L_CLONE);
            int j = (na != null) ? na.GetIValue(i) : i;
            Pix pix2 = pixa2.GetPix(j, L.L_CLONE);
            if (!Pix.Equal(pix1, pix2))
                return false;
        }

        pnaindex = na;
        return true;
    }

    // leptonica: pixaSetFullSizeBoxa (pixafunc1.c)
    /// <remarks>
    /// (1) This is used to ensure that each pix has an associated box, covering the full size of
    ///     the pix, replacing any existing boxa.
    /// </remarks>
    public void SetFullSizeBoxa()
    {
        int n = Count;
        if (n == 0) {
            Log.Info("pixaSetFullSizeBoxa", "pixa contains no pix");
            return;
        }

        Boxa boxa = Boxa.Create(n);
        SetBoxa(boxa, L.L_INSERT);
        for (int i = 0; i < n; i++) {
            Pix pix = GetPix(i, L.L_CLONE);
            pix.GetDimensions(out int w, out int h, out _);
            Box box = Box.Create(0, 0, w, h);
            boxa.AddBox(box, L.L_INSERT);
        }
    }
}

public sealed partial class Pixaa
{
    // leptonica: pixaaSelectRange (pixafunc1.c)
    /// <param name="first">use 0 to select from the beginning</param>
    /// <param name="last">use -1 to select to the end</param>
    /// <param name="copyflag">L_COPY, L_CLONE</param>
    public Pixaa SelectRange(int first, int last, int copyflag)
    {
        const string func = "pixaaSelectRange";
        Pixaa paas = this;
        if (copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg(func, "invalid copyflag");
        int n = paas.Count;
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

        int npixa = last - first + 1;
        Pixaa paad = Create(npixa);
        for (int i = first; i <= last; i++) {
            Pixa pixa = paas.GetPixa(i, copyflag);
            paad.AddPixa(pixa, L.L_INSERT);
        }
        return paad;
    }

    // leptonica: pixaaScaleToSize (pixafunc1.c)
    /// <param name="wd">target width; use 0 if using height as target</param>
    /// <param name="hd">target height; use 0 if using width as target</param>
    public Pixaa ScaleToSize(int wd, int hd)
    {
        Pixaa paas = this;
        if (wd <= 0 && hd <= 0)
            throw Lept.Arg("pixaaScaleToSize", "neither wd nor hd > 0");

        int n = paas.Count;
        Pixaa paad = Create(n);
        for (int i = 0; i < n; i++) {
            Pixa pixa1 = paas.GetPixa(i, L.L_CLONE);
            Pixa pixa2 = pixa1.ScaleToSize(wd, hd);
            paad.AddPixa(pixa2, L.L_INSERT);
        }
        return paad;
    }

    // leptonica: pixaaScaleToSizeVar (pixafunc1.c)
    /// <param name="nawd">[optional] widths for each pixa</param>
    /// <param name="nahd">[optional] heights for each pixa</param>
    /// <remarks>(1) Both numa, if given, must have the same count as this pixaa.</remarks>
    public Pixaa ScaleToSizeVar(Numa? nawd, Numa? nahd)
    {
        Pixaa paas = this;
        if (nawd == null && nahd == null)
            throw Lept.Arg("pixaaScaleToSizeVar", "!nawd && !nahd");

        int n = paas.Count;
        if (nawd != null && (n != nawd.Count))
            throw Lept.Arg("pixaaScaleToSizeVar", "nawd wrong size");
        if (nahd != null && (n != nahd.Count))
            throw Lept.Arg("pixaaScaleToSizeVar", "nahd wrong size");

        Pixaa paad = Create(n);
        for (int i = 0; i < n; i++) {
            int wd = 0, hd = 0;
            if (nawd != null) wd = nawd.GetIValue(i);
            if (nahd != null) hd = nahd.GetIValue(i);
            Pixa pixa1 = paas.GetPixa(i, L.L_CLONE);
            Pixa pixa2 = pixa1.ScaleToSize(wd, hd);
            paad.AddPixa(pixa2, L.L_INSERT);
        }
        return paad;
    }

    // leptonica: pixaaFlattenToPixa (pixafunc1.c)
    /// <param name="pnaindex">the index of the pixa in the pixaa for each pix</param>
    /// <param name="copyflag">L_COPY or L_CLONE</param>
    /// <remarks>(1) The boxes are carried over when the pixa have them.</remarks>
    public Pixa FlattenToPixa(out Numa pnaindex, int copyflag)
    {
        Pixaa paa = this;
        if (copyflag != L.L_COPY && copyflag != L.L_CLONE)
            throw Lept.Arg("pixaaFlattenToPixa", "invalid copyflag");

        Numa naindex = Numa.Create(0);
        pnaindex = naindex;
        int n = paa.Count;
        Pixa pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pixa pixat = paa.GetPixa(i, L.L_CLONE);
            int m = pixat.Count;
            int mb = pixat.BoxaCount;
            for (int j = 0; j < m; j++) {
                Pix pix = pixat.GetPix(j, copyflag);
                pixa.AddPix(pix, L.L_INSERT);
                if (j < mb) {
                    Box? box = pixat.GetBox(j, copyflag);
                    if (box != null)
                        pixa.AddBox(box, L.L_INSERT);
                    else
                        Log.Error("pixaAddBox", "box not defined");
                }
                naindex.AddNumber(i);  /* save 'row' number */
            }
        }
        return pixa;
    }

    // leptonica: pixaaSizeRange (pixafunc1.c)
    /// <summary>Gets the minimum and maximum width and height over all images in all arrays.</summary>
    /// <param name="minw">Receives the minimum width.</param>
    /// <param name="minh">Receives the minimum height.</param>
    /// <param name="maxw">Receives the maximum width.</param>
    /// <param name="maxh">Receives the maximum height.</param>
    public void SizeRange(out int minw, out int minh, out int maxw, out int maxh)
    {
        minw = minh = 100000000;
        maxw = maxh = 0;
        int n = Count;
        for (int i = 0; i < n; i++) {
            Pixa pixa = GetPixa(i, L.L_CLONE);
            pixa.SizeRange(out int minpw, out int minph, out int maxpw, out int maxph);
            if (minpw < minw)
                minw = minpw;
            if (minph < minh)
                minh = minph;
            if (maxpw > maxw)
                maxw = maxpw;
            if (maxph > maxh)
                maxh = maxph;
        }
    }
}
