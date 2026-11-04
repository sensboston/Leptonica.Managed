using System;

namespace Leptonica;

/*
 *  paintcmap.c: Painting on colormapped images.
 *
 *      These in-place functions paint onto colormap images.
 *
 *      Repaint selected pixels in region
 *           Pix.SetSelectCmap (pixSetSelectCmap)
 *
 *      Repaint non-white pixels in region
 *           Pix.ColorGrayRegionsCmap (pixColorGrayRegionsCmap)
 *           Pix.ColorGrayCmap (pixColorGrayCmap)
 *           Pix.ColorGrayMaskedCmap (pixColorGrayMaskedCmap)
 *           PixColormap.AddColorizedGrayToCmap (addColorizedGrayToCmap)
 *
 *      Repaint selected pixels through mask
 *           Pix.SetSelectMaskedCmap (pixSetSelectMaskedCmap)
 *
 *      Repaint all pixels through mask
 *           Pix.SetMaskedCmap (pixSetMaskedCmap)
 *
 *  The 'set select' functions condition the setting on a specific
 *  pixel value (i.e., index into the colormap) of the underlying
 *  Pix that is being modified.  The same conditioning is used in
 *  pixBlendCmap().
 *
 *  The pixColorGrayCmap() function sets all truly gray (r = g = b) pixels,
 *  with the exception of either black or white pixels, to a new color.
 *
 *  The pixSetSelectMaskedCmap() function conditions pixel painting
 *  on both a specific pixel value and location within the fg mask.
 *  By contrast, pixSetMaskedCmap() sets all pixels under the
 *  mask foreground, without considering the initial pixel values.
 */

/// <summary>Constants from pix.h used by paintcmap.c.</summary>
public static partial class L
{
    /* Painting gray pixels through a colormap */
    /// <summary>Colorizes the non-black pixels.</summary>
    public const int L_PAINT_LIGHT = 1;

    /// <summary>Colorizes the non-white pixels.</summary>
    public const int L_PAINT_DARK = 2;
}

public sealed partial class Pix
{
    /*-------------------------------------------------------------*
     *               Repaint selected pixels in region             *
     *-------------------------------------------------------------*/
    // leptonica: pixSetSelectCmap (paintcmap.c)
    /// <summary>Sets the pixels of a colormap index in a region to a new color (in place).</summary>
    /// <param name="box">[optional] region to set color; can be null</param>
    /// <param name="sindex">colormap index of pixels to be changed</param>
    /// <param name="rval">new color to paint</param>
    /// <param name="gval">new color to paint</param>
    /// <param name="bval">new color to paint</param>
    /// <remarks>
    /// (1) This is an in-place operation.
    /// (2) It sets all pixels in region that have the color specified
    ///     by the colormap index %sindex to the new color.
    /// (3) %sindex must be in the existing colormap; otherwise an
    ///     error is returned.
    /// (4) If the new color exists in the colormap, it is used;
    ///     otherwise, it is added to the colormap.  If it cannot be
    ///     added because the colormap is full, an error is returned.
    /// (5) If %box is NULL, applies function to the entire image; otherwise,
    ///     clips the operation to the intersection of the box and pix.
    /// (6) An example of use would be to set to a specific color all
    ///     the light (background) pixels within a certain region of
    ///     a 3-level 2 bpp image, while leaving light pixels outside
    ///     this region unchanged.
    /// </remarks>
    public void SetSelectCmap(Box? box, int sindex, int rval, int gval, int bval)
    {
        const string func = "pixSetSelectCmap";
        Pix pixs = this;
        PixColormap cmap = pixs.Colormap ?? throw Lept.Arg(func, "no colormap");
        int d = pixs.Depth;
        if (d != 1 && d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "depth not in {1,2,4,8}");

            /* Add new color if necessary; get index of this color in cmap */
        int n = cmap.Count;
        if (sindex >= n)
            throw Lept.Arg(func, "sindex too large; no cmap entry");
        int index;  /* of new color to be set */
        if (!cmap.GetIndex(rval, gval, bval, out index)) {  /* not found */
            cmap.AddColor(rval, gval, bval);
            index = n;  /* we've added one color */
        }

            /* Determine the region of substitution */
        pixs.GetDimensions(out int w, out int h, out _);
        int x1, y1, x2, y2;
        if (box == null) {
            x1 = y1 = 0;
            x2 = w;
            y2 = h;
        } else {
            box.GetGeometry(out x1, out y1, out int bw, out int bh);
            x2 = x1 + bw - 1;
            y2 = y1 + bh - 1;
        }

            /* Replace pixel value sindex by index in the region */
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        for (int i = y1; i <= y2; i++) {
            if (i < 0 || i >= h)  /* clip */
                continue;
            Span<uint> lines = datas.AsSpan(i * wpls, wpls);
            for (int j = x1; j <= x2; j++) {
                if (j < 0 || j >= w)  /* clip */
                    continue;
                int val;
                switch (d) {
                case 1:
                    val = ArrayAccess.GetDataBit(lines, j);
                    if (val == sindex) {
                        if (index == 0)
                            ArrayAccess.ClearDataBit(lines, j);
                        else
                            ArrayAccess.SetDataBit(lines, j);
                    }
                    break;
                case 2:
                    val = ArrayAccess.GetDataDibit(lines, j);
                    if (val == sindex)
                        ArrayAccess.SetDataDibit(lines, j, index);
                    break;
                case 4:
                    val = ArrayAccess.GetDataQbit(lines, j);
                    if (val == sindex)
                        ArrayAccess.SetDataQbit(lines, j, index);
                    break;
                case 8:
                    val = ArrayAccess.GetDataByte(lines, j);
                    if (val == sindex)
                        ArrayAccess.SetDataByte(lines, j, index);
                    break;
                default:
                    throw Lept.Arg(func, "depth not in {1,2,4,8}");
                }
            }
        }
    }

