using System;

namespace Leptonica;

/*
 *  classapp.c: High-level jb2 classification, word/character box extraction,
 *              and word-box page comparison.
 *
 *      Top-level jb2 correlation and rank-hausdorff
 *         JbClass.Correlation            (jbCorrelation)
 *         JbClass.RankHaus               (jbRankHaus)
 *
 *      Extract and classify words in textline order
 *         JbClass.WordsInTextlines       (jbWordsInTextlines)
 *         Pix.GetWordsInTextlines        (pixGetWordsInTextlines)
 *         Pix.GetWordBoxesInTextlines    (pixGetWordBoxesInTextlines)
 *
 *      Extract word and character bounding boxes
 *         Pix.FindWordAndCharacterBoxes  (pixFindWordAndCharacterBoxes)
 *
 *      Use word bounding boxes to compare page images
 *         Boxa.ExtractSortedPattern      (boxaExtractSortedPattern)
 *         Numaa.CompareImagesByBoxes     (numaaCompareImagesByBoxes)
 *         static TestLineAlignmentX      (testLineAlignmentX)
 *         static CountAlignedMatches     (countAlignedMatches)
 *         static PrintRowIndices         (printRowIndices)
 */

/// <summary>Application-level functions built on the JBIG2-style classifier, such as word-box comparison between page images.</summary>
public static class JbClass
{
    private const int L_BUF_SIZE = 512;          /* size of filename buffer */
    private const int JB_WORDS_MIN_WIDTH = 5;    /* min. word width in pixels */
    private const int JB_WORDS_MIN_HEIGHT = 3;   /* min. word height in pixels */

    /*------------------------------------------------------------------*
     *          Top-level jb2 correlation and rank-hausdorff            *
     *------------------------------------------------------------------*/
    // leptonica: jbCorrelation (classapp.c)
    /// <summary>Classifies components of 1 bpp pages by correlation and writes jb2 data.</summary>
    /// <param name="dirin">directory of input images</param>
    /// <param name="thresh">typically ~0.8</param>
    /// <param name="weight">typically ~0.6</param>
    /// <param name="components">JB_CONN_COMPS, JB_CHARACTERS, JB_WORDS</param>
    /// <param name="rootname">for output files</param>
    /// <param name="firstpage">0-based</param>
    /// <param name="npages">use 0 for all pages in dirin</param>
    /// <param name="renderflag">1 to render from templates; 0 to skip</param>
    /// <remarks>
    /// (1) The images must be 1 bpp.  If they are not, you can convert
    ///     them using convertFilesTo1bpp().
    /// (2) See prog/jbcorrelation for generating more output (e.g.,
    ///     for debugging)
    /// </remarks>
    public static void Correlation(string dirin, float thresh, float weight, int components,
                                   string rootname, int firstpage, int npages, int renderflag)
    {
        const string func = "jbCorrelation";
        int nfiles, i, numpages;
        JbData data;
        JbClasser classer;
        Pix pix;
        Pixa pixa;
        Sarray safiles;

        if (dirin == null)
            throw Lept.Arg(func, "dirin not defined");
        if (rootname == null)
            throw Lept.Arg(func, "rootname not defined");
        if (components != L.JB_CONN_COMPS && components != L.JB_CHARACTERS &&
            components != L.JB_WORDS)
            throw Lept.Arg(func, "components invalid");

        safiles = Sarray.GetSortedPathnamesInDirectory(dirin, null, firstpage, npages);
        nfiles = safiles.Count;

            /* Classify components */
        classer = JbClasser.CorrelationInit(components, 0, 0, thresh, weight);
        classer.AddPages(safiles);

            /* Save data */
        data = classer.DataSave();
        data.Write(rootname);

            /* Optionally, render pages using class templates */
        if (renderflag != 0) {
            pixa = data.Render(false);
            numpages = pixa.Count;
            if (numpages != nfiles)
                Utils.LeptStderr("numpages = %d, nfiles = %d, not equal!\n",
                                 numpages, nfiles);
            for (i = 0; i < numpages; i++) {
                pix = pixa.GetPix(i, L.L_CLONE);
                string filename = CFormat.Sprintf("%s.%04d", rootname, i);
                if (filename.Length > L_BUF_SIZE - 1)
                    filename = filename.Substring(0, L_BUF_SIZE - 1);
                Utils.LeptStderr("filename: %s\n", filename);
                Pix.Write(filename, pix, L.IFF_PNG);
            }
        }
    }

