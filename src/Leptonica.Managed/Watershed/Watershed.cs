using System;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  watershed.c: Watershed transform of an 8 bpp image.
 *
 *      Top-level
 *            wshedCreate()
 *            wshedApply()
 *      Helpers
 *            identifyWatershedBasin()
 *            mergeLookup()
 *            wshedGetHeight()
 *            pushNewPixel()
 *            popNewPixel()
 *            pushWSPixel()
 *            popWSPixel()
 *            debugPrintLUT()
 *            debugWshedMerge()
 *      Output
 *            wshedBasins()
 *            wshedRenderFill()
 *            wshedRenderColors()
 *
 *  The watershed function identifies the "catch basins" of the input
 *  8 bpp image, with respect to the specified seeds or "markers".
 *  The use is in segmentation, but the selection of the markers is
 *  critical to getting meaningful results.
 *
 *  How are the markers selected?  You can't simply use the local
 *  minima, because a typical image has sufficient noise so that
 *  a useful catch basin can easily have multiple local minima.  However
 *  they are selected, the question for the watershed function is
 *  how to handle local minima that are not markers.  The reason
 *  this is important is because of the algorithm used to find the
 *  watersheds, which is roughly like this:
 *
 *    (1) Identify the markers and the local minima, and enter them
 *        into a priority queue based on the pixel value.  Each marker
 *        is shrunk to a single pixel, if necessary, before the
 *        operation starts.
 *
 *    (2) Feed the priority queue with neighbors of pixels that are
 *        popped off the queue.  Each of these queue pixels is labeled
 *        with the index value of its parent.
 *
 *    (3) Each pixel is also labeled, in a 32-bit image, with the marker
 *        or local minimum index, from which it was originally derived.
 *
 *    (4) There are actually 3 classes of labels: seeds, minima, and
 *        fillers.  The fillers are labels of regions that have already
 *        been identified as watersheds and are continuing to fill, for
 *        the purpose of finding higher watersheds.
 *
 *    (5) When a pixel is popped that has already been labeled in the
 *        32-bit image and that label differs from the label of its
 *        parent (stored in the queue pixel), a boundary has been crossed.
 *        There are several cases:
 *         (a) Both parents are derived from markers but at least one
 *             is not deep enough to become a watershed.  Absorb the
 *             shallower basin into the deeper one, fixing the LUT to
 *             redirect the shallower index to the deeper one.
 *         (b) Both parents are derived from markers and both are deep
 *             enough.  Identify and save the watershed for each marker.
 *         (c) One parent was derived from a marker and the other from
 *             a minima: absorb the minima basin into the marker basin.
 *         (d) One parent was derived from a marker and the other is
 *             a filler: identify and save the watershed for the marker.
 *         (e) Both parents are derived from minima: merge them.
 *         (f) One parent is a filler and the other is derived from a
 *             minima: merge the minima into the filler.
 *
 *    (6) The output of the watershed operation consists of:
 *         ~ a pixa of the basins
 *         ~ a pta of the markers
 *         ~ a numa of the watershed levels
 *
 *  Typical usage:
 *      L_WShed wshed = L_WShed.Create(pixs, pixseed, mindepth, 0);
 *      wshed.Apply();
 *      wshed.Basins(out Pixa pixa, out Numa nalevels);
 *        ... do something with pixa, nalevels ...
 *      Pix pixd = wshed.RenderFill();
 *
 *  Differences from C:
 *    - L_WShed is a class; wshedCreate() is L_WShed.Create(), the other
 *      functions are members. The line pointer arrays (lines8, linem1,
 *      linelab32, linet1) are not stored: the pixel data arrays are
 *      accessed with the line offset.
 *    - The storage stacks that C uses to reuse the L_NEWPIXEL and
 *      L_WSPIXEL structs are not needed: new objects are allocated.
 *    - wshedDestroy() is not ported (the GC owns the objects).
 *    - wshedBasins() returns the pixa and numa as out parameters; both
 *      are shared (cloned) with the L_WShed, as in C.
 *    - The wshed field nother is never set in C (see UPSTREAM_NOTES.md);
 *      this is kept.
 *    - The debug output goes to stderr.
 *
 *  Not ported: wshedDestroy().
 */

