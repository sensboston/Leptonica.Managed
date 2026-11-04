using System;

namespace Leptonica;

/*
 *  coloring.c: Coloring of gray and color pixels.
 *
 *      Coloring "gray" pixels
 *           Pix.ColorGrayRegions
 *           Pix.ColorGray
 *           Pix.ColorGrayMasked
 *
 *      Adjusting one or more colors to a target color
 *           Pix.SnapColor
 *           Pix.SnapColorCmap
 *
 *      Piecewise linear color mapping based on a source/target pair
 *           Pix.LinearMapToTargetColor
 *           Pix.PixelLinearMapToTargetColor
 *
 *      Fractional shift of RGB towards black or white
 *           Pix.ShiftByComponent
 *           Pix.PixelShiftByComponent
 *           Pix.PixelFractionalShift
 *           Pix.MapWithInvariantHue
 *
 *  There are quite a few "coloring" functions in leptonica.
 *  You can find them in these files:
 *       coloring.c
 *       paintcmap.c
 *       pix2.c
 *       blend.c
 *       enhance.c
 *
 *  They fall into the following categories:
 *
 *  (1) Moving either the light or dark pixels toward a
 *      specified color. (pixColorGray, pixColorGrayMasked)
 *  (2) Forcing all pixels whose color is within some delta of a
 *      specified color to move to that color. (pixSnapColor)
 *  (3) Doing a piecewise linear color shift specified by a source
 *      and a target color.  Each component shifts independently.
 *      (pixLinearMapToTargetColor, pixMapWithInvariantHue).
 *  (4) Shifting all colors by a given fraction of their distance
 *      from 0 (if shifting down) or from 255 (if shifting up).
 *      This is useful for colorizing either the background or
 *      the foreground of a grayscale image. (pixShiftByComponent)
 *  (5) Shifting all colors by a component-dependent fraction of
 *      their distance from 0 (if shifting down) or from 255 (if
 *      shifting up).  This is useful for modifying the color to
 *      compensate for color shifts in acquisition or printing.
 *      (enhance.c: pixColorShiftRGB, pixMosaicColorShiftRGB).
 *  (6) Repainting selected pixels. (paintcmap.c: pixSetSelectMaskedCmap)
 *  (7) Blending a fraction of a specific color with the existing RGB
 *      color.  (pix2.c: pixBlendInRect())
 *  (8) Changing selected colors in a colormap.
 *      (paintcmap.c: pixSetSelectCmap, pixSetSelectMaskedCmap)
 *  (9) Shifting all the pixels towards black or white depending on
 *      the gray value of a second image.  (blend.c: pixFadeWithGray)
 *  (10) Changing the hue, saturation or brightness, by changing one of
 *      these parameters in HSV color space by a fraction of the distance
 *      toward its end-point, but leaving the other two parameters
 *      invariant.  For example, you can change the brightness by moving
 *      each pixel's v-parameter a specified fraction of the distance
 *      toward 0 (darkening) or toward 255 (brightening), without altering
 *      the hue or saturation.  (enhance.c: pixModifySaturation,
 *      pixModifyHue, pixModifyBrightness)
 *
 *  Notes on the port:
 *    - The functions with an optional pixd (pixSnapColor, pixSnapColorCmap,
 *      pixLinearMapToTargetColor, pixShiftByComponent, pixMapWithInvariantHue)
 *      are static; where the C functions return pixd on error, they throw.
 *    - The pixel functions return the pixel value.
 */