    // leptonica: jbRankHaus (classapp.c)
    /// <summary>Classifies components of 1 bpp pages by rank Hausdorff and writes jb2 data.</summary>
    /// <param name="dirin">directory of input images</param>
    /// <param name="size">of Sel used for dilation; typ. 2</param>
    /// <param name="rank">rank value of match; typ. 0.97</param>
    /// <param name="components">JB_CONN_COMPS, JB_CHARACTERS, JB_WORDS</param>
    /// <param name="rootname">for output files</param>
    /// <param name="firstpage">0-based</param>
    /// <param name="npages">use 0 for all pages in dirin</param>
    /// <param name="renderflag">1 to render from templates; 0 to skip</param>
    /// <remarks>
    /// (1) See prog/jbrankhaus for generating more output (e.g.,
    ///     for debugging)
    /// </remarks>
    public static void RankHaus(string dirin, int size, float rank, int components,
                                string rootname, int firstpage, int npages, int renderflag)
    {
        const string func = "jbRankHaus";
        int nfiles, i, numpages;
        JbData data;
        JbClasser classer;
        Pix pix;
        Pixa pixa;
        Sarray safiles;

        if (dirin == null)
            throw Lept.Arg(func, "dirin not defined");
        if (rootname == null)
            throw Lept.Arg(func, "rootname not defined");
        if (components != L.JB_CONN_COMPS && components != L.JB_CHARACTERS &&
            components != L.JB_WORDS)
            throw Lept.Arg(func, "components invalid");

        safiles = Sarray.GetSortedPathnamesInDirectory(dirin, null, firstpage, npages);
        nfiles = safiles.Count;

            /* Classify components */
        classer = JbClasser.RankHausInit(components, 0, 0, size, rank);
        classer.AddPages(safiles);

            /* Save data */
        data = classer.DataSave();
        data.Write(rootname);

            /* Optionally, render pages using class templates */
        if (renderflag != 0) {
            pixa = data.Render(false);
            numpages = pixa.Count;
            if (numpages != nfiles)
                Utils.LeptStderr("numpages = %d, nfiles = %d, not equal!\n",
                                 numpages, nfiles);
            for (i = 0; i < numpages; i++) {
                pix = pixa.GetPix(i, L.L_CLONE);
                string filename = CFormat.Sprintf("%s.%04d", rootname, i);
                if (filename.Length > L_BUF_SIZE - 1)
                    filename = filename.Substring(0, L_BUF_SIZE - 1);
                Utils.LeptStderr("filename: %s\n", filename);
                Pix.Write(filename, pix, L.IFF_PNG);
            }
        }
    }

