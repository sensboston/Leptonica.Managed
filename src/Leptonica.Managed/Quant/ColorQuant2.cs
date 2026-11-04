using System;

namespace Leptonica;

/*
 *  colorquant2.c: Modified median cut color quantization.
 *
 *      High level
 *          pixMedianCutQuant()
 *          pixMedianCutQuantGeneral()
 *          pixMedianCutQuantMixed()
 *          pixFewColorsMedianCutQuantMixed()
 *
 *      Median cut indexed histogram
 *          pixMedianCutHisto()
 *
 *   Paul Heckbert published the median cut algorithm, "Color Image
 *   Quantization for Frame Buffer Display," in Proc. SIGGRAPH '82,
 *   Boston, July 1982, pp. 297-307.
 *
 *   Median cut starts with either the full color space or the occupied
 *   region of color space.  If you're not dithering, the occupied region
 *   can be used, but with dithering, pixels can end up in any place
 *   in the color space, so you must represent the entire color space in
 *   the final colormap.
 *
 *   Color components are quantized to typically 5 or 6 significant
 *   bits (for each of r, g and b).   Call a 3D region of color
 *   space a 'vbox'.  Any color in this quantized space is represented
 *   by an element of a linear histogram array, indexed by rgb value.
 *   The initial region is then divided into two regions that have roughly
 *   equal pixel occupancy (hence the name "median cut").  Subdivision
 *   continues until the requisite number of vboxes has been generated.
 *
 *   The choices made in the subdivision process:
 *     (1) We divide based on the largest vbox side.
 *     (2) Suppose you've chosen a side.  Then you have a histogram
 *         of pixel occupancy in 2D slices of the vbox.  One of those
 *         slices includes the median pixel.  Suppose there are L bins
 *         to the left (smaller index) and R bins to the right.  Then
 *         this slice (or bin) should be assigned to the box containing
 *         the smaller of L and R.  The cut plane is then moved halfway
 *         into the longer side, so that a significant number of
 *         low-count vboxes are produced, allowing much better
 *         reproduction of low-count spot colors.
 *     (3) To make sure that the boxes are subdivided toward equal
 *         occupancy, use an occupancy-sorted priority queue, rather
 *         than a simple queue.
 *     (4) A fraction of the vboxes is subdivided based on population alone;
 *         the rest is subdivided based on the product of the vbox volume and
 *         its population.  By using the product, we avoid further subdivision
 *         of nearly empty vboxes, and directly target large vboxes with
 *         significant population.
 *
 *   Once the subdivision has been made, the colormap is generated,
 *   with one color for each vbox and using the average color in the vbox.
 *   At the same time, the histogram array is converted to an inverse
 *   colormap table, storing the colormap index in every cell in the
 *   vbox.  Finally, using both the colormap and the inverse colormap,
 *   a colormapped pix is quickly generated from the original rgb pix.
 *
 *   In the present implementation, subdivided regions of colorspace
 *   that are not occupied are retained, but not further subdivided.
 *   This is required for our inverse colormap lookup table for
 *   dithering, because dithered pixels may fall into these unoccupied
 *   regions.  For such empty regions, we use the center as the rgb
 *   colormap value.
 *
 *  Differences from C:
 *    - The histogram is an int[] (the C functions return a malloc'd array).
 *    - The vboxes of the priority queue are objects (L_BOX3D), with the sort
 *      parameter as the heap key.
 *    - No destroy functions: the GC owns the objects.
 *    - The debug output (DEBUG_MC_COLORS, DEBUG_SPLIT_AXES) is not ported.
 */

/// <summary>
/// Median cut 3-d volume element (struct L_Box3d).  The heap sorts on the first
/// element, which can be the number of pixels, the volume or a combination of these.
/// </summary>
internal sealed class L_Box3d : IHeapItem
{
    public float sortparam;   /* parameter on which to sort the vbox */
    public int npix;          /* number of pixels in the vbox        */
    public int vol;           /* quantized volume of vbox            */
    public int r1;            /* min r index in the vbox             */
    public int r2;            /* max r index in the vbox             */
    public int g1;            /* min g index in the vbox             */
    public int g2;            /* max g index in the vbox             */
    public int b1;            /* min b index in the vbox             */
    public int b2;            /* max b index in the vbox             */

    public float HeapKey => sortparam;
}

public sealed partial class Pix
{
    /* 5 significant bits for each component is generally satisfactory */
    private const int DefaultSigBits = 5;
    private const int MaxItersAllowed = 5000;  /* prevents infinite looping */

    /* Specify fraction of vboxes made that are sorted on population alone.
     * The remaining vboxes are sorted on (population * vbox-volume).  */
    private const float FractByPopulation = 0.85f;

    /* To get the max value of 'dif' in the dithering color transfer,
     * divide DifCap by 8. */
    private const int DifCap = 100;

    /*------------------------------------------------------------------------*
     *                                 High level                             *
     *------------------------------------------------------------------------*/

    // leptonica: pixMedianCutQuant (colorquant2.c)
    /// <summary>Modified median cut color quantization to 256 colors.</summary>
    /// <param name="ditherflag">1 for dither; 0 for no dither</param>
    /// <returns>pixd 8 bit with colormap</returns>
    /// <remarks>
    /// The pix is 32 bpp; rgb color.
    /// (1) Simple interface.  See pixMedianCutQuantGeneral() for
    ///     use of defaulted parameters.
    /// </remarks>
    public Pix MedianCutQuant(int ditherflag)
    {
        return MedianCutQuantGeneral(ditherflag, 0, 256, DefaultSigBits, 1, 1);
    }

