using System;

namespace Leptonica;

/*
 *  colorquant1.c: Octcube color quantization.
 *
 *  There are several different octcube/octree based quantizations.
 *  These can be classified, in the order in which they appear in this
 *  file, as follows:
 *
 *  (1) General adaptive octree
 *  (2) Adaptive octree by population at fixed level
 *  (3) Adaptive octree using population and with specified number
 *      of output colors
 *  (4) Octcube with colormap representation of mixed color/gray
 *  (5) 256 fixed octcubes covering color space
 *  (6) Octcubes at fixed level for ncolors <= 256
 *  (7) Octcubes at fixed level with RGB output
 *  (8) Quantizing an rgb image using a specified colormap
 *
 *  Leptonica also provides color quantization using a modified
 *  form of median cut.  See colorquant2.c for details.
 *
 *  Differences from C:
 *    - No destroy functions (cqcellTreeDestroy() and the frees): the GC owns
 *      the objects.
 *    - The octree (an array of arrays of CQCELL pointers) is a CqCell[][].
 *    - Output pointers become return values or out parameters; the functions
 *      that return an error code in C throw.
 *    - The debug output (DEBUG_*, PRINT_OCTCUBE_STATS) is not ported.
 */

/// <summary>Cell of the color octree used by pixOctreeColorQuant() (struct ColorQuantCell, CQCELL).</summary>
/// <remarks>
/// (1) the CTE (color table entry) index is sequentially
///     assigned as the tree is pruned back
/// (2) if 'bleaf' == 1, all pixels in that cube have been
///     assigned to one or more CTEs.  But note that if
///     all 8 subcubes have 'bleaf' == 1, it will have no
///     pixels left for assignment and will not be a CTE.
/// (3) 'nleaves', the number of leaves contained at the next
///     lower level is some number between 0 and 8, inclusive.
///     If it is zero, it means that all colors within this cube
///     are part of a single growing cluster that has not yet
///     been set aside as a leaf.  If 'nleaves' &gt; 0, 'bleaf'
///     will be set to 1 and all pixels not assigned to leaves
///     at lower levels will be assigned to a CTE here.
///     (However, as described above, if all pixels are already
///     assigned, we set 'bleaf' = 1 but do not create a CTE
///     at this level.)
/// (4) To keep the maximum color error to a minimum, we
///     prune the tree back to level 2, and require that
///     all 64 level 2 cells are CTEs.
/// (5) We reserve an extra set of colors to prevent running out
///     of colors during the assignment of the final 64 level 2 cells.
///     This is more likely to happen with small images.
/// (6) When we run out of colors, the dithered image can be very
///     poor, so we additionally prevent dithering if the image
///     is small.
/// (7) The color content of the image is measured, and if there
///     is very little color, it is quantized in grayscale.
/// </remarks>
internal sealed class CqCell
{
    public int rc, gc, bc;   /* center values                              */
    public int n;            /* number of samples in this cell             */
    public int index;        /* CTE (color table entry) index              */
    public int nleaves;      /* # of leaves contained at next lower level  */
    public int bleaf;        /* boolean: 0 if not a leaf, 1 if so          */
}

/// <summary>
/// Cell used by pixOctreeQuantNumColors() (struct OctcubeQuantCell, OQCELL).
/// The number of samples is a float, because it is the key that orders the
/// cells in the priority queue.
/// </summary>
internal sealed class OqCell : IHeapItem
{
    public float n;                  /* number of samples in this cell       */
    public int octindex;             /* octcube index                        */
    public int rcum, gcum, bcum;     /* cumulative values                    */
    public int rval, gval, bval;     /* average values                       */

    public float HeapKey => n;
}

/// <summary>Octcube population, for heap sorting octcubes by decreasing population (L_OCTCUBE_POP).</summary>
internal sealed class L_OctcubePop : IHeapItem
{
    public float npix;     /* parameter on which to sort  */
    public int index;      /* octcube index at assigned level */
    public int rval;       /* mean red value of pixels in octcube */
    public int gval;       /* mean green value of pixels in octcube */
    public int bval;       /* mean blue value of pixels in octcube */

    public float HeapKey => npix;
}

public sealed partial class Pix
{
    /* Constants for pixOctreeColorQuant() */
    private const int CqNLevels = 5;               /* only 4, 5 and 6 are allowed */
    private const int CqReservedColors = 64;       /* to allow for level 2 */
                                                   /* remainder CTEs */
    private const int ExtraReservedColors = 25;    /* to avoid running out */
    private const int TreeGenWidth = 350;          /* big enough for good stats */
    private const int MinDitherSize = 250;         /* don't dither if smaller */

    /*
     *   In pixDitherOctindexWithCmap(), we use these default values.
     *   To get the max value of 'dif' in the dithering color transfer,
     *   divide these "DIF_CAP" values by 8.  However, a value of
     *   0 means that there is no cap (infinite cap).  A very small
     *   value is used for POP_DIF_CAP because dithering on the population
     *   generated colormap can be unstable without a tight cap.
     */
    private const int FIXED_DIF_CAP = 0;
    private const int POP_DIF_CAP = 40;

    /*-------------------------------------------------------------------------*
     *                Two-pass adaptive octree color quantization              *
     *-------------------------------------------------------------------------*/

    // leptonica: pixOctreeColorQuant (colorquant1.c)
    /// <summary>Two-pass adaptive octree color quantization.</summary>
    /// <param name="colors">in colormap; some number in range [128 ... 240];
    /// the actual number of colors used will be smaller</param>
    /// <param name="ditherflag">1 to dither, 0 otherwise</param>
    /// <returns>8 bpp pix with colormap</returns>
    /// <remarks>
    /// The pix is 32 bpp; 24-bit color.  The first pass is done on a subsampled
    /// image, and generates a pruned octree of color cubes (CTEs, color table
    /// entries) with 2^15 cells at the deepest level.  The second pass classifies
    /// each pixel by traversing the tree from the root and looking for the lowest
    /// cube that is a leaf.  With dithering, the results are better.
    ///
    /// For robustness, efficiency and high quality output, we do the following:
    /// (1) Measure the color content of the image.  If there is very little
    ///     color, quantize in grayscale.
    /// (2) For efficiency, build the octree with a subsampled image if the
    ///     image is larger than some threshold size.
    /// (3) Reserve an extra set of colors to prevent running out of colors
    ///     when pruning the octree; specifically, during the assignment
    ///     of those level 2 cells out of the 64 that have unassigned
    ///     pixels.  The problem of running out is more likely to happen
    ///     with small images, because the estimation we use for the
    ///     number of pixels available is not accurate.
    /// (4) In the unlikely event that we run out of colors, the dithered
    ///     image can be very poor.  As this would only happen with very
    ///     small images, and dithering is not particularly noticeable with
    ///     such images, turn it off.
    /// </remarks>
    public Pix OctreeColorQuant(int colors, int ditherflag)
    {
        if (Depth != 32)
            throw Lept.Arg("pixOctreeColorQuant", "pixs not 32 bpp");
        if (colors < 128 || colors > 240)  /* further restricted */
            throw Lept.Arg("pixOctreeColorQuant", "colors must be in [128, 240]");

        return OctreeColorQuantGeneral(colors, ditherflag, 0.01f, 0.01f);
    }

    // leptonica: pixOctreeColorQuantGeneral (colorquant1.c)
    /// <summary>Two-pass adaptive octree color quantization, with control of the color test.</summary>
    /// <param name="colors">in colormap; some number in range [128 ... 240];
    /// the actual number of colors used will be smaller</param>
    /// <param name="ditherflag">1 to dither, 0 otherwise</param>
    /// <param name="validthresh">minimum fraction of pixels neither near white
    /// nor black, required for color quantization; typically ~0.01, but smaller
    /// for images that have color but are nearly all white</param>
    /// <param name="colorthresh">minimum fraction of pixels with color that are
    /// not near white or black, that are required for color quantization; typ.
    /// ~0.01, but smaller for images that have color along with a significant
    /// fraction of gray</param>
    /// <returns>pixd 8 bit with colormap</returns>
    /// <remarks>
    /// (1) The parameters validthresh and colorthresh are used to
    ///     determine if color quantization should be used on an image,
    ///     or whether, instead, it should be quantized in grayscale.
    ///     If the image has very few non-white and non-black pixels, or
    ///     if those pixels that are non-white and non-black are all
    ///     very close to either white or black, it is usually better
    ///     to treat the color as accidental and to quantize the image
    ///     to gray only.  To disable conversion to gray and force
    ///     color quantization, use validthresh = 0.0 and colorthresh = 0.0.
    /// (2) See pixOctreeColorQuant() for algorithmic and implementation
    ///     details.  This function has a more general interface.
    /// (3) See pixColorFraction() for computing the fraction of pixels
    ///     that are neither white nor black, and the fraction of those
    ///     pixels that have little color.  We test against the product
    ///     validthresh * colorthresh to find color in images that have
    ///     either very few intermediate gray pixels or that have many
    ///     such gray pixels.
    /// </remarks>
    public Pix OctreeColorQuantGeneral(int colors, int ditherflag, float validthresh,
                                       float colorthresh)
    {
        const string func = "pixOctreeColorQuantGeneral";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (colors < 128 || colors > 240)
            throw Lept.Arg(func, "colors must be in [128, 240]");

            /* Determine if the image has sufficient color content for
             *   octree quantization, based on the input thresholds.
             * If pixfract << 1, most pixels are close to black or white.
             * If colorfract << 1, the pixels that are not near
             *   black or white have very little color.
             * If with insufficient color, quantize with a grayscale colormap. */
        GetDimensions(out int w, out int h, out _);
        if (validthresh > 0.0 && colorthresh > 0.0) {
            int minside = Math.Min(w, h);
            int factor = Math.Max(1, minside / 400);
            ColorFraction(20, 244, 20, factor, out float pixfract, out float colorfract);
            if (pixfract * colorfract < validthresh * colorthresh) {
                Log.Info(func, CFormat.Sprintf("\n  Pixel fraction neither white nor black = %6.3f" +
                                               "\n  Color fraction of those pixels = %6.3f" +
                                               "\n  Quantizing to 8 bpp gray\n",
                                               (double)pixfract, (double)colorfract));
                return ConvertTo8(true);
            }
        } else {
            Log.Info(func, "\n  Process in color by default\n");
        }

            /* Conditionally subsample to speed up the first pass */
        Pix pixsub;
        if (w > TreeGenWidth) {
            float scalefactor = (float)TreeGenWidth / (float)w;
            pixsub = ScaleBySampling(scalefactor, scalefactor);
        } else {
            pixsub = Clone(this);
        }

            /* Drop the number of requested colors if image is very small */
        if (w < MinDitherSize && h < MinDitherSize)
            colors = Math.Min(colors, 220);

            /* Make the pruned octree */
        CqCell[][] cqcaa = OctreeGenerateAndPrune(pixsub, colors, CqReservedColors,
                                                  out PixColormap cmap);

            /* Do not dither if image is very small */
        if (w < MinDitherSize && h < MinDitherSize && ditherflag == 1) {
            Log.Info(func, "Small image: dithering turned off\n");
            ditherflag = 0;
        }

            /* Traverse tree from root, looking for lowest cube
             * that is a leaf, and set dest pix value to its
             * colortable index */
        Pix pixd = OctreeQuantizePixels(this, cqcaa, ditherflag);

            /* Attach colormap and copy res */
        pixd.SetColormap(cmap);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);

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