/// <summary>Watershed state (struct L_WShed).</summary>
public sealed class L_WShed
{
    private const uint MAX_LABEL_VALUE = 0x7fffffff;  /* largest l_int32 */

    /// <summary>Pixel in the priority queue (L_WSPIXEL).</summary>
    private sealed class WSPixel : IHeapItem
    {
        public float val;    /* pixel value */
        public int x;        /* x coordinate */
        public int y;        /* y coordinate */
        public int index;    /* label for set to which pixel belongs */

        public float HeapKey => val;
    }

    /// <summary>Clone of input 8 bpp pixs.</summary>
    public Pix Pixs { get; private set; } = null!;
    /// <summary>Clone of input 1 bpp seed (marker) pixm.</summary>
    public Pix Pixm { get; private set; } = null!;
    /// <summary>Minimum depth allowed for a watershed.</summary>
    public int MinDepth { get; private set; }
    /// <summary>32 bpp label pix.</summary>
    public Pix Pixlab { get; private set; } = null!;
    /// <summary>Scratch pix for computing wshed regions.</summary>
    public Pix Pixt { get; private set; } = null!;
    /// <summary>Result: 1 bpp pixa of watersheds.</summary>
    public Pixa? Pixad { get; private set; }
    /// <summary>Pta of initial seed pixels.</summary>
    public Pta? Ptas { get; private set; }
    /// <summary>Numa of seed indicators; 0 if completed.</summary>
    public Numa? Nasi { get; private set; }
    /// <summary>Numa of initial seed heights.</summary>
    public Numa? Nash { get; private set; }
    /// <summary>Numa of initial minima heights.</summary>
    public Numa? Namh { get; private set; }
    /// <summary>Result: numa of watershed levels.</summary>
    public Numa? Nalevels { get; private set; }
    /// <summary>Number of seeds (markers).</summary>
    public int Nseeds { get; private set; }
    /// <summary>Number of minima different from seeds (never set in C).</summary>
    public int Nother { get; private set; }
    /// <summary>Lut for pixel indices.</summary>
    public int[]? Lut { get; private set; }
    /// <summary>Back-links into lut, for updates.</summary>
    public Numa?[]? Links { get; private set; }
    /// <summary>Size of links array.</summary>
    public int ArraySize { get; private set; }
    /// <summary>Set to 1 for debug output.</summary>
    public int Debug { get; set; }

    private L_WShed()
    {
    }

    /*-----------------------------------------------------------------------*
     *                        Top-level watershed                            *
     *-----------------------------------------------------------------------*/
    // leptonica: wshedCreate (watershed.c)
    /// <summary>Creates a watershed state.</summary>
    /// <param name="pixs">8 bpp source</param>
    /// <param name="pixm">1 bpp 'marker' seed</param>
    /// <param name="mindepth">minimum depth; anything less is not saved</param>
    /// <param name="debugflag">1 for debug output</param>
    /// <returns>WShed</returns>
    /// <remarks>
    /// (1) It is not necessary for the fg pixels in the seed image
    ///     be at minima, or that they be isolated.  We extract a
    ///     single pixel from each connected component, and a seed
    ///     anywhere in a watershed will eventually label the watershed
    ///     when the filling level reaches it.
    /// (2) Set mindepth to some value to ignore noise in pixs that
    ///     can create small local minima.  Any watershed shallower
    ///     than mindepth, even if it has a seed, will not be saved;
    ///     It will either be incorporated in another watershed or
    ///     eliminated.
    /// </remarks>
    public static L_WShed Create(Pix pixs, Pix pixm, int mindepth, int debugflag)
    {
        const string func = "wshedCreate";

        if (pixs.Depth != 8)
            throw Lept.Arg(func, "pixs is not 8 bpp");
        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm is not 1 bpp");
        pixs.GetDimensions(out int w, out int h, out _);
        if (pixm.Width != w || pixm.Height != h)
            throw Lept.Arg(func, "pixs/m sizes are unequal");

        L_WShed wshed = new L_WShed();
        wshed.Pixs = Pix.Clone(pixs);
        wshed.Pixm = Pix.Clone(pixm);
        wshed.MinDepth = Math.Max(1, mindepth);
        wshed.Pixlab = Pix.Create(w, h, 32);
        wshed.Pixlab.SetAllArbitrary(MAX_LABEL_VALUE);
        wshed.Pixt = Pix.Create(w, h, 1);
        wshed.Debug = debugflag;
        return wshed;
    }

