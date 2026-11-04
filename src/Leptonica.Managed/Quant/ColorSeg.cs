using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Leptonica;

/*
 *  colorseg.c: Unsupervised color segmentation.
 *
 *    Unsupervised color segmentation
 *
 *               Pix.ColorSegment (pixColorSegment)
 *               Pix.ColorSegmentCluster (pixColorSegmentCluster)
 *       static  Pix.ColorSegmentTryCluster (pixColorSegmentTryCluster)
 *               Pix.AssignToNearestColor (pixAssignToNearestColor)
 *               Pix.ColorSegmentClean (pixColorSegmentClean)
 *               Pix.ColorSegmentRemoveColors (pixColorSegmentRemoveColors)
 */
public sealed partial class Pix
{
        /* Maximum allowed iterations in Phase 1. */
    private const int MAX_ALLOWED_ITERATIONS = 20;

        /* Factor by which max dist is increased on each iteration */
    private const float DIST_EXPAND_FACT = 1.3f;

        /* Octcube division level for computing nearest colormap color using LUT.
         * Using 4 should suffice for up to 50 - 100 colors, and it is
         * very fast.  Using 5 takes 8 times as long to set up the LUT
         * for little perceptual gain, even with 100 colors. */
    private const int LEVEL_IN_OCTCUBE = 4;

    /*------------------------------------------------------------------*
     *                 Unsupervised color segmentation                  *
     *------------------------------------------------------------------*/
    // leptonica: pixColorSegment (colorseg.c)
    /// <summary>Unsupervised color segmentation in four phases.</summary>
    /// <param name="maxdist">max euclidean dist to existing cluster</param>
    /// <param name="maxcolors">max number of colors allowed in first pass</param>
    /// <param name="selsize">linear size of sel for closing to remove noise</param>
    /// <param name="finalcolors">max number of final colors allowed after 4th pass</param>
    /// <param name="debugflag">1 for debug output; 0 otherwise</param>
    /// <returns>pixd 8 bit with colormap</returns>
    /// <remarks>
    ///  Color segmentation proceeds in four phases:
    ///
    ///  Phase 1:  pixColorSegmentCluster()
    ///  The image is traversed in raster order.  Each pixel either
    ///  becomes the representative for a new cluster or is assigned to an
    ///  existing cluster.  Assignment is greedy.  The data is stored in
    ///  a colormapped image.  Three auxiliary arrays are used to hold
    ///  the colors of the representative pixels, for fast lookup.
    ///  The average color in each cluster is computed.
    ///
    ///  Phase 2.  pixAssignToNearestColor()
    ///  A second non-greedy clustering pass is performed, where each pixel
    ///  is assigned to the nearest cluster average.  We also keep track
    ///  of how many pixels are assigned to each cluster.
    ///
    ///  Phase 3.  pixColorSegmentClean()
    ///  For each cluster, starting with the largest, do a morphological
    ///  closing to eliminate small components within larger ones.
    ///
    ///  Phase 4.  pixColorSegmentRemoveColors()
    ///  Eliminate all colors except the most populated 'finalcolors'.
    ///  Then remove unused colors from the colormap, and reassign those
    ///  pixels to the nearest remaining cluster, using the original pixel values.
    ///
    /// Notes:
    /// (1) The goal is to generate a small number of colors.
    ///     Typically this would be specified by 'finalcolors',
    ///     a number that would be somewhere between 3 and 6.
    ///     The parameter 'maxcolors' specifies the maximum number of
    ///     colors generated in the first phase.  This should be
    ///     larger than finalcolors, perhaps twice as large.
    ///     If more than 'maxcolors' are generated in the first phase
    ///     using the input 'maxdist', the distance is repeatedly
    ///     increased by a multiplicative factor until the condition
    ///     is satisfied.  The implicit relation between 'maxdist'
    ///     and 'maxcolors' is thus adjusted programmatically.
    /// (2) As a very rough guideline, given a target value of 'finalcolors',
    ///     here are approximate values of 'maxdist' and 'maxcolors'
    ///     to start with:
    ///
    ///          finalcolors    maxcolors    maxdist
    ///          -----------    ---------    -------
    ///              3             6          100
    ///              4             8           90
    ///              5            10           75
    ///              6            12           60
    ///
    ///     For a given number of finalcolors, if you use too many
    ///     maxcolors, the result will be noisy.  If you use too few,
    ///     the result will be a relatively poor assignment of colors.
    /// </remarks>
    public Pix ColorSegment(int maxdist, int maxcolors, int selsize, int finalcolors,
                            int debugflag)
    {
        const string func = "pixColorSegment";
        Pix pixs = this;
        if (pixs.Depth != 32)
            throw Lept.Arg(func, "must be rgb color");

            /* Phase 1; original segmentation */
        Pix pixd = pixs.ColorSegmentCluster(maxdist, maxcolors, debugflag);
        if (debugflag != 0) {
            Utils.LeptMkdir("lept/segment");
            pixd.WriteDebug("/tmp/lept/segment/colorseg1.png", L.IFF_PNG);
        }

            /* Phase 2; refinement in pixel assignment */
        int[] countarray = new int[256];
        pixd.AssignToNearestColor(pixs, null, LEVEL_IN_OCTCUBE, countarray);
        if (debugflag != 0)
            pixd.WriteDebug("/tmp/lept/segment/colorseg2.png", L.IFF_PNG);

            /* Phase 3: noise removal by separately closing each color */
        pixd.ColorSegmentClean(selsize, countarray);
        if (debugflag != 0)
            pixd.WriteDebug("/tmp/lept/segment/colorseg3.png", L.IFF_PNG);

            /* Phase 4: removal of colors with small population and
             * reassignment of pixels to remaining colors */
        pixd.ColorSegmentRemoveColors(pixs, finalcolors);
        return pixd;
    }

