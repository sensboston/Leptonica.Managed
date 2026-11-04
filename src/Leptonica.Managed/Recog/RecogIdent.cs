using System;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Leptonica;

/*
 *  recogident.c: character identification, greedy/DID splitting, postprocessing.
 *
 *  Not ported (memory management): rchaDestroy, rchDestroy.
 */

public sealed partial class Rch
{
    // leptonica: rchCreate (recogident.c)
    internal static Rch Create(int index, float score, string? text,
                               int sample, int xloc, int yloc, int width)
    {
        return new Rch(index, score, text, sample, xloc, yloc, width);
    }

    // leptonica: rchExtract (recogident.c)
    /// <summary>Gets the fields of the match result.</summary>
    /// <param name="pindex">Receives the index of the best matching character class.</param>
    /// <param name="pscore">Receives the correlation score of the best match.</param>
    /// <param name="ptext">Receives the text label of the class, or <c>null</c> if there is none.</param>
    /// <param name="psample">Receives the index of the best matching template within its class.</param>
    /// <param name="pxloc">Receives the x location of the matched character.</param>
    /// <param name="pyloc">Receives the y location of the matched character.</param>
    /// <param name="pwidth">Receives the width of the matched character.</param>
    public void Extract(out int pindex, out float pscore, out string? ptext,
                        out int psample, out int pxloc, out int pyloc, out int pwidth)
    {
        pindex = index;
        pscore = score;
        ptext = text;
        psample = sample;
        pxloc = xloc;
        pyloc = yloc;
        pwidth = width;
    }

    // leptonica: transferRchToRcha (recogident.c)
    internal static void TransferRchToRcha(Rch rch, Rcha rcha)
    {
        const string func = "transferRchToRcha";
        if (rch == null)
            throw Lept.Arg(func, "rch not defined");
        if (rcha == null)
            throw Lept.Arg(func, "rcha not defined");

        rcha.naindex.AddNumber(rch.index);
        rcha.nascore.AddNumber(rch.score);
        rcha.satext.AddString(rch.text ?? string.Empty, L.L_COPY);
        rcha.nasample.AddNumber(rch.sample);
        rcha.naxloc.AddNumber(rch.xloc);
        rcha.nayloc.AddNumber(rch.yloc);
        rcha.nawidth.AddNumber(rch.width);
    }
}

public sealed partial class Rcha
{
    // leptonica: rchaCreate (recogident.c)
    internal static Rcha Create() => new Rcha();

    // leptonica: rchaExtract (recogident.c)
    /// <summary>Gets copies of the arrays that make up the match results.</summary>
    /// <param name="pnaindex">Receives the indices of the best matching classes.</param>
    /// <param name="pnascore">Receives the correlation scores of the best matches.</param>
    /// <param name="psatext">Receives the text labels of the best matching classes.</param>
    /// <param name="pnasample">Receives the indices of the best matching templates within their classes.</param>
    /// <param name="pnaxloc">Receives the x locations of the matched characters.</param>
    /// <param name="pnayloc">Receives the y locations of the matched characters.</param>
    /// <param name="pnawidth">Receives the widths of the matched characters.</param>
    public void Extract(out Numa pnaindex, out Numa pnascore, out Sarray psatext,
                        out Numa pnasample, out Numa pnaxloc, out Numa pnayloc,
                        out Numa pnawidth)
    {
        pnaindex = naindex.Clone();
        pnascore = nascore.Clone();
        psatext = satext.Clone();
        pnasample = nasample.Clone();
        pnaxloc = naxloc.Clone();
        pnayloc = nayloc.Clone();
        pnawidth = nawidth.Clone();
    }
}

public sealed partial class Recog
{
    private const int LeftRightPadding = 32;
    private const float MinFillFactor = 0.10f;
    private const int DefaultMinHeight = 15;
    private const int MinOverlap1 = 6;
    private const int MinOverlap2 = 6;
    private const int MinHeightPass1 = 5;

    private static int decodeDebugIndex;

    /*------------------------------------------------------------------------*
     *                             Identification                             *
     *------------------------------------------------------------------------*/
    // leptonica: recogIdentifyMultiple (recogident.c)
    /// <summary>Finds, optionally splits, and identifies the characters in an image.</summary>
    /// <param name="pixs">An image containing typically a small number of characters.</param>
    /// <param name="minh">Components shorter than this are removed; 0 for the default.</param>
    /// <param name="skipsplit">1 to skip the splitting step.</param>
    /// <param name="pboxa">Receives the locations of the identified components.</param>
    /// <param name="ppixa">Receives the images of the identified components.</param>
    /// <param name="debugsplit">1 to generate debugging images for the splitting.</param>
    /// <returns>0 on success; 1 if nothing is found.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c>.</exception>
    /// <exception cref="LeptonicaException">Training is not finished.</exception>
    /// <remarks>
    /// <para>This filters the input and calls <see cref="IdentifyPixa(Pixa)"/>.</para>
    /// <para>Splitting is relatively slow, because it tries to match all character templates to all locations.
    /// This step can be skipped.</para>
    /// <para>An attempt is made to order the returned images and boxes in two-dimensional sorted order.
    /// These can then be used to aggregate identified characters into numbers or words.
    /// One typically wants the image array, which contains a box array of the extracted subimages.</para>
    /// </remarks>
    public int IdentifyMultiple(Pix pixs, int minh, int skipsplit,
                                out Boxa? pboxa, out Pixa? ppixa, int debugsplit)
    {
        return IdentifyMultipleCore(pixs, minh, skipsplit, false,
                                    out pboxa, out ppixa, out _, debugsplit);
    }

    /// <summary>Finds, optionally splits, and identifies the characters in an image, and also returns a debug image.</summary>
    /// <param name="pixs">An image containing typically a small number of characters.</param>
    /// <param name="minh">Components shorter than this are removed; 0 for the default.</param>
    /// <param name="skipsplit">1 to skip the splitting step.</param>
    /// <param name="pboxa">Receives the locations of the identified components.</param>
    /// <param name="ppixa">Receives the images of the identified components.</param>
    /// <param name="ppixdb">Receives a debug image showing the inputs and best fits.</param>
    /// <param name="debugsplit">1 to generate debugging images for the splitting.</param>
    /// <returns>0 on success; 1 if nothing is found.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c>.</exception>
    /// <exception cref="LeptonicaException">Training is not finished.</exception>
    public int IdentifyMultiple(Pix pixs, int minh, int skipsplit,
                                out Boxa? pboxa, out Pixa? ppixa, out Pix? ppixdb,
                                int debugsplit)
    {
        return IdentifyMultipleCore(pixs, minh, skipsplit, true,
                                    out pboxa, out ppixa, out ppixdb, debugsplit);
    }

