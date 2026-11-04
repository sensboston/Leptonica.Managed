using System;

namespace Leptonica;

/*
 *  colorcontent.c: Color content of images.
 *
 *      Build an image of the color content, on a per-pixel basis,
 *      as a measure of the amount of divergence of each color
 *      component (R,G,B) from gray.
 *         Pix.ColorContent (pixColorContent)
 *
 *      Find the 'amount' of color in an image, on a per-pixel basis,
 *      as a measure of the difference of the pixel color from gray.
 *         Pix.ColorMagnitude (pixColorMagnitude)
 *
 *      Find the fraction of pixels with "color" that are not close to black
 *         Pix.ColorFraction (pixColorFraction)
 *
 *      Do a linear TRC to map colors so that the three input reference
 *      values go to white.  These three numbers are typically the median
 *      or average background values.
 *         Pix.ColorShiftWhitePoint (pixColorShiftWhitePoint)
 *
 *      Generate a mask over pixels that have sufficient color and
 *      are not too close to gray pixels.
 *         Pix.MaskOverColorPixels (pixMaskOverColorPixels)
 *
 *      Generate a mask over dark pixels with little color
 *         Pix.MaskOverGrayPixels (pixMaskOverGrayPixels)
 *
 *      Generate mask over pixels within a prescribed cube in RGB space
 *         Pix.MaskOverColorRange (pixMaskOverColorRange)
 *
 *      Determine if there are significant color regions that are
 *      not background in a page image
 *         Pix.FindColorRegions (pixFindColorRegions)
 *
 *      Find the number of perceptually significant gray intensities
 *      in a grayscale image.
 *         Pix.NumSignificantGrayColors (pixNumSignificantGrayColors)
 *
 *      Identify images where color quantization will cause posterization
 *      due to the existence of many colors in low-gradient regions.
 *         Pix.ColorsForQuantization (pixColorsForQuantization)
 *
 *      Find the number of unique colors in an image
 *         Pix.NumColors (pixNumColors)
 *
 *      Lossless conversion of RGB image to colormapped
 *         Pix.ConvertRGBToCmapLossless (pixConvertRGBToCmapLossless)
 *
 *      Find the most "populated" colors in the image (and quantize)
 *         Pix.GetMostPopulatedColors (pixGetMostPopulatedColors)
 *         Pix.SimpleColorQuantize (pixSimpleColorQuantize)
 *
 *      Construct a color histogram based on rgb indices
 *         Pix.GetRGBHistogram (pixGetRGBHistogram)
 *         L.MakeRGBIndexTables (makeRGBIndexTables)
 *         L.GetRGBFromIndex (getRGBFromIndex)
 *
 *      Identify images that have highlight (red) color
 *         Pix.HasHighlightRed (pixHasHighlightRed)
 *
 *  Color is tricky.  If we consider gray (r = g = b) to have no color
 *  content, how should we define the color content in each component
 *  of an arbitrary pixel, as well as the overall color magnitude?
 *
 *  I can think of three ways to define the color content in each component:
 *
 *  (1) Linear.  For each component, take the difference from the average
 *      of all three.
 *  (2) Linear.  For each component, take the difference from the average
 *      of the other two.
 *  (3) Nonlinear.  For each component, take the minimum of the differences
 *      from the other two.
 *
 *  How might one choose from among these?  Consider two different situations:
 *  (a) r = g = 0, b = 255            {255}   /255/   <255>
 *  (b) r = 0, g = 127, b = 255       {191}   /128/   <255>
 *  How much g is in each of these?  The three methods above give:
 *  (a)  1: 85   2: 127   3: 0        [85]
 *  (b)  1: 0    2: 0     3: 127      [0]
 *  How much b is in each of these?
 *  (a)  1: 170  2: 255   3: 255      [255]
 *  (b)  1: 127  2: 191   3: 127      [191]
 *  The number I'd "like" to give is in [].  (Please don't ask why, it's
 *  just a feeling.
 *
 *  So my preferences seem to be somewhere between (1) and (2).
 *  (3) is just too "decisive!"  Let's pick (2).
 *
 *  We also allow compensation for white imbalance.  For each
 *  component, we do a linear TRC (gamma = 1.0), where the black
 *  point remains at 0 and the white point is given by the input
 *  parameter.  This is equivalent to doing a global remapping,
 *  as with pixGlobalNormRGB(), followed by color content (or magnitude)
 *  computation, but without the overhead of first creating the
 *  white point normalized image.
 *
 *  Another useful property is the overall color magnitude in the pixel.
 *  For this there are again several choices, such as:
 *      (a) rms deviation from the mean
 *      (b) the average L1 deviation from the mean
 *      (c) the maximum (over components) of one of the color
 *          content measures given above.
 *
 *  For now, we will consider three of the methods in (c):
 *    L_INTERMED_DIFF
 *        Define the color magnitude as the intermediate value of the
 *        three differences between the three components.
 *        For (a) and (b) above, this value is in /../.
 *     L_AVE_MAX_DIFF_2
 *        Define the color magnitude as the maximum over components
 *        of the difference between the component value and the
 *        average of the other two.  It is easy to show that
 *        this is equivalent to selecting the two component values
 *        that are closest to each other, averaging them, and
 *        using the distance from that average to the third component.
 *        For (a) and (b) above, this value is in {..}.
 *    L_MAX_DIFF
 *        Define the color magnitude as the maximum value of the
 *        three differences between the three components.
 *        For (a) and (b) above, this value is in <..>.
 *
 *  Differences from C:
 *    - Optional output pointers become plain out parameters that are always
 *      computed (pixColorContent computes all three components).
 *    - Error returns throw.
 */

/// <summary>Constants from pix.h used by colorcontent.c.</summary>
public static partial class L
{
    /* Choice of color magnitude calculation (pix.h) */
    /// <summary>Uses the intermediate value of the differences between the component values.</summary>
    public const int L_INTERMED_DIFF = 1;

    /// <summary>Uses the difference between the average of the two closest components and the third.</summary>
    public const int L_AVE_MAX_DIFF_2 = 2;

    /// <summary>Uses the maximum difference between the component values.</summary>
    public const int L_MAX_DIFF = 3;

    // leptonica: makeRGBIndexTables (colorcontent.c)
    /// <summary>Makes the 256-entry tables that map rgb sample values to an rgb index.</summary>
    /// <param name="prtab">256-entry red index table</param>
    /// <param name="pgtab">256-entry green index table</param>
    /// <param name="pbtab">256-entry blue index table</param>
    /// <param name="sigbits">2-6, significant bits retained in the quantizer
    ///                       for each component of the input image</param>
    /// <remarks>
    /// (1) These tables are used to map from rgb sample values to
    ///     an rgb index, using
    ///        rgbindex = rtab[rval] | gtab[gval] | btab[bval]
    ///     where, e.g., if sigbits = 3, the index is a 9 bit integer:
    ///        r7 r6 r5 g7 g6 g5 b7 b6 b5
    /// </remarks>
    public static void MakeRGBIndexTables(out uint[] prtab, out uint[] pgtab, out uint[] pbtab,
                                          int sigbits)
    {
        const string func = "makeRGBIndexTables";
        if (sigbits < 2 || sigbits > 6)
            throw Lept.Arg(func, "sigbits not in [2 ... 6]");

        uint[] rtab = new uint[256];
        uint[] gtab = new uint[256];
        uint[] btab = new uint[256];
        prtab = rtab;
        pgtab = gtab;
        pbtab = btab;
        switch (sigbits) {
        case 2:
            for (int i = 0; i < 256; i++) {
                rtab[i] = (uint)((i & 0xc0) >> 2);
                gtab[i] = (uint)((i & 0xc0) >> 4);
                btab[i] = (uint)((i & 0xc0) >> 6);
            }
            break;
        case 3:
            for (int i = 0; i < 256; i++) {
                rtab[i] = (uint)((i & 0xe0) << 1);
                gtab[i] = (uint)((i & 0xe0) >> 2);
                btab[i] = (uint)((i & 0xe0) >> 5);
            }
            break;
        case 4:
            for (int i = 0; i < 256; i++) {
                rtab[i] = (uint)((i & 0xf0) << 4);
                gtab[i] = (uint)(i & 0xf0);
                btab[i] = (uint)((i & 0xf0) >> 4);
            }
            break;
        case 5:
            for (int i = 0; i < 256; i++) {
                rtab[i] = (uint)((i & 0xf8) << 7);
                gtab[i] = (uint)((i & 0xf8) << 2);
                btab[i] = (uint)((i & 0xf8) >> 3);
            }
            break;
        case 6:
            for (int i = 0; i < 256; i++) {
                rtab[i] = (uint)((i & 0xfc) << 10);
                gtab[i] = (uint)((i & 0xfc) << 4);
                btab[i] = (uint)((i & 0xfc) >> 2);
            }
            break;
        }
    }