    // leptonica: pixColorSegmentCluster (colorseg.c)
    /// <summary>Phase 1 of color segmentation: greedy clustering.</summary>
    /// <param name="maxdist">max euclidean dist to existing cluster</param>
    /// <param name="maxcolors">max number of colors allowed in first pass</param>
    /// <param name="debugflag">1 for debug output; 0 otherwise</param>
    /// <returns>pixd 8 bit with colormap</returns>
    /// <remarks>
    /// (1) This is phase 1.  See description in pixColorSegment().
    /// (2) Greedy unsupervised classification.  If the limit 'maxcolors'
    ///     is exceeded, the computation is repeated with a larger
    ///     allowed cluster size.
    /// (3) On each successive iteration, 'maxdist' is increased by a
    ///     constant factor.  See comments in pixColorSegment() for
    ///     a guideline on parameter selection.
    ///     Note that the diagonal of the 8-bit rgb color cube is about
    ///     440, so for 'maxdist' = 440, you are guaranteed to get 1 color!
    /// </remarks>
    public Pix ColorSegmentCluster(int maxdist, int maxcolors, int debugflag)
    {
        const string func = "pixColorSegmentCluster";
        Pix pixs = this;
        if (pixs.Depth != 32)
            throw Lept.Arg(func, "must be rgb color");

        pixs.GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 8);
        PixColormap cmap = PixColormap.Create(8);
        pixd.SetColormap(cmap);
        pixd.CopyResolution(pixs);

        int newmaxdist = maxdist;
        int niters = 0;
        bool success = true;
        while (true) {
            bool ok = ColorSegmentTryCluster(pixd, pixs, newmaxdist, maxcolors, debugflag);
            niters++;
            if (ok) {
                int ncolors = cmap.Count;
                if (debugflag != 0)
                    Log.Info(func, CFormat.Sprintf("Success with %d colors after %d iters",
                                                   ncolors, niters));
                break;
            }
            if (niters == MAX_ALLOWED_ITERATIONS) {
                Log.Warning(func, CFormat.Sprintf("too many iters; newmaxdist = %d", newmaxdist));
                success = false;
                break;
            }
            newmaxdist = (int)(DIST_EXPAND_FACT * (float)newmaxdist);
        }