    private int IdentifyMultipleCore(Pix pixs, int minh, int skipsplit, bool wantdb,
                                     out Boxa? pboxa, out Pixa? ppixa, out Pix? ppixdb,
                                     int debugsplit)
    {
        const string func = "recogIdentifyMultiple";
        pboxa = null;
        ppixa = null;
        ppixdb = null;
        if (!train_done)
            throw Lept.Fail(func, "training not finished");
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");

            /* Binarize if necessary */
        Pix pixb = pixs.Depth > 1 ? pixs.ConvertTo1(threshold) : Pix.Clone(pixs);

            /* Noise removal and splitting of touching characters */
        SplitIntoCharacters(pixb, minh, skipsplit, out Boxa? boxa, out Pixa? pixa,
                            debugsplit);
        if (pixa == null || pixa.Count == 0) {
            Log.Warning(func, "nothing found");
            return 1;
        }

        if (wantdb)
            IdentifyPixa(pixa, out ppixdb);
        else
            IdentifyPixa(pixa);
        pboxa = boxa;
        ppixa = pixa;
        return 0;
    }

    /*------------------------------------------------------------------------*
     *                     Segmentation and noise removal                     *
     *------------------------------------------------------------------------*/
    // leptonica: recogSplitIntoCharacters (recogident.c)
    /// <summary>Filters noise from an image of text and splits touching characters.</summary>
    /// <param name="pixs">A 1 bpp image that contains only mostly deskewed text.</param>
    /// <param name="minh">Components shorter than this are removed; 0 for the default.</param>
    /// <param name="skipsplit">1 to skip the splitting step.</param>
    /// <param name="pboxa">Receives the character bounding boxes.</param>
    /// <param name="ppixa">Receives the character images.</param>
    /// <param name="debug">1 to write results to the splitting debug array.</param>
    /// <returns>0 on success.</returns>
    /// <remarks>
    /// <para>This can be given an image that has an arbitrary number of text characters. It optionally splits connected
    /// components based on document image decoding (see <see cref="Decode(Pix, int)"/>).
    /// The returned image array includes the boxes from which the (possibly split) components are extracted.</para>
    /// <para>After noise filtering, the resulting components are put in row-major (two-dimensional) order, and the smaller
    /// of overlapping components are removed if they satisfy conditions of relative size and fractional overlap.</para>
    /// <para>The splitting uses unscaled templates and does not return the class results and scores.
    /// These are more accurately found later using the scaled templates.</para>
    /// </remarks>
    public int SplitIntoCharacters(Pix pixs, int minh, int skipsplit,
                                   out Boxa? pboxa, out Pixa? ppixa, int debug)
    {
        const string func = "recogSplitIntoCharacters";
        Utils.LeptMkdir("lept/recog");

        pboxa = null;
        ppixa = null;
        if (!train_done)
            throw Lept.Fail(func, "training not finished");
        if (pixs == null || pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");
        if (minh <= 0) minh = DefaultMinHeight;
        if (pixs.Zero())
            return 1;

            /* Small vertical close for consolidation. */
        Pix pix1 = pixs.MorphSequence("c1.3", 0);

            /* Carefully filter out noise */
        Pix pix2 = PreSplittingFilter(pix1, minh, MinFillFactor, debug);

            /* Get the 8-connected components to be split/identified */
        Boxa boxa1 = pix2.ConnComp(8);
        int ncomp = boxa1.Count;
        if (ncomp == 0) {
            Log.Warning(func, "all components removed");
            return 1;
        }

            /* Save everything and split the large components */
        Boxa boxa2 = Boxa.Create(ncomp);
        int maxw = maxwidth_u + 5;
        bool scaling = scalew > 0 || scaleh > 0;
        Pixa? pixa = debug != 0 ? Pixa.Create(ncomp) : null;
        for (int i = 0; i < ncomp; i++) {
            Box box = boxa1.GetBox(i, L.L_CLONE);
            box.GetGeometry(out int xoff, out int yoff, out int bw, out _);
            if (bw <= maxw || scaling || skipsplit != 0) {
                boxa2.AddBox(box, L.L_INSERT);
            } else {
                Pix pix = pixs.ClipRectangle(box, out _)!;
                Boxa? boxa3;
                if (debug == 0) {
                    boxa3 = Decode(pix, 2);
                } else {
                    boxa3 = Decode(pix, 2, out Pix? pixDb);
                    if (pixDb != null)
                        pixa!.AddPix(pixDb, L.L_INSERT);
                }
                if (boxa3 == null) {
                    Log.Error(func, $"boxa3 not found for component {i}");
                } else {
                    Boxa boxa4 = boxa3.Transform(xoff, yoff, 1.0f, 1.0f);
                    boxa2.Join(boxa4, 0, -1);
                }
            }
        }
        if (pixa != null) {  /* debug */
            Pix pix3 = pixa.DisplayTiledInColumns(1, 1.0f, 20, 2);
            int idx = Interlocked.Increment(ref decodeDebugIndex) - 1;
            string buf = $"/tmp/lept/recog/decode-{idx}.png";
            Pix.Write(buf, pix3, L.IFF_PNG);
        }

            /* 2D sort on the bounding boxes, and flatten to 1D */
        Boxaa baa = boxa2.Sort2d(out _, MinOverlap1, MinOverlap2, MinHeightPass1);
        Boxa boxaFlat = baa.FlattenToBoxa(out _, L.L_CLONE);

            /* Remove smaller components of overlapping pairs */
        Boxa boxad = boxaFlat.HandleOverlaps(L.L_COMBINE, 10, 0.5f, 0.3f, out _);

            /* Extract and save the image pieces from the input image */
        ppixa = pixs.ClipRectangles(boxad);
        pboxa = boxad;
        return 0;
    }

    /*------------------------------------------------------------------------*
     *                       Greedy character splitting                       *
     *------------------------------------------------------------------------*/
    // leptonica: recogCorrelationBestRow (recogident.c)
    /// <summary>Finds the best-matching characters in an image of touching characters, greedily.</summary>
    /// <param name="pixs">A 1 bpp image, typically of multiple touching characters.</param>
    /// <param name="pboxa">Receives the bounding boxes of the best-fit characters.</param>
    /// <param name="pnascore">Receives the correlation scores.</param>
    /// <param name="pnaindex">Receives the indices of the classes.</param>
    /// <param name="psachar">Receives the character strings of the classes.</param>
    /// <param name="debug">1 to write results to the splitting debug array.</param>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c> or not 1 bpp.</exception>
    /// <remarks>
    /// <para>This supervises character matching for, in general, a connected component with multiple touching characters.
    /// It finds the best match greedily and rejects small parts that are left over after splitting.</para>
    /// <para>Matching is to the average templates, and without character scaling.</para>
    /// </remarks>
    public void CorrelationBestRow(Pix pixs, out Boxa pboxa, out Numa pnascore,
                                   out Numa pnaindex, out Sarray psachar, int debug)
    {
        const string func = "recogCorrelationBestRow";
        if (pixs == null || pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");
        if (pixs.Width < minwidth_u - 4)
            throw Lept.Arg(func, "pixs too narrow");
        if (!train_done)
            throw Lept.Fail(func, "training not finished");

            /* Binarize and crop to foreground if necessary */
        Pix? pixb = ProcessToIdentify(pixs, 0);
        if (pixb == null)
            throw Lept.Fail(func, "no foreground pixels");

            /* Initialize the arrays */
        Boxa boxat = Boxa.Create(4);
        Numa nascoret = Numa.Create(4);
        Numa naindext = Numa.Create(4);
        Sarray sachart = Sarray.Create(4);
        Pixa? pixadb = debug != 0 ? Pixa.Create(4) : null;

        Pixa pixar = Pixa.Create(1);
        pixb.GetDimensions(out int w, out int h, out _);
        Box box = Box.Create(0, 0, w, h);
        pixar.AddPix(pixb, L.L_INSERT);
        pixar.AddBox(box, L.L_INSERT);

            /* Successively split on the best match until nothing is left. */
        for (int iter = 0; iter < 11; iter++) {
            if (pixar.Count == 0)
                break;
            if (iter == 10) {
                Log.Warning(func, "more than 10 chars; ending search");
                break;
            }

                /* Pop one from the queue */
            pixar.RemovePixAndSave(0, out Pix? pixc, out Box? boxc);
            boxc!.GetGeometry(out int bxc, out _, out int bwc, out _);

                /* This is a single component; if noise, remove it */
            SplittingFilter(pixc!, 0, MinFillFactor, out bool remove, debug);
            if (remove)
                continue;

                /* Find the best character match */
            float score;
            int index;
            string charstr;
            if (debug != 0) {
                CorrelationBestChar(pixc!, out box, out score, out index, out charstr,
                                    out Pix? pixdb);
                if (pixdb != null)
                    pixadb!.AddPix(pixdb, L.L_INSERT);
            } else {
                CorrelationBestChar(pixc!, out box, out score, out index, out charstr);
            }

            Box boxtrans = box.Transform(bxc, 0, 1.0f, 1.0f);
            boxat.AddBox(boxtrans, L.L_INSERT);
            nascoret.AddNumber(score);
            naindext.AddNumber(index);
            sachart.AddString(charstr, L.L_INSERT);

            box.GetGeometry(out int bx, out _, out int bw, out _);
            int w1 = bx;
            int w3 = bwc - bx - bw;
            if (w1 < minwidth_u - 4) {
                if (debug != 0) Log.Info(func, $"discarding width {w1} on left");
            } else {
                Box boxl = Box.Create(0, 0, bx + 1, h);
                Pix pixl = pixc!.ClipRectangle(boxl, out _)!;
                Box boxlt = boxl.Transform(bxc, 0, 1.0f, 1.0f);
                pixar.AddPix(pixl, L.L_INSERT);
                pixar.AddBox(boxlt, L.L_INSERT);
            }
            if (w3 < minwidth_u - 4) {
                if (debug != 0) Log.Info(func, $"discarding width {w3} on right");
            } else {
                Box boxr = Box.Create(bx + bw - 1, 0, w3 + 1, h);
                Pix pixr = pixc!.ClipRectangle(boxr, out _)!;
                Box boxrt = boxr.Transform(bxc, 0, 1.0f, 1.0f);
                pixar.AddPix(pixr, L.L_INSERT);
                pixar.AddBox(boxrt, L.L_INSERT);
            }
        }

            /* Sort the output results by left-to-right in the boxa */
        pboxa = boxat.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out Numa? nasort);
        pnascore = nasort != null ? nascoret.SortByIndex(nasort) : Numa.Create(0);
        pnaindex = nasort != null ? naindext.SortByIndex(nasort) : Numa.Create(0);
        psachar = nasort != null ? Sarray.SortByIndex(sachart, nasort) : Sarray.Create(0);

        if (debug != 0 && pixadb != null) {
            Pix pixd = pixadb.DisplayTiledInRows(32, 2000, 1.0f, 0, 15, 2);
            pixd.Display(400, 400);
            pixadb_split.AddPix(pixd, L.L_INSERT);
        }
    }

    // leptonica: recogCorrelationBestChar (recogident.c)
    /// <summary>Finds the best match among all templates to some region of an image.</summary>
    /// <param name="pixs">A 1 bpp image, which can contain multiple touching characters.</param>
    /// <param name="pbox">Receives the bounding box of the best-fit character.</param>
    /// <param name="pscore">Receives the correlation score.</param>
    /// <param name="pindex">Receives the index of the class.</param>
    /// <param name="pcharstr">Receives the character string of the class.</param>
    /// <remarks>
    /// <para>This is the basic matching character splitter. It finds the best match among all templates to some region of
    /// the image, which can result in splitting the image into two parts. This is image decoding without dynamic
    /// programming, because no setwidth is used and the best matching score is not computed for the entire image.</para>
    /// <para>Matching is to the average templates, without character scaling.</para>
    /// </remarks>
    public void CorrelationBestChar(Pix pixs, out Box pbox, out float pscore,
                                    out int pindex, out string pcharstr)
    {
        CorrelationBestCharCore(pixs, false, out pbox, out pscore,
                                out pindex, out pcharstr, out _);
    }

    /// <summary>Finds the best match among all templates to some region of an image, and also returns a debug image.</summary>
    /// <param name="pixs">A 1 bpp image, which can contain multiple touching characters.</param>
    /// <param name="pbox">Receives the bounding box of the best-fit character.</param>
    /// <param name="pscore">Receives the correlation score.</param>
    /// <param name="pindex">Receives the index of the class.</param>
    /// <param name="pcharstr">Receives the character string of the class.</param>
    /// <param name="ppixdb">Receives a debug image showing the input and the best fit.</param>
    public void CorrelationBestChar(Pix pixs, out Box pbox, out float pscore,
                                    out int pindex, out string pcharstr, out Pix? ppixdb)
    {
        CorrelationBestCharCore(pixs, true, out pbox, out pscore,
                                out pindex, out pcharstr, out ppixdb);
    }

    private void CorrelationBestCharCore(Pix pixs, bool wantdb, out Box pbox,
                                         out float pscore, out int pindex,
                                         out string pcharstr, out Pix? ppixdb)
    {
        const string func = "recogCorrelationBestChar";
        ppixdb = null;
        if (pixs == null || pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");
        if (!train_done)
            throw Lept.Fail(func, "training not finished");

        Pix? pix1 = ProcessToIdentify(pixs, LeftRightPadding);
        if (pix1 == null)
            throw Lept.Fail(func, "no foreground pixels");
        pix1.GetDimensions(out int w1, out int h1, out _);

        Numa nasum1 = pix1.CountPixelsByColumn();
        Numa namoment1 = pix1.GetMomentByColumn(1);

        int n = setsize;
        Boxa boxa = Boxa.Create(n);
        float bestscore = 0.0f;
        int bestindex = 0, bestdelx = 0, bestdely = 0;
        for (int i = 0; i < n; i++) {
            Pix pix2 = pixa_u!.GetPix(i, L.L_CLONE);
            int w2 = pix2.Width;
            Box box;
            if (w1 >= w2) {
                int area2 = nasum_u!.GetIValue(i);
                pta_u!.GetIPt(i, out _, out int ycent2);
                CorrelationBestShift(pix1, pix2, nasum1, namoment1, area2, ycent2,
                                     maxyshift, sumtab, out int delx, out int dely,
                                     out float score, 1);
                box = Box.Create(delx - LeftRightPadding, 0, w2, h1);
                if (score > bestscore) {
                    bestscore = score;
                    bestdelx = delx - LeftRightPadding;
                    bestdely = dely;
                    bestindex = i;
                }
            } else {
                box = Box.Create(0, 0, 1, 1);
            }
            boxa.AddBox(box, L.L_INSERT);
        }

        pscore = bestscore;
        pbox = boxa.GetBox(bestindex, L.L_COPY);
        pindex = bestindex;
        pcharstr = GetClassString(bestindex);

        if (wantdb) {
            Log.Info(func, $"Best match: class {bestindex}; shifts ({bestdelx}, {bestdely})");
            Pix pix2 = pixa_u!.GetPix(bestindex, L.L_CLONE);
            ppixdb = ShowMatch(pix1, pix2, null, -1, 0.0f);
        }
    }

    // leptonica: pixCorrelationBestShift (recogident.c)
    private static void CorrelationBestShift(Pix pix1, Pix pix2, Numa nasum1, Numa namoment1,
                                             int area2, int ycent2, int maxyshift,
                                             int[]? tab8, out int pdelx, out int pdely,
                                             out float pscore, int debugflag)
    {
        const string func = "pixCorrelationBestShift";
        pdelx = 0;
        pdely = 0;
        pscore = 0.0f;
        if (pix1 == null || pix1.Depth != 1)
            throw Lept.Arg(func, "pix1 not defined or not 1 bpp");
        if (pix2 == null || pix2.Depth != 1)
            throw Lept.Arg(func, "pix2 not defined or not 1 bpp");
        if (nasum1 == null || namoment1 == null)
            throw Lept.Arg(func, "nasum1 and namoment1 not both defined");
        if (area2 <= 0 || ycent2 <= 0)
            throw Lept.Arg(func, "area2 and ycent2 must be > 0");

        pix1.GetDimensions(out int w1, out int h1, out _);
        pix2.GetDimensions(out int w2, out int h2, out _);
        if (w1 < w2) {
            if (debugflag > 0)
                Log.Info(func, $"skipping match with w1 = {w1} and w2 = {w2}");
            return;
        }
        int nx = w1 - w2 + 1;

        FPix? fpix = debugflag > 0 ? FPix.Create(nx, 2 * maxyshift + 1) : null;
        int[] tab = tab8 ?? Pix.MakePixelSumTab8();

        int[] area1 = new int[nx];
        float[] ycent1 = new float[nx];
        int[] arraysum = nasum1.GetIArray();
        int[] arraymoment = namoment1.GetIArray();
        int sum = 0, moment = 0;
        for (int i = 0; i < w2; i++) {
            sum += arraysum[i];
            moment += arraymoment[i];
        }
        for (int i = 0; i < nx - 1; i++) {
            area1[i] = sum;
            ycent1[i] = (sum == 0) ? ycent2 : (float)moment / (float)sum;
            sum += arraysum[w2 + i] - arraysum[i];
            moment += arraymoment[w2 + i] - arraymoment[i];
        }
        area1[nx - 1] = sum;
        ycent1[nx - 1] = (sum == 0) ? ycent2 : (float)moment / (float)sum;

        Pix pixt = Pix.Create(w2, h1, 1);
        float maxscore = 0;
        int delx = 0, dely = 0;
        for (int i = 0; i < nx; i++) {
            int shifty = (int)(ycent1[i] - ycent2 + 0.5);
            for (int j = -maxyshift; j <= maxyshift; j++) {
                pixt.ClearAll();
                pixt.Rasterop(0, shifty + j, w2, h2, L.PIX_SRC, pix2, 0, 0);
                pixt.Rasterop(0, 0, w2, h1, L.PIX_SRC & L.PIX_DST, pix1, i, 0);
                int count = pixt.CountPixels(tab);
                float score = (float)count * (float)count /
                              ((float)area1[i] * (float)area2);
                if (score > maxscore) {
                    maxscore = score;
                    delx = i;
                    dely = shifty + j;
                }

                if (debugflag > 0)
                    fpix!.SetPixel(i, maxyshift + j, 1000.0f * score);
            }
        }

        if (debugflag > 0 && Pix.LeptDebugOK) {
            Utils.LeptMkdir("lept/recog");
            Pix pixt1 = fpix!.DisplayMaxDynamicRange();
            Pix pixt2 = pixt1.ExpandReplicate(5);
            string buf = $"/tmp/lept/recog/junkbs_{debugflag}.png";
            Pix.Write(buf, pixt2, L.IFF_PNG);
        }

        pdelx = delx;
        pdely = dely;
        pscore = maxscore;
    }

    /*------------------------------------------------------------------------*
     *                          Low-level identification                      *
     *------------------------------------------------------------------------*/
    // leptonica: recogIdentifyPixa (recogident.c)
    /// <summary>Identifies each image in an array of single characters.</summary>
    /// <param name="pixa">The 1 bpp images to match.</param>
    /// <exception cref="ArgumentException"><paramref name="pixa"/> is <c>null</c>.</exception>
    /// <remarks>
    /// This should be called by <see cref="IdentifyMultiple(Pix, int, int, out Boxa?, out Pixa?, int)"/>, which binarizes
    /// and splits characters before sending the images here. It calls <see cref="IdentifyPix(Pix)"/> on each image.
    /// </remarks>
    public void IdentifyPixa(Pixa pixa)
    {
        IdentifyPixaCore(pixa, false, out _);
    }

    /// <summary>Identifies each image in an array of single characters, and also returns a debug image.</summary>
    /// <param name="pixa">The 1 bpp images to match.</param>
    /// <param name="ppixdb">Receives an image showing the inputs and best fits.</param>
    /// <exception cref="ArgumentException"><paramref name="pixa"/> is <c>null</c>.</exception>
    public void IdentifyPixa(Pixa pixa, out Pix? ppixdb)
    {
        IdentifyPixaCore(pixa, true, out ppixdb);
    }

    private void IdentifyPixaCore(Pixa pixa, bool wantdb, out Pix? ppixdb)
    {
        const string func = "recogIdentifyPixa";
        ppixdb = null;
        if (pixa == null)
            throw Lept.Arg(func, "pixa not defined");

        int n = pixa.Count;
        rcha = Rcha.Create();
        Pixa? pixa1 = wantdb ? Pixa.Create(n) : null;
        int depth = 1;
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixa.GetPix(i, L.L_CLONE);
            bool ok = TryIdentifyPixCore(pix1, wantdb, out Pix? pix2);
            if (!ok)
                SkipIdentify();
            Rch? curRch = rch;
            if (curRch == null) {
                Log.Error(func, $"rch not found for char {i}");
                continue;
            }
            curRch.Extract(out int index, out float score, out string? text,
                           out _, out _, out _, out _);
            pix1.Text = text;
            if (wantdb && pix2 != null) {
                Pix pix3 = ShowMatch(pix2, null, null, index, score);
                if (i == 0) depth = pix3.Depth;
                pixa1!.AddPix(pix3, L.L_INSERT);
            }
            Rch.TransferRchToRcha(curRch, rcha);
        }

        if (wantdb && pixa1 != null && pixa1.Count > 0)
            ppixdb = pixa1.DisplayTiledInRows(depth, 2500, 1.0f, 0, 20, 1);
    }

    // leptonica: recogIdentifyPix (recogident.c)
    /// <summary>Identifies a single character.</summary>
    /// <param name="pixs">A 1 bpp image of a single character.</param>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c> or not 1 bpp.</exception>
    /// <exception cref="LeptonicaException">Identification fails.</exception>
    /// <remarks>
    /// <para>This is the basic recognition method for a single character.</para>
    /// <para>If <see cref="TemplUse"/> is <see cref="L.L_USE_ALL_TEMPLATES"/>, which is the default, matching is attempted
    /// to every bitmap in the recognizer, and the identity of the best match is returned.</para>
    /// <para>For finding outliers, <see cref="TemplUse"/> is <see cref="L.L_USE_AVERAGE_TEMPLATES"/>, and matching is only
    /// attempted to the averaged bitmaps. In this case the index of the best sample is meaningless (0 is returned).</para>
    /// <para>The score is related to the confidence (probability of correct identification), in that a higher score is
    /// correlated with a higher probability. However, the actual relation between the correlation and the probability
    /// is not known; it is called a score because confidence can be misinterpreted as an actual probability.</para>
    /// </remarks>
    public void IdentifyPix(Pix pixs)
    {
        if (!TryIdentifyPixCore(pixs, false, out _))
            throw Lept.Fail("recogIdentifyPix", "identification failed");
    }

    /// <summary>Identifies a single character, and also returns a debug image.</summary>
    /// <param name="pixs">A 1 bpp image of a single character.</param>
    /// <param name="ppixdb">Receives a debug image showing the input and the best fit.</param>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c> or not 1 bpp.</exception>
    /// <exception cref="LeptonicaException">Identification fails.</exception>
    public void IdentifyPix(Pix pixs, out Pix? ppixdb)
    {
        if (!TryIdentifyPixCore(pixs, true, out ppixdb))
            throw Lept.Fail("recogIdentifyPix", "identification failed");
    }

    internal bool TryIdentifyPixCore(Pix pixs, bool wantdb, out Pix? ppixdb)
    {
        const string func = "recogIdentifyPix";
        ppixdb = null;
        if (pixs == null || pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");

            /* Do the averaging if required and not yet done. */
        if (templ_use == L.L_USE_AVERAGE_TEMPLATES && !ave_done) {
            if (AverageSamples(0) != 0) {
                Log.Error(func, "averaging failed");
                return false;
            }
        }

            /* Binarize and crop to foreground if necessary */
        Pix? pix0 = ProcessToIdentify(pixs, 0);
        if (pix0 == null) {
            Log.Error(func, "no fg pixels in pix0");
            return false;
        }

            /* Optionally scale and/or convert to fixed stroke width */
        Pix? pix1 = ModifyTemplate(pix0);
        if (pix1 == null) {
            Log.Error(func, "no fg pixels in pix1");
            return false;
        }

            /* Do correlation at all positions within +-maxyshift of
             * the nominal centroid alignment. */
        int area1 = pix1.CountPixels(sumtab);
        pix1.Centroid(centtab, sumtab, out float x1, out float y1);
        int bestindex = 0, bestsample = 0, bestdelx = 0, bestdely = 0, bestwidth = 0;
        float maxscore = 0.0f;
        int myshift = maxyshift;
        if (templ_use == L.L_USE_AVERAGE_TEMPLATES) {
            for (int i = 0; i < setsize; i++) {
                int area2 = nasum!.GetIValue(i);
                if (area2 == 0) continue;
                Pix pix2 = pixa!.GetPix(i, L.L_CLONE);
                pta!.GetPt(i, out float x2, out float y2);
                float delx = x1 - x2;
                float dely = y1 - y2;
                for (int shifty = -myshift; shifty <= myshift; shifty++) {
                    for (int shiftx = -myshift; shiftx <= myshift; shiftx++) {
                        float score = pix1.CorrelationScoreSimple(pix2, area1, area2,
                                                                  delx + shiftx, dely + shifty,
                                                                  5, 5, sumtab);
                        if (score > maxscore) {
                            bestindex = i;
                            bestdelx = (int)(delx + shiftx);
                            bestdely = (int)(dely + shifty);
                            maxscore = score;
                        }
                    }
                }
            }
        } else {  /* use all the samples */
            for (int i = 0; i < setsize; i++) {
                Pixa pa = pixaa!.GetPixa(i, L.L_CLONE);
                int n = pa.Count;
                if (n == 0) continue;
                Numa numa = naasum!.GetNuma(i, L.L_CLONE);
                Pta ptaClass = ptaa!.GetPta(i, L.L_CLONE);
                for (int j = 0; j < n; j++) {
                    Pix pix2 = pa.GetPix(j, L.L_CLONE);
                    int area2 = numa.GetIValue(j);
                    ptaClass.GetPt(j, out float x2, out float y2);
                    float delx = x1 - x2;
                    float dely = y1 - y2;
                    for (int shifty = -myshift; shifty <= myshift; shifty++) {
                        for (int shiftx = -myshift; shiftx <= myshift; shiftx++) {
                            float score = pix1.CorrelationScoreSimple(pix2, area1, area2,
                                                                      delx + shiftx, dely + shifty,
                                                                      5, 5, sumtab);
                            if (score > maxscore) {
                                bestindex = i;
                                bestsample = j;
                                bestdelx = (int)(delx + shiftx);
                                bestdely = (int)(dely + shifty);
                                maxscore = score;
                                bestwidth = pix2.Width;
                            }
                        }
                    }
                }
            }
        }

            /* Package up the results */
        string text = setsize > 0 ? GetClassString(bestindex) : string.Empty;
        rch = Rch.Create(bestindex, maxscore, text, bestsample,
                         bestdelx, bestdely, bestwidth);

        if (wantdb) {
            Pix pix2;
            if (templ_use == L.L_USE_AVERAGE_TEMPLATES) {
                Log.Info(func, $"Best match: str {text}; class {bestindex}; sh ({bestdelx}, {bestdely}); score {maxscore:F3}");
                pix2 = pixa!.GetPix(bestindex, L.L_CLONE);
            } else {
                Log.Info(func, $"Best match: str {text}; sample {bestsample} in class {bestindex}; score {maxscore:F3}");
                pix2 = pixaa!.GetPix(bestindex, bestsample, L.L_CLONE);
            }
            ppixdb = ShowMatch(pix1, pix2, null, -1, 0.0f);
        }

        return true;
    }

    // leptonica: recogSkipIdentify (recogident.c)
    /// <summary>Stores a placeholder match result with a score of 0 and an empty text label.</summary>
    public void SkipIdentify()
    {
        rch = Rch.Create(0, 0.0f, string.Empty, 0, 0, 0, 0);
    }

    /*------------------------------------------------------------------------*
     *                        Preprocessing and filtering                     *
     *------------------------------------------------------------------------*/
    // leptonica: recogProcessToIdentify (recogident.c)
    /// <summary>Prepares an image for identification: makes it 1 bpp, crops it to the foreground, and pads it.</summary>
    /// <param name="pixs">Typically a single character, possibly with depth greater than 1 and uncropped.</param>
    /// <param name="pad">Number of extra pixels added to the left and right sides.</param>
    /// <returns>A 1 bpp image clipped to the foreground, or <c>null</c> if there are no foreground pixels.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c>.</exception>
    /// <remarks>
    /// This is a lightweight operation to ensure that the input image is 1 bpp, properly cropped, and padded on each side.
    /// If the depth is greater than 1, the image is thresholded.
    /// </remarks>
    public Pix? ProcessToIdentify(Pix pixs, int pad)
    {
        const string func = "recogProcessToIdentify";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");

        Pix pix1 = pixs.Depth != 1 ? pixs.ThresholdToBinary(threshold) : Pix.Clone(pixs);
        Pix? pix2;
        if (pix1.TestClipToForeground())
            pix1.ClipToForeground(out pix2, out _);
        else
            pix2 = Pix.Clone(pix1);
        if (pix2 == null) {
            Log.Error(func, "no foreground pixels");
            return null;
        }

        return pix2.AddBorderGeneral(pad, pad, 0, 0, 0);
    }

    // leptonica: recogPreSplittingFilter (recogident.c)
    private Pix PreSplittingFilter(Pix pixs, int minh, float minaf, int debug)
    {
        const string func = "recogPreSplittingFilter";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");

        bool scaling = scalew > 0 || scaleh > 0;
        int minsplitw = scaling ? 1 : min_splitw - 3;
        int maxsplith = scaling ? 150 : max_splith;
        int maxasp = (int)max_wh_ratio;

        pixs.ConnComp(out Pixa pixas, 8);
        pixas.FindDimensions(out Numa naw, out Numa nah);
        Numa na1 = naw.MakeThresholdIndicator(minsplitw, L.L_SELECT_IF_LT);
        Numa na1c = na1.Copy();
        Numa na2 = nah.MakeThresholdIndicator(minh, L.L_SELECT_IF_LT);
        Numa na3 = nah.MakeThresholdIndicator(maxsplith, L.L_SELECT_IF_GT);
        Numa na4 = pixas.FindWidthHeightRatio();
        Numa na5 = na4.MakeThresholdIndicator(maxasp, L.L_SELECT_IF_GT);
        Numa na6 = pixas.FindAreaFraction();
        Numa na7 = na6.MakeThresholdIndicator(minaf, L.L_SELECT_IF_LT);
        Numa.LogicalOp(na1, na1, na2, L.L_UNION);
        Numa.LogicalOp(na1, na1, na3, L.L_UNION);
        Numa.LogicalOp(na1, na1, na5, L.L_UNION);
        Numa.LogicalOp(na1, na1, na7, L.L_UNION);
        Pix pixd = Pix.Copy(null, pixs);
        pixd.RemoveWithIndicator(pixas, na1);
        if (debug != 0)
            ShowIndicatorSplitValues(na1c, na2, na3, na5, na7, na1);
        return pixd;
    }

    // leptonica: recogSplittingFilter (recogident.c)
    private void SplittingFilter(Pix pixs, int minh, float minaf,
                                 out bool premove, int debug)
    {
        const string func = "recogSplittingFilter";
        premove = false;
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");
        if (minh <= 0) minh = DefaultMinHeight;

        pixs.GetDimensions(out int w, out int h, out _);
        if (w < min_splitw) {
            if (debug != 0) Log.Info(func, $"w = {w} < {min_splitw}");
            premove = true;
            return;
        }
        if (h < minh) {
            if (debug != 0) Log.Info(func, $"h = {h} < {minh}");
            premove = true;
            return;
        }
        float aspratio = (float)w / (float)h;
        if (aspratio > max_wh_ratio) {
            if (debug != 0) Log.Info(func, $"w/h = {aspratio:F3} too large");
            premove = true;
            return;
        }
        float fract = pixs.FindAreaFraction(sumtab);
        if (fract < minaf) {
            if (debug != 0) Log.Info(func, $"area fill fract {fract:F3} < {minaf:F3}");
            premove = true;
        }
    }

    /*------------------------------------------------------------------------*
     *                              Postprocessing                            *
     *------------------------------------------------------------------------*/
    // leptonica: recogExtractNumbers (recogident.c)
    /// <summary>Extracts the identified numbers from the locations of identified components.</summary>
    /// <param name="boxas">The locations of the components.</param>
    /// <param name="scorethresh">Minimum score for which a component is accepted; a typical value is 0.60.</param>
    /// <param name="spacethresh">Maximum horizontal distance allowed between digits; -1 for the default.</param>
    /// <param name="pbaa">Receives the bounding boxes of the digits in each identified number.</param>
    /// <param name="pnaa">Receives the scores of the digits in each identified number.</param>
    /// <returns>An array of the identified numbers, or <c>null</c> if no text is identified.</returns>
    /// <remarks>
    /// <para>This extracts digit data after <see cref="IdentifyMultiple(Pix, int, int, out Boxa?, out Pixa?, int)"/> or
    /// lower-level identification has taken place.</para>
    /// <para>Each string in the returned array contains a sequence of ascii digits in a number.</para>
    /// <para>The horizontal distance between boxes (limited by <paramref name="spacethresh"/>) is the negative of the horizontal overlap.</para>
    /// <para>Components with a score less than <paramref name="scorethresh"/>, which may be hyphens or other small characters,
    /// signal the end of the current sequence of digits in the number.</para>
    /// <para>Two digits are combined if the first is to the left of the second, the second has a horizontal separation
    /// less than <paramref name="spacethresh"/>, the vertical overlap is not negative, and both have a score that exceeds
    /// <paramref name="scorethresh"/>.</para>
    /// <para>Each score array in <paramref name="pnaa"/> contains the digit scores of a number. Each box array in
    /// <paramref name="pbaa"/> contains the bounding boxes of the digits in the number.</para>
    /// </remarks>
    public Sarray? ExtractNumbers(Boxa boxas, float scorethresh, int spacethresh,
                                  out Boxaa? pbaa, out Numaa? pnaa)
    {
        const string func = "recogExtractNumbers";
        pbaa = null;
        pnaa = null;
        if (rcha == null)
            throw Lept.Fail(func, "recog and rcha not both defined");
        if (boxas == null)
            throw Lept.Arg(func, "boxas not defined");

        if (spacethresh < 0)
            spacethresh = Math.Max(maxheight_u, 20);
        rcha.Extract(out _, out Numa nascore, out Sarray satext,
                     out _, out _, out _, out _);

        Sarray saout = Sarray.Create(0);
        Numaa naa = Numaa.Create(0);
        Boxaa baa = Boxaa.Create(0);
        Box? prebox = null;
        Sarray? sa = null;
        Boxa? ba = null;
        Numa? na = null;
        int n = nascore.Count;
        for (int i = 0; i < n; i++) {
            float score = nascore.GetFValue(i);
            string text = satext.GetString(i, L.L_NOCOPY);
            if (prebox == null) {  /* no current run */
                if (score < scorethresh) {
                    continue;
                } else {  /* start a number run */
                    sa = Sarray.Create(0);
                    ba = Boxa.Create(0);
                    na = Numa.Create(0);
                    sa.AddString(text, L.L_COPY);
                    prebox = boxas.GetBox(i, L.L_CLONE);
                    ba.AddBox(prebox, L.L_COPY);
                    na.AddNumber(score);
                }
            } else {  /* in a current number run */
                Box box = boxas.GetBox(i, L.L_CLONE);
                prebox.GetGeometry(out int x1, out _, out _, out _);
                box.GetGeometry(out int x2, out _, out _, out _);
                box.OverlapDistance(prebox, out int hOvl, out int vOvl);
                int hSep = -hOvl;
                int vSep = -vOvl;
                prebox = null;
                if (x1 < x2 && hSep <= spacethresh &&
                    vSep < 0 && score >= scorethresh) {  /* add to number */
                    sa!.AddString(text, L.L_COPY);
                    ba!.AddBox(box, L.L_COPY);
                    na!.AddNumber(score);
                    prebox = box;
                } else {  /* save the completed number */
                    string str = sa!.ToString(0);
                    saout.AddString(str, L.L_INSERT);
                    sa = null;
                    baa.AddBoxa(ba!, L.L_INSERT);
                    ba = null;
                    naa.AddNuma(na!, L.L_INSERT);
                    na = null;
                    if (score >= scorethresh) {  /* start a new number */
                        i--;
                        continue;
                    }
                }
            }
        }

        if (prebox != null) {  /* save the last number */
            string str = sa!.ToString(0);
            saout.AddString(str, L.L_INSERT);
            baa.AddBoxa(ba!, L.L_INSERT);
            naa.AddNuma(na!, L.L_INSERT);
        }

        if (saout.Count == 0) {
            Log.Info(func, "saout has no identified text");
            return null;
        }

        pbaa = baa;
        pnaa = naa;
        return saout;
    }

    // leptonica: showExtractNumbers (recogident.c)
    /// <summary>Renders the identified numbers with their text and scores, as a debugging aid for digit identification.</summary>
    /// <param name="pixs">The input 1 bpp image.</param>
    /// <param name="sa">The recognized text strings.</param>
    /// <param name="baa">The boxes for the location of the characters in each string.</param>
    /// <param name="naa">The scores of the characters in each string.</param>
    /// <returns>An array of images of the identified strings with their text and scores.</returns>
    /// <remarks>
    /// Typical use: identify with <see cref="IdentifyMultiple(Pix, int, int, out Boxa?, out Pixa?, int)"/>, extract the numbers with
    /// <see cref="ExtractNumbers"/>, then pass the results to this method.
    /// </remarks>
    public static Pixa ShowExtractNumbers(Pix pixs, Sarray sa, Boxaa baa, Numaa naa)
    {
        return ShowExtractNumbersCore(pixs, sa, baa, naa, false, out _);
    }

    /// <summary>Renders the identified numbers with their text and scores, and also returns the input with the identified characters outlined.</summary>
    /// <param name="pixs">The input 1 bpp image.</param>
    /// <param name="sa">The recognized text strings.</param>
    /// <param name="baa">The boxes for the location of the characters in each string.</param>
    /// <param name="naa">The scores of the characters in each string.</param>
    /// <param name="ppixdb">Receives the input image with the identified characters outlined.</param>
    /// <returns>An array of images of the identified strings with their text and scores.</returns>
    public static Pixa ShowExtractNumbers(Pix pixs, Sarray sa, Boxaa baa, Numaa naa,
                                          out Pix? ppixdb)
    {
        return ShowExtractNumbersCore(pixs, sa, baa, naa, true, out ppixdb);
    }

    private static Pixa ShowExtractNumbersCore(Pix pixs, Sarray sa, Boxaa baa, Numaa naa,
                                               bool wantdb, out Pix? ppixdb)
    {
        const string func = "showExtractNumbers";
        ppixdb = null;
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        if (baa == null)
            throw Lept.Arg(func, "baa not defined");
        if (naa == null)
            throw Lept.Arg(func, "naa not defined");

        int n = sa.Count;
        Pixa pixa = Pixa.Create(n);
        L_Bmf bmf = L_Bmf.Create(null, 6);
        if (wantdb) ppixdb = pixs.ConvertTo8(true);
        for (int i = 0; i < n; i++) {
            string textstr = sa.GetString(i, L.L_NOCOPY);
            Boxa ba = baa.GetBoxa(i, L.L_CLONE);
            Numa na = naa.GetNuma(i, L.L_CLONE);
            ba.GetExtent(out _, out _, out Box box1);
            Box box2 = Box.AdjustSides(null, box1, -5, 5, -5, 5);
            if (wantdb) ppixdb!.RenderBoxArb(box2, 3, 255, 0, 0);
            Pix pix1 = pixs.ClipRectangle(box1, out _)!;
            int len = textstr.Length + 1;
            Pix pix2 = pix1.AddBlackOrWhiteBorder(14 * len, 14 * len,
                                                  5, 3, L.L_SET_WHITE);
            Pix pix3 = pix2.ConvertTo8(true);
            int nchar = na.Count;
            StringBuilder scoreSb = new StringBuilder();
            for (int j = 0; j < nchar; j++) {
                float score = na.GetFValue(j);
                scoreSb.Append(((int)(100 * score)).ToString(CultureInfo.InvariantCulture));
                if (j < nchar - 1) scoreSb.Append(',');
            }
            string buf = $"{textstr}: {scoreSb}\n";
            Pix pix4 = pix3.AddTextlines(bmf, buf, 0xff000000u, L.L_ADD_BELOW);
            pixa.AddPix(pix4, L.L_INSERT);
        }

        return pixa;
    }

    /*------------------------------------------------------------------------*
     *                        Static debug helper                             *
     *------------------------------------------------------------------------*/
    // leptonica: l_showIndicatorSplitValues (recogident.c)
    private static void ShowIndicatorSplitValues(Numa na1, Numa na2, Numa na3,
                                                 Numa na4, Numa na5, Numa na6)
    {
        int n = na1.Count;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("================================================");
        AppendRow(sb, "lt minw:    ", na1, n);
        AppendRow(sb, "lt minh:    ", na2, n);
        AppendRow(sb, "gt maxh:    ", na3, n);
        AppendRow(sb, "gt maxasp:  ", na4, n);
        AppendRow(sb, "lt minaf:   ", na5, n);
        sb.AppendLine("------------------------------------------------");
        AppendRow(sb, "result:     ", na6, n);
        sb.Append("================================================");
        Log.Info("l_showIndicatorSplitValues", sb.ToString());
    }

    private static void AppendRow(StringBuilder sb, string label, Numa na, int n)
    {
        sb.Append(label);
        for (int i = 0; i < n; i++)
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0,4} ", na.GetIValue(i));
        sb.AppendLine();
    }
}