    /*-------------------------------------------------------------*
     *                  Repaint gray pixels in region              *
     *-------------------------------------------------------------*/
    // leptonica: pixColorGrayRegionsCmap (paintcmap.c)
    /// <summary>Colorizes the gray pixels in regions of an 8 bpp colormapped image (in place).</summary>
    /// <param name="boxa">of regions in which to apply color</param>
    /// <param name="type">L_PAINT_LIGHT, L_PAINT_DARK</param>
    /// <param name="rval">target color</param>
    /// <param name="gval">target color</param>
    /// <param name="bval">target color</param>
    /// <remarks>
    /// (1) This is an in-place operation.
    /// (2) If %type == L_PAINT_LIGHT, it colorizes non-black pixels,
    ///     preserving antialiasing.
    ///     If %type == L_PAINT_DARK, it colorizes non-white pixels,
    ///     preserving antialiasing.  See pixColorGrayCmap() for details.
    /// (3) This can also be called through pixColorGrayRegions().
    /// (4) This increases the colormap size by the number of
    ///     different gray (non-black or non-white) colors in the
    ///     selected regions of pixs.  If there is not enough room in
    ///     the colormap for this expansion, it fails (throws).
    /// (5) Because two boxes in %boxa can overlap, pixels that
    ///     are colorized in the first box must be excluded in the
    ///     second because their value exceeds the size of the map.
    /// </remarks>
    public void ColorGrayRegionsCmap(Boxa boxa, int type, int rval, int gval, int bval)
    {
        const string func = "pixColorGrayRegionsCmap";
        Pix pixs = this;
        PixColormap cmap = pixs.Colormap ?? throw Lept.Arg(func, "no colormap");
        if (pixs.Depth != 8)
            throw Lept.Arg(func, "depth not 8 bpp");
        if (type != L.L_PAINT_DARK && type != L.L_PAINT_LIGHT)
            throw Lept.Arg(func, "invalid type");

        int nc = cmap.Count;
        if (!cmap.AddColorizedGrayToCmap(type, rval, gval, bval, out Numa? na))
            throw Lept.Fail(func, "no room; cmap full");
        int[] map = na!.GetIArray();

        pixs.GetDimensions(out int w, out int h, out _);
        uint[] data = pixs.Data;
        int wpl = pixs.Wpl;
        int n = boxa.Count;
        for (int k = 0; k < n; k++) {
            Box box = boxa.GetBox(k, L.L_CLONE);
            box.GetGeometry(out int x1, out int y1, out int bw, out int bh);
            int x2 = x1 + bw - 1;
            int y2 = y1 + bh - 1;

                /* Remap gray pixels in the region */
            for (int i = y1; i <= y2; i++) {
                if (i < 0 || i >= h)  /* clip */
                    continue;
                Span<uint> line = data.AsSpan(i * wpl, wpl);
                for (int j = x1; j <= x2; j++) {
                    if (j < 0 || j >= w)  /* clip */
                        continue;
                    int val = ArrayAccess.GetDataByte(line, j);
                    if (val >= nc) continue;  /* from overlapping b.b. */
                    int nval = map[val];
                    if (nval != 256)
                        ArrayAccess.SetDataByte(line, j, nval);
                }
            }
        }
    }