    // leptonica: pixMedianCutQuantGeneral (colorquant2.c)
    /// <summary>Modified median cut color quantization.</summary>
    /// <param name="ditherflag">1 for dither; 0 for no dither</param>
    /// <param name="outdepth">output depth; valid: 0, 1, 2, 4, 8</param>
    /// <param name="maxcolors">between 2 and 256</param>
    /// <param name="sigbits">valid: 5 or 6; use 0 for default</param>
    /// <param name="maxsub">max subsampling, integer; use 0 for default; 1 for no subsampling</param>
    /// <param name="checkbw">1 to check if color content is very small,
    /// 0 to assume there is sufficient color</param>
    /// <returns>pixd 8 bit with colormap</returns>
    /// <remarks>
    /// The pix is 32 bpp; rgb color.
    /// (1) maxcolors must be in the range [2 ... 256].
    /// (2) Use outdepth = 0 to have the output depth computed as the
    ///     minimum required to hold the actual colors found, given
    ///     the maxcolors constraint.
    /// (3) Use outdepth = 1, 2, 4 or 8 to specify the output depth.
    ///     In that case, maxcolors must not exceed 2^(outdepth).
    /// (4) If there are fewer quantized colors in the image than maxcolors,
    ///     the colormap is simply generated from those colors.
    /// (5) maxsub is the maximum allowed subsampling to be used in the
    ///     computation of the color histogram and region of occupied
    ///     color space.  The subsampling is chosen internally for
    ///     efficiency, based on the image size, but this parameter
    ///     limits it.  Use maxsub = 0 for the internal default, which is the
    ///     maximum allowed subsampling.  Use maxsub = 1 to prevent
    ///     subsampling.  In general use maxsub >= 1 to specify the
    ///     maximum subsampling to be allowed, where the actual subsampling
    ///     will be the minimum of this value and the internally
    ///     determined default value.
    /// (6) sigbits can be 5 or 6.  There are 2^24 colors in the color space.
    ///         sigbits     # of volume elems    # of colors in a volume elem
    ///            5              2^15                  2^9 = 512
    ///            6              2^18                  2^6 = 64
    ///     Volume in color space is measured in the number of volume elements.
    /// (7) If the image appears gray because either most of the pixels
    ///     are gray or most of the pixels are essentially black or white,
    ///     the image is trivially quantized with a grayscale colormap.  The
    ///     reason is that median cut divides the color space into rectangular
    ///     regions, and it does a very poor job if all the pixels are
    ///     near the diagonal of the color space cube.
    /// </remarks>
    public Pix MedianCutQuantGeneral(int ditherflag, int outdepth, int maxcolors, int sigbits,
                                     int maxsub, int checkbw)
    {
        const string func = "pixMedianCutQuantGeneral";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        if (maxcolors < 2 || maxcolors > 256)
            throw Lept.Arg(func, "maxcolors not in [2...256]");
        if (outdepth != 0 && outdepth != 1 && outdepth != 2 && outdepth != 4 &&
            outdepth != 8)
            throw Lept.Arg(func, "outdepth not in {0,1,2,4,8}");
        if (outdepth > 0 && (maxcolors > (1 << outdepth)))
            throw Lept.Arg(func, "maxcolors > 2^(outdepth)");
        if (sigbits == 0)
            sigbits = DefaultSigBits;
        else if (sigbits < 5 || sigbits > 6)
            throw Lept.Arg(func, "sigbits not 5 or 6");
        if (maxsub <= 0)
            maxsub = 10;  /* default will prevail for 10^7 pixels or less */

            /* Determine if the image has sufficient color content.
             * If pixfract << 1, most pixels are close to black or white.
             * If colorfract << 1, the pixels that are not near
             *   black or white have very little color.
             * If with little color, quantize with a grayscale colormap. */
        GetDimensions(out int w, out int h, out _);
        if (checkbw != 0) {
            int minside = Math.Min(w, h);
            int factor = Math.Max(1, minside / 400);
            ColorFraction(20, 244, 20, factor, out float pixfract, out float colorfract);
            if (pixfract * colorfract < 0.00025) {
                Log.Info(func, CFormat.Sprintf("\n  Pixel fraction neither white nor black = %6.3f" +
                                               "\n  Color fraction of those pixels = %6.3f" +
                                               "\n  Quantizing in gray\n",
                                               (double)pixfract, (double)colorfract));
                return ConvertTo8(true);
            }
        }

            /* Compute the color space histogram.  Default sampling
             * is about 10^5 sampled pixels.  */
        int subsample;
        if (maxsub == 1) {
            subsample = 1;
        } else {
            subsample = (int)Math.Sqrt((double)(w * h) / 100000.0);
            subsample = Math.Max(1, Math.Min(maxsub, subsample));
        }
        int[] histo = MedianCutHisto(sigbits, subsample);
        int histosize = 1 << (3 * sigbits);

            /* See if the number of quantized colors is less than maxcolors */
        int ncolors = 0;
        bool smalln = true;
        for (int i = 0; i < histosize; i++) {
            if (histo[i] != 0)
                ncolors++;
            if (ncolors > maxcolors) {
                smalln = false;
                break;
            }
        }
        PixColormap cmap;
        if (smalln) {  /* finish up now */
            if (outdepth == 0) {
                if (ncolors <= 2)
                    outdepth = 1;
                else if (ncolors <= 4)
                    outdepth = 2;
                else if (ncolors <= 16)
                    outdepth = 4;
                else
                    outdepth = 8;
            }
            cmap = CmapGenerateFromHisto(this, outdepth, histo, histosize, sigbits);
            return QuantizeWithColormap(this, ditherflag, outdepth, cmap,
                                        histo, histosize, sigbits);
        }

            /* Build a 3D summed-volume table once.  Median-cut repeatedly asks
         * for populations of boxes and cumulative axis slices; answering
         * those queries from the prefix table avoids rescanning the same
         * color bins after every split. */
        int[] histoPrefix = BuildMedianCutPrefix(histo, sigbits);

        /* Initial vbox: minimum region in colorspace occupied by pixels */
        L_Box3d vbox;
        if (ditherflag != 0 || subsample > 1)  /* use full color space */
            vbox = Box3dCreate(0, (1 << sigbits) - 1,
                               0, (1 << sigbits) - 1,
                               0, (1 << sigbits) - 1);
        else
            vbox = GetColorRegion(this, sigbits, subsample);
        vbox.npix = VboxGetCount(vbox, histoPrefix, sigbits);
        vbox.vol = VboxGetVolume(vbox);

            /* For a fraction 'popcolors' of the desired 'maxcolors',
             * generate median cuts based on population, putting
             * everything on a priority queue sorted by population. */
        L_Heap lh = L_Heap.Create(0, L.L_SORT_DECREASING);
        lh.Add(vbox);
        ncolors = 1;
        int niters = 0;
        int popcolors = (int)(FractByPopulation * maxcolors);
        while (true) {
            vbox = (L_Box3d)lh.Remove()!;
            if (VboxGetCount(vbox, histoPrefix, sigbits) == 0)  { /* just put it back */
                lh.Add(vbox);
                continue;
            }
            MedianCutApply(histo, histoPrefix, sigbits, vbox, out L_Box3d? vbox1, out L_Box3d? vbox2);
            if (vbox1 == null) {
                Log.Warning(func, "vbox1 not defined; shouldn't happen!\n");
                break;
            }
            if (vbox1.vol > 1)
                vbox1.sortparam = vbox1.npix;
            lh.Add(vbox1);
            if (vbox2 != null) {  /* vbox2 can be null */
                if (vbox2.vol > 1)
                    vbox2.sortparam = vbox2.npix;
                lh.Add(vbox2);
                ncolors++;
            }
            if (ncolors >= popcolors)
                break;
            if (niters++ > MaxItersAllowed) {
                Log.Warning(func, "infinite loop; perhaps too few pixels!\n");
                break;
            }
        }

            /* Re-sort by the product of pixel occupancy times the size
             * in color space.  Normalize to the largest product to avoid
             * integer overflow. */
        float maxprod = 0.0f;
        for (int i = 0; i < lh.Count; i++) {
            vbox = (L_Box3d)lh.GetElement(i);
            float prod = (float)vbox.npix * (float)vbox.vol;
            if (prod > maxprod) maxprod = prod;
        }
        float norm = (float)((maxprod == 0) ? 1.0 : 1000000.0 / maxprod);
        L_Heap lhs = L_Heap.Create(0, L.L_SORT_DECREASING);
        L_Box3d? vboxr;
        while ((vboxr = (L_Box3d?)lh.Remove()) != null) {
            vboxr.sortparam = norm * vboxr.npix * vboxr.vol;
            lhs.Add(vboxr);
        }

            /* For the remaining (maxcolors - popcolors), generate the
             * median cuts using the (npix * vol) sorting. */
        while (true) {
            vbox = (L_Box3d)lhs.Remove()!;
            if (VboxGetCount(vbox, histoPrefix, sigbits) == 0)  { /* just put it back */
                lhs.Add(vbox);
                continue;
            }
            MedianCutApply(histo, histoPrefix, sigbits, vbox, out L_Box3d? vbox1, out L_Box3d? vbox2);
            if (vbox1 == null) {
                Log.Warning(func, "vbox1 not defined; shouldn't happen!\n");
                break;
            }
            if (vbox1.vol > 1)
                vbox1.sortparam = norm * vbox1.npix * vbox1.vol;
            lhs.Add(vbox1);
            if (vbox2 != null) {  /* vbox2 can be null */
                if (vbox2.vol > 1)
                    vbox2.sortparam = norm * vbox2.npix * vbox2.vol;
                lhs.Add(vbox2);
                ncolors++;
            }
            if (ncolors >= maxcolors)
                break;
            if (niters++ > MaxItersAllowed) {
                Log.Warning(func, "infinite loop; perhaps too few pixels!\n");
                break;
            }
        }

            /* Re-sort by pixel occupancy.  This is not necessary,
             * but it makes a more useful listing.  */
        lh = L_Heap.Create(0, L.L_SORT_DECREASING);
        while ((vboxr = (L_Box3d?)lhs.Remove()) != null) {
            vboxr.sortparam = vboxr.npix;
            lh.Add(vboxr);
        }

            /* Generate colormap from median cuts and quantize pixd */
        cmap = CmapGenerateFromMedianCuts(lh, histo, sigbits);
        if (outdepth == 0) {
            ncolors = cmap.Count;
            if (ncolors <= 2)
                outdepth = 1;
            else if (ncolors <= 4)
                outdepth = 2;
            else if (ncolors <= 16)
                outdepth = 4;
            else
                outdepth = 8;
        }
        Pix pixd = QuantizeWithColormap(this, ditherflag, outdepth, cmap,
                                        histo, histosize, sigbits);

            /* Force darkest color to black if each component <= 4 */
        int index = cmap.GetRankIntensity(0.0f);
        cmap.GetColor(index, out int rval, out int gval, out int bval);
        if (rval < 5 && gval < 5 && bval < 5)
            cmap.ResetColor(index, 0, 0, 0);

            /* Force lightest color to white if each component >= 252 */
        index = cmap.GetRankIntensity(1.0f);
        cmap.GetColor(index, out rval, out gval, out bval);
        if (rval > 251 && gval > 251 && bval > 251)
            cmap.ResetColor(index, 255, 255, 255);

        return pixd;
    }