    /*------------------------------------------------------------------*
     *           Extract and classify words in textline order           *
     *------------------------------------------------------------------*/
    // leptonica: jbWordsInTextlines (classapp.c)
    /// <summary>Extracts and classifies words in textline order across pages in a directory.</summary>
    /// <param name="dirin">directory of input pages</param>
    /// <param name="reduction">1 for full res; 2 for half-res</param>
    /// <param name="maxwidth">of word mask components, to be kept</param>
    /// <param name="maxheight">of word mask components, to be kept</param>
    /// <param name="thresh">on correlation; 0.80 is reasonable</param>
    /// <param name="weight">for handling thick text; 0.6 is reasonable</param>
    /// <param name="natl">numa with textline index for each component</param>
    /// <param name="firstpage">0-based</param>
    /// <param name="npages">use 0 for all pages in dirin</param>
    /// <returns>classer for the set of pages</returns>
    /// <remarks>
    /// (1) This is a high-level function.  See prog/jbwords for example
    ///     of usage.
    /// (2) Typically, use input of 75 - 150 ppi for finding words.
    /// </remarks>
    public static JbClasser WordsInTextlines(string dirin, int reduction, int maxwidth,
                                             int maxheight, float thresh, float weight,
                                             out Numa natl, int firstpage, int npages)
    {
        const string func = "jbWordsInTextlines";
        string fname;
        int nfiles, i, w, h;
        Boxa boxa;
        JbClasser classer;
        Numa nai;
        Pix pix1, pix2;
        Pixa pixa;
        Sarray safiles;

        natl = null!;
        if (dirin == null)
            throw Lept.Arg(func, "dirin not defined");
        if (reduction != 1 && reduction != 2)
            throw Lept.Arg(func, "reduction not in {1,2}");

        safiles = Sarray.GetSortedPathnamesInDirectory(dirin, null, firstpage, npages);
        nfiles = safiles.Count;

            /* Classify components */
        classer = JbClasser.CorrelationInit(L.JB_WORDS, maxwidth, maxheight, thresh, weight);
        classer.Safiles = safiles.Copy();
        natl = Numa.Create(0);
        for (i = 0; i < nfiles; i++) {
            fname = safiles.GetString(i, L.L_NOCOPY);
            try {
                pix1 = Pix.Read(fname);
            } catch (LeptonicaException) {
                Log.Warning(func, $"image file {i} not read\n");
                continue;
            }
            if (reduction == 1)
                pix2 = Pix.Clone(pix1);
            else  /* reduction == 2 */
                pix2 = pix1.ReduceRankBinaryCascade(1, 0, 0, 0);
            pix2.GetWordsInTextlines(JB_WORDS_MIN_WIDTH,
                                     JB_WORDS_MIN_HEIGHT, maxwidth, maxheight,
                                     out boxa, out pixa, out nai);
            pix2.GetDimensions(out w, out h, out _);
            classer.W = w;
            classer.H = h;
            classer.AddPageComponents(pix2, boxa, pixa);
            natl.JoinIgnoringError(nai);
        }

        return classer;
    }
}

public sealed partial class Pix
{
    // leptonica: pixGetWordsInTextlines (classapp.c)
    /// <summary>Extracts word boxes and word images sorted in textline order.</summary>
    /// <param name="minwidth">of saved components; smaller are discarded</param>
    /// <param name="minheight">of saved components; smaller are discarded</param>
    /// <param name="maxwidth">of saved components; larger are discarded</param>
    /// <param name="maxheight">of saved components; larger are discarded</param>
    /// <param name="boxad">word boxes sorted in textline line order</param>
    /// <param name="pixad">word images sorted in textline line order</param>
    /// <param name="nai">index of textline for each word</param>
    /// <remarks>
    /// (1) The input should be at a resolution of between 75 and 150 ppi.
    /// (2) The four size constraints on saved components are all
    ///     scaled by reduction.
    /// (3) The result are word images (and their b.b.), extracted in
    ///     textline order, at either full res or 2x reduction,
    ///     and with a numa giving the textline index for each word.
    /// (4) The pixa and boxa interfaces should make this type of
    ///     application simple to put together.  The steps are:
    ///      ~ generate first estimate of word masks
    ///      ~ get b.b. of these, and remove the small and big ones
    ///      ~ extract pixa of the word images, using the b.b.
    ///      ~ sort actual word images in textline order (2d)
    ///      ~ flatten them to a pixa (1d), saving the textline index
    ///        for each pix
    /// </remarks>
    public void GetWordsInTextlines(int minwidth, int minheight, int maxwidth, int maxheight,
                                    out Boxa boxad, out Pixa pixad, out Numa nai)
    {
        Boxa boxa1;
        Boxaa baa;
        Numaa naa;
        Pixa pixa1;
        Pixaa paa;

            /* Get the bounding boxes of the words from the word mask. */
        WordBoxesByDilation(minwidth, minheight, maxwidth, maxheight,
                            out boxa1, out _, null);

            /* Generate a pixa of the word images */
        pixa1 = Pixa.CreateFromBoxa(this, boxa1, 0, 0, out _);

            /* Sort the bounding boxes of these words by line.  We use the
             * index mapping to allow identical sorting of the pixa. */
        baa = boxa1.Sort2d(out naa, -1, -1, 4);
        _ = baa;
        paa = pixa1.Sort2dByIndex(naa, L.L_CLONE);

            /* Flatten the word paa */
        pixad = paa.FlattenToPixa(out nai, L.L_CLONE);
        boxad = pixad.GetBoxa(L.L_COPY);
    }