    // leptonica: pixColorGrayCmap (paintcmap.c)
    /// <summary>Colorizes the gray pixels of a colormapped image (in place).</summary>
    /// <param name="box">[optional] region to set color; can be null</param>
    /// <param name="type">L_PAINT_LIGHT, L_PAINT_DARK</param>
    /// <param name="rval">target color</param>
    /// <param name="gval">target color</param>
    /// <param name="bval">target color</param>
    /// <remarks>
    /// (1) This is an in-place operation.
    /// (2) If %type == L_PAINT_LIGHT, it colorizes non-black pixels,
    ///     preserving antialiasing.
    ///     If %type == L_PAINT_DARK, it colorizes non-white pixels,
    ///     preserving antialiasing.
    /// (3) %box gives the region to apply color; if NULL, this
    ///     colorizes the entire image.
    /// (4) If the cmap is only 2 or 4 bpp, pixs is converted in-place
    ///     to an 8 bpp cmap.  A 1 bpp cmap is not a valid input pix.
    /// (5) This can also be called through pixColorGray().
    /// (6) This operation increases the colormap size by the number of
    ///     different gray (non-black or non-white) colors in the
    ///     input colormap.  If there is not enough room in the colormap
    ///     for this expansion, it fails (throws).
    /// (7) Using the darkness of each original pixel in the rect,
    ///     it generates a new color (based on the input rgb values).
    ///     If %type == L_PAINT_LIGHT, the new color is a (generally)
    ///     darken-to-black version of the input rgb color, where the
    ///     amount of darkening increases with the darkness of the
    ///     original pixel color.
    ///     If %type == L_PAINT_DARK, the new color is a (generally)
    ///     faded-to-white version of the input rgb color, where the
    ///     amount of fading increases with the brightness of the
    ///     original pixel color.
    /// </remarks>
    public void ColorGrayCmap(Box? box, int type, int rval, int gval, int bval)
    {
        const string func = "pixColorGrayCmap";
        Pix pixs = this;
        if (pixs.Colormap == null)
            throw Lept.Arg(func, "no colormap");
        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "depth not in {2, 4, 8}");
        if (type != L.L_PAINT_DARK && type != L.L_PAINT_LIGHT)
            throw Lept.Arg(func, "invalid type");

            /* If 2 bpp or 4 bpp, convert in-place to 8 bpp. */
        if (d == 2 || d == 4) {
            Pix? pixt = pixs.ConvertTo8(true);
            pixs.TransferAllData(ref pixt, false, false);
        }