    // leptonica: pixMedianCutQuantMixed (colorquant2.c)
    /// <summary>Median cut quantization of the color pixels, with separate gray levels for the others.</summary>
    /// <param name="ncolor">maximum number of colors assigned to pixels with significant color</param>
    /// <param name="ngray">number of gray colors to be used; must be &gt;= 2</param>
    /// <param name="darkthresh">threshold near black; if the lightest component
    /// is below this, the pixel is not considered to be gray or color;
    /// uses 0 for default</param>
    /// <param name="lightthresh">threshold near white; if the darkest component
    /// is above this, the pixel is not considered to be gray or color;
    /// use 0 for default</param>
    /// <param name="diffthresh">thresh for the max difference between component
    /// values; for differences below this, the pixel is considered to be gray;
    /// use 0 for default</param>
    /// <returns>pixd 8 bpp cmapped</returns>
    /// <remarks>
    /// The pix is 32 bpp; rgb color.
    /// (1) ncolor + ngray must not exceed 255.
    /// (2) The method makes use of pixMedianCutQuantGeneral() with
    ///     minimal addition.
    ///     (a) Preprocess the image, setting all pixels with little color
    ///         to black, and populating an auxiliary 8 bpp image with the
    ///         expected colormap values corresponding to the set of
    ///         quantized gray values.
    ///     (b) Color quantize the altered input image to n + 1 colors.
    ///     (c) Augment the colormap with the gray indices, and
    ///         substitute the gray quantized values from the auxiliary
    ///         image for those in the color quantized output that had
    ///         been quantized as black.
    /// (3) Median cut color quantization is relatively poor for grayscale
    ///     images with many colors, when compared to octcube quantization.
    ///     Thus, for images with both gray and color, it is important
    ///     to quantize the gray pixels by another method.  Here, we
    ///     are conservative in detecting color, preferring to use
    ///     a few extra bits to encode colorful pixels that push them
    ///     to gray.
    /// </remarks>
    public Pix MedianCutQuantMixed(int ncolor, int ngray, int darkthresh, int lightthresh,
                                   int diffthresh)
    {
        const string func = "pixMedianCutQuantMixed";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        if (ngray < 2)
            throw Lept.Arg(func, "ngray < 2");
        if (ncolor + ngray > 255)
            throw Lept.Arg(func, "ncolor + ngray > 255");
        if (darkthresh <= 0) darkthresh = 20;
        if (lightthresh <= 0) lightthresh = 244;
        if (diffthresh <= 0) diffthresh = 20;

            /* First check if this should be quantized in gray.
             * Use a more sensitive parameter for detecting color than with
             * pixMedianCutQuantGeneral(), because this function can handle
             * gray pixels well.  */
        GetDimensions(out int w, out int h, out _);
        int minside = Math.Min(w, h);
        int factor = Math.Max(1, minside / 400);
        ColorFraction(darkthresh, lightthresh, diffthresh, factor,
                      out float pixfract, out float colorfract);
        if (pixfract * colorfract < 0.0001) {
            Log.Info(func, CFormat.Sprintf("\n  Pixel fraction neither white nor black = %6.3f" +
                                           "\n  Color fraction of those pixels = %6.3f" +
                                           "\n  Quantizing in gray\n",
                                           (double)pixfract, (double)colorfract));
            Pix pixg0 = ConvertTo8(false);
            return pixg0.ThresholdOn8bpp(ngray, true);
        }

            /* OK, there is color in the image.
             * Preprocess to handle the gray pixels.  Set the color pixels in pixc
             * to black, and store their (eventual) colormap indices in pixg.*/
        Pix pixc = Copy(null, this);
        Pix pixg = Create(w, h, 8);  /* color pixels will remain 0 here */
        uint[] datac = pixc.Data;
        uint[] datag = pixg.Data;
        int wplc = pixc.Wpl;
        int wplg = pixg.Wpl;
        int[] lut = new int[256];
        for (int i = 0; i < 256; i++)
            lut[i] = ncolor + 1 + (i * (ngray - 1) + 128) / 255;
        for (int i = 0; i < h; i++) {
            int linec = i * wplc;
            Span<uint> lineg = datag.AsSpan(i * wplg, wplg);
            for (int j = 0; j < w; j++) {
                bool iscolor = false;
                ExtractRGBValues(datac[linec + j], out int rval, out int gval, out int bval);
                int minval = Math.Min(rval, gval);
                minval = Math.Min(minval, bval);
                int maxval = Math.Max(rval, gval);
                maxval = Math.Max(maxval, bval);
                if (maxval >= darkthresh &&
                    minval <= lightthresh &&
                    maxval - minval >= diffthresh) {
                    iscolor = true;
                }
                if (!iscolor) {
                    datac[linec + j] = 0x0;  /* set to black */
                    int grayval = (maxval + minval) / 2;
                    ArrayAccess.SetDataByte(lineg, j, lut[grayval]);
                }
            }
        }

            /* Median cut on color pixels plus black */
        Pix pixd = pixc.MedianCutQuantGeneral(0, 8, ncolor + 1, DefaultSigBits, 1, 0);

            /* Augment the colormap with gray values.  The new cmap
             * indices should agree with the values previously stored in pixg. */
        PixColormap cmap = pixd.Colormap!;
        int nc = cmap.Count;
        int unused = ncolor  + 1 - nc;
        if (unused < 0)
            Log.Error(func, $"Too many colors: extra = {-unused}\n");
        if (unused > 0) {  /* fill in with black; these won't be used */
            Log.Info(func, $"{unused} unused colors\n");
            for (int i = 0; i < unused; i++)
                cmap.AddColor(0, 0, 0);
        }
        for (int i = 0; i < ngray; i++) {
            int grayval = (255 * i) / (ngray - 1);
            cmap.AddColor(grayval, grayval, grayval);
        }

            /* Substitute cmap indices for the gray pixels into pixd */
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            ReadOnlySpan<uint> lineg = datag.AsSpan(i * wplg, wplg);
            for (int j = 0; j < w; j++) {
                int val = ArrayAccess.GetDataByte(lineg, j);  /* if 0, it's a color pixel */
                if (val != 0)
                    ArrayAccess.SetDataByte(lined, j, val);
            }
        }