public sealed partial class Pix
{
    /*---------------------------------------------------------------------*
     *                        Coloring "gray" pixels                       *
     *---------------------------------------------------------------------*/
    // leptonica: pixColorGrayRegions (coloring.c)
    /// <summary>Colorizes the gray pixels in regions of an image.</summary>
    /// <param name="boxa">of regions in which to apply color</param>
    /// <param name="type">L_PAINT_LIGHT, L_PAINT_DARK</param>
    /// <param name="thresh">average value below/above which pixel is unchanged</param>
    /// <param name="rval">new color to paint</param>
    /// <param name="gval">new color to paint</param>
    /// <param name="bval">new color to paint</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) This generates a new image, where some of the pixels in each
    ///     box in the boxa are colorized.  See pixColorGray() for usage
    ///     with %type and %thresh.  Note that %thresh is only used for
    ///     rgb; it is ignored for colormapped images.
    /// (2) If the input image is colormapped, the new image will be 8 bpp
    ///     colormapped if possible; otherwise, it will be converted
    ///     to 32 bpp rgb.  Only pixels that are strictly gray will be
    ///     colorized.
    /// (3) If the input image is not colormapped, it is converted to rgb.
    ///     A "gray" value for a pixel is determined by averaging the
    ///     components, and the output rgb value is determined from this.
    /// (4) This can be used in conjunction with pixHasHighlightRed() to
    ///     add highlight color to a grayscale image.
    /// </remarks>
    public Pix ColorGrayRegions(Boxa boxa, int type, int thresh, int rval, int gval, int bval)
    {
        const string func = "pixColorGrayRegions";
        int i, n, ncolors, ngray;
        Box box;
        Pix pixd;
        PixColormap? cmap;

        if (Depth == 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (type != L.L_PAINT_LIGHT && type != L.L_PAINT_DARK)
            throw Lept.Arg(func, "invalid type");

            /* If cmapped and there is room in an 8 bpp colormap for
             * expansion, convert pixs to 8 bpp, and colorize. */
        cmap = Colormap;
        if (cmap != null) {
            ncolors = cmap.Count;
            ngray = cmap.CountGrayColors();
            if (ncolors + ngray < 255) {
                pixd = ConvertTo8(true);  /* always new image */
                pixd.ColorGrayRegionsCmap(boxa, type, rval, gval, bval);
                return pixd;
            }
        }

            /* The output will be rgb.  Make sure the thresholds are valid */
        if (type == L.L_PAINT_LIGHT) {  /* thresh should be low */
            if (thresh >= 255)
                throw Lept.Arg(func, "thresh must be < 255");
            if (thresh > 127)
                Log.Warning(func, "threshold set very high");
        } else {  /* type == L_PAINT_DARK; thresh should be high */
            if (thresh <= 0)
                throw Lept.Arg(func, "thresh must be > 0");
            if (thresh < 128)
                Log.Warning(func, "threshold set very low");
        }

        pixd = ConvertTo32();  /* always new image */
        n = boxa.Count;
        for (i = 0; i < n; i++) {
            box = boxa.GetBox(i, L.L_CLONE);
            pixd.ColorGray(box, type, thresh, rval, gval, bval);
        }

        return pixd;
    }

    // leptonica: pixColorGray (coloring.c)
    /// <summary>Colorizes the gray pixels of an image in place.</summary>
    /// <param name="box">[optional] region in which to apply color; can be null</param>
    /// <param name="type">L_PAINT_LIGHT, L_PAINT_DARK</param>
    /// <param name="thresh">average value below/above which pixel is unchanged</param>
    /// <param name="rval">new color to paint</param>
    /// <param name="gval">new color to paint</param>
    /// <param name="bval">new color to paint</param>
    /// <remarks>
    /// (1) This is an in-place operation; pixs is modified.
    ///     If pixs is colormapped, the operation will add colors to the
    ///     colormap.  Otherwise, pixs will be converted to 32 bpp rgb if
    ///     it is initially 8 bpp gray.
    /// (2) If type == L_PAINT_LIGHT, it colorizes non-black pixels,
    ///     preserving antialiasing.
    ///     If type == L_PAINT_DARK, it colorizes non-white pixels,
    ///     preserving antialiasing.
    /// (3) If box is NULL, applies function to the entire image; otherwise,
    ///     clips the operation to the intersection of the box and pix.
    /// (4) If colormapped, calls pixColorGrayCmap(), which applies the
    ///     coloring algorithm only to pixels that are strictly gray.
    /// (5) For RGB, determines a "gray" value by averaging; then uses this
    ///     value, plus the input rgb target, to generate the output
    ///     pixel values.
    /// (6) thresh is only used for rgb; it is ignored for colormapped pix.
    ///     If type == L_PAINT_LIGHT, use thresh = 0 if all pixels are to
    ///     be colored (black pixels will be unaltered).
    ///     In situations where there are a lot of black pixels,
    ///     setting thresh > 0 will make the function considerably
    ///     more efficient without affecting the final result.
    ///     If type == L_PAINT_DARK, use thresh = 255 if all pixels
    ///     are to be colored (white pixels will be unaltered).
    ///     In situations where there are a lot of white pixels,
    ///     setting thresh &lt; 255 will make the function considerably
    ///     more efficient without affecting the final result.
    /// </remarks>
    public void ColorGray(Box? box, int type, int thresh, int rval, int gval, int bval)
    {
        const string func = "pixColorGray";
        int i, j, w, h, d, wpl, x1, x2, y1, y2, bw, bh;
        int nrval, ngval, nbval, aveval;
        float factor;
        uint val32;
        Pix? pixt;
        PixColormap? cmap;

        if (type != L.L_PAINT_LIGHT && type != L.L_PAINT_DARK)
            throw Lept.Arg(func, "invalid type");

        cmap = Colormap;
        GetDimensions(out w, out h, out d);
        if (cmap == null && d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not cmapped, 8 bpp or rgb");
        if (cmap != null) {
            ColorGrayCmap(box, type, rval, gval, bval);
            return;
        }

            /* rgb or 8 bpp gray image; check the thresh */
        if (type == L.L_PAINT_LIGHT) {  /* thresh should be low */
            if (thresh >= 255)
                throw Lept.Arg(func, "thresh must be < 255; else this is a no-op");
            if (thresh > 127)
                Log.Warning(func, "threshold set very high");
        } else {  /* type == L_PAINT_DARK; thresh should be high */
            if (thresh <= 0)
                throw Lept.Arg(func, "thresh must be > 0; else this is a no-op");
            if (thresh < 128)
                Log.Warning(func, "threshold set very low");
        }

            /* In-place conversion to 32 bpp if necessary */
        if (d == 8) {
            pixt = ConvertTo32();
            TransferAllData(ref pixt, true, false);
        }

        if (box == null) {
            x1 = y1 = 0;
            x2 = w;
            y2 = h;
        } else {
            box.GetGeometry(out x1, out y1, out bw, out bh);
            x2 = x1 + bw - 1;
            y2 = y1 + bh - 1;
        }

        uint[] data = Data;
        wpl = Wpl;
        factor = 1.0f / 255.0f;
        for (i = y1; i <= y2; i++) {
            if (i < 0 || i >= h)
                continue;
            int line = i * wpl;
            for (j = x1; j <= x2; j++) {
                if (j < 0 || j >= w)
                    continue;
                val32 = data[line + j];
                aveval = (int)(((val32 >> 24) + ((val32 >> 16) & 0xff) +
                                ((val32 >> 8) & 0xff)) / 3);
                if (type == L.L_PAINT_LIGHT) {
                    if (aveval < thresh)  /* skip sufficiently dark pixels */
                        continue;
                    nrval = (int)(rval * aveval * factor);
                    ngval = (int)(gval * aveval * factor);
                    nbval = (int)(bval * aveval * factor);
                } else {  /* type == L_PAINT_DARK */
                    if (aveval > thresh)  /* skip sufficiently light pixels */
                        continue;
                    nrval = rval + (int)((255.0 - rval) * aveval * factor);
                    ngval = gval + (int)((255.0 - gval) * aveval * factor);
                    nbval = bval + (int)((255.0 - bval) * aveval * factor);
                }
                ComposeRGBPixel(nrval, ngval, nbval, out val32);
                data[line + j] = val32;
            }
        }
    }

    // leptonica: pixColorGrayMasked (coloring.c)
    /// <summary>Colorizes the gray pixels of an image under a mask.</summary>
    /// <param name="pixm">1 bpp mask, through which to apply color</param>
    /// <param name="type">L_PAINT_LIGHT, L_PAINT_DARK</param>
    /// <param name="thresh">average value below/above which pixel is unchanged</param>
    /// <param name="rval">new color to paint</param>
    /// <param name="gval">new color to paint</param>
    /// <param name="bval">new color to paint</param>
    /// <returns>pixd colorized</returns>
    /// <remarks>
    /// (1) This generates a new image, where some of the pixels under
    ///     FG in the mask are colorized.
    /// (2) See pixColorGray() for usage with %type and %thresh.  Note
    ///     that %thresh is only used for rgb; it is ignored for
    ///     colormapped images.  In most cases, the mask will be over
    ///     the darker parts and %type == L_PAINT_DARK.
    /// (3) If pixs is colormapped this calls pixColorMaskedCmap(),
    ///     which adds colors to the colormap for pixd; it only adds
    ///     colors corresponding to strictly gray colors in the colormap.
    ///     Otherwise, if pixs is 8 bpp gray, pixd will be 32 bpp rgb.
    /// (4) If pixs is 32 bpp rgb, for each pixel a "gray" value is
    ///     found by averaging.  This average is then used with the
    ///     input rgb target to generate the output pixel values.
    /// (5) This can be used in conjunction with pixHasHighlightRed() to
    ///     add highlight color to a grayscale image.
    /// </remarks>
    public Pix ColorGrayMasked(Pix pixm, int type, int thresh, int rval, int gval, int bval)
    {
        const string func = "pixColorGrayMasked";
        int i, j, w, h, d, wm, hm, wmin, hmin, wpl, wplm;
        int nrval, ngval, nbval, aveval;
        float factor;
        uint val32;
        Pix pixd;
        PixColormap? cmap;

        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm undefined or not 1 bpp");
        if (type != L.L_PAINT_LIGHT && type != L.L_PAINT_DARK)
            throw Lept.Arg(func, "invalid type");

        cmap = Colormap;
        GetDimensions(out w, out h, out d);
        if (cmap == null && d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not cmapped, 8 bpp gray or 32 bpp");
        if (cmap != null) {
            pixd = Copy(null, this);
            pixd.ColorGrayMaskedCmap(pixm, type, rval, gval, bval);
            return pixd;
        }

            /* rgb or 8 bpp gray image; check the thresh */
        if (type == L.L_PAINT_LIGHT) {  /* thresh should be low */
            if (thresh >= 255)
                throw Lept.Arg(func, "thresh must be < 255; else this is a no-op");
            if (thresh > 127)
                Log.Warning(func, "threshold set very high");
        } else {  /* type == L_PAINT_DARK; thresh should be high */
            if (thresh <= 0)
                throw Lept.Arg(func, "thresh must be > 0; else this is a no-op");
            if (thresh < 128)
                Log.Warning(func, "threshold set very low");
        }

        pixm.GetDimensions(out wm, out hm, out _);
        if (wm != w)
            Log.Warning(func, $"wm = {wm} differs from w = {w}");
        if (hm != h)
            Log.Warning(func, $"hm = {hm} differs from h = {h}");
        wmin = Math.Min(w, wm);
        hmin = Math.Min(h, hm);
        if (d == 8)
            pixd = ConvertTo32();
        else
            pixd = Copy(null, this);

        uint[] data = pixd.Data;
        wpl = pixd.Wpl;
        uint[] datam = pixm.Data;
        wplm = pixm.Wpl;
        factor = 1.0f / 255.0f;
        for (i = 0; i < hmin; i++) {
            int line = i * wpl;
            ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
            for (j = 0; j < wmin; j++) {
                if (ArrayAccess.GetDataBit(linem, j) == 0)
                    continue;
                val32 = data[line + j];
                aveval = (int)(((val32 >> 24) + ((val32 >> 16) & 0xff) +
                                ((val32 >> 8) & 0xff)) / 3);
                if (type == L.L_PAINT_LIGHT) {
                    if (aveval < thresh)  /* skip sufficiently dark pixels */
                        continue;
                    nrval = (int)(rval * aveval * factor);
                    ngval = (int)(gval * aveval * factor);
                    nbval = (int)(bval * aveval * factor);
                } else {  /* type == L_PAINT_DARK */
                    if (aveval > thresh)  /* skip sufficiently light pixels */
                        continue;
                    nrval = rval + (int)((255.0 - rval) * aveval * factor);
                    ngval = gval + (int)((255.0 - gval) * aveval * factor);
                    nbval = bval + (int)((255.0 - bval) * aveval * factor);
                }
                ComposeRGBPixel(nrval, ngval, nbval, out val32);
                data[line + j] = val32;
            }
        }

        return pixd;
    }

    /*------------------------------------------------------------------*
     *            Adjusting one or more colors to a target color        *
     *------------------------------------------------------------------*/
    // leptonica: pixSnapColor (coloring.c)
    /// <summary>Sets all pixels within a distance of a color to a target color.</summary>
    /// <param name="pixd">[optional]; either null or equal to pixs for in-place</param>
    /// <param name="pixs">colormapped or 8 bpp gray or 32 bpp rgb</param>
    /// <param name="srcval">color center to be selected for change: 0xrrggbb00</param>
    /// <param name="dstval">target color for pixels: 0xrrggbb00</param>
    /// <param name="diff">max absolute difference, applied to all components</param>
    /// <returns>pixd with all pixels within diff of srcval set to dstval</returns>
    /// <remarks>
    /// (1) For inplace operation, call it this way:
    ///      pixSnapColor(pixs, pixs, ... )
    /// (2) For generating a new pixd:
    ///      pixd = pixSnapColor(NULL, pixs, ...)
    /// (3) If pixs has a colormap, it is handled by pixSnapColorCmap().
    /// (4) All pixels within 'diff' of 'srcval', componentwise,
    ///     will be changed to 'dstval'.
    /// </remarks>
    public static Pix SnapColor(Pix? pixd, Pix pixs, uint srcval, uint dstval, int diff)
    {
        const string func = "pixSnapColor";
        int val, sval, dval;
        int rval, gval, bval, rsval, gsval, bsval;
        int i, j, w, h, d, wpl;
        uint pixel;

        if (pixd != null && pixd != pixs)
            throw Lept.Arg(func, "pixd exists, but != pixs");

        if (pixs.Colormap != null)
            return SnapColorCmap(pixd, pixs, srcval, dstval, diff);

            /* pixs does not have a colormap; it must be 8 bpp gray or
             * 32 bpp rgb. */
        if (pixs.Depth < 8)
            throw Lept.Arg(func, "pixs is < 8 bpp");

            /* Do the work on pixd */
        if (pixd == null)
            pixd = Copy(null, pixs);

        pixd.GetDimensions(out w, out h, out d);
        uint[] data = pixd.Data;
        wpl = pixd.Wpl;
        if (d == 8) {
            sval = (int)(srcval & 0xff);
            dval = (int)(dstval & 0xff);
            for (i = 0; i < h; i++) {
                Span<uint> line = data.AsSpan(i * wpl, wpl);
                for (j = 0; j < w; j++) {
                    val = ArrayAccess.GetDataByte(line, j);
                    if (Math.Abs(val - sval) <= diff)
                        ArrayAccess.SetDataByte(line, j, dval);
                }
            }
        } else {  /* d == 32 */
            ExtractRGBValues(srcval, out rsval, out gsval, out bsval);
            for (i = 0; i < h; i++) {
                int line = i * wpl;
                for (j = 0; j < w; j++) {
                    pixel = data[line + j];
                    ExtractRGBValues(pixel, out rval, out gval, out bval);
                    if ((Math.Abs(rval - rsval) <= diff) &&
                        (Math.Abs(gval - gsval) <= diff) &&
                        (Math.Abs(bval - bsval) <= diff))
                        data[line + j] = dstval;  /* replace */
                }
            }
        }

        return pixd;
    }

    // leptonica: pixSnapColorCmap (coloring.c)
    /// <summary>Sets all colors of a colormapped image within a distance of a color to a target color.</summary>
    /// <param name="pixd">[optional]; either null or equal to pixs for in-place</param>
    /// <param name="pixs">colormapped</param>
    /// <param name="srcval">color center to be selected for change: 0xrrggbb00</param>
    /// <param name="dstval">target color for pixels: 0xrrggbb00</param>
    /// <param name="diff">max absolute difference, applied to all components</param>
    /// <returns>pixd with all pixels within diff of srcval set to dstval</returns>
    /// <remarks>
    /// (1) For inplace operation, call it this way:
    ///      pixSnapCcmap(pixs, pixs, ... )
    /// (2) For generating a new pixd:
    ///      pixd = pixSnapCmap(NULL, pixs, ...)
    /// (3) pixs must have a colormap.
    /// (4) All colors within 'diff' of 'srcval', componentwise,
    ///     will be changed to 'dstval'.
    /// </remarks>
    public static Pix SnapColorCmap(Pix? pixd, Pix pixs, uint srcval, uint dstval, int diff)
    {
        const string func = "pixSnapColorCmap";
        int i, ncolors, index, found;
        int rval, gval, bval, rsval, gsval, bsval, rdval, gdval, bdval;
        int[] tab;
        Pix pixm;
        PixColormap cmap;

        if (pixs.Colormap == null)
            throw Lept.Arg(func, "cmap not found");
        if (pixd != null && pixd != pixs)
            throw Lept.Arg(func, "pixd exists, but != pixs");

        if (pixd == null)
            pixd = Copy(null, pixs);

            /* If no free colors, look for one close to the target
             * that can be commandeered. */
        cmap = pixd.Colormap!;
        ncolors = cmap.Count;
        ExtractRGBValues(srcval, out rsval, out gsval, out bsval);
        ExtractRGBValues(dstval, out rdval, out gdval, out bdval);
        found = 0;
        index = 0;
        if (cmap.FreeCount == 0) {
            for (i = 0; i < ncolors; i++) {
                cmap.GetColor(i, out rval, out gval, out bval);
                if ((Math.Abs(rval - rsval) <= diff) &&
                    (Math.Abs(gval - gsval) <= diff) &&
                    (Math.Abs(bval - bsval) <= diff)) {
                    index = i;
                    cmap.ResetColor(index, rdval, gdval, bdval);
                    found = 1;
                    break;
                }
            }
        } else {  /* just add the new color */
            cmap.AddColor(rdval, gdval, bdval);
            ncolors = cmap.Count;
            index = ncolors - 1;  /* index of new destination color */
            found = 1;
        }

        if (found == 0) {
            Log.Info(func, "nothing to do");
            return pixd;
        }

            /* For each color in cmap that is close enough to srcval,
             * set the tab value to 1.  Then generate a 1 bpp mask with
             * fg pixels for every pixel in pixd that is close enough
             * to srcval (i.e., has value 1 in tab). */
        tab = new int[256];
        for (i = 0; i < ncolors; i++) {
            cmap.GetColor(i, out rval, out gval, out bval);
            if ((Math.Abs(rval - rsval) <= diff) &&
                (Math.Abs(gval - gsval) <= diff) &&
                (Math.Abs(bval - bsval) <= diff))
                tab[i] = 1;
        }
        pixm = pixd.MakeMaskFromLUT(tab);

            /* Use the binary mask to set all selected pixels to
             * the dest color index (pixSetMasked() takes the color
             * of a colormapped pixd). */
        pixd.SetMasked(pixm, dstval);

            /* Remove all unused colors from the colormap. */
        pixd.RemoveUnusedColors();

        return pixd;
    }

    /*---------------------------------------------------------------------*
     *     Piecewise linear color mapping based on a source/target pair    *
     *---------------------------------------------------------------------*/
    // leptonica: pixLinearMapToTargetColor (coloring.c)
    /// <summary>Piecewise linear mapping of each component, based on a source/target color pair.</summary>
    /// <param name="pixd">[optional]; either null or equal to pixs for in-place</param>
    /// <param name="pixs">32 bpp rgb</param>
    /// <param name="srcval">source color: 0xrrggbb00</param>
    /// <param name="dstval">target color: 0xrrggbb00</param>
    /// <returns>pixd with all pixels mapped based on the srcval/destval mapping</returns>
    /// <remarks>
    /// (1) For each component (r, b, g) separately, this does a piecewise
    ///     linear mapping of the colors in pixs to colors in pixd.
    ///     If rs and rd are the red src and dest components in %srcval and
    ///     %dstval, then the range [0 ... rs] in pixs is mapped to
    ///     [0 ... rd] in pixd.  Likewise, the range [rs ... 255] in pixs
    ///     is mapped to [rd ... 255] in pixd.  And similarly for green
    ///     and blue.
    /// (2) The mapping will in general change the hue of the pixels.
    ///     However, if the src and dst targets are related by
    ///     a transformation given by pixelFractionalShift(), the hue
    ///     is invariant.  A special case is where the dest in the
    ///     map is white (255, 255, 255) for an arbitrary srcval.
    /// (3) For inplace operation, call it this way:
    ///       pixLinearMapToTargetColor(pixs, pixs, ... );
    ///     For generating a new pixd:
    ///       pixd = pixLinearMapToTargetColor(NULL, pixs, ...);
    /// (4) See pixShiftWithInvariantHue() for a special case of this function.
    /// </remarks>
    public static Pix LinearMapToTargetColor(Pix? pixd, Pix pixs, uint srcval, uint dstval)
    {
        const string func = "pixLinearMapToTargetColor";
        int i, j, w, h, wpl;
        int rval, gval, bval, rsval, gsval, bsval, rdval, gdval, bdval;
        int[] rtab, gtab, btab;
        uint pixel;

        if (pixs.Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        if (pixd != null && pixd != pixs)
            throw Lept.Arg(func, "pixd exists, but != pixs");

            /* Do the work on pixd */
        if (pixd == null)
            pixd = Copy(null, pixs);

        ExtractRGBValues(srcval, out rsval, out gsval, out bsval);
        ExtractRGBValues(dstval, out rdval, out gdval, out bdval);
        rsval = Math.Min(254, Math.Max(1, rsval));
        gsval = Math.Min(254, Math.Max(1, gsval));
        bsval = Math.Min(254, Math.Max(1, bsval));
        rtab = new int[256];
        gtab = new int[256];
        btab = new int[256];
        for (i = 0; i < 256; i++) {
            if (i <= rsval)
                rtab[i] = (i * rdval) / rsval;
            else
                rtab[i] = rdval + ((255 - rdval) * (i - rsval)) / (255 - rsval);
            if (i <= gsval)
                gtab[i] = (i * gdval) / gsval;
            else
                gtab[i] = gdval + ((255 - gdval) * (i - gsval)) / (255 - gsval);
            if (i <= bsval)
                btab[i] = (i * bdval) / bsval;
            else
                btab[i] = bdval + ((255 - bdval) * (i - bsval)) / (255 - bsval);
        }
        pixd.GetDimensions(out w, out h, out _);
        uint[] data = pixd.Data;
        wpl = pixd.Wpl;
        for (i = 0; i < h; i++) {
            int line = i * wpl;
            for (j = 0; j < w; j++) {
                pixel = data[line + j];
                ExtractRGBValues(pixel, out rval, out gval, out bval);
                ComposeRGBPixel(rtab[rval], gtab[gval], btab[bval], out pixel);
                data[line + j] = pixel;
            }
        }

        return pixd;
    }

    // leptonica: pixelLinearMapToTargetColor (coloring.c)
    /// <summary>The piecewise linear mapping of pixLinearMapToTargetColor() for a single color.</summary>
    /// <param name="scolor">rgb source color: 0xrrggbb00</param>
    /// <param name="srcmap">source mapping color: 0xrrggbb00</param>
    /// <param name="dstmap">target mapping color: 0xrrggbb00</param>
    /// <returns>rgb dest color: 0xrrggbb00</returns>
    /// <remarks>
    /// (1) This does this does a piecewise linear mapping of each
    ///     component of %scolor to %dcolor, based on the relation
    ///     between the components of %srcmap and %dstmap.  It is the
    ///     same transformation, performed on a single color, as mapped
    ///     on every pixel in a pix by pixLinearMapToTargetColor().
    /// (2) For each component, if the sval is larger than the smap,
    ///     the dval will be pushed up from dmap towards white.
    ///     Otherwise, dval will be pushed down from dmap towards black.
    ///     This is because you can visualize the transformation as
    ///     a linear stretching where smap moves to dmap, and everything
    ///     else follows linearly with 0 and 255 fixed.
    /// (3) The mapping will in general change the hue of %scolor.
    ///     However, if the %srcmap and %dstmap targets are related by
    ///     a transformation given by pixelFractionalShift(), the hue
    ///     will be invariant.
    /// </remarks>
    public static uint PixelLinearMapToTargetColor(uint scolor, uint srcmap, uint dstmap)
    {
        int srval, sgval, sbval, drval, dgval, dbval;
        int srmap, sgmap, sbmap, drmap, dgmap, dbmap;

        ExtractRGBValues(scolor, out srval, out sgval, out sbval);
        ExtractRGBValues(srcmap, out srmap, out sgmap, out sbmap);
        ExtractRGBValues(dstmap, out drmap, out dgmap, out dbmap);
        srmap = Math.Min(254, Math.Max(1, srmap));
        sgmap = Math.Min(254, Math.Max(1, sgmap));
        sbmap = Math.Min(254, Math.Max(1, sbmap));

        if (srval <= srmap)
            drval = (srval * drmap) / srmap;
        else
            drval = drmap + ((255 - drmap) * (srval - srmap)) / (255 - srmap);
        if (sgval <= sgmap)
            dgval = (sgval * dgmap) / sgmap;
        else
            dgval = dgmap + ((255 - dgmap) * (sgval - sgmap)) / (255 - sgmap);
        if (sbval <= sbmap)
            dbval = (sbval * dbmap) / sbmap;
        else
            dbval = dbmap + ((255 - dbmap) * (sbval - sbmap)) / (255 - sbmap);

        ComposeRGBPixel(drval, dgval, dbval, out uint pdcolor);
        return pdcolor;
    }

    /*------------------------------------------------------------------*
     *          Fractional shift of RGB towards black or white          *
     *------------------------------------------------------------------*/
    // leptonica: pixShiftByComponent (coloring.c)
    /// <summary>Shifts each component of the pixels toward black or white by a fraction.</summary>
    /// <param name="pixd">[optional]; either null or equal to pixs for in-place</param>
    /// <param name="pixs">32 bpp rgb, cmap OK</param>
    /// <param name="srcval">source color: 0xrrggbb00</param>
    /// <param name="dstval">target color: 0xrrggbb00</param>
    /// <returns>pixd with all pixels mapped based on the srcval/destval mapping</returns>
    /// <remarks>
    /// (1) For each component (r, b, g) separately, this does a linear
    ///     mapping of the colors in pixs to colors in pixd.
    ///     Let rs and rd be the red src and dest components in %srcval and
    ///     %dstval, and rval is the red component of the src pixel.
    ///     Then for all pixels in pixs, the mapping for the red
    ///     component from pixs to pixd is:
    ///        if (rd &lt;= rs)   (shift toward black)
    ///            rval --&gt; (rd/rs) * rval
    ///        if (rd > rs)    (shift toward white)
    ///           (255 - rval) --&gt; ((255 - rs)/(255 - rd)) * (255 - rval)
    ///     Thus if rd &lt;= rs, the red component of all pixels is
    ///     mapped by the same fraction toward white, and if rd > rs,
    ///     they are mapped by the same fraction toward black.
    ///     This is essentially a different linear TRC (gamma = 1)
    ///     for each component.  The source and target color inputs are
    ///     just used to generate the three fractions.
    /// (2) Note that this mapping differs from that in
    ///     pixLinearMapToTargetColor(), which maps rs --&gt; rd and does
    ///     a piecewise stretching in between.
    /// (3) For inplace operation, call it this way:
    ///       pixFractionalShiftByComponent(pixs, pixs, ... )
    /// (4) For generating a new pixd:
    ///       pixd = pixLinearMapToTargetColor(NULL, pixs, ...)
    /// (5) A simple application is to color a grayscale image.
    ///     A light background can be colored using srcval = 0xffffff00
    ///     and picking a target background color for dstval.
    ///     A dark foreground can be colored by using srcval = 0x0
    ///     and choosing a target foreground color for dstval.
    /// </remarks>
    public static Pix ShiftByComponent(Pix? pixd, Pix pixs, uint srcval, uint dstval)
    {
        const string func = "pixShiftByComponent";
        int i, j, w, h, wpl;
        int rval, gval, bval, rsval, gsval, bsval, rdval, gdval, bdval;
        int[] rtab, gtab, btab;
        uint pixel;
        PixColormap? cmap;

        if (pixd != null && pixd != pixs)
            throw Lept.Arg(func, "pixd exists, but != pixs");
        if (pixs.Depth != 32 && pixs.Colormap == null)
            throw Lept.Arg(func, "pixs not cmapped or 32 bpp");

            /* Do the work on pixd */
        if (pixd == null)
            pixd = Copy(null, pixs);

            /* If colormapped, just modify it */
        if ((cmap = pixd.Colormap) != null) {
            cmap.ShiftByComponent(srcval, dstval);
            return pixd;
        }

        ExtractRGBValues(srcval, out rsval, out gsval, out bsval);
        ExtractRGBValues(dstval, out rdval, out gdval, out bdval);
        rtab = new int[256];
        gtab = new int[256];
        btab = new int[256];
        for (i = 0; i < 256; i++) {
            if (rdval == rsval)
                rtab[i] = i;
            else if (rdval < rsval)
                rtab[i] = (i * rdval) / rsval;
            else
                rtab[i] = 255 - (255 - rdval) * (255 - i) / (255 - rsval);
            if (gdval == gsval)
                gtab[i] = i;
            else if (gdval < gsval)
                gtab[i] = (i * gdval) / gsval;
            else
                gtab[i] = 255 - (255 - gdval) * (255 - i) / (255 - gsval);
            if (bdval == bsval)
                btab[i] = i;
            else if (bdval < bsval)
                btab[i] = (i * bdval) / bsval;
            else
                btab[i] = 255 - (255 - bdval) * (255 - i) / (255 - bsval);
        }
        pixd.GetDimensions(out w, out h, out _);
        uint[] data = pixd.Data;
        wpl = pixd.Wpl;
        for (i = 0; i < h; i++) {
            int line = i * wpl;
            for (j = 0; j < w; j++) {
                pixel = data[line + j];
                ExtractRGBValues(pixel, out rval, out gval, out bval);
                ComposeRGBPixel(rtab[rval], gtab[gval], btab[bval], out pixel);
                data[line + j] = pixel;
            }
        }

        return pixd;
    }

    // leptonica: pixelShiftByComponent (coloring.c)
    /// <summary>The shift of pixShiftByComponent() for a single pixel.</summary>
    /// <param name="rval">pixel components</param>
    /// <param name="gval">pixel components</param>
    /// <param name="bval">pixel components</param>
    /// <param name="srcval">source color: 0xrrggbb00</param>
    /// <param name="dstval">target color: 0xrrggbb00</param>
    /// <returns>rgb value</returns>
    /// <remarks>
    /// (1) This is a linear transformation that gives the same result
    ///     on a single pixel as pixShiftByComponent() gives
    ///     on a pix.  Each component is handled separately.  If
    ///     the dest component is larger than the src, then the
    ///     component is pushed toward 255 by the same fraction as
    ///     the src --&gt; dest shift.
    /// </remarks>
    public static uint PixelShiftByComponent(int rval, int gval, int bval, uint srcval, uint dstval)
    {
        int rsval, rdval, gsval, gdval, bsval, bdval, rs, gs, bs;

        ExtractRGBValues(srcval, out rsval, out gsval, out bsval);
        ExtractRGBValues(dstval, out rdval, out gdval, out bdval);
        if (rdval == rsval)
            rs = rval;
        else if (rdval < rsval)
            rs = (rval * rdval) / rsval;
        else
            rs = 255 - (255 - rdval) * (255 - rval) / (255 - rsval);
        if (gdval == gsval)
            gs = gval;
        else if (gdval < gsval)
            gs = (gval * gdval) / gsval;
        else
            gs = 255 - (255 - gdval) * (255 - gval) / (255 - gsval);
        if (bdval == bsval)
            bs = bval;
        else if (bdval < bsval)
            bs = (bval * bdval) / bsval;
        else
            bs = 255 - (255 - bdval) * (255 - bval) / (255 - bsval);
        ComposeRGBPixel(rs, gs, bs, out uint ppixel);
        return ppixel;
    }

    // leptonica: pixelFractionalShift (coloring.c)
    /// <summary>Shifts the components of a pixel a fraction toward black or white.</summary>
    /// <param name="rval">red source component</param>
    /// <param name="gval">green source component</param>
    /// <param name="bval">blue source component</param>
    /// <param name="fract">negative toward black; positive toward white</param>
    /// <returns>resulting rgb value</returns>
    /// <remarks>
    /// (1) This linear transformation shifts each component a fraction
    ///     toward either black (%fract &lt; 0) or white (%fract > 0).
    /// (2) It changes the saturation and intensity, but leaves the hue
    ///     invariant.  See usage in pixLinearMapToTargetColor() and
    ///     pixMapWithInvariantHue().
    /// (3) %fract is in the range [-1 .... +1].  If %fract &lt; 0,
    ///     saturation is increased and brightness is reduced.  The
    ///     opposite results if %fract > 0.  If %fract == -1,
    ///     the resulting pixel is black; %fract == 1 results in white.
    /// </remarks>
    public static uint PixelFractionalShift(int rval, int gval, int bval, float fract)
    {
        int nrval, ngval, nbval;

        if (fract < -1.0 || fract > 1.0)
            throw Lept.Arg("pixelFractionalShift", "fraction not in [-1 ... +1]");

        nrval = (fract < 0) ? (int)((1.0 + fract) * rval + 0.5) :
                rval + (int)(fract * (255 - rval) + 0.5);
        ngval = (fract < 0) ? (int)((1.0 + fract) * gval + 0.5) :
                gval + (int)(fract * (255 - gval) + 0.5);
        nbval = (fract < 0) ? (int)((1.0 + fract) * bval + 0.5) :
                bval + (int)(fract * (255 - bval) + 0.5);
        ComposeRGBPixel(nrval, ngval, nbval, out uint ppixel);
        return ppixel;
    }

    // leptonica: pixMapWithInvariantHue (coloring.c)
    /// <summary>Hue-preserving linear mapping of the colors, defined by a source color and a fraction.</summary>
    /// <param name="pixd">[optional]; either null or equal to pixs for in-place</param>
    /// <param name="pixs">32 bpp rgb</param>
    /// <param name="srcval">reference source color: 0xrrggbb00</param>
    /// <param name="fract">fraction toward white of dest color</param>
    /// <returns>pixd with all pixels mapped based on the srcval/destval mapping</returns>
    /// <remarks>
    /// (1) The combination of %srcval and %fract define the linear
    ///     hue-preserving transformation, that is applied to all pixels.
    /// (2) %fract is in the range [-1 .... +1].  If %fract &lt; 0,
    ///     saturation is increased and brightness is reduced.  The
    ///     opposite results if %fract > 0.  If %fract == -1,
    ///     %srcval is mapped to black; if %fract == 1, it is mapped to white.
    /// (3) For inplace operation, call it this way:
    ///       pixMapWithInvariatHue(pixs, pixs, ... );
    ///     For generating a new pixd:
    ///       pixd = pixMapWithInvariantHue(NULL, pixs, ...);
    /// </remarks>
    public static Pix MapWithInvariantHue(Pix? pixd, Pix pixs, uint srcval, float fract)
    {
        const string func = "pixMapWithInvariantHue";
        int rval, gval, bval;
        uint dstval;

        if (pixs.Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        if (pixd != null && pixd != pixs)
            throw Lept.Arg(func, "pixd exists, but != pixs");
        if (fract < -1.0 || fract > 1.0)
            throw Lept.Arg(func, "fraction not in [-1 ... +1]");

            /* Generate the dstval that is %fract toward white from %srcval */
        ExtractRGBValues(srcval, out rval, out gval, out bval);
        dstval = PixelFractionalShift(rval, gval, bval, fract);

            /* Use the (%srcval, dstval) pair to define the linear transform */
        return LinearMapToTargetColor(pixd, pixs, srcval, dstval);
    }
}