            /* If box == NULL, color the entire image */
        Boxa boxa = Boxa.Create(1);
        if (box != null) {
            boxa.AddBox(box, L.L_COPY);
        } else {
            box = Box.Create(0, 0, w, h);
            boxa.AddBox(box, L.L_INSERT);
        }
        pixs.ColorGrayRegionsCmap(boxa, type, rval, gval, bval);
    }

    // leptonica: pixColorGrayMaskedCmap (paintcmap.c)
    /// <summary>Colorizes the gray pixels under the foreground of a mask (in place).</summary>
    /// <param name="pixm">1 bpp mask, through which to apply color</param>
    /// <param name="type">L_PAINT_LIGHT, L_PAINT_DARK</param>
    /// <param name="rval">target color</param>
    /// <param name="gval">target color</param>
    /// <param name="bval">target color</param>
    /// <remarks>
    /// (1) This is an in-place operation.
    /// (2) If %type == L_PAINT_LIGHT, it colorizes non-black pixels,
    ///     preserving antialiasing.
    ///     If %type == L_PAINT_DARK, it colorizes non-white pixels,
    ///     preserving antialiasing.  See pixColorGrayCmap() for details.
    /// (3) This increases the colormap size by the number of
    ///     different gray (non-black or non-white) colors in the
    ///     input colormap.  If there is not enough room in the colormap
    ///     for this expansion, it fails (throws).
    /// </remarks>
    public void ColorGrayMaskedCmap(Pix pixm, int type, int rval, int gval, int bval)
    {
        const string func = "pixColorGrayMaskedCmap";
        Pix pixs = this;
        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm undefined or not 1 bpp");
        PixColormap cmap = pixs.Colormap ?? throw Lept.Arg(func, "no colormap");
        if (pixs.Depth != 8)
            throw Lept.Arg(func, "depth not 8 bpp");
        if (type != L.L_PAINT_DARK && type != L.L_PAINT_LIGHT)
            throw Lept.Arg(func, "invalid type");

        if (!cmap.AddColorizedGrayToCmap(type, rval, gval, bval, out Numa? na))
            throw Lept.Fail(func, "no room; cmap full");
        int[] map = na!.GetIArray();

        pixs.GetDimensions(out int w, out int h, out _);
        pixm.GetDimensions(out int wm, out int hm, out _);
        if (wm != w)
            Log.Warning(func, CFormat.Sprintf("wm = %d differs from w = %d", wm, w));
        if (hm != h)
            Log.Warning(func, CFormat.Sprintf("hm = %d differs from h = %d", hm, h));
        int wmin = Math.Min(w, wm);
        int hmin = Math.Min(h, hm);

        uint[] data = pixs.Data;
        int wpl = pixs.Wpl;
        uint[] datam = pixm.Data;
        int wplm = pixm.Wpl;

            /* Remap gray pixels in the region */
        for (int i = 0; i < hmin; i++) {
            Span<uint> line = data.AsSpan(i * wpl, wpl);
            ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
            for (int j = 0; j < wmin; j++) {
                if (ArrayAccess.GetDataBit(linem, j) == 0)
                    continue;
                int val = ArrayAccess.GetDataByte(line, j);
                int nval = map[val];
                if (nval != 256)
                    ArrayAccess.SetDataByte(line, j, nval);
            }
        }
    }

    /*-------------------------------------------------------------*
     *             Repaint selected pixels through mask            *
     *-------------------------------------------------------------*/
    // leptonica: pixSetSelectMaskedCmap (paintcmap.c)
    /// <summary>Sets the pixels of a colormap index under a mask to a new color (in place).</summary>
    /// <param name="pixm">[optional] 1 bpp mask; no-op if null</param>
    /// <param name="x">UL corner of mask relative to pixs</param>
    /// <param name="y">UL corner of mask relative to pixs</param>
    /// <param name="sindex">cmap index of pixels in pixs to be changed</param>
    /// <param name="rval">new color to substitute</param>
    /// <param name="gval">new color to substitute</param>
    /// <param name="bval">new color to substitute</param>
    /// <remarks>
    /// (1) This is an in-place operation.
    /// (2) This paints through the fg of pixm and replaces all pixels
    ///     in pixs that have the value %sindex with the new color.
    /// (3) If pixm == NULL, a warning is given.
    /// (4) %sindex must be in the existing colormap; otherwise an
    ///     error is returned.
    /// (5) If the new color exists in the colormap, it is used;
    ///     otherwise, it is added to the colormap.  If the colormap
    ///     is full, an error is returned.
    /// </remarks>
    public void SetSelectMaskedCmap(Pix? pixm, int x, int y, int sindex,
                                    int rval, int gval, int bval)
    {
        const string func = "pixSetSelectMaskedCmap";
        Pix pixs = this;
        PixColormap cmap = pixs.Colormap ?? throw Lept.Arg(func, "no colormap");
        if (pixm == null) {
            Log.Warning(func, "no mask; nothing to do");
            return;
        }

        int d = pixs.Depth;
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "depth not in {2, 4, 8}");

            /* add new color if necessary; get index of this color in cmap */
        int n = cmap.Count;
        if (sindex >= n)
            throw Lept.Arg(func, "sindex too large; no cmap entry");
        int index;  /* of new color to be set */
        if (!cmap.GetIndex(rval, gval, bval, out index)) {  /* not found */
            cmap.AddColor(rval, gval, bval);
            index = n;  /* we've added one color */
        }

            /* replace pixel value sindex by index when fg pixel in pixmc
             * overlays it */
        pixs.GetDimensions(out int w, out int h, out _);
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        int wm = pixm.Width;
        int hm = pixm.Height;
        uint[] datam = pixm.Data;
        int wplm = pixm.Wpl;
        for (int i = 0; i < hm; i++) {
            if (i + y < 0 || i + y >= h) continue;
            Span<uint> lines = datas.AsSpan((y + i) * wpls, wpls);
            ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
            for (int j = 0; j < wm; j++) {
                if (j + x < 0 || j + x >= w) continue;
                if (ArrayAccess.GetDataBit(linem, j) != 0) {
                    int val;
                    switch (d) {
                    case 2:
                        val = ArrayAccess.GetDataDibit(lines, x + j);
                        if (val == sindex)
                            ArrayAccess.SetDataDibit(lines, x + j, index);
                        break;
                    case 4:
                        val = ArrayAccess.GetDataQbit(lines, x + j);
                        if (val == sindex)
                            ArrayAccess.SetDataQbit(lines, x + j, index);
                        break;
                    case 8:
                        val = ArrayAccess.GetDataByte(lines, x + j);
                        if (val == sindex)
                            ArrayAccess.SetDataByte(lines, x + j, index);
                        break;
                    default:
                        throw Lept.Arg(func, "depth not in {1,2,4,8}");
                    }
                }
            }
        }
    }

    /*-------------------------------------------------------------*
     *               Repaint all pixels through mask               *
     *-------------------------------------------------------------*/
    // leptonica: pixSetMaskedCmap (paintcmap.c)
    /// <summary>Sets all pixels under a mask to a color (in place).</summary>
    /// <param name="pixm">[optional] 1 bpp mask; no-op if null</param>
    /// <param name="x">origin of pixm relative to pixs; can be negative</param>
    /// <param name="y">origin of pixm relative to pixs; can be negative</param>
    /// <param name="rval">new color to set at each masked pixel</param>
    /// <param name="gval">new color to set at each masked pixel</param>
    /// <param name="bval">new color to set at each masked pixel</param>
    /// <remarks>
    /// (1) This is an in-place operation.
    /// (2) It paints a single color through the mask (as a stencil).
    /// (3) The mask origin is placed at (%x,%y) on %pixs, and the
    ///     operation is clipped to the intersection of the mask and pixs.
    /// (4) If %pixm == NULL, a warning is given.
    /// (5) Typically, %pixm is a small binary mask located somewhere
    ///     on the larger %pixs.
    /// (6) If the color is in the colormap, it is used.  Otherwise,
    ///     it is added if possible; an error is returned if the
    ///     colormap is already full.
    /// </remarks>
    public void SetMaskedCmap(Pix? pixm, int x, int y, int rval, int gval, int bval)
    {
        const string func = "pixSetMaskedCmap";
        Pix pixs = this;
        PixColormap cmap = pixs.Colormap ?? throw Lept.Arg(func, "no colormap in pixs");
        if (pixm == null) {
            Log.Warning(func, "no mask; nothing to do");
            return;
        }
        int d = pixs.Depth;
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "depth not in {2,4,8}");
        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm not 1 bpp");

            /* Add new color if necessary; store in 'index' */
        int index;
        if (!cmap.GetIndex(rval, gval, bval, out index)) {  /* not found */
            cmap.AddColor(rval, gval, bval);
            index = cmap.Count - 1;
        }

        pixs.GetDimensions(out int w, out int h, out _);
        int wpl = pixs.Wpl;
        uint[] data = pixs.Data;
        pixm.GetDimensions(out int wm, out int hm, out _);
        int wplm = pixm.Wpl;
        uint[] datam = pixm.Data;
        for (int i = 0; i < hm; i++) {
            if (i + y < 0 || i + y >= h) continue;
            Span<uint> line = data.AsSpan((i + y) * wpl, wpl);
            ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
            for (int j = 0; j < wm; j++) {
                if (j + x < 0 || j + x >= w) continue;
                if (ArrayAccess.GetDataBit(linem, j) != 0) {  /* paint color */
                    switch (d) {
                    case 2:
                        ArrayAccess.SetDataDibit(line, j + x, index);
                        break;
                    case 4:
                        ArrayAccess.SetDataQbit(line, j + x, index);
                        break;
                    case 8:
                        ArrayAccess.SetDataByte(line, j + x, index);
                        break;
                    default:
                        throw Lept.Arg(func, "depth not in {2,4,8}");
                    }
                }
            }
        }
    }
}