    // leptonica: pixGetWordBoxesInTextlines (classapp.c)
    /// <summary>Extracts word boxes sorted in textline order.</summary>
    /// <param name="minwidth">of saved components; smaller are discarded</param>
    /// <param name="minheight">of saved components; smaller are discarded</param>
    /// <param name="maxwidth">of saved components; larger are discarded</param>
    /// <param name="maxheight">of saved components; larger are discarded</param>
    /// <param name="boxad">word boxes sorted in textline line order</param>
    /// <param name="nai">[optional] index of textline for each word</param>
    /// <remarks>
    /// (1) The input should be at a resolution of between 75 and 150 ppi.
    /// (2) This is a special version of pixGetWordsInTextlines(), that
    ///     just finds the word boxes in line order, with a numa
    ///     giving the textline index for each word.
    ///     See pixGetWordsInTextlines() for more details.
    /// </remarks>
    public void GetWordBoxesInTextlines(int minwidth, int minheight, int maxwidth, int maxheight,
                                        out Boxa boxad, out Numa nai)
    {
        Boxa boxa1;
        Boxaa baa;

            /* Get the bounding boxes of the words from the word mask. */
        WordBoxesByDilation(minwidth, minheight, maxwidth, maxheight,
                            out boxa1, out _, null);

            /* 2D sort the bounding boxes of these words. */
        baa = boxa1.Sort2d(out _, 3, -5, 5);

            /* Flatten the boxaa, saving the boxa index for each box */
        boxad = baa.FlattenToBoxa(out nai, L.L_CLONE);
    }