    // leptonica: octreeGenerateAndPrune (colorquant1.c)
    /// <param name="pixs">32 bpp</param>
    /// <param name="colors">number of colors to use between 128 and 256</param>
    /// <param name="reservedcolors">number of reserved colors</param>
    /// <param name="pcmap">colormap returned</param>
    /// <returns>octree (one array of cells for each level)</returns>
    /// <remarks>
    /// (1) The number of colors in the cmap may differ from the number
    ///     of colors requested, but it will not be larger than 256
    /// </remarks>
    private static CqCell[][] OctreeGenerateAndPrune(Pix pixs, int colors, int reservedcolors,
                                                     out PixColormap pcmap)
    {
        const string func = "octreeGenerateAndPrune";

        int rval, gval, bval;
        int rv = 0, gv = 0, bv = 0;   /* NOTE(upstream): used before being set in the "cmap full" case */
        float[] thresholdFactor = { 0.01f, 0.01f, 1.0f, 1.0f, 1.0f, 1.0f };

        if (pixs.Depth != 32)
            throw Lept.Arg(func, "pixs must be 32 bpp");
        if (colors < 128 || colors > 256)
            throw Lept.Arg(func, "colors not in [128,256]");

        CqCell[][] cqcaa = CqcellTreeCreate();   /* one array for each octree level */

            /* Make the canonical index tables */
        MakeRGBToIndexTables(CqNLevels, out uint[] rtab, out uint[] gtab, out uint[] btab);

            /* Generate an 8 bpp cmap (max size 256) */
        PixColormap cmap = PixColormap.Create(8);
        pcmap = cmap;

        pixs.GetDimensions(out int w, out int h, out _);
        int npix = w * h;  /* initialize to all pixels: number of remaining pixels to be assigned */
        int ncolor = colors - reservedcolors - ExtraReservedColors;  /* number of remaining color cells to be used */
        int ppc = npix / ncolor;  /* ave number of pixels left for each color cell */
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;

            /* Accumulate the centers of each cluster at level CqNLevels */
        CqCell[] cqca = cqcaa[CqNLevels];
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                uint octindex = rtab[rval] | gtab[gval] | btab[bval];
                CqCell cqc = cqca[octindex];
                cqc.n++;
            }
        }

            /* Prune back from the lowest level and generate the colormap */
        for (int level = CqNLevels - 1; level >= 2; level--) {
            float thresh = thresholdFactor[level];  /* factor of ppc for this level */
            cqca = cqcaa[level];
            CqCell[] cqcasub = cqcaa[level + 1];
            int ncells = 1 << (3 * level);
            for (int i = 0; i < ncells; i++) {  /* i is octindex at level */
                CqCell cqc = cqca[i];
                for (int j = 0; j < 8; j++) {  /* check all subnodes */
                    int isub = 8 * i + j;   /* isub is octindex at level+1 */
                    CqCell cqcsub = cqcasub[isub];
                    if (cqcsub.bleaf == 1) {  /* already a leaf? */
                        cqc.nleaves++;   /* count the subcube leaves */
                        continue;
                    }
                    if (cqcsub.n >= thresh * ppc) {  /* make it a true leaf? */
                        cqcsub.bleaf = 1;
                        if (cmap.Count < 256) {
                            cqcsub.index = cmap.Count;  /* assign the color index */
                            GetRGBFromOctcube(isub, level + 1, out rv, out gv, out bv);
                            cmap.AddColor(rv, gv, bv);
                            cqcsub.rc = rv;
                            cqcsub.gc = gv;
                            cqcsub.bc = bv;
                        } else {
                                /* This doesn't seem to happen. Do something. */
                            Log.Error(func, "assigning pixels to wrong color\n");
                            cmap.GetNearestIndex(128, 128, 128, out int cindex);
                            cqcsub.index = cindex;  /* assign to the nearest */
                            cmap.GetColor(cindex, out rval, out gval, out bval);
                            cqcsub.rc = rval;
                            cqcsub.gc = gval;
                            cqcsub.bc = bval;
                        }
                        cqc.nleaves++;
                        npix -= cqcsub.n;
                        ncolor--;
                        if (ncolor > 0)
                            ppc = npix / ncolor;
                        else if (ncolor + reservedcolors > 0)
                            ppc = npix / (ncolor + reservedcolors);
                        else
                            ppc = 1000000;  /* make it big */
                    }
                }
                if (cqc.nleaves > 0 || level == 2) { /* make the cube a leaf now */
                    cqc.bleaf = 1;
                    if (cqc.nleaves < 8) {  /* residual CTE cube: acquire the
                                             * remaining pixels */
                        for (int j = 0; j < 8; j++) {  /* check all subnodes */
                            int isub = 8 * i + j;
                            CqCell cqcsub = cqcasub[isub];
                            if (cqcsub.bleaf == 0)  /* absorb */
                                cqc.n += cqcsub.n;
                        }
                        if (cmap.Count < 256) {
                            cqc.index = cmap.Count;  /* assign the color index */
                            GetRGBFromOctcube(i, level, out rv, out gv, out bv);
                            cmap.AddColor(rv, gv, bv);
                            cqc.rc = rv;
                            cqc.gc = gv;
                            cqc.bc = bv;
                        } else {
                            Log.Warning(func, "possibly assigned pixels to wrong color\n");
                                /* This is very bad.  It will only cause trouble
                                 * with dithering, and we try to avoid it with
                                 * ExtraReservedColors. */
                            cmap.GetNearestIndex(rv, gv, bv, out int cindex);
                            cqc.index = cindex;  /* assign to the nearest */
                            cmap.GetColor(cindex, out rval, out gval, out bval);
                            cqc.rc = rval;
                            cqc.gc = gval;
                            cqc.bc = bval;
                        }
                        npix -= cqc.n;
                        ncolor--;
                        if (ncolor > 0)
                            ppc = npix / ncolor;
                        else if (ncolor + reservedcolors > 0)
                            ppc = npix / (ncolor + reservedcolors);
                        else
                            ppc = 1000000;  /* make it big */
                    }
                } else {  /* absorb all the subpixels but don't make it a leaf */
                    for (int j = 0; j < 8; j++) {  /* absorb from all subnodes */
                        int isub = 8 * i + j;
                        CqCell cqcsub = cqcasub[isub];
                        cqc.n += cqcsub.n;
                    }
                }
            }
        }

        return cqcaa;
    }

    // leptonica: pixOctreeQuantizePixels (colorquant1.c)
    /// <param name="pixs">32 bpp</param>
    /// <param name="cqcaa">octree in array format</param>
    /// <param name="ditherflag">1 for dithering, 0 for no dithering</param>
    /// <remarks>
    /// (1) This routine doesn't need to use the CTEs (colormap
    ///     table entries) because the color indices are embedded
    ///     in the octree.  Thus, the calling program must make
    ///     and attach the colormap to pixd after it is returned.
    /// (2) Dithering is performed in integers, effectively rounding
    ///     to 1/8 sample increment.  The data in the integer buffers is
    ///     64 times the sample values.  The 'dif' is 8 times the
    ///     sample values, and this spread, multiplied by 8, to the
    ///     integer buffers.  Because the dif is truncated to an
    ///     integer, the dither is accurate to 1/8 of a sample increment,
    ///     or 1/2048 of the color range.
    /// </remarks>
    private static Pix OctreeQuantizePixels(Pix pixs, CqCell[][] cqcaa, int ditherflag)
    {
        if (pixs.Depth != 32)
            throw Lept.Arg("pixOctreeQuantizePixels", "pixs must be 32 bpp");

            /* Make output 8 bpp palette image */
        pixs.GetDimensions(out int w, out int h, out _);
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        Pix pixd = Create(w, h, 8);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

            /* Make the canonical index tables */
        MakeRGBToIndexTables(CqNLevels, out uint[] rtab, out uint[] gtab, out uint[] btab);

            /* Traverse tree from root, looking for lowest cube
             * that is a leaf, and set dest pix to its
             * colortable index value.  The results are far
             * better when dithering to get a more accurate
             * average color.  */
        if (ditherflag == 0) {    /* no dithering */
            for (int i = 0; i < h; i++) {
                int lines = i * wpls;
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    ExtractRGBValues(datas[lines + j], out int rval, out int gval, out int bval);
                    uint octindex = rtab[rval] | gtab[gval] | btab[bval];
                    OctreeFindColorCell((int)octindex, cqcaa, out int index, out _, out _, out _);
                    ArrayAccess.SetDataByte(lined, j, index);
                }
            }
        } else {  /* Dither */
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
                    uint octindex = rtab[rval] | gtab[gval] | btab[bval];
                    OctreeFindColorCell((int)octindex, cqcaa, out int index,
                                        out int rc, out int gc, out int bc);
                    ArrayAccess.SetDataByte(lined, j, index);

                    DiffuseError(buf1r, buf2r, j, buf1r[j] / 8 - 8 * rc, 0);
                    DiffuseError(buf1g, buf2g, j, buf1g[j] / 8 - 8 * gc, 0);
                    DiffuseError(buf1b, buf2b, j, buf1b[j] / 8 - 8 * bc, 0);
                }

                    /* Get last pixel in row; no downward propagation */
                {
                    int rval = buf1r[w - 1] / 64;
                    int gval = buf1g[w - 1] / 64;
                    int bval = buf1b[w - 1] / 64;
                    uint octindex = rtab[rval] | gtab[gval] | btab[bval];
                    OctreeFindColorCell((int)octindex, cqcaa, out int index, out _, out _, out _);
                    ArrayAccess.SetDataByte(lined, w - 1, index);
                }
            }

                /* Get last row of pixels; no leftward propagation */
            {
                Span<uint> lined = datad.AsSpan((h - 1) * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    int rval = buf2r[j] / 64;
                    int gval = buf2g[j] / 64;
                    int bval = buf2b[j] / 64;
                    uint octindex = rtab[rval] | gtab[gval] | btab[bval];
                    OctreeFindColorCell((int)octindex, cqcaa, out int index, out _, out _, out _);
                    ArrayAccess.SetDataByte(lined, j, index);
                }
            }
        }

        return pixd;
    }

    /// <summary>
    /// The error diffusion step shared by the three channels of the octcube dithering:
    /// the difference dif (8 times the sample value) is spread to the right neighbor on
    /// this line and to the neighbors below, with weights 3, 3 and 2.
    /// </summary>
    /// <param name="buf1">The error buffer for the current line.</param>
    /// <param name="buf2">The error buffer for the next line.</param>
    /// <param name="j">The index of the current sample in the buffers.</param>
    /// <param name="dif">The difference (8 times the sample value) to spread.</param>
    /// <param name="difcap">The maximum allowed dither transfer; 0 for no cap.</param>
    private static void DiffuseError(int[] buf1, int[] buf2, int j, int dif, int difcap)
    {
        if (difcap > 0) {
            if (dif > difcap) dif = difcap;
            if (dif < -difcap) dif = -difcap;
        }
        if (dif != 0) {
            int val1 = buf1[j + 1] + 3 * dif;
            int val2 = buf2[j] + 3 * dif;
            int val3 = buf2[j + 1] + 2 * dif;
            if (dif > 0) {
                buf1[j + 1] = Math.Min(16383, val1);
                buf2[j] = Math.Min(16383, val2);
                buf2[j + 1] = Math.Min(16383, val3);
            } else {
                buf1[j + 1] = Math.Max(0, val1);
                buf2[j] = Math.Max(0, val2);
                buf2[j + 1] = Math.Max(0, val3);
            }
        }
    }

    // leptonica: octreeFindColorCell (colorquant1.c)
    /// <param name="octindex">the octcube index</param>
    /// <param name="cqcaa">the octree</param>
    /// <param name="pindex">index of CTE; returned to set pixel value</param>
    /// <param name="prval">of CTE</param>
    /// <param name="pgval">of CTE</param>
    /// <param name="pbval">of CTE</param>
    /// <remarks>
    /// (1) As this is in inner loop, we don't check input pointers!
    /// (2) This traverses from the root (well, actually from level 2,
    ///     because the level 2 cubes are the largest CTE cubes),
    ///     and finds the index number of the cell and the color values,
    ///     which can be used either directly or in a (Floyd-Steinberg)
    ///     error-diffusion dithering algorithm.
    /// </remarks>
    private static void OctreeFindColorCell(int octindex, CqCell[][] cqcaa, out int pindex,
                                            out int prval, out int pgval, out int pbval)
    {
        pindex = 0;
        prval = 0;
        pgval = 0;
        pbval = 0;

            /* Use rgb values stored in the cubes; a little faster */
        for (int level = 2; level < CqNLevels; level++) {
            GetOctcubeIndices(octindex, level, out int baseindex, out int subindex);
            CqCell cqc = cqcaa[level][baseindex];
            CqCell cqcsub = cqcaa[level + 1][subindex];
            if (cqcsub.bleaf == 0) {  /* use cell at level above */
                pindex = cqc.index;
                prval = cqc.rc;
                pgval = cqc.gc;
                pbval = cqc.bc;
                break;
            } else if (level == CqNLevels - 1) {  /* reached the bottom */
                pindex = cqcsub.index;
                prval = cqcsub.rc;
                pgval = cqcsub.gc;
                pbval = cqcsub.bc;
                break;
            }
        }
    }

    /*------------------------------------------------------------------*
     *                      Helper cqcell functions                     *
     *------------------------------------------------------------------*/

    // leptonica: cqcellTreeCreate (colorquant1.c)
    private static CqCell[][] CqcellTreeCreate()
    {
            /* Make array of accumulation cell arrays from levels 1 to 5 */
        CqCell[][] cqcaa = new CqCell[CqNLevels + 1][];
        for (int level = 0; level <= CqNLevels; level++) {
            int ncells = 1 << (3 * level);
            CqCell[] cqca = new CqCell[ncells];   /* one array for each octree level */
            cqcaa[level] = cqca;
            for (int i = 0; i < ncells; i++)
                cqca[i] = new CqCell();
        }

        return cqcaa;
    }

    /*------------------------------------------------------------------*
     *                       Helper index functions                     *
     *------------------------------------------------------------------*/

    // leptonica: makeRGBToIndexTables (colorquant1.c)
    /// <param name="cqlevels">can be 1, 2, 3, 4, 5 or 6</param>
    /// <param name="prtab">table for r</param>
    /// <param name="pgtab">table for g</param>
    /// <param name="pbtab">table for b</param>
    /// <remarks>
    /// Set up tables.  e.g., for cqlevels = 5, we need an integer 0 &lt; i &lt; 2^15:
    ///   rtab = 0  i7  0   0  i6  0   0  i5  0   0   i4  0   0   i3  0   0
    ///   gtab = 0  0   i7  0   0  i6  0   0  i5  0   0   i4  0   0   i3  0
    ///   btab = 0  0   0   i7  0  0   i6  0  0   i5  0   0   i4  0   0   i3
    ///
    /// The tables are then used to map from rbg --> index as follows:
    ///   index = 0  r7  g7  b7  r6  g6  b6  r5  g5  b5  r4  g4  b4  r3  g3  b3
    ///
    /// e.g., for cqlevels = 4, we map to
    ///   index = 0  0   0   0   r7  g7  b7  r6  g6  b6  r5  g5  b5  r4  g4  b4
    ///
    /// The notation 'r7' means the MSBit of the r value which has 8 bits,
    /// going down from r7 to r0.  Level 1 is composed of 8 octcubes,
    /// represented by the bits r7 g7 b7, which divide the entire color space
    /// into 8 cubes.  At level 2, each of these 8 octcubes is further divided
    /// into 8 cubes, each labeled by the second most significant bits r6 g6 b6
    /// of the rgb color.
    /// </remarks>
    public static void MakeRGBToIndexTables(int cqlevels, out uint[] prtab, out uint[] pgtab,
                                            out uint[] pbtab)
    {
        if (cqlevels < 1 || cqlevels > 6)
            throw Lept.Arg("makeRGBToIndexTables", "cqlevels must be in {1,...6}");

        uint[] rtab = new uint[256];
        uint[] gtab = new uint[256];
        uint[] btab = new uint[256];
        prtab = rtab;
        pgtab = gtab;
        pbtab = btab;

        switch (cqlevels)
        {
        case 1:
            for (uint i = 0; i < 256; i++) {
                rtab[i] = (i >> 5) & 0x0004;
                gtab[i] = (i >> 6) & 0x0002;
                btab[i] = (i >> 7);
            }
            break;
        case 2:
            for (uint i = 0; i < 256; i++) {
                rtab[i] = ((i >> 2) & 0x0020) | ((i >> 4) & 0x0004);
                gtab[i] = ((i >> 3) & 0x0010) | ((i >> 5) & 0x0002);
                btab[i] = ((i >> 4) & 0x0008) | ((i >> 6) & 0x0001);
            }
            break;
        case 3:
            for (uint i = 0; i < 256; i++) {
                rtab[i] = ((i << 1) & 0x0100) | ((i >> 1) & 0x0020) |
                          ((i >> 3) & 0x0004);
                gtab[i] = (i & 0x0080) | ((i >> 2) & 0x0010) |
                          ((i >> 4) & 0x0002);
                btab[i] = ((i >> 1) & 0x0040) | ((i >> 3) & 0x0008) |
                          ((i >> 5) & 0x0001);
            }
            break;
        case 4:
            for (uint i = 0; i < 256; i++) {
                rtab[i] = ((i << 4) & 0x0800) | ((i << 2) & 0x0100) |
                          (i & 0x0020) | ((i >> 2) & 0x0004);
                gtab[i] = ((i << 3) & 0x0400) | ((i << 1) & 0x0080) |
                          ((i >> 1) & 0x0010) | ((i >> 3) & 0x0002);
                btab[i] = ((i << 2) & 0x0200) | (i & 0x0040) |
                          ((i >> 2) & 0x0008) | ((i >> 4) & 0x0001);
            }
            break;
        case 5:
            for (uint i = 0; i < 256; i++) {
                rtab[i] = ((i << 7) & 0x4000) | ((i << 5) & 0x0800) |
                          ((i << 3) & 0x0100) | ((i << 1) & 0x0020) |
                          ((i >> 1) & 0x0004);
                gtab[i] = ((i << 6) & 0x2000) | ((i << 4) & 0x0400) |
                          ((i << 2) & 0x0080) | (i & 0x0010) |
                          ((i >> 2) & 0x0002);
                btab[i] = ((i << 5) & 0x1000) | ((i << 3) & 0x0200) |
                          ((i << 1) & 0x0040) | ((i >> 1) & 0x0008) |
                          ((i >> 3) & 0x0001);
            }
            break;
        case 6:
            for (uint i = 0; i < 256; i++) {
                rtab[i] = ((i << 10) & 0x20000) | ((i << 8) & 0x4000) |
                          ((i << 6) & 0x0800) | ((i << 4) & 0x0100) |
                          ((i << 2) & 0x0020) | (i & 0x0004);
                gtab[i] = ((i << 9) & 0x10000) | ((i << 7) & 0x2000) |
                          ((i << 5) & 0x0400) | ((i << 3) & 0x0080) |
                          ((i << 1) & 0x0010) | ((i >> 1) & 0x0002);
                btab[i] = ((i << 8) & 0x8000) | ((i << 6) & 0x1000) |
                          ((i << 4) & 0x0200) | ((i << 2) & 0x0040) |
                          (i & 0x0008) | ((i >> 2) & 0x0001);
            }
            break;
        }
    }

    // leptonica: getOctcubeIndexFromRGB (colorquant1.c)
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <param name="rtab">generated with makeRGBToIndexTables()</param>
    /// <param name="gtab">generated with makeRGBToIndexTables()</param>
    /// <param name="btab">generated with makeRGBToIndexTables()</param>
    /// <returns>the octcube index</returns>
    /// <remarks>No error checking!</remarks>
    public static uint GetOctcubeIndexFromRGB(int rval, int gval, int bval, uint[] rtab,
                                              uint[] gtab, uint[] btab)
    {
        return rtab[rval] | gtab[gval] | btab[bval];
    }

    // leptonica: getRGBFromOctcube (colorquant1.c)
    /// <param name="cubeindex">octcube index</param>
    /// <param name="level">at which index is expressed</param>
    /// <param name="prval">r val of this cube</param>
    /// <param name="pgval">g val of this cube</param>
    /// <param name="pbval">b val of this cube</param>
    /// <remarks>
    /// (1) We can consider all octcube indices to represent a
    ///     specific point in color space: namely, the location
    ///     of the 'upper-left' corner of the cube, where indices
    ///     increase down and to the right.  The upper left corner
    ///     of the color space is then 00000....
    /// (2) The 'rgbindex' is a 24-bit representation of the location,
    ///     in octcube notation, at the center of the octcube.
    ///     To get to the center of an octcube, you choose the 111
    ///     octcube at the next lower level.
    /// (3) For example, if the octcube index = 110101 (binary),
    ///     which is a level 2 expression, then the rgbindex
    ///     is the 24-bit representation of 110101111 (at level 3);
    ///     namely, 000110101111000000000000.  The number is padded
    ///     with 3 leading 0s (because the representation uses
    ///     only 21 bits) and 12 trailing 0s (the default for
    ///     levels 4-7, which are contained within each of the level3
    ///     octcubes.  Then the rgb values for the center of the
    ///     octcube are: rval = 11100000, gval = 10100000, bval = 01100000
    /// </remarks>
    internal static void GetRGBFromOctcube(int cubeindex, int level, out int prval,
                                          out int pgval, out int pbval)
    {
            /* Bring to format in 21 bits: (r7 g7 b7 r6 g6 b6 ...) */
            /* This is valid for levels from 0 to 6 */
        int rgbindex = cubeindex << (3 * (7 - level));  /* upper corner of cube */
        rgbindex |= (0x7 << (3 * (6 - level)));   /* index to center of cube */

            /* Extract separate pieces */
        prval = ((rgbindex >> 13) & 0x80) |
                ((rgbindex >> 11) & 0x40) |
                ((rgbindex >> 9) & 0x20) |
                ((rgbindex >> 7) & 0x10) |
                ((rgbindex >> 5) & 0x08) |
                ((rgbindex >> 3) & 0x04) |
                ((rgbindex >> 1) & 0x02);
        pgval = ((rgbindex >> 12) & 0x80) |
                ((rgbindex >> 10) & 0x40) |
                ((rgbindex >> 8) & 0x20) |
                ((rgbindex >> 6) & 0x10) |
                ((rgbindex >> 4) & 0x08) |
                ((rgbindex >> 2) & 0x04) |
                (rgbindex & 0x02);
        pbval = ((rgbindex >> 11) & 0x80) |
                ((rgbindex >> 9) & 0x40) |
                ((rgbindex >> 7) & 0x20) |
                ((rgbindex >> 5) & 0x10) |
                ((rgbindex >> 3) & 0x08) |
                ((rgbindex >> 1) & 0x04) |
                ((rgbindex << 1) & 0x02);
    }

    // leptonica: getOctcubeIndices (colorquant1.c)
    /// <param name="rgbindex">the rgb index</param>
    /// <param name="level">octree level 0, 1, 2, 3, 4, 5</param>
    /// <param name="pbindex">base index index at the octree level</param>
    /// <param name="psindex">sub index index at the next lower level</param>
    /// <remarks>
    /// for CqNLevels = 5, the full RGB index is in the form:
    ///    index = (0[16] 0 r7 g7 b7 r6 g6 b6 r5 g5 b5 r4 g4 b4 r3 g3 b3)
    /// The base index is the index of the octcube at the level given,
    /// whereas the sub index is the index at the next level down.
    /// </remarks>
    private static void GetOctcubeIndices(int rgbindex, int level, out int pbindex,
                                          out int psindex)
    {
        if (level < 0 || level > CqNLevels - 1)
            throw Lept.Arg("getOctcubeIndices", "level must be in e.g., [0 ... 5]");

        pbindex = rgbindex >> (3 * (CqNLevels - level));
        psindex = rgbindex >> (3 * (CqNLevels - 1 - level));
    }

    // leptonica: octcubeGetCount (colorquant1.c)
    /// <param name="level">valid values are in [1,...6]; there are 2^level
    /// cubes along each side of the rgb cube</param>
    /// <returns>2^(3 * level) cubes in the entire rgb cube</returns>
    private static int OctcubeGetCount(int level)
    {
        if (level < 1 || level > 6)
            throw Lept.Arg("octcubeGetCount", "invalid level");

        return 1 << (3 * level);
    }

    /*---------------------------------------------------------------------------*
     *      Adaptive octree quantization based on population at a fixed level    *
     *---------------------------------------------------------------------------*/

    // leptonica: pixOctreeQuantByPopulation (colorquant1.c)
    /// <summary>Adaptive octree quantization based on the population at a fixed level.</summary>
    /// <param name="level">significant bits for each of RGB; valid for {3,4}.
    /// Use 0 for default (level 4; recommended</param>
    /// <param name="ditherflag">1 to dither, 0 otherwise</param>
    /// <returns>pixd quantized to octcubes</returns>
    /// <remarks>
    /// The pix is 32 bpp rgb.
    /// (1) This color quantization method works very well without
    ///     dithering, using octcubes at two different levels:
    ///       (a) the input level, which is either 3 or 4
    ///       (b) level 2 (64 octcubes to cover the entire color space)
    /// (2) For best results, using level = 4 is recommended.
    /// (3) This first breaks up the color space into octcubes at the
    ///     input level, and computes, for each octcube, the average
    ///     value of the pixels that are in it.
    /// (4) Then there are two possible situations:
    ///       (a) If there are not more than 256 populated octcubes,
    ///           it returns a cmapped pix with those values assigned.
    ///       (b) Otherwise, it selects 192 octcubes containing the largest
    ///           number of pixels and quantizes pixels within those octcubes
    ///           to their average.  Then, to handle the residual pixels
    ///           that are not in those 192 octcubes, it generates a
    ///           level 2 octree consisting of 64 octcubes, and within
    ///           each octcube it quantizes the residual pixels to their
    ///           average within each of those level 2 octcubes.
    /// (5) Unpopulated level 2 octcubes are represented in the colormap
    ///     by their centers.  This, of course, has no effect unless
    ///     dithering is used for the output image.
    /// (6) The depth of pixd is the minimum required to support the
    ///     number of colors found at level; namely, 2, 4 or 8.
    /// (7) This function works particularly well on images such as maps,
    ///     where there are a relatively small number of well-populated
    ///     colors, but due to antialiasing and compression artifacts
    ///     there may be a large number of different colors.
    /// (8) The highest level of octcubes allowed is 4.
    /// (9) Dithering shows artifacts on some images.  If you plan to
    ///     dither, pixOctreeColorQuant() and pixFixedOctcubeQuant256()
    ///     usually give better results.
    /// </remarks>
    public Pix OctreeQuantByPopulation(int level, int ditherflag)
    {
        const string func = "pixOctreeQuantByPopulation";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (level == 0) level = 4;
        if (level < 3 || level > 4)
            throw Lept.Arg(func, "level not in {3,4}");

            /* Do not dither if image is very small */
        GetDimensions(out int w, out int h, out _);
        if (w < MinDitherSize && h < MinDitherSize && ditherflag == 1) {
            Log.Info(func, "Small image: dithering turned off\n");
            ditherflag = 0;
        }

        int size = OctcubeGetCount(level);  /* array size = 2 ** (3 * level) */
        MakeRGBToIndexTables(level, out uint[] rtab, out uint[] gtab, out uint[] btab);

        int[] narray = new int[size];
        int[] rarray = new int[size];
        int[] garray = new int[size];
        int[] barray = new int[size];

            /* Place the pixels in octcube leaves. */
        uint[] datas = Data;
        int wpls = Wpl;
        int rval, gval, bval;
        uint octindex;
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                octindex = rtab[rval] | gtab[gval] | btab[bval];
                narray[octindex]++;
                rarray[octindex] += rval;
                garray[octindex] += gval;
                barray[octindex] += bval;
            }
        }

            /* Find the number of different colors */
        int ncolors = 0;
        for (int i = 0; i < size; i++) {
            if (narray[i] > 0)
                ncolors++;
        }
        int depth;
        if (ncolors <= 4)
            depth = 2;
        else if (ncolors <= 16)
            depth = 4;
        else
            depth = 8;
        Pix pixd = Create(w, h, depth);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        PixColormap cmap = PixColormap.Create(depth);
        pixd.SetColormap(cmap);

            /* Average the colors in each octcube leaf. */
        for (int i = 0; i < size; i++) {
            if (narray[i] > 0) {
                rarray[i] /= narray[i];
                garray[i] /= narray[i];
                barray[i] /= narray[i];
            }
        }

            /* If ncolors <= 256, finish immediately.  Do not dither.
             * Re-use narray to hold the colormap index + 1  */
        if (ncolors <= 256) {
            int index = 0;
            for (int i = 0; i < size; i++) {
                if (narray[i] > 0) {
                    cmap.AddColor(rarray[i], garray[i], barray[i]);
                    narray[i] = index + 1;  /* to avoid storing 0 */
                    index++;
                }
            }

                /* Set the cmap indices for each pixel */
            for (int i = 0; i < h; i++) {
                int lines = i * wpls;
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                    octindex = rtab[rval] | gtab[gval] | btab[bval];
                    switch (depth)
                    {
                    case 8:
                        ArrayAccess.SetDataByte(lined, j, narray[octindex] - 1);
                        break;
                    case 4:
                        ArrayAccess.SetDataQbit(lined, j, narray[octindex] - 1);
                        break;
                    case 2:
                        ArrayAccess.SetDataDibit(lined, j, narray[octindex] - 1);
                        break;
                    default:
                        Log.Warning(func, "shouldn't get here\n");
                        break;
                    }
                }
            }
            return pixd;
        }

            /* More complicated.  Sort by decreasing population */
        L_Heap lh = L_Heap.Create(500, L.L_SORT_DECREASING);
        for (int i = 0; i < size; i++) {
            if (narray[i] > 0) {
                L_OctcubePop opop = new L_OctcubePop();
                opop.npix = (float)narray[i];
                opop.index = i;
                opop.rval = rarray[i];
                opop.gval = garray[i];
                opop.bval = barray[i];
                lh.Add(opop);
            }
        }

            /* Take the top 192.  These will form the first 192 colors
             * in the cmap.  iarray[i] holds the index into the cmap. */
        int[] iarray = new int[size];
        for (int i = 0; i < 192; i++) {
            L_OctcubePop? opop = (L_OctcubePop?)lh.Remove();
            if (opop == null) break;
            cmap.AddColor(opop.rval, opop.gval, opop.bval);
            iarray[opop.index] = i + 1;  /* +1 to avoid storing 0 */
        }

            /* Make the octindex tables for level 2, and reuse rarray, etc. */
        MakeRGBToIndexTables(2, out uint[] rtab2, out uint[] gtab2, out uint[] btab2);
        for (int i = 0; i < 64; i++) {
            narray[i] = 0;
            rarray[i] = 0;
            garray[i] = 0;
            barray[i] = 0;
        }

            /* Take the rest of the occupied octcubes, assigning the pixels
             * to these new colormap indices.  iarray[] is addressed
             * by level octcube indices, and it now holds the
             * colormap indices for all pixels in pixs.  */
        uint octindex2;
        for (int i = 192; i < size; i++) {
            L_OctcubePop? opop = (L_OctcubePop?)lh.Remove();
            if (opop == null) break;
            rval = opop.rval;
            gval = opop.gval;
            bval = opop.bval;
            octindex2 = rtab2[rval] | gtab2[gval] | btab2[bval];
            narray[octindex2] += (int)opop.npix;
            rarray[octindex2] += (int)opop.npix * rval;
            garray[octindex2] += (int)opop.npix * gval;
            barray[octindex2] += (int)opop.npix * bval;
            iarray[opop.index] = (int)(192 + octindex2 + 1);  /* +1 to avoid storing 0 */
        }

            /* To span the full color space, which is necessary for dithering,
             * set each iarray element whose value is still 0 at the input
             * level octcube leaves (because there were no pixels in those
             * octcubes) to the colormap index corresponding to its level 2
             * octcube. */
        if (ditherflag != 0) {
            for (int i = 0; i < size; i++) {
                if (iarray[i] == 0) {
                    GetRGBFromOctcube(i, level, out rval, out gval, out bval);
                    octindex2 = rtab2[rval] | gtab2[gval] | btab2[bval];
                    iarray[i] = (int)(192 + octindex2 + 1);
                }
            }
        }

            /* Average the colors from the residuals in each level 2 octcube,
             * and add these 64 values to the colormap. */
        for (int i = 0; i < 64; i++) {
            if (narray[i] > 0) {
                rarray[i] /= narray[i];
                garray[i] /= narray[i];
                barray[i] /= narray[i];
            } else {  /* no pixels in this octcube; use center value */
                GetRGBFromOctcube(i, 2, out rarray[i], out garray[i], out barray[i]);
            }
            cmap.AddColor(rarray[i], garray[i], barray[i]);
        }

            /* Set the cmap indices for each pixel.  Subtract 1 from
             * the value in iarray[] because we added 1 earlier.  */
        if (ditherflag == 0) {
            for (int i = 0; i < h; i++) {
                int lines = i * wpls;
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                    octindex = rtab[rval] | gtab[gval] | btab[bval];
                    ArrayAccess.SetDataByte(lined, j, iarray[octindex] - 1);
                }
            }
        } else {  /* dither */
            DitherOctindexWithCmap(this, pixd, rtab, gtab, btab, iarray, POP_DIF_CAP);
        }

        return pixd;
    }

    // leptonica: pixDitherOctindexWithCmap (colorquant1.c)
    /// <param name="pixs">32 bpp rgb</param>
    /// <param name="pixd">8 bpp cmapped</param>
    /// <param name="rtab">tables from rval to octindex</param>
    /// <param name="gtab">tables from gval to octindex</param>
    /// <param name="btab">tables from bval to octindex</param>
    /// <param name="indexmap">array mapping octindex to cmap index</param>
    /// <param name="difcap">max allowed dither transfer; use 0 for infinite cap</param>
    /// <remarks>
    /// (1) This performs dithering to generate the colormap indices
    ///     in pixd.  The colormap has been calculated, along with
    ///     four input LUTs that together give the inverse colormapping
    ///     from RGB to colormap index.
    /// (2) For pixOctreeQuantByPopulation(), indexmap maps from the
    ///     standard octindex to colormap index (after subtracting 1).
    ///     The basic pixel-level function, without dithering, is:
    ///        extractRGBValues(lines[j], &amp;rval, &amp;gval, &amp;bval);
    ///        octindex = rtab[rval] | gtab[gval] | btab[bval];
    ///        SET_DATA_BYTE(lined, j, indexmap[octindex] - 1);
    /// (3) This can be used in any situation where the general
    ///     prescription for finding the colormap index from the rgb
    ///     value is precisely this:
    ///        cmapindex = indexmap[rtab[rval] | gtab[gval] | btab[bval]] - 1
    ///     For example, in pixFixedOctcubeQuant256(), we don't use
    ///     standard octcube indexing, the rtab (etc) LUTs map directly
    ///     to the colormap index, and indexmap just compensates for
    ///     the 1-off indexing assumed to be in that table.
    /// </remarks>
    private static void DitherOctindexWithCmap(Pix pixs, Pix pixd, uint[] rtab, uint[] gtab,
                                               uint[] btab, int[] indexmap, int difcap)
    {
        const string func = "pixDitherOctindexWithCmap";

        if (pixs.Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        if (pixd.Depth != 8)
            throw Lept.Arg(func, "pixd undefined or not 8 bpp");
        PixColormap? cmap = pixd.Colormap;
        if (cmap == null)
            throw Lept.Arg(func, "pixd not cmapped");
        pixs.GetDimensions(out int w, out int h, out _);
        if (pixd.Width != w || pixd.Height != h)
            throw Lept.Arg(func, "pixs and pixd not same size");

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

        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
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
                uint octindex = rtab[rval] | gtab[gval] | btab[bval];
                int cmapindex = indexmap[octindex] - 1;
                ArrayAccess.SetDataByte(lined, j, cmapindex);
                cmap.GetColor(cmapindex, out int rc, out int gc, out int bc);

                DiffuseError(buf1r, buf2r, j, buf1r[j] / 8 - 8 * rc, difcap);
                DiffuseError(buf1g, buf2g, j, buf1g[j] / 8 - 8 * gc, difcap);
                DiffuseError(buf1b, buf2b, j, buf1b[j] / 8 - 8 * bc, difcap);
            }

                /* Get last pixel in row; no downward propagation */
            {
                int rval = buf1r[w - 1] / 64;
                int gval = buf1g[w - 1] / 64;
                int bval = buf1b[w - 1] / 64;
                uint octindex = rtab[rval] | gtab[gval] | btab[bval];
                int cmapindex = indexmap[octindex] - 1;
                ArrayAccess.SetDataByte(lined, w - 1, cmapindex);
            }
        }

            /* Get last row of pixels; no leftward propagation */
        {
            Span<uint> lined = datad.AsSpan((h - 1) * wpld, wpld);
            for (int j = 0; j < w; j++) {
                int rval = buf2r[j] / 64;
                int gval = buf2g[j] / 64;
                int bval = buf2b[j] / 64;
                uint octindex = rtab[rval] | gtab[gval] | btab[bval];
                int cmapindex = indexmap[octindex] - 1;
                ArrayAccess.SetDataByte(lined, j, cmapindex);
            }
        }
    }

    /*---------------------------------------------------------------------------*
     *         Adaptive octree quantization to 4 and 8 bpp with max colors       *
     *---------------------------------------------------------------------------*/

    // leptonica: pixOctreeQuantNumColors (colorquant1.c)
    /// <summary>Adaptive octree quantization to 4 or 8 bpp with a maximum number of colors.</summary>
    /// <param name="maxcolors">8 to 256; the actual number of colors used may be less than this</param>
    /// <param name="subsample">factor for computing color distribution; use 0 for default</param>
    /// <returns>pixd 4 or 8 bpp, colormapped</returns>
    /// <remarks>
    /// The pix is 32 bpp rgb.  This function is adaptive, but supports octcube
    /// leaves at only two depths: a smaller depth that guarantees full coverage of
    /// the color space and octcubes at one level deeper for more accurate colors.
    ///
    /// For 4 bpp, we have a minimum of 8 colors for the level 1 octcubes, plus up
    /// to 8 additional colors that are determined from the level 2 popularity.  If
    /// the number of colors is between 8 and 16, the output is a 4 bpp image.  If
    /// the number of colors is greater than 16, the output is a 8 bpp image.
    ///
    /// A priority queue, implemented with a heap, selects the requisite number of
    /// most populated octcubes at the deepest level (level 2 for 64 or fewer
    /// colors; level 3 for more than 64 colors).  These are combined with one color
    /// for each octcube one level above, which is used to span the color space of
    /// octcubes that were not included at the deeper level.
    ///
    ///     Number of colors      dest image depth      maxlevel
    ///     ----------------      ----------------      --------
    ///       8 to 16                  4 bpp               2
    ///       17 to 64                 8 bpp               2
    ///       65 to 256                8 bpp               3
    ///
    /// The colormap entries that correspond to color space regions that have no
    /// pixels in the source image are removed, and the average R, G and B color
    /// values in each region are stored in the colormap.
    /// </remarks>
    public Pix OctreeQuantNumColors(int maxcolors, int subsample)
    {
        const string func = "pixOctreeQuantNumColors";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (maxcolors < 8) {
            Log.Warning(func, "max colors < 8; setting to 8\n");
            maxcolors = 8;
        }
        if (maxcolors > 256) {
            Log.Warning(func, "max colors > 256; setting to 256\n");
            maxcolors = 256;
        }

        GetDimensions(out int w, out int h, out _);
        uint[] datas = Data;
        int wpls = Wpl;
        int minside = Math.Min(w, h);
        if (subsample <= 0) {
           subsample = Math.Max(1, minside / 200);
        }

        int bpp, maxlevel, ncubes, nbase, nextra;
        Pix pixd;
        if (maxcolors <= 16) {
            bpp = 4;
            pixd = Create(w, h, bpp);
            maxlevel = 2;
            ncubes = 64;   /* 2^6 */
            nbase = 8;
            nextra = maxcolors - nbase;
        } else if (maxcolors <= 64) {
            bpp = 8;
            pixd = Create(w, h, bpp);
            maxlevel = 2;
            ncubes = 64;  /* 2^6 */
            nbase = 8;
            nextra = maxcolors - nbase;
        } else {  /* maxcolors <= 256 */
            bpp = 8;
            pixd = Create(w, h, bpp);
            maxlevel = 3;
            ncubes = 512;  /* 2^9 */
            nbase = 64;
            nextra = maxcolors - nbase;
        }

        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);

        PixColormap cmap;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        int rval, gval, bval;
        OqCell[] oqca;

            /*----------------------------------------------------------*
             * If we're using the minimum number of colors, it is       *
             * much simpler.  We just use 'nbase' octcubes.             *
             * For this case, we don't eliminate any extra colors.      *
             *----------------------------------------------------------*/
        if (nextra == 0) {
                /* prepare the OctcubeQuantCell array */
            oqca = new OqCell[nbase];
            for (int i = 0; i < nbase; i++) {
                oqca[i] = new OqCell();
                oqca[i].n = 0.0f;
            }

            MakeRGBToIndexTables(maxlevel - 1, out uint[] rtab1, out uint[] gtab1, out uint[] btab1);

                /* Go through the entire image, gathering statistics and
                 * assigning pixels to their quantized value */
            for (int i = 0; i < h; i++) {
                int lines = i * wpls;
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                    uint index = GetOctcubeIndexFromRGB(rval, gval, bval, rtab1, gtab1, btab1);
                    if (bpp == 4)
                        ArrayAccess.SetDataQbit(lined, j, (int)index);
                    else  /* bpp == 8 */
                        ArrayAccess.SetDataByte(lined, j, (int)index);
                    oqca[index].n += 1.0f;
                    oqca[index].rcum += rval;
                    oqca[index].gcum += gval;
                    oqca[index].bcum += bval;
                }
            }

                /* Compute average color values in each octcube, and
                 * generate colormap */
            cmap = PixColormap.Create(bpp);
            pixd.SetColormap(cmap);
            for (int i = 0; i < nbase; i++) {
                OqCell oqc = oqca[i];
                if (oqc.n != 0) {
                    oqc.rval = (int)(oqc.rcum / oqc.n);
                    oqc.gval = (int)(oqc.gcum / oqc.n);
                    oqc.bval = (int)(oqc.bcum / oqc.n);
                } else {
                    GetRGBFromOctcube(i, maxlevel - 1, out oqc.rval, out oqc.gval, out oqc.bval);
                }
                cmap.AddColor(oqc.rval, oqc.gval, oqc.bval);
            }

            return pixd;
        }

            /*------------------------------------------------------------*
             * General case: we will use colors in octcubes at maxlevel.  *
             * We also remove any colors that are not populated from      *
             * the colormap.                                              *
             *------------------------------------------------------------*/
            /* Prepare the OctcubeQuantCell array */
        oqca = new OqCell[ncubes];
        for (int i = 0; i < ncubes; i++) {
            oqca[i] = new OqCell();
            oqca[i].n = 0.0f;
        }

            /* Make the tables to map color to the octindex,
             * of which there are 'ncubes' at 'maxlevel' */
        MakeRGBToIndexTables(maxlevel, out uint[] rtab, out uint[] gtab, out uint[] btab);

            /* Estimate the color distribution; we want to find the
             * most popular nextra colors at 'maxlevel' */
        for (int i = 0; i < h; i += subsample) {
            int lines = i * wpls;
            for (int j = 0; j < w; j += subsample) {
                ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                uint index = GetOctcubeIndexFromRGB(rval, gval, bval, rtab, gtab, btab);
                oqca[index].n += 1.0f;
                oqca[index].octindex = (int)index;
                oqca[index].rcum += rval;
                oqca[index].gcum += gval;
                oqca[index].bcum += bval;
            }
        }

            /* Transfer the OQCELL from the array, and order in a heap */
        L_Heap lh = L_Heap.Create(512, L.L_SORT_DECREASING);
        for (int i = 0; i < ncubes; i++)
            lh.Add(oqca[i]);

            /* Prepare a new OctcubeQuantCell array, with maxcolors cells  */
        oqca = new OqCell[maxcolors];
        for (int i = 0; i < nbase; i++) {  /* make nbase cells */
            oqca[i] = new OqCell();
            oqca[i].n = 0.0f;
        }

            /* Remove the nextra most populated ones, and put them in the array */
        for (int i = 0; i < nextra; i++) {
            OqCell oqc = (OqCell)lh.Remove()!;
            oqc.n = 0.0f;  /* reinit */
            oqc.rcum = 0;
            oqc.gcum = 0;
            oqc.bcum = 0;
            oqca[nbase + i] = oqc;  /* store it in the array */
        }

            /* Generate a lookup table from octindex at maxlevel
             * to color table index */
        int[] lut1 = new int[ncubes];
        for (int i = 0; i < nextra; i++)
            lut1[oqca[nbase + i].octindex] = nbase + i;
        for (int index = 0; index < ncubes; index++) {
            if (lut1[index] == 0)  /* not one of the extras; need to assign */
                lut1[index] = index >> 3;  /* remove the least significant bits */
        }

            /* Go through the entire image, gathering statistics and
             * assigning pixels to their quantized value */
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                uint index = GetOctcubeIndexFromRGB(rval, gval, bval, rtab, gtab, btab);
                int val = lut1[index];
                switch (bpp) {
                case 4:
                    ArrayAccess.SetDataQbit(lined, j, val);
                    break;
                case 8:
                    ArrayAccess.SetDataByte(lined, j, val);
                    break;
                default:
                    throw Lept.Arg(func, "bpp not 4 or 8!");
                }
                oqca[val].n += 1.0f;
                oqca[val].rcum += rval;
                oqca[val].gcum += gval;
                oqca[val].bcum += bval;
            }
        }

            /* Compute averages, set up a colormap, and make a second
             * lut that converts from the color values currently in
             * the image to a minimal set */
        int[] lut2 = new int[ncubes];
        cmap = PixColormap.Create(bpp);
        pixd.SetColormap(cmap);
        int cmindex = 0;
        for (int i = 0; i < maxcolors; i++) {
            OqCell oqc = oqca[i];
            lut2[i] = cmindex;
            if (oqc.n == 0)  /* no occupancy; don't bump up index */
                continue;
            oqc.rval = (int)(oqc.rcum / oqc.n);
            oqc.gval = (int)(oqc.gcum / oqc.n);
            oqc.bval = (int)(oqc.bcum / oqc.n);
            cmap.AddColor(oqc.rval, oqc.gval, oqc.bval);
            cmindex++;
        }
        int actualcolors = cmap.Count;

            /* Last time through the image; use the lookup table to
             * remap the pixel value to the minimal colormap */
        if (actualcolors < maxcolors) {
            for (int i = 0; i < h; i++) {
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    switch (bpp) {
                    case 4:
                        {
                            int val = ArrayAccess.GetDataQbit(lined, j);
                            ArrayAccess.SetDataQbit(lined, j, lut2[val]);
                        }
                        break;
                    case 8:
                        {
                            int val = ArrayAccess.GetDataByte(lined, j);
                            ArrayAccess.SetDataByte(lined, j, lut2[val]);
                        }
                        break;
                    }
                }
            }
        }

        return pixd;
    }

    /*-------------------------------------------------------------------------*
     *      Mixed color/gray quantization with specified number of colors      *
     *-------------------------------------------------------------------------*/

    // leptonica: pixOctcubeQuantMixedWithGray (colorquant1.c)
    /// <summary>Mixed color/gray quantization with a specified number of colors.</summary>
    /// <param name="depth">of output pix</param>
    /// <param name="graylevels">graylevels (must be &gt; 1)</param>
    /// <param name="delta">threshold for deciding if a pix is color or gray</param>
    /// <returns>pixd quantized to octcube and gray levels</returns>
    /// <remarks>
    /// The pix is 32 bpp rgb.
    /// (1) Generates a colormapped image, where the colormap table values
    ///     have two components: octcube values representing pixels with
    ///     color content, and grayscale values for the rest.
    /// (2) The threshold (delta) is the maximum allowable difference of
    ///     the max abs value of | r - g |, | r - b | and | g - b |.
    /// (3) The octcube values are the averages of all pixels that are
    ///     found in the octcube, and that are far enough from gray to
    ///     be considered color.  This can roughly be visualized as all
    ///     the points in the rgb color cube that are not within a "cylinder"
    ///     of diameter approximately 'delta' along the main diagonal.
    /// (4) We want to guarantee full coverage of the rgb color space; thus,
    ///     if the output depth is 4, the octlevel is 1 (2 x 2 x 2 = 8 cubes)
    ///     and if the output depth is 8, the octlevel is 2 (4 x 4 x 4
    ///     = 64 cubes).
    /// (5) Consequently, we have the following constraint on the number
    ///     of allowed gray levels: for 4 bpp, 8; for 8 bpp, 192.
    /// </remarks>
    public Pix OctcubeQuantMixedWithGray(int depth, int graylevels, int delta)
    {
        const string func = "pixOctcubeQuantMixedWithGray";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (graylevels < 2)
            throw Lept.Arg(func, "invalid graylevels");
        int octlevels, size;
        if (depth == 4) {
            octlevels = 1;
            size = 8;   /* 2 ** 3 */
            if (graylevels > 8)
                throw Lept.Arg(func, "max 8 gray levels");
        } else if (depth == 8) {
            octlevels = 2;
            size = 64;   /* 2 ** 6 */
            if (graylevels > 192)
                throw Lept.Arg(func, "max 192 gray levels");
        } else {
            throw Lept.Arg(func, "output depth not 4 or 8 bpp");
        }

            /* Make octcube index tables */
        MakeRGBToIndexTables(octlevels, out uint[] rtab, out uint[] gtab, out uint[] btab);

            /* Make octcube arrays for storing points in each cube */
        int[] carray = new int[size];
        int[] rarray = new int[size];
        int[] garray = new int[size];
        int[] barray = new int[size];

            /* Make lookup table, using computed thresholds  */
        int[] tabval = MakeGrayQuantIndexTable(graylevels);

            /* Make colormapped output pixd */
        GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, depth);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        PixColormap cmap = PixColormap.Create(depth);
        for (int j = 0; j < size; j++)  /* reserve octcube colors */
            cmap.AddColor(1, 1, 1);  /* a color that won't be used */
        for (int j = 0; j < graylevels; j++) {  /* set grayscale colors */
            int val = (255 * j) / (graylevels - 1);
            cmap.AddColor(val, val, val);
        }
        pixd.SetColormap(cmap);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;

            /* Go through src image: assign dest pixels to colormap values
             * and compute average colors in each occupied octcube */
        uint[] datas = Data;
        int wpls = Wpl;
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(datas[lines + j], out int rval, out int gval, out int bval);
                int del, midval;
                if (rval > gval) {
                    if (gval > bval) {   /* r > g > b */
                        del = rval - bval;
                        midval = gval;
                    } else if (rval > bval) {  /* r > b > g */
                        del = rval - gval;
                        midval = bval;
                    } else {  /* b > r > g */
                        del = bval - gval;
                        midval = rval;
                    }
                } else {  /* gval >= rval */
                    if (rval > bval) {  /* g > r > b */
                        del = gval - bval;
                        midval = rval;
                    } else if (gval > bval) {  /* g > b > r */
                        del = gval - rval;
                        midval = bval;
                    } else {  /* b > g > r */
                        del = bval - rval;
                        midval = gval;
                    }
                }
                if (del > delta) {  /* assign to color */
                    uint octindex = rtab[rval] | gtab[gval] | btab[bval];
                    carray[octindex]++;
                    rarray[octindex] += rval;
                    garray[octindex] += gval;
                    barray[octindex] += bval;
                    if (depth == 4)
                        ArrayAccess.SetDataQbit(lined, j, (int)octindex);
                    else  /* depth == 8 */
                        ArrayAccess.SetDataByte(lined, j, (int)octindex);
                } else {  /* assign to grayscale */
                    int val = size + tabval[midval];
                    if (depth == 4)
                        ArrayAccess.SetDataQbit(lined, j, val);
                    else  /* depth == 8 */
                        ArrayAccess.SetDataByte(lined, j, val);
                }
            }
        }

            /* Average the colors in each bin and reset the colormap */
        for (int i = 0; i < size; i++) {
            if (carray[i] > 0) {
                rarray[i] /= carray[i];
                garray[i] /= carray[i];
                barray[i] /= carray[i];
                cmap.ResetColor(i, rarray[i], garray[i], barray[i]);
            }
        }

        return pixd;
    }

    /*-------------------------------------------------------------------------*
     *             Fixed partition octcube quantization with 256 cells         *
     *-------------------------------------------------------------------------*/

    // leptonica: pixFixedOctcubeQuant256 (colorquant1.c)
    /// <summary>Simple 1-pass color quantization with 256 fixed cells.</summary>
    /// <param name="ditherflag">1 for dithering; 0 for no dithering</param>
    /// <returns>pixd 8 bit with colormap</returns>
    /// <remarks>
    /// The pix is 32 bpp; 24-bit color.
    /// This simple 1-pass color quantization works by breaking the
    /// color space into 256 pieces, with 3 bits quantized for each of
    /// red and green, and 2 bits quantized for blue.  We shortchange
    /// blue because the eye is least sensitive to blue.  This
    /// division of the color space is into two levels of octrees,
    /// followed by a further division by 4 not 8, where both
    /// blue octrees have been combined in the third level.
    ///
    /// The color map is generated from the 256 color centers by
    /// taking the representative color to be the center of the
    /// cell volume.  This gives a maximum error in the red and
    /// green values of 16 levels, and a maximum error in the
    /// blue sample of 32 levels.
    ///
    /// Each pixel in the 24-bit color image is placed in its containing
    /// cell, given by the relevant MSbits of the red, green and blue
    /// samples.  An error-diffusion dithering is performed on each
    /// color sample to give the appearance of good average local color.
    /// Dithering is required; without it, the contouring and visible
    /// color errors are very bad.  For speed and simplicity, we use
    /// integer dithering and propagate only to the right, down, and
    /// diagonally down-right, with ratios 3/8, 3/8 and 1/4, respectively.
    ///
    /// We use a simple mapping from the three 8 bit rgb samples
    /// to the 8 bit cell index; namely, r7 r6 r5 g7 g6 g5 b7 b6.
    /// This is not in an octcube format, but it doesn't matter.
    /// </remarks>
    public Pix FixedOctcubeQuant256(int ditherflag)
    {
        if (Depth != 32)
            throw Lept.Arg("pixFixedOctcubeQuant256", "pixs not 32 bpp");

            /* Do not dither if image is very small */
        GetDimensions(out int w, out int h, out _);
        if (w < MinDitherSize && h < MinDitherSize && ditherflag == 1) {
            Log.Info("pixFixedOctcubeQuant256", "Small image: dithering turned off\n");
            ditherflag = 0;
        }

            /* Find the centers of the 256 cells, each of which represents
             * the 3 MSBits of the red and green components, and the
             * 2 MSBits of the blue component.  This gives a mapping
             * from a "cube index" to the rgb values.  Save all 256
             * rgb values of these centers in a colormap.
             * For example, to get the red color of the cell center,
             * you take the 3 MSBits of to the index and add the
             * offset to the center of the cell, which is 0x10. */
        PixColormap cmap = PixColormap.Create(8);
        for (int cindex = 0; cindex < 256; cindex++) {
            int rv = (cindex & 0xe0) | 0x10;
            int gv = ((cindex << 3) & 0xe0) | 0x10;
            int bv = ((cindex << 6) & 0xc0) | 0x20;
            cmap.AddColor(rv, gv, bv);
        }

            /* Make output 8 bpp palette image */
        uint[] datas = Data;
        int wpls = Wpl;
        Pix pixd = Create(w, h, 8);
        pixd.SetColormap(cmap);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

            /* Set dest pix values to colortable indices */
        if (ditherflag == 0) {   /* no dithering */
            for (int i = 0; i < h; i++) {
                int lines = i * wpls;
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    ExtractRGBValues(datas[lines + j], out int rval, out int gval, out int bval);
                    byte index = (byte)((rval & 0xe0) | ((gval >> 3) & 0x1c) | (bval >> 6));
                    ArrayAccess.SetDataByte(lined, j, index);
                }
            }
        } else {  /* ditherflag == 1 */
                /* Set up conversion tables from rgb directly to the colormap
                 * index.  However, the dithering function expects these tables
                 * to generate an octcube index (+1), and the table itab[] to
                 * convert to the colormap index.  So we make a trivial
                 * itab[], that simply compensates for the -1 in
                 * pixDitherOctindexWithCmap().   No cap is required on
                 * the propagated difference.  */
            uint[] rtab = new uint[256];
            uint[] gtab = new uint[256];
            uint[] btab = new uint[256];
            int[] itab = new int[256];
            for (uint i = 0; i < 256; i++) {
                rtab[i] = i & 0xe0;
                gtab[i] = (i >> 3) & 0x1c;
                btab[i] = i >> 6;
                itab[i] = (int)i + 1;
            }
            DitherOctindexWithCmap(this, pixd, rtab, gtab, btab, itab, FIXED_DIF_CAP);
        }

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *           Nearly exact quantization for images with few colors            *
     *---------------------------------------------------------------------------*/

    // leptonica: pixFewColorsOctcubeQuant1 (colorquant1.c)
    /// <summary>Quantizes to the averages of the occupied octcubes, if there are not more than 256.</summary>
    /// <param name="level">significant bits for each of RGB; valid in [1...6]</param>
    /// <returns>pixd quantized to octcube, or null if there are more than 256 colors</returns>
    /// <remarks>
    /// The pix is 32 bpp rgb.
    /// (1) Generates a colormapped image, where the colormap table values
    ///     are the averages of all pixels that are found in the octcube.
    /// (2) This fails if there are more than 256 colors (i.e., more
    ///     than 256 occupied octcubes).
    /// (3) Often level 3 (512 octcubes) will succeed because not more
    ///     than half of them are occupied with 1 or more pixels.
    /// (4) The depth of the result, which is either 2, 4 or 8 bpp,
    ///     is the minimum required to hold the number of colors that
    ///     are found.
    /// (5) This can be useful for quantizing orthographically generated
    ///     images such as color maps, where there may be more than 256 colors
    ///     because of aliasing or jpeg artifacts on text or lines, but
    ///     there are a relatively small number of solid colors.  Then,
    ///     use with level = 3 can often generate a compact and accurate
    ///     representation of the original RGB image.  For this purpose,
    ///     it is better than pixFewColorsOctcubeQuant2(), because it
    ///     uses the average value of pixels in the octcube rather
    ///     than the first found pixel.  It is also simpler to use,
    ///     because it generates the histogram internally.
    /// </remarks>
    public Pix? FewColorsOctcubeQuant1(int level)
    {
        const string func = "pixFewColorsOctcubeQuant1";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (level < 1 || level > 6)
            throw Lept.Arg(func, "invalid level");

        int size = OctcubeGetCount(level);  /* array size = 2 ** (3 * level) */
        MakeRGBToIndexTables(level, out uint[] rtab, out uint[] gtab, out uint[] btab);

        int[] carray = new int[size];
        int[] rarray = new int[size];
        int[] garray = new int[size];
        int[] barray = new int[size];

            /* Place the pixels in octcube leaves. */
        GetDimensions(out int w, out int h, out _);
        uint[] datas = Data;
        int wpls = Wpl;
        int rval, gval, bval;
        uint octindex;
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                octindex = rtab[rval] | gtab[gval] | btab[bval];
                carray[octindex]++;
                rarray[octindex] += rval;
                garray[octindex] += gval;
                barray[octindex] += bval;
            }
        }

            /* Find the number of different colors */
        int ncolors = 0;
        for (int i = 0; i < size; i++) {
            if (carray[i] > 0)
                ncolors++;
        }
        if (ncolors > 256) {
            Log.Warning(func, $"{ncolors} colors found; more than 256\n");
            return null;
        }
        int depth;
        if (ncolors <= 4)
            depth = 2;
        else if (ncolors <= 16)
            depth = 4;
        else
            depth = 8;

            /* Average the colors in each octcube leaf and add to colormap table;
             * then use carray to hold the colormap index + 1  */
        PixColormap cmap = PixColormap.Create(depth);
        int index = 0;
        for (int i = 0; i < size; i++) {
            if (carray[i] > 0) {
                rarray[i] /= carray[i];
                garray[i] /= carray[i];
                barray[i] /= carray[i];
                cmap.AddColor(rarray[i], garray[i], barray[i]);
                carray[i] = index + 1;  /* to avoid storing 0 */
                index++;
            }
        }

        Pix pixd = Create(w, h, depth);
        pixd.SetColormap(cmap);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                octindex = rtab[rval] | gtab[gval] | btab[bval];
                switch (depth)
                {
                case 2:
                    ArrayAccess.SetDataDibit(lined, j, carray[octindex] - 1);
                    break;
                case 4:
                    ArrayAccess.SetDataQbit(lined, j, carray[octindex] - 1);
                    break;
                case 8:
                    ArrayAccess.SetDataByte(lined, j, carray[octindex] - 1);
                    break;
                default:
                    Log.Warning(func, "shouldn't get here\n");
                    break;
                }
            }
        }

        return pixd;
    }

    // leptonica: pixFewColorsOctcubeQuant2 (colorquant1.c)
    /// <summary>Quantizes to the first color found in each occupied octcube.</summary>
    /// <param name="level">of octcube indexing, for histogram: 3, 4, 5, 6</param>
    /// <param name="na">histogram of pixel occupation in octree leaves at given level</param>
    /// <param name="ncolors">number of occupied octree leaves at given level</param>
    /// <param name="pnerrors">num of pixels not exactly represented in the colormap
    /// (-1 if not computed)</param>
    /// <returns>pixd 2, 4 or 8 bpp with colormap</returns>
    /// <remarks>
    /// The pix is 32 bpp rgb.
    /// (1) Generates a colormapped image, where the colormap table values
    ///     are the colors of the first pixel found in each octcube.
    /// (2) This fails if there are more than 256 colors (i.e., more
    ///     than 256 occupied octcubes).
    /// (3) Often level 3 (512 octcubes) will succeed because not more
    ///     than half of them are occupied with 1 or more pixels.
    /// (4) For an image with not more than 256 colors, it is unlikely
    ///     that two pixels of different color will fall in the same
    ///     octcube at level = 4.   However it is possible, and this
    ///     function returns nerrors, the number of pixels
    ///     where, because more than one color is in the same octcube,
    ///     the pixel color is not exactly reproduced in the colormap.
    /// (5) This differs from pixFewColorsOctcubeQuant1(), which also
    ///     requires not more than 256 occupied leaves, but represents
    ///     the color of each leaf by an average over the pixels in
    ///     that leaf.  This also requires precomputing the histogram
    ///     of occupied octree leaves, which is generated using
    ///     pixOctcubeHistogram().
    /// (6) This is used in pixConvertRGBToColormap() for images that
    ///     are determined, by their histogram, to have relatively few
    ///     colors.
    /// </remarks>
    public Pix FewColorsOctcubeQuant2(int level, Numa na, int ncolors, out int pnerrors)
    {
        const string func = "pixFewColorsOctcubeQuant2";

        pnerrors = -1;   /* UNDEF */
        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (level < 3 || level > 6)
            throw Lept.Arg(func, "level not in {4, 5, 6}");
        if (ncolors > 256)
            throw Lept.Arg(func, "ncolors > 256");

            /* Represent the image with a set of leaf octcubes
             * at 'level', one for each color. */
        MakeRGBToIndexTables(level, out uint[] rtab, out uint[] gtab, out uint[] btab);

            /* The octarray will give a ptr from the octcube to the colorarray */
        int ncubes;
        if ((ncubes = na.Count) == 0)
            throw Lept.Fail(func, "no slots in pixel occupation histogram");
        int[] octarray = new int[ncubes];

            /* The colorarray will hold the colors of the first pixel
             * that lands in the leaf octcube.  After filling, it is
             * used to generate the colormap.  */
        uint[] colorarray = new uint[ncolors + 1];

            /* Determine the output depth from the number of colors */
        GetDimensions(out int w, out int h, out _);
        uint[] datas = Data;
        int wpls = Wpl;
        int depth;
        if (ncolors <= 4)
            depth = 2;
        else if (ncolors <= 16)
            depth = 4;
        else  /* ncolors <= 256 */
            depth = 8;

        Pix pixd = Create(w, h, depth);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

            /* For each pixel, get the octree index for its leaf octcube.
             * Check if a pixel has already been found in this octcube.
             *   ~ If not yet found, save that color in the colorarray
             *     and save the cindex in the octarray.
             *   ~ If already found, compare the pixel color with the
             *     color in the colorarray, and note if it differs.
             * Then set the dest pixel value to the cindex - 1, which
             * will be the cmap index for this color.  */
        int cindex = 1;  /* start with 1 */
        int nerrors = 0;
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                uint ppixel = datas[lines + j];
                ExtractRGBValues(ppixel, out int rval, out int gval, out int bval);
                uint octindex = rtab[rval] | gtab[gval] | btab[bval];
                int oval = octarray[octindex];
                if (oval == 0) {
                    octarray[octindex] = cindex;
                    colorarray[cindex] = ppixel;
                    SetPixelLow(lined, j, depth, (uint)(cindex - 1));
                    cindex++;
                } else {  /* already have seen this color; is it unique? */
                    SetPixelLow(lined, j, depth, (uint)(oval - 1));
                    if (colorarray[oval] != ppixel)
                        nerrors++;
                }
            }
        }
        pnerrors = nerrors;

            /* Make the colormap. */
        PixColormap cmap = PixColormap.Create(depth);
        for (int i = 0; i < ncolors; i++) {
            ExtractRGBValues(colorarray[i + 1], out int rval, out int gval, out int bval);
            cmap.AddColor(rval, gval, bval);
        }
        pixd.SetColormap(cmap);

        return pixd;
    }

    // leptonica: pixFewColorsOctcubeQuantMixed (colorquant1.c)
    /// <summary>Quantizes an image with few colors to octcubes for the colors and to gray levels for the grays.</summary>
    /// <param name="level">significant octcube bits for each of RGB;
    /// valid in [1...6]; use 0 for default</param>
    /// <param name="darkthresh">threshold near black; if the lightest component
    /// is below this, the pixel is not considered to be gray or color;
    /// uses 0 for default</param>
    /// <param name="lightthresh">threshold near white; if the darkest component
    /// is above this, the pixel is not considered to be gray or color;
    /// use 0 for default</param>
    /// <param name="diffthresh">thresh for the max difference between component
    /// values; for differences below this, the pixel is considered to be gray;
    /// use 0 for default</param>
    /// <param name="minfract">min fraction of pixels for gray histo bin;
    /// use 0.0 for default</param>
    /// <param name="maxspan">max size of gray histo bin; use 0 for default</param>
    /// <returns>pixd 8 bpp, quantized to octcube for pixels that are
    /// not gray; gray pixels are quantized separately over the full gray range</returns>
    /// <remarks>
    /// The pix is 32 bpp rgb.
    /// (1) First runs pixFewColorsOctcubeQuant1().  If this succeeds,
    ///     it separates the color from gray(ish) entries in the cmap,
    ///     and re-quantizes the gray pixels.  The result has some pixels
    ///     in color and others in gray.
    /// (2) This fails if there are more than 256 colors (i.e., more
    ///     than 256 occupied octcubes in the color quantization).
    /// (3) Level 3 (512 octcubes) will usually succeed because not more
    ///     than half of them are occupied with 1 or more pixels.
    /// (4) This uses the criterion from pixColorFraction() for deciding
    ///     if a colormap entry is color; namely, if the color components
    ///     are not too close to either black or white, and the maximum
    ///     difference between component values equals or exceeds a threshold.
    /// (5) For quantizing the gray pixels, it uses a histogram-based
    ///     method where input parameters determining the buckets are
    ///     the minimum population fraction and the maximum allowed size.
    /// (6) Recommended input parameters are:
    ///         level:  3 or 4  (3 is default)
    ///         darkthresh:  20
    ///         lightthresh: 244
    ///         diffthresh: 20
    ///         minfract: 0.05
    ///         maxspan: 15
    ///     These numbers are intended to be conservative (somewhat over-
    ///     sensitive) in color detection,  It's usually better to pay
    ///     extra with octcube quantization of a grayscale image than
    ///     to use grayscale quantization on an image that has some
    ///     actual color.  Input 0 on any of these to get the default.
    /// (7) This can be useful for quantizing orthographically generated
    ///     images such as color maps.  It usually gives results that are
    ///     better than pixOctcubeQuantMixedWithGray(), both in size and
    ///     appearance.  But it is a bit slower.
    /// </remarks>
    public Pix FewColorsOctcubeQuantMixed(int level, int darkthresh, int lightthresh,
                                          int diffthresh, float minfract, int maxspan)
    {
        const string func = "pixFewColorsOctcubeQuantMixed";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        if (level <= 0) level = 3;
        if (level > 6)
            throw Lept.Arg(func, "invalid level");
        if (darkthresh <= 0) darkthresh = 20;
        if (lightthresh <= 0) lightthresh = 244;
        if (diffthresh <= 0) diffthresh = 20;
        if (minfract <= 0.0) minfract = 0.05f;
        if (maxspan <= 2) maxspan = 15;

            /* Start with a simple fixed octcube quantizer. */
        Pix? pix1 = FewColorsOctcubeQuant1(level);
        if (pix1 == null)
            throw Lept.Fail(func, "too many colors");
        Pix pixc = pix1.ConvertTo8(true);  /* must be 8 bpp */

            /* Identify and save color entries in the colormap.  Set up a LUT
             * that returns -1 for any gray pixel. */
        PixColormap cmap = pixc.Colormap!;
        int ncolors = cmap.Count;
        PixColormap cmapd = PixColormap.Create(8);
        int[] lut = new int[256];
        for (int i = 0; i < 256; i++)
            lut[i] = -1;
        int index = 0;
        for (int i = 0; i < ncolors; i++) {
            cmap.GetColor(i, out int rval, out int gval, out int bval);
            int minval = Math.Min(rval, gval);
            minval = Math.Min(minval, bval);
            if (minval > lightthresh)  /* near white */
                continue;
            int maxval = Math.Max(rval, gval);
            maxval = Math.Max(maxval, bval);
            if (maxval < darkthresh)  /* near black */
                continue;

                /* Use the max diff between components to test for color */
            if (maxval - minval >= diffthresh) {
                cmapd.AddColor(rval, gval, bval);
                lut[i] = index;
                index++;
            }
        }

            /* Generate dest pix with just the color pixels set to their
             * colormap indices.  At the same time, make a 1 bpp mask
             * of the non-color pixels */
        GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 8);
        pixd.SetColormap(cmapd);
        Pix pixm = Create(w, h, 1);
        uint[] datac = pixc.Data;
        uint[] datam = pixm.Data;
        uint[] datad = pixd.Data;
        int wplc = pixc.Wpl;
        int wplm = pixm.Wpl;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> linec = datac.AsSpan(i * wplc, wplc);
            Span<uint> linem = datam.AsSpan(i * wplm, wplm);
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                int val = ArrayAccess.GetDataByte(linec, j);
                if (lut[val] == -1)
                    ArrayAccess.SetDataBit(linem, j);
                else
                    ArrayAccess.SetDataByte(lined, j, lut[val]);
            }
        }

            /* Fill in the gray values.  Use a grayscale version of pixs
             * as input, along with the mask over the actual gray pixels. */
        Pix pixg = ConvertTo8(false);
        GrayQuantFromHisto(pixd, pixg, pixm, minfract, maxspan);

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *           Fixed partition octcube quantization with RGB output            *
     *---------------------------------------------------------------------------*/

    // leptonica: pixFixedOctcubeQuantGenRGB (colorquant1.c)
    /// <summary>Quantizes to the centers of the octcubes at a given level, with rgb output.</summary>
    /// <param name="level">significant bits for each of r,g,b</param>
    /// <returns>pixd rgb; quantized to octcube centers</returns>
    /// <remarks>
    /// The pix is 32 bpp rgb.
    /// (1) Unlike the other color quantization functions, this one
    ///     generates an rgb image.
    /// (2) The pixel values are quantized to the center of each octcube
    ///     (at the specified level) containing the pixel.  They are
    ///     not quantized to the average of the pixels in that octcube.
    /// </remarks>
    public Pix FixedOctcubeQuantGenRGB(int level)
    {
        const string func = "pixFixedOctcubeQuantGenRGB";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (level < 1 || level > 6)
            throw Lept.Arg(func, "level not in {1,...6}");

        MakeRGBToIndexTables(level, out uint[] rtab, out uint[] gtab, out uint[] btab);

        GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 32);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        uint[] datas = Data;
        int wpls = Wpl;
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            int lined = i * wpld;
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(datas[lines + j], out int rval, out int gval, out int bval);
                uint octindex = rtab[rval] | gtab[gval] | btab[bval];
                GetRGBFromOctcube((int)octindex, level, out rval, out gval, out bval);
                ComposeRGBPixel(rval, gval, bval, out datad[lined + j]);
            }
        }

        return pixd;
    }

    /*------------------------------------------------------------------*
     *          Color quantize RGB image using existing colormap        *
     *------------------------------------------------------------------*/

    // leptonica: pixQuantFromCmap (colorquant1.c)
    /// <summary>Quantizes a grayscale or rgb image to a specified colormap.</summary>
    /// <param name="cmap">to quantize to; insert copy into dest pix</param>
    /// <param name="mindepth">minimum depth of pixd: can be 2, 4 or 8 bpp</param>
    /// <param name="level">of octcube used for finding nearest color in cmap</param>
    /// <param name="metric">L_MANHATTAN_DISTANCE, L_EUCLIDEAN_DISTANCE</param>
    /// <returns>pixd  2, 4 or 8 bpp, colormapped</returns>
    /// <remarks>
    /// The pix is 8 bpp grayscale without cmap, or 32 bpp rgb.
    /// (1) This is a top-level wrapper for quantizing either grayscale
    ///     or rgb images to a specified colormap.
    /// (2) The actual output depth is constrained by mindepth and
    ///     by the number of colors in cmap.
    /// (3) For grayscale, level and metric are ignored.
    /// (4) If the cmap has color and pixs is grayscale, the color is
    ///     removed from the cmap before quantizing pixs.
    /// </remarks>
    public Pix QuantFromCmap(PixColormap cmap, int mindepth, int level, int metric)
    {
        const string func = "pixQuantFromCmap";

        if (mindepth != 2 && mindepth != 4 && mindepth != 8)
            throw Lept.Arg(func, "invalid mindepth");
        int d = Depth;
        if (d == 8)
            return GrayQuantFromCmap(cmap, mindepth);
        else if (d == 32)
            return OctcubeQuantFromCmap(cmap, mindepth, level, metric);
        else
            throw Lept.Arg(func, "d not 8 or 32 bpp");
    }

    // leptonica: pixOctcubeQuantFromCmap (colorquant1.c)
    /// <summary>Quantizes an rgb image to a specified colormap, using a LUT from octcubes.</summary>
    /// <param name="cmap">to quantize to; insert copy into dest pix</param>
    /// <param name="mindepth">minimum depth of pixd: can be 2, 4 or 8 bpp</param>
    /// <param name="level">of octcube used for finding nearest color in cmap</param>
    /// <param name="metric">L_MANHATTAN_DISTANCE, L_EUCLIDEAN_DISTANCE</param>
    /// <returns>pixd  2, 4 or 8 bpp, colormapped</returns>
    /// <remarks>
    /// The pix is 32 bpp rgb.
    /// (1) In typical use, we are doing an operation, such as
    ///     interpolative scaling, on a colormapped pix, where it is
    ///     necessary to remove the colormap before the operation.
    ///     We then want to re-quantize the RGB result using the same
    ///     colormap.
    /// (2) The level is used to divide the color space into octcubes.
    ///     Each input pixel is, in effect, placed at the center of an
    ///     octcube at the given level, and it is mapped into the
    ///     exact color (given in the colormap) that is the closest
    ///     to that location.  We need to know that distance, for each color
    ///     in the colormap.  The higher the level of the octtree, the smaller
    ///     the octcubes in the color space, and hence the more accurately
    ///     we can determine the closest color in the colormap; however,
    ///     the size of the LUT, which is the total number of octcubes,
    ///     increases by a factor of 8 for each increase of 1 level.
    ///     Level 4 is the recommended minimum size to be used.
    /// (3) Typically you should use the Euclidean metric, because the
    ///     resulting voronoi cells (which are generated using the actual
    ///     colormap values as seeds) are convex for Euclidean distance
    ///     but not for Manhattan distance.
    /// (4) The depth of the output pixd is equal to the maximum of
    ///     (a) mindepth and (b) the minimum (2, 4 or 8 bpp) necessary
    ///     to hold the indices in the colormap.
    /// (5) We build a mapping table from octcube to colormap index so
    ///     that this function can run in a time (otherwise) independent
    ///     of the number of colors in the colormap.  This avoids a
    ///     brute-force search for the closest colormap color to each
    ///     pixel in the image.
    /// (6) This is similar to the function pixAssignToNearestColor()
    ///     used for color segmentation.
    /// (7) Except for very small images or when using level > 4,
    ///     it takes very little time to generate the tables,
    ///     compared to the generation of the colormapped dest pix.
    /// </remarks>
    public Pix OctcubeQuantFromCmap(PixColormap cmap, int mindepth, int level, int metric)
    {
        const string func = "pixOctcubeQuantFromCmap";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (mindepth != 2 && mindepth != 4 && mindepth != 8)
            throw Lept.Arg(func, "invalid mindepth");
        if (level < 1 || level > 6)
            throw Lept.Arg(func, "level not in {1...6}");
        if (metric != L.L_MANHATTAN_DISTANCE && metric != L.L_EUCLIDEAN_DISTANCE)
            throw Lept.Arg(func, "invalid metric");

            /* Set up the tables to map rgb to the nearest colormap index */
        MakeRGBToIndexTables(level, out uint[] rtab, out uint[] gtab, out uint[] btab);
        int[] cmaptab = cmap.ToOctcubeLUT(level, metric);

        return OctcubeQuantFromCmapLUT(cmap, mindepth, cmaptab, rtab, gtab, btab);
    }

    // leptonica: pixOctcubeQuantFromCmapLUT (colorquant1.c)
    /// <param name="cmap">to quantize to; insert copy into dest pix</param>
    /// <param name="mindepth">minimum depth of pixd: can be 2, 4 or 8 bpp</param>
    /// <param name="cmaptab">table mapping from octindex to colormap index</param>
    /// <param name="rtab">tables mapping from RGB to octindex</param>
    /// <param name="gtab">tables mapping from RGB to octindex</param>
    /// <param name="btab">tables mapping from RGB to octindex</param>
    /// <returns>pixd  2, 4 or 8 bpp, colormapped</returns>
    /// <remarks>
    /// (1) See the notes in the higher-level function
    ///     pixOctcubeQuantFromCmap().  The octcube level for
    ///     the generated octree is specified there, along with
    ///     the distance metric for determining the closest
    ///     color in the colormap to each octcube.
    /// (2) If the colormap, level and metric information have already
    ///     been used to construct the set of mapping tables,
    ///     this low-level function can be used directly (i.e.,
    ///     independently of pixOctcubeQuantFromCmap()) to build
    ///     a colormapped pix that uses the specified colormap.
    /// </remarks>
    private Pix OctcubeQuantFromCmapLUT(PixColormap cmap, int mindepth, int[] cmaptab,
                                        uint[] rtab, uint[] gtab, uint[] btab)
    {
        const string func = "pixOctcubeQuantFromCmapLUT";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (mindepth != 2 && mindepth != 4 && mindepth != 8)
            throw Lept.Arg(func, "invalid mindepth");

            /* Init dest pix (with minimum bpp depending on cmap) */
        int depth = cmap.MinDepth;
        depth = Math.Max(depth, mindepth);
        GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, depth);
        PixColormap cmapc = cmap.Copy();
        pixd.SetColormap(cmapc);
        pixd.CopyResolution(this);
        pixd.CopyInputFormat(this);

            /* Insert the colormap index of the color nearest to the input pixel */
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        int wpls = Wpl;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(datas[lines + j], out int rval, out int gval, out int bval);
                    /* Map from rgb to octcube index */
                uint octindex = GetOctcubeIndexFromRGB(rval, gval, bval, rtab, gtab, btab);
                    /* Map from octcube index to nearest colormap index */
                int index = cmaptab[octindex];
                if (depth == 2)
                    ArrayAccess.SetDataDibit(lined, j, index);
                else if (depth == 4)
                    ArrayAccess.SetDataQbit(lined, j, index);
                else  /* depth == 8 */
                    ArrayAccess.SetDataByte(lined, j, index);
            }
        }

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                       Generation of octcube histogram                     *
     *---------------------------------------------------------------------------*/

    // leptonica: pixOctcubeHistogram (colorquant1.c)
    /// <summary>Generates the histogram of the occupation of the octcubes at a level.</summary>
    /// <param name="level">significant bits for each of RGB; valid in [1...6]</param>
    /// <param name="pncolors">number of occupied cubes</param>
    /// <returns>numa histogram of color pixels</returns>
    /// <remarks>The pix is 32 bpp rgb.</remarks>
    public Numa OctcubeHistogram(int level, out int pncolors)
    {
        pncolors = 0;
        if (Depth != 32)
            throw Lept.Arg("pixOctcubeHistogram", "pixs not 32 bpp");

        GetDimensions(out int w, out int h, out _);
        int wpl = Wpl;
        uint[] data = Data;

        int size = OctcubeGetCount(level);  /* array size = 2 ** (3 * level) */
        MakeRGBToIndexTables(level, out uint[] rtab, out uint[] gtab, out uint[] btab);

        Numa na = Numa.Create(size);
        na.Count = size;
        float[] array = na.GetFArray(L.L_NOCOPY);

        for (int i = 0; i < h; i++) {
            int line = i * wpl;
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(data[line + j], out int rval, out int gval, out int bval);
                uint octindex = rtab[rval] | gtab[gval] | btab[bval];
                array[octindex] += 1.0f;
            }
        }

        int ncolors = 0;
        for (int i = 0; i < size; i++) {
            int val = na.GetIValue(i);
            if (val > 0)
                ncolors++;
        }
        pncolors = ncolors;

        return na;
    }

    /*------------------------------------------------------------------*
     *               Strip out unused elements in colormap              *
     *------------------------------------------------------------------*/

    // leptonica: pixRemoveUnusedColors (colorquant1.c)
    /// <summary>Strips out the unused elements of the colormap, renumbering the pixels.</summary>
    /// <remarks>
    /// The pix is colormapped.
    /// (1) This is an in-place operation.
    /// (2) If the image doesn't have a colormap, returns without error.
    /// (3) Unusued colors are removed from the colormap, and the
    ///     image pixels are re-numbered.
    /// </remarks>
    public void RemoveUnusedColors()
    {
        PixColormap? cmap = Colormap;
        if (cmap == null)
            return;

        int d = Depth;
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg("pixRemoveUnusedColors", "d not in {2, 4, 8}");

            /* Find which indices are actually used */
        int nc = cmap.Count;
        int[] histo = new int[nc];
        GetDimensions(out int w, out int h, out _);
        int wpls = Wpl;
        uint[] datas = Data;
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
            for (int j = 0; j < w; j++) {
                int val;
                switch (d)
                {
                case 2:
                    val = ArrayAccess.GetDataDibit(lines, j);
                    break;
                case 4:
                    val = ArrayAccess.GetDataQbit(lines, j);
                    break;
                default:  /* 8 */
                    val = ArrayAccess.GetDataByte(lines, j);
                    break;
                }
                if (val >= nc) {
                    Log.Warning("pixRemoveUnusedColors", "cmap index out of bounds!\n");
                    continue;
                }
                histo[val]++;
            }
        }

            /* Check if there are any zeroes.  If none, quit. */
        bool zerofound = false;
        for (int i = 0; i < nc; i++) {
            if (histo[i] == 0) {
                zerofound = true;
                break;
            }
        }
        if (!zerofound)
            return;

            /* Generate mapping tables between indices */
        int[] map1 = new int[nc];
        int[] map2 = new int[nc];
        int index = 0;
        for (int i = 0; i < nc; i++) {
            if (histo[i] != 0) {
                map1[index] = i;  /* get old index from new */
                map2[i] = index;  /* get new index from old */
                index++;
            }
        }

            /* Generate new colormap and attach to pixs */
        PixColormap cmapd = PixColormap.Create(d);
        for (int i = 0; i < index; i++) {
            cmap.GetColor(map1[i], out int rval, out int gval, out int bval);
            cmapd.AddColor(rval, gval, bval);
        }
        SetColormap(cmapd);

            /* Map pixel (index) values to new cmap */
        for (int i = 0; i < h; i++) {
            Span<uint> lines = datas.AsSpan(i * wpls, wpls);
            for (int j = 0; j < w; j++) {
                switch (d)
                {
                case 2:
                    ArrayAccess.SetDataDibit(lines, j, map2[ArrayAccess.GetDataDibit(lines, j)]);
                    break;
                case 4:
                    ArrayAccess.SetDataQbit(lines, j, map2[ArrayAccess.GetDataQbit(lines, j)]);
                    break;
                case 8:
                    ArrayAccess.SetDataByte(lines, j, map2[ArrayAccess.GetDataByte(lines, j)]);
                    break;
                }
            }
        }
    }

    /*------------------------------------------------------------------*
     *      Find number of occupied octcubes at the specified level     *
     *------------------------------------------------------------------*/

    // leptonica: pixNumberOccupiedOctcubes (colorquant1.c)
    /// <summary>Counts the occupied octcubes at the specified level.</summary>
    /// <param name="level">of octcube</param>
    /// <param name="mincount">minimum num pixels in an octcube to be counted; -1 to not use</param>
    /// <param name="minfract">minimum fract of pixels in an octcube to be counted; -1 to not use</param>
    /// <returns>number of occupied octcubes</returns>
    /// <remarks>
    /// The pix is 32 bpp.
    /// (1) Exactly one of (mincount, minfract) must be -1, so, e.g.,
    ///     if mincount == -1, then we use minfract.
    /// (2) If all occupied octcubes are to count, set mincount == 1.
    ///     Setting minfract == 0.0 is taken to mean the same thing.
    /// NOTE(upstream): with minfract &gt; 0, the minimum count is
    /// min(1, (int)(minfract * w * h)), that is, never more than 1.
    /// </remarks>
    public int NumberOccupiedOctcubes(int level, int mincount, float minfract)
    {
        const string func = "pixNumberOccupiedOctcubes";

        GetDimensions(out int w, out int h, out int d);
        if (d != 32)
            throw Lept.Arg(func, "pix not 32 bpp");
        if (level < 1 || level > 6)
            throw Lept.Arg(func, "invalid level");
        if ((mincount < 0 && minfract < 0) || (mincount >= 0.0 && minfract >= 0.0))
            throw Lept.Arg(func, "invalid mincount/minfract");
        if (mincount == 0 || minfract == 0.0)
            mincount = 1;
        else if (minfract > 0.0)
            mincount = Math.Min(1, (int)(minfract * w * h));

        int size = OctcubeGetCount(level);  /* array size = 2 ** (3 * level) */
        MakeRGBToIndexTables(level, out uint[] rtab, out uint[] gtab, out uint[] btab);
        int[] carray = new int[size];

            /* Mark the occupied octcube leaves */
        uint[] data = Data;
        int wpl = Wpl;
        for (int i = 0; i < h; i++) {
            int line = i * wpl;
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(data[line + j], out int rval, out int gval, out int bval);
                uint octindex = rtab[rval] | gtab[gval] | btab[bval];
                carray[octindex]++;
            }
        }

            /* Count them */
        int ncolors = 0;
        for (int i = 0; i < size; i++) {
            if (carray[i] >= mincount)
                ncolors++;
        }
        return ncolors;
    }
}