    // leptonica: wshedApply (watershed.c)
    /// <summary>Applies the watershed transform.</summary>
    /// <remarks>
    /// (1) N.B. This is buggy!  It seems to locate watersheds that are
    ///     duplicates.  The watershed extraction after complete fill
    ///     grabs some regions belonging to existing watersheds.
    ///     See prog/watershedtest.c for testing.
    /// </remarks>
    public void Apply()
    {
        const string two_new_watersheds = "Two new watersheds";
        const string seed_absorbed_into_seeded_basin = "Seed absorbed into seeded basin";
        const string one_new_watershed_label = "One new watershed (label)";
        const string one_new_watershed_index = "One new watershed (index)";
        const string minima_absorbed_into_seeded_basin = "Minima absorbed into seeded basin";
        const string minima_absorbed_by_filler_or_another = "Minima absorbed by filler or another";
        L_WShed wshed = this;
        int i, j, x, y, val, index, clabel, cindex;
        int hindex, hlabel, hmin, hmax, minhindex, maxhindex;

        /* ------------------------------------------------------------ *
         *  Initialize priority queue and pixlab with seeds and minima  *
         * ------------------------------------------------------------ */
        L_Heap lh = L_Heap.Create(0, L.L_SORT_INCREASING);  /* remove lowest values first */

        wshed.Pixs.GetDimensions(out int w, out int h, out _);
        uint[] dataGray = wshed.Pixs.Data;  /* lines8 */
        int wpls = wshed.Pixs.Wpl;
        uint[] dataLab = wshed.Pixlab.Data;  /* linelab32 */
        int wpllab = wshed.Pixlab.Wpl;

        /* Identify seed (marker) pixels, 1 for each c.c. in pixm */
        wshed.Pixs.SelectMinInConnComp(wshed.Pixm, out Pta ptas, out Numa nash);
        Pix pixsd = Pix.GenerateFromPta(ptas, w, h);
        int nseeds = ptas.Count;
        for (i = 0; i < nseeds; i++) {
            ptas.GetIPt(i, out x, out y);
            uint uval = (uint)GetDataByte(dataGray.AsSpan(y * wpls, wpls), x);
            pushWSPixel(lh, (int)uval, x, y, i);
        }
        wshed.Ptas = ptas;
        Numa nasi = Numa.MakeConstant(1, nseeds);  /* indicator array */
        wshed.Nasi = nasi;
        wshed.Nash = nash;
        wshed.Nseeds = nseeds;

        /* Identify minima that are not seeds.  Use these 4 steps:
         *  (1) Get the local minima, which can have components
         *      of arbitrary size.  This will be a clipping mask.
         *  (2) Get the image of the actual seeds (pixsd)
         *  (3) Remove all elements of the clipping mask that have a seed.
         *  (4) Shrink each of the remaining elements of the minima mask
         *      to a single pixel.  */
        wshed.Pixs.LocalExtrema(200, 0, out Pix pixmin, out _);
        Pix.RemoveSeededComponents(pixmin, pixsd, pixmin, 8, 2);
        wshed.Pixs.SelectMinInConnComp(pixmin, out Pta ptao, out Numa namh);
        int nother = ptao.Count;
        for (i = 0; i < nother; i++) {
            ptao.GetIPt(i, out x, out y);
            uint uval = (uint)GetDataByte(dataGray.AsSpan(y * wpls, wpls), x);
            pushWSPixel(lh, (int)uval, x, y, nseeds + i);
        }
        wshed.Namh = namh;

        /* ------------------------------------------------------------ *
         *                Initialize merging lookup tables              *
         * ------------------------------------------------------------ */
        /* nalut should always give the current after-merging index.
         * links are effectively backpointers: they are numas associated with
         * a dest index of all indices in nalut that point to that index. */
        int mindepth = wshed.MinDepth;
        int nboth = nseeds + nother;
        int arraysize = 2 * nboth;
        wshed.ArraySize = arraysize;
        Numa nalut = Numa.MakeSequence(0, 1, arraysize);
        int[] lut = nalut.GetIArray();
        wshed.Lut = lut;  /* wshed owns this */
        Numa?[] links = new Numa?[arraysize];
        wshed.Links = links;  /* wshed owns this */
        int nindex = nseeds + nother;  /* the next unused index value */

        /* ------------------------------------------------------------ *
         *              Fill the basins, using the priority queue       *
         * ------------------------------------------------------------ */
        Pixa pixad = Pixa.Create(nseeds);
        wshed.Pixad = pixad;  /* wshed owns this */
        Numa nalevels = Numa.Create(nseeds);
        wshed.Nalevels = nalevels;  /* wshed owns this */

        Log.Info("wshedApply", $"nseeds = {nseeds}, nother = {nother}");
        while (lh.Count > 0) {
            popWSPixel(lh, out val, out x, out y, out index);
            uint ulabel = dataLab[y * wpllab + x];
            if (ulabel == MAX_LABEL_VALUE)
                clabel = (int)ulabel;
            else
                clabel = lut[ulabel];
            cindex = lut[index];
            if (clabel == cindex) continue;  /* have already seen this one */

            if (clabel == (int)MAX_LABEL_VALUE) {  /* new one; assign index and try to
                                                    * propagate to all neighbors */
                dataLab[y * wpllab + x] = (uint)cindex;
                int imin = Math.Max(0, y - 1);
                int imax = Math.Min(h - 1, y + 1);
                int jmin = Math.Max(0, x - 1);
                int jmax = Math.Min(w - 1, x + 1);
                for (i = imin; i <= imax; i++) {
                    for (j = jmin; j <= jmax; j++) {
                        if (i == y && j == x) continue;
                        uint uval = (uint)GetDataByte(dataGray.AsSpan(i * wpls, wpls), j);
                        pushWSPixel(lh, (int)uval, j, i, cindex);
                    }
                }
            } else {  /* pixel is already labeled (differently); must resolve */
                /* If both indices are seeds, check if the min height is
                 * greater than mindepth.  If so, we have two new watersheds;
                 * locate them and assign to both regions a new index
                 * for further waterfill.  If not, absorb the shallower
                 * watershed into the deeper one and continue filling it. */
                pixsd.GetPixel(x, y, out _);
                if (clabel < nseeds && cindex < nseeds) {
                    hlabel = wshedGetHeight(val, clabel);
                    hindex = wshedGetHeight(val, cindex);
                    hmin = Math.Min(hlabel, hindex);
                    hmax = Math.Max(hlabel, hindex);
                    if (hmin == hmax) {
                        hmin = hlabel;
                        hmax = hindex;
                    }
                    if (wshed.Debug != 0) {
                        Console.Error.WriteLine($"clabel,hlabel = {clabel},{hlabel}");
                        Console.Error.WriteLine($"hmin = {hmin}, hmax = {hmax}");
                        Console.Error.WriteLine($"cindex,hindex = {cindex},{hindex}");
                        if (hmin < mindepth)
                            Console.Error.WriteLine("Too shallow!");
                    }
                    if (hmin >= mindepth) {
                        debugWshedMerge(two_new_watersheds, x, y, clabel, cindex);
                        wshedSaveBasin(cindex, val - 1);
                        wshedSaveBasin(clabel, val - 1);
                        nasi.SetValue(cindex, 0);
                        nasi.SetValue(clabel, 0);
                        if (wshed.Debug != 0) Console.Error.WriteLine($"nindex = {nindex}");
                        debugPrintLUT(lut, nindex, wshed.Debug);
                        mergeLookup(clabel, nindex);
                        debugPrintLUT(lut, nindex, wshed.Debug);
                        mergeLookup(cindex, nindex);
                        debugPrintLUT(lut, nindex, wshed.Debug);
                        nindex++;
                    } else {  /* extraneous seed within seeded basin; absorb */
                        debugWshedMerge(seed_absorbed_into_seeded_basin, x, y, clabel, cindex);
                    }
                    maxhindex = clabel;  /* TODO: is this part of above 'else'? */
                    minhindex = cindex;
                    if (hindex > hlabel) {
                        maxhindex = cindex;
                        minhindex = clabel;
                    }
                    mergeLookup(minhindex, maxhindex);
                } else if (clabel < nseeds && cindex >= nboth) {
                    /* If one index is a seed and the other is a merge of
                     * 2 watersheds, generate a single watershed. */
                    debugWshedMerge(one_new_watershed_label, x, y, clabel, cindex);
                    wshedSaveBasin(clabel, val - 1);
                    nasi.SetValue(clabel, 0);
                    mergeLookup(clabel, cindex);
                } else if (cindex < nseeds && clabel >= nboth) {
                    debugWshedMerge(one_new_watershed_index, x, y, clabel, cindex);
                    wshedSaveBasin(cindex, val - 1);
                    nasi.SetValue(cindex, 0);
                    mergeLookup(cindex, clabel);
                } else if (clabel < nseeds) {  /* cindex from minima; absorb */
                    /* If one index is a seed and the other is from a minimum,
                     * merge the minimum wshed into the seed wshed. */
                    debugWshedMerge(minima_absorbed_into_seeded_basin, x, y, clabel, cindex);
                    mergeLookup(cindex, clabel);
                } else if (cindex < nseeds) {  /* clabel from minima; absorb */
                    debugWshedMerge(minima_absorbed_into_seeded_basin, x, y, clabel, cindex);
                    mergeLookup(clabel, cindex);
                } else {  /* If neither index is a seed, just merge */
                    debugWshedMerge(minima_absorbed_by_filler_or_another, x, y, clabel, cindex);
                    mergeLookup(clabel, cindex);
                }
            }
        }
    }