    /*------------------------------------------------------------------*
     *             Extract word and character bounding boxes            *
     *------------------------------------------------------------------*/
    // leptonica: pixFindWordAndCharacterBoxes (classapp.c)
    /// <summary>Finds word bounding boxes and character bounding boxes within each word.</summary>
    /// <param name="boxs">[optional] region to select in pixs</param>
    /// <param name="thresh">binarization threshold (typ. 100 - 150)</param>
    /// <param name="boxaw">return the word boxes</param>
    /// <param name="boxaac">return the character boxes</param>
    /// <param name="debugdir">[optional] for debug images; use null to skip</param>
    /// <remarks>
    /// (1) If boxs == null, the entire input image is used.
    /// (2) Having an input pix that is not 1bpp is necessary to reduce
    ///     touching characters by using a low binarization threshold.
    ///     Suggested thresholds are between 100 and 150.
    /// (3) The coordinates in the output boxes are global, with respect
    ///     to the input image.
    /// </remarks>
    public void FindWordAndCharacterBoxes(Box? boxs, int thresh, out Boxa boxaw, out Boxaa boxaac,
                                          string? debugdir)
    {
        const string func = "pixFindWordAndCharacterBoxes";
        string debugfile, subdir;
        int i, xs, ys, xb, yb, nb, loc;
        float scalefact;
        Box box1, box2;
        Boxa boxa1, boxa1a, boxa2, boxa3, boxa4, boxa5;
        Pix? pix1, pix4;
        Pix pix2, pix3, pix3a, pix5;

        boxaw = null!;
        boxaac = null!;
        if (Depth == 1)
            throw Lept.Arg(func, "pixs not defined or 1 bpp");
        if (thresh > 150)
            Log.Warning(func, $"threshold is {thresh}; may be too high\n");

        if (boxs != null) {
            if ((pix1 = ClipRectangle(boxs, out _)) == null)
                throw Lept.Fail(func, "pix1 not made");
            boxs.GetGeometry(out xs, out ys, out _, out _);
        } else {
            pix1 = Clone(this);
            xs = ys = 0;
        }

            /* Convert pix1 to 8 bpp gray if necessary */
        pix2 = pix1.ConvertTo8(false);

            /* To find the words and letters, work with 1 bpp images and use
             * a low threshold to reduce the number of touching characters. */
        pix3 = pix2.ConvertTo1(thresh);

            /* Work at about 120 ppi to find the word bounding boxes. */
        pix3a = pix3.ScaleToResolution(120.0f, 300.0f, out scalefact);

            /* First find the words, removing the very small things like
             * dots over the 'i' that weren't included in word boxes. */
        pix3a.GetWordBoxesInTextlines(1, 4, 150, 40, out boxa1a, out _);
        boxa1 = boxa1a.Transform(0, 0, (float)(1.0 / scalefact), (float)(1.0 / scalefact));
        if (debugdir != null) {
            loc = 0;
            subdir = Utils.StringReplaceSubstr(debugdir, "/tmp/", "", ref loc, out _);
            Utils.LeptMkdir(subdir);
            pix4 = pix2.ConvertTo32();
            pix4.RenderBoxaArb(boxa1, 2, 255, 0, 0);
            debugfile = Utils.StringJoin(debugdir, "/words.png");
            Write(debugfile, pix4, L.IFF_PNG);
        }

            /* Now find the letters at 300 ppi */
        nb = boxa1.Count;
        boxaw = Boxa.Create(nb);
        boxaac = Boxaa.Create(nb);
        for (i = 0; i < nb; i++) {
            box1 = boxa1.GetBox(i, L.L_COPY);
            box1.GetGeometry(out xb, out yb, out _, out _);
            pix4 = pix3.ClipRectangle(box1, out _)!;
                /* Join detached parts of characters vertically */
            pix5 = pix4.MorphSequence("c1.10", 0);
                /* The connected components should mostly be characters */
            boxa2 = pix5.ConnCompBB(4);
                /* Remove very small pieces */
            boxa3 = boxa2.SelectBySize(2, 5, L.L_SELECT_IF_BOTH,
                                       L.L_SELECT_IF_GTE, out _);
                /* Order left to right */
            boxa4 = boxa3.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out _);
                /* Express locations with reference to the full input image */
            boxa5 = boxa4.Transform(xs + xb, ys + yb, 1.0f, 1.0f);
            box2 = box1.Transform(xs, ys, 1.0f, 1.0f);

                /* Ignore any boxa with no boxes after size filtering */
            if (boxa5.Count > 0) {
                boxaw.AddBox(box2, L.L_INSERT);
                boxaac.AddBoxa(boxa5, L.L_INSERT);
            }
        }
        if (debugdir != null) {
            pix4 = ConvertTo32();
            boxa2 = boxaac.FlattenToBoxa(out _, L.L_COPY);
            pix4.RenderBoxaArb(boxa2, 2, 255, 0, 0);
            boxa3 = boxaw.AdjustSides(-2, 2, -2, 2);
            pix4.RenderBoxaArb(boxa3, 2, 0, 255, 0);
            debugfile = Utils.StringJoin(debugdir, "/chars.png");
            Write(debugfile, pix4, L.IFF_PNG);
        }
    }
}