        if (!success)
            throw Lept.Fail(func, "failure in phase 1");

        return pixd;
    }

    // leptonica: pixColorSegmentTryCluster (colorseg.c)
    /// <returns>true if OK; false if more than maxcolors colors are needed</returns>
    /// <remarks>This function should only be called from pixColorSegCluster()</remarks>
    private static bool ColorSegmentTryCluster(Pix pixd, Pix pixs, int maxdist, int maxcolors,
                                               int debugflag)
    {
        const string func = "pixColorSegmentTryCluster";
        Span<int> rmap = stackalloc int[256];
        Span<int> gmap = stackalloc int[256];
        Span<int> bmap = stackalloc int[256];
        Span<int> countarray = stackalloc int[256];
        Span<int> rsum = stackalloc int[256];
        Span<int> gsum = stackalloc int[256];
        Span<int> bsum = stackalloc int[256];

        int w = pixs.Width;
        int h = pixs.Height;
        int maxdist2 = maxdist * maxdist;
        PixColormap cmap = pixd.Colormap!;
        cmap.Clear();

        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
        ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
        int ncolors = 0;
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            ref uint dstLineRef = ref Unsafe.Add(ref datadRef, i * wpld);
            for (int j = 0; j < w; j++) {
                uint ppixel = Unsafe.Add(ref datasRef, lines + j);
                int rval = (int)(ppixel >> 24);
                int gval = (int)((ppixel >> 16) & 0xffu);
                int bval = (int)((ppixel >> 8) & 0xffu);
                bool found = false;
                for (int k = 0; k < ncolors; k++) {
                    int dr = rval - rmap[k];
                    int dg = gval - gmap[k];
                    int db = bval - bmap[k];
                    if (dr * dr + dg * dg + db * db <= maxdist2) {
                        found = true;
                        ArrayAccess.SetDataByteUnchecked(ref dstLineRef, j, k);
                        countarray[k]++;
                        rsum[k] += rval;
                        gsum[k] += gval;
                        bsum[k] += bval;
                        break;
                    }
                }
                if (!found) {
                    int ret = cmap.AddNewColor(rval, gval, bval, out int index);
                    if (ret == 0 && index < maxcolors) {
                        ncolors = cmap.Count;
                        countarray[index] = 1;
                        ArrayAccess.SetDataByteUnchecked(ref dstLineRef, j, index);
                        rmap[index] = rval;
                        gmap[index] = gval;
                        bmap[index] = bval;
                        rsum[index] = rval;
                        gsum[index] = gval;
                        bsum[index] = bval;
                    } else {
                        if (debugflag != 0)
                            Log.Info(func, CFormat.Sprintf("maxcolors exceeded for maxdist = %d",
                                                           maxdist));
                        return false;
                    }
                }
            }
        }

            /* Replace the colors in the colormap by the averages */
        for (int k = 0; k < ncolors; k++) {
            int c = countarray[k];
            cmap.ResetColor(k, rsum[k] / c, gsum[k] / c, bsum[k] / c);
        }

        return true;
    }

    // leptonica: pixAssignToNearestColor (colorseg.c)
    /// <summary>Assigns each pixel to the nearest color of the colormap (in place).</summary>
    /// <param name="pixs">32 bpp; 24-bit color</param>
    /// <param name="pixm">[optional] 1 bpp</param>
    /// <param name="level">of octcube used for finding nearest color in cmap</param>
    /// <param name="countarray">[optional] array, in which we can store
    ///     the number of pixels found in each color in the colormap in pixd</param>
    /// <remarks>
    /// (1) This is used in phase 2 of color segmentation, where pixs
    ///     is the original input image to pixColorSegment(), and
    ///     pixd is the colormapped image returned from
    ///     pixColorSegmentCluster().  It is also used, with a mask,
    ///     in phase 4.
    /// (2) This is an in-place operation.
    /// (3) The colormap in pixd is unchanged.
    /// (4) pixs and pixd must be the same size (w, h).
    /// (5) The selection mask pixm can be null.  If it exists, it must
    ///     be the same size as pixs and pixd, and only pixels
    ///     corresponding to fg in pixm are assigned.  Set to
    ///     NULL if all pixels in pixd are to be assigned.
    /// (6) The countarray can be null.  If it exists, it is pre-allocated
    ///     and of a size at least equal to the size of the colormap in pixd.
    /// (7) This does a best-fit (non-greedy) assignment of pixels to
    ///     existing clusters.  Specifically, it assigns each pixel
    ///     in pixd to the color index in the pixd colormap that has a
    ///     color closest to the corresponding rgb pixel in pixs.
    /// (8) 'level' is the octcube level used to quickly find the nearest
    ///     color in the colormap for each pixel.  For color segmentation,
    ///     this parameter is set to LEVEL_IN_OCTCUBE.
    /// (9) We build a mapping table from octcube to colormap index so
    ///     that this function can run in a time (otherwise) independent
    ///     of the number of colors in the colormap.  This avoids a
    ///     brute-force search for the closest colormap color to each
    ///     pixel in the image.
    /// </remarks>
    public void AssignToNearestColor(Pix pixs, Pix? pixm, int level, int[]? countarray)
    {
        const string func = "pixAssignToNearestColor";
        Pix pixd = this;
        PixColormap cmap = pixd.Colormap ?? throw Lept.Arg(func, "cmap not found");
        if (pixs.Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (level < 1 || level > 6)
            throw Lept.Arg(func, "level not in [1 ... 6]");

            /* Set up the tables to map rgb to the nearest colormap index */
        MakeRGBToIndexTables(level, out uint[] rtab, out uint[] gtab, out uint[] btab);
        int[] cmaptab = cmap.ToOctcubeLUT(level, L.L_MANHATTAN_DISTANCE);

        pixs.GetDimensions(out int w, out int h, out _);
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        uint[]? datam = pixm?.Data;
        int wplm = pixm?.Wpl ?? 0;
        int w4 = w & ~3;
        object? countLock = countarray != null ? new object() : null;

        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            ref uint rtabRef = ref MemoryMarshal.GetArrayDataReference(rtab);
            ref uint gtabRef = ref MemoryMarshal.GetArrayDataReference(gtab);
            ref uint btabRef = ref MemoryMarshal.GetArrayDataReference(btab);
            ref int cmaptabRef = ref MemoryMarshal.GetArrayDataReference(cmaptab);
            Span<int> localCounts = countarray != null ? stackalloc int[256] : Span<int>.Empty;
            if (!localCounts.IsEmpty) localCounts.Clear();

            for (int i = yStart; i < yEnd; i++) {
                int lines = i * wpls;
                ref uint dstLineRef = ref Unsafe.Add(ref datadRef, i * wpld);
                if (datam == null) {
                    int j = 0;
                    for (; j < w4; j += 4) {
                        uint p0 = Unsafe.Add(ref datasRef, lines + j);
                        uint p1 = Unsafe.Add(ref datasRef, lines + j + 1);
                        uint p2 = Unsafe.Add(ref datasRef, lines + j + 2);
                        uint p3 = Unsafe.Add(ref datasRef, lines + j + 3);
                        int c0 = Unsafe.Add(ref cmaptabRef, (int)(Unsafe.Add(ref rtabRef, (int)(p0 >> 24)) | Unsafe.Add(ref gtabRef, (int)((p0 >> 16) & 0xffu)) | Unsafe.Add(ref btabRef, (int)((p0 >> 8) & 0xffu))));
                        int c1 = Unsafe.Add(ref cmaptabRef, (int)(Unsafe.Add(ref rtabRef, (int)(p1 >> 24)) | Unsafe.Add(ref gtabRef, (int)((p1 >> 16) & 0xffu)) | Unsafe.Add(ref btabRef, (int)((p1 >> 8) & 0xffu))));
                        int c2 = Unsafe.Add(ref cmaptabRef, (int)(Unsafe.Add(ref rtabRef, (int)(p2 >> 24)) | Unsafe.Add(ref gtabRef, (int)((p2 >> 16) & 0xffu)) | Unsafe.Add(ref btabRef, (int)((p2 >> 8) & 0xffu))));
                        int c3 = Unsafe.Add(ref cmaptabRef, (int)(Unsafe.Add(ref rtabRef, (int)(p3 >> 24)) | Unsafe.Add(ref gtabRef, (int)((p3 >> 16) & 0xffu)) | Unsafe.Add(ref btabRef, (int)((p3 >> 8) & 0xffu))));
                        if (!localCounts.IsEmpty) {
                            localCounts[c0]++;
                            localCounts[c1]++;
                            localCounts[c2]++;
                            localCounts[c3]++;
                        }
                        Unsafe.Add(ref dstLineRef, j >> 2) = (uint)((c0 << 24) | (c1 << 16) | (c2 << 8) | c3);
                    }
                    for (; j < w; j++) {
                        uint p = Unsafe.Add(ref datasRef, lines + j);
                        int c = Unsafe.Add(ref cmaptabRef, (int)(Unsafe.Add(ref rtabRef, (int)(p >> 24)) | Unsafe.Add(ref gtabRef, (int)((p >> 16) & 0xffu)) | Unsafe.Add(ref btabRef, (int)((p >> 8) & 0xffu))));
                        if (!localCounts.IsEmpty) localCounts[c]++;
                        ArrayAccess.SetDataByteUnchecked(ref dstLineRef, j, c);
                    }
                } else {
                    ReadOnlySpan<uint> linem = datam.AsSpan(i * wplm, wplm);
                    for (int j = 0; j < w; j++) {
                        if (ArrayAccess.GetDataBit(linem, j) == 0)
                            continue;
                        uint p = Unsafe.Add(ref datasRef, lines + j);
                        int c = Unsafe.Add(ref cmaptabRef, (int)(Unsafe.Add(ref rtabRef, (int)(p >> 24)) | Unsafe.Add(ref gtabRef, (int)((p >> 16) & 0xffu)) | Unsafe.Add(ref btabRef, (int)((p >> 8) & 0xffu))));
                        if (!localCounts.IsEmpty) localCounts[c]++;
                        ArrayAccess.SetDataByteUnchecked(ref dstLineRef, j, c);
                    }
                }
            }

            if (countLock != null) {
                lock (countLock) {
                    int lim = Math.Min(countarray!.Length, 256);
                    for (int k = 0; k < lim; k++)
                        countarray[k] += localCounts[k];
                }
            }
        });
    }

    // leptonica: pixColorSegmentClean (colorseg.c)
    /// <summary>Phase 3 of color segmentation: closes each color, starting with the largest (in place).</summary>
    /// <param name="selsize">for closing</param>
    /// <param name="countarray">array containing the number of pixels
    ///     found in each color in the colormap</param>
    /// <remarks>
    /// (1) This operation is in-place.
    /// (2) This is phase 3 of color segmentation.  It is the first
    ///     part of a two-step noise removal process.  Colors with a
    ///     large population are closed first; this operation absorbs
    ///     small sets of intercolated pixels of a different color.
    /// </remarks>
    public void ColorSegmentClean(int selsize, int[] countarray)
    {
        const string func = "pixColorSegmentClean";
        Pix pixs = this;
        if (pixs.Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        PixColormap cmap = pixs.Colormap ?? throw Lept.Arg(func, "cmap not found");
        if (selsize <= 1)
            return;  /* nothing to do */

            /* Sort colormap indices in decreasing order of pixel population */
        int ncolors = cmap.Count;
        Numa na = Numa.Create(ncolors);
        for (int i = 0; i < ncolors; i++)
            na.AddNumber(countarray[i]);
        Numa nasi = na.GetSortIndex(L.L_SORT_DECREASING);

            /* For each color, in order of decreasing population,
             * do a closing and absorb the added pixels.  Note that
             * if the closing removes pixels at the border, they'll
             * still appear in the xor and will be properly (re)set. */
        for (int i = 0; i < ncolors; i++) {
            int val = nasi.GetIValue(i);
            Pix pixt1 = pixs.GenerateMaskByValue(val, true);
            Pix pixt2 = CloseSafeCompBrick(null, pixt1, selsize, selsize);
            Xor(pixt2, pixt2, pixt1);  /* pixels to be added to type 'val' */
            uint val32 = cmap.GetColor32(val);
            pixs.SetMasked(pixt2, val32);  /* add them */
        }
    }

    // leptonica: pixColorSegmentRemoveColors (colorseg.c)
    /// <summary>Phase 4 of color segmentation: keeps only the most populated colors (in place).</summary>
    /// <param name="pixs">32 bpp rgb, with initial pixel values</param>
    /// <param name="finalcolors">max number of colors to retain</param>
    /// <remarks>
    /// (1) This operation is in-place.
    /// (2) This is phase 4 of color segmentation, and the second part
    ///     of the 2-step noise removal.  Only 'finalcolors' different
    ///     colors are retained, with colors with smaller populations
    ///     being replaced by the nearest color of the remaining colors.
    ///     For highest accuracy, for pixels that are being replaced,
    ///     we find the nearest colormap color  to the original rgb color.
    /// </remarks>
    public void ColorSegmentRemoveColors(Pix pixs, int finalcolors)
    {
        const string func = "pixColorSegmentRemoveColors";
        Pix pixd = this;
        if (pixd.Depth != 8)
            throw Lept.Arg(func, "pixd not 8 bpp");
        PixColormap cmap = pixd.Colormap ?? throw Lept.Arg(func, "cmap not found");
        int ncolors = cmap.Count;
        if (finalcolors >= ncolors)  /* few enough colors already; nothing to do */
            return;

            /* Generate a mask over all pixels that are not in the
             * 'finalcolors' most populated colors.  Save the colormap
             * index of any one of the retained colors in 'tempindex'.
             * The LUT has values 0 for the 'finalcolors' most populated colors,
             * which will be retained; and 1 for the rest, which are marked
             * by fg pixels in pixm and will be removed. */
        Numa na = pixd.GetCmapHistogram(1);
        Numa nasi = na.GetSortIndex(L.L_SORT_DECREASING);
        int tempindex = nasi.GetIValue(finalcolors - 1);  /* retain down to this */
        uint tempcolor = cmap.GetColor32(tempindex);  /* use this color */
        int[] tab = new int[256];
        for (int i = finalcolors; i < ncolors; i++) {
            int index = nasi.GetIValue(i);
            tab[index] = 1;
        }

        Pix pixm = pixd.MakeMaskFromLUT(tab);

            /* Reassign the masked pixels temporarily to the saved index
             * (tempindex).  This guarantees that no pixels are labeled by
             * a colormap index of any colors that will be removed.
             * The actual value doesn't matter, as long as it's one
             * of the retained colors, because these pixels will later
             * be reassigned based on the full set of colors retained
             * in the colormap. */
        pixd.SetMasked(pixm, tempcolor);

            /* Now remove unused colors from the colormap.  This reassigns
             * image pixels as required. */
        pixd.RemoveUnusedColors();

            /* Finally, reassign the pixels under the mask (those that were
             * given a 'tempindex' value) to the nearest color in the colormap.
             * This is the function used in phase 2 on all image pixels; here
             * it is only used on the masked pixels given by pixm. */
        pixd.AssignToNearestColor(pixs, pixm, LEVEL_IN_OCTCUBE, null);
    }
}