    /*-----------------------------------------------------------------------*
     *                               Helpers                                 *
     *-----------------------------------------------------------------------*/
    // leptonica: wshedSaveBasin (watershed.c)
    /// <summary>Identifies a single watershed and saves it.</summary>
    /// <param name="index">index of basin to be located</param>
    /// <param name="level">filling level reached at the time this function is called</param>
    /// <remarks>
    /// (1) This identifies a single watershed.  It does not change
    ///     the LUT, which must be done subsequently.
    /// (2) The fill level of a basin is taken to be level - 1.
    /// </remarks>
    private void wshedSaveBasin(int index, int level)
    {
        identifyWatershedBasin(index, level, out Box box, out Pix pix);
        Pixad!.AddPix(pix, L.L_INSERT);
        Pixad.AddBox(box, L.L_INSERT);
        Nalevels!.AddNumber(level - 1);
    }

    // leptonica: identifyWatershedBasin (watershed.c)
    /// <summary>Finds the pixels of a basin: label equal to index and value less than level.</summary>
    /// <param name="index">index of basin to be located</param>
    /// <param name="level">of basin at point at which the two basins met</param>
    /// <param name="pbox">bounding box of basin</param>
    /// <param name="ppixd">pix of basin, cropped to its bounding box</param>
    /// <remarks>
    /// (1) This is a static function, so we assume pixlab, pixs and pixt
    ///     exist and are the same size.
    /// (2) It selects all pixels that have the label index in pixlab
    ///     and that have a value in pixs that is less than level.
    /// (3) It is used whenever two seeded basins meet (typically at a saddle),
    ///     or when one seeded basin meets a 'filler'.  All identified
    ///     basins are saved as a watershed.
    /// </remarks>
    private void identifyWatershedBasin(int index, int level, out Box pbox, out Pix ppixd)
    {
        /* Make a queue */
        L_Queue<NewPixel> lq = L_Queue<NewPixel>.Create(0);

        Pix pixs = Pixs;
        Pix pixt = Pixt;
        uint[] dataGray = pixs.Data;   /* lines8 */
        int wpls = pixs.Wpl;
        uint[] dataLab = Pixlab.Data;  /* linelab32 */
        int wpllab = Pixlab.Wpl;
        uint[] datat = pixt.Data;      /* linet1 */
        int wplt = pixt.Wpl;
        int[] lut = Lut!;
        pixs.GetDimensions(out int w, out int h, out _);

        /* Prime the queue with the seed pixel for this watershed. */
        int minx = 1000000, miny = 1000000;
        int maxx = 0, maxy = 0;
        Ptas!.GetIPt(index, out int x, out int y);
        pixt.SetPixel(x, y, 1);
        pushNewPixel(lq, x, y, ref minx, ref maxx, ref miny, ref maxy);
        if (Debug != 0) Console.Error.WriteLine($"prime: (x,y) = ({x}, {y})");

        /* Each pixel in a spreading breadth-first search is inspected.
         * It is accepted as part of this watershed, and pushed on
         * the search queue, if:
         *     (1) It has a label value equal to index
         *     (2) The pixel value is less than level, the overflow
         *         height at which the two basins join.
         *     (3) It has not yet been seen in this search.  */
        while (lq.Count > 0) {
            popNewPixel(lq, ref x, ref y);
            int imin = Math.Max(0, y - 1);
            int imax = Math.Min(h - 1, y + 1);
            int jmin = Math.Max(0, x - 1);
            int jmax = Math.Min(w - 1, x + 1);
            for (int i = imin; i <= imax; i++) {
                for (int j = jmin; j <= jmax; j++) {
                    if (j == x && i == y) continue;  /* parent */
                    uint label = dataLab[i * wpllab + j];
                    if (label == MAX_LABEL_VALUE || lut[label] != index) continue;
                    uint bval = (uint)GetDataBit(datat.AsSpan(i * wplt, wplt), j);
                    if (bval == 1) continue;  /* already seen */
                    uint lval = (uint)GetDataByte(dataGray.AsSpan(i * wpls, wpls), j);
                    if (lval >= level) continue;  /* too high */
                    SetDataBit(datat.AsSpan(i * wplt, wplt), j);
                    pushNewPixel(lq, j, i, ref minx, ref maxx, ref miny, ref maxy);
                }
            }
        }

        /* Extract the box and pix, and clear pixt */
        int bw = maxx - minx + 1;
        int bh = maxy - miny + 1;
        Box box = Box.Create(minx, miny, bw, bh);
        Pix pixd = pixt.ClipRectangle(box, out _)!;
        pixt.Rasterop(minx, miny, bw, bh, L.PIX_SRC ^ L.PIX_DST, pixd, 0, 0);

        pbox = box;
        ppixd = pixd;
    }