public sealed partial class Boxa
{
    /*------------------------------------------------------------------*
     *           Use word bounding boxes to compare page images         *
     *------------------------------------------------------------------*/
    // leptonica: boxaExtractSortedPattern (classapp.c)
    /// <summary>Extracts a per-textline coordinate pattern from sorted word bounding boxes.</summary>
    /// <param name="na">index of textline for each box in boxa</param>
    /// <returns>naa NUMAA, where each numa represents one textline</returns>
    /// <remarks>
    /// (1) The input is expected to come from pixGetWordBoxesInTextlines().
    /// (2) Each numa in the output consists of an average y coordinate
    ///     of the first box in the textline, followed by pairs of
    ///     x coordinates representing the left and right edges of each
    ///     of the boxes in the textline.
    /// </remarks>
    public Numaa ExtractSortedPattern(Numa na)
    {
        const string func = "boxaExtractSortedPattern";
        int index, nbox, row, prevrow, x, y, w, h;
        Box box;
        Numa? nad = null;
        Numaa naa;

        if (na == null)
            throw Lept.Arg(func, "na not defined");

        naa = Numaa.Create(0);
        nbox = Count;
        if (nbox == 0)
            return naa;

        prevrow = -1;
        for (index = 0; index < nbox; index++) {
            box = GetBox(index, L.L_CLONE);
            row = na.GetIValue(index);
            if (row > prevrow) {
                if (index > 0 && nad != null)
                    naa.AddNuma(nad, L.L_INSERT);
                nad = Numa.Create(0);
                prevrow = row;
                box.GetGeometry(out _, out y, out _, out h);
                nad.AddNumber(y + h / 2);
            }
            box.GetGeometry(out x, out _, out w, out _);
            if (nad != null) {
                nad.AddNumber(x);
                nad.AddNumber(x + w - 1);
            }
        }
        if (nad != null)
            naa.AddNuma(nad, L.L_INSERT);

        return naa;
    }
}