public sealed partial class PixColormap
{
    // leptonica: addColorizedGrayToCmap (paintcmap.c)
    /// <summary>Adds colorized versions of the gray entries of the colormap.</summary>
    /// <param name="type">L_PAINT_LIGHT, L_PAINT_DARK</param>
    /// <param name="rval">target color</param>
    /// <param name="gval">target color</param>
    /// <param name="bval">target color</param>
    /// <param name="na">table for mapping new cmap entries</param>
    /// <returns>true if OK; false if the new colors will not fit in the cmap
    ///     (the C function returns 2)</returns>
    /// <remarks>
    /// (1) If %type == L_PAINT_LIGHT, it colorizes non-black pixels,
    ///     preserving antialiasing.
    ///     If %type == L_PAINT_DARK, it colorizes non-white pixels,
    ///     preserving antialiasing.
    /// (2) This increases the colormap size by the number of
    ///     different gray (non-black or non-white) colors in the
    ///     input colormap.  If there is not enough room in the colormap
    ///     for this expansion, it returns false (treated as a warning);
    ///     the caller should check the return value.
    /// (3) The mapping table contains, for each gray color found, the
    ///     index of the corresponding colorized pixel.  Non-gray
    ///     pixels are assigned the invalid index 256.
    /// (4) See pixColorGrayCmap() for usage.
    /// </remarks>
    public bool AddColorizedGrayToCmap(int type, int rval, int gval, int bval, out Numa? na)
    {
        const string func = "addColorizedGrayToCmap";
        na = null;
        PixColormap cmap = this;
        if (type != L.L_PAINT_DARK && type != L.L_PAINT_LIGHT)
            throw Lept.Arg(func, "invalid type");

        int n = cmap.Count;
        Numa nat = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            cmap.GetColor(i, out int erval, out int egval, out int ebval);
            if (type == L.L_PAINT_LIGHT) {
                if (erval == egval && erval == ebval && erval != 0) {
                    int nrval = (int)(rval * (float)erval / 255.0);
                    int ngval = (int)(gval * (float)egval / 255.0);
                    int nbval = (int)(bval * (float)ebval / 255.0);
                    if (cmap.AddNewColor(nrval, ngval, nbval, out int newindex) != 0) {
                        Log.Warning(func, "no room; colormap full");
                        return false;
                    }
                    nat.AddNumber(newindex);
                } else {
                    nat.AddNumber(256);  /* invalid number; not gray */
                }
            } else {  /* L_PAINT_DARK */
                if (erval == egval && erval == ebval && erval != 255) {
                    int nrval = rval + (int)((255.0 - rval) * (float)erval / 255.0);
                    int ngval = gval + (int)((255.0 - gval) * (float)egval / 255.0);
                    int nbval = bval + (int)((255.0 - bval) * (float)ebval / 255.0);
                    if (cmap.AddNewColor(nrval, ngval, nbval, out int newindex) != 0) {
                        Log.Warning(func, "no room; colormap full");
                        return false;
                    }
                    nat.AddNumber(newindex);
                } else {
                    nat.AddNumber(256);  /* invalid number; not gray */
                }
            }
        }

        na = nat;
        return true;
    }
}