        return pixd;
    }

    // leptonica: pixFewColorsMedianCutQuantMixed (colorquant2.c)
    /// <summary>The "few colors" version of pixMedianCutQuantMixed().</summary>
    /// <param name="ncolor">number of colors to be assigned to pixels with significant color</param>
    /// <param name="ngray">number of gray colors to be used; must be &gt;= 2</param>
    /// <param name="maxncolors">maximum number of colors to be returned from
    /// pixColorsForQuantization(); use 0 for default</param>
    /// <param name="darkthresh">threshold near black; if the lightest component
    /// is below this, the pixel is not considered to be gray or color;
    /// use 0 for default</param>
    /// <param name="lightthresh">threshold near white; if the darkest component
    /// is above this, the pixel is not considered to be gray or color;
    /// use 0 for default</param>
    /// <param name="diffthresh">thresh for the max difference between component
    /// values; for differences below this, the pixel is considered to be gray;
    /// use 0 for default</param>
    /// <returns>pixd 8 bpp, median cut quantized for pixels that are not gray; gray
    /// pixels are quantized separately over the full gray range; null if too many
    /// colors</returns>
    /// <remarks>
    /// The pix is 32 bpp rgb.
    /// (1) This is the "few colors" version of pixMedianCutQuantMixed().
    ///     It fails (returns null) if it finds more than maxncolors, but
    ///     otherwise it gives the same result.
    /// (2) Recommended input parameters are:
    ///         maxncolors:  20
    ///         darkthresh:  20
    ///         lightthresh: 244
    ///         diffthresh:  15  (any higher can miss colors differing
    ///                           slightly from gray)
    /// (3) Both ncolor and ngray should be at least equal to maxncolors.
    ///     If they're not, they are automatically increased, and a
    ///     warning is given.
    /// (4) If very little color content is found, the input is
    ///     converted to gray and quantized in equal intervals.
    /// (5) This can be useful for quantizing orthographically generated
    ///     images such as color maps, where there may be more than 256 colors
    ///     because of aliasing or jpeg artifacts on text or lines, but
    ///     there are a relatively small number of solid colors.
    /// (6) Example of usage:
    ///        // Try to quantize, using default values for mixed med cut
    ///        Pix? pixq = pixs.FewColorsMedianCutQuantMixed(100, 20, 0, 0, 0, 0);
    ///        if (pixq == null)  // too many colors; don't quantize
    ///            pixq = Pix.Clone(pixs);
    /// </remarks>
    public Pix? FewColorsMedianCutQuantMixed(int ncolor, int ngray, int maxncolors,
                                             int darkthresh, int lightthresh, int diffthresh)
    {
        const string func = "pixFewColorsMedianCutQuantMixed";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        if (maxncolors <= 0) maxncolors = 20;
        if (darkthresh <= 0) darkthresh = 20;
        if (lightthresh <= 0) lightthresh = 244;
        if (diffthresh <= 0) diffthresh = 15;
        if (ncolor < maxncolors) {
            Log.Warning(func, $"ncolor too small; setting to {maxncolors}\n");
            ncolor = maxncolors;
        }
        if (ngray < maxncolors) {
            Log.Warning(func, $"ngray too small; setting to {maxncolors}\n");
            ngray = maxncolors;
        }

            /* Estimate the color content and the number of colors required */
        ColorsForQuantization(15, out int ncolors, out bool iscolor, 0);

            /* Note that maxncolors applies to all colors required to quantize,
             * both gray and colorful */
        if (ncolors > maxncolors) {
            Log.Error(func, "too many colors");
            return null;
        }

            /* If no color, return quantized gray pix */
        if (!iscolor) {
            Pix pixg = ConvertTo8(false);
            return pixg.ThresholdOn8bpp(ngray, true);
        }

            /* Use the mixed gray/color quantizer */
        return MedianCutQuantMixed(ncolor, ngray, darkthresh, lightthresh, diffthresh);
    }

    /*------------------------------------------------------------------------*
     *                        Median cut indexed histogram                    *
     *------------------------------------------------------------------------*/

    // leptonica: pixMedianCutHisto (colorquant2.c)
    /// <summary>Computes the histogram of the colors in a quantized color space.</summary>
    /// <param name="sigbits">valid: 5 or 6</param>
    /// <param name="subsample">integer &gt; 0</param>
    /// <returns>histo: 1-d array, giving the number of pixels in each
    /// quantized region of color space</returns>
    /// <remarks>
    /// The pix is 32 bpp; rgb color.
    /// (1) Array is indexed by (3 * sigbits) bits.  The array size
    ///     is 2^(3 * sigbits).
    /// (2) Indexing into the array from rgb uses red sigbits as
    ///     most significant and blue as least.
    /// </remarks>
    public int[] MedianCutHisto(int sigbits, int subsample)
    {
        const string func = "pixMedianCutHisto";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (sigbits < 5 || sigbits > 6)
            throw Lept.Arg(func, "sigbits not 5 or 6");
        if (subsample <= 0)
            throw Lept.Arg(func, "subsample not > 0");

        int histosize = 1 << (3 * sigbits);
        int[] histo = new int[histosize];

        int rshift = 8 - sigbits;
        uint mask = 0xffu >> rshift;
        GetDimensions(out int w, out int h, out _);
        uint[] data = Data;
        int wpl = Wpl;
        for (int i = 0; i < h; i += subsample) {
            int line = i * wpl;
            for (int j = 0; j < w; j += subsample) {
                uint pixel = data[line + j];
                int index = GetColorIndexMedianCut(pixel, rshift, mask, sigbits);
                histo[index]++;
            }
        }

        return histo;
    }

    /*------------------------------------------------------------------------*
     *                               Static helpers                           *
     *------------------------------------------------------------------------*/

    // leptonica: pixcmapGenerateFromHisto (colorquant2.c)
    /// <param name="pixs">32 bpp; rgb color</param>
    /// <param name="depth">of colormap</param>
    /// <param name="histo">the histogram</param>
    /// <param name="histosize">size of the histogram</param>
    /// <param name="sigbits">significant bits</param>
    /// <remarks>
    /// (1) This is used when the number of colors in the histo
    ///     is not greater than maxcolors.
    /// (2) As a side-effect, the histo becomes an inverse colormap,
    ///     labeling the cmap indices for each existing color.
    /// </remarks>
    private static PixColormap CmapGenerateFromHisto(Pix pixs, int depth, int[] histo,
                                                     int histosize, int sigbits)
    {
        if (pixs.Depth != 32)
            throw Lept.Arg("pixcmapGenerateFromHisto", "pixs not 32 bpp");

            /* Capture the rgb values of each occupied cube in the histo,
             * and re-label the histo value with the colormap index. */
        PixColormap cmap = PixColormap.Create(depth);
        int shift = 8 - sigbits;
        uint mask = 0xffu >> shift;
        int index = 0;
        for (int i = 0; i < histosize; i++) {
            if (histo[i] != 0) {
                int rval = (i >> (2 * sigbits)) << shift;
                int gval = (int)((((uint)i >> sigbits) & mask) << shift);
                int bval = (int)(((uint)i & mask) << shift);
                cmap.AddColor(rval, gval, bval);
                histo[i] = index++;
            }
        }

        return cmap;
    }

    // leptonica: pixQuantizeWithColormap (colorquant2.c)
    /// <param name="pixs">32 bpp; rgb color</param>
    /// <param name="ditherflag">1 for dither; 0 for no dither</param>
    /// <param name="outdepth">depth of the returned pixd</param>
    /// <param name="cmap">colormap</param>
    /// <param name="indexmap">lookup table</param>
    /// <param name="mapsize">size of the lookup table</param>
    /// <param name="sigbits">significant bits in output</param>
    /// <returns>pixd quantized to colormap</returns>
    /// <remarks>
    /// (1) The indexmap is a LUT that takes the rgb indices of the
    ///     pixel and returns the index into the colormap.
    /// (2) If ditherflag is 1, outdepth is ignored and the output
    ///     depth is set to 8.
    /// </remarks>
    private static Pix QuantizeWithColormap(Pix pixs, int ditherflag, int outdepth,
                                            PixColormap cmap, int[] indexmap, int mapsize,
                                            int sigbits)
    {
        if (pixs.Depth != 32)
            throw Lept.Arg("pixQuantizeWithColormap", "pixs not 32 bpp");
        if (ditherflag != 0)
            outdepth = 8;

        pixs.GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, outdepth);
        pixd.SetColormap(cmap);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;

        int rshift = 8 - sigbits;
        uint mask = 0xffu >> rshift;
        if (ditherflag == 0) {
            for (int i = 0; i < h; i++) {
                int lines = i * wpls;
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                if (outdepth == 1) {
                    for (int j = 0; j < w; j++) {
                        uint pixel = datas[lines + j];
                        int index = GetColorIndexMedianCut(pixel, rshift, mask, sigbits);
                        if (indexmap[index] != 0)
                            ArrayAccess.SetDataBit(lined, j);
                    }
                } else if (outdepth == 2) {
                    for (int j = 0; j < w; j++) {
                        uint pixel = datas[lines + j];
                        int index = GetColorIndexMedianCut(pixel, rshift, mask, sigbits);
                        ArrayAccess.SetDataDibit(lined, j, indexmap[index]);
                    }
                } else if (outdepth == 4) {
                    for (int j = 0; j < w; j++) {
                        uint pixel = datas[lines + j];
                        int index = GetColorIndexMedianCut(pixel, rshift, mask, sigbits);
                        ArrayAccess.SetDataQbit(lined, j, indexmap[index]);
                    }
                } else {  /* outdepth == 8 */
                    int j = 0;
                    for (; j + 3 < w; j += 4) {
                        uint p0 = datas[lines + j];
                        uint p1 = datas[lines + j + 1];
                        uint p2 = datas[lines + j + 2];
                        uint p3 = datas[lines + j + 3];
                        uint v0 = (byte)indexmap[GetColorIndexMedianCut(p0, rshift, mask, sigbits)];
                        uint v1 = (byte)indexmap[GetColorIndexMedianCut(p1, rshift, mask, sigbits)];
                        uint v2 = (byte)indexmap[GetColorIndexMedianCut(p2, rshift, mask, sigbits)];
                        uint v3 = (byte)indexmap[GetColorIndexMedianCut(p3, rshift, mask, sigbits)];
                        lined[j >> 2] = (v0 << 24) | (v1 << 16) | (v2 << 8) | v3;
                    }
                    for (; j < w; j++) {
                        uint pixel = datas[lines + j];
                        int index = GetColorIndexMedianCut(pixel, rshift, mask, sigbits);
                        ArrayAccess.SetDataByte(lined, j, indexmap[index]);
                    }
                }
            }
        } else {  /* ditherflag == 1 */
            byte[] bufu8r = new byte[w];
            byte[] bufu8g = new byte[w];
            byte[] bufu8b = new byte[w];
            int[] buf1r = new int[w];
            int[] buf1g = new int[w];
            int[] buf1b = new int[w];
            int[] buf2r = new int[w];
            int[] buf2g = new int[w];
            int[] buf2b = new int[w];

                /* Start by priming buf2; line 1 is above line 2 */
            pixs.GetRGBLine(0, bufu8r, bufu8g, bufu8b);
            for (int j = 0; j < w; j++) {
                buf2r[j] = 64 * bufu8r[j];
                buf2g[j] = 64 * bufu8g[j];
                buf2b[j] = 64 * bufu8b[j];
            }

            for (int i = 0; i < h - 1; i++) {
                    /* Swap data 2 --> 1, and read in new line 2 */
                Array.Copy(buf2r, buf1r, w);
                Array.Copy(buf2g, buf1g, w);
                Array.Copy(buf2b, buf1b, w);
                pixs.GetRGBLine(i + 1, bufu8r, bufu8g, bufu8b);
                for (int j = 0; j < w; j++) {
                    buf2r[j] = 64 * bufu8r[j];
                    buf2g[j] = 64 * bufu8g[j];
                    buf2b[j] = 64 * bufu8b[j];
                }

                    /* Dither */
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w - 1; j++) {
                    int rval = buf1r[j] / 64;
                    int gval = buf1g[j] / 64;
                    int bval = buf1b[j] / 64;
                    int index = ((rval >> rshift) << (2 * sigbits)) +
                                ((gval >> rshift) << sigbits) + (bval >> rshift);
                    int cmapindex = indexmap[index];
                    ArrayAccess.SetDataByte(lined, j, cmapindex);
                    cmap.GetColor(cmapindex, out int rc, out int gc, out int bc);

                    DiffuseError(buf1r, buf2r, j, buf1r[j] / 8 - 8 * rc, DifCap);
                    DiffuseError(buf1g, buf2g, j, buf1g[j] / 8 - 8 * gc, DifCap);
                    DiffuseError(buf1b, buf2b, j, buf1b[j] / 8 - 8 * bc, DifCap);
                }

                    /* Get last pixel in row; no downward propagation */
                {
                    int rval = buf1r[w - 1] / 64;
                    int gval = buf1g[w - 1] / 64;
                    int bval = buf1b[w - 1] / 64;
                    int index = ((rval >> rshift) << (2 * sigbits)) +
                                ((gval >> rshift) << sigbits) + (bval >> rshift);
                    ArrayAccess.SetDataByte(lined, w - 1, indexmap[index]);
                }
            }

                /* Get last row of pixels; no leftward propagation */
            {
                Span<uint> lined = datad.AsSpan((h - 1) * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    int rval = buf2r[j] / 64;
                    int gval = buf2g[j] / 64;
                    int bval = buf2b[j] / 64;
                    int index = ((rval >> rshift) << (2 * sigbits)) +
                                ((gval >> rshift) << sigbits) + (bval >> rshift);
                    ArrayAccess.SetDataByte(lined, j, indexmap[index]);
                }
            }
        }

        return pixd;
    }

    // leptonica: getColorIndexMedianCut (colorquant2.c)
    /// <param name="pixel">32 bit rgb</param>
    /// <param name="rshift">of component: 8 - sigbits</param>
    /// <param name="mask">over sigbits</param>
    /// <param name="sigbits">significant bits</param>
    /// <returns>rgb index value</returns>
    /// <remarks>
    /// (1) This is used on each pixel in the source image.  No checking
    ///     is done on input values.
    /// </remarks>
    private static int GetColorIndexMedianCut(uint pixel, int rshift, uint mask, int sigbits)
    {
        int rval = (int)(pixel >> (24 + rshift));
        int gval = (int)((pixel >> (16 + rshift)) & mask);
        int bval = (int)((pixel >> (8 + rshift)) & mask);
        return (rval << (2 * sigbits)) + (gval << sigbits) + bval;
    }

    // leptonica: pixGetColorRegion (colorquant2.c)
    /// <param name="pixs">32 bpp; rgb color</param>
    /// <param name="sigbits">valid: 5, 6</param>
    /// <param name="subsample">integer &gt; 0</param>
    /// <returns>vbox: minimum 3D box in color space enclosing all pixels</returns>
    private static L_Box3d GetColorRegion(Pix pixs, int sigbits, int subsample)
    {
        int rmin, gmin, bmin;
        rmin = gmin = bmin = 1000000;
        int rmax, gmax, bmax;
        rmax = gmax = bmax = 0;
        int rshift = 8 - sigbits;
        uint mask = 0xffu >> rshift;
        pixs.GetDimensions(out int w, out int h, out _);
        uint[] data = pixs.Data;
        int wpl = pixs.Wpl;
        for (int i = 0; i < h; i += subsample) {
            int line = i * wpl;
            for (int j = 0; j < w; j += subsample) {
                uint pixel = data[line + j];
                int rval = (int)(pixel >> (24 + rshift));
                int gval = (int)((pixel >> (16 + rshift)) & mask);
                int bval = (int)((pixel >> (8 + rshift)) & mask);
                if (rval < rmin)
                    rmin = rval;
                else if (rval > rmax)
                    rmax = rval;
                if (gval < gmin)
                    gmin = gval;
                else if (gval > gmax)
                    gmax = gval;
                if (bval < bmin)
                    bmin = bval;
                else if (bval > bmax)
                    bmax = bval;
            }
        }

        return Box3dCreate(rmin, rmax, gmin, gmax, bmin, bmax);
    }

    // leptonica: medianCutApply (colorquant2.c)
    /// <summary>Splits a color-space box into two parts at the median of its longest axis (median cut).</summary>
    /// <param name="histo">Histogram array in the rgb colorspace.</param>
    /// <param name="histoPrefix">Cumulative (prefix-summed) histogram along the splitting axis.</param>
    /// <param name="sigbits">Number of significant bits per component.</param>
    /// <param name="vbox">input 3D box</param>
    /// <param name="pvbox1">vbox split in two parts</param>
    /// <param name="pvbox2">vbox split in two parts</param>
    /// <remarks>
    /// When the C function fails, it returns an error code which the callers
    /// ignore; the outputs are then null (pvbox1 == null).
    /// </remarks>
    private static void MedianCutApply(int[] histo, int[] histoPrefix, int sigbits, L_Box3d vbox,
                                       out L_Box3d? pvbox1, out L_Box3d? pvbox2)
    {
        const string func = "medianCutApply";

        pvbox1 = null;
        pvbox2 = null;

        if (VboxGetCount(vbox, histoPrefix, sigbits) == 0) {
            Log.Error(func, "no pixels in vbox");
            return;
        }

            /* If the vbox occupies just one element in color space, it can't
             * be split.  Leave the 'sortparam' field at 0, so that it goes to
             * the tail of the priority queue and stays there, thereby avoiding
             * an infinite loop (take off, put back on the head) if it
             * happens to be the most populous box! */
        int rw = vbox.r2 - vbox.r1 + 1;
        int gw = vbox.g2 - vbox.g1 + 1;
        int bw = vbox.b2 - vbox.b1 + 1;
        if (rw == 1 && gw == 1 && bw == 1) {
            pvbox1 = Box3dCopy(vbox);
            return;
        }

            /* Select the longest axis for splitting */
        int maxw = Math.Max(rw, gw);
        maxw = Math.Max(maxw, bw);

            /* Find the partial sum arrays along the selected axis. */
        int[] partialsum = new int[128];
        int total = VboxGetCount(vbox, histoPrefix, sigbits);
        if (maxw == rw) {
            for (int i = vbox.r1; i <= vbox.r2; i++)
                partialsum[i] = VboxGetCount(vbox.r1, i, vbox.g1, vbox.g2,
                                             vbox.b1, vbox.b2, histoPrefix, sigbits);
        } else if (maxw == gw) {
            for (int i = vbox.g1; i <= vbox.g2; i++)
                partialsum[i] = VboxGetCount(vbox.r1, vbox.r2, vbox.g1, i,
                                             vbox.b1, vbox.b2, histoPrefix, sigbits);
        } else {  /* maxw == bw */
            for (int i = vbox.b1; i <= vbox.b2; i++)
                partialsum[i] = VboxGetCount(vbox.r1, vbox.r2, vbox.g1, vbox.g2,
                                             vbox.b1, i, histoPrefix, sigbits);
        }

            /* Determine the cut planes, making sure that two vboxes
             * are always produced.  Generate the two vboxes and compute
             * the sum in each of them.  Choose the cut plane within
             * the greater of the (left, right) sides of the bin in which
             * the median pixel resides.  Here's the surprise: go halfway
             * into that side.  By doing that, you technically move away
             * from "median cut," but in the process a significant number
             * of low-count vboxes are produced, allowing much better
             * reproduction of low-count spot colors. */
        L_Box3d? vbox1 = null, vbox2 = null;
        if (maxw == rw) {
            for (int i = vbox.r1; i <= vbox.r2; i++) {
                if (partialsum[i] > total / 2) {
                    vbox1 = Box3dCopy(vbox);
                    vbox2 = Box3dCopy(vbox);
                    int left = i - vbox.r1;
                    int right = vbox.r2 - i;
                    if (left <= right)
                        vbox1.r2 = Math.Min(vbox.r2 - 1, i + right / 2);
                    else  /* left > right */
                        vbox1.r2 = Math.Max(vbox.r1, i - 1 - left / 2);
                    vbox2.r1 = vbox1.r2 + 1;
                    break;
                }
            }
        } else if (maxw == gw) {
            for (int i = vbox.g1; i <= vbox.g2; i++) {
                if (partialsum[i] > total / 2) {
                    vbox1 = Box3dCopy(vbox);
                    vbox2 = Box3dCopy(vbox);
                    int left = i - vbox.g1;
                    int right = vbox.g2 - i;
                    if (left <= right)
                        vbox1.g2 = Math.Min(vbox.g2 - 1, i + right / 2);
                    else  /* left > right */
                        vbox1.g2 = Math.Max(vbox.g1, i - 1 - left / 2);
                    vbox2.g1 = vbox1.g2 + 1;
                    break;
                }
            }
        } else {  /* maxw == bw */
            for (int i = vbox.b1; i <= vbox.b2; i++) {
                if (partialsum[i] > total / 2) {
                    vbox1 = Box3dCopy(vbox);
                    vbox2 = Box3dCopy(vbox);
                    int left = i - vbox.b1;
                    int right = vbox.b2 - i;
                    if (left <= right)
                        vbox1.b2 = Math.Min(vbox.b2 - 1, i + right / 2);
                    else  /* left > right */
                        vbox1.b2 = Math.Max(vbox.b1, i - 1 - left / 2);
                    vbox2.b1 = vbox1.b2 + 1;
                    break;
                }
            }
        }
        pvbox1 = vbox1;
        pvbox2 = vbox2;
        if (vbox1 == null) {
            Log.Error(func, "vbox1 not made; shouldn't happen");
            return;
        }
        if (vbox2 == null) {
            Log.Error(func, "vbox2 not made; shouldn't happen");
            return;
        }
        vbox1.npix = VboxGetCount(vbox1, histoPrefix, sigbits);
        vbox2.npix = VboxGetCount(vbox2, histoPrefix, sigbits);
        vbox1.vol = VboxGetVolume(vbox1);
        vbox2.vol = VboxGetVolume(vbox2);
    }

    // leptonica: pixcmapGenerateFromMedianCuts (colorquant2.c)
    /// <param name="lh">priority queue of vboxes</param>
    /// <param name="histo">the histogram</param>
    /// <param name="sigbits">valid: 5 or 6</param>
    /// <remarks>
    /// (1) Each vbox in the heap represents a color in the colormap.
    /// (2) As a side-effect, the histo becomes an inverse colormap,
    ///     where the part of the array correpsonding to each vbox
    ///     is labeled with the cmap index for that vbox.  Then
    ///     for each rgb pixel, the colormap index is found directly
    ///     by mapping the rgb value to the histo array index.
    /// </remarks>
    private static PixColormap CmapGenerateFromMedianCuts(L_Heap lh, int[] histo, int sigbits)
    {
        PixColormap cmap = PixColormap.Create(8);
        int index = 0;
        while (lh.Count > 0) {
            L_Box3d vbox = (L_Box3d)lh.Remove()!;
            VboxGetAverageColor(vbox, histo, sigbits, index, out int rval, out int gval,
                                out int bval);
            cmap.AddColor(rval, gval, bval);
            index++;
        }

        return cmap;
    }

    // leptonica: vboxGetAverageColor (colorquant2.c)
    /// <param name="vbox">3d region of color space for one quantized color</param>
    /// <param name="histo">the histogram</param>
    /// <param name="sigbits">valid: 5 or 6</param>
    /// <param name="index">if &gt;= 0, assign to all colors in histo in this vbox</param>
    /// <param name="prval">average color</param>
    /// <param name="pgval">average color</param>
    /// <param name="pbval">average color</param>
    /// <remarks>
    /// (1) The vbox represents one color in the colormap.
    /// (2) If index &gt;= 0, as a side-effect, all array elements in
    ///     the histo corresponding to the vbox are labeled with this
    ///     cmap index for that vbox.  Otherwise, the histo array
    ///     is not changed.
    /// (3) The vbox is quantized in sigbits.  So the actual 8-bit color
    ///     components are found by multiplying the quantized value
    ///     by either 4 or 8.  We must add 0.5 to the quantized index
    ///     before multiplying to get the approximate 8-bit color in
    ///     the center of the vbox; otherwise we get values on
    ///     the lower corner.
    /// </remarks>
    private static void VboxGetAverageColor(L_Box3d vbox, int[] histo, int sigbits, int index,
                                            out int prval, out int pgval, out int pbval)
    {
        int ntot = 0;
        int mult = 1 << (8 - sigbits);
        int rsum = 0, gsum = 0, bsum = 0;
        for (int i = vbox.r1; i <= vbox.r2; i++) {
            for (int j = vbox.g1; j <= vbox.g2; j++) {
                for (int k = vbox.b1; k <= vbox.b2; k++) {
                    int histoindex = (i << (2 * sigbits)) + (j << sigbits) + k;
                    ntot += histo[histoindex];
                    rsum += (int)(histo[histoindex] * (i + 0.5) * mult);
                    gsum += (int)(histo[histoindex] * (j + 0.5) * mult);
                    bsum += (int)(histo[histoindex] * (k + 0.5) * mult);
                    if (index >= 0)
                        histo[histoindex] = index;
                }
            }
        }

        if (ntot == 0) {
            prval = mult * (vbox.r1 + vbox.r2 + 1) / 2;
            pgval = mult * (vbox.g1 + vbox.g2 + 1) / 2;
            pbval = mult * (vbox.b1 + vbox.b2 + 1) / 2;
        } else {
            prval = rsum / ntot;
            pgval = gsum / ntot;
            pbval = bsum / ntot;
        }
    }

    // Build an inclusive 3D summed-volume table in the same R/G/B layout as histo.
    private static int[] BuildMedianCutPrefix(int[] histo, int sigbits)
    {
        int n = 1 << sigbits;
        int n2 = n * n;
        int[] prefix = (int[])histo.Clone();

        // Prefix along the contiguous blue axis.
        for (int r = 0; r < n; r++) {
            int rbase = r * n2;
            for (int g = 0; g < n; g++) {
                int row = rbase + g * n;
                for (int b = 1; b < n; b++)
                    prefix[row + b] += prefix[row + b - 1];
            }
        }
        // Then along green, followed by red.
        for (int r = 0; r < n; r++) {
            int rbase = r * n2;
            for (int g = 1; g < n; g++) {
                int row = rbase + g * n;
                int previous = row - n;
                for (int b = 0; b < n; b++)
                    prefix[row + b] += prefix[previous + b];
            }
        }
        for (int r = 1; r < n; r++) {
            int row = r * n2;
            int previous = row - n2;
            for (int i = 0; i < n2; i++)
                prefix[row + i] += prefix[previous + i];
        }
        return prefix;
    }

    // Returns the number of histogram pixels in an inclusive box, using 8 corners.
    private static int VboxGetCount(L_Box3d vbox, int[] prefix, int sigbits)
        => VboxGetCount(vbox.r1, vbox.r2, vbox.g1, vbox.g2,
                        vbox.b1, vbox.b2, prefix, sigbits);

    private static int VboxGetCount(int r1, int r2, int g1, int g2,
                                    int b1, int b2, int[] prefix, int sigbits)
    {
        int n = 1 << sigbits;
        int n2 = n * n;
        int count = PrefixAt(prefix, r2, g2, b2, n, n2)
                  - PrefixAt(prefix, r1 - 1, g2, b2, n, n2)
                  - PrefixAt(prefix, r2, g1 - 1, b2, n, n2)
                  - PrefixAt(prefix, r2, g2, b1 - 1, n, n2)
                  + PrefixAt(prefix, r1 - 1, g1 - 1, b2, n, n2)
                  + PrefixAt(prefix, r1 - 1, g2, b1 - 1, n, n2)
                  + PrefixAt(prefix, r2, g1 - 1, b1 - 1, n, n2)
                  - PrefixAt(prefix, r1 - 1, g1 - 1, b1 - 1, n, n2);
        return count;
    }

    private static int PrefixAt(int[] prefix, int r, int g, int b, int n, int n2)
    {
        if (r < 0 || g < 0 || b < 0)
            return 0;
        return prefix[r * n2 + g * n + b];
    }

    // leptonica: vboxGetVolume (colorquant2.c)
    /// <param name="vbox">3d region of color space for one quantized color</param>
    /// <returns>quantized volume of vbox</returns>
    private static int VboxGetVolume(L_Box3d vbox)
    {
        return ((vbox.r2 - vbox.r1 + 1) * (vbox.g2 - vbox.g1 + 1) *
                (vbox.b2 - vbox.b1 + 1));
    }

    // leptonica: box3dCreate (colorquant2.c)
    private static L_Box3d Box3dCreate(int r1, int r2, int g1, int g2, int b1, int b2)
    {
        L_Box3d vbox = new L_Box3d();
        vbox.r1 = r1;
        vbox.r2 = r2;
        vbox.g1 = g1;
        vbox.g2 = g2;
        vbox.b1 = b1;
        vbox.b2 = b2;
        return vbox;
    }

    // leptonica: box3dCopy (colorquant2.c)
    /// <remarks>Don't copy the sortparam.</remarks>
    private static L_Box3d Box3dCopy(L_Box3d vbox)
    {
        L_Box3d vboxc = Box3dCreate(vbox.r1, vbox.r2, vbox.g1, vbox.g2, vbox.b1, vbox.b2);
        vboxc.npix = vbox.npix;
        vboxc.vol = vbox.vol;
        return vboxc;
    }
}