public sealed partial class Numaa
{
    // leptonica: numaaCompareImagesByBoxes (classapp.c)
    /// <summary>Compares two page images by aligning their sorted word-box patterns.</summary>
    /// <param name="naa2">for image 2, formatted by boxaExtractSortedPattern()</param>
    /// <param name="nperline">number of box regions to be used in each textline</param>
    /// <param name="nreq">number of complete row matches required</param>
    /// <param name="maxshiftx">max allowed x shift between two patterns, in pixels</param>
    /// <param name="maxshifty">max allowed y shift between two patterns, in pixels</param>
    /// <param name="delx">max allowed difference in x data, after alignment</param>
    /// <param name="dely">max allowed difference in y data, after alignment</param>
    /// <param name="debugflag">1 for debug output</param>
    /// <returns>true if nreq row matches are found; false otherwise</returns>
    /// <remarks>
    /// (1) Each input numaa describes a set of sorted bounding boxes
    ///     (sorted by textline and, within each textline, from
    ///     left to right) in the images from which they are derived.
    ///     See boxaExtractSortedPattern() for a description of the data
    ///     format in each of the input numaa.
    /// (2) This function does an alignment between the input
    ///     descriptions of bounding boxes for two images. The
    ///     input parameter nperline specifies the number of boxes
    ///     to consider in each line when testing for a match, and
    ///     nreq is the required number of lines that must be well-aligned
    ///     to get a match.
    /// (3) Testing by alignment has 3 steps:
    ///     (a) Generating the location of word bounding boxes from the
    ///         images (prior to calling this function).
    ///     (b) Listing all possible pairs of aligned rows, based on
    ///         tolerances in horizontal and vertical positions of
    ///         the boxes.  Specifically, all pairs of rows are enumerated
    ///         whose first nperline boxes can be brought into close
    ///         alignment, based on the delx parameter for boxes in the
    ///         line and within the overall maxshiftx and maxshifty
    ///         constraints.
    ///     (c) Each pair, starting with the first, is used to search
    ///         for a set of nreq - 1 other pairs that can all be aligned
    ///         with a difference in global translation of not more
    ///         than (delx, dely).
    /// </remarks>
    public bool CompareImagesByBoxes(Numaa naa2, int nperline, int nreq,
                                     int maxshiftx, int maxshifty,
                                     int delx, int dely, int debugflag)
    {
        const string func = "numaaCompareImagesByBoxes";
        int n1, n2, i, j, nbox, y1, y2, xl1, xl2;
        int shiftx, shifty;
        bool match, same;
        int[] line1, line2;    /* indicator for sufficient boxes in a line */
        int[] yloc1, yloc2;    /* arrays of y value for first box in a line */
        int[] xleft1, xleft2;  /* arrays of x value for left side of first box */
        Numa na1, na2, nai1, nai2, nasx, nasy;

        if (naa2 == null)
            throw Lept.Arg(func, "naa2 not defined");
        if (nperline < 1)
            throw Lept.Arg(func, "nperline < 1");
        if (nreq < 1)
            throw Lept.Arg(func, "nreq < 1");

        n1 = Count;
        n2 = naa2.Count;
        if (n1 < nreq || n2 < nreq)
            return false;

            /* Find the lines in naa1 and naa2 with sufficient boxes.
             * Also, find the y-values for each of the lines, and the
             * LH x-values of the first box in each line. */
        line1 = new int[n1];
        line2 = new int[n2];
        yloc1 = new int[n1];
        yloc2 = new int[n2];
        xleft1 = new int[n1];
        xleft2 = new int[n2];
        for (i = 0; i < n1; i++) {
            na1 = GetNuma(i, L.L_CLONE);
            if (na1.Count > 0) yloc1[i] = na1.GetIValue(0);
            if (na1.Count > 1) xleft1[i] = na1.GetIValue(1);
            nbox = (na1.Count - 1) / 2;
            if (nbox >= nperline)
                line1[i] = 1;
        }
        for (i = 0; i < n2; i++) {
            na2 = naa2.GetNuma(i, L.L_CLONE);
            if (na2.Count > 0) yloc2[i] = na2.GetIValue(0);
            if (na2.Count > 1) xleft2[i] = na2.GetIValue(1);
            nbox = (na2.Count - 1) / 2;
            if (nbox >= nperline)
                line2[i] = 1;
        }

            /* Enumerate all possible line matches.  A 'possible' line
             * match is one where the x and y shifts for the first box
             * in each line are within the maxshiftx and maxshifty
             * constraints, and the left and right sides of the remaining
             * (nperline - 1) successive boxes are within delx of each other.
             * The result is a set of four numas giving parameters of
             * each set of matching lines. */
        nai1 = Numa.Create(0);  /* line index 1 of match */
        nai2 = Numa.Create(0);  /* line index 2 of match */
        nasx = Numa.Create(0);  /* shiftx for match */
        nasy = Numa.Create(0);  /* shifty for match */
        for (i = 0; i < n1; i++) {
            if (line1[i] == 0) continue;
            y1 = yloc1[i];
            xl1 = xleft1[i];
            na1 = GetNuma(i, L.L_CLONE);
            for (j = 0; j < n2; j++) {
                if (line2[j] == 0) continue;
                y2 = yloc2[j];
                if (Math.Abs(y1 - y2) > maxshifty) continue;
                xl2 = xleft2[j];
                if (Math.Abs(xl1 - xl2) > maxshiftx) continue;
                shiftx = xl1 - xl2;  /* shift to add to x2 values */
                shifty = y1 - y2;  /* shift to add to y2 values */
                na2 = naa2.GetNuma(j, L.L_CLONE);

                    /* Now check if 'nperline' boxes in the two lines match */
                match = TestLineAlignmentX(na1, na2, shiftx, delx, nperline);
                if (match) {
                    nai1.AddNumber(i);
                    nai2.AddNumber(j);
                    nasx.AddNumber(shiftx);
                    nasy.AddNumber(shifty);
                }
            }
        }

            /* Determine if there are a sufficient number of mutually
             * aligned matches.  Mutually aligned matches place an additional
             * constraint on the 'possible' matches, where the relative
             * shifts must not exceed the (delx, dely) distances. */
        CountAlignedMatches(nai1, nai2, nasx, nasy, n1, n2, delx, dely,
                            nreq, out same, debugflag);

        return same;
    }