    // leptonica: mergeLookup (watershed.c)
    /// <summary>Merges the lut and backlinks after a merge of two indices.</summary>
    /// <param name="sindex">primary index being changed in the merge</param>
    /// <param name="dindex">index that sindex will point to after the merge</param>
    /// <remarks>
    /// (1) The links are a sparse array of Numas showing current back-links.
    ///     The lut gives the current index (of the seed or the minima
    ///     for the wshed) in which it is located.
    /// (2) Think of each entry in the lut.  There are two types:
    ///        owner:     lut[index] = index
    ///        redirect:  lut[index] != index
    /// (3) This is called each time a merge occurs.  It puts the lut
    ///     and backlinks in a canonical form after the merge, where
    ///     all entries in the lut point to the current "owner", which
    ///     has all backlinks.  That is, every "redirect" in the lut
    ///     points to an "owner".  The lut always gives the index of
    ///     the current owner.
    /// </remarks>
    private void mergeLookup(int sindex, int dindex)
    {
        int size = ArraySize;
        if (sindex < 0 || sindex >= size)
            throw Lept.Arg("mergeLookup", "invalid sindex");
        if (dindex < 0 || dindex >= size)
            throw Lept.Arg("mergeLookup", "invalid dindex");

        /* Redirect links in the lut */
        int n = 0;
        Numa?[] links = Links!;
        int[] lut = Lut!;
        Numa? na;
        if ((na = links[sindex]) != null) {
            n = na.Count;
            for (int i = 0; i < n; i++) {
                int index = na.GetIValue(i);
                lut[index] = dindex;
            }
        }
        lut[sindex] = dindex;

        /* Shift the backlink arrays from sindex to dindex.
         * sindex should have no backlinks because all entries in the
         * lut that were previously pointing to it have been redirected
         * to dindex. */
        if (links[dindex] == null)
            links[dindex] = Numa.Create(n);
        links[dindex]!.Join(links[sindex], 0, -1);
        links[dindex]!.AddNumber(sindex);
        links[sindex] = null;
    }