public sealed partial class PixColormap
{
    // leptonica: pixcmapToOctcubeLUT (colorquant1.c)
    /// <summary>Makes the table that maps each octcube at a level to the nearest colormap index.</summary>
    /// <param name="level">significant bits for each of RGB; valid in [1...6]</param>
    /// <param name="metric">L_MANHATTAN_DISTANCE, L_EUCLIDEAN_DISTANCE</param>
    /// <returns>tab[2**3 * level]</returns>
    /// <remarks>
    /// (1) This function is used to quickly find the colormap color
    ///     that is closest to any rgb color.  It is used to assign
    ///     rgb colors to an existing colormap.  It can be very expensive
    ///     to search through the entire colormap for the closest color
    ///     to each pixel.  Instead, we first set up this table, which is
    ///     populated by the colormap index nearest to each octcube
    ///     color.  Then we go through the image; for each pixel,
    ///     do two table lookups: first to generate the octcube index
    ///     from rgb and second to use this table to read out the
    ///     colormap index.
    /// (2) Do a slight modification for white and black.  For level = 4,
    ///     each octcube size is 16.  The center of the whitest octcube
    ///     is at (248, 248, 248), which is closer to 242 than 255.
    ///     Consequently, any gray color between 242 and 254 will
    ///     be selected, even if white (255, 255, 255) exists.  This is
    ///     typically not optimal, because the original color was
    ///     likely white.  Therefore, if white exists in the colormap,
    ///     use it for any rgb color that falls into the most white octcube.
    ///     Do the similar thing for black.
    /// (3) Here are the actual function calls for quantizing to a
    ///     specified colormap:
    ///       ~ first make the tables that map from rgb --> octcube index
    ///                makeRGBToIndexTables()
    ///       ~ then for each pixel:
    ///           * use the tables to get the octcube index
    ///                getOctcubeIndexFromRGB()
    ///           * use this table to get the nearest color in the colormap
    ///                cmap_index = tab[index]
    /// (4) Distance can be either manhattan or euclidean.
    /// (5) In typical use, level = 4 gives reasonable results, and
    ///     level = 5 is slightly better.
    /// </remarks>
    public int[] ToOctcubeLUT(int level, int metric)
    {
        const string func = "pixcmapToOctcubeLUT";

        if (level < 1 || level > 6)
            throw Lept.Arg(func, "level not in {1...6}");
        if (metric != L.L_MANHATTAN_DISTANCE && metric != L.L_EUCLIDEAN_DISTANCE)
            throw Lept.Arg(func, "invalid metric");

        int size = 1 << (3 * level);  /* array size = 2 ** (3 * level) */
        int[] tab = new int[size];

        int ncolors = Count;
        ToArrays(out int[] rmap, out int[] gmap, out int[] bmap, out _);

            /* Assign based on the closest octcube center to the cmap color */
        for (int i = 0; i < size; i++) {
            Pix.GetRGBFromOctcube(i, level, out int rval, out int gval, out int bval);
            int mindist = 1000000;
            int mincolor = 0;  /* irrelevant init */
            for (int k = 0; k < ncolors; k++) {
                int dist;
                if (metric == L.L_MANHATTAN_DISTANCE) {
                    dist = Math.Abs(rval - rmap[k]) + Math.Abs(gval - gmap[k]) +
                           Math.Abs(bval - bmap[k]);
                } else {  /* L_EUCLIDEAN_DISTANCE */
                    dist = (rval - rmap[k]) * (rval - rmap[k]) +
                           (gval - gmap[k]) * (gval - gmap[k]) +
                           (bval - bmap[k]) * (bval - bmap[k]);
                }
                if (dist < mindist) {
                    mindist = dist;
                    mincolor = k;
                }
            }
            tab[i] = mincolor;
        }

            /* Reset black and white if available in the colormap.
             * The darkest octcube is at octindex 0.
             * The lightest octcube is at the max octindex. */
        GetNearestIndex(0, 0, 0, out int index);
        GetColor(index, out int r, out int g, out int b);
        if (r < 7 && g < 7 && b < 7) {
            tab[0] = index;
        }
        GetNearestIndex(255, 255, 255, out index);
        GetColor(index, out r, out g, out b);
        if (r > 248 && g > 248 && b > 248) {
            tab[(1 << (3 * level)) - 1] = index;
        }

        return tab;
    }

}

public sealed partial class PixColormap
{
}