    // leptonica: testLineAlignmentX (classapp.c)
    private static bool TestLineAlignmentX(Numa na1, Numa na2, int shiftx, int delx, int nperline)
    {
        int i, xl1, xr1, xl2, xr2, diffl, diffr;

        for (i = 0; i < nperline; i++) {
            // NOTE(upstream): reads (i + 1, i + 2) instead of (2 * i + 1, 2 * i + 2);
            // see docs/UPSTREAM_NOTES.md.
            xl1 = na1.GetIValue(i + 1);
            xr1 = na1.GetIValue(i + 2);
            xl2 = na2.GetIValue(i + 1);
            xr2 = na2.GetIValue(i + 2);
            diffl = Math.Abs(xl1 - xl2 - shiftx);
            diffr = Math.Abs(xr1 - xr2 - shiftx);
            if (diffl > delx || diffr > delx)
                return false;
        }

        return true;
    }

    // leptonica: countAlignedMatches (classapp.c)
    private static void CountAlignedMatches(Numa nai1, Numa nai2, Numa nasx, Numa nasy,
                                            int n1, int n2, int delx, int dely, int nreq,
                                            out bool same, int debugflag)
    {
        int i, j, nm, shiftx, shifty, nmatch, diffx, diffy;
        int[] ia1, ia2, iasx, iasy, index1, index2;

        same = false;

            /* Check for sufficient aligned matches, doing a double iteration
             * over the set of raw matches.  The row index arrays
             * are used to verify that the same rows in either image
             * are not used in more than one match.  Whenever there
             * is a match that is properly aligned, those rows are
             * marked in the index arrays.  */
        nm = nai1.Count;  /* number of matches */
        if (nm < nreq)
            return;

        ia1 = nai1.GetIArray();
        ia2 = nai2.GetIArray();
        iasx = nasx.GetIArray();
        iasy = nasy.GetIArray();
        index1 = new int[n1];  /* watch rows */
        index2 = new int[n2];
        for (i = 0; i < nm; i++) {
            if (same)
                break;

                /* Reset row index arrays */
            Array.Clear(index1, 0, n1);
            Array.Clear(index2, 0, n2);
            nmatch = 1;
            index1[ia1[i]] = nmatch;  /* mark these rows as taken */
            index2[ia2[i]] = nmatch;
            shiftx = iasx[i];  /* reference shift between two rows */
            shifty = iasy[i];  /* ditto */
            if (nreq == 1) {
                same = true;
                break;
            }
            for (j = 0; j < nm; j++) {
                if (j == i) continue;
                    /* Rows must both be different from any previously seen */
                if (index1[ia1[j]] > 0 || index2[ia2[j]] > 0) continue;
                    /* Check the shift for this match */
                diffx = Math.Abs(shiftx - iasx[j]);
                diffy = Math.Abs(shifty - iasy[j]);
                if (diffx > delx || diffy > dely) continue;
                    /* We have a match */
                nmatch++;
                index1[ia1[j]] = nmatch;  /* mark the rows */
                index2[ia2[j]] = nmatch;
                if (nmatch >= nreq) {
                    same = true;
                    if (debugflag != 0)
                        PrintRowIndices(index1, n1, index2, n2);
                    break;
                }
            }
        }
    }

    // leptonica: printRowIndices (classapp.c)
    private static void PrintRowIndices(int[] index1, int n1, int[] index2, int n2)
    {
        int i;

        Utils.LeptStderr("Index1: ");
        for (i = 0; i < n1; i++) {
            if (i != 0 && (i % 20 == 0))
                Utils.LeptStderr("\n        ");
            Utils.LeptStderr("%3d", index1[i]);
        }
        Utils.LeptStderr("\n");

        Utils.LeptStderr("Index2: ");
        for (i = 0; i < n2; i++) {
            if (i != 0 && (i % 20 == 0))
                Utils.LeptStderr("\n        ");
            Utils.LeptStderr("%3d", index2[i]);
        }
        Utils.LeptStderr("\n");
    }
}