    // leptonica: wshedGetHeight (watershed.c)
    /// <summary>Finds the height of the current pixel above its seed or minimum in the watershed.</summary>
    /// <param name="val">value of current pixel popped off queue</param>
    /// <param name="label">of pixel or 32 bpp label image</param>
    /// <returns>height of current value from seed or minimum of watershed</returns>
    /// <remarks>
    /// (1) It is only necessary to find the height for a watershed
    ///     that is indexed by a seed or a minima.  This function should
    ///     not be called on a finished watershed (that continues to fill).
    /// </remarks>
    private int wshedGetHeight(int val, int label)
    {
        int minval;
        if (label < Nseeds)
            minval = Nash!.GetIValue(label);
        else if (label < Nseeds + Nother)
            minval = Namh!.GetIValue(label);
        else
            throw Lept.Fail("wshedGetHeight", "finished watershed; should not call");
        return val - minval;
    }

    /// <summary>New pixel coordinates (L_NEWPIXEL).</summary>
    private sealed class NewPixel
    {
        public int x;
        public int y;
    }

    // leptonica: pushNewPixel (watershed.c)
    /// <remarks>
    /// (1) This is a wrapper for adding a NewPixel to a queue, which
    ///     updates the bounding box for all pixels on that queue.
    /// </remarks>
    private static void pushNewPixel(L_Queue<NewPixel> lq, int x, int y,
                                     ref int pminx, ref int pmaxx, ref int pminy, ref int pmaxy)
    {
        /* Adjust bounding box */
        pminx = Math.Min(pminx, x);
        pmaxx = Math.Max(pmaxx, x);
        pminy = Math.Min(pminy, y);
        pmaxy = Math.Max(pmaxy, y);

        lq.Add(new NewPixel { x = x, y = y });
    }