    // leptonica: getRGBFromIndex (colorcontent.c)
    /// <summary>Gets the rgb values at the center of the cube with the given index.</summary>
    /// <param name="index">rgbindex</param>
    /// <param name="sigbits">2-6, significant bits retained in the quantizer
    ///                       for each component of the input image</param>
    /// <param name="prval">red value</param>
    /// <param name="pgval">green value</param>
    /// <param name="pbval">blue value</param>
    /// <remarks>
    /// (1) The index is expressed in bits, based on the the
    ///     sigbits of the r, g and b components, as
    ///        r7 r6 ... g7 g6 ... b7 b6 ...
    /// (2) The computed rgb values are in the center of the quantized cube.
    ///     The extra bit that is OR'd accomplishes this.
    /// </remarks>
    public static void GetRGBFromIndex(uint index, int sigbits,
                                       out int prval, out int pgval, out int pbval)
    {
        const string func = "getRGBFromIndex";
        prval = pgval = pbval = 0;
        if (sigbits < 2 || sigbits > 6)
            throw Lept.Arg(func, "sigbits not in [2 ... 6]");

        switch (sigbits) {
        case 2:
            prval = (int)(((index << 2) & 0xc0) | 0x20);
            pgval = (int)(((index << 4) & 0xc0) | 0x20);
            pbval = (int)(((index << 6) & 0xc0) | 0x20);
            break;
        case 3:
            prval = (int)(((index >> 1) & 0xe0) | 0x10);
            pgval = (int)(((index << 2) & 0xe0) | 0x10);
            pbval = (int)(((index << 5) & 0xe0) | 0x10);
            break;
        case 4:
            prval = (int)(((index >> 4) & 0xf0) | 0x08);
            pgval = (int)((index & 0xf0) | 0x08);
            pbval = (int)(((index << 4) & 0xf0) | 0x08);
            break;
        case 5:
            prval = (int)(((index >> 7) & 0xf8) | 0x04);
            pgval = (int)(((index >> 2) & 0xf8) | 0x04);
            pbval = (int)(((index << 3) & 0xf8) | 0x04);
            break;
        case 6:
            prval = (int)(((index >> 10) & 0xfc) | 0x02);
            pgval = (int)(((index >> 4) & 0xfc) | 0x02);
            pbval = (int)(((index << 2) & 0xfc) | 0x02);
            break;
        }
    }
}

public sealed partial class Pix
{
    /* ----------------------------------------------------------------------- *
     *      Build an image of the color content, on a per-pixel basis,         *
     *      as a measure of the amount of divergence of each color             *
     *      component (R,G,B) from gray.                                       *
     * ----------------------------------------------------------------------- */
    // leptonica: pixColorContent (colorcontent.c)
    /// <summary>Builds images of the color content of each component.</summary>
    /// <param name="rref">reference value for red component</param>
    /// <param name="gref">reference value for green component</param>
    /// <param name="bref">reference value for blue component</param>
    /// <param name="mingray">min gray value for which color is measured</param>
    /// <param name="ppixr">8 bpp red 'content'</param>
    /// <param name="ppixg">8 bpp green 'content'</param>
    /// <param name="ppixb">8 bpp blue 'content'</param>
    /// <remarks>
    /// (1) This returns the color content in each component, which is
    ///     a measure of the deviation from gray, and is defined
    ///     as the difference between the component and the average of
    ///     the other two components.  See the discussion at the
    ///     top of this file.
    /// (2) The three numbers (rref, gref and bref) can be thought
    ///     of in two ways:
    ///       (a) as the values in the image corresponding to white,
    ///           to compensate for an unbalanced color white point.
    ///       (b) the median or mean values of the background color of
    ///           a scan.
    ///     The gamma TRC transformation is used to modify all colors so that
    ///     these reference values become white.
    ///     These three numbers must either be all 0 or all non-zero.
    ///     To skip the TRC transform, set them all to 0.
    /// (3) If the maximum component after white point correction,
    ///     max(r,g,b), is less than mingray, all color components
    ///     for that pixel are set to zero.
    ///     Use mingray = 0 to turn off this filtering of dark pixels.
    /// (4) Therefore, use 0 for all four input parameters if the color
    ///     magnitude is to be calculated without either white balance
    ///     correction or dark filtering.
    /// </remarks>
    public void ColorContent(int rref, int gref, int bref, int mingray,
                             out Pix ppixr, out Pix ppixg, out Pix ppixb)
    {
        const string func = "pixColorContent";
        if (mingray < 0) mingray = 0;
        if (mingray > 255)
            throw Lept.Arg(func, "mingray > 255");

            /* Do the optional linear color map; this checks the ref vals */
        Pix pix1 = ColorShiftWhitePoint(rref, gref, bref);

        pix1.GetDimensions(out int w, out int h, out _);
        Pix pixr = Create(w, h, 8);
        uint[] datar = pixr.Data;
        int wplr = pixr.Wpl;
        ppixr = pixr;
        Pix pixg = Create(w, h, 8);
        uint[] datag = pixg.Data;
        int wplg = pixg.Wpl;
        ppixg = pixg;
        Pix pixb = Create(w, h, 8);
        uint[] datab = pixb.Data;
        int wplb = pixb.Wpl;
        ppixb = pixb;

        uint[] data1 = pix1.Data;
        int wpl1 = pix1.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl1, wpl1);
            Span<uint> liner = datar.AsSpan(i * wplr, wplr);
            Span<uint> lineg = datag.AsSpan(i * wplg, wplg);
            Span<uint> lineb = datab.AsSpan(i * wplb, wplb);
            for (int j = 0; j < w; j++) {
                uint pixel = line1[j];
                ExtractRGBValues(pixel, out int rval, out int gval, out int bval);
                if (mingray > 0) {  /* dark pixels have no color value */
                    int maxval = Math.Max(rval, gval);
                    maxval = Math.Max(maxval, bval);
                    if (maxval < mingray)
                        continue;  /* colorval = 0 for each component */
                }
                int rgdiff = Math.Abs(rval - gval);
                int rbdiff = Math.Abs(rval - bval);
                int gbdiff = Math.Abs(gval - bval);
                int colorval = (rgdiff + rbdiff) / 2;
                ArrayAccess.SetDataByte(liner, j, colorval);
                colorval = (rgdiff + gbdiff) / 2;
                ArrayAccess.SetDataByte(lineg, j, colorval);
                colorval = (rbdiff + gbdiff) / 2;
                ArrayAccess.SetDataByte(lineb, j, colorval);
            }
        }
    }

    /* ----------------------------------------------------------------------- *
     *      Find the 'amount' of color in an image, on a per-pixel basis,      *
     *      as a measure of the difference of the pixel color from gray.       *
     * ----------------------------------------------------------------------- */
    // leptonica: pixColorMagnitude (colorcontent.c)
    /// <summary>Finds the amount of color in each pixel, as a measure of the difference from gray.</summary>
    /// <param name="rref">reference value for red component</param>
    /// <param name="gref">reference value for green component</param>
    /// <param name="bref">reference value for blue component</param>
    /// <param name="type">chooses the method for calculating the color magnitude:
    ///                    L_INTERMED_DIFF, L_AVE_MAX_DIFF_2, L_MAX_DIFF</param>
    /// <returns>pixd 8 bpp, amount of color in each source pixel</returns>
    /// <remarks>
    /// (1) For an RGB image, a gray pixel is one where all three components
    ///     are equal.  We define the amount of color in an RGB pixel as
    ///     a function depending on the absolute value of the differences
    ///     between the three color components.  Consider the two largest
    ///     of these differences.  The pixel component in common to these
    ///     two differences is the color farthest from the other two.
    ///     The color magnitude in an RGB pixel can be taken as one
    ///     of these three definitions:
    ///       (a) The minimum value of these two differences.  This is
    ///           the intermediate value of the three distances between
    ///           component values.
    ///       (b) The average of these two differences.  This is the
    ///           average distance from the two components that are
    ///           nearest to each other to the third component.
    ///       (c) The maximum difference between component values.
    /// (2) As an example, suppose that R and G are the closest in
    ///     magnitude.  Then the color is determined as either:
    ///       (a) The minimum distance of B from these two:
    ///              min(|B - R|, |B - G|).
    ///       (b) The average distance of B from these two:
    ///              (|B - R| + |B - G|) / 2
    ///       (c) The maximum distance of B from these two:
    ///              max(|B - R|, |B - G|)
    /// (3) This example can be visualized graphically.  Put the R,G and B
    ///     component values on a line; e.g.,
    ///           G...R...........B
    ///       (a) B - R
    ///       (b) B - (R + G) / 2
    ///       (c) B - G
    /// (4) The three methods for choosing the color magnitude from
    ///     the components are selected with these flags:
    ///       (a) L_INTERMED_DIFF
    ///       (b) L_AVE_MAX_DIFF_2
    ///       (c) L_MAX_DIFF
    /// (5) The three numbers (rref, gref and bref) can be thought
    ///     of in two ways:
    ///       (a) as the values in the image corresponding to white,
    ///           to compensate for an unbalanced color white point.
    ///       (b) the median or mean values of the background color of
    ///           a scan.
    ///     The gamma TRC transformation is used to modify all colors so that
    ///     these reference values become white.
    ///     These three numbers must either be all 0 or all non-zero.
    ///     To skip the TRC transform, set them all to 0.
    /// </remarks>
    public Pix ColorMagnitude(int rref, int gref, int bref, int type)
    {
        const string func = "pixColorMagnitude";
        if (type != L.L_INTERMED_DIFF && type != L.L_AVE_MAX_DIFF_2 &&
            type != L.L_MAX_DIFF)
            throw Lept.Arg(func, "invalid type");

            /* Do the optional linear color map; this checks the ref vals */
        Pix pix1 = ColorShiftWhitePoint(rref, gref, bref);

        pix1.GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 8);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        uint[] data1 = pix1.Data;
        int wpl1 = pix1.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl1, wpl1);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                uint pixel = line1[j];
                ExtractRGBValues(pixel, out int rval, out int gval, out int bval);
                int colorval;
                if (type == L.L_INTERMED_DIFF) {
                    int rgdist = Math.Abs(rval - gval);
                    int rbdist = Math.Abs(rval - bval);
                    int gbdist = Math.Abs(gval - bval);
                    int maxdist = Math.Max(rgdist, rbdist);
                    if (gbdist >= maxdist) {
                        colorval = maxdist;
                    } else {  /* gbdist is smallest or intermediate */
                        int mindist = Math.Min(rgdist, rbdist);
                        colorval = Math.Max(mindist, gbdist);
                    }
                } else if (type == L.L_AVE_MAX_DIFF_2) {
                    int rdist = ((gval + bval) / 2 - rval);
                    rdist = Math.Abs(rdist);
                    int gdist = ((rval + bval) / 2 - gval);
                    gdist = Math.Abs(gdist);
                    int bdist = ((rval + gval) / 2 - bval);
                    bdist = Math.Abs(bdist);
                    colorval = Math.Max(rdist, gdist);
                    colorval = Math.Max(colorval, bdist);
                } else {  /* type == L_MAX_DIFF */
                    int minval = Math.Min(rval, gval);
                    minval = Math.Min(minval, bval);
                    int maxval = Math.Max(rval, gval);
                    maxval = Math.Max(maxval, bval);
                    colorval = maxval - minval;
                }
                ArrayAccess.SetDataByte(lined, j, colorval);
            }
        }

        return pixd;
    }

    /* ----------------------------------------------------------------------- *
     *   Find the fraction of pixels with "color" that are not close to black  *
     * ----------------------------------------------------------------------- */
    // leptonica: pixColorFraction (colorcontent.c)
    /// <summary>Finds the fraction of pixels with color that are not close to black or white.</summary>
    /// <param name="darkthresh">threshold near black; if the largest (lightest)
    ///                          component is below this, the pixel is not
    ///                          considered in the statistics; typ. 20</param>
    /// <param name="lightthresh">threshold near white; if the smallest (darkest)
    ///                           component is above this, the pixel is not
    ///                           considered in the statistics; typ. 244</param>
    /// <param name="diffthresh">thresh for the maximum difference between
    ///                          component values; below this the pixel is not
    ///                          considered to have sufficient color; typ. 60.</param>
    /// <param name="factor">subsampling factor</param>
    /// <param name="ppixfract">fraction of pixels in intermediate
    ///                         brightness range that were considered
    ///                         for color content</param>
    /// <param name="pcolorfract">fraction of pixels that meet the
    ///                           criterion for sufficient color; 0.0 on error</param>
    /// <remarks>
    /// (1) This function is asking the question: to what extent does the
    ///     image appear to have color?   The amount of color a pixel
    ///     appears to have depends on both the deviation of the
    ///     individual components from their average and on the average
    ///     intensity itself.  For example, the color will be much more
    ///     obvious with a small deviation from white than the same
    ///     deviation from black.
    /// (2) Any pixel that meets these three tests is considered a
    ///     colorful pixel:
    ///       (a) the lightest component must equal or exceed darkthresh
    ///       (b) the darkest component must not exceed lightthresh
    ///       (c) the max difference between components must equal or
    ///           exceed diffthresh.
    /// (3) The dark pixels are removed from consideration because
    ///     they don't appear to have color.
    /// (4) The very lightest pixels are removed because if an image
    ///     has a lot of "white", the color fraction will be artificially
    ///     low, even if all the other pixels are colorful.
    /// (5) If pixfract is very small, there are few pixels that are neither
    ///     black nor white.  If colorfract is very small, the pixels
    ///     that are neither black nor white have very little color
    ///     content.  The product 'pixfract * colorfract' gives the
    ///     fraction of pixels with significant color content.
    /// (6) One use of this function is as a preprocessing step for median
    ///     cut quantization (colorquant2.c), which does a very poor job
    ///     splitting the color space into rectangular volume elements when
    ///     all the pixels are near the diagonal of the color cube.  For
    ///     octree quantization of an image with only gray values, the
    ///     2^(level) octcubes on the diagonal are the only ones
    ///     that can be occupied.
    /// </remarks>
    public void ColorFraction(int darkthresh, int lightthresh, int diffthresh, int factor,
                              out float ppixfract, out float pcolorfract)
    {
        const string func = "pixColorFraction";
        ppixfract = 0.0f;
        pcolorfract = 0.0f;
        if (Depth != 32)
            throw Lept.Arg(func, "pixs not defined or not 32 bpp");

        GetDimensions(out int w, out int h, out _);
        uint[] data = Data;
        int wpl = Wpl;
        int npix = 0, ncolor = 0, total = 0;
        for (int i = 0; i < h; i += factor) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w; j += factor) {
                total++;
                uint pixel = line[j];
                ExtractRGBValues(pixel, out int rval, out int gval, out int bval);
                int minval = Math.Min(rval, gval);
                minval = Math.Min(minval, bval);
                if (minval > lightthresh)  /* near white */
                    continue;
                int maxval = Math.Max(rval, gval);
                maxval = Math.Max(maxval, bval);
                if (maxval < darkthresh)  /* near black */
                    continue;

                npix++;
                if (maxval - minval >= diffthresh)
                    ncolor++;
            }
        }

        if (npix == 0) {
            Log.Warning(func, "No pixels found for consideration\n");
            return;
        }
        ppixfract = (float)npix / (float)total;
        pcolorfract = (float)ncolor / (float)npix;
    }

    /* ----------------------------------------------------------------------- *
     *      Do a linear TRC to map colors so that the three input reference    *
     *      values go to white.  These three numbers are typically the median  *
     *      or average background values.                                      *
     * ----------------------------------------------------------------------- */
    // leptonica: pixColorShiftWhitePoint (colorcontent.c)
    /// <summary>Maps colors linearly so that the three reference values go to white.</summary>
    /// <param name="rref">reference value for red component</param>
    /// <param name="gref">reference value for green component</param>
    /// <param name="bref">reference value for blue component</param>
    /// <returns>pix2 32 bpp</returns>
    /// <remarks>
    /// (1) This returns a pix where the colors are linearly mapped to
    ///     so that the components go to 255 at the input reference values.
    /// (2) These three numbers (rref, gref and bref) can be thought
    ///     of in two ways:
    ///       (a) as the values in the image corresponding to white,
    ///           to compensate for an unbalanced color white point.
    ///       (b) the median or mean values of the background color of
    ///           an image.
    ///     A linear (gamma = 1) TRC transformation is used.
    /// (3) Any existing colormap is removed and a 32 bpp rgb pix is returned.
    /// (4) No transformation is applied if any of the three numbers are &lt;= 0.
    ///     If any are &lt; 0, or if some but not all are 0, a warning is given.
    /// </remarks>
    public Pix ColorShiftWhitePoint(int rref, int gref, int bref)
    {
        const string func = "pixColorShiftWhitePoint";
        PixColormap? cmap = Colormap;
        if (cmap == null && Depth != 32)
            throw Lept.Arg(func, "pixs neither cmapped nor 32 bpp");
        Pix pix1;
        if (cmap != null)
            pix1 = RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        else
            pix1 = this;

        if (rref == 0 && gref == 0 && bref == 0)  /* all 0; no transform requested */
            return pix1;

            /* Some ref values are < 0, or some (but not all) are 0 */
        if ((rref < 0 || gref < 0 || bref < 0) || (rref * gref * bref == 0)) {
            Log.Warning(func, "invalid set of ref values\n");
            return pix1;
        }

            /* All white point ref values > 0; do transformation */
        pix1.GetDimensions(out int w, out int h, out _);
        Pix pix2 = Create(w, h, 32);
        uint[] data1 = pix1.Data;
        int wpl1 = pix1.Wpl;
        uint[] data2 = pix2.Data;
        int wpl2 = pix2.Wpl;
        Numa nar = Numa.GammaTRC(1.0f, 0, rref);
        int[] rtab = nar.GetIArray();
        Numa nag = Numa.GammaTRC(1.0f, 0, gref);
        int[] gtab = nag.GetIArray();
        Numa nab = Numa.GammaTRC(1.0f, 0, bref);
        int[] btab = nab.GetIArray();
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> line1 = data1.AsSpan(i * wpl1, wpl1);
            Span<uint> line2 = data2.AsSpan(i * wpl2, wpl2);
            for (int j = 0; j < w; j++) {
                uint pixel = line1[j];
                ExtractRGBValues(pixel, out int rval, out int gval, out int bval);
                rval = rtab[rval];
                gval = gtab[gval];
                bval = btab[bval];
                ComposeRGBPixel(rval, gval, bval, out line2[j]);
            }
        }
        return pix2;
    }

    /* ----------------------------------------------------------------------- *
     *      Generate a mask over pixels that have sufficient color and         *
     *      are not too close to gray pixels.                                  *
     * ----------------------------------------------------------------------- */
    // leptonica: pixMaskOverColorPixels (colorcontent.c)
    /// <summary>Generates a mask over pixels that have sufficient color and are not too close to gray.</summary>
    /// <param name="threshdiff">threshold for minimum of the max difference
    ///                          between components</param>
    /// <param name="mindist">min allowed distance from nearest non-color pixel</param>
    /// <returns>pixd 1 bpp, mask over color pixels</returns>
    /// <remarks>
    /// (1) The generated mask identifies each pixel as either color or
    ///     non-color.  For a pixel to be color, it must satisfy two
    ///     constraints:
    ///       (a) The max difference between the r,g and b components must
    ///           equal or exceed a threshold threshdiff.
    ///       (b) It must be at least mindist (in an 8-connected way)
    ///           from the nearest non-color pixel.
    /// (2) The distance constraint (b) is only applied if mindist > 1.
    ///     For example, if mindist == 2, the color pixels identified
    ///     by (a) are eroded by a 3x3 Sel.  In general, the Sel size
    ///     for erosion is 2 * (mindist - 1) + 1.
    ///     Why have this constraint?  In scanned images that are
    ///     essentially gray, color artifacts are typically introduced
    ///     in transition regions near sharp edges that go from dark
    ///     to light, so this allows these transition regions to be removed.
    /// </remarks>
    public Pix MaskOverColorPixels(int threshdiff, int mindist)
    {
        const string func = "pixMaskOverColorPixels";
        GetDimensions(out int w, out int h, out int d);

        PixColormap? cmap = Colormap;
        if (cmap == null && d != 32)
            throw Lept.Arg(func, "pixs not cmapped or 32 bpp");
        Pix pixc;
        if (cmap != null)
            pixc = RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        else
            pixc = this;
        if (pixc.Depth != 32)
            throw Lept.Fail(func, "rgb pix not made");

        Pix pixd = Create(w, h, 1);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        uint[] datas = pixc.Data;
        int wpls = pixc.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(lines[j], out int rval, out int gval, out int bval);
                int minval = Math.Min(rval, gval);
                minval = Math.Min(minval, bval);
                int maxval = Math.Max(rval, gval);
                maxval = Math.Max(maxval, bval);
                if (maxval - minval >= threshdiff)
                    ArrayAccess.SetDataBit(lined, j);
            }
        }

        if (mindist > 1) {
            int size = 2 * (mindist - 1) + 1;
            ErodeBrick(pixd, pixd, size, size);
        }

        return pixd;
    }

    /* ----------------------------------------------------------------------- *
     *          Generate a mask over dark pixels with little color             *
     * ----------------------------------------------------------------------- */
    // leptonica: pixMaskOverGrayPixels (colorcontent.c)
    /// <summary>Generates a mask over rgb pixels that are gray (low saturation) and not too bright.</summary>
    /// <param name="maxlimit">only consider pixels with max component &lt;= maxlimit</param>
    /// <param name="satlimit">only consider pixels with saturation &lt;= satlimit</param>
    /// <returns>pixd (1 bpp)</returns>
    /// <remarks>
    /// (1) This generates a mask over rgb pixels that are gray (i.e.,
    ///     have low saturation) and are not too bright.  For example, if
    ///     we know that the gray pixels in pixs have saturation
    ///     (max - min) less than 10, and brightness (max) less than 200,
    ///        pixMaskOverGrayPixels(pixs, 220, 10)
    ///     will generate a mask over the gray pixels.  Other pixels that
    ///     are not too dark and have a relatively large saturation will
    ///     be little affected.
    /// (2) The algorithm is related to pixDarkenGray().
    /// </remarks>
    public Pix MaskOverGrayPixels(int maxlimit, int satlimit)
    {
        const string func = "pixMaskOverGrayPixels";
        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        if (maxlimit < 0 || maxlimit > 255)
            throw Lept.Arg(func, "invalid maxlimit");
        if (satlimit < 1)
            throw Lept.Arg(func, "invalid satlimit");

        GetDimensions(out int w, out int h, out _);
        uint[] datas = Data;
        int wpls = Wpl;
        Pix pixd = Create(w, h, 1);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(lines[j], out int rval, out int gval, out int bval);
                int minrg = Math.Min(rval, gval);
                int min = Math.Min(minrg, bval);
                int maxrg = Math.Max(rval, gval);
                int max = Math.Max(maxrg, bval);
                int sat = max - min;
                if (max <= maxlimit && sat <= satlimit)
                    ArrayAccess.SetDataBit(lined, j);
            }
        }
        return pixd;
    }

    /* ----------------------------------------------------------------------- *
     *      Generate a mask over pixels that have RGB color components         *
     *      within the prescribed range (a cube in RGB color space)            *
     * ----------------------------------------------------------------------- */
    // leptonica: pixMaskOverColorRange (colorcontent.c)
    /// <summary>Generates a mask over pixels with rgb components within the given ranges.</summary>
    /// <param name="rmin">min allowed value for red component</param>
    /// <param name="rmax">max allowed value for red component</param>
    /// <param name="gmin">min allowed value for green component</param>
    /// <param name="gmax">max allowed value for green component</param>
    /// <param name="bmin">min allowed value for blue component</param>
    /// <param name="bmax">max allowed value for blue component</param>
    /// <returns>pixd 1 bpp, mask over color pixels</returns>
    public Pix MaskOverColorRange(int rmin, int rmax, int gmin, int gmax, int bmin, int bmax)
    {
        const string func = "pixMaskOverColorRange";
        GetDimensions(out int w, out int h, out int d);

        PixColormap? cmap = Colormap;
        if (cmap == null && d != 32)
            throw Lept.Arg(func, "pixs not cmapped or 32 bpp");
        Pix pixc;
        if (cmap != null)
            pixc = RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        else
            pixc = this;

        Pix pixd = Create(w, h, 1);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        uint[] datas = pixc.Data;
        int wpls = pixc.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(lines[j], out int rval, out int gval, out int bval);
                if (rval < rmin || rval > rmax) continue;
                if (gval < gmin || gval > gmax) continue;
                if (bval < bmin || bval > bmax) continue;
                ArrayAccess.SetDataBit(lined, j);
            }
        }

        return pixd;
    }

    /* ----------------------------------------------------------------------- *
     *     Determine if there are significant color regions in a page image    *
     * ----------------------------------------------------------------------- */
    // leptonica: pixFindColorRegions (colorcontent.c)
    /// <summary>Determines if there are significant color regions that are not background in a page image.</summary>
    /// <param name="pixm">[optional] 1 bpp mask image</param>
    /// <param name="factor">subsample factor; integer >= 1</param>
    /// <param name="lightthresh">threshold for component average in lightest
    ///                           of 10 buckets; typ. 210; -1 for default</param>
    /// <param name="darkthresh">threshold to eliminate dark pixels (e.g., text)
    ///                          from consideration; typ. 70; -1 for default.</param>
    /// <param name="mindiff">minimum difference (b - r) and (g - r), used to
    ///                       find blue or green pixels; typ. 10; -1 for default</param>
    /// <param name="colordiff">minimum difference in (max - min) component to
    ///                         qualify as a color pixel; typ. 90; -1 for default</param>
    /// <param name="edgefract">fraction of image half-width and half-height
    ///                         for which color pixels are ignored; typ. 0.05.</param>
    /// <param name="pcolorfract">fraction of 'color' pixels found</param>
    /// <param name="pcolormask1">mask over background color, if any</param>
    /// <param name="pcolormask2">filtered mask over background color</param>
    /// <param name="pixadb">[optional] debug intermediate results</param>
    /// <remarks>
    /// (1) This function tries to determine if there is a significant
    ///     color or darker region on a scanned page image, where part
    ///     of the image is background that is either white or reddish.
    ///     This also allows extraction of regions of colored pixels that
    ///     have a smaller red component than blue or green components.
    /// (2) If pixm exists, pixels under its fg are combined with
    ///     dark pixels to make a mask of pixels not to be considered
    ///     as color candidates.
    /// (3) There are four thresholds.
    ///     * lightthresh: compute the average value of each rgb pixel,
    ///       and make 10 buckets by value.  If the lightest bucket gray
    ///       value is below lightthresh, the image is not considered
    ///       to have a light bg, and this returns 0.0 for colorfract.
    ///     * darkthresh: ignore pixels darker than this (typ. fg text).
    ///       We make a 1 bpp mask of these pixels, and then dilate it to
    ///       remove all vestiges of fg from their vicinity.
    ///     * mindiff: consider pixels with either (b - r) or (g - r)
    ///       being at least this value, as having color.
    ///     * colordiff: consider pixels where the (max - min) difference
    ///       of the pixel components exceeds this value, as having color.
    /// (4) All components of color pixels that are touching the image
    ///     border are removed.  Additionally, all pixels within some
    ///     normalized distance edgefract from the image border can
    ///     be removed.  This insures that dark pixels near the edge
    ///     of the image are not included.
    /// (5) This returns in pcolorfract the fraction of pixels that have
    ///     color and are not in the set consisting of an OR between
    ///     pixm and the dilated dark pixel mask.
    /// (6) No masks are returned unless light color pixels are found.
    ///     If colorfract > 0.0 and pcolormask1 is defined, this returns
    ///     a 1 bpp mask with fg pixels over the color background.
    ///     This mask may have some holes in it.
    /// (7) If colorfract > 0.0 and pcolormask2 is defined, this returns
    ///     a version of colormask1 where small holes have been filled.
    /// (8) To generate a boxa of rectangular regions from the overlap
    ///     of components in the filtered mask:
    ///           boxa1 = pixConnCompBB(colormask2, 8);
    ///           boxa2 = boxaCombineOverlaps(boxa1, NULL);
    ///     This is done here in debug mode.
    /// </remarks>
    public void FindColorRegions(Pix? pixm, int factor, int lightthresh, int darkthresh,
                                 int mindiff, int colordiff, float edgefract,
                                 out float pcolorfract, out Pix? pcolormask1,
                                 out Pix? pcolormask2, Pixa? pixadb)
    {
        const string func = "pixFindColorRegions";
        pcolormask1 = null;
        pcolormask2 = null;
        pcolorfract = 0.0f;
        if (Depth != 32)
            throw Lept.Arg(func, "pixs not defined or not 32 bpp");
        if (factor < 1) factor = 1;
        if (lightthresh < 0) lightthresh = 210;  /* defaults */
        if (darkthresh < 0) darkthresh = 70;
        if (mindiff < 0) mindiff = 10;
        if (colordiff < 0) colordiff = 90;
        if (edgefract < 0.0 || edgefract > 1.0) edgefract = 0.05f;

            /* Check if pixm covers most of the image.  If so, just return. */
        if (pixm != null) {
            float ratio = pixm.ForegroundFraction();
            if (ratio > 0.7) {
                if (pixadb != null)
                    Log.Info(func, CFormat.Sprintf("pixm has big fg: %f5.2\n", ratio));
                return;
            }
        }

            /* Get the light background color.  Use the average component value
             * and select the lightest of 10 buckets.  Require that it is
             * reddish and, using lightthresh, not too dark. */
        uint[]? carray = GetRankColorArray(10, L.L_SELECT_AVERAGE, factor, null, 0);
        if (carray == null)
            throw Lept.Fail(func, "rank color array not made");
        ExtractRGBValues(carray[9], out int rval, out int gval, out int bval);
        if (pixadb != null)
            Log.Info(func, CFormat.Sprintf(
                     "lightest background color: (r,g,b) = (%d,%d,%d)\n", rval, gval, bval));
        bool proceed = true;
        if ((rval < bval - 2) || (rval < gval - 2)) {
            if (pixadb != null) Log.Info(func, "background not reddish\n");
            proceed = false;
        }
        int aveval = (rval + gval + bval) / 3;
        if (aveval < lightthresh) {
            if (pixadb != null) Log.Info(func, "background too dark\n");
            proceed = false;
        }
        if (pixadb != null) {
            Pix pixc = DisplayColorArray(carray, 10, 120, 3, 6);
            pixadb.AddPix(pixc, L.L_INSERT);
        }
        if (!proceed) return;

            /* Make a mask pixm1 over the dark pixels in the image:
             * convert to gray using the average of the components;
             * threshold using darkthresh; do a small dilation;
             * combine with pixm. */
        Pix pix1 = ConvertRGBToGray(0.33f, 0.34f, 0.33f);
        pixadb?.AddPix(pix1, L.L_COPY);
        Pix pixm1 = pix1.ThresholdToBinary(darkthresh);
        DilateBrick(pixm1, pixm1, 7, 7);
        pixadb?.AddPix(pixm1, L.L_COPY);
        if (pixm != null) {
            Or(pixm1, pixm1, pixm);
            pixadb?.AddPix(pixm1, L.L_COPY);
        }

            /* Make masks over pixels that are bluish, or greenish, or
               have a very large color saturation (max - min) value. */
        Pix pixm2 = ConvertRGBToBinaryArb(-1.0f, 0.0f, 1.0f, mindiff,
                                          L.L_SELECT_IF_GTE);  /* b - r */
        pixadb?.AddPix(pixm2, L.L_COPY);
        pix1 = ConvertRGBToBinaryArb(-1.0f, 1.0f, 0.0f, mindiff,
                                     L.L_SELECT_IF_GTE);  /* g - r */
        pixadb?.AddPix(pix1, L.L_COPY);
        Or(pixm2, pixm2, pix1);
        pix1 = ConvertRGBToGrayMinMax(L.L_CHOOSE_MAXDIFF);
        Pix pix2 = pix1.ThresholdToBinary(colordiff);
        Invert(pix2, pix2);
        pixadb?.AddPix(pix2, L.L_COPY);
        Or(pixm2, pixm2, pix2);
        pixadb?.AddPix(pixm2, L.L_COPY);

            /* Subtract the dark pixels represented by pixm1.
             * pixm2 now holds all the color pixels of interest  */
        Subtract(pixm2, pixm2, pixm1);
        pixadb?.AddPix(pixm2, L.L_COPY);

            /* But we're not quite finished.  Remove pixels from any component
             * that is touching the image border.  False color pixels can
             * sometimes be found there if the image is much darker near
             * the border, due to oxidation or reduced illumination.  Also
             * remove any pixels within the normalized fraction %distfract
             * of the image border. */
        Pix pixm3 = pixm2.RemoveBorderConnComps(8);
        pixm3.GetDimensions(out int w, out int h, out _);
        if (edgefract > 0.0) {
            pix2 = MakeSymmetricMask(w, h, edgefract, edgefract, L.L_USE_INNER);
            And(pixm3, pixm3, pix2);
        }
        pixadb?.AddPix(pixm3, L.L_COPY);

            /* Get the fraction of light color pixels */
        int count = pixm3.CountPixels(null);
        pcolorfract = (float)count / ((float)(w) * h);
        if (pixadb != null) {
            if (count == 0)
                Log.Info(func, "no light color pixels found\n");
            else
                Log.Info(func, CFormat.Sprintf("fraction of light color pixels = %5.3f\n",
                                               pcolorfract));
        }

            /* Debug: extract the color pixels from pixs */
        if (pixadb != null && count > 0) {
                /* Use pixm3 to extract the color pixels */
            Pix pix3 = CreateTemplate(this);
            pix3.SetAll();
            pix3.CombineMasked(this, pixm3);
            pixadb.AddPix(pix3, L.L_INSERT);

                /* Use additional filtering to extract the color pixels */
            pix3 = CloseSafeBrick(null, pixm3, 15, 15);
            pixadb.AddPix(pix3, L.L_INSERT);
            Pix pix5 = CreateTemplate(this);
            pix5.SetAll();
            pix5.CombineMasked(this, pix3);
            pixadb.AddPix(pix5, L.L_INSERT);

                /* Get the combined bounding boxes of the mask components
                 * in pix3, and extract those pixels from pixs. */
            Boxa boxa1 = pix3.ConnCompBB(8);
            Boxa boxa2 = boxa1.CombineOverlaps(null);
            Pix pix4 = CreateTemplate(pix3);
            MaskBoxa(pix4, pix4, boxa2, L.L_SET_PIXELS);
            pixadb.AddPix(pix4, L.L_INSERT);
            pix5 = CreateTemplate(this);
            pix5.SetAll();
            pix5.CombineMasked(this, pix4);
            pixadb.AddPix(pix5, L.L_INSERT);
        }
        pixadb?.AddPix(this, L.L_COPY);

            /* Optional colormask returns */
        if (count > 0)
            pcolormask2 = CloseSafeBrick(null, pixm3, 15, 15);
        if (count > 0)
            pcolormask1 = pixm3;
    }

    /* ----------------------------------------------------------------------- *
     *      Find the number of perceptually significant gray intensities       *
     *      in a grayscale image.                                              *
     * ----------------------------------------------------------------------- */
    // leptonica: pixNumSignificantGrayColors (colorcontent.c)
    /// <summary>Finds the number of perceptually significant gray intensities in a grayscale image.</summary>
    /// <param name="darkthresh">dark threshold for minimum intensity to be
    ///                          considered; typ. 20</param>
    /// <param name="lightthresh">threshold near white, for maximum intensity
    ///                           to be considered; typ. 236</param>
    /// <param name="minfract">minimum fraction of all pixels to include a level
    ///                        as significant; typ. 0.0001; should be &lt; 0.001</param>
    /// <param name="factor">subsample factor; integer >= 1</param>
    /// <returns>number of significant colors</returns>
    /// <remarks>
    /// (1) This function is asking the question: how many perceptually
    ///     significant gray color levels is in this pix?
    ///     A color level must meet 3 criteria to be significant:
    ///       ~ it can't be too close to black
    ///       ~ it can't be too close to white
    ///       ~ it must have at least some minimum fractional population
    /// (2) Use -1 for default values for darkthresh, lightthresh and minfract.
    /// (3) Choose default of darkthresh = 20, because variations in very
    ///     dark pixels are not visually significant.
    /// (4) Choose default of lightthresh = 236, because document images
    ///     that have been jpeg'd typically have near-white pixels in the
    ///     8x8 jpeg blocks, and these should not be counted.  It is desirable
    ///     to obtain a clean image by quantizing this noise away.
    /// </remarks>
    public int NumSignificantGrayColors(int darkthresh, int lightthresh, float minfract,
                                        int factor)
    {
        const string func = "pixNumSignificantGrayColors";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not defined or not 8 bpp");
        if (darkthresh < 0) darkthresh = 20;  /* defaults */
        if (lightthresh < 0) lightthresh = 236;
        if (minfract < 0.0)
            minfract = 0.0001f;
        if (minfract > 1.0)
            throw Lept.Arg(func, "minfract > 1.0");
        if (minfract >= 0.001)
            Log.Warning(func, "minfract too big; likely to underestimate ncolors\n");
        if (lightthresh > 255 || darkthresh >= lightthresh)
            throw Lept.Arg(func, "invalid thresholds");
        if (factor < 1) factor = 1;

        GetDimensions(out int w, out int h, out _);
        int mincount = (int)(minfract * w * h * factor * factor);
        Numa na = GetGrayHistogram(factor);
        int ncolors = 2;  /* add in black and white */
        for (int i = darkthresh; i <= lightthresh; i++) {
            int count = na.GetIValue(i);
            if (count >= mincount)
                ncolors++;
        }

        return ncolors;
    }

    /* ----------------------------------------------------------------------- *
     *   Identifies images where color quantization will cause posterization   *
     *   due to the existence of many colors in low-gradient regions.          *
     * ----------------------------------------------------------------------- */
    // leptonica: pixColorsForQuantization (colorcontent.c)
    /// <summary>Finds a measure of the number of colors in the low-gradient regions of an image.</summary>
    /// <param name="thresh">binary threshold on edge gradient; 0 for default</param>
    /// <param name="ncolors">the number of colors found</param>
    /// <param name="iscolor">true if significant color is found.
    ///                       If pixs is 8 bpp, and does not have
    ///                       a colormap with color entries, this is false</param>
    /// <param name="debug">1 to output masked image that is tested for colors;
    ///                     0 otherwise</param>
    /// <remarks>
    /// (1) This function finds a measure of the number of colors that are
    ///     found in low-gradient regions of an image.  By its
    ///     magnitude relative to some threshold (not specified in
    ///     this function), it gives a good indication of whether
    ///     quantization will generate posterization.   This number
    ///     is larger for images with regions of slowly varying
    ///     intensity (if 8 bpp) or color (if rgb). Such images, if
    ///     quantized, may require dithering to avoid posterization,
    ///     and lossless compression is then expected to be poor.
    /// (2) If pixs has a colormap, the number of colors returned is
    ///     the number in the colormap.
    /// (3) It is recommended that document images be reduced to a width
    ///     of 800 pixels before applying this function.  Then it can
    ///     be expected that color detection will be fairly accurate
    ///     and the number of colors will reflect both the content and
    ///     the type of compression to be used.  For less than 15 colors,
    ///     there is unlikely to be a halftone image, and lossless
    ///     quantization should give both a good visual result and
    ///     better compression.
    /// (4) When using the default threshold on the gradient (15),
    ///     images (both gray and rgb) where ncolors is greater than
    ///     about 15 will compress poorly with either lossless
    ///     compression or dithered quantization, and they may be
    ///     posterized with non-dithered quantization.
    /// (5) For grayscale images, or images without significant color,
    ///     this returns the number of significant gray levels in
    ///     the low-gradient regions.  The actual number of gray levels
    ///     can be large due to jpeg compression noise in the background.
    /// (6) Similarly, for color images, the actual number of different
    ///     (r,g,b) colors in the low-gradient regions (rather than the
    ///     number of occupied level 4 octcubes) can be quite large, e.g.,
    ///     due to jpeg compression noise, even for regions that appear
    ///     to be of a single color.  By quantizing to level 4 octcubes,
    ///     most of these superfluous colors are removed from the counting.
    /// (7) The image is tested for color.  If there is very little color,
    ///     it is thresholded to gray and the number of gray levels in
    ///     the low gradient regions is found.  If the image has color,
    ///     the number of occupied level 4 octcubes is found.
    /// (8) The number of colors in the low-gradient regions increases
    ///     monotonically with the threshold thresh on the edge gradient.
    /// (9) Background: grayscale and color quantization is often useful
    ///     to achieve highly compressed images with little visible
    ///     distortion.  However, gray or color washes (regions of
    ///     low gradient) can defeat this approach to high compression.
    ///     How can one determine if an image is expected to compress
    ///     well using gray or color quantization?  We use the fact that
    ///       * gray washes, when quantized with less than 50 intensities,
    ///         have posterization (visible boundaries between regions
    ///         of uniform 'color') and poor lossless compression
    ///       * color washes, when quantized with level 4 octcubes,
    ///         typically result in both posterization and the occupancy
    ///         of many level 4 octcubes.
    ///     Images can have colors either intrinsically or as jpeg
    ///     compression artifacts.  This function reduces but does not
    ///     completely eliminate measurement of jpeg quantization noise
    ///     in the white background of grayscale or color images.
    /// </remarks>
    public void ColorsForQuantization(int thresh, out int ncolors, out bool iscolor, int debug)
    {
        const string func = "pixColorsForQuantization";
        iscolor = false;
        ncolors = 0;
        PixColormap? cmap = Colormap;
        if (cmap != null) {
            ncolors = cmap.Count;
            iscolor = cmap.HasColor();
            return;
        }

        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 8 or 32 bpp");
        if (thresh <= 0)
            thresh = 15;

            /* First test if 32 bpp has any significant color; if not,
             * convert it to gray.  Colors whose average values are within
             * 20 of black or 8 of white are ignored because they're not
             * very 'colorful'.  If less than 2.5/10000 of the pixels have
             * significant color, consider the image to be gray. */
        int minside = Math.Min(w, h);
        Pix pixt;
        int factor;
        if (d == 8) {
            pixt = this;
        } else {  /* d == 32 */
            factor = Math.Max(1, minside / 400);
            ColorFraction(20, 248, 30, factor, out float pixfract, out float colorfract);
            if (pixfract * colorfract < 0.00025) {
                pixt = GetRGBComponent(L.COLOR_RED);
                d = 8;
            } else {  /* d == 32 */
                pixt = this;
                iscolor = true;
            }
        }

            /* If the smallest side is less than 1000, do not downscale.
             * If it is in [1000 ... 2000), downscale by 2x.  If it is >= 2000,
             * downscale by 4x.  Factors of 2 are chosen for speed.  The
             * actual resolution at which subsequent calculations take place
             * is not strongly dependent on downscaling.  */
        factor = Math.Max(1, minside / 500);
        Pix pixsc;
        if (factor == 1)
            pixsc = Copy(null, pixt);  /* to be sure pixs is unchanged */
        else if (factor == 2 || factor == 3)
            pixsc = pixt.ScaleAreaMap2();
        else
            pixsc = pixt.ScaleAreaMap(0.25f, 0.25f);

            /* Basic edge mask generation procedure:
             *   ~ work on a grayscale image
             *   ~ get a 1 bpp edge mask by using an edge filter and
             *     thresholding to get fg pixels at the edges
             *   ~ for gray, dilate with a 3x3 brick Sel to get mask over
             *     all pixels within a distance of 1 pixel from the nearest
             *     edge pixel
             *   ~ for color, dilate with a 7x7 brick Sel to get mask over
             *     all pixels within a distance of 3 pixels from the nearest
             *     edge pixel  */
        Pix pixg;
        if (d == 8)
            pixg = pixsc;
        else  /* d == 32 */
            pixg = pixsc.ConvertRGBToLuminance();
        Pix pixe = pixg.SobelEdgeFilter(L.L_ALL_EDGES);
        Pix pixb = pixe.ThresholdToBinary(thresh);
        Invert(pixb, pixb);
        Pix pixm;
        if (d == 8)
            pixm = pixb.MorphSequence("d3.3", 0);
        else
            pixm = pixb.MorphSequence("d7.7", 0);

            /* Mask the near-edge pixels to white, and count the colors.
             * If grayscale, don't count colors within 20 levels of
             * black or white, and only count colors with a fraction
             * of at least 1/10000 of the image pixels.
             * If color, count the number of level 4 octcubes that
             * contain at least 20 pixels.  These magic numbers are guesses
             * as to what might work, based on a small data set.  Results
             * should not be overly sensitive to their actual values. */
        if (d == 8) {
            pixg.SetMasked(pixm, 0xff);
            if (debug != 0) Write("junkpix8.png", pixg, L.IFF_PNG);
            ncolors = pixg.NumSignificantGrayColors(20, 236, 0.0001f, 1);
        } else {  /* d == 32 */
            pixsc.SetMasked(pixm, 0xffffffff);
            if (debug != 0) Write("junkpix32.png", pixsc, L.IFF_PNG);
            ncolors = pixsc.NumberOccupiedOctcubes(4, 20, -1);
        }
    }

    /* ----------------------------------------------------------------------- *
     *               Find the number of unique colors in an image              *
     * ----------------------------------------------------------------------- */
    // leptonica: pixNumColors (colorcontent.c)
    /// <summary>Finds the number of unique colors in an image.</summary>
    /// <param name="factor">subsampling factor; integer</param>
    /// <returns>the number of colors found in the pix</returns>
    /// <remarks>
    /// (1) This returns the number of colors found in the image,
    ///     even if there is a colormap.  If factor == 1 and the
    ///     number of colors differs from the number of entries
    ///     in the colormap, a warning is issued.
    /// (2) Use factor == 1 to find the actual number of colors.
    ///     Use factor > 1 to more efficiently find an approximate
    ///     number of colors.
    /// (3) For d = 2, 4 or 8 bpp grayscale, this returns the number
    ///     of colors found in the image in 'ncolors'.
    /// (4) For d = 32 bpp (rgb), if the number of colors is greater
    ///     than 256, this uses a hash set with factor == 1.
    /// </remarks>
    public int NumColors(int factor)
    {
        const string func = "pixNumColors";
        GetDimensions(out int w, out int h, out int d);
        if (d != 2 && d != 4 && d != 8 && d != 32)
            throw Lept.Arg(func, "d not in {2, 4, 8, 32}");
        if (factor < 1) factor = 1;

        uint[] data = Data;
        int wpl = Wpl;
        int sum = 0;
        if (d != 32) {  /* grayscale */
            int[] inta = new int[256];
            for (int i = 0; i < h; i += factor) {
                ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
                for (int j = 0; j < w; j += factor) {
                    int val;
                    if (d == 8)
                        val = ArrayAccess.GetDataByte(line, j);
                    else if (d == 4)
                        val = ArrayAccess.GetDataQbit(line, j);
                    else  /* d == 2 */
                        val = ArrayAccess.GetDataDibit(line, j);
                    inta[val] = 1;
                }
            }
            for (int i = 0; i < 256; i++)
                if (inta[i] != 0) sum++;

            PixColormap? cmap = Colormap;
            if (cmap != null && factor == 1) {
                int count = cmap.Count;
                if (sum != count)
                    Log.Warning(func, $"colormap size {count} differs from actual colors\n");
            }
            return sum;
        }

            /* 32 bpp rgb; quit if we get above 256 colors */
        int hashsize = 5507;  /* big and prime; collisions are not likely */
        int[] hasha = new int[hashsize];
        bool manycolors = false;
        for (int i = 0; i < h && !manycolors; i += factor) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w; j += factor) {
                uint pixel = line[j];
                ExtractRGBValues(pixel, out int rval, out int gval, out int bval);
                int val = (137 * rval + 269 * gval + 353 * bval) % hashsize;
                if (hasha[val] == 0) {
                    hasha[val] = 1;
                    sum++;
                    if (sum > 256) {
                        manycolors = true;
                        break;
                    }
                }
            }
        }

        if (!manycolors)
            return sum;

            /* More than 256 colors in RGB image; count all the pixels */
        return CountRGBColorsByHash();
    }

    /* ----------------------------------------------------------------------- *
     *             Lossless conversion of RGB image to colormapped             *
     * ----------------------------------------------------------------------- */
    // leptonica: pixConvertRGBToCmapLossless (colorcontent.c)
    /// <summary>Losslessly converts an RGB image with at most 256 colors to a colormapped one.</summary>
    /// <returns>pixd if num colors &lt;= 256; null otherwise</returns>
    /// <remarks>
    /// (1) If there are not more than 256 colors, this losslessly
    ///     converts and RGB image to a colormapped one, with the
    ///     smallest pixel depth required to hold all the colors.
    /// </remarks>
    public Pix? ConvertRGBToCmapLossless()
    {
        const string func = "pixConvertRGBToCmapLossless";
        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");

        int ncolors = NumColors(1);
        if (ncolors > 256) {
            Log.Error(func, $"too many colors found: {ncolors}\n");
            return null;
        }

        GetDimensions(out int w, out int h, out _);
        int d;
        if (ncolors <= 2)
            d = 1;
        else if (ncolors <= 4)
            d = 2;
        else if (ncolors <= 16)
            d = 4;
        else  /* ncolors <= 256 */
            d = 8;

        Pix pixd = Create(w, h, d);
        PixColormap cmap = PixColormap.Create(d);
        uint[] datas = Data;
        int wpls = Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

            /* hasha1 is a 1/0 indicator array for colors seen.
               hasha2 holds the index into the colormap that will be
               generated from the colors in the order seen. This is
               the value inserted into pixd.  */
        int hashsize = 5507;  /* big and prime; collisions are not likely */
        int[] hasha1 = new int[hashsize];
        int[] hasha2 = new int[hashsize];
        int index = -1;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                uint pixel = lines[j];
                ExtractRGBValues(pixel, out int rval, out int gval, out int bval);
                int hashval = (137 * rval + 269 * gval + 353 * bval) % hashsize;
                if (hasha1[hashval] == 0) {  /* new color */
                    hasha1[hashval] = 1;
                    index++;
                    hasha2[hashval] = index;
                    cmap.AddColor(rval, gval, bval);
                }
                int val = hasha2[hashval];
                SetLineDataVal(lined, j, d, (uint)val);
            }
        }
        pixd.SetColormap(cmap);

        return pixd;
    }

    /* ----------------------------------------------------------------------- *
     *       Find the most "populated" colors in the image (and quantize)      *
     * ----------------------------------------------------------------------- */
    // leptonica: pixGetMostPopulatedColors (colorcontent.c)
    /// <summary>Finds the most populated cubes in rgb colorspace.</summary>
    /// <param name="sigbits">2-6, significant bits retained in the quantizer
    ///                       for each component of the input image</param>
    /// <param name="factor">subsampling factor; use 1 for no subsampling</param>
    /// <param name="ncolors">the number of most populated colors to select</param>
    /// <param name="parray">array of colors, each as 0xrrggbb00</param>
    /// <param name="pcmap">colormap of the colors</param>
    /// <remarks>
    /// (1) This finds the ncolors most populated cubes in rgb colorspace,
    ///     where the cube size depends on sigbits as
    ///          cube side = (256 >> sigbits)
    /// (2) The rgb color components are found at the center of the cube.
    /// (3) The output array of colors can be displayed using
    ///          pixDisplayColorArray(array, ncolors, ...);
    /// </remarks>
    public void GetMostPopulatedColors(int sigbits, int factor, int ncolors,
                                       out uint[] parray, out PixColormap pcmap)
    {
        const string func = "pixGetMostPopulatedColors";
        if (Depth != 32)
            throw Lept.Arg(func, "pixs not defined");
        if (sigbits < 2 || sigbits > 6)
            throw Lept.Arg(func, "sigbits not in [2 ... 6]");
        if (factor < 1 || ncolors < 1)
            throw Lept.Arg(func, "factor < 1 or ncolors < 1");

        Numa nahisto = GetRGBHistogram(sigbits, factor);

            /* naindex contains the index into nahisto, which is the rgbindex */
        Numa naindex = nahisto.SortIndexAutoSelect(L.L_SORT_DECREASING);

        int n = naindex.Count;
        ncolors = Math.Min(n, ncolors);
        parray = new uint[ncolors];
        pcmap = PixColormap.Create(8);
        for (int i = 0; i < ncolors; i++) {
            int rgbindex = naindex.GetIValue(i);  /* rgb index */
            L.GetRGBFromIndex((uint)rgbindex, sigbits, out int rval, out int gval, out int bval);
            ComposeRGBPixel(rval, gval, bval, out parray[i]);
            pcmap.AddColor(rval, gval, bval);
        }
    }

    // leptonica: pixSimpleColorQuantize (colorcontent.c)
    /// <summary>Simple color quantizer based on the population of cells in rgb color space.</summary>
    /// <param name="sigbits">2-4, significant bits retained in the quantizer
    ///                       for each component of the input image</param>
    /// <param name="factor">subsampling factor; use 1 for no subsampling</param>
    /// <param name="ncolors">the number of most populated colors to select</param>
    /// <returns>pixd 8 bpp cmapped</returns>
    /// <remarks>
    /// (1) If you want to do color quantization for real, use octcube
    ///     or modified median cut.  This function shows that it is
    ///     easy to make a simple quantizer based solely on the population
    ///     in cells of a given size in rgb color space.
    /// (2) The ncolors most populated cells at the sigbits level form
    ///     the colormap for quantizing, and this uses octcube indexing
    ///     under the covers to assign each pixel to the nearest color.
    /// (3) sigbits is restricted to 2, 3 and 4.  At the low end, the
    ///     color discrimination is very crude; at the upper end, a set of
    ///     similar colors can dominate the result.  Interesting results
    ///     are generally found for sigbits = 3 and ncolors ~ 20.
    /// (4) See also pixColorSegment() for a method of quantizing the
    ///     colors to generate regions of similar color.
    /// (5) See also pixConvertRGBToCmapLossless() to losslessly convert
    ///     an RGB image with not more than 256 colors.
    /// </remarks>
    public Pix SimpleColorQuantize(int sigbits, int factor, int ncolors)
    {
        const string func = "pixSimpleColorQuantize";
        if (Depth != 32)
            throw Lept.Arg(func, "pixs not defined");
        if (sigbits < 2 || sigbits > 4)
            throw Lept.Arg(func, "sigbits not in {2,3,4}");

        GetMostPopulatedColors(sigbits, factor, ncolors, out _, out PixColormap cmap);
        GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 8);
        pixd.SetColormap(cmap);
        pixd.AssignToNearestColor(this, null, 4, null);
        return pixd;
    }

    /* ----------------------------------------------------------------------- *
     *            Constructs a color histogram based on rgb indices            *
     * ----------------------------------------------------------------------- */
    // leptonica: pixGetRGBHistogram (colorcontent.c)
    /// <summary>Constructs a color histogram indexed by rgb indices.</summary>
    /// <param name="sigbits">2-6, significant bits retained in the quantizer
    ///                       for each component of the input image</param>
    /// <param name="factor">subsampling factor; use 1 for no subsampling</param>
    /// <returns>numa histogram of colors, indexed by RGB components</returns>
    /// <remarks>
    /// (1) This uses a simple, fast method of indexing into an rgb image.
    /// (2) The output is a 1D histogram of count vs. rgb-index, which
    ///     uses red sigbits as the most significant and blue as the least.
    /// (3) This function produces the same result as pixMedianCutHisto().
    /// </remarks>
    public Numa GetRGBHistogram(int sigbits, int factor)
    {
        const string func = "pixGetRGBHistogram";
        if (Depth != 32)
            throw Lept.Arg(func, "pixs not defined");
        if (sigbits < 2 || sigbits > 6)
            throw Lept.Arg(func, "sigbits not in [2 ... 6]");
        if (factor < 1)
            throw Lept.Arg(func, "factor < 1");

            /* Get histogram size: 2^(3 * sigbits) */
        int size = 1 << (3 * sigbits);  /* 64, 512, 4096, 32768, 262144 */
        Numa na = Numa.MakeConstant(0, size);  /* init to all 0 */
        float[] array = na.GetFArray(L.L_NOCOPY);

        L.MakeRGBIndexTables(out uint[] rtab, out uint[] gtab, out uint[] btab, sigbits);

            /* Check the number of sampled pixels */
        GetDimensions(out int w, out int h, out _);
        int npts = ((w + factor - 1) / factor) * ((h + factor - 1) / factor);
        if (npts < 1000)
            Log.Warning(func, $"only sampling {npts} pixels\n");
        int wpl = Wpl;
        uint[] data = Data;
        for (int i = 0; i < h; i += factor) {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            for (int j = 0; j < w; j += factor) {
                uint val32 = line[j];
                ExtractRGBValues(val32, out int rval, out int gval, out int bval);
                uint rgbindex = rtab[rval] | gtab[gval] | btab[bval];
                array[rgbindex]++;
            }
        }

        return na;
    }

    /* ----------------------------------------------------------------------- *
     *             Identify images that have highlight (red) color             *
     * ----------------------------------------------------------------------- */
    // leptonica: pixHasHighlightRed (colorcontent.c)
    /// <summary>Identifies images that have highlight (red) color.</summary>
    /// <param name="factor">subsampling; an integer >= 1; use 1 for all pixels</param>
    /// <param name="minfract">threshold fraction of all image pixels; must be > 0.0</param>
    /// <param name="fthresh">threshold on a function of the components; typ. ~2.5</param>
    /// <param name="phasred">true if red pixels are above threshold</param>
    /// <param name="pratio">normalized fraction of threshold
    ///                      red pixels that is actually observed</param>
    /// <param name="ppixdb">seed pixel mask</param>
    /// <remarks>
    /// (1) Pixels are identified as red if they satisfy two conditions:
    ///     (a) The components satisfy (R-B)/B > fthresh   (red or dark fg)
    ///     (b) The red component satisfied R > 128  (red or light bg)
    ///     Masks are generated for (a) and (b), and the intersection
    ///     gives the pixels that are red but not either light bg or
    ///     dark fg.
    /// (2) A typical value for minfract = 0.0001, which gives sensitivity
    ///     to an image where a small fraction of the pixels are printed
    ///     in red.
    /// (3) A typical value for fthresh = 2.5.  Higher values give less
    ///     sensitivity to red, and fewer false positives.
    /// </remarks>
    public void HasHighlightRed(int factor, float minfract, float fthresh,
                                out bool phasred, out float pratio, out Pix ppixdb)
    {
        const string func = "pixHasHighlightRed";
        pratio = 0.0f;
        phasred = false;
        if (Depth != 32)
            throw Lept.Arg(func, "pixs not defined or not 32 bpp");
        if (minfract <= 0.0)
            throw Lept.Arg(func, "minfract must be > 0.0");
        if (fthresh < 1.5 || fthresh > 3.5)
            Log.Warning(func, CFormat.Sprintf("fthresh = %f is out of normal bounds\n", fthresh));

        Pix pix1;
        if (factor > 1)
            pix1 = ScaleByIntSampling(factor);
        else
            pix1 = this;

            /* Identify pixels that are either red or dark foreground */
        FPix fpix = pix1.ComponentFunction(1.0f, 0.0f, -1.0f, 0.0f, 0.0f, 1.0f);
        Pix pix2 = fpix.ThresholdToPix(fthresh);
        Invert(pix2, pix2);

            /* Identify pixels that are either red or light background */
        Pix pix3 = pix1.GetRGBComponent(L.COLOR_RED);
        Pix pix4 = pix3.ThresholdToBinary(130);
        Invert(pix4, pix4);

        And(pix4, pix4, pix2);
        float fract = pix4.ForegroundFraction();
        float ratio = fract / minfract;
        Log.Info(func, CFormat.Sprintf("fract = %7.5f, ratio = %7.3f\n", fract, ratio));
        pratio = ratio;
        if (ratio >= 1.0)
            phasred = true;
        ppixdb = pix4;
    }
}