    // leptonica: popNewPixel (watershed.c)
    /// <remarks>
    /// (1) This is a wrapper for removing a NewPixel from a queue,
    ///     which returns the pixel coordinates.
    /// </remarks>
    private static void popNewPixel(L_Queue<NewPixel> lq, ref int px, ref int py)
    {
        NewPixel? np = lq.Remove();
        if (np == null)
            return;
        px = np.x;
        py = np.y;
    }

    // leptonica: pushWSPixel (watershed.c)
    /// <param name="lh">priority queue</param>
    /// <param name="val">pixel value: used for ordering the heap</param>
    /// <param name="x">pixel coordinates</param>
    /// <param name="y">pixel coordinates</param>
    /// <param name="index">label for set to which pixel belongs</param>
    /// <remarks>
    /// (1) This is a wrapper for adding a WSPixel to a heap.
    /// </remarks>
    private static void pushWSPixel(L_Heap lh, int val, int x, int y, int index)
    {
        WSPixel wsp = new WSPixel();
        wsp.val = (float)val;
        wsp.x = x;
        wsp.y = y;
        wsp.index = index;
        lh.Add(wsp);
    }

    // leptonica: popWSPixel (watershed.c)
    /// <remarks>
    /// (1) This is a wrapper for removing a WSPixel from a heap,
    ///     which returns the WSPixel data.
    /// </remarks>
    private static void popWSPixel(L_Heap lh, out int pval, out int px, out int py, out int pindex)
    {
        pval = 0;
        px = 0;
        py = 0;
        pindex = 0;
        WSPixel? wsp = (WSPixel?)lh.Remove();
        if (wsp == null)
            return;
        pval = (int)wsp.val;
        px = wsp.x;
        py = wsp.y;
        pindex = wsp.index;
    }

    // leptonica: debugPrintLUT (watershed.c)
    private static void debugPrintLUT(int[] lut, int size, int debug)
    {
        if (debug == 0) return;
        Console.Error.Write("lut: ");
        for (int i = 0; i < size; i++)
            Console.Error.Write($"{lut[i]} ");
        Console.Error.WriteLine();
    }

    // leptonica: debugWshedMerge (watershed.c)
    private void debugWshedMerge(string descr, int x, int y, int label, int index)
    {
        if (Debug == 0)
            return;

        Console.Error.WriteLine($"{descr}:");
        Console.Error.WriteLine($"   (x, y) = ({x}, {y})");
        Console.Error.WriteLine($"   clabel = {label}, cindex = {index}");
    }

    /*-----------------------------------------------------------------------*
     *                                 Output                                *
     *-----------------------------------------------------------------------*/
    // leptonica: wshedBasins (watershed.c)
    /// <summary>Returns the basins and their levels.</summary>
    /// <param name="ppixa">mask of watershed basins</param>
    /// <param name="pnalevels">watershed levels</param>
    public void Basins(out Pixa ppixa, out Numa pnalevels)
    {
        ppixa = Pixad!.Copy(L.L_CLONE);
        pnalevels = Nalevels!.Clone();
    }

    // leptonica: wshedRenderFill (watershed.c)
    /// <summary>Renders the initial image with all basins filled.</summary>
    /// <returns>pixd initial image with all basins filled</returns>
    public Pix RenderFill()
    {
        Basins(out Pixa pixa, out Numa na);
        Pix pixd = Pix.Copy(null, Pixs);
        int n = pixa.Count;
        for (int i = 0; i < n; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            pixa.GetBoxGeometry(i, out int bx, out int by, out _, out _);
            int level = na.GetIValue(i);
            pixd.PaintThroughMask(pix, bx, by, (uint)level);
        }

        return pixd;
    }

    // leptonica: wshedRenderColors (watershed.c)
    /// <summary>Renders the initial image with each basin in a random color.</summary>
    /// <returns>pixd initial image with all basins filled</returns>
    public Pix RenderColors()
    {
        Basins(out Pixa pixa, out _);
        Pix pixg = Pix.Copy(null, Pixs);
        Pixs.GetDimensions(out int w, out int h, out _);
        Pix pixd = pixg.ConvertTo32();
        Pix pixt = pixa.DisplayRandomCmap(w, h);
        Pix pixc = pixt.ConvertTo32();
        Pix pixm = pixa.Display(w, h);
        pixd.CombineMasked(pixc, pixm);

        return pixd;
    }
}
